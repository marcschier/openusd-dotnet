// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

/// <summary>
/// Backend-neutral helpers for the display-transform conformance gates.
/// </summary>
internal static class DisplayTransformConformance
{
    internal const uint Width = 32;
    internal const uint Height = 24;

    internal static async Task OutputResizeUsesExactOverlap(SilkGraphicsBackend backend, bool oneByteShort)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var textures = new SilkGpuTextureBudget(oneByteShort ? 999_999ul : 1_000_000ul);
        textures.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Width, Height));
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. CreateQuad(1, "/Mesh", -0.6f, 0.6f, 0.6f, -0.6f, 0.5f)], 2);
        _ = renderer.ApplyAndRender(first, color, depth);
        byte[] before = new byte[Width * Height * 4];
        color.ReadbackForTesting(before);
        using ISilkGraphicsTexture larger = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(Width * 2, Height * 2));
        using ISilkGraphicsTexture largerDepth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(Width * 2, Height * 2));
        using (ISilkGraphicsCommandList initialize = device.CreateCommandList())
        {
            initialize.ClearColor(larger, new(1, 0, 1, 1));
            initialize.ClearDepth(largerDepth, 0.875f);
            using ISilkGraphicsSubmission submission = device.Submit(initialize);
            submission.Wait();
        }
        byte[] sentinel = new byte[before.Length * 4];
        larger.ReadbackForTesting(sentinel);
        ulong retained = textures.Usage.ReservedBytes;
        const ulong newTargets = Width * 2 * Height * 2 * 8;
        ulong pressure = 1_000_000 - retained - newTargets;
        using var page = new OpenUsdSilkPage(30, 2, [], 0);
        using (textures.Reserve(SilkTextureDescriptor.ColorTarget(checked((uint)(pressure / 4)), 1), 1))
        {
            if (oneByteShort)
            {
                await Assert.That(() => renderer.ApplyAndRender(page, larger, largerDepth))
                    .Throws<SilkGpuTextureBudgetExceededException>();
                await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
                await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(retained + pressure);
                byte[] unchanged = new byte[sentinel.Length];
                larger.ReadbackForTesting(unchanged);
                await Assert.That(unchanged.SequenceEqual(sentinel)).IsTrue();
                _ = renderer.Render(color, depth);
                byte[] old = new byte[before.Length];
                color.ReadbackForTesting(old);
                await Assert.That(old.SequenceEqual(before)).IsTrue();
            }
            else
            {
                _ = renderer.ApplyAndRender(page, larger, largerDepth);
                await Assert.That(renderer.Scene.Revision).IsEqualTo(2ul);
                await Assert.That(textures.Usage.PeakReservedBytes).IsEqualTo(textures.MaximumBytes);
                await Assert.That(textures.Usage.ReservedBytes)
                    .IsEqualTo(textures.MaximumBytes - (Width * Height * 8ul));
                _ = renderer.Render(larger, largerDepth);
                await Assert.That(textures.Usage.PeakReservedBytes).IsEqualTo(textures.MaximumBytes);
            }
        }
        if (oneByteShort)
        {
            _ = renderer.ApplyAndRender(page, larger, largerDepth);
        }
        byte[] after = new byte[sentinel.Length];
        larger.ReadbackForTesting(after);
        await Assert.That(after.SequenceEqual(sentinel)).IsFalse();
        renderer.Dispose();
        largerDepth.Dispose();
        larger.Dispose();
        depth.Dispose();
        color.Dispose();
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    internal static async Task CancelledCapturePreservesFrameAndReplay(SilkGraphicsBackend backend)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        var provider = new SolidLatticeProvider();
        var cache = new SilkDisplayTransformLatticeCache(provider, identityProvider: provider);
        using var renderer = new SilkMeshRenderer(device,
            backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV,
            displayLattices: cache);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Width, Height));
        var transform = new RenderDisplayTransform(Path.Combine(Path.GetTempPath(), "synthetic-cancellation.ocio"),
            "linear", latticeSize: 8);
        var options = SilkMeshRenderOptions.Default with { DisplayTransform = transform };
        byte[] mesh = CreateQuad(1, "/Mesh", -0.7f, 0.5f, -0.1f, -0.5f, 0.5f);
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. mesh], 2);
        _ = renderer.ApplyAndRender(first, color, depth, options);
        byte[] before = new byte[Width * Height * 4];
        color.ReadbackForTesting(before);
        ulong reserved = budget.Usage.ReservedBytes;
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(mesh.AsSpan(176), 0.7);
        using var changed = new OpenUsdSilkPage(30, 2, mesh, 1);
        using var cancellation = new CancellationTokenSource();
        var capture = SilkDepthCaptureRequest.ForHdrColor(
            checked((int)Width), checked((int)Height), null, cancellation.Token);
        device.CancelAfterSubmission = cancellation;
        await Assert.That(() => renderer.ApplyAndRenderForDisplayCapture(
            changed, color, depth, options, capture, out _)).Throws<OperationCanceledException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        byte[] retained = new byte[before.Length];
        color.ReadbackForTesting(retained);
        await Assert.That(retained.SequenceEqual(before)).IsTrue();
        using var following = new OpenUsdSilkPage(30, 3, [], 0);
        renderer.ApplyPage(following);
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(3ul);
        await Assert.That(renderer.Scene.Meshes.Values.Single().Transform.Span[12]).IsEqualTo(0.7);
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    internal static async Task CompletedPublicationSurvivesCleanupFailure(SilkGraphicsBackend backend)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Width, Height));
        byte[] mesh = CreateQuad(1, "/Mesh", -0.7f, 0.5f, -0.1f, -0.5f, 0.5f);
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. mesh], 2);
        _ = renderer.ApplyAndRender(first, color, depth);
        byte[] before = new byte[Width * Height * 4];
        color.ReadbackForTesting(before);
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(mesh.AsSpan(176), 0.7);
        using var changed = new OpenUsdSilkPage(30, 2, mesh, 1);
        device.FailSubmissionCleanupAfter = 1;
        await Assert.That(() => renderer.ApplyAndRender(changed, color, depth)).Throws<InvalidOperationException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2ul);
        await Assert.That(renderer.GpuResources.MeshValues.Single().Mesh.Transform.Span[12]).IsEqualTo(0.7);
        byte[] published = new byte[before.Length];
        color.ReadbackForTesting(published);
        await Assert.That(published.SequenceEqual(before)).IsFalse();
        using var following = new OpenUsdSilkPage(30, 3, [], 0);
        renderer.ApplyPage(following);
        _ = renderer.Render(color, depth);
        byte[] after = new byte[before.Length];
        color.ReadbackForTesting(after);
        await Assert.That(after.SequenceEqual(published)).IsTrue();
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    internal static async Task FinalPublicationKeepsPhysicsGeometry(SilkGraphicsBackend backend, bool retainedFrame)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Width, Height));
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. CreateQuad(1, "/Cloth", -0.7f, 0.5f, -0.1f, -0.5f, 0.5f)], 2);
        _ = renderer.ApplyAndRender(first, color, depth);
        SilkMeshData authored = renderer.Scene.Meshes.Values.Single();
        SilkMeshGpuResource original = renderer.GpuResources.MeshValues.Single();
        byte[] before = new byte[Width * Height * 4];
        color.ReadbackForTesting(before);
        ulong reserved = budget.Usage.ReservedBytes;
        var id = new PhysicsRenderObjectId(1, PhysicsRenderObjectKind.Deformable);
        var bindings = new PhysicsRenderBindingTable(1);
        await Assert.That(bindings.TryBind(id, "/Cloth")).IsTrue();
        float[] points = authored.Points.ToArray();
        for (int index = 0; index < points.Length; index += 3)
        {
            points[index] += 0.7f;
        }
        var deformations = new SilkPhysicsDeformations();
        PhysicsRenderDeformableRegion[] regions = [new(id, PhysicsRenderDomain.Cloth, 0, 4, 1)];
        _ = deformations.Stage(bindings, new PhysicsRenderDeformationView(
            regions, points, revision: 1));
        renderer.PhysicsDeformations = deformations;
        ulong physicsRevision = deformations.Revision;
        if (retainedFrame)
        {
            byte[] incoming = CreateQuad(1, "/Cloth", -0.7f, 0.5f, -0.1f, -0.5f, 0.5f);
            System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(incoming.AsSpan(176), 0.2);
            using var pending = new OpenUsdSilkPage(30, 2, incoming, 1);
            device.RefuseSubmission = true;
            await Assert.That(() => renderer.ApplyAndRender(pending, color, depth))
                .Throws<SilkGpuStagingBudgetExceededException>();
            device.RefuseSubmission = false;
        }
        using var changed = new OpenUsdSilkPage(30, 2, [], 0);
        Action render = retainedFrame ? () => renderer.Render(color, depth) :
            () => renderer.ApplyAndRender(changed, color, depth);
        device.RefuseSubmissionAfter = 1;
        await Assert.That(render).Throws<SilkGpuStagingBudgetExceededException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        await Assert.That(renderer.Scene.Meshes.Values.Single()).IsSameReferenceAs(authored);
        await Assert.That(renderer.GpuResources.MeshValues.Single()).IsSameReferenceAs(original);
        await Assert.That(renderer.Scene.HasAuthoredGeometry(authored.Id)).IsFalse();
        await Assert.That(deformations.Count).IsEqualTo(0);
        await Assert.That(deformations.Revision).IsEqualTo(physicsRevision);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        byte[] retained = new byte[before.Length];
        color.ReadbackForTesting(retained);
        await Assert.That(retained.SequenceEqual(before)).IsTrue();
        device.RefuseSubmissionAfter = null;
        render();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(retainedFrame ? 1ul : 2ul);
        await Assert.That(renderer.Scene.Meshes.Values.Single().Points.Span.SequenceEqual(points)).IsTrue();
        await Assert.That(deformations.Count).IsEqualTo(1);
        await Assert.That(renderer.Scene.HasAuthoredGeometry(authored.Id)).IsTrue();
        color.ReadbackForTesting(retained);
        await Assert.That(retained.SequenceEqual(before)).IsFalse();
        if (retainedFrame)
        {
            using var following = new OpenUsdSilkPage(30, 3, [], 0);
            renderer.ApplyPage(following);
            await Assert.That(renderer.Scene.Revision).IsEqualTo(3ul);
            await Assert.That(renderer.Scene.Meshes.Values.Single().Transform.Span[12]).IsEqualTo(0.2);
        }
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    internal static async Task FinalSubmissionKeepsPageUnpublished(
        SilkGraphicsBackend backend, bool capture, bool publish)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Width, Height));
        byte[] mesh = CreateQuad(1, "/Mesh", -0.7f, 0.5f, -0.1f, -0.5f, 0.5f);
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. mesh], 2);
        _ = renderer.ApplyAndRender(first, color, depth);
        byte[] before = new byte[Width * Height * 4];
        color.ReadbackForTesting(before);
        float[] beforeDepth = new float[Width * Height];
        depth.ReadbackForTesting(beforeDepth);
        SilkMeshGpuResource original = renderer.GpuResources.MeshValues.Single();
        SilkMeshData oldMesh = original.Mesh;
        ulong gpuRevision = renderer.GpuResources.Revision;
        ulong reserved = budget.Usage.ReservedBytes;
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(mesh.AsSpan(176), 0.7);
        using var moved = new OpenUsdSilkPage(30, 2, mesh, 1);
        device.RefuseSubmission = !publish;
        device.RefuseSubmissionAfter = publish ? 1 : null;
        Action apply = capture
            ? () => renderer.ApplyAndRenderForDisplayCapture(moved, color, depth, SilkMeshRenderOptions.Default)
            : () => renderer.ApplyAndRender(moved, color, depth);
        await Assert.That(apply).Throws<SilkGpuStagingBudgetExceededException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        await Assert.That(renderer.GpuResources.Revision).IsEqualTo(gpuRevision);
        await Assert.That(original.Mesh).IsSameReferenceAs(oldMesh);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        byte[] untouched = new byte[before.Length];
        color.ReadbackForTesting(untouched);
        await Assert.That(untouched.SequenceEqual(before)).IsTrue();
        float[] untouchedDepth = new float[beforeDepth.Length];
        depth.ReadbackForTesting(untouchedDepth);
        await Assert.That(untouchedDepth.SequenceEqual(beforeDepth)).IsTrue();
        using var following = new OpenUsdSilkPage(30, 3, [], 0);
        await Assert.That(() => renderer.ApplyPage(following)).Throws<SilkGpuStagingBudgetExceededException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        device.RefuseSubmission = false;
        device.RefuseSubmissionAfter = null;
        _ = renderer.Render(color, depth);
        byte[] restored = new byte[before.Length];
        color.ReadbackForTesting(restored);
        await Assert.That(restored.SequenceEqual(before)).IsTrue();
        renderer.ApplyPage(following);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(3ul);
        _ = renderer.Render(color, depth);
        color.ReadbackForTesting(restored);
        await Assert.That(restored.SequenceEqual(before)).IsFalse();
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    internal static async Task RefusedColorSubmissionKeepsDisplay(SilkGraphicsBackend backend)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        var provider = new SolidLatticeProvider();
        var cache = new SilkDisplayTransformLatticeCache(provider, identityProvider: provider);
        using var renderer = new SilkMeshRenderer(device,
            backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV,
            displayLattices: cache);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Width, Height));
        var transform = new RenderDisplayTransform(Path.Combine(Path.GetTempPath(), "synthetic-frame.ocio"),
            "linear", latticeSize: 8);
        var options = SilkMeshRenderOptions.Default with { DisplayTransform = transform };
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. CreateQuad(1, "/Mesh", -0.6f, 0.6f, 0.6f, -0.6f, 0.5f)], 2);
        _ = renderer.ApplyAndRender(first, color, depth, options);
        byte[] before = new byte[Width * Height * 4];
        color.ReadbackForTesting(before);
        ulong reserved = budget.Usage.ReservedBytes;
        var changed = options with { Exposure = 2 };
        device.RefuseSubmission = true;
        await Assert.That(() => renderer.Render(color, depth, changed)).Throws<SilkGpuStagingBudgetExceededException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        device.RefuseSubmission = false;
        device.RefuseDisplayBinding = true;
        _ = renderer.Render(color, depth, options);
        byte[] retained = new byte[before.Length];
        color.ReadbackForTesting(retained);
        await Assert.That(retained.SequenceEqual(before)).IsTrue();
        device.RefuseDisplayBinding = false;
        _ = renderer.Render(color, depth, changed);
        color.ReadbackForTesting(retained);
        await Assert.That(retained.SequenceEqual(before)).IsFalse();
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    internal static async Task RefusedColorSubmissionKeepsSelection(SilkGraphicsBackend backend)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. CreateQuad(1, "/Selected", -0.6f, 0.6f, 0.6f, -0.6f, 0.5f)], 2);
        var original = new SelectionState(["/Selected"]);
        renderer.UpdateSelection(original);
        _ = renderer.ApplyAndRender(first, color, depth);
        byte[] before = new byte[Width * Height * 4];
        color.ReadbackForTesting(before);
        ulong reserved = budget.Usage.ReservedBytes;
        var candidate = new SelectionState([new SelectionItem("/Selected", null, null, 0, SelectionElementKind.Face)]);
        var settings = new SilkSelectionOutlineSettings(true, new SilkColor(0, 1, 0, 1), 3, true);
        renderer.UpdateSelection(candidate, settings);
        device.RefuseSubmission = true;
        await Assert.That(() => renderer.Render(color, depth)).Throws<SilkGpuStagingBudgetExceededException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        renderer.UpdateSelection(original, SilkSelectionOutlineSettings.Default);
        device.RefuseSubmission = false;
        device.RefuseSelectionBinding = true;
        _ = renderer.Render(color, depth);
        byte[] retained = new byte[before.Length];
        color.ReadbackForTesting(retained);
        await Assert.That(retained.SequenceEqual(before)).IsTrue();
        device.RefuseSelectionBinding = false;
        renderer.UpdateSelection(candidate, settings);
        _ = renderer.Render(color, depth);
        color.ReadbackForTesting(retained);
        await Assert.That(retained.SequenceEqual(before)).IsFalse();
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    internal static async Task RefusedColorSubmissionKeepsInstances(SilkGraphicsBackend backend)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Width, Height));
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. SilkNestedInstanceLinkConformance.Quad(1, "/Prototype", -0.5),
                .. SilkNestedInstanceLinkConformance.InstanceReference("/Prototype", 1, 0.5)], 3);
        _ = renderer.ApplyAndRender(first, color, depth);
        SilkMeshGpuGeometryResource geometry = renderer.GpuResources.MeshValues.First().Geometry;
        await Assert.That(geometry.InstanceSlotCount).IsEqualTo(1);
        ISilkGraphicsBuffer original = geometry.RequireInstanceBuffer(0);
        byte[] originalRows = new byte[checked((int)original.Size)];
        original.ReadbackForTesting(originalRows);
        byte[] before = new byte[Width * Height * 4];
        color.ReadbackForTesting(before);
        ulong reserved = budget.Usage.ReservedBytes;
        double[] view = SilkMeshRendererConformance.Identity();
        view[12] = 0.25;
        using var camera = new OpenUsdSilkPage(30, 2,
            SilkMeshRendererConformance.CreateFrameCommand(Width, Height, view), 1);
        renderer.ApplyPage(camera);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            device.RefuseSubmission = true;
            await Assert.That(() => renderer.Render(color, depth)).Throws<SilkGpuStagingBudgetExceededException>();
            await Assert.That(geometry.RequireInstanceBuffer(0)).IsSameReferenceAs(original);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
            byte[] rows = new byte[originalRows.Length];
            original.ReadbackForTesting(rows);
            await Assert.That(rows.SequenceEqual(originalRows)).IsTrue();
            byte[] retained = new byte[before.Length];
            color.ReadbackForTesting(retained);
            await Assert.That(retained.SequenceEqual(before)).IsTrue();
        }
        device.RefuseSubmission = false;
        _ = renderer.Render(color, depth);
        ISilkGraphicsBuffer published = geometry.RequireInstanceBuffer(0);
        await Assert.That(published).IsNotSameReferenceAs(original);
        byte[] after = new byte[before.Length];
        color.ReadbackForTesting(after);
        await Assert.That(after.SequenceEqual(before)).IsFalse();
        _ = renderer.Render(color, depth);
        await Assert.That(geometry.RequireInstanceBuffer(0)).IsSameReferenceAs(published);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    internal static async Task RefusedColorSubmissionKeepsUniforms(SilkGraphicsBackend backend, bool lazyCapacity)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Width, Height));
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. CreateQuad(1, "/Mesh", -0.6f, 0.6f, 0.6f, -0.6f, 0.5f)], 2);
        if (lazyCapacity)
        {
            SilkSceneDelta delta = renderer.Scene.Apply(first);
            renderer.GpuResources.Apply(renderer.Scene, delta);
            _ = renderer.Render(color, depth);
        }
        else
        {
            _ = renderer.ApplyAndRender(first, color, depth);
        }
        SilkMeshGpuResource mesh = renderer.GpuResources.MeshValues.Single();
        ISilkGraphicsBuffer original = mesh.UniformBuffer;
        ulong reserved = budget.Usage.ReservedBytes;
        byte[] before = new byte[Width * Height * 4];
        color.ReadbackForTesting(before);
        double[] view = SilkMeshRendererConformance.Identity();
        view[12] = 0.25;
        using var camera = new OpenUsdSilkPage(30, 2,
            SilkMeshRendererConformance.CreateFrameCommand(Width, Height, view), 1);
        renderer.ApplyPage(camera);
        var options = SilkMeshRenderOptions.Default with { Exposure = 1 };
        ulong expectedWrites = SilkSceneUniformWriter.ByteSize + SilkFrameUniformWriter.ByteSize;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            ulong writes = renderer.GpuResources.Statistics.BufferWriteBytes;
            device.RefuseSubmission = true;
            await Assert.That(() => renderer.Render(color, depth, options))
                .Throws<SilkGpuStagingBudgetExceededException>();
            await Assert.That(mesh.UniformBuffer).IsSameReferenceAs(original);
            await Assert.That(renderer.GpuResources.Statistics.BufferWriteBytes - writes).IsEqualTo(expectedWrites);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
            byte[] retained = new byte[before.Length];
            color.ReadbackForTesting(retained);
            await Assert.That(retained.SequenceEqual(before)).IsTrue();
        }
        device.RefuseSubmission = false;
        ulong beforeRetry = renderer.GpuResources.Statistics.BufferWriteBytes;
        _ = renderer.Render(color, depth, options);
        await Assert.That(mesh.UniformBuffer).IsNotSameReferenceAs(original);
        await Assert.That(renderer.GpuResources.Statistics.BufferWriteBytes - beforeRetry).IsEqualTo(expectedWrites);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(
            lazyCapacity ? reserved + expectedWrites : reserved);
        byte[] after = new byte[before.Length];
        color.ReadbackForTesting(after);
        await Assert.That(after.SequenceEqual(before)).IsFalse();
        ulong beforeWarm = renderer.GpuResources.Statistics.BufferWriteBytes;
        _ = renderer.Render(color, depth, options);
        await Assert.That(renderer.GpuResources.Statistics.BufferWriteBytes).IsEqualTo(beforeWarm);
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    internal static async Task SelectionPageRollbackRespectsGeneration(SilkGraphicsBackend backend, bool deviceLoss)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));
        using ISilkGraphicsTexture hdr = device.CreateTexture2D(
            SilkTextureDescriptor.HdrColorTarget(Width * 2, Height * 2));
        using ISilkGraphicsTexture resizedDepth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width * 2, Height * 2));
        byte[] mesh = CreateQuad(1, "/Selected", -0.6f, 0.6f, 0.6f, -0.6f, 0.5f);
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. mesh], 2);
        var selection = new SelectionState(["/Selected"]);
        renderer.UpdateSelection(selection);
        _ = renderer.ApplyAndRender(first, color, depth);
        byte[] before = new byte[Width * Height * 4];
        color.ReadbackForTesting(before);
        ISilkSelectionOutlineBinding original = device.LastSelectionBinding!;
        ulong reserved = budget.Usage.ReservedBytes;
        renderer.UpdateSelection(selection,
            new SilkSelectionOutlineSettings(true, new SilkColor(0, 1, 0, 1), 4, true));
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(mesh.AsSpan(176), 0.2);
        using var moved = new OpenUsdSilkPage(30, 2, mesh, 1);
        device.RefuseGraphicsPipeline = true;
        device.LoseGenerationOnPipelineRefusal = deviceLoss;
        await Assert.That(() => renderer.ApplyAndRender(moved, hdr, resizedDepth)).Throws<InvalidOperationException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        await Assert.That(device.LastSelectionBinding).IsNotSameReferenceAs(original);
        using (ISilkGraphicsCommandList commands = device.CreateCommandList())
        {
            if (deviceLoss)
            {
                await Assert.That(() => commands.ClearColor(original.Descriptor.MaskTexture, new(0, 0, 0, 0)))
                    .Throws<ObjectDisposedException>();
            }
            else
            {
                commands.ClearColor(original.Descriptor.MaskTexture, new(0, 0, 0, 0));
                await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
            }
        }
        device.RefuseGraphicsPipeline = false;
        device.LoseGenerationOnPipelineRefusal = false;
        renderer.UpdateSelection(selection, SilkSelectionOutlineSettings.Default);
        _ = renderer.Render(color, depth);
        byte[] restored = new byte[before.Length];
        color.ReadbackForTesting(restored);
        await Assert.That(restored.SequenceEqual(before)).IsTrue();
        _ = renderer.ApplyAndRender(moved, color, depth);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2ul);
        color.ReadbackForTesting(restored);
        await Assert.That(restored.SequenceEqual(before)).IsFalse();
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    internal static async Task SelectionMaskPageKeepsPriorScene(
        SilkGraphicsBackend backend, SelectionElementKind kind)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));
        byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(
            1, "/Selected", [-0.6f, 0.6f, 0.5f, 0.6f, 0.6f, 0.5f, 0.6f, -0.6f, 0.5f, -0.6f, -0.6f, 0.5f],
            [0, 1, 2, 0, 2, 3], 0, 0, null, 1, [0, 1], [0, 1, 2, 3], [0, 1, -1, -1, 2, 3]);
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. mesh], 2);
        var original = new SelectionState(["/Selected"]);
        renderer.UpdateSelection(original);
        _ = renderer.ApplyAndRender(first, color, depth);
        byte[] before = new byte[Width * Height * 4];
        color.ReadbackForTesting(before);
        ulong reserved = budget.Usage.ReservedBytes;
        var candidate = kind == SelectionElementKind.None ? original :
            new SelectionState([new SelectionItem("/Selected", null, null, 0, kind)]);
        var settings = new SilkSelectionOutlineSettings(true, new SilkColor(0, 1, 0, 1), 3,
            kind is SelectionElementKind.Edge or SelectionElementKind.Point);
        renderer.UpdateSelection(candidate, settings);
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(mesh.AsSpan(176), 0.2);
        using var moved = new OpenUsdSilkPage(30, 2, mesh, 1);
        device.RefuseSelectionMaskPipeline = true;
        await Assert.That(() => renderer.ApplyAndRender(moved, color, depth)).Throws<InvalidOperationException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        renderer.UpdateSelection(original, SilkSelectionOutlineSettings.Default);
        _ = renderer.Render(color, depth);
        byte[] restored = new byte[before.Length];
        color.ReadbackForTesting(restored);
        await Assert.That(restored.SequenceEqual(before)).IsTrue();
        device.RefuseSelectionMaskPipeline = false;
        renderer.UpdateSelection(candidate, settings);
        _ = renderer.ApplyAndRender(moved, color, depth);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2ul);
        color.ReadbackForTesting(restored);
        await Assert.That(restored.SequenceEqual(before)).IsFalse();
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    internal static async Task CaptureReplayKeepsSelectionOmitted(SilkGraphicsBackend backend)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture hdr = device.CreateTexture2D(SilkTextureDescriptor.HdrColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));
        byte[] mesh = CreateQuad(1, "/Selected", -0.6f, 0.6f, 0.6f, -0.6f, 0.5f);
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. mesh], 2);
        _ = renderer.ApplyAndRender(first, color, depth);
        renderer.UpdateSelection(new SelectionState(["/Selected"]));
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(mesh.AsSpan(176), 0.2);
        using var moved = new OpenUsdSilkPage(30, 2, mesh, 1);
        device.RefuseGraphicsPipeline = true;
        await Assert.That(() => renderer.ApplyAndRenderForDisplayCapture(
            moved, hdr, depth, SilkMeshRenderOptions.Default)).Throws<InvalidOperationException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        device.RefuseGraphicsPipeline = false;
        device.RefuseSelectionBinding = true;
        using var following = new OpenUsdSilkPage(30, 3, [], 0);
        await Assert.That(() => renderer.ApplyAndRender(following, color, depth)).Throws<InvalidOperationException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2ul);
        await Assert.That(device.LastSelectionBinding).IsNull();
        device.RefuseSelectionBinding = false;
        _ = renderer.ApplyAndRender(following, color, depth);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(3ul);
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    internal static async Task SelectionReplayAdmitsTemporaryDepth(SilkGraphicsBackend backend, bool oneByteShort)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var buffers = new SilkGpuBufferBudget(1_000_000);
        var textures = new SilkGpuTextureBudget(oneByteShort ? 999_999ul : 1_000_000ul);
        buffers.ConfigureDevice(device);
        textures.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));
        byte[] mesh = CreateQuad(1, "/Selected", -0.6f, 0.6f, 0.6f, -0.6f, 0.5f);
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. mesh], 2);
        var selection = new SelectionState(["/Selected"]);
        renderer.UpdateSelection(selection, SilkSelectionOutlineSettings.Default);
        _ = renderer.ApplyAndRender(first, color, depth);
        byte[] before = new byte[Width * Height * 4];
        color.ReadbackForTesting(before);
        ulong retainedTextures = textures.Usage.ReservedBytes;
        ulong retainedBuffers = buffers.Usage.ReservedBytes;
        renderer.UpdateSelection(selection,
            new SilkSelectionOutlineSettings(true, new SilkColor(0, 1, 0, 1), 3, true));
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(mesh.AsSpan(176), 0.2);
        using var moved = new OpenUsdSilkPage(30, 2, mesh, 1);
        device.RefuseSelectionBinding = true;
        await Assert.That(() => renderer.ApplyAndRender(moved, color, depth)).Throws<InvalidOperationException>();
        device.RefuseSelectionBinding = false;
        using var following = new OpenUsdSilkPage(30, 3, [], 0);
        const ulong replayBytes = Width * Height * (sizeof(float) + 4);
        ulong pressureBytes = 1_000_000 - retainedTextures - replayBytes;
        using (textures.Reserve(SilkTextureDescriptor.ColorTarget(checked((uint)(pressureBytes / 4)), 1), 1))
        {
            if (oneByteShort)
            {
                await Assert.That(() => renderer.ApplyPage(following)).Throws<SilkGpuTextureBudgetExceededException>();
                await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
                await Assert.That(renderer.Scene.Meshes.Values.Single().Transform.Span[12]).IsEqualTo(0.0);
                await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(retainedBuffers);
                renderer.UpdateSelection(selection, SilkSelectionOutlineSettings.Default);
                _ = renderer.Render(color, depth);
                byte[] retained = new byte[before.Length];
                color.ReadbackForTesting(retained);
                await Assert.That(retained.SequenceEqual(before)).IsTrue();
            }
            else
            {
                renderer.ApplyPage(following);
                await Assert.That(renderer.Scene.Revision).IsEqualTo(3ul);
                await Assert.That(textures.Usage.PeakReservedBytes).IsEqualTo(textures.MaximumBytes);
                _ = renderer.Render(color, depth);
            }
            await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(retainedTextures + pressureBytes);
        }
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(retainedTextures);
        if (oneByteShort)
        {
            renderer.ApplyPage(following);
            _ = renderer.Render(color, depth);
            await Assert.That(renderer.Scene.Revision).IsEqualTo(3ul);
            await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(retainedTextures);
        }
        byte[] after = new byte[before.Length];
        color.ReadbackForTesting(after);
        await Assert.That(after.SequenceEqual(before)).IsFalse();
        renderer.Dispose();
        depth.Dispose();
        color.Dispose();
        await Assert.That(textures.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    internal static async Task SelectionOverlayPageKeepsPriorScene(SilkGraphicsBackend backend, bool targetlessReplay)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));
        byte[] mesh = CreateQuad(1, "/Selected", -0.6f, 0.6f, 0.6f, -0.6f, 0.5f);
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. mesh], 2);
        _ = renderer.ApplyAndRender(first, color, depth);
        var selection = new SelectionState(["/Selected"]);
        var oldSettings = SilkSelectionOutlineSettings.Default;
        renderer.UpdateSelection(selection, oldSettings);
        _ = renderer.Render(color, depth);
        byte[] before = new byte[Width * Height * 4];
        color.ReadbackForTesting(before);
        ulong reserved = budget.Usage.ReservedBytes;
        renderer.UpdateSelection(selection,
            new SilkSelectionOutlineSettings(true, new SilkColor(0, 1, 0, 1), 4, true));
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(mesh.AsSpan(176), 0.2);
        using var moved = new OpenUsdSilkPage(30, 2, mesh, 1);
        device.RefuseSelectionBinding = true;
        await Assert.That(() => renderer.ApplyAndRender(moved, color, depth)).Throws<InvalidOperationException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        using var following = new OpenUsdSilkPage(30, 3, [], 0);
        await Assert.That(() => renderer.ApplyPage(following)).Throws<InvalidOperationException>();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        renderer.UpdateSelection(selection, oldSettings);
        _ = renderer.Render(color, depth);
        byte[] restored = new byte[before.Length];
        color.ReadbackForTesting(restored);
        await Assert.That(restored.SequenceEqual(before)).IsTrue();
        device.RefuseSelectionBinding = false;
        if (targetlessReplay)
        {
            renderer.ApplyPage(following);
            _ = renderer.Render(color, depth);
        }
        else
        {
            _ = renderer.ApplyAndRender(following, color, depth);
        }
        await Assert.That(renderer.Scene.Revision).IsEqualTo(3ul);
        color.ReadbackForTesting(restored);
        await Assert.That(restored.SequenceEqual(before)).IsFalse();
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    internal static async Task ComponentSelectionPageKeepsPriorScene(
        SilkGraphicsBackend backend, bool latePipeline)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture hdr = device.CreateTexture2D(SilkTextureDescriptor.HdrColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));
        byte[] mesh = CreateQuad(1, "/Selected", -0.6f, 0.6f, 0.6f, -0.6f, 0.5f);
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. mesh], 2);
        _ = renderer.ApplyAndRender(first, color, depth);
        renderer.UpdateSelection(new SelectionState(
            [new SelectionItem("/Selected", null, null, 0, SelectionElementKind.Face)]));
        _ = renderer.Render(color, depth);
        byte[] before = new byte[Width * Height * 4];
        color.ReadbackForTesting(before);
        ulong reserved = budget.Usage.ReservedBytes;
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(mesh.AsSpan(176), 0.2);
        using var moved = new OpenUsdSilkPage(30, 2, mesh, 1);
        using (IDisposable? pressure = latePipeline ? null : budget.Reserve(budget.MaximumBytes - reserved - 11))
        {
            device.RefuseGraphicsPipeline = latePipeline;
            if (latePipeline)
            {
                await Assert.That(() => renderer.ApplyAndRender(moved, hdr, depth)).Throws<InvalidOperationException>();
            }
            else
            {
                await Assert.That(() => renderer.ApplyAndRender(moved, color, depth))
                    .Throws<SilkGpuBufferBudgetExceededException>();
            }
            await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(latePipeline ? reserved : budget.MaximumBytes - 11);
            _ = renderer.Render(color, depth);
            byte[] restored = new byte[before.Length];
            color.ReadbackForTesting(restored);
            await Assert.That(restored.SequenceEqual(before)).IsTrue();
        }
        device.RefuseGraphicsPipeline = false;
        _ = renderer.ApplyAndRender(moved, color, depth);
        byte[] after = new byte[before.Length];
        color.ReadbackForTesting(after);
        await Assert.That(after.SequenceEqual(before)).IsFalse();
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2ul);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
    }

    internal static async Task ScopedSelectionRefusalKeepsPublishedIndices(
        SilkGraphicsBackend backend, bool multipleFaces)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));
        using var page = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. CreateQuad(1, "/Selected", -0.6f, 0.6f, 0.6f, -0.6f, 0.5f)], 2);
        _ = renderer.ApplyAndRender(page, color, depth);
        var firstFace = new SelectionItem("/Selected", null, null, 0, SelectionElementKind.Face);
        var secondFace = new SelectionItem("/Selected", null, null, 1, SelectionElementKind.Face);
        var previous = new SelectionState([firstFace]);
        var candidate = new SelectionState(multipleFaces ? [firstFace, secondFace] : [secondFace]);
        renderer.UpdateSelection(previous);
        _ = renderer.Render(color, depth);
        byte[] before = new byte[Width * Height * 4];
        color.ReadbackForTesting(before);
        ulong reserved = budget.Usage.ReservedBytes;
        int buffers = device.CreatedBuffers;
        ulong required = multipleFaces ? 24ul : 12ul;
        using (budget.Reserve(budget.MaximumBytes - reserved - required + 1))
        {
            renderer.UpdateSelection(candidate);
            await Assert.That(() => renderer.Render(color, depth)).Throws<SilkGpuBufferBudgetExceededException>();
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(budget.MaximumBytes - required + 1);
            renderer.UpdateSelection(previous);
            _ = renderer.Render(color, depth);
            await Assert.That(device.CreatedBuffers - buffers).IsEqualTo(multipleFaces ? 1 : 0);
            byte[] restored = new byte[before.Length];
            color.ReadbackForTesting(restored);
            await Assert.That(restored.SequenceEqual(before)).IsTrue();
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        renderer.UpdateSelection(candidate);
        _ = renderer.Render(color, depth);
        byte[] after = new byte[before.Length];
        color.ReadbackForTesting(after);
        await Assert.That(after.SequenceEqual(before)).IsFalse();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved - 12 + required);
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    internal static async Task SelectionRefusalKeepsPublishedResources(SilkGraphicsBackend backend, string failure)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture hdr = device.CreateTexture2D(SilkTextureDescriptor.HdrColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.SampledDepthTarget(Width, Height));
        using var page = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. CreateQuad(1, "/Selected", -0.6f, 0.6f, 0.6f, -0.6f, 0.5f)], 2);
        _ = renderer.ApplyAndRender(page, color, depth);
        byte[] plain = new byte[Width * Height * 4];
        color.ReadbackForTesting(plain);
        var selection = new SelectionState(["/Selected"]);
        var originalSettings = new SilkSelectionOutlineSettings(true, new SilkColor(1, 0.5f, 0, 1), 1, true);
        renderer.UpdateSelection(selection, originalSettings);
        _ = renderer.Render(color, depth);
        byte[] before = new byte[plain.Length];
        color.ReadbackForTesting(before);
        await Assert.That(before.SequenceEqual(plain)).IsFalse();
        ISilkSelectionOutlineBinding original = device.LastSelectionBinding!;
        ulong reserved = budget.Usage.ReservedBytes;
        renderer.UpdateSelection(selection, new SilkSelectionOutlineSettings(true, new SilkColor(0, 1, 0, 1), 3, true));
        device.RefuseBufferAfter = failure == "format" ? 0 : null;
        device.RefuseSelectionBinding = failure == "binding";
        using IDisposable? pressure = failure == "quota"
            ? budget.Reserve(budget.MaximumBytes - reserved - SilkSelectionOutlineUniformWriter.ByteSize + 1) : null;
        Action render = () => renderer.Render(failure == "format" ? hdr : color, depth);
        if (failure == "quota")
        {
            await Assert.That(render).Throws<SilkGpuBufferBudgetExceededException>();
        }
        else
        {
            await Assert.That(render).Throws<InvalidOperationException>();
        }
        pressure?.Dispose();
        await Assert.That(device.LastSelectionBinding).IsSameReferenceAs(original);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        using (ISilkGraphicsCommandList commands = device.CreateCommandList())
        {
            commands.ClearColor(original.Descriptor.MaskTexture, new SilkColor(0, 0, 0, 0));
        }
        renderer.UpdateSelection(selection, originalSettings);
        _ = renderer.Render(color, depth);
        byte[] restored = new byte[plain.Length];
        color.ReadbackForTesting(restored);
        await Assert.That(restored.SequenceEqual(before)).IsTrue();
        device.RefuseBufferAfter = null;
        device.RefuseSelectionBinding = false;
        renderer.UpdateSelection(selection, new SilkSelectionOutlineSettings(true, new SilkColor(0, 1, 0, 1), 3, true));
        _ = renderer.Render(color, depth);
        color.ReadbackForTesting(restored);
        await Assert.That(restored.SequenceEqual(before)).IsFalse();
        renderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    internal static async Task ColorPipelineRefusalKeepsPublishedScene(SilkGraphicsBackend backend)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture display = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture candidate = device.CreateTexture2D(
            SilkTextureDescriptor.HdrColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Width, Height));
        using var first = new OpenUsdSilkPage(30, 1,
            [.. SilkMeshRendererConformance.CreateFrameCommand(Width, Height, SilkMeshRendererConformance.Identity()),
                .. CreateQuad(1, "/Original", -0.8f, 0.8f, 0.8f, -0.8f, 0.5f)], 2);
        _ = renderer.ApplyAndRender(first, display, depth);
        SilkMeshGpuResource original = renderer.GpuResources.MeshValues.Single();
        ulong reserved = budget.Usage.ReservedBytes;
        using var next = new OpenUsdSilkPage(30, 2,
            CreateQuad(2, "/Added", -0.5f, 0.5f, 0.5f, -0.5f, 0.3f), 1);
        device.RefuseGraphicsPipeline = true;
        await Assert.That(() => renderer.ApplyAndRender(next, candidate, depth)).Throws<InvalidOperationException>();
        device.RefuseGraphicsPipeline = false;
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        await Assert.That(renderer.GpuResources.MeshValues.Single()).IsSameReferenceAs(original);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        _ = renderer.ApplyAndRender(next, candidate, depth);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2ul);
        await Assert.That(renderer.GpuResources.MeshValues.Count).IsEqualTo(2);
    }

    internal static async Task DisplayAndSceneRefusalIsAtomic(
        SilkGraphicsBackend backend, bool lateMesh, bool capture)
    {
        using var device = new SilkGpuPublicationDevice(SilkDepthCaptureConformance.CreateDevice(backend));
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        var provider = new SolidLatticeProvider();
        var cache = new SilkDisplayTransformLatticeCache(provider, identityProvider: provider);
        using var renderer = new SilkMeshRenderer(device,
            backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV,
            displayLattices: cache);
        using ISilkGraphicsTexture display = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Width, Height));
        var transform = new RenderDisplayTransform(Path.Combine(Path.GetTempPath(), "synthetic-display.ocio"), "linear",
            latticeSize: 8);
        var options = new SilkMeshRenderOptions(new SilkColor(0.5f, 0.5f, 0.5f, 1), 1) { DisplayTransform = transform };
        byte[] frame = SilkMeshRendererConformance.CreateFrameCommand(
            Width, Height, SilkMeshRendererConformance.Identity());
        using var initial = new OpenUsdSilkPage(30, 1, frame, 1);
        _ = renderer.ApplyAndRender(initial, display, depth, options);
        byte[] before = new byte[Width * Height * 4];
        display.ReadbackForTesting(before);
        ISilkDisplayTransformBinding originalBinding = device.LastDisplayBinding!;
        ulong reserved = budget.Usage.ReservedBytes;
        provider.Blue = true;
        device.RefuseDisplayBinding = !lateMesh;
        device.RefuseBufferAfterSubmission = lateMesh;
        using var changed = new OpenUsdSilkPage(30, 2,
            CreateQuad(1, "/Candidate", -0.8f, 0.8f, 0.8f, -0.8f, 0.5f), 1);
        Action apply = capture
            ? () => renderer.ApplyAndRenderForDisplayCapture(changed, display, depth, options)
            : () => renderer.ApplyAndRender(changed, display, depth, options);
        await Assert.That(apply).Throws<InvalidOperationException>();
        using var next = new OpenUsdSilkPage(30, 3, [], 0);
        if (!lateMesh)
        {
            await Assert.That(() => renderer.ApplyPage(next)).Throws<InvalidOperationException>();
            await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        }
        device.RefuseDisplayBinding = false;
        device.RefuseBufferAfterSubmission = false;
        device.RefuseBufferAfter = null;
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        await Assert.That(renderer.GpuResources.Meshes).IsEmpty();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        using (ISilkGraphicsCommandList retained = device.CreateCommandList())
        {
            retained.ClearColor(originalBinding.Descriptor.SceneColorTexture, new SilkColor(0.5f, 0.5f, 0.5f, 1));
        }
        byte[] afterRefusal = new byte[before.Length];
        display.ReadbackForTesting(afterRefusal);
        await Assert.That(afterRefusal.SequenceEqual(before)).IsTrue();
        _ = renderer.Render(display, depth, options);
        display.ReadbackForTesting(afterRefusal);
        await Assert.That(afterRefusal.SequenceEqual(before)).IsTrue();
        provider.Blue = true;
        renderer.ApplyPage(next);
        _ = renderer.Render(display, depth, options);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(3ul);
        await Assert.That(renderer.GpuResources.MeshValues.Count).IsEqualTo(1);
        display.ReadbackForTesting(afterRefusal);
        await Assert.That(afterRefusal.SequenceEqual(before)).IsFalse();
    }

    internal static async Task AdmissionRefusalKeepsPublishedTransform(
        SilkGraphicsBackend backend, string failure)
    {
        using ISilkGraphicsDevice inner = SilkDepthCaptureConformance.CreateDevice(backend);
        var cpu = new SilkCpuTextureBudget(8192);
        cpu.ConfigureDevice(inner);
        using var device = new SilkGpuPublicationDevice(inner);
        var budget = new SilkGpuBufferBudget(1_000_000);
        budget.ConfigureDevice(device);
        var provider = new SolidLatticeProvider();
        var cache = new SilkDisplayTransformLatticeCache(cpu, provider, identityProvider: provider);
        using var pass = new SilkDisplayTransformPass(device,
            backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV, cache);
        using ISilkGraphicsTexture display = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Width, Height));
        using ISilkGraphicsTexture larger = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(Width * 2, Height * 2));
        using ISilkGraphicsTexture hdr = device.CreateTexture2D(
            SilkTextureDescriptor.HdrColorTarget(Width, Height));
        var transform = new RenderDisplayTransform(Path.Combine(Path.GetTempPath(), "synthetic-display.ocio"), "linear",
            latticeSize: 8);
        await Assert.That(pass.TryPrepare(transform, display, 1, out ISilkGraphicsTexture brighter)).IsTrue();
        byte[] exposed = RenderPass(pass, device, display, brighter);
        await Assert.That(pass.TryPrepare(transform, display, 0, out ISilkGraphicsTexture original)).IsTrue();
        byte[] before = RenderPass(pass, device, display, original);
        await Assert.That(before.SequenceEqual(exposed)).IsFalse();
        await Assert.That(before[0]).IsGreaterThan((byte)20);
        await Assert.That(before[2]).IsEqualTo((byte)0);
        ISilkDisplayTransformBinding binding = device.LastDisplayBinding!;
        ulong reserved = budget.Usage.ReservedBytes;
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(2048ul);
        provider.Blue = failure != "parameters";
        device.RefuseDisplayBinding = failure is "binding" or "parameters";
        device.RefuseSubmission = failure == "upload";
        device.RefuseBufferAfter = failure == "format" ? 0 : null;
        IDisposable? pressure = failure == "quota"
            ? budget.Reserve(budget.MaximumBytes - reserved - SilkDisplayTransformUniformWriter.ByteSize + 1) : null;
        ISilkGraphicsTexture target = failure == "parameters" ? display : failure == "format" ? hdr : larger;
        try
        {
            if (failure == "quota")
            {
                await Assert.That(() => pass.TryPrepare(transform, target, 1, out _))
                    .Throws<SilkGpuBufferBudgetExceededException>();
            }
            else
            {
                await Assert.That(() => pass.TryPrepare(transform, target, 1, out _))
                    .Throws<InvalidOperationException>();
            }
        }
        finally
        {
            pressure?.Dispose();
            device.RefuseDisplayBinding = false;
            device.RefuseSubmission = false;
            device.RefuseBufferAfter = null;
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        await Assert.That(device.LastDisplayBinding).IsSameReferenceAs(binding);
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(failure == "parameters" ? 2048ul : 4096ul);
        cache.Clear();
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(2048ul);
        await Assert.That(RenderPass(pass, device, display, original).SequenceEqual(before)).IsTrue();

        provider.Blue = true;
        await Assert.That(pass.TryPrepare(transform, larger, 1, out ISilkGraphicsTexture candidate)).IsTrue();
        byte[] after = RenderPass(pass, device, larger, candidate);
        await Assert.That(after[0]).IsEqualTo((byte)0);
        await Assert.That(after[2]).IsGreaterThan((byte)20);
        await Assert.That(candidate).IsNotSameReferenceAs(original);
        ISilkDisplayTransformBinding settled = device.LastDisplayBinding!;
        ulong uploads = pass.Diagnostics.LatticeUploads;
        int buffers = device.CreatedBuffers;
        await Assert.That(pass.TryPrepare(transform, larger, 1, out ISilkGraphicsTexture reused)).IsTrue();
        await Assert.That(reused).IsSameReferenceAs(candidate);
        await Assert.That(device.LastDisplayBinding).IsSameReferenceAs(settled);
        await Assert.That(pass.Diagnostics.LatticeUploads).IsEqualTo(uploads);
        await Assert.That(device.CreatedBuffers).IsEqualTo(buffers);
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(2048ul);
        pass.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(cpu.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    private static byte[] RenderPass(
        SilkDisplayTransformPass pass, SilkGpuPublicationDevice device,
        ISilkGraphicsTexture display, ISilkGraphicsTexture scene)
    {
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        commands.ClearColor(scene, new SilkColor(0.5f, 0.5f, 0.5f, 1));
        pass.Record(commands, (ISilkDisplayTransformGraphicsCommandList)commands, display);
        using ISilkGraphicsSubmission submission = device.Submit(commands);
        submission.Wait();
        byte[] pixels = new byte[checked((int)(display.Width * display.Height * 4))];
        display.ReadbackForTesting(pixels);
        return pixels;
    }

    private sealed class SolidLatticeProvider :
        ISilkDisplayTransformLatticeProvider, ISilkDisplayTransformConfigIdentityProvider
    {
        internal bool Blue { get; set; }
        public SilkDisplayTransformConfigIdentity GetIdentity(string configPath) => new(Blue ? "blue" : "red", true);
        public SilkDisplayTransformLattice Create(RenderDisplayTransform transform)
        {
            byte[] pixels = new byte[8 * 8 * 8 * 4];
            for (int index = 0; index < pixels.Length; index += 4)
            {
                pixels[index + (Blue ? 2 : 0)] = (byte)(((index / 4) % 8) * 255 / 7);
                pixels[index + 3] = 255;
            }
            return new SilkDisplayTransformLattice(8, transform.ShaperMinimumLog2, transform.ShaperRangeLog2, pixels);
        }
    }

    /// <summary>
    /// Builds a vertically asymmetric scene: a bright quad covering the upper half of
    /// clip space and a dark quad covering the lower half.
    /// </summary>
    /// <remarks>
    /// A clear colour cannot catch a vertical flip and neither can a centred quad, which
    /// is exactly why the fullscreen composite's orientation went unnoticed. This scene
    /// makes the top and bottom of the image different, so writing the source's top row
    /// into the target's bottom row is a failing assertion rather than an invisible one.
    /// </remarks>
    internal static void ApplyVerticallyAsymmetricScene(SilkMeshRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        byte[] frame = SilkMeshRendererConformance.CreateFrameCommand(
            Width,
            Height,
            SilkMeshRendererConformance.Identity());
        // Geometry, not colour, carries the asymmetry: one quad occupies the upper half
        // of clip space and the lower half stays at the clear colour. That keeps the
        // difference independent of shading, tinting, and lighting, so the assertion is
        // about orientation and nothing else.
        byte[] upper = CreateQuad(1, "/Upper", -0.9f, 0.9f, 0.9f, 0.1f, 0.5f);
        SilkMeshRendererConformance.Apply(renderer, 1, [frame, upper]);
    }

    /// <summary>
    /// Gets the mean luminance of the top and bottom quarter of an RGBA8 image.
    /// </summary>
    internal static (double Top, double Bottom) MeasureVerticalBias(ReadOnlySpan<byte> rgba)
    {
        int quarter = (int)(Height / 4);
        double top = 0;
        double bottom = 0;
        int count = 0;
        for (int row = 0; row < quarter; row++)
        {
            for (int column = 0; column < Width; column++)
            {
                int topOffset = (int)(((row * Width) + column) * 4);
                int bottomOffset =
                    (int)(((((int)Height - 1 - row) * Width) + column) * 4);
                top += rgba[topOffset] + rgba[topOffset + 1] + rgba[topOffset + 2];
                bottom +=
                    rgba[bottomOffset] + rgba[bottomOffset + 1] + rgba[bottomOffset + 2];
                count++;
            }
        }
        return (top / (count * 3.0), bottom / (count * 3.0));
    }

    private static byte[] CreateQuad(
        ulong id,
        string pathValue,
        float left,
        float top,
        float right,
        float bottom,
        float depth) =>
        SilkMeshRendererConformance.CreateMeshCommand(
            id,
            pathValue,
            [
                left, top, depth,
                right, top, depth,
                right, bottom, depth,
                left, bottom, depth
            ],
            [0, 1, 2, 0, 2, 3]);
}
