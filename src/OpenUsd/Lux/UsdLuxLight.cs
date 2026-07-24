// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Lux;

/// <summary>Shared UsdLuxLightAPI controls for a validated concrete light.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public sealed class UsdLuxLight : IUsdStageBound
{
    private readonly UsdStage _stage;

    internal UsdLuxLight(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    /// <summary>Gets the light prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the underlying prim descriptor.</summary>
    public UsdPrim Prim => Stage.GetPrim(Path);

    /// <summary>Gets the transformable schema view.</summary>
    public UsdGeomXformable Xformable => new(Stage, Path);

    /// <summary>Gets or sets the light intensity.</summary>
    public float Intensity
    {
        get => GetFloat(OpenUsdNativeLuxFloatProperty.Intensity);
        set => SetFloat(OpenUsdNativeLuxFloatProperty.Intensity, value);
    }

    /// <summary>Gets or sets the light exposure in stops.</summary>
    public float Exposure
    {
        get => GetFloat(OpenUsdNativeLuxFloatProperty.Exposure);
        set => SetFloat(OpenUsdNativeLuxFloatProperty.Exposure, value);
    }

    /// <summary>Gets or sets the diffuse contribution multiplier.</summary>
    public float Diffuse
    {
        get => GetFloat(OpenUsdNativeLuxFloatProperty.Diffuse);
        set => SetFloat(OpenUsdNativeLuxFloatProperty.Diffuse, value);
    }

    /// <summary>Gets or sets the specular contribution multiplier.</summary>
    public float Specular
    {
        get => GetFloat(OpenUsdNativeLuxFloatProperty.Specular);
        set => SetFloat(OpenUsdNativeLuxFloatProperty.Specular, value);
    }

    /// <summary>Gets or sets the color temperature in kelvins.</summary>
    public float ColorTemperature
    {
        get => GetFloat(OpenUsdNativeLuxFloatProperty.ColorTemperature);
        set => SetFloat(OpenUsdNativeLuxFloatProperty.ColorTemperature, value);
    }

    /// <summary>Gets or sets whether color temperature affects the light color.</summary>
    public bool EnableColorTemperature
    {
        get => GetBool(OpenUsdNativeLuxBoolProperty.EnableColorTemperature);
        set => SetBool(OpenUsdNativeLuxBoolProperty.EnableColorTemperature, value);
    }

    /// <summary>Gets or sets whether power is normalized by the light dimensions.</summary>
    public bool Normalize
    {
        get => GetBool(OpenUsdNativeLuxBoolProperty.Normalize);
        set => SetBool(OpenUsdNativeLuxBoolProperty.Normalize, value);
    }

    /// <summary>Gets or sets the RGB light color.</summary>
    public UsdVec3f Color
    {
        get => UsdVec3f.FromNative(Stage.Native.GetLuxColor(Path));
        set
        {
            UsdLuxSchema.ValidateColor(value, nameof(value));
            Stage.Native.SetLuxColor(Path, value.ToNative());
        }
    }

    /// <summary>Gets whether UsdLuxShapingAPI has been applied.</summary>
    public bool HasShaping => Stage.Native.HasLuxShaping(Path);

    /// <summary>Applies UsdLuxShapingAPI and returns its focused controls.</summary>
    public UsdLuxShaping ApplyShaping()
    {
        Stage.Native.ApplyLuxShaping(Path);
        return new UsdLuxShaping(Stage, Path);
    }

    /// <summary>Gets existing shaping controls or throws when the API is not applied.</summary>
    public UsdLuxShaping GetShaping()
    {
        if (!HasShaping)
        {
            throw new InvalidOperationException(
                $"UsdLuxShapingAPI is not applied to light '{Path}'.");
        }
        return new UsdLuxShaping(Stage, Path);
    }

    private float GetFloat(OpenUsdNativeLuxFloatProperty property) =>
        Stage.Native.GetLuxFloat(Path, property);

    private void SetFloat(OpenUsdNativeLuxFloatProperty property, float value)
    {
        switch (property)
        {
            case OpenUsdNativeLuxFloatProperty.Intensity:
                UsdLuxSchema.ValidateNonNegative(value, nameof(value));
                break;
            case OpenUsdNativeLuxFloatProperty.ColorTemperature:
                UsdLuxSchema.ValidateRange(value, 1000, 10000, nameof(value));
                break;
            default:
                UsdLuxSchema.ValidateFinite(value, nameof(value));
                break;
        }
        Stage.Native.SetLuxFloat(Path, property, value);
    }

    private bool GetBool(OpenUsdNativeLuxBoolProperty property) =>
        Stage.Native.GetLuxBool(Path, property);

    private void SetBool(OpenUsdNativeLuxBoolProperty property, bool value) =>
        Stage.Native.SetLuxBool(Path, property, value);

    private UsdStage Stage => _stage;
}
