// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed partial class ViewerWorkspaceNativeSmokeTests
{
    [Test]
    public async Task PresetMenusAndOwnedPalettePreserveOperatorIntentAndRestoreKeyboardFocus()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_WORKSPACE_SMOKE") != "1")
        {
            Skip.Test("Run this class alone with OPENUSD_VIEWER_WORKSPACE_SMOKE=1 on a Windows desktop.");
        }

        string root = Path.Combine(AppContext.BaseDirectory, "workspace-smoke", Guid.NewGuid().ToString("N"));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Program.BuildAvaloniaApp().SetupWithoutStarting();
                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        await ExerciseWindowAsync(root);
                        await ExerciseStormWindowAsync(Path.Combine(root, "stage"), empty: false);
                        await ExerciseStormWindowAsync(Path.Combine(root, "stage"), empty: true);
                        completion.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                    finally
                    {
                        ViewerStartupOptions.Initialize([]);
                        lifetime.Cancel();
                    }
                });
                Dispatcher.UIThread.MainLoop(lifetime.Token);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Viewer workspace native smoke"
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
        bool stopped;
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(110));
        }
        finally
        {
            lifetime.Cancel();
            stopped = thread.Join(TimeSpan.FromSeconds(5));
            if (stopped && Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        await Assert.That(stopped).IsTrue();
    }

    private static async Task ExerciseWindowAsync(string root)
    {
        ViewerStartupOptions.Initialize(new ViewerHostOptions { Renderer = "D3D12" });
        using var store = new ViewerSettingsStore(root);
        ViewerSettings original = ViewerSettings.Default with
        {
            StagePanelWidth = 216,
            InspectorPanelWidth = 392,
            SelectedTabId = "layers",
            InspectorPanelVisible = false,
            TimelineVisible = false,
            RendererPreference = "Vulkan",
            ThemePreference = ViewerThemePreference.Dark,
            PickTarget = "edge",
            SelectionMode = "xray",
            SnapTimelineToFrames = true
        };
        await store.SaveAsync(original);
        var window = new MainWindow(new RecentStageStore(root), store);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            window.Show();
            await Task.Delay(150);
            await Assert.That(Required<Border>(window, "WelcomePanel").IsVisible).IsTrue();
            await Assert.That(Required<Grid>(window, "MainContentGrid").IsVisible).IsFalse();
            await Assert.That(Required<Border>(window, "StagePanel").IsVisible).IsTrue();
            await Assert.That(Required<Grid>(window, "MainContentGrid").ColumnDefinitions[0].Width.Value)
                .IsEqualTo(216);
            await Assert.That(Required<TabControl>(window, "InspectorTabs").SelectedItem)
                .IsSameReferenceAs(Required<TabItem>(window, "LayersTab"));
            Click(window, "WorkspaceInspectMenuItem");
            await Task.Delay(100);
            ViewerSettings saved = (await store.LoadAsync()).Settings;
            await Assert.That(saved.StagePanelWidth).IsEqualTo(280);
            await Assert.That(saved.InspectorPanelWidth).IsEqualTo(380);
            await Assert.That(saved.SelectedTabId).IsEqualTo("properties");
            await Assert.That(saved.InspectorPanelVisible).IsTrue();
            await Assert.That(saved.RendererPreference).IsEqualTo("Vulkan");
            await Assert.That(saved.ThemePreference).IsEqualTo(ViewerThemePreference.Dark);
            await Assert.That(saved.PickTarget).IsEqualTo("edge");
            await Assert.That(saved.SelectionMode).IsEqualTo("xray");
            await Assert.That(saved.SnapTimelineToFrames).IsTrue();
            await Assert.That(Required<ComboBox>(window, "RendererSelector").SelectedIndex).IsEqualTo(2);

            Button searchButton = Required<Button>(window, "CommandPaletteButton");
            searchButton.Focus();
            Click(window, "CommandPaletteMenuItem");
            await Task.Delay(100);
            Window palette = window.OwnedWindows.Single(child => child.Title == "Search commands");
            TextBox search = Required<TextBox>(palette, "CommandSearch");
            await Assert.That(palette.Owner).IsSameReferenceAs(window);
            await Assert.That(search.IsKeyboardFocusWithin).IsTrue();
            await Assert.That(palette.ActualThemeVariant).IsEqualTo(ThemeVariant.Dark);
            search.Text = "viewer theme light";
            await Task.Delay(50);
            palette.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await Task.Delay(100);
            await Assert.That(palette.IsVisible).IsFalse();
            await Assert.That(searchButton.IsKeyboardFocusWithin).IsTrue();
            await Assert.That(window.ActualThemeVariant).IsEqualTo(ThemeVariant.Light);
            await Assert.That((await store.LoadAsync()).Settings.RendererPreference).IsEqualTo("Vulkan");
            Click(window, "CommandPaletteMenuItem");
            await Task.Delay(75);
            Window closeOnlyPalette = window.OwnedWindows.Single(child => child.Title == "Search commands");
            Required<TextBox>(closeOnlyPalette, "CommandSearch").Text = "viewer theme dark";
            await Task.Delay(50);
            Button closePalette = Required<Button>(closeOnlyPalette, "ClosePaletteButton");
            closePalette.Focus();
            await Assert.That(closePalette.IsKeyboardFocusWithin).IsTrue();
            closePalette.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter
            });
            await Task.Delay(75);
            await Assert.That(closeOnlyPalette.IsVisible).IsFalse();
            await Assert.That(window.ActualThemeVariant).IsEqualTo(ThemeVariant.Light)
                .Because("Enter on the Close button must not run the selected Dark-theme command");
            Click(window, "CommandPaletteMenuItem");
            await Task.Delay(75);
            Window inactivePalette = window.OwnedWindows.Single(child => child.Title == "Search commands");
            _ = SetActiveWindow(window.TryGetPlatformHandle()?.Handle ??
                throw new InvalidOperationException("The Viewer has no native window."));
            searchButton.Focus();
            await WaitUntilAsync(() => window.IsActive && !inactivePalette.IsActive);
            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.F1
            });
            await Task.Delay(75);
            ShortcutsWindow? shortcuts = window.OwnedWindows.OfType<ShortcutsWindow>().SingleOrDefault();
            await Assert.That(shortcuts).IsNotNull()
                .Because("an inactive modeless palette must not suppress shortcuts in its active owner");
            shortcuts!.Close();
            inactivePalette.Close();
            Click(window, "CompareCapturesMenuItem");
            await Task.Delay(50);
            var comparison = (CaptureComparisonWindow)window.OwnedWindows
                .Single(child => child.Title == "Compare captures");
            await Assert.That(comparison.Owner).IsSameReferenceAs(window);
            await Assert.That(comparison.ActualThemeVariant).IsEqualTo(ThemeVariant.Light);
            string beforePath = Path.Combine(root, "before.png");
            string afterPath = Path.Combine(root, "after.bmp");
            using (FileStream png = File.Create(beforePath))
            {
                _ = PngRgba8Writer.Write(png, 1, 1, [255, 0, 0, 128], 1024);
            }
            ViewerFrameBitmapWriter.WriteBmp(afterPath, 2, 1, [0, 255, 0, 255, 0, 0, 255, 255]);
            await comparison.LoadPairAsync(beforePath, afterPath);
            await Assert.That(Required<Image>(comparison, "BeforeImage").Source).IsNotNull();
            await Assert.That(Required<Image>(comparison, "AfterImage").Source).IsNotNull();
            var beforeBitmap = (WriteableBitmap)Required<Image>(comparison, "BeforeImage").Source!;
            await Assert.That(beforeBitmap.AlphaFormat).IsEqualTo(AlphaFormat.Unpremul);
            byte alpha;
            using (var pixels = beforeBitmap.Lock())
            {
                alpha = System.Runtime.InteropServices.Marshal.ReadByte(pixels.Address, 3);
            }
            await Assert.That(alpha).IsEqualTo((byte)128);
            await Assert.That(Required<TextBlock>(comparison, "ComparisonStatus").Text)
                .Contains("A: 1 x 1; B: 2 x 1");
            Required<ComboBox>(comparison, "ComparisonMode").SelectedIndex = 1;
            await Assert.That(Required<Border>(comparison, "BeforePane").IsVisible).IsTrue();
            await Assert.That(Required<Border>(comparison, "AfterPane").IsVisible).IsFalse();
            await comparison.LoadPairAsync(beforePath, Path.Combine(root, "missing.bmp"));
            await Assert.That(Required<TextBlock>(comparison, "ComparisonStatus").Text)
                .Contains("Could not compare captures");
            await Assert.That(Required<Image>(comparison, "BeforeImage").Source).IsNull();
            await Assert.That(Required<Image>(comparison, "AfterImage").Source).IsNull();
            string malformedPath = Path.Combine(root, "malformed.bmp");
            await File.WriteAllBytesAsync(malformedPath, [0, 1, 2]);
            await comparison.LoadPairAsync(beforePath, malformedPath);
            await Assert.That(Required<TextBlock>(comparison, "ComparisonStatus").Text)
                .Contains("Could not compare captures");
            await Assert.That(Required<Image>(comparison, "BeforeImage").Source).IsNull();
            await Assert.That(Required<Image>(comparison, "AfterImage").Source).IsNull();
            await Assert.That(Required<Button>(comparison, "CompareAgainButton").IsEnabled).IsTrue();
            await comparison.LoadPairAsync(beforePath, afterPath);
            await Assert.That(Required<Image>(comparison, "BeforeImage").Source).IsNotNull();
            string corruptPng = Path.Combine(root, "corrupt.png");
            byte[] corrupt = await File.ReadAllBytesAsync(beforePath);
            corrupt[^1] ^= 1;
            await File.WriteAllBytesAsync(corruptPng, corrupt);
            await comparison.LoadPairAsync(beforePath, corruptPng);
            await Assert.That(Required<TextBlock>(comparison, "ComparisonStatus").Text).Contains("CRC");
            await Assert.That(Required<Image>(comparison, "BeforeImage").Source).IsNull();
            await Assert.That(Required<Image>(comparison, "AfterImage").Source).IsNull();
            await Assert.That(Required<Button>(comparison, "CompareAgainButton").IsEnabled).IsTrue();
            comparison.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            await Task.Delay(50);
            await Assert.That(comparison.IsVisible).IsFalse();
            await Assert.That(searchButton.IsKeyboardFocusWithin).IsTrue();
            await ExerciseComparisonCancellationAsync(window, beforePath, afterPath);
        }
        finally
        {
            foreach (Window child in window.OwnedWindows.ToArray())
            {
                child.Close();
            }
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static void Click(Control owner, string name) =>
        Required<MenuItem>(owner, name).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing workspace control: {name}");

    private static async Task ExerciseComparisonCancellationAsync(Window owner, string beforePath, string afterPath)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TaskCompletionSource<ViewerCapturePair>(TaskCreationOptions.RunContinuationsAsynchronously);
        var comparison = new CaptureComparisonWindow((_, _, cancellationToken) =>
        {
            entered.TrySetResult();
            return blocked.Task.WaitAsync(cancellationToken);
        });
        comparison.Show(owner);
        try
        {
            Task loading = comparison.LoadPairAsync(beforePath, afterPath);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.That(Required<Button>(comparison, "ChooseBeforeButton").IsEnabled).IsFalse();
            Required<Button>(comparison, "CancelComparisonButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loading.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.That(Required<TextBlock>(comparison, "ComparisonStatus").Text).Contains("cancelled");
            await Assert.That(Required<Image>(comparison, "BeforeImage").Source).IsNull();
            await Assert.That(Required<Image>(comparison, "AfterImage").Source).IsNull();

            Task pendingOnClose = comparison.LoadPairAsync(beforePath, afterPath);
            await comparison.CloseAsync().WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.That(pendingOnClose.IsCompleted).IsTrue();
            await Assert.That(comparison.IsVisible).IsFalse();
        }
        finally
        {
            await comparison.CloseAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
    }
}
