// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal sealed class SilkHdrColorCaptureFixture : IDisposable
{
    internal SilkHdrColorCaptureFixture(string? emissionA = null, bool transparent = false)
    {
        string workRoot = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT")
            ?? Path.Combine(AppContext.BaseDirectory, "hdr-work");
        Root = Path.Combine(workRoot, "silk-hdr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        ScenePath = Path.Combine(Root, "emissive.usda");
        string scene = emissionA is null
            ? Scene
            : Scene.Replace("(0.125, 0.5, 2)", emissionA, StringComparison.Ordinal);
        if (transparent)
        {
            scene = scene.Replace("float inputs:opacity = 1", "float inputs:opacity = 0.5", StringComparison.Ordinal);
        }
        File.WriteAllText(ScenePath, scene);
    }

    internal string Root { get; }

    internal string ScenePath { get; }

    internal static CameraState Camera { get; } = new(
        Matrix4x4.CreateTranslation(0, 0, -2),
        new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -0.2f, 0, 0, 0, -1.2f, 1));

    internal static string OcioConfigPath =>
        Path.Combine(AppContext.BaseDirectory, "depth-capture", "ocio-test-config.ocio");

    internal static SilkOpenColorIoProcessor CreateProcessor(string view) =>
        new SilkOpenColorIoDisplayTransform(OcioConfigPath, "linear", "TestDisplay", view).CreateProcessor();

    internal static RenderSettings Settings(
        float exposure = 0,
        RenderOutputTransform outputTransform = RenderOutputTransform.Identity,
        Vector4? clearColor = null) =>
        new(1, true, true, clearColor ?? new Vector4(0, 0, 0, 1), true, true,
            RenderComplexity.Low, outputTransform, exposure);

    internal static ulong RetainScene(
        ISilkGraphicsDevice device, SilkMeshRenderer renderer, OpenUsdSilkSession session, double timeCode = 0)
    {
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.HdrColorTarget(40, 32));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.SampledDepthTarget(40, 32));
        using OpenUsdSilkPage page = session.Sync(40, 32, timeCode, Camera);
        _ = renderer.ApplyAndRender(page, color, depth);
        return page.Revision;
    }

    internal OpenUsdSilkSession CreateSession()
    {
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(plugins))
        {
            Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH to the complete matched native runtime plugin directory.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        return OpenUsdSilkRuntime.Create(plugins, ScenePath);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);

    private const string Scene = """
        #usda 1.0
        (
            defaultPrim = "World"
            upAxis = "Y"
            metersPerUnit = 1
            renderSettingsPrimPath = "/Settings"
        )
        def Xform "World"
        {
            def Camera "Camera"
            {
                token projection = "orthographic"
                float horizontalAperture = 20
                float verticalAperture = 20
                float focalLength = 10
                float2 clippingRange = (1, 11)
                double3 xformOp:translate = (0, 0, 2)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Mesh "A" (
                prepend apiSchemas = ["MaterialBindingAPI"]
            )
            {
                uniform token subdivisionScheme = "none"
                uniform bool doubleSided = 1
                point3f[] points = [(-0.9, 0.1, -1), (-0.1, 0.1, -1), (-0.1, 0.9, -1), (-0.9, 0.9, -1)]
                int[] faceVertexCounts = [4]
                int[] faceVertexIndices = [0, 1, 2, 3]
                rel material:binding = </World/MaterialA>
                double3 xformOp:translate.timeSamples = { 0: (0, 0, 0), 1: (0, 0, -1) }
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Mesh "B" (
                prepend apiSchemas = ["MaterialBindingAPI"]
            )
            {
                uniform token subdivisionScheme = "none"
                uniform bool doubleSided = 1
                point3f[] points = [(0.1, -0.9, -5), (0.9, -0.9, -5), (0.9, -0.1, -5), (0.1, -0.1, -5)]
                int[] faceVertexCounts = [4]
                int[] faceVertexIndices = [0, 1, 2, 3]
                rel material:binding = </World/MaterialB>
            }
            def Material "MaterialA"
            {
                token outputs:surface.connect = </World/MaterialA/Surface.outputs:surface>
                def Shader "Surface"
                {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0, 0, 0)
                    int inputs:useSpecularWorkflow = 1
                    color3f inputs:specularColor = (0, 0, 0)
                    float inputs:clearcoat = 0
                    color3f inputs:emissiveColor.timeSamples = { 0: (0.125, 0.5, 2), 1: (0.5, 2, 0.125) }
                    float inputs:opacity = 1
                    token outputs:surface
                }
            }
            def Material "MaterialB"
            {
                token outputs:surface.connect = </World/MaterialB/Surface.outputs:surface>
                def Shader "Surface"
                {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0, 0, 0)
                    int inputs:useSpecularWorkflow = 1
                    color3f inputs:specularColor = (0, 0, 0)
                    float inputs:clearcoat = 0
                    color3f inputs:emissiveColor = (4, 0.25, 0.0625)
                    float inputs:opacity = 1
                    token outputs:surface
                }
            }
        }
        def RenderSettings "Settings"
        {
            rel camera = </World/Camera>
            rel products = </Product>
            int2 resolution = (96, 96)
            float4 dataWindowNDC = (0.125, 0.25, 0.75, 0.625)
        }
        def RenderProduct "Product"
        {
            token productName = "not-written.exr"
            rel orderedVars = </Color>
        }
        def RenderVar "Color"
        {
            token dataType = "color3f"
            string sourceName = "color"
            token sourceType = "raw"
        }
        """;
}
