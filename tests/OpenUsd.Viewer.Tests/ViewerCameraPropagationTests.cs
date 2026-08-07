// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerCameraPropagationTests
{
    [Test]
    public async Task ExplicitIdentityMatricesReachStormRenderAndRequestBoundaries()
    {
        var camera = new CameraState(Matrix4x4.Identity, Matrix4x4.Identity);
        StageRenderState state = StageRenderState.Create(new StageIdentity("identity.usda"))
            .WithCamera(camera)
            .WithTime(new StageTime(12));
        var adapter = new RecordingStormFrameAdapter();

        _ = ViewerFrameAdapter.RenderStorm(adapter, state);
        ViewerFrameAdapter.RequestStorm(adapter, state);

        await Assert.That(adapter.RenderRequests.Count).IsEqualTo(1);
        await Assert.That(adapter.Requests.Count).IsEqualTo(1);
        await Assert.That(adapter.RenderRequests[0].Revision).IsEqualTo(state.Revision);
        await Assert.That(adapter.RenderRequests[0].TimeCode).IsEqualTo(12);
        await Assert.That(adapter.RenderRequests[0].Camera).IsEqualTo(camera);
        await Assert.That(adapter.RenderRequests[0].Camera.Mode)
            .IsEqualTo(CameraMode.Matrices);
        await Assert.That(adapter.Requests[0]).IsEqualTo(adapter.RenderRequests[0]);
    }

    [Test]
    public async Task NonIdentityMatricesReachSilkSessionBoundaryExactly()
    {
        var camera = new CameraState(
            new Matrix4x4(
                1, 2, 3, 4,
                5, 6, 7, 8,
                9, 10, 11, 12,
                13, 14, 15, 16),
            new Matrix4x4(
                17, 18, 19, 20,
                21, 22, 23, 24,
                25, 26, 27, 28,
                29, 30, 31, 32));
        StageRenderState state = StageRenderState.Create(new StageIdentity("matrices.usda"))
            .WithCamera(camera)
            .WithTime(new StageTime(24));
        var adapter = new RecordingSilkSessionAdapter();

        await Assert.That(() => ViewerFrameAdapter.SyncSilk(adapter, 800, 600, state))
            .Throws<SilkBoundaryCapturedException>();

        await Assert.That(adapter.Width).IsEqualTo(800);
        await Assert.That(adapter.Height).IsEqualTo(600);
        await Assert.That(adapter.Request.Revision).IsEqualTo(state.Revision);
        await Assert.That(adapter.Request.TimeCode).IsEqualTo(24);
        await Assert.That(adapter.Request.Camera).IsEqualTo(camera);
        await Assert.That(adapter.Request.Camera.View).IsEqualTo(camera.View);
        await Assert.That(adapter.Request.Camera.Projection).IsEqualTo(camera.Projection);
    }


    [Test]
    [Arguments(RenderDrawMode.WireframeOnSurface)]
    [Arguments(RenderDrawMode.GeomOnly)]
    [Arguments(RenderDrawMode.GeomFlat)]
    [Arguments(RenderDrawMode.GeomSmooth)]
    [Arguments(RenderDrawMode.HiddenSurfaceWireframe)]
    public async Task DrawModeReachesSilkSessionBoundary(RenderDrawMode drawMode)
    {
        StageRenderState state = StageRenderState.Create(new StageIdentity("draw-mode.usda"))
            .WithDisplay(SceneDisplayState.Default with { DrawMode = drawMode });
        var adapter = new RecordingSilkSessionAdapter();

        await Assert.That(() => ViewerFrameAdapter.SyncSilk(adapter, 640, 480, state))
            .Throws<SilkBoundaryCapturedException>();

        await Assert.That(adapter.Request.DrawMode).IsEqualTo(drawMode);
    }

    [Test]
    public async Task AutomaticCameraRemainsAutomaticAtBothNativeBoundaries()
    {
        StageRenderState state = StageRenderState.Create(new StageIdentity("automatic.usda"))
            .WithTime(new StageTime(3));
        var storm = new RecordingStormFrameAdapter();
        var silk = new RecordingSilkSessionAdapter();

        ViewerFrameAdapter.RequestStorm(storm, state);
        await Assert.That(() => ViewerFrameAdapter.SyncSilk(silk, 320, 200, state))
            .Throws<SilkBoundaryCapturedException>();

        await Assert.That(storm.Requests[0].Camera).IsEqualTo(CameraState.Default);
        await Assert.That(storm.Requests[0].Camera.Mode).IsEqualTo(CameraMode.Automatic);
        await Assert.That(silk.Request.Camera).IsEqualTo(CameraState.Default);
        await Assert.That(silk.Request.Camera.Mode).IsEqualTo(CameraMode.Automatic);
    }

    [Test]
    public async Task StateUpdatesAndCompatibilityHostDoNotSubstituteDefaultCamera()
    {
        var firstCamera = new CameraState(
            Matrix4x4.CreateTranslation(1, 2, 3),
            Matrix4x4.CreateOrthographic(8, 6, 0.1f, 100));
        var secondCamera = new CameraState(
            Matrix4x4.CreateTranslation(4, 5, 6),
            Matrix4x4.CreatePerspectiveFieldOfView(0.75f, 1.5f, 0.1f, 100));
        StageRenderState first = StageRenderState.Create(new StageIdentity("updates.usda"))
            .WithCamera(firstCamera);
        StageRenderState second = first.WithCamera(secondCamera);
        var storm = new RecordingStormFrameAdapter();
        var compatibility = new StormViewportControl();

        ViewerFrameAdapter.RequestStorm(storm, first);
        ViewerFrameAdapter.RequestStorm(storm, second);
        compatibility.UpdateRenderState(second);

        await Assert.That(storm.Requests.Count).IsEqualTo(2);
        await Assert.That(storm.Requests[0].Camera).IsEqualTo(firstCamera);
        await Assert.That(storm.Requests[1].Camera).IsEqualTo(secondCamera);
        await Assert.That(storm.Requests[1].Revision).IsEqualTo(second.Revision);
        await Assert.That(storm.Requests[1].Camera).IsNotEqualTo(CameraState.Default);
        await Assert.That(compatibility.CurrentRenderState).IsSameReferenceAs(second);
        await Assert.That(compatibility.CurrentRenderState.Camera).IsEqualTo(secondCamera);
    }

    private sealed class RecordingStormFrameAdapter : IViewerStormFrameAdapter
    {
        internal List<ViewerFrameRequest> RenderRequests { get; } = [];

        internal List<ViewerFrameRequest> Requests { get; } = [];

        public OpenUsdStormChildDiagnostics Render(ViewerFrameRequest request)
        {
            RenderRequests.Add(request);
            return default;
        }

        public void RequestFrame(ViewerFrameRequest request) => Requests.Add(request);
    }

    private sealed class RecordingSilkSessionAdapter : IViewerSilkSessionAdapter
    {
        internal int Width { get; private set; }

        internal int Height { get; private set; }

        internal ViewerFrameRequest Request { get; private set; }

        public OpenUsdSilkPage Sync(
            int width,
            int height,
            ViewerFrameRequest request)
        {
            Width = width;
            Height = height;
            Request = request;
            throw new SilkBoundaryCapturedException();
        }
    }

    private sealed class SilkBoundaryCapturedException : Exception;
}
