// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

internal static class SilkGraphicsUniformBinding
{
    internal static void ValidateBinding(
        uint set,
        uint binding,
        ISilkGraphicsBuffer buffer,
        SilkBindingLayoutDescriptor? activeLayout)
    {
        uint requiredBytes = RequiredByteSize(activeLayout);
        if (set != 0 || binding != 0 ||
            !buffer.Usage.HasFlag(SilkBufferUsage.Uniform) ||
            buffer.Size < requiredBytes)
        {
            throw new ArgumentException(
                $"A uniform buffer of at least {requiredBytes} bytes is required at set 0, binding 0.",
                nameof(buffer));
        }
    }

    internal static void ValidateDraw(
        ISilkGraphicsBuffer buffer,
        SilkBindingLayoutDescriptor? activeLayout)
    {
        uint requiredBytes = RequiredByteSize(activeLayout);
        if (buffer.Size < requiredBytes)
        {
            throw new InvalidOperationException(
                $"The active pipeline requires a uniform buffer of at least {requiredBytes} bytes.");
        }
    }

    private static uint RequiredByteSize(SilkBindingLayoutDescriptor? activeLayout) =>
        activeLayout?.UniformByteSize ?? SilkSceneUniformWriter.ByteSize;
}
