// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal sealed class SilkGpuPublicationDevice(ISilkGraphicsDevice inner)
    : ISilkGraphicsDevice, ISilkDeviceLossGraphicsDevice, ISilkBufferAdmissionDevice
{
    internal int? RefuseBufferAfter { get; set; }
    internal int CreatedBuffers { get; private set; }
    public SilkGraphicsBackend Backend => inner.Backend;
    public SilkGraphicsCapabilities Capabilities => inner.Capabilities;
    public bool ClipSpaceYPointsDown => inner.ClipSpaceYPointsDown;
    public ulong DeviceLossGeneration => (inner as ISilkDeviceLossGraphicsDevice)?.DeviceLossGeneration ?? 0;
    public SilkGpuBufferBudget? GpuBufferBudget => (inner as ISilkBufferAdmissionDevice)?.GpuBufferBudget;
    public void ConfigureBufferBudget(SilkGpuBufferBudget budget) => budget.ConfigureDevice(inner);

    public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage)
    {
        if (RefuseBufferAfter is { } count)
        {
            RefuseBufferAfter = count - 1;
            if (count == 0)
            {
                throw new InvalidOperationException("Injected late GPU preparation refusal.");
            }
        }
        ISilkGraphicsBuffer result = inner.CreateBuffer(size, usage);
        CreatedBuffers++;
        return result;
    }

    public ISilkGraphicsTexture CreateTexture2D(
        uint width, uint height, SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
        inner.CreateTexture2D(width, height, format);

    public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) => inner.CreateTexture2D(descriptor);
    public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) => inner.CreateSampler(descriptor);
    public ISilkGraphicsShaderModule CreateShaderModule(SilkShaderModuleDescriptor descriptor) =>
        inner.CreateShaderModule(descriptor);
    public ISilkGraphicsBindingLayout CreateBindingLayout(SilkBindingLayoutDescriptor descriptor) =>
        inner.CreateBindingLayout(descriptor);
    public ISilkGraphicsShaderProgram CreateShaderProgram(SilkShaderProgramDescriptor descriptor) =>
        inner.CreateShaderProgram(descriptor);
    public ISilkGraphicsPipeline CreateGraphicsPipeline(SilkGraphicsPipelineDescriptor descriptor) =>
        inner.CreateGraphicsPipeline(descriptor);
    public ISilkComputeBindingLayout CreateComputeBindingLayout(SilkComputeBindingLayoutDescriptor descriptor) =>
        inner.CreateComputeBindingLayout(descriptor);
    public ISilkComputeShaderProgram CreateComputeShaderProgram(SilkComputeShaderProgramDescriptor descriptor) =>
        inner.CreateComputeShaderProgram(descriptor);
    public ISilkComputePipeline CreateComputePipeline(SilkComputePipelineDescriptor descriptor) =>
        inner.CreateComputePipeline(descriptor);
    public ISilkGraphicsCommandList CreateCommandList() => inner.CreateCommandList();
    public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) => inner.Submit(commandList);
    public void WaitIdle() => inner.WaitIdle();
    public void Dispose() => inner.Dispose();
}
