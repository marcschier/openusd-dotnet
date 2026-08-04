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
            "float3[]" or "vector3f[]" or "normal3f[]" or "point3f[]" or "color3f[]" =>
                UsdScalarValue.FromVec3fArray(
                    ConvertVec3f(Stage.Native.GetVec3fArray(PrimPath, Name, timeCode))),
            _ => UsdScalarValue.FromNative(
                timeCode.HasValue
                    ? Stage.Native.GetAttributeScalarValue(PrimPath, Name, timeCode.Value)
                    : Stage.Native.GetAttributeScalarValue(PrimPath, Name))
        };
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
