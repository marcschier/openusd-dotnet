// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkGpuTextureAdmissionTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task OnePolicySharesEveryConfiguredPoolAcrossRealDevicesAndReleasesAllCredit(
        SilkGraphicsBackend backend)
    {
        var budgets = new SilkTexturePreparationBudgets(new(128), new(128), new(1_000_000));
        using ISilkGraphicsDevice first = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsDevice second = SilkDepthCaptureConformance.CreateDevice(backend);
        budgets.ConfigureDevice(first);
        budgets.ConfigureDevice(second);
        budgets.ConfigureDevice(first);
        foreach (ISilkGraphicsDevice device in new[] { first, second })
        {
            await Assert.That(((ISilkCpuTextureAdmissionDevice)device).CpuTextureBudget)
                .IsSameReferenceAs(budgets.CpuTextureBudget);
            await Assert.That(((ISilkTextureAdmissionDevice)device).GpuTextureBudget)
                .IsSameReferenceAs(budgets.GpuTextureBudget);
            await Assert.That(((ISilkStagingAdmissionDevice)device).GpuStagingBudget)
                .IsSameReferenceAs(budgets.GpuStagingBudget);
        }
        var descriptor = new SilkTextureDescriptor(4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination | SilkTextureUsage.CopySource);
        using ISilkGraphicsTexture a = first.CreateTexture2D(descriptor);
        using ISilkGraphicsTexture b = second.CreateTexture2D(descriptor);
        await Assert.That(budgets.GpuTextureBudget!.Usage.ReservedBytes).IsEqualTo(128ul);
        await Assert.That(() => second.CreateTexture2D(descriptor)).Throws<SilkGpuTextureBudgetExceededException>();
        byte[] pixels = Enumerable.Range(0, 64).Select(static value => (byte)value).ToArray();
        using (ISilkGraphicsCommandList aCommands = first.CreateCommandList())
        using (ISilkGraphicsCommandList bCommands = second.CreateCommandList())
        {
            aCommands.UploadTexture(a, pixels);
            bCommands.UploadTexture(b, pixels);
            await Assert.That(budgets.CpuTextureBudget!.Usage.ReservedBytes).IsEqualTo(128ul);
            using ISilkGraphicsCommandList excess = first.CreateCommandList();
            await Assert.That(() => excess.UploadTexture(a, pixels)).Throws<SilkCpuTextureBudgetExceededException>();
            using ISilkGraphicsSubmission aSubmission = first.Submit(aCommands);
            using ISilkGraphicsSubmission bSubmission = second.Submit(bCommands);
            aSubmission.Wait();
            bSubmission.Wait();
        }
        byte[] actual = new byte[64];
        a.ReadbackForTesting(actual);
        await Assert.That(actual.SequenceEqual(pixels)).IsTrue();
        b.ReadbackForTesting(actual);
        await Assert.That(actual.SequenceEqual(pixels)).IsTrue();
        await Assert.That(budgets.GpuStagingBudget!.Usage.PeakReservedBytes).IsGreaterThan(0ul);
        a.Dispose();
        b.Dispose();
        await Assert.That(budgets.CpuTextureBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budgets.GpuTextureBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budgets.GpuStagingBudget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task RealTexturesShareMipAndVolumeCapacityWithoutChargingTheBufferPool(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice first = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsDevice second = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(168);
        var buffers = new SilkGpuBufferBudget(16);
        budget.ConfigureDevice(first);
        budget.ConfigureDevice(second);
        buffers.ConfigureDevice(first);
        using var resources = new SilkSceneGpuResources(first);
        RenderDiagnosticsState diagnostics = resources.Diagnostics;
        using ISilkGraphicsTexture mips = first.CreateTexture2D(
            new SilkTextureDescriptor(5, 3, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.Sampled, 3));
        using ISilkGraphicsTexture volume = ((ISilkVolumeTextureGraphicsDevice)second).CreateTexture3D(
            2, 3, 4, SilkTextureFormat.R32Float);
        using ISilkGraphicsBuffer buffer = first.CreateBuffer(16, SilkBufferUsage.Upload);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(168ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(2ul);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(16ul);
        await Assert.That(() => first.CreateTexture2D(1, 1)).Throws<SilkGpuTextureBudgetExceededException>();
        mips.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(96ul);
        using ISilkGraphicsTexture retry = second.CreateTexture2D(
            new SilkTextureDescriptor(5, 3, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.Sampled, 3));
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(168ul);
        retry.Dispose();
        volume.Dispose();
        buffer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(resources.Diagnostics.Equals(diagnostics)).IsTrue();
        await Assert.That(diagnostics.Entries.Single(
            entry => entry.Code == "HDSILK_GPU_TEXTURE_ADMISSION").Message).Contains("ceiling=168");
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task RealSubmissionRetainsDisposedTextureCreditUntilNativeRelease(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(16384);
        await OffscreenRhiConformance.SubmittedTextureSurvivesEarlyDispose(device, budget);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(16384ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task OwnedNativeTexturesPreventLateBudgetConfiguration(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(1, 1);
        var budget = new SilkGpuTextureBudget(4);
        await Assert.That(() => budget.ConfigureDevice(device)).Throws<InvalidOperationException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }
}
