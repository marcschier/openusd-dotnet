// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer;

internal readonly record struct ViewerFrameRequest(
    ulong Revision,
    double TimeCode,
    CameraState Camera,
    RenderComplexity Complexity,
    ulong? SceneRevision)
{
    internal static ViewerFrameRequest Capture(StageRenderState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new ViewerFrameRequest(
            state.Revision,
            state.Time.TimeCode,
            state.Camera,
            state.RenderSettings.Complexity,
            SceneRevision: null);
    }
}

internal interface IViewerStormFrameAdapter
{
    OpenUsdStormChildDiagnostics Render(ViewerFrameRequest request);

    void RequestFrame(ViewerFrameRequest request);
}

internal sealed class OpenUsdStormFrameAdapter(OpenUsdStormChildSession session)
    : IViewerStormFrameAdapter
{
    public OpenUsdStormChildDiagnostics Render(ViewerFrameRequest request) =>
        session.Render(
            request.TimeCode,
            request.Camera,
            request.Revision,
            request.SceneRevision);

    public void RequestFrame(ViewerFrameRequest request) =>
        session.RequestFrame(
            request.TimeCode,
            request.Revision,
            request.Camera,
            request.SceneRevision);
}

internal interface IViewerSilkSessionAdapter
{
    OpenUsdSilkPage Sync(
        int width,
        int height,
        ViewerFrameRequest request);
}

internal sealed class OpenUsdSilkSessionAdapter(OpenUsdSilkSession session)
    : IViewerSilkSessionAdapter
{
    public OpenUsdSilkPage Sync(
        int width,
        int height,
        ViewerFrameRequest request) =>
        session.Sync(width, height, request.TimeCode, request.Camera, request.Complexity);
}

internal static class ViewerFrameAdapter
{
    internal static OpenUsdStormChildDiagnostics RenderStorm(
        IViewerStormFrameAdapter adapter,
        StageRenderState state)
        => RenderStorm(adapter, ViewerFrameRequest.Capture(state));

    internal static OpenUsdStormChildDiagnostics RenderStorm(
        IViewerStormFrameAdapter adapter,
        ViewerFrameRequest request)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return adapter.Render(request);
    }

    internal static void RequestStorm(
        IViewerStormFrameAdapter adapter,
        StageRenderState state)
        => RequestStorm(adapter, ViewerFrameRequest.Capture(state));

    internal static void RequestStorm(
        IViewerStormFrameAdapter adapter,
        ViewerFrameRequest request)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        adapter.RequestFrame(request);
    }

    internal static OpenUsdSilkPage SyncSilk(
        IViewerSilkSessionAdapter adapter,
        int width,
        int height,
        StageRenderState state)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        return adapter.Sync(width, height, ViewerFrameRequest.Capture(state));
    }
}
