// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Tests;

public sealed class UsdPhysicsTimelineTests
{
    [Test]
    public async Task AuthoredRateResolvesUnclampedFixedStep()
    {
        var timeline = new UsdPhysicsTimeline(60, 0, 120);
        UsdPhysicsFixedStep step = UsdPhysicsFixedStep.Resolve(timeline, null);

        await Assert.That(step.FrequencyHz).IsEqualTo(60.0);
        await Assert.That(step.Seconds).IsEqualTo(1.0 / 60);
        await Assert.That(step.WasClamped).IsFalse();
        await Assert.That(step.CreateClampDiagnostic()).IsNull();
        await Assert.That(timeline.DurationSeconds).IsEqualTo(2.0);
        await Assert.That(timeline.HasAuthoredRange).IsTrue();
    }

    [Test]
    public async Task SlowAuthoredRateIsClampedUpAndDiagnosed()
    {
        var timeline = new UsdPhysicsTimeline(1, 0, 10);
        UsdPhysicsFixedStep step = UsdPhysicsFixedStep.Resolve(timeline, null);

        await Assert.That(step.RequestedFrequencyHz).IsEqualTo(1.0);
        await Assert.That(step.FrequencyHz).IsEqualTo(UsdPhysicsFixedStep.MinimumFrequencyHz);
        await Assert.That(step.WasClamped).IsTrue();

        UsdPhysicsDiagnostic? diagnostic = step.CreateClampDiagnostic();
        await Assert.That(diagnostic).IsNotNull();
        await Assert.That(diagnostic!.Code).IsEqualTo(UsdPhysicsFixedStep.ClampedDiagnosticCode);
        await Assert.That(diagnostic.Severity).IsEqualTo(UsdPhysicsDiagnosticSeverity.Warning);
    }

    [Test]
    public async Task FastOverrideIsClampedDownAndDiagnosed()
    {
        var timeline = new UsdPhysicsTimeline(24, 0, 24);
        UsdPhysicsFixedStep step = UsdPhysicsFixedStep.Resolve(timeline, 1000);

        await Assert.That(step.FrequencyHz).IsEqualTo(UsdPhysicsFixedStep.MaximumFrequencyHz);
        await Assert.That(step.WasClamped).IsTrue();
        await Assert.That(step.CreateClampDiagnostic()).IsNotNull();
    }

    [Test]
    public async Task OverrideReplacesTheAuthoredRate()
    {
        var timeline = new UsdPhysicsTimeline(24, 0, 24);
        UsdPhysicsFixedStep step = UsdPhysicsFixedStep.Resolve(timeline, 120);

        await Assert.That(step.FrequencyHz).IsEqualTo(120.0);
        await Assert.That(step.WasClamped).IsFalse();
    }

    [Test]
    public async Task TimeCodeConversionsRoundTrip()
    {
        var timeline = new UsdPhysicsTimeline(48, 10, 58);

        await Assert.That(timeline.ToTimeCode(0)).IsEqualTo(10.0);
        await Assert.That(timeline.ToTimeCode(1)).IsEqualTo(58.0);
        await Assert.That(timeline.ToSeconds(58)).IsEqualTo(1.0);
        await Assert.That(timeline.ToSeconds(timeline.ToTimeCode(0.25))).IsEqualTo(0.25);
    }

    [Test]
    public async Task EmptyAuthoredRangeHasNoDurationAndNeverLoops()
    {
        var timeline = new UsdPhysicsTimeline(24, 5, 5);

        await Assert.That(timeline.HasAuthoredRange).IsFalse();
        await Assert.That(timeline.DurationSeconds).IsEqualTo(0.0);
    }

    [Test]
    public async Task InvalidAuthoredValuesAreRejectedAndFallBack()
    {
        await Assert.That(() => new UsdPhysicsTimeline(0, 0, 1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new UsdPhysicsTimeline(24, 5, 1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(UsdPhysicsTimeline.TryCreate(double.NaN, 0, 1, out _)).IsFalse();
        await Assert.That(UsdPhysicsTimeline.TryCreate(24, 0, 1, out UsdPhysicsTimeline created)).IsTrue();
        await Assert.That(created.TimeCodesPerSecond).IsEqualTo(24.0);
    }

    [Test]
    public async Task UnusableOverrideFallsBackToTheDefaultRate()
    {
        UsdPhysicsFixedStep step = UsdPhysicsFixedStep.Resolve(UsdPhysicsTimeline.Default, null);

        await Assert.That(step.FrequencyHz).IsEqualTo(24.0);
        await Assert.That(step.WasClamped).IsFalse();
    }
}
