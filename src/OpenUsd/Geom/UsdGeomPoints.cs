// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS1591

namespace OpenUsd.Geom;

/// <summary>A validated, bulk-oriented view of a UsdGeomPoints prim.</summary>
[ExcludeFromCodeCoverage(Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomPoints : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdGeomPoints(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    public string Path { get; }
    public UsdGeomImageable Imageable => new(Stage, Path);
    public UsdGeomXformable Xformable => new(Stage, Path);
    public UsdPrim Prim => Stage.GetPrim(Path);

    public static bool TryWrap(UsdPrim prim, out UsdGeomPoints value)
    {
        if (UsdGeomFacade.TryWrap(prim, UsdGeomSchemaKind.Points, out UsdStage? stage))
        {
            value = new UsdGeomPoints(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    public static UsdGeomPoints Wrap(UsdPrim prim) => new(
        UsdGeomFacade.Validate(prim, UsdGeomSchemaKind.Points, nameof(UsdGeomPoints)),
        prim.Path);

    public void SetPoints(ReadOnlySpan<UsdVec3f> values) => Prim.SetVec3fArray("points", values);
    public void SetPoints(ReadOnlySpan<UsdVec3f> values, double timeCode) =>
        Prim.SetVec3fArray("points", values, timeCode);
    public UsdVec3f[] GetPoints() => Prim.GetVec3fArray("points");
    public UsdVec3f[] GetPoints(double timeCode) => Prim.GetVec3fArray("points", timeCode);
    public void SetWidths(ReadOnlySpan<float> values) => Prim.SetFloatArray("widths", values);
    public float[] GetWidths() => Prim.GetFloatArray("widths");
    public void SetNormals(ReadOnlySpan<UsdVec3f> values) => Prim.SetVec3fArray("normals", values);
    public UsdVec3f[] GetNormals() => Prim.GetVec3fArray("normals");
    public void SetVelocities(ReadOnlySpan<UsdVec3f> values) => Prim.SetVec3fArray("velocities", values);
    public UsdVec3f[] GetVelocities() => Prim.GetVec3fArray("velocities");
    public void SetAccelerations(ReadOnlySpan<UsdVec3f> values) => Prim.SetVec3fArray("accelerations", values);
    public UsdVec3f[] GetAccelerations() => Prim.GetVec3fArray("accelerations");
    public void SetExtent(UsdExtent3f extent) => UsdGeomSchema.SetExtent(Prim, extent);
    public UsdExtent3f GetExtent() => UsdGeomSchema.GetExtent(Prim);

    private UsdStage Stage => UsdGeomFacade.Require(_stage);
}
