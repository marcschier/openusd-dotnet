// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class VulkanDeviceTests
{
    [Test]
    public async Task DescriptorIndexedTextureTableProbeRecordsSetupFailure()
    {
        var failure = new InvalidOperationException("injected descriptor pool failure");

        VulkanDescriptorIndexedTextureTables? tables =
            VulkanDescriptorIndexedTextureTables.TryCreate(
                null!,
                default,
                _ => throw failure,
                out string? diagnostic);

        await Assert.That(tables).IsNull();
        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!).Contains("Vulkan descriptor-indexed texture tables unavailable");
        await Assert.That(diagnostic!).Contains(nameof(InvalidOperationException));
        await Assert.That(diagnostic!).Contains("injected descriptor pool failure");

        var capabilities = new SilkGraphicsCapabilities(
            "Injected Vulkan",
            "1.3",
            SupportsCompute: true,
            IsSoftware: true)
        {
            SupportsDescriptorIndexedTextureTables = tables is not null,
            DescriptorIndexedTextureTablesDiagnostic = diagnostic
        };
        await Assert.That(capabilities.SupportsDescriptorIndexedTextureTables).IsFalse();
        await Assert.That(capabilities.ToString()).Contains("injected descriptor pool failure");
    }

    [Test]
    public async Task CreatesQueueAndBufferWhenVulkanIsAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        using ISilkGraphicsBuffer buffer = device.CreateBuffer(
            4096,
            SilkBufferUsage.Storage | SilkBufferUsage.Upload);
        byte[] data = [1, 2, 3, 4];
        buffer.Write(data, 128);
        device.WaitIdle();

        await Assert.That(device.Backend).IsEqualTo(SilkGraphicsBackend.Vulkan);
        await Assert.That(device.Capabilities.SupportsCompute).IsTrue();
        if (!device.DescriptorIndexingFeaturesForTesting
            .SupportsDescriptorIndexedTextureTables)
        {
            await Assert.That(device.Capabilities.SupportsDescriptorIndexedTextureTables)
                .IsFalse();
        }
        if (device.Capabilities.DeviceName.Contains(
            "SwiftShader",
            StringComparison.Ordinal))
        {
            await Assert.That(device.Capabilities.SupportsDescriptorIndexedTextureTables)
                .IsTrue();
        }
        await Assert.That(buffer.Size).IsEqualTo((nuint)4096);
    }

    [Test]
    public async Task SwiftShaderClearsAndReadsBackOffscreenTexture()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.ClearReadbackAndDisposal(device);
    }

    [Test]
    public async Task SwiftShaderRoundTripsFloatingPointTextures()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.FloatingPointTextureRoundTrips(device);
        await SilkMeshRendererConformance.RendersIntoFloatingPointTarget(device);
        await SilkMeshRendererConformance.RendersSelectionIntoFloatingPointTarget(device);
    }

    [Test]
    public async Task SwiftShaderSubmissionLeasesTextureUntilCompletion()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.SubmittedTextureSurvivesEarlyDispose(device);
    }

    [Test]
    public async Task SwiftShaderSubmitFailureReleasesTextureLeases()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.SubmitFailureReleasesAcquiredLeases(device);
    }

    [Test]
    public async Task SwiftShaderReadbackWaitsForPendingSubmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.ReadbackWaitsForPendingSubmission(device);
    }

    [Test]
    public async Task SwiftShaderClearsAndReadsBackDepthTargets()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.DepthClearReadbackAndLifetime(device);
    }

    [Test]
    public async Task SwiftShaderRejectsCrossDeviceDepthTargets()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice textureDevice = VulkanSilkGraphicsDevice.Create();
        using VulkanSilkGraphicsDevice commandDevice = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.CrossDeviceDepthTargetIsRejected(
            textureDevice,
            commandDevice);
    }

    [Test]
    public async Task SwiftShaderUploadsAndReadsBackSampledTextures()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.TextureUploadReadbackAndLifetime(device);
    }

    [Test]
    public async Task SwiftShaderUploadsMultiLevelMipChainAndPreservesBaseLevelReadback()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.MultiLevelTextureUploadPreservesBaseLevelReadback(device);
    }

    [Test]
    public async Task SwiftShaderRejectsCrossDeviceTextureUploads()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice textureDevice = VulkanSilkGraphicsDevice.Create();
        using VulkanSilkGraphicsDevice commandDevice = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.CrossDeviceUploadIsRejected(
            textureDevice,
            commandDevice);
    }

    [Test]
    public async Task SwiftShaderCreatesAndDisposesSamplers()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.SamplerCreationAndDisposal(device);
    }

    [Test]
    public async Task SwiftShaderAdvertisesAndHonorsAnisotropicSamplerCapability()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        // SwiftShader may legitimately report samplerAnisotropy as unsupported (a 1x
        // maximum); the shared helper asserts capability-honoring behavior either way
        // without weakening the contract to "anisotropy is always available".
        await OffscreenRhiConformance.AnisotropicSamplerCreationHonorsCapability(device);
    }

    [Test]
    public async Task SwiftShaderDrawsCheckedIndexedTriangle()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.DrawsIndexedTriangle(
            device,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task VulkanCompositesStraightAlphaOverDestination()
    {
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.StraightAlphaPipelineCompositesOverDestination(
            device,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderDrawsIdenticallyThroughAMaterialBindingLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.MaterialBindingLayoutDrawsIdenticallyToSceneParameters(
            device,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderBindsMaterialTexturesAndSamplersToADraw()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.MaterialResourcesBindToADrawWithoutPerturbingIt(
            device,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderRejectsMaterialResourcesTheLayoutDoesNotDeclare()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.MaterialBindingRejectsResourcesTheLayoutDoesNotDeclare(
            device,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderRendersRetainedSilkMeshes()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkMeshRendererConformance.RendersRetainedMeshes(device);
    }

    [Test]
    public async Task SwiftShaderRejectsCrossDeviceSilkTargets()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice rendererDevice = VulkanSilkGraphicsDevice.Create();
        using VulkanSilkGraphicsDevice targetDevice = VulkanSilkGraphicsDevice.Create();
        await SilkMeshRendererConformance.RejectsCrossDeviceTargets(
            rendererDevice,
            targetDevice);
    }

    [Test]
    public async Task SwiftShaderLeasesIndexedDrawResourcesUntilCompletion()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.IndexedDrawSubmissionLeasesResources(
            device,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderRejectsCrossDeviceGraphicsResources()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("This test is only applicable on Windows.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice resourceDevice =
            VulkanSilkGraphicsDevice.Create();
        using VulkanSilkGraphicsDevice commandDevice =
            VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.RejectsCrossDeviceGraphicsResources(
            resourceDevice,
            commandDevice,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderPreservesOrderedGraphicsCommands()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.PreservesOrderedGraphicsCommands(
            device,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderDispatchesCheckedComputeKernels()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await OffscreenRhiConformance.DispatchesCheckedComputeKernels(
            device,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderLeasesComputeResourcesUntilCompletion()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await OffscreenRhiConformance.ComputeSubmissionLeasesResources(
            device,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderRejectsInvalidComputeResources()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using VulkanSilkGraphicsDevice resourceDevice =
            VulkanSilkGraphicsDevice.Create();
        using VulkanSilkGraphicsDevice commandDevice =
            VulkanSilkGraphicsDevice.Create();
        await OffscreenRhiConformance.RejectsInvalidComputeResources(
            resourceDevice,
            commandDevice,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderInterleavesGraphicsAndComputeCommands()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await OffscreenRhiConformance.InterleavesGraphicsAndComputeCommands(
            device,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderComputeGraphicsBufferBarriers()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await OffscreenRhiConformance.ComputeOutputFeedsVertexBuffer(
            device,
            SilkShaderBinaryFormat.SpirV);
        await OffscreenRhiConformance.ComputeOutputFeedsIndexBuffer(
            device,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderDispatchBoundariesAndOverflow()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await OffscreenRhiConformance.DispatchBoundariesAndOverflow(
            device,
            SilkShaderBinaryFormat.SpirV);
    }
}
