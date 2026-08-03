// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS1591

namespace OpenUsd.Geom;

/// <summary>A validated view of UsdGeomModelAPI data on a prim.</summary>
[ExcludeFromCodeCoverage(Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdGeomModelAPI : IUsdStageBound
{
    private readonly UsdStage? _stage;

    public UsdGeomModelAPI(UsdPrim prim)
    {
        _stage = UsdGeomFacade.Validate(prim, UsdGeomSchemaKind.ModelAPI, nameof(UsdGeomModelAPI));
        Path = prim.Path;
    }

    internal UsdGeomModelAPI(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }

    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);
    public string DrawMode
    {
        get => Prim.GetToken("model:drawMode");
        set => Prim.SetToken("model:drawMode", value);
    }

    public string CardGeometry
    {
        get => Prim.GetToken("model:cardGeometry");
        set => Prim.SetToken("model:cardGeometry", value);
    }

    public bool ApplyDrawMode
    {
        get => Prim.GetBool("model:applyDrawMode");
        set => Prim.SetBool("model:applyDrawMode", value);
    }
    public void SetExtentsHint(ReadOnlySpan<UsdVec3f> values) => Prim.SetVec3fArray("extentsHint", values);
    public UsdVec3f[] GetExtentsHint() => Prim.GetVec3fArray("extentsHint");

    public static bool TryWrap(UsdPrim prim, out UsdGeomModelAPI value)
    {
        if (UsdGeomFacade.TryWrap(prim, UsdGeomSchemaKind.ModelAPI, out UsdStage? stage))
        {
            value = new UsdGeomModelAPI(stage!, prim.Path);
            return true;
        }
        value = default;
        return false;
    }

    public static UsdGeomModelAPI Wrap(UsdPrim prim) => new(prim);

    private UsdStage Stage => UsdGeomFacade.Require(_stage);
}
