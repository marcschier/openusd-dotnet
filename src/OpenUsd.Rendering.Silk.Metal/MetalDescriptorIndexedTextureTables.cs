// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace OpenUsd.Rendering.Silk.Metal;

[SupportedOSPlatform("macos")]
internal sealed class MetalDescriptorIndexedTextureTables : IDisposable
{
    internal const uint FragmentArgumentBufferIndex = 1;

    private const ulong DrawCapacity = 4096;

    private readonly MTLDevice _device;
    private readonly object _gate = new();
    private readonly Dictionary<MetalArgumentBufferLayoutKey, MetalArgumentBufferTable>
        _tables = [];
    private bool _disposed;

    internal MetalDescriptorIndexedTextureTables(MTLDevice device)
    {
        _device = device;
    }

    internal bool TryBind(
        MTLRenderCommandEncoder encoder,
        SilkBindingLayoutDescriptor layout,
        IReadOnlyList<MetalMaterialBinding> bindings)
    {
        if (bindings.Count == 0)
        {
            return true;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            MetalArgumentBufferLayoutKey key = MetalArgumentBufferLayoutKey.Create(layout);
            if (!_tables.TryGetValue(key, out MetalArgumentBufferTable? table))
            {
                table = MetalArgumentBufferTable.TryCreate(_device, key);
                if (table is null)
                {
                    return false;
                }
                _tables.Add(key, table);
            }

            if (!table.TryAllocate(out ulong offset))
            {
                return false;
            }

            table.Encode(layout, bindings, offset);
            encoder.SetFragmentBuffer(table.Buffer, offset, FragmentArgumentBufferIndex);
        }

        // Metal argument buffers contain references, but the encoder still must be
        // told which resources are resident for this draw. Without useResource the
        // shader may read black or garbage even though the argument buffer encodes
        // the right texture handles.
        MTLResource[] residentTextures = new MTLResource[bindings.Count];
        ulong residentTextureCount = 0;
        foreach (MetalMaterialBinding binding in bindings)
        {
            if (binding.Kind == SilkBindingKind.SampledTexture)
            {
                residentTextures[residentTextureCount++] = binding.Texture!.Texture;
            }
        }
        if (residentTextureCount != 0)
        {
            encoder.UseResources(
                residentTextures,
                residentTextureCount,
                MTLResourceUsage.Sample,
                MTLRenderStages.RenderStageFragment);
        }
        return true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            foreach (MetalArgumentBufferTable table in _tables.Values)
            {
                table.Dispose();
            }
            _tables.Clear();
        }
    }

    private sealed class MetalArgumentBufferTable : IDisposable
    {
        private readonly MTLArgumentEncoder _encoder;
        private readonly ulong _stride;
        private ulong _nextDraw;
        private bool _disposed;

        private MetalArgumentBufferTable(
            MTLArgumentEncoder encoder,
            MTLBuffer buffer,
            ulong stride)
        {
            _encoder = encoder;
            Buffer = buffer;
            _stride = stride;
        }

        internal MTLBuffer Buffer { get; }

        internal static MetalArgumentBufferTable? TryCreate(
            MTLDevice device,
            MetalArgumentBufferLayoutKey key)
        {
            using NSArray descriptors = CreateArgumentDescriptors(key);
            MTLArgumentEncoder encoder = default;
            MTLBuffer buffer = default;
            bool success = false;
            try
            {
                encoder = device.NewArgumentEncoder(descriptors);
                if (encoder.NativePtr == 0)
                {
                    return null;
                }

                ulong stride = AlignUp(encoder.EncodedLength, encoder.Alignment);
                buffer = device.NewBuffer(
                    checked(stride * DrawCapacity),
                    MTLResourceOptions.ResourceStorageModeShared);
                if (buffer.NativePtr == 0)
                {
                    return null;
                }

                MetalArgumentBufferTable table =
                    new(encoder, buffer, stride);
                success = true;
                return table;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (buffer.NativePtr != 0)
                {
                    if (!success)
                    {
                        buffer.Dispose();
                    }
                }
                if (encoder.NativePtr != 0)
                {
                    if (!success)
                    {
                        encoder.Dispose();
                    }
                }
            }
        }

        internal bool TryAllocate(out ulong offset)
        {
            if (_nextDraw >= DrawCapacity)
            {
                offset = 0;
                return false;
            }

            offset = checked(_nextDraw++ * _stride);
            return true;
        }

        internal void Encode(
            SilkBindingLayoutDescriptor layout,
            IReadOnlyList<MetalMaterialBinding> bindings,
            ulong offset)
        {
            _encoder.SetArgumentBuffer(Buffer, offset);
            uint maxTextureBinding = 0;
            uint maxSamplerBinding = 0;
            int textureCount = 0;
            int samplerCount = 0;
            foreach (MetalMaterialBinding binding in bindings)
            {
                _ = layout.RequireMaterialSlot(0, binding.Binding, binding.Kind);
                if (binding.Kind == SilkBindingKind.SampledTexture)
                {
                    maxTextureBinding = Math.Max(maxTextureBinding, binding.Binding);
                    textureCount++;
                    continue;
                }
                // An argument buffer here holds textures and samplers only. Anything
                // else used to fall through into the sampler branch and be encoded as
                // a sampler, which would have silently corrupted a buffer binding
                // rather than failing. Callers partition buffers out before this point.
                if (binding.Kind != SilkBindingKind.Sampler)
                {
                    throw new InvalidOperationException(
                        $"A Metal argument buffer cannot encode a {binding.Kind} at " +
                        $"binding {binding.Binding}; only textures and samplers.");
                }
                maxSamplerBinding = Math.Max(maxSamplerBinding, binding.Binding);
                samplerCount++;
            }
            if (textureCount != 0)
            {
                MTLTexture[] textures = new MTLTexture[maxTextureBinding + 1];
                foreach (MetalMaterialBinding binding in bindings)
                {
                    if (binding.Kind == SilkBindingKind.SampledTexture)
                    {
                        textures[binding.Binding] = binding.Texture!.Texture;
                    }
                }
                _encoder.SetTextures(
                    textures,
                    new NSRange { location = 0, length = (ulong)textures.Length });
            }
            if (samplerCount != 0)
            {
                MTLSamplerState[] samplers = new MTLSamplerState[maxSamplerBinding + 1];
                foreach (MetalMaterialBinding binding in bindings)
                {
                    if (binding.Kind == SilkBindingKind.Sampler)
                    {
                        samplers[binding.Binding] = binding.Sampler!.Sampler;
                    }
                }
                _encoder.SetSamplerStates(
                    samplers,
                    new NSRange { location = 0, length = (ulong)samplers.Length });
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Buffer.Dispose();
            _encoder.Dispose();
        }

        private static ulong AlignUp(ulong value, ulong alignment)
        {
            if (alignment == 0)
            {
                return value;
            }
            return checked(((value + alignment - 1) / alignment) * alignment);
        }

        private static unsafe NSArray CreateArgumentDescriptors(
            MetalArgumentBufferLayoutKey key)
        {
            var descriptors = new MTLArgumentDescriptor[key.Slots.Count];
            var objects = new nint[key.Slots.Count];
            try
            {
                for (int index = 0; index < key.Slots.Count; index++)
                {
                    MetalArgumentBufferSlot slot = key.Slots[index];
                    descriptors[index] = new MTLArgumentDescriptor
                    {
                        Access = MTLBindingAccess.ReadOnly,
                        ArrayLength = 1,
                        DataType = slot.Kind == SilkBindingKind.SampledTexture
                            ? MTLDataType.Texture
                            : MTLDataType.Sampler,
                        Index = slot.Binding
                    };
                    if (slot.Kind == SilkBindingKind.SampledTexture)
                    {
                        descriptors[index].TextureType = MTLTextureType.Type2D;
                    }
                    objects[index] = descriptors[index].NativePtr;
                }

                fixed (nint* objectsPointer = objects)
                {
                    return new NSArray().Init(
                        new NSObject((nint)objectsPointer),
                        (ulong)objects.Length);
                }
            }
            finally
            {
                for (int index = 0; index < descriptors.Length; index++)
                {
                    if (descriptors[index].NativePtr != 0)
                    {
                        descriptors[index].Dispose();
                    }
                }
            }
        }
    }
}

internal sealed class MetalArgumentBufferLayoutKey : IEquatable<MetalArgumentBufferLayoutKey>
{
    private MetalArgumentBufferLayoutKey(
        string signature,
        IReadOnlyList<MetalArgumentBufferSlot> slots)
    {
        Signature = signature;
        Slots = slots;
    }

    internal string Signature { get; }

    internal IReadOnlyList<MetalArgumentBufferSlot> Slots { get; }

    internal static MetalArgumentBufferLayoutKey Create(
        SilkBindingLayoutDescriptor layout)
    {
        IReadOnlyList<SilkBindingSlot> materialSlots = layout.MaterialSlots ?? [];
        List<MetalArgumentBufferSlot> slots = [];
        var signature = new System.Text.StringBuilder();
        for (int index = 0; index < materialSlots.Count; index++)
        {
            SilkBindingSlot slot = materialSlots[index];
            if (slot.Kind is SilkBindingKind.SampledTexture or SilkBindingKind.Sampler)
            {
                slots.Add(new MetalArgumentBufferSlot(slot.Binding, slot.Kind));
                if (signature.Length != 0)
                {
                    signature.Append(';');
                }
                signature.Append(slot.Binding);
                signature.Append(':');
                signature.Append((int)slot.Kind);
            }
        }
        return new MetalArgumentBufferLayoutKey(signature.ToString(), slots);
    }

    public bool Equals(MetalArgumentBufferLayoutKey? other) =>
        other is not null &&
        StringComparer.Ordinal.Equals(Signature, other.Signature);

    public override bool Equals(object? obj) =>
        obj is MetalArgumentBufferLayoutKey other && Equals(other);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(Signature);
}

internal readonly record struct MetalArgumentBufferSlot(
    uint Binding,
    SilkBindingKind Kind);
