// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Threading;
using OpenUsd.Render;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed partial class ViewerRenderCoordinator
{
    internal bool SupportsRenderProductCapture =>
        _backendRegistry.CaptureRenderSequenceBackend()?.SupportsRenderProductCapture == true;

    internal async ValueTask<ViewerAuthoredRenderProductSnapshot> QueryAuthoredRenderProductsAsync(
        string? settingsPath,
        string? selectedProductPath,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!string.IsNullOrWhiteSpace(settingsPath))
        {
            UsdPath.ValidateAbsolutePrimPath(settingsPath);
        }
        UsdRenderSpecification? specification = await Scheduler.InvokeAsync(
            stage => stage.GetRenderSpecification(string.IsNullOrWhiteSpace(settingsPath) ? null : settingsPath),
            cancellationToken);
        return ViewerAuthoredRenderProductSelection.CreateSnapshot(specification, selectedProductPath);
    }

    internal Task<RenderDiskJobResult> RenderAuthoredProductAsync(
        ViewerAuthoredRenderProductRequest request,
        Action<ViewerAuthoredRenderProductProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        ThrowIfDisposed();
        lock (_sequenceGate)
        {
            ThrowIfDisposed();
            if (!_sequenceDrained.IsCompleted)
            {
                throw new InvalidOperationException("An image or product sequence already owns this renderer.");
            }
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
            var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _sequenceCancellation = cancellation;
            _sequenceDrained = drained.Task;
            return RenderAuthoredProductCoreAsync(request, progress, cancellation, drained);
        }
    }

    private async Task<RenderDiskJobResult> RenderAuthoredProductCoreAsync(
        ViewerAuthoredRenderProductRequest request,
        Action<ViewerAuthoredRenderProductProgress> progress,
        CancellationTokenSource cancellation,
        TaskCompletionSource drained)
    {
        await Task.Yield();
        CancellationToken token = cancellation.Token;
        bool entered = false;
        try
        {
            await _stateGate.WaitAsync(token);
            entered = true;
            ThrowIfDisposed();
            ViewerRenderBackend backend = _backendRegistry.CaptureRenderSequenceBackend() ??
                throw new NotSupportedException("No renderer is active.");
            StageRenderState original = CurrentState;
            string output = ViewerRenderSequenceRange.CreateOutputDirectory(request.OutputParent);
            ulong serial = await Scheduler.InvokeAsync(static stage => stage.ChangeSerial, token);
            RenderProductJobPlan plan = await RenderProductJobPlan.PrepareAsync(
                Scheduler,
                original.Stage,
                request.Range.Times,
                original.RenderSettings,
                request.SettingsPath,
                request.ProductPath,
                expectedStageRevision: serial,
                cancellationToken: token);
            if (backend.GetRenderProductUnsupportedReason(original, plan, request.HdrColorFormat) is { } unsupported)
            {
                throw new NotSupportedException(unsupported);
            }
            await Dispatcher.UIThread.InvokeAsync(
                () => progress(new ViewerAuthoredRenderProductProgress(
                    "Preparing", 0, plan.Frames.Count, plan.Frames[0].TimeCode)),
                DispatcherPriority.Normal, token);
            RenderDiskJobRequest job = plan.CreateJob(output, request.HdrColorFormat);
            StageRenderState restoredState = original.AdvanceRevision();
            bool restored = false;
            IViewerRenderProductCaptureLease? lease =
                await backend.BeginRenderProductCaptureAsync(plan, request.HdrColorFormat, token);
            async ValueTask restore()
            {
                if (restored)
                {
                    return;
                }
                if (lease is not null)
                {
                    await lease.DisposeAsync();
                    lease = null;
                }
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    RenderBackendManagerResult update = await _manager.UpdateStateAsync(
                        restoredState, CancellationToken.None);
                    if (!update.IsSuccess)
                    {
                        throw new InvalidOperationException("The renderer refused the pre-product view restoration.");
                    }
                    await backend.RestoreRenderSequenceFrameAsync(restoredState, CancellationToken.None);
                    Volatile.Write(ref _currentState, restoredState);
                    StateChanged?.Invoke(restoredState);
                });
                restored = true;
            }
            async ValueTask<ViewerFrameCaptureResult> capture(
                StageRenderState frame,
                CancellationToken frameToken)
            {
                IViewerRenderProductCaptureLease activeLease = lease ??
                    throw new ObjectDisposedException(nameof(IViewerRenderProductCaptureLease));
                await RequireSequenceRevisionAsync(serial, frameToken);
                ViewerFrameCaptureResult image = await await Dispatcher.UIThread.InvokeAsync(
                    async () => await activeLease.CaptureAsync(frame, frameToken),
                    DispatcherPriority.Normal,
                    frameToken);
                await RequireSequenceRevisionAsync(serial, frameToken);
                return image;
            }
            void report(ViewerRenderSequenceProgress update)
            {
                progress(new ViewerAuthoredRenderProductProgress(
                    update.Phase, update.CompletedFrames, update.TotalFrames, update.TimeCode));
            }
            void validate(RenderProductJobPlan productPlan, CancellationToken validateToken)
            {
                validateToken.ThrowIfCancellationRequested();
                if (!ReferenceEquals(plan, productPlan))
                {
                    throw new InvalidOperationException("The disk job validated a different product plan.");
                }
                if (backend.GetRenderProductUnsupportedReason(original, productPlan, request.HdrColorFormat) is
                    { } unsupported)
                {
                    throw new NotSupportedException(unsupported);
                }
            }
            try
            {
                return await ViewerRenderSequenceRunner.ExecuteAsync(job, capture, restore, report, token, validate);
            }
            finally
            {
                await restore();
            }
        }
        finally
        {
            if (entered)
            {
                _stateGate.Release();
            }
            lock (_sequenceGate)
            {
                _sequenceCancellation = null;
            }
            cancellation.Dispose();
            drained.TrySetResult();
        }
    }
}
