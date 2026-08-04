// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Proc;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public static class UsdProcStageExtensions
{
    public static UsdProcGenerativeProcedural DefineGenerativeProcedural(this UsdStage stage, string path)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path);
        stage.Native.DefineProc(path, OpenUsdNativeProcSchemaKind.GenerativeProcedural);
        return new(stage, path);
    }
}


