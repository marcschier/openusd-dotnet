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
    public async Task MaterialLayoutKeepsSceneParametersAndValidatesItsSlots()
    {
        SilkBindingLayoutDescriptor material = SilkBindingLayoutDescriptor.ForMaterial(
        [
            new SilkBindingSlot(
                0, 1, SilkBindingKind.SampledTexture, 0, SilkShaderStageVisibility.Fragment),
            new SilkBindingSlot(
                0, 2, SilkBindingKind.Sampler, 0, SilkShaderStageVisibility.Fragment),
            new SilkBindingSlot(
                0, 3, SilkBindingKind.UniformBuffer, 32, SilkShaderStageVisibility.Fragment),
        ]);
        material.Validate();

        // SceneParameters must stay exactly where every existing pipeline expects it,
        // so a material pipeline is additive rather than a different contract.
        await Assert.That(material.Set).IsEqualTo(0u);
        await Assert.That(material.Binding).IsEqualTo(0u);
        await Assert.That(material.UniformByteSize).IsEqualTo(80u);
        await Assert.That(material.MaterialSlots.Count).IsEqualTo(6);

        // The mesh layout includes the always-on instance, surface, and frame buffers.
        await Assert.That(SilkBindingLayoutDescriptor.SceneParameters.MaterialSlots.Count)
            .IsEqualTo(3);
    }

    [Test]
    public async Task MaterialLayoutRejectsCollisionsAndMalformedSlots()
    {
        // Colliding slots would silently overwrite one another in a descriptor set.
        await Assert.That(() => SilkBindingLayoutDescriptor.ForMaterial(
        [
            new SilkBindingSlot(
                0, 1, SilkBindingKind.SampledTexture, 0, SilkShaderStageVisibility.Fragment),
            new SilkBindingSlot(
                0, 1, SilkBindingKind.Sampler, 0, SilkShaderStageVisibility.Fragment),
        ]).Validate()).Throws<ArgumentException>();

        // Set 0 binding 0 is SceneParameters and cannot be reused.
        await Assert.That(() => SilkBindingLayoutDescriptor.ForMaterial(
        [
            new SilkBindingSlot(
                0, 0, SilkBindingKind.SampledTexture, 0, SilkShaderStageVisibility.Fragment),
        ]).Validate()).Throws<ArgumentException>();

        // A texture slot has no uniform size, and a uniform slot must have one.
        await Assert.That(() => SilkBindingLayoutDescriptor.ForMaterial(
        [
            new SilkBindingSlot(
                0, 1, SilkBindingKind.SampledTexture, 16, SilkShaderStageVisibility.Fragment),
        ]).Validate()).Throws<ArgumentException>();
        await Assert.That(() => SilkBindingLayoutDescriptor.ForMaterial(
        [
            new SilkBindingSlot(
                0, 1, SilkBindingKind.UniformBuffer, 0, SilkShaderStageVisibility.Fragment),
        ]).Validate()).Throws<ArgumentOutOfRangeException>();

        // A slot no stage can see is a silently dead binding.
        await Assert.That(() => SilkBindingLayoutDescriptor.ForMaterial(
        [
            new SilkBindingSlot(0, 1, SilkBindingKind.Sampler, 0, 0),
        ]).Validate()).Throws<ArgumentException>();

        // No backend binds a second set, so describing one must fail rather than
        // silently produce a binding nothing can reach.
        await Assert.That(() => SilkBindingLayoutDescriptor.ForMaterial(
        [
            new SilkBindingSlot(
                1, 0, SilkBindingKind.Sampler, 0, SilkShaderStageVisibility.Fragment),
        ]).Validate()).Throws<ArgumentException>();

        await Assert.That(() => SilkBindingLayoutDescriptor.ForMaterial([]))
            .Throws<ArgumentException>();
    }

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
        await Assert.That(dxilVertex.Code.Length).IsEqualTo(5920);
        await Assert.That(dxilFragment.Code.Length).IsEqualTo(7472);
        await Assert.That(spirvVertex.Code.Length).IsEqualTo(2476);
        await Assert.That(spirvFragment.Code.Length).IsEqualTo(7756);
        await Assert.That(dxilVertex.EntryPoint).IsEqualTo("vertexMain");
        await Assert.That(dxilFragment.EntryPoint).IsEqualTo("fragmentMain");
        await Assert.That(spirvVertex.EntryPoint).IsEqualTo("main");
        await Assert.That(spirvFragment.EntryPoint).IsEqualTo("main");
    }

    [Test]
    public async Task MeshPermutationIdsMatchCheckedArtifactNames()
    {
        Assembly assembly = typeof(SilkCheckedShaderAssets).Assembly;
        HashSet<string> resources = assembly.GetManifestResourceNames().ToHashSet(
            StringComparer.Ordinal);
        SilkShaderFeatures[] fragmentFeatures = GetManifestFragmentFeatures();
        SilkShaderFeatures[] vertexFeatures =
        [
            SilkShaderFeatures.None,
            SilkShaderFeatures.Uv,
            SilkShaderFeatures.Uv | SilkShaderFeatures.NormalMap
        ];

        await Assert.That(fragmentFeatures.Length).IsEqualTo(17);
        foreach (SilkShaderFeatures features in fragmentFeatures)
        {
            var permutation = new SilkShaderPermutationId(features);
            AssertEmbeddedResourceExists(
                resources,
                $"{permutation.MeshFragmentArtifactName}.dxil");
            AssertEmbeddedResourceExists(
                resources,
                $"{permutation.MeshFragmentArtifactName}.spv");
            AssertEmbeddedResourceExists(
                resources,
                $"{permutation.MeshFragmentArtifactName}.reflection.json");

            SilkShaderModuleDescriptor dxil =
                SilkCheckedShaderAssets.LoadMeshFragment(
                    SilkShaderBinaryFormat.Dxil,
                    permutation);
            SilkShaderModuleDescriptor spirv =
                SilkCheckedShaderAssets.LoadMeshFragment(
                    SilkShaderBinaryFormat.SpirV,
                    permutation);

            await Assert.That(dxil.Code.IsEmpty).IsFalse();
            await Assert.That(spirv.Code.IsEmpty).IsFalse();
            await Assert.That(dxil.EntryPoint)
                .IsEqualTo(GetExpectedEntryPoint("fragmentMain", features));
            await Assert.That(spirv.EntryPoint).IsEqualTo("main");
        }

        await Assert.That(vertexFeatures.Length).IsEqualTo(3);
        foreach (SilkShaderFeatures features in vertexFeatures)
        {
            var permutation = new SilkShaderPermutationId(features);
            AssertEmbeddedResourceExists(
                resources,
                $"{permutation.MeshVertexArtifactName}.dxil");
            AssertEmbeddedResourceExists(
                resources,
                $"{permutation.MeshVertexArtifactName}.spv");
            AssertEmbeddedResourceExists(
                resources,
                $"{permutation.MeshVertexArtifactName}.reflection.json");
            SilkShaderModuleDescriptor dxil =
                SilkCheckedShaderAssets.LoadMeshVertex(
                    SilkShaderBinaryFormat.Dxil,
                    permutation);

            await Assert.That(dxil.EntryPoint)
                .IsEqualTo(GetExpectedEntryPoint(
                    "vertexMain",
                    features & (SilkShaderFeatures.Uv | SilkShaderFeatures.NormalMap)));
        }
    }

    [Test]
    public async Task MeshPermutationIdsRejectManifestInvalidMapFeatures()
    {
        await Assert.That(() => new SilkShaderPermutationId(
            SilkShaderFeatures.BaseColorMap)).Throws<ArgumentException>();
        await Assert.That(() => new SilkShaderPermutationId(
            (SilkShaderFeatures)32)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task MissingMeshPermutationAssetThrowsInsteadOfFallingBack()
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => SilkCheckedShaderAssets.LoadMeshFragmentForTesting(
                SilkShaderBinaryFormat.Dxil,
                "mesh.fragment.uv+basecolor+missing"))!;

        await Assert.That(exception.Message)
            .Contains("mesh.fragment.uv+basecolor+missing.dxil");
    }

    [Test]
    public async Task PipelineCacheSharesLeasedPipelinesAndInvalidatesGenerations()
    {
        using var device = new CountingPipelineDevice();
        using var cache = new SilkGraphicsPipelineCache(
            device,
            SilkShaderBinaryFormat.Dxil);
        var permutation = new SilkShaderPermutationId(
            SilkShaderFeatures.Uv | SilkShaderFeatures.BaseColorMap);

        ISilkGraphicsPipeline first = cache.GetOrCreateMeshPipeline(
            permutation,
            SilkVertexLayoutDescriptor.PositionNormal,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureFormat.D32Float);
        ISilkGraphicsPipeline second = cache.GetOrCreateMeshPipeline(
            permutation,
            SilkVertexLayoutDescriptor.PositionNormal,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureFormat.D32Float);

        first.Dispose();
        await Assert.That(device.CreatedPipelineCount).IsEqualTo(1);
        await Assert.That(device.DisposedPipelineCount).IsEqualTo(0);

        second.Dispose();
        device.PickDeviceGenerationValue++;
        using ISilkGraphicsPipeline third = cache.GetOrCreateMeshPipeline(
            permutation,
            SilkVertexLayoutDescriptor.PositionNormal,
            SilkTextureFormat.Rgba8Unorm,
            SilkTextureFormat.D32Float);

        await Assert.That(device.CreatedPipelineCount).IsEqualTo(2);
        await Assert.That(device.DisposedPipelineCount).IsEqualTo(1);
        await Assert.That(device.CreatedFragmentShaders).IsEqualTo(2);
        await Assert.That(device.LastBindingLayout.MaterialSlots.Count).IsEqualTo(5);
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

    private static SilkShaderFeatures[] GetManifestFragmentFeatures()
    {
        SilkShaderFeatures[] maps =
        [
            SilkShaderFeatures.BaseColorMap,
            SilkShaderFeatures.NormalMap,
            SilkShaderFeatures.RoughnessMetallicMap,
            SilkShaderFeatures.EmissiveMap
        ];
        var features = new List<SilkShaderFeatures>
        {
            SilkShaderFeatures.None,
            SilkShaderFeatures.Uv
        };
        for (int mask = 1; mask < 16; mask++)
        {
            SilkShaderFeatures value = SilkShaderFeatures.Uv;
            for (int bit = 0; bit < maps.Length; bit++)
            {
                if ((mask & (1 << bit)) != 0)
                {
                    value |= maps[bit];
                }
            }
            features.Add(value);
        }
        return [.. features];
    }

    private static void AssertEmbeddedResourceExists(
        HashSet<string> resources,
        string artifactName)
    {
        string resourceName = "OpenUsd.Rendering.Silk.Shaders." + artifactName;
        if (!resources.Contains(resourceName))
        {
            Assert.Fail($"Missing embedded checked shader resource '{resourceName}'.");
        }
    }

    private static string GetExpectedEntryPoint(
        string baseEntryPoint,
        SilkShaderFeatures features)
    {
        if (features == SilkShaderFeatures.None)
        {
            return baseEntryPoint;
        }

        string suffix = new SilkShaderPermutationId(features)
            .MeshFragmentArtifactName["mesh.fragment.".Length..]
            .Replace('+', '_');
        return $"{baseEntryPoint}_{suffix}";
    }

    private sealed class CountingPipelineDevice : ISilkGraphicsDevice, ISilkPickingGraphicsDevice
    {
        public int CreatedPipelineCount { get; private set; }

        public int DisposedPipelineCount { get; private set; }

        public int CreatedFragmentShaders { get; private set; }

        public ulong PickDeviceGenerationValue { get; set; } = 1;

        public SilkBindingLayoutDescriptor LastBindingLayout { get; private set; }

        public SilkGraphicsBackend Backend => SilkGraphicsBackend.Vulkan;

        public SilkGraphicsCapabilities Capabilities { get; } =
            new("Counting", "1", SupportsCompute: false, IsSoftware: true);

        public ulong PickDeviceGeneration => PickDeviceGenerationValue;

        public void Dispose()
        {
        }

        public ISilkGraphicsBuffer CreateBuffer(nuint size, SilkBufferUsage usage) =>
            throw new NotSupportedException();

        public ISilkGraphicsTexture CreateTexture2D(
            uint width,
            uint height,
            SilkTextureFormat format = SilkTextureFormat.Rgba8Unorm) =>
            throw new NotSupportedException();

        public ISilkGraphicsTexture CreateTexture2D(SilkTextureDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsSampler CreateSampler(SilkSamplerDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsShaderModule CreateShaderModule(
            SilkShaderModuleDescriptor descriptor)
        {
            if (descriptor.Stage == SilkShaderStage.Fragment)
            {
                CreatedFragmentShaders++;
            }
            return new CountingShaderModule(descriptor);
        }

        public ISilkGraphicsBindingLayout CreateBindingLayout(
            SilkBindingLayoutDescriptor descriptor)
        {
            LastBindingLayout = descriptor;
            return new CountingBindingLayout(descriptor);
        }

        public ISilkGraphicsShaderProgram CreateShaderProgram(
            SilkShaderProgramDescriptor descriptor) =>
            new CountingShaderProgram(descriptor.BindingLayout);

        public ISilkGraphicsPipeline CreateGraphicsPipeline(
            SilkGraphicsPipelineDescriptor descriptor)
        {
            CreatedPipelineCount++;
            return new CountingPipeline(descriptor, () => DisposedPipelineCount++);
        }

        public ISilkComputeBindingLayout CreateComputeBindingLayout(
            SilkComputeBindingLayoutDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputeShaderProgram CreateComputeShaderProgram(
            SilkComputeShaderProgramDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkComputePipeline CreateComputePipeline(
            SilkComputePipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkGraphicsCommandList CreateCommandList() =>
            throw new NotSupportedException();

        public ISilkGraphicsSubmission Submit(ISilkGraphicsCommandList commandList) =>
            throw new NotSupportedException();

        public void WaitIdle()
        {
        }

        public ISilkPickGraphicsPipeline CreatePickGraphicsPipeline(
            SilkPickPipelineDescriptor descriptor) =>
            throw new NotSupportedException();

        public ISilkPickReadbackBuffer CreatePickReadbackBuffer() =>
            throw new NotSupportedException();
    }

    private sealed class CountingShaderModule(
        SilkShaderModuleDescriptor descriptor) : ISilkGraphicsShaderModule
    {
        public SilkShaderModuleDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class CountingBindingLayout(
        SilkBindingLayoutDescriptor descriptor) : ISilkGraphicsBindingLayout
    {
        public SilkBindingLayoutDescriptor Descriptor { get; } = descriptor;

        public void Dispose()
        {
        }
    }

    private sealed class CountingShaderProgram(
        ISilkGraphicsBindingLayout layout) : ISilkGraphicsShaderProgram
    {
        public ISilkGraphicsBindingLayout BindingLayout { get; } = layout;

        public void Dispose()
        {
        }
    }

    private sealed class CountingPipeline(
        SilkGraphicsPipelineDescriptor descriptor,
        Action disposed) : ISilkGraphicsPipeline
    {
        private readonly Action _disposed = disposed;

        public SilkGraphicsPipelineDescriptor Descriptor { get; } = descriptor;

        public void Dispose() => _disposed();
    }
}
