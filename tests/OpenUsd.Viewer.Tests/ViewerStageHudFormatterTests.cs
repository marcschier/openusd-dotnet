// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerStageHudFormatterTests
{
    [Test]
    public async Task StageHudReportsGeometryCountsTimingsAndAabb()
    {
        var statistics = new ViewerStageStatisticsSnapshot(
            "root.usda",
            "session.usda",
            "/World",
            PrimCount: 9,
            MeshCount: 2,
            CurveVertexCount: 5,
            MeshVertexCount: 8,
            FaceCount: 3,
            RootPrimCount: 1,
            LeafPrimCount: 4,
            MaximumDepth: 3,
            new UsdBounds3d(
                new UsdVec3d(-1, -2, -3),
                new UsdVec3d(4, 5, 6)),
            new UsdOrientedBounds3d(
                new UsdBounds3d(
                    new UsdVec3d(-2, -3, -4),
                    new UsdVec3d(5, 6, 7)),
                UsdMatrix4d.Identity),
            TimeSpan.FromMilliseconds(1.5));
        ViewerStageTimingSnapshot timing = ViewerStageTimingSnapshot.Create(1, 24, 24, 24);
        var diagnostics = new ViewerDiagnosticsSnapshot(
            DateTimeOffset.UnixEpoch,
            "hdSilk",
            ViewerBackendRuntimeIdentity.Unknown,
            "None",
            TimeSpan.FromMilliseconds(4),
            TimeSpan.FromMilliseconds(2),
            DrawCalls: 7,
            Triangles: 11,
            RetiredCleanupCount: 0,
            StateRevision: 2,
            default,
            [],
            []);

        string hud = ViewerStageHudFormatter.FormatStatistics(statistics, timing, diagnostics);

        await Assert.That(hud).Contains("Traversable prims: 9");
        await Assert.That(hud).Contains("Meshes: 2; mesh vertices: 8; curve CVs: 5; faces: 3");
        await Assert.That(hud).Contains("Playback: 1..24; FPS 24; TCPS 24");
        await Assert.That(hud).Contains("Render: CPU 4 ms; GPU 2 ms; draws 7; triangles 11");
        await Assert.That(hud).Contains("AABB: [(-1, -2, -3) .. (4, 5, 6)]; OBB: ");
        await Assert.That(hud).Contains("[(-2, -3, -4) .. (5, 6, 7)]");
        await Assert.That(hud).Contains("bbox query 1.5 ms");
    }
}
