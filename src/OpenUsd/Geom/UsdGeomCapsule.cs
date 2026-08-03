// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS1591

namespace OpenUsd.Geom;

/// <summary>A validated view of a UsdGeomCapsule implicit-surface prim.</summary>
[ExcludeFromCodeCoverage(Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomCapsule : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdGeomCapsule(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    public string Path { get; }
    public UsdGeomImageable Imageable => new(Stage, Path);
    public UsdGeomXformable Xformable => new(Stage, Path);
    public UsdPrim Prim => Stage.GetPrim(Path);

    public static bool TryWrap(UsdPrim prim, out UsdGeomCapsule value)
    {
        if (UsdGeomFacade.TryWrap(prim, UsdGeomSchemaKind.Capsule, out UsdStage? stage))
        {
            value = new UsdGeomCapsule(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    public static UsdGeomCapsule Wrap(UsdPrim prim) => new(
        UsdGeomFacade.Validate(prim, UsdGeomSchemaKind.Capsule, nameof(UsdGeomCapsule)),
        prim.Path);

    public double Radius { get => Prim.GetDouble("radius"); set => Prim.SetDouble("radius", value); }
    public double Height { get => Prim.GetDouble("height"); set => Prim.SetDouble("height", value); }
    public UsdGeomAxis Axis
    {
        get => UsdGeomSchema.ToAxis(Prim.GetToken("axis"));
        set => Prim.SetToken("axis", UsdGeomSchema.ToToken(value));
    }
    public void SetExtent(UsdExtent3f extent) => UsdGeomSchema.SetExtent(Prim, extent);
    public UsdExtent3f GetExtent() => UsdGeomSchema.GetExtent(Prim);

    private UsdStage Stage => UsdGeomFacade.Require(_stage);
}

