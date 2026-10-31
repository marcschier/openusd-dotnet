// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Viewer.Tests;

[NotInParallel]
public sealed class ViewerPreparationLimitsTests
{
    [Test]
    public async Task GpuUploadPolicyIsStrictOverridableAndCannotBypassD3D12()
    {
        const string flag = "OPENUSD_D3D12_GPU_UPLOAD_HEAPS";
        const string buffers = "OPENUSD_SILK_GPU_BUFFER_BYTES";
        string? oldFlag = Environment.GetEnvironmentVariable(flag);
        string? oldBuffers = Environment.GetEnvironmentVariable(buffers);
        try
        {
            Environment.SetEnvironmentVariable(flag, null);
            Environment.SetEnvironmentVariable(buffers, null);
            ViewerStartupOptions.Initialize([]);
            await Assert.That(ViewerStartupOptions.D3D12GpuUploadHeaps).IsFalse();
            Environment.SetEnvironmentVariable(flag, "true");
            await Assert.That(() => ViewerStartupOptions.Initialize([])).Throws<ArgumentException>();
            var budget = new SilkGpuBufferBudget(4096);
            ViewerStartupOptions.Initialize(new ViewerHostOptions { GpuBufferBudget = budget });
            await Assert.That(ViewerStartupOptions.D3D12GpuUploadHeaps).IsTrue();
            await Assert.That(ViewerStartupOptions.RequiresCompositionPlatform).IsTrue();
            foreach (RenderBackendKind kind in new[]
                { RenderBackendKind.Storm, RenderBackendKind.Vulkan, RenderBackendKind.Metal })
            {
                await Assert.That(AvaloniaViewerRenderBackendHost.GetPreparationUnsupportedReason(
                    kind, null, budget, gpuUploadHeaps: true)).IsNotNull();
            }
            await Assert.That(AvaloniaViewerRenderBackendHost.GetPreparationUnsupportedReason(
                RenderBackendKind.D3D12, null, budget, gpuUploadHeaps: true)).IsNull();
            Environment.SetEnvironmentVariable(flag, "invalid");
            await Assert.That(() => ViewerStartupOptions.Initialize([])).Throws<ArgumentException>();
            ViewerStartupOptions.Initialize(new ViewerHostOptions { D3D12GpuUploadHeaps = false });
            await Assert.That(ViewerStartupOptions.D3D12GpuUploadHeaps).IsFalse();
            await Assert.That(ViewerStartupOptions.GpuBufferBudget).IsNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(flag, oldFlag);
            Environment.SetEnvironmentVariable(buffers, oldBuffers);
            ViewerStartupOptions.Initialize([]);
        }
    }

    [Test]
    public async Task StreamingRequiresAdmissionAndEmbeddingCanDisableAnAmbientOptIn()
    {
        const string flag = "OPENUSD_SILK_STREAM_MATERIAL_TEXTURES";
        const string buffers = "OPENUSD_SILK_GPU_BUFFER_BYTES";
        string? oldFlag = Environment.GetEnvironmentVariable(flag);
        string? oldBuffers = Environment.GetEnvironmentVariable(buffers);
        try
        {
            Environment.SetEnvironmentVariable(flag, null);
            Environment.SetEnvironmentVariable(buffers, null);
            ViewerStartupOptions.Initialize([]);
            await Assert.That(ViewerStartupOptions.TextureResidencyOptions.StreamMaterialTextures).IsFalse();
            Environment.SetEnvironmentVariable(flag, "true");
            await Assert.That(() => ViewerStartupOptions.Initialize([])).Throws<ArgumentException>();
            Environment.SetEnvironmentVariable(buffers, "1048576");
            ViewerStartupOptions.Initialize([]);
            await Assert.That(ViewerStartupOptions.TextureResidencyOptions.StreamMaterialTextures).IsTrue();
            await Assert.That(ViewerStartupOptions.RequiresCompositionPlatform).IsTrue();
            Environment.SetEnvironmentVariable(flag, "invalid");
            await Assert.That(() => ViewerStartupOptions.Initialize([])).Throws<ArgumentException>();
            ViewerStartupOptions.Initialize(new ViewerHostOptions { StreamMaterialTextures = false });
            await Assert.That(ViewerStartupOptions.TextureResidencyOptions.StreamMaterialTextures).IsFalse();
            ViewerStartupOptions.Initialize(new ViewerHostOptions
            {
                StreamMaterialTextures = true,
                GpuBufferBudget = new(4096)
            });
            await Assert.That(ViewerStartupOptions.TextureResidencyOptions.StreamMaterialTextures).IsTrue();
            await Assert.That(ViewerStartupOptions.GpuBufferBudget!.MaximumBytes).IsEqualTo(4096ul);
        }
        finally
        {
            Environment.SetEnvironmentVariable(flag, oldFlag);
            Environment.SetEnvironmentVariable(buffers, oldBuffers);
            ViewerStartupOptions.Initialize([]);
        }
    }

    [Test]
    public async Task StoredCommandPolicyIsOptInAndEmbeddingOverridesAmbientStorage()
    {
        string[] variables = ["OPENUSD_SILK_MESH_RESERVATION_BYTES", "OPENUSD_SILK_MAX_PAGE_BYTES",
            "OPENUSD_SILK_STORAGE_LIVE_BYTES", "OPENUSD_SILK_STORAGE_UPDATE_BYTES",
            "OPENUSD_SILK_STORAGE_COMMAND_BYTES", "OPENUSD_SILK_STORAGE_TRANSFER_BYTES"];
        string?[] before = variables.Select(Environment.GetEnvironmentVariable).ToArray();
        string[] values = ["1000000", "65536", "6000000000", "5000000000", "32768", "4096"];
        try
        {
            for (int index = 0; index < variables.Length; index++)
            {
                Environment.SetEnvironmentVariable(variables[index], values[index]);
            }
            ViewerStartupOptions.Initialize([]);
            SilkCommandStorageLimits storage = ViewerStartupOptions.PreparationLimits!.CommandStorage!;
            await Assert.That(storage.MaximumLiveBytes).IsEqualTo(6000000000ul);
            await Assert.That(storage.MaximumUpdateBytes).IsEqualTo(5000000000ul);
            await Assert.That(storage.MaximumCommandBytes).IsEqualTo(32768);
            await Assert.That(storage.TransferBufferBytes).IsEqualTo(4096);
            Environment.SetEnvironmentVariable(variables[5], "invalid");
            await Assert.That(() => ViewerStartupOptions.Initialize([])).Throws<ArgumentException>();
            var embedded = new SilkPreparationLimits(1000000, 65536);
            ViewerStartupOptions.Initialize(new ViewerHostOptions { PreparationLimits = embedded });
            await Assert.That(ViewerStartupOptions.PreparationLimits).IsSameReferenceAs(embedded);
            await Assert.That(ViewerStartupOptions.PreparationLimits!.CommandStorage).IsNull();
        }
        finally
        {
            for (int index = 0; index < variables.Length; index++)
            {
                Environment.SetEnvironmentVariable(variables[index], before[index]);
            }
            ViewerStartupOptions.Initialize([]);
        }
    }

    [Test]
    public async Task TexturePoliciesShareEmbeddingPoolsAndOverrideTheEntireEnvironmentPolicy()
    {
        string[] variables =
            ["OPENUSD_SILK_CPU_TEXTURE_BYTES", "OPENUSD_SILK_GPU_TEXTURE_BYTES", "OPENUSD_SILK_GPU_STAGING_BYTES"];
        string?[] previous = variables.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            foreach (string variable in variables)
            {
                Environment.SetEnvironmentVariable(variable, null);
            }
            ViewerStartupOptions.Initialize([]);
            await Assert.That(ViewerStartupOptions.TexturePreparationBudgets).IsNull();
            for (int domain = 0; domain < variables.Length; domain++)
            {
                Environment.SetEnvironmentVariable(variables[domain], "12345");
                ViewerStartupOptions.Initialize([]);
                SilkTexturePreparationBudgets policy = ViewerStartupOptions.TexturePreparationBudgets!;
                await Assert.That(policy.CpuTextureBudget?.MaximumBytes)
                    .IsEqualTo(domain == 0 ? 12345ul : (ulong?)null);
                await Assert.That(policy.GpuTextureBudget?.MaximumBytes)
                    .IsEqualTo(domain == 1 ? 12345ul : (ulong?)null);
                await Assert.That(policy.GpuStagingBudget?.MaximumBytes)
                    .IsEqualTo(domain == 2 ? 12345ul : (ulong?)null);
                await Assert.That(ViewerStartupOptions.RequiresCompositionPlatform).IsTrue();
                await Assert.That(AvaloniaViewerRenderBackendHost.GetPreparationUnsupportedReason(
                    RenderBackendKind.Storm, null, null, policy)).IsNotNull();
                Environment.SetEnvironmentVariable(variables[domain], "invalid");
                await Assert.That(() => ViewerStartupOptions.Initialize([])).Throws<ArgumentException>();
                var shared = new SilkTexturePreparationBudgets(null, new(4321), null);
                ViewerStartupOptions.Initialize(new ViewerHostOptions { TexturePreparationBudgets = shared });
                await Assert.That(ViewerStartupOptions.TexturePreparationBudgets).IsSameReferenceAs(shared);
                await Assert.That(shared.CpuTextureBudget).IsNull();
                await Assert.That(shared.GpuStagingBudget).IsNull();
                Environment.SetEnvironmentVariable(variables[domain], null);
            }
        }
        finally
        {
            for (int index = 0; index < variables.Length; index++)
            {
                Environment.SetEnvironmentVariable(variables[index], previous[index]);
            }
            ViewerStartupOptions.Initialize([]);
        }
    }

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
        var texturePolicy = new SilkTexturePreparationBudgets(null, new(1000), null);
        await Assert.That(AvaloniaViewerRenderBackendHost.GetPreparationUnsupportedReason(
            backend, null, null, texturePolicy) is not null).IsEqualTo(refused);
    }
}
