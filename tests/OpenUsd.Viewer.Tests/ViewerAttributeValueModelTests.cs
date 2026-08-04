// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerAttributeValueModelTests
{
    [Test]
    public async Task TimeSampleSummaryIncludesFirstMiddleAndLastSamples()
    {
        string summary = ViewerStageSnapshotBuilder.FormatTimeSamples(
            [1, 2, 3, 4, 5]);

        await Assert.That(summary).IsEqualTo("1, 3, 5 (5 samples)");
    }

    [Test]
    public async Task AttributeSnapshotEqualityIncludesSampleSummaryAndValue()
    {
        var left = new ViewerAttributeSnapshot(
            "points",
            "point3f[]",
            HasAuthoredValue: true,
            IsBlocked: false,
            TimeSampleCount: 5,
            TimeSamples: "1, 3, 5 (5 samples)",
            Value: "[(0, 0, 0)]");
        var right = new ViewerAttributeSnapshot(
            "points",
            "point3f[]",
            HasAuthoredValue: true,
            IsBlocked: false,
            TimeSampleCount: 5,
            TimeSamples: "1, 3, 5 (5 samples)",
            Value: "[(0, 0, 0)]");

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left).IsNotEqualTo(right with { TimeSamples = "1, 2, 5 (5 samples)" });
        await Assert.That(left).IsNotEqualTo(right with { Value = "[(1, 0, 0)]" });
    }
}
