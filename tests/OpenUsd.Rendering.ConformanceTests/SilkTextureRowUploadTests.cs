// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkTextureRowUploadTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, SilkTextureFormat.Rgba8Unorm)]
    [Arguments(SilkGraphicsBackend.Vulkan, SilkTextureFormat.Rgba8Unorm)]
    [Arguments(SilkGraphicsBackend.D3D12, SilkTextureFormat.Rgba32Float)]
    [Arguments(SilkGraphicsBackend.Vulkan, SilkTextureFormat.Rgba32Float)]
    [Arguments(SilkGraphicsBackend.D3D12, SilkTextureFormat.R32Float)]
    [Arguments(SilkGraphicsBackend.Vulkan, SilkTextureFormat.R32Float)]
    [Arguments(SilkGraphicsBackend.D3D12, SilkTextureFormat.R8Unorm)]
    [Arguments(SilkGraphicsBackend.Vulkan, SilkTextureFormat.R8Unorm)]
    public async Task EveryRowAndMipMatchesPackedUploadAndSnapshotsCallerBytes(
        SilkGraphicsBackend backend, SilkTextureFormat format)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var cpu = new SilkCpuTextureBudget(4096);
        var staging = new SilkGpuStagingBudget(4096);
        var textures = new SilkGpuTextureBudget(4096);
        new SilkTexturePreparationBudgets(cpu, textures, staging).ConfigureDevice(device);
        bool floatingPoint = format is SilkTextureFormat.Rgba32Float or SilkTextureFormat.R32Float;
        byte[] source = new byte[5 * 3 * SilkTextureFormats.GetBytesPerPixel(format)];
        if (floatingPoint)
        {
            Span<float> values = MemoryMarshal.Cast<byte, float>(source.AsSpan());
            for (int index = 0; index < values.Length; index++)
            { values[index] = (index - 12) * 0.125f; }
        }
        else
        {
            for (int index = 0; index < source.Length; index++)
            { source[index] = checked((byte)(index * 3)); }
        }
        byte[] expected = SilkMipGenerator.GenerateChain(source, 5, 3, format, false, out uint count);
        using (ISilkGraphicsTexture texture = device.CreateTexture2D(new(5, 3, format,
            SilkTextureUsage.Sampled | SilkTextureUsage.CopySource | SilkTextureUsage.CopyDestination, count)))
        {
            foreach (SilkMipLevelLayout mip in SilkMipChainLayout.Create(5, 3, format, count))
            {
                for (uint row = 0; row < mip.Height; row++)
                {
                    using ISilkGraphicsCommandList commands = device.CreateCommandList();
                    var upload = (ISilkTextureRowUploadCommandList)commands;
                    byte[] data = expected.AsSpan(checked(mip.Offset + (int)row * mip.RowPitch), mip.RowPitch).ToArray();
                    upload.UploadTextureRows(texture, new(mip.Level, row, 1), data);
                    Array.Fill(data, (byte)0xEE);
                    using ISilkGraphicsSubmission submission = device.Submit(commands);
                    submission.Wait();
                }
            }
            byte[] actual = new byte[expected.Length];
            var readback = (ISilkMaterialMipReadback)texture;
            foreach (SilkMipLevelLayout mip in SilkMipChainLayout.Create(5, 3, format, count))
            {
                readback.ReadMipForTesting(mip.Level, actual.AsSpan(mip.Offset, mip.Size));
            }
            await Assert.That(actual.SequenceEqual(expected)).IsTrue();
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
            using ISilkGraphicsCommandList invalid = device.CreateCommandList();
            var rowCommands = (ISilkTextureRowUploadCommandList)invalid;
            await Assert.That(() => rowCommands.UploadTextureRows(texture, new(count, 0, 1), []))
                .Throws<ArgumentOutOfRangeException>();
            await Assert.That(() => rowCommands.UploadTextureRows(texture, new(0, 3, 1), []))
                .Throws<InvalidOperationException>();
            await Assert.That(() => rowCommands.UploadTextureRows(texture, new(0, 0, 1), []))
                .Throws<ArgumentException>();
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
    }
}
