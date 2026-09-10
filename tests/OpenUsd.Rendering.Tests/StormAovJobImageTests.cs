// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.Tests;

public sealed class StormAovJobImageTests
{
    [Test]
    public async Task ChildConversionKeepsNativePresentationSeparateFromRawAovPlanes()
    {
        StormAovSnapshot snapshot = Snapshot();
        byte[] nativeRgba = [3, 5, 7, 11, 13, 17, 19, 23];
        var framebuffer = new OpenUsdStormFramebufferCapture(
            1, 0, 2, 2, 2, 1, 96, 0, 0, 0, 0, 0, nativeRgba);
        var capture = new OpenUsdStormChildAovCapture(framebuffer, snapshot);
        RenderJobImage image = capture.CreateJobImage(true, true);
        await Assert.That(image.Rgba.Span.SequenceEqual(nativeRgba)).IsTrue();
        await Assert.That(image.RowOrder).IsEqualTo(Rgba8RowOrder.BottomUp);
        await Assert.That(image.DeviceDepth!.Values.ToArray()).IsEquivalentTo([0.25f, 1f]);
        await Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(image.HdrColor!.Rgba16Float.Span))
            .IsEqualTo((ushort)0x5400);
        await Assert.That(image.Diagnostics.Entries[0].Code).IsEqualTo("STORM_CHILD_AOV_JOB_IMAGE");
        if (!MemoryMarshal.TryGetArray(image.Rgba, out ArraySegment<byte> bytes))
        {
            throw new InvalidOperationException("The image must own its native display copy.");
        }
        bytes.Array![bytes.Offset] = 255;
        await Assert.That(capture.Framebuffer.RgbaPixels.Span[0]).IsEqualTo((byte)3);
    }

    [Test]
    public async Task ChildConversionChargesExistingNativePixelsAndEachNewPlane()
    {
        var capture = new OpenUsdStormChildAovCapture(
            new OpenUsdStormFramebufferCapture(1, 0, 2, 2, 2, 1, 96, 0, 0, 0, 0, 0, new byte[8]),
            Snapshot());
        await Assert.That(capture.ManagedStorageUpperBound).IsEqualTo(4616UL);
        await Assert.That(() => capture.CreateJobImage(true, true, maximumManagedBytes: 5159))
            .Throws<RenderOutputQuotaExceededException>();
        await Assert.That(capture.CreateJobImage(true, true, maximumManagedBytes: 5160).Rgba.Length)
            .IsEqualTo(8);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(() => capture.CreateJobImage(cancellationToken: cancellation.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task NativePresentationNeedsOnlyTheAdditionalAovPlanesRequestedForExport()
    {
        var capture = new OpenUsdStormChildAovCapture(
            new OpenUsdStormFramebufferCapture(1, 0, 2, 2, 2, 1, 96, 0, 0, 0, 0, 0, new byte[8]),
            Snapshot("missing-color", hasDisplaySelection: true));
        RenderJobImage depth = capture.CreateJobImage(includeDeviceDepth: true);
        await Assert.That(depth.DeviceDepth!.Values.Span[0]).IsEqualTo(0.25f);
        await Assert.That(depth.HdrColor).IsNull();
        await Assert.That(() => capture.CreateJobImage(includeHdrColor: true)).Throws<NotSupportedException>();
    }

    [Test]
    public async Task ConversionPreservesRawHalfBitsAndDepthWhileDisplayConversionIsIndependent()
    {
        StormAovSnapshot snapshot = Snapshot();
        RenderJobImage raw = snapshot.CreateJobImage(includeDeviceDepth: true, includeHdrColor: true);
        RenderJobImage display = snapshot.CreateJobImage(includeDeviceDepth: true, includeHdrColor: true,
            outputTransform: RenderOutputTransform.Reinhard, exposure: -6);
        await Assert.That(raw.Rgba.Span.SequenceEqual(display.Rgba.Span)).IsFalse();
        await Assert.That(raw.HdrColor!.Rgba16Float.Span.SequenceEqual(display.HdrColor!.Rgba16Float.Span)).IsTrue();
        await Assert.That(raw.DeviceDepth!.Values.Span.SequenceEqual(display.DeviceDepth!.Values.Span)).IsTrue();
        await Assert.That(raw.RowOrder).IsEqualTo(Rgba8RowOrder.TopDown);
        await Assert.That(raw.Rgba.Span[3]).IsEqualTo((byte)128);
        await Assert.That(display.Rgba.Span[3]).IsEqualTo((byte)128);
        await Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(raw.HdrColor.Rgba16Float.Span))
            .IsEqualTo(BitConverter.HalfToUInt16Bits((Half)64));
        await Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(raw.HdrColor.Rgba16Float.Span[10..]))
            .IsEqualTo((ushort)0x8000);
        await Assert.That(raw.DeviceDepth.Values.ToArray()).IsEquivalentTo([0.25f, 1f]);
        await Assert.That(snapshot.GetOutput<StormAovColor>(StormAovKind.Color).GetPixel(0, 0).Red)
            .IsEqualTo((Half)64);
    }

    [Test]
    public async Task DefaultConversionProducesOnlyPngPixelsAndNeverExposesSnapshotStorage()
    {
        StormAovSnapshot snapshot = Snapshot();
        RenderJobImage image = snapshot.CreateJobImage();
        await Assert.That(image.HdrColor).IsNull();
        await Assert.That(image.DeviceDepth).IsNull();
        RenderJobImage copied = snapshot.CreateJobImage(true, true);
        if (!MemoryMarshal.TryGetArray(copied.DeviceDepth!.Values, out ArraySegment<float> depth))
        {
            throw new InvalidOperationException("The job image must own detached depth storage.");
        }
        depth.Array![depth.Offset] = 0.9f;
        await Assert.That(snapshot.GetOutput<float>(StormAovKind.Depth).GetPixel(0, 0)).IsEqualTo(0.25f);
        await Assert.That(image.Diagnostics.Entries[0].Code).IsEqualTo("STORM_AOV_JOB_IMAGE");
    }

    [Test]
    public async Task QuotaIncludesTheExistingSnapshotAndAllNewPlaneStorage()
    {
        StormAovSnapshot snapshot = Snapshot();
        long limit = checked((long)snapshot.ManagedStorageUpperBound + 512 + 2 * 16);
        await Assert.That(() => snapshot.CreateJobImage(true, true, maximumManagedBytes: limit - 1))
            .Throws<RenderOutputQuotaExceededException>();
        RenderJobImage image = snapshot.CreateJobImage(true, true, maximumManagedBytes: limit);
        await Assert.That(image.Width).IsEqualTo(2);
        await Assert.That(image.Height).IsEqualTo(1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(() => snapshot.CreateJobImage(cancellationToken: cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(() => snapshot.CreateJobImage(exposure: float.PositiveInfinity))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task SelectionBearingNativeColorCannotMasqueradeAsPreSelectionHdr()
    {
        StormAovSnapshot snapshot = Snapshot(hasDisplaySelection: true);
        RenderJobImage png = snapshot.CreateJobImage(includeDeviceDepth: true);
        await Assert.That(png.DeviceDepth).IsNotNull();
        await Assert.That(() => snapshot.CreateJobImage(includeHdrColor: true)).Throws<NotSupportedException>();
    }

    [Test]
    [Arguments("missing-color")]
    [Arguments("missing-depth")]
    [Arguments("normal")]
    [Arguments("depth-range")]
    [Arguments("color-nonfinite")]
    public async Task UnavailableOrUnrepresentablePlanesCannotProduceSuccessShapedImages(string invalid)
    {
        StormAovSnapshot snapshot = Snapshot(invalid);
        await Assert.That(() => snapshot.CreateJobImage(true, true)).Throws<InvalidDataException>();
    }

    private static unsafe StormAovSnapshot Snapshot(string? invalid = null, bool hasDisplaySelection = false)
    {
        var color = new StormAovNative.Output
        {
            Kind = StormAovKind.Color,
            Status = StormAovStatus.Ready,
            Format = StormAovFormat.Float16Vec4,
            Width = 2,
            Height = 1,
            RowStrideBytes = 16,
            Origin = StormAovOrigin.TopLeft
        };
        var depth = new StormAovNative.Output
        {
            Kind = StormAovKind.Depth,
            Status = StormAovStatus.Ready,
            Format = StormAovFormat.Float32,
            Width = 2,
            Height = 1,
            RowStrideBytes = 8,
            Origin = StormAovOrigin.TopLeft,
            DepthConvention = StormAovDepthConvention.OpenGlWindow
        };
        var outputs = new List<StormAovOutput>();
        if (invalid != "missing-color")
        {
            if (invalid == "normal")
            {
                color.Kind = StormAovKind.Neye;
            }
            outputs.Add(new StormAovOutput<StormAovColor>(in color,
            [
                new(invalid == "color-nonfinite" ? Half.NaN : (Half)64, (Half)0, (Half)0, (Half)0.5),
                new((Half)(-1), BitConverter.UInt16BitsToHalf(0x8000), (Half)0.25, (Half)1)
            ]));
        }
        if (invalid != "missing-depth")
        {
            outputs.Add(new StormAovOutput<float>(in depth, [invalid == "depth-range" ? 2 : 0.25f, 1f]));
        }
        var view = new StormAovNative.View { Width = 2, Height = 1, CaptureId = 1 };
        return new StormAovSnapshot(
            in view, [.. outputs], [], [], [], managedStorageUpperBound: 4096, hasDisplaySelection);
    }
}
