// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerCameraBookmarkNativeTests
{
    private static async Task<ViewerStageSession> ExercisePendingSavedViewReloadAsync(
        MainWindow window, ViewerStageSession session, Func<Task<ViewerStageSession>> prepareReopen,
        CancellationToken token)
    {
        ViewerCameraBookmark bookmark = (await ReadUiBookmarksAsync(session, token)).Single(
            static item => item.Name == "Hero _renamed");
        Window manager = await OpenBookmarkManagerAsync(window, token);
        SelectBookmark(manager, bookmark.Id);
        await using var blocked = new SavedViewRecallBlock(window, session, token);
        Task<ViewerStageSession> reopened = prepareReopen();
        try
        {
            ClickBookmarkButton(manager, "RecallSavedViewButton");
            await blocked.Entered.WaitAsync(token);
            await WaitBookmarkAsync(() => ((CameraBookmarksWindow)manager).IsRunning &&
                BookmarkControl<TextBlock>(window, "ViewerStatus").Text == "Recalling saved view...", token);
            MenuItem reload = BookmarkControl<MenuItem>(window, "ReloadStageMenuItem");
            await Assert.That(reload.IsEnabled).IsTrue();
            reload.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        finally
        {
            await blocked.ReleaseAsync();
        }
        await WaitBookmarkAsync(() => window.OwnedWindows.OfType<DocumentChangesWindow>().Any(), token);
        DocumentChangesWindow decision = window.OwnedWindows.OfType<DocumentChangesWindow>().Single();
        ClickBookmarkButton(decision, "DiscardDocumentChangesButton");
        ViewerStageSession current = await reopened.WaitAsync(token);
        await Assert.That(current).IsNotSameReferenceAs(session);
        await Assert.That(manager.IsVisible).IsFalse();
        await Assert.That(await ReadUiBookmarksAsync(current, token)).IsEmpty();
        await Assert.That(async () => await session.Scheduler.InvokeAsync(
            static stage => stage.RootLayerIdentifier, token)).Throws<ObjectDisposedException>();
        ClickBookmarkButton(manager, "RecallSavedViewButton");
        await Assert.That(await ReadUiBookmarksAsync(current, token)).IsEmpty()
            .Because("a closed old-document manager cannot replay into the replacement document");
        return current;
    }

    private static async Task ExercisePendingSavedViewCloseAsync(
        MainWindow window, ViewerStageSession session, Task closed, CancellationToken token)
    {
        await ClickBookmarkMenuAsync(window, "ResetCameraLegacyMenuItem", token);
        Window manager = await OpenBookmarkManagerAsync(window, token);
        await WaitBookmarkAsync(() => BookmarkControl<Button>(manager, "SaveCurrentViewButton").IsEnabled, token);
        BookmarkControl<TextBox>(manager, "SavedViewName").Text = "Close drain";
        ClickBookmarkButton(manager, "SaveCurrentViewButton");
        await WaitBookmarkOperationAsync(manager, token);
        ViewerCameraBookmark bookmark = (await ReadUiBookmarksAsync(session, token)).Single();
        SelectBookmark(manager, bookmark.Id);
        await using var blocked = new SavedViewRecallBlock(window, session, token);
        try
        {
            ClickBookmarkButton(manager, "RecallSavedViewButton");
            await blocked.Entered.WaitAsync(token);
            await WaitBookmarkAsync(() => ((CameraBookmarksWindow)manager).IsRunning &&
                BookmarkControl<TextBlock>(window, "ViewerStatus").Text == "Recalling saved view...", token);
            window.Close();
        }
        finally
        {
            await blocked.ReleaseAsync();
        }
        await WaitBookmarkAsync(() => window.OwnedWindows.OfType<DocumentChangesWindow>().Any(), token);
        DocumentChangesWindow decision = window.OwnedWindows.OfType<DocumentChangesWindow>().Single();
        ClickBookmarkButton(decision, "DiscardDocumentChangesButton");
        await closed.WaitAsync(TimeSpan.FromSeconds(15), token);
        await Assert.That(manager.IsVisible).IsFalse();
        await Assert.That(async () => await session.Scheduler.InvokeAsync(
            static stage => stage.RootLayerIdentifier, token)).Throws<ObjectDisposedException>();
    }

    private sealed class SavedViewRecallBlock : IAsyncDisposable
    {
        private readonly ViewerStageSession _session;
        private readonly CancellationToken _token;
        private readonly TextBlock _status;
        private readonly ManualResetEventSlim _release = new();
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task _blocked = Task.CompletedTask;
        private bool _started;

        internal SavedViewRecallBlock(MainWindow window, ViewerStageSession session, CancellationToken token)
        {
            _session = session;
            _token = token;
            _status = BookmarkControl<TextBlock>(window, "ViewerStatus");
            _status.PropertyChanged += OnStatus;
        }

        internal Task Entered => _entered.Task;

        private void OnStatus(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_started || e.Property != TextBlock.TextProperty || _status.Text != "Recalling saved view...")
            {
                return;
            }
            _started = true;
            _blocked = _session.Scheduler.InvokeAsync(_ =>
            {
                _entered.TrySetResult();
                _release.Wait(_token);
                return true;
            }, _token).AsTask();
        }

        internal async Task ReleaseAsync()
        {
            _status.PropertyChanged -= OnStatus;
            _release.Set();
            await _blocked.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await ReleaseAsync();
            _release.Dispose();
        }
    }
}
