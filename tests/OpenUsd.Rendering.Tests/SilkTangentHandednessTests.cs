// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkTangentHandednessTests
{
    [Test]
    [Arguments(false, false, 1f)]
    [Arguments(true, false, -1f)]
    [Arguments(false, true, -1f)]
    [Arguments(true, true, 1f)]
    public async Task TangentSignTracksMirroredUvsAndAuthoredNormalOrientation(
        bool mirrored, bool reversedNormal, float expectedSign)
    {
        float u = mirrored ? -1 : 1;
        float n = reversedNormal ? -1 : 1;
        var uv = new SilkVertexAttributeData("st", SilkAttributeSemantic.TexCoord,
            SilkAttributeInterpolation.Vertex, 2, [0, 0, u, 0, 0, 1]);
        var mesh = new SilkMeshData(1, "/Triangle", 1, -1, -1, SilkTopologyKind.TriangleList, 1,
            [0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 1, 2], [0], [1, 1, 1, 1],
            [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1],
            0, true, SilkMeshCullStyle.Nothing, [0, 0, n, 0, 0, n, 0, 0, n], "", [uv]);
        SilkMeshGeometry geometry = SilkMeshGeometryBuilder.Build(mesh, "st", requireTangents: true);
        await Assert.That(geometry.HasTangents).IsTrue();
        for (int vertex = 0; vertex < 3; vertex++)
        {
            await Assert.That(geometry.Vertices[vertex * 12 + 8]).IsEqualTo(u);
            await Assert.That(geometry.Vertices[vertex * 12 + 9]).IsEqualTo(0f);
            await Assert.That(geometry.Vertices[vertex * 12 + 10]).IsEqualTo(0f);
            await Assert.That(geometry.Vertices[vertex * 12 + 11]).IsEqualTo(expectedSign);
        }
    }
}
