// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.LiveAuthoring.Tests;

public sealed class LiveAuthoringSessionCoordinatorTests
{
    private const string BridgeRoot = "/Bridge";
    private const string RemoteOrigin = "kit-bridge";
    private const string SessionId = "session-a";

    [Test]
    public async Task DisconnectedSessionRejectsSnapshotsAndDeltas()
    {
        await using Harness harness = Harness.Create();

        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.Disconnected);

        LiveAuthoringSessionResult snapshot =
            await harness.Coordinator.ApplySnapshotAsync(Snapshot(0, Epoch(0)));
        LiveAuthoringSessionResult delta =
            await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(0)));

        await Assert.That(snapshot.Rejection).IsEqualTo(LiveAuthoringSessionRejection.SessionState);
        await Assert.That(delta.Rejection).IsEqualTo(LiveAuthoringSessionRejection.SessionState);
        await Assert.That(harness.Executor.AppliedBatchCount).IsEqualTo(0);
    }

    [Test]
    public async Task ConnectingSessionRejectsDeltasUntilAFullSnapshotArrives()
    {
        await using Harness harness = Harness.Create();
        await harness.Coordinator.ConnectAsync(Epoch(1));

        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.Connecting);

        LiveAuthoringSessionResult delta =
            await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1)));

        await Assert.That(delta.Outcome).IsEqualTo(LiveAuthoringSessionOutcome.Rejected);
        await Assert.That(delta.Rejection).IsEqualTo(LiveAuthoringSessionRejection.SessionState);
        await Assert.That(harness.Executor.AppliedBatchCount).IsEqualTo(0);
    }

    [Test]
    public async Task SnapshotReplacesTheBridgeOverlayInOneAtomicBatch()
    {
        await using Harness harness = Harness.Create();
        await harness.Coordinator.ConnectAsync(Epoch(1));

        LiveAuthoringSessionResult applied = await harness.Coordinator.ApplySnapshotAsync(
            Snapshot(
                7,
                Epoch(1),
                new DefinePrimUpdate($"{BridgeRoot}/Cube", "Xform"),
                new SetAttributeUpdate(
                    $"{BridgeRoot}/Cube",
                    "custom:pressure",
                    LiveAttributeValue.FromDouble(1.5))));

        await Assert.That(applied.IsApplied).IsTrue();
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.Synchronized);
        await Assert.That(applied.LastAppliedSequence).IsEqualTo(7);
        await Assert.That(harness.Executor.AppliedBatchCount).IsEqualTo(1);
        ReplaceBridgeOverlayUpdate replacement = harness.Executor.LastReplacement!;
        await Assert.That(replacement.BridgeRootPath).IsEqualTo(BridgeRoot);
        await Assert.That(replacement.Updates.Count).IsEqualTo(2);
        await Assert.That(replacement.Updates[0]).IsTypeOf<DefinePrimUpdate>();
    }

    [Test]
    public async Task InOrderDeltasApplyAndAdvanceTheAppliedSequence()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();

        LiveAuthoringSessionResult first =
            await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(1)));
        LiveAuthoringSessionResult second =
            await harness.Coordinator.ApplyDeltaAsync(Delta(2, Epoch(1), Pressure(2)));

        await Assert.That(first.IsApplied).IsTrue();
        await Assert.That(second.IsApplied).IsTrue();
        LiveAuthoringSessionStatus status = harness.Coordinator.GetStatus();
        await Assert.That(status.State).IsEqualTo(LiveAuthoringSessionState.Synchronized);
        await Assert.That(status.LastAcceptedSequence).IsEqualTo(2);
        await Assert.That(status.LastAppliedSequence).IsEqualTo(2);
        await Assert.That(status.AppliedDeltaCount).IsEqualTo(2);
        await Assert.That(harness.Executor.AppliedBatchCount).IsEqualTo(3);
    }

    [Test]
    public async Task ReplayedDeltaIsIdempotentAndNeverReachesTheStageTwice()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();
        await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(1)));
        int appliedAfterFirst = harness.Executor.AppliedBatchCount;

        LiveAuthoringSessionResult replay =
            await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(1)));

        await Assert.That(replay.Outcome).IsEqualTo(LiveAuthoringSessionOutcome.Duplicate);
        await Assert.That(harness.Executor.AppliedBatchCount).IsEqualTo(appliedAfterFirst);
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.Synchronized);
        await Assert.That(harness.Coordinator.GetStatus().DuplicateDeltaCount).IsEqualTo(1);
    }

    [Test]
    public async Task SequenceGapEntersResyncRequiredAndRejectsEveryFurtherDelta()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();
        await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(1)));

        LiveAuthoringSessionResult gap =
            await harness.Coordinator.ApplyDeltaAsync(Delta(3, Epoch(1), Pressure(3)));
        LiveAuthoringSessionResult afterGap =
            await harness.Coordinator.ApplyDeltaAsync(Delta(2, Epoch(1), Pressure(2)));

        await Assert.That(gap.Rejection).IsEqualTo(LiveAuthoringSessionRejection.SequenceGap);
        await Assert.That(afterGap.Rejection).IsEqualTo(LiveAuthoringSessionRejection.ResyncRequired);
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.ResyncRequired);
        await Assert.That(harness.Executor.AppliedBatchCount).IsEqualTo(2);
    }

    [Test]
    public async Task NewerFullSnapshotRecoversAResyncRequiredSession()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();
        await harness.Coordinator.ApplyDeltaAsync(Delta(5, Epoch(1), Pressure(5)));
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.ResyncRequired);

        LiveAuthoringSessionResult recovered = await harness.Coordinator.ApplySnapshotAsync(
            Snapshot(
                9,
                Epoch(1),
                new DefinePrimUpdate($"{BridgeRoot}/Cube", "Xform"),
                Pressure(9)));
        LiveAuthoringSessionResult resumed =
            await harness.Coordinator.ApplyDeltaAsync(Delta(10, Epoch(1), Pressure(10)));

        await Assert.That(recovered.IsApplied).IsTrue();
        await Assert.That(resumed.IsApplied).IsTrue();
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.Synchronized);
        await Assert.That(harness.Coordinator.GetStatus().LastAppliedSequence).IsEqualTo(10);
    }

    [Test]
    public async Task IncrementalApplyFailureEntersResyncRequiredWithoutACheckpoint()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();
        harness.Executor.FailWhenAttributeIs("custom:poison");

        LiveAuthoringSessionResult failed = await harness.Coordinator.ApplyDeltaAsync(
            Delta(
                1,
                Epoch(1),
                new SetAttributeUpdate(
                    $"{BridgeRoot}/Cube",
                    "custom:poison",
                    LiveAttributeValue.FromDouble(1))));
        LiveAuthoringSessionResult next =
            await harness.Coordinator.ApplyDeltaAsync(Delta(2, Epoch(1), Pressure(2)));

        await Assert.That(failed.Rejection).IsEqualTo(LiveAuthoringSessionRejection.ApplyFailed);
        await Assert.That(next.Rejection).IsEqualTo(LiveAuthoringSessionRejection.ResyncRequired);
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.ResyncRequired);
        // The failed delta never becomes part of the exported overlay, because the candidate model is
        // adopted only after the edit succeeds.
        await Assert.That(harness.Coordinator.ExportOverlayUpdates()
                .OfType<SetAttributeUpdate>()
                .Any(static update => update.AttributeName == "custom:poison"))
            .IsFalse();
    }

    [Test]
    public async Task DeltaCarryingTheLocalOriginIsSuppressedAsAnEcho()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();
        int appliedBefore = harness.Executor.AppliedBatchCount;

        LiveAuthoringSessionResult echo = await harness.Coordinator.ApplyDeltaAsync(
            new LiveAuthoringDelta(
                Epoch(1),
                1,
                [Pressure(1)],
                originId: Harness.LocalOrigin));

        await Assert.That(echo.Outcome).IsEqualTo(LiveAuthoringSessionOutcome.LoopSuppressed);
        await Assert.That(harness.Executor.AppliedBatchCount).IsEqualTo(appliedBefore);
        LiveAuthoringSessionStatus status = harness.Coordinator.GetStatus();
        await Assert.That(status.LoopSuppressedDeltaCount).IsEqualTo(1);
        // The echo is not re-authored, but its sequence is still consumed so the remote's ordered
        // stream stays contiguous and the overlay model keeps describing what the stage holds.
        await Assert.That(status.LastAcceptedSequence).IsEqualTo(1);
        await Assert.That(status.LastAppliedSequence).IsEqualTo(1);
        await Assert.That(harness.Coordinator.ExportOverlayUpdates()
                .OfType<SetAttributeUpdate>()
                .Any(static update => update.AttributeName == "custom:pressure"))
            .IsTrue();
    }

    [Test]
    public async Task ASuppressedEchoDoesNotMakeTheNextDeltaLookLikeAGap()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();

        await harness.Coordinator.ApplyDeltaAsync(new LiveAuthoringDelta(
            Epoch(1),
            1,
            [Pressure(1)],
            originId: Harness.LocalOrigin));
        LiveAuthoringSessionResult next =
            await harness.Coordinator.ApplyDeltaAsync(Delta(2, Epoch(1), Pressure(2)));

        await Assert.That(next.IsApplied).IsTrue().Because(next.Detail ?? "no detail");
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.Synchronized);
    }

    [Test]
    public async Task ALocalOriginDeltaFromAnotherSessionIsRejectedNotReportedAsAnEcho()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();

        LiveAuthoringSessionResult foreignSession = await harness.Coordinator.ApplyDeltaAsync(
            new LiveAuthoringDelta(
                new LiveAuthoringRemoteEpoch(RemoteOrigin, "session-b", 1),
                1,
                [Pressure(1)],
                originId: Harness.LocalOrigin));

        await Assert.That(foreignSession.Outcome).IsEqualTo(LiveAuthoringSessionOutcome.Rejected);
        await Assert.That(foreignSession.Rejection)
            .IsEqualTo(LiveAuthoringSessionRejection.SessionIdentity);
        LiveAuthoringSessionStatus status = harness.Coordinator.GetStatus();
        await Assert.That(status.LoopSuppressedDeltaCount).IsEqualTo(0);
        await Assert.That(status.LastAcceptedSequence).IsEqualTo(0);
    }

    [Test]
    public async Task ALocalOriginDeltaOnADisconnectedSessionIsRejectedNotReportedAsAnEcho()
    {
        await using Harness harness = Harness.Create();

        LiveAuthoringSessionResult disconnected = await harness.Coordinator.ApplyDeltaAsync(
            new LiveAuthoringDelta(Epoch(1), 1, [Pressure(1)], originId: Harness.LocalOrigin));

        await Assert.That(disconnected.Outcome).IsEqualTo(LiveAuthoringSessionOutcome.Rejected);
        await Assert.That(disconnected.Rejection)
            .IsEqualTo(LiveAuthoringSessionRejection.SessionState);
        await Assert.That(harness.Coordinator.GetStatus().LoopSuppressedDeltaCount).IsEqualTo(0);
    }

    [Test]
    public async Task AReplayWithConflictingContentIsNotAcknowledgedAsADuplicate()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();
        await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(1)));
        int appliedAfterFirst = harness.Executor.AppliedBatchCount;

        LiveAuthoringSessionResult conflict =
            await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(99)));
        LiveAuthoringSessionResult blocked =
            await harness.Coordinator.ApplyDeltaAsync(Delta(2, Epoch(1), Pressure(2)));

        await Assert.That(conflict.Outcome).IsEqualTo(LiveAuthoringSessionOutcome.Rejected);
        await Assert.That(conflict.Rejection)
            .IsEqualTo(LiveAuthoringSessionRejection.DuplicateConflict);
        await Assert.That(blocked.Rejection).IsEqualTo(LiveAuthoringSessionRejection.ResyncRequired);
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.ResyncRequired);
        await Assert.That(harness.Executor.AppliedBatchCount).IsEqualTo(appliedAfterFirst);
        await Assert.That(harness.Coordinator.GetStatus().DuplicateDeltaCount).IsEqualTo(0);
    }

    [Test]
    public async Task AReplayDifferingOnlyByCorrelationIdentityIsNotADuplicate()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();
        await harness.Coordinator.ApplyDeltaAsync(new LiveAuthoringDelta(
            Epoch(1),
            1,
            [Pressure(1)],
            correlationId: "remote-1",
            originId: RemoteOrigin));

        LiveAuthoringSessionResult conflict = await harness.Coordinator.ApplyDeltaAsync(
            new LiveAuthoringDelta(
                Epoch(1),
                1,
                [Pressure(1)],
                correlationId: "remote-1-retried",
                originId: RemoteOrigin));

        await Assert.That(conflict.Rejection)
            .IsEqualTo(LiveAuthoringSessionRejection.DuplicateConflict);
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.ResyncRequired);
    }

    [Test]
    public async Task AReplayOlderThanTheRetainedWindowIsRejectedRatherThanClaimedAsADuplicate()
    {
        await using Harness harness = Harness.Create(replayWindowLength: 2);
        await harness.ConnectAndSynchronizeAsync();
        for (long sequence = 1; sequence <= 3; sequence++)
        {
            await harness.Coordinator.ApplyDeltaAsync(
                Delta(sequence, Epoch(1), Pressure(sequence)));
        }

        LiveAuthoringSessionStatus before = harness.Coordinator.GetStatus();
        LiveAuthoringSessionResult expired =
            await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(1)));

        await Assert.That(before.ReplayWindowLength).IsEqualTo(2);
        await Assert.That(before.ReplayLedgerCount).IsEqualTo(2);
        await Assert.That(before.OldestRetainedSequence).IsEqualTo(2);
        await Assert.That(before.ReplayLedgerBytes)
            .IsEqualTo(2 * LiveAuthoringValidation.ReplayLedgerEntryBytes);
        await Assert.That(expired.Outcome).IsEqualTo(LiveAuthoringSessionOutcome.Rejected);
        await Assert.That(expired.Rejection).IsEqualTo(LiveAuthoringSessionRejection.ReplayExpired);
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.ResyncRequired);
    }

    [Test]
    public async Task AReplayStillInsideTheWindowIsAcknowledgedAfterOlderEntriesAreEvicted()
    {
        await using Harness harness = Harness.Create(replayWindowLength: 2);
        await harness.ConnectAndSynchronizeAsync();
        for (long sequence = 1; sequence <= 3; sequence++)
        {
            await harness.Coordinator.ApplyDeltaAsync(
                Delta(sequence, Epoch(1), Pressure(sequence)));
        }

        LiveAuthoringSessionResult retained =
            await harness.Coordinator.ApplyDeltaAsync(Delta(3, Epoch(1), Pressure(3)));

        await Assert.That(retained.Outcome).IsEqualTo(LiveAuthoringSessionOutcome.Duplicate);
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.Synchronized);
    }

    [Test]
    public async Task ReconnectClearsTheReplayLedgerSoNewEpochSequencesStartFresh()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();
        await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(1)));
        await Assert.That(harness.Coordinator.GetStatus().ReplayLedgerCount).IsEqualTo(1);

        await harness.Coordinator.ConnectAsync(Epoch(2));
        LiveAuthoringSessionStatus reconnected = harness.Coordinator.GetStatus();
        await harness.Coordinator.ApplySnapshotAsync(new LiveAuthoringSnapshot(
            Epoch(2),
            0,
            BridgeRoot,
            [new DefinePrimUpdate($"{BridgeRoot}/Cube", "Xform")]));
        LiveAuthoringSessionResult reused = await harness.Coordinator.ApplyDeltaAsync(
            Delta(1, Epoch(2), Pressure(1)));

        await Assert.That(reconnected.ReplayLedgerCount).IsEqualTo(0);
        await Assert.That(reconnected.OldestRetainedSequence).IsEqualTo(0);
        await Assert.That(reconnected.ReplayLedgerBytes).IsEqualTo(0);
        // Sequence 1 is reused by the new epoch and must be applied, not mistaken for the previous
        // epoch's sequence 1.
        await Assert.That(reused.IsApplied).IsTrue().Because(reused.Detail ?? "no detail");
    }

    [Test]
    public async Task ASnapshotClearsTheReplayLedgerBecauseItEstablishesANewBaseline()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();
        await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(1)));

        await harness.Coordinator.ApplySnapshotAsync(new LiveAuthoringSnapshot(
            Epoch(1),
            4,
            BridgeRoot,
            [new DefinePrimUpdate($"{BridgeRoot}/Cube", "Xform")]));
        LiveAuthoringSessionStatus status = harness.Coordinator.GetStatus();
        LiveAuthoringSessionResult stale =
            await harness.Coordinator.ApplyDeltaAsync(Delta(3, Epoch(1), Pressure(3)));

        await Assert.That(status.ReplayLedgerCount).IsEqualTo(0);
        await Assert.That(status.LastAcceptedSequence).IsEqualTo(4);
        await Assert.That(stale.Rejection).IsEqualTo(LiveAuthoringSessionRejection.ReplayExpired);
    }

    [Test]
    public async Task AReplayWindowLengthOutsideItsBoundsIsRejected()
    {
        await Assert.That(() => new LiveAuthoringSessionCoordinator(
                new QueuedLiveAuthoringSink(new RecordingOverlayExecutor(), capacity: 1),
                new LiveAuthoringSessionOptions { ReplayWindowLength = 0 }))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new LiveAuthoringSessionCoordinator(
                new QueuedLiveAuthoringSink(new RecordingOverlayExecutor(), capacity: 1),
                new LiveAuthoringSessionOptions
                {
                    ReplayWindowLength = LiveAuthoringValidation.MaxReplayWindowLength + 1
                }))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task OnlyTheAuthoritativeRemoteOriginAndSessionAreAccepted()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();

        LiveAuthoringSessionResult foreignOrigin = await harness.Coordinator.ApplyDeltaAsync(
            Delta(1, new LiveAuthoringRemoteEpoch("other-bridge", SessionId, 1), Pressure(1)));
        LiveAuthoringSessionResult foreignSession = await harness.Coordinator.ApplyDeltaAsync(
            Delta(1, new LiveAuthoringRemoteEpoch(RemoteOrigin, "session-b", 1), Pressure(1)));

        await Assert.That(foreignOrigin.Rejection)
            .IsEqualTo(LiveAuthoringSessionRejection.RemoteOrigin);
        await Assert.That(foreignSession.Rejection)
            .IsEqualTo(LiveAuthoringSessionRejection.SessionIdentity);
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.Synchronized);
    }

    [Test]
    public async Task RetiredEpochIsRejectedAndAdvancedEpochRequiresAFullSnapshot()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync(epoch: 2);

        LiveAuthoringSessionResult retired =
            await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(1)));
        await Assert.That(retired.Rejection).IsEqualTo(LiveAuthoringSessionRejection.EpochRetired);
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.Synchronized);

        LiveAuthoringSessionResult advanced =
            await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(3), Pressure(1)));

        await Assert.That(advanced.Rejection).IsEqualTo(LiveAuthoringSessionRejection.EpochAdvanced);
        await Assert.That(harness.Coordinator.State)
            .IsEqualTo(LiveAuthoringSessionState.ResyncRequired);
    }

    [Test]
    public async Task ReconnectDiscardsTheSequenceAgreementAndRequiresAFullSnapshot()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();
        await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(1)));

        LiveAuthoringSessionStatus reconnected = await harness.Coordinator.ConnectAsync(Epoch(2));
        LiveAuthoringSessionResult tooEarly =
            await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(2), Pressure(1)));

        await Assert.That(reconnected.State).IsEqualTo(LiveAuthoringSessionState.Connecting);
        await Assert.That(reconnected.LastAcceptedSequence).IsEqualTo(0);
        await Assert.That(reconnected.LastAppliedSequence).IsEqualTo(0);
        await Assert.That(tooEarly.Rejection).IsEqualTo(LiveAuthoringSessionRejection.SessionState);
    }

    [Test]
    public async Task RemoteEpochCannotMoveBackwardsForTheSameSession()
    {
        await using Harness harness = Harness.Create();
        await harness.Coordinator.ConnectAsync(Epoch(3));

        await Assert.That(async () => await harness.Coordinator.ConnectAsync(Epoch(2)))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task UpdatesOutsideTheBridgeRootAreRejected()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();

        LiveAuthoringSessionResult delta = await harness.Coordinator.ApplyDeltaAsync(
            Delta(
                1,
                Epoch(1),
                new SetAttributeUpdate(
                    "/UserWorld/Widget",
                    "custom:pressure",
                    LiveAttributeValue.FromDouble(1))));
        LiveAuthoringSessionResult snapshot = await harness.Coordinator.ApplySnapshotAsync(
            new LiveAuthoringSnapshot(
                Epoch(1),
                4,
                "/Other",
                [new DefinePrimUpdate("/Other/Cube")]));

        await Assert.That(delta.Rejection).IsEqualTo(LiveAuthoringSessionRejection.BridgeScope);
        await Assert.That(snapshot.Rejection).IsEqualTo(LiveAuthoringSessionRejection.BridgeScope);
    }

    [Test]
    public async Task ASnapshotCannotEscapeItsOwnBridgeRoot()
    {
        await Assert.That(() => new LiveAuthoringSnapshot(
                Epoch(1),
                1,
                BridgeRoot,
                [new DefinePrimUpdate("/BridgeOther/Cube")]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ExportedSnapshotRebuildsTheSameOverlayOnAFreshSession()
    {
        await using Harness source = Harness.Create();
        await source.ConnectAndSynchronizeAsync();
        await source.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(11)));
        await source.Coordinator.ApplyDeltaAsync(
            Delta(
                2,
                Epoch(1),
                new DefinePrimUpdate($"{BridgeRoot}/Sensor", "Xform"),
                new SetMetadataUpdate(
                    $"{BridgeRoot}/Sensor",
                    "comment",
                    LiveMetadataValue.FromString("live"))));
        await source.Coordinator.ApplyDeltaAsync(
            Delta(3, Epoch(1), new RemovePrimUpdate($"{BridgeRoot}/Sensor")));

        LiveAuthoringSnapshot exported = source.Coordinator.ExportSnapshot("handoff");

        await using Harness target = Harness.Create();
        await target.Coordinator.ConnectAsync(Epoch(1));
        LiveAuthoringSessionResult imported = await target.Coordinator.ApplySnapshotAsync(exported);

        await Assert.That(imported.IsApplied).IsTrue();
        await Assert.That(exported.Sequence).IsEqualTo(3);
        await Assert.That(Describe(target.Coordinator.ExportOverlayUpdates()))
            .IsEquivalentTo(Describe(source.Coordinator.ExportOverlayUpdates()));
        // The removed prim left no residue in either overlay.
        await Assert.That(Describe(exported.Updates).Any(
                static text => text.Contains("/Sensor", StringComparison.Ordinal)))
            .IsFalse();
    }

    [Test]
    public async Task ClearingAnOpinionDropsItFromTheExportedOverlay()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();
        await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(3)));
        await Assert.That(harness.Coordinator.ExportOverlayUpdates().Count).IsEqualTo(2);

        await harness.Coordinator.ApplyDeltaAsync(
            Delta(
                2,
                Epoch(1),
                new ClearUpdate(
                    $"{BridgeRoot}/Cube",
                    LiveClearTargetKind.AttributeValue,
                    "custom:pressure")));

        await Assert.That(harness.Coordinator.ExportOverlayUpdates().Count).IsEqualTo(1);
        await Assert.That(harness.Coordinator.ExportOverlayUpdates()[0])
            .IsTypeOf<DefinePrimUpdate>();
    }

    [Test]
    public async Task RepeatingAnOpinionSupersedesItInsteadOfAccumulating()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();

        for (long sequence = 1; sequence <= 5; sequence++)
        {
            await harness.Coordinator.ApplyDeltaAsync(Delta(sequence, Epoch(1), Pressure(sequence)));
        }

        IReadOnlyList<LiveStageUpdate> overlay = harness.Coordinator.ExportOverlayUpdates();
        await Assert.That(overlay.Count).IsEqualTo(2);
        await Assert.That(overlay.OfType<SetAttributeUpdate>().Single().Value.DoubleValue)
            .IsEqualTo(5);
    }

    [Test]
    public async Task RequestResyncForcesAFullSnapshotBeforeDeltasResume()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();

        LiveAuthoringSessionStatus status = harness.Coordinator.RequestResync("transport reset");
        LiveAuthoringSessionResult rejected =
            await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(1)));

        await Assert.That(status.State).IsEqualTo(LiveAuthoringSessionState.ResyncRequired);
        await Assert.That(status.LastFailureDetail).IsEqualTo("transport reset");
        await Assert.That(rejected.Rejection).IsEqualTo(LiveAuthoringSessionRejection.ResyncRequired);
    }

    [Test]
    public async Task ASinkDisposedBeneathTheSessionFaultsItWithoutAFalseRecoveryPath()
    {
        var executor = new RecordingOverlayExecutor();
        var sink = new QueuedLiveAuthoringSink(executor, capacity: 4);
        await using var coordinator = new LiveAuthoringSessionCoordinator(
            sink,
            new LiveAuthoringSessionOptions
            {
                BridgeRootPath = BridgeRoot,
                LocalOriginId = Harness.LocalOrigin
            });
        await coordinator.ConnectAsync(Epoch(1));
        await sink.DisposeAsync();

        LiveAuthoringSessionResult result = await coordinator.ApplySnapshotAsync(
            Snapshot(0, Epoch(1), new DefinePrimUpdate($"{BridgeRoot}/Cube", "Xform")));

        await Assert.That(result.Rejection).IsEqualTo(LiveAuthoringSessionRejection.ApplyFailed);
        await Assert.That(coordinator.State).IsEqualTo(LiveAuthoringSessionState.Faulted);
        await Assert.That(coordinator.RequestResync("adapter retry").State)
            .IsEqualTo(LiveAuthoringSessionState.Faulted);
    }

    [Test]
    public async Task RequestResyncOnADisconnectedSessionChangesNothing()
    {
        await using Harness harness = Harness.Create();

        LiveAuthoringSessionStatus status = harness.Coordinator.RequestResync("noise");

        await Assert.That(status.State).IsEqualTo(LiveAuthoringSessionState.Disconnected);
        await Assert.That(status.ResyncRequiredCount).IsEqualTo(0);
    }

    [Test]
    public async Task DisconnectReleasesTheEpochAndClearsTheExportedBaseline()
    {
        await using Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();
        await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(1)));

        LiveAuthoringSessionStatus status = await harness.Coordinator.DisconnectAsync();

        await Assert.That(status.State).IsEqualTo(LiveAuthoringSessionState.Disconnected);
        await Assert.That(status.RemoteOriginId).IsNull();
        await Assert.That(harness.Coordinator.ExportOverlayUpdates()).IsEmpty();
        await Assert.That(() => harness.Coordinator.ExportSnapshot())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StructuredSessionEventsDescribeEveryTransition()
    {
        var observer = new RecordingSessionObserver();
        await using Harness harness = Harness.Create(observer);
        await harness.ConnectAndSynchronizeAsync();
        await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(1)));
        await harness.Coordinator.ApplyDeltaAsync(Delta(4, Epoch(1), Pressure(4)));

        LiveAuthoringSessionEventKind[] kinds = [.. observer.Events.Select(static e => e.Kind)];
        await Assert.That(kinds).Contains(LiveAuthoringSessionEventKind.Connecting);
        await Assert.That(kinds).Contains(LiveAuthoringSessionEventKind.SnapshotApplied);
        await Assert.That(kinds).Contains(LiveAuthoringSessionEventKind.DeltaApplied);
        await Assert.That(kinds).Contains(LiveAuthoringSessionEventKind.ResyncRequired);
        LiveAuthoringSessionEvent applied = observer.Events.First(
            static e => e.Kind == LiveAuthoringSessionEventKind.DeltaApplied);
        await Assert.That(applied.RemoteOriginId).IsEqualTo(RemoteOrigin);
        await Assert.That(applied.SessionId).IsEqualTo(SessionId);
        await Assert.That(applied.LastAppliedSequence).IsEqualTo(1);
    }

    [Test]
    public async Task ABrokenSessionObserverCannotChangeSessionSemantics()
    {
        var observer = new ThrowingSessionObserver();
        await using Harness harness = Harness.Create(observer);

        await harness.ConnectAndSynchronizeAsync();
        LiveAuthoringSessionResult applied =
            await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(1)));

        await Assert.That(applied.IsApplied).IsTrue();
        LiveAuthoringSessionStatus status = harness.Coordinator.GetStatus();
        await Assert.That(status.State).IsEqualTo(LiveAuthoringSessionState.Synchronized);
        await Assert.That(status.SessionObserverFailureCount).IsGreaterThan(0);
        await Assert.That(status.LastSessionObserverFailureDetail).IsNotNull();
    }

    [Test]
    public async Task DisposalIsIdempotentAndRejectsLaterOperations()
    {
        Harness harness = Harness.Create();
        await harness.ConnectAndSynchronizeAsync();

        await harness.Coordinator.DisposeAsync();
        await harness.Coordinator.DisposeAsync();

        await Assert.That(
                async () => await harness.Coordinator.ApplyDeltaAsync(Delta(1, Epoch(1), Pressure(1))))
            .Throws<ObjectDisposedException>();
        await Assert.That(() => harness.Coordinator.RequestResync())
            .Throws<ObjectDisposedException>();
        await harness.DisposeAsync();
    }

    [Test]
    public async Task DisposalOwnsTheSinkOnlyWhenAskedTo()
    {
        var executor = new RecordingOverlayExecutor();
        var sink = new QueuedLiveAuthoringSink(executor, capacity: 4);
        var coordinator = new LiveAuthoringSessionCoordinator(
            sink,
            new LiveAuthoringSessionOptions
            {
                BridgeRootPath = BridgeRoot,
                LocalOriginId = Harness.LocalOrigin,
                OwnsSink = true
            });

        await coordinator.DisposeAsync();

        await Assert.That(sink.Completion.IsCompleted).IsTrue();
        await Assert.That(
                async () => await sink.ApplyAsync(
                    new LiveAuthoringBatch(1, [new DefinePrimUpdate($"{BridgeRoot}/Cube")])))
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task ABridgeOverlayReplacementMustBeTheOnlyUpdateInItsBatch()
    {
        await Assert.That(() => new LiveAuthoringBatch(
                1,
                [
                    new ReplaceBridgeOverlayUpdate(
                        BridgeRoot,
                        [new DefinePrimUpdate($"{BridgeRoot}/Cube")]),
                    new DefinePrimUpdate($"{BridgeRoot}/Other")
                ]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ABridgeOverlayReplacementCannotNestAnotherReplacement()
    {
        await Assert.That(() => new LiveAuthoringBatch(
                1,
                [
                    new ReplaceBridgeOverlayUpdate(
                        BridgeRoot,
                        [
                            new ReplaceBridgeOverlayUpdate(
                                BridgeRoot,
                                [new DefinePrimUpdate($"{BridgeRoot}/Cube")])
                        ])
                ]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ABridgeOverlayRootCannotBeTheStagePseudoRoot()
    {
        await Assert.That(() => new LiveAuthoringSessionCoordinator(
                new QueuedLiveAuthoringSink(new RecordingOverlayExecutor(), capacity: 1),
                new LiveAuthoringSessionOptions { BridgeRootPath = "/" }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ABridgeOverlayReplacementIsBoundedByTheOverlayUpdateLimit()
    {
        LiveStageUpdate[] updates =
        [
            .. Enumerable
                .Range(0, LiveAuthoringValidation.MaxBridgeOverlayUpdates + 1)
                .Select(index => new DefinePrimUpdate($"{BridgeRoot}/Prim{index}"))
        ];

        await Assert.That(() => new ReplaceBridgeOverlayUpdate(BridgeRoot, updates))
            .ThrowsNothing();
        await Assert.That(() => new LiveAuthoringBatch(
                1,
                [new ReplaceBridgeOverlayUpdate(BridgeRoot, updates)]))
            .Throws<ArgumentException>();
    }

    private static LiveAuthoringRemoteEpoch Epoch(long epoch) =>
        new(RemoteOrigin, SessionId, epoch);

    private static SetAttributeUpdate Pressure(double value) =>
        new($"{BridgeRoot}/Cube", "custom:pressure", LiveAttributeValue.FromDouble(value));

    private static LiveAuthoringSnapshot Snapshot(
        long sequence,
        LiveAuthoringRemoteEpoch epoch,
        params LiveStageUpdate[] updates) =>
        new(epoch, sequence, BridgeRoot, updates);

    private static LiveAuthoringDelta Delta(
        long sequence,
        LiveAuthoringRemoteEpoch epoch,
        params LiveStageUpdate[] updates) =>
        new(
            epoch,
            sequence,
            updates.Length == 0 ? [new DefinePrimUpdate($"{BridgeRoot}/Cube")] : updates,
            correlationId: $"remote-{sequence}",
            originId: RemoteOrigin);

    private static string[] Describe(IEnumerable<LiveStageUpdate> updates) =>
        [.. updates.Select(static update => update.ToString() ?? string.Empty)];

    private sealed class Harness : IAsyncDisposable
    {
        internal const string LocalOrigin = "openusd-test";

        private Harness(
            RecordingOverlayExecutor executor,
            QueuedLiveAuthoringSink sink,
            LiveAuthoringSessionCoordinator coordinator)
        {
            Executor = executor;
            Sink = sink;
            Coordinator = coordinator;
        }

        internal RecordingOverlayExecutor Executor { get; }

        internal QueuedLiveAuthoringSink Sink { get; }

        internal LiveAuthoringSessionCoordinator Coordinator { get; }

        internal static Harness Create(
            IProgress<LiveAuthoringSessionEvent>? observer = null,
            int replayWindowLength = LiveAuthoringValidation.DefaultReplayWindowLength)
        {
            var executor = new RecordingOverlayExecutor();
            var sink = new QueuedLiveAuthoringSink(executor, capacity: 8);
            var coordinator = new LiveAuthoringSessionCoordinator(
                sink,
                new LiveAuthoringSessionOptions
                {
                    BridgeRootPath = BridgeRoot,
                    LocalOriginId = LocalOrigin,
                    ReplayWindowLength = replayWindowLength,
                    SessionObserver = observer
                });
            return new Harness(executor, sink, coordinator);
        }

        internal async Task ConnectAndSynchronizeAsync(long epoch = 1)
        {
            await Coordinator.ConnectAsync(new LiveAuthoringRemoteEpoch(
                RemoteOrigin,
                SessionId,
                epoch));
            await Coordinator.ApplySnapshotAsync(new LiveAuthoringSnapshot(
                new LiveAuthoringRemoteEpoch(RemoteOrigin, SessionId, epoch),
                0,
                BridgeRoot,
                [new DefinePrimUpdate($"{BridgeRoot}/Cube", "Xform")]));
        }

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync();
            await Sink.DisposeAsync();
        }
    }

    private sealed class RecordingOverlayExecutor : ILiveAuthoringBatchExecutor
    {
        private readonly List<LiveStageUpdate> _overlay = [];
        private string? _failAttributeName;
        private int _appliedBatchCount;

        internal int AppliedBatchCount => Volatile.Read(ref _appliedBatchCount);

        internal ReplaceBridgeOverlayUpdate? LastReplacement { get; private set; }

        internal IReadOnlyList<LiveStageUpdate> Overlay => _overlay;

        internal void FailWhenAttributeIs(string attributeName) =>
            _failAttributeName = attributeName;

        public ValueTask<LiveAuthoringBatchResult> ExecuteAsync(
            LiveAuthoringBatch batch,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (LiveStageUpdate update in batch.Updates)
            {
                if (update is SetAttributeUpdate attribute &&
                    _failAttributeName is not null &&
                    attribute.AttributeName == _failAttributeName)
                {
                    throw new InvalidOperationException(
                        $"The executor deliberately fails '{attribute.AttributeName}'.");
                }

                if (update is ReplaceBridgeOverlayUpdate replacement)
                {
                    LastReplacement = replacement;
                    _overlay.Clear();
                    _overlay.AddRange(replacement.Updates);
                }
                else
                {
                    _overlay.Add(update);
                }
            }

            Interlocked.Increment(ref _appliedBatchCount);
            return ValueTask.FromResult(new LiveAuthoringBatchResult(
                batch.Sequence,
                batch.Sequence,
                1,
                batch.Updates.Count,
                batch.Invalidation,
                (ulong)batch.Sequence,
                (ulong)batch.Sequence + 1,
                "session",
                batch.CorrelationId,
                batch.OriginId));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingSessionObserver : IProgress<LiveAuthoringSessionEvent>
    {
        private readonly List<LiveAuthoringSessionEvent> _events = [];
        private readonly object _gate = new();

        internal IReadOnlyList<LiveAuthoringSessionEvent> Events
        {
            get
            {
                lock (_gate)
                {
                    return [.. _events];
                }
            }
        }

        public void Report(LiveAuthoringSessionEvent value)
        {
            lock (_gate)
            {
                _events.Add(value);
            }
        }
    }

    private sealed class ThrowingSessionObserver : IProgress<LiveAuthoringSessionEvent>
    {
        public void Report(LiveAuthoringSessionEvent value) =>
            throw new InvalidOperationException("The session observer deliberately fails.");
    }
}
