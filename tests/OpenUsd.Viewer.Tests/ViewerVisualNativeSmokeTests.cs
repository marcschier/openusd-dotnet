// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Runs alone in a native desktop test process, with temporary settings and no USD stage.
/// Opt in with OPENUSD_VIEWER_THEME_SMOKE=1 and the exact class treenode filter.
/// </summary>
[NotInParallel]
public sealed class ViewerVisualNativeSmokeTests
{
    [Test]
    public async Task ThemeCommandsUpdateTheLiveShellDialogsAndPersistenceWithoutRestylingTheHost()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_VIEWER_THEME_SMOKE") != "1")
        {
            Skip.Test("Run the isolated Windows desktop theme smoke with OPENUSD_VIEWER_THEME_SMOKE=1.");
        }

        string root = Path.Combine(AppContext.BaseDirectory, "viewer-theme-smoke", Guid.NewGuid().ToString("N"));
        string artifacts = Environment.GetEnvironmentVariable("OPENUSD_VIEWER_THEME_SMOKE_ARTIFACTS") ??
            Path.Combine(AppContext.BaseDirectory, "TestResults", "viewer-theme-smoke");
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
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
                        await ExerciseWindowAsync(
                            Path.Combine(root, "auto"), Path.Combine(artifacts, "auto"), "Auto");
                        await ExerciseWindowAsync(
                            Path.Combine(root, "forced"), Path.Combine(artifacts, "forced"), "D3D12");
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
            Name = "Viewer theme native smoke"
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
        bool stopped;
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(50));
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
        await Assert.That(stopped).IsTrue().Because("the native Viewer smoke must shut down");
    }

    private static async Task ExerciseWindowAsync(string root, string artifacts, string startupRenderer)
    {
        ViewerStartupOptions.Initialize(new ViewerHostOptions { Renderer = startupRenderer });
        Directory.CreateDirectory(artifacts);
        using var store = new ViewerSettingsStore(root);
        ViewerSettings initial = ViewerSettings.Default with
        {
            RendererPreference = "Vulkan",
            PickTarget = "edge",
            SelectionMode = "xray"
        };
        await store.SaveAsync(initial);
        var unrelatedButton = new Button { Content = "Host action" };
        var host = new Window
        {
            Width = 320,
            Height = 200,
            RequestedThemeVariant = ThemeVariant.Light,
            Content = unrelatedButton
        };
        var window = new MainWindow(new RecentStageStore(root), store);
        var dialog = new ShortcutsWindow();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            host.Show();
            window.Show();
            await Task.Delay(150);
            window.UpdateLayout();
            double hostButtonHeight = unrelatedButton.MinHeight;
            IBrush? hostButtonBackground = unrelatedButton.Background;
            ComboBox renderer = Required<ComboBox>(window, "RendererSelector");
            ComboBox background = Required<ComboBox>(window, "BackgroundColorSelector");
            int rendererIndex = renderer.SelectedIndex;
            int backgroundIndex = background.SelectedIndex;
            await Assert.That(rendererIndex).IsEqualTo(startupRenderer == "Auto" ? 3 : 2);
            bool highContrast = Application.Current?.TryGetFeature(typeof(IPlatformSettings))
                is IPlatformSettings settings &&
                settings.GetColorValues().ContrastPreference == ColorContrastPreference.High;

            foreach ((string choice, ThemeVariant variant, Color panelColor) in new[]
            {
                ("Dark", ThemeVariant.Dark, highContrast ? Colors.Black : Color.Parse("#202A31")),
                ("Light", ThemeVariant.Light, highContrast ? Colors.White : Color.Parse("#FAFAF8"))
            })
            {
                Required<MenuItem>(window, $"Theme{choice}MenuItem")
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Task.Delay(100);
                window.UpdateLayout();
                ViewerSettings persisted = (await store.LoadAsync()).Settings;
                Color actualPanelColor = Required<Border>(window, "StagePanel").Background
                    is ISolidColorBrush panelBrush
                    ? panelBrush.Color
                    : throw new InvalidOperationException("The Viewer panel has no solid background.");

                await Assert.That(window.ActualThemeVariant).IsEqualTo(variant);
                await Assert.That(actualPanelColor).IsEqualTo(panelColor);
                await Assert.That(Required<MenuItem>(window, $"Theme{choice}MenuItem").IsChecked).IsTrue();
                await Assert.That(persisted.ThemePreference).IsEqualTo(
                    choice == "Dark" ? ViewerThemePreference.Dark : ViewerThemePreference.Light);
                await Assert.That(persisted.RendererPreference).IsEqualTo(initial.RendererPreference);
                await Assert.That(persisted.PickTarget).IsEqualTo(initial.PickTarget);
                await Assert.That(persisted.SelectionMode).IsEqualTo(initial.SelectionMode);
                await Assert.That(renderer.SelectedIndex).IsEqualTo(rendererIndex);
                await Assert.That(background.SelectedIndex).IsEqualTo(backgroundIndex);
                await Assert.That(Required<TabItem>(window, "DiagnosticsTab").IsVisible).IsFalse();
                await Assert.That(Required<TabItem>(window, "HydraSceneTab").IsVisible).IsFalse();
                await Assert.That(Required<TabItem>(window, "TfDebugTab").IsVisible).IsFalse();
                await Assert.That(host.ActualThemeVariant).IsEqualTo(ThemeVariant.Light);
                await Assert.That(unrelatedButton.MinHeight).IsEqualTo(hostButtonHeight);
                await Assert.That(unrelatedButton.Background).IsSameReferenceAs(hostButtonBackground);

                foreach ((int width, int height) in new[] { (1440, 900), (960, 600) })
                {
                    window.Width = width;
                    window.Height = height;
                    await Task.Delay(50);
                    window.UpdateLayout();
                    Button frame = Required<Button>(window, "FrameSelectedButton");
                    Point origin = frame.TranslatePoint(default, window) ??
                        throw new InvalidOperationException("The frame action is not in the Viewer tree.");
                    await Assert.That(origin.X + frame.Bounds.Width).IsLessThanOrEqualTo(window.Bounds.Width);
                    SaveCapture(window, Path.Combine(artifacts, $"viewer-{choice.ToLowerInvariant()}-{width}.png"));
                }
            }

            dialog.Show(window);
            Required<MenuItem>(window, "ThemeDarkMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Task.Delay(100);
            await Assert.That(dialog.ActualThemeVariant).IsEqualTo(ThemeVariant.Dark);
            var contrast = new ViewerThemeContrast(window);
            contrast.Apply(ColorContrastPreference.High);
            await Task.Delay(50);
            Color highContrastPanel = Required<Border>(window, "StagePanel").Background
                is ISolidColorBrush highContrastBrush
                ? highContrastBrush.Color
                : throw new InvalidOperationException("The high-contrast panel has no solid background.");
            await Assert.That(highContrastPanel).IsEqualTo(Colors.Black);
            await Assert.That(window.RequestedThemeVariant).IsEqualTo(ThemeVariant.Dark);
            await Assert.That((await store.LoadAsync()).Settings.ThemePreference)
                .IsEqualTo(ViewerThemePreference.Dark);
            SaveCapture(window, Path.Combine(artifacts, "viewer-dark-high-contrast.png"));
            contrast.Apply(ColorContrastPreference.NoPreference);
            Required<MenuItem>(window, "ThemeSystemMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Task.Delay(100);
            await Assert.That(window.RequestedThemeVariant).IsEqualTo(ThemeVariant.Default);
            await Assert.That(dialog.RequestedThemeVariant).IsEqualTo(ThemeVariant.Default);
            await Assert.That((await store.LoadAsync()).Settings.ThemePreference)
                .IsEqualTo(ViewerThemePreference.System);

            renderer.SelectedIndex = 0;
            Required<MenuItem>(window, "ThemeLightMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Task.Delay(100);
            await Assert.That((await store.LoadAsync()).Settings.RendererPreference).IsEqualTo("Auto")
                .Because("an explicit operator renderer choice is persisted even after a host startup override");
            Console.WriteLine(
                "Viewer theme smoke: 1440x900 and 960x600 logical units; " +
                $"scale={window.RenderScaling}; {artifacts}");
        }
        finally
        {
            dialog.Close();
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            host.Close();
        }
    }

    private static T Required<T>(Control parent, string name) where T : Control =>
        parent.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing Viewer control: {name}");

    private static void SaveCapture(Window window, string path)
    {
        double scale = window.RenderScaling;
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(window.Bounds.Width * scale),
                (int)Math.Ceiling(window.Bounds.Height * scale)),
            new Vector(96 * scale, 96 * scale));
        bitmap.Render(window);
        bitmap.Save(path, new PngBitmapEncoderOptions());
    }
}
