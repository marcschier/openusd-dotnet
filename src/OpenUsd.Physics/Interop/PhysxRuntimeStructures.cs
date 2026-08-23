// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenUsd.Physics.Interop;

/// <summary>Mirrors <c>openusd_physx_error_buffer</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PhysxErrorBuffer
{
    /// <summary>The caller-owned message buffer.</summary>
    public byte* Data;

    /// <summary>The capacity of <see cref="Data"/>, in bytes.</summary>
    public nuint Capacity;

    /// <summary>The number of bytes the runtime required, including the terminator.</summary>
    public nuint Required;

    /// <summary>Initializes an error buffer over caller-owned memory.</summary>
    public PhysxErrorBuffer(byte* data, nuint capacity)
    {
        Data = data;
        Capacity = capacity;
        Required = 0;
    }
}

/// <summary>Mirrors <c>openusd_physx_command</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxCommand
{
    /// <summary>The identity this command targets; never zero.</summary>
    public ulong TargetId;

    /// <summary>The command type, as a <see cref="PhysxCommandType"/>.</summary>
    public uint Type;

    /// <summary>The command flags, as <see cref="PhysxCommandFlags"/>.</summary>
    public uint Flags;

    /// <summary>The absolute pose for a teleport or kinematic target command.</summary>
    public PhysxTransform Pose;

    /// <summary>The force, impulse, velocity, or gravity vector.</summary>
    public PhysxVec3f Vector;

    /// <summary>The world-space application point.</summary>
    public PhysxVec3f Point;

    /// <summary>An additional scalar parameter.</summary>
    public float Scalar;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved1;
}

/// <summary>Mirrors <c>openusd_physx_body_state</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxBodyState
{
    /// <summary>The actor identity.</summary>
    public ulong Id;

    /// <summary>The world pose.</summary>
    public PhysxTransform Pose;

    /// <summary>The linear velocity.</summary>
    public PhysxVec3f LinearVelocity;

    /// <summary>The angular velocity.</summary>
    public PhysxVec3f AngularVelocity;

    /// <summary>The body state flags, as <see cref="PhysxBodyStateFlags"/>.</summary>
    public uint Flags;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved1;
}

/// <summary>Mirrors <c>openusd_physx_event</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxEventRecord
{
    /// <summary>The primary identity.</summary>
    public ulong Id0;

    /// <summary>The secondary identity, or zero.</summary>
    public ulong Id1;

    /// <summary>The step index the event was produced in.</summary>
    public ulong StepIndex;

    /// <summary>The event type, as a <see cref="PhysxEventType"/>.</summary>
    public uint Type;

    /// <summary>Reserved event flags.</summary>
    public uint Flags;

    /// <summary>The world-space event position.</summary>
    public PhysxVec3f Position;

    /// <summary>The world-space event normal.</summary>
    public PhysxVec3f Normal;

    /// <summary>The contact impulse magnitude.</summary>
    public float Impulse;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved;

    /// <summary>The primary detail identity, or zero.</summary>
    public ulong Detail0;

    /// <summary>The secondary detail identity, or zero.</summary>
    public ulong Detail1;
}

/// <summary>Holds the fixed-size UTF-8 message of an <c>openusd_physx_diagnostic</c>.</summary>
[InlineArray(PhysxAbi.DiagnosticMessageBytes)]
internal struct PhysxDiagnosticMessage
{
    private byte _element0;
}

/// <summary>Mirrors <c>openusd_physx_diagnostic</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxDiagnosticRecord
{
    /// <summary>The identity the diagnostic addresses, or zero.</summary>
    public ulong Id;

    /// <summary>The severity, as a <see cref="PhysxDiagnosticSeverity"/>.</summary>
    public uint Severity;

    /// <summary>The diagnostic code, as a <see cref="PhysxDiagnosticCode"/>.</summary>
    public uint Code;

    /// <summary>The null-padded UTF-8 message.</summary>
    public PhysxDiagnosticMessage Message;
}

/// <summary>Mirrors <c>openusd_physx_debug_line</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxDebugLine
{
    /// <summary>The world-space start point.</summary>
    public PhysxVec3f Start;

    /// <summary>The world-space end point.</summary>
    public PhysxVec3f End;

    /// <summary>The packed RGBA color.</summary>
    public uint Color;

    /// <summary>The debug category.</summary>
    public uint Category;
}

/// <summary>Mirrors <c>openusd_physx_result_header</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxResultHeader
{
    /// <summary>The monotonic result revision.</summary>
    public ulong Revision;

    /// <summary>The number of fixed steps advanced since the last build or reset.</summary>
    public ulong StepIndex;

    /// <summary>The accumulated simulation time, in seconds.</summary>
    public double SimulationTime;

    /// <summary>The wall time the last step took, in seconds.</summary>
    public double LastStepSeconds;

    /// <summary>The accumulated wall time of every step, in seconds.</summary>
    public double TotalStepSeconds;

    /// <summary>The number of valid body state slots.</summary>
    public uint BodyStateCount;

    /// <summary>The number of valid event slots.</summary>
    public uint EventCount;

    /// <summary>The number of valid diagnostic slots.</summary>
    public uint DiagnosticCount;

    /// <summary>The number of valid debug line slots.</summary>
    public uint DebugLineCount;

    /// <summary>The number of events dropped because the declared capacity was exceeded.</summary>
    public uint DroppedEventCount;

    /// <summary>The number of diagnostics dropped because the declared capacity was exceeded.</summary>
    public uint DroppedDiagnosticCount;

    /// <summary>The number of debug lines dropped because the declared capacity was exceeded.</summary>
    public uint DroppedDebugLineCount;

    /// <summary>The overflow flags, as <see cref="PhysxOverflowFlags"/>.</summary>
    public uint OverflowFlags;

    /// <summary>The world state, as a <see cref="PhysxWorldState"/>.</summary>
    public uint State;

    /// <summary>The number of deformation bodies written into the result page.</summary>
    public uint DeformationBodyCount;

    /// <summary>The number of deformation vertices written into the result page.</summary>
    public uint DeformationPointCount;

    /// <summary>The number of deformation bodies dropped whole because they did not fit.</summary>
    public uint DroppedDeformationBodyCount;
}

/// <summary>Mirrors <c>openusd_physx_deformation_state</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxDeformationState
{
    /// <summary>The stable identity of the deformable body.</summary>
    public ulong Id;

    /// <summary>The deformation kind, as a <see cref="PhysxDeformationKind"/>.</summary>
    public uint Kind;

    /// <summary>The deformation flags, as <see cref="PhysxDeformationFlags"/>.</summary>
    public uint Flags;

    /// <summary>The element offset into the deformation point buffer of the same result.</summary>
    public uint PointOffset;

    /// <summary>The number of vertices this body published.</summary>
    public uint PointCount;

    /// <summary>Reserved; always zero.</summary>
    public ulong Reserved0;
}

/// <summary>Mirrors <c>openusd_physx_result_page</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PhysxResultPage
{
    /// <summary>The size of this structure, in bytes.</summary>
    public uint StructSize;

    /// <summary>The exact ABI version of this structure.</summary>
    public uint AbiVersion;

    /// <summary>The result header the runtime fills.</summary>
    public PhysxResultHeader Header;

    /// <summary>The caller-owned body state buffer.</summary>
    public PhysxBodyState* BodyStates;

    /// <summary>The capacity of <see cref="BodyStates"/>.</summary>
    public nuint BodyStateCapacity;

    /// <summary>The caller-owned event buffer.</summary>
    public PhysxEventRecord* Events;

    /// <summary>The capacity of <see cref="Events"/>.</summary>
    public nuint EventCapacity;

    /// <summary>The caller-owned diagnostic buffer.</summary>
    public PhysxDiagnosticRecord* Diagnostics;

    /// <summary>The capacity of <see cref="Diagnostics"/>.</summary>
    public nuint DiagnosticCapacity;

    /// <summary>The caller-owned debug line buffer.</summary>
    public PhysxDebugLine* DebugLines;

    /// <summary>The capacity of <see cref="DebugLines"/>.</summary>
    public nuint DebugLineCapacity;

    /// <summary>The caller-owned deformation body buffer.</summary>
    public PhysxDeformationState* Deformations;

    /// <summary>The capacity of <see cref="Deformations"/>.</summary>
    public nuint DeformationCapacity;

    /// <summary>The caller-owned deformation vertex buffer.</summary>
    public PhysxVec3f* DeformationPoints;

    /// <summary>The capacity of <see cref="DeformationPoints"/>.</summary>
    public nuint DeformationPointCapacity;
}

/// <summary>Mirrors <c>openusd_physx_world_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxWorldDesc
{
    /// <summary>The size of this structure, in bytes.</summary>
    public uint StructSize;

    /// <summary>The exact ABI version of this structure.</summary>
    public uint AbiVersion;

    /// <summary>The requested worker thread count.</summary>
    public uint WorkerThreadCount;

    /// <summary>The world flags, as <see cref="PhysxWorldFlags"/>.</summary>
    public uint Flags;

    /// <summary>Reserved; must be zero.</summary>
    public ulong Reserved0;

    /// <summary>Reserved; must be zero.</summary>
    public ulong Reserved1;
}

/// <summary>Mirrors <c>openusd_physx_step_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PhysxStepDesc
{
    /// <summary>The size of this structure, in bytes.</summary>
    public uint StructSize;

    /// <summary>Reserved step flags; must be zero for ABI version 1.</summary>
    public uint Flags;

    /// <summary>The fixed time step, in seconds; zero uses the rate declared by the build page.</summary>
    public double FixedTimeStep;

    /// <summary>The number of substeps to advance; zero means one.</summary>
    public uint SubstepCount;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved;

    /// <summary>The caller-owned command batch, valid only for the duration of the call.</summary>
    public PhysxCommand* Commands;

    /// <summary>The number of commands in the batch.</summary>
    public nuint CommandCount;
}

/// <summary>Mirrors <c>openusd_physx_reset_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PhysxResetDesc
{
    /// <summary>The size of this structure, in bytes.</summary>
    public uint StructSize;

    /// <summary>Reserved reset flags; must be zero for ABI version 1.</summary>
    public uint Flags;

    /// <summary>The simulation time to restore, in seconds.</summary>
    public double SimulationTime;

    /// <summary>The caller-owned body state overrides, valid only for the duration of the call.</summary>
    public PhysxBodyState* BodyStates;

    /// <summary>The number of body state overrides.</summary>
    public nuint BodyStateCount;
}

/// <summary>Mirrors <c>openusd_physx_query_request</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxQueryRequest
{
    /// <summary>The caller-defined identifier echoed on every hit.</summary>
    public ulong UserId;

    /// <summary>The query type, as a <see cref="PhysxQueryType"/>.</summary>
    public uint Type;

    /// <summary>The query flags, as <see cref="PhysxQueryFlags"/>.</summary>
    public uint Flags;

    /// <summary>The world-space origin.</summary>
    public PhysxVec3f Origin;

    /// <summary>The world-space direction.</summary>
    public PhysxVec3f Direction;

    /// <summary>The maximum distance to travel or search.</summary>
    public float MaxDistance;

    /// <summary>The swept or overlapped shape type, as a <see cref="PhysxShapeType"/>.</summary>
    public uint ShapeType;

    /// <summary>The box half extents of the swept or overlapped shape.</summary>
    public PhysxVec3f HalfExtents;

    /// <summary>The rotation of the swept or overlapped shape.</summary>
    public PhysxQuatf Rotation;

    /// <summary>The radius of the swept or overlapped shape.</summary>
    public float Radius;

    /// <summary>The half height of the swept or overlapped shape.</summary>
    public float HalfHeight;

    /// <summary>The collision group filter mask; zero accepts every group.</summary>
    public uint FilterMask;

    /// <summary>The maximum number of hits to retain for this request.</summary>
    public uint MaxHits;

    /// <summary>The scene index to query.</summary>
    public uint SceneIndex;
}

/// <summary>Mirrors <c>openusd_physx_query_hit</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxQueryHit
{
    /// <summary>The identifier of the request that produced this hit.</summary>
    public ulong UserId;

    /// <summary>The hit actor identity.</summary>
    public ulong ActorId;

    /// <summary>The hit shape identity.</summary>
    public ulong ShapeId;

    /// <summary>The world-space hit position.</summary>
    public PhysxVec3f Position;

    /// <summary>The world-space hit normal.</summary>
    public PhysxVec3f Normal;

    /// <summary>The distance from the request origin.</summary>
    public float Distance;

    /// <summary>The hit face index, where available.</summary>
    public uint FaceIndex;

    /// <summary>The hit flags, as <see cref="PhysxQueryHitFlags"/>.</summary>
    public uint Flags;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved;
}

/// <summary>Mirrors <c>openusd_physx_query_desc</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PhysxQueryDesc
{
    /// <summary>The size of this structure, in bytes.</summary>
    public uint StructSize;

    /// <summary>The exact ABI version of this structure.</summary>
    public uint AbiVersion;

    /// <summary>The caller-owned request batch, valid only for the duration of the call.</summary>
    public PhysxQueryRequest* Requests;

    /// <summary>The number of requests in the batch.</summary>
    public nuint RequestCount;

    /// <summary>The caller-owned hit buffer, valid only for the duration of the call.</summary>
    public PhysxQueryHit* Hits;

    /// <summary>The capacity of <see cref="Hits"/>.</summary>
    public nuint HitCapacity;
}

/// <summary>Mirrors <c>openusd_physx_query_result</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxQueryResultInfo
{
    /// <summary>The size of this structure, in bytes.</summary>
    public uint StructSize;

    /// <summary>The overflow flags, as <see cref="PhysxOverflowFlags"/>.</summary>
    public uint OverflowFlags;

    /// <summary>The number of retained hits.</summary>
    public nuint HitCount;

    /// <summary>The number of hits dropped because the declared capacity was exceeded.</summary>
    public nuint DroppedHitCount;

    /// <summary>The number of requests the runtime rejected.</summary>
    public nuint RejectedRequestCount;
}

/// <summary>Mirrors <c>openusd_physx_abi_info</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxAbiInfo
{
    /// <summary>The size of this structure, in bytes.</summary>
    public uint StructSize;

    /// <summary>The ABI version the runtime implements.</summary>
    public uint AbiVersion;

    /// <summary>The build page magic the runtime expects.</summary>
    public ulong PageMagic;

    /// <summary>The native size of the build page header.</summary>
    public uint BuildPageHeaderSize;

    /// <summary>The native size of a page span.</summary>
    public uint PageSpanSize;

    /// <summary>The native size of the result capacities structure.</summary>
    public uint CapacitiesSize;

    /// <summary>The native size of an identity record.</summary>
    public uint IdentitySize;

    /// <summary>The native size of a scene descriptor.</summary>
    public uint SceneDescSize;

    /// <summary>The native size of a material descriptor.</summary>
    public uint MaterialDescSize;

    /// <summary>The native size of a shape descriptor.</summary>
    public uint ShapeDescSize;

    /// <summary>The native size of an actor descriptor.</summary>
    public uint ActorDescSize;

    /// <summary>The native size of an actor shape reference.</summary>
    public uint ActorShapeRefSize;

    /// <summary>The native size of a joint descriptor.</summary>
    public uint JointDescSize;

    /// <summary>The native size of a suppressed collision pair.</summary>
    public uint FilterPairSize;

    /// <summary>The native size of a command.</summary>
    public uint CommandSize;

    /// <summary>The native size of a body state.</summary>
    public uint BodyStateSize;

    /// <summary>The native size of an event.</summary>
    public uint EventSize;

    /// <summary>The native size of a diagnostic.</summary>
    public uint DiagnosticSize;

    /// <summary>The native size of a debug line.</summary>
    public uint DebugLineSize;

    /// <summary>The native size of a result header.</summary>
    public uint ResultHeaderSize;

    /// <summary>The native size of a query request.</summary>
    public uint QueryRequestSize;

    /// <summary>The native size of a query hit.</summary>
    public uint QueryHitSize;

    /// <summary>The native size of a height field sample.</summary>
    public uint HeightfieldSampleSize;

    /// <summary>The native size of an articulation descriptor.</summary>
    public uint ArticulationDescSize;

    /// <summary>The native size of an articulation link descriptor.</summary>
    public uint ArticulationLinkDescSize;

    /// <summary>The native size of a controller descriptor.</summary>
    public uint ControllerDescSize;

    /// <summary>The native size of an articulation tendon descriptor.</summary>
    public uint TendonDescSize;

    /// <summary>The native size of an articulation tendon node descriptor.</summary>
    public uint TendonNodeDescSize;

    /// <summary>The native size of an articulation mimic joint descriptor.</summary>
    public uint MimicJointDescSize;

    /// <summary>The native size of a vehicle descriptor.</summary>
    public uint VehicleDescSize;

    /// <summary>The native size of a vehicle wheel descriptor.</summary>
    public uint VehicleWheelDescSize;

    /// <summary>The native size of a particle material descriptor.</summary>
    public uint ParticleMaterialDescSize;

    /// <summary>The native size of a particle system descriptor.</summary>
    public uint ParticleSystemDescSize;

    /// <summary>The native size of a particle body descriptor.</summary>
    public uint ParticleBodyDescSize;

    /// <summary>The native size of a deformable material descriptor.</summary>
    public uint DeformableMaterialDescSize;

    /// <summary>The native size of a deformable descriptor.</summary>
    public uint DeformableDescSize;

    /// <summary>The native size of a deformation state record.</summary>
    public uint DeformationStateSize;

    /// <summary>The page alignment the runtime requires.</summary>
    public uint PageAlignment;

    /// <summary>Reserved.</summary>
    public uint Reserved;
}

/// <summary>Mirrors <c>openusd_physx_capabilities</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxCapabilitiesInfo
{
    /// <summary>The size of this structure, in bytes.</summary>
    public uint StructSize;

    /// <summary>The ABI version the runtime implements.</summary>
    public uint AbiVersion;

    /// <summary>The supported capabilities, as <see cref="PhysxCapabilityFlags"/>.</summary>
    public uint Flags;

    /// <summary>The PhysX major version.</summary>
    public uint PhysxVersionMajor;

    /// <summary>The PhysX minor version.</summary>
    public uint PhysxVersionMinor;

    /// <summary>The PhysX bugfix version.</summary>
    public uint PhysxVersionBugfix;

    /// <summary>The maximum number of scenes.</summary>
    public uint MaxScenes;

    /// <summary>The number of collision groups.</summary>
    public uint MaxCollisionGroups;

    /// <summary>The slowest supported simulation rate, in hertz.</summary>
    public uint MinSimulationRateHz;

    /// <summary>The fastest supported simulation rate, in hertz.</summary>
    public uint MaxSimulationRateHz;

    /// <summary>The maximum number of substeps per step call.</summary>
    public uint MaxSubsteps;

    /// <summary>The maximum declarable result capacity.</summary>
    public uint MaxResultCapacity;
}

/// <summary>Mirrors <c>openusd_physx_page_validation</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxPageValidation
{
    /// <summary>The size of this structure, in bytes.</summary>
    public uint StructSize;

    /// <summary>The failure code, as a <see cref="PhysxPageError"/>.</summary>
    public uint ErrorCode;

    /// <summary>The failing section, as a <see cref="PhysxPageSection"/>.</summary>
    public uint Section;

    /// <summary>The failing element index.</summary>
    public uint ElementIndex;

    /// <summary>The failing byte offset.</summary>
    public ulong ByteOffset;

    /// <summary>The page revision.</summary>
    public ulong Revision;

    /// <summary>The page source hash.</summary>
    public ulong SourceHash;

    /// <summary>The number of identities.</summary>
    public uint IdentityCount;

    /// <summary>The number of scenes.</summary>
    public uint SceneCount;

    /// <summary>The number of materials.</summary>
    public uint MaterialCount;

    /// <summary>The number of shapes.</summary>
    public uint ShapeCount;

    /// <summary>The number of actors.</summary>
    public uint ActorCount;

    /// <summary>The number of movable actors.</summary>
    public uint DynamicActorCount;

    /// <summary>The number of joints.</summary>
    public uint JointCount;

    /// <summary>The number of suppressed collision pairs.</summary>
    public uint FilterPairCount;

    /// <summary>The declared result capacities.</summary>
    public PhysxResultCapacities Capacities;
}

/// <summary>Mirrors <c>openusd_physx_world_status_info</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PhysxWorldStatusInfo
{
    /// <summary>The size of this structure, in bytes.</summary>
    public uint StructSize;

    /// <summary>The world state, as a <see cref="PhysxWorldState"/>.</summary>
    public uint State;

    /// <summary>The revision of the built page.</summary>
    public ulong Revision;

    /// <summary>The number of fixed steps advanced since the last build or reset.</summary>
    public ulong StepIndex;

    /// <summary>The accumulated simulation time, in seconds.</summary>
    public double SimulationTime;

    /// <summary>The number of actors.</summary>
    public uint ActorCount;

    /// <summary>The number of movable actors.</summary>
    public uint DynamicActorCount;

    /// <summary>The number of joints.</summary>
    public uint JointCount;

    /// <summary>The number of scenes.</summary>
    public uint SceneCount;

    /// <summary>The number of articulations.</summary>
    public uint ArticulationCount;

    /// <summary>The number of articulation links.</summary>
    public uint ArticulationLinkCount;

    /// <summary>The number of controllers.</summary>
    public uint ControllerCount;

    /// <summary>The number of articulation tendons.</summary>
    public uint TendonCount;

    /// <summary>The number of articulation mimic joints.</summary>
    public uint MimicJointCount;

    /// <summary>The number of vehicles.</summary>
    public uint VehicleCount;

    /// <summary>The number of vehicle wheels.</summary>
    public uint VehicleWheelCount;

    /// <summary>The number of particle systems that were actually created on a device.</summary>
    public uint ParticleSystemCount;

    /// <summary>The number of particle bodies that were actually created on a device.</summary>
    public uint ParticleBodyCount;

    /// <summary>The number of surface deformables that were actually created on a device.</summary>
    public uint DeformableSurfaceCount;

    /// <summary>The number of volume deformables that were actually created on a device.</summary>
    public uint DeformableVolumeCount;

    /// <summary>The number of deformation bodies this world publishes each step.</summary>
    public uint DeformationBodyCount;

    /// <summary>The number of deformation vertices this world publishes each step.</summary>
    public uint DeformationPointCount;

    /// <summary>Reserved; must be zero.</summary>
    public uint Reserved0;

    /// <summary>The declared result capacities.</summary>
    public PhysxResultCapacities Capacities;
}
