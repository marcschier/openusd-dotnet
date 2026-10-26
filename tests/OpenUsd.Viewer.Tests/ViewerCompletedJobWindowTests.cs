// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerCompletedJobWindowTests
{
    [Test]
    [Arguments(520, 460, 0, false)]
    [Arguments(520, 460, 512, false)]
    [Arguments(520, 460, 2048, false)]
    [Arguments(940, 780, 512, false)]
    [Arguments(520, 460, 512, true)]
    public async Task CompletedResultsKeepCloseAndVerifiedFramesReachableWithLongDescriptions(
        int width, int height, int descriptionLength, bool longOutputPath)
    {
        using var files = new ViewerCompletedJobTestFiles();
        var job = files.CreateJob(3);
        if (longOutputPath)
        {
            string parent = files.Root;
            while (parent.Length < 300)
            {
                parent = Path.Combine(parent, new string('a', 60));
            }
            Directory.CreateDirectory(parent);
            string output = Path.Combine(parent, "recording");
            Directory.Move(job.OutputDirectory, output);
            job = new RenderDiskJobResult(output, job.Frames.ToArray(), job.TotalBytes, []);
        }
        string description = "Authored product /" + new string('x', descriptionLength);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new CompletedRenderJobWindow(job, description) { Width = width, Height = height };
            Bitmap? firstImage = null;
            window.Show();
            try
            {
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => !Required<ProgressBar>(window, "ResultsLoading").IsVisible);
                window.UpdateLayout();

                Button close = Required<Button>(window, "ResultsCloseButton");
                Point? origin = close.TranslatePoint(default, window);
                await Assert.That(origin).IsNotNull();
                await Assert.That(origin!.Value.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(origin.Value.Y + close.Bounds.Height)
                    .IsLessThanOrEqualTo(window.ClientSize.Height);
                await Assert.That(Required<TextBlock>(window, "ResultsDescription").Text).IsEqualTo(description);
                TextBox output = Required<TextBox>(window, "ResultsOutputLocation");
                await Assert.That(output.Text).IsEqualTo(job.OutputDirectory);
                await Assert.That(output.IsReadOnly).IsTrue();
                await Assert.That(output.Focus()).IsTrue();
                output.BringIntoView();
                window.UpdateLayout();
                ScrollViewer metadata = Required<ScrollViewer>(window, "ResultsMetadataScroll");
                Point? outputOrigin = output.TranslatePoint(default, metadata);
                await Assert.That(outputOrigin).IsNotNull();
                await Assert.That(outputOrigin!.Value.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(outputOrigin.Value.Y + output.Bounds.Height)
                    .IsLessThanOrEqualTo(metadata.Viewport.Height);
                ListBox frames = Required<ListBox>(window, "ResultsFrames");
                await Assert.That(frames.Bounds.Height).IsGreaterThanOrEqualTo(64);
                CompletedRenderJobTile[] tiles = frames.Items.Cast<CompletedRenderJobTile>().ToArray();
                await Assert.That(tiles.Select(static tile => tile.FrameIndex).SequenceEqual([0, 1, 2])).IsTrue();
                await Assert.That(tiles[0].Image.PixelSize).IsEqualTo(new PixelSize(256, 256));
                firstImage = tiles[0].Image;
                byte[] pixels = ViewerCompletedJobPreviewJourney.ReadPixels(tiles[0].Image);
                await Assert.That(Convert.ToHexString(pixels.AsSpan(64 * 256 * 4, 4))).IsEqualTo("FF000080");
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            await Assert.That(Required<ListBox>(window, "ResultsFrames").ItemCount).IsEqualTo(0);
            await Assert.That(firstImage).IsNotNull();
            await Assert.That(() => ViewerCompletedJobPreviewJourney.ReadPixels(firstImage!))
                .Throws<ObjectDisposedException>();
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing completed-results control: {name}");

    private static class SkiaAppEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
