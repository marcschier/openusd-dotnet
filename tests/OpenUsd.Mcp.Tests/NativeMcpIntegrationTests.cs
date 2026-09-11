// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using Microsoft.Extensions.DependencyInjection;

namespace OpenUsd.Mcp.Tests;

public sealed partial class NativeMcpIntegrationTests
{
    [Test]
    [NotInParallel]
    [Arguments(false, "raw")]
    [Arguments(true, "raw")]
    [Arguments(false, "exr")]
    [Arguments(true, "exr")]
    public async Task NativeSequenceRendersDistinctTimesAndExposesTheChosenImmutableFrame(
        bool includeDepth, string hdrFormat)
    {
        if (hdrFormat == "exr" && !OpenUsd.Rendering.ExrRgba16FloatWriter.IsSupported)
        {
            Skip.Test("EXR output currently requires Windows x64.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        NativeLayout layout = RequireNativeLayout(requireImaging: true);
        using var files = new WorkspaceTestFiles();
        const string scene = """
            #usda 1.0
            (metersPerUnit = 1)
            def Camera "Camera" {
                token projection = "orthographic"
                float horizontalAperture = 20
                float verticalAperture = 20
                float2 clippingRange = (1, 10)
            }
            def Xform "Mover" {
                double3 xformOp:translate.timeSamples = {0: (-0.5, 0, -4), 1: (0, 0, -4), 2: (0.5, 0, -4)}
                uniform token[] xformOpOrder = ["xformOp:translate"]
                def Mesh "Quad" (prepend apiSchemas = ["MaterialBindingAPI"]) {
                    uniform token subdivisionScheme = "none"
                    uniform bool doubleSided = true
                    point3f[] points = [(-0.2,-0.3,0), (0.2,-0.3,0), (0.2,0.3,0), (-0.2,0.3,0)]
                    int[] faceVertexCounts = [4]
                    int[] faceVertexIndices = [0,1,2,3]
                    rel material:binding = </Material>
                }
            }
            def Material "Material" {
                token outputs:surface.connect = </Material/Shader.outputs:surface>
                def Shader "Shader" {
                    uniform token info:id = "UsdPreviewSurface"
                    color3f inputs:diffuseColor = (0,0,0)
                    color3f inputs:emissiveColor = (64,0,0)
                    int inputs:useSpecularWorkflow = 1
                    color3f inputs:specularColor = (0,0,0)
                    token outputs:surface
                }
            }
            """;
        await File.WriteAllTextAsync(files.SourcePath, scene);
        string plugins = Path.Combine(layout.ShimRoot, "plugin", "usd");
        var options = new OpenUsdMcpApplicationOptions(
            files.SourceRoot, files.OutputRoot, plugins, files.OutputRoot,
            Path.Combine(files.OutputRoot, "viewer-not-launched.exe"));
        await using ServiceProvider services = new ServiceCollection().AddOpenUsdMcpServices(options)
            .BuildServiceProvider();
        IOpenUsdMcpService service = services.GetRequiredService<IOpenUsdMcpService>();
        McpSessionDto session = await service.OpenSceneAsync(
            new OpenSceneRequest { SourcePath = "scene.usda" }, default);
        McpRenderSequenceResultDto job = await service.RenderSequenceAsync(new RenderSequenceRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision,
            CameraPath = "/Camera",
            Width = 64,
            Height = 64,
            StartTimeCode = 0,
            TimeStep = 1,
            FrameCount = 3,
            IncludeDeviceDepth = includeDepth,
            IncludeHdrColor = true,
            HdrColorFormat = hdrFormat
        }, default);
        IArtifactResourceStore resources = services.GetRequiredService<IArtifactResourceStore>();
        var centers = new double[3];
        for (int index = 0; index < 3; index++)
        {
            McpSequenceFrameResultDto frame = await service.ReadSequenceFrameAsync(new ReadSequenceFrameRequest
            {
                JobId = job.JobId,
                FrameIndex = index
            }, default);
            ArtifactResourceContent? encoded = await resources.ReadAsync(new Uri(frame.Artifact.Uri));
            ImageRgba8 pixels = PngRgba8Decoder.Decode(encoded!.Content.Span);
            (double center, int coverage) = RedCenter(pixels);
            await Assert.That(coverage).IsGreaterThan(100);
            await Assert.That(frame.TimeCode).IsEqualTo((double)index);
            centers[index] = center;
            if (includeDepth)
            {
                await Assert.That(frame.DeviceDepth).IsNotNull();
                ArtifactResourceContent? depth = await resources.ReadAsync(new Uri(frame.DeviceDepth!.Uri));
                await Assert.That(depth!.Content.Length).IsEqualTo(64 * 64 * sizeof(float));
                float surfaceDepth = BinaryPrimitives.ReadSingleLittleEndian(
                    depth.Content.Span.Slice(((32 * 64) + (int)Math.Round(center)) * sizeof(float), sizeof(float)));
                await Assert.That(Math.Abs(surfaceDepth - (1f / 3f))).IsLessThan(0.00001f);
                await Assert.That(BinaryPrimitives.ReadSingleLittleEndian(depth.Content.Span)).IsEqualTo(1f);
            }
            else
            {
                await Assert.That(frame.DeviceDepth).IsNull();
            }
            await Assert.That(frame.HdrColor).IsNotNull();
            ArtifactResourceContent? hdr = await resources.ReadAsync(new Uri(frame.HdrColor!.Uri));
            byte[] half = hdrFormat == "exr"
                ? ExrScanlineOracle.Read(hdr!.Content.ToArray(), 64, 64) : hdr!.Content.ToArray();
            await Assert.That(half.Length).IsEqualTo(64 * 64 * 8);
            await Assert.That(frame.HdrColorFormat).IsEqualTo(hdrFormat);
            await Assert.That(Convert.ToHexString(
                half.AsSpan(((32 * 64) + (int)Math.Round(center)) * 8, 8)))
                .IsEqualTo("005400000000003C");
        }
        await Assert.That(centers[1] - centers[0]).IsGreaterThan(12d);
        await Assert.That(centers[2] - centers[1]).IsGreaterThan(12d);
        await Assert.That(await File.ReadAllTextAsync(files.SourcePath)).IsEqualTo(scene);
        await service.CloseSceneAsync(new SceneRevisionRequest
        {
            SessionId = session.SessionId,
            Generation = session.Generation,
            StageRevision = session.StageRevision
        }, default);
    }

    private static (double Center, int Coverage) RedCenter(ImageRgba8 image)
    {
        long sum = 0;
        int count = 0;
        ReadOnlySpan<byte> pixels = image.Pixels.Span;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] > pixels[offset + 1] + 20 &&
                pixels[offset] > pixels[offset + 2] + 20 && pixels[offset + 3] >= 240)
            {
                sum += (offset / 4) % image.Width;
                count++;
            }
        }
        return (count == 0 ? double.NaN : (double)sum / count, count);
    }

    [Test]
    public async Task CallerDisposedRenderSourceIsRemovedFromBackendTracking()
    {
        _ = RequireNativeLayout(requireImaging: false);
        using var files = new WorkspaceTestFiles();
        UsdStageScheduler scheduler = UsdStageScheduler.Create(files.SourcePath);
        var backend = new OpenUsdWorkspaceSessionBackend(
            new WorkspaceSessionBackendContext(
                "render-source-tracking",
                files.SourcePath,
                files.OutputRoot,
                files.SourcePath),
            scheduler);
        UsdStageRenderSource source;
        try
        {
            source = await backend.AcquireRenderSourceAsync();
        }
        catch (Exception exception) when (IsUnavailableNativeRuntime(exception))
        {
            await backend.DisposeAsync();
            Skip.Test($"OpenUSD Core native runtime is unavailable: {exception.Message}");
            throw;
        }

        await Assert.That(backend.TrackedRenderSourceCount).IsEqualTo(1);
        source.Dispose();
        await Assert.That(backend.TrackedRenderSourceCount).IsEqualTo(0);

        await backend.DisposeAsync();
    }

    [Test]
    public async Task NativeOverlayCheckpointAndRollbackRoundTrip()
    {
        _ = RequireNativeLayout(requireImaging: false);
        using var files = new WorkspaceTestFiles();
        await using var workspace = new McpSessionWorkspace(
            new McpSessionWorkspaceOptions(files.SourceRoot, files.OutputRoot));
        WorkspaceSessionInfo session;
        try
        {
            session = await workspace.StartAsync("scene.usda");
        }
        catch (Exception exception) when (IsUnavailableNativeRuntime(exception))
        {
            Skip.Test($"OpenUSD Core native runtime is unavailable: {exception.Message}");
            throw;
        }

        WorkspaceEditResult first = await workspace.EditAsync(
            Revision(session),
            new WorkspaceEditBatch([new DefinePrimWorkspaceEdit("/World", "Xform")]));
        WorkspaceCheckpointResult checkpoint = await workspace.CheckpointAsync(
            new WorkspaceSessionRevision(
                first.SessionId,
                first.Generation,
                first.StageRevision));
        WorkspaceEditResult second = await workspace.EditAsync(
            new WorkspaceSessionRevision(
                checkpoint.SessionId,
                checkpoint.Generation,
                checkpoint.StageRevision),
            new WorkspaceEditBatch([new DefinePrimWorkspaceEdit("/World/Temporary", "Xform")]));
        WorkspaceRollbackResult rollback = await workspace.RollbackAsync(
            new WorkspaceSessionRevision(
                second.SessionId,
                second.Generation,
                second.StageRevision),
            checkpoint.Checkpoint.CheckpointId);
        WorkspaceSceneStatistics statistics = await workspace.InspectSceneAsync(
            new WorkspaceSessionRevision(
                rollback.SessionId,
                rollback.Generation,
                rollback.StageRevision));

        await Assert.That(statistics.PrimCount).IsEqualTo(1);
        await Assert.That(statistics.RootPrimCount).IsEqualTo(1);
        await Assert.That(statistics.LeafPrimCount).IsEqualTo(1);
        await Assert.That(rollback.Generation).IsEqualTo(second.Generation + 1);
    }

    [Test]
    public async Task NativeSessionRejectsAnUncomposableSourceLayer()
    {
        _ = RequireNativeLayout(requireImaging: false);
        using var files = new WorkspaceTestFiles();
        await File.WriteAllTextAsync(
            files.SourcePath,
            "#usda 1.0\ndef Xform \"Broken\" { double value = (1, 2) }");
        await using var workspace = new McpSessionWorkspace(
            new McpSessionWorkspaceOptions(files.SourceRoot, files.OutputRoot));

        try
        {
            WorkspaceSourceCompositionException exception = await Assert.That(
                    async () => await workspace.StartAsync("scene.usda"))
                .Throws<WorkspaceSourceCompositionException>() ??
                throw new InvalidOperationException("Expected invalid source rejection.");
            await Assert.That(exception.Message).Contains(
                "source layer could not be composed");
        }
        catch (Exception exception) when (IsUnavailableNativeRuntime(exception))
        {
            Skip.Test($"OpenUSD Core native runtime is unavailable: {exception.Message}");
        }
    }

    [Test]
    public async Task NativeRetainedPreviewProducesDecodablePng()
    {
        NativeLayout layout = RequireNativeLayout(requireImaging: true);
        using var files = new WorkspaceTestFiles();
        string pluginPath = Path.Combine(files.OutputRoot, "native-plugins");
        CopyTree(Path.Combine(layout.NativeRoot, "lib", "usd"), pluginPath);
        CopyTree(Path.Combine(layout.NativeRoot, "plugin", "usd"), pluginPath);
        CopyTree(Path.Combine(layout.ShimRoot, "plugin", "usd"), pluginPath);
        string silkMetadata = Path.Combine(
            pluginPath,
            "hdSilk",
            "resources",
            "plugInfo.json");
        if (!File.Exists(silkMetadata))
        {
            Skip.Test(
                $"OpenUSD Imaging native runtime is unavailable: missing '{silkMetadata}'.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        await using var workspace = new McpSessionWorkspace(
            new McpSessionWorkspaceOptions(files.SourceRoot, files.OutputRoot));
        WorkspaceSessionInfo session;
        try
        {
            session = await workspace.StartAsync("scene.usda");
        }
        catch (Exception exception) when (IsUnavailableNativeRuntime(exception))
        {
            Skip.Test($"OpenUSD Core native runtime is unavailable: {exception.Message}");
            throw;
        }

        var artifacts = new ArtifactResourceStore();
        try
        {
            using var processor = new PreviewCaptureProcessor(
                new PreviewSilkFrameSourceFactory(
                    pluginPath,
                    workspace,
                    new PreviewGraphicsDeviceFactory(),
                    new PreviewGraphicsDeviceOptions()),
                artifacts);
            PreviewCaptureResult result = processor.Process(
                new PreviewCaptureRequest("native", 4, 4));
            ArtifactResourceContent? png = await artifacts.ReadAsync(
                result.Artifacts.Single().ResourceUri);
            ImageRgba8 decoded = PngRgba8Decoder.Decode(png!.Content.Span);

            await Assert.That(png).IsNotNull();
            await Assert.That(decoded.Width).IsEqualTo(4);
            await Assert.That(decoded.Height).IsEqualTo(4);
        }
        catch (Exception exception) when (IsUnavailableNativeRuntime(exception))
        {
            Skip.Test($"OpenUSD Imaging native runtime is unavailable: {exception.Message}");
            throw;
        }

        await workspace.CloseAsync(Revision(session));
    }

    [Test]
    public async Task NativePreviewCamerasUseAuthoredCameraAndDistinctOrbitViews()
    {
        _ = RequireNativeLayout(requireImaging: false);
        using var files = new WorkspaceTestFiles();
        await File.WriteAllTextAsync(
            files.SourcePath,
            """
            #usda 1.0
            def Xform "World"
            {
                def Cube "Subject"
                {
                    double size = 2
                }
                def Camera "ShotCamera"
                {
                    float focalLength = 35
                    double3 xformOp:translate = (0, 1, 6)
                    uniform token[] xformOpOrder = ["xformOp:translate"]
                }
            }
            """);
        await using var workspace = new McpSessionWorkspace(
            new McpSessionWorkspaceOptions(files.SourceRoot, files.OutputRoot));
        WorkspaceSessionInfo session;
        try
        {
            session = await workspace.StartAsync("scene.usda");
        }
        catch (Exception exception) when (IsUnavailableNativeRuntime(exception))
        {
            Skip.Test($"OpenUSD Core native runtime is unavailable: {exception.Message}");
            throw;
        }

        IReadOnlyList<OpenUsd.Rendering.CameraState> authored =
            await workspace.CreatePreviewCamerasAsync(
                Revision(session),
                "/World/ShotCamera",
                orbit: false,
                640,
                480,
                [0],
                default);
        IReadOnlyList<OpenUsd.Rendering.CameraState> orbit =
            await workspace.CreatePreviewCamerasAsync(
                Revision(session),
                cameraPath: null,
                orbit: true,
                640,
                480,
                [0, 0, 0, 0],
                default);

        await Assert.That(authored.Single().Mode)
            .IsEqualTo(OpenUsd.Rendering.CameraMode.Matrices);
        await Assert.That(orbit.Select(static camera => camera.View).Distinct().Count())
            .IsEqualTo(4);
    }

    private static NativeLayout RequireNativeLayout(bool requireImaging)
    {
        string rid = RuntimeRid();
        string root = FindRepositoryRoot();
        string nativeRoot = Environment.GetEnvironmentVariable("OPENUSD_MCP_TEST_NATIVE_ROOT") ??
            Path.Combine(root, "native", "install", rid);
        string shimRoot = Environment.GetEnvironmentVariable("OPENUSD_MCP_TEST_SHIM_ROOT") ??
            Path.Combine(root, "native", "install", "shim", rid);
        if (!Path.IsPathFullyQualified(nativeRoot) || !Path.IsPathFullyQualified(shimRoot))
        {
            throw new ArgumentException("Native MCP test roots must be absolute paths.");
        }
        string? missing = new[] { nativeRoot, shimRoot }
            .FirstOrDefault(path => !Directory.Exists(path));
        if (missing is not null)
        {
            string capability = requireImaging ? "Core/Imaging" : "Core";
            Skip.Test(
                $"Repository {capability} native runtime is unavailable: missing '{missing}'.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        return new NativeLayout(nativeRoot, shimRoot);
    }

    private static string RuntimeRid()
    {
        if (OperatingSystem.IsWindows() &&
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
            System.Runtime.InteropServices.Architecture.X64)
        {
            return "win-x64";
        }
        if (OperatingSystem.IsLinux() &&
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
            System.Runtime.InteropServices.Architecture.X64)
        {
            return "linux-x64";
        }
        if (OperatingSystem.IsMacOS() &&
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
            System.Runtime.InteropServices.Architecture.Arm64)
        {
            return "osx-arm64";
        }

        Skip.Test("Native MCP integration is unsupported on this host architecture.");
        throw new InvalidOperationException("Skip.Test returned unexpectedly.");
    }

    private static bool IsUnavailableNativeRuntime(Exception exception) =>
        exception is DllNotFoundException or
        BadImageFormatException or
        EntryPointNotFoundException;

    private static WorkspaceSessionRevision Revision(WorkspaceSessionInfo session) =>
        new(session.SessionId, session.Generation, session.StageRevision);

    private static void CopyTree(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(
                destination,
                Path.GetRelativePath(source, directory)));
        }
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            // Only fixture copies are writable; frozen native inputs keep their original attributes.
            File.SetAttributes(target, File.GetAttributes(target) & ~FileAttributes.ReadOnly);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }

    private sealed record NativeLayout(string NativeRoot, string ShimRoot);
}
