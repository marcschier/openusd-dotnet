// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class RenderBackendManagerTests
{
    [Test]
    public async Task ProbeUnavailableFallsBackFromStormToD3D12()
    {
        var storm = Factory(
            RenderBackendKind.Storm,
            backend => backend.ProbeResult = RenderBackendProbeResult.Unavailable(
                RenderBackendProbeFailureKind.DeviceUnavailable));
        var d3d12 = Factory(RenderBackendKind.D3D12);
        await using var manager = Manager(storm, d3d12);

        RenderBackendManagerResult result = await manager.InitializeAsync(CreateState());

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(storm.Instances).Count().IsEqualTo(1);
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(1);
        await Assert.That(d3d12.Instances[0].DisposeCount).IsEqualTo(0);
        await Assert.That(result.Diagnostics.Entries.Any(
            diagnostic => diagnostic.Category == RenderBackendDiagnosticCategory.Fallback)).IsTrue();
    }

    [Test]
    public async Task InitializationFailureFallsBackFromStormToD3D12()
    {
        var storm = Factory(
            RenderBackendKind.Storm,
            backend => backend.InitializationResult = RenderBackendInitializationResult.Failed(
                RenderBackendInitializationFailureKind.DeviceCreationFailed));
        var d3d12 = Factory(RenderBackendKind.D3D12);
        await using var manager = Manager(storm, d3d12);

        RenderBackendManagerResult result = await manager.InitializeAsync(CreateState());

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task FactoryExceptionBecomesUnknownDiagnosticAndFallsBack()
    {
        var storm = Factory(RenderBackendKind.Storm);
        storm.CreateException = new InvalidOperationException("factory boom");
        var d3d12 = Factory(RenderBackendKind.D3D12);
        await using var manager = Manager(storm, d3d12);

        RenderBackendManagerResult result = await manager.InitializeAsync(CreateState());
        RenderBackendDiagnostic diagnostic = result.Diagnostics.Entries.Single(
            entry => entry.Code == "manager.factory_exception");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(diagnostic.InitializationFailure)
            .IsEqualTo(RenderBackendInitializationFailureKind.Unknown);
        await Assert.That(diagnostic.ExceptionType).Contains(nameof(InvalidOperationException));
        await Assert.That(diagnostic.ExceptionMessage).IsEqualTo("factory boom");
        await Assert.That(storm.Instances).IsEmpty();
    }

    [Test]
    public async Task ProbeExceptionBecomesUnknownDiagnosticAndFallsBack()
    {
        var storm = Factory(
            RenderBackendKind.Storm,
            backend => backend.ProbeHandler = _ =>
                ValueTask.FromException<RenderBackendProbeResult>(
                    new InvalidOperationException("probe boom")));
        var d3d12 = Factory(RenderBackendKind.D3D12);
        await using var manager = Manager(storm, d3d12);

        RenderBackendManagerResult result = await manager.InitializeAsync(CreateState());
        RenderBackendDiagnostic diagnostic = result.Diagnostics.Entries.Single(
            entry => entry.Code == "manager.probe_exception");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(diagnostic.ProbeFailure)
            .IsEqualTo(RenderBackendProbeFailureKind.Unknown);
        await Assert.That(diagnostic.ExceptionType).Contains(nameof(InvalidOperationException));
        await Assert.That(diagnostic.ExceptionMessage).IsEqualTo("probe boom");
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task InitializationExceptionBecomesUnknownDiagnosticAndFallsBack()
    {
        var storm = Factory(
            RenderBackendKind.Storm,
            backend => backend.InitializationHandler = (_, _) =>
                ValueTask.FromException<RenderBackendInitializationResult>(
                    new InvalidOperationException("initialize boom")));
        var d3d12 = Factory(RenderBackendKind.D3D12);
        await using var manager = Manager(storm, d3d12);

        RenderBackendManagerResult result = await manager.InitializeAsync(CreateState());
        RenderBackendDiagnostic diagnostic = result.Diagnostics.Entries.Single(
            entry => entry.Code == "manager.initialization_exception");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(diagnostic.InitializationFailure)
            .IsEqualTo(RenderBackendInitializationFailureKind.Unknown);
        await Assert.That(diagnostic.ExceptionType).Contains(nameof(InvalidOperationException));
        await Assert.That(diagnostic.ExceptionMessage).IsEqualTo("initialize boom");
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task D3D12FailureProgressesToVulkanOnWindows()
    {
        var storm = Factory(
            RenderBackendKind.Storm,
            backend => backend.ProbeResult = RenderBackendProbeResult.Unavailable(
                RenderBackendProbeFailureKind.RuntimeUnavailable));
        var d3d12 = Factory(
            RenderBackendKind.D3D12,
            backend => backend.InitializationResult = RenderBackendInitializationResult.Failed(
                RenderBackendInitializationFailureKind.DeviceCreationFailed));
        var vulkan = Factory(RenderBackendKind.Vulkan);
        await using var manager = Manager(storm, d3d12, vulkan);

        RenderBackendManagerResult result = await manager.InitializeAsync(CreateState());

        await Assert.That(result.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.Vulkan);
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(1);
        await Assert.That(d3d12.Instances[0].DisposeCount).IsEqualTo(1);
        await Assert.That(vulkan.Instances[0].DisposeCount).IsEqualTo(0);
    }

    [Test]
    public async Task ManualSelectionCreatesOnlyRequestedBackend()
    {
        var storm = Factory(RenderBackendKind.Storm);
        var d3d12 = Factory(RenderBackendKind.D3D12);
        var vulkan = Factory(RenderBackendKind.Vulkan);
        await using var manager = Manager(storm, d3d12, vulkan);

        RenderBackendManagerResult result = await manager.InitializeAsync(
            CreateState(),
            RenderBackendKind.Vulkan);

        await Assert.That(result.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.Vulkan);
        await Assert.That(storm.Instances).IsEmpty();
        await Assert.That(d3d12.Instances).IsEmpty();
        await Assert.That(vulkan.Instances).Count().IsEqualTo(1);
    }

    [Test]
    public async Task SwitchPreservesExactStateReference()
    {
        var storm = Factory(RenderBackendKind.Storm);
        var d3d12 = Factory(RenderBackendKind.D3D12);
        await using var manager = Manager(storm, d3d12);
        StageRenderState state = CreateState();

        _ = await manager.InitializeAsync(state);
        RenderBackendManagerResult switched = await manager.SwitchAsync(RenderBackendKind.D3D12);

        await Assert.That(switched.IsSuccess).IsTrue();
        await Assert.That(storm.Instances[0].InitializationState).IsSameReferenceAs(state);
        await Assert.That(d3d12.Instances[0].InitializationState).IsSameReferenceAs(state);
        await Assert.That(manager.CurrentState).IsSameReferenceAs(state);
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task FailedManualSwitchLeavesCurrentBackendActive()
    {
        var storm = Factory(RenderBackendKind.Storm);
        var d3d12 = Factory(
            RenderBackendKind.D3D12,
            backend => backend.InitializationResult = RenderBackendInitializationResult.Failed(
                RenderBackendInitializationFailureKind.DeviceCreationFailed));
        await using var manager = Manager(storm, d3d12);
        _ = await manager.InitializeAsync(CreateState());

        RenderBackendManagerResult switched = await manager.SwitchAsync(RenderBackendKind.D3D12);
        ManagedRenderFrameResult rendered = await manager.RenderAsync();

        await Assert.That(switched.IsSuccess).IsFalse();
        await Assert.That(manager.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.Storm);
        await Assert.That(rendered.IsSuccess).IsTrue();
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(0);
        await Assert.That(d3d12.Instances[0].DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task FailedPreviousDeactivationAbortsSwitchWithoutDuplicateActivation()
    {
        int deactivationFailures = 1;
        var storm = Factory(
            RenderBackendKind.Storm,
            backend => backend.DeactivateHandler = _ =>
                deactivationFailures-- > 0
                    ? ValueTask.FromException(
                        new InvalidOperationException("deactivate boom"))
                    : ValueTask.CompletedTask);
        var d3d12 = Factory(RenderBackendKind.D3D12);
        await using var manager = Manager(storm, d3d12);
        StageRenderState state = CreateState();
        _ = await manager.InitializeAsync(state);

        RenderBackendManagerResult switched =
            await manager.SwitchAsync(RenderBackendKind.D3D12);

        await Assert.That(switched.IsSuccess).IsFalse();
        await Assert.That(switched.Failure)
            .IsEqualTo(RenderBackendManagerFailureKind.BackendOperationFailed);
        await Assert.That(manager.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.Storm);
        await Assert.That(manager.CurrentState).IsSameReferenceAs(state);
        await Assert.That(storm.Instances[0].IsActive).IsTrue();
        await Assert.That(d3d12.Instances[0].IsActive).IsFalse();
        await Assert.That(d3d12.Instances[0].DisposeCount).IsEqualTo(1);
        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(0);
    }

    [Test]
    public async Task CandidateActivationFailureReactivatesPreviousAndDisposesCandidate()
    {
        var storm = Factory(RenderBackendKind.Storm);
        var d3d12 = Factory(
            RenderBackendKind.D3D12,
            backend => backend.ActivateHandler = _ =>
                ValueTask.FromException(
                    new InvalidOperationException("activate boom")));
        await using var manager = Manager(storm, d3d12);
        _ = await manager.InitializeAsync(CreateState());

        RenderBackendManagerResult switched =
            await manager.SwitchAsync(RenderBackendKind.D3D12);

        await Assert.That(switched.IsSuccess).IsFalse();
        await Assert.That(manager.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.Storm);
        await Assert.That(storm.Instances[0].IsActive).IsTrue();
        await Assert.That(d3d12.Instances[0].IsActive).IsFalse();
        await Assert.That(d3d12.Instances[0].DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task PreviousReactivationFailureAfterCandidateAdoptionLeavesTruthfulInactiveState()
    {
        int activationCount = 0;
        int cleanupFailures = 1;
        var storm = Factory(
            RenderBackendKind.Storm,
            backend =>
            {
                backend.ActivateHandler = _ =>
                    ++activationCount == 2
                        ? ValueTask.FromException(
                            new InvalidOperationException("reactivate boom"))
                        : ValueTask.CompletedTask;
                backend.DisposeHandler = () =>
                    cleanupFailures-- > 0
                        ? ValueTask.FromException(
                            new InvalidOperationException("rollback cleanup boom"))
                        : ValueTask.CompletedTask;
            });
        var d3d12 = Factory(
            RenderBackendKind.D3D12,
            backend => backend.ActivateHandler = _ =>
                ValueTask.FromException(
                    new InvalidOperationException("candidate activate boom")));
        var manager = Manager(storm, d3d12);
        StageRenderState state = CreateState();
        _ = await manager.InitializeAsync(state);

        RenderBackendManagerResult switched =
            await manager.SwitchAsync(RenderBackendKind.D3D12);

        await Assert.That(switched.IsSuccess).IsFalse();
        await Assert.That(switched.Failure)
            .IsEqualTo(RenderBackendManagerFailureKind.BackendOperationFailed);
        await Assert.That(switched.ActiveBackend).IsNull();
        await Assert.That(manager.ActiveBackend).IsNull();
        await Assert.That(storm.Instances[0].IsActive).IsFalse();
        await Assert.That(d3d12.Instances[0].IsActive).IsFalse();
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(1);
        await Assert.That(d3d12.Instances[0].DisposeCount).IsEqualTo(1);
        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(1);
        await Assert.That(switched.Diagnostics.Entries.Any(
            entry => entry.Code == "manager.previous_backend_reactivation_failed"))
            .IsTrue();
        await Assert.That(switched.Diagnostics.Entries.Any(
            entry => entry.Code == "manager.previous_backend_rollback_cleanup_failed"))
            .IsTrue();

        RenderBackendManagerResult inactive = await manager.UpdateStateAsync(state);

        await Assert.That(inactive.IsSuccess).IsFalse();
        await Assert.That(inactive.Failure)
            .IsEqualTo(RenderBackendManagerFailureKind.BackendOperationFailed);
        await Assert.That(inactive.ActiveBackend).IsNull();
        await Assert.That(inactive.Diagnostics.Entries.Any(
            entry => entry.Code == "manager.backend_operation_failed"))
            .IsTrue();
        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(0);
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(2);

        await manager.DisposeAsync();
    }

    [Test]
    public async Task AutomaticSwitchReturnsToPreferredAvailableBackend()
    {
        var storm = Factory(RenderBackendKind.Storm);
        var d3d12 = Factory(RenderBackendKind.D3D12);
        await using var manager = Manager(storm, d3d12);
        StageRenderState state = CreateState();
        _ = await manager.InitializeAsync(state, RenderBackendKind.D3D12);

        RenderBackendManagerResult switched = await manager.SwitchAsync();

        await Assert.That(switched.IsSuccess).IsTrue();
        await Assert.That(switched.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.Storm);
        await Assert.That(storm.Instances[0].InitializationState).IsSameReferenceAs(state);
        await Assert.That(d3d12.Instances[0].DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task StateResizeAndRenderForwardToActiveBackend()
    {
        var storm = Factory(RenderBackendKind.Storm);
        await using var manager = Manager(storm);
        _ = await manager.InitializeAsync(CreateState());
        StageRenderState updated = CreateState().WithTime(new StageTime(48));
        var viewport = new ViewportDimensions(1920, 1080);

        RenderBackendManagerResult stateResult = await manager.UpdateStateAsync(updated);
        RenderBackendManagerResult resizeResult = await manager.ResizeAsync(viewport);
        ManagedRenderFrameResult frame = await manager.RenderAsync();

        FakeBackend backend = storm.Instances[0];
        await Assert.That(stateResult.IsSuccess).IsTrue();
        await Assert.That(resizeResult.IsSuccess).IsTrue();
        await Assert.That(backend.UpdatedState).IsSameReferenceAs(updated);
        await Assert.That(backend.LastViewport).IsEqualTo(viewport);
        await Assert.That(frame.Frame!.StateRevision).IsEqualTo(updated.Revision);
    }

    [Test]
    public async Task DeviceLossDisposesLostBackendAndRendersWithFallback()
    {
        StageRenderState state = CreateState();
        var storm = Factory(
            RenderBackendKind.Storm,
            backend => backend.Frames.Enqueue(RenderFrameResult.LostDevice(
                state.Revision,
                RenderDeviceLossKind.Removed)));
        var d3d12 = Factory(RenderBackendKind.D3D12);
        await using var manager = Manager(storm, d3d12);
        _ = await manager.InitializeAsync(state);

        ManagedRenderFrameResult result = await manager.RenderAsync();

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.DidFailOver).IsTrue();
        await Assert.That(result.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(result.Frame!.Status).IsEqualTo(RenderFrameStatus.Rendered);
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(1);
        await Assert.That(d3d12.Instances[0].InitializationState).IsSameReferenceAs(state);
    }

    [Test]
    public async Task FailedCandidateCleanupFailureIsDiagnosedWithoutStoppingFallback()
    {
        int cleanupFailures = 1;
        var storm = Factory(
            RenderBackendKind.Storm,
            backend =>
            {
                backend.ProbeResult = RenderBackendProbeResult.Unavailable(
                    RenderBackendProbeFailureKind.DeviceUnavailable);
                backend.DisposeHandler = () =>
                    cleanupFailures-- > 0
                        ? ValueTask.FromException(
                            new InvalidOperationException("candidate cleanup boom"))
                        : ValueTask.CompletedTask;
            });
        var d3d12 = Factory(RenderBackendKind.D3D12);
        await using var manager = Manager(storm, d3d12);

        RenderBackendManagerResult result = await manager.InitializeAsync(CreateState());
        RenderBackendDiagnostic diagnostic = result.Diagnostics.Entries.Single(
            entry => entry.Code == "manager.candidate_cleanup_failed");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(diagnostic.Category)
            .IsEqualTo(RenderBackendDiagnosticCategory.Cleanup);
        await Assert.That(diagnostic.ExceptionType).Contains(nameof(InvalidOperationException));
        await Assert.That(diagnostic.ExceptionMessage).IsEqualTo("candidate cleanup boom");
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(1);
        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(1);

        _ = await manager.RenderAsync();

        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(0);
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(2);
    }

    [Test]
    public async Task PreviousCleanupFailureRetainsNewlyInitializedBackend()
    {
        int cleanupFailures = 1;
        var storm = Factory(
            RenderBackendKind.Storm,
            backend => backend.DisposeHandler = () =>
                cleanupFailures-- > 0
                    ? ValueTask.FromException(
                        new InvalidOperationException("previous cleanup boom"))
                    : ValueTask.CompletedTask);
        var d3d12 = Factory(RenderBackendKind.D3D12);
        await using var manager = Manager(storm, d3d12);
        _ = await manager.InitializeAsync(CreateState());

        RenderBackendManagerResult switched = await manager.SwitchAsync(RenderBackendKind.D3D12);
        RenderBackendDiagnostic diagnostic = switched.Diagnostics.Entries.Single(
            entry => entry.Code == "manager.previous_backend_cleanup_failed");

        await Assert.That(switched.IsSuccess).IsTrue();
        await Assert.That(manager.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(diagnostic.ExceptionMessage).IsEqualTo("previous cleanup boom");
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(1);
        await Assert.That(storm.Instances[0].IsActive).IsFalse();
        await Assert.That(d3d12.Instances[0].IsActive).IsTrue();
        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(1);

        ManagedRenderFrameResult rendered = await manager.RenderAsync();

        await Assert.That(rendered.IsSuccess).IsTrue();
        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(0);
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(2);
    }

    [Test]
    public async Task DeviceLossCleanupFailureIsDiagnosedAndFallbackContinues()
    {
        StageRenderState state = CreateState();
        int cleanupFailures = 1;
        var storm = Factory(
            RenderBackendKind.Storm,
            backend =>
            {
                backend.Frames.Enqueue(RenderFrameResult.LostDevice(
                    state.Revision,
                    RenderDeviceLossKind.Removed));
                backend.DisposeHandler = () =>
                    cleanupFailures-- > 0
                        ? ValueTask.FromException(
                            new InvalidOperationException("lost cleanup boom"))
                        : ValueTask.CompletedTask;
            });
        var d3d12 = Factory(RenderBackendKind.D3D12);
        await using var manager = Manager(storm, d3d12);
        _ = await manager.InitializeAsync(state);

        ManagedRenderFrameResult result = await manager.RenderAsync();
        RenderBackendDiagnostic diagnostic = result.Diagnostics.Entries.Single(
            entry => entry.Code == "manager.device_lost_cleanup_failed");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(diagnostic.Category)
            .IsEqualTo(RenderBackendDiagnosticCategory.DeviceLoss);
        await Assert.That(diagnostic.ExceptionMessage).IsEqualTo("lost cleanup boom");
        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(1);

        _ = await manager.RenderAsync();

        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(0);
    }

    [Test]
    public async Task LaterAutomaticActivationRetriesTransientBackendFailure()
    {
        int creation = 0;
        var storm = Factory(
            RenderBackendKind.Storm,
            backend =>
            {
                if (creation++ == 0)
                {
                    backend.ProbeResult = RenderBackendProbeResult.Unavailable(
                        RenderBackendProbeFailureKind.DeviceUnavailable);
                }
            });
        await using var manager = Manager(storm);

        RenderBackendManagerResult first = await manager.InitializeAsync(CreateState());
        RenderBackendManagerResult retry = await manager.SwitchAsync();

        await Assert.That(first.IsSuccess).IsFalse();
        await Assert.That(retry.IsSuccess).IsTrue();
        await Assert.That(retry.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.Storm);
        await Assert.That(storm.Instances).Count().IsEqualTo(2);
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task AllCandidatesFailWithoutLeakingBackends()
    {
        var storm = Factory(
            RenderBackendKind.Storm,
            backend => backend.ProbeResult = RenderBackendProbeResult.Unavailable(
                RenderBackendProbeFailureKind.DeviceUnavailable));
        var d3d12 = Factory(
            RenderBackendKind.D3D12,
            backend => backend.InitializationResult = RenderBackendInitializationResult.Failed(
                RenderBackendInitializationFailureKind.ResourceCreationFailed));
        var vulkan = Factory(
            RenderBackendKind.Vulkan,
            backend => backend.ProbeResult = RenderBackendProbeResult.Unavailable(
                RenderBackendProbeFailureKind.IncompatibleDriver));
        await using var manager = Manager(storm, d3d12, vulkan);

        RenderBackendManagerResult result = await manager.InitializeAsync(CreateState());
        ManagedRenderFrameResult frame = await manager.RenderAsync();

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Failure)
            .IsEqualTo(RenderBackendManagerFailureKind.NoBackendAvailable);
        await Assert.That(frame.IsSuccess).IsFalse();
        await Assert.That(frame.Failure)
            .IsEqualTo(RenderBackendManagerFailureKind.NoBackendAvailable);
        await Assert.That(manager.ActiveBackend).IsNull();
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(1);
        await Assert.That(d3d12.Instances[0].DisposeCount).IsEqualTo(1);
        await Assert.That(vulkan.Instances[0].DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task CancellationDisposesCandidateAndReleasesLifecycleGate()
    {
        var storm = Factory(
            RenderBackendKind.Storm,
            backend => backend.ProbeHandler = async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return RenderBackendProbeResult.Available();
            });
        var d3d12 = Factory(RenderBackendKind.D3D12);
        await using var manager = Manager(storm, d3d12);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        bool canceled = false;
        try
        {
            _ = await manager.InitializeAsync(CreateState(), cancellationToken: cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        RenderBackendManagerResult retry = await manager.InitializeAsync(
            CreateState(),
            RenderBackendKind.D3D12);

        await Assert.That(canceled).IsTrue();
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(1);
        await Assert.That(d3d12.Instances).Count().IsEqualTo(1);
        await Assert.That(retry.IsSuccess).IsTrue();
    }

    [Test]
    public async Task RequestedInitializationCancellationDoesNotFallback()
    {
        var initializationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var storm = Factory(
            RenderBackendKind.Storm,
            backend => backend.InitializationHandler = async (_, cancellationToken) =>
            {
                initializationEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return RenderBackendInitializationResult.Success();
            });
        var d3d12 = Factory(RenderBackendKind.D3D12);
        await using var manager = Manager(storm, d3d12);
        using var cancellation = new CancellationTokenSource();

        Task initialization = manager.InitializeAsync(
            CreateState(),
            cancellationToken: cancellation.Token).AsTask();
        await initializationEntered.Task;
        cancellation.Cancel();
        bool canceled = false;
        try
        {
            await initialization;
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        await Assert.That(canceled).IsTrue();
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(1);
        await Assert.That(d3d12.Instances).IsEmpty();
    }

    [Test]
    public async Task CanceledStateIsRolledBackBeforeALaterFailover()
    {
        StageRenderState initial = CreateState();
        StageRenderState updated = initial.WithTime(new StageTime(12));
        using var cancellation = new CancellationTokenSource();
        var storm = Factory(
            RenderBackendKind.Storm,
            backend => backend.UpdateHandler = (_, cancellationToken) =>
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            });
        var d3d12 = Factory(RenderBackendKind.D3D12);
        await using var manager = Manager(storm, d3d12);
        _ = await manager.InitializeAsync(initial);

        bool canceled = false;
        try
        {
            _ = await manager.UpdateStateAsync(updated, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        // A backend that was cancelled part-way through applying a state cannot be
        // trusted to hold it, so it is discarded and a replacement of the same kind is
        // built from the last accepted state.
        await Assert.That(canceled).IsTrue();
        await Assert.That(storm.Instances.Count).IsEqualTo(2);
        await Assert.That(storm.Instances[1].InitializationState).IsSameReferenceAs(initial);
        await Assert.That(manager.CurrentState).IsSameReferenceAs(initial);

        storm.Instances[1].Frames.Enqueue(RenderFrameResult.LostDevice(
            initial.Revision,
            RenderDeviceLossKind.Removed));
        ManagedRenderFrameResult rendered = await manager.RenderAsync();

        // A state the active backend refused is not an accepted state, so it is rolled
        // back rather than retained. Retaining it handed the very state that had just
        // been cancelled to the replacement backend, and left every consumer mirroring
        // the manager's retained state describing an image no backend had been given.
        await Assert.That(d3d12.Instances[0].InitializationState).IsSameReferenceAs(initial);
        await Assert.That(rendered.IsSuccess).IsTrue();
    }

    [Test]
    public async Task SelectionFailurePersistsForLaterInactiveOperations()
    {
        var storm = Factory(RenderBackendKind.Storm);
        await using var manager = Manager(storm);

        RenderBackendManagerResult initialized = await manager.InitializeAsync(
            CreateState(),
            RenderBackendKind.Metal);
        RenderBackendManagerResult updated = await manager.UpdateStateAsync(
            CreateState().WithTime(new StageTime(2)));

        await Assert.That(initialized.Failure)
            .IsEqualTo(RenderBackendManagerFailureKind.SelectionFailed);
        await Assert.That(updated.Failure)
            .IsEqualTo(RenderBackendManagerFailureKind.SelectionFailed);
        await Assert.That(updated.Diagnostics.Entries.Single().Code)
            .IsEqualTo("manager.selection_failed");
    }

    [Test]
    public async Task DisposalIsDeterministicIdempotentAndRejectsOperations()
    {
        var storm = Factory(RenderBackendKind.Storm);
        var manager = Manager(storm);
        _ = await manager.InitializeAsync(CreateState());

        await manager.DisposeAsync();
        await manager.DisposeAsync();
        bool rejected = false;
        try
        {
            _ = await manager.RenderAsync();
        }
        catch (ObjectDisposedException)
        {
            rejected = true;
        }

        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(1);
        await Assert.That(rejected).IsTrue();
    }

    [Test]
    public async Task FailedRetiredCleanupCanBeRetriedByManagerDisposal()
    {
        int cleanupFailures = 2;
        var storm = Factory(
            RenderBackendKind.Storm,
            backend => backend.DisposeHandler = () =>
                cleanupFailures-- > 0
                    ? ValueTask.FromException(
                        new InvalidOperationException("cleanup retry boom"))
                    : ValueTask.CompletedTask);
        var d3d12 = Factory(RenderBackendKind.D3D12);
        var manager = Manager(storm, d3d12);
        _ = await manager.InitializeAsync(CreateState());
        _ = await manager.SwitchAsync(RenderBackendKind.D3D12);

        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(1);
        await Assert.That(manager.DisposeAsync().AsTask())
            .Throws<AggregateException>();
        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(1);

        await manager.DisposeAsync();

        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(0);
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(3);
        await manager.DisposeAsync();
    }

    [Test]
    public async Task LifecycleOperationsAreSerialized()
    {
        var updateEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpdate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resizeEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var storm = Factory(
            RenderBackendKind.Storm,
            backend =>
            {
                backend.UpdateHandler = async (_, _) =>
                {
                    updateEntered.TrySetResult();
                    await releaseUpdate.Task;
                };
                backend.ResizeHandler = (_, _) =>
                {
                    resizeEntered.TrySetResult();
                    return ValueTask.CompletedTask;
                };
            });
        await using var manager = Manager(storm);
        _ = await manager.InitializeAsync(CreateState());

        Task update = manager.UpdateStateAsync(CreateState().WithTime(new StageTime(2))).AsTask();
        await updateEntered.Task;
        Task resize = manager.ResizeAsync(new ViewportDimensions(800, 600)).AsTask();
        await Task.Yield();

        await Assert.That(resizeEntered.Task.IsCompleted).IsFalse();
        releaseUpdate.TrySetResult();
        await Task.WhenAll(update, resize);
        await Assert.That(resizeEntered.Task.IsCompleted).IsTrue();
    }

    [Test]
    public async Task ConcurrentDisposeWaitsForActiveWorkAndRejectsQueuedWork()
    {
        var updateEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUpdate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var storm = Factory(
            RenderBackendKind.Storm,
            backend => backend.UpdateHandler = async (_, _) =>
            {
                updateEntered.TrySetResult();
                await releaseUpdate.Task;
            });
        var manager = Manager(storm);
        _ = await manager.InitializeAsync(CreateState());

        Task update = manager.UpdateStateAsync(
            CreateState().WithTime(new StageTime(3))).AsTask();
        await updateEntered.Task;
        Task queuedResize = manager.ResizeAsync(new ViewportDimensions(800, 600)).AsTask();
        Task dispose = manager.DisposeAsync().AsTask();
        await Task.Yield();

        await Assert.That(dispose.IsCompleted).IsFalse();
        releaseUpdate.TrySetResult();
        await update;

        bool queuedRejected = false;
        try
        {
            await queuedResize;
        }
        catch (ObjectDisposedException)
        {
            queuedRejected = true;
        }

        await dispose;
        await Assert.That(queuedRejected).IsTrue();
        await Assert.That(storm.Instances[0].DisposeCount).IsEqualTo(1);
    }

    private static StageRenderState CreateState() =>
        StageRenderState.Create(new StageIdentity("manager.usda"))
            .WithViewport(new ViewportDimensions(1280, 720));

    private static RenderBackendManager Manager(params FakeBackendFactory[] factories) =>
        new(RenderPlatform.Windows, factories);

    private static FakeBackendFactory Factory(
        RenderBackendKind kind,
        Action<FakeBackend>? configure = null) =>
        new(kind, configure);

    private sealed class FakeBackendFactory(
        RenderBackendKind kind,
        Action<FakeBackend>? configure) : IRenderBackendFactory
    {
        internal Exception? CreateException { get; set; }

        internal List<FakeBackend> Instances { get; } = [];

        public RenderBackendKind Kind { get; } = kind;

        public IRenderBackend Create()
        {
            if (CreateException is not null)
            {
                throw CreateException;
            }

            var backend = new FakeBackend(Kind);
            configure?.Invoke(backend);
            Instances.Add(backend);
            return backend;
        }
    }

    private sealed class FakeBackend(RenderBackendKind kind)
        : IRenderBackend, IRenderBackendActivationControl
    {
        internal Queue<RenderFrameResult> Frames { get; } = [];

        internal RenderBackendInitializationResult InitializationResult { get; set; } =
            RenderBackendInitializationResult.Success();

        internal Func<
            StageRenderState,
            CancellationToken,
            ValueTask<RenderBackendInitializationResult>>? InitializationHandler
        { get; set; }

        internal StageRenderState? InitializationState { get; private set; }

        internal ViewportDimensions? LastViewport { get; private set; }

        internal Func<CancellationToken, ValueTask<RenderBackendProbeResult>>? ProbeHandler { get; set; }

        internal RenderBackendProbeResult ProbeResult { get; set; } =
            RenderBackendProbeResult.Available();

        internal Func<ViewportDimensions, CancellationToken, ValueTask>? ResizeHandler { get; set; }

        internal Func<StageRenderState, CancellationToken, ValueTask>? UpdateHandler { get; set; }

        internal StageRenderState? UpdatedState { get; private set; }

        internal int DisposeCount { get; private set; }

        internal Func<ValueTask>? DisposeHandler { get; set; }

        internal Func<CancellationToken, ValueTask>? ActivateHandler { get; set; }

        internal Func<CancellationToken, ValueTask>? DeactivateHandler { get; set; }

        internal bool IsActive { get; private set; }

        public RenderBackendIdentity Identity { get; } = new(kind, kind.ToString());

        public RenderBackendCapabilities Capabilities { get; } =
            RenderBackendCapabilities.None;

        public ValueTask<RenderBackendProbeResult> ProbeAsync(
            CancellationToken cancellationToken = default) =>
            ProbeHandler is null
                ? ValueTask.FromResult(ProbeResult)
                : ProbeHandler(cancellationToken);

        public async ValueTask ActivateAsync(CancellationToken cancellationToken = default)
        {
            if (ActivateHandler is not null)
            {
                await ActivateHandler(cancellationToken);
            }
            IsActive = true;
        }

        public async ValueTask DeactivateAsync(CancellationToken cancellationToken = default)
        {
            if (DeactivateHandler is not null)
            {
                await DeactivateHandler(cancellationToken);
            }
            IsActive = false;
        }

        public ValueTask<RenderBackendInitializationResult> InitializeAsync(
            StageRenderState initialState,
            CancellationToken cancellationToken = default)
        {
            InitializationState = initialState;
            UpdatedState = initialState;
            return InitializationHandler is null
                ? ValueTask.FromResult(InitializationResult)
                : InitializationHandler(initialState, cancellationToken);
        }

        public ValueTask UpdateStateAsync(
            StageRenderState state,
            CancellationToken cancellationToken = default)
        {
            UpdatedState = state;
            return UpdateHandler is null
                ? ValueTask.CompletedTask
                : UpdateHandler(state, cancellationToken);
        }

        public ValueTask ResizeAsync(
            ViewportDimensions viewport,
            CancellationToken cancellationToken = default)
        {
            LastViewport = viewport;
            return ResizeHandler is null
                ? ValueTask.CompletedTask
                : ResizeHandler(viewport, cancellationToken);
        }

        public ValueTask<RenderFrameResult> RenderAsync(
            CancellationToken cancellationToken = default)
        {
            RenderFrameResult frame = Frames.Count == 0
                ? RenderFrameResult.Rendered(
                    UpdatedState?.Revision ?? 0,
                    RenderFrameStatistics.Empty)
                : Frames.Dequeue();
            return ValueTask.FromResult(frame);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return DisposeHandler is null
                ? ValueTask.CompletedTask
                : DisposeHandler();
        }
    }
}
