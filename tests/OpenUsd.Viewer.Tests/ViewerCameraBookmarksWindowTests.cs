// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using OpenUsd.Editing;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerCameraBookmarksWindowTests
{
    private static readonly string[] BookmarkActionNames =
    [
        "SaveCurrentViewButton", "RenameSavedViewButton", "RemoveSavedViewButton", "RecallSavedViewButton"
    ];

    [Test]
    [Arguments(false, "success", "owned")]
    [Arguments(true, "success", "owned")]
    [Arguments(false, "success", "close")]
    [Arguments(true, "success", "close")]
    [Arguments(false, "success", "body")]
    [Arguments(true, "success", "body")]
    [Arguments(false, "success", "other")]
    [Arguments(true, "success", "other")]
    [Arguments(false, "failure", "owned")]
    [Arguments(true, "failure", "owned")]
    [Arguments(false, "failure", "close")]
    [Arguments(true, "failure", "close")]
    [Arguments(false, "failure", "body")]
    [Arguments(true, "failure", "body")]
    [Arguments(false, "failure", "other")]
    [Arguments(true, "failure", "other")]
    [Arguments(false, "cancelled", "owned")]
    [Arguments(true, "cancelled", "owned")]
    [Arguments(false, "cancelled", "body")]
    [Arguments(true, "cancelled", "body")]
    [Arguments(false, "cancelled", "other")]
    [Arguments(true, "cancelled", "other")]
    public async Task InitialSavedViewReadRestoresOnlyOwnedFocusAndPreservesMovedFocus(
        bool dark, string outcome, string destination)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            string stamp = new('A', 64);
            ViewerCameraBookmark bookmark = ViewerCameraBookmark.CreateFree(
                Guid.NewGuid(), "Original saved view", stamp, 12.5, new ViewportDimensions(800, 600),
                new ViewerCameraNavigationState(false, new(1, 2, 3), 5, 0.5f, 0.2f,
                    ViewerCameraProjectionMode.Perspective, 0.7f, 3, 0.1f, 100, 800f / 600));
            var capture = new ViewerCameraBookmarkCapture(
                new ViewerAuthoredEditCapture(Guid.NewGuid(), "review",
                    new UsdLayerAuthoredSnapshot(new(1, 2, 3), 0, [], [])), stamp, [bookmark]);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int reads = 0;
            var window = new CameraBookmarksWindow(
                async token =>
                {
                    if (++reads == 1)
                    {
                        entered.TrySetResult();
                        await release.Task;
                        token.ThrowIfCancellationRequested();
                        if (outcome == "failure")
                        {
                            throw new IOException("Controlled initial saved-view refusal.");
                        }
                    }
                    return capture;
                },
                (_, _) => throw new InvalidOperationException("Initial discovery must not capture."),
                (_, _, _, _) => throw new InvalidOperationException("Initial discovery must not edit."),
                (_, _) => throw new InvalidOperationException("Initial discovery must not recall."),
                () => new ViewerCameraBookmarkAvailability(true, true, true, "Original review", "Ready."))
            {
                Width = 560,
                Height = 440,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            var otherAction = new Button { Content = "Other window action" };
            var other = new Window { Content = otherAction, Width = 240, Height = 160 };
            window.Show();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.That(window.IsRunning).IsTrue();
                await Assert.That(Required<TextBox>(window, "SavedViewName").IsEnabled).IsFalse();
                await Assert.That(Required<ListBox>(window, "SavedViewsList").IsEnabled).IsFalse();
                await Assert.That(Required<Button>(window, "SavedViewsCancelButton").IsKeyboardFocusWithin).IsTrue();
                if (outcome == "cancelled")
                {
                    Required<Button>(window, "SavedViewsCancelButton")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Assert.That(window.IsRunning).IsTrue();
                }
                Control expectedFocus = destination switch
                {
                    "owned" => Required<TextBox>(window, "SavedViewName"),
                    "close" => Required<Button>(window, "SavedViewsCloseButton"),
                    "body" => Required<ScrollViewer>(window, "SavedViewsBodyScroll"),
                    "other" => otherAction,
                    _ => throw new ArgumentOutOfRangeException(nameof(destination))
                };
                if (destination == "other")
                {
                    other.Show();
                    await Assert.That(otherAction.Focus()).IsTrue();
                }
                else if (destination != "owned")
                {
                    await Assert.That(expectedFocus.Focus()).IsTrue();
                }
                release.TrySetResult();
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(() => !window.IsRunning);
                await Assert.That(expectedFocus.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(reads).IsEqualTo(1);
                ListBox list = Required<ListBox>(window, "SavedViewsList");
                await Assert.That(list.ItemCount).IsEqualTo(outcome == "success" ? 1 : 0);
                await Assert.That(Required<TextBlock>(window, "SavedViewsStatus").Text).Contains(outcome switch
                {
                    "success" => "Ready.",
                    "cancelled" => "Cancelled.",
                    _ => "Controlled initial saved-view refusal."
                });
                other.Close();
                Button refresh = Required<Button>(window, "SavedViewsRefreshButton");
                await Assert.That(refresh.Focus()).IsTrue();
                await window.RefreshAsync();
                await Assert.That(reads).IsEqualTo(2);
                await Assert.That(refresh.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(list.ItemCount).IsEqualTo(1);
                await Assert.That(((ListBoxItem)list.Items[0]!).Tag).IsSameReferenceAs(bookmark);
                await Assert.That(Required<TextBlock>(window, "SavedViewsStatus").Text).IsEqualTo("Ready.");
                await Assert.That(window.ActualThemeVariant)
                    .IsEqualTo(dark ? ThemeVariant.Dark : ThemeVariant.Light);
            }
            finally
            {
                release.TrySetResult();
                other.Close();
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            await Assert.That(window.IsVisible).IsFalse();
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    [Arguments(false, "reordered")]
    [Arguments(true, "reordered")]
    [Arguments(false, "renamed")]
    [Arguments(true, "renamed")]
    [Arguments(false, "removed")]
    [Arguments(true, "removed")]
    [Arguments(false, "same-name-replacement")]
    [Arguments(true, "same-name-replacement")]
    public async Task RefreshButtonRetainsOnlyTheSelectedBookmarkIdentityAndUsesItsNewSnapshot(bool dark, string change)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            string stamp = new('A', 64);
            ViewerCameraBookmark first = create("First");
            ViewerCameraBookmark selected = create("Selected");
            ViewerCameraBookmark updated = selected.Rename(change == "renamed" ? "Updated name" : selected.Name);
            ViewerCameraBookmark[] refreshed = change switch
            {
                "removed" => [first],
                "same-name-replacement" => [create(selected.Name), first],
                _ => [updated, first]
            };
            bool retained = change is "reordered" or "renamed";
            var edit = new ViewerAuthoredEditCapture(Guid.NewGuid(), "review",
                new UsdLayerAuthoredSnapshot(new(1, 2, 3), 0, [], []));
            int reads = 0;
            int recalls = 0;
            ViewerCameraBookmark? recalled = null;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var window = new CameraBookmarksWindow(
                async token =>
                {
                    if (++reads == 1)
                    {
                        return new ViewerCameraBookmarkCapture(edit, stamp, [first, selected]);
                    }
                    await release.Task.WaitAsync(token);
                    return new ViewerCameraBookmarkCapture(edit, stamp, refreshed);
                },
                (_, _) => throw new InvalidOperationException("Refresh must not capture."),
                (_, _, _, _) => throw new InvalidOperationException("Refresh must not edit."),
                (bookmark, _) =>
                {
                    recalls++;
                    recalled = bookmark;
                    return Task.CompletedTask;
                },
                () => new ViewerCameraBookmarkAvailability(true, true, true, "Original review", "Ready."))
            {
                Width = 560,
                Height = 440,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                ListBox list = Required<ListBox>(window, "SavedViewsList");
                list.SelectedIndex = 1;
                await Assert.That(Required<TextBox>(window, "SavedViewName").Text).IsEqualTo(selected.Name);
                Button refresh = Required<Button>(window, "SavedViewsRefreshButton");
                await Assert.That(refresh.Focus()).IsTrue();
                refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Assert.That(window.IsRunning).IsTrue();
                await Assert.That(Required<Button>(window, "SavedViewsCancelButton").IsKeyboardFocusWithin).IsTrue();
                await Assert.That(list.IsEnabled).IsFalse();
                foreach (string action in BookmarkActionNames)
                {
                    await Assert.That(Required<Button>(window, action).IsEnabled).IsFalse();
                }
                await window.RefreshAsync();
                await Assert.That(reads).IsEqualTo(2);
                release.TrySetResult();
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(() => !window.IsRunning);
                await Assert.That(refresh.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(list.SelectedIndex).IsEqualTo(retained ? 0 : -1);
                await Assert.That((list.SelectedItem as ListBoxItem)?.Tag)
                    .IsSameReferenceAs(retained ? updated : null);
                await Assert.That(recalls).IsEqualTo(0);
                foreach (string action in BookmarkActionNames.Skip(1))
                {
                    await Assert.That(Required<Button>(window, action).IsEnabled).IsEqualTo(retained);
                }
                if (retained)
                {
                    await Assert.That(Required<TextBox>(window, "SavedViewName").Text).IsEqualTo(updated.Name);
                }
                await Assert.That(list.Focus()).IsTrue();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(() => !window.IsRunning);
                await Assert.That(recalls).IsEqualTo(retained ? 1 : 0);
                await Assert.That(recalled).IsSameReferenceAs(retained ? updated : null);
                await window.RefreshAsync();
                await Assert.That(reads).IsEqualTo(3);
                await Assert.That((list.SelectedItem as ListBoxItem)?.Tag)
                    .IsSameReferenceAs(retained ? updated : null);
            }
            finally
            {
                release.TrySetResult();
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            await Assert.That(window.IsVisible).IsFalse();
            return 0;

            ViewerCameraBookmark create(string name) => ViewerCameraBookmark.CreateFree(
                Guid.NewGuid(), name, stamp, 12.5, new ViewportDimensions(800, 600),
                new ViewerCameraNavigationState(false, new(1, 2, 3), 5, 0.5f, 0.2f,
                    ViewerCameraProjectionMode.Perspective, 0.7f, 3, 0.1f, 100, 800f / 600));
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SavedViewContextCanBeReadWithKeyboardScrollingWithoutStartingAnOperation(bool dark)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            int reads = 0;
            string source = string.Join(' ', Enumerable.Repeat("Original review source and persistence.", 96));
            string refusal = string.Join(' ', Enumerable.Repeat("The original review cannot be read.", 24));
            var window = new CameraBookmarksWindow(
                _ =>
                {
                    reads++;
                    return Task.FromException<ViewerCameraBookmarkCapture>(new IOException(refusal));
                },
                (_, _) => throw new InvalidOperationException("Reading context must not capture."),
                (_, _, _, _) => throw new InvalidOperationException("Reading context must not edit."),
                (_, _) => throw new InvalidOperationException("Reading context must not recall."),
                () => new ViewerCameraBookmarkAvailability(false, false, false, source, "Unavailable."))
            {
                Width = 560,
                Height = 440,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                window.UpdateLayout();
                ScrollViewer body = Required<ScrollViewer>(window, "SavedViewsBodyScroll");
                await Assert.That(body.Focus()).IsTrue();
                await Assert.That(ReferenceEquals(window.FocusManager?.GetFocusedElement(), body)).IsTrue();
                for (int page = 0; page < 64 && body.Offset.Y > 0; page++)
                {
                    window.KeyPress(Key.PageUp, RawInputModifiers.None, PhysicalKey.PageUp, null);
                    window.UpdateLayout();
                }
                await Assert.That(body.Offset.Y).IsEqualTo(0);
                TextBlock context = Required<TextBlock>(window, "SavedViewsSource");
                await Assert.That(context.Text).IsEqualTo(source);
                Point? contextOrigin = context.TranslatePoint(default, body);
                await Assert.That(contextOrigin).IsNotNull();
                await Assert.That(contextOrigin!.Value.Y).IsGreaterThanOrEqualTo(0);
                await Assert.That(contextOrigin.Value.Y).IsLessThan(body.Viewport.Height);
                window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
                window.UpdateLayout();
                await Assert.That(body.Offset.Y).IsGreaterThan(0);
                for (int page = 0; page < 64 && body.Offset.Y < body.Extent.Height - body.Viewport.Height; page++)
                {
                    window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
                    window.UpdateLayout();
                }
                await Assert.That(body.Offset.Y).IsEqualTo(body.Extent.Height - body.Viewport.Height);
                TextBlock status = Required<TextBlock>(window, "SavedViewsStatus");
                await Assert.That(status.Text).Contains(refusal);
                Point? statusOrigin = status.TranslatePoint(default, body);
                await Assert.That(statusOrigin).IsNotNull();
                await Assert.That(statusOrigin!.Value.Y + status.Bounds.Height).IsGreaterThan(0);
                await Assert.That(statusOrigin.Value.Y + status.Bounds.Height)
                    .IsLessThanOrEqualTo(body.Viewport.Height);
                await Assert.That(reads).IsEqualTo(1);
                await Assert.That(window.IsRunning).IsFalse();
                await Assert.That(body.IsKeyboardFocusWithin).IsTrue();
                await AssertInsideWindowAsync(Required<Button>(window, "SavedViewsCloseButton"), window);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await window.CloseAsync().WaitAsync(lifetime.Token);
                await Assert.That(window.IsVisible).IsFalse();
            }
            finally
            {
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            return 0;
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EnterOnSelectedSavedViewUsesGuardedRecallAndRestoresListFocusAfterCancellation(bool dark)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SkiaAppEntryPoint));
        await session.Dispatch(async () =>
        {
            string stamp = new('A', 64);
            ViewerCameraBookmark[] bookmarks = [create("First"), create("Second")];
            var capture = new ViewerCameraBookmarkCapture(
                new ViewerAuthoredEditCapture(Guid.NewGuid(), "review",
                    new UsdLayerAuthoredSnapshot(new(1, 2, 3), 0, [], [])), stamp, bookmarks);
            bool available = true;
            int recalls = 0;
            ViewerCameraBookmark? recalled = null;
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var drain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var window = new CameraBookmarksWindow(
                _ => Task.FromResult(capture),
                (_, _) => throw new InvalidOperationException("Keyboard recall must not capture."),
                (_, _, _, _) => throw new InvalidOperationException("Keyboard recall must not edit."),
                async (bookmark, token) =>
                {
                    recalled = bookmark;
                    int attempt = ++recalls;
                    if (attempt == 3)
                    {
                        return;
                    }
                    if (attempt == 2)
                    {
                        throw new IOException("The saved camera is no longer available.");
                    }
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
                },
                () => new ViewerCameraBookmarkAvailability(false, false, available, "Original review", "Read only."))
            {
                Width = 560,
                Height = 440,
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
            };
            window.Show();
            try
            {
                ListBox list = Required<ListBox>(window, "SavedViewsList");
                await Assert.That(list.Focus()).IsTrue();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await Assert.That(recalls).IsEqualTo(0);
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                await Assert.That(list.SelectedIndex).IsEqualTo(1);
                window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
                await Assert.That(recalls).IsEqualTo(0);
                available = false;
                window.UpdateContext();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await Assert.That(recalls).IsEqualTo(0);
                available = true;
                window.UpdateContext();
                TextBox name = Required<TextBox>(window, "SavedViewName");
                name.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await Assert.That(recalls).IsEqualTo(0);
                list.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await Assert.That(recalls).IsEqualTo(1);
                await Assert.That(ReferenceEquals(recalled, bookmarks[1])).IsTrue();
                await Assert.That(window.IsRunning).IsTrue();
                Button cancel = Required<Button>(window, "SavedViewsCancelButton");
                await Assert.That(cancel.IsKeyboardFocusWithin).IsTrue();
                list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                await Assert.That(recalls).IsEqualTo(1);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await cancelled.Task.WaitAsync(lifetime.Token);
                await Assert.That(window.IsRunning).IsTrue();
                await Assert.That(window.IsVisible).IsTrue();
                await Assert.That(cancel.IsEnabled).IsFalse();
                drain.TrySetResult();
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(() => !window.IsRunning);
                await Assert.That(list.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(list.SelectedIndex).IsEqualTo(1);
                await Assert.That(Required<TextBlock>(window, "SavedViewsStatus").Text).StartsWith("Cancelled.");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(() => !window.IsRunning);
                await Assert.That(recalls).IsEqualTo(2);
                await Assert.That(Required<TextBlock>(window, "SavedViewsStatus").Text)
                    .Contains("The saved camera is no longer available.");
                await Assert.That(list.IsKeyboardFocusWithin).IsTrue();
                await Assert.That(name.Text).IsEqualTo("Second");
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(() => !window.IsRunning);
                await Assert.That(recalls).IsEqualTo(3);
                await Assert.That(ReferenceEquals(recalled, bookmarks[1])).IsTrue();
                await Assert.That(Required<TextBlock>(window, "SavedViewsStatus").Text)
                    .IsEqualTo("Saved view recalled exactly. Playback is paused; the review was not edited.");
                await Assert.That(list.IsKeyboardFocusWithin).IsTrue();
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await window.CloseAsync().WaitAsync(lifetime.Token);
            }
            finally
            {
                drain.TrySetResult();
                await window.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            return 0;

            ViewerCameraBookmark create(string name) => ViewerCameraBookmark.CreateFree(
                Guid.NewGuid(), name, stamp, 12.5, new ViewportDimensions(800, 600),
                new ViewerCameraNavigationState(false, new(1, 2, 3), 5, 0.5f, 0.2f,
                    ViewerCameraProjectionMode.Perspective, 0.7f, 3, 0.1f, 100, 800f / 600));
        }, lifetime.Token).WaitAsync(lifetime.Token);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task RefreshHandsKeyboardFocusToCancellationAndRestoresItAfterDrain(
        bool dark, bool moveFocus)
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
                ScrollViewer body = Required<ScrollViewer>(window, "SavedViewsBodyScroll");
                if (moveFocus)
                {
                    await Assert.That(body.Focus()).IsTrue();
                }
                drain.TrySetResult();
                await ViewerCompletedJobPreviewJourney.WaitUntilAsync(() => !window.IsRunning);
                await Assert.That((moveFocus ? (Control)body : refresh).IsKeyboardFocusWithin).IsTrue();
                await Assert.That(Required<TextBlock>(window, "SavedViewsStatus").Text).StartsWith("Cancelled.");
                await Assert.That(refresh.Focus()).IsTrue();
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
