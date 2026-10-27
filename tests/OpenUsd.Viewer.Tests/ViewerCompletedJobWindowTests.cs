// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerCompletedJobWindowTests
{
    [Test]
    [Arguments(false, "gallery")]
    [Arguments(true, "gallery")]
    [Arguments(false, "frame")]
    [Arguments(true, "frame")]
    [Arguments(false, "owner")]
    [Arguments(true, "owner")]
    [Arguments(false, "other")]
    [Arguments(true, "other")]
    public async Task RenderResultsGalleryCloseRestoresOnlyItsOwnedOriginFocus(bool dark, string destination)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult job = files.CreateJob(count: 1);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var action = new Button { Content = "View results" };
            var moved = new Button { Content = "Other owner action" };
            var owner = new Window
            {
                Content = new StackPanel { Children = { action, moved } },
                Width = 400,
                Height = 300,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            var otherAction = new Button { Content = "Other window action" };
            var other = new Window { Content = otherAction, Width = 240, Height = 160 };
            int publications = 0;
            var results = new ViewerCompletedJobResults(owner, action, (_, _) => publications++);
            results.Publish(job, "Original completed job");
            owner.Show();
            try
            {
                await Assert.That(action.Focus()).IsTrue();
                action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                CompletedRenderJobWindow gallery = owner.OwnedWindows.OfType<CompletedRenderJobWindow>().Single();
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => !Required<ProgressBar>(gallery, "ResultsLoading").IsVisible);
                await Assert.That(Required<Button>(gallery, "ResultsCloseButton").IsKeyboardFocusWithin).IsTrue();
                Control expectedFocus = destination is "gallery" or "frame" ? action :
                    destination == "owner" ? moved : otherAction;
                if (destination == "frame")
                {
                    ListBox frames = Required<ListBox>(gallery, "ResultsFrames");
                    gallery.UpdateLayout();
                    Control first = frames.ContainerFromIndex(0) ??
                        throw new InvalidOperationException("The completed frame has no focus container.");
                    await Assert.That(first.Focus()).IsTrue();
                }
                else if (destination == "owner")
                {
                    await Assert.That(moved.Focus()).IsTrue();
                }
                else if (destination == "other")
                {
                    other.Show();
                    await Assert.That(otherAction.Focus()).IsTrue();
                }
                await gallery.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(expectedFocus.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(Required<ListBox>(gallery, "ResultsFrames").ItemCount).IsEqualTo(0);
                await Assert.That(action.IsEnabled).IsTrue();
                other.Close();
                action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                CompletedRenderJobWindow reopened = owner.OwnedWindows.OfType<CompletedRenderJobWindow>().Single();
                await Assert.That(reopened).IsNotSameReferenceAs(gallery);
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => !Required<ProgressBar>(reopened, "ResultsLoading").IsVisible);
                await Assert.That(reopened.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                await reopened.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(action.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(publications).IsEqualTo(1);
            }
            finally
            {
                other.Close();
                await results.ClearAsync().WaitAsync(TimeSpan.FromSeconds(5));
                owner.Close();
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task HistoricalRequestContextRetainsDisplayColorAndNestedSelectionWithoutReadingLiveState(
        bool dark, bool defaultOcioNames)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult original = files.CreateJob(2);
        var settings = new RenderSettings(7, false, true, new Vector4(0.125f, 0.25f, 0.5f, 0.75f),
            false, false, RenderComplexity.Medium, RenderOutputTransform.Identity, -1.25f)
        {
            DisplayTransform = new RenderDisplayTransform(
                Path.Combine(files.Root, "missing-config.ocio"), "linear",
                defaultOcioNames ? null : "Display", defaultOcioNames ? null : "View", defaultOcioNames ? null : "Look",
                32, -12.5f, 5.25f)
        };
        StageRenderState requested = original.Frames[0].State
            .WithDisplay(new SceneDisplayState(
                RenderPurpose.Guide | RenderPurpose.Proxy, RenderVisibility.RespectAuthored, RenderDrawMode.Wireframe))
            .WithRenderSettings(settings)
            .WithSelection(new SelectionState(
            [
                SelectionItem.FromInstancerContext("/Prototype",
                    [new("/Outer", 2), new("/Inner", 7)], 3, SelectionElementKind.Face),
                new SelectionItem("/World/Anchor")
            ]));
        RenderDiskFrameResult first = original.Frames[0];
        var job = new RenderDiskJobResult(original.OutputDirectory,
            [
                new RenderDiskFrameResult(first.Index, requested, first.FileName, first.Bytes, first.Sha256),
                original.Frames[1]
            ], original.TotalBytes, []);
        byte[] originalPng = await File.ReadAllBytesAsync(Path.Combine(job.OutputDirectory, first.FileName));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new CompletedRenderJobWindow(job, "Original display and selection")
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
                TextBox context = Required<TextBox>(window, "ResultsRequestContext");
                await Assert.That(context.IsReadOnly).IsTrue();
                await Assert.That(AutomationProperties.GetName(context))
                    .IsEqualTo("Original selected frame display, colour and selection request");
                await Assert.That(context.Text).Contains("Recorded request; not effective backend state");
                await Assert.That(context.Text).Contains($"Purposes: {requested.Display.Purposes}");
                await Assert.That(context.Text).Contains("Visibility: RespectAuthored\nDraw mode: Wireframe");
                await Assert.That(context.Text).Contains("Samples per pixel: 7");
                await Assert.That(context.Text).Contains("Lighting: False\nShadows: True\nBackface culling: False");
                await Assert.That(context.Text).Contains("Scene materials: False\nComplexity: Medium");
                await Assert.That(context.Text).Contains("Output transform: Identity\nExposure: -1.25");
                await Assert.That(context.Text).Contains("Clear RGBA: 0.125 0.25 0.5 0.75");
                await Assert.That(context.Text)
                    .Contains($"OCIO config request: {settings.DisplayTransform.ConfigPath}");
                await Assert.That(context.Text).Contains(defaultOcioNames
                    ? "Source colour space: linear\nDisplay: config default requested\nView: display default requested"
                    : "Source colour space: linear\nDisplay: Display\nView: View");
                await Assert.That(context.Text).Contains(defaultOcioNames
                    ? "Look: none requested\nLattice size: 32" : "Look: Look\nLattice size: 32");
                await Assert.That(context.Text).Contains("Shaper log2: -12.5 5.25");
                await Assert.That(context.Text).Contains("Selection items: 2\nItem 0: /Prototype\nElement: Face 3");
                await Assert.That(context.Text).Contains("Instancer 0: /Outer; instance 2");
                await Assert.That(context.Text).Contains("Instancer 1: /Inner; instance 7");
                await Assert.That(context.Text).Contains("Item 1: /World/Anchor\nElement: None");
                window.UpdateLayout();
                await Assert.That(context.Focus()).IsTrue();
                context.BringIntoView();
                window.UpdateLayout();
                ScrollViewer metadata = Required<ScrollViewer>(window, "ResultsMetadataScroll");
                Point origin = context.TranslatePoint(default, metadata) ??
                    throw new InvalidOperationException("Request context has no metadata coordinates.");
                await Assert.That(origin.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(origin.Y + context.Bounds.Height).IsLessThanOrEqualTo(metadata.Viewport.Height);
                string? recordedText = context.Text;
                window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
                await Assert.That(context.SelectedText).IsEqualTo(recordedText);
                window.KeyPress(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
                window.KeyRelease(Key.End, RawInputModifiers.Control, PhysicalKey.End, null);
                await Assert.That(context.CaretIndex).IsEqualTo(recordedText!.Length);
                window.KeyPress(Key.Home, RawInputModifiers.Control, PhysicalKey.Home, null);
                window.KeyRelease(Key.Home, RawInputModifiers.Control, PhysicalKey.Home, null);
                await Assert.That(context.CaretIndex).IsEqualTo(0);
                await Assert.That(context.Text).IsEqualTo(recordedText);
                ListBox frames = Required<ListBox>(window, "ResultsFrames");
                bitmap = ((CompletedRenderJobTile)frames.Items[0]!).Image;
                await Assert.That(Convert.ToHexString(ViewerCompletedJobPreviewJourney.ReadPixels(bitmap)
                    .AsSpan(64 * 256 * 4, 4))).IsEqualTo("FF000080");
                frames.SelectedIndex = 1;
                await Assert.That(context.Text).Contains("No OCIO display transform was requested.");
                await Assert.That(context.Text).Contains("Selection items: 0");
                await Assert.That(context.Text).DoesNotContain("/Prototype");
                await Assert.That(context.Text).DoesNotContain("missing-config");
                frames.SelectedIndex = -1;
                await Assert.That(context.Text).IsEqualTo("Select a completed frame to inspect its render request.");
                frames.SelectedIndex = 0;
                await Assert.That(context.Text).Contains("Item 0: /Prototype");
                await Assert.That(frames.Bounds.Height).IsGreaterThanOrEqualTo(64);
                Button close = Required<Button>(window, "ResultsCloseButton");
                Point closeOrigin = close.TranslatePoint(default, window) ??
                    throw new InvalidOperationException("Close has no window coordinates.");
                await Assert.That(closeOrigin.Y + close.Bounds.Height).IsLessThanOrEqualTo(window.ClientSize.Height);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            await Assert.That(Required<TextBox>(window, "ResultsRequestContext").Text)
                .IsEqualTo("Select a completed frame to inspect its render request.");
            await Assert.That(bitmap).IsNotNull();
            await Assert.That(() => ViewerCompletedJobPreviewJourney.ReadPixels(bitmap!))
                .Throws<ObjectDisposedException>();
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
        await Assert.That(File.Exists(settings.DisplayTransform.ConfigPath)).IsFalse();
        await Assert.That((await File.ReadAllBytesAsync(Path.Combine(job.OutputDirectory, first.FileName)))
            .SequenceEqual(originalPng)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalJobDescriptionRemainsKeyboardReadableAboveHistoricalFrames(bool dark)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult job = files.CreateJob(1);
        byte[] original = await File.ReadAllBytesAsync(Path.Combine(job.OutputDirectory, job.Frames[0].FileName));
        string description = string.Join("\n", Enumerable.Range(0, 40)
            .Select(index => $"Original job context line {index:D2}; historical evidence, not the current scene."));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new CompletedRenderJobWindow(job, description)
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
                window.UpdateLayout();
                ScrollViewer metadata = Required<ScrollViewer>(window, "ResultsMetadataScroll");
                await Assert.That(Required<TextBox>(window, "ResultsOutputLocation").Focus()).IsTrue();
                window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
                window.KeyRelease(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
                await Assert.That(ReferenceEquals(window.FocusManager?.GetFocusedElement(), metadata)).IsTrue();
                int pages = (int)Math.Ceiling(metadata.Extent.Height / metadata.Viewport.Height) + 1;
                for (int page = 0; page < pages; page++)
                {
                    window.KeyPress(Key.PageUp, RawInputModifiers.None, PhysicalKey.PageUp, null);
                    window.UpdateLayout();
                }
                await Assert.That(metadata.Offset.Y).IsEqualTo(0);
                TextBlock context = Required<TextBlock>(window, "ResultsDescription");
                await Assert.That(context.Text).IsEqualTo(description);
                Point? origin = context.TranslatePoint(default, metadata);
                await Assert.That(origin).IsNotNull();
                await Assert.That(origin!.Value.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(origin.Value.Y).IsLessThan(metadata.Viewport.Height);
                double previous = metadata.Offset.Y;
                for (int page = 0; page < pages; page++)
                {
                    window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
                    window.UpdateLayout();
                    await Assert.That(metadata.Offset.Y).IsGreaterThanOrEqualTo(previous);
                    await Assert.That(metadata.Offset.Y - previous).IsLessThanOrEqualTo(metadata.Viewport.Height);
                    previous = metadata.Offset.Y;
                }
                await Assert.That(metadata.Offset.Y).IsEqualTo(metadata.Extent.Height - metadata.Viewport.Height);
                TextBox camera = Required<TextBox>(window, "ResultsCameraContext");
                Point? cameraOrigin = camera.TranslatePoint(default, metadata);
                await Assert.That(cameraOrigin).IsNotNull();
                await Assert.That(cameraOrigin!.Value.Y + camera.Bounds.Height)
                    .IsLessThanOrEqualTo(metadata.Viewport.Height);
                await Assert.That(context.Text).IsEqualTo(description);
                ListBox frames = Required<ListBox>(window, "ResultsFrames");
                await Assert.That(frames.ItemCount).IsEqualTo(1);
                await Assert.That(frames.SelectedIndex).IsEqualTo(0);
                await Assert.That(frames.Bounds.Height).IsGreaterThanOrEqualTo(64);
                bitmap = ((CompletedRenderJobTile)frames.Items[0]!).Image;
                await Assert.That(Convert.ToHexString(ViewerCompletedJobPreviewJourney.ReadPixels(bitmap)
                    .AsSpan(64 * 256 * 4, 4))).IsEqualTo("FF000080");
                Button close = Required<Button>(window, "ResultsCloseButton");
                Point? closeOrigin = close.TranslatePoint(default, window);
                await Assert.That(closeOrigin).IsNotNull();
                await Assert.That(closeOrigin!.Value.Y + close.Bounds.Height)
                    .IsLessThanOrEqualTo(window.ClientSize.Height);
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await window.CloseAsync().WaitAsync(lifetime.Token);
                await Assert.That(frames.ItemCount).IsEqualTo(0);
                await Assert.That(() => ViewerCompletedJobPreviewJourney.ReadPixels(bitmap))
                    .Throws<ObjectDisposedException>();
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
        await Assert.That((await File.ReadAllBytesAsync(Path.Combine(job.OutputDirectory, job.Frames[0].FileName)))
            .SequenceEqual(original)).IsTrue();
    }

    [Test]
    [Arguments(520, 460, false)]
    [Arguments(520, 460, true)]
    [Arguments(940, 780, false)]
    [Arguments(940, 780, true)]
    public async Task LongPreviewRefusalsRemainKeyboardReadableWithoutHidingClose(int width, int height, bool dark)
    {
        using var files = new ViewerCompletedJobTestFiles();
        RenderDiskJobResult job = files.CreateJob(1);
        string refusal = string.Join("\n", Enumerable.Range(0, 40).Select(index =>
            $"Original PNG refusal {index}: the recorded image is unavailable; retain this full path and reason."));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new CompletedRenderJobWindow(job, "Original unavailable recording",
                (_, _) => Task.FromException<ViewerCompletedJobPreview>(new IOException(refusal)))
            {
                Width = width,
                Height = height,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => !Required<ProgressBar>(window, "ResultsLoading").IsVisible);
                window.UpdateLayout();
                Button close = Required<Button>(window, "ResultsCloseButton");
                Point origin = close.TranslatePoint(default, window) ??
                    throw new InvalidOperationException("Completed results Close has no window coordinates.");
                await Assert.That(origin.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(origin.Y + close.Bounds.Height).IsLessThanOrEqualTo(window.ClientSize.Height);
                await Assert.That(Required<TextBlock>(window, "ResultsStatus").Text)
                    .IsEqualTo($"Could not preview results: {refusal}");
                await Assert.That(Required<ListBox>(window, "ResultsFrames").ItemCount).IsEqualTo(0);
                ScrollViewer status = Required<ScrollViewer>(window, "ResultsStatusScroll");
                await Assert.That(AutomationProperties.GetName(status))
                    .IsEqualTo("Completed preview status and errors");
                await Assert.That(close.IsKeyboardFocusWithin).IsTrue();
                window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
                window.KeyRelease(Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab, "\t");
                await Assert.That(status.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(status.Extent.Height).IsGreaterThan(status.Viewport.Height);
                int pages = (int)Math.Ceiling(status.Extent.Height / status.Viewport.Height) + 1;
                for (int page = 0; page < pages; page++)
                {
                    status.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageDown });
                    window.UpdateLayout();
                }
                await Assert.That(status.Offset.Y).IsEqualTo(status.Extent.Height - status.Viewport.Height);
                for (int page = 0; page < pages; page++)
                {
                    status.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.PageUp });
                    window.UpdateLayout();
                }
                await Assert.That(status.Offset.Y).IsEqualTo(0);
                await Assert.That(Required<TextBlock>(window, "ResultsStatus").Text)
                    .IsEqualTo($"Could not preview results: {refusal}");
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                status.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                await window.CloseAsync().WaitAsync(lifetime.Token);
                await Assert.That(window.IsVisible).IsFalse();
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
        await Assert.That(File.Exists(Path.Combine(job.OutputDirectory, job.Frames[0].FileName))).IsTrue();
    }

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
        RenderDiskFrameResult second = original.Frames[1];
        var job = new RenderDiskJobResult(original.OutputDirectory,
            [
                new RenderDiskFrameResult(first.Index, first.State.WithCamera(camera),
                    first.FileName, first.Bytes, first.Sha256),
                new RenderDiskFrameResult(second.Index,
                    second.State.WithStage(new StageIdentity("different-original-frame-stage")),
                    second.FileName, second.Bytes, second.Sha256)
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
                window.UpdateLayout();
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
                TextBlock source = Required<TextBlock>(window, "ResultsSource");
                await Assert.That(source.Text).IsEqualTo("Stage: different-original-frame-stage");
                await Assert.That(ToolTip.GetTip(source)).IsEqualTo(source.Text);
                await Assert.That(context.Text).Contains("Mode: Automatic");
                await Assert.That(context.Text).Contains("The renderer-selected camera was not recorded.");
                await Assert.That(context.Text).DoesNotContain("View (row-major)");
                await Assert.That(context.Text).DoesNotContain("Clip plane");
                frames.SelectedIndex = -1;
                await Assert.That(source.Text).IsEqualTo("Select a completed frame to inspect its original stage.");
                await Assert.That(ToolTip.GetTip(source)).IsNull();
                await Assert.That(context.Text).IsEqualTo("Select a completed frame to inspect its camera request.");
                frames.SelectedIndex = 0;
                await Assert.That(source.Text).IsEqualTo("Stage: original-completed-stage");
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
