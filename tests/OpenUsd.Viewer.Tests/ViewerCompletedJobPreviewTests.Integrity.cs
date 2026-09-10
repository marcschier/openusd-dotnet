// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerCompletedJobPreviewTests
{
    [Test]
    public async Task TamperingWithTheLastPngFailsTheWholeLoadAndReleasesEveryFileBeforeRetry()
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult job = files.CreateJob();
        string last = Path.Combine(job.OutputDirectory, job.Frames[^1].FileName);
        byte[] original = await File.ReadAllBytesAsync(last);
        byte[] changed = [.. original];
        changed[^1] ^= 1;
        await File.WriteAllBytesAsync(last, changed);

        await Assert.That(async () => await ViewerCompletedJobPreview.LoadAsync(job, default))
            .Throws<InvalidDataException>();

        foreach (RenderDiskFrameResult frame in job.Frames)
        {
            using var exclusive = new FileStream(Path.Combine(job.OutputDirectory, frame.FileName),
                FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            await Assert.That(exclusive.Length).IsEqualTo(frame.Bytes);
        }
        await File.WriteAllBytesAsync(last, original);
        using ViewerCompletedJobPreview preview = await ViewerCompletedJobPreview.LoadAsync(job, default);
        await Assert.That(preview.Frames.Count).IsEqualTo(3);
    }

    [Test]
    [Arguments("length")]
    [Arguments("dimensions")]
    [Arguments("crc")]
    [Arguments("adler")]
    [Arguments("trailing")]
    [Arguments("oversize")]
    [Arguments("wide-raster")]
    [Arguments("pixel-budget")]
    [Arguments("index")]
    [Arguments("traversal")]
    [Arguments("absolute")]
    [Arguments("alternate-stream")]
    public async Task InvalidRecordedOrEncodedLastFrameCannotProduceASheet(string defect)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult job = files.CreateJob();
        RenderDiskFrameResult last = job.Frames[^1];
        string path = Path.Combine(job.OutputDirectory, last.FileName);
        byte[] png = await File.ReadAllBytesAsync(path);
        if (defect is "length" or "trailing")
        {
            png = [.. png, 0];
        }
        else if (defect == "dimensions")
        {
            png = PngRgba8Writer.Encode(1, 2, [255, 0, 0, 128, 0, 0, 255, 64]);
        }
        else if (defect == "crc")
        {
            png[^1] ^= 1;
        }
        else if (defect == "adler")
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(33));
            png[41 + length - 1] ^= 1;
            BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(41 + length),
                PngCrc32.Calculate(png.AsSpan(37, 4), png.AsSpan(41, length)));
        }
        await File.WriteAllBytesAsync(path, png);
        if (defect != "length")
        {
            job = ViewerCompletedJobTestFiles.ReplaceLastFrame(
                job, bytes: png.Length, sha256: Convert.ToHexString(SHA256.HashData(png)));
        }
        job = defect switch
        {
            "wide-raster" => ViewerCompletedJobTestFiles.ReplaceLastFrame(job,
                state: last.State.WithViewport(new ViewportDimensions(8193, 1))),
            "pixel-budget" => ViewerCompletedJobTestFiles.ReplaceLastFrame(job,
                state: last.State.WithViewport(new ViewportDimensions(4097, 4096))),
            "index" => ViewerCompletedJobTestFiles.ReplaceLastFrame(job, index: 0),
            "traversal" => ViewerCompletedJobTestFiles.ReplaceLastFrame(job, name: "..\\frame-000000.png"),
            "absolute" => ViewerCompletedJobTestFiles.ReplaceLastFrame(job, name: path),
            "alternate-stream" => ViewerCompletedJobTestFiles.ReplaceLastFrame(job, name: last.FileName + ":other"),
            _ => job
        };
        if (defect == "oversize")
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.SetLength(64 * 1024 * 1024 + 1);
            }
            job = ViewerCompletedJobTestFiles.ReplaceLastFrame(job, bytes: 64 * 1024 * 1024 + 1);
        }

        await Assert.That(async () => await ViewerCompletedJobPreview.LoadAsync(job, default))
            .Throws<InvalidDataException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(4097)]
    public async Task EmptyOrExcessiveJobMetadataIsRejectedBeforeOpeningAnyPath(int count)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult first = files.CreateJob(1);
        var job = new RenderDiskJobResult(Path.Combine(files.Root, "does-not-exist"),
            Enumerable.Repeat(first.Frames[0], count).ToArray(), 0, []);

        await Assert.That(async () => await ViewerCompletedJobPreview.LoadAsync(job, default))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task PreCancelledLoadingDoesNotOpenTheRecordedOutput()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var job = new RenderDiskJobResult("not-an-output-path", [], 0, []);

        await Assert.That(async () => await ViewerCompletedJobPreview.LoadAsync(job, cancellation.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    [Arguments("job")]
    [Arguments("ancestor")]
    [Arguments("frame")]
    public async Task JobFrameAndAncestorReparsePointsAreRefused(string kind)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This junction fixture requires Windows.");
            return;
        }
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult job = files.CreateJob(1);
        string link = kind == "frame"
            ? Path.Combine(job.OutputDirectory, job.Frames[0].FileName) : Path.Combine(files.Root, "alias");
        if (kind == "frame")
        {
            File.Delete(link);
        }
        await ViewerPortableReviewFixture.CreateJunctionAsync(link, kind == "ancestor" ? files.Root : job.OutputDirectory);
        try
        {
            string output = kind switch
            {
                "ancestor" => Path.Combine(link, Path.GetFileName(job.OutputDirectory)),
                "job" => link,
                _ => job.OutputDirectory
            };
            var aliased = new RenderDiskJobResult(output, job.Frames.ToArray(), job.TotalBytes, []);

            await Assert.That(async () => await ViewerCompletedJobPreview.LoadAsync(aliased, default))
                .Throws<NotSupportedException>();
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Test]
    public async Task Exactly64MiBEncodedPngIsAcceptedButOneAdditionalByteIsNot()
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult job = files.CreateJob(1);
        string path = Path.Combine(job.OutputDirectory, job.Frames[0].FileName);
        byte[] original = await File.ReadAllBytesAsync(path);
        byte[] png = new byte[64 * 1024 * 1024];
        int offset = original.Length - 12;
        original.AsSpan(0, offset).CopyTo(png);
        int payload = png.Length - original.Length - 12;
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(offset), (uint)payload);
        "teSt"u8.CopyTo(png.AsSpan(offset + 4));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(offset + 8 + payload),
            PngCrc32.Calculate(png.AsSpan(offset + 4, 4), png.AsSpan(offset + 8, payload)));
        original.AsSpan(original.Length - 12).CopyTo(png.AsSpan(png.Length - 12));
        await File.WriteAllBytesAsync(path, png);
        job = ViewerCompletedJobTestFiles.ReplaceLastFrame(
            job, bytes: png.Length, sha256: Convert.ToHexString(SHA256.HashData(png)));

        using (ViewerCompletedJobPreview preview = await ViewerCompletedJobPreview.LoadAsync(job, default))
        {
            await Assert.That(Pixel(preview.Frames[0].Pixels, 0, 64)).IsEqualTo("FF000080");
        }
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(png.Length + 1);
        }
        job = ViewerCompletedJobTestFiles.ReplaceLastFrame(job, bytes: png.Length + 1);
        await Assert.That(async () => await ViewerCompletedJobPreview.LoadAsync(job, default))
            .Throws<InvalidDataException>();
    }

    [Test]
    [Arguments("eof")]
    [Arguments("growing")]
    public async Task SharedReaderRequiresStableLengthAndEofEvenWithTheRecordedHash(string defect)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult job = files.CreateJob(1);
        byte[] png = await File.ReadAllBytesAsync(Path.Combine(job.OutputDirectory, job.Frames[0].FileName));
        using var input = new ChangedStream(png, defect);

        await Assert.That(() => CompletedRenderFrameReader.ReadThumbnail(input, job.Frames[0], 256, 256, default))
            .Throws<InvalidDataException>();
        await Assert.That(input.CanRead).IsTrue();
    }

    [Test]
    [Arguments(0, 256)]
    [Arguments(1025, 256)]
    [Arguments(256, 0)]
    [Arguments(256, 1025)]
    public async Task SharedReaderRefusesUnboundedThumbnailDimensions(int width, int height)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskFrameResult frame = files.CreateJob(1).Frames[0];
        using var input = new MemoryStream();

        await Assert.That(() => CompletedRenderFrameReader.ReadThumbnail(input, frame, width, height, default))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task SharedReaderCancelsAfterReadingAndRejectsStreamsWithoutAnExactExtent()
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult job = files.CreateJob(1);
        byte[] png = await File.ReadAllBytesAsync(Path.Combine(job.OutputDirectory, job.Frames[0].FileName));
        using var cancellation = new CancellationTokenSource();
        using var input = new ChangedStream(png, "cancel", cancellation);
        await Assert.That(() => CompletedRenderFrameReader.ReadThumbnail(input, job.Frames[0], 256, 256, cancellation.Token))
            .Throws<OperationCanceledException>();
        using var unbounded = new ChangedStream(png, "unbounded");
        await Assert.That(() => CompletedRenderFrameReader.ReadThumbnail(unbounded, job.Frames[0], 256, 256, default))
            .Throws<ArgumentException>();
    }

    private sealed class ChangedStream(
        byte[] png, string defect, CancellationTokenSource? cancellation = null) : MemoryStream(png, writable: false)
    {
        public override bool CanSeek => defect != "unbounded";
        public override long Length => base.Length + (defect == "growing" && Position > 0 ? 1 : 0);
        public override int ReadByte() => defect == "eof" ? 0 : base.ReadByte();

        public override int Read(Span<byte> buffer)
        {
            int read = base.Read(buffer);
            cancellation?.Cancel();
            return read;
        }
    }
}
