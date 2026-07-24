// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Skel;

/// <summary>A validated UsdSkelRoot view.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdSkelRoot : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdSkelRoot(UsdStage stage, string path)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path, nameof(path));
        _stage = stage;
        Path = path;
    }

    /// <summary>Gets the prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the underlying prim descriptor.</summary>
    public UsdPrim Prim => Stage.GetPrim(Path);

    /// <summary>Gets the imageable schema view.</summary>
    public UsdGeomImageable Imageable => new(Stage, Path);

    /// <summary>Gets the transformable schema view.</summary>
    public UsdGeomXformable Xformable => new(Stage, Path);

    /// <summary>Applies and returns the root's UsdSkelBindingAPI view.</summary>
    public UsdSkelBinding ApplyBinding() => UsdSkelBinding.Apply(Prim);

    /// <summary>Tries to wrap an exact UsdSkelRoot prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdSkelRoot value)
    {
        if (UsdSkelSchema.TryValidate(
            prim,
            OpenUsdNativeSkelSchemaKind.Root,
            out UsdStage? stage))
        {
            value = new UsdSkelRoot(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps an exact UsdSkelRoot prim.</summary>
    public static UsdSkelRoot Wrap(UsdPrim prim) => new(
        UsdSkelSchema.Validate(
            prim,
            OpenUsdNativeSkelSchemaKind.Root,
            nameof(UsdSkelRoot)),
        prim.Path);

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
