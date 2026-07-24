// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Interop;

/// <summary>Identifies the supported native UsdShade value types.</summary>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "The names mirror OpenUSD's Sdf value type terminology.")]
internal enum OpenUsdNativeShadeValueType
{
    /// <summary>An invalid or unsupported value type.</summary>
    Invalid = 0,
    /// <summary>A scalar float.</summary>
    Float = 1,
    /// <summary>A role-bearing color3f.</summary>
    Color3f = 2,
    /// <summary>A role-bearing vector3f.</summary>
    Vector3f = 3,
    /// <summary>A role-bearing normal3f.</summary>
    Normal3f = 4,
    /// <summary>A token.</summary>
    Token = 5,
    /// <summary>A string.</summary>
    String = 6,
    /// <summary>An asset path.</summary>
    Asset = 7,
    /// <summary>A roleless three-component float tuple.</summary>
    Float3 = 8
}

/// <summary>Identifies a UsdShade input or output.</summary>
internal enum OpenUsdNativeShadeAttributeType
{
    /// <summary>An invalid shading property kind.</summary>
    Invalid = 0,
    /// <summary>A shading input.</summary>
    Input = 1,
    /// <summary>A shading output.</summary>
    Output = 2
}

/// <summary>Describes one connected shading source.</summary>
internal readonly record struct OpenUsdNativeShadeConnection(
    string SourcePrimPath,
    string SourceName,
    OpenUsdNativeShadeAttributeType SourceType);

public static unsafe partial class OpenUsdNativeRuntime
{
    internal static bool IsShadeMaterial(OpenUsdNativeStage stage, string primPath) =>
        GetStagePrimBool(stage, primPath, NativeMethods.ShadeIsMaterial);

    internal static bool IsShadeShader(OpenUsdNativeStage stage, string primPath) =>
        GetStagePrimBool(stage, primPath, NativeMethods.ShadeIsShader);

    internal static void DefineShadeMaterial(OpenUsdNativeStage stage, string primPath) =>
        InvokeStagePrimAction(stage, primPath, NativeMethods.ShadeDefineMaterial);

    internal static void DefineShadeShader(OpenUsdNativeStage stage, string primPath) =>
        InvokeStagePrimAction(stage, primPath, NativeMethods.ShadeDefineShader);

    internal static void SetShaderSourceId(
        OpenUsdNativeStage stage,
        string shaderPath,
        string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        InvokeShadeAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.ShadeShaderSetSourceId(handle, shaderPath, sourceId, ref error));
    }

    internal static string GetShaderSourceId(
        OpenUsdNativeStage stage,
        string shaderPath) =>
        GetStagePrimString(stage, shaderPath, NativeMethods.ShadeShaderGetSourceId);

    internal static void CreateShadeInput(
        OpenUsdNativeStage stage,
        string connectablePath,
        string inputName,
        OpenUsdNativeShadeValueType valueType)
    {
        ValidateShadeProperty(connectablePath, inputName);
        InvokeShadeAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.ShadeCreateInput(
                    handle,
                    connectablePath,
                    inputName,
                    (int)valueType,
                    ref error));
    }

    internal static OpenUsdNativeShadeValueType GetShadeInputType(
        OpenUsdNativeStage stage,
        string connectablePath,
        string inputName)
    {
        ValidateShadeProperty(connectablePath, inputName);
        return (OpenUsdNativeShadeValueType)GetShadeInt(
            stage,
            (nint handle, out int value, ref NativeErrorBuffer error) =>
                NativeMethods.ShadeGetInputType(
                    handle,
                    connectablePath,
                    inputName,
                    out value,
                    ref error));
    }

    internal static void SetShadeInputFloat(
        OpenUsdNativeStage stage,
        string shaderPath,
        string inputName,
        float value)
    {
        ValidateShadeProperty(shaderPath, inputName);
        InvokeShadeAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.ShadeSetInputFloat(
                    handle,
                    shaderPath,
                    inputName,
                    value,
                    ref error));
    }

    internal static float GetShadeInputFloat(
        OpenUsdNativeStage stage,
        string shaderPath,
        string inputName)
    {
        ValidateShadeProperty(shaderPath, inputName);
        return GetShadeFloat(
            stage,
            (nint handle, out float value, ref NativeErrorBuffer error) =>
                NativeMethods.ShadeGetInputFloat(
                    handle,
                    shaderPath,
                    inputName,
                    out value,
                    ref error));
    }

    internal static void SetShadeInputVec3f(
        OpenUsdNativeStage stage,
        string shaderPath,
        string inputName,
        OpenUsdNativeShadeValueType valueType,
        OpenUsdNativeVec3f value)
    {
        ValidateShadeProperty(shaderPath, inputName);
        InvokeShadeAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.ShadeSetInputVec3f(
                    handle,
                    shaderPath,
                    inputName,
                    (int)valueType,
                    value,
                    ref error));
    }

    internal static OpenUsdNativeVec3f GetShadeInputVec3f(
        OpenUsdNativeStage stage,
        string shaderPath,
        string inputName,
        OpenUsdNativeShadeValueType valueType)
    {
        ValidateShadeProperty(shaderPath, inputName);
        return GetShadeVec3f(
            stage,
            (nint handle, out OpenUsdNativeVec3f value, ref NativeErrorBuffer error) =>
                NativeMethods.ShadeGetInputVec3f(
                    handle,
                    shaderPath,
                    inputName,
                    (int)valueType,
                    out value,
                    ref error));
    }

    internal static void SetShadeInputString(
        OpenUsdNativeStage stage,
        string shaderPath,
        string inputName,
        OpenUsdNativeShadeValueType valueType,
        string value)
    {
        ValidateShadeProperty(shaderPath, inputName);
        ArgumentNullException.ThrowIfNull(value);
        InvokeShadeAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.ShadeSetInputString(
                    handle,
                    shaderPath,
                    inputName,
                    (int)valueType,
                    value,
                    ref error));
    }

    internal static string GetShadeInputString(
        OpenUsdNativeStage stage,
        string shaderPath,
        string inputName,
        OpenUsdNativeShadeValueType valueType)
    {
        ValidateShadeProperty(shaderPath, inputName);
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        nint handle = lease.Handle;
        return GetString((byte* buffer, nuint capacity, out nuint required) =>
        {
            Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
            fixed (byte* errorPointer = errorBytes)
            {
                var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
                OpenUsdNativeStatus status = NativeMethods.ShadeGetInputString(
                    handle,
                    shaderPath,
                    inputName,
                    (int)valueType,
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

    internal static void CreateShadeOutput(
        OpenUsdNativeStage stage,
        string connectablePath,
        string outputName,
        OpenUsdNativeShadeValueType valueType)
    {
        ValidateShadeProperty(connectablePath, outputName);
        InvokeShadeAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.ShadeCreateOutput(
                    handle,
                    connectablePath,
                    outputName,
                    (int)valueType,
                    ref error));
    }

    internal static OpenUsdNativeShadeValueType GetShadeOutputType(
        OpenUsdNativeStage stage,
        string connectablePath,
        string outputName)
    {
        ValidateShadeProperty(connectablePath, outputName);
        return (OpenUsdNativeShadeValueType)GetShadeInt(
            stage,
            (nint handle, out int value, ref NativeErrorBuffer error) =>
                NativeMethods.ShadeGetOutputType(
                    handle,
                    connectablePath,
                    outputName,
                    out value,
                    ref error));
    }

    internal static void ConnectShade(
        OpenUsdNativeStage stage,
        string destinationPath,
        string destinationName,
        OpenUsdNativeShadeAttributeType destinationType,
        string sourcePath,
        string sourceName,
        OpenUsdNativeShadeAttributeType sourceType)
    {
        ValidateShadeProperty(destinationPath, destinationName);
        ValidateShadeProperty(sourcePath, sourceName);
        InvokeShadeAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.ShadeConnect(
                    handle,
                    destinationPath,
                    destinationName,
                    (int)destinationType,
                    sourcePath,
                    sourceName,
                    (int)sourceType,
                    ref error));
    }

    internal static void DisconnectShade(
        OpenUsdNativeStage stage,
        string destinationPath,
        string destinationName,
        OpenUsdNativeShadeAttributeType destinationType)
    {
        ValidateShadeProperty(destinationPath, destinationName);
        InvokeShadeAction(
            stage,
            (nint handle, ref NativeErrorBuffer error) =>
                NativeMethods.ShadeDisconnect(
                    handle,
                    destinationPath,
                    destinationName,
                    (int)destinationType,
                    ref error));
    }

    internal static OpenUsdNativeShadeConnection GetConnectedShadeSource(
        OpenUsdNativeStage stage,
        string destinationPath,
        string destinationName,
        OpenUsdNativeShadeAttributeType destinationType)
    {
        OpenUsdNativeShadeConnection[] sources =
            GetConnectedShadeSources(stage, destinationPath, destinationName, destinationType);
        if (sources.Length != 1)
        {
            throw new OpenUsdNativeException(
                OpenUsdNativeStatus.InvalidArgument,
                "The shading property does not have exactly one connected source.");
        }
        return sources[0];
    }

    internal static OpenUsdNativeShadeConnection[] GetConnectedShadeSources(
        OpenUsdNativeStage stage,
        string destinationPath,
        string destinationName,
        OpenUsdNativeShadeAttributeType destinationType)
    {
        ValidateShadeProperty(destinationPath, destinationName);
        ArgumentNullException.ThrowIfNull(stage);
        using var lease = new SafeHandleLease(stage);
        var view = new NativeStringListView
        {
            StructSize = (uint)sizeof(NativeStringListView)
        };
        nint list = 0;
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.ShadeGetConnectedSources(
                lease.Handle,
                destinationPath,
                destinationName,
                (int)destinationType,
                out list,
                ref view,
                ref error);
            ThrowIfFailedAndReleaseStringList(status, errorBytes, error, ref list);
        }

        try
        {
            string[] values = DecodeStringListView(view);
            return DecodeConnectedShadeSources(values);
        }
        finally
        {
            if (list != 0)
            {
                NativeMethods.StringListRelease(list);
            }
        }
    }

    internal static OpenUsdNativeShadeConnection[] DecodeConnectedShadeSources(
        ReadOnlySpan<string> values)
    {
        if (values.IsEmpty || values.Length % 3 != 0)
        {
            throw InvalidConnectedSource("the field count is not a non-zero multiple of three");
        }

        var sources = new OpenUsdNativeShadeConnection[values.Length / 3];
        for (int i = 0; i < sources.Length; i++)
        {
            int field = i * 3;
            string sourcePrimPath = values[field];
            string sourceName = values[field + 1];
            if (!NativeStringValidation.IsValidAbsolutePrimPath(sourcePrimPath))
            {
                throw InvalidConnectedSource("a source prim path is not absolute and valid");
            }
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                throw InvalidConnectedSource("a source property name is empty");
            }
            if (!int.TryParse(
                values[field + 2],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int sourceType) ||
                sourceType is < (int)OpenUsdNativeShadeAttributeType.Input
                    or > (int)OpenUsdNativeShadeAttributeType.Output)
            {
                throw InvalidConnectedSource("a source property type is invalid");
            }
            sources[i] = new OpenUsdNativeShadeConnection(
                sourcePrimPath,
                sourceName,
                (OpenUsdNativeShadeAttributeType)sourceType);
        }
        return sources;
    }

    internal static void CreateMaterialSurfaceOutput(
        OpenUsdNativeStage stage,
        string materialPath) =>
        InvokeStagePrimAction(
            stage,
            materialPath,
            NativeMethods.ShadeMaterialCreateSurfaceOutput);

    internal static void BindMaterial(
        OpenUsdNativeStage stage,
        string primPath,
        string materialPath) =>
        InvokeStagePrimPairAction(
            stage,
            primPath,
            materialPath,
            NativeMethods.ShadeMaterialBind);

    internal static void UnbindMaterial(OpenUsdNativeStage stage, string primPath) =>
        InvokeStagePrimAction(stage, primPath, NativeMethods.ShadeMaterialUnbind);

    internal static string GetDirectMaterialPath(
        OpenUsdNativeStage stage,
        string primPath) =>
        GetStagePrimString(stage, primPath, NativeMethods.ShadeGetDirectMaterial);

    private static void ValidateShadeProperty(string primPath, string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
    }

    private static void InvokeShadeAction(
        OpenUsdNativeStage stage,
        NativeShadeAction action)
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

    private static int GetShadeInt(OpenUsdNativeStage stage, NativeShadeIntGetter getter)
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

    private static float GetShadeFloat(
        OpenUsdNativeStage stage,
        NativeShadeFloatGetter getter)
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

    private static OpenUsdNativeVec3f GetShadeVec3f(
        OpenUsdNativeStage stage,
        NativeShadeVec3fGetter getter)
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

    private static OpenUsdNativeException InvalidConnectedSource(string detail) =>
        new(
            OpenUsdNativeStatus.NativeError,
            $"The native runtime returned an invalid connected-source record: {detail}.");

    private delegate OpenUsdNativeStatus NativeShadeAction(
        nint stage,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeShadeIntGetter(
        nint stage,
        out int value,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeShadeFloatGetter(
        nint stage,
        out float value,
        ref NativeErrorBuffer error);

    private delegate OpenUsdNativeStatus NativeShadeVec3fGetter(
        nint stage,
        out OpenUsdNativeVec3f value,
        ref NativeErrorBuffer error);
}
