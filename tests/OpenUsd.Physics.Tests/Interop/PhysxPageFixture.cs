// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Interop;

/// <summary>
/// Builds one canonical, fully populated build page that every page test works against.
/// </summary>
internal static class PhysxPageFixture
{
    internal const string ScenePath = "/World/PhysicsScene";
    internal const string RubberPath = "/World/Materials/Rubber";
    internal const string UnicodeMaterialPath = "/World/Materials/Ünïcøde_Gummi";
    internal const string GroundColliderPath = "/World/Ground/Collider";
    internal const string BoxColliderPath = "/World/Böx/Collider";
    internal const string HullColliderPath = "/World/Hull/Collider";
    internal const string GroundPath = "/World/Ground";
    internal const string BoxPath = "/World/Böx";
    internal const string HullPath = "/World/Hull";
    internal const string JointPath = "/World/Joint";

    internal const ulong FixtureRevision = 12;
    internal const ulong FixtureSourceHash = 0xFEEDFACECAFEBEEFUL;

    internal static readonly PhysxVec3f[] HullPoints =
    [
        new(0.0F, 0.0F, 0.0F),
        new(1.0F, 0.0F, 0.0F),
        new(0.0F, 1.0F, 0.0F),
        new(0.0F, 0.0F, 1.0F)
    ];

    internal static readonly uint[] HullIndices =
    [
        0, 1, 2,
        0, 2, 3,
        0, 3, 1,
        1, 3, 2
    ];

    /// <summary>Creates a builder that is already staged with a valid scene.</summary>
    internal static PhysxPageBuilder CreateBuilder()
    {
        var builder = new PhysxPageBuilder
        {
            Revision = FixtureRevision,
            SourceHash = FixtureSourceHash,
            MetersPerUnit = 0.01,
            KilogramsPerUnit = 1.0,
            TimeCodesPerSecond = 24.0,
            StartTimeCode = 0.0,
            EndTimeCode = 100.0,
            UpAxis = PhysxUpAxis.Z,
            SimulationRateHz = 60,
            MaxSubsteps = 4
        };

        try
        {
            Stage(builder);
        }
        catch
        {
            builder.Dispose();
            throw;
        }

        return builder;
    }

    /// <summary>Creates the canonical page.</summary>
    internal static PhysxBuildPage CreatePage()
    {
        using PhysxPageBuilder builder = CreateBuilder();
        return builder.Build();
    }

    /// <summary>Creates a mutable copy of the canonical page bytes.</summary>
    internal static byte[] CreatePageBytes()
    {
        using PhysxBuildPage page = CreatePage();
        return page.Bytes.ToArray();
    }

    /// <summary>Reads the header of a page buffer.</summary>
    internal static PhysxBuildPageHeader ReadHeader(byte[] page) =>
        MemoryMarshal.Read<PhysxBuildPageHeader>(page);

    private static void Stage(PhysxPageBuilder builder)
    {
        builder.AddScene(new PhysxSceneDesc
        {
            Id = builder.DefineIdentity(ScenePath),
            GravityDirection = new PhysxVec3f(0.0F, 0.0F, -1.0F),
            GravityMagnitude = 981.0F,
            Flags = (uint)PhysxSceneFlags.EnableCcd,
            PositionIterations = 4,
            VelocityIterations = 1,
            BounceThreshold = 2.0F,
            ContactOffset = 0.02F
        });

        builder.AddMaterial(new PhysxMaterialDesc
        {
            Id = builder.DefineIdentity(RubberPath),
            StaticFriction = 0.6F,
            DynamicFriction = 0.5F,
            Restitution = 0.4F,
            Density = 1000.0F
        });
        builder.AddMaterial(new PhysxMaterialDesc
        {
            Id = builder.DefineIdentity(UnicodeMaterialPath),
            StaticFriction = 0.2F,
            DynamicFriction = 0.1F,
            Restitution = 0.0F,
            Density = 750.0F,
            Flags = (uint)PhysxMaterialFlags.DisableStrongFriction
        });

        builder.AddShape(new PhysxShapeDesc
        {
            Id = builder.DefineIdentity(GroundColliderPath),
            Type = (uint)PhysxShapeType.Plane,
            LocalPose = PhysxTransform.Identity,
            Scale = new PhysxVec3f(1.0F, 1.0F, 1.0F),
            MaterialIndex = 0
        });
        builder.AddShape(new PhysxShapeDesc
        {
            Id = builder.DefineIdentity(BoxColliderPath),
            Type = (uint)PhysxShapeType.Box,
            LocalPose = PhysxTransform.Identity,
            Scale = new PhysxVec3f(1.0F, 1.0F, 1.0F),
            HalfExtents = new PhysxVec3f(0.5F, 0.5F, 0.5F),
            MaterialIndex = 1
        });

        uint pointOffset = builder.AddMeshPoints(HullPoints);
        uint indexOffset = builder.AddMeshIndices(HullIndices);
        builder.AddShape(new PhysxShapeDesc
        {
            Id = builder.DefineIdentity(HullColliderPath),
            Type = (uint)PhysxShapeType.ConvexMesh,
            LocalPose = PhysxTransform.Identity,
            Scale = new PhysxVec3f(1.0F, 1.0F, 1.0F),
            PointOffset = pointOffset,
            PointCount = (uint)HullPoints.Length,
            IndexOffset = indexOffset,
            IndexCount = (uint)HullIndices.Length,
            MaterialIndex = -1
        });

        builder.AddActorShape(new PhysxActorShapeRef(0, -1));
        builder.AddActorShape(new PhysxActorShapeRef(1, -1));
        builder.AddActorShape(new PhysxActorShapeRef(2, 0));

        builder.AddActor(new PhysxActorDesc
        {
            Id = builder.DefineIdentity(GroundPath),
            SceneIndex = 0,
            Type = (uint)PhysxActorType.Static,
            WorldPose = PhysxTransform.Identity,
            ShapeOffset = 0,
            ShapeCount = 1
        });
        builder.AddActor(new PhysxActorDesc
        {
            Id = builder.DefineIdentity(BoxPath),
            SceneIndex = 0,
            Type = (uint)PhysxActorType.Dynamic,
            WorldPose = new PhysxTransform(new PhysxVec3f(0.0F, 0.0F, 10.0F), PhysxQuatf.Identity),
            LinearVelocity = new PhysxVec3f(0.0F, 0.0F, -1.0F),
            Mass = 2.0F,
            CenterOfMass = new PhysxVec3f(0.0F, 0.0F, 0.0F),
            Inertia = new PhysxVec3f(1.0F, 1.0F, 1.0F),
            LinearDamping = 0.05F,
            AngularDamping = 0.05F,
            Flags = (uint)PhysxActorFlags.EnableCcd,
            ShapeOffset = 1,
            ShapeCount = 1,
            CollisionGroup = 1
        });
        builder.AddActor(new PhysxActorDesc
        {
            Id = builder.DefineIdentity(HullPath),
            SceneIndex = 0,
            Type = (uint)PhysxActorType.Kinematic,
            WorldPose = new PhysxTransform(new PhysxVec3f(1.0F, 2.0F, 3.0F), PhysxQuatf.Identity),
            Mass = 5.0F,
            Inertia = new PhysxVec3f(1.0F, 1.0F, 1.0F),
            ShapeOffset = 2,
            ShapeCount = 1,
            CollisionGroup = 2
        });

        builder.AddJoint(new PhysxJointDesc
        {
            Id = builder.DefineIdentity(JointPath),
            Type = (uint)PhysxJointType.Revolute,
            Flags = (uint)(PhysxJointFlags.LimitEnabled | PhysxJointFlags.DriveEnabled),
            Actor0Index = 1,
            Actor1Index = 2,
            LocalFrame0 = PhysxTransform.Identity,
            LocalFrame1 = PhysxTransform.Identity,
            Axis = (uint)PhysxAxis.Z,
            LowerLimit = -1.0F,
            UpperLimit = 1.0F,
            MinDistance = 0.0F,
            MaxDistance = 1.0F,
            DriveStiffness = 100.0F,
            DriveDamping = 10.0F,
            DriveMaxForce = 1000.0F,
            BreakForce = 500.0F,
            BreakTorque = 250.0F
        });

        builder.AddFilterPair(new PhysxFilterPair(1, 2));
    }
}
