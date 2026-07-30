// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class VulkanDeviceTests
{
    [Test]
    public async Task CreatesQueueAndBufferWhenVulkanIsAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
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
        await Assert.That(device.Capabilities.SupportsDescriptorIndexedTextureTables)
            .IsFalse();
        await Assert.That(buffer.Size).IsEqualTo((nuint)4096);
    }

    [Test]
    public async Task SwiftShaderClearsAndReadsBackOffscreenTexture()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.ClearReadbackAndDisposal(device);
    }

    [Test]
    public async Task SwiftShaderSubmissionLeasesTextureUntilCompletion()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.SubmittedTextureSurvivesEarlyDispose(device);
    }

    [Test]
    public async Task SwiftShaderSubmitFailureReleasesTextureLeases()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.SubmitFailureReleasesAcquiredLeases(device);
    }

    [Test]
    public async Task SwiftShaderReadbackWaitsForPendingSubmission()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.ReadbackWaitsForPendingSubmission(device);
    }

    [Test]
    public async Task SwiftShaderClearsAndReadsBackDepthTargets()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.DepthClearReadbackAndLifetime(device);
    }

    [Test]
    public async Task SwiftShaderRejectsCrossDeviceDepthTargets()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
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
            return;
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.TextureUploadReadbackAndLifetime(device);
    }

    [Test]
    public async Task SwiftShaderRejectsCrossDeviceTextureUploads()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
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
            return;
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.SamplerCreationAndDisposal(device);
    }

    [Test]
    public async Task SwiftShaderDrawsCheckedIndexedTriangle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.DrawsIndexedTriangle(
            device,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderDrawsIdenticallyThroughAMaterialBindingLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
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
            return;
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
            return;
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
            return;
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkMeshRendererConformance.RendersRetainedMeshes(device);
    }

    [Test]
    public async Task SwiftShaderRejectsCrossDeviceSilkTargets()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            return;
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
            return;
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
            return;
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
            return;
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
            return;
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
            return;
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
            return;
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
            return;
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
            return;
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
            return;
        }
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await OffscreenRhiConformance.DispatchBoundariesAndOverflow(
            device,
            SilkShaderBinaryFormat.SpirV);
    }
}
