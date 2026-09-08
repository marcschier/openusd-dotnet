// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal readonly record struct ViewerCameraBookmarkUiState(
    ViewerCameraNavigationState FreeCamera, ViewportDimensions Viewport,
    ViewerStageCameraSnapshot? StageCamera, bool ForcesAutomatic);

internal readonly record struct ViewerCameraBookmarkAvailability(
    bool CanEdit, bool CanCapture, bool CanRecall, string SourceDescription, string CaptureReason);
