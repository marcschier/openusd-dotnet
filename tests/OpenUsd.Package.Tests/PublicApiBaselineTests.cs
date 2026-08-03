// Copyright (c) marcschier. Licensed under the MIT License.

using System.Xml.Linq;

namespace OpenUsd.Package.Tests;

public sealed class PublicApiBaselineTests
{
    private static readonly string[] ExpectedApiProjects =
    [
        "OpenUsd",
        "OpenUsd.Interop",
        "OpenUsd.Rendering",
        "OpenUsd.Rendering.Silk",
        "OpenUsd.Rendering.Silk.D3D12",
        "OpenUsd.Rendering.Silk.Metal",
        "OpenUsd.Rendering.Silk.Vulkan",
        "OpenUsd.Rendering.Storm"
    ];

    private static readonly string[] ExpectedRuntimeProjects =
    [
        "OpenUsd.Runtime.Core.linux-x64",
        "OpenUsd.Runtime.Core.osx-arm64",
        "OpenUsd.Runtime.Core.win-x64",
        "OpenUsd.Runtime.Imaging.linux-x64",
        "OpenUsd.Runtime.Imaging.osx-arm64",
        "OpenUsd.Runtime.Imaging.win-x64"
    ];

    private static readonly string[] ExpectedPackableManagedProjects =
    [
        "OpenUsd",
        "OpenUsd.Interop",
        "OpenUsd.Rendering",
        "OpenUsd.Rendering.Silk",
        "OpenUsd.Rendering.Silk.D3D12",
        "OpenUsd.Rendering.Silk.Metal",
        "OpenUsd.Rendering.Silk.Vulkan",
        "OpenUsd.Rendering.Storm",
        "OpenUsd.Viewer"
    ];

    [Test]
    public async Task CentralAnalyzerWiringIsVersionedPrivateAndScoped()
    {
        string root = FindRepositoryRoot();
        XDocument packages = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));
        XElement analyzerVersion = packages
            .Descendants("PackageVersion")
            .Single(element =>
                element.Attribute("Include")?.Value ==
                "Microsoft.CodeAnalysis.PublicApiAnalyzers");

        await Assert.That(analyzerVersion.Attribute("Version")?.Value)
            .IsEqualTo("5.6.0");

        XDocument build = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        XElement analyzerReference = build
            .Descendants("PackageReference")
            .Single(element =>
                element.Attribute("Include")?.Value ==
                "Microsoft.CodeAnalysis.PublicApiAnalyzers");
        string referenceCondition = analyzerReference.Attribute("Condition")?.Value ?? "";

        await Assert.That(analyzerReference.Attribute("PrivateAssets")?.Value)
            .IsEqualTo("all");
        await Assert.That(referenceCondition)
            .Contains("OpenUsdPublicApiAnalysisEnabled");

        XElement eligibility = build
            .Descendants("OpenUsdPublicApiAnalysisEnabled")
            .Single();
        string eligibilityCondition = eligibility.Attribute("Condition")?.Value ?? "";

        await Assert.That(eligibilityCondition).Contains("_IsProductionLibrary");
        await Assert.That(eligibilityCondition).Contains("MSBuildProjectDirectory.StartsWith");
        await Assert.That(eligibilityCondition).Contains("OpenUsd.Runtime.");

        XElement additionalFiles = build
            .Descendants("ItemGroup")
            .Single(element =>
                (element.Attribute("Condition")?.Value ?? "")
                    .Contains("OpenUsdPublicApiAnalysisEnabled", StringComparison.Ordinal) &&
                element.Elements("AdditionalFiles").Any());
        string[] includes = additionalFiles
            .Elements("AdditionalFiles")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToArray();

        await Assert.That(includes).IsEquivalentTo(
        [
            @"$(MSBuildProjectDirectory)\PublicAPI.Shipped.txt",
            @"$(MSBuildProjectDirectory)\PublicAPI.Unshipped.txt"
        ]);

        string buildText = await File.ReadAllTextAsync(
            Path.Combine(root, "Directory.Build.props"));
        await Assert.That(buildText).DoesNotContain("RS0016");
        await Assert.That(buildText).DoesNotContain("RS0017");
    }

    [Test]
    public async Task EveryEligibleProductionProjectHasReviewedApiFiles()
    {
        string root = FindRepositoryRoot();
        string sourceRoot = Path.Combine(root, "src");
        ProjectContract[] projects = Directory
            .EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Select(LoadProjectContract)
            .ToArray();
        ProjectContract[] eligible = projects
            .Where(project => !project.IsApplication && !project.IsContentOnly)
            .ToArray();

        await Assert.That(eligible.Select(project => project.Name).ToArray())
            .IsEquivalentTo(ExpectedApiProjects);

        foreach (ProjectContract project in eligible)
        {
            string shipped = Path.Combine(project.Directory, "PublicAPI.Shipped.txt");
            string unshipped = Path.Combine(project.Directory, "PublicAPI.Unshipped.txt");

            await Assert.That(File.Exists(shipped)).IsTrue();
            await Assert.That(File.Exists(unshipped)).IsTrue();

            string[] shippedLines = await File.ReadAllLinesAsync(shipped);
            string[] unshippedLines = await File.ReadAllLinesAsync(unshipped);

            await Assert.That(shippedLines).IsEquivalentTo(["#nullable enable"]);
            await Assert.That(unshippedLines.Length > 1).IsTrue();
            await Assert.That(unshippedLines[0]).IsEqualTo("#nullable enable");
            await Assert.That(unshippedLines.Skip(1).All(line => line.Length > 0)).IsTrue();
            await Assert.That(unshippedLines.Distinct(StringComparer.Ordinal).Count())
                .IsEqualTo(unshippedLines.Length);
        }
    }

    [Test]
    public async Task ContentOnlyRuntimePackagesRemainExcluded()
    {
        string root = FindRepositoryRoot();
        ProjectContract[] runtimeProjects = Directory
            .EnumerateFiles(
                Path.Combine(root, "src"),
                "OpenUsd.Runtime.*.csproj",
                SearchOption.AllDirectories)
            .Select(LoadProjectContract)
            .ToArray();

        await Assert.That(runtimeProjects.Select(project => project.Name).ToArray())
            .IsEquivalentTo(ExpectedRuntimeProjects);

        foreach (ProjectContract project in runtimeProjects)
        {
            await Assert.That(project.IsContentOnly).IsTrue();
            await Assert.That(File.Exists(Path.Combine(
                project.Directory,
                "PublicAPI.Shipped.txt"))).IsFalse();
            await Assert.That(File.Exists(Path.Combine(
                project.Directory,
                "PublicAPI.Unshipped.txt"))).IsFalse();
        }
    }

    [Test]
    public async Task PackableManagedProjectsTargetEveryProductionFramework()
    {
        string root = FindRepositoryRoot();
        ProjectContract[] packable = Directory
            .EnumerateFiles(
                Path.Combine(root, "src"),
                "*.csproj",
                SearchOption.AllDirectories)
            .Select(LoadProjectContract)
            .Where(project => project.IsPackable && !project.IsContentOnly)
            .ToArray();

        await Assert.That(packable.Select(project => project.Name).ToArray())
            .IsEquivalentTo(ExpectedPackableManagedProjects);

        foreach (ProjectContract project in packable)
        {
            await Assert.That(project.TargetFrameworks)
                .IsEqualTo("net8.0;net9.0;net10.0")
                .Because(project.Name);
        }
    }

    [Test]
    public async Task InteropBaselineExposesOnlyIntentionalTopLevelTypes()
    {
        string root = FindRepositoryRoot();
        string[] entries = await File.ReadAllLinesAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Interop",
            "PublicAPI.Unshipped.txt"));
        string[] publicTypes = entries
            .Where(entry =>
                entry.StartsWith("OpenUsd.Interop.", StringComparison.Ordinal) &&
                entry.Count(character => character == '.') == 2)
            .ToArray();

        await Assert.That(publicTypes).IsEquivalentTo(
        [
            "OpenUsd.Interop.OpenUsdNativeContract",
            "OpenUsd.Interop.OpenUsdNativeException",
            "OpenUsd.Interop.OpenUsdNativeRuntime",
            "OpenUsd.Interop.OpenUsdNativeStatus"
        ]);
    }

    private static ProjectContract LoadProjectContract(string path)
    {
        XDocument project = XDocument.Load(path);
        bool isApplication = project
            .Descendants("IsApplicationProject")
            .Any(element => IsTrue(element.Value)) ||
            project
                .Descendants("OutputType")
                .Any(element =>
                    element.Value.Equals("Exe", StringComparison.OrdinalIgnoreCase) ||
                    element.Value.Equals("WinExe", StringComparison.OrdinalIgnoreCase));
        bool isContentOnly = project
            .Descendants("IncludeBuildOutput")
            .Any(element => IsFalse(element.Value));
        bool isPackable = project
            .Descendants("IsPackable")
            .Any(element => IsTrue(element.Value)) ||
            (!isApplication && !project
                .Descendants("IsPackable")
                .Any(element => IsFalse(element.Value)));
        string targetFrameworks = project
            .Descendants("TargetFrameworks")
            .Select(element => element.Value)
            .FirstOrDefault() ??
            project
                .Descendants("TargetFramework")
                .Select(element => element.Value)
                .FirstOrDefault() ??
            "";
        return new ProjectContract(
            Path.GetFileNameWithoutExtension(path),
            Path.GetDirectoryName(path) ??
                throw new InvalidOperationException($"Project path has no directory: {path}"),
            isApplication,
            isContentOnly,
            isPackable,
            targetFrameworks);
    }

    private static bool IsTrue(string value) =>
        bool.TryParse(value, out bool parsed) && parsed;

    private static bool IsFalse(string value) =>
        bool.TryParse(value, out bool parsed) && !parsed;

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

    private sealed record ProjectContract(
        string Name,
        string Directory,
        bool IsApplication,
        bool IsContentOnly,
        bool IsPackable,
        string TargetFrameworks);
}
