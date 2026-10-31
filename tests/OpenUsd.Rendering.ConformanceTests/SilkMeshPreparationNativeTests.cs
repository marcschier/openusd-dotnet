// Copyright (c) marcschier. Licensed under the MIT License.

using System.Numerics;
using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkMeshPreparationNativeTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task AuthoredReaderFallbackAndTimeEditsMatchExplicitCoordinates(SilkGraphicsBackend backend)
    {
        const string material = """
            def Material "Material" {
                token outputs:surface.connect = </Material/Surface.outputs:surface>
                def Shader "Surface" {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0,0,0)
                    color3f inputs:emissiveColor.connect = </Material/Texture.outputs:rgb>
                    token outputs:surface
                }
                def Shader "Texture" {
                    uniform token info:id = "UsdUVTexture"
                    asset inputs:file = @colors.png@
                    token inputs:sourceColorSpace = "raw"
                    token inputs:wrapS = "clamp"
                    token inputs:wrapT = "clamp"
                    float2 inputs:st.connect = </Material/Transform.outputs:result>
                    float3 outputs:rgb
                }
                def Shader "Transform" {
                    uniform token info:id = "UsdTransform2d"
                    float2 inputs:scale = (0.5,1)
                    float2 inputs:translation = (0.25,0)
                    float2 inputs:in.connect = </Material/Reader.outputs:result>
                    float2 outputs:result
                }
                def Shader "Reader" {
                    uniform token info:id = "UsdPrimvarReader_float2"
                    token inputs:varname = "uvSet1"
                    float2 inputs:fallback.timeSamples = {0:(0,0.5), 1:(1,0.5)}
                    float2 outputs:result
                }
            }
            """;
        string mesh = Mesh.Replace("def Mesh \"Mesh\" {",
            "def Mesh \"Mesh\" (prepend apiSchemas = [\"MaterialBindingAPI\"]) {\nrel material:binding = </Material>",
            StringComparison.Ordinal);
        string explicitMesh = mesh.Replace("uniform token subdivisionScheme",
            "texCoord2f[] primvars:uvSet1 (interpolation = \"constant\")\n" +
            "texCoord2f[] primvars:uvSet1.timeSamples = {0:[(0,0.5)], 1:[(1,0.5)]}\nuniform token subdivisionScheme",
            StringComparison.Ordinal);
        using Fixture actualFixture = Create(material + "\n" + mesh);
        using Fixture expectedFixture = Create(material + "\n" + explicitMesh);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        new SilkGpuBufferBudget(4_194_304).ConfigureDevice(device);
        new SilkTexturePreparationBudgets(new(4_194_304), new(4_194_304), new(4_194_304)).ConfigureDevice(device);
        var policy = new SilkTextureResidencyOptions(1024, 1024, true);
        var limits = new SilkPreparationLimits(1_000_000, 65536, new(131072, 65536, 65536, 31));
        foreach (Fixture fixture in new[] { actualFixture, expectedFixture })
        {
            using FileStream file = File.Create(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(
                fixture.Path)!, "colors.png"));
            _ = PngRgba8Writer.Write(file, 2, 1, [255, 0, 0, 255, 0, 0, 255, 255], 1024);
        }
        string source = await File.ReadAllTextAsync(actualFixture.Path);
        using OpenUsdSilkSession actualSession = OpenUsdSilkRuntime.Create(actualFixture.Plugins,
            actualFixture.Path, limits);
        using OpenUsdSilkSession expectedSession = OpenUsdSilkRuntime.Create(expectedFixture.Plugins,
            expectedFixture.Path, limits);
        using var actual = new SilkMeshRenderer(device, policy);
        using var expected = new SilkMeshRenderer(device, policy);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.HdrColorTarget(32, 32));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.SampledDepthTarget(32, 32));
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0.5f, 0.5f, 3), new Vector3(0.5f, 0.5f, 0), Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        byte[]? first = null;
        for (int time = 0; time < 2; time++)
        {
            using OpenUsdSilkPage actualPage = actualSession.Sync(32, 32, time, camera);
            _ = actual.ApplyAndRender(actualPage, color, depth);
            SilkVertexAttributeData uv = actual.Scene.Meshes.Values.Single().FindTexCoord("uvSet1")!;
            await Assert.That(uv.Interpolation).IsEqualTo(SilkAttributeInterpolation.Constant);
            await Assert.That(uv.Data.ToArray()).IsEquivalentTo([(float)time, 0.5f]);
            await Assert.That(actual.GpuResources.Diagnostics.Entries.Select(static entry => entry.Code))
                .DoesNotContain(SilkRenderDiagnosticCodes.TextureCoordinatesUnavailable);
            byte[] pixels = new byte[32 * 32 * 8];
            color.ReadbackForTesting(pixels);
            using OpenUsdSilkPage expectedPage = expectedSession.Sync(32, 32, time, camera);
            _ = expected.ApplyAndRender(expectedPage, color, depth);
            byte[] reference = new byte[pixels.Length];
            color.ReadbackForTesting(reference);
            await Assert.That(pixels.SequenceEqual(reference)).IsTrue();
            if (first is null)
            {
                first = pixels;
            }
            else
            {
                await Assert.That(pixels.SequenceEqual(first)).IsFalse();
            }
        }
        await Assert.That(await File.ReadAllTextAsync(actualFixture.Path)).IsEqualTo(source);
    }

    private const string Mesh = """
        def Mesh "Mesh" {
            uniform token subdivisionScheme = "none"
            point3f[] points = [(0,0,0),(1,0,0),(1,1,0),(0,1,0)]
            int[] faceVertexCounts = [4]
            int[] faceVertexIndices = [0,1,2,3]
            normal3f[] normals = [(0,0,1)] (interpolation = "constant")
            texCoord2f[] primvars:st = [(0,0),(1,0),(1,1),(0,1)] (interpolation = "faceVarying")
        }
        """;

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public Task FileBackedCapturesMatchPixelsAndDepthAndReleaseTheirLiveFileCredit(SilkGraphicsBackend backend) =>
        VerifyStoredCapture(backend, hardware: false);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public Task FileBackedHardwareCapturesMatchPixelsDepthAndCredit(SilkGraphicsBackend backend)
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_STORED_UPDATE_HARDWARE_REQUIRED") != "1")
        {
            Skip.Test("Set OPENUSD_STORED_UPDATE_HARDWARE_REQUIRED=1 with a hardware Vulkan ICD and native runtime.");
        }
        return VerifyStoredCapture(backend, hardware: true);
    }

    private static async Task VerifyStoredCapture(SilkGraphicsBackend backend, bool hardware)
    {
        using Fixture fixture = Create(Mesh);
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0.5f, 0.5f, 3), new Vector3(0.5f, 0.5f, 0), Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        int bytes;
        using (OpenUsdSilkSession sizing = fixture.Open())
        using (OpenUsdSilkPage page = sizing.Sync(32, 32, Options(1_000_000), camera: camera))
        {
            bytes = page.ByteLength;
        }
        var limits = new SilkPreparationLimits(1_000_000, bytes,
            new((ulong)bytes, (ulong)bytes, bytes, 31));
        using OpenUsdSilkSession stored = OpenUsdSilkRuntime.Create(fixture.Plugins, fixture.Path, limits);
        using OpenUsdSilkSession reference = fixture.Open();
        using ISilkGraphicsDevice device = hardware
            && backend == SilkGraphicsBackend.D3D12 && OperatingSystem.IsWindows()
            ? OpenUsd.Rendering.Silk.D3D12.D3D12SilkGraphicsDevice.Create(useWarp: false)
            : SilkDepthCaptureConformance.CreateDevice(backend);
        await Assert.That(device.Capabilities.IsSoftware).IsEqualTo(!hardware);
        Console.WriteLine(
            $"STORED_CAPTURE_DEVICE={device.Capabilities.DeviceName}; software={device.Capabilities.IsSoftware}");
        using var expectedCapturer = new SilkFrameCapturer(device);
        using var actualCapturer = new SilkFrameCapturer(device);
        SilkFrameCaptureResult expected = expectedCapturer.CaptureWithDepth(
            reference, 32, 32, RenderSettings.Default, Options(1_000_000), camera: camera);
        for (int frame = 0; frame < 3; frame++)
        {
            SilkFrameCaptureResult actual = actualCapturer.CaptureWithDepth(
                stored, 32, 32, RenderSettings.Default, Options(1_000_000), camera: camera);
            await Assert.That(actual.Rgba.Span.SequenceEqual(expected.Rgba.Span)).IsTrue();
            await Assert.That(actual.Depth!.Values.Span.SequenceEqual(expected.Depth!.Values.Span)).IsTrue();
            await Assert.That(actual.Depth.Values.ToArray().Any(static depth => depth < 1)).IsTrue();
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "texture")]
    [Arguments(SilkGraphicsBackend.Vulkan, "texture")]
    [Arguments(SilkGraphicsBackend.D3D12, "staging")]
    [Arguments(SilkGraphicsBackend.Vulkan, "staging")]
    public async Task EnvironmentRefusalKeepsOldPixelsAndReplaysRetirementBeforeNativeAdvance(
        SilkGraphicsBackend backend, string kind)
    {
        const string dome = """
            def DomeLight "Environment" {
                asset inputs:texture:file = @before.hdr@
                token inputs:texture:format = "latlong"
                float inputs:intensity = 1
                float inputs:specular = 0
            }
            """;
        using Fixture fixture = Create(dome + "\n" + Mesh + "\n" +
            Mesh.Replace("\"Mesh\"", "\"Removed\"", StringComparison.Ordinal));
        string root = Path.GetDirectoryName(fixture.Path)!;
        await File.WriteAllTextAsync(Path.Combine(root, "before.hdr"), "owned-before-environment-stamp");
        await File.WriteAllTextAsync(Path.Combine(root, "after.hdr"), "owned-after-environment-stamp");
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var textures = new SilkGpuTextureBudget(16_777_216);
        var staging = new SilkGpuStagingBudget(16_777_216);
        textures.ConfigureDevice(device);
        staging.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device,
            backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV,
            (path, _) =>
            {
                float[] pixels = new float[8 * 4 * 4];
                bool before = path.Contains("before", StringComparison.Ordinal);
                for (int index = 0; index < pixels.Length; index += 4)
                {
                    pixels[index + (before ? 0 : 2)] = 1f;
                    pixels[index + 3] = 1f;
                }
                return new SilkDecodedImage(8, 4, MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray(),
                    SilkTextureFormat.Rgba32Float);
            });
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0.5f, 0.5f, 3), new Vector3(0.5f, 0.5f, 0), Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        using OpenUsdSilkPage initial = session.Sync(32, 32, camera: camera);
        renderer.ApplyPage(initial);
        await Assert.That(renderer.GpuResources.EnvironmentBinding.Enabled).IsTrue();
        byte[] beforePixels = SilkFrameCapture.CaptureRetained(
            renderer, device, 32, 32, RenderSettings.Default).Rgba.ToArray();
        const int center = (16 * 32 + 16) * 4;
        await Assert.That(beforePixels[center]).IsGreaterThan(beforePixels[center + 2]);
        ulong revision = renderer.Scene.Revision;
        await scheduler.InvokeAsync(stage =>
        {
            var environment = OpenUsd.Lux.UsdLuxDomeLight.Wrap(stage.GetPrim("/Environment"));
            environment.TextureFile = new UsdAssetPath("after.hdr");
            stage.RemovePrim("/Removed");
        });
        using OpenUsdSilkPage update = session.Sync(32, 32, camera: camera);
        IDisposable reserve() => kind == "staging"
            ? staging.Reserve(staging.MaximumBytes - staging.Usage.ReservedBytes - 1)
            : textures.Reserve(new SilkTextureDescriptor(
                checked((uint)((textures.MaximumBytes - textures.Usage.ReservedBytes) / 16)),
                1, SilkTextureFormat.Rgba32Float, SilkTextureUsage.Sampled), 1);
        bool expected(Exception? error) => kind == "staging"
            ? error is SilkGpuStagingBudgetExceededException
            : error is SilkGpuTextureBudgetExceededException;
        using (IDisposable pressure = reserve())
        {
            Exception? error = await Assert.That(() => renderer.ApplyPage(update)).ThrowsException();
            await Assert.That(expected(error)).IsTrue();
            await Assert.That(renderer.Scene.Revision).IsEqualTo(revision);
            await Assert.That(renderer.Scene.MeshesByPath.ContainsKey(("/Removed", 0))).IsTrue();
        }
        byte[] refused = SilkFrameCapture.CaptureRetained(
            renderer, device, 32, 32, RenderSettings.Default).Rgba.ToArray();
        await Assert.That(refused.SequenceEqual(beforePixels)).IsTrue();
        update.Dispose();
        using (IDisposable pressure = reserve())
        {
            Exception? error = await Assert.That(() => session.Sync(32, 32, camera: camera)).ThrowsException();
            await Assert.That(expected(error)).IsTrue();
            await Assert.That(renderer.Scene.Revision).IsEqualTo(revision);
        }
        using OpenUsdSilkPage retry = session.Sync(32, 32, camera: camera);
        renderer.ApplyPage(retry);
        await Assert.That(renderer.Scene.MeshesByPath.ContainsKey(("/Removed", 0))).IsFalse();
        byte[] afterPixels = SilkFrameCapture.CaptureRetained(
            renderer, device, 32, 32, RenderSettings.Default).Rgba.ToArray();
        await Assert.That(afterPixels[center + 2]).IsGreaterThan(afterPixels[center]);
        renderer.Dispose();
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "texture", false)]
    [Arguments(SilkGraphicsBackend.Vulkan, "texture", false)]
    [Arguments(SilkGraphicsBackend.D3D12, "cpu", false)]
    [Arguments(SilkGraphicsBackend.Vulkan, "cpu", false)]
    [Arguments(SilkGraphicsBackend.D3D12, "staging", false)]
    [Arguments(SilkGraphicsBackend.Vulkan, "staging", false)]
    [Arguments(SilkGraphicsBackend.D3D12, "texture", true)]
    [Arguments(SilkGraphicsBackend.Vulkan, "texture", true)]
    [Arguments(SilkGraphicsBackend.D3D12, "cpu", true)]
    [Arguments(SilkGraphicsBackend.Vulkan, "cpu", true)]
    [Arguments(SilkGraphicsBackend.D3D12, "staging", true)]
    [Arguments(SilkGraphicsBackend.Vulkan, "staging", true)]
    public async Task MaterialPreparationRefusalPreservesPixelsAndReplaysNativeRetirements(
        SilkGraphicsBackend backend, string kind, bool promoted)
    {
        const string material = """
            def Material "Material" {
                token outputs:surface.connect = </Material/Surface.outputs:surface>
                def Shader "Surface" {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor.connect = </Material/Texture.outputs:rgb>
                    float inputs:roughness = 1
                    token outputs:surface
                }
                def Shader "Texture" {
                    uniform token info:id = "UsdUVTexture"
                    asset inputs:file = @before.png@
                    token inputs:sourceColorSpace = "raw"
                    float2 inputs:st.connect = </Material/Reader.outputs:result>
                    float3 outputs:rgb
                }
                def Shader "Reader" {
                    uniform token info:id = "UsdPrimvarReader_float2"
                    token inputs:varname = "st"
                    float2 outputs:result
                }
            }
            """;
        string mesh = Mesh.Replace("def Mesh \"Mesh\" {",
            "def Mesh \"Mesh\" (prepend apiSchemas = [\"MaterialBindingAPI\"]) {\n" +
            "rel material:binding = </Material>", StringComparison.Ordinal);
        string definition = promoted ? material.Replace("token inputs:sourceColorSpace = \"raw\"",
            "token inputs:sourceColorSpace = \"raw\"\nfloat4 inputs:scale = (3, 3, 3, 1)\n" +
            "float4 inputs:bias = (-1, -1, -1, 0)", StringComparison.Ordinal) : material;
        using Fixture fixture = Create(definition + "\n" + mesh + "\n" +
            mesh.Replace("\"Mesh\"", "\"Removed\"", StringComparison.Ordinal));
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(
            fixture.Plugins, source, new SilkPreparationLimits(1_000_000, 1_000_000));
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var textures = new SilkGpuTextureBudget(1_048_576);
        var cpu = new SilkCpuTextureBudget(1_048_576);
        var staging = new SilkGpuStagingBudget(1_048_576);
        textures.ConfigureDevice(device);
        cpu.ConfigureDevice(device);
        staging.ConfigureDevice(device);
        static SilkDecodedImage decode(string path, bool _) => new(
            1, 1, path.Contains("before", StringComparison.Ordinal) ? [255, 0, 0, 255] : [0, 0, 255, 255]);
        using var renderer = new SilkMeshRenderer(device,
            backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV,
            decode, cpuTextureBudget: cpu, ownedImageDecoder: (path, linear, budget) =>
            {
                IDisposable reservation = budget.Reserve(4);
                SilkDecodedImage image = decode(path, linear);
                image.OwnReservation(reservation);
                return image;
            });
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0.5f, 0.5f, 3), new Vector3(0.5f, 0.5f, 0), Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        using OpenUsdSilkPage initial = session.Sync(32, 32, camera: camera);
        renderer.ApplyPage(initial);
        byte[] before = SilkFrameCapture.CaptureRetained(
            renderer, device, 32, 32, RenderSettings.Default).Rgba.ToArray();
        ulong revision = renderer.Scene.Revision;
        ulong meshCount = (ulong)renderer.Scene.Meshes.Count;
        await scheduler.InvokeAsync(stage =>
        {
            OpenUsd.Shade.UsdShadeShader.Wrap(stage.GetPrim("/Material/Texture"))
                .GetInput("file").SetAssetPath(new UsdAssetPath("after.png"));
            stage.RemovePrim("/Removed");
        });
        using OpenUsdSilkPage update = session.Sync(32, 32, camera: camera);
        IDisposable reserve() => kind switch
        {
            "cpu" => cpu.Reserve(cpu.MaximumBytes - cpu.Usage.ReservedBytes - 1),
            "staging" => staging.Reserve(staging.MaximumBytes - staging.Usage.ReservedBytes - 1),
            _ => textures.Reserve(new SilkTextureDescriptor(
                checked((uint)((textures.MaximumBytes - textures.Usage.ReservedBytes) / 4)),
                1, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.Sampled), 1)
        };
        bool isExpected(Exception? failure) => kind switch
        {
            "cpu" => failure is SilkCpuTextureBudgetExceededException,
            "staging" => failure is SilkGpuStagingBudgetExceededException,
            _ => failure is SilkGpuTextureBudgetExceededException
        };
        using (IDisposable pressure = reserve())
        {
            ulong oldTextures = textures.Usage.ReservedBytes;
            ulong oldCpu = cpu.Usage.ReservedBytes;
            ulong oldStaging = staging.Usage.ReservedBytes;
            Exception? failure = await Assert.That(() => renderer.ApplyPage(update)).ThrowsException();
            await Assert.That(isExpected(failure)).IsTrue();
            await Assert.That(renderer.Scene.Revision).IsEqualTo(revision);
            await Assert.That((ulong)renderer.Scene.Meshes.Count).IsEqualTo(meshCount);
            await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(oldTextures);
            await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(oldCpu);
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(oldStaging);
        }
        byte[] refused = SilkFrameCapture.CaptureRetained(
            renderer, device, 32, 32, RenderSettings.Default).Rgba.ToArray();
        await Assert.That(refused.SequenceEqual(before)).IsTrue();
        update.Dispose();
        using (IDisposable pressure = reserve())
        {
            Exception? failure = await Assert.That(() => session.Sync(32, 32, camera: camera)).ThrowsException();
            await Assert.That(isExpected(failure)).IsTrue();
            await Assert.That(renderer.Scene.Revision).IsEqualTo(revision);
        }
        using OpenUsdSilkPage retry = session.Sync(32, 32, camera: camera);
        renderer.ApplyPage(retry);
        await Assert.That(renderer.Scene.MeshesByPath.ContainsKey(("/Removed", 0))).IsFalse();
        byte[] after = SilkFrameCapture.CaptureRetained(
            renderer, device, 32, 32, RenderSettings.Default).Rgba.ToArray();
        await Assert.That(after.SequenceEqual(before)).IsFalse();
        renderer.Dispose();
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, "color")]
    [Arguments(SilkGraphicsBackend.Vulkan, "color")]
    [Arguments(SilkGraphicsBackend.D3D12, "hdr")]
    [Arguments(SilkGraphicsBackend.Vulkan, "hdr")]
    [Arguments(SilkGraphicsBackend.D3D12, "cpu-ocio")]
    [Arguments(SilkGraphicsBackend.Vulkan, "cpu-ocio")]
    [Arguments(SilkGraphicsBackend.D3D12, "gpu-ocio")]
    [Arguments(SilkGraphicsBackend.Vulkan, "gpu-ocio")]
    public async Task CaptureReadbackRefusalKeepsTheNativePageUnpublished(SilkGraphicsBackend backend, string mode)
    {
        using Fixture fixture = Create(Mesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(
            fixture.Plugins, source, new SilkPreparationLimits(1_000_000, 1_000_000));
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var staging = new SilkGpuStagingBudget(4_194_304);
        staging.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0.5f, 0.5f, 3), new Vector3(0.5f, 0.5f, 0), Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        RenderSettings settings = mode == "gpu-ocio" ? RenderSettings.Default with
        {
            DisplayTransform = new RenderDisplayTransform(
                SilkHdrColorCaptureFixture.OcioConfigPath, "linear", "TestDisplay", "TestView")
        } : RenderSettings.Default;
        SilkDepthCaptureRequest? request = mode == "color" ? null : SilkDepthCaptureRequest.ForHdrColor(
            32, 32, new SilkHdrColorCaptureOptions(includeDeviceDepth: true), CancellationToken.None);
        using SilkOpenColorIoProcessor? processor = mode == "cpu-ocio"
            ? SilkHdrColorCaptureFixture.CreateProcessor("TestView") : null;
        SilkFrameCaptureResult capture(SilkOpenColorIoProcessor? current = null) => mode == "cpu-ocio"
            ? SilkFrameCapture.CaptureCoreOcio(
                session, device, renderer, 32, 32, settings, current ?? processor!, 0, camera, request)
            : SilkFrameCapture.CaptureCore(session, device, renderer, 32, 32, settings, 0, camera, request);
        if (mode != "color")
        {
            renderer.UpdateSelection(new SelectionState(["/Mesh"]));
        }
        SilkFrameCaptureResult before = capture();
        ulong revision = renderer.Scene.Revision;
        SilkMeshGpuResource original = renderer.GpuResources.MeshValues.Single();
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray("points",
            [new(0.5f, 0, 0), new(1.5f, 0, 0), new(1.5f, 1, 0), new(0.5f, 1, 0)]));
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        using (staging.Reserve(staging.MaximumBytes))
        {
            await Assert.That(() => capture()).Throws<SilkGpuStagingBudgetExceededException>();
            await Assert.That(renderer.Scene.Revision).IsEqualTo(revision);
            await Assert.That(renderer.GpuResources.MeshValues.Single()).IsSameReferenceAs(original);
            processor?.Dispose();
            await Assert.That(() => session.Sync(32, 32, camera: camera))
                .Throws<SilkGpuStagingBudgetExceededException>();
            await Assert.That(renderer.Scene.Revision).IsEqualTo(revision);
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(staging.MaximumBytes);
        }
        using SilkOpenColorIoProcessor? retryProcessor = mode == "cpu-ocio"
            ? SilkHdrColorCaptureFixture.CreateProcessor("TestView") : null;
        SilkFrameCaptureResult after = capture(retryProcessor);
        await Assert.That(after.Rgba.Span.SequenceEqual(before.Rgba.Span)).IsFalse();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(revision + 2);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
        using OpenUsdSilkSession reference = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var referenceRenderer = new SilkMeshRenderer(device);
        referenceRenderer.UpdateSelection(renderer.Selection);
        SilkFrameCaptureResult expected = mode == "cpu-ocio"
            ? SilkFrameCapture.CaptureCoreOcio(
                reference, device, referenceRenderer, 32, 32, settings, retryProcessor!, 0, camera, request)
            : SilkFrameCapture.CaptureCore(
                reference, device, referenceRenderer, 32, 32, settings, 0, camera, request);
        await Assert.That(after.Rgba.Span.SequenceEqual(expected.Rgba.Span)).IsTrue();
        if (mode != "color")
        {
            await Assert.That(after.HdrColor!.Rgba16Float.Length).IsEqualTo(32 * 32 * 8);
            await Assert.That(after.Depth!.Values.Length).IsEqualTo(32 * 32);
            await Assert.That(after.HdrColor.Rgba16Float.Span.SequenceEqual(expected.HdrColor!.Rgba16Float.Span))
                .IsTrue();
            await Assert.That(after.Depth.Values.Span.SequenceEqual(expected.Depth!.Values.Span)).IsTrue();
            await Assert.That(renderer.SelectionOutlineDiagnostics.Status)
                .IsEqualTo(SilkSelectionOutlineStatus.Rendered);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task NativeSyncWaitsForRejectedFinalFramePublication(SilkGraphicsBackend backend)
    {
        using Fixture fixture = Create(Mesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(
            fixture.Plugins, source, new SilkPreparationLimits(1_000_000, 1_000_000));
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(32, 32));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(32, 32));
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0.5f, 0.5f, 3), new Vector3(0.5f, 0.5f, 0), Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        using OpenUsdSilkPage initial = session.Sync(32, 32, camera: camera);
        _ = renderer.ApplyAndRender(initial, color, depth);
        byte[] before = new byte[32 * 32 * 4];
        color.ReadbackForTesting(before);
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray("points",
            [new(0.5f, 0, 0), new(1.5f, 0, 0), new(1.5f, 1, 0), new(0.5f, 1, 0)]));
        using OpenUsdSilkPage update = session.Sync(32, 32, camera: camera);
        device.RefuseSubmissionAfter = 1;
        await Assert.That(() => renderer.ApplyAndRender(update, color, depth))
            .Throws<SilkGpuStagingBudgetExceededException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(initial.Revision);
        update.Dispose();
        await Assert.That(() => session.Sync(32, 32, camera: camera)).Throws<SilkGpuStagingBudgetExceededException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(initial.Revision);
        byte[] retained = new byte[before.Length];
        color.ReadbackForTesting(retained);
        await Assert.That(retained.SequenceEqual(before)).IsTrue();
        device.RefuseSubmissionAfter = null;
        using OpenUsdSilkPage next = session.Sync(32, 32, camera: camera);
        await Assert.That(next.Revision).IsEqualTo(update.Revision + 1);
        await Assert.That(MeshValues(next)).IsEqualTo(string.Empty);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(update.Revision);
        _ = renderer.ApplyAndRender(next, color, depth);
        color.ReadbackForTesting(retained);
        await Assert.That(retained.SequenceEqual(before)).IsFalse();
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + Mesh);
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task RejectedGpuPreparationReplaysRetirementsBeforeAnotherNativeSync(
        SilkGraphicsBackend backend, bool bufferBudget)
    {
        string geometry = Mesh + "\n" + Mesh.Replace("\"Mesh\"", "\"Removed\"", StringComparison.Ordinal);
        using Fixture fixture = Create(geometry);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(
            fixture.Plugins, source, new SilkPreparationLimits(1_000_000, 1_000_000));
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        if (bufferBudget)
        {
            budget.ConfigureDevice(device);
        }
        using var renderer = new SilkMeshRenderer(device);
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0.5f, 0.5f, 3), new Vector3(0.5f, 0.5f, 0), Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        using OpenUsdSilkPage initial = session.Sync(32, 32, camera: camera);
        renderer.ApplyPage(initial);
        byte[] before = SilkFrameCapture.CaptureRetained(
            renderer, device, 32, 32, RenderSettings.Default).Rgba.ToArray();
        using ISilkGraphicsBuffer? pressure = bufferBudget
            ? device.CreateBuffer(checked((nuint)(budget.MaximumBytes - budget.Usage.ReservedBytes - 1)),
                SilkBufferUsage.Upload) : null;
        ulong revision = renderer.Scene.Revision;
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray("points",
            [new(0.5f, 0, 0), new(1.5f, 0, 0), new(1.5f, 1, 0), new(0.5f, 1, 0)]));
        await scheduler.InvokeAsync(stage => stage.RemovePrim("/Removed"));
        using OpenUsdSilkPage update = session.Sync(32, 32, camera: camera);
        device.RefuseBufferAfter = bufferBudget ? null : 2;
        if (bufferBudget)
        {
            await Assert.That(() => renderer.ApplyPage(update)).Throws<SilkGpuBufferBudgetExceededException>();
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(budget.MaximumBytes - 1);
        }
        else
        {
            await Assert.That(() => renderer.ApplyPage(update)).Throws<InvalidOperationException>();
        }
        device.RefuseBufferAfter = null;
        await Assert.That(renderer.Scene.Revision).IsEqualTo(revision);
        byte[] afterRefusal = SilkFrameCapture.CaptureRetained(
            renderer, device, 32, 32, RenderSettings.Default).Rgba.ToArray();
        await Assert.That(afterRefusal.AsSpan().SequenceEqual(before)).IsTrue();
        update.Dispose();
        device.RefuseBufferAfter = bufferBudget ? null : 2;
        if (bufferBudget)
        {
            await Assert.That(() => session.Sync(32, 32, camera: camera))
                .Throws<SilkGpuBufferBudgetExceededException>();
        }
        else
        {
            await Assert.That(() => session.Sync(32, 32, camera: camera)).Throws<InvalidOperationException>();
        }
        await Assert.That(renderer.Scene.Revision).IsEqualTo(revision);
        device.RefuseBufferAfter = null;
        pressure?.Dispose();
        using OpenUsdSilkPage retry = session.Sync(32, 32, camera: camera);
        await Assert.That(retry.Revision).IsEqualTo(update.Revision + 1);
        await Assert.That(MeshValues(retry)).IsEqualTo(string.Empty);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(update.Revision);
        renderer.ApplyPage(retry);
        await Assert.That(renderer.Scene.MeshesByPath.ContainsKey(("/Removed", 0))).IsFalse();
        byte[] afterRetry = SilkFrameCapture.CaptureRetained(
            renderer, device, 32, 32, RenderSettings.Default).Rgba.ToArray();
        await Assert.That(afterRetry.AsSpan().SequenceEqual(before)).IsFalse();
        using OpenUsdSilkSession reference = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var referenceCapturer = new SilkFrameCapturer(device);
        SilkFrameCaptureResult expected = referenceCapturer.Capture(
            reference, 32, 32, RenderSettings.Default, camera: camera);
        await Assert.That(afterRetry.AsSpan().SequenceEqual(expected.Rgba.Span)).IsTrue();
        using OpenUsdSilkPage quiet = session.Sync(32, 32, camera: camera);
        await Assert.That(MeshValues(quiet)).IsEqualTo(string.Empty);
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + geometry);
    }

    [Test]
    public async Task OutOfOrderRejectedPagesRefuseFurtherNativeDeltas()
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = fixture.Open();
        using OpenUsdSilkPage first = session.Sync(16, 16);
        using OpenUsdSilkPage second = session.Sync(16, 16);
        await Assert.That(() => first.RegisterReplay(static () => { })).Throws<InvalidOperationException>();
        InvalidOperationException? error = await Assert.That(() => session.Sync(16, 16))
            .Throws<InvalidOperationException>();
        await Assert.That(error!.Message).Contains("Recreate the session");
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task DisposingARejectedPageConsumerCannotSilentlyDiscardItsNativeDelta(SilkGraphicsBackend backend)
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = fixture.Open();
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        using var renderer = new SilkMeshRenderer(device);
        using OpenUsdSilkPage page = session.Sync(16, 16);
        device.RefuseBufferAfter = 0;
        await Assert.That(() => renderer.ApplyPage(page)).Throws<InvalidOperationException>();
        renderer.Dispose();
        InvalidOperationException? failure = await Assert.That(() => session.Sync(16, 16))
            .Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).Contains("Recreate the session");
    }

    [Test]
    public async Task PageReplayRunsOutsideTheSessionLockAndStopsNativeProgressUntilSuccessful()
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = fixture.Open();
        using OpenUsdSilkPage first = session.Sync(16, 16);
        int attempts = 0;
        Action replay = () =>
        {
            if (!Task.Run(() => session.HasSynchronized).Wait(TimeSpan.FromSeconds(2)))
            {
                throw new TimeoutException("Page replay ran while the native-session lock was held.");
            }
            if (++attempts < 3)
            {
                throw new InvalidOperationException("Controlled page replay refusal.");
            }
        };
        first.RegisterReplay(replay);
        await Assert.That(() => session.Sync(16, 16)).Throws<InvalidOperationException>();
        await Assert.That(() => session.Sync(16, 16)).Throws<InvalidOperationException>();
        using OpenUsdSilkPage next = session.Sync(16, 16);
        await Assert.That(attempts).IsEqualTo(3);
        await Assert.That(next.Revision).IsEqualTo(first.Revision + 1);
        using OpenUsdSilkPage quiet = session.Sync(16, 16);
        await Assert.That(attempts).IsEqualTo(3);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SessionCeilingsBoundPathAndSharedStageLegacyAndExplicitCalls(bool limitPage, bool sharedStage)
    {
        using Fixture fixture = Create(Mesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        var limits = new SilkPreparationLimits(limitPage ? 1_000_000ul : 1ul, limitPage ? 1 : 1_000_000);
        using OpenUsdSilkSession session = sharedStage
            ? OpenUsdSilkRuntime.Create(fixture.Plugins, source, limits)
            : OpenUsdSilkRuntime.Create(fixture.Plugins, fixture.Path, limits);
        await Assert.That(session.PreparationLimits).IsSameReferenceAs(limits);
        await Assert.That(() => session.Sync(16, 16)).Throws<OpenUsdSilkException>();
        await Assert.That(() => session.Sync(16, 16, new SilkSceneIngestionOptions(RenderPurpose.Default, "full")))
            .Throws<OpenUsdSilkException>();
        await Assert.That(() => session.Sync(16, 16, PageOptions(1_000_000))).Throws<OpenUsdSilkException>();
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + Mesh);
    }

    [Test]
    public async Task LegacySessionPagesKeepExactUsageAndTighterRequestsRestoreToTheSessionCeiling()
    {
        using Fixture fixture = Create(Mesh);
        var limits = new SilkPreparationLimits(1_000_000, 1_000_000);
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, fixture.Path, limits);
        using OpenUsdSilkPage initial = session.Sync(16, 16);
        ulong reserved = initial.MeshPreparationUsage!.ReservedBytes;
        string original = MeshValues(initial);
        await Assert.That(reserved).IsGreaterThan(0ul);
        await Assert.That(initial.MeshPreparationUsage.MaximumReservedBytes).IsEqualTo(1_000_000ul);
        await Assert.That(() => session.Sync(16, 16, Options(reserved - 1))).Throws<OpenUsdSilkException>();
        using OpenUsdSilkPage exact = session.Sync(16, 16, Options(reserved));
        await Assert.That(exact.MeshPreparationUsage!.MaximumReservedBytes).IsEqualTo(reserved);
        await Assert.That(MeshValues(exact)).IsEqualTo(original);
        using OpenUsdSilkPage restored = session.Sync(16, 16);
        await Assert.That(restored.MeshPreparationUsage!.MaximumReservedBytes).IsEqualTo(1_000_000ul);
        await Assert.That(MeshValues(restored)).IsEqualTo(original);
        await Assert.That(initial.MeshPreparationUsage.ReservedBytes).IsEqualTo(reserved);
        await Assert.That(MeshValues(initial)).IsEqualTo(original);
        await Assert.That(() => session.Sync(16, 16, complexity: RenderComplexity.High))
            .Throws<NotSupportedException>();
        await Assert.That(() => session.Sync(16, 16, drawMode: RenderDrawMode.Wireframe))
            .Throws<NotSupportedException>();
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task ConfiguredSessionCapturesLegacyPixelsAndRecoversWithoutRelaxingCeilings(
        SilkGraphicsBackend backend)
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(
            fixture.Plugins, fixture.Path, new SilkPreparationLimits(4096, 100_000));
        using OpenUsdSilkSession reference = fixture.Open();
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var capturer = new SilkFrameCapturer(device);
        using var referenceCapturer = new SilkFrameCapturer(device);
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0.5f, 0.5f, 3), new Vector3(0.5f, 0.5f, 0), Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        SilkFrameCaptureResult expected = referenceCapturer.CaptureWithDepth(
            reference, 32, 32, RenderSettings.Default, camera: camera);
        SilkFrameCaptureResult initial = capturer.CaptureWithDepth(
            session, 32, 32, RenderSettings.Default, camera: camera);
        await Assert.That(initial.Rgba.Span.SequenceEqual(expected.Rgba.Span)).IsTrue();
        await Assert.That(() => capturer.CaptureWithDepth(
            session, 32, 32, RenderSettings.Default, PageOptions(1), camera: camera)).Throws<OpenUsdSilkException>();
        SilkFrameCaptureResult recovered = capturer.CaptureWithDepth(
            session, 32, 32, RenderSettings.Default, camera: camera);
        await Assert.That(recovered.Rgba.Span.SequenceEqual(expected.Rgba.Span)).IsTrue();
        RenderDiagnostic admitted = recovered.Diagnostics.Entries.Single(
            static entry => entry.Code == "HDSILK_PREPARATION_ADMISSION");
        await Assert.That(admitted.Message).Contains("limit=4096");
        await Assert.That(admitted.Message).Contains("ceiling=100000");
        await Assert.That(admitted.Severity).IsEqualTo(RenderDiagnosticSeverity.Information);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task CaptureRefusalRecoversIdenticalPixelsOnTheSameRetainedRenderer(
        SilkGraphicsBackend backend, bool limitPage)
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = fixture.Open();
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        using var capturer = new SilkFrameCapturer(device);
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0.5f, 0.5f, 3), new Vector3(0.5f, 0.5f, 0), Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        SilkFrameCaptureResult before = capturer.CaptureWithDepth(
            session, 32, 32, RenderSettings.Default, Options(4096), camera: camera);
        byte[] pixels = before.Rgba.ToArray();
        await Assert.That(() => capturer.CaptureWithDepth(
            session, 32, 32, RenderSettings.Default, limitPage ? PageOptions(1) : Options(1), camera: camera))
            .Throws<OpenUsdSilkException>();
        SilkFrameCaptureResult after = capturer.CaptureWithDepth(
            session, 32, 32, RenderSettings.Default, Options(4096), camera: camera);
        await Assert.That(after.Rgba.Span.SequenceEqual(pixels)).IsTrue();
        await Assert.That(before.Rgba.Span.SequenceEqual(pixels)).IsTrue();
        int colors = Enumerable.Range(0, pixels.Length / 4)
            .Select(index => Convert.ToHexString(pixels.AsSpan(index * 4, 3))).Distinct().Count();
        await Assert.That(colors).IsGreaterThan(1);
    }

    [Test]
    public async Task CommandPageBoundaryRefusesTheWholeUpdateAndPreservesExactRetryValues()
    {
        using Fixture fixture = Create(Mesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using OpenUsdSilkPage first = session.Sync(16, 16, PageOptions(1_000_000));
        int length = first.ByteLength;
        string before = MeshValues(first);
        await Assert.That(length).IsGreaterThan(1);
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray(
            "points", [new(0, 0, 2), new(1, 0, 2), new(1, 1, 2), new(0, 1, 2)]));
        OpenUsdSilkException? refusal = null;
        try
        {
            using OpenUsdSilkPage rejected = session.Sync(16, 16, PageOptions(length - 1));
        }
        catch (OpenUsdSilkException exception)
        {
            refusal = exception;
        }
        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!.Message).Contains("hdSilk command page refused");
        await Assert.That(MeshValues(first)).IsEqualTo(before);
        using OpenUsdSilkPage retry = session.Sync(16, 16, PageOptions(length));
        await Assert.That(retry.ByteLength).IsEqualTo(length);
        await Assert.That(MeshValues(retry)).IsNotEqualTo(before);
        using OpenUsdSilkSession referenceSession = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using OpenUsdSilkPage reference = referenceSession.Sync(16, 16, Options(1_000_000));
        await Assert.That(MeshValues(retry)).IsEqualTo(MeshValues(reference));
        using OpenUsdSilkPage quiet = session.Sync(16, 16, PageOptions(length + 1));
        await Assert.That(MeshValues(quiet)).IsEqualTo(string.Empty);
        await Assert.That(quiet.ByteLength).IsLessThan(length);
        await Assert.That(MeshValues(retry)).IsEqualTo(MeshValues(reference));
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + Mesh);
    }

    [Test]
    public async Task EvenAnEmptySceneMustFitItsCompleteFrameCommand()
    {
        using Fixture fixture = Create(string.Empty);
        using OpenUsdSilkSession session = fixture.Open();
        using OpenUsdSilkPage reference = session.Sync(16, 16, PageOptions(1_000_000));
        int length = reference.ByteLength;
        await Assert.That(() => session.Sync(16, 16, PageOptions(length - 1))).Throws<OpenUsdSilkException>();
        using OpenUsdSilkPage exact = session.Sync(16, 16, PageOptions(length));
        await Assert.That(exact.ByteLength).IsEqualTo(length);
        await Assert.That(exact.CommandCount).IsEqualTo(reference.CommandCount);
        await Assert.That(exact.MeshPreparationUsage!.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task VersionOneAndLegacySyncDoNotInheritTheOptionalPageCeiling()
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = fixture.Open();
        await Assert.That(() => session.Sync(16, 16, PageOptions(1))).Throws<OpenUsdSilkException>();
        using OpenUsdSilkPage versionOne = session.Sync(16, 16, Options(1_000_000));
        await Assert.That(MeshValues(versionOne).Length).IsGreaterThan(0);
        await Assert.That(versionOne.MeshPreparationUsage).IsNotNull();
        await Assert.That(() => session.Sync(16, 16, PageOptions(1))).Throws<OpenUsdSilkException>();
        using OpenUsdSilkPage legacy = session.Sync(16, 16);
        await Assert.That(legacy.MeshPreparationUsage).IsNull();
        await Assert.That(MeshValues(legacy)).IsEqualTo(MeshValues(versionOne));
    }

    [Test]
    public async Task EmptyGeometryAndFullWidthLimitsKeepExactAccounting()
    {
        using Fixture empty = Create(string.Empty);
        using OpenUsdSilkSession emptySession = empty.Open();
        using OpenUsdSilkPage emptyPage = emptySession.Sync(16, 16, Options(1));
        await Assert.That(emptyPage.MeshPreparationUsage!.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(emptyPage.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(0ul);
        using Fixture mesh = Create(Mesh);
        using OpenUsdSilkSession meshSession = mesh.Open();
        using OpenUsdSilkPage meshPage = meshSession.Sync(16, 16, Options(ulong.MaxValue));
        await Assert.That(meshPage.MeshPreparationUsage!.MaximumReservedBytes).IsEqualTo(ulong.MaxValue);
        await Assert.That(meshPage.MeshPreparationUsage.ReservedBytes).IsGreaterThan(0ul);
    }

    [Test]
    public async Task ExactReservationBoundaryRejectsOneByteLessBeforePublishing()
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = fixture.Open();
        using OpenUsdSilkPage legacy = session.Sync(16, 16,
            new SilkSceneIngestionOptions(RenderPurpose.Default | RenderPurpose.Render | RenderPurpose.Proxy, "full"));
        using OpenUsdSilkPage first = session.Sync(16, 16, Options(1_000_000));
        SilkMeshPreparationUsage usage = first.MeshPreparationUsage ??
            throw new InvalidOperationException("The native request omitted its reservation accounting.");
        await Assert.That(usage.ReservedBytes).IsGreaterThan(0ul);
        await Assert.That(usage.PeakReservedBytes).IsEqualTo(usage.ReservedBytes);
        string original = MeshValues(first);
        await Assert.That(original).IsEqualTo(MeshValues(legacy));
        await Assert.That(() => session.Sync(16, 16, Options(usage.ReservedBytes - 1)))
            .Throws<OpenUsdSilkException>();
        using OpenUsdSilkPage retry = session.Sync(16, 16, Options(usage.ReservedBytes));
        await Assert.That(retry.MeshPreparationUsage!.ReservedBytes).IsEqualTo(usage.ReservedBytes);
        await Assert.That(MeshValues(retry)).IsEqualTo(original);
        await Assert.That(MeshValues(first)).IsEqualTo(original);
        using OpenUsdSilkPage quiet = session.Sync(16, 16, Options(usage.ReservedBytes));
        await Assert.That(MeshValues(quiet)).IsEqualTo(string.Empty);
        await Assert.That(quiet.MeshPreparationUsage!.ReservedBytes).IsEqualTo(usage.ReservedBytes);
    }

    [Test]
    public async Task FailedGrowthPreservesTheOldPageAndReleasesReservationsForRetry()
    {
        using Fixture fixture = Create(Mesh);
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using OpenUsdSilkPage initial = session.Sync(16, 16, Options(4096));
        string original = MeshValues(initial);
        ulong reserved = initial.MeshPreparationUsage!.ReservedBytes;
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray(
            "points", Enumerable.Repeat(new UsdVec3f(2, 3, 4), 1024).ToArray()));
        await Assert.That(() => session.Sync(16, 16, Options(4096))).Throws<OpenUsdSilkException>();
        await Assert.That(MeshValues(initial)).IsEqualTo(original);
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Mesh").SetVec3fArray(
            "points", [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)]));
        using OpenUsdSilkPage retry = session.Sync(16, 16, Options(4096));
        await Assert.That(MeshValues(retry)).IsEqualTo(original);
        await Assert.That(retry.MeshPreparationUsage!.ReservedBytes).IsEqualTo(reserved);
        await Assert.That(retry.MeshPreparationUsage.PeakReservedBytes).IsLessThanOrEqualTo(4096ul);
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo("#usda 1.0\n" + Mesh);
    }

    [Test]
    public async Task IndependentMeshesAggregateAndInstanceReferencesShareOneLease()
    {
        using Fixture single = Create(Mesh);
        using OpenUsdSilkSession one = single.Open();
        using OpenUsdSilkPage onePage = one.Sync(16, 16, Options(1_000_000));
        ulong cost = onePage.MeshPreparationUsage!.ReservedBytes;
        using Fixture pair = Create(Mesh + "\n" + Mesh.Replace("\"Mesh\"", "\"Other\"", StringComparison.Ordinal));
        using OpenUsdSilkSession two = pair.Open();
        await Assert.That(() => two.Sync(16, 16, Options(cost * 2 - 1))).Throws<OpenUsdSilkException>();
        using OpenUsdSilkPage twoPage = two.Sync(16, 16, Options(cost * 2));
        await Assert.That(twoPage.MeshPreparationUsage!.ReservedBytes).IsEqualTo(cost * 2);

        using Fixture instances = Create("""
            def PointInstancer "Instances" {
                rel prototypes = </Instances/Mesh>
                int[] protoIndices = [0,0,0]
                point3f[] positions = [(0,0,0),(2,0,0),(4,0,0)]
            """ + "\n" + Mesh + "\n}");
        using OpenUsdSilkSession instanced = instances.Open();
        using OpenUsdSilkPage instancePage = instanced.Sync(16, 16, Options(cost));
        await Assert.That(instancePage.MeshPreparationUsage!.ReservedBytes).IsEqualTo(cost);
        int references = 0;
        using (SilkCommandEnumerator commands = instancePage.GetEnumerator())
        {
            while (commands.MoveNext())
            {
                if (commands.Current.Type == SilkCommandType.MeshUpsert &&
                    commands.Current.AsMeshUpsert().PointCount == 0)
                {
                    references++;
                }
            }
        }
        await Assert.That(references).IsEqualTo(2);
    }

    [Test]
    public async Task LegacySyncRestoresItsUnboundedProfileAfterABudgetRefusal()
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = fixture.Open();
        await Assert.That(() => session.Sync(16, 16, Options(1))).Throws<OpenUsdSilkException>();
        using OpenUsdSilkPage legacy = session.Sync(16, 16);
        await Assert.That(legacy.MeshPreparationUsage).IsNull();
        await Assert.That(MeshValues(legacy).Length).IsGreaterThan(0);
        using OpenUsdSilkPage bounded = session.Sync(16, 16, Options(4096));
        await Assert.That(bounded.MeshPreparationUsage!.ReservedBytes).IsGreaterThan(0ul);
    }

    [Test]
    [Arguments("Points")]
    [Arguments("BasisCurves")]
    [Arguments("Volume")]
    [Arguments("SkelRoot")]
    public async Task UnaccountedGeometryProfilesAreRefusedInsteadOfOmitted(string type)
    {
        string geometry = type switch
        {
            "Points" => "def Points \"Other\" {\npoint3f[] points = [(0,0,0)]\n}",
            "BasisCurves" => """
                def BasisCurves "Other" {
                    point3f[] points = [(0,0,0),(1,0,0)]
                    int[] curveVertexCounts = [2]
                    uniform token type = "linear"
                }
                """,
            _ => $"def {type} \"Other\" {{}}"
        };
        using Fixture fixture = Create(Mesh + "\n" + geometry);
        using OpenUsdSilkSession session = fixture.Open();
        await Assert.That(() => session.Sync(16, 16, Options(4096))).Throws<OpenUsdSilkException>();
    }

    [Test]
    public async Task RefinementAndAlternateDrawModesCannotBypassThePreparationProfile()
    {
        using Fixture fixture = Create(Mesh);
        using OpenUsdSilkSession session = fixture.Open();
        await Assert.That(() => session.Sync(16, 16, Options(4096), complexity: RenderComplexity.Medium))
            .Throws<NotSupportedException>();
        await Assert.That(() => session.Sync(16, 16, Options(4096), drawMode: RenderDrawMode.Wireframe))
            .Throws<NotSupportedException>();
        using OpenUsdSilkPage accepted = session.Sync(16, 16, Options(4096));
        await Assert.That(accepted.MeshPreparationUsage!.ReservedBytes).IsGreaterThan(0ul);
    }

    private static SilkSceneIngestionOptions Options(ulong maximum) =>
        new(RenderPurpose.Default | RenderPurpose.Render | RenderPurpose.Proxy, "full", maximum);

    private static SilkSceneIngestionOptions PageOptions(int maximumPageBytes) =>
        new(RenderPurpose.Default | RenderPurpose.Render | RenderPurpose.Proxy, "full", 1_000_000, maximumPageBytes);

    private static string MeshValues(OpenUsdSilkPage page)
    {
        var values = new List<string>();
        using SilkCommandEnumerator commands = page.GetEnumerator();
        while (commands.MoveNext())
        {
            if (commands.Current.Type != SilkCommandType.MeshUpsert)
            {
                continue;
            }
            SilkMeshUpsertCommand mesh = commands.Current.AsMeshUpsert();
            values.Add(mesh.Path);
            for (int point = 0; point < mesh.PointCount; point++)
            {
                for (int component = 0; component < 3; component++)
                {
                    values.Add(BitConverter.SingleToInt32Bits(mesh.GetPointComponent(point, component)).ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                }
                values.Add(mesh.GetPointOrigin(point).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            for (int index = 0; index < mesh.IndexCount; index++)
            {
                values.Add(mesh.GetIndex(index).ToString(System.Globalization.CultureInfo.InvariantCulture));
                values.Add(mesh.GetCornerEdge(index).ToString(System.Globalization.CultureInfo.InvariantCulture));
                values.Add(mesh.GetTriangleSubprim(index / 3).ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            }
            for (int index = 0; index < mesh.AttributeCount; index++)
            {
                SilkMeshAttributeEntry attribute = mesh.GetAttribute(index);
                values.Add(attribute.Name);
                for (int element = 0; element < attribute.ElementCount; element++)
                {
                    for (int component = 0; component < attribute.ComponentCount; component++)
                    {
                        values.Add(BitConverter.SingleToInt32Bits(attribute.GetComponent(element, component))
                            .ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
            }
        }
        return string.Join(",", values);
    }

    private static Fixture Create(string geometry)
    {
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrEmpty(plugins))
        {
            if (Environment.GetEnvironmentVariable("OPENUSD_MESH_PREPARATION_REQUIRED") == "1")
            {
                throw new InvalidOperationException(
                    "The native mesh preparation profile requires matched plugin inputs.");
            }
            Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH to a runtime with the mesh preparation extension.");
        }
        string root = Directory.CreateTempSubdirectory("silk-mesh-preparation-").FullName;
        string path = System.IO.Path.Combine(root, "scene.usda");
        File.WriteAllText(path, "#usda 1.0\n" + geometry);
        return new Fixture(root, path, plugins);
    }

    private sealed class Fixture(string root, string path, string plugins) : IDisposable
    {
        internal string Path { get; } = path;
        internal string Plugins { get; } = plugins;
        internal OpenUsdSilkSession Open() => OpenUsdSilkRuntime.Create(Plugins, Path);
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
