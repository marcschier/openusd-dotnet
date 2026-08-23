// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Extraction;

/// <summary>Reports what the CUDA accelerated part of one composition staged.</summary>
/// <param name="ParticleSystems">How many particle systems were staged.</param>
/// <param name="ParticleBodies">How many particle bodies were staged.</param>
/// <param name="SurfaceDeformables">How many surface deformables were staged.</param>
/// <param name="VolumeDeformables">How many volume deformables were staged.</param>
/// <param name="DeformationVertices">How many simulated vertices those objects declare.</param>
internal readonly record struct UsdPhysicsGpuCompositionCounts(
    int ParticleSystems,
    int ParticleBodies,
    int SurfaceDeformables,
    int VolumeDeformables,
    int DeformationVertices);

/// <summary>
/// Composes the CUDA accelerated domains of one extraction page.
/// </summary>
/// <remarks>
/// <para>
/// Composition is device neutral on purpose. A particle system, a cloth, or a volume deformable is
/// staged into the build page whenever the authored stage describes one that the ABI can carry,
/// whether or not the machine running the composition has a CUDA device. The runtime is the single
/// place that decides whether such an object can actually be created, and it reports one skip
/// diagnostic per object it cannot build. Deciding here would make an authored stage compose
/// differently on two machines, which is exactly what the build page contract forbids.
/// </para>
/// <para>
/// Everything the ABI cannot carry is still skipped here, individually and with an ordered note, so
/// one unsupported prim never costs a sibling that is fully supported.
/// </para>
/// </remarks>
internal static partial class UsdPhysicsExtractionComposer
{
    /// <summary>The rest offset a particle system falls back to, in metres.</summary>
    private const float DefaultParticleRestOffset = 0.05F;

    /// <summary>The shell thickness a surface deformable material falls back to, in metres.</summary>
    private const float DefaultShellThickness = 0.001F;

    /// <summary>The largest Poisson ratio the solver accepts; one half is incompressible.</summary>
    private const double MaxPoissonsRatio = 0.499;

    /// <summary>
    /// Resolves the factor that turns one authored local point of a simulated object into the
    /// simulation-space offset the build page carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An extracted point is authored, local, and in stage units, while the build page is metres
    /// and its poses carry rotation and translation only. A simulated vertex therefore has to
    /// absorb both the stage unit scale and the authored scale of the prim it belongs to, because
    /// nothing downstream can apply either: a particle buffer and a deformable vertex buffer are
    /// positions, not shapes with a geometry scale a solver could scale for them.
    /// </para>
    /// <para>
    /// Baking the authored scale in reproduces the authored transform exactly. Extraction
    /// decomposes the local-to-world basis into per-axis lengths and a normalized rotation, so a
    /// world position is the local point scaled per axis, then rotated, then translated - which is
    /// exactly the order the build page and the runtime apply. A scale that cannot be simulated,
    /// because an axis is not finite or collapses the object to nothing, is refused rather than
    /// silently replaced, so an unusable prim is skipped and reported instead of being simulated at
    /// a size its author never wrote.
    /// </para>
    /// </remarks>
    /// <param name="page">The extraction page the object belongs to.</param>
    /// <param name="item">The extracted object whose points are staged.</param>
    /// <param name="scale">The per-axis factor to multiply every local point by.</param>
    /// <returns><see langword="false"/> when the authored scale cannot be simulated.</returns>
    private static bool TryResolvePointScale(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        out (double X, double Y, double Z) scale)
    {
        scale = default;
        double units = page.MetersPerUnit;
        if (!double.IsFinite(units) || units <= 0.0)
        {
            return false;
        }

        (double x, double y, double z) = item.Scale;
        if (!TryPositiveScale(x, out float scaleX) |
            !TryPositiveScale(y, out float scaleY) |
            !TryPositiveScale(z, out float scaleZ))
        {
            return false;
        }

        // Extraction carries a mirrored basis as a negative first axis. A vertex buffer has no
        // geometry scale to mirror, so the magnitude is used and the handedness of the authored
        // basis is not reproduced; the alternative is to refuse an otherwise simulable prim.
        scale = (scaleX * units, scaleY * units, scaleZ * units);
        return true;
    }

    /// <summary>Narrows one staged local point into the build page.</summary>
    private static bool TryScalePoint(
        (float X, float Y, float Z) authored,
        (double X, double Y, double Z) scale,
        out PhysxVec3f point)
    {
        point = new PhysxVec3f(
            ToFloat(authored.X * scale.X, float.NaN),
            ToFloat(authored.Y * scale.Y, float.NaN),
            ToFloat(authored.Z * scale.Z, float.NaN));
        return point.IsFinite;
    }

    private static UsdPhysicsGpuCompositionCounts ComposeGpuDomains(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        int[] sceneIndices,
        ImmutableArray<string>.Builder skipped)
    {
        int[] particleMaterialIndices = ComposeParticleMaterials(page, builder, skipped);
        int[] deformableMaterialIndices = ComposeDeformableMaterials(page, builder, skipped);
        (int systems, int bodies, int particleVertices) = ComposeParticleSystems(
            page, builder, sceneIndices, particleMaterialIndices, skipped);
        (int surfaces, int volumes, int deformableVertices) = ComposeDeformables(
            page, builder, sceneIndices, deformableMaterialIndices, skipped);
        ReportUnsupportedGpuObjects(page, skipped);
        return new UsdPhysicsGpuCompositionCounts(
            systems, bodies, surfaces, volumes, particleVertices + deformableVertices);
    }

    private static int[] ComposeParticleMaterials(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        ImmutableArray<string>.Builder skipped)
    {
        int[] indices = CreateMap(page.ObjectCount);
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind != UsdPhysicsExtractionObjectKind.PbdMaterial || !item.IsEnabled)
            {
                continue;
            }

            double densityScale = DensityScale(page);
            var material = new PhysxParticleMaterialDesc
            {
                Id = builder.DefineIdentity(item.Path),
                Friction = Positive(page, item, UsdPhysicsExtractionKey.PbdMaterialFriction, 0.2),
                Damping = Positive(page, item, UsdPhysicsExtractionKey.PbdMaterialDamping, 0.0),
                Adhesion = Positive(page, item, UsdPhysicsExtractionKey.PbdMaterialAdhesion, 0.0),
                AdhesionOffsetScale =
                    Positive(page, item, UsdPhysicsExtractionKey.PbdMaterialAdhesionOffsetScale, 0.0),
                ParticleFrictionScale =
                    Positive(page, item, UsdPhysicsExtractionKey.PbdMaterialParticleFrictionScale, 1.0),
                ParticleAdhesionScale =
                    Positive(page, item, UsdPhysicsExtractionKey.PbdMaterialParticleAdhesionScale, 1.0),
                Viscosity = Positive(page, item, UsdPhysicsExtractionKey.PbdMaterialViscosity, 0.0),
                SurfaceTension =
                    Positive(page, item, UsdPhysicsExtractionKey.PbdMaterialSurfaceTension, 0.0),
                Cohesion = Positive(page, item, UsdPhysicsExtractionKey.PbdMaterialCohesion, 0.0),
                VorticityConfinement =
                    Positive(page, item, UsdPhysicsExtractionKey.PbdMaterialVorticityConfinement, 0.0),
                Drag = Positive(page, item, UsdPhysicsExtractionKey.PbdMaterialDrag, 0.0),
                Lift = Positive(page, item, UsdPhysicsExtractionKey.PbdMaterialLift, 0.0),
                GravityScale =
                    Positive(page, item, UsdPhysicsExtractionKey.PbdMaterialGravityScale, 1.0),
                Density = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.PbdMaterialDensity, 0.0) * densityScale,
                    0.0F),
                CflCoefficient =
                    Positive(page, item, UsdPhysicsExtractionKey.PbdMaterialCflCoefficient, 1.0),
                Reserved0 = 0,
            };
            if (!float.IsFinite(material.Density) || material.Density < 0.0F)
            {
                skipped.Add(
                    $"{item.Path} authors a particle density that cannot be simulated, so the runtime " +
                        "default is used.");
                material.Density = 0.0F;
            }
            indices[index] = builder.AddParticleMaterial(in material);
        }

        return indices;
    }

    private static int[] ComposeDeformableMaterials(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        ImmutableArray<string>.Builder skipped)
    {
        int[] indices = CreateMap(page.ObjectCount);
        double densityScale = DensityScale(page);
        // Young's modulus and the bending stiffness are pressures, so they scale
        // with force over area rather than with a length.
        double pressureScale = ForceScale(page) / (page.MetersPerUnit * page.MetersPerUnit);
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            bool surface = item.Kind == UsdPhysicsExtractionObjectKind.SurfaceDeformableMaterial;
            bool volume = item.Kind == UsdPhysicsExtractionObjectKind.VolumeDeformableMaterial;
            if ((!surface && !volume) || !item.IsEnabled)
            {
                continue;
            }

            float youngs = ToFloat(
                NonNegative(page, item, UsdPhysicsExtractionKey.DeformableMaterialYoungsModulus, 0.0) *
                    pressureScale,
                0.0F);
            if (!(youngs > 0.0F))
            {
                youngs = surface ? 500000.0F : 50000000.0F;
            }
            float density = ToFloat(
                NonNegative(page, item, UsdPhysicsExtractionKey.DeformableMaterialDensity, 1000.0) *
                    densityScale,
                0.0F);
            if (!(density > 0.0F))
            {
                density = DefaultDensity;
            }
            double poissons = NonNegative(
                page, item, UsdPhysicsExtractionKey.DeformableMaterialPoissonsRatio, 0.45);
            if (poissons >= 0.5)
            {
                skipped.Add(
                    $"{item.Path} authors an incompressible Poisson ratio, so the largest compressible ratio is used.");
                poissons = MaxPoissonsRatio;
            }

            var material = new PhysxDeformableMaterialDesc
            {
                Id = builder.DefineIdentity(item.Path),
                Kind = surface ? (uint)PhysxDeformableKind.Surface : (uint)PhysxDeformableKind.Volume,
                Reserved0 = 0,
                YoungsModulus = youngs,
                PoissonsRatio = ToFloat(poissons, 0.45F),
                DynamicFriction = Positive(
                    page, item, UsdPhysicsExtractionKey.DeformableMaterialDynamicFriction, 0.25),
                Density = density,
                ElasticityDamping = Positive(
                    page, item, UsdPhysicsExtractionKey.DeformableMaterialElasticityDamping, 0.0),
                Reserved1 = 0,
                Reserved2 = 0,
            };

            if (surface)
            {
                material.BendingStiffness = ToFloat(
                    NonNegative(
                        page, item, UsdPhysicsExtractionKey.DeformableMaterialBendingStiffness, 0.0) *
                        pressureScale,
                    0.0F);
                material.BendingDamping = Positive(
                    page, item, UsdPhysicsExtractionKey.DeformableMaterialBendingDamping, 0.0);
                float thickness = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.DeformableMaterialThickness, 0.001) *
                        page.MetersPerUnit,
                    0.0F);
                material.Thickness = thickness > 0.0F ? thickness : DefaultShellThickness;
            }
            else
            {
                // The simulation SDK models damping as one elasticity damping term,
                // so an authored damping and damping scale are folded here rather
                // than carried into an ABI field that maps to nothing.
                double damping = NonNegative(
                    page, item, UsdPhysicsExtractionKey.DeformableMaterialDamping, 0.0);
                double dampingScale = NonNegative(
                    page, item, UsdPhysicsExtractionKey.DeformableMaterialDampingScale, 1.0);
                float folded = ToFloat(material.ElasticityDamping + (damping * dampingScale), 0.0F);
                material.ElasticityDamping = folded >= 0.0F ? folded : material.ElasticityDamping;
            }

            indices[index] = builder.AddDeformableMaterial(in material);
        }

        return indices;
    }

    private static (int Systems, int Bodies, int Vertices) ComposeParticleSystems(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        int[] sceneIndices,
        int[] particleMaterialIndices,
        ImmutableArray<string>.Builder skipped)
    {
        int systems = 0;
        int bodies = 0;
        int vertices = 0;
        var claimed = new HashSet<int>();

        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind != UsdPhysicsExtractionObjectKind.ParticleSystem || !item.IsEnabled)
            {
                continue;
            }
            if (!ReadFlag(page, item, UsdPhysicsExtractionKey.ParticleSystemEnabled, true))
            {
                skipped.Add($"{item.Path} disables its particle system, so it is not simulated.");
                continue;
            }
            int sceneIndex = ResolveScene(page, item, sceneIndices);
            if (sceneIndex < 0)
            {
                skipped.Add(
                    $"{item.Path} is a particle system with no simulated scene, so it is not simulated.");
                continue;
            }

            // The body window has to be contiguous, so every body of this system is
            // staged before the system record that names the window is added.
            uint bodyOffset = (uint)builder.ParticleBodyCount;
            int staged = 0;
            for (int candidate = 0; candidate < page.ObjectCount; candidate++)
            {
                UsdPhysicsExtractionObject body = page.GetObject(candidate);
                if (body.Kind != UsdPhysicsExtractionObjectKind.ParticleSet || !body.IsEnabled)
                {
                    continue;
                }
                if (ResolveParticleSystem(page, body) != index || !claimed.Add(candidate))
                {
                    continue;
                }
                if (!ReadFlag(page, body, UsdPhysicsExtractionKey.ParticleBodyEnabled, true))
                {
                    skipped.Add($"{body.Path} disables its particle set, so it is not simulated.");
                    continue;
                }
                if (!TryComposeParticleBody(
                        page, builder, body, particleMaterialIndices, out string reason))
                {
                    skipped.Add($"{body.Path} {reason}, so it is not simulated.");
                    continue;
                }
                staged++;
                vertices += body.PointCount;
            }

            float particleContactOffset = ToFloat(
                NonNegativeLength(page, item, UsdPhysicsExtractionKey.ParticleSystemParticleContactOffset),
                0.0F);
            float solidRest = ClampRest(
                ToFloat(
                    NonNegativeLength(page, item, UsdPhysicsExtractionKey.ParticleSystemSolidRestOffset),
                    0.0F),
                particleContactOffset);
            float fluidRest = ClampRest(
                ToFloat(
                    NonNegativeLength(page, item, UsdPhysicsExtractionKey.ParticleSystemFluidRestOffset),
                    0.0F),
                particleContactOffset);
            float contactOffset = ToFloat(
                NonNegativeLength(page, item, UsdPhysicsExtractionKey.ParticleSystemContactOffset),
                0.0F);
            float restOffset = ClampRest(
                ToFloat(
                    NonNegativeLength(page, item, UsdPhysicsExtractionKey.ParticleSystemRestOffset), 0.0F),
                contactOffset);

            var system = new PhysxParticleSystemDesc
            {
                Id = builder.DefineIdentity(item.Path),
                SceneIndex = sceneIndex,
                Flags = ParticleSystemFlags(page, item),
                ContactOffset = contactOffset,
                RestOffset = restOffset,
                ParticleContactOffset = particleContactOffset,
                SolidRestOffset = solidRest,
                FluidRestOffset = fluidRest,
                MaxDepenetrationVelocity = ToFloat(
                    NonNegativeLength(
                        page, item, UsdPhysicsExtractionKey.ParticleSystemMaxDepenetrationVelocity),
                    0.0F),
                NeighborhoodScale = Positive(
                    page, item, UsdPhysicsExtractionKey.ParticleSystemNeighborhoodScale, 1.01),
                MaxNeighborhood = ReadNeighborhood(page, item),
                SolverPositionIterations = ReadIterations(
                    page, item, UsdPhysicsExtractionKey.ParticleSystemSolverPositionIterations, 4u),
                Wind = ReadWind(page, item),
                BodyOffset = bodyOffset,
                BodyCount = (uint)staged,
                Reserved0 = 0,
                Reserved1 = 0,
            };
            builder.AddParticleSystem(in system);
            systems++;
            bodies += staged;
        }

        // A particle set that names no system, or names one that was not simulated,
        // is reported by itself rather than silently belonging to nothing.
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind == UsdPhysicsExtractionObjectKind.ParticleSet && item.IsEnabled &&
                !claimed.Contains(index))
            {
                skipped.Add(
                    $"{item.Path} is a particle set that names no simulated particle system, so it is not simulated.");
            }
        }

        return (systems, bodies, vertices);
    }

    private static bool TryComposeParticleBody(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        UsdPhysicsExtractionObject item,
        int[] particleMaterialIndices,
        out string reason)
    {
        if (item.PointCount <= 0)
        {
            reason = "is a particle set whose geometry carries no points";
            return false;
        }
        if ((uint)item.PointCount > PhysxAbi.MaxParticlesPerBody)
        {
            reason = "is a particle set with more particles than the runtime accepts";
            return false;
        }

        if (!TryResolvePointScale(page, item, out (double X, double Y, double Z) scale))
        {
            reason = "is a particle set whose authored scale or stage units cannot be simulated";
            return false;
        }

        var points = new PhysxVec3f[item.PointCount];
        for (int offset = 0; offset < item.PointCount; offset++)
        {
            if (!TryScalePoint(page.GetPoint(item.PointStart + offset), scale, out PhysxVec3f point))
            {
                reason = "declares particle positions that are not finite";
                return false;
            }
            points[offset] = point;
        }

        uint flags = 0;
        if (ReadFlag(page, item, UsdPhysicsExtractionKey.ParticleBodyFluid))
        {
            flags |= (uint)PhysxParticleBodyFlags.Fluid;
        }
        if (ReadFlag(page, item, UsdPhysicsExtractionKey.ParticleBodySelfCollision, true))
        {
            flags |= (uint)PhysxParticleBodyFlags.SelfCollision;
        }

        var body = new PhysxParticleBodyDesc
        {
            Id = builder.DefineIdentity(item.Path),
            Kind = (uint)PhysxParticleBodyKind.Set,
            Flags = flags,
            ParticleGroup = ReadGroup(page, item),
            MaterialIndex = ResolveMaterialIndex(page, item, particleMaterialIndices),
            Mass = ToFloat(
                NonNegative(page, item, UsdPhysicsExtractionKey.ParticleBodyMass, 0.0) *
                    page.KilogramsPerUnit,
                0.0F),
            PointOffset = builder.AddMeshPoints(points),
            PointCount = (uint)points.Length,
            WorldPose = ToTransform(item),
            Reserved0 = 0,
            Reserved1 = 0,
        };
        if (!float.IsFinite(body.Mass) || body.Mass < 0.0F)
        {
            body.Mass = 0.0F;
        }
        builder.AddParticleBody(in body);
        reason = string.Empty;
        return true;
    }

    private static (int Surfaces, int Volumes, int Vertices) ComposeDeformables(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        int[] sceneIndices,
        int[] deformableMaterialIndices,
        ImmutableArray<string>.Builder skipped)
    {
        int surfaces = 0;
        int volumes = 0;
        int vertices = 0;
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            bool surface = item.Kind == UsdPhysicsExtractionObjectKind.SurfaceDeformable;
            bool volume = item.Kind == UsdPhysicsExtractionObjectKind.VolumeDeformable;
            if ((!surface && !volume) || !item.IsEnabled)
            {
                continue;
            }
            string label = surface ? "surface deformable" : "volume deformable";
            if (!ReadFlag(page, item, UsdPhysicsExtractionKey.DeformableEnabled, true))
            {
                skipped.Add($"{item.Path} disables its {label}, so it is not simulated.");
                continue;
            }
            int sceneIndex = ResolveScene(page, item, sceneIndices);
            if (sceneIndex < 0)
            {
                skipped.Add($"{item.Path} is a {label} with no simulated scene, so it is not simulated.");
                continue;
            }

            var deformable = new PhysxDeformableDesc
            {
                Id = builder.DefineIdentity(item.Path),
                SceneIndex = sceneIndex,
                Kind = surface ? (uint)PhysxDeformableKind.Surface : (uint)PhysxDeformableKind.Volume,
                Flags = DeformableFlags(page, item, surface),
                MaterialIndex = ResolveMaterialIndex(page, item, deformableMaterialIndices),
                SolverPositionIterations = ReadIterations(
                    page, item, UsdPhysicsExtractionKey.DeformableSolverPositionIterations, 16u),
                VertexVelocityDamping = Positive(
                    page, item, UsdPhysicsExtractionKey.DeformableVertexVelocityDamping, 0.005),
                SelfCollisionFilterDistance = ToFloat(
                    NonNegativeLength(
                        page, item, UsdPhysicsExtractionKey.DeformableSelfCollisionFilterDistance),
                    0.1F),
                WorldPose = ToTransform(item),
                Reserved0 = 0,
                Reserved1 = 0,
            };

            if (surface)
            {
                deformable.CollisionIterationMultiplier = ReadIterations(
                    page, item, UsdPhysicsExtractionKey.DeformableCollisionIterationMultiplier, 0u);
                deformable.CollisionPairUpdateFrequency = ReadIterations(
                    page, item, UsdPhysicsExtractionKey.DeformableCollisionPairUpdateFrequency, 0u);
                deformable.MaxDisplacement = ToFloat(
                    NonNegativeLength(page, item, UsdPhysicsExtractionKey.DeformableMaxDisplacement),
                    0.0F);
                if (!TryComposeSurfaceMesh(page, builder, item, ref deformable, out string reason))
                {
                    skipped.Add($"{item.Path} {reason}, so it is not simulated.");
                    continue;
                }
                surfaces++;
            }
            else
            {
                deformable.MaxDepenetrationVelocity = ToFloat(
                    NonNegativeLength(
                        page, item, UsdPhysicsExtractionKey.DeformableMaxDepenetrationVelocity),
                    0.0F);
                deformable.SettlingThreshold = Positive(
                    page, item, UsdPhysicsExtractionKey.DeformableSettlingThreshold, 0.0);
                deformable.SleepThreshold = Positive(
                    page, item, UsdPhysicsExtractionKey.DeformableSleepThreshold, 0.0);
                if (!TryComposeVolumeMesh(page, builder, item, ref deformable, out string reason))
                {
                    skipped.Add($"{item.Path} {reason}, so it is not simulated.");
                    continue;
                }
                volumes++;
            }

            builder.AddDeformable(in deformable);
            vertices += (int)deformable.PointCount;
        }

        return (surfaces, volumes, vertices);
    }

    private static bool TryComposeSurfaceMesh(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        UsdPhysicsExtractionObject item,
        ref PhysxDeformableDesc deformable,
        out string reason)
    {
        if (item.PointCount < 3)
        {
            reason = "is a surface deformable whose geometry carries fewer than three vertices";
            return false;
        }
        if ((uint)item.PointCount > PhysxAbi.MaxDeformableVertices)
        {
            reason = "is a surface deformable with more vertices than the runtime accepts";
            return false;
        }
        if (item.IndexCount < 3 || item.IndexCount % 3 != 0)
        {
            reason = "is a surface deformable whose topology is not made of whole triangles";
            return false;
        }

        if (!TryResolvePointScale(page, item, out (double X, double Y, double Z) scale))
        {
            reason =
                "is a surface deformable whose authored scale or stage units cannot be simulated";
            return false;
        }

        var points = new PhysxVec3f[item.PointCount];
        for (int offset = 0; offset < item.PointCount; offset++)
        {
            if (!TryScalePoint(page.GetPoint(item.PointStart + offset), scale, out PhysxVec3f point))
            {
                reason = "declares surface vertices that are not finite";
                return false;
            }
            points[offset] = point;
        }

        var triangles = new uint[item.IndexCount];
        for (int offset = 0; offset < item.IndexCount; offset++)
        {
            int value = page.GetIndex(item.IndexStart + offset);
            if (value < 0 || value >= points.Length)
            {
                reason = "declares surface indices outside its own vertices";
                return false;
            }
            triangles[offset] = (uint)value;
        }

        deformable.PointOffset = builder.AddMeshPoints(points);
        deformable.PointCount = (uint)points.Length;
        deformable.IndexOffset = builder.AddMeshIndices(triangles);
        deformable.IndexCount = (uint)triangles.Length;
        reason = string.Empty;
        return true;
    }

    private static bool TryComposeVolumeMesh(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        UsdPhysicsExtractionObject item,
        ref PhysxDeformableDesc deformable,
        out string reason)
    {
        // A volume is solved on an authored tetrahedral simulation mesh. Nothing
        // here tetrahedralizes a surface: building a simulation mesh from a render
        // mesh is a device side operation, so a stage that authors no tetrahedra
        // is reported rather than approximated on the CPU.
        double[] restPoints = ReadNumbers(
            page, item, UsdPhysicsExtractionKey.DeformableSimulationRestPoints);
        double[] simulationIndices = ReadNumbers(
            page, item, UsdPhysicsExtractionKey.DeformableSimulationIndices);
        if (restPoints.Length < 12 || restPoints.Length % 3 != 0)
        {
            reason =
                "is a volume deformable that authors no tetrahedral simulation vertices, which " +
                "cannot be built on the CPU";
            return false;
        }
        if (simulationIndices.Length < 4 || simulationIndices.Length % 4 != 0)
        {
            reason = "is a volume deformable whose simulation topology is not made of whole tetrahedra";
            return false;
        }

        int pointCount = restPoints.Length / 3;
        if ((uint)pointCount > PhysxAbi.MaxDeformableVertices)
        {
            reason = "is a volume deformable with more simulation vertices than the runtime accepts";
            return false;
        }

        if (!TryResolvePointScale(page, item, out (double X, double Y, double Z) scale))
        {
            reason =
                "is a volume deformable whose authored scale or stage units cannot be simulated";
            return false;
        }

        if (!TryStageLocalPoints(builder, restPoints, scale, out uint pointOffset))
        {
            reason = "declares simulation vertices that are not finite";
            return false;
        }
        if (!TryStageIndices(builder, simulationIndices, pointCount, out uint indexOffset))
        {
            reason = "declares simulation indices outside its own simulation vertices";
            return false;
        }

        deformable.PointOffset = pointOffset;
        deformable.PointCount = (uint)pointCount;
        deformable.IndexOffset = indexOffset;
        deformable.IndexCount = (uint)simulationIndices.Length;

        double[] collisionPoints = ReadNumbers(
            page, item, UsdPhysicsExtractionKey.DeformableCollisionRestPoints);
        double[] collisionIndices = ReadNumbers(
            page, item, UsdPhysicsExtractionKey.DeformableCollisionIndices);
        if (collisionPoints.Length >= 12 && collisionPoints.Length % 3 == 0 &&
            collisionIndices.Length >= 4 && collisionIndices.Length % 4 == 0)
        {
            int collisionCount = collisionPoints.Length / 3;
            if ((uint)collisionCount <= PhysxAbi.MaxDeformableVertices &&
                TryStageLocalPoints(
                    builder, collisionPoints, scale, out uint collisionPointOffset) &&
                TryStageIndices(builder, collisionIndices, collisionCount, out uint collisionIndexOffset))
            {
                deformable.CollisionPointOffset = collisionPointOffset;
                deformable.CollisionPointCount = (uint)collisionCount;
                deformable.CollisionIndexOffset = collisionIndexOffset;
                deformable.CollisionIndexCount = (uint)collisionIndices.Length;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryStageLocalPoints(
        PhysxPageBuilder builder,
        double[] values,
        (double X, double Y, double Z) scale,
        out uint offset)
    {
        offset = 0;
        var points = new PhysxVec3f[values.Length / 3];
        for (int index = 0; index < points.Length; index++)
        {
            var point = new PhysxVec3f(
                ToFloat(values[(index * 3) + 0] * scale.X, float.NaN),
                ToFloat(values[(index * 3) + 1] * scale.Y, float.NaN),
                ToFloat(values[(index * 3) + 2] * scale.Z, float.NaN));
            if (!point.IsFinite)
            {
                return false;
            }
            points[index] = point;
        }

        offset = builder.AddMeshPoints(points);
        return true;
    }

    private static bool TryStageIndices(
        PhysxPageBuilder builder,
        double[] values,
        int pointCount,
        out uint offset)
    {
        offset = 0;
        var indices = new uint[values.Length];
        for (int index = 0; index < indices.Length; index++)
        {
            double value = values[index];
            if (!double.IsFinite(value) || value < 0.0 || value >= pointCount)
            {
                return false;
            }
            indices[index] = (uint)value;
        }

        offset = builder.AddMeshIndices(indices);
        return true;
    }

    /// <summary>Reports every authored GPU object the build page contract cannot carry.</summary>
    private static void ReportUnsupportedGpuObjects(
        UsdPhysicsExtractionPage page,
        ImmutableArray<string>.Builder skipped)
    {
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (!item.IsEnabled)
            {
                continue;
            }
            switch (item.Kind)
            {
                case UsdPhysicsExtractionObjectKind.ParticleCloth:
                    skipped.Add(
                        $"{item.Path} authors particle cloth, which this build does not implement, " +
                        "so it is not simulated; apply OpenUsdPhysicsSurfaceDeformableAPI instead, " +
                        "which is the supported cloth path.");
                    break;
                case UsdPhysicsExtractionObjectKind.DiffuseParticles:
                    skipped.Add(
                        $"{item.Path} authors diffuse particles, which the build page contract does not carry, " +
                        "so they are not simulated.");
                    break;
                default:
                    break;
            }
        }
    }

    private static uint ParticleSystemFlags(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item)
    {
        uint flags = 0;
        if (ReadFlag(page, item, UsdPhysicsExtractionKey.ParticleSystemEnableCcd))
        {
            flags |= (uint)PhysxParticleSystemFlags.EnableCcd;
        }
        if (ReadFlag(page, item, UsdPhysicsExtractionKey.ParticleSystemGlobalSelfCollision, true))
        {
            flags |= (uint)PhysxParticleSystemFlags.GlobalSelfCollision;
        }
        if (ReadFlag(page, item, UsdPhysicsExtractionKey.ParticleSystemNonParticleCollision, true))
        {
            flags |= (uint)PhysxParticleSystemFlags.NonParticleCollision;
        }
        return flags;
    }

    private static uint DeformableFlags(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        bool surface)
    {
        uint flags = 0;
        if (ReadFlag(page, item, UsdPhysicsExtractionKey.DeformableEnableCcd))
        {
            flags |= (uint)PhysxDeformableFlags.EnableCcd;
        }
        if (ReadFlag(page, item, UsdPhysicsExtractionKey.DeformableSelfCollision))
        {
            flags |= (uint)PhysxDeformableFlags.SelfCollision;
        }
        if (!surface && ReadFlag(page, item, UsdPhysicsExtractionKey.DeformableKinematic))
        {
            flags |= (uint)PhysxDeformableFlags.Kinematic;
        }
        return flags;
    }

    private static uint ReadNeighborhood(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item)
    {
        double value = NonNegative(
            page, item, UsdPhysicsExtractionKey.ParticleSystemMaxNeighborhood, 0.0);
        if (value <= 0.0)
        {
            return 0;
        }
        return (uint)Math.Clamp(
            value, PhysxAbi.MinParticleNeighborhood, PhysxAbi.MaxParticleNeighborhood);
    }

    private static uint ReadGroup(UsdPhysicsExtractionPage page, UsdPhysicsExtractionObject item)
    {
        double value = NonNegative(page, item, UsdPhysicsExtractionKey.ParticleBodyGroup, 0.0);
        // The phase group is twenty bits wide in the solver and the build page
        // bounds it to the same width, so a larger authored group is clamped to
        // the last usable group rather than wrapping into a neighbouring one.
        return value <= 0.0 ? 0u : (uint)Math.Min(value, PhysxAbi.MaxParticleGroup);
    }

    private static PhysxVec3f ReadWind(UsdPhysicsExtractionPage page, UsdPhysicsExtractionObject item)
    {
        AuthoredValue state = ReadAuthoredVector(
            page, item, UsdPhysicsExtractionKey.ParticleSystemWind, out var authored);
        if (state != AuthoredValue.Usable)
        {
            return default;
        }
        double scale = page.MetersPerUnit;
        var wind = new PhysxVec3f(
            ToFloat(authored.X * scale, float.NaN),
            ToFloat(authored.Y * scale, float.NaN),
            ToFloat(authored.Z * scale, float.NaN));
        return wind.IsFinite ? wind : default;
    }

    /// <summary>Reads one non negative authored length and converts it into metres.</summary>
    /// <remarks>
    /// The project schemas state a negative offset as "ask the runtime for its own default", which
    /// the build page spells as zero, so a negative authored value is folded to zero rather than
    /// reported as a malformed one.
    /// </remarks>
    private static double NonNegativeLength(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key)
    {
        double value = ReadScalar(page, item, key, 0.0);
        if (!double.IsFinite(value) || value <= 0.0)
        {
            return 0.0;
        }
        return value * page.MetersPerUnit;
    }

    /// <summary>Reads one non negative authored number, falling back when it cannot be used.</summary>
    private static float Positive(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        double fallback) =>
        ToFloat(NonNegative(page, item, key, fallback), ToFloat(fallback, 0.0F));

    /// <summary>Clamps one rest offset so it can never exceed the contact offset it belongs to.</summary>
    private static float ClampRest(float rest, float contactOffset) =>
        contactOffset > 0.0F && rest > contactOffset ? contactOffset : rest;

    private static int ResolveScene(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        int[] sceneIndices)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (item.SceneIndex >= 0 && item.SceneIndex < sceneIndices.Length)
        {
            int mapped = sceneIndices[item.SceneIndex];
            if (mapped >= 0)
            {
                return mapped;
            }
        }

        // An object whose authored owner was not simulated still belongs to the
        // world, so it joins the first simulated scene rather than disappearing.
        for (int index = 0; index < sceneIndices.Length; index++)
        {
            if (sceneIndices[index] >= 0)
            {
                return sceneIndices[index];
            }
        }

        return -1;
    }

    private static int ResolveParticleSystem(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item)
    {
        var targets = new List<int>();
        CollectTargets(page, item, UsdPhysicsExtractionKey.ParticleSystemTargets, targets);
        foreach (int target in targets)
        {
            if (page.GetObject(target).Kind == UsdPhysicsExtractionObjectKind.ParticleSystem)
            {
                return target;
            }
        }
        return -1;
    }

    private static int ResolveMaterialIndex(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        int[] materialIndices)
    {
        var targets = new List<int>();
        CollectTargets(page, item, UsdPhysicsExtractionKey.DeformableMaterialTargets, targets);
        CollectTargets(page, item, UsdPhysicsExtractionKey.MaterialBindingTargets, targets);
        foreach (int target in targets)
        {
            if (target >= 0 && target < materialIndices.Length && materialIndices[target] >= 0)
            {
                return materialIndices[target];
            }
        }
        return -1;
    }
}
