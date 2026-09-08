// Copyright (c) marcschier. Licensed under the MIT License.

using System.Security.Cryptography;
using OpenUsd.Interop;
using OpenUsd.Render;

namespace OpenUsd.Tests;

public sealed class UsdRenderDeferredNativeTests
{
    [Test]
    [Arguments(".usda")]
    [Arguments(".usdc")]
    public async Task PreparedNativeSpecificationPreservesSourceAndSurvivesSchedulerRelease(string extension)
    {
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(plugins))
        {
            Skip.Test("This data probe requires the native runtime.");
        }
        if (extension == ".usdc" &&
            Environment.GetEnvironmentVariable("OPENUSD_TEST_STORAGE_ADMISSION") != "1")
        {
            Skip.Test("This crate probe requires the pinned storage-admission SDK profile.");
        }
        _ = OpenUsdNativeRuntime.RegisterPlugins(plugins!);
        string directory = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT")
            ?? Path.Combine(AppContext.BaseDirectory, "native-work");
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, $"render-preparation-{Guid.NewGuid():N}.usda");
        string path = Path.ChangeExtension(sourcePath, extension);
        try
        {
            await File.WriteAllTextAsync(sourcePath,
                """
                #usda 1.0
                (renderSettingsPrimPath = "/Settings")
                def Camera "Camera"
                {
                    float horizontalAperture = 40
                    float verticalAperture = 20
                    float horizontalAperture.timeSamples = { 1: 80, 2: 120 }
                }
                def Scope "Forward"
                {
                    rel camera = </Camera>
                    rel products = </Product>
                    rel vars = </Beauty>
                }
                class "Base"
                {
                    uniform token[] includedPurposes = ["proxy", "render"]
                }
                def RenderSettings "Settings" (prepend inherits = </Base>)
                {
                    rel camera = </Forward.camera>
                    rel products = </Forward.products>
                    uniform int2 resolution = (800, 400)
                    custom string renderer:opaque = "untouched graph input"
                }
                def RenderProduct "Product"
                {
                    token productName = "never-written.exr"
                    uniform int2 resolution = (400, 400)
                    uniform token aspectRatioConformPolicy = "cropAperture"
                    uniform float4 dataWindowNDC = (0.125, 0.25, 0.875, 1)
                    rel orderedVars = </Forward.vars>
                }
                def RenderVar "Beauty"
                {
                    token dataType = "color3f"
                    string sourceName = "Ci"
                    token sourceType = "raw"
                }
                """);
            if (extension == ".usdc")
            {
                using UsdStage source = UsdStage.Open(sourcePath);
                using UsdLayer layer = source.GetRootLayer();
                layer.Export(path);
                byte[] bytes = await File.ReadAllBytesAsync(path);
                await Assert.That(bytes.AsSpan(0, 8).SequenceEqual("PXR-USDC"u8)).IsTrue();
            }
            byte[] original = SHA256.HashData(await File.ReadAllBytesAsync(path));
            UsdRenderSpecification snapshot;
            await using (UsdStageScheduler scheduler = UsdStageScheduler.Open(path))
            {
                snapshot = await scheduler.InvokeAsync(stage =>
                {
                    ulong serial = stage.ChangeSerial;
                    UsdRenderSpecification result = stage.GetRenderSpecification()!;
                    if (stage.ChangeSerial != serial)
                    {
                        throw new InvalidOperationException("Render preparation changed the stage revision.");
                    }
                    return result;
                });
            }
            await Assert.That(snapshot.SettingsPath).IsEqualTo("/Settings");
            await Assert.That(snapshot.Products.Count).IsEqualTo(1);
            UsdRenderProductSpecification product = snapshot.Products[0];
            await Assert.That(product.Path).IsEqualTo("/Product");
            await Assert.That(product.CameraPath).IsEqualTo("/Camera");
            await Assert.That(product.Width).IsEqualTo(400);
            await Assert.That(product.Height).IsEqualTo(400);
            await Assert.That(product.ApertureSize).IsEqualTo(new UsdVec2f(20, 20));
            await Assert.That(product.DataWindowNdc).IsEqualTo(new UsdVec4f(0.125f, 0.25f, 0.875f, 1));
            await Assert.That(product.RenderVariableIndices[0]).IsEqualTo(0);
            await Assert.That(snapshot.RenderVariables[0].Path).IsEqualTo("/Beauty");
            await Assert.That(snapshot.RenderVariables[0].SourceName).IsEqualTo("Ci");
            await Assert.That(snapshot.IncludedPurposes.ToArray()).IsEquivalentTo(["proxy", "render"]);
            await Assert.That(snapshot.NamespacedSettingNames).Contains("renderer:opaque");
            await Assert.That(SHA256.HashData(await File.ReadAllBytesAsync(path)).SequenceEqual(original)).IsTrue();
        }
        finally
        {
            File.Delete(path);
            if (path != sourcePath)
            {
                File.Delete(sourcePath);
            }
        }
    }
}
