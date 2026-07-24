// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Interop;

/// <summary>Identifies supported concrete UsdLux schemas.</summary>
internal enum OpenUsdNativeLuxSchemaKind
{
    /// <summary>A distant light.</summary>
    DistantLight = 0,
    /// <summary>A sphere light.</summary>
    SphereLight = 1,
    /// <summary>A rectangular light.</summary>
    RectLight = 2,
    /// <summary>A disk light.</summary>
    DiskLight = 3,
    /// <summary>A dome light.</summary>
    DomeLight = 4,
    /// <summary>A cylinder light.</summary>
    CylinderLight = 5
}

/// <summary>Identifies shared scalar UsdLux inputs.</summary>
internal enum OpenUsdNativeLuxFloatProperty
{
    /// <summary>Light intensity.</summary>
    Intensity = 0,
    /// <summary>Light exposure.</summary>
    Exposure = 1,
    /// <summary>Diffuse contribution multiplier.</summary>
    Diffuse = 2,
    /// <summary>Specular contribution multiplier.</summary>
    Specular = 3,
    /// <summary>Color temperature in kelvins.</summary>
    ColorTemperature = 4
}

/// <summary>Identifies shared boolean UsdLux inputs.</summary>
internal enum OpenUsdNativeLuxBoolProperty
{
    /// <summary>Whether color temperature affects the light color.</summary>
    EnableColorTemperature = 0,
    /// <summary>Whether power is normalized by light size.</summary>
    Normalize = 1
}

/// <summary>Identifies concrete-light shape inputs.</summary>
internal enum OpenUsdNativeLuxShapeProperty
{
    /// <summary>Distant-light angular diameter.</summary>
    Angle = 0,
    /// <summary>Sphere, disk, or cylinder radius.</summary>
    Radius = 1,
    /// <summary>Rect-light width.</summary>
    Width = 2,
    /// <summary>Rect-light height.</summary>
    Height = 3,
    /// <summary>Cylinder-light length.</summary>
    Length = 4
}

/// <summary>Identifies supported light asset inputs.</summary>
internal enum OpenUsdNativeLuxAssetProperty
{
    /// <summary>Rect- or dome-light texture file.</summary>
    TextureFile = 0
}

/// <summary>Identifies focused UsdLuxShapingAPI inputs.</summary>
internal enum OpenUsdNativeLuxShapingProperty
{
    /// <summary>Emission focus exponent.</summary>
    Focus = 0,
    /// <summary>Cone cutoff angle.</summary>
    ConeAngle = 1,
    /// <summary>Cone-edge softness.</summary>
    ConeSoftness = 2
}

public static unsafe partial class OpenUsdNativeRuntime
{
    internal static bool IsLuxSchema(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeLuxSchemaKind schemaKind) =>
        GetLuxInt(
            stage,
            (nint handle, out int value, ref NativeErrorBuffer error) =>
                NativeMethods.LuxIsSchema(
                    handle,
                    primPath,
                    (int)schemaKind,
                    out value,
                    ref error)) != 0;

    internal static void DefineLux(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeLuxSchemaKind schemaKind) =>
        InvokeLuxAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.LuxDefine(handle, primPath, (int)schemaKind, ref error));

    internal static void SetLuxFloat(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeLuxFloatProperty property,
        float value) =>
        InvokeLuxAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.LuxSetFloat(handle, primPath, (int)property, value, ref error));

    internal static float GetLuxFloat(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeLuxFloatProperty property) =>
        GetLuxFloat(
            stage,
            (nint handle, out float value, ref NativeErrorBuffer error) =>
                NativeMethods.LuxGetFloat(
                    handle,
                    primPath,
                    (int)property,
                    out value,
                    ref error));

    internal static void SetLuxBool(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeLuxBoolProperty property,
        bool value) =>
        InvokeLuxAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.LuxSetBool(
                    handle,
                    primPath,
                    (int)property,
                    value ? 1 : 0,
                    ref error));

    internal static bool GetLuxBool(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeLuxBoolProperty property) =>
        GetLuxInt(
            stage,
            (nint handle, out int value, ref NativeErrorBuffer error) =>
                NativeMethods.LuxGetBool(
                    handle,
                    primPath,
                    (int)property,
                    out value,
                    ref error)) != 0;

    internal static void SetLuxColor(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeVec3f value) =>
        InvokeLuxAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.LuxSetColor(handle, primPath, value, ref error));

    internal static OpenUsdNativeVec3f GetLuxColor(
        OpenUsdNativeStage stage,
        string primPath) =>
        GetLuxVec3f(
            stage,
            (nint handle, out OpenUsdNativeVec3f value, ref NativeErrorBuffer error) =>
                NativeMethods.LuxGetColor(handle, primPath, out value, ref error));

    internal static void SetLuxShape(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeLuxShapeProperty property,
        float value) =>
        InvokeLuxAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.LuxSetShape(handle, primPath, (int)property, value, ref error));

    internal static float GetLuxShape(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeLuxShapeProperty property) =>
        GetLuxFloat(
            stage,
            (nint handle, out float value, ref NativeErrorBuffer error) =>
                NativeMethods.LuxGetShape(
                    handle,
                    primPath,
                    (int)property,
                    out value,
                    ref error));

    internal static void SetLuxAsset(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeLuxAssetProperty property,
        string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        InvokeLuxAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.LuxSetAsset(handle, primPath, (int)property, value, ref error));
    }

    internal static string GetLuxAsset(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeLuxAssetProperty property)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        using var lease = new SafeHandleLease(stage);
        nint handle = lease.Handle;
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.LuxGetAsset(
                    handle,
                    primPath,
                    (int)property,
                    buffer,
                    capacity,
                    out required,
                    ref error);
                if (status != OpenUsdNativeStatus.BufferTooSmall)
                {
                    ThrowIfFailed(status, errorBytes, error);
                }
                return status;
            }
        });
    }

    internal static bool HasLuxShaping(OpenUsdNativeStage stage, string primPath) =>
        GetStagePrimBool(stage, primPath, NativeMethods.LuxHasShaping);

    internal static void ApplyLuxShaping(OpenUsdNativeStage stage, string primPath) =>
        InvokeStagePrimAction(stage, primPath, NativeMethods.LuxApplyShaping);

    internal static void SetLuxShaping(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeLuxShapingProperty property,
        float value) =>
        InvokeLuxAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.LuxSetShaping(
                    handle,
                    primPath,
                    (int)property,
                    value,
                    ref error));

    internal static float GetLuxShaping(
        OpenUsdNativeStage stage,
        string primPath,
        OpenUsdNativeLuxShapingProperty property) =>
        GetLuxFloat(
            stage,
            (nint handle, out float value, ref NativeErrorBuffer error) =>
                NativeMethods.LuxGetShaping(
                    handle,
                    primPath,
                    (int)property,
                    out value,
                    ref error));

    private static void InvokeLuxAction(OpenUsdNativeStage stage, NativeLuxAction action)
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

    private static int GetLuxInt(OpenUsdNativeStage stage, NativeLuxIntGetter getter)
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

    private static float GetLuxFloat(OpenUsdNativeStage stage, NativeLuxFloatGetter getter)
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

    private static OpenUsdNativeVec3f GetLuxVec3f(
        OpenUsdNativeStage stage,
        NativeLuxVec3fGetter getter)
    {
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status =
                getter(lease.Handle, out OpenUsdNativeVec3f value, ref error);
            ThrowIfFailed(status, errorBytes, error);
            return value;
        }
    }

    private delegate OpenUsdNativeStatus NativeLuxAction(
        nint stage,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeLuxIntGetter(
        nint stage,
        out int value,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeLuxFloatGetter(
        nint stage,
        out float value,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeLuxVec3fGetter(
        nint stage,
        out OpenUsdNativeVec3f value,
        ref NativeErrorBuffer error);
}
