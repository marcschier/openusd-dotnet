// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkGraphicsPipelineTransitionTests
{
    [Test]
    public Task MaterialBindingsAcrossD3D12PipelineTransitions() =>
        CheckTransitions(SilkGraphicsBackend.D3D12);

    [Test]
    public Task MaterialBindingsAcrossVulkanPipelineTransitions() =>
        CheckTransitions(SilkGraphicsBackend.Vulkan);

    [Test]
    public Task MaterialBindingsAcrossMetalPipelineTransitions() =>
        CheckTransitions(SilkGraphicsBackend.Metal);

    private static async Task CheckTransitions(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        SilkShaderBinaryFormat format = backend switch
        {
            SilkGraphicsBackend.D3D12 => SilkShaderBinaryFormat.Dxil,
            SilkGraphicsBackend.Vulkan => SilkShaderBinaryFormat.SpirV,
            SilkGraphicsBackend.Metal => SilkShaderBinaryFormat.MetalLibrary,
            _ => throw new ArgumentOutOfRangeException(nameof(backend))
        };
        await OffscreenRhiConformance.MaterialBindingsFollowPipelineTransitions(device, format);
    }
}
