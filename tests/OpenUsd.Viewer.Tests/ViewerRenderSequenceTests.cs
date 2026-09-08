// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerRenderSequenceTests
{
    [Test]
    public async Task ExrChoiceIsExplicitAndRetainsTheExtendedCaptureQuota()
    {
        var viewport = new ViewportDimensions(4096, 820);
        var defaults = new ViewerRenderSequenceOutputOptions(false, true);
        await Assert.That(defaults.HdrColorFormat).IsEqualTo(RenderHdrColorFormat.RawRgba16Float);
        await Assert.That(new ViewerRenderSequenceOutputOptions(false, false, RenderHdrColorFormat.Exr)
            .GetAdmissionUnsupportedReason(viewport)).Contains("requires HDR");
        await Assert.That(new ViewerRenderSequenceOutputOptions(false, true, (RenderHdrColorFormat)9)
            .GetAdmissionUnsupportedReason(viewport)).Contains("Choose");
        var exr = new ViewerRenderSequenceOutputOptions(true, true, RenderHdrColorFormat.Exr);
        await Assert.That(exr.GetAdmissionUnsupportedReason(viewport))
            .Contains(ExrRgba16FloatWriter.IsSupported ? "20 bytes/pixel" : "Windows x64");
        if (ExrRgba16FloatWriter.IsSupported)
        {
            await Assert.That(exr.GetAdmissionUnsupportedReason(new ViewportDimensions(4096, 819))).IsNull();
        }
    }

    [Test]
    public async Task InclusiveFractionalRangeUsesTheExactRequestedEndpoint()
    {
        var range = new ViewerRenderSequenceRange(0, 0.3, 0.1);
        await Assert.That(range.Times.SequenceEqual([0d, 0.1, 0.2, 0.3])).IsTrue();
    }

    [Test]
    public async Task RangeAdmitsExactly4096FramesWithoutTruncation()
    {
        var range = new ViewerRenderSequenceRange(1, 4096, 1);
        await Assert.That(range.Times.Count).IsEqualTo(4096);
        await Assert.That(range.Times[0]).IsEqualTo(1d);
        await Assert.That(range.Times[^1]).IsEqualTo(4096d);
    }

    [Test]
    [Arguments(0, 4096, 1)]
    [Arguments(2, 1, 1)]
    [Arguments(0, 1, 0)]
    [Arguments(0, 1, -1)]
    [Arguments(double.NaN, 1, 1)]
    [Arguments(0, double.PositiveInfinity, 1)]
    [Arguments(1e20, 1e20 + 16384, 8)]
    public async Task InvalidOrIndistinguishableFramesAreRefused(double start, double end, double step)
    {
        await Assert.That(() => new ViewerRenderSequenceRange(start, end, step)).Throws<ArgumentException>();
    }

    [Test]
    public async Task OutputNamesAreNewChildrenWithoutPublishingOrCreatingAnything()
    {
        string root = Directory.CreateTempSubdirectory("viewer-sequence-range-").FullName;
        try
        {
            string first = ViewerRenderSequenceRange.CreateOutputDirectory(root);
            string second = ViewerRenderSequenceRange.CreateOutputDirectory(root);
            await Assert.That(Path.GetDirectoryName(first)).IsEqualTo(root);
            await Assert.That(first).IsNotEqualTo(second);
            await Assert.That(Path.GetFileName(first)).StartsWith("render-sequence-");
            await Assert.That(Directory.EnumerateFileSystemEntries(root)).IsEmpty();
            await Assert.That(() => ViewerRenderSequenceRange.CreateOutputDirectory("relative"))
                .Throws<ArgumentException>();
            await Assert.That(() => ViewerRenderSequenceRange.CreateOutputDirectory(Path.Combine(root, "missing")))
                .Throws<DirectoryNotFoundException>();
        }
        finally
        {
            Directory.Delete(root);
        }
    }

    [Test]
    [Arguments(false, false, 4096, 4096, true)]
    [Arguments(false, false, 8192, 2048, true)]
    [Arguments(false, false, 8193, 1, false)]
    [Arguments(true, false, 4096, 819, true)]
    [Arguments(false, true, 4096, 819, true)]
    [Arguments(true, true, 4096, 819, true)]
    [Arguments(true, false, 4096, 820, false)]
    [Arguments(false, true, 4096, 820, false)]
    [Arguments(true, true, 4096, 820, false)]
    public async Task SelectedPlaneAdmissionMatchesTheDiskJobBeforeRendering(
        bool depth, bool hdr, int width, int height, bool admitted)
    {
        var outputs = new ViewerRenderSequenceOutputOptions(depth, hdr);
        var viewport = new ViewportDimensions(width, height);
        StageRenderState state = StageRenderState.Create(new StageIdentity("admission.usda")).WithViewport(viewport);
        string? reason = outputs.GetAdmissionUnsupportedReason(viewport);
        await Assert.That(reason is null).IsEqualTo(admitted);
        string output = Path.Combine(AppContext.BaseDirectory, "unpublished-admission");
        if (admitted)
        {
            var request = new RenderDiskJobRequest(output, [state], depth, hdr);
            await Assert.That(request.IncludeDeviceDepth).IsEqualTo(depth);
            await Assert.That(request.IncludeHdrColor).IsEqualTo(hdr);
        }
        else
        {
            await Assert.That(() => new RenderDiskJobRequest(output, [state], depth, hdr))
                .Throws<ArgumentOutOfRangeException>();
        }
        if (!admitted && (depth || hdr))
        {
            await Assert.That(reason).Contains("20 bytes/pixel");
        }
    }
}
