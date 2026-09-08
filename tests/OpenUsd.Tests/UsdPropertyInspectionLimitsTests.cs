// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Tests;

public sealed class UsdPropertyInspectionLimitsTests
{
    [Test]
    public async Task DefaultsBoundRowsTextWorkAndAllThreePreviewDomains()
    {
        UsdPropertyInspectionLimits limits = UsdPropertyInspectionLimits.Default;
        await Assert.That(limits.MaximumPropertyCount).IsEqualTo(4096);
        await Assert.That(limits.MaximumTextBytes).IsEqualTo(1_048_576);
        await Assert.That(limits.MaximumMetadataWork).IsEqualTo(262_144);
        await Assert.That(limits.MaximumPreviewTextBytes).IsEqualTo(4096);
        await Assert.That(limits.PreviewElements).IsEqualTo(16);
        await Assert.That(limits.TimeSamplePreview).IsEqualTo(16);
        await Assert.That(limits.TargetPreview).IsEqualTo(16);
        await Assert.That(UsdPropertyInspectionLimits.Viewer.MaximumPropertyCount).IsEqualTo(65_536);
        await Assert.That(UsdPropertyInspectionLimits.Viewer.PreviewElements).IsEqualTo(16);
    }

    [Test]
    [Arguments(0, -1)]
    [Arguments(0, 65_537)]
    [Arguments(1, -1)]
    [Arguments(1, 16_777_217)]
    [Arguments(2, -1)]
    [Arguments(2, 17)]
    [Arguments(3, -1)]
    [Arguments(3, 17)]
    [Arguments(4, -1)]
    [Arguments(4, 17)]
    [Arguments(5, -1)]
    [Arguments(5, 1_048_577)]
    [Arguments(6, -1)]
    [Arguments(6, 65_537)]
    public async Task InvalidBoundsCannotReachNativeAllocation(int field, int value)
    {
        await Assert.That(() => new UsdPropertyInspectionLimits(
            maximumPropertyCount: field == 0 ? value : 4096,
            maximumTextBytes: field == 1 ? value : 1_048_576,
            previewElements: field == 2 ? value : 16,
            timeSamplePreview: field == 3 ? value : 16,
            targetPreview: field == 4 ? value : 16,
            maximumMetadataWork: field == 5 ? value : 262_144,
            maximumPreviewTextBytes: field == 6 ? value : 4096)).Throws<ArgumentOutOfRangeException>();
    }
}
