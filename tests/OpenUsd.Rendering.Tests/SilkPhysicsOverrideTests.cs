// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkPhysicsOverrideTests
{
    [Test]
    public async Task EqualPageGeometrySharesOnlyImmutableArraysAcrossDistinctPrimIdentities()
    {
        var scene = new SilkSceneState();
        byte[] first = CreateMeshCommand(path: "/First", primId: 7);
        byte[] second = CreateMeshCommand(path: "/Second", primId: 8);
        _ = scene.Apply([.. first, .. second], 2, 1);
        SilkMeshData a = scene.Meshes[7];
        SilkMeshData b = scene.Meshes[8];
        await Assert.That(a.Path).IsEqualTo("/First");
        await Assert.That(b.Path).IsEqualTo("/Second");
        await Assert.That(a.Id).IsNotEqualTo(b.Id);
        await Assert.That(SameArray(a.Points, b.Points)).IsTrue();
        await Assert.That(SameArray(a.Indices, b.Indices)).IsTrue();
        await Assert.That(SameArray(a.TriangleSubprims, b.TriangleSubprims)).IsTrue();
        Array.Clear(first);
        Array.Clear(second);
        await Assert.That(a.Points.ToArray()).IsEquivalentTo(b.Points.ToArray());
        float[] changed = a.Points.ToArray();
        changed[0] += 1;
        SilkMeshData simulated = a.WithSimulatedPoints(changed);
        await Assert.That(simulated.Points.Span[0]).IsEqualTo(b.Points.Span[0] + 1);
        await Assert.That(SameArray(simulated.Points, b.Points)).IsFalse();
    }

    private static bool SameArray<T>(ReadOnlyMemory<T> left, ReadOnlyMemory<T> right) =>
        MemoryMarshal.TryGetArray(left, out ArraySegment<T> a) &&
        MemoryMarshal.TryGetArray(right, out ArraySegment<T> b) && ReferenceEquals(a.Array, b.Array);

    [Test]
    public async Task ImmutableArraySharingPreservesExactFloatBitsAndChangedTopology()
    {
        var arrays = new SilkMeshArrayPool();
        float[] first = [1, 0, float.NaN];
        float[] identical = [1, 0, float.NaN];
        float[] negativeZero = [1, BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)), float.NaN];
        float[] original = arrays.Floats.Share(first);
        await Assert.That(arrays.Floats.Share(identical)).IsSameReferenceAs(original);
        await Assert.That(arrays.Floats.Share(negativeZero)).IsSameReferenceAs(negativeZero);
        uint[] topology = [0, 1, 2];
        uint[] changed = [0, 2, 1];
        await Assert.That(arrays.Indices.Share(topology)).IsSameReferenceAs(topology);
        await Assert.That(arrays.Indices.Share(changed)).IsSameReferenceAs(changed);
        await Assert.That(arrays.Indices.Share([0, 1, 2])).IsSameReferenceAs(topology);
    }

    [Test]
    public async Task SubtreeBodyMotionPreservesEveryChildOffsetAndAuthoredStretch()
    {
        var scene = new SilkSceneState();
        double[] first = (double[])AuthoredTransform.Clone();
        first[0] = 2;
        first[1] = 0.25;
        first[12] = 12;
        first[13] = first[14] = 0;
        double[] second = (double[])AuthoredTransform.Clone();
        second[12] = 8;
        second[13] = second[14] = 0;
        _ = scene.Apply(CreateMeshCommand(path: "/Body/First", primId: 7, transform: first), 1, 1);
        _ = scene.Apply(CreateMeshCommand(path: "/Body/Second", primId: 8, transform: second), 1, 2);
        _ = scene.Apply(CreateMeshCommand(path: "/BodyAdjacent/Mesh", primId: 9), 1, 3);
        var id = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
        var bindings = new PhysicsRenderBindingTable(1);
        _ = bindings.TryBindSubtree(id, "/Body", new UsdVec3d(10, 0, 0), PhysicsRenderOrientation.Identity);
        var overrides = new SilkPhysicsTransformOverrides(3);
        var value = new PhysicsRenderTransformOverride(id, new UsdVec3d(20, 0, 0),
            new PhysicsRenderOrientation(0, 0, Math.Sqrt(0.5), Math.Sqrt(0.5)), true);
        _ = overrides.Refresh(scene, bindings, new PhysicsRenderOverrideView(new[] { value }, 1));
        await Assert.That(overrides.Count).IsEqualTo(2);
        await Assert.That(overrides.ResolvedOverrideCount).IsEqualTo(1);
        await Assert.That(overrides.Contains(9)).IsFalse();
        double[] actual = overrides.GetTransform(7).ToArray();
        await Assert.That(actual[12]).IsEqualTo(20d).Within(1e-10);
        await Assert.That(actual[13]).IsEqualTo(2d).Within(1e-10);
        await Assert.That(actual[0]).IsEqualTo(-0.25d).Within(1e-10);
        await Assert.That(actual[1]).IsEqualTo(2d).Within(1e-10);
        await Assert.That(overrides.GetTransform(8)[13]).IsEqualTo(-2d).Within(1e-10);
    }

    [Test]
    [Arguments(1, 0)]
    [Arguments(2, 2)]
    public async Task SubtreeCapacityAppliesToTheWholeBody(int capacity, int expectedMeshes)
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(path: "/Body/First", primId: 7), 1, 1);
        _ = scene.Apply(CreateMeshCommand(path: "/Body/Second", primId: 8), 1, 2);
        var id = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
        var bindings = new PhysicsRenderBindingTable(1);
        _ = bindings.TryBindSubtree(id, "/Body", default, PhysicsRenderOrientation.Identity);
        var overrides = new SilkPhysicsTransformOverrides(capacity);
        _ = overrides.Refresh(scene, bindings, CreateView(id, 1, 2, 3));
        await Assert.That(overrides.Count).IsEqualTo(expectedMeshes);
        await Assert.That(overrides.ResolvedOverrideCount).IsEqualTo(expectedMeshes == 0 ? 0 : 1);
        await Assert.That(overrides.DroppedOverrides).IsEqualTo(expectedMeshes == 0 ? 1L : 0L);
    }

    [Test]
    public async Task SubtreeRejectsInstancedDescendantsWithoutApplyingTheOtherChildren()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(path: "/Body/First", primId: 7), 1, 1);
        _ = scene.Apply(CreateMeshCommand(path: "/Body/Instanced", primId: 8, instanceIndex: 1), 1, 2);
        var id = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
        var bindings = new PhysicsRenderBindingTable(1);
        _ = bindings.TryBindSubtree(id, "/Body", default, PhysicsRenderOrientation.Identity);
        var overrides = new SilkPhysicsTransformOverrides(3);
        _ = overrides.Refresh(scene, bindings, CreateView(id, 1, 2, 3));
        await Assert.That(overrides.Count).IsEqualTo(0);
        await Assert.That(overrides.ResolvedOverrideCount).IsEqualTo(0);
        await Assert.That(overrides.UnresolvedOverrides).IsEqualTo(1L);
    }

    [Test]
    public async Task SubtreeTargetsAreRebuiltAfterRollbackEvenWhenGeometryRevisionIsReused()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(path: "/Body/First", primId: 7), 1, 1);
        var id = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
        var bindings = new PhysicsRenderBindingTable(1);
        _ = bindings.TryBindSubtree(id, "/Body", default, PhysicsRenderOrientation.Identity);
        var overrides = new SilkPhysicsTransformOverrides(3);
        PhysicsRenderOverrideView view = CreateView(id, 1, 2, 3);
        ulong speculativeRevision;
        int speculativeCount;
        using (var page = new OpenUsdSilkPage(30, 2, CreateMeshCommand(path: "/Outside", primId: 8), 1))
        using (SilkSceneState.PreparedPage transaction = scene.PreparePage(page))
        {
            speculativeRevision = scene.GeometryRevision;
            _ = overrides.Refresh(scene, bindings, view);
            speculativeCount = overrides.Count;
        }
        await Assert.That(speculativeCount).IsEqualTo(1);
        _ = scene.Apply(CreateMeshCommand(path: "/Body/Second", primId: 8), 1, 2);
        await Assert.That(scene.GeometryRevision).IsEqualTo(speculativeRevision);
        _ = overrides.Refresh(scene, bindings, view);
        await Assert.That(overrides.Count).IsEqualTo(2);
        await Assert.That(overrides.Contains(8)).IsTrue();
    }

    [Test]
    public async Task WarmSubtreeResolutionAllocatesNoManagedStorage()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(path: "/Body/First", primId: 7), 1, 1);
        _ = scene.Apply(CreateMeshCommand(path: "/Body/Second", primId: 8), 1, 2);
        var id = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
        var bindings = new PhysicsRenderBindingTable(1);
        _ = bindings.TryBindSubtree(id, "/Body", default, PhysicsRenderOrientation.Identity);
        var overrides = new SilkPhysicsTransformOverrides(3);
        PhysicsRenderOverrideView view = CreateView(id, 1, 2, 3);
        long allocated = MeasureWarmAllocations(() =>
        {
            overrides.Stage(bindings, view);
            _ = overrides.RefreshStaged(scene);
        });
        await Assert.That(allocated).IsEqualTo(0L);
        await Assert.That(overrides.Count).IsEqualTo(2);
        await Assert.That(overrides.ResolvedOverrideCount).IsEqualTo(1);
    }

    [Test]
    public async Task ConflictingSubtreeAndDirectBindingsDoNotMoveTheSharedMesh()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(path: "/Body/Mesh", primId: 7), 1, 1);
        var parent = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
        var child = new PhysicsRenderObjectId(102, PhysicsRenderObjectKind.RigidBody);
        var bindings = new PhysicsRenderBindingTable(2);
        _ = bindings.TryBindSubtree(parent, "/Body", default, PhysicsRenderOrientation.Identity);
        _ = bindings.TryBind(child, "/Body/Mesh");
        var overrides = new SilkPhysicsTransformOverrides(2);
        _ = overrides.Refresh(scene, bindings, CreateView([(parent, 1, 2, 3), (child, 4, 5, 6)]));
        await Assert.That(overrides.Count).IsEqualTo(0);
        await Assert.That(overrides.ResolvedOverrideCount).IsEqualTo(0);
        await Assert.That(overrides.UnresolvedOverrides).IsEqualTo(2L);
    }

    [Test]
    public async Task RefusedStageKeepsThePreviousPendingPublication()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(), 1, 1);
        var id = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
        var bindings = new PhysicsRenderBindingTable(1);
        _ = bindings.TryBind(id, "/Triangle");
        var overrides = new SilkPhysicsTransformOverrides(1);
        overrides.Stage(bindings, CreateView(id, 5, 6, 7));
        await Assert.That(() => overrides.Stage(bindings, CreateView([(id, 9, 9, 9), (id, 8, 8, 8)])))
            .Throws<ArgumentOutOfRangeException>();
        _ = overrides.RefreshStaged(scene);
        await Assert.That(overrides.GetTransform(7)[12]).IsEqualTo(5d);
    }

    [Test]
    public async Task ChangedStagedBindingsDoNotReplaceResolvedTransforms()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(), 1, 1);
        var id = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
        var bindings = new PhysicsRenderBindingTable(1);
        _ = bindings.TryBind(id, "/Triangle");
        var overrides = new SilkPhysicsTransformOverrides(1);
        _ = overrides.Refresh(scene, bindings, CreateView(id, 1, 2, 3));
        overrides.Stage(bindings, CreateView(id, 5, 6, 7));
        bindings.Clear();
        await Assert.That(() => overrides.RefreshStaged(scene)).Throws<InvalidOperationException>();
        await Assert.That(overrides.GetTransform(7)[12]).IsEqualTo(1d);
    }

    [Test]
    public async Task WarmStagingAndPostSyncResolutionAllocateNoManagedStorage()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(), 1, 1);
        var id = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
        var bindings = new PhysicsRenderBindingTable(1);
        _ = bindings.TryBind(id, "/Triangle");
        var overrides = new SilkPhysicsTransformOverrides(1);
        PhysicsRenderOverrideView view = CreateView(id, 5, 6, 7);
        long allocated = MeasureWarmAllocations(() =>
        {
            overrides.Stage(bindings, view);
            _ = overrides.RefreshStaged(scene);
        });
        await Assert.That(allocated).IsEqualTo(0L);
    }

    /// <summary>
    /// Measures 1,000 calls of a hot path after it stops allocating.
    /// </summary>
    /// <remarks>
    /// A fixed warm-up count cannot absorb tier-1 promotion, which compiles asynchronously after
    /// about thirty calls and can land inside the measured loop on a contended runner.
    /// <c>WarmSubtreeResolutionAllocatesNoManagedStorage</c> reported 3,048 bytes on hosted
    /// Windows that way. Waiting for four consecutive quiet blocks, and measuring a second time
    /// when the first measurement allocates, keeps the assertion at exactly zero: a path that
    /// really allocates allocates in both measurements.
    /// </remarks>
    private static long MeasureWarmAllocations(Action action)
    {
        long allocated = 0;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            int quietBlocks = 0;
            for (int block = 0; block < 50 && quietBlocks < 4; block++)
            {
                long blockBefore = GC.GetAllocatedBytesForCurrentThread();
                for (int index = 0; index < 200; index++)
                {
                    action();
                }
                quietBlocks = GC.GetAllocatedBytesForCurrentThread() == blockBefore ? quietBlocks + 1 : 0;
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 1000; index++)
            {
                action();
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocated == 0)
            {
                return 0;
            }
        }
        return allocated;
    }

    [Test]
    public async Task StagedPublicationUsesCurrentAuthoredStretchAndOwnsItsPoseValues()
    {
        var scene = new SilkSceneState();
        var id = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
        var bindings = new PhysicsRenderBindingTable(1);
        _ = bindings.TryBind(id, "/Triangle");
        PhysicsRenderTransformOverride[] poses =
        [
            new(id, new UsdVec3d(5, 6, 7), PhysicsRenderOrientation.Identity, true)
        ];
        var overrides = new SilkPhysicsTransformOverrides(1);
        overrides.Stage(bindings, new PhysicsRenderOverrideView(poses, 17));
        poses[0] = poses[0] with { Position = new UsdVec3d(99, 99, 99) };
        await Assert.That(overrides.SourceRevision).IsEqualTo(0ul);
        await Assert.That(overrides.HasStaged).IsTrue();
        double[] authored = (double[])AuthoredTransform.Clone();
        authored[0] = 2;
        _ = scene.Apply(CreateMeshCommand(transform: authored), 1, 1);
        await Assert.That(overrides.RefreshStaged(scene)).IsEqualTo(1);
        await Assert.That(overrides.SourceRevision).IsEqualTo(17ul);
        await Assert.That(overrides.GetTransform(7)[0]).IsEqualTo(2d);
        await Assert.That(overrides.GetTransform(7)[12]).IsEqualTo(5d);
        authored[0] = 3;
        _ = scene.Apply(CreateMeshCommand(transform: authored), 1, 2);
        _ = overrides.RefreshStaged(scene);
        await Assert.That(overrides.GetTransform(7)[0]).IsEqualTo(3d);
        overrides.Clear();
        await Assert.That(overrides.HasStaged).IsFalse();
        await Assert.That(overrides.SourceRevision).IsEqualTo(0ul);
        await Assert.That(overrides.RefreshStaged(scene)).IsEqualTo(0);
    }

    private static readonly double[] AuthoredTransform =
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        2, 3, 4, 1
    ];

    [Test]
    public async Task ResolvesOverridesOntoRetainedMeshes()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(), 1, 1);
        var bindings = new PhysicsRenderBindingTable(4);
        var id = new PhysicsRenderObjectId(101, PhysicsRenderObjectKind.RigidBody);
        _ = bindings.TryBind(id, "/Triangle");
        var overrides = new SilkPhysicsTransformOverrides(4);

        int count = overrides.Refresh(scene, bindings, CreateView(id, 5, 6, 7));

        await Assert.That(count).IsEqualTo(1);
        await Assert.That(overrides.HasOverrides).IsTrue();
        await Assert.That(overrides.Contains(7ul)).IsTrue();
        double[] transform = overrides.GetTransform(7).ToArray();
        await Assert.That(transform.Length).IsEqualTo(16);
        await Assert.That(transform[12]).IsEqualTo(5d);
        await Assert.That(transform[13]).IsEqualTo(6d);
        await Assert.That(transform[14]).IsEqualTo(7d);
        await Assert.That(overrides.UnresolvedOverrides).IsEqualTo(0L);
        await Assert.That(overrides.GetTransform(999).IsEmpty).IsTrue();
    }

    [Test]
    public async Task UnboundAndMissingEntitiesAreCountedAndDiagnosed()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(), 1, 1);
        var bindings = new PhysicsRenderBindingTable(4);
        var unbound = new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.RigidBody);
        var deleted = new PhysicsRenderObjectId(2, PhysicsRenderObjectKind.RigidBody);
        _ = bindings.TryBind(deleted, "/Deleted");
        var overrides = new SilkPhysicsTransformOverrides(4);

        int count = overrides.Refresh(
            scene,
            bindings,
            CreateView([(unbound, 1, 1, 1), (deleted, 2, 2, 2)]));
        var diagnostics = new List<RenderDiagnostic>();
        overrides.CollectDiagnostics(diagnostics);

        await Assert.That(count).IsEqualTo(0);
        await Assert.That(overrides.UnresolvedOverrides).IsEqualTo(2L);
        await Assert.That(diagnostics.Count).IsEqualTo(1);
        await Assert.That(diagnostics[0].Code)
            .IsEqualTo(PhysicsRenderDiagnosticCodes.OverrideUnresolved);
        await Assert.That(diagnostics[0].Severity)
            .IsEqualTo(RenderDiagnosticSeverity.Information);
    }

    [Test]
    public async Task OverflowIsDroppedAndDiagnosedWithoutStoppingSupportedMeshes()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(), 1, 1);
        _ = scene.Apply(CreateMeshCommand(path: "/Second", primId: 8), 1, 2);
        var bindings = new PhysicsRenderBindingTable(4);
        var first = new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.RigidBody);
        var second = new PhysicsRenderObjectId(2, PhysicsRenderObjectKind.RigidBody);
        _ = bindings.TryBind(first, "/Triangle");
        _ = bindings.TryBind(second, "/Second");
        var overrides = new SilkPhysicsTransformOverrides(1);

        int count = overrides.Refresh(
            scene,
            bindings,
            CreateView([(first, 1, 1, 1), (second, 2, 2, 2)]));
        var diagnostics = new List<RenderDiagnostic>();
        overrides.CollectDiagnostics(diagnostics);

        await Assert.That(count).IsEqualTo(1);
        await Assert.That(overrides.Contains(7ul)).IsTrue();
        await Assert.That(overrides.Contains(8ul)).IsFalse();
        await Assert.That(overrides.DroppedOverrides).IsEqualTo(1L);
        await Assert.That(diagnostics[0].Code)
            .IsEqualTo(PhysicsRenderDiagnosticCodes.OverrideCapacityExceeded);
        await Assert.That(diagnostics[0].Severity).IsEqualTo(RenderDiagnosticSeverity.Warning);
    }

    [Test]
    public async Task PointInstancedIdentitiesResolveIndependently()
    {
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(), 1, 1);
        _ = scene.Apply(CreateMeshCommand(instanceIndex: 1), 1, 2);
        var bindings = new PhysicsRenderBindingTable(4);
        var prototype = new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.RigidBody);
        var instance = new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.RigidBody, 1);
        _ = bindings.TryBind(prototype, "/Triangle");
        _ = bindings.TryBind(instance, "/Triangle", 1);
        var overrides = new SilkPhysicsTransformOverrides(4);

        int count = overrides.Refresh(
            scene,
            bindings,
            CreateView([(prototype, 1, 0, 0), (instance, 9, 0, 0)]));

        ulong instanceKey = scene.MeshesByPath[("/Triangle", 1)].Id;
        await Assert.That(count).IsEqualTo(2);
        await Assert.That(overrides.GetTransform(7)[12]).IsEqualTo(1d);
        await Assert.That(overrides.GetTransform(instanceKey)[12]).IsEqualTo(9d);
        await Assert.That(instanceKey).IsNotEqualTo(7ul);
    }

    [Test]
    public async Task AuthoredScaleSurvivesTheOverride()
    {
        double[] scaled =
        [
            2, 0, 0, 0,
            0, 2, 0, 0,
            0, 0, 2, 0,
            0, 0, 0, 1
        ];
        var scene = new SilkSceneState();
        _ = scene.Apply(CreateMeshCommand(transform: scaled), 1, 1);
        var bindings = new PhysicsRenderBindingTable(2);
        var id = new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.RigidBody);
        _ = bindings.TryBind(id, "/Triangle");
        var overrides = new SilkPhysicsTransformOverrides(2);

        _ = overrides.Refresh(scene, bindings, CreateView(id, 1, 2, 3));

        double[] transform = overrides.GetTransform(7).ToArray();
        await Assert.That(transform[0]).IsEqualTo(2d);
        await Assert.That(transform[5]).IsEqualTo(2d);
        await Assert.That(transform[10]).IsEqualTo(2d);
        await Assert.That(transform[12]).IsEqualTo(1d);
    }

    [Test]
    public async Task UniformUploadUsesTheOverrideTransformAndClearRestoresAuthored()
    {
        var scene = new SilkSceneState();
        using var device = new TestGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device);
        byte[] frame = CreateFrameCommand();
        _ = scene.Apply(frame, 1, 1);
        SilkSceneDelta delta = scene.Apply(CreateMeshCommand(), 1, 2);
        resources.Apply(scene, delta);
        _ = resources.UpdateUniforms(scene.Frame);
        byte[] authored = ReadUniform(resources);

        var bindings = new PhysicsRenderBindingTable(2);
        var id = new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.RigidBody);
        _ = bindings.TryBind(id, "/Triangle");
        var overrides = new SilkPhysicsTransformOverrides(2);
        _ = overrides.Refresh(scene, bindings, CreateView(id, 20, 30, 40));
        int overriddenUploads = resources.UpdateUniforms(scene.Frame, overrides);
        byte[] overridden = ReadUniform(resources);

        overrides.Clear();
        int restoredUploads = resources.UpdateUniforms(scene.Frame, overrides);
        byte[] restored = ReadUniform(resources);

        var expected = new byte[SilkSceneUniformWriter.ByteSize];
        SilkSceneUniformWriter.Write(
            scene.Meshes[7],
            scene.Frame,
            expected,
            ((ISilkGraphicsDevice)device).ClipSpaceYPointsDown,
            overrideTransformFor(20, 30, 40));

        await Assert.That(overriddenUploads).IsEqualTo(1);
        await Assert.That(restoredUploads).IsEqualTo(1);
        await Assert.That(overridden).IsNotEquivalentTo(authored);
        await Assert.That(overridden).IsEquivalentTo(expected);
        await Assert.That(restored).IsEquivalentTo(authored);

        static double[] overrideTransformFor(double x, double y, double z)
        {
            var composed = new double[PhysicsRenderTransforms.ElementCount];
            PhysicsRenderTransforms.Compose(
                new UsdVec3d(x, y, z),
                PhysicsRenderOrientation.Identity,
                AuthoredTransform,
                composed);
            return composed;
        }
    }

    [Test]
    public async Task UnoverriddenMeshesKeepTheirAuthoredUniforms()
    {
        var scene = new SilkSceneState();
        using var device = new TestGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device);
        _ = scene.Apply(CreateFrameCommand(), 1, 1);
        SilkSceneDelta first = scene.Apply(CreateMeshCommand(), 1, 2);
        resources.Apply(scene, first);
        SilkSceneDelta second = scene.Apply(CreateMeshCommand(path: "/Second", primId: 8), 1, 3);
        resources.Apply(scene, second);
        _ = resources.UpdateUniforms(scene.Frame);
        byte[] authoredSecond = ReadUniform(resources, 8);

        var bindings = new PhysicsRenderBindingTable(2);
        var id = new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.RigidBody);
        _ = bindings.TryBind(id, "/Triangle");
        var overrides = new SilkPhysicsTransformOverrides(2);
        _ = overrides.Refresh(scene, bindings, CreateView(id, 20, 30, 40));
        _ = resources.UpdateUniforms(scene.Frame, overrides);

        await Assert.That(ReadUniform(resources, 8)).IsEquivalentTo(authoredSecond);
        await Assert.That(ReadUniform(resources, 7)).IsNotEquivalentTo(authoredSecond);
    }

    [Test]
    public async Task SteadyOverriddenFrameDoesNotUploadTwice()
    {
        var scene = new SilkSceneState();
        using var device = new TestGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device);
        _ = scene.Apply(CreateFrameCommand(), 1, 1);
        SilkSceneDelta delta = scene.Apply(CreateMeshCommand(), 1, 2);
        resources.Apply(scene, delta);
        var bindings = new PhysicsRenderBindingTable(2);
        var id = new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.RigidBody);
        _ = bindings.TryBind(id, "/Triangle");
        var overrides = new SilkPhysicsTransformOverrides(2);
        _ = overrides.Refresh(scene, bindings, CreateView(id, 20, 30, 40));

        int firstUploads = resources.UpdateUniforms(scene.Frame, overrides);
        int steadyUploads = resources.UpdateUniforms(scene.Frame, overrides);

        await Assert.That(firstUploads).IsEqualTo(1);
        await Assert.That(steadyUploads).IsEqualTo(0);
    }

    [Test]
    [Arguments(PhysicsRenderDomain.RigidBody, true)]
    [Arguments(PhysicsRenderDomain.Articulation, true)]
    [Arguments(PhysicsRenderDomain.Controller, true)]
    [Arguments(PhysicsRenderDomain.Vehicle, true)]
    [Arguments(PhysicsRenderDomain.Particles, false)]
    [Arguments(PhysicsRenderDomain.Cloth, false)]
    [Arguments(PhysicsRenderDomain.Deformable, false)]
    public async Task DomainSupportIsReportedPerDomain(PhysicsRenderDomain domain, bool supported) =>
        await Assert.That(SilkPhysicsTransformOverrides.IsDomainSupported(domain))
            .IsEqualTo(supported);

    [Test]
    public async Task UnsupportedDomainsAreDiagnosedIndividually()
    {
        var channel = new PhysicsRenderChannel(new PhysicsRenderCapacities(4, 1, 3));
        var interpolator = new PhysicsRenderInterpolator(new PhysicsRenderCapacities(4, 1, 3));
        PhysicsRenderSnapshot snapshot = channel.TryBeginWrite()!;
        snapshot.BeginWrite(1, 1, 0.5, 0.5, 1.0 / 60);
        _ = snapshot.TryAddBody(PhysicsRenderSnapshotTests.Body(1, 1, 1, 1));
        _ = snapshot.TryAddDeformable(
            new PhysicsRenderObjectId(2, PhysicsRenderObjectKind.Deformable),
            PhysicsRenderDomain.Cloth,
            [0, 0, 0],
            topologyRevision: 1);
        snapshot.EndWrite();
        _ = channel.Publish(snapshot);
        _ = interpolator.TryIngest(channel);
        _ = interpolator.Update(0.5);

        PhysicsRenderDomainReport rigid = SilkPhysicsTransformOverrides.Describe(
            interpolator,
            PhysicsRenderDomain.RigidBody);
        PhysicsRenderDomainReport cloth = SilkPhysicsTransformOverrides.Describe(
            interpolator,
            PhysicsRenderDomain.Cloth);

        await Assert.That(rigid.IsRenderable).IsTrue();
        await Assert.That(cloth.Status).IsEqualTo(PhysicsRenderDomainStatus.Unsupported);
        await Assert.That(cloth.ToDiagnostic()!.Code)
            .IsEqualTo(PhysicsRenderDiagnosticCodes.DomainUnsupported);
        await Assert.That(interpolator.Overrides.Count).IsEqualTo(1);
    }

    [Test]
    public async Task WarmedOverriddenUniformUpdateDoesNotAllocate()
    {
        var scene = new SilkSceneState();
        using var device = new TestGraphicsDevice();
        using var resources = new SilkSceneGpuResources(device);
        _ = scene.Apply(CreateFrameCommand(), 1, 1);
        SilkSceneDelta delta = scene.Apply(CreateMeshCommand(), 1, 2);
        resources.Apply(scene, delta);
        var bindings = new PhysicsRenderBindingTable(2);
        var id = new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.RigidBody);
        _ = bindings.TryBind(id, "/Triangle");
        var overrides = new SilkPhysicsTransformOverrides(2);
        var buffer = new PhysicsRenderTransformOverride[1];
        const int warmupIterations = 32;
        const int measuredIterations = 1000;
        const int maximumMeasuredPasses = 8;
        const int requiredConsecutiveZeroPasses = 2;

        void run(int iterations, int seed)
        {
            for (int index = 0; index < iterations; index++)
            {
                buffer[0] = new PhysicsRenderTransformOverride(
                    id,
                    new UsdVec3d(seed + index, 0, 0),
                    PhysicsRenderOrientation.Identity,
                    Snapped: false);
                _ = overrides.Refresh(
                    scene,
                    bindings,
                    new PhysicsRenderOverrideView(buffer, (ulong)(seed + index)));
                _ = resources.UpdateUniforms(scene.Frame, overrides);
            }
        }

        run(warmupIterations, 1);

        int consecutiveZeroPasses = 0;
        long allocated = 0;
        for (int pass = 0; pass < maximumMeasuredPasses; pass++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            run(measuredIterations, 1_000 + (pass * measuredIterations));
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            consecutiveZeroPasses = allocated == 0 ? consecutiveZeroPasses + 1 : 0;
            if (consecutiveZeroPasses == requiredConsecutiveZeroPasses)
            {
                break;
            }
        }

        await Assert.That(consecutiveZeroPasses).IsEqualTo(requiredConsecutiveZeroPasses);
        await Assert.That(allocated).IsEqualTo(0L);
    }

    private static byte[] ReadUniform(SilkSceneGpuResources resources, ulong meshId = 7)
    {
        var bytes = new byte[SilkSceneUniformWriter.ByteSize];
        resources.Meshes[meshId].UniformBuffer.ReadbackForTesting(bytes);
        return bytes;
    }

    private static PhysicsRenderOverrideView CreateView(
        PhysicsRenderObjectId id,
        double x,
        double y,
        double z) =>
        CreateView([(id, x, y, z)]);

    private static PhysicsRenderOverrideView CreateView(
        (PhysicsRenderObjectId Id, double X, double Y, double Z)[] items)
    {
        var overrides = new PhysicsRenderTransformOverride[items.Length];
        for (int index = 0; index < items.Length; index++)
        {
            (PhysicsRenderObjectId id, double x, double y, double z) = items[index];
            overrides[index] = new PhysicsRenderTransformOverride(
                id,
                new UsdVec3d(x, y, z),
                PhysicsRenderOrientation.Identity,
                Snapped: false);
        }

        return new PhysicsRenderOverrideView(overrides, 1);
    }

    private static byte[] CreateFrameCommand()
    {
        var bytes = new byte[272];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 64);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), 64);
        for (int i = 0; i < 16; i++)
        {
            double value = i % 5 == 0 ? 1 : 0;
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(16 + (i * 8)), value);
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(144 + (i * 8)), value);
        }
        return bytes;
    }

    private static byte[] CreateMeshCommand(
        string path = "/Triangle",
        int primId = 7,
        int instanceIndex = 0,
        double[]? transform = null)
    {
        byte[] pathBytes = Encoding.UTF8.GetBytes(path);
        byte[] instancerPathBytes = instanceIndex == 0
            ? []
            : Encoding.UTF8.GetBytes("/Instancer");
        float[] points = [-0.5f, -0.5f, 0, 0, 0.5f, 0, 0.5f, -0.5f, 0];
        uint[] indices = [0, 1, 2];
        transform ??= AuthoredTransform;
        int size = 268 +
            pathBytes.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            sizeof(uint) +
            instancerPathBytes.Length +
            (instancerPathBytes.Length == 0 ? 0 : 8 + instancerPathBytes.Length);
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(path));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), primId);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), instanceIndex == 0 ? 0 : 11);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), instanceIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), (uint)pathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 1);
        for (int i = 0; i < 4; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(64 + (i * 4)), 1);
        }
        for (int i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(80 + (i * 8)), transform[i]);
        }
        pathBytes.CopyTo(bytes, 268);
        int pointsOffset = 268 + pathBytes.Length;
        for (int i = 0; i < points.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(pointsOffset + (i * sizeof(float))),
                points[i]);
        }
        int indicesOffset = pointsOffset + (points.Length * sizeof(float));
        for (int i = 0; i < indices.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(indicesOffset + (i * sizeof(uint))),
                indices[i]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(indicesOffset + (indices.Length * sizeof(uint))),
            0);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(260),
            (uint)instancerPathBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(264),
            instancerPathBytes.Length == 0 ? 0u : 1u);
        int instancerPathOffset =
            indicesOffset + (indices.Length * sizeof(uint)) + sizeof(uint);
        instancerPathBytes.CopyTo(bytes.AsSpan(instancerPathOffset));
        if (instancerPathBytes.Length != 0)
        {
            int contextOffset = instancerPathOffset + instancerPathBytes.Length;
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(contextOffset),
                (uint)instancerPathBytes.Length);
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(contextOffset + 4),
                instanceIndex);
            instancerPathBytes.CopyTo(bytes.AsSpan(contextOffset + 8));
        }
        return bytes;
    }

    private sealed class TestGraphicsDevice : ISilkGraphicsDevice
    {
        internal List<TestGraphicsBuffer> Buffers { get; } = [];

        public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

        public SilkGraphicsCapabilities Capabilities { get; } =
            new("Test", "1", SupportsCompute: true, IsSoftware: true);

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage)
        {
            var buffer = new TestGraphicsBuffer(size, usage);
            Buffers.Add(buffer);
            return buffer;
        }

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            throw new NotSupportedException();

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderModule CreateShaderModule(SilkShaderModuleDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsBindingLayout CreateBindingLayout(
            SilkBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderProgram CreateShaderProgram(
            SilkShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsPipeline CreateGraphicsPipeline(
            SilkGraphicsPipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(
            SilkComputePipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsCommandList CreateCommandList() => throw new NotSupportedException();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
            throw new NotSupportedException();

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestGraphicsBuffer(nuint size, SilkBufferUsage usage)
        : SilkGraphicsBufferBase(size, usage)
    {
        internal byte[] Data { get; } = new byte[checked((int)size)];

        public override void Write(ReadOnlySpan<byte> data, nuint offset = 0)
        {
            _ = ValidateWrite(data.Length, offset);
            data.CopyTo(Data.AsSpan(checked((int)offset)));
        }

        public override void ReadbackForTesting(Span<byte> destination)
        {
            _ = ValidateReadback(destination.Length);
            Data.CopyTo(destination);
        }

        protected override void ReleaseNative()
        {
        }
    }
}
