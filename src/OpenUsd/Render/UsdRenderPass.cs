// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Render;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdRenderPass : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdRenderPass(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }
    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);

    public UsdRelationship RenderSource => Prim.GetRelationship("renderSource");
    public UsdRelationship InputPasses => Prim.GetRelationship("inputPasses");
    public string PassType
    {
        get => Prim.GetToken("passType");
        set => Prim.SetToken("passType", value);
    }
    public string Command
    {
        get => Prim.GetString("command");
        set => Prim.SetString("command", value);
    }
    public string FileName
    {
        get => Prim.GetString("fileName");
        set => Prim.SetString("fileName", value);
    }

    public static bool TryWrap(UsdPrim prim, out UsdRenderPass value)
    {
        if (UsdRenderSchema.TryValidate(prim, OpenUsdNativeRenderSchemaKind.Pass, out UsdStage? stage))
        {
            value = new UsdRenderPass(stage!, prim.Path);
            return true;
        }

        value = default;
        return false;
    }
    public static UsdRenderPass Wrap(UsdPrim prim) =>
        new(
            UsdRenderSchema.Validate(prim, OpenUsdNativeRenderSchemaKind.Pass, nameof(UsdRenderPass)),
            prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}


