// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Storm;

namespace OpenUsd.Rendering.Tests;

public sealed class StormPhysicsOverrideTests
{
    private static PhysicsRenderObjectId Body(ulong value) =>
        new(value, PhysicsRenderObjectKind.RigidBody);

    private static PhysicsRenderTransformOverride Override(
        ulong id,
        double x,
        double y,
        double z,
        bool snapped = false) =>
        new(
            Body(id),
            new UsdVec3d(x, y, z),
            PhysicsRenderOrientation.Identity,
            snapped);

    private static PhysicsRenderOverrideView View(
        ulong revision,
        params PhysicsRenderTransformOverride[] items) =>
        new(items, revision);

    [Test]
    public async Task RefreshPacksBoundOverridesInOrder()
    {
        var bindings = new PhysicsRenderBindingTable(8);
        _ = bindings.TryBind(Body(1), "/World/Cube");
        _ = bindings.TryBind(Body(2), "/World/Sphere");
        var batch = new StormPhysicsTransformOverrides(8, 4096);

        int packed = batch.Refresh(
            View(7, Override(1, 1, 2, 3), Override(2, 4, 5, 6)),
            bindings);

        await Assert.That(packed).IsEqualTo(2);
        await Assert.That(batch.Count).IsEqualTo(2);
        await Assert.That(batch.Revision).IsEqualTo(7UL);
        await Assert.That(batch.PathByteCount).IsEqualTo(
            "/World/Cube".Length + "/World/Sphere".Length);
        await Assert.That(batch.DroppedOverrides).IsEqualTo(0L);
        await Assert.That(batch.UnboundOverrides).IsEqualTo(0L);
    }

    [Test]
    public async Task RefreshPacksTheComposedWorldTransform()
    {
        var bindings = new PhysicsRenderBindingTable(4);
        _ = bindings.TryBind(Body(1), "/World/Cube");
        var batch = new StormPhysicsTransformOverrides(4, 1024);
        var expected = new double[PhysicsRenderTransforms.ElementCount];
        PhysicsRenderTransforms.Compose(
            Override(1, 3, -4, 5),
            default,
            expected);

        _ = batch.Refresh(View(1, Override(1, 3, -4, 5)), bindings);
        var actual = new double[PhysicsRenderTransforms.ElementCount];
        batch.CopyTransform(0, actual);

        await Assert.That(actual).IsEquivalentTo(expected);
        await Assert.That(actual[12]).IsEqualTo(3d);
        await Assert.That(actual[13]).IsEqualTo(-4d);
        await Assert.That(actual[14]).IsEqualTo(5d);
    }

    [Test]
    public async Task RefreshSkipsIdentitiesWithoutARenderBinding()
    {
        var bindings = new PhysicsRenderBindingTable(4);
        _ = bindings.TryBind(Body(1), "/World/Cube");
        var batch = new StormPhysicsTransformOverrides(4, 1024);

        int packed = batch.Refresh(
            View(1, Override(1, 0, 0, 0), Override(9, 0, 0, 0)),
            bindings);

        await Assert.That(packed).IsEqualTo(1);
        await Assert.That(batch.UnboundOverrides).IsEqualTo(1L);
        await Assert.That(batch.DroppedOverrides).IsEqualTo(0L);
    }

    [Test]
    public async Task RefreshDropsOverflowWithoutFailingSupportedBodies()
    {
        var bindings = new PhysicsRenderBindingTable(8);
        for (ulong id = 1; id <= 4; id++)
        {
            _ = bindings.TryBind(Body(id), $"/World/Body{id}");
        }
        var batch = new StormPhysicsTransformOverrides(2, 1024);

        int packed = batch.Refresh(
            View(
                1,
                Override(1, 0, 0, 0),
                Override(2, 0, 0, 0),
                Override(3, 0, 0, 0),
                Override(4, 0, 0, 0)),
            bindings);

        await Assert.That(packed).IsEqualTo(2);
        await Assert.That(batch.Count).IsEqualTo(2);
        await Assert.That(batch.DroppedOverrides).IsEqualTo(2L);
    }

    [Test]
    public async Task RefreshDropsOverridesThatExceedThePathBudget()
    {
        var bindings = new PhysicsRenderBindingTable(4);
        _ = bindings.TryBind(Body(1), "/World/Cube");
        _ = bindings.TryBind(Body(2), "/World/AnotherLongerPrimPath");
        var batch = new StormPhysicsTransformOverrides(4, "/World/Cube".Length);

        int packed = batch.Refresh(
            View(1, Override(1, 0, 0, 0), Override(2, 0, 0, 0)),
            bindings);

        await Assert.That(packed).IsEqualTo(1);
        await Assert.That(batch.DroppedOverrides).IsEqualTo(1L);
        await Assert.That(batch.PathByteCount).IsEqualTo("/World/Cube".Length);
    }

    [Test]
    public async Task RefreshReusesStorageAcrossUpdates()
    {
        var bindings = new PhysicsRenderBindingTable(4);
        _ = bindings.TryBind(Body(1), "/World/Cube");
        var batch = new StormPhysicsTransformOverrides(4, 1024);

        _ = batch.Refresh(View(1, Override(1, 1, 1, 1)), bindings);
        _ = batch.Refresh(View(2), bindings);

        await Assert.That(batch.Count).IsEqualTo(0);
        await Assert.That(batch.PathByteCount).IsEqualTo(0);
        await Assert.That(batch.Revision).IsEqualTo(2UL);
        await Assert.That(batch.RefreshCount).IsEqualTo(2L);
    }

    [Test]
    public async Task ClearEmptiesTheBatchSoAuthoredTransformsReturn()
    {
        var bindings = new PhysicsRenderBindingTable(4);
        _ = bindings.TryBind(Body(1), "/World/Cube");
        var batch = new StormPhysicsTransformOverrides(4, 1024);
        _ = batch.Refresh(View(4, Override(1, 1, 1, 1)), bindings);

        batch.Clear(9);

        await Assert.That(batch.Count).IsEqualTo(0);
        await Assert.That(batch.PathByteCount).IsEqualTo(0);
        await Assert.That(batch.Revision).IsEqualTo(9UL);
    }

    [Test]
    public async Task ResetClearsEveryCounter()
    {
        var bindings = new PhysicsRenderBindingTable(4);
        var batch = new StormPhysicsTransformOverrides(1, 8);
        _ = batch.Refresh(View(1, Override(1, 0, 0, 0)), bindings);

        batch.Reset();

        await Assert.That(batch.Count).IsEqualTo(0);
        await Assert.That(batch.Revision).IsEqualTo(0UL);
        await Assert.That(batch.UnboundOverrides).IsEqualTo(0L);
        await Assert.That(batch.DroppedOverrides).IsEqualTo(0L);
        await Assert.That(batch.RefreshCount).IsEqualTo(0L);
    }

    [Test]
    public async Task DeletedBindingsStopProducingOverrides()
    {
        var bindings = new PhysicsRenderBindingTable(4);
        _ = bindings.TryBind(Body(1), "/World/Cube");
        var batch = new StormPhysicsTransformOverrides(4, 1024);
        _ = batch.Refresh(View(1, Override(1, 0, 0, 0)), bindings);

        _ = bindings.Unbind(Body(1));
        int packed = batch.Refresh(View(2, Override(1, 0, 0, 0)), bindings);

        await Assert.That(packed).IsEqualTo(0);
        await Assert.That(batch.UnboundOverrides).IsEqualTo(1L);
    }

    [Test]
    public async Task StableIdentitiesKeepTheirPackedPrimPath()
    {
        var bindings = new PhysicsRenderBindingTable(4);
        _ = bindings.TryBind(Body(1), "/World/Cube");
        _ = bindings.TryBind(Body(2), "/World/Sphere");
        var batch = new StormPhysicsTransformOverrides(4, 1024);

        _ = batch.Refresh(
            View(1, Override(2, 0, 0, 0), Override(1, 0, 0, 0)),
            bindings);

        await Assert.That(batch.PathAt(0))
            .IsEqualTo("/World/Sphere");
        await Assert.That(batch.PathAt(1))
            .IsEqualTo("/World/Cube");
        await Assert.That(batch.ObjectIdAt(0))
            .IsEqualTo(2UL);
        await Assert.That(batch.ObjectIdAt(1))
            .IsEqualTo(1UL);
    }

    [Test]
    public async Task SnappedOverridesCarryTheSnapFlag()
    {
        var bindings = new PhysicsRenderBindingTable(4);
        _ = bindings.TryBind(Body(1), "/World/Cube");
        _ = bindings.TryBind(Body(2), "/World/Sphere");
        var batch = new StormPhysicsTransformOverrides(4, 1024);

        _ = batch.Refresh(
            View(
                1,
                Override(1, 0, 0, 0, snapped: true),
                Override(2, 0, 0, 0)),
            bindings);

        await Assert.That(batch.FlagsAt(0)).IsEqualTo(3U);
        await Assert.That(batch.FlagsAt(1)).IsEqualTo(2U);
    }

    [Test]
    public async Task EveryOverrideAsksStormToKeepTheRenderedScaleAndShear()
    {
        var bindings = new PhysicsRenderBindingTable(4);
        _ = bindings.TryBind(Body(1), "/World/Cube");
        var batch = new StormPhysicsTransformOverrides(4, 1024);

        _ = batch.Refresh(View(1, Override(1, 3, 4, 5)), bindings);

        await Assert.That(batch.FlagsAt(0) & 2U).IsEqualTo(2U);

        // The batch carries rotation and translation only so the renderer can compose the
        // rendered prim's own scale and shear without any managed component reading the stage.
        double[] transform = new double[16];
        batch.CopyTransform(0, transform);
        await Assert.That(transform[0]).IsEqualTo(1.0).Within(1e-12);
        await Assert.That(transform[5]).IsEqualTo(1.0).Within(1e-12);
        await Assert.That(transform[10]).IsEqualTo(1.0).Within(1e-12);
        await Assert.That(transform[12]).IsEqualTo(3.0).Within(1e-12);
    }

    [Test]
    public async Task WarmedRefreshDoesNotAllocate()
    {
        var bindings = new PhysicsRenderBindingTable(64);
        var items = new PhysicsRenderTransformOverride[32];
        for (ulong id = 1; id <= 32; id++)
        {
            _ = bindings.TryBind(Body(id), $"/World/Body{id}");
            items[id - 1] = Override(id, id, id + 1, id + 2);
        }
        var view = new PhysicsRenderOverrideView(items, 1);
        var batch = new StormPhysicsTransformOverrides(64, 8192);
        for (int warm = 0; warm < 32; warm++)
        {
            _ = batch.Refresh(view, bindings);
        }

        bool clean = false;
        int consecutive = 0;
        for (int pass = 0; pass < 8 && !clean; pass++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 1000; iteration++)
            {
                _ = batch.Refresh(view, bindings);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            consecutive = allocated == 0 ? consecutive + 1 : 0;
            clean = consecutive >= 2;
        }

        await Assert.That(clean).IsTrue();
    }

    [Test]
    public async Task DomainSupportIsDiagnosedIndividually()
    {
        await Assert.That(
                StormPhysicsTransformOverrides.IsDomainSupported(
                    PhysicsRenderDomain.RigidBody))
            .IsTrue();
        await Assert.That(
                StormPhysicsTransformOverrides.IsDomainSupported(
                    PhysicsRenderDomain.Articulation))
            .IsTrue();
        await Assert.That(
                StormPhysicsTransformOverrides.IsDomainSupported(
                    PhysicsRenderDomain.Controller))
            .IsTrue();
        await Assert.That(
                StormPhysicsTransformOverrides.IsDomainSupported(
                    PhysicsRenderDomain.Vehicle))
            .IsTrue();
        await Assert.That(
                StormPhysicsTransformOverrides.IsDomainSupported(
                    PhysicsRenderDomain.Particles))
            .IsFalse();
        await Assert.That(
                StormPhysicsTransformOverrides.IsDomainSupported(
                    PhysicsRenderDomain.Cloth))
            .IsFalse();
        await Assert.That(
                StormPhysicsTransformOverrides.IsDomainSupported(
                    PhysicsRenderDomain.Deformable))
            .IsFalse();
    }

    [Test]
    public async Task ConstructorRejectsCapacitiesOutsideTheAbiLimits()
    {
        await Assert.That(() => new StormPhysicsTransformOverrides(0, 1024))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(
                () => new StormPhysicsTransformOverrides(
                    StormPhysicsTransformOverrides.MaximumCapacity + 1,
                    1024))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new StormPhysicsTransformOverrides(4, 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(
                () => new StormPhysicsTransformOverrides(
                    4,
                    StormPhysicsTransformOverrides.MaximumPathBytes + 1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task RefreshRejectsAMissingBindingTable()
    {
        var batch = new StormPhysicsTransformOverrides(4, 1024);
        PhysicsRenderOverrideView view = PhysicsRenderOverrideView.Empty;

        await Assert.That(() => batch.Refresh(view, null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task DiagnosticsDescribeTheAppliedState()
    {
        var diagnostics = new StormPhysicsOverrideDiagnostics(
            AppliedCount: 12,
            UnresolvedCount: 1,
            DroppedCount: 2,
            UnsupportedCount: 3,
            Capacity: 4096,
            Revision: 17,
            AppliedBatchCount: 5,
            RejectedBatchCount: 1,
            DirtiedPrimCount: 24);

        await Assert.That(diagnostics.IsComplete).IsFalse();
        await Assert.That(diagnostics.Describe()).Contains("applied=12");
        await Assert.That(diagnostics.Describe()).Contains("unresolved=1");
        await Assert.That(diagnostics.Describe()).Contains("rejected=1");
        await Assert.That(StormPhysicsOverrideDiagnostics.Empty.IsComplete).IsTrue();
    }

    [Test]
    public async Task DescribeReportsBoundedCapacity()
    {
        var batch = new StormPhysicsTransformOverrides(16, 512);

        await Assert.That(batch.Describe()).Contains("capacity=16");
        await Assert.That(batch.Describe()).Contains("/512");
        await Assert.That(batch.Capacity).IsEqualTo(16);
        await Assert.That(batch.PathByteCapacity).IsEqualTo(512);
    }

    [Test]
    public async Task StormBackendDeclaresThePhysicsOverrideCapability()
    {
        var capabilities = new RenderBackendCapabilities(
            RenderBackendCapability.Presentation |
                RenderBackendCapability.PhysicsTransformOverrides,
            maxSamplesPerPixel: 8,
            isSoftware: false);

        await Assert.That(
                capabilities.Supports(
                    RenderBackendCapability.PhysicsTransformOverrides))
            .IsTrue();
    }

    [Test]
    public async Task ComposedTransformsStayFiniteForExtremePoses()
    {
        var bindings = new PhysicsRenderBindingTable(4);
        _ = bindings.TryBind(Body(1), "/World/Cube");
        var batch = new StormPhysicsTransformOverrides(4, 1024);
        var value = new PhysicsRenderTransformOverride(
            Body(1),
            new UsdVec3d(1e30, -1e30, 1e30),
            new PhysicsRenderOrientation(0.5, 0.5, 0.5, 0.5),
            Snapped: true);

        _ = batch.Refresh(new PhysicsRenderOverrideView(new[] { value }, 1), bindings);
        var actual = new double[PhysicsRenderTransforms.ElementCount];
        batch.CopyTransform(0, actual);

        foreach (double element in actual)
        {
            await Assert.That(double.IsFinite(element)).IsTrue();
        }
    }

    [Test]
    public async Task ConcurrentRefreshAndReadNeverTearsABatch()
    {
        var bindings = new PhysicsRenderBindingTable(16);
        for (ulong id = 1; id <= 8; id++)
        {
            _ = bindings.TryBind(Body(id), $"/World/Body{id}");
        }
        var batch = new StormPhysicsTransformOverrides(16, 2048);
        var items = new PhysicsRenderTransformOverride[8];
        for (ulong id = 1; id <= 8; id++)
        {
            items[id - 1] = Override(id, id, id, id);
        }
        var view = new PhysicsRenderOverrideView(items, 1);
        bool torn = false;

        var writer = Task.Run(() =>
        {
            for (int iteration = 0; iteration < 4000; iteration++)
            {
                _ = batch.Refresh(view, bindings);
            }
        });
        for (int iteration = 0; iteration < 4000; iteration++)
        {
            int count = batch.Count;
            if (count is < 0 or > 16)
            {
                torn = true;
                break;
            }
        }
        await writer;

        await Assert.That(torn).IsFalse();
        await Assert.That(batch.Count).IsEqualTo(8);
    }

    [Test]
    public async Task NativeStormTransformOverrideContractIsVersionedAndPointerFree()
    {
        string root = FindRepositoryRoot();
        string physicsHeader = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "include",
            "openusd_render_physics.h"));
        string hydraHeader = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_hydra",
            "include",
            "openusd_hydra.h"));
        string childHeader = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "include",
            "openusd_storm_child.h"));

        await Assert.That(physicsHeader).Contains(
            "OPENUSD_STORM_TRANSFORM_OVERRIDE_UPDATE_VERSION 1u");
        await Assert.That(physicsHeader).Contains(
            "OPENUSD_STORM_TRANSFORM_OVERRIDE_DIAGNOSTICS_VERSION 1u");
        await Assert.That(physicsHeader).Contains(
            "OPENUSD_STORM_TRANSFORM_OVERRIDE_MAXIMUM_ITEMS 4096u");
        await Assert.That(physicsHeader).Contains(
            "static_assert(sizeof(openusd_storm_transform_override_item) == 152)");
        await Assert.That(physicsHeader).Contains(
            "static_assert(sizeof(openusd_storm_transform_override_update) == 48)");
        await Assert.That(physicsHeader).Contains(
            "static_assert(sizeof(openusd_storm_transform_override_diagnostics) == 64)");
        await Assert.That(physicsHeader).Contains(
            "OPENUSD_STORM_TRANSFORM_OVERRIDE_ITEM_PRESERVE_STRETCH 0x2u");
        await Assert.That(physicsHeader).DoesNotContain("PhysX");
        await Assert.That(physicsHeader).DoesNotContain("physx");
        await Assert.That(hydraHeader).Contains("OPENUSD_STORM_ABI_VERSION 8u");
        await Assert.That(hydraHeader).Contains("openusd_storm_set_transform_overrides");
        await Assert.That(hydraHeader).Contains(
            "openusd_storm_get_transform_override_diagnostics");
        await Assert.That(childHeader).Contains("OPENUSD_STORM_CHILD_ABI_VERSION 8u");
        await Assert.That(childHeader).Contains(
            "openusd_storm_child_set_transform_overrides");
        // The child export is additive, so the child ABI and SONAME stay put
        // while the imaging shim announces the capability.
        await Assert.That(childHeader).Contains("This entry point is purely additive");
    }

    [Test]
    public async Task NativeSceneIndexOverlayNeverAuthorsUsd()
    {
        string root = FindRepositoryRoot();
        string sceneIndex = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_hydra",
            "src",
            "openusd_physics_override_scene_index.h"));
        string hydraSource = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_hydra",
            "src",
            "openusd_hydra.cpp"));

        await Assert.That(sceneIndex).Contains("HdSingleInputFilteringSceneIndexBase");
        await Assert.That(sceneIndex).Contains("HdOverlayContainerDataSource");
        await Assert.That(sceneIndex).Contains("SetResetXformStack");
        await Assert.That(sceneIndex).Contains("std::shared_mutex");
        await Assert.That(sceneIndex).Contains("ClearOverrides");
        await Assert.That(sceneIndex).Contains("RegisterSceneIndexForRenderer");
        await Assert.That(sceneIndex).Contains("OpenUsdPhysicsExtractStretch");
        await Assert.That(sceneIndex).Contains("preserve_stretch");
        await Assert.That(sceneIndex).DoesNotContain("UsdPrim");
        await Assert.That(sceneIndex).DoesNotContain("CreateAttribute");
        await Assert.That(sceneIndex).DoesNotContain("physx");
        await Assert.That(hydraSource).Contains(
            "openusd_storm_set_transform_overrides");
        await Assert.That(hydraSource).Contains(
            "OpenUsdPhysicsOverrideSceneIndexRegistrar::Capture");
    }

    [Test]
    public async Task NativeChildForwardsTheBatchOnEveryPlatform()
    {
        string root = FindRepositoryRoot();
        string payload = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "src",
            "openusd_storm_child_pick.h"));
        await Assert.That(payload).Contains(
            "OpenUsdStormChildTransformOverridePayload");

        foreach (string file in new[]
        {
            "openusd_storm_child.cpp",
            "openusd_storm_child_linux.cpp",
            "openusd_storm_child_macos.mm",
        })
        {
            string source = await File.ReadAllTextAsync(Path.Combine(
                root,
                "native",
                "openusd_storm_child",
                "src",
                file));
            await Assert.That(source).Contains("CommandKind::TransformOverrides");
            await Assert.That(source).Contains("QueueTransformOverrides");
            await Assert.That(source).Contains(
                "openusd_storm_child_set_transform_overrides");
        }
    }

    /// <summary>
    /// Requires the deformation entry point on every platform the child host builds for.
    /// </summary>
    /// <remarks>
    /// The child ABI header declares one set of exports for all platforms and the managed
    /// <c>LibraryImport</c> is likewise unconditional, so an entry point implemented in the Windows
    /// translation unit alone links and runs on Windows while throwing
    /// <c>EntryPointNotFoundException</c> from the first deformable frame on Linux - a shipped
    /// physics platform - and macOS. That is exactly what happened to
    /// <c>openusd_storm_child_set_deformation_overrides</c>, which was added beside the transform
    /// twin the case above pins but only to <c>openusd_storm_child.cpp</c>. A compile can never
    /// catch it: each platform compiles exactly one of these sources.
    /// </remarks>
    [Test]
    public async Task NativeChildForwardsDeformationsOnEveryPlatform()
    {
        string root = FindRepositoryRoot();
        string payload = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_storm_child",
            "src",
            "openusd_storm_child_pick.h"));
        await Assert.That(payload).Contains(
            "OpenUsdStormChildDeformationOverridePayload");

        foreach (string file in new[]
        {
            "openusd_storm_child.cpp",
            "openusd_storm_child_linux.cpp",
            "openusd_storm_child_macos.mm",
        })
        {
            string source = await File.ReadAllTextAsync(Path.Combine(
                root,
                "native",
                "openusd_storm_child",
                "src",
                file));
            await Assert.That(source)
                .Contains("CommandKind::DeformationOverrides");
            await Assert.That(source).Contains("QueueDeformationOverrides");
            await Assert.That(source).Contains(
                "openusd_storm_child_set_deformation_overrides");
        }
    }

    [Test]
    public async Task ManagedMirrorsTrackTheNativeContract()
    {
        string root = FindRepositoryRoot();
        string interop = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Storm",
            "StormPhysicsOverrideInterop.cs"));
        string renderer = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Storm",
            "OpenUsdStormRenderer.cs"));
        string child = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Rendering.Storm",
            "OpenUsdStormChildRuntime.cs"));
        string linuxValidator = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Runtime.Packaging",
            "Validate-LinuxNativePackage.ps1"));
        string macValidator = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "OpenUsd.Runtime.Packaging",
            "Validate-MacOsNativePackage.ps1"));

        await Assert.That(interop).Contains("openusd_render_physics.h");
        await Assert.That(interop).DoesNotContain("PhysX");
        await Assert.That(renderer).Contains(
            "StormPhysicsOverrideDiagnostics SetPhysicsTransformOverrides(");
        await Assert.That(child).Contains(
            "StormPhysicsOverrideDiagnostics SetPhysicsTransformOverrides(");
        await Assert.That(child).Contains(
            "openusd_storm_child_set_transform_overrides");
        foreach (string validator in new[] { linuxValidator, macValidator })
        {
            await Assert.That(validator).Contains(
                "openusd_storm_child_set_transform_overrides");
        }
    }

    [Test]
    public async Task NativeProbeCoversTheOverrideSceneIndex()
    {
        string root = FindRepositoryRoot();
        string probe = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_hydra",
            "tests",
            "physics_override_contract_probe.cpp"));
        string cmake = await File.ReadAllTextAsync(Path.Combine(
            root,
            "native",
            "openusd_hydra",
            "tests",
            "CMakeLists.txt"));

        await Assert.That(cmake).Contains(
            "openusd_storm_physics_override_contract_probe");
        await Assert.That(probe).Contains("HdRetainedSceneIndex");
        await Assert.That(probe).Contains("ClearOverrides");
        await Assert.That(probe).Contains("std::thread");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
