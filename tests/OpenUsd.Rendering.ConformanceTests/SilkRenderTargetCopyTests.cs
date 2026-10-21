// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkRenderTargetCopyTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, SilkTextureFormat.Rgba8Unorm)]
    [Arguments(SilkGraphicsBackend.Vulkan, SilkTextureFormat.Rgba8Unorm)]
    [Arguments(SilkGraphicsBackend.Metal, SilkTextureFormat.Rgba8Unorm)]
    [Arguments(SilkGraphicsBackend.D3D12, SilkTextureFormat.Rgba16Float)]
    [Arguments(SilkGraphicsBackend.Vulkan, SilkTextureFormat.Rgba16Float)]
    [Arguments(SilkGraphicsBackend.Metal, SilkTextureFormat.Rgba16Float)]
    [Arguments(SilkGraphicsBackend.D3D12, SilkTextureFormat.Rgba32Float)]
    [Arguments(SilkGraphicsBackend.Vulkan, SilkTextureFormat.Rgba32Float)]
    [Arguments(SilkGraphicsBackend.Metal, SilkTextureFormat.Rgba32Float)]
    public async Task OrderedCopiesPreserveEveryColorBit(SilkGraphicsBackend backend, SilkTextureFormat format)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var descriptor = new SilkTextureDescriptor(4, 3, format,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopyDestination | SilkTextureUsage.CopySource);
        using ISilkGraphicsTexture source = device.CreateTexture2D(descriptor);
        using ISilkGraphicsTexture first = device.CreateTexture2D(descriptor);
        using ISilkGraphicsTexture second = device.CreateTexture2D(descriptor);
        byte[] expected = CreatePixels(format);
        using (ISilkGraphicsCommandList commands = device.CreateCommandList())
        {
            var copies = (ISilkTextureCopyGraphicsCommandList)commands;
            commands.UploadTexture(source, expected);
            copies.CopyRenderTarget(source, first);
            copies.CopyRenderTarget(first, second);
            commands.ClearColor(first, new(0, 0, 0, 0));
            copies.CopyRenderTarget(second, first);
            using ISilkGraphicsSubmission submission = device.Submit(commands);
            submission.Wait();
        }
        byte[] actual = new byte[expected.Length];
        first.ReadbackForTesting(actual);
        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        using (ISilkGraphicsCommandList commands = device.CreateCommandList())
        {
            var copies = (ISilkTextureCopyGraphicsCommandList)commands;
            commands.ClearColor(second, new(0, 0, 0, 0));
            copies.CopyRenderTarget(first, second);
            using ISilkGraphicsSubmission submission = device.Submit(commands);
            submission.Wait();
        }
        second.ReadbackForTesting(actual);
        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task DepthCopiesPreserveTheExactStoredValue(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsTexture source = device.CreateTexture2D(SilkTextureDescriptor.SampledDepthTarget(4, 3));
        using ISilkGraphicsTexture destination = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(4, 3));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.ClearDepth(source, 0.375f);
        commands.ClearDepth(destination, 0.875f);
        ((ISilkTextureCopyGraphicsCommandList)commands).CopyRenderTarget(source, destination);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();
        float[] actual = new float[12];
        destination.ReadbackForTesting(actual);
        await Assert.That(actual.All(value => BitConverter.SingleToUInt32Bits(value) ==
            BitConverter.SingleToUInt32Bits(0.375f))).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task CopySubmissionRetainsBothTexturesWithoutPixelOrStagingAllocations(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var textures = new SilkGpuTextureBudget(96);
        var staging = new SilkGpuStagingBudget(1);
        var pixels = new SilkCpuTextureBudget(1);
        textures.ConfigureDevice(device);
        staging.ConfigureDevice(device);
        pixels.ConfigureDevice(device);
        using ISilkGraphicsTexture source = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(4, 3));
        using ISilkGraphicsTexture destination = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(4, 3));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.ClearColor(source, new(0.25f, 0.5f, 1, 1));
        ((ISilkTextureCopyGraphicsCommandList)commands).CopyRenderTarget(source, destination);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        source.Dispose();
        destination.Dispose();
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(96ul);
        await Assert.That(staging.Usage.PeakReservedBytes).IsEqualTo(0ul);
        await Assert.That(pixels.Usage.PeakReservedBytes).IsEqualTo(0ul);
        submission.Wait();
        submission.Dispose();
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "alias")]
    [Arguments(SilkGraphicsBackend.Vulkan, "alias")]
    [Arguments(SilkGraphicsBackend.Metal, "alias")]
    [Arguments(SilkGraphicsBackend.D3D12, "size")]
    [Arguments(SilkGraphicsBackend.Vulkan, "size")]
    [Arguments(SilkGraphicsBackend.Metal, "size")]
    [Arguments(SilkGraphicsBackend.D3D12, "format")]
    [Arguments(SilkGraphicsBackend.Vulkan, "format")]
    [Arguments(SilkGraphicsBackend.Metal, "format")]
    [Arguments(SilkGraphicsBackend.D3D12, "usage")]
    [Arguments(SilkGraphicsBackend.Vulkan, "usage")]
    [Arguments(SilkGraphicsBackend.Metal, "usage")]
    public async Task RejectedCopyDoesNotRecordOrCorruptLaterWork(SilkGraphicsBackend backend, string invalid)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsTexture source = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(4, 3));
        using ISilkGraphicsTexture valid = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(4, 3));
        SilkTextureDescriptor descriptor = invalid switch
        {
            "size" => SilkTextureDescriptor.ColorTarget(3, 4),
            "format" => SilkTextureDescriptor.HdrColorTarget(4, 3),
            "usage" => SilkTextureDescriptor.SampledRgba8(4, 3),
            _ => SilkTextureDescriptor.ColorTarget(4, 3)
        };
        using ISilkGraphicsTexture destination = device.CreateTexture2D(descriptor);
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        var copies = (ISilkTextureCopyGraphicsCommandList)commands;
        await Assert.That(() => copies.CopyRenderTarget(source, invalid == "alias" ? source : destination))
            .Throws<ArgumentException>();
        commands.ClearColor(source, new(1, 0, 0, 1));
        commands.ClearColor(valid, new(0, 1, 0, 1));
        copies.CopyRenderTarget(source, valid);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();
        byte[] actual = new byte[48];
        valid.ReadbackForTesting(actual);
        await Assert.That(Enumerable.Range(0, 12).All(index =>
            actual[index * 4] == 255 && actual[(index * 4) + 1] == 0 &&
            actual[(index * 4) + 2] == 0 && actual[(index * 4) + 3] == 255)).IsTrue();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    [Arguments(SilkGraphicsBackend.Metal)]
    public async Task CopyRejectsForeignDisposedAndActivelyRenderedTargets(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsDevice other = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsTexture source = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(4, 3));
        using ISilkGraphicsTexture destination = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(4, 3));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(4, 3));
        using ISilkGraphicsTexture foreign = other.CreateTexture2D(SilkTextureDescriptor.ColorTarget(4, 3));
        using ISilkGraphicsTexture disposed = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(4, 3));
        disposed.Dispose();
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        var copies = (ISilkTextureCopyGraphicsCommandList)commands;
        await Assert.That(() => copies.CopyRenderTarget(foreign, destination)).Throws<ArgumentException>();
        await Assert.That(() => copies.CopyRenderTarget(source, foreign)).Throws<ArgumentException>();
        await Assert.That(() => copies.CopyRenderTarget(disposed, destination)).Throws<ObjectDisposedException>();
        await Assert.That(() => copies.CopyRenderTarget(source, disposed)).Throws<ObjectDisposedException>();
        commands.BeginRendering(new(source, depth));
        await Assert.That(() => copies.CopyRenderTarget(source, destination)).Throws<InvalidOperationException>();
        commands.EndRendering();
        commands.ClearColor(source, new(0, 0, 1, 1));
        copies.CopyRenderTarget(source, destination);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();
        await Assert.That(() => copies.CopyRenderTarget(source, destination)).Throws<InvalidOperationException>();
        byte[] actual = new byte[48];
        destination.ReadbackForTesting(actual);
        await Assert.That(Enumerable.Range(0, 12).All(index =>
            actual[index * 4] == 0 && actual[(index * 4) + 1] == 0 &&
            actual[(index * 4) + 2] == 255 && actual[(index * 4) + 3] == 255)).IsTrue();
    }

    private static byte[] CreatePixels(SilkTextureFormat format)
    {
        float[] values = [.. Enumerable.Range(0, 48).Select(index => ((index * 7) - 32) / 8f)];
        return format switch
        {
            SilkTextureFormat.Rgba8Unorm => [.. Enumerable.Range(0, 48).Select(index => (byte)(index * 5))],
            SilkTextureFormat.Rgba16Float => MemoryMarshal.AsBytes<Half>(
                values.Select(value => (Half)value).ToArray()).ToArray(),
            SilkTextureFormat.Rgba32Float => MemoryMarshal.AsBytes<float>(values).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }
}
