// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;

namespace OpenUsd.Package.Tests;

public sealed class McpDocumentationTests
{
    [Test]
    public async Task CopilotInstallAndManagementCommandsStayDocumented()
    {
        string root = FindRepositoryRoot();
        string mcp = await File.ReadAllTextAsync(Path.Combine(root, "docs", "mcp.md"));

        string[] requiredText =
        [
            "dotnet tool install --global OpenUsd.Mcp.Tool",
            "dotnet new tool-manifest --output .config",
            "dotnet tool install OpenUsd.Mcp.Tool",
            "copilot mcp add openusd -- openusd-mcp",
            "/mcp add",
            "**Server Type:** choose **STDIO**",
            "**Command:** `openusd-mcp`",
            "**Tools:** `*`",
            "<kbd>Ctrl</kbd>+<kbd>S</kbd>",
            "/mcp show openusd",
            "copilot mcp list",
            "copilot mcp get openusd",
            "copilot mcp remove openusd",
            "~/.copilot/mcp-config.json",
            "COPILOT_HOME",
            ".mcp.json",
            ".github/mcp.json",
        ];

        foreach (string expected in requiredText)
        {
            await Assert.That(mcp).Contains(expected);
        }
    }

    [Test]
    public async Task CopilotToolExamplesStayParseableAndConfined()
    {
        string root = FindRepositoryRoot();
        string[] exampleNames =
        [
            "openusd-mcp-tool-windows.json",
            "openusd-mcp-tool-linux.json",
            "openusd-mcp-tool-macos.json",
        ];

        foreach (string exampleName in exampleNames)
        {
            string path = Path.Combine(root, "docs", "examples", exampleName);
            await using FileStream stream = File.OpenRead(path);
            using JsonDocument document = await JsonDocument.ParseAsync(stream);
            JsonElement server = document.RootElement
                .GetProperty("mcpServers")
                .GetProperty("openusd");
            JsonElement environment = server.GetProperty("env");

            await Assert.That(server.GetProperty("type").GetString()).IsEqualTo("stdio");
            await Assert.That(server.GetProperty("command").GetString())
                .IsEqualTo("openusd-mcp");
            await Assert.That(server.GetProperty("args").GetArrayLength()).IsEqualTo(0);
            await Assert.That(server.GetProperty("tools")[0].GetString()).IsEqualTo("*");
            foreach (string variableName in new[]
                     {
                         "OPENUSD_MCP_SOURCE_ROOT",
                         "OPENUSD_MCP_OUTPUT_ROOT",
                         "OPENUSD_PLUGIN_PATH",
                         "OPENUSD_MCP_VIEWER_ROOT",
                     })
            {
                await Assert.That(environment.TryGetProperty(variableName, out _)).IsTrue();
                await Assert.That(environment.GetProperty(variableName).GetString() ?? "")
                    .StartsWith("<ABSOLUTE_");
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new InvalidOperationException("Repository root was not found.");
    }
}
