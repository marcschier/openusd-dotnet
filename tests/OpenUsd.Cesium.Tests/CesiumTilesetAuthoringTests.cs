// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;

namespace OpenUsd.Cesium.Tests;

public sealed class CesiumTilesetAuthoringTests
{
    private static readonly int[] ExpectedFaceCounts = [3, 3, 3, 3];

    [Test]
    public async Task CapturedTileMeshAuthorsTopologyAndPointData()
    {
        using AuthoredTile authored = CreateAuthoredTile("cesium-topology-test.usda");

        await Assert.That(authored.Result.MeshCount).IsEqualTo(1);
        await Assert.That(authored.Points[0]).IsEqualTo(new UsdVec3f(0, 0, 0));
        await Assert.That(authored.Points[2]).IsEqualTo(new UsdVec3f(10, 10, 0));
        await Assert.That(authored.Points[^1]).IsEqualTo(new UsdVec3f(5, 5, 2));
        await Assert.That(authored.FaceVertexCounts).IsEquivalentTo(ExpectedFaceCounts);
        await Assert.That(authored.FaceVertexIndices[0]).IsEqualTo(0);
        await Assert.That(authored.FaceVertexIndices[authored.FaceVertexIndices.Length / 2]).IsEqualTo(2);
        await Assert.That(authored.FaceVertexIndices[^1]).IsEqualTo(4);
        await Assert.That(authored.Normals[0]).IsEqualTo(new UsdVec3f(0, 0, 1));
        await Assert.That(authored.Normals[authored.Normals.Length / 2]).IsEqualTo(new UsdVec3f(0, 1, 0));
        await Assert.That(authored.Normals[^1]).IsEqualTo(new UsdVec3f(1, 0, 0));
        await Assert.That(authored.TexCoords[0]).IsEqualTo(new UsdVec2f(0, 0));
        await Assert.That(authored.TexCoords[authored.TexCoords.Length / 2]).IsEqualTo(new UsdVec2f(1, 1));
        await Assert.That(authored.TexCoords[^1]).IsEqualTo(new UsdVec2f(0.5f, 0.5f));
    }

    [Test]
    public async Task CapturedTileMeshKeepsEcefOffsetOnParentAndFloatVerticesSmall()
    {
        using AuthoredTile authored = CreateAuthoredTile("cesium-georeference-test.usda");

        await Assert.That(authored.ParentTransform.M30).IsEqualTo(6_378_137.123456789);
        await Assert.That(authored.ParentTransform.M31).IsEqualTo(-12_345.5);
        await Assert.That(authored.ParentTransform.M32).IsEqualTo(456.25);
        await Assert.That(authored.LocalTransform.ExtractTranslation()).IsEqualTo(new UsdVec3d(0, 0, 0));
        await Assert.That(authored.Points.Max(MaxAbsComponent)).IsLessThan(32);
    }

    private static AuthoredTile CreateAuthoredTile(string fileName)
    {
        string path = Path.Combine(Environment.CurrentDirectory, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        UsdStage stage = UsdStage.Create(path);
        UsdMatrix4d ecefTransform = UsdMatrix4d.CreateTranslation(6_378_137.123456789, -12_345.5, 456.25);
        var captured = new CesiumTileset.CapturedMesh(
            ecefTransform,
            [
                new UsdVec3f(0, 0, 0),
                new UsdVec3f(10, 0, 0),
                new UsdVec3f(10, 10, 0),
                new UsdVec3f(0, 10, 0),
                new UsdVec3f(5, 5, 2)
            ],
            [3, 3, 3, 3],
            [0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4],
            [
                new UsdVec3f(0, 0, 1),
                new UsdVec3f(0, 0, 1),
                new UsdVec3f(0, 1, 0),
                new UsdVec3f(0, 1, 0),
                new UsdVec3f(1, 0, 0)
            ],
            [
                new UsdVec2f(0, 0),
                new UsdVec2f(1, 0),
                new UsdVec2f(1, 1),
                new UsdVec2f(0, 1),
                new UsdVec2f(0.5f, 0.5f)
            ]);

        CesiumTileImportResult result = CesiumTileset.AuthorCapturedMeshes(stage, "/CesiumTiles", [captured]);
        UsdGeomXform georeference = UsdGeomXform.Wrap(stage.GetPrim("/CesiumTiles"));
        UsdGeomMesh mesh = UsdGeomMesh.Wrap(stage.GetPrim(result.PrimPaths[0]));
        return new AuthoredTile(
            stage,
            result,
            georeference.Xformable.GetLocalTransform(),
            mesh.Xformable.GetLocalTransform(),
            mesh.GetPoints(),
            mesh.GetFaceVertexCounts(),
            mesh.GetFaceVertexIndices(),
            mesh.GetNormals(),
            new UsdGeomPrimvarsAPI(mesh.Prim).GetPrimvar("st").GetVec2fArray());
    }

    private static float MaxAbsComponent(UsdVec3f value) =>
        MathF.Max(MathF.Abs(value.X), MathF.Max(MathF.Abs(value.Y), MathF.Abs(value.Z)));

    private sealed record AuthoredTile(
        UsdStage Stage,
        CesiumTileImportResult Result,
        UsdMatrix4d ParentTransform,
        UsdMatrix4d LocalTransform,
        UsdVec3f[] Points,
        int[] FaceVertexCounts,
        int[] FaceVertexIndices,
        UsdVec3f[] Normals,
        UsdVec2f[] TexCoords) : IDisposable
    {
        public void Dispose() => Stage.Dispose();
    }
}
