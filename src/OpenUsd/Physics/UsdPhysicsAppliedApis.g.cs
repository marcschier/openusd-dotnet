// Copyright (c) marcschier. Licensed under the MIT License.
#nullable enable


using OpenUsd.Interop;

namespace OpenUsd.Physics;

#pragma warning disable CS1591

public readonly struct UsdPhysicsCollisionAPI
{
    private readonly UsdStage? _stage;
    internal UsdPhysicsCollisionAPI(UsdStage stage, string path) { _stage = stage; Path = path; }
    public string Path { get; }
    public UsdPrim Prim => new(Stage, Path);
    public bool CollisionEnabled { get => Stage.Native.GetPhysicsBool(Path, OpenUsdNativePhysicsBoolProperty.CollisionEnabled); set => Stage.Native.SetPhysicsBool(Path, OpenUsdNativePhysicsBoolProperty.CollisionEnabled, value); }
    public void SetSimulationOwner(string target) => Prim.SetRelationshipTargets("physics:simulationOwner", [target]);
    public string[] GetSimulationOwner() => Prim.GetRelationshipTargets("physics:simulationOwner");
    public static bool Has(UsdPrim prim) => prim.OwningStage.Native.HasPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.Collision);
    public static UsdPhysicsCollisionAPI Apply(UsdPrim prim) { prim.OwningStage.Native.ApplyPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.Collision); return new(prim.OwningStage, prim.Path); }
    public static UsdPhysicsCollisionAPI Wrap(UsdPrim prim) => new(UsdPhysicsSchema.ValidateApi(prim, OpenUsdNativePhysicsApiKind.Collision, null, nameof(UsdPhysicsCollisionAPI)), prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}

public readonly struct UsdPhysicsMeshCollisionAPI
{
    private readonly UsdStage? _stage;
    internal UsdPhysicsMeshCollisionAPI(UsdStage stage, string path) { _stage = stage; Path = path; }
    public string Path { get; }
    public UsdPhysicsMeshCollisionApproximation Approximation { get => UsdPhysicsTokens.ToApproximation(Stage.Native.GetPhysicsToken(Path, OpenUsdNativePhysicsTokenProperty.MeshCollisionApproximation)); set => Stage.Native.SetPhysicsToken(Path, OpenUsdNativePhysicsTokenProperty.MeshCollisionApproximation, UsdPhysicsTokens.ToToken(value)); }
    public static bool Has(UsdPrim prim) => prim.OwningStage.Native.HasPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.MeshCollision);
    public static UsdPhysicsMeshCollisionAPI Apply(UsdPrim prim) { prim.OwningStage.Native.ApplyPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.MeshCollision); return new(prim.OwningStage, prim.Path); }
    public static UsdPhysicsMeshCollisionAPI Wrap(UsdPrim prim) => new(UsdPhysicsSchema.ValidateApi(prim, OpenUsdNativePhysicsApiKind.MeshCollision, null, nameof(UsdPhysicsMeshCollisionAPI)), prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}

public readonly struct UsdPhysicsMaterialAPI
{
    private readonly UsdStage? _stage;
    internal UsdPhysicsMaterialAPI(UsdStage stage, string path) { _stage = stage; Path = path; }
    public string Path { get; }
    public float DynamicFriction { get => F(OpenUsdNativePhysicsFloatProperty.MaterialDynamicFriction); set => SF(OpenUsdNativePhysicsFloatProperty.MaterialDynamicFriction, value); }
    public float StaticFriction { get => F(OpenUsdNativePhysicsFloatProperty.MaterialStaticFriction); set => SF(OpenUsdNativePhysicsFloatProperty.MaterialStaticFriction, value); }
    public float Restitution { get => F(OpenUsdNativePhysicsFloatProperty.MaterialRestitution); set => SF(OpenUsdNativePhysicsFloatProperty.MaterialRestitution, value); }
    public float Density { get => F(OpenUsdNativePhysicsFloatProperty.MaterialDensity); set => SF(OpenUsdNativePhysicsFloatProperty.MaterialDensity, value); }
    public static bool Has(UsdPrim prim) => prim.OwningStage.Native.HasPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.Material);
    public static UsdPhysicsMaterialAPI Apply(UsdPrim prim) { prim.OwningStage.Native.ApplyPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.Material); return new(prim.OwningStage, prim.Path); }
    public static UsdPhysicsMaterialAPI Wrap(UsdPrim prim) => new(UsdPhysicsSchema.ValidateApi(prim, OpenUsdNativePhysicsApiKind.Material, null, nameof(UsdPhysicsMaterialAPI)), prim.Path);
    private float F(OpenUsdNativePhysicsFloatProperty p) => Stage.Native.GetPhysicsFloat(Path, p);
    private void SF(OpenUsdNativePhysicsFloatProperty p, float v) => Stage.Native.SetPhysicsFloat(Path, p, v);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}

public readonly struct UsdPhysicsFilteredPairsAPI
{
    private readonly UsdStage? _stage;
    internal UsdPhysicsFilteredPairsAPI(UsdStage stage, string path) { _stage = stage; Path = path; }
    public string Path { get; }
    public UsdPrim Prim => new(Stage, Path);
    public void SetFilteredPairs(ReadOnlySpan<string> targets) => Prim.SetRelationshipTargets("physics:filteredPairs", targets);
    public string[] GetFilteredPairs() => Prim.GetRelationshipTargets("physics:filteredPairs");
    public static bool Has(UsdPrim prim) => prim.OwningStage.Native.HasPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.FilteredPairs);
    public static UsdPhysicsFilteredPairsAPI Apply(UsdPrim prim) { prim.OwningStage.Native.ApplyPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.FilteredPairs); return new(prim.OwningStage, prim.Path); }
    public static UsdPhysicsFilteredPairsAPI Wrap(UsdPrim prim) => new(UsdPhysicsSchema.ValidateApi(prim, OpenUsdNativePhysicsApiKind.FilteredPairs, null, nameof(UsdPhysicsFilteredPairsAPI)), prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}

public readonly struct UsdPhysicsArticulationRootAPI
{
    public static bool Has(UsdPrim prim) => prim.OwningStage.Native.HasPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.ArticulationRoot);
    public static UsdPhysicsArticulationRootAPI Apply(UsdPrim prim) { prim.OwningStage.Native.ApplyPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.ArticulationRoot); return new(); }
    public static UsdPhysicsArticulationRootAPI Wrap(UsdPrim prim) { UsdPhysicsSchema.ValidateApi(prim, OpenUsdNativePhysicsApiKind.ArticulationRoot, null, nameof(UsdPhysicsArticulationRootAPI)); return new(); }
}

public readonly struct UsdPhysicsLimitAPI
{
    private readonly UsdStage? _stage;
    internal UsdPhysicsLimitAPI(UsdStage stage, string path, string name) { _stage = stage; Path = path; Name = name; }
    public string Path { get; }
    public string Name { get; }
    public float Low { get => Stage.Native.GetPhysicsFloat(Path, OpenUsdNativePhysicsFloatProperty.LimitLow, Name); set => Stage.Native.SetPhysicsFloat(Path, OpenUsdNativePhysicsFloatProperty.LimitLow, value, Name); }
    public float High { get => Stage.Native.GetPhysicsFloat(Path, OpenUsdNativePhysicsFloatProperty.LimitHigh, Name); set => Stage.Native.SetPhysicsFloat(Path, OpenUsdNativePhysicsFloatProperty.LimitHigh, value, Name); }
    public static bool Has(UsdPrim prim, string name) { UsdPhysicsSchema.ValidateInstanceName(name); return prim.OwningStage.Native.HasPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.Limit, name); }
    public static UsdPhysicsLimitAPI Apply(UsdPrim prim, string name) { UsdPhysicsSchema.ValidateInstanceName(name); prim.OwningStage.Native.ApplyPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.Limit, name); return new(prim.OwningStage, prim.Path, name); }
    public static UsdPhysicsLimitAPI Wrap(UsdPrim prim, string name) { UsdPhysicsSchema.ValidateInstanceName(name); return new(UsdPhysicsSchema.ValidateApi(prim, OpenUsdNativePhysicsApiKind.Limit, name, nameof(UsdPhysicsLimitAPI)), prim.Path, name); }
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}

public readonly struct UsdPhysicsDriveAPI
{
    private readonly UsdStage? _stage;
    internal UsdPhysicsDriveAPI(UsdStage stage, string path, string name) { _stage = stage; Path = path; Name = name; }
    public string Path { get; }
    public string Name { get; }
    public UsdPhysicsDriveType Type { get => UsdPhysicsTokens.ToDriveType(Stage.Native.GetPhysicsToken(Path, OpenUsdNativePhysicsTokenProperty.DriveType, Name)); set => Stage.Native.SetPhysicsToken(Path, OpenUsdNativePhysicsTokenProperty.DriveType, UsdPhysicsTokens.ToToken(value), Name); }
    public float MaxForce { get => F(OpenUsdNativePhysicsFloatProperty.DriveMaxForce); set => SF(OpenUsdNativePhysicsFloatProperty.DriveMaxForce, value); }
    public float TargetPosition { get => F(OpenUsdNativePhysicsFloatProperty.DriveTargetPosition); set => SF(OpenUsdNativePhysicsFloatProperty.DriveTargetPosition, value); }
    public float TargetVelocity { get => F(OpenUsdNativePhysicsFloatProperty.DriveTargetVelocity); set => SF(OpenUsdNativePhysicsFloatProperty.DriveTargetVelocity, value); }
    public float Damping { get => F(OpenUsdNativePhysicsFloatProperty.DriveDamping); set => SF(OpenUsdNativePhysicsFloatProperty.DriveDamping, value); }
    public float Stiffness { get => F(OpenUsdNativePhysicsFloatProperty.DriveStiffness); set => SF(OpenUsdNativePhysicsFloatProperty.DriveStiffness, value); }
    public static bool Has(UsdPrim prim, string name) { UsdPhysicsSchema.ValidateInstanceName(name); return prim.OwningStage.Native.HasPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.Drive, name); }
    public static UsdPhysicsDriveAPI Apply(UsdPrim prim, string name) { UsdPhysicsSchema.ValidateInstanceName(name); prim.OwningStage.Native.ApplyPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.Drive, name); return new(prim.OwningStage, prim.Path, name); }
    public static UsdPhysicsDriveAPI Wrap(UsdPrim prim, string name) { UsdPhysicsSchema.ValidateInstanceName(name); return new(UsdPhysicsSchema.ValidateApi(prim, OpenUsdNativePhysicsApiKind.Drive, name, nameof(UsdPhysicsDriveAPI)), prim.Path, name); }
    private float F(OpenUsdNativePhysicsFloatProperty p) => Stage.Native.GetPhysicsFloat(Path, p, Name);
    private void SF(OpenUsdNativePhysicsFloatProperty p, float v) => Stage.Native.SetPhysicsFloat(Path, p, v, Name);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
