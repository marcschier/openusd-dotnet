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
}
