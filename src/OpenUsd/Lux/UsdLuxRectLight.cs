// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Lux;

/// <summary>A validated UsdLuxRectLight view.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdLuxRectLight : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdLuxRectLight(UsdStage stage, string path)
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

    /// <summary>Gets or sets the rectangle width.</summary>
    public float Width
    {
        get => GetShape(OpenUsdNativeLuxShapeProperty.Width);
        set => SetShape(OpenUsdNativeLuxShapeProperty.Width, value);
    }

    /// <summary>Gets or sets the rectangle height.</summary>
    public float Height
    {
        get => GetShape(OpenUsdNativeLuxShapeProperty.Height);
        set => SetShape(OpenUsdNativeLuxShapeProperty.Height, value);
    }

    /// <summary>Gets or sets the texture-file asset.</summary>
    public UsdAssetPath TextureFile
    {
        get => new(Stage.Native.GetLuxAsset(
            Path,
            OpenUsdNativeLuxAssetProperty.TextureFile));
        set => Stage.Native.SetLuxAsset(
            Path,
            OpenUsdNativeLuxAssetProperty.TextureFile,
            value.Path);
    }

    /// <summary>Tries to wrap an exact UsdLuxRectLight prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdLuxRectLight value)
    {
        if (UsdLuxSchema.TryValidate(
            prim,
            OpenUsdNativeLuxSchemaKind.RectLight,
            out UsdStage? stage))
        {
            value = new UsdLuxRectLight(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps an exact UsdLuxRectLight prim.</summary>
    public static UsdLuxRectLight Wrap(UsdPrim prim) => new(
        UsdLuxSchema.Validate(
            prim,
            OpenUsdNativeLuxSchemaKind.RectLight,
            nameof(UsdLuxRectLight)),
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
