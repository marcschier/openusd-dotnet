// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CS1591

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Interop;

namespace OpenUsd.Render;

[ExcludeFromCodeCoverage(
    Justification = "Exercised by clean native and NativeAOT integration probes.")]
public static class UsdRenderStageExtensions
{
    /// <summary>Reads a detached native render specification at uniform/default time.</summary>
    /// <remarks>
    /// Returns null only when no default renderSettingsPrimPath is authored.
    /// An explicit or authored invalid path fails. No output file or RenderPass command is executed.
    /// </remarks>
    public static UsdRenderSpecification? GetRenderSpecification(this UsdStage stage, string? settingsPath = null)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (settingsPath is not null)
        {
            UsdPath.ValidateAbsolutePrimPath(settingsPath);
        }
        OpenUsdNativeRenderSpecification? native = stage.Native.GetRenderSpecification(settingsPath);
        if (native is null)
        {
            return null;
        }
        var products = new UsdRenderProductSpecification[native.Products.Length];
        for (int index = 0; index < products.Length; index++)
        {
            OpenUsdNativeRenderProductSpecification product = native.Products[index];
            OpenUsdNativeRenderProductRecord values = product.Values;
            products[index] = new UsdRenderProductSpecification(
                product.Path, product.Name, product.ProductType, product.CameraPath,
                values.Width, values.Height, values.PixelAspectRatio, product.AspectRatioConformPolicy,
                new UsdVec2f(values.ApertureWidth, values.ApertureHeight),
                new UsdVec4f(
                    values.DataWindowMinX, values.DataWindowMinY, values.DataWindowMaxX, values.DataWindowMaxY),
                values.DisableMotionBlur != 0, values.DisableDepthOfField != 0,
                product.RenderVariableIndices, product.NamespacedSettingNames);
        }
        var variables = new UsdRenderVariableSpecification[native.RenderVariables.Length];
        for (int index = 0; index < variables.Length; index++)
        {
            OpenUsdNativeRenderVariableSpecification variable = native.RenderVariables[index];
            variables[index] = new UsdRenderVariableSpecification(
                variable.Path, variable.DataType, variable.SourceName, variable.SourceType,
                variable.NamespacedSettingNames);
        }
        return new UsdRenderSpecification(
            native.SettingsPath, products, variables, native.IncludedPurposes,
            native.MaterialBindingPurposes, native.RenderingColorSpace, native.NamespacedSettingNames);
    }

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
