// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Render;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdRenderProduct : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdRenderProduct(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }
    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);

    public UsdRenderSettingsBase SettingsBase => new(Stage, Path);
    public UsdRelationship OrderedVars => Prim.GetRelationship("orderedVars");
    public void SetOrderedVars(ReadOnlySpan<UsdRenderVar> vars)
    {
        string[] paths = new string[vars.Length];
        for (int i = 0; i < vars.Length; i++)
        {
            paths[i] = vars[i].Path;
        }

        Prim.SetRelationshipTargets("orderedVars", paths);
    }
    public string ProductType
    {
        get => Prim.GetToken("productType");
        set => Prim.SetToken("productType", value);
    }
    public string ProductName
    {
        get => Prim.GetToken("productName");
        set => Prim.SetToken("productName", value);
    }

    public static bool TryWrap(UsdPrim prim, out UsdRenderProduct value)
    {
        if (UsdRenderSchema.TryValidate(prim, OpenUsdNativeRenderSchemaKind.Product, out UsdStage? stage))
        {
            value = new UsdRenderProduct(stage!, prim.Path);
            return true;
        }

        value = default;
        return false;
    }
    public static UsdRenderProduct Wrap(UsdPrim prim) =>
        new(
            UsdRenderSchema.Validate(prim, OpenUsdNativeRenderSchemaKind.Product, nameof(UsdRenderProduct)),
            prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}

