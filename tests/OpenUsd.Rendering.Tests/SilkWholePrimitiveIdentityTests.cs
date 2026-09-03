// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

/// <summary>
/// Covers the whole-resource pick token ranges every rendered topology owns,
/// and the ordered instancing chain a retained record carries through to a
/// resolved identity.
/// </summary>
/// <remarks>
/// Both exist for the same reason: an identity that names only part of what it
/// describes is indistinguishable from a different identity. A curve with no
/// token range at all could not be drawn into the surface pass, so it could
/// neither be picked nor occlude anything; and a nested instance described by
/// an innermost path beside a composed ordinal names an instance that does not
/// exist.
/// </remarks>
public sealed class SilkWholePrimitiveIdentityTests
{
    [Test]
    public async Task EveryRenderedTopologyOwnsANonEmptyWholeResourceRange()
    {
        var table = new SilkPickIdentityTable();
        SilkPickTokenRange triangles = table.Upsert(Triangles("/Mesh", 1));
        SilkPickTokenRange lines = table.Upsert(Lines("/Curve", 2));
        SilkPickTokenRange points = table.Upsert(Points("/Cloud", 3));

        // Before this, a line or point resource was refused a range entirely,
        // so the surface pass had no token to draw it with and threw.
        await Assert.That(triangles.FirstToken).IsNotEqualTo(0u);
        await Assert.That(lines.FirstToken).IsNotEqualTo(0u);
        await Assert.That(points.FirstToken).IsNotEqualTo(0u);
        await Assert.That(lines.TokenCount).IsEqualTo(2u);
        await Assert.That(points.TokenCount).IsEqualTo(3u);

        // The ranges are disjoint, so one token space still resolves without a
        // second table.
        await Assert.That(lines.FirstToken).IsGreaterThan(triangles.LastToken);
        await Assert.That(points.FirstToken).IsGreaterThan(lines.LastToken);
    }

    [Test]
    public async Task ACurveOrPointResourceResolvesToTheWholeResourceKind()
    {
        var table = new SilkPickIdentityTable();
        SilkPickTokenRange lines = table.Upsert(Lines("/Curve", 1));
        SilkPickTokenRange points = table.Upsert(Points("/Cloud", 2));

        await Assert.That(table.TryResolve(
            lines.FirstToken,
            out SilkPickIdentity curve)).IsTrue();
        await Assert.That(curve.Path).IsEqualTo("/Curve");
        await Assert.That(curve.SubprimKind)
            .IsEqualTo(SilkPickSubprimKind.Primitive);

        await Assert.That(table.TryResolve(
            points.LastToken,
            out SilkPickIdentity cloud)).IsTrue();
        await Assert.That(cloud.Path).IsEqualTo("/Cloud");
        await Assert.That(cloud.SubprimKind)
            .IsEqualTo(SilkPickSubprimKind.Primitive);

        // A triangulated mesh keeps resolving its whole-resource tokens to the
        // authored face they came from, which is what a face pick reads.
        SilkPickTokenRange triangles = table.Upsert(Triangles("/Mesh", 3));
        await Assert.That(table.TryResolve(
            triangles.FirstToken,
            out SilkPickIdentity mesh)).IsTrue();
        await Assert.That(mesh.SubprimKind).IsEqualTo(SilkPickSubprimKind.Face);
        await Assert.That(mesh.SubprimIndex).IsEqualTo(4);
    }

    [Test]
    public async Task OnlyATriangulatedMeshAnswersTheFaceTarget()
    {
        var table = new SilkPickIdentityTable();
        _ = table.Upsert(Triangles("/Mesh", 1));
        _ = table.Upsert(Lines("/Curve", 2));
        _ = table.Upsert(Points("/Cloud", 3));

        await Assert.That(table.AnswersFacePicks("/Mesh", 0)).IsTrue();
        await Assert.That(table.AnswersFacePicks("/Curve", 0)).IsFalse();
        await Assert.That(table.AnswersFacePicks("/Cloud", 0)).IsFalse();

        // A face request over a curve therefore has no range to draw a token
        // from at all, which is what makes it a pure occluder.
        await Assert.That(table.TryGetRange(
                "/Curve",
                0,
                SilkPickSubprimKind.Face,
                out SilkPickTokenRange faces))
            .IsTrue();
        await Assert.That(faces.TokenCount).IsEqualTo(0u);

        // The whole-resource target still answers, which is what a prim pick
        // and the surface depth pass both use.
        await Assert.That(table.TryGetRange(
                "/Curve",
                0,
                SilkPickSubprimKind.Primitive,
                out SilkPickTokenRange whole))
            .IsTrue();
        await Assert.That(whole.TokenCount).IsEqualTo(2u);
    }

    [Test]
    public async Task AResolvedIdentityCarriesTheOrderedInstancingChain()
    {
        var table = new SilkPickIdentityTable();
        SilkMeshData nested = Create(
            "/Proto",
            1,
            SilkTopologyKind.TriangleList,
            [0, 0, 0, 1, 0, 0, 0, 1, 0],
            [0, 1, 2],
            [4],
            instanceId: 11,
            instanceIndex: 17,
            instancerPath: "/World/Outer/Inner",
            instancerContext:
            [
                new SilkInstancerContextEntry("/World/Outer", 2),
                new SilkInstancerContextEntry("/World/Outer/Inner", 5)
            ]);
        SilkPickTokenRange range = table.Upsert(nested);

        await Assert.That(table.TryResolve(
            range.FirstToken,
            out SilkPickIdentity identity)).IsTrue();

        // The composite ordinal keys the retained table and is deliberately not
        // any level's own index; only the chain names a scene instance.
        await Assert.That(identity.InstanceIndex).IsEqualTo(17);
        await Assert.That(identity.InstancerPath).IsEqualTo("/World/Outer/Inner");
        await Assert.That(identity.InstancerContext.Length).IsEqualTo(2);
        await Assert.That(identity.InstancerContext[0].InstancerPath)
            .IsEqualTo("/World/Outer");
        await Assert.That(identity.InstancerContext[0].InstanceIndex).IsEqualTo(2);
        await Assert.That(identity.InstancerContext[1].InstancerPath)
            .IsEqualTo("/World/Outer/Inner");
        await Assert.That(identity.InstancerContext[1].InstanceIndex).IsEqualTo(5);
    }

    [Test]
    public async Task ANonInstancedRecordCarriesNoChain()
    {
        var table = new SilkPickIdentityTable();
        SilkPickTokenRange range = table.Upsert(Triangles("/Mesh", 1));

        await Assert.That(table.TryResolve(
            range.FirstToken,
            out SilkPickIdentity identity)).IsTrue();
        await Assert.That(identity.InstancerPath).IsNull();
        await Assert.That(identity.InstancerContext.Length).IsEqualTo(0);
    }

    private static SilkMeshData Triangles(string path, int primId) =>
        Create(
            path,
            primId,
            SilkTopologyKind.TriangleList,
            [0, 0, 0, 1, 0, 0, 0, 1, 0],
            [0, 1, 2],
            [4]);

    private static SilkMeshData Lines(string path, int primId) =>
        Create(
            path,
            primId,
            SilkTopologyKind.LineList,
            [0, 0, 0, 1, 0, 0, 2, 0, 0],
            [0, 1, 1, 2],
            [0, 1]);

    private static SilkMeshData Points(string path, int primId) =>
        Create(
            path,
            primId,
            SilkTopologyKind.PointList,
            [0, 0, 0, 1, 0, 0, 2, 0, 0],
            [0, 1, 2],
            [0, 1, 2]);

    private static SilkMeshData Create(
        string path,
        int primId,
        SilkTopologyKind topologyKind,
        float[] points,
        uint[] indices,
        int[] primitiveSubprims,
        int instanceId = 0,
        int instanceIndex = 0,
        string? instancerPath = null,
        SilkInstancerContextEntry[]? instancerContext = null) =>
        new(
            primId,
            path,
            SilkWireFormat.ComputeStableHash(path),
            instanceId,
            instanceIndex,
            topologyKind,
            1,
            points,
            indices,
            primitiveSubprims,
            [1, 1, 1, 1],
            [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1])
        {
            InstancerPath = instancerPath ?? string.Empty,
            InstancerContext = instancerContext ?? []
        };
}
