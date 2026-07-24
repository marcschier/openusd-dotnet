// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Tests;

public sealed class UsdStageChangeTests
{
    [Test]
    public async Task CoalescePreservesRangeCountAndStrongestInvalidation()
    {
        var first = new UsdStageChange(
            beforeChangeSerial: 10,
            afterChangeSerial: 12,
            UsdStageInvalidationKind.Property);
        var second = new UsdStageChange(
            beforeChangeSerial: 12,
            afterChangeSerial: 18,
            UsdStageInvalidationKind.Composition,
            editCount: 3);

        UsdStageChange combined = first.Coalesce(second);

        await Assert.That(combined.BeforeChangeSerial).IsEqualTo(10ul);
        await Assert.That(combined.AfterChangeSerial).IsEqualTo(18ul);
        await Assert.That(combined.Invalidation).IsEqualTo(UsdStageInvalidationKind.Composition);
        await Assert.That(combined.EditCount).IsEqualTo(4);
    }

    [Test]
    public async Task CoalesceAcrossUnclassifiedSerialGapRequiresFullInvalidation()
    {
        var first = new UsdStageChange(1, 2, UsdStageInvalidationKind.Property);
        var subsequent = new UsdStageChange(4, 5, UsdStageInvalidationKind.Property);

        UsdStageChange combined = first.Coalesce(subsequent);

        await Assert.That(combined.Invalidation).IsEqualTo(UsdStageInvalidationKind.Full);
        await Assert.That(combined.BeforeChangeSerial).IsEqualTo(1ul);
        await Assert.That(combined.AfterChangeSerial).IsEqualTo(5ul);
    }

    [Test]
    public async Task SustainedCoalescingRemainsOneOrderedValue()
    {
        var combined = new UsdStageChange(100, 101, UsdStageInvalidationKind.Property);

        for (ulong i = 1; i <= 10_000; i++)
        {
            UsdStageInvalidationKind invalidation = i == 5_000
                ? UsdStageInvalidationKind.Topology
                : UsdStageInvalidationKind.Property;
            combined = combined.Coalesce(new UsdStageChange(
                100 + i,
                101 + i,
                invalidation));
        }

        await Assert.That(combined.BeforeChangeSerial).IsEqualTo(100ul);
        await Assert.That(combined.AfterChangeSerial).IsEqualTo(10_101ul);
        await Assert.That(combined.Invalidation).IsEqualTo(UsdStageInvalidationKind.Topology);
        await Assert.That(combined.EditCount).IsEqualTo(10_001);
    }

    [Test]
    public async Task CoalesceRejectsOverlappingChanges()
    {
        var first = new UsdStageChange(10, 20, UsdStageInvalidationKind.Property);
        var overlapping = new UsdStageChange(19, 21, UsdStageInvalidationKind.Topology);

        await Assert.That(() => first.Coalesce(overlapping))
            .Throws<ArgumentException>();
    }
}
