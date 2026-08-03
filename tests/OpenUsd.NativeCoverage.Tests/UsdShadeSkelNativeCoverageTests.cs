// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Shade;
using OpenUsd.Skel;

namespace OpenUsd.NativeCoverage.Tests;

public sealed class UsdShadeSkelNativeCoverageTests
{
    private const int BulkValueCount = 257;

    [Test]
    public async Task NodeGraphConnectionWalksBackToShaderSourceOnRealStage()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(NodeGraphConnectionWalksBackToShaderSourceOnRealStage));
        string path = Path.Combine(directory, "shade-nodegraph-behavior.usda");
        using UsdStage stage = UsdStage.Create(path);
        UsdShadeShader shader = stage.DefineShader("/World/Looks/Noise");
        shader.SourceId = "ND_noise3d_float";
        UsdShadeInput shaderInput = shader.CreateInputFloat("amplitude");
        shaderInput.Set(0.75F);
        UsdShadeOutput shaderOutput = shader.CreateOutput("rgb", UsdShadeValueType.Color3f);
        UsdShadeNodeGraph graph = stage.DefineNodeGraph("/World/Looks/Graph");
        UsdShadeInput graphInput = graph.CreateInput("color", UsdShadeValueType.Color3f);
        UsdShadeOutput graphOutput = graph.CreateOutput("surface", UsdShadeValueType.Color3f);

        graphInput.ConnectToSource(shaderOutput);
        graphOutput.ConnectToSource(shaderOutput);

        UsdShadeConnection source = graph.GetInput("color").GetConnectedSource();
        IReadOnlyList<UsdShadeConnection> sources = graph.GetInput("color").GetConnectedSources();

        await Assert.That(shader.SourceId).IsEqualTo("ND_noise3d_float");
        await Assert.That(shader.GetInput("amplitude").GetFloat()).IsEqualTo(0.75F);
        await Assert.That(graph.GetInputNames()).IsEquivalentTo(["color"]);
        await Assert.That(graph.GetOutputNames()).IsEquivalentTo(["surface"]);
        await Assert.That(source.SourcePrimPath).IsEqualTo(shader.Path);
        await Assert.That(source.SourceName).IsEqualTo("rgb");
        await Assert.That(source.SourceType).IsEqualTo(UsdShadeAttributeType.Output);
        await Assert.That(sources).IsEquivalentTo([source]);
        await Assert.That(graph.GetOutput("surface").GetConnectedSource().SourcePrimPath)
            .IsEqualTo(shader.Path);

        await Assert.That(UsdShadeNodeGraph.TryWrap(shader.Prim, out _)).IsFalse();
        await Assert.That(() => UsdShadeNodeGraph.Wrap(shader.Prim)).Throws<ArgumentException>();
    }

    [Test]
    public async Task MaterialBindingStrengthCollectionAndTerminalsResolveOnRealStage()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(MaterialBindingStrengthCollectionAndTerminalsResolveOnRealStage));
        string path = Path.Combine(directory, "shade-binding-behavior.usda");
        using UsdStage stage = UsdStage.Create(path);
        UsdPrim parent = stage.DefinePrim("/World/Geom", "Xform");
        UsdPrim child = stage.DefinePrim("/World/Geom/Cube", "Cube");
        UsdShadeMaterial parentMaterial = stage.DefineMaterial("/World/Looks/Parent");
        UsdShadeMaterial childMaterial = stage.DefineMaterial("/World/Looks/Child");
        UsdShadeShader surfaceShader = stage.DefineShader("/World/Looks/Parent/Surface");
        UsdShadeOutput surface = surfaceShader.CreateOutput("surface", UsdShadeValueType.Token);
        UsdShadeOutput surfaceTerminal = parentMaterial.CreateTerminalOutput(
            UsdShadeMaterialTerminal.Surface,
            "mtlx");
        UsdShadeOutput displacementTerminal = parentMaterial.CreateDisplacementOutput();
        UsdShadeOutput volumeTerminal = parentMaterial.CreateVolumeOutput();

        surfaceTerminal.ConnectToSource(surface);
        displacementTerminal.ConnectToSource(surface);
        volumeTerminal.ConnectToSource(surface);
        childMaterial.Bind(child, UsdShadeBindingStrength.WeakerThanDescendants);
        parentMaterial.Bind(parent, UsdShadeBindingStrength.StrongerThanDescendants);

        await Assert.That(stage.GetBoundMaterial(child).Path).IsEqualTo(parentMaterial.Path);
        await Assert.That(stage.GetDirectlyBoundMaterial(child).Path).IsEqualTo(childMaterial.Path);
        await Assert.That(surfaceTerminal.GetConnectedSource().SourcePrimPath).IsEqualTo(surfaceShader.Path);
        await Assert.That(displacementTerminal.GetConnectedSource().SourcePrimPath).IsEqualTo(surfaceShader.Path);
        await Assert.That(volumeTerminal.GetConnectedSource().SourcePrimPath).IsEqualTo(surfaceShader.Path);

        UsdPrim owner = stage.DefinePrim("/World/Collections", "Xform");
        UsdPrim member = stage.DefinePrim("/World/Collections/Member", "Cube");
        UsdShadeMaterial collectionMaterial = stage.DefineMaterial("/World/Looks/Collection");
        owner.SetRelationshipTargets("collection:lookMembers:includes", [member.Path]);
        collectionMaterial.BindCollection(
            owner,
            owner,
            "lookMembers",
            bindingName: "collectionLook",
            purpose: UsdShadeMaterialPurpose.Preview);

        await Assert.That(stage.GetBoundMaterial(member, UsdShadeMaterialPurpose.Preview).Path)
            .IsEqualTo(collectionMaterial.Path);
    }

    [Test]
    public async Task BlendShapeBulkArraysInbetweensAndBindingRoundTripOnRealStage()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(BlendShapeBulkArraysInbetweensAndBindingRoundTripOnRealStage));
        string path = Path.Combine(directory, "skel-blendshape-behavior.usda");
        using UsdStage stage = UsdStage.Create(path);
        UsdSkelSkeleton skeleton = stage.DefineSkeleton("/World/Skel/Skeleton");
        UsdSkelBlendShape smile = stage.DefineBlendShape("/World/Skel/Smile");
        UsdVec3f[] offsets = CreateVec3Values(0.1F);
        UsdVec3f[] normalOffsets = CreateVec3Values(10.1F);
        int[] pointIndices = Enumerable.Range(0, BulkValueCount)
            .Select(index => index * 2 + 2)
            .ToArray();

        smile.SetOffsets(offsets);
        smile.SetNormalOffsets(normalOffsets);
        smile.SetPointIndices(pointIndices);

        UsdVec3f[] readOffsets = smile.GetOffsets();
        UsdVec3f[] readNormalOffsets = smile.GetNormalOffsets();
        int[] readPointIndices = smile.GetPointIndices();
        int middle = BulkValueCount / 2;
        await Assert.That(readOffsets.Length).IsEqualTo(BulkValueCount);
        await Assert.That(readOffsets[0]).IsEqualTo(offsets[0]);
        await Assert.That(readOffsets[middle]).IsEqualTo(offsets[middle]);
        await Assert.That(readOffsets[^1]).IsEqualTo(offsets[^1]);
        await Assert.That(readNormalOffsets[middle]).IsEqualTo(normalOffsets[middle]);
        await Assert.That(readPointIndices[0]).IsEqualTo(2);
        await Assert.That(readPointIndices[middle]).IsEqualTo(258);
        await Assert.That(readPointIndices[^1]).IsEqualTo(514);
        await Assert.That(readPointIndices).IsEquivalentTo(pointIndices);

        UsdVec3f[] inbetweenOffsets = CreateVec3Values(20.1F);
        UsdVec3f[] inbetweenNormalOffsets = CreateVec3Values(30.1F);
        UsdPrim mesh = stage.DefinePrim("/World/Skel/Mesh", "Mesh");
        UsdSkelBinding binding = UsdSkelBinding.Apply(mesh);
        smile.SetInbetween("halfSmile", 0.5F, inbetweenOffsets, inbetweenNormalOffsets);
        binding.SkinningMethod = UsdSkelSkinningMethod.DualQuaternion;
        binding.SetBlendShapes(["smile"]);
        binding.SetBlendShapeTargets([smile]);
        UsdSkelBlendShapeInbetween inbetween = smile.GetInbetween("halfSmile");
        UsdSkelBlendShapeQuery query = UsdSkelBlendShapeQuery.Create(binding);

        await Assert.That(smile.GetInbetweenNames()).IsEquivalentTo(["halfSmile"]);
        await Assert.That(inbetween.Weight).IsEqualTo(0.5F);
        await Assert.That(inbetween.Offsets[middle]).IsEqualTo(inbetweenOffsets[middle]);
        await Assert.That(inbetween.NormalOffsets[middle]).IsEqualTo(inbetweenNormalOffsets[middle]);
        await Assert.That(binding.SkinningMethod).IsEqualTo(UsdSkelSkinningMethod.DualQuaternion);
        await Assert.That(query.BlendShapes).IsEquivalentTo(["smile"]);
        await Assert.That(query.Targets[0].Path).IsEqualTo(smile.Path);
        await Assert.That(query.GetInbetweens(0)[0].Offsets[^1]).IsEqualTo(inbetweenOffsets[^1]);

        await Assert.That(UsdSkelBlendShape.TryWrap(skeleton.Prim, out _)).IsFalse();
        await Assert.That(() => UsdSkelBlendShape.Wrap(skeleton.Prim)).Throws<ArgumentException>();
    }

    private static UsdVec3f[] CreateVec3Values(float seed)
    {
        UsdVec3f[] values = new UsdVec3f[BulkValueCount];
        for (int index = 0; index < values.Length; index++)
        {
            float value = seed + index;
            values[index] = new UsdVec3f(value, value + 0.25F, value + 0.5F);
        }

        return values;
    }
}
