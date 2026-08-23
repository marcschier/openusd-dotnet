// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Drives the viewer's physics controller against a fake transport, a fake clock, and a fake render
/// backend, so playback, pacing, invalidation, and lifecycle rules are asserted exactly instead of
/// being timed against a real solver.
/// </summary>
public sealed class ViewerPhysicsControllerTests
{
    [Test]
    public async Task PhysicsIsNotCreatedUntilItIsRequested()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());

        await Assert.That(controller.IsEnabled).IsFalse();
        await Assert.That(factory.Created).IsEqualTo(0);
        await Assert.That(controller.Snapshot.CanRun(ViewerPhysicsCommand.Play)).IsFalse();
        await Assert.That(controller.Snapshot.DescribeUnavailable(ViewerPhysicsCommand.Play))
            .IsEqualTo("Enable physics for this stage first.");

        await controller.EnableAsync();

        await Assert.That(factory.Created).IsEqualTo(1);
        await Assert.That(controller.IsEnabled).IsTrue();
        await Assert.That(factory.Transport.Builds).IsEqualTo(1);
    }

    [Test]
    public async Task ClosingDuringABuildDisposesTheTransportWithoutFaulting()
    {
        var factory = new FakePhysicsTransportFactory();
        factory.Transport.BlockBuild = true;
        var controller = NewController(factory, new FakePhysicsClock());

        Task enable = controller.EnableAsync();
        await factory.Transport.BuildEntered.Task;

        Task dispose = controller.DisposeAsync().AsTask();
        factory.Transport.ReleaseBuild();

        await enable;
        await dispose;

        await Assert.That(factory.Transport.Disposed).IsTrue();
        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ClosingDuringASeekCancelsTheSeekAndStillDisposes()
    {
        var factory = new FakePhysicsTransportFactory();
        var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        factory.Transport.BlockSeek = true;
        Task seek = controller.SeekAsync(12d);
        await factory.Transport.SeekEntered.Task;

        await controller.DisposeAsync();
        await seek;

        await Assert.That(factory.Transport.Disposed).IsTrue();
        await Assert.That(factory.Transport.SeekWasCanceled).IsTrue();
    }

    [Test]
    public async Task RapidScrubbingCancelsEverySupersededSeek()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        factory.Transport.BlockSeek = true;
        Task first = controller.SeekAsync(1d);
        await factory.Transport.SeekEntered.Task;

        factory.Transport.BlockSeek = false;
        Task second = controller.SeekAsync(9d);
        await first;
        await second;

        await Assert.That(factory.Transport.SeekWasCanceled).IsTrue();
        await Assert.That(factory.Transport.LastSeekTimeCode).IsEqualTo(9d);
    }

    [Test]
    public async Task AFullTransportQueueSurfacesAsADiagnosticNotAnUnhandledFailure()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        factory.Transport.StepFailure = new ViewerPhysicsException(
            ViewerPhysicsFailureKind.QueueFull,
            "The physics request queue is full.");

        await controller.StepOneFrameAsync();

        await Assert.That(controller.Snapshot.Error)
            .IsEqualTo("The physics request queue is full.");
        await Assert.That(controller.IsPlaying).IsFalse();
    }

    [Test]
    public async Task RelevantEditsPauseImmediatelyAndInvalidateOnceTheBurstGoesQuiet()
    {
        var clock = new FakePhysicsClock();
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, clock, debounceSeconds: 0.25d);
        await controller.EnableAsync();
        controller.Play();
        await Assert.That(controller.IsPlaying).IsTrue();

        controller.NotifyStageChanged(ViewerPhysicsEditKind.Relevant);
        await Assert.That(controller.IsPlaying).IsFalse();

        clock.NowSeconds += 0.1d;
        controller.NotifyStageChanged(ViewerPhysicsEditKind.Relevant);
        await controller.PumpAsync();
        await Assert.That(factory.Transport.Invalidations).IsEqualTo(0);

        clock.NowSeconds += 0.3d;
        await controller.PumpAsync();
        await Assert.That(factory.Transport.Invalidations).IsEqualTo(1);

        clock.NowSeconds += 5d;
        await controller.PumpAsync();
        await Assert.That(factory.Transport.Invalidations).IsEqualTo(1);
    }

    [Test]
    public async Task VisualEditsNeverInvalidateTheBuiltWorld()
    {
        var clock = new FakePhysicsClock();
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, clock, debounceSeconds: 0.25d);
        await controller.EnableAsync();
        controller.Play();

        controller.NotifyStageChanged(ViewerPhysicsEditKind.Visual);
        clock.NowSeconds += 5d;
        await controller.PumpAsync();

        await Assert.That(factory.Transport.Invalidations).IsEqualTo(0);
        await Assert.That(controller.IsPlaying).IsTrue();
        await Assert.That(controller.Edits.ObservedVisualEdits).IsEqualTo(1L);
    }

    [Test]
    public async Task ApplyingAPreviewDoesNotInvalidateTheWorldItJustWrote()
    {
        var clock = new FakePhysicsClock();
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, clock, debounceSeconds: 0.05d);
        await controller.EnableAsync();

        _ = await controller.SetPreviewAsync(true);

        // The overlay edit the preview authored comes back through the same observer every other
        // edit uses, and it is recognised by its identity rather than by when it arrived.
        controller.NotifyStageChanged(
            ViewerPhysicsEditKind.Relevant,
            factory.Transport.LastPreviewEdit);

        clock.NowSeconds += 1d;
        await controller.PumpAsync();

        await Assert.That(factory.Transport.Invalidations).IsEqualTo(0);
        await Assert.That(controller.Snapshot.PreviewEnabled).IsTrue();
    }

    [Test]
    public async Task ThePreviewsOwnEditIsSuppressedNoMatterHowLateItArrives()
    {
        var clock = new FakePhysicsClock();
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, clock, debounceSeconds: 0.05d);
        await controller.EnableAsync();

        _ = await controller.SetPreviewAsync(true);
        ViewerPhysicsStageEdit own = factory.Transport.LastPreviewEdit;

        // A time window would have expired long before this notification arrives.
        clock.NowSeconds += 30d;
        controller.NotifyStageChanged(ViewerPhysicsEditKind.Relevant, own);

        clock.NowSeconds += 1d;
        await controller.PumpAsync();

        await Assert.That(factory.Transport.Invalidations).IsEqualTo(0);
        await Assert.That(controller.Snapshot.PreviewEnabled).IsTrue();
    }

    [Test]
    public async Task AnExternalEditThatRacesThePreviewIsNeverDiscarded()
    {
        var clock = new FakePhysicsClock();
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, clock, debounceSeconds: 0.05d);
        await controller.EnableAsync();

        _ = await controller.SetPreviewAsync(true);
        ViewerPhysicsStageEdit own = factory.Transport.LastPreviewEdit;

        // A different change, authored by someone else at the same moment, must still invalidate.
        var external = new ViewerPhysicsStageEdit(own.AfterSerial, own.AfterSerial + 1);
        controller.NotifyStageChanged(ViewerPhysicsEditKind.Relevant, external);

        clock.NowSeconds += 1d;
        await controller.PumpAsync();

        await Assert.That(factory.Transport.Invalidations).IsEqualTo(1);
        await Assert.That(controller.Snapshot.PreviewEnabled).IsFalse();

        // The controller's own edit is still recognised afterwards, so one external edit does not
        // consume the suppression the preview registered.
        controller.NotifyStageChanged(ViewerPhysicsEditKind.Relevant, own);
        clock.NowSeconds += 1d;
        await controller.PumpAsync();

        await Assert.That(factory.Transport.Invalidations).IsEqualTo(1);
    }

    [Test]
    public async Task AnEditWithNoKnownIdentityIsAlwaysTreatedAsExternal()
    {
        var clock = new FakePhysicsClock();
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, clock, debounceSeconds: 0.05d);
        await controller.EnableAsync();

        _ = await controller.SetPreviewAsync(true);
        controller.NotifyStageChanged(ViewerPhysicsEditKind.Relevant);

        clock.NowSeconds += 1d;
        await controller.PumpAsync();

        await Assert.That(factory.Transport.Invalidations).IsEqualTo(1);
    }

    [Test]
    public async Task EveryChunkOfAMultiChunkPreviewIsSuppressedIndividually()
    {
        var clock = new FakePhysicsClock();
        var factory = new FakePhysicsTransportFactory();
        factory.Transport.PreviewChunks = 5;
        await using var controller = NewController(factory, clock, debounceSeconds: 0.05d);
        await controller.EnableAsync();

        _ = await controller.SetPreviewAsync(true);
        IReadOnlyList<ViewerPhysicsStageEdit> own = factory.Transport.LastPreviewEdits;
        await Assert.That(own.Count).IsEqualTo(5);

        // A chunked apply authors one change per chunk; a bracket around the whole apply would
        // either swallow unrelated edits between the chunks or fail to match any of them.
        foreach (ViewerPhysicsStageEdit edit in own)
        {
            controller.NotifyStageChanged(ViewerPhysicsEditKind.Relevant, edit);
        }

        clock.NowSeconds += 1d;
        await controller.PumpAsync();

        await Assert.That(factory.Transport.Invalidations).IsEqualTo(0);
        await Assert.That(controller.Snapshot.PreviewEnabled).IsTrue();
    }

    [Test]
    public async Task AnEditInterleavedBetweenPreviewChunksIsNeverSwallowed()
    {
        var clock = new FakePhysicsClock();
        var factory = new FakePhysicsTransportFactory();
        factory.Transport.PreviewChunks = 4;
        await using var controller = NewController(factory, clock, debounceSeconds: 0.05d);
        await controller.EnableAsync();

        _ = await controller.SetPreviewAsync(true);
        IReadOnlyList<ViewerPhysicsStageEdit> own = factory.Transport.LastPreviewEdits;
        ViewerPhysicsStageEdit external = factory.Transport.AuthorExternalEdit();

        controller.NotifyStageChanged(ViewerPhysicsEditKind.Relevant, own[0]);
        controller.NotifyStageChanged(ViewerPhysicsEditKind.Relevant, own[1]);
        controller.NotifyStageChanged(ViewerPhysicsEditKind.Relevant, external);
        controller.NotifyStageChanged(ViewerPhysicsEditKind.Relevant, own[2]);
        controller.NotifyStageChanged(ViewerPhysicsEditKind.Relevant, own[3]);

        clock.NowSeconds += 1d;
        await controller.PumpAsync();

        // Exactly the one edit the controller did not author rebuilt the world.
        await Assert.That(factory.Transport.Invalidations).IsEqualTo(1);
        await Assert.That(controller.Snapshot.PreviewEnabled).IsFalse();
    }

    [Test]
    public async Task APreviewThatFailsPartWayStillSuppressesTheChunksItAuthored()
    {
        var clock = new FakePhysicsClock();
        var factory = new FakePhysicsTransportFactory();
        var partial = new ViewerPhysicsStageEdit[]
        {
            new(10UL, 12UL),
            new(12UL, 14UL),
        };
        factory.Transport.PreviewFailure = new ViewerPhysicsException(
            ViewerPhysicsFailureKind.Faulted,
            "The preview stopped after two chunks.",
            partial);
        await using var controller = NewController(factory, clock, debounceSeconds: 0.05d);
        await controller.EnableAsync();

        _ = Assert.Throws<ViewerPhysicsException>(
            () => controller.SetPreviewAsync(true).GetAwaiter().GetResult());
        foreach (ViewerPhysicsStageEdit edit in partial)
        {
            controller.NotifyStageChanged(ViewerPhysicsEditKind.Relevant, edit);
        }

        clock.NowSeconds += 1d;
        await controller.PumpAsync();

        // The failure is reported, but the half-written overlay must not also rebuild the world.
        await Assert.That(factory.Transport.Invalidations).IsEqualTo(0);
        await Assert.That(controller.Snapshot.PreviewEnabled).IsFalse();
        await Assert.That(factory.Transport.PreviewClears).IsGreaterThan(0);
    }

    [Test]
    public async Task SpeedChangesPacingWithoutEverChangingTheFixedStep()
    {
        var clock = new FakePhysicsClock();
        var factory = new FakePhysicsTransportFactory();
        factory.Transport.FixedStep = 1d / 60d;
        await using var controller = NewController(factory, clock);
        await controller.EnableAsync();
        controller.Play();

        clock.NowSeconds += 1d / 60d * 4d;
        await controller.PumpAsync();
        int atFullSpeed = factory.Transport.LastStepCount;

        controller.SetSpeed(0.5d);
        clock.NowSeconds += 1d / 60d * 4d;
        await controller.PumpAsync();
        int atHalfSpeed = factory.Transport.LastStepCount;

        await Assert.That(atFullSpeed).IsEqualTo(4);
        await Assert.That(atHalfSpeed).IsEqualTo(2);
        await Assert.That(factory.Transport.FixedStep).IsEqualTo(1d / 60d);
        await Assert.That(controller.Snapshot.Speed).IsEqualTo(0.5d);
    }

    [Test]
    public async Task PlaybackStopsAtTheAuthoredEndUnlessLoopingIsRequested()
    {
        var clock = new FakePhysicsClock();
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, clock);
        await controller.EnableAsync();
        controller.Play();

        factory.Transport.SetState(ViewerPhysicsRunState.Ended);
        clock.NowSeconds += 1d;
        await controller.PumpAsync();
        await Assert.That(controller.IsPlaying).IsFalse();

        await controller.SetLoopAsync(true);
        controller.Play();
        clock.NowSeconds += 1d;
        await controller.PumpAsync();

        await Assert.That(factory.Transport.Loop).IsTrue();
        await Assert.That(controller.IsPlaying).IsTrue();
    }

    [Test]
    public async Task StoppingClearsThePreviewAndRestoresTheAuthoredRenderState()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();
        _ = await controller.SetPreviewAsync(true);

        var target = new FakePhysicsOverrideTarget();
        factory.Transport.PublishFrame(bodies: 2);
        _ = controller.PumpRenderFrame(0.5d, target);
        await Assert.That(target.Applied).IsEqualTo(2);

        await controller.StopAsync();
        _ = controller.PumpRenderFrame(0.6d, target);

        await Assert.That(target.Cleared).IsEqualTo(1);
        await Assert.That(controller.Snapshot.PreviewEnabled).IsFalse();
        await Assert.That(factory.Transport.PreviewApplications).IsEqualTo(1);
        await Assert.That(factory.Transport.PreviewClears).IsEqualTo(1);
        await Assert.That(factory.Transport.Resets).IsEqualTo(1);
    }

    [Test]
    public async Task ARecoveredBackendReplaysTheLatestCompleteOverrideBatch()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        var first = new FakePhysicsOverrideTarget();
        factory.Transport.PublishFrame(bodies: 3);
        _ = controller.PumpRenderFrame(0.1d, first);
        await Assert.That(first.Applied).IsEqualTo(3);

        // A backend switch or a lost graphics context leaves the new backend retaining nothing.
        var second = new FakePhysicsOverrideTarget();
        controller.RequestOverrideReplay();
        ViewerPhysicsFramePumpResult replay = controller.PumpRenderFrame(0.2d, second);

        await Assert.That(replay.Applied).IsEqualTo(3);
        await Assert.That(replay.Ingested).IsFalse();
        await Assert.That(second.Applied).IsEqualTo(3);
    }

    [Test]
    public async Task ABackendThatCannotDrawOverridesNeverBlocksTheSimulation()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();
        factory.Transport.PublishFrame(bodies: 4);

        ViewerPhysicsFramePumpResult result = controller.PumpRenderFrame(
            0.1d,
            ViewerPhysicsUnsupportedOverrideTarget.Instance);

        await Assert.That(result.Applied).IsEqualTo(0);
        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task TheRenderFramePumpNeverWaitsForANewSimulationFrame()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();
        var target = new FakePhysicsOverrideTarget();

        ViewerPhysicsFramePumpResult empty = controller.PumpRenderFrame(0.1d, target);
        await Assert.That(empty.Ingested).IsFalse();
        await Assert.That(empty.Applied).IsEqualTo(0);

        factory.Transport.PublishFrame(bodies: 1);
        ViewerPhysicsFramePumpResult ingested = controller.PumpRenderFrame(0.2d, target);
        ViewerPhysicsFramePumpResult repeated = controller.PumpRenderFrame(0.3d, target);

        await Assert.That(ingested.Ingested).IsTrue();
        await Assert.That(repeated.Ingested).IsFalse();
        await Assert.That(repeated.Applied).IsEqualTo(1);
    }

    [Test]
    public async Task PacingIsBoundedSoAStalledShellCannotRequestAnUnboundedCatchUp()
    {
        var pacer = new ViewerPhysicsPacer(1d / 60d, maxStepsPerPump: 8);

        int steps = pacer.Advance(2d, 1d);

        await Assert.That(steps).IsEqualTo(8);
        await Assert.That(pacer.DroppedCatchUpSteps).IsGreaterThan(0L);
        await Assert.That(pacer.PendingSeconds).IsEqualTo(0d);
    }

    [Test]
    [NotInParallel]
    public async Task WarmPacingAllocatesNothing()
    {
        var pacer = new ViewerPhysicsPacer(1d / 60d);

        long allocated = AllocationWarmup.MeasureQuiet(
            _ => pacer.Advance(1d / 120d, 1d),
            1000);

        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    [NotInParallel]
    public async Task WarmRenderFramePumpsAllocateNothing()
    {
        var bridge = new ViewerPhysicsRenderBridge(ViewerPhysicsRenderCapacities.Default);
        var target = new FakePhysicsOverrideTarget();
        FakePhysicsTransport.PublishFrame(bridge.Channel, bodies: 8, revisionSeed: 1);

        long allocated = AllocationWarmup.MeasureQuiet(
            index => bridge.Pump(index * 0.001d, target),
            1000);

        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task BakeRequestsAreValidatedBeforeAnyStageWorkIsScheduled()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        ViewerPhysicsBakeOutcome refused = await controller.BakeAsync(
            new ViewerPhysicsBakeRequest(
                "session-only",
                0d,
                10d,
                1d,
                ViewerPhysicsBakePolicy.Overwrite,
                Save: false));

        await Assert.That(refused.Succeeded).IsFalse();
        await Assert.That(factory.Transport.Bakes).IsEqualTo(0);

        ViewerPhysicsBakeOutcome accepted = await controller.BakeAsync(
            new ViewerPhysicsBakeRequest(
                "baked.usda",
                0d,
                10d,
                1d,
                ViewerPhysicsBakePolicy.Overwrite,
                Save: true));

        await Assert.That(accepted.Succeeded).IsTrue();
        await Assert.That(factory.Transport.Bakes).IsEqualTo(1);
    }

    [Test]
    public async Task DisposingTwiceIsSafeAndCommandsAfterDisposalDoNothing()
    {
        var factory = new FakePhysicsTransportFactory();
        var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        await controller.DisposeAsync();
        await controller.DisposeAsync();
        await controller.StepOneFrameAsync();
        controller.Play();

        await Assert.That(controller.IsPlaying).IsFalse();
        await Assert.That(factory.Transport.LastStepCount).IsEqualTo(0);
    }

    [Test]
    public async Task BuildingPopulatesTheBindingTableSoOverridesReachAuthoredPrims()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        await Assert.That(factory.Transport.BindingLoads).IsEqualTo(1);
        await Assert.That(controller.Bindings.Bound).IsEqualTo(4);
        await Assert.That(controller.Bindings.HasBindings).IsTrue();
        await Assert.That(controller.Objects.Count).IsEqualTo(4);
        await Assert.That(controller.Objects[0].Path).IsEqualTo("/World/Body0");
        await Assert.That(controller.Objects[0].IsRendered).IsTrue();

        var target = new FakePhysicsOverrideTarget();
        factory.Transport.PublishFrame(bodies: 2);
        _ = controller.PumpRenderFrame(0.1d, target);

        // Every override the backend received resolved to the authored prim it must move; an
        // unpopulated table would have applied a batch that moved nothing at all.
        await Assert.That(target.Applied).IsEqualTo(2);
        await Assert.That(target.Resolved).IsEqualTo(2);
        await Assert.That(target.ResolvedPaths).Contains("/World/Body0");
        await Assert.That(target.ResolvedPaths).Contains("/World/Body1");
        await Assert.That(controller.Bindings.Unresolved).IsEqualTo(0);
    }

    [Test]
    public async Task RebuildingRebindsToTheNewIdentitiesAndDropsTheOldOnes()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        factory.Transport.Bindings.Clear();
        factory.Transport.Bindings.Add(new ViewerPhysicsBinding(
            7,
            PhysicsRenderObjectKind.RigidBody,
            "/World/Rebuilt",
            0,
            true,
            "Simulated body."));
        await controller.RebuildAsync();

        await Assert.That(factory.Transport.BindingLoads).IsEqualTo(2);
        await Assert.That(controller.Bindings.Bound).IsEqualTo(1);
        await Assert.That(controller.Objects.Count).IsEqualTo(1);
        await Assert.That(controller.Objects[0].Path).IsEqualTo("/World/Rebuilt");

        var target = new FakePhysicsOverrideTarget();
        factory.Transport.PublishFrame(bodies: 2);

        // The rebuild asked the render loop to restore the authored state first, so the frame that
        // applies the new world's poses is the one after the clear.
        _ = controller.PumpRenderFrame(0.1d, target);
        _ = controller.PumpRenderFrame(0.2d, target);

        // The identities the previous build published are no longer bound, so they resolve to
        // nothing and are counted rather than silently moving the prim they used to move.
        await Assert.That(target.Resolved).IsEqualTo(0);
        await Assert.That(controller.Bindings.Unresolved).IsEqualTo(2);
    }

    [Test]
    public async Task ABindingThatCannotBeStoredIsReportedInsteadOfFailingTheBuild()
    {
        var factory = new FakePhysicsTransportFactory();
        factory.Transport.Bindings.Add(new ViewerPhysicsBinding(
            9,
            PhysicsRenderObjectKind.RigidBody,
            "not-an-absolute-path",
            0,
            true,
            "Simulated body."));
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        await Assert.That(controller.Bindings.Bound).IsEqualTo(4);
        await Assert.That(controller.Bindings.Refused).IsEqualTo(1);
        await Assert.That(controller.Objects.Count).IsEqualTo(5);
        await Assert.That(controller.Objects[4].IsRendered).IsFalse();
        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task TheCapabilityMatrixOnlyClaimsWhatTheActiveBackendActuallyDraws()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        // Nothing has been applied yet, so no backend has told the controller it can draw.
        ViewerPhysicsCapabilityRow beforeAnyFrame = controller.Capabilities[0];
        await Assert.That(beforeAnyFrame.IsSupported).IsTrue();
        await Assert.That(beforeAnyFrame.IsRenderable).IsFalse();

        var refusing = new FakePhysicsOverrideTarget { SupportsPhysicsTransformOverrides = false };
        factory.Transport.PublishFrame(bodies: 2);
        _ = controller.PumpRenderFrame(0.1d, refusing);

        ViewerPhysicsCapabilityRow refused = controller.Capabilities[0];
        await Assert.That(refused.IsRenderable).IsFalse();
        await Assert.That(refused.StatusText).IsEqualTo("Not drawn");
        await Assert.That(refused.Detail).Contains("applies no transform overrides");

        var drawing = new FakePhysicsOverrideTarget();
        factory.Transport.PublishFrame(bodies: 2);
        _ = controller.PumpRenderFrame(0.2d, drawing);

        ViewerPhysicsCapabilityRow drawn = controller.Capabilities[0];
        await Assert.That(drawn.IsRenderable).IsTrue();
        await Assert.That(drawn.StatusText).IsEqualTo("Ready");

        // An unsupported CUDA domain is still reported as unsupported and never claims to draw.
        ViewerPhysicsCapabilityRow cuda = controller.Capabilities[1];
        await Assert.That(cuda.IsSupported).IsFalse();
        await Assert.That(cuda.IsRenderable).IsFalse();
    }

    [Test]
    public async Task ACapabilityWithNoBoundIdentityNeverClaimsToBeDrawn()
    {
        var factory = new FakePhysicsTransportFactory();
        factory.Transport.Bindings.Clear();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        var target = new FakePhysicsOverrideTarget();
        factory.Transport.PublishFrame(bodies: 2);
        _ = controller.PumpRenderFrame(0.1d, target);

        ViewerPhysicsCapabilityRow row = controller.Capabilities[0];
        await Assert.That(row.IsRenderable).IsFalse();
        await Assert.That(row.Detail).Contains("no simulated identity is bound");
    }

    [Test]
    public async Task ACapabilityIsNotDrawnUntilTheBackendItselfReportsResolvingTheBatch()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        // An in-process backend accepts a batch on the caller's thread and only resolves it later,
        // on its own thread, so accepting it proves nothing about what was drawn.
        var target = new FakePhysicsOverrideTarget { DeferReports = true };
        factory.Transport.PublishFrame(bodies: 3);
        ViewerPhysicsFramePumpResult accepted = controller.PumpRenderFrame(0.1d, target);

        await Assert.That(accepted.Applied).IsEqualTo(3);
        await Assert.That(controller.Bridge.HasAppliedBatch).IsFalse();
        await Assert.That(controller.Capabilities[0].IsRenderable).IsFalse();
        await Assert.That(controller.Capabilities[0].Detail)
            .Contains("has not reported drawing a pose yet");

        target.FlushReport();
        _ = controller.PumpRenderFrame(0.2d, target);

        await Assert.That(controller.Bridge.HasAppliedBatch).IsTrue();
        await Assert.That(controller.Bridge.AppliedOverrides).IsEqualTo(3);
        await Assert.That(controller.Capabilities[0].IsRenderable).IsTrue();
    }

    [Test]
    public async Task ABackendThatResolvesNothingIsCountedAsUnresolvedRatherThanApplied()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        // The backend takes the whole batch for staging but its own resolution finds no prim, so
        // every pose was dropped even though the accepted count looks like a success.
        var target = new FakePhysicsOverrideTarget { ResolveNothing = true };
        factory.Transport.PublishFrame(bodies: 4);
        ViewerPhysicsFramePumpResult result = controller.PumpRenderFrame(0.1d, target);

        await Assert.That(result.Applied).IsEqualTo(4);
        await Assert.That(controller.Bridge.AppliedOverrides).IsEqualTo(0);
        await Assert.That(controller.Bridge.HasAppliedBatch).IsFalse();
        await Assert.That(controller.Bindings.Unresolved).IsEqualTo(4);
        foreach (ViewerPhysicsCapabilityRow row in controller.Capabilities)
        {
            await Assert.That(row.IsRenderable).IsFalse();
        }
    }

    [Test]
    public async Task SwitchingToABackendThatHasNotDrawnYetWithdrawsTheDrawnClaim()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        var drawing = new FakePhysicsOverrideTarget();
        factory.Transport.PublishFrame(bodies: 2);
        _ = controller.PumpRenderFrame(0.1d, drawing);
        await Assert.That(controller.Capabilities[0].IsRenderable).IsTrue();

        // A context loss hands the replay to a backend that has drawn nothing of its own yet.
        var replacement = new FakePhysicsOverrideTarget { DeferReports = true };
        controller.RequestOverrideReplay();
        _ = controller.PumpRenderFrame(0.2d, replacement);

        await Assert.That(controller.Bridge.HasAppliedBatch).IsFalse();
        await Assert.That(controller.Capabilities[0].IsRenderable).IsFalse();

        replacement.FlushReport();
        _ = controller.PumpRenderFrame(0.3d, replacement);
        await Assert.That(controller.Capabilities[0].IsRenderable).IsTrue();
    }

    [Test]
    public async Task TheCapabilityMatrixIsNotRebuiltWhileNothingItDependsOnChanges()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        var target = new FakePhysicsOverrideTarget();
        factory.Transport.PublishFrame(bodies: 2);
        _ = controller.PumpRenderFrame(0.1d, target);

        IReadOnlyList<ViewerPhysicsCapabilityRow> first = controller.Capabilities;
        for (int index = 0; index < 32; index++)
        {
            factory.Transport.PublishFrame(bodies: 2);
            _ = controller.PumpRenderFrame(0.2d + index, target);
            await Assert.That(ReferenceEquals(controller.Capabilities, first)).IsTrue();
        }

        // Only a change in what the matrix is derived from produces a new list.
        controller.RequestOverrideReplay();
        _ = controller.PumpRenderFrame(64d, new FakePhysicsOverrideTarget { DeferReports = true });
        await Assert.That(ReferenceEquals(controller.Capabilities, first)).IsFalse();
    }

    [Test]
    [NotInParallel]
    public async Task ReadingTheCapabilityMatrixEveryStepAllocatesNothingWhenWarm()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        var target = new FakePhysicsOverrideTarget();
        factory.Transport.PublishFrame(bodies: 2);
        _ = controller.PumpRenderFrame(0.1d, target);
        int warmed = 0;
        _ = AllocationWarmup.UntilQuiet(_ => warmed += controller.Capabilities.Count);
        await Assert.That(warmed).IsGreaterThan(0);

        long before = GC.GetAllocatedBytesForCurrentThread();
        int total = 0;
        for (int index = 0; index < 1000; index++)
        {
            total += controller.Capabilities.Count;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(total).IsEqualTo(2000);
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    [NotInParallel]
    public async Task ReadingTheDiagnosticListEveryStepAllocatesNothingWhenWarm()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();
        factory.Transport.PublishDiagnostic("CODE_A", "The world was built.");

        int warmed = 0;
        _ = AllocationWarmup.UntilQuiet(_ => warmed += controller.Diagnostics.Count);
        await Assert.That(warmed).IsGreaterThan(0);

        // The measured loop is its own method so that its first execution pays for whatever tiered
        // or on-stack-replacement compilation it needs, and the assertion still measures exactly
        // zero rather than being loosened to a tolerance.
        long allocated = MeasureDiagnosticReads(controller, out int total);
        if (allocated != 0)
        {
            allocated = MeasureDiagnosticReads(controller, out total);
        }

        await Assert.That(total).IsEqualTo(1000);
        await Assert.That(allocated).IsEqualTo(0L);
    }

    private static long MeasureDiagnosticReads(ViewerPhysicsController controller, out int total)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        int count = 0;
        for (int index = 0; index < 1000; index++)
        {
            count += controller.Diagnostics.Count;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        total = count;
        return allocated;
    }

    [Test]
    public async Task ATransportThatRebuildsItsMetadataOnEveryReadIsNotTakenAtItsWord()
    {
        // The production transport builds both lists per read, so the controller cannot rely on the
        // transport handing back the same reference; this asserts the fake really does churn, which
        // is what makes the caching assertions in this file meaningful rather than lucky.
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();
        factory.Transport.PublishDiagnostic("CODE_A", "The world was built.");

        FakePhysicsTransport transport = factory.Transport;
        await Assert.That(ReferenceEquals(transport.Capabilities, transport.Capabilities)).IsFalse();
        await Assert.That(ReferenceEquals(transport.Diagnostics, transport.Diagnostics)).IsFalse();

        IReadOnlyList<ViewerPhysicsCapabilityRow> capabilities = controller.Capabilities;
        IReadOnlyList<ViewerPhysicsDiagnosticRow> diagnostics = controller.Diagnostics;
        for (int index = 0; index < 64; index++)
        {
            await Assert.That(ReferenceEquals(controller.Capabilities, capabilities)).IsTrue();
            await Assert.That(ReferenceEquals(controller.Diagnostics, diagnostics)).IsTrue();
        }

        await Assert.That(transport.CapabilityReads).IsGreaterThan(64);
        await Assert.That(transport.DiagnosticReads).IsGreaterThan(64);
    }

    [Test]
    public async Task AChangedCapabilityFromTheTransportIsPublishedThroughTheCache()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        IReadOnlyList<ViewerPhysicsCapabilityRow> before = controller.Capabilities;
        await Assert.That(before[1].Name).IsEqualTo("Cuda");
        await Assert.That(before[1].IsSupported).IsFalse();

        // A CUDA device appearing is a content change behind an unchanged instance count, which the
        // cache must notice: masking it would leave the inspector claiming the domain is missing.
        factory.Transport.SetCapabilitySupported("Cuda", supported: true);

        IReadOnlyList<ViewerPhysicsCapabilityRow> after = controller.Capabilities;
        await Assert.That(ReferenceEquals(after, before)).IsFalse();
        await Assert.That(after[1].IsSupported).IsTrue();
        await Assert.That(ReferenceEquals(controller.Capabilities, after)).IsTrue();
    }

    [Test]
    public async Task EveryDiagnosticChangeReachesTheInspectorThroughTheCache()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        IReadOnlyList<ViewerPhysicsDiagnosticRow> empty = controller.Diagnostics;
        await Assert.That(empty).IsEmpty();

        factory.Transport.PublishDiagnostic("CODE_A", "A collider was skipped.");
        IReadOnlyList<ViewerPhysicsDiagnosticRow> added = controller.Diagnostics;
        await Assert.That(ReferenceEquals(added, empty)).IsFalse();
        await Assert.That(added.Count).IsEqualTo(1);
        await Assert.That(added[0].Message).IsEqualTo("A collider was skipped.");

        factory.Transport.PublishDiagnostic("CODE_B", "A joint was skipped.");
        IReadOnlyList<ViewerPhysicsDiagnosticRow> second = controller.Diagnostics;
        await Assert.That(ReferenceEquals(second, added)).IsFalse();
        await Assert.That(second.Count).IsEqualTo(2);

        factory.Transport.ClearDiagnostics();
        IReadOnlyList<ViewerPhysicsDiagnosticRow> cleared = controller.Diagnostics;
        await Assert.That(ReferenceEquals(cleared, second)).IsFalse();
        await Assert.That(cleared).IsEmpty();
    }

    [Test]
    public async Task TheBindingRevisionOnlyMovesWhenTheBindingsAreRebuilt()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        ulong afterEnable = controller.BindingRevision;
        await Assert.That(afterEnable).IsGreaterThan(0UL);

        var target = new FakePhysicsOverrideTarget();
        for (int index = 0; index < 8; index++)
        {
            factory.Transport.PublishFrame(bodies: 2);
            _ = controller.PumpRenderFrame(0.1d + index, target);
        }

        // Simulating and drawing never rebinds, so the object list the inspector keys off is stable.
        await Assert.That(controller.BindingRevision).IsEqualTo(afterEnable);

        await controller.RebuildAsync();
        await Assert.That(controller.BindingRevision).IsGreaterThan(afterEnable);
    }

    [Test]
    public async Task AFailingOverrideApplyDisablesTheBridgeInsteadOfEscapingTheRenderLoop()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        var target = new FakePhysicsOverrideTarget
        {
            ApplyFailure = new InvalidOperationException("the device was lost"),
        };
        factory.Transport.PublishFrame(bodies: 2);

        ViewerPhysicsFramePumpResult result = controller.PumpRenderFrame(0.1d, target);

        await Assert.That(result.Applied).IsEqualTo(0);
        await Assert.That(controller.IsBridgeDisabled).IsTrue();
        await Assert.That(target.Cleared).IsEqualTo(1);
        await Assert.That(controller.Snapshot.Error).Contains("the device was lost");

        // The render loop keeps calling; every later frame is a no-op rather than a new failure.
        factory.Transport.PublishFrame(bodies: 2);
        ViewerPhysicsFramePumpResult after = controller.PumpRenderFrame(0.2d, target);
        await Assert.That(after.Applied).IsEqualTo(0);
        await Assert.That(target.Cleared).IsEqualTo(1);
    }

    [Test]
    public async Task ABridgeThatCannotEvenClearStillDoesNotFailTheRenderLoop()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        var target = new FakePhysicsOverrideTarget
        {
            ApplyFailure = new InvalidOperationException("apply failed"),
            ClearFailure = new InvalidOperationException("clear failed"),
        };
        factory.Transport.PublishFrame(bodies: 2);

        _ = controller.PumpRenderFrame(0.1d, target);

        await Assert.That(controller.IsBridgeDisabled).IsTrue();
        await Assert.That(controller.Snapshot.Error).Contains("could not be restored");
    }

    [Test]
    public async Task ADisabledBridgeIsReenabledByTheRebuildTheUserAsksFor()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        var target = new FakePhysicsOverrideTarget
        {
            ApplyFailure = new InvalidOperationException("apply failed"),
        };
        factory.Transport.PublishFrame(bodies: 2);
        _ = controller.PumpRenderFrame(0.1d, target);
        await Assert.That(controller.IsBridgeDisabled).IsTrue();

        target.ApplyFailure = null;
        controller.ResetBridgeFailure();
        await controller.RebuildAsync();
        factory.Transport.PublishFrame(bodies: 2);

        // The rebuild restores the authored state on the first frame and applies the new world on
        // the next one.
        _ = controller.PumpRenderFrame(0.2d, target);
        ViewerPhysicsFramePumpResult recovered = controller.PumpRenderFrame(0.3d, target);

        await Assert.That(controller.IsBridgeDisabled).IsFalse();
        await Assert.That(recovered.Applied).IsEqualTo(2);
        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task APreviewThatDoesNotCompleteIsReportedAsAFailureNotASuccess()
    {
        var factory = new FakePhysicsTransportFactory();
        factory.Transport.PreviewFailure = new ViewerPhysicsException(
            ViewerPhysicsFailureKind.Rejected,
            "The preview is not supported for this stage.");
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        ViewerPhysicsException thrown = Assert.Throws<ViewerPhysicsException>(
            () => controller.SetPreviewAsync(true).GetAwaiter().GetResult());

        await Assert.That(thrown.Kind).IsEqualTo(ViewerPhysicsFailureKind.Rejected);
        await Assert.That(controller.Snapshot.PreviewEnabled).IsFalse();
        await Assert.That(controller.Snapshot.Error)
            .IsEqualTo("The preview is not supported for this stage.");
    }

    [Test]
    public async Task AFailedPreviewIsClearedByTheNextPumpSoNoStalePosesSurvive()
    {
        var clock = new FakePhysicsClock();
        var factory = new FakePhysicsTransportFactory();
        factory.Transport.PreviewFailure = new ViewerPhysicsException(
            ViewerPhysicsFailureKind.Faulted,
            "The preview failed part way through.");
        await using var controller = NewController(factory, clock);
        await controller.EnableAsync();

        try
        {
            _ = await controller.SetPreviewAsync(true);
        }
        catch (ViewerPhysicsException)
        {
            // The failure is the subject of the previous test; this one is about the clean-up.
        }

        factory.Transport.PreviewFailure = null;
        await controller.PumpAsync();

        await Assert.That(factory.Transport.PreviewClears).IsEqualTo(1);
        await Assert.That(controller.Snapshot.PreviewEnabled).IsFalse();
    }

    [Test]
    public async Task AWorldInvalidationClearsThePreviewItCanNoLongerJustify()
    {
        var clock = new FakePhysicsClock();
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, clock, debounceSeconds: 0.05d);
        await controller.EnableAsync();
        _ = await controller.SetPreviewAsync(true);

        controller.NotifyStageChanged(ViewerPhysicsEditKind.Relevant);
        clock.NowSeconds += 1d;
        await controller.PumpAsync();

        await Assert.That(factory.Transport.Invalidations).IsEqualTo(1);
        await Assert.That(factory.Transport.PreviewClears).IsEqualTo(1);
        await Assert.That(controller.Snapshot.PreviewEnabled).IsFalse();
    }

    [Test]
    public async Task ARebuildClearsThePreviewAuthoredFromTheWorldItDiscards()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();
        _ = await controller.SetPreviewAsync(true);

        await controller.RebuildAsync();

        await Assert.That(factory.Transport.PreviewClears).IsEqualTo(1);
        await Assert.That(controller.Snapshot.PreviewEnabled).IsFalse();
    }

    [Test]
    public async Task AFaultedWorldClearsThePreviewOnceACommandCanReachTheOverlay()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();
        _ = await controller.SetPreviewAsync(true);

        factory.Transport.StepFailure = new ViewerPhysicsException(
            ViewerPhysicsFailureKind.Faulted,
            "The solver faulted.");
        await controller.StepOneFrameAsync();

        await Assert.That(controller.Snapshot.PreviewEnabled).IsFalse();

        await controller.PumpAsync();

        await Assert.That(factory.Transport.PreviewClears).IsEqualTo(1);
    }

    [Test]
    public async Task PacedStepsNeverReportThemselvesAsABusyUserCommand()
    {
        var clock = new FakePhysicsClock();
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, clock);
        await controller.EnableAsync();

        var busyStates = new List<bool>();
        controller.StatusChanged += snapshot => busyStates.Add(snapshot.IsBusy);
        controller.Play();

        for (int tick = 0; tick < 16; tick++)
        {
            clock.NowSeconds += 1d / 60d;
            await controller.PumpAsync();
        }

        // The status still moves - the time code advances - but the toolbar is never disabled by
        // work the user did not ask for.
        await Assert.That(busyStates.Count).IsGreaterThan(0);
        await Assert.That(busyStates).DoesNotContain(true);
        await Assert.That(factory.Transport.LastStepCount).IsGreaterThan(0);
    }

    [Test]
    public async Task AUserCommandStillReportsItselfAsBusy()
    {
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, new FakePhysicsClock());
        await controller.EnableAsync();

        var busyStates = new List<bool>();
        controller.StatusChanged += snapshot => busyStates.Add(snapshot.IsBusy);
        await controller.StepOneFrameAsync();

        await Assert.That(busyStates).Contains(true);
        await Assert.That(controller.IsBusy).IsFalse();
    }

    [Test]
    public async Task PacingIsOnlyConsumedWhenAStepCanActuallyBeIssued()
    {
        var clock = new FakePhysicsClock();
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, clock);
        await controller.EnableAsync();
        controller.Play();

        // A seek owns the transport, so the pump that lands while it is in flight must not consume
        // the wall clock it could not spend.
        factory.Transport.BlockSeek = true;
        Task seek = controller.SeekAsync(4d);
        await factory.Transport.SeekEntered.Task;

        clock.NowSeconds += 1d / 60d * 4d;
        await controller.PumpAsync();
        await Assert.That(factory.Transport.LastStepCount).IsEqualTo(0);

        factory.Transport.BlockSeek = false;
        await controller.SeekAsync(4d);
        await seek;

        controller.Play();
        clock.NowSeconds += 1d / 60d * 4d;
        await controller.PumpAsync();

        await Assert.That(factory.Transport.LastStepCount).IsEqualTo(4);
    }

    [Test]
    public async Task OverlappingPumpTicksNeverAdvancePacingTwiceForTheSameWallClock()
    {
        var clock = new FakePhysicsClock();
        var factory = new FakePhysicsTransportFactory();
        await using var controller = NewController(factory, clock);
        await controller.EnableAsync();
        controller.Play();

        factory.Transport.BlockStep = true;
        clock.NowSeconds += 1d / 60d * 4d;
        ValueTask first = controller.PumpAsync();
        await factory.Transport.StepEntered.Task;

        clock.NowSeconds += 1d / 60d * 4d;
        await controller.PumpAsync();

        factory.Transport.ReleaseStep();
        await first;

        // The second tick found the first still running and did nothing, so exactly one step batch
        // was issued for the elapsed time rather than two overlapping ones.
        await Assert.That(factory.Transport.StepCalls).IsEqualTo(1);
        await Assert.That(factory.Transport.LastStepCount).IsEqualTo(4);
    }

    private static ViewerPhysicsController NewController(
        FakePhysicsTransportFactory factory,
        FakePhysicsClock clock,
        double debounceSeconds = 0.25d) =>
        new(factory, clock, ViewerPhysicsRenderCapacities.Default, 8, debounceSeconds);

    private sealed class FakePhysicsClock : IViewerPhysicsClock
    {
        public double NowSeconds { get; set; }
    }

    private sealed class FakePhysicsOverrideTarget : IViewerPhysicsOverrideTarget
    {
        private ViewerPhysicsOverrideReport _report;
        private bool _hasReport;
        private ViewerPhysicsOverrideReport _deferred;
        private bool _hasDeferred;

        public bool SupportsPhysicsTransformOverrides { get; set; } = true;

        public int Applied { get; private set; }

        public int Cleared { get; private set; }

        public int Resolved { get; private set; }

        public int Accepted { get; private set; }

        public ulong LastRevision { get; private set; }

        public List<string> ResolvedPaths { get; } = [];

        public Exception? ApplyFailure { get; set; }

        public Exception? ClearFailure { get; set; }

        /// <summary>
        /// Models an in-process backend whose own thread has not consumed the batch yet: the batch
        /// is accepted for delivery but nothing is reported until <see cref="FlushReport"/> runs.
        /// </summary>
        public bool DeferReports { get; set; }

        /// <summary>
        /// Models a backend that stages the whole batch but resolves none of it, which is what a
        /// stale or empty scene index looks like from the caller's side.
        /// </summary>
        public bool ResolveNothing { get; set; }

        public int ApplyPhysicsOverrides(
            in PhysicsRenderOverrideView overrides,
            PhysicsRenderBindingTable bindings)
        {
            ArgumentNullException.ThrowIfNull(bindings);
            if (ApplyFailure is { } failure)
            {
                throw failure;
            }

            ResolvedPaths.Clear();
            Resolved = 0;
            if (!ResolveNothing)
            {
                foreach (PhysicsRenderTransformOverride item in overrides.Items)
                {
                    if (bindings.TryResolve(item.Id, out PhysicsRenderBinding binding))
                    {
                        Resolved++;
                        ResolvedPaths.Add(binding.PrimPath);
                    }
                }
            }

            Applied = Resolved;
            Accepted = overrides.Count;
            LastRevision = overrides.Revision;
            var report = new ViewerPhysicsOverrideReport(
                overrides.Revision,
                Resolved,
                Math.Max(0, overrides.Count - Resolved));
            if (DeferReports)
            {
                _deferred = report;
                _hasDeferred = true;
            }
            else
            {
                _report = report;
                _hasReport = true;
            }

            return Accepted;
        }

        /// <summary>Models a backend that draws rigid poses but uploads no deformable geometry.</summary>
        public int ApplyPhysicsDeformations(
            in PhysicsRenderDeformationView deformations,
            PhysicsRenderBindingTable bindings)
        {
            ArgumentNullException.ThrowIfNull(bindings);
            return 0;
        }

        public bool TryTakeOverrideReport(out ViewerPhysicsOverrideReport report)
        {
            if (!_hasReport)
            {
                report = default;
                return false;
            }

            report = _report;
            _hasReport = false;
            return true;
        }

        /// <summary>Publishes the deferred report, as the owning thread would after drawing.</summary>
        public void FlushReport()
        {
            if (!_hasDeferred)
            {
                return;
            }

            _report = _deferred;
            _hasReport = true;
            _hasDeferred = false;
        }

        public void ClearPhysicsOverrides()
        {
            if (ClearFailure is { } failure)
            {
                throw failure;
            }

            Applied = 0;
            Resolved = 0;
            Accepted = 0;
            _hasReport = false;
            _hasDeferred = false;
            ResolvedPaths.Clear();
            Cleared++;
        }
    }

    private sealed class FakePhysicsTransportFactory : IViewerPhysicsTransportFactory
    {
        public FakePhysicsTransport Transport { get; } = new();

        public int Created { get; private set; }

        public ValueTask<IViewerPhysicsTransport> CreateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Created++;
            return ValueTask.FromResult<IViewerPhysicsTransport>(Transport);
        }
    }

    private sealed class FakePhysicsTransport : IViewerPhysicsTransport
    {
        private readonly List<ViewerPhysicsCapabilitySupport> _capabilities =
        [
            new("RigidBodies", true, PhysicsRenderDomain.RigidBody, "Simulated on the CPU."),
            new("Cuda", false, null, "No CUDA device is available."),
        ];

        private readonly List<ViewerPhysicsDiagnosticRow> _diagnostics = [];

        private readonly IReadOnlyList<ViewerPhysicsCapabilitySupport>[] _capabilitySnapshots =
            new IReadOnlyList<ViewerPhysicsCapabilitySupport>[4];

        private readonly IReadOnlyList<ViewerPhysicsDiagnosticRow>[] _diagnosticSnapshots =
            new IReadOnlyList<ViewerPhysicsDiagnosticRow>[4];

        private ViewerPhysicsRunState _state = ViewerPhysicsRunState.Paused;
        private ulong _revision;
        private ulong _stepIndex;
        private ulong _changeSerial;
        private double _timeCode;
        private int _publishedBodies;

        public FakePhysicsTransport()
        {
            RefreshCapabilitySnapshots();
            RefreshDiagnosticSnapshots();
        }

        public TaskCompletionSource BuildEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SeekEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool BlockBuild { get; set; }

        public bool BlockSeek { get; set; }

        public bool SeekWasCanceled { get; private set; }

        public double FixedStep { get; set; } = 1d / 60d;

        public int Builds { get; private set; }

        public int Resets { get; private set; }

        public int Invalidations { get; private set; }

        public int Bakes { get; private set; }

        public int PreviewApplications { get; private set; }

        public int LastStepCount { get; private set; }

        public double LastSeekTimeCode { get; private set; }

        public bool Loop { get; private set; }

        public bool Disposed { get; private set; }

        public ViewerPhysicsException? StepFailure { get; set; }

        public ViewerPhysicsException? PreviewFailure { get; set; }

        public ViewerPhysicsException? BindingFailure { get; set; }

        public int BindingLoads { get; private set; }

        public int PreviewClears { get; private set; }

        public ViewerPhysicsStageEdit LastPreviewEdit { get; private set; }

        public IReadOnlyList<ViewerPhysicsStageEdit> LastPreviewEdits { get; private set; } = [];

        /// <summary>Gets or sets how many chunk changes one preview apply reports.</summary>
        public int PreviewChunks { get; set; } = 1;

        public List<ViewerPhysicsBinding> Bindings { get; } =
        [
            new(1, PhysicsRenderObjectKind.RigidBody, "/World/Body0", 0, true, "Simulated body."),
            new(2, PhysicsRenderObjectKind.RigidBody, "/World/Body1", 0, true, "Simulated body."),
            new(3, PhysicsRenderObjectKind.RigidBody, "/World/Body2", 0, true, "Simulated body."),
            new(4, PhysicsRenderObjectKind.RigidBody, "/World/Body3", 0, true, "Simulated body."),
        ];

        public int SkippedBindings { get; set; }

        public ViewerPhysicsTransportStatus Status =>
            new(_state, _revision, _stepIndex, _timeCode, _timeCode, 0d, 0, 0);

        public double FixedStepSeconds => FixedStep;

        public double StartTimeCode => 0d;

        public double EndTimeCode => 24d;

        // A real transport rebuilds both lists on every read, so the fake hands back a different
        // instance every time as well: a fake that returned one stable reference would let a
        // controller that never caches look correct. The instances are pre-built and rotated so the
        // fake itself allocates nothing, which keeps the controller's warm allocation measurable.
        public IReadOnlyList<ViewerPhysicsCapabilitySupport> Capabilities =>
            _capabilitySnapshots[CapabilityReads++ % _capabilitySnapshots.Length];

        public IReadOnlyList<ViewerPhysicsDiagnosticRow> Diagnostics =>
            _diagnosticSnapshots[DiagnosticReads++ % _diagnosticSnapshots.Length];

        public int CapabilityReads { get; private set; }

        public int DiagnosticReads { get; private set; }

        public void SetCapabilitySupported(string name, bool supported)
        {
            for (int index = 0; index < _capabilities.Count; index++)
            {
                if (_capabilities[index].Name == name)
                {
                    _capabilities[index] = _capabilities[index] with { IsSupported = supported };
                    RefreshCapabilitySnapshots();
                    return;
                }
            }

            throw new InvalidOperationException($"The fake has no '{name}' capability.");
        }

        public void PublishDiagnostic(string code, string message)
        {
            _diagnostics.Add(new ViewerPhysicsDiagnosticRow("Warning", "Build", code, message));
            RefreshDiagnosticSnapshots();
        }

        public void ClearDiagnostics()
        {
            _diagnostics.Clear();
            RefreshDiagnosticSnapshots();
        }

        private void RefreshCapabilitySnapshots()
        {
            for (int index = 0; index < _capabilitySnapshots.Length; index++)
            {
                _capabilitySnapshots[index] = new List<ViewerPhysicsCapabilitySupport>(_capabilities);
            }
        }

        private void RefreshDiagnosticSnapshots()
        {
            for (int index = 0; index < _diagnosticSnapshots.Length; index++)
            {
                _diagnosticSnapshots[index] = new List<ViewerPhysicsDiagnosticRow>(_diagnostics);
            }
        }

        /// <summary>Every runtime command batch the fake was asked to stage.</summary>
        public List<IReadOnlyList<ViewerPhysicsRuntimeCommand>> CommandBatches { get; } = [];

        /// <summary>Set to refuse every submitted command, modelling an unsupported backend.</summary>
        public bool RefuseCommands { get; set; }

        /// <summary>The document the inspector read is built from.</summary>
        public ViewerPhysicsExtractionDocument InspectorDocument { get; set; } =
            ViewerPhysicsExtractionDocument.Empty;

        /// <summary>The number of times the inspector document was read.</summary>
        public int InspectorLoads { get; private set; }

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
            InspectorLoads++;
            return ValueTask.FromResult(InspectorDocument);
        }

        public ValueTask<ViewerPhysicsBindingSet> LoadBindingsAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BindingLoads++;
            if (BindingFailure is { } failure)
            {
                return ValueTask.FromException<ViewerPhysicsBindingSet>(failure);
            }

            return ValueTask.FromResult(new ViewerPhysicsBindingSet(
                _revision,
                Bindings.ToArray(),
                SkippedBindings,
                "Bound from the fake extraction."));
        }

        public void SetState(ViewerPhysicsRunState state) => _state = state;

        public void PublishFrame(int bodies)
        {
            _publishedBodies = bodies;
            _revision++;
        }

        public static void PublishFrame(PhysicsRenderChannel channel, int bodies, ulong revisionSeed)
        {
            ArgumentNullException.ThrowIfNull(channel);
            PhysicsRenderSnapshot? snapshot = channel.TryBeginWrite();
            if (snapshot is null)
            {
                return;
            }

            snapshot.BeginWrite(revisionSeed, 1, revisionSeed * 0.1d, revisionSeed * 0.1d, 1d / 60d);
            for (int index = 0; index < bodies; index++)
            {
                _ = snapshot.TryAddBody(new PhysicsRenderBodyState(
                    new PhysicsRenderObjectId((ulong)index + 1, PhysicsRenderObjectKind.RigidBody),
                    new UsdVec3d(index, revisionSeed, 0d),
                    PhysicsRenderOrientation.Identity,
                    IsSleeping: false,
                    IsKinematic: false));
            }

            snapshot.EndWrite();
            _ = channel.Publish(snapshot);
        }

        public async Task BuildAsync(CancellationToken cancellationToken)
        {
            Builds++;
            _state = ViewerPhysicsRunState.Paused;
            if (!BlockBuild)
            {
                return;
            }

            BuildEntered.TrySetResult();
            await BuildGate.Task.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        public TaskCompletionSource BuildGate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseBuild() => BuildGate.TrySetResult();

        public Task ResetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Resets++;
            _timeCode = 0d;
            _stepIndex = 0;
            _state = ViewerPhysicsRunState.Paused;
            return Task.CompletedTask;
        }

        public async Task SeekAsync(double timeCode, CancellationToken cancellationToken)
        {
            if (BlockSeek)
            {
                SeekEntered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    SeekWasCanceled = true;
                    throw;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            LastSeekTimeCode = timeCode;
            _timeCode = timeCode;
        }

        public Task StepAsync(int steps, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (StepFailure is { } failure)
            {
                return Task.FromException(failure);
            }

            StepCalls++;
            LastStepCount = steps;
            _stepIndex += (ulong)steps;
            _timeCode += steps * FixedStep;
            if (!BlockStep)
            {
                return Task.CompletedTask;
            }

            StepEntered.TrySetResult();
            return StepGate.Task;
        }

        public bool BlockStep { get; set; }

        public int StepCalls { get; private set; }

        public TaskCompletionSource StepEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StepGate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseStep() => StepGate.TrySetResult();

        public Task SetLoopAsync(bool loop, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Loop = loop;
            if (loop && _state == ViewerPhysicsRunState.Ended)
            {
                _state = ViewerPhysicsRunState.Paused;
            }

            return Task.CompletedTask;
        }

        public Task InvalidateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invalidations++;
            _state = ViewerPhysicsRunState.Invalidated;
            return Task.CompletedTask;
        }

        public bool TryPublishLatestFrame(PhysicsRenderChannel channel)
        {
            ArgumentNullException.ThrowIfNull(channel);
            if (_publishedBodies == 0)
            {
                return false;
            }

            PublishFrame(channel, _publishedBodies, _revision);
            _publishedBodies = 0;
            return true;
        }

        public Task<ViewerPhysicsPreviewOutcome> ApplyPreviewAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (enabled)
            {
                PreviewApplications++;
            }
            else
            {
                PreviewClears++;
            }

            if (PreviewFailure is { } failure)
            {
                return Task.FromException<ViewerPhysicsPreviewOutcome>(failure);
            }

            // A real apply authors one change per chunk, so the fake reports the exact pair of
            // every chunk it "authored" rather than a bracketing range.
            int chunks = enabled ? Math.Max(1, PreviewChunks) : 1;
            var edits = new ViewerPhysicsStageEdit[chunks];
            for (int index = 0; index < chunks; index++)
            {
                ulong before = _changeSerial;
                _changeSerial += 2;
                edits[index] = new ViewerPhysicsStageEdit(before, _changeSerial);
            }

            LastPreviewEdits = edits;
            LastPreviewEdit = edits[^1];
            return Task.FromResult(new ViewerPhysicsPreviewOutcome(
                enabled ? "Preview applied." : "Preview cleared.",
                edits,
                enabled ? Bindings.Count : 0));
        }

        /// <summary>Advances the stage serial as an unrelated external edit would.</summary>
        public ViewerPhysicsStageEdit AuthorExternalEdit()
        {
            ulong before = _changeSerial;
            _changeSerial += 2;
            return new ViewerPhysicsStageEdit(before, _changeSerial);
        }

        public Task<ViewerPhysicsBakeOutcome> BakeAsync(
            ViewerPhysicsBakeRequest request,
            IProgress<ViewerPhysicsBakeProgress>? progress,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            Bakes++;
            progress?.Report(new ViewerPhysicsBakeProgress(1, 1, request.StartTimeCode));
            return Task.FromResult(
                new ViewerPhysicsBakeOutcome(true, request.Save, 1, "Baked 1 sample."));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            BuildGate.TrySetResult();
            StepGate.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
