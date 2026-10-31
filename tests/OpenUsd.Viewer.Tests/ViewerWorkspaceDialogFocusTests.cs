// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerWorkspaceDialogFocusTests
{
    [Test]
    [Arguments("CompletedJobsMenuItem", "HistoryCloseButton", 0, false)]
    [Arguments("CompletedJobsMenuItem", "HistoryCloseButton", 0, true)]
    [Arguments("CompletedJobsMenuItem", "HistoryCloseButton", 1, false)]
    [Arguments("CompletedJobsMenuItem", "HistoryCloseButton", 1, true)]
    [Arguments("CompletedJobsMenuItem", "HistoryCloseButton", 2, false)]
    [Arguments("CompletedJobsMenuItem", "HistoryCloseButton", 2, true)]
    [Arguments("CompareCapturesMenuItem", "CloseComparisonButton", 0, false)]
    [Arguments("CompareCapturesMenuItem", "CloseComparisonButton", 0, true)]
    [Arguments("CompareCapturesMenuItem", "CloseComparisonButton", 1, false)]
    [Arguments("CompareCapturesMenuItem", "CloseComparisonButton", 1, true)]
    [Arguments("CompareCapturesMenuItem", "CloseComparisonButton", 2, false)]
    [Arguments("CompareCapturesMenuItem", "CloseComparisonButton", 2, true)]
    [Arguments("CommandPaletteMenuItem", "ClosePaletteButton", 0, false)]
    [Arguments("CommandPaletteMenuItem", "ClosePaletteButton", 0, true)]
    [Arguments("CommandPaletteMenuItem", "ClosePaletteButton", 1, false)]
    [Arguments("CommandPaletteMenuItem", "ClosePaletteButton", 1, true)]
    [Arguments("CommandPaletteMenuItem", "ClosePaletteButton", 2, false)]
    [Arguments("CommandPaletteMenuItem", "ClosePaletteButton", 2, true)]
    public async Task ClosingWorkspaceDialogRestoresOnlyItsOwnedKeyboardFocus(
        string menuName, string closeName, int focusDestination, bool dark)
    {
        using var files = new ViewerCompletedJobTestFiles();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaEntryPoint));
        await session.Dispatch(async () =>
        {
            ViewerStartupOptions.Initialize(new ViewerHostOptions { Renderer = "Storm" });
            using var settings = new ViewerSettingsStore(Path.Combine(files.Root, "settings"));
            var main = new MainWindow(new RecentStageStore(Path.Combine(files.Root, "recent")), settings);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            main.Closed += (_, _) => closed.TrySetResult();
            Window? other = null;
            try
            {
                main.Show();
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => Required<MenuItem>(main, "ThemeMenu").IsEnabled);
                main.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                Button origin = Required<Button>(main, "CommandPaletteButton");
                await Assert.That(origin.Focus()).IsTrue();
                Required<MenuItem>(main, menuName).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Window dialog = main.OwnedWindows.Single();
                await Assert.That(dialog.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                await Assert.That(Required<Button>(dialog, closeName).Focus()).IsTrue();
                Control expectedFocus = origin;
                if (focusDestination == 1)
                {
                    expectedFocus = Required<Button>(main, "WelcomeOpenButton");
                    main.Activate();
                    await Assert.That(expectedFocus.Focus()).IsTrue();
                }
                else if (focusDestination == 2)
                {
                    var input = new TextBox { Text = "Deliberately moved keyboard focus" };
                    other = new Window { Content = input, Width = 320, Height = 200 };
                    other.Show();
                    await Assert.That(input.Focus()).IsTrue();
                    expectedFocus = input;
                }
                var dialogClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                dialog.Closed += (_, _) => dialogClosed.TrySetResult();
                dialog.Close();
                await dialogClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Dispatcher.UIThread.RunJobs();
                await Assert.That(dialog.IsVisible).IsFalse();
                await Assert.That(expectedFocus.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(Required<RendererSwitchingViewport>(main, "ViewportHost").AttachedControlCount)
                    .IsEqualTo(0);
                await Assert.That(main.OwnedWindows).IsEmpty();
                main.Activate();
                await Assert.That(origin.Focus()).IsTrue();
                Required<MenuItem>(main, menuName).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Window reopened = main.OwnedWindows.Single();
                await Assert.That(reopened).IsNotSameReferenceAs(dialog);
                await Assert.That(Required<Button>(reopened, closeName).Focus()).IsTrue();
                var reopenedClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                reopened.Closed += (_, _) => reopenedClosed.TrySetResult();
                reopened.Close();
                await reopenedClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(origin.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(main.OwnedWindows).IsEmpty();
            }
            finally
            {
                other?.Close();
                main.Close();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                ViewerStartupOptions.Initialize([]);
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing workspace control: {name}");

    private static class SkiaEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
