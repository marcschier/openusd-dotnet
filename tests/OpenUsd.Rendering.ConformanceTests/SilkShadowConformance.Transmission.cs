// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal static partial class SilkShadowConformance
{
    internal static async Task BlendedLayersMatchAnalyticPixels(
        ISilkGraphicsDevice device, int layers, bool behindReceiver)
    {
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Size, Size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        SilkMeshRendererConformance.Apply(renderer, 1, CreateShadowLightFrame(), CreateReceiver(),
            CreateCasterMaterial([(SilkMaterialParameter.Opacity, 0.5f)]),
            CreateLayeredCaster(layers, behindReceiver));
        _ = renderer.Render(color, depth);
        byte[] lit = ReadPixels(color);
        SilkMeshRendererConformance.Apply(renderer, 2, CreateTransmissionShadow());
        _ = renderer.Render(color, depth, SilkMeshRenderOptions.Default with { UseSceneMaterials = false });
        byte[] opaque = ReadPixels(color);
        _ = renderer.Render(color, depth);
        byte[] actual = ReadPixels(color);
        int frontLayers = layers - (behindReceiver ? 1 : 0);
        if (frontLayers > 0)
        {
            await Assert.That(lit[Offset(ShadowedX)] - opaque[Offset(ShadowedX)]).IsGreaterThan(50);
        }
        await AssertTransmissionPixel(actual, lit, opaque, Math.Pow(0.5, frontLayers));
        await Assert.That(HasCasterDiagnostic(renderer)).IsFalse();
        ulong rendered = renderer.ShadowMapRenderCount;
        _ = renderer.Render(color, depth);
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(rendered);
        await Assert.That(ReadPixels(color).SequenceEqual(actual)).IsTrue();

        SilkMeshRendererConformance.Apply(renderer, 3,
            CreateCasterMaterial([(SilkMaterialParameter.Opacity, 0.25f)]));
        _ = renderer.Render(color, depth);
        await AssertTransmissionPixel(ReadPixels(color), lit, opaque, Math.Pow(0.75, frontLayers));
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(rendered + 1);
        float[] animatedPoints = renderer.Scene.MeshesByPath[("/World/Layers", 0)].Points.ToArray();
        for (int point = 0; point < animatedPoints.Length; point += 3)
        {
            animatedPoints[point] += 0.7f;
        }
        SilkDeformationResult deformation = renderer.Scene.ReplacePoints(
            "/World/Layers", 0, animatedPoints, out ulong meshId);
        await Assert.That(deformation).IsEqualTo(SilkDeformationResult.Applied);
        renderer.GpuResources.Apply(renderer.Scene,
            new SilkSceneDelta(new[] { meshId }, ReadOnlyMemory<ulong>.Empty));
        _ = renderer.Render(color, depth);
        await AssertTransmissionPixel(ReadPixels(color), lit, opaque, 1);
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(rendered + 2);
        SilkMeshRendererConformance.Apply(renderer, 4, CreateLayeredCaster(layers, behindReceiver, x: 0.7));
        _ = renderer.Render(color, depth);
        await AssertTransmissionPixel(ReadPixels(color), lit, opaque, 1);
        SilkMeshRendererConformance.Apply(renderer, 5, CreateShadow(0));
        _ = renderer.Render(color, depth);
        await Assert.That(renderer.ShadowMapCount).IsEqualTo(0);
        await Assert.That(ReadPixels(color).SequenceEqual(lit)).IsTrue();
    }

    internal static async Task BlendedOverflowOrEqualDepthRefusesAndAllowsCorrection(
        ISilkGraphicsDevice device, bool equalDepth)
    {
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Size, Size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        using var initial = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 1,
        [
            .. CreateShadowLightFrame(), .. CreateReceiver(),
            .. CreateCasterMaterial([(SilkMaterialParameter.Opacity, 0.5f)]),
            .. CreateLayeredCaster(1, false), .. CreateTransmissionShadow(),
        ], 5);
        _ = renderer.ApplyAndRender(initial, color, depth);
        byte[] before = ReadPixels(color);
        ulong count = renderer.ShadowMapRenderCount;
        using var refused = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2,
            CreateLayeredCaster(equalDepth ? 2 : 5, false, equalDepth: equalDepth, topologyRevision: 2), 1);
        await Assert.That(() => renderer.ApplyAndRender(refused, color, depth))
            .Throws<InvalidDataException>()
            .WithMessageContaining(equalDepth ? "ShadowTransmissionEqualDepth" : "ShadowTransmissionLayerOverflow");
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(count);
        await Assert.That(ReadPixels(color).SequenceEqual(before)).IsTrue();
        _ = renderer.Render(color, depth);
        await Assert.That(ReadPixels(color).SequenceEqual(before)).IsTrue();
        using var corrected = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2,
            CreateLayeredCaster(2, false, topologyRevision: 2), 1);
        _ = renderer.ApplyAndRender(corrected, color, depth);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2ul);
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(count + 1);
        await Assert.That(ReadPixels(color)[Offset(ShadowedX)]).IsLessThan(before[Offset(ShadowedX)]);
    }

    internal static async Task BlendedBudgetRefusalPreservesPixelsAndRetries(
        ISilkGraphicsDevice device, SilkGpuTextureBudget budget)
    {
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Size, Size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        using var initial = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 1,
        [
            .. CreateShadowLightFrame(), .. CreateReceiver(),
            .. CreateCasterMaterial([(SilkMaterialParameter.Opacity, 0.5f)]),
            .. CreateLayeredCaster(2, false), .. CreateTransmissionShadow(),
        ], 5);
        _ = renderer.ApplyAndRender(initial, color, depth);
        byte[] before = ReadPixels(color);
        ulong rendered = renderer.ShadowMapRenderCount;
        ulong reserved = budget.Usage.ReservedBytes;
        using var changed = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2,
            CreateCasterMaterial([(SilkMaterialParameter.Opacity, 0.25f)]), 1);
        using (IDisposable pressure = budget.Reserve(new SilkTextureDescriptor(
            checked((uint)((budget.MaximumBytes - reserved) / 4)), 1,
            SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.Sampled), 1))
        {
            await Assert.That(() => renderer.ApplyAndRender(changed, color, depth))
                .Throws<SilkGpuTextureBudgetExceededException>();
            await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
            await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(rendered);
            await Assert.That(ReadPixels(color).SequenceEqual(before)).IsTrue();
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(reserved);
        _ = renderer.ApplyAndRender(changed, color, depth);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2ul);
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(rendered + 1);
        await Assert.That(ReadPixels(color)[Offset(ShadowedX)]).IsGreaterThan(before[Offset(ShadowedX)]);
    }

    internal static async Task BlendedTextureChangesMatchAnalyticPixels(
        ISilkGraphicsDevice device, SilkGraphicsBackend backend, bool streaming)
    {
        using var renderer = new SilkMeshRenderer(device,
            backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV,
            (asset, _) => new SilkDecodedImage(1, 1,
                asset == "A" ? [64, 64, 64, 255] : [192, 192, 192, 255]),
            textureResidencyOptions: new SilkTextureResidencyOptions(1_048_576, 1_048_576, streaming));
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Size, Size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        SilkMeshRendererConformance.Apply(renderer, 1, CreateShadowLightFrame(), CreateReceiver(),
            SilkMaterialStreamingTests.Material('A', false, scalarParameter: SilkMaterialParameter.Opacity),
            CreateTexturedCaster());
        _ = renderer.Render(color, depth);
        byte[] lit = ReadPixels(color);
        SilkMeshRendererConformance.Apply(renderer, 2, CreateTransmissionShadow());
        _ = renderer.Render(color, depth, SilkMeshRenderOptions.Default with { UseSceneMaterials = false });
        byte[] opaque = ReadPixels(color);
        _ = renderer.Render(color, depth);
        await AssertTransmissionPixel(ReadPixels(color), lit, opaque, 1 - (64.0 / 255));
        SilkMeshRendererConformance.Apply(renderer, 3,
            SilkMaterialStreamingTests.Material('A', false,
                scalarParameter: SilkMaterialParameter.Opacity, assetName: 'B'));
        _ = renderer.Render(color, depth);
        await AssertTransmissionPixel(ReadPixels(color), lit, opaque, 1 - (192.0 / 255));
        SilkMeshRendererConformance.Apply(renderer, 4,
            CreateLightLink(1, (Caster, -1, (UInt128)1, (UInt128)0)));
        _ = renderer.Render(color, depth);
        await AssertTransmissionPixel(ReadPixels(color), lit, opaque, 1);
        await Assert.That(HasCasterDiagnostic(renderer)).IsFalse();
    }

    internal static async Task MixedConstantAndTexturedLayersMatchAnalyticPixels(
        ISilkGraphicsDevice device, SilkGraphicsBackend backend, bool streaming, bool constantBehindTexture)
    {
        using var renderer = new SilkMeshRenderer(device,
            backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV,
            (_, _) => new SilkDecodedImage(1, 1, [64, 64, 64, 255]),
            textureResidencyOptions: new SilkTextureResidencyOptions(1_048_576, 1_048_576, streaming));
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Size, Size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        SilkMeshRendererConformance.Apply(renderer, 1, CreateShadowLightFrame(), CreateReceiver(),
            CreateCasterMaterial([(SilkMaterialParameter.Opacity, 0.5f)]),
            CreateLayeredCaster(1, false, firstDepth: constantBehindTexture ? 0.95f : 0.65f),
            SilkMaterialStreamingTests.Material('A', false, scalarParameter: SilkMaterialParameter.Opacity),
            CreateTexturedCaster());
        _ = renderer.Render(color, depth);
        byte[] lit = ReadPixels(color);
        SilkMeshRendererConformance.Apply(renderer, 2, CreateTransmissionShadow());
        _ = renderer.Render(color, depth, SilkMeshRenderOptions.Default with { UseSceneMaterials = false });
        byte[] opaque = ReadPixels(color);
        await Assert.That(lit[Offset(ShadowedX)] - opaque[Offset(ShadowedX)]).IsGreaterThan(50);

        _ = renderer.Render(color, depth);
        byte[] combined = ReadPixels(color);
        await AssertTransmissionPixel(combined, lit, opaque, 0.5 * (1 - (64.0 / 255)));
        await AssertTransmissionPixel(combined, lit, opaque, 1, LitX);
        await Assert.That(HasCasterDiagnostic(renderer)).IsFalse();
        ulong rendered = renderer.ShadowMapRenderCount;
        _ = renderer.Render(color, depth);
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(rendered);
        await Assert.That(ReadPixels(color).SequenceEqual(combined)).IsTrue();

        SilkMeshRendererConformance.Apply(renderer, 3,
            CreateLightLink(1, (Caster, -1, (UInt128)1, (UInt128)0)));
        _ = renderer.Render(color, depth);
        await AssertTransmissionPixel(ReadPixels(color), lit, opaque, 0.5);
        SilkMeshRendererConformance.Apply(renderer, 4,
            CreateLightLink(1, ("/World/Layers", -1, (UInt128)1, (UInt128)0)));
        _ = renderer.Render(color, depth);
        await AssertTransmissionPixel(ReadPixels(color), lit, opaque, 1 - (64.0 / 255));
    }

    internal static async Task OpaqueAndCutoutBlockersComposeWithBlendedLayers(ISilkGraphicsDevice device)
    {
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Size, Size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        SilkMeshRendererConformance.Apply(renderer, 1, CreateShadowLightFrame(), CreateReceiver(),
            CreateCasterMaterial([(SilkMaterialParameter.Opacity, 0.5f)]),
            CreateLayeredCaster(2, false), CreateCaster(0));
        _ = renderer.Render(color, depth);
        byte[] lit = ReadPixels(color);
        SilkMeshRendererConformance.Apply(renderer, 2, CreateTransmissionShadow());
        _ = renderer.Render(color, depth);
        byte[] opaque = ReadPixels(color);
        await AssertShadowed(opaque, ShadowedX, "opaque blocker in front of blended layers");
        // The extra material belongs only to mesh 2, while the two-layer mesh retains the blended material.
        byte[] cutout = CreateCasterMaterial(
            [(SilkMaterialParameter.Opacity, 0.25f), (SilkMaterialParameter.OpacityThreshold, 0.5f)]);
        byte[] invisibleMaterial = ReplaceMaterialPath(cutout, CutoutMaterial, "/World/Materials/Invisible");
        byte[] cutoutCaster = VolumeCommandAuthoring.WithMaterialBinding(
            CreateCaster(0), "/World/Materials/Invisible");
        SilkMeshRendererConformance.Apply(renderer, 3, invisibleMaterial, cutoutCaster);
        _ = renderer.Render(color, depth);
        byte[] cutoutPixels = ReadPixels(color);
        await AssertTransmissionPixel(cutoutPixels, lit, opaque, 0.25);
        SilkMeshRendererConformance.Apply(renderer, 4, CreateLightLink(1,
            ("/World/Layers", -1, (UInt128)1, (UInt128)0)));
        _ = renderer.Render(color, depth);
        await AssertLit(ReadPixels(color), ShadowedX, "cutout hole and linked-out blended layers");
    }

    private static byte[] ReplaceMaterialPath(byte[] material, string oldPath, string newPath)
    {
        int oldLength = System.Text.Encoding.UTF8.GetByteCount(oldPath);
        byte[] replacement = System.Text.Encoding.UTF8.GetBytes(newPath);
        byte[] result = new byte[material.Length - oldLength + replacement.Length];
        material.AsSpan(0, 32).CopyTo(result);
        replacement.CopyTo(result.AsSpan(32));
        material.AsSpan(32 + oldLength).CopyTo(result.AsSpan(32 + replacement.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)result.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(8), ComputeStableHash(newPath));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16), (uint)replacement.Length);
        return result;
    }

    private static byte[] CreateLayeredCaster(
        int layers, bool behindReceiver, double x = 0, bool equalDepth = false, ulong topologyRevision = 1,
        float firstDepth = 0.65f)
    {
        List<float> positions = [];
        List<uint> indices = [];
        for (int layer = 0; layer < layers; layer++)
        {
            float z = behindReceiver && layer == layers - 1 ? -1.5f : firstDepth + (equalDepth ? 0 : layer * 0.1f);
            float centerX = LightTiltX * (z - CasterDepth);
            uint first = (uint)(positions.Count / 3);
            positions.AddRange([
                centerX - 0.25f, -0.25f, z, centerX + 0.25f, -0.25f, z,
                centerX + 0.25f, 0.25f, z, centerX - 0.25f, 0.25f, z]);
            indices.AddRange([first, first + 2, first + 1, first, first + 3, first + 2]);
        }
        return BindCutoutMaterial(SilkMeshRendererConformance.CreateMeshCommand(
            3, "/World/Layers", [.. positions], [.. indices], x, 0, [1, 1, 1, 1], topologyRevision));
    }

    private static byte[] CreateTransmissionShadow()
    {
        byte[] command = CreateShadow(1, LightTiltX, 0,
            new Vector3(-2, -1, -1.6f), new Vector3(1, 1, 1.3f), 1.5);
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(32), 256);
        return command;
    }

    private static async Task AssertTransmissionPixel(
        byte[] actual, byte[] lit, byte[] opaque, double transmission, int x = ShadowedX)
    {
        for (int channel = 0; channel < 3; channel++)
        {
            int offset = Offset(x) + channel;
            double expected = opaque[offset] + (transmission * (lit[offset] - opaque[offset]));
            await Assert.That(Math.Abs(actual[offset] - expected)).IsLessThanOrEqualTo(2.0);
        }
    }
}

[NotInParallel]
public sealed class SilkShadowTransmissionConformanceTests
{
    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task TextureBudgetRefusalPreservesTransmissionAndRetriesTheSamePage(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(67_108_864);
        budget.ConfigureDevice(device);
        await SilkShadowConformance.BlendedBudgetRefusalPreservesPixelsAndRetries(device, budget);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, 1, false)]
    [Arguments(SilkGraphicsBackend.D3D12, 2, false)]
    [Arguments(SilkGraphicsBackend.D3D12, 4, false)]
    [Arguments(SilkGraphicsBackend.D3D12, 1, true)]
    [Arguments(SilkGraphicsBackend.D3D12, 2, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, 1, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, 2, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, 4, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, 1, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, 2, true)]
    public async Task SingleAndMultipleLayersRespectReceiverDepthAndLifecycle(
        SilkGraphicsBackend backend, int layers, bool behindReceiver)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(67_108_864);
        budget.ConfigureDevice(device);
        await SilkShadowConformance.BlendedLayersMatchAnalyticPixels(device, layers, behindReceiver);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task OverflowAndEqualDepthRefusalPreservePublishedPixelsAndAllowCorrection(
        SilkGraphicsBackend backend, bool equalDepth)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(67_108_864);
        budget.ConfigureDevice(device);
        await SilkShadowConformance.BlendedOverflowOrEqualDepthRefusesAndAllowsCorrection(device, equalDepth);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task OpacityTextureEditsAndShadowLinksMatchAnalyticPixels(
        SilkGraphicsBackend backend, bool streaming)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(67_108_864);
        budget.ConfigureDevice(device);
        await SilkShadowConformance.BlendedTextureChangesMatchAnalyticPixels(device, backend, streaming);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false, false)]
    [Arguments(SilkGraphicsBackend.D3D12, false, true)]
    [Arguments(SilkGraphicsBackend.D3D12, true, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, false, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true, true)]
    public async Task ConstantAndTexturedLayersComposeInEitherDepthOrder(
        SilkGraphicsBackend backend, bool streaming, bool constantBehindTexture)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(67_108_864);
        budget.ConfigureDevice(device);
        await SilkShadowConformance.MixedConstantAndTexturedLayersMatchAnalyticPixels(
            device, backend, streaming, constantBehindTexture);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task OpaqueAndCutoutCastersComposeWithTransmission(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(67_108_864);
        budget.ConfigureDevice(device);
        await SilkShadowConformance.OpaqueAndCutoutBlockersComposeWithBlendedLayers(device);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }
}
