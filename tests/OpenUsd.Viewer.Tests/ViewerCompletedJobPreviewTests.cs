// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerCompletedJobPreviewTests
{
    [Test]
    public async Task CompletedJobsSampleSixteenOrderedFramesWithEndpointsWithoutRenderingAgain()
    {
        string root = Directory.CreateTempSubdirectory("viewer-results-").FullName;
        try
        {
            var source = new CompletedSource();
            StageRenderState state = StageRenderState.Create(new StageIdentity("completed-stage"))
                .WithViewport(new ViewportDimensions(2, 1));
            RenderDiskJobResult job = RenderDiskJob.Execute(new RenderDiskJobRequest(
                Path.Combine(root, "render-sequence-original"),
                Enumerable.Range(0, 20).Select(index => state.WithTime(new StageTime(index + 0.5))).ToArray()),
                source);
            source.Closed = true;

            using ViewerCompletedJobPreview preview = await ViewerCompletedJobPreview.LoadAsync(job, default);

            await Assert.That(preview.Frames.Select(static frame => frame.Index)
                .SequenceEqual([0, 1, 2, 3, 5, 6, 7, 8, 10, 11, 12, 13, 15, 16, 17, 19])).IsTrue();
            await Assert.That(preview.Frames[0].TimeCode).IsEqualTo(0.5);
            await Assert.That(preview.Frames[^1].TimeCode).IsEqualTo(19.5);
            await Assert.That(preview.Frames.Sum(static frame => frame.Pixels.Length)).IsEqualTo(4 * 1024 * 1024);
            foreach (ViewerCompletedFramePreview frame in preview.Frames)
            {
                await Assert.That(frame.Width).IsEqualTo(256);
                await Assert.That(frame.Height).IsEqualTo(256);
                await Assert.That(frame.Pixels.AsSpan(0, 256 * 64 * 4).IndexOfAnyExcept((byte)0)).IsEqualTo(-1);
                await Assert.That(Convert.ToHexString(frame.Pixels.AsSpan((64 * 256) * 4, 4)))
                    .IsEqualTo("FF000080");
                await Assert.That(Convert.ToHexString(frame.Pixels.AsSpan((191 * 256 + 255) * 4, 4)))
                    .IsEqualTo("0000FF40");
                await Assert.That(frame.Pixels.AsSpan(192 * 256 * 4).IndexOfAnyExcept((byte)0)).IsEqualTo(-1);
            }
            await Assert.That(source.Calls).IsEqualTo(20);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments(1, "0")]
    [Arguments(16, "0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15")]
    [Arguments(17, "0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,16")]
    public async Task SmallJobsAndTheExactTileLimitKeepTheirOriginalIndices(int count, string expected)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult job = files.CreateJob(count);
        using ViewerCompletedJobPreview preview = await ViewerCompletedJobPreview.LoadAsync(job, default);

        await Assert.That(string.Join(",", preview.Frames.Select(static frame => frame.Index))).IsEqualTo(expected);
        await Assert.That(preview.Frames.Sum(static frame => frame.Pixels.Length)).IsLessThanOrEqualTo(4 * 1024 * 1024);
    }

    [Test]
    public async Task MaximumCompletedJobSamplesOnlySixteenFilesAndIncludesFrame4095()
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult first = files.CreateJob(1);
        RenderDiskFrameResult template = first.Frames[0];
        byte[] png = await File.ReadAllBytesAsync(Path.Combine(first.OutputDirectory, template.FileName));
        int[] selected = [0, 273, 546, 819, 1092, 1365, 1638, 1911, 2184, 2457, 2730, 3003, 3276, 3549, 3822, 4095];
        var frames = new RenderDiskFrameResult[4096];
        for (int index = 0; index < frames.Length; index++)
        {
            frames[index] = new RenderDiskFrameResult(index, template.State.WithTime(new StageTime(index)),
                FormattableString.Invariant($"frame-{index:D6}.png"), template.Bytes, template.Sha256);
            if (index != 0 && selected.Contains(index))
            {
                await File.WriteAllBytesAsync(Path.Combine(first.OutputDirectory, frames[index].FileName), png);
            }
        }
        var job = new RenderDiskJobResult(first.OutputDirectory, frames, template.Bytes * frames.Length, []);

        using ViewerCompletedJobPreview preview = await ViewerCompletedJobPreview.LoadAsync(job, default);

        await Assert.That(preview.Frames.Select(static frame => frame.Index).SequenceEqual(selected)).IsTrue();
        await Assert.That(preview.Frames[^1].TimeCode).IsEqualTo(4095d);
        await Assert.That(preview.Frames.Sum(static frame => frame.Pixels.Length)).IsEqualTo(4 * 1024 * 1024);
    }

    [Test]
    public async Task PortraitBottomUpPngKeepsOrientationAlphaAndAsymmetricLetterboxing()
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult job = files.CreateJob(1, 2, 3,
            [0, 0, 255, 64, 255, 255, 255, 0, 10, 20, 30, 40, 50, 60, 70, 80, 255, 0, 0, 128, 0, 255, 0, 255],
            Rgba8RowOrder.BottomUp);

        using ViewerCompletedJobPreview preview = await ViewerCompletedJobPreview.LoadAsync(job, default);
        byte[] pixels = preview.Frames[0].Pixels;

        await Assert.That(Pixel(pixels, 41, 0)).IsEqualTo("00000000");
        await Assert.That(Pixel(pixels, 42, 0)).IsEqualTo("FF000080");
        await Assert.That(Pixel(pixels, 212, 0)).IsEqualTo("00FF00FF");
        await Assert.That(Pixel(pixels, 213, 0)).IsEqualTo("00000000");
        await Assert.That(Pixel(pixels, 42, 86)).IsEqualTo("0A141E28");
        await Assert.That(Pixel(pixels, 212, 86)).IsEqualTo("323C4650");
        await Assert.That(Pixel(pixels, 42, 255)).IsEqualTo("0000FF40");
        await Assert.That(Pixel(pixels, 212, 255)).IsEqualTo("FFFFFF00");
        await Assert.That(preview.Frames[0].SourceDimensions).IsEqualTo(new ViewportDimensions(2, 3));
    }

    [Test]
    public async Task DisposingThePreviewReleasesEveryTileRasterAndIsIdempotent()
    {
        using var files = new ViewerCompletedJobTestFiles();
        using ViewerCompletedJobPreview preview =
            await ViewerCompletedJobPreview.LoadAsync(files.CreateJob(16), default);
        ViewerCompletedFramePreview[] frames = preview.Frames.ToArray();

        preview.Dispose();
        preview.Dispose();

        await Assert.That(preview.Frames).IsEmpty();
        foreach (ViewerCompletedFramePreview frame in frames)
        {
            await Assert.That(frame.Pixels).IsEmpty();
        }
    }

    private static string Pixel(byte[] pixels, int x, int y) =>
        Convert.ToHexString(pixels.AsSpan((y * 256 + x) * 4, 4));

    private sealed class CompletedSource : IRenderJobFrameSource
    {
        internal int Calls { get; private set; }
        internal bool Closed { get; set; }

        public RenderJobImage Render(StageRenderState state, CancellationToken cancellationToken)
        {
            if (Closed)
            {
                throw new InvalidOperationException("The completed source is closed; results must not render.");
            }
            Calls++;
            return new RenderJobImage(2, 1, new byte[] { 255, 0, 0, 128, 0, 0, 255, 64 }, Rgba8RowOrder.TopDown);
        }
    }
}
