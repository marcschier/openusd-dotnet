// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkOwnedResourceLeasesTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task OwnerFailureRetainsFailedAndUnvisitedRealBaseLeasesAndRetriesOnlyRemainingWork(int failAt)
    {
        var budget = new SilkGpuBufferBudget(24);
        Resource[] resources = [new(budget), new(budget), new(budget)];
        IDisposable?[] leases = resources.Select(resource => resource.Borrow()).ToArray();
        IDisposable?[] original = [.. leases];
        var owner = new Owner(leases);
        foreach (Resource resource in resources)
        { resource.Dispose(); }
        resources[failAt].Refuse = true;
        await Assert.That(owner.Dispose).Throws<InvalidOperationException>();
        await Assert.That(owner.IsReleased).IsFalse();
        for (int index = 0; index < resources.Length; index++)
        {
            await Assert.That(resources[index].Attempts).IsEqualTo(index <= failAt ? 1 : 0);
            await Assert.That(resources[index].Releases).IsEqualTo(index < failAt ? 1 : 0);
            await Assert.That(leases[index]).IsSameReferenceAs(index < failAt ? null : original[index]);
        }
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(checked((ulong)(3 - failAt) * 8));
        resources[failAt].Refuse = false;
        owner.Dispose();
        owner.Dispose();
        await Assert.That(owner.IsReleased).IsTrue();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        for (int index = 0; index < resources.Length; index++)
        {
            await Assert.That(resources[index].Attempts).IsEqualTo(index == failAt ? 2 : 1);
            await Assert.That(resources[index].Releases).IsEqualTo(1);
            await Assert.That(leases[index]).IsNull();
        }
    }

    [Test]
    public async Task ResourceOwnerCanCompleteFailedDestructionBeforeTheLeaseArrayOwnerRetries()
    {
        var budget = new SilkGpuBufferBudget(16);
        using var first = new Resource(budget);
        using var last = new Resource(budget);
        var owner = new Owner([first.Borrow(), last.Borrow()]);
        first.Dispose();
        last.Dispose();
        first.Refuse = true;
        await Assert.That(owner.Dispose).Throws<InvalidOperationException>();
        await Assert.That(last.Attempts).IsEqualTo(0);
        first.Refuse = false;
        first.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(8ul);
        owner.Dispose();
        await Assert.That(owner.IsReleased).IsTrue();
        await Assert.That(first.Attempts).IsEqualTo(2);
        await Assert.That(last.Attempts).IsEqualTo(1);
        await Assert.That(first.Releases).IsEqualTo(1);
        await Assert.That(last.Releases).IsEqualTo(1);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    [Test]
    public async Task EmptyAndCompletedOwnersAreIdempotent()
    {
        var empty = new Owner([]);
        empty.Dispose();
        empty.Dispose();
        await Assert.That(empty.IsReleased).IsTrue();
    }

    [Test]
    public async Task ReentrantArrayRetirementCannotDiscardTheCurrentOrFollowingLease()
    {
        var budget = new SilkGpuBufferBudget(16);
        using var first = new Resource(budget);
        using var last = new Resource(budget);
        IDisposable?[] leases = [first.Borrow(), last.Borrow()];
        var owner = new Owner(leases);
        first.Dispose();
        last.Dispose();
        first.OnRelease = owner.Dispose;
        await Assert.That(owner.Dispose).Throws<InvalidOperationException>();
        await Assert.That(leases[0]).IsNotNull();
        await Assert.That(leases[1]).IsNotNull();
        await Assert.That(first.Releases).IsEqualTo(0);
        await Assert.That(last.Attempts).IsEqualTo(0);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(16ul);
        first.OnRelease = null;
        owner.Dispose();
        await Assert.That(first.Attempts).IsEqualTo(2);
        await Assert.That(first.Releases).IsEqualTo(1);
        await Assert.That(last.Releases).IsEqualTo(1);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
    }

    private sealed class Owner(IDisposable?[] resources) : IDisposable
    {
        private IDisposable?[]? _resources = resources;
        private bool _releasing;
        internal bool IsReleased => _resources is null;
        public void Dispose() => SilkOwnedResourceLeases.Dispose(ref _resources, ref _releasing);
    }

    private sealed class Resource : SilkGraphicsResourceBase
    {
        internal Resource(SilkGpuBufferBudget budget) => OwnBufferReservation(budget.Reserve(8));
        internal bool Refuse { get; set; }
        internal int Attempts { get; private set; }
        internal int Releases { get; private set; }
        internal Action? OnRelease { get; set; }
        internal IDisposable Borrow() => AcquireResourceLease();

        protected override void ReleaseNative()
        {
            Attempts++;
            if (Refuse)
            { throw new InvalidOperationException("Injected owned lease release failure."); }
            OnRelease?.Invoke();
            Releases++;
        }
    }
}
