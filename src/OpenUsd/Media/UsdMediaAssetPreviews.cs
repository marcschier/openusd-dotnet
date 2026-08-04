// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Media;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdMediaAssetPreviews : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdMediaAssetPreviews(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }
    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);
    public UsdAssetPath DefaultThumbnail
    {
        get => new(Stage.Native.GetMediaAsset(Path, OpenUsdNativeMediaAssetProperty.DefaultThumbnail));
        set => Stage.Native.SetMediaAsset(Path, OpenUsdNativeMediaAssetProperty.DefaultThumbnail, value.Path);
    }
    public void ClearDefaultThumbnail() =>
        Stage.Native.ClearMediaAsset(Path, OpenUsdNativeMediaAssetProperty.DefaultThumbnail);
    public static UsdMediaAssetPreviews Apply(UsdPrim prim)
    {
        UsdStage stage = UsdMediaSchema.ValidateAttachedPrim(prim);
        stage.Native.ApplyMediaApi(prim.Path, OpenUsdNativeMediaSchemaKind.AssetPreviewsApi);
        return new(stage, prim.Path);
    }
    public static bool TryWrap(UsdPrim prim, out UsdMediaAssetPreviews value)
    {
        if (UsdMediaSchema.TryValidate(prim, OpenUsdNativeMediaSchemaKind.AssetPreviewsApi, out UsdStage? stage))
        {
            value = new(stage!, prim.Path);
            return true;
        }

        value = default;
        return false;
    }
    public static UsdMediaAssetPreviews Wrap(UsdPrim prim) =>
        new(
            UsdMediaSchema.Validate(prim, OpenUsdNativeMediaSchemaKind.AssetPreviewsApi, nameof(UsdMediaAssetPreviews)),
            prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}


