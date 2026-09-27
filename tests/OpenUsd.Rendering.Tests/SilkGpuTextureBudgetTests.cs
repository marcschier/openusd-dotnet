// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkGpuTextureBudgetTests
{
    private static SilkTextureDescriptor Mips => new(
        5, 3, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.Sampled, 3);

    [Test]
    public async Task EveryMipCountsAndOneByteBelowCapacityRefusesBeforeNativeCreation()
    {
        var rejected = new Device();
        var below = new SilkGpuTextureBudget(71);
        rejected.ConfigureTextureBudget(below);
        SilkGpuTextureBudgetExceededException? failure =
            await Assert.That(() => rejected.Create(Mips)).Throws<SilkGpuTextureBudgetExceededException>();
        await Assert.That(failure!.RequestedBytes).IsEqualTo(72ul);
        await Assert.That(failure.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(failure.MaximumBytes).IsEqualTo(71ul);
        await Assert.That(rejected.Creates).IsEqualTo(0);
        await Assert.That(below.Usage.ReservationCount).IsEqualTo(0ul);

        var device = new Device();
        var budget = new SilkGpuTextureBudget(72);
        device.ConfigureTextureBudget(budget);
        using Texture texture = device.Create(Mips);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(72ul);
        await Assert.That(() => device.Create(new(1, 1, SilkTextureFormat.R32Float, SilkTextureUsage.Sampled)))
            .Throws<SilkGpuTextureBudgetExceededException>();
        await Assert.That(device.Creates).IsEqualTo(1);
        texture.Dispose();
        using Texture retry = device.Create(Mips);
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(72ul);
    }

    [Test]
    public async Task VolumeSlicesAndHdrMipsShareOnePoolAcrossDevices()
    {
        var budget = new SilkGpuTextureBudget(136);
        var first = new Device();
        var second = new Device();
        first.ConfigureTextureBudget(budget);
        second.ConfigureTextureBudget(budget);
        using Texture volume = first.Create(new(2, 3, SilkTextureFormat.R32Float, SilkTextureUsage.Sampled), 4);
        using Texture hdr = second.Create(new(2, 2, SilkTextureFormat.Rgba16Float, SilkTextureUsage.Sampled, 2));
        SilkGpuTextureUsage snapshot = budget.Usage;
        await Assert.That(snapshot.ReservedBytes).IsEqualTo(136ul);
        await Assert.That(snapshot.ReservationCount).IsEqualTo(2ul);
        volume.Dispose();
        hdr.Dispose();
        await Assert.That(snapshot.ReservedBytes).IsEqualTo(136ul);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    public async Task SubmissionLeasesHoldCreditUntilTheFinalNativeRelease()
    {
        var budget = new SilkGpuTextureBudget(72);
        var device = new Device();
        device.ConfigureTextureBudget(budget);
        using Texture texture = device.Create(Mips);
        IDisposable first = texture.Borrow();
        IDisposable last = texture.Borrow();
        texture.Dispose();
        first.Dispose();
        first.Dispose();
        await Assert.That(texture.Releases).IsEqualTo(0);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(72ul);
        await Assert.That(() => device.Create(Mips)).Throws<SilkGpuTextureBudgetExceededException>();
        last.Dispose();
        last.Dispose();
        await Assert.That(texture.Releases).IsEqualTo(1);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task FailedCreationReturnsOnlyItsOwnReservation()
    {
        var budget = new SilkGpuTextureBudget(144);
        var first = new Device();
        var second = new Device { FailCreation = true };
        first.ConfigureTextureBudget(budget);
        second.ConfigureTextureBudget(budget);
        using Texture retained = first.Create(Mips);
        await Assert.That(() => second.Create(Mips)).Throws<InvalidOperationException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(72ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(1ul);
        second.FailCreation = false;
        using Texture retry = second.Create(Mips);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(144ul);
    }

    [Test]
    public async Task ConfigurationIsImmutableAndCannotRetroactivelyAccountForOwnedTextures()
    {
        var budget = new SilkGpuTextureBudget(72);
        var device = new Device();
        device.ConfigureTextureBudget(budget);
        using Texture texture = device.Create(Mips);
        device.ConfigureTextureBudget(budget);
        await Assert.That(device.GpuTextureBudget).IsSameReferenceAs(budget);
        await Assert.That(() => device.ConfigureTextureBudget(new(144))).Throws<InvalidOperationException>();
        var unbounded = new Device();
        using Texture earlier = unbounded.Create(Mips);
        await Assert.That(() => unbounded.ConfigureTextureBudget(budget)).Throws<InvalidOperationException>();
        await Assert.That(unbounded.GpuTextureBudget).IsNull();
        var buffers = new SilkGpuBufferBudget(64);
        device.ConfigureBufferBudget(buffers);
        await Assert.That(buffers.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(72ul);
        await Assert.That(() => new SilkGpuTextureBudget(0)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task LogicalTextureAccountingIsFullWidthAndOverflowCannotReachTheFactory()
    {
        var budget = new SilkGpuTextureBudget(ulong.MaxValue);
        var device = new Device();
        device.ConfigureTextureBudget(budget);
        using Texture wide = device.Create(new(65536, 16384, SilkTextureFormat.Rgba32Float, SilkTextureUsage.Sampled));
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(17179869184ul);
        await Assert.That(() => device.Create(
            new(uint.MaxValue, uint.MaxValue, SilkTextureFormat.Rgba32Float, SilkTextureUsage.Sampled)))
            .Throws<OverflowException>();
        await Assert.That(device.Creates).IsEqualTo(1);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(1ul);
    }

    [Test]
    public async Task NativeReleaseFailureWithholdsUnprovenCredit()
    {
        var budget = new SilkGpuTextureBudget(72);
        var device = new Device();
        device.ConfigureTextureBudget(budget);
        Texture texture = device.Create(Mips);
        texture.FailRelease = true;
        await Assert.That(texture.Dispose).Throws<InvalidOperationException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(72ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(1ul);
    }

    [Test]
    public async Task UnsupportedDevicesRefuseRatherThanAcceptAnUnenforcedBudget()
    {
        using var device = new SilkHdrColorReadbackDevice();
        await Assert.That(() => new SilkGpuTextureBudget(72).ConfigureDevice(device)).Throws<NotSupportedException>();
    }

    [Test]
    public async Task ConcurrentRequestsCannotExceedTheSharedTextureCeiling()
    {
        var budget = new SilkGpuTextureBudget(256);
        var first = new Device();
        var second = new Device();
        first.ConfigureTextureBudget(budget);
        second.ConfigureTextureBudget(budget);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int arrived = 0;
        int accepted = 0;
        int refused = 0;
        Task[] requests = Enumerable.Range(0, 16).Select(index => Task.Run(async () =>
        {
            Texture? texture = null;
            try
            {
                texture = (index % 2 == 0 ? first : second).Create(
                    new(4, 4, SilkTextureFormat.Rgba8Unorm, SilkTextureUsage.Sampled));
                Interlocked.Increment(ref accepted);
            }
            catch (SilkGpuTextureBudgetExceededException)
            {
                Interlocked.Increment(ref refused);
            }
            finally
            {
                if (Interlocked.Increment(ref arrived) == 16)
                {
                    ready.TrySetResult();
                }
            }
            await release.Task;
            texture?.Dispose();
        })).ToArray();
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(accepted).IsEqualTo(4);
            await Assert.That(refused).IsEqualTo(12);
            await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(256ul);
            await Assert.That(budget.Usage.ReservationCount).IsEqualTo(4ul);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(requests);
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(256ul);
    }

    private sealed class Device : SilkGraphicsDeviceLifetimeBase
    {
        private int _creates;
        internal int Creates => Volatile.Read(ref _creates);
        internal bool FailCreation { get; set; }

        internal Texture Create(SilkTextureDescriptor descriptor, uint depth = 1)
        {
            IDisposable? reservation = ReserveTextureAllocation(descriptor, depth);
            try
            {
                Interlocked.Increment(ref _creates);
                if (FailCreation)
                {
                    throw new InvalidOperationException("Injected native texture creation failure.");
                }
                var texture = new Texture(descriptor);
                texture.OwnTextureReservation(reservation);
                return texture;
            }
            catch
            {
                reservation?.Dispose();
                throw;
            }
        }
    }

    private sealed class Texture(SilkTextureDescriptor descriptor) : SilkGraphicsTextureBase(descriptor)
    {
        internal int Releases { get; private set; }
        internal bool FailRelease { get; set; }
        internal IDisposable Borrow() => AcquireSubmissionLease();
        public override void ReadbackForTesting(Span<byte> destination) => throw new NotSupportedException();
        public override void ReadbackForTesting(Span<float> destination) => throw new NotSupportedException();
        protected override void ReleaseNative()
        {
            if (FailRelease)
            {
                throw new InvalidOperationException("Injected native texture release failure.");
            }
            Releases++;
        }
    }
}
