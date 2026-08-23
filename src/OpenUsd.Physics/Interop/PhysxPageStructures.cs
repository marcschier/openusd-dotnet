// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenUsd.Physics.Interop;

/// <summary>Mirrors <c>openusd_physx_vec3f</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxVec3f : IEquatable<PhysxVec3f>
{
    /// <summary>The X component.</summary>
    public float X;

    /// <summary>The Y component.</summary>
    public float Y;

    /// <summary>The Z component.</summary>
    public float Z;

    /// <summary>Initializes a vector from its components.</summary>
    public PhysxVec3f(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Gets a value indicating whether every component is finite.</summary>
    public readonly bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);

    /// <summary>Gets a value indicating whether every component is exactly zero.</summary>
    public readonly bool IsZero => X == 0 && Y == 0 && Z == 0;

    /// <inheritdoc/>
    public readonly bool Equals(PhysxVec3f other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    /// <inheritdoc/>
    public readonly override bool Equals(object? obj) => obj is PhysxVec3f other && Equals(other);

    /// <inheritdoc/>
    public readonly override int GetHashCode() => HashCode.Combine(X, Y, Z);

    /// <summary>Determines whether two vectors are component-wise equal.</summary>
    public static bool operator ==(PhysxVec3f left, PhysxVec3f right) => left.Equals(right);

    /// <summary>Determines whether two vectors differ.</summary>
    public static bool operator !=(PhysxVec3f left, PhysxVec3f right) => !left.Equals(right);
}

/// <summary>Mirrors <c>openusd_physx_quatf</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxQuatf : IEquatable<PhysxQuatf>
{
    /// <summary>The imaginary X component.</summary>
    public float X;

    /// <summary>The imaginary Y component.</summary>
    public float Y;

    /// <summary>The imaginary Z component.</summary>
    public float Z;

    /// <summary>The real component.</summary>
    public float W;

    /// <summary>Initializes a quaternion from its components.</summary>
    public PhysxQuatf(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    /// <summary>Gets the identity rotation.</summary>
    public static PhysxQuatf Identity => new(0, 0, 0, 1);

    /// <summary>Gets a value indicating whether every component is finite.</summary>
    public readonly bool IsFinite =>
        float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z) && float.IsFinite(W);

    /// <summary>
    /// Gets a value indicating whether this rotation is usable, matching the native rule that the
    /// squared length lies inside <c>[0.25, 4.0]</c>.
    /// </summary>
    public readonly bool IsUsableRotation
    {
        get
        {
            if (!IsFinite)
            {
                return false;
            }

            double lengthSquared = ((double)X * X) + ((double)Y * Y) + ((double)Z * Z) + ((double)W * W);
            return lengthSquared is >= 0.25 and <= 4.0;
        }
    }

    /// <inheritdoc/>
    public readonly bool Equals(PhysxQuatf other) =>
        X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

    /// <inheritdoc/>
    public readonly override bool Equals(object? obj) => obj is PhysxQuatf other && Equals(other);

    /// <inheritdoc/>
    public readonly override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

    /// <summary>Determines whether two rotations are component-wise equal.</summary>
    public static bool operator ==(PhysxQuatf left, PhysxQuatf right) => left.Equals(right);

    /// <summary>Determines whether two rotations differ.</summary>
    public static bool operator !=(PhysxQuatf left, PhysxQuatf right) => !left.Equals(right);
}

/// <summary>Mirrors <c>openusd_physx_transform</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxTransform : IEquatable<PhysxTransform>
{
    /// <summary>The translation component.</summary>
    public PhysxVec3f Position;

    /// <summary>The rotation component.</summary>
    public PhysxQuatf Rotation;

    /// <summary>Initializes a transform from a position and rotation.</summary>
    public PhysxTransform(PhysxVec3f position, PhysxQuatf rotation)
    {
        Position = position;
        Rotation = rotation;
    }

    /// <summary>Gets the identity transform.</summary>
    public static PhysxTransform Identity => new(default, PhysxQuatf.Identity);

    /// <summary>Gets a value indicating whether every component is finite.</summary>
    public readonly bool IsFinite => Position.IsFinite && Rotation.IsFinite;

    /// <inheritdoc/>
    public readonly bool Equals(PhysxTransform other) =>
        Position.Equals(other.Position) && Rotation.Equals(other.Rotation);

    /// <inheritdoc/>
    public readonly override bool Equals(object? obj) => obj is PhysxTransform other && Equals(other);

    /// <inheritdoc/>
    public readonly override int GetHashCode() => HashCode.Combine(Position, Rotation);

    /// <summary>Determines whether two transforms are component-wise equal.</summary>
    public static bool operator ==(PhysxTransform left, PhysxTransform right) => left.Equals(right);

    /// <summary>Determines whether two transforms differ.</summary>
    public static bool operator !=(PhysxTransform left, PhysxTransform right) => !left.Equals(right);
}

/// <summary>Mirrors <c>openusd_physx_page_span</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxPageSpan
{
    /// <summary>The byte offset from the first byte of the page.</summary>
    public uint Offset;

    /// <summary>The element count, or the byte count for the string section.</summary>
    public uint Count;

    /// <summary>Initializes a span from a byte offset and element count.</summary>
    public PhysxPageSpan(uint offset, uint count)
    {
        Offset = offset;
        Count = count;
    }
}

/// <summary>Mirrors <c>openusd_physx_result_capacities</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxResultCapacities
{
    /// <summary>The maximum number of body states one result page can hold.</summary>
    public uint MaxBodyStates;

    /// <summary>The maximum number of events one result page can hold.</summary>
    public uint MaxEvents;

    /// <summary>The maximum number of diagnostics one result page can hold.</summary>
    public uint MaxDiagnostics;

    /// <summary>The maximum number of debug lines one result page can hold.</summary>
    public uint MaxDebugLines;

    /// <summary>The maximum number of hits one query batch can hold.</summary>
    public uint MaxQueryHits;

    /// <summary>The maximum number of deformation bodies one result page can hold.</summary>
    public uint MaxDeformationBodies;

    /// <summary>The maximum number of deformation vertices one result page can hold.</summary>
    public uint MaxDeformationPoints;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;
}

/// <summary>Mirrors <c>openusd_physx_build_page_header</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxBuildPageHeader
{
    /// <summary>The page magic; must equal <see cref="PhysxAbi.PageMagic"/>.</summary>
    public ulong Magic;

    /// <summary>The exact ABI version of this page.</summary>
    public uint AbiVersion;

    /// <summary>The size of this header, in bytes.</summary>
    public uint HeaderSize;

    /// <summary>The total page size, in bytes.</summary>
    public ulong ByteSize;

    /// <summary>The monotonic extraction revision this page was produced for.</summary>
    public ulong Revision;

    /// <summary>The physics-relevance fingerprint of the source stage.</summary>
    public ulong SourceHash;

    /// <summary>The authored <c>metersPerUnit</c>.</summary>
    public double MetersPerUnit;

    /// <summary>The authored <c>kilogramsPerUnit</c>.</summary>
    public double KilogramsPerUnit;

    /// <summary>The authored <c>timeCodesPerSecond</c>.</summary>
    public double TimeCodesPerSecond;

    /// <summary>The authored start time code.</summary>
    public double StartTimeCode;

    /// <summary>The authored end time code.</summary>
    public double EndTimeCode;

    /// <summary>The authored up axis, as a <see cref="PhysxUpAxis"/>.</summary>
    public uint UpAxis;

    /// <summary>Reserved page flags; must be zero for ABI version 1.</summary>
    public uint Flags;

    /// <summary>The fixed simulation rate, in hertz.</summary>
    public uint SimulationRateHz;

    /// <summary>The maximum number of substeps one step may advance.</summary>
    public uint MaxSubsteps;

    /// <summary>The UTF-8 string byte section.</summary>
    public PhysxPageSpan StringBytes;

    /// <summary>The identity table section.</summary>
    public PhysxPageSpan Identities;

    /// <summary>The scene section.</summary>
    public PhysxPageSpan Scenes;

    /// <summary>The material section.</summary>
    public PhysxPageSpan Materials;

    /// <summary>The shape section.</summary>
    public PhysxPageSpan Shapes;

    /// <summary>The actor section.</summary>
    public PhysxPageSpan Actors;

    /// <summary>The actor-to-shape reference section.</summary>
    public PhysxPageSpan ActorShapes;

    /// <summary>The joint section.</summary>
    public PhysxPageSpan Joints;

    /// <summary>The suppressed collision pair section.</summary>
    public PhysxPageSpan FilterPairs;

    /// <summary>The mesh point section.</summary>
    public PhysxPageSpan MeshPoints;

    /// <summary>The mesh index section.</summary>
    public PhysxPageSpan MeshIndices;

    /// <summary>The height field sample section.</summary>
    public PhysxPageSpan HeightfieldSamples;

    /// <summary>The articulation section.</summary>
    public PhysxPageSpan Articulations;

    /// <summary>The articulation link section.</summary>
    public PhysxPageSpan ArticulationLinks;

    /// <summary>The controller section.</summary>
    public PhysxPageSpan Controllers;

    /// <summary>The articulation tendon section.</summary>
    public PhysxPageSpan ArticulationTendons;

    /// <summary>The articulation tendon node section.</summary>
    public PhysxPageSpan ArticulationTendonNodes;

    /// <summary>The articulation mimic joint section.</summary>
    public PhysxPageSpan ArticulationMimicJoints;

    /// <summary>The vehicle section.</summary>
    public PhysxPageSpan Vehicles;

    /// <summary>The vehicle wheel section.</summary>
    public PhysxPageSpan VehicleWheels;

    /// <summary>The position based dynamics particle material section.</summary>
    public PhysxPageSpan ParticleMaterials;

    /// <summary>The particle system section.</summary>
    public PhysxPageSpan ParticleSystems;

    /// <summary>The particle body section.</summary>
    public PhysxPageSpan ParticleBodies;

    /// <summary>The surface and volume deformable material section.</summary>
    public PhysxPageSpan DeformableMaterials;

    /// <summary>The surface and volume deformable section.</summary>
    public PhysxPageSpan Deformables;

    /// <summary>The result capacities every result page must satisfy.</summary>
    public PhysxResultCapacities Capacities;

    /// <summary>Reserved; must be zero.</summary>
    public ulong Reserved0;

    /// <summary>Reserved; must be zero.</summary>
    public ulong Reserved1;

    /// <summary>Reserved; must be zero.</summary>
    public ulong Reserved2;
}

/// <summary>Mirrors <c>openusd_physx_identity</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxIdentityRecord
{
    /// <summary>The stable identity; never <see cref="PhysxAbi.InvalidId"/>.</summary>
    public ulong Id;

    /// <summary>The byte offset of the prim path inside the string section.</summary>
    public uint PathOffset;

    /// <summary>The byte length of the prim path, without a terminator.</summary>
    public uint PathLength;

    /// <summary>The instance domain, as a <see cref="PhysxInstanceDomain"/>.</summary>
    public uint InstanceDomain;

    /// <summary>The instance index inside the instance domain.</summary>
    public uint InstanceIndex;
}

/// <summary>Mirrors <c>openusd_physx_scene_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxSceneDesc
{
    /// <summary>The scene identity.</summary>
    public ulong Id;

    /// <summary>The gravity direction.</summary>
    public PhysxVec3f GravityDirection;

    /// <summary>The gravity magnitude, in stage units per second squared.</summary>
    public float GravityMagnitude;

    /// <summary>The scene flags, as <see cref="PhysxSceneFlags"/>.</summary>
    public uint Flags;

    /// <summary>The solver position iteration count.</summary>
    public uint PositionIterations;

    /// <summary>The solver velocity iteration count.</summary>
    public uint VelocityIterations;

    /// <summary>The restitution bounce threshold.</summary>
    public float BounceThreshold;

    /// <summary>The contact offset.</summary>
    public float ContactOffset;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;
}

/// <summary>Mirrors <c>openusd_physx_material_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxMaterialDesc
{
    /// <summary>The material identity.</summary>
    public ulong Id;

    /// <summary>The static friction coefficient.</summary>
    public float StaticFriction;

    /// <summary>The dynamic friction coefficient.</summary>
    public float DynamicFriction;

    /// <summary>The restitution coefficient, between zero and one.</summary>
    public float Restitution;

    /// <summary>The density used when an actor requests computed mass.</summary>
    public float Density;

    /// <summary>The material flags, as <see cref="PhysxMaterialFlags"/>.</summary>
    public uint Flags;

    /// <summary>How two friction coefficients combine, as a <see cref="PhysxCombineMode"/>.</summary>
    public uint FrictionCombineMode;

    /// <summary>How two restitution coefficients combine, as a <see cref="PhysxCombineMode"/>.</summary>
    public uint RestitutionCombineMode;

    /// <summary>The contact damping read only for a compliant contact material.</summary>
    public float Damping;
}

/// <summary>Mirrors <c>openusd_physx_shape_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxShapeDesc
{
    /// <summary>The shape identity.</summary>
    public ulong Id;

    /// <summary>The shape type, as a <see cref="PhysxShapeType"/>.</summary>
    public uint Type;

    /// <summary>The shape flags, as <see cref="PhysxShapeFlags"/>.</summary>
    public uint Flags;

    /// <summary>The shape pose relative to its actor.</summary>
    public PhysxTransform LocalPose;

    /// <summary>The positive scale applied to the shape geometry.</summary>
    public PhysxVec3f Scale;

    /// <summary>The box half extents.</summary>
    public PhysxVec3f HalfExtents;

    /// <summary>The sphere or capsule radius.</summary>
    public float Radius;

    /// <summary>The capsule half height.</summary>
    public float HalfHeight;

    /// <summary>The element offset into the mesh point section.</summary>
    public uint PointOffset;

    /// <summary>The number of mesh points.</summary>
    public uint PointCount;

    /// <summary>The element offset into the mesh index section.</summary>
    public uint IndexOffset;

    /// <summary>The number of mesh indices.</summary>
    public uint IndexCount;

    /// <summary>The material index, or <c>-1</c> to use the world default material.</summary>
    public int MaterialIndex;

    /// <summary>The contact offset; zero asks for the value the scene declares.</summary>
    public float ContactOffset;

    /// <summary>The rest offset; must stay below a positive contact offset.</summary>
    public float RestOffset;

    /// <summary>The torsional friction patch radius; zero keeps no torsional friction.</summary>
    public float TorsionalPatchRadius;

    /// <summary>The minimum torsional friction patch radius.</summary>
    public float MinTorsionalPatchRadius;

    /// <summary>The element offset into the height field sample section.</summary>
    public uint SampleOffset;

    /// <summary>The number of height field sample rows.</summary>
    public uint RowCount;

    /// <summary>The number of height field sample columns.</summary>
    public uint ColumnCount;

    /// <summary>The distance one raw height unit represents.</summary>
    public float HeightScale;

    /// <summary>The distance between two height field rows.</summary>
    public float RowScale;

    /// <summary>The distance between two height field columns.</summary>
    public float ColumnScale;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved1;
}

/// <summary>Mirrors <c>openusd_physx_heightfield_sample</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxHeightfieldSample
{
    /// <summary>The raw height, scaled by the shape height scale.</summary>
    public short Height;

    /// <summary>The material index of the first sample triangle.</summary>
    public byte Material0;

    /// <summary>The material index of the second sample triangle.</summary>
    public byte Material1;
}

/// <summary>Mirrors <c>openusd_physx_actor_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxActorDesc
{
    /// <summary>The actor identity.</summary>
    public ulong Id;

    /// <summary>The owning scene index.</summary>
    public int SceneIndex;

    /// <summary>The actor type, as a <see cref="PhysxActorType"/>.</summary>
    public uint Type;

    /// <summary>The world pose at build time.</summary>
    public PhysxTransform WorldPose;

    /// <summary>The initial linear velocity.</summary>
    public PhysxVec3f LinearVelocity;

    /// <summary>The initial angular velocity.</summary>
    public PhysxVec3f AngularVelocity;

    /// <summary>The mass; zero requests density based mass computation.</summary>
    public float Mass;

    /// <summary>The center of mass in actor space.</summary>
    public PhysxVec3f CenterOfMass;

    /// <summary>The diagonal inertia tensor.</summary>
    public PhysxVec3f Inertia;

    /// <summary>
    /// The rotation from the actor frame into the frame <see cref="Inertia"/> is stated in.
    /// </summary>
    /// <remarks>
    /// An all zero quaternion is what a default initialized description carries and stands for the
    /// identity rotation, so a page never has to author it when the inertia is stated about the
    /// actor axes.
    /// </remarks>
    public PhysxQuatf PrincipalAxes;

    /// <summary>The linear damping coefficient.</summary>
    public float LinearDamping;

    /// <summary>The angular damping coefficient.</summary>
    public float AngularDamping;

    /// <summary>The actor flags, as <see cref="PhysxActorFlags"/>.</summary>
    public uint Flags;

    /// <summary>The element offset into the actor shape section.</summary>
    public uint ShapeOffset;

    /// <summary>The number of referenced shapes; at least one.</summary>
    public uint ShapeCount;

    /// <summary>The collision group, between zero and thirty one.</summary>
    public uint CollisionGroup;

    /// <summary>The solver position iterations; zero selects the value the scene declares.</summary>
    public uint PositionIterations;

    /// <summary>The solver velocity iterations; zero selects the value the scene declares.</summary>
    public uint VelocityIterations;

    /// <summary>The maximum linear velocity; zero keeps the runtime default.</summary>
    public float MaxLinearVelocity;

    /// <summary>The maximum angular velocity; zero keeps the runtime default.</summary>
    public float MaxAngularVelocity;

    /// <summary>The maximum depenetration velocity; zero keeps the runtime default.</summary>
    public float MaxDepenetrationVelocity;

    /// <summary>The maximum contact impulse; zero keeps the runtime default.</summary>
    public float MaxContactImpulse;

    /// <summary>The sleep energy threshold; zero keeps the threshold the scene decides.</summary>
    public float SleepThreshold;

    /// <summary>The stabilization energy threshold.</summary>
    public float StabilizationThreshold;

    /// <summary>The seconds the body stays awake after it is woken.</summary>
    public float WakeCounter;

    /// <summary>The minimum continuous collision advance coefficient, between zero and one.</summary>
    public float MinCcdAdvanceCoefficient;

    /// <summary>The contact slop coefficient.</summary>
    public float ContactSlopCoefficient;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;
}

/// <summary>Mirrors <c>openusd_physx_actor_shape_ref</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxActorShapeRef
{
    /// <summary>The referenced shape index.</summary>
    public uint ShapeIndex;

    /// <summary>The material index override, or <c>-1</c> to keep the material of the shape.</summary>
    public int MaterialIndex;

    /// <summary>Initializes an actor shape reference.</summary>
    public PhysxActorShapeRef(uint shapeIndex, int materialIndex)
    {
        ShapeIndex = shapeIndex;
        MaterialIndex = materialIndex;
    }
}

/// <summary>Mirrors <c>openusd_physx_joint_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxJointDesc
{
    /// <summary>The joint identity.</summary>
    public ulong Id;

    /// <summary>The joint type, as a <see cref="PhysxJointType"/>.</summary>
    public uint Type;

    /// <summary>The joint flags, as <see cref="PhysxJointFlags"/>.</summary>
    public uint Flags;

    /// <summary>The first actor index, or <c>-1</c> to attach to the static world frame.</summary>
    public int Actor0Index;

    /// <summary>The second actor index, or <c>-1</c> to attach to the static world frame.</summary>
    public int Actor1Index;

    /// <summary>The joint frame relative to the first actor.</summary>
    public PhysxTransform LocalFrame0;

    /// <summary>The joint frame relative to the second actor.</summary>
    public PhysxTransform LocalFrame1;

    /// <summary>The primary axis, as a <see cref="PhysxAxis"/>.</summary>
    public uint Axis;

    /// <summary>The lower limit.</summary>
    public float LowerLimit;

    /// <summary>The upper limit.</summary>
    public float UpperLimit;

    /// <summary>The minimum distance for a distance joint.</summary>
    public float MinDistance;

    /// <summary>The maximum distance for a distance joint.</summary>
    public float MaxDistance;

    /// <summary>The first cone limit angle.</summary>
    public float ConeAngle0;

    /// <summary>The second cone limit angle.</summary>
    public float ConeAngle1;

    /// <summary>The drive stiffness.</summary>
    public float DriveStiffness;

    /// <summary>The drive damping.</summary>
    public float DriveDamping;

    /// <summary>The maximum drive force.</summary>
    public float DriveMaxForce;

    /// <summary>The drive target position.</summary>
    public float DriveTargetPosition;

    /// <summary>The drive target velocity.</summary>
    public float DriveTargetVelocity;

    /// <summary>The break force.</summary>
    public float BreakForce;

    /// <summary>The break torque.</summary>
    public float BreakTorque;

    /// <summary>The soft limit spring stiffness, read only for a soft limit.</summary>
    public float LimitStiffness;

    /// <summary>The soft limit spring damping, read only for a soft limit.</summary>
    public float LimitDamping;

    /// <summary>The hard limit restitution, between zero and one.</summary>
    public float LimitRestitution;

    /// <summary>The hard limit bounce threshold.</summary>
    public float LimitBounceThreshold;

    /// <summary>The limit contact distance.</summary>
    public float LimitContactDistance;

    /// <summary>The inverse mass scale of the first body; zero keeps the unscaled mass.</summary>
    public float InvMassScale0;

    /// <summary>The inverse inertia scale of the first body; zero keeps the unscaled inertia.</summary>
    public float InvInertiaScale0;

    /// <summary>The inverse mass scale of the second body; zero keeps the unscaled mass.</summary>
    public float InvMassScale1;

    /// <summary>The inverse inertia scale of the second body; zero keeps the unscaled inertia.</summary>
    public float InvInertiaScale1;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved1;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved2;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved3;

    /// <summary>The per axis motion, as a <see cref="PhysxJointMotion"/>.</summary>
    public PhysxJointAxisUInt32 Motion;

    /// <summary>The per axis lower limit.</summary>
    public PhysxJointAxisSingle AxisLowerLimit;

    /// <summary>The per axis upper limit.</summary>
    public PhysxJointAxisSingle AxisUpperLimit;

    /// <summary>The per axis drive stiffness.</summary>
    public PhysxJointAxisSingle AxisDriveStiffness;

    /// <summary>The per axis drive damping.</summary>
    public PhysxJointAxisSingle AxisDriveDamping;

    /// <summary>The per axis maximum drive force.</summary>
    public PhysxJointAxisSingle AxisDriveMaxForce;

    /// <summary>The per axis drive target position.</summary>
    public PhysxJointAxisSingle AxisDriveTargetPosition;

    /// <summary>The per axis drive target velocity.</summary>
    public PhysxJointAxisSingle AxisDriveTargetVelocity;

    /// <summary>The per axis drive flags, as <see cref="PhysxJointDriveFlags"/>.</summary>
    public PhysxJointAxisUInt32 AxisDriveFlags;
}

/// <summary>One unsigned value for every <see cref="PhysxJointAxis"/>.</summary>
[InlineArray(PhysxAbi.JointAxisCount)]
internal struct PhysxJointAxisUInt32
{
    private uint _element0;
}

/// <summary>One single precision value for every <see cref="PhysxJointAxis"/>.</summary>
[InlineArray(PhysxAbi.JointAxisCount)]
internal struct PhysxJointAxisSingle
{
    private float _element0;
}

/// <summary>Mirrors <c>openusd_physx_filter_pair</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxFilterPair
{
    /// <summary>The first actor index.</summary>
    public uint Actor0Index;

    /// <summary>The second actor index.</summary>
    public uint Actor1Index;

    /// <summary>Initializes a suppressed collision pair.</summary>
    public PhysxFilterPair(uint actor0Index, uint actor1Index)
    {
        Actor0Index = actor0Index;
        Actor1Index = actor1Index;
    }
}

/// <summary>Mirrors <c>openusd_physx_articulation_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxArticulationDesc
{
    /// <summary>The articulation identity.</summary>
    public ulong Id;

    /// <summary>The owning scene index.</summary>
    public int SceneIndex;

    /// <summary>The articulation flags, as <see cref="PhysxArticulationFlags"/>.</summary>
    public uint Flags;

    /// <summary>The element offset into the articulation link section.</summary>
    public uint LinkOffset;

    /// <summary>The number of links owned by this articulation.</summary>
    public uint LinkCount;

    /// <summary>The solver position iterations; zero selects the value the scene declares.</summary>
    public uint PositionIterations;

    /// <summary>The solver velocity iterations; zero selects the value the scene declares.</summary>
    public uint VelocityIterations;

    /// <summary>The sleep energy threshold; zero keeps the threshold the scene decides.</summary>
    public float SleepThreshold;

    /// <summary>The stabilization energy threshold.</summary>
    public float StabilizationThreshold;

    /// <summary>The maximum joint velocity; zero keeps the runtime default.</summary>
    public float MaxJointVelocity;

    /// <summary>The seconds the articulation stays awake after it is woken.</summary>
    public float WakeCounter;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved1;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved2;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved3;
}

/// <summary>Mirrors <c>openusd_physx_articulation_link_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxArticulationLinkDesc
{
    /// <summary>The link identity.</summary>
    public ulong Id;

    /// <summary>The parent link identity, or zero for the root link.</summary>
    public ulong ParentId;

    /// <summary>The world pose at build time.</summary>
    public PhysxTransform WorldPose;

    /// <summary>The joint frame stated in the parent link frame.</summary>
    public PhysxTransform ParentFrame;

    /// <summary>The joint frame stated in this link frame.</summary>
    public PhysxTransform ChildFrame;

    /// <summary>The center of mass in link space.</summary>
    public PhysxVec3f CenterOfMass;

    /// <summary>The diagonal inertia tensor.</summary>
    public PhysxVec3f Inertia;

    /// <summary>The rotation from the link frame into the frame the inertia is stated in.</summary>
    public PhysxQuatf PrincipalAxes;

    /// <summary>The mass; zero requests density based mass computation.</summary>
    public float Mass;

    /// <summary>The linear damping coefficient.</summary>
    public float LinearDamping;

    /// <summary>The angular damping coefficient.</summary>
    public float AngularDamping;

    /// <summary>The maximum linear velocity; zero keeps the runtime default.</summary>
    public float MaxLinearVelocity;

    /// <summary>The maximum angular velocity; zero keeps the runtime default.</summary>
    public float MaxAngularVelocity;

    /// <summary>The Coulomb friction of the inbound joint; zero keeps the SDK default.</summary>
    public float JointFriction;

    /// <summary>The maximum joint velocity; zero keeps the runtime default.</summary>
    public float MaxJointVelocity;

    /// <summary>The joint type, as a <see cref="PhysxArticulationJointType"/>.</summary>
    public uint JointType;

    /// <summary>The link flags, as <see cref="PhysxArticulationLinkFlags"/>.</summary>
    public uint Flags;

    /// <summary>The element offset into the actor shape section.</summary>
    public uint ShapeOffset;

    /// <summary>The number of referenced shapes.</summary>
    public uint ShapeCount;

    /// <summary>The collision group, between zero and thirty one.</summary>
    public uint CollisionGroup;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>The per axis motion, as a <see cref="PhysxJointMotion"/>.</summary>
    public PhysxJointAxisUInt32 Motion;

    /// <summary>The per axis lower limit.</summary>
    public PhysxJointAxisSingle LowerLimit;

    /// <summary>The per axis upper limit.</summary>
    public PhysxJointAxisSingle UpperLimit;

    /// <summary>The per axis drive stiffness.</summary>
    public PhysxJointAxisSingle DriveStiffness;

    /// <summary>The per axis drive damping.</summary>
    public PhysxJointAxisSingle DriveDamping;

    /// <summary>The per axis maximum drive force.</summary>
    public PhysxJointAxisSingle DriveMaxForce;

    /// <summary>The per axis drive target position.</summary>
    public PhysxJointAxisSingle DriveTargetPosition;

    /// <summary>The per axis drive target velocity.</summary>
    public PhysxJointAxisSingle DriveTargetVelocity;

    /// <summary>The per axis drive flags, as <see cref="PhysxJointDriveFlags"/>.</summary>
    public PhysxJointAxisUInt32 DriveFlags;

    /// <summary>The per axis armature; zero adds no additional inertia.</summary>
    public PhysxJointAxisSingle Armature;
}

/// <summary>Mirrors <c>openusd_physx_controller_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxControllerDesc
{
    /// <summary>The controller identity.</summary>
    public ulong Id;

    /// <summary>The owning scene index.</summary>
    public int SceneIndex;

    /// <summary>The controller shape, as a <see cref="PhysxControllerShape"/>.</summary>
    public uint Shape;

    /// <summary>The initial world-space foot position.</summary>
    public PhysxVec3f Position;

    /// <summary>The up direction; all zero reads as the page up axis.</summary>
    public PhysxVec3f UpDirection;

    /// <summary>The capsule radius.</summary>
    public float Radius;

    /// <summary>The capsule height between the two sphere centres.</summary>
    public float Height;

    /// <summary>The box half extents, read only for a box controller.</summary>
    public PhysxVec3f HalfExtents;

    /// <summary>The slope limit in radians; zero keeps the SDK default.</summary>
    public float SlopeLimit;

    /// <summary>The maximum step height the controller can climb.</summary>
    public float StepOffset;

    /// <summary>The contact offset.</summary>
    public float ContactOffset;

    /// <summary>The density used for mass computation.</summary>
    public float Density;

    /// <summary>The scale coefficient; must be at most one.</summary>
    public float ScaleCoefficient;

    /// <summary>The volume growth factor.</summary>
    public float VolumeGrowth;

    /// <summary>The controller flags, as <see cref="PhysxControllerFlags"/>.</summary>
    public uint Flags;

    /// <summary>The non-walkable mode, as a <see cref="PhysxControllerNonWalkableMode"/>.</summary>
    public uint NonWalkableMode;

    /// <summary>The climbing mode, as a <see cref="PhysxControllerClimbingMode"/>.</summary>
    public uint ClimbingMode;

    /// <summary>The material index, or <c>-1</c> to use the world default material.</summary>
    public int MaterialIndex;

    /// <summary>The collision group, between zero and thirty one.</summary>
    public uint CollisionGroup;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved1;
}

/// <summary>Mirrors <c>openusd_physx_tendon_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxTendonDesc
{
    /// <summary>The tendon identity.</summary>
    public ulong Id;

    /// <summary>The owning articulation index.</summary>
    public uint ArticulationIndex;

    /// <summary>The tendon kind, as a <see cref="PhysxTendonType"/>.</summary>
    public uint Type;

    /// <summary>The first node of this tendon in the tendon node section.</summary>
    public uint NodeOffset;

    /// <summary>The number of nodes this tendon owns; at least one.</summary>
    public uint NodeCount;

    /// <summary>The tendon flags, as <see cref="PhysxTendonFlags"/>.</summary>
    public uint Flags;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>The tendon stiffness.</summary>
    public float Stiffness;

    /// <summary>The tendon damping.</summary>
    public float Damping;

    /// <summary>The stiffness applied once the tendon passes a limit.</summary>
    public float LimitStiffness;

    /// <summary>The tendon offset.</summary>
    public float Offset;

    /// <summary>The rest length, read for fixed tendons only.</summary>
    public float RestLength;

    /// <summary>The low limit, read for fixed tendons only.</summary>
    public float LowLimit;

    /// <summary>The high limit, read for fixed tendons only.</summary>
    public float HighLimit;

    /// <summary>Reserved; must be zero.</summary>
    public float Reserved1;
}

/// <summary>Mirrors <c>openusd_physx_tendon_node_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxTendonNodeDesc
{
    /// <summary>The node identity.</summary>
    public ulong Id;

    /// <summary>Zero for the tendon root, otherwise the one based local index of an earlier node.</summary>
    public uint ParentIndex;

    /// <summary>The local link index inside the articulation window.</summary>
    public uint LinkIndex;

    /// <summary>The coupled axis, as a <see cref="PhysxJointAxis"/>; fixed tendons only.</summary>
    public uint Axis;

    /// <summary>The node flags, as <see cref="PhysxTendonFlags"/>.</summary>
    public uint Flags;

    /// <summary>The node coefficient.</summary>
    public float Coefficient;

    /// <summary>The reciprocal coefficient; fixed tendons only.</summary>
    public float RecipCoefficient;

    /// <summary>The attachment offset in the link frame; spatial tendons only.</summary>
    public PhysxVec3f RelativeOffset;

    /// <summary>The rest length; spatial leaf attachments only.</summary>
    public float RestLength;

    /// <summary>The low limit; spatial leaf attachments only.</summary>
    public float LowLimit;

    /// <summary>The high limit; spatial leaf attachments only.</summary>
    public float HighLimit;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved1;
}

/// <summary>Mirrors <c>openusd_physx_mimic_joint_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxMimicJointDesc
{
    /// <summary>The mimic joint identity.</summary>
    public ulong Id;

    /// <summary>The owning articulation index.</summary>
    public uint ArticulationIndex;

    /// <summary>The local link index whose inbound joint is the driven joint.</summary>
    public uint LinkA;

    /// <summary>The driven axis, as a <see cref="PhysxJointAxis"/>.</summary>
    public uint AxisA;

    /// <summary>The local link index whose inbound joint is the reference joint.</summary>
    public uint LinkB;

    /// <summary>The reference axis, as a <see cref="PhysxJointAxis"/>.</summary>
    public uint AxisB;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>The gear ratio enforced as <c>qA + gearRatio * qB + offset = 0</c>.</summary>
    public float GearRatio;

    /// <summary>The offset enforced by the mimic joint.</summary>
    public float Offset;

    /// <summary>The compliance natural frequency; zero makes the coupling rigid.</summary>
    public float NaturalFrequency;

    /// <summary>The compliance damping ratio; zero makes the coupling rigid.</summary>
    public float DampingRatio;

    /// <summary>Reserved; must be zero.</summary>
    public ulong Reserved1;

    /// <summary>Reserved; must be zero.</summary>
    public ulong Reserved2;
}

/// <summary>Mirrors <c>openusd_physx_vehicle_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxVehicleDesc
{
    /// <summary>The vehicle identity.</summary>
    public ulong Id;

    /// <summary>The owning scene index.</summary>
    public uint SceneIndex;

    /// <summary>The chassis actor index in the actor section.</summary>
    public uint ActorIndex;

    /// <summary>The first wheel of this vehicle in the vehicle wheel section.</summary>
    public uint WheelOffset;

    /// <summary>The number of wheels this vehicle owns; at least one.</summary>
    public uint WheelCount;

    /// <summary>The vehicle flags, as <see cref="PhysxVehicleFlags"/>.</summary>
    public uint Flags;

    /// <summary>The drivetrain, as a <see cref="PhysxVehicleDrive"/>.</summary>
    public uint Drive;

    /// <summary>The road geometry query, as a <see cref="PhysxVehicleQuery"/>.</summary>
    public uint Query;

    /// <summary>The forward axis, as a <see cref="PhysxAxis"/>.</summary>
    public uint LongitudinalAxis;

    /// <summary>The right axis, as a <see cref="PhysxAxis"/>.</summary>
    public uint LateralAxis;

    /// <summary>The up axis, as a <see cref="PhysxAxis"/>.</summary>
    public uint VerticalAxis;

    /// <summary>The chassis mass; zero keeps the actor mass.</summary>
    public float ChassisMass;

    /// <summary>The chassis moment of inertia; all zero keeps the actor inertia.</summary>
    public PhysxVec3f ChassisMoi;

    /// <summary>The peak engine torque.</summary>
    public float EnginePeakTorque;

    /// <summary>The engine moment of inertia.</summary>
    public float EngineMoi;

    /// <summary>The engine idle angular speed.</summary>
    public float EngineIdleOmega;

    /// <summary>The engine maximum angular speed.</summary>
    public float EngineMaxOmega;

    /// <summary>The engine damping at full throttle.</summary>
    public float EngineDampingFullThrottle;

    /// <summary>The engine damping at zero throttle with the clutch engaged.</summary>
    public float EngineDampingZeroThrottleClutchEngaged;

    /// <summary>The engine damping at zero throttle with the clutch disengaged.</summary>
    public float EngineDampingZeroThrottleClutchDisengaged;

    /// <summary>The clutch strength.</summary>
    public float ClutchStrength;

    /// <summary>The time one gear change takes, in seconds.</summary>
    public float GearSwitchTime;

    /// <summary>The final drive ratio.</summary>
    public float FinalGearRatio;

    /// <summary>The reverse gear ratio, as a positive magnitude.</summary>
    public float ReverseGearRatio;

    /// <summary>The first forward gear ratio.</summary>
    public float FirstGearRatio;

    /// <summary>The top forward gear ratio.</summary>
    public float TopGearRatio;

    /// <summary>The engine speed fraction at which the autobox shifts up.</summary>
    public float AutoboxUpRatio;

    /// <summary>The engine speed fraction at which the autobox shifts down.</summary>
    public float AutoboxDownRatio;

    /// <summary>The minimum time between two autobox shifts, in seconds.</summary>
    public float AutoboxLatency;

    /// <summary>The number of forward gears; at least one.</summary>
    public uint ForwardGearCount;

    /// <summary>The maximum brake torque.</summary>
    public float MaxBrakeTorque;

    /// <summary>The maximum handbrake torque.</summary>
    public float MaxHandBrakeTorque;

    /// <summary>The steer angle at full lock, in radians.</summary>
    public float MaxSteerAngle;

    /// <summary>The friction used when no material friction entry matches.</summary>
    public float DefaultFriction;

    /// <summary>The total sprung mass; zero resolves it from the chassis.</summary>
    public float SprungMassTotal;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved1;
}

/// <summary>Mirrors <c>openusd_physx_vehicle_wheel_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxVehicleWheelDesc
{
    /// <summary>The wheel identity.</summary>
    public ulong Id;

    /// <summary>Where the suspension is anchored on the chassis, in chassis space.</summary>
    public PhysxTransform SuspensionAttachment;

    /// <summary>The suspension travel direction in chassis space.</summary>
    public PhysxVec3f SuspensionTravelDir;

    /// <summary>The suspension travel distance.</summary>
    public float SuspensionTravelDist;

    /// <summary>Where the wheel sits relative to the suspension frame.</summary>
    public PhysxTransform WheelAttachment;

    /// <summary>The wheel radius.</summary>
    public float Radius;

    /// <summary>The wheel half width.</summary>
    public float HalfWidth;

    /// <summary>The wheel mass.</summary>
    public float Mass;

    /// <summary>The wheel moment of inertia; zero resolves a solid disc.</summary>
    public float Moi;

    /// <summary>The wheel damping rate.</summary>
    public float DampingRate;

    /// <summary>The suspension spring stiffness.</summary>
    public float SuspensionStiffness;

    /// <summary>The suspension damping.</summary>
    public float SuspensionDamping;

    /// <summary>The sprung mass; zero splits the chassis mass evenly.</summary>
    public float SprungMass;

    /// <summary>The tire lateral stiffness normalised load.</summary>
    public float TireLatStiffX;

    /// <summary>The tire lateral stiffness per unit normalised load.</summary>
    public float TireLatStiffY;

    /// <summary>The tire longitudinal stiffness.</summary>
    public float TireLongStiff;

    /// <summary>The tire camber stiffness.</summary>
    public float TireCamberStiff;

    /// <summary>The tire rest load; zero uses the sprung mass weight.</summary>
    public float TireRestLoad;

    /// <summary>The tire friction multiplier.</summary>
    public float TireFriction;

    /// <summary>The fraction of the maximum steer angle this wheel answers.</summary>
    public float SteerResponse;

    /// <summary>The fraction of the maximum brake torque this wheel answers.</summary>
    public float BrakeResponse;

    /// <summary>The fraction of the maximum handbrake torque this wheel answers.</summary>
    public float HandBrakeResponse;

    /// <summary>The differential share; zero shares evenly across driven wheels.</summary>
    public float DriveTorqueRatio;

    /// <summary>The axle this wheel belongs to.</summary>
    public uint AxleIndex;

    /// <summary>The wheel flags, as <see cref="PhysxVehicleWheelFlags"/>.</summary>
    public uint Flags;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved1;
}
/// <summary>Mirrors <c>openusd_physx_particle_material_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxParticleMaterialDesc
{
    /// <summary>The particle material identity.</summary>
    public ulong Id;

    /// <summary>The friction coefficient against other objects.</summary>
    public float Friction;

    /// <summary>The velocity damping applied to the particles.</summary>
    public float Damping;

    /// <summary>The attraction between particles and other objects.</summary>
    public float Adhesion;

    /// <summary>The scale applied to the particle contact offset when evaluating adhesion.</summary>
    public float AdhesionOffsetScale;

    /// <summary>The scale applied to the friction for particle-particle contacts.</summary>
    public float ParticleFrictionScale;

    /// <summary>The scale applied to the adhesion for particle-particle contacts.</summary>
    public float ParticleAdhesionScale;

    /// <summary>The fluid viscosity.</summary>
    public float Viscosity;

    /// <summary>The fluid surface tension.</summary>
    public float SurfaceTension;

    /// <summary>The attraction between particles of the same set.</summary>
    public float Cohesion;

    /// <summary>The strength of the fluid vorticity confinement term.</summary>
    public float VorticityConfinement;

    /// <summary>The aerodynamic drag coefficient.</summary>
    public float Drag;

    /// <summary>The aerodynamic lift coefficient.</summary>
    public float Lift;

    /// <summary>The scale applied to scene gravity; zero keeps unscaled gravity.</summary>
    public float GravityScale;

    /// <summary>The particle density; zero requests the runtime default.</summary>
    public float Density;

    /// <summary>The Courant-Friedrichs-Lewy coefficient; zero keeps the runtime default.</summary>
    public float CflCoefficient;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;
}

/// <summary>Mirrors <c>openusd_physx_particle_system_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxParticleSystemDesc
{
    /// <summary>The particle system identity.</summary>
    public ulong Id;

    /// <summary>The owning scene index.</summary>
    public int SceneIndex;

    /// <summary>The system flags, as <see cref="PhysxParticleSystemFlags"/>.</summary>
    public uint Flags;

    /// <summary>The contact offset against other objects; zero requests the runtime default.</summary>
    public float ContactOffset;

    /// <summary>The rest offset against other objects; zero requests the runtime default.</summary>
    public float RestOffset;

    /// <summary>The particle-particle contact offset; zero requests the runtime default.</summary>
    public float ParticleContactOffset;

    /// <summary>The solid particle rest offset; zero requests the runtime default.</summary>
    public float SolidRestOffset;

    /// <summary>The fluid particle rest offset; zero requests the runtime default.</summary>
    public float FluidRestOffset;

    /// <summary>The depenetration velocity bound; zero keeps the runtime default.</summary>
    public float MaxDepenetrationVelocity;

    /// <summary>The neighbourhood scale; zero keeps the runtime default.</summary>
    public float NeighborhoodScale;

    /// <summary>The neighbourhood budget; zero keeps the runtime default.</summary>
    public uint MaxNeighborhood;

    /// <summary>The solver position iteration count; zero selects the scene value.</summary>
    public uint SolverPositionIterations;

    /// <summary>The wind velocity applied to the system.</summary>
    public PhysxVec3f Wind;

    /// <summary>The element offset of this system's particle body window.</summary>
    public uint BodyOffset;

    /// <summary>The element count of this system's particle body window.</summary>
    public uint BodyCount;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved1;
}

/// <summary>Mirrors <c>openusd_physx_particle_body_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxParticleBodyDesc
{
    /// <summary>The particle body identity.</summary>
    public ulong Id;

    /// <summary>The body kind, as a <see cref="PhysxParticleBodyKind"/>.</summary>
    public uint Kind;

    /// <summary>The body flags, as <see cref="PhysxParticleBodyFlags"/>.</summary>
    public uint Flags;

    /// <summary>The collision filtering group of these particles.</summary>
    public uint ParticleGroup;

    /// <summary>The bound particle material index; -1 selects the world default.</summary>
    public int MaterialIndex;

    /// <summary>The total mass; zero requests a mass derived from the material density.</summary>
    public float Mass;

    /// <summary>The element offset into the mesh point section.</summary>
    public uint PointOffset;

    /// <summary>The element count of the particle point window.</summary>
    public uint PointCount;

    /// <summary>The world pose the authored rest configuration is placed by.</summary>
    public PhysxTransform WorldPose;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved1;
}

/// <summary>Mirrors <c>openusd_physx_deformable_material_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxDeformableMaterialDesc
{
    /// <summary>The deformable material identity.</summary>
    public ulong Id;

    /// <summary>The deformable kind this material applies to, as a <see cref="PhysxDeformableKind"/>.</summary>
    public uint Kind;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>The stretch resistance.</summary>
    public float YoungsModulus;

    /// <summary>The transverse contraction, in the open zero to one half interval.</summary>
    public float PoissonsRatio;

    /// <summary>The friction coefficient against other objects.</summary>
    public float DynamicFriction;

    /// <summary>The material density.</summary>
    public float Density;

    /// <summary>The damping applied to the elastic response.</summary>
    public float ElasticityDamping;

    /// <summary>The bending stiffness; surface materials only.</summary>
    public float BendingStiffness;

    /// <summary>The bending damping; surface materials only.</summary>
    public float BendingDamping;

    /// <summary>The simulated shell thickness; surface materials only.</summary>
    public float Thickness;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved1;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved2;
}

/// <summary>Mirrors <c>openusd_physx_deformable_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxDeformableDesc
{
    /// <summary>The deformable identity.</summary>
    public ulong Id;

    /// <summary>The owning scene index.</summary>
    public int SceneIndex;

    /// <summary>The deformable kind, as a <see cref="PhysxDeformableKind"/>.</summary>
    public uint Kind;

    /// <summary>The deformable flags, as <see cref="PhysxDeformableFlags"/>.</summary>
    public uint Flags;

    /// <summary>The bound deformable material index; -1 selects the world default.</summary>
    public int MaterialIndex;

    /// <summary>The solver position iteration count; zero selects the scene value.</summary>
    public uint SolverPositionIterations;

    /// <summary>The collision iterations per solver iteration; surfaces only.</summary>
    public uint CollisionIterationMultiplier;

    /// <summary>The steps between two collision pair rebuilds; surfaces only.</summary>
    public uint CollisionPairUpdateFrequency;

    /// <summary>The velocity damping applied to the simulated vertices.</summary>
    public float VertexVelocityDamping;

    /// <summary>The per step displacement clamp; surfaces only, zero disables it.</summary>
    public float MaxDisplacement;

    /// <summary>The rest distance below which self collisions are filtered.</summary>
    public float SelfCollisionFilterDistance;

    /// <summary>The depenetration velocity bound; volumes only.</summary>
    public float MaxDepenetrationVelocity;

    /// <summary>The settling threshold; volumes only.</summary>
    public float SettlingThreshold;

    /// <summary>The sleep threshold; volumes only.</summary>
    public float SleepThreshold;

    /// <summary>The element offset of the simulation point window.</summary>
    public uint PointOffset;

    /// <summary>The element count of the simulation point window.</summary>
    public uint PointCount;

    /// <summary>The element offset of the simulation index window.</summary>
    public uint IndexOffset;

    /// <summary>The element count of the simulation index window.</summary>
    public uint IndexCount;

    /// <summary>The element offset of the collision point window; volumes only.</summary>
    public uint CollisionPointOffset;

    /// <summary>The element count of the collision point window; volumes only.</summary>
    public uint CollisionPointCount;

    /// <summary>The element offset of the collision index window; volumes only.</summary>
    public uint CollisionIndexOffset;

    /// <summary>The element count of the collision index window; volumes only.</summary>
    public uint CollisionIndexCount;

    /// <summary>The world pose the authored rest configuration is placed by.</summary>
    public PhysxTransform WorldPose;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved1;
}
