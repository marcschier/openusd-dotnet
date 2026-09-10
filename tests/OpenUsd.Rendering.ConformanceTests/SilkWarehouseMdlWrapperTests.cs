// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkWarehouseMdlWrapperTests
{
    [Test]
    public async Task SdkVariantConstantsReachHydraAndTheRenderedHdrPixels()
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_REQUIRED") != "1")
        {
            Skip.Test(
                "Set OPENUSD_WAREHOUSE_MDL_REQUIRED=1 with the qualified SDK adapter, modules and native runtime.");
            return;
        }
        string plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH") ??
            throw new InvalidOperationException("The matched native plugin directory is required.");
        string wrapper = Environment.GetEnvironmentVariable("OPENUSD_WAREHOUSE_MDL_WRAPPER") ??
            throw new InvalidOperationException("The repository-owned variants.mdl fixture path is required.");
        if (!Path.IsPathFullyQualified(wrapper) || !File.Exists(wrapper))
        {
            throw new ArgumentException(
                "Use the existing absolute SDK variant fixture, not a synthesized base module.");
        }
        string root = Directory.CreateTempSubdirectory("warehouse-mdl-render-").FullName;
        try
        {
            string projected = Path.Combine(root, "projected.usda");
            string reference = Path.Combine(root, "reference.usda");
            string control = Path.Combine(root, "base-defaults.usda");
            await File.WriteAllTextAsync(projected, Scene(Material(
                $$"""
                uniform token info:implementationSource = "sourceAsset"
                uniform asset info:mdl:sourceAsset = @{{wrapper.Replace('\\', '/')}}@
                uniform token info:mdl:sourceAsset:subIdentifier = "constants"
                token outputs:out
                """, "mdl:surface", "out")));
            await File.WriteAllTextAsync(reference, Scene(Preview("0.12, 0.34, 0.56", "0.21", "0.84")));
            await File.WriteAllTextAsync(control, Scene(Preview("0.2, 0.2, 0.2", "0.5", "0")));
            using D3D12SilkGraphicsDevice device = D3D12SilkGraphicsDevice.Create(useWarp: false);
            Console.WriteLine(
                $"WAREHOUSE_MDL_RENDER_DEVICE name={device.Capabilities.DeviceName} " +
                $"software={device.Capabilities.IsSoftware}");
            byte[] expected = Capture(device, plugins, reference);
            byte[] defaults = Capture(device, plugins, control);
            byte[] actual = Capture(device, plugins, projected);
            await Assert.That(device.Capabilities.IsSoftware).IsFalse();
            await Assert.That(actual.Length).IsEqualTo(32 * 32 * 8);
            await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue()
                .Because("SDK-proven body constants must survive Hydra translation " +
                    "and match the literal PreviewSurface control");
            await Assert.That(actual.AsSpan().SequenceEqual(defaults)).IsFalse()
                .Because("silently replacing variant constants with root defaults must change this fixture");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] Capture(ISilkGraphicsDevice device, string plugins, string path)
    {
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, path);
        using var capturer = new SilkFrameCapturer(device);
        var camera = new CameraState(Matrix4x4.Identity, new Matrix4x4(
            0.5f, 0, 0, 0, 0, 0.5f, 0, 0,
            0, 0, -0.2f, 0, 0, 0, -1.2f, 1));
        SilkFrameCaptureResult result = capturer.CaptureWithHdrColor(
            session, 32, 32, RenderSettings.PresentationDefault, camera: camera);
        return (result.HdrColor ??
            throw new InvalidOperationException("The retained raw color plane is absent.")).Rgba16Float.ToArray();
    }

    private static string Preview(string color, string roughness, string metallic) => Material(
        $$"""
        uniform token info:id = "UsdPreviewSurface"
        color3f inputs:diffuseColor = ({{color}})
        float inputs:roughness = {{roughness}}
        float inputs:metallic = {{metallic}}
        token outputs:surface
        """, "surface", "surface");

    private static string Material(string shader, string terminal, string output) =>
        $$"""
            def Material "Material" {
                token outputs:{{terminal}}.connect = </World/Material/Shader.outputs:{{output}}>
                def Shader "Shader" {
                    {{shader}}
                }
            }
        """;

    private static string Scene(string material) =>
        $$"""
        #usda 1.0
        (
            defaultPrim = "World"
            metersPerUnit = 1
            upAxis = "Y"
        )
        def Xform "World" {
            def Mesh "Quad" (prepend apiSchemas = ["MaterialBindingAPI"]) {
                point3f[] points = [(-1, -1, -4), (1, -1, -4), (1, 1, -4), (-1, 1, -4)]
                int[] faceVertexCounts = [4]
                int[] faceVertexIndices = [0, 1, 2, 3]
                uniform token subdivisionScheme = "none"
                normal3f[] normals = [(0, 0, 1)] (interpolation = "constant")
                rel material:binding = </World/Material>
            }
        {{material}}
        }
        """;
}
