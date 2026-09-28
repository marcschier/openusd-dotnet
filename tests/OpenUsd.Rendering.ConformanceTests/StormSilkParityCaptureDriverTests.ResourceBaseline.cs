// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed partial class StormSilkParityCaptureDriverTests
{
    [Test]
    public async Task CuratedResourceBudgetsKeepTheFixedFrameBlockOutsideVariableHeadroom()
    {
        ParityScene[] scenes = CreateAllScenes();
        await Assert.That(scenes.Length).IsEqualTo(25);
        foreach (ParityScene scene in scenes)
        {
            ParityPerformanceBudget budget = scene.GetPerformanceBudget("Vulkan SwiftShader");
            ulong variable = budget.MeasuredBufferAllocationBytes - 15_296;
            ulong headroom = Math.Max(256ul, (variable + 3) / 4);
            await Assert.That(budget.MaxBufferAllocationBytes)
                .IsEqualTo(budget.MeasuredBufferAllocationBytes + headroom);
            await Assert.That(budget.MaxBufferAllocationBytes)
                .IsLessThan(budget.MeasuredBufferAllocationBytes + budget.MeasuredBufferAllocationBytes / 4);
        }
    }

    [Test]
    public async Task RecordsCuratedResourceFootprintsForBaselineReview()
    {
        if (Environment.GetEnvironmentVariable("OPENUSD_PARITY_MEASURE_RESOURCES") != "1")
        {
            Skip.Test("Opt-in resource baseline measurement; the ordinary performance gates remain independent.");
            return;
        }
        SilkParityBackend backend = CreateBackends().Single(
            item => item.Name == "Vulkan SwiftShader");
        foreach (ParityScene scene in CreateAllScenes())
        {
            if (!TryCreateInput(scene, out ParityCaptureInput input))
            {
                throw new InvalidOperationException($"Missing baseline input for {scene.Name}.");
            }
            await using UsdStageScheduler scheduler = UsdStageScheduler.Open(input.StagePath);
            using UsdStageRenderSource source = await scheduler.AcquireRenderSourceAsync();
            SilkParityCapture capture = ParityCaptureDriver.CaptureSilk(input, source, backend);
            await Assert.That(capture.DrawCount).IsGreaterThan(0);
            if (Environment.GetEnvironmentVariable("OPENUSD_PARITY_VERIFY_RESOURCE_BASELINES") == "1")
            {
                await AssertPerformanceBudget(scene, capture);
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                scene.Name,
                capture.BackendName,
                capture.DrawCount,
                capture.Statistics
            }));
        }
    }
}
