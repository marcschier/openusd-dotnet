// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Package.Tests;

internal static class RenderDeferredConsumerSource
{
    internal const string Text =
        """
        using System;
        using System.IO;
        using System.Linq;
        using OpenUsd;
        using OpenUsd.Geom;
        using OpenUsd.Render;

        namespace PackageExecutionConsumer;

        internal static class RenderDeferredConsumer
        {
            internal static bool Run()
            {
                string sourcePath = Path.Combine(AppContext.BaseDirectory,
                    "render-deferred-" + Guid.NewGuid().ToString("N") + ".usda");
                string path = Path.ChangeExtension(sourcePath, ".usdc");
                try
                {
                    using (UsdStage source = UsdStage.Create(sourcePath))
                    {
                        var camera = source.DefineCamera("/Camera");
                        camera.HorizontalAperture = 40;
                        camera.VerticalAperture = 20;
                        var settings = source.DefineRenderSettings("/Settings");
                        settings.SettingsBase.SetCamera(camera);
                        settings.SettingsBase.SetResolution(800, 400);
                        var product = source.DefineRenderProduct("/Product");
                        var variable = source.DefineRenderVar("/Beauty");
                        variable.SourceName = "Ci";
                        product.SetOrderedVars(new[] { variable });
                        settings.SetProducts(new[] { product });
                        using var layer = source.GetRootLayer();
                        layer.Export(path);
                    }
                    byte[] sourceBytes = File.ReadAllBytes(path);
                    if (!sourceBytes.AsSpan(0, 8).SequenceEqual("PXR-USDC"u8))
                    {
                        return false;
                    }
                    UsdRenderSpecification snapshot;
                    UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
                    try
                    {
                        snapshot = scheduler.InvokeAsync(stage =>
                        {
                            ulong serial = stage.ChangeSerial;
                            var result = stage.GetRenderSpecification("/Settings")!;
                            if (stage.ChangeSerial != serial)
                            {
                                throw new InvalidOperationException("Crate preparation changed the source.");
                            }
                            return result;
                        }).GetAwaiter().GetResult();
                    }
                    finally
                    {
                        scheduler.DisposeAsync().GetAwaiter().GetResult();
                    }
                    return snapshot.Products.Count == 1 && snapshot.Products[0].Width == 800 &&
                        snapshot.Products[0].ApertureSize == new UsdVec2f(40, 20) &&
                        snapshot.RenderVariables[0].SourceName == "Ci" &&
                        sourceBytes.SequenceEqual(File.ReadAllBytes(path));
                }
                finally
                {
                    File.Delete(path);
                    File.Delete(sourcePath);
                }
            }
        }
        """;
}
