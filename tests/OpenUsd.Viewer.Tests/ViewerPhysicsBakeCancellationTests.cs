// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Asserts that a running physics bake can always be canceled: the cancel affordance never routes
/// through the transport command gate the bake itself is holding, and the hand-off between the bake
/// completing and the user cancelling never touches a disposed source.
/// </summary>
public sealed class ViewerPhysicsBakeCancellationTests
{
    [Test]
    public async Task ARunningBakeCanBeCanceledWhileItHoldsTheCommandGate()
    {
        using var cancellation = new ViewerPhysicsBakeCancellation();
        using ViewerPhysicsBakeLease lease = cancellation.Begin(CancellationToken.None);

        await Assert.That(cancellation.IsRunning).IsTrue();
        await Assert.That(lease.IsCancellationRequested).IsFalse();

        await Assert.That(cancellation.Cancel()).IsTrue();
        await Assert.That(lease.Token.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task NoBakeIsRunningSoCancellingIsAQuietNoOp()
    {
        using var cancellation = new ViewerPhysicsBakeCancellation();

        await Assert.That(cancellation.IsRunning).IsFalse();
        await Assert.That(cancellation.Cancel()).IsFalse();
    }

    [Test]
    public async Task ClosingTheDocumentCancelsTheBakeSoItsRollbackRuns()
    {
        using var document = new CancellationTokenSource();
        using var cancellation = new ViewerPhysicsBakeCancellation();
        using ViewerPhysicsBakeLease lease = cancellation.Begin(document.Token);

        document.Cancel();

        await Assert.That(lease.Token.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task ABakeThatFinishedFirstIsNotCanceledAndNothingIsDisposedTwice()
    {
        using var cancellation = new ViewerPhysicsBakeCancellation();
        ViewerPhysicsBakeLease lease = cancellation.Begin(CancellationToken.None);
        lease.Dispose();

        await Assert.That(cancellation.IsRunning).IsFalse();
        await Assert.That(cancellation.Cancel()).IsFalse();

        // The bake's own finally and a repeated release must both be safe.
        lease.Dispose();
        await Assert.That(cancellation.IsRunning).IsFalse();
    }

    [Test]
    public async Task ANewBakeStopsAnyBakeThatSomehowStillHoldsALease()
    {
        using var cancellation = new ViewerPhysicsBakeCancellation();
        using ViewerPhysicsBakeLease stale = cancellation.Begin(CancellationToken.None);
        using ViewerPhysicsBakeLease current = cancellation.Begin(CancellationToken.None);

        await Assert.That(stale.Token.IsCancellationRequested).IsTrue();
        await Assert.That(current.Token.IsCancellationRequested).IsFalse();

        // Cancelling reaches the bake that is actually current, not the superseded one.
        await Assert.That(cancellation.Cancel()).IsTrue();
        await Assert.That(current.Token.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task ReleasingAStaleLeaseNeverStealsTheRunningBake()
    {
        using var cancellation = new ViewerPhysicsBakeCancellation();
        ViewerPhysicsBakeLease stale = cancellation.Begin(CancellationToken.None);
        using ViewerPhysicsBakeLease current = cancellation.Begin(CancellationToken.None);

        stale.Dispose();

        await Assert.That(cancellation.IsRunning).IsTrue();
        await Assert.That(cancellation.Cancel()).IsTrue();
        await Assert.That(current.Token.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task ABakeStartedAfterTeardownIsHandedAnAlreadyCanceledToken()
    {
        var cancellation = new ViewerPhysicsBakeCancellation();
        cancellation.Dispose();

        using ViewerPhysicsBakeLease lease = cancellation.Begin(CancellationToken.None);

        await Assert.That(lease.Token.IsCancellationRequested).IsTrue();
        await Assert.That(cancellation.IsRunning).IsFalse();
    }

    [Test]
    public async Task DisposingWhileABakeRunsCancelsItAndStaysSafeToRepeat()
    {
        var cancellation = new ViewerPhysicsBakeCancellation();
        using ViewerPhysicsBakeLease lease = cancellation.Begin(CancellationToken.None);

        cancellation.Dispose();
        cancellation.Dispose();

        await Assert.That(lease.Token.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async Task CancellingWhileTheBakeCompletesNeverThrowsFromEitherSide()
    {
        // The cancel button and the bake's own completion race on every real cancel, so this walks
        // the race repeatedly rather than sampling one interleaving.
        for (int attempt = 0; attempt < 200; attempt++)
        {
            using var cancellation = new ViewerPhysicsBakeCancellation();
            ViewerPhysicsBakeLease lease = cancellation.Begin(CancellationToken.None);
            using var start = new Barrier(2);

            Task release = Task.Run(() =>
            {
                start.SignalAndWait();
                lease.Dispose();
            });
            Task cancel = Task.Run(() =>
            {
                start.SignalAndWait();
                _ = cancellation.Cancel();
            });

            await Task.WhenAll(release, cancel);
            await Assert.That(cancellation.IsRunning).IsFalse();
        }
    }

    [Test]
    public async Task ManyOverlappingBakesLeaveExactlyOneRunningLease()
    {
        using var cancellation = new ViewerPhysicsBakeCancellation();
        var leases = new ViewerPhysicsBakeLease[16];

        Parallel.For(0, leases.Length, index =>
        {
            leases[index] = cancellation.Begin(CancellationToken.None);
        });

        int live = 0;
        foreach (ViewerPhysicsBakeLease lease in leases)
        {
            if (!lease.IsCancellationRequested)
            {
                live++;
            }
        }

        await Assert.That(live).IsEqualTo(1);
        await Assert.That(cancellation.IsRunning).IsTrue();

        foreach (ViewerPhysicsBakeLease lease in leases)
        {
            lease.Dispose();
        }

        await Assert.That(cancellation.IsRunning).IsFalse();
    }
}
