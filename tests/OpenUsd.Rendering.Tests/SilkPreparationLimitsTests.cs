// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkPreparationLimitsTests
{
    [Test]
    public async Task UnconfiguredLimitsPreserveLegacyAndConfiguredLimitsKeepTheirFullWidth()
    {
        await Assert.That(SilkPreparationLimits.FromConfiguration(null, null)).IsNull();
        SilkPreparationLimits parsed = SilkPreparationLimits.FromConfiguration(
            "18446744073709551615", "2147483647")!;
        await Assert.That(parsed.MaximumMeshPreparationReservationBytes).IsEqualTo(ulong.MaxValue);
        await Assert.That(parsed.MaximumCommandPageBytes).IsEqualTo(int.MaxValue);
        await Assert.That(new SilkPreparationLimits(1, 1).MaximumCommandPageBytes).IsEqualTo(1);
        await Assert.That(() => new SilkPreparationLimits(0, 1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new SilkPreparationLimits(1, 0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new SilkPreparationLimits(1, -1)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(null, "1")]
    [Arguments("1", null)]
    [Arguments("", "1")]
    [Arguments("1", "")]
    [Arguments("0", "1")]
    [Arguments("1", "0")]
    [Arguments("-1", "1")]
    [Arguments("1", "-1")]
    [Arguments("+1", "1")]
    [Arguments("1", " 1")]
    [Arguments("1,000", "1")]
    [Arguments("1.5", "1")]
    [Arguments("18446744073709551616", "1")]
    [Arguments("1", "2147483648")]
    public async Task InvalidOrPartialConfigurationCannotSilentlyDisableAdmission(string? mesh, string? page)
    {
        await Assert.That(() => SilkPreparationLimits.FromConfiguration(mesh, page)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(500ul, 100, 500ul, 100)]
    [Arguments(2000ul, 100, 1000ul, 100)]
    [Arguments(500ul, 500, 500ul, 200)]
    [Arguments(2000ul, 500, 1000ul, 200)]
    public async Task RequestLimitsCanOnlyTightenTheSessionWithoutChangingSceneChoices(
        ulong mesh, int page, ulong expectedMesh, int expectedPage)
    {
        var session = new SilkPreparationLimits(1000, 200);
        var request = new SilkSceneIngestionOptions(RenderPurpose.Default | RenderPurpose.Guide, "preview", mesh, page);
        SilkSceneIngestionOptions effective = session.Apply(request);
        await Assert.That(effective.MaximumMeshPreparationReservationBytes).IsEqualTo(expectedMesh);
        await Assert.That(effective.MaximumCommandPageBytes).IsEqualTo(expectedPage);
        await Assert.That(effective.IncludedPurposes).IsEqualTo(request.IncludedPurposes);
        await Assert.That(effective.MaterialBindingPurpose).IsEqualTo("preview");
        await Assert.That(request.MaximumMeshPreparationReservationBytes).IsEqualTo(mesh);
        await Assert.That(request.MaximumCommandPageBytes).IsEqualTo(page);
        await Assert.That(session.MaximumMeshPreparationReservationBytes).IsEqualTo(1000ul);
        await Assert.That(session.MaximumCommandPageBytes).IsEqualTo(200);
    }

    [Test]
    public async Task UnboundedProductOptionsInheritBothCeilings()
    {
        var limits = new SilkPreparationLimits(1000, 200);
        SilkSceneIngestionOptions effective = limits.Apply(new(RenderPurpose.Default, "full"));
        await Assert.That(effective.MaximumMeshPreparationReservationBytes).IsEqualTo(1000ul);
        await Assert.That(effective.MaximumCommandPageBytes).IsEqualTo(200);
        await Assert.That(limits.Apply(effective)).IsSameReferenceAs(effective);
    }
}
