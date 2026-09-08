// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;
using OpenUsd.Geom;

namespace OpenUsd.Viewer.Tests;

public sealed partial class ViewerCameraBookmarkNativeTests
{
    private static async Task ExerciseRecallFailureAsync(
        ViewerCameraBookmarkFixture files, CancellationToken token)
    {
        await using ViewerPreparedDocument prepared =
            await ViewerPreparedDocument.OpenSourceAsync(files.SourcePath, null, token);
        var host = new RecallFailureHost();
        await using ViewerRenderCoordinator coordinator = await ViewerRenderCoordinator.OpenAsync(
            prepared.Scheduler, (_, _) => host, RenderBackendKind.D3D12, RenderSettings.PresentationDefault, token);
        prepared.TransferOwnership();
        await using var editor = new ViewerAuthoredEditController(coordinator.Scheduler, prepared.SourceBinding);
        var catalog = new ViewerCameraBookmarkCatalog(editor);
        ViewerCameraBookmarkCapture empty = await catalog.ReadAsync(token);
        var viewport = new ViewportDimensions(800, 600);
        ViewerCameraBookmark bookmark = ViewerCameraBookmark.CreateFree(
            Guid.NewGuid(), "Refusal", empty.SourceStamp, 9, viewport,
            ViewerCameraNavigationState.CreateLegacy(false, 800f / 600));
        await catalog.ApplyAsync(empty, [bookmark], "Add saved view", token);
        await coordinator.UpdateStateAsync(coordinator.CurrentState.WithViewport(viewport)
            .WithTime(new StageTime(1)).AdvanceRevision(), token);
        StageRenderState original = coordinator.CurrentState;
        int commits = 0;
        int restores = 0;
        bool uiChanged = false;
        host.OnUpdate = state =>
        {
            if (state.Time.TimeCode == 9)
            {
                host.OnUpdate = null;
                throw new InvalidOperationException("Backend refused after a partial state update.");
            }
        };
        await Assert.That(async () => await coordinator.RecallCameraBookmarkAsync(
            bookmark, _ => commits++, _ => restores++, token)).Throws<InvalidOperationException>();
        await Assert.That(commits).IsEqualTo(0);
        await Assert.That(restores).IsEqualTo(1);
        await Assert.That(coordinator.CurrentState.Camera).IsEqualTo(original.Camera);
        await Assert.That(coordinator.CurrentState.Time).IsEqualTo(original.Time);
        await Assert.That(host.Current!.CurrentState.Camera).IsEqualTo(original.Camera);
        await Assert.That(host.Current.CurrentState.Time).IsEqualTo(original.Time);

        using var cancelled = new CancellationTokenSource();
        host.OnUpdate = state =>
        {
            if (state.Time.TimeCode == 9)
            {
                host.OnUpdate = null;
                cancelled.Cancel();
            }
        };
        await Assert.That(async () => await coordinator.RecallCameraBookmarkAsync(
            bookmark, _ => commits++, _ => restores++, cancelled.Token)).Throws<OperationCanceledException>();
        await Assert.That(commits).IsEqualTo(0);
        await Assert.That(restores).IsEqualTo(2);
        await Assert.That(coordinator.CurrentState.Camera).IsEqualTo(original.Camera);
        await Assert.That(host.Current.CurrentState.Time).IsEqualTo(original.Time);

        await Assert.That(async () => await coordinator.RecallCameraBookmarkAsync(bookmark,
            _ =>
            {
                uiChanged = true;
                throw new InvalidOperationException("UI commit failed before publication.");
            },
            _ => uiChanged = false, token)).Throws<InvalidOperationException>();
        await Assert.That(uiChanged).IsFalse();
        await Assert.That(coordinator.CurrentState.Camera).IsEqualTo(original.Camera);
        await Assert.That(coordinator.CurrentState.Time).IsEqualTo(original.Time);
        await Assert.That(host.Current.CurrentState.Camera).IsEqualTo(original.Camera);

        var optics = new UsdGeomCameraState(
            UsdGeomCameraProjection.Perspective, -1, 1, -1, 1, 0.1, 100, 50, 36, 24, 0, 0, 1, 0);
        ViewerCameraBookmark missing = ViewerCameraBookmark.CreateStage(
            Guid.NewGuid(), "Missing", empty.SourceStamp, viewport,
            ViewerStageCameraSnapshotFactory.Create("/MissingCamera", 9,
                UsdMatrix4d.CreateTranslation(0, 0, 8), optics));
        await catalog.ApplyAsync(await catalog.ReadAsync(token), [bookmark, missing], "Add stale view fixture", token);
        await Assert.That(async () => await coordinator.RecallCameraBookmarkAsync(
            missing, _ => commits++, _ => restores++, token)).Throws<InvalidOperationException>();
        await Assert.That(coordinator.CurrentState.Camera).IsEqualTo(original.Camera);
        await Assert.That(coordinator.CurrentState.Time).IsEqualTo(original.Time);
        ViewerCameraBookmark foreign = ViewerCameraBookmark.CreateFree(
            bookmark.Id, bookmark.Name, new string('B', 64), bookmark.TimeCode, viewport, bookmark.FreeCamera);
        await Assert.That(async () => await coordinator.RecallCameraBookmarkAsync(
            foreign, _ => commits++, _ => restores++, token)).Throws<InvalidOperationException>();
        await Assert.That(coordinator.CurrentState.Camera).IsEqualTo(original.Camera);
        await Assert.That(coordinator.CurrentState.Time).IsEqualTo(original.Time);
    }

    private sealed class RecallFailureHost : IViewerRenderBackendHost
    {
        internal Action<StageRenderState>? OnUpdate { get; set; }
        internal RecallFailureSession? Current { get; private set; }

        public ValueTask<RenderBackendProbeResult> ProbeAsync(
            RenderBackendKind kind, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(RenderBackendProbeResult.Available());
        }

        public ValueTask<IViewerRenderBackendSession> AttachAsync(
            RenderBackendKind kind, StageRenderState initialState, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Current = new RecallFailureSession(this, initialState);
            return ValueTask.FromResult<IViewerRenderBackendSession>(Current);
        }
    }

    private sealed class RecallFailureSession(
        RecallFailureHost host, StageRenderState initialState) : IViewerRenderBackendSession
    {
        public RenderBackendDiagnostics Diagnostics => RenderBackendDiagnostics.Empty;
        public StageRenderState CurrentState { get; private set; } = initialState;
        public ValueTask ActivateAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DeactivateAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ResizeAsync(ViewportDimensions viewport, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask UpdateStateAsync(StageRenderState state, CancellationToken cancellationToken)
        {
            CurrentState = state;
            host.OnUpdate?.Invoke(state);
            return ValueTask.CompletedTask;
        }

        public ValueTask<RenderFrameResult> RenderAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(RenderFrameResult.Rendered(CurrentState.Revision, RenderFrameStatistics.Empty));
    }
}
