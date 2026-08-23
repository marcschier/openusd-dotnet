// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics;
using OpenUsd.Physics.Baking;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Drives the viewer's physics controller against the real transport on a real physics stage, so
/// the deterministic fake-transport suite cannot be the only thing that has ever exercised the
/// wiring.
/// </summary>
/// <remarks>
/// <para>
/// The stage carries a physics scene, a static collider, and two rigid bodies, because a smoke that
/// runs against a stage with no physics on it can only ever assert that nothing happened. The first
/// test asserts that real extraction produced real identities, that those identities reached the
/// binding table, and that stopping hands the backend a clear. The second asserts that the
/// transport published a frame the controller ingested and applied to the backend.
/// </para>
/// <para>
/// The tests skip only when a runtime is genuinely absent: the OpenUSD runtime, without which no
/// stage can be opened at all, and the separately staged native solver, without which there is
/// nothing to step. When a runtime is staged the assertions are real, and a simulation that
/// silently produces nothing fails here.
/// </para>
/// </remarks>
[NotInParallel("ViewerPhysicsNativeSmoke")]
public sealed class ViewerPhysicsNativeSmokeTests
{
    [Test]
    public async Task ARealStageExtractsBindsAndClearsThroughTheRenderBridge()
    {
        await using UsdStageScheduler scheduler = OpenSchedulerOrSkip("viewer-physics-smoke.usda");
        await using ViewerPhysicsController controller = NewController(scheduler);

        await controller.EnableAsync();
        await Assert.That(controller.IsEnabled).IsTrue();
        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);

        // Extraction has to find the authored rigid bodies, and every one of them has to reach the
        // binding table: an unbound identity is an override the backend would silently drop.
        ViewerPhysicsBindingStats bindings = controller.Bindings;
        await Assert.That(bindings.Bound).IsGreaterThanOrEqualTo(2);
        await Assert.That(bindings.Refused).IsEqualTo(0);
        await Assert.That(controller.Objects.Count).IsGreaterThanOrEqualTo(2);

        string[] boundPaths = [.. controller.Objects.Select(row => row.PrimPath)];
        await Assert.That(boundPaths).Contains("/FallingBox");
        await Assert.That(boundPaths).Contains("/FallingSphere");

        // The identities the extractor produced have to be the identities the render bridge
        // resolves; a table that binds nothing is exactly the bug this smoke exists to catch.
        var probe = new RecordingOverrideTarget();

        // Before any batch reaches a backend nothing can be drawn, so no capability may claim it.
        foreach (ViewerPhysicsCapabilityRow row in controller.Capabilities)
        {
            await Assert.That(row.IsRenderable).IsFalse();
        }

        _ = controller.PumpRenderFrame(0.016d, probe);
        _ = controller.PumpRenderFrame(0.016d, probe);
        await Assert.That(controller.Bridge.BindingCapacity).IsGreaterThanOrEqualTo(2);
        await Assert.That(controller.IsBridgeDisabled).IsFalse();

        // A capability may only claim to be drawn once a backend has reported resolving a batch.
        // Without a solver nothing is ever published, so the matrix still has to admit that.
        if (!controller.Bridge.HasAppliedBatch)
        {
            foreach (ViewerPhysicsCapabilityRow row in controller.Capabilities)
            {
                await Assert.That(row.IsRenderable).IsFalse();
            }
        }

        // Restoring the authored state has to reach the backend through the real bridge.
        int clears = probe.Cleared;
        controller.RequestOverrideClear();
        _ = controller.PumpRenderFrame(0.032d, probe);
        await Assert.That(probe.Cleared).IsGreaterThan(clears);
        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ARealPreviewNormalizesTheSessionOverlayAndClearsIt()
    {
        await using UsdStageScheduler scheduler = OpenSchedulerOrSkip("viewer-physics-smoke.usda");
        await using ViewerPhysicsController controller = NewController(scheduler);

        await controller.EnableAsync();
        if (!UsdPhysicsPreviewApplier.IsSupported)
        {
            Skip.Test("The staged native runtime does not provide batched physics authoring.");
        }

        // Clearing normalizes the session overlay through the scheduler exactly as enabling does,
        // so it exercises the overlay capture without needing a solver to publish a pose first. A
        // stage-bound result returned from a scheduled call is refused, which used to make every
        // preview fail; this asserts the applier is reached and the clear runs.
        string message = await controller.SetPreviewAsync(false);
        await Assert.That(message).IsNotEmpty();
        await Assert.That(controller.Snapshot.PreviewEnabled).IsFalse();
        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);

        SkipWhenSolverIsNotStaged(controller);
        await controller.StepOneFrameAsync();
        string applied = await controller.SetPreviewAsync(true);
        await Assert.That(applied).IsNotEmpty();
        await Assert.That(controller.Snapshot.PreviewEnabled).IsTrue();
        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);

        string cleared = await controller.SetPreviewAsync(false);
        await Assert.That(cleared).IsNotEmpty();
        await Assert.That(controller.Snapshot.PreviewEnabled).IsFalse();
        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ARealTransportSimulatesAndAppliesOverridesToTheBackend()
    {
        await using UsdStageScheduler scheduler = OpenSchedulerOrSkip("viewer-physics-smoke.usda");
        await using ViewerPhysicsController controller = NewController(scheduler);

        await controller.EnableAsync();
        SkipWhenSolverIsNotStaged(controller);

        await controller.StepOneFrameAsync();
        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);
        await Assert.That(controller.Snapshot.Status.StepIndex).IsGreaterThan(0UL);

        // The world has to have been given the extracted stage: a transport that built without one
        // still steps and still publishes, it just publishes an empty frame, and every symptom of
        // that is downstream. The composition summary is the direct evidence.
        foreach (ViewerPhysicsDiagnosticRow row in controller.Diagnostics)
        {
            await Assert.That(row.Code).IsNotEqualTo("OPENUSD_PHYSICS_EXTRACTION_UNAVAILABLE");
        }

        var target = new RecordingOverrideTarget();
        int applied = 0;
        int ingested = 0;
        for (int frame = 0; frame < 8 && applied == 0; frame++)
        {
            ViewerPhysicsFramePumpResult result = controller.PumpRenderFrame(0.016d, target);
            ingested += result.Ingested ? 1 : 0;
            applied += result.Applied;
        }

        await Assert.That(ingested).IsGreaterThan(0);

        // A published frame that carries no pose ingests happily and applies nothing, so the
        // override count is asserted directly rather than only through the accepted total.
        await Assert.That(controller.Bridge.Overrides.Count).IsGreaterThan(0);
        await Assert.That(applied).IsGreaterThan(0);
        await Assert.That(target.LastResolved).IsGreaterThan(0);
        await Assert.That(controller.IsBridgeDisabled).IsFalse();
        await Assert.That(controller.Bindings.Bound).IsGreaterThan(0);
        await Assert.That(controller.Bindings.Unresolved).IsEqualTo(0);

        // The backend has now reported resolving a whole batch, so every simulated capability that
        // draws through a render domain has to be reported as drawn - and one that draws nothing of
        // its own still must not be.
        await Assert.That(controller.Bridge.HasAppliedBatch).IsTrue();
        var renderable = 0;
        foreach (ViewerPhysicsCapabilityRow row in controller.Capabilities)
        {
            if (row.IsRenderable)
            {
                renderable++;
                await Assert.That(row.IsSupported).IsTrue();
            }
        }

        await Assert.That(renderable).IsGreaterThan(0);

        // Stopping has to hand the backend a clear so the viewport returns to authored poses rather
        // than freezing on the last simulated one.
        await controller.StopAsync();
        int clears = target.Cleared;
        _ = controller.PumpRenderFrame(0.032d, target);
        await Assert.That(target.Cleared).IsGreaterThan(clears);
        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);
    }

    private static ViewerPhysicsController NewController(UsdStageScheduler scheduler) =>
        new(
            new ViewerPhysicsTransportFactory(scheduler),
            ViewerPhysicsStopwatchClock.Instance,
            ViewerPhysicsRenderCapacities.Default,
            8,
            0.25d,
            new ViewerPhysicsSchedulerAuthoringStage(scheduler));

    [Test]
    public async Task ARealExtractionProducesInspectorSectionsForTheAuthoredObjects()
    {
        await using UsdStageScheduler scheduler = OpenSchedulerOrSkip("viewer-physics-smoke.usda");
        await using ViewerPhysicsController controller = NewController(scheduler);

        await controller.EnableAsync();
        IReadOnlyList<ViewerPhysicsObjectSection> sections =
            await controller.LoadInspectorAsync();

        // The stage authors a scene, a static collider, and two rigid bodies, so a projection that
        // produced nothing would mean the inspector is reading something other than the real page.
        await Assert.That(sections.Count).IsGreaterThanOrEqualTo(3);
        await Assert.That(controller.InspectorRevision).IsNotEqualTo(0UL);

        var paths = new List<string>();
        var rows = 0;
        foreach (ViewerPhysicsObjectSection section in sections)
        {
            paths.Add(section.PrimPath);
            rows += section.Rows.Count;
            await Assert.That(section.Header).Contains(section.PrimPath);
        }

        await Assert.That(paths).Contains("/FallingBox");
        await Assert.That(rows).IsGreaterThan(0);

        // Every row the real extractor produced has to be classified, and a mass - which is float
        // and which the scalar ABI cannot carry - has to be reported as read only rather than as
        // an editable field that would silently fail.
        ViewerPhysicsPropertyRow? mass = ViewerPhysicsInspectorProjector.FindRow(
            sections, "/FallingBox", "physics:mass");
        if (mass is not null)
        {
            await Assert.That(mass.IsEditable).IsFalse();
            await Assert.That(mass.Authorability)
                .IsEqualTo(ViewerPhysicsAuthorability.UnsupportedType);
        }
    }

    [Test]
    public async Task ARealPropertyEditIsAuthoredIntoTheOverlayAndUndoneExactly()
    {
        await using UsdStageScheduler scheduler = OpenSchedulerOrSkip("viewer-physics-smoke.usda");
        await using ViewerPhysicsController controller = NewController(scheduler);

        await controller.EnableAsync();
        await Assert.That(controller.CanAuthor).IsTrue();

        const string propertyName = "openUsdPhysics:body:sleepThreshold";
        ViewerPhysicsValue before = await controller.ReadPropertyAsync(
            "/FallingBox",
            propertyName,
            ViewerPhysicsValueKind.Number);
        await Assert.That(before.IsAuthored).IsFalse();

        var step = new ViewerPhysicsEditStep(
            "Sleep Threshold on /FallingBox",
            [
                new ViewerPhysicsEdit(
                    "/FallingBox",
                    propertyName,
                    "Sleep Threshold",
                    before,
                    ViewerPhysicsValue.FromNumber(0.125d)),
            ]);

        ViewerPhysicsAuthoringResult applied = await controller.ApplyEditAsync(step);

        await Assert.That(applied.Applied).IsEqualTo(1);
        await Assert.That(applied.Rejected).IsEqualTo(0);
        await Assert.That(applied.Edits.Count).IsEqualTo(1);
        await Assert.That(applied.Edits[0].IsKnown).IsTrue();

        ViewerPhysicsValue authored = await controller.ReadPropertyAsync(
            "/FallingBox",
            propertyName,
            ViewerPhysicsValueKind.Number);
        await Assert.That(authored.IsAuthored).IsTrue();
        await Assert.That(authored.NumberValue).IsEqualTo(0.125d);

        // The edit must land in the session overlay's user layer, never in the file the stage was
        // opened from, so the root layer still has to be unchanged afterwards.
        string editTarget = await scheduler.InvokeAsync(
            stage => stage.EditTargetLayerIdentifier);
        string rootLayer = await scheduler.InvokeAsync(stage => stage.RootLayerIdentifier);
        await Assert.That(editTarget).IsEqualTo(rootLayer);

        await Assert.That(controller.History.CanUndo).IsTrue();
        ViewerPhysicsAuthoringResult undone = await controller.UndoAsync();
        await Assert.That(undone.Applied).IsEqualTo(1);

        ViewerPhysicsValue restored = await controller.ReadPropertyAsync(
            "/FallingBox",
            propertyName,
            ViewerPhysicsValueKind.Number);
        await Assert.That(restored.IsAuthored).IsFalse();
        await Assert.That(controller.History.CanRedo).IsTrue();

        ViewerPhysicsAuthoringResult redone = await controller.RedoAsync();
        await Assert.That(redone.Applied).IsEqualTo(1);
        ViewerPhysicsValue again = await controller.ReadPropertyAsync(
            "/FallingBox",
            propertyName,
            ViewerPhysicsValueKind.Number);
        await Assert.That(again.NumberValue).IsEqualTo(0.125d);
    }

    [Test]
    public async Task ARealWorldStagesABatchedRuntimeCommandAndAppliesItOnTheNextStep()
    {
        await using UsdStageScheduler scheduler = OpenSchedulerOrSkip("viewer-physics-smoke.usda");
        await using ViewerPhysicsController controller = NewController(scheduler);

        await controller.EnableAsync();
        SkipWhenSolverIsNotStaged(controller);

        ulong target = controller.ResolveIdentity("/FallingBox");
        await Assert.That(target).IsNotEqualTo(0UL);

        // Two commands in one batch: the world has to accept both without a per-command interop
        // transition, which is the whole point of submitting a batch.
        ViewerPhysicsCommandOutcome outcome = await controller.SubmitCommandsAsync(
        [
            new ViewerPhysicsRuntimeCommand(
                ViewerPhysicsRuntimeCommandKind.Wake, target, ViewerPhysicsVector3.Zero),
            new ViewerPhysicsRuntimeCommand(
                ViewerPhysicsRuntimeCommandKind.Impulse,
                target,
                new ViewerPhysicsVector3(0d, 1d, 0d),
                25d),
        ]);

        await Assert.That(outcome.Rejected).IsEqualTo(0);
        await Assert.That(outcome.Accepted).IsEqualTo(2);
        await Assert.That(controller.StagedCommands).IsEqualTo(2L);

        await controller.StepOneFrameAsync();
        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);
        await Assert.That(controller.Snapshot.Status.StepIndex).IsGreaterThan(0UL);
    }

    [Test]
    public async Task ARealWorldRefusesAVehicleInputOutsideTheRangeTheAbiDocuments()
    {
        await using UsdStageScheduler scheduler = OpenSchedulerOrSkip("viewer-physics-smoke.usda");
        await using ViewerPhysicsController controller = NewController(scheduler);

        await controller.EnableAsync();
        SkipWhenSolverIsNotStaged(controller);
        ulong target = controller.ResolveIdentity("/FallingBox");
        await Assert.That(target).IsNotEqualTo(0UL);

        // The viewer's own vehicle model clamps, so this bypasses it deliberately: the point is
        // that an out-of-range input reaches a refusal rather than a silently clamped drivetrain.
        var command = new ViewerPhysicsRuntimeCommand(
            ViewerPhysicsRuntimeCommandKind.VehicleInput,
            target,
            new ViewerPhysicsVector3(2d, 0d, 0d))
        {
            Point = new ViewerPhysicsVector3(0d, 0d, 0d),
        };

        ViewerPhysicsCommandOutcome outcome = await controller.SubmitCommandsAsync([command]);

        await Assert.That(outcome.Accepted).IsEqualTo(0);
        await Assert.That(outcome.Rejected).IsEqualTo(1);
        await Assert.That(outcome.Message).IsNotEmpty();
        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);
    }


    [Test]
    public async Task ARealStageResolvesEachSectionOfOnePrimToItsOwnComposedIdentity()
    {
        await using UsdStageScheduler scheduler =
            OpenSchedulerOrSkip("viewer-physics-authoring.usda");
        await using ViewerPhysicsController controller = NewController(scheduler);

        await controller.EnableAsync();
        SkipWhenSolverIsNotStaged(controller);

        IReadOnlyList<ViewerPhysicsObjectSection> sections =
            await controller.LoadInspectorAsync();

        // One prim, several sections. /Box authors the body and the collider together, and /Car
        // authors the body, the collider, and the vehicle together, so the path alone cannot name
        // the object an interaction is meant to drive.
        ViewerPhysicsObjectSection boxBody = Require(sections, "/Box", "RigidBody");
        ViewerPhysicsObjectSection boxCollider = Require(sections, "/Box", "Collider");
        ViewerPhysicsObjectSection carBody = Require(sections, "/Car", "RigidBody");
        ViewerPhysicsObjectSection carVehicle = Require(sections, "/Car", "Vehicle");
        ViewerPhysicsObjectSection walker = Require(sections, "/Walker", "CharacterController");

        // The extractor's identity distinguishes the sections, which is what selection anchoring
        // needs, and it is never the identity a command carries.
        await Assert.That(boxBody.ObjectId).IsNotEqualTo(boxCollider.ObjectId);
        await Assert.That(carBody.ObjectId).IsNotEqualTo(carVehicle.ObjectId);
        await Assert.That(boxBody.ObjectId).IsNotEqualTo(boxBody.TargetId);
        await Assert.That(carVehicle.ObjectId).IsNotEqualTo(carVehicle.TargetId);

        // A collider is not addressable on its own, so it resolves to the body that owns it.
        ulong boxActor = UsdPhysicsIdentities
            .ForSimulatedObject("/Box", UsdPhysicsObjectKind.RigidBody).Value;
        await Assert.That(boxBody.TargetId).IsEqualTo(boxActor);
        await Assert.That(boxCollider.TargetId).IsEqualTo(boxActor);
        await Assert.That(boxBody.Accepts(ViewerPhysicsCommandability.Body)).IsTrue();
        await Assert.That(boxCollider.Accepts(ViewerPhysicsCommandability.Body)).IsTrue();

        // The vehicle and the chassis actor share a prim and never share an identity.
        await Assert.That(carVehicle.TargetId).IsEqualTo(
            UsdPhysicsIdentities.ForSimulatedObject("/Car", UsdPhysicsObjectKind.Vehicle).Value);
        await Assert.That(carBody.TargetId).IsEqualTo(
            UsdPhysicsIdentities.ForSimulatedObject("/Car", UsdPhysicsObjectKind.RigidBody).Value);
        await Assert.That(carVehicle.TargetId).IsNotEqualTo(carBody.TargetId);
        await Assert.That(carVehicle.Accepts(ViewerPhysicsCommandability.Vehicle)).IsTrue();
        await Assert.That(carVehicle.Accepts(ViewerPhysicsCommandability.Body)).IsFalse();
        await Assert.That(carBody.Accepts(ViewerPhysicsCommandability.Vehicle)).IsFalse();

        await Assert.That(walker.TargetId).IsEqualTo(
            UsdPhysicsIdentities
                .ForSimulatedObject("/Walker", UsdPhysicsObjectKind.Controller).Value);
        await Assert.That(walker.Accepts(ViewerPhysicsCommandability.Controller)).IsTrue();
        await Assert.That(walker.Accepts(ViewerPhysicsCommandability.Body)).IsFalse();

        // A section the world cannot address must offer nothing rather than a target that is only
        // ever refused.
        ViewerPhysicsObjectSection scene = Require(sections, "/Scene", "Scene");
        await Assert.That(scene.Accepts(ViewerPhysicsCommandability.Body)).IsFalse();
        await Assert.That(scene.Accepts(ViewerPhysicsCommandability.Scene)).IsTrue();

        // Every force, impulse, torque, and velocity the world carries is refused unless the actor
        // is dynamic, so a kinematic body and a collider with no body offer no body interaction.
        ViewerPhysicsObjectSection kinematic = Require(sections, "/KinematicBox", "RigidBody");
        await Assert.That(kinematic.TargetId).IsEqualTo(
            UsdPhysicsIdentities
                .ForSimulatedObject("/KinematicBox", UsdPhysicsObjectKind.RigidBody).Value);
        await Assert.That(kinematic.Accepts(ViewerPhysicsCommandability.Body)).IsFalse();

        ViewerPhysicsObjectSection ground = Require(sections, "/Ground", "Collider");
        await Assert.That(ground.Accepts(ViewerPhysicsCommandability.Body)).IsFalse();
    }

    [Test]
    public async Task ARealWorldAcceptsEveryCommandTheSectionsSayTheyAccept()
    {
        await using UsdStageScheduler scheduler =
            OpenSchedulerOrSkip("viewer-physics-authoring.usda");
        await using ViewerPhysicsController controller = NewController(scheduler);

        await controller.EnableAsync();
        SkipWhenSolverIsNotStaged(controller);

        IReadOnlyList<ViewerPhysicsObjectSection> sections =
            await controller.LoadInspectorAsync();
        ViewerPhysicsObjectSection boxCollider = Require(sections, "/Box", "Collider");
        ViewerPhysicsObjectSection carVehicle = Require(sections, "/Car", "Vehicle");
        ViewerPhysicsObjectSection walker = Require(sections, "/Walker", "CharacterController");

        // Every one of these goes to a different map inside the world. If any address were wrong
        // the world would step anyway and report a missing target, which is what the diagnostics
        // assertion below catches. Building them through the same resolver the UI uses is what
        // makes this a test of the shipped path rather than of a hand written identity.
        ulong bodyTarget = ViewerPhysicsController.ResolveCommandTarget(
            boxCollider, ViewerPhysicsCommandability.Body);
        ulong vehicleTarget = ViewerPhysicsController.ResolveCommandTarget(
            carVehicle, ViewerPhysicsCommandability.Vehicle);
        ulong controllerTarget = ViewerPhysicsController.ResolveCommandTarget(
            walker, ViewerPhysicsCommandability.Controller);
        await Assert.That(bodyTarget).IsNotEqualTo(0UL);
        await Assert.That(vehicleTarget).IsNotEqualTo(0UL);
        await Assert.That(controllerTarget).IsNotEqualTo(0UL);

        ViewerPhysicsCommandOutcome outcome = await controller.SubmitCommandsAsync(
        [
            new ViewerPhysicsRuntimeCommand(
                ViewerPhysicsRuntimeCommandKind.Impulse,
                bodyTarget,
                new ViewerPhysicsVector3(1d, 0d, 0d),
                20d),
            new ViewerPhysicsRuntimeCommand(
                ViewerPhysicsRuntimeCommandKind.ControllerMove,
                controllerTarget,
                new ViewerPhysicsVector3(0.02d, 0d, 0d)),
            new ViewerPhysicsRuntimeCommand(
                ViewerPhysicsRuntimeCommandKind.VehicleInput,
                vehicleTarget,
                new ViewerPhysicsVector3(1d, 0d, 0d))
            {
                Point = ViewerPhysicsVector3.Zero,
            },
        ]);

        await Assert.That(outcome.Rejected).IsEqualTo(0);
        await Assert.That(outcome.Accepted).IsEqualTo(3);

        await controller.StepOneFrameAsync();
        await controller.StepOneFrameAsync();
        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);

        // The world reports an address it does not hold, so a target in the wrong identity space
        // shows up here rather than as a simulation that quietly ignores its input.
        foreach (ViewerPhysicsDiagnosticRow row in controller.Diagnostics)
        {
            await Assert.That(row.Code).DoesNotContain("OPENUSD_PHYSICS_COMMAND_TARGET_MISSING");
            await Assert.That(row.Code).DoesNotContain("OPENUSD_PHYSICS_COMMAND_REJECTED");
        }
    }

    [Test]
    public async Task ArticulationLinksAndTheirCollidersTargetTheLinkAndTheRootTargetsNothing()
    {
        await using UsdStageScheduler scheduler =
            OpenSchedulerOrSkip("viewer-physics-authoring.usda");
        await using ViewerPhysicsController controller = NewController(scheduler);

        await controller.EnableAsync();
        SkipWhenSolverIsNotStaged(controller);

        IReadOnlyList<ViewerPhysicsObjectSection> sections =
            await controller.LoadInspectorAsync();

        // A link is composed into its articulation rather than into the actor table, but it keeps
        // its own prim address, so a force aimed at it has to carry that address.
        ViewerPhysicsObjectSection link = Require(sections, "/Arm/Link", "RigidBody");
        ulong linkActor = UsdPhysicsIdentities
            .ForSimulatedObject("/Arm/Link", UsdPhysicsObjectKind.RigidBody).Value;
        await Assert.That(link.TargetId).IsEqualTo(linkActor);
        await Assert.That(link.Accepts(ViewerPhysicsCommandability.Body)).IsTrue();

        // The collider authored on the same prim resolves to the link that owns it, so a drag
        // started on the collider section pushes the link rather than nothing at all.
        ViewerPhysicsObjectSection collider = Require(sections, "/Arm/Link", "Collider");
        await Assert.That(collider.TargetId).IsEqualTo(linkActor);
        await Assert.That(collider.Accepts(ViewerPhysicsCommandability.Body)).IsTrue();
        await Assert.That(collider.ObjectId).IsNotEqualTo(link.ObjectId);

        // The articulation root is neither an actor nor a link. Its composed identity lives in the
        // world's articulation table, which no command reaches, so it must offer nothing.
        ViewerPhysicsObjectSection root = Require(sections, "/Arm", "ArticulationRoot");
        await Assert.That(root.Accepts(ViewerPhysicsCommandability.Body)).IsFalse();
        await Assert.That(root.Commandability).IsEqualTo(ViewerPhysicsCommandability.None);
    }

    [Test]
    public async Task ARealWorldAcceptsBodyCommandsAimedAtAnArticulationLink()
    {
        await using UsdStageScheduler scheduler =
            OpenSchedulerOrSkip("viewer-physics-authoring.usda");
        await using ViewerPhysicsController controller = NewController(scheduler);

        await controller.EnableAsync();
        SkipWhenSolverIsNotStaged(controller);

        IReadOnlyList<ViewerPhysicsObjectSection> sections =
            await controller.LoadInspectorAsync();
        ViewerPhysicsObjectSection collider = Require(sections, "/Arm/Link", "Collider");

        // Built through the same resolver the drag gesture uses, so this exercises the shipped
        // path rather than a hand written identity.
        ulong target = ViewerPhysicsController.ResolveCommandTarget(
            collider, ViewerPhysicsCommandability.Body);
        await Assert.That(target).IsNotEqualTo(0UL);

        ViewerPhysicsCommandOutcome outcome = await controller.SubmitCommandsAsync(
        [
            new ViewerPhysicsRuntimeCommand(
                ViewerPhysicsRuntimeCommandKind.Force,
                target,
                new ViewerPhysicsVector3(1d, 0d, 0d),
                20d),
            new ViewerPhysicsRuntimeCommand(
                ViewerPhysicsRuntimeCommandKind.ClearForce,
                target,
                ViewerPhysicsVector3.Zero),
        ]);
        await Assert.That(outcome.Rejected).IsEqualTo(0);
        await Assert.That(outcome.Accepted).IsEqualTo(2);

        await controller.StepOneFrameAsync();
        await controller.StepOneFrameAsync();
        await Assert.That(controller.Snapshot.Error).IsEqualTo(string.Empty);

        // The world reports an address it does not hold, and a command a link cannot express. A
        // link that is not in the world's link map, or a force family it refuses, surfaces here
        // rather than as an interaction that quietly does nothing.
        foreach (ViewerPhysicsDiagnosticRow row in controller.Diagnostics)
        {
            await Assert.That(row.Code).DoesNotContain("OPENUSD_PHYSICS_COMMAND_TARGET_MISSING");
            await Assert.That(row.Code).DoesNotContain("OPENUSD_PHYSICS_COMMAND_REJECTED");
        }
    }

    [Test]
    public async Task AnArticulationLinkOffersForceButNotImpulse()
    {
        await using UsdStageScheduler scheduler =
            OpenSchedulerOrSkip("viewer-physics-authoring.usda");
        await using ViewerPhysicsController controller = NewController(scheduler);

        await controller.EnableAsync();
        SkipWhenSolverIsNotStaged(controller);

        IReadOnlyList<ViewerPhysicsObjectSection> sections =
            await controller.LoadInspectorAsync();

        // PhysX refuses the impulse and velocity change force modes on a reduced coordinate link,
        // so the impulse control has to be off for one while force, torque, wake, sleep and drag
        // stay on. Offering an impulse the world would only ever refuse is the lie this prevents.
        ViewerPhysicsObjectSection link = Require(sections, "/Arm/Link", "RigidBody");
        await Assert.That(link.Accepts(ViewerPhysicsCommandability.Body)).IsTrue();
        await Assert.That(link.Accepts(ViewerPhysicsCommandability.Impulse)).IsFalse();

        // The collider on the link inherits the link's answer, so a drag still works and an impulse
        // still does not.
        ViewerPhysicsObjectSection collider = Require(sections, "/Arm/Link", "Collider");
        await Assert.That(collider.Accepts(ViewerPhysicsCommandability.Body)).IsTrue();
        await Assert.That(collider.Accepts(ViewerPhysicsCommandability.Impulse)).IsFalse();

        // A free rigid actor is unaffected and still takes both.
        ViewerPhysicsObjectSection free = Require(sections, "/Box", "RigidBody");
        await Assert.That(free.Accepts(ViewerPhysicsCommandability.Body)).IsTrue();
        await Assert.That(free.Accepts(ViewerPhysicsCommandability.Impulse)).IsTrue();

        // The resolver the buttons run through agrees.
        await Assert.That(ViewerPhysicsController.ResolveCommandTarget(
                link, ViewerPhysicsCommandability.Impulse))
            .IsEqualTo(0UL);
        await Assert.That(ViewerPhysicsController.ResolveCommandTarget(
                link, ViewerPhysicsCommandability.Body))
            .IsNotEqualTo(0UL);
        await Assert.That(ViewerPhysicsController.ResolveCommandTarget(
                free, ViewerPhysicsCommandability.Impulse))
            .IsNotEqualTo(0UL);
    }

    private static ViewerPhysicsObjectSection Require(
        IReadOnlyList<ViewerPhysicsObjectSection> sections,
        string primPath,
        string kind)
    {
        for (int index = 0; index < sections.Count; index++)
        {
            if (string.Equals(sections[index].PrimPath, primPath, StringComparison.Ordinal) &&
                string.Equals(sections[index].Kind, kind, StringComparison.Ordinal))
            {
                return sections[index];
            }
        }

        throw new InvalidOperationException(
            $"The extraction produced no {kind} section for '{primPath}'. It produced: " +
            string.Join(", ", sections.Select(section => $"{section.PrimPath}:{section.Kind}")));
    }

    private static void SkipWhenSolverIsNotStaged(ViewerPhysicsController controller)
    {
        // The solver shim ships separately from the OpenUSD runtime. When it is staged this test
        // asserts a real simulation; when the build never produced it there is nothing to step and
        // the extraction half of the smoke is the part that still has to hold.
        foreach (ViewerPhysicsDiagnosticRow row in controller.Diagnostics)
        {
            if (row.Code == "OPENUSD_PHYSICS_BACKEND_UNAVAILABLE")
            {
                Skip.Test($"The native physics solver is not staged: {row.Message}");
            }
        }
    }


    private static UsdStageScheduler OpenSchedulerOrSkip(string fileName)
    {
        string path = Path.Combine(FindRepositoryRoot(), "test-assets", fileName);
        try
        {
            // The scheduler opens the stage on its own thread, so probing here is what turns a
            // managed-only checkout into a skip instead of a background DllNotFoundException.
            using UsdStage probe = UsdStage.Open(path);
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
            throw;
        }

        return UsdStageScheduler.Open(path);
    }

    private static string FindRepositoryRoot()
    {
        string currentDirectory = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(currentDirectory, "OpenUsd.slnx")))
        {
            return currentDirectory;
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the OpenUSD repository root.");
    }

    /// <summary>A backend that resolves each override the way Storm and Silk do.</summary>
    private sealed class RecordingOverrideTarget : IViewerPhysicsOverrideTarget
    {
        private ViewerPhysicsOverrideReport _report;
        private bool _hasReport;

        public bool SupportsPhysicsTransformOverrides => true;

        public int Cleared { get; private set; }

        public int LastResolved { get; private set; }

        public int LastDeformationRegions { get; private set; }

        public int ApplyPhysicsOverrides(
            in PhysicsRenderOverrideView overrides,
            PhysicsRenderBindingTable bindings)
        {
            ArgumentNullException.ThrowIfNull(bindings);
            int resolved = 0;
            ReadOnlySpan<PhysicsRenderTransformOverride> items = overrides.Items;
            for (int index = 0; index < items.Length; index++)
            {
                if (bindings.TryResolve(items[index].Id, out _))
                {
                    resolved++;
                }
            }

            LastResolved = resolved;
            _report = new ViewerPhysicsOverrideReport(
                overrides.Revision,
                resolved,
                Math.Max(0, overrides.Count - resolved));
            _hasReport = true;
            return resolved;
        }

        /// <summary>Counts the deformable regions a rigid-only backend would decline to upload.</summary>
        public int ApplyPhysicsDeformations(
            in PhysicsRenderDeformationView deformations,
            PhysicsRenderBindingTable bindings)
        {
            ArgumentNullException.ThrowIfNull(bindings);
            LastDeformationRegions = deformations.Regions.Length;
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

        public void ClearPhysicsOverrides()
        {
            _hasReport = false;
            Cleared++;
        }
    }
}
