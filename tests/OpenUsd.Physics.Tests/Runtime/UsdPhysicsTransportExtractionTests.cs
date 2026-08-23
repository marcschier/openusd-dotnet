// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Extraction;
using OpenUsd.Physics.Tests.Extraction;

namespace OpenUsd.Physics.Tests;

/// <summary>
/// Asserts that the retained world is given the extracted stage it is supposed to compose from.
/// </summary>
/// <remarks>
/// A transport that never attaches an extraction still builds, still steps, and still publishes
/// frames - it just publishes empty ones, because the world composed authored timeline metadata and
/// nothing else. Every symptom of that is downstream and misleading: bindings exist, snapshots are
/// ingested, and only the override count is zero. These tests pin the attach itself.
/// </remarks>
public sealed class UsdPhysicsTransportExtractionTests
{
    private const double TimeCodesPerSecond = 24.0;
    private const double FrequencyHz = 60.0;

    [Test]
    public async Task AWorldWithNothingAttachedStartsWithNoExtraction()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);

        await RunAsync(transport, transport.BuildAsync());

        await Assert.That(world.Attachments).IsEqualTo(0);
        await Assert.That(world.Extraction).IsNull();
    }

    [Test]
    public async Task AnAttachedExtractionReachesTheWorldOnTheOwningThread()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        UsdPhysicsExtractionPage page = EmptyPage();

        await RunAsync(transport, transport.AttachExtractionAsync(page));

        await Assert.That(world.Attachments).IsEqualTo(1);
        await Assert.That(world.Extraction).IsSameReferenceAs(page);
    }

    [Test]
    public async Task AnAttachQueuedBeforeABuildIsAppliedBeforeIt()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        UsdPhysicsExtractionPage page = EmptyPage();

        // The build must compose the page the caller just extracted, so the attach has to land
        // first. Recording what the world held when Build ran is the only way to prove ordering.
        UsdPhysicsExtractionPage? attachedWhenBuilt = null;
        world.OnBuild = observed => attachedWhenBuilt = observed.Extraction;

        Task attach = transport.AttachExtractionAsync(page);
        Task build = transport.BuildAsync();
        transport.Pump();
        await attach;
        await build;

        await Assert.That(attachedWhenBuilt).IsSameReferenceAs(page);
        await Assert.That(world.BuildCount).IsEqualTo(1);
    }

    [Test]
    public async Task AttachingNullDetachesTheExtraction()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.AttachExtractionAsync(EmptyPage()));

        await RunAsync(transport, transport.AttachExtractionAsync(null));

        await Assert.That(world.Attachments).IsEqualTo(2);
        await Assert.That(world.Extraction).IsNull();
    }

    [Test]
    public async Task ARebuildComposesTheNewestAttachedExtraction()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        UsdPhysicsExtractionPage first = EmptyPage();
        UsdPhysicsExtractionPage second = EmptyPage();

        await RunAsync(transport, transport.AttachExtractionAsync(first));
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(transport, transport.AttachExtractionAsync(second));
        await RunAsync(transport, transport.BuildAsync());

        await Assert.That(world.Extraction).IsSameReferenceAs(second);
        await Assert.That(world.BuildCount).IsEqualTo(2);
    }

    private static UsdPhysicsExtractionPage EmptyPage() =>
        new UsdPhysicsExtractionPageFixture().BuildPage();

    private static UsdPhysicsTransport CreateTransport(
        FakePhysicsWorld world,
        FakePhysicsClock clock) =>
        UsdPhysicsTransport.CreateForTesting(
            world,
            new UsdPhysicsTimeline(TimeCodesPerSecond, 0, 24.0),
            new UsdPhysicsTransportOptions(
                new UsdPhysicsSessionOptions(
                    maxSubStepsPerTick: 8,
                    fixedFrequencyOverrideHz: FrequencyHz),
                loop: false,
                requestQueueCapacity: 64),
            clock);

    private static async Task RunAsync(UsdPhysicsTransport transport, Task request)
    {
        transport.Pump();
        await request;
    }
}
