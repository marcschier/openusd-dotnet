// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Tests;

public sealed class RenderDirectoryRetryTests
{
    [Test]
    [Arguments(5)]
    [Arguments(32)]
    [Arguments(33)]
    public async Task TransientFailuresWaitForEachBoundaryAndSucceedOnTheFifthAttempt(int errorCode)
    {
        RequireWindows();
        var clock = new ManualRetryTimeProvider();
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        Task operation = Start(() =>
        {
            if (Interlocked.Increment(ref calls) <= 4)
            {
                throw Error(errorCode);
            }
        }, clock, cancellation.Token);
        try
        {
            int attempt = 0;
            foreach (int milliseconds in new[] { 20, 40, 80, 160 })
            {
                TimeSpan delay = await clock.NextDelayAsync();
                await Assert.That(delay).IsEqualTo(TimeSpan.FromMilliseconds(milliseconds));
                await Assert.That(Volatile.Read(ref calls)).IsEqualTo(++attempt);
                clock.Advance(delay - TimeSpan.FromMilliseconds(1));
                await Assert.That(operation.IsCompleted).IsFalse();
                await Assert.That(Volatile.Read(ref calls)).IsEqualTo(attempt);
                clock.Advance(TimeSpan.FromMilliseconds(1));
            }
            await operation;
            await Assert.That(calls).IsEqualTo(5);
            await Assert.That(clock.CreatedTimers).IsEqualTo(4);
            await Assert.That(clock.ActiveTimers).IsEqualTo(0);
            await Assert.That(clock.GetUtcNow() - DateTimeOffset.UnixEpoch).IsEqualTo(TimeSpan.FromMilliseconds(300));
        }
        finally
        {
            await DrainAsync(operation, clock, cancellation);
        }
    }

    [Test]
    public async Task PersistentSharingFailureStopsAfterFourDelays()
    {
        RequireWindows();
        var clock = new ManualRetryTimeProvider();
        var failure = Error(32);
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        Task operation = Start(() =>
        {
            Interlocked.Increment(ref calls);
            throw failure;
        }, clock, cancellation.Token);
        try
        {
            foreach (int milliseconds in new[] { 20, 40, 80, 160 })
            {
                TimeSpan delay = await clock.NextDelayAsync();
                await Assert.That(delay).IsEqualTo(TimeSpan.FromMilliseconds(milliseconds));
                clock.Advance(delay);
            }
            IOException? thrown = await Assert.ThrowsAsync<IOException>(() => operation);
            await Assert.That(ReferenceEquals(thrown, failure)).IsTrue();
            await Assert.That(calls).IsEqualTo(5);
            await Assert.That(clock.CreatedTimers).IsEqualTo(4);
            await Assert.That(clock.ActiveTimers).IsEqualTo(0);
        }
        finally
        {
            await DrainAsync(operation, clock, cancellation);
        }
    }

    [Test]
    public async Task CancellationDuringDelayDoesNotRepeatTheOperation()
    {
        RequireWindows();
        var clock = new ManualRetryTimeProvider();
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        Task operation = Start(() =>
        {
            Interlocked.Increment(ref calls);
            throw Error(32);
        }, clock, cancellation.Token);
        try
        {
            await Assert.That(await clock.NextDelayAsync()).IsEqualTo(TimeSpan.FromMilliseconds(20));
            clock.Advance(TimeSpan.FromMilliseconds(19));
            await Assert.That(operation.IsCompleted).IsFalse();
            cancellation.Cancel();
            OperationCanceledException? thrown = await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
            await Assert.That(thrown).IsNotNull();
            await Assert.That(thrown!.CancellationToken).IsEqualTo(cancellation.Token);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            await Assert.That(calls).IsEqualTo(1);
            await Assert.That(clock.CreatedTimers).IsEqualTo(1);
            await Assert.That(clock.ActiveTimers).IsEqualTo(0);
        }
        finally
        {
            await DrainAsync(operation, clock, cancellation);
        }
    }

    [Test]
    [Arguments(2)]
    [Arguments(80)]
    [Arguments(183)]
    public async Task OtherIoErrorsAreNotRetried(int errorCode)
    {
        var clock = new ManualRetryTimeProvider();
        var failure = Error(errorCode);
        int calls = 0;
        IOException? thrown = await Assert.ThrowsAsync<IOException>(() => Task.Run(() =>
            RenderDiskJob.RetryDirectorySharing(() =>
            {
                calls++;
                throw failure;
            }, clock, CancellationToken.None)));
        await Assert.That(ReferenceEquals(thrown, failure)).IsTrue();
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(clock.CreatedTimers).IsEqualTo(0);
    }

    [Test]
    public async Task AlreadyCanceledRequestsDoNotCallTheOperationOrCreateATimer()
    {
        var clock = new ManualRetryTimeProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int calls = 0;
        _ = await Assert.ThrowsAsync<OperationCanceledException>(() => Task.Run(() =>
            RenderDiskJob.RetryDirectorySharing(() => calls++, clock, cancellation.Token)));
        await Assert.That(calls).IsEqualTo(0);
        await Assert.That(clock.CreatedTimers).IsEqualTo(0);
    }

    private static Task Start(Action operation, TimeProvider clock, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(
            () => RenderDiskJob.RetryDirectorySharing(operation, clock, cancellationToken),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static async Task DrainAsync(
        Task operation, ManualRetryTimeProvider clock, CancellationTokenSource cancellation)
    {
        cancellation.Cancel();
        clock.Advance(TimeSpan.FromDays(1));
        try
        {
            await operation;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
    }

    private static IOException Error(int code) => new("Controlled I/O failure.", unchecked((int)0x80070000) | code);

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("Sharing-violation retries are Windows-specific.");
            throw new InvalidOperationException("Skip.Test returned unexpectedly.");
        }
    }
}
