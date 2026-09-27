// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkGpuStagingBudgetTests
{
    [Test]
    public async Task ExactFullWidthReservationAndSnapshotRemainIndependentOfOtherPools()
    {
        var budget = new SilkGpuStagingBudget(ulong.MaxValue);
        using IDisposable lease = budget.Reserve(ulong.MaxValue);
        SilkGpuBufferUsage before = budget.Usage;
        SilkGpuStagingBudgetExceededException? failure =
            await Assert.That(() => budget.Reserve(1)).Throws<SilkGpuStagingBudgetExceededException>();
        await Assert.That(failure!.RequestedBytes).IsEqualTo(1ul);
        await Assert.That(failure.ReservedBytes).IsEqualTo(ulong.MaxValue);
        await Assert.That(failure.MaximumBytes).IsEqualTo(ulong.MaxValue);
        lease.Dispose();
        lease.Dispose();
        await Assert.That(before.ReservedBytes).IsEqualTo(ulong.MaxValue);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(ulong.MaxValue);
        await Assert.That(() => budget.Reserve(0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new SilkGpuStagingBudget(0)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task DeviceConfigurationIsIndependentImmutableAndSealedByTheFirstTransfer()
    {
        var device = new Device();
        var staging = new SilkGpuStagingBudget(16);
        device.ConfigureStagingBudget(staging);
        device.ConfigureStagingBudget(staging);
        device.ConfigureBufferBudget(new(1));
        device.ConfigureTextureBudget(new(1));
        using IDisposable? transfer = device.Reserve(16);
        device.ConfigureStagingBudget(staging);
        await Assert.That(device.GpuStagingBudget).IsSameReferenceAs(staging);
        await Assert.That(device.GpuBufferBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(device.GpuTextureBudget!.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(() => device.ConfigureStagingBudget(new(32))).Throws<InvalidOperationException>();
        await Assert.That(() => device.Reserve(1)).Throws<SilkGpuStagingBudgetExceededException>();
        var unbounded = new Device();
        await Assert.That(unbounded.Reserve(1)).IsNull();
        await Assert.That(() => unbounded.ConfigureStagingBudget(staging)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UnsupportedDevicesCannotAcceptAnUnenforcedTransferBudget()
    {
        using var device = new SilkHdrColorReadbackDevice();
        await Assert.That(() => new SilkGpuStagingBudget(64).ConfigureDevice(device)).Throws<NotSupportedException>();
    }

    private sealed class Device : SilkGraphicsDeviceLifetimeBase
    {
        internal IDisposable? Reserve(ulong bytes) => ReserveStagingAllocation(bytes);
    }
}
