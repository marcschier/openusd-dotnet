// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;
using OpenUsd.Render;

namespace OpenUsd.NativeProbe;

internal static class RenderSpecificationProbe
{
    internal static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"render-specification-aot-{Guid.NewGuid():N}.usda");
        try
        {
            UsdRenderSpecification specification;
            UsdRenderProductSpecification product;
            UsdRenderVariableSpecification variable;
            await using (UsdStageScheduler scheduler = UsdStageScheduler.Create(path))
            {
                specification = await scheduler.InvokeAsync(stage =>
                {
                    if (stage.GetRenderSpecification() is not null)
                    {
                        throw new InvalidOperationException("An unauthored render default was not absent.");
                    }
                    UsdGeomCamera camera = stage.DefineCamera("/Camera");
                    camera.HorizontalAperture = 40;
                    camera.VerticalAperture = 20;
                    UsdRenderSettings settings = stage.DefineRenderSettings("/Settings");
                    settings.SettingsBase.SetCamera(camera);
                    settings.SettingsBase.SetResolution(800, 400);
                    settings.Prim.SetString("renderer:unsupported", "not evaluated");
                    UsdRenderProduct authoredProduct = stage.DefineRenderProduct("/Product");
                    UsdRenderSettingsBase productSettings = authoredProduct.SettingsBase;
                    productSettings.SetResolution(400, 400);
                    productSettings.AspectRatioConformPolicy = "cropAperture";
                    authoredProduct.ProductName = "probe.exr";
                    UsdRenderVar authoredVariable = stage.DefineRenderVar("/Variable");
                    authoredVariable.SourceType = "lpe";
                    authoredVariable.SourceName = "C<RD>L";
                    authoredProduct.SetOrderedVars([authoredVariable]);
                    settings.SetProducts([authoredProduct]);
                    return stage.GetRenderSpecification("/Settings")
                        ?? throw new InvalidOperationException("Explicit render settings unexpectedly returned null.");
                }).ConfigureAwait(false);
                product = await scheduler.InvokeAsync(stage =>
                    stage.GetRenderSpecification("/Settings")!.Products[0]).ConfigureAwait(false);
                variable = await scheduler.InvokeAsync(stage =>
                    stage.GetRenderSpecification("/Settings")!.RenderVariables[0]).ConfigureAwait(false);
            }
            if (specification.SettingsPath != "/Settings" || specification.Products.Count != 1 ||
                specification.RenderVariables.Count != 1 || product.Width != 400 || product.Height != 400 ||
                product.CameraPath != "/Camera" || product.ApertureSize != new UsdVec2f(20, 20) ||
                product.RenderVariableIndices.Count != 1 || product.RenderVariableIndices[0] != 0 ||
                variable.SourceType != "lpe" || variable.SourceName != "C<RD>L" ||
                specification.NamespacedSettingNames.Count != 1 ||
                specification.NamespacedSettingNames[0] != "renderer:unsupported")
            {
                throw new InvalidOperationException(
                    "Native render snapshots did not survive scheduler disposal intact.");
            }
            Console.WriteLine("RENDER_SPECIFICATION_MANAGED_OK: native evaluation, detached scheduler DTOs, metadata");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
