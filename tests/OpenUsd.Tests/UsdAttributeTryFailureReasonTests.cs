// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed class UsdAttributeTryFailureReasonTests
{
    [Test]
    public async Task TryGetExistingValueCoreReportsNativeFailureWithoutThrowing()
    {
        bool result = UsdAttribute.TryGetExistingValueCore(
            () => throw new OpenUsdNativeException(
                OpenUsdNativeStatus.NativeError,
                "Synthetic native failure."),
            out UsdScalarValue value,
            out UsdAttributeTryFailureReason failureReason);

        await Assert.That(result).IsFalse();
        await Assert.That(value).IsEqualTo(default(UsdScalarValue));
        await Assert.That(failureReason)
            .IsEqualTo(UsdAttributeTryFailureReason.NativeCallFailed);
    }
}
