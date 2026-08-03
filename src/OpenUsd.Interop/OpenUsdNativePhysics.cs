// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Interop;

internal enum OpenUsdNativePhysicsSchemaKind
{
    Scene = 0,
    CollisionGroup = 1,
    Joint = 2,
    RevoluteJoint = 3,
    PrismaticJoint = 4,
    SphericalJoint = 5,
    DistanceJoint = 6,
    FixedJoint = 7
}

internal enum OpenUsdNativePhysicsApiKind
{
    RigidBody = 0,
    Mass = 1,
    Collision = 2,
    MeshCollision = 3,
    Material = 4,
    FilteredPairs = 5,
    ArticulationRoot = 6,
    Limit = 7,
    Drive = 8
}

internal enum OpenUsdNativePhysicsFloatProperty
{
    SceneGravityMagnitude = 0,
    MassMass = 1,
    MassDensity = 2,
    MaterialDynamicFriction = 3,
    MaterialStaticFriction = 4,
    MaterialRestitution = 5,
    MaterialDensity = 6,
    JointBreakForce = 7,
    JointBreakTorque = 8,
    RevoluteLowerLimit = 9,
    RevoluteUpperLimit = 10,
    PrismaticLowerLimit = 11,
    PrismaticUpperLimit = 12,
    SphericalConeAngle0Limit = 13,
    SphericalConeAngle1Limit = 14,
    DistanceMinDistance = 15,
    DistanceMaxDistance = 16,
    LimitLow = 17,
    LimitHigh = 18,
    DriveMaxForce = 19,
    DriveTargetPosition = 20,
    DriveTargetVelocity = 21,
    DriveDamping = 22,
    DriveStiffness = 23
}

internal enum OpenUsdNativePhysicsBoolProperty
{
    RigidBodyEnabled = 0,
    RigidBodyKinematicEnabled = 1,
    RigidBodyStartsAsleep = 2,
    CollisionEnabled = 3,
    CollisionGroupInvertFilteredGroups = 4,
    JointEnabled = 5,
    JointCollisionEnabled = 6,
    JointExcludeFromArticulation = 7
}

internal enum OpenUsdNativePhysicsVec3fProperty
{
    SceneGravityDirection = 0,
    RigidBodyVelocity = 1,
    RigidBodyAngularVelocity = 2,
    MassCenterOfMass = 3,
    MassDiagonalInertia = 4,
    JointLocalPos0 = 5,
    JointLocalPos1 = 6
}

internal enum OpenUsdNativePhysicsQuatfProperty
{
    MassPrincipalAxes = 0,
    JointLocalRot0 = 1,
    JointLocalRot1 = 2
}

internal enum OpenUsdNativePhysicsTokenProperty
{
    MeshCollisionApproximation = 0,
    RevoluteAxis = 1,
    PrismaticAxis = 2,
    SphericalAxis = 3,
    DriveType = 4
}

internal enum OpenUsdNativePhysicsStringProperty
{
    CollisionGroupMergeGroupName = 0
}

public static unsafe partial class OpenUsdNativeRuntime
{
    internal static bool IsPhysicsSchema(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativePhysicsSchemaKind schemaKind) =>
        GetPhysicsInt(
            stage,
            (nint handle, out int value, ref NativeErrorBuffer error) =>
                NativeMethods.PhysicsIsSchema(handle, primPath, (int)schemaKind, out value, ref error)) != 0;

    internal static void DefinePhysics(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativePhysicsSchemaKind schemaKind) =>
        InvokePhysics(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.PhysicsDefine(handle, primPath, (int)schemaKind, ref error));

    internal static bool HasPhysicsApi(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativePhysicsApiKind apiKind,
        string? instanceName) =>
        GetPhysicsInt(
            stage,
            (nint handle, out int value, ref NativeErrorBuffer error) =>
                NativeMethods.PhysicsHasApi(
                    handle,
                    primPath,
                    (int)apiKind,
                    instanceName ?? string.Empty,
                    out value,
                    ref error)) != 0;

    internal static void ApplyPhysicsApi(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativePhysicsApiKind apiKind,
        string? instanceName) =>
        InvokePhysics(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.PhysicsApplyApi(handle, primPath, (int)apiKind, instanceName ?? string.Empty, ref error));

    internal static void SetPhysicsFloat(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativePhysicsFloatProperty property,
        float value,
        string? instanceName) =>
        InvokePhysics(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.PhysicsSetFloat(
                    handle,
                    primPath,
                    (int)property,
                    instanceName ?? string.Empty,
                    value,
                    ref error));

    internal static float GetPhysicsFloat(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativePhysicsFloatProperty property,
        string? instanceName) =>
        GetPhysicsFloat(
            stage,
            (nint handle, out float value, ref NativeErrorBuffer error) =>
                NativeMethods.PhysicsGetFloat(
                    handle,
                    primPath,
                    (int)property,
                    instanceName ?? string.Empty,
                    out value,
                    ref error));

    internal static void SetPhysicsBool(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativePhysicsBoolProperty property,
        bool value) =>
        InvokePhysics(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.PhysicsSetBool(handle, primPath, (int)property, value ? 1 : 0, ref error));

    internal static bool GetPhysicsBool(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativePhysicsBoolProperty property) =>
        GetPhysicsInt(
            stage,
            (nint handle, out int value, ref NativeErrorBuffer error) =>
                NativeMethods.PhysicsGetBool(handle, primPath, (int)property, out value, ref error)) != 0;

    internal static void SetPhysicsVec3f(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativePhysicsVec3fProperty property,
        OpenUsdNativeVec3f value) =>
        InvokePhysics(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.PhysicsSetVec3f(handle, primPath, (int)property, value, ref error));

    internal static OpenUsdNativeVec3f GetPhysicsVec3f(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativePhysicsVec3fProperty property) =>
        GetPhysicsVec3f(
            stage,
            (nint handle, out OpenUsdNativeVec3f value, ref NativeErrorBuffer error) =>
                NativeMethods.PhysicsGetVec3f(handle, primPath, (int)property, out value, ref error));

    internal static void SetPhysicsQuatf(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativePhysicsQuatfProperty property,
        OpenUsdNativeQuatf value) =>
        InvokePhysics(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.PhysicsSetQuatf(handle, primPath, (int)property, value, ref error));

    internal static OpenUsdNativeQuatf GetPhysicsQuatf(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativePhysicsQuatfProperty property) =>
        GetPhysicsQuatf(
            stage,
            (nint handle, out OpenUsdNativeQuatf value, ref NativeErrorBuffer error) =>
                NativeMethods.PhysicsGetQuatf(handle, primPath, (int)property, out value, ref error));

    internal static void SetPhysicsToken(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativePhysicsTokenProperty property,
        string value,
        string? instanceName) =>
        InvokePhysics(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.PhysicsSetToken(
                    handle,
                    primPath,
                    (int)property,
                    instanceName ?? string.Empty,
                    value,
                    ref error));

    internal static string GetPhysicsToken(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativePhysicsTokenProperty property,
        string? instanceName) =>
        GetPhysicsString(
            stage,
            (nint handle, byte* buffer, nuint capacity, out nuint required, ref NativeErrorBuffer error) =>
                NativeMethods.PhysicsGetToken(
                    handle,
                    primPath,
                    (int)property,
                    instanceName ?? string.Empty,
                    buffer,
                    capacity,
                    out required,
                    ref error));

    internal static void SetPhysicsString(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativePhysicsStringProperty property,
        string value) =>
        InvokePhysics(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.PhysicsSetString(handle, primPath, (int)property, value, ref error));

    internal static string GetPhysicsString(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativePhysicsStringProperty property) =>
        GetPhysicsString(
            stage,
            (nint handle, byte* buffer, nuint capacity, out nuint required, ref NativeErrorBuffer error) =>
                NativeMethods.PhysicsGetString(
                    handle,
                    primPath,
                    (int)property,
                    buffer,
                    capacity,
                    out required,
                    ref error));

    private static void InvokePhysics(OpenUsdNativeStage stage, NativePhysicsAction action)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = action(lease.Handle, ref error);
            ThrowIfFailed(status, errorBytes, error);
        }
    }

    private static int GetPhysicsInt(OpenUsdNativeStage stage, NativePhysicsIntGetter getter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(lease.Handle, out int value, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    private static float GetPhysicsFloat(OpenUsdNativeStage stage, NativePhysicsFloatGetter getter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(lease.Handle, out float value, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    private static OpenUsdNativeVec3f GetPhysicsVec3f(
        OpenUsdNativeStage stage,
        NativePhysicsVec3fGetter getter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(lease.Handle, out OpenUsdNativeVec3f value, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    private static OpenUsdNativeQuatf GetPhysicsQuatf(
        OpenUsdNativeStage stage,
        NativePhysicsQuatfGetter getter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = getter(lease.Handle, out OpenUsdNativeQuatf value, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    private static string GetPhysicsString(OpenUsdNativeStage stage, NativePhysicsStringGetter getter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        nint handle = lease.Handle;
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = getter(handle, buffer, capacity, out required, ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall)
                {
                    ThrowIfFailed(status, errorBytes, error);
                }
                return status;
            }
        });
    }

    private delegate OpenUsdNativeStatus NativePhysicsAction(nint stage, ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativePhysicsIntGetter(
        nint stage,
        out int value,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativePhysicsFloatGetter(
        nint stage,
        out float value,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativePhysicsVec3fGetter(
        nint stage,
        out OpenUsdNativeVec3f value,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativePhysicsQuatfGetter(
        nint stage,
        out OpenUsdNativeQuatf value,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativePhysicsStringGetter(
        nint stage,
        byte* buffer,
        nuint capacity,
        out nuint required,
        ref NativeErrorBuffer error);
}
