// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;
using System.Runtime.CompilerServices;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Drives the production coordinator's state mutation against a backend that really
/// fails, and reads what the coordinator publishes.
/// </summary>
/// <remarks>
/// <para>
/// The defect this pins is that the coordinator used to publish its new state and raise
/// <c>StateChanged</c> <em>before</em> telling the backend. A backend that threw or was
/// cancelled therefore left the coordinator claiming a state no renderer had ever been
/// given -- and every consumer that mirrors that state into a menu item, a persisted
/// setting, or a cached transform key mirrored the claim rather than the image.
/// </para>
/// <para>
/// The coordinator's constructor needs a live stage, so the instance here is materialized
/// without it and only the fields the mutation path touches are set. Everything executed
/// afterwards is the production method.
/// </para>
/// </remarks>
public sealed class ViewerCoordinatorTransactionTests
{
    private static readonly RenderDisplayTransform Transform = new(
        OperatingSystem.IsWindows() ? @"C:\configs\a.ocio" : "/configs/a.ocio",
        "linear",
        "sRGB",
        "view");

    [Test]
    public async Task ASuccessfulMutationPublishesTheStateAndReportsIt()
    {
        var host = new FailingHost();
        StageRenderState initial = StageRenderState.Create(new StageIdentity("stage.usda"));
        RenderBackendManager manager = CreateManager(host);
        _ = await manager.InitializeAsync(initial);

        ViewerRenderCoordinator coordinator = CreateCoordinator(manager, initial);
        StageRenderState? observed = null;
        coordinator.StateChanged += state => observed = state;

        ViewerStateMutationResult result = await coordinator.TryMutateStateAsync(
            state => ViewerViewportStateMutation.WithDisplayTransform(state, Transform));

        await Assert.That(result.Applied).IsTrue();
        await Assert.That(result.Changed).IsTrue();
        await Assert.That(result.PublishedState.RenderSettings.DisplayTransform)
            .IsEqualTo(Transform);
        await Assert.That(coordinator.CurrentState.RenderSettings.DisplayTransform)
            .IsEqualTo(Transform);
        await Assert.That(observed).IsNotNull();
        await Assert.That(host.Session!.UpdateCount).IsEqualTo(1);
    }

    [Test]
    [Arguments("throw")]
    [Arguments("cancel")]
    public async Task AFailingBackendLeavesTheCoordinatorHoldingItsPreviousState(string mode)
    {
        var host = new FailingHost();
        StageRenderState initial = StageRenderState.Create(new StageIdentity("stage.usda"));
        RenderBackendManager manager = CreateManager(host);
        _ = await manager.InitializeAsync(initial);

        ViewerRenderCoordinator coordinator = CreateCoordinator(manager, initial);
        int stateChanges = 0;
        coordinator.StateChanged += _ => stateChanges++;
        host.Sessions[RenderBackendKind.D3D12].FailWith = mode == "throw"
            ? new InvalidOperationException("the device refused the state")
            : new OperationCanceledException();

        // Apply: the backend refuses the transform.
        Exception? thrown = null;
        try
        {
            _ = await coordinator.TryMutateStateAsync(
                state => ViewerViewportStateMutation.WithDisplayTransform(state, Transform));
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        await Assert.That(thrown).IsNotNull();
        await Assert.That(coordinator.CurrentState.RenderSettings.DisplayTransform).IsNull();
        await Assert.That(stateChanges).IsEqualTo(0);

        // The colour-management commit must therefore not move: the menu, the persisted
        // choice, and the cached key all keep describing the image that is on screen, and
        // the request becomes a deferred one.
        var requested = new ViewerColorManagement
        {
            Enabled = true,
            ConfigPath = Transform.ConfigPath,
            Display = "sRGB",
            View = "view",
        };
        ViewerColorManagementCommit decision = ViewerColorManagementCommit.Decide(
            requested with { Enabled = false },
            committedTransformKey:
                coordinator.CurrentState.RenderSettings.DisplayTransform?.CacheKey,
            committedStateTransform: coordinator.CurrentState.RenderSettings.DisplayTransform,
            requested,
            validated: Transform,
            diagnostic: null,
            applied: false);

        await Assert.That(decision.Committed.Enabled).IsFalse();
        await Assert.That(decision.CommittedTransformKey).IsNull();
        await Assert.That(decision.StateTransform).IsNull();
        await Assert.That(decision.Deferred!.Enabled).IsTrue();
        await Assert.That(decision.IsConsistent).IsTrue();
    }

    [Test]
    [Arguments("throw")]
    [Arguments("cancel")]
    public async Task AFailingBackendCannotClearATransformThatIsStillRunning(string mode)
    {
        var host = new FailingHost();
        StageRenderState initial = StageRenderState.Create(new StageIdentity("stage.usda"));
        RenderBackendManager manager = CreateManager(host);
        _ = await manager.InitializeAsync(initial);

        ViewerRenderCoordinator coordinator = CreateCoordinator(manager, initial);
        ViewerStateMutationResult applied = await coordinator.TryMutateStateAsync(
            state => ViewerViewportStateMutation.WithDisplayTransform(state, Transform));
        await Assert.That(applied.Applied).IsTrue();

        host.Sessions[RenderBackendKind.D3D12].FailWith = mode == "throw"
            ? new InvalidOperationException("the device refused the state")
            : new OperationCanceledException();

        // Clear: the backend refuses, so the transform is still the one being rendered.
        Exception? thrown = null;
        try
        {
            _ = await coordinator.TryMutateStateAsync(
                static state => ViewerViewportStateMutation.WithDisplayTransform(state, null));
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        await Assert.That(thrown).IsNotNull();
        await Assert.That(coordinator.CurrentState.RenderSettings.DisplayTransform)
            .IsEqualTo(Transform);

        var enabled = new ViewerColorManagement
        {
            Enabled = true,
            ConfigPath = Transform.ConfigPath,
            Display = "sRGB",
            View = "view",
        };
        ViewerColorManagementCommit decision = ViewerColorManagementCommit.Decide(
            enabled,
            committedTransformKey: Transform.CacheKey,
            committedStateTransform: coordinator.CurrentState.RenderSettings.DisplayTransform,
            enabled with { Enabled = false },
            validated: null,
            diagnostic: null,
            applied: false);

        // The menu must not claim the transform is off while it is still colouring the
        // image; the disable becomes a deferred request instead.
        await Assert.That(decision.Committed.Enabled).IsTrue();
        await Assert.That(decision.CommittedTransformKey).IsEqualTo(Transform.CacheKey);
        await Assert.That(decision.StateTransform).IsEqualTo(Transform);
        await Assert.That(decision.Deferred!.Enabled).IsFalse();
        await Assert.That(decision.IsConsistent).IsTrue();
    }

    [Test]
    public async Task TheCoordinatorTellsTheBackendBeforeItPublishes()
    {
        string root = FindRepositoryRoot();
        string source = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "ViewerRenderCoordinator.cs"));

        int mutateIndex = source.IndexOf(
            "internal async ValueTask<ViewerStateMutationResult> TryMutateStateAsync(",
            StringComparison.Ordinal);
        await Assert.That(mutateIndex).IsGreaterThan(0);
        string body = source[mutateIndex..];
        int updateIndex = body.IndexOf(".UpdateStateAsync(state, cancellationToken)", StringComparison.Ordinal);
        int publishIndex = body.IndexOf(
            "Volatile.Write(ref _currentState, state);",
            StringComparison.Ordinal);

        await Assert.That(updateIndex).IsGreaterThan(0);
        await Assert.That(publishIndex).IsGreaterThan(updateIndex);
    }

    [Test]
    [Arguments("throw")]
    [Arguments("cancel")]
    public async Task AFailoverReplaysTheLastAcceptedStateNotTheRejectedOne(string mode)
    {
        var host = new FailingHost();
        StageRenderState initial = StageRenderState.Create(new StageIdentity("stage.usda"));
        RenderBackendManager manager = CreateManager(host);
        _ = await manager.InitializeAsync(initial);

        ViewerRenderCoordinator coordinator = CreateCoordinator(manager, initial);
        ViewerStateMutationResult accepted = await coordinator.TryMutateStateAsync(
            state => ViewerViewportStateMutation.WithDisplayTransform(state, Transform));
        await Assert.That(accepted.Applied).IsTrue();
        StageRenderState lastAccepted = accepted.PublishedState;

        // The backend refuses the next state.
        host.Sessions[RenderBackendKind.D3D12].FailWith = mode == "throw"
            ? new InvalidOperationException("the device refused the state")
            : new OperationCanceledException();
        try
        {
            _ = await coordinator.TryMutateStateAsync(
                static state => ViewerViewportStateMutation.WithDisplayTransform(state, null));
        }
        catch (Exception)
        {
            // Expected: the rejection is the point.
        }

        // Failing over must hand the replacement backend the last state a backend
        // actually accepted. Retaining the rejected one meant the successor was handed
        // the very state that had just been refused.
        RenderBackendManagerResult switched =
            await manager.SwitchAsync(RenderBackendKind.Vulkan);
        await Assert.That(switched.ActiveBackend?.Kind).IsEqualTo(RenderBackendKind.Vulkan);

        StageRenderState replayed = host.AttachedStates[RenderBackendKind.Vulkan];
        await Assert.That(replayed.RenderSettings.DisplayTransform).IsEqualTo(Transform);
        await Assert.That(replayed).IsEqualTo(lastAccepted);
        await Assert.That(coordinator.CurrentState.RenderSettings.DisplayTransform)
            .IsEqualTo(Transform);
    }

    [Test]
    public async Task TheReconciliationCommitsOnlyAfterAnAppliedClear()
    {
        string root = FindRepositoryRoot();
        string source = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "MainWindow.ColorManagement.cs"));

        int index = source.IndexOf(
            "internal async Task SynchronizeColorManagementFromBackendAsync()",
            StringComparison.Ordinal);
        await Assert.That(index).IsGreaterThan(0);
        string body = source[index..];
        int end = body.IndexOf(
            "private void CommitReconciledStatus(",
            StringComparison.Ordinal);
        await Assert.That(end).IsGreaterThan(0);
        body = body[..end];

        // The repair goes through the transactional mutation, and the disabled model,
        // the key, and the persisted settings move only once the coordinator published a
        // state with no transform.
        await Assert.That(body).Contains(
            "ViewerStateMutationResult mutation = await TryApplyViewportStateAsync(");
        await Assert.That(body).DoesNotContain("await ApplyViewportStateAsync(");
        int mutationIndex = body.IndexOf(
            "ViewerStateMutationResult mutation",
            StringComparison.Ordinal);
        int commitIndex = body.IndexOf(
            "_committedDisplayTransformKey = null;",
            StringComparison.Ordinal);
        await Assert.That(commitIndex).IsGreaterThan(mutationIndex);
        await Assert.That(body).Contains("if (!mutation.Applied ||");
        await Assert.That(body).Contains(
            "mutation.PublishedState.RenderSettings.DisplayTransform is not null)");
        await Assert.That(body).Contains(
            "_deferredColorManagement = new ViewerDeferredColorManagement(");
    }

    [Test]
    [Arguments("throw")]
    [Arguments("cancel")]
    public async Task ABackendThatMutatesBeforeFailingIsRebuiltFromTheLastAcceptedState(
        string mode)
    {
        var host = new FailingHost();
        StageRenderState initial = StageRenderState.Create(new StageIdentity("stage.usda"));
        RenderBackendManager manager = CreateManager(host);
        _ = await manager.InitializeAsync(initial);

        ViewerRenderCoordinator coordinator = CreateCoordinator(manager, initial);
        ViewerStateMutationResult accepted = await coordinator.TryMutateStateAsync(
            state => ViewerViewportStateMutation.WithDisplayTransform(state, Transform));
        await Assert.That(accepted.Applied).IsTrue();
        StageRenderState lastAccepted = accepted.PublishedState;

        FailingSession first = host.Sessions[RenderBackendKind.D3D12];
        first.MutateBeforeFailing = true;
        first.FailWith = mode == "throw"
            ? new InvalidOperationException("the device refused the state")
            : new OperationCanceledException();

        try
        {
            _ = await coordinator.TryMutateStateAsync(
                static state => ViewerViewportStateMutation.WithDisplayTransform(state, null));
        }
        catch (Exception)
        {
            // Expected.
        }

        // The session really did mutate before throwing, which is exactly why it can no
        // longer be trusted to hold the manager's retained state.
        await Assert.That(first.CurrentState.RenderSettings.DisplayTransform).IsNull();
        await Assert.That(first.DisposeCount).IsEqualTo(1);

        // A replacement was built and given the last accepted state, so the very next
        // frame renders that rather than a half-applied one.
        FailingSession rebuilt = host.Sessions[RenderBackendKind.D3D12];
        await Assert.That(rebuilt).IsNotSameReferenceAs(first);
        await Assert.That(rebuilt.CurrentState.RenderSettings.DisplayTransform)
            .IsEqualTo(Transform);
        await Assert.That(manager.CurrentState).IsSameReferenceAs(lastAccepted);
        await Assert.That(coordinator.CurrentState.RenderSettings.DisplayTransform)
            .IsEqualTo(Transform);

        // And a later failover hands the replacement backend the same accepted state.
        _ = await manager.SwitchAsync(RenderBackendKind.Vulkan);
        await Assert.That(host.AttachedStates[RenderBackendKind.Vulkan])
            .IsSameReferenceAs(lastAccepted);
    }

    [Test]
    [Arguments("throw")]
    [Arguments("cancel")]
    public async Task ADirectStateReplacementPublishesOnlyAfterTheBackendAccepts(string mode)
    {
        var host = new FailingHost();
        StageRenderState initial = StageRenderState.Create(new StageIdentity("stage.usda"));
        RenderBackendManager manager = CreateManager(host);
        _ = await manager.InitializeAsync(initial);

        ViewerRenderCoordinator coordinator = CreateCoordinator(manager, initial);
        int stateChanges = 0;
        coordinator.StateChanged += _ => stateChanges++;

        StageRenderState replacement = ViewerViewportStateMutation.WithDisplayTransform(
            initial,
            Transform);
        host.Sessions[RenderBackendKind.D3D12].FailWith = mode == "throw"
            ? new InvalidOperationException("the device refused the state")
            : new OperationCanceledException();

        Exception? thrown = null;
        try
        {
            _ = await coordinator.UpdateStateAsync(replacement);
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        await Assert.That(thrown).IsNotNull();
        await Assert.That(coordinator.CurrentState).IsSameReferenceAs(initial);
        await Assert.That(stateChanges).IsEqualTo(0);

        // The same call succeeds once the backend accepts, and only then publishes.
        RenderBackendManagerResult result = await coordinator.UpdateStateAsync(replacement);
        await Assert.That(result.Failure).IsEqualTo(RenderBackendManagerFailureKind.None);
        await Assert.That(coordinator.CurrentState).IsSameReferenceAs(replacement);
        await Assert.That(stateChanges).IsEqualTo(1);
    }

    [Test]
    public async Task EveryCoordinatorPathTellsTheBackendBeforeItPublishes()
    {
        string root = FindRepositoryRoot();
        string source = await File.ReadAllTextAsync(Path.Combine(
            root, "src", "OpenUsd.Viewer", "ViewerRenderCoordinator.cs"));

        // Three paths write the authoritative state: the computed mutation, the direct
        // replacement, and the live stage-change pump. All three must call the manager
        // first, or a refused state is published, announced, and mirrored anyway.
        int publishes = CountOccurrences(source, "Volatile.Write(ref _currentState, ");
        await Assert.That(publishes).IsEqualTo(3);

        foreach (string anchor in new[]
        {
            "internal async ValueTask<ViewerStateMutationResult> TryMutateStateAsync(",
            "internal async ValueTask<RenderBackendManagerResult> UpdateStateAsync(",
            "private async Task PumpStageChangesAsync(",
        })
        {
            int index = source.IndexOf(anchor, StringComparison.Ordinal);
            await Assert.That(index).IsGreaterThan(0);
            string body = source[index..];
            int update = body.IndexOf(".UpdateStateAsync(", StringComparison.Ordinal);
            int publish = body.IndexOf(
                "Volatile.Write(ref _currentState, ",
                StringComparison.Ordinal);
            await Assert.That(update).IsGreaterThan(0);
            await Assert.That(publish).IsGreaterThan(update);
        }

        // The pump announces the stage change after the publish, never before the update.
        int pumpIndex = source.IndexOf(
            "private async Task PumpStageChangesAsync(",
            StringComparison.Ordinal);
        string pump = source[pumpIndex..];
        await Assert.That(pump.IndexOf("StageChanged?.Invoke(change);", StringComparison.Ordinal))
            .IsGreaterThan(pump.IndexOf(".UpdateStateAsync(", StringComparison.Ordinal));
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    [Test]
    public async Task RecoveryDeactivatesTheRejectingBackendBeforeReplacingIt()
    {
        var host = new FailingHost();
        StageRenderState initial = StageRenderState.Create(new StageIdentity("stage.usda"));
        RenderBackendManager manager = CreateManager(host);
        _ = await manager.InitializeAsync(initial);

        ViewerRenderCoordinator coordinator = CreateCoordinator(manager, initial);
        _ = await coordinator.TryMutateStateAsync(
            state => ViewerViewportStateMutation.WithDisplayTransform(state, Transform));

        FailingSession first = host.Sessions[RenderBackendKind.D3D12];
        first.MutateBeforeFailing = true;
        // Disposal deliberately leaves the presenter alone, so only a real deactivation
        // can be what stops it. Retired cleanup runs later and may never run at all, so
        // it cannot be the thing that takes the presenter down.
        first.LeaveActiveOnDispose = true;
        first.FailWith = new InvalidOperationException("the device refused the state");

        try
        {
            _ = await coordinator.TryMutateStateAsync(
                static state => ViewerViewportStateMutation.WithDisplayTransform(state, null));
        }
        catch (Exception)
        {
            // Expected.
        }

        await Assert.That(first.DeactivateCount).IsEqualTo(1);
        await Assert.That(first.DeactivatedBeforeDispose).IsTrue();
        await Assert.That(first.IsActive).IsFalse();

        FailingSession rebuilt = host.Sessions[RenderBackendKind.D3D12];
        await Assert.That(rebuilt).IsNotSameReferenceAs(first);
        await Assert.That(rebuilt.IsActive).IsTrue();
        await Assert.That(host.AllSessions.Count(session => session.IsActive)).IsEqualTo(1);
    }

    [Test]
    public async Task RecoveryRefusesToReplaceABackendItCouldNeitherStopNorDispose()
    {
        var host = new FailingHost();
        StageRenderState initial = StageRenderState.Create(new StageIdentity("stage.usda"));
        RenderBackendManager manager = CreateManager(host);
        _ = await manager.InitializeAsync(initial);

        ViewerRenderCoordinator coordinator = CreateCoordinator(manager, initial);
        ViewerStateMutationResult accepted = await coordinator.TryMutateStateAsync(
            state => ViewerViewportStateMutation.WithDisplayTransform(state, Transform));
        StageRenderState lastAccepted = accepted.PublishedState;

        FailingSession first = host.Sessions[RenderBackendKind.D3D12];
        int sessionsBefore = host.AllSessions.Count;
        first.MutateBeforeFailing = true;
        first.LeaveActiveOnDispose = true;
        first.FailDeactivateWith = new InvalidOperationException("deactivation failed");
        first.FailDisposeWith = new InvalidOperationException("disposal failed");
        first.FailWith = new InvalidOperationException("the device refused the state");

        try
        {
            _ = await coordinator.TryMutateStateAsync(
                static state => ViewerViewportStateMutation.WithDisplayTransform(state, null));
        }
        catch (Exception)
        {
            // Expected.
        }

        // Neither deactivation nor disposal proved the presenter stopped, so nothing new
        // was activated. Two presenters on one surface is worse than a reported recovery
        // failure.
        await Assert.That(first.DeactivateCount).IsEqualTo(1);
        await Assert.That(first.IsActive).IsTrue();
        await Assert.That(host.AllSessions.Count).IsEqualTo(sessionsBefore);
        await Assert.That(host.AllSessions.Count(session => session.IsActive)).IsEqualTo(1);

        // The retained state is still the last accepted one, and the failure is visible
        // rather than swallowed.
        await Assert.That(manager.CurrentState).IsSameReferenceAs(lastAccepted);
        RenderBackendManagerResult reported = await manager.UpdateStateAsync(lastAccepted);
        await Assert.That(reported.Failure)
            .IsNotEqualTo(RenderBackendManagerFailureKind.None);
    }

    [Test]
    public async Task ASuccessfulDisposalIsEnoughToProveTheOldPresenterStopped()
    {
        var host = new FailingHost();
        StageRenderState initial = StageRenderState.Create(new StageIdentity("stage.usda"));
        RenderBackendManager manager = CreateManager(host);
        _ = await manager.InitializeAsync(initial);

        ViewerRenderCoordinator coordinator = CreateCoordinator(manager, initial);
        ViewerStateMutationResult accepted = await coordinator.TryMutateStateAsync(
            state => ViewerViewportStateMutation.WithDisplayTransform(state, Transform));

        FailingSession first = host.Sessions[RenderBackendKind.D3D12];
        first.MutateBeforeFailing = true;
        // Deactivation fails, but disposal succeeds and takes the presenter with it, so
        // a replacement may be activated.
        first.FailDeactivateWith = new InvalidOperationException("deactivation failed");
        first.FailWith = new InvalidOperationException("the device refused the state");

        try
        {
            _ = await coordinator.TryMutateStateAsync(
                static state => ViewerViewportStateMutation.WithDisplayTransform(state, null));
        }
        catch (Exception)
        {
            // Expected.
        }

        await Assert.That(first.DeactivateCount).IsEqualTo(1);
        await Assert.That(first.DisposeCount).IsEqualTo(1);
        await Assert.That(first.IsActive).IsFalse();

        FailingSession rebuilt = host.Sessions[RenderBackendKind.D3D12];
        await Assert.That(rebuilt).IsNotSameReferenceAs(first);
        await Assert.That(rebuilt.CurrentState.RenderSettings.DisplayTransform)
            .IsEqualTo(Transform);
        await Assert.That(host.AllSessions.Count(session => session.IsActive)).IsEqualTo(1);
        await Assert.That(manager.CurrentState)
            .IsSameReferenceAs(accepted.PublishedState);
    }

    private static ViewerRenderCoordinator CreateCoordinator(
        RenderBackendManager manager,
        StageRenderState initialState)
    {
        var coordinator = (ViewerRenderCoordinator)RuntimeHelpers.GetUninitializedObject(
            typeof(ViewerRenderCoordinator));
        SetField(coordinator, "_manager", manager);
        SetField(coordinator, "_currentState", initialState);
        SetField(coordinator, "_stateGate", new SemaphoreSlim(1, 1));
        SetField(coordinator, "_latestDiagnostics", RenderBackendDiagnostics.Empty);
        SetField(coordinator, "_latestRecoveryReason", "None");
        return coordinator;
    }

    private static void SetField(object instance, string name, object value)
    {
        FieldInfo field = instance.GetType().GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException(
                $"ViewerRenderCoordinator no longer has a '{name}' field.");
        field.SetValue(instance, value);
    }

    private static RenderBackendManager CreateManager(FailingHost host) =>
        new(
            RenderPlatform.Windows,
            [
                new FailingFactory(RenderBackendKind.D3D12, host),
                new FailingFactory(RenderBackendKind.Vulkan, host)
            ]);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class FailingFactory(RenderBackendKind kind, FailingHost host)
        : IRenderBackendFactory
    {
        public RenderBackendKind Kind { get; } = kind;

        public IRenderBackend Create() => new ViewerRenderBackend(Kind, host);
    }

    private sealed class FailingHost : IViewerRenderBackendHost
    {
        internal FailingSession? Session { get; private set; }

        internal Dictionary<RenderBackendKind, FailingSession> Sessions { get; } = [];

        internal List<FailingSession> AllSessions { get; } = [];

        internal Dictionary<RenderBackendKind, StageRenderState> AttachedStates { get; } = [];

        public ValueTask<RenderBackendProbeResult> ProbeAsync(
            RenderBackendKind kind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(RenderBackendProbeResult.Available());
        }

        public ValueTask<IViewerRenderBackendSession> AttachAsync(
            RenderBackendKind kind,
            StageRenderState initialState,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = new FailingSession(initialState);
            Session = session;
            Sessions[kind] = session;
            AllSessions.Add(session);
            AttachedStates[kind] = initialState;
            return ValueTask.FromResult<IViewerRenderBackendSession>(session);
        }
    }

    private sealed class FailingSession(StageRenderState initialState)
        : IViewerRenderBackendSession
    {
        private StageRenderState _state = initialState;

        internal Exception? FailWith { get; set; }

        internal Exception? FailDeactivateWith { get; set; }

        internal Exception? FailDisposeWith { get; set; }

        internal bool LeaveActiveOnDispose { get; set; }

        internal bool MutateBeforeFailing { get; set; }

        internal int UpdateCount { get; private set; }

        internal int DisposeCount { get; private set; }

        internal int DeactivateCount { get; private set; }

        /// <summary>Whether this session still owns a presenter on the surface.</summary>
        internal bool IsActive { get; private set; } = true;

        internal bool DeactivatedBeforeDispose { get; private set; }

        public RenderBackendDiagnostics Diagnostics { get; } =
            RenderBackendDiagnostics.Empty;

        public StageRenderState CurrentState => _state;

        public ValueTask ActivateAsync(CancellationToken cancellationToken)
        {
            IsActive = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask DeactivateAsync(CancellationToken cancellationToken)
        {
            DeactivateCount++;
            if (FailDeactivateWith is { } failure)
            {
                FailDeactivateWith = null;
                throw failure;
            }

            DeactivatedBeforeDispose = DisposeCount == 0;
            IsActive = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateStateAsync(
            StageRenderState state,
            CancellationToken cancellationToken)
        {
            if (FailWith is { } failure)
            {
                FailWith = null;
                if (MutateBeforeFailing)
                {
                    // The realistic shape of the hazard: some of the state was applied
                    // before the device gave up, so the backend now holds neither the
                    // old state nor the new one.
                    _state = state;
                }
                throw failure;
            }

            _state = state;
            UpdateCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ResizeAsync(
            ViewportDimensions viewport,
            CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask<RenderFrameResult> RenderAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(RenderFrameResult.Rendered(
                _state.Revision,
                RenderFrameStatistics.Empty));

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            if (FailDisposeWith is { } failure)
            {
                FailDisposeWith = null;
                // A failed disposal proves nothing about the presenter.
                throw failure;
            }

            if (!LeaveActiveOnDispose)
            {
                IsActive = false;
            }
            return ValueTask.CompletedTask;
        }
    }
}
