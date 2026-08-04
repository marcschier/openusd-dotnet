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

        // Both lists appear in the message. This assertion once failed on Linux
        // while passing on Windows, and a bare "Expected to be true" gave no way
        // to tell whether the sets differed or merely their order. On a platform
        // that cannot be reproduced locally that is the difference between one
        // CI round trip and several.
        await Assert.That(firstErrors.Count)
            .IsEqualTo(secondErrors.Count)
            .Because(
                $"two polls of an unchanged stage returned {firstErrors.Count} " +
                $"and {secondErrors.Count} errors: " +
                $"[{Describe(firstErrors)}] versus [{Describe(secondErrors)}]");

        await Assert.That(firstErrors.SequenceEqual(secondErrors))
            .IsTrue()
            .Because(
                "validators run in parallel, so the facade sorts results to " +
                "keep detached snapshots diffable: " +
                $"[{Describe(firstErrors)}] versus [{Describe(secondErrors)}]");
    }

    [Test]
    public async Task ValidationResultsAreOrderedDeterministically()
    {
        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(ValidationResultsAreOrderedDeterministically));
        string stagePath = Path.Combine(directory, "validation-order.usda");
        using UsdStage stage = UsdStage.Create(stagePath);
        stage.DefinePrim("/World", "Xform");
        stage.DefinePrim("/World/Child", "Xform");

        IReadOnlyList<UsdValidationError> errors = UsdValidation.Validate(stage);

        // Holds vacuously when a stage reports nothing, which is the common
        // case here, so this is stated as an ordering invariant rather than as
        // evidence that any particular stage produces errors.
        string[] keys = [.. errors.Select(error =>
            $"{error.ValidatorName}\u0000{error.ErrorName}\u0000{error.Message}")];

        await Assert.That(keys)
            .IsEquivalentTo([.. keys.Order(StringComparer.Ordinal)])
            .Because(
                "the facade must return validation errors in a stable ordinal " +
                "order, because parallel validator execution does not: " +
                string.Join(" | ", keys));
    }

    private static string Describe(IReadOnlyList<UsdValidationError> errors) =>
        string.Join(
            "; ",
            errors.Select(error =>
                $"{error.ValidatorName}/{error.ErrorName}: {error.Message}"));
}
