// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Numerics;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkCubicCurveNativeTests
{
    private const int ChordsPerSpan = 8;

    private static readonly Vector3[] OpenPoints =
    [
        new(-0.875f, -0.5f, 0), new(-0.75f, 0.5f, 0),
        new(-0.25f, 0.75f, 0), new(0, 0, 0),
        new(0.25f, -0.75f, 0), new(0.75f, -0.5f, 0), new(0.875f, 0.5f, 0),
        new(-0.625f, -0.75f, 0), new(-0.25f, -0.875f, 0),
        new(0.25f, -0.875f, 0), new(0.625f, -0.75f, 0)
    ];

    private static readonly Vector3[] PeriodicPoints =
    [
        new(-0.75f, 0, 0), new(-0.75f, 0.75f, 0), new(-0.25f, 0.75f, 0),
        new(0, 0, 0), new(0.25f, -0.75f, 0), new(0.75f, -0.75f, 0),
        new(0.375f, 0.25f, 0), new(0.625f, 0.75f, 0), new(0.875f, 0.25f, 0)
    ];

    private static readonly Vector3[] ShortPinnedPoints =
    [
        new(-0.875f, -0.5f, 0), new(-0.5f, 0.875f, 0),
        new(0.125f, -0.75f, 0), new(0.5f, 0.875f, 0), new(0.875f, -0.25f, 0)
    ];

    [Test]
    [Arguments("nonperiodic", "constant", false)]
    [Arguments("nonperiodic", "uniform", false)]
    [Arguments("nonperiodic", "varying", false)]
    [Arguments("nonperiodic", "vertex", false)]
    [Arguments("periodic", "constant", false)]
    [Arguments("periodic", "uniform", false)]
    [Arguments("periodic", "varying", false)]
    [Arguments("periodic", "vertex", false)]
    [Arguments("pinned", "constant", false)]
    [Arguments("pinned", "uniform", false)]
    [Arguments("pinned", "varying", false)]
    [Arguments("pinned", "vertex", false)]
    [Arguments("nonperiodic", "constant", true)]
    [Arguments("nonperiodic", "uniform", true)]
    [Arguments("nonperiodic", "varying", true)]
    [Arguments("nonperiodic", "vertex", true)]
    [Arguments("periodic", "constant", true)]
    [Arguments("periodic", "uniform", true)]
    [Arguments("periodic", "varying", true)]
    [Arguments("periodic", "vertex", true)]
    [Arguments("pinned", "constant", true)]
    [Arguments("pinned", "uniform", true)]
    [Arguments("pinned", "varying", true)]
    [Arguments("pinned", "vertex", true)]
    public async Task CubicBezierSamplesAndWidthsMatchIndependentBasis(
        string wrap, string interpolation, bool bounded)
    {
        Vector3[] points = wrap == "periodic" ? PeriodicPoints : OpenPoints;
        int[] counts = wrap == "periodic" ? [6, 3] : [7, 4];
        float[] widths = Widths(interpolation, counts, wrap, points.Length);
        CurveReference expected = Reference(points, counts, wrap, interpolation, widths);
        using var fixture = new Fixture(Curve(points, counts, wrap, interpolation, widths));
        using OpenUsdSilkSession session = fixture.Open();
        using OpenUsdSilkPage page = bounded
            ? session.Sync(16, 16, Options(1_000_000))
            : session.Sync(16, 16);
        using var scene = new SilkSceneState();
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkVertexAttributeData attribute = mesh.Attributes.Single(entry => entry.Name == "widths");

        await Assert.That(mesh.TopologyKind).IsEqualTo(SilkTopologyKind.LineList);
        await Assert.That(mesh.PrimId).IsGreaterThanOrEqualTo(0);
        await Assert.That(mesh.SubprimIdentity).IsEqualTo(SilkSubprimIdentity.None);
        await Assert.That(mesh.SubprimUnsupported.HasFlag(SilkSubprimUnsupportedReason.TopologyMode)).IsTrue();
        await Assert.That(mesh.Points.Length).IsEqualTo(expected.Points.Length);
        await Assert.That(MaxDifference(mesh.Points.ToArray(), expected.Points)).IsLessThanOrEqualTo(0.000001f);
        await Assert.That(mesh.Indices.ToArray().SequenceEqual(expected.Indices)).IsTrue();
        await Assert.That(mesh.TriangleSubprims.ToArray().SequenceEqual(expected.Subprims)).IsTrue();
        await Assert.That(attribute.Semantic).IsEqualTo(SilkAttributeSemantic.Width);
        await Assert.That(attribute.ComponentCount).IsEqualTo(1);
        await Assert.That(attribute.Interpolation).IsEqualTo(interpolation == "constant"
            ? SilkAttributeInterpolation.Constant : SilkAttributeInterpolation.Vertex);
        await Assert.That(attribute.Data.Length).IsEqualTo(expected.Widths.Length);
        await Assert.That(MaxDifference(attribute.Data.ToArray(), expected.Widths)).IsLessThanOrEqualTo(0.000001f);

        float actualMidpoint = mesh.Points.ToArray()[((3 * 2 + 1) * 3) + 1];
        float chordMidpoint = (points[0].Y + points[3].Y) * 0.5f;
        await Assert.That(MathF.Abs(actualMidpoint - chordMidpoint)).IsGreaterThan(0.1f);
        if (wrap == "periodic")
        {
            float[] emitted = mesh.Points.ToArray();
            int secondSpanEnd = ((2 * ChordsPerSpan * 2) - 1) * 3;
            await Assert.That(emitted[secondSpanEnd]).IsEqualTo(points[0].X);
            await Assert.That(emitted[secondSpanEnd + 1]).IsEqualTo(points[0].Y);
            int finalEnd = emitted.Length - 3;
            await Assert.That(emitted[finalEnd]).IsEqualTo(points[6].X);
            await Assert.That(emitted[finalEnd + 1]).IsEqualTo(points[6].Y);
        }
    }

    [Test]
    public async Task CubicBezierVertexWidthsAcceptUnusedTrailingPoints()
    {
        Vector3[] points = [.. OpenPoints.Take(4), new(0.5f, 0.5f, 0), new(0.75f, 0.75f, 0)];
        float[] widths = [0.125f, 0.75f, 0.25f, 1, 0.5f, 0.875f];
        CurveReference expected = Reference(points, [4], "nonperiodic", "vertex", widths);
        using var fixture = new Fixture(Curve(points, [4], "nonperiodic", "vertex", widths));
        using OpenUsdSilkSession session = fixture.Open();
        using OpenUsdSilkPage page = session.Sync(16, 16, Options(704));
        using var scene = new SilkSceneState();
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkVertexAttributeData attribute = mesh.Attributes.Single(entry => entry.Name == "widths");

        await Assert.That(page.MeshPreparationUsage!.ReservedBytes).IsEqualTo(704ul);
        await Assert.That(MaxDifference(mesh.Points.ToArray(), expected.Points)).IsLessThanOrEqualTo(0.000001f);
        await Assert.That(MaxDifference(attribute.Data.ToArray(), expected.Widths)).IsLessThanOrEqualTo(0.000001f);
    }

    [Test]
    [Arguments("two", "constant", false)]
    [Arguments("two", "uniform", false)]
    [Arguments("two", "varying", false)]
    [Arguments("two", "vertex", false)]
    [Arguments("two", "constant", true)]
    [Arguments("two", "uniform", true)]
    [Arguments("two", "varying", true)]
    [Arguments("two", "vertex", true)]
    [Arguments("three", "constant", false)]
    [Arguments("three", "uniform", false)]
    [Arguments("three", "varying", false)]
    [Arguments("three", "vertex", false)]
    [Arguments("three", "constant", true)]
    [Arguments("three", "uniform", true)]
    [Arguments("three", "varying", true)]
    [Arguments("three", "vertex", true)]
    [Arguments("pair", "constant", false)]
    [Arguments("pair", "uniform", false)]
    [Arguments("pair", "varying", false)]
    [Arguments("pair", "vertex", false)]
    [Arguments("pair", "constant", true)]
    [Arguments("pair", "uniform", true)]
    [Arguments("pair", "varying", true)]
    [Arguments("pair", "vertex", true)]
    public async Task PinnedBezierShortControlsMatchRepeatedLastControlReference(
        string layout, string interpolation, bool bounded)
    {
        int[] counts = layout switch
        {
            "two" => [2],
            "three" => [3],
            "pair" => [2, 3],
            _ => throw new ArgumentOutOfRangeException(nameof(layout))
        };
        int[] expandedIndices = layout switch
        {
            "two" => [0, 1, 1, 1],
            "three" => [0, 1, 2, 2],
            "pair" => [0, 1, 1, 1, 2, 3, 4, 4],
            _ => throw new ArgumentOutOfRangeException(nameof(layout))
        };
        Vector3[] points = [.. ShortPinnedPoints.Take(counts.Sum())];
        float[] widths = interpolation switch
        {
            "constant" => [0.375f],
            "uniform" => counts.Length == 1 ? [0.25f] : [0.25f, 0.75f],
            "varying" => counts.Length == 1
                ? [0.125f, 0.875f] : [0.125f, 0.375f, 0.625f, 0.875f],
            "vertex" => Enumerable.Range(1, points.Length).Select(index => index / 8f).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(interpolation))
        };
        Vector3[] expandedPoints = expandedIndices.Select(index => points[index]).ToArray();
        float[] expandedWidths = interpolation == "vertex"
            ? expandedIndices.Select(index => widths[index]).ToArray() : widths;
        CurveReference expected = Reference(
            expandedPoints, Enumerable.Repeat(4, counts.Length).ToArray(),
            "pinned", interpolation, expandedWidths);
        ulong cost = (ulong)(points.Length * 12 + widths.Length * 4 + counts.Length * ChordsPerSpan * 76);
        using var fixture = new Fixture(Curve(points, counts, "pinned", interpolation, widths));
        using OpenUsdSilkSession session = fixture.Open();
        using OpenUsdSilkPage page = bounded
            ? session.Sync(16, 16, Options(cost))
            : session.Sync(16, 16);
        using var scene = new SilkSceneState();
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkVertexAttributeData attribute = mesh.Attributes.Single(entry => entry.Name == "widths");
        float[] emitted = mesh.Points.ToArray();

        if (bounded)
        {
            await Assert.That(page.MeshPreparationUsage!.ReservedBytes).IsEqualTo(cost);
            await Assert.That(page.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(cost);
        }
        await Assert.That(mesh.Path).IsEqualTo("/Curve");
        await Assert.That(mesh.PrimId).IsGreaterThanOrEqualTo(0);
        await Assert.That(mesh.TopologyKind).IsEqualTo(SilkTopologyKind.LineList);
        await Assert.That(mesh.SubprimIdentity).IsEqualTo(SilkSubprimIdentity.None);
        await Assert.That(mesh.SubprimUnsupported.HasFlag(SilkSubprimUnsupportedReason.TopologyMode)).IsTrue();
        await Assert.That(MaxDifference(emitted, expected.Points)).IsLessThanOrEqualTo(0.000001f);
        await Assert.That(mesh.Indices.ToArray().SequenceEqual(expected.Indices)).IsTrue();
        await Assert.That(mesh.TriangleSubprims.ToArray().SequenceEqual(expected.Subprims)).IsTrue();
        await Assert.That(attribute.Semantic).IsEqualTo(SilkAttributeSemantic.Width);
        await Assert.That(attribute.ComponentCount).IsEqualTo(1);
        await Assert.That(attribute.Interpolation).IsEqualTo(interpolation == "constant"
            ? SilkAttributeInterpolation.Constant : SilkAttributeInterpolation.Vertex);
        await Assert.That(MaxDifference(attribute.Data.ToArray(), expected.Widths)).IsLessThanOrEqualTo(0.000001f);
        await Assert.That(emitted[^3]).IsEqualTo(points[^1].X);
        await Assert.That(emitted[^2]).IsEqualTo(points[^1].Y);
    }

    [Test]
    [Arguments("nonperiodic", 2, false)]
    [Arguments("nonperiodic", 2, true)]
    [Arguments("nonperiodic", 3, false)]
    [Arguments("nonperiodic", 3, true)]
    [Arguments("periodic", 2, false)]
    [Arguments("periodic", 2, true)]
    public async Task ShortBezierFallbackRemainsScopedToPinnedWrap(
        string wrap, int controlCount, bool bounded)
    {
        Vector3[] points = [.. ShortPinnedPoints.Take(controlCount)];
        float[] widths = Enumerable.Repeat(0.25f, controlCount).ToArray();
        ulong cost = (ulong)(controlCount * (12 + 4));
        using var fixture = new Fixture(Curve(points, [controlCount], wrap, "vertex", widths));
        using OpenUsdSilkSession session = fixture.Open();
        using OpenUsdSilkPage page = bounded
            ? session.Sync(16, 16, Options(cost))
            : session.Sync(16, 16);
        using var scene = new SilkSceneState();
        _ = scene.Apply(page);

        await Assert.That(scene.Meshes.Count).IsEqualTo(0);
        if (bounded)
        {
            await Assert.That(page.MeshPreparationUsage!.ReservedBytes).IsEqualTo(cost);
        }
    }

    [Test]
    public async Task PinnedBezierShortControlEditsPreserveBudgetAndIdentity()
    {
        Vector3[] two = [.. ShortPinnedPoints.Take(2)];
        Vector3[] three = [.. ShortPinnedPoints.Take(3)];
        float[] twoWidths = [0.125f, 0.875f];
        float[] threeWidths = [0.125f, 0.875f, 0.5f];
        const ulong twoCost = 2 * 12 + 2 * 4 + ChordsPerSpan * 76;
        const ulong threeCost = 3 * 12 + 3 * 4 + ChordsPerSpan * 76;
        const ulong oneCost = 12 + 4;
        const ulong refusalLimit = twoCost + threeCost - 1;
        const ulong retryLimit = threeCost * 2;
        using var fixture = new Fixture(Curve(two, [2], "pinned", "vertex", twoWidths));
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();

        using OpenUsdSilkPage initial = session.Sync(16, 16, Options(refusalLimit));
        _ = scene.Apply(initial);
        SilkMeshData initialMesh = scene.Meshes.Values.Single();
        float[] initialPoints = initialMesh.Points.ToArray();
        uint[] initialIndices = initialMesh.Indices.ToArray();
        int[] initialSubprims = initialMesh.TriangleSubprims.ToArray();
        float[] initialWidths = initialMesh.Attributes.Single(entry => entry.Name == "widths").Data.ToArray();
        int primId = initialMesh.PrimId;
        ulong initialPageRevision = initial.Revision;
        await Assert.That(initial.MeshPreparationUsage!.MaximumReservedBytes).IsEqualTo(refusalLimit);
        await Assert.That(initial.MeshPreparationUsage!.ReservedBytes).IsEqualTo(twoCost);
        await Assert.That(initial.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(twoCost);
        await Assert.That(initialMesh.Path).IsEqualTo("/Curve");
        await Assert.That(initialMesh.PrimId).IsGreaterThanOrEqualTo(0);
        await Assert.That(initialMesh.TopologyRevision).IsGreaterThan(0ul);

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim curve = stage.GetPrim("/Curve");
            curve.SetInt32Array("curveVertexCounts", [3]);
            curve.SetVec3fArray("points", ToUsd(three));
            curve.SetFloatArray("widths", threeWidths);
        });
        await Assert.That(() => session.Sync(16, 16, Options(refusalLimit)))
            .Throws<OpenUsdSilkException>()
            .WithMessageContaining("hdSilk mesh preparation refused");
        await Assert.That(initial.Revision).IsEqualTo(initialPageRevision);
        await Assert.That(scene.Revision).IsEqualTo(initialPageRevision);
        await AssertSameCurveSnapshot(
            initialMesh, scene.Meshes.Values.Single(), initialPoints, initialIndices, initialSubprims, initialWidths);
        using var oldPageScene = new SilkSceneState();
        _ = oldPageScene.Apply(initial);
        await Assert.That(oldPageScene.Revision).IsEqualTo(initialPageRevision);
        await AssertSameCurveSnapshot(
            initialMesh, oldPageScene.Meshes.Values.Single(), initialPoints, initialIndices, initialSubprims,
            initialWidths);

        using OpenUsdSilkPage retry = session.Sync(16, 16, Options(retryLimit));
        _ = scene.Apply(retry);
        SilkMeshData expanded = scene.Meshes.Values.Single();
        CurveReference threeReference = Reference(
            [three[0], three[1], three[2], three[2]], [4], "pinned", "vertex",
            [threeWidths[0], threeWidths[1], threeWidths[2], threeWidths[2]]);
        await Assert.That(retry.MeshPreparationUsage!.ReservedBytes).IsEqualTo(threeCost);
        await Assert.That(retry.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(threeCost);
        await Assert.That(expanded.PrimId).IsEqualTo(primId);
        await Assert.That(expanded.Path).IsEqualTo(initialMesh.Path);
        await Assert.That(expanded.TopologyRevision).IsGreaterThan(0ul);
        await Assert.That(MaxDifference(expanded.Points.ToArray(), threeReference.Points))
            .IsLessThanOrEqualTo(0.000001f);
        await Assert.That(MaxDifference(
            expanded.Attributes.Single(entry => entry.Name == "widths").Data.ToArray(),
            threeReference.Widths)).IsLessThanOrEqualTo(0.000001f);

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim curve = stage.GetPrim("/Curve");
            curve.SetInt32Array("curveVertexCounts", [2]);
            curve.SetVec3fArray("points", ToUsd(two));
            curve.SetFloatArray("widths", twoWidths);
        });
        using OpenUsdSilkPage shrunk = session.Sync(16, 16, Options(retryLimit));
        _ = scene.Apply(shrunk);
        SilkMeshData shrunkMesh = scene.Meshes.Values.Single();
        await Assert.That(shrunk.MeshPreparationUsage!.ReservedBytes).IsEqualTo(twoCost);
        await Assert.That(shrunk.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(threeCost + twoCost);
        await Assert.That(shrunkMesh.PrimId).IsEqualTo(primId);
        await Assert.That(shrunkMesh.Path).IsEqualTo(initialMesh.Path);
        await Assert.That(shrunkMesh.TopologyRevision).IsGreaterThan(expanded.TopologyRevision);
        await Assert.That(shrunkMesh.Points.ToArray().SequenceEqual(initialPoints)).IsTrue();
        await Assert.That(shrunkMesh.Attributes.Single(entry => entry.Name == "widths").Data.Span.SequenceEqual(
            initialMesh.Attributes.Single(entry => entry.Name == "widths").Data.Span)).IsTrue();

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim curve = stage.GetPrim("/Curve");
            curve.SetInt32Array("curveVertexCounts", [3]);
            curve.SetVec3fArray("points", ToUsd(three));
            curve.SetFloatArray("widths", threeWidths);
        });
        using OpenUsdSilkPage reexpanded = session.Sync(16, 16, Options(retryLimit));
        _ = scene.Apply(reexpanded);
        SilkMeshData reexpandedMesh = scene.Meshes.Values.Single();
        await Assert.That(reexpanded.MeshPreparationUsage!.ReservedBytes).IsEqualTo(threeCost);
        await Assert.That(reexpanded.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(twoCost + threeCost);
        await Assert.That(reexpandedMesh.PrimId).IsEqualTo(primId);
        await Assert.That(reexpandedMesh.Path).IsEqualTo(initialMesh.Path);
        await Assert.That(reexpandedMesh.TopologyRevision).IsGreaterThan(shrunkMesh.TopologyRevision);
        await Assert.That(MaxDifference(reexpandedMesh.Points.ToArray(), threeReference.Points))
            .IsLessThanOrEqualTo(0.000001f);
        await Assert.That(MaxDifference(
            reexpandedMesh.Attributes.Single(entry => entry.Name == "widths").Data.ToArray(),
            threeReference.Widths)).IsLessThanOrEqualTo(0.000001f);

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Curve").SetToken("wrap", "periodic"));
        using OpenUsdSilkPage closed = session.Sync(16, 16, Options(retryLimit));
        _ = scene.Apply(closed);
        SilkMeshData periodic = scene.Meshes.Values.Single();
        float[] closedPoints = periodic.Points.ToArray();
        CurveReference periodicReference = Reference(three, [3], "periodic", "vertex", threeWidths);
        await Assert.That(closed.MeshPreparationUsage!.ReservedBytes).IsEqualTo(threeCost);
        await Assert.That(closed.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(threeCost * 2);
        await Assert.That(periodic.PrimId).IsEqualTo(primId);
        await Assert.That(periodic.TopologyRevision).IsGreaterThan(reexpandedMesh.TopologyRevision);
        await Assert.That(MaxDifference(closedPoints, periodicReference.Points)).IsLessThanOrEqualTo(0.000001f);
        await Assert.That(MaxDifference(
            periodic.Attributes.Single(entry => entry.Name == "widths").Data.ToArray(),
            periodicReference.Widths)).IsLessThanOrEqualTo(0.000001f);
        await Assert.That(closedPoints.SequenceEqual(reexpandedMesh.Points.ToArray())).IsFalse();
        await Assert.That(closedPoints[^3]).IsEqualTo(three[0].X);
        await Assert.That(closedPoints[^2]).IsEqualTo(three[0].Y);

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim curve = stage.GetPrim("/Curve");
            curve.SetToken("wrap", "pinned");
            curve.SetInt32Array("curveVertexCounts", [1]);
            curve.SetVec3fArray("points", ToUsd([two[0]]));
            curve.SetFloatArray("widths", [twoWidths[0]]);
        });
        using OpenUsdSilkPage invalid = session.Sync(16, 16, Options(retryLimit));
        _ = scene.Apply(invalid);
        await Assert.That(scene.Meshes.Count).IsEqualTo(0);
        await Assert.That(invalid.MeshPreparationUsage!.ReservedBytes).IsEqualTo(oneCost);
        await Assert.That(invalid.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(threeCost + oneCost);

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim curve = stage.GetPrim("/Curve");
            curve.SetInt32Array("curveVertexCounts", [2]);
            curve.SetVec3fArray("points", ToUsd(two));
            curve.SetFloatArray("widths", twoWidths);
        });
        using OpenUsdSilkPage restored = session.Sync(16, 16, Options(retryLimit));
        _ = scene.Apply(restored);
        SilkMeshData restoredMesh = scene.Meshes.Values.Single();
        await Assert.That(restored.MeshPreparationUsage!.ReservedBytes).IsEqualTo(twoCost);
        await Assert.That(restored.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(oneCost + twoCost);
        await Assert.That(restoredMesh.PrimId).IsEqualTo(primId);
        await Assert.That(restoredMesh.TopologyRevision).IsGreaterThan(periodic.TopologyRevision);
        await Assert.That(restoredMesh.Points.ToArray().SequenceEqual(initialPoints)).IsTrue();
        await Assert.That(restoredMesh.SubprimUnsupported.HasFlag(SilkSubprimUnsupportedReason.TopologyMode))
            .IsTrue();

        await scheduler.InvokeAsync(stage => stage.RemovePrim("/Curve"));
        using OpenUsdSilkPage removed = session.Sync(16, 16, Options(retryLimit));
        _ = scene.Apply(removed);
        await Assert.That(scene.Meshes.Count).IsEqualTo(0);
        await Assert.That(removed.MeshPreparationUsage!.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CubicBezierPointTopologyAndUnsupportedEditsRestoreIdentity(bool bounded)
    {
        Vector3[] original = [.. OpenPoints.Take(4)];
        float[] widths = [0.125f, 0.75f, 0.25f, 1];
        using var fixture = new Fixture(Curve(original, [4], "nonperiodic", "vertex", widths));
        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();

        using OpenUsdSilkPage first = Sync(session, bounded);
        _ = scene.Apply(first);
        SilkMeshData initial = scene.Meshes.Values.Single();
        float[] initialPoints = initial.Points.ToArray();
        int primId = initial.PrimId;
        ulong initialRevision = initial.TopologyRevision;
        Vector3[] edited = [.. original];
        edited[1] = new Vector3(-0.5f, 0.875f, 0);
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Curve").SetVec3fArray(
            "points", ToUsd(edited)));
        using OpenUsdSilkPage moved = Sync(session, bounded);
        _ = scene.Apply(moved);
        SilkMeshData movedMesh = scene.Meshes.Values.Single();
        await Assert.That(movedMesh.PrimId).IsEqualTo(primId);
        await Assert.That(movedMesh.TopologyRevision).IsEqualTo(initialRevision);
        await Assert.That(movedMesh.Points.ToArray().SequenceEqual(initialPoints)).IsFalse();
        await Assert.That(MaxDifference(
            movedMesh.Points.ToArray(), Reference(edited, [4], "nonperiodic", "vertex", widths).Points))
            .IsLessThanOrEqualTo(0.000001f);

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Curve").SetInt32Array("curveVertexCounts", [5]));
        using OpenUsdSilkPage invalid = Sync(session, bounded);
        _ = scene.Apply(invalid);
        await Assert.That(scene.Meshes.Count).IsEqualTo(0);

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim curve = stage.GetPrim("/Curve");
            curve.SetInt32Array("curveVertexCounts", [4]);
            curve.SetVec3fArray("points", ToUsd(original));
        });
        using OpenUsdSilkPage restored = Sync(session, bounded);
        _ = scene.Apply(restored);
        SilkMeshData restoredMesh = scene.Meshes.Values.Single();
        await Assert.That(restoredMesh.PrimId).IsEqualTo(primId);
        await Assert.That(restoredMesh.TopologyRevision).IsGreaterThan(initialRevision);
        await Assert.That(restoredMesh.Points.ToArray().SequenceEqual(initialPoints)).IsTrue();

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim curve = stage.GetPrim("/Curve");
            curve.SetToken("wrap", "periodic");
            curve.SetInt32Array("curveVertexCounts", [3]);
            curve.SetVec3fArray("points", ToUsd(original.Take(3).ToArray()));
            curve.SetFloatArray("widths", widths.AsSpan(0, 3));
        });
        using OpenUsdSilkPage closed = Sync(session, bounded);
        _ = scene.Apply(closed);
        SilkMeshData periodic = scene.Meshes.Values.Single();
        float[] periodicPoints = periodic.Points.ToArray();
        await Assert.That(periodic.PrimId).IsEqualTo(primId);
        await Assert.That(periodic.TopologyRevision).IsGreaterThan(restoredMesh.TopologyRevision);
        await Assert.That(periodicPoints[0]).IsEqualTo(periodicPoints[^3]);
        await Assert.That(periodicPoints[1]).IsEqualTo(periodicPoints[^2]);

        await scheduler.InvokeAsync(stage =>
        {
            UsdPrim curve = stage.GetPrim("/Curve");
            curve.SetToken("wrap", "pinned");
            curve.SetInt32Array("curveVertexCounts", [4]);
            curve.SetVec3fArray("points", ToUsd(original));
            curve.SetFloatArray("widths", widths);
        });
        using OpenUsdSilkPage pinned = Sync(session, bounded);
        _ = scene.Apply(pinned);
        await Assert.That(scene.Meshes.Values.Single().Points.ToArray().SequenceEqual(initialPoints)).IsTrue();

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Curve").SetToken("basis", "bspline"));
        if (bounded)
        {
            await Assert.That(() => Sync(session, true)).Throws<OpenUsdSilkException>()
                .WithMessageContaining("cubic Bezier");
            await Assert.That(scene.Meshes.Values.Single().Points.ToArray().SequenceEqual(initialPoints)).IsTrue();
        }
        else
        {
            using OpenUsdSilkPage unsupported = Sync(session, false);
            _ = scene.Apply(unsupported);
            await Assert.That(scene.Meshes.Count).IsEqualTo(0);
        }

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Curve").SetToken("basis", "bezier"));
        using OpenUsdSilkPage recovered = Sync(session, bounded);
        _ = scene.Apply(recovered);
        SilkMeshData recoveredMesh = scene.Meshes.Values.Single();
        await Assert.That(recoveredMesh.PrimId).IsEqualTo(primId);
        await Assert.That(recoveredMesh.Points.ToArray().SequenceEqual(initialPoints)).IsTrue();
        await Assert.That(recoveredMesh.SubprimUnsupported.HasFlag(SilkSubprimUnsupportedReason.TopologyMode))
            .IsTrue();
        await Assert.That(await File.ReadAllTextAsync(fixture.Path)).IsEqualTo(fixture.Text);
    }

    [Test]
    public async Task CubicBezierReservationCountsChordsAndPreservesOldPageOnRefusal()
    {
        Vector3[] original = [.. OpenPoints.Take(4)];
        float[] widths = [0.125f, 0.75f, 0.25f, 1];
        using var fixture = new Fixture(Curve(original, [4], "nonperiodic", "vertex", widths));
        const ulong cost = 4 * 12 + 4 * 4 + ChordsPerSpan * 76;
        const ulong refusalLimit = cost * 2 - 1;
        const ulong retryLimit = cost * 2;
        using (OpenUsdSilkSession boundary = fixture.Open())
        {
            await Assert.That(() => boundary.Sync(16, 16, Options(cost - 1)))
                .Throws<OpenUsdSilkException>()
                .WithMessageContaining("hdSilk mesh preparation refused");
            using OpenUsdSilkPage exact = boundary.Sync(16, 16, Options(cost));
            await Assert.That(exact.MeshPreparationUsage!.ReservedBytes).IsEqualTo(cost);
            await Assert.That(exact.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(cost);
        }

        await using UsdStageScheduler scheduler = UsdStageScheduler.Open(fixture.Path);
        using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
        using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(fixture.Plugins, source);
        using var scene = new SilkSceneState();

        using OpenUsdSilkPage initial = session.Sync(16, 16, Options(refusalLimit));
        _ = scene.Apply(initial);
        SilkMeshData initialMesh = scene.Meshes.Values.Single();
        float[] initialPoints = initialMesh.Points.ToArray();
        uint[] initialIndices = initialMesh.Indices.ToArray();
        int[] initialSubprims = initialMesh.TriangleSubprims.ToArray();
        float[] initialWidths = initialMesh.Attributes.Single(entry => entry.Name == "widths").Data.ToArray();
        ulong initialPageRevision = initial.Revision;
        await Assert.That(initial.MeshPreparationUsage!.MaximumReservedBytes).IsEqualTo(refusalLimit);
        await Assert.That(initial.MeshPreparationUsage!.ReservedBytes).IsEqualTo(cost);
        await Assert.That(initial.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(cost);

        Vector3[] changed = [.. original];
        changed[2] = new Vector3(0, 0.875f, 0);
        await scheduler.InvokeAsync(stage => stage.GetPrim("/Curve").SetVec3fArray("points", ToUsd(changed)));
        await Assert.That(() => session.Sync(16, 16, Options(refusalLimit)))
            .Throws<OpenUsdSilkException>()
            .WithMessageContaining("hdSilk mesh preparation refused");
        await Assert.That(initial.Revision).IsEqualTo(initialPageRevision);
        await Assert.That(scene.Revision).IsEqualTo(initialPageRevision);
        await AssertSameCurveSnapshot(
            initialMesh, scene.Meshes.Values.Single(), initialPoints, initialIndices, initialSubprims, initialWidths);
        using var oldPageScene = new SilkSceneState();
        _ = oldPageScene.Apply(initial);
        await Assert.That(oldPageScene.Revision).IsEqualTo(initialPageRevision);
        await AssertSameCurveSnapshot(
            initialMesh, oldPageScene.Meshes.Values.Single(), initialPoints, initialIndices, initialSubprims,
            initialWidths);

        using OpenUsdSilkPage retry = session.Sync(16, 16, Options(retryLimit));
        _ = scene.Apply(retry);
        SilkMeshData updated = scene.Meshes.Values.Single();
        await Assert.That(retry.MeshPreparationUsage!.ReservedBytes).IsEqualTo(cost);
        await Assert.That(retry.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(cost);
        await Assert.That(updated.Path).IsEqualTo(initialMesh.Path);
        await Assert.That(updated.PrimId).IsEqualTo(initialMesh.PrimId);
        await Assert.That(MaxDifference(
            updated.Points.ToArray(),
            Reference(changed, [4], "nonperiodic", "vertex", widths).Points))
            .IsLessThanOrEqualTo(0.000001f);

        await scheduler.InvokeAsync(stage => stage.GetPrim("/Curve").SetVec3fArray("points", ToUsd(original)));
        using OpenUsdSilkPage restored = session.Sync(16, 16, Options(retryLimit));
        _ = scene.Apply(restored);
        SilkMeshData restoredMesh = scene.Meshes.Values.Single();
        await Assert.That(restored.MeshPreparationUsage!.ReservedBytes).IsEqualTo(cost);
        await Assert.That(restored.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(retryLimit);
        await Assert.That(restoredMesh.PrimId).IsEqualTo(updated.PrimId);
        await Assert.That(restoredMesh.Path).IsEqualTo(updated.Path);
        await Assert.That(restoredMesh.TopologyRevision).IsEqualTo(updated.TopologyRevision);
        await Assert.That(restoredMesh.Points.ToArray().SequenceEqual(initialPoints)).IsTrue();
        await Assert.That(restoredMesh.Indices.Span.SequenceEqual(initialIndices)).IsTrue();
        await Assert.That(restoredMesh.TriangleSubprims.Span.SequenceEqual(initialSubprims)).IsTrue();
        await Assert.That(restoredMesh.Attributes.Single(entry => entry.Name == "widths").Data.Span.SequenceEqual(
            initialWidths)).IsTrue();

        using OpenUsdSilkPage quiet = session.Sync(16, 16, Options(retryLimit));
        await Assert.That(quiet.MeshPreparationUsage!.ReservedBytes).IsEqualTo(cost);
        await Assert.That(CountUpserts(quiet)).IsEqualTo(0);

        await scheduler.InvokeAsync(stage => stage.RemovePrim("/Curve"));
        using OpenUsdSilkPage removed = session.Sync(16, 16, Options(retryLimit));
        _ = scene.Apply(removed);
        await Assert.That(scene.Meshes.Count).IsEqualTo(0);
        await Assert.That(removed.MeshPreparationUsage!.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(4, "nonperiodic")]
    [Arguments(2, "pinned")]
    [Arguments(3, "pinned")]
    public async Task CubicBezierInstancesShareOneReservationAndKeepInstanceIdentity(
        int controlCount, string wrap)
    {
        Vector3[] points = [.. OpenPoints.Take(controlCount)];
        float[] allWidths = [0.125f, 0.75f, 0.25f, 1f];
        float[] widths = allWidths[..controlCount];
        int[] controls = controlCount switch
        {
            2 => [0, 1, 1, 1],
            3 => [0, 1, 2, 2],
            4 => [0, 1, 2, 3],
            _ => throw new ArgumentOutOfRangeException(nameof(controlCount))
        };
        CurveReference expected = Reference(
            controls.Select(index => points[index]).ToArray(), [4], wrap, "vertex",
            controls.Select(index => widths[index]).ToArray());
        ulong cost = (ulong)(controlCount * (12 + 4) + ChordsPerSpan * 76);
        string curve = Curve(points, [controlCount], wrap, "vertex", widths);
        using var fixture = new Fixture($$"""
            def PointInstancer "Instances" {
                rel prototypes = </Instances/Curve>
                int[] protoIndices = [0,0,0]
                point3f[] positions = [(0,0,0),(2,0,0),(4,0,0)]
            {{curve}}
            }
            """);
        using OpenUsdSilkSession session = fixture.Open();
        using OpenUsdSilkPage page = session.Sync(16, 16, Options(cost));
        using var scene = new SilkSceneState();
        _ = scene.Apply(page);
        SilkMeshData[] meshes = scene.Meshes.Values.OrderBy(mesh => mesh.InstanceIndex).ToArray();

        await Assert.That(page.MeshPreparationUsage!.ReservedBytes).IsEqualTo(cost);
        await Assert.That(page.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(cost);
        await Assert.That(meshes.Length).IsEqualTo(3);
        await Assert.That(meshes.Select(mesh => mesh.InstanceIndex).SequenceEqual([0, 1, 2])).IsTrue();
        string hydraPath = meshes[0].Path;
        await Assert.That(hydraPath.StartsWith("/Instances/Curve/", StringComparison.Ordinal)).IsTrue();
        await Assert.That(meshes.All(mesh => mesh.Path == hydraPath)).IsTrue();
        await Assert.That(meshes.All(mesh => mesh.InstancerPath == "/Instances")).IsTrue();
        await Assert.That(meshes[0].PrimId).IsGreaterThanOrEqualTo(0);
        await Assert.That(meshes.All(mesh => mesh.PrimId == meshes[0].PrimId)).IsTrue();
        await Assert.That(meshes[0].InstanceId).IsNotEqualTo(0);
        await Assert.That(meshes.All(mesh => mesh.InstanceId == meshes[0].InstanceId)).IsTrue();
        await Assert.That(meshes.Select(mesh => mesh.Id).Distinct().Count()).IsEqualTo(3);
        await Assert.That(meshes.All(mesh => mesh.Points.ToArray().SequenceEqual(meshes[0].Points.ToArray())))
            .IsTrue();
        await Assert.That(MaxDifference(meshes[0].Points.ToArray(), expected.Points)).IsLessThanOrEqualTo(0.000001f);
        await Assert.That(MaxDifference(
            meshes[0].Attributes.Single(entry => entry.Name == "widths").Data.ToArray(), expected.Widths))
            .IsLessThanOrEqualTo(0.000001f);
        IReadOnlyList<SelectionItem> sourceIdentities = session.ResolveSourceIdentities(
            page.Revision, meshes.Select(mesh => (mesh.Path, mesh.InstanceIndex)).ToArray());
        await Assert.That(sourceIdentities.Count).IsEqualTo(meshes.Length);
        for (int index = 0; index < meshes.Length; index++)
        {
            SilkMeshData mesh = meshes[index];
            await Assert.That(mesh.InstancerContext.Count).IsEqualTo(1);
            await Assert.That(mesh.InstancerContext[0].InstancerPath).IsEqualTo("/Instances");
            await Assert.That(mesh.InstancerContext[0].InstanceIndex).IsEqualTo(index);
            await Assert.That(mesh.Transform.Span[12]).IsEqualTo((double)index * 2);
            await Assert.That(scene.MeshesByPath.ContainsKey((hydraPath, index))).IsTrue();
            await Assert.That(sourceIdentities[index].PrimPath).IsEqualTo("/Instances/Curve");
            await Assert.That(sourceIdentities[index].InstancerContext.Count).IsEqualTo(1);
            await Assert.That(sourceIdentities[index].InstancerPath).IsEqualTo("/Instances");
            await Assert.That(sourceIdentities[index].InstanceIndex).IsEqualTo(index);
        }
        int payloads = 0;
        int references = 0;
        int payloadIndex = -1;
        var referenceIndices = new List<int>();
        using (SilkCommandEnumerator commands = page.GetEnumerator())
        {
            while (commands.MoveNext())
            {
                if (commands.Current.Type == SilkCommandType.MeshUpsert)
                {
                    SilkMeshUpsertCommand command = commands.Current.AsMeshUpsert();
                    if (command.PointCount == 0)
                    {
                        references++;
                        referenceIndices.Add(command.InstanceIndex);
                    }
                    else
                    {
                        payloads++;
                        payloadIndex = command.InstanceIndex;
                    }
                }
            }
        }
        await Assert.That(payloads).IsEqualTo(1);
        await Assert.That(references).IsEqualTo(2);
        await Assert.That(payloadIndex).IsEqualTo(0);
        await Assert.That(referenceIndices.OrderBy(index => index).SequenceEqual([1, 2])).IsTrue();
    }

    [Test]
    [Arguments("bspline", "nonperiodic", "")]
    [Arguments("catmullRom", "nonperiodic", "")]
    [Arguments("bezier", "segmented", "")]
    [Arguments("bezier", "nonperiodic", "normal3f[] normals = [(0,0,1)] (interpolation = \"constant\")")]
    [Arguments("bezier", "nonperiodic", "float[] primvars:displayOpacity = [0.5] (interpolation = \"constant\")")]
    [Arguments("bezier", "nonperiodic", "texCoord2f[] primvars:st = [(0,0)] (interpolation = \"constant\")")]
    public async Task CubicBezierUnsupportedInputsRefuseBoundedAndRemoveLegacyGeometry(
        string basis, string wrap, string extra)
    {
        string curve = Curve([.. OpenPoints.Take(4)], [4], wrap, "vertex", [0.125f, 0.75f, 0.25f, 1], extra)
            .Replace("token basis = \"bezier\"", $"token basis = \"{basis}\"", StringComparison.Ordinal);
        using var fixture = new Fixture(curve);
        using OpenUsdSilkSession bounded = fixture.Open();
        await Assert.That(() => bounded.Sync(16, 16, Options(4096))).Throws<OpenUsdSilkException>()
            .WithMessageContaining("cubic Bezier");

        using OpenUsdSilkSession legacy = fixture.Open();
        using OpenUsdSilkPage page = legacy.Sync(16, 16);
        using var scene = new SilkSceneState();
        _ = scene.Apply(page);
        await Assert.That(scene.Meshes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CubicBezierMalformedDeclaredWidthsUseDefaultInsteadOfAnotherInterpolation()
    {
        Vector3[] points = [.. OpenPoints.Take(4)];
        using var fixture = new Fixture(Curve(points, [4], "nonperiodic", "vertex", [0.125f, 0.875f]));
        using OpenUsdSilkSession session = fixture.Open();
        using OpenUsdSilkPage page = session.Sync(16, 16, Options(4096));
        using var scene = new SilkSceneState();
        _ = scene.Apply(page);
        SilkMeshData mesh = scene.Meshes.Values.Single();
        SilkVertexAttributeData width = mesh.Attributes.Single(entry => entry.Name == "widths");
        await Assert.That(width.Interpolation).IsEqualTo(SilkAttributeInterpolation.Constant);
        await Assert.That(width.Data.ToArray().SequenceEqual([1f])).IsTrue();
        await Assert.That(mesh.Indices.Length).IsEqualTo(16);
    }

    [Test]
    [MethodDataSource(nameof(CubicRasterCases))]
    public Task CubicBezierRasterMatchesIndependentSegments(SilkGraphicsBackend backend, bool bounded) =>
        AssertRasterMatchesSegments(backend, bounded);

    public static IEnumerable<(SilkGraphicsBackend, bool)> CubicRasterCases()
    {
        foreach (SilkGraphicsBackend backend in SilkConformanceBackends.Available())
        {
            yield return (backend, false);
            yield return (backend, true);
        }
    }

    private static async Task AssertRasterMatchesSegments(SilkGraphicsBackend backend, bool bounded)
    {
        Vector3[] points = [.. OpenPoints.Take(4)];
        float[] widths = [0.25f];
        CurveReference reference = Reference(points, [4], "nonperiodic", "constant", widths);
        string actualBody = Curve(points, [4], "nonperiodic", "constant", widths);
        string referenceBody = LinearSegments(reference.Points);
        string chordBody = LinearSegments(
            [points[0].X, points[0].Y, points[0].Z, points[3].X, points[3].Y, points[3].Z]);
        using var actualFixture = new Fixture(actualBody);
        using var referenceFixture = new Fixture(referenceBody);
        using var chordFixture = new Fixture(chordBody);
        using OpenUsdSilkSession actual = actualFixture.Open();
        using OpenUsdSilkSession expected = referenceFixture.Open();
        using OpenUsdSilkSession chord = chordFixture.Open();
        using ISilkGraphicsDevice device = SilkDepthCaptureConformance.CreateDevice(backend);
        var budget = new SilkGpuBufferBudget(4_194_304);
        budget.ConfigureDevice(device);
        using var actualRenderer = new SilkMeshRenderer(device);
        using var expectedRenderer = new SilkMeshRenderer(device);
        using var chordRenderer = new SilkMeshRenderer(device);

        byte[] actualPixels = Capture(actual, device, actualRenderer, bounded);
        byte[] expectedPixels = Capture(expected, device, expectedRenderer, bounded);
        byte[] chordPixels = Capture(chord, device, chordRenderer, bounded);
        await Assert.That(actualPixels.SequenceEqual(expectedPixels)).IsTrue();
        await Assert.That(actualPixels.SequenceEqual(chordPixels)).IsFalse();
        await Assert.That(actualPixels.Chunk(4).Select(pixel => Convert.ToHexString(pixel[..3])).Distinct().Count())
            .IsGreaterThan(1);
        actualRenderer.Dispose();
        expectedRenderer.Dispose();
        chordRenderer.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    private static byte[] Capture(
        OpenUsdSilkSession session, ISilkGraphicsDevice device, SilkMeshRenderer renderer, bool bounded)
    {
        var camera = new CameraState(
            Matrix4x4.CreateLookAt(new Vector3(0, 0, 3), Vector3.Zero, Vector3.UnitY),
            Matrix4x4.CreateOrthographic(2, 2, 0.1f, 10));
        return bounded
            ? SilkFrameCapture.CaptureCore(
                session, device, renderer, 64, 64, RenderSettings.Default, 0, camera,
                ingestionOptions: Options(1_000_000))
                .Rgba.ToArray()
            : SilkFrameCapture.CaptureCore(
                session, device, renderer, 64, 64, RenderSettings.Default, 0, camera).Rgba.ToArray();
    }

    private static OpenUsdSilkPage Sync(OpenUsdSilkSession session, bool bounded) =>
        bounded ? session.Sync(16, 16, Options(4096)) : session.Sync(16, 16);

    private static SilkSceneIngestionOptions Options(ulong limit) =>
        new(RenderPurpose.Default | RenderPurpose.Render | RenderPurpose.Proxy, "full", limit);

    private static int CountUpserts(OpenUsdSilkPage page)
    {
        int count = 0;
        using SilkCommandEnumerator commands = page.GetEnumerator();
        while (commands.MoveNext())
        {
            if (commands.Current.Type == SilkCommandType.MeshUpsert)
            {
                count++;
            }
        }
        return count;
    }

    private static async Task AssertSameCurveSnapshot(
        SilkMeshData expected,
        SilkMeshData actual,
        float[] points,
        uint[] indices,
        int[] subprims,
        float[] widths)
    {
        await Assert.That(actual.Path).IsEqualTo(expected.Path);
        await Assert.That(actual.PrimId).IsEqualTo(expected.PrimId);
        await Assert.That(actual.StableHash).IsEqualTo(expected.StableHash);
        await Assert.That(actual.InstanceId).IsEqualTo(expected.InstanceId);
        await Assert.That(actual.InstanceIndex).IsEqualTo(expected.InstanceIndex);
        await Assert.That(actual.TopologyKind).IsEqualTo(expected.TopologyKind);
        await Assert.That(actual.TopologyRevision).IsEqualTo(expected.TopologyRevision);
        await Assert.That(actual.SubprimIdentity).IsEqualTo(expected.SubprimIdentity);
        await Assert.That(actual.SubprimUnsupported).IsEqualTo(expected.SubprimUnsupported);
        await Assert.That(actual.Points.Span.SequenceEqual(points)).IsTrue();
        await Assert.That(actual.Indices.Span.SequenceEqual(indices)).IsTrue();
        await Assert.That(actual.TriangleSubprims.Span.SequenceEqual(subprims)).IsTrue();
        SilkVertexAttributeData expectedWidths = expected.Attributes.Single(entry => entry.Name == "widths");
        SilkVertexAttributeData actualWidths = actual.Attributes.Single(entry => entry.Name == "widths");
        await Assert.That(actualWidths.Semantic).IsEqualTo(expectedWidths.Semantic);
        await Assert.That(actualWidths.Interpolation).IsEqualTo(expectedWidths.Interpolation);
        await Assert.That(actualWidths.ComponentCount).IsEqualTo(expectedWidths.ComponentCount);
        await Assert.That(actualWidths.Data.Span.SequenceEqual(widths)).IsTrue();
    }

    private static UsdVec3f[] ToUsd(Vector3[] points) =>
        points.Select(point => new UsdVec3f(point.X, point.Y, point.Z)).ToArray();

    private static float MaxDifference(float[] actual, float[] expected) =>
        actual.Length == expected.Length
            ? actual.Zip(expected, (left, right) => MathF.Abs(left - right)).DefaultIfEmpty().Max()
            : float.PositiveInfinity;

    private static float[] Widths(string interpolation, int[] counts, string wrap, int pointCount) =>
        interpolation switch
        {
            "constant" => [0.375f],
            "uniform" => [0.125f, 0.875f],
            "varying" => Enumerable.Range(1, counts.Sum(count =>
                (wrap == "periodic" ? count / 3 : (count - 4) / 3 + 2)))
                .Select(index => index / 8f).ToArray(),
            "vertex" => Enumerable.Range(1, pointCount).Select(index => index / 16f).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(interpolation))
        };

    private static CurveReference Reference(
        Vector3[] points, int[] counts, string wrap, string interpolation, float[] widths)
    {
        var positions = new List<float>();
        var indices = new List<uint>();
        var subprims = new List<int>();
        var emittedWidths = new List<float>();
        int pointOffset = 0;
        int varyingOffset = 0;
        int spanId = 0;
        bool periodic = wrap == "periodic";
        for (int curve = 0; curve < counts.Length; curve++)
        {
            int count = counts[curve];
            int spans = periodic ? count / 3 : (count - 4) / 3 + 1;
            for (int span = 0; span < spans; span++, spanId++)
            {
                int[] controls = Enumerable.Range(0, 4)
                    .Select(index => pointOffset + ((span * 3 + index) % count)).ToArray();
                for (int chord = 0; chord < ChordsPerSpan; chord++)
                {
                    for (int endpoint = 0; endpoint < 2; endpoint++)
                    {
                        double t = (chord + endpoint) / (double)ChordsPerSpan;
                        double s = 1 - t;
                        double[] basis = [s * s * s, 3 * s * s * t, 3 * s * t * t, t * t * t];
                        Vector3 point = Vector3.Zero;
                        for (int control = 0; control < 4; control++)
                        {
                            point += points[controls[control]] * (float)basis[control];
                        }
                        positions.Add(point.X);
                        positions.Add(point.Y);
                        positions.Add(point.Z);
                        indices.Add((uint)indices.Count);
                        float width = interpolation switch
                        {
                            "constant" => widths[0],
                            "uniform" => widths[curve],
                            "varying" => (float)((1 - t) * widths[varyingOffset + span] +
                                t * widths[varyingOffset + (periodic ? (span + 1) % spans : span + 1)]),
                            "vertex" => (float)Enumerable.Range(0, 4)
                                .Sum(index => basis[index] * widths[controls[index]]),
                            _ => throw new ArgumentOutOfRangeException(nameof(interpolation))
                        };
                        emittedWidths.Add(width);
                    }
                    subprims.Add(spanId);
                }
            }
            pointOffset += count;
            varyingOffset += spans + (periodic ? 0 : 1);
        }
        return new CurveReference(
            positions.ToArray(), indices.ToArray(), subprims.ToArray(),
            interpolation == "constant" ? [widths[0]] : emittedWidths.ToArray());
    }

    private static string Curve(
        Vector3[] points, int[] counts, string wrap, string interpolation, float[] widths, string extra = "") =>
        $$"""
        def BasisCurves "Curve" {
            uniform token type = "cubic"
            uniform token basis = "bezier"
            uniform token wrap = "{{wrap}}"
            int[] curveVertexCounts = [{{string.Join(",", counts)}}]
            point3f[] points = {{FormatPoints(points)}}
            float[] widths = [{{string.Join(",", widths.Select(Format))}}] (interpolation = "{{interpolation}}")
            color3f[] primvars:displayColor = [(1,0.5,0.1)] (interpolation = "constant")
            {{extra}}
        }
        """;

    private static string LinearSegments(float[] points)
    {
        Vector3[] vectors = Enumerable.Range(0, points.Length / 3)
            .Select(index => new Vector3(points[index * 3], points[index * 3 + 1], points[index * 3 + 2]))
            .ToArray();
        return $$"""
            def BasisCurves "Curve" {
                uniform token type = "linear"
                uniform token wrap = "segmented"
                int[] curveVertexCounts = [{{string.Join(",", Enumerable.Repeat(2, vectors.Length / 2))}}]
                point3f[] points = {{FormatPoints(vectors)}}
                float[] widths = [0.25] (interpolation = "constant")
                color3f[] primvars:displayColor = [(1,0.5,0.1)] (interpolation = "constant")
            }
            """;
    }

    private static string FormatPoints(Vector3[] points) =>
        "[" + string.Join(",", points.Select(point =>
            $"({Format(point.X)},{Format(point.Y)},{Format(point.Z)})")) + "]";

    private static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private sealed record CurveReference(float[] Points, uint[] Indices, int[] Subprims, float[] Widths);

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory;

        internal Fixture(string body)
        {
            string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
            if (string.IsNullOrEmpty(plugins))
            {
                if (Environment.GetEnvironmentVariable("OPENUSD_MESH_PREPARATION_REQUIRED") == "1")
                {
                    throw new InvalidOperationException(
                        "The native curve preparation profile requires matched plugins.");
                }
                Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH to a matched native runtime.");
                throw new InvalidOperationException("Skip.Test returned unexpectedly.");
            }
            Plugins = plugins;
            string root = Environment.GetEnvironmentVariable("OPENUSD_TEST_WORK_ROOT")
                ?? System.IO.Path.Combine(Environment.CurrentDirectory, "artifacts");
            _directory = System.IO.Path.Combine(root, $"silk-cubic-curves-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "scene.usda");
            Text = "#usda 1.0\n" + body;
            File.WriteAllText(Path, Text);
        }

        internal string Plugins { get; }
        internal string Path { get; }
        internal string Text { get; }

        internal OpenUsdSilkSession Open() => OpenUsdSilkRuntime.Create(Plugins, Path);

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
