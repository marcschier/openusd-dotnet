// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class SharedAnalysisParityTests
{
    [Test]
    public async Task ViewerStatisticsMapSharedStageAnalysisWithoutDrift()
    {
        using UsdStage stage = OpenStageOrSkip("native-usdgeom.usda");
        ViewerHierarchySnapshot hierarchy = ViewerStageSnapshotBuilder.BuildHierarchy(stage);
        StageStatisticsAnalysis shared = StageStatisticsAnalyzer.Analyze(
            stage,
            hierarchy.Entries.Select(static entry => entry.Path));

        ViewerStageStatisticsSnapshot viewer =
            ViewerStageSnapshotBuilder.BuildDocument(stage).Statistics;

        await Assert.That(viewer.RootLayerIdentifier)
            .IsEqualTo(shared.RootLayerIdentifier);
        await Assert.That(viewer.SessionLayerIdentifier)
            .IsEqualTo(shared.SessionLayerIdentifier);
        await Assert.That(viewer.DefaultPrimPath).IsEqualTo(shared.DefaultPrimPath);
        await Assert.That(viewer.PrimCount).IsEqualTo(shared.PrimCount);
        await Assert.That(viewer.MeshCount).IsEqualTo(shared.MeshCount);
        await Assert.That(viewer.CurveVertexCount).IsEqualTo(shared.CurveVertexCount);
        await Assert.That(viewer.MeshVertexCount).IsEqualTo(shared.MeshVertexCount);
        await Assert.That(viewer.FaceCount).IsEqualTo(shared.FaceCount);
        await Assert.That(viewer.RootPrimCount).IsEqualTo(shared.RootPrimCount);
        await Assert.That(viewer.LeafPrimCount).IsEqualTo(shared.LeafPrimCount);
        await Assert.That(viewer.MaximumDepth).IsEqualTo(shared.MaximumDepth);
        await Assert.That(viewer.WorldBounds).IsEqualTo(shared.WorldBounds);
        await Assert.That(viewer.OrientedWorldBounds)
            .IsEqualTo(shared.OrientedWorldBounds);
    }

    [Test]
    public async Task ViewerFramingMapsSharedBoundsFramingWithoutDrift()
    {
        var bounds = new UsdBounds3d(
            new UsdVec3d(-1d, -2d, -3d),
            new UsdVec3d(3d, 2d, 1d));
        var controller = new ViewerCameraNavigationController(
            new Rendering.ViewportDimensions(1600, 900));
        ViewerCameraNavigationState before = controller.State;
        bool sharedCreated = Rendering.BoundsCameraFraming.TryCreate(
            bounds,
            before.VerticalFieldOfView,
            before.AspectRatio,
            out Rendering.BoundsCameraFraming shared);

        bool viewerCreated = controller.FrameBounds(bounds);

        await Assert.That(viewerCreated).IsEqualTo(sharedCreated);
        await Assert.That(controller.State.Target).IsEqualTo(shared.Target);
        await Assert.That(controller.State.Distance).IsEqualTo(shared.Distance);
        await Assert.That(controller.State.OrthographicHeight)
            .IsEqualTo(shared.OrthographicHeight);
        await Assert.That(controller.State.NearPlane).IsEqualTo(shared.NearPlane);
        await Assert.That(controller.State.FarPlane).IsEqualTo(shared.FarPlane);
    }

    private static UsdStage OpenStageOrSkip(string fileName)
    {
        try
        {
            return UsdStage.Open(Path.Combine(FindRepositoryRoot(), "test-assets", fileName));
        }
        catch (DllNotFoundException exception)
        {
            Skip.Test($"openusd_dotnet native runtime is unavailable: {exception.Message}");
            throw;
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
