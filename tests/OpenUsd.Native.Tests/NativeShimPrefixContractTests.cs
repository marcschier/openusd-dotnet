// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;
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
        "eng/build-physx-shim.ps1",
    ];

    /// <summary>Physics presets, which exist only where the pinned simulation SDK builds.</summary>
    private static readonly string[] ExpectedPhysicsPresets = ["win-x64-physx", "linux-x64-physx"];

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

    /// <summary>
    /// Requires the physics packaging to read its payload from the physics prefix, and to name each
    /// asset rather than glob the directory.
    /// </summary>
    /// <remarks>
    /// The physics prefix holds a full second copy of <c>openusd_dotnet</c>, <c>openusd_hydra</c>,
    /// <c>openusd_hdsilk</c> and <c>openusd_storm_child</c>, because the physics preset configures
    /// the whole native project. Those are Core and Imaging payload. A glob over this directory
    /// would publish a second copy of each at a consumer's application root, where a duplicate does
    /// not sit harmlessly beside the current binary but is loaded instead of it.
    /// </remarks>
    [Test]
    public async Task PhysicsPackagingNamesItsAssetsInThePhysicsPrefix()
    {
        string root = FindRepositoryRoot();
        string targets = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Runtime.Packaging",
            "OpenUsd.Runtime.Packaging.targets"));

        MatchCollection physicsAssets = Regex.Matches(
            targets,
            @"<_OpenUsdPhysicsShimLibrary\s+Include=""(?<value>[^""]*)""",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));
        MatchCollection physicsNames = Regex.Matches(
            targets,
            @"<_OpenUsdPhysicsLibraryFileName[^>]*>(?<value>[^<]*)<",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        List<string> offenders = [];
        foreach (Match asset in physicsAssets)
        {
            string value = asset.Groups["value"].Value;
            if (value.Contains("$(OpenUsdShimInstallRoot)", StringComparison.Ordinal))
            {
                offenders.Add($"{value.Trim()} reads the verified shim prefix");
            }
            if (value.Contains('*', StringComparison.Ordinal))
            {
                offenders.Add($"{value.Trim()} globs the physics prefix");
            }
        }
        foreach (Match name in physicsNames)
        {
            string value = name.Groups["value"].Value.Trim();
            if (!value.Contains("openusd_physx", StringComparison.Ordinal))
            {
                offenders.Add($"{value} is not the physics shim");
            }
        }

        // Non-vacuity: the payload is one named library per supported RID, and both RIDs must
        // still be declared. Zero matches would pass while checking nothing.
        await Assert.That(physicsAssets.Count)
            .IsEqualTo(1)
            .Because("the physics package publishes exactly one native asset");
        await Assert.That(physicsNames.Count)
            .IsEqualTo(2)
            .Because("that asset must still be named for win-x64 and linux-x64");
        await Assert.That(offenders)
            .IsEmpty()
            .Because(
                "physics packaging must name assets under OpenUsdPhysicsShimInstallRoot; a glob or " +
                "the verified shim prefix is how a duplicate core shim or a proprietary NVIDIA " +
                "module would be published: " + string.Join("; ", offenders));
    }

    /// <summary>
    /// Requires the physics shim to link PhysXVehicle2 explicitly everywhere the pinned port does
    /// not, and to fail configure rather than build a solver with no vehicle implementation.
    /// </summary>
    /// <remarks>
    /// The pinned vcpkg port's CMake config appends <c>PhysXVehicle2</c> to its aggregate SDK
    /// target inside an <c>if(WIN32)</c>. <c>openusd_physx</c> compiles <c>physx::vehicle2</c>
    /// unconditionally, so on Linux the aggregate target describes a solver the vehicle code
    /// cannot link against. Nothing in this repository can see that: the port lives in a vcpkg
    /// cache, Windows CI links it happily, and the managed capability set says vehicles are
    /// supported on every RID the package ships for.
    ///
    /// This pins the explicit lookup and the hard configure failure. Failing closed matters more
    /// than the lookup itself: a published package must never advertise operational vehicles it
    /// cannot run, and a soft fallback would produce exactly that.
    /// </remarks>
    [Test]
    public async Task PhysicsShimLinksVehicle2ExplicitlyOffWindows()
    {
        string root = FindRepositoryRoot();
        string cmake = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "openusd_physx", "CMakeLists.txt"));

        await Assert.That(cmake)
            .Contains("PhysXVehicle2_static_64", StringComparison.Ordinal)
            .Because("the static Vehicle2 library has to be located by name off Windows");
        await Assert.That(cmake)
            .Contains("OPENUSD_PHYSX_VEHICLE2_LIBRARY", StringComparison.Ordinal)
            .Because("the resolved library has to reach the link line");

        Match guard = Regex.Match(
            cmake,
            @"if\(NOT OPENUSD_PHYSX_VEHICLE2_LIBRARY\)\s*\r?\n\s*message\(FATAL_ERROR",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));
        await Assert.That(guard.Success)
            .IsTrue()
            .Because(
                "a missing Vehicle2 library must fail configure; a solver built without it would " +
                "still advertise the vehicle capability the package documents");

        // Ordering, not just presence: GNU ld resolves static archives in command-line order, and
        // PhysXVehicle2 draws on PhysXFoundation and PhysXCommon, which the aggregate supplies.
        int vehicleIndex = cmake.IndexOf(
            "\"${OPENUSD_PHYSX_VEHICLE2_LIBRARY}\"",
            StringComparison.Ordinal);
        int aggregateIndex = cmake.IndexOf(
            "unofficial::omniverse-physx-sdk::sdk",
            vehicleIndex,
            StringComparison.Ordinal);
        int trailingExtensionsIndex = cmake.IndexOf(
            "\"${OPENUSD_PHYSX_EXTENSIONS_LIBRARY}\"",
            aggregateIndex,
            StringComparison.Ordinal);
        await Assert.That(vehicleIndex).IsGreaterThan(0);
        await Assert.That(aggregateIndex)
            .IsGreaterThan(vehicleIndex)
            .Because("Vehicle2 must precede the aggregate SDK target on the link line");
        await Assert.That(trailingExtensionsIndex)
            .IsGreaterThan(aggregateIndex)
            .Because("PhysX Extensions must follow the aggregate to resolve its static archive symbols");
        await Assert.That(cmake)
            .Contains("$<LINK_GROUP:RESCAN,${OPENUSD_PHYSX_VEHICLE2_LIBRARY}")
            .Because("Linux must rescan the circular PhysX static archives until all symbols resolve");
        await Assert.That(cmake)
            .Contains("target_link_options(openusd_physx PRIVATE -Wl,-z,defs)")
            .Because("Linux must reject unresolved shim symbols before probes or packages link");

        // The sources this contract exists for. If the vehicle translation unit is ever dropped,
        // this test should be revisited deliberately rather than keep passing over nothing.
        string vehicleSource = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_physx",
            "src",
            "openusd_physx_vehicle.cpp"));
        await Assert.That(vehicleSource)
            .Contains("physx::vehicle2", StringComparison.Ordinal)
            .Because("the explicit link exists because these sources use vehicle2 unconditionally");
    }

    /// <summary>
    /// Requires the Vulkan placeholder targets to be defined whenever Vulkan is off, for any
    /// reason, rather than only for one optional shim.
    /// </summary>
    /// <remarks>
    /// The locked OpenUSD build enables Vulkan on Windows and Linux, so its exported
    /// <c>hgiVulkan</c> target names <c>Vulkan::Vulkan</c> and <c>Vulkan::shaderc_combined</c> in
    /// its interface. <c>find_package(pxr)</c> imports that target regardless of what this build
    /// links, and CMake fails the generate step on a dangling target reference.
    ///
    /// The placeholder branch used to be written <c>elseif(OPENUSD_WITH_CESIUM AND NOT TARGET
    /// Vulkan::Vulkan)</c>, which made "Vulkan is off" mean "Vulkan is off, but only for Cesium".
    /// Turning Vulkan off for the physics presets then failed generation with
    /// <c>Vulkan::Vulkan ... but the target was not found</c> pointing at
    /// <c>pxrConfig.cmake</c> -- a message that names neither physics nor Vulkan configuration.
    /// The condition that matters is whether the names are missing, not why.
    /// </remarks>
    [Test]
    public async Task NativeBuildDefinesVulkanPlaceholdersWheneverVulkanIsOff()
    {
        string root = FindRepositoryRoot();
        string cmake = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "CMakeLists.txt"));

        Match branch = Regex.Match(
            cmake,
            @"if\(OPENUSD_WITH_VULKAN\)(?<on>.*?)\r?\nelse\(\)(?<off>.*?)\r?\nendif\(\)\r?\nif\(UNIX",
            RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));
        await Assert.That(branch.Success)
            .IsTrue()
            .Because(
                "the Vulkan option must have a plain else branch; an elseif re-introduces the " +
                "gate that made physics configure fail");

        // Comments are stripped before the condition checks: the branch explains the history that
        // named OPENUSD_WITH_CESIUM, and a prose mention must not be read as a gate.
        string off = string.Join(
            "\n",
            branch.Groups["off"].Value
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => !line.TrimStart().StartsWith('#')));
        await Assert.That(off)
            .DoesNotContain("OPENUSD_WITH_CESIUM", StringComparison.Ordinal)
            .Because("the placeholders must not depend on which optional shim is enabled");
        await Assert.That(off)
            .DoesNotContain("OPENUSD_WITH_PHYSX", StringComparison.Ordinal)
            .Because("naming physics here would repeat the mistake with a different option");

        foreach (string target in new[] { "Vulkan::Vulkan", "Vulkan::shaderc_combined" })
        {
            await Assert.That(off)
                .Contains($"if(NOT TARGET {target})", StringComparison.Ordinal)
                .Because($"{target} must be created only when it is genuinely missing");
            await Assert.That(off)
                .Contains($"add_library({target} INTERFACE IMPORTED)", StringComparison.Ordinal)
                .Because($"{target} must exist for pxr's exported hgiVulkan interface to resolve");
        }

        // Ordering: a placeholder created after the import is no placeholder at all.
        int vulkanBranch = cmake.IndexOf("if(OPENUSD_WITH_VULKAN)", StringComparison.Ordinal);
        int pxrImport = cmake.IndexOf("find_package(pxr CONFIG REQUIRED)", StringComparison.Ordinal);
        await Assert.That(pxrImport)
            .IsGreaterThan(0)
            .Because("the pxr import is what pulls in the dangling Vulkan reference");
        await Assert.That(vulkanBranch)
            .IsLessThan(pxrImport)
            .Because("the placeholders must exist before find_package(pxr) imports hgiVulkan");
    }

    /// <summary>
    /// Requires the physics CMake presets not to pull in Vulkan.
    /// </summary>
    /// <remarks>
    /// The physics presets inherit the platform presets, which resolve Vulkan for the OpenUSD
    /// imaging build. The physics shim links neither Vulkan nor hdSilk, so inheriting that
    /// requirement made every physics CI caller need a Vulkan SDK it never used, and the callers
    /// that did not provide one failed configure for a reason unrelated to physics.
    ///
    /// The Linux preset is checked structurally here because no Windows host can configure it. Its
    /// inheritance, its prefix path, and both cache variables are the whole of what CI supplies to
    /// <c>cmake --preset linux-x64-physx</c>, so a defect in any of them is a defect this test can
    /// see without a Linux runner.
    /// </remarks>
    [Test]
    public async Task PhysicsPresetsDoNotRequireVulkan()
    {
        string root = FindRepositoryRoot();
        using JsonDocument presets = JsonDocument.Parse(
            await File.ReadAllBytesAsync(Path.Combine(root, "native", "CMakePresets.json")));

        JsonElement[] configurePresets = [.. presets.RootElement
            .GetProperty("configurePresets")
            .EnumerateArray()];
        string[] physicsPresets =
        [
            .. configurePresets
                .Select(preset => preset.GetProperty("name").GetString()!)
                .Where(name => name.EndsWith("-physx", StringComparison.Ordinal)),
        ];

        await Assert.That(physicsPresets)
            .IsEquivalentTo(ExpectedPhysicsPresets)
            .Because(
                "the pinned vcpkg PhysX port supports win-x64 and linux-x64 only, so no other " +
                "physics preset may exist to be built by mistake");

        foreach (JsonElement preset in configurePresets.Where(preset =>
            preset.GetProperty("name").GetString()!.EndsWith("-physx", StringComparison.Ordinal)))
        {
            string name = preset.GetProperty("name").GetString()!;
            string rid = name[..^"-physx".Length];

            await Assert.That(preset.GetProperty("inherits").GetString())
                .IsEqualTo(rid)
                .Because($"{name} must inherit the platform preset for {rid}");

            JsonElement cache = preset.GetProperty("cacheVariables");
            await Assert.That(cache.GetProperty("OPENUSD_WITH_VULKAN").GetString())
                .IsEqualTo("OFF")
                .Because($"{name} must not inherit a Vulkan requirement the physics shim never uses");
            await Assert.That(cache.GetProperty("OPENUSD_WITH_PHYSX").GetString()).IsEqualTo("ON");

            string prefixPath = cache.GetProperty("CMAKE_PREFIX_PATH").GetString()!;
            await Assert.That(prefixPath)
                .Contains("$env{OPENUSD_ROOT}", StringComparison.Ordinal)
                .Because($"{name} must resolve the locked OpenUSD install");
            await Assert.That(prefixPath)
                .Contains($"install/physx/{rid}", StringComparison.Ordinal)
                .Because($"{name} must resolve the quarantined PhysX install for its own RID");
            await Assert.That(prefixPath)
                .Contains($"build/physx/{rid}/vcpkg_installed", StringComparison.Ordinal)
                .Because($"{name} must resolve the vcpkg tree that carries the port config");
        }

        // The build presets CI invokes have to exist and point at these configure presets.
        string[] physicsBuildPresets =
        [
            .. presets.RootElement
                .GetProperty("buildPresets")
                .EnumerateArray()
                .Where(preset =>
                    preset.GetProperty("name").GetString()!
                        .EndsWith("-physx", StringComparison.Ordinal))
                .Select(preset => preset.GetProperty("configurePreset").GetString()!),
        ];
        await Assert.That(physicsBuildPresets).IsEquivalentTo(ExpectedPhysicsPresets);
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
