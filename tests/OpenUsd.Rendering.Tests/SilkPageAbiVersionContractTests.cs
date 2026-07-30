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
