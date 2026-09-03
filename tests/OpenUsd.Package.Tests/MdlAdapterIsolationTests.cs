// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenUsd.Package.Tests;

/// <summary>
/// Keeps the optional MDL slice out of every base package and keeps its pinned
/// provenance honest.
/// </summary>
/// <remarks>
/// The MDL slice is optional in three independent senses, and each one is a
/// separate way to get it wrong: the adapter is a separate build target, no
/// base package may carry its binary, and no NVIDIA MDL SDK binary may reach
/// any package at all. Asserting only the build option would leave the packaging
/// side unproven, which is precisely where an optional dependency usually leaks:
/// a glob that sweeps a shim prefix picks up whatever a developer built there.
/// The packing half of that second sense is proven by
/// <c>RuntimePackageTests.WindowsBasePackagesExcludeABuiltMdlAdapter</c>, which
/// stages a built adapter into the shim prefix and packs the base packages from
/// it.
/// </remarks>
public sealed class MdlAdapterIsolationTests
{
    /// <summary>
    /// The environment variables the hdSilk-side adapter loader is permitted to
    /// read. <c>OPENUSD_MDL_SDK_RUNTIME</c> is deliberately absent: it is read
    /// by the SDK-backed adapter itself, not by the loader.
    /// </summary>
    private static readonly string[] ExpectedLoaderEnvironment =
        ["OPENUSD_MDL_ADAPTER_PATH", "OPENUSD_MDL_MODULE_PATH"];

    [Test]
    public async Task MdlLockPinsAVerifiedSourceReleaseAndLicense()
    {
        string root = FindRepositoryRoot();
        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(root, "eng", "mdl.lock.json")));
        JsonElement sdk = document.RootElement.GetProperty("mdlSdk");

        await Assert.That(sdk.GetProperty("repository").GetString())
            .IsEqualTo("https://github.com/NVIDIA/MDL-SDK");
        await Assert.That(sdk.GetProperty("release").GetString()).IsNotNull();
        // A 40-character tag commit is the checkable identity of the pinned
        // release. A pin without one names a moving target.
        string commit = sdk.GetProperty("commit").GetString() ?? string.Empty;
        await Assert.That(Regex.IsMatch(
                commit,
                "^[0-9a-f]{40}$",
                RegexOptions.None,
                TimeSpan.FromSeconds(5)))
            .IsTrue();

        JsonElement license = sdk.GetProperty("license");
        // Verified against the repository's own LICENSE.md at the pinned tag.
        // The MDL SDK is BSD-3-Clause; pinning it as Apache-2.0 would misstate
        // the obligations the SDK-backed build takes on.
        await Assert.That(license.GetProperty("spdx").GetString()).IsEqualTo("BSD-3-Clause");
        await Assert.That(license.GetProperty("blobSha").GetString()).IsNotNull();

        // Every acquirable asset must carry a digest that was recorded before the
        // download, not computed from it: a hash taken from whatever arrived
        // attests to nothing.
        foreach (JsonElement asset in sdk.GetProperty("prebuiltAssets").EnumerateArray())
        {
            string digest = asset.GetProperty("sha256").GetString() ?? string.Empty;
            await Assert.That(Regex.IsMatch(
                    digest,
                    "^[0-9a-f]{64}$",
                    RegexOptions.None,
                    TimeSpan.FromSeconds(5)))
                .IsTrue()
                .Because($"{asset.GetProperty("name").GetString()} needs a recorded SHA-256");
        }

        JsonElement acquisition = sdk.GetProperty("acquisition");
        await Assert.That(acquisition.GetProperty("script").GetString())
            .IsEqualTo("eng/fetch-mdl-sdk.ps1");
        // Acquisition is one thing and redistribution is another. Conflating them
        // is how an NVIDIA binary ends up in a package.
        JsonElement redistribution = acquisition.GetProperty("redistribution");
        await Assert.That(redistribution.GetProperty("permitted").GetBoolean()).IsFalse();
        await Assert.That(redistribution.GetProperty("packagingStatus").GetString())
            .IsEqualTo("not-supported");

        JsonElement adapter = document.RootElement.GetProperty("adapter");
        await Assert.That(adapter.GetProperty("shippedInBasePackages").GetBoolean()).IsFalse();
        await Assert.That(adapter.GetProperty("linksMdlSdk").GetBoolean()).IsFalse();
        await Assert.That(adapter.GetProperty("default").GetString()).IsEqualTo("off");
    }

    [Test]
    public async Task MdlLockSeparatesTheSdkBackedAdapterFromTheDependencyFreeOne()
    {
        string root = FindRepositoryRoot();
        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(root, "eng", "mdl.lock.json")));

        JsonElement[] adapters =
        [
            .. document.RootElement.GetProperty("adapters").EnumerateArray(),
        ];
        JsonElement plain = adapters.Single(
            entry => entry.GetProperty("name").GetString() == "openusd_mdl");
        JsonElement sdkBacked = adapters.Single(
            entry => entry.GetProperty("name").GetString() == "openusd_mdl_sdk");

        // The dependency-free adapter must stay dependency free even in a tree
        // where the SDK was fetched. That is the whole reason there are two
        // targets rather than one behind a compile flag.
        await Assert.That(plain.GetProperty("sdkBacked").GetBoolean()).IsFalse();
        await Assert.That(plain.GetProperty("linksMdlSdk").GetBoolean()).IsFalse();

        await Assert.That(sdkBacked.GetProperty("sdkBacked").GetBoolean()).IsTrue();
        // Even the SDK-backed adapter links nothing: neuraylib is header-only and
        // the runtime is opened at run time, so no MDL SDK code is built into it.
        await Assert.That(sdkBacked.GetProperty("linksMdlSdk").GetBoolean()).IsFalse();
        await Assert.That(sdkBacked.GetProperty("shippedInAnyPackage").GetBoolean()).IsFalse();

        string[] notImplemented =
        [
            .. sdkBacked.GetProperty("notImplemented")
                .EnumerateArray()
                .Select(value => value.GetString() ?? string.Empty),
        ];
        // These must stay named as unimplemented for as long as they are. A
        // capability list that quietly grows is how an honest claim rots.
        await Assert.That(notImplemented.Any(entry => entry.Contains(
                "shader code",
                StringComparison.OrdinalIgnoreCase)))
            .IsTrue();
        await Assert.That(notImplemented.Any(entry => entry.Contains(
                "layered BSDF",
                StringComparison.OrdinalIgnoreCase)))
            .IsTrue();
    }

    [Test]
    public async Task SdkBackedAdapterIsASeparateTargetWithNoMdlLinkLine()
    {
        string root = FindRepositoryRoot();
        string lists = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "openusd_mdl", "CMakeLists.txt"));

        await Assert.That(lists.Contains(
                "add_library(openusd_mdl SHARED",
                StringComparison.Ordinal))
            .IsTrue();
        await Assert.That(lists.Contains(
                "add_library(openusd_mdl_sdk SHARED",
                StringComparison.Ordinal))
            .IsTrue();
        // The SDK-backed target must be guarded by the SDK root, so a tree
        // without one still builds exactly the dependency-free adapter.
        Match guarded = Regex.Match(
            lists,
            @"if\(OPENUSD_MDL_SDK_ROOT\)(?<body>.*?)\nendif\(\)",
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(5));
        await Assert.That(guarded.Success).IsTrue();
        await Assert.That(guarded.Groups["body"].Value.Contains(
                "add_library(openusd_mdl_sdk SHARED",
                StringComparison.Ordinal))
            .IsTrue();

        // No target may link an MDL SDK import library. The check reads the
        // arguments after the target name, because the SDK-backed target is
        // itself called openusd_mdl_sdk and would otherwise match its own name.
        foreach (Match match in Regex.Matches(
            lists,
            @"target_link_libraries\(\s*(?<target>\S+)(?<arguments>[^)]*)\)",
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(5)))
        {
            await Assert.That(match.Groups["arguments"].Value.Contains(
                    "mdl",
                    StringComparison.OrdinalIgnoreCase))
                .IsFalse()
                .Because("the MDL SDK runtime is loaded at run time, never linked");
        }
    }

    [Test]
    public async Task MdlLockAdapterAbiMatchesTheProjectOwnedHeader()
    {
        string root = FindRepositoryRoot();
        string header = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "include", "openusd_mdl.h"));
        Match match = Regex.Match(
            header,
            @"#define\s+OPENUSD_MDL_ABI_VERSION\s+(\d+)u",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        await Assert.That(match.Success).IsTrue();

        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(root, "eng", "mdl.lock.json")));
        await Assert.That(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .IsEqualTo(document.RootElement.GetProperty("adapter").GetProperty("abi").GetInt32());
    }

    [Test]
    public async Task AdapterLoaderResolvesOnlyAbsolutePathsWithSafeSearchFlags()
    {
        string root = FindRepositoryRoot();
        string loader = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "hdSilk", "src", "mdlAdapter.cpp"));

        // A shared library loaded by bare name is a shared library an attacker
        // can substitute, because the platform search can include directories
        // this process does not control. The loader must therefore never hand a
        // bare name to LoadLibrary/dlopen, and must state the two search flags
        // that replace the legacy Windows search outright.
        await Assert.That(loader.Contains(
                "LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS",
                StringComparison.Ordinal))
            .IsTrue();
        await Assert.That(Regex.IsMatch(
                loader,
                @"\bLoadLibraryW?\s*\(",
                RegexOptions.None,
                TimeSpan.FromSeconds(5)))
            .IsFalse()
            .Because("only LoadLibraryExW with explicit search flags may be used");
        await Assert.That(loader.Contains("IsAbsolutePath(configuredPath)", StringComparison.Ordinal))
            .IsTrue()
            .Because("OPENUSD_MDL_ADAPTER_PATH must be rejected when it is relative");
        await Assert.That(loader.Contains("GetHostModuleDirectory()", StringComparison.Ordinal))
            .IsTrue()
            .Because("the default location must be derived from the hosting module's own path");

        // Exactly one environment variable, read by name. ArchEnviron would hand
        // the whole environment block to a diagnostic, which is how unrelated
        // secrets end up in logs.
        await Assert.That(Regex.Count(
                loader,
                @"ArchGetEnv\(",
                RegexOptions.None,
                TimeSpan.FromSeconds(5)))
            .IsEqualTo(2);
        await Assert.That(loader.Contains("ArchEnviron", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task MdlLockRecordsTheLoaderContractAndTheGatedRids()
    {
        string root = FindRepositoryRoot();
        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(root, "eng", "mdl.lock.json")));
        JsonElement adapter = document.RootElement.GetProperty("adapter");

        await Assert.That(adapter.GetProperty("sdkBacked").GetBoolean())
            .IsFalse()
            .Because("OpenUsd ships an authored-value distillation foundation, not an MDL " +
                "SDK-backed adapter");

        JsonElement loader = adapter.GetProperty("loader");
        await Assert.That(loader.GetProperty("bareLibraryNameLoad").GetBoolean()).IsFalse();
        string[] flags =
        [
            .. loader.GetProperty("windowsSearchFlags")
                .EnumerateArray()
                .Select(value => value.GetString() ?? string.Empty),
        ];
        await Assert.That(flags).Contains("LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR");
        await Assert.That(flags).Contains("LOAD_LIBRARY_SEARCH_DEFAULT_DIRS");
        string[] environment =
        [
            .. loader.GetProperty("environmentRead")
                .EnumerateArray()
                .Select(value => value.GetString() ?? string.Empty),
        ];
        await Assert.That(environment).IsEquivalentTo(ExpectedLoaderEnvironment);

        // The gated set must be a subset of the buildable set, and it must match
        // what a workflow actually builds. Claiming a RID no job builds is the
        // failure mode this check exists for.
        string[] buildable =
        [
            .. adapter.GetProperty("buildableRids")
                .EnumerateArray()
                .Select(value => value.GetString() ?? string.Empty),
        ];
        string[] gated =
        [
            .. adapter.GetProperty("gatedRids")
                .EnumerateArray()
                .Select(value => value.GetString() ?? string.Empty),
        ];
        await Assert.That(gated).IsNotEmpty();
        foreach (string rid in gated)
        {
            await Assert.That(buildable).Contains(rid);
        }

        string workflow = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "native.yml"));
        foreach (string rid in gated)
        {
            await Assert.That(workflow.Contains(
                    $"./eng/build-mdl-shim.ps1 -Rid {rid} -RunProbe",
                    StringComparison.Ordinal))
                .IsTrue()
                .Because($"eng/mdl.lock.json claims {rid} is gated, so a workflow must build it");
        }

        // A RID that is buildable but not gated must not be claimed as evidence.
        using JsonDocument manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(root, "eng", "support-manifest.json")));
        JsonElement distillation = manifest.RootElement
            .GetProperty("areas")
            .EnumerateArray()
            .Single(area => area.GetProperty("id").GetString() == "rendering")
            .GetProperty("entries")
            .EnumerateArray()
            .Single(entry =>
                entry.GetProperty("id").GetString() == "mdl-accepted-subset-distillation");
        string[] platforms =
        [
            .. distillation.GetProperty("evidencePlatforms")
                .EnumerateArray()
                .Select(value => value.GetString() ?? string.Empty),
        ];
        await Assert.That(platforms).IsEquivalentTo(gated);
    }

    [Test]
    public async Task AdapterBuildIsOptionalAndOffByDefault()
    {
        string root = FindRepositoryRoot();
        string lists = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "CMakeLists.txt"));

        await Assert.That(Regex.IsMatch(
                lists,
                @"option\(OPENUSD_WITH_MDL\s+""[^""]*""\s+OFF\)",
                RegexOptions.None,
                TimeSpan.FromSeconds(5)))
            .IsTrue()
            .Because(
                "the MDL adapter must be an explicit opt-in; a default-on option would put it " +
                "into every native build and from there into a package layout by accident");

        await Assert.That(lists.Contains("add_subdirectory(openusd_mdl)", StringComparison.Ordinal))
            .IsTrue();
        // The subdirectory must be guarded. A bare add_subdirectory would build
        // the adapter unconditionally whatever the option says.
        Match guarded = Regex.Match(
            lists,
            @"if\(OPENUSD_WITH_MDL\)(?<body>.*?)endif\(\)",
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(5));
        await Assert.That(guarded.Success).IsTrue();
        await Assert.That(guarded.Groups["body"].Value
                .Contains("add_subdirectory(openusd_mdl)", StringComparison.Ordinal))
            .IsTrue();
    }

    [Test]
    public async Task AdapterLinksNoMdlSdkOrOpenUsdLibrary()
    {
        string root = FindRepositoryRoot();
        string lists = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "openusd_mdl", "CMakeLists.txt"));

        // The dependency-free adapter's link line must stay empty. Scoping the
        // claim to the text outside the SDK guard is the point: the SDK-backed
        // sibling does link the platform dl library, and conflating the two
        // would either weaken this check or fail it for the wrong reason.
        string outsideSdkGuard = Regex.Replace(
            lists,
            @"if\(OPENUSD_MDL_SDK_ROOT\).*?\nendif\(\)",
            string.Empty,
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(5));
        await Assert.That(outsideSdkGuard.Contains(
                "target_link_libraries",
                StringComparison.Ordinal))
            .IsFalse()
            .Because(
                "the dependency-free adapter must stay dependency-free so that 'no MDL or " +
                "NVIDIA binary ships' is verifiable by inspecting the built artifact rather " +
                "than by reading a build description");

        string source = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "openusd_mdl", "src", "adapter.cpp"));
        foreach (string forbidden in new[] { "mi/mdl", "mi/neuraylib", "pxr/", "MaterialX" })
        {
            await Assert.That(source.Contains(
                    $"#include <{forbidden}",
                    StringComparison.Ordinal))
                .IsFalse();
            await Assert.That(source.Contains(
                    $"#include \"{forbidden}",
                    StringComparison.Ordinal))
                .IsFalse();
        }

        // Every MDL SDK type is confined to one translation unit, which is what
        // lets the same adapter source build with and without an SDK.
        string backend = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "openusd_mdl", "src", "sdk_backend.cpp"));
        await Assert.That(backend.Contains(
                "#if defined(OPENUSD_MDL_WITH_SDK)",
                StringComparison.Ordinal))
            .IsTrue();
        string header = await File.ReadAllTextAsync(
            Path.Combine(root, "native", "openusd_mdl", "src", "sdk_backend.h"));
        await Assert.That(header.Contains("mi/", StringComparison.Ordinal))
            .IsFalse()
            .Because("no MDL SDK type may appear in a header the rest of the adapter includes");
    }

    [Test]
    public async Task ReleaseSbomCarriesNoMdlOrNvidiaComponent()
    {
        string root = FindRepositoryRoot();
        using JsonDocument sbom = JsonDocument.Parse(
            await File.ReadAllTextAsync(
                Path.Combine(root, "eng", "sbom", "openusd-release.cdx.json")));

        List<string> offenders = [];
        if (sbom.RootElement.TryGetProperty("components", out JsonElement components))
        {
            foreach (JsonElement item in components.EnumerateArray())
            {
                string name = item.TryGetProperty("name", out JsonElement nameElement)
                    ? nameElement.GetString() ?? string.Empty
                    : string.Empty;
                if (Regex.IsMatch(
                    name,
                    @"\b(mdl|mdl-sdk|nvidia)\b",
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(5)))
                {
                    offenders.Add(name);
                }
            }
        }

        await Assert.That(offenders)
            .IsEmpty()
            .Because(
                "the release SBOM describes what this repository ships, and it ships no MDL SDK " +
                "and no NVIDIA binary: " + string.Join(", ", offenders));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the OpenUsd repository root.");
    }
}
