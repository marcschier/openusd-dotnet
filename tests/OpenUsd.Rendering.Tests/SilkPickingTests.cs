// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkPickingTests
{
    [Test]
    public async Task TokenEncodingIsExplicitLittleEndianRgba()
    {
        var bytes = new byte[SilkPickTokenEncoding.ByteSize];

        SilkPickTokenEncoding.Encode(0x12345678, bytes);

        await Assert.That(bytes).IsEquivalentTo(
            new byte[] { 0x78, 0x56, 0x34, 0x12 });
        await Assert.That(SilkPickTokenEncoding.Decode(bytes))
            .IsEqualTo(0x12345678U);
        await Assert.That(SilkPickTokenEncoding.Decode([0, 0, 0, 0]))
            .IsEqualTo(0U);
    }

    [Test]
    public async Task RendererRejectsDefaultPickRequest()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => _ = renderer.PickAsync(default).AsTask());

        await Assert.That(exception.ParamName).IsEqualTo("request");
    }

    [Test]
    public async Task ReadbackRingReusesSlotsAndReturnsSaturation()
    {
        using var device = new TestGraphicsDevice();
        using var ring = new SilkPickReadbackRing(device, capacity: 2);
        var context = new SilkPickReadbackContext(
            CreateRequest(0, 0, 2, 2, stateRevision: 1, sceneRevision: 2),
            1,
            2,
            3,
            device.PickDeviceGeneration,
            new ViewportDimensions(2, 2));

        await Assert.That(ring.TryAcquire(out SilkPickReadbackReservation first))
            .IsTrue();
        await Assert.That(ring.TryAcquire(out SilkPickReadbackReservation second))
            .IsTrue();
        await Assert.That(ring.TryAcquire(out _)).IsFalse();

        var firstSubmission = new TestSubmission();
        var secondSubmission = new TestSubmission();
        ring.Commit(first, firstSubmission, context);
        ring.Commit(second, secondSubmission, context);
        ((TestReadbackBuffer)first.Buffer).SetToken(0x44332211);
        firstSubmission.Complete();

        await Assert.That(ring.TryReadCompleted(out SilkPickReadbackResult completed))
            .IsTrue();
        await Assert.That(completed.Token).IsEqualTo(0x44332211U);
        await Assert.That(completed.SlotIndex).IsEqualTo(0);
        await Assert.That(ring.InFlightCount).IsEqualTo(1);
        await Assert.That(ring.TryAcquire(out SilkPickReadbackReservation reused))
            .IsTrue();
        await Assert.That(reused.Buffer).IsSameReferenceAs(first.Buffer);
        ring.Cancel(reused);
    }

    [Test]
    public async Task WarmReadbackRingOperationsDoNotAllocate()
    {
        using var device = new TestGraphicsDevice();
        using var ring = new SilkPickReadbackRing(device, capacity: 2);
        var context = new SilkPickReadbackContext(
            CreateRequest(0, 0, 1, 1, stateRevision: 1, sceneRevision: 2),
            1,
            2,
            3,
            device.PickDeviceGeneration,
            new ViewportDimensions(1, 1));
        long allocated = MeasureReadbackRingAllocations(ring, context);
        if (allocated != 0)
        {
            allocated = MeasureReadbackRingAllocations(ring, context);
        }

        await Assert.That(allocated).IsEqualTo(0);
    }

    [Test]
    public async Task RecordsNoIdPassWithoutRequestAndResolvesAuthoritativeHit()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device, 8, 6);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(8, 6));
        ApplyScene(renderer, 8, 6, pageRevision: 1, topologyRevision: 1);
        var binding = new SilkPickFrameBinding(5, 9);

        SilkMeshRenderResult visible = renderer.Render(color, depth, binding);

        await Assert.That(visible.DrawCount).IsEqualTo(1);
        await Assert.That(device.PickSubmissionCount).IsEqualTo(0);
        await Assert.That(renderer.PickingStatistics.PassesRecorded).IsEqualTo(0UL);
        await Assert.That(renderer.Scene.PickIdentities.TryGetRange(
            "/Triangle",
            out SilkPickTokenRange range)).IsTrue();

        device.EnqueuePickToken(range.FirstToken);
        ValueTask<RenderPickResult> pending = renderer.PickAsync(
            CreateRequest(3, 2, 8, 6, 5, 9));
        SilkMeshRenderResult pickedFrame = renderer.Render(color, depth, binding);
        RenderPickResult result = await pending;

        await Assert.That(pickedFrame.DrawCount).IsEqualTo(visible.DrawCount);
        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(result.PrimPath).IsEqualTo("/Triangle");
        await Assert.That(result.ElementIndex).IsEqualTo(7);
        await Assert.That(result.BackendKind).IsEqualTo(RenderBackendKind.Vulkan);
        await Assert.That(result.BackendToken).IsEqualTo(range.FirstToken);
        await Assert.That(result.WorldPosition).IsNull();
        await Assert.That(result.WorldNormal).IsNull();
        await Assert.That(result.NormalizedDepth).IsNull();
        await Assert.That(device.PickSubmissionCount).IsEqualTo(1);
        await Assert.That(device.LastPickCoordinate)
            .IsEqualTo(new SilkTexturePixelCoordinate(3, 2));
        await Assert.That(device.LastPickScissor)
            .IsEqualTo(new SilkScissor(3, 2, 1, 1));
        await Assert.That(device.LastPickSource).IsNotSameReferenceAs(color);
        await Assert.That(device.LastPickBaseTokens).Contains(range.FirstToken);
        await Assert.That(device.PickPipelineCreateCount).IsEqualTo(1);
        await Assert.That(device.ReadbackBufferCreateCount).IsEqualTo(3);
    }

    [Test]
    public async Task ReturnsStaleMissAndUnsupportedWithoutFabricatingIdentity()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device, 8, 6);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(8, 6));
        ApplyScene(renderer, 8, 6, pageRevision: 1, topologyRevision: 1);

        ValueTask<RenderPickResult> stalePending = renderer.PickAsync(
            CreateRequest(1, 1, 8, 6, stateRevision: 4, sceneRevision: 9));
        _ = renderer.Render(
            color,
            depth,
            new SilkPickFrameBinding(5, 9));
        RenderPickResult stale = await stalePending;

        ValueTask<RenderPickResult> sceneStalePending = renderer.PickAsync(
            CreateRequest(1, 1, 8, 6, stateRevision: 5, sceneRevision: 8));
        _ = renderer.Render(
            color,
            depth,
            new SilkPickFrameBinding(5, 9));
        RenderPickResult sceneStale = await sceneStalePending;

        device.EnqueuePickToken(0);
        ValueTask<RenderPickResult> missPending = renderer.PickAsync(
            CreateRequest(1, 1, 8, 6, stateRevision: 5, sceneRevision: 9));
        _ = renderer.Render(
            color,
            depth,
            new SilkPickFrameBinding(5, 9));
        RenderPickResult miss = await missPending;

        ValueTask<RenderPickResult> unsupportedPending = renderer.PickAsync(
            CreateRequest(
                1,
                1,
                8,
                6,
                stateRevision: 5,
                sceneRevision: 9,
                target: RenderPickTarget.Edge));
        _ = renderer.Render(
            color,
            depth,
            new SilkPickFrameBinding(5, 9));
        RenderPickResult unsupported = await unsupportedPending;

        await Assert.That(stale.Status).IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(stale.StaleReasons)
            .IsEqualTo(RenderPickStaleReason.StateRevision);
        await AssertEmptyIdentity(stale);
        await Assert.That(sceneStale.Status).IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(sceneStale.StaleReasons)
            .IsEqualTo(RenderPickStaleReason.SceneRevision);
        await AssertEmptyIdentity(sceneStale);
        await Assert.That(miss.Status).IsEqualTo(RenderPickStatus.Miss);
        await AssertEmptyIdentity(miss);
        await Assert.That(unsupported.Status)
            .IsEqualTo(RenderPickStatus.Unsupported);
        await AssertEmptyIdentity(unsupported);
        await Assert.That(device.PickSubmissionCount).IsEqualTo(1);
    }

    [Test]
    public async Task ReadbackRingSaturationBoundsWorkAndReusesBuffers()
    {
        using var device = new TestGraphicsDevice
        {
            AutoCompletePickSubmissions = false
        };
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device, 8, 6);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(8, 6));
        ApplyScene(renderer, 8, 6, pageRevision: 1, topologyRevision: 1);
        _ = renderer.Scene.PickIdentities.TryGetRange(
            "/Triangle",
            out SilkPickTokenRange range);
        var binding = new SilkPickFrameBinding(5, 9);
        var pending = new List<Task<RenderPickResult>>();

        for (int index = 0; index < 3; index++)
        {
            device.EnqueuePickToken(range.FirstToken);
            pending.Add(renderer.PickAsync(
                CreateRequest(index, 1, 8, 6, 5, 9)).AsTask());
            _ = renderer.Render(color, depth, binding);
        }

        device.EnqueuePickToken(range.FirstToken);
        pending.Add(renderer.PickAsync(
            CreateRequest(4, 1, 8, 6, 5, 9)).AsTask());
        _ = renderer.Render(color, depth, binding);

        await Assert.That(device.PickSubmissionCount).IsEqualTo(3);
        await Assert.That(renderer.PickingStatistics.InFlightReadbacks)
            .IsEqualTo(3);
        await Assert.That(renderer.PickingStatistics.QueuedRequests)
            .IsEqualTo(1);
        await Assert.That(renderer.PickingStatistics.RingSaturations)
            .IsEqualTo(1UL);

        device.CompleteNextPick();
        _ = renderer.Render(color, depth, binding);
        RenderPickResult first = await pending[0];

        await Assert.That(first.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(device.PickSubmissionCount).IsEqualTo(4);
        await Assert.That(device.ReadbackBufferCreateCount).IsEqualTo(3);

        device.CompleteAllPicks();
        _ = renderer.Render(color, depth, binding);
        for (int index = 1; index < pending.Count; index++)
        {
            await Assert.That((await pending[index]).Status)
                .IsEqualTo(RenderPickStatus.Hit);
        }
    }

    [Test]
    public async Task ResizeAndDeviceGenerationInvalidateInFlightResults()
    {
        using var device = new TestGraphicsDevice
        {
            AutoCompletePickSubmissions = false
        };
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device, 8, 6);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(8, 6));
        ApplyScene(renderer, 8, 6, pageRevision: 1, topologyRevision: 1);
        _ = renderer.Scene.PickIdentities.TryGetRange(
            "/Triangle",
            out SilkPickTokenRange range);

        device.EnqueuePickToken(range.FirstToken);
        ValueTask<RenderPickResult> resizedPending = renderer.PickAsync(
            CreateRequest(1, 1, 8, 6, 5, 9));
        _ = renderer.Render(
            color,
            depth,
            new SilkPickFrameBinding(5, 9));
        device.CompleteNextPick();

        using ISilkGraphicsTexture resizedColor = CreateColorTarget(
            device,
            10,
            7);
        using ISilkGraphicsTexture resizedDepth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(10, 7));
        _ = renderer.Render(
            resizedColor,
            resizedDepth,
            new SilkPickFrameBinding(5, 9));
        RenderPickResult resized = await resizedPending;

        device.EnqueuePickToken(range.FirstToken);
        ValueTask<RenderPickResult> generationPending = renderer.PickAsync(
            CreateRequest(1, 1, 10, 7, 5, 9));
        _ = renderer.Render(
            resizedColor,
            resizedDepth,
            new SilkPickFrameBinding(5, 9));
        device.PickDeviceGeneration++;
        _ = renderer.Render(
            resizedColor,
            resizedDepth,
            new SilkPickFrameBinding(5, 9));
        RenderPickResult generation = await generationPending;

        await Assert.That(resized.Status).IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(resized.StaleReasons.HasFlag(
            RenderPickStaleReason.Viewport)).IsTrue();
        await Assert.That(generation.Status).IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(generation.StaleReasons.HasFlag(
            RenderPickStaleReason.ContextGeneration)).IsTrue();
        await Assert.That(renderer.PickingStatistics.TargetCreations)
            .IsEqualTo(2UL);
        await Assert.That(device.PickPipelineCreateCount).IsEqualTo(2);
        await Assert.That(device.ReadbackBufferCreateCount).IsEqualTo(6);
    }

    [Test]
    public async Task TopologyRevisionInvalidatesInFlightTokenWithoutGeometryRebuild()
    {
        using var device = new TestGraphicsDevice
        {
            AutoCompletePickSubmissions = false
        };
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device, 8, 6);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(8, 6));
        ApplyScene(renderer, 8, 6, pageRevision: 1, topologyRevision: 1);
        _ = renderer.Scene.PickIdentities.TryGetRange(
            "/Triangle",
            out SilkPickTokenRange firstRange);

        device.EnqueuePickToken(firstRange.FirstToken);
        ValueTask<RenderPickResult> pending = renderer.PickAsync(
            CreateRequest(1, 1, 8, 6, 5, 9));
        _ = renderer.Render(
            color,
            depth,
            new SilkPickFrameBinding(5, 9));

        ApplyScene(renderer, 8, 6, pageRevision: 2, topologyRevision: 2);
        device.CompleteNextPick();
        _ = renderer.Render(
            color,
            depth,
            new SilkPickFrameBinding(5, 9));
        RenderPickResult result = await pending;
        _ = renderer.Scene.PickIdentities.TryGetRange(
            "/Triangle",
            out SilkPickTokenRange secondRange);

        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Stale);
        await Assert.That(result.StaleReasons.HasFlag(
            RenderPickStaleReason.BackendState)).IsTrue();
        await Assert.That(secondRange.FirstToken)
            .IsGreaterThan(firstRange.LastToken);
        await Assert.That(renderer.Scene.PickIdentities.TryResolve(
            firstRange.FirstToken,
            out _)).IsFalse();
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds)
            .IsEqualTo(1UL);
    }

    [Test]
    public async Task FrameAndPropertyUpdatesDoNotInvalidateInFlightIdentity()
    {
        using var device = new TestGraphicsDevice
        {
            AutoCompletePickSubmissions = false
        };
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device, 8, 6);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(8, 6));
        _ = ApplyScene(
            renderer,
            8,
            6,
            pageRevision: 1,
            topologyRevision: 1);
        _ = renderer.Scene.PickIdentities.TryGetRange(
            "/Triangle",
            out SilkPickTokenRange range);
        ulong identityRevision = renderer.Scene.PickIdentities.Revision;

        device.EnqueuePickToken(range.FirstToken);
        ValueTask<RenderPickResult> pending = renderer.PickAsync(
            CreateRequest(1, 1, 8, 6, 5, 9));
        _ = renderer.Render(
            color,
            depth,
            new SilkPickFrameBinding(5, 9));

        SilkSceneDelta frameDelta = ApplyFrame(
            renderer,
            8,
            6,
            pageRevision: 2);
        SilkSceneDelta propertyDelta = ApplyScene(
            renderer,
            8,
            6,
            pageRevision: 3,
            topologyRevision: 1,
            displayColor: 0.5f);
        device.CompleteNextPick();
        _ = renderer.Render(
            color,
            depth,
            new SilkPickFrameBinding(5, 9));
        RenderPickResult result = await pending;

        await Assert.That(frameDelta.MeshUpserts).IsEqualTo(0);
        await Assert.That(frameDelta.MeshRemovals).IsEqualTo(0);
        await Assert.That(propertyDelta.UpsertedMeshIds.ToArray())
            .IsEquivalentTo(new ulong[] { 7 });
        await Assert.That(renderer.Scene.Revision).IsEqualTo(3UL);
        await Assert.That(renderer.Scene.PickIdentities.Revision)
            .IsEqualTo(identityRevision);
        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(result.BackendToken).IsEqualTo(range.FirstToken);
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds)
            .IsEqualTo(1UL);
    }

    [Test]
    public async Task CoalescedRecreationUsesLogicalRemovalAndFreshPickRanges()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device, 8, 6);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(8, 6));
        _ = ApplyScene(
            renderer,
            8,
            6,
            pageRevision: 1,
            topologyRevision: 5,
            primId: 7,
            subprim: 7);
        _ = renderer.Scene.PickIdentities.TryGetRange(
            "/Triangle",
            out SilkPickTokenRange firstRange);

        SilkSceneDelta primRecreation = ApplyScene(
            renderer,
            8,
            6,
            pageRevision: 2,
            topologyRevision: 6,
            primId: 8,
            subprim: 11);
        _ = renderer.Scene.PickIdentities.TryGetRange(
            "/Triangle",
            out SilkPickTokenRange secondRange);
        SilkMeshGpuResource recreatedResource = renderer.GpuResources.Meshes[8];

        SilkSceneDelta revisionReset = ApplyScene(
            renderer,
            8,
            6,
            pageRevision: 3,
            topologyRevision: 1,
            primId: 8,
            subprim: 12);
        _ = renderer.Scene.PickIdentities.TryGetRange(
            "/Triangle",
            out SilkPickTokenRange resetRange);

        device.EnqueuePickToken(resetRange.FirstToken);
        ValueTask<RenderPickResult> pending = renderer.PickAsync(
            CreateRequest(1, 1, 8, 6, 5, 9));
        _ = renderer.Render(
            color,
            depth,
            new SilkPickFrameBinding(5, 9));
        RenderPickResult result = await pending;

        await Assert.That(primRecreation.RemovedMeshIds.ToArray())
            .IsEquivalentTo(new ulong[] { 7 });
        await Assert.That(primRecreation.UpsertedMeshIds.ToArray())
            .IsEquivalentTo(new ulong[] { 8 });
        await Assert.That(renderer.GpuResources.Meshes.ContainsKey(7)).IsFalse();
        await Assert.That(secondRange.FirstToken)
            .IsGreaterThan(firstRange.LastToken);
        await Assert.That(revisionReset.RemovedMeshIds.Length).IsEqualTo(0);
        await Assert.That(revisionReset.UpsertedMeshIds.ToArray())
            .IsEquivalentTo(new ulong[] { 8 });
        await Assert.That(renderer.GpuResources.Meshes[8])
            .IsSameReferenceAs(recreatedResource);
        await Assert.That(resetRange.FirstToken)
            .IsGreaterThan(secondRange.LastToken);
        await Assert.That(renderer.Scene.PickIdentities.TryResolve(
            firstRange.FirstToken,
            out _)).IsFalse();
        await Assert.That(renderer.Scene.PickIdentities.TryResolve(
            secondRange.FirstToken,
            out _)).IsFalse();
        await Assert.That(result.Status).IsEqualTo(RenderPickStatus.Hit);
        await Assert.That(result.PrimPath).IsEqualTo("/Triangle");
        await Assert.That(result.ElementIndex).IsEqualTo(12);
        await Assert.That(result.BackendToken).IsEqualTo(resetRange.FirstToken);
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds)
            .IsEqualTo(2UL);
    }

    [Test]
    public async Task WarmPickingDoesNotRebuildMeshesOrChurnPersistentResources()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device, 8, 6);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(8, 6));
        ApplyScene(renderer, 8, 6, pageRevision: 1, topologyRevision: 1);
        _ = renderer.Scene.PickIdentities.TryGetRange(
            "/Triangle",
            out SilkPickTokenRange range);

        device.EnqueuePickToken(range.FirstToken);
        ValueTask<RenderPickResult> warmup = renderer.PickAsync(
            CreateRequest(1, 1, 8, 6, 5, 9));
        _ = renderer.Render(
            color,
            depth,
            new SilkPickFrameBinding(5, 9));
        _ = await warmup;
        int bufferCount = device.BufferCreateCount;
        int readbackCount = device.ReadbackBufferCreateCount;
        int pipelineCount = device.PickPipelineCreateCount;
        ulong targetCount = renderer.PickingStatistics.TargetCreations;

        for (ulong revision = 6; revision < 26; revision++)
        {
            device.EnqueuePickToken(range.FirstToken);
            ValueTask<RenderPickResult> pending = renderer.PickAsync(
                CreateRequest(1, 1, 8, 6, revision, 9));
            SilkMeshRenderResult frame = renderer.Render(
                color,
                depth,
                new SilkPickFrameBinding(revision, 9));
            RenderPickResult result = await pending;
            if (frame.DrawCount != 1 ||
                result.Status != RenderPickStatus.Hit ||
                result.WorldPosition is not null ||
                result.WorldNormal is not null ||
                result.NormalizedDepth is not null)
            {
                throw new InvalidOperationException(
                    "A warm ID-only pick changed visible rendering or fabricated geometry.");
            }
        }

        await Assert.That(device.BufferCreateCount).IsEqualTo(bufferCount);
        await Assert.That(device.ReadbackBufferCreateCount)
            .IsEqualTo(readbackCount);
        await Assert.That(device.PickPipelineCreateCount)
            .IsEqualTo(pipelineCount);
        await Assert.That(renderer.PickingStatistics.TargetCreations)
            .IsEqualTo(targetCount);
        await Assert.That(renderer.GpuResources.Statistics.GeometryBuilds)
            .IsEqualTo(1UL);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1UL);
    }

    [Test]
    public async Task LatestWinsQueueRetainsOnlyActiveAndNewestPending()
    {
        using var device = new TestGraphicsDevice
        {
            AutoCompletePickSubmissions = false
        };
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = CreateColorTarget(device, 8, 6);
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(8, 6));
        ApplyScene(renderer, 8, 6, pageRevision: 1, topologyRevision: 1);
        _ = renderer.Scene.PickIdentities.TryGetRange(
            "/Triangle",
            out SilkPickTokenRange range);
        var binding = new SilkPickFrameBinding(5, 9);
        var submitted = new List<Task<RenderPickResult>>();

        for (int index = 0; index < 3; index++)
        {
            device.EnqueuePickToken(range.FirstToken);
            submitted.Add(renderer.PickAsync(
                CreateRequest(index, 0, 8, 6, 5, 9)).AsTask());
            _ = renderer.Render(color, depth, binding);
        }

        device.EnqueuePickToken(range.FirstToken);
        device.EnqueuePickToken(range.FirstToken);
        Task<RenderPickResult> active = renderer.PickAsync(
            CreateRequest(3, 0, 8, 6, 5, 9)).AsTask();
        Task<RenderPickResult> superseded = renderer.PickAsync(
            CreateRequest(4, 0, 8, 6, 5, 9)).AsTask();
        Task<RenderPickResult> newest = renderer.PickAsync(
            CreateRequest(5, 0, 8, 6, 5, 9)).AsTask();

        await Assert.That(active.IsCompleted).IsFalse();
        await Assert.That(superseded.IsCanceled).IsTrue();
        await Assert.That(newest.IsCompleted).IsFalse();
        await Assert.That(renderer.PickingStatistics.QueuedRequests)
            .IsEqualTo(2);
        await Assert.That(renderer.PickingStatistics.SupersededRequests)
            .IsEqualTo(1UL);

        device.CompleteAllPicks();
        _ = renderer.Render(color, depth, binding);
        device.CompleteAllPicks();
        _ = renderer.Render(color, depth, binding);
        device.CompleteAllPicks();
        _ = renderer.Render(color, depth, binding);
        foreach (Task<RenderPickResult> task in submitted)
        {
            _ = await task;
        }
        _ = await active;
        _ = await newest;
    }

    private static async Task AssertEmptyIdentity(RenderPickResult result)
    {
        await Assert.That(result.Item).IsNull();
        await Assert.That(result.BackendToken).IsNull();
        await Assert.That(result.WorldPosition).IsNull();
        await Assert.That(result.WorldNormal).IsNull();
        await Assert.That(result.NormalizedDepth).IsNull();
    }

    private static void ExerciseReadbackRing(
        SilkPickReadbackRing ring,
        SilkPickReadbackContext context)
    {
        if (!ring.TryAcquire(out SilkPickReadbackReservation reservation))
        {
            throw new InvalidOperationException("The warmed readback ring is saturated.");
        }

        ((TestReadbackBuffer)reservation.Buffer).SetToken(0x01020304);
        ring.Commit(reservation, CompletedSubmission.Instance, context);
        if (!ring.TryReadCompleted(out SilkPickReadbackResult result) ||
            result.Token != 0x01020304)
        {
            throw new InvalidOperationException(
                "The warmed readback ring did not return the retained token.");
        }
    }

    private static long MeasureReadbackRingAllocations(
        SilkPickReadbackRing ring,
        SilkPickReadbackContext context)
    {
        const int blockSize = 200;
        const int maximumBlocks = 50;
        const int requiredQuietBlocks = 4;
        int consecutiveQuietBlocks = 0;
        for (int block = 0; block < maximumBlocks && consecutiveQuietBlocks < requiredQuietBlocks; block++)
        {
            long blockBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < blockSize; iteration++)
            {
                ExerciseReadbackRing(ring, context);
            }

            consecutiveQuietBlocks =
                GC.GetAllocatedBytesForCurrentThread() - blockBefore == 0
                    ? consecutiveQuietBlocks + 1
                    : 0;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            ExerciseReadbackRing(ring, context);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static RenderPickRequest CreateRequest(
        int x,
        int y,
        int width,
        int height,
        ulong stateRevision,
        ulong? sceneRevision,
        RenderPickTarget target = RenderPickTarget.Face) =>
        new(
            x,
            y,
            new ViewportDimensions(width, height),
            stateRevision,
            sceneRevision,
            target);

    private static ISilkGraphicsTexture CreateColorTarget(
        TestGraphicsDevice device,
        uint width,
        uint height) =>
        device.CreateTexture2D(new SilkTextureDescriptor(
            width,
            height,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget |
                SilkTextureUsage.CopySource));

    private static SilkSceneDelta ApplyScene(
        SilkMeshRenderer renderer,
        uint width,
        uint height,
        ulong pageRevision,
        ulong topologyRevision,
        int primId = 7,
        int subprim = 7,
        float displayColor = 1)
    {
        byte[] frame = CreateFrameCommand(width, height);
        byte[] mesh = CreateMeshCommand(
            topologyRevision,
            primId,
            subprim,
            displayColor);
        var page = new byte[frame.Length + mesh.Length];
        frame.CopyTo(page, 0);
        mesh.CopyTo(page, frame.Length);
        SilkSceneDelta delta = renderer.Scene.Apply(page, 2, pageRevision);
        renderer.GpuResources.Apply(renderer.Scene, delta);
        return delta;
    }

    private static SilkSceneDelta ApplyFrame(
        SilkMeshRenderer renderer,
        uint width,
        uint height,
        ulong pageRevision)
    {
        byte[] frame = CreateFrameCommand(width, height);
        SilkSceneDelta delta = renderer.Scene.Apply(frame, 1, pageRevision);
        renderer.GpuResources.Apply(renderer.Scene, delta);
        return delta;
    }

    private static byte[] CreateFrameCommand(uint width, uint height)
    {
        var bytes = new byte[272];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)SilkCommandType.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(4),
            (uint)bytes.Length);
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
                bytes.AsSpan(16 + (index * 8)),
                value);
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(144 + (index * 8)),
                value);
        }
        return bytes;
    }

    private static byte[] CreateMeshCommand(
        ulong topologyRevision,
        int primId,
        int subprim,
        float displayColor)
    {
        const string pathValue = "/Triangle";
        byte[] path = Encoding.UTF8.GetBytes(pathValue);
        float[] points = [-0.5f, -0.5f, 0, 0, 0.5f, 0, 0.5f, -0.5f, 0];
        uint[] indices = [0, 1, 2];
        int size = 268 +
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
            (uint)bytes.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash(pathValue));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), primId);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(32),
            topologyRevision);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(40),
            1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(48),
            (uint)path.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 1);
        for (int index = 0; index < 4; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(64 + (index * 4)),
                index == 3 ? 1 : displayColor);
        }
        for (int index = 0; index < 16; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (index * 8)),
                index % 5 == 0 ? 1 : 0);
        }
        path.CopyTo(bytes, 268);
        int pointOffset = 268 + path.Length;
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
            checked((uint)subprim));
        return bytes;
    }

    private sealed class TestGraphicsDevice :
        ISilkGraphicsDevice,
        ISilkPickingGraphicsDevice
    {
        private readonly Queue<uint> _pickTokens = [];
        private readonly List<TestSubmission> _pickSubmissions = [];

        internal bool AutoCompletePickSubmissions { get; set; } = true;

        internal int BufferCreateCount { get; private set; }

        internal int PickPipelineCreateCount { get; private set; }

        internal int ReadbackBufferCreateCount { get; private set; }

        internal int VisibleSubmissionCount { get; private set; }

        internal int PickSubmissionCount { get; private set; }

        internal SilkTexturePixelCoordinate LastPickCoordinate { get; set; }

        internal SilkScissor LastPickScissor { get; set; }

        internal ISilkGraphicsTexture? LastPickSource { get; set; }

        internal List<uint> LastPickBaseTokens { get; } = [];

        public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

        public SilkGraphicsCapabilities Capabilities { get; } =
            new("Test", "1", SupportsCompute: true, IsSoftware: true);

        public ulong PickDeviceGeneration { get; set; } = 1;

        public ISilkGraphicsBuffer CreateBuffer(
            nuint size,
            SilkBufferUsage usage)
        {
            BufferCreateCount++;
            return new TestGraphicsBuffer(size, usage);
        }

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            new TestTexture(width, height, format);

        public ISilkGraphicsTexture CreateTexture2D(
            SilkTextureDescriptor descriptor) =>
            new TestTexture(descriptor);

        public ISilkGraphicsSampler CreateSampler(
            SilkSamplerDescriptor descriptor) =>
            new TestSampler(descriptor);

        public ISilkGraphicsShaderModule CreateShaderModule(
            SilkShaderModuleDescriptor descriptor)
        {
            descriptor.Validate();
            return new TestShaderModule(descriptor);
        }

        public ISilkGraphicsBindingLayout CreateBindingLayout(
            SilkBindingLayoutDescriptor descriptor)
        {
            descriptor.Validate();
            return new TestBindingLayout(descriptor);
        }

        public ISilkGraphicsShaderProgram CreateShaderProgram(
            SilkShaderProgramDescriptor descriptor) =>
            new TestShaderProgram(descriptor.BindingLayout);

        public ISilkGraphicsPipeline CreateGraphicsPipeline(
            SilkGraphicsPipelineDescriptor descriptor)
        {
            descriptor.Validate();
            return new TestGraphicsPipeline(descriptor);
        }

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(
            SilkComputePipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsCommandList CreateCommandList() =>
            new TestCommandList(this);

        public ISilkGraphicsSubmission Submit(
            ISilkGraphicsCommandList commandList)
        {
            var commands = (TestCommandList)commandList;
            if (!commands.IsPick)
            {
                VisibleSubmissionCount++;
                return new TestSubmission();
            }

            PickSubmissionCount++;
            uint token = _pickTokens.Count == 0 ? 0 : _pickTokens.Dequeue();
            var submission = new TestSubmission(
                () => commands.CompletePick(token));
            _pickSubmissions.Add(submission);
            if (AutoCompletePickSubmissions)
            {
                submission.Complete();
            }
            return submission;
        }

        public ISilkPickGraphicsPipeline CreatePickGraphicsPipeline(
            SilkPickPipelineDescriptor descriptor)
        {
            descriptor.Validate();
            PickPipelineCreateCount++;
            return new TestPickPipeline(descriptor);
        }

        public ISilkPickReadbackBuffer CreatePickReadbackBuffer()
        {
            ReadbackBufferCreateCount++;
            return new TestReadbackBuffer();
        }

        public void EnqueuePickToken(uint token) => _pickTokens.Enqueue(token);

        public void CompleteNextPick()
        {
            TestSubmission submission = _pickSubmissions.First(
                candidate => !candidate.IsCompleted && !candidate.IsDisposed);
            submission.Complete();
        }

        public void CompleteAllPicks()
        {
            foreach (TestSubmission submission in _pickSubmissions)
            {
                if (!submission.IsCompleted && !submission.IsDisposed)
                {
                    submission.Complete();
                }
            }
        }

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestGraphicsBuffer(nuint size, SilkBufferUsage usage)
        : SilkGraphicsBufferBase(size, usage)
    {
        private readonly byte[] _data = new byte[checked((int)size)];

        public override void Write(ReadOnlySpan<byte> data, nuint offset = 0)
        {
            _ = ValidateWrite(data.Length, offset);
            data.CopyTo(_data.AsSpan(checked((int)offset)));
        }

        public override void ReadbackForTesting(Span<byte> destination)
        {
            _ = ValidateReadback(destination.Length);
            _data.CopyTo(destination);
        }

        protected override void ReleaseNative()
        {
        }
    }

    private sealed class TestTexture : SilkGraphicsTextureBase
    {
        internal TestTexture(
            uint width,
            uint height,
            SilkTextureFormat format)
            : base(width, height, format)
        {
        }

        internal TestTexture(SilkTextureDescriptor descriptor)
            : base(descriptor)
        {
        }

        public override void ReadbackForTesting(Span<byte> destination)
        {
            _ = ValidateReadback(destination.Length);
            destination.Clear();
        }

        public override void ReadbackForTesting(Span<float> destination)
        {
            _ = ValidateDepthReadback(destination.Length);
            destination.Clear();
        }

        protected override void ReleaseNative()
        {
        }
    }

    private sealed class TestReadbackBuffer : ISilkPickReadbackBuffer
    {
        private readonly byte[] _bytes =
            new byte[SilkPickTokenEncoding.ByteSize];
        private bool _disposed;

        public int ByteSize => _bytes.Length;

        public void ReadRgba8Pixel(Span<byte> destination)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (destination.Length != _bytes.Length)
            {
                throw new ArgumentException(
                    "The destination must contain exactly four bytes.",
                    nameof(destination));
            }
            _bytes.CopyTo(destination);
        }

        internal void SetToken(uint token) =>
            SilkPickTokenEncoding.Encode(token, _bytes);

        public void Dispose() => _disposed = true;
    }

    private sealed class TestCommandList(TestGraphicsDevice device) :
        ISilkGraphicsCommandList,
        ISilkPickGraphicsCommandList
    {
        private TestReadbackBuffer? _destination;

        internal bool IsPick { get; private set; }

        public void UploadTexture(
            ISilkGraphicsTexture texture,
            ReadOnlySpan<byte> source)
        {
        }

        public void ClearColor(ISilkGraphicsTexture texture, SilkColor color)
        {
        }

        public void ClearDepth(ISilkGraphicsTexture texture, float depth)
        {
        }

        public void BeginRendering(SilkRenderingDescriptor descriptor)
        {
        }

        public void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline)
        {
        }

        public void SetPickGraphicsPipeline(
            ISilkPickGraphicsPipeline pipeline) =>
            IsPick = true;

        public void SetViewport(SilkViewport viewport)
        {
        }

        public void SetScissor(SilkScissor scissor)
        {
            if (IsPick)
            {
                device.LastPickScissor = scissor;
            }
        }

        public void SetVertexBuffer(ISilkGraphicsBuffer buffer)
        {
        }

        public void SetIndexBuffer(ISilkGraphicsBuffer buffer)
        {
        }

        public void SetUniformBuffer(
            uint setIndex,
            uint binding,
            ISilkGraphicsBuffer buffer)
        {
        }

        public void SetTexture(
            uint setIndex,
            uint binding,
            ISilkGraphicsTexture texture)
        {
        }

        public void SetSampler(
            uint setIndex,
            uint binding,
            ISilkGraphicsSampler sampler)
        {
        }

        public void SetPickBaseToken(uint baseToken)
        {
            device.LastPickBaseTokens.Add(baseToken);
        }

        public void DrawIndexed(uint indexCount)
        {
        }

        public void DrawIndexedInstanced(uint indexCount, uint instanceCount)
        {
        }

        public void EndRendering()
        {
        }

        public void CopyRgba8Pixel(
            ISilkGraphicsTexture source,
            SilkTexturePixelCoordinate coordinate,
            ISilkPickReadbackBuffer destination)
        {
            IsPick = true;
            coordinate.Validate(source);
            device.LastPickSource = source;
            device.LastPickCoordinate = coordinate;
            _destination = (TestReadbackBuffer)destination;
        }

        public void SetComputePipeline(ISilkComputePipeline pipeline) =>
            throw new NotSupportedException();

        public void SetStorageBuffer(
            uint setIndex,
            uint binding,
            ISilkGraphicsBuffer buffer)
        {
        }

        public void SetComputeUniformBuffer(
            uint setIndex,
            uint binding,
            ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        public void Dispatch(uint elementCount) =>
            throw new NotSupportedException();

        public void BufferBarrier(ISilkGraphicsBuffer buffer) =>
            throw new NotSupportedException();

        internal void CompletePick(uint token) =>
            (_destination ??
                throw new InvalidOperationException(
                    "The pick command list has no copy destination."))
            .SetToken(token);

        public void Dispose()
        {
        }
    }

    private sealed class TestSubmission(Action? completion = null)
        : ISilkGraphicsSubmission
    {
        private Action? _completion = completion;

        public bool IsCompleted { get; private set; }

        internal bool IsDisposed { get; private set; }

        public void Complete()
        {
            if (IsCompleted || IsDisposed)
            {
                return;
            }
            Interlocked.Exchange(ref _completion, null)?.Invoke();
            IsCompleted = true;
        }

        public void Wait() => Complete();

        public void Dispose() => IsDisposed = true;
    }

    private sealed class CompletedSubmission : ISilkGraphicsSubmission
    {
        internal static CompletedSubmission Instance { get; } = new();

        public bool IsCompleted => true;

        public void Wait()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestShaderModule(
        SilkShaderModuleDescriptor descriptor)
        : ISilkGraphicsShaderModule
    {
        public SilkShaderModuleDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class TestBindingLayout(
        SilkBindingLayoutDescriptor descriptor)
        : ISilkGraphicsBindingLayout
    {
        public SilkBindingLayoutDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class TestShaderProgram(
        ISilkGraphicsBindingLayout bindingLayout)
        : ISilkGraphicsShaderProgram
    {
        public ISilkGraphicsBindingLayout BindingLayout { get; } =
            bindingLayout;

        public void Dispose()
        {
        }
    }

    private sealed class TestGraphicsPipeline(
        SilkGraphicsPipelineDescriptor descriptor)
        : ISilkGraphicsPipeline
    {
        public SilkGraphicsPipelineDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class TestPickPipeline(
        SilkPickPipelineDescriptor descriptor)
        : ISilkPickGraphicsPipeline
    {
        public SilkPickPipelineDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class TestSampler(SilkSamplerDescriptor descriptor)
        : ISilkGraphicsSampler
    {
        public SilkSamplerDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }
}
