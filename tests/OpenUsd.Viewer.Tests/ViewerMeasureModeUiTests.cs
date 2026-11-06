// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerMeasureModeUiTests
{
    [Test]
    public async Task MeasureMenuIsAccessibleAndCannotStartWithoutAStage()
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
            try
            {
                main.Show();
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(
                    () => Required<MenuItem>(main, "ThemeMenu").IsEnabled);
                MenuItem measure = Required<MenuItem>(main, "ToolsMeasureDistanceMenuItem");

                await Assert.That(AutomationProperties.GetName(measure))
                    .IsEqualTo("Measure distance between two picked points");
                await Assert.That(AutomationProperties.GetAutomationId(measure))
                    .IsEqualTo(ViewerCommandIds.ToolsMeasureDistance);
                await Assert.That(measure.ToggleType).IsEqualTo(MenuItemToggleType.CheckBox);
                await Assert.That(measure.InputGesture).IsEqualTo(KeyGesture.Parse("Ctrl+M"));
                await Assert.That(measure.IsEnabled).IsFalse();

                main.KeyPress(Key.M, RawInputModifiers.Control, PhysicalKey.M, "m");
                Dispatcher.UIThread.RunJobs();
                await Assert.That(main.MeasureSession.IsActive).IsFalse();
                await Assert.That(measure.IsChecked).IsFalse();

                // Even a forced click cannot start measuring when no stage is open.
                measure.IsEnabled = true;
                main.KeyPress(Key.M, RawInputModifiers.Control, PhysicalKey.M, "m");
                Dispatcher.UIThread.RunJobs();
                await Assert.That(main.MeasureSession.IsActive).IsFalse();
                await Assert.That(measure.IsChecked).IsFalse();

                main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Dispatcher.UIThread.RunJobs();
                await Assert.That(main.MeasureSession.IsActive).IsFalse();
            }
            finally
            {
                main.Close();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                ViewerStartupOptions.Initialize([]);
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing measure control: {name}");

    private static class SkiaEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
