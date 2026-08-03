// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Media;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public static class UsdMediaStageExtensions
{
    public static UsdMediaSpatialAudio DefineSpatialAudio(this UsdStage stage, string path)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path);
        stage.Native.DefineMedia(path, OpenUsdNativeMediaSchemaKind.SpatialAudio);
        return new(stage, path);
    }
}


