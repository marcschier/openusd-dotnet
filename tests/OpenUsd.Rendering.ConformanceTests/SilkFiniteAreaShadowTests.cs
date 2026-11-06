// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkFiniteAreaShadowTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task RectEmitterShadowsMatchFiveRayReferenceAcrossUpdateAndRetirement(
        SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        // Replacement retains the published 3x3 atlas while preparing its candidate.
        var budget = new SilkGpuTextureBudget((2 * 3_072ul * 3_072 * sizeof(float)) + 1_048_576);
        budget.ConfigureDevice(device);
        await SilkFiniteAreaShadowConformance.RectEmitterShadowsMatchFiveRayReferenceAcrossUpdateAndRetirement(
            device);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task TransformedRectEmitterShadowsMatchFiveRayReference(
        SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(67_108_864);
        budget.ConfigureDevice(device);
        await SilkFiniteAreaShadowConformance.TransformedRectEmitterShadowsMatchFiveRayReference(
            device);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }
}
