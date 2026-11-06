// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

internal static partial class SilkShadowConformance
{
    internal static async Task BlendedLayersMatchAnalyticPixels(
        ISilkGraphicsDevice device, int layers, bool behindReceiver, bool pointLight = false)
    {
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Size, Size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        int shadowedX = pointLight ? 32 : ShadowedX;
        if (pointLight && behindReceiver)
        {
            await Assert.That(Math.Abs(PointCasterIntersectionX(shadowedX, -1.5f))).IsLessThan(0.25);
        }
        SilkMeshRendererConformance.Apply(renderer, 1, CreateTransmissionLightFrame(pointLight), CreateReceiver(),
            CreateCasterMaterial([(SilkMaterialParameter.Opacity, 0.5f)]),
            CreateLayeredCaster(layers, behindReceiver, pointLight: pointLight));
        _ = renderer.Render(color, depth);
        byte[] lit = ReadPixels(color);
        SilkMeshRendererConformance.Apply(renderer, 2, CreateTransmissionShadow(pointLight));
        _ = renderer.Render(color, depth, SilkMeshRenderOptions.Default with { UseSceneMaterials = false });
        byte[] opaque = ReadPixels(color);
        _ = renderer.Render(color, depth);
        byte[] actual = ReadPixels(color);
        int frontLayers = layers - (behindReceiver ? 1 : 0);
        if (frontLayers > 0)
        {
            await Assert.That(lit[Offset(shadowedX)] - opaque[Offset(shadowedX)])
                .IsGreaterThan(pointLight ? 30 : 50);
        }
        await AssertTransmissionPixel(actual, lit, opaque, Math.Pow(0.5, frontLayers), shadowedX);
        await Assert.That(HasCasterDiagnostic(renderer)).IsFalse();
        ulong rendered = renderer.ShadowMapRenderCount;
        _ = renderer.Render(color, depth);
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(rendered);
        await Assert.That(ReadPixels(color).SequenceEqual(actual)).IsTrue();

        SilkMeshRendererConformance.Apply(renderer, 3,
            CreateCasterMaterial([(SilkMaterialParameter.Opacity, 0.25f)]));
        _ = renderer.Render(color, depth);
        await AssertTransmissionPixel(ReadPixels(color), lit, opaque, Math.Pow(0.75, frontLayers), shadowedX);
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
        await AssertTransmissionPixel(ReadPixels(color), lit, opaque, 1, shadowedX);
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(rendered + 2);
        SilkMeshRendererConformance.Apply(renderer, 4,
            CreateLayeredCaster(layers, behindReceiver, x: 0.7, pointLight: pointLight));
        _ = renderer.Render(color, depth);
        await AssertTransmissionPixel(ReadPixels(color), lit, opaque, 1, shadowedX);
        SilkMeshRendererConformance.Apply(renderer, 5, CreateShadow(0));
        _ = renderer.Render(color, depth);
        await Assert.That(renderer.ShadowMapCount).IsEqualTo(0);
        await Assert.That(ReadPixels(color).SequenceEqual(lit)).IsTrue();
    }

    internal static async Task BlendedOverflowOrEqualDepthRefusesAndAllowsCorrection(
        ISilkGraphicsDevice device, bool equalDepth, bool pointLight = false)
    {
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Size, Size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        using var initial = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 1,
        [
            .. CreateTransmissionLightFrame(pointLight), .. CreateReceiver(),
            .. CreateCasterMaterial([(SilkMaterialParameter.Opacity, 0.5f)]),
            .. CreateLayeredCaster(1, false, pointLight: pointLight), .. CreateTransmissionShadow(pointLight),
        ], 5);
        _ = renderer.ApplyAndRender(initial, color, depth);
        byte[] before = ReadPixels(color);
        ulong count = renderer.ShadowMapRenderCount;
        using var refused = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2,
            CreateLayeredCaster(equalDepth ? 2 : 5, false, equalDepth: equalDepth,
                topologyRevision: 2, pointLight: pointLight), 1);
        await Assert.That(() => renderer.ApplyAndRender(refused, color, depth))
            .Throws<InvalidDataException>()
            .WithMessageContaining(equalDepth ? "ShadowTransmissionEqualDepth" : "ShadowTransmissionLayerOverflow");
        await Assert.That(renderer.Scene.Revision).IsEqualTo(1ul);
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(count);
        await Assert.That(ReadPixels(color).SequenceEqual(before)).IsTrue();
        _ = renderer.Render(color, depth);
        await Assert.That(ReadPixels(color).SequenceEqual(before)).IsTrue();
        using var corrected = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 2,
            CreateLayeredCaster(2, false, topologyRevision: 2, pointLight: pointLight), 1);
        _ = renderer.ApplyAndRender(corrected, color, depth);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2ul);
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(count + 1);
        int shadowedX = pointLight ? 32 : ShadowedX;
        await Assert.That(ReadPixels(color)[Offset(shadowedX)]).IsLessThan(before[Offset(shadowedX)]);
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

    internal static async Task OpaqueAndCutoutBlockersComposeWithBlendedLayers(
        ISilkGraphicsDevice device, bool pointLight = false)
    {
        using var renderer = new SilkMeshRenderer(device);
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Size, Size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        int shadowedX = pointLight ? 32 : ShadowedX;
        SilkMeshRendererConformance.Apply(renderer, 1, CreateTransmissionLightFrame(pointLight), CreateReceiver(),
            CreateCasterMaterial([(SilkMaterialParameter.Opacity, 0.5f)]),
            CreateLayeredCaster(2, false, pointLight: pointLight), CreateCaster(0));
        _ = renderer.Render(color, depth);
        byte[] lit = ReadPixels(color);
        SilkMeshRendererConformance.Apply(renderer, 2, CreateTransmissionShadow(pointLight));
        _ = renderer.Render(color, depth);
        byte[] opaque = ReadPixels(color);
        await AssertShadowed(opaque, shadowedX, "opaque blocker in front of blended layers");
        if (pointLight)
        {
            await Assert.That(lit[Offset(shadowedX)] - opaque[Offset(shadowedX)]).IsGreaterThan(30);
        }
        // The extra material belongs only to mesh 2, while the two-layer mesh retains the blended material.
        byte[] cutout = CreateCasterMaterial(
            [(SilkMaterialParameter.Opacity, 0.25f), (SilkMaterialParameter.OpacityThreshold, 0.5f)]);
        byte[] invisibleMaterial = ReplaceMaterialPath(cutout, CutoutMaterial, "/World/Materials/Invisible");
        byte[] cutoutCaster = VolumeCommandAuthoring.WithMaterialBinding(
            CreateCaster(0), "/World/Materials/Invisible");
        SilkMeshRendererConformance.Apply(renderer, 3, invisibleMaterial, cutoutCaster);
        _ = renderer.Render(color, depth);
        byte[] cutoutPixels = ReadPixels(color);
        await AssertTransmissionPixel(cutoutPixels, lit, opaque, 0.25, shadowedX);
        SilkMeshRendererConformance.Apply(renderer, 4, CreateLightLink(1,
            ("/World/Layers", -1, (UInt128)1, (UInt128)0)));
        _ = renderer.Render(color, depth);
        if (pointLight)
        {
            await AssertTransmissionPixel(ReadPixels(color), lit, opaque, 1, shadowedX);
        }
        else
        {
            await AssertLit(ReadPixels(color), shadowedX, "cutout hole and linked-out blended layers");
        }
    }

    internal static async Task PointConstantAndTexturedLayersMatchAnalyticPixels(
        ISilkGraphicsDevice device, SilkGraphicsBackend backend, bool constantBehindTexture)
    {
        using var renderer = new SilkMeshRenderer(device,
            backend == SilkGraphicsBackend.D3D12 ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV,
            (_, _) => new SilkDecodedImage(1, 1, [64, 64, 64, 255]),
            textureResidencyOptions: new SilkTextureResidencyOptions(1_048_576, 1_048_576, false));
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Size, Size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        const int center = 32;
        float constantDepth = constantBehindTexture ? 0.65f : 0.95f;
        await Assert.That(Math.Abs(PointCasterIntersectionX(center, constantDepth))).IsLessThan(0.25);
        await Assert.That(Math.Abs(PointCasterIntersectionX(center, CasterDepth))).IsLessThan(0.25);
        await Assert.That(Math.Abs(PointCasterIntersectionX(63, constantDepth))).IsGreaterThan(0.25);
        await Assert.That(Math.Abs(PointCasterIntersectionX(63, CasterDepth))).IsGreaterThan(0.25);
        SilkMeshRendererConformance.Apply(renderer, 1, CreateTransmissionLightFrame(true), CreateReceiver(),
            CreateCasterMaterial([(SilkMaterialParameter.Opacity, 0.5f)]),
            CreateLayeredCaster(1, false, firstDepth: constantDepth, pointLight: true),
            SilkMaterialStreamingTests.Material('A', false, scalarParameter: SilkMaterialParameter.Opacity),
            CreateTexturedCaster());
        _ = renderer.Render(color, depth);
        byte[] lit = ReadPixels(color);
        SilkMeshRendererConformance.Apply(renderer, 2, CreateTransmissionShadow(true));
        _ = renderer.Render(color, depth, SilkMeshRenderOptions.Default with { UseSceneMaterials = false });
        byte[] opaque = ReadPixels(color);
        await Assert.That(lit[Offset(center)] - opaque[Offset(center)]).IsGreaterThan(30);

        _ = renderer.Render(color, depth);
        byte[] combined = ReadPixels(color);
        await AssertTransmissionPixel(combined, lit, opaque, 0.5 * (1 - (64.0 / 255)), center);
        await AssertTransmissionPixel(combined, lit, opaque, 1, 63);
        await Assert.That(HasCasterDiagnostic(renderer)).IsFalse();
        ulong rendered = renderer.ShadowMapRenderCount;
        _ = renderer.Render(color, depth);
        await Assert.That(renderer.ShadowMapRenderCount).IsEqualTo(rendered);
        await Assert.That(ReadPixels(color).SequenceEqual(combined)).IsTrue();

        SilkMeshRendererConformance.Apply(renderer, 3,
            CreateLightLink(1, (Caster, -1, (UInt128)1, (UInt128)0)));
        _ = renderer.Render(color, depth);
        await AssertTransmissionPixel(ReadPixels(color), lit, opaque, 0.5, center);
        SilkMeshRendererConformance.Apply(renderer, 4,
            CreateLightLink(1, ("/World/Layers", -1, (UInt128)1, (UInt128)0)));
        _ = renderer.Render(color, depth);
        await AssertTransmissionPixel(ReadPixels(color), lit, opaque, 1 - (64.0 / 255), center);
    }

    internal static async Task PointLayersBeyondFarPlaneDoNotTriggerOverflow(
        ISilkGraphicsDevice device, SilkGraphicsBackend backend, bool textured)
    {
        SilkShaderBinaryFormat format = backend == SilkGraphicsBackend.D3D12
            ? SilkShaderBinaryFormat.Dxil : SilkShaderBinaryFormat.SpirV;
        using var renderer = new SilkMeshRenderer(
            device,
            format,
            (_, _) => new SilkDecodedImage(1, 1, [128, 128, 128, 255]),
            textureResidencyOptions: new SilkTextureResidencyOptions(1_048_576, 1_048_576, false));
        using ISilkGraphicsTexture color = device.CreateTexture2D(SilkTextureDescriptor.ColorTarget(Size, Size));
        using ISilkGraphicsTexture depth = device.CreateTexture2D(SilkTextureDescriptor.DepthTarget(Size, Size));
        var position = new Vector3(0, 0, 50);
        byte[] frame = CreateTransmissionLightFrame(true);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(580), 2500);
        WriteCubeMatrix(frame.AsSpan(584), Matrix4x4.CreateTranslation(position));
        byte[] layers = CreateLayeredCaster(5, false, pointLight: true, textured: textured);
        WriteCubeMatrix(layers.AsSpan(80), Matrix4x4.CreateTranslation(0, 0, 40));
        byte[] material = textured
            ? SilkMaterialStreamingTests.Material('A', false, scalarParameter: SilkMaterialParameter.Opacity)
            : CreateCasterMaterial([(SilkMaterialParameter.Opacity, 0.5f)]);
        SilkMeshRendererConformance.Apply(renderer, 1, frame, CreateReceiver(), material, layers);
        _ = renderer.Render(color, depth);
        SilkMeshData layerData = renderer.Scene.MeshesByPath[("/World/Layers", 0)];
        await Assert.That(SilkShadowMapCache.ClassifyOpacity(renderer.Scene, layerData))
            .IsEqualTo(SilkShadowOpacityCoverage.Transmissive);
        byte[] unshadowed = ReadPixels(color);
        await Assert.That(unshadowed[Offset(32)]).IsGreaterThan((byte)40);

        SilkMeshRendererConformance.Apply(renderer, 2,
            CreatePointCubeTable(position, resolution: 256, near: 1e-8, far: 4, depthBias: 0));
        _ = renderer.Render(color, depth);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(2ul);
        await Assert.That(renderer.ShadowMapCount).IsEqualTo(6);
        await Assert.That(HasCasterDiagnostic(renderer)).IsFalse();
        await Assert.That(Math.Abs(ReadPixels(color)[Offset(32)] - unshadowed[Offset(32)]))
            .IsLessThanOrEqualTo(2);

        SilkMeshRendererConformance.Apply(renderer, 3,
            CreatePointCubeTable(
                position, resolution: 256, near: 1e-8, far: 4, depthBias: 0, otherFaceFar: 100));
        _ = renderer.Render(color, depth);
        byte[] outsideShortFace = ReadPixels(color);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(3ul);
        await Assert.That(HasCasterDiagnostic(renderer)).IsFalse();
        await Assert.That(Math.Abs(outsideShortFace[Offset(32)] - unshadowed[Offset(32)]))
            .IsLessThanOrEqualTo(2);

        using var overflow = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 4,
            CreatePointCubeTable(position, resolution: 256, near: 1e-8, far: 100, depthBias: 0), 1);
        await Assert.That(() => renderer.ApplyAndRender(overflow, color, depth))
            .Throws<InvalidDataException>()
            .WithMessageContaining("ShadowTransmissionLayerOverflow");
        await Assert.That(renderer.Scene.Revision).IsEqualTo(3ul);
        await Assert.That(ReadPixels(color).SequenceEqual(outsideShortFace)).IsTrue();

        using var corrected = new OpenUsdSilkPage(SilkCommandParser.PageAbiVersion, 4,
            CreatePointCubeTable(
                position, resolution: 256, near: 1e-8, far: 4, depthBias: 0, otherFaceFar: 100), 1);
        _ = renderer.ApplyAndRender(corrected, color, depth);
        await Assert.That(renderer.Scene.Revision).IsEqualTo(4ul);
        await Assert.That(ReadPixels(color).SequenceEqual(outsideShortFace)).IsTrue();
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
        float firstDepth = 0.65f, bool pointLight = false, bool textured = false)
    {
        List<float> positions = [];
        List<uint> indices = [];
        List<float> texCoords = [];
        for (int layer = 0; layer < layers; layer++)
        {
            float z = behindReceiver && layer == layers - 1 ? -1.5f : firstDepth + (equalDepth ? 0 : layer * 0.1f);
            float centerX = pointLight ? 0 : LightTiltX * (z - CasterDepth);
            uint first = (uint)(positions.Count / 3);
            positions.AddRange([
                centerX - 0.25f, -0.25f, z, centerX + 0.25f, -0.25f, z,
                centerX + 0.25f, 0.25f, z, centerX - 0.25f, 0.25f, z]);
            indices.AddRange([first, first + 2, first + 1, first, first + 3, first + 2]);
            if (textured)
            {
                texCoords.AddRange([0, 0, 1, 0, 1, 1, 0, 1]);
            }
        }
        byte[] mesh = SilkMeshRendererConformance.CreateMeshCommand(
            3, "/World/Layers", [.. positions], [.. indices], x, 0, [1, 1, 1, 1], topologyRevision,
            triangleSubprims: null, pointOrigins: null, cornerEdges: null,
            attributes: textured ? [("st", (uint)SilkAttributeSemantic.TexCoord, 2, [.. texCoords])] : null);
        return textured
            ? VolumeCommandAuthoring.WithMaterialBinding(mesh, "/A")
            : BindCutoutMaterial(mesh);
    }

    private static byte[] CreateTransmissionLightFrame(bool pointLight)
    {
        if (!pointLight)
        {
            return CreateShadowLightFrame();
        }

        byte[] frame = CreateShadowLightFrame(0, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(552), 2);
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(724), 0);
        WriteCubeMatrix(frame.AsSpan(584), Matrix4x4.CreateTranslation(0, 0, 1.4f));
        return frame;
    }

    private static double PointCasterIntersectionX(int pixelX, float casterDepth)
    {
        double receiverX = (((pixelX + 0.5) * 2) / Size) - 1;
        return receiverX * ((1.4 - casterDepth) / (1.4 - ReceiverDepth));
    }

    private static byte[] CreateTransmissionShadow(bool pointLight = false)
    {
        if (pointLight)
        {
            return CreatePointCubeTable(new Vector3(0, 0, 1.4f), resolution: 256);
        }

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
    public async Task PointLightLayersRespectReceiverDepthAndLifecycle(
        SilkGraphicsBackend backend, int layers, bool behindReceiver)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(268_435_456);
        budget.ConfigureDevice(device);
        await SilkShadowConformance.BlendedLayersMatchAnalyticPixels(device, layers, behindReceiver, pointLight: true);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task PointLightOverflowAndEqualDepthPreservePixelsAndAllowCorrection(
        SilkGraphicsBackend backend, bool equalDepth)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(268_435_456);
        budget.ConfigureDevice(device);
        await SilkShadowConformance.BlendedOverflowOrEqualDepthRefusesAndAllowsCorrection(
            device, equalDepth, pointLight: true);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task PointLightConstantAndTexturedLayersComposeInEitherDepthOrder(
        SilkGraphicsBackend backend, bool constantBehindTexture)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(268_435_456);
        budget.ConfigureDevice(device);
        await SilkShadowConformance.PointConstantAndTexturedLayersMatchAnalyticPixels(
            device, backend, constantBehindTexture);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12)]
    [Arguments(SilkGraphicsBackend.Vulkan)]
    public async Task PointLightOpaqueAndCutoutCastersComposeWithTransmission(SilkGraphicsBackend backend)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(268_435_456);
        budget.ConfigureDevice(device);
        await SilkShadowConformance.OpaqueAndCutoutBlockersComposeWithBlendedLayers(device, pointLight: true);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(SilkGraphicsBackend.D3D12, false)]
    [Arguments(SilkGraphicsBackend.D3D12, true)]
    [Arguments(SilkGraphicsBackend.Vulkan, false)]
    [Arguments(SilkGraphicsBackend.Vulkan, true)]
    public async Task PointLightLayersBeyondFarPlaneDoNotTriggerOverflow(
        SilkGraphicsBackend backend, bool textured)
    {
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuTextureBudget(268_435_456);
        budget.ConfigureDevice(device);
        await SilkShadowConformance.PointLayersBeyondFarPlaneDoNotTriggerOverflow(device, backend, textured);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }
}
