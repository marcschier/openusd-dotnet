// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal sealed class SilkSingleMeshHdrFixture : IDisposable
{
    internal SilkSingleMeshHdrFixture()
    {
        string work = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT")
            ?? Path.Combine(AppContext.BaseDirectory, "single-hdr-work");
        Root = Path.Combine(work, "single-hdr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        ScenePath = Path.Combine(Root, "cube.usda");
        File.WriteAllText(ScenePath, Scene);
    }

    internal string Root { get; }

    internal string ScenePath { get; }

    internal static RenderSettings GpuSettings(string? config = null) => RenderSettings.Default with
    {
        DisplayTransform = new RenderDisplayTransform(
            config ?? SilkHdrColorCaptureFixture.OcioConfigPath, "linear", "TestDisplay", "TestView")
    };

    internal OpenUsdSilkSession CreateSession()
    {
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrWhiteSpace(plugins))
        {
            Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH to the matched native runtime plugin directory.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        return OpenUsdSilkRuntime.Create(plugins, ScenePath);
    }

    internal static void Retain(
        ISilkGraphicsDevice device, SilkMeshRenderer renderer, OpenUsdSilkSession session, double time = 0)
    {
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.HdrColorTarget(16, 16));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.SampledDepthTarget(16, 16));
        using OpenUsdSilkPage page = session.Sync(16, 16, time, SilkHdrColorCaptureFixture.Camera);
        _ = renderer.ApplyAndRender(page, color, depth);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);

    private const string Scene = """
        #usda 1.0
        (
            defaultPrim = "World"
            upAxis = "Y"
            metersPerUnit = 1
        )
        def Xform "World"
        {
            def Cube "Cube" (
                prepend apiSchemas = ["MaterialBindingAPI"]
            )
            {
                double size = 1
                rel material:binding = </World/Material>
                double3 xformOp:translate.timeSamples = { 0: (0, 0, 0), 1: (0, 0, -1) }
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Material "Material"
            {
                token outputs:surface.connect = </World/Material/Surface.outputs:surface>
                def Shader "Surface"
                {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0, 0, 0)
                    int inputs:useSpecularWorkflow = 1
                    color3f inputs:specularColor = (0, 0, 0)
                    float inputs:clearcoat = 0
                    color3f inputs:emissiveColor.timeSamples = { 0: (64, 0, 0), 1: (0, 64, 0) }
                    float inputs:opacity = 1
                    token outputs:surface
                }
            }
        }
        """;
}
