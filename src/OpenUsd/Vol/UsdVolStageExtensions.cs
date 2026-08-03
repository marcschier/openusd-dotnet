// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Vol;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public static class UsdVolStageExtensions
{
    public static UsdVolVolume DefineVolume(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeVolSchemaKind.Volume);
        return new(stage, path);
    }
    public static UsdVolOpenVDBAsset DefineOpenVDBAsset(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeVolSchemaKind.OpenVdbAsset);
        return new(stage, path);
    }
    public static UsdVolField3DAsset DefineField3DAsset(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeVolSchemaKind.Field3dAsset);
        return new(stage, path);
    }
    private static void Define(UsdStage stage, string path, OpenUsdNativeVolSchemaKind kind)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path);
        stage.Native.DefineVol(path, kind);
    }
}


