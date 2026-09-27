// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkCpuTextureBudgetTests
{
    [Test]
    public async Task FullWidthCpuOwnershipAndSnapshotsRemainExactAcrossTransfers()
    {
        var budget = new SilkCpuTextureBudget(ulong.MaxValue);
        IDisposable lease = budget.Reserve(ulong.MaxValue);
        SilkGpuBufferUsage snapshot = budget.Usage;
        var image = new SilkDecodedImage(1, 1, new byte[4]);
        image.OwnReservation(lease);
        IDisposable transferred = image.TransferReservation()!;
        image.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(ulong.MaxValue);
        SilkCpuTextureBudgetExceededException? refused =
            await Assert.That(() => budget.Reserve(1)).Throws<SilkCpuTextureBudgetExceededException>();
        await Assert.That(refused!.RequestedBytes).IsEqualTo(1ul);
        await Assert.That(refused.ReservedBytes).IsEqualTo(ulong.MaxValue);
        transferred.Dispose();
        transferred.Dispose();
        await Assert.That(snapshot.ReservedBytes).IsEqualTo(ulong.MaxValue);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
        await Assert.That(() => new SilkCpuTextureBudget(0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => budget.Reserve(0)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task RefusedCopyLeavesEarlierOwnershipAndSourceBytesUnchanged()
    {
        var budget = new SilkCpuTextureBudget(8);
        List<IDisposable>? owners = null;
        byte[] source = [1, 2, 3, 4];
        try
        {
            byte[] first = budget.Copy(source, ref owners);
            byte[] second = budget.Copy(source, ref owners);
            await Assert.That(() => budget.Copy(source, ref owners)).Throws<SilkCpuTextureBudgetExceededException>();
            source[0] = 99;
            await Assert.That(first[0]).IsEqualTo((byte)1);
            await Assert.That(second[0]).IsEqualTo((byte)1);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(8ul);
            await Assert.That(owners!.Count).IsEqualTo(2);
        }
        finally
        {
            SilkCpuTextureBudget.ReleaseCopies(ref owners);
        }
        await Assert.That(owners).IsNull();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(8ul);
    }

    [Test]
    public async Task UnsupportedDevicesCannotSilentlyIgnoreCpuAdmission()
    {
        using var device = new SilkHdrColorReadbackDevice();
        await Assert.That(() => new SilkCpuTextureBudget(8).ConfigureDevice(device)).Throws<NotSupportedException>();
    }
}
