// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Performance.Tests;

internal sealed class CountingGraphicsDevice : ISilkGraphicsDevice
{
    internal List<CountingGraphicsBuffer> Buffers { get; } = [];

    internal CountingGraphicsCommandList? LastSubmittedCommandList { get; private set; }

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
        new CountingGraphicsTexture(
            new SilkTextureDescriptor(
                width,
                height,
                format,
                SilkTextureDescriptor.GetDefaultUsage(format)));

    public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
        new CountingGraphicsTexture(descriptor);

    public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
        new CountingGraphicsSampler(descriptor);

    public ISilkGraphicsShaderModule CreateShaderModule(
        SilkShaderModuleDescriptor descriptor) =>
        new CountingGraphicsShaderModule(descriptor);

    public ISilkGraphicsBindingLayout CreateBindingLayout(
        SilkBindingLayoutDescriptor descriptor) =>
        new CountingGraphicsBindingLayout(descriptor);

    public ISilkGraphicsShaderProgram CreateShaderProgram(
        SilkShaderProgramDescriptor descriptor) =>
        new CountingGraphicsShaderProgram(descriptor.BindingLayout);

    public ISilkGraphicsPipeline CreateGraphicsPipeline(
        SilkGraphicsPipelineDescriptor descriptor) =>
        new CountingGraphicsPipeline(descriptor);

    public ISilkComputeBindingLayout CreateComputeBindingLayout(
        SilkComputeBindingLayoutDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkComputeShaderProgram CreateComputeShaderProgram(
        SilkComputeShaderProgramDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkComputePipeline CreateComputePipeline(
        SilkComputePipelineDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkGraphicsCommandList CreateCommandList() => new CountingGraphicsCommandList();

    public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList)
    {
        LastSubmittedCommandList = (CountingGraphicsCommandList)commandList;
        return new CountingGraphicsSubmission();
    }

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

    internal int WriteCount { get; private set; }

    internal int WrittenByteCount { get; private set; }

    public override void Write(ReadOnlySpan<byte> data, nuint offset = 0)
    {
        _ = ValidateWrite(data.Length, offset);
        data.CopyTo(Data.AsSpan(checked((int)offset)));
        WriteCount++;
        WrittenByteCount += data.Length;
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

internal sealed class CountingGraphicsTexture(SilkTextureDescriptor descriptor)
    : SilkGraphicsTextureBase(descriptor)
{
    public override void ReadbackForTesting(Span<byte> destination) =>
        destination.Clear();

    public override void ReadbackForTesting(Span<float> destination) =>
        destination.Clear();

    protected override void ReleaseNative()
    {
    }
}

internal sealed class CountingGraphicsSampler(SilkSamplerDescriptor descriptor)
    : ISilkGraphicsSampler
{
    public SilkSamplerDescriptor Descriptor { get; } = descriptor;

    public void Dispose()
    {
    }
}

internal sealed class CountingGraphicsShaderModule(SilkShaderModuleDescriptor descriptor)
    : ISilkGraphicsShaderModule
{
    public SilkShaderModuleDescriptor Descriptor { get; } = descriptor;

    public void Dispose()
    {
    }
}

internal sealed class CountingGraphicsBindingLayout(SilkBindingLayoutDescriptor descriptor)
    : ISilkGraphicsBindingLayout
{
    public SilkBindingLayoutDescriptor Descriptor { get; } = descriptor;

    public void Dispose()
    {
    }
}

internal sealed class CountingGraphicsShaderProgram(ISilkGraphicsBindingLayout bindingLayout)
    : ISilkGraphicsShaderProgram
{
    public ISilkGraphicsBindingLayout BindingLayout { get; } = bindingLayout;

    public void Dispose()
    {
    }
}

internal sealed class CountingGraphicsPipeline(SilkGraphicsPipelineDescriptor descriptor)
    : ISilkGraphicsPipeline
{
    public SilkGraphicsPipelineDescriptor Descriptor { get; } = descriptor;

    public void Dispose()
    {
    }
}

internal sealed class CountingGraphicsCommandList : ISilkGraphicsCommandList
{
    internal int PipelineBindCount { get; private set; }

    internal int SurfaceBufferBindCount { get; private set; }

    internal int DrawCount { get; private set; }

    public void UploadTexture(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source)
    {
    }

    public void ClearColor(ISilkGraphicsTexture texture, SilkColor color)
    {
    }

    public void ClearDepth(ISilkGraphicsTexture texture, float depth)
    {
    }

    public void BeginRendering(SilkRenderingDescriptor descriptor)
    {
    }

    public void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline) =>
        PipelineBindCount++;

    public void SetViewport(SilkViewport viewport)
    {
    }

    public void SetScissor(SilkScissor scissor)
    {
    }

    public void SetVertexBuffer(ISilkGraphicsBuffer buffer)
    {
    }

    public void SetIndexBuffer(ISilkGraphicsBuffer buffer)
    {
    }

    public void SetUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
    {
    }

    public void SetTexture(uint setIndex, uint binding, ISilkGraphicsTexture texture)
    {
    }

    public void SetSampler(uint setIndex, uint binding, ISilkGraphicsSampler sampler)
    {
    }

    public void DrawIndexed(uint indexCount) => DrawCount++;

    public void DrawIndexedInstanced(uint indexCount, uint instanceCount) =>
        DrawCount++;

    public void EndRendering()
    {
    }

    public void SetComputePipeline(ISilkComputePipeline pipeline)
    {
    }

    public void SetStorageBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
    {
        if (binding == SilkBindingLayoutDescriptor.SurfaceParametersBinding)
        {
            SurfaceBufferBindCount++;
        }
    }

    public void SetComputeUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
    {
    }

    public void Dispatch(uint elementCount)
    {
    }

    public void BufferBarrier(ISilkGraphicsBuffer buffer)
    {
    }

    public void Dispose()
    {
    }
}

internal sealed class CountingGraphicsSubmission : ISilkGraphicsSubmission
{
    public bool IsCompleted => true;

    public void Wait()
    {
    }

    public void Dispose()
    {
    }
}
