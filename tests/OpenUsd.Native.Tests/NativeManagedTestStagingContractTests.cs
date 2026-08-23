// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace OpenUsd.Native.Tests;

/// <summary>
/// Requires the native-backed managed test runner to detect a staged physics runtime on every
/// platform rather than only where the shim happens to install into <c>bin</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>eng/run-native-managed-tests.ps1</c> turns a staged physics shim into a contract by setting
/// <c>OPENUSD_REQUIRE_NATIVE_PHYSICS</c>, which is what makes the native-backed physics suite fail
/// loudly instead of skipping when the runtime it was told to exercise cannot be loaded.
/// </para>
/// <para>
/// The detection used to scan only the staged <c>bin</c> directory. CMake installs
/// <c>openusd_physx</c> as a runtime artifact on Windows, which lands in <c>bin</c>, but as a
/// library artifact on Linux and macOS, which lands in <c>lib</c>. On those platforms the scan found
/// nothing, the requirement was cleared, and the whole physics suite silently skipped while
/// reporting success. Nothing else can see that: the run is green and the skip reason reads like a
/// legitimately unstaged host.
/// </para>
/// <para>
/// The optional CUDA modules are loaded late and by name from the directory the physics runtime
/// lives in, so the same platform split decides where they have to be staged. This pins that they
/// are placed beside the runtime rather than left wherever the install layout put them.
/// </para>
/// </remarks>
public sealed class NativeManagedTestStagingContractTests
{
    private const string Script = "eng/run-native-managed-tests.ps1";

    [Test]
    public async Task TheStagedPhysicsScanCoversBothInstallLayouts()
    {
        string script = await ReadScriptAsync();
        Match scan = Regex.Match(
            script,
            @"\$stagedPhysicsFiles\s*=\s*@\((?<body>[\s\S]{0,400}?)\)\r?\n",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        await Assert.That(scan.Success)
            .IsTrue()
            .Because($"{Script} must resolve the staged physics runtime files in one place");

        string body = scan.Groups["body"].Value;
        await Assert.That(body)
            .Contains("$binTarget")
            .Because("a Windows install stages the physics shim as a runtime artifact into bin");
        await Assert.That(body)
            .Contains("$libTarget")
            .Because("a Linux or macOS install stages the same shim as a library artifact into lib");
        await Assert.That(body).Contains("$physicsModulePattern");
        await Assert.That(script).Contains(@"$physicsModulePattern = '^(lib)?openusd_physx\.(dll|dylib|so)'");
    }

    [Test]
    public async Task TheRequirementIsDerivedFromThatScan()
    {
        string script = await ReadScriptAsync();

        await Assert.That(script).Contains("$stagedPhysics = $stagedPhysicsFiles.Count -gt 0");
        await Assert.That(script)
            .Contains("$env:OPENUSD_REQUIRE_NATIVE_PHYSICS = if ($stagedPhysics) { '1' } else { '0' }")
            .Because("a staged runtime must turn the physics suite into a requirement, not a hint");
    }

    [Test]
    public async Task TheCudaModulesAreStagedBesideTheRuntime()
    {
        string script = await ReadScriptAsync();

        await Assert.That(script)
            .Contains("PhysX(Gpu|Device)")
            .Because("the optional CUDA modules must be recognized by name");
        await Assert.That(script)
            .Contains("Copy-Item -Destination $physicsHome -Force")
            .Because("the CUDA modules are loaded from the directory the physics runtime lives in");
    }

    [Test]
    public async Task TheCudaModulesInstallBesideTheRuntimeOnEveryPlatform()
    {
        string root = FindRepositoryRoot();
        string path = Path.Combine(
            root,
            "native".Replace('/', Path.DirectorySeparatorChar),
            "openusd_physx",
            "CMakeLists.txt");
        await Assert.That(File.Exists(path)).IsTrue();

        string project = await File.ReadAllTextAsync(path);

        // A runtime artifact lands in the binary directory and a library artifact
        // lands in the library directory, so the module destination has to follow
        // the shim rather than be fixed to one of them.
        await Assert.That(project).Contains("if(WIN32)");
        await Assert.That(project).Contains("set(openusd_physx_gpu_destination ${CMAKE_INSTALL_BINDIR})");
        await Assert.That(project).Contains("set(openusd_physx_gpu_destination ${CMAKE_INSTALL_LIBDIR})");
        await Assert.That(project).Contains("DESTINATION ${openusd_physx_gpu_destination}");
    }

    private static async Task<string> ReadScriptAsync()
    {
        string root = FindRepositoryRoot();
        string path = Path.Combine(root, Script.Replace('/', Path.DirectorySeparatorChar));
        await Assert.That(File.Exists(path))
            .IsTrue()
            .Because($"{Script} must exist for this contract to mean anything");
        return await File.ReadAllTextAsync(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root could not be located.");
    }
}
