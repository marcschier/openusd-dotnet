// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

[NotInParallel]
public sealed class SilkSelectionOutlineTests
{
    [Test]
    public async Task EmptyAndMissingSelectionsDoNotRecordOutlinePasses()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using TestTexture color = CreateColorTarget(device, 8, 6);
        using TestTexture depth = CreateDepthTarget(device, 8, 6);
        ApplyScene(renderer, 8, 6, 1, (7, "/Triangle"));

        _ = renderer.Render(color, depth);

        await Assert.That(device.MaskPassCount).IsEqualTo(0);
        await Assert.That(device.OutlinePassCount).IsEqualTo(0);
        await Assert.That(renderer.SelectionOutlineDiagnostics.Status)
            .IsEqualTo(SilkSelectionOutlineStatus.EmptySelection);

        renderer.UpdateSelection(new SelectionState(["/Missing"]));
        _ = renderer.Render(color, depth);

        SilkSelectionOutlineDiagnostics diagnostics =
            renderer.SelectionOutlineDiagnostics;
        await Assert.That(device.MaskPassCount).IsEqualTo(0);
        await Assert.That(device.OutlinePassCount).IsEqualTo(0);
        await Assert.That(device.SelectionMaskTargetCreateCount).IsEqualTo(0);
        await Assert.That(diagnostics.Status)
            .IsEqualTo(SilkSelectionOutlineStatus.NoMatchingMeshes);
        await Assert.That(diagnostics.MissingPathCount).IsEqualTo(1);
    }

    [Test]
    public async Task SelectedPathRecordsMaskAndFullscreenComposite()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using TestTexture color = CreateColorTarget(device, 8, 6);
        using TestTexture depth = CreateDepthTarget(device, 8, 6);
        ApplyScene(renderer, 8, 6, 1, (7, "/Triangle"));
        renderer.UpdateSelection(new SelectionState(["/Triangle"]));

        SilkMeshRenderResult result = renderer.Render(color, depth);

        SilkSelectionOutlineDiagnostics diagnostics =
            renderer.SelectionOutlineDiagnostics;
        await Assert.That(result.DrawCount).IsEqualTo(1);
        await Assert.That(device.MaskPassCount).IsEqualTo(1);
        await Assert.That(device.MaskDrawCount).IsEqualTo(1);
        await Assert.That(device.OutlinePassCount).IsEqualTo(1);
        await Assert.That(device.FullscreenDrawCount).IsEqualTo(1);
        await Assert.That(device.SelectionMaskPipelineCreateCount).IsEqualTo(1);
        await Assert.That(device.SelectionOutlinePipelineCreateCount).IsEqualTo(1);
        await Assert.That(device.SelectionMaskTargetCreateCount).IsEqualTo(1);
        await Assert.That(device.SelectionBindingCreateCount).IsEqualTo(1);
        await Assert.That(color.Data[0]).IsEqualTo((byte)0xfe);
        await Assert.That(diagnostics.Status)
            .IsEqualTo(SilkSelectionOutlineStatus.Rendered);
        await Assert.That(diagnostics.ResolvedMeshCount).IsEqualTo(1);
        await Assert.That(diagnostics.MissingPathCount).IsEqualTo(0);
        await Assert.That(diagnostics.MaskPasses).IsEqualTo(1UL);
        await Assert.That(diagnostics.OutlinePasses).IsEqualTo(1UL);
        await Assert.That(diagnostics.SelectedDraws).IsEqualTo(1UL);
    }

    [Test]
    public async Task UnsampledVisibleDepthDoesNotRequestSelectionPasses()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using TestTexture color = CreateColorTarget(device, 8, 6);
        using var depth = (TestTexture)device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(8, 6));
        ApplyScene(renderer, 8, 6, 1, (7, "/Triangle"));
        renderer.UpdateSelection(new SelectionState(["/Triangle"]));

        _ = renderer.Render(color, depth);

        await Assert.That(device.MaskPassCount).IsEqualTo(0);
        await Assert.That(device.SelectionMaskTargetCreateCount).IsEqualTo(0);
        await Assert.That(renderer.SelectionOutlineDiagnostics.Status)
            .IsEqualTo(SilkSelectionOutlineStatus.DepthSamplingUnsupported);
    }

    [Test]
    public async Task MultipleSelectionItemsDrawUniqueAuthoritativePaths()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using TestTexture color = CreateColorTarget(device, 8, 6);
        using TestTexture depth = CreateDepthTarget(device, 8, 6);
        ApplyScene(
            renderer,
            8,
            6,
            1,
            (7, "/A"),
            (8, "/B"));
        renderer.UpdateSelection(new SelectionState(
        [
            new SelectionItem("/A"),
            new SelectionItem(
                "/A",
                instancerPath: null,
                instanceIndex: null,
                elementIndex: 0,
                elementKind: SelectionElementKind.Face),
            new SelectionItem("/B")
        ]));

        _ = renderer.Render(color, depth);

        await Assert.That(device.MaskPassCount).IsEqualTo(1);
        await Assert.That(device.MaskDrawCount).IsEqualTo(2);
        await Assert.That(device.OutlinePassCount).IsEqualTo(1);
        await Assert.That(renderer.SelectionOutlineDiagnostics.SelectionItemCount)
            .IsEqualTo(3);
        await Assert.That(renderer.SelectionOutlineDiagnostics.ResolvedMeshCount)
            .IsEqualTo(2);
    }

    [Test]
    public async Task SelectionChangeDoesNotUploadMeshesOrChangePickIdentity()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using TestTexture color = CreateColorTarget(device, 8, 6);
        using TestTexture depth = CreateDepthTarget(device, 8, 6);
        ApplyScene(
            renderer,
            8,
            6,
            17,
            (7, "/A"),
            (8, "/B"));
        renderer.UpdateSelection(new SelectionState(["/A"]));
        _ = renderer.Render(color, depth);
        _ = renderer.Scene.PickIdentities.TryGetRange(
            "/A",
            out SilkPickTokenRange firstRange);
        SilkSceneGpuStatistics gpuBefore = renderer.GpuResources.Statistics;
        ulong gpuRevision = renderer.GpuResources.Revision;
        ulong sceneRevision = renderer.Scene.Revision;
        ulong identityRevision = renderer.Scene.PickIdentities.Revision;
        int maskPipelineCount = device.SelectionMaskPipelineCreateCount;
        int outlinePipelineCount = device.SelectionOutlinePipelineCreateCount;
        int maskTargetCount = device.SelectionMaskTargetCreateCount;
        int bindingCount = device.SelectionBindingCreateCount;

        renderer.UpdateSelection(new SelectionState(["/B"]));
        _ = renderer.Render(color, depth);
        _ = renderer.Scene.PickIdentities.TryGetRange(
            "/A",
            out SilkPickTokenRange secondRange);

        SilkSceneGpuStatistics gpuAfter = renderer.GpuResources.Statistics;
        await Assert.That(secondRange).IsEqualTo(firstRange);
        await Assert.That(renderer.Scene.PickIdentities.Revision)
            .IsEqualTo(identityRevision);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(sceneRevision);
        await Assert.That(renderer.GpuResources.Revision).IsEqualTo(gpuRevision);
        await Assert.That(gpuAfter.GeometryBuilds).IsEqualTo(gpuBefore.GeometryBuilds);
        await Assert.That(gpuAfter.VertexUploads).IsEqualTo(gpuBefore.VertexUploads);
        await Assert.That(gpuAfter.IndexUploads).IsEqualTo(gpuBefore.IndexUploads);
        await Assert.That(device.SelectionMaskPipelineCreateCount)
            .IsEqualTo(maskPipelineCount);
        await Assert.That(device.SelectionOutlinePipelineCreateCount)
            .IsEqualTo(outlinePipelineCount);
        await Assert.That(device.SelectionMaskTargetCreateCount)
            .IsEqualTo(maskTargetCount);
        await Assert.That(device.SelectionBindingCreateCount).IsEqualTo(bindingCount);
    }

    [Test]
    public async Task ResizeRecreatesMaskAndBindingButKeepsPipelines()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        ApplyScene(renderer, 8, 6, 1, (7, "/Triangle"));
        renderer.UpdateSelection(new SelectionState(["/Triangle"]));
        using (TestTexture color = CreateColorTarget(device, 8, 6))
        using (TestTexture depth = CreateDepthTarget(device, 8, 6))
        {
            _ = renderer.Render(color, depth);
        }

        using TestTexture resizedColor = CreateColorTarget(device, 10, 7);
        using TestTexture resizedDepth = CreateDepthTarget(device, 10, 7);
        _ = renderer.Render(resizedColor, resizedDepth);

        await Assert.That(device.SelectionMaskTargetCreateCount).IsEqualTo(2);
        await Assert.That(device.SelectionBindingCreateCount).IsEqualTo(2);
        await Assert.That(device.SelectionMaskPipelineCreateCount).IsEqualTo(1);
        await Assert.That(device.SelectionOutlinePipelineCreateCount).IsEqualTo(1);
        await Assert.That(device.SelectionParameterBufferCreateCount).IsEqualTo(1);
        await Assert.That(renderer.SelectionOutlineDiagnostics.ParameterUploads)
            .IsEqualTo(2UL);
    }

    [Test]
    public async Task DeviceGenerationInvalidatesAllCachedOutlineResources()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        ApplyScene(renderer, 8, 6, 1, (7, "/Triangle"));
        renderer.UpdateSelection(new SelectionState(["/Triangle"]));
        using (TestTexture color = CreateColorTarget(device, 8, 6))
        using (TestTexture depth = CreateDepthTarget(device, 8, 6))
        {
            _ = renderer.Render(color, depth);
        }

        device.SelectionOutlineDeviceGeneration++;
        using TestTexture replacementColor = CreateColorTarget(device, 8, 6);
        using TestTexture replacementDepth = CreateDepthTarget(device, 8, 6);
        _ = renderer.Render(replacementColor, replacementDepth);

        SilkSelectionOutlineDiagnostics diagnostics =
            renderer.SelectionOutlineDiagnostics;
        await Assert.That(device.SelectionMaskPipelineCreateCount).IsEqualTo(2);
        await Assert.That(device.SelectionOutlinePipelineCreateCount).IsEqualTo(2);
        await Assert.That(device.SelectionMaskTargetCreateCount).IsEqualTo(2);
        await Assert.That(device.SelectionBindingCreateCount).IsEqualTo(2);
        await Assert.That(device.SelectionParameterBufferCreateCount).IsEqualTo(2);
        await Assert.That(diagnostics.DeviceInvalidations).IsEqualTo(1UL);
    }

    [Test]
    public async Task WarmSelectionRenderingDoesNotAllocateOrChurnResources()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using TestTexture color = CreateColorTarget(device, 8, 6);
        using TestTexture depth = CreateDepthTarget(device, 8, 6);
        ApplyScene(renderer, 8, 6, 1, (7, "/Triangle"));
        renderer.UpdateSelection(new SelectionState(["/Triangle"]));
        for (int warmup = 0; warmup < 4; warmup++)
        {
            _ = renderer.Render(color, depth);
        }
        int bufferCount = device.BufferCreateCount;
        int maskPipelineCount = device.SelectionMaskPipelineCreateCount;
        int outlinePipelineCount = device.SelectionOutlinePipelineCreateCount;
        int maskTargetCount = device.SelectionMaskTargetCreateCount;
        int bindingCount = device.SelectionBindingCreateCount;
        int samplerCount = device.SamplerCreateCount;

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            _ = renderer.Render(color, depth);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsLessThan(930_000);
        await Assert.That(device.BufferCreateCount).IsEqualTo(bufferCount);
        await Assert.That(device.SelectionMaskPipelineCreateCount)
            .IsEqualTo(maskPipelineCount);
        await Assert.That(device.SelectionOutlinePipelineCreateCount)
            .IsEqualTo(outlinePipelineCount);
        await Assert.That(device.SelectionMaskTargetCreateCount)
            .IsEqualTo(maskTargetCount);
        await Assert.That(device.SelectionBindingCreateCount).IsEqualTo(bindingCount);
        await Assert.That(device.SamplerCreateCount).IsEqualTo(samplerCount);
    }

    [Test]
    public async Task DisabledAndXRayPoliciesLeaveVisibleTargetUncomposited()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using TestTexture color = CreateColorTarget(device, 8, 6);
        using TestTexture depth = CreateDepthTarget(device, 8, 6);
        ApplyScene(renderer, 8, 6, 1, (7, "/Triangle"));
        var disabled = new SilkSelectionOutlineSettings(
            enabled: false,
            SilkSelectionOutlineSettings.Default.Color,
            SilkSelectionOutlineSettings.Default.Width,
            visibleOnly: true);
        renderer.UpdateSelection(new SelectionState(["/Triangle"]), disabled);

        _ = renderer.Render(
            color,
            depth,
            new SilkMeshRenderOptions(new SilkColor(0.2f, 0.3f, 0.4f, 1), 1));

        await Assert.That(color.Data[0]).IsEqualTo((byte)51);
        await Assert.That(device.MaskPassCount).IsEqualTo(0);
        await Assert.That(renderer.SelectionOutlineDiagnostics.Status)
            .IsEqualTo(SilkSelectionOutlineStatus.Disabled);

        var xray = new SilkSelectionOutlineSettings(
            enabled: true,
            SilkSelectionOutlineSettings.Default.Color,
            SilkSelectionOutlineSettings.Default.Width,
            visibleOnly: false);
        renderer.UpdateSelection(renderer.Selection, xray);
        _ = renderer.Render(color, depth);

        await Assert.That(device.MaskPassCount).IsEqualTo(0);
        await Assert.That(renderer.SelectionOutlineCapabilities.SupportsVisibleOnly)
            .IsTrue();
        await Assert.That(renderer.SelectionOutlineCapabilities.SupportsXRay)
            .IsFalse();
        await Assert.That(renderer.SelectionOutlineDiagnostics.Status)
            .IsEqualTo(SilkSelectionOutlineStatus.XRayUnsupported);
        await Assert.That(renderer.SelectionOutlineDiagnostics.UnsupportedXRayRequests)
            .IsEqualTo(1UL);
    }

    [Test]
    public async Task SettingsRejectNonFiniteColorAndOutOfRangeWidth()
    {
        await Assert.That(
            () => new SilkSelectionOutlineSettings(
                true,
                new SilkColor(float.NaN, 0, 0, 1),
                2,
                true)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(
            () => new SilkSelectionOutlineSettings(
                true,
                new SilkColor(1, 1, 1, 1),
                float.NaN,
                true)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(
            () => new SilkSelectionOutlineSettings(
                true,
                new SilkColor(1, 1, 1, 1),
                SilkSelectionOutlineSettings.MinimumWidth - 0.1f,
                true)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(
            () => new SilkSelectionOutlineSettings(
                true,
                new SilkColor(1, 1, 1, 1),
                SilkSelectionOutlineSettings.MaximumWidth + 1,
                true)).Throws<ArgumentOutOfRangeException>();
    }

    private static TestTexture CreateColorTarget(
        TestGraphicsDevice device,
        uint width,
        uint height) =>
        (TestTexture)device.CreateTexture2D(new SilkTextureDescriptor(
            width,
            height,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureUsage.ColorRenderTarget | SilkTextureUsage.CopySource));

    private static TestTexture CreateDepthTarget(
        TestGraphicsDevice device,
        uint width,
        uint height) =>
        (TestTexture)device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(width, height));

    /// <summary>
    /// A face, edge, or point selection masks only the named component, in the
    /// topology that component is drawn with, rather than the whole prim.
    /// </summary>
    /// <remarks>
    /// The mask is what the outline is drawn around, so scope is the difference
    /// between showing what the user selected and showing something that merely
    /// contains it. A face selection that produced the prim's whole silhouette
    /// would be indistinguishable from selecting the prim.
    /// </remarks>
    /// <summary>
    /// A legacy item whose element kind was never stated still masks the
    /// authored face its index names.
    /// </summary>
    /// <remarks>
    /// The four-parameter constructor predates the kind, so a producer using it
    /// could only ever have meant a face. The mask needs a concrete component to
    /// scope to, so it resolves the unstated kind that way and keeps the
    /// long-standing highlight behavior; the item's own identity still reports
    /// <see cref="SelectionElementKind.Unspecified"/>, so nothing downstream is
    /// told the index is a face.
    /// </remarks>
    [Test]
    public async Task AnUnstatedElementKindMasksTheAuthoredFace()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using TestTexture color = CreateColorTarget(device, 8, 6);
        using TestTexture depth = CreateDepthTarget(device, 8, 6);
        ApplySubprimScene(renderer, 8, 6, 1);

        var legacy = new SelectionItem("/Quad", null, null, elementIndex: 1);
        await Assert.That(legacy.ElementKind)
            .IsEqualTo(SelectionElementKind.Unspecified);

        renderer.UpdateSelection(new SelectionState([legacy]));
        _ = renderer.Render(color, depth);

        await Assert.That(device.LastMaskIndexCount).IsEqualTo(3u);
        await Assert.That(device.LastMaskTopology)
            .IsEqualTo(SilkSelectionMaskPrimitiveTopology.TriangleList);
    }

    [Test]
    public async Task AComponentSelectionMasksOnlyThatComponent()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using TestTexture color = CreateColorTarget(device, 8, 6);
        using TestTexture depth = CreateDepthTarget(device, 8, 6);
        ApplySubprimScene(renderer, 8, 6, 1);

        renderer.UpdateSelection(new SelectionState([new SelectionItem("/Quad")]));
        _ = renderer.Render(color, depth);
        uint wholePrim = device.LastMaskIndexCount;

        renderer.UpdateSelection(new SelectionState(
        [
            new SelectionItem(
                "/Quad",
                instancerPath: null,
                instanceIndex: null,
                elementIndex: 1,
                elementKind: SelectionElementKind.Face)
        ]));
        _ = renderer.Render(color, depth);

        // The prim draws both triangles; authored face one is a single
        // triangle, so the scoped draw is exactly three indices.
        await Assert.That(wholePrim).IsEqualTo(6u);
        await Assert.That(device.LastMaskIndexCount).IsEqualTo(3u);
        await Assert.That(device.LastMaskTopology)
            .IsEqualTo(SilkSelectionMaskPrimitiveTopology.TriangleList);

        renderer.UpdateSelection(new SelectionState(
        [
            new SelectionItem(
                "/Quad",
                instancerPath: null,
                instanceIndex: null,
                elementIndex: 2,
                elementKind: SelectionElementKind.Edge)
        ]));
        _ = renderer.Render(color, depth);

        await Assert.That(device.LastMaskTopology)
            .IsEqualTo(SilkSelectionMaskPrimitiveTopology.LineList);
        await Assert.That(device.LastMaskIndexCount).IsEqualTo(2u);

        renderer.UpdateSelection(new SelectionState(
        [
            new SelectionItem(
                "/Quad",
                instancerPath: null,
                instanceIndex: null,
                elementIndex: 3,
                elementKind: SelectionElementKind.Point)
        ]));
        _ = renderer.Render(color, depth);

        await Assert.That(device.LastMaskTopology)
            .IsEqualTo(SilkSelectionMaskPrimitiveTopology.PointList);
        await Assert.That(device.LastMaskIndexCount).IsEqualTo(1u);
    }

    /// <summary>
    /// A component the retained mesh cannot resolve exactly produces no mask
    /// draw at all rather than a broader one.
    /// </summary>
    [Test]
    public async Task AnUnresolvableComponentMasksNothingRatherThanTheWholePrim()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using TestTexture color = CreateColorTarget(device, 8, 6);
        using TestTexture depth = CreateDepthTarget(device, 8, 6);
        ApplySubprimScene(renderer, 8, 6, 1);

        renderer.UpdateSelection(new SelectionState(
        [
            new SelectionItem(
                "/Quad",
                instancerPath: null,
                instanceIndex: null,
                elementIndex: 97,
                elementKind: SelectionElementKind.Face)
        ]));
        _ = renderer.Render(color, depth);

        await Assert.That(renderer.SelectionOutlineDiagnostics.ResolvedMeshCount)
            .IsEqualTo(0);
        await Assert.That(device.MaskDrawCount).IsEqualTo(0);
    }

    /// <summary>
    /// A prim selected whole already contains every component of itself, so a
    /// component item for the same prim adds no second mask draw.
    /// </summary>
    [Test]
    public async Task AWholePrimSelectionSubsumesAComponentOfTheSamePrim()
    {
        using var device = new TestGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using TestTexture color = CreateColorTarget(device, 8, 6);
        using TestTexture depth = CreateDepthTarget(device, 8, 6);
        ApplySubprimScene(renderer, 8, 6, 1);

        renderer.UpdateSelection(new SelectionState(
        [
            new SelectionItem("/Quad"),
            new SelectionItem(
                "/Quad",
                instancerPath: null,
                instanceIndex: null,
                elementIndex: 0,
                elementKind: SelectionElementKind.Face)
        ]));
        _ = renderer.Render(color, depth);

        await Assert.That(device.MaskDrawCount).IsEqualTo(1);
        await Assert.That(device.LastMaskIndexCount).IsEqualTo(6u);
    }

    private static void ApplySubprimScene(
        SilkMeshRenderer renderer,
        uint width,
        uint height,
        ulong pageRevision)
    {
        byte[] frame = CreateFrameCommand(width, height);
        byte[] mesh = CreateSubprimQuadCommand();
        var page = new byte[frame.Length + mesh.Length];
        frame.CopyTo(page, 0);
        mesh.CopyTo(page, frame.Length);
        SilkSceneDelta delta = renderer.Scene.Apply(page, 2, pageRevision);
        renderer.GpuResources.Apply(renderer.Scene, delta);
    }

    /// <summary>
    /// A quad with two authored faces, four authored points, and four authored
    /// edges, so a component selection has a strictly smaller scope than the
    /// prim and every subprim topology has something to mask.
    /// </summary>
    private static byte[] CreateSubprimQuadCommand()
    {
        byte[] path = Encoding.UTF8.GetBytes("/Quad");
        float[] points =
        [
            -0.5f, -0.5f, 0,
            0.5f, -0.5f, 0,
            0.5f, 0.5f, 0,
            -0.5f, 0.5f, 0
        ];
        uint[] indices = [0, 1, 2, 0, 2, 3];
        uint[] subprims = [0, 1];
        uint[] pointOrigins = [0, 1, 2, 3];
        uint[] cornerEdges = [0, 1, 0xFFFFFFFFu, 0xFFFFFFFFu, 2, 3];
        int size = 268 +
            path.Length +
            (points.Length * sizeof(float)) +
            (indices.Length * sizeof(uint)) +
            (subprims.Length * sizeof(uint)) +
            (pointOrigins.Length * sizeof(uint)) +
            (cornerEdges.Length * sizeof(uint));
        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes,
            (uint)SilkCommandType.MeshUpsert);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)size);
        BinaryPrimitives.WriteUInt64LittleEndian(
            bytes.AsSpan(8),
            SilkWireFormat.ComputeStableHash("/Quad"));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 11);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(28),
            (uint)SilkTopologyKind.TriangleList);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(44),
            (uint)SilkMeshCullStyle.BackUnlessDoubleSided);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(48),
            (uint)path.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 2);
        for (int index = 0; index < 4; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                bytes.AsSpan(64 + (index * sizeof(float))),
                1);
        }
        for (int index = 0; index < 16; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (index * sizeof(double))),
                index % 5 == 0 ? 1 : 0);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(236), 1 | 2 | 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(244), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(248), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(252), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(256), 4);
        path.CopyTo(bytes, 268);
        int cursor = 268 + path.Length;
        foreach (float value in points)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(float);
        }
        foreach (uint value in indices)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(uint);
        }
        foreach (uint value in subprims)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(uint);
        }
        foreach (uint value in pointOrigins)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(uint);
        }
        foreach (uint value in cornerEdges)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(cursor), value);
            cursor += sizeof(uint);
        }
        return bytes;
    }

    private static void ApplyScene(
        SilkMeshRenderer renderer,
        uint width,
        uint height,
        ulong pageRevision,
        params (int PrimId, string Path)[] meshes)
    {
        byte[] frame = CreateFrameCommand(width, height);
        var commands = new byte[meshes.Length + 1][];
        commands[0] = frame;
        int totalSize = frame.Length;
        for (int index = 0; index < meshes.Length; index++)
        {
            commands[index + 1] = CreateMeshCommand(
                meshes[index].PrimId,
                meshes[index].Path);
            totalSize += commands[index + 1].Length;
        }

        var page = new byte[totalSize];
        int offset = 0;
        foreach (byte[] command in commands)
        {
            command.CopyTo(page, offset);
            offset += command.Length;
        }
        SilkSceneDelta delta = renderer.Scene.Apply(
            page,
            checked((uint)commands.Length),
            pageRevision);
        renderer.GpuResources.Apply(renderer.Scene, delta);
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
                bytes.AsSpan(16 + (index * sizeof(double))),
                value);
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(144 + (index * sizeof(double))),
                value);
        }
        return bytes;
    }

    private static byte[] CreateMeshCommand(int primId, string pathValue)
    {
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
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), 1);
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
                bytes.AsSpan(64 + (index * sizeof(float))),
                1);
        }
        for (int index = 0; index < 16; index++)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                bytes.AsSpan(80 + (index * sizeof(double))),
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
            0);
        return bytes;
    }

    private sealed class TestGraphicsDevice :
        ISilkGraphicsDevice,
        ISilkSelectionOutlineGraphicsDevice
    {
        private readonly TestCommandList _commandList;
        private readonly TestSubmission _submission = new();

        internal TestGraphicsDevice()
        {
            _commandList = new TestCommandList(this);
        }

        internal int BufferCreateCount { get; private set; }

        internal int SamplerCreateCount { get; private set; }

        internal int SelectionParameterBufferCreateCount { get; private set; }

        internal int SelectionMaskTargetCreateCount { get; private set; }

        internal int SelectionMaskPipelineCreateCount { get; private set; }

        internal int SelectionOutlinePipelineCreateCount { get; private set; }

        internal int SelectionBindingCreateCount { get; private set; }

        internal int MaskPassCount { get; set; }

        internal int MaskDrawCount { get; set; }

        /// <summary>The index count of the most recent mask draw.</summary>
        internal uint LastMaskIndexCount { get; set; }

        /// <summary>The topology the most recent mask pipeline bind selected.</summary>
        internal SilkSelectionMaskPrimitiveTopology LastMaskTopology { get; set; }

        internal int OutlinePassCount { get; set; }

        internal int FullscreenDrawCount { get; set; }

        public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

        public SilkGraphicsCapabilities Capabilities { get; } =
            new("Selection test", "1", SupportsCompute: false, IsSoftware: true);

        public ulong SelectionOutlineDeviceGeneration { get; set; } = 1;

        public SilkSelectionOutlineCapabilities SelectionOutlineCapabilities { get; set; } =
            SilkSelectionOutlineCapabilities.VisibleOnly;

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage)
        {
            BufferCreateCount++;
            if (size == SilkSelectionOutlineUniformWriter.ByteSize &&
                usage == (SilkBufferUsage.Uniform | SilkBufferUsage.Upload))
            {
                SelectionParameterBufferCreateCount++;
            }
            return new TestBuffer(size, usage);
        }

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            new TestTexture(new SilkTextureDescriptor(
                width,
                height,
                format,
                SilkTextureDescriptor.GetDefaultUsage(format)));

        public ISilkGraphicsTexture CreateTexture2D(
            SilkTextureDescriptor descriptor)
        {
            descriptor.Validate();
            if (descriptor == SilkTextureDescriptor.SelectionMask(
                descriptor.Width,
                descriptor.Height))
            {
                SelectionMaskTargetCreateCount++;
            }
            return new TestTexture(descriptor);
        }

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor)
        {
            descriptor.Validate();
            SamplerCreateCount++;
            return new TestSampler(descriptor);
        }

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

        public ISilkGraphicsCommandList CreateCommandList()
        {
            _commandList.Reset();
            return _commandList;
        }

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
            _submission;

        public ISilkSelectionMaskGraphicsPipeline
            CreateSelectionMaskGraphicsPipeline(
                SilkSelectionMaskPipelineDescriptor descriptor)
        {
            descriptor.Validate();
            SelectionMaskPipelineCreateCount++;
            return new TestSelectionMaskPipeline(descriptor);
        }

        public ISilkSelectionOutlineGraphicsPipeline
            CreateSelectionOutlineGraphicsPipeline(
                SilkSelectionOutlinePipelineDescriptor descriptor)
        {
            descriptor.Validate();
            SelectionOutlinePipelineCreateCount++;
            return new TestSelectionOutlinePipeline(descriptor);
        }

        public ISilkSelectionOutlineBinding CreateSelectionOutlineBinding(
            SilkSelectionOutlineBindingDescriptor descriptor)
        {
            descriptor.Validate();
            SelectionBindingCreateCount++;
            return new TestSelectionBinding(descriptor);
        }

        public void WaitIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestCommandList :
        ISilkGraphicsCommandList,
        ISilkSelectionOutlineGraphicsCommandList
    {
        private readonly TestGraphicsDevice _device;
        private RenderScope _scope;
        private TestTexture? _maskTarget;
        private TestTexture? _visibleTarget;

        internal TestCommandList(TestGraphicsDevice device)
        {
            _device = device;
        }

        internal void Reset()
        {
            _scope = RenderScope.None;
            _maskTarget = null;
            _visibleTarget = null;
        }

        public void UploadTexture(
            ISilkGraphicsTexture texture,
            ReadOnlySpan<byte> source) =>
            source.CopyTo(((TestTexture)texture).Data);

        public void ClearColor(ISilkGraphicsTexture texture, SilkColor color)
        {
            var target = (TestTexture)texture;
            byte red = ToByte(color.Red);
            byte green = ToByte(color.Green);
            byte blue = ToByte(color.Blue);
            byte alpha = ToByte(color.Alpha);
            for (int offset = 0; offset < target.Data.Length; offset += 4)
            {
                target.Data[offset] = red;
                target.Data[offset + 1] = green;
                target.Data[offset + 2] = blue;
                target.Data[offset + 3] = alpha;
            }
        }

        public void ClearDepth(ISilkGraphicsTexture texture, float depth) =>
            ((TestTexture)texture).DepthData.AsSpan().Fill(depth);

        public void BeginRendering(SilkRenderingDescriptor descriptor)
        {
            _scope = RenderScope.Visible;
            _visibleTarget = (TestTexture)descriptor.ColorAttachment;
        }

        public void SetGraphicsPipeline(ISilkGraphicsPipeline pipeline)
        {
        }

        public void SetViewport(SilkViewport viewport) => viewport.Validate();

        public void SetScissor(SilkScissor scissor) => scissor.Validate();

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

        public void DrawIndexed(uint indexCount)
        {
            if (_scope == RenderScope.Mask)
            {
                _device.MaskDrawCount++;
                _device.LastMaskIndexCount = indexCount;
                if (_maskTarget is not null)
                {
                    _maskTarget.Data[0] = byte.MaxValue;
                    _maskTarget.Data[1] = byte.MaxValue;
                    _maskTarget.Data[2] = byte.MaxValue;
                    _maskTarget.Data[3] = byte.MaxValue;
                }
            }
        }

        public void DrawIndexedInstanced(uint indexCount, uint instanceCount)
        {
            DrawIndexed(indexCount);
        }

        public void EndRendering() => _scope = RenderScope.None;

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

        public void BeginSelectionMaskRendering(
            SilkSelectionMaskRenderingDescriptor descriptor)
        {
            descriptor.Validate();
            _scope = RenderScope.Mask;
            _maskTarget = (TestTexture)descriptor.MaskAttachment;
            _device.MaskPassCount++;
        }

        public void SetSelectionMaskGraphicsPipeline(
            ISilkSelectionMaskGraphicsPipeline pipeline) =>
            _device.LastMaskTopology =
                pipeline.Descriptor.PrimitiveTopology;

        public void BeginSelectionOutlineRendering(
            SilkSelectionOutlineRenderingDescriptor descriptor)
        {
            descriptor.Validate();
            _scope = RenderScope.Outline;
            _visibleTarget = (TestTexture)descriptor.VisibleColorAttachment;
            _device.OutlinePassCount++;
        }

        public void SetSelectionOutlineGraphicsPipeline(
            ISilkSelectionOutlineGraphicsPipeline pipeline)
        {
        }

        public void SetSelectionOutlineBinding(
            ISilkSelectionOutlineBinding binding)
        {
        }

        public void DrawSelectionOutlineFullscreenTriangle()
        {
            if (_scope != RenderScope.Outline || _visibleTarget is null)
            {
                throw new InvalidOperationException();
            }
            _device.FullscreenDrawCount++;
            _visibleTarget.Data[0] = 0xfe;
        }

        public void Dispose()
        {
        }

        private static byte ToByte(float value) =>
            checked((byte)MathF.Round(value * byte.MaxValue));

        private enum RenderScope
        {
            None,
            Visible,
            Mask,
            Outline
        }
    }

    private sealed class TestBuffer(nuint size, SilkBufferUsage usage)
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
        internal TestTexture(SilkTextureDescriptor descriptor)
            : base(descriptor)
        {
            Data = descriptor.Format == SilkTextureFormat.Rgba8Unorm
                ? new byte[checked((int)(descriptor.Width * descriptor.Height * 4))]
                : [];
            DepthData = descriptor.Format == SilkTextureFormat.D32Float
                ? new float[checked((int)(descriptor.Width * descriptor.Height))]
                : [];
        }

        internal byte[] Data { get; }

        internal float[] DepthData { get; }

        public override void ReadbackForTesting(Span<byte> destination)
        {
            _ = ValidateReadback(destination.Length);
            Data.CopyTo(destination);
        }

        public override void ReadbackForTesting(Span<float> destination)
        {
            _ = ValidateDepthReadback(destination.Length);
            DepthData.CopyTo(destination);
        }

        protected override void ReleaseNative()
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

    private sealed class TestShaderModule(SilkShaderModuleDescriptor descriptor)
        : ISilkGraphicsShaderModule
    {
        public SilkShaderModuleDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class TestBindingLayout(SilkBindingLayoutDescriptor descriptor)
        : ISilkGraphicsBindingLayout
    {
        public SilkBindingLayoutDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class TestShaderProgram(ISilkGraphicsBindingLayout bindingLayout)
        : ISilkGraphicsShaderProgram
    {
        public ISilkGraphicsBindingLayout BindingLayout { get; } = bindingLayout;

        public void Dispose()
        {
        }
    }

    private sealed class TestGraphicsPipeline(
        SilkGraphicsPipelineDescriptor descriptor) : ISilkGraphicsPipeline
    {
        public SilkGraphicsPipelineDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class TestSelectionMaskPipeline(
        SilkSelectionMaskPipelineDescriptor descriptor)
        : ISilkSelectionMaskGraphicsPipeline
    {
        public SilkSelectionMaskPipelineDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class TestSelectionOutlinePipeline(
        SilkSelectionOutlinePipelineDescriptor descriptor)
        : ISilkSelectionOutlineGraphicsPipeline
    {
        public SilkSelectionOutlinePipelineDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class TestSelectionBinding(
        SilkSelectionOutlineBindingDescriptor descriptor)
        : ISilkSelectionOutlineBinding
    {
        public SilkSelectionOutlineBindingDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class TestSubmission : ISilkGraphicsSubmission
    {
        public bool IsCompleted => true;

        public void Wait()
        {
        }

        public void Dispose()
        {
        }
    }
}
