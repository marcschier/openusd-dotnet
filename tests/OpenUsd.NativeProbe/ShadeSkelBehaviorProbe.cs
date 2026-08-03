// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Shade;
using OpenUsd.Skel;

namespace OpenUsd.NativeProbe;

internal static partial class Program
{
    private static void RunShadeSkelBehaviorProbe(string directory)
    {
        RunNodeGraphConnectionProbe(directory);
        RunMaterialBindingProbe(directory);
        RunBlendShapeProbe(directory);
    }

    private static void RunNodeGraphConnectionProbe(string directory)
    {
        string path = Path.Combine(directory, "shade-nodegraph-behavior.usda");
        File.Delete(path);
        using UsdStage stage = UsdStage.Create(path);
        UsdShadeShader shader = stage.DefineShader("/World/Looks/Noise");
        UsdShadeOutput shaderOutput = shader.CreateOutput("rgb", UsdShadeValueType.Color3f);
        UsdShadeNodeGraph graph = stage.DefineNodeGraph("/World/Looks/Graph");
        UsdShadeInput graphInput = graph.CreateInput("color", UsdShadeValueType.Color3f);

        graphInput.ConnectToSource(shaderOutput);

        UsdShadeConnection source = graph.GetInput("color").GetConnectedSource();
        IReadOnlyList<UsdShadeConnection> sources = graph.GetInput("color").GetConnectedSources();
        RequireShadeSkel(
            source.SourcePrimPath == shader.Path &&
            source.SourceName == "rgb" &&
            source.SourceType == UsdShadeAttributeType.Output &&
            sources.Count == 1 &&
            sources[0] == source,
            "NodeGraph input connection did not walk back to the shader output.");

        bool wrongTypeRejected =
            !UsdShadeNodeGraph.TryWrap(shader.Prim, out _) &&
            Throws<ArgumentException>(() => UsdShadeNodeGraph.Wrap(shader.Prim));
        RequireShadeSkel(wrongTypeRejected, "UsdShadeNodeGraph.TryWrap accepted a shader prim.");
    }

    private static void RunMaterialBindingProbe(string directory)
    {
        string path = Path.Combine(directory, "shade-binding-behavior.usda");
        File.Delete(path);
        using UsdStage stage = UsdStage.Create(path);
        UsdPrim parent = stage.DefinePrim("/World/Geom", "Xform");
        UsdPrim child = stage.DefinePrim("/World/Geom/Cube", "Cube");
        UsdShadeMaterial parentMaterial = stage.DefineMaterial("/World/Looks/Parent");
        UsdShadeMaterial childMaterial = stage.DefineMaterial("/World/Looks/Child");
        childMaterial.Bind(child, UsdShadeBindingStrength.WeakerThanDescendants);
        parentMaterial.Bind(parent, UsdShadeBindingStrength.StrongerThanDescendants);

        RequireShadeSkel(
            stage.GetBoundMaterial(child).Path == parentMaterial.Path &&
            stage.GetDirectlyBoundMaterial(child).Path == childMaterial.Path,
            "Material binding strength did not resolve the stronger ancestor binding.");

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

        RequireShadeSkel(
            stage.GetBoundMaterial(member, UsdShadeMaterialPurpose.Preview).Path ==
                collectionMaterial.Path,
            "Collection material binding did not resolve for the collection member.");
    }

    private static void RunBlendShapeProbe(string directory)
    {
        string path = Path.Combine(directory, "skel-blendshape-behavior.usda");
        File.Delete(path);
        using UsdStage stage = UsdStage.Create(path);
        UsdSkelSkeleton skeleton = stage.DefineSkeleton("/World/Skel/Skeleton");
        UsdSkelBlendShape smile = stage.DefineBlendShape("/World/Skel/Smile");
        UsdVec3f[] offsets =
        [
            new(0.1F, 0.2F, 0.3F),
            new(1.1F, 1.2F, 1.3F),
            new(2.1F, 2.2F, 2.3F),
            new(3.1F, 3.2F, 3.3F),
            new(4.1F, 4.2F, 4.3F)
        ];
        int[] pointIndices = [2, 4, 8, 16, 32];

        smile.SetOffsets(offsets);
        smile.SetPointIndices(pointIndices);

        UsdVec3f[] readOffsets = smile.GetOffsets();
        int[] readPointIndices = smile.GetPointIndices();
        RequireShadeSkel(
            readOffsets.SequenceEqual(offsets) &&
            readOffsets[0] == offsets[0] &&
            readOffsets[2] == offsets[2] &&
            readOffsets[4] == offsets[4] &&
            readPointIndices.SequenceEqual(pointIndices) &&
            readPointIndices[0] == 2 &&
            readPointIndices[2] == 8 &&
            readPointIndices[4] == 32,
            "BlendShape offsets or point indices did not round-trip through bulk arrays.");

        UsdVec3f[] inbetweenOffsets =
        [
            new(0.5F, 0.0F, 0.0F),
            new(0.0F, 0.5F, 0.0F),
            new(0.0F, 0.0F, 0.5F)
        ];
        UsdPrim mesh = stage.DefinePrim("/World/Skel/Mesh", "Mesh");
        UsdSkelBinding binding = UsdSkelBinding.Apply(mesh);
        smile.SetInbetween("halfSmile", 0.5F, inbetweenOffsets);
        binding.SkinningMethod = UsdSkelSkinningMethod.DualQuaternion;
        binding.SetBlendShapes(["smile"]);
        binding.SetBlendShapeTargets([smile]);
        UsdSkelBlendShapeInbetween inbetween = smile.GetInbetween("halfSmile");
        UsdSkelBlendShapeQuery query = UsdSkelBlendShapeQuery.Create(binding);

        RequireShadeSkel(
            smile.GetInbetweenNames().SequenceEqual(["halfSmile"]) &&
            Math.Abs(inbetween.Weight - 0.5F) < 1e-6F &&
            inbetween.Offsets.SequenceEqual(inbetweenOffsets) &&
            inbetween.Offsets[1] == inbetweenOffsets[1] &&
            binding.SkinningMethod == UsdSkelSkinningMethod.DualQuaternion &&
            query.BlendShapes.SequenceEqual(["smile"]) &&
            query.Targets[0].Path == smile.Path,
            "BlendShape inbetween or binding surface did not round-trip.");

        bool wrongTypeRejected =
            !UsdSkelBlendShape.TryWrap(skeleton.Prim, out _) &&
            Throws<ArgumentException>(() => UsdSkelBlendShape.Wrap(skeleton.Prim));
        RequireShadeSkel(wrongTypeRejected, "UsdSkelBlendShape.TryWrap accepted a skeleton prim.");
    }

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static void RequireShadeSkel(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
