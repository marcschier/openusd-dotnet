// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Media;
using OpenUsd.Proc;
using OpenUsd.Render;
using OpenUsd.UI;
using OpenUsd.Vol;

namespace OpenUsd.Tests;

public sealed class SchemaFacadeSurfaceTests
{
    [Test]
    public async Task ExposesRequestedTypedSchemaFamilies()
    {
        Type[] types =
        [
            typeof(UsdVolVolume),
            typeof(UsdVolOpenVDBAsset),
            typeof(UsdVolField3DAsset),
            typeof(UsdVolVolumeFieldAsset),
            typeof(UsdRenderSettings),
            typeof(UsdRenderProduct),
            typeof(UsdRenderVar),
            typeof(UsdRenderSettingsBase),
            typeof(UsdRenderPass),
            typeof(UsdMediaSpatialAudio),
            typeof(UsdProcGenerativeProcedural),
            typeof(UsdUIBackdrop)
        ];

        foreach (Type type in types)
        {
            await Assert.That(typeof(IUsdStageBound).IsAssignableFrom(type))
                .IsTrue()
                .Because(type.FullName ?? type.Name);
            await Assert.That(type.GetMethod("TryWrap") is not null).IsTrue();
            await Assert.That(type.GetMethod("Wrap") is not null).IsTrue();
        }
    }

    [Test]
    public async Task ExposesAppliedApiSchemaPattern()
    {
        Type[] appliedApiTypes =
        [
            typeof(UsdMediaAssetPreviews),
            typeof(UsdUINodeGraphNode),
            typeof(UsdUISceneGraphPrim)
        ];

        foreach (Type type in appliedApiTypes)
        {
            await Assert.That(type.GetMethod("Apply") is not null)
                .IsTrue()
                .Because(type.FullName ?? type.Name);
            await Assert.That(type.GetMethod("TryWrap") is not null).IsTrue();
            await Assert.That(type.GetMethod("Wrap") is not null).IsTrue();
        }
    }
}



