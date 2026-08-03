// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS1591

namespace OpenUsd.Geom;

/// <summary>A validated, bulk-oriented view of a UsdGeomNurbsCurves prim.</summary>
[ExcludeFromCodeCoverage(Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomNurbsCurves : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdGeomNurbsCurves(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    public string Path { get; }
    public UsdGeomImageable Imageable => new(Stage, Path);
    public UsdGeomXformable Xformable => new(Stage, Path);
    public UsdPrim Prim => Stage.GetPrim(Path);

    public static bool TryWrap(UsdPrim prim, out UsdGeomNurbsCurves value)
    {
        if (UsdGeomFacade.TryWrap(prim, UsdGeomSchemaKind.NurbsCurves, out UsdStage? stage))
        {
            value = new UsdGeomNurbsCurves(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    public static UsdGeomNurbsCurves Wrap(UsdPrim prim) => new(
        UsdGeomFacade.Validate(prim, UsdGeomSchemaKind.NurbsCurves, nameof(UsdGeomNurbsCurves)),
        prim.Path);

    public void SetPoints(ReadOnlySpan<UsdVec3f> values) => Prim.SetVec3fArray("points", values);
    public UsdVec3f[] GetPoints() => Prim.GetVec3fArray("points");
    public void SetCurveVertexCounts(ReadOnlySpan<int> values) => Prim.SetInt32Array("curveVertexCounts", values);
    public int[] GetCurveVertexCounts() => Prim.GetInt32Array("curveVertexCounts");
    public void SetOrder(ReadOnlySpan<int> values) => Prim.SetInt32Array("order", values);
    public int[] GetOrder() => Prim.GetInt32Array("order");
    public void SetKnots(ReadOnlySpan<double> values) => Prim.SetDoubleArray("knots", values);
    public double[] GetKnots() => Prim.GetDoubleArray("knots");
    public void SetWidths(ReadOnlySpan<float> values) => Prim.SetFloatArray("widths", values);
    public float[] GetWidths() => Prim.GetFloatArray("widths");

    private UsdStage Stage => UsdGeomFacade.Require(_stage);
}

