// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp.Tests;

public sealed class WorkspaceSessionTests
{
    [Test]
    public async Task StaleAndForeignRevisionsAreRejected()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        var revision = Revision(session);
        WorkspaceEditResult result = await workspace.EditAsync(
            revision,
            new WorkspaceEditBatch([new DefinePrimWorkspaceEdit("/World", "Xform")]));

        await Assert.That(
                async () => await workspace.EditAsync(
                    revision,
                    new WorkspaceEditBatch([new SetActiveWorkspaceEdit("/World", false)])))
            .Throws<WorkspaceSessionRevisionException>();
        await Assert.That(
                async () => await workspace.CheckpointAsync(
                    new WorkspaceSessionRevision(
                        "foreign",
                        result.Generation,
                        result.StageRevision)))
            .Throws<WorkspaceSessionRevisionException>();
        await Assert.That(
                async () => await workspace.CheckpointAsync(
                    new WorkspaceSessionRevision(
                        result.SessionId,
                        result.Generation,
                        result.StageRevision + 1)))
            .Throws<WorkspaceSessionRevisionException>();
    }

    [Test]
    public async Task CloseUpdatesStatusAndAllowsAReplacementSession()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo first = await workspace.StartAsync("scene.usda");

        await Assert.That(async () => await workspace.StartAsync("scene.usda"))
            .Throws<InvalidOperationException>();
        WorkspaceSessionStatus active = await workspace.GetStatusAsync();
        WorkspaceSessionSnapshot snapshot = await workspace.GetSnapshotAsync(Revision(first));
        await workspace.CloseAsync(Revision(first));
        WorkspaceSessionStatus closed = await workspace.GetStatusAsync();
        WorkspaceSessionInfo second = await workspace.StartAsync("scene.usda");
        await workspace.CloseAsync(Revision(second));
        await workspace.DisposeAsync();

        await Assert.That(active.IsActive).IsTrue();
        await Assert.That(snapshot.Session.SessionId).IsEqualTo(first.SessionId);
        await Assert.That(closed.IsActive).IsFalse();
        await Assert.That(second.SessionId).IsNotEqualTo(first.SessionId);
        await Assert.That(backend.DisposeCount).IsEqualTo(2);
    }

    [Test]
    public async Task RenderSourceAcquisitionForwardsOnlyWhileSessionIsActive()
    {
        using var files = new WorkspaceTestFiles();
        var expected = new InvalidOperationException("acquire");
        var backend = new RecordingWorkspaceBackend
        {
            AcquireRenderSourceError = expected,
        };
        await using var workspace = files.CreateWorkspace(backend);

        await Assert.That(async () => await workspace.AcquireRenderSourceAsync())
            .ThrowsExactly<InvalidOperationException>();
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        await Assert.That(async () => await workspace.AcquireRenderSourceAsync())
            .ThrowsExactly<InvalidOperationException>();
        await workspace.CloseAsync(Revision(session));
        await Assert.That(async () => await workspace.AcquireRenderSourceAsync())
            .ThrowsExactly<InvalidOperationException>();

        await Assert.That(backend.AcquireRenderSourceCount).IsEqualTo(1);
    }

    [Test]
    public async Task CloseRejectsStaleStageRevisionWithoutDisposingBackend()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");

        await Assert.That(
                async () => await workspace.CloseAsync(
                    new WorkspaceSessionRevision(
                        session.SessionId,
                        session.Generation,
                        session.StageRevision + 1)))
            .Throws<WorkspaceSessionRevisionException>();

        await Assert.That(backend.DisposeCount).IsEqualTo(0);
        await Assert.That((await workspace.GetStatusAsync()).IsActive).IsTrue();
    }

    [Test]
    public async Task LiveBackendStageRevisionInvalidatesCachedRevision()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        backend.StageRevision++;

        await Assert.That(
                async () => await workspace.CheckpointAsync(Revision(session)))
            .Throws<WorkspaceSessionRevisionException>();

        WorkspaceSessionInfo current = (await workspace.GetStatusAsync()).Session!;
        await Assert.That(current.StageRevision).IsEqualTo(backend.StageRevision);
    }

    [Test]
    public async Task RepeatedSameRevisionCheckpointsStopAtExactQuotaBeforeCreation()
    {
        using var files = new WorkspaceTestFiles(maximumCheckpointCount: 2);
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        var revision = Revision(session);

        _ = await workspace.CheckpointAsync(revision);
        _ = await workspace.CheckpointAsync(revision);
        await Assert.That(async () => await workspace.CheckpointAsync(revision))
            .ThrowsExactly<WorkspaceQuotaExceededException>();

        WorkspaceSessionManifest manifest = await workspace.GetManifestAsync(revision);
        await Assert.That(manifest.Checkpoints).Count().IsEqualTo(2);
        await Assert.That(backend.Events.Count(static item => item == "checkpoint"))
            .IsEqualTo(2);
    }

    [Test]
    public async Task InitializationFailureReleasesBackendAndLeavesNoActiveSession()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend
        {
            PersistFailuresRemaining = 1,
        };
        await using var workspace = files.CreateWorkspace(backend);

        await Assert.That(async () => await workspace.StartAsync("scene.usda"))
            .ThrowsExactly<IOException>();

        await Assert.That((await workspace.GetStatusAsync()).IsActive).IsFalse();
        await Assert.That(backend.DisposeCount).IsEqualTo(1);
    }

    private static WorkspaceSessionRevision Revision(WorkspaceSessionInfo session) =>
        new(session.SessionId, session.Generation, session.StageRevision);
}
