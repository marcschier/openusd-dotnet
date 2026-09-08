// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Threading;
using OpenUsd.Editing;
using OpenUsd.Geom;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed partial class ViewerRenderCoordinator
{
    internal async Task RecallCameraBookmarkAsync(
        ViewerCameraBookmark bookmark, Action<StageRenderState> commitUi, Action<StageRenderState> restoreUi,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bookmark);
        ArgumentNullException.ThrowIfNull(commitUi);
        ArgumentNullException.ThrowIfNull(restoreUi);
        ThrowIfDisposed();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        CancellationToken token = lifetime.Token;
        await _stateGate.WaitAsync(token);
        StageRenderState original = CurrentState;
        bool backendTouched = false;
        bool committed = false;
        try
        {
            ThrowIfDisposed();
            if (_manager.ActiveBackend is null)
            {
                throw new NotSupportedException("No renderer is active to accept a saved view.");
            }
            if (!bookmark.HasMatchingAspect(original.Viewport))
            {
                throw new NotSupportedException(
                    $"This saved view requires aspect {bookmark.Viewport.Width}:{bookmark.Viewport.Height}. " +
                    "Resize to that aspect; exact recall does not adapt the projection.");
            }
            ulong serial = await Scheduler.InvokeAsync(stage => ValidateSavedView(stage, bookmark), token);
            StageRenderState next = original.WithTime(new StageTime(bookmark.TimeCode))
                .WithCamera(bookmark.Camera).AdvanceRevision();
            token.ThrowIfCancellationRequested();
            backendTouched = true;
            RenderBackendManagerResult accepted = await _manager.UpdateStateAsync(next, token);
            if (!accepted.IsSuccess)
            {
                throw new InvalidOperationException("The renderer refused the saved-view state.");
            }
            ulong current = await Scheduler.InvokeAsync(static stage => stage.ChangeSerial, token);
            if (current != serial)
            {
                throw new InvalidOperationException("The document changed while the saved view was prepared.");
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                commitUi(next);
                Volatile.Write(ref _currentState, next);
                committed = true;
                StateChanged?.Invoke(next);
            }, DispatcherPriority.Normal, token);
        }
        finally
        {
            try
            {
                if (backendTouched && !committed)
                {
                    await RestoreSavedViewAsync(original, restoreUi);
                }
            }
            finally
            {
                _stateGate.Release();
            }
        }
    }

    private async Task RestoreSavedViewAsync(StageRenderState original, Action<StageRenderState> restoreUi)
    {
        StageRenderState restored = original.AdvanceRevision().AdvanceRevision();
        RenderBackendManagerResult accepted = await _manager.UpdateStateAsync(restored, CancellationToken.None);
        if (!accepted.IsSuccess)
        {
            throw new InvalidOperationException(
                "Saved-view recall failed and the renderer refused restoration of the prior view.");
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            restoreUi(restored);
            Volatile.Write(ref _currentState, restored);
            StateChanged?.Invoke(restored);
        });
    }

    private static ulong ValidateSavedView(UsdStage stage, ViewerCameraBookmark bookmark)
    {
        UsdReviewSourceBinding binding = stage.CaptureReviewSourceBinding();
        string stamp = ViewerCameraBookmarkCatalog.CreateSourceStamp(binding);
        if (!string.Equals(stamp, bookmark.SourceStamp, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The saved view belongs to a different verified source context.");
        }
        using UsdLayer review = stage.GetUserReviewLayer();
        UsdLayerAuthoredSnapshot snapshot = review.CaptureAuthored([ViewerCameraBookmarkCatalog.Address]);
        ViewerCameraBookmarkCatalog.ValidateAccess(stage, review, snapshot, binding);
        ViewerCameraBookmark? current = ViewerCameraBookmarkCatalog.Decode(snapshot, stamp)
            .SingleOrDefault(item => item.Id == bookmark.Id);
        if (current is null || !current.HasSameContent(bookmark))
        {
            throw new InvalidOperationException("The saved view changed or was removed. Refresh the saved-view list.");
        }
        if (bookmark.StageCamera is { } expected)
        {
            if (!stage.HasPrim(expected.PrimPath))
            {
                throw new InvalidOperationException("The saved authored camera is missing; the current view was kept.");
            }
            UsdPrim prim = stage.GetPrim(expected.PrimPath);
            if (!prim.IsActive() || !prim.IsDefined() || !UsdGeomCamera.TryWrap(prim, out UsdGeomCamera camera))
            {
                throw new InvalidOperationException(
                    "The saved authored camera is inactive or no longer a camera; the current view was kept.");
            }
            ViewerStageCameraSnapshot sample = ViewerStageCameraSnapshotFactory.Create(
                expected.PrimPath, expected.TimeCode, camera.Xformable.GetWorldTransform(expected.TimeCode),
                camera.GetState(expected.TimeCode));
            if (sample != expected)
            {
                throw new InvalidOperationException(
                    "The authored camera sample changed. Save a new view; exact recall will not substitute it.");
            }
        }
        return stage.ChangeSerial;
    }
}
