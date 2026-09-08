// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal sealed class SilkDepthCaptureObservedDevice(ISilkGraphicsDevice device) :
    ISilkGraphicsDevice,
    ISilkSelectionOutlineGraphicsDevice
{
    private readonly ISilkGraphicsDevice _device = device;
    private readonly int _threadId = Environment.CurrentManagedThreadId;

    internal Action? AfterCompletedWait { get; set; }

    internal int TextureRequests { get; private set; }

    internal int Submissions { get; private set; }

    internal int CompletedSubmissions { get; private set; }

    internal bool StayedOnCallingThread { get; private set; } = true;

    public SilkGraphicsBackend Backend => _device.Backend;

    public SilkGraphicsCapabilities Capabilities => _device.Capabilities;

    public bool ClipSpaceYPointsDown => _device.ClipSpaceYPointsDown;

    public ulong SelectionOutlineDeviceGeneration => SelectionDevice.SelectionOutlineDeviceGeneration;

    public SilkSelectionOutlineCapabilities SelectionOutlineCapabilities =>
        SelectionDevice.SelectionOutlineCapabilities;

    private ISilkSelectionOutlineGraphicsDevice SelectionDevice =>
        _device as ISilkSelectionOutlineGraphicsDevice ??
        throw new NotSupportedException("The observed device does not support selection outlines.");

    public ISilkSelectionMaskGraphicsPipeline CreateSelectionMaskGraphicsPipeline(
        SilkSelectionMaskPipelineDescriptor descriptor) =>
        SelectionDevice.CreateSelectionMaskGraphicsPipeline(descriptor);

    public ISilkSelectionOutlineGraphicsPipeline CreateSelectionOutlineGraphicsPipeline(
        SilkSelectionOutlinePipelineDescriptor descriptor) =>
        SelectionDevice.CreateSelectionOutlineGraphicsPipeline(descriptor);

    public ISilkSelectionOutlineBinding CreateSelectionOutlineBinding(
        SilkSelectionOutlineBindingDescriptor descriptor) =>
        SelectionDevice.CreateSelectionOutlineBinding(descriptor);

    public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage) =>
        _device.CreateBuffer(size, usage);

    public ISilkGraphicsTexture CreateTexture2D(
        uint width, uint height, SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm)
    {
        ObserveThread();
        TextureRequests++;
        return _device.CreateTexture2D(width, height, format);
    }

    public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor)
    {
        ObserveThread();
        TextureRequests++;
        return _device.CreateTexture2D(descriptor);
    }

    public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
        _device.CreateSampler(descriptor);

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

    public ISilkGraphicsCommandList CreateCommandList() => _device.CreateCommandList();

    public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList)
    {
        ObserveThread();
        ISilkGraphicsSubmission submission = _device.Submit(commandList);
        Submissions++;
        return new ObservedSubmission(this, submission);
    }

    public void WaitIdle() => _device.WaitIdle();

    public void Dispose() => _device.Dispose();

    private void ObserveThread() =>
        StayedOnCallingThread &= Environment.CurrentManagedThreadId == _threadId;

    private sealed class ObservedSubmission(
        SilkDepthCaptureObservedDevice owner,
        ISilkGraphicsSubmission submission) : ISilkGraphicsSubmission
    {
        private readonly SilkDepthCaptureObservedDevice _owner = owner;
        private readonly ISilkGraphicsSubmission _submission = submission;
        private bool _completed;

        public bool IsCompleted => _submission.IsCompleted;

        public void Wait()
        {
            _submission.Wait();
            _owner.ObserveThread();
            if (!_completed)
            {
                _completed = true;
                _owner.CompletedSubmissions++;
                Action? callback = _owner.AfterCompletedWait;
                _owner.AfterCompletedWait = null;
                callback?.Invoke();
            }
        }

        public void Dispose() => _submission.Dispose();
    }
}
