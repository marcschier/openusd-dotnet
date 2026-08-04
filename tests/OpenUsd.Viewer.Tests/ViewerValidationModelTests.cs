// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerValidationModelTests
{
    [Test]
    public async Task ValidationSnapshotFormatsErrorsInUsdValidationOrder()
    {
        UsdValidationValidatorInfo[] validators =
        [
            new("validator.a", "A", "usdValidation", [], [], IsSuite: false, IsTimeDependent: false),
            new("validator.b", "B", "usdValidation", [], [], IsSuite: false, IsTimeDependent: false)
        ];
        UsdValidationError[] errors =
        [
            new(UsdValidationSeverity.Error, "validator.a", "first", "First issue", ["/World/A"]),
            new(UsdValidationSeverity.Warning, "validator.b", "middle", "Middle issue", ["/World/B"]),
            new(UsdValidationSeverity.Info, "validator.b", "last", "Last issue", ["/World/C"])
        ];

        ViewerValidationSnapshot snapshot = ViewerValidationSnapshot.Create(
            validators,
            errors,
            TimeSpan.FromMilliseconds(2));
        string state = ViewerValidationFormatter.FormatState(snapshot);
        string details = ViewerValidationFormatter.FormatDetails(snapshot);

        await Assert.That(state).Contains("3 result(s) from 2 validator(s)");
        await Assert.That(state).Contains("errors: 1; warnings: 1; info: 1");
        await Assert.That(details.IndexOf("First issue", StringComparison.Ordinal))
            .IsLessThan(details.IndexOf("Middle issue", StringComparison.Ordinal));
        await Assert.That(details.IndexOf("Middle issue", StringComparison.Ordinal))
            .IsLessThan(details.IndexOf("Last issue", StringComparison.Ordinal));
        await Assert.That(details).Contains("Sites: /World/A");
        await Assert.That(details).Contains("Sites: /World/B");
        await Assert.That(details).Contains("Sites: /World/C");
    }

    [Test]
    public async Task ValidationSnapshotsCompareErrorCollectionsByValue()
    {
        UsdValidationError[] leftErrors =
        [
            new(UsdValidationSeverity.Error, "validator", "error", "Message", ["/World"])
        ];
        UsdValidationError[] rightErrors =
        [
            new(UsdValidationSeverity.Error, "validator", "error", "Message", ["/World"])
        ];

        ViewerValidationSnapshot left = ViewerValidationSnapshot.Create(
            [],
            leftErrors,
            TimeSpan.FromMilliseconds(1));
        ViewerValidationSnapshot right = ViewerValidationSnapshot.Create(
            [],
            rightErrors,
            TimeSpan.FromMilliseconds(1));

        await Assert.That(left).IsEqualTo(right);
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
    }
}
