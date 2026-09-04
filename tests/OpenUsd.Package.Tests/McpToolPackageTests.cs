// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;

namespace OpenUsd.Package.Tests;

[NotInParallel]
public sealed class McpToolPackageTests
{
    private const string PackageId = "OpenUsd.Mcp.Tool";
    private const string ToolRoot = "tools/net10.0/any/";

    private static readonly string[] RequiredManagedAssemblies =
    [
        "OpenUsd.Mcp.dll",
        "OpenUsd.dll",
        "OpenUsd.Interop.dll",
        "OpenUsd.Rendering.dll",
        "OpenUsd.Rendering.Silk.dll",
        "OpenUsd.Rendering.Silk.D3D12.dll",
        "OpenUsd.Rendering.Silk.Metal.dll",
        "OpenUsd.Rendering.Silk.Vulkan.dll",
        "Microsoft.Extensions.Hosting.dll",
        "ModelContextProtocol.dll",
    ];

    [Test]
    public async Task McpToolPackageMetadataDocumentsExternalNativeRuntime()
    {
        string root = FindRepositoryRoot();
        XDocument project = XDocument.Load(Path.Combine(
            root,
            "src",
            "OpenUsd.Mcp",
            "OpenUsd.Mcp.csproj"));
        string readme = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Mcp",
            "README.md"));

        await Assert.That(project.Descendants("PackageId").Single().Value)
            .IsEqualTo(PackageId);
        await Assert.That(project.Descendants("PackageReadmeFile").Single().Value)
            .IsEqualTo("README.md");
        await Assert.That(project.Descendants("PackageLicenseExpression").Single().Value)
            .IsEqualTo("MIT");
        await Assert.That(project.Descendants("RepositoryUrl").Single().Value)
            .IsEqualTo("https://github.com/marcschier/openusd-dotnet");
        await Assert.That(project.Descendants("IncludeSymbols").Single().Value)
            .IsEqualTo("true");
        await Assert.That(project.Descendants("SymbolPackageFormat").Single().Value)
            .IsEqualTo("snupkg");
        await Assert.That(project.Descendants("ProjectReference").Count())
            .IsEqualTo(7);
        await Assert.That(project
                .Descendants("PackageReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => value?.StartsWith(
                    "OpenUsd.Runtime.",
                    StringComparison.Ordinal) == true))
            .IsEmpty();

        foreach (string requiredText in new[]
                 {
                     "does not contain a local OpenUSD build",
                     "OPENUSD_PLUGIN_PATH",
                     "LD_LIBRARY_PATH",
                     "DYLD_LIBRARY_PATH",
                     "eng/run-mcp.ps1",
                     "eng/publish-mcp-bundle.ps1",
                 })
        {
            await Assert.That(readme).Contains(requiredText);
        }
    }

    [Test]
    public async Task ReleasePacksAndPromotesMcpToolDeliberatelyOnce()
    {
        string root = FindRepositoryRoot();
        string pack = await File.ReadAllTextAsync(Path.Combine(
            root,
            "eng",
            "pack-packages.ps1"));
        string release = await File.ReadAllTextAsync(Path.Combine(
            root,
            ".github",
            "workflows",
            "release.yml"));
        string nuget = await File.ReadAllTextAsync(Path.Combine(
            root,
            ".github",
            "workflows",
            "nuget.yml"));

        await Assert.That(CountOccurrences(release, "-Scope tool"))
            .IsEqualTo(1);
        await Assert.That(release).Contains("name: Pack the MCP dotnet tool");
        await Assert.That(release).Contains("if: matrix.rid == 'linux-x64'");
        await Assert.That(pack).Contains("[ValidateSet('all', 'managed', 'metal', 'runtime', 'tool')]");
        await Assert.That(pack).Contains("$_ -ne $toolPackage");
        await Assert.That(pack).Contains("$published = @($toolPackage)");
        await Assert.That(pack).Contains($"'{PackageId}'");
        await Assert.That(nuget).Contains("./eng/pack-packages.ps1 -ListPublished");
    }

    [Test]
    public async Task PackedMcpToolIsInstallableManagedOnlyPackage()
    {
        string root = FindRepositoryRoot();
        string testRoot = Path.Combine(
            root,
            "artifacts",
            "mcp-tool-package-tests",
            Guid.NewGuid().ToString("N"));
        string packageRoot = Path.Combine(testRoot, "packages");
        Directory.CreateDirectory(packageRoot);
        string publishRoot = Path.Combine(
            root,
            "src",
            "OpenUsd.Mcp",
            "bin",
            "Release",
            "net10.0",
            "publish");
        string[] staleNativeAssets =
        [
            Path.Combine(publishRoot, "openusd_dotnet.dll"),
            Path.Combine(publishRoot, "mesh.metallib"),
        ];
        List<string> createdStaleNativeAssets = [];

        try
        {
            foreach (string staleNativeAsset in staleNativeAssets)
            {
                if (File.Exists(staleNativeAsset))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(staleNativeAsset)!);
                await File.WriteAllTextAsync(
                    staleNativeAsset,
                    "stale local native install");
                createdStaleNativeAssets.Add(staleNativeAsset);
            }

            ProcessResult pack = await RunDotNetAsync(
                root,
                [
                    "pack",
                    Path.Combine(root, "src", "OpenUsd.Mcp", "OpenUsd.Mcp.csproj"),
                    "--configuration", "Release",
                    "--output", packageRoot,
                    "-p:PublicRelease=true",
                ]);
            await Assert.That(pack.ExitCode).IsEqualTo(0).Because(pack.Output);

            string expectedVersion = ReadRepositoryVersion(root);
            string[] packages = Directory.GetFiles(
                packageRoot,
                $"{PackageId}.*.nupkg");
            string[] symbols = Directory.GetFiles(
                packageRoot,
                $"{PackageId}.*.snupkg");
            await Assert.That(packages.Length).IsEqualTo(1).Because(pack.Output);
            await Assert.That(symbols.Length).IsEqualTo(1).Because(pack.Output);
            await Assert.That(Path.GetFileName(packages[0]))
                .IsEqualTo($"{PackageId}.{expectedVersion}.nupkg");
            await Assert.That(Path.GetFileName(symbols[0]))
                .IsEqualTo($"{PackageId}.{expectedVersion}.snupkg");

            using ZipArchive package = ZipFile.OpenRead(packages[0]);
            HashSet<string> entries = package.Entries
                .Select(entry => NormalizeEntry(entry.FullName))
                .ToHashSet(StringComparer.Ordinal);

            await Assert.That(entries).Contains("README.md");
            await Assert.That(entries).Contains(
                $"{ToolRoot}DotnetToolSettings.xml");
            await Assert.That(entries).Contains($"{ToolRoot}OpenUsd.Mcp.deps.json");
            await Assert.That(entries).Contains($"{ToolRoot}OpenUsd.Mcp.runtimeconfig.json");
            foreach (string assembly in RequiredManagedAssemblies)
            {
                await Assert.That(entries).Contains($"{ToolRoot}{assembly}");
            }

            string[] toolFrameworkRoots = entries
                .Where(entry => entry.StartsWith("tools/", StringComparison.Ordinal))
                .Select(entry => string.Join('/', entry.Split('/').Take(3)) + "/")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            await Assert.That(toolFrameworkRoots)
                .IsEquivalentTo([ToolRoot.TrimEnd('/') + "/"]);

            await AssertToolSettingsAsync(package);
            await AssertNuspecAsync(package, expectedVersion);
            await AssertManagedDependencyClosureAsync(package, entries);
            await AssertNoNativeAssetsAsync(package);
            await AssertPortablePdbPathsAsync(package);
            await AssertSymbolPackageAsync(symbols[0]);

            string installedRoot = Path.Combine(testRoot, "installed");
            string configPath = Path.Combine(testRoot, "NuGet.config");
            await File.WriteAllTextAsync(
                configPath,
                CreateLocalNuGetConfig(packageRoot));
            ProcessResult install = await RunDotNetAsync(
                root,
                [
                    "tool", "install", PackageId,
                    "--tool-path", installedRoot,
                    "--version", expectedVersion,
                    "--configfile", configPath,
                ]);
            await Assert.That(install.ExitCode).IsEqualTo(0).Because(install.Output);

            ProcessResult list = await RunDotNetAsync(
                root,
                ["tool", "list", "--tool-path", installedRoot]);
            await Assert.That(list.ExitCode).IsEqualTo(0).Because(list.Output);
            await Assert.That(list.Output).Contains(PackageId.ToLowerInvariant());
            await Assert.That(list.Output).Contains(expectedVersion);
            await Assert.That(File.Exists(Path.Combine(
                    installedRoot,
                    OperatingSystem.IsWindows() ? "openusd-mcp.exe" : "openusd-mcp")))
                .IsTrue();
        }
        finally
        {
            foreach (string staleNativeAsset in createdStaleNativeAssets)
            {
                File.Delete(staleNativeAsset);
            }
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static async Task AssertToolSettingsAsync(ZipArchive package)
    {
        XDocument settings = XDocument.Parse(ReadEntry(
            package,
            $"{ToolRoot}DotnetToolSettings.xml"));
        XElement command = settings.Descendants("Command").Single();

        await Assert.That(command.Attribute("Name")?.Value)
            .IsEqualTo("openusd-mcp");
        await Assert.That(command.Attribute("EntryPoint")?.Value)
            .IsEqualTo("OpenUsd.Mcp.dll");
        await Assert.That(command.Attribute("Runner")?.Value)
            .IsEqualTo("dotnet");
    }

    private static async Task AssertNuspecAsync(
        ZipArchive package,
        string expectedVersion)
    {
        ZipArchiveEntry nuspecEntry = package.Entries.Single(
            entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        XDocument nuspec = XDocument.Parse(ReadEntry(package, nuspecEntry.FullName));
        XNamespace ns = nuspec.Root!.Name.Namespace;
        XElement metadata = nuspec.Root.Element(ns + "metadata")!;

        await Assert.That(metadata.Element(ns + "id")?.Value)
            .IsEqualTo(PackageId);
        await Assert.That(metadata.Element(ns + "version")?.Value)
            .IsEqualTo(expectedVersion);
        await Assert.That(metadata.Element(ns + "readme")?.Value)
            .IsEqualTo("README.md");
        await Assert.That(metadata.Element(ns + "license")?.Value)
            .IsEqualTo("MIT");
        await Assert.That(metadata
                .Element(ns + "packageTypes")?
                .Elements(ns + "packageType")
                .Single()
                .Attribute("name")?
                .Value)
            .IsEqualTo("DotnetTool");
        await Assert.That(metadata.Element(ns + "dependencies")).IsNull();

        XElement repository = metadata.Element(ns + "repository")!;
        await Assert.That(repository.Attribute("type")?.Value).IsEqualTo("git");
        await Assert.That(repository.Attribute("url")?.Value)
            .IsEqualTo("https://github.com/marcschier/openusd-dotnet");
        await Assert.That(repository.Attribute("commit")?.Value ?? "")
            .Matches("^[0-9a-f]{40}$");

        string packagedReadme = ReadEntry(package, "README.md");
        string root = FindRepositoryRoot();
        string sourceReadme = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Mcp",
            "README.md"));
        await Assert.That(packagedReadme).IsEqualTo(sourceReadme);
    }

    private static async Task AssertManagedDependencyClosureAsync(
        ZipArchive package,
        HashSet<string> entries)
    {
        using JsonDocument document = JsonDocument.Parse(ReadEntry(
            package,
            $"{ToolRoot}OpenUsd.Mcp.deps.json"));
        JsonElement targets = document.RootElement.GetProperty("targets");
        JsonProperty target = targets.EnumerateObject().Single();
        await Assert.That(target.Name).IsEqualTo(".NETCoreApp,Version=v10.0");

        int managedAssets = 0;
        int intentionallyExternalNativeAssets = 0;
        foreach (JsonProperty library in target.Value.EnumerateObject())
        {
            if (library.Value.TryGetProperty("runtime", out JsonElement runtime))
            {
                foreach (JsonProperty asset in runtime.EnumerateObject())
                {
                    managedAssets++;
                    await Assert.That(entries).Contains(
                        $"{ToolRoot}{Path.GetFileName(asset.Name)}");
                }
            }

            if (!library.Value.TryGetProperty(
                    "runtimeTargets",
                    out JsonElement runtimeTargets))
            {
                continue;
            }

            foreach (JsonProperty asset in runtimeTargets.EnumerateObject())
            {
                string assetType = asset.Value.GetProperty("assetType").GetString() ?? "";
                string packagePath = $"{ToolRoot}{NormalizeEntry(asset.Name)}";
                if (assetType == "runtime")
                {
                    managedAssets++;
                    await Assert.That(entries).Contains(packagePath);
                }
                else if (assetType == "native")
                {
                    intentionallyExternalNativeAssets++;
                    await Assert.That(entries).DoesNotContain(packagePath);
                }
            }
        }

        await Assert.That(managedAssets).IsGreaterThan(40);
        await Assert.That(intentionallyExternalNativeAssets).IsGreaterThan(10);
    }

    private static async Task AssertNoNativeAssetsAsync(ZipArchive package)
    {
        foreach (ZipArchiveEntry entry in package.Entries)
        {
            string path = NormalizeEntry(entry.FullName);
            string extension = Path.GetExtension(path);
            await Assert.That(path.Contains(
                    "/native/",
                    StringComparison.OrdinalIgnoreCase))
                .IsFalse()
                .Because(path);
            await Assert.That(extension is ".exe" or ".so" or ".dylib" or ".metallib")
                .IsFalse()
                .Because(path);

            if (!extension.Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using Stream stream = entry.Open();
            using MemoryStream image = new();
            await stream.CopyToAsync(image);
            image.Position = 0;
            using PEReader reader = new(image);
            await Assert.That(reader.HasMetadata)
                .IsTrue()
                .Because($"{path} must be a managed assembly");
        }
    }

    private static async Task AssertSymbolPackageAsync(string symbolPath)
    {
        using ZipArchive symbols = ZipFile.OpenRead(symbolPath);
        HashSet<string> entries = symbols.Entries
            .Select(entry => NormalizeEntry(entry.FullName))
            .ToHashSet(StringComparer.Ordinal);
        foreach (string assembly in RequiredManagedAssemblies
                     .Where(name => name.StartsWith("OpenUsd.", StringComparison.Ordinal)))
        {
            await Assert.That(entries).Contains(
                $"{ToolRoot}{Path.ChangeExtension(assembly, ".pdb")}");
        }

        await AssertPortablePdbPathsAsync(symbols);
    }

    private static async Task AssertPortablePdbPathsAsync(ZipArchive archive)
    {
        foreach (ZipArchiveEntry entry in archive.Entries.Where(
                     item => item.FullName.EndsWith(
                         ".pdb",
                         StringComparison.OrdinalIgnoreCase)))
        {
            using Stream stream = entry.Open();
            using MemoryStream image = new();
            await stream.CopyToAsync(image);
            image.Position = 0;
            using MetadataReaderProvider provider =
                MetadataReaderProvider.FromPortablePdbStream(
                    image,
                    MetadataStreamOptions.LeaveOpen);
            MetadataReader reader = provider.GetMetadataReader();
            foreach (DocumentHandle handle in reader.Documents)
            {
                string document = reader.GetString(reader.GetDocument(handle).Name);
                string normalized = NormalizeEntry(document);
                bool deterministicRoot = normalized.StartsWith(
                    "/_/",
                    StringComparison.Ordinal);
                bool windowsAbsolute =
                    normalized.Length >= 3 &&
                    char.IsAsciiLetter(normalized[0]) &&
                    normalized[1] == ':' &&
                    normalized[2] == '/';
                bool unixAbsolute =
                    normalized.StartsWith('/') &&
                    !deterministicRoot;

                await Assert.That(windowsAbsolute || unixAbsolute)
                    .IsFalse()
                    .Because($"{entry.FullName} contains local source path '{document}'");
            }
        }
    }

    private static string ReadRepositoryVersion(string repositoryRoot)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repositoryRoot, "version.json")));
        return document.RootElement.GetProperty("version").GetString() ??
            throw new InvalidOperationException("version.json has no version.");
    }

    private static string CreateLocalNuGetConfig(string packageRoot)
    {
        string escapedRoot =
            System.Security.SecurityElement.Escape(packageRoot) ??
            throw new InvalidOperationException("Could not encode package source path.");
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="mcp-tool-test" value="{escapedRoot}" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="mcp-tool-test">
                  <package pattern="{PackageId}" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """;
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        ZipArchiveEntry entry = archive.GetEntry(path) ??
            throw new InvalidOperationException($"Package entry '{path}' was not found.");
        using StreamReader reader = new(entry.Open());
        return reader.ReadToEnd();
    }

    private static string NormalizeEntry(string path) =>
        path.Replace('\\', '/');

    private static int CountOccurrences(string value, string substring) =>
        value.Split(substring, StringSplitOptions.None).Length - 1;

    private static async Task<ProcessResult> RunDotNetAsync(
        string workingDirectory,
        string[] arguments)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            throw new InvalidOperationException("Could not start dotnet.");
        }
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(5));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException exception)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"dotnet {string.Join(' ', arguments)} timed out.",
                exception);
        }

        return new ProcessResult(
            process.ExitCode,
            $"{await standardOutput}{await standardError}");
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

    private sealed record ProcessResult(int ExitCode, string Output);
}
