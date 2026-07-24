// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Shade;

/// <summary>A typed UsdShade input descriptor.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdShadeInput : IUsdStageBound
{
    internal UsdShadeInput(
        UsdStage stage,
        string primPath,
        string name,
        UsdShadeValueType valueType)
    {
        Stage = stage;
        PrimPath = primPath;
        Name = name;
        ValueType = valueType;
    }

    /// <summary>Gets the owning prim path.</summary>
    public string PrimPath { get; }

    /// <summary>Gets the base input name without the inputs namespace.</summary>
    public string Name { get; }

    /// <summary>Gets the declared USD value type.</summary>
    public UsdShadeValueType ValueType { get; }

    /// <summary>Authors a float value.</summary>
    public void Set(float value)
    {
        RequireType(UsdShadeValueType.Float);
        Stage.Native.SetShadeInput(PrimPath, Name, value);
    }

    /// <summary>Reads a float value.</summary>
    public float GetFloat()
    {
        RequireType(UsdShadeValueType.Float);
        return Stage.Native.GetShadeInputFloat(PrimPath, Name);
    }

    /// <summary>Authors a color3f value.</summary>
    public void SetColor(UsdVec3f value) => SetVec3f(value, UsdShadeValueType.Color3f);

    /// <summary>Reads a color3f value.</summary>
    public UsdVec3f GetColor() => GetVec3f(UsdShadeValueType.Color3f);

    /// <summary>Authors a vector3f value.</summary>
    public void SetVector(UsdVec3f value) => SetVec3f(value, UsdShadeValueType.Vector3f);

    /// <summary>Reads a vector3f value.</summary>
    public UsdVec3f GetVector() => GetVec3f(UsdShadeValueType.Vector3f);

    /// <summary>Authors a normal3f value.</summary>
    public void SetNormal(UsdVec3f value) => SetVec3f(value, UsdShadeValueType.Normal3f);

    /// <summary>Reads a normal3f value.</summary>
    public UsdVec3f GetNormal() => GetVec3f(UsdShadeValueType.Normal3f);

    /// <summary>Authors a token value.</summary>
    public void SetToken(string value) => SetString(value, UsdShadeValueType.Token);

    /// <summary>Reads a token value.</summary>
    public string GetToken() => GetString(UsdShadeValueType.Token);

    /// <summary>Authors a string value.</summary>
    public void SetString(string value) => SetString(value, UsdShadeValueType.String);

    /// <summary>Reads a string value.</summary>
    public string GetString() => GetString(UsdShadeValueType.String);

    /// <summary>Authors an asset path.</summary>
    public void SetAssetPath(UsdAssetPath value) =>
        SetString(value.Path, UsdShadeValueType.Asset);

    /// <summary>Reads the authored, unresolved asset path.</summary>
    public UsdAssetPath GetAssetPath() =>
        new(GetString(UsdShadeValueType.Asset));

    /// <summary>Connects this input to a source output.</summary>
    public void ConnectToSource(UsdShadeOutput source)
    {
        UsdShadeSchema.ValidateSameStage(Stage, source.Stage);
        Stage.Native.ConnectShade(
            PrimPath,
            Name,
            OpenUsd.Interop.OpenUsdNativeShadeAttributeType.Input,
            source.PrimPath,
            source.Name,
            OpenUsd.Interop.OpenUsdNativeShadeAttributeType.Output);
    }

    /// <summary>Connects this input to another source input.</summary>
    public void ConnectToSource(UsdShadeInput source)
    {
        UsdShadeSchema.ValidateSameStage(Stage, source.Stage);
        Stage.Native.ConnectShade(
            PrimPath,
            Name,
            OpenUsd.Interop.OpenUsdNativeShadeAttributeType.Input,
            source.PrimPath,
            source.Name,
            OpenUsd.Interop.OpenUsdNativeShadeAttributeType.Input);
    }

    /// <summary>Disconnects the current source.</summary>
    public void Disconnect() =>
        Stage.Native.DisconnectShade(
            PrimPath,
            Name,
            OpenUsd.Interop.OpenUsdNativeShadeAttributeType.Input);

    /// <summary>Gets the single connected source.</summary>
    public UsdShadeConnection GetConnectedSource() =>
        UsdShadeSchema.FromNative(
            Stage.Native.GetConnectedShadeSource(
                PrimPath,
                Name,
                OpenUsd.Interop.OpenUsdNativeShadeAttributeType.Input));

    /// <summary>Gets every connected source in authored order.</summary>
    public IReadOnlyList<UsdShadeConnection> GetConnectedSources() =>
        Stage.Native.GetConnectedShadeSources(
                PrimPath,
                Name,
                OpenUsd.Interop.OpenUsdNativeShadeAttributeType.Input)
            .Select(UsdShadeSchema.FromNative)
            .ToArray();

    internal UsdStage Stage { get; }

    private void SetVec3f(UsdVec3f value, UsdShadeValueType requiredType)
    {
        RequireType(requiredType);
        Stage.Native.SetShadeInput(
            PrimPath,
            Name,
            UsdShadeSchema.ToNative(requiredType),
            value.ToNative());
    }

    private UsdVec3f GetVec3f(UsdShadeValueType requiredType)
    {
        RequireType(requiredType);
        return UsdVec3f.FromNative(
            Stage.Native.GetShadeInputVec3f(
                PrimPath,
                Name,
                UsdShadeSchema.ToNative(requiredType)));
    }

    private void SetString(string value, UsdShadeValueType requiredType)
    {
        RequireType(requiredType);
        Stage.Native.SetShadeInput(
            PrimPath,
            Name,
            UsdShadeSchema.ToNative(requiredType),
            value);
    }

    private string GetString(UsdShadeValueType requiredType)
    {
        RequireType(requiredType);
        return Stage.Native.GetShadeInputString(
            PrimPath,
            Name,
            UsdShadeSchema.ToNative(requiredType));
    }

    private void RequireType(UsdShadeValueType requiredType)
    {
        if (ValueType != requiredType)
        {
            throw new InvalidOperationException(
                $"Input '{Name}' is {ValueType}, not {requiredType}.");
        }
    }
}
