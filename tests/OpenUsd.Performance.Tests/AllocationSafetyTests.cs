// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Runtime.CompilerServices;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Performance.Tests;

[NotInParallel]
public sealed class AllocationSafetyTests
{
    private const int Iterations = 4_096;
    private const int WarmupIterations = 64;

    [Test]
    public async Task CommandEnumerationDoesNotAllocateAfterWarmup()
    {
        const uint commandCount = 16;
        byte[] page = PerformanceTestData.CreateFramePage((int)commandCount);
        for (int iteration = 0; iteration < WarmupIterations; iteration++)
        {
            _ = ConsumeFrameCommands(page, commandCount);
        }

        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            checksum += ConsumeFrameCommands(page, commandCount);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0L);
        await Assert.That(checksum).IsNotEqualTo(0L);
    }

    [Test]
    public async Task DetachedMatrixMathDoesNotAllocateAfterWarmup()
    {
        var matrix = new UsdMatrix4d(
            1.5, 0.1, 0.2, 0,
            -0.2, 0.75, 0.3, 0,
            0.05, -0.1, 2.25, 0,
            12, -4, 8, 1);
        var point = new UsdVec3d(3.5, -2.25, 7.75);
        for (int iteration = 0; iteration < WarmupIterations; iteration++)
        {
            _ = ConsumeDetachedMath(matrix, point);
        }

        double checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            checksum += ConsumeDetachedMath(matrix, point);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0L);
        await Assert.That(checksum).IsNotEqualTo(0D);
    }

    [Test]
    public async Task CameraProjectionMathDoesNotAllocateAfterWarmup()
    {
        StageRenderState state = StageRenderState.Default;
        var eye = new Vector3(8.5f, 5.25f, 11.75f);
        var target = new Vector3(0.5f, 1.25f, -0.75f);
        for (int iteration = 0; iteration < WarmupIterations; iteration++)
        {
            _ = ConsumeCameraMath(state, eye, target);
        }

        float checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            checksum += ConsumeCameraMath(state, eye, target);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0L);
        await Assert.That(checksum).IsNotEqualTo(0F);
    }

    [Test]
    public async Task FrameOnlySceneApplyDoesNotAllocateAfterWarmup()
    {
        byte[] page = PerformanceTestData.CreateFrameCommand();
        var scene = new SilkSceneState();
        _ = scene.Apply(page, commandCount: 1, revision: 1);
        for (int iteration = 0; iteration < WarmupIterations; iteration++)
        {
            _ = ConsumeFrameApply(scene, page);
        }

        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            checksum += ConsumeFrameApply(scene, page);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0L);
        await Assert.That(checksum).IsNotEqualTo(0L);
    }

    [Test]
    public async Task PickResolutionDoesNotAllocateAfterWarmup()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(
            PerformanceTestData.CreateMeshCommand(triangleCount: 8),
            commandCount: 1,
            revision: 1);
        if (!scene.PickIdentities.TryGetRange(
                "/World/PerformanceMesh",
                out SilkPickTokenRange range))
        {
            throw new InvalidOperationException("The retained pick range is missing.");
        }
        for (int iteration = 0; iteration < WarmupIterations; iteration++)
        {
            _ = ResolvePick(scene.PickIdentities, range.LastToken);
        }

        long checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            checksum += ResolvePick(scene.PickIdentities, range.LastToken);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0L);
        await Assert.That(checksum).IsNotEqualTo(0L);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ConsumeFrameCommands(byte[] page, uint commandCount)
    {
        long checksum = 0;
        using SilkCommandEnumerator commands = SilkCommandParser.Enumerate(page, commandCount);
        while (commands.MoveNext())
        {
            SilkFrameCommand frame = commands.Current.AsFrame();
            checksum += frame.Width + frame.Height;
            checksum += (long)frame.GetViewElement(0);
        }
        return checksum;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double ConsumeDetachedMath(UsdMatrix4d matrix, UsdVec3d point)
    {
        if (!matrix.TryInvert(out UsdMatrix4d inverse))
        {
            throw new InvalidOperationException("The safety-gate matrix must be invertible.");
        }
        UsdVec3d transformed = matrix.TransformPoint(point);
        UsdVec3d restored = inverse.TransformPoint(transformed);
        return restored.X + restored.Y + restored.Z;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float ConsumeCameraMath(
        StageRenderState state,
        Vector3 eye,
        Vector3 target)
    {
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4,
            16f / 9f,
            0.1f,
            2_000f);
        var camera = new CameraState(view, projection);
        StageRenderState unchanged = state.WithCamera(state.Camera);
        if (!ReferenceEquals(state, unchanged))
        {
            throw new InvalidOperationException("A no-op camera update allocated a new state.");
        }
        return camera.View.M11 + camera.Projection.M22;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ConsumeFrameApply(SilkSceneState scene, byte[] page)
    {
        SilkSceneDelta delta = scene.Apply(page, commandCount: 1, revision: 2);
        return scene.Frame.Width + scene.Frame.Height +
            delta.MeshUpserts + delta.MeshRemovals;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ResolvePick(SilkPickIdentityTable table, uint token)
    {
        if (!table.TryResolve(token, out SilkPickIdentity identity))
        {
            throw new InvalidOperationException("The retained pick token did not resolve.");
        }
        return identity.PrimId + identity.SubprimIndex;
    }
}
