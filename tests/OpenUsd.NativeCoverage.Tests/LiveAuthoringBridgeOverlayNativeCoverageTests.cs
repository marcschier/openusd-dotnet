// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.LiveAuthoring;

namespace OpenUsd.NativeCoverage.Tests;

/// <summary>
/// Exercises bridge-overlay recovery against a real native stage: a full snapshot must replace only
/// the bridge-owned subtree, and every user or physics opinion outside it must survive untouched.
/// </summary>
public sealed class LiveAuthoringBridgeOverlayNativeCoverageTests
{
    private const string BridgeRoot = "/Bridge";
    private const string RemoteOrigin = "kit-bridge";
    private const string SessionId = "native-session";

    [Test]
    public async Task FullSnapshotReplacesOnlyTheBridgeOwnedOverlay()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(FullSnapshotReplacesOnlyTheBridgeOwnedOverlay));
        string stagePath = Path.Combine(directory, "bridge-overlay.usda");
        await using UsdStageScheduler scheduler = UsdStageScheduler.Create(stagePath);
        var executor = new UsdStageBatchExecutor(scheduler, LiveAuthoringEditLayer.Session);
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 4);
        await using var coordinator = new LiveAuthoringSessionCoordinator(
            sink,
            new LiveAuthoringSessionOptions
            {
                BridgeRootPath = BridgeRoot,
                LocalOriginId = "openusd-native-test"
            });

        // A user edit outside the bridge root, authored into the same live session layer. It must
        // survive every later bridge overlay replacement.
        await scheduler.EditAsync(
            static stage =>
            {
                stage.SetEditTargetToSessionLayer();
                stage.DefinePrim("/UserWorld", "Xform");
                stage.GetPrim("/UserWorld").SetDouble("custom:userValue", 42);
            },
            UsdStageInvalidationKind.Composition);

        await coordinator.ConnectAsync(new LiveAuthoringRemoteEpoch(RemoteOrigin, SessionId, 1));
        LiveAuthoringSessionResult first = await coordinator.ApplySnapshotAsync(
            new LiveAuthoringSnapshot(
                new LiveAuthoringRemoteEpoch(RemoteOrigin, SessionId, 1),
                4,
                BridgeRoot,
                [
                    new DefinePrimUpdate($"{BridgeRoot}/Cube", "Xform"),
                    new DefinePrimUpdate($"{BridgeRoot}/Retired", "Xform"),
                    new SetAttributeUpdate(
                        $"{BridgeRoot}/Cube",
                        "custom:pressure",
                        LiveAttributeValue.FromDouble(1.5))
                ]));

        await Assert.That(first.IsApplied).IsTrue().Because(first.Detail ?? "no detail");
        await AssertStageAsync(
            scheduler,
            static stage =>
            {
                bool cube = stage.HasPrim($"{BridgeRoot}/Cube");
                bool retired = stage.HasPrim($"{BridgeRoot}/Retired");
                double pressure = stage.GetPrim($"{BridgeRoot}/Cube").GetDouble("custom:pressure");
                return cube && retired && pressure == 1.5;
            },
            "the first snapshot authored both bridge prims and the pressure value");

        LiveAuthoringSessionResult second = await coordinator.ApplySnapshotAsync(
            new LiveAuthoringSnapshot(
                new LiveAuthoringRemoteEpoch(RemoteOrigin, SessionId, 1),
                9,
                BridgeRoot,
                [
                    new DefinePrimUpdate($"{BridgeRoot}/Cube", "Xform"),
                    new SetAttributeUpdate(
                        $"{BridgeRoot}/Cube",
                        "custom:pressure",
                        LiveAttributeValue.FromDouble(7.25))
                ]));

        await Assert.That(second.IsApplied).IsTrue().Because(second.Detail ?? "no detail");
        await AssertStageAsync(
            scheduler,
            static stage => stage.HasPrim($"{BridgeRoot}/Cube") &&
                !stage.HasPrim($"{BridgeRoot}/Retired") &&
                stage.GetPrim($"{BridgeRoot}/Cube").GetDouble("custom:pressure") == 7.25,
            "the newer snapshot replaced the overlay and dropped the retired prim");
        await AssertStageAsync(
            scheduler,
            static stage => stage.HasPrim("/UserWorld") &&
                stage.GetPrim("/UserWorld").GetDouble("custom:userValue") == 42,
            "a bridge overlay replacement must never touch a user opinion outside the bridge root");
        await Assert.That(coordinator.State).IsEqualTo(LiveAuthoringSessionState.Synchronized);
    }

    [Test]
    public async Task DeltasApplyToTheLiveStageAndAFailureForcesSnapshotRecovery()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(DeltasApplyToTheLiveStageAndAFailureForcesSnapshotRecovery));
        string stagePath = Path.Combine(directory, "bridge-delta.usda");
        await using UsdStageScheduler scheduler = UsdStageScheduler.Create(stagePath);
        var executor = new UsdStageBatchExecutor(scheduler, LiveAuthoringEditLayer.Session);
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 4);
        await using var coordinator = new LiveAuthoringSessionCoordinator(
            sink,
            new LiveAuthoringSessionOptions
            {
                BridgeRootPath = BridgeRoot,
                LocalOriginId = "openusd-native-test"
            });
        var epoch = new LiveAuthoringRemoteEpoch(RemoteOrigin, SessionId, 1);

        await coordinator.ConnectAsync(epoch);
        await coordinator.ApplySnapshotAsync(new LiveAuthoringSnapshot(
            epoch,
            0,
            BridgeRoot,
            [new DefinePrimUpdate($"{BridgeRoot}/Cube", "Xform")]));
        LiveAuthoringSessionResult applied = await coordinator.ApplyDeltaAsync(new LiveAuthoringDelta(
            epoch,
            1,
            [
                new SetAttributeUpdate(
                    $"{BridgeRoot}/Cube",
                    "custom:pressure",
                    LiveAttributeValue.FromDouble(3.5))
            ],
            originId: RemoteOrigin));

        await Assert.That(applied.IsApplied).IsTrue().Because(applied.Detail ?? "no detail");
        await AssertStageAsync(
            scheduler,
            static stage => stage.GetPrim($"{BridgeRoot}/Cube").GetDouble("custom:pressure") == 3.5,
            "an in-order delta reaches the live stage");

        // Removing an API schema has no typed OpenUSD surface yet, so the executor rejects it while
        // applying. That is exactly the incremental failure recovery must convert into ResyncRequired.
        LiveAuthoringSessionResult failed = await coordinator.ApplyDeltaAsync(new LiveAuthoringDelta(
            epoch,
            2,
            [
                new ApiSchemaUpdate(
                    $"{BridgeRoot}/Cube",
                    "SkelBindingAPI",
                    LiveApiSchemaOperation.Remove)
            ],
            originId: RemoteOrigin));
        LiveAuthoringSessionResult blocked = await coordinator.ApplyDeltaAsync(new LiveAuthoringDelta(
            epoch,
            3,
            [
                new SetAttributeUpdate(
                    $"{BridgeRoot}/Cube",
                    "custom:pressure",
                    LiveAttributeValue.FromDouble(9))
            ],
            originId: RemoteOrigin));

        await Assert.That(failed.Rejection).IsEqualTo(LiveAuthoringSessionRejection.ApplyFailed);
        await Assert.That(blocked.Rejection).IsEqualTo(LiveAuthoringSessionRejection.ResyncRequired);
        await Assert.That(coordinator.State).IsEqualTo(LiveAuthoringSessionState.ResyncRequired);
        await AssertStageAsync(
            scheduler,
            static stage => stage.GetPrim($"{BridgeRoot}/Cube").GetDouble("custom:pressure") == 3.5,
            "a rejected delta must not reach the stage while the session awaits a full snapshot");

        LiveAuthoringSessionResult recovered = await coordinator.ApplySnapshotAsync(
            new LiveAuthoringSnapshot(
                epoch,
                12,
                BridgeRoot,
                [
                    new DefinePrimUpdate($"{BridgeRoot}/Cube", "Xform"),
                    new SetAttributeUpdate(
                        $"{BridgeRoot}/Cube",
                        "custom:pressure",
                        LiveAttributeValue.FromDouble(11.5))
                ]));

        await Assert.That(recovered.IsApplied).IsTrue().Because(recovered.Detail ?? "no detail");
        await Assert.That(coordinator.State).IsEqualTo(LiveAuthoringSessionState.Synchronized);
        await AssertStageAsync(
            scheduler,
            static stage => stage.GetPrim($"{BridgeRoot}/Cube").GetDouble("custom:pressure") == 11.5,
            "a newer full snapshot restores the session and the overlay content");
    }

    [Test]
    public async Task ConflictingReplayNeverReachesTheStageAndForcesSnapshotRecovery()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(ConflictingReplayNeverReachesTheStageAndForcesSnapshotRecovery));
        string stagePath = Path.Combine(directory, "bridge-replay.usda");
        await using UsdStageScheduler scheduler = UsdStageScheduler.Create(stagePath);
        var executor = new UsdStageBatchExecutor(scheduler, LiveAuthoringEditLayer.Session);
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 4);
        await using var coordinator = new LiveAuthoringSessionCoordinator(
            sink,
            new LiveAuthoringSessionOptions
            {
                BridgeRootPath = BridgeRoot,
                LocalOriginId = "openusd-native-test",
                ReplayWindowLength = 4
            });
        var epoch = new LiveAuthoringRemoteEpoch(RemoteOrigin, SessionId, 1);

        await coordinator.ConnectAsync(epoch);
        await coordinator.ApplySnapshotAsync(new LiveAuthoringSnapshot(
            epoch,
            0,
            BridgeRoot,
            [new DefinePrimUpdate($"{BridgeRoot}/Cube", "Xform")]));
        await coordinator.ApplyDeltaAsync(Pressure(epoch, 1, 3.5));
        LiveAuthoringSessionResult replayed = await coordinator.ApplyDeltaAsync(
            Pressure(epoch, 1, 3.5));
        LiveAuthoringSessionResult conflicting = await coordinator.ApplyDeltaAsync(
            Pressure(epoch, 1, 8.25));

        await Assert.That(replayed.Outcome).IsEqualTo(LiveAuthoringSessionOutcome.Duplicate);
        await Assert.That(conflicting.Rejection)
            .IsEqualTo(LiveAuthoringSessionRejection.DuplicateConflict);
        await Assert.That(coordinator.State).IsEqualTo(LiveAuthoringSessionState.ResyncRequired);
        await AssertStageAsync(
            scheduler,
            static stage => stage.GetPrim($"{BridgeRoot}/Cube").GetDouble("custom:pressure") == 3.5,
            "neither an identical replay nor a conflicting one may author the stage a second time");

        LiveAuthoringSessionResult recovered = await coordinator.ApplySnapshotAsync(
            new LiveAuthoringSnapshot(
                epoch,
                6,
                BridgeRoot,
                [
                    new DefinePrimUpdate($"{BridgeRoot}/Cube", "Xform"),
                    new SetAttributeUpdate(
                        $"{BridgeRoot}/Cube",
                        "custom:pressure",
                        LiveAttributeValue.FromDouble(8.25))
                ]));

        await Assert.That(recovered.IsApplied).IsTrue().Because(recovered.Detail ?? "no detail");
        await Assert.That(coordinator.GetStatus().ReplayLedgerCount).IsEqualTo(0);
        await AssertStageAsync(
            scheduler,
            static stage => stage.GetPrim($"{BridgeRoot}/Cube").GetDouble("custom:pressure") == 8.25,
            "a newer full snapshot is the only way out of a duplicate conflict");
    }

    private static LiveAuthoringDelta Pressure(
        LiveAuthoringRemoteEpoch epoch,
        long sequence,
        double value) =>
        new(
            epoch,
            sequence,
            [
                new SetAttributeUpdate(
                    $"{BridgeRoot}/Cube",
                    "custom:pressure",
                    LiveAttributeValue.FromDouble(value))
            ],
            correlationId: $"remote-{sequence}",
            originId: RemoteOrigin);

    [Test]
    public async Task ABridgeReplacementPreservesTheSimulationAndUserOverlayLayers()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(ABridgeReplacementPreservesTheSimulationAndUserOverlayLayers));
        string stagePath = Path.Combine(directory, "bridge-session-overlay.usda");
        await using UsdStageScheduler scheduler = UsdStageScheduler.Create(stagePath);
        var executor = new UsdStageBatchExecutor(scheduler, LiveAuthoringEditLayer.Session);
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 4);
        await using var coordinator = new LiveAuthoringSessionCoordinator(
            sink,
            new LiveAuthoringSessionOptions
            {
                BridgeRootPath = BridgeRoot,
                LocalOriginId = "openusd-native-test"
            });
        var epoch = new LiveAuthoringRemoteEpoch(RemoteOrigin, SessionId, 1);

        UsdSessionOverlay? overlay = null;
        string[] layerIdentifiers = await scheduler.EditAsync(
            stage =>
            {
                // The overlay stays alive for the whole test: disposing it removes the physics
                // overlay layer, which is the very thing a bridge replacement must not disturb.
                overlay = stage.NormalizeSessionOverlay();
                overlay.SetEditTargetToUserLayer();
                stage.DefinePrim("/UserWorld", "Xform");
                stage.GetPrim("/UserWorld").SetDouble("custom:userValue", 42);
                return new[] { overlay.PhysicsLayerIdentifier, overlay.UserLayerIdentifier };
            },
            UsdStageInvalidationKind.Composition);

        try
        {
            await coordinator.ConnectAsync(epoch);
            await coordinator.ApplySnapshotAsync(new LiveAuthoringSnapshot(
                epoch,
                1,
                BridgeRoot,
                [new DefinePrimUpdate($"{BridgeRoot}/Cube", "Xform")]));
            LiveAuthoringSessionResult replaced = await coordinator.ApplySnapshotAsync(
                new LiveAuthoringSnapshot(
                    epoch,
                    2,
                    BridgeRoot,
                    [new DefinePrimUpdate($"{BridgeRoot}/Other", "Xform")]));

            await Assert.That(replaced.IsApplied).IsTrue().Because(replaced.Detail ?? "no detail");
            await AssertStageAsync(
                scheduler,
                static stage => stage.HasPrim("/UserWorld") &&
                    stage.GetPrim("/UserWorld").GetDouble("custom:userValue") == 42 &&
                    stage.HasPrim($"{BridgeRoot}/Other") &&
                    !stage.HasPrim($"{BridgeRoot}/Cube"),
                "the user-edit layer content survives while the bridge overlay is replaced");

            string[] stackAfter = await scheduler.InvokeAsync(
                static stage => stage.GetLayerStackIdentifiers());
            await Assert.That(stackAfter).Contains(layerIdentifiers[0])
                .Because("the physics overlay layer must still compose after a bridge replacement");
            await Assert.That(stackAfter).Contains(layerIdentifiers[1])
                .Because("the user-edit overlay layer must still compose after a bridge replacement");
        }
        finally
        {
            await scheduler.InvokeAsync(_ => overlay?.Dispose());
        }
    }

    private static async Task AssertStageAsync(
        UsdStageScheduler scheduler,
        Func<UsdStage, bool> assertion,
        string because)
    {
        bool result = await scheduler.InvokeAsync(assertion);
        await Assert.That(result).IsTrue().Because(because);
    }
}
