// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenUsd.Rendering.Silk;

// Isolated publication candidate. No production material selector calls this.
// Optional captured material values use the existing RGBA8 preprocessing rules.
// Mutation, binding and retirement attempts reject concurrent/reentrant calls.
// Callers retain the cache and retry cleanup after an error, even after Dispose.
internal sealed class SilkPtexSlotGpuCandidateCache : IDisposable
{
    private readonly ISilkGraphicsDevice _device;
    private readonly SilkPtexSlotCandidateCache _cpu = new();
    private Publication? _current;
    private PreparedUpdate? _pending;
    private bool _disposed;
    private readonly List<SilkPtexCleanupOwner> _cleanup = new(64);
    private int _busy;

    private Operation Enter()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "Ptex candidate cache operations must be serialized and cannot reenter.");
        }
        return new Operation(this);
    }

    private readonly struct Operation(SilkPtexSlotGpuCandidateCache owner) : IDisposable
    {
        public void Dispose() => Volatile.Write(ref owner._busy, 0);
    }

    internal void RetryCleanup()
    {
        using Operation operation = Enter();
        DrainCleanup();
    }

    private void DrainCleanup()
    {
        for (int index = 0; index < _cleanup.Count;)
        {
            if (_cleanup[index].Drain())
            {
                _cleanup.RemoveAt(index);
            }
            else
            {
                index++;
            }
        }
    }

    internal SilkPtexSlotGpuCandidateCache(ISilkGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
    }

    internal PreparedUpdate Prepare(
        SilkMeshData mesh,
        SilkMeshGeometry ordinaryGeometry,
        IReadOnlyList<SilkPtexSlotSource> sources,
        uint maximumDimension,
        SilkCpuTextureBudget budget,
        ulong retryRevision,
        IReadOnlyList<SilkMaterialTexture>? materials = null,
        SilkPtexCornerCertificate? cornerCertificate = null)
    {
        using Operation operation = Enter();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(sources);
        if (_pending is not null)
        {
            throw new InvalidOperationException("A Ptex GPU candidate publication is already pending.");
        }
        DrainCleanup();
        if (_cleanup.Count == 64)
        {
            throw new InvalidOperationException("Ptex candidate retirement capacity is exhausted.");
        }
        if (_device is not ISilkBufferAdmissionDevice { GpuBufferBudget: not null } ||
            _device is not ISilkTextureAdmissionDevice { GpuTextureBudget: not null } ||
            _device is not ISilkStagingAdmissionDevice { GpuStagingBudget: not null } ||
            _device is not ISilkCpuTextureAdmissionDevice cpuAdmission ||
            !ReferenceEquals(cpuAdmission.CpuTextureBudget, budget))
        {
            throw new InvalidOperationException(
                "Ptex GPU candidates require configured buffer, texture, staging and shared CPU texture admission.");
        }
        if (sources.Count is < 1 or > 16)
        {
            throw new InvalidDataException("A Ptex GPU candidate requires between one and sixteen slots.");
        }
        for (int slot = 0; slot < sources.Count; slot++)
        {
            (uint sampler, uint texture) =
                SilkBindingLayoutDescriptor.GetMaterialTextureBindings(sources[slot].Parameter);
            for (int earlier = 0; earlier < slot; earlier++)
            {
                (uint earlierSampler, uint earlierTexture) =
                    SilkBindingLayoutDescriptor.GetMaterialTextureBindings(sources[earlier].Parameter);
                if (sampler == earlierSampler || texture == earlierTexture)
                {
                    throw new InvalidDataException("Ptex candidate slots require independent material bindings.");
                }
            }
        }
        uint deviceLimit = _device.Capabilities.MaxTextureDimension2D;
        if (deviceLimit == 0 || maximumDimension > deviceLimit)
        {
            throw new InvalidOperationException("The Ptex candidate atlas limit must fit the reported device limit.");
        }
        using IDisposable? valuesReservation = materials is null ? null : budget.Reserve(checked(
            (ulong)sources.Count * (ulong)Unsafe.SizeOf<SilkPtexMaterialValue>()));
        SilkPtexMaterialValue[]? values = null;
        if (materials is not null)
        {
            if (materials.Count != sources.Count)
            {
                throw new ArgumentException("Each Ptex slot requires its own material value.", nameof(materials));
            }
            values = new SilkPtexMaterialValue[materials.Count];
            for (int slot = 0; slot < values.Length; slot++)
            {
                values[slot] = SilkSceneGpuResources.CapturePtexMaterialValue(materials[slot]);
                if (values[slot].Parameter != sources[slot].Parameter)
                {
                    throw new InvalidDataException("Ptex material values must follow the lookup slot order.");
                }
            }
        }
        SilkPtexSlotCandidateCache.PreparedUpdate cpu = _cpu.Prepare(
            mesh, ordinaryGeometry, sources, maximumDimension, budget, retryRevision, cornerCertificate);
        Publication? created = null;
        SilkPtexCleanupOwner? cleanup = null;
        try
        {
            Publication value;
            if (_current is { } current && current.Matches(cpu.Value, values))
            {
                value = current;
            }
            else
            {
                cleanup = SilkPtexCleanupOwner.Create(budget);
                _cleanup.Add(cleanup);
                value = created = Publication.Upload(this, _device, cpu.Value, budget, values, cleanup);
            }
            var update = new PreparedUpdate(this, cpu, _current, value);
            _pending = update;
            return update;
        }
        catch (Exception primary)
        {
            try
            {
                if (created is not null)
                {
                    created.Dispose();
                }
                else
                {
                    cleanup?.Retire();
                }
                DrainCleanup();
            }
            catch (Exception cleanupFailure)
            {
                try
                {
                    cpu.Dispose();
                }
                catch (Exception cpuFailure)
                {
                    throw new AggregateException(primary, cleanupFailure, cpuFailure);
                }
                throw new AggregateException(primary, cleanupFailure);
            }
            try
            {
                cpu.Dispose();
            }
            catch (Exception cpuFailure)
            {
                throw new AggregateException(primary, cpuFailure);
            }
            throw;
        }
    }

    internal Publication.Lease AcquireLease()
    {
        using Operation operation = Enter();
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (_current ?? throw new InvalidOperationException("No Ptex GPU candidate has been published."))
            .AcquireLease();
    }

    internal sealed class PreparedUpdate(
        SilkPtexSlotGpuCandidateCache owner,
        SilkPtexSlotCandidateCache.PreparedUpdate cpu,
        Publication? previous,
        Publication value) : IDisposable
    {
        private bool _finished;

        internal void Commit()
        {
            using Operation operation = owner.Enter();
            ObjectDisposedException.ThrowIf(owner._disposed, owner);
            if (_finished || !ReferenceEquals(owner._pending, this) ||
                !ReferenceEquals(owner._current, previous))
            {
                throw new InvalidOperationException("The Ptex GPU candidate publication is no longer pending.");
            }
            // All uploads have completed. Neither current owner changes on an upload failure.
            cpu.Commit();
            owner._current = value;
            owner._pending = null;
            _finished = true;
            cpu.Dispose();
            if (!ReferenceEquals(previous, value))
            {
                previous?.Dispose();
            }
        }

        public void Dispose()
        {
            using Operation operation = owner.Enter();
            Abandon();
        }

        internal void Abandon()
        {
            if (_finished)
            {
                owner.DrainCleanup();
                return;
            }
            _finished = true;
            if (ReferenceEquals(owner._pending, this))
            {
                owner._pending = null;
            }
            try
            {
                if (!ReferenceEquals(previous, value))
                {
                    value.Dispose();
                }
            }
            catch (Exception cleanupFailure)
            {
                try
                {
                    cpu.Dispose();
                }
                catch (Exception cpuFailure)
                {
                    throw new AggregateException(cleanupFailure, cpuFailure);
                }
                throw;
            }
            cpu.Dispose();
        }
    }

    internal sealed class Publication : IDisposable
    {
        private readonly object _gate = new();
        private readonly object _identity;
        private readonly SilkMaterialParameter[] _parameters;
        private readonly ISilkGraphicsTexture[] _textures;
        private readonly ISilkGraphicsBuffer _vertices;
        private readonly ISilkGraphicsBuffer _indices;
        private readonly ISilkGraphicsBuffer _lookup;
        private readonly SilkVertexLayoutDescriptor _vertexLayout;
        private readonly uint _indexCount;
        private readonly SilkPtexMaterialValue[]? _values;
        private readonly ISilkGraphicsSampler? _sampler;
        private int _leases;
        private bool _disposed;
        private readonly SilkPtexCleanupOwner _cleanup;
        private readonly SilkPtexSlotGpuCandidateCache _cache;

        private Publication(
            SilkPtexSlotCandidateCache.Candidate identity,
            SilkMaterialParameter[] parameters,
            ISilkGraphicsTexture[] textures,
            ISilkGraphicsBuffer vertices,
            ISilkGraphicsBuffer indices,
            ISilkGraphicsBuffer lookup,
            SilkVertexLayoutDescriptor vertexLayout,
            SilkPtexMaterialValue[]? values,
            ISilkGraphicsSampler? sampler,
            SilkPtexCleanupOwner cleanup,
            SilkPtexSlotGpuCandidateCache cache)
        {
            _identity = identity.PublicationIdentity;
            _parameters = parameters;
            _textures = textures;
            _vertices = vertices;
            _indices = indices;
            _lookup = lookup;
            _vertexLayout = vertexLayout;
            _indexCount = checked((uint)identity.Geometry.Indices.Length);
            _values = values;
            _sampler = sampler;
            _cleanup = cleanup;
            _cache = cache;
        }

        internal bool Matches(SilkPtexSlotCandidateCache.Candidate identity, SilkPtexMaterialValue[]? values) =>
            ReferenceEquals(_identity, identity.PublicationIdentity) &&
            ((_values is null && values is null) ||
                (_values is not null && values is not null && _values.AsSpan().SequenceEqual(values)));

        internal static Publication Upload(
            SilkPtexSlotGpuCandidateCache cache,
            ISilkGraphicsDevice device,
            SilkPtexSlotCandidateCache.Candidate candidate,
            SilkCpuTextureBudget budget,
            SilkPtexMaterialValue[]? values,
            SilkPtexCleanupOwner cleanup)
        {
            SilkMeshGeometry geometry = candidate.Geometry;
            SilkPtexCandidateBinding.ValidateDraw(geometry, geometry.Indices.Length, 0, 0, 0, false);
            _ = cleanup.Own(budget.Reserve(checked(
                (ulong)candidate.SlotCount * ((ulong)IntPtr.Size + sizeof(uint) +
                    (values is null ? 0ul : (ulong)Unsafe.SizeOf<SilkPtexMaterialValue>())) +
                (ulong)geometry.VertexLayout.Attributes.Count *
                    (ulong)Unsafe.SizeOf<SilkVertexAttributeDescriptor>())));
            ISilkGraphicsSampler? sampler = null;
            ISilkGraphicsTexture[] textures = [];
            ISilkGraphicsBuffer? vertices = null;
            ISilkGraphicsBuffer? indices = null;
            ISilkGraphicsBuffer? lookup = null;
            try
            {
                textures = new ISilkGraphicsTexture[candidate.SlotCount];
                var parameters = new SilkMaterialParameter[candidate.SlotCount];
                SilkVertexLayoutDescriptor vertexLayout = geometry.VertexLayout with
                {
                    Attributes = Array.AsReadOnly(geometry.VertexLayout.Attributes.ToArray()),
                };
                SilkPtexMaterialValue[]? retainedValues = values?.ToArray();
                if (values is not null)
                {
                    sampler = cleanup.Own(device.CreateSampler(SilkSceneGpuResources.PtexMaterialSampler));
                }
                SilkPtexSlotLookup.Lease lease = cleanup.Own(candidate.Lookup.AcquireLease());
                {
                    int count = lease.ShaderWordCount;
                    IDisposable wordsReservation = cleanup.Own(budget.Reserve(checked((ulong)count * sizeof(uint))));
                    var words = new uint[count];
                    lease.CopyShaderWords(words);
                    lookup = CreateBuffer(
                        device, MemoryMarshal.AsBytes(words.AsSpan()), SilkBufferUsage.Storage, cleanup);
                    cleanup.Release(wordsReservation);
                    cleanup.Release(lease);
                }
                vertices = CreateBuffer(
                    device, MemoryMarshal.AsBytes(geometry.Vertices.AsSpan()), SilkBufferUsage.Vertex, cleanup);
                indices = CreateBuffer(
                    device, MemoryMarshal.AsBytes(geometry.Indices.AsSpan()), SilkBufferUsage.Index, cleanup);
                ISilkGraphicsCommandList upload = cleanup.Own(device.CreateCommandList(), beforePayload: true);
                {
                    for (int slot = 0; slot < textures.Length; slot++)
                    {
                        SilkPackedPtexAtlas atlas = candidate.GetAtlas(slot);
                        parameters[slot] = candidate.GetParameter(slot);
                        textures[slot] = cleanup.Own(device.CreateTexture2D(new SilkTextureDescriptor(
                            atlas.Width, atlas.Height, SilkTextureFormat.Rgba8Unorm,
                            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination)));
                        if (values is null)
                        {
                            upload.UploadTexture(textures[slot], atlas.Pixels);
                        }
                        else
                        {
                            IDisposable pixelsReservation = cleanup.Own(
                                budget.Reserve(checked((ulong)atlas.Pixels.Length)));
                            byte[] pixels = atlas.Pixels.ToArray();
                            SilkSceneGpuResources.PreparePtexMaterialPixels(pixels, values[slot]);
                            upload.UploadTexture(textures[slot], pixels);
                            cleanup.Release(pixelsReservation);
                        }
                    }
                    ISilkGraphicsSubmission submission = cleanup.Own(device.Submit(upload), beforePayload: true);
                    submission.Wait();
                    cleanup.Release(submission);
                    cleanup.Release(upload);
                }
                var result = new Publication(candidate, parameters, textures,
                    vertices, indices, lookup, vertexLayout, retainedValues, sampler, cleanup, cache);
                return result;
            }
            catch
            {
                cleanup.Retire();
                throw;
            }
        }

        private static ISilkGraphicsBuffer CreateBuffer(
            ISilkGraphicsDevice device, ReadOnlySpan<byte> bytes, SilkBufferUsage usage,
            SilkPtexCleanupOwner cleanup)
        {
            ISilkGraphicsBuffer buffer = cleanup.Own(
                device.CreateBuffer(checked((nuint)bytes.Length), usage | SilkBufferUsage.Upload));
            buffer.Write(bytes);
            return buffer;
        }

        internal Lease AcquireLease()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var lease = new Lease(this);
                _leases = checked(_leases + 1);
                return lease;
            }
        }

        // Keep this lease through submission completion; native submission retention
        // remains a separate backend qualification requirement.
        internal sealed class Lease(Publication owner) : IDisposable
        {
            private bool _disposed;

            internal int SlotCount
            {
                get
                {
                    lock (owner._gate)
                    {
                        ObjectDisposedException.ThrowIf(_disposed, this);
                        return owner._parameters.Length;
                    }
                }
            }

            internal SilkVertexLayoutDescriptor VertexLayout
            {
                get
                {
                    lock (owner._gate)
                    {
                        ObjectDisposedException.ThrowIf(_disposed, this);
                        return owner._vertexLayout;
                    }
                }
            }

            internal SilkMaterialParameter GetParameter(int slot)
            {
                lock (owner._gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    return owner._parameters[slot];
                }
            }

            internal void BindAndDraw(ISilkGraphicsCommandList commands, uint instanceCount)
            {
                using Operation operation = owner._cache.Enter();
                ArgumentNullException.ThrowIfNull(commands);
                ArgumentOutOfRangeException.ThrowIfZero(instanceCount);
                lock (owner._gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    commands.SetVertexBuffer(owner._vertices);
                    commands.SetIndexBuffer(owner._indices);
                    commands.SetStorageBuffer(0, SilkPtexCandidateBinding.Binding, owner._lookup);
                    for (int slot = 0; slot < owner._textures.Length; slot++)
                    {
                        (uint samplerBinding, uint binding) =
                            SilkBindingLayoutDescriptor.GetMaterialTextureBindings(owner._parameters[slot]);
                        commands.SetTexture(0, binding, owner._textures[slot]);
                        if (owner._sampler is not null)
                        {
                            commands.SetSampler(0, samplerBinding, owner._sampler);
                        }
                    }
                    commands.DrawIndexedInstanced(owner._indexCount, instanceCount);
                }
            }

            public void Dispose()
            {
                using Operation operation = owner._cache.Enter();
                lock (owner._gate)
                {
                    if (_disposed)
                    {
                        if (owner._disposed && owner._leases == 0)
                        {
                            owner.Release();
                        }
                        return;
                    }
                    _disposed = true;
                    owner._leases--;
                    if (owner._disposed && owner._leases == 0)
                    {
                        owner.Release();
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                if (_leases == 0)
                {
                    Release();
                }
            }
        }

        private void Release()
        {
            _cleanup.Retire();
            _cleanup.Drain();
        }
    }

    public void Dispose()
    {
        using Operation operation = Enter();
        _disposed = true;
        _pending?.Abandon();
        _current?.Dispose();
        _current = null;
        _cpu.Dispose();
        DrainCleanup();
    }
}
