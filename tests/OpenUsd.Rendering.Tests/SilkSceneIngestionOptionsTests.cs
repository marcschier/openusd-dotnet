// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkSceneIngestionOptionsTests
{
    [Test]
    public async Task MeshPreparationPacketsKeepFixedWidthCounters()
    {
        await Assert.That(Marshal.SizeOf<OpenUsdSilkRuntime.NativeMeshPreparationLimits>()).IsEqualTo(16);
        await Assert.That(Marshal.SizeOf<OpenUsdSilkRuntime.NativeMeshPreparationUsage>()).IsEqualTo(32);
        await Assert.That(Marshal.SizeOf<OpenUsdSilkRuntime.NativeMeshPreparationPageLimits>()).IsEqualTo(24);
        await Assert.That(Marshal.OffsetOf<OpenUsdSilkRuntime.NativeMeshPreparationPageLimits>(
            nameof(OpenUsdSilkRuntime.NativeMeshPreparationPageLimits.MaximumPageBytes)).ToInt32()).IsEqualTo(16);
        await Assert.That(Marshal.OffsetOf<OpenUsdSilkRuntime.NativeMeshPreparationUsage>(
            nameof(OpenUsdSilkRuntime.NativeMeshPreparationUsage.MaximumReservedBytes)).ToInt32()).IsEqualTo(8);
        await Assert.That(Marshal.OffsetOf<OpenUsdSilkRuntime.NativeMeshPreparationUsage>(
            nameof(OpenUsdSilkRuntime.NativeMeshPreparationUsage.ReservedBytes)).ToInt32()).IsEqualTo(16);
        await Assert.That(Marshal.OffsetOf<OpenUsdSilkRuntime.NativeMeshPreparationUsage>(
            nameof(OpenUsdSilkRuntime.NativeMeshPreparationUsage.PeakReservedBytes)).ToInt32()).IsEqualTo(24);
    }

    [Test]
    public async Task CommandPageLimitIsExplicitPositiveAndPreservesMeshAdmission()
    {
        var legacy = new SilkSceneIngestionOptions(RenderPurpose.Default, "full");
        var version1 = new SilkSceneIngestionOptions(RenderPurpose.Default, "full", 4096);
        var version2 = new SilkSceneIngestionOptions(RenderPurpose.Default, "full", 4096, 2048);
        await Assert.That(legacy.MaximumCommandPageBytes).IsNull();
        await Assert.That(version1.MaximumCommandPageBytes).IsNull();
        await Assert.That(version2.MaximumCommandPageBytes).IsEqualTo(2048);
        await Assert.That(version2.MaximumMeshPreparationReservationBytes).IsEqualTo(4096ul);
        await Assert.That(() => new SilkSceneIngestionOptions(RenderPurpose.Default, "full", 4096, 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new SilkSceneIngestionOptions(RenderPurpose.Default, "full", 4096, -1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new SilkSceneIngestionOptions(RenderPurpose.Default, "full", 0, 2048))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(new SilkSceneIngestionOptions(RenderPurpose.Default, "full", ulong.MaxValue, int.MaxValue)
            .MaximumCommandPageBytes).IsEqualTo(int.MaxValue);
    }

    [Test]
    public async Task MeshPreparationReservationIsExplicitAndPositive()
    {
        var legacy = new SilkSceneIngestionOptions(RenderPurpose.Default, "full");
        var bounded = new SilkSceneIngestionOptions(RenderPurpose.Default, "full", 4096);
        await Assert.That(legacy.MaximumMeshPreparationReservationBytes).IsNull();
        await Assert.That(bounded.MaximumMeshPreparationReservationBytes).IsEqualTo(4096ul);
        await Assert.That(() => new SilkSceneIngestionOptions(RenderPurpose.Default, "full", 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ExplicitOptionsPreserveTheRequestedSupportedProductChoices()
    {
        SilkSceneIngestionOptions options = new(
            RenderPurpose.Default | RenderPurpose.Render,
            "full");

        await Assert.That(options.IncludedPurposes)
            .IsEqualTo(RenderPurpose.Default | RenderPurpose.Render);
        await Assert.That(options.MaterialBindingPurpose).IsEqualTo("full");
    }

    [Test]
    public async Task EmptyAllPurposeBindingIsRejectedExplicitly()
    {
        await Assert.That(() => new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render,
                "allPurpose"))
            .Throws<NotSupportedException>();
        await Assert.That(() => new SilkSceneIngestionOptions(
                RenderPurpose.Default | RenderPurpose.Render,
                string.Empty))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task ExcludingDefaultPurposeIsRejectedExplicitly()
    {
        await Assert.That(() => new SilkSceneIngestionOptions(RenderPurpose.Render, string.Empty))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task MaterialBindingPurposeMustBeOneToken()
    {
        await Assert.That(() => new SilkSceneIngestionOptions(RenderPurpose.Default, "bad token"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task UnsupportedCustomBindingPurposeIsRejectedExplicitly()
    {
        await Assert.That(() => new SilkSceneIngestionOptions(RenderPurpose.Default, "customLook"))
            .Throws<NotSupportedException>();
    }
}
