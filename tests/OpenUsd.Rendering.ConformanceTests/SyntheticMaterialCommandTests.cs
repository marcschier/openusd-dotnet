// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed class SyntheticMaterialCommandTests
{
    [Test]
    public async Task SyntheticMaterialCommandsPreserveTextureEntriesAcrossPageAbi()
    {
        using var scene = new SilkSceneState();
        scene.Apply(StormSilkParityCaptureDriverTests.CreateSyntheticMultiTextureMaterialCommand(), 1, 1);
        SilkMaterialData material = scene.Materials["/Synthetic"];
        await Assert.That(material.Textures.Count).IsEqualTo(2);
        await Assert.That(material.Textures[0].SourceKind).IsEqualTo(SilkTextureSourceKind.Standard);
        await Assert.That(material.Textures[0].Asset).IsEqualTo("textures/albedo.ptx");
        await Assert.That(material.Textures[0].UvPrimvar).IsEqualTo("st");
        await Assert.That(material.Textures[0].Parameter).IsEqualTo(SilkMaterialParameter.DiffuseColor);
        await Assert.That(material.Textures[1].SourceKind).IsEqualTo(SilkTextureSourceKind.Standard);
        await Assert.That(material.Textures[1].Asset).IsEqualTo("textures/roughness.png");
        await Assert.That(material.Textures[1].UvPrimvar).IsEqualTo("st2");
        await Assert.That(material.Textures[1].Parameter).IsEqualTo(SilkMaterialParameter.Roughness);
    }
}
