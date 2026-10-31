// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkPickPublicationTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task RefusedPickTargetCreationFaultsTheRequestAndReleasesPartialTargets(
        SilkGraphicsBackend backend, bool allowColor)
    {
        using var device = new PickingDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var textures = new SilkGpuTextureBudget(1_000_000);
        textures.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(32, 24));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(32, 24));
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(32, 24, SilkMeshRendererConformance.Identity()),
                .. CreateQuad()], 2);
        _ = renderer.ApplyAndRender(first, color, depth);
        byte[] before = new byte[32 * 24 * 4];
        color.ReadbackForTesting(before);
        ulong retained = textures.Usage.ReservedBytes;
        var binding = new SilkPickFrameBinding(1, 2);
        var request = new RenderPickRequest(16, 12, new ViewportDimensions(32, 24), 1, 2, RenderPickTarget.Face);
        Task<RenderPickResult> pending = renderer.PickAsync(request).AsTask();
        using var next = new OpenUsdSilkPage(30, 2, [], 0);
        ulong headroom = allowColor ? 32ul * 24 * 4 : 0;
        ulong pressure = textures.MaximumBytes - retained - headroom;
        using (textures.Reserve(SilkTextureDescriptor.ColorTarget(checked((uint)(pressure / 4)), 1), 1))
        {
            await Assert.That(() => renderer.ApplyAndRender(next, color, depth, binding))
                .Throws<SilkGpuTextureBudgetExceededException>();
            await Assert.That(pending.IsCompleted).IsTrue();
            await Assert.That(async () => await pending).Throws<SilkGpuTextureBudgetExceededException>();
            await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
            await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(textures.MaximumBytes - headroom);
        }
        byte[] after = new byte[before.Length];
        color.ReadbackForTesting(after);
        await Assert.That(after.SequenceEqual(before)).IsTrue();
        Task<RenderPickResult> retry = renderer.PickAsync(request).AsTask();
        _ = renderer.ApplyAndRender(next, color, depth, binding);
        RenderPickResult result = await CompletePick(renderer, color, depth, binding, retry);
        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(result.PrimPath).IsEqualTo("/Quad");
        await Assert.That(result.ElementIndex).IsEqualTo(42);
        renderer.Dispose();
        color.Dispose();
        depth.Dispose();
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, RenderPickTarget.Face, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, RenderPickTarget.Face, false)]
    [Arguments(SilkGraphicsBackend.D3D12, RenderPickTarget.Edge, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, RenderPickTarget.Edge, false)]
    [Arguments(SilkGraphicsBackend.D3D12, RenderPickTarget.Point, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, RenderPickTarget.Point, false)]
    [Arguments(SilkGraphicsBackend.D3D12, RenderPickTarget.Face, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, RenderPickTarget.Face, true)]
    [Arguments(SilkGraphicsBackend.D3D12, RenderPickTarget.Edge, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, RenderPickTarget.Edge, true)]
    [Arguments(SilkGraphicsBackend.D3D12, RenderPickTarget.Point, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, RenderPickTarget.Point, true)]
    public async Task RefusedResizedSubprimPickRestoresPublishedTargetsAndReleasesCandidateOwnership(
        SilkGraphicsBackend backend, RenderPickTarget target, bool refusePublication)
    {
        using var device = new PickingDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var buffers = new SilkGpuBufferBudget(1_000_000);
        var textures = new SilkGpuTextureBudget(1_000_000);
        var staging = new SilkGpuStagingBudget(1_000_000);
        buffers.ConfigureDevice(device);
        textures.ConfigureDevice(device);
        staging.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(32, 24));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(32, 24));
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(32, 24, SilkMeshRendererConformance.Identity()),
                .. CreateQuad()], 2);
        var oldBinding = new SilkPickFrameBinding(7, 1);
        var oldRequest = new RenderPickRequest(16, 12, new ViewportDimensions(32, 24), 7, 1, RenderPickTarget.Face);
        Task<RenderPickResult> initialPick = renderer.PickAsync(oldRequest).AsTask();
        _ = renderer.ApplyAndRender(first, color, depth, oldBinding);
        RenderPickResult initial = await CompletePick(renderer, color, depth, oldBinding, initialPick);
        await Assert.That(initial.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(initial.ElementIndex).IsEqualTo(42);
        byte[] published = new byte[32 * 24 * 4];
        color.ReadbackForTesting(published);
        using ISilkGraphicsTexture resizedColor = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(64, 48));
        using ISilkGraphicsTexture resizedDepth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(64, 48));
        using (ISilkGraphicsCommandList commands = device.CreateCommandList())
        {
            commands.ClearColor(resizedColor, new SilkColor(0.25f, 0.5f, 0.75f, 1));
            commands.ClearDepth(resizedDepth, 0.75f);
            using ISilkGraphicsSubmission submission = device.Submit(commands);
            submission.Wait();
        }
        byte[] beforeColor = new byte[64 * 48 * 4];
        float[] beforeDepth = new float[64 * 48];
        resizedColor.ReadbackForTesting(beforeColor);
        resizedDepth.ReadbackForTesting(beforeDepth);
        ulong bufferBytes = buffers.Usage.ReservedBytes;
        ulong textureBytes = textures.Usage.ReservedBytes;
        ulong stagingBytes = staging.Usage.ReservedBytes;
        var binding = new SilkPickFrameBinding(8, 2);
        (int x, int y, int element) = target switch
        {
            RenderPickTarget.Face => (32, 24, 42),
            RenderPickTarget.Edge => (32, 12, 0),
            RenderPickTarget.Point => (16, 12, 17),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        var request = new RenderPickRequest(x, y, new ViewportDimensions(64, 48), 8, 2, target);
        using var next = new OpenUsdSilkPage(30, 2,
            SilkMeshRendererConformance.CreateFrameCommand(64, 48, SilkMeshRendererConformance.Identity()), 1);
        Task<RenderPickResult> pending = renderer.PickAsync(request).AsTask();
        device.RefuseSubmissionAfter = refusePublication ? 2 : 1;
        await Assert.That(() => renderer.ApplyAndRender(next, resizedColor, resizedDepth, binding))
            .Throws<SilkGpuStagingBudgetExceededException>();
        await Assert.That(pending.IsCompleted).IsTrue();
        await Assert.That(async () => await pending).Throws<InvalidOperationException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(bufferBytes);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(textureBytes);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(stagingBytes);
        byte[] afterColor = new byte[beforeColor.Length];
        float[] afterDepth = new float[beforeDepth.Length];
        resizedColor.ReadbackForTesting(afterColor);
        resizedDepth.ReadbackForTesting(afterDepth);
        await Assert.That(afterColor.SequenceEqual(beforeColor)).IsTrue();
        await Assert.That(afterDepth.SequenceEqual(beforeDepth)).IsTrue();
        device.RefuseSubmissionAfter = null;
        ulong pressure = textures.MaximumBytes - textureBytes;
        using (textures.Reserve(SilkTextureDescriptor.ColorTarget(checked((uint)(pressure / 4)), 1), 1))
        {
            Task<RenderPickResult> retained = renderer.PickAsync(oldRequest).AsTask();
            RenderPickResult retainedResult = await CompletePick(renderer, color, depth, oldBinding, retained);
            await Assert.That(retainedResult.Status).IsEqualTo(RenderPickStatus.Hit);
            await Assert.That(retainedResult.ElementIndex).IsEqualTo(42);
        }
        byte[] afterRetained = new byte[published.Length];
        color.ReadbackForTesting(afterRetained);
        await Assert.That(afterRetained.SequenceEqual(published)).IsTrue();
        Task<RenderPickResult> retry = renderer.PickAsync(request).AsTask();
        _ = renderer.ApplyAndRender(next, resizedColor, resizedDepth, binding);
        RenderPickResult result = await CompletePick(renderer, resizedColor, resizedDepth, binding, retry);
        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(result.PrimPath).IsEqualTo("/Quad");
        await Assert.That(result.ElementIndex).IsEqualTo(element);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2ul);
        renderer.Dispose();
        color.Dispose();
        depth.Dispose();
        resizedColor.Dispose();
        resizedDepth.Dispose();
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task VulkanPickPreparationQuotaRefusalReleasesCandidateFramebufferOwnership()
    {
        using var device = new PickingDevice(SilkDepthCaptureConformance.CreateDevice(SilkGraphicsBackend.Vulkan));
        var textures = new SilkGpuTextureBudget(1_000_000);
        var staging = new SilkGpuStagingBudget(1_000_000);
        textures.ConfigureDevice(device);
        staging.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(32, 24));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(32, 24));
        byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(1, "/Mesh",
            [-0.6f, 0.6f, 0.5f, 0.6f, 0.6f, 0.5f, 0.6f, -0.6f, 0.5f], [0, 1, 2]);
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(32, 24, SilkMeshRendererConformance.Identity()),
                .. mesh], 2);
        _ = renderer.ApplyAndRender(first, color, depth);
        ulong retainedTextures = textures.Usage.ReservedBytes;
        ulong retainedStaging = staging.Usage.ReservedBytes;
        var binding = new SilkPickFrameBinding(1, 2);
        var request = new RenderPickRequest(20, 8, new ViewportDimensions(32, 24), 1, 2);
        Task<RenderPickResult> pick = renderer.PickAsync(request).AsTask();
        using var next = new OpenUsdSilkPage(30, 2, [], 0);
        using (staging.Reserve(staging.MaximumBytes - retainedStaging))
        {
            await Assert.That(() => renderer.ApplyAndRender(next, color, depth, binding))
                .Throws<SilkGpuStagingBudgetExceededException>();
            await Assert.That(pick.IsCompleted).IsTrue();
            await Assert.That(async () => await pick).Throws<SilkGpuStagingBudgetExceededException>();
            await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
            await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(retainedTextures);
            await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(staging.MaximumBytes);
        }
        for (int attempt = 0; attempt < 4; attempt++)
        {
            Task<RenderPickResult> retry = renderer.PickAsync(request).AsTask();
            if (attempt == 0)
            {
                _ = renderer.ApplyAndRender(next, color, depth, binding);
            }
            else
            {
                _ = renderer.Render(color, depth, binding);
            }
            for (int frame = 0; frame < 100 && !retry.IsCompleted; frame++)
            {
                _ = renderer.Render(color, depth, binding);
                await Task.Yield();
            }
            await Assert.That(retry.IsCompleted).IsTrue();
            await Assert.That((await retry).Status).IsEqualTo(RenderPickStatus.Hit);
        }
        renderer.Dispose();
        color.Dispose();
        depth.Dispose();
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task RefusedPickOrPublicationCannotPublishScenePixelsOrAHit(
        SilkGraphicsBackend backend, bool refusePublication)
    {
        using var device = new PickingDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var buffers = new SilkGpuBufferBudget(1_000_000);
        var textures = new SilkGpuTextureBudget(1_000_000);
        var staging = new SilkGpuStagingBudget(1_000_000);
        buffers.ConfigureDevice(device);
        textures.ConfigureDevice(device);
        staging.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(32, 24));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(32, 24));
        byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(1, "/Mesh",
            [-0.6f, 0.6f, 0.5f, 0.6f, 0.6f, 0.5f, 0.6f, -0.6f, 0.5f, -0.6f, -0.6f, 0.5f], [0, 1, 2, 0, 2, 3]);
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(32, 24, SilkMeshRendererConformance.Identity()),
                .. mesh], 2);
        _ = renderer.ApplyAndRender(first, color, depth);
        ulong bufferBytes = buffers.Usage.ReservedBytes;
        ulong textureBytes = textures.Usage.ReservedBytes;
        ulong stagingBytes = staging.Usage.ReservedBytes;
        byte[] before = new byte[32 * 24 * 4];
        color.ReadbackForTesting(before);
        BinaryPrimitives.WriteDoubleLittleEndian(mesh.AsSpan(176), 0.25);
        using var changed = new OpenUsdSilkPage(30, 2, mesh, 1);
        var binding = new SilkPickFrameBinding(7, 2);
        var request = new RenderPickRequest(16, 12, new ViewportDimensions(32, 24),
            binding.StateRevision, binding.SceneRevision, RenderPickTarget.Primitive);
        Task<RenderPickResult> pick = renderer.PickAsync(request).AsTask();
        device.RefuseSubmissionAfter = refusePublication ? 2 : 1;
        await Assert.That(() => renderer.ApplyAndRender(changed, color, depth, binding))
            .Throws<SilkGpuStagingBudgetExceededException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        await Assert.That(pick.IsCompleted).IsTrue();
        await Assert.That(async () => await pick).Throws<InvalidOperationException>();
        byte[] afterRefusal = new byte[before.Length];
        color.ReadbackForTesting(afterRefusal);
        await Assert.That(afterRefusal.SequenceEqual(before)).IsTrue();
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(bufferBytes);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(textureBytes);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(stagingBytes);
        device.RefuseSubmissionAfter = null;
        Task<RenderPickResult> retry = renderer.PickAsync(request).AsTask();
        _ = renderer.ApplyAndRender(changed, color, depth, binding);
        for (int index = 0; index < 100 && !retry.IsCompleted; index++)
        {
            _ = renderer.Render(color, depth, binding);
            await Task.Yield();
        }
        await Assert.That(retry.IsCompleted).IsTrue();
        RenderPickResult result = await retry;
        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(result.PrimPath).IsEqualTo("/Mesh");
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2ul);
        renderer.Dispose();
        color.Dispose();
        depth.Dispose();
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(staging.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    private static byte[] CreateQuad() => SilkMeshRendererConformance.CreateMeshCommand(1, "/Quad",
        [-31f / 64, 23f / 48, 0.5f, 33f / 64, 23f / 48, 0.5f,
            33f / 64, -25f / 48, 0.5f, -31f / 64, -25f / 48, 0.5f],
        [0, 1, 2, 0, 2, 3], 0, 0, null, 1, triangleSubprims: [42, 42], pointOrigins: [17, 18, 19, 20],
        cornerEdges: [0, 1, -1, -1, 2, 3]);

    private static async Task<RenderPickResult> CompletePick(SilkMeshRenderer renderer,
        ISilkGraphicsTexture color, ISilkGraphicsTexture depth, SilkPickFrameBinding binding,
        Task<RenderPickResult> pending)
    {
        for (int frame = 0; frame < 100 && !pending.IsCompleted; frame++)
        {
            _ = renderer.Render(color, depth, binding);
            await Task.Yield();
        }
        await Assert.That(pending.IsCompleted).IsTrue();
        return await pending;
    }

    private sealed class PickingDevice(ISilkGraphicsDevice inner)
        : SilkGpuPublicationDevice(inner), ISilkPickingGraphicsDevice
    {
        public ulong PickDeviceGeneration => ((ISilkPickingGraphicsDevice)Inner).PickDeviceGeneration;
        public ISilkPickGraphicsPipeline CreatePickGraphicsPipeline(SilkPickPipelineDescriptor descriptor) =>
            ((ISilkPickingGraphicsDevice)Inner).CreatePickGraphicsPipeline(descriptor);
        public ISilkPickReadbackBuffer CreatePickReadbackBuffer() =>
            ((ISilkPickingGraphicsDevice)Inner).CreatePickReadbackBuffer();
    }
}
