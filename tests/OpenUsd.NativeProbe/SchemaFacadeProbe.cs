// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;
using OpenUsd.Media;
using OpenUsd.Proc;
using OpenUsd.Render;
using OpenUsd.UI;
using OpenUsd.Vol;

namespace OpenUsd.NativeProbe;

internal static partial class Program
{
    private static void RunSchemaFacadeProbe(string directory)
    {
        UsdVolVolumeAndOpenVdbAssetRoundTripFieldRelationships(directory);
        UsdRenderSettingsProductsAndOrderedVarsRoundTripInOrder(directory);
        UsdMediaSpatialAudioRoundTripsPlaybackProperties(directory);
        UsdProcGenerativeProceduralRoundTripsProceduralSystem(directory);
        UsdUiBackdropAndNodeGraphApiRoundTripAndApplySchemas(directory);
        AppliedApiApplyAddsSchemaNameToPrim(directory);
        TryWrapReturnsFalseForWrongSchemaPrims(directory);
    }

    private static void UsdVolVolumeAndOpenVdbAssetRoundTripFieldRelationships(string directory)
    {
        using UsdStage stage = CreateSchemaStage(directory);
        UsdVolVolume volume = stage.DefineVolume("/World/Volume");
        UsdVolOpenVDBAsset density = stage.DefineOpenVDBAsset("/World/Fields/Density");
        UsdVolVolumeFieldAsset densityAsset = UsdVolVolumeFieldAsset.Wrap(density.Prim);

        densityAsset.FilePath = new UsdAssetPath("volumes/smoke.vdb");
        densityAsset.FieldName = "density";
        densityAsset.FieldIndex = 3;
        volume.SetField("density", UsdVolVolumeFieldBase.Wrap(density.Prim));

        IReadOnlyDictionary<string, string> fields = volume.GetFieldPaths();
        Require(
            fields.TryGetValue("density", out string? fieldPath) && fieldPath == density.Path,
            "UsdVol field relationship did not resolve to the OpenVDBAsset prim.");
        Require(volume.HasFieldRelationship("density"), "UsdVol field relationship was not authored.");
        Require(densityAsset.FilePath.Path == "volumes/smoke.vdb", "UsdVol filePath did not round-trip.");
        Require(densityAsset.FieldName == "density", "UsdVol fieldName did not round-trip.");
        Require(densityAsset.FieldIndex == 3, "UsdVol fieldIndex did not round-trip.");
    }

    private static void UsdRenderSettingsProductsAndOrderedVarsRoundTripInOrder(string directory)
    {
        using UsdStage stage = CreateSchemaStage(directory);
        UsdGeomCamera camera = stage.DefineCamera("/World/Camera");
        UsdRenderSettings settings = stage.DefineRenderSettings("/Render/Settings");
        UsdRenderProduct product = stage.DefineRenderProduct("/Render/Product");
        UsdRenderVar color = stage.DefineRenderVar("/Render/Vars/Color");
        UsdRenderVar depth = stage.DefineRenderVar("/Render/Vars/Depth");

        settings.SettingsBase.SetCamera(camera);
        settings.SettingsBase.SetResolution(1920, 1080);
        settings.SettingsBase.SetDataWindowNdc(0.125f, 0.25f, 0.875f, 1.0f);
        settings.SetProducts([product]);
        color.DataType = "color3f";
        color.SourceName = "Ci";
        depth.DataType = "float";
        depth.SourceName = "depth";
        product.SetOrderedVars([color, depth]);

        settings.SettingsBase.GetResolution(out int width, out int height);
        settings.SettingsBase.GetDataWindowNdc(out float minX, out float minY, out float maxX, out float maxY);

        RequireSequence(settings.SettingsBase.GetCameraTargets(), [camera.Path], "UsdRender camera relationship");
        Require(width == 1920 && height == 1080, "UsdRender resolution did not round-trip.");
        Require(
            minX == 0.125f && minY == 0.25f && maxX == 0.875f && maxY == 1.0f,
            "UsdRender dataWindowNDC did not round-trip.");
        RequireSequence(settings.Prim.GetRelationshipTargets("products"), [product.Path], "UsdRender products");
        RequireSequence(
            product.Prim.GetRelationshipTargets("orderedVars"),
            [color.Path, depth.Path],
            "UsdRender orderedVars");
        Require(color.DataType == "color3f", "UsdRenderVar color dataType did not round-trip.");
        Require(color.SourceName == "Ci", "UsdRenderVar color sourceName did not round-trip.");
        Require(depth.DataType == "float", "UsdRenderVar depth dataType did not round-trip.");
        Require(depth.SourceName == "depth", "UsdRenderVar depth sourceName did not round-trip.");
    }

    private static void UsdMediaSpatialAudioRoundTripsPlaybackProperties(string directory)
    {
        using UsdStage stage = CreateSchemaStage(directory);
        UsdMediaSpatialAudio audio = stage.DefineSpatialAudio("/World/Sound/Ambience");

        audio.FilePath = new UsdAssetPath("audio/ambience.wav");
        audio.AuralMode = "spatial";
        audio.PlaybackMode = "loopFromStartToEnd";
        audio.StartTime = 12.5;
        audio.EndTime = 48.25;

        Require(audio.FilePath.Path == "audio/ambience.wav", "UsdMedia filePath did not round-trip.");
        Require(audio.AuralMode == "spatial", "UsdMedia auralMode did not round-trip.");
        Require(audio.PlaybackMode == "loopFromStartToEnd", "UsdMedia playbackMode did not round-trip.");
        Require(audio.StartTime == 12.5, "UsdMedia startTime did not round-trip.");
        Require(audio.EndTime == 48.25, "UsdMedia endTime did not round-trip.");
    }

    private static void UsdProcGenerativeProceduralRoundTripsProceduralSystem(string directory)
    {
        using UsdStage stage = CreateSchemaStage(directory);
        UsdProcGenerativeProcedural procedural = stage.DefineGenerativeProcedural("/World/Procedural");

        procedural.ProceduralSystem = "openusd-dotnet-test";

        Require(
            procedural.ProceduralSystem == "openusd-dotnet-test",
            "UsdProc proceduralSystem did not round-trip.");
    }

    private static void UsdUiBackdropAndNodeGraphApiRoundTripAndApplySchemas(string directory)
    {
        using UsdStage stage = CreateSchemaStage(directory);
        UsdUIBackdrop backdrop = stage.DefineBackdrop("/World/UI/Backdrop");
        UsdPrim arbitraryPrim = stage.DefinePrim("/World/Node", "Xform");

        backdrop.Description = "Groups the procedural controls.";
        UsdUINodeGraphNode node = UsdUINodeGraphNode.Apply(arbitraryPrim);
        node.Position = new UsdVec2f(11.5f, 22.25f);
        node.NodeSize = new UsdVec2f(320.0f, 180.0f);
        node.DisplayColor = new UsdVec3f(0.25f, 0.5f, 0.75f);

        Require(backdrop.Description == "Groups the procedural controls.", "UsdUI description did not round-trip.");
        Require(
            arbitraryPrim.GetAppliedSchemas().Contains("NodeGraphNodeAPI", StringComparer.Ordinal),
            "UsdUI NodeGraphNodeAPI was not applied.");
        Require(node.Position == new UsdVec2f(11.5f, 22.25f), "UsdUI pos did not round-trip.");
        Require(node.NodeSize == new UsdVec2f(320.0f, 180.0f), "UsdUI size did not round-trip.");
        Require(node.DisplayColor == new UsdVec3f(0.25f, 0.5f, 0.75f), "UsdUI displayColor did not round-trip.");
    }

    private static void AppliedApiApplyAddsSchemaNameToPrim(string directory)
    {
        using UsdStage stage = CreateSchemaStage(directory);
        UsdPrim assetPrim = stage.DefinePrim("/World/Asset", "Xform");
        UsdPrim scenePrim = stage.DefinePrim("/World/Scene", "Xform");

        UsdMediaAssetPreviews.Apply(assetPrim);
        UsdUISceneGraphPrim.Apply(scenePrim);

        Require(
            assetPrim.GetAppliedSchemas().Contains("AssetPreviewsAPI", StringComparer.Ordinal),
            "UsdMedia AssetPreviewsAPI was not applied.");
        Require(
            scenePrim.GetAppliedSchemas().Contains("SceneGraphPrimAPI", StringComparer.Ordinal),
            "UsdUI SceneGraphPrimAPI was not applied.");
    }

    private static void TryWrapReturnsFalseForWrongSchemaPrims(string directory)
    {
        using UsdStage stage = CreateSchemaStage(directory);
        UsdPrim xform = stage.DefinePrim("/World/Wrong", "Xform");

        Require(!UsdVolVolume.TryWrap(xform, out _), "UsdVolVolume wrapped a wrong-type prim.");
        Require(!UsdRenderSettings.TryWrap(xform, out _), "UsdRenderSettings wrapped a wrong-type prim.");
        Require(!UsdMediaSpatialAudio.TryWrap(xform, out _), "UsdMediaSpatialAudio wrapped a wrong-type prim.");
        Require(
            !UsdProcGenerativeProcedural.TryWrap(xform, out _),
            "UsdProcGenerativeProcedural wrapped a wrong-type prim.");
        Require(!UsdUIBackdrop.TryWrap(xform, out _), "UsdUIBackdrop wrapped a wrong-type prim.");
        Require(!UsdUINodeGraphNode.TryWrap(xform, out _), "UsdUINodeGraphNode wrapped a prim without the API schema.");
    }

    private static UsdStage CreateSchemaStage(string directory)
    {
        string path = Path.Combine(directory, $"schema-{Guid.NewGuid():N}.usda");
        return UsdStage.Create(path);
    }

    private static void RequireSequence<T>(IEnumerable<T> actual, IEnumerable<T> expected, string label)
    {
        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidOperationException($"{label} did not round-trip in the expected order.");
        }
    }
}
