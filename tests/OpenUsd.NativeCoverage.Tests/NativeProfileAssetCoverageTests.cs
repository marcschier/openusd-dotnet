// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;
using OpenUsd.Shade;
using OpenUsd.Vol;

namespace OpenUsd.NativeCoverage.Tests;

public sealed class NativeProfileAssetCoverageTests
{
    [Test]
    public async Task OpenVdbAssetComposesVolumeFieldRelationships()
    {
        using UsdStage stage = OpenProfileAsset("openvdb", "vdb-volume.usda");

        await Assert.That(stage.GetDefaultPrim().Path).IsEqualTo("/VdbVolume");
        await Assert.That(stage.Traverse().Select(prim => prim.Path).ToArray())
            .IsEquivalentTo(["/VdbVolume", "/VdbVolume/Density"]);

        UsdVolVolume volume = UsdVolVolume.Wrap(stage.GetPrim("/VdbVolume"));
        IReadOnlyDictionary<string, string> fields = volume.GetFieldPaths();
        await Assert.That(fields).ContainsKey("density");
        await Assert.That(fields["density"]).IsEqualTo("/VdbVolume/Density");

        UsdVolVolumeFieldAsset density = UsdVolVolumeFieldAsset.Wrap(stage.GetPrim("/VdbVolume/Density"));
        await Assert.That(density.FilePath.Path).IsEqualTo("smoke_000001.vdb");
        await Assert.That(density.FieldName).IsEqualTo("density");
    }

    [Test]
    public async Task AlembicAssetOpensAsUsdStageWithExpectedPrimTopology()
    {
        using UsdStage stage = OpenProfileAsset("alembic", "mesh.abc");
        IReadOnlyList<UsdPrim> prims = stage.Traverse();

        await Assert.That(prims.Count).IsEqualTo(2);
        await Assert.That(stage.GetDefaultPrim().Path).IsEqualTo("/pCubeShape1");
        await Assert.That(prims[0].Path).IsEqualTo("/pCubeShape1");
        await Assert.That(prims[0].TypeName).IsEqualTo("Mesh");
        await Assert.That(prims[1].Path).IsEqualTo("/pCubeShape2");
        await Assert.That(prims[1].TypeName).IsEqualTo("Mesh");
    }

    [Test]
    public async Task DracoCompressedMeshReadsDecodedTopologyAndPoints()
    {
        using UsdStage stage = OpenProfileAsset("draco", "CubeCompressedTriangles.usda");
        UsdGeomMesh mesh = UsdGeomMesh.Wrap(stage.GetPrim("/Cube/Geom/Cube"));

        int[] faceVertexCounts = mesh.GetFaceVertexCounts();
        int[] faceVertexIndices = mesh.GetFaceVertexIndices();
        UsdVec3f[] points = mesh.GetPoints();

        await Assert.That(faceVertexCounts.Length).IsEqualTo(12);
        await Assert.That(faceVertexCounts[0]).IsEqualTo(3);
        await Assert.That(faceVertexCounts[faceVertexCounts.Length / 2]).IsEqualTo(3);
        await Assert.That(faceVertexCounts[^1]).IsEqualTo(3);
        await Assert.That(faceVertexIndices.Length).IsEqualTo(36);
        await Assert.That(points.Length).IsEqualTo(8);
        await Assert.That(points[0]).IsEqualTo(new UsdVec3f(-0.5f, 0.5f, -0.5f));
        await Assert.That(points[points.Length / 2]).IsEqualTo(new UsdVec3f(0.5f, 0.5f, -0.5f));
        await Assert.That(points[^1]).IsEqualTo(new UsdVec3f(0.5f, 0.5f, 0.5f));
    }

    [Test]
    public async Task PtexAssetPathComposesThroughUsdUvTextureShader()
    {
        using UsdStage stage = OpenProfileAsset("ptex", "ptex-material.usda");
        UsdShadeShader shader = UsdShadeShader.Wrap(stage.GetPrim("/PtexMaterial/Texture"));

        await Assert.That(shader.SourceId).IsEqualTo("UsdUVTexture");
        await Assert.That(shader.GetInput("file").GetAssetPath().Path).IsEqualTo("tetrahedron.ptx");
        await Assert.That(File.Exists(ProfileAssetPath("ptex", "tetrahedron.ptx"))).IsTrue();
    }

    private static UsdStage OpenProfileAsset(string component, string fileName)
    {
        NativeCoverageRuntime.EnsureNativeLoaded();
        return UsdStage.Open(ProfileAssetPath(component, fileName));
    }

    private static string ProfileAssetPath(string component, string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "test-assets", "native-profile", component, fileName);
}
