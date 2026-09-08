// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerCameraBookmarkNativeTests
{
    private static async Task<ViewerStageSession> ExerciseSavedViewOperationsAsync(
        MainWindow window, ViewerStageSession session, BookmarkDocumentPicker picker, string destination,
        Func<Task<ViewerStageSession>> prepareReopen, CancellationToken token)
    {
        ViewerCameraBookmark original = (await ReadUiBookmarksAsync(session, token)).Single();
        Window manager = await OpenBookmarkManagerAsync(window, token);
        SelectBookmark(manager, original.Id);
        BookmarkControl<TextBox>(manager, "SavedViewName").Text = "Hero _renamed";
        ClickBookmarkButton(manager, "RenameSavedViewButton");
        await WaitBookmarkOperationAsync(manager, token);
        await Assert.That((await ReadUiBookmarksAsync(session, token)).Single().Name).IsEqualTo("Hero _renamed");
        ClickBookmarkButton(manager, "RemoveSavedViewButton");
        await WaitBookmarkOperationAsync(manager, token);
        await Assert.That(await ReadUiBookmarksAsync(session, token)).IsEmpty();
        await ((CameraBookmarksWindow)manager).CloseAsync();
        await ClickBookmarkMenuAsync(window, "EditUndoMenuItem", token);
        await WaitUiBookmarksAsync(session, items => items is [{ Name: "Hero _renamed" }], token);
        await ClickBookmarkMenuAsync(window, "EditUndoMenuItem", token);
        await WaitUiBookmarksAsync(session, items => items is [{ Name: "Hero _north" }], token);
        await ClickBookmarkMenuAsync(window, "EditRedoMenuItem", token);
        await WaitUiBookmarksAsync(session, items => items is [{ Name: "Hero _renamed" }], token);
        ViewerCameraBookmark saved = (await ReadUiBookmarksAsync(session, token)).Single();
        await Assert.That(saved.Id).IsEqualTo(original.Id);
        await Assert.That(saved.Camera).IsEqualTo(original.Camera);
        await ClickBookmarkMenuAsync(window, "CameraOrbitRightMenuItem", token);
        await WaitBookmarkAsync(() => session.CurrentRenderState.Camera != original.Camera, token);
        manager = await OpenBookmarkManagerAsync(window, token);
        SelectBookmark(manager, saved.Id);
        ClickBookmarkButton(manager, "RecallSavedViewButton");
        await WaitBookmarkOperationAsync(manager, token);
        await Assert.That(BookmarkControl<TextBlock>(manager, "SavedViewsStatus").Text).Contains("recalled exactly");
        await Assert.That(session.CurrentRenderState.Camera).IsEqualTo(saved.Camera);
        await Assert.That(session.CurrentRenderState.Time.TimeCode).IsEqualTo(saved.TimeCode);
        await ((CameraBookmarksWindow)manager).CloseAsync();

        await ClickBookmarkMenuAsync(window, "SaveReviewMenuItem", token);
        await WaitBookmarkAsync(() => File.Exists(destination) &&
            BookmarkControl<TextBlock>(window, "ViewerStatus").Text == "Review saved.", token);
        await Assert.That(await IsReviewDirtyAsync(session, token)).IsFalse();
        picker.SavePath = null;
        await ClickBookmarkMenuAsync(window, "SaveReviewAsMenuItem", token);
        await WaitBookmarkAsync(() => BookmarkControl<TextBlock>(window, "ViewerStatus").Text?.Contains(
            "cancelled", StringComparison.OrdinalIgnoreCase) == true, token);
        await Assert.That(await IsReviewDirtyAsync(session, token)).IsFalse();
        picker.SavePath = destination;
        picker.OpenPath = destination;
        Task<ViewerStageSession> reopened = prepareReopen();
        await ClickBookmarkMenuAsync(window, "OpenReviewMenuItem", token);
        await WaitBookmarkAsync(() => window.OwnedWindows.OfType<DocumentActionWindow>().Any(), token);
        DocumentActionWindow intent = window.OwnedWindows.OfType<DocumentActionWindow>().Single();
        ClickBookmarkButton(intent, "AcceptDocumentActionButton");
        ViewerStageSession current = await reopened.WaitAsync(token);
        await WaitUiBookmarksAsync(current, items => items is [{ Name: "Hero _renamed" }], token);
        await Assert.That(BookmarkControl<MenuItem>(window, "EditUndoMenuItem").IsEnabled).IsFalse();
        await Assert.That(await IsReviewDirtyAsync(current, token)).IsFalse();

        await ClickBookmarkMenuAsync(window, "CommandPaletteMenuItem", token);
        Window palette = window.OwnedWindows.Single(child => child.Title == "Search commands");
        BookmarkControl<TextBox>(palette, "CommandSearch").Text = "Hero _renamed";
        await WaitBookmarkAsync(() => BookmarkControl<ListBox>(palette, "CommandResults").SelectedItem is
            ListBoxItem { Tag: string id } && id == "camera.savedView:" + saved.Id.ToString("N"), token);
        palette.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        await WaitBookmarkAsync(() => current.CurrentRenderState.Camera == saved.Camera &&
            current.CurrentRenderState.Time.TimeCode == saved.TimeCode, token);
        await Assert.That(await IsReviewDirtyAsync(current, token)).IsFalse();
        await Assert.That(BookmarkControl<MenuItem>(window, "EditUndoMenuItem").IsEnabled).IsFalse();
        return current;
    }

    private static async Task<Window> OpenBookmarkManagerAsync(MainWindow window, CancellationToken token)
    {
        await ClickBookmarkMenuAsync(window, "ManageSavedViewsMenuItem", token);
        await WaitBookmarkAsync(() => window.OwnedWindows.OfType<CameraBookmarksWindow>().Any(), token);
        CameraBookmarksWindow manager = window.OwnedWindows.OfType<CameraBookmarksWindow>().Single();
        await WaitBookmarkOperationAsync(manager, token);
        await WaitBookmarkAsync(() => BookmarkControl<TextBlock>(manager, "SavedViewsStatus").Text == "Ready.", token);
        return manager;
    }

    private static void SelectBookmark(Window manager, Guid id)
    {
        ListBox list = BookmarkControl<ListBox>(manager, "SavedViewsList");
        list.SelectedItem = list.Items.OfType<ListBoxItem>().Single(
            item => item.Tag is ViewerCameraBookmark bookmark && bookmark.Id == id);
    }

    private static void ClickBookmarkButton(Control owner, string name) =>
        BookmarkControl<Button>(owner, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task ClickBookmarkMenuAsync(MainWindow window, string name, CancellationToken token)
    {
        MenuItem menu = BookmarkControl<MenuItem>(window, name);
        await WaitBookmarkAsync(() => menu.IsEnabled, token);
        menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private static Task WaitBookmarkOperationAsync(Window window, CancellationToken token) =>
        WaitBookmarkAsync(() => !((CameraBookmarksWindow)window).IsRunning, token);

    private static Task<ViewerCameraBookmark[]> ReadUiBookmarksAsync(
        ViewerStageSession session, CancellationToken token) => session.Scheduler.InvokeAsync(static stage =>
    {
        using UsdLayer review = stage.GetUserReviewLayer();
        var snapshot = review.CaptureAuthored([ViewerCameraBookmarkCatalog.Address]);
        string stamp = ViewerCameraBookmarkCatalog.CreateSourceStamp(stage.CaptureReviewSourceBinding());
        return ViewerCameraBookmarkCatalog.Decode(snapshot, stamp).ToArray();
    }, token).AsTask();

    private static Task<bool> IsReviewDirtyAsync(ViewerStageSession session, CancellationToken token) =>
        session.Scheduler.InvokeAsync(static stage =>
        {
            using UsdLayer review = stage.GetUserReviewLayer();
            return review.GetEditingState().IsDirty;
        }, token).AsTask();

    private static async Task WaitUiBookmarksAsync(
        ViewerStageSession session, Func<ViewerCameraBookmark[], bool> predicate, CancellationToken token)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!predicate(await ReadUiBookmarksAsync(session, token)))
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The native saved-view catalog did not reach the expected state.");
            }
            await Task.Delay(20, token);
        }
    }
}
