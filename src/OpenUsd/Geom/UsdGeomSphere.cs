// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS1591

namespace OpenUsd.Geom;

/// <summary>A validated view of a UsdGeomSphere implicit-surface prim.</summary>
[ExcludeFromCodeCoverage(Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomSphere : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdGeomSphere(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    public string Path { get; }
    public UsdGeomImageable Imageable => new(Stage, Path);
    public UsdGeomXformable Xformable => new(Stage, Path);
    public UsdPrim Prim => Stage.GetPrim(Path);

    public static bool TryWrap(UsdPrim prim, out UsdGeomSphere value)
    {
        if (UsdGeomFacade.TryWrap(prim, UsdGeomSchemaKind.Sphere, out UsdStage? stage))
        {
            value = new UsdGeomSphere(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    public static UsdGeomSphere Wrap(UsdPrim prim) => new(
        UsdGeomFacade.Validate(prim, UsdGeomSchemaKind.Sphere, nameof(UsdGeomSphere)),
        prim.Path);

    public double Radius { get => Prim.GetDouble("radius"); set => Prim.SetDouble("radius", value); }
    public void SetExtent(UsdExtent3f extent) => UsdGeomSchema.SetExtent(Prim, extent);
    public UsdExtent3f GetExtent() => UsdGeomSchema.GetExtent(Prim);

    private UsdStage Stage => UsdGeomFacade.Require(_stage);
}

