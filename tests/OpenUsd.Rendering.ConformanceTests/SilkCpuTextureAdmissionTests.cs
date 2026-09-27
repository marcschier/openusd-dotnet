// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkCpuTextureAdmissionTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task CommandPixelCopiesAreAdmittedAndReleasedByTheirOwnOwner(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var cpu = new SilkCpuTextureBudget(64);
        var staging = new SilkGpuStagingBudget(4096);
        cpu.ConfigureDevice(device);
        staging.ConfigureDevice(device);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(new SilkTextureDescriptor(
            4, 4, SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopyDestination | SilkTextureUsage.CopySource));
        byte[] pixels = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, pixels);
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(64ul);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        using (ISilkGraphicsCommandList refused = device.CreateCommandList())
        {
            await Assert.That(() => refused.UploadTexture(texture, pixels))
                .Throws<SilkCpuTextureBudgetExceededException>();
        }
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        commands.Dispose();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(staging.Usage.ReservedBytes).IsGreaterThan(0ul);
        submission.Wait();
        submission.Dispose();
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        byte[] readback = new byte[64];
        texture.ReadbackForTesting(readback);
        await Assert.That(readback.SequenceEqual(pixels)).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task CpuPolicyCannotBeInstalledAfterResourceOwnerConstruction(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var resources = new SilkSceneGpuResources(device);
        await Assert.That(() => new SilkCpuTextureBudget(64).ConfigureDevice(device))
            .Throws<InvalidOperationException>();
    }
}
