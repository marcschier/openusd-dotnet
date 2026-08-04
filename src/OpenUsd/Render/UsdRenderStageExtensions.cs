// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Render;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public static class UsdRenderStageExtensions
{
    public static UsdRenderSettings DefineRenderSettings(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeRenderSchemaKind.Settings);
        return new(stage, path);
    }
    public static UsdRenderProduct DefineRenderProduct(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeRenderSchemaKind.Product);
        return new(stage, path);
    }
    public static UsdRenderVar DefineRenderVar(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeRenderSchemaKind.Var);
        return new(stage, path);
    }
    public static UsdRenderPass DefineRenderPass(this UsdStage stage, string path)
    {
        Define(stage, path, OpenUsdNativeRenderSchemaKind.Pass);
        return new(stage, path);
    }
    private static void Define(UsdStage stage, string path, OpenUsdNativeRenderSchemaKind kind)
    {
        ArgumentNullException.ThrowIfNull(stage);
        UsdPath.ValidateAbsolutePrimPath(path);
        stage.Native.DefineRender(path, kind);
    }
}


