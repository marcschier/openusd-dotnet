// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class VulkanPickingTests
{
    [Test]
    public async Task SwiftShaderCopiesExactRgbaTokenAndTokenZeroMiss()
    {
        if (!IsSupportedPlatform())
        {
            return;
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        using ISilkPickGraphicsPipeline pipeline =
            device.CreatePickGraphicsPipeline(
                SilkPickPipelineDescriptor.CreateChecked(
                    SilkShaderBinaryFormat.SpirV));
        using ISilkPickReadbackBuffer readback =
            device.CreatePickReadbackBuffer();
        using ISilkGraphicsTexture color = CreatePickColor(device, 16, 16);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(16, 16));
        using ISilkGraphicsBuffer vertices = CreateTriangleVertices(device, 0.25f);
        using ISilkGraphicsBuffer indices = CreateTriangleIndices(device);
        using ISilkGraphicsBuffer uniforms = CreateIdentityUniform(device);

        byte[] bytes = await SubmitManualPick(
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
            indexCount: 3,
            coordinate: new SilkTexturePixelCoordinate(8, 8));
        await Assert.That(bytes.SequenceEqual(
                new byte[] { 0x78, 0x56, 0x34, 0x12 }))
            .IsTrue()
            .Because($"Read {Convert.ToHexString(bytes)}.");

        bytes = await SubmitManualPick(
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
            indexCount: 3,
            coordinate: new SilkTexturePixelCoordinate(8, 8));
        await Assert.That(bytes.SequenceEqual(new byte[] { 0, 0, 0, 0 }))
            .IsTrue();
    }

    [Test]
    public async Task SwiftShaderReplayAddsThePrimitiveTokenOffset()
    {
        if (!IsSupportedPlatform())
        {
            return;
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        using ISilkPickGraphicsPipeline pipeline =
            device.CreatePickGraphicsPipeline(
                SilkPickPipelineDescriptor.CreateChecked(
                    SilkShaderBinaryFormat.SpirV));
        using ISilkPickReadbackBuffer readback =
            device.CreatePickReadbackBuffer();
        using ISilkGraphicsTexture color = CreatePickColor(device, 16, 16);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(16, 16));
        using ISilkGraphicsBuffer vertices = CreateQuadVertices(device);
        using ISilkGraphicsBuffer indices = CreateQuadIndices(device);
        using ISilkGraphicsBuffer uniforms = CreateIdentityUniform(device);

        byte[] first = await SubmitManualPick(
            device,
            pipeline,
            readback,
            color,
            depth,
            vertices,
            indices,
            uniforms,
            baseToken: 0x01020304,
            draw: true,
            indexCount: 6,
            coordinate: new SilkTexturePixelCoordinate(13, 2));
        byte[] second = await SubmitManualPick(
            device,
            pipeline,
            readback,
            color,
            depth,
            vertices,
            indices,
            uniforms,
            baseToken: 0x01020304,
            draw: true,
            indexCount: 6,
            coordinate: new SilkTexturePixelCoordinate(2, 13));

        await Assert.That(SilkPickTokenEncoding.Decode(first))
            .IsEqualTo(0x01020304U);
        await Assert.That(SilkPickTokenEncoding.Decode(second))
            .IsEqualTo(0x01020305U);
    }

    [Test]
    public async Task SwiftShaderPicksNearestDepthAndLeavesVisibleRenderUnchanged()
    {
        if (!IsSupportedPlatform())
        {
            return;
        }

        const uint size = 32;
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateVisibleColor(device, size, size);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(size, size));
        SilkMeshRendererConformance.Apply(
            renderer,
            1,
            SilkMeshRendererConformance.CreateFrameCommand(
                size,
                size,
                SilkMeshRendererConformance.Identity()),
            CreateMeshCommand(
                1,
                "/Near",
                FullTriangle(0.2f),
                [0, 1, 2],
                topologyRevision: 1),
            CreateMeshCommand(
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
        byte[] after = ReadPixels(color);

        await Assert.That(hit.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(hit.PrimPath).IsEqualTo("/Near");
        await Assert.That(hit.WorldPosition).IsNull();
        await Assert.That(hit.WorldNormal).IsNull();
        await Assert.That(hit.NormalizedDepth).IsNull();
        await Assert.That(hit.BackendToken).IsNotNull();
        await Assert.That(after.SequenceEqual(before)).IsTrue();
    }

    [Test]
    public async Task SwiftShaderUsesPhysicalTopLeftCoordinatesAtAllCorners()
    {
        if (!IsSupportedPlatform())
        {
            return;
        }

        const uint width = 16;
        const uint height = 12;
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateVisibleColor(device, width, height);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(width, height));
        SilkMeshRendererConformance.Apply(
            renderer,
            1,
            SilkMeshRendererConformance.CreateFrameCommand(
                width,
                height,
                SilkMeshRendererConformance.Identity()),
            CreateQuadCommand(1, "/TopLeft", -1, -1, 0, 0),
            CreateQuadCommand(2, "/TopRight", 0, -1, 1, 0),
            CreateQuadCommand(3, "/BottomLeft", -1, 0, 0, 1),
            CreateQuadCommand(4, "/BottomRight", 0, 0, 1, 1));
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
    public async Task SwiftShaderReturnsMissAndStalesTopologyAndState()
    {
        if (!IsSupportedPlatform())
        {
            return;
        }

        const uint size = 24;
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateVisibleColor(device, size, size);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(size, size));
        ApplyCenterTriangle(renderer, size, topologyRevision: 1, pageRevision: 1);
        var firstBinding = new SilkPickFrameBinding(5, 9);

        RenderPickResult miss = await SubmitAndComplete(
            renderer,
            color,
            depth,
            firstBinding,
            CreateRequest(1, 1, size, size, 5, 9));
        await Assert.That(miss.Status).IsEqualTo(RenderPickStatus.Miss);
        await Assert.That(miss.Item).IsNull();
        await Assert.That(miss.BackendToken).IsNull();

        device.SetPickCompletionsSuppressedForTesting(true);
        Task<RenderPickResult> pending = renderer.PickAsync(
            CreateRequest(12, 12, size, size, 5, 9)).AsTask();
        _ = renderer.Render(color, depth, firstBinding);
        await Assert.That(pending.IsCompleted).IsFalse();

        ApplyCenterTriangle(renderer, size, topologyRevision: 2, pageRevision: 2);
        device.SetPickCompletionsSuppressedForTesting(false);
        var secondBinding = new SilkPickFrameBinding(6, 10);
        _ = renderer.Render(color, depth, secondBinding);
        RenderPickResult stale = await CompletePending(
            renderer,
            color,
            depth,
            secondBinding,
            pending);

        await Assert.That(stale.Status).IsEqualTo(RenderPickStatus.Stale);

        ValueTask<RenderPickResult> statePending = renderer.PickAsync(
            CreateRequest(12, 12, size, size, 5, 10));
        _ = renderer.Render(color, depth, secondBinding);
        RenderPickResult stateStale = await statePending;
        await Assert.That(stateStale.Status).IsEqualTo(RenderPickStatus.Stale);
    }

    [Test]
    public async Task SwiftShaderReadbackRingSaturatesThenReusesPersistentSlots()
    {
        if (!IsSupportedPlatform())
        {
            return;
        }

        const uint size = 24;
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateVisibleColor(device, size, size);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(size, size));
        ApplyCenterTriangle(renderer, size, topologyRevision: 1, pageRevision: 1);
        var binding = new SilkPickFrameBinding(1, 2);
        device.SetPickCompletionsSuppressedForTesting(true);
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

        VulkanSilkPickingDiagnostics saturated = device.PickDiagnostics;
        await Assert.That(saturated.Submissions).IsEqualTo(3L);
        await Assert.That(saturated.ReadbackBufferCreations).IsEqualTo(3L);
        await Assert.That(saturated.CommandPoolCreations).IsEqualTo(3L);
        await Assert.That(saturated.FenceCreations).IsEqualTo(3L);
        await Assert.That(renderer.PickingStatistics.RingSaturations)
            .IsEqualTo(1UL);

        device.SetPickCompletionsSuppressedForTesting(false);
        for (int iteration = 0;
             iteration < 100 && pending.Any(task => !task.IsCompleted);
             iteration++)
        {
            _ = renderer.Render(color, depth, binding);
            await Task.Yield();
        }
        foreach (Task<RenderPickResult> task in pending)
        {
            await Assert.That((await task).Status)
                .IsEqualTo(RenderPickStatus.Hit);
        }

        VulkanSilkPickingDiagnostics reused = device.PickDiagnostics;
        await Assert.That(reused.Submissions).IsEqualTo(4L);
        await Assert.That(reused.ReadbackBufferCreations)
            .IsEqualTo(saturated.ReadbackBufferCreations);
        await Assert.That(reused.CommandPoolCreations)
            .IsEqualTo(saturated.CommandPoolCreations);
        await Assert.That(reused.FenceCreations)
            .IsEqualTo(saturated.FenceCreations);
        await Assert.That(reused.FramebufferCreations).IsEqualTo(3L);
        await Assert.That(reused.DescriptorPoolCreations).IsEqualTo(3L);
        await Assert.That(reused.UniformBufferCreations).IsEqualTo(3L);
        await Assert.That(reused.SecondaryCommandRecordings).IsEqualTo(3L);
    }

    [Test]
    public async Task SwiftShaderResizeInvalidatesPendingAndRecreatesTargets()
    {
        if (!IsSupportedPlatform())
        {
            return;
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateVisibleColor(device, 16, 12);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(16, 12));
        ApplyCenterTriangle(renderer, 16, topologyRevision: 1, pageRevision: 1);
        var firstBinding = new SilkPickFrameBinding(1, 1);
        device.SetPickCompletionsSuppressedForTesting(true);
        Task<RenderPickResult> oldPending = renderer.PickAsync(
            CreateRequest(8, 6, 16, 12, 1, 1)).AsTask();
        _ = renderer.Render(color, depth, firstBinding);

        using ISilkGraphicsTexture resizedColor = CreateVisibleColor(
            device,
            20,
            14);
        using ISilkGraphicsTexture resizedDepth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(20, 14));
        SilkMeshRendererConformance.Apply(
            renderer,
            2,
            SilkMeshRendererConformance.CreateFrameCommand(
                20,
                14,
                SilkMeshRendererConformance.Identity()));
        Task<RenderPickResult> resizedPending = renderer.PickAsync(
            CreateRequest(10, 7, 20, 14, 2, 2)).AsTask();
        device.SetPickCompletionsSuppressedForTesting(false);
        var resizedBinding = new SilkPickFrameBinding(2, 2);
        _ = renderer.Render(resizedColor, resizedDepth, resizedBinding);

        RenderPickResult oldResult = await CompletePending(
            renderer,
            resizedColor,
            resizedDepth,
            resizedBinding,
            oldPending);
        RenderPickResult resizedResult = await CompletePending(
            renderer,
            resizedColor,
            resizedDepth,
            resizedBinding,
            resizedPending);

        await Assert.That(oldResult.Status).IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(resizedResult.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(renderer.PickingStatistics.TargetCreations)
            .IsEqualTo(2UL);
    }

    [Test]
    public async Task SwiftShaderSubmissionFailureAndDeviceLossInvalidateGeneration()
    {
        if (!IsSupportedPlatform())
        {
            return;
        }

        const uint size = 24;
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateVisibleColor(device, size, size);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(size, size));
        ApplyCenterTriangle(renderer, size, topologyRevision: 1, pageRevision: 1);
        var firstBinding = new SilkPickFrameBinding(1, 1);

        device.FailNextPickSubmissionForTesting(deviceLost: false);
        Task<RenderPickResult> failed = renderer.PickAsync(
            CreateRequest(12, 12, size, size, 1, 1)).AsTask();
        await Assert.That(() => renderer.Render(color, depth, firstBinding))
            .Throws<InvalidOperationException>();
        await Assert.That(async () => await failed)
            .Throws<InvalidOperationException>();
        await Assert.That(device.PickDiagnostics.Submissions).IsEqualTo(0L);

        RenderPickResult recovered = await SubmitAndComplete(
            renderer,
            color,
            depth,
            firstBinding,
            CreateRequest(12, 12, size, size, 1, 1));
        await Assert.That(recovered.Status).IsEqualTo(RenderPickStatus.Hit);

        ulong generation = device.PickDeviceGeneration;
        device.FailNextPickSubmissionForTesting(deviceLost: true);
        Task<RenderPickResult> lost = renderer.PickAsync(
            CreateRequest(12, 12, size, size, 1, 1)).AsTask();
        await Assert.That(() => renderer.Render(color, depth, firstBinding))
            .Throws<InvalidOperationException>();
        await Assert.That(async () => await lost)
            .Throws<InvalidOperationException>();
        await Assert.That(device.PickDeviceGeneration)
            .IsGreaterThan(generation);

        var nextBinding = new SilkPickFrameBinding(2, 2);
        _ = renderer.Render(color, depth, nextBinding);
        VulkanSilkPickingDiagnostics invalidated = device.PickDiagnostics;
        await Assert.That(invalidated.PipelineCreations).IsEqualTo(2L);
        await Assert.That(invalidated.ReadbackBufferCreations).IsEqualTo(6L);
        await Assert.That(invalidated.DeviceLosses).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SwiftShaderFenceFailureStillCleansEveryReadbackSlot(
        bool deviceLost)
    {
        if (!IsSupportedPlatform())
        {
            return;
        }

        const uint size = 16;
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        ISilkPickGraphicsPipeline pipeline =
            device.CreatePickGraphicsPipeline(
                SilkPickPipelineDescriptor.CreateChecked(
                    SilkShaderBinaryFormat.SpirV));
        VulkanSilkPickingDiagnostics baseline = device.PickDiagnostics;
        var ring = new SilkPickReadbackRing(device);
        using ISilkGraphicsTexture color = CreatePickColor(device, size, size);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(size, size));
        using ISilkGraphicsBuffer vertices = CreateTriangleVertices(device, 0.25f);
        using ISilkGraphicsBuffer indices = CreateTriangleIndices(device);
        using ISilkGraphicsBuffer uniforms = CreateIdentityUniform(device);
        var context = new SilkPickReadbackContext(
            CreateRequest(8, 8, size, size, 1, 1),
            1,
            1,
            1,
            device.PickDeviceGeneration,
            new ViewportDimensions((int)size, (int)size));

        try
        {
            for (int index = 0; index < 3; index++)
            {
                if (!ring.TryAcquire(
                        out SilkPickReadbackReservation reservation))
                {
                    throw new InvalidOperationException(
                        "The persistent Vulkan readback ring saturated early.");
                }
                ISilkGraphicsSubmission submission = SubmitManualPickPending(
                    device,
                    pipeline,
                    reservation.Buffer,
                    color,
                    depth,
                    vertices,
                    indices,
                    uniforms,
                    baseToken: checked((uint)index + 1),
                    indexCount: 3,
                    coordinate: new SilkTexturePixelCoordinate(8, 8));
                ring.Commit(reservation, submission, context);
            }
            device.WaitIdle();

            VulkanSilkPickingDiagnostics submitted = device.PickDiagnostics;
            await Assert.That(submitted.LiveBuffers).IsEqualTo(6L);
            await Assert.That(submitted.LiveCommandPools).IsEqualTo(3L);
            await Assert.That(submitted.LiveFences).IsEqualTo(3L);
            await Assert.That(submitted.LiveMappings).IsEqualTo(6L);
            await Assert.That(submitted.LiveDependentObjects)
                .IsEqualTo(baseline.LiveDependentObjects + 3);

            device.FailNextPickFenceForTesting(deviceLost);
            await Assert.That(() => ring.TryReadCompleted(out _))
                .Throws<InvalidOperationException>();

            ring.Dispose();
            ring.Dispose();

            VulkanSilkPickingDiagnostics cleaned = device.PickDiagnostics;
            await Assert.That(cleaned.LiveBuffers)
                .IsEqualTo(baseline.LiveBuffers);
            await Assert.That(cleaned.LiveCommandPools)
                .IsEqualTo(baseline.LiveCommandPools);
            await Assert.That(cleaned.LiveFences)
                .IsEqualTo(baseline.LiveFences);
            await Assert.That(cleaned.LiveMappings)
                .IsEqualTo(baseline.LiveMappings);
            await Assert.That(cleaned.LiveDependentObjects)
                .IsEqualTo(baseline.LiveDependentObjects);
            await Assert.That(cleaned.DeviceLosses)
                .IsEqualTo(
                    baseline.DeviceLosses +
                    (deviceLost ? 1 : 0));
            await Assert.That(cleaned.FenceWaitCalls)
                .IsEqualTo(
                    submitted.FenceWaitCalls +
                    (deviceLost ? 0 : 2));
        }
        finally
        {
            ring.Dispose();
            pipeline.Dispose();
        }

        await Assert.That(device.PickDiagnostics.LiveDependentObjects)
            .IsEqualTo(0L);
    }

    [Test]
    public async Task SwiftShaderWarmPickingHasZeroNativeResourceChurn()
    {
        if (!IsSupportedPlatform())
        {
            return;
        }

        const uint size = 24;
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateVisibleColor(device, size, size);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(size, size));
        ApplyCenterTriangle(renderer, size, topologyRevision: 1, pageRevision: 1);

        for (ulong revision = 1; revision <= 3; revision++)
        {
            _ = await SubmitAndComplete(
                renderer,
                color,
                depth,
                new SilkPickFrameBinding(revision, 1),
                CreateRequest(12, 12, size, size, revision, 1));
        }
        VulkanSilkPickingDiagnostics warm = device.PickDiagnostics;

        for (ulong revision = 4; revision <= 15; revision++)
        {
            RenderPickResult result = await SubmitAndComplete(
                renderer,
                color,
                depth,
                new SilkPickFrameBinding(revision, 1),
                CreateRequest(12, 12, size, size, revision, 1));
            if (result.Status != RenderPickStatus.Hit)
            {
                throw new InvalidOperationException(
                    "A warmed SwiftShader pick did not resolve a hit.");
            }
        }
        VulkanSilkPickingDiagnostics steady = device.PickDiagnostics;

        await Assert.That(steady.PipelineCreations)
            .IsEqualTo(warm.PipelineCreations);
        await Assert.That(steady.ShaderModuleCreations)
            .IsEqualTo(warm.ShaderModuleCreations);
        await Assert.That(steady.ReadbackBufferCreations)
            .IsEqualTo(warm.ReadbackBufferCreations);
        await Assert.That(steady.CommandPoolCreations)
            .IsEqualTo(warm.CommandPoolCreations);
        await Assert.That(steady.FenceCreations)
            .IsEqualTo(warm.FenceCreations);
        await Assert.That(steady.FramebufferCreations)
            .IsEqualTo(warm.FramebufferCreations);
        await Assert.That(steady.DescriptorPoolCreations)
            .IsEqualTo(warm.DescriptorPoolCreations);
        await Assert.That(steady.UniformBufferCreations)
            .IsEqualTo(warm.UniformBufferCreations);
        await Assert.That(steady.SecondaryCommandRecordings)
            .IsEqualTo(warm.SecondaryCommandRecordings);
        await Assert.That(steady.Submissions).IsEqualTo(15L);
    }

    private static bool IsSupportedPlatform() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    private static ISilkGraphicsTexture CreateVisibleColor(
        VulkanSilkGraphicsDevice device,
        uint width,
        uint height) =>
        device.CreateTexture2D(new SilkTextureDescriptor(
            width,
            height,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget |
                SilkTextureUsage.CopySource));

    private static ISilkGraphicsTexture CreatePickColor(
        VulkanSilkGraphicsDevice device,
        uint width,
        uint height) =>
        CreateVisibleColor(device, width, height);

    private static ISilkGraphicsBuffer CreateTriangleVertices(
        VulkanSilkGraphicsDevice device,
        float depth)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            72,
            SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
        buffer.Write(MemoryMarshal.AsBytes<float>(
        [
            -0.9f, -0.9f, depth, 0, 0, 1,
             0.0f,  0.9f, depth, 0, 0, 1,
             0.9f, -0.9f, depth, 0, 0, 1
        ]));
        return buffer;
    }

    private static ISilkGraphicsBuffer CreateTriangleIndices(
        VulkanSilkGraphicsDevice device)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            12,
            SilkBufferUsage.Index | SilkBufferUsage.Upload);
        buffer.Write(MemoryMarshal.AsBytes<uint>([0, 1, 2]));
        return buffer;
    }

    private static ISilkGraphicsBuffer CreateQuadVertices(
        VulkanSilkGraphicsDevice device)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            96,
            SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
        buffer.Write(MemoryMarshal.AsBytes<float>(
        [
            -0.9f, -0.9f, 0.25f, 0, 0, 1,
             0.9f, -0.9f, 0.25f, 0, 0, 1,
             0.9f,  0.9f, 0.25f, 0, 0, 1,
            -0.9f,  0.9f, 0.25f, 0, 0, 1
        ]));
        return buffer;
    }

    private static ISilkGraphicsBuffer CreateQuadIndices(
        VulkanSilkGraphicsDevice device)
    {
        ISilkGraphicsBuffer buffer = device.CreateBuffer(
            24,
            SilkBufferUsage.Index | SilkBufferUsage.Upload);
        buffer.Write(MemoryMarshal.AsBytes<uint>(
            [0, 2, 1, 0, 3, 2]));
        return buffer;
    }

    private static ISilkGraphicsBuffer CreateIdentityUniform(
        VulkanSilkGraphicsDevice device)
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

    private static async Task<byte[]> SubmitManualPick(
        VulkanSilkGraphicsDevice device,
        ISilkPickGraphicsPipeline pipeline,
        ISilkPickReadbackBuffer readback,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        ISilkGraphicsBuffer vertices,
        ISilkGraphicsBuffer indices,
        ISilkGraphicsBuffer uniforms,
        uint baseToken,
        bool draw,
        uint indexCount,
        SilkTexturePixelCoordinate coordinate)
    {
        using ISilkGraphicsSubmission submission = SubmitManualPickPending(
            device,
            pipeline,
            readback,
            color,
            depth,
            vertices,
            indices,
            uniforms,
            baseToken,
            indexCount,
            coordinate,
            draw);
        submission.Wait();
        await Assert.That(submission.IsCompleted).IsTrue();
        var bytes = new byte[SilkPickTokenEncoding.ByteSize];
        readback.ReadRgba8Pixel(bytes);
        return bytes;
    }

    private static ISilkGraphicsSubmission SubmitManualPickPending(
        VulkanSilkGraphicsDevice device,
        ISilkPickGraphicsPipeline pipeline,
        ISilkPickReadbackBuffer readback,
        ISilkGraphicsTexture color,
        ISilkGraphicsTexture depth,
        ISilkGraphicsBuffer vertices,
        ISilkGraphicsBuffer indices,
        ISilkGraphicsBuffer uniforms,
        uint baseToken,
        uint indexCount,
        SilkTexturePixelCoordinate coordinate,
        bool draw = true)
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
            commands.DrawIndexed(indexCount);
        }
        commands.EndRendering();
        pickCommands.CopyRgba8Pixel(color, coordinate, readback);
        return device.Submit(commands);
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
                "The SwiftShader pick did not complete without a render-loop wait.");
        }
        return await pending;
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
        uint size,
        ulong topologyRevision,
        ulong pageRevision) =>
        SilkMeshRendererConformance.Apply(
            renderer,
            pageRevision,
            SilkMeshRendererConformance.CreateFrameCommand(
                size,
                size,
                SilkMeshRendererConformance.Identity()),
            CreateMeshCommand(
                1,
                "/Triangle",
                [
                    -0.6f, -0.6f, 0.2f,
                     0.0f,  0.6f, 0.2f,
                     0.6f, -0.6f, 0.2f
                ],
                [0, 1, 2],
                topologyRevision));

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
        CreateMeshCommand(
            id,
            path,
            [
                left, top, 0.25f,
                right, top, 0.25f,
                right, bottom, 0.25f,
                left, bottom, 0.25f
            ],
            [0, 2, 1, 0, 3, 2],
            topologyRevision: 1);

    private static byte[] CreateMeshCommand(
        ulong id,
        string pathValue,
        float[] points,
        uint[] indices,
        ulong topologyRevision)
    {
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        int triangleCount = indices.Length / 3;
        int size = 200 +
            path.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            (triangleCount * sizeof(uint));
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(4),
            checked((uint)size));
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            ComputeStableHash(pathValue));
        BinaryPrimitives.WriteInt32LittleEndian(
            bytes.AsSpan(16),
            checked((int)id));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(32),
            topologyRevision);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(40),
            checked((uint)path.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            checked((uint)(points.Length / 3)));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(48),
            checked((uint)indices.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(52),
            checked((uint)triangleCount));
        for (int channel = 0; channel < 4; channel++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(56 + (channel * sizeof(float))),
                1);
        }
        double[] transform = SilkMeshRendererConformance.Identity();
        for (int index = 0; index < transform.Length; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(72 + (index * sizeof(double))),
                transform[index]);
        }
        path.CopyTo(bytes, 200);
        int pointsOffset = 200 + path.Length;
        for (int index = 0; index < points.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(pointsOffset + (index * sizeof(float))),
                points[index]);
        }
        int indicesOffset = pointsOffset + (points.Length * sizeof(float));
        for (int index = 0; index < indices.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(indicesOffset + (index * sizeof(uint))),
                indices[index]);
        }
        int subprimOffset = indicesOffset + (indices.Length * sizeof(uint));
        for (int triangle = 0; triangle < triangleCount; triangle++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(subprimOffset + (triangle * sizeof(uint))),
                checked((uint)triangle));
        }
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
}
