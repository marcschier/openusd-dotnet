// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace OpenUsd.Package.Tests;

public sealed class McpApplicationPackagingTests
{
    [Test]
    public async Task McpHostIsClassifiedAsAPackableToolApplication()
    {
        string root = FindRepositoryRoot();
        XDocument build = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        XElement applicationClassification = build
            .Descendants("IsApplicationProject")
            .Single(element =>
                element.Attribute("Condition")?.Value.Contains(
                    "MSBuildProjectName.EndsWith('.Mcp')",
                    StringComparison.Ordinal) == true);
        XElement nonProductionPackaging = build
            .Descendants("IsPackable")
            .Single(element =>
                element.Value.Equals("false", StringComparison.OrdinalIgnoreCase) &&
                element.Parent?.Attribute("Condition")?.Value.Contains(
                    "_IsProductionLibrary",
                    StringComparison.Ordinal) == true);
        XDocument project = XDocument.Load(Path.Combine(
            root,
            "src",
            "OpenUsd.Mcp",
            "OpenUsd.Mcp.csproj"));

        await Assert.That(applicationClassification.Value).IsEqualTo("true");
        await Assert.That(nonProductionPackaging.Parent?.Attribute("Condition")?.Value ?? "")
            .Contains("_IsProductionLibrary");
        await Assert.That(project.Descendants("OutputType").Single().Value).IsEqualTo("Exe");
        await Assert.That(project.Descendants("TargetFramework").Single().Value)
            .IsEqualTo("net10.0");
        await Assert.That(project.Descendants("IsPackable").Single().Value)
            .IsEqualTo("true");
        await Assert.That(project.Descendants("PackAsTool").Single().Value)
            .IsEqualTo("true");
        await Assert.That(project.Descendants("ToolCommandName").Single().Value)
            .IsEqualTo("openusd-mcp");
        await Assert.That(project.Descendants("PackageId").Single().Value)
            .IsEqualTo("OpenUsd.Mcp.Tool");
        await Assert.That(project.Descendants("IsAotCompatible")).IsEmpty();
        await Assert.That(project.Descendants("IsTrimmable")).IsEmpty();
    }

    [Test]
    public async Task McpBundleScriptStagesSupportedCoreAndImagingAssetsOnly()
    {
        string root = FindRepositoryRoot();
        string scriptPath = Path.Combine(root, "eng", "publish-mcp-bundle.ps1");
        string script = await File.ReadAllTextAsync(scriptPath);
        string runner = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "run-mcp.ps1"));

        await Assert.That(script).Contains(
            "[ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]");
        await Assert.That(script).Contains(
            "'src/OpenUsd.Mcp/OpenUsd.Mcp.csproj'");
        await Assert.That(script).Contains("native/install/$Rid");
        await Assert.That(script).Contains("native/install/shim/$Rid");
        await Assert.That(script).Contains("'openusd_dotnet.dll'");
        await Assert.That(script).Contains("'openusd_hydra.dll'");
        await Assert.That(script).Contains("'openusd_hdsilk.dll'");
        await Assert.That(script).Contains("'libopenusd_dotnet.so'");
        await Assert.That(script).Contains("'libopenusd_hydra.so'");
        await Assert.That(script).Contains("'libopenusd_hdsilk.so'");
        await Assert.That(script).Contains("'libopenusd_dotnet.dylib'");
        await Assert.That(script).Contains("'libopenusd_hydra.dylib'");
        await Assert.That(script).Contains("'libopenusd_hdsilk.dylib'");
        await Assert.That(script).Contains("'hdSilk/resources/plugInfo.json'");
        await Assert.That(script).Contains("'viewer-app'");
        await Assert.That(script).Contains("'cesium-runtime'");
        await Assert.That(script).DoesNotContain("OpenUsd.Viewer.App.csproj");
        await Assert.That(script).Contains(
            "$file.Name -like 'OpenUsd.Runtime.Cesium*'");
        await Assert.That(script).Contains(
            "(Join-Path $PSScriptRoot 'native-install-metadata.ps1')");
        await Assert.That(script).Contains("-Operation Verify");
        await Assert.That(script).Contains("-InstallRoot $installRoot");
        await Assert.That(runner).Contains(
            "[ValidateSet('win-x64', 'linux-x64', 'osx-arm64')]");
        await Assert.That(runner).Contains("--no-build");
        await Assert.That(runner).Contains(
            "(Join-Path $PSScriptRoot 'native-install-metadata.ps1')");
        await Assert.That(runner).Contains("-Operation Verify");
        await Assert.That(runner).Contains("-InstallRoot $installRoot");
        await Assert.That(runner).Contains("Out-Null");
        await Assert.That(runner).Contains("$env:OPENUSD_PLUGIN_PATH = $pluginRoot");
        await Assert.That(runner).Contains("$env:LD_LIBRARY_PATH");
        await Assert.That(runner).Contains("$env:DYLD_LIBRARY_PATH");
    }

    [Test]
    public async Task McpBundleScriptsPreflightBeforeMutationAndPreserveOutputs()
    {
        string root = FindRepositoryRoot();
        string testRoot = CreateTestRoot(root);
        try
        {
            string mockDotNet = await WriteMockDotNetAsync(testRoot);
            foreach (string rid in SupportedRids)
            {
                SyntheticLayout layout = CreateNativeLayout(root, testRoot, rid);
                File.Delete(layout.HdSilkPlugInfo);

                string publishOutput = Path.Combine(testRoot, $"publish-{rid}");
                string previousLayout = Path.Combine(publishOutput, "layout", rid);
                string previousArtifacts = Path.Combine(
                    publishOutput,
                    "artifacts",
                    rid);
                WriteFile(Path.Combine(previousLayout, "previous.txt"), "layout");
                WriteFile(Path.Combine(previousArtifacts, "previous.txt"), "artifacts");
                string publishInvocation = Path.Combine(
                    testRoot,
                    $"publish-invoked-{rid}.txt");

                ScriptResult publish = await RunScriptAsync(
                    root,
                    "publish-mcp-bundle.ps1",
                    [
                        "-Rid", rid,
                        "-OutputRoot", publishOutput,
                        "-NativeRoot", layout.NativeRoot,
                        "-ShimRoot", layout.ShimRoot,
                        "-DotNetCommand", mockDotNet,
                        "-NoArchive",
                    ],
                    new Dictionary<string, string?>
                    {
                        ["OPENUSD_MCP_TEST_INVOCATION"] = publishInvocation,
                    });

                await Assert.That(publish.ExitCode).IsNotEqualTo(0);
                await Assert.That(publish.Output).Contains("hdSilk plugin metadata");
                await Assert.That(File.Exists(
                    Path.Combine(previousLayout, "previous.txt"))).IsTrue();
                await Assert.That(File.Exists(
                    Path.Combine(previousArtifacts, "previous.txt"))).IsTrue();
                await Assert.That(File.Exists(publishInvocation)).IsFalse();
                await AssertNoTemporarySiblingAsync(
                    Path.GetDirectoryName(previousLayout)!,
                    rid);
                await AssertNoTemporarySiblingAsync(
                    Path.GetDirectoryName(previousArtifacts)!,
                    rid);

                string runtimeRoot = Path.Combine(testRoot, $"runtime-{rid}");
                WriteFile(Path.Combine(runtimeRoot, "previous.txt"), "runtime");
                string runInvocation = Path.Combine(
                    testRoot,
                    $"run-invoked-{rid}.txt");
                ScriptResult run = await RunScriptAsync(
                    root,
                    "run-mcp.ps1",
                    [
                        "-Rid", rid,
                        "-RuntimeRoot", runtimeRoot,
                        "-NativeRoot", layout.NativeRoot,
                        "-ShimRoot", layout.ShimRoot,
                        "-DotNetCommand", mockDotNet,
                    ],
                    new Dictionary<string, string?>
                    {
                        ["OPENUSD_MCP_TEST_INVOCATION"] = runInvocation,
                    });

                await Assert.That(run.ExitCode).IsNotEqualTo(0);
                await Assert.That(run.Output).Contains("hdSilk plugin metadata");
                await Assert.That(File.Exists(
                    Path.Combine(runtimeRoot, "previous.txt"))).IsTrue();
                await Assert.That(File.Exists(runInvocation)).IsFalse();
                await AssertNoTemporarySiblingAsync(
                    Path.GetDirectoryName(runtimeRoot)!,
                    Path.GetFileName(runtimeRoot));
            }
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Test]
    public async Task McpPublishStagesOffToTheSideAndReplacesOnlyValidatedOutput()
    {
        string root = FindRepositoryRoot();
        string testRoot = CreateTestRoot(root);
        try
        {
            const string rid = "win-x64";
            SyntheticLayout layout = CreateNativeLayout(root, testRoot, rid);
            await WriteNativeInstallMetadataAsync(root, layout, rid);
            string mockDotNet = await WriteMockDotNetAsync(testRoot);
            string outputRoot = Path.Combine(testRoot, "publish");
            string previousLayout = Path.Combine(outputRoot, "layout", rid);
            string previousArtifacts = Path.Combine(outputRoot, "artifacts", rid);
            WriteFile(Path.Combine(previousLayout, "previous.txt"), "layout");
            WriteFile(Path.Combine(previousArtifacts, "previous.txt"), "artifacts");

            ScriptResult publishFailure = await RunScriptAsync(
                root,
                "publish-mcp-bundle.ps1",
                [
                    "-Rid", rid,
                    "-OutputRoot", outputRoot,
                    "-NativeRoot", layout.NativeRoot,
                    "-ShimRoot", layout.ShimRoot,
                    "-DotNetCommand", mockDotNet,
                    "-NoArchive",
                ],
                new Dictionary<string, string?>
                {
                    ["OPENUSD_MCP_TEST_MODE"] = "publish-fail",
                });
            await Assert.That(publishFailure.ExitCode).IsNotEqualTo(0);
            await Assert.That(File.Exists(
                Path.Combine(previousLayout, "previous.txt"))).IsTrue();
            await Assert.That(File.Exists(
                Path.Combine(previousArtifacts, "previous.txt"))).IsTrue();

            ScriptResult validationFailure = await RunScriptAsync(
                root,
                "publish-mcp-bundle.ps1",
                [
                    "-Rid", rid,
                    "-OutputRoot", outputRoot,
                    "-NativeRoot", layout.NativeRoot,
                    "-ShimRoot", layout.ShimRoot,
                    "-DotNetCommand", mockDotNet,
                    "-NoArchive",
                ],
                new Dictionary<string, string?>
                {
                    ["OPENUSD_MCP_TEST_MODE"] = "publish-excluded",
                });
            await Assert.That(validationFailure.ExitCode).IsNotEqualTo(0);
            await Assert.That(validationFailure.Output).Contains("excluded asset");
            await Assert.That(File.Exists(
                Path.Combine(previousLayout, "previous.txt"))).IsTrue();
            await Assert.That(File.Exists(
                Path.Combine(previousArtifacts, "previous.txt"))).IsTrue();

            ScriptResult success = await RunScriptAsync(
                root,
                "publish-mcp-bundle.ps1",
                [
                    "-Rid", rid,
                    "-OutputRoot", outputRoot,
                    "-NativeRoot", layout.NativeRoot,
                    "-ShimRoot", layout.ShimRoot,
                    "-DotNetCommand", mockDotNet,
                    "-NoArchive",
                ],
                environment: null);
            await Assert.That(success.ExitCode).IsEqualTo(0);
            await Assert.That(success.Output).Contains("MCP_BUNDLE_PUBLISHED");
            await Assert.That(File.Exists(
                Path.Combine(previousLayout, "previous.txt"))).IsFalse();
            await Assert.That(File.Exists(
                Path.Combine(previousArtifacts, "previous.txt"))).IsFalse();
            await Assert.That(File.Exists(
                Path.Combine(previousLayout, "OpenUsd.Mcp.exe"))).IsTrue();
            await Assert.That(File.Exists(
                Path.Combine(previousLayout, "bin", "openusd_hdsilk.dll"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(
                previousLayout,
                "plugin",
                "usd",
                "hdSilk",
                "resources",
                "plugInfo.json"))).IsTrue();
            await Assert.That(Directory.EnumerateFiles(
                    previousLayout,
                    "OpenUsd.Viewer*",
                    SearchOption.AllDirectories))
                .IsEmpty();
            await Assert.That(File.Exists(Path.Combine(
                previousArtifacts,
                $"OpenUsd.Mcp.{rid}.manifest.json"))).IsTrue();
            await AssertNoTemporarySiblingAsync(
                Path.GetDirectoryName(previousLayout)!,
                rid);
            await AssertNoTemporarySiblingAsync(
                Path.GetDirectoryName(previousArtifacts)!,
                rid);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Test]
    public async Task McpSourceRuntimePreservesPreviousStageUntilValidationSucceeds()
    {
        string root = FindRepositoryRoot();
        string testRoot = CreateTestRoot(root);
        try
        {
            const string rid = "win-x64";
            SyntheticLayout layout = CreateNativeLayout(root, testRoot, rid);
            await WriteNativeInstallMetadataAsync(root, layout, rid);
            string mockDotNet = await WriteMockDotNetAsync(testRoot);
            string runtimeRoot = Path.Combine(testRoot, "runtime");
            WriteFile(Path.Combine(runtimeRoot, "previous.txt"), "runtime");
            WriteFile(
                Path.Combine(layout.ShimRoot, "bin", "OpenUsd.Viewer.App.exe"),
                "excluded");

            ScriptResult validationFailure = await RunScriptAsync(
                root,
                "run-mcp.ps1",
                [
                    "-Rid", rid,
                    "-RuntimeRoot", runtimeRoot,
                    "-NativeRoot", layout.NativeRoot,
                    "-ShimRoot", layout.ShimRoot,
                    "-DotNetCommand", mockDotNet,
                ],
                environment: null);
            await Assert.That(validationFailure.ExitCode).IsNotEqualTo(0);
            await Assert.That(validationFailure.Output).Contains("excluded asset");
            await Assert.That(File.Exists(
                Path.Combine(runtimeRoot, "previous.txt"))).IsTrue();
            File.Delete(Path.Combine(
                layout.ShimRoot,
                "bin",
                "OpenUsd.Viewer.App.exe"));

            string invocation = Path.Combine(testRoot, "run-invoked.txt");
            ScriptResult success = await RunScriptAsync(
                root,
                "run-mcp.ps1",
                [
                    "-Rid", rid,
                    "-RuntimeRoot", runtimeRoot,
                    "-NativeRoot", layout.NativeRoot,
                    "-ShimRoot", layout.ShimRoot,
                    "-DotNetCommand", mockDotNet,
                ],
                new Dictionary<string, string?>
                {
                    ["OPENUSD_MCP_TEST_INVOCATION"] = invocation,
                });
            await Assert.That(success.ExitCode).IsEqualTo(0);
            await Assert.That(File.Exists(
                Path.Combine(runtimeRoot, "previous.txt"))).IsFalse();
            await Assert.That(File.Exists(Path.Combine(
                runtimeRoot,
                "bin",
                "openusd_dotnet.dll"))).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(invocation))
                .IsEqualTo(Path.Combine(runtimeRoot, "plugin", "usd"));
            await AssertNoTemporarySiblingAsync(
                Path.GetDirectoryName(runtimeRoot)!,
                Path.GetFileName(runtimeRoot));
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Test]
    public async Task McpBundleScriptsRejectStaleNativeMetadataBeforeMutation()
    {
        string root = FindRepositoryRoot();
        string testRoot = CreateTestRoot(root);
        try
        {
            string mockDotNet = await WriteMockDotNetAsync(testRoot);
            foreach (string mismatch in new[]
                     {
                         "metadata",
                         "bounded-capability",
                         "binary-hash",
                     })
            {
                string caseRoot = Path.Combine(testRoot, mismatch);
                const string rid = "win-x64";
                SyntheticLayout layout = CreateNativeLayout(root, caseRoot, rid);
                await WriteNativeInstallMetadataAsync(root, layout, rid);
                ApplyNativeMetadataMismatch(layout, mismatch);

                foreach (string scriptName in new[]
                         {
                             "publish-mcp-bundle.ps1",
                             "run-mcp.ps1",
                         })
                {
                    string scriptRoot = Path.Combine(
                        caseRoot,
                        Path.GetFileNameWithoutExtension(scriptName));
                    string targetRoot = scriptName == "publish-mcp-bundle.ps1"
                        ? Path.Combine(scriptRoot, "output")
                        : Path.Combine(scriptRoot, "runtime");
                    string previousLayout = scriptName == "publish-mcp-bundle.ps1"
                        ? Path.Combine(targetRoot, "layout", rid)
                        : targetRoot;
                    WriteFile(
                        Path.Combine(previousLayout, "previous.txt"),
                        "preserve");
                    if (scriptName == "publish-mcp-bundle.ps1")
                    {
                        WriteFile(
                            Path.Combine(
                                targetRoot,
                                "artifacts",
                                rid,
                                "previous.txt"),
                            "preserve");
                    }

                    string invocation = Path.Combine(
                        scriptRoot,
                        "dotnet-invoked.txt");
                    List<string> arguments =
                    [
                        "-Rid", rid,
                        "-NativeRoot", layout.NativeRoot,
                        "-ShimRoot", layout.ShimRoot,
                        "-DotNetCommand", mockDotNet,
                    ];
                    if (scriptName == "publish-mcp-bundle.ps1")
                    {
                        arguments.AddRange(
                            ["-OutputRoot", targetRoot, "-NoArchive"]);
                    }
                    else
                    {
                        arguments.AddRange(["-RuntimeRoot", targetRoot]);
                    }

                    ScriptResult result = await RunScriptAsync(
                        root,
                        scriptName,
                        arguments.ToArray(),
                        new Dictionary<string, string?>
                        {
                            ["OPENUSD_MCP_TEST_INVOCATION"] = invocation,
                        });

                    await Assert.That(result.ExitCode).IsNotEqualTo(0);
                    await Assert.That(result.Output).Contains(
                        mismatch switch
                        {
                            "metadata" => "lockSha256",
                            "bounded-capability" => "shimDataCapabilities",
                            "binary-hash" => "dataLibrarySha256",
                            _ => throw new InvalidOperationException(),
                        });
                    await Assert.That(File.Exists(
                        Path.Combine(previousLayout, "previous.txt"))).IsTrue();
                    await Assert.That(File.Exists(invocation)).IsFalse();
                    await AssertNoTemporarySiblingAsync(
                        Path.GetDirectoryName(previousLayout)!,
                        Path.GetFileName(previousLayout));
                    if (scriptName == "publish-mcp-bundle.ps1")
                    {
                        string artifactRoot = Path.Combine(
                            targetRoot,
                            "artifacts",
                            rid);
                        await Assert.That(File.Exists(
                            Path.Combine(artifactRoot, "previous.txt"))).IsTrue();
                        await AssertNoTemporarySiblingAsync(
                            Path.GetDirectoryName(artifactRoot)!,
                            rid);
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Test]
    public async Task McpDocumentationAndClientExamplesStayLinkedAndParseable()
    {
        string root = FindRepositoryRoot();
        string readme = await File.ReadAllTextAsync(Path.Combine(root, "README.md"));
        string docsIndex = await File.ReadAllTextAsync(
            Path.Combine(root, "docs", "README.md"));
        string mcp = await File.ReadAllTextAsync(Path.Combine(root, "docs", "mcp.md"));

        await Assert.That(readme).Contains("[MCP server](docs/mcp.md)");
        await Assert.That(docsIndex).Contains("[MCP server](mcp.md)");
        string[] requiredToolNames =
        [
            "open_scene",
            "close_scene",
            "get_scene",
            "inspect_scene",
            "apply_edits",
            "checkpoint_scene",
            "rollback_scene",
            "render_preview",
            "analyze_scene",
            "apply_proposals",
            "finalize_scene",
            "present_scene",
        ];
        foreach (string toolName in requiredToolNames)
        {
            await Assert.That(mcp).Contains($"`{toolName}`");
        }

        foreach (string exampleName in new[]
                 {
                     "openusd-mcp-source.json",
                     "openusd-mcp-published.json",
                 })
        {
            string path = Path.Combine(root, "docs", "examples", exampleName);
            await using FileStream stream = File.OpenRead(path);
            using System.Text.Json.JsonDocument document =
                await System.Text.Json.JsonDocument.ParseAsync(stream);
            System.Text.Json.JsonElement server = document.RootElement
                .GetProperty("mcpServers")
                .GetProperty("openusd");
            await Assert.That(server.GetProperty("command").GetString() ?? "").IsNotEmpty();
            await Assert.That(server.GetProperty("args").ValueKind)
                .IsEqualTo(System.Text.Json.JsonValueKind.Array);
            await Assert.That(server.GetProperty("env")
                    .TryGetProperty("OPENUSD_MCP_SOURCE_ROOT", out _))
                .IsTrue();
            await Assert.That(server.GetProperty("env")
                    .TryGetProperty("OPENUSD_MCP_OUTPUT_ROOT", out _))
                .IsTrue();
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
            throw new InvalidOperationException("Could not locate repository root.");
    }

    private static readonly string[] SupportedRids =
    [
        "win-x64",
        "linux-x64",
        "osx-arm64",
    ];

    private static string CreateTestRoot(string repositoryRoot)
    {
        string root = Path.Combine(
            repositoryRoot,
            "artifacts",
            "mcp-bundle-atomicity-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static SyntheticLayout CreateNativeLayout(
        string repositoryRoot,
        string testRoot,
        string rid)
    {
        string installRoot = Path.Combine(testRoot, "install");
        string nativeRoot = Path.Combine(installRoot, rid);
        string shimRoot = Path.Combine(installRoot, "shim", rid);
        string nativeLibrary = rid switch
        {
            "win-x64" => Path.Combine(nativeRoot, "lib", "usd_ms.dll"),
            "linux-x64" => Path.Combine(nativeRoot, "lib", "libusd_ms.so"),
            "osx-arm64" => Path.Combine(nativeRoot, "lib", "libusd_ms.dylib"),
            _ => throw new ArgumentOutOfRangeException(nameof(rid)),
        };
        string shimDirectory = Path.Combine(
            shimRoot,
            rid == "win-x64" ? "bin" : "lib");
        string[] shimLibraries = rid switch
        {
            "win-x64" =>
            [
                "openusd_dotnet.dll",
                "openusd_hydra.dll",
                "openusd_hdsilk.dll",
                "openusd_storm_child.dll",
            ],
            "linux-x64" =>
            [
                "libopenusd_dotnet.so",
                "libopenusd_hydra.so",
                "libopenusd_hdsilk.so",
                "libopenusd_storm_child.so",
            ],
            "osx-arm64" =>
            [
                "libopenusd_dotnet.dylib",
                "libopenusd_hydra.dylib",
                "libopenusd_hdsilk.dylib",
                "libopenusd_storm_child.dylib",
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(rid)),
        };

        WriteFile(nativeLibrary, "native");
        foreach (string shimLibrary in shimLibraries)
        {
            WriteFile(Path.Combine(shimDirectory, shimLibrary), "native");
        }
        WriteFile(
            Path.Combine(nativeRoot, "lib", "usd", "plugInfo.json"),
            "{}");
        WriteFile(
            Path.Combine(nativeRoot, "plugin", "usd", "plugInfo.json"),
            "{}");
        WriteFile(Path.Combine(
            nativeRoot,
            "plugin",
            "usd",
            "hdStorm",
            "resources",
            "plugInfo.json"), "{}");
        string hdSilkPlugInfo = Path.Combine(
            shimRoot,
            "plugin",
            "usd",
            "hdSilk",
            "resources",
            "plugInfo.json");
        WriteFile(hdSilkPlugInfo, "{}");
        foreach ((string source, string installedName) in new[]
                 {
                     ("native/openusd_dotnet/include/openusd_dotnet.h",
                         "openusd_dotnet.h"),
                     ("native/openusd_hydra/include/openusd_hydra.h",
                         "openusd_hydra.h"),
                     ("native/hdSilk/include/openusd_hdsilk.h",
                         "openusd_hdsilk.h"),
                     ("native/include/openusd_render_camera.h",
                         "openusd_render_camera.h"),
                     ("native/include/openusd_render_lighting.h",
                         "openusd_render_lighting.h"),
                     ("native/include/openusd_render_pick.h",
                         "openusd_render_pick.h"),
                     ("native/openusd_storm_child/include/openusd_storm_child.h",
                         "openusd_storm_child.h"),
                 })
        {
            string destination = Path.Combine(
                shimRoot,
                "include",
                installedName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(
                Path.Combine(
                    repositoryRoot,
                    source.Replace('/', Path.DirectorySeparatorChar)),
                destination);
        }
        return new SyntheticLayout(
            installRoot,
            nativeRoot,
            shimRoot,
            hdSilkPlugInfo);
    }

    private static async Task WriteNativeInstallMetadataAsync(
        string repositoryRoot,
        SyntheticLayout layout,
        string rid)
    {
        ScriptResult result = await RunScriptAsync(
            repositoryRoot,
            "native-install-metadata.ps1",
            [
                "-Operation", "Write",
                "-Rid", rid,
                "-InstallRoot", layout.InstallRoot,
            ],
            environment: null);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Output);
    }

    private static void ApplyNativeMetadataMismatch(
        SyntheticLayout layout,
        string mismatch)
    {
        string metadataPath = Path.Combine(
            layout.NativeRoot,
            ".openusd-install-metadata.json");
        if (mismatch == "binary-hash")
        {
            File.AppendAllText(
                Path.Combine(
                    layout.ShimRoot,
                    "bin",
                    "openusd_dotnet.dll"),
                "changed");
            return;
        }

        JsonObject metadata = JsonNode
            .Parse(File.ReadAllText(metadataPath))!
            .AsObject();
        if (mismatch == "metadata")
        {
            metadata["lockSha256"] = "STALE";
        }
        else if (mismatch == "bounded-capability")
        {
            const ulong boundedInspection = 1UL << 18;
            ulong capabilities =
                metadata["shimDataCapabilities"]!.GetValue<ulong>();
            if ((capabilities & boundedInspection) == 0)
            {
                throw new InvalidOperationException(
                    "Synthetic metadata did not include bounded inspection.");
            }
            metadata["shimDataCapabilities"] =
                capabilities & ~boundedInspection;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(mismatch));
        }
        File.WriteAllText(
            metadataPath,
            metadata.ToJsonString(new() { WriteIndented = true }));
    }

    private static async Task<string> WriteMockDotNetAsync(string testRoot)
    {
        string path = Path.Combine(testRoot, "mock-dotnet.ps1");
        await File.WriteAllTextAsync(
            path,
            """
            $ErrorActionPreference = 'Stop'
            if (-not [string]::IsNullOrWhiteSpace(
                $env:OPENUSD_MCP_TEST_INVOCATION))
            {
                Set-Content `
                    -LiteralPath $env:OPENUSD_MCP_TEST_INVOCATION `
                    -Value 'invoked' `
                    -NoNewline
            }
            $verb = [string]$args[0]
            if ($verb -eq 'publish')
            {
                if ($env:OPENUSD_MCP_TEST_MODE -eq 'publish-fail')
                {
                    exit 17
                }
                $outputIndex = [Array]::IndexOf($args, '-o')
                $ridIndex = [Array]::IndexOf($args, '-r')
                $output = [string]$args[$outputIndex + 1]
                $rid = [string]$args[$ridIndex + 1]
                New-Item -ItemType Directory -Force -Path $output | Out-Null
                $executable = if ($rid -eq 'win-x64')
                {
                    'OpenUsd.Mcp.exe'
                }
                else
                {
                    'OpenUsd.Mcp'
                }
                Set-Content `
                    -LiteralPath (Join-Path $output $executable) `
                    -Value 'application'
                if ($env:OPENUSD_MCP_TEST_MODE -eq 'publish-excluded')
                {
                    Set-Content `
                        -LiteralPath (Join-Path $output 'OpenUsd.Viewer.dll') `
                        -Value 'excluded'
                }
                exit 0
            }
            if ($verb -eq 'run')
            {
                if (-not [string]::IsNullOrWhiteSpace(
                    $env:OPENUSD_MCP_TEST_INVOCATION))
                {
                    Set-Content `
                        -LiteralPath $env:OPENUSD_MCP_TEST_INVOCATION `
                        -Value $env:OPENUSD_PLUGIN_PATH `
                        -NoNewline
                }
                exit 0
            }
            exit 19
            """);
        return path;
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static async Task<ScriptResult> RunScriptAsync(
        string repositoryRoot,
        string scriptName,
        string[] arguments,
        IReadOnlyDictionary<string, string?>? environment)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(
            Path.Combine(repositoryRoot, "eng", scriptName));
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        if (environment is not null)
        {
            foreach ((string name, string? value) in environment)
            {
                process.StartInfo.Environment[name] = value;
            }
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start pwsh.");
        }
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ScriptResult(
            process.ExitCode,
            $"{await standardOutput}{await standardError}");
    }

    private static async Task AssertNoTemporarySiblingAsync(
        string parent,
        string targetName)
    {
        string[] temporaryEntries = Directory
            .EnumerateFileSystemEntries(parent, $".{targetName}.tmp.*")
            .ToArray();
        await Assert.That(temporaryEntries).IsEmpty();
    }

    private sealed record SyntheticLayout(
        string InstallRoot,
        string NativeRoot,
        string ShimRoot,
        string HdSilkPlugInfo);

    private sealed record ScriptResult(int ExitCode, string Output);
}
