// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkDepthCaptureTests
{
    [Test]
    [Arguments(0, 1)]
    [Arguments(1, 0)]
    [Arguments(-1, 1)]
    [Arguments(1, -1)]
    [Arguments(int.MaxValue, int.MaxValue)]
    [Arguments(16_385, 1)]
    [Arguments(1, 16_385)]
    [Arguments(4_097, 4_096)]
    public async Task InvalidRasterIsRejectedBeforeAnyGraphicsAllocation(int width, int height)
    {
        using var device = new RefusingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);

        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, width, height, RenderSettings.Default))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(device.ResourceRequests).IsEqualTo(0);
    }

    [Test]
    public async Task PixelAndByteQuotasAreEnforcedBeforeAllocatingTargets()
    {
        using var device = new RefusingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        renderer.UpdateSelection(new SelectionState(["/Plane"]));

        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 4, 4, RenderSettings.Default,
            new SilkDepthCaptureOptions(maximumPixelCount: 15)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 4, 4, RenderSettings.Default,
            new SilkDepthCaptureOptions(maximumReadbackBytes: 319)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(device.ResourceRequests).IsEqualTo(0);

        // Exactly 16 pixels / 320 charged bytes reaches the device. Its explicit
        // unsupported-target error must propagate, not become an empty depth image.
        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 4, 4, RenderSettings.Default,
            new SilkDepthCaptureOptions(maximumPixelCount: 16, maximumReadbackBytes: 320)))
            .Throws<NotSupportedException>();
        await Assert.That(device.ResourceRequests).IsEqualTo(1);
    }

    [Test]
    public async Task PrimIdChargesTwentyEightBytesAndRefusesNonPickDevicesBeforeAllocating()
    {
        using var device = new RefusingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);

        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 4, 4, RenderSettings.Default,
            new SilkDepthCaptureOptions(maximumReadbackBytes: 447) { IncludePrimId = true }))
            .Throws<ArgumentOutOfRangeException>();
        NotSupportedException? refusal = Assert.Throws<NotSupportedException>(
            () => SilkFrameCapture.CaptureRetainedWithDepth(
                renderer, device, 4, 4, RenderSettings.Default,
                new SilkDepthCaptureOptions(maximumReadbackBytes: 448) { IncludePrimId = true }));

        await Assert.That(refusal!.Message).Contains("pick-capable");
        await Assert.That(device.ResourceRequests).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, 448)]
    [Arguments(true, 512)]
    public async Task InstanceIdChargesItsOwnRasterAndRefusesNonPickDevicesBeforeAllocating(
        bool withPrimId, int quota)
    {
        using var device = new RefusingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);

        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 4, 4, RenderSettings.Default,
            new SilkDepthCaptureOptions(maximumReadbackBytes: quota - 1)
            {
                IncludePrimId = withPrimId,
                IncludeInstanceId = true,
            }))
            .Throws<ArgumentOutOfRangeException>();
        NotSupportedException? refusal = Assert.Throws<NotSupportedException>(
            () => SilkFrameCapture.CaptureRetainedWithDepth(
                renderer, device, 4, 4, RenderSettings.Default,
                new SilkDepthCaptureOptions(maximumReadbackBytes: quota)
                {
                    IncludePrimId = withPrimId,
                    IncludeInstanceId = true,
                }));

        await Assert.That(refusal!.Message).Contains(withPrimId ? "Prim identity" : "Instance identity");
        await Assert.That(refusal.Message).Contains("pick-capable");
        await Assert.That(device.ResourceRequests).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false, 448)]
    [Arguments(true, false, 512)]
    [Arguments(true, true, 576)]
    public async Task ElementIdChargesItsOwnRasterAndRefusesNonPickDevicesBeforeAllocating(
        bool withPrimId, bool withInstanceId, int quota)
    {
        using var device = new RefusingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);

        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 4, 4, RenderSettings.Default,
            new SilkDepthCaptureOptions(maximumReadbackBytes: quota - 1)
            {
                IncludePrimId = withPrimId,
                IncludeInstanceId = withInstanceId,
                IncludeElementId = true,
            }))
            .Throws<ArgumentOutOfRangeException>();
        NotSupportedException? refusal = Assert.Throws<NotSupportedException>(
            () => SilkFrameCapture.CaptureRetainedWithDepth(
                renderer, device, 4, 4, RenderSettings.Default,
                new SilkDepthCaptureOptions(maximumReadbackBytes: quota)
                {
                    IncludePrimId = withPrimId,
                    IncludeInstanceId = withInstanceId,
                    IncludeElementId = true,
                }));

        await Assert.That(refusal!.Message).Contains(withPrimId ? "Prim identity" : "Element identity");
        await Assert.That(refusal.Message).Contains("pick-capable");
        await Assert.That(device.ResourceRequests).IsEqualTo(0);
    }

    [Test]
    public async Task NormalChargesTwentyEightBytesPerPixelBeforeAllocating()
    {
        using var device = new RefusingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);

        // 20 bytes for depth and color plus 16 for the RGBA32F normal readback and 12 for
        // the packed float3 copy: 48 bytes for each of the 16 pixels.
        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 4, 4, RenderSettings.Default,
            new SilkDepthCaptureOptions(maximumReadbackBytes: 767) { IncludeNormal = true }))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(device.ResourceRequests).IsEqualTo(0);

        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 4, 4, RenderSettings.Default,
            new SilkDepthCaptureOptions(maximumReadbackBytes: 768) { IncludeNormal = true }))
            .Throws<NotSupportedException>();
        await Assert.That(device.ResourceRequests).IsEqualTo(1);
    }

    [Test]
    public async Task DefaultByteQuotaRejects4096SquareBeforeGraphicsAllocation()
    {
        using var device = new RefusingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        renderer.UpdateSelection(new SelectionState(["/Plane"]));

        // 4096 squared fits the pixel ceiling but needs 320 MiB, not 256 MiB:
        // depth 4 + HDR staging 8 + RGBA output 4 + selection upload copy 4.
        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 4_096, 4_096, RenderSettings.Default))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(device.ResourceRequests).IsEqualTo(0);
    }

    [Test]
    public async Task PreCancellationDoesNotAllocateOrRender()
    {
        using var device = new RefusingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 16, 16, RenderSettings.Default,
            cancellationToken: cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(device.ResourceRequests).IsEqualTo(0);
    }

    [Test]
    public async Task ADeviceOtherThanTheRetainedRenderersDeviceIsRejectedBeforeAllocation()
    {
        using var rendererDevice = new RefusingGraphicsDevice();
        using var otherDevice = new RefusingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(rendererDevice);

        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, otherDevice, 16, 16, RenderSettings.Default))
            .Throws<ArgumentException>();
        await Assert.That(rendererDevice.ResourceRequests).IsEqualTo(0);
        await Assert.That(otherDevice.ResourceRequests).IsEqualTo(0);
    }

    [Test]
    public async Task DisposedRendererIsRejectedBeforeTargetAllocation()
    {
        using var device = new RefusingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        renderer.Dispose();

        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithDepth(
            renderer, device, 16, 16, RenderSettings.Default))
            .Throws<ObjectDisposedException>();
        await Assert.That(device.ResourceRequests).IsEqualTo(0);
    }

    [Test]
    public async Task InvalidLimitsAreNotSilentlyClamped()
    {
        await Assert.That(() => new SilkDepthCaptureOptions(0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new SilkDepthCaptureOptions(16_777_217))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new SilkDepthCaptureOptions(maximumReadbackBytes: 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    private sealed class RefusingGraphicsDevice : ISilkGraphicsDevice
    {
        internal int ResourceRequests { get; private set; }

        public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

        public SilkGraphicsCapabilities Capabilities { get; } =
            new("Refusing device", "1", SupportsCompute: false, IsSoftware: true);

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage) =>
            throw RefuseResource();

        public ISilkGraphicsTexture CreateTexture2D(
            uint width, uint height, SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            throw RefuseResource();

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
            throw RefuseResource();

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
            throw RefuseResource();

        public ISilkGraphicsShaderModule CreateShaderModule(SilkShaderModuleDescriptor descriptor) =>
            throw RefuseResource();

        public ISilkGraphicsBindingLayout CreateBindingLayout(SilkBindingLayoutDescriptor descriptor) =>
            throw RefuseResource();

        public ISilkGraphicsShaderProgram CreateShaderProgram(SilkShaderProgramDescriptor descriptor) =>
            throw RefuseResource();

        public ISilkGraphicsPipeline CreateGraphicsPipeline(SilkGraphicsPipelineDescriptor descriptor) =>
            throw RefuseResource();

        public ISilkComputeBindingLayout CreateComputeBindingLayout(SilkComputeBindingLayoutDescriptor descriptor) =>
            throw RefuseResource();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(SilkComputeShaderProgramDescriptor descriptor) =>
            throw RefuseResource();

        public ISilkComputePipeline CreateComputePipeline(SilkComputePipelineDescriptor descriptor) =>
            throw RefuseResource();

        public ISilkGraphicsCommandList CreateCommandList() => throw RefuseResource();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
            throw RefuseResource();

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }

        private NotSupportedException RefuseResource()
        {
            ResourceRequests++;
            return new NotSupportedException("This device does not support capture targets.");
        }
    }
}
