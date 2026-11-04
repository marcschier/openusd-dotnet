// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkResourceReleaseStepsTests
{
    [Test]
    [Arguments(8, 0)]
    [Arguments(8, 1)]
    [Arguments(8, 2)]
    [Arguments(8, 3)]
    [Arguments(8, 4)]
    [Arguments(8, 5)]
    [Arguments(8, 6)]
    [Arguments(8, 7)]
    [Arguments(5, 0)]
    [Arguments(5, 1)]
    [Arguments(5, 2)]
    [Arguments(5, 3)]
    [Arguments(5, 4)]
    public async Task NativeRetirementResumesAtTheFailedStepWithoutRepeatingCompletedDestruction(int count, int failAt)
    {
        SilkResourceReleaseSteps steps = default;
        var attempts = new int[count];
        var releases = new int[count];
        bool refuse = true;
        void Release(int step)
        {
            attempts[step]++;
            if (refuse && step == failAt)
            { throw new InvalidOperationException("Injected native retirement step failure."); }
            releases[step]++;
        }
        await Assert.That(() => steps.Release(count, Release)).Throws<InvalidOperationException>();
        await Assert.That(steps.Completed).IsEqualTo(failAt);
        for (int step = 0; step < count; step++)
        {
            await Assert.That(attempts[step]).IsEqualTo(step <= failAt ? 1 : 0);
            await Assert.That(releases[step]).IsEqualTo(step < failAt ? 1 : 0);
        }
        refuse = false;
        steps.Release(count, Release);
        steps.Release(count, Release);
        await Assert.That(steps.Completed).IsEqualTo(count);
        for (int step = 0; step < count; step++)
        {
            await Assert.That(attempts[step]).IsEqualTo(step == failAt ? 2 : 1);
            await Assert.That(releases[step]).IsEqualTo(1);
        }
    }

    [Test]
    [Arguments(-1)]
    [Arguments(0)]
    [Arguments(33)]
    public async Task InvalidOrChangedRetirementPlansRefuseBeforeTheNextRelease(int invalidCount)
    {
        SilkResourceReleaseSteps steps = default;
        int calls = 0;
        void Release(int step) => calls++;
        await Assert.That(() => steps.Release(invalidCount, Release)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(calls).IsEqualTo(0);
        steps.Release(2, Release);
        await Assert.That(() => steps.Release(3, Release)).Throws<InvalidOperationException>();
        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(steps.Completed).IsEqualTo(2);
    }

    [Test]
    public async Task ReentrantRetirementDoesNotAdvanceTheCurrentStep()
    {
        SilkResourceReleaseSteps steps = default;
        bool observedActiveRelease = false;
        Action<int> reenter = _ =>
        {
            observedActiveRelease = steps.IsReleasing;
            steps.Release(1, static _ => { });
        };
        await Assert.That(() => steps.Release(1, reenter)).Throws<InvalidOperationException>();
        await Assert.That(observedActiveRelease).IsTrue();
        await Assert.That(steps.IsReleasing).IsFalse();
        await Assert.That(steps.Completed).IsEqualTo(0);
        int calls = 0;
        steps.Release(1, _ => calls++);
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(steps.Completed).IsEqualTo(1);
        await Assert.That(steps.IsReleasing).IsFalse();
    }
}
