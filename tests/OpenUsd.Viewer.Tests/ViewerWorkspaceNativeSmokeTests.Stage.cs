// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerWorkspaceNativeSmokeTests
{
    private static async Task ExerciseStormWindowAsync(string root, bool empty)
    {
        string pluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH") ??
            throw new InvalidOperationException("Set OPENUSD_PLUGIN_PATH to the existing native plugin directory.");
        var opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            Renderer = "Storm",
            PluginPath = pluginPath,
            StageReadyAsync = (session, _) =>
            {
                opened.TrySetResult(session);
                return Task.CompletedTask;
            }
        });
        using var store = new ViewerSettingsStore(root);
        var recent = new RecentStageStore(root);
        if (!empty)
        {
            await store.SaveAsync(ViewerSettings.Default with
            {
                RendererPreference = "Vulkan",
                ThemePreference = ViewerThemePreference.Dark,
                PickTarget = "edge",
                SelectionMode = "xray",
                SnapTimelineToFrames = true
            });
        }
        else
        {
            string emptyPath = Path.Combine(root, "empty.usda");
            await File.WriteAllTextAsync(emptyPath, "#usda 1.0\n");
            await recent.AddAsync(emptyPath);
        }
        var window = new MainWindow(recent, store);
        nint nativeChild = 0;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            window.Show();
            await Task.Delay(150);
            await Assert.That(Required<Border>(window, "WelcomePanel").IsVisible).IsTrue();
            await Assert.That(Required<Border>(window, "TimelinePanel").IsEffectivelyVisible).IsFalse();
            if (empty)
            {
                Button entry = Required<StackPanel>(window, "WelcomeRecentItems").Children.OfType<Button>().First();
                await Assert.That(AutomationProperties.GetName(entry)).Contains("empty.usda");
                entry.Command!.Execute(null);
            }
            else
            {
                Click(window, "OpenSampleMenuItem");
            }
            ViewerStageSession session = await opened.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Task.Delay(150);
            nativeChild = Required<RendererSwitchingViewport>(window, "ViewportHost")
                .GetActiveStormNavigationSource()?.GetEvidenceWindow() ??
                throw new InvalidOperationException("The native Storm viewport did not attach.");
            await Assert.That(Required<Border>(window, "WelcomePanel").IsVisible).IsFalse();
            await Assert.That(Required<Grid>(window, "MainContentGrid").IsVisible).IsTrue();
            await Assert.That(Required<StackPanel>(window, "WelcomeRecentItems").Children.Count)
                .IsEqualTo(empty ? 2 : 1);
            if (!empty)
            {
                await ExerciseLoadedReviewAsync(window, session, store);
            }
            else
            {
                await Assert.That(Required<TextBlock>(window, "StageStatus").Text).IsEqualTo("empty.usda");
                await Assert.That(Required<MenuItem>(window, "ReloadStageMenuItem").IsEnabled).IsTrue();
            }
        }
        finally
        {
            foreach (Window child in window.OwnedWindows.ToArray())
            {
                child.Close();
            }
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        await Assert.That(IsWindow(nativeChild)).IsEqualTo(0);
    }

    private static async Task ExerciseLoadedReviewAsync(
        MainWindow window, ViewerStageSession session, ViewerSettingsStore store)
    {
        string authored = await File.ReadAllTextAsync(session.StagePath);
        string editTarget = await session.Scheduler.InvokeAsync(static stage => stage.EditTargetLayerIdentifier);
        foreach (string preset in new[] { "Review", "Materials", "Presentation", "Inspect" })
        {
            Click(window, $"Workspace{preset}MenuItem");
            await Task.Delay(75);
            ViewerSettings saved = (await store.LoadAsync()).Settings;
            await Assert.That(saved.RendererPreference).IsEqualTo("Vulkan");
            await Assert.That(saved.ThemePreference).IsEqualTo(ViewerThemePreference.Dark);
            await Assert.That(saved.PickTarget).IsEqualTo("edge");
            await Assert.That(saved.SelectionMode).IsEqualTo("xray");
            await Assert.That(saved.SnapTimelineToFrames).IsTrue();
            await Assert.That(await session.Scheduler.InvokeAsync(static stage => stage.EditTargetLayerIdentifier))
                .IsEqualTo(editTarget);
            await Assert.That(await File.ReadAllTextAsync(session.StagePath)).IsEqualTo(authored);
        }
        Grid content = Required<Grid>(window, "MainContentGrid");
        double originalWidth = content.ColumnDefinitions[0].Width.Value;
        GridSplitter splitter = Required<GridSplitter>(window, "StagePanelSplitter");
        splitter.Focus();
        splitter.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
        window.UpdateLayout();
        await Assert.That(content.ColumnDefinitions[0].Width.Value).IsGreaterThan(originalWidth)
            .Because("workspace size constraints must not prevent expanding the existing splitter");
        double windowWidth = window.Width;
        content.ColumnDefinitions[0].Width = new GridLength(1600);
        content.ColumnDefinitions[4].Width = new GridLength(180);
        window.Width = 960;
        await WaitUntilAsync(() => Math.Abs(window.Bounds.Width - 960) < 1);
        window.UpdateLayout();
        await Assert.That(content.ColumnDefinitions[2].ActualWidth).IsGreaterThanOrEqualTo(419)
            .Because("a compact window must retain viewport space even with very uneven saved panel widths");
        await Assert.That(content.ColumnDefinitions[0].Width.Value).IsEqualTo(1600)
            .Because("passive fitting must not overwrite the saved width request");
        window.Width = windowWidth;
        Click(window, "WorkspaceInspectMenuItem");
        await Task.Delay(75);
        window.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.F,
            KeyModifiers = KeyModifiers.Control
        });
        TextBox filter = Required<TextBox>(window, "HierarchyFilter");
        await Assert.That(filter.IsKeyboardFocusWithin).IsTrue();
        filter.Text = "TealCube";
        TreeView hierarchy = Required<TreeView>(window, "StageHierarchy");
        TreeViewItem rootItem = hierarchy.Items.OfType<TreeViewItem>().Single();
        rootItem.IsExpanded = true;
        hierarchy.SelectedItem = rootItem.Items.OfType<TreeViewItem>().Single(item =>
            AutomationProperties.GetName(item) == "Prim /Review/TealCube");
        await WaitUntilAsync(() => Required<Button>(window, "WorkspaceInspectButton").IsEffectivelyEnabled);
        Required<Button>(window, "WorkspaceInspectButton").Command!.Execute(null);
        Required<Button>(window, "FrameSelectedButton").Command!.Execute(null);
        await Task.Delay(200);
        await Assert.That(Required<TabControl>(window, "InspectorTabs").SelectedItem)
            .IsSameReferenceAs(Required<TabItem>(window, "PropertiesTab"));
        RendererSwitchingViewport viewport = Required<RendererSwitchingViewport>(window, "ViewportHost");
        StormNativeControlHost source = viewport.GetActiveStormNavigationSource() ??
            throw new InvalidOperationException("The workspace smoke did not create a live native Storm child.");
        OpenUsdStormFramebufferCapture capture = await source.CaptureFramebufferAsync(CancellationToken.None);
        await Assert.That(capture.NonBackgroundPixelCount).IsGreaterThan(16UL);
        await Assert.That(capture.RgbaPixels.Length).IsEqualTo(capture.Width * capture.Height * 4);

        source.FocusEvidenceWindow();
        await Task.Delay(50);
        await Assert.That(source.TryGetNavigationInput(out OpenUsdStormNavigationInput focused) && focused.Focused)
            .IsTrue();
        ViewerCameraState camera = session.Camera;
        Click(window, "CommandPaletteMenuItem");
        await Task.Delay(75);
        Window palette = window.OwnedWindows.Single(child => child.Title == "Search commands");
        TextBox search = Required<TextBox>(palette, "CommandSearch");
        await Assert.That(search.IsKeyboardFocusWithin).IsTrue();
        nint handle = palette.TryGetPlatformHandle()?.Handle ??
            throw new InvalidOperationException("The command palette has no native window.");
        await Assert.That(GetWindowRect(handle, out NativeRect rectangle)).IsNotEqualTo(0);
        var centre = new NativePoint(
            rectangle.Left + ((rectangle.Right - rectangle.Left) / 2),
            rectangle.Top + ((rectangle.Bottom - rectangle.Top) / 2));
        nint ownerHandle = window.TryGetPlatformHandle()!.Handle;
        await Assert.That(GetWindow(handle, 4)).IsEqualTo(ownerHandle);
        await Assert.That(GetAncestor(source.GetEvidenceWindow(), 2)).IsEqualTo(ownerHandle);
        await Assert.That(IsAbove(handle, ownerHandle)).IsTrue()
            .Because("the owned palette must be above the root containing the native Storm child");
        nint screenPoint = unchecked((nint)((centre.Y << 16) | (centre.X & 0xffff)));
        await Assert.That(SendMessageW(handle, 0x0084, 0, screenPoint)).IsEqualTo((nint)1)
            .Because("the native palette window must report its centre as an interactive client area");
        await Assert.That(palette.RenderScaling).IsEqualTo(window.RenderScaling);
        foreach (char character in "pfgkn")
        {
            _ = SendMessageW(handle, 0x0100, char.ToUpperInvariant(character), 1);
            _ = SendMessageW(handle, 0x0102, character, 1);
            _ = SendMessageW(handle, 0x0101, char.ToUpperInvariant(character), unchecked((nint)0xC0000001U));
        }
        await Task.Delay(100);
        await Assert.That(search.Text).IsEqualTo("pfgkn");
        await Assert.That(session.Camera).IsEqualTo(camera);
        _ = SetActiveWindow(ownerHandle);
        source.FocusEvidenceWindow();
        await WaitUntilAsync(() => !palette.IsActive);
        bool ownerFocused = source.TryGetNavigationInput(out OpenUsdStormNavigationInput ownerInput) &&
            ownerInput.Focused;
        await Assert.That(ownerFocused)
            .IsTrue();
        await Assert.That(GetWindowRect(source.GetEvidenceWindow(), out NativeRect viewportRectangle)).IsNotEqualTo(0);
        int viewportX = viewportRectangle.Left + ((viewportRectangle.Right - viewportRectangle.Left) / 2);
        int viewportY = viewportRectangle.Top + ((viewportRectangle.Bottom - viewportRectangle.Top) / 2);
        nint viewportPoint = unchecked((nint)((viewportY << 16) | (viewportX & 0xffff)));
        // Let native polling establish its new focus baseline before sending a fresh wheel gesture.
        await Task.Delay(100);
        ViewerCameraState beforeWheel = session.Camera;
        _ = SendMessageW(source.GetEvidenceWindow(), 0x020A, 120 << 16, viewportPoint);
        await WaitUntilAsync(() => session.Camera != beforeWheel);
        await Assert.That(palette.IsVisible).IsTrue()
            .Because("native navigation must work with an open but inactive modeless palette");
        palette.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        await Task.Delay(75);
        await Assert.That(source.TryGetNavigationInput(out OpenUsdStormNavigationInput restored) && restored.Focused)
            .IsTrue().Because("closing the palette must restore the actual native viewport focus");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The workspace UI did not reach its expected state.");
            }
            await Task.Delay(20);
        }
    }

    private static bool IsAbove(nint upper, nint lower)
    {
        // Compare our windows' z-order, not whichever unrelated desktop window is foreground.
        nint candidate = lower;
        for (int count = 0; count < 256 && candidate != 0; count++)
        {
            candidate = GetWindow(candidate, 3);
            if (candidate == upper)
            {
                return true;
            }
        }
        return false;
    }

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint SetActiveWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial int IsWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint window, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint command);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowRect(nint window, out NativeRect rectangle);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativeRect(int Left, int Top, int Right, int Bottom);
}
