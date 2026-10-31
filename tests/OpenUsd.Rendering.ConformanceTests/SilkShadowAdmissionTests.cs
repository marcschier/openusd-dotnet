// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkShadowAdmissionTests
{
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
