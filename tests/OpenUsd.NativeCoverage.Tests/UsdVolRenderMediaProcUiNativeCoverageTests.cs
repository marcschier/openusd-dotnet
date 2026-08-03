// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;
using OpenUsd.Interop;
using OpenUsd.Media;
using OpenUsd.Proc;
using OpenUsd.Render;
using OpenUsd.UI;
using OpenUsd.Vol;

namespace OpenUsd.NativeCoverage.Tests;

public sealed class UsdVolRenderMediaProcUiNativeCoverageTests
{
    private const int BulkValueCount = 257;
    private const int MiddleIndex = BulkValueCount / 2;

    [Test]
    public async Task VolumeFieldsAndOpenVdbAssetRoundTripOnRealStage()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(VolumeFieldsAndOpenVdbAssetRoundTripOnRealStage));
        using UsdStage stage = CreateSchemaStage(directory);
        UsdVolVolume volume = stage.DefineVolume("/World/Volume");
        var assets = new UsdVolVolumeFieldAsset[BulkValueCount];

        for (int i = 0; i < BulkValueCount; i++)
        {
            UsdVolOpenVDBAsset asset = stage.DefineOpenVDBAsset($"/World/Fields/Density{i:000}");
            UsdVolVolumeFieldAsset fieldAsset = UsdVolVolumeFieldAsset.Wrap(asset.Prim);
            fieldAsset.FilePath = new UsdAssetPath($"volumes/smoke-{i:000}.vdb");
            fieldAsset.FieldName = $"density{i:000}";
            fieldAsset.FieldIndex = i;
            volume.SetField($"density{i:000}", UsdVolVolumeFieldBase.Wrap(asset.Prim));
            assets[i] = fieldAsset;
        }

        IReadOnlyDictionary<string, string> fields = volume.GetFieldPaths();

        await Assert.That(fields.Count).IsEqualTo(BulkValueCount);
        await Assert.That(fields["density000"]).IsEqualTo(assets[0].Path);
        await Assert.That(fields[$"density{MiddleIndex:000}"]).IsEqualTo(assets[MiddleIndex].Path);
        await Assert.That(fields[$"density{BulkValueCount - 1:000}"]).IsEqualTo(assets[BulkValueCount - 1].Path);
        await Assert.That(volume.HasFieldRelationship($"density{MiddleIndex:000}")).IsTrue();
        await Assert.That(assets[0].FilePath.Path).IsEqualTo("volumes/smoke-000.vdb");
        await Assert.That(assets[MiddleIndex].FieldName).IsEqualTo($"density{MiddleIndex:000}");
        await Assert.That(assets[BulkValueCount - 1].FieldIndex).IsEqualTo(BulkValueCount - 1);
    }

    [Test]
    public async Task RenderSettingsProductsAndOrderedVarsRoundTripInOrderOnRealStage()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(RenderSettingsProductsAndOrderedVarsRoundTripInOrderOnRealStage));
        using UsdStage stage = CreateSchemaStage(directory);
        UsdGeomCamera camera = stage.DefineCamera("/World/Camera");
        UsdRenderSettings settings = stage.DefineRenderSettings("/Render/Settings");
        var products = new UsdRenderProduct[BulkValueCount];
        var vars = new UsdRenderVar[BulkValueCount];

        for (int i = 0; i < BulkValueCount; i++)
        {
            products[i] = stage.DefineRenderProduct($"/Render/Products/Product{i:000}");
            products[i].ProductType = i % 2 == 0 ? "raster" : "deepRaster";
            products[i].ProductName = $"product-{i:000}.exr";
            vars[i] = stage.DefineRenderVar($"/Render/Vars/Var{i:000}");
            vars[i].DataType = i % 2 == 0 ? "color3f" : "float";
            vars[i].SourceName = $"source:{i:000}";
        }

        settings.SettingsBase.SetCamera(camera);
        settings.SettingsBase.SetResolution(1920, 1080);
        settings.SettingsBase.SetDataWindowNdc(0.125f, 0.25f, 0.875f, 1.0f);
        settings.SetProducts(products);
        products[MiddleIndex].SetOrderedVars(vars);

        settings.SettingsBase.GetResolution(out int width, out int height);
        settings.SettingsBase.GetDataWindowNdc(out float minX, out float minY, out float maxX, out float maxY);
        string[] productTargets = settings.Prim.GetRelationshipTargets("products");
        string[] orderedVars = products[MiddleIndex].Prim.GetRelationshipTargets("orderedVars");

        await Assert.That(settings.SettingsBase.GetCameraTargets()).IsEquivalentTo([camera.Path]);
        await Assert.That(width).IsEqualTo(1920);
        await Assert.That(height).IsEqualTo(1080);
        await Assert.That(minX).IsEqualTo(0.125f);
        await Assert.That(minY).IsEqualTo(0.25f);
        await Assert.That(maxX).IsEqualTo(0.875f);
        await Assert.That(maxY).IsEqualTo(1.0f);
        await Assert.That(productTargets.Length).IsEqualTo(BulkValueCount);
        await Assert.That(productTargets[0]).IsEqualTo(products[0].Path);
        await Assert.That(productTargets[MiddleIndex]).IsEqualTo(products[MiddleIndex].Path);
        await Assert.That(productTargets[BulkValueCount - 1]).IsEqualTo(products[BulkValueCount - 1].Path);
        await Assert.That(orderedVars.Length).IsEqualTo(BulkValueCount);
        await Assert.That(orderedVars[0]).IsEqualTo(vars[0].Path);
        await Assert.That(orderedVars[MiddleIndex]).IsEqualTo(vars[MiddleIndex].Path);
        await Assert.That(orderedVars[BulkValueCount - 1]).IsEqualTo(vars[BulkValueCount - 1].Path);
        await Assert.That(products[MiddleIndex].ProductName).IsEqualTo($"product-{MiddleIndex:000}.exr");
        await Assert.That(vars[0].DataType).IsEqualTo("color3f");
        await Assert.That(vars[MiddleIndex].SourceName).IsEqualTo($"source:{MiddleIndex:000}");
        await Assert.That(vars[BulkValueCount - 1].DataType).IsEqualTo("color3f");
    }

    [Test]
    public async Task MediaSpatialAudioRoundTripsPlaybackPropertiesOnRealStage()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(MediaSpatialAudioRoundTripsPlaybackPropertiesOnRealStage));
        using UsdStage stage = CreateSchemaStage(directory);
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
    public async Task ProceduralAndUiFacadesRoundTripAndApplySchemasOnRealStage()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(ProceduralAndUiFacadesRoundTripAndApplySchemasOnRealStage));
        using UsdStage stage = CreateSchemaStage(directory);
        UsdProcGenerativeProcedural procedural = stage.DefineGenerativeProcedural("/World/Procedural");
        UsdUIBackdrop backdrop = stage.DefineBackdrop("/World/UI/Backdrop");
        UsdPrim nodePrim = stage.DefinePrim("/World/Node", "Xform");
        UsdPrim assetPrim = stage.DefinePrim("/World/Asset", "Xform");
        UsdPrim scenePrim = stage.DefinePrim("/World/Scene", "Xform");

        procedural.ProceduralSystem = "openusd-dotnet-test";
        backdrop.Description = "Groups the procedural controls.";
        UsdUINodeGraphNode node = UsdUINodeGraphNode.Apply(nodePrim);
        node.Position = new UsdVec2f(11.5f, 22.25f);
        node.NodeSize = new UsdVec2f(320.0f, 180.0f);
        node.DisplayColor = new UsdVec3f(0.25f, 0.5f, 0.75f);
        UsdMediaAssetPreviews.Apply(assetPrim);
        UsdUISceneGraphPrim.Apply(scenePrim);

        await Assert.That(procedural.ProceduralSystem).IsEqualTo("openusd-dotnet-test");
        await Assert.That(backdrop.Description).IsEqualTo("Groups the procedural controls.");
        await Assert.That(nodePrim.GetAppliedSchemas().Contains("NodeGraphNodeAPI", StringComparer.Ordinal)).IsTrue();
        await Assert.That(assetPrim.GetAppliedSchemas().Contains("AssetPreviewsAPI", StringComparer.Ordinal)).IsTrue();
        await Assert.That(scenePrim.GetAppliedSchemas().Contains("SceneGraphPrimAPI", StringComparer.Ordinal)).IsTrue();
        await Assert.That(node.Position).IsEqualTo(new UsdVec2f(11.5f, 22.25f));
        await Assert.That(node.NodeSize).IsEqualTo(new UsdVec2f(320.0f, 180.0f));
        await Assert.That(node.DisplayColor).IsEqualTo(new UsdVec3f(0.25f, 0.5f, 0.75f));
    }

    [Test]
    public async Task WrongSchemaAndMissingPrimsFailPredictablyOnRealStage()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(WrongSchemaAndMissingPrimsFailPredictablyOnRealStage));
        using UsdStage stage = CreateSchemaStage(directory);
        UsdPrim xform = stage.DefinePrim("/World/Wrong", "Xform");
        UsdPrim missing = stage.GetPrim("/World/Missing");

        await Assert.That(UsdVolVolume.TryWrap(xform, out _)).IsFalse();
        await Assert.That(() => UsdVolVolume.Wrap(xform)).Throws<ArgumentException>();
        await Assert.That(() => UsdVolVolume.Wrap(missing)).Throws<OpenUsdNativeException>();
        await Assert.That(UsdRenderSettings.TryWrap(xform, out _)).IsFalse();
        await Assert.That(() => UsdRenderSettings.Wrap(xform)).Throws<ArgumentException>();
        await Assert.That(() => UsdRenderSettings.Wrap(missing)).Throws<OpenUsdNativeException>();
        await Assert.That(UsdMediaSpatialAudio.TryWrap(xform, out _)).IsFalse();
        await Assert.That(() => UsdMediaSpatialAudio.Wrap(xform)).Throws<ArgumentException>();
        await Assert.That(() => UsdMediaSpatialAudio.Wrap(missing)).Throws<OpenUsdNativeException>();
        await Assert.That(UsdProcGenerativeProcedural.TryWrap(xform, out _)).IsFalse();
        await Assert.That(() => UsdProcGenerativeProcedural.Wrap(xform)).Throws<ArgumentException>();
        await Assert.That(() => UsdProcGenerativeProcedural.Wrap(missing)).Throws<OpenUsdNativeException>();
        await Assert.That(UsdUIBackdrop.TryWrap(xform, out _)).IsFalse();
        await Assert.That(() => UsdUIBackdrop.Wrap(xform)).Throws<ArgumentException>();
        await Assert.That(() => UsdUIBackdrop.Wrap(missing)).Throws<OpenUsdNativeException>();
        await Assert.That(UsdUINodeGraphNode.TryWrap(xform, out _)).IsFalse();
    }

    private static UsdStage CreateSchemaStage(string directory)
    {
        string path = Path.Combine(directory, $"schema-{Guid.NewGuid():N}.usda");
        return UsdStage.Create(path);
    }
}
