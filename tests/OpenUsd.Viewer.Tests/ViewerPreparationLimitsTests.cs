// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerPreparationLimitsTests
{
    [Test]
    public async Task GpuBufferConfigurationIsStrictAndUsesTheEmbeddingHostsSharedPool()
    {
        const string variable = "OPENUSD_SILK_GPU_BUFFER_BYTES";
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, null);
            ViewerStartupOptions.Initialize([]);
            await Assert.That(ViewerStartupOptions.GpuBufferBudget).IsNull();
            Environment.SetEnvironmentVariable(variable, "12345");
            ViewerStartupOptions.Initialize([]);
            await Assert.That(ViewerStartupOptions.GpuBufferBudget!.MaximumBytes).IsEqualTo(12345ul);
            await Assert.That(ViewerStartupOptions.RequiresCompositionPlatform).IsTrue();
            Environment.SetEnvironmentVariable(variable, "invalid");
            await Assert.That(() => ViewerStartupOptions.Initialize([])).Throws<ArgumentException>();
            var shared = new SilkGpuBufferBudget(4321);
            ViewerStartupOptions.Initialize(new ViewerHostOptions { GpuBufferBudget = shared });
            await Assert.That(ViewerStartupOptions.GpuBufferBudget).IsSameReferenceAs(shared);
            await Assert.That(AvaloniaViewerRenderBackendHost.GetPreparationUnsupportedReason(
                RenderBackendKind.Storm, null, shared)).IsNotNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
            ViewerStartupOptions.Initialize([]);
        }
    }

    [Test]
    public async Task SessionPreparationConfigurationRequiresBothCeilingsAndEmbeddingOverridesThem()
    {
        const string meshVariable = "OPENUSD_SILK_MESH_RESERVATION_BYTES";
        const string pageVariable = "OPENUSD_SILK_MAX_PAGE_BYTES";
        string? meshBefore = Environment.GetEnvironmentVariable(meshVariable);
        string? pageBefore = Environment.GetEnvironmentVariable(pageVariable);
        try
        {
            Environment.SetEnvironmentVariable(meshVariable, null);
            Environment.SetEnvironmentVariable(pageVariable, null);
            ViewerStartupOptions.Initialize([]);
            await Assert.That(ViewerStartupOptions.PreparationLimits).IsNull();
            Environment.SetEnvironmentVariable(meshVariable, "5000");
            await Assert.That(() => ViewerStartupOptions.Initialize([])).Throws<ArgumentException>();
            Environment.SetEnvironmentVariable(pageVariable, "6000");
            ViewerStartupOptions.Initialize([]);
            await Assert.That(ViewerStartupOptions.PreparationLimits!.MaximumMeshPreparationReservationBytes)
                .IsEqualTo(5000ul);
            await Assert.That(ViewerStartupOptions.PreparationLimits.MaximumCommandPageBytes).IsEqualTo(6000);
            await Assert.That(ViewerStartupOptions.RequiresCompositionPlatform).IsTrue();
            var embedded = new SilkPreparationLimits(1000, 2000);
            Environment.SetEnvironmentVariable(pageVariable, "invalid");
            ViewerStartupOptions.Initialize(new ViewerHostOptions { PreparationLimits = embedded });
            await Assert.That(ViewerStartupOptions.PreparationLimits).IsSameReferenceAs(embedded);
            await Assert.That(() => ViewerStartupOptions.Initialize([])).Throws<ArgumentException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable(meshVariable, meshBefore);
            Environment.SetEnvironmentVariable(pageVariable, pageBefore);
            ViewerStartupOptions.Initialize([]);
        }
    }

    [Test]
    [Arguments(RenderBackendKind.Storm, true)]
    [Arguments(RenderBackendKind.D3D12, false)]
    [Arguments(RenderBackendKind.Vulkan, false)]
    [Arguments(RenderBackendKind.Metal, false)]
    public async Task ConfiguredAdmissionCannotFallBackToAnUnboundedStormRenderer(
        RenderBackendKind backend, bool refused)
    {
        var limits = new SilkPreparationLimits(1000, 2000);
        string? reason = AvaloniaViewerRenderBackendHost.GetPreparationUnsupportedReason(backend, limits);
        await Assert.That(reason is not null).IsEqualTo(refused);
        if (refused)
        {
            await Assert.That(reason!).Contains("cannot enforce");
        }
        await Assert.That(AvaloniaViewerRenderBackendHost.GetPreparationUnsupportedReason(backend, null)).IsNull();
    }
}
