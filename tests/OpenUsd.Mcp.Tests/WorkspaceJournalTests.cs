// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp.Tests;

public sealed class WorkspaceJournalTests
{
    [Test]
    public async Task CommittedEditAdvancesGenerationAndJournalsCheckpoint()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");

        WorkspaceEditResult result = await workspace.EditAsync(
            new WorkspaceSessionRevision(
                session.SessionId,
                session.Generation,
                session.StageRevision),
            new WorkspaceEditBatch([new DefinePrimWorkspaceEdit("/World", "Xform")]));
        WorkspaceSessionManifest manifest = await workspace.GetManifestAsync(
            new WorkspaceSessionRevision(
                session.SessionId,
                result.Generation,
                result.StageRevision));

        await Assert.That(result.Generation).IsEqualTo(1);
        await Assert.That(manifest.Checkpoints).Count().IsEqualTo(1);
        await Assert.That(manifest.Journal.Select(static entry => entry.Sequence))
            .IsEquivalentTo([1L, 2L]);
        await Assert.That(manifest.Journal.Select(static entry => entry.Sequence)).IsInOrder();
        await Assert.That(manifest.Journal[^1].Kind)
            .IsEqualTo(WorkspaceJournalKind.EditCommitted);
        await Assert.That(manifest.Journal[^1].CheckpointId)
            .IsEqualTo(result.Checkpoint.CheckpointId);
    }

    [Test]
    public async Task FailedEditRollsBackAndJournalsWithoutAdvancingGeneration()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend { ApplyError = new InvalidOperationException("fail") };
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");

        await Assert.That(
                async () => await workspace.EditAsync(
                    new WorkspaceSessionRevision(
                        session.SessionId,
                        session.Generation,
                        session.StageRevision),
                    new WorkspaceEditBatch([new DefinePrimWorkspaceEdit("/World")])))
            .Throws<InvalidOperationException>();
        WorkspaceSessionStatus status = await workspace.GetStatusAsync();
        WorkspaceSessionInfo current = status.Session!;
        WorkspaceSessionManifest manifest = await workspace.GetManifestAsync(
            new WorkspaceSessionRevision(
                current.SessionId,
                current.Generation,
                current.StageRevision));

        await Assert.That(backend.Events).Contains("rollback");
        await Assert.That(manifest.Generation).IsEqualTo(0);
        await Assert.That(manifest.StageRevision).IsEqualTo(1UL);
        await Assert.That(manifest.Journal[^1].Kind).IsEqualTo(WorkspaceJournalKind.EditFailed);
    }

    [Test]
    public async Task SessionClosedIsPersistedAfterBackendDisposal()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        backend.Events.Clear();

        await workspace.CloseAsync(new WorkspaceSessionRevision(
            session.SessionId,
            session.Generation,
            session.StageRevision));

        await Assert.That(string.Join(",", backend.Events))
            .IsEqualTo("get-stage-revision,dispose,persist");
        await Assert.That(backend.LastManifest!.Journal[^1].Kind)
            .IsEqualTo(WorkspaceJournalKind.SessionClosed);
    }

    [Test]
    public async Task CloseFailureRejectsOperationsAndSuccessfulRetryIsJournaled()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend
        {
            DisposeFailuresRemaining = 1,
        };
        var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        var revision = new WorkspaceSessionRevision(
            session.SessionId,
            session.Generation,
            session.StageRevision);

        await Assert.That(async () => await workspace.CloseAsync(revision))
            .ThrowsExactly<IOException>();
        await Assert.That(backend.LastManifest!.Journal[^1].Kind)
            .IsEqualTo(WorkspaceJournalKind.SessionCloseFailed);
        await Assert.That(backend.LastManifest.Journal)
            .DoesNotContain(entry => entry.Kind == WorkspaceJournalKind.SessionClosed);
        await Assert.That(async () => await workspace.CheckpointAsync(revision))
            .ThrowsExactly<InvalidOperationException>();

        await workspace.CloseAsync(revision);
        await workspace.DisposeAsync();

        await Assert.That(backend.LastManifest!.Journal
                .TakeLast(3)
                .Select(static entry => entry.Kind))
            .IsEquivalentTo(
            [
                WorkspaceJournalKind.SessionCloseFailed,
                WorkspaceJournalKind.SessionCloseRetry,
                WorkspaceJournalKind.SessionClosed,
            ]);
        await Assert.That(backend.DisposeAttemptCount).IsEqualTo(2);
    }

    [Test]
    public async Task CloseReserveAllowsFailureAndRetryAtJournalBoundary()
    {
        using var files = new WorkspaceTestFiles(maximumJournalEntryCount: 5);
        var backend = new RecordingWorkspaceBackend
        {
            DisposeFailuresRemaining = 2,
        };
        var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        var revision = new WorkspaceSessionRevision(
            session.SessionId,
            session.Generation,
            session.StageRevision);
        _ = await workspace.CheckpointAsync(revision);

        await Assert.That(async () => await workspace.CheckpointAsync(revision))
            .ThrowsExactly<WorkspaceQuotaExceededException>();
        await Assert.That(async () => await workspace.CloseAsync(revision))
            .ThrowsExactly<IOException>();
        await Assert.That(async () => await workspace.CloseAsync(revision))
            .ThrowsExactly<IOException>();
        await workspace.CloseAsync(revision);
        await workspace.DisposeAsync();

        await Assert.That(backend.LastManifest!.Journal).Count().IsEqualTo(5);
        await Assert.That(backend.LastManifest.Journal[^1].Kind)
            .IsEqualTo(WorkspaceJournalKind.SessionClosed);
        await Assert.That(backend.Events.Count(static item => item == "checkpoint"))
            .IsEqualTo(1);
        await Assert.That(backend.DisposeAttemptCount).IsEqualTo(3);
    }

    [Test]
    public async Task CommitJournalFailureRestoresCheckpointAndPersistsRecovery()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        backend.Events.Clear();
        backend.PersistFailuresRemaining = 1;

        await Assert.That(
                async () => await workspace.EditAsync(
                    new WorkspaceSessionRevision(
                        session.SessionId,
                        session.Generation,
                        session.StageRevision),
                    new WorkspaceEditBatch([new DefinePrimWorkspaceEdit("/World")])))
            .Throws<IOException>();

        WorkspaceSessionInfo current = (await workspace.GetStatusAsync()).Session!;
        WorkspaceSessionManifest manifest = await workspace.GetManifestAsync(
            new WorkspaceSessionRevision(
                current.SessionId,
                current.Generation,
                current.StageRevision));
        await Assert.That(string.Join(",", backend.Events.Take(5)))
            .IsEqualTo("get-stage-revision,checkpoint,apply,persist-failed,rollback");
        await Assert.That(manifest.Generation).IsEqualTo(session.Generation);
        await Assert.That(manifest.Journal[^1].Kind)
            .IsEqualTo(WorkspaceJournalKind.EditFailed);
    }

    [Test]
    public async Task CheckpointManifestFailureDeletesAndUntracksUncommittedCheckpoint()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        backend.PersistFailuresRemaining = 1;

        await Assert.That(
                async () => await workspace.CheckpointAsync(
                    new WorkspaceSessionRevision(
                        session.SessionId,
                        session.Generation,
                        session.StageRevision)))
            .Throws<IOException>();
        WorkspaceSessionManifest manifest = await workspace.GetManifestAsync(
            new WorkspaceSessionRevision(
                session.SessionId,
                session.Generation,
                session.StageRevision));

        await Assert.That(manifest.Checkpoints).IsEmpty();
        await Assert.That(manifest.Journal).Count().IsEqualTo(1);
        await Assert.That(manifest.Journal[0].Kind)
            .IsEqualTo(WorkspaceJournalKind.SessionCreated);
        await Assert.That(CheckpointFiles(session)).IsEmpty();
        await Assert.That(backend.DeletedCheckpointIds).Count().IsEqualTo(1);
    }

    [Test]
    public async Task CheckpointCleanupFailureAggregatesErrorsAndRemainsRecoverable()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend
        {
            DeleteCheckpointFailuresRemaining = 1,
        };
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        backend.PersistFailuresRemaining = 1;

        AggregateException? failure = null;
        try
        {
            _ = await workspace.CheckpointAsync(Revision(session));
        }
        catch (AggregateException exception)
        {
            failure = exception;
        }

        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.InnerExceptions).Count().IsEqualTo(2);
        await Assert.That(failure.InnerExceptions[0].Message).IsEqualTo("persist failed");
        await Assert.That(failure.InnerExceptions[1].Message)
            .IsEqualTo("delete checkpoint failed");
        WorkspaceSessionManifest pending = await workspace.GetManifestAsync(Revision(session));
        await Assert.That(pending.Checkpoints).Count().IsEqualTo(1);
        await Assert.That(CheckpointFiles(session)).Count().IsEqualTo(1);

        WorkspaceCheckpointResult recovered = await workspace.CheckpointAsync(Revision(session));
        WorkspaceSessionManifest manifest = await workspace.GetManifestAsync(Revision(session));

        await Assert.That(manifest.Checkpoints).Count().IsEqualTo(1);
        await Assert.That(manifest.Checkpoints[0].CheckpointId)
            .IsEqualTo(recovered.Checkpoint.CheckpointId);
        await Assert.That(CheckpointFiles(session)).Count().IsEqualTo(1);
        await Assert.That(backend.Events.Count(static item => item == "checkpoint"))
            .IsEqualTo(2);
    }

    [Test]
    public async Task RepeatedCheckpointPersistenceFailuresDoNotGrowDiskOrConsumeQuota()
    {
        using var files = new WorkspaceTestFiles(maximumCheckpointCount: 1);
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        backend.PersistFailuresRemaining = 3;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            await Assert.That(async () => await workspace.CheckpointAsync(Revision(session)))
                .ThrowsExactly<IOException>();
            await Assert.That(CheckpointFiles(session)).IsEmpty();
        }

        WorkspaceCheckpointResult result = await workspace.CheckpointAsync(Revision(session));
        WorkspaceSessionManifest manifest = await workspace.GetManifestAsync(Revision(session));

        await Assert.That(manifest.Checkpoints).Count().IsEqualTo(1);
        await Assert.That(manifest.Checkpoints[0].CheckpointId)
            .IsEqualTo(result.Checkpoint.CheckpointId);
        await Assert.That(CheckpointFiles(session)).Count().IsEqualTo(1);
        await Assert.That(backend.DeletedCheckpointIds).Count().IsEqualTo(3);
    }

    [Test]
    public async Task FailedCheckpointCommitNeverDeletesExistingValidCheckpoint()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        WorkspaceCheckpointResult existing = await workspace.CheckpointAsync(Revision(session));
        backend.PersistFailuresRemaining = 1;

        await Assert.That(async () => await workspace.CheckpointAsync(Revision(session)))
            .ThrowsExactly<IOException>();
        WorkspaceSessionManifest manifest = await workspace.GetManifestAsync(Revision(session));

        await Assert.That(manifest.Checkpoints).Count().IsEqualTo(1);
        await Assert.That(manifest.Checkpoints[0].CheckpointId)
            .IsEqualTo(existing.Checkpoint.CheckpointId);
        await Assert.That(CheckpointFiles(session)).Count().IsEqualTo(1);
        await Assert.That(backend.DeletedCheckpointIds)
            .DoesNotContain(existing.Checkpoint.CheckpointId);
    }

    [Test]
    public async Task ExplicitRollbackCheckpointsCurrentOverlayBeforeMutation()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        WorkspaceCheckpointResult target = await workspace.CheckpointAsync(
            new WorkspaceSessionRevision(
                session.SessionId,
                session.Generation,
                session.StageRevision));
        backend.Events.Clear();

        WorkspaceRollbackResult result = await workspace.RollbackAsync(
            new WorkspaceSessionRevision(
                target.SessionId,
                target.Generation,
                target.StageRevision),
            target.Checkpoint.CheckpointId);

        await Assert.That(string.Join(",", backend.Events))
            .IsEqualTo("get-stage-revision,checkpoint,rollback,persist");
        await Assert.That(result.Generation).IsEqualTo(target.Generation + 1);
        await Assert.That(result.StageRevision).IsGreaterThan(target.StageRevision);
    }

    private static string[] CheckpointFiles(WorkspaceSessionInfo session)
    {
        string checkpointDirectory = Path.Combine(session.OutputDirectory, "checkpoints");
        return Directory.Exists(checkpointDirectory)
            ? Directory.GetFiles(checkpointDirectory, "*.usda")
            : [];
    }

    private static WorkspaceSessionRevision Revision(WorkspaceSessionInfo session) =>
        new(session.SessionId, session.Generation, session.StageRevision);
}
