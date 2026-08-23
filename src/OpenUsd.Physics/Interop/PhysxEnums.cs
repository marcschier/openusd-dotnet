// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Interop;

/// <summary>Mirrors <c>openusd_physx_status</c>.</summary>
internal enum PhysxStatus
{
    /// <summary>The call succeeded.</summary>
    Ok = 0,

    /// <summary>An argument was null, out of range, or inconsistent.</summary>
    InvalidArgument = 1,

    /// <summary>A caller-owned buffer was smaller than the required size.</summary>
    BufferTooSmall = 2,

    /// <summary>The native runtime or the simulation SDK reported a failure.</summary>
    NativeError = 3,

    /// <summary>An ABI version or a structure size did not match exactly.</summary>
    VersionMismatch = 4,

    /// <summary>A build page failed validation.</summary>
    InvalidPage = 5,

    /// <summary>The world was not in a state that allows the requested operation.</summary>
    InvalidState = 6,

    /// <summary>The requested feature is not supported by this runtime.</summary>
    Unsupported = 7,

    /// <summary>A declared capacity was too small for the requested operation.</summary>
    CapacityExceeded = 8
}

/// <summary>Mirrors <c>openusd_physx_up_axis</c>.</summary>
internal enum PhysxUpAxis : uint
{
    /// <summary>The stage up axis is X.</summary>
    X = 0,

    /// <summary>The stage up axis is Y.</summary>
    Y = 1,

    /// <summary>The stage up axis is Z.</summary>
    Z = 2
}

/// <summary>Mirrors <c>openusd_physx_axis</c>.</summary>
internal enum PhysxAxis : uint
{
    /// <summary>The X axis.</summary>
    X = 0,

    /// <summary>The Y axis.</summary>
    Y = 1,

    /// <summary>The Z axis.</summary>
    Z = 2
}

/// <summary>Mirrors <c>openusd_physx_instance_domain</c>.</summary>
internal enum PhysxInstanceDomain : uint
{
    /// <summary>A plain composed prim.</summary>
    Prim = 0,

    /// <summary>A prim reached through a native instance proxy.</summary>
    NativeInstance = 1,

    /// <summary>One instance of a <c>PointInstancer</c>.</summary>
    PointInstancer = 2,

    /// <summary>The number of defined instance domains.</summary>
    Count = 3
}

/// <summary>Mirrors <c>openusd_physx_actor_type</c>.</summary>
internal enum PhysxActorType : uint
{
    /// <summary>A non-simulated static body.</summary>
    Static = 0,

    /// <summary>A simulated dynamic rigid body.</summary>
    Dynamic = 1,

    /// <summary>An animated kinematic rigid body.</summary>
    Kinematic = 2,

    /// <summary>The number of defined actor types.</summary>
    Count = 3
}

/// <summary>Mirrors <c>openusd_physx_scene_flags</c>.</summary>
[Flags]
internal enum PhysxSceneFlags : uint
{
    /// <summary>No optional scene behavior.</summary>
    None = 0u,

    /// <summary>Enable continuous collision detection for the scene.</summary>
    EnableCcd = 1u << 0,

    /// <summary>Enable the enhanced determinism solver mode.</summary>
    EnableEnhancedDeterminism = 1u << 1,

    /// <summary>Prevent bodies in the scene from sleeping.</summary>
    DisableSleeping = 1u << 2,

    /// <summary>Every defined scene flag.</summary>
    All = 0x7u
}

/// <summary>Mirrors <c>openusd_physx_material_flags</c>.</summary>
[Flags]
internal enum PhysxMaterialFlags : uint
{
    /// <summary>No optional material behavior.</summary>
    None = 0u,

    /// <summary>Disable friction for the material.</summary>
    DisableFriction = 1u << 0,

    /// <summary>Disable the strong friction model for the material.</summary>
    DisableStrongFriction = 1u << 1,

    /// <summary>The material resolves contacts with a compliant spring instead of a hard impulse.</summary>
    CompliantContact = 1u << 2,

    /// <summary>Every defined material flag.</summary>
    All = 0x7u
}

/// <summary>Mirrors <c>openusd_physx_combine_mode</c>.</summary>
internal enum PhysxCombineMode : uint
{
    /// <summary>Average the two coefficients.</summary>
    Average = 0,

    /// <summary>Take the smaller of the two coefficients.</summary>
    Min = 1,

    /// <summary>Multiply the two coefficients.</summary>
    Multiply = 2,

    /// <summary>Take the larger of the two coefficients.</summary>
    Max = 3,

    /// <summary>The number of defined combine modes.</summary>
    Count = 4
}

/// <summary>Mirrors <c>openusd_physx_actor_flags</c>.</summary>
[Flags]
internal enum PhysxActorFlags : uint
{
    /// <summary>No optional actor behavior.</summary>
    None = 0u,

    /// <summary>The actor ignores scene gravity.</summary>
    DisableGravity = 1u << 0,

    /// <summary>The actor uses continuous collision detection.</summary>
    EnableCcd = 1u << 1,

    /// <summary>The actor starts asleep.</summary>
    StartAsleep = 1u << 2,

    /// <summary>The actor's rotation is locked on every axis.</summary>
    LockRotation = 1u << 3,

    /// <summary>The actor uses speculative (contact based) continuous collision detection.</summary>
    EnableSpeculativeCcd = 1u << 4,

    /// <summary>The actor keeps the accelerations it accumulated across a step.</summary>
    RetainAccelerations = 1u << 5,

    /// <summary>The actor cannot translate along the world x axis.</summary>
    LockLinearX = 1u << 6,

    /// <summary>The actor cannot translate along the world y axis.</summary>
    LockLinearY = 1u << 7,

    /// <summary>The actor cannot translate along the world z axis.</summary>
    LockLinearZ = 1u << 8,

    /// <summary>The actor cannot rotate about the world x axis.</summary>
    LockAngularX = 1u << 9,

    /// <summary>The actor cannot rotate about the world y axis.</summary>
    LockAngularY = 1u << 10,

    /// <summary>The actor cannot rotate about the world z axis.</summary>
    LockAngularZ = 1u << 11,

    /// <summary>The actor integrates gyroscopic forces.</summary>
    EnableGyroscopicForces = 1u << 12,

    /// <summary>The actor never goes to sleep.</summary>
    DisableSleeping = 1u << 13,

    /// <summary>Every defined actor flag.</summary>
    All = 0x3FFFu
}

/// <summary>Mirrors <c>openusd_physx_shape_type</c>.</summary>
internal enum PhysxShapeType : uint
{
    /// <summary>An analytic sphere.</summary>
    Sphere = 0,

    /// <summary>An analytic box.</summary>
    Box = 1,

    /// <summary>An analytic capsule.</summary>
    Capsule = 2,

    /// <summary>An infinite half space.</summary>
    Plane = 3,

    /// <summary>A cooked convex mesh.</summary>
    ConvexMesh = 4,

    /// <summary>A cooked triangle mesh.</summary>
    TriangleMesh = 5,

    /// <summary>An analytic cylinder about its local x axis.</summary>
    Cylinder = 6,

    /// <summary>An analytic cone about its local x axis.</summary>
    Cone = 7,

    /// <summary>A row major height field.</summary>
    Heightfield = 8,

    /// <summary>The number of defined shape types.</summary>
    Count = 9
}

/// <summary>Mirrors <c>openusd_physx_shape_flags</c>.</summary>
[Flags]
internal enum PhysxShapeFlags : uint
{
    /// <summary>No optional shape behavior.</summary>
    None = 0u,

    /// <summary>The shape reports trigger events instead of colliding.</summary>
    Trigger = 1u << 0,

    /// <summary>The shape never participates in collision.</summary>
    DisableCollision = 1u << 1,

    /// <summary>The shape is double sided.</summary>
    DoubleSided = 1u << 2,

    /// <summary>Every defined shape flag.</summary>
    All = 0x7u
}

/// <summary>Mirrors <c>openusd_physx_joint_type</c>.</summary>
internal enum PhysxJointType : uint
{
    /// <summary>A fixed joint.</summary>
    Fixed = 0,

    /// <summary>A revolute (hinge) joint.</summary>
    Revolute = 1,

    /// <summary>A prismatic (slider) joint.</summary>
    Prismatic = 2,

    /// <summary>A spherical (ball) joint.</summary>
    Spherical = 3,

    /// <summary>A distance joint.</summary>
    Distance = 4,

    /// <summary>A six degree of freedom joint.</summary>
    D6 = 5,

    /// <summary>The number of defined joint types.</summary>
    Count = 6
}

/// <summary>Mirrors <c>openusd_physx_joint_flags</c>.</summary>
[Flags]
internal enum PhysxJointFlags : uint
{
    /// <summary>No optional joint behavior.</summary>
    None = 0u,

    /// <summary>The joint is authored but disabled.</summary>
    Disabled = 1u << 0,

    /// <summary>Collision between the jointed bodies stays enabled.</summary>
    CollisionEnabled = 1u << 1,

    /// <summary>The joint limit is active.</summary>
    LimitEnabled = 1u << 2,

    /// <summary>The joint drive is active.</summary>
    DriveEnabled = 1u << 3,

    /// <summary>The drive is stated as an acceleration instead of a force.</summary>
    DriveAcceleration = 1u << 4,

    /// <summary>The drive limits are forces rather than impulses.</summary>
    DriveLimitsAreForces = 1u << 5,

    /// <summary>The limit is a spring instead of a hard stop.</summary>
    LimitSoft = 1u << 6,

    /// <summary>The joint is kept out of any articulation reduction.</summary>
    ExcludeFromArticulation = 1u << 7,

    /// <summary>The joint reports its constraint forces.</summary>
    ReportForces = 1u << 8,

    /// <summary>Every defined joint flag.</summary>
    All = 0x1FFu
}

/// <summary>Mirrors <c>openusd_physx_joint_axis</c>.</summary>
internal enum PhysxJointAxis : uint
{
    /// <summary>Translation along the joint frame x axis.</summary>
    X = 0,

    /// <summary>Translation along the joint frame y axis.</summary>
    Y = 1,

    /// <summary>Translation along the joint frame z axis.</summary>
    Z = 2,

    /// <summary>Rotation about the joint frame x axis.</summary>
    Twist = 3,

    /// <summary>Rotation about the joint frame y axis.</summary>
    Swing1 = 4,

    /// <summary>Rotation about the joint frame z axis.</summary>
    Swing2 = 5,

    /// <summary>The number of joint axes.</summary>
    Count = 6
}

/// <summary>Mirrors <c>openusd_physx_joint_motion</c>.</summary>
internal enum PhysxJointMotion : uint
{
    /// <summary>The axis is fully constrained.</summary>
    Locked = 0,

    /// <summary>The axis moves between its authored limits.</summary>
    Limited = 1,

    /// <summary>The axis is unconstrained.</summary>
    Free = 2,

    /// <summary>The number of defined motions.</summary>
    Count = 3
}

/// <summary>Mirrors <c>openusd_physx_joint_drive_flags</c>.</summary>
[Flags]
internal enum PhysxJointDriveFlags : uint
{
    /// <summary>The axis has no drive.</summary>
    None = 0u,

    /// <summary>The axis drive is active.</summary>
    Enabled = 1u << 0,

    /// <summary>The axis drive is stated as an acceleration instead of a force.</summary>
    Acceleration = 1u << 1,

    /// <summary>Every defined axis drive flag.</summary>
    All = 0x3u
}

/// <summary>Mirrors <c>openusd_physx_command_type</c>.</summary>
internal enum PhysxCommandType : uint
{
    /// <summary>Set the next kinematic target pose.</summary>
    KinematicTarget = 0,

    /// <summary>Teleport an actor to an absolute pose.</summary>
    Teleport = 1,

    /// <summary>Set the linear velocity of a dynamic actor.</summary>
    SetLinearVelocity = 2,

    /// <summary>Set the angular velocity of a dynamic actor.</summary>
    SetAngularVelocity = 3,

    /// <summary>Add a force at the center of mass.</summary>
    AddForce = 4,

    /// <summary>Add a torque.</summary>
    AddTorque = 5,

    /// <summary>Add an impulse at the center of mass.</summary>
    AddImpulse = 6,

    /// <summary>Add a force at an explicit world-space point.</summary>
    AddForceAtPoint = 7,

    /// <summary>Wake a sleeping actor.</summary>
    Wake = 8,

    /// <summary>Put an actor to sleep.</summary>
    Sleep = 9,

    /// <summary>Replace the gravity vector of one scene.</summary>
    SetSceneGravity = 10,

    /// <summary>Add an impulse at an explicit application point.</summary>
    AddImpulseAtPoint = 11,

    /// <summary>Add an angular impulse.</summary>
    AddAngularImpulse = 12,

    /// <summary>Clear the pending force and impulse accumulators.</summary>
    ClearForce = 13,

    /// <summary>Clear the pending torque and angular impulse accumulators.</summary>
    ClearTorque = 14,

    /// <summary>Move a character controller by a displacement vector.</summary>
    MoveController = 15,

    /// <summary>Set the driver input of a vehicle.</summary>
    VehicleInput = 16,

    /// <summary>The number of defined command types.</summary>
    Count = 17
}

/// <summary>Mirrors <c>openusd_physx_command_flags</c>.</summary>
[Flags]
internal enum PhysxCommandFlags : uint
{
    /// <summary>No optional command behavior.</summary>
    None = 0u,

    /// <summary>The vector is a direction and the scalar carries the magnitude.</summary>
    Magnitude = 1u << 0,

    /// <summary>The application point is expressed in actor local space.</summary>
    PointLocal = 1u << 1,

    /// <summary>The application point is the center of mass.</summary>
    PointCenterOfMass = 1u << 2,

    /// <summary>Interpret the value as an acceleration instead of a force.</summary>
    ModeAcceleration = 1u << 3,

    /// <summary>Interpret the value as a direct velocity change.</summary>
    ModeVelocityChange = 1u << 4,

    /// <summary>Do not wake a sleeping actor.</summary>
    NoWake = 1u << 5,

    /// <summary>Every defined command flag.</summary>
    All = 0x3Fu
}

/// <summary>Mirrors <c>openusd_physx_event_type</c>.</summary>
internal enum PhysxEventType : uint
{
    /// <summary>An actor fell asleep.</summary>
    Sleep = 0,

    /// <summary>An actor woke up.</summary>
    Wake = 1,

    /// <summary>A joint exceeded its break force or torque.</summary>
    JointBreak = 2,

    /// <summary>Two shapes began touching.</summary>
    ContactFound = 3,

    /// <summary>Two shapes stopped touching.</summary>
    ContactLost = 4,

    /// <summary>An actor entered a trigger volume.</summary>
    TriggerEnter = 5,

    /// <summary>An actor left a trigger volume.</summary>
    TriggerLeave = 6,

    /// <summary>A character controller touched a shape.</summary>
    ControllerHit = 7,

    /// <summary>A vehicle gearbox changed gear.</summary>
    VehicleGearChange = 8,

    /// <summary>The number of defined event types.</summary>
    Count = 9
}

/// <summary>Mirrors <c>openusd_physx_event_flags</c>.</summary>
[Flags]
internal enum PhysxEventFlags : uint
{
    /// <summary>The event carries no optional geometry.</summary>
    None = 0u,

    /// <summary>The position field holds a world-space point.</summary>
    HasPosition = 1u << 0,

    /// <summary>The normal field holds a unit world-space normal.</summary>
    HasNormal = 1u << 1,

    /// <summary>The impulse field holds a non-zero magnitude.</summary>
    HasImpulse = 1u << 2,

    /// <summary>The detail identities name shapes rather than actors.</summary>
    DetailIsShape = 1u << 3,

    /// <summary>Every defined event flag.</summary>
    All = 0xFu
}

/// <summary>Mirrors <c>openusd_physx_diagnostic_severity</c>.</summary>
internal enum PhysxDiagnosticSeverity : uint
{
    /// <summary>Informational only.</summary>
    Info = 0,

    /// <summary>A recoverable condition that degraded one object.</summary>
    Warning = 1,

    /// <summary>An operation failed.</summary>
    Error = 2,

    /// <summary>The number of defined severities.</summary>
    Count = 3
}

/// <summary>Mirrors <c>openusd_physx_diagnostic_code</c>.</summary>
internal enum PhysxDiagnosticCode : uint
{
    /// <summary>No diagnostic.</summary>
    None = 0,

    /// <summary>A collider used an unsupported shape.</summary>
    UnsupportedShape = 1,

    /// <summary>Mesh cooking failed.</summary>
    CookingFailed = 2,

    /// <summary>An actor could not be created.</summary>
    ActorCreateFailed = 3,

    /// <summary>A joint could not be created.</summary>
    JointCreateFailed = 4,

    /// <summary>A command targeted an identity this world does not contain.</summary>
    CommandTargetMissing = 5,

    /// <summary>A command was rejected.</summary>
    CommandRejected = 6,

    /// <summary>A result buffer overflowed its declared capacity.</summary>
    ResultOverflow = 7,

    /// <summary>A scene query request was rejected.</summary>
    QueryRejected = 8,

    /// <summary>No CUDA device could be reached, so every GPU object was skipped.</summary>
    GpuUnavailable = 9,

    /// <summary>One named GPU object could not be created and was skipped on its own.</summary>
    GpuObjectSkipped = 10,

    /// <summary>The number of defined diagnostic codes.</summary>
    Count = 11
}

/// <summary>Mirrors <c>openusd_physx_overflow_flags</c>.</summary>
[Flags]
internal enum PhysxOverflowFlags : uint
{
    /// <summary>No buffer overflowed.</summary>
    None = 0u,

    /// <summary>The body state buffer overflowed.</summary>
    BodyStates = 1u << 0,

    /// <summary>The event buffer overflowed.</summary>
    Events = 1u << 1,

    /// <summary>The diagnostic buffer overflowed.</summary>
    Diagnostics = 1u << 2,

    /// <summary>The debug line buffer overflowed.</summary>
    DebugLines = 1u << 3,

    /// <summary>The query hit buffer overflowed.</summary>
    QueryHits = 1u << 4,

    /// <summary>
    /// The simulation SDK exhausted its own touch buffer while gathering hits, so the dropped hit
    /// count is a lower bound rather than an exact count.
    /// </summary>
    QueryTruncated = 1u << 5,

    /// <summary>
    /// A deformation body did not fit the declared deformation capacity. Every body that was
    /// reported is complete: a body that did not fit is dropped whole and counted.
    /// </summary>
    Deformation = 1u << 6
}

/// <summary>Mirrors <c>openusd_physx_world_state</c>.</summary>
internal enum PhysxWorldState : uint
{
    /// <summary>The world holds no built page.</summary>
    Empty = 0,

    /// <summary>The world holds a successfully built page.</summary>
    Ready = 1,

    /// <summary>The world failed and must be rebuilt.</summary>
    Faulted = 2,

    /// <summary>The number of defined world states.</summary>
    Count = 3
}

/// <summary>Mirrors <c>openusd_physx_world_flags</c>.</summary>
[Flags]
internal enum PhysxWorldFlags : uint
{
    /// <summary>No optional world behavior.</summary>
    None = 0u,

    /// <summary>Collect simulation events.</summary>
    EnableEvents = 1u << 0,

    /// <summary>Collect debug visualization lines.</summary>
    EnableDebug = 1u << 1,

    /// <summary>Enable continuous collision detection.</summary>
    EnableCcd = 1u << 2,

    /// <summary>Request the deterministic solver configuration.</summary>
    Deterministic = 1u << 3,

    /// <summary>Every defined world flag.</summary>
    All = 0xFu
}

/// <summary>Mirrors <c>openusd_physx_body_state_flags</c>.</summary>
[Flags]
internal enum PhysxBodyStateFlags : uint
{
    /// <summary>The body is awake and dynamic.</summary>
    None = 0u,

    /// <summary>The body is asleep.</summary>
    Sleeping = 1u << 0,

    /// <summary>The body is kinematic.</summary>
    Kinematic = 1u << 1,

    /// <summary>The body belongs to a reduced coordinate articulation link.</summary>
    ArticulationLink = 1u << 2,

    /// <summary>The body belongs to a character controller.</summary>
    Controller = 1u << 3,

    /// <summary>The body is a vehicle wheel published by its vehicle.</summary>
    VehicleWheel = 1u << 4,

    /// <summary>Every defined body state flag.</summary>
    All = 0x1Fu
}

/// <summary>Mirrors <c>openusd_physx_capability_flags</c>.</summary>
[Flags]
internal enum PhysxCapabilityFlags : uint
{
    /// <summary>No capability is available.</summary>
    None = 0u,

    /// <summary>CPU rigid body simulation.</summary>
    CpuRigidBodies = 1u << 0,

    /// <summary>Convex and triangle mesh cooking.</summary>
    MeshCooking = 1u << 1,

    /// <summary>Joints, limits, and drives.</summary>
    Joints = 1u << 2,

    /// <summary>Batched scene queries.</summary>
    SceneQueries = 1u << 3,

    /// <summary>Sleep and wake events.</summary>
    SleepEvents = 1u << 4,

    /// <summary>Joint break events.</summary>
    JointBreakEvents = 1u << 5,

    /// <summary>Contact and trigger events.</summary>
    ContactEvents = 1u << 6,

    /// <summary>Debug line visualization.</summary>
    DebugLines = 1u << 7,

    /// <summary>Optional CUDA-backed domains.</summary>
    GpuDomains = 1u << 8,

    /// <summary>Trigger enter and leave events.</summary>
    TriggerEvents = 1u << 9,

    /// <summary>Character controller hit events.</summary>
    ControllerHitEvents = 1u << 10,

    /// <summary>Batched raycast, sweep, and overlap queries.</summary>
    BatchedQueries = 1u << 11,

    /// <summary>Analytic cylinder and cone collision geometry.</summary>
    ConvexCoreShapes = 1u << 12,

    /// <summary>Height field collision geometry.</summary>
    HeightfieldShapes = 1u << 13,

    /// <summary>Per axis six degree of freedom joint motions, limits, and drives.</summary>
    D6JointDrives = 1u << 14,

    /// <summary>Per shape contact, rest, and torsional patch offsets.</summary>
    ShapeOffsets = 1u << 15,

    /// <summary>Per body solver iterations, velocity budgets, and axis locks.</summary>
    RigidBodyTuning = 1u << 16,

    /// <summary>Reduced coordinate articulations with per link joints, limits, drives, and armature.</summary>
    Articulations = 1u << 17,

    /// <summary>Capsule and box character controllers with move commands and hit reporting.</summary>
    CharacterControllers = 1u << 18,

    /// <summary>Fixed and spatial articulation tendons.</summary>
    ArticulationTendons = 1u << 19,

    /// <summary>Articulation mimic joints coupling two joint axes through a gear ratio.</summary>
    ArticulationMimicJoints = 1u << 20,

    /// <summary>Engine driven vehicles with suspension, tires, gearbox, brakes, and steering.</summary>
    Vehicles = 1u << 21,

    /// <summary>
    /// A CUDA context manager was created and reported a usable device. It is never reported
    /// because the runtime was compiled with GPU support.
    /// </summary>
    CudaContext = 1u << 22,

    /// <summary>Position based dynamics particle systems with solid and fluid particle bodies.</summary>
    ParticleSystems = 1u << 23,

    /// <summary>Surface deformables, which is what a simulated cloth is built from.</summary>
    SurfaceDeformables = 1u << 24,

    /// <summary>Finite element volume deformables.</summary>
    VolumeDeformables = 1u << 25,

    /// <summary>Per vertex deformation results published for every built GPU domain.</summary>
    DeformationResults = 1u << 26
}

/// <summary>Mirrors <c>openusd_physx_query_type</c>.</summary>
internal enum PhysxQueryType : uint
{
    /// <summary>Cast a ray.</summary>
    Raycast = 0,

    /// <summary>Sweep a shape.</summary>
    Sweep = 1,

    /// <summary>Report overlapping shapes.</summary>
    Overlap = 2,

    /// <summary>The number of defined query types.</summary>
    Count = 3
}

/// <summary>Mirrors <c>openusd_physx_query_flags</c>.</summary>
[Flags]
internal enum PhysxQueryFlags : uint
{
    /// <summary>No optional query behavior.</summary>
    None = 0u,

    /// <summary>Stop at the first hit.</summary>
    AnyHit = 1u << 0,

    /// <summary>Ignore static actors.</summary>
    ExcludeStatic = 1u << 1,

    /// <summary>Ignore movable actors.</summary>
    ExcludeDynamic = 1u << 2,

    /// <summary>Ignore shapes flagged as triggers.</summary>
    ExcludeTriggers = 1u << 3,

    /// <summary>Report sweeps that start already overlapping.</summary>
    SweepInitialOverlap = 1u << 4,

    /// <summary>Every defined query flag.</summary>
    All = 0x1Fu
}

/// <summary>Mirrors <c>openusd_physx_query_hit_flags</c>.</summary>
[Flags]
internal enum PhysxQueryHitFlags : uint
{
    /// <summary>The hit carries no optional geometry.</summary>
    None = 0u,

    /// <summary>The position field holds a world-space point.</summary>
    HasPosition = 1u << 0,

    /// <summary>The normal field holds a unit world-space normal.</summary>
    HasNormal = 1u << 1,

    /// <summary>The distance field is meaningful.</summary>
    HasDistance = 1u << 2,

    /// <summary>The face index names an element of the hit geometry.</summary>
    HasFace = 1u << 3,

    /// <summary>The sweep started already overlapping the hit shape.</summary>
    InitialOverlap = 1u << 4,

    /// <summary>The hit shape is a trigger.</summary>
    Trigger = 1u << 5,

    /// <summary>Every defined query hit flag.</summary>
    All = 0x3Fu
}

/// <summary>Mirrors <c>openusd_physx_page_section</c>.</summary>
internal enum PhysxPageSection : uint
{
    /// <summary>The fixed page header.</summary>
    Header = 0,

    /// <summary>The UTF-8 string byte section.</summary>
    Strings = 1,

    /// <summary>The identity table.</summary>
    Identities = 2,

    /// <summary>The scene section.</summary>
    Scenes = 3,

    /// <summary>The material section.</summary>
    Materials = 4,

    /// <summary>The shape section.</summary>
    Shapes = 5,

    /// <summary>The actor section.</summary>
    Actors = 6,

    /// <summary>The actor-to-shape reference section.</summary>
    ActorShapes = 7,

    /// <summary>The joint section.</summary>
    Joints = 8,

    /// <summary>The suppressed collision pair section.</summary>
    FilterPairs = 9,

    /// <summary>The mesh point section.</summary>
    MeshPoints = 10,

    /// <summary>The mesh index section.</summary>
    MeshIndices = 11,

    /// <summary>The declared result capacities.</summary>
    Capacities = 12,

    /// <summary>The height field sample section.</summary>
    HeightfieldSamples = 13,

    /// <summary>The articulation section.</summary>
    Articulations = 14,

    /// <summary>The articulation link section.</summary>
    ArticulationLinks = 15,

    /// <summary>The controller section.</summary>
    Controllers = 16,

    /// <summary>The articulation tendon section.</summary>
    ArticulationTendons = 17,

    /// <summary>The articulation tendon node section.</summary>
    ArticulationTendonNodes = 18,

    /// <summary>The articulation mimic joint section.</summary>
    ArticulationMimicJoints = 19,

    /// <summary>The vehicle section.</summary>
    Vehicles = 20,

    /// <summary>The vehicle wheel section.</summary>
    VehicleWheels = 21,

    /// <summary>The position based dynamics particle material section.</summary>
    ParticleMaterials = 22,

    /// <summary>The particle system section.</summary>
    ParticleSystems = 23,

    /// <summary>The particle body section.</summary>
    ParticleBodies = 24,

    /// <summary>The surface and volume deformable material section.</summary>
    DeformableMaterials = 25,

    /// <summary>The surface and volume deformable section.</summary>
    Deformables = 26,

    /// <summary>The number of defined sections.</summary>
    Count = 27
}

/// <summary>Mirrors <c>openusd_physx_page_error</c>.</summary>
internal enum PhysxPageError : uint
{
    /// <summary>The page is valid.</summary>
    None = 0,

    /// <summary>The page buffer was null.</summary>
    Null = 1,

    /// <summary>The page size is out of range or disagrees with the header.</summary>
    Size = 2,

    /// <summary>The page magic is wrong.</summary>
    Magic = 3,

    /// <summary>The page declares a different ABI version.</summary>
    Abi = 4,

    /// <summary>The declared header size does not match this ABI.</summary>
    HeaderSize = 5,

    /// <summary>The page or a section offset is not eight byte aligned.</summary>
    Alignment = 6,

    /// <summary>A section or record range falls outside the page.</summary>
    Range = 7,

    /// <summary>Two sections overlap.</summary>
    Overlap = 8,

    /// <summary>A section exceeds its supported element count.</summary>
    CountLimit = 9,

    /// <summary>A field holds a value outside its supported range.</summary>
    Value = 10,

    /// <summary>A record references an element that does not exist.</summary>
    Reference = 11,

    /// <summary>Two identities collide.</summary>
    DuplicateId = 12,

    /// <summary>The string section is not valid UTF-8 without embedded null bytes.</summary>
    Encoding = 13,

    /// <summary>A declared result capacity is unsupported or too small.</summary>
    Capacity = 14,

    /// <summary>The number of defined page errors.</summary>
    Count = 15
}

/// <summary>Mirrors <c>openusd_physx_articulation_flags</c>.</summary>
[Flags]
internal enum PhysxArticulationFlags : uint
{
    /// <summary>No optional articulation behavior.</summary>
    None = 0u,

    /// <summary>The root link is welded to the world instead of being free to move.</summary>
    FixedBase = 1u << 0,

    /// <summary>Links of this articulation collide with each other.</summary>
    SelfCollision = 1u << 1,

    /// <summary>The articulation never goes to sleep.</summary>
    DisableSleeping = 1u << 2,

    /// <summary>Integrate the gyroscopic term on every link.</summary>
    EnableGyroscopicForces = 1u << 3,

    /// <summary>Every defined articulation flag.</summary>
    All = 0xFu
}

/// <summary>Mirrors <c>openusd_physx_articulation_joint_type</c>.</summary>
internal enum PhysxArticulationJointType : uint
{
    /// <summary>No inbound joint (root link).</summary>
    None = 0,

    /// <summary>A fixed joint.</summary>
    Fixed = 1,

    /// <summary>A revolute (hinge) joint.</summary>
    Revolute = 2,

    /// <summary>A prismatic (slider) joint.</summary>
    Prismatic = 3,

    /// <summary>A spherical (ball) joint.</summary>
    Spherical = 4,

    /// <summary>The number of defined articulation joint types.</summary>
    Count = 5
}

/// <summary>Mirrors <c>openusd_physx_articulation_link_flags</c>.</summary>
[Flags]
internal enum PhysxArticulationLinkFlags : uint
{
    /// <summary>No optional link behavior.</summary>
    None = 0u,

    /// <summary>The link ignores scene gravity.</summary>
    DisableGravity = 1u << 0,

    /// <summary>The joint drives are read as accelerations rather than forces.</summary>
    DriveAcceleration = 1u << 1,

    /// <summary>Every defined articulation link flag.</summary>
    All = 0x3u
}

/// <summary>Mirrors <c>openusd_physx_controller_shape</c>.</summary>
internal enum PhysxControllerShape : uint
{
    /// <summary>A capsule controller.</summary>
    Capsule = 0,

    /// <summary>A box controller.</summary>
    Box = 1,

    /// <summary>The number of defined controller shapes.</summary>
    Count = 2
}

/// <summary>Mirrors <c>openusd_physx_controller_non_walkable_mode</c>.</summary>
internal enum PhysxControllerNonWalkableMode : uint
{
    /// <summary>Prevent climbing steep surfaces.</summary>
    PreventClimbing = 0,

    /// <summary>Prevent climbing and force sliding down steep surfaces.</summary>
    PreventClimbingAndForceSliding = 1,

    /// <summary>The number of defined non-walkable modes.</summary>
    Count = 2
}

/// <summary>Mirrors <c>openusd_physx_controller_climbing_mode</c>.</summary>
internal enum PhysxControllerClimbingMode : uint
{
    /// <summary>Easy climbing uses only the bottom sphere of the capsule.</summary>
    Easy = 0,

    /// <summary>Constrained climbing requires the full capsule to clear the step.</summary>
    Constrained = 1,

    /// <summary>The number of defined climbing modes.</summary>
    Count = 2
}

/// <summary>Mirrors <c>openusd_physx_controller_flags</c>.</summary>
[Flags]
internal enum PhysxControllerFlags : uint
{
    /// <summary>No optional controller behavior.</summary>
    None = 0u,

    /// <summary>Report a controller hit event for every shape the controller touches.</summary>
    ReportHits = 1u << 0,

    /// <summary>Apply scene gravity to the controller between move commands.</summary>
    ApplyGravity = 1u << 1,

    /// <summary>Every defined controller flag.</summary>
    All = 0x3u
}

/// <summary>Mirrors <c>openusd_physx_tendon_type</c>.</summary>
internal enum PhysxTendonType : uint
{
    /// <summary>A fixed tendon coupling joint axes.</summary>
    Fixed = 0,

    /// <summary>A spatial tendon coupling points in space.</summary>
    Spatial = 1,

    /// <summary>The number of defined tendon types.</summary>
    Count = 2
}

/// <summary>Mirrors <c>openusd_physx_tendon_flags</c>.</summary>
[Flags]
internal enum PhysxTendonFlags : uint
{
    /// <summary>The tendon runs unlimited.</summary>
    None = 0u,

    /// <summary>Apply the low and high limit carried by the record.</summary>
    LimitEnabled = 1u << 0
}

/// <summary>Mirrors <c>openusd_physx_vehicle_drive</c>.</summary>
internal enum PhysxVehicleDrive : uint
{
    /// <summary>A full engine, clutch, gearbox, autobox, and differential drivetrain.</summary>
    Engine = 0,

    /// <summary>The number of defined drivetrains.</summary>
    Count = 1
}

/// <summary>Mirrors <c>openusd_physx_vehicle_query</c>.</summary>
internal enum PhysxVehicleQuery : uint
{
    /// <summary>Find the road surface with a raycast under each wheel.</summary>
    Raycast = 0,

    /// <summary>Find the road surface by sweeping a wheel shaped volume.</summary>
    Sweep = 1,

    /// <summary>The number of defined road geometry queries.</summary>
    Count = 2
}

/// <summary>Mirrors <c>openusd_physx_vehicle_flags</c>.</summary>
[Flags]
internal enum PhysxVehicleFlags : uint
{
    /// <summary>No optional vehicle behavior.</summary>
    None = 0u,

    /// <summary>
    /// Build the vehicle with an automatic gearbox: the autobox picks the gear whenever a command
    /// asks for gear zero, and the vehicle starts in neutral. Without this flag no autobox is
    /// built, gear zero holds the gear the gearbox already targets, and the vehicle starts in its
    /// first forward gear. An explicit gear command is honored either way.
    /// </summary>
    AutoboxEnabled = 1u << 0,

    /// <summary>Publish one body state per wheel carrying the wheel world transform.</summary>
    PublishWheels = 1u << 1,

    /// <summary>Limit the speed at which a suspension may expand.</summary>
    LimitSuspensionExpansion = 1u << 2,

    /// <summary>Every defined vehicle flag.</summary>
    All = 0x7u
}

/// <summary>Mirrors <c>openusd_physx_vehicle_wheel_flags</c>.</summary>
[Flags]
internal enum PhysxVehicleWheelFlags : uint
{
    /// <summary>The wheel answers no driver command.</summary>
    None = 0u,

    /// <summary>The wheel answers the steer command.</summary>
    Steers = 1u << 0,

    /// <summary>The wheel answers the brake command.</summary>
    Brakes = 1u << 1,

    /// <summary>The wheel answers the handbrake command.</summary>
    HandBrakes = 1u << 2,

    /// <summary>The wheel receives drive torque from the differential.</summary>
    Driven = 1u << 3,

    /// <summary>Every defined wheel flag.</summary>
    All = 0xFu
}
/// <summary>Mirrors <c>openusd_physx_particle_system_flags</c>.</summary>
[Flags]
internal enum PhysxParticleSystemFlags : uint
{
    /// <summary>No optional particle system behavior.</summary>
    None = 0u,

    /// <summary>Continuous collision detection for the particles of this system.</summary>
    EnableCcd = 1u << 0,

    /// <summary>Particles of two different particle groups collide with each other.</summary>
    GlobalSelfCollision = 1u << 1,

    /// <summary>Particles collide with rigid and deformable geometry.</summary>
    NonParticleCollision = 1u << 2,

    /// <summary>Every defined particle system flag.</summary>
    All = 0x7u
}

/// <summary>Mirrors <c>openusd_physx_particle_body_kind</c>.</summary>
internal enum PhysxParticleBodyKind : uint
{
    /// <summary>Solid, granular, or fluid particles carried by one particle buffer.</summary>
    Set = 0,

    /// <summary>The number of defined particle body kinds.</summary>
    Count = 1
}

/// <summary>Mirrors <c>openusd_physx_particle_body_flags</c>.</summary>
[Flags]
internal enum PhysxParticleBodyFlags : uint
{
    /// <summary>No optional particle body behavior.</summary>
    None = 0u,

    /// <summary>Simulate the particles with the fluid constraint model.</summary>
    Fluid = 1u << 0,

    /// <summary>Particles of this body collide with each other.</summary>
    SelfCollision = 1u << 1,

    /// <summary>Every defined particle body flag.</summary>
    All = 0x3u
}

/// <summary>Mirrors <c>openusd_physx_deformable_kind</c>.</summary>
internal enum PhysxDeformableKind : uint
{
    /// <summary>A triangulated surface solved on its own vertices.</summary>
    Surface = 0,

    /// <summary>A finite element volume driven by a tetrahedral simulation mesh.</summary>
    Volume = 1,

    /// <summary>The number of defined deformable kinds.</summary>
    Count = 2
}

/// <summary>Mirrors <c>openusd_physx_deformable_flags</c>.</summary>
[Flags]
internal enum PhysxDeformableFlags : uint
{
    /// <summary>No optional deformable behavior.</summary>
    None = 0u,

    /// <summary>Continuous collision detection for this deformable.</summary>
    EnableCcd = 1u << 0,

    /// <summary>Collisions between parts of this deformable.</summary>
    SelfCollision = 1u << 1,

    /// <summary>The simulation mesh is driven by the authored animation; volumes only.</summary>
    Kinematic = 1u << 2,

    /// <summary>Scene gravity is not applied to this deformable.</summary>
    DisableGravity = 1u << 3,

    /// <summary>Every defined deformable flag.</summary>
    All = 0xFu
}

/// <summary>Mirrors <c>openusd_physx_deformation_kind</c>.</summary>
internal enum PhysxDeformationKind : uint
{
    /// <summary>Solid or granular particles of one particle body.</summary>
    Particles = 0,

    /// <summary>Fluid particles of one particle body.</summary>
    Fluid = 1,

    /// <summary>Simulated vertices of one surface deformable.</summary>
    Surface = 2,

    /// <summary>Simulated vertices of one volume deformable simulation mesh.</summary>
    Volume = 3,

    /// <summary>The number of defined deformation kinds.</summary>
    Count = 4
}

/// <summary>Mirrors <c>openusd_physx_deformation_flags</c>.</summary>
[Flags]
internal enum PhysxDeformationFlags : uint
{
    /// <summary>The published window carries no optional state.</summary>
    None = 0u,

    /// <summary>The body did not move since the previous published step.</summary>
    Sleeping = 1u << 0
}
