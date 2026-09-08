// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerFrameFileWriterTests
{
    [Test]
    [Arguments(".png")]
    [Arguments(".BMP")]
    public async Task OnlyCompleteImagesReplaceTheDestinationAndFailuresLeaveNoTemporaryFiles(string extension)
    {
        string root = Directory.CreateTempSubdirectory("viewer-capture-export-").FullName;
        string path = Path.Combine(root, "frame" + extension);
        byte[] original = [7, 5, 3, 1];
        var frame = new ViewerFrameCaptureResult(2, 2,
            new byte[] { 0, 0, 255, 64, 10, 20, 30, 40, 255, 0, 0, 255, 0, 255, 0, 128 },
            ViewerFrameRowOrder.BottomUp);
        try
        {
            await File.WriteAllBytesAsync(path, original);
            await Assert.That(async () => await ViewerFrameFileWriter.WriteAsync(
                path, frame, CancellationToken.None, maximumBytes: 40)).Throws<InvalidOperationException>();
            await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(original)).IsTrue();
            await Assert.That(Directory.GetFiles(root).Single()).IsEqualTo(path);

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.That(async () => await ViewerFrameFileWriter.WriteAsync(path, frame, cancellation.Token))
                .Throws<OperationCanceledException>();
            await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(original)).IsTrue();
            await Assert.That(Directory.GetFiles(root).Single()).IsEqualTo(path);

            await ViewerFrameFileWriter.WriteAsync(path, frame, CancellationToken.None, maximumBytes: 1024);
            ViewerCapturePair decoded = await ViewerCaptureComparison.LoadAsync(path, path, CancellationToken.None);
            await Assert.That(Convert.ToHexString(decoded.Before.Rgba)).IsEqualTo(extension == ".png"
                ? "FF0000FF00FF00800000FF400A141E28" : "FF0000FF00FF00FF0000FFFF0A141EFF");
            await Assert.That(Directory.GetFiles(root).Single()).IsEqualTo(path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task UnsupportedFormatsAndOversizedOutputBudgetsDoNotTouchTheDestination()
    {
        string root = Directory.CreateTempSubdirectory("viewer-capture-format-").FullName;
        string path = Path.Combine(root, "source.usda");
        var frame = new ViewerFrameCaptureResult(1, 1, new byte[] { 1, 2, 3, 4 }, ViewerFrameRowOrder.TopDown);
        try
        {
            await File.WriteAllTextAsync(path, "#usda 1.0");
            await Assert.That(async () => await ViewerFrameFileWriter.WriteAsync(path, frame, CancellationToken.None))
                .Throws<NotSupportedException>();
            await Assert.That(async () => await ViewerFrameFileWriter.WriteAsync(
                Path.Combine(root, "frame.png"), frame, CancellationToken.None, maximumBytes: long.MaxValue))
                .Throws<ArgumentOutOfRangeException>();
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo("#usda 1.0");
            await Assert.That(Directory.GetFiles(root).Single()).IsEqualTo(path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
