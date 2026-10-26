// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerCompletedJobWindowTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task HistoricalFramesExposeOriginalCameraRequestsWithoutInventingAnAutomaticCamera(
        bool dark, bool includeClipPlanes)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult original = files.CreateJob(2);
        Vector4[] planes = Enumerable.Range(0, includeClipPlanes ? CameraState.MaxClipPlanes : 0)
            .Select(index => new Vector4(index + 0.125f, -index - 0.25f, 0.5f, 1.75f)).ToArray();
        var view = new Matrix4x4(
            1.25f, 2, 3, 4, 5, 6.5f, 7, 8, 9, 10, 11.75f, 12, -13.125f, 14, 15, 16);
        var projection = new Matrix4x4(
            17, 18, 19, 20, 21, 22, 23, 24, 25, 26, -27.5f, 28, 29, 30, 31, 32);
        CameraState camera = new(view, projection, planes);
        if (includeClipPlanes)
        {
            planes[0] = new Vector4(999);
        }
        RenderDiskFrameResult first = original.Frames[0];
        var job = new RenderDiskJobResult(original.OutputDirectory,
            [
                new RenderDiskFrameResult(first.Index, first.State.WithCamera(camera),
                    first.FileName, first.Bytes, first.Sha256),
                original.Frames[1]
            ], original.TotalBytes, []);
        byte[] originalPng = await File.ReadAllBytesAsync(Path.Combine(job.OutputDirectory, first.FileName));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new CompletedRenderJobWindow(job, "Retired camera request")
            {
                Width = 520,
                Height = 460,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            Bitmap? bitmap = null;
            try
            {
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => !Required<ProgressBar>(window, "ResultsLoading").IsVisible);
                TextBox context = Required<TextBox>(window, "ResultsCameraContext");
                await Assert.That(context.IsReadOnly).IsTrue();
                await Assert.That(context.Text).Contains("Recorded camera request; not effective backend state");
                await Assert.That(context.Text).Contains("Mode: Matrices");
                await Assert.That(context.Text).Contains(
                    "View (row-major): 1.25 2 3 4; 5 6.5 7 8; 9 10 11.75 12; -13.125 14 15 16");
                await Assert.That(context.Text).Contains(
                    "Projection (row-major): 17 18 19 20; 21 22 23 24; 25 26 -27.5 28; 29 30 31 32");
                if (includeClipPlanes)
                {
                    await Assert.That(context.Text).Contains("Clip plane 0 (view-space): 0.125 -0.25 0.5 1.75");
                    await Assert.That(context.Text).Contains("Clip plane 7 (view-space): 7.125 -7.25 0.5 1.75");
                }
                else
                {
                    await Assert.That(context.Text).Contains("No clip planes were requested.");
                    await Assert.That(context.Text).DoesNotContain("Clip plane 0");
                }
                await Assert.That(context.Text).DoesNotContain("999");
                await Assert.That(context.Focus()).IsTrue();
                context.BringIntoView();
                window.UpdateLayout();
                ScrollViewer metadata = Required<ScrollViewer>(window, "ResultsMetadataScroll");
                Point location = context.TranslatePoint(default, metadata) ??
                    throw new InvalidOperationException("Camera request context has no window coordinates.");
                await Assert.That(location.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(location.Y + context.Bounds.Height).IsLessThanOrEqualTo(metadata.Viewport.Height);
                Button close = Required<Button>(window, "ResultsCloseButton");
                Point closeLocation = close.TranslatePoint(default, window) ??
                    throw new InvalidOperationException("Completed results Close has no window coordinates.");
                await Assert.That(closeLocation.Y + close.Bounds.Height).IsLessThanOrEqualTo(window.ClientSize.Height);
                ListBox frames = Required<ListBox>(window, "ResultsFrames");
                await Assert.That(frames.Bounds.Height).IsGreaterThanOrEqualTo(64);
                bitmap = ((CompletedRenderJobTile)frames.Items[0]!).Image;
                await Assert.That(Convert.ToHexString(ViewerCompletedJobPreviewJourney.ReadPixels(bitmap)
                    .AsSpan(64 * 256 * 4, 4))).IsEqualTo("FF000080");
                frames.SelectedIndex = 1;
                await Assert.That(context.Text).Contains("Mode: Automatic");
                await Assert.That(context.Text).Contains("The renderer-selected camera was not recorded.");
                await Assert.That(context.Text).DoesNotContain("View (row-major)");
                await Assert.That(context.Text).DoesNotContain("Clip plane");
                frames.SelectedIndex = -1;
                await Assert.That(context.Text).IsEqualTo("Select a completed frame to inspect its camera request.");
                frames.SelectedIndex = 0;
                await Assert.That(context.Text).Contains("Mode: Matrices");
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            await Assert.That(Required<TextBox>(window, "ResultsCameraContext").Text)
                .IsEqualTo("Select a completed frame to inspect its camera request.");
            await Assert.That(bitmap).IsNotNull();
            await Assert.That(() => ViewerCompletedJobPreviewJourney.ReadPixels(bitmap!))
                .Throws<ObjectDisposedException>();
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
        await Assert.That((await File.ReadAllBytesAsync(Path.Combine(job.OutputDirectory, first.FileName)))
            .SequenceEqual(originalPng)).IsTrue();
    }

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
