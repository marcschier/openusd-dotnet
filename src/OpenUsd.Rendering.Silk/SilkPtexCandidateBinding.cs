// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal static class SilkPtexCandidateBinding
{
    internal const uint Binding = 49;
    internal const uint HlslRegister = 31;
    internal const uint MetalBuffer = 2;
    internal const int HeaderWords = 4;
    internal const int CornerWords = 4;

    internal static SilkBindingSlot Slot => new(
        0, Binding, SilkBindingKind.StorageBuffer, 0,
        SilkShaderStageVisibility.Vertex | SilkShaderStageVisibility.Fragment);

    internal static SilkBindingLayoutDescriptor WithLookupSlot(SilkBindingLayoutDescriptor descriptor)
    {
        descriptor.Validate();
        IReadOnlyList<SilkBindingSlot> slots = descriptor.MaterialSlots ?? [];
        if (slots.Any(slot => slot.Binding == Binding ||
            ((slot.Kind == SilkBindingKind.UniformBuffer || slot.Kind == SilkBindingKind.StorageBuffer) &&
                slot.Binding == MetalBuffer) ||
            (slot.Kind == SilkBindingKind.StorageBuffer && slot.Binding == HlslRegister)))
        {
            throw new InvalidDataException(
                "The candidate lookup binding or backend buffer register is already occupied.");
        }
        SilkBindingLayoutDescriptor result = descriptor with { MaterialSlots = [.. slots, Slot] };
        result.Validate();
        return result;
    }

    internal static int GetWordCount(int corners, int faces, int slots)
    {
        int words = checked(HeaderWords + corners * CornerWords + faces * slots * 4);
        _ = checked(words * sizeof(uint));
        return words;
    }

    internal static void ValidateDraw(
        SilkMeshGeometry geometry,
        int cornerCount,
        uint firstIndex,
        int baseVertex,
        uint firstInstance,
        bool gpuDeformed)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        geometry.VertexLayout.Validate();
        int stride = checked((int)(geometry.VertexLayout.Stride / sizeof(float)));
        if (firstIndex != 0 || baseVertex != 0 || firstInstance != 0 ||
            gpuDeformed || !geometry.DerivedCornerVertices ||
            geometry.Indices.Length != cornerCount || cornerCount == 0 || cornerCount % 3 != 0 ||
            geometry.Vertices.Length % stride != 0 || geometry.VertexCount < (uint)cornerCount)
        {
            throw new InvalidDataException(
                "Ptex vertex-ID lookup requires a zero-origin CPU-derived corner draw.");
        }
        // The surface draw cannot reference any pick-only vertices after the corner prefix.
        for (int corner = 0; corner < cornerCount; corner++)
        {
            if (geometry.Indices[corner] != (uint)corner)
            {
                throw new InvalidDataException(
                    "Ptex vertex-ID lookup requires explicit identity corner indices.");
            }
        }
    }
}
