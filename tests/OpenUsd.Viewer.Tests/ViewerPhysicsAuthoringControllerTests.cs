// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Drives the controller's authoring surface: transactional property edits, undo and redo, exact
/// self-edit serial handling, batched runtime commands, and the inspector projection.
/// </summary>
public sealed class ViewerPhysicsAuthoringControllerTests
{
    [Test]
    public async Task AnEditIsAuthoredOnceAndBecomesOneUndoStep()
    {
        var factory = new FakeTransportFactory();
        var authoring = new FakeAuthoringStage();
        await using ViewerPhysicsController controller = NewController(factory, authoring);
        await controller.EnableAsync();

        ViewerPhysicsAuthoringResult result = await controller.ApplyEditAsync(
            Step("/World/Body", "openUsdPhysics:body:sleepThreshold", 0d, 0.5d));

        await Assert.That(result.Applied).IsEqualTo(1);
        await Assert.That(result.Rejected).IsEqualTo(0);
        await Assert.That(authoring.Steps.Count).IsEqualTo(1);
        await Assert.That(controller.History.CanUndo).IsTrue();
        await Assert.That(controller.History.CanRedo).IsFalse();
    }

    [Test]
    public async Task UndoAuthorsTheReverseAndRedoAuthorsItAgain()
    {
        var factory = new FakeTransportFactory();
        var authoring = new FakeAuthoringStage();
        await using ViewerPhysicsController controller = NewController(factory, authoring);
        await controller.EnableAsync();
        await controller.ApplyEditAsync(
            Step("/World/Body", "openUsdPhysics:body:sleepThreshold", 0d, 0.5d));

        await controller.UndoAsync();

        await Assert.That(authoring.Steps.Count).IsEqualTo(2);
        await Assert.That(authoring.Steps[1].Edits[0].After.NumberValue).IsEqualTo(0d);
        await Assert.That(controller.History.CanUndo).IsFalse();
        await Assert.That(controller.History.CanRedo).IsTrue();

        await controller.RedoAsync();

        await Assert.That(authoring.Steps.Count).IsEqualTo(3);
        await Assert.That(authoring.Steps[2].Edits[0].After.NumberValue).IsEqualTo(0.5d);
        await Assert.That(controller.History.CanUndo).IsTrue();
    }

    [Test]
    public async Task AnUndoThatAuthorsNothingIsPutBackOntoTheUndoStack()
    {
        var factory = new FakeTransportFactory();
        var authoring = new FakeAuthoringStage();
        await using ViewerPhysicsController controller = NewController(factory, authoring);
        await controller.EnableAsync();
        await controller.ApplyEditAsync(
            Step("/World/Body", "openUsdPhysics:body:sleepThreshold", 0d, 0.5d));
        authoring.RefuseNext = true;

        ViewerPhysicsAuthoringResult result = await controller.UndoAsync();

        await Assert.That(result.Applied).IsEqualTo(0);
        await Assert.That(controller.History.CanUndo).IsTrue();
        await Assert.That(controller.History.CanRedo).IsFalse();
    }

    [Test]
    public async Task UndoingWithAnEmptyHistorySaysSoRatherThanFailing()
    {
        var factory = new FakeTransportFactory();
        await using ViewerPhysicsController controller =
            NewController(factory, new FakeAuthoringStage());
        await controller.EnableAsync();

        ViewerPhysicsAuthoringResult undo = await controller.UndoAsync();
        ViewerPhysicsAuthoringResult redo = await controller.RedoAsync();

        await Assert.That(undo.Message).Contains("undo");
        await Assert.That(redo.Message).Contains("redo");
    }

    [Test]
    public async Task ADocumentWithoutAWritableStageRefusesEveryEditHonestly()
    {
        var factory = new FakeTransportFactory();
        await using ViewerPhysicsController controller = NewController(factory, authoring: null);
        await controller.EnableAsync();

        ViewerPhysicsAuthoringResult result = await controller.ApplyEditAsync(
            Step("/World/Body", "openUsdPhysics:body:sleepThreshold", 0d, 0.5d));

        await Assert.That(controller.CanAuthor).IsFalse();
        await Assert.That(result.Applied).IsEqualTo(0);
        await Assert.That(result.Message).Contains("writable stage");
        await Assert.That(controller.History.CanUndo).IsFalse();
    }

    [Test]
    public async Task AuthoringASimulationInputPausesPlaybackAndRebuildsAfterTheQuietWindow()
    {
        var factory = new FakeTransportFactory();
        var authoring = new FakeAuthoringStage();
        var clock = new FakeClock();
        await using ViewerPhysicsController controller = NewController(factory, authoring, clock);
        await controller.EnableAsync();
        controller.Play();
        await Assert.That(controller.IsPlaying).IsTrue();

        ViewerPhysicsAuthoringResult result = await controller.ApplyEditAsync(
            Step("/World/Body", "openUsdPhysics:body:sleepThreshold", 0d, 0.5d));
        controller.NotifyStageChanged(ViewerPhysicsEditKind.Relevant, result.Edits[0]);

        await Assert.That(controller.IsPlaying).IsFalse();
        int invalidations = factory.Transport.Invalidations;
        clock.NowSeconds += 1d;
        await controller.PumpAsync();
        await Assert.That(factory.Transport.Invalidations).IsGreaterThan(invalidations);
    }

    [Test]
    public async Task AuthoringSimulationMetadataNeverRebuildsTheWorld()
    {
        var factory = new FakeTransportFactory();
        var authoring = new FakeAuthoringStage();
        var clock = new FakeClock();
        await using ViewerPhysicsController controller = NewController(factory, authoring, clock);
        await controller.EnableAsync();
        controller.Play();

        ViewerPhysicsAuthoringResult result = await controller.ApplyEditAsync(
            Step("/World/Scene", "openUsdPhysics:simulation:sourceRevision", 0d, 1d));

        // The caller classifies every observed change conservatively; the controller replaces that
        // with its own exact knowledge of the field it authored.
        controller.NotifyStageChanged(ViewerPhysicsEditKind.Relevant, result.Edits[0]);

        await Assert.That(controller.IsPlaying).IsTrue();
        int invalidations = factory.Transport.Invalidations;
        clock.NowSeconds += 5d;
        await controller.PumpAsync();
        await Assert.That(factory.Transport.Invalidations).IsEqualTo(invalidations);
    }

    [Test]
    public async Task AnUnrelatedChangeWithTheSameShapeStillInvalidatesTheWorld()
    {
        var factory = new FakeTransportFactory();
        var authoring = new FakeAuthoringStage();
        var clock = new FakeClock();
        await using ViewerPhysicsController controller = NewController(factory, authoring, clock);
        await controller.EnableAsync();
        controller.Play();

        await controller.ApplyEditAsync(
            Step("/World/Scene", "openUsdPhysics:simulation:sourceRevision", 0d, 1d));

        // A different change, not the one the controller authored, must not inherit the neutral
        // classification the authored change carried.
        controller.NotifyStageChanged(
            ViewerPhysicsEditKind.Relevant,
            new ViewerPhysicsStageEdit(900UL, 901UL));

        await Assert.That(controller.IsPlaying).IsFalse();
    }

    [Test]
    public async Task RuntimeCommandsAreSubmittedAsOneBatch()
    {
        var factory = new FakeTransportFactory();
        await using ViewerPhysicsController controller =
            NewController(factory, new FakeAuthoringStage());
        await controller.EnableAsync();

        ViewerPhysicsCommandOutcome outcome = await controller.SubmitCommandsAsync(
        [
            new ViewerPhysicsRuntimeCommand(
                ViewerPhysicsRuntimeCommandKind.Wake, 1UL, ViewerPhysicsVector3.Zero),
            new ViewerPhysicsVehicleInput(1d, 0d, 0d, 0d, 0d, 3).ToCommand(2UL),
        ]);

        await Assert.That(outcome.Accepted).IsEqualTo(2);
        await Assert.That(factory.Transport.CommandBatches.Count).IsEqualTo(1);
        await Assert.That(factory.Transport.CommandBatches[0].Count).IsEqualTo(2);
        await Assert.That(controller.StagedCommands).IsEqualTo(2L);
    }

    [Test]
    public async Task CommandsSubmittedWhilePausedSayTheyApplyOnTheNextStep()
    {
        var factory = new FakeTransportFactory();
        await using ViewerPhysicsController controller =
            NewController(factory, new FakeAuthoringStage());
        await controller.EnableAsync();

        ViewerPhysicsCommandOutcome outcome = await controller.SubmitCommandsAsync(
            [new ViewerPhysicsRuntimeCommand(
                ViewerPhysicsRuntimeCommandKind.Wake, 1UL, ViewerPhysicsVector3.Zero)]);

        await Assert.That(outcome.Message).Contains("next simulation step");
    }

    [Test]
    public async Task CommandsAreRefusedBeforeTheWorldExistsAndAfterItGoesStale()
    {
        var factory = new FakeTransportFactory();
        await using ViewerPhysicsController controller =
            NewController(factory, new FakeAuthoringStage());

        ViewerPhysicsCommandOutcome disabled = await controller.SubmitCommandsAsync(
            [new ViewerPhysicsRuntimeCommand(
                ViewerPhysicsRuntimeCommandKind.Wake, 1UL, ViewerPhysicsVector3.Zero)]);
        await Assert.That(disabled.Rejected).IsEqualTo(1);
        await Assert.That(disabled.Message).Contains("Enable physics");

        await controller.EnableAsync();
        factory.Transport.SetState(ViewerPhysicsRunState.Invalidated);
        ViewerPhysicsCommandOutcome stale = await controller.SubmitCommandsAsync(
            [new ViewerPhysicsRuntimeCommand(
                ViewerPhysicsRuntimeCommandKind.Wake, 1UL, ViewerPhysicsVector3.Zero)]);
        await Assert.That(stale.Rejected).IsEqualTo(1);
        await Assert.That(stale.Message).Contains("rebuild");
    }

    [Test]
    public async Task ARefusedCommandIsCountedAndReportedRatherThanThrown()
    {
        var factory = new FakeTransportFactory();
        factory.Transport.RefuseCommands = true;
        await using ViewerPhysicsController controller =
            NewController(factory, new FakeAuthoringStage());
        await controller.EnableAsync();

        ViewerPhysicsCommandOutcome outcome = await controller.SubmitCommandsAsync(
            [new ViewerPhysicsRuntimeCommand(
                ViewerPhysicsRuntimeCommandKind.Wake, 1UL, ViewerPhysicsVector3.Zero)]);

        await Assert.That(outcome.Accepted).IsEqualTo(0);
        await Assert.That(outcome.Rejected).IsEqualTo(1);
        await Assert.That(controller.RefusedCommands).IsEqualTo(1L);
        await Assert.That(controller.Snapshot.Error).IsEmpty();
    }

    [Test]
    public async Task TheInspectorIsProjectedAgainstTheCapabilitiesTheWorldReports()
    {
        var factory = new FakeTransportFactory();
        factory.Transport.InspectorDocument = new ViewerPhysicsExtractionDocument(
            5UL,
            [
                new ViewerPhysicsExtractedObject(
                    1UL,
                    "/World/Body",
                    "RigidBody",
                    IsEnabled: true,
                    [
                        new ViewerPhysicsExtractedProperty(
                            "openUsdPhysics:body:sleepThreshold", "0.1", "Project", true),
                        new ViewerPhysicsExtractedProperty(
                            "openUsdPhysics:vehicle:lateralStickyTireDamping",
                            "0.5",
                            "Project",
                            true),
                    ],
                    []),
            ],
            "Extracted one object.");
        await using ViewerPhysicsController controller =
            NewController(factory, new FakeAuthoringStage());
        await controller.EnableAsync();

        IReadOnlyList<ViewerPhysicsObjectSection> sections =
            await controller.LoadInspectorAsync();

        await Assert.That(sections.Count).IsEqualTo(1);
        await Assert.That(controller.InspectorRevision).IsEqualTo(5UL);
        ViewerPhysicsPropertyRow? body = ViewerPhysicsInspectorProjector.FindRow(
            sections, "/World/Body", "openUsdPhysics:body:sleepThreshold");
        ViewerPhysicsPropertyRow? vehicle = ViewerPhysicsInspectorProjector.FindRow(
            sections, "/World/Body", "openUsdPhysics:vehicle:lateralStickyTireDamping");

        // The fake world simulates rigid bodies but not vehicles, so only the body row may be
        // authored and the vehicle row says exactly why it may not.
        await Assert.That(body!.IsEditable).IsTrue();
        await Assert.That(vehicle!.IsEditable).IsFalse();
        await Assert.That(vehicle.Authorability)
            .IsEqualTo(ViewerPhysicsAuthorability.UnsupportedCapability);
    }

    [Test]
    public async Task PerObjectDiagnosticsReachTheInspectorSection()
    {
        var factory = new FakeTransportFactory();
        factory.Transport.InspectorDocument = new ViewerPhysicsExtractionDocument(
            1UL,
            [
                new ViewerPhysicsExtractedObject(
                    1UL,
                    "/World/Broken",
                    "Joint",
                    IsEnabled: false,
                    [],
                    ["Error Joint OPENUSD_PHYSICS_JOINT_BAD: the joint has no bodies"]),
            ],
            "Extracted one object.");
        await using ViewerPhysicsController controller =
            NewController(factory, new FakeAuthoringStage());
        await controller.EnableAsync();

        IReadOnlyList<ViewerPhysicsObjectSection> sections =
            await controller.LoadInspectorAsync();

        await Assert.That(sections[0].Diagnostics.Count).IsEqualTo(1);
        await Assert.That(sections[0].Diagnostics[0]).Contains("no bodies");
        await Assert.That(sections[0].Detail).Contains("not simulated");
    }

    [Test]
    public async Task ResolvingAnIdentityUsesTheBindingTableTheBuildProduced()
    {
        var factory = new FakeTransportFactory();
        await using ViewerPhysicsController controller =
            NewController(factory, new FakeAuthoringStage());
        await controller.EnableAsync();

        await Assert.That(controller.ResolveIdentity("/World/Body0")).IsEqualTo(1UL);
        await Assert.That(controller.ResolveIdentity("/World/Missing")).IsEqualTo(0UL);
    }

    private static ViewerPhysicsController NewController(
        FakeTransportFactory factory,
        IViewerPhysicsAuthoringStage? authoring,
        FakeClock? clock = null) =>
        new(
            factory,
            clock ?? new FakeClock(),
            ViewerPhysicsRenderCapacities.Default,
            8,
            0.25d,
            authoring);

    private static ViewerPhysicsEditStep Step(
        string primPath,
        string name,
        double before,
        double after) =>
        new(
            $"{name} on {primPath}",
            [
                new ViewerPhysicsEdit(
                    primPath,
                    name,
                    name,
                    ViewerPhysicsValue.FromNumber(before),
                    ViewerPhysicsValue.FromNumber(after)),
            ]);

    private sealed class FakeClock : IViewerPhysicsClock
    {
        public double NowSeconds { get; set; }
    }

    private sealed class FakeAuthoringStage : IViewerPhysicsAuthoringStage
    {
        private ulong _serial = 100UL;

        public List<ViewerPhysicsEditStep> Steps { get; } = [];

        public bool RefuseNext { get; set; }

        public ValueTask<ViewerPhysicsAuthoringResult> ApplyAsync(
            ViewerPhysicsEditStep step,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(step);
            cancellationToken.ThrowIfCancellationRequested();
            if (RefuseNext)
            {
                RefuseNext = false;
                return ValueTask.FromResult(new ViewerPhysicsAuthoringResult(
                    0, step.Edits.Count, "The fake stage refused the step.", []));
            }

            Steps.Add(step);
            ulong before = _serial;
            _serial += 2UL;
            return ValueTask.FromResult(new ViewerPhysicsAuthoringResult(
                step.Edits.Count,
                0,
                $"Authored {step.Edits.Count} physics property.",
                [new ViewerPhysicsStageEdit(before, _serial)]));
        }

        public ValueTask<ViewerPhysicsValue> ReadAsync(
            string primPath,
            string name,
            ViewerPhysicsValueKind kind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ViewerPhysicsValue.Unauthored(kind));
        }
    }

    private sealed class FakeTransportFactory : IViewerPhysicsTransportFactory
    {
        public FakeTransport Transport { get; } = new();

        public ValueTask<IViewerPhysicsTransport> CreateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IViewerPhysicsTransport>(Transport);
        }
    }

    private sealed class FakeTransport : IViewerPhysicsTransport
    {
        private ViewerPhysicsRunState _state = ViewerPhysicsRunState.Paused;

        public List<IReadOnlyList<ViewerPhysicsRuntimeCommand>> CommandBatches { get; } = [];

        public bool RefuseCommands { get; set; }

        public ViewerPhysicsExtractionDocument InspectorDocument { get; set; } =
            ViewerPhysicsExtractionDocument.Empty;

        public int Builds { get; private set; }

        public int Invalidations { get; private set; }

        public ViewerPhysicsTransportStatus Status =>
            new(_state, 0UL, 0UL, 0d, 0d, 0d, 0, 0);

        public double FixedStepSeconds => 1d / 60d;

        public double StartTimeCode => 0d;

        public double EndTimeCode => 24d;

        public IReadOnlyList<ViewerPhysicsCapabilitySupport> Capabilities { get; } =
        [
            new("RigidBodies", true, PhysicsRenderDomain.RigidBody, "Simulated on the CPU."),
            new("Commands", true, null, "Runtime commands are accepted."),
            new("Vehicles", false, PhysicsRenderDomain.Vehicle, "No vehicle support."),
        ];

        public IReadOnlyList<ViewerPhysicsDiagnosticRow> Diagnostics { get; } = [];

        public void SetState(ViewerPhysicsRunState state) => _state = state;

        public ValueTask<ViewerPhysicsBindingSet> LoadBindingsAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ViewerPhysicsBindingSet(
                1UL,
                [
                    new ViewerPhysicsBinding(
                        1UL,
                        PhysicsRenderObjectKind.RigidBody,
                        "/World/Body0",
                        0,
                        true,
                        "Simulated body."),
                ],
                0,
                "Bound from the fake extraction."));
        }

        public Task BuildAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Builds++;
            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SeekAsync(double timeCode, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task StepAsync(int steps, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SetLoopAsync(bool loop, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task InvalidateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invalidations++;
            _state = ViewerPhysicsRunState.Invalidated;
            return Task.CompletedTask;
        }

        public Task<ViewerPhysicsCommandOutcome> SubmitCommandsAsync(
            IReadOnlyList<ViewerPhysicsRuntimeCommand> commands,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(commands);
            cancellationToken.ThrowIfCancellationRequested();
            CommandBatches.Add(commands);
            return Task.FromResult(RefuseCommands
                ? new ViewerPhysicsCommandOutcome(
                    0, commands.Count, "The fake world refuses runtime commands.")
                : new ViewerPhysicsCommandOutcome(
                    commands.Count, 0, $"Staged {commands.Count} runtime command(s)."));
        }

        public ValueTask<ViewerPhysicsExtractionDocument> LoadInspectorAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(InspectorDocument);
        }

        public bool TryPublishLatestFrame(PhysicsRenderChannel channel) => false;

        public Task<ViewerPhysicsPreviewOutcome> ApplyPreviewAsync(
            bool enabled,
            CancellationToken cancellationToken) =>
            Task.FromResult(ViewerPhysicsPreviewOutcome.None);

        public Task<ViewerPhysicsBakeOutcome> BakeAsync(
            ViewerPhysicsBakeRequest request,
            IProgress<ViewerPhysicsBakeProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ViewerPhysicsBakeOutcome(false, false, 0, "Not supported."));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
