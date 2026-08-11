// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Runtime.CompilerServices;
using OpenUsd.Rendering;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerCameraNavigationTests
{
    private const float Tolerance = 1e-5f;
    private const int AllocationIterations = 1000;
    private static float _allocationSink;

    [Test]
    public async Task AutomaticDefaultPreservesNativeCameraAndLegacyModel()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(800, 600));
        ViewerCameraNavigationState state = controller.State;

        await Assert.That(state.IsAutomatic).IsTrue();
        await Assert.That(state.Target).IsEqualTo(Vector3.Zero);
        await Assert.That(state.Distance)
            .IsEqualTo(ViewerCameraNavigationState.LegacyDistance);
        await Assert.That(state.Yaw)
            .IsEqualTo(ViewerCameraNavigationState.LegacyYaw);
        await Assert.That(state.Pitch)
            .IsEqualTo(ViewerCameraNavigationState.LegacyPitch);
        await Assert.That(state.ProjectionMode)
            .IsEqualTo(ViewerCameraProjectionMode.Perspective);
        await Assert.That(state.VerticalFieldOfView)
            .IsEqualTo(ViewerCameraNavigationState.LegacyVerticalFieldOfView);
        await Assert.That(state.NearPlane)
            .IsEqualTo(ViewerCameraNavigationState.LegacyNearPlane);
        await Assert.That(state.FarPlane)
            .IsEqualTo(ViewerCameraNavigationState.LegacyFarPlane);
        await Assert.That(state.AspectRatio).IsEqualTo(4f / 3f);
        await Assert.That(controller.Camera).IsEqualTo(CameraState.Default);
        await Assert.That(controller.Camera.Mode).IsEqualTo(CameraMode.Automatic);
    }

    [Test]
    public async Task ExplicitResetMaterializesExactLegacyPose()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(1920, 1080));

        await Assert.That(controller.ResetToExplicitPose()).IsTrue();

        ViewerCameraNavigationState state = controller.State;
        CameraState camera = controller.Camera;
        Matrix4x4 expectedView = Matrix4x4.CreateLookAt(
            new Vector3(4f, 3f, 4f),
            Vector3.Zero,
            Vector3.UnitY);
        Matrix4x4 expectedProjection = CreatePinnedGfPerspective(
            MathF.PI / 4f,
            16f / 9f,
            0.1f,
            1000f);

        await Assert.That(state.IsAutomatic).IsFalse();
        await Assert.That(state.Eye).IsEqualTo(new Vector3(4f, 3f, 4f));
        await Assert.That(camera.Mode).IsEqualTo(CameraMode.Matrices);
        await Assert.That(camera.View).IsEqualTo(expectedView);
        await Assert.That(camera.Projection).IsEqualTo(expectedProjection);
        await Assert.That(camera).IsNotEqualTo(CameraState.Default);
    }

    [Test]
    public async Task FirstNavigationActionStartsFromExactLegacyParameters()
    {
        var orbit = new ViewerCameraNavigationController(
            new ViewportDimensions(800, 600));
        var pan = new ViewerCameraNavigationController(
            new ViewportDimensions(800, 600));
        var zoom = new ViewerCameraNavigationController(
            new ViewportDimensions(800, 600));

        await Assert.That(orbit.Orbit(0.25f, -0.125f)).IsTrue();
        await Assert.That(orbit.State.IsAutomatic).IsFalse();
        await Assert.That(NearlyEqual(
            orbit.State.Yaw,
            ViewerCameraNavigationState.LegacyYaw + 0.25f)).IsTrue();
        await Assert.That(NearlyEqual(
            orbit.State.Pitch,
            ViewerCameraNavigationState.LegacyPitch - 0.125f)).IsTrue();
        await Assert.That(orbit.State.Distance)
            .IsEqualTo(ViewerCameraNavigationState.LegacyDistance);

        await Assert.That(pan.Pan(2f, -1f)).IsTrue();
        Vector3 expectedTarget = LegacyRight() * 2f - LegacyUp();
        await Assert.That(NearlyEqual(pan.State.Target, expectedTarget)).IsTrue();
        await Assert.That(pan.State.Distance)
            .IsEqualTo(ViewerCameraNavigationState.LegacyDistance);

        await Assert.That(zoom.Zoom(MathF.Log(2f))).IsTrue();
        await Assert.That(NearlyEqual(
            zoom.State.Distance,
            ViewerCameraNavigationState.LegacyDistance * 2f)).IsTrue();
        await Assert.That(zoom.State.Target).IsEqualTo(Vector3.Zero);
        await Assert.That(zoom.State.Yaw)
            .IsEqualTo(ViewerCameraNavigationState.LegacyYaw);
        await Assert.That(zoom.State.Pitch)
            .IsEqualTo(ViewerCameraNavigationState.LegacyPitch);
    }

    [Test]
    public async Task ExplicitIdentityCameraRemainsDistinctFromAutomaticMode()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(640, 480));
        var explicitIdentity = new CameraState(
            Matrix4x4.Identity,
            Matrix4x4.Identity);

        await Assert.That(controller.Camera.Mode).IsEqualTo(CameraMode.Automatic);
        await Assert.That(explicitIdentity.Mode).IsEqualTo(CameraMode.Matrices);
        await Assert.That(explicitIdentity).IsNotEqualTo(controller.Camera);
        await Assert.That(explicitIdentity).IsNotEqualTo(CameraState.Default);
    }

    [Test]
    public async Task OrbitWrapsYawClampsPitchAndLeavesZeroDeltaAutomatic()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(640, 480));
        ViewerCameraNavigationState automatic = controller.State;

        await Assert.That(controller.Orbit(0f, 0f)).IsFalse();
        await Assert.That(controller.State).IsEqualTo(automatic);

        await Assert.That(controller.Orbit(float.MaxValue, float.MaxValue)).IsTrue();
        await Assert.That(float.IsFinite(controller.State.Yaw)).IsTrue();
        await Assert.That(controller.State.Yaw >= -MathF.PI).IsTrue();
        await Assert.That(controller.State.Yaw <= MathF.PI).IsTrue();
        await Assert.That(controller.State.Pitch)
            .IsEqualTo(ViewerCameraNavigationState.MaximumPitch);

        await Assert.That(controller.Orbit(0f, -float.MaxValue)).IsTrue();
        await Assert.That(controller.State.Pitch)
            .IsEqualTo(-ViewerCameraNavigationState.MaximumPitch);
        await Assert.That(IsFinite(controller.Camera)).IsTrue();
    }

    [Test]
    public async Task PanUsesRollFreeCameraRightAndUpAxes()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(1024, 768));
        controller.ResetToExplicitPose();
        ViewerCameraNavigationState before = controller.State;

        await Assert.That(controller.Pan(3f, 2f)).IsTrue();

        Vector3 expected = (LegacyRight() * 3f) + (LegacyUp() * 2f);
        await Assert.That(NearlyEqual(controller.State.Target, expected)).IsTrue();
        await Assert.That(controller.State.Distance).IsEqualTo(before.Distance);
        await Assert.That(controller.State.Yaw).IsEqualTo(before.Yaw);
        await Assert.That(controller.State.Pitch).IsEqualTo(before.Pitch);
        await Assert.That(controller.State.ProjectionMode)
            .IsEqualTo(before.ProjectionMode);
        await Assert.That(controller.State.NearPlane).IsEqualTo(before.NearPlane);
        await Assert.That(controller.State.FarPlane).IsEqualTo(before.FarPlane);
    }

    [Test]
    public async Task ZoomIsExponentialAndClampsPerspectiveAndOrthographicRanges()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(800, 600));
        controller.ResetToExplicitPose();
        float originalDistance = controller.State.Distance;

        controller.Zoom(MathF.Log(3f));
        await Assert.That(NearlyEqual(
            controller.State.Distance,
            originalDistance * 3f)).IsTrue();
        controller.Zoom(float.MaxValue);
        await Assert.That(controller.State.Distance)
            .IsEqualTo(ViewerCameraNavigationState.MaximumDistance);
        controller.Zoom(-float.MaxValue);
        await Assert.That(controller.State.Distance)
            .IsEqualTo(ViewerCameraNavigationState.MinimumDistance);

        controller.ResetToExplicitPose();
        controller.ToggleProjection();
        float originalHeight = controller.State.OrthographicHeight;
        controller.Zoom(MathF.Log(2f));
        await Assert.That(NearlyEqual(
            controller.State.OrthographicHeight,
            originalHeight * 2f)).IsTrue();
        controller.Zoom(float.MaxValue);
        await Assert.That(controller.State.OrthographicHeight)
            .IsEqualTo(ViewerCameraNavigationState.MaximumOrthographicHeight);
        controller.Zoom(-float.MaxValue);
        await Assert.That(controller.State.OrthographicHeight)
            .IsEqualTo(ViewerCameraNavigationState.MinimumOrthographicHeight);
    }

    [Test]
    public async Task ProjectionTogglePreservesTargetScaleAndPose()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(1600, 900));
        controller.ResetToExplicitPose();
        ViewerCameraNavigationState perspective = controller.State;
        float visibleHeight = perspective.GetVisibleVerticalSpan();

        await Assert.That(controller.ToggleProjection()).IsTrue();
        ViewerCameraNavigationState orthographic = controller.State;
        await Assert.That(orthographic.ProjectionMode)
            .IsEqualTo(ViewerCameraProjectionMode.Orthographic);
        await Assert.That(NearlyEqual(
            orthographic.OrthographicHeight,
            visibleHeight)).IsTrue();
        await Assert.That(orthographic.Target).IsEqualTo(perspective.Target);
        await Assert.That(orthographic.Yaw).IsEqualTo(perspective.Yaw);
        await Assert.That(orthographic.Pitch).IsEqualTo(perspective.Pitch);
        await Assert.That(orthographic.NearPlane).IsEqualTo(perspective.NearPlane);
        await Assert.That(orthographic.FarPlane).IsEqualTo(perspective.FarPlane);

        controller.Zoom(MathF.Log(2f));
        await Assert.That(controller.ToggleProjection()).IsTrue();
        await Assert.That(controller.State.ProjectionMode)
            .IsEqualTo(ViewerCameraProjectionMode.Perspective);
        await Assert.That(NearlyEqual(
            controller.State.Distance,
            perspective.Distance * 2f)).IsTrue();
    }

    [Test]
    public async Task ResetToAutomaticRestoresLegacyStateAndNativeSelection()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(1234, 567));
        controller.Orbit(0.5f, 0.25f);
        controller.Pan(4f, -3f);
        controller.Zoom(1f);
        controller.ToggleProjection();

        await Assert.That(controller.ResetToAutomatic()).IsTrue();

        ViewerCameraNavigationState expected =
            ViewerCameraNavigationState.CreateLegacy(
                isAutomatic: true,
                1234f / 567f);
        await Assert.That(controller.State).IsEqualTo(expected);
        await Assert.That(controller.Camera).IsEqualTo(CameraState.Default);
        await Assert.That(controller.ResetToAutomatic()).IsFalse();
    }

    [Test]
    public async Task ResizeUpdatesAspectWithoutMaterializingOrChangingPose()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(800, 600));
        ViewerCameraNavigationState automatic = controller.State;

        await Assert.That(controller.Resize(new ViewportDimensions(1920, 1080))).IsTrue();
        await Assert.That(controller.State.IsAutomatic).IsTrue();
        await Assert.That(controller.Camera).IsEqualTo(CameraState.Default);
        await Assert.That(controller.State.AspectRatio).IsEqualTo(16f / 9f);
        AssertPoseExceptAspectEqual(automatic, controller.State);

        controller.ResetToExplicitPose();
        Matrix4x4 expectedProjection = CreatePinnedGfPerspective(
            MathF.PI / 4f,
            16f / 9f,
            0.1f,
            1000f);
        await Assert.That(controller.Camera.Projection).IsEqualTo(expectedProjection);

        await Assert.That(controller.Resize(ViewportDimensions.Empty)).IsTrue();
        await Assert.That(controller.State.AspectRatio).IsEqualTo(1f);
        await Assert.That(controller.State.IsAutomatic).IsFalse();
        await Assert.That(IsFinite(controller.Camera)).IsTrue();
        await Assert.That(controller.Resize(ViewportDimensions.Empty)).IsFalse();
    }

    [Test]
    public async Task PerspectiveMatrixMatchesPinnedGfFrustumAndMapsOpenGlClipDepth()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(1280, 720));
        controller.ResetToExplicitPose();
        ViewerCameraNavigationState state = controller.State;
        CameraState camera = controller.Camera;
        Matrix4x4 expectedProjection = CreatePinnedGfPerspective(
            state.VerticalFieldOfView,
            state.AspectRatio,
            state.NearPlane,
            state.FarPlane);

        Vector3 eyeInView = Vector3.Transform(state.Eye, camera.View);
        Vector3 targetInView = Vector3.Transform(state.Target, camera.View);
        float nearDepth = ProjectDepth(-state.NearPlane, camera.Projection);
        float farDepth = ProjectDepth(-state.FarPlane, camera.Projection);

        await Assert.That(camera.Projection).IsEqualTo(expectedProjection);
        await Assert.That(NearlyEqual(eyeInView, Vector3.Zero)).IsTrue();
        await Assert.That(NearlyEqual(
            targetInView,
            new Vector3(0f, 0f, -state.Distance))).IsTrue();
        await Assert.That(NearlyEqual(nearDepth, -1f)).IsTrue();
        await Assert.That(NearlyEqual(farDepth, 1f)).IsTrue();
    }

    [Test]
    public async Task OrthographicMatrixMatchesPinnedGfFrustumAndMapsOpenGlClipDepth()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(800, 400));
        controller.ResetToExplicitPose();
        controller.ToggleProjection();
        ViewerCameraNavigationState state = controller.State;

        Matrix4x4 expected = CreatePinnedGfOrthographic(
            state.OrthographicHeight,
            state.AspectRatio,
            state.NearPlane,
            state.FarPlane);
        float nearDepth = ProjectDepth(
            -state.NearPlane,
            controller.Camera.Projection);
        float farDepth = ProjectDepth(
            -state.FarPlane,
            controller.Camera.Projection);

        await Assert.That(controller.Camera.Projection).IsEqualTo(expected);
        await Assert.That(NearlyEqual(nearDepth, -1f)).IsTrue();
        await Assert.That(NearlyEqual(farDepth, 1f)).IsTrue();
        await Assert.That(IsFinite(controller.Camera)).IsTrue();
    }

    private static Matrix4x4 CreatePinnedGfPerspective(
        float verticalFieldOfView,
        float aspectRatio,
        float nearPlane,
        float farPlane)
    {
        double tangent = Math.Tan(verticalFieldOfView / 2d);
        double depthRange = (double)farPlane - nearPlane;
        return new Matrix4x4(
            (float)(1d / (tangent * aspectRatio)), 0f, 0f, 0f,
            0f, (float)(1d / tangent), 0f, 0f,
            0f, 0f, (float)(-(((double)farPlane + nearPlane) / depthRange)), -1f,
            0f, 0f, (float)(-(2d * nearPlane * farPlane / depthRange)), 0f);
    }

    private static Matrix4x4 CreatePinnedGfOrthographic(
        float orthographicHeight,
        float aspectRatio,
        float nearPlane,
        float farPlane)
    {
        double width = (double)orthographicHeight * aspectRatio;
        double depthRange = (double)farPlane - nearPlane;
        return new Matrix4x4(
            (float)(2d / width), 0f, 0f, 0f,
            0f, (float)(2d / orthographicHeight), 0f, 0f,
            0f, 0f, (float)(-2d / depthRange), 0f,
            0f, 0f, (float)(-(((double)farPlane + nearPlane) / depthRange)), 1f);
    }

    private static float ProjectDepth(float viewSpaceZ, Matrix4x4 projection)
    {
        Vector4 clip = Vector4.Transform(
            new Vector4(0f, 0f, viewSpaceZ, 1f),
            projection);
        return clip.Z / clip.W;
    }

    [Test]
    public async Task FrameBoundsCentersAndFitsNormalBoundsWithFiniteClipping()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(1600, 900));
        var bounds = new UsdBounds3d(
            new UsdVec3d(-1d, -2d, -3d),
            new UsdVec3d(3d, 2d, 1d));
        const float margin = 1.25f;
        double radius = Math.Sqrt(12d);
        double framedRadius = radius * margin;
        float expectedDistance = (float)(
            framedRadius /
            Math.Sin(ViewerCameraNavigationState.LegacyVerticalFieldOfView / 2d));

        await Assert.That(controller.FrameBounds(bounds, margin)).IsTrue();

        ViewerCameraNavigationState state = controller.State;
        await Assert.That(state.IsAutomatic).IsFalse();
        await Assert.That(state.Target).IsEqualTo(new Vector3(1f, 0f, -1f));
        await Assert.That(NearlyEqual(state.Distance, expectedDistance)).IsTrue();
        await Assert.That(state.NearPlane > 0f).IsTrue();
        await Assert.That(state.FarPlane > state.NearPlane).IsTrue();
        await Assert.That(state.NearPlane < state.Distance - radius).IsTrue();
        await Assert.That(state.FarPlane > state.Distance + radius).IsTrue();
        await Assert.That(state.Yaw)
            .IsEqualTo(ViewerCameraNavigationState.LegacyYaw);
        await Assert.That(state.Pitch)
            .IsEqualTo(ViewerCameraNavigationState.LegacyPitch);
        await Assert.That(IsFinite(controller.Camera)).IsTrue();
    }

    [Test]
    public async Task FramePointBoundsUsesStableNonzeroRadius()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(600, 900));
        var point = new UsdBounds3d(
            new UsdVec3d(2d, -3d, 4d),
            new UsdVec3d(2d, -3d, 4d));
        double framedRadius =
            ViewerCameraNavigationController.PointBoundsRadius *
            ViewerCameraNavigationController.DefaultFrameMargin;
        double horizontalHalfAngle = Math.Atan(
            Math.Tan(ViewerCameraNavigationState.LegacyVerticalFieldOfView / 2d) *
            (2d / 3d));
        float expectedDistance = (float)(
            framedRadius / Math.Sin(horizontalHalfAngle));

        await Assert.That(controller.FrameBounds(point)).IsTrue();

        await Assert.That(controller.State.Target)
            .IsEqualTo(new Vector3(2f, -3f, 4f));
        await Assert.That(NearlyEqual(
            controller.State.Distance,
            expectedDistance)).IsTrue();
        await Assert.That(
            controller.State.OrthographicHeight >
            ViewerCameraNavigationState.MinimumOrthographicHeight).IsTrue();
        await Assert.That(controller.State.FarPlane > controller.State.NearPlane)
            .IsTrue();
        await Assert.That(IsFinite(controller.Camera)).IsTrue();
    }

    [Test]
    public async Task FrameOrthographicBoundsAccountsForNarrowAspectAndPreservesMode()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(400, 800));
        controller.ResetToExplicitPose();
        controller.ToggleProjection();
        var bounds = new UsdBounds3d(
            new UsdVec3d(-1d, -1d, -1d),
            new UsdVec3d(1d, 1d, 1d));
        double radius = Math.Sqrt(3d);
        float expectedHeight = (float)(
            (2d * radius * ViewerCameraNavigationController.DefaultFrameMargin) /
            0.5d);

        controller.FrameBounds(bounds);

        await Assert.That(controller.State.ProjectionMode)
            .IsEqualTo(ViewerCameraProjectionMode.Orthographic);
        await Assert.That(NearlyEqual(
            controller.State.OrthographicHeight,
            expectedHeight)).IsTrue();
        await Assert.That(controller.State.Target).IsEqualTo(Vector3.Zero);
        await Assert.That(IsFinite(controller.Camera)).IsTrue();
    }

    [Test]
    public async Task FrameHugeFiniteBoundsSaturatesWithoutOverflow()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(int.MaxValue, 1));
        var huge = new UsdBounds3d(
            new UsdVec3d(-8e307, -4e307, -2e307),
            new UsdVec3d(8e307, 4e307, 2e307));

        await Assert.That(controller.FrameBounds(huge, 2f)).IsTrue();

        ViewerCameraNavigationState state = controller.State;
        await Assert.That(state.Distance)
            .IsEqualTo(ViewerCameraNavigationState.MaximumDistance);
        await Assert.That(state.OrthographicHeight)
            .IsEqualTo(ViewerCameraNavigationState.MaximumOrthographicHeight);
        await Assert.That(state.NearPlane)
            .IsEqualTo(ViewerCameraNavigationState.MinimumNearPlane);
        await Assert.That(state.FarPlane)
            .IsEqualTo(ViewerCameraNavigationState.MaximumFarPlane);
        await Assert.That(float.IsFinite(state.AspectRatio)).IsTrue();
        await Assert.That(IsFinite(controller.Camera)).IsTrue();

        controller.ToggleProjection();
        await Assert.That(controller.State.ProjectionMode)
            .IsEqualTo(ViewerCameraProjectionMode.Orthographic);
        Matrix4x4 expectedOrthographic = CreatePinnedGfOrthographic(
            controller.State.OrthographicHeight,
            controller.State.AspectRatio,
            controller.State.NearPlane,
            controller.State.FarPlane);
        await Assert.That(controller.Camera.Projection)
            .IsEqualTo(expectedOrthographic);
        await Assert.That(controller.Camera.Projection.M11 > 0f).IsTrue();
        await Assert.That(IsFinite(controller.Camera)).IsTrue();

        var hugePoint = new UsdBounds3d(
            new UsdVec3d(1e308, -1e308, 5e307),
            new UsdVec3d(1e308, -1e308, 5e307));
        controller.FrameBounds(hugePoint);
        await Assert.That(controller.State.Target).IsEqualTo(
            new Vector3(
                ViewerCameraNavigationState.MaximumTargetComponent,
                -ViewerCameraNavigationState.MaximumTargetComponent,
                ViewerCameraNavigationState.MaximumTargetComponent));
        await Assert.That(IsFinite(controller.Camera)).IsTrue();
    }

    [Test]
    public async Task EmptyBoundsAndZeroDeltasPreserveState()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(800, 600));
        ViewerCameraNavigationState before = controller.State;

        await Assert.That(controller.FrameBounds(UsdBounds3d.Empty)).IsFalse();
        await Assert.That(controller.Orbit(0f, 0f)).IsFalse();
        await Assert.That(controller.Pan(0f, 0f)).IsFalse();
        await Assert.That(controller.Zoom(0f)).IsFalse();
        await Assert.That(controller.State).IsEqualTo(before);
        await Assert.That(controller.Camera).IsEqualTo(CameraState.Default);
    }

    [Test]
    public async Task NonFiniteInputsAreRejectedWithoutChangingState()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(800, 600));
        ViewerCameraNavigationState before = controller.State;
        var bounds = new UsdBounds3d(
            new UsdVec3d(-1d, -1d, -1d),
            new UsdVec3d(1d, 1d, 1d));

        await Assert.That(() => controller.Orbit(float.NaN, 0f))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => controller.Pan(0f, float.PositiveInfinity))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => controller.Zoom(float.NegativeInfinity))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => controller.FrameBounds(bounds, float.NaN))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => controller.FrameBounds(bounds, 0.99f))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => ViewerCameraInputDeltas.CreateOrbitDelta(
            new Vector2(float.NaN, 0f))).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => ViewerCameraInputDeltas.CreateZoomExponent(
            float.PositiveInfinity)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(controller.State).IsEqualTo(before);

        await Assert.That(() => CreateState(target: new Vector3(float.NaN, 0f, 0f)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateState(distance: float.PositiveInfinity))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateState(aspectRatio: float.NaN))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => CreateState(
            projectionMode: (ViewerCameraProjectionMode)int.MaxValue))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task StateClampsFiniteExtremesAndHasValueEquality()
    {
        var first = new ViewerCameraNavigationState(
            isAutomatic: false,
            new Vector3(float.MaxValue, -float.MaxValue, 0f),
            distance: -10f,
            yaw: float.MaxValue,
            pitch: float.MaxValue,
            ViewerCameraProjectionMode.Perspective,
            verticalFieldOfView: -1f,
            orthographicHeight: -1f,
            nearPlane: -1f,
            farPlane: -1f,
            aspectRatio: float.Epsilon);
        var same = new ViewerCameraNavigationState(
            isAutomatic: false,
            new Vector3(float.MaxValue, -float.MaxValue, 0f),
            distance: -10f,
            yaw: float.MaxValue,
            pitch: float.MaxValue,
            ViewerCameraProjectionMode.Perspective,
            verticalFieldOfView: -1f,
            orthographicHeight: -1f,
            nearPlane: -1f,
            farPlane: -1f,
            aspectRatio: float.Epsilon);
        var automatic = new ViewerCameraNavigationState(
            isAutomatic: true,
            first.Target,
            first.Distance,
            first.Yaw,
            first.Pitch,
            first.ProjectionMode,
            first.VerticalFieldOfView,
            first.OrthographicHeight,
            first.NearPlane,
            first.FarPlane,
            first.AspectRatio);

        await Assert.That(first.Target).IsEqualTo(
            new Vector3(
                ViewerCameraNavigationState.MaximumTargetComponent,
                -ViewerCameraNavigationState.MaximumTargetComponent,
                0f));
        await Assert.That(first.Distance)
            .IsEqualTo(ViewerCameraNavigationState.MinimumDistance);
        await Assert.That(first.Pitch)
            .IsEqualTo(ViewerCameraNavigationState.MaximumPitch);
        await Assert.That(first.VerticalFieldOfView)
            .IsEqualTo(ViewerCameraNavigationState.MinimumVerticalFieldOfView);
        await Assert.That(first.OrthographicHeight)
            .IsEqualTo(ViewerCameraNavigationState.MinimumOrthographicHeight);
        await Assert.That(first.NearPlane)
            .IsEqualTo(ViewerCameraNavigationState.MinimumNearPlane);
        await Assert.That(first.FarPlane > first.NearPlane).IsTrue();
        await Assert.That(first.AspectRatio > 0f).IsTrue();
        await Assert.That(first).IsEqualTo(same);
        await Assert.That(first.GetHashCode()).IsEqualTo(same.GetHashCode());
        await Assert.That(first).IsNotEqualTo(automatic);
        await Assert.That(
            RuntimeHelpers.IsReferenceOrContainsReferences<
                ViewerCameraNavigationState>()).IsFalse();
    }

    [Test]
    public async Task InputDeltaHelpersAreDeterministicAndControlAgnostic()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(800, 400));
        controller.ResetToExplicitPose();
        Vector2 orbit = ViewerCameraInputDeltas.CreateOrbitDelta(
            new Vector2(18f, -9f));
        Vector2 pan = ViewerCameraInputDeltas.CreatePanDelta(
            new Vector2(80f, 40f),
            controller.Viewport,
            controller.State);
        float zoom = ViewerCameraInputDeltas.CreateZoomExponent(2f);
        float visibleHeight = controller.State.GetVisibleVerticalSpan();

        await Assert.That(orbit).IsEqualTo(
            new Vector2(
                18f * ViewerCameraInputDeltas.OrbitRadiansPerPixel,
                9f * ViewerCameraInputDeltas.OrbitRadiansPerPixel));
        await Assert.That(NearlyEqual(pan.X, -0.2f * visibleHeight)).IsTrue();
        await Assert.That(NearlyEqual(pan.Y, 0.1f * visibleHeight)).IsTrue();
        await Assert.That(zoom)
            .IsEqualTo(2f * ViewerCameraInputDeltas.ZoomExponentPerWheelUnit);
        await Assert.That(ViewerCameraInputDeltas.CreatePanDelta(
            Vector2.One,
            ViewportDimensions.Empty,
            controller.State)).IsEqualTo(Vector2.Zero);
    }

    [Test]
    public async Task GestureAndMatrixHotPathsAllocateNothingAfterWarmup()
    {
        var controller = new ViewerCameraNavigationController(
            new ViewportDimensions(1280, 720));
        controller.ResetToExplicitPose();
        AllocationWarmup.UntilQuiet(_ => ExerciseHotPaths(controller));

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < AllocationIterations; i++)
        {
            ExerciseHotPaths(controller);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0);
        await Assert.That(float.IsFinite(_allocationSink)).IsTrue();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ExerciseHotPaths(
        ViewerCameraNavigationController controller)
    {
        Vector2 orbit = ViewerCameraInputDeltas.CreateOrbitDelta(
            new Vector2(0.25f, -0.125f));
        controller.Orbit(orbit.X, orbit.Y);
        ViewerCameraNavigationState state = controller.State;
        Vector2 pan = ViewerCameraInputDeltas.CreatePanDelta(
            new Vector2(0.5f, -0.25f),
            controller.Viewport,
            state);
        controller.Pan(pan.X, pan.Y);
        controller.Zoom(0.0001f);
        CameraState camera = controller.Camera;
        controller.Zoom(-0.0001f);
        controller.Pan(-pan.X, -pan.Y);
        controller.Orbit(-orbit.X, -orbit.Y);
        _allocationSink = camera.View.M11 + camera.Projection.M22;
    }

    private static ViewerCameraNavigationState CreateState(
        Vector3? target = null,
        float distance = 1f,
        ViewerCameraProjectionMode projectionMode =
            ViewerCameraProjectionMode.Perspective,
        float aspectRatio = 1f) =>
        new(
            isAutomatic: false,
            target ?? Vector3.Zero,
            distance,
            yaw: 0f,
            pitch: 0f,
            projectionMode,
            verticalFieldOfView: MathF.PI / 4f,
            orthographicHeight: 1f,
            nearPlane: 0.1f,
            farPlane: 1000f,
            aspectRatio);

    private static Vector3 LegacyRight()
    {
        float yaw = ViewerCameraNavigationState.LegacyYaw;
        return new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
    }

    private static Vector3 LegacyUp()
    {
        float yaw = ViewerCameraNavigationState.LegacyYaw;
        float pitch = ViewerCameraNavigationState.LegacyPitch;
        float sinPitch = MathF.Sin(pitch);
        return new Vector3(
            -sinPitch * MathF.Sin(yaw),
            MathF.Cos(pitch),
            -sinPitch * MathF.Cos(yaw));
    }

    private static void AssertPoseExceptAspectEqual(
        in ViewerCameraNavigationState expected,
        in ViewerCameraNavigationState actual)
    {
        if (expected.IsAutomatic != actual.IsAutomatic ||
            expected.Target != actual.Target ||
            expected.Distance != actual.Distance ||
            expected.Yaw != actual.Yaw ||
            expected.Pitch != actual.Pitch ||
            expected.ProjectionMode != actual.ProjectionMode ||
            expected.VerticalFieldOfView != actual.VerticalFieldOfView ||
            expected.OrthographicHeight != actual.OrthographicHeight ||
            expected.NearPlane != actual.NearPlane ||
            expected.FarPlane != actual.FarPlane)
        {
            throw new InvalidOperationException("Resize changed camera pose state.");
        }
    }

    private static bool NearlyEqual(float left, float right) =>
        MathF.Abs(left - right) <=
        Tolerance * MathF.Max(1f, MathF.Max(MathF.Abs(left), MathF.Abs(right)));

    private static bool NearlyEqual(Vector3 left, Vector3 right) =>
        NearlyEqual(left.X, right.X) &&
        NearlyEqual(left.Y, right.Y) &&
        NearlyEqual(left.Z, right.Z);

    private static bool IsFinite(CameraState camera) =>
        IsFinite(camera.View) && IsFinite(camera.Projection);

    private static bool IsFinite(Matrix4x4 value) =>
        float.IsFinite(value.M11) &&
        float.IsFinite(value.M12) &&
        float.IsFinite(value.M13) &&
        float.IsFinite(value.M14) &&
        float.IsFinite(value.M21) &&
        float.IsFinite(value.M22) &&
        float.IsFinite(value.M23) &&
        float.IsFinite(value.M24) &&
        float.IsFinite(value.M31) &&
        float.IsFinite(value.M32) &&
        float.IsFinite(value.M33) &&
        float.IsFinite(value.M34) &&
        float.IsFinite(value.M41) &&
        float.IsFinite(value.M42) &&
        float.IsFinite(value.M43) &&
        float.IsFinite(value.M44);
}
