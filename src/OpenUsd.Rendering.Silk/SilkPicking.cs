// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace OpenUsd.Rendering.Silk;

/// <summary>Encodes one non-authoritative GPU pick token in RGBA8 channel order.</summary>
public static class SilkPickTokenEncoding
{
    /// <summary>Gets the exact RGBA8 byte count for one token.</summary>
    public const int ByteSize = sizeof(uint);

    /// <summary>Writes a token as explicit little-endian R, G, B, and A bytes.</summary>
    public static void Encode(uint token, Span<byte> destination)
    {
        if (destination.Length != ByteSize)
        {
            throw new ArgumentException(
                $"A Silk pick token requires exactly {ByteSize} RGBA8 bytes.",
                nameof(destination));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination, token);
    }

    /// <summary>Reads a token from explicit little-endian R, G, B, and A bytes.</summary>
    public static uint Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length != ByteSize)
        {
            throw new ArgumentException(
                $"A Silk pick token requires exactly {ByteSize} RGBA8 bytes.",
                nameof(source));
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(source);
    }
}

/// <summary>Binds a Silk frame to renderer-neutral state and scene revisions.</summary>
public readonly record struct SilkPickFrameBinding(
    ulong StateRevision,
    ulong? SceneRevision)
{
    /// <summary>Creates a binding from one immutable renderer-neutral stage snapshot.</summary>
    public static SilkPickFrameBinding FromState(
        StageRenderState state,
        ulong? sceneRevision)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new SilkPickFrameBinding(state.Revision, sceneRevision);
    }
}

/// <summary>Defines the two checked constant-buffer bindings used by the pick pass.</summary>
public readonly record struct SilkPickBindingLayoutDescriptor(
    uint SceneSet,
    uint SceneBinding,
    uint SceneUniformByteSize,
    uint PickSet,
    uint PickBinding,
    uint PickUniformByteSize)
{
    /// <summary>Gets the reflected checked pick binding layout.</summary>
    public static SilkPickBindingLayoutDescriptor Checked => new(
        0,
        0,
        SilkCheckedShaderAssets.SceneParameters.ByteSize,
        0,
        1,
        SilkCheckedShaderAssets.PickParameters.ByteSize);

    /// <summary>Validates the checked pick binding layout.</summary>
    public void Validate()
    {
        if (SceneSet != 0 ||
            SceneBinding != 0 ||
            SceneUniformByteSize != SilkSceneUniformWriter.ByteSize)
        {
            throw new ArgumentException(
                "Pick SceneParameters must use set zero, binding zero, and 80 bytes.");
        }
        if (PickSet != 0 || PickBinding != 1 || PickUniformByteSize != 16)
        {
            throw new ArgumentException(
                "PickParameters must use set zero, binding one, and 16 bytes.");
        }
    }
}

/// <summary>Primitive topology supported by the checked pick pipeline.</summary>
public enum SilkPickPrimitiveTopology
{
    /// <summary>Independent indexed triangles.</summary>
    TriangleList
}

/// <summary>Face-culling mode supported by a Silk pick pipeline.</summary>
public enum SilkPickCullMode
{
    /// <summary>Rasterizes front- and back-facing triangles.</summary>
    None
}

/// <summary>Depth comparison used by the checked pick pipeline.</summary>
public enum SilkPickDepthCompare
{
    /// <summary>Accepts fragments whose depth is less than or equal to retained depth.</summary>
    LessEqual
}

/// <summary>Describes the checked single-sample RGBA8/D32 pick pipeline.</summary>
public readonly record struct SilkPickPipelineDescriptor(
    SilkShaderModuleDescriptor VertexShader,
    SilkShaderModuleDescriptor FragmentShader,
    SilkVertexLayoutDescriptor VertexLayout,
    SilkPickBindingLayoutDescriptor BindingLayout,
    SilkTextureFormat ColorFormat,
    SilkTextureFormat DepthFormat,
    uint SampleCount,
    SilkPickPrimitiveTopology PrimitiveTopology,
    SilkPickCullMode CullMode,
    bool BlendEnabled,
    bool DepthTestEnabled,
    bool DepthWriteEnabled,
    SilkPickDepthCompare DepthCompare)
{
    /// <summary>Creates the checked pick pipeline descriptor for one backend format.</summary>
    public static SilkPickPipelineDescriptor CreateChecked(
        SilkShaderBinaryFormat format) =>
        new(
            SilkCheckedShaderAssets.LoadPickVertex(format),
            SilkCheckedShaderAssets.LoadPickFragment(format),
            SilkVertexLayoutDescriptor.PositionNormal,
            SilkPickBindingLayoutDescriptor.Checked,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureFormat.D32Float,
            1,
            SilkPickPrimitiveTopology.TriangleList,
            SilkPickCullMode.None,
            BlendEnabled: false,
            DepthTestEnabled: true,
            DepthWriteEnabled: true,
            DepthCompare: SilkPickDepthCompare.LessEqual);

    /// <summary>Validates checked shader stages, bindings, formats, and sample count.</summary>
    public void Validate()
    {
        VertexShader.Validate();
        FragmentShader.Validate();
        if (VertexShader.Stage != SilkShaderStage.Vertex ||
            FragmentShader.Stage != SilkShaderStage.Fragment ||
            VertexShader.Format != FragmentShader.Format)
        {
            throw new ArgumentException(
                "A Silk pick pipeline requires matching vertex and fragment shader formats.");
        }

        string vertexEntryPoint = VertexShader.Format == SilkShaderBinaryFormat.SpirV
            ? "main"
            : "pickVertexMain";
        string fragmentEntryPoint = FragmentShader.Format == SilkShaderBinaryFormat.SpirV
            ? "main"
            : "pickFragmentMain";
        if (!string.Equals(
                VertexShader.EntryPoint,
                vertexEntryPoint,
                StringComparison.Ordinal) ||
            !string.Equals(
                FragmentShader.EntryPoint,
                fragmentEntryPoint,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The Silk pick pipeline must use the checked pick entry points.");
        }

        VertexLayout.Validate();
        BindingLayout.Validate();
        if (ColorFormat != SilkTextureFormat.Rgba8Unorm ||
            DepthFormat != SilkTextureFormat.D32Float ||
            SampleCount != 1 ||
            PrimitiveTopology != SilkPickPrimitiveTopology.TriangleList ||
            CullMode != SilkPickCullMode.None ||
            BlendEnabled ||
            !DepthTestEnabled ||
            !DepthWriteEnabled ||
            DepthCompare != SilkPickDepthCompare.LessEqual)
        {
            throw new ArgumentException(
                "The Silk pick pipeline requires single-sample triangle-list " +
                "RGBA8/D32 rendering with no culling or blending and writable " +
                "less-equal depth.");
        }
    }
}

/// <summary>Backend pick pipeline created from checked shader artifacts.</summary>
public interface ISilkPickGraphicsPipeline : IDisposable
{
    /// <summary>Gets the checked pipeline descriptor.</summary>
    SilkPickPipelineDescriptor Descriptor { get; }
}

/// <summary>Persistent backend buffer that receives one copied RGBA8 pixel.</summary>
public interface ISilkPickReadbackBuffer : IDisposable
{
    /// <summary>Gets the fixed byte capacity, which must be four.</summary>
    int ByteSize { get; }

    /// <summary>Reads the completed tightly packed R, G, B, and A bytes.</summary>
    void ReadRgba8Pixel(Span<byte> destination);
}

/// <summary>
/// Optional RHI capability implemented by backends that support the Silk ID pass.
/// </summary>
public interface ISilkPickingGraphicsDevice
{
    /// <summary>
    /// Gets the generation of native device resources used by picking.
    /// </summary>
    /// <remarks>A changed generation invalidates pipelines, targets, and in-flight readbacks.</remarks>
    ulong PickDeviceGeneration { get; }

    /// <summary>Creates the checked pick pipeline for the current device generation.</summary>
    ISilkPickGraphicsPipeline CreatePickGraphicsPipeline(
        SilkPickPipelineDescriptor descriptor);

    /// <summary>Creates one persistent four-byte readback buffer.</summary>
    ISilkPickReadbackBuffer CreatePickReadbackBuffer();
}

/// <summary>Optional pick commands exposed by a pick-capable graphics command list.</summary>
public interface ISilkPickGraphicsCommandList
{
    /// <summary>Sets the checked pick graphics pipeline.</summary>
    void SetPickGraphicsPipeline(ISilkPickGraphicsPipeline pipeline);

    /// <summary>
    /// Binds the nonzero first token for the next draw at set zero, binding one.
    /// </summary>
    void SetPickBaseToken(uint baseToken);

    /// <summary>
    /// Copies exactly one RGBA8 pixel using physical top-left-origin coordinates.
    /// </summary>
    void CopyRgba8Pixel(
        ISilkGraphicsTexture source,
        SilkTexturePixelCoordinate coordinate,
        ISilkPickReadbackBuffer destination);
}

/// <summary>One physical pixel coordinate measured from the texture's top-left corner.</summary>
public readonly record struct SilkTexturePixelCoordinate(uint X, uint Y)
{
    /// <summary>Validates that this coordinate lies inside an RGBA8 copy-source texture.</summary>
    public void Validate(ISilkGraphicsTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (texture.Format != SilkTextureFormat.Rgba8Unorm ||
            !texture.Usage.HasFlag(SilkTextureUsage.CopySource))
        {
            throw new ArgumentException(
                "A pick pixel source must be an RGBA8 copy-source texture.",
                nameof(texture));
        }
        if (X >= texture.Width || Y >= texture.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(texture),
                "The top-left pixel coordinate lies outside the source texture.");
        }
    }
}

/// <summary>Immutable metadata retained while one pick pixel is in flight.</summary>
public readonly record struct SilkPickReadbackContext(
    RenderPickRequest Request,
    ulong StateRevision,
    ulong? SceneRevision,
    ulong IdentityRevision,
    ulong DeviceGeneration,
    ViewportDimensions Viewport);

/// <summary>One completed readback token and its exact submission binding.</summary>
public readonly record struct SilkPickReadbackResult(
    int SlotIndex,
    SilkPickReadbackContext Context,
    uint Token);

/// <summary>One temporarily reserved persistent readback-ring slot.</summary>
public readonly struct SilkPickReadbackReservation
{
    private readonly ISilkPickReadbackBuffer? _buffer;

    internal SilkPickReadbackReservation(
        SilkPickReadbackRing owner,
        int slotIndex,
        uint reservation,
        ISilkPickReadbackBuffer buffer)
    {
        Owner = owner;
        SlotIndex = slotIndex;
        Reservation = reservation;
        _buffer = buffer;
    }

    internal SilkPickReadbackRing? Owner { get; }

    internal int SlotIndex { get; }

    internal uint Reservation { get; }

    /// <summary>Gets the persistent backend buffer owned by this slot.</summary>
    public ISilkPickReadbackBuffer Buffer =>
        _buffer ?? throw new InvalidOperationException(
            "The default readback reservation has no buffer.");
}

/// <summary>
/// Owns a deterministic two- or three-slot persistent pick readback ring.
/// </summary>
/// <remarks>
/// Acquiring a saturated ring returns false and never waits. Slots are reused in
/// round-robin order after their submissions complete and their four bytes are read.
/// </remarks>
public sealed class SilkPickReadbackRing : IDisposable
{
    private readonly Entry[] _entries;
    private int _nextAcquire;
    private int _nextCompletion;
    private uint _nextReservation = 1;
    private bool _disposed;

    /// <summary>Creates all persistent readback buffers up front.</summary>
    public SilkPickReadbackRing(
        ISilkPickingGraphicsDevice device,
        int capacity = 3)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (capacity is < 2 or > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "The Silk pick readback ring must contain two or three slots.");
        }

        DeviceGeneration = device.PickDeviceGeneration;
        _entries = new Entry[capacity];
        int created = 0;
        try
        {
            for (; created < capacity; created++)
            {
                ISilkPickReadbackBuffer buffer = device.CreatePickReadbackBuffer();
                if (buffer.ByteSize != SilkPickTokenEncoding.ByteSize)
                {
                    buffer.Dispose();
                    throw new InvalidOperationException(
                        "A Silk pick readback buffer must contain exactly four bytes.");
                }
                _entries[created] = new Entry(buffer);
            }
        }
        catch
        {
            for (int index = 0; index < created; index++)
            {
                _entries[index].Buffer.Dispose();
            }
            throw;
        }
    }

    /// <summary>Gets the persistent slot count.</summary>
    public int Capacity => _entries.Length;

    /// <summary>Gets the device generation for which the ring was allocated.</summary>
    public ulong DeviceGeneration { get; }

    /// <summary>Gets the number of submitted slots awaiting completion or consumption.</summary>
    public int InFlightCount { get; private set; }

    /// <summary>Attempts to reserve the next free slot without waiting.</summary>
    public bool TryAcquire(out SilkPickReadbackReservation reservation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int offset = 0; offset < _entries.Length; offset++)
        {
            int index = (_nextAcquire + offset) % _entries.Length;
            Entry entry = _entries[index];
            if (entry.State != EntryState.Free)
            {
                continue;
            }

            uint currentReservation = _nextReservation++;
            if (_nextReservation == 0)
            {
                _nextReservation = 1;
            }
            entry.State = EntryState.Reserved;
            entry.Reservation = currentReservation;
            _nextAcquire = (index + 1) % _entries.Length;
            reservation = new SilkPickReadbackReservation(
                this,
                index,
                currentReservation,
                entry.Buffer);
            return true;
        }

        reservation = default;
        return false;
    }

    /// <summary>Associates a reserved slot with one submitted pick command list.</summary>
    public void Commit(
        SilkPickReadbackReservation reservation,
        ISilkGraphicsSubmission submission,
        SilkPickReadbackContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(submission);
        Entry entry = ValidateReservation(reservation);
        entry.Submission = submission;
        entry.Context = context;
        entry.State = EntryState.Submitted;
        InFlightCount++;
    }

    /// <summary>Returns an unsubmitted reservation to the ring.</summary>
    public void Cancel(SilkPickReadbackReservation reservation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Entry entry = ValidateReservation(reservation);
        Reset(entry);
    }

    /// <summary>Reads and releases the next completed slot without waiting.</summary>
    public bool TryReadCompleted(out SilkPickReadbackResult result)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int offset = 0; offset < _entries.Length; offset++)
        {
            int index = (_nextCompletion + offset) % _entries.Length;
            Entry entry = _entries[index];
            if (entry.State != EntryState.Submitted ||
                entry.Submission is null ||
                !entry.Submission.IsCompleted)
            {
                continue;
            }

            Span<byte> bytes = stackalloc byte[SilkPickTokenEncoding.ByteSize];
            try
            {
                entry.Buffer.ReadRgba8Pixel(bytes);
                result = new SilkPickReadbackResult(
                    index,
                    entry.Context,
                    SilkPickTokenEncoding.Decode(bytes));
            }
            finally
            {
                entry.Submission.Dispose();
                Reset(entry);
                InFlightCount--;
                _nextCompletion = (index + 1) % _entries.Length;
            }
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>Discards one submitted slot and returns its context for stale completion.</summary>
    public bool TryDiscard(
        out int slotIndex,
        out SilkPickReadbackContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int offset = 0; offset < _entries.Length; offset++)
        {
            int index = (_nextCompletion + offset) % _entries.Length;
            Entry entry = _entries[index];
            if (entry.State != EntryState.Submitted)
            {
                continue;
            }

            context = entry.Context;
            slotIndex = index;
            entry.Submission?.Dispose();
            Reset(entry);
            InFlightCount--;
            _nextCompletion = (index + 1) % _entries.Length;
            return true;
        }

        slotIndex = -1;
        context = default;
        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (Entry entry in _entries)
        {
            entry.Submission?.Dispose();
            entry.Buffer.Dispose();
            Reset(entry);
        }
        InFlightCount = 0;
        _disposed = true;
    }

    private Entry ValidateReservation(SilkPickReadbackReservation reservation)
    {
        if (!ReferenceEquals(reservation.Owner, this) ||
            reservation.SlotIndex < 0 ||
            reservation.SlotIndex >= _entries.Length)
        {
            throw new ArgumentException(
                "The readback reservation does not belong to this ring.",
                nameof(reservation));
        }

        Entry entry = _entries[reservation.SlotIndex];
        if (entry.State != EntryState.Reserved ||
            entry.Reservation != reservation.Reservation)
        {
            throw new InvalidOperationException(
                "The readback reservation is no longer active.");
        }
        return entry;
    }

    private static void Reset(Entry entry)
    {
        entry.Submission = null;
        entry.Context = default;
        entry.Reservation = 0;
        entry.State = EntryState.Free;
    }

    private sealed class Entry(ISilkPickReadbackBuffer buffer)
    {
        internal ISilkPickReadbackBuffer Buffer { get; } = buffer;

        internal EntryState State { get; set; }

        internal uint Reservation { get; set; }

        internal ISilkGraphicsSubmission? Submission { get; set; }

        internal SilkPickReadbackContext Context { get; set; }
    }

    private enum EntryState
    {
        Free,
        Reserved,
        Submitted
    }
}
