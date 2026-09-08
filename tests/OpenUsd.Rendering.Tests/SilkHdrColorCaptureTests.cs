// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkHdrColorCaptureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SelectedCaptureChargesTwentyBytesPerPixelEvenWithoutDepth(bool includeDeviceDepth)
    {
        using var device = new RefusingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        renderer.UpdateSelection(new SelectionState(["/Plane"]));

        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 40, 32, RenderSettings.Default,
            new SilkHdrColorCaptureOptions(includeDeviceDepth, maximumReadbackBytes: 25_599)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(device.ResourceRequests).IsEqualTo(0);

        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 40, 32, RenderSettings.Default,
            new SilkHdrColorCaptureOptions(includeDeviceDepth, maximumReadbackBytes: 25_600)))
            .Throws<NotSupportedException>();
        await Assert.That(device.ResourceRequests).IsEqualTo(1)
            .Because("the exact quota reaches the device's unsupported-target error");
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task NonfiniteStoredChannelsIncludingAlphaAreRefusedWithoutClamping(int channel)
    {
        using var device = new SilkHdrColorReadbackDevice();
        using var renderer = new SilkMeshRenderer(device);
        foreach (ushort nonfinite in new ushort[] { 0x7C00, 0xFC00, 0x7E00, 0x7C01, 0xFFFF })
        {
            BinaryPrimitives.WriteUInt16LittleEndian(device.Pixel.AsSpan(channel * 2, 2), nonfinite);
            await Assert.That(() => SilkFrameCapture.CaptureRetainedWithHdrColor(
                renderer, device, 2, 2, RenderSettings.Default)).Throws<InvalidDataException>();
        }
        await Assert.That(device.DepthReadbacks).IsEqualTo(0);
    }

    [Test]
    public async Task FiniteNegativeValuesAndSignedZeroArePreservedVerbatimInDetachedHdr()
    {
        using var device = new SilkHdrColorReadbackDevice();
        using var renderer = new SilkMeshRenderer(device);
        byte[] expected = [0x00, 0xB0, 0x00, 0x80, 0xFF, 0x7B, 0x00, 0x80];
        expected.CopyTo(device.Pixel, 0);
        SilkFrameCaptureResult result = SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 1, 1, RenderSettings.Default);
        device.Pixel.AsSpan().Clear();
        _ = SilkFrameCapture.CaptureRetainedWithHdrColor(renderer, device, 2, 2, RenderSettings.Default);
        renderer.Dispose();
        device.Dispose();

        await Assert.That(result.HdrColor!.Rgba16Float.Span.SequenceEqual(expected)).IsTrue();
        await Assert.That(result.Rgba.Span.SequenceEqual<byte>([0, 0, 255, 0])).IsTrue();
        await Assert.That(result.HdrColor.Convention)
            .IsEqualTo(SilkHdrColorConvention.RendererWorkingCompositedBeforeExposureAndDisplay);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CaptureDoesNotAllocateAnotherHalfOrExpandedFloatPixelArray(bool includeDeviceDepth)
    {
        using var device = new SilkHdrColorReadbackDevice();
        using var renderer = new SilkMeshRenderer(device);
        var options = new SilkHdrColorCaptureOptions(includeDeviceDepth);
        _ = SilkFrameCapture.CaptureRetainedWithHdrColor(renderer, device, 2, 2, RenderSettings.Default, options);
        long before = GC.GetAllocatedBytesForCurrentThread();
        SilkFrameCaptureResult result = SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 256, 128, RenderSettings.Default, options);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Raw + RGBA8 (+ optional depth) consumes 12 (16) bytes/pixel. Allow
        // another 4 bytes/pixel for small per-frame objects, but not an 8-byte HDR copy.
        await Assert.That(allocated).IsLessThan(includeDeviceDepth ? 655_360L : 524_288L);
        await Assert.That(result.HdrColor!.Rgba16Float.Length).IsEqualTo(262_144);
        await Assert.That(result.Rgba.Length).IsEqualTo(131_072);
        await Assert.That(device.DepthReadbacks).IsEqualTo(includeDeviceDepth ? 2 : 0);
    }

    [Test]
    public async Task CancellationAfterReadbackIsReportedBeforePublishingHdr()
    {
        using var device = new SilkHdrColorReadbackDevice();
        using var renderer = new SilkMeshRenderer(device);
        using var cancellation = new CancellationTokenSource();
        device.AfterHdrReadback = cancellation.Cancel;

        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 2, 2, RenderSettings.Default, cancellationToken: cancellation.Token))
            .Throws<OperationCanceledException>();
        device.AfterHdrReadback = null;
        SilkFrameCaptureResult retry = SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 2, 2, RenderSettings.Default);
        await Assert.That(retry.HdrColor!.Rgba16Float.Length).IsEqualTo(32);
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(1, 0)]
    [Arguments(-1, 1)]
    [Arguments(1, -1)]
    [Arguments(int.MaxValue, int.MaxValue)]
    [Arguments(16_385, 1)]
    [Arguments(1, 16_385)]
    [Arguments(4_097, 4_096)]
    [Arguments(4_096, 4_096)]
    public async Task InvalidOrDefaultOverBudgetRasterIsRejectedBeforeAnyGraphicsAllocation(int width, int height)
    {
        using var device = new RefusingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);

        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, width, height, RenderSettings.Default)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(device.ResourceRequests).IsEqualTo(0);
    }

    [Test]
    public async Task PixelLimitsAndInvalidOptionsAreNotSilentlyClamped()
    {
        using var device = new RefusingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);

        await Assert.That(() => new SilkHdrColorCaptureOptions(maximumPixelCount: 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new SilkHdrColorCaptureOptions(maximumPixelCount: 16_777_217))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new SilkHdrColorCaptureOptions(maximumReadbackBytes: 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 40, 32, RenderSettings.Default,
            new SilkHdrColorCaptureOptions(maximumPixelCount: 1279)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(device.ResourceRequests).IsEqualTo(0);
        await Assert.That(SilkHdrColorCaptureOptions.Default.IncludeDeviceDepth).IsFalse();
        await Assert.That(SilkHdrColorCaptureOptions.Default.MaximumPixelCount).IsEqualTo(16_777_216);
        await Assert.That(SilkHdrColorCaptureOptions.Default.MaximumReadbackBytes).IsEqualTo(268_435_456L);
    }

    [Test]
    public async Task PreCancellationWrongDeviceAndDisposedRendererFailBeforeGraphicsAllocation()
    {
        using var device = new RefusingGraphicsDevice();
        using var other = new RefusingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);

        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 40, 32, RenderSettings.Default,
            cancellationToken: new CancellationToken(canceled: true))).Throws<OperationCanceledException>();
        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, other, 40, 32, RenderSettings.Default)).Throws<ArgumentException>();
        renderer.Dispose();
        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 40, 32, RenderSettings.Default)).Throws<ObjectDisposedException>();
        await Assert.That(device.ResourceRequests).IsEqualTo(0);
        await Assert.That(other.ResourceRequests).IsEqualTo(0);
    }

    [Test]
    public async Task CustomRenderersAndGpuTransformsNeverMasqueradeAsHdr()
    {
        using var device = new RefusingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using var custom = new RefusingRenderer();
        RenderSettings gpu = RenderSettings.Default with
        {
            DisplayTransform = new RenderDisplayTransform(
                Path.Combine(AppContext.BaseDirectory, "never-opened.ocio"), "linear", "Display", "View")
        };

        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithHdrColor(
            custom, device, 40, 32, RenderSettings.Default)).Throws<NotSupportedException>();
        await Assert.That(() => SilkFrameCapture.CaptureRetainedWithHdrColor(
            renderer, device, 40, 32, gpu)).Throws<NotSupportedException>();
        await Assert.That(device.ResourceRequests).IsEqualTo(0);
        await Assert.That(custom.Rendered).IsFalse();
    }

    private sealed class RefusingRenderer : ISilkRenderTargetRenderer
    {
        internal bool Rendered { get; private set; }

        public SelectionState Selection => throw new NotSupportedException();

        public SilkSelectionOutlineSettings SelectionOutlineSettings => throw new NotSupportedException();

        public SilkSelectionOutlineCapabilities SelectionOutlineCapabilities => throw new NotSupportedException();

        public SilkSelectionOutlineDiagnostics SelectionOutlineDiagnostics => throw new NotSupportedException();

        public void UpdateSelection(SelectionState selection, SilkSelectionOutlineSettings? settings = null) =>
            throw new NotSupportedException();

        public SilkMeshRenderResult ApplyAndRender(
            OpenUsdSilkPage page, ISilkGraphicsTexture colorTarget, ISilkGraphicsTexture depthTarget,
            SilkMeshRenderOptions? options = null) => throw new NotSupportedException();

        public SilkMeshRenderResult ApplyAndRender(
            OpenUsdSilkPage page, ISilkGraphicsTexture colorTarget, ISilkGraphicsTexture depthTarget,
            SilkPickFrameBinding pickBinding, SilkMeshRenderOptions? options = null) =>
            throw new NotSupportedException();

        public SilkMeshRenderResult Render(
            ISilkGraphicsTexture colorTarget, ISilkGraphicsTexture depthTarget,
            SilkPickFrameBinding pickBinding, SilkMeshRenderOptions? options = null) =>
            throw new NotSupportedException();

        public SilkMeshRenderResult Render(
            ISilkGraphicsTexture colorTarget,
            ISilkGraphicsTexture depthTarget,
            SilkMeshRenderOptions? options = null)
        {
            Rendered = true;
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
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
