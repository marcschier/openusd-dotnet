// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Render;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdRenderSettings : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdRenderSettings(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }
    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);

    public UsdRenderSettingsBase SettingsBase => new(Stage, Path);
    public UsdRelationship Products => Prim.GetRelationship("products");
    public void SetProducts(ReadOnlySpan<UsdRenderProduct> products)
    {
        string[] paths = new string[products.Length];
        for (int i = 0; i < products.Length; i++)
        {
            paths[i] = products[i].Path;
        }

        Prim.SetRelationshipTargets("products", paths);
    }
    public UsdAttribute IncludedPurposes => Prim.GetAttribute("includedPurposes");
    public UsdAttribute MaterialBindingPurposes => Prim.GetAttribute("materialBindingPurposes");
    public string RenderingColorSpace
    {
        get => Prim.GetToken("renderingColorSpace");
        set => Prim.SetToken("renderingColorSpace", value);
    }

    public static bool TryWrap(UsdPrim prim, out UsdRenderSettings value)
    {
        if (UsdRenderSchema.TryValidate(prim, OpenUsdNativeRenderSchemaKind.Settings, out UsdStage? stage))
        {
            value = new UsdRenderSettings(stage!, prim.Path);
            return true;
        }

        value = default;
        return false;
    }
    public static UsdRenderSettings Wrap(UsdPrim prim) =>
        new(
            UsdRenderSchema.Validate(prim, OpenUsdNativeRenderSchemaKind.Settings, nameof(UsdRenderSettings)),
            prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}




