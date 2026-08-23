// Copyright (c) marcschier. Licensed under the MIT License.

using System.Xml.Linq;

namespace OpenUsd.Package.Tests;

public sealed class RuntimePackageValidationContractTests
{
    private static readonly string[] ExpectedRuntimePackageValidationOptOuts =
    [
        "OpenUsd.Runtime.Core",
        "OpenUsd.Runtime.Core.linux-x64",
        "OpenUsd.Runtime.Core.osx-arm64",
        "OpenUsd.Runtime.Core.win-x64",
        "OpenUsd.Runtime.Cesium",
        "OpenUsd.Runtime.Cesium.linux-x64",
        "OpenUsd.Runtime.Cesium.osx-arm64",
        "OpenUsd.Runtime.Cesium.win-x64",
        "OpenUsd.Runtime.Imaging",
        "OpenUsd.Runtime.Imaging.linux-x64",
        "OpenUsd.Runtime.Imaging.osx-arm64",
        "OpenUsd.Runtime.Imaging.win-x64",
        "OpenUsd.Runtime.Physics",
        "OpenUsd.Runtime.Physics.linux-x64",
        "OpenUsd.Runtime.Physics.win-x64"
    ];

    [Test]
    public async Task RuntimePackageValidationOptOutsAreDeliberate()
    {
        string root = FindRepositoryRoot();
        ProjectContract[] runtimeProjects = Directory
            .EnumerateFiles(
                Path.Combine(root, "src"),
                "OpenUsd.Runtime.*.csproj",
                SearchOption.AllDirectories)
            .Select(LoadProjectContract)
            .ToArray();
        ProjectContract[] optOuts = runtimeProjects
            .Where(project => project.PackageValidation == "false")
            .ToArray();

        await Assert.That(optOuts.Select(project => project.Name).ToArray())
            .IsEquivalentTo(ExpectedRuntimePackageValidationOptOuts);

        foreach (ProjectContract project in optOuts)
        {
            await Assert.That(project.IncludeBuildOutput).IsEqualTo("false")
                .Because(project.Name);
            await Assert.That(project.HasPackageValidationReason).IsTrue()
                .Because(project.Name);
        }

        await Assert.That(runtimeProjects.Length)
            .IsEqualTo(ExpectedRuntimePackageValidationOptOuts.Length);
    }

    [Test]
    public async Task NonRuntimeProductionProjectsDoNotDisablePackageValidation()
    {
        string root = FindRepositoryRoot();
        XDocument build = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        XElement repositoryDefault = build
            .Descendants("EnablePackageValidation")
            .Single(element => element.Value == "true");

        await Assert.That(repositoryDefault.Parent?.Attribute("Condition")?.Value ?? "")
            .Contains("_IsProductionLibrary");

        string[] nonRuntimeOptOuts = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(LoadProjectContract)
            .Where(project =>
                !project.Name.StartsWith("OpenUsd.Runtime.", StringComparison.Ordinal) &&
                project.PackageValidation == "false")
            .Select(project => project.Name)
            .ToArray();

        await Assert.That(nonRuntimeOptOuts)
            .IsEmpty()
            .Because(
                "managed production libraries inherit package validation from Directory.Build.props; " +
                "only assembly-less runtime packaging projects may opt out");
    }

    private static ProjectContract LoadProjectContract(string path)
    {
        XDocument project = XDocument.Load(path);
        string text = File.ReadAllText(path);
        string name = Path.GetFileNameWithoutExtension(path);
        string includeBuildOutput = project
            .Descendants("IncludeBuildOutput")
            .Select(element => element.Value)
            .FirstOrDefault() ??
            "";
        string packageValidation = project
            .Descendants("EnablePackageValidation")
            .Select(element => element.Value)
            .FirstOrDefault() ??
            "";
        bool hasPackageValidationReason =
            text.Contains("Package validation compares managed lib/ref assemblies.", StringComparison.Ordinal) &&
            text.Contains("the opt-out is intentional.", StringComparison.Ordinal);

        return new ProjectContract(
            name,
            includeBuildOutput,
            packageValidation,
            hasPackageValidationReason);
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

    private sealed record ProjectContract(
        string Name,
        string IncludeBuildOutput,
        string PackageValidation,
        bool HasPackageValidationReason);
}
