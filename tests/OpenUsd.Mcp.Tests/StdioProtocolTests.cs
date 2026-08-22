// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Concurrent;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace OpenUsd.Mcp.Tests;

public sealed class StdioProtocolTests
{
    [Test]
    public async Task ActualStdioHostUsesProtocolOnlyStdoutAndLogsToStderr()
    {
        string root = FindRepositoryRoot();
        string testRoot = Path.Combine(
            AppContext.BaseDirectory,
            "stdio-tests",
            Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(testRoot, "source");
        string outputRoot = Path.Combine(testRoot, "output");
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(outputRoot);
        var standardError = new ConcurrentQueue<string>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var transport = new StdioClientTransport(
                new StdioClientTransportOptions
                {
                    Command = RepositoryDotnet(root),
                    Arguments =
                    [
                        Path.Combine(
                            root,
                            "src",
                            "OpenUsd.Mcp",
                            "bin",
                            "Release",
                            "net10.0",
                            "OpenUsd.Mcp.dll"),
                    ],
                    Name = "OpenUsd.Mcp stdio validation",
                    WorkingDirectory = root,
                    InheritEnvironmentVariables = true,
                    EnvironmentVariables = new Dictionary<string, string?>
                    {
                        ["OPENUSD_MCP_SOURCE_ROOT"] = sourceRoot,
                        ["OPENUSD_MCP_OUTPUT_ROOT"] = outputRoot,
                    },
                    StandardErrorLines = line => standardError.Enqueue(line),
                });
            await using McpClient client = await McpClient.CreateAsync(
                transport,
                cancellationToken: cancellation.Token);

            IList<McpClientTool> tools = await client.ListToolsAsync(
                cancellationToken: cancellation.Token);
            IList<McpClientResource> resources = await client.ListResourcesAsync(
                cancellationToken: cancellation.Token);
            IList<McpClientResourceTemplate> templates =
                await client.ListResourceTemplatesAsync(
                    cancellationToken: cancellation.Token);
            CallToolResult noSession = await client.CallToolAsync(
                "get_scene",
                new Dictionary<string, object?>
                {
                    ["request"] = new Dictionary<string, object?>
                    {
                        ["sessionId"] = "stdio-smoke",
                        ["generation"] = 0,
                        ["stageRevision"] = 0,
                    },
                },
                cancellationToken: cancellation.Token);

            await Assert.That(tools.Count).IsEqualTo(12);
            await Assert.That(resources).IsEmpty();
            await Assert.That(templates).Count().IsEqualTo(1);
            await Assert.That(noSession.IsError).IsTrue();
            await Assert.That(noSession.StructuredContent!.Value
                    .GetProperty("error")
                    .GetProperty("code")
                    .GetString())
                .IsEqualTo("no_session");
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }

        await Assert.That(standardError).IsNotEmpty()
            .Because(
                "the official stdio transport rejects non-JSON stdout frames, while host logs " +
                "must be observable only through its stderr callback");
    }

    private static string RepositoryDotnet(string root)
    {
        string executable = Path.Combine(
            root,
            ".dotnet",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(executable) ? executable : "dotnet";
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
