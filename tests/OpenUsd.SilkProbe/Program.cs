// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using OpenUsd.Geom;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Metal;
using OpenUsd.Rendering.Silk.Vulkan;
using SharpMetal.Metal;

namespace OpenUsd.SilkProbe;

internal static partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length >= 1 &&
            string.Equals(args[0], "--shared-stage-soak", StringComparison.Ordinal))
        {
            return await RunSharedStageSoakAsync(args).ConfigureAwait(false);
        }
        if (args.Length >= 1 &&
            string.Equals(args[0], "--metal-composition", StringComparison.Ordinal))
        {
            return await RunMetalCompositionAsync(args).ConfigureAwait(false);
        }
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: OpenUsd.SilkProbe <plugin-path> <stage-path>\n" +
                "   or: OpenUsd.SilkProbe --shared-stage-soak " +
                "<plugin-path> <stage-path> <artifact-path>\n" +
                "   or: OpenUsd.SilkProbe --metal-composition " +
                "<plugin-path> <stage-path> <artifact-path>");
            return 2;
        }

        try
        {
            ValidateCheckedPickAssets();
            const int width = 128;
            const int height = 96;
            using ISilkGraphicsDevice device = CreateGraphicsDevice();
            using var sceneResources = new SilkSceneGpuResources(device);
            var scene = new SilkSceneState();
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(args[0], args[1]);
            using OpenUsdSilkPage first = session.Sync(
                width,
                height,
                camera: CameraState.Default);
            (int frames, int upserts, int removals) = CountCommands(first);
            SilkSceneDelta firstDelta = scene.Apply(first);
            sceneResources.Apply(scene, firstDelta);
            Console.WriteLine(
                $"First page: revision={first.Revision}, commands={first.CommandCount}, " +
                $"frames={frames}, upserts={upserts}, removals={removals}");
            if (first.AbiVersion != SilkCommandParser.PageAbiVersion ||
                frames != 1 ||
                upserts < 1 ||
                sceneResources.Meshes.Count != upserts ||
                scene.MeshesByPath.Values.Any(
                    mesh => mesh.InstanceId != 0 ||
                        mesh.InstanceIndex != 0 ||
                        mesh.TopologyRevision == 0 ||
                        mesh.TriangleCount != mesh.Indices.Length / 3) ||
                !ValidatePickIdentityTable(scene))
            {
                Console.Error.WriteLine(
                    "Initial page did not contain and upload frame and mesh data.");
                return 3;
            }

            using OpenUsdSilkPage second = session.Sync(
                width,
                height,
                camera: CameraState.Default);
            (frames, upserts, removals) = CountCommands(second);
            SilkSceneDelta secondDelta = scene.Apply(second);
            sceneResources.Apply(scene, secondDelta);
            device.WaitIdle();
            Console.WriteLine(
                $"Steady page: revision={second.Revision}, commands={second.CommandCount}, " +
                $"frames={frames}, upserts={upserts}, removals={removals}");
            if (frames != 1 || upserts != 0 || removals != 0)
            {
                Console.Error.WriteLine("Steady-state page repeated unchanged mesh data.");
                return 4;
            }

            var renderEvidence = new List<string>
            {
                RenderPage(session, first, device, width, height)
            };
            if (OperatingSystem.IsWindows())
            {
                using VulkanSilkGraphicsDevice vulkan = VulkanSilkGraphicsDevice.Create();
                renderEvidence.Add(RenderPage(session, first, vulkan, width, height));
            }
            Console.WriteLine($"Offscreen render: {string.Join("; ", renderEvidence)}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void ValidateCheckedPickAssets()
    {
        _ = SilkCheckedShaderAssets.PickParameters;
        _ = SilkCheckedShaderAssets.LoadPickVertex(
            SilkShaderBinaryFormat.Dxil);
        _ = SilkCheckedShaderAssets.LoadPickFragment(
            SilkShaderBinaryFormat.Dxil);
        _ = SilkCheckedShaderAssets.LoadPickVertex(
            SilkShaderBinaryFormat.SpirV);
        _ = SilkCheckedShaderAssets.LoadPickFragment(
            SilkShaderBinaryFormat.SpirV);
    }

    private static async Task<int> RunSharedStageSoakAsync(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine(
                "Usage: OpenUsd.SilkProbe --shared-stage-soak " +
                "<plugin-path> <stage-path> <artifact-path>");
            return 2;
        }

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        string artifactPath = Path.GetFullPath(args[3]);
        UsdStageScheduler? scheduler = null;
        UsdStageRenderSource? source = null;
        try
        {
            string stagePath = Path.GetFullPath(args[2]);
            string assetPath = Path.Combine(
                Path.GetDirectoryName(stagePath)!,
                "shared-stage-soak-asset.usda");
            SharedStageSoak.ResetDiagnosticPeaks();
            SharedStageResourceSnapshot baseline =
                SharedStageSoak.CaptureResources(null, null);
            scheduler = UsdStageScheduler.Open(
                stagePath,
                capacity: 1024,
                notificationCapacity: 8);
            source = await scheduler.AcquireRenderSourceAsync().ConfigureAwait(false);
            SharedStageSoakResult result = await SharedStageSoak.RunAsync(
                args[1],
                scheduler,
                source,
                new SharedStageSoakOptions
                {
                    AssetPath = assetPath,
                    BuildIdentity = SharedStageBuildIdentity.FromEnvironment(),
                    BaselineResources = baseline,
                    CreateGraphicsDevice = CreateGraphicsDevice,
                    ReportStatus = Console.WriteLine
                }).ConfigureAwait(false);
            source.Dispose();
            source = null;
            await scheduler.DisposeAsync().ConfigureAwait(false);
            scheduler = null;
            result = result.WithResourcesReleased(
                SharedStageSoak.CaptureResources(null, null),
                default);
            SharedStageSoak.WriteArtifact(artifactPath, result);
            Console.WriteLine(
                $"Shared-stage soak passed: edits={result.MutatingOperations}, " +
                $"reads={result.ReadOperations}, " +
                $"serial={result.InitialChangeSerial}->{result.FinalChangeSerial}, " +
                $"syncs={result.SilkSyncPages}, upserts={result.SilkMeshUpserts}, " +
                $"removals={result.SilkMeshRemovals}, " +
                $"workingSetDelta={result.FinalWorkingSetBytes - result.WarmWorkingSetBytes}");
            return 0;
        }
        catch (Exception exception)
        {
            SharedStageSoak.WriteFailureArtifact(artifactPath, startedAt, exception);
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            source?.Dispose();
            if (scheduler is not null)
            {
                try
                {
                    await scheduler.DisposeAsync().ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }

    private static async Task<int> RunMetalCompositionAsync(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine(
                "Usage: OpenUsd.SilkProbe --metal-composition " +
                "<plugin-path> <stage-path> <artifact-path>");
            return 2;
        }
        if (!OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("The Metal composition probe requires macOS.");
            return 2;
        }

        string artifactPath = Path.GetFullPath(args[3]);
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        UsdStageScheduler? scheduler = null;
        UsdStageRenderSource? source = null;
        OpenUsdSilkSession? session = null;
        MetalCompositionViewportPresenter? presenter = null;
        ICompositionPresentationGeneration? generation = null;
        try
        {
            var baseline = OpenUsdSilkRuntime.GetDiagnostics();
            scheduler = UsdStageScheduler.Open(Path.GetFullPath(args[2]));
            source = await scheduler.AcquireRenderSourceAsync().ConfigureAwait(false);
            session = OpenUsdSilkRuntime.Create(args[1], source);
            presenter = new MetalCompositionViewportPresenter(
                context =>
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    using OpenUsdSilkPage page = session.Sync(
                        checked((int)context.ColorTarget.Width),
                        checked((int)context.ColorTarget.Height),
                        camera: CameraState.Default);
                    SilkMeshRenderResult rendered = context.Renderer.ApplyAndRender(
                        page,
                        context.ColorTarget,
                        context.DepthTarget);
                    return new MetalCompositionRenderResult(page.Revision, rendered);
                },
                required: true);
            CompositionPresenterProbeResult probe = await presenter.ProbeAsync(
                new CompositionPresentationTarget(
                    [MetalCompositionViewportPresenter.IOSurfaceHandleType],
                    [MetalCompositionViewportPresenter.SharedEventHandleType],
                    deviceLuid: null,
                    deviceUuid: null));
            if (!probe.IsAvailable)
            {
                throw new InvalidOperationException(probe.Status);
            }

            generation = await presenter.CreateGenerationAsync(
                new ViewportDimensions(128, 96),
                2);
            MetalProbeCapture initial = await CaptureMetalFrameAsync(
                presenter,
                generation.Frames[0]).ConfigureAwait(false);
            if (initial.DrawCount <= 0 ||
                initial.TriangleCount <= 0 ||
                initial.SceneRevision == 0)
            {
                throw new InvalidOperationException(
                    "The initial Metal IOSurface frame contained no retained stage mesh.");
            }

            await scheduler.EditAsync(
                static stage => stage.GetPrim("/World/Cube").SetVec3fArray(
                    "primvars:displayColor",
                    [new UsdVec3f(0.95f, 0.1f, 0.05f)]),
                UsdStageInvalidationKind.Property).ConfigureAwait(false);
            MetalProbeCapture colorEdited = await CaptureMetalFrameAsync(
                presenter,
                generation.Frames[0]).ConfigureAwait(false);
            if (colorEdited.SceneRevision <= initial.SceneRevision ||
                string.Equals(
                    colorEdited.Sha256,
                    initial.Sha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The live display-color edit did not change the Metal IOSurface hash.");
            }

            await scheduler.EditAsync(
                static stage => UsdGeomXformable.Wrap(stage.GetPrim("/World/Cube"))
                    .SetLocalTransform(UsdMatrix4d.CreateTranslation(0.45, 0, 0)),
                UsdStageInvalidationKind.Property).ConfigureAwait(false);
            MetalProbeCapture transformed = await CaptureMetalFrameAsync(
                presenter,
                generation.Frames[1]).ConfigureAwait(false);
            if (transformed.SceneRevision <= colorEdited.SceneRevision ||
                string.Equals(
                    transformed.Sha256,
                    colorEdited.Sha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The live transform edit did not change the Metal IOSurface hash.");
            }

            await generation.DisposeAsync().ConfigureAwait(false);
            generation = await presenter.CreateGenerationAsync(
                new ViewportDimensions(96, 128),
                2);
            MetalProbeCapture resized = await CaptureMetalFrameAsync(
                presenter,
                generation.Frames[0]).ConfigureAwait(false);
            if (resized.Width != 96 ||
                resized.Height != 128 ||
                resized.DrawCount <= 0)
            {
                throw new InvalidOperationException(
                    "The resized Metal generation did not render the retained stage mesh.");
            }
            await generation.DisposeAsync().ConfigureAwait(false);
            generation = null;

            MetalCompositionPresenterDiagnostics diagnostics = presenter.GetDiagnostics();
            if (diagnostics.ActiveGenerations != 0 ||
                diagnostics.ActiveFrames != 0 ||
                diagnostics.RingReuseFrames < 1 ||
                diagnostics.RenderCallbacks < 4 ||
                diagnostics.LastDrawCount <= 0 ||
                diagnostics.LastTriangleCount <= 0)
            {
                throw new InvalidOperationException(
                    $"Metal composition diagnostics were incomplete: {diagnostics}.");
            }

            await presenter.DisposeAsync().ConfigureAwait(false);
            presenter = null;
            session.Dispose();
            session = null;
            source.Dispose();
            source = null;
            await scheduler.DisposeAsync().ConfigureAwait(false);
            scheduler = null;
            var released = OpenUsdSilkRuntime.GetDiagnostics();
            if (released.ManagedSessions != baseline.ManagedSessions ||
                released.NativeSessions != baseline.NativeSessions ||
                released.ManagedPages != baseline.ManagedPages ||
                released.NativePages != baseline.NativePages ||
                released.GpuScenes != baseline.GpuScenes ||
                released.GpuMeshes != baseline.GpuMeshes)
            {
                throw new InvalidOperationException(
                    $"Metal composition resources did not return to baseline: {released}.");
            }

            string artifact =
                "{\n" +
                "  \"schemaVersion\": 1,\n" +
                "  \"status\": \"passed\",\n" +
                $"  \"initialRevision\": {initial.SceneRevision},\n" +
                $"  \"finalRevision\": {resized.SceneRevision},\n" +
                $"  \"draws\": {resized.DrawCount},\n" +
                $"  \"triangles\": {resized.TriangleCount},\n" +
                $"  \"ringReuseFrames\": {diagnostics.RingReuseFrames},\n" +
                $"  \"renderCallbacks\": {diagnostics.RenderCallbacks},\n" +
                $"  \"initialHash\": \"{initial.Sha256}\",\n" +
                $"  \"displayColorHash\": \"{colorEdited.Sha256}\",\n" +
                $"  \"transformHash\": \"{transformed.Sha256}\",\n" +
                $"  \"resizedHash\": \"{resized.Sha256}\"\n" +
                "}\n";
            File.WriteAllText(artifactPath, artifact);
            Console.WriteLine(
                $"Metal composition passed: revision={initial.SceneRevision}->" +
                $"{resized.SceneRevision}, draws={resized.DrawCount}, " +
                $"triangles={resized.TriangleCount}, reuse={diagnostics.RingReuseFrames}, " +
                $"artifact={artifactPath}");
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(
                artifactPath,
                "{\n" +
                "  \"schemaVersion\": 1,\n" +
                "  \"status\": \"failed\",\n" +
                $"  \"error\": \"{EscapeJson(exception.ToString())}\"\n" +
                "}\n");
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            if (generation is not null)
            {
                await generation.DisposeAsync().ConfigureAwait(false);
            }
            if (presenter is not null)
            {
                await presenter.DisposeAsync().ConfigureAwait(false);
            }
            session?.Dispose();
            source?.Dispose();
            if (scheduler is not null)
            {
                await scheduler.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    [SupportedOSPlatform("macos")]
    private static async Task<MetalProbeCapture> CaptureMetalFrameAsync(
        MetalCompositionViewportPresenter presenter,
        ICompositionPresentationFrame frame)
    {
        await using ICompositionExternalHandleLease image =
            await frame.LeaseImageHandleAsync().ConfigureAwait(false);
        await using ICompositionExternalHandleLease sharedEventLease =
            await frame.LeaseSemaphoreHandleAsync(
                frame.Semaphores[0].ResourceId).ConfigureAwait(false);
        var sharedEvent = new MTLSharedEvent(sharedEventLease.Handle);
        CompositionFrameRenderResult rendered =
            await presenter.RenderAsync(frame).ConfigureAwait(false);
        if (rendered.Status != CompositionFrameRenderStatus.Presented ||
            !sharedEvent.WaitUntilSignaledValue(
                rendered.Synchronization.WaitValue,
                5000))
        {
            throw new InvalidOperationException(
                "The Metal composition producer did not signal a presented frame.");
        }
        byte[] pixels = ReadIOSurfacePixels(
            image.Handle,
            frame.Image.Size.Width,
            frame.Image.Size.Height);
        sharedEvent.SignaledValue = rendered.Synchronization.SignalValue;
        MetalCompositionPresenterDiagnostics diagnostics = presenter.GetDiagnostics();
        return new MetalProbeCapture(
            frame.Image.Size.Width,
            frame.Image.Size.Height,
            diagnostics.LastSceneRevision,
            diagnostics.LastDrawCount,
            diagnostics.LastTriangleCount,
            Convert.ToHexString(SHA256.HashData(pixels)));
    }

    private static byte[] ReadIOSurfacePixels(nint surface, int width, int height)
    {
        int status = IOSurfaceLock(surface, 1, 0);
        if (status != 0)
        {
            throw new InvalidOperationException(
                $"IOSurfaceLock failed with status {status}.");
        }
        try
        {
            nint address = IOSurfaceGetBaseAddress(surface);
            nuint rowBytes = IOSurfaceGetBytesPerRow(surface);
            int packedRowBytes = checked(width * 4);
            var pixels = new byte[checked(packedRowBytes * height)];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(
                    address + checked((nint)(checked((nuint)y) * rowBytes)),
                    pixels,
                    checked(y * packedRowBytes),
                    packedRowBytes);
            }
            return pixels;
        }
        finally
        {
            _ = IOSurfaceUnlock(surface, 1, 0);
        }
    }

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private readonly record struct MetalProbeCapture(
        int Width,
        int Height,
        ulong SceneRevision,
        int DrawCount,
        long TriangleCount,
        string Sha256);

    private const string IOSurfaceLibrary =
        "/System/Library/Frameworks/IOSurface.framework/IOSurface";

    [LibraryImport(IOSurfaceLibrary, EntryPoint = "IOSurfaceGetBaseAddress")]
    private static partial nint IOSurfaceGetBaseAddress(nint surface);

    [LibraryImport(IOSurfaceLibrary, EntryPoint = "IOSurfaceGetBytesPerRow")]
    private static partial nuint IOSurfaceGetBytesPerRow(nint surface);

    [LibraryImport(IOSurfaceLibrary, EntryPoint = "IOSurfaceLock")]
    private static partial int IOSurfaceLock(nint surface, uint options, nint seed);

    [LibraryImport(IOSurfaceLibrary, EntryPoint = "IOSurfaceUnlock")]
    private static partial int IOSurfaceUnlock(nint surface, uint options, nint seed);

    private static ISilkGraphicsDevice CreateGraphicsDevice()
    {
        if (OperatingSystem.IsWindows())
        {
            return D3D12SilkGraphicsDevice.Create(useWarp: true);
        }
        if (OperatingSystem.IsMacOS())
        {
            return MetalSilkGraphicsDevice.Create();
        }
        return VulkanSilkGraphicsDevice.Create();
    }

    private static string RenderPage(
        OpenUsdSilkSession session,
        OpenUsdSilkPage page,
        ISilkGraphicsDevice device,
        int requestedWidth,
        int requestedHeight)
    {
        uint width = checked((uint)requestedWidth);
        uint height = checked((uint)requestedHeight);
        using ISilkGraphicsTexture color = device.CreateTexture2D(new SilkTextureDescriptor(
            width,
            height,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(width, height));
        using var renderer = new SilkMeshRenderer(device);
        _ = renderer.ApplyAndRender(page, color, depth);
        SilkMeshRenderResult result = renderer.SyncAndRender(
            session,
            color,
            depth,
            0);
        if (result.DrawCount == 0)
        {
            throw new InvalidOperationException(
                $"{device.Backend} did not draw any retained hdSilk meshes.");
        }

        var pixels = new byte[checked((int)(width * height * 4))];
        color.ReadbackForTesting(pixels);
        int coloredPixels = 0;
        int minX = checked((int)width);
        int minY = checked((int)height);
        int maxX = -1;
        int maxY = -1;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] != 0 || pixels[offset + 1] != 0 || pixels[offset + 2] != 0)
            {
                coloredPixels++;
                int pixel = offset / 4;
                int x = pixel % checked((int)width);
                int y = pixel / checked((int)width);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }
        if (coloredPixels == 0)
        {
            throw new InvalidOperationException(
                $"{device.Backend} produced only clear-color pixels.");
        }
        return $"{device.Backend}:{result.DrawCount} draws/{coloredPixels} colored pixels " +
            $"at ({minX},{minY})-({maxX},{maxY}), steadyUniformUploads={result.UniformUploads}";
    }

    private static (int Frames, int Upserts, int Removals) CountCommands(OpenUsdSilkPage page)
    {
        int frames = 0;
        int upserts = 0;
        int removals = 0;
        using SilkCommandEnumerator commands = page.GetEnumerator();
        while (commands.MoveNext())
        {
            switch (commands.Current.Type)
            {
                case SilkCommandType.Frame:
                    SilkFrameCommand frame = commands.Current.AsFrame();
                    _ = frame.GetViewElement(0);
                    frames++;
                    break;
                case SilkCommandType.MeshUpsert:
                    SilkMeshUpsertCommand mesh = commands.Current.AsMeshUpsert();
                    _ = mesh.Path;
                    _ = mesh.StableHash;
                    _ = mesh.PrimId;
                    _ = mesh.InstanceId;
                    _ = mesh.InstanceIndex;
                    _ = mesh.TopologyKind;
                    _ = mesh.TopologyRevision;
                    _ = mesh.GetPointComponent(0, 0);
                    _ = mesh.GetIndex(0);
                    if (mesh.TriangleCount != 0)
                    {
                        _ = mesh.GetTriangleSubprim(0);
                    }
                    upserts++;
                    break;
                case SilkCommandType.MeshRemove:
                    _ = commands.Current.AsMeshRemove().Path;
                    removals++;
                    break;
                default:
                    throw new InvalidDataException($"Unknown command type {commands.Current.Type}.");
            }
        }
        return (frames, upserts, removals);
    }

    private static bool ValidatePickIdentityTable(SilkSceneState scene)
    {
        foreach (SilkMeshData mesh in scene.MeshesByPath.Values)
        {
            _ = mesh.TopologyFingerprint;
            if (!scene.PickIdentities.TryGetRange(
                    mesh.Path,
                    out SilkPickTokenRange range) ||
                range.TokenCount != mesh.TriangleCount)
            {
                return false;
            }
            if (range.TokenCount != 0 &&
                (!scene.PickIdentities.TryResolve(
                    range.FirstToken,
                    out SilkPickIdentity identity) ||
                    identity.Path != mesh.Path ||
                    identity.PrimId != mesh.PrimId ||
                    identity.TopologyRevision != mesh.TopologyRevision ||
                    identity.SubprimIndex != mesh.TriangleSubprims.Span[0]))
            {
                return false;
            }
        }
        return true;
    }
}
