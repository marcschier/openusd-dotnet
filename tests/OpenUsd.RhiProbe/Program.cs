// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;
using OpenUsd.Rendering.Silk.Metal;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.RhiProbe;

internal static class Program
{
    private const string VulkanPresentationIterationsVariable =
        "OPENUSD_VULKAN_PRESENTATION_ITERATIONS";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            VulkanRuntimeLoader.EnsureLoaded();
            if (args is ["--vulkan-runtime-only"])
            {
                using VulkanSilkGraphicsDevice runtime =
                    VulkanSilkGraphicsDevice.Create();
                RequireExpectedVulkanRuntime(runtime);
                return 0;
            }
            if (args.Length != 0)
            {
                throw new ArgumentException(
                    $"Unknown RHI probe arguments: {string.Join(' ', args)}");
            }

            if (OperatingSystem.IsWindows())
            {
                using D3D12SilkGraphicsDevice d3d12 =
                    D3D12SilkGraphicsDevice.Create(useWarp: true);
                Probe(d3d12, SilkGraphicsBackend.D3D12);
                ProbeD3D12SelectionOutline(d3d12);
                ProbeD3D12Picking(d3d12);
                await ProbeD3D12Presentation(d3d12);
            }

            VulkanSilkGraphicsDevice? vulkan = null;
            try
            {
                vulkan = VulkanSilkGraphicsDevice.Create();
                RequireExpectedVulkanRuntime(vulkan);
                Probe(vulkan, SilkGraphicsBackend.Vulkan);
                ProbeVulkanPicking(vulkan);
                ProbeVulkanSelectionOutline(vulkan);
            }
            finally
            {
                Console.WriteLine("Vulkan device teardown: begin");
                vulkan?.Dispose();
                Console.WriteLine("Vulkan device teardown: complete");
            }

            int presentationIterations = GetVulkanPresentationIterations();
            for (int iteration = 1; iteration <= presentationIterations; iteration++)
            {
                Console.WriteLine(
                    $"Vulkan presentation probe {iteration}/{presentationIterations}: begin");
                await ProbeVulkanPresentation();
                Console.WriteLine(
                    $"Vulkan presentation probe {iteration}/{presentationIterations}: complete");
            }

            if (OperatingSystem.IsMacOS())
            {
                using MetalSilkGraphicsDevice metal = MetalSilkGraphicsDevice.Create();
                Probe(metal, SilkGraphicsBackend.Metal);
                ProbeMetalSelectionOutline(metal);
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void RequireExpectedVulkanRuntime(
        VulkanSilkGraphicsDevice device)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(
                    VulkanRuntimeLoader.RequireSwiftShaderVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        string loaderPath = VulkanRuntimeLoader.RequireAbsoluteFile(
            VulkanRuntimeLoader.LoaderPathVariable);
        string driverPath = VulkanRuntimeLoader.RequireAbsoluteFile(
            VulkanRuntimeLoader.DriverPathVariable);
        string manifestPath = VulkanRuntimeLoader.RequireAbsoluteFile(
            VulkanRuntimeLoader.ManifestPathVariable);
        string expectedApiText =
            VulkanRuntimeLoader.RequireEnvironmentVariable(
                VulkanRuntimeLoader.ApiVersionVariable);
        if (!Version.TryParse(expectedApiText, out Version? expectedApi) ||
            expectedApi.Major != 1 ||
            expectedApi.Minor != 3)
        {
            throw new InvalidOperationException(
                $"The locked Vulkan API must be 1.3, but was '{expectedApiText}'.");
        }

        VulkanRuntimeLoader.AssertSha256(
            driverPath,
            VulkanRuntimeLoader.RequireEnvironmentVariable(
                VulkanRuntimeLoader.DriverHashVariable),
            "SwiftShader driver");
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllBytes(manifestPath));
        JsonElement icd = manifest.RootElement.GetProperty("ICD");
        string manifestDriver =
            icd.GetProperty("library_path").GetString() ?? string.Empty;
        string manifestApi =
            icd.GetProperty("api_version").GetString() ?? string.Empty;
        if (!Path.IsPathFullyQualified(manifestDriver) ||
            !VulkanRuntimeLoader.PathsEqual(manifestDriver, driverPath) ||
            !string.Equals(
                manifestApi,
                expectedApiText,
                StringComparison.Ordinal) ||
            !VulkanRuntimeLoader.PathsEqual(
                VulkanRuntimeLoader.RequireEnvironmentVariable(
                    "VK_DRIVER_FILES"),
                manifestPath) ||
            !VulkanRuntimeLoader.PathsEqual(
                VulkanRuntimeLoader.RequireEnvironmentVariable(
                    "VK_ICD_FILENAMES"),
                manifestPath))
        {
            throw new InvalidOperationException(
                "The active Vulkan manifest does not select the locked " +
                "absolute SwiftShader Vulkan 1.3 driver.");
        }

        if (!device.Capabilities.IsSoftware ||
            !device.Capabilities.DeviceName.Contains(
                "SwiftShader",
                StringComparison.OrdinalIgnoreCase) ||
            !Version.TryParse(device.Capabilities.ApiVersion, out Version? apiVersion) ||
            apiVersion != expectedApi)
        {
            throw new InvalidOperationException(
                "The Vulkan probe requires the locked SwiftShader Vulkan 1.3 runtime.");
        }
        Console.WriteLine(
            $"VULKAN_RUNTIME loader={VulkanRuntimeLoader.LoadedPath ?? loaderPath}; " +
            $"loaderSha256={VulkanRuntimeLoader.RequireEnvironmentVariable(
                VulkanRuntimeLoader.LoaderHashVariable)}; " +
            $"manifest={manifestPath}; driver={driverPath}; " +
            $"driverSha256={VulkanRuntimeLoader.RequireEnvironmentVariable(
                VulkanRuntimeLoader.DriverHashVariable)}; " +
            $"device={device.Capabilities.DeviceName}; " +
            $"api={device.Capabilities.ApiVersion}");
    }

    private static int GetVulkanPresentationIterations()
    {
        string? value = Environment.GetEnvironmentVariable(
            VulkanPresentationIterationsVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return 1;
        }
        if (!int.TryParse(
                value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int iterations) ||
            iterations is < 1 or > 100)
        {
            throw new InvalidOperationException(
                $"{VulkanPresentationIterationsVariable} must be between 1 and 100.");
        }
        return iterations;
    }

    [SupportedOSPlatform("macos")]
    private static void ProbeMetalSelectionOutline(
        MetalSilkGraphicsDevice device)
    {
        const uint size = 8;
        using ISilkSelectionMaskGraphicsPipeline maskPipeline =
            device.CreateSelectionMaskGraphicsPipeline(
                SilkSelectionMaskPipelineDescriptor.CreateChecked(
                    SilkShaderBinaryFormat.MetalLibrary));
        using ISilkSelectionOutlineGraphicsPipeline outlinePipeline =
            device.CreateSelectionOutlineGraphicsPipeline(
                SilkSelectionOutlinePipelineDescriptor.CreateChecked(
                    SilkShaderBinaryFormat.MetalLibrary));
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                size,
                size,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget |
                    SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(size, size));
        using ISilkGraphicsTexture mask = device.CreateTexture2D(
            SilkTextureDescriptor.SelectionMask(size, size));
        using ISilkGraphicsSampler sampler = device.CreateSampler(
            SilkSamplerDescriptor.NearestClamp);
        using ISilkGraphicsBuffer parameters = device.CreateBuffer(
            SilkSelectionOutlineUniformWriter.ByteSize,
            SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        Span<byte> parameterBytes =
            stackalloc byte[SilkSelectionOutlineUniformWriter.ByteSize];
        SilkSelectionOutlineUniformWriter.Write(
            SilkSelectionOutlineSettings.Default,
            size,
            size,
            parameterBytes);
        parameters.Write(parameterBytes);
        using ISilkSelectionOutlineBinding binding =
            device.CreateSelectionOutlineBinding(new(
                mask,
                depth,
                sampler,
                parameters));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        var selectionCommands =
            (ISilkSelectionOutlineGraphicsCommandList)commands;
        commands.ClearColor(color, new SilkColor(0, 1, 0, 1));
        commands.ClearColor(mask, new SilkColor(0, 0, 0, 0));
        commands.ClearDepth(depth, 1);
        selectionCommands.BeginSelectionMaskRendering(new(mask, depth));
        selectionCommands.SetSelectionMaskGraphicsPipeline(maskPipeline);
        commands.SetViewport(new SilkViewport(0, 0, size, size));
        commands.SetScissor(new SilkScissor(0, 0, size, size));
        commands.EndRendering();
        selectionCommands.BeginSelectionOutlineRendering(new(color));
        selectionCommands.SetSelectionOutlineGraphicsPipeline(outlinePipeline);
        selectionCommands.SetSelectionOutlineBinding(binding);
        commands.SetViewport(new SilkViewport(0, 0, size, size));
        commands.SetScissor(new SilkScissor(0, 0, size, size));
        selectionCommands.DrawSelectionOutlineFullscreenTriangle();
        commands.EndRendering();
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();

        var pixels = new byte[size * size * 4];
        color.ReadbackForTesting(pixels);
        ValidateGreenClear(pixels);
        Console.WriteLine(
            $"Metal selection outline: generation={device.SelectionOutlineDeviceGeneration}; " +
            $"visibleOnly={device.SelectionOutlineCapabilities.SupportsVisibleOnly}");
    }

    private static async Task ProbeVulkanPresentation()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            return;
        }

        const string opaqueNtHandle = "VulkanOpaqueNtHandle";
        const string opaquePosixFileDescriptor =
            "VulkanOpaquePosixFileDescriptor";
        string supportedHandleType = OperatingSystem.IsWindows()
            ? opaqueNtHandle
            : opaquePosixFileDescriptor;
        string unsupportedHandleType = OperatingSystem.IsWindows()
            ? opaquePosixFileDescriptor
            : opaqueNtHandle;
        await using VulkanCompositionViewportPresenter presenter =
            VulkanCompositionViewportPresenter.Create();

        var unsupportedTarget = new CompositionPresentationTarget(
            [unsupportedHandleType],
            [unsupportedHandleType],
            deviceLuid: null,
            deviceUuid: null);
        CompositionPresenterProbeResult unsupported =
            await presenter.ProbeAsync(unsupportedTarget);
        if (unsupported.IsAvailable)
        {
            throw new InvalidOperationException(
                "The Vulkan presenter accepted an external handle type from the wrong platform.");
        }
        Console.WriteLine(
            $"Vulkan presentation unsupported target: {unsupported.Status}");

        var supportedTarget = new CompositionPresentationTarget(
            [supportedHandleType],
            [supportedHandleType],
            deviceLuid: null,
            deviceUuid: null);
        CompositionPresenterProbeResult supported =
            await presenter.ProbeAsync(supportedTarget);
        Console.WriteLine(
            $"Vulkan presentation supported target: available={supported.IsAvailable}; " +
            supported.Status);
        if (!supported.IsAvailable)
        {
            return;
        }

        ICompositionPresentationGeneration? generation = null;
        ICompositionExternalHandleLease? imageLease = null;
        ICompositionExternalHandleLease? semaphoreLease = null;
        try
        {
            generation = await presenter.CreateGenerationAsync(
                new ViewportDimensions(4, 3),
                frameCount: 2);
            ICompositionPresentationFrame frame = generation.Frames[0];
            imageLease = await frame.LeaseImageHandleAsync();
            semaphoreLease = await frame.LeaseSemaphoreHandleAsync(
                frame.Semaphores[0].ResourceId);
            CompositionExternalHandleValidityPolicy expectedValidity =
                OperatingSystem.IsWindows()
                    ? CompositionExternalHandleValidityPolicy.NonZero
                    : CompositionExternalHandleValidityPolicy.NonNegativeFileDescriptor;
            if (imageLease.IsInvalid ||
                semaphoreLease.IsInvalid ||
                imageLease.ValidityPolicy != expectedValidity ||
                semaphoreLease.ValidityPolicy != expectedValidity ||
                imageLease.HandleType != supportedHandleType ||
                semaphoreLease.HandleType != supportedHandleType)
            {
                throw new InvalidOperationException(
                    "The Vulkan presentation probe returned an invalid external lease.");
            }
            Console.WriteLine(
                $"Vulkan presentation generation: frames={generation.Frames.Count}; " +
                $"image={imageLease.Handle}; semaphore={semaphoreLease.Handle}");
        }
        finally
        {
            if (semaphoreLease is not null)
            {
                await semaphoreLease.DisposeAsync();
            }
            if (imageLease is not null)
            {
                await imageLease.DisposeAsync();
            }
            if (generation is not null)
            {
                await generation.DisposeAsync();
            }
        }
        if (imageLease is null ||
            semaphoreLease is null ||
            !imageLease.IsInvalid ||
            !semaphoreLease.IsInvalid)
        {
            throw new InvalidOperationException(
                "Disposed Vulkan external leases did not report invalid.");
        }
        Console.WriteLine("Vulkan presentation generation teardown: complete");
    }

    private static void ProbeVulkanPicking(VulkanSilkGraphicsDevice device)
    {
        if (!device.Capabilities.IsSoftware ||
            !device.Capabilities.DeviceName.Contains(
                "SwiftShader",
                StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                $"Vulkan NativeAOT picking skipped for {device.Capabilities.DeviceName}.");
            return;
        }

        const uint size = 16;
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(size, size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(size, size));
        byte[] frame = CreateVulkanPickFrame(size, size);
        byte[] mesh = CreateVulkanPickMesh();
        var page = new byte[frame.Length + mesh.Length];
        frame.CopyTo(page, 0);
        mesh.CopyTo(page, frame.Length);
        SilkSceneDelta delta = renderer.Scene.Apply(page, 2, 1);
        renderer.GpuResources.Apply(renderer.Scene, delta);
        if (!renderer.Scene.PickIdentities.TryGetRange(
                "/NativeAotVulkanPick",
                out SilkPickTokenRange range))
        {
            throw new InvalidOperationException(
                "The Vulkan NativeAOT pick identity was not retained.");
        }

        var request = new RenderPickRequest(
            8,
            8,
            new ViewportDimensions((int)size, (int)size),
            requestedStateRevision: 1,
            requestedSceneRevision: 1,
            target: RenderPickTarget.Face);
        Task<RenderPickResult> pending = renderer.PickAsync(request).AsTask();
        var binding = new SilkPickFrameBinding(1, 1);
        _ = renderer.Render(color, depth, binding);
        for (int iteration = 0;
             iteration < 100 && !pending.IsCompleted;
             iteration++)
        {
            Thread.Yield();
            _ = renderer.Render(color, depth, binding);
        }
        if (!pending.IsCompleted)
        {
            throw new TimeoutException(
                "The Vulkan NativeAOT ID readback did not complete.");
        }
        RenderPickResult result = pending.GetAwaiter().GetResult();
        if (result.Status != RenderPickStatus.Hit ||
            result.PrimPath != "/NativeAotVulkanPick" ||
            result.ElementIndex != 42 ||
            result.BackendKind != RenderBackendKind.Vulkan ||
            result.BackendToken != range.FirstToken ||
            result.WorldPosition is not null ||
            result.WorldNormal is not null ||
            result.NormalizedDepth is not null)
        {
            throw new InvalidOperationException(
                "The Vulkan NativeAOT pick did not resolve the ID token with nullable geometry.");
        }
        Console.WriteLine(
            $"Vulkan NativeAOT pick: {result.PrimPath}[{result.ElementIndex}], " +
            $"token={result.BackendToken}");
    }

    private static void ProbeVulkanSelectionOutline(
        VulkanSilkGraphicsDevice device)
    {
        if (!device.Capabilities.IsSoftware ||
            !device.Capabilities.DeviceName.Contains(
                "SwiftShader",
                StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                $"Vulkan NativeAOT selection outline skipped for " +
                $"{device.Capabilities.DeviceName}.");
            return;
        }

        const uint size = 32;
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(size, size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(size, size));
        byte[] frame = CreateVulkanPickFrame(size, size);
        byte[] mesh = CreateVulkanPickMesh();
        var page = new byte[frame.Length + mesh.Length];
        frame.CopyTo(page, 0);
        mesh.CopyTo(page, frame.Length);
        SilkSceneDelta delta = renderer.Scene.Apply(page, 2, 1);
        renderer.GpuResources.Apply(renderer.Scene, delta);

        _ = renderer.Render(color, depth);
        byte[] baseline = ReadTexture(color);
        renderer.UpdateSelection(
            new SelectionState(["/NativeAotVulkanPick"]));
        _ = renderer.Render(color, depth);
        byte[] outlined = ReadTexture(color);
        renderer.UpdateSelection(SelectionState.Empty);
        _ = renderer.Render(color, depth);
        byte[] cleared = ReadTexture(color);
        if (outlined.AsSpan().SequenceEqual(baseline) ||
            !cleared.AsSpan().SequenceEqual(baseline) ||
            CountVulkanOutlinePixels(outlined) == 0 ||
            renderer.SelectionOutlineDiagnostics.Status !=
                SilkSelectionOutlineStatus.EmptySelection)
        {
            throw new InvalidOperationException(
                "The Vulkan NativeAOT selection outline pixel path failed.");
        }
        Console.WriteLine(
            $"Vulkan NativeAOT selection outline: " +
            $"{CountVulkanOutlinePixels(outlined)} pixels.");
    }

    private static int CountVulkanOutlinePixels(ReadOnlySpan<byte> pixels)
    {
        int count = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] > 150 &&
                pixels[offset] > pixels[offset + 1] + 25 &&
                pixels[offset + 1] > pixels[offset + 2])
            {
                count++;
            }
        }
        return count;
    }

    private static byte[] CreateVulkanPickFrame(uint width, uint height)
    {
        var bytes = new byte[272];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(4),
            checked((uint)bytes.Length));
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(8),
            checked((int)width));
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(12),
            checked((int)height));
        for (int index = 0; index < 16; index++)
        {
            double value = index % 5 == 0 ? 1 : 0;
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(16 + (index * sizeof(double))),
                value);
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(144 + (index * sizeof(double))),
                value);
        }
        return bytes;
    }

    private static byte[] CreateVulkanPickMesh()
    {
        const string pathValue = "/NativeAotVulkanPick";
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        float[] points =
        [
            -0.75f, -0.75f, 0.25f,
             0.00f,  0.75f, 0.25f,
             0.75f, -0.75f, 0.25f
        ];
        uint[] indices = [0, 1, 2];
        int size = 216 +
            path.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            sizeof(uint);
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(4),
            checked((uint)bytes.Length));
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            ComputeStableHash(pathValue));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(40),
            checked((uint)path.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 1);
        for (int channel = 0; channel < 4; channel++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(56 + (channel * sizeof(float))),
                1);
        }
        for (int index = 0; index < 16; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(72 + (index * sizeof(double))),
                index % 5 == 0 ? 1 : 0);
        }
        path.CopyTo(bytes, 216);
        int pointOffset = 216 + path.Length;
        for (int index = 0; index < points.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(pointOffset + (index * sizeof(float))),
                points[index]);
        }
        int indexOffset = pointOffset + (points.Length * sizeof(float));
        for (int index = 0; index < indices.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(indexOffset + (index * sizeof(uint))),
                indices[index]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(indexOffset + (indices.Length * sizeof(uint))),
            42);
        return bytes;
    }

    private static ulong ComputeStableHash(string path)
    {
        ulong hash = 14695981039346656037;
        foreach (byte value in Encoding.UTF8.GetBytes(path))
        {
            hash ^= value;
            hash *= 1099511628211;
        }
        return hash;
    }

    [SupportedOSPlatform("windows")]
    private static void ProbeD3D12SelectionOutline(
        D3D12SilkGraphicsDevice device)
    {
        const uint size = 16;
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                size,
                size,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget |
                    SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(size, size));
        byte[] frame = CreateVulkanPickFrame(size, size);
        byte[] mesh = CreateVulkanPickMesh();
        var page = new byte[frame.Length + mesh.Length];
        frame.CopyTo(page, 0);
        mesh.CopyTo(page, frame.Length);
        SilkSceneDelta delta = renderer.Scene.Apply(page, 2, 1);
        renderer.GpuResources.Apply(renderer.Scene, delta);

        _ = renderer.Render(color, depth);
        byte[] baseline = ReadTexture(color);
        renderer.UpdateSelection(
            new SelectionState(["/NativeAotVulkanPick"]));
        _ = renderer.Render(color, depth);
        byte[] selected = ReadTexture(color);
        if (selected.SequenceEqual(baseline))
        {
            throw new InvalidOperationException(
                "The D3D12 NativeAOT selection outline changed no pixels.");
        }
        renderer.UpdateSelection(SelectionState.Empty);
        _ = renderer.Render(color, depth);
        byte[] restored = ReadTexture(color);
        if (!restored.SequenceEqual(baseline))
        {
            throw new InvalidOperationException(
                "The D3D12 NativeAOT empty selection did not restore baseline pixels.");
        }
        Console.WriteLine("D3D12 NativeAOT visible selection outline: passed");
    }

    private static byte[] ReadTexture(ISilkGraphicsTexture texture)
    {
        var pixels = new byte[
            checked((int)(texture.Width * texture.Height * 4))];
        texture.ReadbackForTesting(pixels);
        return pixels;
    }

    [SupportedOSPlatform("windows")]
    private static void ProbeD3D12Picking(D3D12SilkGraphicsDevice device)
    {
        using ISilkPickGraphicsPipeline pipeline =
            device.CreatePickGraphicsPipeline(
                SilkPickPipelineDescriptor.CreateChecked(
                    SilkShaderBinaryFormat.Dxil));
        using ISilkPickReadbackBuffer readback =
            device.CreatePickReadbackBuffer();
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            new SilkTextureDescriptor(
                1,
                1,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureUsage.ColorRenderTarget |
                    SilkTextureUsage.CopySource));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(1, 1));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        var pickCommands = (ISilkPickGraphicsCommandList)commands;
        commands.ClearColor(color, new SilkColor(0, 0, 0, 0));
        commands.ClearDepth(depth, 1);
        commands.BeginRendering(new SilkRenderingDescriptor(color, depth));
        pickCommands.SetPickGraphicsPipeline(pipeline);
        commands.SetViewport(new SilkViewport(0, 0, 1, 1));
        commands.SetScissor(new SilkScissor(0, 0, 1, 1));
        commands.EndRendering();
        pickCommands.CopyRgba8Pixel(
            color,
            new SilkTexturePixelCoordinate(0, 0),
            readback);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();
        Span<byte> token = stackalloc byte[SilkPickTokenEncoding.ByteSize];
        readback.ReadRgba8Pixel(token);
        if (SilkPickTokenEncoding.Decode(token) != 0)
        {
            throw new InvalidOperationException(
                "The D3D12 NativeAOT pick probe did not preserve token-zero miss.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task ProbeD3D12Presentation(D3D12SilkGraphicsDevice device)
    {
        var presenter = new D3D12CompositionViewportPresenter(device);
        ICompositionPresentationGeneration? generation = null;
        ICompositionExternalHandleLease? lease = null;
        try
        {
            var target = new CompositionPresentationTarget(
                [D3D12CompositionViewportPresenter.D3D11TextureNtHandle],
                [],
                presenter.RendererAdapterLuid.ToArray(),
                null);
            CompositionPresenterProbeResult probe = await presenter.ProbeAsync(target);
            if (!probe.IsAvailable)
            {
                throw new InvalidOperationException(probe.Status);
            }

            generation = await presenter.CreateGenerationAsync(
                new ViewportDimensions(4, 3),
                2);
            lease = await generation.Frames[0].LeaseImageHandleAsync();
            if (lease.IsInvalid ||
                lease.ValidityPolicy != CompositionExternalHandleValidityPolicy.NonZero ||
                lease.HandleType !=
                    D3D12CompositionViewportPresenter.D3D11TextureNtHandle)
            {
                throw new InvalidOperationException(
                    "The D3D12 presentation probe returned an invalid D3D11 NT lease.");
            }
            Console.WriteLine(
                $"D3D12 presentation: {probe.Status}; handle=0x{lease.Handle:X}");
        }
        finally
        {
            if (lease is not null)
            {
                await lease.DisposeAsync();
            }
            if (generation is not null)
            {
                await generation.DisposeAsync();
            }
            await presenter.DisposeAsync();
        }
    }

    private static void Probe(
        ISilkGraphicsDevice device,
        SilkGraphicsBackend expectedBackend)
    {
        if (expectedBackend == SilkGraphicsBackend.Metal)
        {
            SilkCheckedShaderAssets.ValidatePinnedMetalLibrary();
        }
        using ISilkGraphicsBuffer buffer = device.CreateBuffer(
            4096,
            SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
        byte[] data = [1, 2, 3, 4];
        buffer.Write(data, 128);
        using ISilkGraphicsTexture texture = device.CreateTexture2D(4, 3);
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.ClearColor(texture, new SilkColor(0, 1, 0, 1));
        using ISilkGraphicsSubmission submission = device.Submit(commands);

        byte[] readback = new byte[4 * 3 * 4];
        texture.ReadbackForTesting(readback);
        ValidateGreenClear(readback);
        ProbeDepthTarget(device);
        ProbeTextureUpload(device);
        ProbeSubmissionLifetime(device);
        SilkShaderBinaryFormat shaderFormat = expectedBackend switch
        {
            SilkGraphicsBackend.D3D12 => SilkShaderBinaryFormat.Dxil,
            SilkGraphicsBackend.Vulkan => SilkShaderBinaryFormat.SpirV,
            SilkGraphicsBackend.Metal => SilkShaderBinaryFormat.MetalLibrary,
            _ => throw new ArgumentOutOfRangeException(nameof(expectedBackend))
        };
        ProbeIndexedTriangle(
            device,
            shaderFormat);
        ProbeCompute(device, shaderFormat);

        if (device.Backend != expectedBackend ||
            buffer.Size != (nuint)4096 ||
            texture.Width != 4 ||
            texture.Height != 3 ||
            !submission.IsCompleted ||
            !device.Capabilities.SupportsCompute)
        {
            throw new InvalidOperationException(
                $"{expectedBackend} did not satisfy the RHI probe contract.");
        }

        Console.WriteLine(
            $"{device.Backend}: {device.Capabilities.DeviceName}; " +
            $"API {device.Capabilities.ApiVersion}; " +
            $"software={device.Capabilities.IsSoftware}");
    }

    private static void ProbeTextureUpload(ISilkGraphicsDevice device)
    {
        byte[] expected =
        [
            1, 2, 3, 255,
            5, 8, 13, 254,
            21, 34, 55, 253,
            89, 144, 233, 252
        ];
        using ISilkGraphicsTexture texture = device.CreateTexture2D(
            SilkTextureDescriptor.SampledRgba8(2, 2));
        using ISilkGraphicsSampler sampler = device.CreateSampler(
            SilkSamplerDescriptor.LinearClamp);
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.UploadTexture(texture, expected);
        using ISilkGraphicsSubmission submission = device.Submit(commands);

        byte[] actual = new byte[expected.Length];
        texture.ReadbackForTesting(actual);
        if (!actual.AsSpan().SequenceEqual(expected) ||
            !submission.IsCompleted ||
            sampler.Descriptor.MinFilter != SilkSamplerFilter.Linear ||
            texture.Usage !=
                (SilkTextureUsage.Sampled |
                 SilkTextureUsage.CopySource |
                 SilkTextureUsage.CopyDestination))
        {
            throw new InvalidOperationException(
                "The texture upload and sampler did not satisfy the RHI probe contract.");
        }
    }

    private static void ProbeDepthTarget(ISilkGraphicsDevice device)
    {
        using ISilkGraphicsTexture texture = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(4, 3));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.ClearDepth(texture, 0.25f);
        commands.ClearDepth(texture, 0.625f);
        using ISilkGraphicsSubmission submission = device.Submit(commands);

        float[] readback = new float[4 * 3];
        texture.ReadbackForTesting(readback);
        foreach (float value in readback)
        {
            if (value != 0.625f)
            {
                throw new InvalidOperationException(
                    "The offscreen depth clear did not produce D32Float values.");
            }
        }
        if (!submission.IsCompleted ||
            texture.Format != SilkTextureFormat.D32Float ||
            texture.Usage != SilkTextureUsage.DepthRenderTarget)
        {
            throw new InvalidOperationException(
                "The depth target did not satisfy the RHI probe contract.");
        }
    }

    private static void ProbeSubmissionLifetime(ISilkGraphicsDevice device)
    {
        ISilkGraphicsTexture texture = device.CreateTexture2D(64, 64);
        ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.ClearColor(texture, new SilkColor(0, 0, 1, 1));
        ISilkGraphicsSubmission submission = device.Submit(commands);

        commands.Dispose();
        texture.Dispose();
        try
        {
            texture.ReadbackForTesting(new byte[64 * 64 * 4]);
            throw new InvalidOperationException(
                "A disposed submitted texture remained callable.");
        }
        catch (ObjectDisposedException)
        {
        }

        submission.Wait();
        submission.Dispose();
    }

    private static void ProbeCompute(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        const uint elementCount = 67;
        using ISilkGraphicsBuffer output = device.CreateBuffer(
            checked((nuint)elementCount * 16),
            SilkBufferUsage.Storage);
        using ISilkGraphicsBuffer fillUniform = CreateComputeUniform(
            device,
            elementCount,
            1.5f);
        using ISilkGraphicsBuffer scaleUniform = CreateComputeUniform(
            device,
            elementCount,
            2);
        using ISilkComputeBindingLayout layout = device.CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor.Checked);
        using ISilkGraphicsShaderModule fillShader = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadComputeFill(shaderFormat));
        using ISilkGraphicsShaderModule scaleShader = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadComputeScale(shaderFormat));
        using ISilkComputeShaderProgram fillProgram =
            device.CreateComputeShaderProgram(
                new SilkComputeShaderProgramDescriptor(fillShader, layout));
        using ISilkComputeShaderProgram scaleProgram =
            device.CreateComputeShaderProgram(
                new SilkComputeShaderProgramDescriptor(scaleShader, layout));
        using ISilkComputePipeline fillPipeline = device.CreateComputePipeline(
            SilkComputePipelineDescriptor.Checked(fillProgram));
        using ISilkComputePipeline scalePipeline = device.CreateComputePipeline(
            SilkComputePipelineDescriptor.Checked(scaleProgram));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.SetComputePipeline(fillPipeline);
        commands.SetStorageBuffer(0, 0, output);
        commands.SetComputeUniformBuffer(0, 1, fillUniform);
        commands.Dispatch(elementCount);
        commands.BufferBarrier(output);
        commands.SetComputePipeline(scalePipeline);
        commands.SetComputeUniformBuffer(0, 1, scaleUniform);
        commands.Dispatch(elementCount);
        commands.BufferBarrier(output);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();

        byte[] bytes = new byte[checked((int)elementCount * 16)];
        output.ReadbackForTesting(bytes);
        ReadOnlySpan<float> values = MemoryMarshal.Cast<byte, float>(bytes);
        for (uint index = 0; index < elementCount; index++)
        {
            int offset = checked((int)index * 4);
            if (values[offset] != index * 3 ||
                values[offset + 1] != 0 ||
                values[offset + 2] != 0 ||
                values[offset + 3] != 1)
            {
                throw new InvalidOperationException(
                    $"{device.Backend} compute probe mismatch at element {index}.");
            }
        }
        Console.WriteLine(
            $"{device.Backend}: compute[66]=({values[264]}, {values[265]}, " +
            $"{values[266]}, {values[267]})");
    }

    private static ISilkGraphicsBuffer CreateComputeUniform(
        ISilkGraphicsDevice device,
        uint elementCount,
        float scale)
    {
        byte[] bytes = new SilkComputeParameters(elementCount, scale)
            .ToBytes(device.Backend);
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            checked((nuint)bytes.Length),
            SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        buffer.Write(bytes);
        return buffer;
    }

    private static void ProbeIndexedTriangle(
        ISilkGraphicsDevice device,
        SilkShaderBinaryFormat shaderFormat)
    {
        const uint size = 64;
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(size, size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(size, size));
        using ISilkGraphicsShaderModule vertexShader = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadMeshVertex(shaderFormat));
        using ISilkGraphicsShaderModule fragmentShader = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadMeshFragment(shaderFormat));
        using ISilkGraphicsBindingLayout layout = device.CreateBindingLayout(
            SilkBindingLayoutDescriptor.SceneParameters);
        using ISilkGraphicsShaderProgram program = device.CreateShaderProgram(
            new SilkShaderProgramDescriptor(vertexShader, fragmentShader, layout));
        using ISilkGraphicsPipeline pipeline = device.CreateGraphicsPipeline(
            new SilkGraphicsPipelineDescriptor(
                program,
                SilkVertexLayoutDescriptor.PositionNormal,
                SilkTextureFormat.Rgba8Unorm,
                SilkTextureFormat.D32Float));
        using ISilkGraphicsBuffer vertices = device.CreateBuffer(
            144,
            SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
        using ISilkGraphicsBuffer indices = device.CreateBuffer(
            12,
            SilkBufferUsage.Index | SilkBufferUsage.Upload);
        using ISilkGraphicsBuffer uniforms = device.CreateBuffer(
            80,
            SilkBufferUsage.Uniform | SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        using ISilkGraphicsBuffer surfaceConstants = CreateSurfaceConstants(device);
        vertices.Write(MemoryMarshal.AsBytes<float>(
        [
            -3.0f, -3.0f, 0, 0, 1, 0,
             3.0f,  3.0f, 0, 0, 0, 1,
             0.0f,  0.75f, 0, 0, 0, 1,
            -3.0f,  3.0f, 0, 0, 1, 1,
            -0.75f, -0.75f, 0, 0, 0, 1,
             0.75f, -0.75f, 0, 0, 0, 1
        ]));
        indices.Write(MemoryMarshal.AsBytes<uint>([4, 2, 5]));
        uniforms.Write(MemoryMarshal.AsBytes<float>(
        [
            0.5f, 0, 0, 0,
            0, 0.75f, 0, 0,
            0, 0, 1, 0,
            0.25f, -0.125f, 0, 1,
            1, 1, 1, 1
        ]));

        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.ClearColor(color, new SilkColor(0, 0, 0, 1));
        commands.ClearDepth(depth, 1);
        commands.BeginRendering(new SilkRenderingDescriptor(color, depth));
        commands.SetGraphicsPipeline(pipeline);
        commands.SetViewport(new SilkViewport(16, 8, 32, 40));
        commands.SetScissor(new SilkScissor(34, 16, 10, 24));
        commands.SetVertexBuffer(vertices);
        commands.SetIndexBuffer(indices);
        commands.SetUniformBuffer(0, 0, uniforms);
        BindAlwaysOnSlots(commands, uniforms, surfaceConstants);
        commands.DrawIndexed(3);
        commands.EndRendering();
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();

        byte[] pixels = new byte[size * size * 4];
        color.ReadbackForTesting(pixels);
        int backgroundOffset = checked((int)((2 * size + 2) * 4));
        int interiorOffset = checked((int)((27 * size + 34) * 4));
        int scissorExcludedOffset = checked((int)((27 * size + 33) * 4));
        int viewportExcludedOffset = checked((int)((30 * size + 50) * 4));
        if (!pixels.AsSpan(backgroundOffset, 4).SequenceEqual(
                new byte[] { 0, 0, 0, 255 }) ||
            !pixels.AsSpan(scissorExcludedOffset, 4).SequenceEqual(
                new byte[] { 0, 0, 0, 255 }) ||
            !pixels.AsSpan(viewportExcludedOffset, 4).SequenceEqual(
                new byte[] { 0, 0, 0, 255 }) ||
            pixels[interiorOffset] < 240 ||
            pixels[interiorOffset + 1] < 240 ||
            pixels[interiorOffset + 2] < 240 ||
            pixels[interiorOffset + 3] < 240)
        {
            throw new InvalidOperationException(
                "The indexed triangle did not produce the expected GPU-rendered pixels.");
        }
        Console.WriteLine(
            $"{device.Backend} triangle: background=000000ff, " +
            $"interior={Convert.ToHexString(pixels.AsSpan(interiorOffset, 4)).ToLowerInvariant()}");

        using (ISilkGraphicsCommandList ordered = device.CreateCommandList())
        {
            ordered.ClearDepth(depth, 1);
            RecordProbeDraw(
                ordered,
                color,
                depth,
                pipeline,
                vertices,
                indices,
                uniforms,
                surfaceConstants,
                size);
            ordered.ClearColor(color, new SilkColor(0, 1, 0, 1));
            using ISilkGraphicsSubmission orderedSubmission = device.Submit(ordered);
            orderedSubmission.Wait();
        }
        color.ReadbackForTesting(pixels);
        ValidateSolidColor(pixels, 0, 255, 0, 255, "draw then clear");

        using (ISilkGraphicsCommandList ordered = device.CreateCommandList())
        {
            ordered.ClearColor(color, new SilkColor(0, 0, 1, 1));
            ordered.ClearDepth(depth, 1);
            RecordProbeDraw(
                ordered,
                color,
                depth,
                pipeline,
                vertices,
                indices,
                uniforms,
                surfaceConstants,
                size);
            ordered.ClearColor(color, new SilkColor(1, 1, 0, 1));
            using ISilkGraphicsSubmission orderedSubmission = device.Submit(ordered);
            orderedSubmission.Wait();
        }
        color.ReadbackForTesting(pixels);
        ValidateSolidColor(pixels, 255, 255, 0, 255, "clear, draw, then clear");

        using ISilkGraphicsBuffer computedIndices = device.CreateBuffer(
            16,
            SilkBufferUsage.Storage | SilkBufferUsage.Index);
        using ISilkGraphicsBuffer computeUniform = CreateComputeUniform(device, 1, 1);
        using ISilkComputeBindingLayout computeLayout =
            device.CreateComputeBindingLayout(SilkComputeBindingLayoutDescriptor.Checked);
        using ISilkGraphicsShaderModule computeShader = device.CreateShaderModule(
            SilkCheckedShaderAssets.LoadComputeFill(shaderFormat));
        using ISilkComputeShaderProgram computeProgram =
            device.CreateComputeShaderProgram(
                new SilkComputeShaderProgramDescriptor(computeShader, computeLayout));
        using ISilkComputePipeline computePipeline = device.CreateComputePipeline(
            SilkComputePipelineDescriptor.Checked(computeProgram));
        using (ISilkGraphicsCommandList barrierCommands = device.CreateCommandList())
        {
            barrierCommands.SetComputePipeline(computePipeline);
            barrierCommands.SetStorageBuffer(0, 0, computedIndices);
            barrierCommands.SetComputeUniformBuffer(0, 1, computeUniform);
            barrierCommands.Dispatch(1);
            barrierCommands.BufferBarrier(computedIndices);
            barrierCommands.ClearColor(color, new SilkColor(0, 0, 0, 1));
            barrierCommands.ClearDepth(depth, 1);
            RecordProbeDraw(
                barrierCommands,
                color,
                depth,
                pipeline,
                vertices,
                computedIndices,
                uniforms,
                surfaceConstants,
                size);
            using ISilkGraphicsSubmission barrierSubmission =
                device.Submit(barrierCommands);
            barrierSubmission.Wait();
        }
        computedIndices.ReadbackForTesting(new byte[16]);
        color.ReadbackForTesting(pixels);
        ValidateSolidColor(pixels, 0, 0, 0, 255, "compute-written index draw");
        Console.WriteLine($"{device.Backend}: compute-written index barrier passed");
    }

    private static void RecordProbeDraw(
        ISilkGraphicsCommandList commands,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        ISilkGraphicsPipeline pipeline,
        ISilkGraphicsBuffer vertices,
        ISilkGraphicsBuffer indices,
        ISilkGraphicsBuffer uniforms,
        ISilkGraphicsBuffer surfaceConstants,
        uint size)
    {
        commands.BeginRendering(new SilkRenderingDescriptor(color, depth));
        commands.SetGraphicsPipeline(pipeline);
        commands.SetViewport(new SilkViewport(0, 0, size, size));
        commands.SetScissor(new SilkScissor(0, 0, size, size));
        commands.SetVertexBuffer(vertices);
        commands.SetIndexBuffer(indices);
        commands.SetUniformBuffer(0, 0, uniforms);
        BindAlwaysOnSlots(commands, uniforms, surfaceConstants);
        commands.DrawIndexed(3);
        commands.EndRendering();
    }

    /// <summary>
    /// Binds the slots the checked mesh shaders read on every draw: the instance
    /// table the vertex stage indexes, and the surface constants the fragment stage
    /// reads. Leaving either unbound renders correctly on D3D12, renders nothing on
    /// Metal, and faults SwiftShader.
    /// </summary>
    private static void BindAlwaysOnSlots(
        ISilkGraphicsCommandList commands,
        ISilkGraphicsBuffer uniforms,
        ISilkGraphicsBuffer surfaceConstants)
    {
        commands.SetStorageBuffer(0, 6, uniforms);
        commands.SetStorageBuffer(
            0,
            SilkBindingLayoutDescriptor.SurfaceParametersBinding,
            surfaceConstants);
    }

    /// <summary>
    /// Creates the default surface constants: no material, so the shaded flag is
    /// zero and the scene tint drives diffuse, lit by the deterministic headlight.
    /// </summary>
    private static ISilkGraphicsBuffer CreateSurfaceConstants(ISilkGraphicsDevice device)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            128,
            SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        buffer.Write(MemoryMarshal.AsBytes<float>(
        [
            0.18f, 0.18f, 0.18f, 1,
            0, 0, 0, 1,
            0, 0, 0, 1.5f,
            0, 0.5f, 0, 0,
            0, 0.01f, 0, 0,
            0, 0, 1, 1,
            1, 1, 1, 0,
            0, 0, 0, 0
        ]));
        return buffer;
    }

    private static void ValidateSolidColor(
        ReadOnlySpan<byte> pixels,
        byte red,
        byte green,
        byte blue,
        byte alpha,
        string operation)
    {
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] != red ||
                pixels[offset + 1] != green ||
                pixels[offset + 2] != blue ||
                pixels[offset + 3] != alpha)
            {
                throw new InvalidOperationException(
                    $"The ordered {operation} probe produced the wrong final pixels.");
            }
        }
    }

    private static void ValidateGreenClear(ReadOnlySpan<byte> readback)
    {
        for (int offset = 0; offset < readback.Length; offset += 4)
        {
            if (readback[offset] != 0 ||
                readback[offset + 1] != byte.MaxValue ||
                readback[offset + 2] != 0 ||
                readback[offset + 3] != byte.MaxValue)
            {
                throw new InvalidOperationException(
                    "The offscreen clear did not produce RGBA8 green pixels.");
            }
        }
    }
}
