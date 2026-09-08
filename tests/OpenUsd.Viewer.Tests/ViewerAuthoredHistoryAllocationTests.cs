// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerAuthoredHistoryAllocationTests
{
    [Test]
    [NotInParallel("ViewerPhysicsAllocation")]
    public async Task RepeatedHistoryPayloadReadsAndRevisionMismatchChecksAllocateNothing()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "authored-allocation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "source.usda");
        await File.WriteAllTextAsync(path, "#usda 1.0\ndef Xform \"Body\" {}\n");
        try
        {
            await using UsdStageScheduler scheduler = UsdStageScheduler.Open(path);
            await using var editor = new ViewerAuthoredEditController(scheduler);
            var address = new UsdLayerEditAddress("/Body.review:labels", UsdLayerEditField.Default);
            var foreign = new UsdLayerEditAddress("/Body.review:foreign", UsdLayerEditField.Default);
            string[] values = Enumerable.Repeat(new string('x', 2048), 128).ToArray();
            ViewerAuthoredEditCapture before = await editor.CaptureAsync([address]);
            ViewerAuthoredEditResult first = await editor.ApplyAsync(before,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromStringArray(values), "string[]")], "Labels");
            UsdLayerAuthoredSnapshot after = first.After!;
            ViewerAuthoredEditCapture equal = await editor.CaptureAsync([address]);
            await scheduler.EditAsync(stage =>
            {
                using UsdLayer review = stage.GetUserReviewLayer();
                return review.CompareAndApply(review.CaptureAuthored([foreign]),
                    [UsdLayerEdit.Set(foreign, UsdLayerEditValue.FromDouble(42), "double")]);
            }, UsdStageInvalidationKind.Property);
            ViewerAuthoredEditCapture revised = await editor.CaptureAsync([address]);
            values[0] = "changed";
            ViewerAuthoredEditResult second = await editor.ApplyAsync(revised,
                [UsdLayerEdit.Set(address, UsdLayerEditValue.FromStringArray(values), "string[]")], "More labels");

            var history = new ViewerEditHistory<ViewerAuthoredEditStep>();
            history.Record(new ViewerAuthoredEditStep("Labels", before.LayerIdentifier, before.Snapshot, after), 0);
            var next = new ViewerAuthoredEditStep(
                "More labels", before.LayerIdentifier, revised.Snapshot, second.After!);
            await Assert.That(after.ByteLength).IsGreaterThanOrEqualTo(262_144);
            await Assert.That(ReferenceEquals(after, equal.Snapshot)).IsFalse();
            await Assert.That(after.Revision).IsNotEqualTo(revised.Snapshot.Revision);
            await Assert.That(after.Opinions[0].Value.Equals(revised.Snapshot.Opinions[0].Value)).IsTrue();

            long byteTotal = 0;
            int equalPackets = 0;
            int differentRevisions = 0;
            int equalAffectedStates = 0;
            int differentAffectedStates = 0;
            int refusedMerges = 0;
            void Read(int iteration)
            {
                if (!history.TryPeekUndo(out ViewerAuthoredEditStep? step))
                {
                    throw new InvalidOperationException("The retained history entry disappeared.");
                }
                byteTotal += step.Before.ByteLength + (long)step.After.ByteLength;
                equalPackets += step.After.HasSamePayload(equal.Snapshot) ? 1 : 0;
                differentRevisions += step.After.HasSamePayload(revised.Snapshot) ? 0 : 1;
                equalAffectedStates +=
                    ViewerAuthoredEditStep.HasSameAffectedState(step.After, revised.Snapshot) ? 1 : 0;
                differentAffectedStates += ViewerAuthoredEditStep.HasSameAffectedState(step.After, next.After) ? 0 : 1;
                refusedMerges += step.TryCoalesce(next, out _) ? 0 : 1;
            }

            Action<int> read = Read;
            _ = AllocationWarmup.UntilQuiet(read);
            byteTotal = 0;
            equalPackets = 0;
            differentRevisions = 0;
            equalAffectedStates = 0;
            differentAffectedStates = 0;
            refusedMerges = 0;
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 1000; index++)
            {
                read(index);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - start;

            await Assert.That(allocated).IsEqualTo(0L);
            await Assert.That(equalPackets).IsEqualTo(1000);
            await Assert.That(differentRevisions).IsEqualTo(1000);
            await Assert.That(equalAffectedStates).IsEqualTo(1000);
            await Assert.That(differentAffectedStates).IsEqualTo(1000);
            await Assert.That(refusedMerges).IsEqualTo(1000);
            await Assert.That(byteTotal).IsGreaterThanOrEqualTo(262_144_000L);
            await Assert.That(history.UndoDepth).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
