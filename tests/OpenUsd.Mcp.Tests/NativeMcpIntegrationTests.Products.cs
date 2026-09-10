// Copyright (c) marcschier. Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace OpenUsd.Mcp.Tests;

public sealed partial class NativeMcpIntegrationTests
{
    [Test]
    [NotInParallel]
    [Arguments("shrink")]
    [Arguments("grow")]
    [Arguments("animate")]
    [Arguments("axis")]
    public async Task HiddenProductGeometryIsCurrentWhenLegacyFilteringIncludesItAgain(string change)
    {
        bool animated = change == "animate";
        if (Environment.GetEnvironmentVariable("OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED=1 with a matching ingestion-capable runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        NativeLayout layout = RequireNativeLayout(requireImaging: true);
        using var files = new WorkspaceTestFiles();
        string scene = change switch
        {
            "animate" => ProductScene.Replace("double3 xformOp:translate = (-1, -1, -4)",
                "double3 xformOp:translate.timeSamples = { 0: (-1, -1, -4), 2: (1, -1, -4) }",
                StringComparison.Ordinal),
            "axis" => ProductScene.Replace("def Cube \"InheritedProxy\" {",
                "def Cylinder \"InheritedProxy\" {\n double radius = 0.75\n double height = 1.5\n uniform token axis = \"Z\"",
                StringComparison.Ordinal)
                .Replace("def Cube \"Guide\" {",
                    "def Cylinder \"ReferenceCylinder\" {\n double radius = 0.75\n double height = 1.5\n uniform token axis = \"X\"",
                    StringComparison.Ordinal)
                .Replace("uniform token purpose = \"guide\"", "uniform token purpose = \"default\"",
                    StringComparison.Ordinal),
            _ => ProductScene
        };
        await File.WriteAllTextAsync(files.SourcePath, scene);
        var options = new OpenUsdMcpApplicationOptions(files.SourceRoot, files.OutputRoot,
            Path.Combine(layout.ShimRoot, "plugin", "usd"), files.OutputRoot,
            Path.Combine(files.OutputRoot, "viewer-not-launched.exe"));
        await using ServiceProvider provider = new ServiceCollection().AddOpenUsdMcpServices(options)
            .BuildServiceProvider();
        IOpenUsdMcpService service = provider.GetRequiredService<IOpenUsdMcpService>();
        IArtifactResourceStore artifacts = provider.GetRequiredService<IArtifactResourceStore>();
        McpSessionDto session = await service.OpenSceneAsync(new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        long generation = session.Generation;
        ulong revision = session.StageRevision;
        byte[] initial = await Legacy(0);
        await AssertHalf(initial, 4, 12, "000000000054003C");
        if (change == "axis")
        {
            await AssertHalf(initial, 1, 9, "000000000000003C");
            await AssertHalf(initial, 9, 9, "000000000054003C");
        }
        _ = await Product(0);
        if (!animated)
        {
            WorkspaceEditDto mutation = change == "axis"
                ? new WorkspaceEditDto
                {
                    Kind = "set_token",
                    PrimPath = "/World/ProxyParent/InheritedProxy",
                    AttributeName = "axis",
                    StringValue = "X"
                }
                : new WorkspaceEditDto
                {
                    Kind = "set_double",
                    PrimPath = "/World/ProxyParent/InheritedProxy",
                    AttributeName = "size",
                    Value = change == "grow" ? 2 : 0.1
                };
            McpEditResultDto edit = await service.ApplyEditsAsync(new ApplyEditsRequest
            {
                SessionId = session.SessionId,
                Generation = generation,
                StageRevision = revision,
                Edits = [mutation]
            }, default);
            generation = edit.Generation;
            revision = edit.StageRevision;
        }
        _ = await Product(2);
        byte[] restored = await Legacy(2);
        await AssertHalf(restored, 4, 12, change is "grow" or "axis" ? "000000000054003C" : "000000000000003C");
        if (change is "grow" or "axis")
        {
            await AssertHalf(restored, 1, 9, "000000000054003C");
        }
        if (animated)
        {
            await AssertHalf(restored, 12, 12, "000000000054003C");
        }
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo(scene);
        return;

        async ValueTask<McpRenderSequenceResultDto> Product(double time) =>
            await service.RenderProductAsync(new RenderProductCaptureRequest
            {
                SessionId = session.SessionId,
                Generation = generation,
                StageRevision = revision,
                SettingsPath = "/SettingsFull",
                ProductPath = "/Product",
                StartTimeCode = time
            }, default);

        async Task<byte[]> Legacy(double time)
        {
            McpRenderSequenceResultDto job = await service.RenderSequenceAsync(new RenderSequenceRequest
            {
                SessionId = session.SessionId,
                Generation = generation,
                StageRevision = revision,
                CameraPath = "/Camera",
                Width = 16,
                Height = 16,
                FrameCount = 1,
                StartTimeCode = time,
                IncludeHdrColor = true
            }, default);
            McpSequenceFrameResultDto frame = await service.ReadSequenceFrameAsync(
                new ReadSequenceFrameRequest { JobId = job.JobId, FrameIndex = 0 }, default);
            ArtifactResourceContent? half = await artifacts.ReadAsync(new Uri(frame.HdrColor!.Uri));
            return half!.Content.ToArray();
        }
    }

    [Test]
    [NotInParallel]
    public async Task AuthoredProductTimeAndCropAreExecutedRatherThanResizingTheViewport()
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED=1 with a matching ingestion-capable runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        NativeLayout layout = RequireNativeLayout(requireImaging: true);
        using var files = new WorkspaceTestFiles();
        string scene = ProductScene
            .Replace("int2 resolution = (16, 16)", "int2 resolution = (32, 16)", StringComparison.Ordinal)
            .Replace("token productName = \"../must-not-be-used.exr\"",
                "token productName = \"../must-not-be-used.exr\"\n    float4 dataWindowNDC = (0.25, 0, 0.75, 1)",
                StringComparison.Ordinal)
            .Replace("double3 xformOp:translate = (-1, 1, -4)",
                "double3 xformOp:translate.timeSamples = { 0: (-1, 1, -4), 2: (1, 1, -4) }",
                StringComparison.Ordinal);
        await File.WriteAllTextAsync(files.SourcePath, scene);
        var options = new OpenUsdMcpApplicationOptions(files.SourceRoot, files.OutputRoot,
            Path.Combine(layout.ShimRoot, "plugin", "usd"), files.OutputRoot,
            Path.Combine(files.OutputRoot, "viewer-not-launched.exe"));
        await using ServiceProvider provider = new ServiceCollection().AddOpenUsdMcpServices(options)
            .BuildServiceProvider();
        IOpenUsdMcpService service = provider.GetRequiredService<IOpenUsdMcpService>();
        IArtifactResourceStore artifacts = provider.GetRequiredService<IArtifactResourceStore>();
        McpSessionDto session = await service.OpenSceneAsync(new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        var request = new RenderProductCaptureRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            SettingsPath = "/SettingsFull",
            ProductPath = "/Product",
            FrameCount = 2,
            TimeStep = 2
        };
        McpRenderSequenceResultDto job = await service.RenderProductAsync(request, default);
        for (int index = 0; index < 2; index++)
        {
            McpSequenceFrameResultDto frame = await service.ReadSequenceFrameAsync(
                new ReadSequenceFrameRequest { JobId = job.JobId, FrameIndex = index }, default);
            await Assert.That(frame.Width).IsEqualTo(16);
            await Assert.That(frame.Height).IsEqualTo(16);
            await Assert.That(frame.TimeCode).IsEqualTo(index * 2d);
            ArtifactResourceContent? half = await artifacts.ReadAsync(new Uri(frame.HdrColor!.Uri));
            await AssertHalf(half!.Content.ToArray(), 4, 4,
                index == 0 ? "005400000000003C" : "000000000000003C");
            await AssertHalf(half.Content.ToArray(), 12, 4, "005400000000003C");
        }
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(files.OutputRoot, job.ManifestPath)));
        JsonElement raster = manifest.RootElement.GetProperty("frames")[0].GetProperty("productRaster");
        await Assert.That(raster.GetProperty("fullWidth").GetInt32()).IsEqualTo(32);
        await Assert.That(raster.GetProperty("fullHeight").GetInt32()).IsEqualTo(16);
        await Assert.That(raster.GetProperty("dataWindowMinX").GetInt32()).IsEqualTo(8);
        await Assert.That(raster.GetProperty("dataWindowMinY").GetInt32()).IsEqualTo(0);
        await Assert.That(raster.GetProperty("pixelAspectRatio").GetDouble()).IsEqualTo(1d);
        McpSequenceSheetResultDto sheet = await service.ReadSequenceSheetAsync(new ReadSequenceSheetRequest
        {
            JobId = job.JobId,
            FrameIndices = [1, 0],
            Width = 32,
            Height = 16
        }, default);
        await Assert.That(sheet.Tiles.Select(static tile => tile.TimeCode).SequenceEqual([2d, 0d])).IsTrue();
        await Assert.That(sheet.StageRevision).IsEqualTo(session.StageRevision);
        ArtifactResourceContent sheetResource = await artifacts.ReadAsync(new Uri(sheet.Artifact.Uri)) ??
            throw new InvalidOperationException("The completed product sheet is absent.");
        ImageRgba8 sheetImage = PngRgba8Decoder.Decode(sheetResource.Content.Span);
        await Assert.That(Convert.ToHexString(sheetImage.Pixels.Span.Slice((4 * 32 + 4) * 4, 4)))
            .IsEqualTo("000000FF");
        await Assert.That(Convert.ToHexString(sheetImage.Pixels.Span.Slice((4 * 32 + 20) * 4, 4)))
            .IsEqualTo("800000FF");
        string renders = Path.GetDirectoryName(Path.Combine(files.OutputRoot, job.OutputDirectory))!;
        await Assert.That(async () => await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            SettingsPath = request.SettingsPath,
            ProductPath = request.ProductPath,
            HdrColorFormat = "exr"
        }, default)).Throws<OpenUsdMcpFailureException>();
        await Assert.That(Directory.GetDirectories(renders)).Count().IsEqualTo(1);
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo(scene);
    }

    [Test]
    [NotInParallel]
    public async Task AuthoredProductsActuallyChangePixelsAndLegacyCapturesRestoreTheirOwnFilters()
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_RENDER_PRODUCT_EXECUTION_REQUIRED=1 with a matching ingestion-capable runtime.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        NativeLayout layout = RequireNativeLayout(requireImaging: true);
        using var files = new WorkspaceTestFiles();
        await File.WriteAllTextAsync(files.SourcePath, ProductScene);
        var options = new OpenUsdMcpApplicationOptions(files.SourceRoot, files.OutputRoot,
            Path.Combine(layout.ShimRoot, "plugin", "usd"), files.OutputRoot,
            Path.Combine(files.OutputRoot, "viewer-not-launched.exe"));
        await using ServiceProvider provider = new ServiceCollection().AddOpenUsdMcpServices(options)
            .BuildServiceProvider();
        IOpenUsdMcpService service = provider.GetRequiredService<IOpenUsdMcpService>();
        IArtifactResourceStore artifacts = provider.GetRequiredService<IArtifactResourceStore>();
        McpSessionDto session = await service.OpenSceneAsync(new OpenSceneRequest { SourcePath = "scene.usda" }, default);

        McpRenderSequenceResultDto full = await Product("/SettingsFull");
        byte[] fullPixels = await ReadHalf(full);
        await AssertHalf(fullPixels, 4, 4, "005400000000003C");
        await AssertHalf(fullPixels, 12, 4, "005400000000003C");
        await AssertHalf(fullPixels, 4, 12, "000000000000003C");
        await AssertHalf(fullPixels, 12, 12, "000000000000003C");
        McpRenderSequenceResultDto preview = await Product("/SettingsPreview");
        byte[] previewPixels = await ReadHalf(preview);
        await AssertHalf(previewPixels, 4, 4, "000000540000003C");
        await AssertHalf(previewPixels, 12, 4, "000000000000003C");
        await AssertHalf(previewPixels, 4, 12, "000000540000003C");
        await AssertHalf(previewPixels, 12, 12, "000000000000003C");

        McpRenderSequenceResultDto legacy = await service.RenderSequenceAsync(new RenderSequenceRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            CameraPath = "/Camera",
            Width = 16,
            Height = 16,
            FrameCount = 1,
            IncludeHdrColor = true
        }, default);
        byte[] legacyPixels = await ReadHalf(legacy);
        await AssertHalf(legacyPixels, 4, 4, "000000000054003C");
        await AssertHalf(legacyPixels, 12, 4, "000000000054003C");
        await AssertHalf(legacyPixels, 4, 12, "000000000054003C");
        await AssertHalf(legacyPixels, 12, 12, "000000000000003C");
        McpRenderSequenceResultDto restored = await Product("/SettingsFull");
        await Assert.That((await ReadHalf(restored)).SequenceEqual(fullPixels)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo(ProductScene);
        McpSessionDto unchanged = await service.GetSceneAsync(new SceneRevisionRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision
        }, default);
        await Assert.That(unchanged.StageRevision).IsEqualTo(session.StageRevision);
        return;

        async ValueTask<McpRenderSequenceResultDto> Product(string settings) =>
            await service.RenderProductAsync(new RenderProductCaptureRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
                SettingsPath = settings,
                ProductPath = "/Product"
            }, default);

        async Task<byte[]> ReadHalf(McpRenderSequenceResultDto job)
        {
            McpSequenceFrameResultDto frame = await service.ReadSequenceFrameAsync(
                new ReadSequenceFrameRequest { JobId = job.JobId, FrameIndex = 0 }, default);
            ArtifactResourceContent? half = await artifacts.ReadAsync(new Uri(frame.HdrColor!.Uri));
            await Assert.That(half).IsNotNull();
            await Assert.That(half!.Content.Length).IsEqualTo(16 * 16 * 8);
            return half.Content.ToArray();
        }
    }

    private static async Task AssertHalf(byte[] pixels, int x, int y, string expected) =>
        await Assert.That(Convert.ToHexString(pixels.AsSpan((y * 16 + x) * 8, 8))).IsEqualTo(expected);

    private const string ProductScene = """
        #usda 1.0
        (
            renderSettingsPrimPath = "/SettingsFull"
            metersPerUnit = 1
            upAxis = "Y"
        )
        def Camera "Camera" {
            token projection = "orthographic"
            float horizontalAperture = 40
            float verticalAperture = 40
            float2 clippingRange = (1, 11)
        }
        def RenderSettings "SettingsFull" {
            rel camera = </Camera>
            rel products = </Product>
            int2 resolution = (16, 16)
            uniform token[] includedPurposes = ["default", "render"]
            uniform token[] materialBindingPurposes = ["full"]
        }
        def RenderSettings "SettingsPreview" {
            rel camera = </Camera>
            rel products = </Product>
            int2 resolution = (16, 16)
            uniform token[] includedPurposes = ["default", "proxy"]
            uniform token[] materialBindingPurposes = ["preview"]
        }
        def RenderProduct "Product" {
            token productName = "../must-not-be-used.exr"
            rel orderedVars = </Color>
        }
        def RenderVar "Color" {
            token dataType = "half4"
            string sourceName = "color"
            token sourceType = "raw"
        }
        def Scope "Looks" {
            def Material "Full" {
                token outputs:surface.connect = </Looks/Full/Shader.outputs:surface>
                def Shader "Shader" {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0, 0, 0)
                    color3f inputs:emissiveColor = (64, 0, 0)
                    int inputs:useSpecularWorkflow = 1
                    color3f inputs:specularColor = (0, 0, 0)
                    token outputs:surface
                }
            }
            def Material "Preview" {
                token outputs:surface.connect = </Looks/Preview/Shader.outputs:surface>
                def Shader "Shader" {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0, 0, 0)
                    color3f inputs:emissiveColor = (0, 64, 0)
                    int inputs:useSpecularWorkflow = 1
                    color3f inputs:specularColor = (0, 0, 0)
                    token outputs:surface
                }
            }
            def Material "Legacy" {
                token outputs:surface.connect = </Looks/Legacy/Shader.outputs:surface>
                def Shader "Shader" {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0, 0, 0)
                    color3f inputs:emissiveColor = (0, 0, 64)
                    int inputs:useSpecularWorkflow = 1
                    color3f inputs:specularColor = (0, 0, 0)
                    token outputs:surface
                }
            }
        }
        def Xform "World" (prepend apiSchemas = ["MaterialBindingAPI"]) {
            rel material:binding = </Looks/Legacy>
            rel material:binding:full = </Looks/Full>
            rel material:binding:preview = </Looks/Preview>
            def Cube "Default" {
                double size = 0.75
                double3 xformOp:translate = (-1, 1, -4)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Cube "Render" {
                uniform token purpose = "render"
                double size = 0.75
                double3 xformOp:translate = (1, 1, -4)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            def Xform "ProxyParent" {
                uniform token purpose = "proxy"
                def Cube "InheritedProxy" {
                    double size = 0.75
                    double3 xformOp:translate = (-1, -1, -4)
                    uniform token[] xformOpOrder = ["xformOp:translate"]
                }
            }
            def Cube "Guide" {
                uniform token purpose = "guide"
                double size = 0.75
                double3 xformOp:translate = (1, -1, -4)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
        }
        """;
}
