// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace OpenUsd.Native.Tests;

/// <summary>
/// Guards structural properties of the workflow files that nothing else can
/// see until a release run reaches the job in question.
/// </summary>
/// <remarks>
/// Shipping 0.5.0-alpha took six release runs. Four of the five failures were
/// the first time the failing gate had ever executed, because
/// <c>package.yml</c> and <c>release.yml</c> only run from a release and a
/// release is the most expensive way to discover anything: each attempt costs
/// hours, and the fix cannot be verified except by another release.
///
/// Two of those failures are structural rather than behavioural, so they can be
/// caught here in milliseconds:
///
/// The publish job gained a step that reads <c>eng/pack-packages.ps1</c>, and
/// that job had never needed a checkout because publishing consumes only
/// artifacts and credentials. It failed with "The term './eng/pack-packages.ps1'
/// is not recognized" after every artifact in the release had already been
/// built. actionlint does not model this, and no amount of local testing would,
/// because locally the file is always present.
///
/// And the package execution gates ran nowhere but a release, which is what
/// made the other three defects expensive. That is now a
/// <c>workflow_run</c> trigger, and this pins it so it cannot quietly revert to
/// release-only.
///
/// Both properties are read out of the workflow files rather than restated, so
/// this cannot pass against a stale copy of the expectation.
/// </remarks>
public sealed class WorkflowStructureContractTests
{
    /// <summary>Matches a job header: two spaces, a name, a colon, nothing else.</summary>
    private static readonly Regex JobHeader = new(
        @"^  (?<name>[A-Za-z0-9_-]+):\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Matches an invocation of a repository script from a run block.</summary>
    private static readonly Regex RepositoryScript = new(
        @"(\./)?eng/[A-Za-z0-9._/-]+\.(ps1|py|sh)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Test]
    public async Task EveryJobRunningARepositoryScriptChecksOutTheRepository()
    {
        string root = FindRepositoryRoot();
        List<string> offenders = [];
        int scriptJobs = 0;

        foreach (string workflow in Directory.EnumerateFiles(
            Path.Combine(root, ".github", "workflows"),
            "*.yml"))
        {
            string name = Path.GetFileName(workflow);
            foreach ((string job, string body) in ReadJobs(
                await File.ReadAllTextAsync(workflow)))
            {
                // Jobs that delegate to a reusable workflow have no steps of
                // their own, so the checkout belongs to the callee.
                if (!body.Contains("steps:", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!RepositoryScript.IsMatch(body))
                {
                    continue;
                }

                scriptJobs++;
                if (!body.Contains("actions/checkout@", StringComparison.Ordinal))
                {
                    offenders.Add($"{name}:{job}");
                }
            }
        }

        // Non-vacuity: a parser that stops recognising jobs would report no
        // offenders while checking nothing at all.
        await Assert.That(scriptJobs)
            .IsGreaterThan(4)
            .Because("the workflows must still be parseable into jobs that run eng/ scripts");
        await Assert.That(offenders)
            .IsEmpty()
            .Because(
                "these jobs invoke a repository script without checking the " +
                "repository out, which fails only when the job actually runs: " +
                string.Join(", ", offenders));
    }

    [Test]
    public async Task PackageGatesRunOutsideARelease()
    {
        string root = FindRepositoryRoot();
        string package = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "package.yml"));
        string native = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "native.yml"));

        string triggers = ReadTriggerBlock(package);
        await Assert.That(triggers).IsNotEmpty();

        // workflow_call and workflow_dispatch both require someone to ask. Only
        // a self-firing trigger makes these gates run without a release.
        bool selfFiring =
            triggers.Contains("workflow_run:", StringComparison.Ordinal) ||
            Regex.IsMatch(triggers, @"^\s{2}push:", RegexOptions.Multiline) ||
            Regex.IsMatch(triggers, @"^\s{2}pull_request:", RegexOptions.Multiline);

        await Assert.That(selfFiring)
            .IsTrue()
            .Because(
                "package.yml carries the package-only execution gates; with only " +
                "workflow_call and workflow_dispatch they run once per release, " +
                "which is how three of them shipped having never executed");

        // The workflow_run form names its upstream workflow by title, and a
        // renamed upstream silently never fires.
        Match named = Regex.Match(
            triggers,
            @"workflows:\s*\[\s*'(?<title>[^']+)'\s*\]",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));
        if (named.Success)
        {
            Match upstream = Regex.Match(
                native,
                @"^name:\s*(?<title>.+?)\s*$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5));
            await Assert.That(upstream.Success).IsTrue();
            await Assert.That(named.Groups["title"].Value)
                .IsEqualTo(upstream.Groups["title"].Value)
                .Because(
                    "package.yml triggers on the native pipeline by title, and a " +
                    "title that no longer matches never fires and never reports why");
        }
    }

    [Test]
    public async Task NativeWorkflowPathFiltersExcludeValidationOnlyInputs()
    {
        string root = FindRepositoryRoot();
        string native = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "native.yml"));
        string ci = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "ci.yml"));
        string triggers = ReadTriggerBlock(native);
        await Assert.That(triggers).IsNotEmpty();

        foreach (string path in new[]
        {
            "'eng/test-linux-native-prerequisites.ps1'",
            "'eng/test-render-native-archive.ps1'",
            "'eng/run-native-probe.ps1'",
            "'eng/run-silk-probe.ps1'",
        })
        {
            await Assert.That(triggers)
                .DoesNotContain(path, StringComparison.Ordinal)
                .Because(
                    $"{path} is exercised by a cheaper self-firing workflow " +
                    "or is a workflow contract test, so it must not starve " +
                    "the serialized native archive queue");
        }

        foreach (string path in new[]
        {
            "'eng/openusd.lock.json'",
            "'eng/openusd.install.lock.json'",
            "'eng/fetch-native.ps1'",
            "'eng/build-native.ps1'",
            "'eng/build-vulkan-sdk.ps1'",
            "'eng/check-linux-native-prerequisites.ps1'",
            "'eng/native-install-metadata.ps1'",
            "'eng/prepare-render-native.ps1'",
            "'eng/create-native-archive.ps1'",
            "'eng/run-native-fuzz.ps1'",
            "'eng/native-fuzz-lsan.supp'",
            "'eng/physx.lock.json'",
            "'eng/fetch-physx-native.ps1'",
            "'eng/build-physx-native.ps1'",
            "'native/**'",
            "'test-assets/fuzz-seeds/**'",
        })
        {
            await Assert.That(triggers)
                .Contains(path, StringComparison.Ordinal)
                .Because(
                    $"{path} can change archive bytes, archive-only validation " +
                    "that no other workflow exercises, the archive sidecar, " +
                    "or the cache key that downstream workflows restore");
        }

        await Assert.That(native)
            .Contains("./eng/test-linux-native-prerequisites.ps1", StringComparison.Ordinal)
            .Because("the Linux prerequisite contract still has to run when native.yml runs");
        await Assert.That(native)
            .Contains("./eng/check-linux-native-prerequisites.ps1", StringComparison.Ordinal)
            .Because("the Linux prerequisite preflight still has to run when native.yml runs");

        string ciBuild = ReadJob(ci, "build-test");
        foreach (string script in new[]
        {
            "./eng/test-linux-native-prerequisites.ps1",
            "./eng/test-render-native-archive.ps1",
        })
        {
            await Assert.That(ciBuild)
                .Contains(script, StringComparison.Ordinal)
                .Because(
                    $"{script} no longer triggers native.yml, so ordinary " +
                    "push CI must execute it before release-only workflows do");
        }
    }

    [Test]
    public async Task ConsumerCheckoutReportsSurviveATagRelease()
    {
        string root = FindRepositoryRoot();
        foreach (string workflow in new[] { "package.yml", "viewer-distribution.yml" })
        {
            string text = await File.ReadAllTextAsync(
                Path.Combine(root, ".github", "workflows", workflow));

            await Assert.That(text)
                .DoesNotContain("git rev-parse \"origin/$branch\"", StringComparison.Ordinal)
                .Because(
                    "a tag release sets ref_name to the tag, so origin/<ref> does not resolve; " +
                    "plain git rev-parse echoes the argument and exits 128, which pwsh " +
                    "propagates and fails the step");
            await Assert.That(text)
                .Contains(
                    "git rev-parse --verify --quiet \"refs/remotes/origin/$branch\"",
                    StringComparison.Ordinal)
                .Because("the resolution must fail quietly so the fallback SHA can be used");
            await Assert.That(text)
                .Contains("$global:LASTEXITCODE = 0", StringComparison.Ordinal)
                .Because(
                    "release run 31250563169 lost all three package jobs because the failed " +
                    "rev-parse left a non-zero exit code that the pwsh shell propagated");
        }
    }

    [Test]
    public async Task NarrowOpenUsdInstallCacheYieldsToTheFullNativeCache()
    {
        string root = FindRepositoryRoot();
        string native = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "native.yml"));

        int narrow = native.IndexOf(
            "openusd-install-linux-x64-",
            StringComparison.Ordinal);
        await Assert.That(narrow)
            .IsGreaterThan(0)
            .Because("native.yml must still save the narrow OpenUSD install cache for ci.yml");

        string guard = native[..narrow];
        int step = guard.LastIndexOf("- uses: actions/cache@", StringComparison.Ordinal);
        await Assert.That(step)
            .IsGreaterThan(0)
            .Because("the narrow key must belong to a cache step");

        await Assert.That(guard[step..])
            .Contains("steps.native-cache.outputs.cache-hit != 'true'", StringComparison.Ordinal)
            .Because(
                "both caches write native/install/linux-x64 and the narrow key omits the " +
                "shim headers, so restoring it after a native-cache hit overwrites the " +
                "install metadata sidecar with an older ABI and fails verification, which " +
                "is how release run 31249949333 died");
    }

    [Test]
    public async Task PackageWorkflowRestoresTheCacheTheNativePipelineSaves()
    {
        string root = FindRepositoryRoot();
        string package = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "package.yml"));
        string native = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "native.yml"));

        const string prefix = "native-${{ matrix.rid }}-";
        IReadOnlyList<string> saved = HashFileInputs(native, prefix);
        IReadOnlyList<string> restored = HashFileInputs(package, prefix);

        // Non-vacuity: two empty lists compare equal and would prove nothing.
        await Assert.That(saved.Count)
            .IsGreaterThan(20)
            .Because("native.yml must still key its install cache on hashFiles");
        await Assert.That(restored.Count)
            .IsGreaterThan(20)
            .Because("package.yml must still restore that cache rather than rebuilding");
        await Assert.That(restored)
            .IsEquivalentTo(saved)
            .Because(
                "hashFiles over a different file list yields a different digest, " +
                "so the restore would silently never hit and every packaging push " +
                "would rebuild OpenUSD from source on all three RIDs");

        // The producer saves; the consumer must not, or it can write a smaller
        // archive under the producer's key. actions/cache never overwrites, so
        // native.yml would restore a partial tree from then on.
        await Assert.That(package)
            .Contains("actions/cache/restore@", StringComparison.Ordinal)
            .Because("package.yml consumes the native install and must not save it");
    }

    [Test]
    public async Task PackageWorkflowDefersOnlySelfFiringCacheMisses()
    {
        string root = FindRepositoryRoot();
        string package = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "package.yml"));
        string packageExecution = ReadJob(package, "package-execution");

        await Assert.That(packageExecution)
            .Contains("ready: ${{ steps.native-ready.outputs.ready }}", StringComparison.Ordinal)
            .Because("dependent jobs need a job output that can skip loudly after a deferred native miss");
        await Assert.That(packageExecution)
            .Contains("DEFER_ON_NATIVE_CACHE_MISS:", StringComparison.Ordinal)
            .Because("the cache-miss deferral must be an explicit event-mode decision");
        await Assert.That(packageExecution)
            .Contains("github.event_name == 'push' || github.event_name == 'pull_request'", StringComparison.Ordinal)
            .Because(
                "only self-firing push and pull_request runs may defer; " +
                "release and dispatch runs must not skip gates");
        await Assert.That(packageExecution)
            .Contains("PACKAGE_SMOKE_DEFERRED", StringComparison.Ordinal)
            .Because("a deferred package smoke must leave a searchable notice rather than a silent skip");
        await Assert.That(packageExecution)
            .Contains("workflow_call and workflow_dispatch keep building from source", StringComparison.Ordinal)
            .Because("the release path calls this workflow and must never silently defer package gates");

        string fetchStep = ReadStep(packageExecution, "Fetch locked native sources");
        string buildStep = ReadStep(packageExecution, "Build locked native install");
        await Assert.That(fetchStep)
            .Contains("env.DEFER_ON_NATIVE_CACHE_MISS != 'true'", StringComparison.Ordinal)
            .Because("push and pull_request cache misses must not fetch native sources that will be thrown away");
        await Assert.That(buildStep)
            .Contains("env.DEFER_ON_NATIVE_CACHE_MISS != 'true'", StringComparison.Ordinal)
            .Because("push and pull_request cache misses must not rebuild OpenUSD in the consumer workflow");

        foreach (string step in new[]
        {
            "Download verified native pipeline archive",
            "Extract immutable Windows native archive",
            "Extract immutable Linux native archive",
            "Extract immutable macOS native archive",
            "Verify native install metadata",
            "Build Cesium native install",
            "Build Cesium shim",
            "Execute managed NativeAOT probe",
            "Execute hdSilk NativeAOT probe",
            "Build package tests",
            "Verify Metal package staging",
            "Run required package execution gates",
            "Require Linux ABI-7 SONAME topology and package-only evidence",
            "Require macOS signed Storm child package-only evidence",
        })
        {
            await Assert.That(ReadStep(packageExecution, step))
                .Contains("steps.native-ready.outputs.ready == 'true'", StringComparison.Ordinal)
                .Because($"{step} needs the native install and must skip when the smoke was deferred");
        }
    }

    [Test]
    public async Task RenderWorkflowBuildsCesiumShimOnlyForFullPackageGates()
    {
        string root = FindRepositoryRoot();
        string render = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "render.yml"));

        string macos = ReadJob(render, "macos-arm64");
        string linux = ReadJob(render, "linux-presentation");

        await Assert.That(macos)
            .Contains("OPENUSD_PACKAGE_EXECUTION_REQUIRED: 'true'", StringComparison.Ordinal)
            .Because("the macOS render job executes the full package suite under the required-execution gate");
        await Assert.That(macos)
            .Contains("--minimum-expected-tests 22", StringComparison.Ordinal)
            .Because("the macOS render job runs the full package suite, including Cesium package execution");
        await Assert.That(ReadStep(macos, "Build Cesium native install"))
            .Contains("./eng/build-cesium-native.ps1 -Rid osx-arm64 -SkipSmokeProbe", StringComparison.Ordinal)
            .Because(
                "release run 31251906449 failed when the macOS package gate could not find " +
                "libopenusd_cesium.dylib");
        await Assert.That(ReadStep(macos, "Build Cesium shim"))
            .Contains("./eng/build-cesium-shim.ps1 -Rid osx-arm64", StringComparison.Ordinal)
            .Because("the package tests require the runtime shim, not only the Cesium native install");
        await Assert.That(macos)
            .Contains("cesium-vcpkg-osx-arm64-${{ hashFiles(", StringComparison.Ordinal)
            .Because("the expensive Cesium vcpkg graph must be cached on the same inputs as package.yml");
        await Assert.That(macos)
            .Contains("eng/build-cesium-shim.ps1", StringComparison.Ordinal)
            .Because("the cache key must change when the shim build changes");

        await Assert.That(linux)
            .Contains("OPENUSD_PACKAGE_EXECUTION_REQUIRED: 'true'", StringComparison.Ordinal)
            .Because("the Linux render job still runs package-test executable gates");
        await Assert.That(linux)
            .DoesNotContain("--minimum-expected-tests 22", StringComparison.Ordinal)
            .Because("Linux render intentionally runs two filtered non-Cesium package gates, not the full suite");
        await Assert.That(ReadStep(linux, "Build Cesium shim"))
            .IsEmpty()
            .Because("the Linux render job must not spend a half-hour building Cesium for filtered non-Cesium gates");
    }

    [Test]
    public async Task CesiumNativeBuildConfiguresPositionIndependentCode()
    {
        string root = FindRepositoryRoot();
        string buildCesiumNative = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "build-cesium-native.ps1"));

        await Assert.That(buildCesiumNative)
            .Contains("-DCMAKE_POSITION_INDEPENDENT_CODE=ON", StringComparison.Ordinal)
            .Because(
                "the Linux package-only NativeAOT gate links cesium-native static " +
                "archives into libopenusd_cesium.so, and non-PIC thread_local " +
                "statics can emit executable-only R_X86_64_TPOFF32 relocations");
    }

    [Test]
    public async Task ConsumerWorkflowsAnnounceValidatedCommitAndStaleWorkflowRunCheckouts()
    {
        string root = FindRepositoryRoot();
        foreach (string workflowName in new[] { "package.yml", "viewer-distribution.yml" })
        {
            string workflow = await File.ReadAllTextAsync(
                Path.Combine(root, ".github", "workflows", workflowName));

            await Assert.That(workflow)
                .Contains("WORKFLOW_CHECKOUT_VALIDATION", StringComparison.Ordinal)
                .Because($"{workflowName} must say which commit its gates validate");
            await Assert.That(workflow)
                .Contains("WORKFLOW_CHECKOUT_STALE", StringComparison.Ordinal)
                .Because($"{workflowName} must warn when workflow_run validates a commit behind the branch head");
            await Assert.That(workflow)
                .Contains(
                    "this is the native artifact pipeline commit and can lag behind the branch head",
                    StringComparison.Ordinal)
                .Because(
                    $"{workflowName} workflow_run failures must not be " +
                    "mistaken for regressions at current branch head");
            await Assert.That(workflow)
                .Contains(
                    "git rev-parse --verify --quiet \"refs/remotes/origin/$branch\"",
                    StringComparison.Ordinal)
                .Because($"{workflowName} must compare the validated checkout with the current branch head");
        }
    }

    [Test]
    public async Task ViewerDistributionRunsOutsideAReleaseOnEverySupportedRid()
    {
        string root = FindRepositoryRoot();
        string release = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "release.yml"));
        string viewer = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "viewer-distribution.yml"));
        string native = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "native.yml"));

        string releaseViewerJob = ReadJob(release, "viewer-distribution");
        await Assert.That(releaseViewerJob)
            .Contains("if: startsWith(github.ref, 'refs/tags/v')", StringComparison.Ordinal)
            .Because("the release bundle job must stay tag-gated and keep consuming release pack artifacts");
        await Assert.That(releaseViewerJob)
            .Contains("needs: pack", StringComparison.Ordinal)
            .Because("the release bundle job must continue smoking the exact packages produced by release pack");

        string triggers = ReadTriggerBlock(viewer);
        await Assert.That(triggers).IsNotEmpty();
        await Assert.That(triggers)
            .Contains("workflow_run:", StringComparison.Ordinal)
            .Because("native changes must smoke the Viewer bundle after the native archive is available");
        await Assert.That(triggers)
            .Contains("push:", StringComparison.Ordinal)
            .Because("Viewer bundle script changes must run without a tag");
        await Assert.That(triggers)
            .Contains("pull_request:", StringComparison.Ordinal)
            .Because("Viewer bundle changes must be smoke-tested before merge");

        // A trigger that exists but names a branch nothing pushes to is
        // indistinguishable from no trigger, and nothing else would report it.
        foreach (string branch in new[] { "master", "main" })
        {
            await Assert.That(triggers)
                .Contains(branch, StringComparison.Ordinal)
                .Because(
                    $"the push and pull_request triggers must cover '{branch}', or the " +
                    "workflow silently never runs while still appearing to be wired");
        }

        foreach (string path in new[]
        {
            "'.github/workflows/viewer-distribution.yml'",
            "'eng/publish-viewer-bundle.ps1'",
            "'eng/test-viewer-bundle-smoke.ps1'",
            "'src/OpenUsd.Viewer/**'",
            "'src/OpenUsd.Viewer.App/**'",
        })
        {
            await Assert.That(triggers)
                .Contains(path, StringComparison.Ordinal)
                .Because($"{path} changes the Viewer bundle and must trigger a non-release smoke");
        }

        string viewerSmokeJob = ReadJob(viewer, "viewer-distribution");
        foreach (string rid in new[] { "win-x64", "linux-x64", "osx-arm64" })
        {
            await Assert.That(viewerSmokeJob)
                .Contains($"rid: {rid}", StringComparison.Ordinal)
                .Because("the Viewer distribution smoke must run on every RID shipped in a release");
        }

        await Assert.That(viewerSmokeJob)
            .Contains("Download packed Viewer inputs", StringComparison.Ordinal)
            .Because("the non-release smoke must build from freshly packed local inputs, not nuget.org");
        await Assert.That(viewerSmokeJob)
            .Contains("Smoke the installed Viewer bundle", StringComparison.Ordinal)
            .Because("building the archive without executing it would not catch loader and renderer failures");
        await Assert.That(viewerSmokeJob)
            .Contains("Smoke the installed Linux Viewer bundle", StringComparison.Ordinal)
            .Because("the Linux smoke needs the Xvfb leg that catches GL loader regressions");

        const string prefix = "native-${{ matrix.rid }}-";
        IReadOnlyList<string> saved = HashFileInputs(native, prefix);
        IReadOnlyList<string> restored = HashFileInputs(viewer, prefix);

        await Assert.That(restored.Count)
            .IsGreaterThan(20)
            .Because("the push smoke must restore native.yml's install cache rather than rebuilding");
        await Assert.That(restored)
            .IsEquivalentTo(saved)
            .Because("a different hashFiles list silently changes the native cache key");
        await Assert.That(viewer)
            .Contains("actions/cache/restore@", StringComparison.Ordinal)
            .Because("viewer-distribution.yml consumes the native install and must not save it");
    }

    [Test]
    public async Task ViewerDistributionDoesNotCancelSmokeEvidenceAcrossMainPushes()
    {
        string root = FindRepositoryRoot();
        string viewer = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "viewer-distribution.yml"));

        string workflowHeader = viewer[..viewer.IndexOf("jobs:", StringComparison.Ordinal)];
        await Assert.That(workflowHeader)
            .DoesNotContain("cancel-in-progress: true", StringComparison.Ordinal)
            .Because(
                "runs 31251898756, 31249941766, 31248387006, 31245824359, " +
                "31215759239, and 31210681699 showed workflow-level cancellation " +
                "systematically kills the slow osx-arm64 smoke before it reports evidence");

        string packJob = ReadJob(viewer, "pack-viewer-inputs");
        await Assert.That(packJob)
            .Contains("concurrency:", StringComparison.Ordinal)
            .Because("only the cheap pack job should be superseded by newer pushes");
        await Assert.That(packJob)
            .Contains("viewer-distribution-pack-${{ github.ref }}-${{ matrix.rid }}", StringComparison.Ordinal)
            .Because("pack cancellation should be per RID and should not share a group with smoke jobs");
        await Assert.That(packJob)
            .Contains("cancel-in-progress: true", StringComparison.Ordinal)
            .Because("the cheap pack work may still be cancelled when a newer commit supersedes it");

        string viewerSmokeJob = ReadJob(viewer, "viewer-distribution");
        await Assert.That(viewerSmokeJob)
            .DoesNotContain("cancel-in-progress: true", StringComparison.Ordinal)
            .Because("a started smoke job is the evidence producer and must survive later pushes");
        await Assert.That(viewerSmokeJob)
            .DoesNotContain("viewer-distribution-${{ github.ref }}", StringComparison.Ordinal)
            .Because("a ref-wide smoke concurrency group lets every main push kill the pending macOS leg");
    }

    [Test]
    public async Task ViewerBundleSmokeCapturesNativeCrashDiagnostics()
    {
        string root = FindRepositoryRoot();
        string smoke = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "test-viewer-bundle-smoke.ps1"));

        await Assert.That(smoke)
            .Contains("DOTNET_DbgEnableMiniDump", StringComparison.Ordinal)
            .Because("a Unix SIGSEGV can bypass managed stderr and Avalonia tracing");
        await Assert.That(smoke)
            .Contains("COMPlus_DbgEnableMiniDump", StringComparison.Ordinal)
            .Because("older runtime aliases should produce the same dump artifact");
        await Assert.That(smoke)
            .Contains("Library/Logs/DiagnosticReports", StringComparison.Ordinal)
            .Because("macOS writes native crash reports outside the bundle directory");
        await Assert.That(smoke)
            .Contains("viewer macOS crash reports", StringComparison.Ordinal)
            .Because("the CI log must include the report text, not only upload it");
        await Assert.That(smoke)
            .Contains("Copy-MacOSCrashReports -SinceUtc $processStartUtc", StringComparison.Ordinal);
    }

    [Test]
    public async Task ViewerBundleSmokeCapturesLiveHangDiagnosticsBeforeKilling()
    {
        string root = FindRepositoryRoot();
        string smoke = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "test-viewer-bundle-smoke.ps1"));

        await Assert.That(smoke)
            .Contains("function Capture-HangDiagnostics", StringComparison.Ordinal)
            .Because("run 31245824359 proved the Linux Viewer smoke can hang without crashing");
        await Assert.That(smoke)
            .Contains("dotnet-stack", StringComparison.Ordinal)
            .Because("a managed stack at timeout distinguishes stage-open deadlocks");
        await Assert.That(smoke)
            .Contains("createdump", StringComparison.Ordinal)
            .Because("live process dumps are needed when a hang produces no crash dump");
        await Assert.That(smoke)
            .Contains("Capture-HangDiagnostics -ViewerProcess $process", StringComparison.Ordinal)
            .Because("the process must be captured while it is still hung, before finally kills it");
        await Assert.That(smoke)
            .Contains("viewer hang stack", StringComparison.Ordinal)
            .Because("the timeout diagnostics must appear in the CI log, not only in artifacts");
    }

    [Test]
    public async Task ViewerBundleSmokeInstallsAndEnablesLinuxManagedHangStackCapture()
    {
        string root = FindRepositoryRoot();
        string viewer = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "viewer-distribution.yml"));
        string smoke = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "test-viewer-bundle-smoke.ps1"));

        string viewerSmokeJob = ReadJob(viewer, "viewer-distribution");
        string diagnosticStep = ReadStep(viewerSmokeJob, "Install Linux hang diagnostic tools");
        await Assert.That(diagnosticStep)
            .Contains("if: matrix.rid == 'linux-x64'", StringComparison.Ordinal)
            .Because("the Linux hang is the reproducible no-crash failure that needs stack evidence");
        await Assert.That(diagnosticStep)
            .Contains("dotnet tool update --global dotnet-stack", StringComparison.Ordinal)
            .Because("run 31251898756 had no dotnet-stack on PATH, so no managed stack was captured");
        await Assert.That(diagnosticStep)
            .Contains("echo \"$HOME/.dotnet/tools\" >> \"$GITHUB_PATH\"", StringComparison.Ordinal)
            .Because("global .NET tools are invisible to later GitHub Actions steps until this path is exported");
        await Assert.That(diagnosticStep)
            .Contains("sudo sysctl -w kernel.yama.ptrace_scope=0", StringComparison.Ordinal)
            .Because("createdump was denied by Ubuntu ptrace_scope=1 while opening /proc/<pid>/mem");

        await Assert.That(smoke)
            .Contains("Get-Command dotnet-stack -ErrorAction SilentlyContinue", StringComparison.Ordinal)
            .Because("the smoke script must tolerate local runs where the diagnostic tool is absent");
        await Assert.That(smoke)
            .Contains("'report', '-p', [string]$ViewerProcess.Id", StringComparison.Ordinal)
            .Because("dotnet-stack report prints the managed stack directly in the timeout log");
        await Assert.That(smoke)
            .Contains("dotnet-stack was not available on PATH.", StringComparison.Ordinal)
            .Because("absence of the optional diagnostic tool should be reported rather than throwing");
    }

    /// <summary>
    /// Returns the quoted arguments of the <c>hashFiles</c> call anchored on a
    /// cache key prefix, in declaration order.
    /// </summary>
    private static IReadOnlyList<string> HashFileInputs(string workflow, string prefix)
    {
        Match anchor = Regex.Match(
            workflow,
            Regex.Escape(prefix) + @"\$\{\{\s*hashFiles\((?<args>[^)]*)\)",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        return anchor.Success
            ? [.. Regex.Matches(
                anchor.Groups["args"].Value,
                @"'(?<file>[^']+)'",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5))
                .Select(match => match.Groups["file"].Value)]
            : [];
    }

    /// <summary>Splits a workflow into its jobs, keyed by job id.</summary>
    private static IEnumerable<(string Name, string Body)> ReadJobs(string workflow)
    {
        string[] lines = workflow.Split('\n');
        int jobsAt = Array.FindIndex(
            lines,
            line => line.StartsWith("jobs:", StringComparison.Ordinal));
        if (jobsAt < 0)
        {
            yield break;
        }

        string? current = null;
        List<string> body = [];
        for (int index = jobsAt + 1; index < lines.Length; index++)
        {
            Match header = JobHeader.Match(lines[index].TrimEnd('\r'));
            if (header.Success)
            {
                if (current is not null)
                {
                    yield return (current, string.Join("\n", body));
                }

                current = header.Groups["name"].Value;
                body = [];
                continue;
            }

            body.Add(lines[index]);
        }

        if (current is not null)
        {
            yield return (current, string.Join("\n", body));
        }
    }

    /// <summary>Returns one workflow job body by id, or an empty string when absent.</summary>
    private static string ReadJob(string workflow, string name) =>
        ReadJobs(workflow)
            .Where(job => job.Name == name)
            .Select(job => job.Body)
            .FirstOrDefault() ?? string.Empty;

    /// <summary>Returns one workflow step body by display name, or an empty string when absent.</summary>
    private static string ReadStep(string job, string name)
    {
        string header = $"      - name: {name}";
        string[] lines = job.Split('\n');
        int start = Array.FindIndex(
            lines,
            line => line.TrimEnd('\r') == header);
        if (start < 0)
        {
            return string.Empty;
        }

        List<string> step = [];
        for (int index = start; index < lines.Length; index++)
        {
            string line = lines[index].TrimEnd('\r');
            if (index > start && line.StartsWith("      - ", StringComparison.Ordinal))
            {
                break;
            }

            step.Add(line);
        }

        return string.Join("\n", step);
    }

    /// <summary>Returns the <c>on:</c> block, up to the next top-level key.</summary>
    private static string ReadTriggerBlock(string workflow)
    {
        string[] lines = workflow.Split('\n');
        int start = Array.FindIndex(
            lines,
            line => line.StartsWith("on:", StringComparison.Ordinal));
        if (start < 0)
        {
            return string.Empty;
        }

        List<string> block = [];
        for (int index = start + 1; index < lines.Length; index++)
        {
            string line = lines[index].TrimEnd('\r');
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                break;
            }

            block.Add(line);
        }

        return string.Join("\n", block);
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
