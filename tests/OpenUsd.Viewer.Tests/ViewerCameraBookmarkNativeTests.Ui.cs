// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUsd.Editing;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerCameraBookmarkNativeTests
{
    private static async Task ExerciseSavedViewsUiAsync(ViewerCameraBookmarkFixture files, CancellationToken token)
    {
        var opened = new TaskCompletionSource<ViewerStageSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        string sourcePath = Path.Combine(files.Root, "views.usda");
        await File.WriteAllTextAsync(sourcePath, SavedViewCameraStage, token);
        ViewerStartupOptions.Initialize(new ViewerHostOptions
        {
            StagePath = sourcePath,
            Renderer = "D3D12",
            PluginPath = Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH"),
            StageReadyAsync = (session, _) =>
            {
                Volatile.Read(ref opened).TrySetResult(session);
                return Task.CompletedTask;
            }
        });
        using var settings = new ViewerSettingsStore(Path.Combine(files.Root, "saved-view-settings"));
        var picker = new BookmarkDocumentPicker { SavePath = files.DestinationPath };
        var window = new MainWindow(new RecentStageStore(files.Root), settings, picker);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            window.Show();
            ViewerStageSession session = await opened.Task.WaitAsync(token);
            await WaitBookmarkAsync(() => BookmarkControl<MenuItem>(window, "ManageSavedViewsMenuItem").IsEnabled,
                token);
            BookmarkControl<MenuItem>(window, "ManageSavedViewsMenuItem")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitBookmarkAsync(() => window.OwnedWindows.Any(child => child.Title == "Saved views"), token);
            Window manager = window.OwnedWindows.Single(child => child.Title == "Saved views");
            await WaitBookmarkAsync(
                () => BookmarkControl<TextBlock>(manager, "SavedViewsStatus").Text == "Ready.", token);
            await Assert.That(BookmarkControl<Button>(manager, "SaveCurrentViewButton").IsEnabled).IsFalse();
            await Assert.That(BookmarkControl<TextBlock>(manager, "SavedViewsCaptureReason").Text)
                .Contains("Automatic");
            BookmarkControl<TextBox>(manager, "SavedViewName").Text = "Cancelled name";
            manager.Close();
            await WaitBookmarkAsync(() => !manager.IsVisible, token);
            await Assert.That(await session.Scheduler.InvokeAsync(static stage =>
            {
                using UsdLayer review = stage.GetUserReviewLayer();
                return review.GetEditingState().IsDirty;
            }, token)).IsFalse();
            BookmarkControl<MenuItem>(window, "ResetCameraLegacyMenuItem")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitBookmarkAsync(() => session.CurrentRenderState.Camera.Mode == CameraMode.Matrices, token);
            BookmarkControl<MenuItem>(window, "ManageSavedViewsMenuItem")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitBookmarkAsync(() => window.OwnedWindows.Any(child => child.Title == "Saved views"), token);
            manager = window.OwnedWindows.Single(child => child.Title == "Saved views");
            await WaitBookmarkAsync(() => BookmarkControl<Button>(manager, "SaveCurrentViewButton").IsEnabled, token);
            BookmarkControl<TextBox>(manager, "SavedViewName").Text = "Hero _north";
            BookmarkControl<Button>(manager, "SaveCurrentViewButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitBookmarkAsync(() => BookmarkControl<ListBox>(manager, "SavedViewsList").ItemCount == 1, token);
            await Assert.That(BookmarkControl<TextBlock>(manager, "SavedViewsStatus").Text).Contains("unsaved");
            await Assert.That(await session.Scheduler.InvokeAsync(static stage =>
            {
                using UsdLayer review = stage.GetUserReviewLayer();
                var snapshot = review.CaptureAuthored([ViewerCameraBookmarkCatalog.Address]);
                string stamp = ViewerCameraBookmarkCatalog.CreateSourceStamp(stage.CaptureReviewSourceBinding());
                return ViewerCameraBookmarkCatalog.Decode(snapshot, stamp).Single().Name;
            }, token)).IsEqualTo("Hero _north");
            manager.Close();
            await WaitBookmarkAsync(() => !manager.IsVisible, token);
            session = await ExerciseSavedViewOperationsAsync(window, session, picker, files.DestinationPath,
                () =>
                {
                    opened = new TaskCompletionSource<ViewerStageSession>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    return opened.Task;
                }, token);
            await ExerciseSavedViewCameraModesAsync(window, session, token);
            session = await ExercisePendingSavedViewReloadAsync(window, session,
                () =>
                {
                    opened = new TaskCompletionSource<ViewerStageSession>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    return opened.Task;
                }, token);
            await ExercisePendingSavedViewCloseAsync(window, session, closed.Task, token);
            await Assert.That(await File.ReadAllTextAsync(sourcePath, token)).IsEqualTo(SavedViewCameraStage);
        }
        finally
        {
            window.Close();
            await WaitBookmarkAsync(() => closed.Task.IsCompleted ||
                window.OwnedWindows.OfType<DocumentChangesWindow>().Any(), token);
            if (window.OwnedWindows.OfType<DocumentChangesWindow>().FirstOrDefault() is { } changes)
            {
                BookmarkControl<Button>(changes, "DiscardDocumentChangesButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
        }
    }

    private static T BookmarkControl<T>(Control owner, string name) where T : Control =>
        owner.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing saved-view control: {name}");

    private static async Task WaitBookmarkAsync(Func<bool> predicate, CancellationToken token)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The saved-view workflow did not reach the expected state.");
            }
            await Task.Delay(20, token);
        }
    }

    private sealed class BookmarkDocumentPicker : IViewerDocumentFilePicker
    {
        internal string? SavePath { get; set; }
        internal string? OpenPath { get; set; }
        public Task<string?> SaveReviewAsync(Window owner, string suggestedPath) => Task.FromResult(SavePath);
        public Task<string?> OpenReviewAsync(Window owner) => Task.FromResult(OpenPath);
    }

    private const string SavedViewCameraStage = """
        #usda 1.0
        (
            defaultPrim = "Body"
            subLayers = [@source.usda@]
            startTimeCode = 1
            endTimeCode = 10
            timeCodesPerSecond = 24
        )
        def Camera "ReviewCamera"
        {
            float focalLength = 35
            float horizontalAperture = 36
            float verticalAperture = 24
            float horizontalApertureOffset = 2
            float2 clippingRange = (0.1, 100)
            double3 xformOp:translate.timeSamples = { 1: (0, 0, 8), 10: (1, 0, 8) }
            uniform token[] xformOpOrder = ["xformOp:translate"]
        }
        """;
}
