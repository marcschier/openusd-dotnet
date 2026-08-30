// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using OpenUsd.Interop;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.Tests;

public sealed class SharedStageSoakTests
{
    [Test]
    public async Task DeterministicPlanIncludesTenThousandEditsAndReads()
    {
        int[] counts = new int[4];
        for (int index = 0; index < 12_500; index++)
        {
            counts[(int)SharedStageSoak.GetOperation(index, 12_500)]++;
        }

        await Assert.That(counts).IsEquivalentTo([5000, 2500, 2500, 2500]);
        SharedStageSoakOperation[] cycle =
        [
            SharedStageSoakOperation.Property,
            SharedStageSoakOperation.Topology,
            SharedStageSoakOperation.Composition,
            SharedStageSoakOperation.Property,
            SharedStageSoakOperation.Read
        ];
        for (int index = 0; index < 20; index++)
        {
            await Assert.That(SharedStageSoak.GetOperation(index, 12_500))
                .IsEqualTo(cycle[index % cycle.Length]);
        }
    }

    [Test]
    public async Task ArtifactRecordsMetricsAndReleasedResources()
    {
        SharedStageSoakResult result = new SharedStageSoakResult
        {
            StartedAt = DateTimeOffset.UnixEpoch,
            CompletedAt = DateTimeOffset.UnixEpoch.AddSeconds(90),
            OrderedOperations = 12_500,
            MutatingOperations = 10_001,
            BuildIdentity = CreateIdentity(),
            ResourcesReleased = true,
            ContextLossSimulated = true,
            SilkSessionTeardownSimulated = true,
            ActiveChildRejectionObserved = true
        };

        string json = result.ToJson();

        await Assert.That(json).Contains("\"orderedOperations\": 12500");
        await Assert.That(json).Contains("\"resourcesReleased\": true");
        await Assert.That(json).Contains("\"contextLossSimulated\": true");
        await Assert.That(json).Contains("\"silkSessionTeardownSimulated\": true");
        await Assert.That(json).Contains("\"activeChildRejectionObserved\": true");
        await Assert.That(json).Contains("\"sourceHash\": \"SOURCE\"");
        await Assert.That(json).Contains("\"dataAbi\": 15");
        await Assert.That(json).Contains("\"stormAbi\": 8");
        await Assert.That(json).Contains("\"silkSessionAbi\": 5");
        await Assert.That(json).Contains("\"silkPageAbi\": 13");
        await Assert.That(json).Contains("\"expectedFinalMeshes\"");
        await Assert.That(json).Contains("\"actualFinalDisplayColor\"");
    }

    [Test]
    public async Task SoakAbiIdentityMatchesAuthoritativeContracts()
    {
        uint soakData = GetConstant<uint>(
            typeof(SharedStageBuildIdentity),
            nameof(SharedStageBuildIdentity.DataAbi));
        uint soakStorm = GetConstant<uint>(
            typeof(SharedStageBuildIdentity),
            nameof(SharedStageBuildIdentity.StormAbi));
        uint soakSilkSession = GetConstant<uint>(
            typeof(SharedStageBuildIdentity),
            nameof(SharedStageBuildIdentity.SilkSessionAbi));
        uint soakSilkPage = GetConstant<uint>(
            typeof(SharedStageBuildIdentity),
            nameof(SharedStageBuildIdentity.SilkPageAbi));
        uint stormExpected = GetConstant<uint>(
            typeof(OpenUsdStormRuntime),
            nameof(OpenUsdStormRuntime.ExpectedAbiVersion));
        uint renderStorm = GetConstant<uint>(
            typeof(RenderNativeAbiVersions),
            nameof(RenderNativeAbiVersions.StormAbi));
        uint renderSilkSession = GetConstant<uint>(
            typeof(RenderNativeAbiVersions),
            nameof(RenderNativeAbiVersions.SilkSessionAbi));

        await Assert.That(soakData).IsEqualTo(OpenUsdNativeContract.AbiVersion);
        await Assert.That(soakStorm).IsEqualTo(stormExpected);
        await Assert.That(stormExpected).IsEqualTo(renderStorm);
        await Assert.That(soakSilkSession).IsEqualTo(renderSilkSession);
        await Assert.That(soakSilkPage).IsEqualTo(SilkCommandParser.PageAbiVersion);

        string root = FindRepositoryRoot();
        uint stormHeader = await ReadNativeHeaderAbiVersionAsync(
            root,
            Path.Combine("native", "openusd_hydra", "include", "openusd_hydra.h"),
            @"#define\s+OPENUSD_STORM_ABI_VERSION\s+(\d+)u");
        uint silkSessionHeader = await ReadNativeHeaderAbiVersionAsync(
            root,
            Path.Combine("native", "hdSilk", "include", "openusd_hdsilk.h"),
            @"#define\s+OPENUSD_SILK_SESSION_ABI_VERSION\s+(\d+)u");

        await Assert.That(stormHeader).IsEqualTo(soakStorm);
        await Assert.That(silkSessionHeader).IsEqualTo(soakSilkSession);
    }

    [Test]
    public async Task StaleSourceIdentityIsRejected()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => SharedStageBuildIdentity.ValidateExact(
                "source hash",
                "EXPECTED",
                "STALE"));

        await Assert.That(exception.Message).Contains("stale");
    }

    [Test]
    public async Task LaterWindowSlopeDetectsLinearGrowth()
    {
        SharedStageMemoryCheckpoint[] checkpoints = Enumerable.Range(1, 10)
            .Select(index => new SharedStageMemoryCheckpoint(
                index * 500,
                index * 400,
                index * 40_000L,
                0,
                0,
                0,
                2,
                default))
            .ToArray();

        double slope = SharedStageSoak.CalculateSlope(
            checkpoints,
            checkpoint => checkpoint.ManagedRetainedBytes);

        await Assert.That(slope).IsBetween(99_999.9, 100_000.1);
    }

    [Test]
    public async Task LaterWindowSlopeIgnoresSingleWorkingSetEndpointSpike()
    {
        SharedStageMemoryCheckpoint[] checkpoints = Enumerable.Range(1, 12)
            .Select(index => new SharedStageMemoryCheckpoint(
                index * 500,
                index * 400,
                0,
                index == 12 ? 32L * 1024 * 1024 : 64L * 1024 * 1024,
                0,
                0,
                2,
                default))
            .ToArray();

        double slope = SharedStageSoak.CalculateSlope(
            checkpoints,
            checkpoint => checkpoint.WorkingSetBytes);

        await Assert.That(slope).IsEqualTo(0);
    }

    [Test]
    public async Task LaterWindowSlopeStillDetectsSustainedWorkingSetLeak()
    {
        const double leakBytesPerThousandEdits = 5 * 1024 * 1024;
        SharedStageMemoryCheckpoint[] checkpoints = Enumerable.Range(1, 12)
            .Select(index =>
            {
                int mutations = index * 400;
                return new SharedStageMemoryCheckpoint(
                    index * 500,
                    mutations,
                    0,
                    (long)(mutations * leakBytesPerThousandEdits / 1000),
                    0,
                    0,
                    2,
                    default);
            })
            .ToArray();

        double slope = SharedStageSoak.CalculateSlope(
            checkpoints,
            checkpoint => checkpoint.WorkingSetBytes);

        await Assert.That(slope).IsGreaterThan(4 * 1024 * 1024);
    }

    [Test]
    public async Task WorkingSetSlopeAboveCeilingIsReportedButDoesNotFailMemoryGate()
    {
        SharedStageMemoryCheckpoint[] checkpoints = Enumerable.Range(1, 12)
            .Select(index =>
            {
                int mutations = index * 400;
                return new SharedStageMemoryCheckpoint(
                    index * 500,
                    mutations,
                    0,
                    (long)(mutations * 5.5 * 1024 * 1024 / 1000),
                    0,
                    0,
                    2,
                    default);
            })
            .ToArray();
        double workingSetSlope = SharedStageSoak.CalculateSlope(
            checkpoints,
            checkpoint => checkpoint.WorkingSetBytes);
        bool survived = false;
        SharedStageSoak.ValidateMemoryGrowth(
            new SharedStageSoak.MemorySnapshot(100 * 1024 * 1024, 1_000 * 1024 * 1024),
            new SharedStageSoak.MemorySnapshot(101 * 1024 * 1024, 1_020 * 1024 * 1024),
            managedSlope: 64 * 1024,
            workingSetCeiling: SharedStageSoak.ViewerWorkingSetGrowthCeilingBytes);
        survived = true;

        await Assert.That(workingSetSlope).IsGreaterThan(4 * 1024 * 1024);
        await Assert.That(survived).IsTrue();
    }

    [Test]
    public async Task HeadlessWorkingSetCeilingFailsOneMiBPerThousandEditNativeLeak()
    {
        const long sustainedLeak = 10_002L * 1024 * 1024 / 1000;
        await Assert.That(
                () => SharedStageSoak.ValidateMemoryGrowth(
                    new SharedStageSoak.MemorySnapshot(100 * 1024 * 1024, 1_000 * 1024 * 1024),
                    new SharedStageSoak.MemorySnapshot(
                        101 * 1024 * 1024,
                        1_000 * 1024 * 1024 + sustainedLeak),
                    managedSlope: 64 * 1024,
                    workingSetCeiling: SharedStageSoak.HeadlessWorkingSetGrowthCeilingBytes))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task HeadlessWorkingSetCeilingAllowsEightMiBBackstop()
    {
        bool survived = false;
        SharedStageSoak.ValidateMemoryGrowth(
            new SharedStageSoak.MemorySnapshot(100 * 1024 * 1024, 1_000 * 1024 * 1024),
            new SharedStageSoak.MemorySnapshot(
                101 * 1024 * 1024,
                1_000 * 1024 * 1024 + SharedStageSoak.HeadlessWorkingSetGrowthCeilingBytes),
            managedSlope: 64 * 1024,
            workingSetCeiling: SharedStageSoak.HeadlessWorkingSetGrowthCeilingBytes);
        survived = true;

        await Assert.That(survived).IsTrue();
    }

    [Test]
    public async Task ViewerWorkingSetCeilingFailsThirteenMiBPerThousandEditNativeLeak()
    {
        const long sustainedLeak = 10_002L * 13 * 1024 * 1024 / 1000;
        await Assert.That(
                () => SharedStageSoak.ValidateMemoryGrowth(
                    new SharedStageSoak.MemorySnapshot(100 * 1024 * 1024, 1_000 * 1024 * 1024),
                    new SharedStageSoak.MemorySnapshot(
                        101 * 1024 * 1024,
                        1_000 * 1024 * 1024 + sustainedLeak),
                    managedSlope: 64 * 1024,
                    workingSetCeiling: SharedStageSoak.ViewerWorkingSetGrowthCeilingBytes))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ViewerWorkingSetCeilingAllowsOneHundredTwentyEightMiBBackstop()
    {
        bool survived = false;
        SharedStageSoak.ValidateMemoryGrowth(
            new SharedStageSoak.MemorySnapshot(100 * 1024 * 1024, 1_000 * 1024 * 1024),
            new SharedStageSoak.MemorySnapshot(
                101 * 1024 * 1024,
                1_000 * 1024 * 1024 + SharedStageSoak.ViewerWorkingSetGrowthCeilingBytes),
            managedSlope: 64 * 1024,
            workingSetCeiling: SharedStageSoak.ViewerWorkingSetGrowthCeilingBytes);
        survived = true;

        await Assert.That(survived).IsTrue();
    }

    [Test]
    public async Task ManagedSlopeStillFailsForManagedLeak()
    {
        await Assert.That(
                () => SharedStageSoak.ValidateMemoryGrowth(
                    new SharedStageSoak.MemorySnapshot(100 * 1024 * 1024, 1_000 * 1024 * 1024),
                    new SharedStageSoak.MemorySnapshot(101 * 1024 * 1024, 1_020 * 1024 * 1024),
                    managedSlope: 256 * 1024,
                    workingSetCeiling: SharedStageSoak.ViewerWorkingSetGrowthCeilingBytes))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ReleasedResourcesRequirePostLossFrameAndNoFault()
    {
        SharedStageSoakResult missingFrame = new()
        {
            BuildIdentity = CreateIdentity(),
            ContextLossSimulated = true
        };
        await Assert.That(
            () => missingFrame.WithResourcesReleased(default, default))
            .Throws<InvalidOperationException>();

        SharedStageSoakResult faulted = missingFrame with
        {
            ContextLossSimulated = false
        };
        await Assert.That(
            () => faulted.WithResourcesReleased(
                default,
                new SharedStageRendererDiagnostics(0, 0, 1, 0, 0, 0, 0, 1)))
            .Throws<InvalidOperationException>();

        SharedStageSoakResult noPump = faulted with { StormFrames = 1 };
        await Assert.That(
            () => noPump.WithResourcesReleased(default, default))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task TargetedUpsertRejectsUnaffectedMeshChanges()
    {
        await Assert.That(SharedStageSoak.IsTargetedUpsert(
            7,
            [7],
            [],
            targetReplaced: true,
            unaffectedStable: true,
            actualColor: 0.25f,
            expectedColor: 0.25f)).IsTrue();
        await Assert.That(SharedStageSoak.IsTargetedUpsert(
            7,
            [7, 8],
            [],
            targetReplaced: true,
            unaffectedStable: false,
            actualColor: 0.25f,
            expectedColor: 0.25f)).IsFalse();
    }

    [Test]
    [NotInParallel]
    public async Task ManagedPageCounterReturnsToBaseline()
    {
        int baseline = SilkManagedDiagnostics.LivePages;
        var page = new OpenUsdSilkPage(
            SilkCommandParser.PageAbiVersion,
            1,
            [],
            0);
        await Assert.That(SilkManagedDiagnostics.LivePages).IsEqualTo(baseline + 1);

        page.Dispose();

        for (int attempt = 0;
             attempt < 200 && SilkManagedDiagnostics.LivePages != baseline;
             attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
        await Assert.That(SilkManagedDiagnostics.LivePages).IsEqualTo(baseline);
    }

    [Test]
    public async Task FinalDefaultDisplayColorIsExact()
    {
        float[] expected = SharedStageSoak.GetExpectedFinalDisplayColor();

        await Assert.That(expected).IsEquivalentTo([0.92f, 0.752f, 0.416f, 1f]);
        SharedStageSoak.ValidateFinalDisplayColor(expected, [.. expected], "default");
        await Assert.That(
            () => SharedStageSoak.ValidateFinalDisplayColor(
                expected,
                [0.92f, 0.752f, 0.417f, 1f],
                "default"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task FinalMeshIdentityRequiresExactSetAndRestoration()
    {
        SharedStageMeshIdentity[] expected =
        [
            new(10, "/World/Existing"),
            new(20, "/World/SoakMeshA"),
            new(30, "/World/SoakMeshB")
        ];
        string[] bothPaths = ["/World/SoakMeshA", "/World/SoakMeshB"];

        SharedStageSoak.ValidateFinalMeshState(expected, [.. expected], bothPaths, bothPaths);

        // A prim that came back under a new ID is still the same prim, which is what
        // Hydra actually does after a delete and re-create at the same path.
        SharedStageMeshIdentity[] reidentified =
        [
            new(10, "/World/Existing"),
            new(41, "/World/SoakMeshA"),
            new(42, "/World/SoakMeshB")
        ];
        SharedStageSoak.ValidateFinalMeshState(expected, reidentified, bothPaths, bothPaths);

        await Assert.That(
            () => SharedStageSoak.ValidateFinalMeshState(
                expected,
                [.. expected, new SharedStageMeshIdentity(40, "/World/Unexpected")],
                bothPaths,
                bothPaths))
            .Throws<InvalidOperationException>();
        await Assert.That(
            () => SharedStageSoak.ValidateFinalMeshState(
                expected,
                [.. expected],
                bothPaths,
                ["/World/SoakMeshA"]))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task BackendSourceMutationRejectsStaleEvidence()
    {
        string repositoryRoot = FindRepositoryRoot();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(
            Path.Combine(repositoryRoot, "eng", "test-shared-stage-soak-identity.ps1"));
        process.Start();
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        string output = await outputTask;
        string error = await errorTask;

        await Assert.That(process.ExitCode)
            .IsEqualTo(0)
            .Because($"stdout: {output}{Environment.NewLine}stderr: {error}");
    }

    private static SharedStageBuildIdentity CreateIdentity() => new()
    {
        SourceHash = "SOURCE",
        ExecutableHash = "EXECUTABLE",
        ExecutableTimestamp = DateTimeOffset.UnixEpoch,
        BuildHash = "BUILD"
    };

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                directory.FullName,
                "eng",
                "shared-stage-soak-identity.ps1")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static async Task<uint> ReadNativeHeaderAbiVersionAsync(
        string root,
        string relativePath,
        string pattern)
    {
        string header = await File.ReadAllTextAsync(Path.Combine(root, relativePath));
        Match match = Regex.Match(
            header,
            pattern,
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not find ABI version in {relativePath}.");
        }
        return uint.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static T GetConstant<T>(Type type, string name) =>
        (T)(type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetRawConstantValue()
            ?? throw new MissingFieldException(type.FullName, name));
}
