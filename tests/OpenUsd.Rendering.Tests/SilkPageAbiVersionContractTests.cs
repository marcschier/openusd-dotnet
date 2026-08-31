// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Pins the command-page ABI version to every place that independently states
/// it. The version lives in the native header, the lock file, the managed
/// parser, and the package test's required-version constant, and a bump that
/// updates only some of them produces a build that loads a native library it
/// cannot parse. That drift has already happened once: the package constant
/// stayed at 3 through the ABI 4 bump.
/// </summary>
public sealed class SilkPageAbiVersionContractTests
{
    [Test]
    public async Task ManagedParserMatchesTheLockedRenderCommandAbi()
    {
        using JsonDocument lockFile = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(FindRepositoryRoot(), "eng", "openusd.lock.json")));

        uint locked = lockFile.RootElement
            .GetProperty("abi")
            .GetProperty("renderCommands")
            .GetUInt32();

        uint parser = SilkCommandParser.PageAbiVersion;
        await Assert.That(parser).IsEqualTo(locked);
    }

    [Test]
    public async Task NativeHeaderMatchesTheLockedRenderCommandAbi()
    {
        string root = FindRepositoryRoot();
        string header = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "hdSilk", "include", "openusd_hdsilk.h"));
        Match match = Regex.Match(
            header,
            @"#define\s+OPENUSD_SILK_PAGE_ABI_VERSION\s+(\d+)u",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        await Assert.That(match.Success).IsTrue();

        using JsonDocument lockFile = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(root, "eng", "openusd.lock.json")));
        uint locked = lockFile.RootElement
            .GetProperty("abi")
            .GetProperty("renderCommands")
            .GetUInt32();

        await Assert.That(uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).IsEqualTo(locked);
    }

    [Test]
    public async Task PackageTestRequiredVersionMatchesTheLockedRenderCommandAbi()
    {
        string root = FindRepositoryRoot();
        string source = await File.ReadAllTextAsync(
            Path.Combine(
                root, "tests", "OpenUsd.Package.Tests", "RuntimePackageTests.cs"));
        Match match = Regex.Match(
            source,
            @"RequiredSilkPageAbiVersion\s*=\s*(\d+);",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        await Assert.That(match.Success).IsTrue();

        using JsonDocument lockFile = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(root, "eng", "openusd.lock.json")));
        uint locked = lockFile.RootElement
            .GetProperty("abi")
            .GetProperty("renderCommands")
            .GetUInt32();

        // Checked from this assembly rather than from the package tests, because
        // the package tests need a built native archive and so do not run on an
        // ordinary CI push, which is exactly when the drift is introduced.
        await Assert.That(uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).IsEqualTo(locked);
    }

    /// <summary>
    /// Pins the Storm ABI generation to the lock the same way the command page is pinned.
    /// </summary>
    /// <remarks>
    /// The Storm ABI is stated in the Hydra header, in the managed constant, and in the lock, but
    /// the only check that compared them lived in <c>eng/native-install-metadata.ps1</c>, which
    /// needs a completed native install and so never runs on an ordinary push. The header and the
    /// managed constant moved to 8 while the lock still recorded 7, and nothing failed.
    /// </remarks>
    [Test]
    public async Task NativeHeaderAndManagedConstantMatchTheLockedStormAbi()
    {
        string root = FindRepositoryRoot();
        string header = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "openusd_hydra", "include", "openusd_hydra.h"));
        Match match = Regex.Match(
            header,
            @"#define\s+OPENUSD_STORM_ABI_VERSION\s+(\d+)u",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        await Assert.That(match.Success).IsTrue();

        using JsonDocument lockFile = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(root, "eng", "openusd.lock.json")));
        uint locked = lockFile.RootElement
            .GetProperty("abi")
            .GetProperty("storm")
            .GetUInt32();

        await Assert.That(uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .IsEqualTo(locked);
        uint managed = RenderNativeAbiVersions.StormAbi;
        await Assert.That(managed).IsEqualTo(locked);
    }

    [Test]
    public async Task NativeProbeStaticAssertMatchesTheLockedRenderCommandAbi()
    {
        // The probe's static_assert is a fifth independent statement of the page
        // ABI, and it was missing from this file's original coverage. That gap
        // was not theoretical: a change once bumped the header, lock, parser and
        // package constant to 6 while leaving the probe at 5, which would have
        // failed the native build, and was only caught by reading the diff.
        string root = FindRepositoryRoot();
        string probe = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "hdSilk", "tests", "hdsilk_probe.cpp"));
        Match match = Regex.Match(
            probe,
            @"static_assert\(OPENUSD_SILK_PAGE_ABI_VERSION\s*==\s*(\d+)\)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        await Assert.That(match.Success).IsTrue();

        using JsonDocument lockFile = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(root, "eng", "openusd.lock.json")));
        uint locked = lockFile.RootElement
            .GetProperty("abi")
            .GetProperty("renderCommands")
            .GetUInt32();

        await Assert.That(uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .IsEqualTo(locked);
    }

    /// <summary>
    /// Pins the prose that states the *current* page ABI to the lock.
    /// </summary>
    /// <remarks>
    /// Documentation states the current version in several places, and those
    /// sentences drifted through two bumps: the architecture overview, the
    /// renderer overview, the troubleshooting contract list, and the packaging
    /// runtime-identity line each named a version the runtime no longer speaks.
    /// A reader following any of them reaches the wrong conclusion about which
    /// native library a build can load. Sentences describing what an *older*
    /// version introduced are deliberately not matched: those stay true forever.
    /// </remarks>
    [Test]
    public async Task DocumentedCurrentPageAbiMatchesTheLockedRenderCommandAbi()
    {
        string root = FindRepositoryRoot();
        using JsonDocument lockFile = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(root, "eng", "openusd.lock.json")));
        uint locked = lockFile.RootElement
            .GetProperty("abi")
            .GetProperty("renderCommands")
            .GetUInt32();

        (string Path, string Pattern)[] statements =
        [
            (Path.Combine("docs", "architecture.md"),
                @"Managed code validates page ABI (?<version>\d+)"),
            (Path.Combine("docs", "rendering.md"),
                @"the hdSilk page ABI is v(?<version>\d+)"),
            (Path.Combine("docs", "troubleshooting.md"),
                @"hdSilk command-page ABI (?<version>\d+)"),
            (Path.Combine("docs", "packaging.md"),
                @"hdSilk session/page ABI \d+/(?<version>\d+)"),
        ];

        List<string> drifted = [];
        foreach ((string relative, string pattern) in statements)
        {
            string text = await File.ReadAllTextAsync(Path.Combine(root, relative));
            Match match = Regex.Match(
                text,
                pattern,
                RegexOptions.None,
                TimeSpan.FromSeconds(5));
            if (!match.Success)
            {
                drifted.Add($"{relative} no longer states the current page ABI");
                continue;
            }
            uint stated = uint.Parse(
                match.Groups["version"].Value,
                CultureInfo.InvariantCulture);
            if (stated != locked)
            {
                drifted.Add($"{relative} states {stated}, the lock records {locked}");
            }
        }

        await Assert.That(drifted)
            .IsEmpty()
            .Because(
                "Documentation names a command-page ABI the runtime no longer " +
                "speaks. " + string.Join("; ", drifted));
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
