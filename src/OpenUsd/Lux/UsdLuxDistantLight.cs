// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Lux;

/// <summary>A validated UsdLuxDistantLight view.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdLuxDistantLight : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdLuxDistantLight(UsdStage stage, string path)
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

    /// <summary>Gets or sets the apparent angular diameter in degrees.</summary>
    public float Angle
    {
        get => Stage.Native.GetLuxShape(Path, OpenUsdNativeLuxShapeProperty.Angle);
        set
        {
            UsdLuxSchema.ValidateRange(
                value, 0, 360, nameof(value), maximumInclusive: false);
            Stage.Native.SetLuxShape(Path, OpenUsdNativeLuxShapeProperty.Angle, value);
        }
    }

    /// <summary>Tries to wrap an exact UsdLuxDistantLight prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdLuxDistantLight value)
    {
        if (UsdLuxSchema.TryValidate(
            prim,
            OpenUsdNativeLuxSchemaKind.DistantLight,
            out UsdStage? stage))
        {
            value = new UsdLuxDistantLight(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps an exact UsdLuxDistantLight prim.</summary>
    public static UsdLuxDistantLight Wrap(UsdPrim prim) => new(
        UsdLuxSchema.Validate(
            prim,
            OpenUsdNativeLuxSchemaKind.DistantLight,
            nameof(UsdLuxDistantLight)),
        prim.Path);

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
