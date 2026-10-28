// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenUsd.Geom;
using OpenUsd.Render;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Mcp.Tests;

public sealed class RenderProductServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    [Arguments(false, "before", 1)]
    [Arguments(true, "before", 1)]
    [Arguments(false, "overflow", 1)]
    [Arguments(true, "overflow", 1)]
    [Arguments(false, "after", 1)]
    [Arguments(true, "after", 1)]
    [Arguments(false, "batched", 1)]
    [Arguments(true, "batched", 1)]
    [Arguments(false, "overflow", -1)]
    [Arguments(true, "overflow", -1)]
    [Arguments(false, "batched", -1)]
    [Arguments(true, "batched", -1)]
    [Arguments(false, "ordinary", 1)]
    [Arguments(true, "ordinary", 1)]
    [Arguments(false, "subnormal", 1)]
    [Arguments(true, "subnormal", 1)]
    public async Task FiniteSequenceSamplesSurviveIntermediateOverflowAndKeepOrdinaryRounding(
        bool product, string kind, int direction)
    {
        int count = kind switch
        {
            "before" => 4,
            "overflow" => 5,
            "after" => 6,
            "batched" => 40,
            "ordinary" => 12,
            "subnormal" => 9,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        double start = kind switch
        {
            "ordinary" => -1,
            "subnormal" => 4 * double.Epsilon,
            _ => direction * Math.ScaleB(1, 1023)
        };
        double step = kind switch
        {
            "ordinary" => 0.1,
            "subnormal" => -double.Epsilon,
            "batched" => -direction * Math.ScaleB(1, 1019),
            _ => -direction * Math.ScaleB(1, 1022)
        };
        double[] expected = Enumerable.Range(0, count).Select(index => kind switch
        {
            "ordinary" or "subnormal" => start + index * step,
            "batched" => Math.ScaleB(direction * (16 - index), 1019),
            _ => Math.ScaleB(direction * (2 - index), 1022)
        }).ToArray();
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        var prepared = new List<double>();
        backend.PrepareProduct = times =>
        {
            prepared.AddRange(times);
            return Plan(backend.Context!.SessionId, backend.StageRevision, times);
        };
        backend.PreparePreviewCameras = times =>
        {
            prepared.AddRange(times);
            return Enumerable.Repeat(CameraState.Default, times.Count).ToArray();
        };
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            FileStorageRoot: Path.Combine(files.OutputRoot, "artifacts")));
        var factory = new ProductFactory();
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        McpRenderSequenceResultDto job = product
            ? await service.RenderProductAsync(new RenderProductCaptureRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
                FrameCount = count,
                StartTimeCode = start,
                TimeStep = step
            }, default)
            : await service.RenderSequenceAsync(new RenderSequenceRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
                FrameCount = count,
                StartTimeCode = start,
                TimeStep = step,
                Width = 2,
                Height = 2
            }, default);
        await Assert.That(job.FrameCount).IsEqualTo(count);
        await Assert.That(prepared.SequenceEqual(expected)).IsTrue();
        await Assert.That((product ? factory.Source.Times : factory.Source.ViewportTimes).SequenceEqual(expected))
            .IsTrue();
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(backend.Context!.OutputDirectory, "renders", job.JobId, "manifest.json")));
        JsonElement frames = manifest.RootElement.GetProperty("frames");
        await Assert.That(frames.GetArrayLength()).IsEqualTo(count);
        for (int index = 0; index < count; index++)
        {
            McpSequenceFrameResultDto frame = await service.ReadSequenceFrameAsync(
                new ReadSequenceFrameRequest { JobId = job.JobId, FrameIndex = index }, default);
            await Assert.That(BitConverter.DoubleToInt64Bits(frame.TimeCode))
                .IsEqualTo(BitConverter.DoubleToInt64Bits(expected[index]));
            await Assert.That(BitConverter.DoubleToInt64Bits(frames[index].GetProperty("timeCode").GetDouble()))
                .IsEqualTo(BitConverter.DoubleToInt64Bits(expected[index]));
        }
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs.Count).IsEqualTo(1);
        await Assert.That(backend.Events).DoesNotContain("apply");
        await Assert.That(backend.Events).DoesNotContain("checkpoint");
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo("#usda 1.0");
    }

    [Test]
    [Arguments(false, "negativeEndpoint")]
    [Arguments(true, "negativeEndpoint")]
    [Arguments(false, "positiveEndpoint")]
    [Arguments(true, "positiveEndpoint")]
    [Arguments(false, "startNaN")]
    [Arguments(true, "startNaN")]
    [Arguments(false, "startInfinity")]
    [Arguments(true, "startInfinity")]
    [Arguments(false, "stepNaN")]
    [Arguments(true, "stepNaN")]
    [Arguments(false, "stepInfinity")]
    [Arguments(true, "stepInfinity")]
    [Arguments(false, "zeroStep")]
    [Arguments(true, "zeroStep")]
    public async Task NonfiniteSequenceSamplesStillRefuseBeforeWorkspaceOutputOrHistory(bool product, string kind)
    {
        double start = kind switch
        {
            "startNaN" => double.NaN,
            "startInfinity" => double.PositiveInfinity,
            _ => Math.ScaleB(1, 1023)
        };
        double step = kind switch
        {
            "stepNaN" => double.NaN,
            "stepInfinity" => double.NegativeInfinity,
            "zeroStep" => 0,
            "negativeEndpoint" => -Math.ScaleB(1, 1022),
            _ => Math.ScaleB(1, 1022)
        };
        int count = kind == "negativeEndpoint" ? 7 : 3;
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        if (product)
        {
            await Assert.That(async () => await service.RenderProductAsync(new RenderProductCaptureRequest
            {
                SessionId = "unused",
                FrameCount = count,
                StartTimeCode = start,
                TimeStep = step
            }, default)).Throws<ArgumentOutOfRangeException>();
        }
        else
        {
            await Assert.That(async () => await service.RenderSequenceAsync(new RenderSequenceRequest
            {
                SessionId = "unused",
                FrameCount = count,
                StartTimeCode = start,
                TimeStep = step,
                Width = 2,
                Height = 2
            }, default)).Throws<ArgumentOutOfRangeException>();
        }
        await Assert.That(backend.Events).IsEmpty();
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).IsEmpty();
        await Assert.That(Directory.EnumerateFileSystemEntries(files.OutputRoot)).IsEmpty();
    }

    [Test]
    [Arguments(true, true)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(false, false)]
    public async Task HistoricalProductCameraKeepsPerFrameSamplesAndEffectFlagsAfterSceneClose(
        bool disableMotionBlur, bool disableDepthOfField)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        backend.PrepareProduct = times => Plan(backend.Context!.SessionId, backend.StageRevision, times,
            cameraAtTime: time => new UsdGeomCameraState(UsdGeomCameraProjection.Orthographic,
                -1, 1, -1, 1, 1, 11, 0, 20, 20, 0, 0, time == 2 ? 10 : 20,
                disableDepthOfField ? time == 2 ? 3 : 6 : 0),
            cameraSettingsAtTime: time => time == 2
                ? new RenderCameraFrameSettings(disableMotionBlur ? -0.25 : 0, disableMotionBlur ? 0.25 : 0,
                    -1, 200, 4, 2, 1)
                : new RenderCameraFrameSettings(disableMotionBlur ? -0.5 : 0, disableMotionBlur ? 0.75 : 0,
                    -2, 400, 8, 4, 2),
            disableMotionBlur: disableMotionBlur, disableDepthOfField: disableDepthOfField);
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            FileStorageRoot: Path.Combine(files.OutputRoot, "artifacts")));
        var factory = new ProductFactory();
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        McpRenderSequenceResultDto product = await service.RenderProductAsync(Request(session, 2), default);
        string manifestPath = Path.Combine(backend.Context!.OutputDirectory, "renders", product.JobId, "manifest.json");
        await service.CloseSceneAsync(new SceneRevisionRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision
        }, default);
        backend.PrepareProduct = _ =>
            throw new InvalidOperationException("Historical reads must not prepare a product.");
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        await File.WriteAllTextAsync(manifestPath, "{}");
        McpSequenceFrameResultDto first = await service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = product.JobId, FrameIndex = 0 }, default);
        McpSequenceFrameResultDto second = await service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = product.JobId, FrameIndex = 1 }, default);
        await Assert.That(first.ProductCamera)
            .IsEqualTo(new McpProductCameraDto(disableMotionBlur ? -0.25 : 0, disableMotionBlur ? 0.25 : 0,
                -1, 200, 4, 2, 1, 1, 10, disableDepthOfField ? 3 : 0, disableMotionBlur, disableDepthOfField, 0));
        await Assert.That(second.ProductCamera)
            .IsEqualTo(new McpProductCameraDto(disableMotionBlur ? -0.5 : 0, disableMotionBlur ? 0.75 : 0,
                -2, 400, 8, 4, 2, 1, 20, disableDepthOfField ? 6 : 0, disableMotionBlur, disableDepthOfField, 0));
        McpSequenceFrameResultDto[] frames = [first, second];
        foreach (McpSequenceFrameResultDto frame in frames)
        {
            JsonElement metadata = JsonSerializer.SerializeToElement(frame.ProductCamera, JsonOptions);
            JsonElement saved = manifest.RootElement.GetProperty("frames")[frame.FrameIndex]
                .GetProperty("productCamera");
            foreach (JsonProperty property in saved.EnumerateObject())
            {
                await Assert.That(JsonElement.DeepEquals(metadata.GetProperty(property.Name), property.Value)).IsTrue();
            }
            JsonElement authored = manifest.RootElement.GetProperty("authoredProduct");
            await Assert.That(metadata.GetProperty("disableMotionBlur").GetBoolean())
                .IsEqualTo(authored.GetProperty("disableMotionBlur").GetBoolean());
            await Assert.That(metadata.GetProperty("disableDepthOfField").GetBoolean())
                .IsEqualTo(authored.GetProperty("disableDepthOfField").GetBoolean());
        }
        McpSequenceSheetResultDto sheet = await service.ReadSequenceSheetAsync(new ReadSequenceSheetRequest
        {
            JobId = product.JobId,
            FrameIndices = [1, 0],
            Width = 8,
            Height = 4
        }, default);
        await Assert.That(sheet.Tiles[0].ProductCamera).IsEqualTo(second.ProductCamera);
        await Assert.That(sheet.Tiles[1].ProductCamera).IsEqualTo(first.ProductCamera);
        await Assert.That(first.HdrColor!.Sha256).IsEqualTo(second.HdrColor!.Sha256);
        await Assert.That(first.Artifact.Sha256).IsEqualTo(second.Artifact.Sha256);
        await Assert.That(factory.Source.Times.Count).IsEqualTo(2);
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo("#usda 1.0");
    }

    [Test]
    public async Task HistoricalProductRasterKeepsPerFrameConformanceAndRequestedSheetOrderAfterSceneClose()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        backend.PrepareProduct = times => Plan(backend.Context!.SessionId, backend.StageRevision, times,
            authoredPixelAspectRatio: 3, authoredConformPolicy: "adjustPixelAspectRatio",
            cameraAtTime: time => new UsdGeomCameraState(UsdGeomCameraProjection.Orthographic,
                time == 2 ? -1 : -2, time == 2 ? 1 : 2, -1, 1, 1, 11, 0, 20, 20, 0, 0, 0, 0));
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            FileStorageRoot: Path.Combine(files.OutputRoot, "artifacts")));
        var factory = new ProductFactory();
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        McpRenderSequenceResultDto product = await service.RenderProductAsync(Request(session, 2), default);
        McpRenderSequenceResultDto viewport = await service.RenderSequenceAsync(new RenderSequenceRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Width = 2,
            Height = 2,
            FrameCount = 1
        }, default);
        await service.CloseSceneAsync(new SceneRevisionRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision
        }, default);
        int preparations = backend.Events.Count(static value => value == "prepare-product");
        McpSequenceFrameResultDto first = await service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = product.JobId, FrameIndex = 0 }, default);
        McpSequenceFrameResultDto second = await service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = product.JobId, FrameIndex = 1 }, default);
        await Assert.That(first.ProductRaster)
            .IsEqualTo(new McpProductRasterDto(2, 2, 0, 0, 1, "adjustPixelAspectRatio"));
        await Assert.That(second.ProductRaster)
            .IsEqualTo(new McpProductRasterDto(2, 2, 0, 0, 2, "adjustPixelAspectRatio"));
        McpSequenceSheetResultDto sheet = await service.ReadSequenceSheetAsync(new ReadSequenceSheetRequest
        {
            JobId = product.JobId,
            FrameIndices = [1, 0],
            Width = 8,
            Height = 4
        }, default);
        await Assert.That(sheet.Tiles[0].ProductRaster).IsEqualTo(second.ProductRaster);
        await Assert.That(sheet.Tiles[1].ProductRaster).IsEqualTo(first.ProductRaster);
        await Assert.That(sheet.Tiles.Select(static tile => tile.FrameIndex).SequenceEqual([1, 0])).IsTrue();
        McpSequenceFrameResultDto plain = await service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = viewport.JobId, FrameIndex = 0 }, default);
        McpSequenceSheetResultDto plainSheet = await service.ReadSequenceSheetAsync(new ReadSequenceSheetRequest
        {
            JobId = viewport.JobId,
            Width = 4,
            Height = 4
        }, default);
        await Assert.That(plain.ProductRaster).IsNull();
        await Assert.That(plainSheet.Tiles[0].ProductRaster).IsNull();
        await Assert.That(plain.ProductCamera).IsNull();
        await Assert.That(plainSheet.Tiles[0].ProductCamera).IsNull();
        await Assert.That(factory.Source.Times.Count).IsEqualTo(2);
        await Assert.That(factory.Source.ViewportTimes.Count).IsEqualTo(1);
        await Assert.That(backend.Events.Count(static value => value == "prepare-product")).IsEqualTo(preparations);
    }

    [Test]
    [Arguments("omitted", 4, 4, 2, 2, 2f, 2f, 0f, 0f)]
    [Arguments("full", 8, 8, 0, 0, 1f, 1f, 0f, 0f)]
    [Arguments("center", 4, 4, 2, 2, 2f, 2f, 0f, 0f)]
    [Arguments("left", 4, 8, 0, 0, 2f, 1f, 1f, 0f)]
    [Arguments("overscan", 12, 12, -2, -2, 2f / 3f, 2f / 3f, 0f, 0f)]
    [Arguments("onePixel", 1, 1, 2, 2, 8f, 8f, 3f, 3f)]
    [Arguments("outside", 4, 4, 8, 8, 2f, 2f, -3f, -3f)]
    [Arguments("pixelCenters", 1, 1, 0, 0, 8f, 8f, 7f, 7f)]
    [Arguments("dimensionLimit", 4096, 8, 0, 0, 1f / 512f, 1f, -511f / 512f, 0f)]
    public async Task ProductDataWindowOverridesPreserveCropOverscanAndHistoricalFramesWithoutAliasingInput(
        string kind, int width, int height, int minX, int minY,
        float projectionX, float projectionY, float offsetX, float offsetY)
    {
        float[]? coordinates = kind switch
        {
            "omitted" => null,
            "full" => [0, 0, 1, 1],
            "center" => [0.25f, 0.25f, 0.75f, 0.75f],
            "left" => [0, 0, 0.5f, 1],
            "overscan" => [-0.25f, -0.25f, 1.25f, 1.25f],
            "onePixel" => [0.25f, 0.25f, 0.375f, 0.375f],
            "outside" => [1, 1, 1.5f, 1.5f],
            "pixelCenters" => [0.0625f, 0.0625f, 0.1875f, 0.1875f],
            "dimensionLimit" => [0, 0, 512, 1],
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        UsdVec4f? expected = coordinates is null ? null :
            new UsdVec4f(coordinates[0], coordinates[1], coordinates[2], coordinates[3]);
        var authored = new UsdVec4f(0.25f, 0.25f, 0.75f, 0.75f);
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        RenderProductJobPlan? prepared = null;
        backend.PrepareProduct = times =>
        {
            if (coordinates is not null)
            {
                Array.Fill(coordinates, float.NaN);
            }
            return prepared = Plan(
                backend.Context!.SessionId, backend.StageRevision, times, backend.LastProductOverrides, authored);
        };
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            FileStorageRoot: Path.Combine(files.OutputRoot, "artifacts")));
        var factory = new ProductFactory();
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        McpRenderSequenceResultDto job = await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Width = 8,
            Height = 8,
            DataWindowNdc = coordinates
        }, default);
        await Assert.That(prepared!.Request.Product.DataWindowNdc).IsEqualTo(authored);
        await Assert.That(prepared.Request.Overrides.DataWindowNdc).IsEqualTo(expected);
        RenderPreparedFrame sample = prepared.Frames[0];
        await Assert.That(sample.OutputDimensions.Width).IsEqualTo(width);
        await Assert.That(sample.OutputDimensions.Height).IsEqualTo(height);
        await Assert.That(sample.DataWindowMinX).IsEqualTo(minX);
        await Assert.That(sample.DataWindowMinY).IsEqualTo(minY);
        await Assert.That(sample.Camera.Projection.M11).IsEqualTo(projectionX);
        await Assert.That(sample.Camera.Projection.M22).IsEqualTo(projectionY);
        await Assert.That(sample.Camera.Projection.M41).IsEqualTo(offsetX);
        await Assert.That(sample.Camera.Projection.M42).IsEqualTo(offsetY);
        string manifestPath = Path.Combine(backend.Context!.OutputDirectory, "renders", job.JobId, "manifest.json");
        await service.CloseSceneAsync(new SceneRevisionRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision
        }, default);
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        JsonElement raster = manifest.RootElement.GetProperty("frames")[0].GetProperty("productRaster");
        await Assert.That(raster.GetProperty("fullWidth").GetInt32()).IsEqualTo(8);
        await Assert.That(raster.GetProperty("fullHeight").GetInt32()).IsEqualTo(8);
        await Assert.That(raster.GetProperty("dataWindowMinX").GetInt32()).IsEqualTo(minX);
        await Assert.That(raster.GetProperty("dataWindowMinY").GetInt32()).IsEqualTo(minY);
        McpSequenceFrameResultDto frame = await service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = job.JobId, FrameIndex = 0 }, default);
        await Assert.That(frame.Width).IsEqualTo(width);
        await Assert.That(frame.Height).IsEqualTo(height);
        await Assert.That(frame.RenderRequest!.Width).IsEqualTo(width);
        await Assert.That(frame.RenderRequest.Height).IsEqualTo(height);
        await Assert.That(frame.ProductRaster).IsNotNull();
        await Assert.That(frame.ProductRaster!.FullWidth).IsEqualTo(8);
        await Assert.That(frame.ProductRaster.FullHeight).IsEqualTo(8);
        await Assert.That(frame.ProductRaster.DataWindowMinX).IsEqualTo(minX);
        await Assert.That(frame.ProductRaster.DataWindowMinY).IsEqualTo(minY);
        await Assert.That(frame.ProductRaster.PixelAspectRatio).IsEqualTo(sample.PixelAspectRatio);
        await Assert.That(frame.ProductRaster.AspectRatioConformPolicy).IsEqualTo("expandAperture");
        await Assert.That(frame.ProductRaster.DataWindowOriginConvention).IsEqualTo("bottom-left");
        await Assert.That(JsonElement.DeepEquals(raster,
            JsonSerializer.SerializeToElement(frame.ProductRaster, JsonOptions))).IsTrue();
        McpSequenceSheetResultDto sheet = await service.ReadSequenceSheetAsync(new ReadSequenceSheetRequest
        {
            JobId = job.JobId,
            FrameIndices = [0],
            Width = 8,
            Height = 4
        }, default);
        await Assert.That(sheet.Tiles[0].ProductRaster).IsEqualTo(frame.ProductRaster);
        await Assert.That(sheet.Tiles[0].Width).IsEqualTo(8);
        await Assert.That(sheet.Tiles[0].Height).IsEqualTo(4);
        ArtifactResourceContent? png = await artifacts.ReadAsync(new Uri(frame.Artifact.Uri));
        ImageRgba8 image = PngRgba8Decoder.Decode(png!.Content.Span);
        await Assert.That(image.Width).IsEqualTo(width);
        await Assert.That(image.Height).IsEqualTo(height);
        ArtifactResourceContent? hdr = await artifacts.ReadAsync(new Uri(frame.HdrColor!.Uri));
        await Assert.That(hdr!.Content.Length).IsEqualTo(width * height * 8);
        await Assert.That(backend.Events).DoesNotContain("apply");
        await Assert.That(backend.Events).DoesNotContain("checkpoint");
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo("#usda 1.0");
    }

    [Test]
    [Arguments(0)]
    [Arguments(3)]
    [Arguments(5)]
    public async Task InvalidProductDataWindowLengthIsRejectedBeforeWorkspaceWork(int length)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        await Assert.That(async () => await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = "unused",
            DataWindowNdc = new float[length]
        }, default)).Throws<ArgumentException>();
        await Assert.That(backend.Events).IsEmpty();
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).IsEmpty();
        await Assert.That(Directory.EnumerateDirectories(files.OutputRoot)).IsEmpty();
    }

    [Test]
    [Arguments("minXNaN")]
    [Arguments("minYNaN")]
    [Arguments("maxXNaN")]
    [Arguments("maxYNaN")]
    [Arguments("minXInfinity")]
    [Arguments("minYInfinity")]
    [Arguments("maxXInfinity")]
    [Arguments("maxYInfinity")]
    [Arguments("zeroWidth")]
    [Arguments("zeroHeight")]
    [Arguments("reversedWidth")]
    [Arguments("reversedHeight")]
    [Arguments("reversedBoth")]
    public async Task InvalidProductDataWindowBoundsAreRejectedBeforeWorkspaceWork(string kind)
    {
        float[] coordinates = kind switch
        {
            "minXNaN" => [float.NaN, 0, 1, 1],
            "minYNaN" => [0, float.NaN, 1, 1],
            "maxXNaN" => [0, 0, float.NaN, 1],
            "maxYNaN" => [0, 0, 1, float.NaN],
            "minXInfinity" => [float.NegativeInfinity, 0, 1, 1],
            "minYInfinity" => [0, float.NegativeInfinity, 1, 1],
            "maxXInfinity" => [0, 0, float.PositiveInfinity, 1],
            "maxYInfinity" => [0, 0, 1, float.PositiveInfinity],
            "zeroWidth" => [0.5f, 0, 0.5f, 1],
            "zeroHeight" => [0, 0.5f, 1, 0.5f],
            "reversedWidth" => [1, 0, 0, 1],
            "reversedHeight" => [0, 1, 1, 0],
            "reversedBoth" => [1, 1, 0, 0],
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        await Assert.That(async () => await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = "unused",
            DataWindowNdc = coordinates
        }, default)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(backend.Events).IsEmpty();
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).IsEmpty();
        await Assert.That(Directory.EnumerateDirectories(files.OutputRoot)).IsEmpty();
    }

    [Test]
    [Arguments(0.01f, 1f)]
    [Arguments(float.MaxValue, 1f)]
    [Arguments(256f, 256f)]
    [Arguments(1024.125f, 1f)]
    public async Task UnrenderableProductDataWindowsCreateNoOutputOrHistory(float maxX, float maxY)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        backend.PrepareProduct = times => Plan(
            backend.Context!.SessionId, backend.StageRevision, times, backend.LastProductOverrides);
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        await Assert.That(async () => await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Width = 8,
            Height = 8,
            DataWindowNdc = [0, 0, maxX, maxY]
        }, default)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(backend.Events).Contains("prepare-product");
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).IsEmpty();
        await Assert.That(Directory.Exists(Path.Combine(backend.Context!.OutputDirectory, "renders"))).IsFalse();
    }

    [Test]
    public async Task CaptureLimitedProductDataWindowsRefuseBeforeSourceAndAllowRetry()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        backend.PrepareProduct = times => Plan(
            backend.Context!.SessionId, backend.StageRevision, times, backend.LastProductOverrides);
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore();
        var factory = new ProductFactory();
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        await Assert.That(async () => await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Width = 8,
            Height = 8,
            DataWindowNdc = [0, 0, 512.125f, 1]
        }, default)).Throws<OpenUsdMcpFailureException>();
        await Assert.That(factory.Creations).IsEqualTo(0);
        await Assert.That(factory.Source.Validations).IsEqualTo(0);
        await Assert.That(factory.Source.Times).IsEmpty();
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).IsEmpty();
        string renders = Path.Combine(backend.Context!.OutputDirectory, "renders");
        await Assert.That(Directory.EnumerateFileSystemEntries(renders)).IsEmpty();
        McpRenderSequenceResultDto retry = await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Width = 8,
            Height = 8,
            DataWindowNdc = [0, 0, 1, 1]
        }, default);
        await Assert.That(retry.FrameCount).IsEqualTo(1);
        await Assert.That(factory.Creations).IsEqualTo(1);
        await Assert.That(factory.Source.Validations).IsEqualTo(1);
        await Assert.That(factory.Source.Times.SequenceEqual([0d])).IsTrue();
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).Count().IsEqualTo(1);
        await Assert.That(Directory.EnumerateDirectories(renders)).Count().IsEqualTo(1);
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo("#usda 1.0");
    }

    [Test]
    [Arguments(null, null, 1.5d, 1f, 2.25f)]
    [Arguments(0.5d, null, 0.5d, 1f / 0.75f, 1f)]
    [Arguments(1d, null, 1d, 1f, 1.5f)]
    [Arguments(2d, null, 2d, 1f, 3f)]
    [Arguments(null, "expandAperture", 1.5d, 1f / 2.25f, 1f)]
    [Arguments(2d, "expandAperture", 2d, 1f / 3f, 1f)]
    [Arguments(2d, "cropAperture", 2d, 1f, 3f)]
    [Arguments(2d, "adjustApertureWidth", 2d, 1f / 3f, 1f)]
    [Arguments(2d, "adjustApertureHeight", 2d, 1f, 3f)]
    [Arguments(2d, "adjustPixelAspectRatio", 2d / 3d, 1f, 1f)]
    [Arguments(null, "adjustPixelAspectRatio", 2d / 3d, 1f, 1f)]
    public async Task ProductFramingOverridesReachProjectionAndHistoricalManifestWithoutChangingAuthoredValues(
        double? pixelAspectRatio, string? conformPolicy, double effectiveAspect, float projectionX, float projectionY)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        RenderProductJobPlan? prepared = null;
        backend.PrepareProduct = times => prepared = Plan(
            backend.Context!.SessionId, backend.StageRevision, times, backend.LastProductOverrides,
            authoredPixelAspectRatio: 1.5f, authoredConformPolicy: "cropAperture");
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            FileStorageRoot: Path.Combine(files.OutputRoot, "artifacts")));
        var factory = new ProductFactory();
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        McpRenderSequenceResultDto job = await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            Width = 6,
            Height = 4,
            PixelAspectRatio = pixelAspectRatio,
            AspectRatioConformPolicy = conformPolicy
        }, default);
        await Assert.That(prepared!.Request.Product.PixelAspectRatio).IsEqualTo(1.5d);
        await Assert.That(prepared.Request.Product.AspectRatioConformPolicy).IsEqualTo("cropAperture");
        await Assert.That(prepared.Request.Overrides.PixelAspectRatio).IsEqualTo(pixelAspectRatio);
        await Assert.That(prepared.Request.Overrides.AspectRatioConformPolicy).IsEqualTo(conformPolicy);
        await Assert.That(prepared.Frames[0].PixelAspectRatio).IsEqualTo(effectiveAspect);
        await Assert.That(prepared.Frames[0].Camera.Projection.M11).IsEqualTo(projectionX);
        await Assert.That(prepared.Frames[0].Camera.Projection.M22).IsEqualTo(projectionY);
        string manifestPath = Path.Combine(backend.Context!.OutputDirectory, "renders", job.JobId, "manifest.json");
        await service.CloseSceneAsync(new SceneRevisionRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision
        }, default);
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        JsonElement raster = manifest.RootElement.GetProperty("frames")[0].GetProperty("productRaster");
        await Assert.That(raster.GetProperty("pixelAspectRatio").GetDouble()).IsEqualTo(effectiveAspect);
        await Assert.That(raster.GetProperty("aspectRatioConformPolicy").GetString())
            .IsEqualTo(conformPolicy ?? "cropAperture");
        McpSequenceFrameResultDto frame = await service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = job.JobId, FrameIndex = 0 }, default);
        await Assert.That(frame.Width).IsEqualTo(6);
        await Assert.That(frame.Height).IsEqualTo(4);
        await Assert.That(frame.ProductRaster)
            .IsEqualTo(new McpProductRasterDto(6, 4, 0, 0, effectiveAspect, conformPolicy ?? "cropAperture"));
        await Assert.That(JsonElement.DeepEquals(raster,
            JsonSerializer.SerializeToElement(frame.ProductRaster, JsonOptions))).IsTrue();
        ArtifactResourceContent? png = await artifacts.ReadAsync(new Uri(frame.Artifact.Uri));
        ImageRgba8 image = PngRgba8Decoder.Decode(png!.Content.Span);
        await Assert.That(image.Width).IsEqualTo(6);
        await Assert.That(image.Height).IsEqualTo(4);
        await Assert.That(backend.Events).DoesNotContain("apply");
        await Assert.That(backend.Events).DoesNotContain("checkpoint");
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo("#usda 1.0");
    }

    [Test]
    [Arguments(0d)]
    [Arguments(-1d)]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    [Arguments(double.NegativeInfinity)]
    public async Task InvalidProductPixelAspectIsRejectedBeforeWorkspaceWork(double pixelAspectRatio)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        await Assert.That(async () => await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = "unused",
            PixelAspectRatio = pixelAspectRatio
        }, default)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(backend.Events).IsEmpty();
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).IsEmpty();
        await Assert.That(Directory.EnumerateDirectories(files.OutputRoot)).IsEmpty();
    }

    [Test]
    [Arguments("")]
    [Arguments("ExpandAperture")]
    [Arguments("unknown")]
    [Arguments(" cropAperture")]
    [Arguments("cropAperture ")]
    public async Task InvalidProductConformPolicyIsRejectedBeforeWorkspaceWork(string conformPolicy)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        await Assert.That(async () => await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = "unused",
            AspectRatioConformPolicy = conformPolicy
        }, default)).Throws<ArgumentException>();
        await Assert.That(backend.Events).IsEmpty();
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).IsEmpty();
        await Assert.That(Directory.EnumerateDirectories(files.OutputRoot)).IsEmpty();
    }

    [Test]
    [Arguments(null, null, false)]
    [Arguments(1, 3, false)]
    [Arguments(3, 1, false)]
    [Arguments(7, 5, false)]
    [Arguments(4096, 1, false)]
    [Arguments(1, 4096, false)]
    [Arguments(8, 6, true)]
    public async Task ProductRasterOverridesReachPreparedFramesAndHistoricalOutputsWithoutChangingAuthoredSize(
        int? width, int? height, bool cropped)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        RenderProductJobPlan? prepared = null;
        var window = cropped ? new UsdVec4f(0.25f, 0, 0.75f, 1) : new UsdVec4f(0, 0, 1, 1);
        backend.PrepareProduct = times => prepared = Plan(
            backend.Context!.SessionId, backend.StageRevision, times, backend.LastProductOverrides, window);
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            FileStorageRoot: Path.Combine(files.OutputRoot, "artifacts")));
        var factory = new ProductFactory();
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        McpRenderSequenceResultDto job = await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            CameraPath = "/AlternateCamera",
            Width = width,
            Height = height
        }, default);
        await Assert.That(prepared!.Request.Product.Width).IsEqualTo(2);
        await Assert.That(prepared.Request.Product.Height).IsEqualTo(2);
        await Assert.That(prepared.Request.Product.DataWindowNdc).IsEqualTo(window);
        await Assert.That(prepared.Request.CameraPath).IsEqualTo("/AlternateCamera");
        await Assert.That(prepared.Request.Product.CameraPath).IsEqualTo("/Camera");
        await Assert.That(prepared.Request.Overrides.Resolution is null).IsEqualTo(width is null);
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(backend.Context!.OutputDirectory, "renders", job.JobId, "manifest.json")));
        JsonElement raster = manifest.RootElement.GetProperty("frames")[0].GetProperty("productRaster");
        await Assert.That(raster.GetProperty("fullWidth").GetInt32()).IsEqualTo(width ?? 2);
        await Assert.That(raster.GetProperty("fullHeight").GetInt32()).IsEqualTo(height ?? 2);
        await Assert.That(raster.GetProperty("dataWindowMinX").GetInt32()).IsEqualTo(cropped ? 2 : 0);
        await Assert.That(raster.GetProperty("dataWindowMinY").GetInt32()).IsEqualTo(0);
        await Assert.That(raster.GetProperty("pixelAspectRatio").GetDouble()).IsEqualTo(1d);
        await Assert.That(raster.GetProperty("aspectRatioConformPolicy").GetString()).IsEqualTo("expandAperture");
        await service.CloseSceneAsync(new SceneRevisionRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision
        }, default);
        McpSequenceFrameResultDto frame = await service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = job.JobId, FrameIndex = 0 }, default);
        int expectedWidth = cropped ? 4 : width ?? 2;
        int expectedHeight = height ?? 2;
        await Assert.That(frame.Width).IsEqualTo(expectedWidth);
        await Assert.That(frame.Height).IsEqualTo(expectedHeight);
        await Assert.That(frame.RenderRequest!.Width).IsEqualTo(expectedWidth);
        await Assert.That(frame.RenderRequest.Height).IsEqualTo(expectedHeight);
        ArtifactResourceContent? png = await artifacts.ReadAsync(new Uri(frame.Artifact.Uri));
        ImageRgba8 image = PngRgba8Decoder.Decode(png!.Content.Span);
        await Assert.That(image.Width).IsEqualTo(expectedWidth);
        await Assert.That(image.Height).IsEqualTo(expectedHeight);
        ArtifactResourceContent? hdr = await artifacts.ReadAsync(new Uri(frame.HdrColor!.Uri));
        await Assert.That(hdr!.Content.Length).IsEqualTo(expectedWidth * expectedHeight * 8);
        await Assert.That(factory.Source.Times.SequenceEqual([0d])).IsTrue();
        await Assert.That(backend.Events).DoesNotContain("apply");
        await Assert.That(backend.Events).DoesNotContain("checkpoint");
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo("#usda 1.0");
    }

    [Test]
    [Arguments(1, null)]
    [Arguments(null, 1)]
    [Arguments(0, 1)]
    [Arguments(1, 0)]
    [Arguments(-1, 1)]
    [Arguments(1, -1)]
    [Arguments(4097, 1)]
    [Arguments(1, 4097)]
    public async Task InvalidProductRasterPairsAreRejectedBeforeWorkspaceWork(int? width, int? height)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        await Assert.That(async () => await service.RenderProductAsync(new RenderProductCaptureRequest
        {
            SessionId = "unused",
            Width = width,
            Height = height
        }, default)).Throws<ArgumentException>();
        await Assert.That(backend.Events).IsEmpty();
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).IsEmpty();
        await Assert.That(Directory.EnumerateDirectories(files.OutputRoot)).IsEmpty();
    }

    [Test]
    public async Task ProductJobsCarryAuthoredBindingsAndShareTheExistingCompletedFrameStore()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        backend.PrepareProduct = times => Plan(backend.Context!.SessionId, backend.StageRevision, times);
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            FileStorageRoot: Path.Combine(files.OutputRoot, "artifacts")));
        var factory = new ProductFactory();
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        var progress = new List<(int, int)>();
        McpRenderSequenceResultDto job = await service.RenderProductAsync(
            Request(session, 3), default, (completed, total) => progress.Add((completed, total)));
        await Assert.That(progress.SequenceEqual([(1, 3), (2, 3), (3, 3)])).IsTrue();
        await Assert.That(job.AuthoredProduct!.ProductPath).IsEqualTo("/Render/Product");
        await Assert.That(job.AuthoredProduct.MaterialBindingPurpose).IsEqualTo("full");
        await Assert.That(job.AuthoredProduct.Outputs.Select(static item => item.SourceName)
            .SequenceEqual(["depth", "color"])).IsTrue();
        await Assert.That(backend.LastSettingsPath).IsEqualTo("/Render/Settings");
        await Assert.That(backend.LastProductPath).IsEqualTo("/Render/Product");
        await Assert.That(factory.Source.Validations).IsEqualTo(1);
        await Assert.That(factory.Source.Times.SequenceEqual([2d, 2.5, 3d])).IsTrue();
        await Assert.That(factory.Source.Purposes).IsEqualTo(RenderPurpose.Default | RenderPurpose.Render);
        McpSequenceFrameResultDto frame = await service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = job.JobId, FrameIndex = 1 }, default);
        await Assert.That(frame.AuthoredProduct).IsSameReferenceAs(job.AuthoredProduct);
        await Assert.That(frame.TimeCode).IsEqualTo(2.5);
        await Assert.That(frame.ArtifactResources.Count).IsEqualTo(3);
        await Assert.That(frame.RenderRequest!.Purposes).IsEqualTo("Default, Render");
        await Assert.That(frame.RenderRequest.Width).IsEqualTo(2);
        await Assert.That(frame.RenderRequest.Height).IsEqualTo(2);
        await Assert.That(frame.RenderRequest.OutputTransform).IsEqualTo("Reinhard");
        await Assert.That(frame.RenderRequest.Exposure).IsEqualTo(-6);
        ArtifactResourceContent? hdr = await artifacts.ReadAsync(new Uri(frame.HdrColor!.Uri));
        await Assert.That(hdr!.Content.Length).IsEqualTo(32);
        await Assert.That(backend.Events).DoesNotContain("apply");
        await Assert.That(backend.Events).DoesNotContain("checkpoint");
        await Assert.That(File.Exists(Path.Combine(files.SourceRoot, "not-authorized.exr"))).IsFalse();
        await service.CloseSceneAsync(new SceneRevisionRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision
        }, default);
        McpSequenceJobsResultDto history = await service.ListSequenceJobsAsync(default);
        await Assert.That(history.Jobs.Count).IsEqualTo(1);
        await Assert.That(history.Jobs[0]).IsSameReferenceAs(job);
        await Assert.That(history.Jobs[0].AuthoredProduct).IsSameReferenceAs(job.AuthoredProduct);
        McpSequenceSheetResultDto sheet = await service.ReadSequenceSheetAsync(new ReadSequenceSheetRequest
        {
            JobId = job.JobId,
            FrameIndices = [2, 0],
            Width = 8,
            Height = 4
        }, default);
        JsonElement json = JsonSerializer.SerializeToElement(sheet, JsonOptions);
        await Assert.That(json.TryGetProperty("authoredProduct", out JsonElement product)).IsTrue();
        await Assert.That(product.GetProperty("productPath").GetString()).IsEqualTo("/Render/Product");
        await Assert.That(product.GetProperty("materialBindingPurpose").GetString()).IsEqualTo("full");
        await Assert.That(sheet.AuthoredProduct).IsSameReferenceAs(job.AuthoredProduct);
        await Assert.That(sheet.AuthoredProduct!.Outputs.Select(static item => item.SourceName)
            .SequenceEqual(["depth", "color"])).IsTrue();
        await Assert.That(sheet.SessionId).IsEqualTo(job.SessionId);
        await Assert.That(sheet.StageRevision).IsEqualTo(job.StageRevision);
        await Assert.That(sheet.Tiles.Select(static tile => tile.TimeCode).SequenceEqual([3d, 2d])).IsTrue();
        foreach (McpSequenceSheetTileDto tile in sheet.Tiles)
        {
            await Assert.That(tile.RenderRequest!.Purposes).IsEqualTo("Default, Render");
            await Assert.That(tile.RenderRequest.Width).IsEqualTo(2);
            await Assert.That(tile.RenderRequest.Height).IsEqualTo(2);
            await Assert.That(tile.RenderRequest.Exposure).IsEqualTo(-6);
        }
        await Assert.That(factory.Source.Times.SequenceEqual([2d, 2.5, 3d])).IsTrue();
    }

    [Test]
    public async Task ProductJobsPublishThePrimIdPlaneAndItsResolvableIdentityTable()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        backend.PrepareProduct = times =>
            Plan(backend.Context!.SessionId, backend.StageRevision, times, includePrimId: true);
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore(new ArtifactResourceStoreOptions(
            FileStorageRoot: Path.Combine(files.OutputRoot, "artifacts")));
        var factory = new ProductFactory();
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        McpRenderSequenceResultDto job = await service.RenderProductAsync(Request(session, 1), default);
        await Assert.That(job.AuthoredProduct!.Outputs.Select(static item => item.Plane)
            .SequenceEqual(["deviceDepth", "hdrColor", "primId"])).IsTrue();
        await Assert.That(job.AuthoredProduct.Outputs[2].DataType).IsEqualTo("int");
        McpSequenceFrameResultDto frame = await service.ReadSequenceFrameAsync(
            new ReadSequenceFrameRequest { JobId = job.JobId, FrameIndex = 0 }, default);
        await Assert.That(frame.ArtifactResources.Count).IsEqualTo(5);
        await Assert.That(frame.PrimId!.MimeType).IsEqualTo("application/octet-stream");
        await Assert.That(frame.PrimIdTable!.MimeType).IsEqualTo("application/json");
        ArtifactResourceContent? raw = await artifacts.ReadAsync(new Uri(frame.PrimId.Uri));
        int[] values = [.. Enumerable.Range(0, 4).Select(index =>
            BinaryPrimitives.ReadInt32LittleEndian(raw!.Content.Span.Slice(index * 4, 4)))];
        await Assert.That(values.SequenceEqual([0, -1, 1, 0])).IsTrue();
        ArtifactResourceContent? table = await artifacts.ReadAsync(new Uri(frame.PrimIdTable.Uri));
        using JsonDocument document = JsonDocument.Parse(table!.Content);
        await Assert.That(document.RootElement.GetProperty("background").GetInt32()).IsEqualTo(-1);
        JsonElement identities = document.RootElement.GetProperty("identities");
        await Assert.That(identities.GetArrayLength()).IsEqualTo(2);
        await Assert.That(identities[values[2]].GetProperty("path").GetString()).IsEqualTo("/World/B");
        await Assert.That(identities[values[2]].GetProperty("instanceIndex").GetInt32()).IsEqualTo(3);
        await Assert.That(identities[values[0]].GetProperty("path").GetString()).IsEqualTo("/World/A");
        JsonElement json = JsonSerializer.SerializeToElement(frame, JsonOptions);
        await Assert.That(json.GetProperty("primId").GetProperty("uri").GetString()).IsEqualTo(frame.PrimId.Uri);
        await Assert.That(json.GetProperty("primIdTable").GetProperty("uri").GetString())
            .IsEqualTo(frame.PrimIdTable.Uri);
    }

    [Test]
    public async Task PrimIdProductsAreRefusedOnlyOnNonPickGraphicsDevices()
    {
        RenderProductJobPlan primId = Plan("session", 1, [0d], includePrimId: true);
        RenderProductJobPlan plain = Plan("session", 1, [0d]);
        ISilkGraphicsDevice device = DispatchProxy.Create<ISilkGraphicsDevice, UnusedDeviceProxy>();
        await Assert.That(primId.IncludePrimId).IsTrue();
        await Assert.That(() => PreviewSilkFrameSource.RequirePrimIdDevice(primId, device))
            .Throws<NotSupportedException>()
            .WithMessage("The MCP capture graphics device cannot run the pick pass needed for the primId identity plane.");
        PreviewSilkFrameSource.RequirePrimIdDevice(plain, device);
    }

    [Test]
    public async Task RefusedProductsConsumeNoJobSlotAndProductAndViewportJobsShareTheQuota()
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        backend.PrepareProduct = times => Plan(backend.Context!.SessionId, backend.StageRevision, times);
        await using var workspace = files.CreateWorkspace(backend);
        using var artifacts = new ArtifactResourceStore();
        var factory = new ProductFactory();
        factory.Source.Refuse = true;
        using var processor = new PreviewCaptureProcessor(factory, artifacts);
        await using var worker = new CaptureWorker(processor);
        using ServiceProvider provider = new ServiceCollection().AddSingleton(worker)
            .AddSingleton<IArtifactResourceStore>(artifacts).BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        await Assert.That(async () => await service.RenderProductAsync(Request(session, 1), default))
            .Throws<OpenUsdMcpFailureException>();
        await Assert.That(factory.Source.Times).IsEmpty();
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs).IsEmpty();
        factory.Source.Refuse = false;
        _ = await service.RenderProductAsync(Request(session, 1), default);
        for (int index = 0; index < 7; index++)
        {
            _ = await service.RenderSequenceAsync(new RenderSequenceRequest
            {
                SessionId = session.SessionId,
                Generation = session.Generation,
                StageRevision = session.StageRevision,
                FrameCount = 1,
                Width = 2,
                Height = 2
            }, default);
        }
        int preparations = backend.Events.Count(static item => item == "prepare-product");
        await Assert.That(async () => await service.RenderProductAsync(Request(session, 1), default))
            .Throws<WorkspaceQuotaExceededException>();
        await Assert.That(backend.Events.Count(static item => item == "prepare-product")).IsEqualTo(preparations);
        await Assert.That((await service.ListSequenceJobsAsync(default)).Jobs.Count).IsEqualTo(8);
    }

    [Test]
    [Arguments("format")]
    [Arguments("settings")]
    [Arguments("product")]
    [Arguments("camera")]
    [Arguments("time")]
    [Arguments("count")]
    public async Task MalformedProductRequestsAreRejectedBeforeWorkspaceWork(string failure)
    {
        using var files = new WorkspaceTestFiles();
        var backend = new RecordingWorkspaceBackend();
        await using var workspace = files.CreateWorkspace(backend);
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        using var service = new OpenUsdMcpService(workspace, provider, Options(files));
        var request = new RenderProductCaptureRequest
        {
            SessionId = "unused",
            SettingsPath = failure == "settings" ? "relative" : null,
            ProductPath = failure == "product" ? "relative" : null,
            CameraPath = failure == "camera" ? "relative" : null,
            HdrColorFormat = failure == "format" ? "png" : "raw",
            TimeStep = failure == "time" ? 0 : 1,
            FrameCount = failure == "count" ? 4097 : 1
        };
        await Assert.That(async () => await service.RenderProductAsync(request, default)).Throws<ArgumentException>();
        await Assert.That(backend.Events).IsEmpty();
    }

    private static RenderProductCaptureRequest Request(McpSessionDto session, int count) => new()
    {
        SessionId = session.SessionId,
        Generation = session.Generation,
        StageRevision = session.StageRevision,
        SettingsPath = "/Render/Settings",
        ProductPath = "/Render/Product",
        FrameCount = count,
        StartTimeCode = 2,
        TimeStep = 0.5
    };

    private static RenderProductJobPlan Plan(
        string sessionId, ulong revision, IReadOnlyList<double> times, RenderProductOverrides? overrides = null,
        UsdVec4f? dataWindow = null, float authoredPixelAspectRatio = 1,
        string authoredConformPolicy = "expandAperture", Func<double, UsdGeomCameraState>? cameraAtTime = null,
        Func<double, RenderCameraFrameSettings>? cameraSettingsAtTime = null,
        bool disableMotionBlur = true, bool disableDepthOfField = true, bool includePrimId = false)
    {
        var specification = new UsdRenderSpecification("/Render/Settings",
            [new UsdRenderProductSpecification("/Render/Product", "../not-authorized.exr", "raster", "/Camera",
                2, 2, authoredPixelAspectRatio, authoredConformPolicy, new UsdVec2f(20, 20),
                dataWindow ?? new UsdVec4f(0, 0, 1, 1),
                disableMotionBlur, disableDepthOfField, includePrimId ? [1, 0, 2] : [1, 0], [])],
            [new UsdRenderVariableSpecification("/Render/Color", "half4", "color", "raw", []),
                new UsdRenderVariableSpecification("/Render/Depth", "float", "depth", "raw", []),
                new UsdRenderVariableSpecification("/Render/PrimId", "int", "primId", "raw", [])],
            ["default", "render"], ["full"], "", []);
        var request = new RenderProductRequest(specification, 0, overrides);
        var optics = new UsdGeomCameraState(
            UsdGeomCameraProjection.Orthographic, -1, 1, -1, 1, 1, 11, 0, 20, 20, 0, 0, 0, 0);
        RenderPreparedFrame[] frames = [.. times.Select(time =>
            request.PrepareFrame(UsdMatrix4d.Identity, cameraAtTime?.Invoke(time) ?? optics, time,
                cameraSettingsAtTime?.Invoke(time) ?? RenderCameraFrameSettings.Default))];
        return new RenderProductJobPlan(new StageIdentity($"session:{sessionId}"), frames,
            RenderSettings.PresentationDefault, revision);
    }

    private static OpenUsdMcpApplicationOptions Options(WorkspaceTestFiles files) => new(
        files.SourceRoot, files.OutputRoot, files.SourceRoot, files.OutputRoot,
        Path.Combine(files.OutputRoot, "viewer-not-launched.exe"));

    public class UnusedDeviceProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException("The refusal test must not touch the graphics device.");
    }

    private sealed class ProductFactory : IPreviewFrameSourceFactory
    {
        internal ProductSource Source { get; } = new();
        internal int Creations { get; private set; }

        public IPreviewFrameSource Create(
            PreviewCaptureRequest request, CancellationToken cancellationToken = default)
        {
            Creations++;
            return Source;
        }
    }

    private sealed class ProductSource : IPreviewFrameSource, IPreviewProductFrameSource
    {
        internal bool Refuse { get; set; }
        internal int Validations { get; private set; }
        internal List<double> Times { get; } = [];
        internal List<double> ViewportTimes { get; } = [];
        internal RenderPurpose Purposes { get; private set; }

        public void ValidateProduct(RenderProductJobPlan plan, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validations++;
            if (Refuse)
            {
                throw new NotSupportedException("Controlled unsupported material binding.");
            }
            Purposes = plan.IncludedPurposes;
        }

        public RenderJobImage CaptureProduct(
            RenderProductJobPlan plan, StageRenderState state, long maximumReadbackBytes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Times.Add(state.Time.TimeCode);
            int width = state.Viewport.Width;
            int height = state.Viewport.Height;
            return new RenderJobImage(width, height, new byte[width * height * 4], Rgba8RowOrder.TopDown)
            {
                HdrColor = new RenderJobHdrColor(width, height, new byte[width * height * 8]),
                DeviceDepth = new RenderJobDeviceDepth(width, height, new float[width * height]),
                PrimId = plan.IncludePrimId
                    ? new RenderJobPrimId(width, height, new[] { 0, -1, 1, 0 },
                        [new RenderJobPrimIdentity("/World/A", -1), new RenderJobPrimIdentity("/World/B", 3)])
                    : null
            };
        }

        public ImageRgba8 Capture(CaptureView view, int width, int height)
        {
            ViewportTimes.Add(view.TimeCode);
            return new(width, height, new byte[width * height * 4]);
        }
        public void Dispose() { }
    }
}
