// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerCameraBookmarksWindowTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RefreshHandsKeyboardFocusToCancellationAndRestoresItAfterDrain(bool dark)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            int reads = 0;
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var drain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var window = new CameraBookmarksWindow(
                async token =>
                {
                    if (++reads == 2)
                    {
                        try
                        {
                            await Task.Delay(Timeout.Infinite, token);
                        }
                        catch (OperationCanceledException)
                        {
                            cancelled.TrySetResult();
                            await drain.Task;
                            throw;
                        }
                    }
                    throw new IOException("The original review cannot be read.");
                },
                (_, _) => throw new InvalidOperationException("Capture must not run."),
                (_, _, _, _) => throw new InvalidOperationException("Editing must not run."),
                (_, _) => throw new InvalidOperationException("Recall must not run."),
                () => new ViewerCameraBookmarkAvailability(false, false, false, "Original review", "Read refused."))
            {
                Width = 560,
                Height = 440,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                TextBox name = Required<TextBox>(window, "SavedViewName");
                await Assert.That(name.IsKeyboardFocusWithin).IsTrue();
                Button refresh = Required<Button>(window, "SavedViewsRefreshButton");
                await Assert.That(refresh.Focus()).IsTrue();
                refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Button cancel = Required<Button>(window, "SavedViewsCancelButton");
                await Assert.That(window.IsRunning).IsTrue();
                await Assert.That(cancel.IsKeyboardFocusWithin).IsTrue();
                cancel.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                await cancelled.Task.WaitAsync(lifetime.Token);
                await Assert.That(cancel.IsEnabled).IsFalse();
                await Assert.That(Required<Button>(window, "SavedViewsCloseButton").IsKeyboardFocusWithin).IsTrue();
                await Assert.That(window.IsVisible).IsTrue();
                await Assert.That(window.IsRunning).IsTrue();
                drain.TrySetResult();
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(() => !window.IsRunning);
                await Assert.That(refresh.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(Required<TextBlock>(window, "SavedViewsStatus").Text).StartsWith("Cancelled.");
                refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Assert.That(reads).IsEqualTo(3);
                await Assert.That(refresh.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(Required<TextBlock>(window, "SavedViewsStatus").Text)
                    .Contains("The original review cannot be read.");
                refresh.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                await window.CloseAsync().WaitAsync(lifetime.Token);
                await Assert.That(window.IsVisible).IsFalse();
            }
            finally
            {
                drain.TrySetResult();
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    [Arguments(560, 440, false, false)]
    [Arguments(560, 440, true, false)]
    [Arguments(560, 440, true, true)]
    [Arguments(660, 520, false, true)]
    public async Task SavedViewsKeepCloseAndFocusedNameReachableWithLongSourceOrReadFailure(
        int width, int height, bool longContext, bool dark)
    {
        string source = longContext
            ? string.Join(' ', Enumerable.Repeat("Original review source", 96))
            : "Original review source";
        string reason = longContext
            ? string.Join(' ', Enumerable.Repeat("Capture is unavailable until the document is ready.", 24))
            : "Capture is unavailable.";
        int reads = 0;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var window = new CameraBookmarksWindow(
                _ =>
                {
                    reads++;
                    return Task.FromException<ViewerCameraBookmarkCapture>(
                        new IOException("The original review cannot be read."));
                },
                (_, _) => throw new InvalidOperationException("Capture must not run."),
                (_, _, _, _) => throw new InvalidOperationException("Editing must not run."),
                (_, _) => throw new InvalidOperationException("Recall must not run."),
                () => new ViewerCameraBookmarkAvailability(false, false, false, source, reason))
            {
                Width = width,
                Height = height,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                window.UpdateLayout();
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
                await Assert.That(Required<TextBlock>(window, "SavedViewsSource").Text).IsEqualTo(source);
                await Assert.That(Required<TextBlock>(window, "SavedViewsCaptureReason").Text).IsEqualTo(reason);
                await Assert.That(Required<TextBlock>(window, "SavedViewsStatus").Text)
                    .Contains("The original review cannot be read.");
                await Assert.That(reads).IsEqualTo(1);
                await Assert.That(window.IsRunning).IsFalse();
                Button close = Required<Button>(window, "SavedViewsCloseButton");
                await AssertInsideWindowAsync(close, window);
                TextBox name = Required<TextBox>(window, "SavedViewName");
                await Assert.That(name.IsKeyboardFocusWithin).IsTrue();
                name.BringIntoView();
                window.UpdateLayout();
                await AssertInsideWindowAsync(name, window);
                ScrollViewer body = Required<ScrollViewer>(window, "SavedViewsBodyScroll");
                Point? nameOrigin = name.TranslatePoint(default, body);
                await Assert.That(nameOrigin).IsNotNull();
                await Assert.That(nameOrigin!.Value.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(nameOrigin.Value.Y + name.Bounds.Height)
                    .IsLessThanOrEqualTo(body.Viewport.Height);
                TextBlock status = Required<TextBlock>(window, "SavedViewsStatus");
                status.BringIntoView();
                window.UpdateLayout();
                Point? statusOrigin = status.TranslatePoint(default, body);
                await Assert.That(statusOrigin).IsNotNull();
                await Assert.That(statusOrigin!.Value.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(statusOrigin.Value.Y + status.Bounds.Height)
                    .IsLessThanOrEqualTo(body.Viewport.Height);
                await Assert.That(Required<ListBox>(window, "SavedViewsList").Bounds.Height)
                    .IsGreaterThanOrEqualTo(120);
                await Assert.That(Required<Button>(window, "SaveCurrentViewButton").IsEnabled).IsFalse();
                await Assert.That(Required<Button>(window, "RecallSavedViewButton").IsEnabled).IsFalse();
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
    public async Task ClosingSavedViewsWaitsForCancelledReadToDrain()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var drain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var window = new CameraBookmarksWindow(
                async token =>
                {
                    try
                    {
                        await Task.Delay(Timeout.Infinite, token);
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled.TrySetResult();
                        await drain.Task;
                        throw;
                    }
                    throw new InvalidOperationException("The read must be cancelled.");
                },
                (_, _) => throw new InvalidOperationException("Capture must not run."),
                (_, _, _, _) => throw new InvalidOperationException("Editing must not run."),
                (_, _) => throw new InvalidOperationException("Recall must not run."),
                () => new ViewerCameraBookmarkAvailability(false, false, false,
                    string.Join(' ', Enumerable.Repeat("Original review source", 96)), "Reading saved views."))
            {
                Width = 560,
                Height = 440
            };
            window.Show();
            try
            {
                window.UpdateLayout();
                await Assert.That(window.IsRunning).IsTrue();
                Button cancel = Required<Button>(window, "SavedViewsCancelButton");
                await AssertInsideWindowAsync(cancel, window);
                await Assert.That(cancel.IsEnabled).IsTrue();
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await cancelled.Task.WaitAsync(lifetime.Token);
                await Assert.That(cancel.IsEnabled).IsFalse();
                await Assert.That(window.IsRunning).IsTrue();
                Required<Button>(window, "SavedViewsCloseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Task closing = window.CloseAsync();
                await Assert.That(closing.IsCompleted).IsFalse();
                await Assert.That(window.IsVisible).IsTrue();
                drain.TrySetResult();
                await closing.WaitAsync(lifetime.Token);
                await Assert.That(window.IsVisible).IsFalse();
                await Assert.That(window.IsRunning).IsFalse();
            }
            finally
            {
                drain.TrySetResult();
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    private static async Task AssertInsideWindowAsync(Control control, Window window)
    {
        Point? origin = control.TranslatePoint(default, window);
        await Assert.That(origin).IsNotNull();
        await Assert.That(origin!.Value.Y).IsGreaterThanOrEqualTo(0);
        await Assert.That(origin.Value.Y + control.Bounds.Height).IsLessThanOrEqualTo(window.ClientSize.Height);
        await Assert.That(origin.Value.X).IsGreaterThanOrEqualTo(0);
        await Assert.That(origin.Value.X + control.Bounds.Width).IsLessThanOrEqualTo(window.ClientSize.Width);
    }

    private static T Required<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing saved-view control: {name}");

    private static class SkiaAppEntryPoint
    {
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
