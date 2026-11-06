// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using OpenUsd.Interop;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class VolumeRenderingConformanceTests
{
    private const string BudgetMaterialPath = "/World/Vdb/Volume/__hdSilkVolumeMaterial";
    private const string BudgetRefusal = "hdSilk decoded volume field admission refused";

    [Test]
    public async Task VolumeBudgetCountsEachFieldOnceUntilItsLastLeaseReleases()
    {
        (string stage, byte[] reference) = CreateBudgetFixture("shared");
        ulong bytes = (ulong)reference.Length;
        using var budget = new SilkVolumeFieldBudget(checked(bytes * 2));
        using OpenUsdSilkSession first = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stage);
        using OpenUsdSilkSession second = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stage);
        using OpenUsdSilkSession third = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stage);
        first.ConfigureVolumeFieldBudget(budget);
        second.ConfigureVolumeFieldBudget(budget);
        third.ConfigureVolumeFieldBudget(budget);

        using OpenUsdSilkPage firstPage = first.Sync(Width, Height, TimeCode);
        using var firstScene = new SilkSceneState();
        _ = firstScene.Apply(firstPage);
        using OpenUsdSilkPage secondPage = second.Sync(Width, Height, TimeCode);
        OpenUsdSilkPage.ReplaySnapshot replay = firstPage.CaptureReplay();
        try
        {
            SilkVolumeFieldUsage full = budget.Usage;
            await Assert.That(full.MaximumBytes).IsEqualTo(bytes * 2);
            await Assert.That(full.ReservedBytes).IsEqualTo(bytes * 2);
            await Assert.That(full.ReservationCount).IsEqualTo(2ul);
            first.Dispose();
            firstPage.Dispose();
            firstScene.Dispose();
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(bytes * 2)
                .Because("the replay snapshot still owns the first field after the page, scene and session close.");

            OpenUsdSilkException? refused = await Assert.That(() =>
            {
                using OpenUsdSilkPage ignored = third.Sync(Width, Height, TimeCode);
            }).Throws<OpenUsdSilkException>();
            await Assert.That(refused!.Message).Contains(BudgetRefusal);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(bytes * 2);
            await Assert.That(budget.Usage.ReservationCount).IsEqualTo(2ul);

            using OpenUsdSilkPage replayPage = replay.CreatePage();
            using var replayScene = new SilkSceneState();
            _ = replayScene.Apply(replayPage);
            SilkMaterialTexture texture = replayScene.Materials[BudgetMaterialPath]
                .GetTexture(SilkMaterialParameter.VolumeDensity)!;
            await Assert.That(ReadVolumeDensity(texture).SequenceEqual(reference)).IsTrue();
            await Assert.That(full.ReservedBytes).IsEqualTo(bytes * 2)
                .Because("an earlier usage snapshot is immutable.");
        }
        finally
        {
            replay.Dispose();
        }

        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(bytes);
        using (OpenUsdSilkPage recovered = third.Sync(Width, Height, TimeCode))
        using (var scene = new SilkSceneState())
        {
            _ = scene.Apply(recovered);
            SilkMaterialTexture texture = scene.Materials[BudgetMaterialPath]
                .GetTexture(SilkMaterialParameter.VolumeDensity)!;
            await Assert.That(ReadVolumeDensity(texture).SequenceEqual(reference)).IsTrue();
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(bytes * 2);
        }
        secondPage.Dispose();
        second.Dispose();
        third.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(bytes * 2);
    }

    [Test]
    public async Task VolumeBudgetRejectsBelowFieldSizeAndCannotBeReplacedOrAppliedRetroactively()
    {
        (string stage, byte[] reference) = CreateBudgetFixture("configuration");
        ulong bytes = (ulong)reference.Length;
        await Assert.That(() =>
        {
            using var invalid = new SilkVolumeFieldBudget(0);
        }).Throws<ArgumentOutOfRangeException>();

        using var budget = new SilkVolumeFieldBudget(bytes - 1);
        using var other = new SilkVolumeFieldBudget(bytes);
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stage);
        session.ConfigureVolumeFieldBudget(budget);
        session.ConfigureVolumeFieldBudget(budget);
        OpenUsdSilkException? replaced = await Assert.That(() => session.ConfigureVolumeFieldBudget(other))
            .Throws<OpenUsdSilkException>();
        await Assert.That(replaced!.Status).IsEqualTo(OpenUsdNativeStatus.InvalidArgument);
        OpenUsdSilkException? refused = await Assert.That(() =>
        {
            using OpenUsdSilkPage ignored = session.Sync(Width, Height, TimeCode);
        }).Throws<OpenUsdSilkException>();
        await Assert.That(refused!.Message).Contains(BudgetRefusal);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        session.ConfigureVolumeFieldBudget(budget);
        await Assert.That(() => session.ConfigureVolumeFieldBudget(other)).Throws<OpenUsdSilkException>();

        using OpenUsdSilkSession unbounded = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stage);
        using OpenUsdSilkPage page = unbounded.Sync(Width, Height, TimeCode);
        OpenUsdSilkException? retroactive = await Assert.That(() => unbounded.ConfigureVolumeFieldBudget(other))
            .Throws<OpenUsdSilkException>();
        await Assert.That(retroactive!.Status).IsEqualTo(OpenUsdNativeStatus.InvalidArgument);
        using var scene = new SilkSceneState();
        _ = scene.Apply(page);
        await Assert.That(ReadVolumeDensity(scene.Materials[BudgetMaterialPath]
            .GetTexture(SilkMaterialParameter.VolumeDensity)!).SequenceEqual(reference)).IsTrue();
    }

    [Test]
    public async Task VolumeBudgetConcurrentSessionsCannotOvercommitTheirSharedCeiling()
    {
        (string stage, byte[] reference) = CreateBudgetFixture("concurrent");
        ulong bytes = (ulong)reference.Length;
        using var budget = new SilkVolumeFieldBudget(checked(bytes * 2));
        var sessions = new List<OpenUsdSilkSession>();
        var pages = new OpenUsdSilkPage?[6];
        try
        {
            for (int index = 0; index < 6; index++)
            {
                OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stage);
                session.ConfigureVolumeFieldBudget(budget);
                sessions.Add(session);
            }
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int refused = 0;
            Task[] attempts = sessions.Select((session, index) => Task.Run(async () =>
            {
                await start.Task.ConfigureAwait(false);
                try
                {
                    pages[index] = session.Sync(Width, Height, TimeCode);
                }
                catch (OpenUsdSilkException error) when (error.Message.Contains(
                    BudgetRefusal, StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref refused);
                }
            })).ToArray();
            start.SetResult();
            await Task.WhenAll(attempts).ConfigureAwait(false);
            await Assert.That(pages.Count(static page => page is not null)).IsEqualTo(2);
            await Assert.That(refused).IsEqualTo(4);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(bytes * 2);
            await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(bytes * 2);
            await Assert.That(budget.Usage.ReservationCount).IsEqualTo(2ul);
        }
        finally
        {
            foreach (OpenUsdSilkPage? page in pages)
            {
                page?.Dispose();
            }
            foreach (OpenUsdSilkSession session in sessions)
            {
                session.Dispose();
            }
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    public async Task VolumeBudgetFailedMultiVolumeSyncReleasesPartialFieldsAndRetriesTheSameSession()
    {
        (_, byte[] reference) = CreateBudgetFixture("partial-reference");
        ulong bytes = (ulong)reference.Length;
        string asset = ResolveOpenVdbAsset("sampled_density_unit.vdb").Replace("\\", "/", StringComparison.Ordinal);
        string stage = Path.Combine(StageDirectory, "volume-budget-partial.usda");
        File.WriteAllText(stage, $$"""
            #usda 1.0
            (
                defaultPrim = "World"
                startTimeCode = 1
                endTimeCode = 2
            )
            def Xform "World"
            {
                def Volume "First"
                {
                    rel field:density = </World/First/Density>
                    def OpenVDBAsset "Density"
                    {
                        asset filePath = @{{asset}}@
                        token fieldName = "density"
                    }
                }
                def Volume "Second"
                {
                    custom bool hdsilk:sampleDensity.timeSamples = { 1: true, 2: false }
                    rel field:density = </World/Second/Density>
                    def OpenVDBAsset "Density"
                    {
                        asset filePath = @{{asset}}@
                        token fieldName = "density"
                    }
                }
            }
            """, new UTF8Encoding(false));
        using var budget = new SilkVolumeFieldBudget(bytes);
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stage);
        session.ConfigureVolumeFieldBudget(budget);
        OpenUsdSilkException? refused = await Assert.That(() =>
        {
            using OpenUsdSilkPage ignored = session.Sync(Width, Height, 1);
        }).Throws<OpenUsdSilkException>();
        await Assert.That(refused!.Message).Contains(BudgetRefusal);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(bytes)
            .Because("the first field was admitted before the second field refused the sync.");

        using (OpenUsdSilkPage page = session.Sync(Width, Height, 2))
        using (var scene = new SilkSceneState())
        {
            _ = scene.Apply(page);
            SilkMaterialTexture first = scene.Materials["/World/First/__hdSilkVolumeMaterial"]
                .GetTexture(SilkMaterialParameter.VolumeDensity)!;
            await Assert.That(ReadVolumeDensity(first).SequenceEqual(reference)).IsTrue();
            await Assert.That(scene.Materials["/World/Second/__hdSilkVolumeMaterial"]
                .GetTexture(SilkMaterialParameter.VolumeDensity)).IsNull();
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(bytes);
        }
        session.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task VolumeBudgetDisposedHandleDoesNotDisableConfiguredSessionOrInvalidatePage()
    {
        (string stage, byte[] reference) = CreateBudgetFixture("handle");
        using var budget = new SilkVolumeFieldBudget((ulong)reference.Length);
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stage);
        session.ConfigureVolumeFieldBudget(budget);
        using OpenUsdSilkPage first = session.Sync(Width, Height, TimeCode);
        budget.Dispose();
        await Assert.That(() =>
        {
            _ = budget.Usage;
        }).Throws<ObjectDisposedException>();
        await Assert.That(() => session.ConfigureVolumeFieldBudget(budget)).Throws<ObjectDisposedException>();
        OpenUsdSilkException? refused = await Assert.That(() =>
        {
            using OpenUsdSilkPage ignored = session.Sync(Width, Height, TimeCode);
        }).Throws<OpenUsdSilkException>();
        await Assert.That(refused!.Message).Contains(BudgetRefusal);
        using (var scene = new SilkSceneState())
        {
            _ = scene.Apply(first);
            await Assert.That(ReadVolumeDensity(scene.Materials[BudgetMaterialPath]
                .GetTexture(SilkMaterialParameter.VolumeDensity)!).SequenceEqual(reference)).IsTrue();
        }
        first.Dispose();
        using OpenUsdSilkPage recovered = session.Sync(Width, Height, TimeCode);
        session.Dispose();
        using var recoveredScene = new SilkSceneState();
        _ = recoveredScene.Apply(recovered);
        await Assert.That(ReadVolumeDensity(recoveredScene.Materials[BudgetMaterialPath]
            .GetTexture(SilkMaterialParameter.VolumeDensity)!).SequenceEqual(reference)).IsTrue();
    }

    [Test]
    public async Task VolumeBudgetStorageFailureReleasesReservationAndLaterSyncCanRetry()
    {
        (string stage, byte[] reference) = CreateBudgetFixture("storage");
        ulong bytes = (ulong)reference.Length;
        using var budget = new SilkVolumeFieldBudget(bytes);
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stage);
        session.ConfigureVolumeFieldBudget(budget);
        string blocked = Path.Combine(StageDirectory, "volume-budget-not-a-directory");
        File.WriteAllText(blocked, "not a directory");
        OpenUsdSilkPage? fallback = null;
        try
        {
            using (new NativeTemporaryDirectoryScope(blocked))
            {
                fallback = session.Sync(Width, Height, TimeCode);
            }
            using var scene = new SilkSceneState();
            _ = scene.Apply(fallback);
            await Assert.That(scene.Materials[BudgetMaterialPath]
                .GetTexture(SilkMaterialParameter.VolumeDensity)).IsNull();
        }
        finally
        {
            try
            {
                fallback?.Dispose();
            }
            finally
            {
                File.Delete(blocked);
            }
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(bytes);
        using (OpenUsdSilkPage recovered = session.Sync(Width, Height, TimeCode))
        using (var scene = new SilkSceneState())
        {
            _ = scene.Apply(recovered);
            await Assert.That(ReadVolumeDensity(scene.Materials[BudgetMaterialPath]
                .GetTexture(SilkMaterialParameter.VolumeDensity)!).SequenceEqual(reference)).IsTrue();
        }
        session.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task VolumeBudgetAppliesToExplicitIngestionAndReleasesExcludedFields()
    {
        (_, byte[] reference) = CreateBudgetFixture("purposes-reference");
        string asset = ResolveOpenVdbAsset("sampled_density_unit.vdb");
        string stage = Path.Combine(StageDirectory, "volume-budget-purposes.usda");
        string source = CreateVdbStage(asset, 0, 1, sampleDensity: true).Replace(
            "custom float hdsilk:density",
            "uniform token purpose = \"render\"\n          custom float hdsilk:density",
            StringComparison.Ordinal);
        File.WriteAllText(stage, source, new UTF8Encoding(false));
        using var budget = new SilkVolumeFieldBudget((ulong)reference.Length);
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stage);
        session.ConfigureVolumeFieldBudget(budget);
        var excluded = new SilkSceneIngestionOptions(RenderPurpose.Default, "full");
        var included = new SilkSceneIngestionOptions(RenderPurpose.Default | RenderPurpose.Render, "full");
        using var scene = new SilkSceneState();

        using (OpenUsdSilkPage page = session.Sync(Width, Height, excluded, TimeCode))
        {
            _ = scene.Apply(page);
            await Assert.That(scene.Materials.ContainsKey(BudgetMaterialPath)).IsFalse();
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        using OpenUsdSilkPage sampled = session.Sync(Width, Height, included, TimeCode);
        _ = scene.Apply(sampled);
        await Assert.That(ReadVolumeDensity(scene.Materials[BudgetMaterialPath]
            .GetTexture(SilkMaterialParameter.VolumeDensity)!).SequenceEqual(reference)).IsTrue();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo((ulong)reference.Length);

        using (OpenUsdSilkPage page = session.Sync(Width, Height, excluded, TimeCode))
        {
            _ = scene.Apply(page);
            await Assert.That(scene.Materials.ContainsKey(BudgetMaterialPath)).IsFalse();
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo((ulong)reference.Length)
                .Because("the earlier sampled page still owns its field.");
        }
        sampled.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        using OpenUsdSilkPage restored = session.Sync(Width, Height, included, TimeCode);
        _ = scene.Apply(restored);
        await Assert.That(ReadVolumeDensity(scene.Materials[BudgetMaterialPath]
            .GetTexture(SilkMaterialParameter.VolumeDensity)!).SequenceEqual(reference)).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.D3D12)]
    public async Task VolumeBudgetedSampledPixelsMatchUnboundedAfterSessionDisposal(SilkGraphicsBackend backend)
    {
        if (backend == SilkGraphicsBackend.D3D12 && !OperatingSystem.IsWindows())
        {
            Skip.Test("Direct3D 12 is available only on Windows.");
        }
        (string stage, byte[] expectedDensity) = CreateBudgetFixture($"render-{backend}");
        Func<ISilkGraphicsDevice> createDevice = backend == SilkGraphicsBackend.D3D12
            ? CreateD3D12WarpDevice
            : VulkanSilkGraphicsDevice.Create;
        ParityImage reference;
        try
        {
            reference = CaptureStage(stage, createDevice);
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"The graphics backend is unavailable: {exception.Message}");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.", exception);
        }
        using var budget = new SilkVolumeFieldBudget((ulong)expectedDensity.Length);
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stage);
        session.ConfigureVolumeFieldBudget(budget);
        using OpenUsdSilkPage page = session.Sync(
            Width, Height, TimeCode, CreateVolumeFixtureCamera());
        session.Dispose();
        ParityImage actual = CapturePage(page, createDevice, out byte[]? density);
        await Assert.That(density!.SequenceEqual(expectedDensity)).IsTrue();
        await Assert.That(ImageDelta.Compare(reference, actual).MaximumChannelDelta).IsEqualTo((byte)0);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo((ulong)expectedDensity.Length);
        page.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    private static (string Stage, byte[] Density) CreateBudgetFixture(string name)
    {
        PrependHdSilkNativeSearchPath();
        string stage = WriteVdbStage(
            $"volume-budget-{name}", ResolveOpenVdbAsset("sampled_density_unit.vdb"), 0, 1, sampleDensity: true);
        try
        {
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(ResolvePluginPath(), stage);
            using OpenUsdSilkPage page = session.Sync(
                Width, Height, TimeCode, CreateVolumeFixtureCamera());
            using var scene = new SilkSceneState();
            _ = scene.Apply(page);
            SilkMaterialTexture? texture = scene.Materials[BudgetMaterialPath]
                .GetTexture(SilkMaterialParameter.VolumeDensity);
            if (texture is null)
            {
                Skip.Test("hioOpenVDB reader is unavailable in the native profile.");
                throw new InvalidOperationException("Skip.Test returned unexpectedly.");
            }
            return (stage, ReadVolumeDensity(texture));
        }
        catch (Exception exception) when (exception is DllNotFoundException or DirectoryNotFoundException)
        {
            Skip.Test($"The hdSilk native runtime is unavailable: {exception.Message}");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.", exception);
        }
    }
}
