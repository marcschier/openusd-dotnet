// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Geom;
using OpenUsd.Viewer;

namespace OpenUsd.NativeCoverage.Tests;

public sealed class StageStatisticsAnalysisNativeCoverageTests
{
    [Test]
    public async Task ViewerStatisticsClassifyMeshAllCurveSchemasAndDefaultPrimStates()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(ViewerStatisticsClassifyMeshAllCurveSchemasAndDefaultPrimStates));
        string stagePath = Path.Combine(directory, "statistics.usda");
        using UsdStage stage = UsdStage.Create(stagePath);
        stage.StartTimeCode = 2.0d;

        stage.DefineXform("/World");

        UsdGeomMesh mesh = stage.DefineMesh("/World/Mesh");
        mesh.SetPoints(
        [
            new UsdVec3f(-10, -8, -2),
            new UsdVec3f(10, -8, -2),
            new UsdVec3f(10, 8, 2),
            new UsdVec3f(-10, 8, 2)
        ]);
        mesh.SetTopology([4], [0, 1, 2, 3]);

        UsdGeomBasisCurves basis = stage.DefineBasisCurves("/World/Basis");
        basis.SetPoints(
        [
            new UsdVec3f(-1, 0, 0),
            new UsdVec3f(0, 1, 0),
            new UsdVec3f(1, 0, 0)
        ]);
        basis.SetCurveVertexCounts([3]);

        UsdGeomHermiteCurves hermite = stage.DefineHermiteCurves("/World/Hermite");
        hermite.SetPoints(
        [
            new UsdVec3f(-1.5f, 0, 0),
            new UsdVec3f(-0.5f, 1, 0),
            new UsdVec3f(0.5f, 1, 0),
            new UsdVec3f(1.5f, 0, 0)
        ]);
        hermite.SetCurveVertexCounts([4]);

        UsdGeomNurbsCurves nurbs = stage.DefineNurbsCurves("/World/Nurbs");
        nurbs.SetPoints(
        [
            new UsdVec3f(-2, -1, 0),
            new UsdVec3f(-1, 0, 0.5f),
            new UsdVec3f(0, 1, 0),
            new UsdVec3f(1, 0, -0.5f),
            new UsdVec3f(2, -1, 0)
        ]);
        nurbs.SetCurveVertexCounts([5]);

        stage.DefinePrim("/World/Scope", "Scope");
        stage.SetDefaultPrim("/World");

        string rootLayerIdentifier = stage.RootLayerIdentifier;
        string sessionLayerIdentifier = stage.SessionLayerIdentifier;
        var expectedWorldBounds = new UsdBounds3d(
            new UsdVec3d(-10, -8, -2),
            new UsdVec3d(10, 8, 2));

        ViewerStageStatisticsSnapshot withDefaultPrim =
            ViewerStageSnapshotBuilder.BuildDocument(stage).Statistics;

        await AssertExactStatisticsAsync(
            withDefaultPrim,
            rootLayerIdentifier,
            sessionLayerIdentifier,
            "/World",
            expectedWorldBounds);
        await Assert.That(Path.GetFileName(rootLayerIdentifier)).IsEqualTo("statistics.usda");
        await Assert.That(sessionLayerIdentifier).IsNotEmpty();
        await Assert.That(sessionLayerIdentifier).IsNotEqualTo(rootLayerIdentifier);

        stage.ClearDefaultPrim();

        ViewerStageStatisticsSnapshot withoutDefaultPrim =
            ViewerStageSnapshotBuilder.BuildDocument(stage).Statistics;

        await AssertExactStatisticsAsync(
            withoutDefaultPrim,
            rootLayerIdentifier,
            sessionLayerIdentifier,
            string.Empty,
            expectedWorldBounds);
        await Assert.That(withoutDefaultPrim.WorldBounds).IsEqualTo(withDefaultPrim.WorldBounds);
        await Assert.That(withoutDefaultPrim.OrientedWorldBounds)
            .IsEqualTo(withDefaultPrim.OrientedWorldBounds);
    }

    private static async Task AssertExactStatisticsAsync(
        ViewerStageStatisticsSnapshot statistics,
        string expectedRootLayerIdentifier,
        string expectedSessionLayerIdentifier,
        string expectedDefaultPrimPath,
        UsdBounds3d expectedWorldBounds)
    {
        await Assert.That(statistics.RootLayerIdentifier)
            .IsEqualTo(expectedRootLayerIdentifier);
        await Assert.That(statistics.SessionLayerIdentifier)
            .IsEqualTo(expectedSessionLayerIdentifier);
        await Assert.That(statistics.DefaultPrimPath).IsEqualTo(expectedDefaultPrimPath);
        await Assert.That(statistics.PrimCount).IsEqualTo(6);
        await Assert.That(statistics.MeshCount).IsEqualTo(1);
        await Assert.That(statistics.MeshVertexCount).IsEqualTo(4L);
        await Assert.That(statistics.FaceCount).IsEqualTo(1L);
        await Assert.That(statistics.CurveVertexCount).IsEqualTo(12L);
        await Assert.That(statistics.RootPrimCount).IsEqualTo(1);
        await Assert.That(statistics.LeafPrimCount).IsEqualTo(5);
        await Assert.That(statistics.MaximumDepth).IsEqualTo(1);
        await Assert.That(statistics.WorldBounds).IsEqualTo(expectedWorldBounds);
        await Assert.That(statistics.WorldBounds.IsEmpty).IsFalse();
        await Assert.That(statistics.OrientedWorldBounds.IsEmpty).IsFalse();
        await Assert.That(statistics.BoundsQueryDuration)
            .IsGreaterThanOrEqualTo(TimeSpan.Zero);
    }
}
