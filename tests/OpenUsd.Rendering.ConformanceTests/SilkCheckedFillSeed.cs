// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Seeds a compute destination with the checked fill kernel's known pattern.
/// </summary>
/// <remarks>
/// The deformation kernel writes into a device-heap buffer, which no host can
/// pre-fill, so the pattern is written by the one other checked compute kernel
/// this repository ships. That makes the seed reproducible arithmetic rather
/// than an assumption about uninitialized device memory, and it exercises two
/// kernels with different generalized binding layouts sharing one command list
/// and one destination buffer.
/// </remarks>
internal sealed class SilkCheckedFillSeed : IDisposable
{
    private const float Scale = 0.5f;

    private readonly ISilkComputeBindingLayout _layout;
    private readonly ISilkGraphicsShaderModule _module;
    private readonly ISilkComputeShaderProgram _program;
    private readonly ISilkComputePipeline _pipeline;
    private readonly ISilkGraphicsBuffer _parameters;
    private readonly uint _elementCount;

    internal SilkCheckedFillSeed(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat,
        int floatCount)
    {
        ArgumentNullException.ThrowIfNull(device);
        // The checked kernel writes float4 elements, so the destination is
        // measured in four-float elements rather than in vertices.
        _elementCount = checked((uint)((floatCount + 3) / 4));
        _layout = device.CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor.Checked);
        _module = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadComputeFill(shaderFormat));
        _program = device.CreateComputeShaderProgram(
            new SilkComputeShaderProgramDescriptor(_module, _layout));
        _pipeline = device.CreateComputePipeline(
            SilkComputePipelineDescriptor.Checked(_program));
        byte[] parameters = new SilkComputeParameters(_elementCount, Scale)
            .ToBytes(device.Backend);
        _parameters = device.CreateBuffer(
            checked((nuint)parameters.Length),
            SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        _parameters.Write(parameters);
    }

    /// <summary>Records the seed dispatch into a command list.</summary>
    internal void Record(ISilkGraphicsCommandList commands, ISilkGraphicsBuffer destination)
    {
        ArgumentNullException.ThrowIfNull(commands);
        commands.SetComputePipeline(_pipeline);
        commands.SetStorageBuffer(0, 0, destination);
        commands.SetComputeUniformBuffer(0, 1, _parameters);
        commands.Dispatch(_elementCount);
    }

    /// <summary>The floats the seed dispatch writes, computed on the host.</summary>
    internal static float[] Expected(int floatCount)
    {
        float[] expected = new float[floatCount];
        for (int index = 0; index < floatCount; index++)
        {
            expected[index] = (index % 4) switch
            {
                0 => (index / 4) * Scale,
                3 => 1.0f,
                _ => 0.0f
            };
        }
        return expected;
    }

    public void Dispose()
    {
        _parameters.Dispose();
        _pipeline.Dispose();
        _program.Dispose();
        _module.Dispose();
        _layout.Dispose();
    }
}
