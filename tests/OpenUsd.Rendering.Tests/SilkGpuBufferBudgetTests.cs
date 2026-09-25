// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkGpuBufferBudgetTests
{
    [Test]
    public async Task ExactCapacityIsAdmittedAndOneByteMoreIsRefusedBeforeCreation()
    {
        var budget = new SilkGpuBufferBudget(64);
        var device = new BudgetDevice();
        device.ConfigureBufferBudget(budget);
        using BudgetBuffer first = device.Create(64);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(64ul);
        await Assert.That(() =>
        {
            using BudgetBuffer rejected = device.Create(1);
        }).Throws<SilkGpuBufferBudgetExceededException>();
        await Assert.That(device.NativeCreates).IsEqualTo(1);
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(64ul);
        first.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        using BudgetBuffer retry = device.Create(64);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(1ul);
    }

    [Test]
    public async Task UnsupportedDevicesCannotAcceptAnUnenforcedBudget()
    {
        using var device = new SilkHdrColorReadbackDevice();
        var budget = new SilkGpuBufferBudget(100);
        await Assert.That(() => budget.ConfigureDevice(device)).Throws<NotSupportedException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    public async Task SubmissionLeasesRetainTheChargeAfterThePublicBufferIsDisposed()
    {
        var budget = new SilkGpuBufferBudget(64);
        var device = new BudgetDevice();
        device.ConfigureBufferBudget(budget);
        using BudgetBuffer buffer = device.Create(64);
        IDisposable firstLease = buffer.Borrow();
        IDisposable lastLease = buffer.Borrow();
        buffer.Dispose();
        firstLease.Dispose();
        firstLease.Dispose();
        await Assert.That(buffer.NativeReleases).IsEqualTo(0);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(64ul);
        await Assert.That(() =>
        {
            using BudgetBuffer refused = device.Create(1);
        }).Throws<SilkGpuBufferBudgetExceededException>();
        lastLease.Dispose();
        buffer.Dispose();
        await Assert.That(buffer.NativeReleases).IsEqualTo(1);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    public async Task TwoDevicesShareOnePoolAndFailedCreationReleasesOnlyItsReservation()
    {
        var budget = new SilkGpuBufferBudget(100);
        var first = new BudgetDevice();
        var second = new BudgetDevice();
        first.ConfigureBufferBudget(budget);
        second.ConfigureBufferBudget(budget);
        using BudgetBuffer a = first.Create(60);
        second.FailCreation = true;
        await Assert.That(() => second.Create(40)).Throws<InvalidOperationException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(60ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(1ul);
        second.FailCreation = false;
        using BudgetBuffer b = second.Create(40);
        await Assert.That(() =>
        {
            using BudgetBuffer refused = first.Create(1);
        }).Throws<SilkGpuBufferBudgetExceededException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(100ul);
        a.Dispose();
        using BudgetBuffer c = second.Create(60);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(100ul);
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(100ul);
    }

    [Test]
    public async Task BudgetConfigurationCannotReplaceOrRetroactivelyMeasureAnExistingDevice()
    {
        var budget = new SilkGpuBufferBudget(64);
        var device = new BudgetDevice();
        device.ConfigureBufferBudget(budget);
        device.ConfigureBufferBudget(budget);
        await Assert.That(device.GpuBufferBudget).IsSameReferenceAs(budget);
        await Assert.That(() => device.ConfigureBufferBudget(new(128))).Throws<InvalidOperationException>();
        using BudgetBuffer buffer = device.Create(64);
        device.ConfigureBufferBudget(budget);
        var unbounded = new BudgetDevice();
        using BudgetBuffer earlier = unbounded.Create(1);
        await Assert.That(() => unbounded.ConfigureBufferBudget(budget)).Throws<InvalidOperationException>();
        await Assert.That(unbounded.GpuBufferBudget).IsNull();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(64ul);
    }

    [Test]
    public async Task FullWidthLimitsAndUsageSnapshotsRemainExactAndImmutable()
    {
        var budget = new SilkGpuBufferBudget(ulong.MaxValue);
        using IDisposable maximum = budget.Reserve(ulong.MaxValue);
        SilkGpuBufferUsage before = budget.Usage;
        SilkGpuBufferBudgetExceededException? failure =
            await Assert.That(() => budget.Reserve(1)).Throws<SilkGpuBufferBudgetExceededException>();
        await Assert.That(failure!.RequestedBytes).IsEqualTo(1ul);
        await Assert.That(failure.ReservedBytes).IsEqualTo(ulong.MaxValue);
        await Assert.That(failure.MaximumBytes).IsEqualTo(ulong.MaxValue);
        maximum.Dispose();
        await Assert.That(before.ReservedBytes).IsEqualTo(ulong.MaxValue);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(ulong.MaxValue);
        await Assert.That(() => new SilkGpuBufferBudget(0)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ConcurrentRequestsCannotOvercommitTheSharedPool()
    {
        var budget = new SilkGpuBufferBudget(64);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int arrived = 0;
        int admitted = 0;
        int refused = 0;
        Task[] tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
        {
            IDisposable? reservation = null;
            try
            {
                reservation = budget.Reserve(16);
                Interlocked.Increment(ref admitted);
            }
            catch (SilkGpuBufferBudgetExceededException)
            {
                Interlocked.Increment(ref refused);
            }
            if (Interlocked.Increment(ref arrived) == 16)
            {
                ready.SetResult();
            }
            await release.Task;
            reservation?.Dispose();
        })).ToArray();
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(64ul);
            await Assert.That(budget.Usage.ReservationCount).IsEqualTo(4ul);
            await Assert.That(admitted).IsEqualTo(4);
            await Assert.That(refused).IsEqualTo(12);
        }
        finally
        {
            release.SetResult();
            await Task.WhenAll(tasks);
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(64ul);
    }

    [Test]
    public async Task FailedNativeReleaseDoesNotReturnUnprovenCapacity()
    {
        var budget = new SilkGpuBufferBudget(64);
        var device = new BudgetDevice();
        device.ConfigureBufferBudget(budget);
        BudgetBuffer buffer = device.Create(64);
        buffer.FailRelease = true;
        await Assert.That(buffer.Dispose).Throws<InvalidOperationException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(64ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(1ul);
    }

    [Test]
    [Arguments("")]
    [Arguments("0")]
    [Arguments("-1")]
    [Arguments("+1")]
    [Arguments(" 1")]
    [Arguments("1.5")]
    [Arguments("1,000")]
    [Arguments("18446744073709551616")]
    public async Task InvalidConfigurationCannotSilentlyRemoveTheBudget(string value)
    {
        await Assert.That(() => SilkGpuBufferBudget.FromConfiguration(value)).Throws<ArgumentException>();
        await Assert.That(SilkGpuBufferBudget.FromConfiguration(null)).IsNull();
        await Assert.That(SilkGpuBufferBudget.FromConfiguration("18446744073709551615")!.MaximumBytes)
            .IsEqualTo(ulong.MaxValue);
    }

    private sealed class BudgetDevice : SilkGraphicsDeviceLifetimeBase
    {
        internal int NativeCreates { get; private set; }
        internal bool FailCreation { get; set; }

        internal BudgetBuffer Create(nuint size)
        {
            IDisposable? reservation = ReserveBufferAllocation(size);
            try
            {
                NativeCreates++;
                if (FailCreation)
                {
                    throw new InvalidOperationException("Injected native creation failure.");
                }
                var result = new BudgetBuffer(size);
                result.OwnBufferReservation(reservation);
                return result;
            }
            catch
            {
                reservation?.Dispose();
                throw;
            }
        }
    }

    private sealed class BudgetBuffer(nuint size) : SilkGraphicsBufferBase(size, SilkBufferUsage.Upload)
    {
        internal int NativeReleases { get; private set; }
        internal bool FailRelease { get; set; }
        internal IDisposable Borrow() => AcquireBufferLease();
        public override void Write(ReadOnlySpan<byte> data, nuint offset = 0) => _ = ValidateWrite(data.Length, offset);
        public override void ReadbackForTesting(Span<byte> destination) => throw new NotSupportedException();
        protected override void ReleaseNative()
        {
            if (FailRelease)
            {
                throw new InvalidOperationException("Injected native release failure.");
            }
            NativeReleases++;
        }
    }
}
