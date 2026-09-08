// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Tests;

public sealed class UsdHierarchyLimitsTests
{
    [Test]
    public async Task DefaultAndViewerLimitsRespectThePublicHierarchyCeilings()
    {
        await Assert.That(UsdHierarchyLimits.Default.MaximumPrimCount).IsEqualTo(100_000);
        await Assert.That(UsdHierarchyLimits.Default.MaximumTextBytes).IsEqualTo(16 * 1024 * 1024);
        await Assert.That(UsdHierarchyLimits.Default.MaximumDepth).IsEqualTo(256);
        await Assert.That(UsdHierarchyLimits.Viewer.MaximumPrimCount).IsEqualTo(1_000_000);
        await Assert.That(UsdHierarchyLimits.Viewer.MaximumTextBytes).IsEqualTo(128 * 1024 * 1024);
        await Assert.That(UsdHierarchyLimits.Viewer.MaximumDepth).IsEqualTo(1024);
        await Assert.That(UsdHierarchyLimits.Viewer.MaximumVariantSets).IsEqualTo(65_536);
        await Assert.That(UsdHierarchyLimits.Viewer.MaximumVariantNames).IsEqualTo(262_144);
        await Assert.That(UsdHierarchyLimits.Viewer.MaximumMetadataWork).IsEqualTo(16_000_000);
    }

    [Test]
    [Arguments(0, -1)]
    [Arguments(0, 1_000_001)]
    [Arguments(1, -1)]
    [Arguments(1, 134_217_729)]
    [Arguments(2, -1)]
    [Arguments(2, 1025)]
    [Arguments(3, -1)]
    [Arguments(3, 65_537)]
    [Arguments(4, -1)]
    [Arguments(4, 262_145)]
    [Arguments(5, -1)]
    [Arguments(5, 16_000_001)]
    public async Task InvalidAdmissionLimitsFailBeforeNativeAccess(int field, int value)
    {
        await Assert.That(() => new UsdHierarchyLimits(
            field == 0 ? value : 1,
            field == 1 ? value : 1,
            field == 2 ? value : 1,
            field == 3 ? value : 1,
            field == 4 ? value : 1,
            field == 5 ? value : 1)).Throws<ArgumentOutOfRangeException>();
    }
}
