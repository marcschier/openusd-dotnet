// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.UI;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdUINodeGraphNode : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdUINodeGraphNode(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }
    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);

    public UsdAttribute Pos => Prim.GetAttribute("ui:nodegraph:node:pos");
    public UsdVec2f Position
    {
        get => UsdVec2f.FromNative(Stage.Native.GetUiVec2f(Path, OpenUsdNativeUiVec2fProperty.NodePos));
        set => Stage.Native.SetUiVec2f(Path, OpenUsdNativeUiVec2fProperty.NodePos, value.ToNative());
    }
    public UsdAttribute Size => Prim.GetAttribute("ui:nodegraph:node:size");
    public UsdVec2f NodeSize
    {
        get => UsdVec2f.FromNative(Stage.Native.GetUiVec2f(Path, OpenUsdNativeUiVec2fProperty.NodeSize));
        set => Stage.Native.SetUiVec2f(Path, OpenUsdNativeUiVec2fProperty.NodeSize, value.ToNative());
    }
    public UsdAttribute StackingOrder => Prim.GetAttribute("ui:nodegraph:node:stackingOrder");
    public UsdVec3f DisplayColor
    {
        get => Prim.GetColor3f("ui:nodegraph:node:displayColor");
        set => Prim.SetColor3f("ui:nodegraph:node:displayColor", value);
    }
    public UsdAttribute Icon => Prim.GetAttribute("ui:nodegraph:node:icon");
    public string ExpansionState
    {
        get => Prim.GetToken("ui:nodegraph:node:expansionState");
        set => Prim.SetToken("ui:nodegraph:node:expansionState", value);
    }
    public string DocUri
    {
        get => Prim.GetString("ui:nodegraph:node:docURI");
        set => Prim.SetString("ui:nodegraph:node:docURI", value);
    }
    public static UsdUINodeGraphNode Apply(UsdPrim prim)
    {
        UsdStage stage = UsdUISchema.ValidateAttachedPrim(prim);
        stage.Native.ApplyUiApi(prim.Path, OpenUsdNativeUiSchemaKind.NodeGraphNodeApi);
        return new(stage, prim.Path);
    }

    public static bool TryWrap(UsdPrim prim, out UsdUINodeGraphNode value)
    {
        if (UsdUISchema.TryValidate(prim, OpenUsdNativeUiSchemaKind.NodeGraphNodeApi, out UsdStage? stage))
        {
            value = new UsdUINodeGraphNode(stage!, prim.Path);
            return true;
        }

        value = default;
        return false;
    }
    public static UsdUINodeGraphNode Wrap(UsdPrim prim) =>
        new(
            UsdUISchema.Validate(prim, OpenUsdNativeUiSchemaKind.NodeGraphNodeApi, nameof(UsdUINodeGraphNode)),
            prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}



