// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS1591

namespace OpenUsd.Geom;

/// <summary>A validated, bulk-oriented view of a UsdGeomSubset prim.</summary>
[ExcludeFromCodeCoverage(Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomSubset : IUsdStageBound
{
    private readonly UsdStage? _stage;

    internal UsdGeomSubset(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    public string Path { get; }
    public UsdGeomImageable Imageable => new(Stage, Path);
    public UsdGeomXformable Xformable => new(Stage, Path);
    public UsdPrim Prim => Stage.GetPrim(Path);

    public static bool TryWrap(UsdPrim prim, out UsdGeomSubset value)
    {
        if (UsdGeomFacade.TryWrap(prim, UsdGeomSchemaKind.Subset, out UsdStage? stage))
        {
            value = new UsdGeomSubset(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    public static UsdGeomSubset Wrap(UsdPrim prim) => new(
        UsdGeomFacade.Validate(prim, UsdGeomSchemaKind.Subset, nameof(UsdGeomSubset)),
        prim.Path);

    public void SetIndices(ReadOnlySpan<int> values) => Prim.SetInt32Array("indices", values);
    public int[] GetIndices() => Prim.GetInt32Array("indices");
    public string ElementType { get => Prim.GetToken("elementType"); set => Prim.SetToken("elementType", value); }
    public string FamilyName { get => Prim.GetToken("familyName"); set => Prim.SetToken("familyName", value); }

    private UsdStage Stage => UsdGeomFacade.Require(_stage);
}

