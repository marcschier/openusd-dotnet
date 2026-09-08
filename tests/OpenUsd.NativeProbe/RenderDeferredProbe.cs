// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;
using OpenUsd.Render;

namespace OpenUsd.NativeProbe;

internal static class RenderDeferredProbe
{
    internal static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, $"render-crate-aot-{Guid.NewGuid():N}.usda");
        string path = Path.ChangeExtension(sourcePath, ".usdc");
        try
        {
            using (UsdStage source = UsdStage.Create(sourcePath))
            {
                UsdGeomCamera camera = source.DefineCamera("/Camera");
                camera.HorizontalAperture = 40;
                camera.VerticalAperture = 20;
                UsdRenderSettings settings = source.DefineRenderSettings("/Settings");
                settings.SettingsBase.SetCamera(camera);
                settings.SettingsBase.SetResolution(800, 400);
                UsdRenderProduct product = source.DefineRenderProduct("/Product");
                product.ProductName = "not-written.exr";
                UsdRenderVar variable = source.DefineRenderVar("/Beauty");
                variable.SourceName = "Ci";
                product.SetOrderedVars([variable]);
                settings.SetProducts([product]);
                using UsdLayer layer = source.GetRootLayer();
                layer.Export(path);
            }
            byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            if (!bytes.AsSpan(0, 8).SequenceEqual("PXR-USDC"u8))
            {
                throw new InvalidOperationException("The crate probe was not actual USDC.");
            }
            UsdRenderSpecification snapshot;
            await using (UsdStageScheduler scheduler = UsdStageScheduler.Open(path))
            {
                snapshot = await scheduler.InvokeAsync(stage =>
                {
                    ulong serial = stage.ChangeSerial;
                    UsdRenderSpecification result = stage.GetRenderSpecification("/Settings")!;
                    if (serial != stage.ChangeSerial)
                    {
                        throw new InvalidOperationException("Crate preparation mutated the source.");
                    }
                    return result;
                }).ConfigureAwait(false);
            }
            byte[] unchanged = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            if (snapshot.Products.Count != 1 || snapshot.Products[0].Width != 800 ||
                snapshot.Products[0].ApertureSize != new UsdVec2f(40, 20) ||
                snapshot.RenderVariables[0].Path != "/Beauty" || snapshot.RenderVariables[0].SourceName != "Ci" ||
                !bytes.SequenceEqual(unchanged))
            {
                throw new InvalidOperationException("The detached native crate specification changed after release.");
            }
            Console.WriteLine("RENDER_DEFERRED_MANAGED_OK: actual USDC, native spec, source bytes, released scheduler");
        }
        finally
        {
            File.Delete(path);
            File.Delete(sourcePath);
        }
    }
}
