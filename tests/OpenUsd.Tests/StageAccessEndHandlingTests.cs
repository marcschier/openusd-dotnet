// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.ExceptionServices;
using OpenUsd.Interop;

namespace OpenUsd.Tests;

public sealed class StageAccessEndHandlingTests
{
    [Test]
    public async Task InvalidArgumentIsThrownNormallyAndExecutionContinues()
    {
        var expected = new OpenUsdNativeException(
            OpenUsdNativeStatus.InvalidArgument,
            "No live stage-access guard exists.");

        Exception actual = Capture(
            () => OpenUsdNativeRuntime.HandleStageAccessEndFailure(expected));
        bool continued = true;

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(((OpenUsdNativeException)actual).Status)
            .IsEqualTo(OpenUsdNativeStatus.InvalidArgument);
        await Assert.That(continued).IsTrue();
    }

    [Test]
    public async Task OtherEndFailuresAreThrownNormally()
    {
        var expected = new OpenUsdNativeException(
            OpenUsdNativeStatus.NativeError,
            "Synthetic native release failure.");

        Exception actual = Capture(
            () => OpenUsdNativeRuntime.HandleStageAccessEndFailure(expected));

        await Assert.That(actual).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task SuccessfulEndRethrowsOriginalCallbackException()
    {
        var expected = new InvalidOperationException("Synthetic callback failure.");

        Exception actual = Capture(() =>
            OpenUsdNativeRuntime.ThrowStageAccessFailures(
                ExceptionDispatchInfo.Capture(expected),
                endFailure: null));

        await Assert.That(actual).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task CallbackAndEndFailuresAreBothPreserved()
    {
        var callbackFailure = new InvalidOperationException("Synthetic callback failure.");
        var endFailure = new OpenUsdNativeException(
            OpenUsdNativeStatus.InvalidArgument,
            "Synthetic end failure.");

        Exception actual = Capture(() =>
            OpenUsdNativeRuntime.ThrowStageAccessFailures(
                ExceptionDispatchInfo.Capture(callbackFailure),
                ExceptionDispatchInfo.Capture(endFailure)));

        var combined = (InvalidOperationException)actual;
        var aggregate = (AggregateException)combined.InnerException!;
        await Assert.That(combined.Message)
            .IsEqualTo(OpenUsdNativeRuntime.StageAccessCombinedFailureMessage);
        await Assert.That(aggregate.InnerExceptions[0])
            .IsSameReferenceAs(callbackFailure);
        await Assert.That(aggregate.InnerExceptions[1])
            .IsSameReferenceAs(endFailure);
    }

    private static Exception Capture(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected the operation to throw.");
    }
}
