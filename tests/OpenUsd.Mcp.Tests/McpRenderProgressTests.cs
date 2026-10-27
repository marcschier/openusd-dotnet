// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp.Tests;

public sealed class McpRenderProgressTests
{
    [Test]
    public async Task PrecancelledProgressDoesNotStartRenderingOrDelivery()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        int calls = 0;
        await Assert.That(async () => await McpRenderProgress.RunAsync<int>((_, _) =>
        {
            calls++;
            return ValueTask.FromResult(1);
        }, (_, _, _) =>
        {
            calls++;
            return ValueTask.CompletedTask;
        }, cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(2, 1)]
    [Arguments(1, 4097)]
    public async Task InvalidFrameExtentsFailDeliveryRatherThanInventingProgress(int completed, int total)
    {
        int notifications = 0;
        await Assert.That(async () => await McpRenderProgress.RunAsync<int>((report, _) =>
        {
            report(completed, total);
            return ValueTask.FromResult(1);
        }, (_, _, _) =>
        {
            notifications++;
            return ValueTask.CompletedTask;
        }, default)).Throws<InvalidDataException>();
        await Assert.That(notifications).IsEqualTo(0);
    }

    [Test]
    public async Task SlowNotificationsCoalesceToOnePendingFrameAndDrainBeforeReturningTheResult()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var encoded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = new List<(int, int)>();
        Task<int> pending = McpRenderProgress.RunAsync(async (report, token) =>
        {
            report(1, 4096);
            await first.Task.WaitAsync(token);
            for (int index = 2; index <= 4096; index++)
            {
                report(index, 4096);
            }
            encoded.TrySetResult();
            return 42;
        }, async (completed, total, token) =>
        {
            notifications.Add((completed, total));
            if (completed == 1)
            {
                first.TrySetResult();
                await release.Task.WaitAsync(token);
            }
        }, default).AsTask();
        try
        {
            await encoded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(notifications.SequenceEqual([(1, 4096)])).IsTrue();
            release.TrySetResult();
            await Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(42);
            await Assert.That(notifications.SequenceEqual([(1, 4096), (4096, 4096)])).IsTrue();
        }
        finally
        {
            release.TrySetResult();
            await pending;
        }
    }

    [Test]
    public async Task NotificationFailureCancelsButWaitsForActiveRenderingCleanup()
    {
        var cancelling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new IOException("Progress transport disconnected.");
        Task<int> pending = McpRenderProgress.RunAsync<int>(async (report, token) =>
        {
            report(1, 2);
            try
            {
                await Task.Delay(Timeout.Infinite, token);
                return 1;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                cancelling.TrySetResult();
                await release.Task;
                drained.TrySetResult();
                throw;
            }
        }, (_, _, _) => ValueTask.FromException(error), default).AsTask();
        try
        {
            await cancelling.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(drained.Task.IsCompleted).IsFalse();
            release.TrySetResult();
            IOException? failure = await Assert.That(async () => await pending).Throws<IOException>();
            await Assert.That(failure).IsSameReferenceAs(error);
            await Assert.That(drained.Task.IsCompleted).IsTrue();
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Test]
    public async Task SimultaneousRenderAndNotificationFailuresRetainBothCauses()
    {
        var notification = new IOException("Notification failed.");
        var rendering = new InvalidDataException("Renderer cleanup failed.");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AggregateException? failure = await Assert.That(async () => await McpRenderProgress.RunAsync<int>(
            async (report, token) =>
            {
                report(1, 2);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                throw rendering;
            }, (_, _, _) =>
            {
                entered.TrySetResult();
                return ValueTask.FromException(notification);
            }, default)).Throws<AggregateException>();
        await Assert.That(failure!.InnerExceptions.SequenceEqual([rendering, notification])).IsTrue();
    }

    [Test]
    public async Task OperationWithoutFramesDoesNotInventProgressAndPreservesItsFailure()
    {
        int notifications = 0;
        var error = new ArgumentException("Request refused before rendering.");
        ArgumentException? failure = await Assert.That(async () => await McpRenderProgress.RunAsync<int>(
            (_, _) => ValueTask.FromException<int>(error),
            (_, _, _) =>
            {
                notifications++;
                return ValueTask.CompletedTask;
            }, default)).Throws<ArgumentException>();
        await Assert.That(failure).IsSameReferenceAs(error);
        await Assert.That(notifications).IsEqualTo(0);
    }
}
