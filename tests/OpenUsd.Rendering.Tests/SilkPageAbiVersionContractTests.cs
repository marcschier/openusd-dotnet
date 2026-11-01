// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Runtime.InteropServices;
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
    public async Task NativeChildHeaderManagedConstantAndPackagePinMatchTheLockedAbi()
    {
        string root = FindRepositoryRoot();
        using JsonDocument lockFile = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(root, "eng", "openusd.lock.json")));
        uint locked = lockFile.RootElement.GetProperty("abi").GetProperty("stormChild").GetUInt32();
        string header = await File.ReadAllTextAsync(Path.Combine(
            root, "native", "openusd_storm_child", "include", "openusd_storm_child.h"));
        Match native = Regex.Match(header, @"OPENUSD_STORM_CHILD_ABI_VERSION\s+(\d+)u",
            RegexOptions.None, TimeSpan.FromSeconds(5));
        await Assert.That(native.Success).IsTrue();
        await Assert.That(uint.Parse(native.Groups[1].Value, CultureInfo.InvariantCulture)).IsEqualTo(locked);
        await Assert.That(locked).IsEqualTo(RenderNativeAbiVersions.StormChildAbi);
        string package = await File.ReadAllTextAsync(Path.Combine(
            root, "tests", "OpenUsd.Package.Tests", "RuntimePackageTests.cs"));
        Match required = Regex.Match(package, @"RequiredStormChildAbiVersion\s*=\s*(\d+);",
            RegexOptions.None, TimeSpan.FromSeconds(5));
        await Assert.That(required.Success).IsTrue();
        await Assert.That(uint.Parse(required.Groups[1].Value, CultureInfo.InvariantCulture)).IsEqualTo(locked);
        await Assert.That(header).Contains("openusd_storm_child_capture_aovs");
    }

    [Test]
    public async Task NativeHeaderAndManagedLoaderMatchTheLockedSessionAbi()
    {
        string root = FindRepositoryRoot();
        string header = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "hdSilk", "include", "openusd_hdsilk.h"));
        Match match = Regex.Match(
            header, @"#define\s+OPENUSD_SILK_SESSION_ABI_VERSION\s+(\d+)u",
            RegexOptions.None, TimeSpan.FromSeconds(5));
        await Assert.That(match.Success).IsTrue();
        using JsonDocument lockFile = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(root, "eng", "openusd.lock.json")));
        uint locked = lockFile.RootElement.GetProperty("abi").GetProperty("silkSession").GetUInt32();
        await Assert.That(uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).IsEqualTo(locked);
        await Assert.That(locked).IsEqualTo(RenderNativeAbiVersions.SilkSessionAbi);
        await Assert.That(locked).IsEqualTo(OpenUsdSilkRuntime.RequiredSilkSessionAbiVersion);
        await Assert.That(Marshal.SizeOf<OpenUsdSilkRuntime.NativeSceneIngestionRequest>()).IsEqualTo(32);
        await Assert.That(Marshal.OffsetOf<OpenUsdSilkRuntime.NativeSceneIngestionRequest>(
            nameof(OpenUsdSilkRuntime.NativeSceneIngestionRequest.MaterialBindingPurpose)).ToInt64()).IsEqualTo(24L);
        await Assert.That(Marshal.SizeOf<OpenUsdSilkRuntime.NativeVolumeBudgetUsage>()).IsEqualTo(40);
        await Assert.That(Marshal.OffsetOf<OpenUsdSilkRuntime.NativeVolumeBudgetUsage>(
            nameof(OpenUsdSilkRuntime.NativeVolumeBudgetUsage.ReservationCount)).ToInt64()).IsEqualTo(32L);
        await Assert.That(header).Contains("#define OPENUSD_SILK_SCENE_INGESTION_VERSION 1u");
        await Assert.That(header).Contains("openusd_silk_get_session_abi_version");
        await Assert.That(header).Contains("openusd_silk_get_page_abi_version");
        await Assert.That(header).Contains("openusd_silk_session_sync_with_scene_ingestion");
        await Assert.That(header).Contains("openusd_silk_page_retain_volume_assets");
        await Assert.That(header).Contains("openusd_silk_volume_assets_copy");
        await Assert.That(header).Contains("openusd_silk_volume_assets_release");
        await Assert.That(header).Contains("openusd_silk_volume_budget_create");
        await Assert.That(header).Contains("openusd_silk_session_set_volume_budget");
        await Assert.That(header).Contains("openusd_silk_volume_budget_get_usage");
        await Assert.That(header).Contains("openusd_silk_volume_budget_release");
    }

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
    public async Task FixturePagesUseTheCurrentAbiUnlessTestingRejection()
    {
        string root = FindRepositoryRoot();
        string[] testProjects = ["OpenUsd.Rendering.Tests", "OpenUsd.Rendering.ConformanceTests"];
        HashSet<string> stale = [];
        int constructorCount = 0;
        int literalEnumeratorAbiCount = 0;
        foreach (string project in testProjects)
        {
            string directory = Path.Combine(root, "tests", project);
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                string text = await File.ReadAllTextAsync(file);
                constructorCount += Regex.Count(
                    text, @"new\s+OpenUsdSilkPage\s*\(", RegexOptions.None, TimeSpan.FromSeconds(5));
                foreach (Match match in Regex.Matches(
                    text,
                    @"new\s+OpenUsdSilkPage\s*\(\s*(?<version>\d+)u?\s*,",
                    RegexOptions.None,
                    TimeSpan.FromSeconds(5)))
                {
                    uint version = uint.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture);
                    int lineStart = text.LastIndexOf('\n', match.Index) + 1;
                    bool explicitRejection =
                        Path.GetFileName(file) == "SilkCommandParserTests.cs" &&
                        version == 1 &&
                        text[lineStart..match.Index].Contains("Assert.That(() => ", StringComparison.Ordinal);
                    if (version != SilkCommandParser.PageAbiVersion && !explicitRejection)
                    {
                        stale.Add($"{Path.GetRelativePath(root, file)}: {version}");
                    }
                }
                foreach (Match match in Regex.Matches(
                    text,
                    @"SilkCommandParser\.Enumerate\s*\(\s*(?:[A-Za-z_]\w*|\[\])\s*,\s*\d+\s*,\s*(?<version>\d+)u?\s*\)",
                    RegexOptions.None,
                    TimeSpan.FromSeconds(5)))
                {
                    literalEnumeratorAbiCount++;
                    uint version = uint.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture);
                    string prefix = text[..match.Index];
                    bool explicitRejection =
                        Path.GetFileName(file) == "SilkMaterialCommandTests.cs" &&
                        version == 4 &&
                        prefix.LastIndexOf(
                            "RejectsAPageFromThePreviousAbi",
                            StringComparison.Ordinal) >
                        prefix.LastIndexOf("[Test]", StringComparison.Ordinal);
                    if (version != SilkCommandParser.PageAbiVersion && !explicitRejection)
                    {
                        stale.Add($"{Path.GetRelativePath(root, file)}: parser enumerator ABI {version}");
                    }
                }
            }
        }

        await Assert.That(constructorCount).IsGreaterThan(100);
        await Assert.That(literalEnumeratorAbiCount).IsGreaterThan(0);
        await Assert.That(stale)
            .IsEmpty()
            .Because("Fixture pages must use the current ABI: " + string.Join("; ", stale.Order()));
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
    /// renderer overview, the troubleshooting contract list, the packaging
    /// runtime-identity line, the compatibility contract table, and the probe
    /// description in the testing guide each named a version the runtime no
    /// longer speaks.
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
            (Path.Combine("docs", "versioning-compatibility.md"),
                @"\| hdSilk command page \| ABI (?<version>\d+) \|"),
            (Path.Combine("docs", "testing.md"),
                @"It asserts page ABI (?<version>\d+) and the exact byte offsets"),
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

    [Test]
    public async Task DeclaredPageAndSessionAbiContractsAre32And7()
    {
        uint page = SilkCommandParser.PageAbiVersion;
        uint session = RenderNativeAbiVersions.SilkSessionAbi;
        uint loader = OpenUsdSilkRuntime.RequiredSilkSessionAbiVersion;
        await Assert.That(page).IsEqualTo(32u);
        await Assert.That(session).IsEqualTo(7u);
        await Assert.That(loader).IsEqualTo(7u);
    }

    [Test]
    [Arguments(22u)]
    [Arguments(23u)]
    [Arguments(24u)]
    [Arguments(25u)]
    [Arguments(26u)]
    [Arguments(27u)]
    [Arguments(28u)]
    [Arguments(29u)]
    [Arguments(30u)]
    [Arguments(31u)]
    public async Task ParserRejectsAdjacentAndStalePageAbiVersions(uint rejectedVersion)
    {
        byte[] page = new byte[272];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            page, (uint)SilkCommandType.Frame);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(4), 272u);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(8), 123);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(12), 47);
        for (int element = 0; element < 16; element++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(
                page.AsSpan(16 + (element * 8)), element % 5 == 0 ? 1d : 0d);
            System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(
                page.AsSpan(144 + (element * 8)), element % 5 == 0 ? 1d : 0d);
        }

        (int width, int height, double viewDiagonal, bool finished) =
            ReadVersionedFrame(page, 32u);
        await Assert.That(width).IsEqualTo(123);
        await Assert.That(height).IsEqualTo(47);
        await Assert.That(viewDiagonal).IsEqualTo(1d);
        await Assert.That(finished).IsTrue();
        await Assert.That(() => ReadVersionedFrame(page, rejectedVersion))
            .Throws<InvalidDataException>();
    }

    private static (int Width, int Height, double ViewDiagonal, bool Finished)
        ReadVersionedFrame(byte[] page, uint version)
    {
        using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(page, 1, version);
        if (!commands.MoveNext())
        {
            throw new InvalidDataException("The versioned page contains no frame.");
        }
        SilkFrameCommand frame = commands.Current.AsFrame();
        int width = frame.Width;
        int height = frame.Height;
        double viewDiagonal = frame.GetViewElement(15);
        return (width, height, viewDiagonal, !commands.MoveNext());
    }
}
