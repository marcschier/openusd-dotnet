// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkGpuStagingAdmissionTests
{
    [Test]
    public async Task RetainedD3D12ReadbackRecordWithholdsCreditUntilItsRelease()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("D3D12 retained readback ownership is Windows-only.");
            return;
        }
        var budget = new SilkGpuStagingBudget(64);
        var retained = new D3D12RetainedResources([], 0, 0, 0, budget.Reserve(64));
        try
        {
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(64ul);
            await Assert.That(() => budget.Reserve(1)).Throws<SilkGpuStagingBudgetExceededException>();
        }
        finally
        {
            retained.Release();
        }
        retained.Release();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 2824ul)]
    [Arguments(SilkGraphicsBackend.Vulkan, 96ul)]
    public async Task VolumeUploadCountsEveryPaddedSlice(SilkGraphicsBackend backend, ulong expectedBytes)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuStagingBudget(expectedBytes);
        budget.ConfigureDevice(device);
        using ISilkGraphicsTexture volume = ((ISilkVolumeTextureGraphicsDevice)device).CreateTexture3D(
            2, 3, 4, SilkTextureFormat.R32Float);
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        ((ISilkVolumeTextureCommandList)commands).UploadTexture3D(volume, new byte[96]);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(expectedBytes);
        submission.Wait();
        submission.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(expectedBytes);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 1540ul)]
    [Arguments(SilkGraphicsBackend.Vulkan, 84ul)]
    public async Task MipUploadCountsPerLevelTransferPadding(SilkGraphicsBackend backend, ulong expectedBytes)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuStagingBudget(expectedBytes);
        budget.ConfigureDevice(device);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination, 3));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, new byte[84]);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(expectedBytes);
        submission.Wait();
        submission.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(expectedBytes);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 784ul)]
    [Arguments(SilkGraphicsBackend.Vulkan, 64ul)]
    public async Task TextureUploadCountsPaddedNativeBytesAndHoldsThemThroughSubmission(
        SilkGraphicsBackend backend, ulong expectedBytes)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var staging = new SilkGpuStagingBudget(expectedBytes);
        var textures = new SilkGpuTextureBudget(64);
        var buffers = new SilkGpuBufferBudget(16);
        staging.ConfigureDevice(device);
        textures.ConfigureDevice(device);
        buffers.ConfigureDevice(device);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopySource | SilkTextureUsage.CopyDestination));
        byte[] pixels = Enumerable.Range(0, 64).Select(static value => (byte)value).ToArray();
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, pixels);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        commands.Dispose();
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(expectedBytes);
        await Assert.That(staging.Usage.ReservationCount).IsEqualTo(1ul);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(64ul);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
        using (ISilkGraphicsCommandList refused = device.CreateCommandList())
        {
            refused.UploadTexture(texture, pixels);
            await Assert.That(() => device.Submit(refused)).Throws<SilkGpuStagingBudgetExceededException>();
        }
        submission.Wait();
        submission.Dispose();
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        byte[] readback = new byte[pixels.Length];
        texture.ReadbackForTesting(readback);
        await Assert.That(readback.SequenceEqual(pixels)).IsTrue();
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(staging.Usage.PeakReservedBytes).IsEqualTo(expectedBytes);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 784ul)]
    [Arguments(SilkGraphicsBackend.Vulkan, 64ul)]
    public async Task OneByteUnderUploadCapacityRefusesAndReleasesAllPendingCredit(
        SilkGraphicsBackend backend, ulong expectedBytes)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var staging = new SilkGpuStagingBudget(expectedBytes - 1);
        staging.ConfigureDevice(device);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, new byte[64]);
        SilkGpuStagingBudgetExceededException? failure = await Assert.That(() => device.Submit(commands))
            .Throws<SilkGpuStagingBudgetExceededException>();
        await Assert.That(failure!.RequestedBytes).IsEqualTo(expectedBytes);
        await Assert.That(failure.MaximumBytes).IsEqualTo(expectedBytes - 1);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(staging.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 784ul)]
    [Arguments(SilkGraphicsBackend.Vulkan, 64ul)]
    public async Task LaterUploadRefusalReturnsEveryEarlierStagingReservation(
        SilkGraphicsBackend backend, ulong capacity)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var staging = new SilkGpuStagingBudget(capacity);
        staging.ConfigureDevice(device);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination));
        using ISilkGraphicsTexture second = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, new byte[64]);
        commands.UploadTexture(second, new byte[64]);
        await Assert.That(() => device.Submit(commands)).Throws<SilkGpuStagingBudgetExceededException>();
        await Assert.That(staging.Usage.PeakReservedBytes).IsEqualTo(capacity);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(staging.Usage.ReservationCount).IsEqualTo(0ul);
        using ISilkGraphicsCommandList retry = device.CreateCommandList();
        retry.UploadTexture(texture, new byte[64]);
        using ISilkGraphicsSubmission submitted = device.Submit(retry);
        submitted.Wait();
        submitted.Dispose();
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task PickReadbackIsAdmittedAndChargedUntilDisposal(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuStagingBudget(8192);
        budget.ConfigureDevice(device);
        using ISilkPickReadbackBuffer readback = ((ISilkPickingGraphicsDevice)device).CreatePickReadbackBuffer();
        await Assert.That(budget.Usage.ReservedBytes).IsGreaterThanOrEqualTo(4ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(1ul);
        readback.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task OneBytePickCapacityRefusesWithoutStrandingTheDevice(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuStagingBudget(1);
        budget.ConfigureDevice(device);
        await Assert.That(() => ((ISilkPickingGraphicsDevice)device).CreatePickReadbackBuffer())
            .Throws<SilkGpuStagingBudgetExceededException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task ComputeReadbackUsesTheIndependentStagingPoolAndRefusesBeforeCopy(
        SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var staging = new SilkGpuStagingBudget(15);
        staging.ConfigureDevice(device);
        using ISilkGraphicsBuffer buffer = device.CreateBuffer(16, SilkBufferUsage.Storage);
        byte[] result = Enumerable.Repeat((byte)0xa5, 16).ToArray();
        await Assert.That(() => buffer.ReadbackForTesting(result)).Throws<SilkGpuStagingBudgetExceededException>();
        await Assert.That(result.All(static value => value == 0xa5)).IsTrue();
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task ConfiguringAfterAnInternalUploadIsRefused(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            1, 1, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, new byte[4]);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();
        await Assert.That(() => new SilkGpuStagingBudget(1024).ConfigureDevice(device))
            .Throws<InvalidOperationException>();
    }
}
