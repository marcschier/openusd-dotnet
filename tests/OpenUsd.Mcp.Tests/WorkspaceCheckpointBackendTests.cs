// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp.Tests;

public sealed class WorkspaceCheckpointBackendTests
{
    [Test]
    public async Task CheckpointPathRequiresCanonicalSessionCheckpointName()
    {
        using var files = new WorkspaceTestFiles();
        string outputDirectory = Path.Combine(files.OutputRoot, "session");
        Directory.CreateDirectory(outputDirectory);
        var context = new WorkspaceSessionBackendContext(
            "session",
            files.SourcePath,
            outputDirectory,
            Path.Combine(outputDirectory, "overlay.usda"));
        string checkpointId = Guid.NewGuid().ToString("N");
        var checkpoint = new WorkspaceCheckpoint(
            checkpointId,
            0,
            Path.Combine("checkpoints", "..", "manifest.json"),
            DateTimeOffset.UtcNow);

        await Assert.That(
                () => OpenUsdWorkspaceSessionBackend.ResolveCheckpointPath(
                    context,
                    checkpoint))
            .Throws<WorkspacePathContainmentException>();
    }
}
