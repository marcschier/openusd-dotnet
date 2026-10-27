// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerSequenceWindowLayoutTests
{
    private static readonly string[] RenderOutcomes = ["success", "failure", "cancel"];
    private static readonly string[] FocusDestinations = ["owned", "close", "other"];

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task SequenceRenderHandsFocusToCancelAndRestoresOnlyOwnedFocusAfterDrain(bool dark, bool physics)
    {
        using var files = new ViewerCompletedJobTestFiles();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            foreach (string outcome in RenderOutcomes)
            {
                foreach (string destination in FocusDestinations)
                {
                    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    CancellationToken observedToken = default;
                    int published = 0;
                    int renders = 0;
                    var job = files.CreateJob(count: 1);
                    async Task<RenderDiskJobResult> renderAsync(CancellationToken token)
                    {
                        renders++;
                        if (renders > 1)
                        {
                            return job;
                        }
                        observedToken = token;
                        entered.TrySetResult();
                        await release.Task;
                        token.ThrowIfCancellationRequested();
                        if (outcome == "failure")
                        {
                            throw new IOException("Controlled sequence refusal.");
                        }
                        return job;
                    }
                    var window = new RenderImageSequenceWindow(0, 0, "Original renderer", _ => null,
                        (_, _, _, _, token) => renderAsync(token),
                        (_, _, token) => renderAsync(token), _ => null, physicsMode: physics,
                        completed: (_, _) => published++)
                    {
                        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
                    };
                    Required<TextBox>(window, "SequenceOutputFolder").Text = files.Root;
                    var otherAction = new Button { Content = "Other window action" };
                    var other = new Window { Content = otherAction, Width = 240, Height = 160 };
                    window.Show();
                    try
                    {
                        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
                        Button render = Required<Button>(window, "SequenceRenderButton");
                        Button cancel = Required<Button>(window, "SequenceCancelButton");
                        Button close = Required<Button>(window, "SequenceCloseButton");
                        await Assert.That(render.Focus()).IsTrue();
                        render.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        await Assert.That(window.IsRunning).IsTrue();
                        await Assert.That(render.IsEnabled).IsFalse();
                        await Assert.That(cancel.IsKeyboardFocusWithin).IsTrue();
                        if (outcome == "cancel")
                        {
                            Control escapeTarget = cancel;
                            if (destination == "close")
                            {
                                await Assert.That(close.Focus()).IsTrue();
                                escapeTarget = close;
                            }
                            escapeTarget.RaiseEvent(new KeyEventArgs
                            {
                                RoutedEvent = InputElement.KeyDownEvent,
                                Key = Key.Escape
                            });
                            await Assert.That(observedToken.IsCancellationRequested).IsTrue();
                            await Assert.That(cancel.IsEnabled).IsFalse();
                            await Assert.That(close.IsKeyboardFocusWithin).IsTrue();
                            await Assert.That(window.IsVisible).IsTrue();
                            await Assert.That(window.IsRunning).IsTrue();
                        }
                        Control expected = destination == "owned" ? render :
                            destination == "close" ? close : otherAction;
                        if (destination == "close")
                        {
                            await Assert.That(close.Focus()).IsTrue();
                        }
                        else if (destination == "other")
                        {
                            other.Show();
                            await Assert.That(otherAction.Focus()).IsTrue();
                        }
                        release.TrySetResult();
                        var timeout = System.Diagnostics.Stopwatch.StartNew();
                        while (window.IsRunning)
                        {
                            if (timeout.Elapsed > TimeSpan.FromSeconds(5))
                            {
                                throw new TimeoutException("The sequence did not drain.");
                            }
                            await Task.Delay(10);
                        }
                        await Assert.That(expected.IsKeyboardFocusWithin).IsTrue();
                        await Assert.That(published).IsEqualTo(outcome == "success" ? 1 : 0);
                        await Assert.That(Required<Button>(window, "SequenceViewResultsButton").IsEnabled)
                            .IsEqualTo(outcome == "success");
                        await Assert.That(Required<TextBlock>(window, "SequenceStatus").Text)
                            .Contains(outcome == "success" ? "Completed" :
                                outcome == "failure" ? "Controlled sequence refusal." : "Cancelled.");
                        await Assert.That(renders).IsEqualTo(1);
                        other.Close();
                        await Assert.That(render.Focus()).IsTrue();
                        render.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await Assert.That(window.IsRunning).IsFalse();
                        await Assert.That(render.IsKeyboardFocusWithin).IsTrue();
                        await Assert.That(published).IsEqualTo(outcome == "success" ? 2 : 1);
                        await Assert.That(renders).IsEqualTo(2);
                    }
                    finally
                    {
                        release.TrySetResult();
                        other.Close();
                        await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    }
                }
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

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
        using var files = new ViewerCompletedJobTestFiles();
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
            Required<TextBox>(window, "SequenceOutputFolder").Text = files.Root;
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
        await ViewerAuthoredRenderProductTests.ExerciseLayoutAsync(longContext: false, dark: false);
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
