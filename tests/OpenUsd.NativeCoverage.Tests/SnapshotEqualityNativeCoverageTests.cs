// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.NativeCoverage.Tests;

public sealed class SnapshotEqualityNativeCoverageTests
{
    [Test]
    public async Task RepeatedInspectionPollsOfUnchangedStageCompareEqual()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(RepeatedInspectionPollsOfUnchangedStageCompareEqual));
        string stagePath = Path.Combine(directory, "snapshot-equality.usda");
        using UsdStage stage = UsdStage.Create(stagePath);
        UsdPrim prim = stage.DefinePrim("/World", "Xform");

        PcpPrimIndex firstIndex = prim.GetPrimIndex();
        PcpPrimIndex secondIndex = prim.GetPrimIndex();
        IReadOnlyList<UsdValidationError> firstErrors = UsdValidation.Validate(stage);
        IReadOnlyList<UsdValidationError> secondErrors = UsdValidation.Validate(stage);

        await Assert.That(firstIndex).IsEqualTo(secondIndex);
        await Assert.That(firstIndex.GetHashCode()).IsEqualTo(secondIndex.GetHashCode());
        await Assert.That(firstErrors.SequenceEqual(secondErrors)).IsTrue();
    }
}
