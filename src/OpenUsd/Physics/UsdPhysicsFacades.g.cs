// Copyright (c) marcschier. Licensed under the MIT License.
#nullable enable


using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Physics;

#pragma warning disable CS1591

/// <summary>Focused UsdPhysics schema-definition conveniences for <see cref="UsdStage"/>.</summary>
[ExcludeFromCodeCoverage(Justification = "Exercised by native and NativeAOT integration probes.")]
public static class UsdPhysicsStageExtensions
{
    public static UsdPhysicsScene DefinePhysicsScene(this UsdStage stage, string path) =>
        Define(stage, path, OpenUsdNativePhysicsSchemaKind.Scene, (s, p) => new UsdPhysicsScene(s, p));

    public static UsdPhysicsCollisionGroup DefinePhysicsCollisionGroup(this UsdStage stage, string path) =>
        Define(stage, path, OpenUsdNativePhysicsSchemaKind.CollisionGroup, (s, p) => new UsdPhysicsCollisionGroup(s, p));

    public static UsdPhysicsJoint DefinePhysicsJoint(this UsdStage stage, string path) =>
        Define(stage, path, OpenUsdNativePhysicsSchemaKind.Joint, (s, p) => new UsdPhysicsJoint(s, p));

    public static UsdPhysicsRevoluteJoint DefinePhysicsRevoluteJoint(this UsdStage stage, string path) =>
        Define(stage, path, OpenUsdNativePhysicsSchemaKind.RevoluteJoint, (s, p) => new UsdPhysicsRevoluteJoint(s, p));

    public static UsdPhysicsPrismaticJoint DefinePhysicsPrismaticJoint(this UsdStage stage, string path) =>
        Define(stage, path, OpenUsdNativePhysicsSchemaKind.PrismaticJoint, (s, p) => new UsdPhysicsPrismaticJoint(s, p));

    public static UsdPhysicsSphericalJoint DefinePhysicsSphericalJoint(this UsdStage stage, string path) =>
        Define(stage, path, OpenUsdNativePhysicsSchemaKind.SphericalJoint, (s, p) => new UsdPhysicsSphericalJoint(s, p));

    public static UsdPhysicsDistanceJoint DefinePhysicsDistanceJoint(this UsdStage stage, string path) =>
        Define(stage, path, OpenUsdNativePhysicsSchemaKind.DistanceJoint, (s, p) => new UsdPhysicsDistanceJoint(s, p));

    public static UsdPhysicsFixedJoint DefinePhysicsFixedJoint(this UsdStage stage, string path) =>
        Define(stage, path, OpenUsdNativePhysicsSchemaKind.FixedJoint, (s, p) => new UsdPhysicsFixedJoint(s, p));

    private static T Define<T>(
        UsdStage stage,
        string path,
        OpenUsdNativePhysicsSchemaKind schemaKind,
        Func<UsdStage, string, T> factory)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path);
        stage.Native.DefinePhysics(path, schemaKind);
        return factory(stage, path);
    }
}

public readonly struct UsdPhysicsScene : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdPhysicsScene(UsdStage stage, string path) { _stage = stage; Path = path; }
    public string Path { get; }
    public UsdPrim Prim => new(Stage, Path);
    public UsdGeomXformable Xformable => UsdGeomXformable.Wrap(Prim);
    public UsdVec3f GravityDirection
    {
        get => UsdVec3f.FromNative(Stage.Native.GetPhysicsVec3f(Path, OpenUsdNativePhysicsVec3fProperty.SceneGravityDirection));
        set { UsdPhysicsSchema.ValidateVec3f(value, nameof(value)); Stage.Native.SetPhysicsVec3f(Path, OpenUsdNativePhysicsVec3fProperty.SceneGravityDirection, value.ToNative()); }
    }
    public float GravityMagnitude
    {
        get => Stage.Native.GetPhysicsFloat(Path, OpenUsdNativePhysicsFloatProperty.SceneGravityMagnitude);
        set => Stage.Native.SetPhysicsFloat(Path, OpenUsdNativePhysicsFloatProperty.SceneGravityMagnitude, value);
    }
    public static bool TryWrap(UsdPrim prim, out UsdPhysicsScene value) => TryWrap(prim, OpenUsdNativePhysicsSchemaKind.Scene, out value);
    public static UsdPhysicsScene Wrap(UsdPrim prim) => new(UsdPhysicsSchema.Validate(prim, OpenUsdNativePhysicsSchemaKind.Scene, nameof(UsdPhysicsScene)), prim.Path);
    private static bool TryWrap(UsdPrim prim, OpenUsdNativePhysicsSchemaKind kind, out UsdPhysicsScene value)
    {
        if (UsdPhysicsSchema.TryValidate(prim, kind, out UsdStage? stage))
        { value = new(stage!, prim.Path); return true; }
        value = default;
        return false;
    }
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}

public readonly struct UsdPhysicsCollisionGroup : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdPhysicsCollisionGroup(UsdStage stage, string path) { _stage = stage; Path = path; }
    public string Path { get; }
    public UsdPrim Prim => new(Stage, Path);
    public string MergeGroupName { get => Stage.Native.GetPhysicsString(Path, OpenUsdNativePhysicsStringProperty.CollisionGroupMergeGroupName); set => Stage.Native.SetPhysicsString(Path, OpenUsdNativePhysicsStringProperty.CollisionGroupMergeGroupName, value); }
    public bool InvertFilteredGroups { get => Stage.Native.GetPhysicsBool(Path, OpenUsdNativePhysicsBoolProperty.CollisionGroupInvertFilteredGroups); set => Stage.Native.SetPhysicsBool(Path, OpenUsdNativePhysicsBoolProperty.CollisionGroupInvertFilteredGroups, value); }
    public void SetFilteredGroups(ReadOnlySpan<string> targets) => Prim.SetRelationshipTargets("physics:filteredGroups", targets);
    public string[] GetFilteredGroups() => Prim.GetRelationshipTargets("physics:filteredGroups");
    public void SetColliders(ReadOnlySpan<string> targets) => Prim.SetRelationshipTargets("collection:colliders:includes", targets);
    public string[] GetColliders() => Prim.GetRelationshipTargets("collection:colliders:includes");
    public static bool TryWrap(UsdPrim prim, out UsdPhysicsCollisionGroup value) => TryWrap(prim, OpenUsdNativePhysicsSchemaKind.CollisionGroup, out value);
    public static UsdPhysicsCollisionGroup Wrap(UsdPrim prim) => new(UsdPhysicsSchema.Validate(prim, OpenUsdNativePhysicsSchemaKind.CollisionGroup, nameof(UsdPhysicsCollisionGroup)), prim.Path);
    private static bool TryWrap(UsdPrim prim, OpenUsdNativePhysicsSchemaKind kind, out UsdPhysicsCollisionGroup value)
    {
        if (UsdPhysicsSchema.TryValidate(prim, kind, out UsdStage? stage))
        { value = new(stage!, prim.Path); return true; }
        value = default;
        return false;
    }
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}

public readonly struct UsdPhysicsJoint : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdPhysicsJoint(UsdStage stage, string path) { _stage = stage; Path = path; }
    public string Path { get; }
    public UsdPrim Prim => new(Stage, Path);
    public UsdGeomImageable Imageable => UsdGeomImageable.Wrap(Prim);
    public UsdVec3f LocalPos0 { get => GetVec(OpenUsdNativePhysicsVec3fProperty.JointLocalPos0); set => SetVec(OpenUsdNativePhysicsVec3fProperty.JointLocalPos0, value); }
    public UsdVec3f LocalPos1 { get => GetVec(OpenUsdNativePhysicsVec3fProperty.JointLocalPos1); set => SetVec(OpenUsdNativePhysicsVec3fProperty.JointLocalPos1, value); }
    public UsdQuatf LocalRot0 { get => GetQuat(OpenUsdNativePhysicsQuatfProperty.JointLocalRot0); set => Stage.Native.SetPhysicsQuatf(Path, OpenUsdNativePhysicsQuatfProperty.JointLocalRot0, value.ToNative()); }
    public UsdQuatf LocalRot1 { get => GetQuat(OpenUsdNativePhysicsQuatfProperty.JointLocalRot1); set => Stage.Native.SetPhysicsQuatf(Path, OpenUsdNativePhysicsQuatfProperty.JointLocalRot1, value.ToNative()); }
    public bool JointEnabled { get => GetBool(OpenUsdNativePhysicsBoolProperty.JointEnabled); set => SetBool(OpenUsdNativePhysicsBoolProperty.JointEnabled, value); }
    public bool CollisionEnabled { get => GetBool(OpenUsdNativePhysicsBoolProperty.JointCollisionEnabled); set => SetBool(OpenUsdNativePhysicsBoolProperty.JointCollisionEnabled, value); }
    public bool ExcludeFromArticulation { get => GetBool(OpenUsdNativePhysicsBoolProperty.JointExcludeFromArticulation); set => SetBool(OpenUsdNativePhysicsBoolProperty.JointExcludeFromArticulation, value); }
    public float BreakForce { get => GetFloat(OpenUsdNativePhysicsFloatProperty.JointBreakForce); set => SetFloat(OpenUsdNativePhysicsFloatProperty.JointBreakForce, value); }
    public float BreakTorque { get => GetFloat(OpenUsdNativePhysicsFloatProperty.JointBreakTorque); set => SetFloat(OpenUsdNativePhysicsFloatProperty.JointBreakTorque, value); }
    public void SetBody0(string target) => Prim.SetRelationshipTargets("physics:body0", [target]);
    public void SetBody1(string target) => Prim.SetRelationshipTargets("physics:body1", [target]);
    public string[] GetBody0() => Prim.GetRelationshipTargets("physics:body0");
    public string[] GetBody1() => Prim.GetRelationshipTargets("physics:body1");
    internal float GetFloat(OpenUsdNativePhysicsFloatProperty p) => Stage.Native.GetPhysicsFloat(Path, p);
    internal void SetFloat(OpenUsdNativePhysicsFloatProperty p, float v) { UsdPhysicsSchema.ValidateFinite(v, nameof(v)); Stage.Native.SetPhysicsFloat(Path, p, v); }
    internal bool GetBool(OpenUsdNativePhysicsBoolProperty p) => Stage.Native.GetPhysicsBool(Path, p);
    internal void SetBool(OpenUsdNativePhysicsBoolProperty p, bool v) => Stage.Native.SetPhysicsBool(Path, p, v);
    internal UsdVec3f GetVec(OpenUsdNativePhysicsVec3fProperty p) => UsdVec3f.FromNative(Stage.Native.GetPhysicsVec3f(Path, p));
    internal void SetVec(OpenUsdNativePhysicsVec3fProperty p, UsdVec3f v) { UsdPhysicsSchema.ValidateVec3f(v, nameof(v)); Stage.Native.SetPhysicsVec3f(Path, p, v.ToNative()); }
    internal UsdQuatf GetQuat(OpenUsdNativePhysicsQuatfProperty p) => UsdQuatf.FromNative(Stage.Native.GetPhysicsQuatf(Path, p));
    public static bool TryWrap(UsdPrim prim, out UsdPhysicsJoint value) => TryWrap(prim, OpenUsdNativePhysicsSchemaKind.Joint, out value);
    public static UsdPhysicsJoint Wrap(UsdPrim prim) => new(UsdPhysicsSchema.Validate(prim, OpenUsdNativePhysicsSchemaKind.Joint, nameof(UsdPhysicsJoint)), prim.Path);
    internal static bool TryWrap(UsdPrim prim, OpenUsdNativePhysicsSchemaKind kind, out UsdPhysicsJoint value)
    {
        if (UsdPhysicsSchema.TryValidate(prim, kind, out UsdStage? stage))
        { value = new(stage!, prim.Path); return true; }
        value = default;
        return false;
    }
    internal UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
