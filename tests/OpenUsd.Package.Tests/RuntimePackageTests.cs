// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OpenUsd.Package.Tests;

[NotInParallel]
public sealed class RuntimePackageTests
{
    private const string RequiredExecutionEnvironmentVariable =
        "OPENUSD_PACKAGE_EXECUTION_REQUIRED";

    // Read from eng/openusd.lock.json rather than restated here. These moved on
    // every schema merge of the current wave, and because nothing in an
    // ordinary local run executes the package suite, a stale value surfaced
    // only as a CI failure whose visible symptom was four compiler warnings and
    // a bare "exit code 2" from a generated consumer -- with the actual cause,
    // an ABI comparison that could never be true, invisible.
    //
    // The lock is a sound source: OpenUsd.Native.Tests independently requires
    // it to agree with the C header and common.h, so all four move together or
    // something fails in milliseconds.
    private static readonly uint RequiredDataAbiVersion =
        (uint)ReadLockNumber("data");
    private static readonly ulong RequiredDataCapabilities =
        ReadLockNumber("dataCapabilities");

    // "Stale" means the contract immediately before the newest capability was
    // added, which is what a consumer built against the previous package has.
    // Clearing the highest set bit reproduces the values these constants
    // carried by hand: 0x3FFF became 0x2FFF, and 0x7FFF becomes 0x3FFF.
    private static readonly uint PreviousDataAbiVersion =
        RequiredDataAbiVersion - 1;
    private static readonly ulong PreviousDataCapabilities =
        RequiredDataCapabilities & ~HighestSetBit(RequiredDataCapabilities);

    private const int RequiredStormAbiVersion = 8;
    private const int RequiredSilkSessionAbiVersion = 5;
    private const int RequiredSilkPageAbiVersion = 15;
    private const int RequiredStormChildAbiVersion = 8;
    private const int RequiredStormChildNavigationInputVersion = 2;

    // Kept in step with eng/openusd.lock.json by
    // OpenUsd.Physics.Tests.PhysicsAbiLockContractTests, which runs on an ordinary push. This
    // suite needs a built native install, so a constant only checked here would drift for a
    // whole wave before anything noticed.
    private const int RequiredPhysicsAbiVersion = 7;
    private const int RequiredPhysicsExtractAbiVersion = 1;

    private static readonly string[] ExpectedPhysicsRidPackages =
    [
        "OpenUsd.Runtime.Physics.win-x64",
        "OpenUsd.Runtime.Physics.linux-x64",
    ];

    /// <summary>The RIDs the pinned vcpkg PhysX port actually builds for.</summary>
    private static readonly string[] SupportedPhysicsRids = ["win-x64", "linux-x64"];

    private static ulong HighestSetBit(ulong value) =>
        value == 0 ? 0 : 1UL << (63 - System.Numerics.BitOperations.LeadingZeroCount(value));

    private static ulong ReadLockNumber(string name)
    {
        string path = Path.Combine(FindRepositoryRoot(), "eng", "openusd.lock.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("abi").GetProperty(name).GetUInt64();
    }

    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly ExecutionPlatform[] SupportedExecutionPlatforms =
    [
        new(
            "win-x64",
            "OpenUsd.Rendering.Silk.D3D12",
            "OpenUsd.Rendering.Silk.D3D12",
            "D3D12SilkGraphicsDevice.Create(useWarp: true)",
            "D3D12",
            "D3D12_WARP",
            "D3D12_WARP_UPLOAD",
            "OperatingSystem.IsWindows()",
            "openusd_dotnet.dll",
            "usd_ms.dll",
            "openusd_hydra.dll",
            "openusd_hdsilk.dll",
            "openusd_storm_child.dll",
            "../../../openusd_hdsilk.dll",
            RequiresSwiftShader: false),
        new(
            "linux-x64",
            "OpenUsd.Rendering.Silk.Vulkan",
            "OpenUsd.Rendering.Silk.Vulkan",
            "VulkanSilkGraphicsDevice.Create()",
            "Vulkan",
            "VULKAN_SWIFTSHADER",
            "VULKAN_SWIFTSHADER_UPLOAD",
            "OperatingSystem.IsLinux()",
            "libopenusd_dotnet.so",
            "libusd_ms.so",
            "libopenusd_hydra.so",
            "libopenusd_hdsilk.so",
            "libopenusd_storm_child.so",
            "../../../libopenusd_hdsilk.so",
            RequiresSwiftShader: true),
        new(
            "osx-arm64",
            "OpenUsd.Rendering.Silk.Metal",
            "OpenUsd.Rendering.Silk.Metal",
            "MetalSilkGraphicsDevice.Create()",
            "Metal",
            "METAL",
            "METAL_UPLOAD",
            "OperatingSystem.IsMacOS()",
            "libopenusd_dotnet.dylib",
            "libusd_ms.dylib",
            "libopenusd_hydra.dylib",
            "libopenusd_hdsilk.dylib",
            "libopenusd_storm_child.dylib",
            "../../../libopenusd_hdsilk.dylib",
            RequiresSwiftShader: false),
    ];

    [Test]
    public async Task RuntimePackageProjectsCoverSupportedMatrix()
    {
        string repositoryRoot = FindRepositoryRoot();
        // Stated as a map rather than derived from an id prefix, so a new family can never be
        // folded into an existing metapackage by name resemblance alone.
        Dictionary<string, string[]> metapackages = new(StringComparer.Ordinal)
        {
            ["OpenUsd.Runtime.Core"] =
            [
                "OpenUsd.Runtime.Core.win-x64",
                "OpenUsd.Runtime.Core.linux-x64",
                "OpenUsd.Runtime.Core.osx-arm64",
            ],
            ["OpenUsd.Runtime.Imaging"] =
            [
                "OpenUsd.Runtime.Imaging.win-x64",
                "OpenUsd.Runtime.Imaging.linux-x64",
                "OpenUsd.Runtime.Imaging.osx-arm64",
            ],
            ["OpenUsd.Runtime.Cesium"] =
            [
                "OpenUsd.Runtime.Cesium.win-x64",
                "OpenUsd.Runtime.Cesium.linux-x64",
                "OpenUsd.Runtime.Cesium.osx-arm64",
            ],
            // Two RIDs, not three. The pinned vcpkg PhysX port declares
            // "(windows & x64 & !mingw & !uwp) | (linux & x64) | (linux & arm64)", so there is no
            // arm64-osx simulation SDK to package. macOS is absent by platform fact rather than
            // by omission, and no empty placeholder stands in for it.
            ["OpenUsd.Runtime.Physics"] =
            [
                "OpenUsd.Runtime.Physics.win-x64",
                "OpenUsd.Runtime.Physics.linux-x64",
            ],
        };
        string[] ridPackages = [.. metapackages.Values.SelectMany(ids => ids)];

        await Assert.That(AllRuntimePackageIds().Order(StringComparer.Ordinal).ToArray())
            .IsEquivalentTo(
                metapackages.Keys.Concat(ridPackages).Order(StringComparer.Ordinal).ToArray());

        foreach (string packageId in metapackages.Keys.Concat(ridPackages))
        {
            string projectPath = Path.Combine(repositoryRoot, "src", packageId, $"{packageId}.csproj");
            await Assert.That(File.Exists(projectPath)).IsTrue();

            XDocument project = XDocument.Load(projectPath);
            string? declaredPackageId = project.Descendants("PackageId").SingleOrDefault()?.Value;
            await Assert.That(declaredPackageId).IsEqualTo(packageId);
            await Assert.That(project.Descendants("IncludeBuildOutput").Single().Value).IsEqualTo("false");

            if (ridPackages.Contains(packageId, StringComparer.Ordinal))
            {
                string? runtimeRid = project.Descendants("RuntimePackageRid").SingleOrDefault()?.Value;
                string expectedRid = packageId[(packageId.LastIndexOf('.') + 1)..];

                await Assert.That(runtimeRid).IsEqualTo(expectedRid);

                string targetsPath = Path.Combine(
                    repositoryRoot,
                    "src",
                    packageId,
                    "buildTransitive",
                    $"{packageId}.targets");
                await Assert.That(File.Exists(targetsPath)).IsTrue();
            }
            else
            {
                string[] dependencies = project
                    .Descendants("ProjectReference")
                    .Select(element => Path.GetFileNameWithoutExtension(
                        element.Attribute("Include")?.Value)!)
                    .ToArray();
                await Assert.That(dependencies).IsEquivalentTo(metapackages[packageId]);
            }
        }
    }

    [Test]
    public async Task ConsumerDocumentationListsRuntimeChoicesAtPackageReferencePoint()
    {
        string repositoryRoot = FindRepositoryRoot();
        string[] documentPaths =
        [
            "README.md",
            Path.Combine("docs", "packaging.md"),
            Path.Combine("samples", "README.md"),
        ];

        foreach (string relativePath in documentPaths)
        {
            string text = await File.ReadAllTextAsync(Path.Combine(repositoryRoot, relativePath));
            foreach (string packageId in AllRuntimePackageIds())
            {
                await Assert.That(text).Contains(packageId);
            }
        }
    }

    [Test]
    public async Task RuntimeMetapackagesDependOnEverySupportedRidPackage()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);

        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);

            PackedPackage corePackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Core",
                packageRoot);
            PackedPackage imagingPackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Imaging",
                packageRoot);
            PackedPackage cesiumPackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Cesium",
                packageRoot);
            PackedPackage physicsPackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Physics",
                packageRoot);

            await AssertPackageDependenciesAsync(
                corePackage.Path,
                corePackage.Version,
                GetCoreMetaPackageGraph().Where(id => id != "OpenUsd.Runtime.Core").ToArray());
            await AssertPackageDependenciesAsync(
                imagingPackage.Path,
                imagingPackage.Version,
                AllRuntimePackageIds()
                    .Where(id => id.StartsWith("OpenUsd.Runtime.Imaging.", StringComparison.Ordinal))
                    .ToArray());
            await AssertPackageDependenciesAsync(
                cesiumPackage.Path,
                cesiumPackage.Version,
                AllRuntimePackageIds()
                    .Where(id => id.StartsWith("OpenUsd.Runtime.Cesium.", StringComparison.Ordinal))
                    .ToArray());
            // Exactly the two RIDs the pinned simulation SDK builds for, and nothing else. An
            // extra entry here would mean a consumer restoring a package that cannot exist.
            await AssertPackageDependenciesAsync(
                physicsPackage.Path,
                physicsPackage.Version,
                [
                    "OpenUsd.Runtime.Physics.win-x64",
                    "OpenUsd.Runtime.Physics.linux-x64",
                ],
                exact: true);
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task NativePackageValidatorsTrackCurrentStormChildAbiContract()
    {
        string repositoryRoot = FindRepositoryRoot();
        int stormChildAbiVersion = ReadStormChildAbiVersion(repositoryRoot);
        foreach (string validatorName in new[]
        {
            "Validate-LinuxNativePackage.ps1",
            "Validate-MacOsNativePackage.ps1",
        })
        {
            string validator = await File.ReadAllTextAsync(Path.Combine(
                repositoryRoot,
                "src",
                "OpenUsd.Runtime.Packaging",
                validatorName));
            await Assert.That(validator)
                .Contains($"$requiredStormChildAbiVersion = {stormChildAbiVersion}");
        }
    }

    private static async Task AssertMacOsValidationEvidenceAsync(string packagePath)
    {
        int stormChildAbiVersion = ReadStormChildAbiVersion(FindRepositoryRoot());
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry evidenceEntry = package.Entries.Single(
            entry => entry.FullName ==
                "build/OpenUsd.Runtime.Imaging.osx-arm64.native-validation.json");
        using Stream evidenceStream = evidenceEntry.Open();
        using JsonDocument evidence = await JsonDocument.ParseAsync(evidenceStream);
        JsonElement root = evidence.RootElement;
        await Assert.That(root.GetProperty("schemaVersion").GetInt32()).IsEqualTo(2);
        await Assert.That(root.GetProperty("rid").GetString()).IsEqualTo("osx-arm64");
        await Assert.That(root.GetProperty("stormChildAbiVersion").GetInt32())
            .IsEqualTo(stormChildAbiVersion);
        JsonElement rpathPolicy = root.GetProperty("rpathPolicy");
        await Assert.That(
            rpathPolicy.GetProperty("exactAllowlist")[0].GetString()).IsEqualTo("@loader_path");
        await Assert.That(
            rpathPolicy.GetProperty("exactAllowlist").GetArrayLength()).IsEqualTo(1);
        string[] exports = root
            .GetProperty("stormChildExports")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        await Assert.That(exports).Contains("openusd_storm_child_get_abi_version");
        await Assert.That(exports).Contains("openusd_storm_child_render_v2");
        await Assert.That(exports).Contains("openusd_storm_child_request_frame_v3");
        await Assert.That(exports).Contains("openusd_storm_child_pick");
        await Assert.That(exports).Contains("openusd_storm_child_set_selection");
        await Assert.That(exports).Contains("openusd_storm_child_get_navigation_input");
        await Assert.That(exports).Contains("openusd_storm_child_capture_framebuffer");

        JsonElement[] libraries = root.GetProperty("libraries").EnumerateArray().ToArray();
        await Assert.That(libraries.Length).IsEqualTo(3);
        foreach (JsonElement library in libraries)
        {
            string name = library.GetProperty("name").GetString()!;
            await Assert.That(library.GetProperty("installName").GetString())
                .IsEqualTo($"@rpath/{name}");
            string[] rpaths = library
                .GetProperty("rpaths")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray();
            await Assert.That(rpaths).IsEquivalentTo(["@loader_path"]);
        }
    }

    private static MacLoadedImageValidation ValidateMacLoadedImages(
        string publishRoot,
        IEnumerable<string> imagePaths,
        bool requireStormAndCore,
        string? requiredExecutableName = null)
    {
        string canonicalRoot = Path.GetFullPath(publishRoot)
            .TrimEnd(Path.DirectorySeparatorChar);
        string rootPrefix = canonicalRoot + Path.DirectorySeparatorChar;
        var projectImages = new List<string>();
        bool confined = true;
        bool stormLoaded = false;
        bool coreLoaded = false;
        bool dotNetLoaded = false;
        bool executableLoaded = false;
        foreach (string imagePath in imagePaths)
        {
            string fileName = Path.GetFileName(imagePath);
            bool isExecutable = !string.IsNullOrEmpty(requiredExecutableName) &&
                string.Equals(fileName, requiredExecutableName, StringComparison.Ordinal);
            if (!IsProjectOpenUsdLibrary(fileName) && !isExecutable)
            {
                continue;
            }

            string canonicalPath = Path.GetFullPath(imagePath);
            projectImages.Add(canonicalPath);
            confined &= IsMacLoadedImageUnderAppBase(rootPrefix, canonicalPath);
            stormLoaded |= string.Equals(
                fileName,
                "libopenusd_storm_child.dylib",
                StringComparison.Ordinal);
            coreLoaded |= string.Equals(fileName, "libusd_ms.dylib", StringComparison.Ordinal);
            dotNetLoaded |= string.Equals(
                fileName,
                "libopenusd_dotnet.dylib",
                StringComparison.Ordinal);
            executableLoaded |= isExecutable;
        }

        if (requireStormAndCore)
        {
            confined &= stormLoaded &&
                coreLoaded &&
                dotNetLoaded &&
                executableLoaded;
        }
        return new MacLoadedImageValidation(
            canonicalRoot,
            confined,
            projectImages.ToArray(),
            stormLoaded,
            coreLoaded,
            dotNetLoaded,
            executableLoaded);
    }

    private static bool IsMacLoadedImageUnderAppBase(
        string appBasePrefix,
        string canonicalPath)
    {
        string normalized = canonicalPath.Replace('\\', '/');
        return canonicalPath.StartsWith(appBasePrefix, StringComparison.Ordinal) &&
            !normalized.Contains("/native/install/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("/native/build/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("/src/", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("/source/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProjectOpenUsdLibrary(string fileName) =>
        fileName.StartsWith("libopenusd_", StringComparison.OrdinalIgnoreCase) ||
        fileName.StartsWith("libusd_", StringComparison.OrdinalIgnoreCase);

    private static void ValidateMacCodeSignEvidence(
        IReadOnlyCollection<MacCodeSignEvidence> evidence,
        int expectedCount)
    {
        var failures = new List<string>();
        if (evidence.Count != expectedCount)
        {
            failures.Add($"Expected {expectedCount} signed paths, found {evidence.Count}.");
        }
        foreach (MacCodeSignEvidence item in evidence)
        {
            if (!item.Verified)
            {
                failures.Add($"codesign strict verification failed: {item.Path}");
            }
            if (!item.Hardened)
            {
                failures.Add($"Hardened runtime is missing: {item.Path}");
            }
            if (item.Sha256.Length != 64 ||
                item.Sha256.Any(character =>
                    !Uri.IsHexDigit(character) || char.IsLower(character)))
            {
                failures.Add($"Post-sign SHA-256 is invalid: {item.Path}");
            }
        }
        if (failures.Count != 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        }
    }

    [Test]
    public async Task LinuxElfRunpathParserRejectsUnsafeDynamicSections()
    {
        string repositoryRoot = FindRepositoryRoot();
        CommandResult result = await RunProcessAsync(
            "pwsh",
            repositoryRoot,
            [
                "-NoProfile",
                "-File",
                "src/OpenUsd.Runtime.Packaging/Test-LinuxElfValidation.ps1",
            ],
            nugetPackagesRoot: null,
            sanitizeRuntimeEnvironment: false,
            runtimeEnvironment: null);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output).Contains(
            "Linux ELF DT_RUNPATH and ABI-8 DT_SONAME parser tests passed.");

        CommandResult topologyResult = await RunProcessAsync(
            "pwsh",
            repositoryRoot,
            [
                "-NoProfile",
                "-File",
                "src/OpenUsd.Runtime.Packaging/Test-LinuxStormChildTopology.ps1",
            ],
            nugetPackagesRoot: null,
            sanitizeRuntimeEnvironment: false,
            runtimeEnvironment: null);
        await Assert.That(topologyResult.ExitCode).IsEqualTo(0);
        await Assert.That(topologyResult.Output).Contains(
            "Linux Storm child ABI-8 SONAME topology tests passed.");

        CommandResult evidenceResult = await RunProcessAsync(
            "pwsh",
            repositoryRoot,
            [
                "-NoProfile",
                "-File",
                "eng/test-linux-package-evidence.ps1",
            ],
            nugetPackagesRoot: null,
            sanitizeRuntimeEnvironment: false,
            runtimeEnvironment: null);
        await Assert.That(evidenceResult.ExitCode).IsEqualTo(0);
        await Assert.That(evidenceResult.Output).Contains(
            "Synthetic Linux package evidence schema/hash/topology tests passed.");
    }

    [Test]
    public async Task MacOsMachOParserRejectsUnsafeRPathsAndDependencies()
    {
        string repositoryRoot = FindRepositoryRoot();
        CommandResult result = await RunProcessAsync(
            "pwsh",
            repositoryRoot,
            [
                "-NoProfile",
                "-File",
                "src/OpenUsd.Runtime.Packaging/Test-MacOsNativeValidation.ps1",
            ],
            nugetPackagesRoot: null,
            sanitizeRuntimeEnvironment: false,
            runtimeEnvironment: null);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Output).Contains(
            "macOS Mach-O LC_RPATH and dependency parser tests passed.");

        CommandResult evidenceResult = await RunProcessAsync(
            "pwsh",
            repositoryRoot,
            [
                "-NoProfile",
                "-File",
                "eng/validate-macos-package-evidence.ps1",
                "-RunParserTests",
            ],
            nugetPackagesRoot: null,
            sanitizeRuntimeEnvironment: false,
            runtimeEnvironment: null);
        await Assert.That(evidenceResult.ExitCode).IsEqualTo(0);
        await Assert.That(evidenceResult.Output).Contains(
            "macOS package evidence parser/hash tests passed.");
    }

    [Test]
    public async Task MacOsLoadedImageEvidenceRejectsExternalProjectLibrary()
    {
        string publishRoot = Path.GetFullPath(Path.Combine("consumer", "publish"));
        string internalPath = Path.Combine(publishRoot, "libopenusd_dotnet.dylib");
        string externalPath = Path.GetFullPath(Path.Combine("global", "libusd_ms.dylib"));

        await Assert.That(
            ValidateMacLoadedImages(
                publishRoot,
                [internalPath],
                requireStormAndCore: false).Confined).IsTrue();
        await Assert.That(
            ValidateMacLoadedImages(
                publishRoot,
                [internalPath, externalPath],
                requireStormAndCore: false).Confined).IsFalse();
    }

    [Test]
    public async Task MacOsCodeSignEvidenceRejectsUnsignedOrNonHardenedFiles()
    {
        MacCodeSignEvidence[] valid =
        [
            new("libopenusd_dotnet.dylib", new string('A', 64), Verified: true, Hardened: true),
            new("Consumer", new string('B', 64), Verified: true, Hardened: true),
        ];
        ValidateMacCodeSignEvidence(valid, expectedCount: 2);

        await Assert.That(() => ValidateMacCodeSignEvidence(
            [new("libopenusd_dotnet.dylib", new string('A', 64), Verified: false, Hardened: true)],
            expectedCount: 1)).Throws<InvalidOperationException>();
        await Assert.That(() => ValidateMacCodeSignEvidence(
            [new("libopenusd_dotnet.dylib", new string('A', 64), Verified: true, Hardened: false)],
            expectedCount: 1)).Throws<InvalidOperationException>();
        await Assert.That(() => ValidateMacCodeSignEvidence(
            [new("libopenusd_dotnet.dylib", string.Empty, Verified: true, Hardened: true)],
            expectedCount: 1)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MacOsPublishedStormChildIdentityRejectsPreSignMutation()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);
        string installPath = Path.Combine(workRoot, "install", "libopenusd_storm_child.dylib");
        string publishedPath = Path.Combine(workRoot, "publish", "libopenusd_storm_child.dylib");
        string packagePath = Path.Combine(workRoot, "OpenUsd.Runtime.Imaging.osx-arm64.nupkg");
        const string entryPath =
            "runtimes/osx-arm64/native/libopenusd_storm_child.dylib";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(installPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(publishedPath)!);
            byte[] original = "unsigned-storm-child"u8.ToArray();
            await File.WriteAllBytesAsync(installPath, original);
            await File.WriteAllBytesAsync(publishedPath, original);
            using (ZipArchive package = ZipFile.Open(
                packagePath,
                ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = package.CreateEntry(entryPath);
                await using Stream destination = entry.Open();
                await destination.WriteAsync(original);
            }

            MacStormChildIdentity valid =
                await ValidateMacPublishedStormChildIdentityAsync(
                    packagePath,
                    installPath,
                    publishedPath);
            await Assert.That(valid.PackageEntrySha256)
                .IsEqualTo(valid.NativeInstallSha256);
            await Assert.That(valid.PackageEntrySha256)
                .IsEqualTo(valid.PublishedPreSignSha256);

            await File.AppendAllTextAsync(publishedPath, "-altered");
            await Assert.That(() =>
                ValidateMacPublishedStormChildIdentityAsync(
                    packagePath,
                    installPath,
                    publishedPath).GetAwaiter().GetResult())
                .Throws<InvalidOperationException>();
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task WindowsPackagesPreserveLayoutAndPublishFromACleanFeed()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);

        try
        {
            (string installRoot, string shimRoot, string vulkanRuntimeLibrary) =
                CreateSyntheticWindowsInstall(workRoot);
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);

            PackedPackage corePackage = await PackAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Core.win-x64",
                installRoot,
                shimRoot,
                vulkanRuntimeLibrary,
                packageRoot);
            PackedPackage imagingPackage = await PackAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Imaging.win-x64",
                installRoot,
                shimRoot,
                vulkanRuntimeLibrary,
                packageRoot);

            await AssertPackageEntriesAsync(
                corePackage.Path,
                [
                    "buildTransitive/OpenUsd.Runtime.Core.win-x64.targets",
                    "runtimes/win-x64/native/Imath-3_1.dll",
                    "runtimes/win-x64/native/MaterialXCore.dll",
                    "runtimes/win-x64/native/OpenColorIO_2_2.dll",
                    "runtimes/win-x64/native/openusd_dotnet.dll",
                    "runtimes/win-x64/native/usd_ms.dll",
                    "runtimes/win-x64/native/vulkan-1.dll",
                    "runtimes/win-x64/resources/usd/plugInfo.json",
                    "runtimes/win-x64/resources/usd/usd/resources/plugInfo.json",
                ]);
            await AssertPackageEntriesAsync(
                imagingPackage.Path,
                [
                    "buildTransitive/OpenUsd.Runtime.Imaging.win-x64.targets",
                    "runtimes/win-x64/native/openusd_hdsilk.dll",
                    "runtimes/win-x64/native/openusd_hydra.dll",
                    "runtimes/win-x64/native/openusd_storm_child.dll",
                    "runtimes/win-x64/resources/plugin/usd/hdSilk/resources/plugInfo.json",
                    "runtimes/win-x64/resources/plugin/usd/hdStorm/resources/plugInfo.json",
                    "runtimes/win-x64/resources/plugin/usd/plugInfo.json",
                ]);
            await AssertSingleNativePackageEntryAsync(
                imagingPackage.Path,
                "win-x64",
                "openusd_storm_child.dll");
            await AssertPackageDoesNotContainAsync(
                imagingPackage.Path,
                "runtimes/win-x64/resources/bin/openusd_hdsilk.dll");
            await AssertHdSilkPackageAsync(
                imagingPackage.Path,
                "win-x64",
                "openusd_hdsilk.dll",
                "../../../openusd_hdsilk.dll");
            await AssertImagingDependsOnCoreAsync(
                imagingPackage.Path,
                "win-x64",
                corePackage.Version);

            string publishRoot = await PublishConsumerAsync(workRoot, packageRoot, corePackage.Version);
            string[] publishedPaths =
            [
                "MaterialXCore.dll",
                "Imath-3_1.dll",
                "OpenColorIO_2_2.dll",
                "openusd_dotnet.dll",
                "usd_ms.dll",
                Path.Combine("usd", "plugInfo.json"),
                Path.Combine("usd", "usd", "resources", "plugInfo.json"),
            ];
            foreach (string publishedPath in publishedPaths)
            {
                await Assert.That(File.Exists(Path.Combine(publishRoot, publishedPath))).IsTrue();
            }
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task WindowsCesiumPackagePreservesOptInLayout()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);

        try
        {
            (string installRoot, string shimRoot, string vulkanRuntimeLibrary) =
                CreateSyntheticWindowsInstall(workRoot);
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);

            PackedPackage corePackage = await PackAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Core.win-x64",
                installRoot,
                shimRoot,
                vulkanRuntimeLibrary,
                packageRoot);
            PackedPackage cesiumPackage = await PackAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Cesium.win-x64",
                installRoot,
                shimRoot,
                vulkanRuntimeLibrary,
                packageRoot);

            await AssertPackageEntriesAsync(
                cesiumPackage.Path,
                [
                    "buildTransitive/OpenUsd.Runtime.Cesium.win-x64.targets",
                    "runtimes/win-x64/native/openusd_cesium.dll",
                ]);
            await AssertPackageEntryMatchesFileAsync(
                cesiumPackage.Path,
                "runtimes/win-x64/native/openusd_cesium.dll",
                Path.Combine(shimRoot, "bin", "openusd_cesium.dll"));
            await AssertSingleNativePackageEntryAsync(
                cesiumPackage.Path,
                "win-x64",
                "openusd_cesium.dll");
            await AssertPackageDoesNotContainAsync(corePackage.Path, "openusd_cesium");
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    /// <summary>
    /// Requires the physics package to carry the solver alone.
    /// </summary>
    /// <remarks>
    /// Two independent failures are asserted here, and they fail for different reasons. The physics
    /// shim prefix holds a duplicate of every core and imaging shim, and publishing one of those at
    /// an application root replaces the archive-verified binary rather than sitting beside it.
    ///
    /// The same prefix also holds the PhysXGpu and PhysXDevice modules. Those are packman blobs
    /// carrying NVIDIA proprietary terms rather than the BSD-3-Clause PhysX source, and this
    /// project has no agreement to redistribute them, so publishing one is a licensing defect and
    /// not merely a layout defect. The synthetic prefix stages both kinds of file deliberately, so
    /// this test would see either mistake rather than passing over an empty directory.
    /// </remarks>
    [Test]
    public async Task WindowsPhysicsPackageCarriesOnlyTheSolverShim()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);

        try
        {
            (string installRoot, string shimRoot, string vulkanRuntimeLibrary) =
                CreateSyntheticWindowsInstall(workRoot);
            (string physicsShimRoot, string physXInstallRoot) =
                CreateSyntheticPhysicsInstall(workRoot, "win-x64");
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);

            PackedPackage corePackage = await PackAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Core.win-x64",
                installRoot,
                shimRoot,
                vulkanRuntimeLibrary,
                packageRoot);
            PackedPackage physicsPackage = await PackAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Physics.win-x64",
                installRoot,
                shimRoot,
                vulkanRuntimeLibrary,
                packageRoot,
                physicsShimRoot: physicsShimRoot,
                physXInstallRoot: physXInstallRoot);

            await AssertPackageEntriesAsync(
                physicsPackage.Path,
                [
                    "THIRD-PARTY-NOTICES.md",
                    "THIRD-PARTY-PHYSX.md",
                    "build/OpenUsd.Runtime.Physics.win-x64.native-abi.json",
                    "buildTransitive/OpenUsd.Runtime.Physics.win-x64.targets",
                    "runtimes/win-x64/native/openusd_physx.dll",
                ]);
            await AssertPackageEntryMatchesFileAsync(
                physicsPackage.Path,
                "runtimes/win-x64/native/openusd_physx.dll",
                Path.Combine(physicsShimRoot, "bin", "openusd_physx.dll"));
            await AssertSingleNativePackageEntryAsync(
                physicsPackage.Path,
                "win-x64",
                "openusd_physx.dll");
            foreach (string masked in new[]
            {
                "openusd_dotnet.dll",
                "openusd_hydra.dll",
                "openusd_hdsilk.dll",
                "openusd_storm_child.dll",
            })
            {
                await AssertPackageDoesNotContainAsync(physicsPackage.Path, masked);
            }

            await AssertNoProprietaryPhysXModulesAsync(physicsPackage.Path);
            await AssertPhysicsAbiEvidenceAsync(physicsPackage.Path, "win-x64");
            await AssertPackageDoesNotContainAsync(corePackage.Path, "openusd_physx");
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    /// <summary>
    /// Requires the Linux physics package to carry the solver alone, with no GPU module.
    /// </summary>
    /// <remarks>
    /// Packed on whatever host runs this suite, because a nupkg is a zip of staged files and needs
    /// no Linux runner to assemble. The Linux prefix is the one that stages a bare
    /// <c>libPhysXGpu_64.so</c> with no device module beside it, so it is the case where a
    /// count-based rule would have been satisfied by the wrong file.
    /// </remarks>
    [Test]
    public async Task LinuxPhysicsPackageCarriesOnlyTheSolverShim()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);

        try
        {
            (string installRoot, string shimRoot) = CreateSyntheticUnixInstall(workRoot, "linux-x64");
            (string physicsShimRoot, string physXInstallRoot) =
                CreateSyntheticPhysicsInstall(workRoot, "linux-x64");
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);

            PackedPackage physicsPackage = await PackAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Physics.linux-x64",
                installRoot,
                shimRoot,
                string.Empty,
                packageRoot,
                physicsShimRoot: physicsShimRoot,
                physXInstallRoot: physXInstallRoot);

            await AssertPackageEntriesAsync(
                physicsPackage.Path,
                [
                    "THIRD-PARTY-NOTICES.md",
                    "THIRD-PARTY-PHYSX.md",
                    "build/OpenUsd.Runtime.Physics.linux-x64.native-abi.json",
                    "buildTransitive/OpenUsd.Runtime.Physics.linux-x64.targets",
                    "runtimes/linux-x64/native/libopenusd_physx.so",
                ]);
            await AssertSingleNativePackageEntryAsync(
                physicsPackage.Path,
                "linux-x64",
                "libopenusd_physx.so");
            await AssertNoProprietaryPhysXModulesAsync(physicsPackage.Path);
            await AssertPhysicsAbiEvidenceAsync(physicsPackage.Path, "linux-x64");
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    /// <summary>
    /// Requires no published package, project, or pack scope to carry an NVIDIA GPU module.
    /// </summary>
    /// <remarks>
    /// The PhysXGpu and PhysXDevice modules are packman blobs the vcpkg port downloads under NVIDIA
    /// proprietary terms; they are not built from the BSD-3-Clause PhysX sources this project
    /// statically links, and there is no redistribution agreement covering them. They were briefly
    /// published as <c>OpenUsd.Runtime.Physics.Cuda*</c> packages, which is exactly the mistake
    /// that cannot be made twice: a package pushed to nuget.org can be unlisted but never
    /// withdrawn.
    ///
    /// This is a source-level gate on purpose. The pack-time guard only sees packages that are
    /// actually packed, so it cannot catch a resurrected project or package id that a release run
    /// would then produce for the first time.
    /// </remarks>
    [Test]
    public async Task NoPublishedPackageCarriesProprietaryPhysXGpuModules()
    {
        string repositoryRoot = FindRepositoryRoot();

        await Assert.That(AllRuntimePackageIds()
            .Any(id => id.Contains("Cuda", StringComparison.OrdinalIgnoreCase))).IsFalse();

        string[] projectDirectories =
        [
            .. Directory
                .EnumerateDirectories(Path.Combine(repositoryRoot, "src"))
                .Select(Path.GetFileName)
                .Select(name => name!)
                .Where(name => name.Contains("Cuda", StringComparison.OrdinalIgnoreCase)),
        ];
        await Assert.That(projectDirectories)
            .IsEmpty()
            .Because("a CUDA runtime project would be packed by a future release scope");

        string packer = await File.ReadAllTextAsync(
            Path.Combine(repositoryRoot, "eng", "pack-packages.ps1"));
        // Matched on the quoted-id form the published and scope lists use, so the file may still
        // explain in prose why no CUDA package exists.
        MatchCollection packedIds = Regex.Matches(
            packer,
            @"'(?<id>OpenUsd[^']*)'",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));
        await Assert.That(packedIds.Count)
            .IsGreaterThan(20)
            .Because("the published list must still be readable for this check to mean anything");
        await Assert.That(packedIds
            .Select(match => match.Groups["id"].Value)
            .Where(id => id.Contains("Cuda", StringComparison.OrdinalIgnoreCase))
            .ToArray())
            .IsEmpty()
            .Because("no pack scope may name a CUDA package id");

        string targets = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "OpenUsd.Runtime.Packaging",
            "OpenUsd.Runtime.Packaging.targets"));
        foreach (string module in new[] { "PhysXGpu", "PhysXDevice" })
        {
            await Assert.That(targets)
                .DoesNotContain($"{module}_64", StringComparison.Ordinal)
                .Because($"no packaging item may reference the proprietary {module} module");
        }

        // The validator refuses one by name even if a future edit widened the expected asset list.
        string validator = await File.ReadAllTextAsync(Path.Combine(
            repositoryRoot,
            "src",
            "OpenUsd.Runtime.Packaging",
            "Validate-PhysicsNativePackage.ps1"));
        await Assert.That(validator)
            .Contains("^(lib)?PhysX(Gpu|Device)", StringComparison.Ordinal)
            .Because("packing must refuse a proprietary module independently of the name list");
    }

    /// <summary>
    /// Requires the PhysX notice to separate what is redistributed from what is not.
    /// </summary>
    /// <remarks>
    /// A notice that named only BSD-3-Clause would tell a reader that everything PhysX ships is
    /// permissively licensed. The static SDK linked into the shim is; the GPU modules are not, and
    /// the difference is the whole reason no package contains them.
    /// </remarks>
    [Test]
    public async Task PhysXNoticeSeparatesRedistributedFromProprietaryModules()
    {
        string repositoryRoot = FindRepositoryRoot();
        string script = await File.ReadAllTextAsync(
            Path.Combine(repositoryRoot, "eng", "build-physx-native.ps1"));

        await Assert.That(script).Contains("## Redistributed here");
        await Assert.That(script).Contains("## Not redistributed");
        await Assert.That(script).Contains("gpuModuleLicense");

        using JsonDocument physxLock = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(repositoryRoot, "eng", "physx.lock.json")));
        JsonElement physx = physxLock.RootElement.GetProperty("physx");

        await Assert.That(physx.GetProperty("license").GetString()).IsEqualTo("BSD-3-Clause");
        await Assert.That(physx.GetProperty("gpuModuleLicense").GetString())
            .IsEqualTo("LicenseRef-NvidiaProprietary");
        await Assert.That(physx.GetProperty("gpuModuleScope").GetString())
            .Contains("never redistributed");
        await Assert.That(
            physx.GetProperty("supportedRids").EnumerateArray()
                .Select(rid => rid.GetString()!)
                .ToArray())
            .IsEquivalentTo(SupportedPhysicsRids);
    }

    /// <summary>
    /// Requires macOS to have no physics package at all rather than an empty placeholder.
    /// </summary>
    /// <remarks>
    /// The pinned vcpkg PhysX port declares
    /// <c>(windows &amp; x64 &amp; !mingw &amp; !uwp) | (linux &amp; x64) | (linux &amp; arm64)</c>.
    /// There is no arm64-osx simulation SDK, so an <c>OpenUsd.Runtime.Physics.osx-arm64</c> package
    /// could only ever be empty, and a consumer would read its emptiness as a broken deployment
    /// instead of a platform fact. Advertising an unbuildable package is worse than advertising
    /// none, so the project, the package id, the preset, and every pack expectation are absent
    /// together.
    /// </remarks>
    [Test]
    public async Task MacOsHasNoPhysicsPackageBecauseTheSimulationSdkHasNoMacOsBuild()
    {
        string repositoryRoot = FindRepositoryRoot();

        await Assert.That(Directory.Exists(Path.Combine(
            repositoryRoot,
            "src",
            "OpenUsd.Runtime.Physics.osx-arm64"))).IsFalse();
        await Assert.That(AllRuntimePackageIds()).DoesNotContain("OpenUsd.Runtime.Physics.osx-arm64");

        string packer = await File.ReadAllTextAsync(
            Path.Combine(repositoryRoot, "eng", "pack-packages.ps1"));
        await Assert.That(packer).DoesNotContain("OpenUsd.Runtime.Physics.osx-arm64");

        XDocument metapackage = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "OpenUsd.Runtime.Physics",
            "OpenUsd.Runtime.Physics.csproj"));
        string[] ridPackages =
        [
            .. metapackage
                .Descendants("ProjectReference")
                .Select(element => Path.GetFileNameWithoutExtension(
                    element.Attribute("Include")?.Value)!),
        ];
        await Assert.That(ridPackages).IsEquivalentTo(ExpectedPhysicsRidPackages);

        // The build scripts refuse the RID rather than failing deep inside vcpkg or cmake.
        foreach (string script in new[] { "build-physx-native.ps1", "build-physx-shim.ps1" })
        {
            string text = await File.ReadAllTextAsync(
                Path.Combine(repositoryRoot, "eng", script));
            await Assert.That(text)
                .Contains("[ValidateSet('win-x64', 'linux-x64')]", StringComparison.Ordinal)
                .Because($"{script} must refuse an unsupported RID at the parameter boundary");
        }
    }

    [Test]
    public async Task MissingPhysicsShimFailsPackClearly()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            (string installRoot, string shimRoot, string vulkanRuntimeLibrary) =
                CreateSyntheticWindowsInstall(workRoot);
            (string physicsShimRoot, string physXInstallRoot) =
                CreateSyntheticPhysicsInstall(workRoot, "win-x64");
            File.Delete(Path.Combine(physicsShimRoot, "bin", "openusd_physx.dll"));
            string projectPath = Path.Combine(
                repositoryRoot,
                "src",
                "OpenUsd.Runtime.Physics.win-x64",
                "OpenUsd.Runtime.Physics.win-x64.csproj");
            CommandResult result = await RunDotnetAsync(
                repositoryRoot,
                [
                    "pack",
                    projectPath,
                    "-c",
                    "Release",
                    "--nologo",
                    $"-p:OpenUsdInstallRoot={installRoot}",
                    $"-p:OpenUsdShimInstallRoot={shimRoot}",
                    $"-p:OpenUsdPhysicsShimInstallRoot={physicsShimRoot}",
                    $"-p:OpenUsdPhysXInstallRoot={physXInstallRoot}",
                    $"-p:OpenUsdVulkanRuntimeLibrary={vulkanRuntimeLibrary}",
                    $"-p:PackageOutputPath={Path.Combine(workRoot, "packages")}",
                ]);

            await Assert.That(result.ExitCode).IsNotEqualTo(0);
            await Assert.That(result.Output).Contains("The OpenUsd physics shim is missing");
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task PhysicsManagedPackageCarriesEmbeddedSchemaResources()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);

        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);
            PackedPackage physicsPackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd.Physics",
                packageRoot);

            // The metapackage dependency is what makes a bare OpenUsd.Physics reference resolve a
            // solver. No CUDA package may appear: none is published, because the GPU modules are
            // NVIDIA proprietary blobs this project does not redistribute.
            await AssertPackageDependenciesAsync(
                physicsPackage.Path,
                physicsPackage.Version,
                ["OpenUsd.Runtime.Physics", "OpenUsd"]);
            await AssertPackageDoesNotContainAsync(physicsPackage.Path, "Cuda");
            await AssertNoProprietaryPhysXModulesAsync(physicsPackage.Path);

            using ZipArchive package = ZipFile.OpenRead(physicsPackage.Path);
            foreach (string targetFramework in new[] { "net8.0", "net9.0", "net10.0" })
            {
                ZipArchiveEntry entry = package.Entries.Single(candidate =>
                    string.Equals(
                        candidate.FullName,
                        $"lib/{targetFramework}/OpenUsd.Physics.dll",
                        StringComparison.Ordinal));

                using Stream content = entry.Open();
                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer);
                buffer.Position = 0;

                using var assembly = new PEReader(buffer);
                MetadataReader metadata = assembly.GetMetadataReader();
                string[] resources =
                [
                    .. metadata.ManifestResources
                        .Select(handle => metadata.GetString(
                            metadata.GetManifestResource(handle).Name))
                        .Order(StringComparer.Ordinal),
                ];

                await Assert.That(resources).Contains("OpenUsd.Physics.Schema.plugInfo.json");
                await Assert.That(resources).Contains("OpenUsd.Physics.Schema.generatedSchema.usda");
            }
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task MissingCesiumShimFailsPackClearly()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            (string installRoot, string shimRoot, string vulkanRuntimeLibrary) =
                CreateSyntheticWindowsInstall(workRoot);
            File.Delete(Path.Combine(shimRoot, "bin", "openusd_cesium.dll"));
            string projectPath = Path.Combine(
                repositoryRoot,
                "src",
                "OpenUsd.Runtime.Cesium.win-x64",
                "OpenUsd.Runtime.Cesium.win-x64.csproj");
            CommandResult result = await RunDotnetAsync(
                repositoryRoot,
                [
                    "pack",
                    projectPath,
                    "-c",
                    "Release",
                    "--nologo",
                    $"-p:OpenUsdInstallRoot={installRoot}",
                    $"-p:OpenUsdShimInstallRoot={shimRoot}",
                    $"-p:OpenUsdCesiumShimInstallRoot={shimRoot}",
                    $"-p:OpenUsdVulkanRuntimeLibrary={vulkanRuntimeLibrary}",
                    $"-p:PackageOutputPath={Path.Combine(workRoot, "packages")}",
                ]);

            await Assert.That(result.ExitCode).IsNotEqualTo(0);
            await Assert.That(result.Output).Contains("The OpenUsd Cesium shim is missing");
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task MissingStormChildFailsWindowsImagingPackClearly()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            (string installRoot, string shimRoot, string vulkanRuntimeLibrary) =
                CreateSyntheticWindowsInstall(workRoot);
            File.Delete(Path.Combine(shimRoot, "bin", "openusd_storm_child.dll"));
            string projectPath = Path.Combine(
                repositoryRoot,
                "src",
                "OpenUsd.Runtime.Imaging.win-x64",
                "OpenUsd.Runtime.Imaging.win-x64.csproj");
            CommandResult result = await RunDotnetAsync(
                repositoryRoot,
                [
                    "pack",
                    projectPath,
                    "-c",
                    "Release",
                    "--nologo",
                    $"-p:OpenUsdInstallRoot={installRoot}",
                    $"-p:OpenUsdShimInstallRoot={shimRoot}",
                    $"-p:OpenUsdVulkanRuntimeLibrary={vulkanRuntimeLibrary}",
                    $"-p:PackageOutputPath={Path.Combine(workRoot, "packages")}",
                ]);

            await Assert.That(result.ExitCode).IsNotEqualTo(0);
            await Assert.That(result.Output).Contains("The OpenUsd Storm child shim is missing");
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task MissingNativeInstallFailsPackClearly()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);

        try
        {
            string projectPath = Path.Combine(
                repositoryRoot,
                "src",
                "OpenUsd.Runtime.Core.linux-x64",
                "OpenUsd.Runtime.Core.linux-x64.csproj");
            CommandResult result = await RunDotnetAsync(
                repositoryRoot,
                [
                    "pack",
                    projectPath,
                    "-c",
                    "Release",
                    "--nologo",
                    $"-p:OpenUsdInstallRoot={Path.Combine(workRoot, "missing-openusd")}",
                    $"-p:OpenUsdShimInstallRoot={Path.Combine(workRoot, "missing-shim")}",
                    $"-p:PackageOutputPath={Path.Combine(workRoot, "packages")}",
                ]);

            await Assert.That(result.ExitCode).IsNotEqualTo(0);
            await Assert.That(result.Output).Contains("The locked OpenUSD install for linux-x64 is missing");
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task UnixImagingPackagesTransformHdSilkPluginWithoutDuplicateLibrary()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);

        try
        {
            (string Rid, string LibraryFile, string LibraryPath)[] cases =
            [
                ("linux-x64", "libopenusd_hdsilk.so", "../../../libopenusd_hdsilk.so"),
                ("osx-arm64", "libopenusd_hdsilk.dylib", "../../../libopenusd_hdsilk.dylib"),
            ];

            foreach ((string rid, string libraryFile, string libraryPath) in cases)
            {
                (string installRoot, string shimRoot) =
                    CreateSyntheticUnixInstall(workRoot, rid);
                string packageRoot = Path.Combine(workRoot, "packages", rid);
                Directory.CreateDirectory(packageRoot);
                PackedPackage imagingPackage = await PackAsync(
                    repositoryRoot,
                    $"OpenUsd.Runtime.Imaging.{rid}",
                    installRoot,
                    shimRoot,
                    vulkanRuntimeLibrary: string.Empty,
                    packageRoot,
                    skipLinuxElfValidation: rid == "linux-x64",
                    skipMacOsMachOValidation: rid == "osx-arm64");

                await AssertHdSilkPackageAsync(
                    imagingPackage.Path,
                    rid,
                    libraryFile,
                    libraryPath);
                await AssertImagingDependsOnCoreAsync(
                    imagingPackage.Path,
                    rid,
                    imagingPackage.Version);
                if (rid == "osx-arm64")
                {
                    string stormChildPath = Path.Combine(
                        shimRoot,
                        "lib",
                        "libopenusd_storm_child.dylib");
                    await AssertSingleNativePackageEntryAsync(
                        imagingPackage.Path,
                        rid,
                        "libopenusd_storm_child.dylib");
                    await AssertPackageEntryMatchesFileAsync(
                        imagingPackage.Path,
                        "runtimes/osx-arm64/native/libopenusd_storm_child.dylib",
                        stormChildPath);
                }

                string sourcePlugInfo = Path.Combine(
                    shimRoot,
                    "plugin",
                    "usd",
                    "hdSilk",
                    "resources",
                    "plugInfo.json");
                using JsonDocument source = JsonDocument.Parse(
                    await File.ReadAllTextAsync(sourcePlugInfo));
                string? installedLibraryPath = source
                    .RootElement
                    .GetProperty("Plugins")[0]
                    .GetProperty("LibraryPath")
                    .GetString();
                await Assert.That(installedLibraryPath).IsEqualTo("installed-source-path");
            }
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task LinuxStormChildPackagePreservesLayoutVersionAndHashes()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);

        try
        {
            (string installRoot, string shimRoot) =
                CreateSyntheticUnixInstall(workRoot, "linux-x64");
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);
            PackedPackage corePackage = await PackAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Core.linux-x64",
                installRoot,
                shimRoot,
                vulkanRuntimeLibrary: string.Empty,
                packageRoot);
            PackedPackage imagingPackage = await PackAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Imaging.linux-x64",
                installRoot,
                shimRoot,
                vulkanRuntimeLibrary: string.Empty,
                packageRoot,
                skipLinuxElfValidation: true);

            string stormChild = Path.Combine(
                shimRoot,
                "lib",
                "libopenusd_storm_child.so");
            string versionedStormChild = $"{stormChild}.8.0.0";
            await AssertPackageEntriesAsync(
                imagingPackage.Path,
                [
                    "buildTransitive/OpenUsd.Runtime.Imaging.linux-x64.targets",
                    "runtimes/linux-x64/native/libopenusd_storm_child.so",
                    "runtimes/linux-x64/native/libopenusd_storm_child.so.8",
                    "runtimes/linux-x64/native/libopenusd_storm_child.so.8.0.0",
                ]);
            await AssertPackageEntryContainsAsync(
                imagingPackage.Path,
                "buildTransitive/OpenUsd.Runtime.Imaging.linux-x64.targets",
                "libopenusd_storm_child.so.8.0.0",
                "libopenusd_storm_child.so.7");
            await AssertSingleNativePackageEntryAsync(
                imagingPackage.Path,
                "linux-x64",
                "libopenusd_storm_child.so");
            await AssertSingleNativePackageEntryAsync(
                imagingPackage.Path,
                "linux-x64",
                "libopenusd_storm_child.so.8");
            await AssertSingleNativePackageEntryAsync(
                imagingPackage.Path,
                "linux-x64",
                "libopenusd_storm_child.so.8.0.0");
            await AssertPackageSymbolicLinkAsync(
                imagingPackage.Path,
                "runtimes/linux-x64/native/libopenusd_storm_child.so",
                "libopenusd_storm_child.so.8");
            await AssertPackageSymbolicLinkAsync(
                imagingPackage.Path,
                "runtimes/linux-x64/native/libopenusd_storm_child.so.8",
                "libopenusd_storm_child.so.8.0.0");
            await AssertPackageEntryMatchesFileAsync(
                imagingPackage.Path,
                "runtimes/linux-x64/native/libopenusd_storm_child.so.8.0.0",
                versionedStormChild);
            await AssertLinuxStormChildInstallMatchesPackageAsync(
                imagingPackage.Path,
                shimRoot);
            await AssertImagingDependsOnCoreAsync(
                imagingPackage.Path,
                "linux-x64",
                corePackage.Version);
            await Assert.That(imagingPackage.Version).IsEqualTo(corePackage.Version);
            PackedPackage imagingMetaPackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Imaging",
                packageRoot);
            await Assert.That(imagingMetaPackage.Version).IsEqualTo(corePackage.Version);
            await CreateStubRuntimePackagesAsync(
                workRoot,
                packageRoot,
                imagingPackage.Version,
                [.. GetRuntimeImagingMetaPackageGraph(
                    SupportedExecutionPlatforms.Single(platform => platform.Rid == "linux-x64"))
                    .Where(id => id != "OpenUsd.Runtime.Imaging")
                    .Where(id => id != "OpenUsd.Runtime.Imaging.linux-x64")
                    .Where(id => id != "OpenUsd.Runtime.Core.linux-x64")]);
            await AssertPackageDoesNotContainFileNameOutsideNativeAsync(
                imagingPackage.Path,
                "libopenusd_storm_child.so");
            await AssertPackageDoesNotContainFileNameOutsideNativeAsync(
                imagingPackage.Path,
                "libopenusd_storm_child.so.8");
            await AssertPackageDoesNotContainFileNameOutsideNativeAsync(
                imagingPackage.Path,
                "libopenusd_storm_child.so.8.0.0");

            ExecutionConsumer consumer = await PublishStormChildConsumerAsync(
                workRoot,
                packageRoot,
                imagingPackage.Version,
                SupportedExecutionPlatforms.Single(platform => platform.Rid == "linux-x64"),
                publishAot: false);
            AssertPackageOnlyGraph(
                consumer.AssetsPath,
                GetRuntimeImagingMetaPackageGraph(
                    SupportedExecutionPlatforms.Single(platform => platform.Rid == "linux-x64")));
            string consumerProject = await File.ReadAllTextAsync(consumer.ProjectPath);
            await Assert.That(consumerProject).DoesNotContain("ProjectReference");
            await Assert.That(consumerProject).DoesNotContain("OpenUsd.Runtime.Core.linux-x64");
            await AssertNoSourcePathLeakageAsync(consumerProject, repositoryRoot);
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task MissingStormChildFailsMacOsImagingPackClearly()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            (string installRoot, string shimRoot) =
                CreateSyntheticUnixInstall(workRoot, "osx-arm64");
            File.Delete(Path.Combine(
                shimRoot,
                "lib",
                "libopenusd_storm_child.dylib"));
            string projectPath = Path.Combine(
                repositoryRoot,
                "src",
                "OpenUsd.Runtime.Imaging.osx-arm64",
                "OpenUsd.Runtime.Imaging.osx-arm64.csproj");
            CommandResult result = await RunDotnetAsync(
                repositoryRoot,
                [
                    "pack",
                    projectPath,
                    "-c",
                    "Release",
                    "--nologo",
                    $"-p:OpenUsdInstallRoot={installRoot}",
                    $"-p:OpenUsdShimInstallRoot={shimRoot}",
                    "-p:OpenUsdSkipMacOsMachOValidation=true",
                    $"-p:PackageOutputPath={Path.Combine(workRoot, "packages")}",
                ]);

            await Assert.That(result.ExitCode).IsNotEqualTo(0);
            await Assert.That(result.Output).Contains("The OpenUsd Storm child shim is missing");
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task NonMetalImagingConsumerSourcesCompileFromCleanFeeds()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Cross-RID package consumer compilation is covered by the Windows package job.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        string repositoryRoot = FindRepositoryRoot();
        ExecutionPlatform[] platforms =
            SupportedExecutionPlatforms
                .Where(platform => platform.Rid != "osx-arm64")
                .ToArray();
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);
            string[] managedPackageIds =
            [
                "OpenUsd.Interop",
                "OpenUsd",
                "OpenUsd.Rendering",
                "OpenUsd.Rendering.Silk",
                .. platforms.Select(platform => platform.BackendPackageId),
            ];
            var managedPackages = new List<PackedPackage>();
            foreach (string packageId in managedPackageIds)
            {
                managedPackages.Add(
                    await PackManagedPackageAsync(repositoryRoot, packageId, packageRoot));
            }

            string packageVersion = managedPackages[0].Version;
            foreach (PackedPackage package in managedPackages)
            {
                await Assert.That(package.Version).IsEqualTo(packageVersion);
            }

            foreach (ExecutionPlatform platform in platforms)
            {
                (string installRoot, string shimRoot, string vulkanRuntimeLibrary) =
                    platform.Rid == "win-x64"
                        ? CreateSyntheticWindowsInstall(workRoot)
                        : CreateSyntheticUnixExecutionInstall(workRoot, platform.Rid);
                PackedPackage coreRuntimePackage = await PackAsync(
                    repositoryRoot,
                    $"OpenUsd.Runtime.Core.{platform.Rid}",
                    installRoot,
                    shimRoot,
                    vulkanRuntimeLibrary,
                    packageRoot,
                    skipLinuxElfValidation: platform.Rid == "linux-x64");
                PackedPackage imagingRuntimePackage = await PackAsync(
                    repositoryRoot,
                    $"OpenUsd.Runtime.Imaging.{platform.Rid}",
                    installRoot,
                    shimRoot,
                    vulkanRuntimeLibrary,
                    packageRoot,
                    skipLinuxElfValidation: platform.Rid == "linux-x64");
                await Assert.That(coreRuntimePackage.Version).IsEqualTo(packageVersion);
                await Assert.That(imagingRuntimePackage.Version).IsEqualTo(packageVersion);
            }

            PackedPackage imagingMetaPackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Imaging",
                packageRoot);
            await Assert.That(imagingMetaPackage.Version).IsEqualTo(packageVersion);
            await CreateStubRuntimePackagesAsync(
                workRoot,
                packageRoot,
                packageVersion,
                [.. GetImagingMetaPackageGraph(platforms[0])
                    .Where(id => id.StartsWith("OpenUsd.Runtime.", StringComparison.Ordinal))
                    .Where(id => id != "OpenUsd.Runtime.Imaging")
                    .Where(id => !platforms.Any(platform =>
                        id == $"OpenUsd.Runtime.Core.{platform.Rid}" ||
                        id == $"OpenUsd.Runtime.Imaging.{platform.Rid}"))]);

            foreach (ExecutionPlatform platform in platforms)
            {
                ExecutionConsumer consumer = await PublishImagingExecutionConsumerAsync(
                    workRoot,
                    packageRoot,
                    packageVersion,
                    platform,
                    publishAot: false);
                AssertPackageOnlyGraph(
                    consumer.AssetsPath,
                    GetImagingMetaPackageGraph(platform));

                string consumerProject = await File.ReadAllTextAsync(consumer.ProjectPath);
                await Assert.That(consumerProject).DoesNotContain("ProjectReference");
                await Assert.That(consumerProject).Contains(platform.BackendPackageId);
                await Assert.That(consumerProject).Contains("OpenUsd.Runtime.Imaging");
                await Assert.That(consumerProject).DoesNotContain(
                    $"OpenUsd.Runtime.Imaging.{platform.Rid}");
                await Assert.That(consumerProject).DoesNotContain(
                    $"OpenUsd.Runtime.Core.{platform.Rid}");
            }
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task LinuxStormChildPackageExecutesNativeAotWithoutLibraryPath()
    {
        string repositoryRoot = FindRepositoryRoot();
        int stormChildAbiVersion = ReadStormChildAbiVersion(repositoryRoot);
        if (!OperatingSystem.IsLinux() ||
            RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            Skip.Test("Linux x64 package-only Storm child execution runs in Linux CI.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        if (!TryGetExecutionInputs(
            repositoryRoot,
            out NativeExecutionInputs inputs,
            out string reason))
        {
            HandleMissingExecutionPrerequisites(
                nameof(LinuxStormChildPackageExecutesNativeAotWithoutLibraryPath),
                reason);
            return;
        }

        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);
            PackedPackage corePackage = await PackAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Core.linux-x64",
                inputs.InstallRoot,
                inputs.ShimRoot,
                inputs.VulkanRuntimeLibrary,
                packageRoot);
            PackedPackage imagingPackage = await PackAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Imaging.linux-x64",
                inputs.InstallRoot,
                inputs.ShimRoot,
                inputs.VulkanRuntimeLibrary,
                packageRoot);
            await Assert.That(imagingPackage.Version).IsEqualTo(corePackage.Version);
            await AssertImagingDependsOnCoreAsync(
                imagingPackage.Path,
                "linux-x64",
                corePackage.Version);
            PackedPackage imagingMetaPackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Imaging",
                packageRoot);
            await Assert.That(imagingMetaPackage.Version).IsEqualTo(corePackage.Version);
            await CreateStubRuntimePackagesAsync(
                workRoot,
                packageRoot,
                imagingPackage.Version,
                [.. GetRuntimeImagingMetaPackageGraph(inputs.Platform)
                    .Where(id => id != "OpenUsd.Runtime.Imaging")
                    .Where(id => id != "OpenUsd.Runtime.Imaging.linux-x64")
                    .Where(id => id != "OpenUsd.Runtime.Core.linux-x64")]);

            string installedStormChildPath = Path.Combine(
                inputs.ShimRoot,
                "lib",
                "libopenusd_storm_child.so");
            await AssertLinuxStormChildInstallMatchesPackageAsync(
                imagingPackage.Path,
                inputs.ShimRoot);
            await AssertLinuxValidationEvidenceAsync(imagingPackage.Path);

            ExecutionConsumer consumer = await PublishStormChildConsumerAsync(
                workRoot,
                packageRoot,
                imagingPackage.Version,
                inputs.Platform,
                publishAot: true);
            AssertPackageOnlyGraph(
                consumer.AssetsPath,
                GetRuntimeImagingMetaPackageGraph(inputs.Platform));

            CommandResult result = await RunExecutableAsync(
                GetExecutablePath(consumer.PublishRoot, "Consumer"),
                consumer.PublishRoot,
                []);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.Output);
            }

            await Assert.That(result.Output).Contains("PACKAGE_STORM_CHILD_EXECUTION_OK");
            await Assert.That(result.Output).Contains($"STORM_CHILD_ABI={stormChildAbiVersion}");
            await Assert.That(result.Output).Contains("STORM_CHILD_CAPTURE_STATUS=1");
            await Assert.That(result.Output).Contains(
                "STORM_CHILD_CAPTURE_ERROR=A valid Storm native child is required.");
            await Assert.That(result.Output).Contains("STORM_CHILD_NAVIGATION_STATUS=1");
            await Assert.That(result.Output).Contains(
                "STORM_CHILD_NAVIGATION_ERROR=A valid Storm native child is required.");
            await Assert.That(result.Output).Contains("STORM_CHILD_NAVIGATION_RESET=true");
            await Assert.That(result.Output)
                .Contains("STORM_CHILD_INITIALIZE_LINUX_EXPORT=true");
            await Assert.That(result.Output).Contains("LD_LIBRARY_PATH_PRESENT=false");
            await Assert.That(result.Output).Contains("PROJECT_OPENUSD_MAPS_CONFINED=true");
            await Assert.That(result.Output).Contains("STORM_CHILD_MAP_PUBLISH_ROOT=true");
            await Assert.That(result.Output).Contains("OPENUSD_MAP_PUBLISH_ROOT=true");
            await Assert.That(result.Output).Contains("CWD_IS_PUBLISH=true");
            // APP_BASE_CANONICAL reports the consumer's own publish directory, which
            // the harness creates under artifacts/, so it necessarily contains the
            // repository path and is not leakage. That it is the publish root is
            // already asserted by CWD_IS_PUBLISH and the map confinement above, so
            // scan everything else for real source paths.
            string leakageScan = string.Join(
                '\n',
                result.Output
                    .Split('\n')
                    .Where(line => !line.StartsWith(
                        "APP_BASE_CANONICAL=",
                        StringComparison.Ordinal)));
            await AssertNoSourcePathLeakageAsync(leakageScan, repositoryRoot);
            await AssertPublishedLinuxStormChildTopologyAsync(consumer.PublishRoot);

            string[] stormLibraries = Directory.GetFiles(
                consumer.PublishRoot,
                "libopenusd_storm_child.so*",
                SearchOption.AllDirectories);
            await Assert.That(stormLibraries.Count(
                path => Path.GetFileName(path) == "libopenusd_storm_child.so")).IsEqualTo(1);
            await AssertFileHashesEqualAsync(
                installedStormChildPath,
                Path.Combine(consumer.PublishRoot, "libopenusd_storm_child.so"));
            await WriteLinuxStormChildArtifactsAsync(
                repositoryRoot,
                imagingPackage.Path,
                result.Output);
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task MacOsStormChildPackageExecutesSignedNativeAotWithoutLibraryPath()
    {
        if (!OperatingSystem.IsMacOS() ||
            RuntimeInformation.OSArchitecture != Architecture.Arm64)
        {
            Skip.Test("macOS Arm64 package-only Storm child execution runs in macOS CI.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        string repositoryRoot = FindRepositoryRoot();
        int stormChildAbiVersion = ReadStormChildAbiVersion(repositoryRoot);
        await RequireMetalLibraryOnMacOSAsync(repositoryRoot);
        if (!TryGetExecutionInputs(
            repositoryRoot,
            out NativeExecutionInputs inputs,
            out string reason))
        {
            HandleMissingExecutionPrerequisites(
                nameof(MacOsStormChildPackageExecutesSignedNativeAotWithoutLibraryPath),
                reason);
            return;
        }

        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);
            foreach (string packageId in new[]
            {
                "OpenUsd.Interop",
                "OpenUsd",
                "OpenUsd.Rendering",
                "OpenUsd.Rendering.Silk",
                "OpenUsd.Rendering.Silk.Metal",
            })
            {
                await PackManagedPackageAsync(repositoryRoot, packageId, packageRoot);
            }

            PackedPackage corePackage = await PackAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Core.osx-arm64",
                inputs.InstallRoot,
                inputs.ShimRoot,
                inputs.VulkanRuntimeLibrary,
                packageRoot);
            PackedPackage imagingPackage = await PackAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Imaging.osx-arm64",
                inputs.InstallRoot,
                inputs.ShimRoot,
                inputs.VulkanRuntimeLibrary,
                packageRoot);
            await Assert.That(imagingPackage.Version).IsEqualTo(corePackage.Version);
            await AssertImagingDependsOnCoreAsync(
                imagingPackage.Path,
                "osx-arm64",
                corePackage.Version);
            PackedPackage imagingMetaPackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Imaging",
                packageRoot);
            await Assert.That(imagingMetaPackage.Version).IsEqualTo(corePackage.Version);
            await CreateStubRuntimePackagesAsync(
                workRoot,
                packageRoot,
                imagingPackage.Version,
                [.. GetRuntimeImagingMetaPackageGraph(inputs.Platform)
                    .Where(id => id != "OpenUsd.Runtime.Imaging")
                    .Where(id => id != "OpenUsd.Runtime.Imaging.osx-arm64")
                    .Where(id => id != "OpenUsd.Runtime.Core.osx-arm64")]);
            await AssertMacOsValidationEvidenceAsync(imagingPackage.Path);
            await AssertMetalPackageMatchesStagedLibraryAsync(
                FindPackage(packageRoot, "OpenUsd.Rendering.Silk.Metal"),
                repositoryRoot);

            string installedStormChildPath = Path.Combine(
                inputs.ShimRoot,
                "lib",
                "libopenusd_storm_child.dylib");
            await AssertPackageEntryMatchesFileAsync(
                imagingPackage.Path,
                "runtimes/osx-arm64/native/libopenusd_storm_child.dylib",
                installedStormChildPath);

            ExecutionConsumer consumer = await PublishStormChildConsumerAsync(
                workRoot,
                packageRoot,
                imagingPackage.Version,
                inputs.Platform,
                publishAot: true);
            AssertPackageOnlyGraph(
                consumer.AssetsPath,
                [
                    "OpenUsd.Rendering.Silk.Metal",
                    .. GetRuntimeImagingMetaPackageGraph(inputs.Platform),
                ]);
            string consumerProject = await File.ReadAllTextAsync(consumer.ProjectPath);
            await Assert.That(consumerProject).DoesNotContain("ProjectReference");
            await Assert.That(consumerProject).Contains("OpenUsd.Runtime.Imaging");
            await Assert.That(consumerProject).DoesNotContain("OpenUsd.Runtime.Imaging.osx-arm64");
            await Assert.That(consumerProject).Contains("OpenUsd.Rendering.Silk.Metal");
            await AssertNoSourcePathLeakageAsync(consumerProject, repositoryRoot);
            await AssertMetalPublishedAssetsAsync(consumer.PublishRoot, repositoryRoot);
            string publishedStormChildPath = Directory.GetFiles(
                consumer.PublishRoot,
                "libopenusd_storm_child.dylib",
                SearchOption.AllDirectories).Single();
            MacStormChildIdentity stormIdentity =
                await ValidateMacPublishedStormChildIdentityAsync(
                    imagingPackage.Path,
                    installedStormChildPath,
                    publishedStormChildPath);
            MacCodeSignEvidence[] signingEvidence = await SignAndVerifyMacConsumerAsync(
                consumer.PublishRoot,
                GetExecutablePath(consumer.PublishRoot, "Consumer"));

            CommandResult result = await RunExecutableAsync(
                GetExecutablePath(consumer.PublishRoot, "Consumer"),
                consumer.PublishRoot,
                []);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.Output);
            }

            await Assert.That(result.Output).Contains("PACKAGE_STORM_CHILD_EXECUTION_OK");
            await Assert.That(result.Output).Contains($"STORM_CHILD_ABI={stormChildAbiVersion}");
            await Assert.That(result.Output).Contains("STORM_CHILD_CAPTURE_STATUS=1");
            await Assert.That(result.Output).Contains(
                "STORM_CHILD_CAPTURE_ERROR=A valid Storm native child is required.");
            await Assert.That(result.Output).Contains("STORM_CHILD_NAVIGATION_STATUS=1");
            await Assert.That(result.Output).Contains(
                "STORM_CHILD_NAVIGATION_ERROR=A valid Storm native child is required.");
            await Assert.That(result.Output).Contains("STORM_CHILD_NAVIGATION_RESET=true");
            await Assert.That(result.Output)
                .Contains("STORM_CHILD_INITIALIZE_LINUX_EXPORT=false");
            await Assert.That(result.Output).Contains("DYLD_LIBRARY_PATH_PRESENT=false");
            await Assert.That(result.Output).Contains(
                "PROJECT_OPENUSD_DYLD_IMAGES_CONFINED=true");
            await Assert.That(result.Output).Contains("STORM_CHILD_DYLD_PUBLISH_ROOT=true");
            await Assert.That(result.Output).Contains("OPENUSD_DYLD_PUBLISH_ROOT=true");
            await Assert.That(result.Output).Contains("METAL_PACKAGE_PATHS_CONFINED=true");
            await Assert.That(result.Output).Contains("CWD_IS_PUBLISH=true");
            string appBaseCanonical = result.Output
                .Split(
                    [Environment.NewLine],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Single(line => line.StartsWith(
                    "APP_BASE_CANONICAL=",
                    StringComparison.Ordinal))["APP_BASE_CANONICAL=".Length..];
            string[] loadedImagePaths = result.Output
                .Split(
                    [Environment.NewLine],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith("OPENUSD_DYLD_IMAGE=", StringComparison.Ordinal))
                .Select(line => line["OPENUSD_DYLD_IMAGE=".Length..])
                .ToArray();
            MacLoadedImageValidation loadedImages = ValidateMacLoadedImages(
                appBaseCanonical,
                loadedImagePaths,
                requireStormAndCore: true,
                requiredExecutableName: Path.GetFileName(
                    GetExecutablePath(consumer.PublishRoot, "Consumer")));
            await Assert.That(loadedImages.Confined).IsTrue();
            await Assert.That(loadedImages.Paths.Length).IsGreaterThanOrEqualTo(4);
            ValidateMacCodeSignEvidence(
                signingEvidence,
                Directory.GetFiles(
                    consumer.PublishRoot,
                    "*.dylib",
                    SearchOption.AllDirectories).Length + 1);
            await AssertNoNativeSourcePathLeakageAsync(result.Output);

            await Assert.That(Directory.GetFiles(
                consumer.PublishRoot,
                "libopenusd_storm_child.dylib",
                SearchOption.AllDirectories)).HasSingleItem();
            await WriteMacOsStormChildArtifactsAsync(
                repositoryRoot,
                imagingPackage.Path,
                result.Output,
                signingEvidence,
                loadedImages,
                stormIdentity);
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task MetalPackageProductionFailsWithoutValidatedLibrary()
    {
        if (OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is not applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        string repositoryRoot = FindRepositoryRoot();
        string projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "OpenUsd.Rendering.Silk.Metal",
            "OpenUsd.Rendering.Silk.Metal.csproj");
        CommandResult result = await RunDotnetAsync(
            repositoryRoot,
            [
                "pack",
                projectPath,
                "-c",
                "Release",
                "--nologo",
                "--no-build",
                "--no-restore",
            ]);

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.Output).Contains(
            "cannot be packed without the validated combined ten-entry mesh.metallib");
    }

    [Test]
    public async Task MetalPackInvokesAuthoritativeSidecarValidation()
    {
        string repositoryRoot = FindRepositoryRoot();
        string projectPath = Path.Combine(
            repositoryRoot,
            "src",
            "OpenUsd.Rendering.Silk.Metal",
            "OpenUsd.Rendering.Silk.Metal.csproj");
        XDocument project = XDocument.Load(projectPath);
        XElement target = project
            .Descendants("Target")
            .Single(element => (
                (string?)element.Attribute("Name") ==
                "ValidateMetalPackageProduction"));
        XElement[] tasks = target.Elements().ToArray();
        XElement validator = tasks.Single(element => element.Name == "Exec");
        string command = (string?)validator.Attribute("Command") ?? string.Empty;
        string validatorPath = project
            .Descendants("OpenUsdMetalShaderValidatorPath")
            .Single()
            .Value;

        await Assert.That(validatorPath).EndsWith("metal_sidecar.py");
        await Assert.That(command).Contains("$(OpenUsdMetalShaderValidatorPath)");
        await Assert.That(command).Contains("--sidecar");
        await Assert.That(command).Contains("--library");
        await Assert.That(command).Contains("--manifest");
        await Assert.That(command).Contains("--lock");
        await Assert.That(command).Contains("--verify-checked-files");
        int validatorIndex = Array.IndexOf(tasks, validator);
        int macOsGateIndex = Array.FindIndex(
            tasks,
            element => (
                ((string?)element.Attribute("Text"))?.Contains(
                    "supported only on macOS",
                    StringComparison.Ordinal) == true));
        await Assert.That(validatorIndex).IsGreaterThanOrEqualTo(0);
        await Assert.That(macOsGateIndex).IsGreaterThan(validatorIndex);
    }

    [Test]
    public async Task ManagedPackagesMatchCurrentReleaseOutputs()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);
            string[] packageIds =
            [
                "OpenUsd.Interop",
                "OpenUsd",
                "OpenUsd.Rendering",
                "OpenUsd.Rendering.Storm",
                "OpenUsd.Rendering.Silk",
                "OpenUsd.Rendering.Silk.D3D12",
                "OpenUsd.Rendering.Silk.Vulkan",
                "OpenUsd.Physics",
            ];
            foreach (string packageId in packageIds)
            {
                string projectPath = Path.Combine(
                    repositoryRoot,
                    "src",
                    packageId,
                    $"{packageId}.csproj");
                CommandResult buildResult = await RunDotnetAsync(
                    repositoryRoot,
                    [
                        "build",
                        projectPath,
                        "-c",
                        "Release",
                        "--nologo",
                        "-p:BuildInParallel=false",
                    ]);
                if (buildResult.ExitCode != 0)
                {
                    throw new InvalidOperationException(buildResult.Output);
                }

                CommandResult packResult = await RunDotnetAsync(
                    repositoryRoot,
                    [
                        "pack",
                        projectPath,
                        "-c",
                        "Release",
                        "--no-build",
                        "--nologo",
                        "-p:BuildInParallel=false",
                        $"-p:PackageOutputPath={packageRoot}",
                    ]);
                if (packResult.ExitCode != 0)
                {
                    throw new InvalidOperationException(packResult.Output);
                }

                string packagePath = FindPackage(packageRoot, packageId);
                string symbolPackagePath = Directory
                    .GetFiles(packageRoot, "*.snupkg")
                    .Single(path => string.Equals(
                        ReadPackageId(path),
                        packageId,
                        StringComparison.Ordinal));
                foreach (string targetFramework in new[] { "net8.0", "net9.0", "net10.0" })
                {
                    string releaseRoot = Path.Combine(
                        repositoryRoot,
                        "src",
                        packageId,
                        "bin",
                        "Release",
                        targetFramework);
                    await AssertPackageEntryMatchesFileAsync(
                        packagePath,
                        $"lib/{targetFramework}/{packageId}.dll",
                        Path.Combine(releaseRoot, $"{packageId}.dll"));
                    await AssertPackageEntryMatchesFileAsync(
                        symbolPackagePath,
                        $"lib/{targetFramework}/{packageId}.pdb",
                        Path.Combine(releaseRoot, $"{packageId}.pdb"));
                }

                await Assert.That(new FileInfo(packagePath).Length).IsGreaterThan(10_000);
                await Assert.That(new FileInfo(packagePath).Length).IsLessThan(10_000_000);
                await Assert.That(new FileInfo(symbolPackagePath).Length).IsGreaterThan(10_000);
                await Assert.That(new FileInfo(symbolPackagePath).Length).IsLessThan(10_000_000);
                await AssertManagedPackageRepositoryMetadataAsync(packagePath, repositoryRoot);
                await AssertManagedPackageRepositoryMetadataAsync(symbolPackagePath, repositoryRoot);
            }
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task NativeInstallMetadataMatchesCurrentContractsAndAssets()
    {
        string repositoryRoot = FindRepositoryRoot();
        if (!TryGetExecutionInputs(
            repositoryRoot,
            out NativeExecutionInputs inputs,
            out string reason))
        {
            HandleMissingExecutionPrerequisites(
                nameof(NativeInstallMetadataMatchesCurrentContractsAndAssets),
                reason);
            return;
        }

        var arguments = new List<string>
        {
            "-NoProfile",
            "-File",
            "eng/native-install-metadata.ps1",
            "-Operation",
            "Verify",
            "-Rid",
            inputs.Platform.Rid,
        };
        if (inputs.Platform.Rid == "win-x64")
        {
            arguments.Add("-RequireVulkanRuntime");
        }
        CommandResult result = await RunProcessAsync(
            "pwsh",
            repositoryRoot,
            arguments.ToArray(),
            nugetPackagesRoot: null,
            sanitizeRuntimeEnvironment: false,
            runtimeEnvironment: null);
        await Assert.That(result.ExitCode)
            .IsEqualTo(0)
            .Because(result.Output);
        await Assert.That(result.Output)
            .Contains($"data ABI {RequiredDataAbiVersion}");
        await Assert.That(result.Output)
            .Contains($"capabilities 0x{RequiredDataCapabilities:X}");
        await Assert.That(result.Output).Contains("camera state v1");
        await Assert.That(result.Output)
            .Contains($"Storm ABI {RequiredStormAbiVersion}");
        await Assert.That(result.Output)
            .Contains(
                $"Silk session/page ABI {RequiredSilkSessionAbiVersion}/" +
                $"{RequiredSilkPageAbiVersion}");
        await Assert.That(result.Output)
            .Contains($"Storm child ABI {RequiredStormChildAbiVersion}");
        await Assert.That(result.Output).Contains(
            $"navigation v{RequiredStormChildNavigationInputVersion}");

        string metadataPath = Path.Combine(
            inputs.InstallRoot,
            ".openusd-install-metadata.json");
        using JsonDocument metadata = JsonDocument.Parse(
            await File.ReadAllTextAsync(metadataPath));
        JsonElement root = metadata.RootElement;
        await Assert.That(root.GetProperty("schemaVersion").GetInt32()).IsEqualTo(3);
        await Assert.That(root.GetProperty("shimDataAbiVersion").GetUInt32())
            .IsEqualTo(RequiredDataAbiVersion);
        await Assert.That(root.GetProperty("shimDataCapabilities").GetUInt64())
            .IsEqualTo(RequiredDataCapabilities);
        await Assert.That(root.GetProperty("dataCameraStateVersion").GetInt32())
            .IsEqualTo(1);
        await Assert.That(root.GetProperty("stormAbiVersion").GetInt32())
            .IsEqualTo(RequiredStormAbiVersion);
        await Assert.That(root.GetProperty("silkSessionAbiVersion").GetInt32())
            .IsEqualTo(RequiredSilkSessionAbiVersion);
        await Assert.That(root.GetProperty("shimPageAbiVersion").GetInt32())
            .IsEqualTo(RequiredSilkPageAbiVersion);
        await Assert.That(root.GetProperty("stormChildAbiVersion").GetInt32())
            .IsEqualTo(RequiredStormChildAbiVersion);
        await Assert.That(root.GetProperty("stormChildNavigationInputVersion").GetInt32())
            .IsEqualTo(RequiredStormChildNavigationInputVersion);
        await Assert.That(root.GetProperty("lockSha256").GetString())
            .IsEqualTo(GetFileSha256(Path.Combine(repositoryRoot, "eng", "openusd.lock.json")));
        await Assert.That(root.GetProperty("dataSourceSha256").GetString())
            .IsEqualTo(GetFileSha256(
                ResolveMetadataHashedSource(repositoryRoot, "dataAbiSource")));
        await Assert.That(root.GetProperty("stormChildSourceSha256").GetString())
            .IsEqualTo(GetFileSha256(
                ResolveMetadataHashedSource(repositoryRoot, "stormChildSource")));

        string nativeDirectory = inputs.Platform.Rid == "win-x64" ? "bin" : "lib";
        var hashedAssets = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dataHeaderSha256"] = Path.Combine(
                inputs.ShimRoot,
                "include",
                "openusd_dotnet.h"),
            ["dataLibrarySha256"] = Path.Combine(
                inputs.ShimRoot,
                nativeDirectory,
                inputs.Platform.DotnetLibrary),
            ["hydraHeaderSha256"] = Path.Combine(
                inputs.ShimRoot,
                "include",
                "openusd_hydra.h"),
            ["hydraLibrarySha256"] = Path.Combine(
                inputs.ShimRoot,
                nativeDirectory,
                inputs.Platform.HydraLibrary),
            ["hdSilkHeaderSha256"] = Path.Combine(
                inputs.ShimRoot,
                "include",
                "openusd_hdsilk.h"),
            ["hdSilkLibrarySha256"] = Path.Combine(
                inputs.ShimRoot,
                nativeDirectory,
                inputs.Platform.HdSilkLibrary),
            ["renderCameraHeaderSha256"] = Path.Combine(
                inputs.ShimRoot,
                "include",
                "openusd_render_camera.h"),
            ["renderLightingHeaderSha256"] = Path.Combine(
                inputs.ShimRoot,
                "include",
                "openusd_render_lighting.h"),
            ["renderPickHeaderSha256"] = Path.Combine(
                inputs.ShimRoot,
                "include",
                "openusd_render_pick.h"),
            ["stormChildHeaderSha256"] = Path.Combine(
                inputs.ShimRoot,
                "include",
                "openusd_storm_child.h"),
            ["stormChildLibrarySha256"] = Path.Combine(
                inputs.ShimRoot,
                nativeDirectory,
                inputs.Platform.StormChildLibrary),
        };
        foreach ((string propertyName, string assetPath) in hashedAssets)
        {
            await Assert.That(File.Exists(assetPath)).IsTrue();
            await Assert.That(root.GetProperty(propertyName).GetString())
                .IsEqualTo(GetFileSha256(assetPath));
        }

        await AssertFileHashesEqualAsync(
            Path.Combine(
                repositoryRoot,
                "native",
                "openusd_dotnet",
                "include",
                "openusd_dotnet.h"),
            hashedAssets["dataHeaderSha256"]);
        await AssertFileHashesEqualAsync(
            Path.Combine(repositoryRoot, "native", "openusd_hydra", "include", "openusd_hydra.h"),
            hashedAssets["hydraHeaderSha256"]);
        await AssertFileHashesEqualAsync(
            Path.Combine(repositoryRoot, "native", "hdSilk", "include", "openusd_hdsilk.h"),
            hashedAssets["hdSilkHeaderSha256"]);
        await AssertFileHashesEqualAsync(
            Path.Combine(repositoryRoot, "native", "include", "openusd_render_camera.h"),
            hashedAssets["renderCameraHeaderSha256"]);
        await AssertFileHashesEqualAsync(
            Path.Combine(repositoryRoot, "native", "include", "openusd_render_pick.h"),
            hashedAssets["renderPickHeaderSha256"]);
        await AssertFileHashesEqualAsync(
            Path.Combine(
                repositoryRoot,
                "native",
                "openusd_storm_child",
                "include",
                "openusd_storm_child.h"),
            hashedAssets["stormChildHeaderSha256"]);
        await AssertNoSourcePathLeakageAsync(
            await File.ReadAllTextAsync(metadataPath),
            repositoryRoot);
    }

    [Test]
    public async Task PackageOnlyInteropRejectsStaleDataContractsWithTypedErrors()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);
            PackedPackage interopPackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd.Interop",
                packageRoot);
            string consumerRoot = Path.Combine(workRoot, "abi-compatibility-consumer");
            Directory.CreateDirectory(consumerRoot);
            await File.WriteAllTextAsync(
                Path.Combine(consumerRoot, "Directory.Build.props"),
                "<Project />");
            await File.WriteAllTextAsync(
                Path.Combine(consumerRoot, "Directory.Packages.props"),
                """
                <Project>
                  <PropertyGroup>
                    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(consumerRoot, "NuGet.config"),
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="isolated-openusd-feed" value="{packageRoot}" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                  <packageSourceMapping>
                    <packageSource key="isolated-openusd-feed">
                      <package pattern="OpenUsd*" />
                    </packageSource>
                    <packageSource key="nuget.org">
                      <package pattern="Microsoft.*" />
                      <package pattern="runtime.*" />
                    </packageSource>
                  </packageSourceMapping>
                </configuration>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(consumerRoot, "Consumer.csproj"),
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="OpenUsd.Interop"
                                      Version="{interopPackage.Version}" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(consumerRoot, "Program.cs"),
                $$"""
                using System;
                using System.Reflection;
                using OpenUsd.Interop;

                if (OpenUsdNativeContract.AbiVersion != {{RequiredDataAbiVersion}}U ||
                    OpenUsdNativeContract.RequiredCapabilities !=
                        {{RequiredDataCapabilities}}UL)
                {
                    return 2;
                }

                MethodInfo validator = typeof(OpenUsdNativeRuntime).GetMethod(
                    "ValidateAbiCompatibility",
                    BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException("ABI validator was not found.");
                Type? staleAbiError = Reject(
                    validator,
                    {{PreviousDataAbiVersion}}U,
                    {{RequiredDataCapabilities}}UL);
                Type? oldCapabilitiesError = Reject(
                    validator,
                    {{RequiredDataAbiVersion}}U,
                    {{PreviousDataCapabilities}}UL);
                Console.WriteLine($"STALE_ABI6_REJECTED={staleAbiError?.Name}");
                Console.WriteLine(
                    $"OLD_CAPABILITIES_0x7F_REJECTED={oldCapabilitiesError?.Name}");
                return staleAbiError == typeof(OpenUsdNativeException) &&
                    oldCapabilitiesError == typeof(OpenUsdNativeException)
                    ? 0
                    : 3;

                static Type? Reject(MethodInfo validator, uint abi, ulong capabilities)
                {
                    try
                    {
                        validator.Invoke(null, [abi, capabilities]);
                        return null;
                    }
                    catch (TargetInvocationException exception)
                    {
                        return exception.InnerException?.GetType();
                    }
                }
                """);

            string globalPackagesRoot = Path.Combine(workRoot, "abi-global-packages");
            CommandResult restoreResult = await RunDotnetAsync(
                consumerRoot,
                [
                    "restore",
                    "Consumer.csproj",
                    "--nologo",
                    "--configfile",
                    "NuGet.config",
                ],
                globalPackagesRoot);
            if (restoreResult.ExitCode != 0)
            {
                throw new InvalidOperationException(restoreResult.Output);
            }
            CommandResult runResult = await RunDotnetAsync(
                consumerRoot,
                [
                    "run",
                    "--project",
                    "Consumer.csproj",
                    "-c",
                    "Release",
                    "--no-restore",
                    "--nologo",
                ],
                globalPackagesRoot);
            await Assert.That(runResult.ExitCode)
                .IsEqualTo(0)
                .Because(runResult.Output);
            await Assert.That(runResult.Output)
                .Contains("STALE_ABI6_REJECTED=OpenUsdNativeException");
            await Assert.That(runResult.Output)
                .Contains("OLD_CAPABILITIES_0x7F_REJECTED=OpenUsdNativeException");
            AssertPackageOnlyGraph(
                Path.Combine(consumerRoot, "obj", "project.assets.json"),
                ["OpenUsd.Interop"]);
            string consumerProject = await File.ReadAllTextAsync(
                Path.Combine(consumerRoot, "Consumer.csproj"));
            await Assert.That(consumerProject).DoesNotContain("ProjectReference");
            await AssertNoSourcePathLeakageAsync(consumerProject, repositoryRoot);
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task PackageOnlyPumpSpikeAppliesOrderedExternalBatches()
    {
        string repositoryRoot = FindRepositoryRoot();
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);
            PackedPackage interopPackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd.Interop",
                packageRoot);
            PackedPackage openUsdPackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd",
                packageRoot);
            PackedPackage liveAuthoringPackage = await PackProjectPackageAsync(
                repositoryRoot,
                Path.Combine("samples", "OpenUsd.LiveAuthoring", "OpenUsd.LiveAuthoring.csproj"),
                "OpenUsd.LiveAuthoring",
                packageRoot);
            string consumerRoot = Path.Combine(workRoot, "opcua-pump-spike-consumer");
            Directory.CreateDirectory(consumerRoot);
            await File.WriteAllTextAsync(
                Path.Combine(consumerRoot, "Directory.Build.props"),
                "<Project />");
            await File.WriteAllTextAsync(
                Path.Combine(consumerRoot, "Directory.Packages.props"),
                """
                <Project>
                  <PropertyGroup>
                    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(consumerRoot, "NuGet.config"),
                $"""
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="isolated-openusd-feed" value="{packageRoot}" />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                  <packageSourceMapping>
                    <packageSource key="isolated-openusd-feed">
                      <package pattern="OpenUsd*" />
                    </packageSource>
                    <packageSource key="nuget.org">
                      <package pattern="Microsoft.*" />
                      <package pattern="runtime.*" />
                    </packageSource>
                  </packageSourceMapping>
                </configuration>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(consumerRoot, "Consumer.csproj"),
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="OpenUsd.Interop" Version="{interopPackage.Version}" />
                    <PackageReference Include="OpenUsd" Version="{openUsdPackage.Version}" />
                    <PackageReference Include="OpenUsd.LiveAuthoring"
                                      Version="{liveAuthoringPackage.Version}" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(consumerRoot, "Program.cs"),
                """
                using OpenUsd;
                using OpenUsd.LiveAuthoring;

                const int sampleCount = 24;
                var executor = new OrderingProofExecutor(sampleCount);
                await using var queue = new QueuedLiveAuthoringSink(executor, capacity: 2);
                var stageSink = new OpenUsdStageSink(queue);
                await new SimulatedOpcUaPump(stageSink, sampleCount, batchSize: 3).RunAsync();

                bool ordered = executor.SourceSequences.SequenceEqual(
                    Enumerable.Range(1, sampleCount).Select(static value => (long)value));
                bool bounded = queue.PeakPendingBatchCount <= queue.Capacity;
                bool gapDetected = await DetectGapAsync(stageSink);
                Console.WriteLine($"REAL_SINK_TYPE={typeof(ILiveAuthoringSink).FullName}");
                Console.WriteLine($"REAL_BATCH_TYPE={typeof(LiveAuthoringBatch).FullName}");
                Console.WriteLine($"ORDERED_SOURCE_SEQUENCES={ordered}");
                Console.WriteLine($"BOUNDED_PENDING={bounded}");
                Console.WriteLine($"GAP_DETECTED={gapDetected}");
                Console.WriteLine($"APPLIED={string.Join(",", executor.SourceSequences)}");
                return ordered && bounded && gapDetected ? 0 : 1;

                static async Task<bool> DetectGapAsync(IUsdSink sink)
                {
                    try
                    {
                        await sink.ApplyAsync(
                            new PumpBatch([new PumpSample(26, 12.5, "Running")]),
                            CancellationToken.None);
                        return false;
                    }
                    catch (InvalidOperationException exception)
                        when (exception.Message.Contains("strictly increasing", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                public interface IUsdSink
                {
                    ValueTask<LiveAuthoringBatchResult> ApplyAsync(
                        PumpBatch batch,
                        CancellationToken cancellationToken);
                }

                public sealed class OpenUsdStageSink(ILiveAuthoringSink inner) : IUsdSink
                {
                    private readonly SemaphoreSlim _producerGate = new(1, 1);
                    private long _lastSourceSequence;
                    private long _nextBatchSequence;
                    private bool _primDefined;

                    public async ValueTask<LiveAuthoringBatchResult> ApplyAsync(
                        PumpBatch batch,
                        CancellationToken cancellationToken)
                    {
                        ArgumentNullException.ThrowIfNull(batch);
                        Task<LiveAuthoringBatchResult> submitted;
                        await _producerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                        try
                        {
                            var updates = new List<LiveStageUpdate>();
                            if (!_primDefined)
                            {
                                updates.Add(new DefinePrimUpdate("/Plant", "Xform"));
                                updates.Add(new DefinePrimUpdate("/Plant/Pump1", "Xform"));
                                _primDefined = true;
                            }

                            foreach (PumpSample sample in batch.Samples)
                            {
                                if (sample.SourceSequence != _lastSourceSequence + 1)
                                {
                                    throw new InvalidOperationException(
                                        "Pump source sequences must be strictly increasing.");
                                }

                                _lastSourceSequence = sample.SourceSequence;
                                updates.Add(new SetScalarUpdate(
                                    "/Plant/Pump1",
                                    "custom:sourceSequence",
                                    LiveScalarValue.FromInt64(sample.SourceSequence),
                                    TimeCode: sample.SourceSequence));
                                updates.Add(new SetScalarUpdate(
                                    "/Plant/Pump1",
                                    "custom:pressure",
                                    LiveScalarValue.FromDouble(sample.Pressure),
                                    TimeCode: sample.SourceSequence));
                                updates.Add(new SetScalarUpdate(
                                    "/Plant/Pump1",
                                    "custom:state",
                                    LiveScalarValue.FromToken(sample.State),
                                    TimeCode: sample.SourceSequence));
                            }

                            var usdBatch = new LiveAuthoringBatch(++_nextBatchSequence, updates);
                            submitted = inner.ApplyAsync(usdBatch, cancellationToken).AsTask();
                        }
                        finally
                        {
                            _producerGate.Release();
                        }

                        return await submitted.ConfigureAwait(false);
                    }
                }

                public sealed class SimulatedOpcUaPump(IUsdSink sink, int sampleCount, int batchSize)
                {
                    public async Task RunAsync(CancellationToken cancellationToken = default)
                    {
                        var pending = new List<Task<LiveAuthoringBatchResult>>();
                        for (int first = 1; first <= sampleCount; first += batchSize)
                        {
                            await Task.Yield();
                            PumpSample[] samples = Enumerable
                                .Range(first, Math.Min(batchSize, sampleCount - first + 1))
                                .Select(static sequence => new PumpSample(
                                    sequence,
                                    10 + sequence * 0.25,
                                    sequence % 2 == 0 ? "Running" : "Idle"))
                                .ToArray();
                            pending.Add(sink.ApplyAsync(new PumpBatch(samples), cancellationToken).AsTask());
                        }

                        await Task.WhenAll(pending).ConfigureAwait(false);
                    }
                }

                public sealed class OrderingProofExecutor(int expectedSamples) : ILiveAuthoringBatchExecutor
                {
                    private long _nextSourceSequence = 1;

                    public List<long> SourceSequences { get; } = [];

                    public ValueTask<LiveAuthoringBatchResult> ExecuteAsync(
                        LiveAuthoringBatch batch,
                        CancellationToken cancellationToken)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        foreach (SetScalarUpdate update in batch.Updates.OfType<SetScalarUpdate>())
                        {
                            if (update.AttributeName != "custom:sourceSequence")
                            {
                                continue;
                            }

                            long sourceSequence = update.Value.Int64Value;
                            if (sourceSequence != _nextSourceSequence)
                            {
                                throw new InvalidOperationException(
                                    $"Expected source sequence {_nextSourceSequence}, saw {sourceSequence}.");
                            }

                            SourceSequences.Add(sourceSequence);
                            _nextSourceSequence++;
                        }

                        return ValueTask.FromResult(new LiveAuthoringBatchResult(
                            batch.Sequence,
                            batch.Sequence,
                            1,
                            batch.Updates.Count,
                            batch.Invalidation,
                            (ulong)batch.Sequence,
                            (ulong)batch.Sequence + 1,
                            "session"));
                    }

                    public ValueTask DisposeAsync()
                    {
                        if (SourceSequences.Count != expectedSamples)
                        {
                            throw new InvalidOperationException(
                                $"Expected {expectedSamples} samples, saw {SourceSequences.Count}.");
                        }

                        return ValueTask.CompletedTask;
                    }
                }

                public sealed record PumpBatch(IReadOnlyList<PumpSample> Samples);

                public sealed record PumpSample(long SourceSequence, double Pressure, string State);
                """);

            string globalPackagesRoot = Path.Combine(workRoot, "opcua-global-packages");
            CommandResult restoreResult = await RunDotnetAsync(
                consumerRoot,
                [
                    "restore",
                    "Consumer.csproj",
                    "--nologo",
                    "--configfile",
                    "NuGet.config",
                ],
                globalPackagesRoot);
            if (restoreResult.ExitCode != 0)
            {
                throw new InvalidOperationException(restoreResult.Output);
            }

            CommandResult runResult = await RunDotnetAsync(
                consumerRoot,
                [
                    "run",
                    "--project",
                    "Consumer.csproj",
                    "-c",
                    "Release",
                    "--no-restore",
                    "--nologo",
                ],
                globalPackagesRoot);
            await Assert.That(runResult.ExitCode)
                .IsEqualTo(0)
                .Because(runResult.Output);
            await Assert.That(runResult.Output)
                .Contains("REAL_SINK_TYPE=OpenUsd.LiveAuthoring.ILiveAuthoringSink");
            await Assert.That(runResult.Output)
                .Contains("REAL_BATCH_TYPE=OpenUsd.LiveAuthoring.LiveAuthoringBatch");
            await Assert.That(runResult.Output).Contains("ORDERED_SOURCE_SEQUENCES=True");
            await Assert.That(runResult.Output).Contains("BOUNDED_PENDING=True");
            await Assert.That(runResult.Output).Contains("GAP_DETECTED=True");
            AssertPackageOnlyGraph(
                Path.Combine(consumerRoot, "obj", "project.assets.json"),
                ["OpenUsd.Interop", "OpenUsd", "OpenUsd.LiveAuthoring"]);
            string consumerProject = await File.ReadAllTextAsync(
                Path.Combine(consumerRoot, "Consumer.csproj"));
            await Assert.That(consumerProject).DoesNotContain("ProjectReference");
            await AssertNoSourcePathLeakageAsync(consumerProject, repositoryRoot);
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task CesiumPackageExecutesTilesetReadFromCleanFeed()
    {
        string repositoryRoot = FindRepositoryRoot();
        if (!TryGetExecutionInputs(
            repositoryRoot,
            out NativeExecutionInputs inputs,
            out string reason))
        {
            HandleMissingExecutionPrerequisites(
                nameof(CesiumPackageExecutesTilesetReadFromCleanFeed),
                reason);
            return;
        }

        ExecutionPlatform platform = inputs.Platform;
        string cesiumLibrary = GetCesiumLibraryName(platform);
        string shimNativeDirectory = platform.Rid == "win-x64" ? "bin" : "lib";
        string installedCesiumPath = Path.Combine(
            inputs.CesiumShimRoot,
            shimNativeDirectory,
            cesiumLibrary);
        if (!File.Exists(installedCesiumPath))
        {
            // Publication of the Cesium packages is deferred, so nothing in CI
            // builds the shim any more and its absence here is expected rather
            // than broken. Keyed off the packer's own deferral list so that
            // re-enabling publication makes this gate required again without a
            // second edit.
            if (IsPublicationDeferred(repositoryRoot, $"OpenUsd.Runtime.Cesium.{platform.Rid}"))
            {
                Console.WriteLine(
                    "PACKAGE_EXECUTION_DEFERRED: " +
                    $"{nameof(CesiumPackageExecutesTilesetReadFromCleanFeed)} did not run because " +
                    $"OpenUsd.Runtime.Cesium.{platform.Rid} is withheld from every pack scope by " +
                    "eng/pack-packages.ps1, so no job builds the Cesium shim.");
                return;
            }

            HandleMissingExecutionPrerequisites(
                nameof(CesiumPackageExecutesTilesetReadFromCleanFeed),
                $"The Cesium shim is missing at '{installedCesiumPath}'.");
            return;
        }

        string workRoot = Path.Combine(repositoryRoot, "artifacts", "pc");
        if (Directory.Exists(workRoot))
        {
            Directory.Delete(workRoot, recursive: true);
        }
        Directory.CreateDirectory(workRoot);
        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);
            foreach (string packageId in new[]
            {
                "OpenUsd.Interop",
                "OpenUsd",
                "OpenUsd.Cesium",
                "OpenUsd.Runtime.Core",
                "OpenUsd.Runtime.Cesium",
            })
            {
                await PackManagedPackageAsync(repositoryRoot, packageId, packageRoot);
            }

            PackedPackage coreRuntimePackage = await PackAsync(
                repositoryRoot,
                $"OpenUsd.Runtime.Core.{platform.Rid}",
                inputs.InstallRoot,
                inputs.ShimRoot,
                inputs.VulkanRuntimeLibrary,
                packageRoot);
            PackedPackage cesiumRuntimePackage = await PackAsync(
                repositoryRoot,
                $"OpenUsd.Runtime.Cesium.{platform.Rid}",
                inputs.InstallRoot,
                inputs.ShimRoot,
                inputs.VulkanRuntimeLibrary,
                packageRoot,
                cesiumShimRoot: inputs.CesiumShimRoot);
            await Assert.That(cesiumRuntimePackage.Version).IsEqualTo(coreRuntimePackage.Version);
            await AssertPackageEntryMatchesFileAsync(
                cesiumRuntimePackage.Path,
                $"runtimes/{platform.Rid}/native/{cesiumLibrary}",
                installedCesiumPath);
            await CreateStubRuntimePackagesAsync(
                workRoot,
                packageRoot,
                cesiumRuntimePackage.Version,
                [.. GetCesiumConsumerPackageGraph(platform)
                    .Where(id => id.StartsWith("OpenUsd.Runtime.", StringComparison.Ordinal))
                    .Where(id => id != "OpenUsd.Runtime.Core")
                    .Where(id => id != "OpenUsd.Runtime.Cesium")
                    .Where(id => id != $"OpenUsd.Runtime.Core.{platform.Rid}")
                    .Where(id => id != $"OpenUsd.Runtime.Cesium.{platform.Rid}")]);

            ExecutionConsumer consumer = await PublishCesiumConsumerAsync(
                workRoot,
                packageRoot,
                cesiumRuntimePackage.Version,
                platform);
            AssertPackageOnlyGraph(
                consumer.AssetsPath,
                GetCesiumConsumerPackageGraph(platform));
            await Assert.That(File.Exists(Path.Combine(consumer.PublishRoot, cesiumLibrary))).IsTrue();
            await AssertFileHashesEqualAsync(
                installedCesiumPath,
                Path.Combine(consumer.PublishRoot, cesiumLibrary));

            CommandResult result = await RunExecutableAsync(
                GetExecutablePath(consumer.PublishRoot, "Consumer"),
                consumer.PublishRoot,
                [],
                GetCoreRuntimeEnvironment(platform, consumer.PublishRoot));

            Console.WriteLine(result.Output.Trim());
            await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Output);
            await Assert.That(result.Output).Contains("PACKAGE_CESIUM_TILESET_OK");
            await Assert.That(result.Output).Contains("TILESET_REQUESTED=true");
            await Assert.That(result.Output).Contains("SHIM_PRESENT=true");
            await Assert.That(result.Output).Contains("CWD_IS_PUBLISH=true");
            await AssertNoSourcePathLeakageAsync(result.Output, repositoryRoot);
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    /// <summary>
    /// Drives the retained physics runtime from packages alone, on a clean feed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing else proves the physics packages work. The layout tests pack against a synthetic
    /// install, so they can only assert which names appear; the managed suites use the repository's
    /// staged native tree, which no consumer has. This publishes a real application from a feed that
    /// contains only the packed archives, simulates a stage through it, and requires bodies to have
    /// moved -- so a package that resolves, restores, and then finds no solver fails here rather
    /// than in a consumer's application.
    /// </para>
    /// <para>
    /// The consumer is published NativeAOT and self-contained, matching every other package
    /// execution gate here. Trim and AOT analysis are errors in that publish, so a package whose
    /// trimming metadata went missing fails the publish rather than surviving to a run that faults
    /// on a member the trimmer removed.
    /// </para>
    /// </remarks>
    [Test]
    public async Task PhysicsPackageExecutesRetainedSimulationFromCleanFeed()
    {
        string repositoryRoot = FindRepositoryRoot();
        if (!TryGetExecutionInputs(
            repositoryRoot,
            out NativeExecutionInputs inputs,
            out string reason))
        {
            HandleMissingExecutionPrerequisites(
                nameof(PhysicsPackageExecutesRetainedSimulationFromCleanFeed),
                reason);
            return;
        }

        ExecutionPlatform platform = inputs.Platform;
        if (platform.Rid == "osx-arm64")
        {
            // Not a missing prerequisite: the pinned vcpkg PhysX port declares no arm64-osx
            // support, so no macOS physics package exists to execute. Reported through the
            // structural-capability path so that OPENUSD_PACKAGE_EXECUTION_REQUIRED, which is set
            // on the macOS render job, cannot demand a run of something that cannot be built.
            HandleUnavailableHostCapability(
                nameof(PhysicsPackageExecutesRetainedSimulationFromCleanFeed),
                "PhysX simulation SDK",
                "the pinned vcpkg PhysX port supports win-x64 and linux-x64 only, so macOS has " +
                "no physics package to execute");
            return;
        }

        string physicsLibrary = GetPhysicsLibraryName(platform);
        string shimNativeDirectory = platform.Rid == "win-x64" ? "bin" : "lib";
        string installedPhysicsPath = Path.Combine(
            inputs.PhysicsShimRoot,
            shimNativeDirectory,
            physicsLibrary);
        if (!File.Exists(installedPhysicsPath))
        {
            HandleMissingExecutionPrerequisites(
                nameof(PhysicsPackageExecutesRetainedSimulationFromCleanFeed),
                $"The physics shim is missing at '{installedPhysicsPath}'. " +
                $"Run eng/build-physx-native.ps1 -Rid {platform.Rid} and " +
                $"eng/build-physx-shim.ps1 -Rid {platform.Rid}.");
            return;
        }

        string workRoot = Path.Combine(repositoryRoot, "artifacts", "pp");
        if (Directory.Exists(workRoot))
        {
            Directory.Delete(workRoot, recursive: true);
        }
        Directory.CreateDirectory(workRoot);
        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);
            foreach (string packageId in new[]
            {
                "OpenUsd.Interop",
                "OpenUsd",
                "OpenUsd.Physics",
                "OpenUsd.Runtime.Core",
                "OpenUsd.Runtime.Physics",
            })
            {
                await PackManagedPackageAsync(repositoryRoot, packageId, packageRoot);
            }

            PackedPackage coreRuntimePackage = await PackAsync(
                repositoryRoot,
                $"OpenUsd.Runtime.Core.{platform.Rid}",
                inputs.InstallRoot,
                inputs.ShimRoot,
                inputs.VulkanRuntimeLibrary,
                packageRoot);
            PackedPackage physicsRuntimePackage = await PackAsync(
                repositoryRoot,
                $"OpenUsd.Runtime.Physics.{platform.Rid}",
                inputs.InstallRoot,
                inputs.ShimRoot,
                inputs.VulkanRuntimeLibrary,
                packageRoot,
                physicsShimRoot: inputs.PhysicsShimRoot,
                physXInstallRoot: inputs.PhysXInstallRoot);
            await Assert.That(physicsRuntimePackage.Version).IsEqualTo(coreRuntimePackage.Version);
            await AssertPackageEntryMatchesFileAsync(
                physicsRuntimePackage.Path,
                $"runtimes/{platform.Rid}/native/{physicsLibrary}",
                installedPhysicsPath);
            await AssertPhysicsAbiEvidenceAsync(
                physicsRuntimePackage.Path,
                platform.Rid);
            await AssertNoProprietaryPhysXModulesAsync(physicsRuntimePackage.Path);
            await CreateStubRuntimePackagesAsync(
                workRoot,
                packageRoot,
                physicsRuntimePackage.Version,
                [.. GetPhysicsConsumerPackageGraph(platform)
                    .Where(id => id.StartsWith("OpenUsd.Runtime.", StringComparison.Ordinal))
                    .Where(id => id != "OpenUsd.Runtime.Core")
                    .Where(id => id != "OpenUsd.Runtime.Physics")
                    .Where(id => id != $"OpenUsd.Runtime.Core.{platform.Rid}")
                    .Where(id => id != $"OpenUsd.Runtime.Physics.{platform.Rid}")]);

            ExecutionConsumer consumer = await PublishPhysicsConsumerAsync(
                workRoot,
                packageRoot,
                physicsRuntimePackage.Version,
                platform);
            AssertPackageOnlyGraph(
                consumer.AssetsPath,
                GetPhysicsConsumerPackageGraph(platform));

            // Referencing the managed physics library must never resolve a proprietary NVIDIA
            // payload, because no package publishes one.
            string assets = await File.ReadAllTextAsync(consumer.AssetsPath);
            await Assert.That(assets).DoesNotContain("Cuda");

            await Assert.That(File.Exists(Path.Combine(consumer.PublishRoot, physicsLibrary))).IsTrue();
            await AssertFileHashesEqualAsync(
                installedPhysicsPath,
                Path.Combine(consumer.PublishRoot, physicsLibrary));
            await WritePhysicsConsumerStageAsync(consumer.PublishRoot);

            CommandResult result = await RunExecutableAsync(
                GetExecutablePath(consumer.PublishRoot, "Consumer"),
                consumer.PublishRoot,
                [],
                GetCoreRuntimeEnvironment(platform, consumer.PublishRoot));

            Console.WriteLine(result.Output.Trim());
            await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Output);
            await Assert.That(result.Output).Contains("PACKAGE_PHYSICS_TRANSPORT_OK");
            await Assert.That(result.Output).Contains("PACKAGE_PHYSICS_SOLVER_PRESENT=true");
            await Assert.That(result.Output).Contains("PACKAGE_PHYSICS_SCHEMA_EXTRACTED=true");
            await Assert.That(result.Output).Contains("PACKAGE_PHYSICS_RIGID_BODIES=true");
            // A package-only deployment resolves no GPU module, because no package publishes one,
            // so it must claim no GPU capability either.
            await Assert.That(result.Output).Contains("PACKAGE_PHYSICS_CUDA_MODULE_PRESENT=false");
            await Assert.That(result.Output).Contains("PACKAGE_PHYSICS_CUDA_CAPABILITY=false");

            // Only the consumer's own lines are checked for leakage. The simulation SDK prints its
            // own build path from a __FILE__ literal when the optional GPU module is absent, which
            // is exactly the expected CPU-only case here and is baked into a third-party binary
            // rather than produced by anything this repository controls.
            await AssertNoSourcePathLeakageAsync(
                string.Join(
                    Environment.NewLine,
                    result.Output
                        .Split('\n')
                        .Select(line => line.TrimEnd('\r'))
                        .Where(line => line.StartsWith("PACKAGE_PHYSICS_", StringComparison.Ordinal))),
                repositoryRoot);
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task ManagedPackagesExecuteNativeAotStageRoundTrip()
    {
        string repositoryRoot = FindRepositoryRoot();
        if (!TryGetExecutionInputs(
            repositoryRoot,
            out NativeExecutionInputs inputs,
            out string reason))
        {
            HandleMissingExecutionPrerequisites(
                nameof(ManagedPackagesExecuteNativeAotStageRoundTrip),
                reason);
            return;
        }

        ExecutionPlatform platform = inputs.Platform;
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);

            PackedPackage interopPackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd.Interop",
                packageRoot);
            PackedPackage openUsdPackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd",
                packageRoot);
            PackedPackage runtimePackage = await PackAsync(
                repositoryRoot,
                $"OpenUsd.Runtime.Core.{platform.Rid}",
                inputs.InstallRoot,
                inputs.ShimRoot,
                inputs.VulkanRuntimeLibrary,
                packageRoot);
            PackedPackage runtimeMetaPackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Core",
                packageRoot);
            await CreateStubRuntimePackagesAsync(
                workRoot,
                packageRoot,
                runtimePackage.Version,
                [.. GetCoreMetaPackageGraph().Where(id =>
                    id != "OpenUsd.Runtime.Core" &&
                    id != $"OpenUsd.Runtime.Core.{platform.Rid}")]);
            string shimNativeDirectory = platform.Rid == "win-x64" ? "bin" : "lib";
            string installedDataShim = Path.Combine(
                inputs.ShimRoot,
                shimNativeDirectory,
                platform.DotnetLibrary);
            string installedOpenUsd = Path.Combine(
                inputs.InstallRoot,
                "lib",
                platform.OpenUsdLibrary);
            await AssertPackageEntryMatchesFileAsync(
                runtimePackage.Path,
                $"runtimes/{platform.Rid}/native/{platform.DotnetLibrary}",
                installedDataShim);
            await AssertPackageEntryMatchesFileAsync(
                runtimePackage.Path,
                $"runtimes/{platform.Rid}/native/{platform.OpenUsdLibrary}",
                installedOpenUsd);
            await Assert.That(new FileInfo(runtimePackage.Path).Length)
                .IsGreaterThan(1_000_000);
            await Assert.That(new FileInfo(runtimePackage.Path).Length)
                .IsLessThan(250_000_000);

            await Assert.That(openUsdPackage.Version).IsEqualTo(interopPackage.Version);
            await Assert.That(runtimePackage.Version).IsEqualTo(openUsdPackage.Version);
            await Assert.That(runtimeMetaPackage.Version).IsEqualTo(openUsdPackage.Version);

            ExecutionConsumer consumer = await PublishExecutionConsumerAsync(
                workRoot,
                packageRoot,
                openUsdPackage.Version,
                platform);
            AssertPackageOnlyGraph(
                consumer.AssetsPath,
                [
                    "OpenUsd",
                    "OpenUsd.Interop",
                    .. GetCoreMetaPackageGraph(),
                ]);

            string inputPath = Path.Combine(consumer.PublishRoot, "input.usda");
            string outputPath = Path.Combine(consumer.PublishRoot, "roundtrip.usda");
            await File.WriteAllTextAsync(
                inputPath,
                """
                #usda 1.0

                def Xform "Input" {
                }

                def Camera "Camera"
                {
                    token projection = "perspective"
                    float focalLength = 50
                    float horizontalAperture = 20
                    float verticalAperture = 10
                    float horizontalApertureOffset = 1
                    float verticalApertureOffset = -0.5
                    float2 clippingRange = (0.25, 500)
                    float focusDistance = 12
                    float fStop = 2.8
                }
                """);

            // A vendor plugin tree is staged beside the packaged trees, not merged into them. It
            // is a resource plugin rather than a real ArResolver, so this gate covers the
            // packaging contract - discovery in place, own resource path, packaged plugInfo.json
            // untouched - while the CTest native probe covers executable resolver behaviour.
            string vendorPluginRoot = Path.Combine(
                consumer.PublishRoot,
                "vendor-plugins",
                "openusdVendorResolver",
                "resources");
            Directory.CreateDirectory(vendorPluginRoot);
            string contextAssetRoot = Path.Combine(consumer.PublishRoot, "assets");
            Directory.CreateDirectory(contextAssetRoot);
            await File.WriteAllTextAsync(
                Path.Combine(contextAssetRoot, "context-asset.usda"),
                """
                #usda 1.0

                def Xform "ContextAsset" {
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(vendorPluginRoot, "plugInfo.json"),
                """
                {
                    "Plugins": [
                        {
                            "Info": {},
                            "Name": "openusdVendorResolver",
                            "ResourcePath": "resources",
                            "Root": "..",
                            "Type": "resource"
                        }
                    ]
                }
                """);

            CommandResult result = await RunExecutableAsync(
                GetExecutablePath(consumer.PublishRoot, "Consumer"),
                consumer.PublishRoot,
                ["input.usda", "roundtrip.usda"],
                GetCoreRuntimeEnvironment(platform, consumer.PublishRoot));

            Console.WriteLine(result.Output.Trim());
            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(result.Output).Contains("PACKAGE_EXECUTION_OK");
            await Assert.That(result.Output).Contains($"ABI={RequiredDataAbiVersion}");
            await Assert.That(result.Output)
                .Contains($"CAPABILITIES=0x{RequiredDataCapabilities:X}");
            await Assert.That(result.Output).Contains("INPUT_OPENED=true");
            await Assert.That(result.Output).Contains("CAMERA_STATE_QUERY=true");
            await Assert.That(result.Output).Contains("ROUNDTRIP_SAVED=true");
            await Assert.That(result.Output).Contains("ROUNDTRIP_VALUE=42.5");
            await Assert.That(result.Output).Contains("RESOLVER_PRIMARY=ArDefaultResolver");
            await Assert.That(result.Output).Contains("RESOLVER_BULK=true");
            await Assert.That(result.Output).Contains("RESOLVER_SCOPED=true");
            await Assert.That(result.Output).Contains("VENDOR_PLUGINS=1");
            await Assert.That(result.Output).Contains("VENDOR_PLUGIN_TREE=true");
            await Assert.That(result.Output).Contains("PACKAGED_PLUGIN_TREE_INTACT=true");
            await Assert.That(result.Output).Contains("CWD_IS_PUBLISH=true");
            await AssertNoSourcePathLeakageAsync(result.Output, repositoryRoot);

            await Assert.That(File.Exists(outputPath)).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(outputPath)).Contains("PackageRoundTrip");
            await Assert.That(File.Exists(Path.Combine(consumer.PublishRoot, "usd", "plugInfo.json"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(vendorPluginRoot, "plugInfo.json"))).IsTrue();
            await Assert.That(
                    await File.ReadAllTextAsync(
                        Path.Combine(consumer.PublishRoot, "usd", "plugInfo.json")))
                .DoesNotContain("openusdVendorResolver");
            await Assert.That(
                File.Exists(Path.Combine(consumer.PublishRoot, platform.DotnetLibrary))).IsTrue();
            await Assert.That(
                File.Exists(Path.Combine(consumer.PublishRoot, platform.OpenUsdLibrary))).IsTrue();
            await AssertFileHashesEqualAsync(
                installedDataShim,
                Path.Combine(consumer.PublishRoot, platform.DotnetLibrary));
            await AssertFileHashesEqualAsync(
                installedOpenUsd,
                Path.Combine(consumer.PublishRoot, platform.OpenUsdLibrary));
            if (platform.Rid == "win-x64")
            {
                await Assert.That(
                    File.Exists(Path.Combine(consumer.PublishRoot, "vulkan-1.dll"))).IsTrue();
            }

            string consumerProject = await File.ReadAllTextAsync(consumer.ProjectPath);
            await Assert.That(consumerProject).DoesNotContain("ProjectReference");
            await AssertNoSourcePathLeakageAsync(consumerProject, repositoryRoot);
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task ImagingPackagesExecuteNativeAotHdSilkGpuUpload()
    {
        string repositoryRoot = FindRepositoryRoot();
        await RequireMetalLibraryOnMacOSAsync(repositoryRoot);
        if (!TryGetExecutionInputs(
            repositoryRoot,
            out NativeExecutionInputs inputs,
            out string reason))
        {
            HandleMissingExecutionPrerequisites(
                nameof(ImagingPackagesExecuteNativeAotHdSilkGpuUpload),
                reason);
            return;
        }

        ExecutionPlatform platform = inputs.Platform;
        string workRoot = CreateWorkRoot(repositoryRoot);
        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);
            string[] managedPackageIds =
            [
                "OpenUsd.Interop",
                "OpenUsd",
                "OpenUsd.Rendering",
                "OpenUsd.Rendering.Silk",
                platform.BackendPackageId,
            ];

            var managedPackages = new List<PackedPackage>();
            foreach (string packageId in managedPackageIds)
            {
                managedPackages.Add(
                    await PackManagedPackageAsync(repositoryRoot, packageId, packageRoot));
            }
            if (platform.Rid == "osx-arm64")
            {
                await AssertMetalPackageMatchesStagedLibraryAsync(
                    FindPackage(packageRoot, platform.BackendPackageId),
                    repositoryRoot);
            }

            PackedPackage coreRuntimePackage = await PackAsync(
                repositoryRoot,
                $"OpenUsd.Runtime.Core.{platform.Rid}",
                inputs.InstallRoot,
                inputs.ShimRoot,
                inputs.VulkanRuntimeLibrary,
                packageRoot);
            PackedPackage imagingRuntimePackage = await PackAsync(
                repositoryRoot,
                $"OpenUsd.Runtime.Imaging.{platform.Rid}",
                inputs.InstallRoot,
                inputs.ShimRoot,
                inputs.VulkanRuntimeLibrary,
                packageRoot);
            PackedPackage imagingMetaPackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Imaging",
                packageRoot);
            await CreateStubRuntimePackagesAsync(
                workRoot,
                packageRoot,
                imagingRuntimePackage.Version,
                [.. GetImagingMetaPackageGraph(platform)
                    .Where(id => id.StartsWith("OpenUsd.Runtime.", StringComparison.Ordinal))
                    .Where(id => id != "OpenUsd.Runtime.Imaging")
                    .Where(id => id != $"OpenUsd.Runtime.Imaging.{platform.Rid}")
                    .Where(id => id != $"OpenUsd.Runtime.Core.{platform.Rid}")]);
            string shimNativeDirectory = platform.Rid == "win-x64" ? "bin" : "lib";
            string installedHydraPath = Path.Combine(
                inputs.ShimRoot,
                shimNativeDirectory,
                platform.HydraLibrary);
            string installedHdSilkPath = Path.Combine(
                inputs.ShimRoot,
                shimNativeDirectory,
                platform.HdSilkLibrary);
            await AssertPackageEntryMatchesFileAsync(
                imagingRuntimePackage.Path,
                $"runtimes/{platform.Rid}/native/{platform.HydraLibrary}",
                installedHydraPath);
            await AssertPackageEntryMatchesFileAsync(
                imagingRuntimePackage.Path,
                $"runtimes/{platform.Rid}/native/{platform.HdSilkLibrary}",
                installedHdSilkPath);
            await Assert.That(new FileInfo(imagingRuntimePackage.Path).Length)
                .IsGreaterThan(50_000);
            await Assert.That(new FileInfo(imagingRuntimePackage.Path).Length)
                .IsLessThan(10_000_000);
            string[] ocioDependencies = platform.Rid switch
            {
                "win-x64" =>
                [
                    "runtimes/win-x64/native/Imath-3_1.dll",
                    "runtimes/win-x64/native/OpenColorIO_2_2.dll",
                ],
                "linux-x64" =>
                [
                    "runtimes/linux-x64/native/libImath-3_1.so",
                    "runtimes/linux-x64/native/libOpenColorIO.so",
                ],
                "osx-arm64" =>
                [
                    "runtimes/osx-arm64/native/libImath-3_1.dylib",
                    "runtimes/osx-arm64/native/libOpenColorIO.dylib",
                ],
                _ => throw new InvalidOperationException(
                    $"Unsupported execution RID '{platform.Rid}'."),
            };
            await AssertPackageEntriesAsync(coreRuntimePackage.Path, ocioDependencies);

            string packageVersion = managedPackages[0].Version;
            foreach (PackedPackage package in managedPackages)
            {
                await Assert.That(package.Version).IsEqualTo(packageVersion);
            }
            await Assert.That(coreRuntimePackage.Version).IsEqualTo(packageVersion);
            await Assert.That(imagingRuntimePackage.Version).IsEqualTo(packageVersion);
            await Assert.That(imagingMetaPackage.Version).IsEqualTo(packageVersion);
            string? installedStormChildPath = null;
            if (platform.Rid == "win-x64")
            {
                installedStormChildPath = Path.Combine(
                    inputs.ShimRoot,
                    "bin",
                    "openusd_storm_child.dll");
                await AssertPackageEntryMatchesFileAsync(
                    imagingRuntimePackage.Path,
                    "runtimes/win-x64/native/openusd_storm_child.dll",
                    installedStormChildPath);
            }
            else if (platform.Rid == "osx-arm64")
            {
                installedStormChildPath = Path.Combine(
                    inputs.ShimRoot,
                    "lib",
                    "libopenusd_storm_child.dylib");
                await AssertPackageEntryMatchesFileAsync(
                    imagingRuntimePackage.Path,
                    "runtimes/osx-arm64/native/libopenusd_storm_child.dylib",
                    installedStormChildPath);
                await AssertMacOsValidationEvidenceAsync(imagingRuntimePackage.Path);
            }

            ExecutionConsumer consumer = await PublishImagingExecutionConsumerAsync(
                workRoot,
                packageRoot,
                packageVersion,
                platform,
                publishAot: true);
            AssertPackageOnlyGraph(
                consumer.AssetsPath,
                GetImagingMetaPackageGraph(platform));
            await AssertPlatformBackendAssetsAsync(platform, consumer.PublishRoot);
            if (platform.Rid == "osx-arm64")
            {
                await AssertMetalPublishedAssetsAsync(consumer.PublishRoot, repositoryRoot);
            }

            CommandResult result = await RunExecutableAsync(
                GetExecutablePath(consumer.PublishRoot, "Consumer"),
                consumer.PublishRoot,
                [],
                GetImagingRuntimeEnvironment(platform, consumer.PublishRoot));

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.Output);
            }

            Console.WriteLine(result.Output.Trim());
            await Assert.That(result.ExitCode).IsEqualTo(0);
            await Assert.That(result.Output).Contains("PACKAGE_IMAGING_EXECUTION_OK");
            await Assert.That(result.Output).Contains("FIRST_PAGE_FRAMES=1");
            await Assert.That(result.Output).Contains("FIRST_PAGE_UPSERTS=");
            await Assert.That(result.Output).Contains("FIRST_PAGE_REMOVALS=0");
            await Assert.That(result.Output).Contains("STEADY_PAGE_FRAMES=1");
            await Assert.That(result.Output).Contains("STEADY_PAGE_UPSERTS=0");
            await Assert.That(result.Output).Contains("STEADY_PAGE_REMOVALS=0");
            await Assert.That(result.Output).Contains($"{platform.UploadMarker}=true");
            await Assert.That(result.Output).Contains(
                $"GPU_BACKEND={platform.BackendDisplayName}");
            await Assert.That(result.Output).Contains("INCREMENTAL_GPU_UPLOAD=true");
            await Assert.That(result.Output).Contains("WAIT_IDLE=true");
            await Assert.That(result.Output).Contains("PLUGIN_LAYOUT=true");
            await Assert.That(result.Output).Contains("OCIO_DISPLAY_TRANSFORM=true");
            await Assert.That(result.Output).Contains("CWD_IS_PUBLISH=true");
            if (platform.Rid is "win-x64" or "osx-arm64")
            {
                int stormChildAbiVersion = ReadStormChildAbiVersion(repositoryRoot);
                await Assert.That(result.Output)
                    .Contains($"STORM_CHILD_ABI={stormChildAbiVersion}");
                await Assert.That(result.Output).Contains("STORM_CHILD_DLLIMPORT=true");
                await Assert.That(result.Output).Contains("STORM_CHILD_CAPTURE_STATUS=1");
                await Assert.That(result.Output).Contains(
                    "STORM_CHILD_CAPTURE_ERROR=A valid Storm native child is required.");
                await Assert.That(result.Output).Contains("STORM_CHILD_CAPTURE_DLLIMPORT=true");
                await Assert.That(result.Output).Contains("STORM_CHILD_NAVIGATION_STATUS=1");
                await Assert.That(result.Output).Contains(
                    "STORM_CHILD_NAVIGATION_ERROR=A valid Storm native child is required.");
                await Assert.That(result.Output).Contains("STORM_CHILD_NAVIGATION_RESET=true");
                await Assert.That(result.Output).Contains("STORM_CHILD_NAVIGATION_DLLIMPORT=true");
                await Assert.That(result.Output)
                    .Contains("STORM_CHILD_INITIALIZE_LINUX_EXPORT=false");
            }
            if (platform.Rid == "osx-arm64")
            {
                await Assert.That(result.Output).Contains("DYLD_LIBRARY_PATH_PRESENT=false");
            }
            if (platform.RequiresSwiftShader)
            {
                await Assert.That(result.Output).Contains("SOFTWARE_DEVICE=true");
            }
            await AssertNoSourcePathLeakageAsync(result.Output, repositoryRoot);

            string packagedPlugInfoPath = Path.Combine(
                consumer.PublishRoot,
                "plugin",
                "usd",
                "hdSilk",
                "resources",
                "plugInfo.json");
            await Assert.That(File.Exists(packagedPlugInfoPath)).IsTrue();
            await Assert.That(
                File.Exists(Path.Combine(
                    consumer.PublishRoot,
                    "plugin",
                    "usd",
                    "hdStorm",
                    "resources",
                    "plugInfo.json"))).IsTrue();
            string[] hdSilkLibraries = Directory
                .GetFiles(consumer.PublishRoot, "*", SearchOption.AllDirectories)
                .Where(path => IsHdSilkLibraryFile(
                    platform.Rid,
                    platform.HdSilkLibrary,
                    Path.GetFileName(path)))
                .ToArray();
            await Assert.That(hdSilkLibraries.Length).IsEqualTo(1);
            await Assert.That(hdSilkLibraries[0]).IsEqualTo(
                Path.Combine(consumer.PublishRoot, platform.HdSilkLibrary));
            if (platform.Rid is "win-x64" or "osx-arm64")
            {
                string stormChildName = platform.Rid == "win-x64"
                    ? "openusd_storm_child.dll"
                    : "libopenusd_storm_child.dylib";
                string[] stormChildLibraries = Directory.GetFiles(
                    consumer.PublishRoot,
                    stormChildName,
                    SearchOption.AllDirectories);
                await Assert.That(stormChildLibraries).HasSingleItem();
                string publishedStormChildPath = Path.Combine(
                    consumer.PublishRoot,
                    stormChildName);
                await Assert.That(stormChildLibraries[0]).IsEqualTo(publishedStormChildPath);
                await AssertFileHashesEqualAsync(
                    installedStormChildPath!,
                    publishedStormChildPath);
            }
            using (JsonDocument packagedPlugInfo = JsonDocument.Parse(
                await File.ReadAllTextAsync(packagedPlugInfoPath)))
            {
                string? packagedLibraryPath = packagedPlugInfo
                    .RootElement
                    .GetProperty("Plugins")[0]
                    .GetProperty("LibraryPath")
                    .GetString();
                await Assert.That(packagedLibraryPath).IsEqualTo(
                    platform.HdSilkPluginLibraryPath);
            }
            await Assert.That(File.Exists(Path.Combine(consumer.PublishRoot, "usd", "plugInfo.json"))).IsTrue();
            await Assert.That(
                File.Exists(Path.Combine(consumer.PublishRoot, platform.DotnetLibrary))).IsTrue();
            await Assert.That(
                File.Exists(Path.Combine(consumer.PublishRoot, platform.OpenUsdLibrary))).IsTrue();
            await AssertFileHashesEqualAsync(
                installedHydraPath,
                Path.Combine(consumer.PublishRoot, platform.HydraLibrary));
            await AssertFileHashesEqualAsync(
                installedHdSilkPath,
                Path.Combine(consumer.PublishRoot, platform.HdSilkLibrary));
            await Assert.That(File.Exists(Path.Combine(consumer.PublishRoot, "minimal.usda"))).IsTrue();

            string consumerProject = await File.ReadAllTextAsync(consumer.ProjectPath);
            await Assert.That(consumerProject).DoesNotContain("ProjectReference");
            await Assert.That(consumerProject).Contains(platform.BackendPackageId);
            await Assert.That(consumerProject).Contains("OpenUsd.Runtime.Imaging");
            await Assert.That(consumerProject).DoesNotContain($"OpenUsd.Runtime.Imaging.{platform.Rid}");
            await Assert.That(consumerProject).DoesNotContain($"OpenUsd.Runtime.Core.{platform.Rid}");
            await AssertNoSourcePathLeakageAsync(consumerProject, repositoryRoot);

            string generatedTargetsPath = Path.Combine(
                Path.GetDirectoryName(consumer.ProjectPath)!,
                "obj",
                "Consumer.csproj.nuget.g.targets");
            string generatedTargets = await File.ReadAllTextAsync(generatedTargetsPath);
            await Assert.That(generatedTargets).Contains(
                $"OpenUsd.Runtime.Core.{platform.Rid}");
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    [Test]
    public async Task WindowsImagingPackageShadesBoundUsdPreviewSurfaceFromMergedPluginPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Windows package material rendering is covered by the win-x64 package job.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        string repositoryRoot = FindRepositoryRoot();
        if (!TryGetExecutionInputs(
            repositoryRoot,
            out NativeExecutionInputs inputs,
            out string reason))
        {
            HandleMissingExecutionPrerequisites(
                nameof(WindowsImagingPackageShadesBoundUsdPreviewSurfaceFromMergedPluginPath),
                reason);
            return;
        }
        if (inputs.Platform.Rid != "win-x64")
        {
            Console.WriteLine("The UsdPreviewSurface package rendering repro is win-x64 only.");
            return;
        }

        string workRoot = Path.Combine(repositoryRoot, "artifacts", "pm");
        if (Directory.Exists(workRoot))
        {
            Directory.Delete(workRoot, recursive: true);
        }
        Directory.CreateDirectory(workRoot);
        try
        {
            string packageRoot = Path.Combine(workRoot, "packages");
            Directory.CreateDirectory(packageRoot);
            string[] managedPackageIds =
            [
                "OpenUsd.Interop",
                "OpenUsd",
                "OpenUsd.Rendering",
                "OpenUsd.Rendering.Storm",
                "OpenUsd.Rendering.Silk",
                inputs.Platform.BackendPackageId,
            ];

            var managedPackages = new List<PackedPackage>();
            foreach (string packageId in managedPackageIds)
            {
                managedPackages.Add(
                    await PackManagedPackageAsync(repositoryRoot, packageId, packageRoot));
            }

            PackedPackage coreRuntimePackage = await PackAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Core.win-x64",
                inputs.InstallRoot,
                inputs.ShimRoot,
                inputs.VulkanRuntimeLibrary,
                packageRoot);
            PackedPackage imagingRuntimePackage = await PackAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Imaging.win-x64",
                inputs.InstallRoot,
                inputs.ShimRoot,
                inputs.VulkanRuntimeLibrary,
                packageRoot);
            string packageVersion = managedPackages[0].Version;
            foreach (PackedPackage package in managedPackages)
            {
                await Assert.That(package.Version).IsEqualTo(packageVersion);
            }
            await Assert.That(coreRuntimePackage.Version).IsEqualTo(packageVersion);
            await Assert.That(imagingRuntimePackage.Version).IsEqualTo(packageVersion);
            PackedPackage imagingMetaPackage = await PackManagedPackageAsync(
                repositoryRoot,
                "OpenUsd.Runtime.Imaging",
                packageRoot);
            await Assert.That(imagingMetaPackage.Version).IsEqualTo(packageVersion);
            await CreateStubRuntimePackagesAsync(
                workRoot,
                packageRoot,
                packageVersion,
                [.. GetImagingMetaPackageGraph(inputs.Platform)
                    .Where(id => id.StartsWith("OpenUsd.Runtime.", StringComparison.Ordinal))
                    .Where(id => id != "OpenUsd.Runtime.Imaging")
                    .Where(id => id != "OpenUsd.Runtime.Imaging.win-x64")
                    .Where(id => id != "OpenUsd.Runtime.Core.win-x64")]);

            ExecutionConsumer consumer = await PublishPreviewSurfaceMaterialConsumerAsync(
                workRoot,
                packageRoot,
                packageVersion,
                inputs.Platform);
            AssertPackageOnlyGraph(
                consumer.AssetsPath,
                [
                    "OpenUsd.Interop",
                    "OpenUsd",
                    "OpenUsd.Rendering",
                    "OpenUsd.Rendering.Storm",
                    "OpenUsd.Rendering.Silk",
                    inputs.Platform.BackendPackageId,
                    "OpenUsd.Runtime.Imaging",
                    "OpenUsd.Runtime.Imaging.win-x64",
                    "OpenUsd.Runtime.Imaging.linux-x64",
                    "OpenUsd.Runtime.Imaging.osx-arm64",
                    "OpenUsd.Runtime.Core.win-x64",
                ]);
            string mergedPluginPath = Path.Combine(consumer.PublishRoot, "usd");
            await Assert.That(File.Exists(Path.Combine(
                mergedPluginPath,
                "hdStorm",
                "resources",
                "plugInfo.json"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(
                mergedPluginPath,
                "hdSilk",
                "resources",
                "plugInfo.json"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(
                mergedPluginPath,
                "sdrGlslfx",
                "resources",
                "plugInfo.json"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(
                mergedPluginPath,
                "usdShaders",
                "resources",
                "shaders",
                "previewSurface.glslfx"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(
                consumer.PublishRoot,
                "plugin",
                "usd",
                "usdShaders",
                "resources",
                "shaders",
                "previewSurface.glslfx"))).IsTrue();

            CommandResult result = await RunExecutableAsync(
                GetExecutablePath(consumer.PublishRoot, "Consumer"),
                consumer.PublishRoot,
                [],
                GetImagingRuntimeEnvironment(inputs.Platform, consumer.PublishRoot));
            Console.WriteLine(result.Output.Trim());
            if (result.ExitCode != 0)
            {
                // Every assertion above this line -- the merged plugin layout,
                // sdrGlslfx, and previewSurface.glslfx in both roots -- is the
                // actual issue #4 regression guard and has already run. What
                // follows needs Storm, and Storm needs an OpenGL context that
                // hosted Windows runners do not provide. Only that specific
                // condition is tolerated; any other consumer failure still
                // fails the gate.
                if (result.Output.Contains("WGL_ARB_create_context is unavailable", StringComparison.Ordinal))
                {
                    HandleUnavailableHostCapability(
                        nameof(WindowsImagingPackageShadesBoundUsdPreviewSurfaceFromMergedPluginPath),
                        "Storm shading",
                        "The host exposes no WGL_ARB_create_context, so no GL context can be made. " +
                        "The packaging half of this gate ran and passed.");
                    return;
                }

                throw new InvalidOperationException(result.Output);
            }

            await Assert.That(result.Output).Contains("PACKAGE_PREVIEW_SURFACE_MATERIAL_OK");
            await Assert.That(result.Output).Contains("PLUGIN_MERGED_LAYOUT=true");
            await Assert.That(result.Output).Contains("PREVIEW_SURFACE_GLSLFX=true");
            await Assert.That(result.Output).Contains("STAGE_HAS_DISPLAY_COLOR=false");
            await Assert.That(result.Output).Contains("STORM_MATERIAL_DIVERGED=true");
            await Assert.That(result.Output).Contains("HDSILK_MATERIAL_DIVERGED=true");
            await AssertNoSourcePathLeakageAsync(result.Output, repositoryRoot);
        }
        finally
        {
            Directory.Delete(workRoot, recursive: true);
        }
    }

    private static async Task<ExecutionConsumer> PublishPreviewSurfaceMaterialConsumerAsync(
        string workRoot,
        string packageRoot,
        string packageVersion,
        ExecutionPlatform platform)
    {
        string consumerRoot = Path.Combine(workRoot, "psc");
        string publishRoot = Path.Combine(consumerRoot, "publish");
        string projectPath = Path.Combine(consumerRoot, "Consumer.csproj");
        Directory.CreateDirectory(consumerRoot);

        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Directory.Build.props"),
            "<Project />");
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Directory.Packages.props"),
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "NuGet.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="isolated-openusd-feed" value="{packageRoot}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="isolated-openusd-feed">
                  <package pattern="OpenUsd*" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="Microsoft.*" />
                  <package pattern="runtime.*" />
                  <package pattern="Silk.NET.*" />
                  <package pattern="Stride.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        await File.WriteAllTextAsync(
            projectPath,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RuntimeIdentifier>{platform.Rid}</RuntimeIdentifier>
                <PublishAot>false</PublishAot>
                <SelfContained>false</SelfContained>
                <ImplicitUsings>disable</ImplicitUsings>
                <InvariantGlobalization>true</InvariantGlobalization>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="OpenUsd.Rendering.Storm"
                                  Version="{packageVersion}" />
                <PackageReference Include="{platform.BackendPackageId}"
                                  Version="{packageVersion}" />
                <PackageReference Include="OpenUsd.Runtime.Imaging"
                                  Version="{packageVersion}" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Program.cs"),
            CreatePreviewSurfaceMaterialConsumerProgram());

        string globalPackagesRoot = Path.Combine(workRoot, "g");
        CommandResult result = await RunDotnetAsync(
            consumerRoot,
            [
                "publish",
                "Consumer.csproj",
                "-c",
                "Release",
                "-r",
                platform.Rid,
                "--nologo",
                "--configfile",
                "NuGet.config",
                "--self-contained",
                "false",
                "-o",
                publishRoot,
            ],
            globalPackagesRoot);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Output);
        }

        return new ExecutionConsumer(
            projectPath,
            publishRoot,
            Path.Combine(consumerRoot, "obj", "project.assets.json"));
    }

    private static string CreatePreviewSurfaceMaterialConsumerProgram() =>
        """
        using System;
        using System.ComponentModel;
        using System.IO;
        using System.Runtime.InteropServices;
        using System.Runtime.Versioning;
        using OpenUsd;
        using OpenUsd.Geom;
        using OpenUsd.Interop;
        using OpenUsd.Rendering;
        using OpenUsd.Rendering.Silk;
        using OpenUsd.Rendering.Storm;
        using OpenUsd.Rendering.Silk.D3D12;
        using OpenUsd.Shade;

        namespace PackagePreviewSurfaceMaterialConsumer;

        [SupportedOSPlatform("windows")]
        internal static partial class Program
        {
            private const int Width = 320;
            private const int Height = 180;
            private const byte ClearR = 5;
            private const byte ClearG = 7;
            private const byte ClearB = 11;
            private static readonly WndProc WindowProcedure = DefWindowProc;

            public static int Main()
            {
                try
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        return 8;
                    }

                    string pluginPath = Path.Combine(AppContext.BaseDirectory, "usd");
                    string legacyPluginPath = Path.Combine(AppContext.BaseDirectory, "plugin", "usd");
                    string previewSurfacePath = Path.Combine(
                        pluginPath,
                        "usdShaders",
                        "resources",
                        "shaders",
                        "previewSurface.glslfx");
                    bool mergedLayout =
                        File.Exists(Path.Combine(pluginPath, "plugInfo.json")) &&
                        File.Exists(Path.Combine(pluginPath, "usdShade", "resources", "plugInfo.json")) &&
                        File.Exists(Path.Combine(pluginPath, "hdStorm", "resources", "plugInfo.json")) &&
                        File.Exists(Path.Combine(pluginPath, "hdSilk", "resources", "plugInfo.json")) &&
                        File.Exists(Path.Combine(pluginPath, "sdrGlslfx", "resources", "plugInfo.json")) &&
                        File.Exists(previewSurfacePath) &&
                        File.Exists(Path.Combine(
                            legacyPluginPath,
                            "usdShaders",
                            "resources",
                            "shaders",
                            "previewSurface.glslfx"));
                    if (!mergedLayout)
                    {
                        Console.WriteLine("PLUGIN_MERGED_LAYOUT=false");
                        return 2;
                    }

                    nuint pluginCount = OpenUsdNativeRuntime.RegisterPlugins(pluginPath);
                    string warmStagePath = Path.Combine(AppContext.BaseDirectory, "preview-surface-warm.usda");
                    string coolStagePath = Path.Combine(AppContext.BaseDirectory, "preview-surface-cool.usda");
                    WriteStage(
                        warmStagePath,
                        new UsdVec3f(0.95f, 0.04f, 0.02f),
                        new UsdVec3f(0.02f, 0.80f, 0.10f));
                    WriteStage(
                        coolStagePath,
                        new UsdVec3f(0.05f, 0.20f, 0.95f),
                        new UsdVec3f(0.95f, 0.75f, 0.05f));
                    string stageText = File.ReadAllText(warmStagePath) + File.ReadAllText(coolStagePath);
                    bool hasDisplayColor = stageText.Contains("displayColor", StringComparison.Ordinal);
                    if (hasDisplayColor)
                    {
                        return 3;
                    }

                    RenderedImage stormWarm = RenderStorm(pluginPath, warmStagePath);
                    RenderedImage stormCool = RenderStorm(pluginPath, coolStagePath);
                    RenderedImage hdSilkWarm = RenderHdSilk(pluginPath, warmStagePath);
                    RenderedImage hdSilkCool = RenderHdSilk(pluginPath, coolStagePath);
                    MaterialMetrics storm = MeasureMaterialDivergence(stormWarm, stormCool);
                    MaterialMetrics hdSilk = MeasureMaterialDivergence(hdSilkWarm, hdSilkCool);
                    bool stormDiverged = storm.MaxChannelDelta >= 32 && storm.MeanChannelDelta >= 2;
                    bool hdSilkDiverged = hdSilk.MaxChannelDelta >= 32 && hdSilk.MeanChannelDelta >= 2;

                    Console.WriteLine("PACKAGE_PREVIEW_SURFACE_MATERIAL_OK");
                    Console.WriteLine($"REGISTERED_PLUGIN_COUNT={pluginCount}");
                    Console.WriteLine($"PLUGIN_MERGED_LAYOUT={mergedLayout.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"PREVIEW_SURFACE_GLSLFX={File.Exists(previewSurfacePath).ToString().ToLowerInvariant()}");
                    Console.WriteLine($"STAGE_HAS_DISPLAY_COLOR={hasDisplayColor.ToString().ToLowerInvariant()}");
                    Console.WriteLine($"STORM_MATERIAL_MAX_CHANNEL_DELTA={storm.MaxChannelDelta}");
                    Console.WriteLine($"STORM_MATERIAL_MEAN_CHANNEL_DELTA={storm.MeanChannelDelta:F3}");
                    Console.WriteLine($"STORM_MATERIAL_DIVERGED={stormDiverged.ToString().ToLowerInvariant()}");
                    Console.WriteLine($"HDSILK_MATERIAL_MAX_CHANNEL_DELTA={hdSilk.MaxChannelDelta}");
                    Console.WriteLine($"HDSILK_MATERIAL_MEAN_CHANNEL_DELTA={hdSilk.MeanChannelDelta:F3}");
                    Console.WriteLine($"HDSILK_MATERIAL_DIVERGED={hdSilkDiverged.ToString().ToLowerInvariant()}");
                    return stormDiverged && hdSilkDiverged ? 0 : 4;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(exception);
                    return 1;
                }
            }

            private static void WriteStage(string stagePath, UsdVec3f leftColor, UsdVec3f rightColor)
            {
                File.Delete(stagePath);
                using UsdStage stage = UsdStage.Create(stagePath);
                _ = stage.DefineXform("/World");
                stage.SetDefaultPrim("/World");
                _ = stage.DefinePrim("/World/Looks", "Scope");

                UsdGeomCube left = stage.DefineCube("/World/Left");
                left.Size = 1.2;
                left.Xformable.SetLocalTransform(UsdMatrix4d.CreateTranslation(-0.75, 0, 0));
                UsdGeomCube right = stage.DefineCube("/World/Right");
                right.Size = 1.2;
                right.Xformable.SetLocalTransform(UsdMatrix4d.CreateTranslation(0.75, 0, 0));

                UsdPreviewSurface leftMaterial = UsdPreviewSurface.Create(
                    stage,
                    "/World/Looks/Left",
                    "/World/Looks/Left/PreviewSurface");
                leftMaterial.SetDiffuseColor(leftColor);
                leftMaterial.SetRoughness(1);
                leftMaterial.Material.Bind(left.Prim);

                UsdPreviewSurface rightMaterial = UsdPreviewSurface.Create(
                    stage,
                    "/World/Looks/Right",
                    "/World/Looks/Right/PreviewSurface");
                rightMaterial.SetDiffuseColor(rightColor);
                rightMaterial.SetRoughness(1);
                rightMaterial.Material.Bind(right.Prim);
                stage.Save();
            }

            private static RenderedImage RenderHdSilk(string pluginPath, string stagePath)
            {
                using ISilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: true);
                using ISilkGraphicsTexture color = device.CreateTexture2D(
                    new SilkTextureDescriptor(
                        (uint)Width,
                        (uint)Height,
                        SilkTextureFormat.Rgba8Unorm,
                        SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
                using ISilkGraphicsTexture depth = device.CreateTexture2D(
                    SilkTextureDescriptor.DepthTarget((uint)Width, (uint)Height));
                using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(pluginPath, stagePath);
                using OpenUsdSilkPage page = session.Sync(Width, Height, camera: CameraState.Default);
                using var renderer = new SilkMeshRenderer(device);
                _ = renderer.ApplyAndRender(
                    page,
                    color,
                    depth,
                    new SilkMeshRenderOptions(new SilkColor(ClearR / 255f, ClearG / 255f, ClearB / 255f, 1), 1));
                byte[] pixels = new byte[Width * Height * 4];
                color.ReadbackForTesting(pixels);
                return new RenderedImage(pixels, Width, Height);
            }

            private static RenderedImage RenderStorm(string pluginPath, string stagePath)
            {
                nint parent = CreateHiddenParentWindow();
                try
                {
                    UsdStageScheduler scheduler = UsdStageScheduler.Open(stagePath);
                    try
                    {
                        using UsdStageRenderSource source = scheduler.AcquireRenderSourceAsync()
                            .GetAwaiter()
                            .GetResult();
                        using OpenUsdStormChildSession session = OpenUsdStormChildRuntime.Create(
                            parent,
                            pluginPath,
                            source,
                            Width,
                            Height,
                            96);
                        session.SetVisible(false);
                        _ = session.Render(0, CameraState.Default);
                        OpenUsdStormFramebufferCapture capture = session.CaptureFramebuffer(
                            PackRgba(ClearR, ClearG, ClearB, 255),
                            tolerance: 2,
                            copyPixels: true);
                        return new RenderedImage(capture.RgbaPixels.ToArray(), capture.Width, capture.Height);
                    }
                    finally
                    {
                        scheduler.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    }
                }
                finally
                {
                    if (parent != 0)
                    {
                        _ = DestroyWindow(parent);
                    }
                }
            }

            private static MaterialMetrics MeasureMaterialDivergence(RenderedImage first, RenderedImage second)
            {
                if (first.Width != second.Width || first.Height != second.Height)
                {
                    throw new InvalidOperationException("Material comparison images must have the same dimensions.");
                }

                int max = 0;
                long sum = 0;
                long count = 0;
                for (int offset = 0; offset < first.Pixels.Length; offset += 4)
                {
                    bool firstBackground = IsClear(first.Pixels, offset);
                    bool secondBackground = IsClear(second.Pixels, offset);
                    if (firstBackground && secondBackground)
                    {
                        continue;
                    }

                    for (int channel = 0; channel < 3; channel++)
                    {
                        int delta = Math.Abs(first.Pixels[offset + channel] - second.Pixels[offset + channel]);
                        max = Math.Max(max, delta);
                        sum += delta;
                        count++;
                    }
                }

                return new MaterialMetrics(max, count == 0 ? 0 : (double)sum / count);
            }

            private static bool IsClear(byte[] pixels, int offset) =>
                Math.Abs(pixels[offset] - ClearR) <= 2 &&
                Math.Abs(pixels[offset + 1] - ClearG) <= 2 &&
                Math.Abs(pixels[offset + 2] - ClearB) <= 2;

            private static uint PackRgba(byte r, byte g, byte b, byte a) =>
                (uint)(r | (g << 8) | (b << 16) | (a << 24));

            private static nint CreateHiddenParentWindow()
            {
                nint module = GetModuleHandle(null);
                string className = $"OpenUsdPackageMaterial{Environment.ProcessId}";
                var windowClass = new WindowClass
                {
                    Size = (uint)Marshal.SizeOf<WindowClass>(),
                    WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedure),
                    Instance = module,
                    ClassName = className,
                };
                ushort atom = RegisterClassEx(ref windowClass);
                if (atom == 0)
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error != 1410)
                    {
                        throw new Win32Exception(error, "RegisterClassEx failed for the Storm package material test.");
                    }
                }

                nint window = CreateWindowEx(
                    0,
                    className,
                    "OpenUSD package material test",
                    0,
                    0,
                    0,
                    Width,
                    Height,
                    0,
                    0,
                    module,
                    0);
                if (window == 0)
                {
                    throw new Win32Exception(
                        Marshal.GetLastPInvokeError(),
                        "CreateWindowEx failed for the Storm package material test.");
                }

                return window;
            }

            private readonly record struct RenderedImage(byte[] Pixels, int Width, int Height);

            private readonly record struct MaterialMetrics(int MaxChannelDelta, double MeanChannelDelta);

            private delegate nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam);

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct WindowClass
            {
                public uint Size;
                public uint Style;
                public nint WindowProcedure;
                public int ClassExtra;
                public int WindowExtra;
                public nint Instance;
                public nint Icon;
                public nint Cursor;
                public nint Background;
                public string? MenuName;
                public string ClassName;
                public nint SmallIcon;
            }

            [DllImport("kernel32", EntryPoint = "GetModuleHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern nint GetModuleHandle(string? moduleName);

            [DllImport("user32", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern ushort RegisterClassEx(ref WindowClass windowClass);

            [DllImport("user32", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern nint CreateWindowEx(
                uint extendedStyle,
                string className,
                string windowName,
                uint style,
                int x,
                int y,
                int width,
                int height,
                nint parent,
                nint menu,
                nint instance,
                nint parameter);

            [DllImport("user32", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
            private static extern nint DefWindowProc(nint hwnd, uint message, nuint wParam, nint lParam);

            [DllImport("user32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool DestroyWindow(nint window);
        }
        """;


    private static async Task<PackedPackage> PackAsync(
        string repositoryRoot,
        string packageId,
        string installRoot,
        string shimRoot,
        string vulkanRuntimeLibrary,
        string packageRoot,
        string? cesiumShimRoot = null,
        string? physicsShimRoot = null,
        string? physXInstallRoot = null,
        bool skipLinuxElfValidation = false,
        bool skipMacOsMachOValidation = false)
    {
        string projectPath = Path.Combine(repositoryRoot, "src", packageId, $"{packageId}.csproj");
        CommandResult result = await RunDotnetAsync(
            repositoryRoot,
            [
                "pack",
                projectPath,
                "-c",
                "Release",
                "--nologo",
                $"-p:OpenUsdInstallRoot={installRoot}",
                $"-p:OpenUsdShimInstallRoot={shimRoot}",
                $"-p:OpenUsdCesiumShimInstallRoot={cesiumShimRoot ?? shimRoot}",
                $"-p:OpenUsdPhysicsShimInstallRoot={physicsShimRoot ?? shimRoot}",
                $"-p:OpenUsdPhysXInstallRoot={physXInstallRoot ?? installRoot}",
                $"-p:OpenUsdVulkanRuntimeLibrary={vulkanRuntimeLibrary}",
                $"-p:OpenUsdSkipLinuxElfValidation={skipLinuxElfValidation.ToString().ToLowerInvariant()}",
                $"-p:OpenUsdSkipMacOsMachOValidation={skipMacOsMachOValidation.ToString().ToLowerInvariant()}",
                $"-p:PackageOutputPath={packageRoot}",
            ]);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Output);
        }

        string packagePath = FindPackage(packageRoot, packageId);
        return new PackedPackage(packagePath, ReadPackageVersion(packagePath));
    }

    private static async Task<PackedPackage> PackManagedPackageAsync(
        string repositoryRoot,
        string packageId,
        string packageRoot)
    {
        string projectPath = Path.Combine(repositoryRoot, "src", packageId, $"{packageId}.csproj");
        return await PackProjectPackageAsync(repositoryRoot, projectPath, packageId, packageRoot);
    }

    private static async Task<PackedPackage> PackProjectPackageAsync(
        string repositoryRoot,
        string projectPath,
        string packageId,
        string packageRoot)
    {
        if (!Path.IsPathRooted(projectPath))
        {
            projectPath = Path.Combine(repositoryRoot, projectPath);
        }

        var arguments = new List<string>
        {
            "pack",
            projectPath,
            "-c",
            "Release",
            "--nologo",
            "-p:BuildInParallel=false",
            $"-p:PackageOutputPath={packageRoot}",
            "-p:IsPackable=true",
        };
        if (packageId == "OpenUsd.Rendering.Silk.Metal")
        {
            if (!OperatingSystem.IsMacOS())
            {
                throw new InvalidOperationException(
                    "Metal package production is macOS-only.");
            }
            arguments.Add("-p:OpenUsdRequireMetalShaderLibrary=true");
        }
        CommandResult result = await RunDotnetAsync(
            repositoryRoot,
            arguments.ToArray());

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Output);
        }

        string packagePath = FindPackage(packageRoot, packageId);
        return new PackedPackage(packagePath, ReadPackageVersion(packagePath));
    }

    private static async Task CreateStubRuntimePackagesAsync(
        string workRoot,
        string packageRoot,
        string packageVersion,
        IReadOnlyCollection<string> packageIds)
    {
        foreach (string packageId in packageIds.Distinct(StringComparer.Ordinal))
        {
            string stubRoot = Path.Combine(
                workRoot,
                "stub-packages",
                packageId.Replace('.', '-'));
            Directory.CreateDirectory(stubRoot);
            string projectPath = Path.Combine(stubRoot, "Stub.csproj");
            await File.WriteAllTextAsync(
                projectPath,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>{packageId}</PackageId>
                    <PackageVersion>{packageVersion}</PackageVersion>
                    <Description>Restore-only package-test stub for {packageId}.</Description>
                    <IncludeBuildOutput>false</IncludeBuildOutput>
                    <IncludeSymbols>false</IncludeSymbols>
                    <EnablePackageValidation>false</EnablePackageValidation>
                    <NoWarn>$(NoWarn);NU5128</NoWarn>
                  </PropertyGroup>
                </Project>
                """);

            CommandResult result = await RunDotnetAsync(
                stubRoot,
                [
                    "pack",
                    projectPath,
                    "-c",
                    "Release",
                    "--nologo",
                    $"-p:PackageOutputPath={packageRoot}",
                    "-p:IsPackable=true",
                ],
                Path.Combine(workRoot, "stub-global-packages"));
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.Output);
            }
        }
    }

    private static string FindPackage(string packageRoot, string packageId) =>
        Directory
            .GetFiles(packageRoot, "*.nupkg")
            .Where(path => !path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
            .Single(path => string.Equals(ReadPackageId(path), packageId, StringComparison.Ordinal));

    private static async Task AssertMetalPackageMatchesStagedLibraryAsync(
        string packagePath,
        string repositoryRoot)
    {
        byte[] staged = await File.ReadAllBytesAsync(
            Path.Combine(
                repositoryRoot,
                "eng",
                "shaders",
                "checked",
                "mesh.metallib"));
        byte[] stagedManifest = await File.ReadAllBytesAsync(
            Path.Combine(
                repositoryRoot,
                "eng",
                "shaders",
                "checked",
                "mesh.metallib.manifest.json"));
        await ValidateMetalSidecarAsync(repositoryRoot);
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry[] metallibs =
            package.Entries
                .Where(entry => entry.FullName.EndsWith(
                    ".metallib",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        await Assert.That(metallibs.Length).IsEqualTo(1);
        await Assert.That(metallibs[0].FullName).IsEqualTo(
            "runtimes/osx/native/mesh.metallib");
        using Stream stream = metallibs[0].Open();
        using var content = new MemoryStream();
        await stream.CopyToAsync(content);
        await Assert.That(content.ToArray()).IsEquivalentTo(staged);
        ZipArchiveEntry manifestEntry = package.Entries.Single(
            entry => entry.FullName == (
                "runtimes/osx/native/mesh.metallib.manifest.json"));
        using Stream manifestStream = manifestEntry.Open();
        using var manifestContent = new MemoryStream();
        await manifestStream.CopyToAsync(manifestContent);
        byte[] packagedManifest = manifestContent.ToArray();
        await Assert.That(packagedManifest).IsEquivalentTo(stagedManifest);
        await ValidateMetalSidecarAsync(repositoryRoot);
    }

    private static async Task AssertMetalPublishedAssetsAsync(
        string publishRoot,
        string repositoryRoot)
    {
        string publishedLibrary = Path.Combine(publishRoot, "mesh.metallib");
        string publishedManifest = Path.Combine(
            publishRoot,
            "mesh.metallib.manifest.json");
        await Assert.That(File.Exists(publishedLibrary)).IsTrue();
        await Assert.That(File.Exists(publishedManifest)).IsTrue();
        await AssertFileHashesEqualAsync(
            Path.Combine(repositoryRoot, "eng", "shaders", "checked", "mesh.metallib"),
            publishedLibrary);
        await AssertFileHashesEqualAsync(
            Path.Combine(
                repositoryRoot,
                "eng",
                "shaders",
                "checked",
                "mesh.metallib.manifest.json"),
            publishedManifest);
    }

    private static async Task ValidateMetalSidecarAsync(string repositoryRoot)
    {
        string[] arguments =
        [
            "eng/shaders/scripts/metal_sidecar.py",
            "--sidecar",
            "eng/shaders/checked/mesh.metallib.manifest.json",
            "--library",
            "eng/shaders/checked/mesh.metallib",
            "--manifest",
            "eng/shaders/shader-manifest.json",
            "--lock",
            "eng/shaders/toolchain.lock.json",
            "--repository-root",
            repositoryRoot,
            "--verify-checked-files",
        ];
        CommandResult result = await RunProcessAsync(
            "python",
            repositoryRoot,
            arguments,
            nugetPackagesRoot: null,
            sanitizeRuntimeEnvironment: false,
            runtimeEnvironment: null);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "The staged Metal sidecar failed complete schema-v4 " +
                $"validation.{Environment.NewLine}{result.Output}");
        }
    }

    private static string ReadPackageId(string packagePath)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry nuspecEntry = package.Entries.Single(
            entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using Stream nuspecStream = nuspecEntry.Open();
        XDocument nuspec = XDocument.Load(nuspecStream);
        XNamespace ns = nuspec.Root!.Name.Namespace;
        return nuspec.Descendants(ns + "id").Single().Value;
    }

    private static async Task AssertPackageEntriesAsync(string packagePath, string[] expectedEntries)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        string[] entries = package.Entries.Select(entry => entry.FullName).ToArray();

        foreach (string expectedEntry in expectedEntries)
        {
            await Assert.That(entries).Contains(expectedEntry);
        }

        await Assert.That(entries.Any(entry => entry.StartsWith("lib/", StringComparison.Ordinal))).IsFalse();
        await Assert.That(entries.Any(entry => entry.Contains("native/install", StringComparison.Ordinal))).IsFalse();
    }

    private static async Task AssertPackageEntryContainsAsync(
        string packagePath,
        string entryPath,
        string expectedText,
        string excludedText)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry entry = package.GetEntry(entryPath)
            ?? throw new InvalidOperationException($"Package entry is missing: {entryPath}");
        using var reader = new StreamReader(entry.Open());
        string content = await reader.ReadToEndAsync();
        await Assert.That(content).Contains(expectedText);
        await Assert.That(content).DoesNotContain(excludedText);
    }

    private static async Task AssertPackageDoesNotContainAsync(
        string packagePath,
        string excludedEntry)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        string[] matchingEntries = package
            .Entries
            .Select(entry => entry.FullName)
            .Where(entry => entry.Contains(excludedEntry, StringComparison.Ordinal))
            .ToArray();
        await Assert.That(matchingEntries).IsEmpty();
    }

    private static async Task AssertPackageDoesNotContainFileNameOutsideNativeAsync(
        string packagePath,
        string fileName)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        string[] entries = package
            .Entries
            .Select(entry => entry.FullName)
            .Where(entry => string.Equals(
                Path.GetFileName(entry),
                fileName,
                StringComparison.Ordinal))
            .ToArray();
        await Assert.That(entries).HasSingleItem();
        await Assert.That(entries[0]).StartsWith("runtimes/linux-x64/native/");
    }

    private static async Task AssertSingleNativePackageEntryAsync(
        string packagePath,
        string rid,
        string libraryFile)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        string[] entries = package
            .Entries
            .Select(entry => entry.FullName)
            .Where(entry => string.Equals(
                Path.GetFileName(entry),
                libraryFile,
                StringComparison.Ordinal))
            .ToArray();
        await Assert.That(entries).HasSingleItem();
        await Assert.That(entries[0]).IsEqualTo(
            $"runtimes/{rid}/native/{libraryFile}");
    }

    private static async Task AssertPackageEntryMatchesFileAsync(
        string packagePath,
        string entryPath,
        string filePath)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry entry = package.GetEntry(entryPath)
            ?? throw new InvalidOperationException($"Package entry is missing: {entryPath}");
        await using Stream packageStream = entry.Open();
        string packageHash = Convert.ToHexString(
            await SHA256.HashDataAsync(packageStream));
        await using FileStream fileStream = File.OpenRead(filePath);
        string fileHash = Convert.ToHexString(
            await SHA256.HashDataAsync(fileStream));
        await Assert.That(entry.Length).IsEqualTo(new FileInfo(filePath).Length);
        await Assert.That(packageHash).IsEqualTo(fileHash);
    }

    private static async Task AssertManagedPackageRepositoryMetadataAsync(
        string packagePath,
        string repositoryRoot)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        string[] entryPaths = package.Entries.Select(entry => entry.FullName).ToArray();
        await Assert.That(entryPaths.Any(path => path.Contains('\\', StringComparison.Ordinal))).IsFalse();
        await Assert.That(entryPaths.Any(path => path.Contains("../", StringComparison.Ordinal))).IsFalse();
        await Assert.That(entryPaths.Any(path => path.EndsWith(".cs", StringComparison.Ordinal))).IsFalse();

        ZipArchiveEntry nuspecEntry = package.Entries.Single(
            entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using Stream nuspecStream = nuspecEntry.Open();
        XDocument nuspec = XDocument.Load(nuspecStream);
        XNamespace ns = nuspec.Root!.Name.Namespace;
        XElement repository = nuspec.Descendants(ns + "repository").Single();
        await Assert.That((string?)repository.Attribute("type")).IsEqualTo("git");
        await Assert.That((string?)repository.Attribute("url"))
            .IsEqualTo("https://github.com/marcschier/openusd-dotnet");

        string metadata = nuspec.ToString();
        await Assert.That(metadata).DoesNotContain(repositoryRoot);
        await Assert.That(metadata).DoesNotContain(repositoryRoot.Replace('\\', '/'));
    }

    private static async Task AssertPackageSymbolicLinkAsync(
        string packagePath,
        string entryPath,
        string expectedTarget)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry entry = package.GetEntry(entryPath)
            ?? throw new InvalidOperationException($"Package entry is missing: {entryPath}");
        uint unixMode = ((uint)entry.ExternalAttributes >> 16) & 0xFFFF;
        await Assert.That(unixMode & 0xF000).IsEqualTo(0xA000u);
        using var reader = new StreamReader(entry.Open());
        await Assert.That(await reader.ReadToEndAsync()).IsEqualTo(expectedTarget);
    }

    private static async Task<MacStormChildIdentity>
        ValidateMacPublishedStormChildIdentityAsync(
            string packagePath,
            string nativeInstallPath,
            string publishedPath)
    {
        const string entryPath =
            "runtimes/osx-arm64/native/libopenusd_storm_child.dylib";
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry entry = package.GetEntry(entryPath)
            ?? throw new InvalidOperationException($"Package entry is missing: {entryPath}");
        await using Stream packageStream = entry.Open();
        string packageEntryHash = Convert.ToHexString(
            await SHA256.HashDataAsync(packageStream));
        await using FileStream installStream = File.OpenRead(nativeInstallPath);
        string nativeInstallHash = Convert.ToHexString(
            await SHA256.HashDataAsync(installStream));
        await using FileStream publishedStream = File.OpenRead(publishedPath);
        string publishedHash = Convert.ToHexString(
            await SHA256.HashDataAsync(publishedStream));
        if (!string.Equals(packageEntryHash, nativeInstallHash, StringComparison.Ordinal) ||
            !string.Equals(packageEntryHash, publishedHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The published macOS Storm child changed before codesign: " +
                $"package={packageEntryHash}, install={nativeInstallHash}, " +
                $"published={publishedHash}.");
        }
        return new MacStormChildIdentity(
            packageEntryHash,
            nativeInstallHash,
            publishedHash);
    }

    private static async Task AssertLinuxStormChildInstallMatchesPackageAsync(
        string packagePath,
        string shimRoot)
    {
        string installDirectory = Path.Combine(shimRoot, "lib");
        string[] installedPaths = Directory
            .GetFiles(installDirectory, "libopenusd_storm_child.so*")
            .Where(path =>
                Path.GetFileName(path) == "libopenusd_storm_child.so" ||
                Path.GetFileName(path).StartsWith(
                    "libopenusd_storm_child.so.",
                    StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        string[] packageEntries = package
            .Entries
            .Select(entry => entry.FullName)
            .Where(entry =>
                entry.StartsWith("runtimes/linux-x64/native/", StringComparison.Ordinal) &&
                (Path.GetFileName(entry) == "libopenusd_storm_child.so" ||
                    Path.GetFileName(entry).StartsWith(
                        "libopenusd_storm_child.so.",
                        StringComparison.Ordinal)))
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(packageEntries.Length).IsEqualTo(installedPaths.Length);
        foreach (string installedPath in installedPaths)
        {
            string entryPath =
                $"runtimes/linux-x64/native/{Path.GetFileName(installedPath)}";
            await Assert.That(packageEntries).Contains(entryPath);
            var installedInfo = new FileInfo(installedPath);
            string? linkTarget = installedInfo.LinkTarget;
            if (linkTarget is null &&
                installedInfo.Length < 256 &&
                (installedInfo.Name == "libopenusd_storm_child.so" ||
                    installedInfo.Name == "libopenusd_storm_child.so.8"))
            {
                string candidate = await File.ReadAllTextAsync(installedPath);
                if (candidate.StartsWith(
                    "libopenusd_storm_child.so.8",
                    StringComparison.Ordinal))
                {
                    linkTarget = candidate;
                }
            }
            if (linkTarget is not null)
            {
                await AssertPackageSymbolicLinkAsync(packagePath, entryPath, linkTarget);
            }
            else
            {
                await AssertPackageEntryMatchesFileAsync(
                    packagePath,
                    entryPath,
                    installedPath);
            }
        }
    }

    private static async Task AssertPublishedLinuxStormChildTopologyAsync(string publishRoot)
    {
        string linkPath = Path.Combine(publishRoot, "libopenusd_storm_child.so");
        string sonamePath = Path.Combine(publishRoot, "libopenusd_storm_child.so.8");
        await Assert.That(new FileInfo(linkPath).LinkTarget)
            .IsEqualTo("libopenusd_storm_child.so.8");
        await Assert.That(File.Exists(sonamePath)).IsTrue();
        await Assert.That(new FileInfo(sonamePath).LinkTarget)
            .IsEqualTo("libopenusd_storm_child.so.8.0.0");
        await Assert.That(File.Exists(Path.Combine(
            publishRoot,
            "libopenusd_storm_child.so.8.0.0"))).IsTrue();
        string[] stormEntries = Directory.GetFiles(
            publishRoot,
            "libopenusd_storm_child.so*",
            SearchOption.TopDirectoryOnly);
        await Assert.That(stormEntries.Length).IsEqualTo(3);
    }

    private static async Task AssertFileHashesEqualAsync(
        string expectedPath,
        string actualPath)
    {
        await using FileStream expectedStream = File.OpenRead(expectedPath);
        string expectedHash = Convert.ToHexString(
            await SHA256.HashDataAsync(expectedStream));
        await using FileStream actualStream = File.OpenRead(actualPath);
        string actualHash = Convert.ToHexString(
            await SHA256.HashDataAsync(actualStream));
        await Assert.That(actualHash).IsEqualTo(expectedHash);
    }

    private static string GetFileSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task AssertPackageDependenciesAsync(
        string packagePath,
        string expectedVersion,
        IReadOnlyCollection<string> expectedPackageIds,
        bool exact = false,
        bool pinned = false)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry nuspecEntry = package.Entries.Single(
            entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using Stream nuspecStream = nuspecEntry.Open();
        XDocument nuspec = XDocument.Load(nuspecStream);
        XNamespace ns = nuspec.Root!.Name.Namespace;
        // A multi-targeted package writes one dependency group per framework, so the same id
        // legitimately appears more than once. Distinct versions for one id would be a real
        // problem, so they are collapsed rather than ignored: two different pins would leave two
        // entries and fail the comparison below.
        Dictionary<string, string> dependencies = nuspec
            .Descendants(ns + "dependency")
            .GroupBy(
                element => element.Attribute("id")?.Value ?? string.Empty,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => string.Join(
                    "|",
                    group
                        .Select(element => element.Attribute("version")?.Value ?? string.Empty)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)),
                StringComparer.Ordinal);

        foreach (string packageId in expectedPackageIds)
        {
            await Assert.That(dependencies).ContainsKey(packageId);
            // A pinned dependency is written as a single-version range. Accepting either spelling
            // keeps this usable for the metapackages, which take the floating form.
            await Assert.That(dependencies[packageId].Trim('[', ']')).IsEqualTo(expectedVersion);
        }

        if (exact)
        {
            await Assert.That(dependencies.Keys.Order(StringComparer.Ordinal).ToArray())
                .IsEquivalentTo(expectedPackageIds.Order(StringComparer.Ordinal).ToArray());
        }

        if (pinned)
        {
            foreach (string packageId in expectedPackageIds)
            {
                await Assert.That(dependencies[packageId]).IsEqualTo($"[{expectedVersion}]");
            }
        }
    }

    /// <summary>
    /// Reads the ABI evidence a physics package embeds and requires it to describe that package.
    /// </summary>
    /// <remarks>
    /// The evidence exists so a consumer can tell which ABI generation a package carries without
    /// loading it. Evidence that is present but describes something else is worse than no evidence,
    /// so the package id, the RID, the kind, and the hash of every native asset are all compared
    /// against the archive that actually shipped.
    /// </remarks>
    private static async Task AssertPhysicsAbiEvidenceAsync(
        string packagePath,
        string rid)
    {
        string packageId = ReadPackageId(packagePath);
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry evidenceEntry = package.Entries.Single(entry =>
            string.Equals(
                entry.FullName,
                $"build/{packageId}.native-abi.json",
                StringComparison.Ordinal));
        using Stream evidenceStream = evidenceEntry.Open();
        using JsonDocument evidence = await JsonDocument.ParseAsync(evidenceStream);
        JsonElement root = evidence.RootElement;

        await Assert.That(root.GetProperty("packageId").GetString()).IsEqualTo(packageId);
        await Assert.That(root.GetProperty("rid").GetString()).IsEqualTo(rid);
        await Assert.That(root.GetProperty("kind").GetString()).IsEqualTo("Physics");
        await Assert.That(root.GetProperty("physicsWorldAbiVersion").GetInt32())
            .IsEqualTo(RequiredPhysicsAbiVersion);
        await Assert.That(root.GetProperty("physicsExtractAbiVersion").GetInt32())
            .IsEqualTo(RequiredPhysicsExtractAbiVersion);
        // Recorded rather than inferred from an absence: no OpenUsd package carries a GPU module.
        await Assert.That(root.GetProperty("cudaModulesIncluded").GetBoolean()).IsFalse();

        JsonElement[] assets = [.. root.GetProperty("assets").EnumerateArray()];
        string[] nativeEntries =
        [
            .. package.Entries
                .Where(entry => entry.FullName.StartsWith(
                    $"runtimes/{rid}/native/",
                    StringComparison.Ordinal))
                .Select(entry => entry.FullName)
                .Order(StringComparer.Ordinal),
        ];
        await Assert.That(assets.Length).IsEqualTo(nativeEntries.Length);

        foreach (JsonElement asset in assets)
        {
            string name = asset.GetProperty("name").GetString()!;
            ZipArchiveEntry entry = package.Entries.Single(candidate =>
                string.Equals(
                    candidate.FullName,
                    $"runtimes/{rid}/native/{name}",
                    StringComparison.Ordinal));
            await Assert.That(entry.Length).IsEqualTo(asset.GetProperty("size").GetInt64());

            using Stream content = entry.Open();
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(content));
            await Assert.That(hash).IsEqualTo(asset.GetProperty("sha256").GetString());
        }
    }

    /// <summary>
    /// Requires a packed archive to contain no NVIDIA PhysX GPU or device module, under any name.
    /// </summary>
    /// <remarks>
    /// Matched on the entry name rather than on a fixed path, because the licensing problem is the
    /// bytes being in the archive at all; where they sit inside it makes no difference to a
    /// redistribution claim this project cannot make.
    /// </remarks>
    private static async Task AssertNoProprietaryPhysXModulesAsync(string packagePath)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        string[] proprietary =
        [
            .. package.Entries
                .Select(entry => entry.FullName)
                .Where(name => Regex.IsMatch(
                    Path.GetFileName(name),
                    @"^(lib)?PhysX(Gpu|Device)",
                    RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(5))),
        ];

        await Assert.That(proprietary)
            .IsEmpty()
            .Because(
                "the PhysX GPU and device modules are NVIDIA proprietary packman blobs, not " +
                "artifacts of the BSD-3-Clause sources this project links, and no OpenUsd package " +
                "may redistribute one: " + string.Join(", ", proprietary));
    }

    private static async Task AssertImagingDependsOnCoreAsync(
        string packagePath,
        string rid,
        string coreVersion)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry nuspecEntry = package.Entries.Single(
            entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using Stream nuspecStream = nuspecEntry.Open();
        XDocument nuspec = XDocument.Load(nuspecStream);
        XNamespace ns = nuspec.Root!.Name.Namespace;
        XElement dependency = nuspec
            .Descendants(ns + "dependency")
            .Single(element => element.Attribute("id")?.Value == $"OpenUsd.Runtime.Core.{rid}");

        await Assert.That(dependency.Attribute("version")?.Value).IsEqualTo($"[{coreVersion}]");
        await Assert.That(dependency.Attribute("include")?.Value).IsEqualTo("All");
        await Assert.That(dependency.Attribute("exclude")).IsNull();
    }

    private static async Task AssertLinuxValidationEvidenceAsync(string packagePath)
    {
        int stormChildAbiVersion = ReadStormChildAbiVersion(FindRepositoryRoot());
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry evidenceEntry = package.Entries.Single(
            entry => entry.FullName ==
                "build/OpenUsd.Runtime.Imaging.linux-x64.native-validation.json");
        using Stream evidenceStream = evidenceEntry.Open();
        using JsonDocument evidence = await JsonDocument.ParseAsync(evidenceStream);
        JsonElement root = evidence.RootElement;
        await Assert.That(root.GetProperty("schemaVersion").GetInt32()).IsEqualTo(3);
        await Assert.That(root.GetProperty("rid").GetString()).IsEqualTo("linux-x64");
        await Assert.That(root.GetProperty("stormChildAbiVersion").GetInt32())
            .IsEqualTo(stormChildAbiVersion);
        JsonElement runpathPolicy = root.GetProperty("runpathPolicy");
        await Assert.That(runpathPolicy.GetProperty("dynamicTag").GetString())
            .IsEqualTo("DT_RUNPATH");
        await Assert.That(runpathPolicy.GetProperty("rejectLegacyRpath").GetBoolean()).IsTrue();
        string[] allowedEntries = runpathPolicy
            .GetProperty("allowedEntries")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        await Assert.That(allowedEntries).IsEquivalentTo(["$ORIGIN"]);
        string[] exports = root
            .GetProperty("stormChildExports")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        await Assert.That(exports).Contains("openusd_storm_child_get_abi_version");
        await Assert.That(exports).Contains("openusd_storm_child_initialize_linux");
        await Assert.That(exports).Contains("openusd_storm_child_render_v2");
        await Assert.That(exports).Contains("openusd_storm_child_request_frame_v3");
        await Assert.That(exports).Contains("openusd_storm_child_pick");
        await Assert.That(exports).Contains("openusd_storm_child_set_selection");
        await Assert.That(exports).Contains("openusd_storm_child_get_navigation_input");
        await Assert.That(exports).Contains("openusd_storm_child_capture_framebuffer");
        JsonElement topology = root.GetProperty("stormChildTopology");
        await Assert.That(topology.GetProperty("soname").GetString())
            .IsEqualTo("libopenusd_storm_child.so.8");
        await Assert.That(topology.GetProperty("linkName").GetString())
            .IsEqualTo("libopenusd_storm_child.so");
        string realFile = topology.GetProperty("realFile").GetString()!;
        await Assert.That(realFile.StartsWith(
            "libopenusd_storm_child.so.8",
            StringComparison.Ordinal)).IsTrue();
        JsonElement[] topologyEntries = topology
            .GetProperty("entries")
            .EnumerateArray()
            .ToArray();
        await Assert.That(topologyEntries.Single(entry =>
            entry.GetProperty("name").GetString() == "libopenusd_storm_child.so")
            .GetProperty("target").GetString())
            .IsEqualTo("libopenusd_storm_child.so.8");

        JsonElement[] libraries = root
            .GetProperty("libraries")
            .EnumerateArray()
            .ToArray();
        await Assert.That(libraries.Length).IsEqualTo(3);
        foreach (JsonElement library in libraries)
        {
            await Assert.That(library.GetProperty("dynamicTag").GetString())
                .IsEqualTo("DT_RUNPATH");
            string[] runpathEntries = library
                .GetProperty("runpathEntries")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray();
            await Assert.That(runpathEntries).IsEquivalentTo(["$ORIGIN"]);
        }
        await Assert.That(libraries.Single(library =>
            library.GetProperty("name").GetString() == "libopenusd_storm_child.so")
            .GetProperty("soname").GetString())
            .IsEqualTo("libopenusd_storm_child.so.8");
    }

    private static async Task AssertHdSilkPackageAsync(
        string packagePath,
        string rid,
        string libraryFile,
        string expectedLibraryPath)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        string expectedNativePath = $"runtimes/{rid}/native/{libraryFile}";
        string[] libraryEntries = package
            .Entries
            .Select(entry => entry.FullName)
            .Where(path => IsHdSilkLibraryFile(
                rid,
                libraryFile,
                Path.GetFileName(path)))
            .ToArray();

        await Assert.That(libraryEntries).HasSingleItem();
        await Assert.That(libraryEntries[0]).IsEqualTo(expectedNativePath);
        await Assert.That(
            libraryEntries.Any(path => path.Contains("/resources/", StringComparison.Ordinal))).IsFalse();

        string pluginPath =
            $"runtimes/{rid}/resources/plugin/usd/hdSilk/resources/plugInfo.json";
        ZipArchiveEntry pluginEntry = package.Entries.Single(entry => entry.FullName == pluginPath);
        using Stream pluginStream = pluginEntry.Open();
        using JsonDocument plugin = JsonDocument.Parse(pluginStream);
        string? packagedLibraryPath = plugin
            .RootElement
            .GetProperty("Plugins")[0]
            .GetProperty("LibraryPath")
            .GetString();
        await Assert.That(packagedLibraryPath).IsEqualTo(expectedLibraryPath);
    }

    private static bool IsHdSilkLibraryFile(
        string rid,
        string expectedFileName,
        string actualFileName) =>
        rid == "linux-x64"
            ? actualFileName.StartsWith(expectedFileName, StringComparison.Ordinal)
            : string.Equals(actualFileName, expectedFileName, StringComparison.Ordinal);

    private static string ReadPackageVersion(string packagePath)
    {
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry nuspecEntry = package.Entries.Single(
            entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        using Stream nuspecStream = nuspecEntry.Open();
        XDocument nuspec = XDocument.Load(nuspecStream);
        XNamespace ns = nuspec.Root!.Name.Namespace;
        return nuspec.Descendants(ns + "version").Single().Value;
    }

    private static async Task<string> PublishConsumerAsync(
        string workRoot,
        string packageRoot,
        string corePackageVersion)
    {
        string consumerRoot = Path.Combine(workRoot, "consumer");
        string publishRoot = Path.Combine(consumerRoot, "publish");
        Directory.CreateDirectory(consumerRoot);

        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Directory.Build.props"),
            "<Project />");
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Directory.Packages.props"),
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "NuGet.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="runtime-packages" value="{packageRoot}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Consumer.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RuntimeIdentifier>win-x64</RuntimeIdentifier>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="OpenUsd.Runtime.Core.win-x64" Version="{corePackageVersion}" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Program.cs"),
            "namespace Consumer; internal static class Program { private static void Main() { } }");

        CommandResult result = await RunDotnetAsync(
            consumerRoot,
            [
                "publish",
                "Consumer.csproj",
                "-c",
                "Release",
                "-r",
                "win-x64",
                "--self-contained",
                "false",
                "--nologo",
                "--configfile",
                "NuGet.config",
                "-o",
                publishRoot,
            ],
            Path.Combine(workRoot, "global-packages"));
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Output);
        }

        return publishRoot;
    }

    private static async Task<ExecutionConsumer> PublishCesiumConsumerAsync(
        string workRoot,
        string packageRoot,
        string packageVersion,
        ExecutionPlatform platform)
    {
        string consumerRoot = Path.Combine(workRoot, $"cesium-consumer-{platform.Rid}");
        string publishRoot = Path.Combine(consumerRoot, "publish");
        string projectPath = Path.Combine(consumerRoot, "Consumer.csproj");
        Directory.CreateDirectory(consumerRoot);

        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Directory.Build.props"),
            "<Project />");
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Directory.Packages.props"),
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "NuGet.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="isolated-openusd-feed" value="{packageRoot}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="isolated-openusd-feed">
                  <package pattern="OpenUsd*" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="Microsoft.*" />
                  <package pattern="runtime.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        await File.WriteAllTextAsync(
            projectPath,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RuntimeIdentifier>{platform.Rid}</RuntimeIdentifier>
                <SelfContained>false</SelfContained>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="OpenUsd.Cesium" Version="{packageVersion}" />
                <PackageReference Include="OpenUsd.Runtime.Core" Version="{packageVersion}" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Program.cs"),
            """
            using System;
            using System.IO;
            using System.Threading;
            using OpenUsd;
            using OpenUsd.Cesium;
            using OpenUsd.Geom;

            namespace PackageCesiumExecutionConsumer;

            internal static class Program
            {
                public static int Main()
                {
                    string tilesetPath = Path.Combine(AppContext.BaseDirectory, "tileset.json");
                    File.WriteAllText(
                        tilesetPath,
                        "{ \"asset\": { \"version\": \"1.1\" }, " +
                        "\"geometricError\": 0, \"root\": { " +
                        "\"boundingVolume\": { \"sphere\": [0, 0, 0, 1] }, " +
                        "\"geometricError\": 0 } }");

                    var accessor = new CountingFileAccessor(AppContext.BaseDirectory);
                    using var tileset = new CesiumTileset("tileset.json", accessor);
                    CesiumUpdateResult update = default;
                    for (int attempt = 0; attempt < 20 && accessor.RequestCount == 0; attempt++)
                    {
                        update = tileset.UpdateView(new CesiumViewState(
                            new UsdVec3d(0, 0, 2),
                            new UsdVec3d(0, 0, -1),
                            new UsdVec3d(0, 1, 0),
                            64,
                            64,
                            Math.PI / 3,
                            Math.PI / 3));
                        Thread.Sleep(25);
                    }

                    string outputPath = Path.Combine(AppContext.BaseDirectory, "cesium-package.usda");
                    using UsdStage stage = UsdStage.Create(outputPath);
                    CesiumTileImportResult imported = tileset.ImportVisibleTiles(stage, "/CesiumTiles");
                    stage.Save();

                    bool shimPresent = File.Exists(Path.Combine(
                        AppContext.BaseDirectory,
                        "__CESIUM_LIBRARY__"));
                    string currentDirectory = Path.GetFullPath(".")
                        .TrimEnd(Path.DirectorySeparatorChar);
                    string baseDirectory = AppContext.BaseDirectory
                        .TrimEnd(Path.DirectorySeparatorChar);
                    bool cwdIsPublish = string.Equals(
                        currentDirectory,
                        baseDirectory,
                        StringComparison.OrdinalIgnoreCase);

                    Console.WriteLine("PACKAGE_CESIUM_TILESET_OK");
                    Console.WriteLine($"SHIM_PRESENT={shimPresent.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"TILESET_REQUESTED={accessor.TilesetRequested.ToString().ToLowerInvariant()}");
                    Console.WriteLine($"REQUEST_COUNT={accessor.RequestCount}");
                    Console.WriteLine($"TILES_TO_RENDER={update.TilesToRenderCount}");
                    Console.WriteLine($"IMPORTED_MESHES={imported.MeshCount}");
                    Console.WriteLine($"STAGE_SAVED={File.Exists(outputPath).ToString().ToLowerInvariant()}");
                    Console.WriteLine($"CWD_IS_PUBLISH={cwdIsPublish.ToString().ToLowerInvariant()}");
                    return shimPresent && accessor.TilesetRequested && cwdIsPublish ? 0 : 1;
                }
            }

            internal sealed class CountingFileAccessor(string rootDirectory) : ICesiumAssetAccessor
            {
                private readonly CesiumFileAssetAccessor _inner = new(rootDirectory);

                public int RequestCount { get; private set; }

                public bool TilesetRequested { get; private set; }

                public CesiumAssetResponse Request(CesiumAssetRequest request)
                {
                    RequestCount++;
                    TilesetRequested |= request.Url.EndsWith(
                        "tileset.json",
                        StringComparison.OrdinalIgnoreCase);
                    return _inner.Request(request);
                }
            }
            """.Replace("__CESIUM_LIBRARY__", GetCesiumLibraryName(platform), StringComparison.Ordinal));

        string globalPackagesRoot = Path.Combine(workRoot, "cesium-global-packages");
        CommandResult result = await RunDotnetAsync(
            consumerRoot,
            [
                "publish",
                "Consumer.csproj",
                "-c",
                "Release",
                "-r",
                platform.Rid,
                "--nologo",
                "--configfile",
                "NuGet.config",
                "-o",
                publishRoot,
            ],
            globalPackagesRoot);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Output);
        }

        return new ExecutionConsumer(
            projectPath,
            publishRoot,
            Path.Combine(consumerRoot, "obj", "project.assets.json"));
    }

    /// <summary>
    /// Publishes a consumer that drives the retained physics transport from packages alone.
    /// </summary>
    /// <remarks>
    /// The consumer authors its own stage rather than reading a repository asset, because a test
    /// asset is not part of any package and would prove nothing about what a consumer receives. It
    /// also extracts the codeless <c>openUsdPhysics</c> schema plugin from the managed assembly,
    /// which is the only way to observe that the schema resources survived packing at all: they are
    /// embedded resources, so a package that dropped them would still restore and still compile.
    /// </remarks>
    private static async Task<ExecutionConsumer> PublishPhysicsConsumerAsync(
        string workRoot,
        string packageRoot,
        string packageVersion,
        ExecutionPlatform platform)
    {
        string consumerRoot = Path.Combine(workRoot, $"physics-consumer-{platform.Rid}");
        string publishRoot = Path.Combine(consumerRoot, "publish");
        string projectPath = Path.Combine(consumerRoot, "Consumer.csproj");
        Directory.CreateDirectory(consumerRoot);

        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Directory.Build.props"),
            "<Project />");
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Directory.Packages.props"),
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "NuGet.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="isolated-openusd-feed" value="{packageRoot}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="isolated-openusd-feed">
                  <package pattern="OpenUsd*" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="Microsoft.*" />
                  <package pattern="runtime.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        await File.WriteAllTextAsync(
            projectPath,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RuntimeIdentifier>{platform.Rid}</RuntimeIdentifier>
                <PublishAot>true</PublishAot>
                <SelfContained>true</SelfContained>
                <InvariantGlobalization>true</InvariantGlobalization>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <TrimmerSingleWarn>false</TrimmerSingleWarn>
                <SuppressTrimAnalysisWarnings>false</SuppressTrimAnalysisWarnings>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="OpenUsd.Physics" Version="{packageVersion}" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Program.cs"),
            """
            using System;
            using System.IO;
            using System.Threading.Tasks;
            using OpenUsd;
            using OpenUsd.Physics;
            using OpenUsd.Physics.Extraction;
            using OpenUsd.Physics.Schema;

            namespace PackagePhysicsExecutionConsumer;

            internal static class Program
            {
                public static async Task<int> Main()
                {
                    string schemaRoot = Path.Combine(AppContext.BaseDirectory, "physics-schema");
                    string schemaResources = OpenUsdPhysicsSchemaResources.ExtractPluginTo(schemaRoot);
                    bool schemaExtracted =
                        File.Exists(Path.Combine(schemaResources, "plugInfo.json")) &&
                        File.Exists(Path.Combine(schemaResources, "generatedSchema.usda"));

                    string stagePath = Path.Combine(AppContext.BaseDirectory, "physics-package.usda");

                    await using var scheduler = UsdStageScheduler.Open(stagePath);
                    await using UsdPhysicsTransport transport =
                        await UsdPhysicsTransport.CreateAsync(scheduler);

                    // Extract, attach, then build. A transport built with nothing attached carries
                    // authored timeline metadata only and simulates no authored body, which would
                    // let this gate pass while proving the package can do nothing.
                    UsdPhysicsExtractionPage page =
                        await UsdPhysicsStageExtractor.ExtractAsync(scheduler);
                    await transport.AttachExtractionAsync(page);
                    await transport.BuildAsync();

                    UsdPhysicsCapabilities capabilities = transport.Capabilities;
                    bool rigidBodies = capabilities.Supports(UsdPhysicsCapability.RigidBodies);
                    bool cuda = capabilities.Supports(UsdPhysicsCapability.Cuda);

                    await transport.StepAsync(8);

                    int bodies = 0;
                    ulong stepIndex = 0;
                    if (transport.TryAcquireLatestFrame(out UsdPhysicsFrameLease lease))
                    {
                        using (lease)
                        {
                            bodies = lease.Frame.BodyCount;
                            stepIndex = lease.Frame.StepIndex;
                        }
                    }

                    bool solverPresent = File.Exists(Path.Combine(
                        AppContext.BaseDirectory,
                        "__PHYSICS_LIBRARY__"));
                    bool cudaModulePresent = File.Exists(Path.Combine(
                        AppContext.BaseDirectory,
                        "__CUDA_MODULE__"));

                    Console.WriteLine("PACKAGE_PHYSICS_TRANSPORT_OK");
                    Console.WriteLine($"PACKAGE_PHYSICS_SOLVER_PRESENT={Lower(solverPresent)}");
                    Console.WriteLine($"PACKAGE_PHYSICS_CUDA_MODULE_PRESENT={Lower(cudaModulePresent)}");
                    Console.WriteLine($"PACKAGE_PHYSICS_SCHEMA_EXTRACTED={Lower(schemaExtracted)}");
                    Console.WriteLine($"PACKAGE_PHYSICS_RIGID_BODIES={Lower(rigidBodies)}");
                    Console.WriteLine($"PACKAGE_PHYSICS_CUDA_CAPABILITY={Lower(cuda)}");
                    Console.WriteLine($"PACKAGE_PHYSICS_BODY_COUNT={bodies}");
                    Console.WriteLine($"PACKAGE_PHYSICS_STEP_INDEX={stepIndex}");
                    Console.WriteLine(
                        $"PACKAGE_PHYSICS_DIAGNOSTICS={transport.Diagnostics.Entries.Count}");

                    // The GPU capability is only ever claimed when the module that implements it was
                    // actually published. Absent modules must report an absent capability rather
                    // than a capability that fails on first use.
                    bool cudaHonest = !cuda || cudaModulePresent;
                    return solverPresent && schemaExtracted && rigidBodies && bodies >= 2 &&
                        stepIndex > 0 && cudaHonest ? 0 : 1;
                }

                private static string Lower(bool value) => value ? "true" : "false";
            }
            """
                .Replace("__PHYSICS_LIBRARY__", GetPhysicsLibraryName(platform), StringComparison.Ordinal)
                .Replace(
                    "__CUDA_MODULE__",
                    platform.Rid == "win-x64" ? "PhysXGpu_64.dll" : "libPhysXGpu_64.so",
                    StringComparison.Ordinal));

        string globalPackagesRoot = Path.Combine(workRoot, "physics-global-packages");
        CommandResult result = await RunDotnetAsync(
            consumerRoot,
            [
                "publish",
                "Consumer.csproj",
                "-c",
                "Release",
                "-r",
                platform.Rid,
                "--nologo",
                "--configfile",
                "NuGet.config",
                "-o",
                publishRoot,
            ],
            globalPackagesRoot);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Output);
        }

        return new ExecutionConsumer(
            projectPath,
            publishRoot,
            Path.Combine(consumerRoot, "obj", "project.assets.json"));
    }

    /// <summary>
    /// Writes the physics stage the published consumer simulates.
    /// </summary>
    /// <remarks>
    /// The stage is authored beside the published application rather than inside the consumer's
    /// source, because a stage with nothing simulable on it lets the smoke pass while proving
    /// nothing. It carries a scene, a static collider, and two rigid bodies, so a run that produces
    /// no bodies is a failure rather than an empty success.
    /// </remarks>
    private static async Task WritePhysicsConsumerStageAsync(string publishRoot) =>
        await File.WriteAllTextAsync(
            Path.Combine(publishRoot, "physics-package.usda"),
            """
            #usda 1.0
            (
                metersPerUnit = 1
                kilogramsPerUnit = 1
                upAxis = "Y"
                timeCodesPerSecond = 24
                startTimeCode = 0
                endTimeCode = 24
            )

            def PhysicsScene "Scene"
            {
                vector3f physics:gravityDirection = (0, -1, 0)
                float physics:gravityMagnitude = 9.81
            }

            def Xform "Ground"
            {
                def Cube "Slab" (
                    prepend apiSchemas = ["PhysicsCollisionAPI"]
                )
                {
                    double size = 10
                    double3 xformOp:scale = (1, 0.05, 1)
                    uniform token[] xformOpOrder = ["xformOp:scale"]
                }
            }

            def Xform "FallingBox" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double3 xformOp:translate = (0, 4, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
                float physics:mass = 4
                rel physics:simulationOwner = </Scene>

                def Cube "Shape" (
                    prepend apiSchemas = ["PhysicsCollisionAPI"]
                )
                {
                    double size = 1
                }
            }

            def Xform "FallingSphere" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI"]
            )
            {
                double3 xformOp:translate = (2, 6, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
                rel physics:simulationOwner = </Scene>

                def Sphere "Shape" (
                    prepend apiSchemas = ["PhysicsCollisionAPI"]
                )
                {
                    double radius = 0.5
                }
            }
            """);

    private static async Task<ExecutionConsumer> PublishExecutionConsumerAsync(
        string workRoot,
        string packageRoot,
        string packageVersion,
        ExecutionPlatform platform)
    {
        string consumerRoot = Path.Combine(workRoot, $"execution-consumer-{platform.Rid}");
        string publishRoot = Path.Combine(consumerRoot, "publish");
        string projectPath = Path.Combine(consumerRoot, "Consumer.csproj");
        Directory.CreateDirectory(consumerRoot);

        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Directory.Build.props"),
            "<Project />");
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Directory.Packages.props"),
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "NuGet.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="isolated-openusd-feed" value="{packageRoot}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="isolated-openusd-feed">
                  <package pattern="OpenUsd*" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="Microsoft.*" />
                  <package pattern="runtime.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        await File.WriteAllTextAsync(
            projectPath,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RuntimeIdentifier>{platform.Rid}</RuntimeIdentifier>
                <PublishAot>true</PublishAot>
                <InvariantGlobalization>true</InvariantGlobalization>
                <Nullable>enable</Nullable>
                <StripSymbols>true</StripSymbols>
                <OptimizationPreference>Size</OptimizationPreference>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="OpenUsd" Version="{packageVersion}" />
                <PackageReference Include="OpenUsd.Runtime.Core"
                                  Version="{packageVersion}" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Program.cs"),
            """
            using System;
            using System.Collections.Generic;
            using System.IO;
            using OpenUsd;
            using OpenUsd.Geom;
            using OpenUsd.Interop;

            namespace PackageExecutionConsumer;

            internal static class Program
            {
                public static int Main(string[] args)
                {
                    if (args.Length != 2)
                    {
                        return 2;
                    }

                    string pluginPath = Path.Combine(AppContext.BaseDirectory, "usd");
                    if (!File.Exists(Path.Combine(pluginPath, "plugInfo.json")))
                    {
                        return 3;
                    }

                    nuint plugins = OpenUsdNativeRuntime.RegisterPlugins(pluginPath);
                    bool inputOpened;
                    bool cameraStateQuery;
                    using (UsdStage input = UsdStage.Open(args[0]))
                    {
                        inputOpened = input.HasPrim("/Input");
                        UsdGeomCamera camera = UsdGeomCamera.Wrap(
                            input.GetPrim("/Camera"));
                        UsdGeomCameraState cameraState = camera.GetState();
                        cameraStateQuery =
                            cameraState.Projection ==
                                UsdGeomCameraProjection.Perspective &&
                            cameraState.FocalLength == 50 &&
                            cameraState.HorizontalAperture == 20 &&
                            cameraState.VerticalAperture == 10 &&
                            cameraState.ClippingNear == 0.25 &&
                            cameraState.ClippingFar == 500 &&
                            cameraState.WindowWidth > 0 &&
                            cameraState.WindowHeight > 0;
                    }
                    if (!inputOpened || !cameraStateQuery)
                    {
                        return 4;
                    }

                    using (UsdStage output = UsdStage.Create(args[1]))
                    {
                        UsdPrim prim = output.DefinePrim("/World/PackageRoundTrip", "Xform");
                        prim.SetDouble("custom:value", 42.5);
                        output.Save();
                    }

                    double value;
                    using (UsdStage reopened = UsdStage.Open(args[1]))
                    {
                        if (!reopened.HasPrim("/World/PackageRoundTrip"))
                        {
                            return 5;
                        }
                        value = reopened
                            .GetPrim("/World/PackageRoundTrip")
                            .GetDouble("custom:value");
                    }
                    if (value != 42.5)
                    {
                        return 6;
                    }

                    // This proves the packaging half of the third-party contract: a tree that the
                    // packaged layout knows nothing about is registered from its own
                    // plugInfo.json, where it lies, and nothing is merged into usd/**. It is a
                    // resource plugin, not an ArResolver, because a package consumer cannot
                    // compile a native plugin; the executable ArResolver half of the contract is
                    // proved by the CTest native probe against native/tests/resolver_plugin.
                    string packagedPlugInfo = Path.Combine(pluginPath, "plugInfo.json");
                    string packagedBefore = File.ReadAllText(packagedPlugInfo);
                    string vendorRoot = Path.Combine(
                        AppContext.BaseDirectory,
                        "vendor-plugins",
                        "openusdVendorResolver");
                    string vendorPlugInfo = Path.Combine(vendorRoot, "resources", "plugInfo.json");
                    if (!File.Exists(vendorPlugInfo))
                    {
                        return 8;
                    }

                    int vendorPlugins = UsdPluginRegistry.Register(vendorPlugInfo);
                    bool vendorDiscovered = false;
                    bool vendorOwnsTree = false;
                    foreach (UsdPluginInfo plugin in UsdPluginRegistry.GetRegisteredPlugins())
                    {
                        if (plugin.Name != "openusdVendorResolver")
                        {
                            continue;
                        }
                        vendorDiscovered = true;

                        // Upstream reports resource paths with forward slashes and a trailing
                        // separator, so normalize before comparing directories.
                        string reported = Path.GetFullPath(
                            plugin.ResourcePath.TrimEnd('/', '\\'));
                        vendorOwnsTree =
                            plugin.Kind == UsdPluginKind.Resource &&
                            reported.StartsWith(
                                Path.GetFullPath(vendorRoot),
                                StringComparison.OrdinalIgnoreCase) &&
                            !reported.StartsWith(
                                Path.GetFullPath(pluginPath),
                                StringComparison.OrdinalIgnoreCase);
                    }
                    bool packagedTreeIntact = string.Equals(
                        packagedBefore,
                        File.ReadAllText(packagedPlugInfo),
                        StringComparison.Ordinal);
                    if (vendorPlugins != 1 || !vendorDiscovered || !vendorOwnsTree ||
                        !packagedTreeIntact)
                    {
                        return 9;
                    }

                    // The context asset lives in its own directory rather than the working
                    // directory, so only the resolver context can find it.
                    using UsdResolverContext resolverContext = UsdResolverContext.Create(
                        [
                            new UsdResolverContextString(
                                string.Empty,
                                Path.Combine(AppContext.BaseDirectory, "assets")),
                        ]);
                    IReadOnlyList<UsdResolvedAsset> resolved = UsdResolver.Resolve(
                        [
                            "context-asset.usda",
                            "package-missing-asset.usda",
                            Path.GetFileName(args[1]),
                        ],
                        resolverContext);
                    bool resolverBulk =
                        resolved.Count == 3 &&
                        resolved[0].IsResolved &&
                        resolved[0].Extension == "usda" &&
                        resolved[0].IsContextDependent &&
                        !resolved[1].IsResolved &&
                        resolved[1].ResolvedPath.Length == 0 &&
                        resolved[2].IsResolved;
                    bool resolverScoped;
                    using (resolverContext.Bind())
                    {
                        resolverScoped = UsdResolver.Resolve(["context-asset.usda"])[0].IsResolved;
                    }
                    resolverScoped = resolverScoped &&
                        !UsdResolver.Resolve(["context-asset.usda"])[0].IsResolved;
                    if (!resolverBulk || !resolverScoped)
                    {
                        return 10;
                    }

                    string currentDirectory = Path.GetFullPath(".")
                        .TrimEnd(Path.DirectorySeparatorChar);
                    string baseDirectory = AppContext.BaseDirectory
                        .TrimEnd(Path.DirectorySeparatorChar);
                    bool cwdIsPublish = string.Equals(
                        currentDirectory,
                        baseDirectory,
                        StringComparison.OrdinalIgnoreCase);

                    Console.WriteLine("PACKAGE_EXECUTION_OK");
                    Console.WriteLine($"ABI={OpenUsdNativeRuntime.AbiVersion}");
                    Console.WriteLine($"CAPABILITIES=0x{OpenUsdNativeRuntime.Capabilities:X}");
                    Console.WriteLine($"PLUGINS={plugins}");
                    Console.WriteLine($"INPUT_OPENED={inputOpened.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"CAMERA_STATE_QUERY={cameraStateQuery.ToString().ToLowerInvariant()}");
                    Console.WriteLine($"ROUNDTRIP_SAVED={File.Exists(args[1]).ToString().ToLowerInvariant()}");
                    Console.WriteLine($"ROUNDTRIP_VALUE={value}");
                    Console.WriteLine($"RESOLVER_PRIMARY={UsdResolver.PrimaryTypeName}");
                    Console.WriteLine($"RESOLVER_BULK={resolverBulk.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"RESOLVER_SCOPED={resolverScoped.ToString().ToLowerInvariant()}");
                    Console.WriteLine($"VENDOR_PLUGINS={vendorPlugins}");
                    Console.WriteLine(
                        $"VENDOR_PLUGIN_TREE={vendorOwnsTree.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        "PACKAGED_PLUGIN_TREE_INTACT=" +
                        packagedTreeIntact.ToString().ToLowerInvariant());
                    Console.WriteLine($"CWD_IS_PUBLISH={cwdIsPublish.ToString().ToLowerInvariant()}");
                    return cwdIsPublish ? 0 : 7;
                }
            }
            """);

        string globalPackagesRoot = Path.Combine(workRoot, "execution-global-packages");
        CommandResult result = await RunDotnetAsync(
            consumerRoot,
            [
                "publish",
                "Consumer.csproj",
                "-c",
                "Release",
                "-r",
                platform.Rid,
                "--nologo",
                "--configfile",
                "NuGet.config",
                "-o",
                publishRoot,
            ],
            globalPackagesRoot);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Output);
        }

        return new ExecutionConsumer(
            projectPath,
            publishRoot,
            Path.Combine(consumerRoot, "obj", "project.assets.json"));
    }

    private static async Task<ExecutionConsumer> PublishStormChildConsumerAsync(
        string workRoot,
        string packageRoot,
        string packageVersion,
        ExecutionPlatform platform,
        bool publishAot)
    {
        int stormChildAbiVersion = ReadStormChildAbiVersion(FindRepositoryRoot());
        string consumerRoot = Path.Combine(workRoot, $"{platform.Rid}-storm-child-consumer");
        string publishRoot = Path.Combine(consumerRoot, "publish");
        string projectPath = Path.Combine(consumerRoot, "Consumer.csproj");
        Directory.CreateDirectory(consumerRoot);

        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Directory.Build.props"),
            "<Project />");
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Directory.Packages.props"),
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "NuGet.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="isolated-openusd-feed" value="{packageRoot}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="isolated-openusd-feed">
                  <package pattern="OpenUsd*" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="Microsoft.*" />
                  <package pattern="runtime.*" />
                  <package pattern="SharpMetal" />
                  <package pattern="Silk.NET.*" />
                  <package pattern="Stride.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        string metalPackageReference = platform.Rid == "osx-arm64"
            ? $"""
                <PackageReference Include="OpenUsd.Rendering.Silk.Metal"
                                  Version="{packageVersion}" />
              """
            : string.Empty;
        await File.WriteAllTextAsync(
            projectPath,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RuntimeIdentifier>{platform.Rid}</RuntimeIdentifier>
                <PublishAot>{publishAot.ToString().ToLowerInvariant()}</PublishAot>
                <SelfContained>true</SelfContained>
                <ImplicitUsings>disable</ImplicitUsings>
                <InvariantGlobalization>true</InvariantGlobalization>
                <Nullable>enable</Nullable>
                <StripSymbols>true</StripSymbols>
                <OptimizationPreference>Size</OptimizationPreference>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="OpenUsd.Runtime.Imaging"
                                  Version="{packageVersion}" />
            {metalPackageReference}
              </ItemGroup>
            </Project>
            """);
        string loaderPathName =
            platform.Rid == "osx-arm64" ? "DYLD_LIBRARY_PATH" : "LD_LIBRARY_PATH";
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Program.cs"),
            """
            // Copyright (c) marcschier. Licensed under the MIT License.

            using System;
            using System.Collections.Generic;
            using System.IO;
            using System.Runtime.InteropServices;

            namespace PackageStormChildExecutionConsumer;

            internal static class Program
            {
                public static int Main()
                {
                    if (!__OPERATING_SYSTEM_GUARD__)
                    {
                        return 8;
                    }

                    uint abi = GetStormChildAbiVersion();
                    bool captureCalled = CaptureWithoutChild(
                        out int captureStatus,
                        out string captureError);
                    bool navigationCalled = GetNavigationWithoutChild(
                        out int navigationStatus,
                        out string navigationError,
                        out bool navigationReset);
                    bool linuxInitializerExport = HasLinuxInitializerExport();
                    bool loaderPathAbsent =
                        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("__LOADER_PATH__"));
                    (bool mapsConfined,
                     int projectMapCount,
                     bool stormMapped,
                     bool openUsdMapped,
                     bool metalPathsConfined,
                     string appBaseCanonical,
                     string[] loadedImagePaths) =
                        OperatingSystem.IsMacOS()
                            ? ValidateMacProjectLibraryImages()
                            : ValidateLinuxProjectLibraryMaps();
                    string currentDirectory = Path.GetFullPath(".")
                        .TrimEnd(Path.DirectorySeparatorChar);
                    string baseDirectory = AppContext.BaseDirectory
                        .TrimEnd(Path.DirectorySeparatorChar);
                    bool cwdIsPublish = string.Equals(
                        currentDirectory,
                        baseDirectory,
                        StringComparison.Ordinal);

                    Console.WriteLine("PACKAGE_STORM_CHILD_EXECUTION_OK");
                    Console.WriteLine($"STORM_CHILD_ABI={abi}");
                    Console.WriteLine($"STORM_CHILD_CAPTURE_STATUS={captureStatus}");
                    Console.WriteLine($"STORM_CHILD_CAPTURE_ERROR={captureError}");
                    Console.WriteLine($"STORM_CHILD_NAVIGATION_STATUS={navigationStatus}");
                    Console.WriteLine($"STORM_CHILD_NAVIGATION_ERROR={navigationError}");
                    Console.WriteLine(
                        $"STORM_CHILD_NAVIGATION_RESET={navigationReset.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"STORM_CHILD_INITIALIZE_LINUX_EXPORT=" +
                        $"{linuxInitializerExport.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"__LOADER_PATH___PRESENT={(!loaderPathAbsent).ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"PROJECT_OPENUSD_MAPS_CONFINED={mapsConfined.ToString().ToLowerInvariant()}");
                    Console.WriteLine($"PROJECT_OPENUSD_MAP_COUNT={projectMapCount}");
                    Console.WriteLine(
                        $"STORM_CHILD_MAP_PUBLISH_ROOT={stormMapped.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"OPENUSD_MAP_PUBLISH_ROOT={openUsdMapped.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"PROJECT_OPENUSD_DYLD_IMAGES_CONFINED={mapsConfined.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"STORM_CHILD_DYLD_PUBLISH_ROOT={stormMapped.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"OPENUSD_DYLD_PUBLISH_ROOT={openUsdMapped.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"METAL_PACKAGE_PATHS_CONFINED={metalPathsConfined.ToString().ToLowerInvariant()}");
                    Console.WriteLine($"APP_BASE_CANONICAL={appBaseCanonical}");
                    foreach (string loadedImagePath in loadedImagePaths)
                    {
                        Console.WriteLine($"OPENUSD_DYLD_IMAGE={loadedImagePath}");
                    }
                    Console.WriteLine($"CWD_IS_PUBLISH={cwdIsPublish.ToString().ToLowerInvariant()}");
                    return abi == __STORM_CHILD_ABI__ &&
                        captureCalled &&
                        captureStatus == 1 &&
                        navigationCalled &&
                        navigationStatus == 1 &&
                        navigationReset &&
                        linuxInitializerExport == OperatingSystem.IsLinux() &&
                        loaderPathAbsent &&
                        mapsConfined &&
                        metalPathsConfined &&
                        cwdIsPublish
                            ? 0
                            : 1;
                }

                [DllImport(
                    "openusd_storm_child",
                    EntryPoint = "openusd_storm_child_get_abi_version",
                    CallingConvention = CallingConvention.Cdecl)]
                private static extern uint GetStormChildAbiVersion();

                [DllImport(
                    "openusd_storm_child",
                    EntryPoint = "openusd_storm_child_get_navigation_input",
                    CallingConvention = CallingConvention.Cdecl)]
                private static extern int GetStormChildNavigationInput(
                    nint child,
                    ref NativeNavigationInput input,
                    ref NativeErrorBuffer error);

                [DllImport(
                    "openusd_storm_child",
                    EntryPoint = "openusd_storm_child_capture_framebuffer",
                    CallingConvention = CallingConvention.Cdecl)]
                private static extern int CaptureStormChildFramebuffer(
                    nint child,
                    uint backgroundRgba,
                    byte tolerance,
                    uint flags,
                    nint rgbaBuffer,
                    nuint rgbaCapacity,
                    nint rgbaRequired,
                    nint capture,
                    ref NativeErrorBuffer error);

                private static bool HasLinuxInitializerExport()
                {
                    string libraryName = OperatingSystem.IsLinux()
                        ? "libopenusd_storm_child.so"
                        : "libopenusd_storm_child.dylib";
                    nint library = NativeLibrary.Load(
                        Path.Combine(AppContext.BaseDirectory, libraryName));
                    try
                    {
                        return NativeLibrary.TryGetExport(
                            library,
                            "openusd_storm_child_initialize_linux",
                            out _);
                    }
                    finally
                    {
                        NativeLibrary.Free(library);
                    }
                }

                private static bool CaptureWithoutChild(
                    out int status,
                    out string errorMessage)
                {
                    nint errorData = Marshal.AllocHGlobal(256);
                    nint rgbaRequired = Marshal.AllocHGlobal(IntPtr.Size);
                    nint capture = Marshal.AllocHGlobal(64);
                    try
                    {
                        var error = new NativeErrorBuffer
                        {
                            Data = errorData,
                            Capacity = 256,
                        };
                        status = CaptureStormChildFramebuffer(
                            0,
                            0,
                            0,
                            0,
                            0,
                            0,
                            rgbaRequired,
                            capture,
                            ref error);
                        errorMessage = Marshal.PtrToStringUTF8(errorData) ?? string.Empty;
                        return true;
                    }
                    catch (DllNotFoundException)
                    {
                        status = -1;
                        errorMessage = string.Empty;
                        return false;
                    }
                    catch (EntryPointNotFoundException)
                    {
                        status = -1;
                        errorMessage = string.Empty;
                        return false;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(capture);
                        Marshal.FreeHGlobal(rgbaRequired);
                        Marshal.FreeHGlobal(errorData);
                    }
                }

                private static bool GetNavigationWithoutChild(
                    out int status,
                    out string errorMessage,
                    out bool reset)
                {
                    nint errorData = Marshal.AllocHGlobal(256);
                    try
                    {
                        var input = NativeNavigationInput.CreateSentinel();
                        var error = new NativeErrorBuffer
                        {
                            Data = errorData,
                            Capacity = 256,
                        };
                        status = GetStormChildNavigationInput(0, ref input, ref error);
                        errorMessage = Marshal.PtrToStringUTF8(errorData) ?? string.Empty;
                        reset = input.IsZero;
                        return status == 1 &&
                            errorMessage == "A valid Storm native child is required." &&
                            reset;
                    }
                    catch (DllNotFoundException)
                    {
                        status = -1;
                        errorMessage = string.Empty;
                        reset = false;
                        return false;
                    }
                    catch (EntryPointNotFoundException)
                    {
                        status = -1;
                        errorMessage = string.Empty;
                        reset = false;
                        return false;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(errorData);
                    }
                }

                private static (
                    bool Confined,
                    int Count,
                    bool Storm,
                    bool OpenUsd,
                    bool MetalPaths,
                    string AppBase,
                    string[] Paths)
                    ValidateMacProjectLibraryImages()
                {
                    if (!OperatingSystem.IsMacOS())
                    {
                        return (true, 0, true, true, true, string.Empty, []);
                    }

                    string appBase = ResolveCanonicalPath(AppContext.BaseDirectory);
                    if (string.IsNullOrEmpty(appBase))
                    {
                        return (false, 0, false, false, false, string.Empty, []);
                    }
                    string appPrefix = appBase.TrimEnd(Path.DirectorySeparatorChar) +
                        Path.DirectorySeparatorChar;
                    var handles = new List<nint>();
                    foreach (string libraryPath in Directory.GetFiles(
                        AppContext.BaseDirectory,
                        "*.dylib",
                        SearchOption.TopDirectoryOnly))
                    {
                        if (IsProjectOpenUsdLibrary(Path.GetFileName(libraryPath)))
                        {
                            handles.Add(NativeLibrary.Load(libraryPath));
                        }
                    }
                    handles.Add(NativeLibrary.Load(
                        "/System/Library/Frameworks/Metal.framework/Metal"));

                    string metalLibrary = ResolveCanonicalPath(Path.Combine(
                        AppContext.BaseDirectory,
                        "mesh.metallib"));
                    string metalManifest = ResolveCanonicalPath(Path.Combine(
                        AppContext.BaseDirectory,
                        "mesh.metallib.manifest.json"));
                    bool metalPaths = IsUnderAppBase(metalLibrary, appPrefix) &&
                        IsUnderAppBase(metalManifest, appPrefix);

                    var projectImages = new List<string>();
                    bool confined = true;
                    bool stormLoaded = false;
                    bool coreLoaded = false;
                    bool dotNetLoaded = false;
                    string executablePath = ResolveCanonicalPath(
                        Environment.ProcessPath ?? string.Empty);
                    bool executableLoaded = IsUnderAppBase(executablePath, appPrefix);
                    if (executableLoaded)
                    {
                        projectImages.Add(executablePath);
                    }
                    else
                    {
                        confined = false;
                    }
                    uint imageCount = DyldImageCount();
                    for (uint index = 0; index < imageCount; index++)
                    {
                        nint imageNamePointer = DyldGetImageName(index);
                        string? imageName = Marshal.PtrToStringUTF8(imageNamePointer);
                        if (string.IsNullOrWhiteSpace(imageName))
                        {
                            continue;
                        }
                        string fileName = Path.GetFileName(imageName);
                        if (!IsProjectOpenUsdLibrary(fileName))
                        {
                            continue;
                        }

                        string canonicalPath = ResolveCanonicalPath(imageName);
                        projectImages.Add(canonicalPath);
                        confined &= IsUnderAppBase(canonicalPath, appPrefix);
                        stormLoaded |= string.Equals(
                            fileName,
                            "libopenusd_storm_child.dylib",
                            StringComparison.Ordinal);
                        coreLoaded |= string.Equals(
                            fileName,
                            "libusd_ms.dylib",
                            StringComparison.Ordinal);
                        dotNetLoaded |= string.Equals(
                            fileName,
                            "libopenusd_dotnet.dylib",
                            StringComparison.Ordinal);
                    }

                    GC.KeepAlive(handles);
                    return (
                        confined &&
                            stormLoaded &&
                            coreLoaded &&
                            dotNetLoaded &&
                            executableLoaded,
                        projectImages.Count,
                        stormLoaded,
                        coreLoaded,
                        metalPaths,
                        appBase,
                        projectImages.ToArray());
                }

                private static bool IsUnderAppBase(string path, string appPrefix)
                {
                    if (string.IsNullOrEmpty(path) ||
                        !path.StartsWith(appPrefix, StringComparison.Ordinal))
                    {
                        return false;
                    }
                    string normalized = path.Replace('\\', '/');
                    return !normalized.Contains("/native/install/", StringComparison.Ordinal) &&
                        !normalized.Contains("/native/build/", StringComparison.Ordinal) &&
                        !normalized.Contains("/src/", StringComparison.Ordinal) &&
                        !normalized.Contains("/source/", StringComparison.Ordinal);
                }

                private static string ResolveCanonicalPath(string path)
                {
                    nint pathPointer = Marshal.StringToCoTaskMemUTF8(path);
                    try
                    {
                        nint resolved = RealPath(pathPointer, 0);
                        if (resolved == 0)
                        {
                            return string.Empty;
                        }
                        try
                        {
                            return Marshal.PtrToStringUTF8(resolved) ?? string.Empty;
                        }
                        finally
                        {
                            Free(resolved);
                        }
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pathPointer);
                    }
                }

                [DllImport(
                    "/usr/lib/libSystem.B.dylib",
                    EntryPoint = "_dyld_image_count",
                    CallingConvention = CallingConvention.Cdecl)]
                private static extern uint DyldImageCount();

                [DllImport(
                    "/usr/lib/libSystem.B.dylib",
                    EntryPoint = "_dyld_get_image_name",
                    CallingConvention = CallingConvention.Cdecl)]
                private static extern nint DyldGetImageName(uint imageIndex);

                [DllImport(
                    "/usr/lib/libSystem.B.dylib",
                    EntryPoint = "realpath",
                    CallingConvention = CallingConvention.Cdecl)]
                private static extern nint RealPath(
                    nint path,
                    nint resolvedPath);

                [DllImport(
                    "/usr/lib/libSystem.B.dylib",
                    EntryPoint = "free",
                    CallingConvention = CallingConvention.Cdecl)]
                private static extern void Free(nint pointer);

                private static (
                    bool Confined,
                    int Count,
                    bool Storm,
                    bool OpenUsd,
                    bool MetalPaths,
                    string AppBase,
                    string[] Paths)
                    ValidateLinuxProjectLibraryMaps()
                {
                    if (!OperatingSystem.IsLinux())
                    {
                        return (true, 0, true, true, true, string.Empty, []);
                    }

                    string publishRoot = Path.GetFullPath(AppContext.BaseDirectory)
                        .TrimEnd(Path.DirectorySeparatorChar);
                    string publishPrefix = publishRoot + Path.DirectorySeparatorChar;
                    bool confined = true;
                    bool stormMapped = false;
                    bool openUsdMapped = false;
                    int count = 0;
                    foreach (string line in File.ReadLines("/proc/self/maps"))
                    {
                        int pathStart = line.IndexOf('/');
                        if (pathStart < 0)
                        {
                            continue;
                        }

                        string mappedPath = line[pathStart..];
                        const string deletedSuffix = " (deleted)";
                        if (mappedPath.EndsWith(deletedSuffix, StringComparison.Ordinal))
                        {
                            mappedPath = mappedPath[..^deletedSuffix.Length];
                        }
                        string fileName = Path.GetFileName(mappedPath);
                        if (!IsProjectOpenUsdLibrary(fileName))
                        {
                            continue;
                        }

                        count++;
                        string fullPath = Path.GetFullPath(mappedPath);
                        string normalized = fullPath.Replace('\\', '/');
                        bool underPublishRoot =
                            fullPath.StartsWith(publishPrefix, StringComparison.Ordinal);
                        bool forbiddenSourcePath =
                            normalized.Contains("/native/install/", StringComparison.Ordinal) ||
                            normalized.Contains("/native/build/", StringComparison.Ordinal) ||
                            normalized.Contains("/src/", StringComparison.Ordinal) ||
                            normalized.Contains("/source/", StringComparison.Ordinal);
                        confined &= underPublishRoot && !forbiddenSourcePath;
                        stormMapped |= fileName.StartsWith(
                            "libopenusd_storm_child.so",
                            StringComparison.Ordinal);
                        openUsdMapped |= fileName.StartsWith(
                            "libusd_ms.so",
                            StringComparison.Ordinal);
                    }

                    return (
                        confined && stormMapped && openUsdMapped,
                        count,
                        stormMapped,
                        openUsdMapped,
                        true,
                        publishRoot,
                        []);
                }

                private static bool IsProjectOpenUsdLibrary(string fileName) =>
                    fileName.StartsWith("libopenusd_", StringComparison.Ordinal) ||
                    fileName.StartsWith("libusd_", StringComparison.Ordinal);

                [StructLayout(LayoutKind.Sequential)]
                private struct NativeErrorBuffer
                {
                    public nint Data;
                    public nuint Capacity;
                    public nuint Required;
                }

                [StructLayout(LayoutKind.Sequential)]
                private struct NativeNavigationInput
                {
                    public uint StructSize;
                    public uint Version;
                    public ulong Sequence;
                    public int PointerX;
                    public int PointerY;
                    public uint Buttons;
                    public uint Modifiers;
                    public double CumulativeWheelDelta;
                    public ulong FrameSelectedPressCount;
                    public ulong ResetAutomaticPressCount;
                    public ulong ToggleProjectionPressCount;
                    public uint State;
                    public uint Reserved;
                    public ulong OrbitLeftPressCount;
                    public ulong OrbitRightPressCount;
                    public ulong OrbitUpPressCount;
                    public ulong OrbitDownPressCount;

                    public static NativeNavigationInput CreateSentinel() => new()
                    {
                        StructSize = checked((uint)Marshal.SizeOf<NativeNavigationInput>()),
                        Version = 2,
                        Sequence = 1,
                        PointerX = 1,
                        PointerY = 1,
                        Buttons = 1,
                        Modifiers = 1,
                        CumulativeWheelDelta = 1,
                        FrameSelectedPressCount = 1,
                        ResetAutomaticPressCount = 1,
                        ToggleProjectionPressCount = 1,
                        State = 1,
                        Reserved = 1,
                        OrbitLeftPressCount = 1,
                        OrbitRightPressCount = 1,
                        OrbitUpPressCount = 1,
                        OrbitDownPressCount = 1,
                    };

                    public readonly bool IsZero =>
                        StructSize == 0 &&
                        Version == 0 &&
                        Sequence == 0 &&
                        PointerX == 0 &&
                        PointerY == 0 &&
                        Buttons == 0 &&
                        Modifiers == 0 &&
                        CumulativeWheelDelta == 0 &&
                        FrameSelectedPressCount == 0 &&
                        ResetAutomaticPressCount == 0 &&
                        ToggleProjectionPressCount == 0 &&
                        State == 0 &&
                        Reserved == 0 &&
                        OrbitLeftPressCount == 0 &&
                        OrbitRightPressCount == 0 &&
                        OrbitUpPressCount == 0 &&
                        OrbitDownPressCount == 0;
                }
            }
            """
            .Replace("__OPERATING_SYSTEM_GUARD__", platform.OperatingSystemGuard, StringComparison.Ordinal)
            .Replace("__LOADER_PATH__", loaderPathName, StringComparison.Ordinal)
            .Replace(
                "__STORM_CHILD_ABI__",
                stormChildAbiVersion.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal));

        string globalPackagesRoot = Path.Combine(workRoot, "linux-storm-global-packages");
        var publishArguments = new List<string>
        {
            "publish",
            "Consumer.csproj",
            "-c",
            "Release",
            "-r",
            platform.Rid,
            "--nologo",
            "--configfile",
            "NuGet.config",
            "-o",
            publishRoot,
        };
        if (!publishAot)
        {
            publishArguments.Add("--self-contained");
            publishArguments.Add("false");
        }
        CommandResult result = await RunDotnetAsync(
            consumerRoot,
            [.. publishArguments],
            globalPackagesRoot);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Output);
        }

        return new ExecutionConsumer(
            projectPath,
            publishRoot,
            Path.Combine(consumerRoot, "obj", "project.assets.json"));
    }

    private static async Task<ExecutionConsumer> PublishImagingExecutionConsumerAsync(
        string workRoot,
        string packageRoot,
        string packageVersion,
        ExecutionPlatform platform,
        bool publishAot)
    {
        string consumerRoot = Path.Combine(
            workRoot,
            $"imaging-execution-consumer-{platform.Rid}");
        string publishRoot = Path.Combine(consumerRoot, "publish");
        string projectPath = Path.Combine(consumerRoot, "Consumer.csproj");
        Directory.CreateDirectory(consumerRoot);

        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Directory.Build.props"),
            "<Project />");
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Directory.Packages.props"),
            """
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "NuGet.config"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="isolated-openusd-feed" value="{packageRoot}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="isolated-openusd-feed">
                  <package pattern="OpenUsd*" />
                </packageSource>
                <packageSource key="nuget.org">
                  <package pattern="Microsoft.*" />
                  <package pattern="runtime.*" />
                  <package pattern="SharpMetal" />
                  <package pattern="Silk.NET.*" />
                  <package pattern="Stride.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        await File.WriteAllTextAsync(
            projectPath,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RuntimeIdentifier>{platform.Rid}</RuntimeIdentifier>
                <PublishAot>{publishAot.ToString().ToLowerInvariant()}</PublishAot>
                <InvariantGlobalization>true</InvariantGlobalization>
                <Nullable>enable</Nullable>
                <StripSymbols>true</StripSymbols>
                <OptimizationPreference>Size</OptimizationPreference>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="{platform.BackendPackageId}"
                                  Version="{packageVersion}" />
                <PackageReference Include="OpenUsd.Runtime.Imaging"
                                  Version="{packageVersion}" />
              </ItemGroup>
              <ItemGroup>
                <None Update="minimal.usda" CopyToOutputDirectory="PreserveNewest"
                      CopyToPublishDirectory="PreserveNewest" />
                <None Update="ocio-test-config.ocio" CopyToOutputDirectory="PreserveNewest"
                      CopyToPublishDirectory="PreserveNewest" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "minimal.usda"),
            """
            #usda 1.0
            (
                defaultPrim = "World"
            )

            def Xform "World"
            {
                def Cube "Cube"
                {
                    double size = 2
                }
            }
            """);
        File.Copy(
            Path.Combine(FindRepositoryRoot(), "test-assets", "ocio-test-config.ocio"),
            Path.Combine(consumerRoot, "ocio-test-config.ocio"));
        await File.WriteAllTextAsync(
            Path.Combine(consumerRoot, "Program.cs"),
            CreateImagingConsumerProgram(
                platform,
                ReadStormChildAbiVersion(FindRepositoryRoot())));

        string globalPackagesRoot = Path.Combine(workRoot, "imaging-global-packages");
        var publishArguments = new List<string>
        {
            "publish",
            "Consumer.csproj",
            "-c",
            "Release",
            "-r",
            platform.Rid,
            "--nologo",
            "--configfile",
            "NuGet.config",
            "-o",
            publishRoot,
        };
        if (!publishAot)
        {
            publishArguments.Add("--self-contained");
            publishArguments.Add("false");
        }
        CommandResult result = await RunDotnetAsync(
            consumerRoot,
            [.. publishArguments],
            globalPackagesRoot);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.Output);
        }

        return new ExecutionConsumer(
            projectPath,
            publishRoot,
            Path.Combine(consumerRoot, "obj", "project.assets.json"));
    }

    private static string CreateImagingConsumerProgram(
        ExecutionPlatform platform,
        int stormChildAbiVersion) =>
        """
        using System;
        using System.IO;
        using System.Runtime.InteropServices;
        using OpenUsd.Rendering;
        using OpenUsd.Rendering.Silk;
        using __BACKEND_NAMESPACE__;

        namespace PackageImagingExecutionConsumer;

        internal static class Program
        {
            public static int Main()
            {
                try
                {
                    if (!__OPERATING_SYSTEM_GUARD__)
                    {
                        return 8;
                    }

                    string pluginPath = Path.Combine(
                        AppContext.BaseDirectory,
                        "plugin",
                        "usd");
                    string hdSilkLibrary = Path.Combine(
                        AppContext.BaseDirectory,
                        "__HDSILK_LIBRARY__");
                    string stagePath = Path.Combine(
                        AppContext.BaseDirectory,
                        "minimal.usda");
                    bool pluginLayout =
                        File.Exists(Path.Combine(pluginPath, "plugInfo.json")) &&
                        File.Exists(Path.Combine(
                            pluginPath,
                            "hdSilk",
                            "resources",
                            "plugInfo.json")) &&
                        File.Exists(hdSilkLibrary);
                    if (!pluginLayout || !File.Exists(stagePath))
                    {
                        return 2;
                    }

                    string ocioConfigPath = Path.Combine(
                        AppContext.BaseDirectory,
                        "ocio-test-config.ocio");
                    using var ocioProcessor = new SilkOpenColorIoDisplayTransform(
                        ocioConfigPath,
                        "linear",
                        "TestDisplay",
                        "TestView").CreateProcessor();
                    byte[] linearPixel = [0x00, 0x38, 0x00, 0x38, 0x00, 0x38, 0x00, 0x3C];
                    byte[] displayPixel = new byte[4];
                    ocioProcessor.Apply(linearPixel, displayPixel, 1, 1, 0);
                    bool ocioDisplayTransform =
                        displayPixel[0] > 0 &&
                        displayPixel[1] > 0 &&
                        displayPixel[2] > 0 &&
                        displayPixel[3] == byte.MaxValue;
                    if (!ocioDisplayTransform)
                    {
                        return 12;
                    }

                    uint stormChildAbi = 0;
                    bool stormChildDllImport = true;
                    int stormChildCaptureStatus = 0;
                    string stormChildCaptureError = string.Empty;
                    bool stormChildCaptureDllImport = true;
                    int stormChildNavigationStatus = 0;
                    string stormChildNavigationError = string.Empty;
                    bool stormChildNavigationReset = true;
                    bool stormChildNavigationDllImport = true;
                    bool stormChildLinuxInitializerExport = false;
                    if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                    {
                        stormChildAbi = GetStormChildAbiVersion();
                        stormChildDllImport = stormChildAbi == __STORM_CHILD_ABI__;
                        if (!stormChildDllImport)
                        {
                            return 6;
                        }
                        stormChildCaptureDllImport = CaptureStormChildWithoutWindow(
                            out stormChildCaptureStatus,
                            out stormChildCaptureError);
                        if (!stormChildCaptureDllImport)
                        {
                            return 9;
                        }
                        stormChildNavigationDllImport = GetNavigationWithoutWindow(
                            out stormChildNavigationStatus,
                            out stormChildNavigationError,
                            out stormChildNavigationReset);
                        if (!stormChildNavigationDllImport)
                        {
                            return 11;
                        }
                        stormChildLinuxInitializerExport = HasLinuxInitializerExport();
                        if (stormChildLinuxInitializerExport)
                        {
                            return 10;
                        }
                    }

                    using ISilkGraphicsDevice device = __DEVICE_FACTORY__;
                    using var sceneResources = new SilkSceneGpuResources(device);
                    var scene = new SilkSceneState();
                    using OpenUsdSilkSession session =
                        OpenUsdSilkRuntime.Create(pluginPath, stagePath);

                    using OpenUsdSilkPage first = session.Sync(
                        1280,
                        720,
                        camera: CameraState.Default);
                    (int firstFrames, int firstUpserts, int firstRemovals) =
                        CountCommands(first);
                    SilkSceneDelta firstDelta = scene.Apply(first);
                    sceneResources.Apply(scene, firstDelta);
                    bool incrementalUpload =
                        device.Backend == SilkGraphicsBackend.__BACKEND_ENUM__ &&
                        __SOFTWARE_REQUIREMENT__ &&
                        first.AbiVersion == __SILK_PAGE_ABI__ &&
                        firstFrames == 1 &&
                        firstUpserts > 0 &&
                        firstRemovals == 0 &&
                        sceneResources.Meshes.Count == firstUpserts;
                    if (!incrementalUpload)
                    {
                        return 3;
                    }

                    using OpenUsdSilkPage second = session.Sync(
                        1280,
                        720,
                        camera: CameraState.Default);
                    (int steadyFrames, int steadyUpserts, int steadyRemovals) =
                        CountCommands(second);
                    SilkSceneDelta secondDelta = scene.Apply(second);
                    sceneResources.Apply(scene, secondDelta);
                    device.WaitIdle();
                    bool steadyPage =
                        steadyFrames == 1 &&
                        steadyUpserts == 0 &&
                        steadyRemovals == 0;
                    if (!steadyPage)
                    {
                        return 4;
                    }

                    string currentDirectory = Path.GetFullPath(".")
                        .TrimEnd(Path.DirectorySeparatorChar);
                    string baseDirectory = AppContext.BaseDirectory
                        .TrimEnd(Path.DirectorySeparatorChar);
                    bool cwdIsPublish = string.Equals(
                        currentDirectory,
                        baseDirectory,
                        StringComparison.OrdinalIgnoreCase);
                    bool macLoaderPathAbsent =
                        !OperatingSystem.IsMacOS() ||
                        string.IsNullOrEmpty(
                            Environment.GetEnvironmentVariable("DYLD_LIBRARY_PATH"));

                    Console.WriteLine("PACKAGE_IMAGING_EXECUTION_OK");
                    Console.WriteLine($"FIRST_PAGE_FRAMES={firstFrames}");
                    Console.WriteLine($"FIRST_PAGE_UPSERTS={firstUpserts}");
                    Console.WriteLine($"FIRST_PAGE_REMOVALS={firstRemovals}");
                    Console.WriteLine($"FIRST_PAGE_MESHES={sceneResources.Meshes.Count}");
                    Console.WriteLine($"STEADY_PAGE_FRAMES={steadyFrames}");
                    Console.WriteLine($"STEADY_PAGE_UPSERTS={steadyUpserts}");
                    Console.WriteLine($"STEADY_PAGE_REMOVALS={steadyRemovals}");
                    Console.WriteLine($"__UPLOAD_MARKER__={incrementalUpload.ToString().ToLowerInvariant()}");
                    Console.WriteLine($"GPU_BACKEND=__BACKEND_DISPLAY_NAME__");
                    Console.WriteLine($"INCREMENTAL_GPU_UPLOAD={incrementalUpload.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"SOFTWARE_DEVICE={device.Capabilities.IsSoftware.ToString().ToLowerInvariant()}");
                    Console.WriteLine("WAIT_IDLE=true");
                    Console.WriteLine($"PLUGIN_LAYOUT={pluginLayout.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"OCIO_DISPLAY_TRANSFORM={ocioDisplayTransform.ToString().ToLowerInvariant()}");
                    Console.WriteLine($"STORM_CHILD_ABI={stormChildAbi}");
                    Console.WriteLine(
                        $"STORM_CHILD_DLLIMPORT={stormChildDllImport.ToString().ToLowerInvariant()}");
                    Console.WriteLine($"STORM_CHILD_CAPTURE_STATUS={stormChildCaptureStatus}");
                    Console.WriteLine($"STORM_CHILD_CAPTURE_ERROR={stormChildCaptureError}");
                    Console.WriteLine(
                        $"STORM_CHILD_CAPTURE_DLLIMPORT={stormChildCaptureDllImport.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"STORM_CHILD_NAVIGATION_STATUS={stormChildNavigationStatus}");
                    Console.WriteLine(
                        $"STORM_CHILD_NAVIGATION_ERROR={stormChildNavigationError}");
                    Console.WriteLine(
                        $"STORM_CHILD_NAVIGATION_RESET={stormChildNavigationReset.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"STORM_CHILD_NAVIGATION_DLLIMPORT=" +
                        $"{stormChildNavigationDllImport.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"STORM_CHILD_INITIALIZE_LINUX_EXPORT=" +
                        $"{stormChildLinuxInitializerExport.ToString().ToLowerInvariant()}");
                    Console.WriteLine(
                        $"DYLD_LIBRARY_PATH_PRESENT={(!macLoaderPathAbsent).ToString().ToLowerInvariant()}");
                    Console.WriteLine($"CWD_IS_PUBLISH={cwdIsPublish.ToString().ToLowerInvariant()}");
                    return cwdIsPublish && macLoaderPathAbsent ? 0 : 5;
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(exception);
                    return 1;
                }
            }

            [DllImport(
                "openusd_storm_child",
                EntryPoint = "openusd_storm_child_get_abi_version",
                CallingConvention = CallingConvention.Cdecl)]
            private static extern uint GetStormChildAbiVersion();

            [DllImport(
                "openusd_storm_child",
                EntryPoint = "openusd_storm_child_get_navigation_input",
                CallingConvention = CallingConvention.Cdecl)]
            private static extern int GetStormChildNavigationInput(
                nint child,
                ref NativeNavigationInput input,
                ref NativeErrorBuffer error);

            [DllImport(
                "openusd_storm_child",
                EntryPoint = "openusd_storm_child_capture_framebuffer",
                CallingConvention = CallingConvention.Cdecl)]
            private static extern int CaptureStormChildFramebuffer(
                nint child,
                uint backgroundRgba,
                byte tolerance,
                uint flags,
                nint rgbaBuffer,
                nuint rgbaCapacity,
                nint rgbaRequired,
                nint capture,
                ref NativeErrorBuffer error);

            private static bool HasLinuxInitializerExport()
            {
                string libraryName = OperatingSystem.IsWindows()
                    ? "openusd_storm_child.dll"
                    : "libopenusd_storm_child.dylib";
                nint library = NativeLibrary.Load(
                    Path.Combine(AppContext.BaseDirectory, libraryName));
                try
                {
                    return NativeLibrary.TryGetExport(
                        library,
                        "openusd_storm_child_initialize_linux",
                        out _);
                }
                finally
                {
                    NativeLibrary.Free(library);
                }
            }

            private static bool CaptureStormChildWithoutWindow(
                out int status,
                out string errorMessage)
            {
                nint errorData = Marshal.AllocHGlobal(256);
                nint rgbaRequired = Marshal.AllocHGlobal(IntPtr.Size);
                nint capture = Marshal.AllocHGlobal(64);
                try
                {
                    var error = new NativeErrorBuffer
                    {
                        Data = errorData,
                        Capacity = 256,
                    };
                    status = CaptureStormChildFramebuffer(
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        rgbaRequired,
                        capture,
                        ref error);
                    errorMessage = Marshal.PtrToStringUTF8(errorData) ?? string.Empty;
                    return status == 1 &&
                        errorMessage == "A valid Storm native child is required.";
                }
                finally
                {
                    Marshal.FreeHGlobal(capture);
                    Marshal.FreeHGlobal(rgbaRequired);
                    Marshal.FreeHGlobal(errorData);
                }
            }

            private static bool GetNavigationWithoutWindow(
                out int status,
                out string errorMessage,
                out bool reset)
            {
                nint errorData = Marshal.AllocHGlobal(256);
                try
                {
                    var input = NativeNavigationInput.CreateSentinel();
                    var error = new NativeErrorBuffer
                    {
                        Data = errorData,
                        Capacity = 256,
                    };
                    status = GetStormChildNavigationInput(0, ref input, ref error);
                    errorMessage = Marshal.PtrToStringUTF8(errorData) ?? string.Empty;
                    reset = input.IsZero;
                    return status == 1 &&
                        errorMessage == "A valid Storm native child is required." &&
                        reset;
                }
                catch (DllNotFoundException)
                {
                    status = -1;
                    errorMessage = string.Empty;
                    reset = false;
                    return false;
                }
                catch (EntryPointNotFoundException)
                {
                    status = -1;
                    errorMessage = string.Empty;
                    reset = false;
                    return false;
                }
                finally
                {
                    Marshal.FreeHGlobal(errorData);
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct NativeErrorBuffer
            {
                public nint Data;
                public nuint Capacity;
                public nuint Required;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct NativeNavigationInput
            {
                public uint StructSize;
                public uint Version;
                public ulong Sequence;
                public int PointerX;
                public int PointerY;
                public uint Buttons;
                public uint Modifiers;
                public double CumulativeWheelDelta;
                public ulong FrameSelectedPressCount;
                public ulong ResetAutomaticPressCount;
                public ulong ToggleProjectionPressCount;
                public uint State;
                public uint Reserved;
                public ulong OrbitLeftPressCount;
                public ulong OrbitRightPressCount;
                public ulong OrbitUpPressCount;
                public ulong OrbitDownPressCount;

                public static NativeNavigationInput CreateSentinel() => new()
                {
                    StructSize = checked((uint)Marshal.SizeOf<NativeNavigationInput>()),
                    Version = 2,
                    Sequence = 1,
                    PointerX = 1,
                    PointerY = 1,
                    Buttons = 1,
                    Modifiers = 1,
                    CumulativeWheelDelta = 1,
                    FrameSelectedPressCount = 1,
                    ResetAutomaticPressCount = 1,
                    ToggleProjectionPressCount = 1,
                    State = 1,
                    Reserved = 1,
                    OrbitLeftPressCount = 1,
                    OrbitRightPressCount = 1,
                    OrbitUpPressCount = 1,
                    OrbitDownPressCount = 1,
                };

                public readonly bool IsZero =>
                    StructSize == 0 &&
                    Version == 0 &&
                    Sequence == 0 &&
                    PointerX == 0 &&
                    PointerY == 0 &&
                    Buttons == 0 &&
                    Modifiers == 0 &&
                    CumulativeWheelDelta == 0 &&
                    FrameSelectedPressCount == 0 &&
                    ResetAutomaticPressCount == 0 &&
                    ToggleProjectionPressCount == 0 &&
                    State == 0 &&
                    Reserved == 0 &&
                    OrbitLeftPressCount == 0 &&
                    OrbitRightPressCount == 0 &&
                    OrbitUpPressCount == 0 &&
                    OrbitDownPressCount == 0;
            }

            private static (int Frames, int Upserts, int Removals)
                CountCommands(OpenUsdSilkPage page)
            {
                int frames = 0;
                int upserts = 0;
                int removals = 0;
                using SilkCommandEnumerator commands = page.GetEnumerator();
                while (commands.MoveNext())
                {
                    switch (commands.Current.Type)
                    {
                        case SilkCommandType.Frame:
                            SilkFrameCommand frame = commands.Current.AsFrame();
                            _ = frame.GetViewElement(0);
                            frames++;
                            break;
                        case SilkCommandType.MeshUpsert:
                            SilkMeshUpsertCommand mesh =
                                commands.Current.AsMeshUpsert();
                            _ = mesh.Path;
                            _ = mesh.GetPointComponent(0, 0);
                            _ = mesh.GetIndex(0);
                            upserts++;
                            break;
                        case SilkCommandType.MeshRemove:
                            _ = commands.Current.AsMeshRemove().Path;
                            removals++;
                            break;
                        default:
                            return (-1, -1, -1);
                    }
                }
                return (frames, upserts, removals);
            }
        }
        """
        .Replace("__BACKEND_NAMESPACE__", platform.BackendNamespace, StringComparison.Ordinal)
        .Replace("__OPERATING_SYSTEM_GUARD__", platform.OperatingSystemGuard, StringComparison.Ordinal)
        .Replace("__HDSILK_LIBRARY__", platform.HdSilkLibrary, StringComparison.Ordinal)
        .Replace("__DEVICE_FACTORY__", platform.DeviceFactory, StringComparison.Ordinal)
        .Replace("__BACKEND_ENUM__", platform.BackendEnum, StringComparison.Ordinal)
        .Replace(
            "__SOFTWARE_REQUIREMENT__",
            platform.RequiresSwiftShader ? "device.Capabilities.IsSoftware" : "true",
            StringComparison.Ordinal)
        .Replace("__UPLOAD_MARKER__", platform.UploadMarker, StringComparison.Ordinal)
        .Replace(
            "__BACKEND_DISPLAY_NAME__",
            platform.BackendDisplayName,
            StringComparison.Ordinal)
        .Replace(
            "__STORM_CHILD_ABI__",
            stormChildAbiVersion.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal)
        .Replace(
            "__SILK_PAGE_ABI__",
            RequiredSilkPageAbiVersion.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

    private static void AssertPackageOnlyGraph(
        string assetsPath,
        IReadOnlyCollection<string> expectedPackageIds)
    {
        using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        JsonElement libraries = assets.RootElement.GetProperty("libraries");
        var expectedPackages = new HashSet<string>(expectedPackageIds, StringComparer.Ordinal);
        foreach (JsonProperty library in libraries.EnumerateObject())
        {
            string type = library.Value.GetProperty("type").GetString()
                ?? throw new InvalidOperationException($"Package type is missing for {library.Name}.");
            if (string.Equals(type, "project", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{library.Name} restored from a project.");
            }
            if (library.Name.StartsWith("OpenUsd", StringComparison.Ordinal) &&
                !string.Equals(type, "package", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{library.Name} restored as '{type}' instead of a package.");
            }

            string packageId = library.Name.Split('/')[0];
            expectedPackages.Remove(packageId);
        }

        if (expectedPackages.Count != 0)
        {
            throw new InvalidOperationException(
                $"The package graph is missing: {string.Join(", ", expectedPackages)}.");
        }
    }

    private static string[] AllRuntimePackageIds() =>
    [
        "OpenUsd.Runtime.Core",
        "OpenUsd.Runtime.Core.win-x64",
        "OpenUsd.Runtime.Core.linux-x64",
        "OpenUsd.Runtime.Core.osx-arm64",
        "OpenUsd.Runtime.Imaging",
        "OpenUsd.Runtime.Imaging.win-x64",
        "OpenUsd.Runtime.Imaging.linux-x64",
        "OpenUsd.Runtime.Imaging.osx-arm64",
        "OpenUsd.Runtime.Cesium",
        "OpenUsd.Runtime.Cesium.win-x64",
        "OpenUsd.Runtime.Cesium.linux-x64",
        "OpenUsd.Runtime.Cesium.osx-arm64",
        "OpenUsd.Runtime.Physics",
        "OpenUsd.Runtime.Physics.win-x64",
        "OpenUsd.Runtime.Physics.linux-x64",
    ];

    /// <summary>
    /// The graph a physics consumer restores: the managed API, the physics metapackage and its two
    /// RID packages, and the host RID Core package because the physics shim links the OpenUSD
    /// monolith.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Core appears once, under the host RID, and neither the Core metapackage nor another RID's
    /// Core package appears at all. Nothing references the Core metapackage: <c>OpenUsd</c> depends
    /// only on <c>OpenUsd.Interop</c>, and each physics RID package depends on the Core package of
    /// its own RID. The other RID's physics package is a dependency-free restore stub in this
    /// fixture, so it pulls no Core payload for a platform this host cannot execute anyway.
    /// </para>
    /// <para>
    /// No CUDA package appears here, and none exists. The PhysX GPU and device modules are NVIDIA
    /// proprietary packman blobs that this project does not redistribute, so a licensed user places
    /// them beside the deployed runtime instead of restoring them.
    /// </para>
    /// </remarks>
    private static string[] GetPhysicsConsumerPackageGraph(ExecutionPlatform platform) =>
    [
        "OpenUsd.Interop",
        "OpenUsd",
        "OpenUsd.Physics",
        $"OpenUsd.Runtime.Core.{platform.Rid}",
        "OpenUsd.Runtime.Physics",
        "OpenUsd.Runtime.Physics.win-x64",
        "OpenUsd.Runtime.Physics.linux-x64",
    ];


    private static string[] GetCesiumConsumerPackageGraph(ExecutionPlatform platform) =>
    [
        "OpenUsd.Interop",
        "OpenUsd",
        "OpenUsd.Cesium",
        "OpenUsd.Runtime.Core",
        "OpenUsd.Runtime.Core.win-x64",
        "OpenUsd.Runtime.Core.linux-x64",
        "OpenUsd.Runtime.Core.osx-arm64",
        "OpenUsd.Runtime.Cesium",
        "OpenUsd.Runtime.Cesium.win-x64",
        "OpenUsd.Runtime.Cesium.linux-x64",
        "OpenUsd.Runtime.Cesium.osx-arm64",
    ];

    private static string[] GetRuntimeImagingMetaPackageGraph(ExecutionPlatform platform) =>
    [
        "OpenUsd.Runtime.Imaging",
        "OpenUsd.Runtime.Imaging.win-x64",
        "OpenUsd.Runtime.Imaging.linux-x64",
        "OpenUsd.Runtime.Imaging.osx-arm64",
        $"OpenUsd.Runtime.Core.{platform.Rid}",
    ];

    private static string[] GetImagingPackageGraph(ExecutionPlatform platform) =>
    [
        "OpenUsd",
        "OpenUsd.Interop",
        "OpenUsd.Rendering",
        "OpenUsd.Rendering.Silk",
        platform.BackendPackageId,
        $"OpenUsd.Runtime.Core.{platform.Rid}",
        $"OpenUsd.Runtime.Imaging.{platform.Rid}",
    ];

    private static string[] GetCoreMetaPackageGraph() =>
    [
        "OpenUsd.Runtime.Core",
        "OpenUsd.Runtime.Core.win-x64",
        "OpenUsd.Runtime.Core.linux-x64",
        "OpenUsd.Runtime.Core.osx-arm64",
    ];

    private static string[] GetImagingMetaPackageGraph(ExecutionPlatform platform) =>
    [
        "OpenUsd",
        "OpenUsd.Interop",
        "OpenUsd.Rendering",
        "OpenUsd.Rendering.Silk",
        platform.BackendPackageId,
        "OpenUsd.Runtime.Imaging",
        "OpenUsd.Runtime.Imaging.win-x64",
        "OpenUsd.Runtime.Imaging.linux-x64",
        "OpenUsd.Runtime.Imaging.osx-arm64",
        $"OpenUsd.Runtime.Core.{platform.Rid}",
    ];

    private static string GetCesiumLibraryName(ExecutionPlatform platform) => platform.Rid switch
    {
        "win-x64" => "openusd_cesium.dll",
        "linux-x64" => "libopenusd_cesium.so",
        "osx-arm64" => "libopenusd_cesium.dylib",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform.Rid, null),
    };

    private static string GetPhysicsLibraryName(ExecutionPlatform platform) => platform.Rid switch
    {
        "win-x64" => "openusd_physx.dll",
        "linux-x64" => "libopenusd_physx.so",
        // No osx-arm64 case: the pinned vcpkg PhysX port has no arm64-osx build, so a name here
        // would describe a library that cannot exist.
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform.Rid, null),
    };

    private static string GetExecutablePath(string publishRoot, string name) =>
        Path.Combine(publishRoot, OperatingSystem.IsWindows() ? $"{name}.exe" : name);

    private static Dictionary<string, string>? GetImagingRuntimeEnvironment(
        ExecutionPlatform platform,
        string publishRoot)
    {
        if (platform.RequiresSwiftShader)
        {
            string icdPath = PrepareSwiftShaderManifest(platform, publishRoot);
            string loaderPath = Path.Combine(publishRoot, "libvulkan.so");
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LD_LIBRARY_PATH"] = publishRoot,
                ["LD_PRELOAD"] = loaderPath,
                ["VK_DRIVER_FILES"] = icdPath,
                ["VK_ICD_FILENAMES"] = icdPath,
            };
        }
        return null;
    }

    private static string PrepareSwiftShaderManifest(
        ExecutionPlatform platform,
        string publishRoot)
    {
        string repositoryRoot = FindRepositoryRoot();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "vulkan-test-runtime.lock.json")));
        JsonElement root = document.RootElement;
        JsonElement runtime = root
            .GetProperty("runtimes")
            .GetProperty(platform.Rid);
        string loaderName = runtime.GetProperty("loader").GetString() ??
            throw new InvalidDataException("The Vulkan test loader name is missing.");
        string driverName = runtime.GetProperty("driver").GetString() ??
            throw new InvalidDataException("The Vulkan test driver name is missing.");
        string loaderPath = Path.Combine(publishRoot, loaderName);
        string driverPath = Path.Combine(publishRoot, driverName);
        string expectedLoader = runtime.GetProperty("loaderSha256").GetString() ??
            throw new InvalidDataException("The Vulkan test loader hash is missing.");
        string expectedDriver = runtime.GetProperty("driverSha256").GetString() ??
            throw new InvalidDataException("The Vulkan test driver hash is missing.");
        if (!string.Equals(
                GetFileSha256(loaderPath),
                expectedLoader,
                StringComparison.Ordinal) ||
            !string.Equals(
                GetFileSha256(driverPath),
                expectedDriver,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The package-only Vulkan test runtime does not match its lock.");
        }

        string manifestPath = Path.Combine(
            publishRoot,
            $"openusd-swiftshader-{platform.Rid}.json");
        var manifest = new
        {
            file_format_version = "1.0.0",
            ICD = new
            {
                library_path = Path.GetFullPath(driverPath),
                api_version = root.GetProperty("apiVersion").GetString(),
            },
        };
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, IndentedJsonOptions));
        return manifestPath;
    }

    private static Dictionary<string, string>? GetCoreRuntimeEnvironment(
        ExecutionPlatform platform,
        string publishRoot)
    {
        if (platform.Rid == "linux-x64")
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LD_LIBRARY_PATH"] = publishRoot,
            };
        }
        return null;
    }

    private static async Task AssertPlatformBackendAssetsAsync(
        ExecutionPlatform platform,
        string publishRoot)
    {
        if (platform.Rid == "win-x64")
        {
            await Assert.That(File.Exists(Path.Combine(publishRoot, "vulkan-1.dll"))).IsTrue();
            return;
        }
        if (!platform.RequiresSwiftShader)
        {
            return;
        }

        string icdPath = Path.Combine(publishRoot, "vk_swiftshader_icd.json");
        await Assert.That(File.Exists(Path.Combine(publishRoot, "libvulkan.so"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(publishRoot, "libvk_swiftshader.so"))).IsTrue();
        await Assert.That(File.Exists(icdPath)).IsTrue();
        string icd = await File.ReadAllTextAsync(icdPath);
        await Assert.That(icd).DoesNotContain("native/install");
        await Assert.That(icd).DoesNotContain("src/OpenUsd");
    }

    private static async Task AssertNoSourcePathLeakageAsync(
        string value,
        string repositoryRoot)
    {
        await Assert.That(value).DoesNotContain(repositoryRoot);
        await Assert.That(value).DoesNotContain("native\\install");
        await Assert.That(value).DoesNotContain("native/install");
        await Assert.That(value).DoesNotContain("src\\OpenUsd");
        await Assert.That(value).DoesNotContain("src/OpenUsd");
    }

    private static async Task AssertNoNativeSourcePathLeakageAsync(string value)
    {
        string normalized = value.Replace('\\', '/');
        await Assert.That(normalized).DoesNotContain("/native/install/");
        await Assert.That(normalized).DoesNotContain("/native/build/");
        await Assert.That(normalized).DoesNotContain("/src/");
        await Assert.That(normalized).DoesNotContain("/source/");
    }

    private static async Task WriteLinuxStormChildArtifactsAsync(
        string repositoryRoot,
        string packagePath,
        string executionOutput)
    {
        string artifactRoot = Path.Combine(
            repositoryRoot,
            "artifacts",
            "package-linux-storm-child");
        if (Directory.Exists(artifactRoot))
        {
            Directory.Delete(artifactRoot, recursive: true);
        }
        Directory.CreateDirectory(artifactRoot);
        string artifactPackagePath = Path.Combine(
            artifactRoot,
            Path.GetFileName(packagePath));
        File.Copy(packagePath, artifactPackagePath, overwrite: true);

        await using FileStream packageStream = File.OpenRead(packagePath);
        string packageHash = Convert.ToHexString(
            await SHA256.HashDataAsync(packageStream));
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry validationEntry = package.Entries.Single(
            entry => entry.FullName ==
                "build/OpenUsd.Runtime.Imaging.linux-x64.native-validation.json");
        string validationPath = Path.Combine(
            artifactRoot,
            "linux-native-validation.json");
        await using (Stream source = validationEntry.Open())
        await using (FileStream destination = File.Create(validationPath))
        {
            await source.CopyToAsync(destination);
        }
        await using FileStream validationStream = File.OpenRead(validationPath);
        string validationHash = Convert.ToHexString(
            await SHA256.HashDataAsync(validationStream));
        using JsonDocument validationDocument = JsonDocument.Parse(
            await File.ReadAllBytesAsync(validationPath));
        JsonElement validationTopology = validationDocument.RootElement
            .GetProperty("stormChildTopology");

        var stormEntries = new List<object>();
        foreach (ZipArchiveEntry entry in package.Entries.Where(
            entry => Path.GetFileName(entry.FullName).StartsWith(
                "libopenusd_storm_child.so",
                StringComparison.Ordinal)))
        {
            await using Stream entryStream = entry.Open();
            byte[] entryBytes;
            using (var memory = new MemoryStream())
            {
                await entryStream.CopyToAsync(memory);
                entryBytes = memory.ToArray();
            }
            uint unixMode = ((uint)entry.ExternalAttributes >> 16) & 0xFFFF;
            bool isSymbolicLink = (unixMode & 0xF000) == 0xA000;
            stormEntries.Add(new
            {
                path = entry.FullName,
                type = isSymbolicLink ? "symlink" : "regular",
                target = isSymbolicLink
                    ? System.Text.Encoding.UTF8.GetString(entryBytes)
                    : null,
                size = entry.Length,
                sha256 = Convert.ToHexString(SHA256.HashData(entryBytes)),
            });
        }

        var evidence = new
        {
            schemaVersion = 3,
            rid = "linux-x64",
            package = Path.GetFileName(packagePath),
            packageSize = new FileInfo(packagePath).Length,
            packageSha256 = packageHash,
            nativeValidation = Path.GetFileName(validationPath),
            nativeValidationSha256 = validationHash,
            stormChildSoname = validationTopology.GetProperty("soname").GetString(),
            stormChildRealFile = validationTopology.GetProperty("realFile").GetString(),
            stormChildRealFileSha256 =
                validationTopology.GetProperty("realFileSha256").GetString(),
            stormChildEntries = stormEntries,
            execution = executionOutput
                .Split(
                    [Environment.NewLine],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith("PACKAGE_", StringComparison.Ordinal) ||
                    line.StartsWith("STORM_CHILD_", StringComparison.Ordinal) ||
                    line.StartsWith("LD_LIBRARY_PATH_", StringComparison.Ordinal) ||
                    line.StartsWith("PROJECT_OPENUSD_", StringComparison.Ordinal) ||
                    line.StartsWith("OPENUSD_MAP_", StringComparison.Ordinal) ||
                    line.StartsWith("CWD_IS_", StringComparison.Ordinal))
                .ToArray(),
        };
        await File.WriteAllTextAsync(
            Path.Combine(artifactRoot, "package-evidence.json"),
            JsonSerializer.Serialize(evidence, IndentedJsonOptions));
    }

    private static async Task WriteMacOsStormChildArtifactsAsync(
        string repositoryRoot,
        string packagePath,
        string executionOutput,
        IReadOnlyCollection<MacCodeSignEvidence> signingEvidence,
        MacLoadedImageValidation loadedImages,
        MacStormChildIdentity stormIdentity)
    {
        string artifactRoot = Path.Combine(
            repositoryRoot,
            "artifacts",
            "package-macos-storm-child");
        Directory.CreateDirectory(artifactRoot);
        string artifactPackagePath = Path.Combine(
            artifactRoot,
            Path.GetFileName(packagePath));
        File.Copy(packagePath, artifactPackagePath, overwrite: true);

        await using FileStream packageStream = File.OpenRead(packagePath);
        string packageHash = Convert.ToHexString(
            await SHA256.HashDataAsync(packageStream));
        using ZipArchive package = ZipFile.OpenRead(packagePath);
        ZipArchiveEntry validationEntry = package.Entries.Single(
            entry => entry.FullName ==
                "build/OpenUsd.Runtime.Imaging.osx-arm64.native-validation.json");
        await using (Stream source = validationEntry.Open())
        await using (FileStream destination = File.Create(
            Path.Combine(artifactRoot, "macos-native-validation.json")))
        {
            await source.CopyToAsync(destination);
        }
        string validationPath = Path.Combine(
            artifactRoot,
            "macos-native-validation.json");
        await using FileStream validationStream = File.OpenRead(validationPath);
        string validationHash = Convert.ToHexString(
            await SHA256.HashDataAsync(validationStream));
        using JsonDocument validation = JsonDocument.Parse(
            await File.ReadAllBytesAsync(validationPath));
        string stormInstallName = validation.RootElement
            .GetProperty("libraries")
            .EnumerateArray()
            .Single(library => string.Equals(
                library.GetProperty("name").GetString(),
                "libopenusd_storm_child.dylib",
                StringComparison.Ordinal))
            .GetProperty("installName")
            .GetString()!;

        ZipArchiveEntry stormEntry = package.Entries.Single(
            entry => entry.FullName ==
                "runtimes/osx-arm64/native/libopenusd_storm_child.dylib");
        await using Stream stormStream = stormEntry.Open();
        string stormHash = Convert.ToHexString(await SHA256.HashDataAsync(stormStream));
        if (!string.Equals(
            stormHash,
            stormIdentity.PackageEntrySha256,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The evidenced Storm child package entry changed after identity validation.");
        }
        MacCodeSignEvidence signedStormChild = signingEvidence.Single(item =>
            string.Equals(
                Path.GetFileName(item.Path),
                "libopenusd_storm_child.dylib",
                StringComparison.Ordinal));
        var evidence = new
        {
            schemaVersion = 2,
            rid = "osx-arm64",
            package = Path.GetFileName(packagePath),
            packageSize = new FileInfo(packagePath).Length,
            packageSha256 = packageHash,
            nativeValidation = Path.GetFileName(validationPath),
            nativeValidationSha256 = validationHash,
            stormChild = new
            {
                path = stormEntry.FullName,
                size = stormEntry.Length,
                sha256 = stormHash,
                packageEntrySha256 = stormIdentity.PackageEntrySha256,
                nativeInstallSha256 = stormIdentity.NativeInstallSha256,
                publishedPreSignSha256 = stormIdentity.PublishedPreSignSha256,
                publishedPostSignSha256 = signedStormChild.Sha256,
                installName = stormInstallName,
            },
            appBaseCanonical = loadedImages.AppBaseCanonical,
            loadedImages = loadedImages.Paths
                .Select(path => new
                {
                    path,
                    underAppBase = IsMacLoadedImageUnderAppBase(
                        loadedImages.AppBaseCanonical + Path.DirectorySeparatorChar,
                        path),
                })
                .ToArray(),
            signatures = signingEvidence
                .Select(item => new
                {
                    path = item.Path,
                    sha256 = item.Sha256,
                    verified = item.Verified,
                    hardened = item.Hardened,
                })
                .ToArray(),
            execution = executionOutput
                .Split(
                    [Environment.NewLine],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith("PACKAGE_", StringComparison.Ordinal) ||
                    line.StartsWith("STORM_CHILD_", StringComparison.Ordinal) ||
                    line.StartsWith("DYLD_LIBRARY_PATH_", StringComparison.Ordinal) ||
                    line.StartsWith("PROJECT_OPENUSD_DYLD_", StringComparison.Ordinal) ||
                    line.StartsWith("OPENUSD_DYLD_", StringComparison.Ordinal) ||
                    line.StartsWith("METAL_PACKAGE_", StringComparison.Ordinal) ||
                    line.StartsWith("APP_BASE_", StringComparison.Ordinal) ||
                    line.StartsWith("CWD_IS_", StringComparison.Ordinal))
                .ToArray(),
        };
        await File.WriteAllTextAsync(
            Path.Combine(artifactRoot, "package-evidence.json"),
            JsonSerializer.Serialize(evidence, IndentedJsonOptions));
    }

    private static async Task<MacCodeSignEvidence[]> SignAndVerifyMacConsumerAsync(
        string publishRoot,
        string executablePath)
    {
        string[] libraryPaths = Directory
            .GetFiles(publishRoot, "*.dylib", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        string[] signedPaths = [.. libraryPaths, executablePath];
        var failures = new List<string>();
        foreach (string libraryPath in libraryPaths)
        {
            CommandResult signLibrary = await RunProcessAsync(
                "codesign",
                publishRoot,
                ["--force", "--sign", "-", "--options", "runtime", "--timestamp=none", libraryPath],
                nugetPackagesRoot: null,
                sanitizeRuntimeEnvironment: false,
                runtimeEnvironment: null);
            if (signLibrary.ExitCode != 0)
            {
                failures.Add($"codesign failed for {Path.GetFileName(libraryPath)}: {signLibrary.Output}");
            }
        }

        CommandResult signExecutable = await RunProcessAsync(
            "codesign",
            publishRoot,
            ["--force", "--sign", "-", "--options", "runtime", "--timestamp=none", executablePath],
            nugetPackagesRoot: null,
            sanitizeRuntimeEnvironment: false,
            runtimeEnvironment: null);
        if (signExecutable.ExitCode != 0)
        {
            failures.Add($"codesign failed for executable: {signExecutable.Output}");
        }

        var evidence = new List<MacCodeSignEvidence>();
        foreach (string signedPath in signedPaths)
        {
            CommandResult verify = await RunProcessAsync(
                "codesign",
                publishRoot,
                ["--verify", "--strict", "--verbose=4", signedPath],
                nugetPackagesRoot: null,
                sanitizeRuntimeEnvironment: false,
                runtimeEnvironment: null);
            CommandResult display = await RunProcessAsync(
                "codesign",
                publishRoot,
                ["--display", "--verbose=4", signedPath],
                nugetPackagesRoot: null,
                sanitizeRuntimeEnvironment: false,
                runtimeEnvironment: null);
            bool verified = verify.ExitCode == 0;
            bool hardened = display.ExitCode == 0 &&
                display.Output.Contains(
                    "runtime",
                    StringComparison.OrdinalIgnoreCase);
            string relativePath = Path.GetRelativePath(publishRoot, signedPath)
                .Replace('\\', '/');
            await using FileStream signedStream = File.OpenRead(signedPath);
            string signedHash = Convert.ToHexString(
                await SHA256.HashDataAsync(signedStream));
            evidence.Add(new MacCodeSignEvidence(
                relativePath,
                signedHash,
                verified,
                hardened));
            if (!verified)
            {
                failures.Add($"codesign --verify --strict failed for {relativePath}: {verify.Output}");
            }
            if (!hardened)
            {
                failures.Add($"Hardened runtime inspection failed for {relativePath}: {display.Output}");
            }
        }

        try
        {
            ValidateMacCodeSignEvidence(evidence, signedPaths.Length);
        }
        catch (InvalidOperationException exception)
        {
            failures.Add(exception.Message);
        }
        if (failures.Count != 0)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                failures.Distinct(StringComparer.Ordinal)));
        }
        return evidence.ToArray();
    }

    private static (string InstallRoot, string ShimRoot, string VulkanRuntimeLibrary)
        CreateSyntheticWindowsInstall(string workRoot)
    {
        string installRoot = Path.Combine(workRoot, "native", "install", "win-x64");
        string shimRoot = Path.Combine(workRoot, "native", "install", "shim", "win-x64");
        WriteTestFile(Path.Combine(installRoot, "bin", "MaterialXCore.dll"));
        WriteTestFile(Path.Combine(installRoot, "bin", "Imath-3_1.dll"));
        WriteTestFile(Path.Combine(installRoot, "bin", "OpenColorIO_2_2.dll"));
        WriteTestFile(Path.Combine(installRoot, "lib", "usd_ms.dll"));
        WriteTestFile(Path.Combine(installRoot, "lib", "usd", "plugInfo.json"), "{}");
        WriteTestFile(Path.Combine(installRoot, "lib", "usd", "usd", "resources", "plugInfo.json"), "{}");
        WriteTestFile(Path.Combine(installRoot, "plugin", "usd", "plugInfo.json"), "{}");
        WriteTestFile(Path.Combine(installRoot, "plugin", "usd", "hdStorm", "resources", "plugInfo.json"), "{}");
        WriteTestFile(Path.Combine(installRoot, "THIRD-PARTY.md"), "Synthetic test install.");
        WriteTestFile(
            Path.Combine(workRoot, "native", "install", "cesium", "win-x64", "THIRD-PARTY-CESIUM.md"),
            "Synthetic Cesium notices.");
        WriteTestFile(Path.Combine(shimRoot, "bin", "openusd_dotnet.dll"));
        WriteTestFile(Path.Combine(shimRoot, "bin", "openusd_hydra.dll"));
        WriteTestFile(Path.Combine(shimRoot, "bin", "openusd_hdsilk.dll"));
        WriteTestFile(Path.Combine(shimRoot, "bin", "openusd_storm_child.dll"));
        WriteTestFile(Path.Combine(shimRoot, "bin", "openusd_cesium.dll"));
        WriteTestFile(
            Path.Combine(shimRoot, "plugin", "usd", "hdSilk", "resources", "plugInfo.json"),
            CreateSyntheticHdSilkPlugInfo("../../../bin/openusd_hdsilk.dll"));
        string vulkanRuntimeLibrary = Path.Combine(
            workRoot,
            "native",
            "install",
            "vulkan-sdk-test",
            "bin",
            "vulkan-1.dll");
        WriteTestFile(vulkanRuntimeLibrary);
        return (installRoot, shimRoot, vulkanRuntimeLibrary);
    }

    /// <summary>
    /// Reproduces the physics shim prefix as the physics CMake preset actually leaves it.
    /// </summary>
    /// <remarks>
    /// The duplicated core and imaging shims and the staged GPU modules are the point of this
    /// helper, not incidental detail. The physics preset configures the whole native project, so
    /// its install prefix genuinely contains a second <c>openusd_dotnet</c>, <c>openusd_hydra</c>,
    /// <c>openusd_hdsilk</c> and <c>openusd_storm_child</c>, and the vcpkg port stages the NVIDIA
    /// GPU modules there for local runs. A physics package that swept up the former would mask the
    /// archive-verified binary at a consumer's application root; one that swept up the latter would
    /// redistribute proprietary binaries this project has no licence for. Staging both here is what
    /// makes the layout assertions able to see either happen.
    /// </remarks>
    private static (string PhysicsShimRoot, string PhysXInstallRoot) CreateSyntheticPhysicsInstall(
        string workRoot,
        string rid)
    {
        string physicsShimRoot = Path.Combine(
            workRoot,
            "native",
            "install",
            "shim",
            $"{rid}-physx");
        string physXInstallRoot = Path.Combine(workRoot, "native", "install", "physx", rid);
        string nativeDirectory = rid == "win-x64" ? "bin" : "lib";
        string prefix = rid == "win-x64" ? string.Empty : "lib";
        string extension = rid == "win-x64" ? ".dll" : ".so";

        WriteTestFile(
            Path.Combine(physicsShimRoot, nativeDirectory, $"{prefix}openusd_physx{extension}"),
            $"synthetic {rid} physics shim ABI v{RequiredPhysicsAbiVersion}");
        foreach (string masked in new[]
        {
            "openusd_dotnet",
            "openusd_hydra",
            "openusd_hdsilk",
            "openusd_storm_child",
        })
        {
            WriteTestFile(
                Path.Combine(physicsShimRoot, nativeDirectory, $"{prefix}{masked}{extension}"),
                $"stale duplicate of {masked} left by the physics preset");
        }

        if (rid == "win-x64")
        {
            WriteTestFile(
                Path.Combine(physicsShimRoot, nativeDirectory, "PhysXGpu_64.dll"),
                "synthetic NVIDIA proprietary GPU module");
            WriteTestFile(
                Path.Combine(physicsShimRoot, nativeDirectory, "PhysXDevice64.dll"),
                "synthetic NVIDIA proprietary device module");
        }
        else
        {
            WriteTestFile(
                Path.Combine(physicsShimRoot, nativeDirectory, "libPhysXGpu_64.so"),
                "synthetic NVIDIA proprietary GPU module");
        }

        WriteTestFile(
            Path.Combine(physXInstallRoot, "THIRD-PARTY-PHYSX.md"),
            "Synthetic PhysX notices.");
        return (physicsShimRoot, physXInstallRoot);
    }

    private static (string InstallRoot, string ShimRoot) CreateSyntheticUnixInstall(
        string workRoot,
        string rid)
    {
        int stormChildAbiVersion = ReadStormChildAbiVersion(FindRepositoryRoot());
        string installRoot = Path.Combine(workRoot, "native", "install", rid);
        string shimRoot = Path.Combine(workRoot, "native", "install", "shim", rid);
        string extension = rid == "linux-x64" ? ".so" : ".dylib";

        WriteTestFile(Path.Combine(installRoot, "lib", $"libImath-3_1{extension}"));
        WriteTestFile(Path.Combine(installRoot, "lib", $"libOpenColorIO{extension}"));
        WriteTestFile(Path.Combine(installRoot, "lib", $"libusd_ms{extension}"));
        WriteTestFile(Path.Combine(installRoot, "lib", "usd", "plugInfo.json"), "{}");
        WriteTestFile(Path.Combine(installRoot, "plugin", "usd", "plugInfo.json"), "{}");
        WriteTestFile(
            Path.Combine(installRoot, "plugin", "usd", "hdStorm", "resources", "plugInfo.json"),
            "{}");
        WriteTestFile(Path.Combine(installRoot, "THIRD-PARTY.md"), "Synthetic test install.");
        WriteTestFile(Path.Combine(shimRoot, "lib", $"libopenusd_dotnet{extension}"));
        WriteTestFile(Path.Combine(shimRoot, "lib", $"libopenusd_hydra{extension}"));
        WriteTestFile(Path.Combine(shimRoot, "lib", $"libopenusd_hdsilk{extension}"));
        WriteTestFile(Path.Combine(shimRoot, "lib", $"libopenusd_cesium{extension}"));
        if (rid == "linux-x64")
        {
            string stormChild = Path.Combine(
                shimRoot,
                "lib",
                "libopenusd_storm_child.so");
            string sonameStormChild = $"{stormChild}.{stormChildAbiVersion}";
            string versionedStormChild =
                $"{stormChild}.{stormChildAbiVersion}.0.0";
            WriteTestFile(
                versionedStormChild,
                $"synthetic Linux Storm child ABI v{stormChildAbiVersion} SONAME");
            if (OperatingSystem.IsLinux())
            {
                File.CreateSymbolicLink(stormChild, Path.GetFileName(sonameStormChild));
                File.CreateSymbolicLink(
                    sonameStormChild,
                    Path.GetFileName(versionedStormChild));
            }
            else
            {
                WriteTestFile(stormChild, Path.GetFileName(sonameStormChild));
                WriteTestFile(sonameStormChild, Path.GetFileName(versionedStormChild));
            }
        }
        else
        {
            WriteTestFile(
                Path.Combine(shimRoot, "lib", "libopenusd_storm_child.dylib"),
                $"synthetic macOS Storm child ABI v{stormChildAbiVersion}");
        }
        WriteTestFile(
            Path.Combine(shimRoot, "plugin", "usd", "hdSilk", "resources", "plugInfo.json"),
            CreateSyntheticHdSilkPlugInfo("installed-source-path"));
        return (installRoot, shimRoot);
    }

    private static (string InstallRoot, string ShimRoot, string VulkanRuntimeLibrary)
        CreateSyntheticUnixExecutionInstall(string workRoot, string rid)
    {
        (string installRoot, string shimRoot) = CreateSyntheticUnixInstall(workRoot, rid);
        return (installRoot, shimRoot, string.Empty);
    }

    private static string CreateSyntheticHdSilkPlugInfo(string libraryPath) =>
        $$"""
        {
          "Plugins": [
            {
              "Info": {
                "Types": {
                  "HdSilkRendererPlugin": {
                    "bases": [ "HdRendererPlugin" ]
                  }
                }
              },
              "LibraryPath": "{{libraryPath}}",
              "Name": "hdSilk",
              "ResourcePath": "resources",
              "Root": "..",
              "Type": "library"
            }
          ]
        }
        """;

    private static void WriteTestFile(string path, string content = "synthetic native test asset")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string CreateWorkRoot(string repositoryRoot)
    {
        string workRoot = Path.Combine(
            repositoryRoot,
            "artifacts",
            "package-tests",
            Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(workRoot);
        return workRoot;
    }

    private static bool TryGetExecutionInputs(
        string repositoryRoot,
        out NativeExecutionInputs inputs,
        out string reason)
    {
        if (!TryGetCurrentExecutionPlatform(out ExecutionPlatform platform, out reason))
        {
            inputs = default;
            return false;
        }

        string installRoot = Path.Combine(repositoryRoot, "native", "install", platform.Rid);
        string shimRoot = Path.Combine(
            repositoryRoot,
            "native",
            "install",
            "shim",
            platform.Rid);
        string cesiumShimRoot = Path.Combine(
            repositoryRoot,
            "native",
            "install",
            "shim",
            platform.Rid + "-cesium");
        string physicsShimRoot = Path.Combine(
            repositoryRoot,
            "native",
            "install",
            "shim",
            platform.Rid + "-physx");
        string physXInstallRoot = Path.Combine(
            repositoryRoot,
            "native",
            "install",
            "physx",
            platform.Rid);
        string nativeInstallRoot = Path.Combine(repositoryRoot, "native", "install");
        string[] vulkanRuntimeLibraries = platform.Rid == "win-x64" &&
            Directory.Exists(nativeInstallRoot)
            ? Directory.GetFiles(
                nativeInstallRoot,
                "vulkan-1.dll",
                SearchOption.AllDirectories)
            : [];

        if (!Directory.Exists(installRoot))
        {
            inputs = default;
            reason = $"the locked OpenUSD install is missing at '{installRoot}'";
            return false;
        }
        if (!Directory.Exists(shimRoot))
        {
            inputs = default;
            reason = $"the OpenUsd shim install is missing at '{shimRoot}'";
            return false;
        }
        if (platform.Rid == "win-x64" && vulkanRuntimeLibraries.Length != 1)
        {
            inputs = default;
            reason = "exactly one locked vulkan-1.dll is required";
            return false;
        }

        inputs = new NativeExecutionInputs(
            platform,
            installRoot,
            shimRoot,
            cesiumShimRoot,
            physicsShimRoot,
            physXInstallRoot,
            vulkanRuntimeLibraries.SingleOrDefault() ?? string.Empty);
        reason = string.Empty;
        return true;
    }

    private static async Task RequireMetalLibraryOnMacOSAsync(
        string repositoryRoot)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }
        string libraryPath = Path.Combine(
            repositoryRoot,
            "eng",
            "shaders",
            "checked",
            "mesh.metallib");
        string manifestPath = Path.Combine(
            repositoryRoot,
            "eng",
            "shaders",
            "checked",
            "mesh.metallib.manifest.json");
        if (!File.Exists(libraryPath) || !File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                "The macOS package execution gate requires the validated " +
                "ten-entry Metal library and manifest.");
        }
        await ValidateMetalSidecarAsync(repositoryRoot);
    }

    private static bool TryGetCurrentExecutionPlatform(
        out ExecutionPlatform platform,
        out string reason)
    {
        string? rid = null;
        if (OperatingSystem.IsWindows() &&
            RuntimeInformation.OSArchitecture == Architecture.X64)
        {
            rid = "win-x64";
        }
        else if (OperatingSystem.IsLinux() &&
            RuntimeInformation.OSArchitecture == Architecture.X64)
        {
            rid = "linux-x64";
        }
        else if (OperatingSystem.IsMacOS() &&
            RuntimeInformation.OSArchitecture == Architecture.Arm64)
        {
            rid = "osx-arm64";
        }

        ExecutionPlatform? current =
            SupportedExecutionPlatforms.SingleOrDefault(candidate => candidate.Rid == rid);
        if (current is null)
        {
            platform = null!;
            reason = (
                $"the host {RuntimeInformation.OSDescription} " +
                $"{RuntimeInformation.OSArchitecture} is not a supported package execution RID");
            return false;
        }

        platform = current;
        reason = string.Empty;
        return true;
    }

    private static void HandleMissingExecutionPrerequisites(string testName, string reason)
    {
        string message = $"{testName} did not run because {reason}.";
        if (string.Equals(
            Environment.GetEnvironmentVariable(RequiredExecutionEnvironmentVariable),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{message} {RequiredExecutionEnvironmentVariable}=true requires execution.");
        }

        Console.WriteLine($"PACKAGE_EXECUTION_PREREQUISITES_ABSENT: {message}");
    }

    /// <summary>
    /// Reports an execution prerequisite the host structurally cannot provide,
    /// without failing under <c>OPENUSD_PACKAGE_EXECUTION_REQUIRED</c>.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="HandleMissingExecutionPrerequisites"/>, which
    /// exists for prerequisites that ought to be there. This one is for the
    /// small set that a hosted runner cannot supply at all -- an OpenGL context
    /// for Storm being the only current case, the same limitation that keeps
    /// the Windows WGL leg of the render workflow red. Making that a hard
    /// failure would only mean the package jobs can never be green on hosted
    /// Windows; making it a silent skip would hide a real regression. So it
    /// prints a marker that a log search can find, and callers must keep every
    /// assertion that does not need the missing capability outside the skip.
    /// </remarks>
    private static void HandleUnavailableHostCapability(string testName, string capability, string detail)
    {
        Console.WriteLine(
            $"PACKAGE_EXECUTION_HOST_CAPABILITY_ABSENT: {testName} could not exercise " +
            $"{capability} on this host. {detail}");
    }

    /// <summary>
    /// Resolves a <c>native/</c> source path that
    /// <c>eng/native-install-metadata.ps1</c> hashes, by reading the script
    /// rather than restating the path here.
    /// </summary>
    /// <remarks>
    /// This test previously named <c>openusd_dotnet.cpp</c> directly. That file
    /// was split into per-area translation units, the producer was updated, and
    /// this consumer was not -- so the assertion pointed at a file that no
    /// longer existed. It survived because the package workflow is
    /// workflow_call only and had never run these gates, and because locally
    /// the surrounding test fails earlier on a stale install's lockSha256 and
    /// never reaches this line.
    ///
    /// Deriving the path removes the possibility of that drift. The property
    /// under test is unaffected: it is still that the recorded hash matches the
    /// file's current content. Whether the producer hashes the right file is
    /// already covered by NativeInstallMetadataSourceContractTests.
    /// </remarks>
    private static string ResolveMetadataHashedSource(string repositoryRoot, string variableName)
    {
        string script = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "native-install-metadata.ps1"));
        Match match = Regex.Match(
            script,
            @"\$" + Regex.Escape(variableName) + @"\s*=\s*Join-Path\s+\$repoRoot\s+'(?<path>native/[^']+)'",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"eng/native-install-metadata.ps1 no longer assigns ${variableName} from a " +
                "native/ path, so this test cannot resolve what the metadata hashes.");
        }

        return Path.Combine(
            repositoryRoot,
            match.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// Reports whether <c>eng/pack-packages.ps1</c> withholds a package id from
    /// every pack scope.
    /// </summary>
    /// <remarks>
    /// Read from the packer rather than restated, so that re-enabling a
    /// deferred package automatically makes its execution gate required again
    /// instead of needing a second edit that is easy to forget.
    /// </remarks>
    private static bool IsPublicationDeferred(string repositoryRoot, string packageId)
    {
        string script = File.ReadAllText(
            Path.Combine(repositoryRoot, "eng", "pack-packages.ps1"));
        Match block = Regex.Match(
            script,
            @"\$deferred\s*=\s*@\((?<body>[^)]*)\)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));
        return block.Success
            && Regex.IsMatch(
                block.Groups["body"].Value,
                @"'" + Regex.Escape(packageId) + @"'",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the OpenUsd repository root.");
    }

    private static int ReadStormChildAbiVersion(string repositoryRoot)
    {
        string headerPath = Path.Combine(
            repositoryRoot,
            "native",
            "openusd_storm_child",
            "include",
            "openusd_storm_child.h");
        string marker = File
            .ReadLines(headerPath)
            .Single(line => line.StartsWith(
                "#define OPENUSD_STORM_CHILD_ABI_VERSION ",
                StringComparison.Ordinal));
        string value = marker
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Last()
            .TrimEnd('u', 'U');
        int version = int.Parse(value, CultureInfo.InvariantCulture);
        if (version != RequiredStormChildAbiVersion)
        {
            throw new InvalidOperationException(
                $"Storm child ABI {version} does not match package ABI " +
                $"{RequiredStormChildAbiVersion}.");
        }
        return version;
    }

    private static async Task<CommandResult> RunDotnetAsync(
        string workingDirectory,
        string[] arguments,
        string? nugetPackagesRoot = null) =>
        await RunProcessAsync(
            "dotnet",
            workingDirectory,
            arguments,
            nugetPackagesRoot,
            sanitizeRuntimeEnvironment: false,
            runtimeEnvironment: null);

    private static async Task<CommandResult> RunExecutableAsync(
        string executablePath,
        string workingDirectory,
        string[] arguments,
        IReadOnlyDictionary<string, string>? runtimeEnvironment = null) =>
        await RunProcessAsync(
            executablePath,
            workingDirectory,
            arguments,
            nugetPackagesRoot: null,
            sanitizeRuntimeEnvironment: true,
            runtimeEnvironment);

    private static async Task<CommandResult> RunProcessAsync(
        string fileName,
        string workingDirectory,
        string[] arguments,
        string? nugetPackagesRoot,
        bool sanitizeRuntimeEnvironment,
        IReadOnlyDictionary<string, string>? runtimeEnvironment)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        if (nugetPackagesRoot is not null)
        {
            process.StartInfo.Environment["NUGET_PACKAGES"] = nugetPackagesRoot;
        }
        if (sanitizeRuntimeEnvironment)
        {
            var searchDirectories = new List<string> { workingDirectory };
            if (OperatingSystem.IsWindows())
            {
                searchDirectories.Add(
                    Environment.GetFolderPath(Environment.SpecialFolder.System));
                searchDirectories.Add(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            }
            process.StartInfo.Environment["PATH"] = string.Join(
                Path.PathSeparator,
                searchDirectories.Where(path => !string.IsNullOrWhiteSpace(path)));
            process.StartInfo.Environment.Remove("OPENUSD_PLUGIN_PATH");
            process.StartInfo.Environment.Remove("PXR_PLUGINPATH_NAME");
            process.StartInfo.Environment.Remove("DYLD_LIBRARY_PATH");
            process.StartInfo.Environment.Remove("LD_LIBRARY_PATH");
            process.StartInfo.Environment.Remove("LD_PRELOAD");
            process.StartInfo.Environment.Remove("VK_DRIVER_FILES");
            process.StartInfo.Environment.Remove("VK_ICD_FILENAMES");
            if (runtimeEnvironment is not null)
            {
                foreach ((string name, string value) in runtimeEnvironment)
                {
                    process.StartInfo.Environment[name] = value;
                }
            }
        }

        process.Start();
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        string output = $"{await standardOutput}{Environment.NewLine}{await standardError}";
        return new CommandResult(process.ExitCode, output);
    }

    private sealed record CommandResult(int ExitCode, string Output);

    private sealed record PackedPackage(string Path, string Version);

    private sealed record ExecutionConsumer(string ProjectPath, string PublishRoot, string AssetsPath);

    private sealed record MacCodeSignEvidence(
        string Path,
        string Sha256,
        bool Verified,
        bool Hardened);

    private sealed record MacStormChildIdentity(
        string PackageEntrySha256,
        string NativeInstallSha256,
        string PublishedPreSignSha256);

    private sealed record MacLoadedImageValidation(
        string AppBaseCanonical,
        bool Confined,
        string[] Paths,
        bool StormLoaded,
        bool CoreLoaded,
        bool DotNetLoaded,
        bool ExecutableLoaded);

    private sealed record ExecutionPlatform(
        string Rid,
        string BackendPackageId,
        string BackendNamespace,
        string DeviceFactory,
        string BackendEnum,
        string BackendDisplayName,
        string UploadMarker,
        string OperatingSystemGuard,
        string DotnetLibrary,
        string OpenUsdLibrary,
        string HydraLibrary,
        string HdSilkLibrary,
        string StormChildLibrary,
        string HdSilkPluginLibraryPath,
        bool RequiresSwiftShader);

    private readonly record struct NativeExecutionInputs(
        ExecutionPlatform Platform,
        string InstallRoot,
        string ShimRoot,
        string CesiumShimRoot,
        string PhysicsShimRoot,
        string PhysXInstallRoot,
        string VulkanRuntimeLibrary);
}
