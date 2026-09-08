// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Threading;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed partial class ViewerRenderCoordinator
{
    private readonly object _sequenceGate = new();
    private CancellationTokenSource? _sequenceCancellation;
    private Task _sequenceDrained = Task.CompletedTask;

    internal string? GetRenderSequenceUnsupportedReason(ViewerRenderSequenceOutputOptions outputs) =>
        _backendRegistry.CaptureRenderSequenceBackend() is { } backend
            ? backend.GetRenderSequenceUnsupportedReason(CurrentState, outputs)
            : "No renderer is active.";

    internal Task<RenderDiskJobResult> RenderSequenceAsync(
        ViewerRenderSequenceRange range,
        string outputParent,
        ViewerRenderSequenceOutputOptions outputs,
        string? cameraPath,
        Action<ViewerRenderSequenceProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(progress);
        ThrowIfDisposed();
        lock (_sequenceGate)
        {
            ThrowIfDisposed();
            if (!_sequenceDrained.IsCompleted)
            {
                throw new InvalidOperationException("An image sequence already owns this renderer.");
            }
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
            var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _sequenceCancellation = cancellation;
            _sequenceDrained = drained.Task;
            return RenderSequenceCoreAsync(range, outputParent, outputs, cameraPath, progress, cancellation, drained);
        }
    }

    internal Task CancelAndDrainRenderSequenceAsync()
    {
        lock (_sequenceGate)
        {
            _sequenceCancellation?.Cancel();
            return _sequenceDrained;
        }
    }

    private async Task<RenderDiskJobResult> RenderSequenceCoreAsync(
        ViewerRenderSequenceRange range, string outputParent, ViewerRenderSequenceOutputOptions outputs,
        string? cameraPath,
        Action<ViewerRenderSequenceProgress> progress, CancellationTokenSource cancellation,
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
            if (backend.GetRenderSequenceUnsupportedReason(original, outputs) is { } unsupported)
            {
                throw new NotSupportedException(unsupported);
            }
            string output = ViewerRenderSequenceRange.CreateOutputDirectory(outputParent);
            ulong serial = await Scheduler.InvokeAsync(static stage => stage.ChangeSerial, token);
            var frames = new StageRenderState[range.Times.Count];
            var cameraSource = new ViewerSchedulerStageCameraSource(Scheduler);
            StageRenderState state = original;
            for (int index = 0; index < frames.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                double time = range.Times[index];
                state = state.WithTime(new StageTime(time));
                if (cameraPath is not null)
                {
                    ViewerStageCameraQueryResult camera = await cameraSource.QueryAsync(
                        new ViewerStageCameraRequest(cameraPath, time), token);
                    if (camera.Outcome != ViewerStageCameraQueryOutcome.Ready ||
                        camera.Snapshot.PrimPath != cameraPath || camera.Snapshot.TimeCode != time)
                    {
                        throw new InvalidOperationException(
                            camera.Error ?? "The authored camera did not return the requested time sample.");
                    }
                    state = state.WithCamera(StageCameraProjectionMath.CreateCameraState(
                        camera.Snapshot.WorldToView, camera.Snapshot.Optics, original.Viewport));
                }
                frames[index] = state = state.AdvanceRevision();
                await Dispatcher.UIThread.InvokeAsync(
                    () => progress(new ViewerRenderSequenceProgress("Preparing", index + 1, frames.Length, time)),
                    DispatcherPriority.Normal, token);
            }
            await RequireSequenceRevisionAsync(serial, token);
            var request = new RenderDiskJobRequest(
                output, frames, outputs.IncludeDeviceDepth, outputs.IncludeHdrColor, outputs.HdrColorFormat);
            StageRenderState restoredState = state
                .WithTime(original.Time).WithCamera(original.Camera).AdvanceRevision();
            bool restored = false;
            async ValueTask restore()
            {
                if (restored)
                {
                    return;
                }
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    RenderBackendManagerResult update = await _manager.UpdateStateAsync(
                        restoredState, CancellationToken.None);
                    if (!update.IsSuccess)
                    {
                        throw new InvalidOperationException("The renderer refused the pre-sequence view restoration.");
                    }
                    await backend.RestoreRenderSequenceFrameAsync(restoredState, CancellationToken.None);
                    Volatile.Write(ref _currentState, restoredState);
                    StateChanged?.Invoke(restoredState);
                });
                restored = true;
            }
            async ValueTask<ViewerFrameCaptureResult> capture(StageRenderState frame, CancellationToken frameToken)
            {
                await RequireSequenceRevisionAsync(serial, frameToken);
                ViewerFrameCaptureResult image = await await Dispatcher.UIThread.InvokeAsync(
                    async () => await backend.CaptureRenderSequenceFrameAsync(frame, outputs, frameToken),
                    DispatcherPriority.Normal, frameToken);
                await RequireSequenceRevisionAsync(serial, frameToken);
                return image;
            }
            try
            {
                return await ViewerRenderSequenceRunner.ExecuteAsync(request, capture, restore, progress, token);
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

    private async ValueTask RequireSequenceRevisionAsync(ulong expected, CancellationToken cancellationToken)
    {
        ulong actual = await Scheduler.InvokeAsync(static stage => stage.ChangeSerial, cancellationToken);
        if (actual != expected)
        {
            throw new InvalidOperationException(
                "The stage changed during the sequence. No sequence was published; retry after its writers are idle.");
        }
    }
}
