// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Render;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdRenderVar : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdRenderVar(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }
    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);

    public string DataType
    {
        get => Prim.GetToken("dataType");
        set => Prim.SetToken("dataType", value);
    }
    public string SourceName
    {
        get => Prim.GetString("sourceName");
        set => Prim.SetString("sourceName", value);
    }
    public string SourceType
    {
        get => Prim.GetToken("sourceType");
        set => Prim.SetToken("sourceType", value);
    }

    public static bool TryWrap(UsdPrim prim, out UsdRenderVar value)
    {
        if (UsdRenderSchema.TryValidate(prim, OpenUsdNativeRenderSchemaKind.Var, out UsdStage? stage))
        {
            value = new UsdRenderVar(stage!, prim.Path);
            return true;
        }

        value = default;
        return false;
    }
    public static UsdRenderVar Wrap(UsdPrim prim) =>
        new(
            UsdRenderSchema.Validate(prim, OpenUsdNativeRenderSchemaKind.Var, nameof(UsdRenderVar)),
            prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}


