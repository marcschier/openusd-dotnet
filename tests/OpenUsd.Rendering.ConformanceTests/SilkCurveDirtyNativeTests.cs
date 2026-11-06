// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

[NotInParallel]
public sealed class SilkCurveDirtyNativeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DisplayMetadataEditDoesNotDuplicateRetainedCurveReservation(bool exactLimit)
    {
        string? plugins = Environment.GetEnvironmentVariable("OPENUSD_TEST_PLUGIN_PATH");
        if (string.IsNullOrEmpty(plugins))
        {
            if (Environment.GetEnvironmentVariable("OPENUSD_MESH_PREPARATION_REQUIRED") == "1")
            {
                throw new InvalidOperationException("The native curve preparation profile requires matched plugins.");
            }
            Skip.Test("Set OPENUSD_TEST_PLUGIN_PATH to a runtime with bounded curve preparation.");
        }

        string root = Directory.CreateTempSubdirectory("silk-curve-dirty-").FullName;
        try
        {
            string path = Path.Combine(root, "scene.usda");
            await File.WriteAllTextAsync(path, """
                #usda 1.0
                def BasisCurves "Curve" {
                    uniform token type = "linear"
                    uniform token wrap = "nonperiodic"
                    uniform bool doubleSided = false
                    int[] curveVertexCounts = [4]
                    point3f[] points = [(0,0,0),(1,0,0),(1,1,0),(0,1,0)]
                    float[] widths = [0.1,0.2,0.3,0.4] (interpolation = "vertex")
                }
                """);
            const ulong cost = 292;
            ulong maximum = exactLimit ? cost : cost * 3;
            var options = new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render | RenderPurpose.Proxy, "full", maximum);
            await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
            using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
            using OpenUsdSilkSession session = OpenUsdSilkRuntime.Create(plugins, source);
            using OpenUsdSilkPage initial = session.Sync(16, 16, options);
            await Assert.That(initial.MeshPreparationUsage!.ReservedBytes).IsEqualTo(cost);
            await Assert.That(CountCurveUpserts(initial)).IsEqualTo(1);

            await scheduler.InvokeAsync(stage => stage.GetPrim("/Curve").SetBool("doubleSided", true));
            using OpenUsdSilkPage edited = session.Sync(16, 16, options);
            await Assert.That(CountCurveUpserts(edited)).IsEqualTo(0);
            await Assert.That(edited.MeshPreparationUsage!.ReservedBytes).IsEqualTo(cost);
            await Assert.That(edited.MeshPreparationUsage.PeakReservedBytes).IsEqualTo(cost);

            using OpenUsdSilkPage quiet = session.Sync(16, 16, options);
            await Assert.That(CountCurveUpserts(quiet)).IsEqualTo(0);
            await Assert.That(quiet.MeshPreparationUsage!.ReservedBytes).IsEqualTo(cost);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static int CountCurveUpserts(OpenUsdSilkPage page)
    {
        int upserts = 0;
        using SilkCommandEnumerator commands = page.GetEnumerator();
        while (commands.MoveNext())
        {
            if (commands.Current.Type == SilkCommandType.MeshUpsert)
            {
                upserts++;
            }
        }
        return upserts;
    }
}
