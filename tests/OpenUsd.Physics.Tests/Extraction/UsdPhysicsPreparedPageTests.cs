// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Physics.Extraction;

namespace OpenUsd.Physics.Tests.Extraction;

public sealed class UsdPhysicsPreparedPageTests
{
    private static byte[] CreatePage()
    {
        var fixture = new UsdPhysicsExtractionPageFixture();
        int collider = fixture.AddObject(1, "/Prepared", "Prepared", UsdPhysicsExtractionObjectKind.Collider,
            UsdPhysicsExtractionDomains.Collision,
            UsdPhysicsExtractionObjectTraits.Enabled | UsdPhysicsExtractionObjectTraits.PreparedPointsMetres,
            UsdPhysicsExtractionGeometryKind.ConvexMesh);
        fixture.AddPoint(0, 0, 0);
        fixture.AddPoint(1, 0, 0);
        fixture.AddPoint(0, 1, 0);
        fixture.AddPoint(0, 0, 1);
        fixture.SetObjectGeometry(collider, 0, 4, 0, 0);
        fixture.AddHull(0, 4);
        fixture.SetObjectHulls(collider, 0, 1);
        return fixture.Build();
    }

    [Test]
    public async Task PreparedHullRangesSurviveThePublicPageBoundary()
    {
        UsdPhysicsExtractionPage page = UsdPhysicsExtractionPage.Create(CreatePage());
        await Assert.That(page.HullCount).IsEqualTo(1);
        await Assert.That(page.GetHull(0)).IsEqualTo((0, 4));
        await Assert.That(page.GetObject(0).HullCount).IsEqualTo(1);
        await Assert.That(page.GetObject(0).Flags.HasFlag(
            UsdPhysicsExtractionObjectTraits.PreparedPointsMetres)).IsTrue();
    }

    [Test]
    [Arguments("missing-flags")]
    [Arguments("missing-hulls")]
    [Arguments("outside-hull-section")]
    [Arguments("wrong-geometry")]
    [Arguments("short-hull")]
    [Arguments("outside-points")]
    public async Task InvalidPreparedHullDeclarationsAreRefused(string corruption)
    {
        byte[] bytes = CreatePage();
        int objects = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(PhysicsExtractAbi.OffsetSpans + PhysicsExtractAbi.SectionObjects * 8));
        int hulls = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(PhysicsExtractAbi.OffsetSpans + PhysicsExtractAbi.SectionHulls * 8));
        (int offset, uint value) = corruption switch
        {
            "missing-flags" => (objects + 44, (uint)UsdPhysicsExtractionObjectTraits.Enabled),
            "missing-hulls" => (objects + 212, 0u),
            "outside-hull-section" => (objects + 208, 1u),
            "wrong-geometry" => (objects + 48, (uint)UsdPhysicsExtractionGeometryKind.Mesh),
            "short-hull" => (hulls + 4, 3u),
            "outside-points" => (hulls, 1u),
            _ => throw new ArgumentOutOfRangeException(nameof(corruption)),
        };
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        await Assert.That(() => UsdPhysicsExtractionPage.Create(bytes)).Throws<UsdPhysicsExtractionException>();
    }
}
