// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Shade;

namespace OpenUsd.Tests;

/// <summary>
/// Semantic round-trip evidence for the Omniverse interoperability profile
/// (<c>eng/omniverse-profile.json</c>, and the <c>omniverse-interchange</c> area of
/// <c>eng/support-manifest.json</c>). Fixtures live under <c>test-assets/omniverse/</c> and are
/// repository-authored, synthetic, and redistributable; see that directory's README for
/// provenance. These tests assert composed values survive open, export, and reopen -- not byte
/// equality of the exported file.
/// </summary>
public sealed class OmniverseInterchangeRoundTripTests
{
    [Test]
    public async Task UnknownMetadataCustomPropertiesAndAppliedSchemasSurviveExportAndReopen()
    {
        using UsdStage original = OpenFixtureOrSkip("unknown-metadata-roundtrip.usda");
        string exportPath = GetTempUsdaPath();
        try
        {
            original.Export(exportPath);
            using UsdStage reopened = UsdStage.Open(exportPath);

            UsdPrim world = reopened.GetPrim("/World");
            await Assert.That(world.GetAppliedSchemas()).Contains("CollectionAPI:lightLink");
            await Assert.That(world.GetAppliedSchemas()).Contains("SyntheticVendorMetadataAPI");
            await Assert.That(world.GetMetadataString("vendorTool")).IsEqualTo("synthetic-import-v1");
            await Assert.That(world.GetMetadataInt64("vendorRevision")).IsEqualTo(7L);
            await Assert.That(world.GetDouble("custom:anchorWeight")).IsEqualTo(0.5);
            await Assert.That(world.GetToken("custom:anchorTag")).IsEqualTo("unknown-metadata-fixture");

            UsdPrim anchor = reopened.GetPrim("/World/Anchor");
            await Assert.That(anchor.GetAppliedSchemas()).Contains("CollectionAPI:lightLink");
            await Assert.That(anchor.GetMetadataString("vendorTool")).IsEqualTo("synthetic-import-v1");
            await Assert.That(anchor.GetInt64("custom:vendorPartId")).IsEqualTo(42L);
        }
        finally
        {
            TryDelete(exportPath);
        }
    }

    [Test]
    public async Task DualContextMaterialAnchorResolvesBothTerminalsAndSurvivesExportAndReopen()
    {
        using UsdStage original = OpenFixtureOrSkip("dual-context-material-anchor.usda");
        string exportPath = GetTempUsdaPath();
        try
        {
            original.Export(exportPath);
            using UsdStage reopened = UsdStage.Open(exportPath);

            UsdPrim materialPrim = reopened.GetPrim("/World/Looks/DualContextAnchor");
            UsdShadeMaterial material = UsdShadeMaterial.Wrap(materialPrim);

            UsdShadeConnection universal = material.GetSurfaceOutput().GetConnectedSource();
            await Assert.That(universal.SourcePrimPath)
                .IsEqualTo("/World/Looks/DualContextAnchor/PreviewSurface");
            await Assert.That(universal.SourceName).IsEqualTo("surface");

            UsdShadeConnection mtlx = material
                .CreateTerminalOutput(UsdShadeMaterialTerminal.Surface, "mtlx")
                .GetConnectedSource();
            await Assert.That(mtlx.SourcePrimPath)
                .IsEqualTo("/World/Looks/DualContextAnchor/StandardSurface");
            await Assert.That(mtlx.SourceName).IsEqualTo("out");

            await Assert.That(materialPrim.GetAppliedSchemas()).Contains("MaterialXConfigAPI");
        }
        finally
        {
            TryDelete(exportPath);
        }
    }

    [Test]
    public async Task NestedVendorMetadataDictionariesSurviveExportAndReopenAtLayerAndPrimScope()
    {
        using UsdStage original = OpenFixtureOrSkip("simready-style-metadata-roundtrip.usda");
        string exportPath = GetTempUsdaPath();
        try
        {
            // Layer-level customLayerData must resolve through its ':'-separated key path the
            // same way prim-level customData already does; this is what
            // openusd_layer_set_metadata/get_metadata/clear_metadata now guarantee.
            using (UsdLayer rootLayer = original.GetRootLayer())
            {
                await Assert.That(rootLayer.GetMetadataString("SyntheticSimReadyMetadata:assetType"))
                    .IsEqualTo("synthetic-fixture-widget");
                await Assert.That(rootLayer.GetMetadataString("SyntheticSimReadyMetadata:source"))
                    .IsEqualTo("openusd2-repository");
            }

            original.Export(exportPath);
            using UsdStage reopened = UsdStage.Open(exportPath);

            using (UsdLayer reopenedRootLayer = reopened.GetRootLayer())
            {
                await Assert.That(reopenedRootLayer.GetMetadataString("SyntheticSimReadyMetadata:assetType"))
                    .IsEqualTo("synthetic-fixture-widget");
                await Assert.That(reopenedRootLayer.GetMetadataString("SyntheticSimReadyMetadata:physicsProfile"))
                    .IsEqualTo("neutral");
            }

            UsdPrim prop = reopened.GetPrim("/World/Prop");
            await Assert.That(prop.GetMetadataString("SyntheticSimReadyMetadata:semanticLabel"))
                .IsEqualTo("prop");
        }
        finally
        {
            TryDelete(exportPath);
        }
    }

    private static UsdStage OpenFixtureOrSkip(string fileName)
    {
        try
        {
            return UsdStage.Open(Path.Combine(
                FindRepositoryRoot(),
                "test-assets",
                "omniverse",
                fileName));
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
            throw;
        }
    }

    private static string GetTempUsdaPath() =>
        Path.Combine(Path.GetTempPath(), $"omniverse_interchange_roundtrip_{Guid.NewGuid():N}.usda");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup only; the OS temp directory is reclaimed regardless.
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
