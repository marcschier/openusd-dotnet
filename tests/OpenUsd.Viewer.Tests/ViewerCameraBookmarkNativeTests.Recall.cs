// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Controls;
using Avalonia.Threading;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerCameraBookmarkNativeTests
{
    [Test]
    public async Task ExactRecallUsesTheRealRendererAndKeepsRefusedViewsUnchanged()
    {
        RequireNativeJourney();
        using ViewerCameraBookmarkFixture files = await ViewerCameraBookmarkFixture.CreateAsync();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Program.BuildAvaloniaApp().SetupWithoutStarting();
                _ = Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        await ExerciseRecallAsync(files, lifetime.Token);
                        await ExerciseRecallFailureAsync(files, lifetime.Token);
                        await ExerciseSavedViewsUiAsync(files, lifetime.Token);
                        complete.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        complete.TrySetException(exception);
                    }
                    finally
                    {
                        ViewerStartupOptions.Initialize([]);
                        lifetime.Cancel();
                    }
                });
                Dispatcher.UIThread.MainLoop(lifetime.Token);
            }
            catch (Exception exception)
            {
                complete.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "Viewer saved-view native workflow"
        };
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }
        thread.Start();
        bool stopped;
        try
        {
            await complete.Task.WaitAsync(TimeSpan.FromSeconds(130));
        }
        finally
        {
            lifetime.Cancel();
            stopped = thread.Join(TimeSpan.FromSeconds(5));
        }
        await Assert.That(stopped).IsTrue();
        await files.AssertOriginalsUnchangedAsync();
    }

    private static async Task ExerciseRecallAsync(ViewerCameraBookmarkFixture files, CancellationToken token)
    {
        var viewport = new RendererSwitchingViewport();
        var window = new Window { Width = 480, Height = 360, Content = viewport };
        try
        {
            window.Show();
            await using ViewerPreparedDocument prepared =
                await ViewerPreparedDocument.OpenSourceAsync(files.SourcePath, null, token);
            await using ViewerRenderCoordinator coordinator = await ViewerRenderCoordinator.OpenAsync(
                prepared.Scheduler,
                (scheduler, source) => new AvaloniaViewerRenderBackendHost(viewport, scheduler, source,
                    Environment.GetEnvironmentVariable("OPENUSD_PLUGIN_PATH")!, _ => { }),
                RenderBackendKind.D3D12, RenderSettings.PresentationDefault, token);
            prepared.TransferOwnership();
            await using var editor = new ViewerAuthoredEditController(coordinator.Scheduler, prepared.SourceBinding);
            var catalog = new ViewerCameraBookmarkCatalog(editor);
            ViewerCameraBookmarkCapture empty = await catalog.ReadAsync(token);
            window.UpdateLayout();
            ViewportDimensions dimensions = ViewportPixelMath.ToPixels(
                viewport.Bounds.Width, viewport.Bounds.Height, window.RenderScaling);
            var navigation = new ViewerCameraNavigationController(dimensions);
            navigation.ResetToExplicitPose();
            navigation.Orbit(-0.3f, 0.1f);
            ViewerCameraBookmark bookmark = ViewerCameraBookmark.CreateFree(
                Guid.NewGuid(), "Hero", empty.SourceStamp, 2.25, dimensions, navigation.State);
            await catalog.ApplyAsync(empty, [bookmark], "Add saved view", token);
            await coordinator.UpdateStateAsync(coordinator.CurrentState.WithViewport(dimensions)
                .WithTime(new StageTime(bookmark.TimeCode)).WithCamera(bookmark.Camera).AdvanceRevision(), token);
            ViewerFrameCaptureResult expected = await CaptureRecallFrameAsync(coordinator, token);
            navigation.Orbit(0.8f, -0.3f);
            await coordinator.UpdateStateAsync(coordinator.CurrentState.WithTime(new StageTime(7))
                .WithCamera(navigation.Camera).AdvanceRevision(), token);
            ViewerFrameCaptureResult different = await CaptureRecallFrameAsync(coordinator, token);
            await Assert.That(different.Rgba.Span.SequenceEqual(expected.Rgba.Span)).IsFalse();
            int history = editor.UndoDepth;
            int commits = 0;
            await coordinator.RecallCameraBookmarkAsync(bookmark, _ => commits++, _ => { }, token);
            await Assert.That(commits).IsEqualTo(1);
            await Assert.That(coordinator.CurrentState.Camera).IsEqualTo(bookmark.Camera);
            await Assert.That(coordinator.CurrentState.Time.TimeCode).IsEqualTo(bookmark.TimeCode);
            ViewerFrameCaptureResult actual = await CaptureRecallFrameAsync(coordinator, token);
            await Assert.That(actual.Rgba.Span.SequenceEqual(expected.Rgba.Span)).IsTrue();
            await Assert.That(editor.UndoDepth).IsEqualTo(history);

            StageRenderState before = coordinator.CurrentState;
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.That(async () => await coordinator.RecallCameraBookmarkAsync(
                bookmark, _ => commits++, _ => { }, cancelled.Token)).Throws<OperationCanceledException>();
            await Assert.That(coordinator.CurrentState).IsSameReferenceAs(before);
            await Assert.That(commits).IsEqualTo(1);
            ViewerCameraBookmarkCapture current = await catalog.ReadAsync(token);
            await catalog.ApplyAsync(current, [], "Remove saved view", token);
            await Assert.That(async () => await coordinator.RecallCameraBookmarkAsync(
                bookmark, _ => commits++, _ => { }, token)).Throws<InvalidOperationException>();
            await Assert.That(coordinator.CurrentState.Camera).IsEqualTo(before.Camera);
            await Assert.That(coordinator.CurrentState.Time).IsEqualTo(before.Time);
            await Assert.That(commits).IsEqualTo(1);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task<ViewerFrameCaptureResult> CaptureRecallFrameAsync(
        ViewerRenderCoordinator coordinator, CancellationToken token)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            ManagedRenderFrameResult frame = await coordinator.RenderAsync(token);
            if (frame.Frame?.Status == RenderFrameStatus.Rendered &&
                frame.Frame.StateRevision == coordinator.CurrentState.Revision &&
                coordinator.PickingBackend is IViewerRenderedPickStateSource rendered &&
                rendered.LastRenderedPickState?.State == coordinator.CurrentState)
            {
                break;
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The saved-view renderer did not present the requested frame.");
            }
            await Task.Delay(20, token);
        }
        ViewportDimensions size = coordinator.CurrentState.Viewport;
        return await coordinator.CaptureFrameAsync(size.Width, size.Height, token);
    }
}
