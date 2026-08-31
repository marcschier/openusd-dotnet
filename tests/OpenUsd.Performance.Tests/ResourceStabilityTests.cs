// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Performance.Tests;

[NotInParallel]
public sealed class ResourceStabilityTests
{
    [Test]
    public async Task FrameOnlyPagesKeepRetainedResourcesStable()
    {
        SilkCounterSnapshot baseline = ReadSilkCounters();
        var scene = new SilkSceneState();
        var device = new CountingGraphicsDevice();
        var resources = new SilkSceneGpuResources(device);
        byte[] initialPage = PerformanceTestData.Concat(
            PerformanceTestData.CreateFrameCommand(),
            PerformanceTestData.CreateMeshCommand(triangleCount: 32));
        SilkSceneDelta initialDelta = scene.Apply(initialPage, 2, revision: 1);
        resources.Apply(scene, initialDelta);
        _ = resources.UpdateUniforms(scene.Frame);
        SilkSceneGpuStatistics initialStatistics = resources.Statistics;
        ulong initialIdentityRevision = scene.PickIdentities.Revision;
        SilkCounterSnapshot retained = ReadSilkCounters();

        for (int frame = 0; frame < 512; frame++)
        {
            SilkSceneDelta delta = scene.Apply(
                PerformanceTestData.CreateFrameCommand(),
                commandCount: 1,
                revision: checked((ulong)frame + 2));
            resources.Apply(scene, delta);
            if (resources.UpdateUniforms(scene.Frame) != 0)
            {
                throw new InvalidOperationException(
                    "An unchanged frame caused a uniform upload.");
            }
        }

        await Assert.That(resources.Statistics).IsEqualTo(initialStatistics);
        await Assert.That(resources.Meshes.Count).IsEqualTo(1);
        await Assert.That(device.CreatedBufferCount).IsEqualTo(3);
        await Assert.That(device.DisposedBufferCount).IsEqualTo(0);
        await Assert.That(scene.PickIdentities.ActiveRangeCount).IsEqualTo(1);
        await Assert.That(scene.PickIdentities.AllocatedRangeCount).IsEqualTo(1UL);
        await Assert.That(scene.PickIdentities.Revision)
            .IsEqualTo(initialIdentityRevision);
        await Assert.That(ReadSilkCounters()).IsEqualTo(retained);

        resources.Dispose();
        device.Dispose();

        await Assert.That(device.LiveBufferCount).IsEqualTo(0);
        await Assert.That(ReadSilkCounters()).IsEqualTo(baseline);
    }

    [Test]
    public async Task PropertyUpdatesReuseGeometryResources()
    {
        SilkCounterSnapshot baseline = ReadSilkCounters();
        var scene = new SilkSceneState();
        var device = new CountingGraphicsDevice();
        var resources = new SilkSceneGpuResources(device);
        SilkSceneDelta initialDelta = scene.Apply(
            PerformanceTestData.CreateMeshCommand(triangleCount: 32),
            commandCount: 1,
            revision: 1);
        resources.Apply(scene, initialDelta);
        SilkSceneGpuStatistics initialStatistics = resources.Statistics;
        CountingGraphicsBuffer[] initialBuffers = [.. device.Buffers];
        byte[] propertyPage = PerformanceTestData.CreateMeshCommand(
            triangleCount: 32,
            color: 0.2f);

        for (int update = 0; update < 256; update++)
        {
            SilkSceneDelta delta = scene.Apply(
                propertyPage,
                commandCount: 1,
                revision: checked((ulong)update + 2));
            resources.Apply(scene, delta);
        }

        SilkSceneGpuStatistics finalStatistics = resources.Statistics;
        await Assert.That(finalStatistics.MeshCount).IsEqualTo(1);
        await Assert.That(finalStatistics.GeometryBuilds)
            .IsEqualTo(initialStatistics.GeometryBuilds);
        await Assert.That(finalStatistics.VertexUploads)
            .IsEqualTo(initialStatistics.VertexUploads);
        await Assert.That(finalStatistics.IndexUploads)
            .IsEqualTo(initialStatistics.IndexUploads);
        await Assert.That(device.CreatedBufferCount).IsEqualTo(3);
        await Assert.That(device.DisposedBufferCount).IsEqualTo(0);
        await Assert.That(device.Buffers[0]).IsSameReferenceAs(initialBuffers[0]);
        await Assert.That(device.Buffers[1]).IsSameReferenceAs(initialBuffers[1]);
        await Assert.That(device.Buffers[2]).IsSameReferenceAs(initialBuffers[2]);
        await Assert.That(scene.PickIdentities.ActiveRangeCount).IsEqualTo(1);
        await Assert.That(scene.PickIdentities.AllocatedRangeCount).IsEqualTo(1UL);

        resources.Dispose();
        device.Dispose();

        await Assert.That(device.LiveBufferCount).IsEqualTo(0);
        await Assert.That(ReadSilkCounters()).IsEqualTo(baseline);
    }

    [Test]
    public async Task PointInstancePagesCarryPrototypeGeometryOnce()
    {
        const int instanceCount = 64;
        byte[] prototype = PerformanceTestData.CreateMeshCommand(triangleCount: 128);
        byte[][] commands = new byte[instanceCount][];
        commands[0] = prototype;
        for (int index = 1; index < commands.Length; index++)
        {
            commands[index] = PerformanceTestData.CreateMeshInstanceReferenceCommand(
                instanceIndex: index,
                x: index * 0.01);
        }
        byte[] page = PerformanceTestData.Concat(commands);
        int duplicatedGeometryBytes = prototype.Length * instanceCount;

        var scene = new SilkSceneState();
        var device = new CountingGraphicsDevice();
        var resources = new SilkSceneGpuResources(device);
        SilkSceneDelta delta = scene.Apply(page, (uint)commands.Length, revision: 1);
        resources.Apply(scene, delta);

        await Assert.That(page.Length).IsLessThan(duplicatedGeometryBytes / 2);
        await Assert.That(delta.MeshUpserts).IsEqualTo(instanceCount);
        await Assert.That(resources.Statistics.MeshCount).IsEqualTo(instanceCount);
        await Assert.That(resources.Statistics.GeometryBuilds).IsEqualTo(1UL);

        resources.Dispose();
        device.Dispose();
    }

    [Test]
    public async Task InstanceTransformUpdatesUploadOnlyChangedInstanceRange()
    {
        const int instanceCount = 4;
        byte[][] commands = CreateInstanceCommands(instanceCount, changedIndex: -1);
        var scene = new SilkSceneState();
        var device = new CountingGraphicsDevice();
        var resources = new SilkSceneGpuResources(device);
        SilkSceneDelta initialDelta = scene.Apply(
            PerformanceTestData.Concat(commands),
            (uint)commands.Length,
            revision: 1);
        resources.Apply(scene, initialDelta);
        UpdateInstanceBuffer(resources, device, scene);
        CountingGraphicsBuffer instanceBuffer = GetInstanceBuffer(resources);
        int initialWriteCount = instanceBuffer.WriteCount;
        int initialByteCount = instanceBuffer.WrittenByteCount;

        commands = CreateInstanceCommands(instanceCount, changedIndex: 2);
        SilkSceneDelta delta = scene.Apply(
            PerformanceTestData.Concat(commands),
            (uint)commands.Length,
            revision: 2);
        resources.Apply(scene, delta);
        UpdateInstanceBuffer(resources, device, scene);

        await Assert.That(instanceBuffer.WriteCount - initialWriteCount).IsEqualTo(1);
        await Assert.That(instanceBuffer.WrittenByteCount - initialByteCount)
            .IsEqualTo(80);

        resources.Dispose();
        device.Dispose();
    }

    [Test]
    public async Task IndependentValueUpdatesAvoidFullSceneGeometryPayload()
    {
        const int meshCount = 32;
        byte[][] initialCommands = new byte[meshCount][];
        for (int index = 0; index < initialCommands.Length; index++)
        {
            initialCommands[index] = PerformanceTestData.CreateMeshCommand(
                pathValue: $"/World/AnimatedMesh{index}",
                primId: 200 + index,
                triangleCount: 128,
                x: index * 1.25);
        }

        byte[] fullScenePage = PerformanceTestData.Concat(initialCommands);
        var scene = new SilkSceneState();
        var device = new CountingGraphicsDevice();
        var resources = new SilkSceneGpuResources(device);
        SilkSceneDelta initialDelta = scene.Apply(
            fullScenePage,
            (uint)initialCommands.Length,
            revision: 1);
        resources.Apply(scene, initialDelta);
        SilkSceneGpuStatistics initialStatistics = resources.Statistics;

        byte[] valueOnlyPage = PerformanceTestData.CreateMeshCommand(
            pathValue: "/World/AnimatedMesh17",
            primId: 217,
            triangleCount: 128,
            color: 0.25f,
            x: 99);
        SilkSceneDelta delta = scene.Apply(valueOnlyPage, commandCount: 1, revision: 2);
        resources.Apply(scene, delta);

        await Assert.That(valueOnlyPage.Length).IsLessThan(fullScenePage.Length / 16);
        await Assert.That(delta.MeshUpserts).IsEqualTo(1);
        await Assert.That(resources.Statistics.MeshCount).IsEqualTo(meshCount);
        await Assert.That(resources.Statistics.GeometryBuilds)
            .IsEqualTo(initialStatistics.GeometryBuilds);
        await Assert.That(resources.Statistics.VertexUploads)
            .IsEqualTo(initialStatistics.VertexUploads);
        await Assert.That(resources.Statistics.IndexUploads)
            .IsEqualTo(initialStatistics.IndexUploads);

        resources.Dispose();
        device.Dispose();
    }

    [Test]
    public async Task RendererOrdersDrawsByPipelineAndMaterialToReduceBindingChurn()
    {
        var commands = new byte[8][];
        for (int index = 0; index < commands.Length; index++)
        {
            string material = index % 2 == 0 ? "/Looks/A" : "/Looks/B";
            commands[index] = PerformanceTestData.CreateMeshCommand(
                pathValue: $"/World/Mesh{index}",
                primId: 100 + index,
                triangleCount: 1,
                materialPath: material);
        }
        using OpenUsdSilkPage page = CreatePage(
            SilkCommandParser.PageAbiVersion,
            revision: 1,
            PerformanceTestData.Concat(commands),
            (uint)commands.Length);
        var device = new CountingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));

        SilkMeshRenderResult result = renderer.ApplyAndRender(page, color, depth);
        CountingGraphicsCommandList recorded = device.LastSubmittedCommandList ??
            throw new InvalidOperationException("Renderer did not submit commands.");

        await Assert.That(result.DrawCount).IsEqualTo(commands.Length);
        await Assert.That(recorded.PipelineBindCount).IsEqualTo(1);
        await Assert.That(recorded.SurfaceBufferBindCount).IsEqualTo(2);
    }

    [Test]
    public async Task ColdStartLoadsOnlyCheckedShaderArtifacts()
    {
        byte[] pageData = PerformanceTestData.CreateMeshCommand(triangleCount: 2);
        using OpenUsdSilkPage page = CreatePage(
            SilkCommandParser.PageAbiVersion,
            revision: 1,
            pageData,
            commandCount: 1);
        var device = new CountingGraphicsDevice();
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(
            SilkTextureDescriptor.ColorTarget(64, 64));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(
            SilkTextureDescriptor.DepthTarget(64, 64));

        SilkMeshRenderResult result = renderer.ApplyAndRender(page, color, depth);

        await Assert.That(result.DrawCount).IsEqualTo(1);
        // The renderer creates no GPU objects until a draw needs them. It used to
        // build four eager pipelines and their shader program up front for a
        // fast path that bypassed the pipeline cache, but that path was
        // unreachable -- it compared the mesh's SilkVertexLayoutDescriptor to a
        // freshly allocated PositionNormal, and the record struct's array-valued
        // Attributes made that equality reference-based -- so every draw went
        // through the cache anyway and the eager objects were never bound. This
        // count is the measurement: it was 5 pipelines and 4 shader modules for
        // one draw, of which 4 and 2 were dead.
        await Assert.That(device.CreatedShaderModuleCount).IsEqualTo(2);
        await Assert.That(AllShaderModulesMatchCheckedArtifacts(
                device,
                SilkCheckedShaderAssets.LoadMeshVertex(SilkShaderBinaryFormat.SpirV),
                SilkCheckedShaderAssets.LoadMeshFragment(SilkShaderBinaryFormat.SpirV)))
            .IsTrue();
        await Assert.That(device.CreatedPipelineCount).IsEqualTo(1);
    }

    [Test]
    public async Task PipelineCachePermutationsLoadOnlyCheckedShaderArtifacts()
    {
        var device = new CountingGraphicsDevice();
        using var cache = new SilkGraphicsPipelineCache(device, SilkShaderBinaryFormat.SpirV);
        var permutation = new SilkShaderPermutationId(
            SilkShaderFeatures.Uv |
            SilkShaderFeatures.BaseColorMap |
            SilkShaderFeatures.NormalMap);

        using ISilkGraphicsPipeline pipeline = cache.GetOrCreateMeshPipeline(
            permutation,
            SilkVertexLayoutDescriptor.PositionNormal,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureFormat.D32Float);

        await Assert.That(device.CreatedShaderModuleCount).IsEqualTo(2);
        await Assert.That(AllShaderModulesMatchCheckedArtifacts(
                device,
                SilkCheckedShaderAssets.LoadMeshVertex(SilkShaderBinaryFormat.SpirV, permutation),
                SilkCheckedShaderAssets.LoadMeshFragment(SilkShaderBinaryFormat.SpirV, permutation)))
            .IsTrue();
        await Assert.That(device.CreatedPipelineCount).IsEqualTo(1);
    }

    [Test]
    public async Task ManagedPageCounterReturnsToBaseline()
    {
        SilkCounterSnapshot baseline = ReadSilkCounters();
        for (int iteration = 0; iteration < 512; iteration++)
        {
            using OpenUsdSilkPage page = CreatePage(
                SilkCommandParser.PageAbiVersion,
                checked((ulong)iteration + 1),
                [],
                commandCount: 0);
        }

        await Assert.That(ReadSilkCounters()).IsEqualTo(baseline);
    }

    private static OpenUsdSilkPage CreatePage(
        uint abiVersion,
        ulong revision,
        byte[] data,
        uint commandCount)
    {
        ConstructorInfo constructor = typeof(OpenUsdSilkPage).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(uint), typeof(ulong), typeof(byte[]), typeof(uint)],
            modifiers: null) ??
            throw new InvalidOperationException(
                "The managed Silk page constructor is unavailable.");
        return (OpenUsdSilkPage)constructor.Invoke(
            [abiVersion, revision, data, commandCount]);
    }

    private static byte[][] CreateInstanceCommands(int instanceCount, int changedIndex)
    {
        byte[][] commands = new byte[instanceCount][];
        commands[0] = PerformanceTestData.CreateMeshCommand(triangleCount: 8);
        for (int index = 1; index < commands.Length; index++)
        {
            double x = index == changedIndex ? index + 10 : index;
            commands[index] = PerformanceTestData.CreateMeshInstanceReferenceCommand(
                instanceIndex: index,
                x: x);
        }
        return commands;
    }

    private static void UpdateInstanceBuffer(
        SilkSceneGpuResources resources,
        CountingGraphicsDevice device,
        SilkSceneState scene)
    {
        SilkMeshGpuResource[] instances = [.. resources.Meshes.Values];
        object geometry = GetGeometry(instances[0]);
        MethodInfo method = geometry.GetType().GetMethod(
            "UpdateInstanceBuffer",
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException("Instance buffer updater is unavailable.");
        method.Invoke(
            geometry,
            [device, scene.Frame, instances, ((ISilkGraphicsDevice)device).ClipSpaceYPointsDown]);
    }

    private static CountingGraphicsBuffer GetInstanceBuffer(
        SilkSceneGpuResources resources)
    {
        SilkMeshGpuResource first = resources.Meshes.Values.First();
        object geometry = GetGeometry(first);
        PropertyInfo property = geometry.GetType().GetProperty(
            "InstanceBuffer",
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException("Instance buffer property is unavailable.");
        return (CountingGraphicsBuffer)(property.GetValue(geometry) ??
            throw new InvalidOperationException("Instance buffer was not created."));
    }

    private static object GetGeometry(SilkMeshGpuResource mesh)
    {
        PropertyInfo property = typeof(SilkMeshGpuResource).GetProperty(
            "Geometry",
            BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException("Geometry property is unavailable.");
        return property.GetValue(mesh) ??
            throw new InvalidOperationException("Geometry property returned null.");
    }

    private static SilkCounterSnapshot ReadSilkCounters()
    {
        Type diagnostics = typeof(SilkSceneState).Assembly.GetType(
            "OpenUsd.Rendering.Silk.SilkManagedDiagnostics",
            throwOnError: true) ??
            throw new InvalidOperationException("Silk managed diagnostics are unavailable.");
        return new SilkCounterSnapshot(
            ReadCounter(diagnostics, "LiveGpuSceneResources"),
            ReadCounter(diagnostics, "LiveGpuMeshes"),
            ReadCounter(diagnostics, "LivePages"));
    }

    private static int ReadCounter(Type diagnostics, string propertyName)
    {
        PropertyInfo property = diagnostics.GetProperty(
            propertyName,
            BindingFlags.NonPublic | BindingFlags.Static) ??
            throw new InvalidOperationException(
                $"Silk diagnostic property '{propertyName}' is unavailable.");
        return (int)(property.GetValue(null) ??
            throw new InvalidOperationException(
                $"Silk diagnostic property '{propertyName}' returned null."));
    }

    private readonly record struct SilkCounterSnapshot(
        int GpuSceneResources,
        int GpuMeshes,
        int Pages);

    private static bool AllShaderModulesMatchCheckedArtifacts(
        CountingGraphicsDevice device,
        params SilkShaderModuleDescriptor[] checkedArtifacts)
    {
        foreach (SilkShaderModuleDescriptor actual in device.ShaderModules)
        {
            if (!checkedArtifacts.Any(expected =>
                    actual.Stage == expected.Stage &&
                    actual.Format == expected.Format &&
                    actual.EntryPoint == expected.EntryPoint &&
                    actual.Code.Span.SequenceEqual(expected.Code.Span)))
            {
                return false;
            }
        }

        return true;
    }
}
