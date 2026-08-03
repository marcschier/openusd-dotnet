// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS1591

namespace OpenUsd.Geom;

/// <summary>A validated, bulk-oriented view of a UsdGeomNurbsPatch prim.</summary>
[ExcludeFromCodeCoverage(Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomNurbsPatch : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdGeomNurbsPatch(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    public string Path { get; }
    public UsdGeomImageable Imageable => new(Stage, Path);
    public UsdGeomXformable Xformable => new(Stage, Path);
    public UsdPrim Prim => Stage.GetPrim(Path);

    public static bool TryWrap(UsdPrim prim, out UsdGeomNurbsPatch value)
    {
        if (UsdGeomFacade.TryWrap(prim, UsdGeomSchemaKind.NurbsPatch, out UsdStage? stage))
        {
            value = new UsdGeomNurbsPatch(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    public static UsdGeomNurbsPatch Wrap(UsdPrim prim) => new(
        UsdGeomFacade.Validate(prim, UsdGeomSchemaKind.NurbsPatch, nameof(UsdGeomNurbsPatch)),
        prim.Path);

    public void SetPoints(ReadOnlySpan<UsdVec3f> values) => Prim.SetVec3fArray("points", values);
    public UsdVec3f[] GetPoints() => Prim.GetVec3fArray("points");
    public int UVertexCount
    {
        get => Stage.Native.GetGeomInt32(Path, "uVertexCount");
        set => Stage.Native.SetGeomInt32(Path, "uVertexCount", value);
    }

    public int VVertexCount
    {
        get => Stage.Native.GetGeomInt32(Path, "vVertexCount");
        set => Stage.Native.SetGeomInt32(Path, "vVertexCount", value);
    }

    public int UOrder
    {
        get => Stage.Native.GetGeomInt32(Path, "uOrder");
        set => Stage.Native.SetGeomInt32(Path, "uOrder", value);
    }

    public int VOrder
    {
        get => Stage.Native.GetGeomInt32(Path, "vOrder");
        set => Stage.Native.SetGeomInt32(Path, "vOrder", value);
    }
    public void SetUKnots(ReadOnlySpan<double> values) => Prim.SetDoubleArray("uKnots", values);
    public double[] GetUKnots() => Prim.GetDoubleArray("uKnots");
    public void SetVKnots(ReadOnlySpan<double> values) => Prim.SetDoubleArray("vKnots", values);
    public double[] GetVKnots() => Prim.GetDoubleArray("vKnots");

    private UsdStage Stage => UsdGeomFacade.Require(_stage);
}
