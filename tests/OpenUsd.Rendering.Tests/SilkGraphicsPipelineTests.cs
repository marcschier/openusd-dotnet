// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkGraphicsPipelineTests
{
    [Test]
    public async Task CheckedMeshAssetsExposeValidatedReflectionAndBinaries()
    {
        SilkSceneParametersReflection reflection =
            SilkCheckedShaderAssets.SceneParameters;
        SilkShaderModuleDescriptor dxilVertex =
            SilkCheckedShaderAssets.LoadMeshVertex(SilkShaderBinaryFormat.Dxil);
        SilkShaderModuleDescriptor dxilFragment =
            SilkCheckedShaderAssets.LoadMeshFragment(SilkShaderBinaryFormat.Dxil);
        SilkShaderModuleDescriptor spirvVertex =
            SilkCheckedShaderAssets.LoadMeshVertex(SilkShaderBinaryFormat.SpirV);
        SilkShaderModuleDescriptor spirvFragment =
            SilkCheckedShaderAssets.LoadMeshFragment(SilkShaderBinaryFormat.SpirV);

        await Assert.That(reflection).IsEqualTo(
            new SilkSceneParametersReflection(true, 0, 64, 64, 16, 80));
        await Assert.That(dxilVertex.Code.Length).IsEqualTo(4180);
        await Assert.That(dxilFragment.Code.Length).IsEqualTo(3832);
        await Assert.That(spirvVertex.Code.Length).IsEqualTo(984);
        await Assert.That(spirvFragment.Code.Length).IsEqualTo(904);
        await Assert.That(dxilVertex.EntryPoint).IsEqualTo("vertexMain");
        await Assert.That(dxilFragment.EntryPoint).IsEqualTo("fragmentMain");
        await Assert.That(spirvVertex.EntryPoint).IsEqualTo("main");
        await Assert.That(spirvFragment.EntryPoint).IsEqualTo("main");
    }

    [Test]
    public async Task CheckedPickAssetsExposeValidatedReflectionAndHashes()
    {
        SilkPickParametersReflection reflection =
            SilkCheckedShaderAssets.PickParameters;
        SilkShaderModuleDescriptor dxilVertex =
            SilkCheckedShaderAssets.LoadPickVertex(
                SilkShaderBinaryFormat.Dxil);
        SilkShaderModuleDescriptor dxilFragment =
            SilkCheckedShaderAssets.LoadPickFragment(
                SilkShaderBinaryFormat.Dxil);
        SilkShaderModuleDescriptor spirvVertex =
            SilkCheckedShaderAssets.LoadPickVertex(
                SilkShaderBinaryFormat.SpirV);
        SilkShaderModuleDescriptor spirvFragment =
            SilkCheckedShaderAssets.LoadPickFragment(
                SilkShaderBinaryFormat.SpirV);
        SilkPickPipelineDescriptor descriptor =
            SilkPickPipelineDescriptor.CreateChecked(
                SilkShaderBinaryFormat.SpirV);

        descriptor.Validate();
        await Assert.That(reflection).IsEqualTo(
            new SilkPickParametersReflection(0, 1, 0, 16, 16, true));
        await Assert.That(dxilVertex.Code.Length).IsEqualTo(4008);
        await Assert.That(dxilFragment.Code.Length).IsEqualTo(3732);
        await Assert.That(spirvVertex.Code.Length).IsEqualTo(804);
        await Assert.That(spirvFragment.Code.Length).IsEqualTo(1152);
        await Assert.That(dxilVertex.EntryPoint)
            .IsEqualTo("pickVertexMain");
        await Assert.That(dxilFragment.EntryPoint)
            .IsEqualTo("pickFragmentMain");
        await Assert.That(spirvVertex.EntryPoint).IsEqualTo("main");
        await Assert.That(spirvFragment.EntryPoint).IsEqualTo("main");
        await Assert.That(descriptor.SampleCount).IsEqualTo(1U);
        await Assert.That(descriptor.PrimitiveTopology)
            .IsEqualTo(SilkPickPrimitiveTopology.TriangleList);
        await Assert.That(descriptor.CullMode)
            .IsEqualTo(SilkPickCullMode.None);
        await Assert.That(descriptor.BlendEnabled).IsFalse();
        await Assert.That(descriptor.DepthTestEnabled).IsTrue();
        await Assert.That(descriptor.DepthWriteEnabled).IsTrue();
        await Assert.That(descriptor.DepthCompare)
            .IsEqualTo(SilkPickDepthCompare.LessEqual);
        await Assert.That(GetSha256(dxilVertex.Code.Span)).IsEqualTo(
            "0796a8c15c4e8b46927a3d4dffec53246a341876fc5740249062ee444d232077");
        await Assert.That(GetSha256(dxilFragment.Code.Span)).IsEqualTo(
            "6f13c55f9a3a1f4cbf27118ad1cc5f59dfeccbee4f9721d30b965ec3ffdfbc7e");
        await Assert.That(GetSha256(spirvVertex.Code.Span)).IsEqualTo(
            "54e401ddf27cb2cc3107814c90c2b587eeca5e7dac19f545c644953f86b4211e");
        await Assert.That(GetSha256(spirvFragment.Code.Span)).IsEqualTo(
            "8b09f8ade5778dee88fe0ab4884384939cc4a7e1dc159a38309f7f09066615e1");
    }

    [Test]
    public async Task CheckedSelectionAssetsExposeExactBindingsAndHashes()
    {
        SilkSceneParametersReflection maskReflection =
            SilkCheckedShaderAssets.SelectionMaskSceneParameters;
        SilkSelectionOutlineReflection outlineReflection =
            SilkCheckedShaderAssets.SelectionOutline;
        SilkShaderModuleDescriptor dxilMaskVertex =
            SilkCheckedShaderAssets.LoadSelectionMaskVertex(
                SilkShaderBinaryFormat.Dxil);
        SilkShaderModuleDescriptor dxilMaskFragment =
            SilkCheckedShaderAssets.LoadSelectionMaskFragment(
                SilkShaderBinaryFormat.Dxil);
        SilkShaderModuleDescriptor spirvOutlineVertex =
            SilkCheckedShaderAssets.LoadSelectionOutlineVertex(
                SilkShaderBinaryFormat.SpirV);
        SilkShaderModuleDescriptor spirvOutlineFragment =
            SilkCheckedShaderAssets.LoadSelectionOutlineFragment(
                SilkShaderBinaryFormat.SpirV);
        SilkSelectionMaskPipelineDescriptor maskDescriptor =
            SilkSelectionMaskPipelineDescriptor.CreateChecked(
                SilkShaderBinaryFormat.Dxil);
        SilkSelectionOutlinePipelineDescriptor outlineDescriptor =
            SilkSelectionOutlinePipelineDescriptor.CreateChecked(
                SilkShaderBinaryFormat.SpirV);
        var uniformBytes =
            new byte[SilkSelectionOutlineUniformWriter.ByteSize];

        maskDescriptor.Validate();
        outlineDescriptor.Validate();
        SilkSelectionOutlineUniformWriter.Write(
            SilkSelectionOutlineSettings.Default,
            200,
            100,
            uniformBytes);

        await Assert.That(maskReflection)
            .IsEqualTo(SilkCheckedShaderAssets.SceneParameters);
        await Assert.That(outlineReflection.MaskTexture)
            .IsEqualTo(new SilkShaderResourceBindingReflection("t", 0, 0, 0, 0));
        await Assert.That(outlineReflection.VisibleDepthTexture)
            .IsEqualTo(new SilkShaderResourceBindingReflection("t", 1, 0, 0, 1));
        await Assert.That(outlineReflection.Sampler)
            .IsEqualTo(new SilkShaderResourceBindingReflection("s", 0, 0, 0, 2));
        await Assert.That(outlineReflection.Parameters)
            .IsEqualTo(new SilkShaderResourceBindingReflection("b", 0, 0, 0, 3));
        await Assert.That(outlineReflection.ColorOffset).IsEqualTo(0U);
        await Assert.That(outlineReflection.InverseViewportOffset).IsEqualTo(16U);
        await Assert.That(outlineReflection.WidthOffset).IsEqualTo(24U);
        await Assert.That(outlineReflection.DepthEpsilonOffset).IsEqualTo(28U);
        await Assert.That(outlineReflection.ParameterByteSize).IsEqualTo(32U);
        await Assert.That(outlineReflection.UsesVertexId).IsTrue();
        await Assert.That(maskDescriptor.DepthWriteEnabled).IsFalse();
        await Assert.That(maskDescriptor.DepthTestEnabled).IsTrue();
        await Assert.That(maskDescriptor.SampleCount).IsEqualTo(1U);
        await Assert.That(outlineDescriptor.BlendMode)
            .IsEqualTo(SilkSelectionOutlineBlendMode.StraightAlphaOver);
        await Assert.That(outlineDescriptor.DepthTestEnabled).IsFalse();
        await Assert.That(outlineDescriptor.Primitive)
            .IsEqualTo(SilkSelectionOutlinePrimitive.FullscreenTriangle);
        await Assert.That(dxilMaskVertex.Code.Length).IsEqualTo(4052);
        await Assert.That(dxilMaskFragment.Code.Length).IsEqualTo(2752);
        await Assert.That(spirvOutlineVertex.Code.Length).IsEqualTo(960);
        await Assert.That(spirvOutlineFragment.Code.Length).IsEqualTo(3316);
        await Assert.That(GetSha256(dxilMaskVertex.Code.Span)).IsEqualTo(
            "ac21f3441e4e80bbfea238591f17a43b14255e767cf07e26d2931ad536a6a84f");
        await Assert.That(GetSha256(dxilMaskFragment.Code.Span)).IsEqualTo(
            "682348314c6ef981564dbf7ac7ccd50814009a6f12b51223ac13c3efcdc9b00d");
        await Assert.That(GetSha256(spirvOutlineVertex.Code.Span)).IsEqualTo(
            "8f5f6854662fa8d097ddcb6d339f0a89b217fb62115f02af30c4e90284fc51cf");
        await Assert.That(GetSha256(spirvOutlineFragment.Code.Span)).IsEqualTo(
            "dc48741940caa7802a44b26d244333c0d01e56abafd6fe9f293d2c519993cad9");
        await Assert.That(ReadSingle(uniformBytes, 0)).IsEqualTo(1f);
        await Assert.That(ReadSingle(uniformBytes, 4)).IsEqualTo(0.005f);
        await Assert.That(ReadSingle(uniformBytes, 5)).IsEqualTo(0.01f);
        await Assert.That(ReadSingle(uniformBytes, 6)).IsEqualTo(2f);
        await Assert.That(ReadSingle(uniformBytes, 7))
            .IsEqualTo(SilkSelectionOutlineUniformWriter.DepthEpsilon);
    }

    [Test]
    public async Task CheckedDescriptorsRejectWrongLayouts()
    {
        var binding = SilkBindingLayoutDescriptor.SceneParameters with
        {
            UniformByteSize = 64
        };
        var vertexLayout = SilkVertexLayoutDescriptor.PositionNormal with
        {
            Stride = 12
        };

        await Assert.That(binding.Validate).Throws<ArgumentOutOfRangeException>();
        await Assert.That(vertexLayout.Validate).Throws<ArgumentException>();
    }

    [Test]
    public async Task CheckedComputeAssetsExposeValidatedReflectionAndBinaries()
    {
        SilkComputeReflection reflection = SilkCheckedShaderAssets.Compute;
        SilkShaderModuleDescriptor dxilFill =
            SilkCheckedShaderAssets.LoadComputeFill(SilkShaderBinaryFormat.Dxil);
        SilkShaderModuleDescriptor dxilScale =
            SilkCheckedShaderAssets.LoadComputeScale(SilkShaderBinaryFormat.Dxil);
        SilkShaderModuleDescriptor spirvFill =
            SilkCheckedShaderAssets.LoadComputeFill(SilkShaderBinaryFormat.SpirV);
        SilkShaderModuleDescriptor spirvScale =
            SilkCheckedShaderAssets.LoadComputeScale(SilkShaderBinaryFormat.SpirV);

        await Assert.That(reflection).IsEqualTo(
            new SilkComputeReflection(0, 0, 16, 0, 1, 8, 16, 64, 1, 1));
        await Assert.That(dxilFill.Code.Length).IsEqualTo(3608);
        await Assert.That(dxilScale.Code.Length).IsEqualTo(3776);
        await Assert.That(spirvFill.Code.Length).IsEqualTo(1260);
        await Assert.That(spirvScale.Code.Length).IsEqualTo(1244);
        await Assert.That(dxilFill.EntryPoint).IsEqualTo("fillMain");
        await Assert.That(dxilScale.EntryPoint).IsEqualTo("scaleMain");
        await Assert.That(spirvFill.EntryPoint).IsEqualTo("main");
        await Assert.That(spirvScale.EntryPoint).IsEqualTo("main");
        await Assert.That(spirvFill.Stage).IsEqualTo(SilkShaderStage.Compute);
    }

    [Test]
    public async Task ComputeParametersUseBackendSpecificConstantBufferLayout()
    {
        byte[] d3d = new SilkComputeParameters(67, 1.5f)
            .ToBytes(SilkGraphicsBackend.D3D12);
        byte[] vulkan = new SilkComputeParameters(67, 1.5f)
            .ToBytes(SilkGraphicsBackend.Vulkan);

        await Assert.That(d3d.Length).IsEqualTo(8);
        await Assert.That(vulkan.Length).IsEqualTo(16);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(d3d)).IsEqualTo(67U);
        await Assert.That(
            BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(d3d.AsSpan(4))))
            .IsEqualTo(1.5f);
        await Assert.That(vulkan.AsSpan(8).ToArray()).IsEquivalentTo(new byte[8]);
        await Assert.That(
            () => new SilkComputeParameters(0, 1).ToBytes(SilkGraphicsBackend.D3D12))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(
            () => new SilkComputeParameters(1, float.NaN)
                .ToBytes(SilkGraphicsBackend.D3D12))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CheckedComputeDescriptorsRejectWrongLayouts()
    {
        var layout = SilkComputeBindingLayoutDescriptor.Checked with
        {
            UniformBinding = 2
        };

        await Assert.That(layout.Validate).Throws<ArgumentException>();
    }

    [Test]
    public async Task CheckedComputeReflectionRejectsD3DRegisterMutation()
    {
        Assembly assembly = typeof(SilkCheckedShaderAssets).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(
            "OpenUsd.Rendering.Silk.Shaders.compute.fill.reflection.json")!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        string json = Encoding.UTF8.GetString(memory.ToArray()).Replace(
            "\"registerClass\": \"u\"",
            "\"registerClass\": \"t\"",
            StringComparison.Ordinal);
        MethodInfo parser = typeof(SilkCheckedShaderAssets).GetMethod(
            "ParseComputeReflectionForTesting",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
            () => parser.Invoke(null, [Encoding.UTF8.GetBytes(json)]));

        await Assert.That(exception.InnerException).IsTypeOf<InvalidDataException>();
        await Assert.That(exception.InnerException!.Message).Contains("u0, space 0");
    }

    private static string GetSha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static float ReadSingle(ReadOnlySpan<byte> value, int floatIndex) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(
            value.Slice(floatIndex * sizeof(float), sizeof(float))));
}
