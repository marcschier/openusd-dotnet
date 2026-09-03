// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace OpenUsd.Rendering.Silk;

/// <summary>Validated checked compute resource ABI.</summary>
public readonly record struct SilkComputeReflection(
    uint StorageSet,
    uint StorageBinding,
    uint StorageElementStride,
    uint UniformSet,
    uint UniformBinding,
    uint D3DUniformByteSize,
    uint VulkanUniformByteSize,
    uint ThreadGroupSizeX,
    uint ThreadGroupSizeY,
    uint ThreadGroupSizeZ)
{
    /// <summary>Gets the backend-specific constant-buffer byte size.</summary>
    public uint GetUniformByteSize(SilkGraphicsBackend backend) =>
        backend == SilkGraphicsBackend.Vulkan
            ? VulkanUniformByteSize
            : D3DUniformByteSize;
}

/// <summary>What one slot of a compute binding layout binds.</summary>
/// <remarks>
/// The kind selects the Direct3D register class as well as the Vulkan
/// descriptor type, so a layout states its slots once rather than once per
/// backend. A read-only structured slot is a <c>t</c> register and a Vulkan
/// storage buffer, a read-write one is a <c>u</c> register and the same
/// descriptor type, and a uniform slot is a <c>b</c> register and a Vulkan
/// uniform buffer.
/// </remarks>
public enum SilkComputeSlotKind
{
    /// <summary>A read-only structured buffer.</summary>
    ReadOnlyStructured = 0,

    /// <summary>The single writable structured buffer a kernel produces.</summary>
    ReadWriteStructured = 1,

    /// <summary>The single constant buffer a kernel reads its parameters from.</summary>
    Uniform = 2
}

/// <summary>One slot of a compute binding layout.</summary>
/// <param name="Kind">What the slot binds.</param>
/// <param name="Set">The Vulkan descriptor set. Always zero today.</param>
/// <param name="Binding">
/// The Vulkan binding, which is also the Direct3D register number inside the
/// class <paramref name="Kind"/> selects. Stating one number rather than two is
/// a deliberate constraint on the checked shader sources: a program whose
/// Direct3D register and Vulkan binding disagree is rejected when its
/// reflection is loaded, so the two can never drift apart unnoticed.
/// </param>
/// <param name="ElementStride">
/// The byte stride of one element, used to bound a dispatch against the bound
/// buffer before it is recorded. For a uniform slot it is instead the smallest
/// acceptable buffer size, and zero there means the checked
/// <c>ComputeParameters</c> size for the backend in use.
/// </param>
public readonly record struct SilkComputeSlot(
    SilkComputeSlotKind Kind,
    uint Set,
    uint Binding,
    uint ElementStride);

/// <summary>A bounded compute resource binding layout.</summary>
/// <remarks>
/// <para>
/// The layout is ordered: a backend binds slot <c>i</c> to root parameter
/// <c>i</c>, descriptor binding <see cref="SilkComputeSlot.Binding"/>, or Metal
/// buffer index <c>i</c>, so one declaration drives all three without a
/// per-backend table.
/// </para>
/// <para>
/// The five-argument constructor builds exactly the two-slot layout the checked
/// <c>compute.fill</c> and <c>compute.scale</c> kernels have always used, and
/// <see cref="StorageSet"/> through <see cref="UniformBinding"/> keep reporting
/// what they always did, so every existing caller compiles and behaves
/// identically.
/// </para>
/// </remarks>
public readonly record struct SilkComputeBindingLayoutDescriptor
{
    /// <summary>The largest number of slots one compute layout may declare.</summary>
    /// <remarks>
    /// The bound is a root-signature bound as much as a wire bound: Direct3D
    /// root descriptors cost two DWORDs each out of sixty-four, so twelve
    /// buffers is comfortably inside one root signature on every supported
    /// backend and needs no descriptor table indirection.
    /// </remarks>
    public const int MaximumSlots = 12;

    private readonly SilkComputeSlot[]? _slots;

    /// <summary>Initializes the two-slot checked layout.</summary>
    public SilkComputeBindingLayoutDescriptor(
        uint storageSet,
        uint storageBinding,
        uint storageElementStride,
        uint uniformSet,
        uint uniformBinding)
        : this(
        [
            new SilkComputeSlot(
                SilkComputeSlotKind.ReadWriteStructured,
                storageSet,
                storageBinding,
                storageElementStride),
            new SilkComputeSlot(
                SilkComputeSlotKind.Uniform,
                uniformSet,
                uniformBinding,
                0)
        ])
    {
    }

    /// <summary>Initializes an ordered multi-slot layout.</summary>
    public SilkComputeBindingLayoutDescriptor(IReadOnlyList<SilkComputeSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        _slots = [.. slots];
    }

    /// <summary>Gets the checked compute binding layout.</summary>
    public static SilkComputeBindingLayoutDescriptor Checked
    {
        get
        {
            SilkComputeReflection reflection = SilkCheckedShaderAssets.Compute;
            return new(
                reflection.StorageSet,
                reflection.StorageBinding,
                reflection.StorageElementStride,
                reflection.UniformSet,
                reflection.UniformBinding);
        }
    }

    /// <summary>Gets the ordered slots this layout declares.</summary>
    public IReadOnlyList<SilkComputeSlot> Slots => _slots ?? [];

    /// <summary>Gets the set of the single writable structured slot.</summary>
    public uint StorageSet => ReadWriteSlot.Set;

    /// <summary>Gets the binding of the single writable structured slot.</summary>
    public uint StorageBinding => ReadWriteSlot.Binding;

    /// <summary>Gets the element stride of the single writable structured slot.</summary>
    public uint StorageElementStride => ReadWriteSlot.ElementStride;

    /// <summary>Gets the set of the single uniform slot.</summary>
    public uint UniformSet => UniformSlot.Set;

    /// <summary>Gets the binding of the single uniform slot.</summary>
    public uint UniformBinding => UniformSlot.Binding;

    /// <summary>Gets the single writable structured slot.</summary>
    public SilkComputeSlot ReadWriteSlot => FindSingle(SilkComputeSlotKind.ReadWriteStructured);

    /// <summary>Gets the single uniform slot.</summary>
    public SilkComputeSlot UniformSlot => FindSingle(SilkComputeSlotKind.Uniform);

    /// <summary>Finds the ordinal of the slot at one set and binding.</summary>
    /// <returns>The slot ordinal, or -1 when the layout declares no such slot.</returns>
    public int IndexOf(uint set, uint binding)
    {
        IReadOnlyList<SilkComputeSlot> slots = Slots;
        for (int index = 0; index < slots.Count; index++)
        {
            if (slots[index].Set == set && slots[index].Binding == binding)
            {
                return index;
            }
        }
        return -1;
    }

    /// <summary>
    /// Validates that the layout is bounded, complete, and unambiguous.
    /// </summary>
    /// <remarks>
    /// A backend allocates root parameters, descriptor bindings and Metal
    /// buffer indices straight from this list, so a duplicate binding or a
    /// missing writable slot would produce a pipeline that silently reads or
    /// writes the wrong resource rather than one that fails to build.
    /// A set and binding pair is rejected regardless of the kinds that claim
    /// it: a Vulkan descriptor set layout has one binding number per set with
    /// no register class to separate two claimants, and
    /// <see cref="IndexOf(uint, uint)"/> -- which every backend resolves a
    /// recorded binding through -- can only ever return the first of them, so
    /// the second slot would be permanently unbindable while
    /// <c>ValidateDispatch</c> still demanded a buffer for it.
    /// </remarks>
    public void Validate()
    {
        IReadOnlyList<SilkComputeSlot> slots = Slots;
        if (slots.Count is < 2 or > MaximumSlots)
        {
            throw new ArgumentException(
                $"A compute binding layout declares 2 to {MaximumSlots} slots.");
        }
        int readWrite = 0;
        int uniform = 0;
        for (int index = 0; index < slots.Count; index++)
        {
            SilkComputeSlot slot = slots[index];
            if (!Enum.IsDefined(slot.Kind))
            {
                throw new ArgumentException("A compute slot kind is unsupported.");
            }
            if (slot.Set != 0)
            {
                throw new ArgumentException("A compute slot must live in set zero.");
            }
            switch (slot.Kind)
            {
                case SilkComputeSlotKind.ReadWriteStructured:
                    readWrite++;
                    if (slot.ElementStride == 0)
                    {
                        throw new ArgumentException(
                            "A writable compute slot requires a non-zero element stride.");
                    }
                    break;
                case SilkComputeSlotKind.Uniform:
                    uniform++;
                    break;
                default:
                    if (slot.ElementStride == 0)
                    {
                        throw new ArgumentException(
                            "A read-only compute slot requires a non-zero element stride.");
                    }
                    break;
            }
            for (int other = 0; other < index; other++)
            {
                if (slots[other].Set == slot.Set &&
                    slots[other].Binding == slot.Binding)
                {
                    throw new ArgumentException(
                        "A compute layout binds one register twice.");
                }
            }
        }
        if (readWrite != 1 || uniform != 1)
        {
            throw new ArgumentException(
                "A compute binding layout requires exactly one writable slot and " +
                "one uniform slot.");
        }
    }

    /// <summary>Compares two layouts by their declared slots.</summary>
    public bool Equals(SilkComputeBindingLayoutDescriptor other)
    {
        IReadOnlyList<SilkComputeSlot> slots = Slots;
        IReadOnlyList<SilkComputeSlot> otherSlots = other.Slots;
        if (slots.Count != otherSlots.Count)
        {
            return false;
        }
        for (int index = 0; index < slots.Count; index++)
        {
            if (slots[index] != otherSlots[index])
            {
                return false;
            }
        }
        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = default;
        foreach (SilkComputeSlot slot in Slots)
        {
            hash.Add(slot);
        }
        return hash.ToHashCode();
    }

    private SilkComputeSlot FindSingle(SilkComputeSlotKind kind)
    {
        foreach (SilkComputeSlot slot in Slots)
        {
            if (slot.Kind == kind)
            {
                return slot;
            }
        }
        throw new InvalidOperationException(
            $"The compute binding layout declares no {kind} slot.");
    }
}

/// <summary>Backend compute resource binding layout.</summary>
public interface ISilkComputeBindingLayout : IDisposable
{
    /// <summary>Gets the reflected descriptor.</summary>
    SilkComputeBindingLayoutDescriptor Descriptor { get; }
}

/// <summary>Describes one linked checked compute shader.</summary>
public readonly record struct SilkComputeShaderProgramDescriptor(
    ISilkGraphicsShaderModule ComputeShader,
    ISilkComputeBindingLayout BindingLayout);

/// <summary>Linked compute shader program.</summary>
public interface ISilkComputeShaderProgram : IDisposable
{
    /// <summary>Gets the compute binding layout.</summary>
    ISilkComputeBindingLayout BindingLayout { get; }
}

/// <summary>Describes the checked 64x1x1 compute pipeline.</summary>
public readonly record struct SilkComputePipelineDescriptor(
    ISilkComputeShaderProgram Program,
    uint ThreadGroupSizeX,
    uint ThreadGroupSizeY,
    uint ThreadGroupSizeZ)
{
    /// <summary>Creates a descriptor using checked reflection.</summary>
    public static SilkComputePipelineDescriptor Checked(ISilkComputeShaderProgram program)
    {
        SilkComputeReflection reflection = SilkCheckedShaderAssets.Compute;
        return new(
            program,
            reflection.ThreadGroupSizeX,
            reflection.ThreadGroupSizeY,
            reflection.ThreadGroupSizeZ);
    }

    /// <summary>Validates the program and fixed thread-group dimensions.</summary>
    /// <remarks>
    /// The dimensions are bounded rather than pinned to one kernel's shape.
    /// Every backend dispatches whole groups, so a zero dimension would dispatch
    /// nothing while looking valid, and a group larger than the guaranteed
    /// 1024-invocation maximum would fail at pipeline creation on some drivers
    /// and silently clamp on others. The dimensions themselves come from the
    /// checked reflection of the kernel being created, so a shader whose
    /// numthreads changed cannot be dispatched at the old shape.
    /// </remarks>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Program);
        if (ThreadGroupSizeX == 0 || ThreadGroupSizeY == 0 || ThreadGroupSizeZ == 0)
        {
            throw new ArgumentException(
                "A compute pipeline requires non-zero thread-group dimensions.");
        }
        if ((ulong)ThreadGroupSizeX * ThreadGroupSizeY * ThreadGroupSizeZ > 1024)
        {
            throw new ArgumentException(
                "A compute thread group may not exceed 1024 invocations.");
        }
    }
}

/// <summary>Backend compute pipeline.</summary>
public interface ISilkComputePipeline : IDisposable
{
    /// <summary>Gets the checked pipeline descriptor.</summary>
    SilkComputePipelineDescriptor Descriptor { get; }
}

/// <summary>Host representation of ComputeParameters.</summary>
public readonly record struct SilkComputeParameters(uint ElementCount, float Scale)
{
    /// <summary>Creates the backend-specific constant-buffer payload.</summary>
    public byte[] ToBytes(SilkGraphicsBackend backend)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ElementCount);
        if (!float.IsFinite(Scale))
        {
            throw new InvalidOperationException("Compute scale must be finite.");
        }
        int length = checked((int)SilkCheckedShaderAssets.Compute.GetUniformByteSize(backend));
        var bytes = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, ElementCount);
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(4),
            BitConverter.SingleToInt32Bits(Scale));
        return bytes;
    }
}
