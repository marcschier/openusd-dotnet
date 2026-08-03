// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;
using OpenUsd.Media;
using OpenUsd.Proc;
using OpenUsd.Render;
using OpenUsd.UI;
using OpenUsd.Vol;

namespace OpenUsd.Tests;

public sealed class SchemaFacadeSurfaceTests
{
    [Test]
    public async Task UsdVolVolumeAndOpenVdbAssetRoundTripFieldRelationships()
    {
        using UsdStage stage = CreateStage();
        UsdVolVolume volume = stage.DefineVolume("/World/Volume");
        UsdVolOpenVDBAsset density = stage.DefineOpenVDBAsset("/World/Fields/Density");
        UsdVolVolumeFieldAsset densityAsset = UsdVolVolumeFieldAsset.Wrap(density.Prim);

        densityAsset.FilePath = new UsdAssetPath("volumes/smoke.vdb");
        densityAsset.FieldName = "density";
        densityAsset.FieldIndex = 3;
        volume.SetField("density", UsdVolVolumeFieldBase.Wrap(density.Prim));

        IReadOnlyDictionary<string, string> fields = volume.GetFieldPaths();
        await Assert.That(fields.TryGetValue("density", out string? fieldPath)).IsTrue();
        await Assert.That(fieldPath).IsEqualTo(density.Path);
        await Assert.That(volume.HasFieldRelationship("density")).IsTrue();
        await Assert.That(densityAsset.FilePath.Path).IsEqualTo("volumes/smoke.vdb");
        await Assert.That(densityAsset.FieldName).IsEqualTo("density");
        await Assert.That(densityAsset.FieldIndex).IsEqualTo(3);
    }

    [Test]
    public async Task UsdRenderSettingsProductsAndOrderedVarsRoundTripInOrder()
    {
        using UsdStage stage = CreateStage();
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

        await Assert.That(settings.SettingsBase.GetCameraTargets().SequenceEqual([camera.Path])).IsTrue();
        await Assert.That(width).IsEqualTo(1920);
        await Assert.That(height).IsEqualTo(1080);
        await Assert.That(minX).IsEqualTo(0.125f);
        await Assert.That(minY).IsEqualTo(0.25f);
        await Assert.That(maxX).IsEqualTo(0.875f);
        await Assert.That(maxY).IsEqualTo(1.0f);
        await Assert.That(settings.Prim.GetRelationshipTargets("products").SequenceEqual([product.Path])).IsTrue();
        await Assert.That(
            product.Prim.GetRelationshipTargets("orderedVars").SequenceEqual([color.Path, depth.Path]))
            .IsTrue();
        await Assert.That(color.DataType).IsEqualTo("color3f");
        await Assert.That(color.SourceName).IsEqualTo("Ci");
        await Assert.That(depth.DataType).IsEqualTo("float");
        await Assert.That(depth.SourceName).IsEqualTo("depth");
    }

    [Test]
    public async Task UsdMediaSpatialAudioRoundTripsPlaybackProperties()
    {
        using UsdStage stage = CreateStage();
        UsdMediaSpatialAudio audio = stage.DefineSpatialAudio("/World/Sound/Ambience");

        audio.FilePath = new UsdAssetPath("audio/ambience.wav");
        audio.AuralMode = "spatial";
        audio.PlaybackMode = "loopFromStartToEnd";
        audio.StartTime = 12.5;
        audio.EndTime = 48.25;

        await Assert.That(audio.FilePath.Path).IsEqualTo("audio/ambience.wav");
        await Assert.That(audio.AuralMode).IsEqualTo("spatial");
        await Assert.That(audio.PlaybackMode).IsEqualTo("loopFromStartToEnd");
        await Assert.That(audio.StartTime).IsEqualTo(12.5);
        await Assert.That(audio.EndTime).IsEqualTo(48.25);
    }

    [Test]
    public async Task UsdProcGenerativeProceduralRoundTripsProceduralSystem()
    {
        using UsdStage stage = CreateStage();
        UsdProcGenerativeProcedural procedural = stage.DefineGenerativeProcedural("/World/Procedural");

        procedural.ProceduralSystem = "openusd-dotnet-test";

        await Assert.That(procedural.ProceduralSystem).IsEqualTo("openusd-dotnet-test");
    }

    [Test]
    public async Task UsdUiBackdropAndNodeGraphApiRoundTripAndApplySchemas()
    {
        using UsdStage stage = CreateStage();
        UsdUIBackdrop backdrop = stage.DefineBackdrop("/World/UI/Backdrop");
        UsdPrim arbitraryPrim = stage.DefinePrim("/World/Node", "Xform");

        backdrop.Description = "Groups the procedural controls.";
        UsdUINodeGraphNode node = UsdUINodeGraphNode.Apply(arbitraryPrim);
        node.Position = new UsdVec2f(11.5f, 22.25f);
        node.NodeSize = new UsdVec2f(320.0f, 180.0f);
        node.DisplayColor = new UsdVec3f(0.25f, 0.5f, 0.75f);

        await Assert.That(backdrop.Description).IsEqualTo("Groups the procedural controls.");
        await Assert.That(arbitraryPrim.GetAppliedSchemas().Contains("NodeGraphNodeAPI")).IsTrue();
        await Assert.That(node.Position).IsEqualTo(new UsdVec2f(11.5f, 22.25f));
        await Assert.That(node.NodeSize).IsEqualTo(new UsdVec2f(320.0f, 180.0f));
        await Assert.That(node.DisplayColor).IsEqualTo(new UsdVec3f(0.25f, 0.5f, 0.75f));
    }

    [Test]
    public async Task AppliedApiApplyAddsSchemaNameToPrim()
    {
        using UsdStage stage = CreateStage();
        UsdPrim assetPrim = stage.DefinePrim("/World/Asset", "Xform");
        UsdPrim scenePrim = stage.DefinePrim("/World/Scene", "Xform");

        UsdMediaAssetPreviews.Apply(assetPrim);
        UsdUISceneGraphPrim.Apply(scenePrim);

        await Assert.That(assetPrim.GetAppliedSchemas().Contains("AssetPreviewsAPI")).IsTrue();
        await Assert.That(scenePrim.GetAppliedSchemas().Contains("SceneGraphPrimAPI")).IsTrue();
    }

    [Test]
    public async Task TryWrapReturnsFalseForWrongSchemaPrims()
    {
        using UsdStage stage = CreateStage();
        UsdPrim xform = stage.DefinePrim("/World/Wrong", "Xform");

        await Assert.That(UsdVolVolume.TryWrap(xform, out _)).IsFalse();
        await Assert.That(UsdRenderSettings.TryWrap(xform, out _)).IsFalse();
        await Assert.That(UsdMediaSpatialAudio.TryWrap(xform, out _)).IsFalse();
        await Assert.That(UsdProcGenerativeProcedural.TryWrap(xform, out _)).IsFalse();
        await Assert.That(UsdUIBackdrop.TryWrap(xform, out _)).IsFalse();
        await Assert.That(UsdUINodeGraphNode.TryWrap(xform, out _)).IsFalse();
    }

    private static UsdStage CreateStage()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "schema-roundtrip-stages");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"schema-{Guid.NewGuid():N}.usda");
        return UsdStage.Create(path);
    }
}

