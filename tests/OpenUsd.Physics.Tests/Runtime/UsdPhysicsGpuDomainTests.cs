// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Extraction;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests;

/// <summary>
/// Drives the optional CUDA accelerated domains through the whole chain: an authored stage, native
/// extraction, composition onto the build page, a retained native world, and fixed stepping.
/// </summary>
/// <remarks>
/// <para>
/// The suite asserts one contract on both kinds of machine. Composition is device neutral, so a
/// particle system, a surface deformable, and a volume deformable are always staged onto the build
/// page. Whether they can then be simulated is a runtime question, so every test that reads motion
/// asks the negotiated capability set first: with a device the vertices must actually move, and
/// without one every GPU object must be skipped individually while the rigid bodies of the same
/// build keep falling.
/// </para>
/// <para>
/// Nothing here is emulated. A run without a device proves graceful degradation, never a weaker
/// version of the same simulation.
/// </para>
/// </remarks>
internal sealed class UsdPhysicsGpuDomainTests
{
    private const string ScenePath = "/World/PhysicsScene";
    private const string GroundPath = "/World/Ground";
    private const string FallingPath = "/World/Falling";
    private const string ParticleSystemPath = "/World/ParticleSystem";
    private const string GranulesPath = "/World/Granules";
    private const string FluidPath = "/World/Fluid";
    private const string ClothPath = "/World/Cloth";
    private const string JellyPath = "/World/Jelly";

    /// <summary>True when the negotiated runtime reports an operational CUDA context.</summary>
    private static bool DeviceAvailable =>
        PhysxRuntime.Info.IsAvailable &&
        ((PhysxCapabilityFlags)PhysxRuntime.Info.Capabilities.Flags & PhysxCapabilityFlags.CudaContext) != 0;

    [Test]
    public async Task TheGpuDomainsComposeWhetherOrNotADeviceExists()
    {
        CpuDomainFixture.RequireRuntime();
        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(TheGpuDomainsComposeWhetherOrNotADeviceExists));
        fixture.WriteStage(GpuStage());

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        UsdPhysicsCompositionReport composition = simulation.Composition
            ?? throw new InvalidOperationException("The build ran no composition.");

        await Assert.That(composition.Gpu.ParticleSystems).IsEqualTo(1);
        await Assert.That(composition.Gpu.ParticleBodies).IsEqualTo(2);
        await Assert.That(composition.Gpu.SurfaceDeformables).IsEqualTo(1);
        await Assert.That(composition.Gpu.VolumeDeformables).IsEqualTo(1);
        await Assert.That(composition.Gpu.DeformationVertices > 0).IsTrue();
    }

    [Test]
    public async Task TheCapabilitySetPublishesEveryGpuDomainOnlyWithAnOperationalContext()
    {
        CpuDomainFixture.RequireRuntime();
        UsdPhysicsCapabilities capabilities = PhysxRuntime.Info.ManagedCapabilities;
        bool device = DeviceAvailable;

        await Assert.That(capabilities.Supports(UsdPhysicsCapability.Cuda)).IsEqualTo(device);
        await Assert.That(capabilities.Supports(UsdPhysicsCapability.Particles)).IsEqualTo(device);
        await Assert.That(capabilities.Supports(UsdPhysicsCapability.Cloth)).IsEqualTo(device);
        await Assert.That(capabilities.Supports(UsdPhysicsCapability.Deformables)).IsEqualTo(device);

        // The CPU domains are never affected by the device answer.
        await Assert.That(capabilities.Supports(UsdPhysicsCapability.RigidBodies)).IsTrue();
    }

    [Test]
    public async Task ARigidBodyKeepsSimulatingAlongsideEveryGpuObject()
    {
        CpuDomainFixture.RequireRuntime();
        using CpuDomainFixture fixture =
            CpuDomainFixture.Create(nameof(ARigidBodyKeepsSimulatingAlongsideEveryGpuObject));
        fixture.WriteStage(GpuStage());

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        simulation.Step(1);
        double first = simulation.RequirePose(FallingPath).Position.Y;
        simulation.Step(60);
        double last = simulation.RequirePose(FallingPath).Position.Y;

        await Assert.That(last < first).IsTrue();
    }

    [Test]
    public async Task EveryGpuObjectIsSkippedByItselfWhenNoDeviceIsReachable()
    {
        CpuDomainFixture.RequireRuntime();
        if (DeviceAvailable)
        {
            Skip.Test("A CUDA device is reachable, so the device absent isolation path cannot be exercised.");
        }

        using CpuDomainFixture fixture =
            CpuDomainFixture.Create(nameof(EveryGpuObjectIsSkippedByItselfWhenNoDeviceIsReachable));
        fixture.WriteStage(GpuStage());

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        simulation.Step(1);

        UsdPhysicsDiagnostics diagnostics = simulation.World.DrainDiagnostics();
        var skips = diagnostics.Entries
            .Where(static entry => entry.Code == "OPENUSD_PHYSICS_GPU_OBJECT_SKIPPED" ||
                entry.Code == "OPENUSD_PHYSICS_GPU_UNAVAILABLE")
            .ToArray();

        // One note per declared GPU object: the system, the surface, and the volume.
        await Assert.That(skips.Length >= 3).IsTrue();
        await Assert.That(skips.All(static entry => entry.ObjectId is not null)).IsTrue();
        await Assert.That(simulation.Frame.DeformationCount).IsEqualTo(0);
        await Assert.That(simulation.Frame.DeformationsTruncated).IsFalse();
    }

    [Test]
    public async Task TheParticleBodiesActuallyMoveOnADevice()
    {
        CpuDomainFixture.RequireRuntime();
        if (!DeviceAvailable)
        {
            Skip.Test("No CUDA device is reachable, so particle motion cannot be simulated.");
        }

        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(TheParticleBodiesActuallyMoveOnADevice));
        fixture.WriteStage(GpuStage());

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        simulation.Step(1);
        UsdPhysicsDeformation granules = simulation.FindDeformation(GranulesPath)
            ?? throw new InvalidOperationException("The solid particle body published no deformation window.");
        UsdPhysicsDeformation fluid = simulation.FindDeformation(FluidPath)
            ?? throw new InvalidOperationException("The fluid particle body published no deformation window.");
        await Assert.That(granules.Kind).IsEqualTo(UsdPhysicsDeformationKind.Particles);
        await Assert.That(fluid.Kind).IsEqualTo(UsdPhysicsDeformationKind.Fluid);

        UsdVec3d[] granulesBefore = simulation.CaptureDeformation(in granules);
        UsdVec3d[] fluidBefore = simulation.CaptureDeformation(in fluid);
        simulation.Step(60);

        UsdPhysicsDeformation granulesAfterWindow = simulation.FindDeformation(GranulesPath)!.Value;
        UsdPhysicsDeformation fluidAfterWindow = simulation.FindDeformation(FluidPath)!.Value;
        UsdVec3d[] granulesAfter = simulation.CaptureDeformation(in granulesAfterWindow);
        UsdVec3d[] fluidAfter = simulation.CaptureDeformation(in fluidAfterWindow);

        await Assert.That(UsdPhysicsSimulation.Displacement(granulesBefore, granulesAfter) > 1.0e-4).IsTrue();
        await Assert.That(UsdPhysicsSimulation.Displacement(fluidBefore, fluidAfter) > 1.0e-4).IsTrue();
    }

    [Test]
    public async Task TheSurfaceAndVolumeDeformablesActuallyDeformOnADevice()
    {
        CpuDomainFixture.RequireRuntime();
        if (!DeviceAvailable)
        {
            Skip.Test("No CUDA device is reachable, so deformation cannot be simulated.");
        }

        using CpuDomainFixture fixture =
            CpuDomainFixture.Create(nameof(TheSurfaceAndVolumeDeformablesActuallyDeformOnADevice));
        fixture.WriteStage(GpuStage());

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        simulation.Step(1);
        UsdPhysicsDeformation cloth = simulation.FindDeformation(ClothPath)
            ?? throw new InvalidOperationException("The surface deformable published no deformation window.");
        UsdPhysicsDeformation jelly = simulation.FindDeformation(JellyPath)
            ?? throw new InvalidOperationException("The volume deformable published no deformation window.");
        await Assert.That(cloth.Kind).IsEqualTo(UsdPhysicsDeformationKind.Surface);
        await Assert.That(jelly.Kind).IsEqualTo(UsdPhysicsDeformationKind.Volume);

        UsdVec3d[] clothBefore = simulation.CaptureDeformation(in cloth);
        UsdVec3d[] jellyBefore = simulation.CaptureDeformation(in jelly);
        simulation.Step(60);

        UsdPhysicsDeformation clothAfterWindow = simulation.FindDeformation(ClothPath)!.Value;
        UsdPhysicsDeformation jellyAfterWindow = simulation.FindDeformation(JellyPath)!.Value;
        UsdVec3d[] clothAfter = simulation.CaptureDeformation(in clothAfterWindow);
        UsdVec3d[] jellyAfter = simulation.CaptureDeformation(in jellyAfterWindow);

        await Assert.That(UsdPhysicsSimulation.Displacement(clothBefore, clothAfter) > 1.0e-4).IsTrue();
        await Assert.That(UsdPhysicsSimulation.Displacement(jellyBefore, jellyAfter) > 1.0e-4).IsTrue();
    }

    [Test]
    public async Task ParticleClothIsReportedAsUnimplementedRatherThanSilentlyDropped()
    {
        CpuDomainFixture.RequireRuntime();
        using CpuDomainFixture fixture =
            CpuDomainFixture.Create(nameof(ParticleClothIsReportedAsUnimplementedRatherThanSilentlyDropped));
        fixture.WriteStage(ParticleClothStage());

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        UsdPhysicsCompositionReport composition = simulation.Composition
            ?? throw new InvalidOperationException("The build ran no composition.");

        string note = composition.Skipped.SingleOrDefault(static entry =>
            entry.Contains("particle cloth", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Particle cloth was dropped without a note.");

        // The note has to describe this build's own choice. Claiming the pinned
        // SDK removed the feature would be a statement about the SDK that is not
        // true, and it would send a reader looking for a version that has it.
        await Assert.That(note).Contains("this build does not implement");
        await Assert.That(note).Contains("OpenUsdPhysicsSurfaceDeformableAPI");
        await Assert.That(note).DoesNotContain("retired");
        await Assert.That(note).DoesNotContain("SDK");
        await Assert.That(composition.Gpu.SurfaceDeformables).IsEqualTo(0);
    }

    [Test]
    public async Task AVolumeWithoutATetrahedralMeshIsReportedRatherThanApproximated()
    {
        CpuDomainFixture.RequireRuntime();
        using CpuDomainFixture fixture =
            CpuDomainFixture.Create(nameof(AVolumeWithoutATetrahedralMeshIsReportedRatherThanApproximated));
        fixture.WriteStage(VolumeWithoutTetrahedraStage());

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        UsdPhysicsCompositionReport composition = simulation.Composition
            ?? throw new InvalidOperationException("The build ran no composition.");

        await Assert.That(composition.Gpu.VolumeDeformables).IsEqualTo(0);
        await Assert.That(composition.Skipped.Any(static note => note.Contains(
            "tetrahedral simulation vertices", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task ADisabledGpuObjectIsLeftOutWithItsOwnNote()
    {
        CpuDomainFixture.RequireRuntime();
        using CpuDomainFixture fixture = CpuDomainFixture.Create(nameof(ADisabledGpuObjectIsLeftOutWithItsOwnNote));
        fixture.WriteStage(GpuStage(clothEnabled: false));

        using UsdPhysicsSimulation simulation = fixture.BuildSimulation();
        UsdPhysicsCompositionReport composition = simulation.Composition
            ?? throw new InvalidOperationException("The build ran no composition.");

        await Assert.That(composition.Gpu.SurfaceDeformables).IsEqualTo(0);
        await Assert.That(composition.Gpu.ParticleSystems).IsEqualTo(1);
        await Assert.That(composition.Skipped.Any(static note =>
            note.Contains(ClothPath, StringComparison.Ordinal) &&
            note.Contains("disables", StringComparison.Ordinal))).IsTrue();
    }

    /// <summary>Authors a stage that mixes a rigid body with every CUDA accelerated domain.</summary>
    private static string GpuStage(bool clothEnabled = true) =>
        $$"""
        #usda 1.0
        (
            defaultPrim = "World"
            metersPerUnit = 1
            upAxis = "Y"
            timeCodesPerSecond = 24
            startTimeCode = 0
            endTimeCode = 24
        )

        def Xform "World"
        {
            def PhysicsScene "PhysicsScene"
            {
                vector3f physics:gravityDirection = (0, -1, 0)
                float physics:gravityMagnitude = 9.81
            }

            def Cube "Ground" (prepend apiSchemas = ["PhysicsCollisionAPI"])
            {
                double size = 1
                float3 xformOp:scale = (40, 0.5, 40)
                double3 xformOp:translate = (0, -0.5, 0)
                uniform token[] xformOpOrder = ["xformOp:translate", "xformOp:scale"]
            }

            def Sphere "Falling" (
                prepend apiSchemas = ["PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                double radius = 0.5
                float physics:mass = 2
                double3 xformOp:translate = (0, 6, 0)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }

            def Scope "ParticleSystem" (prepend apiSchemas = ["OpenUsdPhysicsParticleSystemAPI"])
            {
                rel openUsdPhysics:particleSystem:simulationOwner = </World/PhysicsScene>
                double openUsdPhysics:particleSystem:particleContactOffset = 0.06
                double openUsdPhysics:particleSystem:contactOffset = 0.06
                double openUsdPhysics:particleSystem:restOffset = 0.05
                double openUsdPhysics:particleSystem:solidRestOffset = 0.05
                double openUsdPhysics:particleSystem:fluidRestOffset = 0.045
                uniform int64 openUsdPhysics:particleSystem:solverPositionIterationCount = 4
            }

            def Material "Water" (prepend apiSchemas = ["OpenUsdPhysicsPbdMaterialAPI"])
            {
                double openUsdPhysics:pbdMaterial:friction = 0.2
                double openUsdPhysics:pbdMaterial:density = 1000
                double openUsdPhysics:pbdMaterial:viscosity = 0.01
            }

            def Points "Granules" (prepend apiSchemas = ["OpenUsdPhysicsParticleSetAPI"])
            {
                point3f[] points = [(0, 3, 0), (0.1, 3, 0), (0, 3.1, 0), (0.1, 3.1, 0),
                                    (0, 3, 0.1), (0.1, 3, 0.1), (0, 3.1, 0.1), (0.1, 3.1, 0.1)]
                rel openUsdPhysics:particleSet:particleSystem = </World/ParticleSystem>
                rel openUsdPhysics:particleSet:material = </World/Water>
                bool openUsdPhysics:particleSet:fluid = false
                uniform int64 openUsdPhysics:particleSet:particleGroup = 0
            }

            def Points "Fluid" (prepend apiSchemas = ["OpenUsdPhysicsParticleSetAPI"])
            {
                point3f[] points = [(1, 3, 0), (1.1, 3, 0), (1, 3.1, 0), (1.1, 3.1, 0),
                                    (1, 3, 0.1), (1.1, 3, 0.1), (1, 3.1, 0.1), (1.1, 3.1, 0.1)]
                rel openUsdPhysics:particleSet:particleSystem = </World/ParticleSystem>
                rel openUsdPhysics:particleSet:material = </World/Water>
                bool openUsdPhysics:particleSet:fluid = true
                uniform int64 openUsdPhysics:particleSet:particleGroup = 1
            }

            def Material "ClothMaterial" (prepend apiSchemas = ["OpenUsdPhysicsSurfaceDeformableMaterialAPI"])
            {
                double openUsdPhysics:surfaceDeformableMaterial:youngsModulus = 500000
                double openUsdPhysics:surfaceDeformableMaterial:poissonsRatio = 0.45
                double openUsdPhysics:surfaceDeformableMaterial:density = 1000
                double openUsdPhysics:surfaceDeformableMaterial:thickness = 0.001
            }

            def Mesh "Cloth" (prepend apiSchemas = ["OpenUsdPhysicsSurfaceDeformableAPI"])
            {
                int[] faceVertexCounts = [3, 3, 3, 3, 3, 3, 3, 3]
                int[] faceVertexIndices = [0, 1, 3, 1, 4, 3, 1, 2, 4, 2, 5, 4,
                                           3, 4, 6, 4, 7, 6, 4, 5, 7, 5, 8, 7]
                point3f[] points = [(-3, 4, 0), (-2.5, 4, 0), (-2, 4, 0),
                                    (-3, 4, 0.5), (-2.5, 4, 0.5), (-2, 4, 0.5),
                                    (-3, 4, 1), (-2.5, 4, 1), (-2, 4, 1)]
                rel openUsdPhysics:surfaceDeformable:simulationOwner = </World/PhysicsScene>
                rel openUsdPhysics:surfaceDeformable:material = </World/ClothMaterial>
                bool openUsdPhysics:surfaceDeformable:enabled = {{(clothEnabled ? "true" : "false")}}
                uniform int64 openUsdPhysics:surfaceDeformable:solverPositionIterationCount = 16
            }

            def Material "JellyMaterial" (prepend apiSchemas = ["OpenUsdPhysicsVolumeDeformableMaterialAPI"])
            {
                double openUsdPhysics:volumeDeformableMaterial:youngsModulus = 50000
                double openUsdPhysics:volumeDeformableMaterial:poissonsRatio = 0.45
                double openUsdPhysics:volumeDeformableMaterial:density = 1000
            }

            def Mesh "Jelly" (prepend apiSchemas = ["OpenUsdPhysicsVolumeDeformableAPI"])
            {
                int[] faceVertexCounts = [3, 3]
                int[] faceVertexIndices = [0, 1, 2, 0, 2, 3]
                point3f[] points = [(3, 4, 0), (4, 4, 0), (4, 4, 1), (3, 4, 1)]
                rel openUsdPhysics:volumeDeformable:simulationOwner = </World/PhysicsScene>
                rel openUsdPhysics:volumeDeformable:material = </World/JellyMaterial>
                float3[] openUsdPhysics:volumeDeformable:simulationRestPoints = [
                    (3, 4, 0), (4, 4, 0), (4, 4, 1), (3, 4, 1),
                    (3, 5, 0), (4, 5, 0), (4, 5, 1), (3, 5, 1)]
                int[] openUsdPhysics:volumeDeformable:simulationIndices = [
                    0, 1, 3, 4, 1, 2, 3, 6, 1, 3, 4, 6, 1, 4, 5, 6, 3, 4, 6, 7]
                uniform int64 openUsdPhysics:volumeDeformable:solverPositionIterationCount = 16
            }
        }
        """;

    /// <summary>Authors a stage whose only GPU object is unimplemented particle cloth.</summary>
    private static string ParticleClothStage() =>
        """
        #usda 1.0
        (
            defaultPrim = "World"
            metersPerUnit = 1
            upAxis = "Y"
            timeCodesPerSecond = 24
            startTimeCode = 0
            endTimeCode = 24
        )

        def Xform "World"
        {
            def PhysicsScene "PhysicsScene"
            {
                vector3f physics:gravityDirection = (0, -1, 0)
                float physics:gravityMagnitude = 9.81
            }

            def Scope "ParticleSystem" (prepend apiSchemas = ["OpenUsdPhysicsParticleSystemAPI"])
            {
                rel openUsdPhysics:particleSystem:simulationOwner = </World/PhysicsScene>
                double openUsdPhysics:particleSystem:particleContactOffset = 0.06
            }

            def Mesh "Cloth" (prepend apiSchemas = ["OpenUsdPhysicsParticleClothAPI"])
            {
                int[] faceVertexCounts = [3, 3]
                int[] faceVertexIndices = [0, 1, 2, 0, 2, 3]
                point3f[] points = [(0, 4, 0), (1, 4, 0), (1, 4, 1), (0, 4, 1)]
                rel openUsdPhysics:particleCloth:particleSystem = </World/ParticleSystem>
            }
        }
        """;

    /// <summary>Authors a volume deformable that carries no tetrahedral simulation mesh.</summary>
    private static string VolumeWithoutTetrahedraStage() =>
        """
        #usda 1.0
        (
            defaultPrim = "World"
            metersPerUnit = 1
            upAxis = "Y"
            timeCodesPerSecond = 24
            startTimeCode = 0
            endTimeCode = 24
        )

        def Xform "World"
        {
            def PhysicsScene "PhysicsScene"
            {
                vector3f physics:gravityDirection = (0, -1, 0)
                float physics:gravityMagnitude = 9.81
            }

            def Mesh "Jelly" (prepend apiSchemas = ["OpenUsdPhysicsVolumeDeformableAPI"])
            {
                int[] faceVertexCounts = [3, 3]
                int[] faceVertexIndices = [0, 1, 2, 0, 2, 3]
                point3f[] points = [(0, 4, 0), (1, 4, 0), (1, 4, 1), (0, 4, 1)]
                rel openUsdPhysics:volumeDeformable:simulationOwner = </World/PhysicsScene>
            }
        }
        """;
}
