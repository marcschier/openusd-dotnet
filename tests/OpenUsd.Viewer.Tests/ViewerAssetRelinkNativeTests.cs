// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Storm;
using OpenUsd.Shade;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerAssetRelinkNativeTests
{
    [Test]
    public async Task AssetPickerRelinksTheExistingShaderAndUndoRestoresItsRenderedTexture()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_ASSET_RELINK_SMOKE") != "1")
        {
            Skip.Test("Run alone with OPENUSD_VIEWER_ASSET_RELINK_SMOKE=1 on a Windows desktop.");
        }
        using ViewerPortableReviewFixture files = await ViewerPortableReviewFixture.CreateAsync();
        string replacement = Path.Combine(files.Root, "green.png");
        using (FileStream image = File.Create(replacement))
        {
            _ = PngRgba8Writer.Write(image, 1, 1, [0, 255, 0, 255], 1024);
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(80));
        using var dispatcherLifetime = new CancellationTokenSource();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Program.BuildAvaloniaApp().SetupWithoutStarting();
                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        await ExerciseAsync(files, replacement, timeout.Token);
                        completed.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        completed.TrySetException(exception);
                    }
                    finally
                    {
                        ViewerStartupOptions.Initialize([]);
                        dispatcherLifetime.Cancel();
                    }
                });
                Dispatcher.UIThread.MainLoop(dispatcherLifetime.Token);
            }
            catch (Exception exception)
            {
                completed.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Viewer texture asset relinking"
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
        bool stopped;
        try
        {
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(90));
            await files.AssertOriginalsUnchangedAsync();
        }
        finally
        {
            timeout.Cancel();
            dispatcherLifetime.Cancel();
            stopped = thread.Join(TimeSpan.FromSeconds(5));
        }
        await Assert.That(stopped).IsTrue();
    }

    private static async Task ExerciseAsync(
        ViewerPortableReviewFixture files, string replacement, CancellationToken timeout)
    {
        var opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            StagePath = files.SourcePath,
            Renderer = "Storm",
            PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH"),
            StageReadyAsync = (session, _) =>
            {
                Volatile.Read(ref opened).TrySetResult(session);
                return Task.CompletedTask;
            }
        });
        var picker = new AssetPicker { NextPath = null };
        var reviewPicker = new ReviewPicker(files.DestinationPath);
        var framePicker = new FramePicker(Path.Combine(files.Root, "captured-frame.png"));
        string settingsRoot = Path.Combine(files.Root, "asset-ui-settings");
        using var settings = new ViewerSettingsStore(settingsRoot);
        var main = new MainWindow(new RecentStageStore(settingsRoot), settings, reviewPicker, picker, framePicker);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Closed += (_, _) => closed.TrySetResult();
        try
        {
            main.Show();
            ViewerStageSession session = await opened.Task.WaitAsync(timeout);
            var viewport = Required<RendererSwitchingViewport>(main, "ViewportHost");
            await session.FrameAsync("/Body/Quad", timeout);
            await WaitAsync(() => viewport.GetActiveStormNavigationSource() is not null, timeout);
            TreeView tree = Required<TreeView>(main, "StageHierarchy");
            TreeViewItem material = tree.Items.OfType<TreeViewItem>()
                .Single(static row => row.Tag is ViewerHierarchyTreeNode { Entry.Path: "/Material" });
            material.IsExpanded = true;
            tree.SelectedItem = material.Items.OfType<TreeViewItem>()
                .Single(static row => row.Tag is ViewerHierarchyTreeNode { Entry.Path: "/Material/Texture" });
            await WaitAsync(() => Required<MenuItem>(main, "EditPropertyMenuItem").IsEnabled, timeout);
            Required<MenuItem>(main, "EditPropertyMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<PropertyEditWindow>().Any(), timeout);
            PropertyEditWindow property = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            ComboBox selector = Required<ComboBox>(property, "PropertySelector");
            await WaitAsync(() => selector.IsEnabled, timeout);
            selector.SelectedItem = selector.Items.OfType<string>()
                .Single(static item => item == "inputs:file (asset)");
            Button choose = Required<Button>(property, "ChoosePropertyAssetButton");
            await WaitAsync(() => choose.IsVisible && choose.IsEnabled, timeout);
            TextBox draft = Required<TextBox>(property, "PropertyValue");
            draft.Text = "draft-not-applied.png";
            choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => picker.Calls == 1 && choose.IsEnabled, timeout);
            await Assert.That(draft.Text).IsEqualTo("draft-not-applied.png");
            await Assert.That(await session.Scheduler.InvokeAsync(TexturePath, timeout)).IsEqualTo("texture.png");
            await Assert.That(Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled).IsFalse();

            picker.Failure = new UnauthorizedAccessException("Asset folder access was denied.");
            choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => picker.Calls == 2 && choose.IsEnabled, timeout);
            await Assert.That(Required<TextBlock>(property, "PropertyEditStatus").Text)
                .Contains("Asset folder access was denied.");
            await Assert.That(draft.Text).IsEqualTo("draft-not-applied.png");
            await Assert.That(Required<Button>(property, "SetPropertyButton").IsEnabled).IsTrue();
            await Assert.That(await session.Scheduler.InvokeAsync(TexturePath, timeout)).IsEqualTo("texture.png");
            picker.Failure = null;
            picker.NextPath = replacement;
            choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => picker.Calls == 3 && draft.Text == replacement && choose.IsEnabled, timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(TexturePath, timeout)).IsEqualTo("texture.png");
            OpenUsdStormFramebufferCapture before = await viewport.CaptureStormFramebufferAsync(timeout);
            var restoration = new ViewerTextureRestorationOracle(before);
            int originalGreen = GreenPixels(before);
            await property.CloseAsync();
            MenuItem captureMenu = Required<MenuItem>(main, "CaptureFrameMenuItem");
            await Assert.That(captureMenu.IsEnabled).IsTrue();
            framePicker.NextPath = null;
            captureMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => framePicker.Calls == 1 && captureMenu.IsEnabled, timeout);
            await Assert.That(File.Exists(framePicker.Path)).IsFalse();
            framePicker.Failure = new UnauthorizedAccessException("Capture destination access was denied.");
            captureMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => framePicker.Calls == 2 && captureMenu.IsEnabled, timeout);
            await Assert.That(Required<TextBlock>(main, "ViewerStatus").Text)
                .Contains("Capture destination access was denied.");
            await Assert.That(File.Exists(framePicker.Path)).IsFalse();
            framePicker.Failure = null;
            framePicker.NextPath = framePicker.Path;
            captureMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => framePicker.Calls == 3 && captureMenu.IsEnabled, timeout);
            await Assert.That(File.Exists(framePicker.Path)).IsTrue()
                .Because(Required<TextBlock>(main, "ViewerStatus").Text ?? "The Viewer did not report capture state.");
            byte[] captureBytes = await File.ReadAllBytesAsync(framePicker.Path, timeout);
            await Assert.That(Convert.ToHexString(captureBytes.AsSpan(0, 8))).IsEqualTo("89504E470D0A1A0A");
            ViewerCapturePair captured = await ViewerCaptureComparison.LoadAsync(
                framePicker.Path, framePicker.Path, timeout);
            await Assert.That(captured.Before.Width).IsEqualTo(before.Width);
            await Assert.That(captured.Before.Height).IsEqualTo(before.Height);
            byte[] bottomUp = new byte[captured.Before.Rgba.Length];
            int rowBytes = checked(captured.Before.Width * 4);
            for (int y = 0; y < captured.Before.Height; y++)
            {
                captured.Before.Rgba.AsSpan((captured.Before.Height - 1 - y) * rowBytes, rowBytes)
                    .CopyTo(bottomUp.AsSpan(y * rowBytes, rowBytes));
            }
            await Assert.That(restoration.IsRestored(before with { RgbaPixels = bottomUp })).IsTrue();

            Required<MenuItem>(main, "EditPropertyMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<PropertyEditWindow>().Any(), timeout);
            property = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            selector = Required<ComboBox>(property, "PropertySelector");
            await WaitAsync(() => selector.IsEnabled, timeout);
            selector.SelectedItem = selector.Items.OfType<string>()
                .Single(static item => item == "inputs:file (asset)");
            await WaitAsync(() => Required<Button>(property, "SetPropertyButton").IsEnabled, timeout);
            Required<TextBox>(property, "PropertyValue").Text = replacement;
            Required<Button>(property, "SetPropertyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => Required<TextBlock>(property, "PropertyEditStatus").Text == "Review edit applied.",
                timeout);
            await property.CloseAsync();
            await Assert.That(await session.Scheduler.InvokeAsync(TexturePath, timeout)).IsEqualTo(replacement);
            await WaitGreenAsync(viewport, originalGreen + 64, timeout);
            await WaitAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled, timeout);
            Required<MenuItem>(main, "EditUndoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => Required<MenuItem>(main, "EditRedoMenuItem").IsEnabled, timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(TexturePath, timeout)).IsEqualTo("texture.png");
            await WaitRestoredAsync(viewport, restoration, timeout);
            Required<MenuItem>(main, "EditRedoMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled, timeout);
            await WaitGreenAsync(viewport, originalGreen + 64, timeout);
            await WaitAsync(() => Required<MenuItem>(main, "SaveReviewMenuItem").IsEnabled, timeout);
            Required<MenuItem>(main, "SaveReviewMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => Required<TextBlock>(main, "ViewerStatus").Text == "Review saved.", timeout);
            await Assert.That(File.Exists(files.DestinationPath)).IsTrue();
            await Assert.That(await session.Scheduler.InvokeAsync(static stage =>
                UsdShadeShader.Wrap(stage.GetPrim("/Material/Texture")).SourceId, timeout)).IsEqualTo("UsdUVTexture");
            await Assert.That(Required<MenuItem>(main, "SaveSourceMenuItem").IsEnabled).IsFalse();

            opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
            Required<MenuItem>(main, "OpenReviewMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<DocumentActionWindow>().Any(), timeout);
            DocumentActionWindow intent = main.OwnedWindows.OfType<DocumentActionWindow>().Single();
            await Assert.That(Required<TextBlock>(intent, "DocumentActionDetails").Text).Contains(files.SourcePath);
            Required<Button>(intent, "AcceptDocumentActionButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            session = await opened.Task.WaitAsync(timeout);
            await session.FrameAsync("/Body/Quad", timeout);
            await Assert.That(await session.Scheduler.InvokeAsync(TexturePath, timeout)).IsEqualTo(replacement);
            await WaitGreenAsync(viewport, originalGreen + 64, timeout);
            await Assert.That(Required<MenuItem>(main, "EditUndoMenuItem").IsEnabled).IsFalse();

            material = tree.Items.OfType<TreeViewItem>()
                .Single(static row => row.Tag is ViewerHierarchyTreeNode { Entry.Path: "/Material" });
            material.IsExpanded = true;
            tree.SelectedItem = material.Items.OfType<TreeViewItem>()
                .Single(static row => row.Tag is ViewerHierarchyTreeNode { Entry.Path: "/Material/Texture" });
            await WaitAsync(() => Required<MenuItem>(main, "EditPropertyMenuItem").IsEnabled, timeout);
            Required<MenuItem>(main, "EditPropertyMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => main.OwnedWindows.OfType<PropertyEditWindow>().Any(), timeout);
            property = main.OwnedWindows.OfType<PropertyEditWindow>().Single();
            selector = Required<ComboBox>(property, "PropertySelector");
            await WaitAsync(() => selector.IsEnabled, timeout);
            selector.SelectedItem = selector.Items.OfType<string>()
                .Single(static item => item == "inputs:file (asset)");
            choose = Required<Button>(property, "ChoosePropertyAssetButton");
            await WaitAsync(() => choose.IsEnabled, timeout);
            picker.Pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => picker.Calls == 4 && !choose.IsEnabled, timeout);
            await property.CloseAsync().WaitAsync(timeout);
            picker.Pending.TrySetResult(Path.Combine(files.Root, "must-not-apply.png"));
            await Assert.That(await session.Scheduler.InvokeAsync(TexturePath, timeout)).IsEqualTo(replacement);
            await Assert.That(main.OwnedWindows.OfType<PropertyEditWindow>()).IsEmpty();

            framePicker.Pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            captureMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitAsync(() => framePicker.Calls == 4 && !captureMenu.IsEnabled, timeout);
            main.Close();
            await closed.Task.WaitAsync(timeout);
            await Assert.That(framePicker.LastCancellationToken.IsCancellationRequested).IsTrue();
            string cancelledCapture = Path.Combine(files.Root, "must-not-capture.bmp");
            framePicker.Pending.TrySetResult(cancelledCapture);
            await Task.Yield();
            await Assert.That(File.Exists(cancelledCapture)).IsFalse();
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            foreach (PropertyEditWindow property in main.OwnedWindows.OfType<PropertyEditWindow>().ToArray())
            {
                await property.CloseAsync().WaitAsync(cleanup.Token);
            }
            main.Close();
            await WaitAsync(() => closed.Task.IsCompleted ||
                main.OwnedWindows.OfType<DocumentChangesWindow>().Any(), cleanup.Token);
            if (!closed.Task.IsCompleted)
            {
                Required<Button>(main.OwnedWindows.OfType<DocumentChangesWindow>().Single(),
                    "DiscardDocumentChangesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            await closed.Task.WaitAsync(cleanup.Token);
        }
    }

    private static int GreenPixels(OpenUsdStormFramebufferCapture capture)
    {
        int count = 0;
        ReadOnlySpan<byte> pixels = capture.RgbaPixels.Span;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset + 1] > pixels[offset] + 30 && pixels[offset + 1] > pixels[offset + 2] + 30)
            {
                count++;
            }
        }
        return count;
    }

    private static async Task WaitGreenAsync(
        RendererSwitchingViewport viewport, int minimumGreen, CancellationToken timeout)
    {
        long started = Stopwatch.GetTimestamp();
        while (GreenPixels(await viewport.CaptureStormFramebufferAsync(timeout)) < minimumGreen)
        {
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(15))
            {
                throw new TimeoutException("The live texture did not change after its review edit.");
            }
            await Task.Delay(40, timeout);
        }
    }

    private static async Task WaitRestoredAsync(
        RendererSwitchingViewport viewport, ViewerTextureRestorationOracle oracle, CancellationToken timeout)
    {
        long started = Stopwatch.GetTimestamp();
        while (!oracle.IsRestored(await viewport.CaptureStormFramebufferAsync(timeout)))
        {
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(15))
            {
                throw new TimeoutException("Undo did not restore the original visible white quad's pixel footprint.");
            }
            await Task.Delay(40, timeout);
        }
    }

    private static string TexturePath(UsdStage stage) =>
        UsdShadeShader.Wrap(stage.GetPrim("/Material/Texture")).GetInput("file").GetAssetPath().Path;

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing control: {name}");

    private static async Task WaitAsync(
        Func<bool> ready, CancellationToken timeout,
        [CallerArgumentExpression(nameof(ready))] string? condition = null)
    {
        long started = Stopwatch.GetTimestamp();
        while (!ready())
        {
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(15))
            {
                throw new TimeoutException($"Viewer state did not become ready: {condition}");
            }
            await Task.Delay(20, timeout);
        }
    }

    private sealed class AssetPicker : IViewerAssetFilePicker
    {
        internal string? NextPath { get; set; }
        internal int Calls { get; private set; }
        internal Exception? Failure { get; set; }
        internal TaskCompletionSource<string?>? Pending { get; set; }

        public Task<string?> OpenAssetAsync(Window owner, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Failure is { } exception
                ? Task.FromException<string?>(exception)
                : Pending is { } pending
                    ? pending.Task.WaitAsync(cancellationToken)
                    : Task.FromResult(NextPath);
        }
    }

    private sealed class ReviewPicker(string destination) : IViewerDocumentFilePicker
    {
        public Task<string?> SaveReviewAsync(Window owner, string suggestedPath) =>
            Task.FromResult<string?>(destination);
        public Task<string?> OpenReviewAsync(Window owner) => Task.FromResult<string?>(destination);
    }

    private sealed class FramePicker(string path) : IViewerFrameFilePicker
    {
        internal string Path { get; } = path;
        internal string? NextPath { get; set; } = path;
        internal int Calls { get; private set; }
        internal Exception? Failure { get; set; }
        internal TaskCompletionSource<string?>? Pending { get; set; }
        internal CancellationToken LastCancellationToken { get; private set; }

        public Task<string?> SaveFrameAsync(Window owner, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCancellationToken = cancellationToken;
            Calls++;
            return Failure is { } exception
                ? Task.FromException<string?>(exception)
                : Pending is { } pending
                    ? pending.Task.WaitAsync(cancellationToken)
                    : Task.FromResult(NextPath);
        }
    }
}
