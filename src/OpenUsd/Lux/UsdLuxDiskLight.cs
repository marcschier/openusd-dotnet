// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Lux;

/// <summary>A validated UsdLuxDiskLight view.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdLuxDiskLight : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdLuxDiskLight(UsdStage stage, string path)
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

    /// <summary>Gets or sets the disk radius.</summary>
    public float Radius
    {
        get => Stage.Native.GetLuxShape(Path, OpenUsdNativeLuxShapeProperty.Radius);
        set
        {
            UsdLuxSchema.ValidateNonNegative(value, nameof(value));
            Stage.Native.SetLuxShape(Path, OpenUsdNativeLuxShapeProperty.Radius, value);
        }
    }

    /// <summary>Tries to wrap an exact UsdLuxDiskLight prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdLuxDiskLight value)
    {
        if (UsdLuxSchema.TryValidate(
            prim,
            OpenUsdNativeLuxSchemaKind.DiskLight,
            out UsdStage? stage))
        {
            value = new UsdLuxDiskLight(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps an exact UsdLuxDiskLight prim.</summary>
    public static UsdLuxDiskLight Wrap(UsdPrim prim) => new(
        UsdLuxSchema.Validate(
            prim,
            OpenUsdNativeLuxSchemaKind.DiskLight,
            nameof(UsdLuxDiskLight)),
        prim.Path);

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
