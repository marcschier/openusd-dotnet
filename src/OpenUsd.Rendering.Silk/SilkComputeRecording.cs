// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// The backend-neutral half of recording a compute dispatch.
/// </summary>
/// <remarks>
/// <para>
/// Direct3D 12, Vulkan and Metal each record the same four decisions: which
/// slot a buffer binds to, whether that buffer is the right kind and large
/// enough, whether every slot the layout declares was bound, and how many
/// groups a dispatch needs. Those decisions used to be written out three times
/// against one hard-coded layout, which is exactly how three backends drift.
/// They are made once here so a generalized layout means the same thing on
/// every backend, and so the rejection messages a conformance test pins are
/// produced in one place.
/// </para>
/// <para>
/// The helper deliberately holds no backend types: it takes the layout, the
/// slot coordinates, the buffer's usage and size, and returns the slot ordinal
/// the caller binds at.
/// </para>
/// </remarks>
internal static class SilkComputeRecording
{
    /// <summary>
    /// Resolves the ordinal of a structured slot, rejecting a coordinate the
    /// layout does not declare or declares as a uniform.
    /// </summary>
    internal static int ResolveStructuredSlot(
        SilkComputeBindingLayoutDescriptor layout,
        uint setIndex,
        uint binding,
        SilkBufferUsage usage,
        string parameterName)
    {
        if (!usage.HasFlag(SilkBufferUsage.Storage))
        {
            throw new ArgumentException(
                "A storage binding requires a storage buffer.",
                parameterName);
        }
        int ordinal = layout.IndexOf(setIndex, binding);
        if (ordinal < 0 ||
            layout.Slots[ordinal].Kind == SilkComputeSlotKind.Uniform)
        {
            throw new ArgumentException(
                $"The compute layout declares no structured buffer at set {setIndex}, " +
                $"binding {binding}.",
                parameterName);
        }
        return ordinal;
    }

    /// <summary>
    /// Resolves the ordinal of the uniform slot, rejecting a coordinate the
    /// layout does not declare as a uniform and a buffer too small to hold the
    /// parameters the kernel reads.
    /// </summary>
    internal static int ResolveUniformSlot(
        SilkComputeBindingLayoutDescriptor layout,
        uint setIndex,
        uint binding,
        SilkBufferUsage usage,
        nuint size,
        uint checkedUniformByteSize,
        string parameterName)
    {
        int ordinal = layout.IndexOf(setIndex, binding);
        if (ordinal < 0 ||
            layout.Slots[ordinal].Kind != SilkComputeSlotKind.Uniform ||
            !usage.HasFlag(SilkBufferUsage.Uniform))
        {
            throw new ArgumentException(
                $"The compute layout declares no uniform buffer at set {setIndex}, " +
                $"binding {binding}.",
                parameterName);
        }
        uint required = layout.Slots[ordinal].ElementStride == 0
            ? checkedUniformByteSize
            : layout.Slots[ordinal].ElementStride;
        if (size < required)
        {
            throw new ArgumentException(
                $"The compute uniform buffer at set {setIndex}, binding {binding} " +
                $"requires at least {required} bytes.",
                parameterName);
        }
        return ordinal;
    }

    /// <summary>
    /// Checks that a dispatch is fully bound and inside the writable buffer it
    /// produces, and returns the number of thread groups it needs.
    /// </summary>
    /// <remarks>
    /// The byte bound is computed from the layout's own declared element stride
    /// rather than from a constant, so a kernel that writes wider elements is
    /// bounded by its own contract. It is checked while the command is
    /// recorded, before any backend has translated it, so an overrun is a
    /// rejected argument rather than a driver fault.
    /// </remarks>
    internal static uint ValidateDispatch(
        SilkComputeBindingLayoutDescriptor layout,
        uint elementCount,
        uint threadGroupSizeX,
        Func<int, bool> isSlotBound,
        nuint writableSize)
    {
        ArgumentOutOfRangeException.ThrowIfZero(elementCount);
        IReadOnlyList<SilkComputeSlot> slots = layout.Slots;
        for (int ordinal = 0; ordinal < slots.Count; ordinal++)
        {
            if (!isSlotBound(ordinal))
            {
                throw new InvalidOperationException(
                    $"Dispatch requires a buffer bound at set {slots[ordinal].Set}, " +
                    $"binding {slots[ordinal].Binding}.");
            }
        }
        uint stride = layout.ReadWriteSlot.ElementStride;
        if ((ulong)elementCount * stride > writableSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elementCount),
                "The storage buffer is too small for the dispatch.");
        }
        if (threadGroupSizeX == 0)
        {
            throw new InvalidOperationException(
                "The compute pipeline declares a zero thread-group width.");
        }
        return (elementCount + threadGroupSizeX - 1) / threadGroupSizeX;
    }
}
