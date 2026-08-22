// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp.Tests;

[NotInParallel]
public sealed class McpHostConfigurationTests
{
    [Test]
    public async Task ViewerPathDefaultsBeneathConfiguredViewerRoot()
    {
        string root = Path.Combine(
            AppContext.BaseDirectory,
            "configuration-tests",
            Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(root, "source");
        string outputRoot = Path.Combine(root, "output");
        string viewerRoot = Path.Combine(root, "viewer");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(viewerRoot);
        string? originalSource = Environment.GetEnvironmentVariable(
            "OPENUSD_MCP_SOURCE_ROOT");
        string? originalOutput = Environment.GetEnvironmentVariable(
            "OPENUSD_MCP_OUTPUT_ROOT");
        string? originalViewerRoot = Environment.GetEnvironmentVariable(
            "OPENUSD_MCP_VIEWER_ROOT");
        string? originalViewerPath = Environment.GetEnvironmentVariable(
            "OPENUSD_MCP_VIEWER_PATH");
        string? originalArtifactStoreBytes = Environment.GetEnvironmentVariable(
            "OPENUSD_MCP_MAX_ARTIFACT_STORE_BYTES");
        string? originalArtifactReadBytes = Environment.GetEnvironmentVariable(
            "OPENUSD_MCP_MAX_ARTIFACT_READ_BYTES");
        try
        {
            Environment.SetEnvironmentVariable("OPENUSD_MCP_SOURCE_ROOT", sourceRoot);
            Environment.SetEnvironmentVariable("OPENUSD_MCP_OUTPUT_ROOT", outputRoot);
            Environment.SetEnvironmentVariable("OPENUSD_MCP_VIEWER_ROOT", viewerRoot);
            Environment.SetEnvironmentVariable("OPENUSD_MCP_VIEWER_PATH", null);
            Environment.SetEnvironmentVariable(
                "OPENUSD_MCP_MAX_ARTIFACT_STORE_BYTES",
                "12345");
            Environment.SetEnvironmentVariable(
                "OPENUSD_MCP_MAX_ARTIFACT_READ_BYTES",
                "6789");

            OpenUsdMcpApplicationOptions options = McpHostConfiguration.LoadOptions();

            await Assert.That(options.ViewerExecutableRoot)
                .IsEqualTo(Path.GetFullPath(viewerRoot));
            await Assert.That(options.ViewerExecutablePath)
                .IsEqualTo(Path.Combine(
                    Path.GetFullPath(viewerRoot),
                    OperatingSystem.IsWindows()
                        ? "OpenUsd.Viewer.App.exe"
                        : "OpenUsd.Viewer.App"));
            await Assert.That(options.ArtifactStore!.MaximumTotalBytes)
                .IsEqualTo(12345);
            await Assert.That(options.ArtifactStore.MaximumReadResponseBytes)
                .IsEqualTo(6789);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENUSD_MCP_SOURCE_ROOT", originalSource);
            Environment.SetEnvironmentVariable("OPENUSD_MCP_OUTPUT_ROOT", originalOutput);
            Environment.SetEnvironmentVariable(
                "OPENUSD_MCP_VIEWER_ROOT",
                originalViewerRoot);
            Environment.SetEnvironmentVariable(
                "OPENUSD_MCP_VIEWER_PATH",
                originalViewerPath);
            Environment.SetEnvironmentVariable(
                "OPENUSD_MCP_MAX_ARTIFACT_STORE_BYTES",
                originalArtifactStoreBytes);
            Environment.SetEnvironmentVariable(
                "OPENUSD_MCP_MAX_ARTIFACT_READ_BYTES",
                originalArtifactReadBytes);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
