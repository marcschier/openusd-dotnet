// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;
using System.Globalization;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Extraction;

/// <summary>Reports what one composition took from an extraction page.</summary>
/// <param name="Scenes">How many scenes were staged.</param>
/// <param name="Materials">How many materials were staged.</param>
/// <param name="Shapes">How many shapes were staged.</param>
/// <param name="Actors">How many actors were staged.</param>
/// <param name="Joints">How many joints were staged.</param>
/// <param name="FilterPairs">How many suppressed collision pairs were staged.</param>
/// <param name="Articulations">How many articulations were staged.</param>
/// <param name="Controllers">How many controllers were staged.</param>
/// <param name="Tendons">How many articulation tendons were staged.</param>
/// <param name="MimicJoints">How many articulation mimic joints were staged.</param>
/// <param name="Vehicles">How many vehicles were staged.</param>
/// <param name="Gpu">What the CUDA accelerated domains staged.</param>
/// <param name="Skipped">The ordered notes describing every object that was left out.</param>
internal sealed record UsdPhysicsCompositionReport(
    int Scenes,
    int Materials,
    int Shapes,
    int Actors,
    int Joints,
    int FilterPairs,
    int Articulations,
    int Controllers,
    int Tendons,
    int MimicJoints,
    int Vehicles,
    UsdPhysicsGpuCompositionCounts Gpu,
    ImmutableArray<string> Skipped);

/// <summary>
/// Projects the simulated subset of an extraction page onto the existing build page builder.
/// </summary>
/// <remarks>
/// The extraction page deliberately carries more than the runtime can simulate today, so this
/// composer is the narrow place where the supported subset is selected. Anything it cannot map
/// is skipped individually with an ordered note; the remaining domains still compose.
/// </remarks>
internal static partial class UsdPhysicsExtractionComposer
{
    private const float DefaultDensity = 1000.0F;
    private const float DefaultContactOffset = 0.02F;
    private const float DefaultGravity = 9.81F;
    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>The widest swing cone the solver accepts, which stands for an unlimited side.</summary>
    private const double MaxConeAngle = Math.PI - 0.001;
    private const double HalfRootTwo = 0.70710678118654752440;

    /// <summary>Composes one extraction page into a build page builder.</summary>
    /// <param name="page">The extraction page.</param>
    /// <param name="builder">The builder to stage into.</param>
    /// <returns>The composition report.</returns>
    internal static UsdPhysicsCompositionReport Compose(
        UsdPhysicsExtractionPage page, PhysxPageBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(builder);

        var skipped = ImmutableArray.CreateBuilder<string>();
        ApplyStageMetadata(page, builder);

        int[] sceneIndices = ComposeScenes(page, builder, skipped);
        int[] materialIndices = ComposeMaterials(page, builder, skipped);
        int[] shapeIndices = ComposeShapes(page, builder, materialIndices, skipped);

        // A body an articulation claims is simulated as a reduced coordinate link, so it must
        // not also reach the actor section and its inbound joint must not also reach the joint
        // section; either would simulate the same prim twice. Ownership is resolved once, before
        // anything is composed, so that overlapping roots cannot claim the same body twice and
        // give the world two links with one identity.
        Dictionary<int, int> articulationOwners = ResolveArticulationOwnership(
            page, out HashSet<int> refusedArticulations, skipped);
        var articulationLinks = new HashSet<int>(articulationOwners.Keys);
        int[] actorIndices = ComposeActors(
            page, builder, sceneIndices, shapeIndices, articulationLinks, skipped);
        int joints = ComposeJoints(page, builder, actorIndices, articulationLinks, skipped);
        int pairs = ComposeFilterPairs(page, builder, actorIndices, skipped);
        var composedArticulations = new List<ComposedArticulation>();
        int articulations = ComposeArticulations(
            page,
            builder,
            sceneIndices,
            shapeIndices,
            composedArticulations,
            refusedArticulations,
            skipped);
        int controllers = ComposeControllers(page, builder, sceneIndices, skipped);
        int tendons = ComposeTendons(page, builder, composedArticulations, skipped);
        int mimicJoints = ComposeMimicJoints(page, builder, composedArticulations, skipped);
        int vehicles = ComposeVehicles(page, builder, sceneIndices, actorIndices, skipped);

        // The CUDA accelerated domains compose last, exactly like they build
        // last, so a stage that mixes them with rigid bodies loses at most the
        // GPU objects and never the CPU ones.
        UsdPhysicsGpuCompositionCounts gpu = ComposeGpuDomains(page, builder, sceneIndices, skipped);

        return new UsdPhysicsCompositionReport(
            builder.SceneCount,
            builder.MaterialCount,
            builder.ShapeCount,
            builder.ActorCount,
            joints,
            pairs,
            articulations,
            controllers,
            tendons,
            mimicJoints,
            vehicles,
            gpu,
            skipped.ToImmutable());
    }

    private static void ApplyStageMetadata(UsdPhysicsExtractionPage page, PhysxPageBuilder builder)
    {
        // The extraction already reports metres, kilograms and a Y up basis, so the build page
        // describes the simulation space directly rather than repeating the stage conversion.
        builder.MetersPerUnit = 1.0;
        builder.KilogramsPerUnit = 1.0;
        builder.TimeCodesPerSecond = page.TimeCodesPerSecond;
        builder.StartTimeCode = page.StartTimeCode;
        builder.EndTimeCode = page.EndTimeCode;
        builder.UpAxis = PhysxUpAxis.Y;
        builder.SourceHash = page.FingerprintLow ^ page.FingerprintHigh;
    }

    private static int[] ComposeScenes(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        ImmutableArray<string>.Builder skipped)
    {
        int[] indices = CreateMap(page.ObjectCount);
        var space = UsdPhysicsExtractionSpace.FromPage(page);

        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind != UsdPhysicsExtractionObjectKind.Scene || !item.IsEnabled)
            {
                continue;
            }

            var scene = new PhysxSceneDesc
            {
                Id = builder.DefineIdentity(item.Path),
                GravityDirection = ComposeGravityDirection(page, item, space, skipped),
                GravityMagnitude = ComposeSceneScalar(
                    page,
                    item,
                    UsdPhysicsExtractionKey.SceneGravityMagnitude,
                    page.MetersPerUnit,
                    DefaultGravity,
                    "gravity magnitude",
                    skipped),
                Flags = SceneFlags(page, item),
                PositionIterations = ReadIterations(
                    page, item, UsdPhysicsExtractionKey.ScenePositionIterations, 4u),
                VelocityIterations = ReadIterations(
                    page, item, UsdPhysicsExtractionKey.SceneVelocityIterations, 1u),
                // The threshold is a relative normal speed in stage linear units per second, and
                // PhysX reads it in metres per second, so it converts exactly like gravity. The
                // fallback is PhysX's own default and is already stated in metres per second.
                BounceThreshold = ComposeSceneScalar(
                    page,
                    item,
                    UsdPhysicsExtractionKey.SceneBounceThreshold,
                    page.MetersPerUnit,
                    0.2F,
                    "bounce threshold",
                    skipped),
                ContactOffset = DefaultContactOffset,
                Reserved0 = 0,
            };
            indices[index] = builder.AddScene(in scene);
        }

        return indices;
    }

    /// <summary>Composes the gravity direction of one scene in simulation space.</summary>
    /// <remarks>
    /// Only an authored direction lives in stage space. The fallback is already the simulation
    /// space down axis, so rotating it would tip gravity sideways on a stage that is not Y up.
    /// </remarks>
    private static PhysxVec3f ComposeGravityDirection(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionSpace space,
        ImmutableArray<string>.Builder notes)
    {
        var down = new PhysxVec3f(0.0F, -1.0F, 0.0F);
        AuthoredValue state = ReadAuthoredVector(
            page, item, UsdPhysicsExtractionKey.SceneGravityDirection, out var authored);
        if (state == AuthoredValue.Missing)
        {
            return down;
        }
        if (state == AuthoredValue.Usable)
        {
            // USD states an unauthored direction as the zero vector, which means simulation down.
            if (authored.X == 0.0 && authored.Y == 0.0 && authored.Z == 0.0)
            {
                return down;
            }
            if (TryNormalizeDirection(
                space.ToSimulationDirection(authored), out PhysxVec3f direction))
            {
                return direction;
            }
        }

        notes.Add(
            $"{item.Path} authors a gravity direction that cannot be simulated, so gravity points down.");
        return down;
    }

    /// <summary>Composes one non negative scene number, or the default when it is not usable.</summary>
    private static float ComposeSceneScalar(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        double scale,
        float fallback,
        string label,
        ImmutableArray<string>.Builder notes)
    {
        AuthoredValue state = ReadAuthoredScalar(page, item, key, out double authored);
        if (state == AuthoredValue.Missing)
        {
            return fallback;
        }
        if (state == AuthoredValue.Usable && authored >= 0.0 &&
            TryToFloat(authored * scale, out float value) && value >= 0.0F)
        {
            return value;
        }

        notes.Add($"{item.Path} authors a {label} that cannot be simulated, so the default is used.");
        return fallback;
    }

    private static uint SceneFlags(UsdPhysicsExtractionPage page, UsdPhysicsExtractionObject item)
    {
        uint flags = 0;
        if (ReadFlag(page, item, UsdPhysicsExtractionKey.SceneEnableCcd))
        {
            flags |= (uint)PhysxSceneFlags.EnableCcd;
        }
        if (ReadFlag(page, item, UsdPhysicsExtractionKey.SceneEnableDeterminism))
        {
            flags |= (uint)PhysxSceneFlags.EnableEnhancedDeterminism;
        }
        return flags;
    }

    private static int[] ComposeMaterials(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        ImmutableArray<string>.Builder skipped)
    {
        int[] indices = CreateMap(page.ObjectCount);
        int authored = 0;
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind != UsdPhysicsExtractionObjectKind.Material)
            {
                continue;
            }

            authored++;
            if (!item.IsEnabled)
            {
                continue;
            }

            double density = ReadScalar(
                page, item, UsdPhysicsExtractionKey.MaterialDensity, double.NaN);
            density = double.IsFinite(density) && density > 0.0
                ? density * DensityScale(page)
                : DefaultDensity;
            double restitution = ReadScalar(
                page, item, UsdPhysicsExtractionKey.MaterialRestitution, 0.0);
            restitution = double.IsFinite(restitution) ? Math.Clamp(restitution, 0.0, 1.0) : 0.0;

            var material = new PhysxMaterialDesc
            {
                Id = builder.DefineIdentity(item.Path),
                StaticFriction = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.MaterialStaticFriction, 0.5),
                    0.5F),
                DynamicFriction = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.MaterialDynamicFriction, 0.5),
                    0.5F),
                Restitution = ToFloat(restitution, 0.0F),
                Density = ToFloat(density, DefaultDensity),
                Flags = 0,
                FrictionCombineMode = (uint)PhysxCombineMode.Average,
                RestitutionCombineMode = (uint)PhysxCombineMode.Average,
                Damping = 0.0F,
            };
            indices[index] = builder.AddMaterial(in material);
        }

        if (authored > 0 && indices.All(static value => value < 0))
        {
            skipped.Add("No authored rigid body material was usable; shapes use the world default.");
        }

        return indices;
    }

    private static int[] ComposeShapes(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        int[] materialIndices,
        ImmutableArray<string>.Builder skipped)
    {
        int[] indices = CreateMap(page.ObjectCount);
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind != UsdPhysicsExtractionObjectKind.Collider || !item.IsEnabled)
            {
                continue;
            }

            if (!TryMapGeometry(item.Geometry, out PhysxShapeType type))
            {
                skipped.Add($"{item.Path} uses the unsupported collision geometry {item.Geometry}.");
                continue;
            }

            PhysxShapeDesc shape = DescribeShape(page, builder, item, type, materialIndices, skipped);
            if (!IsUsable(in shape, type))
            {
                skipped.Add($"{item.Path} declares collision geometry that is not usable.");
                continue;
            }

            if (type is PhysxShapeType.ConvexMesh or PhysxShapeType.TriangleMesh)
            {
                if (!TryStageMesh(page, builder, item, ref shape, out string reason))
                {
                    skipped.Add($"{item.Path} {reason}.");
                    continue;
                }
            }

            indices[index] = builder.AddShape(in shape);
        }

        return indices;
    }

    private static PhysxShapeDesc DescribeShape(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        UsdPhysicsExtractionObject item,
        PhysxShapeType type,
        int[] materialIndices,
        ImmutableArray<string>.Builder notes)
    {
        (double x, double y, double z) = item.Extent;

        // A dimension that does not survive the narrowing arrives as zero, which the usability
        // check below rejects for every analytic shape and which a mesh shape never reads.
        PhysxVec3f extents = TryToVector((x, y, z), out PhysxVec3f narrowed) ? narrowed : default;

        return new PhysxShapeDesc
        {
            Id = builder.DefineIdentity(item.Path + ".shape"),
            Type = (uint)type,
            Flags = ReadFlag(page, item, UsdPhysicsExtractionKey.CollisionEnabled, fallback: true)
                ? 0u
                : (uint)PhysxShapeFlags.DisableCollision,
            LocalPose = ComposeLocalPose(page, item),
            Scale = ComposeScale(item, notes),
            HalfExtents = extents,
            Radius = ToFloat(x, 0.0F),
            HalfHeight = ToFloat(y, 0.0F),
            PointOffset = 0,
            PointCount = 0,
            IndexOffset = 0,
            IndexCount = 0,
            MaterialIndex = ResolveMaterial(page, item, materialIndices),
            Reserved0 = 0,
            Reserved1 = 0,
        };
    }

    /// <summary>
    /// Places one collider inside the actor that owns it and aligns its authored geometry axis
    /// with the simulation convention.
    /// </summary>
    private static PhysxTransform ComposeLocalPose(
        UsdPhysicsExtractionPage page, UsdPhysicsExtractionObject item)
    {
        (double W, double X, double Y, double Z) rotation = (1.0, 0.0, 0.0, 0.0);
        (double X, double Y, double Z) position = (0.0, 0.0, 0.0);

        // A collider under a body carries its own world pose, so the shape pose is the collider
        // pose expressed in the frame of the body. Both poses are already in simulation space, so
        // no further unit or basis work is needed here.
        if (item.ParentBodyIndex >= 0 && item.ParentBodyIndex != item.Index)
        {
            UsdPhysicsExtractionObject body = page.GetObject(item.ParentBodyIndex);
            (double W, double X, double Y, double Z) inverse = Conjugate(Normalize(body.Rotation));
            (double bodyX, double bodyY, double bodyZ) = body.Position;
            (double shapeX, double shapeY, double shapeZ) = item.Position;
            position = RotateVector(
                inverse, (shapeX - bodyX, shapeY - bodyY, shapeZ - bodyZ));
            rotation = Multiply(inverse, Normalize(item.Rotation));
        }

        rotation = Multiply(rotation, GeometryAxisRotation(item));

        var pose = new PhysxVec3f((float)position.X, (float)position.Y, (float)position.Z);
        var quaternion = new PhysxQuatf(
            (float)rotation.X, (float)rotation.Y, (float)rotation.Z, (float)rotation.W);
        return new PhysxTransform(
            pose.IsFinite ? pose : default,
            quaternion.IsUsableRotation ? quaternion : PhysxQuatf.Identity);
    }

    /// <summary>Rotates the simulation reference axis onto the authored geometry axis.</summary>
    private static (double W, double X, double Y, double Z) GeometryAxisRotation(
        UsdPhysicsExtractionObject item)
    {
        // Only the geometries that have an axis carry one; the extraction reports Y for the rest.
        if (item.Geometry is not (UsdPhysicsExtractionGeometryKind.Capsule
            or UsdPhysicsExtractionGeometryKind.Cylinder
            or UsdPhysicsExtractionGeometryKind.Cone
            or UsdPhysicsExtractionGeometryKind.Plane))
        {
            return (1.0, 0.0, 0.0, 0.0);
        }

        // Capsules, cylinders, cones and plane normals are all X aligned in simulation space.
        return item.GeometryAxis switch
        {
            1 => (HalfRootTwo, 0.0, 0.0, HalfRootTwo),
            2 => (HalfRootTwo, 0.0, -HalfRootTwo, 0.0),
            _ => (1.0, 0.0, 0.0, 0.0),
        };
    }

    /// <summary>Stages the topology of one mesh collider, or reports why it cannot be staged.</summary>
    /// <remarks>
    /// The mesh point section is shared by every collider of the page and is validated as a whole,
    /// so a single non finite point, or a shape the validator refuses for its point count, would
    /// fail the page for every other actor of the stage. The topology is therefore checked against
    /// the same minimum counts the page validator applies, a convex hull needs four points and a
    /// triangle mesh needs three, before any of it is staged, so only the collider that authored it
    /// is dropped and no orphan points are left behind.
    /// </remarks>
    private static bool TryStageMesh(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        UsdPhysicsExtractionObject item,
        ref PhysxShapeDesc shape,
        out string reason)
    {
        int minimumPoints = shape.Type == (uint)PhysxShapeType.ConvexMesh ? 4 : 3;
        if (item.PointCount < minimumPoints)
        {
            reason = item.PointCount == 0
                ? "declares mesh collision geometry with no topology"
                : $"declares mesh collision geometry with {item.PointCount} of the " +
                    $"{minimumPoints} points its shape needs";
            return false;
        }

        // An extracted point is authored, local, and in stage units, while every other length on
        // the build page is metres. The runtime scales a cooked mesh by the authored collider
        // scale, which is dimensionless, so the stage unit conversion has to happen here or a
        // mesh collider would be the only shape on the page whose size depends on the authored
        // metersPerUnit.
        double units = page.MetersPerUnit;
        var points = new PhysxVec3f[item.PointCount];
        for (int offset = 0; offset < item.PointCount; offset++)
        {
            (float x, float y, float z) = page.GetPoint(item.PointStart + offset);
            var point = new PhysxVec3f(
                ToFloat(x * units, float.NaN),
                ToFloat(y * units, float.NaN),
                ToFloat(z * units, float.NaN));
            if (!point.IsFinite)
            {
                reason = "declares mesh collision points that are not finite";
                return false;
            }
            points[offset] = point;
        }

        uint[]? triangles = null;
        if (shape.Type == (uint)PhysxShapeType.TriangleMesh)
        {
            if (item.IndexCount < 3 || item.IndexCount % 3 != 0)
            {
                reason = "declares triangle collision geometry without whole triangles";
                return false;
            }
            triangles = new uint[item.IndexCount];
            for (int offset = 0; offset < item.IndexCount; offset++)
            {
                int value = page.GetIndex(item.IndexStart + offset);
                if (value < 0 || value >= points.Length)
                {
                    reason = "declares triangle indices outside its own mesh points";
                    return false;
                }
                triangles[offset] = (uint)value;
            }
        }

        shape.PointOffset = builder.AddMeshPoints(points);
        shape.PointCount = (uint)points.Length;
        if (triangles is not null)
        {
            shape.IndexOffset = builder.AddMeshIndices(triangles);
            shape.IndexCount = (uint)triangles.Length;
        }

        reason = string.Empty;
        return true;
    }

    private static int[] ComposeActors(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        int[] sceneIndices,
        int[] shapeIndices,
        HashSet<int> articulationLinks,
        ImmutableArray<string>.Builder skipped)
    {
        int[] indices = CreateMap(page.ObjectCount);
        int defaultScene = ResolveDefaultScene(page, builder, sceneIndices, shapeIndices);

        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            bool isBody = item.Kind == UsdPhysicsExtractionObjectKind.RigidBody;
            bool isLooseCollider =
                item.Kind == UsdPhysicsExtractionObjectKind.Collider &&
                item.ParentBodyIndex < 0 &&
                shapeIndices[index] >= 0;
            if ((!isBody && !isLooseCollider) || !item.IsEnabled)
            {
                continue;
            }
            if (articulationLinks.Contains(index))
            {
                continue;
            }

            List<int> shapes = isBody
                ? CollectBodyShapes(page, shapeIndices, index)
                : [shapeIndices[index]];
            if (shapes.Count == 0)
            {
                skipped.Add($"{item.Path} declares a rigid body with no usable collision shape.");
                continue;
            }

            uint type = ActorType(item, isLooseCollider);
            if (type != (uint)PhysxActorType.Static &&
                shapes.Any(shape => IsMovableIncompatible(page, shapeIndices, shape)))
            {
                skipped.Add($"{item.Path} is movable and uses a plane or triangle mesh shape.");
                continue;
            }

            int scene = item.SceneIndex >= 0 && sceneIndices[item.SceneIndex] >= 0
                ? sceneIndices[item.SceneIndex]
                : defaultScene;
            if (scene < 0)
            {
                skipped.Add($"{item.Path} has no simulation scene to belong to.");
                continue;
            }

            uint shapeOffset = (uint)builder.AddActorShape(
                new PhysxActorShapeRef((uint)shapes[0], -1));
            for (int offset = 1; offset < shapes.Count; offset++)
            {
                builder.AddActorShape(new PhysxActorShapeRef((uint)shapes[offset], -1));
            }

            PhysxActorDesc actor = DescribeActor(page, builder, item, type, scene, skipped);
            actor.ShapeOffset = shapeOffset;
            actor.ShapeCount = (uint)shapes.Count;
            indices[index] = builder.AddActor(in actor);
        }

        return indices;
    }

    /// <summary>Describes one actor and reduces every authored value the runtime cannot take.</summary>
    /// <remarks>
    /// An unusable authored number never reaches the build page. The page validator rejects a whole
    /// page, so forwarding one bad value would take every other object of the stage down with it.
    /// Each rejected value falls back to the value that an unauthored property would have produced
    /// and adds one ordered note, so the actor still simulates and the loss stays visible.
    /// </remarks>
    private static PhysxActorDesc DescribeActor(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        UsdPhysicsExtractionObject item,
        uint type,
        int scene,
        ImmutableArray<string>.Builder notes)
    {
        var space = UsdPhysicsExtractionSpace.FromPage(page);
        bool movable = type != (uint)PhysxActorType.Static;
        float mass = ComposeMass(page, item, notes);

        // A velocity is authored in stage units per second, and USD states angular velocity in
        // degrees per second, so both change basis and both change unit. A static actor never
        // carries a velocity, so an unusable one is not worth a note there.
        PhysxVec3f linear = default;
        PhysxVec3f angular = default;
        if (movable)
        {
            AuthoredValue linearState = ReadAuthoredVector(
                page, item, UsdPhysicsExtractionKey.BodyVelocity, out var authoredLinear);
            linear = SanitizeVector(
                linearState,
                space.ToSimulation(authoredLinear),
                allowNegative: true,
                $"{item.Path} authors a linear velocity that cannot be simulated, so the body starts at rest.",
                notes);

            AuthoredValue angularState = ReadAuthoredVector(
                page, item, UsdPhysicsExtractionKey.BodyAngularVelocity, out var authoredAngular);
            angular = SanitizeVector(
                angularState,
                Scale(space.ToSimulationDirection(authoredAngular), DegreesToRadians),
                allowNegative: true,
                $"{item.Path} authors an angular velocity that cannot be simulated, so the body starts at rest.",
                notes);
        }

        // A center of mass and an inertia tensor are stated in the body local frame, which the
        // up axis change never rotates. Only the unit scale applies.
        AuthoredValue centerState = ReadAuthoredVector(
            page, item, UsdPhysicsExtractionKey.MassCenterOfMass, out var authoredCenter);
        PhysxVec3f center = SanitizeVector(
            centerState,
            Scale(authoredCenter, page.MetersPerUnit),
            allowNegative: true,
            $"{item.Path} authors a center of mass that cannot be simulated, so the body origin is used.",
            notes);

        AuthoredValue inertiaState = ReadAuthoredVector(
            page, item, UsdPhysicsExtractionKey.MassDiagonalInertia, out var authoredInertia);
        PhysxVec3f inertia = SanitizeVector(
            inertiaState,
            Scale(
                authoredInertia,
                page.KilogramsPerUnit * page.MetersPerUnit * page.MetersPerUnit),
            allowNegative: false,
            $"{item.Path} authors a diagonal inertia that cannot be simulated, so the collision shapes decide it.",
            notes);

        return new PhysxActorDesc
        {
            Id = builder.DefineIdentity(item.Path),
            SceneIndex = scene,
            Type = type,
            WorldPose = ToTransform(item),
            LinearVelocity = linear,
            AngularVelocity = angular,
            Mass = mass,
            CenterOfMass = center,
            Inertia = inertia,
            PrincipalAxes = ComposePrincipalAxes(page, item, notes),
            LinearDamping = ComposeDamping(
                page, item, UsdPhysicsExtractionKey.BodyLinearDamping, 0.0F, "linear damping", notes),
            AngularDamping = ComposeDamping(
                page, item, UsdPhysicsExtractionKey.BodyAngularDamping, 0.05F, "angular damping", notes),
            Flags = ActorFlags(page, item),
            ShapeOffset = 0,
            ShapeCount = 0,
            CollisionGroup = 0,

            // Version 4 of the world ABI finally carries the per body budgets
            // extraction has always read, so an authored limit no longer has to
            // be dropped on the way into the world. Each one still has to change
            // unit on the way there: the clamps and the sleep threshold are
            // authored in stage units and degrees, and PhysX reads metres,
            // radians, and a mass normalized kinetic energy in metres squared per
            // second squared.
            PositionIterations = ReadOptionalIterations(page, item, UsdPhysicsExtractionKey.BodyPositionIterations),
            VelocityIterations = ReadOptionalIterations(page, item, UsdPhysicsExtractionKey.BodyVelocityIterations),
            MaxLinearVelocity = ReadOptionalBudget(
                page, item, UsdPhysicsExtractionKey.BodyMaxLinearVelocity, page.MetersPerUnit),
            MaxAngularVelocity = ReadOptionalBudget(
                page, item, UsdPhysicsExtractionKey.BodyMaxAngularVelocity, DegreesToRadians),
            MaxDepenetrationVelocity = 0.0F,
            MaxContactImpulse = 0.0F,
            SleepThreshold = ReadOptionalBudget(
                page,
                item,
                UsdPhysicsExtractionKey.BodySleepThreshold,
                page.MetersPerUnit * page.MetersPerUnit),
            StabilizationThreshold = 0.0F,
            WakeCounter = 0.0F,
            MinCcdAdvanceCoefficient = 0.0F,
            ContactSlopCoefficient = 0.0F,
            Reserved0 = 0,
        };
    }

    /// <summary>
    /// Reads one optional solver iteration count, where zero means the owning scene decides it.
    /// </summary>
    private static uint ReadOptionalIterations(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key)
    {
        if (ReadAuthoredScalar(page, item, key, out double value) != AuthoredValue.Usable)
        {
            return 0;
        }
        return !double.IsFinite(value) || value < 1.0 ? 0u : (uint)Math.Min(value, 255.0);
    }

    /// <summary>
    /// Reads one optional non negative budget in simulation units, where zero means the runtime
    /// default stands.
    /// </summary>
    /// <remarks>
    /// A negative or non finite authored value is read as unauthored rather than clamped, because
    /// clamping would silently invent a budget the stage never asked for. The scale is mandatory
    /// rather than defaulted: every budget this reads is authored in a stage unit or in degrees,
    /// and a forgotten conversion does not fail - it produces a clamp that is off by the stage
    /// scale and therefore never engages.
    /// </remarks>
    private static float ReadOptionalBudget(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        double scale)
    {
        if (ReadAuthoredScalar(page, item, key, out double value) != AuthoredValue.Usable)
        {
            return 0.0F;
        }
        if (!double.IsFinite(value) || value <= 0.0 ||
            !TryToFloat(value * scale, out float result) || result <= 0.0F)
        {
            return 0.0F;
        }
        return result;
    }

    /// <summary>Composes the mass of one actor, or zero when its density has to decide it.</summary>
    private static float ComposeMass(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        ImmutableArray<string>.Builder notes)
    {
        AuthoredValue state = ReadAuthoredScalar(
            page, item, UsdPhysicsExtractionKey.MassMass, out double authored);
        if (state == AuthoredValue.Missing || (state == AuthoredValue.Usable && authored == 0.0))
        {
            // Zero is what USD authors when the density and the shapes decide the mass.
            return 0.0F;
        }
        if (state == AuthoredValue.Usable && authored > 0.0 &&
            TryToFloat(authored * page.KilogramsPerUnit, out float mass) && mass > 0.0F)
        {
            return mass;
        }

        notes.Add(
            $"{item.Path} authors a mass that cannot be simulated, so its density decides it.");
        return 0.0F;
    }

    /// <summary>Composes the frame that the diagonal inertia of one actor is stated in.</summary>
    /// <remarks>
    /// The principal axes name the frame that diagonal inertia is stated in. That frame is body
    /// local, so for the same reason as the center of mass it keeps its authored orientation: the
    /// up axis change is already carried by the world pose of the actor.
    /// </remarks>
    private static PhysxQuatf ComposePrincipalAxes(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        ImmutableArray<string>.Builder notes)
    {
        AuthoredValue state = ReadAuthoredQuaternion(
            page, item, UsdPhysicsExtractionKey.MassPrincipalAxes, out var authored);
        if (state == AuthoredValue.Missing)
        {
            return PhysxQuatf.Identity;
        }

        if (state == AuthoredValue.Usable && Length(authored) > 0.0)
        {
            (double W, double X, double Y, double Z) unit = Normalize(authored);
            var rotation = new PhysxQuatf(
                (float)unit.X, (float)unit.Y, (float)unit.Z, (float)unit.W);
            if (rotation.IsUsableRotation)
            {
                return rotation;
            }
        }

        notes.Add(
            $"{item.Path} authors principal axes that cannot be simulated, so the mass frame stays the identity.");
        return PhysxQuatf.Identity;
    }

    /// <summary>Composes one damping value of an actor, or the default when it is not usable.</summary>
    private static float ComposeDamping(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        float fallback,
        string label,
        ImmutableArray<string>.Builder notes)
    {
        AuthoredValue state = ReadAuthoredScalar(page, item, key, out double authored);
        if (state == AuthoredValue.Missing)
        {
            return fallback;
        }
        if (state == AuthoredValue.Usable && authored >= 0.0 &&
            TryToFloat(authored, out float value))
        {
            return value;
        }

        notes.Add(
            $"{item.Path} authors a {label} that cannot be simulated, so the default is used.");
        return fallback;
    }

    /// <summary>Accepts one converted actor vector, or replaces the whole vector with zero.</summary>
    /// <remarks>
    /// The three components of a velocity, of a center of mass or of an inertia tensor are one
    /// authored physical value, so one unusable component replaces the whole vector rather than
    /// only itself. Keeping the usable components would state a value that nothing authored.
    /// </remarks>
    private static PhysxVec3f SanitizeVector(
        AuthoredValue state,
        (double X, double Y, double Z) converted,
        bool allowNegative,
        string note,
        ImmutableArray<string>.Builder notes)
    {
        if (state == AuthoredValue.Missing)
        {
            return default;
        }
        if (state == AuthoredValue.Usable &&
            TryToVector(converted, out PhysxVec3f value) &&
            (allowNegative || (value.X >= 0.0F && value.Y >= 0.0F && value.Z >= 0.0F)))
        {
            return value;
        }

        notes.Add(note);
        return default;
    }

    private static uint ActorFlags(UsdPhysicsExtractionPage page, UsdPhysicsExtractionObject item)
    {
        uint flags = 0;
        if (ReadFlag(page, item, UsdPhysicsExtractionKey.BodyDisableGravity))
        {
            flags |= (uint)PhysxActorFlags.DisableGravity;
        }
        if (ReadFlag(page, item, UsdPhysicsExtractionKey.BodyEnableCcd))
        {
            flags |= (uint)PhysxActorFlags.EnableCcd;
        }
        if (ReadFlag(page, item, UsdPhysicsExtractionKey.BodyStartsAsleep))
        {
            flags |= (uint)PhysxActorFlags.StartAsleep;
        }
        return flags;
    }

    private static int ComposeJoints(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        int[] actorIndices,
        HashSet<int> articulationLinks,
        ImmutableArray<string>.Builder skipped)
    {
        int count = 0;
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind != UsdPhysicsExtractionObjectKind.Joint || !item.IsEnabled)
            {
                continue;
            }

            int body0 = ResolveBodyObject(page, item, UsdPhysicsExtractionKey.Body0Targets);
            int body1 = ResolveBodyObject(page, item, UsdPhysicsExtractionKey.Body1Targets);
            if ((body0 >= 0 && articulationLinks.Contains(body0)) ||
                (body1 >= 0 && articulationLinks.Contains(body1)))
            {
                continue;
            }

            int actor0 = ResolveActor(
                page, item, UsdPhysicsExtractionKey.Body0Targets, actorIndices);
            int actor1 = ResolveActor(
                page, item, UsdPhysicsExtractionKey.Body1Targets, actorIndices);
            if ((actor0 < 0 && actor1 < 0) || (actor0 >= 0 && actor0 == actor1))
            {
                skipped.Add($"{item.Path} does not join two distinct simulated bodies.");
                continue;
            }

            PhysxJointType type = JointType(item.TypeName);
            uint axis = ReadAxis(page, item);
            JointAxes axes = SelectAxes(page, item, type, axis, skipped);
            JointLimits limits = ComposeLimits(page, item, type, axes, skipped);
            JointDrive drive = ComposeDrive(page, item, axes);

            var joint = new PhysxJointDesc
            {
                Id = builder.DefineIdentity(item.Path),
                Type = (uint)type,
                Flags = JointFlags(page, item, limits.Enabled, drive, axes),
                Actor0Index = actor0,
                Actor1Index = actor1,
                LocalFrame0 = ReadFrame(
                    page,
                    item,
                    UsdPhysicsExtractionKey.JointLocalPosition0,
                    UsdPhysicsExtractionKey.JointLocalRotation0),
                LocalFrame1 = ReadFrame(
                    page,
                    item,
                    UsdPhysicsExtractionKey.JointLocalPosition1,
                    UsdPhysicsExtractionKey.JointLocalRotation1),
                Axis = axis,
                LowerLimit = ToLimit(limits.Lower),
                UpperLimit = ToLimit(limits.Upper),
                MinDistance = ToLimit(limits.MinDistance),
                MaxDistance = ToLimit(limits.MaxDistance),
                ConeAngle0 = ToLimit(limits.ConeAngle0),
                ConeAngle1 = ToLimit(limits.ConeAngle1),
                DriveStiffness = ToLimit(drive.Stiffness),
                DriveDamping = ToLimit(drive.Damping),
                DriveMaxForce = ToLimit(drive.MaxForce),
                DriveTargetPosition = ToLimit(drive.TargetPosition),
                DriveTargetVelocity = ToLimit(drive.TargetVelocity),
                BreakForce = ToLimit(NonNegative(
                    page, item, UsdPhysicsExtractionKey.JointBreakForce, 0.0) * ForceScale(page)),
                BreakTorque = ToLimit(NonNegative(
                    page, item, UsdPhysicsExtractionKey.JointBreakTorque, 0.0) * TorqueScale(page)),
                Reserved0 = 0,
                Reserved1 = 0,
            };

            if (joint.MinDistance > joint.MaxDistance)
            {
                joint.MaxDistance = joint.MinDistance;
            }

            builder.AddJoint(in joint);
            count++;
        }

        return count;
    }

    /// <summary>Names the multiple apply instance a joint drive and limit were read from.</summary>
    private readonly record struct JointAxes(string Instance, bool IsAngular);

    /// <summary>Carries one drive already converted into simulation units.</summary>
    private readonly record struct JointDrive(
        bool IsAuthored,
        double Stiffness,
        double Damping,
        double MaxForce,
        double TargetPosition,
        double TargetVelocity);

    /// <summary>
    /// Chooses which authored drive and limit instance a joint is composed from.
    /// </summary>
    /// <remarks>
    /// A joint may carry several multiple apply instances while the build page describes one
    /// drive, so the instance is selected by the joint type and the authored axis first and by
    /// the ordered fallback list afterwards. Every instance that is left out is reported.
    /// </remarks>
    private static JointAxes SelectAxes(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        PhysxJointType type,
        uint axis,
        ImmutableArray<string>.Builder skipped)
    {
        List<string> authored = CollectInstances(page, item);
        if (authored.Count == 0)
        {
            return new JointAxes(string.Empty, IsAngularJoint(type));
        }

        string selected = string.Empty;
        foreach (string candidate in PreferredInstances(type, axis))
        {
            if (authored.Contains(candidate, StringComparer.Ordinal))
            {
                selected = candidate;
                break;
            }
        }
        if (selected.Length == 0)
        {
            selected = authored[0];
        }
        if (authored.Count > 1)
        {
            skipped.Add(
                $"{item.Path} authors several joint axes; only {selected} was composed.");
        }
        return new JointAxes(selected, IsAngularInstance(selected, type));
    }

    private static List<string> CollectInstances(
        UsdPhysicsExtractionPage page, UsdPhysicsExtractionObject item)
    {
        var instances = new List<string>();
        for (int offset = 0; offset < item.PropertyCount; offset++)
        {
            UsdPhysicsExtractionProperty property = page.GetProperty(item.PropertyStart + offset);
            if (!IsInstanced(property.Key))
            {
                continue;
            }
            string instance = InstanceName(property.Name);
            if (instance.Length > 0 && !instances.Contains(instance, StringComparer.Ordinal))
            {
                instances.Add(instance);
            }
        }
        instances.Sort(StringComparer.Ordinal);
        return instances;
    }

    private static bool IsInstanced(UsdPhysicsExtractionKey key) =>
        key is UsdPhysicsExtractionKey.DriveStiffness
            or UsdPhysicsExtractionKey.DriveDamping
            or UsdPhysicsExtractionKey.DriveMaxForce
            or UsdPhysicsExtractionKey.DriveTargetPosition
            or UsdPhysicsExtractionKey.DriveTargetVelocity
            or UsdPhysicsExtractionKey.DriveType
            or UsdPhysicsExtractionKey.LimitLow
            or UsdPhysicsExtractionKey.LimitHigh;

    /// <summary>Reads the instance out of a <c>drive:name:physics:leaf</c> style name.</summary>
    private static string InstanceName(string name)
    {
        int start = name.IndexOf(':', StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }
        int end = name.IndexOf(':', start + 1);
        return end < 0 ? string.Empty : name[(start + 1)..end];
    }

    private static string[] PreferredInstances(PhysxJointType type, uint axis)
    {
        string linear = axis switch { 1 => "transY", 2 => "transZ", _ => "transX" };
        string angular = axis switch { 1 => "rotY", 2 => "rotZ", _ => "rotX" };
        return type switch
        {
            PhysxJointType.Revolute => [angular, "angular", "rotX", "rotY", "rotZ"],
            PhysxJointType.Prismatic => [linear, "linear", "transX", "transY", "transZ"],
            PhysxJointType.Spherical => ["angular", angular, "rotX", "rotY", "rotZ"],
            PhysxJointType.Distance => ["distance", "linear", linear],
            _ => [linear, angular, "linear", "angular", "transX", "transY", "transZ", "rotX",
                "rotY", "rotZ", "distance"],
        };
    }

    private static bool IsAngularJoint(PhysxJointType type) =>
        type is PhysxJointType.Revolute or PhysxJointType.Spherical;

    private static bool IsAngularInstance(string instance, PhysxJointType type)
    {
        if (instance.StartsWith("rot", StringComparison.Ordinal) ||
            string.Equals(instance, "angular", StringComparison.Ordinal))
        {
            return true;
        }
        if (instance.StartsWith("trans", StringComparison.Ordinal) ||
            string.Equals(instance, "linear", StringComparison.Ordinal) ||
            string.Equals(instance, "distance", StringComparison.Ordinal))
        {
            return false;
        }
        return IsAngularJoint(type);
    }

    /// <summary>Carries the resolved joint bounds already converted into simulation units.</summary>
    private readonly record struct JointLimits(
        bool Enabled,
        double Lower,
        double Upper,
        double ConeAngle0,
        double ConeAngle1,
        double MinDistance,
        double MaxDistance);

    /// <summary>
    /// Resolves what the authored limit properties of one joint mean for the retained page.
    /// </summary>
    /// <remarks>
    /// An unauthored bound is an infinity in USD and not a zero, so a joint that states no limit
    /// stays free instead of being welded shut, and a joint that states a range the retained page
    /// cannot carry is reported rather than silently turned into a different joint.
    /// </remarks>
    private static JointLimits ComposeLimits(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        PhysxJointType type,
        JointAxes axes,
        ImmutableArray<string>.Builder skipped) =>
        type switch
        {
            PhysxJointType.Spherical => ComposeConeLimits(page, item, skipped),
            PhysxJointType.Distance => ComposeDistanceLimits(page, item),
            PhysxJointType.Fixed => default,
            _ => ComposeRangeLimits(page, item, type, axes, skipped),
        };

    /// <summary>
    /// Resolves the single axis range of a revolute, a prismatic, or an unnamed joint.
    /// </summary>
    /// <remarks>
    /// A revolute or prismatic joint states its range directly; every other joint states it
    /// through the multiple apply limit instance the drive was selected from. A missing bound
    /// keeps its USD default of an infinity, which leaves that side free. Only a range that is
    /// finite on both sides and ordered strictly upwards is a limit the solver accepts, so a one
    /// sided range and the USD way of locking an axis by authoring a lower bound that is not
    /// below the upper bound are both reported instead of enforced.
    /// </remarks>
    private static JointLimits ComposeRangeLimits(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        PhysxJointType type,
        JointAxes axes,
        ImmutableArray<string>.Builder skipped)
    {
        bool hasLower = TryFind(
            page, item, UsdPhysicsExtractionKey.JointLowerLimit, out
            UsdPhysicsExtractionProperty lowerProperty);
        bool hasUpper = TryFind(
            page, item, UsdPhysicsExtractionKey.JointUpperLimit, out
            UsdPhysicsExtractionProperty upperProperty);
        bool angular;
        if (hasLower || hasUpper)
        {
            angular = IsAngularJoint(type);
        }
        else
        {
            hasLower = TryFindInstanced(
                page, item, UsdPhysicsExtractionKey.LimitLow, axes, out lowerProperty);
            hasUpper = TryFindInstanced(
                page, item, UsdPhysicsExtractionKey.LimitHigh, axes, out upperProperty);
            angular = axes.IsAngular;
        }

        double lower = Bound(hasLower, lowerProperty, double.NegativeInfinity);
        double upper = Bound(hasUpper, upperProperty, double.PositiveInfinity);
        if (double.IsNegativeInfinity(lower) && double.IsPositiveInfinity(upper))
        {
            return default;
        }
        if (double.IsInfinity(lower) || double.IsInfinity(upper))
        {
            skipped.Add(
                $"{item.Path} limits one side of an axis only, which the build page cannot " +
                "carry; the axis stays free.");
            return default;
        }
        if (lower >= upper)
        {
            skipped.Add(
                $"{item.Path} locks an axis through its limit range, which the build page " +
                "cannot carry; the axis stays free.");
            return default;
        }

        double scale = angular ? DegreesToRadians : page.MetersPerUnit;
        return new JointLimits(true, lower * scale, upper * scale, 0.0, 0.0, 0.0, 0.0);
    }

    /// <summary>
    /// Resolves the swing cone of a spherical joint out of its authored cone angles.
    /// </summary>
    /// <remarks>
    /// USD states an unlimited swing as a negative or unauthored cone angle while the solver
    /// wants two angles that are above zero and below half a turn, so an unlimited side is
    /// carried as the widest cone the solver accepts and a cone that is authored shut is
    /// reported rather than handed over as a limit the solver would reject.
    /// </remarks>
    private static JointLimits ComposeConeLimits(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        ImmutableArray<string>.Builder skipped)
    {
        double cone0 = ConeAngle(page, item, UsdPhysicsExtractionKey.JointConeAngle0);
        double cone1 = ConeAngle(page, item, UsdPhysicsExtractionKey.JointConeAngle1);
        if (double.IsPositiveInfinity(cone0) && double.IsPositiveInfinity(cone1))
        {
            return default;
        }
        if (cone0 <= 0.0 || cone1 <= 0.0)
        {
            skipped.Add(
                $"{item.Path} closes its swing cone, which the build page cannot carry; the " +
                "swing stays free.");
            return default;
        }
        return new JointLimits(
            true, 0.0, 0.0, Math.Min(cone0, MaxConeAngle), Math.Min(cone1, MaxConeAngle), 0.0,
            0.0);
    }

    /// <summary>
    /// Resolves the reach of a distance joint out of its authored minimum and maximum.
    /// </summary>
    /// <remarks>
    /// USD states an unlimited side as a negative or unauthored value. The solver switches the
    /// minimum off once it is not above zero and switches the maximum off once it reaches the
    /// largest float, so both defaults map straight onto that.
    /// </remarks>
    private static JointLimits ComposeDistanceLimits(
        UsdPhysicsExtractionPage page, UsdPhysicsExtractionObject item)
    {
        double minimum = DistanceBound(page, item, UsdPhysicsExtractionKey.JointMinDistance);
        double maximum = DistanceBound(page, item, UsdPhysicsExtractionKey.JointMaxDistance);
        bool enabled = !double.IsPositiveInfinity(minimum) || !double.IsPositiveInfinity(maximum);
        double scale = page.MetersPerUnit;
        double low = double.IsPositiveInfinity(minimum) ? 0.0 : minimum * scale;
        double high = double.IsPositiveInfinity(maximum) ? float.MaxValue : maximum * scale;
        return new JointLimits(enabled, 0.0, 0.0, 0.0, 0.0, low, Math.Max(low, high));
    }

    /// <summary>Reads one authored bound, where anything unusable falls back to the default.</summary>
    private static double Bound(
        bool found, UsdPhysicsExtractionProperty property, double fallback) =>
        found && !property.IsText && IsUsableProperty(property) &&
        double.IsFinite(property.Scalar)
            ? property.Scalar
            : fallback;

    /// <summary>Reads one cone angle in radians, where an unlimited swing is an infinity.</summary>
    private static double ConeAngle(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key)
    {
        if (!TryFind(page, item, key, out UsdPhysicsExtractionProperty property) ||
            property.IsText ||
            !IsUsableProperty(property) ||
            !double.IsFinite(property.Scalar) ||
            property.Scalar < 0.0)
        {
            return double.PositiveInfinity;
        }
        return property.Scalar * DegreesToRadians;
    }

    /// <summary>Reads one distance bound, where an unlimited reach is an infinity.</summary>
    private static double DistanceBound(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key)
    {
        if (!TryFind(page, item, key, out UsdPhysicsExtractionProperty property) ||
            property.IsText ||
            !IsUsableProperty(property) ||
            !double.IsFinite(property.Scalar) ||
            property.Scalar < 0.0)
        {
            return double.PositiveInfinity;
        }
        return property.Scalar;
    }

    /// <summary>Narrows one bound into the float the page carries without ever overflowing.</summary>
    private static float ToLimit(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0.0F;
        }
        return (float)Math.Clamp(value, -float.MaxValue, float.MaxValue);
    }

    private static JointDrive ComposeDrive(
        UsdPhysicsExtractionPage page, UsdPhysicsExtractionObject item, JointAxes axes)
    {
        bool authored =
            TryFindInstanced(page, item, UsdPhysicsExtractionKey.DriveStiffness, axes, out _) ||
            TryFindInstanced(page, item, UsdPhysicsExtractionKey.DriveDamping, axes, out _) ||
            TryFindInstanced(page, item, UsdPhysicsExtractionKey.DriveMaxForce, axes, out _) ||
            TryFindInstanced(
                page, item, UsdPhysicsExtractionKey.DriveTargetPosition, axes, out _) ||
            TryFindInstanced(page, item, UsdPhysicsExtractionKey.DriveTargetVelocity, axes, out _);
        if (!authored)
        {
            return new JointDrive(false, 0.0, 0.0, 0.0, 0.0, 0.0);
        }

        // An angular drive is stated per degree and produces a torque, a linear drive is stated
        // per stage length unit and produces a force.
        double effort = axes.IsAngular ? TorqueScale(page) : ForceScale(page);
        double perAngle = axes.IsAngular ? TorqueScale(page) / DegreesToRadians : 0.0;
        double stiffnessScale = axes.IsAngular ? perAngle : page.KilogramsPerUnit;
        double positionScale = axes.IsAngular ? DegreesToRadians : page.MetersPerUnit;

        return new JointDrive(
            true,
            NonNegativeInstanced(page, item, UsdPhysicsExtractionKey.DriveStiffness, axes) *
                stiffnessScale,
            NonNegativeInstanced(page, item, UsdPhysicsExtractionKey.DriveDamping, axes) *
                stiffnessScale,
            NonNegativeInstanced(page, item, UsdPhysicsExtractionKey.DriveMaxForce, axes) * effort,
            FiniteInstanced(page, item, UsdPhysicsExtractionKey.DriveTargetPosition, axes) *
                positionScale,
            FiniteInstanced(page, item, UsdPhysicsExtractionKey.DriveTargetVelocity, axes) *
                positionScale);
    }

    private static double ForceScale(UsdPhysicsExtractionPage page) =>
        page.KilogramsPerUnit * page.MetersPerUnit;

    private static double TorqueScale(UsdPhysicsExtractionPage page) =>
        page.KilogramsPerUnit * page.MetersPerUnit * page.MetersPerUnit;

    private static double DensityScale(UsdPhysicsExtractionPage page) =>
        page.KilogramsPerUnit /
        (page.MetersPerUnit * page.MetersPerUnit * page.MetersPerUnit);

    private static uint ReadAxis(UsdPhysicsExtractionPage page, UsdPhysicsExtractionObject item)
    {
        if (!TryFind(page, item, UsdPhysicsExtractionKey.JointAxis, out
            UsdPhysicsExtractionProperty property))
        {
            return 0u;
        }
        if (!property.IsText)
        {
            return (uint)Math.Clamp((int)property.Scalar, 0, 2);
        }
        string value = property.ValueCount > 0 ? page.GetText(property.ValueStart) : string.Empty;
        return value switch
        {
            "Y" or "y" => 1u,
            "Z" or "z" => 2u,
            _ => 0u,
        };
    }

    private static uint JointFlags(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        bool limited,
        JointDrive drive,
        JointAxes axes)
    {
        uint flags = limited ? (uint)PhysxJointFlags.LimitEnabled : 0u;
        if (!ReadFlag(page, item, UsdPhysicsExtractionKey.JointEnabled, fallback: true))
        {
            flags |= (uint)PhysxJointFlags.Disabled;
        }
        if (ReadFlag(page, item, UsdPhysicsExtractionKey.JointCollisionEnabled))
        {
            flags |= (uint)PhysxJointFlags.CollisionEnabled;
        }
        if (drive.IsAuthored && (drive.Stiffness > 0.0 || drive.Damping > 0.0))
        {
            flags |= (uint)PhysxJointFlags.DriveEnabled;

            // An acceleration drive is normalised by the driven mass, so it has to reach the
            // solver as an acceleration; silently treating it as a force drive would make every
            // authored gain wrong by the mass of the body.
            if (IsAccelerationDrive(page, item, axes))
            {
                flags |= (uint)PhysxJointFlags.DriveAcceleration;
            }
        }
        if (ReadFlag(page, item, UsdPhysicsExtractionKey.JointExcludeFromArticulation))
        {
            flags |= (uint)PhysxJointFlags.ExcludeFromArticulation;
        }
        return flags;
    }

    private static int ComposeFilterPairs(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        int[] actorIndices,
        ImmutableArray<string>.Builder skipped)
    {
        var seen = new HashSet<(uint, uint)>();
        int count = 0;
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (!item.IsEnabled || actorIndices[index] < 0)
            {
                continue;
            }

            for (int offset = 0; offset < item.RelationshipCount; offset++)
            {
                UsdPhysicsExtractionRelationship relationship =
                    page.GetRelationship(item.RelationshipStart + offset);
                if (relationship.Key != UsdPhysicsExtractionKey.FilteredPairsTargets)
                {
                    continue;
                }

                for (int slot = 0; slot < relationship.TargetCount; slot++)
                {
                    UsdPhysicsExtractionTarget target =
                        page.GetTarget(relationship.TargetStart + slot);
                    int other = target.ObjectIndex >= 0 ? actorIndices[target.ObjectIndex] : -1;
                    if (other < 0 || other == actorIndices[index])
                    {
                        skipped.Add($"{item.Path} filters {target.Path}, which is not simulated.");
                        continue;
                    }

                    uint left = (uint)Math.Min(actorIndices[index], other);
                    uint right = (uint)Math.Max(actorIndices[index], other);
                    if (!seen.Add((left, right)))
                    {
                        continue;
                    }

                    builder.AddFilterPair(new PhysxFilterPair(left, right));
                    count++;
                }
            }
        }

        return count;
    }

    private static int ComposeArticulations(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        int[] sceneIndices,
        int[] shapeIndices,
        List<ComposedArticulation> composed,
        HashSet<int> refused,
        ImmutableArray<string>.Builder skipped)
    {
        int defaultScene = builder.SceneCount > 0 ? 0 : -1;
        int count = 0;

        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind != UsdPhysicsExtractionObjectKind.ArticulationRoot || !item.IsEnabled)
            {
                continue;
            }

            // A root that overlaps one already composed was refused whole when ownership was
            // resolved, and the note explaining that is already recorded.
            if (refused.Contains(index))
            {
                continue;
            }

            int scene = item.SceneIndex >= 0 && sceneIndices[item.SceneIndex] >= 0
                ? sceneIndices[item.SceneIndex]
                : defaultScene;
            if (scene < 0)
            {
                skipped.Add($"{item.Path} articulation has no simulation scene.");
                continue;
            }

            List<int> linkObjectIndices = ResolveArticulationTargets(page, item);
            if (linkObjectIndices.Count == 0)
            {
                skipped.Add($"{item.Path} articulation has no representable link chain.");
                continue;
            }

            uint flags = 0;
            if (ReadFlag(page, item, UsdPhysicsExtractionKey.ArticulationFixBase) ||
                HasWorldAnchor(page, linkObjectIndices))
            {
                flags |= (uint)PhysxArticulationFlags.FixedBase;
            }
            if (ReadFlag(page, item, UsdPhysicsExtractionKey.ArticulationSelfCollisions))
            {
                flags |= (uint)PhysxArticulationFlags.SelfCollision;
            }

            uint posIter = ReadIterations(
                page, item, UsdPhysicsExtractionKey.ArticulationPositionIterations, 16);
            uint velIter = ReadIterations(
                page, item, UsdPhysicsExtractionKey.ArticulationVelocityIterations, 4);

            uint linkOffset = (uint)builder.ArticulationLinkCount;
            uint actorShapeBase = (uint)builder.ActorShapeCount;
            bool valid = true;
            var linkIds = new ulong[linkObjectIndices.Count];

            // Nothing is appended to the page until the whole chain validates. A rejected
            // articulation would otherwise leave orphan links and orphan actor shape references
            // behind, which misaligns every later window and invalidates the page.
            var stagedLinks = new List<PhysxArticulationLinkDesc>(linkObjectIndices.Count);
            var stagedShapes = new List<PhysxActorShapeRef>();

            for (int linkLocal = 0; linkLocal < linkObjectIndices.Count; linkLocal++)
            {
                int linkObjIndex = linkObjectIndices[linkLocal];
                UsdPhysicsExtractionObject linkItem = page.GetObject(linkObjIndex);
                linkIds[linkLocal] = builder.DefineIdentity(linkItem.Path);

                ulong linkParentId = 0;
                uint jointType = (uint)PhysxArticulationJointType.None;
                UsdPhysicsExtractionObject? inboundItem = null;
                PhysxTransform parentFrame = PhysxTransform.Identity;
                PhysxTransform childFrame = PhysxTransform.Identity;
                if (linkLocal > 0)
                {
                    int inbound = ResolveInboundJoint(page, linkObjectIndices, linkObjIndex, out int parentObject);
                    int parentLocal = parentObject >= 0 ? linkObjectIndices.IndexOf(parentObject) : -1;
                    if (parentLocal < 0)
                    {
                        parentLocal = linkObjectIndices.IndexOf(linkItem.ParentBodyIndex);
                    }
                    if (parentLocal < 0 || parentLocal >= linkLocal)
                    {
                        skipped.Add(
                            $"{item.Path} articulation link {linkItem.Path} has no valid parent in chain.");
                        valid = false;
                        break;
                    }

                    linkParentId = linkIds[parentLocal];
                    if (inbound < 0)
                    {
                        // A link the stage never joined to its parent is welded, which is the
                        // only inbound joint an articulation can express without authored data.
                        jointType = (uint)PhysxArticulationJointType.Fixed;
                    }
                    else
                    {
                        UsdPhysicsExtractionObject jointItem = page.GetObject(inbound);
                        inboundItem = jointItem;
                        jointType = (uint)ArticulationJointType(JointType(jointItem.TypeName));
                        if (jointType == (uint)PhysxArticulationJointType.None)
                        {
                            skipped.Add(
                                $"{item.Path} articulation link {linkItem.Path} is joined by " +
                                $"{jointItem.Path}, whose type a reduced coordinate articulation " +
                                "cannot express; the link is welded to its parent instead.");
                            jointType = (uint)PhysxArticulationJointType.Fixed;
                        }

                        bool reversed = parentObject != ResolveBodyObject(
                            page, jointItem, UsdPhysicsExtractionKey.Body0Targets);
                        PhysxTransform frame0 = ReadFrame(
                            page,
                            jointItem,
                            UsdPhysicsExtractionKey.JointLocalPosition0,
                            UsdPhysicsExtractionKey.JointLocalRotation0);
                        PhysxTransform frame1 = ReadFrame(
                            page,
                            jointItem,
                            UsdPhysicsExtractionKey.JointLocalPosition1,
                            UsdPhysicsExtractionKey.JointLocalRotation1);
                        parentFrame = reversed ? frame1 : frame0;
                        childFrame = reversed ? frame0 : frame1;
                    }
                }

                float mass = ComposeLinkMass(page, linkItem);

                List<int> shapes = CollectBodyShapes(page, shapeIndices, linkObjIndex);
                uint shapeOffset = 0;
                uint shapeCount = 0;
                if (shapes.Count > 0)
                {
                    // A link is a movable body, so the geometry a movable body cannot carry
                    // rejects the whole articulation instead of being dropped from the link.
                    int unusable = shapes.FindIndex(shape => IsStaticOnlyShape(builder, shape));
                    if (unusable >= 0)
                    {
                        skipped.Add(
                            $"{item.Path} articulation link {linkItem.Path} collides with a plane, " +
                            "triangle mesh, or height field, which a movable body cannot use.");
                        valid = false;
                        break;
                    }

                    shapeOffset = actorShapeBase + (uint)stagedShapes.Count;
                    foreach (int shape in shapes)
                    {
                        stagedShapes.Add(new PhysxActorShapeRef((uint)shape, -1));
                    }
                    shapeCount = (uint)shapes.Count;
                }

                var link = new PhysxArticulationLinkDesc
                {
                    Id = linkIds[linkLocal],
                    ParentId = linkParentId,
                    WorldPose = ToTransform(linkItem),
                    ParentFrame = parentFrame,
                    ChildFrame = childFrame,
                    CenterOfMass = ReadMassCenter(page, linkItem, skipped),
                    Inertia = ReadDiagonalInertia(page, linkItem, skipped),
                    PrincipalAxes = ComposePrincipalAxes(page, linkItem, skipped),
                    Mass = mass,
                    LinearDamping = ComposeDamping(
                        page, linkItem, UsdPhysicsExtractionKey.BodyLinearDamping, 0.0F,
                        "linear damping", skipped),
                    AngularDamping = ComposeDamping(
                        page, linkItem, UsdPhysicsExtractionKey.BodyAngularDamping, 0.05F,
                        "angular damping", skipped),
                    MaxLinearVelocity = ReadOptionalBudget(
                        page,
                        linkItem,
                        UsdPhysicsExtractionKey.BodyMaxLinearVelocity,
                        page.MetersPerUnit),
                    MaxAngularVelocity = ReadOptionalBudget(
                        page,
                        linkItem,
                        UsdPhysicsExtractionKey.BodyMaxAngularVelocity,
                        DegreesToRadians),
                    JointType = jointType,
                    Flags = ReadFlag(page, linkItem, UsdPhysicsExtractionKey.BodyDisableGravity)
                        ? (uint)PhysxArticulationLinkFlags.DisableGravity
                        : 0u,
                    ShapeOffset = shapeOffset,
                    ShapeCount = shapeCount,
                    CollisionGroup = 0,
                };
                ComposeArticulationJointAxes(page, inboundItem, jointType, ref link, skipped);
                stagedLinks.Add(link);
            }

            if (!valid)
            {
                continue;
            }

            foreach (PhysxActorShapeRef reference in stagedShapes)
            {
                builder.AddActorShape(reference);
            }
            foreach (PhysxArticulationLinkDesc staged in stagedLinks)
            {
                builder.AddArticulationLink(in staged);
            }

            var desc = new PhysxArticulationDesc
            {
                Id = builder.DefineIdentity(item.Path),
                SceneIndex = scene,
                Flags = flags,
                LinkOffset = linkOffset,
                LinkCount = (uint)linkObjectIndices.Count,
                PositionIterations = posIter,
                VelocityIterations = velIter,
                SleepThreshold = 0.005f,
                StabilizationThreshold = 0.001f,
            };
            builder.AddArticulation(in desc);
            composed.Add(new ComposedArticulation(
                index, (uint)(builder.ArticulationCount - 1), linkObjectIndices));
            count++;
        }

        return count;
    }

    private static int ComposeControllers(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        int[] sceneIndices,
        ImmutableArray<string>.Builder skipped)
    {
        int defaultScene = builder.SceneCount > 0 ? 0 : -1;
        int count = 0;
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind != UsdPhysicsExtractionObjectKind.CharacterController || !item.IsEnabled)
            {
                continue;
            }
            if (!ReadFlag(page, item, UsdPhysicsExtractionKey.ControllerEnabled, fallback: true))
            {
                continue;
            }

            int scene = item.SceneIndex >= 0 && sceneIndices[item.SceneIndex] >= 0
                ? sceneIndices[item.SceneIndex]
                : defaultScene;
            if (scene < 0)
            {
                skipped.Add($"{item.Path} character controller has no simulation scene.");
                continue;
            }

            string shape = ReadToken(page, item, UsdPhysicsExtractionKey.ControllerShapeType, "capsule");
            bool box = string.Equals(shape, "box", StringComparison.Ordinal);
            if (!box && !string.Equals(shape, "capsule", StringComparison.Ordinal))
            {
                skipped.Add(
                    $"{item.Path} character controller declares the unsupported shape '{shape}'.");
                continue;
            }

            double lengthScale = page.MetersPerUnit;
            float radius = ToFloat(
                NonNegative(page, item, UsdPhysicsExtractionKey.ControllerRadius, 0.5) * lengthScale);
            float height = ToFloat(
                NonNegative(page, item, UsdPhysicsExtractionKey.ControllerHeight, 1.0) * lengthScale);
            (double halfX, double halfY, double halfZ) = ReadVector(
                page, item, UsdPhysicsExtractionKey.ControllerHalfExtents, (0.5, 1.0, 0.5));
            var halfExtents = new PhysxVec3f(
                ToFloat(Math.Abs(halfX) * lengthScale),
                ToFloat(Math.Abs(halfY) * lengthScale),
                ToFloat(Math.Abs(halfZ) * lengthScale));
            if (box
                ? !(halfExtents.X > 0.0F) || !(halfExtents.Y > 0.0F) || !(halfExtents.Z > 0.0F)
                : !(radius > 0.0F))
            {
                skipped.Add($"{item.Path} character controller declares no positive extent.");
                continue;
            }

            (double upX, double upY, double upZ) = ReadVector(
                page, item, UsdPhysicsExtractionKey.ControllerUpAxis, (0.0, 1.0, 0.0));
            var up = new PhysxVec3f(ToFloat(upX), ToFloat(upY), ToFloat(upZ));

            // PhysX rejects a slope limit at or beyond a right angle, and the authored value is
            // an angle in degrees, so it is converted here and clamped just under the limit.
            double slopeDegrees = NonNegative(
                page, item, UsdPhysicsExtractionKey.ControllerSlopeLimit, 45.0);
            float slopeLimit = ToFloat(
                Math.Clamp(slopeDegrees * Math.PI / 180.0, 0.0, 1.5707));

            string nonWalkable = ReadToken(
                page, item, UsdPhysicsExtractionKey.ControllerNonWalkableMode, "preventClimbing");
            string climbing = ReadToken(
                page, item, UsdPhysicsExtractionKey.ControllerClimbingMode, "easy");

            var controller = new PhysxControllerDesc
            {
                Id = builder.DefineIdentity(item.Path + ".controller"),
                SceneIndex = scene,
                Shape = (uint)(box ? PhysxControllerShape.Box : PhysxControllerShape.Capsule),
                Position = ToTransform(item).Position,
                UpDirection = up.IsFinite && !up.IsZero ? up : new PhysxVec3f(0.0F, 1.0F, 0.0F),
                Radius = radius,
                Height = height,
                HalfExtents = halfExtents,
                SlopeLimit = slopeLimit,
                StepOffset = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.ControllerStepOffset, 0.5) *
                    lengthScale),
                ContactOffset = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.ControllerContactOffset, 0.02) *
                    lengthScale),
                Density = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.ControllerDensity, 10.0)),
                ScaleCoefficient = ToFloat(
                    Math.Clamp(
                        NonNegative(page, item, UsdPhysicsExtractionKey.ControllerScaleCoefficient, 0.8),
                        0.0,
                        1.0)),
                VolumeGrowth = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.ControllerVolumeGrowth, 1.5)),
                Flags = (uint)PhysxControllerFlags.ApplyGravity,
                NonWalkableMode = (uint)(
                    string.Equals(nonWalkable, "preventClimbingAndForceSliding", StringComparison.Ordinal)
                        ? PhysxControllerNonWalkableMode.PreventClimbingAndForceSliding
                        : PhysxControllerNonWalkableMode.PreventClimbing),
                ClimbingMode = (uint)(
                    string.Equals(climbing, "constrained", StringComparison.Ordinal)
                        ? PhysxControllerClimbingMode.Constrained
                        : PhysxControllerClimbingMode.Easy),
                MaterialIndex = -1,
                CollisionGroup = 0,
                Reserved0 = 0,
                Reserved1 = 0
            };

            builder.AddController(in controller);
            count++;
        }

        return count;
    }

    private sealed record ComposedArticulation(
        int ObjectIndex,
        uint ArticulationIndex,
        List<int> LinkObjectIndices);

    private static int ComposeTendons(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        List<ComposedArticulation> articulations,
        ImmutableArray<string>.Builder skipped)
    {
        int count = 0;
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            bool fixedTendon = item.Kind == UsdPhysicsExtractionObjectKind.FixedTendon;
            if ((!fixedTendon && item.Kind != UsdPhysicsExtractionObjectKind.SpatialTendon) ||
                !item.IsEnabled)
            {
                continue;
            }
            if (!ReadFlag(page, item, UsdPhysicsExtractionKey.TendonEnabled, fallback: true))
            {
                continue;
            }

            int nodeOffset = builder.TendonNodeCount;
            ComposedArticulation? owner = fixedTendon
                ? ComposeFixedTendonNodes(page, builder, articulations, item, index, skipped)
                : ComposeSpatialTendonNodes(page, builder, articulations, item, index, skipped);
            if (owner is null)
            {
                TrimTendonNodes(builder, nodeOffset);
                continue;
            }

            int nodeCount = builder.TendonNodeCount - nodeOffset;
            if (nodeCount <= 0)
            {
                skipped.Add($"{item.Path} tendon resolves no articulation node.");
                continue;
            }

            double lowLimit = Finite(
                page, item, UsdPhysicsExtractionKey.TendonLowerLimit, double.NegativeInfinity);
            double highLimit = Finite(
                page, item, UsdPhysicsExtractionKey.TendonUpperLimit, double.PositiveInfinity);
            bool limited = double.IsFinite(lowLimit) && double.IsFinite(highLimit) &&
                lowLimit <= highLimit;
            double lengthScale = page.MetersPerUnit;

            var tendon = new PhysxTendonDesc
            {
                Id = builder.DefineIdentity(item.Path + ".tendon"),
                ArticulationIndex = owner.ArticulationIndex,
                Type = (uint)(fixedTendon ? PhysxTendonType.Fixed : PhysxTendonType.Spatial),
                NodeOffset = (uint)nodeOffset,
                NodeCount = (uint)nodeCount,
                Flags = limited ? (uint)PhysxTendonFlags.LimitEnabled : 0u,
                Stiffness = ToFloat(NonNegative(page, item, UsdPhysicsExtractionKey.TendonStiffness, 0.0)),
                Damping = ToFloat(NonNegative(page, item, UsdPhysicsExtractionKey.TendonDamping, 0.0)),
                LimitStiffness =
                    ToFloat(NonNegative(page, item, UsdPhysicsExtractionKey.TendonLimitStiffness, 0.0)),
                Offset = ToFloat(Finite(page, item, UsdPhysicsExtractionKey.TendonOffset, 0.0)),
                RestLength = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.TendonRestLength, 0.0) * lengthScale),
                LowLimit = limited ? ToFloat(lowLimit * lengthScale) : 0.0F,
                HighLimit = limited ? ToFloat(highLimit * lengthScale) : 0.0F
            };

            builder.AddTendon(in tendon);
            count++;
        }

        return count;
    }

    private static ComposedArticulation? ComposeFixedTendonNodes(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        List<ComposedArticulation> articulations,
        UsdPhysicsExtractionObject item,
        int tendonObjectIndex,
        ImmutableArray<string>.Builder skipped)
    {
        var joints = new List<int>();
        int rootJoint = ResolveBodyObject(
            page, item, UsdPhysicsExtractionKey.TendonRootJointTargets);
        if (rootJoint >= 0)
        {
            joints.Add(rootJoint);
        }
        CollectTargets(page, item, UsdPhysicsExtractionKey.TendonJointsTargets, joints);
        if (joints.Count == 0)
        {
            skipped.Add($"{item.Path} fixed tendon names no articulation joint.");
            return null;
        }

        ComposedArticulation? owner = FindArticulationForJoint(page, articulations, joints[0]);
        if (owner is null)
        {
            skipped.Add(
                $"{item.Path} fixed tendon does not resolve an articulation composed from this page.");
            return null;
        }

        double[] gearings = ReadNumbers(page, item, UsdPhysicsExtractionKey.TendonGearings);
        double[] forces = ReadNumbers(page, item, UsdPhysicsExtractionKey.TendonForceCoefficients);

        var linkForJoint = new List<int>(joints.Count);
        foreach (int jointObject in joints)
        {
            int link = ResolveJointLink(page, owner, jointObject);
            if (link <= 0)
            {
                skipped.Add(
                    $"{item.Path} fixed tendon names {page.GetObject(jointObject).Path}, " +
                    "which drives no non root link of its articulation.");
                return null;
            }
            linkForJoint.Add(link);
        }

        // PhysX ignores the axis and the coefficient of the tendon joint that has no parent, so
        // the first authored joint can never be that node. The tendon therefore starts at the
        // parent link of its root joint and every authored joint becomes a child node, which is
        // the only shape in which the authored gearing of the root joint reaches the solver.
        int rootLink = ResolveParentLink(page, owner, linkForJoint[0]);
        if (rootLink < 0)
        {
            skipped.Add(
                $"{item.Path} fixed tendon starts at {page.GetObject(joints[0]).Path}, whose " +
                "parent link is not part of the same articulation.");
            return null;
        }

        var rootNode = new PhysxTendonNodeDesc
        {
            Id = builder.DefineIdentity(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{item.Path}.tendonRoot{tendonObjectIndex}")),
            ParentIndex = 0u,
            LinkIndex = (uint)rootLink,
            Axis = ArticulationTendonAxis(page, page.GetObject(joints[0])),
            Flags = 0u,
            Coefficient = 1.0F,
            RecipCoefficient = 1.0F,
            RelativeOffset = default,
            RestLength = 0.0F,
            LowLimit = 0.0F,
            HighLimit = 0.0F
        };
        builder.AddTendonNode(in rootNode);

        for (int slot = 0; slot < joints.Count; slot++)
        {
            UsdPhysicsExtractionObject jointItem = page.GetObject(joints[slot]);
            int parentNode = 1;
            int parentLink = ResolveParentLink(page, owner, linkForJoint[slot]);
            for (int earlier = 0; earlier < slot; earlier++)
            {
                if (linkForJoint[earlier] == parentLink)
                {
                    parentNode = earlier + 2;
                    break;
                }
            }

            var node = new PhysxTendonNodeDesc
            {
                Id = builder.DefineIdentity(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{jointItem.Path}.tendonJoint{tendonObjectIndex}")),
                ParentIndex = (uint)parentNode,
                LinkIndex = (uint)linkForJoint[slot],
                Axis = ArticulationTendonAxis(page, jointItem),
                Flags = 0u,
                Coefficient = ToFloat(slot < gearings.Length ? Usable(gearings[slot], 1.0) : 1.0),
                RecipCoefficient = ToFloat(slot < forces.Length ? Usable(forces[slot], 1.0) : 1.0),
                RelativeOffset = default,
                RestLength = 0.0F,
                LowLimit = 0.0F,
                HighLimit = 0.0F
            };
            builder.AddTendonNode(in node);
        }

        return owner;
    }

    private static ComposedArticulation? ComposeSpatialTendonNodes(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        List<ComposedArticulation> articulations,
        UsdPhysicsExtractionObject item,
        int tendonObjectIndex,
        ImmutableArray<string>.Builder skipped)
    {
        int root = ResolveBodyObject(
            page, item, UsdPhysicsExtractionKey.TendonRootAttachmentTargets);
        if (root < 0)
        {
            skipped.Add($"{item.Path} spatial tendon names no root attachment.");
            return null;
        }

        var ordered = new List<int> { root };
        for (int cursor = 0; cursor < ordered.Count; cursor++)
        {
            int parent = ordered[cursor];
            for (int candidate = 0; candidate < page.ObjectCount; candidate++)
            {
                UsdPhysicsExtractionObject child = page.GetObject(candidate);
                if (child.Kind != UsdPhysicsExtractionObjectKind.TendonAttachment ||
                    !child.IsEnabled || ordered.Contains(candidate))
                {
                    continue;
                }
                if (ResolveBodyObject(
                        page, child, UsdPhysicsExtractionKey.AttachmentParentTargets) == parent)
                {
                    ordered.Add(candidate);
                }
            }
        }

        ComposedArticulation? owner = FindArticulationForLinkPath(
            page, articulations, page.GetObject(root).Path);
        if (owner is null)
        {
            skipped.Add(
                $"{item.Path} spatial tendon does not resolve an articulation composed from this page.");
            return null;
        }

        double lengthScale = page.MetersPerUnit;
        for (int slot = 0; slot < ordered.Count; slot++)
        {
            UsdPhysicsExtractionObject attachment = page.GetObject(ordered[slot]);
            int link = FindLinkLocalIndex(page, owner, attachment.Path);
            if (link < 0)
            {
                skipped.Add(
                    $"{item.Path} spatial tendon attaches to {attachment.Path}, " +
                    "which is no link of its articulation.");
                return null;
            }

            int parentNode = 0;
            if (slot > 0)
            {
                int parentObject = ResolveBodyObject(
                    page, attachment, UsdPhysicsExtractionKey.AttachmentParentTargets);
                int parentSlot = ordered.IndexOf(parentObject);
                parentNode = parentSlot >= 0 && parentSlot < slot ? parentSlot + 1 : slot;
            }

            bool leaf = string.Equals(
                ReadToken(page, attachment, UsdPhysicsExtractionKey.AttachmentRole, "intermediate"),
                "leaf",
                StringComparison.Ordinal);
            double lowLimit = leaf
                ? Finite(page, attachment, UsdPhysicsExtractionKey.AttachmentLowerLimit, double.NegativeInfinity)
                : double.NegativeInfinity;
            double highLimit = leaf
                ? Finite(page, attachment, UsdPhysicsExtractionKey.AttachmentUpperLimit, double.PositiveInfinity)
                : double.PositiveInfinity;
            bool limited = leaf && double.IsFinite(lowLimit) && double.IsFinite(highLimit) &&
                lowLimit <= highLimit;

            (double x, double y, double z) = ReadVector(
                page, attachment, UsdPhysicsExtractionKey.AttachmentLocalPosition, (0.0, 0.0, 0.0));
            var offset = new PhysxVec3f(
                ToFloat(x * lengthScale), ToFloat(y * lengthScale), ToFloat(z * lengthScale));

            var node = new PhysxTendonNodeDesc
            {
                Id = builder.DefineIdentity(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{attachment.Path}.tendonAttachment{tendonObjectIndex}")),
                ParentIndex = (uint)parentNode,
                LinkIndex = (uint)link,
                Axis = 0u,
                Flags = limited ? (uint)PhysxTendonFlags.LimitEnabled : 0u,
                Coefficient = ToFloat(
                    Finite(page, attachment, UsdPhysicsExtractionKey.AttachmentGearing, 1.0)),
                RecipCoefficient = 0.0F,
                RelativeOffset = offset.IsFinite ? offset : default,
                RestLength = leaf
                    ? ToFloat(
                        NonNegative(page, attachment, UsdPhysicsExtractionKey.AttachmentRestLength, 0.0) *
                        lengthScale)
                    : 0.0F,
                LowLimit = limited ? ToFloat(lowLimit * lengthScale) : 0.0F,
                HighLimit = limited ? ToFloat(highLimit * lengthScale) : 0.0F
            };
            builder.AddTendonNode(in node);
        }

        return owner;
    }

    private static int ComposeMimicJoints(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        List<ComposedArticulation> articulations,
        ImmutableArray<string>.Builder skipped)
    {
        int count = 0;
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind != UsdPhysicsExtractionObjectKind.MimicJoint || !item.IsEnabled)
            {
                continue;
            }
            if (!ReadFlag(page, item, UsdPhysicsExtractionKey.MimicEnabled, fallback: true))
            {
                continue;
            }

            int reference = ResolveBodyObject(
                page, item, UsdPhysicsExtractionKey.MimicReferenceJointTargets);
            if (reference < 0)
            {
                skipped.Add($"{item.Path} mimic joint names no reference joint.");
                continue;
            }

            int driven = FindJointObject(page, item.Path);
            if (driven < 0)
            {
                skipped.Add($"{item.Path} mimic joint is not applied to a composed joint.");
                continue;
            }

            ComposedArticulation? owner = FindArticulationForJoint(page, articulations, driven);
            if (owner is null)
            {
                skipped.Add(
                    $"{item.Path} mimic joint does not resolve an articulation composed from this page.");
                continue;
            }

            int linkA = ResolveJointLink(page, owner, driven);
            int linkB = ResolveJointLink(page, owner, reference);
            if (linkA <= 0 || linkB <= 0)
            {
                skipped.Add(
                    $"{item.Path} mimic joint must couple two non root links of one articulation.");
                continue;
            }

            uint axisA = MimicAxis(page, item, UsdPhysicsExtractionKey.MimicAxis, page.GetObject(driven));
            uint axisB = MimicAxis(
                page, item, UsdPhysicsExtractionKey.MimicReferenceAxis, page.GetObject(reference));
            if (linkA == linkB && axisA == axisB)
            {
                skipped.Add($"{item.Path} mimic joint must couple two different joint axes.");
                continue;
            }

            double gearing = Finite(page, item, UsdPhysicsExtractionKey.MimicGearing, 1.0);
            if (gearing == 0.0)
            {
                skipped.Add($"{item.Path} mimic joint declares a zero gearing.");
                continue;
            }

            var mimic = new PhysxMimicJointDesc
            {
                Id = builder.DefineIdentity(item.Path + ".mimicJoint"),
                ArticulationIndex = owner.ArticulationIndex,
                LinkA = (uint)linkA,
                AxisA = axisA,
                LinkB = (uint)linkB,
                AxisB = axisB,
                GearRatio = ToFloat(gearing),
                Offset = ToFloat(Finite(page, item, UsdPhysicsExtractionKey.MimicOffset, 0.0)),
                NaturalFrequency = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.MimicNaturalFrequency, 0.0)),
                DampingRatio = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.MimicDampingRatio, 0.0))
            };

            builder.AddMimicJoint(in mimic);
            count++;
        }

        return count;
    }

    private static int ComposeVehicles(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        int[] sceneIndices,
        int[] actorIndices,
        ImmutableArray<string>.Builder skipped)
    {
        int defaultScene = builder.SceneCount > 0 ? 0 : -1;
        int count = 0;

        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind != UsdPhysicsExtractionObjectKind.Vehicle || !item.IsEnabled)
            {
                continue;
            }
            if (!ReadFlag(page, item, UsdPhysicsExtractionKey.VehicleEnabled, fallback: true))
            {
                continue;
            }

            string drive = ReadToken(page, item, UsdPhysicsExtractionKey.VehicleDriveType, "engine");
            if (string.Equals(drive, "none", StringComparison.Ordinal))
            {
                skipped.Add(
                    $"{item.Path} vehicle declares no drivetrain; the runtime simulates driven vehicles only.");
                continue;
            }
            if (string.Equals(drive, "direct", StringComparison.Ordinal))
            {
                skipped.Add(
                    $"{item.Path} vehicle requests the direct drivetrain, which this build does not " +
                    "implement; compose an engine drivetrain instead.");
                continue;
            }

            int chassisObject = FindActorObject(page, item.Path);
            int chassis = chassisObject >= 0 ? actorIndices[chassisObject] : -1;
            if (chassis < 0)
            {
                skipped.Add($"{item.Path} vehicle has no composed dynamic chassis body on its prim.");
                continue;
            }

            int scene = item.SceneIndex >= 0 && sceneIndices[item.SceneIndex] >= 0
                ? sceneIndices[item.SceneIndex]
                : defaultScene;
            if (scene < 0)
            {
                skipped.Add($"{item.Path} vehicle has no simulation scene.");
                continue;
            }

            List<int> wheelObjects = CollectVehicleWheels(page, item);
            if (wheelObjects.Count == 0)
            {
                skipped.Add($"{item.Path} vehicle carries no wheel attachment.");
                continue;
            }
            if (wheelObjects.Count > PhysxAbi.MaxVehicleWheels)
            {
                skipped.Add(
                    $"{item.Path} vehicle carries {wheelObjects.Count} wheels, more than the " +
                    $"{PhysxAbi.MaxVehicleWheels} this ABI supports.");
                continue;
            }

            uint longitudinal = VehicleAxis(
                page, item, UsdPhysicsExtractionKey.VehicleLongitudinalAxis, PhysxAxis.Z);
            uint lateral = VehicleAxis(
                page, item, UsdPhysicsExtractionKey.VehicleLateralAxis, PhysxAxis.X);
            uint vertical = VehicleAxis(
                page, item, UsdPhysicsExtractionKey.VehicleVerticalAxis, PhysxAxis.Y);
            if (longitudinal == lateral || longitudinal == vertical || lateral == vertical)
            {
                skipped.Add($"{item.Path} vehicle declares two identical coordinate axes.");
                continue;
            }

            (float reverse, float first, float top, uint forwardGears) = ReadGearbox(page, item);
            double torqueScale = TorqueScale(page);

            int wheelOffset = builder.VehicleWheelCount;
            bool ok = ComposeVehicleWheels(page, builder, item, wheelObjects, vertical, skipped);
            if (!ok)
            {
                while (builder.VehicleWheelCount > wheelOffset)
                {
                    builder.RemoveLastVehicleWheel();
                }
                continue;
            }

            double steerDegrees = NonNegative(
                page, item, UsdPhysicsExtractionKey.SteeringMaxSteerAngle, 0.0);
            float steerRadians = ToFloat(
                Math.Clamp(steerDegrees * Math.PI / 180.0, 0.0, Math.PI));

            var vehicle = new PhysxVehicleDesc
            {
                Id = builder.DefineIdentity(item.Path + ".vehicle"),
                SceneIndex = (uint)scene,
                ActorIndex = (uint)chassis,
                WheelOffset = (uint)wheelOffset,
                WheelCount = (uint)wheelObjects.Count,
                Flags = (uint)(PhysxVehicleFlags.AutoboxEnabled | PhysxVehicleFlags.PublishWheels),
                Drive = (uint)PhysxVehicleDrive.Engine,
                Query = string.Equals(
                    ReadToken(page, item, UsdPhysicsExtractionKey.VehicleSuspensionQueryType, "raycast"),
                    "sweep",
                    StringComparison.Ordinal)
                    ? (uint)PhysxVehicleQuery.Sweep
                    : (uint)PhysxVehicleQuery.Raycast,
                LongitudinalAxis = longitudinal,
                LateralAxis = lateral,
                VerticalAxis = vertical,
                ChassisMass = 0.0F,
                ChassisMoi = default,
                EnginePeakTorque = ToFloat(
                    PositiveOr(
                        NonNegative(page, item, UsdPhysicsExtractionKey.EnginePeakTorque, 500.0) *
                        torqueScale,
                        500.0)),
                EngineMoi = ToFloat(
                    PositiveOr(
                        NonNegative(page, item, UsdPhysicsExtractionKey.EngineMomentOfInertia, 1.0) *
                        torqueScale,
                        1.0)),
                EngineIdleOmega = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.EngineIdleRotationSpeed, 0.0)),
                EngineMaxOmega = ToFloat(
                    PositiveOr(
                        NonNegative(page, item, UsdPhysicsExtractionKey.EngineMaxRotationSpeed, 600.0),
                        600.0)),
                EngineDampingFullThrottle = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.EngineDampingFullThrottle, 0.15)),
                EngineDampingZeroThrottleClutchEngaged = ToFloat(
                    NonNegative(
                        page, item, UsdPhysicsExtractionKey.EngineDampingZeroThrottleClutchEngaged, 2.0)),
                EngineDampingZeroThrottleClutchDisengaged = ToFloat(
                    NonNegative(
                        page,
                        item,
                        UsdPhysicsExtractionKey.EngineDampingZeroThrottleClutchDisengaged,
                        0.35)),
                ClutchStrength = ToFloat(
                    PositiveOr(
                        NonNegative(page, item, UsdPhysicsExtractionKey.ClutchStrength, 10.0) * torqueScale,
                        10.0)),
                GearSwitchTime = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.GearsSwitchTime, 0.5)),
                FinalGearRatio = ToFloat(
                    PositiveOr(NonNegative(page, item, UsdPhysicsExtractionKey.GearsRatioScale, 4.0), 4.0)),
                ReverseGearRatio = reverse,
                FirstGearRatio = first,
                TopGearRatio = top,
                ForwardGearCount = forwardGears,
                AutoboxUpRatio = ToFloat(
                    FirstRatio(page, item, UsdPhysicsExtractionKey.AutoGearBoxUpRatios, 0.65)),
                AutoboxDownRatio = ToFloat(
                    FirstRatio(page, item, UsdPhysicsExtractionKey.AutoGearBoxDownRatios, 0.15)),
                AutoboxLatency = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.AutoGearBoxLatency, 2.0)),
                MaxBrakeTorque = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.BrakesMaxBrakeTorque, 0.0) *
                    torqueScale),
                MaxHandBrakeTorque = ToFloat(
                    NonNegative(page, item, UsdPhysicsExtractionKey.BrakesSecondaryMaxBrakeTorque, 0.0) *
                    torqueScale),
                MaxSteerAngle = steerRadians,
                DefaultFriction = 1.0F,
                SprungMassTotal = 0.0F
            };

            builder.AddVehicle(in vehicle);
            count++;
        }

        return count;
    }

    private static bool ComposeVehicleWheels(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        UsdPhysicsExtractionObject vehicleItem,
        List<int> wheelObjects,
        uint verticalAxis,
        ImmutableArray<string>.Builder skipped)
    {
        double lengthScale = page.MetersPerUnit;
        double massScale = page.KilogramsPerUnit;
        double forceScale = ForceScale(page);
        double torqueScale = TorqueScale(page);
        double stiffnessScale = massScale;

        double[] brakeWheels = ReadNumbers(page, vehicleItem, UsdPhysicsExtractionKey.BrakesWheels);
        double[] brakeGains =
            ReadNumbers(page, vehicleItem, UsdPhysicsExtractionKey.BrakesTorqueMultipliers);
        double[] handBrakeWheels =
            ReadNumbers(page, vehicleItem, UsdPhysicsExtractionKey.BrakesSecondaryWheels);
        double[] handBrakeGains =
            ReadNumbers(page, vehicleItem, UsdPhysicsExtractionKey.BrakesSecondaryTorqueMultipliers);
        double[] steerWheels = ReadNumbers(page, vehicleItem, UsdPhysicsExtractionKey.SteeringWheels);
        double[] steerGains =
            ReadNumbers(page, vehicleItem, UsdPhysicsExtractionKey.SteeringAngleMultipliers);
        double[] driveWheels =
            ReadNumbers(page, vehicleItem, UsdPhysicsExtractionKey.DifferentialWheels);
        double[] driveGains =
            ReadNumbers(page, vehicleItem, UsdPhysicsExtractionKey.DifferentialTorqueRatios);

        PhysxVec3f droop = verticalAxis switch
        {
            (uint)PhysxAxis.X => new PhysxVec3f(-1.0F, 0.0F, 0.0F),
            (uint)PhysxAxis.Z => new PhysxVec3f(0.0F, 0.0F, -1.0F),
            _ => new PhysxVec3f(0.0F, -1.0F, 0.0F),
        };

        for (int slot = 0; slot < wheelObjects.Count; slot++)
        {
            UsdPhysicsExtractionObject wheelItem = page.GetObject(wheelObjects[slot]);
            int wheelIndex = (int)Math.Max(
                0.0, ReadScalar(page, wheelItem, UsdPhysicsExtractionKey.WheelAttachmentIndex, slot));

            (double sx, double sy, double sz) = ReadVector(
                page,
                wheelItem,
                UsdPhysicsExtractionKey.WheelAttachmentSuspensionPosition,
                (0.0, 0.0, 0.0));
            (double wx, double wy, double wz) = ReadVector(
                page, wheelItem, UsdPhysicsExtractionKey.WheelAttachmentWheelPosition, (0.0, 0.0, 0.0));
            (double tx, double ty, double tz) = ReadVector(
                page,
                wheelItem,
                UsdPhysicsExtractionKey.WheelAttachmentSuspensionTravelDirection,
                (droop.X, droop.Y, droop.Z));

            var travel = new PhysxVec3f(ToFloat(tx), ToFloat(ty), ToFloat(tz));
            if (!travel.IsFinite || travel.IsZero)
            {
                travel = droop;
            }

            double radius = PositiveOr(
                NonNegative(page, wheelItem, UsdPhysicsExtractionKey.WheelRadius, 0.5) * lengthScale,
                0.5 * lengthScale);
            double width = PositiveOr(
                NonNegative(page, wheelItem, UsdPhysicsExtractionKey.WheelWidth, 0.2) * lengthScale,
                0.2 * lengthScale);
            double travelDistance = PositiveOr(
                NonNegative(page, wheelItem, UsdPhysicsExtractionKey.SuspensionTravelDistance, 0.5) *
                lengthScale,
                0.5 * lengthScale);

            uint flags = 0;
            float brakeResponse = ResponseFor(brakeWheels, brakeGains, wheelIndex);
            float handBrakeResponse = ResponseFor(handBrakeWheels, handBrakeGains, wheelIndex);
            float steerResponse = ResponseFor(steerWheels, steerGains, wheelIndex);
            float driveResponse = ResponseFor(driveWheels, driveGains, wheelIndex);
            if (brakeResponse > 0.0F)
            {
                flags |= (uint)PhysxVehicleWheelFlags.Brakes;
            }
            if (handBrakeResponse > 0.0F)
            {
                flags |= (uint)PhysxVehicleWheelFlags.HandBrakes;
            }
            if (steerResponse > 0.0F)
            {
                flags |= (uint)PhysxVehicleWheelFlags.Steers;
            }
            if (driveResponse > 0.0F)
            {
                flags |= (uint)PhysxVehicleWheelFlags.Driven;
            }

            var wheel = new PhysxVehicleWheelDesc
            {
                Id = builder.DefineIdentity(wheelItem.Path + ".vehicleWheel"),
                SuspensionAttachment = new PhysxTransform(
                    new PhysxVec3f(
                        ToFloat(sx * lengthScale), ToFloat(sy * lengthScale), ToFloat(sz * lengthScale)),
                    PhysxQuatf.Identity),
                SuspensionTravelDir = travel,
                SuspensionTravelDist = ToFloat(travelDistance),
                WheelAttachment = new PhysxTransform(
                    new PhysxVec3f(
                        ToFloat(wx * lengthScale), ToFloat(wy * lengthScale), ToFloat(wz * lengthScale)),
                    PhysxQuatf.Identity),
                Radius = ToFloat(radius),
                HalfWidth = ToFloat(width * 0.5),
                Mass = ToFloat(
                    PositiveOr(
                        NonNegative(page, wheelItem, UsdPhysicsExtractionKey.WheelMass, 20.0) * massScale,
                        20.0 * massScale)),
                Moi = ToFloat(
                    NonNegative(page, wheelItem, UsdPhysicsExtractionKey.WheelMomentOfInertia, 1.0) *
                    torqueScale),
                DampingRate = ToFloat(
                    NonNegative(page, wheelItem, UsdPhysicsExtractionKey.WheelDampingRate, 0.25) *
                    torqueScale),
                SuspensionStiffness = ToFloat(
                    PositiveOr(
                        NonNegative(
                            page, wheelItem, UsdPhysicsExtractionKey.SuspensionSpringStrength, 10000.0) *
                        stiffnessScale,
                        10000.0 * stiffnessScale)),
                SuspensionDamping = ToFloat(
                    NonNegative(
                        page, wheelItem, UsdPhysicsExtractionKey.SuspensionSpringDamperRate, 1000.0) *
                    stiffnessScale),
                SprungMass = ToFloat(
                    NonNegative(page, wheelItem, UsdPhysicsExtractionKey.SuspensionSprungMass, 0.0) *
                    massScale),
                TireLatStiffX = DefaultTireLatStiffX,
                TireLatStiffY = DefaultTireLatStiffY,
                TireLongStiff = ToFloat(
                    NonNegative(
                        page, wheelItem, UsdPhysicsExtractionKey.TireLongitudinalStiffness, 5000.0) *
                    forceScale),
                TireCamberStiff = ToFloat(
                    NonNegative(page, wheelItem, UsdPhysicsExtractionKey.TireCamberStiffness, 0.0) *
                    forceScale),
                TireRestLoad = ToFloat(
                    NonNegative(page, wheelItem, UsdPhysicsExtractionKey.TireRestLoad, 0.0) * forceScale),
                TireFriction = 1.0F,
                SteerResponse = steerResponse,
                BrakeResponse = brakeResponse,
                HandBrakeResponse = handBrakeResponse,
                DriveTorqueRatio = driveResponse,
                AxleIndex = (uint)Math.Min(slot / 2, wheelObjects.Count - 1),
                Flags = flags
            };

            if (!wheel.SuspensionAttachment.IsFinite || !wheel.WheelAttachment.IsFinite)
            {
                skipped.Add($"{wheelItem.Path} wheel declares a non finite attachment frame.");
                return false;
            }

            builder.AddVehicleWheel(in wheel);
        }

        return true;
    }

    private const float DefaultTireLatStiffX = 0.01F;

    private const float DefaultTireLatStiffY = 18.0F;

    private static List<int> CollectVehicleWheels(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject vehicle)
    {
        var wheels = new List<int>();
        string prefix = vehicle.Path + "/";
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind != UsdPhysicsExtractionObjectKind.VehicleWheelAttachment || !item.IsEnabled)
            {
                continue;
            }
            if (item.Path.StartsWith(prefix, StringComparison.Ordinal))
            {
                wheels.Add(index);
            }
        }

        wheels.Sort((left, right) =>
        {
            double leftIndex = ReadScalar(
                page, page.GetObject(left), UsdPhysicsExtractionKey.WheelAttachmentIndex, left);
            double rightIndex = ReadScalar(
                page, page.GetObject(right), UsdPhysicsExtractionKey.WheelAttachmentIndex, right);
            int order = leftIndex.CompareTo(rightIndex);
            return order != 0 ? order : left.CompareTo(right);
        });
        return wheels;
    }

    private static int FindActorObject(UsdPhysicsExtractionPage page, string path)
    {
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind == UsdPhysicsExtractionObjectKind.RigidBody && item.IsEnabled &&
                string.Equals(item.Path, path, StringComparison.Ordinal))
            {
                return index;
            }
        }
        return -1;
    }

    private static uint VehicleAxis(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        PhysxAxis fallback)
    {
        string token = ReadToken(page, item, key, string.Empty);
        return token switch
        {
            "posX" or "negX" => (uint)PhysxAxis.X,
            "posY" or "negY" => (uint)PhysxAxis.Y,
            "posZ" or "negZ" => (uint)PhysxAxis.Z,
            _ => (uint)fallback,
        };
    }

    private static (float Reverse, float First, float Top, uint ForwardGears) ReadGearbox(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item)
    {
        double[] ratios = ReadNumbers(page, item, UsdPhysicsExtractionKey.GearsRatios);
        double reverse = 0.0;
        var forward = new List<double>();
        foreach (double ratio in ratios)
        {
            if (!double.IsFinite(ratio))
            {
                continue;
            }
            if (ratio < 0.0 && reverse == 0.0)
            {
                reverse = -ratio;
            }
            else if (ratio > 0.0)
            {
                forward.Add(ratio);
            }
        }

        if (forward.Count == 0)
        {
            forward.AddRange([4.0, 2.0, 1.5, 1.1, 1.0]);
        }
        if (reverse <= 0.0)
        {
            reverse = 4.0;
        }

        double first = forward[0];
        double top = forward[^1];
        if (top > first)
        {
            (first, top) = (top, first);
        }

        uint gears = (uint)Math.Min(forward.Count, PhysxAbi.MaxVehicleGears - 2);
        return (ToFloat(reverse), ToFloat(first), ToFloat(top), Math.Max(1u, gears));
    }

    private static double FirstRatio(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        double fallback)
    {
        foreach (double value in ReadNumbers(page, item, key))
        {
            if (double.IsFinite(value) && value >= 0.0 && value <= 1.0)
            {
                return value;
            }
        }
        return fallback;
    }

    private static float ResponseFor(double[] wheels, double[] gains, int wheelIndex)
    {
        for (int slot = 0; slot < wheels.Length; slot++)
        {
            if ((int)wheels[slot] != wheelIndex)
            {
                continue;
            }
            double gain = slot < gains.Length ? gains[slot] : 1.0;
            if (!double.IsFinite(gain) || gain <= 0.0)
            {
                gain = 1.0;
            }
            return ToFloat(Math.Clamp(gain, 0.0, 1.0));
        }
        return 0.0F;
    }

    private static double PositiveOr(double value, double fallback) =>
        double.IsFinite(value) && value > 0.0 ? value : fallback;

    private static void TrimTendonNodes(PhysxPageBuilder builder, int nodeOffset)
    {
        while (builder.TendonNodeCount > nodeOffset)
        {
            builder.RemoveLastTendonNode();
        }
    }

    private static void CollectTargets(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        List<int> targets)
    {
        for (int offset = 0; offset < item.RelationshipCount; offset++)
        {
            UsdPhysicsExtractionRelationship relationship =
                page.GetRelationship(item.RelationshipStart + offset);
            if (relationship.Key != key)
            {
                continue;
            }
            for (int slot = 0; slot < relationship.TargetCount; slot++)
            {
                UsdPhysicsExtractionTarget target = page.GetTarget(relationship.TargetStart + slot);
                if (target.ObjectIndex >= 0 && !targets.Contains(target.ObjectIndex))
                {
                    targets.Add(target.ObjectIndex);
                }
            }
        }
    }

    private static ComposedArticulation? FindArticulationForJoint(
        UsdPhysicsExtractionPage page,
        List<ComposedArticulation> articulations,
        int jointObjectIndex)
    {
        foreach (ComposedArticulation articulation in articulations)
        {
            if (ResolveJointLink(page, articulation, jointObjectIndex) > 0)
            {
                return articulation;
            }
        }
        return null;
    }

    private static ComposedArticulation? FindArticulationForLinkPath(
        UsdPhysicsExtractionPage page,
        List<ComposedArticulation> articulations,
        string path)
    {
        foreach (ComposedArticulation articulation in articulations)
        {
            if (FindLinkLocalIndex(page, articulation, path) >= 0)
            {
                return articulation;
            }
        }
        return null;
    }

    private static int FindLinkLocalIndex(
        UsdPhysicsExtractionPage page,
        ComposedArticulation articulation,
        string path)
    {
        for (int local = 0; local < articulation.LinkObjectIndices.Count; local++)
        {
            if (string.Equals(
                    page.GetObject(articulation.LinkObjectIndices[local]).Path,
                    path,
                    StringComparison.Ordinal))
            {
                return local;
            }
        }
        return -1;
    }

    private static int ResolveJointLink(
        UsdPhysicsExtractionPage page,
        ComposedArticulation articulation,
        int jointObjectIndex)
    {
        UsdPhysicsExtractionObject joint = page.GetObject(jointObjectIndex);
        int body1 = ResolveBodyObject(page, joint, UsdPhysicsExtractionKey.Body1Targets);
        int body0 = ResolveBodyObject(page, joint, UsdPhysicsExtractionKey.Body0Targets);
        int local1 = body1 >= 0 ? articulation.LinkObjectIndices.IndexOf(body1) : -1;
        int local0 = body0 >= 0 ? articulation.LinkObjectIndices.IndexOf(body0) : -1;
        if (local1 > 0 && local0 >= 0)
        {
            return local1;
        }
        if (local0 > 0 && local1 >= 0)
        {
            return local0;
        }
        return -1;
    }

    private static int ResolveParentLink(
        UsdPhysicsExtractionPage page,
        ComposedArticulation articulation,
        int linkLocalIndex)
    {
        int linkObject = articulation.LinkObjectIndices[linkLocalIndex];
        int inbound = ResolveInboundJoint(
            page, articulation.LinkObjectIndices, linkObject, out int parentObject);
        if (inbound < 0 || parentObject < 0)
        {
            return 0;
        }
        int parentLocal = articulation.LinkObjectIndices.IndexOf(parentObject);
        return parentLocal < 0 ? 0 : parentLocal;
    }

    private static int FindJointObject(UsdPhysicsExtractionPage page, string path)
    {
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind == UsdPhysicsExtractionObjectKind.Joint && item.IsEnabled &&
                string.Equals(item.Path, path, StringComparison.Ordinal))
            {
                return index;
            }
        }
        return -1;
    }

    private static uint ArticulationTendonAxis(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject joint)
    {
        uint local = ReadAxis(page, joint);
        bool linear = JointType(joint.TypeName) == PhysxJointType.Prismatic;
        uint axis = linear ? local : (uint)PhysxJointAxis.Twist + local;
        return axis < (uint)PhysxJointAxis.Count ? axis : (uint)PhysxJointAxis.Twist;
    }

    private static uint MimicAxis(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        UsdPhysicsExtractionObject joint)
    {
        string token = ReadToken(page, item, key, string.Empty);
        return token switch
        {
            "transX" => (uint)PhysxJointAxis.X,
            "transY" => (uint)PhysxJointAxis.Y,
            "transZ" => (uint)PhysxJointAxis.Z,
            "rotX" => (uint)PhysxJointAxis.Twist,
            "rotY" => (uint)PhysxJointAxis.Swing1,
            "rotZ" => (uint)PhysxJointAxis.Swing2,
            _ => ArticulationTendonAxis(page, joint),
        };
    }

    private static double[] ReadNumbers(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key)
    {
        if (!TryFind(page, item, key, out UsdPhysicsExtractionProperty property) ||
            property.IsText || !IsUsableProperty(property) || property.ValueCount <= 0)
        {
            return [];
        }
        double[] values = new double[property.ValueCount];
        for (int slot = 0; slot < values.Length; slot++)
        {
            values[slot] = page.GetNumber(property.ValueStart + slot);
        }
        return values;
    }

    private static string ReadToken(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        string fallback) =>
        TryFind(page, item, key, out UsdPhysicsExtractionProperty property) && property.IsText &&
        IsUsableProperty(property) && property.ValueCount > 0
            ? page.GetText(property.ValueStart)
            : fallback;

    private static double Usable(double value, double fallback) =>
        double.IsFinite(value) ? value : fallback;

    /// <summary>
    /// Assigns every rigid body to at most one articulation, deterministically.
    /// </summary>
    /// <param name="page">The extracted stage.</param>
    /// <param name="rejected">Receives the root object indices that were refused.</param>
    /// <param name="skipped">Receives one note per refused root.</param>
    /// <returns>The bodies an accepted root claims, keyed by body object index.</returns>
    /// <remarks>
    /// <para>
    /// Nested or overlapping articulation roots are ill formed but authorable: a root on a prim and
    /// another on one of its descendants both resolve to overlapping body sets. Composing both
    /// stages the same body twice under the same composed identity, which gives the world two links
    /// with one address. The link map then keeps whichever arrived first and the world publishes two
    /// poses for one prim, so a command reaches one link while the viewer draws the other.
    /// </para>
    /// <para>
    /// The rule is: the first root in extraction order that claims a body owns it, and any later
    /// root that overlaps an already claimed body is refused as a whole. Extraction order is stage
    /// traversal order, so for nested roots that is the outermost one, which is the root a reader of
    /// the stage would name. Refusing the inner root whole rather than trimming it is what keeps the
    /// remaining chain meaningful - a partially claimed articulation would be a different mechanism
    /// from the one the scene authored.
    /// </para>
    /// <para>
    /// A refused root leaves no orphans. Its bodies are simply not treated as links, so the ones it
    /// shared are simulated as links of the accepted root and the ones it did not share fall back to
    /// ordinary rigid actors joined by ordinary joints.
    /// </para>
    /// </remarks>
    private static Dictionary<int, int> ResolveArticulationOwnership(
        UsdPhysicsExtractionPage page,
        out HashSet<int> rejected,
        ImmutableArray<string>.Builder skipped)
    {
        var owner = new Dictionary<int, int>();
        rejected = [];
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind != UsdPhysicsExtractionObjectKind.ArticulationRoot || !item.IsEnabled)
            {
                continue;
            }

            List<int> targets = ResolveArticulationTargets(page, item);
            int conflict = -1;
            for (int slot = 0; slot < targets.Count; slot++)
            {
                if (owner.ContainsKey(targets[slot]))
                {
                    conflict = targets[slot];
                    break;
                }
            }

            if (conflict >= 0)
            {
                _ = rejected.Add(index);
                skipped.Add(
                    $"{item.Path} articulation shares {page.GetObject(conflict).Path} with " +
                    $"{page.GetObject(owner[conflict]).Path}, so the outer articulation owns the " +
                    "body and this one is not composed.");
                continue;
            }

            for (int slot = 0; slot < targets.Count; slot++)
            {
                owner[targets[slot]] = index;
            }
        }

        return owner;
    }

    private static HashSet<int> CollectArticulationLinks(UsdPhysicsExtractionPage page)
    {
        var links = new HashSet<int>();
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind != UsdPhysicsExtractionObjectKind.ArticulationRoot || !item.IsEnabled)
            {
                continue;
            }
            foreach (int target in ResolveArticulationTargets(page, item))
            {
                links.Add(target);
            }
        }
        return links;
    }

    private static PhysxArticulationJointType ArticulationJointType(PhysxJointType type) => type switch
    {
        PhysxJointType.Fixed => PhysxArticulationJointType.Fixed,
        PhysxJointType.Revolute => PhysxArticulationJointType.Revolute,
        PhysxJointType.Prismatic => PhysxArticulationJointType.Prismatic,
        PhysxJointType.Spherical => PhysxArticulationJointType.Spherical,
        _ => PhysxArticulationJointType.None,
    };

    private static int ResolveBodyObject(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key)
    {
        for (int offset = 0; offset < item.RelationshipCount; offset++)
        {
            UsdPhysicsExtractionRelationship relationship =
                page.GetRelationship(item.RelationshipStart + offset);
            if (relationship.Key != key)
            {
                continue;
            }
            for (int slot = 0; slot < relationship.TargetCount; slot++)
            {
                UsdPhysicsExtractionTarget target =
                    page.GetTarget(relationship.TargetStart + slot);
                if (target.ObjectIndex >= 0)
                {
                    return target.ObjectIndex;
                }
            }
        }
        return -1;
    }

    private static int ResolveInboundJoint(
        UsdPhysicsExtractionPage page,
        List<int> linkObjectIndices,
        int linkObjectIndex,
        out int parentObjectIndex)
    {
        parentObjectIndex = -1;
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Kind != UsdPhysicsExtractionObjectKind.Joint || !item.IsEnabled)
            {
                continue;
            }

            int body0 = ResolveBodyObject(page, item, UsdPhysicsExtractionKey.Body0Targets);
            int body1 = ResolveBodyObject(page, item, UsdPhysicsExtractionKey.Body1Targets);
            int other;
            if (body1 == linkObjectIndex)
            {
                other = body0;
            }
            else if (body0 == linkObjectIndex)
            {
                other = body1;
            }
            else
            {
                continue;
            }

            if (other >= 0 && linkObjectIndices.Contains(other))
            {
                parentObjectIndex = other;
                return index;
            }
        }

        return -1;
    }

    /// <summary>Resolves the ordered link chain one articulation root owns.</summary>
    /// <remarks>
    /// A stage may name the links directly, but the schema convention is to apply the
    /// articulation root to an ancestor and let the rigid bodies underneath it, joined to one
    /// another, form the chain. Both spellings are resolved here so that the composer and the
    /// loose actor exclusion always agree on which bodies an articulation claims. The result is
    /// ordered breadth first from the root link, which is the order a reduced coordinate
    /// articulation requires because every link must be added after its parent.
    /// </remarks>
    private static List<int> ResolveArticulationTargets(
        UsdPhysicsExtractionPage page, UsdPhysicsExtractionObject item)
    {
        var targets = new List<int>();
        for (int offset = 0; offset < item.RelationshipCount; offset++)
        {
            UsdPhysicsExtractionRelationship relationship =
                page.GetRelationship(item.RelationshipStart + offset);
            if (relationship.Key != UsdPhysicsExtractionKey.ArticulationTargets)
            {
                continue;
            }
            for (int slot = 0; slot < relationship.TargetCount; slot++)
            {
                UsdPhysicsExtractionTarget target =
                    page.GetTarget(relationship.TargetStart + slot);
                if (target.ObjectIndex >= 0 &&
                    page.GetObject(target.ObjectIndex).Kind == UsdPhysicsExtractionObjectKind.RigidBody &&
                    page.GetObject(target.ObjectIndex).IsEnabled &&
                    !targets.Contains(target.ObjectIndex))
                {
                    targets.Add(target.ObjectIndex);
                }
            }
        }

        if (targets.Count == 0)
        {
            targets = CollectSubtreeBodies(page, item.Path);
        }

        return OrderArticulationChain(page, targets);
    }

    /// <summary>Collects every enabled rigid body at or under one prim path.</summary>
    private static List<int> CollectSubtreeBodies(UsdPhysicsExtractionPage page, string path)
    {
        var bodies = new List<int>();
        string prefix = path.EndsWith('/') ? path : path + "/";
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject candidate = page.GetObject(index);
            if (candidate.Kind != UsdPhysicsExtractionObjectKind.RigidBody || !candidate.IsEnabled)
            {
                continue;
            }
            if (string.Equals(candidate.Path, path, StringComparison.Ordinal) ||
                candidate.Path.StartsWith(prefix, StringComparison.Ordinal))
            {
                bodies.Add(index);
            }
        }
        return bodies;
    }

    /// <summary>Orders candidate links so that every link follows its parent.</summary>
    private static List<int> OrderArticulationChain(UsdPhysicsExtractionPage page, List<int> candidates)
    {
        if (candidates.Count <= 1)
        {
            return candidates;
        }

        var members = new HashSet<int>(candidates);
        var children = new HashSet<int>();
        int anchored = -1;
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject joint = page.GetObject(index);
            if (joint.Kind != UsdPhysicsExtractionObjectKind.Joint || !joint.IsEnabled)
            {
                continue;
            }

            int body0 = ResolveBodyObject(page, joint, UsdPhysicsExtractionKey.Body0Targets);
            int body1 = ResolveBodyObject(page, joint, UsdPhysicsExtractionKey.Body1Targets);
            bool inside0 = body0 >= 0 && members.Contains(body0);
            bool inside1 = body1 >= 0 && members.Contains(body1);
            if (inside0 && inside1)
            {
                children.Add(body1);
            }
            else if (anchored < 0 && inside1 && body0 < 0)
            {
                anchored = body1;
            }
            else if (anchored < 0 && inside0 && body1 < 0)
            {
                anchored = body0;
            }
        }

        int root = anchored;
        if (root < 0)
        {
            foreach (int candidate in candidates)
            {
                if (!children.Contains(candidate))
                {
                    root = candidate;
                    break;
                }
            }
        }
        root = root < 0 ? candidates[0] : root;

        var ordered = new List<int>(candidates.Count) { root };
        var visited = new HashSet<int> { root };
        for (int cursor = 0; cursor < ordered.Count; cursor++)
        {
            int parent = ordered[cursor];
            for (int index = 0; index < page.ObjectCount; index++)
            {
                UsdPhysicsExtractionObject joint = page.GetObject(index);
                if (joint.Kind != UsdPhysicsExtractionObjectKind.Joint || !joint.IsEnabled)
                {
                    continue;
                }

                int body0 = ResolveBodyObject(page, joint, UsdPhysicsExtractionKey.Body0Targets);
                int body1 = ResolveBodyObject(page, joint, UsdPhysicsExtractionKey.Body1Targets);
                int child = body0 == parent ? body1 : body1 == parent ? body0 : -1;
                if (child < 0 || !members.Contains(child) || !visited.Add(child))
                {
                    continue;
                }
                ordered.Add(child);
            }
        }

        return ordered;
    }

    /// <summary>Tells whether one of the links is joined to the world, which pins the base.</summary>
    private static bool HasWorldAnchor(UsdPhysicsExtractionPage page, List<int> links)
    {
        var members = new HashSet<int>(links);
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject joint = page.GetObject(index);
            if (joint.Kind != UsdPhysicsExtractionObjectKind.Joint || !joint.IsEnabled)
            {
                continue;
            }

            int body0 = ResolveBodyObject(page, joint, UsdPhysicsExtractionKey.Body0Targets);
            int body1 = ResolveBodyObject(page, joint, UsdPhysicsExtractionKey.Body1Targets);
            bool inside0 = body0 >= 0 && members.Contains(body0);
            bool inside1 = body1 >= 0 && members.Contains(body1);
            if (inside0 != inside1 && (body0 < 0 || body1 < 0))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Composes the mass of one articulation link in simulation units.</summary>
    /// <remarks>
    /// A link mass is authored in stage mass units exactly like a rigid body mass, so an authored
    /// value converts the same way. The inertia of the same link already converts at
    /// <see cref="ReadDiagonalInertia"/>, and a link whose mass skipped the conversion while its
    /// inertia did not is internally inconsistent rather than merely mis-scaled. The fallback is
    /// one kilogram of simulation mass, not one stage unit, because it stands in for a mass the
    /// stage never authored.
    /// </remarks>
    private static float ComposeLinkMass(
        UsdPhysicsExtractionPage page, UsdPhysicsExtractionObject item)
    {
        if (ReadAuthoredScalar(page, item, UsdPhysicsExtractionKey.MassMass, out double authored) !=
            AuthoredValue.Usable)
        {
            return 1.0F;
        }
        double value = authored * page.KilogramsPerUnit;
        return double.IsFinite(value) && value > 0.0 && TryToFloat(value, out float mass) &&
            mass > 0.0F
            ? mass
            : 1.0F;
    }

    /// <summary>Reads the authored center of mass of one body in simulation units.</summary>
    private static PhysxVec3f ReadMassCenter(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        ImmutableArray<string>.Builder notes)
    {
        AuthoredValue state = ReadAuthoredVector(
            page, item, UsdPhysicsExtractionKey.MassCenterOfMass, out var authored);
        return SanitizeVector(
            state,
            Scale(authored, page.MetersPerUnit),
            allowNegative: true,
            $"{item.Path} authors a center of mass that cannot be simulated, so the body origin is used.",
            notes);
    }

    /// <summary>Reads the authored diagonal inertia of one body in simulation units.</summary>
    private static PhysxVec3f ReadDiagonalInertia(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        ImmutableArray<string>.Builder notes)
    {
        AuthoredValue state = ReadAuthoredVector(
            page, item, UsdPhysicsExtractionKey.MassDiagonalInertia, out var authored);
        return SanitizeVector(
            state,
            Scale(authored, page.KilogramsPerUnit * page.MetersPerUnit * page.MetersPerUnit),
            allowNegative: false,
            $"{item.Path} authors a diagonal inertia that cannot be simulated, so the collision shapes decide it.",
            notes);
    }

    /// <summary>
    /// Fills the per axis motion, limit, and drive state of one articulation inbound joint.
    /// </summary>
    /// <remarks>
    /// A reduced coordinate joint states its degrees of freedom as six axes rather than as a
    /// joint type with one authored axis, so the authored axis selects which of the six the
    /// joint type unlocks. The joint frames reach PhysX exactly as they were authored, which
    /// means a rotation about the stage Z axis is the swing2 degree of freedom and never a
    /// rotated twist. Everything the joint does not unlock stays locked, so an unauthored
    /// articulation is a rigid tree instead of a free floating one.
    /// </remarks>
    private static void ComposeArticulationJointAxes(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject? joint,
        uint jointType,
        ref PhysxArticulationLinkDesc link,
        ImmutableArray<string>.Builder skipped)
    {
        if (joint is not UsdPhysicsExtractionObject jointItem ||
            jointType is (uint)PhysxArticulationJointType.None or (uint)PhysxArticulationJointType.Fixed)
        {
            return;
        }

        PhysxJointType usdType = JointType(jointItem.TypeName);
        uint authoredAxis = ReadAxis(page, jointItem);
        JointAxes axes = SelectAxes(page, jointItem, usdType, authoredAxis, skipped);

        Span<int> unlocked = stackalloc int[3];
        int unlockedCount = 0;
        switch (jointType)
        {
            case (uint)PhysxArticulationJointType.Prismatic:
                unlocked[unlockedCount++] = (int)Math.Min(authoredAxis, 2u);
                break;
            case (uint)PhysxArticulationJointType.Revolute:
                unlocked[unlockedCount++] = (int)PhysxJointAxis.Twist + (int)Math.Min(authoredAxis, 2u);
                break;
            default:
                unlocked[unlockedCount++] = (int)PhysxJointAxis.Twist;
                unlocked[unlockedCount++] = (int)PhysxJointAxis.Swing1;
                unlocked[unlockedCount++] = (int)PhysxJointAxis.Swing2;
                break;
        }

        JointLimits limits = ComposeLimits(page, jointItem, usdType, axes, skipped);
        JointDrive drive = ComposeDrive(page, jointItem, axes);
        bool acceleration = IsAccelerationDrive(page, jointItem, axes);
        if (acceleration)
        {
            link.Flags |= (uint)PhysxArticulationLinkFlags.DriveAcceleration;
        }

        for (int slot = 0; slot < unlockedCount; slot++)
        {
            int axis = unlocked[slot];
            bool angular = axis >= (int)PhysxJointAxis.Twist;
            bool limited = limits.Enabled;
            float low;
            float high;
            if (jointType == (uint)PhysxArticulationJointType.Spherical)
            {
                // A cone is symmetric about the joint frame, so each swing carries the authored
                // half angle and the twist stays free unless the stage limited it directly.
                double cone = axis == (int)PhysxJointAxis.Swing1 ? limits.ConeAngle0 : limits.ConeAngle1;
                limited = limits.Enabled && axis != (int)PhysxJointAxis.Twist && cone > 0.0;
                low = ToLimit(-cone);
                high = ToLimit(cone);
            }
            else
            {
                low = ToLimit(limits.Lower);
                high = ToLimit(limits.Upper);
            }

            link.Motion[axis] = limited
                ? (uint)PhysxJointMotion.Limited
                : (uint)PhysxJointMotion.Free;
            link.LowerLimit[axis] = limited ? low : 0.0F;
            link.UpperLimit[axis] = limited ? high : 0.0F;

            if (!drive.IsAuthored || (drive.Stiffness <= 0.0 && drive.Damping <= 0.0 &&
                Math.Abs(drive.TargetVelocity) <= 0.0))
            {
                continue;
            }

            link.DriveStiffness[axis] = ToLimit(drive.Stiffness);
            link.DriveDamping[axis] = ToLimit(drive.Damping);
            link.DriveMaxForce[axis] = ToLimit(drive.MaxForce);
            link.DriveTargetPosition[axis] = ToLimit(drive.TargetPosition);
            link.DriveTargetVelocity[axis] = ToLimit(drive.TargetVelocity);
            link.DriveFlags[axis] = (uint)PhysxJointDriveFlags.Enabled |
                (acceleration ? (uint)PhysxJointDriveFlags.Acceleration : 0u);
            _ = angular;
        }
    }

    /// <summary>States whether the authored drive of one joint axis is read as an acceleration.</summary>
    private static bool IsAccelerationDrive(
        UsdPhysicsExtractionPage page, UsdPhysicsExtractionObject item, JointAxes axes)
    {
        if (!TryFindInstanced(
            page, item, UsdPhysicsExtractionKey.DriveType, axes, out UsdPhysicsExtractionProperty property) ||
            !property.IsText ||
            property.ValueCount == 0)
        {
            return false;
        }
        return string.Equals(page.GetText(property.ValueStart), "acceleration", StringComparison.Ordinal);
    }

    private static List<int> CollectBodyShapes(
        UsdPhysicsExtractionPage page, int[] shapeIndices, int bodyIndex)
    {
        var shapes = new List<int>();
        for (int index = 0; index < page.ObjectCount; index++)
        {
            if (shapeIndices[index] < 0)
            {
                continue;
            }
            if (page.GetObject(index).ParentBodyIndex == bodyIndex)
            {
                shapes.Add(shapeIndices[index]);
            }
        }
        return shapes;
    }

    private static int ResolveDefaultScene(
        UsdPhysicsExtractionPage page,
        PhysxPageBuilder builder,
        int[] sceneIndices,
        int[] shapeIndices)
    {
        if (page.DefaultSceneIndex >= 0 && sceneIndices[page.DefaultSceneIndex] >= 0)
        {
            return sceneIndices[page.DefaultSceneIndex];
        }

        int authored = Array.FindIndex(sceneIndices, static value => value >= 0);
        if (authored >= 0)
        {
            return sceneIndices[authored];
        }

        if (!shapeIndices.Any(static value => value >= 0))
        {
            return -1;
        }

        // A stage may author bodies without ever authoring a scene. One implicit scene keeps that
        // stage simulatable instead of dropping every actor on the floor.
        var scene = new PhysxSceneDesc
        {
            Id = builder.DefineIdentity("/__openusd_default_physics_scene"),
            GravityDirection = new PhysxVec3f(0.0F, -1.0F, 0.0F),
            GravityMagnitude = DefaultGravity,
            Flags = 0,
            PositionIterations = 4,
            VelocityIterations = 1,
            BounceThreshold = 0.2F,
            ContactOffset = DefaultContactOffset,
            Reserved0 = 0,
        };
        return builder.AddScene(in scene);
    }

    private static bool IsStaticOnlyShape(PhysxPageBuilder builder, int shapeIndex)
    {
        var type = (PhysxShapeType)builder.ShapeTypeAt(shapeIndex);
        return type is PhysxShapeType.Plane or PhysxShapeType.TriangleMesh or PhysxShapeType.Heightfield;
    }

    private static bool IsMovableIncompatible(
        UsdPhysicsExtractionPage page, int[] shapeIndices, int shapeIndex)
    {
        for (int index = 0; index < page.ObjectCount; index++)
        {
            if (shapeIndices[index] != shapeIndex)
            {
                continue;
            }
            UsdPhysicsExtractionGeometryKind geometry = page.GetObject(index).Geometry;
            return geometry is UsdPhysicsExtractionGeometryKind.Plane
                or UsdPhysicsExtractionGeometryKind.Mesh;
        }
        return false;
    }

    private static uint ActorType(UsdPhysicsExtractionObject item, bool isLooseCollider)
    {
        if (isLooseCollider || (item.Flags & UsdPhysicsExtractionObjectTraits.Static) != 0)
        {
            return (uint)PhysxActorType.Static;
        }
        return (item.Flags & UsdPhysicsExtractionObjectTraits.Kinematic) != 0
            ? (uint)PhysxActorType.Kinematic
            : (uint)PhysxActorType.Dynamic;
    }

    private static PhysxJointType JointType(string typeName) =>
        typeName switch
        {
            not null when typeName.EndsWith("RevoluteJoint", StringComparison.Ordinal) =>
                PhysxJointType.Revolute,
            not null when typeName.EndsWith("PrismaticJoint", StringComparison.Ordinal) =>
                PhysxJointType.Prismatic,
            not null when typeName.EndsWith("SphericalJoint", StringComparison.Ordinal) =>
                PhysxJointType.Spherical,
            not null when typeName.EndsWith("DistanceJoint", StringComparison.Ordinal) =>
                PhysxJointType.Distance,
            not null when typeName.EndsWith("FixedJoint", StringComparison.Ordinal) =>
                PhysxJointType.Fixed,
            _ => PhysxJointType.D6,
        };

    private static bool TryMapGeometry(
        UsdPhysicsExtractionGeometryKind geometry, out PhysxShapeType type)
    {
        switch (geometry)
        {
            case UsdPhysicsExtractionGeometryKind.Sphere:
                type = PhysxShapeType.Sphere;
                return true;
            case UsdPhysicsExtractionGeometryKind.Box:
                type = PhysxShapeType.Box;
                return true;
            case UsdPhysicsExtractionGeometryKind.Capsule:
                type = PhysxShapeType.Capsule;
                return true;
            case UsdPhysicsExtractionGeometryKind.Plane:
                type = PhysxShapeType.Plane;
                return true;
            case UsdPhysicsExtractionGeometryKind.ConvexMesh:
                type = PhysxShapeType.ConvexMesh;
                return true;
            case UsdPhysicsExtractionGeometryKind.Mesh:
                type = PhysxShapeType.TriangleMesh;
                return true;
            default:
                type = PhysxShapeType.Count;
                return false;
        }
    }

    private static bool IsUsable(in PhysxShapeDesc shape, PhysxShapeType type) => type switch
    {
        PhysxShapeType.Sphere => shape.Radius > 0.0F && float.IsFinite(shape.Radius),
        PhysxShapeType.Box =>
            shape.HalfExtents.IsFinite &&
            shape.HalfExtents.X > 0.0F &&
            shape.HalfExtents.Y > 0.0F &&
            shape.HalfExtents.Z > 0.0F,
        PhysxShapeType.Capsule =>
            shape.Radius > 0.0F && shape.HalfHeight > 0.0F &&
            float.IsFinite(shape.Radius) && float.IsFinite(shape.HalfHeight),
        _ => true,
    };

    private static int ResolveMaterial(
        UsdPhysicsExtractionPage page, UsdPhysicsExtractionObject item, int[] materialIndices)
    {
        for (int offset = 0; offset < item.RelationshipCount; offset++)
        {
            UsdPhysicsExtractionRelationship relationship =
                page.GetRelationship(item.RelationshipStart + offset);
            if (relationship.Key != UsdPhysicsExtractionKey.MaterialBindingTargets)
            {
                continue;
            }
            for (int slot = 0; slot < relationship.TargetCount; slot++)
            {
                UsdPhysicsExtractionTarget target =
                    page.GetTarget(relationship.TargetStart + slot);
                if (target.ObjectIndex >= 0 && materialIndices[target.ObjectIndex] >= 0)
                {
                    return materialIndices[target.ObjectIndex];
                }
            }
        }
        return -1;
    }

    private static int ResolveActor(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        int[] actorIndices)
    {
        for (int offset = 0; offset < item.RelationshipCount; offset++)
        {
            UsdPhysicsExtractionRelationship relationship =
                page.GetRelationship(item.RelationshipStart + offset);
            if (relationship.Key != key)
            {
                continue;
            }
            for (int slot = 0; slot < relationship.TargetCount; slot++)
            {
                UsdPhysicsExtractionTarget target =
                    page.GetTarget(relationship.TargetStart + slot);
                if (target.ObjectIndex >= 0 && actorIndices[target.ObjectIndex] >= 0)
                {
                    return actorIndices[target.ObjectIndex];
                }
            }
        }
        return -1;
    }

    private static PhysxTransform ReadFrame(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey positionKey,
        UsdPhysicsExtractionKey rotationKey)
    {
        (double x, double y, double z) = ReadVector(page, item, positionKey, (0.0, 0.0, 0.0));
        double scale = page.MetersPerUnit;
        var position = new PhysxVec3f((float)(x * scale), (float)(y * scale), (float)(z * scale));

        (double w, double rx, double ry, double rz) =
            ReadQuaternion(page, item, rotationKey, (1.0, 0.0, 0.0, 0.0));
        var rotation = new PhysxQuatf((float)rx, (float)ry, (float)rz, (float)w);
        return new PhysxTransform(
            position.IsFinite ? position : default,
            rotation.IsUsableRotation ? rotation : PhysxQuatf.Identity);
    }

    private static PhysxTransform ToTransform(UsdPhysicsExtractionObject item)
    {
        (double x, double y, double z) = item.Position;
        (double w, double rx, double ry, double rz) = item.Rotation;
        var position = new PhysxVec3f((float)x, (float)y, (float)z);
        var rotation = new PhysxQuatf((float)rx, (float)ry, (float)rz, (float)w);
        return new PhysxTransform(
            position.IsFinite ? position : default,
            rotation.IsUsableRotation ? rotation : PhysxQuatf.Identity);
    }

    private static bool TryFind(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        out UsdPhysicsExtractionProperty property)
    {
        for (int offset = 0; offset < item.PropertyCount; offset++)
        {
            UsdPhysicsExtractionProperty candidate = page.GetProperty(item.PropertyStart + offset);
            if (candidate.Key == key)
            {
                property = candidate;
                return true;
            }
        }
        property = default;
        return false;
    }

    /// <summary>Tells whether one authored property carries a value the composer may use.</summary>
    /// <remarks>
    /// The extraction keeps an authored property that it could not represent so that a caller can
    /// report it, and flags it invalid. Every read here treats such a property as unauthored, so a
    /// value that the runtime cannot take never reaches the build page.
    /// </remarks>
    private static bool IsUsableProperty(UsdPhysicsExtractionProperty property) =>
        (property.Flags & UsdPhysicsExtractionPropertyTraits.Invalid) == 0;

    private static double ReadScalar(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        double fallback) =>
        TryFind(page, item, key, out UsdPhysicsExtractionProperty property) && !property.IsText &&
        IsUsableProperty(property)
            ? property.Scalar
            : fallback;

    private static bool ReadFlag(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        bool fallback = false) =>
        TryFind(page, item, key, out UsdPhysicsExtractionProperty property) &&
        IsUsableProperty(property)
            ? property.Scalar != 0.0
            : fallback;

    private static uint ReadIterations(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        uint fallback)
    {
        double value = ReadScalar(page, item, key, fallback);
        return !double.IsFinite(value) || value < 1.0 ? fallback : (uint)Math.Min(value, 255.0);
    }

    private static double NonNegative(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        double fallback)
    {
        double value = ReadScalar(page, item, key, fallback);
        return double.IsFinite(value) && value >= 0.0 ? value : fallback;
    }

    private static double Finite(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        double fallback)
    {
        double value = ReadScalar(page, item, key, fallback);
        return double.IsFinite(value) ? value : fallback;
    }

    private static (double X, double Y, double Z) ReadVector(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        (double X, double Y, double Z) fallback)
    {
        if (!TryFind(page, item, key, out UsdPhysicsExtractionProperty property) ||
            property.IsText ||
            !IsUsableProperty(property) ||
            property.ValueCount < 3)
        {
            return fallback;
        }
        return (
            page.GetNumber(property.ValueStart),
            page.GetNumber(property.ValueStart + 1),
            page.GetNumber(property.ValueStart + 2));
    }

    private static (double W, double X, double Y, double Z) ReadQuaternion(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        (double W, double X, double Y, double Z) fallback)
    {
        if (!TryFind(page, item, key, out UsdPhysicsExtractionProperty property) ||
            property.IsText ||
            !IsUsableProperty(property) ||
            property.ValueCount < 4)
        {
            return fallback;
        }
        return (
            page.GetNumber(property.ValueStart),
            page.GetNumber(property.ValueStart + 1),
            page.GetNumber(property.ValueStart + 2),
            page.GetNumber(property.ValueStart + 3));
    }

    /// <summary>Describes what one authored value turned out to be.</summary>
    private enum AuthoredValue
    {
        /// <summary>Nothing authored the property.</summary>
        Missing,

        /// <summary>The property is authored and its value can be simulated.</summary>
        Usable,

        /// <summary>The property is authored and its value cannot be simulated.</summary>
        Unusable,
    }

    /// <summary>Reads one authored number and reports whether it can be simulated.</summary>
    private static AuthoredValue ReadAuthoredScalar(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        out double value)
    {
        value = 0.0;
        if (!TryFind(page, item, key, out UsdPhysicsExtractionProperty property) || property.IsText)
        {
            return AuthoredValue.Missing;
        }
        if (!IsUsableProperty(property) || !double.IsFinite(property.Scalar))
        {
            return AuthoredValue.Unusable;
        }

        value = property.Scalar;
        return AuthoredValue.Usable;
    }

    /// <summary>Reads one authored vector and reports whether it can be simulated.</summary>
    private static AuthoredValue ReadAuthoredVector(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        out (double X, double Y, double Z) value)
    {
        value = (0.0, 0.0, 0.0);
        if (!TryFind(page, item, key, out UsdPhysicsExtractionProperty property) ||
            property.IsText ||
            property.ValueCount < 3)
        {
            return AuthoredValue.Missing;
        }

        (double X, double Y, double Z) authored = (
            page.GetNumber(property.ValueStart),
            page.GetNumber(property.ValueStart + 1),
            page.GetNumber(property.ValueStart + 2));
        if (!IsUsableProperty(property) ||
            !double.IsFinite(authored.X) ||
            !double.IsFinite(authored.Y) ||
            !double.IsFinite(authored.Z))
        {
            return AuthoredValue.Unusable;
        }

        value = authored;
        return AuthoredValue.Usable;
    }

    /// <summary>Reads one authored rotation and reports whether it can be simulated.</summary>
    private static AuthoredValue ReadAuthoredQuaternion(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        out (double W, double X, double Y, double Z) value)
    {
        value = (1.0, 0.0, 0.0, 0.0);
        if (!TryFind(page, item, key, out UsdPhysicsExtractionProperty property) ||
            property.IsText ||
            property.ValueCount < 4)
        {
            return AuthoredValue.Missing;
        }

        (double W, double X, double Y, double Z) authored = (
            page.GetNumber(property.ValueStart),
            page.GetNumber(property.ValueStart + 1),
            page.GetNumber(property.ValueStart + 2),
            page.GetNumber(property.ValueStart + 3));
        if (!IsUsableProperty(property) ||
            !double.IsFinite(authored.W) ||
            !double.IsFinite(authored.X) ||
            !double.IsFinite(authored.Y) ||
            !double.IsFinite(authored.Z))
        {
            return AuthoredValue.Unusable;
        }

        value = authored;
        return AuthoredValue.Usable;
    }

    /// <summary>Narrows one converted number, and reports whether the result still holds it.</summary>
    private static bool TryToFloat(double value, out float result)
    {
        result = (float)value;
        return float.IsFinite(result);
    }

    /// <summary>Narrows one converted number, and falls back when the float cannot hold it.</summary>
    private static float ToFloat(double value, float fallback) =>
        TryToFloat(value, out float result) ? result : fallback;

    /// <summary>Narrows one converted scalar, reporting zero when the result no longer holds it.</summary>
    private static float ToFloat(double value) => ToFloat(value, 0.0F);

    /// <summary>Narrows one converted vector, and reports whether the result still holds it.</summary>
    private static bool TryToVector((double X, double Y, double Z) value, out PhysxVec3f result)
    {
        result = ToVector(value);
        return result.IsFinite;
    }

    private static int[] CreateMap(int count)
    {
        var map = new int[count];
        Array.Fill(map, -1);
        return map;
    }

    /// <summary>Finds one property that belongs to a named multiple apply instance.</summary>
    private static bool TryFindInstanced(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        JointAxes axes,
        out UsdPhysicsExtractionProperty property)
    {
        for (int offset = 0; offset < item.PropertyCount; offset++)
        {
            UsdPhysicsExtractionProperty candidate = page.GetProperty(item.PropertyStart + offset);
            if (candidate.Key != key)
            {
                continue;
            }
            if (axes.Instance.Length == 0 ||
                string.Equals(InstanceName(candidate.Name), axes.Instance, StringComparison.Ordinal))
            {
                property = candidate;
                return true;
            }
        }
        property = default;
        return false;
    }

    private static double ReadInstanced(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        JointAxes axes,
        double fallback) =>
        TryFindInstanced(page, item, key, axes, out UsdPhysicsExtractionProperty property) &&
        !property.IsText && IsUsableProperty(property)
            ? property.Scalar
            : fallback;

    private static double NonNegativeInstanced(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        JointAxes axes)
    {
        double value = ReadInstanced(page, item, key, axes, 0.0);
        return double.IsFinite(value) && value >= 0.0 ? value : 0.0;
    }

    private static double FiniteInstanced(
        UsdPhysicsExtractionPage page,
        UsdPhysicsExtractionObject item,
        UsdPhysicsExtractionKey key,
        JointAxes axes)
    {
        double value = ReadInstanced(page, item, key, axes, 0.0);
        return double.IsFinite(value) ? value : 0.0;
    }

    private static (double W, double X, double Y, double Z) Multiply(
        (double W, double X, double Y, double Z) left,
        (double W, double X, double Y, double Z) right) => (
        (left.W * right.W) - (left.X * right.X) - (left.Y * right.Y) - (left.Z * right.Z),
        (left.W * right.X) + (left.X * right.W) + (left.Y * right.Z) - (left.Z * right.Y),
        (left.W * right.Y) - (left.X * right.Z) + (left.Y * right.W) + (left.Z * right.X),
        (left.W * right.Z) + (left.X * right.Y) - (left.Y * right.X) + (left.Z * right.W));

    private static (double W, double X, double Y, double Z) Conjugate(
        (double W, double X, double Y, double Z) value) =>
        (value.W, -value.X, -value.Y, -value.Z);

    private static (double W, double X, double Y, double Z) Normalize(
        (double W, double X, double Y, double Z) value)
    {
        double length = Math.Sqrt(
            (value.W * value.W) + (value.X * value.X) +
            (value.Y * value.Y) + (value.Z * value.Z));
        return length <= 0.0
            ? (1.0, 0.0, 0.0, 0.0)
            : (value.W / length, value.X / length, value.Y / length, value.Z / length);
    }

    private static (double X, double Y, double Z) RotateVector(
        (double W, double X, double Y, double Z) rotation, (double X, double Y, double Z) value)
    {
        (double _, double x, double y, double z) = Multiply(
            Multiply(rotation, (0.0, value.X, value.Y, value.Z)), Conjugate(rotation));
        return (x, y, z);
    }

    private static (double X, double Y, double Z) Scale(
        (double X, double Y, double Z) value, double scale) =>
        (value.X * scale, value.Y * scale, value.Z * scale);

    /// <summary>Composes the scale of one collider, or a unit scale when it is not usable.</summary>
    /// <remarks>
    /// The runtime needs a positive finite scale on every axis, and a scale that only becomes
    /// unusable once it is narrowed to a float, by overflowing or by collapsing to zero, is just
    /// as unusable as one that was authored that way. The whole scale is replaced because the
    /// three axes are one authored value.
    /// </remarks>
    private static PhysxVec3f ComposeScale(
        UsdPhysicsExtractionObject item, ImmutableArray<string>.Builder notes)
    {
        (double x, double y, double z) = item.Scale;
        if (TryPositiveScale(x, out float scaleX) &
            TryPositiveScale(y, out float scaleY) &
            TryPositiveScale(z, out float scaleZ))
        {
            return new PhysxVec3f(scaleX, scaleY, scaleZ);
        }

        notes.Add($"{item.Path} authors a scale that cannot be simulated, so a unit scale is used.");
        return new PhysxVec3f(1.0F, 1.0F, 1.0F);
    }

    /// <summary>Narrows one scale axis, mirroring aside, and reports whether it stays positive.</summary>
    private static bool TryPositiveScale(double value, out float result)
    {
        double magnitude = Math.Abs(value);
        result = 1.0F;
        if (!double.IsFinite(magnitude) || magnitude <= 0.0)
        {
            return false;
        }
        var narrowed = (float)magnitude;
        if (!float.IsFinite(narrowed) || narrowed <= 0.0F)
        {
            return false;
        }
        result = narrowed;
        return true;
    }

    /// <summary>Normalizes one direction without letting its own length overflow.</summary>
    /// <remarks>
    /// Squaring the components of a very large direction overflows to infinity, and dividing by
    /// that infinity produces a zero direction while the magnitude stays positive, which the page
    /// validator rejects. Dividing by the largest component first keeps every square inside the
    /// range of a double, so a direction only fails when it truly carries no orientation.
    /// </remarks>
    private static bool TryNormalizeDirection(
        (double X, double Y, double Z) value, out PhysxVec3f result)
    {
        result = default;
        double largest = Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));
        if (!double.IsFinite(largest) || largest <= 0.0)
        {
            return false;
        }

        (double X, double Y, double Z) scaled =
            (value.X / largest, value.Y / largest, value.Z / largest);
        double length = Length(scaled);
        if (!double.IsFinite(length) || length <= 0.0)
        {
            return false;
        }

        if (!TryToVector((scaled.X / length, scaled.Y / length, scaled.Z / length), out result) ||
            result.IsZero)
        {
            result = default;
            return false;
        }
        return true;
    }

    private static PhysxVec3f ToVector((double X, double Y, double Z) value) =>
        new((float)value.X, (float)value.Y, (float)value.Z);

    private static double Length((double X, double Y, double Z) value) =>
        Math.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));

    private static double Length((double W, double X, double Y, double Z) value) =>
        Math.Sqrt(
            (value.W * value.W) + (value.X * value.X) +
            (value.Y * value.Y) + (value.Z * value.Z));
}
