// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

internal sealed class SilkHdrColorReadbackDevice : ISilkGraphicsDevice
{
    internal byte[] Pixel { get; } = [0x00, 0x30, 0x00, 0x38, 0x00, 0x40, 0x00, 0x3C];

    internal int DepthReadbacks { get; private set; }

    internal Action? AfterHdrReadback { get; set; }

    public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

    public SilkGraphicsCapabilities Capabilities { get; } =
        new("Readback boundary", "1", SupportsCompute: false, IsSoftware: true);

    public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage) => new Buffer(size, usage);

    public ISilkGraphicsTexture CreateTexture2D(
        uint width, uint height, SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
        throw new NotSupportedException();

    public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) => new Texture(this, descriptor);

    public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) => throw new NotSupportedException();

    public ISilkGraphicsShaderModule CreateShaderModule(SilkShaderModuleDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkGraphicsBindingLayout CreateBindingLayout(SilkBindingLayoutDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkGraphicsShaderProgram CreateShaderProgram(SilkShaderProgramDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkGraphicsPipeline CreateGraphicsPipeline(SilkGraphicsPipelineDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkComputeBindingLayout CreateComputeBindingLayout(SilkComputeBindingLayoutDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkComputeShaderProgram CreateComputeShaderProgram(SilkComputeShaderProgramDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkComputePipeline CreateComputePipeline(SilkComputePipelineDescriptor descriptor) =>
        throw new NotSupportedException();

    public ISilkGraphicsCommandList CreateCommandList() => new Commands();

    public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) => new Submission();

    public void WaitIdle()
    {
    }

    public void Dispose()
    {
    }

    private sealed class Texture(
        SilkHdrColorReadbackDevice owner,
        SilkTextureDescriptor descriptor) : SilkGraphicsTextureBase(descriptor)
    {
        public override void ReadbackForTesting(Span<byte> destination)
        {
            _ = ValidateReadback(destination.Length);
            if (Format != SilkTextureFormat.Rgba16Float)
            {
                throw new NotSupportedException("Only the HDR readback boundary is supplied by this device.");
            }
            for (int offset = 0; offset < destination.Length; offset += 8)
            {
                owner.Pixel.CopyTo(destination.Slice(offset, 8));
            }
            owner.AfterHdrReadback?.Invoke();
        }

        public override void ReadbackForTesting(Span<float> destination)
        {
            _ = ValidateDepthReadback(destination.Length);
            destination.Fill(1);
            owner.DepthReadbacks++;
        }

        protected override void ReleaseNative()
        {
        }
    }

    private sealed class Buffer(nuint size, SilkBufferUsage usage) : SilkGraphicsBufferBase(size, usage)
    {
        public override void Write(ReadOnlySpan<byte> data, nuint offset = 0) =>
            _ = ValidateWrite(data.Length, offset);

        public override void ReadbackForTesting(Span<byte> destination) => throw new NotSupportedException();

        protected override void ReleaseNative()
        {
        }
    }

    private sealed class Submission : ISilkGraphicsSubmission
    {
        public bool IsCompleted { get; private set; }

        public void Wait() => IsCompleted = true;

        public void Dispose()
        {
        }
    }

    private sealed class Commands : ISilkGraphicsCommandList
    {
        public void ClearColor(ISilkGraphicsTexture texture, SilkColor color)
        {
        }

        public void ClearDepth(ISilkGraphicsTexture texture, float depth)
        {
        }

        public void BeginRendering(SilkRenderingDescriptor descriptor)
        {
        }

        public void SetViewport(SilkViewport viewport)
        {
        }

        public void SetScissor(SilkScissor scissor)
        {
        }

        public void EndRendering()
        {
        }

        public void UploadTexture(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source) =>
            throw new NotSupportedException();

        public void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline) => throw new NotSupportedException();

        public void SetVertexBuffer(ISilkGraphicsBuffer buffer) => throw new NotSupportedException();

        public void SetIndexBuffer(ISilkGraphicsBuffer buffer) => throw new NotSupportedException();

        public void SetUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        public void SetTexture(uint setIndex, uint binding, ISilkGraphicsTexture texture) =>
            throw new NotSupportedException();

        public void SetSampler(uint setIndex, uint binding, ISilkGraphicsSampler sampler) =>
            throw new NotSupportedException();

        public void DrawIndexed(uint indexCount) => throw new NotSupportedException();

        public void DrawIndexedInstanced(uint indexCount, uint instanceCount) => throw new NotSupportedException();

        public void SetComputePipeline(ISilkComputePipeline pipeline) => throw new NotSupportedException();

        public void SetStorageBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        public void SetComputeUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        public void Dispatch(uint elementCount) => throw new NotSupportedException();

        public void BufferBarrier(ISilkGraphicsBuffer buffer) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
