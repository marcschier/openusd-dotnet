// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Metal;
using SharpMetal.Metal;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class MetalDeviceTests
{
    [Test]
    [SupportedOSPlatform("macos")]
    public async Task CreatesQueueAndBufferOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();
        using ISilkGraphicsBuffer buffer = device.CreateBuffer(
            4096,
            SilkBufferUsage.Vertex | SilkBufferUsage.Upload);
        byte[] data = [1, 2, 3, 4];
        buffer.Write(data, 128);
        device.WaitIdle();

        await Assert.That(device.Backend).IsEqualTo(SilkGraphicsBackend.Metal);
        await Assert.That(device.Capabilities.SupportsCompute).IsTrue();
        // Not the hardware tier. Tier 2 is necessary but not sufficient: while every
        // checked Metal program takes its textures and samplers as direct entry-point
        // arguments, MetalArgumentBufferCompatibility declines every texture-bearing
        // layout, so no draw can take the table path and advertising it would be false.
        await Assert.That(device.Capabilities.SupportsDescriptorIndexedTextureTables)
            .IsEqualTo(
                device.ArgumentBuffersSupportForTesting == MTLArgumentBuffersTier.Tier2 &&
                !MetalArgumentBufferCompatibility.TryGetCapabilityRejectionReason(out _));
        await Assert.That(device.Capabilities.SupportsDescriptorIndexedTextureTables)
            .IsFalse();
        await Assert.That(device.Capabilities.DescriptorIndexedTextureTablesDiagnostic)
            .IsNotNull();
        await Assert.That(buffer.Size).IsEqualTo((nuint)4096);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task ClearsAndReadsBackOffscreenTextureOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.ClearReadbackAndDisposal(device);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task RoundTripsFloatingPointTexturesOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.FloatingPointTextureRoundTrips(device);
        await SilkMeshRendererConformance.RendersIntoFloatingPointTarget(device);
        await SilkMeshRendererConformance.RendersSelectionIntoFloatingPointTarget(device);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task SubmissionLeasesTextureUntilCompletionOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.SubmittedTextureSurvivesEarlyDispose(device);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task SubmitFailureReleasesTextureLeasesOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.SubmitFailureReleasesAcquiredLeases(device);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task ReadbackWaitsForPendingSubmissionOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.ReadbackWaitsForPendingSubmission(device);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task ClearsAndReadsBackDepthTargetsOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.DepthClearReadbackAndLifetime(device);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task RejectsCrossDeviceDepthTargetsOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice textureDevice = MetalSilkGraphicsDevice.Create();
        using MetalSilkGraphicsDevice commandDevice = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.CrossDeviceDepthTargetIsRejected(
            textureDevice,
            commandDevice);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task UploadsAndReadsBackSampledTexturesOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.TextureUploadReadbackAndLifetime(device);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task UploadsMultiLevelMipChainAndPreservesBaseLevelReadbackOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.MultiLevelTextureUploadPreservesBaseLevelReadback(device);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task RejectsCrossDeviceTextureUploadsOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice textureDevice = MetalSilkGraphicsDevice.Create();
        using MetalSilkGraphicsDevice commandDevice = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.CrossDeviceUploadIsRejected(
            textureDevice,
            commandDevice);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task CreatesAndDisposesSamplersOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.SamplerCreationAndDisposal(device);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task AdvertisesAndHonorsAnisotropicSamplerCapabilityOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        // Apple documents MTLSamplerDescriptor.maxAnisotropy as accepting 1-16 on every
        // device; no SharpMetal API currently exposes a narrower runtime limit to query.
        await Assert.That(device.Capabilities.MaxSamplerAnisotropy).IsEqualTo(16f);
        await OffscreenRhiConformance.AnisotropicSamplerCreationHonorsCapability(device);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task DrawsCheckedIndexedTriangleOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.DrawsIndexedTriangle(
            device,
            SilkShaderBinaryFormat.MetalLibrary);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task CompositesStraightAlphaOverDestinationOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.StraightAlphaPipelineCompositesOverDestination(
            device,
            SilkShaderBinaryFormat.MetalLibrary);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task DrawsIdenticallyThroughAMaterialBindingLayoutOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.MaterialBindingLayoutDrawsIdenticallyToSceneParameters(
            device,
            SilkShaderBinaryFormat.MetalLibrary);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task BindsMaterialTexturesAndSamplersToADrawOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.MaterialResourcesBindToADrawWithoutPerturbingIt(
            device,
            SilkShaderBinaryFormat.MetalLibrary);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task BindsMaterialTexturesDirectlyOnTier2HardwareOnMacOS()
    {
        // Formerly a Tier 2 argument-buffer gate. The path it exercised is now declined
        // for every texture-bearing layout, because no checked Metal program declares an
        // argument buffer, so what this must prove is the opposite: on Tier 2 hardware the
        // encoder still falls through to direct SetFragmentTexture/SetFragmentSamplerState
        // and the draw is unperturbed. Deleting it instead would remove the only macOS
        // coverage of the fallback that Tier 2 devices now always take.
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();
        if (device.ArgumentBuffersSupportForTesting != MTLArgumentBuffersTier.Tier2)
        {
            Skip.Test("Metal reported no Tier 2 argument-buffer support.");
            return;
        }

        await Assert.That(device.Capabilities.SupportsDescriptorIndexedTextureTables)
            .IsFalse();

        await OffscreenRhiConformance.MaterialResourcesBindToADrawWithoutPerturbingIt(
            device,
            SilkShaderBinaryFormat.MetalLibrary);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task RejectsMaterialResourcesTheLayoutDoesNotDeclareOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.MaterialBindingRejectsResourcesTheLayoutDoesNotDeclare(
            device,
            SilkShaderBinaryFormat.MetalLibrary);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task RendersRetainedSilkMeshesOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        RequirePinnedMetalLibrary();

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();
        await SilkMeshRendererConformance.RendersRetainedMeshes(device);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task RejectsCrossDeviceSilkTargetsOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        RequirePinnedMetalLibrary();

        using MetalSilkGraphicsDevice rendererDevice = MetalSilkGraphicsDevice.Create();
        using MetalSilkGraphicsDevice targetDevice = MetalSilkGraphicsDevice.Create();
        await SilkMeshRendererConformance.RejectsCrossDeviceTargets(
            rendererDevice,
            targetDevice);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task LeasesIndexedDrawResourcesUntilCompletionOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.IndexedDrawSubmissionLeasesResources(
            device,
            SilkShaderBinaryFormat.MetalLibrary);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task RejectsCrossDeviceGraphicsResourcesOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using MetalSilkGraphicsDevice resourceDevice =
            MetalSilkGraphicsDevice.Create();
        using MetalSilkGraphicsDevice commandDevice =
            MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.RejectsCrossDeviceGraphicsResources(
            resourceDevice,
            commandDevice,
            SilkShaderBinaryFormat.MetalLibrary);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task PreservesOrderedGraphicsCommandsOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        if (!SilkCheckedShaderAssets.HasPinnedMetalLibrary)
        {
            throw new InvalidOperationException(
                "The ordered Metal conformance test requires a validated " +
                "mesh.metallib and mesh.metallib.manifest.json pair.");
        }

        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.PreservesOrderedGraphicsCommands(
            device,
            SilkShaderBinaryFormat.MetalLibrary);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task DispatchesCheckedComputeKernelsOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        RequirePinnedMetalLibrary();
        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();
        await OffscreenRhiConformance.DispatchesCheckedComputeKernels(
            device,
            SilkShaderBinaryFormat.MetalLibrary);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task LeasesComputeResourcesUntilCompletionOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        RequirePinnedMetalLibrary();
        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();
        await OffscreenRhiConformance.ComputeSubmissionLeasesResources(
            device,
            SilkShaderBinaryFormat.MetalLibrary);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task RejectsInvalidComputeResourcesOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        RequirePinnedMetalLibrary();
        using MetalSilkGraphicsDevice resourceDevice =
            MetalSilkGraphicsDevice.Create();
        using MetalSilkGraphicsDevice commandDevice =
            MetalSilkGraphicsDevice.Create();
        await OffscreenRhiConformance.RejectsInvalidComputeResources(
            resourceDevice,
            commandDevice,
            SilkShaderBinaryFormat.MetalLibrary);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task InterleavesGraphicsAndComputeCommandsOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        RequirePinnedMetalLibrary();
        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();
        await OffscreenRhiConformance.InterleavesGraphicsAndComputeCommands(
            device,
            SilkShaderBinaryFormat.MetalLibrary);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task ComputeGraphicsBufferBarriersOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        RequirePinnedMetalLibrary();
        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();
        await OffscreenRhiConformance.ComputeOutputFeedsVertexBuffer(
            device,
            SilkShaderBinaryFormat.MetalLibrary);
        await OffscreenRhiConformance.ComputeOutputFeedsIndexBuffer(
            device,
            SilkShaderBinaryFormat.MetalLibrary);
    }

    [Test]
    [SupportedOSPlatform("macos")]
    public async Task DispatchBoundariesAndOverflowOnMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip.Test("This test is only applicable on macOS.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        RequirePinnedMetalLibrary();
        using MetalSilkGraphicsDevice device = MetalSilkGraphicsDevice.Create();
        await OffscreenRhiConformance.DispatchBoundariesAndOverflow(
            device,
            SilkShaderBinaryFormat.MetalLibrary);
    }

    private static void RequirePinnedMetalLibrary()
    {
        if (!SilkCheckedShaderAssets.HasPinnedMetalLibrary)
        {
            throw new InvalidOperationException(
                "Metal conformance requires a validated mesh.metallib and " +
                "mesh.metallib.manifest.json pair.");
        }
    }
}
