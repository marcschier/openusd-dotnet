// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Lux;

/// <summary>A validated UsdLuxSphereLight view.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdLuxSphereLight : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdLuxSphereLight(UsdStage stage, string path)
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

    /// <summary>Gets or sets the sphere radius.</summary>
    public float Radius
    {
        get => Stage.Native.GetLuxShape(Path, OpenUsdNativeLuxShapeProperty.Radius);
        set
        {
            UsdLuxSchema.ValidateNonNegative(value, nameof(value));
            Stage.Native.SetLuxShape(Path, OpenUsdNativeLuxShapeProperty.Radius, value);
        }
    }

    /// <summary>Tries to wrap an exact UsdLuxSphereLight prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdLuxSphereLight value)
    {
        if (UsdLuxSchema.TryValidate(
            prim,
            OpenUsdNativeLuxSchemaKind.SphereLight,
            out UsdStage? stage))
        {
            value = new UsdLuxSphereLight(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps an exact UsdLuxSphereLight prim.</summary>
    public static UsdLuxSphereLight Wrap(UsdPrim prim) => new(
        UsdLuxSchema.Validate(
            prim,
            OpenUsdNativeLuxSchemaKind.SphereLight,
            nameof(UsdLuxSphereLight)),
        prim.Path);

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
