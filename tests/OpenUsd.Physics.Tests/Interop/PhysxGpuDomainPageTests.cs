// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Interop;

/// <summary>
/// Proves the managed mirror of the CUDA accelerated build page sections agrees with the contract
/// the native validator enforces, without needing a native runtime or a device.
/// </summary>
/// <remarks>
/// Composition is device neutral, so these records are staged on every machine. That makes the
/// managed validator the first line of defence: a page that is malformed here must be refused with
/// the same error code, section and reason the native validator would produce, long before any
/// device memory could be reserved.
/// </remarks>
public sealed class PhysxGpuDomainPageTests
{
    [Test]
    public async Task AParticleAndDeformablePageValidates()
    {
        using PhysxBuildPage page = CreatePage(static _ => { });
        PhysxPageValidationResult result = PhysxPageValidator.Validate(page.Bytes);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Validation.Capacities.MaxDeformationBodies).IsEqualTo(3u);
        await Assert.That(result.Validation.Capacities.MaxDeformationPoints).IsEqualTo(4u + 3u + 4u);
    }

    [Test]
    public async Task AParticleSystemWithUnknownFlagsIsRejected() =>
        await AssertRejects(
            static staged => staged.System.Flags = 0x80u,
            PhysxPageError.Value,
            PhysxPageSection.ParticleSystems);

    [Test]
    public async Task AParticleSystemNamingAMissingSceneIsRejected() =>
        await AssertRejects(
            static staged => staged.System.SceneIndex = 9,
            PhysxPageError.Reference,
            PhysxPageSection.ParticleSystems);

    [Test]
    public async Task AParticleSystemWhoseRestOffsetExceedsItsContactOffsetIsRejected() =>
        await AssertRejects(
            static staged => staged.System.SolidRestOffset = staged.System.ParticleContactOffset + 1.0F,
            PhysxPageError.Value,
            PhysxPageSection.ParticleSystems);

    [Test]
    public async Task AParticleSystemWithANeighbourhoodOutsideTheSupportedRangeIsRejected() =>
        await AssertRejects(
            static staged => staged.System.MaxNeighborhood = PhysxAbi.MaxParticleNeighborhood + 1u,
            PhysxPageError.Value,
            PhysxPageSection.ParticleSystems);

    [Test]
    public async Task AParticleSystemThatLeavesAnOrphanBodyIsRejected() =>
        await AssertRejects(
            static staged => staged.System.BodyCount = 0,
            PhysxPageError.Range,
            PhysxPageSection.ParticleBodies);

    [Test]
    public async Task AParticleBodyOutsideTheMeshPointSectionIsRejected() =>
        await AssertRejects(
            static staged => staged.Body.PointCount = 64,
            PhysxPageError.Range,
            PhysxPageSection.ParticleBodies);

    [Test]
    public async Task AParticleBodyNamingAMissingMaterialIsRejected() =>
        await AssertRejects(
            static staged => staged.Body.MaterialIndex = 7,
            PhysxPageError.Reference,
            PhysxPageSection.ParticleBodies);

    /// <summary>
    /// The runtime packs the collision group, the behaviour flags and the bound material index into
    /// one phase lookup key, so the group has to stay inside the twenty bits a phase reserves for
    /// it. A wider group used to reach the bits the material index occupies: a body with no material
    /// on group <c>1 &lt;&lt; 24</c> produced exactly the key a body on group zero bound to material
    /// zero produced, and the two silently shared one phase. The bound is what makes the packing
    /// provably collision free, so it is asserted through the raw page rather than through the
    /// composer, which clamps.
    /// </summary>
    [Test]
    public async Task AParticleBodyWhoseGroupAliasesTheMaterialFieldIsRejected() =>
        await AssertRejects(
            static staged =>
            {
                staged.Body.MaterialIndex = -1;
                staged.Body.ParticleGroup = 1u << 24;
            },
            PhysxPageError.Range,
            PhysxPageSection.ParticleBodies);

    [Test]
    public async Task AParticleBodyOneGroupPastThePhaseGroupIsRejected() =>
        await AssertRejects(
            static staged => staged.Body.ParticleGroup = PhysxAbi.MaxParticleGroup + 1u,
            PhysxPageError.Range,
            PhysxPageSection.ParticleBodies);

    [Test]
    public async Task AParticleBodyOnTheLastUsableGroupIsAccepted()
    {
        using PhysxBuildPage page = CreatePage(
            static staged => staged.Body.ParticleGroup = PhysxAbi.MaxParticleGroup);
        PhysxPageValidationResult result = PhysxPageValidator.Validate(page.Bytes);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task AnIncompressibleDeformableMaterialIsRejected() =>
        await AssertRejects(
            static staged => staged.SurfaceMaterial.PoissonsRatio = 0.5F,
            PhysxPageError.Value,
            PhysxPageSection.DeformableMaterials);

    [Test]
    public async Task AVolumeMaterialCarryingASurfaceShellIsRejected() =>
        await AssertRejects(
            static staged => staged.VolumeMaterial.Thickness = 0.01F,
            PhysxPageError.Value,
            PhysxPageSection.DeformableMaterials);

    [Test]
    public async Task ADeformableBoundToAMaterialOfTheOtherKindIsRejected() =>
        await AssertRejects(
            static staged => staged.Surface.MaterialIndex = 1,
            PhysxPageError.Reference,
            PhysxPageSection.Deformables);

    [Test]
    public async Task ASurfaceWithoutWholeTrianglesIsRejected() =>
        await AssertRejects(
            static staged => staged.Surface.IndexCount = 2,
            PhysxPageError.Range,
            PhysxPageSection.Deformables);

    [Test]
    public async Task ASurfaceThatDeclaresACollisionMeshIsRejected() =>
        await AssertRejects(
            static staged => staged.Surface.CollisionPointCount = 4,
            PhysxPageError.Value,
            PhysxPageSection.Deformables);

    [Test]
    public async Task AKinematicSurfaceIsRejected() =>
        await AssertRejects(
            static staged => staged.Surface.Flags = (uint)PhysxDeformableFlags.Kinematic,
            PhysxPageError.Value,
            PhysxPageSection.Deformables);

    [Test]
    public async Task AVolumeWithoutWholeTetrahedraIsRejected() =>
        await AssertRejects(
            static staged => staged.Volume.IndexCount = 3,
            PhysxPageError.Range,
            PhysxPageSection.Deformables);

    [Test]
    public async Task ADeformationCapacityBelowTheDeclaredObjectsIsRejected() =>
        await AssertRejects(
            static staged => staged.DeformationBodyCapacity = 1,
            PhysxPageError.Capacity,
            PhysxPageSection.Capacities);

    [Test]
    public async Task ADeformationPointCapacityBelowTheDeclaredVerticesIsRejected() =>
        await AssertRejects(
            static staged => staged.DeformationPointCapacity = 2,
            PhysxPageError.Capacity,
            PhysxPageSection.Capacities);

    [Test]
    public async Task ParticleBodiesWithoutASystemAreRejected()
    {
        // The orphan check fires first and is the more precise diagnosis: the body
        // belongs to no system window at all, which is exactly what the native
        // validator reports in the same order.
        await AssertRejects(
            static staged => staged.DropSystem = true,
            PhysxPageError.Range,
            PhysxPageSection.ParticleBodies);
    }

    private static async Task AssertRejects(
        Action<StagedGpuPage> mutate,
        PhysxPageError expected,
        PhysxPageSection section)
    {
        using var builder = PhysxPageFixture.CreateBuilder();
        var staged = new StagedGpuPage();
        Stage(builder, staged, mutate);

        bool built = builder.TryBuild(out PhysxBuildPage? page, out PhysxPageValidationResult result);
        page?.Dispose();

        await Assert.That(built).IsFalse();
        await Assert.That(result.ErrorCode).IsEqualTo(expected);
        await Assert.That(result.Section).IsEqualTo(section);
        await Assert.That(result.Message).IsNotNull();
    }

    private static PhysxBuildPage CreatePage(Action<StagedGpuPage> mutate)
    {
        using var builder = PhysxPageFixture.CreateBuilder();
        Stage(builder, new StagedGpuPage(), mutate);
        return builder.Build();
    }

    /// <summary>
    /// Stages one particle system with one body plus a surface and a volume deformable, letting the
    /// caller corrupt exactly one field first.
    /// </summary>
    private static void Stage(PhysxPageBuilder builder, StagedGpuPage staged, Action<StagedGpuPage> mutate)
    {
        // Four particle points, then three surface vertices, then four volume vertices, so every
        // window is a distinct slice of the one shared point section.
        uint particlePoints = builder.AddMeshPoints(
        [
            new PhysxVec3f(0.0F, 0.0F, 0.0F),
            new PhysxVec3f(0.1F, 0.0F, 0.0F),
            new PhysxVec3f(0.0F, 0.1F, 0.0F),
            new PhysxVec3f(0.1F, 0.1F, 0.0F)
        ]);
        uint surfacePoints = builder.AddMeshPoints(
        [
            new PhysxVec3f(0.0F, 1.0F, 0.0F),
            new PhysxVec3f(1.0F, 1.0F, 0.0F),
            new PhysxVec3f(0.0F, 1.0F, 1.0F)
        ]);
        uint volumePoints = builder.AddMeshPoints(
        [
            new PhysxVec3f(0.0F, 2.0F, 0.0F),
            new PhysxVec3f(1.0F, 2.0F, 0.0F),
            new PhysxVec3f(0.0F, 2.0F, 1.0F),
            new PhysxVec3f(0.0F, 3.0F, 0.0F)
        ]);
        uint surfaceIndices = builder.AddMeshIndices([0u, 1u, 2u]);
        uint volumeIndices = builder.AddMeshIndices([0u, 1u, 2u, 3u]);

        staged.ParticleMaterial = new PhysxParticleMaterialDesc
        {
            Id = builder.DefineIdentity("/World/Materials/Water"),
            Friction = 0.2F,
            Density = 1000.0F,
            CflCoefficient = 1.0F,
            GravityScale = 1.0F
        };
        staged.System = new PhysxParticleSystemDesc
        {
            Id = builder.DefineIdentity("/World/ParticleSystem"),
            SceneIndex = 0,
            Flags = (uint)PhysxParticleSystemFlags.GlobalSelfCollision,
            ParticleContactOffset = 0.06F,
            ContactOffset = 0.06F,
            RestOffset = 0.05F,
            SolidRestOffset = 0.05F,
            FluidRestOffset = 0.045F,
            NeighborhoodScale = 1.01F,
            MaxNeighborhood = 96,
            SolverPositionIterations = 4,
            BodyOffset = 0,
            BodyCount = 1
        };
        staged.Body = new PhysxParticleBodyDesc
        {
            Id = builder.DefineIdentity("/World/Granules"),
            Kind = (uint)PhysxParticleBodyKind.Set,
            Flags = (uint)PhysxParticleBodyFlags.SelfCollision,
            MaterialIndex = 0,
            PointOffset = particlePoints,
            PointCount = 4,
            WorldPose = new PhysxTransform(new PhysxVec3f(0.0F, 4.0F, 0.0F), PhysxQuatf.Identity)
        };
        staged.SurfaceMaterial = new PhysxDeformableMaterialDesc
        {
            Id = builder.DefineIdentity("/World/Materials/Cloth"),
            Kind = (uint)PhysxDeformableKind.Surface,
            YoungsModulus = 500000.0F,
            PoissonsRatio = 0.45F,
            DynamicFriction = 0.25F,
            Density = 1000.0F,
            Thickness = 0.001F
        };
        staged.VolumeMaterial = new PhysxDeformableMaterialDesc
        {
            Id = builder.DefineIdentity("/World/Materials/Jelly"),
            Kind = (uint)PhysxDeformableKind.Volume,
            YoungsModulus = 50000.0F,
            PoissonsRatio = 0.45F,
            DynamicFriction = 0.25F,
            Density = 1000.0F
        };
        staged.Surface = new PhysxDeformableDesc
        {
            Id = builder.DefineIdentity("/World/Cloth"),
            SceneIndex = 0,
            Kind = (uint)PhysxDeformableKind.Surface,
            MaterialIndex = 0,
            SolverPositionIterations = 16,
            VertexVelocityDamping = 0.005F,
            SelfCollisionFilterDistance = 0.1F,
            PointOffset = surfacePoints,
            PointCount = 3,
            IndexOffset = surfaceIndices,
            IndexCount = 3,
            WorldPose = new PhysxTransform(new PhysxVec3f(0.0F, 5.0F, 0.0F), PhysxQuatf.Identity)
        };
        staged.Volume = new PhysxDeformableDesc
        {
            Id = builder.DefineIdentity("/World/Jelly"),
            SceneIndex = 0,
            Kind = (uint)PhysxDeformableKind.Volume,
            MaterialIndex = 1,
            SolverPositionIterations = 16,
            VertexVelocityDamping = 0.005F,
            SelfCollisionFilterDistance = 0.1F,
            PointOffset = volumePoints,
            PointCount = 4,
            IndexOffset = volumeIndices,
            IndexCount = 4,
            WorldPose = new PhysxTransform(new PhysxVec3f(0.0F, 6.0F, 0.0F), PhysxQuatf.Identity)
        };

        mutate(staged);

        builder.AddParticleMaterial(in staged.ParticleMaterial);
        if (!staged.DropSystem)
        {
            builder.AddParticleSystem(in staged.System);
        }
        builder.AddParticleBody(in staged.Body);
        builder.AddDeformableMaterial(in staged.SurfaceMaterial);
        builder.AddDeformableMaterial(in staged.VolumeMaterial);
        builder.AddDeformable(in staged.Surface);
        builder.AddDeformable(in staged.Volume);

        if (staged.DeformationBodyCapacity is { } bodies)
        {
            builder.DeformationBodyCapacity = bodies;
        }
        if (staged.DeformationPointCapacity is { } points)
        {
            builder.DeformationPointCapacity = points;
        }
    }

    /// <summary>The staged records one test may corrupt before they reach the builder.</summary>
    private sealed class StagedGpuPage
    {
        public PhysxParticleMaterialDesc ParticleMaterial;
        public PhysxParticleSystemDesc System;
        public PhysxParticleBodyDesc Body;
        public PhysxDeformableMaterialDesc SurfaceMaterial;
        public PhysxDeformableMaterialDesc VolumeMaterial;
        public PhysxDeformableDesc Surface;
        public PhysxDeformableDesc Volume;
        public bool DropSystem;
        public uint? DeformationBodyCapacity;
        public uint? DeformationPointCapacity;
    }
}
