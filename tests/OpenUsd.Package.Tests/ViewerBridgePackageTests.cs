// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;

namespace OpenUsd.Package.Tests;

/// <summary>
/// Proves the Viewer's bridge integration is genuinely optional: the base
/// <c>OpenUsd.Viewer</c> package carries no transport, the integration package is the only
/// assembly that references both sides, and it packs for every supported framework under the
/// same trim and NativeAOT analysis the rest of the published libraries are held to.
/// </summary>
[NotInParallel]
public sealed class ViewerBridgePackageTests
{
    private const string IntegrationPackageId = "OpenUsd.Viewer.Bridge.Grpc";

    private static readonly string[] SupportedFrameworks = ["net8.0", "net9.0", "net10.0"];

    [Test]
    public async Task TheViewerPackageCarriesNoTransportOrNvidiaDependency()
    {
        string repositoryRoot = FindRepositoryRoot();
        XDocument viewer = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "OpenUsd.Viewer",
            "OpenUsd.Viewer.csproj"));

        foreach (string reference in viewer
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty))
        {
            await Assert.That(reference).DoesNotContain("Grpc");
            await Assert.That(reference).DoesNotContain("Protobuf");
            await Assert.That(reference.Contains("omni", StringComparison.OrdinalIgnoreCase))
                .IsFalse();
            await Assert.That(reference.Contains("nvidia", StringComparison.OrdinalIgnoreCase))
                .IsFalse();
        }

        foreach (string reference in viewer
            .Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(
                element.Attribute("Include")?.Value) ?? string.Empty))
        {
            await Assert.That(reference).DoesNotContain("Bridge");
        }
    }

    [Test]
    public async Task TheIntegrationPackageIsTheOnlyProjectThatReferencesBothSides()
    {
        string repositoryRoot = FindRepositoryRoot();
        XDocument integration = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            IntegrationPackageId,
            $"{IntegrationPackageId}.csproj"));

        string[] projectReferences = [.. integration
            .Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(
                element.Attribute("Include")?.Value) ?? string.Empty)];

        await Assert.That(projectReferences).IsEquivalentTo(
            ["OpenUsd.Viewer", "OpenUsd.Bridge.Grpc"]);

        // Nothing may reference this package back: the direction is what keeps the base
        // Viewer free of a transport, and a single reverse reference would erase it.
        foreach (string projectPath in Directory.GetFiles(
            Path.Combine(repositoryRoot, "src"),
            "*.csproj",
            SearchOption.AllDirectories))
        {
            if (string.Equals(
                Path.GetFileNameWithoutExtension(projectPath),
                IntegrationPackageId,
                StringComparison.Ordinal))
            {
                continue;
            }

            string text = await File.ReadAllTextAsync(projectPath);
            await Assert.That(text)
                .DoesNotContain($"{IntegrationPackageId}.csproj")
                .Because($"{projectPath} must not reference the integration package");
        }
    }

    [Test]
    public async Task TheIntegrationPackageKeepsProductionTrimAndAotAnalysis()
    {
        string repositoryRoot = FindRepositoryRoot();
        string project = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            IntegrationPackageId,
            $"{IntegrationPackageId}.csproj"));

        // Directory.Build.props applies IsAotCompatible, IsTrimmable, and the trim/AOT/single
        // file analyzers to every production library, and public API tracking to every project
        // under src/. Opting out of that classification is the only way this package could
        // stop being analysed, so the absence of the opt-out is the contract worth asserting.
        await Assert.That(project).DoesNotContain("IsApplicationProject");
        await Assert.That(project).DoesNotContain("EnableAotAnalyzer>false");
        await Assert.That(project).DoesNotContain("EnableTrimAnalyzer>false");
        await Assert.That(project).Contains("<TargetFrameworks>net8.0;net9.0;net10.0<");

        foreach (string baseline in new[] { "PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt" })
        {
            await Assert.That(File.Exists(Path.Combine(
                repositoryRoot, "src", IntegrationPackageId, baseline))).IsTrue();
        }
    }

    [Test]
    public async Task TheIntegrationPackageIsPublishedAndDocumented()
    {
        string repositoryRoot = FindRepositoryRoot();
        string packScript = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot, "eng", "pack-packages.ps1"));
        string solution = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot, "OpenUsd.slnx"));
        string packaging = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot, "docs", "packaging.md"));
        string bridgeDoc = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot, "docs", "omniverse-bridge.md"));
        string viewerDoc = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot, "docs", "viewer.md"));

        await Assert.That(packScript).Contains($"'{IntegrationPackageId}'");
        await Assert.That(solution).Contains(
            $"src/{IntegrationPackageId}/{IntegrationPackageId}.csproj");
        await Assert.That(packaging).Contains(IntegrationPackageId);
        await Assert.That(bridgeDoc).Contains(IntegrationPackageId);
        await Assert.That(viewerDoc).Contains(IntegrationPackageId);
    }

    [Test]
    public async Task TheIntegrationPackagePacksForEveryFrameworkWithoutAnNvidiaComponent()
    {
        // Packing in Release is also the executed multi-framework and analysis evidence: the
        // pack builds net8.0, net9.0, and net10.0 with TreatWarningsAsErrors and the trim,
        // NativeAOT, and single-file analyzers that Directory.Build.props turns on for every
        // production library, so a warning in any of them fails this test. There is no
        // PublishAot consumer here, unlike the transport-only bridge packages: this package
        // references the Avalonia Viewer shell, which is an application assembly that is not
        // published NativeAOT, so a consumer publish would prove a property the product does
        // not claim.
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);
            string packagePath = await PackAsync(
                repositoryRoot,
                IntegrationPackageId,
                packageRoot);

            using ZipArchive archive = ZipFile.OpenRead(packagePath);
            string[] entries = [.. archive.Entries.Select(entry => entry.FullName)];
            foreach (string framework in SupportedFrameworks)
            {
                await Assert.That(entries).Contains(
                    $"lib/{framework}/{IntegrationPackageId}.dll");
            }

            foreach (string entry in entries)
            {
                await Assert.That(entry.Contains("omni", StringComparison.OrdinalIgnoreCase))
                    .IsFalse()
                    .Because($"the package carries '{entry}'");
                await Assert.That(entry.Contains("nvidia", StringComparison.OrdinalIgnoreCase))
                    .IsFalse()
                    .Because($"the package carries '{entry}'");
            }

            string nuspec = await ReadNuspecAsync(packagePath);
            await Assert.That(nuspec).Contains("id=\"OpenUsd.Viewer\"");
            await Assert.That(nuspec).Contains("id=\"OpenUsd.Bridge.Grpc\"");
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    private static async Task<string> PackAsync(
        string repositoryRoot,
        string packageId,
        string packageRoot)
    {
        string projectPath = Path.Combine(repositoryRoot, "src", packageId, $"{packageId}.csproj");
        (int exitCode, string output) = await RunDotnetAsync(
            repositoryRoot,
            [
                "pack",
                projectPath,
                "-c",
                "Release",
                "--nologo",
                "-p:BuildInParallel=false",
                $"-p:PackageOutputPath={packageRoot}"
            ]);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(output);
        }

        return Directory
            .GetFiles(packageRoot, $"{packageId}.*.nupkg")
            .Single(path => !path.EndsWith(".symbols.nupkg", StringComparison.Ordinal));
    }

    private static async Task<string> ReadNuspecAsync(string packagePath)
    {
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry entry = archive.Entries.Single(candidate =>
            candidate.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using var reader = new StreamReader(entry.Open());
        return await reader.ReadToEndAsync();
    }

    private static async Task<(int ExitCode, string Output)> RunDotnetAsync(
        string workingDirectory,
        string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await standardOutput + await standardError);
    }

    private static string CreateWorkRoot(string repositoryRoot)
    {
        string workRoot = Path.Combine(
            repositoryRoot,
            "artifacts",
            "viewer-bridge-package-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);
        return workRoot;
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
            throw new InvalidOperationException("Could not locate the repository root.");
    }
}
