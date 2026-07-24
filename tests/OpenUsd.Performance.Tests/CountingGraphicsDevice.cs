// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Performance.Tests;

internal sealed class CountingGraphicsDevice : ISilkGraphicsDevice
{
    internal List<CountingGraphicsBuffer> Buffers { get; } = [];

    internal int CreatedBufferCount { get; private set; }

    internal int DisposedBufferCount { get; private set; }

    internal int LiveBufferCount => CreatedBufferCount - DisposedBufferCount;

    public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

    public SilkGraphicsCapabilities Capabilities { get; } =
        new("Performance test", "1", SupportsCompute: true, IsSoftware: true);

    public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage)
    {
        var buffer = new CountingGraphicsBuffer(size, usage, BufferDisposed);
        Buffers.Add(buffer);
        CreatedBufferCount++;
        return buffer;
    }

    public ISilkGraphicsTexture CreateTexture2D(
        uint width,
        uint height,
        SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
        throw new NotSupportedException();

    public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkGraphicsShaderModule CreateShaderModule(
        SilkShaderModuleDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkGraphicsBindingLayout CreateBindingLayout(
        SilkBindingLayoutDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkGraphicsShaderProgram CreateShaderProgram(
        SilkShaderProgramDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkGraphicsPipeline CreateGraphicsPipeline(
        SilkGraphicsPipelineDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkComputeBindingLayout CreateComputeBindingLayout(
        SilkComputeBindingLayoutDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkComputeShaderProgram CreateComputeShaderProgram(
        SilkComputeShaderProgramDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkComputePipeline CreateComputePipeline(
        SilkComputePipelineDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkGraphicsCommandList CreateCommandList() =>
        throw new NotSupportedException();

    public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
        throw new NotSupportedException();

    public void WaitIdle()
    {
    }

    public void Dispose()
    {
    }

    private void BufferDisposed() => DisposedBufferCount++;
}

internal sealed class CountingGraphicsBuffer(
    nuint size,
    SilkBufferUsage usage,
    Action disposed)
    : SilkGraphicsBufferBase(size, usage)
{
    private readonly Action _disposed = disposed;

    internal byte[] Data { get; } = new byte[checked((int)size)];

    public override void Write(ReadOnlySpan<byte> data, nuint offset = 0)
    {
        _ = ValidateWrite(data.Length, offset);
        data.CopyTo(Data.AsSpan(checked((int)offset)));
    }

    public override void ReadbackForTesting(Span<byte> destination)
    {
        if (destination.Length != Data.Length)
        {
            throw new ArgumentException(
                "The readback destination must match the buffer size.",
                nameof(destination));
        }
        Data.CopyTo(destination);
    }

    protected override void ReleaseNative() => _disposed();
}
