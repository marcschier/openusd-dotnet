// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Viewer;

internal sealed class ViewerCameraBookmark : IUsdDetachedResult
{
    private ViewerCameraBookmark(
        Guid id, string name, string sourceStamp, double timeCode, ViewportDimensions viewport,
        ViewerCameraNavigationState freeCamera, ViewerStageCameraSnapshot? stageCamera = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A saved view requires a stable nonempty identity.", nameof(id));
        }
        Name = ValidateName(name);
        if (sourceStamp.Length != 64 || sourceStamp.Any(static value => !char.IsAsciiHexDigit(value)))
        {
            throw new ArgumentException("A saved view requires a verified source-context stamp.", nameof(sourceStamp));
        }
        if (!double.IsFinite(timeCode) || viewport.Width <= 0 || viewport.Height <= 0)
        {
            throw new ArgumentException("Saved view time and capture dimensions must be finite and valid.");
        }
        if (stageCamera is { } sample)
        {
            UsdPath.ValidateAbsolutePrimPath(sample.PrimPath);
            if (ViewerCameraBookmarkCodec.Utf8.GetByteCount(sample.PrimPath) > 1024 ||
                sample != ViewerStageCameraSnapshotFactory.Create(
                    sample.PrimPath, timeCode, sample.LocalToWorld, sample.Optics))
            {
                throw new ArgumentException("The authored saved-view sample is invalid or inconsistent.");
            }
            Camera = StageCameraProjectionMath.CreateCameraState(sample.WorldToView, sample.Optics, viewport);
        }
        else if (freeCamera.IsAutomatic)
        {
            throw new NotSupportedException(
                "Automatic is not an exact saved view. Use Frame Selected or the explicit free-camera pose first.");
        }
        else if (freeCamera.AspectRatio != ViewerCameraNavigationMath.AspectRatio(viewport))
        {
            throw new ArgumentException("The logical camera and captured viewport aspect differ.");
        }
        else
        {
            Camera = freeCamera.CreateCameraState();
        }
        Id = id;
        SourceStamp = sourceStamp;
        TimeCode = timeCode;
        Viewport = viewport;
        FreeCamera = freeCamera;
        StageCamera = stageCamera;
    }

    internal Guid Id { get; }
    internal string Name { get; }
    internal string SourceStamp { get; }
    internal double TimeCode { get; }
    internal ViewportDimensions Viewport { get; }
    internal ViewerCameraNavigationState FreeCamera { get; }
    internal ViewerStageCameraSnapshot? StageCamera { get; }
    internal CameraState Camera { get; }

    internal static ViewerCameraBookmark CreateFree(
        Guid id, string name, string sourceStamp, double timeCode, ViewportDimensions viewport,
        ViewerCameraNavigationState camera) => new(id, name, sourceStamp, timeCode, viewport, camera);

    internal static ViewerCameraBookmark CreateStage(
        Guid id, string name, string sourceStamp, ViewportDimensions viewport, ViewerStageCameraSnapshot camera) =>
        new(id, name, sourceStamp, camera.TimeCode, viewport, default, camera);

    internal ViewerCameraBookmark Rename(string name) => StageCamera is { } stage
        ? CreateStage(Id, name, SourceStamp, Viewport, stage)
        : CreateFree(Id, name, SourceStamp, TimeCode, Viewport, FreeCamera);

    internal bool HasMatchingAspect(ViewportDimensions viewport) =>
        viewport.Width > 0 && viewport.Height > 0 &&
        (long)Viewport.Width * viewport.Height == (long)viewport.Width * Viewport.Height;

    internal bool HasSameContent(ViewerCameraBookmark other) =>
        Id == other.Id && Name == other.Name && SourceStamp == other.SourceStamp && TimeCode == other.TimeCode &&
        Viewport == other.Viewport && Camera == other.Camera && FreeCamera == other.FreeCamera &&
        StageCamera == other.StageCamera;

    internal static string ValidateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string trimmed = name.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 128 || trimmed.Any(char.IsControl) ||
            ViewerCameraBookmarkCodec.Utf8.GetByteCount(trimmed) > 128)
        {
            throw new ArgumentException("Use a saved-view name of 1–128 UTF-8 bytes without control characters.",
                nameof(name));
        }
        return trimmed;
    }
}
