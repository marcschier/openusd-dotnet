// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp.Tests;

public sealed class WorkspacePathContainmentTests
{
    [Test]
    public async Task RejectsTraversalAndRootedSourcePaths()
    {
        using var files = new WorkspaceTestFiles();
        var containment = new WorkspacePathContainment(files.SourceRoot, files.OutputRoot);

        await Assert.That(() => containment.ResolveSourceFile(".." + Path.DirectorySeparatorChar + "outside.usda"))
            .Throws<WorkspacePathContainmentException>();
        await Assert.That(() => containment.ResolveSourceFile(files.SourcePath))
            .Throws<WorkspacePathContainmentException>();
    }

    [Test]
    public async Task ResolvesContainedSourceAndCreatesContainedOutput()
    {
        using var files = new WorkspaceTestFiles();
        var containment = new WorkspacePathContainment(files.SourceRoot, files.OutputRoot);

        string source = containment.ResolveSourceFile("scene.usda");
        string output = containment.CreateOutputDirectory("opaque");

        await Assert.That(source).IsEqualTo(files.SourcePath);
        await Assert.That(output).IsEqualTo(Path.Combine(files.OutputRoot, "opaque"));
        await Assert.That(Directory.Exists(output)).IsTrue();
    }

    [Test]
    public async Task RejectsSymbolicLinkSourceEscapeWhenLinksAreSupported()
    {
        using var files = new WorkspaceTestFiles();
        string outside = Path.Combine(
            Path.GetDirectoryName(files.SourceRoot)!,
            "outside.usda");
        string link = Path.Combine(files.SourceRoot, "linked.usda");
        File.WriteAllText(outside, "#usda 1.0");
        try
        {
            try
            {
                _ = File.CreateSymbolicLink(link, outside);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                PlatformNotSupportedException)
            {
                Skip.Test($"Symbolic-link creation is unavailable: {exception.Message}");
                throw;
            }

            var containment = new WorkspacePathContainment(
                files.SourceRoot,
                files.OutputRoot);
            await Assert.That(() => containment.ResolveSourceFile("linked.usda"))
                .Throws<WorkspacePathContainmentException>();
        }
        finally
        {
            File.Delete(link);
            File.Delete(outside);
        }
    }

    [Test]
    public async Task RejectsSymbolicLinkOutputEscapeWhenLinksAreSupported()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        WorkspaceSessionInfo session = await workspace.StartAsync("scene.usda");
        string outside = Path.Combine(
            Path.GetDirectoryName(session.OutputDirectory)!,
            "outside");
        string link = Path.Combine(session.OutputDirectory, "linked");
        Directory.CreateDirectory(outside);
        try
        {
            try
            {
                _ = Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                PlatformNotSupportedException)
            {
                Skip.Test($"Symbolic-link creation is unavailable: {exception.Message}");
                throw;
            }

            await Assert.That(
                    async () => await workspace.ExportFinalStageAsync(
                        new WorkspaceSessionRevision(
                            session.SessionId,
                            session.Generation,
                            session.StageRevision),
                        Path.Combine("linked", "nested", "final.usda")))
                .Throws<WorkspacePathContainmentException>();
            await Assert.That(backend.Events).DoesNotContain("export");
            await Assert.That(Directory.Exists(Path.Combine(outside, "nested"))).IsFalse();
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }
            if (Directory.Exists(outside))
            {
                Directory.Delete(outside);
            }
        }
    }

    [Test]
    public async Task RejectsNestedOutputRootReparseBeforeCreatingMissingSegments()
    {
        using var files = new WorkspaceTestFiles();
        string outside = Path.Combine(
            Path.GetDirectoryName(files.OutputRoot)!,
            "outside-root");
        string link = Path.Combine(files.OutputRoot, "linked-root");
        string configuredOutput = Path.Combine(link, "must-not-be-created");
        Directory.CreateDirectory(outside);
        try
        {
            try
            {
                _ = Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                PlatformNotSupportedException)
            {
                Skip.Test($"Symbolic-link creation is unavailable: {exception.Message}");
                throw;
            }

            await Assert.That(
                    () => new WorkspacePathContainment(
                        files.SourceRoot,
                        configuredOutput))
                .Throws<WorkspacePathContainmentException>();
            await Assert.That(Directory.Exists(Path.Combine(outside, "must-not-be-created")))
                .IsFalse();
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }
            if (Directory.Exists(outside))
            {
                Directory.Delete(outside);
            }
        }
    }
}
