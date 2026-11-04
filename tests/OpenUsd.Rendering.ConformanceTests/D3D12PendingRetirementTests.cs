// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.Versioning;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Silk.D3D12;

namespace OpenUsd.Rendering.ConformanceTests;

[SupportedOSPlatform("windows")]
public sealed class D3D12PendingRetirementTests
{
    [Test]
    [Arguments(0, false)]
    [Arguments(unchecked((int)0x80004005), false)]
    [Arguments(unchecked((int)0x887A0005), true)]
    [Arguments(unchecked((int)0x887A0006), true)]
    [Arguments(unchecked((int)0x887A0007), true)]
    [Arguments(unchecked((int)0x887A0020), true)]
    public async Task RemovalClassificationAgreesAcrossRetirementPaths(int reason, bool expectedRemoval)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("The real D3D12 device retirement owner requires Windows.");
            return;
        }

        using var device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        device.SetTeardownHookForTesting(new TeardownHook(null, reason));
        try
        {
            await Assert.That(device.IsDeviceRemoved()).IsEqualTo(expectedRemoval);
            await Assert.That(device.HasTerminalDeviceRemoval()).IsEqualTo(expectedRemoval);
        }
        finally
        {
            device.SetTeardownHookForTesting(null);
        }
    }

    [Test]
    public async Task MultipleFailedRecordsStayOwnedWhileSuccessfulRecordsAreNeverRetried()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("The real D3D12 device retirement owner requires Windows.");
            return;
        }
        using var device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var budget = new SilkGpuStagingBudget(16);
        using var first = new Reservation(budget.Reserve(8), static () => { }) { Refuse = true };
        using var last = new Reservation(budget.Reserve(8), static () => { }) { Refuse = true };
        var hook = new TeardownHook(null, 0);
        device.SetTeardownHookForTesting(hook);
        device.AddRetainedRecordForTeardownTesting(first);
        device.AddRetainedRecordForTeardownTesting(last);
        try
        {
            AggregateException? aggregate = await Assert.That(device.Dispose).Throws<AggregateException>();
            await Assert.That(aggregate!.InnerExceptions.Count).IsEqualTo(2);
            await Assert.That(aggregate.InnerExceptions[0]).IsSameReferenceAs(first.Failure);
            await Assert.That(aggregate.InnerExceptions[1]).IsSameReferenceAs(last.Failure);
            await Assert.That(device.RetainedResourceCountForTesting).IsEqualTo(2);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(16ul);
            await Assert.That(device.TeardownReleaseCountsForTesting)
                .IsEqualTo(default(D3D12DeviceTeardownReleaseCounts));
            first.Refuse = false;
            InvalidOperationException? repeated = await Assert.That(device.Dispose).Throws<InvalidOperationException>();
            await Assert.That(repeated).IsSameReferenceAs(last.Failure);
            await Assert.That(first.Attempts).IsEqualTo(2);
            await Assert.That(last.Attempts).IsEqualTo(2);
            await Assert.That(device.RetainedResourceCountForTesting).IsEqualTo(1);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(8ul);
            await Assert.That(device.TeardownReleaseCountsForTesting)
                .IsEqualTo(new D3D12DeviceTeardownReleaseCounts(1, 0, 0, 0, 0, 0, 0, 0));
            last.Refuse = false;
            device.Dispose();
            await Assert.That(first.Attempts).IsEqualTo(2);
            await Assert.That(last.Attempts).IsEqualTo(3);
            await Assert.That(hook.WaitCount).IsEqualTo(1);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(device.NativeObjectsReleasedForTesting).IsTrue();
            await Assert.That(device.TeardownReleaseCountsForTesting)
                .IsEqualTo(new D3D12DeviceTeardownReleaseCounts(2, 1, 1, 1, 1, 1, 1, 1));
        }
        finally
        {
            first.Refuse = false;
            last.Refuse = false;
            device.Dispose();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FailedRetainedRecordKeepsNativeOwnerAndRejectsNewWorkUntilDisposeRetry(bool terminalRemoval)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("The real D3D12 device retirement owner requires Windows.");
            return;
        }
        var device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var budget = new SilkGpuStagingBudget(24);
        var events = new List<int>();
        var first = new Reservation(budget.Reserve(8), () => events.Add(0));
        var middle = new Reservation(budget.Reserve(8), () => events.Add(1)) { Refuse = true };
        var last = new Reservation(budget.Reserve(8), () => events.Add(2));
        var waitFailure = new InvalidOperationException("Injected terminal wait failure.");
        var hook = new TeardownHook(terminalRemoval ? waitFailure : null, unchecked((int)0x887A0005));
        using ISilkGraphicsCommandList commands = device.CreateCommandList();
        try
        {
            device.SetTeardownHookForTesting(hook);
            device.AddRetainedRecordForTeardownTesting(first);
            device.AddRetainedRecordForTeardownTesting(middle);
            device.AddRetainedRecordForTeardownTesting(last);
            Exception? observed = null;
            try
            { device.Dispose(); }
            catch (Exception failure) { observed = failure; }
            if (terminalRemoval)
            {
                var aggregate = observed as AggregateException;
                await Assert.That(aggregate).IsNotNull();
                await Assert.That(aggregate!.InnerExceptions.Count).IsEqualTo(2);
                var removal = aggregate.InnerExceptions[0] as D3D12DeviceRemovalTeardownException;
                await Assert.That(removal).IsNotNull();
                await Assert.That(removal!.RemovalReason).IsEqualTo(unchecked((int)0x887A0005));
                await Assert.That(removal.InnerException).IsSameReferenceAs(waitFailure);
                await Assert.That(aggregate.InnerExceptions[1]).IsSameReferenceAs(middle.Failure);
            }
            else
            {
                await Assert.That(observed).IsSameReferenceAs(middle.Failure);
            }
            await Assert.That(string.Join(",", events)).IsEqualTo("0,1,2");
            await Assert.That(first.Attempts).IsEqualTo(1);
            await Assert.That(middle.Attempts).IsEqualTo(1);
            await Assert.That(last.Attempts).IsEqualTo(1);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(8ul);
            await Assert.That(device.RetainedResourceCountForTesting).IsEqualTo(1);
            await Assert.That(device.NativeObjectsReleasedForTesting).IsFalse();
            await Assert.That(device.TeardownReleaseCountsForTesting)
                .IsEqualTo(new D3D12DeviceTeardownReleaseCounts(2, 0, 0, 0, 0, 0, 0, 0));
            await Assert.That(() => device.CreateCommandList()).Throws<ObjectDisposedException>();
            await Assert.That(() => device.Submit(commands)).Throws<ObjectDisposedException>();
            await Assert.That(() => device.CreateBuffer(8, SilkBufferUsage.Upload)).Throws<ObjectDisposedException>();
            await Assert.That(() => device.ConfigureStagingBudget(new SilkGpuStagingBudget(8)))
                .Throws<ObjectDisposedException>();
            await Assert.That(device.WaitIdle).Throws<ObjectDisposedException>();

            Exception? repeatedFailure = null;
            try
            { device.Dispose(); }
            catch (Exception failure) { repeatedFailure = failure; }
            if (terminalRemoval)
            {
                var aggregate = repeatedFailure as AggregateException;
                await Assert.That(aggregate).IsNotNull();
                await Assert.That(aggregate!.InnerExceptions.Count).IsEqualTo(2);
                await Assert.That(aggregate.InnerExceptions[0].InnerException).IsSameReferenceAs(waitFailure);
                await Assert.That(aggregate.InnerExceptions[1]).IsSameReferenceAs(middle.Failure);
            }
            else
            { await Assert.That(repeatedFailure).IsSameReferenceAs(middle.Failure); }
            await Assert.That(device.TeardownReleaseCountsForTesting)
                .IsEqualTo(new D3D12DeviceTeardownReleaseCounts(2, 0, 0, 0, 0, 0, 0, 0));
            await Assert.That(device.RetainedResourceCountForTesting).IsEqualTo(1);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(8ul);

            middle.Refuse = false;
            if (terminalRemoval)
            {
                D3D12DeviceRemovalTeardownException? removal =
                    await Assert.That(device.Dispose).Throws<D3D12DeviceRemovalTeardownException>();
                await Assert.That(removal!.InnerException).IsSameReferenceAs(waitFailure);
                await Assert.That(removal.RemovalReason).IsEqualTo(unchecked((int)0x887A0005));
            }
            else
            { device.Dispose(); }
            await Assert.That(string.Join(",", events)).IsEqualTo("0,1,2,1,1");
            await Assert.That(first.Attempts).IsEqualTo(1);
            await Assert.That(middle.Attempts).IsEqualTo(3);
            await Assert.That(last.Attempts).IsEqualTo(1);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(hook.WaitCount).IsEqualTo(1);
            await Assert.That(hook.RemovalReasonCount).IsEqualTo(terminalRemoval ? 1 : 0);
            await Assert.That(device.NativeObjectsReleasedForTesting).IsTrue();
            var completed = new D3D12DeviceTeardownReleaseCounts(3, 1, 1, 1, 1, 1, 1, 1);
            await Assert.That(device.TeardownReleaseCountsForTesting).IsEqualTo(completed);
            device.Dispose();
            await Assert.That(device.TeardownReleaseCountsForTesting).IsEqualTo(completed);
        }
        finally
        {
            middle.Refuse = false;
            try
            { device.Dispose(); }
            catch (D3D12DeviceRemovalTeardownException) when (terminalRemoval) { }
            first.Dispose();
            middle.Dispose();
            last.Dispose();
        }
    }

    [Test]
    public async Task NonterminalWaitFailureKeepsRecordsUntouchedAndAllowsNewWorkBeforeRetry()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("The real D3D12 device retirement owner requires Windows.");
            return;
        }
        using var device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var budget = new SilkGpuStagingBudget(8);
        using var reservation = new Reservation(budget.Reserve(8), static () => { });
        var waitFailure = new InvalidOperationException("Injected nonterminal wait failure.");
        var hook = new TeardownHook(waitFailure, unchecked((int)0x80004005));
        device.SetTeardownHookForTesting(hook);
        device.AddRetainedRecordForTeardownTesting(reservation);
        try
        {
            InvalidOperationException? observed =
                await Assert.That(device.Dispose).Throws<InvalidOperationException>();
            await Assert.That(observed).IsSameReferenceAs(waitFailure);
            await Assert.That(reservation.Attempts).IsEqualTo(0);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(8ul);
            await Assert.That(device.RetainedResourceCountForTesting).IsEqualTo(1);
            await Assert.That(device.TeardownReleaseCountsForTesting)
                .IsEqualTo(default(D3D12DeviceTeardownReleaseCounts));
            using (ISilkGraphicsCommandList commands = device.CreateCommandList())
            {
                await Assert.That(commands).IsNotNull();
            }
            using (ISilkGraphicsBuffer buffer = device.CreateBuffer(8, SilkBufferUsage.Upload))
            {
                await Assert.That(buffer.Size).IsEqualTo((nuint)8);
            }
            hook.WaitFailure = null;
            device.Dispose();
            await Assert.That(hook.WaitCount).IsEqualTo(2);
            await Assert.That(reservation.Attempts).IsEqualTo(1);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(device.NativeObjectsReleasedForTesting).IsTrue();
        }
        finally { device.SetTeardownHookForTesting(null); }
    }

    [Test]
    public async Task ConcurrentAndReentrantDisposeReturnWhileFailedRecordRemainsOwnedForLaterRetry()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("The real D3D12 device retirement owner requires Windows.");
            return;
        }
        using var device = D3D12SilkGraphicsDevice.Create(useWarp: true);
        var budget = new SilkGpuStagingBudget(8);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        int reentrantReturns = 0;
        using var reservation = new Reservation(budget.Reserve(8), () =>
        {
            device.Dispose();
            Interlocked.Increment(ref reentrantReturns);
            entered.TrySetResult();
            if (!resume.Wait(TimeSpan.FromSeconds(10)))
            { throw new TimeoutException("Retirement test did not resume."); }
        })
        { Refuse = true };
        var hook = new TeardownHook(null, 0);
        device.SetTeardownHookForTesting(hook);
        device.AddRetainedRecordForTeardownTesting(reservation);
        Task<Exception?> first = Task.Run(() =>
        {
            try
            { device.Dispose(); return null; }
            catch (Exception failure) { return failure; }
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Run(device.Dispose).WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(reentrantReturns).IsEqualTo(1);
            await Assert.That(() => device.CreateCommandList()).Throws<ObjectDisposedException>();
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(8ul);
            resume.Set();
            await Assert.That(await first.WaitAsync(TimeSpan.FromSeconds(10))).IsSameReferenceAs(reservation.Failure);
            await Assert.That(reservation.Attempts).IsEqualTo(1);
            await Assert.That(device.NativeObjectsReleasedForTesting).IsFalse();
            reservation.Refuse = false;
            device.Dispose();
            await Assert.That(reservation.Attempts).IsEqualTo(2);
            await Assert.That(reentrantReturns).IsEqualTo(2);
            await Assert.That(hook.WaitCount).IsEqualTo(1);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
            await Assert.That(device.NativeObjectsReleasedForTesting).IsTrue();
            device.Dispose();
            await Assert.That(reservation.Attempts).IsEqualTo(2);
        }
        finally
        {
            resume.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
            reservation.Refuse = false;
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class TeardownHook(Exception? waitFailure, int removalReason) : ID3D12DeviceTeardownHook
    {
        internal Exception? WaitFailure { get; set; } = waitFailure;
        internal int WaitCount { get; private set; }
        internal int RemovalReasonCount { get; private set; }
        public void WaitIdle()
        {
            WaitCount++;
            if (WaitFailure is { } failure)
            { throw failure; }
        }
        public int GetDeviceRemovedReason()
        {
            RemovalReasonCount++;
            return removalReason;
        }
    }

    private sealed class Reservation(IDisposable credit, Action releasing) : IDisposable
    {
        private IDisposable? _credit = credit;
        internal bool Refuse { get; set; }
        internal int Attempts { get; private set; }
        internal InvalidOperationException Failure { get; } = new("Injected retained-record reservation refusal.");
        public void Dispose()
        {
            if (_credit is null)
            { return; }
            Attempts++;
            releasing();
            if (Refuse)
            { throw Failure; }
            _credit.Dispose();
            _credit = null;
        }
    }
}
