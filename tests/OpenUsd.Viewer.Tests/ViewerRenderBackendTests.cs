// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerRenderBackendTests
{
    [Test]
    public async Task HostedBackendAttachesDuringInitializationAndForwardsExactState()
    {
        var host = new FakeHost();
        var backend = new ViewerRenderBackend(RenderBackendKind.D3D12, host);
        StageRenderState initial = StageRenderState.Create(new StageIdentity("stage.usda"));
        StageRenderState revised = initial.AdvanceRevision();

        RenderBackendProbeResult probe = await backend.ProbeAsync();
        RenderBackendInitializationResult initialized = await backend.InitializeAsync(initial);
        await backend.UpdateStateAsync(revised);
        await backend.ResizeAsync(new ViewportDimensions(800, 600));
        RenderFrameResult frame = await backend.RenderAsync();
        await backend.DisposeAsync();

        await Assert.That(probe.IsAvailable).IsTrue();
        await Assert.That(initialized.IsSuccess).IsTrue();
        await Assert.That(host.AttachCount).IsEqualTo(1);
        await Assert.That(host.AttachedState).IsSameReferenceAs(initial);
        await Assert.That(host.Session!.CurrentState).IsSameReferenceAs(revised);
        await Assert.That(host.Session.LastViewport)
            .IsEqualTo(new ViewportDimensions(800, 600));
        await Assert.That(frame.StateRevision).IsEqualTo(revised.Revision);
        await Assert.That(host.Session.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task TypedHostInitializationFailureIsReturnedWithoutLeakingSession()
    {
        var host = new FakeHost
        {
            AttachFailure = new ViewerBackendInitializationException(
                RenderBackendInitializationFailureKind.SurfaceCreationFailed,
                Diagnostics(
                    RenderBackendKind.D3D12,
                    "test.surface",
                    "No compatible surface."))
        };
        await using var backend = new ViewerRenderBackend(RenderBackendKind.D3D12, host);

        RenderBackendInitializationResult result = await backend.InitializeAsync(
            StageRenderState.Create(new StageIdentity("stage.usda")));

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Failure)
            .IsEqualTo(RenderBackendInitializationFailureKind.SurfaceCreationFailed);
        await Assert.That(result.Diagnostics.Entries[0].Code).IsEqualTo("test.surface");
        await Assert.That(host.Session).IsNull();
    }

    [Test]
    public async Task ManagerFallbackAndManualSwitchPreserveExactSnapshot()
    {
        var host = new FakeHost();
        host.Unavailable.Add(RenderBackendKind.Storm);
        StageRenderState state = StageRenderState.Create(new StageIdentity("stage.usda"))
            .WithTime(new StageTime(24));
        await using var manager = CreateManager(host);

        RenderBackendManagerResult initialized = await manager.InitializeAsync(state);
        FakeSession d3d12 = host.Sessions[RenderBackendKind.D3D12];
        RenderBackendManagerResult switched = await manager.SwitchAsync(RenderBackendKind.Vulkan);

        await Assert.That(initialized.ActiveBackend!.Kind)
            .IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(host.AttachedStates[RenderBackendKind.D3D12])
            .IsSameReferenceAs(state);
        await Assert.That(switched.ActiveBackend!.Kind)
            .IsEqualTo(RenderBackendKind.Vulkan);
        await Assert.That(host.AttachedStates[RenderBackendKind.Vulkan])
            .IsSameReferenceAs(state);
        await Assert.That(manager.CurrentState).IsSameReferenceAs(state);
        await Assert.That(d3d12.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task SuccessfulSwitchReappliesFullSelectionToTheNewSession()
    {
        var host = new FakeHost();
        var selectedItem = new SelectionItem(
            "/World/Prototype",
            "/World/Instances",
            instanceIndex: 4,
            elementIndex: 8);
        StageRenderState state = StageRenderState
            .Create(new StageIdentity("stage.usda"))
            .WithSelection(new SelectionState([selectedItem]));
        await using var manager = CreateManager(host);
        _ = await manager.InitializeAsync(state, RenderBackendKind.Storm);

        RenderBackendManagerResult switched =
            await manager.SwitchAsync(RenderBackendKind.Vulkan);
        RenderBackendManagerResult reapplied =
            await ViewerRenderCoordinator.ReapplyCurrentStateAfterSwitchAsync(
                manager,
                state,
                switched);

        FakeSession vulkan = host.Sessions[RenderBackendKind.Vulkan];
        await Assert.That(reapplied.IsSuccess).IsTrue();
        await Assert.That(vulkan.CurrentState.Selection.Items[0]).IsEqualTo(selectedItem);
        await Assert.That(vulkan.UpdateCount).IsEqualTo(1);
    }

    [Test]
    public async Task DeviceLossDisposesLostHostAndContinuesWithVulkan()
    {
        var host = new FakeHost();
        host.Unavailable.Add(RenderBackendKind.Storm);
        host.DeviceLoss.Add(RenderBackendKind.D3D12);
        StageRenderState state = StageRenderState.Create(new StageIdentity("stage.usda"));
        await using var manager = CreateManager(host);
        await manager.InitializeAsync(state);

        ManagedRenderFrameResult result = await manager.RenderAsync();

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.DidFailOver).IsTrue();
        await Assert.That(result.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.Vulkan);
        await Assert.That(host.Sessions[RenderBackendKind.D3D12].DisposeCount).IsEqualTo(1);
        await Assert.That(host.AttachedStates[RenderBackendKind.Vulkan])
            .IsSameReferenceAs(state);
    }

    [Test]
    public async Task DestroyWindowFailureRetainsHiddenOwnerUntilRenderRetry()
    {
        var host = new FakeHost();
        host.DisposeFailures[RenderBackendKind.Storm] = 1;
        StageRenderState state = StageRenderState.Create(new StageIdentity("stage.usda"));
        await using var manager = CreateManager(host);
        _ = await manager.InitializeAsync(state, RenderBackendKind.Storm);

        RenderBackendManagerResult switched =
            await manager.SwitchAsync(RenderBackendKind.D3D12);

        await Assert.That(switched.IsSuccess).IsTrue();
        await Assert.That(manager.CurrentState).IsSameReferenceAs(state);
        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(1);
        await Assert.That(host.Sessions.Values.Count(session => session.IsActive))
            .IsEqualTo(1);
        await Assert.That(host.Sessions[RenderBackendKind.Storm].IsActive).IsFalse();
        await Assert.That(host.Sessions[RenderBackendKind.D3D12].IsActive).IsTrue();
        await Assert.That(switched.Diagnostics.Entries.Any(
            diagnostic => diagnostic.Code == "manager.previous_backend_cleanup_failed"))
            .IsTrue();

        ManagedRenderFrameResult rendered = await manager.RenderAsync();

        await Assert.That(rendered.IsSuccess).IsTrue();
        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(0);
        await Assert.That(host.Sessions[RenderBackendKind.Storm].DisposeCount).IsEqualTo(2);
        await Assert.That(host.Sessions.Values.Count(session => session.IsActive))
            .IsEqualTo(1);
    }

    [Test]
    public async Task FailedStormSetupRetainsOwnerUntilRepeatedCleanupRecovers()
    {
        var host = new FakeHost();
        host.InitializationFailuresAfterAttach.Add(RenderBackendKind.Storm);
        host.DisposeFailures[RenderBackendKind.Storm] = 2;
        StageRenderState state = StageRenderState.Create(new StageIdentity("stage.usda"));
        var manager = CreateManager(host);
        _ = await manager.InitializeAsync(state, RenderBackendKind.D3D12);

        RenderBackendManagerResult failed =
            await manager.SwitchAsync(RenderBackendKind.Storm);
        FakeSession failedStorm = host.Sessions[RenderBackendKind.Storm];

        await Assert.That(failed.IsSuccess).IsFalse();
        await Assert.That(failed.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(manager.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(host.Sessions[RenderBackendKind.D3D12].IsActive).IsTrue();
        await Assert.That(failedStorm.IsActive).IsFalse();
        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(1);
        await Assert.That(failedStorm.DisposeCount).IsEqualTo(1);
        await Assert.That(host.FactoryCalls[RenderBackendKind.Storm]).IsEqualTo(1);
        await Assert.That(host.StormWindowCount).IsEqualTo(1);
        await Assert.That(host.StormWindowPeak).IsEqualTo(1);
        await Assert.That(host.Sessions.Values.Count(session => session.IsActive))
            .IsEqualTo(1);

        RenderBackendManagerResult quarantined =
            await manager.SwitchAsync(RenderBackendKind.Storm);

        await Assert.That(quarantined.IsSuccess).IsFalse();
        await Assert.That(quarantined.Failure)
            .IsEqualTo(RenderBackendManagerFailureKind.CleanupPending);
        await Assert.That(quarantined.ActiveBackend!.Kind)
            .IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(failedStorm.DisposeCount).IsEqualTo(2);
        await Assert.That(host.FactoryCalls[RenderBackendKind.Storm]).IsEqualTo(1);
        await Assert.That(host.StormWindowCount).IsEqualTo(1);
        await Assert.That(host.StormWindowPeak).IsEqualTo(1);

        host.InitializationFailuresAfterAttach.Remove(RenderBackendKind.Storm);
        ManagedRenderFrameResult recovered = await manager.RenderAsync();

        await Assert.That(recovered.IsSuccess).IsTrue();
        await Assert.That(recovered.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(0);
        await Assert.That(failedStorm.DisposeCount).IsEqualTo(3);
        await Assert.That(host.StormWindowCount).IsEqualTo(0);

        host.DisposeFailures[RenderBackendKind.Storm] = 0;
        RenderBackendManagerResult switched =
            await manager.SwitchAsync(RenderBackendKind.Storm);

        await Assert.That(switched.IsSuccess).IsTrue();
        await Assert.That(switched.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.Storm);
        await Assert.That(host.FactoryCalls[RenderBackendKind.Storm]).IsEqualTo(2);
        await Assert.That(host.StormWindowCount).IsEqualTo(1);
        await Assert.That(host.StormWindowPeak).IsEqualTo(1);
        await Assert.That(host.Sessions.Values.Count(session => session.IsActive))
            .IsEqualTo(1);

        await manager.DisposeAsync();

        await Assert.That(host.StormWindowCount).IsEqualTo(0);
        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(0);
        await Assert.That(host.Sessions.Values.Count(session => session.IsActive))
            .IsEqualTo(0);
    }

    [Test]
    public async Task RetiredKindIsQuarantinedUntilPersistentCleanupRecovers()
    {
        var host = new FakeHost();
        host.PersistentDisposeFailures.Add(RenderBackendKind.Storm);
        StageRenderState state = StageRenderState.Create(new StageIdentity("stage.usda"));
        var manager = CreateManager(host);
        _ = await manager.InitializeAsync(state, RenderBackendKind.Storm);

        RenderBackendManagerResult switched =
            await manager.SwitchAsync(RenderBackendKind.D3D12);
        RenderBackendManagerResult manualStorm =
            await manager.SwitchAsync(RenderBackendKind.Storm);
        RenderBackendManagerResult automatic = await manager.SwitchAsync();

        await Assert.That(switched.IsSuccess).IsTrue();
        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(1);
        await Assert.That(manualStorm.IsSuccess).IsFalse();
        await Assert.That(manualStorm.Failure)
            .IsEqualTo(RenderBackendManagerFailureKind.CleanupPending);
        await Assert.That(manualStorm.Diagnostics.Entries.Any(
            diagnostic => diagnostic.Code == "manager.backend_cleanup_pending"))
            .IsTrue();
        await Assert.That(automatic.IsSuccess).IsTrue();
        await Assert.That(automatic.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(host.FactoryCalls[RenderBackendKind.Storm]).IsEqualTo(1);
        await Assert.That(host.AttachCalls[RenderBackendKind.Storm]).IsEqualTo(1);
        await Assert.That(manager.GetCandidateSelectionCount(RenderBackendKind.Storm))
            .IsEqualTo(1);
        await Assert.That(manager.GetFactoryCreationCount(RenderBackendKind.Storm))
            .IsEqualTo(1);
        await Assert.That(host.FactoryCalls.Values.Sum()).IsEqualTo(2);
        await Assert.That(host.StormWindowCount).IsEqualTo(1);
        await Assert.That(host.StormWindowPeak).IsEqualTo(1);

        host.PersistentDisposeFailures.Remove(RenderBackendKind.Storm);
        ManagedRenderFrameResult recovered = await manager.RenderAsync();
        RenderBackendManagerResult reactivated =
            await manager.SwitchAsync(RenderBackendKind.Storm);

        await Assert.That(recovered.IsSuccess).IsTrue();
        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(0);
        await Assert.That(host.StormWindowCount).IsEqualTo(1);
        await Assert.That(host.StormWindowPeak).IsEqualTo(1);
        await Assert.That(reactivated.IsSuccess).IsTrue();
        await Assert.That(reactivated.ActiveBackend!.Kind).IsEqualTo(RenderBackendKind.Storm);
        await Assert.That(host.FactoryCalls[RenderBackendKind.Storm]).IsEqualTo(2);
        await Assert.That(host.AttachCalls[RenderBackendKind.Storm]).IsEqualTo(2);
        await Assert.That(manager.GetCandidateSelectionCount(RenderBackendKind.Storm))
            .IsEqualTo(2);
        await Assert.That(manager.GetFactoryCreationCount(RenderBackendKind.Storm))
            .IsEqualTo(2);

        await manager.DisposeAsync();

        await Assert.That(host.StormWindowCount).IsEqualTo(0);
        await Assert.That(manager.RetiredCleanupCount).IsEqualTo(0);
    }

    [Test]
    public async Task BackendDisposeFailureRetainsSessionForDirectRetry()
    {
        var host = new FakeHost();
        host.DisposeFailures[RenderBackendKind.Storm] = 1;
        var backend = new ViewerRenderBackend(RenderBackendKind.Storm, host);
        _ = await backend.InitializeAsync(
            StageRenderState.Create(new StageIdentity("stage.usda")));

        await Assert.That(backend.DisposeAsync().AsTask())
            .Throws<InvalidOperationException>();
        RenderFrameResult frame = await backend.RenderAsync();
        await backend.DisposeAsync();

        await Assert.That(frame.Status).IsEqualTo(RenderFrameStatus.Rendered);
        await Assert.That(host.Session!.DisposeCount).IsEqualTo(2);
    }

    private static RenderBackendManager CreateManager(FakeHost host) =>
        new(
            RenderPlatform.Windows,
            Enum.GetValues<RenderBackendKind>()
                .Select(kind => new CountingFactory(kind, host)));

    private static RenderBackendDiagnostics Diagnostics(
        RenderBackendKind kind,
        string code,
        string message) =>
        new(
        [
            new RenderBackendDiagnostic(
                kind,
                RenderDiagnosticSeverity.Error,
                RenderBackendDiagnosticCategory.Initialization,
                code,
                message)
        ]);

    private sealed class FakeHost : IViewerRenderBackendHost
    {
        internal HashSet<RenderBackendKind> Unavailable { get; } = [];

        internal HashSet<RenderBackendKind> DeviceLoss { get; } = [];

        internal Dictionary<RenderBackendKind, int> DisposeFailures { get; } = [];

        internal HashSet<RenderBackendKind> PersistentDisposeFailures { get; } = [];

        internal HashSet<RenderBackendKind> InitializationFailuresAfterAttach { get; } = [];

        internal Dictionary<RenderBackendKind, FakeSession> Sessions { get; } = [];

        internal Dictionary<RenderBackendKind, int> FactoryCalls { get; } = [];

        internal Dictionary<RenderBackendKind, int> AttachCalls { get; } = [];

        internal Dictionary<RenderBackendKind, StageRenderState> AttachedStates { get; } = [];

        internal int AttachCount { get; private set; }

        internal StageRenderState? AttachedState { get; private set; }

        internal FakeSession? Session { get; private set; }

        internal Exception? AttachFailure { get; init; }

        internal int StormWindowCount { get; private set; }

        internal int StormWindowPeak { get; private set; }

        public ValueTask<RenderBackendProbeResult> ProbeAsync(
            RenderBackendKind kind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                Unavailable.Contains(kind)
                    ? RenderBackendProbeResult.Unavailable(
                        RenderBackendProbeFailureKind.DeviceUnavailable)
                    : RenderBackendProbeResult.Available());
        }

        public ValueTask<IViewerRenderBackendSession> AttachAsync(
            RenderBackendKind kind,
            StageRenderState initialState,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AttachCount++;
            AttachCalls[kind] = AttachCalls.GetValueOrDefault(kind) + 1;
            AttachedState = initialState;
            AttachedStates[kind] = initialState;
            if (AttachFailure is not null)
            {
                throw AttachFailure;
            }

            var session = new FakeSession(
                this,
                kind,
                DeviceLoss.Contains(kind),
                initialState,
                DisposeFailures.GetValueOrDefault(kind));
            Session = session;
            Sessions[kind] = session;
            if (InitializationFailuresAfterAttach.Contains(kind))
            {
                throw new ViewerBackendInitializationException(
                    RenderBackendInitializationFailureKind.ResourceCreationFailed,
                    Diagnostics(
                        kind,
                        "test.post_attach_setup",
                        "Post-attach managed setup failed."),
                    cleanupOwner: session);
            }
            return ValueTask.FromResult<IViewerRenderBackendSession>(session);
        }

        internal void AttachWindow(RenderBackendKind kind)
        {
            if (kind != RenderBackendKind.Storm)
            {
                return;
            }
            StormWindowCount++;
            StormWindowPeak = Math.Max(StormWindowPeak, StormWindowCount);
        }

        internal void ReleaseWindow(RenderBackendKind kind)
        {
            if (kind == RenderBackendKind.Storm)
            {
                StormWindowCount--;
            }
        }
    }

    private sealed class FakeSession(
        FakeHost host,
        RenderBackendKind kind,
        bool deviceLoss,
        StageRenderState initialState,
        int disposeFailures) : IViewerRenderBackendSession
    {
        private StageRenderState _state = initialState;
        private int _disposeFailures = disposeFailures;
        private bool _windowOwned = AttachWindow(host, kind);

        internal int DisposeCount { get; private set; }

        internal int UpdateCount { get; private set; }

        internal bool IsActive { get; private set; }

        internal ViewportDimensions LastViewport { get; private set; }

        public RenderBackendDiagnostics Diagnostics { get; } =
            RenderBackendDiagnostics.Empty;

        public StageRenderState CurrentState => _state;

        public ValueTask ActivateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsActive = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask DeactivateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsActive = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateStateAsync(
            StageRenderState state,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _state = state;
            UpdateCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResizeAsync(
            ViewportDimensions viewport,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastViewport = viewport;
            return ValueTask.CompletedTask;
        }

        public ValueTask<RenderFrameResult> RenderAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderFrameResult result = deviceLoss
                ? RenderFrameResult.LostDevice(
                    CurrentState.Revision,
                    RenderDeviceLossKind.Removed)
                : RenderFrameResult.Rendered(
                    CurrentState.Revision,
                    RenderFrameStatistics.Empty);
            return ValueTask.FromResult(result);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (host.PersistentDisposeFailures.Contains(kind) || _disposeFailures > 0)
            {
                if (_disposeFailures > 0)
                {
                    _disposeFailures--;
                }
                throw new InvalidOperationException(
                    "The test-only DestroyWindow cleanup failpoint was triggered.");
            }
            if (_windowOwned)
            {
                _windowOwned = false;
                host.ReleaseWindow(kind);
            }
            return ValueTask.CompletedTask;
        }

        private static bool AttachWindow(FakeHost host, RenderBackendKind kind)
        {
            host.AttachWindow(kind);
            return kind == RenderBackendKind.Storm;
        }
    }

    private sealed class CountingFactory(
        RenderBackendKind kind,
        FakeHost host) : IRenderBackendFactory
    {
        public RenderBackendKind Kind { get; } = kind;

        public IRenderBackend Create()
        {
            host.FactoryCalls[Kind] = host.FactoryCalls.GetValueOrDefault(Kind) + 1;
            return new ViewerRenderBackend(Kind, host);
        }
    }
}
