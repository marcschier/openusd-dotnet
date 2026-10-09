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
}
