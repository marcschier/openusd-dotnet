// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Tests;

public sealed class UsdPhysicsTickSchedulerTests
{
    private static UsdPhysicsTickScheduler Create(
        double timeCodesPerSecond = 24,
        double endTimeCode = 24,
        double frequencyHz = 60,
        int maxSubSteps = 8)
    {
        var timeline = new UsdPhysicsTimeline(timeCodesPerSecond, 0, endTimeCode);
        UsdPhysicsFixedStep step = UsdPhysicsFixedStep.Resolve(timeline, frequencyHz);
        return new UsdPhysicsTickScheduler(timeline, step, maxSubSteps);
    }

    [Test]
    public async Task PartialIntervalsAccumulateInsteadOfStepping()
    {
        UsdPhysicsTickScheduler scheduler = Create();

        UsdPhysicsTickPlan first = scheduler.Plan(1.0 / 240);
        await Assert.That(first.SubSteps).IsEqualTo(0);
        scheduler.Commit(in first);

        UsdPhysicsTickPlan second = scheduler.Plan(1.0 / 240);
        await Assert.That(second.SubSteps).IsEqualTo(0);
        scheduler.Commit(in second);

        UsdPhysicsTickPlan third = scheduler.Plan(1.0 / 240);
        await Assert.That(third.SubSteps).IsEqualTo(0);
        scheduler.Commit(in third);

        UsdPhysicsTickPlan fourth = scheduler.Plan(1.0 / 240);
        await Assert.That(fourth.SubSteps).IsEqualTo(1);
        scheduler.Commit(in fourth);
        await Assert.That(scheduler.StepIndex).IsEqualTo(1ul);
    }

    [Test]
    public async Task CatchUpIsBoundedAndSlowsDownWithoutSkippingTime()
    {
        UsdPhysicsTickScheduler scheduler = Create(maxSubSteps: 8);

        UsdPhysicsTickPlan plan = scheduler.Plan(20.0 / 60);
        await Assert.That(plan.SubSteps).IsEqualTo(8);
        await Assert.That(plan.CatchUpLimited).IsTrue();
        scheduler.Commit(in plan);

        await Assert.That(scheduler.CatchUpLimitedTicks).IsEqualTo(1L);
        await Assert.That(scheduler.BacklogSeconds).IsGreaterThan(0.0);

        int advanced = 8;
        for (int tick = 0; tick < 8; tick++)
        {
            UsdPhysicsTickPlan next = scheduler.Plan(0);
            scheduler.Commit(in next);
            advanced += next.SubSteps;
            if (next.ReachedEnd)
            {
                break;
            }
        }

        await Assert.That(advanced).IsEqualTo(20);
        await Assert.That(scheduler.StepIndex).IsEqualTo(20ul);
    }

    [Test]
    public async Task ExactlyMaxSubStepsIsNotReportedAsLimited()
    {
        UsdPhysicsTickScheduler scheduler = Create(maxSubSteps: 8);

        UsdPhysicsTickPlan plan = scheduler.Plan(8.0 / 60);
        await Assert.That(plan.SubSteps).IsEqualTo(8);
        await Assert.That(plan.CatchUpLimited).IsFalse();
    }

    [Test]
    public async Task SimulationTimeIsTheExactProductOfStepsAndTheFixedStep()
    {
        UsdPhysicsTickScheduler scheduler = Create();

        for (int tick = 0; tick < 30; tick++)
        {
            UsdPhysicsTickPlan plan = scheduler.Plan(1.0 / 60);
            scheduler.Commit(in plan);
        }

        await Assert.That(scheduler.StepIndex).IsEqualTo(30ul);
        await Assert.That(scheduler.SimulationSeconds).IsEqualTo(30 * (1.0 / 60));
        await Assert.That(scheduler.TimeCode).IsEqualTo(30 * (1.0 / 60) * 24);
    }

    [Test]
    public async Task PlanStopsExactlyOnTheAuthoredEnd()
    {
        UsdPhysicsTickScheduler scheduler = Create();
        int advanced = 0;

        for (int tick = 0; tick < 200; tick++)
        {
            UsdPhysicsTickPlan plan = scheduler.Plan(1.0 / 60);
            scheduler.Commit(in plan);
            advanced += plan.SubSteps;
            if (plan.ReachedEnd)
            {
                break;
            }
        }

        await Assert.That(advanced).IsEqualTo(60);
        await Assert.That(scheduler.SimulationSeconds).IsEqualTo(1.0);
    }

    [Test]
    public async Task LoopWrapsToStartAndKeepsTheBacklog()
    {
        UsdPhysicsTickScheduler scheduler = Create();
        UsdPhysicsTickPlan plan = scheduler.Plan(2.0);
        scheduler.Commit(in plan);
        await Assert.That(plan.ReachedEnd).IsFalse();

        while (true)
        {
            UsdPhysicsTickPlan next = scheduler.Plan(0);
            scheduler.Commit(in next);
            if (next.ReachedEnd)
            {
                break;
            }
        }

        double backlogBefore = scheduler.BacklogSeconds;
        scheduler.CompleteLoop();

        await Assert.That(scheduler.LoopCount).IsEqualTo(1L);
        await Assert.That(scheduler.StepIndex).IsEqualTo(0ul);
        await Assert.That(scheduler.BacklogSeconds).IsEqualTo(backlogBefore);
    }

    [Test]
    public async Task EndingWithoutLoopDropsTheBacklog()
    {
        UsdPhysicsTickScheduler scheduler = Create();
        UsdPhysicsTickPlan plan = scheduler.Plan(5.0);
        scheduler.Commit(in plan);
        scheduler.CompleteWithoutLoop();

        await Assert.That(scheduler.BacklogSeconds).IsEqualTo(0.0);
    }

    [Test]
    public async Task UnboundedTimelineNeverReachesAnEnd()
    {
        UsdPhysicsTickScheduler scheduler = Create(endTimeCode: 0);

        for (int tick = 0; tick < 100; tick++)
        {
            UsdPhysicsTickPlan plan = scheduler.Plan(1.0 / 60);
            await Assert.That(plan.ReachedEnd).IsFalse();
            scheduler.Commit(in plan);
        }

        await Assert.That(scheduler.StepIndex).IsEqualTo(100ul);
    }

    [Test]
    public async Task SeekTargetsAreClampedIntoTheAuthoredRange()
    {
        UsdPhysicsTickScheduler scheduler = Create();

        await Assert.That(scheduler.StepsToTimeCode(0)).IsEqualTo(0ul);
        await Assert.That(scheduler.StepsToTimeCode(12)).IsEqualTo(30ul);
        await Assert.That(scheduler.StepsToTimeCode(24)).IsEqualTo(60ul);
        await Assert.That(scheduler.StepsToTimeCode(-100)).IsEqualTo(0ul);
        await Assert.That(scheduler.StepsToTimeCode(1000)).IsEqualTo(60ul);
    }

    [Test]
    public async Task NegativeAndNonFiniteIntervalsAreIgnored()
    {
        UsdPhysicsTickScheduler scheduler = Create();

        UsdPhysicsTickPlan plan = scheduler.Plan(-5);
        await Assert.That(plan.SubSteps).IsEqualTo(0);
        plan = scheduler.Plan(double.NaN);
        await Assert.That(plan.SubSteps).IsEqualTo(0);
        await Assert.That(scheduler.BacklogSeconds).IsEqualTo(0.0);
    }

    [Test]
    public async Task SubStepBoundIsNeverAllowedAboveTheHardLimit()
    {
        var timeline = new UsdPhysicsTimeline(24, 0, 24);
        UsdPhysicsFixedStep step = UsdPhysicsFixedStep.Resolve(timeline, 60);

        await Assert.That(() => new UsdPhysicsTickScheduler(timeline, step, 9))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new UsdPhysicsTickScheduler(timeline, step, 0))
            .Throws<ArgumentOutOfRangeException>();
    }
}
