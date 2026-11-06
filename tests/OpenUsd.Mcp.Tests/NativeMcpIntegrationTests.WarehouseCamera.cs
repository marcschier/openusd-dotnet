// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using OpenUsd.Geom;
using OpenUsd.Rendering;

namespace OpenUsd.Mcp.Tests;

public sealed partial class NativeMcpIntegrationTests
{
    private static readonly double[] WarehouseCameraSampleTimes = [0, 120, 240];

    [Test]
    [NotInParallel]
    public async Task NativeGeneratedWarehouseCameraPreservesPoseAndProjection()
    {
        string? path = Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_CAMERA_SCENARIO");
        if (string.IsNullOrEmpty(path))
        {
            Skip.Test("Set OPENUSD_WAREHOUSE_CAMERA_SCENARIO to a generated --focused-camera scenario.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("The generated camera scenario path must be absolute.");
        }
        _ = RequireNativeLayout(requireImaging: false);
        byte[] sourceBefore = await File.ReadAllBytesAsync(path);
        UsdMatrix4d transform;
        UsdGeomCameraState optics;
        var cameras = new CameraState[WarehouseCameraSampleTimes.Length];
        bool sourceGeometryAdmitted;
        using (UsdStage stage = UsdStage.OpenMasked(path, ["/WarehouseDemoCamera"]))
        {
            UsdGeomCamera camera = UsdGeomCamera.Wrap(stage.GetPrim("/WarehouseDemoCamera"));
            transform = camera.GetTransform();
            optics = camera.GetState();
            sourceGeometryAdmitted = stage.HasPrim("/World");
            for (int index = 0; index < cameras.Length; index++)
            {
                cameras[index] = CameraState.FromStageCamera(
                    stage, "/WarehouseDemoCamera", WarehouseCameraSampleTimes[index], 1920, 1080);
            }
        }
        await Assert.That(sourceGeometryAdmitted).IsFalse()
            .Because("this is an imported-camera optics check, not a complete scene or render qualification");
        await Assert.That(transform.ExtractTranslation())
            .IsEqualTo(new UsdVec3d(-20.1080493927, -39.1122589111, 4.9));
        await Assert.That(transform.TryInvert(out UsdMatrix4d view)).IsTrue();
        var target = new UsdVec3d(-24.6080493927, -43.1122589111, 1.9);
        UsdVec3d viewTarget = view.TransformPoint(target);
        await Assert.That(viewTarget.X).IsEqualTo(0d).Within(1e-12);
        await Assert.That(viewTarget.Y).IsEqualTo(0d).Within(1e-12);
        await Assert.That(viewTarget.Z).IsLessThan(0d);
        await Assert.That(optics.Projection).IsEqualTo(UsdGeomCameraProjection.Perspective);
        await Assert.That(optics.HorizontalAperture / optics.VerticalAperture).IsEqualTo(16d / 9);
        await Assert.That(2 * Math.Atan(optics.VerticalAperture / (2 * optics.FocalLength)))
            .IsEqualTo(Math.PI / 4).Within(1e-7);
        await Assert.That(optics.ClippingNear).IsEqualTo(0.05).Within(1e-8);
        await Assert.That(optics.ClippingFar).IsEqualTo(20d);
        foreach (CameraState camera in cameras)
        {
            Vector4 clip = Vector4.Transform(
                new Vector4((float)target.X, (float)target.Y, (float)target.Z, 1),
                camera.View * camera.Projection);
            await Assert.That(clip.W).IsGreaterThan(0f);
            await Assert.That(clip.X / clip.W).IsEqualTo(0f).Within(1e-5f);
            await Assert.That(clip.Y / clip.W).IsEqualTo(0f).Within(1e-5f);
            await Assert.That(clip.Z / clip.W).IsGreaterThan(0f);
            await Assert.That(clip.Z / clip.W).IsLessThan(1f);
            await Assert.That(camera.View).IsEqualTo(cameras[0].View);
            await Assert.That(camera.Projection).IsEqualTo(cameras[0].Projection);
        }
        await Assert.That((await File.ReadAllBytesAsync(path)).SequenceEqual(sourceBefore)).IsTrue();
        if (Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_CAMERA_EVIDENCE") is { Length: > 0 } output)
        {
            if (!Path.IsPathFullyQualified(output))
            {
                throw new ArgumentException("The camera evidence path must be absolute.");
            }
            await using FileStream evidence = new(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(evidence, new
            {
                scope = "Native camera-only population mask; no warehouse geometry or rendering qualified.",
                scenarioSha256 = Convert.ToHexString(SHA256.HashData(sourceBefore)),
                cameraPath = "/WarehouseDemoCamera",
                transform = transform.ToArray(),
                optics,
                times = WarehouseCameraSampleTimes,
                target,
                viewTarget
            });
        }
    }
}
