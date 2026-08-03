// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS1591

namespace OpenUsd.Geom;

/// <summary>A path-based view of one UsdGeom primvar attribute.</summary>
[ExcludeFromCodeCoverage(Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomPrimvar : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdGeomPrimvar(UsdStage stage, string primPath, string name)
    {
        _stage = stage;
        PrimPath = primPath;
        Name = name;
    }

    public string PrimPath { get; }
    public string Name { get; }
    public string AttributeName => "primvars:" + Name;

    public UsdGeomInterpolation Interpolation
    {
        get => (UsdGeomInterpolation)Array.IndexOf(
            ["constant", "uniform", "varying", "vertex", "faceVarying"],
            Prim.GetToken(AttributeName + ":interpolation"));
        set => Prim.SetToken(AttributeName + ":interpolation", ToInterpolationToken(value));
    }

    public int ElementSize
    {
        get => Stage.Native.GetGeomInt32(PrimPath, AttributeName + ":elementSize");
        set => Stage.Native.SetGeomInt32(PrimPath, AttributeName + ":elementSize", value);
    }

    public UsdPrim Prim => Stage.GetPrim(PrimPath);
    public void SetFloatArray(ReadOnlySpan<float> values) => Prim.SetFloatArray(AttributeName, values);
    public float[] GetFloatArray() => Prim.GetFloatArray(AttributeName);
    public void SetInt32Array(ReadOnlySpan<int> values) => Prim.SetInt32Array(AttributeName, values);
    public int[] GetInt32Array() => Prim.GetInt32Array(AttributeName);
    public void SetVec2fArray(ReadOnlySpan<UsdVec2f> values) => Prim.SetVec2fArray(AttributeName, values);
    public UsdVec2f[] GetVec2fArray() => Prim.GetVec2fArray(AttributeName);
    public void SetVec3fArray(ReadOnlySpan<UsdVec3f> values) => Prim.SetVec3fArray(AttributeName, values);
    public UsdVec3f[] GetVec3fArray() => Prim.GetVec3fArray(AttributeName);
    public void SetIndices(ReadOnlySpan<int> values) => Prim.SetInt32Array(AttributeName + ":indices", values);
    public int[] GetIndices() => Prim.GetInt32Array(AttributeName + ":indices");

    private UsdStage Stage => UsdGeomFacade.Require(_stage);

    private static string ToInterpolationToken(UsdGeomInterpolation interpolation) => interpolation switch
    {
        UsdGeomInterpolation.Constant => "constant",
        UsdGeomInterpolation.Uniform => "uniform",
        UsdGeomInterpolation.Varying => "varying",
        UsdGeomInterpolation.Vertex => "vertex",
        UsdGeomInterpolation.FaceVarying => "faceVarying",
        _ => throw new ArgumentOutOfRangeException(nameof(interpolation))
    };
}

