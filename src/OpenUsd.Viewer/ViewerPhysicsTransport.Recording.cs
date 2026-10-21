// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics;
using OpenUsd.Physics.Extraction;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Physics;

namespace OpenUsd.Viewer;

internal sealed partial class ViewerPhysicsTransportAdapter
{
    private ulong _builtFingerprintLow;
    private ulong _builtFingerprintHigh;
    private bool _hasBuiltFingerprint;
    private string? _builtRecordingRefusal;

    internal string? RecordingRefusal => _builtRecordingRefusal;

    internal async Task<ViewerPhysicsRecordingSession> CreateRecordingSessionAsync(
        PhysicsRenderBindingTable bindings, PhysicsRenderCapacities capacities, CancellationToken cancellationToken)
    {
        if (_builtRecordingRefusal is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }
        ulong before = await _scheduler.InvokeAsync(static stage => stage.ChangeSerial, cancellationToken);
        UsdPhysicsExtractionPage page = await ExtractAsync(cancellationToken).ConfigureAwait(false);
        ulong after = await _scheduler.InvokeAsync(static stage => stage.ChangeSerial, cancellationToken);
        if (before != after || !_hasBuiltFingerprint ||
            page.FingerprintLow != _builtFingerprintLow || page.FingerprintHigh != _builtFingerprintHigh)
        {
            throw new InvalidOperationException(
                "Physics inputs changed since the world was built. Rebuild before recording.");
        }
        return new ViewerPhysicsRecordingSession(_transport, bindings, capacities, after);
    }
}

internal sealed class ViewerPhysicsRecordingSession(
    UsdPhysicsTransport transport, PhysicsRenderBindingTable bindings,
    PhysicsRenderCapacities capacities, ulong sourceRevision) : IDisposable
{
    private bool _disposed;
    private readonly ulong _initialStep = transport.Status.StepIndex;

    internal ulong SourceRevision => sourceRevision;

    internal IReadOnlyList<double> CreateTimes(int count, int stepsPerFrame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(stepsPerFrame, transport.Options.MaxCatchUpSubSteps);
        if (transport.Status.State != UsdPhysicsTransportState.Paused || transport.Loop)
        {
            throw new InvalidOperationException("Exact-step recording requires paused, nonlooping physics.");
        }
        if (!transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease initial))
        {
            throw new InvalidOperationException("Physics has not published a complete initial frame.");
        }
        using (initial)
        {
            if (initial.Frame.DeformationCount != 0)
            {
                throw new NotSupportedException(
                    "Viewer recording currently supports rigid-pose publications, not deformable geometry.");
            }
        }
        var times = new double[count];
        for (int index = 0; index < count; index++)
        {
            ulong step = checked(_initialStep + (ulong)(index + 1) * (ulong)stepsPerFrame);
            double seconds = step * transport.FixedStep.Seconds;
            if (!double.IsFinite(seconds) || (transport.Timeline.HasAuthoredRange &&
                seconds > transport.Timeline.DurationSeconds + transport.FixedStep.Seconds * 1e-6))
            {
                throw new ArgumentOutOfRangeException(nameof(count),
                    "Recording would advance beyond the authored end.");
            }
            times[index] = transport.Timeline.ToTimeCode(seconds);
        }
        return Array.AsReadOnly(times);
    }

    internal Task<RenderDiskJobResult> ExecuteAsync(
        RenderDiskJobRequest request, IPhysicsRenderRecordingSource source,
        PhysicsRenderRecordingContext context, int stepsPerFrame, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (transport.Status.StepIndex != _initialStep)
        {
            throw new InvalidOperationException("Physics advanced while recording frames were being prepared.");
        }
        return Task.Factory.StartNew(
            () => PhysicsRenderRecording.Execute(request, transport, bindings, capacities, source,
                context, stepsPerFrame, cancellationToken),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void Dispose() => _disposed = true;
}
