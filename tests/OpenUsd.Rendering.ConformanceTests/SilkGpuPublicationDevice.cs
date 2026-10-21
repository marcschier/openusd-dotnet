// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal class SilkGpuPublicationDevice(ISilkGraphicsDevice inner)
    : SilkGraphicsDeviceLifetimeBase, ISilkGraphicsDevice, ISilkDeviceLossGraphicsDevice, ISilkBufferAdmissionDevice,
        ISilkDisplayTransformGraphicsDevice, ISilkCpuTextureAdmissionDevice, ISilkSelectionOutlineGraphicsDevice,
        ISilkTextureAdmissionDevice, ISilkStagingAdmissionDevice
{
    protected ISilkGraphicsDevice Inner => inner;
    internal int? RefuseBufferAfter { get; set; }
    internal int CreatedBuffers { get; private set; }
    internal int CreatedUniformBuffers { get; private set; }
    internal List<SilkTextureDescriptor> CreatedTextures { get; } = [];
    internal bool RefuseSubmission { get; set; }
    internal int? RefuseSubmissionAfter { get; set; }
    internal int? FailSubmissionCleanupAfter { get; set; }
    internal CancellationTokenSource? CancelAfterSubmission { get; set; }
    internal int CancelSubmissionCountdown { get; set; }
    internal bool RefuseBufferAfterSubmission { get; set; }
    internal uint? TextureDimensionLimit { get; set; }
    internal bool RefuseDisplayBinding { get; set; }
    internal bool RefuseGraphicsPipeline { get; set; }
    internal bool LoseGenerationOnPipelineRefusal { get; set; }
    internal ISilkDisplayTransformBinding? LastDisplayBinding { get; private set; }
    internal bool RefuseSelectionBinding { get; set; }
    internal bool RefuseSelectionMaskPipeline { get; set; }
    internal ISilkSelectionOutlineBinding? LastSelectionBinding { get; private set; }
    private ulong _injectedLossGeneration;
    public SilkGraphicsBackend Backend => inner.Backend;
    public SilkGraphicsCapabilities Capabilities => inner.Capabilities with
    {
        MaxTextureDimension2D = TextureDimensionLimit ?? inner.Capabilities.MaxTextureDimension2D
    };
    public bool ClipSpaceYPointsDown => inner.ClipSpaceYPointsDown;
    public ulong DeviceLossGeneration =>
        ((inner as ISilkDeviceLossGraphicsDevice)?.DeviceLossGeneration ?? 0) + _injectedLossGeneration;
    public new SilkGpuBufferBudget? GpuBufferBudget => (inner as ISilkBufferAdmissionDevice)?.GpuBufferBudget;
    public new void ConfigureBufferBudget(SilkGpuBufferBudget budget)
    {
        budget.ConfigureDevice(inner);
        base.ConfigureBufferBudget(budget);
    }
    public new SilkCpuTextureBudget? CpuTextureBudget => (inner as ISilkCpuTextureAdmissionDevice)?.CpuTextureBudget;
    public new void ConfigureCpuTextureBudget(SilkCpuTextureBudget budget)
    {
        budget.ConfigureDevice(inner);
        base.ConfigureCpuTextureBudget(budget);
    }
    public new SilkGpuTextureBudget? GpuTextureBudget => (inner as ISilkTextureAdmissionDevice)?.GpuTextureBudget;
    public new void ConfigureTextureBudget(SilkGpuTextureBudget budget)
    {
        budget.ConfigureDevice(inner);
        base.ConfigureTextureBudget(budget);
    }
    public new SilkGpuStagingBudget? GpuStagingBudget => (inner as ISilkStagingAdmissionDevice)?.GpuStagingBudget;
    public new void ConfigureStagingBudget(SilkGpuStagingBudget budget)
    {
        budget.ConfigureDevice(inner);
        base.ConfigureStagingBudget(budget);
    }
    public ulong DisplayTransformDeviceGeneration =>
        ((ISilkDisplayTransformGraphicsDevice)inner).DisplayTransformDeviceGeneration;
    public ISilkDisplayTransformGraphicsPipeline CreateDisplayTransformGraphicsPipeline(
        SilkDisplayTransformPipelineDescriptor descriptor) =>
        ((ISilkDisplayTransformGraphicsDevice)inner).CreateDisplayTransformGraphicsPipeline(descriptor);
    public ISilkDisplayTransformBinding CreateDisplayTransformBinding(SilkDisplayTransformBindingDescriptor descriptor)
    {
        if (RefuseDisplayBinding)
        {
            throw new InvalidOperationException("Injected display binding refusal.");
        }
        return LastDisplayBinding =
            ((ISilkDisplayTransformGraphicsDevice)inner).CreateDisplayTransformBinding(descriptor);
    }
    public ulong SelectionOutlineDeviceGeneration =>
        ((ISilkSelectionOutlineGraphicsDevice)inner).SelectionOutlineDeviceGeneration;
    public SilkSelectionOutlineCapabilities SelectionOutlineCapabilities =>
        ((ISilkSelectionOutlineGraphicsDevice)inner).SelectionOutlineCapabilities;
    public ISilkSelectionMaskGraphicsPipeline CreateSelectionMaskGraphicsPipeline(
        SilkSelectionMaskPipelineDescriptor descriptor) =>
        RefuseSelectionMaskPipeline
            ? throw new InvalidOperationException("Injected selection mask pipeline refusal.")
            : ((ISilkSelectionOutlineGraphicsDevice)inner).CreateSelectionMaskGraphicsPipeline(descriptor);
    public ISilkSelectionOutlineGraphicsPipeline CreateSelectionOutlineGraphicsPipeline(
        SilkSelectionOutlinePipelineDescriptor descriptor) =>
        ((ISilkSelectionOutlineGraphicsDevice)inner).CreateSelectionOutlineGraphicsPipeline(descriptor);
    public ISilkSelectionOutlineBinding CreateSelectionOutlineBinding(SilkSelectionOutlineBindingDescriptor descriptor)
    {
        if (RefuseSelectionBinding)
        {
            throw new InvalidOperationException("Injected selection binding refusal.");
        }
        return LastSelectionBinding =
            ((ISilkSelectionOutlineGraphicsDevice)inner).CreateSelectionOutlineBinding(descriptor);
    }

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
        if (size == SilkSceneUniformWriter.ByteSize && (usage & SilkBufferUsage.Uniform) != 0)
        {
            CreatedUniformBuffers++;
        }
        return result;
    }

    public ISilkGraphicsTexture CreateTexture2D(
        uint width, uint height, SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
        inner.CreateTexture2D(width, height, format);

    public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor)
    {
        CreatedTextures.Add(descriptor);
        return inner.CreateTexture2D(descriptor);
    }
    public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) => inner.CreateSampler(descriptor);
    public ISilkGraphicsShaderModule CreateShaderModule(SilkShaderModuleDescriptor descriptor) =>
        inner.CreateShaderModule(descriptor);
    public ISilkGraphicsBindingLayout CreateBindingLayout(SilkBindingLayoutDescriptor descriptor) =>
        inner.CreateBindingLayout(descriptor);
    public ISilkGraphicsShaderProgram CreateShaderProgram(SilkShaderProgramDescriptor descriptor) =>
        inner.CreateShaderProgram(descriptor);
    public ISilkGraphicsPipeline CreateGraphicsPipeline(SilkGraphicsPipelineDescriptor descriptor)
    {
        if (RefuseGraphicsPipeline)
        {
            if (LoseGenerationOnPipelineRefusal)
            {
                _injectedLossGeneration++;
            }
            throw new InvalidOperationException("Injected color pipeline refusal.");
        }
        return inner.CreateGraphicsPipeline(descriptor);
    }
    public ISilkComputeBindingLayout CreateComputeBindingLayout(SilkComputeBindingLayoutDescriptor descriptor) =>
        inner.CreateComputeBindingLayout(descriptor);
    public ISilkComputeShaderProgram CreateComputeShaderProgram(SilkComputeShaderProgramDescriptor descriptor) =>
        inner.CreateComputeShaderProgram(descriptor);
    public ISilkComputePipeline CreateComputePipeline(SilkComputePipelineDescriptor descriptor) =>
        inner.CreateComputePipeline(descriptor);
    public ISilkGraphicsCommandList CreateCommandList() => inner.CreateCommandList();
    public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList)
    {
        if (RefuseSubmission || RefuseSubmissionAfter == 0)
        {
            throw new SilkGpuStagingBudgetExceededException(1, 1, 1);
        }
        if (RefuseSubmissionAfter is { } remaining)
        {
            RefuseSubmissionAfter = remaining - 1;
        }
        ISilkGraphicsSubmission result = inner.Submit(commandList);
        if (CancelSubmissionCountdown > 0)
        {
            CancelSubmissionCountdown--;
        }
        else
        {
            CancelAfterSubmission?.Cancel();
        }
        if (RefuseBufferAfterSubmission)
        {
            RefuseBufferAfter = 0;
        }
        if (FailSubmissionCleanupAfter is { } cleanupRemaining)
        {
            FailSubmissionCleanupAfter = cleanupRemaining - 1;
            if (cleanupRemaining == 0)
            {
                return new CleanupFailureSubmission(result);
            }
        }
        return result;
    }

    private sealed class CleanupFailureSubmission(ISilkGraphicsSubmission inner) : ISilkGraphicsSubmission
    {
        private bool _disposed;
        public bool IsCompleted => inner.IsCompleted;
        public void Wait() => inner.Wait();
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            inner.Dispose();
            throw new InvalidOperationException("Injected publication cleanup failure.");
        }
    }

    public void WaitIdle() => inner.WaitIdle();
    public void Dispose() => inner.Dispose();
}
