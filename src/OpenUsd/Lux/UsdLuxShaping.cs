// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Lux;

/// <summary>Focused controls from an explicitly applied UsdLuxShapingAPI.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdLuxShaping : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdLuxShaping(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    /// <summary>Gets the light prim path.</summary>
    public string Path { get; }

    /// <summary>Gets or sets the emission focus exponent.</summary>
    public float Focus
    {
        get => Get(OpenUsdNativeLuxShapingProperty.Focus);
        set => Set(OpenUsdNativeLuxShapingProperty.Focus, value);
    }

    /// <summary>Gets or sets the cone cutoff angle in degrees.</summary>
    public float ConeAngle
    {
        get => Get(OpenUsdNativeLuxShapingProperty.ConeAngle);
        set => Set(OpenUsdNativeLuxShapingProperty.ConeAngle, value);
    }

    /// <summary>Gets or sets the fractional cone-edge softness.</summary>
    public float ConeSoftness
    {
        get => Get(OpenUsdNativeLuxShapingProperty.ConeSoftness);
        set => Set(OpenUsdNativeLuxShapingProperty.ConeSoftness, value);
    }

    private float Get(OpenUsdNativeLuxShapingProperty property) =>
        Stage.Native.GetLuxShaping(Path, property);

    private void Set(OpenUsdNativeLuxShapingProperty property, float value)
    {
        switch (property)
        {
            case OpenUsdNativeLuxShapingProperty.Focus:
                UsdLuxSchema.ValidateNonNegative(value, nameof(value));
                break;
            case OpenUsdNativeLuxShapingProperty.ConeAngle:
                UsdLuxSchema.ValidateRange(value, 0, 180, nameof(value));
                break;
            case OpenUsdNativeLuxShapingProperty.ConeSoftness:
                UsdLuxSchema.ValidateRange(value, 0, 1, nameof(value));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(property));
        }
        Stage.Native.SetLuxShaping(Path, property, value);
    }

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
