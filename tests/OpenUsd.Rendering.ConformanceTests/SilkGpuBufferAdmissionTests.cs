// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkGpuBufferAdmissionTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task BackendBuffersObeyExactSharedPayloadCapacity(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice first = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsDevice second = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(100);
        budget.ConfigureDevice(first);
        budget.ConfigureDevice(second);
        using var resources = new SilkSceneGpuResources(first);
        RenderDiagnosticsState diagnostics = resources.Diagnostics;
        using ISilkGraphicsBuffer a = first.CreateBuffer(60, SilkBufferUsage.Upload);
        using ISilkGraphicsBuffer b = second.CreateBuffer(40, SilkBufferUsage.Upload);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(100ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(2ul);
        await Assert.That(() =>
        {
            using ISilkGraphicsBuffer refused = first.CreateBuffer(1, SilkBufferUsage.Upload);
        }).Throws<SilkGpuBufferBudgetExceededException>();
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(100ul);
        a.Dispose();
        using ISilkGraphicsBuffer c = second.CreateBuffer(60, SilkBufferUsage.Upload);
        b.Dispose();
        c.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        await Assert.That(resources.Diagnostics.Equals(diagnostics)).IsTrue();
        await Assert.That(diagnostics.Entries.Single().Code).IsEqualTo("HDSILK_GPU_BUFFER_ADMISSION");
        await Assert.That(diagnostics.Entries.Single().Message).Contains("ceiling=100");
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task ConcurrentBackendAllocationsShareTheSameAdmissionPool(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice first = SilkDepthCaptureConformance.CreateDevice(backend);
        using ISilkGraphicsDevice second = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(256);
        budget.ConfigureDevice(first);
        budget.ConfigureDevice(second);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int arrived = 0;
        int admitted = 0;
        int refused = 0;
        Task[] work = Enumerable.Range(0, 16).Select(index => Task.Run(async () =>
        {
            ISilkGraphicsBuffer? buffer = null;
            try
            {
                buffer = (index % 2 == 0 ? first : second).CreateBuffer(64, SilkBufferUsage.Upload);
                Interlocked.Increment(ref admitted);
            }
            catch (SilkGpuBufferBudgetExceededException)
            {
                Interlocked.Increment(ref refused);
            }
            finally
            {
                if (Interlocked.Increment(ref arrived) == 16)
                {
                    ready.TrySetResult();
                }
            }
            await release.Task;
            buffer?.Dispose();
        })).ToArray();
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(admitted).IsEqualTo(4);
            await Assert.That(refused).IsEqualTo(12);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(256ul);
            await Assert.That(budget.Usage.ReservationCount).IsEqualTo(4ul);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(work);
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(256ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task RealComputeSubmissionKeepsDisposedBufferReservationsUntilCompletion(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(1_000_000);
        await OffscreenRhiConformance.ComputeSubmissionLeasesResources(device,
            backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV,
            budget);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public Task BudgetRefusalCannotSilentlyDowngradeGpuDeformation(SilkGraphicsBackend backend) =>
        SilkDeformationRenderConformance.BufferBudgetRefusalPreservesGpuDeformation(
            () => SilkDepthCaptureConformance.CreateDevice(backend),
            backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV);
}
