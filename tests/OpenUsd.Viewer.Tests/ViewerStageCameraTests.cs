// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Runtime.CompilerServices;
using OpenUsd.Geom;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerStageCameraTests
{
    private const float FloatTolerance = 1e-5f;
    private const double DoubleTolerance = 1e-12d;
    private const int AllocationIterations = 1000;
    private static Matrix4x4 _projectionSink;

    [Test]
    public async Task DetachedSnapshotUsesComposedWorldTransformAndDoubleInverse()
    {
        UsdMatrix4d localToWorld = UsdMatrix4d.CreateTranslation(5d, 8d, 17d);

        ViewerStageCameraSnapshot snapshot = ViewerStageCameraSnapshotFactory.Create(
            "/World/Rig/Camera",
            12.5d,
            localToWorld,
            CreateOptics(
                UsdGeomCameraProjection.Perspective,
                focalLength: 50d,
                horizontalAperture: 24d,
                verticalAperture: 18d,
                near: 0.1d,
                far: 1000d));
        IUsdDetachedResult detached = snapshot;

        await Assert.That(detached.GetType())
            .IsEqualTo(typeof(ViewerStageCameraSnapshot));
        await Assert.That(snapshot.PrimPath).IsEqualTo("/World/Rig/Camera");
        await Assert.That(snapshot.TimeCode).IsEqualTo(12.5d);
        await Assert.That(snapshot.LocalToWorld).IsEqualTo(localToWorld);
        await Assert.That(snapshot.WorldToView.ExtractTranslation())
            .IsEqualTo(new UsdVec3d(-5d, -8d, -17d));
        await Assert.That(snapshot.Optics.FocalLength).IsEqualTo(50d);
        await Assert.That(snapshot.Optics.HorizontalAperture).IsEqualTo(24d);
        await Assert.That(snapshot.Optics.VerticalAperture).IsEqualTo(18d);
        await Assert.That(snapshot.Optics.ClippingNear).IsEqualTo(0.1d);
        await Assert.That(snapshot.Optics.ClippingFar).IsEqualTo(1000d);
    }

    [Test]
    public async Task SnapshotFactoryRejectsSingularWorldTransform()
    {
        UsdMatrix4d singular = default;

        await Assert.That(() => ViewerStageCameraSnapshotFactory.Create(
            "/World/Camera",
            0d,
            singular,
            CreateOptics(UsdGeomCameraProjection.Perspective)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ProjectionMatricesMatchSymmetricGfOpenGlFormulas()
    {
        ViewerStageCameraSnapshot perspective = CreateSnapshot(
            UsdGeomCameraProjection.Perspective,
            focalLength: 50d,
            horizontalAperture: 20d,
            verticalAperture: 10d,
            near: 0.1d,
            far: 1000d);
        ViewerStageCameraSnapshot orthographic = CreateSnapshot(
            UsdGeomCameraProjection.Orthographic,
            focalLength: 50d,
            horizontalAperture: 20d,
            verticalAperture: 10d,
            near: 0.1d,
            far: 1000d);
        var viewport = new ViewportDimensions(2000, 1000);

        Matrix4x4 perspectiveMatrix =
            ViewerStageCameraProjectionMath.CreateProjectionMatrix(
                perspective,
                viewport);
        Matrix4x4 orthographicMatrix =
            ViewerStageCameraProjectionMath.CreateProjectionMatrix(
                orthographic,
                viewport);

        double depthRange = 999.9d;
        Matrix4x4 expectedPerspective = new(
            5f, 0f, 0f, 0f,
            0f, 10f, 0f, 0f,
            0f, 0f, (float)(-(1000.1d / depthRange)), -1f,
            0f, 0f, (float)(-(200d / depthRange)), 0f);
        Matrix4x4 expectedOrthographic = new(
            1f, 0f, 0f, 0f,
            0f, 2f, 0f, 0f,
            0f, 0f, (float)(-2d / depthRange), 0f,
            0f, 0f, (float)(-(1000.1d / depthRange)), 1f);

        await Assert.That(NearlyEqual(
            perspectiveMatrix,
            expectedPerspective)).IsTrue();
        await Assert.That(NearlyEqual(
            orthographicMatrix,
            expectedOrthographic)).IsTrue();
    }

    [Test]
    public async Task OffAxisMatricesMatchGeneralGfOpenGlFormulasAndNdcCorners()
    {
        UsdGeomCameraState perspectiveOptics = CreateOpticsFromWindow(
            UsdGeomCameraProjection.Perspective,
            left: -0.2d,
            right: 0.3d,
            bottom: -0.1d,
            top: 0.15d,
            near: 0.1d,
            far: 100d);
        UsdGeomCameraState orthographicOptics = CreateOpticsFromWindow(
            UsdGeomCameraProjection.Orthographic,
            left: -0.2d,
            right: 0.3d,
            bottom: -0.1d,
            top: 0.15d,
            near: -10d,
            far: 100d);
        ViewerStageCameraSnapshot perspective = CreateSnapshot(perspectiveOptics);
        ViewerStageCameraSnapshot orthographic = CreateSnapshot(orthographicOptics);
        var viewport = new ViewportDimensions(2000, 1000);

        Matrix4x4 perspectiveMatrix =
            ViewerStageCameraProjectionMath.CreateProjectionMatrix(
                perspective,
                viewport);
        Matrix4x4 orthographicMatrix =
            ViewerStageCameraProjectionMath.CreateProjectionMatrix(
                orthographic,
                viewport);
        Matrix4x4 expectedPerspective = new(
            4f, 0f, 0f, 0f,
            0f, 8f, 0f, 0f,
            0.2f, 0.2f, (float)(-100.1d / 99.9d), -1f,
            0f, 0f, (float)(-20d / 99.9d), 0f);
        Matrix4x4 expectedOrthographic = new(
            4f, 0f, 0f, 0f,
            0f, 8f, 0f, 0f,
            0f, 0f, (float)(-2d / 110d), 0f,
            -0.2f, -0.2f, (float)(-90d / 110d), 1f);

        await Assert.That(NearlyEqual(perspectiveMatrix, expectedPerspective)).IsTrue();
        await Assert.That(NearlyEqual(orthographicMatrix, expectedOrthographic)).IsTrue();
        await AssertNdcCorners(
            perspectiveMatrix,
            perspectiveOptics,
            perspective: true);
        await AssertNdcCorners(
            orthographicMatrix,
            orthographicOptics,
            perspective: false);
    }

    [Test]
    public async Task WindowFitWidensOrExpandsWithoutCroppingAndPreservesCenter()
    {
        ViewerStageCameraApertureWindow authored =
            ViewerStageCameraProjectionMath.ConformWindow(
                -10d,
                14d,
                -10d,
                8d,
                new ViewportDimensions(800, 600));
        ViewerStageCameraApertureWindow wider =
            ViewerStageCameraProjectionMath.ConformWindow(
                -10d,
                14d,
                -10d,
                8d,
                new ViewportDimensions(1920, 1080));
        ViewerStageCameraApertureWindow narrower =
            ViewerStageCameraProjectionMath.ConformWindow(
                -10d,
                14d,
                -10d,
                8d,
                new ViewportDimensions(900, 1600));

        await Assert.That(NearlyEqual(authored.Width, 24d)).IsTrue();
        await Assert.That(NearlyEqual(authored.Height, 18d)).IsTrue();
        await Assert.That(NearlyEqual(authored.CenterX, 2d)).IsTrue();
        await Assert.That(NearlyEqual(authored.CenterY, -1d)).IsTrue();
        await Assert.That(NearlyEqual(wider.Width, 32d)).IsTrue();
        await Assert.That(NearlyEqual(wider.Height, 18d)).IsTrue();
        await Assert.That(NearlyEqual(wider.CenterX, 2d)).IsTrue();
        await Assert.That(NearlyEqual(wider.CenterY, -1d)).IsTrue();
        await Assert.That(NearlyEqual(narrower.Width, 24d)).IsTrue();
        await Assert.That(NearlyEqual(
            narrower.Height,
            24d / (900d / 1600d))).IsTrue();
        await Assert.That(NearlyEqual(narrower.CenterX, 2d)).IsTrue();
        await Assert.That(NearlyEqual(narrower.CenterY, -1d)).IsTrue();
    }

    [Test]
    public async Task SnapshotValidationRejectsInvalidLensApertureAndFarPlane()
    {
        await Assert.That(() => CreateSnapshot(
            UsdGeomCameraProjection.Perspective,
            focalLength: 0d)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateSnapshot(
            UsdGeomCameraProjection.Perspective,
            horizontalAperture: double.NaN))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateSnapshot(
            UsdGeomCameraProjection.Perspective,
            verticalAperture: -1d)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateSnapshot(
            UsdGeomCameraProjection.Perspective,
            near: 10d,
            far: 10d)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateSnapshot(
            UsdGeomCameraProjection.Orthographic,
            near: double.NaN,
            far: 10d)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateSnapshot(
            (UsdGeomCameraProjection)99)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(0d)]
    [Arguments(-10d)]
    public async Task PerspectiveRejectsZeroOrNegativeNearPlane(double near)
    {
        await Assert.That(() => CreateSnapshot(
            UsdGeomCameraProjection.Perspective,
            near: near,
            far: 100d)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(0d)]
    [Arguments(-10d)]
    public async Task OrthographicAcceptsFiniteZeroOrNegativeNearPlane(double near)
    {
        ViewerStageCameraSnapshot snapshot = CreateSnapshot(
            UsdGeomCameraProjection.Orthographic,
            near: near,
            far: 100d);

        Matrix4x4 projection =
            ViewerStageCameraProjectionMath.CreateProjectionMatrix(
                snapshot,
                new ViewportDimensions(800, 600));

        await Assert.That(snapshot.Optics.ClippingNear).IsEqualTo(near);
        await Assert.That(float.IsFinite(projection.M33)).IsTrue();
        await Assert.That(float.IsFinite(projection.M43)).IsTrue();
    }

    [Test]
    public async Task OrthographicAcceptsZeroFocalWhilePerspectiveAndNegativeReject()
    {
        ViewerStageCameraSnapshot zeroFocal = CreateSnapshot(
            UsdGeomCameraProjection.Orthographic,
            focalLength: 0d);
        ViewerStageCameraSnapshot positiveFocal = CreateSnapshot(
            UsdGeomCameraProjection.Orthographic,
            focalLength: 50d);
        var viewport = new ViewportDimensions(800, 600);

        Matrix4x4 zeroProjection =
            ViewerStageCameraProjectionMath.CreateProjectionMatrix(
                zeroFocal,
                viewport);
        Matrix4x4 positiveProjection =
            ViewerStageCameraProjectionMath.CreateProjectionMatrix(
                positiveFocal,
                viewport);

        await Assert.That(zeroFocal.Optics.FocalLength).IsEqualTo(0d);
        await Assert.That(NearlyEqual(zeroProjection, positiveProjection)).IsTrue();
        await Assert.That(() => CreateSnapshot(
            UsdGeomCameraProjection.Perspective,
            focalLength: 0d)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateSnapshot(
            UsdGeomCameraProjection.Orthographic,
            focalLength: -1d)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(UsdGeomCameraProjection.Perspective, 0.1d, 1000d)]
    [Arguments(UsdGeomCameraProjection.Orthographic, 0d, 100d)]
    [Arguments(UsdGeomCameraProjection.Orthographic, -10d, 100d)]
    public async Task ProjectionMapsNearAndFarPlanesToOpenGlNdc(
        UsdGeomCameraProjection projectionMode,
        double near,
        double far)
    {
        ViewerStageCameraSnapshot snapshot = CreateSnapshot(
            projectionMode,
            near: near,
            far: far);
        Matrix4x4 projection =
            ViewerStageCameraProjectionMath.CreateProjectionMatrix(
                snapshot,
                new ViewportDimensions(800, 600));

        double nearNdc = MapViewZToNdc(projection, -near);
        double farNdc = MapViewZToNdc(projection, -far);

        await Assert.That(NearlyEqualNdc(nearNdc, -1d)).IsTrue();
        await Assert.That(NearlyEqualNdc(farNdc, 1d)).IsTrue();
    }

    [Test]
    public async Task CameraStateConversionRejectsViewOutsideFloatRange()
    {
        UsdMatrix4d hugeView = new(
            double.MaxValue, 0d, 0d, 0d,
            0d, 1d, 0d, 0d,
            0d, 0d, 1d, 0d,
            0d, 0d, 0d, 1d);
        var snapshot = new ViewerStageCameraSnapshot(
            "/World/Camera",
            0d,
            UsdMatrix4d.Identity,
            hugeView,
            CreateOptics(UsdGeomCameraProjection.Perspective));

        await Assert.That(() => ViewerStageCameraProjectionMath.CreateCameraState(
            snapshot,
            new ViewportDimensions(800, 600)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SelectedCameraQueryHandlesNoneReadyMissingAndNonCameraOnce()
    {
        ViewerStageCameraSnapshot snapshot = CreateSnapshot(
            UsdGeomCameraProjection.Perspective);
        var source = new TestStageCameraSource(
            request => request.PrimPath switch
            {
                "/World/Camera" => ViewerStageCameraQueryResult.Ready(snapshot),
                "/World/Mesh" => ViewerStageCameraQueryResult.NotCamera(request.PrimPath),
                _ => ViewerStageCameraQueryResult.Missing(request.PrimPath),
            });

        ViewerStageCameraQueryResult none = await ViewerStageCameraQuery.QueryAsync(
            source,
            selectedPrimPath: null,
            new StageTime(7d),
            CancellationToken.None);
        ViewerStageCameraQueryResult ready = await ViewerStageCameraQuery.QueryAsync(
            source,
            "/World/Camera",
            new StageTime(0d),
            CancellationToken.None);
        ViewerStageCameraQueryResult nonCamera =
            await ViewerStageCameraQuery.QueryAsync(
                source,
                "/World/Mesh",
                new StageTime(1d),
                CancellationToken.None);
        ViewerStageCameraQueryResult missing =
            await ViewerStageCameraQuery.QueryAsync(
                source,
                "/World/Missing",
                new StageTime(2d),
                CancellationToken.None);

        await Assert.That(none.Outcome)
            .IsEqualTo(ViewerStageCameraQueryOutcome.NoSelection);
        await Assert.That(ready.Outcome)
            .IsEqualTo(ViewerStageCameraQueryOutcome.Ready);
        await Assert.That(ready.Snapshot).IsEqualTo(snapshot);
        await Assert.That(nonCamera.Outcome)
            .IsEqualTo(ViewerStageCameraQueryOutcome.NotCamera);
        await Assert.That(missing.Outcome)
            .IsEqualTo(ViewerStageCameraQueryOutcome.MissingPrim);
        await Assert.That(source.QueryCount).IsEqualTo(3);
        await Assert.That(source.Requests[0])
            .IsEqualTo(new ViewerStageCameraRequest("/World/Camera", 0d));
        await Assert.That(source.Requests[1])
            .IsEqualTo(new ViewerStageCameraRequest("/World/Mesh", 1d));
        await Assert.That(source.Requests[2])
            .IsEqualTo(new ViewerStageCameraRequest("/World/Missing", 2d));
    }

    [Test]
    public async Task SmokeContractRejectsMissingNonCameraStaleTimeAndState()
    {
        ViewerStageCameraQueryResult missing =
            ViewerStageCameraQueryResult.Missing("/World/Missing");
        ViewerStageCameraQueryResult nonCamera =
            ViewerStageCameraQueryResult.NotCamera("/World/Cube");
        ViewerStageCameraSnapshot staleSnapshot = CreateSnapshot(
            UsdGeomCameraProjection.Perspective,
            timeCode: ViewerStageCameraSmokeContract.SampledTimeCode);
        ViewerStageCameraQueryResult stale =
            ViewerStageCameraQueryResult.Ready(staleSnapshot);

        await Assert.That(() => ViewerStageCameraSmokeContract.RequireReady(
            missing,
            "/World/Missing",
            ViewerStageCameraSmokeContract.InitialTimeCode))
            .Throws<InvalidOperationException>();
        await Assert.That(() => ViewerStageCameraSmokeContract.RequireReady(
            nonCamera,
            "/World/Cube",
            ViewerStageCameraSmokeContract.InitialTimeCode))
            .Throws<InvalidOperationException>();
        await Assert.That(() => ViewerStageCameraSmokeContract.RequireReady(
            stale,
            staleSnapshot.PrimPath,
            ViewerStageCameraSmokeContract.InitialTimeCode))
            .Throws<InvalidDataException>();

        StageRenderState expected =
            StageRenderState.Create(new StageIdentity("stage.usda"));
        StageRenderState staleState = expected.AdvanceRevision();
        CameraState camera =
            ViewerStageCameraProjectionMath.CreateCameraState(
                CreateSnapshot(UsdGeomCameraProjection.Perspective),
                new ViewportDimensions(800, 600));
        await Assert.That(() => ViewerStageCameraSmokeContract.ApplyCamera(
            staleState,
            expected,
            "/World/Camera",
            0d,
            camera)).Throws<InvalidOperationException>();
        await Assert.That(() => ViewerStageCameraSmokeContract.ApplyAutomatic(
            staleState,
            expected,
            "/World/Camera",
            0d)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SnapshotEvidenceHashBindsPathTimeTransformAndOptics()
    {
        ViewerStageCameraSnapshot initial = CreateSnapshot(
            UsdGeomCameraProjection.Perspective,
            timeCode: ViewerStageCameraSmokeContract.InitialTimeCode,
            localToWorld: UsdMatrix4d.CreateTranslation(0d, 0d, 12d),
            horizontalApertureOffset: 1.5d,
            verticalApertureOffset: -0.75d);
        ViewerStageCameraSnapshot sampled = CreateSnapshot(
            UsdGeomCameraProjection.Perspective,
            focalLength: 28d,
            timeCode: ViewerStageCameraSmokeContract.SampledTimeCode,
            localToWorld: UsdMatrix4d.CreateTranslation(1.25d, 0.5d, 8d),
            horizontalApertureOffset: -1.25d,
            verticalApertureOffset: 0.5d);

        string initialHash =
            ViewerStageCameraSmokeContract.ComputeSnapshotSha256(initial);
        string repeatedHash =
            ViewerStageCameraSmokeContract.ComputeSnapshotSha256(initial);
        string sampledHash =
            ViewerStageCameraSmokeContract.ComputeSnapshotSha256(sampled);

        await Assert.That(initialHash.Length).IsEqualTo(64);
        await Assert.That(initialHash.All(Uri.IsHexDigit)).IsTrue();
        await Assert.That(repeatedHash).IsEqualTo(initialHash);
        await Assert.That(sampledHash).IsNotEqualTo(initialHash);
    }

    [Test]
    public async Task SmokeAssetAuthorsNestedSampledOffAxisCamera()
    {
        string root = FindRepositoryRoot();
        string asset = await File.ReadAllTextAsync(Path.Combine(
            root,
            "test-assets",
            "viewer-stage-camera-smoke.usda"));

        await Assert.That(asset).Contains("def Xform \"CameraRig\"");
        await Assert.That(asset).Contains("def Xform \"Offset\"");
        await Assert.That(asset).Contains("def Camera \"ShotCamera\"");
        await Assert.That(asset).Contains("horizontalApertureOffset.timeSamples");
        await Assert.That(asset).Contains("verticalApertureOffset.timeSamples");
        await Assert.That(asset).Contains("focalLength.timeSamples");
        await Assert.That(asset).Contains("xformOp:transform.timeSamples");
        await Assert.That(asset).Contains("0: (");
        await Assert.That(asset).Contains("24: (");
        await Assert.That(asset).Contains("float2 clippingRange = (0.1, 100)");
        await Assert.That(asset).Contains("def Mesh \"Backdrop\"");
        await Assert.That(asset).Contains("def Cube \"Cube\"");
        await Assert.That(asset).Contains("def Cube \"Marker\"");
    }

    [Test]
    public async Task NavigationCommandsExitStageCameraAndUseOrbitController()
    {
        var verifier = new TestUiThreadVerifier();
        var mode = new ViewerStageCameraModeState(
            new ViewportDimensions(800, 600));
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(800, 600));
        var adapter = new ViewerCameraNavigationUiAdapter(
            controller,
            mode,
            verifier);
        ViewerStageCameraSnapshot snapshot = CreateSnapshot(
            UsdGeomCameraProjection.Perspective);

        Activate(adapter, snapshot);
        await Assert.That(adapter.ApplyGesture(
            ViewerCameraPointerGesture.Orbit,
            new Vector2(8f, -4f))).IsTrue();
        await Assert.That(adapter.DisplayMode)
            .IsEqualTo(ViewerCameraDisplayMode.Perspective);

        Activate(adapter, snapshot);
        await Assert.That(adapter.ApplyGesture(
            ViewerCameraPointerGesture.Pan,
            new Vector2(3f, 2f))).IsTrue();
        await Assert.That(mode.GetView().IsActive).IsFalse();

        Activate(adapter, snapshot);
        await Assert.That(adapter.ZoomWheel(1f)).IsTrue();
        await Assert.That(mode.GetView().IsActive).IsFalse();

        Activate(adapter, snapshot);
        await Assert.That(adapter.ResetToExplicitPose()).IsTrue();
        await Assert.That(mode.GetView().IsActive).IsFalse();

        Activate(adapter, snapshot);
        await Assert.That(adapter.ToggleProjection()).IsTrue();
        await Assert.That(mode.GetView().IsActive).IsFalse();

        Activate(adapter, snapshot);
        await Assert.That(adapter.FrameBounds(new UsdBounds3d(
            new UsdVec3d(-1d, -1d, -1d),
            new UsdVec3d(1d, 1d, 1d)))).IsTrue();
        await Assert.That(mode.GetView().IsActive).IsFalse();

        Activate(adapter, snapshot);
        await Assert.That(adapter.ResetToAutomatic()).IsTrue();
        await Assert.That(adapter.DisplayMode)
            .IsEqualTo(ViewerCameraDisplayMode.Automatic);
        await Assert.That(adapter.Camera).IsEqualTo(CameraState.Default);
    }

    [Test]
    public async Task ResizeRecomputesStageProjectionWithViewportAtomically()
    {
        var verifier = new TestUiThreadVerifier();
        var mode = new ViewerStageCameraModeState(
            new ViewportDimensions(800, 600));
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(800, 600));
        var adapter = new ViewerCameraNavigationUiAdapter(
            controller,
            mode,
            verifier);
        Activate(adapter, CreateSnapshot(UsdGeomCameraProjection.Perspective));
        CameraState beforeCamera = adapter.Camera;
        StageRenderState before = StageRenderState
            .Create(new StageIdentity("stage"))
            .WithViewport(new ViewportDimensions(800, 600))
            .WithCamera(beforeCamera);

        ViewerCameraResizeUpdate update = adapter.Resize(
            new ViewportDimensions(1600, 600));
        StageRenderState after = ViewerCameraStateMutation.ApplyResize(
            before,
            update);

        await Assert.That(update.ViewportChanged).IsTrue();
        await Assert.That(update.CameraChanged).IsTrue();
        await Assert.That(after.Viewport)
            .IsEqualTo(new ViewportDimensions(1600, 600));
        await Assert.That(after.Camera).IsEqualTo(update.Camera);
        await Assert.That(after.Camera).IsNotEqualTo(beforeCamera);
        await Assert.That(after.Camera.Projection.M11)
            .IsLessThan(beforeCamera.Projection.M11);
    }

    [Test]
    public async Task RefreshPumpCoalescesTimeUpdatesAndAppliesLatestSnapshot()
    {
        var mode = new ViewerStageCameraModeState(
            new ViewportDimensions(800, 600));
        ViewerStageCameraSnapshot initial = CreateSnapshot(
            UsdGeomCameraProjection.Perspective);
        ViewerStageCameraActivation activation =
            mode.CaptureActivation(initial.PrimPath, initial.TimeCode);
        await Assert.That(mode.TryActivate(
            activation,
            initial,
            out _)).IsTrue();

        var source = new BlockingStageCameraSource();
        var applications = new List<ViewerStageCameraRefreshApplication>();
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new List<Exception>();
        var pump = new ViewerStageCameraRefreshPump(
            source,
            mode,
            (application, _) =>
            {
                lock (applications)
                {
                    applications.Add(application);
                }
                if (application.Request.TimeCode == 3d)
                {
                    completed.TrySetResult();
                }
                return ValueTask.CompletedTask;
            },
            failures.Add,
            CancellationToken.None);

        await Assert.That(mode.TryCreateRefreshRequest(
            1d,
            applyTime: true,
            out ViewerStageCameraRefreshRequest first)).IsTrue();
        await Assert.That(pump.TryPost(first)).IsTrue();
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(mode.TryCreateRefreshRequest(
            2d,
            applyTime: true,
            out ViewerStageCameraRefreshRequest second)).IsTrue();
        await Assert.That(mode.TryCreateRefreshRequest(
            3d,
            applyTime: true,
            out ViewerStageCameraRefreshRequest third)).IsTrue();
        await Assert.That(pump.TryPost(second)).IsTrue();
        await Assert.That(pump.TryPost(third)).IsTrue();
        source.Release.TrySetResult();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await pump.DisposeAsync();

        await Assert.That(source.Requests.Count).IsEqualTo(2);
        await Assert.That(source.Requests[0].TimeCode).IsEqualTo(1d);
        await Assert.That(source.Requests[1].TimeCode).IsEqualTo(3d);
        await Assert.That(applications.Count).IsEqualTo(1);
        await Assert.That(applications[0].Request.TimeCode).IsEqualTo(3d);
        await Assert.That(applications[0].Outcome)
            .IsEqualTo(ViewerStageCameraRefreshOutcome.Ready);
        await Assert.That(applications[0].Request.ApplyTime).IsTrue();
        await Assert.That(failures).IsEmpty();
        await Assert.That(mode.TryGetCamera(out CameraState refreshed)).IsTrue();
        await Assert.That(NearlyEqual(refreshed.View.M41, -3f)).IsTrue();
        await Assert.That(refreshed.Projection.M34).IsEqualTo(0f);
        await Assert.That(refreshed.Projection.M44).IsEqualTo(1f);
        await Assert.That(NearlyEqual(refreshed.Projection.M41, -0.2f)).IsTrue();
        await Assert.That(NearlyEqual(
            refreshed.Projection.M43,
            (float)(-27d / 33d))).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOrInvalidActiveCameraFallsBackWithoutChangingRequestTime(
        bool invalid)
    {
        var mode = new ViewerStageCameraModeState(
            new ViewportDimensions(800, 600));
        ViewerStageCameraSnapshot initial = CreateSnapshot(
            UsdGeomCameraProjection.Orthographic);
        ViewerStageCameraActivation activation =
            mode.CaptureActivation(initial.PrimPath, initial.TimeCode);
        mode.TryActivate(activation, initial, out _);
        var source = new TestStageCameraSource(request =>
            invalid
                ? ViewerStageCameraQueryResult.Invalid(
                    request.PrimPath,
                    $"Camera '{request.PrimPath}' has invalid clipping.")
                : ViewerStageCameraQueryResult.Missing(request.PrimPath));
        var completed = new TaskCompletionSource<ViewerStageCameraRefreshApplication>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var pump = new ViewerStageCameraRefreshPump(
            source,
            mode,
            (application, _) =>
            {
                completed.TrySetResult(application);
                return ValueTask.CompletedTask;
            },
            static _ => { },
            CancellationToken.None);
        mode.TryCreateRefreshRequest(
            24d,
            applyTime: true,
            out ViewerStageCameraRefreshRequest request);

        await Assert.That(pump.TryPost(request)).IsTrue();
        ViewerStageCameraRefreshApplication application =
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await pump.DisposeAsync();

        await Assert.That(application.Outcome)
            .IsEqualTo(ViewerStageCameraRefreshOutcome.FallbackAutomatic);
        await Assert.That(application.Request.TimeCode).IsEqualTo(24d);
        await Assert.That(application.Request.ApplyTime).IsTrue();
        await Assert.That(application.Error).Contains(
            invalid ? "invalid clipping" : "no longer exists");
        ViewerStageCameraModeView view = mode.GetView();
        await Assert.That(view.IsActive).IsFalse();
        await Assert.That(view.ForcesAutomatic).IsTrue();
        await Assert.That(mode.TryGetCamera(out CameraState camera)).IsTrue();
        await Assert.That(camera).IsEqualTo(CameraState.Default);
    }

    [Test]
    public async Task PureProjectionConversionAllocatesNothingAfterWarmup()
    {
        ViewerStageCameraSnapshot snapshot = CreateSnapshot(
            UsdGeomCameraProjection.Perspective);
        var viewport = new ViewportDimensions(1920, 1080);
        for (int index = 0; index < 64; index++)
        {
            ConvertProjection(snapshot, viewport);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < AllocationIterations; index++)
        {
            ConvertProjection(snapshot, viewport);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task ViewerSourceWiresSelectedCameraAndAutomatedEvidenceGuard()
    {
        string root = FindRepositoryRoot();
        string markup = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml"));
        string window = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "MainWindow.axaml.cs"));
        string models = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerStageCamera.cs"));
        string documentModels = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Viewer",
            "ViewerDocumentModels.cs"));
        string cameraInitialization = SliceMethod(
            window,
            "private void InitializeCameraUpdates",
            "private static async ValueTask PublishCameraAsync");
        string automatedEvidence = SliceMethod(
            window,
            "private async Task RunExplicitCameraEvidenceAsync",
            "private async Task<(");
        string stageCameraEvidence = SliceMethod(
            window,
            "private async Task RunStageCameraBackendSmokeAsync",
            "private async Task<ViewerStageCameraBackendFrameEvidence[]>");

        await Assert.That(markup).Contains("x:Name=\"UseSelectedCameraButton\"");
        await Assert.That(markup)
            .Contains("AutomationProperties.Name=\"Use selected UsdGeomCamera\"");
        await Assert.That(window).Contains("OnUseSelectedCameraClick");
        await Assert.That(window).Contains("inspector is not { IsCamera: true }");
        await Assert.That(window).Contains("if (IsAutomatedViewerRun())");
        await Assert.That(window).Contains("TryQueueStageCameraRefresh(");
        await Assert.That(window).Contains("coordinator.StageChanged += OnStageChanged;");
        await Assert.That(cameraInitialization.IndexOf(
            "if (IsAutomatedViewerRun())",
            StringComparison.Ordinal)).IsLessThan(cameraInitialization.IndexOf(
                "new ViewerStageCameraRefreshPump(",
                StringComparison.Ordinal));
        await Assert.That(models).Contains("UsdGeomCamera.TryWrap(prim");
        await Assert.That(models)
            .Contains("camera.Xformable.GetWorldTransform(request.TimeCode)");
        await Assert.That(models).Contains("camera.GetState(request.TimeCode)");
        await Assert.That(models).DoesNotContain("camera.FocalLength");
        await Assert.That(models).DoesNotContain("camera.HorizontalAperture");
        await Assert.That(models).DoesNotContain("camera.VerticalAperture");
        await Assert.That(models).Contains("localToWorld.TryInvert(");
        await Assert.That(models).Contains("ViewerStageCameraProjectionMath");
        await Assert.That(models).DoesNotContain("stage.Traverse(");
        await Assert.That(documentModels).Contains(
            "bool isCamera = UsdGeomCamera.TryWrap(prim, out _);");
        await Assert.That(automatedEvidence).DoesNotContain(
            "UseSelectedCamera");
        await Assert.That(automatedEvidence).DoesNotContain(
            "ViewerStageCamera");
        await Assert.That(stageCameraEvidence).Contains(
            "new ViewerSchedulerStageCameraSource(coordinator.Scheduler)");
        await Assert.That(stageCameraEvidence).Contains(
            "ViewerStageCameraQuery.QueryAsync(");
        await Assert.That(stageCameraEvidence).Contains(
            "_stageCameraMode.TryActivate(");
        await Assert.That(stageCameraEvidence).Contains(
            "_stageCameraMode.TryRefresh(");
        await Assert.That(stageCameraEvidence).Contains(
            "ViewerStageCameraSmokeContract.ApplyAutomatic(");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConvertProjection(
        ViewerStageCameraSnapshot snapshot,
        ViewportDimensions viewport) =>
        _projectionSink = ViewerStageCameraProjectionMath.CreateProjectionMatrix(
            snapshot,
            viewport);

    private static void Activate(
        ViewerCameraNavigationUiAdapter adapter,
        ViewerStageCameraSnapshot snapshot)
    {
        ViewerStageCameraActivation activation =
            adapter.CaptureStageCameraActivation(
                snapshot.PrimPath,
                snapshot.TimeCode);
        if (!adapter.TryActivateStageCamera(activation, snapshot, out _))
        {
            throw new InvalidOperationException("The test camera could not be activated.");
        }
    }

    private static ViewerStageCameraSnapshot CreateSnapshot(
        UsdGeomCameraProjection projection,
        double focalLength = 50d,
        double horizontalAperture = 24d,
        double verticalAperture = 18d,
        double near = 0.1d,
        double far = 1000d,
        double timeCode = 0d,
        UsdMatrix4d? localToWorld = null,
        double horizontalApertureOffset = 0d,
        double verticalApertureOffset = 0d) =>
        CreateSnapshot(
            CreateOptics(
                projection,
                focalLength,
                horizontalAperture,
                verticalAperture,
                near,
                far,
                horizontalApertureOffset,
                verticalApertureOffset),
            timeCode,
            localToWorld);

    private static ViewerStageCameraSnapshot CreateSnapshot(
        UsdGeomCameraState optics,
        double timeCode = 0d,
        UsdMatrix4d? localToWorld = null)
    {
        UsdMatrix4d world = localToWorld ?? UsdMatrix4d.Identity;
        return ViewerStageCameraSnapshotFactory.Create(
            "/World/Camera",
            timeCode,
            world,
            optics);
    }

    private static UsdGeomCameraState CreateOptics(
        UsdGeomCameraProjection projection,
        double focalLength = 50d,
        double horizontalAperture = 24d,
        double verticalAperture = 18d,
        double near = 0.1d,
        double far = 1000d,
        double horizontalApertureOffset = 0d,
        double verticalApertureOffset = 0d)
    {
        double scale = projection == UsdGeomCameraProjection.Perspective
            ? 1d / focalLength
            : 0.1d;
        return new UsdGeomCameraState(
            projection,
            (-horizontalAperture / 2d + horizontalApertureOffset) * scale,
            (horizontalAperture / 2d + horizontalApertureOffset) * scale,
            (-verticalAperture / 2d + verticalApertureOffset) * scale,
            (verticalAperture / 2d + verticalApertureOffset) * scale,
            near,
            far,
            focalLength,
            horizontalAperture,
            verticalAperture,
            horizontalApertureOffset,
            verticalApertureOffset,
            focusDistance: 0d,
            fStop: 0d);
    }

    private static UsdGeomCameraState CreateOpticsFromWindow(
        UsdGeomCameraProjection projection,
        double left,
        double right,
        double bottom,
        double top,
        double near,
        double far) =>
        new(
            projection,
            left,
            right,
            bottom,
            top,
            near,
            far,
            focalLength: 50d,
            horizontalAperture: 24d,
            verticalAperture: 18d,
            horizontalApertureOffset: 0d,
            verticalApertureOffset: 0d,
            focusDistance: 0d,
            fStop: 0d);

    private static async Task AssertNdcCorners(
        Matrix4x4 projection,
        UsdGeomCameraState optics,
        bool perspective)
    {
        double depthScale = perspective ? optics.ClippingNear : 1d;
        double viewZ = perspective ? -optics.ClippingNear : -optics.ClippingNear;
        (double left, double bottom) = MapViewPointToNdc(
            projection,
            optics.WindowLeft * depthScale,
            optics.WindowBottom * depthScale,
            viewZ);
        (double right, double top) = MapViewPointToNdc(
            projection,
            optics.WindowRight * depthScale,
            optics.WindowTop * depthScale,
            viewZ);

        await Assert.That(NearlyEqualNdc(left, -1d)).IsTrue();
        await Assert.That(NearlyEqualNdc(bottom, -1d)).IsTrue();
        await Assert.That(NearlyEqualNdc(right, 1d)).IsTrue();
        await Assert.That(NearlyEqualNdc(top, 1d)).IsTrue();
    }

    private static (double X, double Y) MapViewPointToNdc(
        Matrix4x4 projection,
        double viewX,
        double viewY,
        double viewZ)
    {
        double clipX =
            (viewX * projection.M11) +
            (viewY * projection.M21) +
            (viewZ * projection.M31) +
            projection.M41;
        double clipY =
            (viewX * projection.M12) +
            (viewY * projection.M22) +
            (viewZ * projection.M32) +
            projection.M42;
        double clipW =
            (viewX * projection.M14) +
            (viewY * projection.M24) +
            (viewZ * projection.M34) +
            projection.M44;
        return (clipX / clipW, clipY / clipW);
    }

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= DoubleTolerance;

    private static bool NearlyEqualNdc(double left, double right) =>
        Math.Abs(left - right) <= FloatTolerance;

    private static bool NearlyEqual(float left, float right) =>
        MathF.Abs(left - right) <= FloatTolerance;

    private static double MapViewZToNdc(Matrix4x4 projection, double viewZ)
    {
        double clipZ = (viewZ * projection.M33) + projection.M43;
        double clipW = (viewZ * projection.M34) + projection.M44;
        return clipZ / clipW;
    }

    private static bool NearlyEqual(Matrix4x4 left, Matrix4x4 right) =>
        NearlyEqual(left.M11, right.M11) &&
        NearlyEqual(left.M12, right.M12) &&
        NearlyEqual(left.M13, right.M13) &&
        NearlyEqual(left.M14, right.M14) &&
        NearlyEqual(left.M21, right.M21) &&
        NearlyEqual(left.M22, right.M22) &&
        NearlyEqual(left.M23, right.M23) &&
        NearlyEqual(left.M24, right.M24) &&
        NearlyEqual(left.M31, right.M31) &&
        NearlyEqual(left.M32, right.M32) &&
        NearlyEqual(left.M33, right.M33) &&
        NearlyEqual(left.M34, right.M34) &&
        NearlyEqual(left.M41, right.M41) &&
        NearlyEqual(left.M42, right.M42) &&
        NearlyEqual(left.M43, right.M43) &&
        NearlyEqual(left.M44, right.M44);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string SliceMethod(
        string source,
        string startMarker,
        string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            throw new InvalidOperationException(
                $"Could not locate source range '{startMarker}' to '{endMarker}'.");
        }
        return source[start..end];
    }

    private sealed class TestUiThreadVerifier : IViewerUiThreadVerifier
    {
        public void VerifyAccess()
        {
        }
    }

    private sealed class TestStageCameraSource(
        Func<ViewerStageCameraRequest, ViewerStageCameraQueryResult> query)
        : IViewerStageCameraSource
    {
        internal List<ViewerStageCameraRequest> Requests { get; } = [];

        internal int QueryCount => Requests.Count;

        public ValueTask<ViewerStageCameraQueryResult> QueryAsync(
            ViewerStageCameraRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult(query(request));
        }
    }

    private sealed class BlockingStageCameraSource : IViewerStageCameraSource
    {
        internal TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal List<ViewerStageCameraRequest> Requests { get; } = [];

        public async ValueTask<ViewerStageCameraQueryResult> QueryAsync(
            ViewerStageCameraRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.TimeCode == 1d)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }

            bool finalSample = request.TimeCode >= 3d;
            ViewerStageCameraSnapshot snapshot = CreateSnapshot(
                finalSample
                    ? UsdGeomCameraProjection.Orthographic
                    : UsdGeomCameraProjection.Perspective,
                horizontalAperture: finalSample ? 20d : 24d,
                verticalAperture: finalSample ? 10d : 18d,
                near: finalSample ? -3d : 0.1d,
                far: finalSample ? 30d : 1000d,
                timeCode: request.TimeCode,
                localToWorld: UsdMatrix4d.CreateTranslation(
                    request.TimeCode,
                    0d,
                    0d),
                horizontalApertureOffset: finalSample ? 2d : 0d,
                verticalApertureOffset: finalSample ? -1d : 0d);
            return ViewerStageCameraQueryResult.Ready(snapshot);
        }
    }
}
