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

    [Test]
    public async Task ShaderNodeDefinitionsCompareEveryFieldAndPropertyPositionByValue()
    {
        UsdShaderNodeDefinition left = CreateShaderNodeDefinition();
        UsdShaderNodeDefinition right = CreateShaderNodeDefinition();

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(right).IsEqualTo(left);
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
        await Assert.That(left.Equals(null)).IsFalse();

        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinition(identifier: "different.identifier"));
        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinition(name: "Different Name"));
        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinition(function: "differentFunction"));
        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinition(shadingSystem: "differentShadingSystem"));
        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinition(context: "differentContext"));
        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinition(resolvedDefinitionUri: "different-definition.usda"));
        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinition(resolvedImplementationUri: "different-implementation.glslfx"));
        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinition(implementationName: "DifferentImplementation"));
        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinition(isValid: false));

        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinition(
                properties: CreateShaderProperties(
                    first: new UsdShaderProperty(
                        "different.first",
                        "color3f",
                        UsdShaderPropertyDirection.Input,
                        IsArray: false,
                        IsConnectable: true))));
        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinition(
                properties: CreateShaderProperties(
                    middle: new UsdShaderProperty(
                        "property.middle",
                        "token",
                        UsdShaderPropertyDirection.Output,
                        IsArray: true,
                        IsConnectable: true))));
        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinition(
                properties: CreateShaderProperties(
                    last: new UsdShaderProperty(
                        "property.last",
                        "token",
                        UsdShaderPropertyDirection.Output,
                        IsArray: true,
                        IsConnectable: true))));
        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinition(properties: CreateShaderProperties()[..2]));
    }

    [Test]
    public async Task ShaderNodeDefinitionSnapshotsCompareDefinitionSequencesAndTruncationByValue()
    {
        UsdShaderNodeDefinitionSnapshot left = CreateShaderNodeDefinitionSnapshot();
        UsdShaderNodeDefinitionSnapshot right = CreateShaderNodeDefinitionSnapshot();

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(right).IsEqualTo(left);
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
        await Assert.That(left.Equals(null)).IsFalse();

        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinitionSnapshot(firstIdentifier: "different.first"));
        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinitionSnapshot(middleIdentifier: "different.middle"));
        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinitionSnapshot(lastIdentifier: "different.last"));
        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinitionSnapshot(includeLast: false));
        await Assert.That(left).IsNotEqualTo(
            CreateShaderNodeDefinitionSnapshot(isTruncated: true));
    }

    [Test]
    public async Task ShaderNodeDefinitionsAndSnapshotsFormatNestedValues()
    {
        UsdShaderNodeDefinition definition = CreateShaderNodeDefinition(
            identifier: "format.identifier",
            function: "formatFunction",
            implementationName: "FormatImplementation",
            properties:
            [
                new UsdShaderProperty(
                    "format.first",
                    "color3f",
                    UsdShaderPropertyDirection.Input,
                    IsArray: false,
                    IsConnectable: true),
                new UsdShaderProperty(
                    "format.middle",
                    "float",
                    UsdShaderPropertyDirection.Input,
                    IsArray: true,
                    IsConnectable: true),
                new UsdShaderProperty(
                    "format.last",
                    "token",
                    UsdShaderPropertyDirection.Output,
                    IsArray: false,
                    IsConnectable: false)
            ]);

        string definitionText = definition.ToString();
        await Assert.That(definitionText).Contains("Identifier = format.identifier");
        await Assert.That(definitionText).Contains("Function = formatFunction");
        await Assert.That(definitionText).Contains("ImplementationName = FormatImplementation");
        await Assert.That(definitionText).Contains("Name = format.first");
        await Assert.That(definitionText).Contains("Name = format.middle");
        await Assert.That(definitionText).Contains("Name = format.last");
        await Assert.That(definitionText).Contains("IsValid = True");

        UsdShaderNodeDefinitionSnapshot snapshot = CreateShaderNodeDefinitionSnapshot(
            firstIdentifier: "format.snapshot.nested");
        string snapshotText = snapshot.ToString();
        await Assert.That(snapshotText).Contains("Identifier = format.snapshot.nested");
        await Assert.That(snapshotText).Contains("IsTruncated = False");
    }

    private static UsdShaderNodeDefinition CreateShaderNodeDefinition(
        string identifier = "shader.identifier",
        string name = "Shader Name",
        string function = "shaderFunction",
        string shadingSystem = "usd",
        string context = "surface",
        string resolvedDefinitionUri = "definitions/shader.usda",
        string resolvedImplementationUri = "implementations/shader.glslfx",
        string implementationName = "ShaderImplementation",
        bool isValid = true,
        IReadOnlyList<UsdShaderProperty>? properties = null)
    {
        IReadOnlyList<UsdShaderProperty> copiedProperties =
            properties is null ? CreateShaderProperties() : [.. properties];
        return new UsdShaderNodeDefinition(
            identifier,
            name,
            function,
            shadingSystem,
            context,
            resolvedDefinitionUri,
            resolvedImplementationUri,
            implementationName,
            copiedProperties,
            isValid);
    }

    private static UsdShaderProperty[] CreateShaderProperties(
        UsdShaderProperty? first = null,
        UsdShaderProperty? middle = null,
        UsdShaderProperty? last = null) =>
        [
            first ?? new UsdShaderProperty(
                "property.first",
                "color3f",
                UsdShaderPropertyDirection.Input,
                IsArray: false,
                IsConnectable: true),
            middle ?? new UsdShaderProperty(
                "property.middle",
                "float",
                UsdShaderPropertyDirection.Input,
                IsArray: true,
                IsConnectable: true),
            last ?? new UsdShaderProperty(
                "property.last",
                "token",
                UsdShaderPropertyDirection.Output,
                IsArray: false,
                IsConnectable: false)
        ];

    private static UsdShaderNodeDefinitionSnapshot CreateShaderNodeDefinitionSnapshot(
        string firstIdentifier = "snapshot.first",
        string middleIdentifier = "snapshot.middle",
        string lastIdentifier = "snapshot.last",
        bool isTruncated = false,
        bool includeLast = true)
    {
        List<UsdShaderNodeDefinition> definitions =
        [
            CreateShaderNodeDefinition(
                identifier: firstIdentifier,
                name: "Snapshot First"),
            CreateShaderNodeDefinition(
                identifier: middleIdentifier,
                name: "Snapshot Middle")
        ];
        if (includeLast)
        {
            definitions.Add(
                CreateShaderNodeDefinition(
                    identifier: lastIdentifier,
                    name: "Snapshot Last"));
        }
        return new UsdShaderNodeDefinitionSnapshot([.. definitions], isTruncated);
    }
}
