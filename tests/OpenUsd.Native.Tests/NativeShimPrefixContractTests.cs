// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace OpenUsd.Native.Tests;

/// <summary>
/// Requires each optional native shim to install into its own prefix rather
/// than over the verified one.
/// </summary>
/// <remarks>
/// <c>eng/build-cesium-shim.ps1</c> used to install into
/// <c>native/install/shim/&lt;rid&gt;</c>, which is the prefix the immutable
/// native archive is extracted into. The <c>&lt;rid&gt;-cesium</c> preset
/// builds the whole native project, so <c>cmake --install</c> wrote
/// <c>openusd_dotnet</c>, <c>openusd_hydra</c>, <c>openusd_hdsilk</c> and
/// <c>openusd_storm_child</c> over binaries that a separate pipeline run had
/// built and verified.
///
/// Every published package rests on that archive chain, so packing Cesium in
/// that state would have shipped locally rebuilt shims in place of verified
/// ones -- silently, because the files land at exactly the paths the packaging
/// expects. It is why the Cesium packages were withheld from 0.5.0-alpha.
///
/// Nothing else can see this. The packaging targets read whatever is at the
/// path, the package layout tests pack against a synthetic install, and the
/// only symptom is that a published binary differs from the archived one. The
/// PhysX shim already had the right shape, using <c>&lt;rid&gt;-physx</c>; this
/// pins that both keep it.
/// </remarks>
public sealed class NativeShimPrefixContractTests
{
    /// <summary>Scripts that build an optional shim beside the verified one.</summary>
    private static readonly string[] OptionalShimScripts =
    [
        "eng/build-cesium-shim.ps1",
    ];

    [Test]
    public async Task OptionalShimsDoNotInstallOverTheVerifiedPrefix()
    {
        string root = FindRepositoryRoot();
        List<string> offenders = [];
        int checkedScripts = 0;

        foreach (string relative in OptionalShimScripts)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            await Assert.That(File.Exists(path))
                .IsTrue()
                .Because($"{relative} must exist for this contract to mean anything");

            string script = await File.ReadAllTextAsync(path);
            Match install = Regex.Match(
                script,
                @"\$shimInstallRoot\s*=\s*Join-Path\s+\$repoRoot\s+""(?<prefix>[^""]+)""",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5));

            if (!install.Success)
            {
                offenders.Add($"{relative}: no $shimInstallRoot assignment found");
                continue;
            }

            checkedScripts++;
            string prefix = install.Groups["prefix"].Value;

            // The verified archive is extracted into native/install/shim/$Rid.
            // Anything installing there overwrites it.
            if (prefix.TrimEnd('/') is "native/install/shim/$Rid")
            {
                offenders.Add(
                    $"{relative} installs into '{prefix}', the prefix the verified " +
                    "native archive occupies");
            }
        }

        // Non-vacuity: a regex that stops matching would report nothing.
        await Assert.That(checkedScripts)
            .IsEqualTo(OptionalShimScripts.Length)
            .Because("every listed script must still declare a resolvable install prefix");
        await Assert.That(offenders)
            .IsEmpty()
            .Because(
                "an optional shim that installs over native/install/shim/<rid> rebuilds " +
                "the archive-verified core, Hydra, hdSilk and Storm child binaries and " +
                "would publish those instead: " + string.Join("; ", offenders));
    }

    /// <summary>
    /// Requires the Cesium packaging to read its payload from the Cesium prefix
    /// rather than from the verified shim prefix.
    /// </summary>
    /// <remarks>
    /// The script and the packaging have to agree. If the script moves and the
    /// targets do not, packing fails loudly, which is fine. If the targets move
    /// back and the script does not, packing silently picks up whatever the
    /// verified prefix happens to hold, which is not.
    /// </remarks>
    [Test]
    public async Task CesiumPackagingReadsTheCesiumPrefix()
    {
        string root = FindRepositoryRoot();
        string targets = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Runtime.Packaging",
            "OpenUsd.Runtime.Packaging.targets"));

        MatchCollection cesiumLibraries = Regex.Matches(
            targets,
            @"<_OpenUsdCesium(?:Library|ShimLibrary)[^>]*>(?<value>[^<]*)<",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        List<string> offenders = [];
        foreach (Match library in cesiumLibraries)
        {
            string value = library.Groups["value"].Value;
            if (value.Contains("$(OpenUsdShimInstallRoot)", StringComparison.Ordinal))
            {
                offenders.Add(value.Trim());
            }
        }

        // Non-vacuity: zero matches would pass while checking nothing.
        await Assert.That(cesiumLibraries.Count)
            .IsGreaterThan(2)
            .Because("the Cesium payload must still be declared per RID in the targets");
        await Assert.That(offenders)
            .IsEmpty()
            .Because(
                "Cesium packaging must read OpenUsdCesiumShimInstallRoot; reading the " +
                "verified shim prefix is how a rebuilt binary would be published in " +
                "place of an archived one: " + string.Join("; ", offenders));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("The repository root was not found.");
    }
}
