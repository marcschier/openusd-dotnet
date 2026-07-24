// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Lux;

/// <summary>A validated UsdLuxCylinderLight view.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdLuxCylinderLight : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdLuxCylinderLight(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    /// <summary>Gets the prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the shared light controls.</summary>
    public UsdLuxLight Light => new(Stage, Path);

    /// <summary>Gets the transformable schema view.</summary>
    public UsdGeomXformable Xformable => Light.Xformable;

    /// <summary>Gets the underlying prim descriptor.</summary>
    public UsdPrim Prim => Light.Prim;

    /// <summary>Gets or sets the cylinder radius.</summary>
    public float Radius
    {
        get => GetShape(OpenUsdNativeLuxShapeProperty.Radius);
        set => SetShape(OpenUsdNativeLuxShapeProperty.Radius, value);
    }

    /// <summary>Gets or sets the cylinder length.</summary>
    public float Length
    {
        get => GetShape(OpenUsdNativeLuxShapeProperty.Length);
        set => SetShape(OpenUsdNativeLuxShapeProperty.Length, value);
    }

    /// <summary>Tries to wrap an exact UsdLuxCylinderLight prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdLuxCylinderLight value)
    {
        if (UsdLuxSchema.TryValidate(
            prim,
            OpenUsdNativeLuxSchemaKind.CylinderLight,
            out UsdStage? stage))
        {
            value = new UsdLuxCylinderLight(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps an exact UsdLuxCylinderLight prim.</summary>
    public static UsdLuxCylinderLight Wrap(UsdPrim prim) => new(
        UsdLuxSchema.Validate(
            prim,
            OpenUsdNativeLuxSchemaKind.CylinderLight,
            nameof(UsdLuxCylinderLight)),
        prim.Path);

    private float GetShape(OpenUsdNativeLuxShapeProperty property) =>
        Stage.Native.GetLuxShape(Path, property);

    private void SetShape(OpenUsdNativeLuxShapeProperty property, float value)
    {
        UsdLuxSchema.ValidateNonNegative(value, nameof(value));
        Stage.Native.SetLuxShape(Path, property, value);
    }

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
