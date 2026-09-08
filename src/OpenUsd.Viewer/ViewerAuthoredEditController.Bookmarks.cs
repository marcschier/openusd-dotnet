// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal sealed partial class ViewerAuthoredEditController
{
    internal UsdReviewSourceBinding CameraBookmarkSourceBinding => _sourceBinding ??
        throw new NotSupportedException(
            "Saved views require a verified source with Save Review support; session-only storage is not offered.");

    private void ValidateBookmarkCapture(UsdStage stage, UsdLayer layer, UsdLayerAuthoredSnapshot snapshot)
    {
        if (ViewerCameraBookmarkCatalog.UsesReservedNamespace(snapshot.Addresses))
        {
            ViewerCameraBookmarkCatalog.ValidateAccess(stage, layer, snapshot, CameraBookmarkSourceBinding);
        }
    }
}
