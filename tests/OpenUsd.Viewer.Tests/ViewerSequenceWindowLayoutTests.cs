// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerSequenceWindowLayoutTests
{
    [Test]
    [Arguments(false, false, false)]
    [Arguments(true, false, false)]
    [Arguments(true, true, false)]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    public async Task SequenceActionsAndFullRefusalRemainReachableAtMinimumSize(
        bool longContext, bool dark, bool physics)
    {
        string description = longContext
            ? string.Join(' ', Enumerable.Repeat("Renderer with original scene context", 64))
            : "Renderer";
        string refusal = longContext
            ? string.Join(' ', Enumerable.Repeat("This exact requested output is unsupported.", 48))
            : "This exact requested output is unsupported.";
        int captures = 0;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new RenderImageSequenceWindow(0, 2, description, _ => refusal,
                (_, _, _, _, _) =>
                {
                    captures++;
                    throw new InvalidOperationException("A refused request must not render.");
                },
                (_, _, _) =>
                {
                    captures++;
                    throw new InvalidOperationException("A refused physics request must not render.");
                },
                _ => refusal, physicsMode: physics)
            {
                Width = 500,
                Height = 610,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                await Assert.That(Required<TextBlock>(window, "SequenceRenderer").Text).IsEqualTo(description);
                TextBlock status = Required<TextBlock>(window, "SequenceStatus");
                await Assert.That(status.Text).IsEqualTo(refusal);
                foreach (Button action in new[]
                {
                    Required<Button>(window, "SequenceViewResultsButton"),
                    Required<Button>(window, "SequenceCancelButton"),
                    Required<Button>(window, "SequenceCloseButton"),
                    Required<Button>(window, "SequenceRenderButton")
                })
                {
                    await AssertInsideAsync(action, window);
                }
                await Assert.That(Required<Grid>(window, "SequenceRangeControls").IsVisible).IsEqualTo(!physics);
                await Assert.That(Required<Grid>(window, "SequencePhysicsControls").IsVisible).IsEqualTo(physics);
                TextBox start = Required<TextBox>(window, physics ? "SequencePhysicsFrames" : "SequenceStartTime");
                await Assert.That(start.IsKeyboardFocusWithin).IsTrue();
                start.BringIntoView();
                window.UpdateLayout();
                await AssertInsideAsync(start, window);
                status.BringIntoView();
                window.UpdateLayout();
                ScrollViewer body = Required<ScrollViewer>(window, "SequenceBodyScroll");
                Point? statusOrigin = status.TranslatePoint(default, body);
                await Assert.That(statusOrigin).IsNotNull();
                await Assert.That(statusOrigin!.Value.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(statusOrigin.Value.Y).IsLessThan(body.Viewport.Height);
                body.ScrollToEnd();
                window.UpdateLayout();
                await Assert.That(body.Offset.Y + body.Viewport.Height)
                    .IsGreaterThanOrEqualTo(body.Extent.Height - 1);
                TextBox output = Required<TextBox>(window, "SequenceOutputLocation");
                await AssertInsideAsync(output, window);
                await Assert.That(Required<Button>(window, "SequenceRenderButton").IsEnabled).IsFalse();
                await Assert.That(captures).IsEqualTo(0);
                Required<Button>(window, "SequenceCloseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            await Assert.That(window.IsVisible).IsFalse();
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    public async Task ManagedCompletedResultsKeepJobRetryCancellationAndOwnerDrainSemantics()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            await ViewerCompletedJobPreviewJourney.ExerciseLifetimesAsync();
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    private static async Task AssertInsideAsync(Control control, Window window)
    {
        Point? origin = control.TranslatePoint(default, window);
        await Assert.That(origin).IsNotNull();
        await Assert.That(origin!.Value.X).IsGreaterThanOrEqualTo(0);
        await Assert.That(origin.Value.Y).IsGreaterThanOrEqualTo(0);
        await Assert.That(origin.Value.X + control.Bounds.Width).IsLessThanOrEqualTo(window.ClientSize.Width);
        await Assert.That(origin.Value.Y + control.Bounds.Height).IsLessThanOrEqualTo(window.ClientSize.Height);
    }

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing sequence control: {name}");

    private static class SkiaAppEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
