// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS1591

namespace OpenUsd.Geom;

/// <summary>A validated, bulk-oriented view of a UsdGeomTetMesh prim.</summary>
[ExcludeFromCodeCoverage(Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomTetMesh : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdGeomTetMesh(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    public string Path { get; }
    public UsdGeomImageable Imageable => new(Stage, Path);
    public UsdGeomXformable Xformable => new(Stage, Path);
    public UsdPrim Prim => Stage.GetPrim(Path);

    public static bool TryWrap(UsdPrim prim, out UsdGeomTetMesh value)
    {
        if (UsdGeomFacade.TryWrap(prim, UsdGeomSchemaKind.TetMesh, out UsdStage? stage))
        {
            value = new UsdGeomTetMesh(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    public static UsdGeomTetMesh Wrap(UsdPrim prim) => new(
        UsdGeomFacade.Validate(prim, UsdGeomSchemaKind.TetMesh, nameof(UsdGeomTetMesh)),
        prim.Path);

    public void SetPoints(ReadOnlySpan<UsdVec3f> values) => Prim.SetVec3fArray("points", values);
    public UsdVec3f[] GetPoints() => Prim.GetVec3fArray("points");
    public void SetTetVertexIndices(ReadOnlySpan<int> values) => Prim.SetInt32Array("tetVertexIndices", values);
    public int[] GetTetVertexIndices() => Prim.GetInt32Array("tetVertexIndices");
    public void SetSurfaceFaceVertexIndices(ReadOnlySpan<int> values) =>
        Prim.SetInt32Array("surfaceFaceVertexIndices", values);
    public int[] GetSurfaceFaceVertexIndices() => Prim.GetInt32Array("surfaceFaceVertexIndices");

    private UsdStage Stage => UsdGeomFacade.Require(_stage);
}
