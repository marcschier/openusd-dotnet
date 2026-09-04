// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.Vulkan;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class VulkanDeviceTests
{
    [Test]
    public async Task ExplicitVulkanLoaderPathIsAbsoluteExistingAndAuthoritative()
    {
        string? previous = Environment.GetEnvironmentVariable(
            VulkanLoaderLibrary.PathEnvironmentVariable);
        string loader = Path.GetTempFileName();
        try
        {
            Environment.SetEnvironmentVariable(
                VulkanLoaderLibrary.PathEnvironmentVariable,
                loader);
            await Assert.That(VulkanLoaderLibrary.GetCandidateNames())
                .IsEquivalentTo([Path.GetFullPath(loader)]);

            Environment.SetEnvironmentVariable(
                VulkanLoaderLibrary.PathEnvironmentVariable,
                "relative-vulkan-loader");
            await Assert.That(() => VulkanLoaderLibrary.GetCandidateNames())
                .Throws<InvalidOperationException>();

            string missing = Path.Combine(
                Path.GetTempPath(),
                $"missing-vulkan-loader-{Guid.NewGuid():N}");
            Environment.SetEnvironmentVariable(
                VulkanLoaderLibrary.PathEnvironmentVariable,
                missing);
            await Assert.That(() => VulkanLoaderLibrary.GetCandidateNames())
                .Throws<FileNotFoundException>();
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                VulkanLoaderLibrary.PathEnvironmentVariable,
                previous);
            File.Delete(loader);
        }
    }

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
    public async Task SwiftShaderRendersReadsAndReusesASampledDepthTarget()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();

        await OffscreenRhiConformance.SampledDepthTargetSurvivesRenderReadAndReuse(
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
    public async Task SwiftShaderAppliesUsdLuxLightLinking()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkLightLinkConformance.LinkedLightsReachOnlyTheirPrims(device);
    }

    [Test]
    public async Task SwiftShaderResolvesNestedInstanceLinkMasks()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkNestedInstanceLinkConformance.ComposedInstancesResolveTheirOwnMasks(device);
    }

    [Test]
    public async Task SwiftShaderAppliesUsdLuxDomeLinking()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkDomeLinkConformance.LinkedDomesReachOnlyTheirPrims(device);
    }

    [Test]
    public async Task SwiftShaderSplitsTheSpecularSkyByDomeLink()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkDomeLinkConformance.LinkedDomesSplitTheSpecularSky(device);
    }

    [Test]
    public async Task SwiftShaderMasksAnUntexturedDomePerDraw()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkDomeLinkConformance.AnUntexturedDomeIsMaskablePerDraw(device);
    }

    [Test]
    public async Task SwiftShaderKeepsEveryInstanceTransformAcrossSplitDomeMasks()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkDomeLinkConformance.SplitDomeMasksKeepEveryInstanceTransform(device);
    }

    [Test]
    public async Task SwiftShaderUploadsTheEnvironmentAgainAfterAFailedSubmission()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkDomeLinkConformance.AFailedSubmissionUploadsTheEnvironmentAgain(device);
    }

    [Test]
    public async Task SwiftShaderCastsAnAuthoredDistantLightShadow()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkShadowConformance.AnAuthoredDistantLightCastsAMeasurableShadow(device);
    }

    [Test]
    public async Task SwiftShaderReusesARetainedShadowMap()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkShadowConformance.ARetainedShadowMapIsReusedUntilItsCastersMove(device);
    }

    [Test]
    public async Task SwiftShaderCastsAnUnlitBlockersShadow()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkShadowConformance.AnUnlitBlockerStillCastsItsShadow(device);
    }

    [Test]
    public async Task SwiftShaderPlacesAYTiltedShadowOnTheComputedSide()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkShadowConformance.AYTiltedShadowLandsOnTheComputedSide(device);
    }

    [Test]
    public async Task SwiftShaderDoesNotSelfShadowARotatedNonUniformReceiver()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkShadowConformance.ARotatedNonUniformReceiverDoesNotSelfShadow(device);
    }

    [Test]
    public async Task SwiftShaderSkipsAndNamesAnOpacityMaskedCaster()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkShadowConformance.AnOpacityMaskedCasterIsSkippedAndNamed(device);
    }

    [Test]
    public async Task SwiftShaderReRendersTheShadowMapWhenACastersMaterialChanges()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkShadowConformance.AMaterialTurningMaskedAndOpaqueAgainReRendersTheMap(device);
    }

    [Test]
    public async Task SwiftShaderLightsAQuadByTheDirectionAndOrientationOfATexturedDome()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkEnvironmentLightingConformance
            .ADirectionalSkyLightsTheQuadByDirectionAndOrientation(device);
    }

    [Test]
    public async Task SwiftShaderResolvesTheDomeContributionScalesAndRoughness()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkEnvironmentLightingConformance
            .TheContributionScalesAndRoughnessDriveTheResponse(device);
    }

    [Test]
    public async Task SwiftShaderFallsBackForAnUnsupportedDomeAndReleasesTheEnvironment()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkEnvironmentLightingConformance
            .AnUnsupportedDomeFallsBackAndRetiringItReleasesTheMaps(device);
    }

    [Test]
    public async Task SwiftShaderLetsATexturedDomeSuppressTheDeterministicHeadlight()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkEnvironmentLightingConformance
            .ATexturedDomeSuppressesTheDeterministicHeadlight(device);
    }

    [Test]
    public async Task SwiftShaderLetsAnUnsupportedDomeSuppressTheDeterministicHeadlight()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkEnvironmentLightingConformance
            .AnUnsupportedDomeSuppressesTheDeterministicHeadlight(device);
    }

    [Test]
    public async Task SwiftShaderReturnsTheSpecularPeakAtExactAlignment()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkEnvironmentLightingConformance
            .TheSpecularLobeReturnsItsPeakAtExactAlignment(device);
    }

    [Test]
    public async Task SwiftShaderKeepsANearMirrorBoundedAtEveryRoughness()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkEnvironmentLightingConformance
            .ANearMirrorStaysBoundedAtEveryRoughness(device);
    }

    [Test]
    public async Task SwiftShaderFollowsRotatedAndNonUniformlyScaledPrimsWithTheEnvironment()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkEnvironmentLightingConformance
            .TheEnvironmentFollowsRotatedAndScaledPrims(device);
    }

    [Test]
    public async Task SwiftShaderLeavesAGeneratedUnlitMaterialUnlitUnderADome()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkEnvironmentLightingConformance
            .AGeneratedUnlitMaterialReceivesNoEnvironment(device);
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
    public async Task SwiftShaderDeformationKernelMatchesTheCpuEvaluator()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkDeformationComputeConformance.DeformationKernelMatchesTheCpuEvaluator(
            device,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderDeformationKernelWritesOnlyPositionsAndNormals()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkDeformationComputeConformance
            .DeformationKernelWritesOnlyPositionsAndNormals(
                device,
                SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderGpuDeformedImageMatchesTheCpuResolvedImage()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        await SilkDeformationRenderConformance.GpuDeformedImageMatchesTheCpuResolvedImage(
            VulkanSilkGraphicsDevice.Create,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderRendersAConstantDisplacementAsDisplacedGeometry()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        await SilkDisplacementRenderConformance.AConstantDisplacementRendersTheDisplacedSurface(
            VulkanSilkGraphicsDevice.Create,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderRendersATextureDisplacementPerVertex()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        await SilkDisplacementRenderConformance
            .ATextureDisplacementRendersThePerVertexDisplacedSurface(
                VulkanSilkGraphicsDevice.Create,
                SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderShadowsFollowTheDisplacedSurface()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        await SilkDisplacementRenderConformance.ShadowsFollowTheDisplacedSurface(
            VulkanSilkGraphicsDevice.Create,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderRendersAnUnsupportedDisplacementUndisplaced()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        await SilkDisplacementRenderConformance
            .AnUnsupportedDisplacementRendersTheUndisplacedSurface(
                VulkanSilkGraphicsDevice.Create,
                SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderRepeatedFramesReuseTheDisplacedGeometry()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        await SilkDisplacementRenderConformance.RepeatedFramesReuseTheDisplacedGeometry(
            VulkanSilkGraphicsDevice.Create,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderDisplacesTheDeformedSurfaceRatherThanTheBindPose()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        await SilkDisplacementRenderConformance.ADisplacedRigDrawsTheDeformedSurfaceDisplaced(
            VulkanSilkGraphicsDevice.Create,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderRepairingAHeightFieldReachesSelectionAndShadows()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        await SilkDisplacementRenderConformance.RepairingAHeightFieldReachesSelectionAndShadows(
            VulkanSilkGraphicsDevice.Create,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderRepeatedFramesReuseAndChangedPosesDispatchOnce()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        await SilkDeformationRenderConformance.RepeatedFramesReuseAndChangedPosesDispatchOnce(
            VulkanSilkGraphicsDevice.Create,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderDeformationSurvivesADeviceGenerationReset()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        await SilkDeformationRenderConformance.ADeviceGenerationResetRedispatchesOnce(
            VulkanSilkGraphicsDevice.Create,
            SilkShaderBinaryFormat.SpirV,
            device => ((VulkanSilkGraphicsDevice)device)
                .InvalidateSelectionOutlineDeviceGenerationForTesting());
    }

    [Test]
    public async Task SwiftShaderAnIneligibleRigDrawsTheCpuGeometry()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        await SilkDeformationRenderConformance.AnIneligibleRigDrawsTheCpuGeometry(
            VulkanSilkGraphicsDevice.Create,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderDeformationParametersAreBoundedByTheirOwnSize()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using ISilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkDeformationComputeConformance.DeformationParametersAreBoundedByTheirOwnSize(
            device,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderAFailedDeformationSetupDrawsTheCpuGeometry()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        await SilkDeformationRenderConformance.AFailedDeformationSetupDrawsTheCpuGeometry(
            VulkanSilkGraphicsDevice.Create,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderAFailedDeformationDispatchDrawsTheCpuGeometry()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        await SilkDeformationRenderConformance.AFailedDeformationDispatchDrawsTheCpuGeometry(
            VulkanSilkGraphicsDevice.Create,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderADeviceLossDuringDeformationDispatchPropagates()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        await SilkDeformationRenderConformance.ADeviceLossDuringDispatchPropagates(
            VulkanSilkGraphicsDevice.Create,
            SilkShaderBinaryFormat.SpirV,
            device => ((VulkanSilkGraphicsDevice)device)
                .InjectNextOffscreenSubmitDeviceLossForTesting(),
            selectionGenerationTracksSubmissionLoss: false);
    }

    [Test]
    public async Task SwiftShaderShadowsFollowTheDeformedSurface()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        await SilkDeformationRenderConformance.ShadowsFollowTheDeformedSurface(
            VulkanSilkGraphicsDevice.Create,
            SilkShaderBinaryFormat.SpirV);
    }

    [Test]
    public async Task SwiftShaderDeformationKernelIsIdempotentForOneIdentity()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Skip.Test("This test is only applicable on Windows or Linux.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
        using VulkanSilkGraphicsDevice device = VulkanSilkGraphicsDevice.Create();
        await SilkDeformationComputeConformance
            .DeformationKernelIsIdempotentForOneIdentity(
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
