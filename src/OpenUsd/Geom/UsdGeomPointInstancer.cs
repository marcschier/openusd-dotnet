// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS1591

namespace OpenUsd.Geom;

/// <summary>A validated, bulk-oriented view of a UsdGeomPointInstancer prim.</summary>
[ExcludeFromCodeCoverage(Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomPointInstancer : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdGeomPointInstancer(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    public string Path { get; }
    public UsdGeomImageable Imageable => new(Stage, Path);
    public UsdGeomXformable Xformable => new(Stage, Path);
    public UsdPrim Prim => Stage.GetPrim(Path);

    public static bool TryWrap(UsdPrim prim, out UsdGeomPointInstancer value)
    {
        if (UsdGeomFacade.TryWrap(prim, UsdGeomSchemaKind.PointInstancer, out UsdStage? stage))
        {
            value = new UsdGeomPointInstancer(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    public static UsdGeomPointInstancer Wrap(UsdPrim prim) => new(
        UsdGeomFacade.Validate(prim, UsdGeomSchemaKind.PointInstancer, nameof(UsdGeomPointInstancer)),
        prim.Path);

    public void SetPositions(ReadOnlySpan<UsdVec3f> values) => Prim.SetVec3fArray("positions", values);
    public void SetPositions(ReadOnlySpan<UsdVec3f> values, double timeCode) =>
        Prim.SetVec3fArray("positions", values, timeCode);
    public UsdVec3f[] GetPositions() => Prim.GetVec3fArray("positions");
    public UsdVec3f[] GetPositions(double timeCode) => Prim.GetVec3fArray("positions", timeCode);
    public void SetProtoIndices(ReadOnlySpan<int> values) => Prim.SetInt32Array("protoIndices", values);
    public int[] GetProtoIndices() => Prim.GetInt32Array("protoIndices");
    public void SetOrientations(ReadOnlySpan<UsdQuatf> values) =>
        Stage.Native.SetGeomPointInstancerOrientations(Path, UsdGeomSchema.ToNative(values));
    public UsdQuatf[] GetOrientations() =>
        UsdGeomSchema.FromNative(Stage.Native.GetGeomPointInstancerOrientations(Path));
    public void SetVelocities(ReadOnlySpan<UsdVec3f> values) => Prim.SetVec3fArray("velocities", values);
    public UsdVec3f[] GetVelocities() => Prim.GetVec3fArray("velocities");
    public void SetScales(ReadOnlySpan<UsdVec3f> values) => Prim.SetVec3fArray("scales", values);
    public UsdVec3f[] GetScales() => Prim.GetVec3fArray("scales");
    public UsdRelationship Prototypes => Prim.GetRelationship("prototypes");

    private UsdStage Stage => UsdGeomFacade.Require(_stage);
}
