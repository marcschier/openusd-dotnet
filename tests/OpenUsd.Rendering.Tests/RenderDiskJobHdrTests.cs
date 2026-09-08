// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;

namespace OpenUsd.Rendering.Tests;

public sealed class RenderDiskJobHdrTests
{
    [Test]
    [Arguments("missing")]
    [Arguments("unexpected")]
    [Arguments("dimensions")]
    [Arguments("nonfinite-r")]
    [Arguments("nonfinite-g")]
    [Arguments("nonfinite-b")]
    [Arguments("nonfinite-a")]
    [Arguments("nan")]
    public async Task InvalidHdrPlanesCannotPublishAnApparentlyCompleteJob(string failure)
    {
        string root = Directory.CreateTempSubdirectory("openusd-job-hdr-invalid-").FullName;
        byte[] hdr = Convert.FromHexString("003000380040003C00440034002C0038");
        int channel = failure switch
        {
            "nonfinite-r" => 0,
            "nonfinite-g" => 1,
            "nonfinite-b" => 2,
            "nonfinite-a" => 3,
            "nan" => 3,
            _ => -1
        };
        if (channel >= 0)
        {
            hdr[channel * 2] = 0;
            hdr[channel * 2 + 1] = failure == "nan" ? (byte)0x7E : (byte)0x7C;
        }
        if (failure == "dimensions")
        {
            hdr = hdr[..8];
        }
        StageRenderState state = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(2, 1));
        var source = new HdrSource(hdr)
        {
            IncludeHdr = failure != "missing",
            HdrWidth = failure == "dimensions" ? 1 : 2
        };
        try
        {
            var request = new RenderDiskJobRequest(Path.Combine(root, "sequence"), [state],
                includeDeviceDepth: false, includeHdrColor: failure != "unexpected");
            if (failure is "missing" or "unexpected")
            {
                await Assert.That(() => RenderDiskJob.Execute(request, source)).Throws<NotSupportedException>();
            }
            else
            {
                await Assert.That(() => RenderDiskJob.Execute(request, source)).Throws<InvalidDataException>();
            }
            await Assert.That(Directory.GetFileSystemEntries(root)).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task HdrAdmissionReservesTwentyBytesPerPixelWithOrWithoutDepth(bool includeDepth)
    {
        string output = Path.Combine(Path.GetTempPath(), $"hdr-admission-{Guid.NewGuid():N}");
        StageRenderState state = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(2, 1));
        await Assert.That(() => new RenderDiskJobRequest(
            output, [state], includeDepth, includeHdrColor: true, new RenderDiskJobLimits(maximumFrameBytes: 39)))
            .Throws<ArgumentOutOfRangeException>();
        var request = new RenderDiskJobRequest(
            output, [state], includeDepth, includeHdrColor: true, new RenderDiskJobLimits(maximumFrameBytes: 40));
        await Assert.That(request.IncludeHdrColor).IsTrue();
        await Assert.That(request.IncludeDeviceDepth).IsEqualTo(includeDepth);
        await Assert.That(Directory.Exists(output)).IsFalse();
    }

    [Test]
    public async Task HdrAndDepthShareTheEncodedBudgetAndPreserveFiniteNegativeAndZeroBits()
    {
        string root = Directory.CreateTempSubdirectory("openusd-job-hdr-combined-").FullName;
        byte[] hdr = Convert.FromHexString("00B800800040003C00440034002C0038");
        StageRenderState state = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(2, 1));
        float[] depth = [0.25f, 1f];
        var source = new HdrSource(hdr) { Depth = new RenderJobDeviceDepth(2, 1, depth) };
        try
        {
            RenderDiskJobResult result = RenderDiskJob.Execute(new RenderDiskJobRequest(
                Path.Combine(root, "complete"), [state], includeDeviceDepth: true, includeHdrColor: true), source);
            RenderDiskFrameResult frame = result.Frames.Single();
            long totalFrame = frame.Bytes + frame.DepthBytes + frame.HdrColorBytes;
            await Assert.That(Convert.ToHexString(await File.ReadAllBytesAsync(
                Path.Combine(result.OutputDirectory, frame.HdrColorFileName!))))
                .IsEqualTo("00B800800040003C00440034002C0038");
            await Assert.That(frame.DepthBytes).IsEqualTo(8L);
            var shortFrame = new RenderDiskJobRequest(
                Path.Combine(root, "refused-frame"), [state], true, true,
                new RenderDiskJobLimits(maximumFrameBytes: totalFrame - 1));
            await Assert.That(() => RenderDiskJob.Execute(shortFrame, source))
                .Throws<RenderOutputQuotaExceededException>();
            var shortJob = new RenderDiskJobRequest(
                Path.Combine(root, "refused-job"), [state], true, true,
                new RenderDiskJobLimits(maximumTotalBytes: result.TotalBytes - 1));
            await Assert.That(() => RenderDiskJob.Execute(shortJob, source))
                .Throws<RenderOutputQuotaExceededException>();
            await Assert.That(Directory.GetDirectories(root)).IsEquivalentTo([result.OutputDirectory]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task HdrColorIsStoredBeforeDisplayWithoutChangingItsHalfBitsOrAlpha()
    {
        string root = Directory.CreateTempSubdirectory("openusd-job-hdr-").FullName;
        byte[] hdr = Convert.FromHexString("003000380040003C00440034002C0038");
        StageRenderState state = StageRenderState.Create(new StageIdentity("source.usda"))
            .WithViewport(new ViewportDimensions(2, 1));
        var source = new HdrSource(hdr);
        try
        {
            RenderDiskJobResult result = RenderDiskJob.Execute(new RenderDiskJobRequest(
                Path.Combine(root, "sequence"), [state], includeDeviceDepth: false, includeHdrColor: true), source);
            RenderDiskFrameResult frame = result.Frames.Single();
            byte[] actual = await File.ReadAllBytesAsync(Path.Combine(result.OutputDirectory, frame.HdrColorFileName!));
            await Assert.That(Convert.ToHexString(actual)).IsEqualTo("003000380040003C00440034002C0038");
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(
                Path.Combine(result.OutputDirectory, "manifest.json")));
            JsonElement metadata = manifest.RootElement.GetProperty("frames")[0].GetProperty("hdrColor");
            await Assert.That(metadata.GetProperty("format").GetString()).IsEqualTo("rgba16float-little-endian");
            await Assert.That(metadata.GetProperty("rowOrder").GetString()).IsEqualTo("top-down");
            await Assert.That(metadata.GetProperty("convention").GetString())
                .IsEqualTo("renderer-working-composited-before-exposure-and-display");
            await Assert.That(metadata.GetProperty("alpha").GetString()).IsEqualTo("stored-framebuffer");
            await Assert.That(metadata.GetProperty("displaySelectionIncluded").GetBoolean()).IsFalse();
            await Assert.That(frame.HdrColorBytes).IsEqualTo(16L);
            await Assert.That(result.TotalBytes).IsEqualTo(
                Directory.GetFiles(result.OutputDirectory).Sum(static path => new FileInfo(path).Length));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class HdrSource(byte[] hdr) : IRenderJobFrameSource
    {
        internal bool IncludeHdr { get; init; } = true;
        internal int HdrWidth { get; init; } = 2;
        internal RenderJobDeviceDepth? Depth { get; init; }

        public RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new RenderJobImage(2, 1, new byte[] { 32, 128, 255, 255, 255, 64, 16, 128 }, Rgba8RowOrder.TopDown)
            {
                HdrColor = IncludeHdr ? new RenderJobHdrColor(HdrWidth, 1, hdr) : null,
                DeviceDepth = Depth
            };
        }
    }
}
