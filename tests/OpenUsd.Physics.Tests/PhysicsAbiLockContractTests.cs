// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenUsd.Physics.Extraction;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests;

/// <summary>
/// Pins the two physics ABI generations to every place that independently states them.
/// </summary>
/// <remarks>
/// <para>
/// The retained world ABI and the extraction page ABI are each written down four times: in the
/// native header, in <c>eng/openusd.lock.json</c>, in the managed mirror that reinterprets native
/// memory against it, and in the packaging validator that embeds the version as package evidence.
/// A bump that moves only some of them produces a package whose recorded evidence is fiction and a
/// consumer that refuses the library it shipped with -- reported from inside an application, which
/// is the most expensive place to find it.
/// </para>
/// <para>
/// This drift is not hypothetical in this repository: the Storm ABI moved to 8 in the header and
/// the managed constant while the lock still recorded 7, which nothing running on an ordinary push
/// could see because the check that compares them needs a completed native install.
/// </para>
/// </remarks>
public sealed class PhysicsAbiLockContractTests
{
    [Test]
    public async Task ManagedWorldMirrorMatchesTheLockedPhysicsAbi()
    {
        uint mirror = PhysxAbi.Version;
        await Assert.That(mirror).IsEqualTo(ReadLockedAbi("physics"));
    }

    [Test]
    public async Task ManagedExtractionMirrorMatchesTheLockedExtractionAbi()
    {
        uint mirror = PhysicsExtractAbi.AbiVersion;
        await Assert.That(mirror).IsEqualTo(ReadLockedAbi("physicsExtract"));
    }

    [Test]
    public async Task NativeWorldHeaderMatchesTheLockedPhysicsAbi() =>
        await AssertHeaderMatchesLockAsync(
            Path.Combine("native", "openusd_physx", "include", "openusd_physx_world.h"),
            @"#define\s+OPENUSD_PHYSX_WORLD_ABI_VERSION\s+(\d+)u",
            "physics");

    [Test]
    public async Task NativeExtractionHeaderMatchesTheLockedExtractionAbi() =>
        await AssertHeaderMatchesLockAsync(
            Path.Combine("native", "openusd_dotnet", "include", "openusd_physics_extract.h"),
            @"#define\s+OPENUSD_PHYSICS_EXTRACT_ABI_VERSION\s+(\d+)u",
            "physicsExtract");

    /// <summary>
    /// Requires the packaging validator to read the locked versions rather than restate them.
    /// </summary>
    /// <remarks>
    /// A validator that carried its own literal would keep certifying the old generation after a
    /// bump, which is worse than having no evidence at all: the package would then ship a signed
    /// claim that the library matches a version it does not.
    /// </remarks>
    [Test]
    public async Task PackagingValidatorReadsTheLockedPhysicsAbi()
    {
        string validator = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "OpenUsd.Runtime.Packaging",
            "Validate-PhysicsNativePackage.ps1"));

        await Assert.That(validator).Contains("$lock.abi.physics");
        await Assert.That(validator).Contains("$lock.abi.physicsExtract");
        await Assert.That(validator).Contains("OPENUSD_PHYSX_WORLD_ABI_VERSION");
        await Assert.That(validator).Contains("OPENUSD_PHYSICS_EXTRACT_ABI_VERSION");
    }

    /// <summary>
    /// Requires the package suite's required-version constant to track the lock.
    /// </summary>
    /// <remarks>
    /// Checked from this assembly rather than from the package tests, because the package tests
    /// need a built native install and so do not run on an ordinary push, which is exactly when the
    /// drift is introduced.
    /// </remarks>
    [Test]
    public async Task PackageTestRequiredVersionsMatchTheLockedPhysicsAbi()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "OpenUsd.Package.Tests",
            "RuntimePackageTests.cs"));

        await AssertConstantMatchesLockAsync(source, "RequiredPhysicsAbiVersion", "physics");
        await AssertConstantMatchesLockAsync(
            source,
            "RequiredPhysicsExtractAbiVersion",
            "physicsExtract");
    }

    private static async Task AssertHeaderMatchesLockAsync(
        string relativeHeaderPath,
        string pattern,
        string lockName)
    {
        string header = await File.ReadAllTextAsync(
            Path.Combine(FindRepositoryRoot(), relativeHeaderPath));
        Match match = Regex.Match(header, pattern, RegexOptions.None, TimeSpan.FromSeconds(5));

        await Assert.That(match.Success).IsTrue();
        await Assert.That(uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .IsEqualTo(ReadLockedAbi(lockName));
    }

    private static async Task AssertConstantMatchesLockAsync(
        string source,
        string constantName,
        string lockName)
    {
        Match match = Regex.Match(
            source,
            $@"{constantName}\s*=\s*(\d+);",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        await Assert.That(match.Success).IsTrue();
        await Assert.That(uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .IsEqualTo(ReadLockedAbi(lockName));
    }

    private static uint ReadLockedAbi(string name)
    {
        using JsonDocument lockFile = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FindRepositoryRoot(), "eng", "openusd.lock.json")));
        return lockFile.RootElement.GetProperty("abi").GetProperty(name).GetUInt32();
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
}
