// Copyright (c) marcschier. Licensed under the MIT License.

#pragma warning disable CA1859 // These tests intentionally exercise the public RHI contracts.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
[SupportedOSPlatform("windows")]
public sealed class D3D12PickingTests
{
    [Test]
    public async Task WarpCopiesExactRgbaTokenWithPersistentAlignedReadback()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var pipeline = (D3D12SilkPickGraphicsPipeline)
            device.CreatePickGraphicsPipeline(
                SilkPickPipelineDescriptor.CreateChecked(
                    SilkShaderBinaryFormat.Dxil));
        using var readback = (D3D12SilkPickReadbackBuffer)
            device.CreatePickReadbackBuffer();
        using ISilkGraphicsTexture color = CreateColor(device, 16, 16);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(16, 16));
        using ISilkGraphicsBuffer vertices = CreateTriangleVertices(
            device,
            0.25f);
        using ISilkGraphicsBuffer indices = CreateTriangleIndices(device);
        using ISilkGraphicsBuffer uniforms = CreateIdentityUniform(device);

        await SubmitManualPick(
            device,
            pipeline,
            readback,
            color,
            depth,
            vertices,
            indices,
            uniforms,
            baseToken: 0x12345678,
            draw: true,
            new SilkTexturePixelCoordinate(8, 8));
        var bytes = new byte[SilkPickTokenEncoding.ByteSize];
        readback.ReadRgba8Pixel(bytes);

        await Assert.That(bytes).IsEquivalentTo(
            new byte[] { 0x78, 0x56, 0x34, 0x12 });
        await Assert.That(readback.NativeByteSize)
            .IsEqualTo(D3D12SilkGraphicsDevice.PickReadbackRowPitch);
        await Assert.That(((D3D12SilkGraphicsTexture)color).State)
            .IsEqualTo(global::Silk.NET.Direct3D12.ResourceStates.RenderTarget);
        await Assert.That(((D3D12SilkGraphicsTexture)depth).State)
            .IsEqualTo(global::Silk.NET.Direct3D12.ResourceStates.DepthWrite);

        await SubmitManualPick(
            device,
            pipeline,
            readback,
            color,
            depth,
            vertices,
            indices,
            uniforms,
            baseToken: 1,
            draw: false,
            new SilkTexturePixelCoordinate(0, 0));
        readback.ReadRgba8Pixel(bytes);

        await Assert.That(bytes).IsEquivalentTo(new byte[] { 0, 0, 0, 0 });
        D3D12PickNativeStatistics statistics =
            device.PickNativeStatisticsForTesting;
        await Assert.That(statistics.PipelineCreations).IsEqualTo(1L);
        await Assert.That(statistics.ReadbackCreations).IsEqualTo(1L);
        await Assert.That(statistics.CommandAllocatorCreations).IsEqualTo(1L);
        await Assert.That(statistics.CommandListCreations).IsEqualTo(1L);
        await Assert.That(statistics.FenceCreations).IsEqualTo(1L);
        await Assert.That(statistics.Submissions).IsEqualTo(2L);
        await Assert.That(statistics.LastCoordinate)
            .IsEqualTo(new SilkTexturePixelCoordinate(0, 0));
    }

    [Test]
    public async Task WarpPicksNearestDepthMissAndPreservesVisibleRender()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const uint size = 32;
        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device, size, size);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(size, size));
        SilkMeshRendererConformance.Apply(
            renderer,
            1,
            SilkMeshRendererConformance.CreateFrameCommand(
                size,
                size,
                SilkMeshRendererConformance.Identity()),
            SilkMeshRendererConformance.CreateMeshCommand(
                1,
                "/Near",
                FullTriangle(0.2f),
                [0, 1, 2],
                topologyRevision: 1),
            SilkMeshRendererConformance.CreateMeshCommand(
                2,
                "/Far",
                FullTriangle(0.8f),
                [0, 1, 2],
                topologyRevision: 1));
        var binding = new SilkPickFrameBinding(10, 20);
        _ = renderer.Render(color, depth, binding);
        byte[] before = ReadPixels(color);

        RenderPickResult hit = await SubmitAndComplete(
            renderer,
            color,
            depth,
            binding,
            CreateRequest(16, 16, size, size, 10, 20));
        RenderPickResult miss = await SubmitAndComplete(
            renderer,
            color,
            depth,
            binding,
            CreateRequest(0, 0, size, size, 10, 20));
        byte[] after = ReadPixels(color);

        await Assert.That(hit.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(hit.PrimPath).IsEqualTo("/Near");
        await Assert.That(hit.ElementIndex).IsEqualTo(0);
        await Assert.That(hit.BackendKind).IsEqualTo(RenderBackendKind.D3D12);
        await Assert.That(hit.BackendToken).IsNotNull();
        await Assert.That(hit.WorldPosition).IsNull();
        await Assert.That(hit.WorldNormal).IsNull();
        await Assert.That(hit.NormalizedDepth).IsNull();
        await Assert.That(miss.Status).IsEqualTo(RenderPickStatus.Miss);
        await Assert.That(miss.Item).IsNull();
        await Assert.That(miss.BackendToken).IsNull();
        await Assert.That(after.SequenceEqual(before)).IsTrue();
    }

    [Test]
    public async Task WarpUsesPhysicalTopLeftCoordinatesAtAllCorners()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const uint width = 16;
        const uint height = 12;
        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device, width, height);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(width, height));
        SilkMeshRendererConformance.Apply(
            renderer,
            1,
            SilkMeshRendererConformance.CreateFrameCommand(
                width,
                height,
                SilkMeshRendererConformance.Identity()),
            CreateQuadCommand(1, "/TopLeft", -1, 1, 0, 0),
            CreateQuadCommand(2, "/TopRight", 0, 1, 1, 0),
            CreateQuadCommand(3, "/BottomLeft", -1, 0, 0, -1),
            CreateQuadCommand(4, "/BottomRight", 0, 0, 1, -1));
        var binding = new SilkPickFrameBinding(3, 4);

        (int X, int Y, string Path)[] cases =
        [
            (0, 0, "/TopLeft"),
            ((int)width - 1, 0, "/TopRight"),
            (0, (int)height - 1, "/BottomLeft"),
            ((int)width - 1, (int)height - 1, "/BottomRight")
        ];
        foreach ((int x, int y, string path) in cases)
        {
            RenderPickResult result = await SubmitAndComplete(
                renderer,
                color,
                depth,
                binding,
                CreateRequest(x, y, width, height, 3, 4));
            await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Hit);
            await Assert.That(result.PrimPath).IsEqualTo(path);
        }
    }

    [Test]
    public async Task WarpStalesTopologyStateAndPendingResize()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device, 16, 12);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(16, 12));
        ApplyCenterTriangle(
            renderer,
            16,
            12,
            topologyRevision: 1,
            pageRevision: 1);
        var firstBinding = new SilkPickFrameBinding(5, 9);
        device.SetPickCompletionsHeldForTesting(true);
        Task<RenderPickResult> topologyPending = renderer.PickAsync(
            CreateRequest(8, 6, 16, 12, 5, 9)).AsTask();
        _ = renderer.Render(color, depth, firstBinding);

        ApplyCenterTriangle(
            renderer,
            16,
            12,
            topologyRevision: 2,
            pageRevision: 2);
        device.SetPickCompletionsHeldForTesting(false);
        var secondBinding = new SilkPickFrameBinding(6, 10);
        RenderPickResult topologyStale = await CompletePending(
            renderer,
            color,
            depth,
            secondBinding,
            topologyPending);
        RenderPickResult stateStale = await SubmitAndComplete(
            renderer,
            color,
            depth,
            secondBinding,
            CreateRequest(8, 6, 16, 12, 5, 10));

        device.SetPickCompletionsHeldForTesting(true);
        Task<RenderPickResult> resizePending = renderer.PickAsync(
            CreateRequest(8, 6, 16, 12, 6, 10)).AsTask();
        _ = renderer.Render(color, depth, secondBinding);
        using ISilkGraphicsTexture resizedColor = CreateColor(device, 20, 14);
        using ISilkGraphicsTexture resizedDepth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(20, 14));
        SilkMeshRendererConformance.Apply(
            renderer,
            3,
            SilkMeshRendererConformance.CreateFrameCommand(
                20,
                14,
                SilkMeshRendererConformance.Identity()));
        device.SetPickCompletionsHeldForTesting(false);
        var resizedBinding = new SilkPickFrameBinding(7, 11);
        RenderPickResult resizeStale = await CompletePending(
            renderer,
            resizedColor,
            resizedDepth,
            resizedBinding,
            resizePending);
        RenderPickResult resizedHit = await SubmitAndComplete(
            renderer,
            resizedColor,
            resizedDepth,
            resizedBinding,
            CreateRequest(10, 7, 20, 14, 7, 11));

        await Assert.That(topologyStale.Status)
            .IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(stateStale.Status).IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(resizeStale.Status).IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(resizedHit.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(renderer.PickingStatistics.TargetCreations)
            .IsEqualTo(2UL);
    }

    [Test]
    public async Task WarpReadbackRingSaturatesAndReusesWithoutNativeChurn()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const uint size = 24;
        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device, size, size);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(size, size));
        ApplyCenterTriangle(
            renderer,
            size,
            size,
            topologyRevision: 1,
            pageRevision: 1);
        var binding = new SilkPickFrameBinding(1, 2);
        device.SetPickCompletionsHeldForTesting(true);
        var pending = new List<Task<RenderPickResult>>();

        for (int index = 0; index < 3; index++)
        {
            pending.Add(renderer.PickAsync(
                CreateRequest(12, 12, size, size, 1, 2)).AsTask());
            _ = renderer.Render(color, depth, binding);
        }
        pending.Add(renderer.PickAsync(
            CreateRequest(12, 12, size, size, 1, 2)).AsTask());
        _ = renderer.Render(color, depth, binding);

        D3D12PickNativeStatistics saturated =
            device.PickNativeStatisticsForTesting;
        await Assert.That(saturated.Submissions).IsEqualTo(3L);
        await Assert.That(saturated.ReadbackCreations).IsEqualTo(3L);
        await Assert.That(saturated.CommandAllocatorCreations).IsEqualTo(3L);
        await Assert.That(saturated.CommandListCreations).IsEqualTo(3L);
        await Assert.That(saturated.FenceCreations).IsEqualTo(3L);
        await Assert.That(renderer.PickingStatistics.RingSaturations)
            .IsEqualTo(1UL);

        device.SetPickCompletionsHeldForTesting(false);
        await CompleteAll(renderer, color, depth, binding, pending);
        foreach (Task<RenderPickResult> task in pending)
        {
            await Assert.That((await task).Status)
                .IsEqualTo(RenderPickStatus.Hit);
        }

        D3D12PickNativeStatistics reused =
            device.PickNativeStatisticsForTesting;
        await Assert.That(reused.Submissions).IsEqualTo(4L);
        AssertPersistentResourcesUnchanged(saturated, reused);

        for (ulong revision = 3; revision <= 14; revision++)
        {
            RenderPickResult result = await SubmitAndComplete(
                renderer,
                color,
                depth,
                new SilkPickFrameBinding(revision, 2),
                CreateRequest(12, 12, size, size, revision, 2));
            if (result.Status != RenderPickStatus.Hit)
            {
                throw new InvalidOperationException(
                    "A warmed D3D12 pick did not resolve a hit.");
            }
        }
        D3D12PickNativeStatistics steady =
            device.PickNativeStatisticsForTesting;
        AssertPersistentResourcesUnchanged(reused, steady);
        await Assert.That(steady.Submissions).IsEqualTo(16L);
    }

    [Test]
    public async Task WarpFailureAndDeviceLossInvalidateAndRecover()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const uint size = 24;
        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColor(device, size, size);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(size, size));
        ApplyCenterTriangle(
            renderer,
            size,
            size,
            topologyRevision: 1,
            pageRevision: 1);
        var firstBinding = new SilkPickFrameBinding(1, 1);

        device.InjectPickFailureForTesting(D3D12PickFailure.CopyFailure);
        Task<RenderPickResult> failed = renderer.PickAsync(
            CreateRequest(12, 12, size, size, 1, 1)).AsTask();
        await Assert.That(() => renderer.Render(color, depth, firstBinding))
            .Throws<InvalidOperationException>();
        await Assert.That(async () => await failed)
            .Throws<InvalidOperationException>();
        RenderPickResult recovered = await SubmitAndComplete(
            renderer,
            color,
            depth,
            firstBinding,
            CreateRequest(12, 12, size, size, 1, 1));
        await Assert.That(recovered.Status).IsEqualTo(RenderPickStatus.Hit);

        ulong removedGeneration = device.PickDeviceGeneration;
        device.InjectPickFailureForTesting(D3D12PickFailure.DeviceRemoved);
        Task<RenderPickResult> removed = renderer.PickAsync(
            CreateRequest(12, 12, size, size, 1, 1)).AsTask();
        await Assert.That(() => renderer.Render(color, depth, firstBinding))
            .Throws<D3D12PickDeviceLostException>();
        await Assert.That(async () => await removed)
            .Throws<D3D12PickDeviceLostException>();
        await Assert.That(device.PickDeviceGeneration)
            .IsGreaterThan(removedGeneration);

        var secondBinding = new SilkPickFrameBinding(2, 2);
        RenderPickResult afterRemoval = await SubmitAndComplete(
            renderer,
            color,
            depth,
            secondBinding,
            CreateRequest(12, 12, size, size, 2, 2));
        await Assert.That(afterRemoval.Status).IsEqualTo(RenderPickStatus.Hit);

        ulong resetGeneration = device.PickDeviceGeneration;
        device.InjectPickFailureForTesting(D3D12PickFailure.DeviceReset);
        Task<RenderPickResult> reset = renderer.PickAsync(
            CreateRequest(12, 12, size, size, 2, 2)).AsTask();
        await Assert.That(() => renderer.Render(color, depth, secondBinding))
            .Throws<D3D12PickDeviceLostException>();
        await Assert.That(async () => await reset)
            .Throws<D3D12PickDeviceLostException>();
        await Assert.That(device.PickDeviceGeneration)
            .IsGreaterThan(resetGeneration);

        var thirdBinding = new SilkPickFrameBinding(3, 3);
        RenderPickResult afterReset = await SubmitAndComplete(
            renderer,
            color,
            depth,
            thirdBinding,
            CreateRequest(12, 12, size, size, 3, 3));
        D3D12PickNativeStatistics statistics =
            device.PickNativeStatisticsForTesting;
        await Assert.That(afterReset.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(statistics.PipelineCreations).IsEqualTo(3L);
        await Assert.That(statistics.ReadbackCreations).IsEqualTo(9L);
        await Assert.That(statistics.CommandAllocatorCreations).IsEqualTo(9L);
        await Assert.That(statistics.CommandListCreations).IsEqualTo(9L);
        await Assert.That(statistics.FenceCreations).IsEqualTo(9L);
    }

    [Test]
    public async Task CompositionCallbackServicesPickWithoutChangingPresentation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const uint size = 16;
        using D3D12SilkGraphicsDevice device =
            D3D12SilkGraphicsDevice.Create(useWarp: true);
        using var callback = new PickingPresentationRenderer(device, size);
        await using var presenter =
            new D3D12CompositionViewportPresenter(device, callback);
        CompositionPresenterProbeResult probe = await presenter.ProbeAsync(
            new CompositionPresentationTarget(
                [D3D12CompositionViewportPresenter.D3D11TextureNtHandle],
                [],
                presenter.RendererAdapterLuid.ToArray(),
                null));
        await Assert.That(probe.IsAvailable).IsTrue();
        await using ICompositionPresentationGeneration generation =
            await presenter.CreateGenerationAsync(
                new ViewportDimensions((int)size, (int)size),
                2);
        var frame = (D3D12CompositionPresentationFrame)generation.Frames[0];
        Task<RenderPickResult> pending = callback.PickAsync(
            CreateRequest(8, 8, size, size, 11, 12));

        for (int iteration = 0;
             iteration < 20 && !pending.IsCompleted;
             iteration++)
        {
            CompositionFrameRenderResult result = await presenter.RenderAsync(frame);
            await Assert.That(result.Status)
                .IsEqualTo(CompositionFrameRenderStatus.Presented);
            frame.SimulateConsumerReleaseForTesting(1, 0);
        }
        RenderPickResult pick = await pending;

        await Assert.That(pick.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(pick.PrimPath).IsEqualTo("/Triangle");
        await Assert.That(callback.RenderCount).IsGreaterThanOrEqualTo(1);
        await Assert.That(presenter.GetStatistics().SilkRenderedFrameCount)
            .IsEqualTo(callback.RenderCount);
    }

    private static ISilkGraphicsTexture CreateColor(
        D3D12SilkGraphicsDevice device,
        uint width,
        uint height) =>
        device.CreateTexture2D(new SilkTextureDescriptor(
            width,
            height,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget |
                SilkTextureUsage.CopySource));

    private static ISilkGraphicsBuffer CreateTriangleVertices(
        D3D12SilkGraphicsDevice device,
        float depth)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            72,
            SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
        buffer.Write(MemoryMarshal.AsBytes<float>(
        [
            -1, -1, depth, 0, 0, 1,
            -1,  3, depth, 0, 0, 1,
             3, -1, depth, 0, 0, 1
        ]));
        return buffer;
    }

    private static ISilkGraphicsBuffer CreateTriangleIndices(
        D3D12SilkGraphicsDevice device)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            12,
            SilkBufferUsage.Index | SilkBufferUsage.Upload);
        buffer.Write(MemoryMarshal.AsBytes<uint>([0, 1, 2]));
        return buffer;
    }

    private static ISilkGraphicsBuffer CreateIdentityUniform(
        D3D12SilkGraphicsDevice device)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            80,
            SilkBufferUsage.Uniform | SilkBufferUsage.Upload);
        buffer.Write(MemoryMarshal.AsBytes<float>(
        [
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1,
            1, 1, 1, 1
        ]));
        return buffer;
    }

    private static async Task SubmitManualPick(
        D3D12SilkGraphicsDevice device,
        ISilkPickGraphicsPipeline pipeline,
        ISilkPickReadbackBuffer readback,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        ISilkGraphicsBuffer vertices,
        ISilkGraphicsBuffer indices,
        ISilkGraphicsBuffer uniforms,
        uint baseToken,
        bool draw,
        SilkTexturePixelCoordinate coordinate)
    {
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        var pickCommands = (ISilkPickGraphicsCommandList)commands;
        commands.ClearColor(color, new SilkColor(0, 0, 0, 0));
        commands.ClearDepth(depth, 1);
        commands.BeginRendering(new SilkRenderingDescriptor(color, depth));
        pickCommands.SetPickGraphicsPipeline(pipeline);
        commands.SetViewport(new SilkViewport(
            0,
            0,
            color.Width,
            color.Height));
        commands.SetScissor(new SilkScissor(
            checked((int)coordinate.X),
            checked((int)coordinate.Y),
            1,
            1));
        if (draw)
        {
            commands.SetVertexBuffer(vertices);
            commands.SetIndexBuffer(indices);
            commands.SetUniformBuffer(0, 0, uniforms);
            pickCommands.SetPickBaseToken(baseToken);
            commands.DrawIndexed(3);
        }
        commands.EndRendering();
        pickCommands.CopyRgba8Pixel(color, coordinate, readback);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();
        await Assert.That(submission.IsCompleted).IsTrue();
    }

    private static async Task<RenderPickResult> SubmitAndComplete(
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        SilkPickFrameBinding binding,
        RenderPickRequest request)
    {
        Task<RenderPickResult> pending = renderer.PickAsync(request).AsTask();
        _ = renderer.Render(color, depth, binding);
        return await CompletePending(
            renderer,
            color,
            depth,
            binding,
            pending);
    }

    private static async Task<RenderPickResult> CompletePending(
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        SilkPickFrameBinding binding,
        Task<RenderPickResult> pending)
    {
        for (int iteration = 0;
             iteration < 100 && !pending.IsCompleted;
             iteration++)
        {
            _ = renderer.Render(color, depth, binding);
            await Task.Yield();
        }
        if (!pending.IsCompleted)
        {
            throw new TimeoutException(
                "The WARP pick did not complete without a render-loop wait.");
        }
        return await pending;
    }

    private static async Task CompleteAll(
        SilkMeshRenderer renderer,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        SilkPickFrameBinding binding,
        IReadOnlyList<Task<RenderPickResult>> pending)
    {
        for (int iteration = 0;
             iteration < 100 && pending.Any(static task => !task.IsCompleted);
             iteration++)
        {
            _ = renderer.Render(color, depth, binding);
            await Task.Yield();
        }
        if (pending.Any(static task => !task.IsCompleted))
        {
            throw new TimeoutException(
                "The saturated WARP pick ring did not drain.");
        }
    }

    private static void AssertPersistentResourcesUnchanged(
        D3D12PickNativeStatistics before,
        D3D12PickNativeStatistics after)
    {
        if (after.PipelineCreations != before.PipelineCreations ||
            after.ReadbackCreations != before.ReadbackCreations ||
            after.CommandAllocatorCreations != before.CommandAllocatorCreations ||
            after.CommandListCreations != before.CommandListCreations ||
            after.FenceCreations != before.FenceCreations)
        {
            throw new InvalidOperationException(
                "Warmed D3D12 picking created new persistent native resources.");
        }
    }

    private static RenderPickRequest CreateRequest(
        int x,
        int y,
        uint width,
        uint height,
        ulong stateRevision,
        ulong? sceneRevision) =>
        new(
            x,
            y,
            new ViewportDimensions(
                checked((int)width),
                checked((int)height)),
            stateRevision,
            sceneRevision,
            RenderPickTarget.Face);

    private static byte[] ReadPixels(ISilkGraphicsTexture texture)
    {
        var pixels = new byte[
            checked((int)(texture.Width * texture.Height * 4))];
        texture.ReadbackForTesting(pixels);
        return pixels;
    }

    private static void ApplyCenterTriangle(
        SilkMeshRenderer renderer,
        uint width,
        uint height,
        ulong topologyRevision,
        ulong pageRevision) =>
        SilkMeshRendererConformance.Apply(
            renderer,
            pageRevision,
            SilkMeshRendererConformance.CreateFrameCommand(
                width,
                height,
                SilkMeshRendererConformance.Identity()),
            SilkMeshRendererConformance.CreateMeshCommand(
                1,
                "/Triangle",
                [
                    -0.6f, -0.6f, 0.2f,
                     0.0f,  0.6f, 0.2f,
                     0.6f, -0.6f, 0.2f
                ],
                [0, 1, 2],
                topologyRevision: topologyRevision));

    private static float[] FullTriangle(float depth) =>
    [
        -0.9f, -0.9f, depth,
         0.0f,  0.9f, depth,
         0.9f, -0.9f, depth
    ];

    private static byte[] CreateQuadCommand(
        ulong id,
        string path,
        float left,
        float top,
        float right,
        float bottom) =>
        SilkMeshRendererConformance.CreateMeshCommand(
            id,
            path,
            [
                left, top, 0.25f,
                right, top, 0.25f,
                right, bottom, 0.25f,
                left, bottom, 0.25f
            ],
            [0, 1, 2, 0, 2, 3],
            topologyRevision: 1);


    [SupportedOSPlatform("windows")]
    private sealed class PickingPresentationRenderer : ISilkPresentationRenderer, IDisposable
    {
        private readonly SilkMeshRenderer _renderer;
        private readonly SilkPickFrameBinding _binding = new(11, 12);

        internal PickingPresentationRenderer(
            D3D12SilkGraphicsDevice device,
            uint size)
        {
            _renderer = new SilkMeshRenderer(device);
            ApplyCenterTriangle(
                _renderer,
                size,
                size,
                topologyRevision: 1,
                pageRevision: 1);
        }

        internal long RenderCount { get; private set; }

        internal Task<RenderPickResult> PickAsync(RenderPickRequest request) =>
            _renderer.PickAsync(request).AsTask();

        public SilkPresentationRenderResult Render(
            ISilkGraphicsTexture colorTarget,
            ISilkGraphicsTexture depthTarget,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RenderCount++;
            SilkMeshRenderResult result = _renderer.Render(
                colorTarget,
                depthTarget,
                _binding);
            return new SilkPresentationRenderResult(
                _binding.StateRevision,
                result.DrawCount);
        }

        public void Dispose() => _renderer.Dispose();
    }
}
