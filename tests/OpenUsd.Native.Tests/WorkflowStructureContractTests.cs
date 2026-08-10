// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Text.Json;
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
    public async Task NuGetPromotionExpectsSymbolsOnlyWherePackingProducesThem()
    {
        string root = FindRepositoryRoot();
        string nuget = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "nuget.yml"));

        await Assert.That(nuget)
            .Contains("-ListSymbolPublished", StringComparison.Ordinal)
            .Because(
                "twelve runtime packaging projects set IncludeSymbols=false, so demanding a " +
                "snupkg for every published id would throw on every promotion");

        string pack = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "pack-packages.ps1"));
        await Assert.That(pack)
            .Contains("<IncludeSymbols>", StringComparison.Ordinal)
            .Because(
                "the symbol set must be derived from each project rather than restated, " +
                "or it drifts the moment a project changes");
    }

    [Test]
    public async Task NuGetPromotionStagesSymbolsFromReleaseArtifacts()
    {
        string root = FindRepositoryRoot();
        string nuget = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "nuget.yml"));
        string release = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "release.yml"));

        string promote = ReadJob(nuget, "promote");
        await Assert.That(nuget)
            .Contains("actions: read", StringComparison.Ordinal)
            .Because("nuget.yml must read release artifacts to recover snupkg files");
        await Assert.That(nuget)
            .Contains("release_run_id:", StringComparison.Ordinal)
            .Because("manual promotion needs an override when the tag run cannot be inferred");
        await Assert.That(nuget)
            .Contains("dry_run:", StringComparison.Ordinal)
            .Because("new promotion plumbing needs a way to exercise downloads and symbol staging safely");
        await Assert.That(promote)
            .Contains("release.yml/runs", StringComparison.Ordinal)
            .Because("symbols are stored on the tag release run, not in the GitHub feed");
        await Assert.That(promote)
            .Contains("name: openusd-published-nupkgs", StringComparison.Ordinal)
            .Because("the published release artifact is where snupkg files survive packing");

        string stageSymbols = ReadStep(promote, "Stage symbol packages for nuget.org");
        await Assert.That(stageSymbols)
            .Contains("Get-ChildItem release-artifacts/nupkg -Filter '*.snupkg'", StringComparison.Ordinal)
            .Because("promotion must stage symbols from release artifacts before pushing");
        await Assert.That(stageSymbols)
            .Contains("Copy-Item", StringComparison.Ordinal)
            .Because("each snupkg must be copied beside its matching nupkg");
        await Assert.That(stageSymbols)
            .Contains("openusd.0.6.0-alpha.snupkg 404", StringComparison.Ordinal)
            .Because("the workflow should explain the concrete symbol-publish regression");

        string push = ReadStep(promote, "Push to nuget.org");
        await Assert.That(push)
            .Contains("dotnet nuget push \"artifacts/*.nupkg\"", StringComparison.Ordinal)
            .Because("the nupkg bytes still come from the GitHub Packages feed");
        await Assert.That(push)
            .Contains("github.event.inputs.dry_run != 'true'", StringComparison.Ordinal)
            .Because("dry-run promotion must validate package and symbol staging without publishing");
        await Assert.That(push)
            .DoesNotContain("--no-symbols", StringComparison.Ordinal)
            .Because("dotnet nuget push uploads an adjacent snupkg unless this option disables it");

        string dryRun = ReadStep(promote, "Dry-run result");
        await Assert.That(dryRun)
            .Contains("github.event.inputs.dry_run == 'true'", StringComparison.Ordinal)
            .Because("dry-run promotion must stop after validating staging");
        await Assert.That(dryRun)
            .Contains("Skipping NuGet/login and dotnet nuget push", StringComparison.Ordinal)
            .Because("the dry-run log must make clear that no one-way publish happened");

        string uploadPublished = ReadStep(ReadJob(release, "publish"), "Upload the published packages");
        await Assert.That(uploadPublished)
            .Contains("path: artifacts/nupkg", StringComparison.Ordinal)
            .Because("release artifacts must include both nupkg and snupkg files from pack");
        await Assert.That(uploadPublished)
            .DoesNotContain("*.nupkg", StringComparison.Ordinal)
            .Because("the release artifact must not filter out the adjacent snupkg files");
    }

    [Test]
    public async Task NuGetPromotionRequiresTheCompleteGitHubFeedPackageSet()
    {
        string root = FindRepositoryRoot();
        string nuget = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "nuget.yml"));

        string promote = ReadJob(nuget, "promote");
        string completeSet = ReadStep(promote, "Require complete GitHub-feed package set");
        await Assert.That(completeSet)
            .Contains("-ListPublished", StringComparison.Ordinal)
            .Because(
                "nuget.org promotion must compare downloaded packages with the release published set, " +
                "not just push whatever subset happened to exist on the GitHub feed");
        await Assert.That(completeSet)
            .Contains("StringComparer]::OrdinalIgnoreCase", StringComparison.Ordinal)
            .Because("GitHub flat-container downloads are lowercase, but project package ids are not");
        await Assert.That(completeSet)
            .Contains("Missing: $($missing -join ', ')", StringComparison.Ordinal)
            .Because("a partial GitHub-feed publish must fail before obtaining a nuget.org token");
        await Assert.That(completeSet)
            .Contains("Unexpected: $($unexpected -join ', ')", StringComparison.Ordinal)
            .Because("promotion must not silently publish bytes outside the declared package set");
    }

    [Test]
    public async Task SymbolPublishedListMatchesProjectIncludeSymbols()
    {
        string root = FindRepositoryRoot();
        string[] published = await RunPowerShellLinesAsync(
            root,
            "./eng/pack-packages.ps1",
            "-ListPublished");
        string[] symbolPublished = await RunPowerShellLinesAsync(
            root,
            "./eng/pack-packages.ps1",
            "-ListSymbolPublished");

        HashSet<string> actual = new(symbolPublished, StringComparer.Ordinal);
        List<string> expected = [];
        foreach (string id in published)
        {
            string projectPath = Path.Combine(root, "src", id, $"{id}.csproj");
            string project = await File.ReadAllTextAsync(projectPath);
            bool suppressesSymbols = Regex.IsMatch(
                    project,
                    @"<IncludeSymbols>\s*false\s*</IncludeSymbols>",
                    RegexOptions.CultureInvariant) ||
                Regex.IsMatch(
                    project,
                    @"<IsApplicationProject>\s*true\s*</IsApplicationProject>",
                    RegexOptions.CultureInvariant);
            if (!suppressesSymbols)
            {
                expected.Add(id);
            }
        }

        await Assert.That(symbolPublished.Length)
            .IsEqualTo(9)
            .Because("the current 22-package release set has nine managed packages that emit snupkg files");
        await Assert.That(actual)
            .IsEquivalentTo(expected)
            .Because("-ListSymbolPublished must match the MSBuild conditions that govern snupkg production");

        // Ground truth, not a restatement of the script. Directory.Build.props sets
        // IncludeSymbols and SymbolPackageFormat inside a PropertyGroup guarded by
        // _IsProductionLibrary, which IsApplicationProject excludes a project from. So an
        // application project has IncludeSymbols *absent* rather than false, and packs no
        // snupkg. Asserting only "not explicitly false" is what let the 0.7.0-alpha
        // promotion demand openusd.viewer.0.7.0-alpha.snupkg and fail after the packages
        // had already been pushed to the GitHub feed.
        await Assert.That(actual).DoesNotContain("OpenUsd.Viewer");
        foreach (string id in symbolPublished)
        {
            string project = await File.ReadAllTextAsync(
                Path.Combine(root, "src", id, $"{id}.csproj"));
            await Assert.That(Regex.IsMatch(
                    project,
                    @"<IsApplicationProject>\s*true\s*</IsApplicationProject>",
                    RegexOptions.CultureInvariant))
                .IsFalse()
                .Because($"{id} is expected to emit a snupkg, so it must be a production library");
        }
    }

    [Test]
    public async Task ReleasePublishesTheGeneratedSbom()
    {
        string root = FindRepositoryRoot();
        string ci = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "ci.yml"));
        string release = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "release.yml"));
        string generator = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "generate-sbom.py"));

        await Assert.That(ci)
            .Contains("./eng/check-sbom.ps1", StringComparison.Ordinal)
            .Because("the checked SBOM must fail CI when pinned dependency inputs change");
        string ciTriggers = ReadTriggerBlock(ci);
        await Assert.That(ciTriggers)
            .Contains("push:", StringComparison.Ordinal)
            .Because("SBOM drift from a version bump must fail on the bump push, not first in release.yml");
        await Assert.That(ciTriggers)
            .Contains("pull_request:", StringComparison.Ordinal)
            .Because("SBOM drift from a version bump must fail before merge when the bump is reviewed");
        await Assert.That(ciTriggers)
            .DoesNotContain("paths:", StringComparison.Ordinal)
            .Because("version.json changes must not be path-filtered away from the SBOM drift check");

        foreach (string pinnedInput in new[]
        {
            "eng/openusd.install.lock.json",
            "eng/cesium.lock.json",
            "eng/physx.lock.json",
            "eng/shaders/toolchain.lock.json",
            "global.json",
            "Directory.Packages.props",
            "eng/pack-packages.ps1",
            "eng/publish-viewer-bundle.ps1",
            "eng/sbom/cesium-vcpkg-components.lock.json",
        })
        {
            await Assert.That(generator)
                .Contains(pinnedInput, StringComparison.Ordinal)
                .Because($"{pinnedInput} contributes to the release SBOM and its drift hash");
        }

        string publish = ReadJob(release, "publish");
        await Assert.That(publish)
            .Contains("contents: write", StringComparison.Ordinal)
            .Because("publishing the SBOM as a GitHub release asset requires contents: write");

        string generate = ReadStep(publish, "Generate release SBOM");
        await Assert.That(generate)
            .Contains("eng/generate-sbom.py", StringComparison.Ordinal)
            .Because("the release must generate from pinned inputs rather than uploading a stale file");
        await Assert.That(generate)
            .Contains("--validate", StringComparison.Ordinal)
            .Because("a generated SBOM must be validated before it is published");

        string uploadArtifact = ReadStep(publish, "Upload the release SBOM artifact");
        await Assert.That(uploadArtifact)
            .Contains("openusd-release.cdx.json", StringComparison.Ordinal)
            .Because("the workflow artifact keeps the SBOM tied to the release run evidence");

        string uploadRelease = ReadStep(publish, "Upload the SBOM to the GitHub release");
        await Assert.That(uploadRelease)
            .Contains("gh release upload", StringComparison.Ordinal)
            .Because("the GitHub release asset is the durable home beside the published artifacts");
        await Assert.That(uploadRelease)
            .Contains("openusd-release.cdx.json", StringComparison.Ordinal)
            .Because("supply-chain scanners need the standardized CycloneDX JSON asset");
    }

    [Test]
    public async Task ReleaseSbomUploadEnsuresGitHubReleaseExists()
    {
        string root = FindRepositoryRoot();
        string release = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "release.yml"));
        string publish = ReadJob(release, "publish");

        string ensureRelease = ReadStep(publish, "Ensure GitHub release exists for SBOM");
        string uploadRelease = ReadStep(publish, "Upload the SBOM to the GitHub release");
        await Assert.That(ensureRelease)
            .Contains("gh release view \"$tag\"", StringComparison.Ordinal)
            .Because("tag releases must tolerate a manually created draft or published release");
        await Assert.That(ensureRelease)
            .Contains("gh release create \"$tag\"", StringComparison.Ordinal)
            .Because("gh release upload cannot create a missing release");
        await Assert.That(ensureRelease)
            .Contains("--draft", StringComparison.Ordinal)
            .Because("the workflow should attach artifacts without publishing hand-curated notes");
        await Assert.That(ensureRelease)
            .DoesNotContain("--generate-notes", StringComparison.Ordinal)
            .Because("release notes are curated manually after the workflow attaches artifacts");
        await Assert.That(ensureRelease)
            .Contains("Release notes are curated manually", StringComparison.Ordinal)
            .Because("an automatically created draft must say why its notes are placeholders");
        await Assert.That(uploadRelease)
            .Contains("--clobber", StringComparison.Ordinal)
            .Because("rerunning the same tag must replace the SBOM asset instead of failing");

        int ensureIndex = publish.IndexOf(
            "Ensure GitHub release exists for SBOM",
            StringComparison.Ordinal);
        int uploadIndex = publish.IndexOf(
            "Upload the SBOM to the GitHub release",
            StringComparison.Ordinal);
        int pushIndex = publish.IndexOf(
            "Push to the GitHub Packages NuGet feed",
            StringComparison.Ordinal);
        await Assert.That(ensureIndex)
            .IsGreaterThanOrEqualTo(0)
            .Because("the publish job must contain the release-existence guard");
        await Assert.That(uploadIndex)
            .IsGreaterThan(ensureIndex)
            .Because("the release must be created or observed before uploading the SBOM asset");
        await Assert.That(uploadIndex)
            .IsLessThan(pushIndex)
            .Because("SBOM attachment failures should happen before the irreversible package push");
    }

    [Test]
    public async Task CheckedReleaseSbomVersionMatchesVersionJson()
    {
        string root = FindRepositoryRoot();
        string expectedVersion = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(root, "version.json")))
            .RootElement
            .GetProperty("version")
            .GetString()!;
        using JsonDocument sbom = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "sbom", "openusd-release.cdx.json")));

        JsonElement metadataComponent = sbom.RootElement
            .GetProperty("metadata")
            .GetProperty("component");
        await Assert.That(metadataComponent.GetProperty("version").GetString())
            .IsEqualTo(expectedVersion)
            .Because("the root release SBOM component is version-coupled to version.json");
        await Assert.That(metadataComponent.GetProperty("purl").GetString())
            .IsEqualTo($"pkg:nuget/OpenUsd@{expectedVersion}")
            .Because("the root release SBOM purl is version-coupled to version.json");

        int releasePackageComponents = 0;
        foreach (JsonElement component in sbom.RootElement.GetProperty("components").EnumerateArray())
        {
            if (!HasProperty(component, "openusd:release-artifact", "nupkg"))
            {
                continue;
            }

            releasePackageComponents++;
            string name = component.GetProperty("name").GetString()!;
            await Assert.That(component.GetProperty("version").GetString())
                .IsEqualTo(expectedVersion)
                .Because($"{name} is a published package component in the release SBOM");
            await Assert.That(component.GetProperty("purl").GetString())
                .IsEqualTo($"pkg:nuget/{name}@{expectedVersion}")
                .Because($"{name} purl must track version.json");
        }

        await Assert.That(releasePackageComponents)
            .IsEqualTo(22)
            .Because("every published package component must be checked for version drift");
    }

    [Test]
    public async Task ReleaseSbomCheckIsHermeticAndNormalizesLineEndings()
    {
        string root = FindRepositoryRoot();
        string generator = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "generate-sbom.py"));
        string check = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "check-sbom.ps1"));

        await Assert.That(generator)
            .Contains("content.replace(b\"\\r\\n\", b\"\\n\")", StringComparison.Ordinal)
            .Because(
                "SBOM input hashes must be LF-normalised so a CRLF checkout does not make the " +
                "checked SBOM appear stale on Linux");
        await Assert.That(generator)
            .Contains("load_vcpkg_components(vcpkg_components, cesium)", StringComparison.Ordinal)
            .Because("normal SBOM generation must consume committed vcpkg component data");
        await Assert.That(generator)
            .Contains("if args.refresh_vcpkg:", StringComparison.Ordinal)
            .Because("network refresh of vcpkg metadata must be an explicit update mode");
        await Assert.That(check)
            .DoesNotContain("--refresh-vcpkg", StringComparison.Ordinal)
            .Because("the CI drift check must be hermetic and must not fetch vcpkg manifests live");
    }


    [Test]
    public async Task ReleaseSbomGenerationIsByteStableAndPortable()
    {
        string root = FindRepositoryRoot();
        string work = Path.Combine(root, "artifacts", "sbom-determinism-tests");
        Directory.CreateDirectory(work);
        string first = Path.Combine(work, "first.cdx.json");
        string second = Path.Combine(work, "second.cdx.json");

        await RunPythonAsync(
            root,
            Path.Combine(root, "eng", "generate-sbom.py"),
            "--output",
            first);
        await RunPythonAsync(
            root,
            Path.Combine(root, "eng", "generate-sbom.py"),
            "--output",
            second);

        byte[] firstBytes = await File.ReadAllBytesAsync(first);
        byte[] secondBytes = await File.ReadAllBytesAsync(second);
        await Assert.That(secondBytes.SequenceEqual(firstBytes))
            .IsTrue()
            .Because("SBOM generation must be byte-for-byte stable within one checkout");

        using JsonDocument document = JsonDocument.Parse(firstBytes);
        await Assert.That(document.RootElement.GetProperty("metadata").TryGetProperty("timestamp", out _))
            .IsFalse()
            .Because("even a fixed timestamp creates an unnecessary portability hazard");

        foreach ((string location, string value) in EnumerateSbomPortableValues(document.RootElement))
        {
            await Assert.That(value)
                .DoesNotContain("\\", StringComparison.Ordinal)
                .Because($"{location} must not contain Windows path separators");
            await Assert.That(Regex.IsMatch(value, @"(^/|^[A-Za-z]:[\\/])", RegexOptions.CultureInvariant))
                .IsFalse()
                .Because($"{location} must not contain an absolute filesystem path");
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
    public async Task ViewerDistributionSmokeOutcomeIsReportedExplicitly()
    {
        string root = FindRepositoryRoot();
        string viewerDistribution = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "viewer-distribution.yml"));

        string smoke = ReadJob(viewerDistribution, "viewer-distribution");
        await Assert.That(smoke)
            .Contains("if: needs.pack-viewer-inputs.outputs.ready == 'true'", StringComparison.Ordinal)
            .Because(
                "missing native artifacts should still defer the expensive smoke instead of " +
                "turning routine native-pipeline lag into a red workflow");

        string passed = ReadJob(viewerDistribution, "viewer-distribution-smoke-passed");
        await Assert.That(passed)
            .Contains("viewer distribution smoke passed", StringComparison.Ordinal)
            .Because("a checks-list reader needs a positive check when the smoke actually ran");
        await Assert.That(passed)
            .Contains("needs.viewer-distribution.result == 'success'", StringComparison.Ordinal)
            .Because("the pass report must be evidence from the smoke job, not only native readiness");

        string deferred = ReadJob(viewerDistribution, "viewer-distribution-smoke-deferred");
        await Assert.That(deferred)
            .Contains("viewer distribution smoke deferred (native artifacts unavailable)", StringComparison.Ordinal)
            .Because("a checks-list reader must not have to infer a deferred smoke from an absent matrix job");
        await Assert.That(deferred)
            .Contains("needs.pack-viewer-inputs.outputs.ready != 'true'", StringComparison.Ordinal)
            .Because("the deferred report must be tied to the same readiness output that gates the smoke");
        await Assert.That(deferred)
            .Contains("This is an expected deferral, not smoke evidence", StringComparison.Ordinal)
            .Because("the run summary must say that the green workflow is not proof of a passed smoke");
    }


    [Test]
    public async Task RenderWorkflowRunsOutsideAReleaseOnEveryHostedLeg()
    {
        string root = FindRepositoryRoot();
        string render = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "render.yml"));
        string native = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "native.yml"));

        string triggers = ReadTriggerBlock(render);
        await Assert.That(triggers).IsNotEmpty();
        await Assert.That(triggers)
            .Contains("workflow_run:", StringComparison.Ordinal)
            .Because("native changes must run render after the verified native archive exists");
        await Assert.That(triggers)
            .Contains("push:", StringComparison.Ordinal)
            .Because(
                "render.yml was release-only until 0.6.0-alpha, which hid stale soak ABI " +
                "constants and capability defects until the release gate");

        foreach (string branch in new[] { "master", "main" })
        {
            await Assert.That(triggers)
                .Contains(branch, StringComparison.Ordinal)
                .Because($"the push trigger must cover '{branch}' or it silently never fires");
        }

        foreach (string path in new[]
        {
            "'.github/workflows/render.yml'",
            "'eng/run-parity-capture.ps1'",
            "'eng/run-platform-smoke.ps1'",
            "'eng/run-native-probe.ps1'",
            "'eng/run-silk-probe.ps1'",
            "'eng/resolve-macos-cgl-capability.ps1'",
            "'tests/OpenUsd.Rendering.ConformanceTests/**'",
            "'src/OpenUsd.Rendering*/**'",
            "'eng/shaders/**'",
        })
        {
            await Assert.That(triggers)
                .Contains(path, StringComparison.Ordinal)
                .Because($"{path} is consumed directly by at least one render leg");
        }

        Match named = Regex.Match(
            triggers,
            @"workflows:\s*\[\s*'(?<title>[^']+)'\s*\]",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));
        Match upstream = Regex.Match(
            native,
            @"^name:\s*(?<title>.+?)\s*$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));
        await Assert.That(named.Success).IsTrue();
        await Assert.That(upstream.Success).IsTrue();
        await Assert.That(named.Groups["title"].Value)
            .IsEqualTo(upstream.Groups["title"].Value)
            .Because("render.yml triggers on the native pipeline by title, so it must match");

        foreach (string job in new[]
        {
            "windows-wgl",
            "windows-vulkan-required",
            "linux-presentation",
            "macos-arm64",
        })
        {
            await Assert.That(ReadJob(render, job))
                .IsNotEmpty()
                .Because($"the render push gate must keep the hosted {job} leg wired");
        }
    }

    [Test]
    public async Task RenderWorkflowReusesNativeArchivesAndNeverRebuildsOnPush()
    {
        string root = FindRepositoryRoot();
        string render = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "render.yml"));
        string native = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "native.yml"));

        const string nativePrefix = "native-${{ matrix.rid }}-";
        IReadOnlyList<string> saved = HashFileInputs(native, nativePrefix);
        await Assert.That(saved.Count)
            .IsGreaterThan(20)
            .Because("native.yml must still key its install cache on hashFiles");

        foreach ((string jobName, string rid) in new[]
        {
            ("windows-wgl", "win-x64"),
            ("windows-vulkan-required", "win-x64"),
            ("linux-presentation", "linux-x64"),
            ("macos-arm64", "osx-arm64"),
        })
        {
            string job = ReadJob(render, jobName);
            await Assert.That(job)
                .Contains("actions/cache/restore@", StringComparison.Ordinal)
                .Because($"{jobName} must restore native.yml's cache rather than saving its own");

            IReadOnlyList<string> restored = HashFileInputs(job, $"native-{rid}-");
            await Assert.That(restored.Count)
                .IsGreaterThan(20)
                .Because($"{jobName} must restore the full native install cache");
            await Assert.That(restored)
                .IsEquivalentTo(saved)
                .Because($"{jobName} must use the exact native.yml cache inputs");

            await Assert.That(job)
                .Contains("DEFER_ON_NATIVE_CACHE_MISS:", StringComparison.Ordinal)
                .Because($"{jobName} needs an explicit push-cache-miss deferral switch");
            await Assert.That(job)
                .Contains("github.event_name == 'push'", StringComparison.Ordinal)
                .Because("only self-firing push runs may defer instead of building OpenUSD");
            await Assert.That(job)
                .Contains("inputs['native-source'] || 'build'", StringComparison.Ordinal)
                .Because("self-firing push runs have no workflow input defaults but still need cache restore");
            await Assert.That(job)
                .Contains("RENDER_SMOKE_DEFERRED", StringComparison.Ordinal)
                .Because("a deferred render smoke must leave a searchable notice");

            string readyStep = ReadStep(job, "Decide native input readiness");
            await Assert.That(readyStep)
                .Contains("$source -eq 'archive'", StringComparison.Ordinal)
                .Because("tag releases pass a native pipeline run id and must be ready in archive mode");
            await Assert.That(readyStep)
                .Contains("steps.native-cache.outputs.cache-hit", StringComparison.Ordinal)
                .Because("push runs may proceed only when the verified native cache is present");
            await Assert.That(readyStep)
                .Contains("DEFER_ON_NATIVE_CACHE_MISS", StringComparison.Ordinal)
                .Because("manual and release runs must not silently skip render gates");

            string prepareStep = ReadStep(job, "Prepare pinned native input");
            await Assert.That(prepareStep)
                .Contains("steps.native-ready.outputs.ready == 'true'", StringComparison.Ordinal)
                .Because($"{jobName} must skip native input staging when the push run deferred");
            await Assert.That(prepareStep)
                .Contains("steps.native-cache.outputs.cache-hit != 'true'", StringComparison.Ordinal)
                .Because($"{jobName} must not call the source-build helper after a cache hit");
        }
    }

    [Test]
    public async Task LinuxStormChildLifecycleSmokeCannotBeSilentlyDropped()
    {
        string root = FindRepositoryRoot();
        string probeCmake = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "tests",
            "CMakeLists.txt"));
        string probeSource = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "tests",
            "storm_child_probe_linux.cpp"));
        string runner = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "run-storm-native-child-linux.sh"));
        string render = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "render.yml"));

        await Assert.That(probeCmake)
            .Contains("NAME openusd_storm_child_lifecycle_smoke\n        COMMAND")
            .Because(
                "the direct-rendering probe skips on hosted Linux Xvfb, so CTest needs a " +
                "separate non-GPU lifecycle smoke that cannot disappear into the old skip");
        await Assert.That(probeCmake)
            .Contains("--lifecycle-smoke", StringComparison.Ordinal)
            .Because("the lifecycle smoke must be a capability-scoped mode of the Linux probe binary");
        await Assert.That(probeCmake)
            .Contains("openusd_storm_child_lifecycle_smoke\n        PROPERTIES TIMEOUT 60")
            .Because("the parent_trap/colormap_trap regression hangs, so the smoke must time out");
        await Assert.That(probeSource)
            .Contains("lifecycle-smoke-context-unavailable", StringComparison.Ordinal)
            .Because("the hosted smoke stops explicitly at the GL context boundary instead of faking GPU proof");
        await Assert.That(probeSource)
            .Contains("The Storm child lifecycle smoke timed out.", StringComparison.Ordinal)
            .Because("a create/destroy hang must fail loudly rather than consuming the whole CI job");
        await Assert.That(runner)
            .Contains("--lifecycle-smoke", StringComparison.Ordinal)
            .Because("archive-mode render jobs run the installed probe directly, not CTest");
        await Assert.That(render)
            .Contains("'eng/run-storm-native-child-linux.sh'", StringComparison.Ordinal)
            .Because("changes to the installed-probe runner must keep triggering the render workflow");
    }

    [Test]
    public async Task RenderWorkflowTagReleaseArchiveModeCannotDeferGates()
    {
        string root = FindRepositoryRoot();
        string render = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "render.yml"));

        foreach (string jobName in new[]
        {
            "windows-wgl",
            "windows-vulkan-required",
            "linux-presentation",
            "macos-arm64",
        })
        {
            string job = ReadJob(render, jobName);
            await Assert.That(job)
                .Contains(
                    "github.event_name == 'workflow_run' && github.event.workflow_run.id",
                    StringComparison.Ordinal)
                .Because($"{jobName} must consume the native pipeline archive after native.yml completes");
            await Assert.That(job)
                .Contains("inputs['native-pipeline-run-id']) && 'archive'", StringComparison.Ordinal)
                .Because(
                    "release.yml invokes render.yml from a tag push; the called workflow still sees " +
                    "event_name == 'push', so readiness must come from the supplied archive input");
            await Assert.That(job)
                .Contains("github.event_name != 'workflow_run' ||", StringComparison.Ordinal)
                .Because("workflow_run should not look for an archive after a failed native pipeline");
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
    public async Task ViewerDistributionPushSmokeConsumesLocalPackagesBeforeNuGetOrg()
    {
        string root = FindRepositoryRoot();
        string viewer = await File.ReadAllTextAsync(
            Path.Combine(root, ".github", "workflows", "viewer-distribution.yml"));
        string publisher = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "publish-viewer-bundle.ps1"));

        string triggers = ReadTriggerBlock(viewer);
        await Assert.That(triggers)
            .Contains("'version.json'", StringComparison.Ordinal)
            .Because("a version bump must start a package-only smoke before that version is on nuget.org");

        string packJob = ReadJob(viewer, "pack-viewer-inputs");
        await Assert.That(packJob)
            .Contains("Pack the runtime packages for this RID", StringComparison.Ordinal)
            .Because("push smoke must produce the current RID packages locally");
        await Assert.That(packJob)
            .Contains("Pack the platform-neutral packages", StringComparison.Ordinal)
            .Because("push smoke must produce OpenUsd.Viewer and managed dependencies locally");
        await Assert.That(packJob)
            .Contains("Upload packed Viewer inputs", StringComparison.Ordinal)
            .Because("the smoke job must consume nupkg artifacts rather than project references");

        string smokeJob = ReadJob(viewer, "viewer-distribution");
        await Assert.That(smokeJob)
            .Contains("Download packed Viewer inputs", StringComparison.Ordinal)
            .Because("the push-triggered smoke must not require the current version to exist on nuget.org");
        await Assert.That(smokeJob)
            .Contains("-PackageSource artifacts/nupkg", StringComparison.Ordinal)
            .Because("the generated consumer app must restore OpenUsd packages from the packed local feed");
        await Assert.That(publisher)
            .Contains("<package pattern=\"OpenUsd.*\" />", StringComparison.Ordinal)
            .Because("OpenUsd packages must be source-mapped to the local nupkg feed when one is supplied");

        // The root package id has no dot, so the "OpenUsd.*" glob does not match it. Mapping only
        // that pattern sent OpenUsd itself to nuget.org, which failed the 0.8.0-alpha release with
        // "NU1102 ... Versions from openusd-local were not considered" — and only during a release,
        // because between a bump and its publication is the one window where nuget.org cannot serve
        // the version.
        await Assert.That(publisher)
            .Contains("<package pattern=\"OpenUsd\" />", StringComparison.Ordinal)
            .Because("the root OpenUsd package id is not matched by the OpenUsd.* glob");
        await Assert.That(publisher)
            .DoesNotContain("<ProjectReference", StringComparison.Ordinal)
            .Because("the Viewer distribution smoke must stay package-only, not become a source build");
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
    public async Task ViewerBundleSmokeBoundsEveryProcessWaitAndOverallRuntime()
    {
        string root = FindRepositoryRoot();
        string smoke = await File.ReadAllTextAsync(
            Path.Combine(root, "eng", "test-viewer-bundle-smoke.ps1"));

        await Assert.That(Regex.Count(
                smoke,
                @"\.WaitForExit\(\s*\)",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5)))
            .IsEqualTo(0)
            .Because("the Viewer smoke must not reintroduce unbounded process waits");
        await Assert.That(smoke)
            .Contains("$overallSmokeTimeoutSeconds = $SmokeSeconds + 180", StringComparison.Ordinal)
            .Because(
                "job 93147442138 sat past twenty minutes in a step with a 120-second render wait; " +
                "the script needs a hard smoke ceiling far below the 120-minute job timeout");
        await Assert.That(smoke)
            .Contains("function Stop-ProcessBounded", StringComparison.Ordinal)
            .Because("Stop-Process must be followed by attributed, bounded exit waits");
        await Assert.That(smoke)
            .Contains("SIGKILL", StringComparison.Ordinal)
            .Because("Unix processes that ignore Stop-Process need an explicit forced-kill escalation");
        await Assert.That(smoke)
            .Contains("Archive extraction wait", StringComparison.Ordinal)
            .Because("bundle extraction is a process wait and must report its own expired bound");
        await Assert.That(smoke)
            .Contains("diagnostic tool '$FilePath'", StringComparison.Ordinal)
            .Because("diagnostic helper waits must stay bounded without anonymous timeout messages");

        int capture = smoke.IndexOf("Capture-HangDiagnostics -ViewerProcess $process", StringComparison.Ordinal);
        int renderedStop = smoke.IndexOf(
            "Stop-ProcessBounded -Process $process -Reason \"viewer rendered status was observed\"",
            StringComparison.Ordinal);
        await Assert.That(capture)
            .IsGreaterThan(0)
            .Because("live hang diagnostics must still be captured before any cleanup kill");
        await Assert.That(renderedStop)
            .IsGreaterThan(capture)
            .Because("the bounded normal shutdown must not move ahead of live hang capture");
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
            .Contains("sudo apt-get install -y gdb", StringComparison.Ordinal)
            .Because("run 93142409201 still had opaque native frames, so gdb must capture them");
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
        await Assert.That(smoke)
            .Contains("viewer-native-stack.txt", StringComparison.Ordinal)
            .Because("the full gdb native backtrace must be uploaded with the smoke diagnostics");
        await Assert.That(smoke)
            .Contains("Get-Command gdb -ErrorAction SilentlyContinue", StringComparison.Ordinal)
            .Because("the smoke script must tolerate local and hosted runs where gdb is absent");
        await Assert.That(smoke)
            .Contains("'-ex=info threads'", StringComparison.Ordinal)
            .Because("gdb must list thread owners before printing full backtraces");
        await Assert.That(smoke)
            .Contains("'-ex=thread apply all bt full'", StringComparison.Ordinal)
            .Because("gdb must capture native frames and locals to identify the blocking X/GLX call");
        await Assert.That(smoke)
            .Contains("$debuggerName was not available on PATH.", StringComparison.Ordinal)
            .Because(
                "absence of the optional native debugger should be reported rather than " +
                "throwing, and the name is now gdb on Linux or lldb on macOS");
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

    private static async Task RunPythonAsync(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("python")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start python.");
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"python {string.Join(" ", arguments)} failed with exit code {process.ExitCode}.\n" +
                output + error);
        }
    }

    private static async Task<string[]> RunPowerShellLinesAsync(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("pwsh")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start pwsh.");
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"pwsh {string.Join(" ", arguments)} failed with exit code {process.ExitCode}.\n" +
                output + error);
        }

        return output.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IEnumerable<(string Location, string Value)> EnumerateSbomPortableValues(JsonElement root)
    {
        foreach (JsonElement component in root.GetProperty("components").EnumerateArray())
        {
            if (component.TryGetProperty("bom-ref", out JsonElement bomRef))
            {
                yield return ($"component {component.GetProperty("name").GetString()} bom-ref", bomRef.GetString()!);
            }

            if (component.TryGetProperty("properties", out JsonElement properties))
            {
                foreach (JsonElement property in properties.EnumerateArray())
                {
                    yield return (
                        $"component {component.GetProperty("name").GetString()} property " +
                        property.GetProperty("name").GetString(),
                        property.GetProperty("value").GetString()!);
                }
            }
        }

        if (root.GetProperty("metadata").TryGetProperty("properties", out JsonElement metadataProperties))
        {
            foreach (JsonElement property in metadataProperties.EnumerateArray())
            {
                yield return (
                    $"metadata property {property.GetProperty("name").GetString()}",
                    property.GetProperty("value").GetString()!);
            }
        }
    }

    private static bool HasProperty(JsonElement component, string name, string value)
    {
        if (!component.TryGetProperty("properties", out JsonElement properties))
        {
            return false;
        }

        foreach (JsonElement property in properties.EnumerateArray())
        {
            if (property.GetProperty("name").GetString() == name &&
                property.GetProperty("value").GetString() == value)
            {
                return true;
            }
        }

        return false;
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
