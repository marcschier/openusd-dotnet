// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Proves the Silk texture cache stays within configured decoded-CPU and estimated-GPU byte
/// budgets via deterministic least-recently-used eviction restricted to entries not touched since
/// the previous trim (protecting the current frame's working set from decode/upload thrash), and
/// that eviction never disposes a texture until it is safe to do so (after the submission that may
/// reference it has completed).
/// </summary>
public sealed class SilkTextureResidencyTests
{
    // Learned once, deterministically, from the real (pure, CPU-only) mip-generation and
    // fallback-image logic, so tests can pass tight residency budgets to the constructor up front
    // instead of mutating a running instance's options.
    private static readonly ulong OneOrdinaryTextureUploadBytes = ComputeOneOrdinaryTextureUploadBytes();
    private static readonly ulong FailedFallbackUploadBytes = ComputeFailedFallbackUploadBytes();

    // ---------------------------------------------------------------------
    // SilkTextureResidencyOptions: validation and defaults.
    // ---------------------------------------------------------------------

    [Test]
    public async Task DefaultOptionsAre512MebibytesForBothBudgets()
    {
        SilkTextureResidencyOptions options = SilkTextureResidencyOptions.Default;

        await Assert.That(options.MaxDecodedCpuBytes).IsEqualTo(512UL * 1024 * 1024);
        await Assert.That(options.MaxGpuBytes).IsEqualTo(512UL * 1024 * 1024);
        await Assert.That(options.MaxDecodedCpuBytes)
            .IsEqualTo(SilkTextureResidencyOptions.DefaultMaxDecodedCpuBytes);
        await Assert.That(options.MaxGpuBytes)
            .IsEqualTo(SilkTextureResidencyOptions.DefaultMaxGpuBytes);
        await Assert.That(new SilkTextureResidencyOptions().MaxDecodedCpuBytes)
            .IsEqualTo(options.MaxDecodedCpuBytes);
    }

    [Test]
    public async Task ZeroDecodedCpuBudgetIsRejected()
    {
        ArgumentOutOfRangeException exception = (await Assert.That(
            () => new SilkTextureResidencyOptions(maxDecodedCpuBytes: 0))
            .Throws<ArgumentOutOfRangeException>())!;

        await Assert.That(exception.ParamName).IsEqualTo("maxDecodedCpuBytes");
    }

    [Test]
    public async Task ZeroGpuBudgetIsRejected()
    {
        ArgumentOutOfRangeException exception = (await Assert.That(
            () => new SilkTextureResidencyOptions(maxGpuBytes: 0))
            .Throws<ArgumentOutOfRangeException>())!;

        await Assert.That(exception.ParamName).IsEqualTo("maxGpuBytes");
    }

    [Test]
    public async Task ExplicitBudgetsAreIndependentlyConfigurable()
    {
        var options = new SilkTextureResidencyOptions(
            maxDecodedCpuBytes: 4096,
            maxGpuBytes: 8192);

        await Assert.That(options.MaxDecodedCpuBytes).IsEqualTo(4096UL);
        await Assert.That(options.MaxGpuBytes).IsEqualTo(8192UL);
    }

    // ---------------------------------------------------------------------
    // Accounting: ordinary mip chains, fallbacks, UDIM atlases, and volumes are all retained.
    // ---------------------------------------------------------------------

    [Test]
    public async Task OrdinaryTextureDecodedAndGpuBytesBothMatchTheUploadedMipChainAndAreRetained()
    {
        SilkMaterialData material = CreateOneTextureMaterial(
            "/World/Materials/Brick",
            SilkMaterialParameter.DiffuseColor,
            "brick.png");
        using var device = new TextureDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)));
        using var commands = new TextureCommandList();

        resources.UploadMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        // Base 2x2 level plus its 1x1 mip: the mip chain the GPU byte estimate must match.
        ulong uploadedBytes = checked((ulong)commands.Uploads.Single().Length);
        await Assert.That(uploadedBytes).IsGreaterThan(16UL);
        SilkSceneGpuStatistics statistics = resources.Statistics;
        await Assert.That(statistics.TextureResidentGpuBytes).IsEqualTo(uploadedBytes);
        await Assert.That(statistics.TextureCacheEntryCount).IsEqualTo(1);
        // Decoded CPU bytes are retained (not released immediately after upload): only safe LRU
        // eviction frees them, which is what makes the decoded CPU budget independently
        // effective rather than one that only ever measures a near-zero residency.
        await Assert.That(statistics.TextureResidentDecodedBytes).IsEqualTo(uploadedBytes);
        await Assert.That(statistics.PeakTextureResidentDecodedBytes).IsEqualTo(uploadedBytes);
        await Assert.That(statistics.PeakTextureResidentGpuBytes).IsEqualTo(uploadedBytes);
    }

    [Test]
    public async Task FallbackTextureIsAccountedAsARetainedCacheEntry()
    {
        SilkMaterialData material = CreateOneTextureMaterial(
            "/World/Materials/Missing",
            SilkMaterialParameter.DiffuseColor,
            "missing.png");
        using var device = new TextureDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => throw new FileNotFoundException("Texture is absent.", "missing.png"));
        using var commands = new TextureCommandList();

        resources.UploadMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        ulong uploadedBytes = checked((ulong)commands.Uploads.Single().Length);
        SilkSceneGpuStatistics statistics = resources.Statistics;
        await Assert.That(statistics.TextureCacheEntryCount).IsEqualTo(1);
        await Assert.That(statistics.TextureResidentGpuBytes).IsEqualTo(uploadedBytes);
        await Assert.That(statistics.TextureResidentDecodedBytes).IsEqualTo(uploadedBytes);
    }

    [Test]
    public async Task UdimAtlasStaysSingleLevelInByteAccountingAndIsRetained()
    {
        SilkMaterialData material = CreateOneTextureMaterial(
            "/World/Materials/Udim",
            SilkMaterialParameter.DiffuseColor,
            "tiles.<UDIM>.png");
        using var device = new TextureDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(2, 1, [255, 0, 0, 255, 255, 0, 0, 255]),
            _ => [new SilkUdimTile(1001, "tiles.1001.png")]);
        using var commands = new TextureCommandList();

        resources.UploadMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        // A UDIM atlas is never mip-mapped, so its GPU bytes equal exactly the uploaded atlas
        // payload rather than a base-plus-mip-chain total, and it is retained like every other
        // cache entry.
        ulong uploadedBytes = checked((ulong)commands.Uploads.Single().Length);
        SilkSceneGpuStatistics statistics = resources.Statistics;
        await Assert.That(statistics.TextureResidentGpuBytes).IsEqualTo(uploadedBytes);
        await Assert.That(statistics.TextureResidentDecodedBytes).IsEqualTo(uploadedBytes);
    }

    [Test]
    public async Task VolumeTextureBytesMatchWidthHeightDepthAndAreRetainedAfterUpload()
    {
        string asset = Path.GetTempFileName();
        try
        {
            byte[] volumeBytes = new byte[2 * 2 * 1 * sizeof(float)];
            await File.WriteAllBytesAsync(asset, volumeBytes);
            SilkMaterialData material = CreateOneTextureMaterial(
                "/World/Materials/Fog",
                SilkMaterialParameter.VolumeDensity,
                asset,
                uvPrimvar: "2,2,1");
            using var device = new TextureDevice();
            using var resources = new SilkSceneGpuResources(device, (_, _) =>
                throw new NotSupportedException("Volume textures do not use the image decoder."));
            using var commands = new TextureCommandList();

            resources.UploadVolumeDensityTexture(commands, material);

            SilkSceneGpuStatistics statistics = resources.Statistics;
            await Assert.That(statistics.TextureCacheEntryCount).IsEqualTo(1);
            await Assert.That(statistics.TextureResidentGpuBytes)
                .IsEqualTo(checked((ulong)volumeBytes.Length));
            await Assert.That(statistics.TextureResidentDecodedBytes)
                .IsEqualTo(checked((ulong)volumeBytes.Length));
            await Assert.That(commands.VolumeUploads.Single().Length).IsEqualTo(volumeBytes.Length);
        }
        finally
        {
            File.Delete(asset);
        }
    }

    // ---------------------------------------------------------------------
    // The decoded CPU budget is independently effective now that bytes are retained.
    // ---------------------------------------------------------------------

    [Test]
    public async Task TinyDecodedCpuBudgetAloneEvictsAStaleEntryRegardlessOfAGenerousGpuBudget()
    {
        using var device = new TextureDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)),
            residencyOptions: new SilkTextureResidencyOptions(
                maxDecodedCpuBytes: OneOrdinaryTextureUploadBytes + (OneOrdinaryTextureUploadBytes / 2),
                maxGpuBytes: ulong.MaxValue));
        using var commands = new TextureCommandList();
        SilkMaterialData materialA = CreateOneTextureMaterial(
            "/World/Materials/A", SilkMaterialParameter.DiffuseColor, "a.png");
        SilkMaterialData materialB = CreateOneTextureMaterial(
            "/World/Materials/B", SilkMaterialParameter.DiffuseColor, "b.png");

        // Frame 1: only A exists; it is this frame's whole working set and fits comfortably.
        resources.UploadMaterialTexture(commands, materialA, SilkMaterialParameter.DiffuseColor);
        resources.TrimTextureResidency();
        await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(0UL);

        // Frame 2: B is newly referenced (pinned this frame). A, not touched this frame, is now
        // stale, and with both entries present the decoded CPU budget alone is exceeded.
        resources.UploadMaterialTexture(commands, materialB, SilkMaterialParameter.DiffuseColor);
        resources.TrimTextureResidency();

        await Assert.That(device.AllTextures[0].DisposeCount).IsEqualTo(1);
        await Assert.That(device.AllTextures[1].DisposeCount).IsEqualTo(0);
        await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(1UL);
        await Assert.That(resources.Statistics.TextureCacheEntryCount).IsEqualTo(1);
    }

    [Test]
    public async Task GpuBudgetAloneCanDriveEvictionRegardlessOfAGenerousDecodedCpuBudget()
    {
        using var device = new TextureDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)),
            residencyOptions: new SilkTextureResidencyOptions(
                maxDecodedCpuBytes: ulong.MaxValue,
                maxGpuBytes: OneOrdinaryTextureUploadBytes + (OneOrdinaryTextureUploadBytes / 2)));
        using var commands = new TextureCommandList();
        SilkMaterialData materialA = CreateOneTextureMaterial(
            "/World/Materials/A", SilkMaterialParameter.DiffuseColor, "a.png");
        SilkMaterialData materialB = CreateOneTextureMaterial(
            "/World/Materials/B", SilkMaterialParameter.DiffuseColor, "b.png");

        resources.UploadMaterialTexture(commands, materialA, SilkMaterialParameter.DiffuseColor);
        resources.TrimTextureResidency();
        resources.UploadMaterialTexture(commands, materialB, SilkMaterialParameter.DiffuseColor);
        resources.TrimTextureResidency();

        await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(1UL);
        await Assert.That(resources.Statistics.TextureCacheEntryCount).IsEqualTo(1);
    }

    // ---------------------------------------------------------------------
    // Deterministic LRU eviction and the frame working-set protection boundary.
    // ---------------------------------------------------------------------

    [Test]
    public async Task NoEvictionOccursUntilTrimIsCalledExplicitly()
    {
        using var device = new TextureDevice();
        // A budget smaller than either texture guarantees the working set is over budget the
        // instant both textures are recorded, proving recording alone never evicts.
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)),
            residencyOptions: new SilkTextureResidencyOptions(maxDecodedCpuBytes: 1, maxGpuBytes: 1));
        using var commands = new TextureCommandList();

        resources.UploadMaterialTexture(
            commands,
            CreateOneTextureMaterial("/World/Materials/A", SilkMaterialParameter.DiffuseColor, "a.png"),
            SilkMaterialParameter.DiffuseColor);
        resources.UploadMaterialTexture(
            commands,
            CreateOneTextureMaterial("/World/Materials/B", SilkMaterialParameter.DiffuseColor, "b.png"),
            SilkMaterialParameter.DiffuseColor);

        await Assert.That(resources.Statistics.TextureCacheEntryCount).IsEqualTo(2);
        await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(0UL);
        await Assert.That(device.AllTextures.Count(texture => texture.DisposeCount != 0))
            .IsEqualTo(0);
    }

    [Test]
    public async Task StaleLeastRecentlyUsedEntryIsEvictedOnceItLeavesTheCurrentFrameWorkingSet()
    {
        using var device = new TextureDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)),
            residencyOptions: new SilkTextureResidencyOptions(
                maxDecodedCpuBytes: ulong.MaxValue,
                maxGpuBytes: OneOrdinaryTextureUploadBytes + (OneOrdinaryTextureUploadBytes / 2)));
        using var commands = new TextureCommandList();
        SilkMaterialData materialA = CreateOneTextureMaterial(
            "/World/Materials/A", SilkMaterialParameter.DiffuseColor, "a.png");
        SilkMaterialData materialB = CreateOneTextureMaterial(
            "/World/Materials/B", SilkMaterialParameter.DiffuseColor, "b.png");

        // Frame 1: A alone is this frame's working set and fits.
        resources.UploadMaterialTexture(commands, materialA, SilkMaterialParameter.DiffuseColor);
        resources.TrimTextureResidency();

        // Frame 2: B is newly referenced. A, unreferenced this frame, is now stale and, with both
        // entries present, the tight GPU budget is exceeded, so A (not B) is evicted.
        resources.UploadMaterialTexture(commands, materialB, SilkMaterialParameter.DiffuseColor);
        resources.TrimTextureResidency();

        await Assert.That(device.AllTextures[0].DisposeCount).IsEqualTo(1);
        await Assert.That(device.AllTextures[1].DisposeCount).IsEqualTo(0);
        await Assert.That(resources.Statistics.TextureCacheEntryCount).IsEqualTo(1);
        await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(1UL);
    }

    [Test]
    public async Task TouchingAnEntryOnCacheHitKeepsItOutOfTheNextStaleEvictionPool()
    {
        using var device = new TextureDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)),
            residencyOptions: new SilkTextureResidencyOptions(
                maxDecodedCpuBytes: ulong.MaxValue,
                maxGpuBytes: OneOrdinaryTextureUploadBytes + (OneOrdinaryTextureUploadBytes / 2)));
        using var commands = new TextureCommandList();
        SilkMaterialData materialA = CreateOneTextureMaterial(
            "/World/Materials/A", SilkMaterialParameter.DiffuseColor, "a.png");
        SilkMaterialData materialB = CreateOneTextureMaterial(
            "/World/Materials/B", SilkMaterialParameter.DiffuseColor, "b.png");

        // Frame 1: both A and B are created together. The very first trim always pins everything
        // touched so far (there is no earlier trim boundary to be stale relative to), so nothing
        // is evicted yet even though two entries already exceed the tight GPU budget.
        resources.UploadMaterialTexture(commands, materialA, SilkMaterialParameter.DiffuseColor);
        resources.UploadMaterialTexture(commands, materialB, SilkMaterialParameter.DiffuseColor);
        resources.TrimTextureResidency();
        await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(0UL);

        // Frame 2: re-requesting A's texture is a cache hit that bumps only A's last-use stamp
        // above the boundary the previous trim recorded; B, not referenced this frame, is now the
        // only stale candidate.
        resources.UploadMaterialTexture(commands, materialA, SilkMaterialParameter.DiffuseColor);
        resources.TrimTextureResidency();

        await Assert.That(device.AllTextures[0].DisposeCount).IsEqualTo(0);
        await Assert.That(device.AllTextures[1].DisposeCount).IsEqualTo(1);
        await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(1UL);
    }

    [Test]
    public async Task StaleFailedFallbacksAreEvictedOnlyAsALastResortAfterOrdinaryCandidates()
    {
        ulong tightGpuBudget = checked(FailedFallbackUploadBytes + OneOrdinaryTextureUploadBytes + 1);
        using var device = new TextureDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (assetPath, _) => assetPath.Contains("missing", StringComparison.Ordinal)
                ? throw new FileNotFoundException("Texture is absent.", assetPath)
                : new SilkDecodedImage(2, 2, CreatePixels(2, 2)),
            residencyOptions: new SilkTextureResidencyOptions(
                maxDecodedCpuBytes: ulong.MaxValue,
                maxGpuBytes: tightGpuBudget));
        using var commands = new TextureCommandList();

        // Frame 1: a failed-fallback entry (oldest by last use) and an ordinary entry are both
        // created together and are this frame's whole working set; nothing is evicted yet.
        resources.UploadMaterialTexture(
            commands,
            CreateOneTextureMaterial(
                "/World/Materials/Fallback", SilkMaterialParameter.DiffuseColor, "missing.png"),
            SilkMaterialParameter.DiffuseColor);
        resources.UploadMaterialTexture(
            commands,
            CreateOneTextureMaterial(
                "/World/Materials/Ordinary", SilkMaterialParameter.DiffuseColor, "ordinary.png"),
            SilkMaterialParameter.DiffuseColor);
        resources.TrimTextureResidency();
        await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(0UL);

        // Frame 2: a second ordinary entry is referenced; the fallback and first ordinary entry,
        // untouched this frame, both become stale. The fallback is the older of the two by last
        // use, but ordinary (and volume) candidates are always preferred over a stale fallback, so
        // exactly the first ordinary entry — not the fallback — is evicted to fit the budget.
        resources.UploadMaterialTexture(
            commands,
            CreateOneTextureMaterial(
                "/World/Materials/OrdinaryTwo", SilkMaterialParameter.DiffuseColor, "b.png"),
            SilkMaterialParameter.DiffuseColor);
        resources.TrimTextureResidency();

        await Assert.That(device.AllTextures[0].DisposeCount).IsEqualTo(0); // fallback survives.
        await Assert.That(device.AllTextures[1].DisposeCount).IsEqualTo(1); // first ordinary evicted.
        await Assert.That(device.AllTextures[2].DisposeCount).IsEqualTo(0); // second ordinary (this frame) survives.
        await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(1UL);
    }

    [Test]
    public async Task TrimEvictsTheOldestStaleEntryAcrossOrdinaryAndVolumeCaches()
    {
        string asset = Path.GetTempFileName();
        try
        {
            byte[] volumeBytes = new byte[2 * 2 * 1 * sizeof(float)];
            await File.WriteAllBytesAsync(asset, volumeBytes);
            SilkMaterialData ordinaryMaterial = CreateOneTextureMaterial(
                "/World/Materials/Ordinary", SilkMaterialParameter.DiffuseColor, "ordinary.png");
            SilkMaterialData volumeMaterial = CreateOneTextureMaterial(
                "/World/Materials/Volume", SilkMaterialParameter.VolumeDensity, asset, uvPrimvar: "2,2,1");

            // The exact ordinary and volume upload sizes are already known without decoding
            // anything: the ordinary upload size was learned once for the whole test class (see
            // OneOrdinaryTextureUploadBytes), and the volume upload is always the whole asset
            // file's bytes verbatim (see RequireVolumeTexture). A GPU budget that fits only one
            // of the two entries makes each scenario below force exactly one eviction.
            ulong ordinaryBytes = OneOrdinaryTextureUploadBytes;
            ulong volumeAssetBytes = checked((ulong)volumeBytes.Length);
            var tightBudget = new SilkTextureResidencyOptions(
                maxDecodedCpuBytes: ulong.MaxValue,
                maxGpuBytes: volumeAssetBytes + (ordinaryBytes / 2));

            // Scenario 1: an ordinary entry (created first) and a volume entry are both frame 1's
            // working set; only the volume entry is referenced again in frame 2, so the ordinary
            // entry -- unreferenced this frame -- is the sole stale candidate. Neither entry is a
            // failed fallback, so plain LRU order applies and the ordinary entry is evicted while
            // the volume entry survives.
            using (var device = new TextureDevice())
            using (var resources = new SilkSceneGpuResources(
                device,
                (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)),
                residencyOptions: tightBudget))
            using (var commands = new TextureCommandList())
            {
                resources.UploadMaterialTexture(commands, ordinaryMaterial, SilkMaterialParameter.DiffuseColor);
                resources.UploadVolumeDensityTexture(commands, volumeMaterial);
                resources.TrimTextureResidency();
                await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(0UL);

                resources.UploadVolumeDensityTexture(commands, volumeMaterial);
                resources.TrimTextureResidency();

                await Assert.That(device.AllTextures[0].DisposeCount).IsEqualTo(1); // ordinary evicted.
                await Assert.That(device.AllTextures[1].DisposeCount).IsEqualTo(0); // volume survives.
                await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(1UL);
            }

            // Scenario 2 (mirror of scenario 1): only the ordinary entry is referenced again in
            // frame 2, so the volume entry -- unreferenced this frame -- is now the sole stale
            // candidate and is the one evicted. This directly exercises the
            // TextureCacheEntryKind.Volume branch of RemoveEvictionCandidate, which scenario 1
            // above never reaches.
            using (var device = new TextureDevice())
            using (var resources = new SilkSceneGpuResources(
                device,
                (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)),
                residencyOptions: tightBudget))
            using (var commands = new TextureCommandList())
            {
                resources.UploadMaterialTexture(commands, ordinaryMaterial, SilkMaterialParameter.DiffuseColor);
                resources.UploadVolumeDensityTexture(commands, volumeMaterial);
                resources.TrimTextureResidency();
                await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(0UL);

                resources.UploadMaterialTexture(commands, ordinaryMaterial, SilkMaterialParameter.DiffuseColor);
                resources.TrimTextureResidency();

                await Assert.That(device.AllTextures[0].DisposeCount).IsEqualTo(0); // ordinary survives.
                await Assert.That(device.AllTextures[1].DisposeCount).IsEqualTo(1); // volume evicted.
                await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(1UL);
                await Assert.That(resources.Statistics.TextureCacheEntryCount).IsEqualTo(1);
            }
        }
        finally
        {
            File.Delete(asset);
        }
    }

    [Test]
    public async Task OversizedSingleStaleEntryIsEvictedWithoutLoopingAndEmitsABoundedDiagnostic()
    {
        using var device = new TextureDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)),
            residencyOptions: new SilkTextureResidencyOptions(
                maxDecodedCpuBytes: OneOrdinaryTextureUploadBytes,
                maxGpuBytes: OneOrdinaryTextureUploadBytes - 1));
        using var commands = new TextureCommandList();
        resources.UploadMaterialTexture(
            commands,
            CreateOneTextureMaterial("/World/Materials/A", SilkMaterialParameter.DiffuseColor, "a.png"),
            SilkMaterialParameter.DiffuseColor);

        // First trim: A is this frame's pinned working set and cannot be evicted yet, even
        // though it alone already exceeds the GPU budget; only the bounded working-set diagnostic
        // is reported, and nothing is evicted.
        resources.TrimTextureResidency();
        await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(0UL);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.TextureBudgetExceeded);

        // Second trim, with no intervening reference to A: it is no longer part of the working
        // set the previous trim recorded, so it becomes a stale (and, on its own, oversized)
        // eviction candidate; the loop must still terminate after evicting exactly one entry.
        resources.TrimTextureResidency();

        await Assert.That(device.AllTextures[0].DisposeCount).IsEqualTo(1);
        await Assert.That(resources.Statistics.TextureCacheEntryCount).IsEqualTo(0);
        await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(1UL);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.TextureBudgetExceeded);
    }

    [Test]
    public async Task RepeatedlyRenderingOverBudgetWorkingSetAvoidsThrashAndEmitsOneDiagnostic()
    {
        int decodeAttempts = 0;
        using var device = new TextureDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) =>
            {
                decodeAttempts++;
                return new SilkDecodedImage(2, 2, CreatePixels(2, 2));
            },
            // Fits only one of the two entries that are, together, always this frame's working
            // set: the working set alone permanently exceeds this GPU budget.
            residencyOptions: new SilkTextureResidencyOptions(
                maxDecodedCpuBytes: ulong.MaxValue,
                maxGpuBytes: OneOrdinaryTextureUploadBytes));
        using var commands = new TextureCommandList();
        SilkMaterialData materialA = CreateOneTextureMaterial(
            "/World/Materials/A", SilkMaterialParameter.DiffuseColor, "a.png");
        SilkMaterialData materialB = CreateOneTextureMaterial(
            "/World/Materials/B", SilkMaterialParameter.DiffuseColor, "b.png");

        for (int frame = 0; frame < 3; frame++)
        {
            resources.UploadMaterialTexture(commands, materialA, SilkMaterialParameter.DiffuseColor);
            resources.UploadMaterialTexture(commands, materialB, SilkMaterialParameter.DiffuseColor);
            resources.TrimTextureResidency();
        }

        // Both textures are referenced every frame, so both stay in the pinned current-frame
        // working set forever: neither is ever a stale eviction candidate, so decoding and
        // uploading each happen exactly once despite the working set alone permanently exceeding
        // the GPU budget.
        await Assert.That(decodeAttempts).IsEqualTo(2);
        await Assert.That(commands.Uploads.Count).IsEqualTo(2);
        await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(0UL);
        await Assert.That(resources.Statistics.TextureCacheEntryCount).IsEqualTo(2);
        // The diagnostic identity is fixed, so repeating this condition across many frames still
        // yields exactly one bounded entry rather than growing without bound.
        await Assert.That(resources.Diagnostics.Entries.Count(
            entry => entry.Code == SilkRenderDiagnosticCodes.TextureBudgetExceeded)).IsEqualTo(1);
        string firstMessage = resources.Diagnostics.Entries.Single(
            entry => entry.Code == SilkRenderDiagnosticCodes.TextureBudgetExceeded).Message;
        await Assert.That(firstMessage).Contains("2 entries");

        // Growing the still-over-budget working set (a third material, also referenced every
        // frame) must refresh the fixed-key diagnostic's reported byte totals and entry count
        // rather than leaving them frozen at whatever they were the first time the working set
        // went over budget: the diagnostic is replaced, not merely left in place, on every
        // emission.
        SilkMaterialData materialC = CreateOneTextureMaterial(
            "/World/Materials/C", SilkMaterialParameter.DiffuseColor, "c.png");
        for (int frame = 0; frame < 3; frame++)
        {
            resources.UploadMaterialTexture(commands, materialA, SilkMaterialParameter.DiffuseColor);
            resources.UploadMaterialTexture(commands, materialB, SilkMaterialParameter.DiffuseColor);
            resources.UploadMaterialTexture(commands, materialC, SilkMaterialParameter.DiffuseColor);
            resources.TrimTextureResidency();
        }

        await Assert.That(decodeAttempts).IsEqualTo(3);
        await Assert.That(resources.Statistics.TextureEvictionCount).IsEqualTo(0UL);
        await Assert.That(resources.Statistics.TextureCacheEntryCount).IsEqualTo(3);
        // Still exactly one fixed-key entry: the diagnostic never accumulates a second, growing
        // one no matter how many frames -- or how many distinct over-budget sizes -- repeat this
        // condition.
        await Assert.That(resources.Diagnostics.Entries.Count(
            entry => entry.Code == SilkRenderDiagnosticCodes.TextureBudgetExceeded)).IsEqualTo(1);
        string secondMessage = resources.Diagnostics.Entries.Single(
            entry => entry.Code == SilkRenderDiagnosticCodes.TextureBudgetExceeded).Message;
        await Assert.That(secondMessage).Contains("3 entries");
        await Assert.That(secondMessage).IsNotEqualTo(firstMessage);
    }

    [Test]
    public async Task EmptyTextureCacheClearsStaleBudgetDiagnostics()
    {
        using var device = new TextureDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)),
            residencyOptions: new SilkTextureResidencyOptions(
                maxDecodedCpuBytes: 1,
                maxGpuBytes: 1));
        using var commands = new TextureCommandList();
        SilkMaterialData material = CreateOneTextureMaterial(
            "/World/Materials/A",
            SilkMaterialParameter.DiffuseColor,
            "a.png");

        resources.UploadMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);
        resources.TrimTextureResidency();
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .Contains(SilkRenderDiagnosticCodes.TextureBudgetExceeded);

        SilkSceneState scene = new();
        SilkSceneDelta delta = scene.Apply(
            CreateMaterialRemove("/World/Materials/A"),
            commandCount: 1,
            revision: 1);
        resources.Apply(scene, delta);
        resources.TrimTextureResidency();

        await Assert.That(resources.Statistics.TextureCacheEntryCount).IsEqualTo(0);
        await Assert.That(resources.Diagnostics.Entries.Select(entry => entry.Code))
            .DoesNotContain(SilkRenderDiagnosticCodes.TextureBudgetExceeded);
    }

    [Test]
    public async Task EvictedTextureIsReDecodedAndReuploadedOnNextRequest()
    {
        int decodeAttempts = 0;
        using var device = new TextureDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) =>
            {
                decodeAttempts++;
                return new SilkDecodedImage(2, 2, CreatePixels(2, 2));
            },
            residencyOptions: new SilkTextureResidencyOptions(maxDecodedCpuBytes: 1, maxGpuBytes: 1));
        using var commands = new TextureCommandList();
        SilkMaterialData material = CreateOneTextureMaterial(
            "/World/Materials/A", SilkMaterialParameter.DiffuseColor, "a.png");
        resources.UploadMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);
        await Assert.That(decodeAttempts).IsEqualTo(1);

        // First trim pins the entry just created as this frame's working set; a second trim with
        // no intervening reference is what makes it a stale, evictable candidate.
        resources.TrimTextureResidency();
        resources.TrimTextureResidency();
        await Assert.That(resources.Statistics.TextureCacheEntryCount).IsEqualTo(0);

        resources.UploadMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

        await Assert.That(decodeAttempts).IsEqualTo(2);
        await Assert.That(commands.Uploads.Count).IsEqualTo(2);
    }

    [Test]
    public async Task EachGpuTextureIsDisposedExactlyOnceAcrossEvictionAndSceneDisposal()
    {
        using var device = new TextureDevice();
        using (var resources = new SilkSceneGpuResources(
            device,
            (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)),
            residencyOptions: new SilkTextureResidencyOptions(
                maxDecodedCpuBytes: ulong.MaxValue,
                maxGpuBytes: OneOrdinaryTextureUploadBytes + (OneOrdinaryTextureUploadBytes / 2))))
        {
            using var commands = new TextureCommandList();
            SilkMaterialData materialA = CreateOneTextureMaterial(
                "/World/Materials/A", SilkMaterialParameter.DiffuseColor, "a.png");
            SilkMaterialData materialB = CreateOneTextureMaterial(
                "/World/Materials/B", SilkMaterialParameter.DiffuseColor, "b.png");

            resources.UploadMaterialTexture(commands, materialA, SilkMaterialParameter.DiffuseColor);
            resources.TrimTextureResidency();
            resources.UploadMaterialTexture(commands, materialB, SilkMaterialParameter.DiffuseColor);
            resources.TrimTextureResidency();

            await Assert.That(device.AllTextures[0].DisposeCount).IsEqualTo(1);

            // Disposing the scene must dispose the survivor exactly once and must never touch
            // the already-evicted texture a second time.
        }

        await Assert.That(device.AllTextures[0].DisposeCount).IsEqualTo(1);
        await Assert.That(device.AllTextures[1].DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task ChangedLocalTextureHotReloadStillWorksWithResidencyTracking()
    {
        string asset = Path.GetTempFileName();
        try
        {
            SilkMaterialData material = CreateOneTextureMaterial(
                "/World/Materials/Reload", SilkMaterialParameter.DiffuseColor, asset);
            int attempts = 0;
            using var device = new TextureDevice();
            using var resources = new SilkSceneGpuResources(
                device,
                (_, _) =>
                {
                    attempts++;
                    return new SilkDecodedImage(1, 1, [checked((byte)attempts), 0, 0, 255]);
                });
            using var commands = new TextureCommandList();

            resources.UploadMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);
            await File.WriteAllTextAsync(asset, "changed-size");
            File.SetLastWriteTimeUtc(asset, DateTime.UtcNow.AddMinutes(1));
            resources.UploadMaterialTexture(commands, material, SilkMaterialParameter.DiffuseColor);

            await Assert.That(attempts).IsEqualTo(2);
            await Assert.That(commands.Uploads.Select(upload => upload[0]))
                .IsEquivalentTo(new byte[] { 1, 2 });
            // The stale GPU texture the reload replaced must have been disposed exactly once,
            // not merely dropped. This dependency-driven invalidation is unconditional and does
            // not depend on TrimTextureResidency's frame working-set boundary.
            await Assert.That(device.AllTextures[0].DisposeCount).IsEqualTo(1);
            await Assert.That(resources.Statistics.TextureCacheEntryCount).IsEqualTo(1);
        }
        finally
        {
            File.Delete(asset);
        }
    }

    // ---------------------------------------------------------------------
    // Material-change invalidation: RemoveChangedMaterialTextureCacheEntries.
    // ---------------------------------------------------------------------

    [Test]
    public async Task AnyMaterialChangeInvalidatesAllRetainedVolumeTexturesAndPicksUpNewDimensions()
    {
        string asset = Path.GetTempFileName();
        try
        {
            byte[] initialVolumeBytes = new byte[2 * 2 * 1 * sizeof(float)];
            await File.WriteAllBytesAsync(asset, initialVolumeBytes);
            using var device = new TextureDevice();
            using var resources = new SilkSceneGpuResources(
                device,
                (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)));
            using var commands = new TextureCommandList();
            var scene = new SilkSceneState();
            SilkMaterialData volumeMaterial = CreateOneTextureMaterial(
                "/World/Materials/Volume", SilkMaterialParameter.VolumeDensity, asset, uvPrimvar: "2,2,1");

            resources.UploadVolumeDensityTexture(commands, volumeMaterial);
            await Assert.That(resources.Statistics.TextureCacheEntryCount).IsEqualTo(1);
            await Assert.That(commands.VolumeUploads.Single().Length)
                .IsEqualTo(initialVolumeBytes.Length);

            // Volume density textures are keyed only by asset path (see RequireVolumeTexture), not
            // by any material path, so they carry no material identity a targeted invalidation
            // could prune by. A completely unrelated material's change must still dispose and
            // drop every retained volume entry rather than leaving this one to be served stale
            // forever.
            byte[] unrelatedMaterialUpsert = CreateMaterialUpsert(
                "/World/Materials/Unrelated", SilkSurfaceKind.PreviewSurface, []);
            SilkSceneDelta delta = scene.Apply(unrelatedMaterialUpsert, 1, revision: 1);
            await Assert.That(delta.MaterialChanges).IsEqualTo(1);
            resources.Apply(scene, delta);

            await Assert.That(device.AllTextures[0].DisposeCount).IsEqualTo(1);
            await Assert.That(resources.Statistics.TextureCacheEntryCount).IsEqualTo(0);

            // Re-authoring the same asset path with different dimensions must not silently keep
            // serving the disposed entry's stale dimensions: the next upload must actually read
            // the (now differently sized) file again and create a fresh, correctly sized texture.
            byte[] resizedVolumeBytes = new byte[2 * 2 * 2 * sizeof(float)];
            await File.WriteAllBytesAsync(asset, resizedVolumeBytes);
            SilkMaterialData resizedVolumeMaterial = CreateOneTextureMaterial(
                "/World/Materials/Volume", SilkMaterialParameter.VolumeDensity, asset, uvPrimvar: "2,2,2");
            resources.UploadVolumeDensityTexture(commands, resizedVolumeMaterial);

            await Assert.That(resources.Statistics.TextureCacheEntryCount).IsEqualTo(1);
            await Assert.That(commands.VolumeUploads.Count).IsEqualTo(2);
            await Assert.That(commands.VolumeUploads[1].Length).IsEqualTo(resizedVolumeBytes.Length);
            await Assert.That(device.AllTextures.Count).IsEqualTo(2);
            await Assert.That(device.AllTextures[1].DisposeCount).IsEqualTo(0);
        }
        finally
        {
            File.Delete(asset);
        }
    }

    [Test]
    public async Task ChangingOneOrdinaryMaterialDoesNotEvictOrRedecodeAnUnrelatedOrdinaryMaterial()
    {
        int decodeAttempts = 0;
        using var device = new TextureDevice();
        using var resources = new SilkSceneGpuResources(
            device,
            (_, _) =>
            {
                decodeAttempts++;
                return new SilkDecodedImage(2, 2, CreatePixels(2, 2));
            });
        using var commands = new TextureCommandList();
        var scene = new SilkSceneState();
        SilkMaterialData materialA = CreateOneTextureMaterial(
            "/World/Materials/A", SilkMaterialParameter.DiffuseColor, "a.png");
        SilkMaterialData materialB = CreateOneTextureMaterial(
            "/World/Materials/B", SilkMaterialParameter.DiffuseColor, "b.png");
        resources.UploadMaterialTexture(commands, materialA, SilkMaterialParameter.DiffuseColor);
        resources.UploadMaterialTexture(commands, materialB, SilkMaterialParameter.DiffuseColor);
        await Assert.That(decodeAttempts).IsEqualTo(2);
        await Assert.That(resources.Statistics.TextureCacheEntryCount).IsEqualTo(2);

        // Only material A changes; its cache entry is keyed by material path (see
        // TextureCacheKey), so the invalidation must be scoped to A and must not touch B's
        // already-decoded/uploaded entry at all.
        byte[] materialAUpsert = CreateMaterialUpsert(
            "/World/Materials/A", SilkSurfaceKind.PreviewSurface, []);
        SilkSceneDelta delta = scene.Apply(materialAUpsert, 1, revision: 1);
        await Assert.That(delta.MaterialChanges).IsEqualTo(1);
        resources.Apply(scene, delta);

        await Assert.That(device.AllTextures[0].DisposeCount).IsEqualTo(1); // A evicted.
        await Assert.That(device.AllTextures[1].DisposeCount).IsEqualTo(0); // B untouched.
        await Assert.That(resources.Statistics.TextureCacheEntryCount).IsEqualTo(1);

        // Re-requesting B must be a pure cache hit: no redecode and no reupload.
        resources.UploadMaterialTexture(commands, materialB, SilkMaterialParameter.DiffuseColor);
        await Assert.That(decodeAttempts).IsEqualTo(2);
        await Assert.That(commands.Uploads.Count).IsEqualTo(2);
    }

    // ---------------------------------------------------------------------
    // Integration: roughness and metallic textures are retained independently.
    // ---------------------------------------------------------------------

    [Test]
    public async Task RenderingAMaterialWithRoughnessAndMetallicTexturesKeepsBothLive()
    {
        using var device = new RenderPipelineDevice();
        using var renderer = new SilkMeshRenderer(
            device,
            SilkShaderBinaryFormat.SpirV,
            imageDecoder: (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)),
            udimResolver: null,
            textureResidencyOptions: new SilkTextureResidencyOptions(
                maxDecodedCpuBytes: 1024 * 1024,
                maxGpuBytes: 1024 * 1024));
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(4, 4));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(4, 4));

        byte[] frame = CreateFrameCommand(4, 4);
        byte[] material = CreateMaterialUpsert(
            "/World/Materials/RoughMetal",
            SilkSurfaceKind.PreviewSurface,
            [
                ScalarTexture(SilkMaterialParameter.Roughness, "rough.png", SilkTextureChannel.R),
                ScalarTexture(SilkMaterialParameter.Metallic, "metal.png", SilkTextureChannel.R),
            ]);
        byte[] mesh = CreateMeshUpsert(
            "/World/RoughMetal", "/World/Materials/RoughMetal", primId: 7);
        SilkSceneDelta delta = renderer.Scene.Apply(Concat(frame, material, mesh), 3, 1);
        renderer.GpuResources.Apply(renderer.Scene, delta);

        _ = renderer.Render(color, depth);

        // Two inputs, two assets, two decoded/uploaded material textures -- dropping
        // either input must not silently collapse this back to one.
        await Assert.That(device.LiveTextureCount(RenderPipelineDevice.MaterialTextureKind))
            .IsEqualTo(2);
    }

    [Test]
    public async Task RenderingAPackedRoughnessMetallicFileRetainsOneEntryPerChannel()
    {
        using var device = new RenderPipelineDevice();
        using var renderer = new SilkMeshRenderer(
            device,
            SilkShaderBinaryFormat.SpirV,
            imageDecoder: (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)),
            udimResolver: null,
            textureResidencyOptions: new SilkTextureResidencyOptions(
                maxDecodedCpuBytes: 1024 * 1024,
                maxGpuBytes: 1024 * 1024));
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(4, 4));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(4, 4));

        byte[] frame = CreateFrameCommand(4, 4);
        byte[] material = CreateMaterialUpsert(
            "/World/Materials/PackedRoughMetal",
            SilkSurfaceKind.PreviewSurface,
            [
                ScalarTexture(SilkMaterialParameter.Roughness, "orm.png", SilkTextureChannel.G),
                ScalarTexture(SilkMaterialParameter.Metallic, "orm.png", SilkTextureChannel.B),
            ]);
        byte[] mesh = CreateMeshUpsert(
            "/World/PackedRoughMetal", "/World/Materials/PackedRoughMetal", primId: 7);
        SilkSceneDelta delta = renderer.Scene.Apply(Concat(frame, material, mesh), 3, 1);
        renderer.GpuResources.Apply(renderer.Scene, delta);

        _ = renderer.Render(color, depth);

        // One packed file feeding two channels is two swizzled entries, so residency
        // accounting must show both rather than one shared entry.
        await Assert.That(device.LiveTextureCount(RenderPipelineDevice.MaterialTextureKind))
            .IsEqualTo(2);
    }

    [Test]
    public async Task RenderingAMaterialWithOnlyARoughnessTextureRetainsOneTexture()
    {
        using var device = new RenderPipelineDevice();
        using var renderer = new SilkMeshRenderer(
            device,
            SilkShaderBinaryFormat.SpirV,
            imageDecoder: (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)),
            udimResolver: null,
            textureResidencyOptions: new SilkTextureResidencyOptions(
                maxDecodedCpuBytes: 1024 * 1024,
                maxGpuBytes: 1024 * 1024));
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(4, 4));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(4, 4));

        byte[] frame = CreateFrameCommand(4, 4);
        byte[] material = CreateMaterialUpsert(
            "/World/Materials/RoughOnly",
            SilkSurfaceKind.PreviewSurface,
            [ScalarTexture(SilkMaterialParameter.Roughness, "rough.png", SilkTextureChannel.G)]);
        byte[] mesh = CreateMeshUpsert("/World/RoughOnly", "/World/Materials/RoughOnly", primId: 7);
        SilkSceneDelta delta = renderer.Scene.Apply(Concat(frame, material, mesh), 3, 1);
        renderer.GpuResources.Apply(renderer.Scene, delta);

        _ = renderer.Render(color, depth);

        // A roughness-only material binds and uploads exactly one texture: metallic is
        // untextured and must not pull anything into residency.
        await Assert.That(device.LiveTextureCount(RenderPipelineDevice.MaterialTextureKind))
            .IsEqualTo(1);
    }

    [Test]
    public async Task RenderingAMaterialWithOnlyAMetallicTextureRetainsOneTexture()
    {
        using var device = new RenderPipelineDevice();
        using var renderer = new SilkMeshRenderer(
            device,
            SilkShaderBinaryFormat.SpirV,
            imageDecoder: (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)),
            udimResolver: null,
            textureResidencyOptions: new SilkTextureResidencyOptions(
                maxDecodedCpuBytes: 1024 * 1024,
                maxGpuBytes: 1024 * 1024));
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(4, 4));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(4, 4));

        byte[] frame = CreateFrameCommand(4, 4);
        byte[] material = CreateMaterialUpsert(
            "/World/Materials/MetalOnly",
            SilkSurfaceKind.PreviewSurface,
            [ScalarTexture(SilkMaterialParameter.Metallic, "metal.png", SilkTextureChannel.B)]);
        byte[] mesh = CreateMeshUpsert("/World/MetalOnly", "/World/Materials/MetalOnly", primId: 7);
        SilkSceneDelta delta = renderer.Scene.Apply(Concat(frame, material, mesh), 3, 1);
        renderer.GpuResources.Apply(renderer.Scene, delta);

        _ = renderer.Render(color, depth);

        // The mirror case: a metallic-only material renders through the metallic slot
        // alone, which the previous shared-slot design could not express.
        await Assert.That(device.LiveTextureCount(RenderPipelineDevice.MaterialTextureKind))
            .IsEqualTo(1);
    }

    private static TextureSpec ScalarTexture(
        SilkMaterialParameter parameter,
        string asset,
        SilkTextureChannel channel) =>
        new(parameter, asset, "st", ComponentCount: 1, Channel: channel);

    // ---------------------------------------------------------------------
    // Integration: the renderer only trims after a submission has completed.
    // ---------------------------------------------------------------------

    [Test]
    public async Task RendererTrimsTextureResidencyOnlyAfterSubmissionCompletes()
    {
        using var device = new RenderPipelineDevice();
        // A GPU budget that fits exactly one uploaded texture: the second frame's distinct
        // material forces eviction of the first frame's texture, but only once its frame's
        // submission has completed. The image decoder is faked (rather than using the public
        // constructor's real native decoder) so this test does not depend on the native
        // image-decode library being present.
        using var renderer = new SilkMeshRenderer(
            device,
            SilkShaderBinaryFormat.SpirV,
            imageDecoder: (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)),
            udimResolver: null,
            textureResidencyOptions: new SilkTextureResidencyOptions(
                maxDecodedCpuBytes: 1024 * 1024,
                maxGpuBytes: 30));
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(4, 4));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(4, 4));

        byte[] frame = CreateFrameCommand(4, 4);
        byte[] materialA = CreateMaterialUpsert(
            "/World/Materials/A",
            SilkSurfaceKind.PreviewSurface,
            [new TextureSpec(SilkMaterialParameter.DiffuseColor, "a.png")]);
        byte[] meshA = CreateMeshUpsert("/World/A", "/World/Materials/A", primId: 7);
        byte[] firstPage = Concat(frame, materialA, meshA);
        SilkSceneDelta firstDelta = renderer.Scene.Apply(firstPage, 3, 1);
        renderer.GpuResources.Apply(renderer.Scene, firstDelta);

        _ = renderer.Render(color, depth);

        // Rendering the first frame alone must not evict anything: it is the first-ever trim, so
        // A is pinned as this frame's whole working set regardless of budget.
        await Assert.That(renderer.GpuResources.Statistics.TextureEvictionCount).IsEqualTo(0UL);
        await Assert.That(device.LiveTextureCount(RenderPipelineDevice.MaterialTextureKind))
            .IsEqualTo(1);

        // Removing mesh A (rather than merely adding mesh B) ensures A's texture is genuinely
        // unreferenced in the second frame — otherwise it would still be bound and touched again,
        // which would correctly keep it pinned rather than stale.
        byte[] removeA = CreateMeshRemoval("/World/A");
        byte[] materialB = CreateMaterialUpsert(
            "/World/Materials/B",
            SilkSurfaceKind.PreviewSurface,
            [new TextureSpec(SilkMaterialParameter.DiffuseColor, "b.png")]);
        byte[] meshB = CreateMeshUpsert("/World/B", "/World/Materials/B", primId: 8);
        byte[] secondPage = Concat(frame, removeA, materialB, meshB);
        SilkSceneDelta secondDelta = renderer.Scene.Apply(secondPage, 4, 2);
        renderer.GpuResources.Apply(renderer.Scene, secondDelta);

        // Before this frame's submission completes, B's texture is recorded and referenced by the
        // outstanding command list; the fake device asserts this itself by never observing a
        // texture dispose while a submission is still unwaited (see RenderPipelineDevice).
        SilkMeshRenderResult result = renderer.Render(color, depth);

        // Only after the submission returned from this call has completed does the renderer
        // trim: A is no longer referenced by any mesh, so it is stale by the second trim and is
        // exactly the one entry evicted.
        await Assert.That(result.Statistics.TextureEvictionCount).IsEqualTo(1UL);
        await Assert.That(result.Statistics.TextureCacheEntryCount).IsEqualTo(1);
        await Assert.That(device.TextureDisposedWhileSubmissionPendingCount).IsEqualTo(0);
        // The evicted texture's own ISilkGraphicsCommandList was still alive (not yet disposed)
        // at the moment of eviction: safety comes from the completed submission's Wait(), not
        // from the recording object's own disposal, which the renderer defers until later.
        await Assert.That(device.TextureDisposedWhileLastCommandListStillAliveCount)
            .IsGreaterThanOrEqualTo(1);
    }

    // ---------------------------------------------------------------------
    // Deterministic byte-size probes (no reflection): learn real, fixed payload sizes once so
    // other tests can pass tight residency budgets to the constructor up front.
    // ---------------------------------------------------------------------

    private static ulong ComputeOneOrdinaryTextureUploadBytes()
    {
        using var device = new TextureDevice();
        using var resources = new SilkSceneGpuResources(
            device, (_, _) => new SilkDecodedImage(2, 2, CreatePixels(2, 2)));
        using var commands = new TextureCommandList();
        resources.UploadMaterialTexture(
            commands,
            CreateOneTextureMaterial(
                "/World/Materials/Probe", SilkMaterialParameter.DiffuseColor, "probe.png"),
            SilkMaterialParameter.DiffuseColor);
        return checked((ulong)commands.Uploads.Single().Length);
    }

    private static ulong ComputeFailedFallbackUploadBytes()
    {
        using var device = new TextureDevice();
        using var resources = new SilkSceneGpuResources(
            device, (_, _) => throw new FileNotFoundException("Texture is absent.", "missing.png"));
        using var commands = new TextureCommandList();
        resources.UploadMaterialTexture(
            commands,
            CreateOneTextureMaterial(
                "/World/Materials/Probe", SilkMaterialParameter.DiffuseColor, "missing.png"),
            SilkMaterialParameter.DiffuseColor);
        return checked((ulong)commands.Uploads.Single().Length);
    }

    // ---------------------------------------------------------------------
    // Material/mesh/frame command construction (mirrors SilkMaterialCommandTests' wire format).
    // ---------------------------------------------------------------------

    private static SilkMaterialData CreateOneTextureMaterial(
        string materialPath,
        SilkMaterialParameter parameter,
        string asset,
        string uvPrimvar = "st") =>
        CopyMaterial(CreateMaterialUpsert(
            materialPath,
            SilkSurfaceKind.PreviewSurface,
            [new TextureSpec(parameter, asset, uvPrimvar)]));

    private static SilkMaterialData CopyMaterial(byte[] command)
    {
        using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(
            command, 1, SilkCommandParser.PageAbiVersion);
        _ = commands.MoveNext();
        return SilkMaterialData.CopyFrom(commands.Current.AsMaterialUpsert());
    }

    private readonly record struct TextureSpec(
        SilkMaterialParameter Parameter,
        string Asset,
        string UvPrimvar = "st",
        int ComponentCount = 4,
        SilkTextureChannel Channel = SilkTextureChannel.Rgb);

    private static byte[] CreateMaterialUpsert(
        string path,
        SilkSurfaceKind kind,
        TextureSpec[] textures)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        List<byte> payload = [];
        payload.AddRange(BitConverter.GetBytes(SilkWireFormat.ComputeStableHash(path)));
        payload.AddRange(BitConverter.GetBytes((uint)pathBytes.Length));
        payload.AddRange(BitConverter.GetBytes((uint)kind));
        payload.AddRange(BitConverter.GetBytes(0u));
        payload.AddRange(BitConverter.GetBytes((uint)textures.Length));
        payload.AddRange(pathBytes);

        foreach (TextureSpec texture in textures)
        {
            byte[] assetBytes = Encoding.UTF8.GetBytes(texture.Asset);
            byte[] uvBytes = Encoding.UTF8.GetBytes(texture.UvPrimvar);
            payload.AddRange(BitConverter.GetBytes((uint)texture.Parameter));
            payload.AddRange(BitConverter.GetBytes((uint)SilkTextureWrap.Repeat));
            payload.AddRange(BitConverter.GetBytes((uint)SilkTextureWrap.Repeat));
            payload.AddRange(BitConverter.GetBytes((uint)SilkColorSpace.Raw));
            payload.AddRange(BitConverter.GetBytes((uint)assetBytes.Length));
            payload.AddRange(BitConverter.GetBytes((uint)uvBytes.Length));
            payload.AddRange(BitConverter.GetBytes((uint)texture.ComponentCount));
            for (int component = 0; component < 4; component++)
            {
                payload.AddRange(BitConverter.GetBytes(1f));
            }
            for (int component = 0; component < 4; component++)
            {
                payload.AddRange(BitConverter.GetBytes(0f));
            }
            for (int component = 0; component < 4; component++)
            {
                payload.AddRange(BitConverter.GetBytes(component == 3 ? 1f : 0f));
            }
            payload.AddRange(BitConverter.GetBytes((uint)texture.Channel));
            payload.AddRange(assetBytes);
            payload.AddRange(uvBytes);
        }

        payload.AddRange(BitConverter.GetBytes(0u));
        payload.AddRange(BitConverter.GetBytes(0u));
        return CreateCommand((uint)SilkCommandType.MaterialUpsert, payload);
    }

    private static byte[] CreateMaterialRemove(string path)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        List<byte> payload = [];
        payload.AddRange(BitConverter.GetBytes(SilkWireFormat.ComputeStableHash(path)));
        payload.AddRange(BitConverter.GetBytes((uint)pathBytes.Length));
        payload.AddRange(pathBytes);
        return CreateCommand((uint)SilkCommandType.MaterialRemove, payload);
    }

    private static byte[] CreateMeshUpsert(string pathValue, string materialPath, int primId)
    {
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        byte[] material = Encoding.UTF8.GetBytes(materialPath);
        float[] points = [-0.5f, -0.5f, 0, 0, 0.5f, 0, 0.5f, -0.5f, 0];
        uint[] indices = [0, 1, 2];
        int size = 224 +
            path.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            sizeof(uint) +
            material.Length;
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(pathValue));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), primId);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)path.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 1);
        for (int i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64 + (i * 4)), 1);
        }
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (i * 8)),
                i % 5 == 0 ? 1 : 0);
        }
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(208),
            SilkWireFormat.ComputeStableHash(materialPath));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(216), (uint)material.Length);
        path.CopyTo(bytes, 224);
        int pointsOffset = 224 + path.Length;
        for (int i = 0; i < points.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(pointsOffset + (i * sizeof(float))),
                points[i]);
        }
        int indicesOffset = pointsOffset + (points.Length * sizeof(float));
        for (int i = 0; i < indices.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(indicesOffset + (i * sizeof(uint))),
                indices[i]);
        }
        int triangleOffset = indicesOffset + (indices.Length * sizeof(uint));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(triangleOffset), 0);
        material.CopyTo(bytes, triangleOffset + sizeof(uint));
        return bytes;
    }

    private static byte[] CreateMeshRemoval(string pathValue)
    {
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        var bytes = new byte[24 + path.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshRemove);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), SilkWireFormat.ComputeStableHash(pathValue));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), (uint)path.Length);
        path.CopyTo(bytes, 24);
        return bytes;
    }

    private static byte[] CreateFrameCommand(uint width, uint height)
    {
        var bytes = new byte[272];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), checked((int)width));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), checked((int)height));
        for (int index = 0; index < 16; index++)
        {
            double value = index % 5 == 0 ? 1 : 0;
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (index * 8)), value);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (index * 8)), value);
        }
        return bytes;
    }

    private static byte[] Concat(params byte[][] commands)
    {
        var bytes = new byte[commands.Sum(command => command.Length)];
        int cursor = 0;
        foreach (byte[] command in commands)
        {
            command.CopyTo(bytes, cursor);
            cursor += command.Length;
        }
        return bytes;
    }

    private static byte[] CreateCommand(uint type, List<byte> payload)
    {
        byte[] command = new byte[8 + payload.Count];
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(0, 4), type);
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(4, 4), (uint)command.Length);
        payload.CopyTo(command, 8);
        return command;
    }

    private static byte[] CreatePixels(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int index = 0; index < pixels.Length; index++)
        {
            pixels[index] = checked((byte)(index % 256));
        }
        return pixels;
    }

    // ---------------------------------------------------------------------
    // Fakes: an upload-only device/command-list pair (no rendering pipeline).
    // ---------------------------------------------------------------------

    private sealed class TextureDevice : ISilkGraphicsDevice, ISilkVolumeTextureGraphicsDevice
    {
        internal List<Texture> AllTextures { get; } = [];

        public SilkGraphicsBackend Backend => SilkGraphicsBackend.D3D12;

        public SilkGraphicsCapabilities Capabilities => new(
            "Residency test device", "test", SupportsCompute: false, IsSoftware: true);

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm)
        {
            var texture = new Texture(width, height, format);
            AllTextures.Add(texture);
            return texture;
        }

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor)
        {
            var texture = new Texture(
                descriptor.Width, descriptor.Height, descriptor.Format, descriptor.MipLevelCount);
            AllTextures.Add(texture);
            return texture;
        }

        public ISilkGraphicsTexture CreateTexture3D(
            uint width,
            uint height,
            uint depth,
            SilkTextureFormat format)
        {
            var texture = new Texture(width, height, format);
            AllTextures.Add(texture);
            return texture;
        }

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage) =>
            throw new NotSupportedException();

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderModule CreateShaderModule(SilkShaderModuleDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsBindingLayout CreateBindingLayout(SilkBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderProgram CreateShaderProgram(SilkShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsPipeline CreateGraphicsPipeline(SilkGraphicsPipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(SilkComputePipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsCommandList CreateCommandList() => throw new NotSupportedException();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
            throw new NotSupportedException();

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class Texture(
        uint width,
        uint height,
        SilkTextureFormat format,
        uint mipLevelCount = 1) : ISilkGraphicsTexture
    {
        internal int DisposeCount { get; private set; }

        public uint Width { get; } = width;

        public uint Height { get; } = height;

        public SilkTextureFormat Format { get; } = format;

        public SilkTextureUsage Usage => SilkTextureUsage.Sampled;

        public uint MipLevelCount { get; } = mipLevelCount;

        public void ReadbackForTesting(Span<byte> destination) => throw new NotSupportedException();

        public void ReadbackForTesting(Span<float> destination) => throw new NotSupportedException();

        public void Dispose() => DisposeCount++;
    }

    private sealed class TextureCommandList : ISilkGraphicsCommandList, ISilkVolumeTextureCommandList
    {
        internal List<byte[]> Uploads { get; } = [];

        internal List<byte[]> VolumeUploads { get; } = [];

        public void UploadTexture(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source) =>
            Uploads.Add(source.ToArray());

        public void UploadTexture3D(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source) =>
            VolumeUploads.Add(source.ToArray());

        public void ClearColor(ISilkGraphicsTexture texture, SilkColor color) =>
            throw new NotSupportedException();

        public void ClearDepth(ISilkGraphicsTexture texture, float depth) =>
            throw new NotSupportedException();

        public void BeginRendering(SilkRenderingDescriptor descriptor) =>
            throw new NotSupportedException();

        public void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline) =>
            throw new NotSupportedException();

        public void SetViewport(SilkViewport viewport) => throw new NotSupportedException();

        public void SetScissor(SilkScissor scissor) => throw new NotSupportedException();

        public void SetVertexBuffer(ISilkGraphicsBuffer buffer) => throw new NotSupportedException();

        public void SetIndexBuffer(ISilkGraphicsBuffer buffer) => throw new NotSupportedException();

        public void SetUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        public void SetTexture(uint setIndex, uint binding, ISilkGraphicsTexture texture) =>
            throw new NotSupportedException();

        public void SetSampler(uint setIndex, uint binding, ISilkGraphicsSampler sampler) =>
            throw new NotSupportedException();

        public void DrawIndexed(uint indexCount) => throw new NotSupportedException();

        public void DrawIndexedInstanced(uint indexCount, uint instanceCount) =>
            throw new NotSupportedException();

        public void EndRendering() => throw new NotSupportedException();

        public void SetComputePipeline(ISilkComputePipeline pipeline) =>
            throw new NotSupportedException();

        public void SetStorageBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        public void SetComputeUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        public void Dispatch(uint elementCount) => throw new NotSupportedException();

        public void BufferBarrier(ISilkGraphicsBuffer buffer) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    // ---------------------------------------------------------------------
    // Fakes: a full no-op rendering pipeline, capable of a real SilkMeshRenderer.Render() call.
    // ---------------------------------------------------------------------

    private sealed class RenderPipelineDevice : ISilkGraphicsDevice
    {
        internal const string MaterialTextureKind = "material";

        private readonly List<RenderPipelineTexture> _textures = [];
        private RenderPipelineCommandList? _lastCommandList;
        private int _pendingSubmissions;

        /// <summary>
        /// Gets the count of texture disposals observed while a submission that may have
        /// referenced a retained texture had not yet completed its <c>Wait()</c>. This is the
        /// actual safety boundary <see cref="SilkSceneGpuResources.TrimTextureResidency"/>
        /// depends on; it must always be zero.
        /// </summary>
        internal int TextureDisposedWhileSubmissionPendingCount { get; private set; }

        /// <summary>
        /// Gets the count of texture disposals observed while the most recently created
        /// <see cref="ISilkGraphicsCommandList"/> had not yet itself been disposed. Proves the
        /// renderer's safety reasoning rests on a completed submission's <c>Wait()</c>, not on
        /// whether the command-list object that recorded the reference has been disposed — the
        /// renderer defers disposing that object until after this trim already ran.
        /// </summary>
        internal int TextureDisposedWhileLastCommandListStillAliveCount { get; private set; }

        public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

        public SilkGraphicsCapabilities Capabilities { get; } =
            new("Residency integration test", "1", SupportsCompute: true, IsSoftware: true);

        internal int LiveTextureCount(string kind) =>
            _textures.Count(texture => texture.Kind == kind && !texture.IsDisposed);

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage) =>
            new RenderPipelineBuffer(size, usage);

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            new RenderPipelineTexture(
                new SilkTextureDescriptor(
                    width, height, format, SilkTextureDescriptor.GetDefaultUsage(format)),
                "target",
                this);

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor)
        {
            // Only sampled, non-attachment descriptors are material/volume textures for the
            // purposes of this test: render targets (color, depth, and the selection mask) reuse
            // the same descriptor overload but must not be counted as retained material textures.
            bool isMaterialTexture =
                (descriptor.Usage & SilkTextureUsage.Sampled) != 0 &&
                (descriptor.Usage & (SilkTextureUsage.ColorRenderTarget |
                    SilkTextureUsage.DepthRenderTarget)) == 0;
            var texture = new RenderPipelineTexture(
                descriptor, isMaterialTexture ? MaterialTextureKind : "target", this);
            if (isMaterialTexture)
            {
                _textures.Add(texture);
            }
            return texture;
        }

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
            new RenderPipelineSampler(descriptor);

        public ISilkGraphicsShaderModule CreateShaderModule(SilkShaderModuleDescriptor descriptor) =>
            new RenderPipelineShaderModule(descriptor);

        public ISilkGraphicsBindingLayout CreateBindingLayout(SilkBindingLayoutDescriptor descriptor) =>
            new RenderPipelineBindingLayout(descriptor);

        public ISilkGraphicsShaderProgram CreateShaderProgram(SilkShaderProgramDescriptor descriptor) =>
            new RenderPipelineShaderProgram(descriptor.BindingLayout);

        public ISilkGraphicsPipeline CreateGraphicsPipeline(SilkGraphicsPipelineDescriptor descriptor) =>
            new RenderPipelinePipeline(descriptor);

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(SilkComputePipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsCommandList CreateCommandList()
        {
            var commandList = new RenderPipelineCommandList();
            _lastCommandList = commandList;
            return commandList;
        }

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList)
        {
            _pendingSubmissions++;
            return new RenderPipelineSubmission(this);
        }

        /// <summary>
        /// Marks one submission as no longer pending. Called only once a submission's
        /// <see cref="ISilkGraphicsSubmission.Wait"/> has completed, mirroring the real backends'
        /// deferred native-release leases. This is independent of whether the
        /// <see cref="ISilkGraphicsCommandList"/> that recorded the submission has itself been
        /// disposed — the renderer may (and does) defer that disposal until later.
        /// </summary>
        internal void NotifySubmissionCompleted() => _pendingSubmissions--;

        internal void NotifyTextureDisposed()
        {
            if (_pendingSubmissions > 0)
            {
                TextureDisposedWhileSubmissionPendingCount++;
            }
            if (_lastCommandList is { IsDisposed: false })
            {
                TextureDisposedWhileLastCommandListStillAliveCount++;
            }
        }

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class RenderPipelineBuffer(nuint size, SilkBufferUsage usage)
        : SilkGraphicsBufferBase(size, usage)
    {
        private readonly byte[] _bytes = new byte[checked((int)size)];

        public override void Write(ReadOnlySpan<byte> data, nuint offset = 0)
        {
            _ = ValidateWrite(data.Length, offset);
            data.CopyTo(_bytes.AsSpan(checked((int)offset)));
        }

        public override void ReadbackForTesting(Span<byte> destination)
        {
            _ = ValidateReadback(destination.Length);
            _bytes.CopyTo(destination);
        }

        protected override void ReleaseNative()
        {
        }
    }

    private sealed class RenderPipelineTexture(
        SilkTextureDescriptor descriptor,
        string kind,
        RenderPipelineDevice owner) : SilkGraphicsTextureBase(descriptor)
    {
        internal string Kind { get; } = kind;

        internal bool IsDisposed { get; private set; }

        public override void ReadbackForTesting(Span<byte> destination) => destination.Clear();

        public override void ReadbackForTesting(Span<float> destination) => destination.Clear();

        protected override void ReleaseNative()
        {
            IsDisposed = true;
            owner.NotifyTextureDisposed();
        }
    }

    private sealed class RenderPipelineSampler(SilkSamplerDescriptor descriptor) : ISilkGraphicsSampler
    {
        public SilkSamplerDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class RenderPipelineShaderModule(SilkShaderModuleDescriptor descriptor)
        : ISilkGraphicsShaderModule
    {
        public SilkShaderModuleDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class RenderPipelineBindingLayout(SilkBindingLayoutDescriptor descriptor)
        : ISilkGraphicsBindingLayout
    {
        public SilkBindingLayoutDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class RenderPipelineShaderProgram(ISilkGraphicsBindingLayout bindingLayout)
        : ISilkGraphicsShaderProgram
    {
        public ISilkGraphicsBindingLayout BindingLayout { get; } = bindingLayout;

        public void Dispose()
        {
        }
    }

    private sealed class RenderPipelinePipeline(SilkGraphicsPipelineDescriptor descriptor)
        : ISilkGraphicsPipeline
    {
        public SilkGraphicsPipelineDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class RenderPipelineCommandList : ISilkGraphicsCommandList
    {
        internal bool IsDisposed { get; private set; }

        public void UploadTexture(ISilkGraphicsTexture texture, ReadOnlySpan<byte> source)
        {
        }

        public void ClearColor(ISilkGraphicsTexture texture, SilkColor color)
        {
        }

        public void ClearDepth(ISilkGraphicsTexture texture, float depth)
        {
        }

        public void BeginRendering(SilkRenderingDescriptor descriptor)
        {
        }

        public void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline)
        {
        }

        public void SetViewport(SilkViewport viewport)
        {
        }

        public void SetScissor(SilkScissor scissor)
        {
        }

        public void SetVertexBuffer(ISilkGraphicsBuffer buffer)
        {
        }

        public void SetIndexBuffer(ISilkGraphicsBuffer buffer)
        {
        }

        public void SetUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
        {
        }

        public void SetTexture(uint setIndex, uint binding, ISilkGraphicsTexture texture)
        {
        }

        public void SetSampler(uint setIndex, uint binding, ISilkGraphicsSampler sampler)
        {
        }

        public void DrawIndexed(uint indexCount)
        {
        }

        public void DrawIndexedInstanced(uint indexCount, uint instanceCount)
        {
        }

        public void EndRendering()
        {
        }

        public void SetComputePipeline(ISilkComputePipeline pipeline)
        {
        }

        public void SetStorageBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
        {
        }

        public void SetComputeUniformBuffer(uint setIndex, uint binding, ISilkGraphicsBuffer buffer)
        {
        }

        public void Dispatch(uint elementCount)
        {
        }

        public void BufferBarrier(ISilkGraphicsBuffer buffer)
        {
        }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class RenderPipelineSubmission(RenderPipelineDevice device) : ISilkGraphicsSubmission
    {
        private bool _waited;

        public bool IsCompleted => true;

        public void Wait()
        {
            if (_waited)
            {
                return;
            }
            _waited = true;
            device.NotifySubmissionCompleted();
        }

        public void Dispose()
        {
        }
    }
}
