// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using OpenUsd.Rendering.Silk;

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
        await Assert.That(json).Contains("\"dataAbi\": 8");
        await Assert.That(json).Contains("\"stormAbi\": 4");
        await Assert.That(json).Contains("\"silkSessionAbi\": 5");
        await Assert.That(json).Contains("\"silkPageAbi\": 1");
        await Assert.That(json).Contains("\"expectedFinalMeshes\"");
        await Assert.That(json).Contains("\"actualFinalDisplayColor\"");
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
}
