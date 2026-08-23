// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Extraction;

/// <summary>Identifies the stage up axis an extraction was produced from.</summary>
/// <remarks>
/// The values mirror the native extraction ABI exactly, so a page can be decoded without a
/// translation table.
/// </remarks>
public enum UsdPhysicsExtractionUpAxis
{
    /// <summary>The stage used an X up basis and was rotated into the simulation basis.</summary>
    X = 0,

    /// <summary>The stage already used the simulation up axis.</summary>
    Y = 1,

    /// <summary>The stage used a Z up basis and was rotated into the simulation basis.</summary>
    Z = 2,
}

/// <summary>Describes stage-wide observations recorded while a page was produced.</summary>
[Flags]
public enum UsdPhysicsExtractionPageTraits : uint
{
    /// <summary>Nothing notable was observed.</summary>
    None = 0,

    /// <summary>No kilogramsPerUnit was authored, so the standard fallback was used.</summary>
    KilogramsFallback = 1u << 0,

    /// <summary>No metersPerUnit was authored, so the standard fallback was used.</summary>
    MetersFallback = 1u << 1,

    /// <summary>No timeCodesPerSecond was authored, so the stage default was used.</summary>
    TimeCodesFallback = 1u << 2,

    /// <summary>No start or end time code was authored.</summary>
    TimeRangeFallback = 1u << 3,

    /// <summary>The stage up axis was rotated into the simulation up axis.</summary>
    UpAxisConverted = 1u << 4,

    /// <summary>At least one object was individually disabled by an error diagnostic.</summary>
    HasDisabledObjects = 1u << 5,

    /// <summary>A capacity bound was reached and part of the stage was not recorded.</summary>
    Truncated = 1u << 6,

    /// <summary>At least one optional foreign opinion took part in resolution.</summary>
    HasForeignOpinions = 1u << 7,

    /// <summary>At least one object came from an instance proxy.</summary>
    HasInstances = 1u << 8,
}

/// <summary>Identifies what one extracted object is.</summary>
public enum UsdPhysicsExtractionObjectKind
{
    /// <summary>The object kind is not recognized.</summary>
    Unknown = 0,

    /// <summary>A physics scene that owns simulated objects.</summary>
    Scene = 1,

    /// <summary>A rigid body.</summary>
    RigidBody = 2,

    /// <summary>A collider.</summary>
    Collider = 3,

    /// <summary>A rigid body material.</summary>
    Material = 4,

    /// <summary>A collision group.</summary>
    CollisionGroup = 5,

    /// <summary>A joint of any joint type.</summary>
    Joint = 6,

    /// <summary>An articulation root.</summary>
    ArticulationRoot = 7,

    /// <summary>An authored filtered pair set.</summary>
    FilteredPairs = 8,

    /// <summary>A character controller.</summary>
    CharacterController = 9,

    /// <summary>A vehicle.</summary>
    Vehicle = 10,

    /// <summary>A vehicle wheel attachment.</summary>
    VehicleWheelAttachment = 11,

    /// <summary>A vehicle tire friction table.</summary>
    VehicleFrictionTable = 12,

    /// <summary>A fixed tendon.</summary>
    FixedTendon = 13,

    /// <summary>A spatial tendon.</summary>
    SpatialTendon = 14,

    /// <summary>A tendon attachment.</summary>
    TendonAttachment = 15,

    /// <summary>A mimic joint.</summary>
    MimicJoint = 16,

    /// <summary>A particle system.</summary>
    ParticleSystem = 17,

    /// <summary>A particle set.</summary>
    ParticleSet = 18,

    /// <summary>A particle cloth.</summary>
    ParticleCloth = 19,

    /// <summary>Diffuse particles.</summary>
    DiffuseParticles = 20,

    /// <summary>A surface deformable body.</summary>
    SurfaceDeformable = 21,

    /// <summary>A volume deformable body.</summary>
    VolumeDeformable = 22,

    /// <summary>An attachment between two simulated actors.</summary>
    Attachment = 23,

    /// <summary>An authored collision filter.</summary>
    CollisionFilter = 24,

    /// <summary>A cooked data reference.</summary>
    CookedData = 25,

    /// <summary>A position based dynamics material.</summary>
    PbdMaterial = 26,

    /// <summary>A surface deformable material.</summary>
    SurfaceDeformableMaterial = 27,

    /// <summary>A volume deformable material.</summary>
    VolumeDeformableMaterial = 28,

    /// <summary>Project simulation metadata.</summary>
    SimulationMetadata = 29,
}

/// <summary>Groups extracted objects into the simulation domains they belong to.</summary>
[Flags]
public enum UsdPhysicsExtractionDomains : uint
{
    /// <summary>No domain.</summary>
    None = 0,

    /// <summary>The scene domain.</summary>
    Scene = 1u << 0,

    /// <summary>The rigid body domain.</summary>
    RigidBody = 1u << 1,

    /// <summary>The collision domain.</summary>
    Collision = 1u << 2,

    /// <summary>The material domain.</summary>
    Material = 1u << 3,

    /// <summary>The joint domain.</summary>
    Joint = 1u << 4,

    /// <summary>The articulation domain.</summary>
    Articulation = 1u << 5,

    /// <summary>The tendon domain.</summary>
    Tendon = 1u << 6,

    /// <summary>The mimic joint domain.</summary>
    Mimic = 1u << 7,

    /// <summary>The character controller domain.</summary>
    Controller = 1u << 8,

    /// <summary>The vehicle domain.</summary>
    Vehicle = 1u << 9,

    /// <summary>The particle and fluid domain.</summary>
    Particle = 1u << 10,

    /// <summary>The deformable and cloth domain.</summary>
    Deformable = 1u << 11,

    /// <summary>The attachment domain.</summary>
    Attachment = 1u << 12,

    /// <summary>The collision filtering domain.</summary>
    Filtering = 1u << 13,

    /// <summary>The cooked data domain.</summary>
    CookedData = 1u << 14,

    /// <summary>The project simulation metadata domain.</summary>
    SimulationMetadata = 1u << 15,
}

/// <summary>Describes how one extracted object participates in simulation.</summary>
[Flags]
public enum UsdPhysicsExtractionObjectTraits : uint
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>The object is enabled.</summary>
    Enabled = 1u << 0,

    /// <summary>An error diagnostic disabled this object individually.</summary>
    DisabledByDiagnostic = 1u << 1,

    /// <summary>The object is simulated dynamically.</summary>
    Dynamic = 1u << 2,

    /// <summary>The object is driven kinematically.</summary>
    Kinematic = 1u << 3,

    /// <summary>The object carries an animated transform.</summary>
    Animated = 1u << 4,

    /// <summary>The object never moves.</summary>
    Static = 1u << 5,

    /// <summary>The object came from an instance proxy.</summary>
    InstanceProxy = 1u << 6,

    /// <summary>The object lives inside a prototype.</summary>
    InPrototype = 1u << 7,

    /// <summary>At least one project opinion was resolved on this object.</summary>
    ProjectOpinions = 1u << 8,

    /// <summary>At least one foreign opinion was resolved on this object.</summary>
    ForeignOpinions = 1u << 9,

    /// <summary>At least one standard opinion was resolved on this object.</summary>
    StandardOpinions = 1u << 10,

    /// <summary>The object belongs to a domain that is extracted but not simulated yet.</summary>
    UnsupportedDomain = 1u << 11,

    /// <summary>The composed visibility of the source prim is invisible.</summary>
    Invisible = 1u << 12,

    /// <summary>The composed purpose of the source prim is guide.</summary>
    GuidePurpose = 1u << 13,

    /// <summary>The source transform has time samples.</summary>
    TimeSampledTransform = 1u << 14,

    /// <summary>A mass was authored on this object.</summary>
    MassAuthored = 1u << 15,

    /// <summary>The object claims an owner that contradicts another authored owner.</summary>
    ContradictoryOwnership = 1u << 16,

    /// <summary>The object is a rigid body nested inside another rigid body.</summary>
    NestedBody = 1u << 17,

    /// <summary>The object is the default simulation scene.</summary>
    DefaultScene = 1u << 18,
}

/// <summary>Identifies the collision geometry an extracted collider resolved to.</summary>
public enum UsdPhysicsExtractionGeometryKind
{
    /// <summary>No collision geometry.</summary>
    None = 0,

    /// <summary>A sphere.</summary>
    Sphere = 1,

    /// <summary>A box.</summary>
    Box = 2,

    /// <summary>A capsule.</summary>
    Capsule = 3,

    /// <summary>A cylinder.</summary>
    Cylinder = 4,

    /// <summary>A cone.</summary>
    Cone = 5,

    /// <summary>An infinite plane.</summary>
    Plane = 6,

    /// <summary>A triangle mesh.</summary>
    Mesh = 7,

    /// <summary>A convex approximation of a mesh.</summary>
    ConvexMesh = 8,

    /// <summary>A point cloud.</summary>
    Points = 9,

    /// <summary>A tetrahedral mesh.</summary>
    TetMesh = 10,
}

/// <summary>Identifies which authored namespace supplied one resolved property.</summary>
public enum UsdPhysicsExtractionSource
{
    /// <summary>The project <c>openUsdPhysics</c> namespace, which always wins.</summary>
    Project = 0,

    /// <summary>An optional foreign vendor namespace that was present on the stage.</summary>
    Foreign = 1,

    /// <summary>The standard <c>physics</c> namespace.</summary>
    Standard = 2,

    /// <summary>No opinion was authored, so a schema fallback applies.</summary>
    Fallback = 3,
}

/// <summary>Identifies how one extracted property value is stored.</summary>
public enum UsdPhysicsExtractionValueKind
{
    /// <summary>No value.</summary>
    None = 0,

    /// <summary>A boolean stored as a scalar.</summary>
    Bool = 1,

    /// <summary>An integer stored as a scalar.</summary>
    Integral = 2,

    /// <summary>A real number stored as a scalar.</summary>
    Real = 3,

    /// <summary>Two real components.</summary>
    Vector2 = 4,

    /// <summary>Three real components.</summary>
    Vector3 = 5,

    /// <summary>Four real components.</summary>
    Vector4 = 6,

    /// <summary>A quaternion stored as w, x, y, z.</summary>
    Quaternion = 7,

    /// <summary>Sixteen real components in row major order.</summary>
    Matrix4 = 8,

    /// <summary>One text value.</summary>
    Text = 9,

    /// <summary>An array of real numbers.</summary>
    RealArray = 10,

    /// <summary>An array of integers.</summary>
    IntegralArray = 11,

    /// <summary>An array of two component vectors.</summary>
    Vector2Array = 12,

    /// <summary>An array of three component vectors.</summary>
    Vector3Array = 13,

    /// <summary>An array of text values.</summary>
    TextArray = 14,
}

/// <summary>Describes how one property was authored and resolved.</summary>
[Flags]
public enum UsdPhysicsExtractionPropertyTraits : uint
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>The property has time samples.</summary>
    TimeSampled = 1u << 0,

    /// <summary>The property is uniform.</summary>
    Uniform = 1u << 1,

    /// <summary>The property carries no canonical meaning.</summary>
    Unmapped = 1u << 2,

    /// <summary>A weaker namespace also authored this property and lost.</summary>
    ShadowsWeaker = 1u << 3,

    /// <summary>The authored value could not be represented.</summary>
    Invalid = 1u << 4,

    /// <summary>The value was converted into simulation units.</summary>
    Converted = 1u << 5,

    /// <summary>More than one foreign opinion matched, so the first was used.</summary>
    AmbiguousForeign = 1u << 6,
}

/// <summary>Identifies how serious one extraction diagnostic is.</summary>
public enum UsdPhysicsExtractionSeverity
{
    /// <summary>The diagnostic only records an observation.</summary>
    Information = 0,

    /// <summary>The diagnostic records a recoverable problem.</summary>
    Warning = 1,

    /// <summary>The diagnostic disabled the object it names.</summary>
    Error = 2,
}

/// <summary>Groups extraction diagnostics by the concern they describe.</summary>
public enum UsdPhysicsExtractionCategory
{
    /// <summary>Stage units, up axis, and time metadata.</summary>
    Units = 0,

    /// <summary>Simulation ownership and body classification.</summary>
    Ownership = 1,

    /// <summary>Schema application and authored values.</summary>
    Schema = 2,

    /// <summary>Namespace precedence resolution.</summary>
    Precedence = 3,

    /// <summary>Domains that are extracted but not simulated yet.</summary>
    Capability = 4,

    /// <summary>Collision geometry.</summary>
    Geometry = 5,

    /// <summary>Extraction capacity bounds.</summary>
    Capacity = 6,

    /// <summary>Instancing and prototypes.</summary>
    Instance = 7,
}

/// <summary>Identifies one specific extraction diagnostic.</summary>
public enum UsdPhysicsExtractionCode
{
    /// <summary>No code.</summary>
    None = 0,

    /// <summary>kilogramsPerUnit was not authored.</summary>
    KilogramsPerUnitFallback = 1,

    /// <summary>metersPerUnit was not authored.</summary>
    MetersPerUnitFallback = 2,

    /// <summary>timeCodesPerSecond was not authored.</summary>
    TimeCodesPerSecondFallback = 3,

    /// <summary>No authored start or end time code was found.</summary>
    TimeRangeFallback = 4,

    /// <summary>The stage up axis was converted.</summary>
    UpAxisConverted = 5,

    /// <summary>Stage units were not positive finite numbers.</summary>
    NonFiniteUnits = 6,

    /// <summary>More than one simulation owner was authored.</summary>
    MultipleSimulationOwners = 10,

    /// <summary>The authored simulation owner is not a physics scene.</summary>
    UnknownSimulationOwner = 11,

    /// <summary>A rigid body is nested inside another rigid body.</summary>
    NestedRigidBody = 12,

    /// <summary>A collider and its body claim different owners.</summary>
    ContradictoryOwnership = 13,

    /// <summary>A dynamic body carries an animated transform.</summary>
    AnimatedDynamicBody = 14,

    /// <summary>A joint body target does not resolve.</summary>
    JointBodyUnresolved = 15,

    /// <summary>A collider has no rigid body ancestor.</summary>
    OrphanedCollider = 16,

    /// <summary>A foreign opinion supplied a canonical property.</summary>
    ForeignOpinionUsed = 20,

    /// <summary>Several foreign opinions matched one canonical property.</summary>
    ForeignOpinionAmbiguous = 21,

    /// <summary>A foreign opinion carries no canonical meaning.</summary>
    ForeignOpinionUnmapped = 22,

    /// <summary>An authored value type is not representable.</summary>
    PropertyTypeUnsupported = 23,

    /// <summary>An authored value is not usable.</summary>
    PropertyValueInvalid = 24,

    /// <summary>The domain is extracted but not simulated yet.</summary>
    DomainNotSimulated = 30,

    /// <summary>The collider prim type has no supported collision geometry.</summary>
    GeometryUnsupported = 31,

    /// <summary>Mesh topology is degenerate or out of range.</summary>
    GeometryDegenerate = 32,

    /// <summary>An extraction capacity bound was reached.</summary>
    CapacityExceeded = 40,

    /// <summary>An instance proxy bound was reached.</summary>
    InstanceProxyLimit = 41,
}
