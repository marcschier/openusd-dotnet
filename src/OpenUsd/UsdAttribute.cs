// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd;

/// <summary>A lightweight attribute descriptor and value wrapper.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "UsdAttribute is the established OpenUSD property type name.")]
public readonly struct UsdAttribute : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdAttribute(UsdStage stage, string primPath, string name)
    {
        _stage = stage;
        PrimPath = primPath;
        Name = name;
    }

    /// <summary>Gets the owning prim path.</summary>
    public string PrimPath { get; }

    /// <summary>Gets the property name.</summary>
    public string Name { get; }

    /// <summary>Gets the declared USD type name.</summary>
    public string TypeName => Stage.Native.GetAttributeTypeName(PrimPath, Name);

    /// <summary>Gets whether the declared USD type is an array type.</summary>
    public bool IsArray => TypeName.EndsWith("[]", StringComparison.Ordinal);

    /// <summary>Gets whether this attribute has an authored value opinion.</summary>
    public bool IsAuthored => GetValueState().HasAuthoredValueOpinion;

    /// <summary>Gets authored and blocked value state at default time.</summary>
    public UsdAttributeValueState GetValueState() => ConvertState(
        Stage.Native.GetAttributeValueState(PrimPath, Name));

    /// <summary>Gets authored and blocked value state at a numeric time code.</summary>
    public UsdAttributeValueState GetValueState(double timeCode) => ConvertState(
        Stage.Native.GetAttributeValueState(PrimPath, Name, timeCode));

    /// <summary>Gets sorted authored time samples using one bulk native buffer.</summary>
    public double[] GetTimeSamples() => Stage.Native.GetAttributeTimeSamples(PrimPath, Name);

    /// <summary>Returns whether this attribute has an authored Ts spline value.</summary>
    public bool HasSpline() => Stage.Native.AttributeHasSpline(PrimPath, Name);

    /// <summary>Gets this attribute's authored Ts spline value.</summary>
    public TsSpline GetSpline() =>
        TsSpline.FromNativeHandle(Stage.Native.GetAttributeSpline(PrimPath, Name));

    /// <summary>Authors a Ts spline value on this attribute.</summary>
    public void SetSpline(TsSpline spline)
    {
        ArgumentNullException.ThrowIfNull(spline);
        Stage.Native.SetAttributeSpline(PrimPath, Name, spline.DangerousGetHandle());
    }

    /// <summary>Clears all authored values at the current edit target.</summary>
    public void ClearValue() => Stage.Native.ClearAttributeValue(PrimPath, Name);

    /// <summary>Blocks weaker values at the current edit target.</summary>
    public void BlockValue() => Stage.Native.BlockAttributeValue(PrimPath, Name);

    /// <summary>Gets an explicitly tagged supported scalar or array at default time.</summary>
    public UsdScalarValue GetValue() => GetValueCore(null);

    /// <summary>Gets an explicitly tagged supported scalar or array at a numeric time code.</summary>
    public UsdScalarValue GetValue(double timeCode) => GetValueCore(timeCode);

    /// <summary>Authors an explicitly tagged supported scalar or array at default time.</summary>
    public void Set(in UsdScalarValue value) => SetCore(value, null);

    /// <summary>Authors an explicitly tagged supported scalar or array at a numeric time code.</summary>
    public void Set(in UsdScalarValue value, double timeCode) => SetCore(value, timeCode);

    /// <summary>Attempts to author an explicitly tagged value on an existing compatible attribute.</summary>
    /// <remarks>
    /// Use <see cref="TrySet(in UsdScalarValue, out UsdAttributeTryFailureReason)"/> to observe
    /// why this method returned <see langword="false"/>.
    /// </remarks>
    public bool TrySet(in UsdScalarValue value) => TrySetCore(value, null);

    /// <summary>Attempts to author an explicitly tagged value on an existing compatible attribute.</summary>
    /// <param name="value">The explicitly tagged value to author.</param>
    /// <param name="failureReason">
    /// When this method returns <see langword="false"/>, receives the reason the value was not
    /// authored; otherwise receives <see cref="UsdAttributeTryFailureReason.None"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value was authored; otherwise, <see langword="false"/>.
    /// </returns>
    public bool TrySet(
        in UsdScalarValue value,
        out UsdAttributeTryFailureReason failureReason) =>
        TrySetCore(value, null, out failureReason);

    /// <summary>Attempts to author an explicitly tagged value on an existing compatible time sample.</summary>
    /// <remarks>
    /// Use <see cref="TrySet(in UsdScalarValue, double, out UsdAttributeTryFailureReason)"/> to
    /// observe why this method returned <see langword="false"/>.
    /// </remarks>
    public bool TrySet(in UsdScalarValue value, double timeCode) => TrySetCore(value, timeCode);

    /// <summary>Attempts to author an explicitly tagged value on an existing compatible time sample.</summary>
    /// <param name="value">The explicitly tagged value to author.</param>
    /// <param name="timeCode">The numeric time code to author.</param>
    /// <param name="failureReason">
    /// When this method returns <see langword="false"/>, receives the reason the value was not
    /// authored; otherwise receives <see cref="UsdAttributeTryFailureReason.None"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the value was authored; otherwise, <see langword="false"/>.
    /// </returns>
    public bool TrySet(
        in UsdScalarValue value,
        double timeCode,
        out UsdAttributeTryFailureReason failureReason) =>
        TrySetCore(value, timeCode, out failureReason);

    /// <summary>Attempts to read an explicitly tagged supported scalar or array at default time.</summary>
    /// <remarks>
    /// Use <see cref="TryGetValue(out UsdScalarValue, out UsdAttributeTryFailureReason)"/> to
    /// observe why this method returned <see langword="false"/>.
    /// </remarks>
    public bool TryGetValue(out UsdScalarValue value) => TryGetValueCore(null, out value);

    /// <summary>Attempts to read an explicitly tagged supported scalar or array at default time.</summary>
    /// <param name="value">
    /// When this method returns <see langword="true"/>, receives the attribute value; otherwise,
    /// receives the default value.
    /// </param>
    /// <param name="failureReason">
    /// When this method returns <see langword="false"/>, receives the reason no value was returned;
    /// otherwise receives <see cref="UsdAttributeTryFailureReason.None"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a value was read; otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryGetValue(
        out UsdScalarValue value,
        out UsdAttributeTryFailureReason failureReason) =>
        TryGetValueCore(null, out value, out failureReason);

    /// <summary>Attempts to read an explicitly tagged supported scalar or array at a numeric time code.</summary>
    /// <remarks>
    /// Use <see cref="TryGetValue(double, out UsdScalarValue, out UsdAttributeTryFailureReason)"/>
    /// to observe why this method returned <see langword="false"/>.
    /// </remarks>
    public bool TryGetValue(double timeCode, out UsdScalarValue value) => TryGetValueCore(timeCode, out value);

    /// <summary>Attempts to read an explicitly tagged supported scalar or array at a numeric time code.</summary>
    /// <param name="timeCode">The numeric time code to read.</param>
    /// <param name="value">
    /// When this method returns <see langword="true"/>, receives the attribute value; otherwise,
    /// receives the default value.
    /// </param>
    /// <param name="failureReason">
    /// When this method returns <see langword="false"/>, receives the reason no value was returned;
    /// otherwise receives <see cref="UsdAttributeTryFailureReason.None"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a value was read; otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryGetValue(
        double timeCode,
        out UsdScalarValue value,
        out UsdAttributeTryFailureReason failureReason) =>
        TryGetValueCore(timeCode, out value, out failureReason);

    private static UsdAttributeValueState ConvertState(
        OpenUsd.Interop.OpenUsdNativeAttributeValueState state) =>
        new(state.HasAuthoredValueOpinion, state.IsBlocked);

    private UsdScalarValue GetValueCore(double? timeCode)
    {
        string typeName = TypeName;
        return typeName switch
        {
            "int[]" => UsdScalarValue.FromInt32Array(
                Stage.Native.GetInt32Array(PrimPath, Name, timeCode)),
            "float[]" => UsdScalarValue.FromFloatArray(
                Stage.Native.GetFloatArray(PrimPath, Name, timeCode)),
            "double[]" => UsdScalarValue.FromDoubleArray(
                Stage.Native.GetDoubleArray(PrimPath, Name, timeCode)),
            "float2[]" or "texCoord2f[]" => UsdScalarValue.FromVec2fArray(
                ConvertVec2f(Stage.Native.GetVec2fArray(PrimPath, Name, timeCode))),
            "float3[]" or "vector3f[]" or "normal3f[]" or "point3f[]" =>
                UsdScalarValue.FromVec3fArray(
                    ConvertVec3f(Stage.Native.GetVec3fArray(PrimPath, Name, timeCode))),
            "color3f[]" => UsdScalarValue.FromColor3fArray(
                ConvertVec3f(Stage.Native.GetColor3fArray(PrimPath, Name, timeCode))),
            "bool[]" => UsdScalarValue.FromBoolArray(
                Stage.Native.GetBoolArray(PrimPath, Name, timeCode)),
            "token[]" => UsdScalarValue.FromTokenArray(
                Stage.Native.GetTokenArray(PrimPath, Name, timeCode)),
            "string[]" => UsdScalarValue.FromStringArray(
                Stage.Native.GetStringArray(PrimPath, Name, timeCode)),
            _ => UsdScalarValue.FromNative(
                timeCode.HasValue
                    ? Stage.Native.GetAttributeScalarValue(PrimPath, Name, timeCode.Value)
                    : Stage.Native.GetAttributeScalarValue(PrimPath, Name))
        };
    }

    private bool TryGetValueCore(double? timeCode, out UsdScalarValue value)
        => TryGetValueCore(timeCode, out value, out _);

    private bool TryGetValueCore(
        double? timeCode,
        out UsdScalarValue value,
        out UsdAttributeTryFailureReason failureReason)
    {
        value = default;
        failureReason = UsdAttributeTryFailureReason.None;
        if (!AttributeExists())
        {
            failureReason = UsdAttributeTryFailureReason.AttributeNotFound;
            return false;
        }

        var state = new TryGetValueState(this, timeCode);
        return TryGetExistingValueCore<StageValueAccessor, TryGetValueState>(
            in state,
            out value,
            out failureReason);
    }

    internal static bool TryGetExistingValueCore(
        Func<UsdScalarValue> getValue,
        out UsdScalarValue value,
        out UsdAttributeTryFailureReason failureReason)
    {
        ArgumentNullException.ThrowIfNull(getValue);
        return TryGetExistingValueCore<DelegateValueAccessor, Func<UsdScalarValue>>(
            in getValue,
            out value,
            out failureReason);
    }

    internal static bool TryGetExistingValueCore<TAccessor, TState>(
        in TState state,
        out UsdScalarValue value,
        out UsdAttributeTryFailureReason failureReason)
        where TAccessor : ITryGetValueAccessor<TState>
    {
        try
        {
            value = TAccessor.GetValue(in state);
            failureReason = UsdAttributeTryFailureReason.None;
            return true;
        }
        catch (Exception exception) when (
            TryClassifyGetValueFailure(exception, out value, out failureReason))
        {
            return false;
        }
    }

    private static bool TryClassifyGetValueFailure(
        Exception exception,
        out UsdScalarValue value,
        out UsdAttributeTryFailureReason failureReason)
    {
        value = default;
        if (exception is OpenUsd.Interop.OpenUsdNativeException)
        {
            failureReason = UsdAttributeTryFailureReason.NativeCallFailed;
            return true;
        }
        if (exception is InvalidOperationException)
        {
            failureReason = UsdAttributeTryFailureReason.UnsupportedValueType;
            return true;
        }

        failureReason = UsdAttributeTryFailureReason.None;
        return false;
    }

    internal interface ITryGetValueAccessor<TState>
    {
        static abstract UsdScalarValue GetValue(in TState state);
    }

    private readonly record struct TryGetValueState(
        UsdAttribute Attribute,
        double? TimeCode);

    private readonly struct StageValueAccessor : ITryGetValueAccessor<TryGetValueState>
    {
        public static UsdScalarValue GetValue(in TryGetValueState state) =>
            state.Attribute.GetValueCore(state.TimeCode);
    }

    private readonly struct DelegateValueAccessor : ITryGetValueAccessor<Func<UsdScalarValue>>
    {
        public static UsdScalarValue GetValue(in Func<UsdScalarValue> state) => state();
    }

    private bool TrySetCore(in UsdScalarValue value, double? timeCode)
        => TrySetCore(value, timeCode, out _);

    private bool TrySetCore(
        in UsdScalarValue value,
        double? timeCode,
        out UsdAttributeTryFailureReason failureReason)
    {
        failureReason = UsdAttributeTryFailureReason.None;
        if (!AttributeExists())
        {
            failureReason = UsdAttributeTryFailureReason.AttributeNotFound;
            return false;
        }

        string typeName;
        try
        {
            typeName = TypeName;
        }
        catch (OpenUsd.Interop.OpenUsdNativeException)
        {
            failureReason = UsdAttributeTryFailureReason.NativeCallFailed;
            return false;
        }

        if (!IsCompatible(typeName, value.Kind))
        {
            failureReason = UsdAttributeTryFailureReason.TypeIncompatible;
            return false;
        }

        try
        {
            SetCore(value, timeCode);
            return true;
        }
        catch (OpenUsd.Interop.OpenUsdNativeException)
        {
            failureReason = UsdAttributeTryFailureReason.NativeCallFailed;
            return false;
        }
    }

    private void SetCore(in UsdScalarValue value, double? timeCode)
    {
        UsdPrim prim = Stage.GetPrim(PrimPath);
        switch (value.Kind)
        {
            case UsdScalarKind.Boolean:
                if (timeCode.HasValue)
                {
                    prim.SetBool(Name, value.BoolValue, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetBool(Name, value.BoolValue);
                }

                break;
            case UsdScalarKind.Signed64:
                if (timeCode.HasValue)
                {
                    prim.SetInt64(Name, value.Int64Value, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetInt64(Name, value.Int64Value);
                }

                break;
            case UsdScalarKind.Number:
                if (timeCode.HasValue)
                {
                    prim.SetDouble(Name, value.DoubleValue, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetDouble(Name, value.DoubleValue);
                }

                break;
            case UsdScalarKind.Text:
                if (timeCode.HasValue)
                {
                    prim.SetString(Name, value.StringValue, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetString(Name, value.StringValue);
                }

                break;
            case UsdScalarKind.Token:
                if (timeCode.HasValue)
                {
                    prim.SetToken(Name, value.TokenValue, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetToken(Name, value.TokenValue);
                }

                break;
            case UsdScalarKind.Vector3:
                if (timeCode.HasValue)
                {
                    prim.SetVec3f(Name, value.Vec3fValue, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetVec3f(Name, value.Vec3fValue);
                }

                break;
            case UsdScalarKind.Color3:
                if (timeCode.HasValue)
                {
                    prim.SetColor3f(Name, value.Color3fValue, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetColor3f(Name, value.Color3fValue);
                }

                break;
            case UsdScalarKind.Matrix4d:
                if (timeCode.HasValue)
                {
                    prim.SetMatrix4d(Name, value.Matrix4dValue, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetMatrix4d(Name, value.Matrix4dValue);
                }

                break;
            case UsdScalarKind.Int32Array:
                if (timeCode.HasValue)
                {
                    prim.SetInt32Array(Name, value.Int32ArrayValue, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetInt32Array(Name, value.Int32ArrayValue);
                }
                break;
            case UsdScalarKind.FloatArray:
                if (timeCode.HasValue)
                {
                    prim.SetFloatArray(Name, value.FloatArrayValue, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetFloatArray(Name, value.FloatArrayValue);
                }
                break;
            case UsdScalarKind.DoubleArray:
                if (timeCode.HasValue)
                {
                    prim.SetDoubleArray(Name, value.DoubleArrayValue, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetDoubleArray(Name, value.DoubleArrayValue);
                }
                break;
            case UsdScalarKind.Vec2fArray:
                if (timeCode.HasValue)
                {
                    prim.SetVec2fArray(Name, value.Vec2fArrayValue, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetVec2fArray(Name, value.Vec2fArrayValue);
                }
                break;
            case UsdScalarKind.Vec3fArray:
                if (timeCode.HasValue)
                {
                    prim.SetVec3fArray(Name, value.Vec3fArrayValue, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetVec3fArray(Name, value.Vec3fArrayValue);
                }
                break;
            case UsdScalarKind.Color3fArray:
                if (timeCode.HasValue)
                {
                    prim.SetColor3fArray(Name, value.Color3fArrayValue, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetColor3fArray(Name, value.Color3fArrayValue);
                }
                break;
            case UsdScalarKind.BooleanArray:
                if (timeCode.HasValue)
                {
                    prim.SetBoolArray(Name, value.BoolArrayValue, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetBoolArray(Name, value.BoolArrayValue);
                }
                break;
            case UsdScalarKind.TokenArray:
                if (timeCode.HasValue)
                {
                    prim.SetTokenArray(Name, value.TokenArrayValue, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetTokenArray(Name, value.TokenArrayValue);
                }
                break;
            case UsdScalarKind.StringArray:
                if (timeCode.HasValue)
                {
                    prim.SetStringArray(Name, value.StringArrayValue, timeCode.GetValueOrDefault());
                }
                else
                {
                    prim.SetStringArray(Name, value.StringArrayValue);
                }
                break;
            default:
                throw new ArgumentException("The scalar value kind is not supported.", nameof(value));
        }
    }

    private bool AttributeExists() =>
        Stage.GetPrim(PrimPath).GetAttributeNames().Contains(Name, StringComparer.Ordinal);

    private static bool IsCompatible(string typeName, UsdScalarKind kind) => kind switch
    {
        UsdScalarKind.Boolean => typeName == "bool",
        UsdScalarKind.Signed64 => typeName == "int64",
        UsdScalarKind.Number => typeName == "double",
        UsdScalarKind.Text => typeName == "string",
        UsdScalarKind.Token => typeName == "token",
        UsdScalarKind.Vector3 => typeName is "float3" or "vector3f",
        UsdScalarKind.Color3 => typeName == "color3f",
        UsdScalarKind.Matrix4d => typeName == "matrix4d",
        UsdScalarKind.Int32Array => typeName == "int[]",
        UsdScalarKind.FloatArray => typeName == "float[]",
        UsdScalarKind.DoubleArray => typeName == "double[]",
        UsdScalarKind.Vec2fArray => typeName is "float2[]" or "texCoord2f[]",
        UsdScalarKind.Vec3fArray => typeName is "float3[]" or "vector3f[]" or "normal3f[]" or "point3f[]",
        UsdScalarKind.Color3fArray => typeName == "color3f[]",
        UsdScalarKind.BooleanArray => typeName == "bool[]",
        UsdScalarKind.TokenArray => typeName == "token[]",
        UsdScalarKind.StringArray => typeName == "string[]",
        _ => false
    };

    private void Set<TValue>(
        double? timeCode,
        TValue value,
        Action<string, TValue> setDefault,
        Action<string, TValue, double> setSample)
    {
        if (timeCode.HasValue)
        {
            setSample(Name, value, timeCode.GetValueOrDefault());
        }
        else
        {
            setDefault(Name, value);
        }
    }

    private static UsdVec2f[] ConvertVec2f(OpenUsd.Interop.OpenUsdNativeVec2f[] values)
    {
        var result = new UsdVec2f[values.Length];
        for (int index = 0; index < values.Length; ++index)
        {
            result[index] = UsdVec2f.FromNative(values[index]);
        }
        return result;
    }

    private static UsdVec3f[] ConvertVec3f(OpenUsd.Interop.OpenUsdNativeVec3f[] values)
    {
        var result = new UsdVec3f[values.Length];
        for (int index = 0; index < values.Length; ++index)
        {
            result[index] = UsdVec3f.FromNative(values[index]);
        }
        return result;
    }

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The attribute is not attached to a stage.");
}
