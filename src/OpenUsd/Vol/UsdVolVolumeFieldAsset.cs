// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Geom;
using OpenUsd.Interop;

namespace OpenUsd.Vol;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public readonly struct UsdVolVolumeFieldAsset : IUsdStageBound
{
    private readonly UsdStage? _stage;
    internal UsdVolVolumeFieldAsset(UsdStage stage, string path)
    {
        _stage = stage;
        Path = path;
    }
    public string Path { get; }
    public UsdPrim Prim => Stage.GetPrim(Path);
    public UsdGeomXformable Xformable => new(Stage, Path);

    public UsdAssetPath FilePath
    {
        get => new(Stage.Native.GetVolAsset(Path, OpenUsdNativeVolAssetProperty.FilePath));
        set => Stage.Native.SetVolAsset(Path, OpenUsdNativeVolAssetProperty.FilePath, value.Path);
    }
    public string FieldName
    {
        get => Prim.GetToken("fieldName");
        set => Prim.SetToken("fieldName", value);
    }
    public int FieldIndex
    {
        get => Stage.Native.GetVolFieldIndex(Path);
        set => Stage.Native.SetVolFieldIndex(Path, value);
    }
    public string FieldDataType
    {
        get => Prim.GetToken("fieldDataType");
        set => Prim.SetToken("fieldDataType", value);
    }
    public string VectorDataRoleHint
    {
        get => Prim.GetToken("vectorDataRoleHint");
        set => Prim.SetToken("vectorDataRoleHint", value);
    }

    public static bool TryWrap(UsdPrim prim, out UsdVolVolumeFieldAsset value)
    {
        if (UsdVolSchema.TryValidate(prim, OpenUsdNativeVolSchemaKind.VolumeFieldAsset, out UsdStage? stage))
        {
            value = new UsdVolVolumeFieldAsset(stage!, prim.Path);
            return true;
        }

        value = default;
        return false;
    }
    public static UsdVolVolumeFieldAsset Wrap(UsdPrim prim) =>
        new(
            UsdVolSchema.Validate(prim, OpenUsdNativeVolSchemaKind.VolumeFieldAsset, nameof(UsdVolVolumeFieldAsset)),
            prim.Path);
    private UsdStage Stage => _stage ?? throw new InvalidOperationException("The schema is not attached to a stage.");
}

