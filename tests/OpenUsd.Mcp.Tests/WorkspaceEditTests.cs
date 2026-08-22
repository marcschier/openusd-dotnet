// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp.Tests;

public sealed class WorkspaceEditTests
{
    [Test]
    public async Task InvalidBatchIsRejectedBeforeCheckpointOrEdit()
    {
        using var files = new WorkspaceTestFiles(maximumBatchOperationCount: 2);
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        backend.Events.Clear();

        async Task submitInvalidBatch()
        {
            _ = await workspace.EditAsync(
                new WorkspaceSessionRevision(
                    session.SessionId,
                    session.Generation,
                    session.StageRevision),
                new WorkspaceEditBatch(
                [
                    new DefinePrimWorkspaceEdit("/World"),
                    new SetDoubleWorkspaceEdit("/World", "bad::name", 1)
                ]));
        }

        await Assert.That(submitInvalidBatch).Throws<ArgumentException>();
        await Assert.That(backend.Events).IsEmpty();
    }

    [Test]
    public async Task OversizedBatchIsRejectedBeforeBackend()
    {
        using var files = new WorkspaceTestFiles(maximumBatchOperationCount: 1);
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        backend.Events.Clear();

        await Assert.That(
                async () => await workspace.EditAsync(
                    new WorkspaceSessionRevision(
                        session.SessionId,
                        session.Generation,
                        session.StageRevision),
                    new WorkspaceEditBatch(
                    [
                        new DefinePrimWorkspaceEdit("/A"),
                        new DefinePrimWorkspaceEdit("/B")
                    ])))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(backend.Events).IsEmpty();
    }

    [Test]
    public async Task CheckpointAlwaysPrecedesMutation()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        backend.Events.Clear();

        _ = await workspace.EditAsync(
            new WorkspaceSessionRevision(
                session.SessionId,
                session.Generation,
                session.StageRevision),
            new WorkspaceEditBatch([new SetDoubleWorkspaceEdit("/World", "size", 2)]));

        await Assert.That(string.Join(",", backend.Events))
            .IsEqualTo("get-stage-revision,checkpoint,apply,persist");
    }

    [Test]
    public async Task DeactivationAndOverlayClearValidateTheirTargets()
    {
        var deactivate = new SetActiveWorkspaceEdit("/World/Model", false);
        var reactivate = new SetActiveWorkspaceEdit("/World/Model", true);
        var clear = new ClearOverlayAttributeWorkspaceEdit("/World/Model", "inputs:roughness");

        await Assert.That(deactivate.Active).IsFalse();
        await Assert.That(reactivate.Active).IsTrue();
        await Assert.That(() => new WorkspaceEditBatch([deactivate, clear]).Validate(2))
            .ThrowsNothing();
        await Assert.That(
                () => new WorkspaceEditBatch(
                    [new SetActiveWorkspaceEdit("World", false)]).Validate(1))
            .Throws<ArgumentException>();
        await Assert.That(
                () => new WorkspaceEditBatch(
                    [new ClearOverlayAttributeWorkspaceEdit("/World", "bad::name")]).Validate(1))
            .Throws<ArgumentException>();
    }
}
