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

/// <summary>Checked outputValues and ComputeParameters binding layout.</summary>
public readonly record struct SilkComputeBindingLayoutDescriptor(
    uint StorageSet,
    uint StorageBinding,
    uint StorageElementStride,
    uint UniformSet,
    uint UniformBinding)
{
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

    /// <summary>Validates the checked compute layout.</summary>
    public void Validate()
    {
        if (this != new SilkComputeBindingLayoutDescriptor(0, 0, 16, 0, 1))
        {
            throw new ArgumentException(
                "Compute requires outputValues at set 0 binding 0 with stride 16 and " +
                "ComputeParameters at set 0 binding 1.");
        }
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
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Program);
        SilkComputeReflection reflection = SilkCheckedShaderAssets.Compute;
        if (ThreadGroupSizeX != reflection.ThreadGroupSizeX ||
            ThreadGroupSizeY != reflection.ThreadGroupSizeY ||
            ThreadGroupSizeZ != reflection.ThreadGroupSizeZ)
        {
            throw new ArgumentException("The checked compute pipeline requires 64x1x1 threads.");
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
