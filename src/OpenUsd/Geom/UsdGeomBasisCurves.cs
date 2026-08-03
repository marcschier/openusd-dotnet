// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS1591

namespace OpenUsd.Geom;

/// <summary>A validated, bulk-oriented view of a UsdGeomBasisCurves prim.</summary>
[ExcludeFromCodeCoverage(Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomBasisCurves : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdGeomBasisCurves(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    public string Path { get; }
    public UsdGeomImageable Imageable => new(Stage, Path);
    public UsdGeomXformable Xformable => new(Stage, Path);
    public UsdPrim Prim => Stage.GetPrim(Path);

    public static bool TryWrap(UsdPrim prim, out UsdGeomBasisCurves value)
    {
        if (UsdGeomFacade.TryWrap(prim, UsdGeomSchemaKind.BasisCurves, out UsdStage? stage))
        {
            value = new UsdGeomBasisCurves(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    public static UsdGeomBasisCurves Wrap(UsdPrim prim) => new(
        UsdGeomFacade.Validate(prim, UsdGeomSchemaKind.BasisCurves, nameof(UsdGeomBasisCurves)),
        prim.Path);

    public void SetPoints(ReadOnlySpan<UsdVec3f> values) => Prim.SetVec3fArray("points", values);
    public UsdVec3f[] GetPoints() => Prim.GetVec3fArray("points");
    public void SetCurveVertexCounts(ReadOnlySpan<int> values) => Prim.SetInt32Array("curveVertexCounts", values);
    public int[] GetCurveVertexCounts() => Prim.GetInt32Array("curveVertexCounts");
    public void SetWidths(ReadOnlySpan<float> values) => Prim.SetFloatArray("widths", values);
    public float[] GetWidths() => Prim.GetFloatArray("widths");
    public void SetNormals(ReadOnlySpan<UsdVec3f> values) => Prim.SetVec3fArray("normals", values);
    public UsdVec3f[] GetNormals() => Prim.GetVec3fArray("normals");
    public string Type { get => Prim.GetToken("type"); set => Prim.SetToken("type", value); }
    public string Basis { get => Prim.GetToken("basis"); set => Prim.SetToken("basis", value); }
    public string WrapMode { get => Prim.GetToken("wrap"); set => Prim.SetToken("wrap", value); }

    private UsdStage Stage => UsdGeomFacade.Require(_stage);
}

