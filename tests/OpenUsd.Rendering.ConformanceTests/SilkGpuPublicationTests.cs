// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkGpuPublicationTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public Task LateGpuPosePreparationRefusalPreservesPixelsAndAmortizedBufferReuse(SilkGraphicsBackend backend) =>
        SilkDeformationRenderConformance.LatePageRefusalPreservesThePublishedPose(
            () => SilkDepthCaptureConformance.CreateDevice(backend),
            backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV);
}
