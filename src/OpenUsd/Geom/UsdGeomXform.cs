// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.Geom;

/// <summary>A validated view of a UsdGeomXform prim.</summary>
[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomXform : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdGeomXform(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    /// <summary>Gets the prim path.</summary>
    public string Path { get; }

    /// <summary>Gets the imageable schema view.</summary>
    public UsdGeomImageable Imageable => new(Stage, Path);

    /// <summary>Gets the xformable schema view.</summary>
    public UsdGeomXformable Xformable => new(Stage, Path);

    /// <summary>Gets the underlying prim descriptor.</summary>
    public UsdPrim Prim => Stage.GetPrim(Path);

    /// <summary>Tries to wrap a UsdGeomXform prim.</summary>
    public static bool TryWrap(UsdPrim prim, out UsdGeomXform value)
    {
        if (UsdGeomSchema.TryValidate(prim, UsdGeomSchemaKind.Xform, out UsdStage? stage))
        {
            value = new UsdGeomXform(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Wraps a UsdGeomXform prim or throws for a wrong schema.</summary>
    public static UsdGeomXform Wrap(UsdPrim prim) => new(
        UsdGeomSchema.Validate(prim, UsdGeomSchemaKind.Xform, nameof(UsdGeomXform)),
        prim.Path);

    private UsdStage Stage =>
        _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}
