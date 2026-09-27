// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

[NotInParallel]
public sealed class SilkNativeImageDecoderTests
{
    [Test]
    public async Task CpuBudgetRefusesBeforeAllocatingPixelsAndHoldsCreditUntilOwnerDisposal()
    {
        RequireNativeImages();
        string root = Directory.CreateTempSubdirectory("silk-cpu-decode-").FullName;
        try
        {
            string path = WriteHdr(root, 256, 256);
            using SilkDecodedImage warm = SilkNativeImageDecoder.Decode(path, false);
            var tooSmall = new SilkCpuTextureBudget(1_048_575);
            long before = GC.GetAllocatedBytesForCurrentThread();
            SilkCpuTextureBudgetExceededException? failure = null;
            try
            {
                using SilkDecodedImage refused = SilkNativeImageDecoder.Decode(path, false, tooSmall);
            }
            catch (SilkCpuTextureBudgetExceededException error)
            {
                failure = error;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.RequestedBytes).IsEqualTo(1_048_576ul);
            await Assert.That(allocated).IsLessThan(65_536L);
            await Assert.That(tooSmall.Usage.ReservedBytes).IsEqualTo(0ul);
            var budget = new SilkCpuTextureBudget(1_048_576);
            using SilkDecodedImage image = SilkNativeImageDecoder.Decode(path, false, budget);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(1_048_576ul);
            await Assert.That(image.Pixels.SequenceEqual(warm.Pixels)).IsTrue();
            await Assert.That(() => SilkNativeImageDecoder.Decode(path, false, budget))
                .Throws<SilkCpuTextureBudgetExceededException>();
            image.Dispose();
            image.Dispose();
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(1, 1, false)]
    [Arguments(3, 2, false)]
    [Arguments(17, 3, true)]
    public async Task HdrDecodePreservesFloatPixelsAndRequestedTransfer(int width, int height, bool linearize)
    {
        RequireNativeImages();
        string root = Directory.CreateTempSubdirectory("silk-hdr-decode-").FullName;
        try
        {
            string path = WriteHdr(root, width, height);
            byte[] source = File.ReadAllBytes(path);
            SilkDecodedImage image = SilkNativeImageDecoder.Decode(path, linearize);
            await Assert.That(image.Width).IsEqualTo((uint)width);
            await Assert.That(image.Height).IsEqualTo((uint)height);
            await Assert.That(image.Format).IsEqualTo(SilkTextureFormat.Rgba32Float);
            await Assert.That(image.Pixels.Length).IsEqualTo(width * height * 16);
            float[] actual = MemoryMarshal.Cast<byte, float>(image.Pixels).ToArray();
            for (int pixel = 0; pixel < width * height; pixel++)
            {
                float[] expected = (pixel % width % 3) switch
                {
                    0 => [2f, 1f, 0.5f],
                    1 => [0.125f, 0.25f, 0.5f],
                    _ => [0f, 0f, 0f]
                };
                for (int channel = 0; channel < 3; channel++)
                {
                    double value = expected[channel];
                    if (linearize)
                    {
                        value = value <= 0.04045
                            ? value / 12.92
                            : Math.Pow((value + 0.055) / 1.055, 2.4);
                    }
                    await Assert.That(actual[(pixel * 4) + channel]).IsEqualTo((float)value).Within(2e-6f);
                }
                await Assert.That(actual[(pixel * 4) + 3]).IsEqualTo(1f);
            }
            await Assert.That(File.ReadAllBytes(path).SequenceEqual(source)).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(256)]
    [Arguments(512)]
    public async Task HdrDecodeAllocatesOneOwnedPixelPayload(int size)
    {
        RequireNativeImages();
        string root = Directory.CreateTempSubdirectory("silk-hdr-allocation-").FullName;
        try
        {
            int payloadBytes = size * size * 16;
            string path = WriteHdr(root, size, size);
            _ = SilkNativeImageDecoder.Decode(path, false);
            long before = GC.GetAllocatedBytesForCurrentThread();
            SilkDecodedImage image = SilkNativeImageDecoder.Decode(path, false);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine($"HDR payload={payloadBytes}; managed decode allocation={allocated}.");
            await Assert.That(image.Pixels.Length).IsEqualTo(payloadBytes);
            await Assert.That(image.Format).IsEqualTo(SilkTextureFormat.Rgba32Float);
            await Assert.That(allocated).IsGreaterThanOrEqualTo((long)payloadBytes);
            // Permit object and interop bookkeeping, but not a second full-size pixel array.
            await Assert.That(allocated).IsLessThanOrEqualTo(payloadBytes + 16384L);
            await Assert.That(MemoryMarshal.Read<float>(image.Pixels)).IsEqualTo(2f);
            await Assert.That(MemoryMarshal.Read<float>(image.Pixels.AsSpan(payloadBytes - sizeof(float))))
                .IsEqualTo(1f);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task MissingHdrDoesNotReturnAnImageAndCanBeRetriedAfterCreation()
    {
        RequireNativeImages();
        string root = Directory.CreateTempSubdirectory("silk-hdr-refusal-").FullName;
        try
        {
            string path = Path.Combine(root, "source.hdr");
            await Assert.That(() => SilkNativeImageDecoder.Decode(path, false)).Throws<FileNotFoundException>();
            _ = WriteHdr(root, 3, 2);
            SilkDecodedImage image = SilkNativeImageDecoder.Decode(path, false);
            await Assert.That(image.Pixels.Length).IsEqualTo(96);
            await Assert.That(MemoryMarshal.Read<float>(image.Pixels)).IsEqualTo(2f);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string WriteHdr(string root, int width, int height)
    {
        string path = Path.Combine(root, "source.hdr");
        using FileStream output = File.Create(path);
        output.Write(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture,
            $"#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y {height} +X {width}\n")));
        byte[] row = new byte[width * 4];
        for (int column = 0; column < width; column++)
        {
            ReadOnlySpan<byte> pixel = (column % 3) switch
            {
                0 => [128, 64, 32, 130],
                1 => [32, 64, 128, 128],
                _ => [0, 0, 0, 0]
            };
            pixel.CopyTo(row.AsSpan(column * 4));
        }
        for (int line = 0; line < height; line++)
        {
            output.Write(row);
        }
        return path;
    }

    private static void RequireNativeImages()
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED=1 with the matching native image runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
    }
}
