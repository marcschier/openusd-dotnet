// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Package.Tests;

public sealed class RuntimeTargetHostSelectionTests
{
    private static readonly RuntimeTargetContract[] RuntimeTargets =
    [
        new("OpenUsd.Runtime.Core.win-x64", "win-x64", HasResources: true),
        new("OpenUsd.Runtime.Core.linux-x64", "linux-x64", HasResources: true),
        new("OpenUsd.Runtime.Core.osx-arm64", "osx-arm64", HasResources: true),
        new("OpenUsd.Runtime.Imaging.win-x64", "win-x64", HasResources: true),
        new("OpenUsd.Runtime.Imaging.linux-x64", "linux-x64", HasResources: true),
        new("OpenUsd.Runtime.Imaging.osx-arm64", "osx-arm64", HasResources: true),
        new("OpenUsd.Runtime.Cesium.win-x64", "win-x64", HasResources: false),
        new("OpenUsd.Runtime.Cesium.linux-x64", "linux-x64", HasResources: false),
        new("OpenUsd.Runtime.Cesium.osx-arm64", "osx-arm64", HasResources: false),
    ];

    [Test]
    public async Task RuntimeTargetsScopeRidlessAssetsToTheSdkHostRid()
    {
        foreach (RuntimeTargetContract target in RuntimeTargets)
        {
            string text = await File.ReadAllTextAsync(TargetPath(target.PackageId));

            await Assert.That(text).DoesNotContain("'$(RuntimeIdentifier)' == '' or");
            await Assert.That(text).Contains("$(NETCoreSdkPortableRuntimeIdentifier)");
            await Assert.That(text).Contains("ValidateOpenUsdRidlessHostRuntime");
            await Assert.That(text).Contains("Set RuntimeIdentifier explicitly");
            await Assert.That(text).Contains($"'$(RuntimeIdentifier)' == '' and '$(_OURid)' == '{target.Rid}'");
            await Assert.That(text).Contains("/native");
            await Assert.That(text).Contains("<Link>%(Filename)%(Extension)</Link>");

            if (target.HasResources)
            {
                await Assert.That(text).Contains($"'$(RuntimeIdentifier)' == '{target.Rid}'");
                await Assert.That(text).Contains("/resources");
                await Assert.That(text).Contains("<Link>%(RecursiveDir)%(Filename)%(Extension)</Link>");
            }
        }
    }

    private static string TargetPath(string packageId) =>
        Path.Combine(FindRepositoryRoot(), "src", packageId, "buildTransitive", $"{packageId}.targets");

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

    private sealed record RuntimeTargetContract(string PackageId, string Rid, bool HasResources);
}
