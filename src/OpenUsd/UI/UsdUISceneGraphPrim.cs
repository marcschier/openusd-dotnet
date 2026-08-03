// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.UI;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdUISceneGraphPrim : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdUISceneGraphPrim(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }
    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);

    public string DisplayName
    {
        get => Prim.GetToken("ui:displayName");
        set => Prim.SetToken("ui:displayName", value);
    }
    public string DisplayGroup
    {
        get => Prim.GetToken("ui:displayGroup");
        set => Prim.SetToken("ui:displayGroup", value);
    }
    public static UsdUISceneGraphPrim Apply(UsdPrim prim)
    {
        UsdStage stage = UsdUISchema.ValidateAttachedPrim(prim);
        stage.Native.ApplyUiApi(prim.Path, OpenUsdNativeUiSchemaKind.SceneGraphPrimApi);
        return new(stage, prim.Path);
    }

    public static bool TryWrap(UsdPrim prim, out UsdUISceneGraphPrim value)
    {
        if (UsdUISchema.TryValidate(prim, OpenUsdNativeUiSchemaKind.SceneGraphPrimApi, out UsdStage? stage))
        {
            value = new UsdUISceneGraphPrim(stage!, prim.Path);
            return true;
        }

        value = default;
        return false;
    }
    public static UsdUISceneGraphPrim Wrap(UsdPrim prim) =>
        new(
            UsdUISchema.Validate(prim, OpenUsdNativeUiSchemaKind.SceneGraphPrimApi, nameof(UsdUISceneGraphPrim)),
            prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}



