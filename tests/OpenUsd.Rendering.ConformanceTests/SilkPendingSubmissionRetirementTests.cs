// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.ConformanceTests;

public sealed class SilkPendingSubmissionRetirementTests
{
    [Test]
    public async Task FailedRetirementsRemainQueuedWhileSuccessfulOnesAreNotRepeated()
    {
        var pending = new SilkPendingSubmissionRetirements();
        var first = new Submission(pending) { Refuse = true };
        var middle = new Submission(pending);
        var last = new Submission(pending) { Refuse = true };
        pending.Retain(first);
        pending.Retain(middle);
        pending.Retain(last);
        pending.Retain(last);

        AggregateException? failure = await Assert.That(
            () => SilkPendingSubmissionRetirements.Retire(pending.Snapshot()))
            .Throws<AggregateException>();
        await Assert.That(failure!.InnerExceptions.Count).IsEqualTo(2);
        await Assert.That(pending.Snapshot().Length).IsEqualTo(2);
        await Assert.That(first.Attempts).IsEqualTo(1);
        await Assert.That(middle.Attempts).IsEqualTo(1);
        await Assert.That(last.Attempts).IsEqualTo(1);
        await Assert.That(middle.Releases).IsEqualTo(1);

        first.Refuse = false;
        last.Refuse = false;
        SilkPendingSubmissionRetirements.Retire(pending.Snapshot());
        SilkPendingSubmissionRetirements.Retire(pending.Snapshot());
        await Assert.That(pending.Snapshot().Length).IsEqualTo(0);
        await Assert.That(first.Attempts).IsEqualTo(2);
        await Assert.That(middle.Attempts).IsEqualTo(1);
        await Assert.That(last.Attempts).IsEqualTo(2);
        await Assert.That(first.Releases).IsEqualTo(1);
        await Assert.That(middle.Releases).IsEqualTo(1);
        await Assert.That(last.Releases).IsEqualTo(1);
    }

    [Test]
    public async Task SubmissionsRetainedAfterSnapshotWaitForAnotherDeviceDrain()
    {
        var pending = new SilkPendingSubmissionRetirements();
        var earlier = new Submission(pending);
        var later = new Submission(pending);
        pending.Retain(earlier);
        SilkPendingSubmissionRetirement[] snapshot = pending.Snapshot();
        pending.Retain(later);

        SilkPendingSubmissionRetirements.Retire(snapshot);
        await Assert.That(earlier.Releases).IsEqualTo(1);
        await Assert.That(later.Attempts).IsEqualTo(0);
        await Assert.That(pending.Snapshot().Length).IsEqualTo(1);
        SilkPendingSubmissionRetirements.Retire(pending.Snapshot());
        await Assert.That(later.Releases).IsEqualTo(1);
        await Assert.That(pending.Snapshot().Length).IsEqualTo(0);
    }

    private sealed class Submission(SilkPendingSubmissionRetirements owner)
        : SilkPendingSubmissionRetirement
    {
        internal bool Refuse { get; set; }
        internal int Attempts { get; private set; }
        internal int Releases { get; private set; }

        internal override void RetireAfterDeviceIdle()
        {
            Attempts++;
            if (Refuse)
            {
                throw new InvalidOperationException("Injected retirement failure.");
            }
            Releases++;
            owner.Remove(this);
        }
    }
}
