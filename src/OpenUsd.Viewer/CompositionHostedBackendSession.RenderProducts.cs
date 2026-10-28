// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Threading;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer;

internal sealed partial class CompositionHostedBackendSession : IViewerRenderProductCaptureBackend
{
    private readonly ViewerProductResourceRegistry _productResources = new();

    public string? GetRenderProductUnsupportedReason(
        StageRenderState state, RenderProductJobPlan plan, RenderHdrColorFormat hdrColorFormat)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(plan);
        if (!SupportsFrameCapture)
        {
            return "The active hdSilk composition renderer has no completed frame available for product readback.";
        }
        if (resources.CaptureDevice is null)
        {
            return "The active hdSilk renderer does not expose the managed graphics device " +
                "needed for product readback.";
        }
        if (plan.IncludePrimId && resources.CaptureDevice is not ISilkPickingGraphicsDevice)
        {
            return "The active hdSilk graphics device cannot run the pick pass needed for the primId " +
                "identity plane.";
        }
        if (plan.IncludeInstanceId && resources.CaptureDevice is not ISilkPickingGraphicsDevice)
        {
            return "The active hdSilk graphics device cannot run the pick pass needed for the instanceId " +
                "identity plane.";
        }
        try
        {
            _ = new SilkSceneIngestionOptions(plan.IncludedPurposes, plan.MaterialBindingPurpose);
            string? outputUnsupported = new ViewerRenderSequenceOutputOptions(
                plan.IncludeDeviceDepth,
                plan.IncludeHdrColor,
                hdrColorFormat).GetAdmissionUnsupportedReason(plan.Frames[0].OutputDimensions);
            if (outputUnsupported is not null)
            {
                return outputUnsupported;
            }
            _ = plan.CreateJob(Path.GetTempPath(), hdrColorFormat);
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException or
            InvalidOperationException or NotSupportedException)
        {
            return exception.Message;
        }
        return null;
    }

    public async ValueTask<IViewerRenderProductCaptureLease> BeginRenderProductCaptureAsync(
        RenderProductJobPlan plan, RenderHdrColorFormat hdrColorFormat, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        if (GetRenderProductUnsupportedReason(CurrentState, plan, hdrColorFormat) is { } unsupported)
        {
            throw new NotSupportedException(unsupported);
        }
        await Dispatcher.UIThread.InvokeAsync(_productResources.Dispose);
        IDisposable? captureLifetime = await control.AcquireCaptureAsync(cancellationToken);
        try
        {
            return await Dispatcher.UIThread.InvokeAsync<IViewerRenderProductCaptureLease>(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                if (resources.CaptureDevice is not { } device)
                {
                    throw new NotSupportedException("The product renderer is no longer available for readback.");
                }
                ViewerProductResourceRegistry.Lease owner = _productResources.CreateLease();
                owner.Pin(captureLifetime ??
                    throw new InvalidOperationException("The product capture lifetime was not acquired."));
                captureLifetime = null;
                try
                {
                    OpenUsdSilkSession productSession = owner.Own(resources.CreateProductSession());
                    SilkFrameCapturer capturer = owner.Own(new SilkFrameCapturer(device,
                        resources.TextureResidencyOptions));
                    return new ProductCaptureLease(productSession, capturer, owner, plan, hdrColorFormat);
                }
                catch (Exception createFailure)
                {
                    try
                    {
                        owner.Dispose();
                    }
                    catch (Exception cleanupFailure)
                    {
                        throw new AggregateException("Product capture construction failed and resources remain owned.",
                            createFailure, cleanupFailure);
                    }
                    throw;
                }
            });
        }
        finally
        {
            captureLifetime?.Dispose();
        }
    }

    private sealed class ProductCaptureLease(
        OpenUsdSilkSession session,
        SilkFrameCapturer capturer,
        ViewerProductResourceRegistry.Lease owner,
        RenderProductJobPlan plan,
        RenderHdrColorFormat hdrColorFormat) : IViewerRenderProductCaptureLease
    {
        private readonly SilkSceneIngestionOptions _ingestionOptions =
            new(plan.IncludedPurposes, plan.MaterialBindingPurpose);
        private int _disposed;
        private int _closing;

        public ValueTask<ViewerFrameCaptureResult> CaptureAsync(
            StageRenderState state, CancellationToken cancellationToken)
        {
            Dispatcher.UIThread.VerifyAccess();
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _closing) != 0 || Volatile.Read(ref _disposed) != 0, this);
            SilkFrameCaptureResult capture = plan.IncludeHdrColor
                ? capturer.CaptureWithHdrColor(
                    session,
                    state.Viewport.Width,
                    state.Viewport.Height,
                    state.RenderSettings,
                    _ingestionOptions,
                    new SilkHdrColorCaptureOptions(
                        plan.IncludeDeviceDepth,
                        maximumReadbackBytes: RenderDiskJobLimits.Default.MaximumFrameBytes)
                    {
                        IncludePrimId = plan.IncludePrimId,
                        IncludeInstanceId = plan.IncludeInstanceId
                    },
                    state.Time.TimeCode,
                    state.Camera,
                    cancellationToken)
                : capturer.CaptureWithDepth(
                    session,
                    state.Viewport.Width,
                    state.Viewport.Height,
                    state.RenderSettings,
                    _ingestionOptions,
                    new SilkDepthCaptureOptions(
                        maximumReadbackBytes: RenderDiskJobLimits.Default.MaximumFrameBytes)
                    {
                        IncludePrimId = plan.IncludePrimId,
                        IncludeInstanceId = plan.IncludeInstanceId
                    },
                    state.Time.TimeCode,
                    state.Camera,
                    cancellationToken);
            if (hdrColorFormat == RenderHdrColorFormat.Exr && capture.HdrColor is null)
            {
                throw new InvalidOperationException("The product renderer did not return HDR color for EXR encoding.");
            }
            return ValueTask.FromResult(new ViewerFrameCaptureResult(
                capture.Width,
                capture.Height,
                capture.Rgba,
                ViewerFrameRowOrder.TopDown,
                capture.Diagnostics)
            {
                DeviceDepth = plan.IncludeDeviceDepth && capture.Depth is { } depth
                    ? new RenderJobDeviceDepth(depth.Width, depth.Height, depth.Values) : null,
                HdrColor = capture.HdrColor is { } hdr
                    ? new RenderJobHdrColor(hdr.Width, hdr.Height, hdr.Rgba16Float) : null,
                PrimId = capture.PrimId is { } primId ? ToJobPrimId(primId) : null,
                InstanceId = capture.InstanceId is { } instanceId
                    ? new RenderJobInstanceId(instanceId.Width, instanceId.Height, instanceId.Values) : null
            });
        }

        private static RenderJobPrimId ToJobPrimId(SilkPrimIdCaptureResult primId)
        {
            var identities = new RenderJobPrimIdentity[primId.Identities.Count];
            for (int index = 0; index < identities.Length; index++)
            {
                SilkPrimIdentity identity = primId.Identities[index];
                identities[index] = new RenderJobPrimIdentity(identity.Path, identity.InstanceIndex);
            }
            return new RenderJobPrimId(primId.Width, primId.Height, primId.Values, identities);
        }

        public async ValueTask DisposeAsync()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }
            Volatile.Write(ref _closing, 1);
            await Dispatcher.UIThread.InvokeAsync(owner.Dispose);
            Volatile.Write(ref _disposed, 1);
        }
    }
}
