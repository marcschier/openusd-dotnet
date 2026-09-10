// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerProductResourceRegistryTests
{
    [Test]
    public async Task FailedProductCleanupKeepsOwnershipAndRetriesOnlyTheFailedResources()
    {
        using var registry = new ViewerProductResourceRegistry();
        ViewerProductResourceRegistry.Lease lease = registry.CreateLease();
        var order = new List<string>();
        var session = lease.Own(new Resource("session", order, failures: 1));
        var capturer = lease.Own(new Resource("capturer", order, failures: 2));
        await Assert.That(lease.Dispose).Throws<AggregateException>();
        await Assert.That(order.SequenceEqual(["capturer", "session"])).IsTrue();
        await Assert.That(registry.Count).IsEqualTo(1);
        await Assert.That(registry.Dispose).Throws<AggregateException>();
        await Assert.That(capturer.Attempts).IsEqualTo(2);
        await Assert.That(session.Attempts).IsEqualTo(2);
        await Assert.That(registry.Count).IsEqualTo(1);
        registry.Dispose();
        await Assert.That(registry.Count).IsEqualTo(0);
        await Assert.That(capturer.Attempts).IsEqualTo(3);
        await Assert.That(session.Attempts).IsEqualTo(2);
        lease.Dispose();
        await Assert.That(capturer.Attempts).IsEqualTo(3);
    }

    [Test]
    public async Task OneFailedProductDoesNotPreventOtherOwnedCleanup()
    {
        using var registry = new ViewerProductResourceRegistry();
        var order = new List<string>();
        ViewerProductResourceRegistry.Lease failing = registry.CreateLease();
        Resource first = failing.Own(new Resource("first", order, failures: 1));
        ViewerProductResourceRegistry.Lease passing = registry.CreateLease();
        Resource second = passing.Own(new Resource("second", order, failures: 0));
        await Assert.That(registry.Dispose).Throws<AggregateException>();
        await Assert.That(first.Attempts).IsEqualTo(1);
        await Assert.That(second.Attempts).IsEqualTo(1);
        await Assert.That(registry.Count).IsEqualTo(1);
        registry.Dispose();
        await Assert.That(first.Attempts).IsEqualTo(2);
        await Assert.That(second.Attempts).IsEqualTo(1);
        await Assert.That(registry.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DeviceLifetimePinsReleaseOnlyAfterAllCaptureResourcesAreClean()
    {
        using var registry = new ViewerProductResourceRegistry();
        var order = new List<string>();
        ViewerProductResourceRegistry.Lease lease = registry.CreateLease();
        Resource pin = lease.Pin(new Resource("device-lifetime", order, failures: 1));
        Resource session = lease.Own(new Resource("session", order, failures: 0));
        Resource capturer = lease.Own(new Resource("capturer", order, failures: 1));

        await Assert.That(lease.Dispose).Throws<AggregateException>();
        await Assert.That(order).IsEquivalentTo(["capturer", "session"]);
        await Assert.That(pin.Attempts).IsEqualTo(0);
        await Assert.That(registry.Count).IsEqualTo(1);
        await Assert.That(registry.Dispose).Throws<AggregateException>();
        await Assert.That(capturer.Attempts).IsEqualTo(2);
        await Assert.That(session.Attempts).IsEqualTo(1);
        await Assert.That(pin.Attempts).IsEqualTo(1);
        await Assert.That(registry.Count).IsEqualTo(1);
        registry.Dispose();
        await Assert.That(pin.Attempts).IsEqualTo(2);
        await Assert.That(registry.Count).IsEqualTo(0);
    }

    private sealed class Resource(string name, List<string> order, int failures) : IDisposable
    {
        internal int Attempts { get; private set; }

        public void Dispose()
        {
            order.Add(name);
            Attempts++;
            if (Attempts <= failures)
            {
                throw new IOException($"Controlled {name} cleanup failure.");
            }
        }
    }
}
