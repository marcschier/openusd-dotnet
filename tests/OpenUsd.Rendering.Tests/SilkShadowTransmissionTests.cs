// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkShadowTransmissionTests
{
    [Test]
    [Arguments(-1)]
    [Arguments(0)]
    [Arguments(1)]
    public async Task DefaultWorkspaceAdmissionChecksTheExactByteBoundary(int delta)
    {
        using var device = new SilkShadowAtlasRetentionTests.ShadowDevice(SilkGraphicsBackend.D3D12);
        ulong pending = SilkShadowTransmissionWorkspace.PeakBytes(0, 256, true, true);
        ulong retained = checked(
            (ulong)((long)(SilkShadowTransmissionWorkspace.DefaultMaximumBytes - pending) + delta));
        if (delta > 0)
        {
            await Assert.That(() => SilkShadowTransmissionWorkspace.Validate(device, retained, 256, true, true))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("ShadowTransmissionWorkspaceLimit");
        }
        else
        {
            SilkShadowTransmissionWorkspace.Validate(device, retained, 256, true, true);
            await Assert.That(SilkShadowTransmissionWorkspace.PeakBytes(retained, 256, true, true))
                .IsEqualTo(checked((ulong)((long)SilkShadowTransmissionWorkspace.DefaultMaximumBytes + delta)));
        }
        await Assert.That(device.CreatedShadowTextureCount).IsEqualTo(0);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    public async Task MissingAnyBudgetKeepsTheDefaultWorkspaceLimit(int configured)
    {
        using var device = new SilkShadowAtlasRetentionTests.ShadowDevice(SilkGraphicsBackend.D3D12);
        if ((configured & 1) != 0)
        {
            new SilkGpuTextureBudget(1024).ConfigureDevice(device);
        }
        if ((configured & 2) != 0)
        {
            new SilkGpuStagingBudget(1024).ConfigureDevice(device);
        }
        if ((configured & 4) != 0)
        {
            new SilkCpuTextureBudget(1024).ConfigureDevice(device);
        }
        if (configured == 7)
        {
            SilkShadowTransmissionWorkspace.Validate(device, 0, 10_240, true, true);
            await Assert.That(() => new SilkShadowAtlasResource(device, 256, true))
                .Throws<SilkGpuTextureBudgetExceededException>();
            await Assert.That(device.GpuTextureBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(device.GpuTextureBudget!.MaximumBytes).IsEqualTo(1024ul);
            await Assert.That(() => device.GpuStagingBudget!.Reserve(1025))
                .Throws<SilkGpuStagingBudgetExceededException>();
            await Assert.That(() => device.CpuTextureBudget!.Reserve(1025))
                .Throws<SilkCpuTextureBudgetExceededException>();
        }
        else
        {
            await Assert.That(() => SilkShadowTransmissionWorkspace.Validate(device, 0, 10_240, true, true))
                .Throws<InvalidOperationException>()
                .WithMessageContaining("configure all three budgets");
            await Assert.That(device.CreatedShadowTextureCount).IsEqualTo(0);
        }
    }

    [Test]
    public async Task WorkspaceCountsActiveIncompatibleSpareCandidateAndPaddedReadback()
    {
        ulong active = SilkShadowTransmissionWorkspace.AtlasBytes(1024, true);
        ulong incompatibleSpare = SilkShadowTransmissionWorkspace.AtlasBytes(512, false);
        ulong retained = active + incompatibleSpare;
        await Assert.That(SilkShadowTransmissionWorkspace.PeakBytes(retained, 257, true, true))
            .IsEqualTo(retained + (257ul * 257 * 80) + (1280ul * 257));
        await Assert.That(SilkShadowTransmissionWorkspace.PeakBytes(active * 2, 1024, true, false))
            .IsEqualTo(160ul * 1024 * 1024);
        await Assert.That(SilkShadowTransmissionWorkspace.PeakBytes(active * 2, 1024, false, true))
            .IsEqualTo(156ul * 1024 * 1024);
        await Assert.That(() => SilkShadowTransmissionWorkspace.AtlasBytes(uint.MaxValue, true))
            .Throws<OverflowException>();
        await Assert.That(() => SilkShadowTransmissionWorkspace.PeakBytes(ulong.MaxValue, 256, true, false))
            .Throws<OverflowException>();
    }

    [Test]
    public async Task WorkspaceRefusalPreservesRetainedOwnersAndAllowsSmallerRetryThenDisposal()
    {
        using var device = new SilkShadowAtlasRetentionTests.ShadowDevice(SilkGraphicsBackend.D3D12);
        var budget = new SilkGpuTextureBudget(67_108_864);
        budget.ConfigureDevice(device);
        using var active = new SilkShadowAtlasResource(device, 256, true);
        using var spare = new SilkShadowAtlasResource(device, 512, false);
        ulong retained = active.PayloadBytes + spare.PayloadBytes;
        ulong reserved = budget.Usage.ReservedBytes;
        int created = device.CreatedShadowTextureCount;
        await Assert.That(() => SilkShadowTransmissionWorkspace.Validate(device, retained, 8192, true, true))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("previously published atlas is unchanged");
        await Assert.That(device.CreatedShadowTextureCount).IsEqualTo(created);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        SilkShadowTransmissionWorkspace.Validate(device, retained, 256, true, true);
        using (var candidate = new SilkShadowAtlasResource(device, 256, true))
        {
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved + candidate.PayloadBytes);
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        active.Dispose();
        spare.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false, 4)]
    [Arguments(true, 76)]
    public async Task AtlasResourcesReserveExactlyTheirOwnedPayloadAndReleaseIt(bool transmission, int bytesPerPixel)
    {
        using var device = new SilkShadowAtlasRetentionTests.ShadowDevice(SilkGraphicsBackend.D3D12);
        var budget = new SilkGpuTextureBudget(8_388_608);
        budget.ConfigureDevice(device);
        var atlas = new SilkShadowAtlasResource(device, 256, transmission);
        await Assert.That(atlas.Width).IsEqualTo(256u);
        await Assert.That(atlas.Height).IsEqualTo(256u);
        await Assert.That(atlas.Layers.Length).IsEqualTo(transmission ? 4 : 0);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(256ul * 256 * (ulong)bytesPerPixel);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(transmission ? 7ul : 1ul);
        if (transmission)
        {
            await Assert.That(atlas.Layers.All(layer => layer.Format == SilkTextureFormat.Rgba32Float)).IsTrue();
            await Assert.That(atlas.PeelDepth!.Format).IsEqualTo(SilkTextureFormat.D32Float);
            await Assert.That(atlas.Probe!.Usage.HasFlag(SilkTextureUsage.CopySource)).IsTrue();
        }
        atlas.Dispose();
        atlas.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(4)]
    [Arguments(20)]
    [Arguments(36)]
    [Arguments(52)]
    [Arguments(68)]
    [Arguments(72)]
    [Arguments(76)]
    public async Task RefusingAnyAtlasAllocationReleasesEveryEarlierReservation(int cumulativeBytesPerPixel)
    {
        using var device = new SilkShadowAtlasRetentionTests.ShadowDevice(SilkGraphicsBackend.D3D12);
        var budget = new SilkGpuTextureBudget((256ul * 256 * (ulong)cumulativeBytesPerPixel) - 1);
        budget.ConfigureDevice(device);
        await Assert.That(() => new SilkShadowAtlasResource(device, 256, transmission: true))
            .Throws<SilkGpuTextureBudgetExceededException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(0)]
    [Arguments(127)]
    [Arguments(128)]
    public async Task EmptyOrSingleIntersectionProbeIsAccepted(int alpha)
    {
        byte[] pixels = new byte[4 * 4 * 4];
        pixels[((2 * 4 + 1) * 4) + 3] = (byte)alpha;
        SilkShadowTransmissionValidation.ValidateProbe(pixels, 4, Tile, 7, 2, overflow: false);
        await Assert.That(pixels[((2 * 4 + 1) * 4) + 3]).IsEqualTo((byte)alpha);
    }

    [Test]
    [Arguments(129)]
    [Arguments(191)]
    [Arguments(255)]
    public async Task EqualDepthMultiplicityRefusesTheCandidateWithPixelAndLight(int alpha)
    {
        byte[] pixels = new byte[4 * 4 * 4];
        pixels[((2 * 4 + 1) * 4) + 3] = (byte)alpha;
        await Assert.That(() => SilkShadowTransmissionValidation.ValidateProbe(
                pixels, 4, Tile, 7, 2, overflow: false))
            .Throws<InvalidDataException>()
            .WithMessageContaining("ShadowTransmissionEqualDepth");
        await Assert.That(() => SilkShadowTransmissionValidation.ValidateProbe(
                pixels, 4, Tile, 7, 2, overflow: false))
            .Throws<InvalidDataException>()
            .WithMessageContaining("light 7, layer 2, atlas pixel (1, 2)");
    }

    [Test]
    public async Task FifthLayerRejectsAnyCoverageButIgnoresOtherAtlasTiles()
    {
        byte[] pixels = new byte[4 * 4 * 4];
        pixels[3] = 255;
        SilkShadowTransmissionValidation.ValidateProbe(pixels, 4, Tile, 0, 4, overflow: true);
        pixels[((1 * 4 + 1) * 4) + 3] = 1;
        await Assert.That(() => SilkShadowTransmissionValidation.ValidateProbe(
                pixels, 4, Tile, 0, 4, overflow: true))
            .Throws<InvalidDataException>()
            .WithMessageContaining("ShadowTransmissionLayerOverflow");
    }

    [Test]
    public async Task MalformedProbeCannotPublish()
    {
        await Assert.That(() => SilkShadowTransmissionValidation.ValidateProbe(
                new byte[63], 4, Tile, 0, 0, overflow: false))
            .Throws<ArgumentException>();
        await Assert.That(() => SilkShadowTransmissionValidation.ValidateProbe(
                new byte[64], 4, Tile with { PixelX = 4 }, 0, 0, overflow: false))
            .Throws<ArgumentException>();
    }

    private static SilkShadowTilePlacement Tile => new(1, 1, 2, 0.25f, 0.25f, 0.5f, 0.5f);
}
