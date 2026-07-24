// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Lux;

/// <summary>A validated UsdLuxDomeLight view.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdLuxDomeLight : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdLuxDomeLight(UsdStage stage, string path)
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

    /// <summary>Gets or sets the environment texture-file asset.</summary>
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

    /// <summary>Tries to wrap an exact UsdLuxDomeLight prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdLuxDomeLight value)
    {
        if (UsdLuxSchema.TryValidate(
            prim,
            OpenUsdNativeLuxSchemaKind.DomeLight,
            out UsdStage? stage))
        {
            value = new UsdLuxDomeLight(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps an exact UsdLuxDomeLight prim.</summary>
    public static UsdLuxDomeLight Wrap(UsdPrim prim) => new(
        UsdLuxSchema.Validate(
            prim,
            OpenUsdNativeLuxSchemaKind.DomeLight,
            nameof(UsdLuxDomeLight)),
        prim.Path);

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
