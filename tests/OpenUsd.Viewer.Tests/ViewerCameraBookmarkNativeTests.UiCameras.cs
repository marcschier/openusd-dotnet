// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUsd.Editing;
using OpenUsd.Geom;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerCameraBookmarkNativeTests
{
    private static async Task ExerciseSavedViewCameraModesAsync(
        MainWindow window, ViewerStageSession session, CancellationToken token)
    {
        await WaitBookmarkAsync(() => BookmarkControl<MenuItem>(window, "StageCamerasMenu").IsEnabled, token);
        MenuItem stageCamera = BookmarkControl<MenuItem>(window, "StageCamerasMenu").Items.OfType<MenuItem>()
            .Single(static item => item.Tag is "/ReviewCamera");
        stageCamera.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await WaitBookmarkAsync(() => session.CurrentRenderState.Camera.View.M43 == -8, token);
        await SetBookmarkTimeAsync(window, session, 2.25, token);
        Window manager = await OpenBookmarkManagerAsync(window, token);
        await WaitBookmarkAsync(() => BookmarkControl<Button>(manager, "SaveCurrentViewButton").IsEnabled, token);
        BookmarkControl<TextBox>(manager, "SavedViewName").Text = "Authored";
        ClickBookmarkButton(manager, "SaveCurrentViewButton");
        await WaitBookmarkOperationAsync(manager, token);
        ViewerCameraBookmark authored = (await ReadUiBookmarksAsync(session, token)).Single(
            static item => item.Name == "Authored");
        await Assert.That(authored.StageCamera?.PrimPath).IsEqualTo("/ReviewCamera");
        await Assert.That(authored.TimeCode).IsEqualTo(2.25);
        await ((CameraBookmarksWindow)manager).CloseAsync();
        await ClickBookmarkMenuAsync(window, "ToggleCameraProjectionMenuItem", token);
        await WaitBookmarkAsync(() => session.CurrentRenderState.Camera.Projection.M44 == 1, token);
        manager = await OpenBookmarkManagerAsync(window, token);
        await WaitBookmarkAsync(() => BookmarkControl<Button>(manager, "SaveCurrentViewButton").IsEnabled, token);
        BookmarkControl<TextBox>(manager, "SavedViewName").Text = "Orthographic";
        ClickBookmarkButton(manager, "SaveCurrentViewButton");
        await WaitBookmarkOperationAsync(manager, token);
        ViewerCameraBookmark orthographic = (await ReadUiBookmarksAsync(session, token)).Single(
            static item => item.Name == "Orthographic");
        await Assert.That(orthographic.FreeCamera.ProjectionMode).IsEqualTo(ViewerCameraProjectionMode.Orthographic);
        SelectBookmark(manager, authored.Id);
        ClickBookmarkButton(manager, "RecallSavedViewButton");
        await WaitBookmarkOperationAsync(manager, token);
        await Assert.That(session.CurrentRenderState.Camera).IsEqualTo(authored.Camera);
        await ((CameraBookmarksWindow)manager).CloseAsync();
        await SetBookmarkTimeAsync(window, session, 3, token);
        await WaitBookmarkAsync(() => session.CurrentRenderState.Camera != authored.Camera, token);
        manager = await OpenBookmarkManagerAsync(window, token);
        SelectBookmark(manager, orthographic.Id);
        ClickBookmarkButton(manager, "RecallSavedViewButton");
        await WaitBookmarkOperationAsync(manager, token);
        await Assert.That(session.CurrentRenderState.Camera).IsEqualTo(orthographic.Camera);
        await ((CameraBookmarksWindow)manager).CloseAsync();
        await SetBookmarkTimeAsync(window, session, 4, token);
        await Assert.That(session.CurrentRenderState.Camera).IsEqualTo(orthographic.Camera)
            .Because("recalling a free view must remove the authored-camera refresh mode");
        await ExerciseSavedViewCancellationAsync(window, session, orthographic, token);

        await session.Scheduler.EditAsync(stage =>
        {
            using UsdLayer prior = stage.GetLocalLayer(stage.EditTargetLayerIdentifier);
            using UsdLayer review = stage.GetUserReviewLayer();
            stage.SetEditTarget(review);
            try
            {
                UsdGeomCamera.Wrap(stage.GetPrim("/ReviewCamera"))
                    .SetTransform(UsdMatrix4d.CreateTranslation(2, 1, 8), authored.TimeCode);
            }
            finally
            {
                stage.SetEditTarget(prior);
            }
        }, UsdStageInvalidationKind.Property, token);
        StageRenderState before = session.CurrentRenderState;
        manager = await OpenBookmarkManagerAsync(window, token);
        SelectBookmark(manager, authored.Id);
        ClickBookmarkButton(manager, "RecallSavedViewButton");
        await WaitBookmarkOperationAsync(manager, token);
        await Assert.That(BookmarkControl<TextBlock>(manager, "SavedViewsStatus").Text).Contains("sample changed");
        await Assert.That(session.CurrentRenderState.Camera).IsEqualTo(before.Camera);
        await Assert.That(session.CurrentRenderState.Time).IsEqualTo(before.Time);
        await ((CameraBookmarksWindow)manager).CloseAsync();

        await session.Scheduler.EditAsync(static stage =>
        {
            using UsdLayer prior = stage.GetLocalLayer(stage.EditTargetLayerIdentifier);
            using UsdLayer review = stage.GetUserReviewLayer();
            stage.SetEditTarget(review);
            try
            {
                stage.GetPrim("/ReviewCamera").SetActive(false);
            }
            finally
            {
                stage.SetEditTarget(prior);
            }
        }, UsdStageInvalidationKind.Composition, token);
        before = session.CurrentRenderState;
        manager = await OpenBookmarkManagerAsync(window, token);
        SelectBookmark(manager, authored.Id);
        ClickBookmarkButton(manager, "RecallSavedViewButton");
        await WaitBookmarkOperationAsync(manager, token);
        await Assert.That(BookmarkControl<TextBlock>(manager, "SavedViewsStatus").Text).Contains("inactive");
        await Assert.That(session.CurrentRenderState.Camera).IsEqualTo(before.Camera);
        await Assert.That(session.CurrentRenderState.Time).IsEqualTo(before.Time);
        await ((CameraBookmarksWindow)manager).CloseAsync();

        double width = window.Width;
        window.Width += 100;
        await WaitBookmarkAsync(() => !orthographic.HasMatchingAspect(session.CurrentRenderState.Viewport), token);
        before = session.CurrentRenderState;
        manager = await OpenBookmarkManagerAsync(window, token);
        SelectBookmark(manager, orthographic.Id);
        ClickBookmarkButton(manager, "RecallSavedViewButton");
        await WaitBookmarkOperationAsync(manager, token);
        await Assert.That(BookmarkControl<TextBlock>(manager, "SavedViewsStatus").Text).Contains("aspect");
        await Assert.That(session.CurrentRenderState.Camera).IsEqualTo(before.Camera);
        await Assert.That(session.CurrentRenderState.Time).IsEqualTo(before.Time);
        await ((CameraBookmarksWindow)manager).CloseAsync();
        window.Width = width;
        await WaitBookmarkAsync(() => orthographic.HasMatchingAspect(session.CurrentRenderState.Viewport), token);
    }

    private static async Task ExerciseSavedViewCancellationAsync(
        MainWindow window, ViewerStageSession session, ViewerCameraBookmark bookmark, CancellationToken token)
    {
        Window manager = await OpenBookmarkManagerAsync(window, token);
        SelectBookmark(manager, bookmark.Id);
        ClickBookmarkButton(window, "PlayPauseButton");
        await WaitBookmarkAsync(() => BookmarkControl<Button>(window, "PlayPauseButton").Content is "_Pause", token);
        await using var blocked = new SavedViewRecallBlock(window, session, token);
        try
        {
            ClickBookmarkButton(manager, "RecallSavedViewButton");
            await blocked.Entered.WaitAsync(token);
            try
            {
                await WaitBookmarkAsync(() => ((CameraBookmarksWindow)manager).IsRunning &&
                    BookmarkControl<Button>(window, "PlayPauseButton").Content is "_Play", token);
            }
            catch (TimeoutException exception)
            {
                throw new TimeoutException(
                    $"Recall cancellation setup: running={((CameraBookmarksWindow)manager).IsRunning}; " +
                    $"playback={BookmarkControl<Button>(window, "PlayPauseButton").Content}; " +
                    $"manager={BookmarkControl<TextBlock>(manager, "SavedViewsStatus").Text}; " +
                    $"viewer={BookmarkControl<TextBlock>(window, "ViewerStatus").Text}.", exception);
            }
            StageRenderState before = session.CurrentRenderState;
            StageRenderState? completedState = null;
            TextBlock status = BookmarkControl<TextBlock>(manager, "SavedViewsStatus");
            void ObserveCancellation(object? sender, AvaloniaPropertyChangedEventArgs args)
            {
                if (args.Property == TextBlock.TextProperty &&
                    status.Text?.StartsWith("Cancelled", StringComparison.Ordinal) == true)
                {
                    completedState = session.CurrentRenderState;
                }
            }
            status.PropertyChanged += ObserveCancellation;
            try
            {
                ClickBookmarkButton(manager, "SavedViewsCancelButton");
                // Cancellation drains admitted scheduler work; it cannot preempt this fixture's held callback.
                await blocked.ReleaseAsync();
                await WaitBookmarkOperationAsync(manager, token);
            }
            finally
            {
                status.PropertyChanged -= ObserveCancellation;
            }
            await Assert.That(BookmarkControl<TextBlock>(manager, "SavedViewsStatus").Text).Contains("Cancelled");
            await Assert.That(completedState).IsNotNull();
            await Assert.That(completedState!.Camera).IsEqualTo(before.Camera);
            await Assert.That(completedState.Time).IsEqualTo(before.Time);
            await Assert.That(BookmarkControl<Button>(window, "PlayPauseButton").Content).IsEqualTo("_Pause")
                .Because("a cancelled recall must resume the pre-recall playback mode");
        }
        finally
        {
            await blocked.ReleaseAsync();
            await ((CameraBookmarksWindow)manager).CloseAsync();
        }
        if (BookmarkControl<Button>(window, "PlayPauseButton").Content is "_Pause")
        {
            ClickBookmarkButton(window, "PlayPauseButton");
            await WaitBookmarkAsync(() => BookmarkControl<Button>(window, "PlayPauseButton").Content is "_Play", token);
        }
    }

    private static async Task SetBookmarkTimeAsync(
        MainWindow window, ViewerStageSession session, double timeCode, CancellationToken token)
    {
        TextBox input = BookmarkControl<TextBox>(window, "CurrentTimeInput");
        await WaitBookmarkAsync(() => input.IsEnabled, token);
        input.Text = ViewerTimelineMath.Format(timeCode);
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        await WaitBookmarkAsync(() => session.CurrentRenderState.Time.TimeCode == timeCode, token);
    }
}
