// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Tests;

/// <summary>
/// Asserts the transport's interactive runtime command path: that a whole batch is staged on the
/// worker thread, that it is applied by the next advance and only once, and that a world which is
/// not in a state to accept commands refuses them instead of silently dropping them.
/// </summary>
public sealed class UsdPhysicsTransportCommandTests
{
    private const double TimeCodesPerSecond = 24.0;
    private const double FrequencyHz = 60.0;

    [Test]
    public async Task AWholeBatchIsStagedInSubmissionOrder()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());

        UsdPhysicsCommandSubmission submission = await RunAsync(
            transport,
            transport.SubmitCommandsAsync(
            [
                new UsdPhysicsCommand(
                    UsdPhysicsCommandKind.Wake,
                    new UsdPhysicsObjectId(1, UsdPhysicsObjectKind.RigidBody),
                    default),
                new UsdPhysicsCommand(
                    UsdPhysicsCommandKind.Impulse,
                    new UsdPhysicsObjectId(2, UsdPhysicsObjectKind.RigidBody),
                    new UsdVec3d(0, 1, 0),
                    5),
            ]));

        await Assert.That(submission.Accepted).IsEqualTo(2);
        await Assert.That(submission.Rejected).IsEqualTo(0);
        await Assert.That(submission.IsComplete).IsTrue();
        await Assert.That(world.StagedCommands.Count).IsEqualTo(2);
        await Assert.That(world.StagedCommands[0].Kind).IsEqualTo(UsdPhysicsCommandKind.Wake);
        await Assert.That(world.StagedCommands[1].Kind).IsEqualTo(UsdPhysicsCommandKind.Impulse);
    }

    [Test]
    public async Task StagingNeverAdvancesTheWorldAndTheNextStepConsumesTheBatchExactlyOnce()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());

        await RunAsync(
            transport,
            transport.SubmitCommandsAsync(
                [new UsdPhysicsCommand(
                    UsdPhysicsCommandKind.Wake,
                    new UsdPhysicsObjectId(1, UsdPhysicsObjectKind.RigidBody),
                    default)]));

        await Assert.That(world.SubStepsAdvanced).IsEqualTo(0L);
        await Assert.That(world.AppliedCommands).IsEqualTo(0);

        await RunAsync(transport, transport.StepAsync(1));
        await Assert.That(world.AppliedCommands).IsEqualTo(1);
        await Assert.That(world.StagedCommands).IsEmpty();

        await RunAsync(transport, transport.StepAsync(1));
        await Assert.That(world.AppliedCommands).IsEqualTo(1);
    }

    [Test]
    public async Task AnEmptyBatchIsAnEmptyOutcomeRatherThanAQueuedRequest()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());

        UsdPhysicsCommandSubmission submission = await transport.SubmitCommandsAsync([]);

        await Assert.That(submission.Accepted).IsEqualTo(0);
        await Assert.That(submission.Rejected).IsEqualTo(0);
        await Assert.That(submission.Message).IsNotEmpty();
    }

    [Test]
    public async Task AnUnbuiltWorldRefusesTheWholeBatchAndSaysWhy()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);

        UsdPhysicsCommandSubmission submission = await RunAsync(
            transport,
            transport.SubmitCommandsAsync(
                [new UsdPhysicsCommand(
                    UsdPhysicsCommandKind.Wake,
                    new UsdPhysicsObjectId(1, UsdPhysicsObjectKind.RigidBody),
                    default)]));

        await Assert.That(submission.Accepted).IsEqualTo(0);
        await Assert.That(submission.Rejected).IsEqualTo(1);
        await Assert.That(submission.IsComplete).IsFalse();
        await Assert.That(world.StagedCommands).IsEmpty();
    }

    [Test]
    public async Task AnInvalidatedWorldRefusesCommandsAndDropsTheOnesAlreadyStaged()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(
            transport,
            transport.SubmitCommandsAsync(
                [new UsdPhysicsCommand(
                    UsdPhysicsCommandKind.Wake,
                    new UsdPhysicsObjectId(1, UsdPhysicsObjectKind.RigidBody),
                    default)]));

        await RunAsync(
            transport,
            transport.InvalidateAsync(UsdPhysicsInvalidationReason.External));

        await Assert.That(world.DiscardCount).IsGreaterThan(0);
        await Assert.That(world.StagedCommands).IsEmpty();

        UsdPhysicsCommandSubmission submission = await RunAsync(
            transport,
            transport.SubmitCommandsAsync(
                [new UsdPhysicsCommand(
                    UsdPhysicsCommandKind.Wake,
                    new UsdPhysicsObjectId(1, UsdPhysicsObjectKind.RigidBody),
                    default)]));
        await Assert.That(submission.Rejected).IsEqualTo(1);
    }

    [Test]
    public async Task ResettingDropsTheStagedBatchSoAnInputIsNeverReplayed()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());
        await RunAsync(
            transport,
            transport.SubmitCommandsAsync(
                [new UsdPhysicsCommand(
                    UsdPhysicsCommandKind.Impulse,
                    new UsdPhysicsObjectId(1, UsdPhysicsObjectKind.RigidBody),
                    new UsdVec3d(0, 1, 0),
                    3)]));

        await RunAsync(transport, transport.ResetAsync());

        await Assert.That(world.StagedCommands).IsEmpty();
        await RunAsync(transport, transport.StepAsync(1));
        await Assert.That(world.AppliedCommands).IsEqualTo(0);
    }

    [Test]
    public async Task AWorldThatRefusesCommandsReportsTheRefusalRatherThanSucceeding()
    {
        var world = new FakePhysicsWorld { RefuseCommands = true };
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);
        await RunAsync(transport, transport.BuildAsync());

        UsdPhysicsCommandSubmission submission = await RunAsync(
            transport,
            transport.SubmitCommandsAsync(
                [new UsdPhysicsCommand(
                    UsdPhysicsCommandKind.Wake,
                    new UsdPhysicsObjectId(1, UsdPhysicsObjectKind.RigidBody),
                    default)]));

        await Assert.That(submission.Accepted).IsEqualTo(0);
        await Assert.That(submission.Rejected).IsEqualTo(1);
        await Assert.That(submission.Message).IsNotEmpty();
    }

    [Test]
    public async Task SubmittingANullBatchIsRefusedAtTheApiBoundary()
    {
        var world = new FakePhysicsWorld();
        var clock = new FakePhysicsClock();
        await using UsdPhysicsTransport transport = CreateTransport(world, clock);

        await Assert.That(async () => await transport.SubmitCommandsAsync(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task TheSubmissionResultRefusesAnImpossibleShape()
    {
        await Assert.That(() => new UsdPhysicsCommandSubmission(-1, 0, "message"))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new UsdPhysicsCommandSubmission(0, -1, "message"))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new UsdPhysicsCommandSubmission(0, 0, "  "))
            .Throws<ArgumentException>();
    }

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

    private static async Task<TResult> RunAsync<TResult>(
        UsdPhysicsTransport transport,
        Task<TResult> request)
    {
        transport.Pump();
        return await request;
    }
}
