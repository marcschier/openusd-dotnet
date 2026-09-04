// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.NativeCoverage.Tests;

public sealed class UsdShaderRegistryNativeCoverageTests
{
    [Test]
    public async Task RegistryEnumerationConvertsBuiltInsAndMatchesConvenienceView()
    {
        _ = NativeCoverageRuntime.CreateTempDirectory(
            nameof(RegistryEnumerationConvertsBuiltInsAndMatchesConvenienceView));

        UsdShaderNodeDefinitionSnapshot snapshot =
            UsdShaderRegistry.GetNodeDefinitionsSnapshot();

        await Assert.That(snapshot.IsTruncated).IsFalse()
            .Because("the staged core registry must fit in the bounded native definition page.");
        await Assert.That(snapshot.Definitions.Count).IsGreaterThan(0)
            .Because("the required staged usdShaders plugin must publish its built-in definitions.");

        UsdShaderNodeDefinition? previewSurface = snapshot.Definitions.FirstOrDefault(
            definition => definition.Identifier == "UsdPreviewSurface" ||
                definition.Name == "UsdPreviewSurface");

        await Assert.That(previewSurface).IsNotNull()
            .Because("UsdPreviewSurface is supplied by the required core usdShaders plugin.");
        await Assert.That(previewSurface!.Identifier).IsEqualTo("UsdPreviewSurface");
        await Assert.That(previewSurface.Name).IsEqualTo("UsdPreviewSurface");
        await Assert.That(previewSurface.IsValid).IsTrue();
        await Assert.That(previewSurface.ShadingSystem).IsEqualTo("glslfx");
        await Assert.That(previewSurface.Properties.Count).IsEqualTo(17)
            .Because("the staged UsdPreviewSurface definition has fifteen inputs and two outputs.");

        UsdShaderProperty? diffuseColor = previewSurface.Properties.FirstOrDefault(
            property => property.Name == "diffuseColor");
        await Assert.That(diffuseColor).IsNotNull();
        await Assert.That(diffuseColor!.Type).IsEqualTo("color");
        await Assert.That(diffuseColor.Direction)
            .IsEqualTo(UsdShaderPropertyDirection.Input);
        await Assert.That(diffuseColor.IsArray).IsFalse();
        await Assert.That(diffuseColor.IsConnectable).IsTrue();

        UsdShaderProperty? surface = previewSurface.Properties.FirstOrDefault(
            property => property.Name == "surface");
        await Assert.That(surface).IsNotNull();
        await Assert.That(surface!.Type).IsEqualTo("terminal");
        await Assert.That(surface.Direction)
            .IsEqualTo(UsdShaderPropertyDirection.Output);
        await Assert.That(surface.IsArray).IsFalse();
        await Assert.That(surface.IsConnectable).IsTrue();

        IReadOnlyList<UsdShaderNodeDefinition> definitions =
            UsdShaderRegistry.GetNodeDefinitions();

        await Assert.That(definitions.Count).IsEqualTo(snapshot.Definitions.Count);
        await Assert.That(snapshot.Definitions.SequenceEqual(definitions)).IsTrue()
            .Because("the convenience API must preserve the native registry order and values.");

        UsdShaderNodeDefinition conveniencePreviewSurface = definitions.First(
            definition => definition.Identifier == "UsdPreviewSurface");
        await Assert.That(ReferenceEquals(conveniencePreviewSurface, previewSurface)).IsFalse()
            .Because("separate registry reads return detached converted definitions.");
        await Assert.That(conveniencePreviewSurface).IsEqualTo(previewSurface)
            .Because("the two public enumeration APIs must convert the built-in by value.");
    }
}
