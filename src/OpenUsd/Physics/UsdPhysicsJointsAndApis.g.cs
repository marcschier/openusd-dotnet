// Copyright (c) marcschier. Licensed under the MIT License.
#nullable enable


using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Physics;

#pragma warning disable CS1591

public readonly struct UsdPhysicsRevoluteJoint
{
    private readonly UsdPhysicsJoint _joint;
    internal UsdPhysicsRevoluteJoint(UsdStage stage, string path) => _joint = new(stage, path);
    public UsdPhysicsJoint Joint => _joint;
    public string Path => _joint.Path;
    public UsdPhysicsAxis Axis { get => UsdPhysicsTokens.ToAxis(_joint.Stage.Native.GetPhysicsToken(Path, OpenUsdNativePhysicsTokenProperty.RevoluteAxis)); set => _joint.Stage.Native.SetPhysicsToken(Path, OpenUsdNativePhysicsTokenProperty.RevoluteAxis, UsdPhysicsTokens.ToToken(value)); }
    public float LowerLimit { get => _joint.GetFloat(OpenUsdNativePhysicsFloatProperty.RevoluteLowerLimit); set => _joint.SetFloat(OpenUsdNativePhysicsFloatProperty.RevoluteLowerLimit, value); }
    public float UpperLimit { get => _joint.GetFloat(OpenUsdNativePhysicsFloatProperty.RevoluteUpperLimit); set => _joint.SetFloat(OpenUsdNativePhysicsFloatProperty.RevoluteUpperLimit, value); }
    public static bool TryWrap(UsdPrim prim, out UsdPhysicsRevoluteJoint value) { if (UsdPhysicsJoint.TryWrap(prim, OpenUsdNativePhysicsSchemaKind.RevoluteJoint, out UsdPhysicsJoint joint)) { value = new(joint.Stage, prim.Path); return true; } value = default; return false; }
    public static UsdPhysicsRevoluteJoint Wrap(UsdPrim prim) => new(UsdPhysicsSchema.Validate(prim, OpenUsdNativePhysicsSchemaKind.RevoluteJoint, nameof(UsdPhysicsRevoluteJoint)), prim.Path);
}

public readonly struct UsdPhysicsPrismaticJoint
{
    private readonly UsdPhysicsJoint _joint;
    internal UsdPhysicsPrismaticJoint(UsdStage stage, string path) => _joint = new(stage, path);
    public UsdPhysicsJoint Joint => _joint;
    public string Path => _joint.Path;
    public UsdPhysicsAxis Axis { get => UsdPhysicsTokens.ToAxis(_joint.Stage.Native.GetPhysicsToken(Path, OpenUsdNativePhysicsTokenProperty.PrismaticAxis)); set => _joint.Stage.Native.SetPhysicsToken(Path, OpenUsdNativePhysicsTokenProperty.PrismaticAxis, UsdPhysicsTokens.ToToken(value)); }
    public float LowerLimit { get => _joint.GetFloat(OpenUsdNativePhysicsFloatProperty.PrismaticLowerLimit); set => _joint.SetFloat(OpenUsdNativePhysicsFloatProperty.PrismaticLowerLimit, value); }
    public float UpperLimit { get => _joint.GetFloat(OpenUsdNativePhysicsFloatProperty.PrismaticUpperLimit); set => _joint.SetFloat(OpenUsdNativePhysicsFloatProperty.PrismaticUpperLimit, value); }
    public static bool TryWrap(UsdPrim prim, out UsdPhysicsPrismaticJoint value) { if (UsdPhysicsJoint.TryWrap(prim, OpenUsdNativePhysicsSchemaKind.PrismaticJoint, out UsdPhysicsJoint joint)) { value = new(joint.Stage, prim.Path); return true; } value = default; return false; }
    public static UsdPhysicsPrismaticJoint Wrap(UsdPrim prim) => new(UsdPhysicsSchema.Validate(prim, OpenUsdNativePhysicsSchemaKind.PrismaticJoint, nameof(UsdPhysicsPrismaticJoint)), prim.Path);
}

public readonly struct UsdPhysicsSphericalJoint
{
    private readonly UsdPhysicsJoint _joint;
    internal UsdPhysicsSphericalJoint(UsdStage stage, string path) => _joint = new(stage, path);
    public UsdPhysicsJoint Joint => _joint;
    public string Path => _joint.Path;
    public UsdPhysicsAxis Axis { get => UsdPhysicsTokens.ToAxis(_joint.Stage.Native.GetPhysicsToken(Path, OpenUsdNativePhysicsTokenProperty.SphericalAxis)); set => _joint.Stage.Native.SetPhysicsToken(Path, OpenUsdNativePhysicsTokenProperty.SphericalAxis, UsdPhysicsTokens.ToToken(value)); }
    public float ConeAngle0Limit { get => _joint.GetFloat(OpenUsdNativePhysicsFloatProperty.SphericalConeAngle0Limit); set => _joint.SetFloat(OpenUsdNativePhysicsFloatProperty.SphericalConeAngle0Limit, value); }
    public float ConeAngle1Limit { get => _joint.GetFloat(OpenUsdNativePhysicsFloatProperty.SphericalConeAngle1Limit); set => _joint.SetFloat(OpenUsdNativePhysicsFloatProperty.SphericalConeAngle1Limit, value); }
    public static bool TryWrap(UsdPrim prim, out UsdPhysicsSphericalJoint value) { if (UsdPhysicsJoint.TryWrap(prim, OpenUsdNativePhysicsSchemaKind.SphericalJoint, out UsdPhysicsJoint joint)) { value = new(joint.Stage, prim.Path); return true; } value = default; return false; }
    public static UsdPhysicsSphericalJoint Wrap(UsdPrim prim) => new(UsdPhysicsSchema.Validate(prim, OpenUsdNativePhysicsSchemaKind.SphericalJoint, nameof(UsdPhysicsSphericalJoint)), prim.Path);
}

public readonly struct UsdPhysicsDistanceJoint
{
    private readonly UsdPhysicsJoint _joint;
    internal UsdPhysicsDistanceJoint(UsdStage stage, string path) => _joint = new(stage, path);
    public UsdPhysicsJoint Joint => _joint;
    public string Path => _joint.Path;
    public float MinDistance { get => _joint.GetFloat(OpenUsdNativePhysicsFloatProperty.DistanceMinDistance); set => _joint.SetFloat(OpenUsdNativePhysicsFloatProperty.DistanceMinDistance, value); }
    public float MaxDistance { get => _joint.GetFloat(OpenUsdNativePhysicsFloatProperty.DistanceMaxDistance); set => _joint.SetFloat(OpenUsdNativePhysicsFloatProperty.DistanceMaxDistance, value); }
    public static bool TryWrap(UsdPrim prim, out UsdPhysicsDistanceJoint value) { if (UsdPhysicsJoint.TryWrap(prim, OpenUsdNativePhysicsSchemaKind.DistanceJoint, out UsdPhysicsJoint joint)) { value = new(joint.Stage, prim.Path); return true; } value = default; return false; }
    public static UsdPhysicsDistanceJoint Wrap(UsdPrim prim) => new(UsdPhysicsSchema.Validate(prim, OpenUsdNativePhysicsSchemaKind.DistanceJoint, nameof(UsdPhysicsDistanceJoint)), prim.Path);
}

public readonly struct UsdPhysicsFixedJoint
{
    public UsdPhysicsFixedJoint(UsdStage stage, string path) => Joint = new(stage, path);
    public UsdPhysicsJoint Joint { get; }
    public string Path => Joint.Path;
    public static bool TryWrap(UsdPrim prim, out UsdPhysicsFixedJoint value) { if (UsdPhysicsJoint.TryWrap(prim, OpenUsdNativePhysicsSchemaKind.FixedJoint, out UsdPhysicsJoint joint)) { value = new(joint.Stage, prim.Path); return true; } value = default; return false; }
    public static UsdPhysicsFixedJoint Wrap(UsdPrim prim) => new(UsdPhysicsSchema.Validate(prim, OpenUsdNativePhysicsSchemaKind.FixedJoint, nameof(UsdPhysicsFixedJoint)), prim.Path);
}

[ExcludeFromCodeCoverage(Justification = "Exercised by native and NativeAOT integration probes.")]
public readonly struct UsdPhysicsRigidBodyAPI
{
    private readonly UsdStage? _stage;
    internal UsdPhysicsRigidBodyAPI(UsdStage stage, string path) { _stage = stage; Path = path; }
    public string Path { get; }
    public bool RigidBodyEnabled { get => Stage.Native.GetPhysicsBool(Path, OpenUsdNativePhysicsBoolProperty.RigidBodyEnabled); set => Stage.Native.SetPhysicsBool(Path, OpenUsdNativePhysicsBoolProperty.RigidBodyEnabled, value); }
    public bool KinematicEnabled { get => Stage.Native.GetPhysicsBool(Path, OpenUsdNativePhysicsBoolProperty.RigidBodyKinematicEnabled); set => Stage.Native.SetPhysicsBool(Path, OpenUsdNativePhysicsBoolProperty.RigidBodyKinematicEnabled, value); }
    public bool StartsAsleep { get => Stage.Native.GetPhysicsBool(Path, OpenUsdNativePhysicsBoolProperty.RigidBodyStartsAsleep); set => Stage.Native.SetPhysicsBool(Path, OpenUsdNativePhysicsBoolProperty.RigidBodyStartsAsleep, value); }
    public UsdVec3f Velocity { get => UsdVec3f.FromNative(Stage.Native.GetPhysicsVec3f(Path, OpenUsdNativePhysicsVec3fProperty.RigidBodyVelocity)); set => Stage.Native.SetPhysicsVec3f(Path, OpenUsdNativePhysicsVec3fProperty.RigidBodyVelocity, value.ToNative()); }
    public UsdVec3f AngularVelocity { get => UsdVec3f.FromNative(Stage.Native.GetPhysicsVec3f(Path, OpenUsdNativePhysicsVec3fProperty.RigidBodyAngularVelocity)); set => Stage.Native.SetPhysicsVec3f(Path, OpenUsdNativePhysicsVec3fProperty.RigidBodyAngularVelocity, value.ToNative()); }
    public void SetSimulationOwner(string target) => Prim.SetRelationshipTargets("physics:simulationOwner", [target]);
    public string[] GetSimulationOwner() => Prim.GetRelationshipTargets("physics:simulationOwner");
    public UsdPrim Prim => new(Stage, Path);
    public static bool Has(UsdPrim prim) => prim.OwningStage.Native.HasPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.RigidBody);
    public static UsdPhysicsRigidBodyAPI Apply(UsdPrim prim) { prim.OwningStage.Native.ApplyPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.RigidBody); return new(prim.OwningStage, prim.Path); }
    public static UsdPhysicsRigidBodyAPI Wrap(UsdPrim prim) => new(UsdPhysicsSchema.ValidateApi(prim, OpenUsdNativePhysicsApiKind.RigidBody, null, nameof(UsdPhysicsRigidBodyAPI)), prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}

public readonly struct UsdPhysicsMassAPI
{
    private readonly UsdStage? _stage;
    internal UsdPhysicsMassAPI(UsdStage stage, string path) { _stage = stage; Path = path; }
    public string Path { get; }
    public float Mass { get => F(OpenUsdNativePhysicsFloatProperty.MassMass); set => SF(OpenUsdNativePhysicsFloatProperty.MassMass, value); }
    public float Density { get => F(OpenUsdNativePhysicsFloatProperty.MassDensity); set => SF(OpenUsdNativePhysicsFloatProperty.MassDensity, value); }
    public UsdVec3f CenterOfMass { get => V(OpenUsdNativePhysicsVec3fProperty.MassCenterOfMass); set => SV(OpenUsdNativePhysicsVec3fProperty.MassCenterOfMass, value); }
    public UsdVec3f DiagonalInertia { get => V(OpenUsdNativePhysicsVec3fProperty.MassDiagonalInertia); set => SV(OpenUsdNativePhysicsVec3fProperty.MassDiagonalInertia, value); }
    public UsdQuatf PrincipalAxes { get => UsdQuatf.FromNative(Stage.Native.GetPhysicsQuatf(Path, OpenUsdNativePhysicsQuatfProperty.MassPrincipalAxes)); set => Stage.Native.SetPhysicsQuatf(Path, OpenUsdNativePhysicsQuatfProperty.MassPrincipalAxes, value.ToNative()); }
    public static bool Has(UsdPrim prim) => prim.OwningStage.Native.HasPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.Mass);
    public static UsdPhysicsMassAPI Apply(UsdPrim prim) { prim.OwningStage.Native.ApplyPhysicsApi(prim.Path, OpenUsdNativePhysicsApiKind.Mass); return new(prim.OwningStage, prim.Path); }
    public static UsdPhysicsMassAPI Wrap(UsdPrim prim) => new(UsdPhysicsSchema.ValidateApi(prim, OpenUsdNativePhysicsApiKind.Mass, null, nameof(UsdPhysicsMassAPI)), prim.Path);
    private float F(OpenUsdNativePhysicsFloatProperty p) => Stage.Native.GetPhysicsFloat(Path, p);
    private void SF(OpenUsdNativePhysicsFloatProperty p, float v) => Stage.Native.SetPhysicsFloat(Path, p, v);
    private UsdVec3f V(OpenUsdNativePhysicsVec3fProperty p) => UsdVec3f.FromNative(Stage.Native.GetPhysicsVec3f(Path, p));
    private void SV(OpenUsdNativePhysicsVec3fProperty p, UsdVec3f v) => Stage.Native.SetPhysicsVec3f(Path, p, v.ToNative());
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
