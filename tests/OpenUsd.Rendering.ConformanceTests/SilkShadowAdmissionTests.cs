// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkShadowAdmissionTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task EveryPointCubeFacePreservesWorldLightingUpdatesAndRestoration(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(134_217_728);
        budget.ConfigureDevice(device);
        await SilkShadowConformance.EveryPointCubeFaceUpdatesAndRestoresPixels(device);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task PointReceiverBeyondFarPlaneRemainsLitWhenPerspectiveDepthRoundsToOne(
        SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(134_217_728);
        budget.ConfigureDevice(device);
        await SilkShadowConformance.PointReceiverBeyondFarPlaneRemainsLit(device);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task ShadowQuotaRefusalPreservesPriorPixelsAndRetriesWithoutNativeLeaks(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(67_108_864);
        budget.ConfigureDevice(device);
        await SilkShadowConformance.AdmissionRefusalKeepsPriorPixelsUntilTheShadowCandidateCompletes(device, budget);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task AdmittedShadowSlotsPreserveAuthoredGeometryAndRetirementPixels(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(67_108_864);
        budget.ConfigureDevice(device);
        await SilkShadowConformance.AnAuthoredDistantLightCastsAMeasurableShadow(device);
        await SilkShadowConformance.ARetainedShadowMapIsReusedUntilItsCastersMove(device);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task ConstantOpacityThresholdChangesShadowCoverageAndReleasesResources(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(67_108_864);
        budget.ConfigureDevice(device);
        await SilkShadowConformance.ConstantOpacityThresholdChangesCasterCoverage(device);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task ConstantOpacityThresholdChangesShadowCoverageWithoutAdmission(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        await SilkShadowConformance.ConstantOpacityThresholdChangesCasterCoverage(device);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true)]
    public async Task TexturedCutoffCastsRetainedAndUpdatedShadows(
        SilkGraphicsBackend backend, bool admission, bool streaming)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var textureBudget = admission ? new SilkGpuTextureBudget(67_108_864) : null;
        var bufferBudget = streaming ? new SilkGpuBufferBudget(4_194_304) : null;
        textureBudget?.ConfigureDevice(device);
        bufferBudget?.ConfigureDevice(device);
        await SilkShadowConformance.TexturedOpacityChangesCasterCoverage(device, backend, streaming);
        if (textureBudget is not null)
        {
            await Assert.That(textureBudget.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(textureBudget.Usage.ReservationCount).IsEqualTo(0ul);
        }
        if (bufferBudget is not null)
        {
            await Assert.That(bufferBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, false, false, true)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false, true)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true, true)]
    public async Task InPlaceOpacityEditsInvalidateRetainedShadowsAndRetry(
        SilkGraphicsBackend backend, bool admission, bool streaming, bool udim)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var textureBudget = admission ? new SilkGpuTextureBudget(67_108_864) : null;
        var bufferBudget = streaming ? new SilkGpuBufferBudget(4_194_304) : null;
        textureBudget?.ConfigureDevice(device);
        bufferBudget?.ConfigureDevice(device);
        await SilkShadowConformance.InPlaceOpacityEditsInvalidateRetainedShadows(
            device, backend, streaming, udim, textureBudget);
        if (textureBudget is not null)
        {
            await Assert.That(textureBudget.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(textureBudget.Usage.ReservationCount).IsEqualTo(0ul);
        }
        if (bufferBudget is not null)
        {
            await Assert.That(bufferBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, false, false, true)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false, true)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true, true)]
    public async Task RetryingFailedOpacityRebuildsRetainedShadows(
        SilkGraphicsBackend backend, bool admission, bool streaming, bool withScene)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var textureBudget = admission ? new SilkGpuTextureBudget(67_108_864) : null;
        var bufferBudget = streaming ? new SilkGpuBufferBudget(4_194_304) : null;
        textureBudget?.ConfigureDevice(device);
        bufferBudget?.ConfigureDevice(device);
        await SilkShadowConformance.RetryingFailedOpacityRebuildsRetainedShadows(
            device, backend, streaming, withScene);
        if (textureBudget is not null)
        {
            await Assert.That(textureBudget.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(textureBudget.Usage.ReservationCount).IsEqualTo(0ul);
        }
        if (bufferBudget is not null)
        {
            await Assert.That(bufferBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true)]
    public async Task MaskedInstancesAndMaterialGroupsKeepTheirOwnCoverage(
        SilkGraphicsBackend backend, bool admission, bool streaming)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var textureBudget = admission ? new SilkGpuTextureBudget(67_108_864) : null;
        var bufferBudget = streaming ? new SilkGpuBufferBudget(4_194_304) : null;
        textureBudget?.ConfigureDevice(device);
        bufferBudget?.ConfigureDevice(device);
        await SilkShadowConformance.MaskedInstancesAndMaterialGroupsKeepTheirOwnCoverage(device, backend, streaming);
        if (textureBudget is not null)
        {
            await Assert.That(textureBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        if (bufferBudget is not null)
        {
            await Assert.That(bufferBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true)]
    public async Task MaskedShadowsUseSecondaryUvAndAlternateTransform(
        SilkGraphicsBackend backend, bool admission, bool streaming)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var textureBudget = admission ? new SilkGpuTextureBudget(67_108_864) : null;
        var bufferBudget = streaming ? new SilkGpuBufferBudget(4_194_304) : null;
        textureBudget?.ConfigureDevice(device);
        bufferBudget?.ConfigureDevice(device);
        await SilkShadowConformance.MaskedShadowsUseSecondaryUvAndAlternateTransform(device, backend, streaming);
        if (textureBudget is not null)
        {
            await Assert.That(textureBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        if (bufferBudget is not null)
        {
            await Assert.That(bufferBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true)]
    public async Task MaskedShadowsUseOpacityChannelAffineCompositeAndBorder(
        SilkGraphicsBackend backend, bool admission, bool streaming)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var textureBudget = admission ? new SilkGpuTextureBudget(67_108_864) : null;
        var bufferBudget = streaming ? new SilkGpuBufferBudget(4_194_304) : null;
        textureBudget?.ConfigureDevice(device);
        bufferBudget?.ConfigureDevice(device);
        await SilkShadowConformance.MaskedShadowsUseOpacityChannelAffineCompositeAndBorder(
            device, backend, streaming);
        if (textureBudget is not null)
        {
            await Assert.That(textureBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        }
        if (bufferBudget is not null)
        {
            await Assert.That(bufferBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task MaskedShadowCoverageMatchesColorPassAtCutoff(
        SilkGraphicsBackend backend, bool streaming)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var bufferBudget = streaming ? new SilkGpuBufferBudget(4_194_304) : null;
        bufferBudget?.ConfigureDevice(device);
        await SilkShadowConformance.MaskedShadowCoverageMatchesColorPassAtCutoff(device, backend, streaming);
        if (bufferBudget is not null)
        {
            await Assert.That(bufferBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, 0)]
    [Arguments(SilkGraphicsBackend.D3D12, false, 1)]
    [Arguments(SilkGraphicsBackend.D3D12, false, 2)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 0)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 1)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 2)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 0)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 1)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 2)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 0)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 1)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 2)]
    public Task MaskedShadowSubmissionRefusalRestoresPriorFrameAndRetries(
        SilkGraphicsBackend backend, bool streaming, int successfulSubmissions) =>
        SilkShadowConformance.MaskedShadowSubmissionRefusalRestoresPriorFrameAndRetries(
            backend, streaming, successfulSubmissions);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public Task MaskedShadowSubmissionRefusalWithOnlyBufferAdmissionRestoresTextureState(
        SilkGraphicsBackend backend) =>
        SilkShadowConformance.MaskedShadowSubmissionRefusalRestoresPriorFrameAndRetries(
            backend, streaming: false, successfulSubmissions: 1, textureAdmission: false);

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task SceneMaterialsToggleUsesTheUnboundCaster(SilkGraphicsBackend backend, bool admission)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = admission ? new SilkGpuTextureBudget(67_108_864) : null;
        budget?.ConfigureDevice(device);
        await SilkShadowConformance.SceneMaterialsToggleMatchesUnboundCaster(device);
        if (budget is not null)
        {
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task SceneMaterialsToggleUsesTheUnboundSurface(SilkGraphicsBackend backend, bool admission)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = admission ? new SilkGpuTextureBudget(67_108_864) : null;
        budget?.ConfigureDevice(device);
        await SilkShadowConformance.SceneMaterialsToggleMatchesUnboundSurface(device);
        if (budget is not null)
        {
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, 1f)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 1f)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 1f)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 1f)]
    [Arguments(SilkGraphicsBackend.D3D12, false, 2f)]
    [Arguments(SilkGraphicsBackend.D3D12, true, 2f)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, 2f)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, 2f)]
    public async Task DisplacedCasterRemainsInsideTheShadowBounds(
        SilkGraphicsBackend backend,
        bool admission,
        float casterScale)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = admission ? new SilkGpuTextureBudget(67_108_864) : null;
        budget?.ConfigureDevice(device);
        await SilkDisplacementRenderConformance.ShadowBoundsFollowDisplacementTowardTheLight(
            device,
            casterScale);
        if (budget is not null)
        {
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task ImageDrivenDisplacementRemainsInsideTheShadowBounds(
        SilkGraphicsBackend backend,
        bool admission)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = admission ? new SilkGpuTextureBudget(67_108_864) : null;
        budget?.ConfigureDevice(device);
        await SilkDisplacementRenderConformance.ImageDrivenShadowBoundsFollowHeightFieldUpdate(
            device,
            backend == SilkGraphicsBackend.D3D12
                ? SilkShaderBinaryFormat.Dxil
                : SilkShaderBinaryFormat.SpirV);
        if (budget is not null)
        {
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task DeformedDisplacementRemainsInsideTheShadowBounds(
        SilkGraphicsBackend backend,
        bool admission)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = admission ? new SilkGpuTextureBudget(67_108_864) : null;
        budget?.ConfigureDevice(device);
        await SilkDisplacementRenderConformance.DeformedShadowBoundsFollowDisplacement(device);
        if (budget is not null)
        {
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task InstancedDisplacementRemainsInsideTheShadowBounds(
        SilkGraphicsBackend backend,
        bool admission)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = admission ? new SilkGpuTextureBudget(67_108_864) : null;
        budget?.ConfigureDevice(device);
        await SilkDisplacementRenderConformance.InstancedShadowBoundsFollowDisplacement(device);
        if (budget is not null)
        {
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        }
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task PerspectiveSpotShadowPixelsMatchAnalyticReceiver(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        await SilkShadowConformance.PerspectiveSpotShadowPixelsMatchAnalyticReceiver(device);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task DisplacedSpotProjectionRefusalPreservesPriorFrameAndAllowsRetry(
        SilkGraphicsBackend backend,
        bool admission)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = admission ? new SilkGpuTextureBudget(67_108_864) : null;
        budget?.ConfigureDevice(device);
        await SilkShadowConformance.DisplacedSpotProjectionRefusalAllowsCorrectedRetry(device);
        if (budget is not null)
        {
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        }
    }

}
