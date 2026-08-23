// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Extraction;

/// <summary>Identifies one canonical simulated quantity resolved by extraction.</summary>
/// <remarks>
/// A key is namespace neutral. The same key is produced whether the winning opinion came
/// from the project namespace, an optional foreign namespace, or the standard namespace.
/// </remarks>
public enum UsdPhysicsExtractionKey
{
    /// <summary>The <c>UNMAPPED</c> canonical key.</summary>
    Unmapped = 0,

    /// <summary>The <c>SCENE_GRAVITY_DIRECTION</c> canonical key.</summary>
    SceneGravityDirection = 1,

    /// <summary>The <c>SCENE_GRAVITY_MAGNITUDE</c> canonical key.</summary>
    SceneGravityMagnitude = 2,

    /// <summary>The <c>SCENE_POSITION_ITERATIONS</c> canonical key.</summary>
    ScenePositionIterations = 3,

    /// <summary>The <c>SCENE_VELOCITY_ITERATIONS</c> canonical key.</summary>
    SceneVelocityIterations = 4,

    /// <summary>The <c>SCENE_BOUNCE_THRESHOLD</c> canonical key.</summary>
    SceneBounceThreshold = 5,

    /// <summary>The <c>SCENE_ENABLE_CCD</c> canonical key.</summary>
    SceneEnableCcd = 6,

    /// <summary>The <c>SCENE_ENABLE_STABILIZATION</c> canonical key.</summary>
    SceneEnableStabilization = 7,

    /// <summary>The <c>SCENE_ENABLE_DETERMINISM</c> canonical key.</summary>
    SceneEnableDeterminism = 8,

    /// <summary>The <c>SCENE_TIME_STEPS_PER_SECOND</c> canonical key.</summary>
    SceneTimeStepsPerSecond = 9,

    /// <summary>The <c>SCENE_MAX_SUBSTEPS</c> canonical key.</summary>
    SceneMaxSubsteps = 10,

    /// <summary>The <c>SCENE_GPU_REQUEST_MODE</c> canonical key.</summary>
    SceneGpuRequestMode = 11,

    /// <summary>The <c>SCENE_REPORT_CONTACTS</c> canonical key.</summary>
    SceneReportContacts = 12,

    /// <summary>The <c>BODY_ENABLED</c> canonical key.</summary>
    BodyEnabled = 20,

    /// <summary>The <c>BODY_KINEMATIC</c> canonical key.</summary>
    BodyKinematic = 21,

    /// <summary>The <c>BODY_STARTS_ASLEEP</c> canonical key.</summary>
    BodyStartsAsleep = 22,

    /// <summary>The <c>BODY_VELOCITY</c> canonical key.</summary>
    BodyVelocity = 23,

    /// <summary>The <c>BODY_ANGULAR_VELOCITY</c> canonical key.</summary>
    BodyAngularVelocity = 24,

    /// <summary>The <c>BODY_DISABLE_GRAVITY</c> canonical key.</summary>
    BodyDisableGravity = 25,

    /// <summary>The <c>BODY_MAX_LINEAR_VELOCITY</c> canonical key.</summary>
    BodyMaxLinearVelocity = 26,

    /// <summary>The <c>BODY_MAX_ANGULAR_VELOCITY</c> canonical key.</summary>
    BodyMaxAngularVelocity = 27,

    /// <summary>The <c>BODY_POSITION_ITERATIONS</c> canonical key.</summary>
    BodyPositionIterations = 28,

    /// <summary>The <c>BODY_VELOCITY_ITERATIONS</c> canonical key.</summary>
    BodyVelocityIterations = 29,

    /// <summary>The <c>BODY_ENABLE_CCD</c> canonical key.</summary>
    BodyEnableCcd = 30,

    /// <summary>The <c>BODY_SLEEP_THRESHOLD</c> canonical key.</summary>
    BodySleepThreshold = 31,

    /// <summary>The <c>BODY_LINEAR_DAMPING</c> canonical key.</summary>
    BodyLinearDamping = 32,

    /// <summary>The <c>BODY_ANGULAR_DAMPING</c> canonical key.</summary>
    BodyAngularDamping = 33,

    /// <summary>The <c>MASS_MASS</c> canonical key.</summary>
    MassMass = 40,

    /// <summary>The <c>MASS_DENSITY</c> canonical key.</summary>
    MassDensity = 41,

    /// <summary>The <c>MASS_CENTER_OF_MASS</c> canonical key.</summary>
    MassCenterOfMass = 42,

    /// <summary>The <c>MASS_DIAGONAL_INERTIA</c> canonical key.</summary>
    MassDiagonalInertia = 43,

    /// <summary>The <c>MASS_PRINCIPAL_AXES</c> canonical key.</summary>
    MassPrincipalAxes = 44,

    /// <summary>The <c>COLLISION_ENABLED</c> canonical key.</summary>
    CollisionEnabled = 50,

    /// <summary>The <c>COLLISION_APPROXIMATION</c> canonical key.</summary>
    CollisionApproximation = 51,

    /// <summary>The <c>COLLISION_CONTACT_OFFSET</c> canonical key.</summary>
    CollisionContactOffset = 52,

    /// <summary>The <c>COLLISION_REST_OFFSET</c> canonical key.</summary>
    CollisionRestOffset = 53,

    /// <summary>The <c>COLLISION_REPORT_CONTACTS</c> canonical key.</summary>
    CollisionReportContacts = 54,

    /// <summary>The <c>MATERIAL_STATIC_FRICTION</c> canonical key.</summary>
    MaterialStaticFriction = 60,

    /// <summary>The <c>MATERIAL_DYNAMIC_FRICTION</c> canonical key.</summary>
    MaterialDynamicFriction = 61,

    /// <summary>The <c>MATERIAL_RESTITUTION</c> canonical key.</summary>
    MaterialRestitution = 62,

    /// <summary>The <c>MATERIAL_DENSITY</c> canonical key.</summary>
    MaterialDensity = 63,

    /// <summary>The <c>MATERIAL_FRICTION_COMBINE</c> canonical key.</summary>
    MaterialFrictionCombine = 64,

    /// <summary>The <c>MATERIAL_RESTITUTION_COMBINE</c> canonical key.</summary>
    MaterialRestitutionCombine = 65,

    /// <summary>The <c>JOINT_ENABLED</c> canonical key.</summary>
    JointEnabled = 70,

    /// <summary>The <c>JOINT_LOCAL_POS0</c> canonical key.</summary>
    JointLocalPosition0 = 71,

    /// <summary>The <c>JOINT_LOCAL_ROT0</c> canonical key.</summary>
    JointLocalRotation0 = 72,

    /// <summary>The <c>JOINT_LOCAL_POS1</c> canonical key.</summary>
    JointLocalPosition1 = 73,

    /// <summary>The <c>JOINT_LOCAL_ROT1</c> canonical key.</summary>
    JointLocalRotation1 = 74,

    /// <summary>The <c>JOINT_AXIS</c> canonical key.</summary>
    JointAxis = 75,

    /// <summary>The <c>JOINT_LOWER_LIMIT</c> canonical key.</summary>
    JointLowerLimit = 76,

    /// <summary>The <c>JOINT_UPPER_LIMIT</c> canonical key.</summary>
    JointUpperLimit = 77,

    /// <summary>The <c>JOINT_MIN_DISTANCE</c> canonical key.</summary>
    JointMinDistance = 78,

    /// <summary>The <c>JOINT_MAX_DISTANCE</c> canonical key.</summary>
    JointMaxDistance = 79,

    /// <summary>The <c>JOINT_CONE_ANGLE0</c> canonical key.</summary>
    JointConeAngle0 = 80,

    /// <summary>The <c>JOINT_CONE_ANGLE1</c> canonical key.</summary>
    JointConeAngle1 = 81,

    /// <summary>The <c>JOINT_BREAK_FORCE</c> canonical key.</summary>
    JointBreakForce = 82,

    /// <summary>The <c>JOINT_BREAK_TORQUE</c> canonical key.</summary>
    JointBreakTorque = 83,

    /// <summary>The <c>JOINT_COLLISION_ENABLED</c> canonical key.</summary>
    JointCollisionEnabled = 84,

    /// <summary>The <c>JOINT_EXCLUDE_FROM_ARTICULATION</c> canonical key.</summary>
    JointExcludeFromArticulation = 85,

    /// <summary>The <c>DRIVE_STIFFNESS</c> canonical key.</summary>
    DriveStiffness = 90,

    /// <summary>The <c>DRIVE_DAMPING</c> canonical key.</summary>
    DriveDamping = 91,

    /// <summary>The <c>DRIVE_MAX_FORCE</c> canonical key.</summary>
    DriveMaxForce = 92,

    /// <summary>The <c>DRIVE_TARGET_POSITION</c> canonical key.</summary>
    DriveTargetPosition = 93,

    /// <summary>The <c>DRIVE_TARGET_VELOCITY</c> canonical key.</summary>
    DriveTargetVelocity = 94,

    /// <summary>The <c>DRIVE_TYPE</c> canonical key.</summary>
    DriveType = 95,

    /// <summary>The <c>LIMIT_LOW</c> canonical key of one multiple apply limit.</summary>
    LimitLow = 96,

    /// <summary>The <c>LIMIT_HIGH</c> canonical key of one multiple apply limit.</summary>
    LimitHigh = 97,

    /// <summary>The <c>FILTER_INVERT_GROUPS</c> canonical key.</summary>
    FilterInvertGroups = 100,

    /// <summary>The <c>FILTER_MERGE_GROUP</c> canonical key.</summary>
    FilterMergeGroup = 101,

    /// <summary>The <c>FILTER_ENABLED</c> canonical key.</summary>
    FilterEnabled = 102,

    /// <summary>The <c>FILTER_MODE</c> canonical key.</summary>
    FilterMode = 103,

    /// <summary>The <c>ARTICULATION_FIX_BASE</c> canonical key.</summary>
    ArticulationFixBase = 110,

    /// <summary>The <c>ARTICULATION_SELF_COLLISIONS</c> canonical key.</summary>
    ArticulationSelfCollisions = 111,

    /// <summary>The <c>ARTICULATION_POSITION_ITERATIONS</c> canonical key.</summary>
    ArticulationPositionIterations = 112,

    /// <summary>The <c>ARTICULATION_VELOCITY_ITERATIONS</c> canonical key.</summary>
    ArticulationVelocityIterations = 113,

    /// <summary>The <c>SIMULATION_IDENTITY</c> canonical key.</summary>
    SimulationIdentity = 120,

    /// <summary>The <c>SIMULATION_IDENTITY_DOMAIN</c> canonical key.</summary>
    SimulationIdentityDomain = 121,

    /// <summary>The <c>SIMULATION_IDENTITY_INDEX</c> canonical key.</summary>
    SimulationIdentityIndex = 122,

    /// <summary>The <c>TENDON_ENABLED</c> canonical key.</summary>
    TendonEnabled = 150,

    /// <summary>The <c>TENDON_STIFFNESS</c> canonical key.</summary>
    TendonStiffness = 151,

    /// <summary>The <c>TENDON_DAMPING</c> canonical key.</summary>
    TendonDamping = 152,

    /// <summary>The <c>TENDON_LIMIT_STIFFNESS</c> canonical key.</summary>
    TendonLimitStiffness = 153,

    /// <summary>The <c>TENDON_OFFSET</c> canonical key.</summary>
    TendonOffset = 154,

    /// <summary>The <c>TENDON_REST_LENGTH</c> canonical key.</summary>
    TendonRestLength = 155,

    /// <summary>The <c>TENDON_LOWER_LIMIT</c> canonical key.</summary>
    TendonLowerLimit = 156,

    /// <summary>The <c>TENDON_UPPER_LIMIT</c> canonical key.</summary>
    TendonUpperLimit = 157,

    /// <summary>The <c>TENDON_GEARINGS</c> canonical key.</summary>
    TendonGearings = 158,

    /// <summary>The <c>TENDON_FORCE_COEFFICIENTS</c> canonical key.</summary>
    TendonForceCoefficients = 159,

    /// <summary>The <c>ATTACHMENT_GEARING</c> canonical key.</summary>
    AttachmentGearing = 160,

    /// <summary>The <c>ATTACHMENT_LOCAL_POSITION</c> canonical key.</summary>
    AttachmentLocalPosition = 161,

    /// <summary>The <c>ATTACHMENT_REST_LENGTH</c> canonical key.</summary>
    AttachmentRestLength = 162,

    /// <summary>The <c>ATTACHMENT_LOWER_LIMIT</c> canonical key.</summary>
    AttachmentLowerLimit = 163,

    /// <summary>The <c>ATTACHMENT_UPPER_LIMIT</c> canonical key.</summary>
    AttachmentUpperLimit = 164,

    /// <summary>The <c>ATTACHMENT_ROLE</c> canonical key.</summary>
    AttachmentRole = 165,

    /// <summary>The <c>MIMIC_ENABLED</c> canonical key.</summary>
    MimicEnabled = 170,

    /// <summary>The <c>MIMIC_GEARING</c> canonical key.</summary>
    MimicGearing = 171,

    /// <summary>The <c>MIMIC_OFFSET</c> canonical key.</summary>
    MimicOffset = 172,

    /// <summary>The <c>MIMIC_AXIS</c> canonical key.</summary>
    MimicAxis = 173,

    /// <summary>The <c>MIMIC_REFERENCE_AXIS</c> canonical key.</summary>
    MimicReferenceAxis = 174,

    /// <summary>The <c>MIMIC_NATURAL_FREQUENCY</c> canonical key.</summary>
    MimicNaturalFrequency = 175,

    /// <summary>The <c>MIMIC_DAMPING_RATIO</c> canonical key.</summary>
    MimicDampingRatio = 176,

    /// <summary>The <c>VEHICLE_ENABLED</c> canonical key.</summary>
    VehicleEnabled = 180,

    /// <summary>The <c>VEHICLE_DRIVE_TYPE</c> canonical key.</summary>
    VehicleDriveType = 181,

    /// <summary>The <c>VEHICLE_LONGITUDINAL_AXIS</c> canonical key.</summary>
    VehicleLongitudinalAxis = 182,

    /// <summary>The <c>VEHICLE_LATERAL_AXIS</c> canonical key.</summary>
    VehicleLateralAxis = 183,

    /// <summary>The <c>VEHICLE_VERTICAL_AXIS</c> canonical key.</summary>
    VehicleVerticalAxis = 184,

    /// <summary>The <c>VEHICLE_SUSPENSION_QUERY_TYPE</c> canonical key.</summary>
    VehicleSuspensionQueryType = 185,

    /// <summary>The <c>ENGINE_PEAK_TORQUE</c> canonical key.</summary>
    EnginePeakTorque = 190,

    /// <summary>The <c>ENGINE_MAX_ROTATION_SPEED</c> canonical key.</summary>
    EngineMaxRotationSpeed = 191,

    /// <summary>The <c>ENGINE_IDLE_ROTATION_SPEED</c> canonical key.</summary>
    EngineIdleRotationSpeed = 192,

    /// <summary>The <c>ENGINE_MOMENT_OF_INERTIA</c> canonical key.</summary>
    EngineMomentOfInertia = 193,

    /// <summary>The <c>ENGINE_DAMPING_FULL_THROTTLE</c> canonical key.</summary>
    EngineDampingFullThrottle = 194,

    /// <summary>The <c>ENGINE_DAMPING_ZERO_THROTTLE_CLUTCH_ENGAGED</c> canonical key.</summary>
    EngineDampingZeroThrottleClutchEngaged = 195,

    /// <summary>The <c>ENGINE_DAMPING_ZERO_THROTTLE_CLUTCH_DISENGAGED</c> canonical key.</summary>
    EngineDampingZeroThrottleClutchDisengaged = 196,

    /// <summary>The <c>GEARS_RATIOS</c> canonical key.</summary>
    GearsRatios = 200,

    /// <summary>The <c>GEARS_RATIO_SCALE</c> canonical key.</summary>
    GearsRatioScale = 201,

    /// <summary>The <c>GEARS_SWITCH_TIME</c> canonical key.</summary>
    GearsSwitchTime = 202,

    /// <summary>The <c>AUTO_GEAR_BOX_UP_RATIOS</c> canonical key.</summary>
    AutoGearBoxUpRatios = 203,

    /// <summary>The <c>AUTO_GEAR_BOX_DOWN_RATIOS</c> canonical key.</summary>
    AutoGearBoxDownRatios = 204,

    /// <summary>The <c>AUTO_GEAR_BOX_LATENCY</c> canonical key.</summary>
    AutoGearBoxLatency = 205,

    /// <summary>The <c>CLUTCH_STRENGTH</c> canonical key.</summary>
    ClutchStrength = 206,

    /// <summary>The <c>DIFFERENTIAL_WHEELS</c> canonical key.</summary>
    DifferentialWheels = 207,

    /// <summary>The <c>DIFFERENTIAL_TORQUE_RATIOS</c> canonical key.</summary>
    DifferentialTorqueRatios = 208,

    /// <summary>The <c>BRAKES_MAX_BRAKE_TORQUE</c> canonical key.</summary>
    BrakesMaxBrakeTorque = 209,

    /// <summary>The <c>BRAKES_WHEELS</c> canonical key.</summary>
    BrakesWheels = 210,

    /// <summary>The <c>BRAKES_TORQUE_MULTIPLIERS</c> canonical key.</summary>
    BrakesTorqueMultipliers = 211,

    /// <summary>The <c>STEERING_MAX_STEER_ANGLE</c> canonical key.</summary>
    SteeringMaxSteerAngle = 212,

    /// <summary>The <c>STEERING_WHEELS</c> canonical key.</summary>
    SteeringWheels = 213,

    /// <summary>The <c>STEERING_ANGLE_MULTIPLIERS</c> canonical key.</summary>
    SteeringAngleMultipliers = 214,

    /// <summary>The <c>BRAKES_SECONDARY_MAX_BRAKE_TORQUE</c> canonical key.</summary>
    BrakesSecondaryMaxBrakeTorque = 215,

    /// <summary>The <c>BRAKES_SECONDARY_WHEELS</c> canonical key.</summary>
    BrakesSecondaryWheels = 216,

    /// <summary>The <c>BRAKES_SECONDARY_TORQUE_MULTIPLIERS</c> canonical key.</summary>
    BrakesSecondaryTorqueMultipliers = 217,

    /// <summary>The <c>WHEEL_ATTACHMENT_INDEX</c> canonical key.</summary>
    WheelAttachmentIndex = 220,

    /// <summary>The <c>WHEEL_ATTACHMENT_SUSPENSION_POSITION</c> canonical key.</summary>
    WheelAttachmentSuspensionPosition = 221,

    /// <summary>The <c>WHEEL_ATTACHMENT_SUSPENSION_TRAVEL_DIR</c> canonical key.</summary>
    WheelAttachmentSuspensionTravelDirection = 222,

    /// <summary>The <c>WHEEL_ATTACHMENT_WHEEL_POSITION</c> canonical key.</summary>
    WheelAttachmentWheelPosition = 223,

    /// <summary>The <c>WHEEL_RADIUS</c> canonical key.</summary>
    WheelRadius = 224,

    /// <summary>The <c>WHEEL_WIDTH</c> canonical key.</summary>
    WheelWidth = 225,

    /// <summary>The <c>WHEEL_MASS</c> canonical key.</summary>
    WheelMass = 226,

    /// <summary>The <c>WHEEL_MOMENT_OF_INERTIA</c> canonical key.</summary>
    WheelMomentOfInertia = 227,

    /// <summary>The <c>WHEEL_DAMPING_RATE</c> canonical key.</summary>
    WheelDampingRate = 228,

    /// <summary>The <c>SUSPENSION_SPRING_STRENGTH</c> canonical key.</summary>
    SuspensionSpringStrength = 229,

    /// <summary>The <c>SUSPENSION_SPRING_DAMPER_RATE</c> canonical key.</summary>
    SuspensionSpringDamperRate = 230,

    /// <summary>The <c>SUSPENSION_TRAVEL_DISTANCE</c> canonical key.</summary>
    SuspensionTravelDistance = 231,

    /// <summary>The <c>SUSPENSION_SPRUNG_MASS</c> canonical key.</summary>
    SuspensionSprungMass = 232,

    /// <summary>The <c>TIRE_LONGITUDINAL_STIFFNESS</c> canonical key.</summary>
    TireLongitudinalStiffness = 233,

    /// <summary>The <c>TIRE_CAMBER_STIFFNESS</c> canonical key.</summary>
    TireCamberStiffness = 234,

    /// <summary>The <c>TIRE_REST_LOAD</c> canonical key.</summary>
    TireRestLoad = 235,

    /// <summary>The <c>CONTROLLER_ENABLED</c> canonical key.</summary>
    ControllerEnabled = 240,

    /// <summary>The <c>CONTROLLER_SHAPE_TYPE</c> canonical key.</summary>
    ControllerShapeType = 241,

    /// <summary>The <c>CONTROLLER_RADIUS</c> canonical key.</summary>
    ControllerRadius = 242,

    /// <summary>The <c>CONTROLLER_HEIGHT</c> canonical key.</summary>
    ControllerHeight = 243,

    /// <summary>The <c>CONTROLLER_HALF_EXTENTS</c> canonical key.</summary>
    ControllerHalfExtents = 244,

    /// <summary>The <c>CONTROLLER_UP_AXIS</c> canonical key.</summary>
    ControllerUpAxis = 245,

    /// <summary>The <c>CONTROLLER_SLOPE_LIMIT</c> canonical key.</summary>
    ControllerSlopeLimit = 246,

    /// <summary>The <c>CONTROLLER_STEP_OFFSET</c> canonical key.</summary>
    ControllerStepOffset = 247,

    /// <summary>The <c>CONTROLLER_CONTACT_OFFSET</c> canonical key.</summary>
    ControllerContactOffset = 248,

    /// <summary>The <c>CONTROLLER_DENSITY</c> canonical key.</summary>
    ControllerDensity = 249,

    /// <summary>The <c>CONTROLLER_SCALE_COEFF</c> canonical key.</summary>
    ControllerScaleCoefficient = 250,

    /// <summary>The <c>CONTROLLER_VOLUME_GROWTH</c> canonical key.</summary>
    ControllerVolumeGrowth = 251,

    /// <summary>The <c>CONTROLLER_NON_WALKABLE_MODE</c> canonical key.</summary>
    ControllerNonWalkableMode = 252,

    /// <summary>The <c>CONTROLLER_CLIMBING_MODE</c> canonical key.</summary>
    ControllerClimbingMode = 253,

    /// <summary>The <c>CONTROLLER_MIN_MOVE_DISTANCE</c> canonical key.</summary>
    ControllerMinMoveDistance = 254,

    /// <summary>The <c>CONTROLLER_MAX_JUMP_HEIGHT</c> canonical key.</summary>
    ControllerMaxJumpHeight = 255,

    /// <summary>The <c>CONTROLLER_INVISIBLE_WALL_HEIGHT</c> canonical key.</summary>
    ControllerInvisibleWallHeight = 256,

    /// <summary>The <c>REL_SIMULATION_OWNER</c> canonical key.</summary>
    SimulationOwnerTargets = 130,

    /// <summary>The <c>REL_BODY0</c> canonical key.</summary>
    Body0Targets = 131,

    /// <summary>The <c>REL_BODY1</c> canonical key.</summary>
    Body1Targets = 132,

    /// <summary>The <c>REL_FILTERED_PAIRS</c> canonical key.</summary>
    FilteredPairsTargets = 133,

    /// <summary>The <c>REL_FILTERED_GROUPS</c> canonical key.</summary>
    FilteredGroupsTargets = 134,

    /// <summary>The <c>REL_MATERIAL_BINDING</c> canonical key.</summary>
    MaterialBindingTargets = 135,

    /// <summary>The <c>REL_COLLIDERS</c> canonical key.</summary>
    CollidersTargets = 136,

    /// <summary>The <c>REL_ATTACHMENT_ACTOR0</c> canonical key.</summary>
    AttachmentActor0Targets = 137,

    /// <summary>The <c>REL_ATTACHMENT_ACTOR1</c> canonical key.</summary>
    AttachmentActor1Targets = 138,

    /// <summary>The <c>REL_ARTICULATION</c> canonical key.</summary>
    ArticulationTargets = 139,

    /// <summary>The <c>REL_PARTICLE_SYSTEM</c> canonical key.</summary>
    ParticleSystemTargets = 140,

    /// <summary>The <c>REL_TENDON_ROOT_JOINT</c> canonical key.</summary>
    TendonRootJointTargets = 141,

    /// <summary>The <c>REL_TENDON_JOINTS</c> canonical key.</summary>
    TendonJointsTargets = 142,

    /// <summary>The <c>REL_TENDON_ROOT_ATTACHMENT</c> canonical key.</summary>
    TendonRootAttachmentTargets = 143,

    /// <summary>The <c>REL_ATTACHMENT_TENDON</c> canonical key.</summary>
    AttachmentTendonTargets = 144,

    /// <summary>The <c>REL_ATTACHMENT_PARENT</c> canonical key.</summary>
    AttachmentParentTargets = 145,

    /// <summary>The <c>REL_MIMIC_REFERENCE_JOINT</c> canonical key.</summary>
    MimicReferenceJointTargets = 146,

    /// <summary>The <c>REL_DEFORMABLE_MATERIAL</c> canonical key.</summary>
    DeformableMaterialTargets = 147,

    /// <summary>The <c>PARTICLE_SYSTEM_ENABLED</c> canonical key.</summary>
    ParticleSystemEnabled = 260,

    /// <summary>The <c>PARTICLE_SYSTEM_CONTACT_OFFSET</c> canonical key.</summary>
    ParticleSystemContactOffset = 261,

    /// <summary>The <c>PARTICLE_SYSTEM_REST_OFFSET</c> canonical key.</summary>
    ParticleSystemRestOffset = 262,

    /// <summary>The <c>PARTICLE_SYSTEM_PARTICLE_CONTACT_OFFSET</c> canonical key.</summary>
    ParticleSystemParticleContactOffset = 263,

    /// <summary>The <c>PARTICLE_SYSTEM_SOLID_REST_OFFSET</c> canonical key.</summary>
    ParticleSystemSolidRestOffset = 264,

    /// <summary>The <c>PARTICLE_SYSTEM_FLUID_REST_OFFSET</c> canonical key.</summary>
    ParticleSystemFluidRestOffset = 265,

    /// <summary>The <c>PARTICLE_SYSTEM_MAX_DEPENETRATION_VELOCITY</c> canonical key.</summary>
    ParticleSystemMaxDepenetrationVelocity = 266,

    /// <summary>The <c>PARTICLE_SYSTEM_NEIGHBORHOOD_SCALE</c> canonical key.</summary>
    ParticleSystemNeighborhoodScale = 267,

    /// <summary>The <c>PARTICLE_SYSTEM_MAX_NEIGHBORHOOD</c> canonical key.</summary>
    ParticleSystemMaxNeighborhood = 268,

    /// <summary>The <c>PARTICLE_SYSTEM_SOLVER_POSITION_ITERATIONS</c> canonical key.</summary>
    ParticleSystemSolverPositionIterations = 269,

    /// <summary>The <c>PARTICLE_SYSTEM_WIND</c> canonical key.</summary>
    ParticleSystemWind = 270,

    /// <summary>The <c>PARTICLE_SYSTEM_ENABLE_CCD</c> canonical key.</summary>
    ParticleSystemEnableCcd = 271,

    /// <summary>The <c>PARTICLE_SYSTEM_GLOBAL_SELF_COLLISION</c> canonical key.</summary>
    ParticleSystemGlobalSelfCollision = 272,

    /// <summary>The <c>PARTICLE_SYSTEM_NON_PARTICLE_COLLISION</c> canonical key.</summary>
    ParticleSystemNonParticleCollision = 273,

    /// <summary>The <c>PARTICLE_BODY_ENABLED</c> canonical key.</summary>
    ParticleBodyEnabled = 280,

    /// <summary>The <c>PARTICLE_BODY_FLUID</c> canonical key.</summary>
    ParticleBodyFluid = 281,

    /// <summary>The <c>PARTICLE_BODY_MASS</c> canonical key.</summary>
    ParticleBodyMass = 282,

    /// <summary>The <c>PARTICLE_BODY_GROUP</c> canonical key.</summary>
    ParticleBodyGroup = 283,

    /// <summary>The <c>PARTICLE_BODY_SELF_COLLISION</c> canonical key.</summary>
    ParticleBodySelfCollision = 284,

    /// <summary>The <c>PARTICLE_BODY_REST_POINTS</c> canonical key.</summary>
    ParticleBodyRestPoints = 285,

    /// <summary>The <c>PBD_MATERIAL_FRICTION</c> canonical key.</summary>
    PbdMaterialFriction = 300,

    /// <summary>The <c>PBD_MATERIAL_DAMPING</c> canonical key.</summary>
    PbdMaterialDamping = 301,

    /// <summary>The <c>PBD_MATERIAL_ADHESION</c> canonical key.</summary>
    PbdMaterialAdhesion = 302,

    /// <summary>The <c>PBD_MATERIAL_ADHESION_OFFSET_SCALE</c> canonical key.</summary>
    PbdMaterialAdhesionOffsetScale = 303,

    /// <summary>The <c>PBD_MATERIAL_PARTICLE_FRICTION_SCALE</c> canonical key.</summary>
    PbdMaterialParticleFrictionScale = 304,

    /// <summary>The <c>PBD_MATERIAL_PARTICLE_ADHESION_SCALE</c> canonical key.</summary>
    PbdMaterialParticleAdhesionScale = 305,

    /// <summary>The <c>PBD_MATERIAL_VISCOSITY</c> canonical key.</summary>
    PbdMaterialViscosity = 306,

    /// <summary>The <c>PBD_MATERIAL_SURFACE_TENSION</c> canonical key.</summary>
    PbdMaterialSurfaceTension = 307,

    /// <summary>The <c>PBD_MATERIAL_COHESION</c> canonical key.</summary>
    PbdMaterialCohesion = 308,

    /// <summary>The <c>PBD_MATERIAL_VORTICITY_CONFINEMENT</c> canonical key.</summary>
    PbdMaterialVorticityConfinement = 309,

    /// <summary>The <c>PBD_MATERIAL_DRAG</c> canonical key.</summary>
    PbdMaterialDrag = 310,

    /// <summary>The <c>PBD_MATERIAL_LIFT</c> canonical key.</summary>
    PbdMaterialLift = 311,

    /// <summary>The <c>PBD_MATERIAL_GRAVITY_SCALE</c> canonical key.</summary>
    PbdMaterialGravityScale = 312,

    /// <summary>The <c>PBD_MATERIAL_DENSITY</c> canonical key.</summary>
    PbdMaterialDensity = 313,

    /// <summary>The <c>PBD_MATERIAL_CFL_COEFFICIENT</c> canonical key.</summary>
    PbdMaterialCflCoefficient = 314,

    /// <summary>The <c>DEFORMABLE_ENABLED</c> canonical key.</summary>
    DeformableEnabled = 320,

    /// <summary>The <c>DEFORMABLE_ENABLE_CCD</c> canonical key.</summary>
    DeformableEnableCcd = 321,

    /// <summary>The <c>DEFORMABLE_SELF_COLLISION</c> canonical key.</summary>
    DeformableSelfCollision = 322,

    /// <summary>The <c>DEFORMABLE_SELF_COLLISION_FILTER_DISTANCE</c> canonical key.</summary>
    DeformableSelfCollisionFilterDistance = 323,

    /// <summary>The <c>DEFORMABLE_SOLVER_POSITION_ITERATIONS</c> canonical key.</summary>
    DeformableSolverPositionIterations = 324,

    /// <summary>The <c>DEFORMABLE_VERTEX_VELOCITY_DAMPING</c> canonical key.</summary>
    DeformableVertexVelocityDamping = 325,

    /// <summary>The <c>DEFORMABLE_MAX_DISPLACEMENT</c> canonical key.</summary>
    DeformableMaxDisplacement = 326,

    /// <summary>The <c>DEFORMABLE_COLLISION_ITERATION_MULTIPLIER</c> canonical key.</summary>
    DeformableCollisionIterationMultiplier = 327,

    /// <summary>The <c>DEFORMABLE_COLLISION_PAIR_UPDATE_FREQUENCY</c> canonical key.</summary>
    DeformableCollisionPairUpdateFrequency = 328,

    /// <summary>The <c>DEFORMABLE_REST_POINTS</c> canonical key.</summary>
    DeformableRestPoints = 329,

    /// <summary>The <c>DEFORMABLE_SIMULATION_INDICES</c> canonical key.</summary>
    DeformableSimulationIndices = 330,

    /// <summary>The <c>DEFORMABLE_KINEMATIC</c> canonical key.</summary>
    DeformableKinematic = 331,

    /// <summary>The <c>DEFORMABLE_MAX_DEPENETRATION_VELOCITY</c> canonical key.</summary>
    DeformableMaxDepenetrationVelocity = 332,

    /// <summary>The <c>DEFORMABLE_SETTLING_THRESHOLD</c> canonical key.</summary>
    DeformableSettlingThreshold = 333,

    /// <summary>The <c>DEFORMABLE_SLEEP_THRESHOLD</c> canonical key.</summary>
    DeformableSleepThreshold = 334,

    /// <summary>The <c>DEFORMABLE_SIMULATION_REST_POINTS</c> canonical key.</summary>
    DeformableSimulationRestPoints = 335,

    /// <summary>The <c>DEFORMABLE_COLLISION_INDICES</c> canonical key.</summary>
    DeformableCollisionIndices = 336,

    /// <summary>The <c>DEFORMABLE_COLLISION_REST_POINTS</c> canonical key.</summary>
    DeformableCollisionRestPoints = 337,

    /// <summary>The <c>DEFORMABLE_HEXAHEDRAL_RESOLUTION</c> canonical key.</summary>
    DeformableHexahedralResolution = 338,

    /// <summary>The <c>DEFORMABLE_MATERIAL_YOUNGS_MODULUS</c> canonical key.</summary>
    DeformableMaterialYoungsModulus = 350,

    /// <summary>The <c>DEFORMABLE_MATERIAL_POISSONS_RATIO</c> canonical key.</summary>
    DeformableMaterialPoissonsRatio = 351,

    /// <summary>The <c>DEFORMABLE_MATERIAL_DYNAMIC_FRICTION</c> canonical key.</summary>
    DeformableMaterialDynamicFriction = 352,

    /// <summary>The <c>DEFORMABLE_MATERIAL_DENSITY</c> canonical key.</summary>
    DeformableMaterialDensity = 353,

    /// <summary>The <c>DEFORMABLE_MATERIAL_ELASTICITY_DAMPING</c> canonical key.</summary>
    DeformableMaterialElasticityDamping = 354,

    /// <summary>The <c>DEFORMABLE_MATERIAL_BENDING_STIFFNESS</c> canonical key.</summary>
    DeformableMaterialBendingStiffness = 355,

    /// <summary>The <c>DEFORMABLE_MATERIAL_BENDING_DAMPING</c> canonical key.</summary>
    DeformableMaterialBendingDamping = 356,

    /// <summary>The <c>DEFORMABLE_MATERIAL_THICKNESS</c> canonical key.</summary>
    DeformableMaterialThickness = 357,

    /// <summary>The <c>DEFORMABLE_MATERIAL_DAMPING</c> canonical key.</summary>
    DeformableMaterialDamping = 358,

    /// <summary>The <c>DEFORMABLE_MATERIAL_DAMPING_SCALE</c> canonical key.</summary>
    DeformableMaterialDampingScale = 359,
}
