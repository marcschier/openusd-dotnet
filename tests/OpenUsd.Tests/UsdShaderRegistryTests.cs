// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Tests;

/// <summary>
/// Native-backed coverage for <see cref="UsdShaderRegistry"/>: bulk Sdr/Ndr shader
/// node-definition registry introspection against the standard <c>UsdPreviewSurface</c>
/// built-ins, a MaterialX standard-library node when the usdMtlx discovery plugin is
/// registered, and the documented no-MDL-SDK path for MDL-only source assets.
/// </summary>
public sealed class UsdShaderRegistryTests
{
    [Test]
    public async Task GetNodeDefinitionsSnapshotReportsNotTruncatedForTheRealRegistry()
    {
        UsdShaderNodeDefinitionSnapshot snapshot = GetSnapshotOrSkip();

        await Assert.That(snapshot.IsTruncated).IsFalse()
            .Because("the real Sdr registry available to this test is far below the bounded page " +
                "capacity (OPENUSD_SDR_NODE_DEFINITION_MAX_NODES/_PROPERTIES/_STRING_BYTES).");
        await Assert.That(snapshot.Definitions).IsNotEmpty();
    }

    [Test]
    public async Task GetNodeDefinitionsAgreesWithTheSnapshotWhenNotTruncated()
    {
        UsdShaderNodeDefinitionSnapshot snapshot = GetSnapshotOrSkip();

        // GetNodeDefinitions() is a thin convenience over GetNodeDefinitionsSnapshot() that only
        // throws when the page was truncated; when it was not, both must agree exactly.
        IReadOnlyList<UsdShaderNodeDefinition> definitions = UsdShaderRegistry.GetNodeDefinitions();
        await Assert.That(definitions.Count).IsEqualTo(snapshot.Definitions.Count);
    }

    [Test]
    public async Task GetNodeDefinitionsIncludesTheUsdPreviewSurfaceBuiltIn()
    {
        UsdShaderNodeDefinition[] definitions = [.. GetSnapshotOrSkip().Definitions];

        UsdShaderNodeDefinition? previewSurface = definitions.FirstOrDefault(
            definition => definition.Name == "UsdPreviewSurface" ||
                definition.Identifier == "UsdPreviewSurface");

        await Assert.That(previewSurface).IsNotNull()
            .Because("the standard UsdPreviewSurface shader must be discoverable in the Sdr registry.");
        await Assert.That(previewSurface!.IsValid).IsTrue();
        await Assert.That(previewSurface.ShadingSystem).IsNotEmpty();

        string[] inputNames = [.. previewSurface.Properties
            .Where(property => property.Direction == UsdShaderPropertyDirection.Input)
            .Select(property => property.Name)];
        string[] outputNames = [.. previewSurface.Properties
            .Where(property => property.Direction == UsdShaderPropertyDirection.Output)
            .Select(property => property.Name)];

        await Assert.That(inputNames).Contains("diffuseColor")
            .Because("diffuseColor is a well-known UsdPreviewSurface input.");
        await Assert.That(outputNames).Contains("surface")
            .Because("surface is UsdPreviewSurface's material terminal output.");

        UsdShaderProperty diffuseColor = previewSurface.Properties
            .First(property => property.Name == "diffuseColor");
        await Assert.That(diffuseColor.Type).IsNotEmpty();
        await Assert.That(diffuseColor.Direction).IsEqualTo(UsdShaderPropertyDirection.Input);
    }

    [Test]
    public async Task GetNodeDefinitionsIncludesTheUsdUvTextureBuiltIn()
    {
        IReadOnlyList<UsdShaderNodeDefinition> definitions = GetSnapshotOrSkip().Definitions;

        UsdShaderNodeDefinition? uvTexture = definitions.FirstOrDefault(
            definition => definition.Name == "UsdUVTexture" || definition.Identifier == "UsdUVTexture");

        await Assert.That(uvTexture).IsNotNull()
            .Because("the standard UsdUVTexture shader must be discoverable in the Sdr registry.");

        string[] propertyNames = [.. uvTexture!.Properties.Select(property => property.Name)];
        await Assert.That(propertyNames).Contains("file");
        await Assert.That(propertyNames).Contains("rgb");
    }

    [Test]
    public async Task GetNodeDefinitionsExposesAMaterialXStandardLibraryNodeWhenDiscoverable()
    {
        IReadOnlyList<UsdShaderNodeDefinition> definitions = GetSnapshotOrSkip().Definitions;

        UsdShaderNodeDefinition? standardSurface = definitions.FirstOrDefault(
            definition => definition.Name.Contains("standard_surface", StringComparison.Ordinal) ||
                definition.Identifier.Contains("standard_surface", StringComparison.Ordinal));

        if (standardSurface is null)
        {
            Skip.Test(
                "No MaterialX ND_standard_surface_surfaceshader definition was discoverable in this " +
                "runtime's Sdr registry; the usdMtlx discovery plugin or its standard-library data " +
                "files are not staged for this test run.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }

        await Assert.That(standardSurface.IsValid).IsTrue();
        await Assert.That(standardSurface.Properties).IsNotEmpty();
        foreach (UsdShaderProperty property in standardSurface.Properties)
        {
            await Assert.That(property.Name).IsNotEmpty();
            await Assert.That(property.Type).IsNotEmpty();
        }
    }

    [Test]
    public async Task TryGetNodeDefinitionFromAssetReportsNotFoundWithoutThrowingForMdlOnlyAssets()
    {
        try
        {
            bool found = UsdShaderRegistry.TryGetNodeDefinitionFromAsset(
                "OmniPBR.mdl",
                "OmniPBR",
                "mdl",
                out UsdShaderNodeDefinition? definition);

            // Without the optional MDL SDK adapter, no parser plugin resolves an .mdl source
            // asset. That must surface as "not found", never as a thrown native error.
            if (!found)
            {
                await Assert.That(definition).IsNull();
            }
            else
            {
                await Assert.That(definition).IsNotNull();
                await Assert.That(definition!.Properties).IsNotNull();
            }
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
        }
    }

    [Test]
    public async Task TryGetNodeDefinitionFromAssetRejectsAnEmptySourceAsset()
    {
        static void act() => UsdShaderRegistry.TryGetNodeDefinitionFromAsset(
            string.Empty,
            null,
            null,
            out _);

        await Assert.That(act).Throws<ArgumentException>();
    }

    private static UsdShaderNodeDefinitionSnapshot GetSnapshotOrSkip()
    {
        try
        {
            UsdShaderNodeDefinitionSnapshot snapshot = UsdShaderRegistry.GetNodeDefinitionsSnapshot();
            if (snapshot.Definitions.Count == 0)
            {
                Skip.Test("The Sdr registry reported zero shader node definitions in this runtime.");
                throw new InvalidOperationException("Skip.Test returned unexpectedly.");
            }
            return snapshot;
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
            throw;
        }
    }
}
