// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal sealed partial class SilkGpuHdrObservedDevice(ISilkGraphicsDevice device) :
    ISilkGraphicsDevice,
    ISilkDisplayTransformGraphicsDevice,
    ISilkSelectionOutlineGraphicsDevice
{
    private readonly ISilkGraphicsDevice _device = device;
    private readonly int _threadId = Environment.CurrentManagedThreadId;

    internal int TextureRequests { get; private set; }

    internal int Submissions { get; private set; }

    internal int CompletedSubmissions { get; private set; }

    internal int ScenePasses { get; private set; }

    internal bool StayedOnCallingThread { get; private set; } = true;

    internal bool FailNextHdrTarget { get; set; }

    internal Action? AfterCompletedWait { get; set; }

    public SilkGraphicsBackend Backend => _device.Backend;

    public SilkGraphicsCapabilities Capabilities => _device.Capabilities;

    public bool ClipSpaceYPointsDown => _device.ClipSpaceYPointsDown;

    public ulong DisplayTransformDeviceGeneration => DisplayDevice.DisplayTransformDeviceGeneration;

    public ulong SelectionOutlineDeviceGeneration => SelectionDevice.SelectionOutlineDeviceGeneration;

    public SilkSelectionOutlineCapabilities SelectionOutlineCapabilities =>
        SelectionDevice.SelectionOutlineCapabilities;

    private ISilkDisplayTransformGraphicsDevice DisplayDevice => (ISilkDisplayTransformGraphicsDevice)_device;

    private ISilkSelectionOutlineGraphicsDevice SelectionDevice => (ISilkSelectionOutlineGraphicsDevice)_device;

    public ISilkDisplayTransformGraphicsPipeline CreateDisplayTransformGraphicsPipeline(
        SilkDisplayTransformPipelineDescriptor descriptor) =>
        DisplayDevice.CreateDisplayTransformGraphicsPipeline(descriptor);

    public ISilkDisplayTransformBinding CreateDisplayTransformBinding(
        SilkDisplayTransformBindingDescriptor descriptor) =>
        DisplayDevice.CreateDisplayTransformBinding(descriptor);

    public ISilkSelectionMaskGraphicsPipeline CreateSelectionMaskGraphicsPipeline(
        SilkSelectionMaskPipelineDescriptor descriptor) =>
        SelectionDevice.CreateSelectionMaskGraphicsPipeline(descriptor);

    public ISilkSelectionOutlineGraphicsPipeline CreateSelectionOutlineGraphicsPipeline(
        SilkSelectionOutlinePipelineDescriptor descriptor) =>
        SelectionDevice.CreateSelectionOutlineGraphicsPipeline(descriptor);

    public ISilkSelectionOutlineBinding CreateSelectionOutlineBinding(
        SilkSelectionOutlineBindingDescriptor descriptor) =>
        SelectionDevice.CreateSelectionOutlineBinding(descriptor);

    public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage) => _device.CreateBuffer(size, usage);

    public ISilkGraphicsTexture CreateTexture2D(
        uint width, uint height, SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
        CreateTexture2D(
            new SilkTextureDescriptor(width, height, format, SilkTextureDescriptor.GetDefaultUsage(format)));

    public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor)
    {
        TextureRequests++;
        if (FailNextHdrTarget && descriptor.Format == SilkTextureFormat.Rgba16Float)
        {
            FailNextHdrTarget = false;
            throw new InvalidOperationException("Injected HDR target preparation failure.");
        }
        return _device.CreateTexture2D(descriptor);
    }

    public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) => _device.CreateSampler(descriptor);

    public ISilkGraphicsShaderModule CreateShaderModule(SilkShaderModuleDescriptor descriptor) =>
        _device.CreateShaderModule(descriptor);

    public ISilkGraphicsBindingLayout CreateBindingLayout(SilkBindingLayoutDescriptor descriptor) =>
        _device.CreateBindingLayout(descriptor);

    public ISilkGraphicsShaderProgram CreateShaderProgram(SilkShaderProgramDescriptor descriptor) =>
        _device.CreateShaderProgram(descriptor);

    public ISilkGraphicsPipeline CreateGraphicsPipeline(SilkGraphicsPipelineDescriptor descriptor) =>
        _device.CreateGraphicsPipeline(descriptor);

    public ISilkComputeBindingLayout CreateComputeBindingLayout(SilkComputeBindingLayoutDescriptor descriptor) =>
        _device.CreateComputeBindingLayout(descriptor);

    public ISilkComputeShaderProgram CreateComputeShaderProgram(SilkComputeShaderProgramDescriptor descriptor) =>
        _device.CreateComputeShaderProgram(descriptor);

    public ISilkComputePipeline CreateComputePipeline(SilkComputePipelineDescriptor descriptor) =>
        _device.CreateComputePipeline(descriptor);

    public ISilkGraphicsCommandList CreateCommandList() => new Commands(this, _device.CreateCommandList());

    public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList)
    {
        StayedOnCallingThread &= Environment.CurrentManagedThreadId == _threadId;
        ISilkGraphicsSubmission submission = _device.Submit(((Commands)commandList).Inner);
        Submissions++;
        return new Submission(this, submission);
    }

    public void WaitIdle() => _device.WaitIdle();

    public void Dispose() => _device.Dispose();

    private sealed class Submission(
        SilkGpuHdrObservedDevice owner, ISilkGraphicsSubmission submission) : ISilkGraphicsSubmission
    {
        private bool _completed;

        public bool IsCompleted => submission.IsCompleted;

        public void Wait()
        {
            submission.Wait();
            owner.StayedOnCallingThread &= Environment.CurrentManagedThreadId == owner._threadId;
            if (!_completed)
            {
                _completed = true;
                owner.CompletedSubmissions++;
                Action? callback = owner.AfterCompletedWait;
                owner.AfterCompletedWait = null;
                callback?.Invoke();
            }
        }

        public void Dispose() => submission.Dispose();
    }
}
