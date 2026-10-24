// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed partial class ViewerPhysicsController
{
    private int _recording;
    private int _renderPumping;
    private int _runtimeSubmissions;

    internal string? RecordingUnsupportedReason => GetRecordingUnsupportedReason(includeBusy: true);

    private string? GetRecordingUnsupportedReason(bool includeBusy)
    {
        if (Volatile.Read(ref _disposed) != 0 || _transport is not ViewerPhysicsTransportAdapter adapter)
        {
            return "Enable the native physics transport before recording.";
        }
        if (includeBusy && IsCommandInFlight)
        {
            return "Wait for the current physics command before recording.";
        }
        if (_isPlaying || _loop || _previewEnabled || _previewClearPending)
        {
            return "Pause physics and turn off Loop and Apply Preview before recording.";
        }
        if (adapter.RecordingRefusal is { } refusal)
        {
            return refusal;
        }
        return _transport.Status.State != ViewerPhysicsRunState.Paused
            ? "Build a valid paused physics world before recording."
            : null;
    }

    internal async Task<RenderDiskJobResult> RecordAsync(
        Func<ViewerPhysicsRecordingSession, CancellationToken, Task<RenderDiskJobResult>> record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (RecordingUnsupportedReason is { } unsupported)
        {
            throw new InvalidOperationException(unsupported);
        }
        using CancellationTokenSource linked = LinkLifetime(cancellationToken);
        if (!await EnterAsync(linked.Token).ConfigureAwait(false))
        {
            throw new OperationCanceledException("Physics recording could not acquire the controller.", linked.Token);
        }
        try
        {
            if (GetRecordingUnsupportedReason(includeBusy: false) is { } changed)
            {
                throw new InvalidOperationException(changed);
            }
            Volatile.Write(ref _recording, 1);
            using var drain = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            drain.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                while (Volatile.Read(ref _renderPumping) != 0 || Volatile.Read(ref _runtimeSubmissions) != 0)
                {
                    await Task.Delay(1, drain.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!linked.IsCancellationRequested && drain.IsCancellationRequested)
            {
                throw new TimeoutException("The live physics bridge did not drain before recording.");
            }
            var adapter = (ViewerPhysicsTransportAdapter)_transport!;
            using ViewerPhysicsRecordingSession session = await adapter.CreateRecordingSessionAsync(
                _bridge.Bindings, _bridge.Channel.Capacities, linked.Token).ConfigureAwait(false);
            return await record(session, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _pacer?.Reset();
            RequestOverrideClear();
            Volatile.Write(ref _recording, 0);
            Exit();
        }
    }
}
