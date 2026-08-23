// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Extraction;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Extraction;

/// <summary>
/// Locks what the authored limit properties of a joint mean once they reach the build page.
/// </summary>
/// <remarks>
/// USD leaves an unauthored bound at an infinity and locks an axis by authoring a lower bound that
/// is not below the upper bound, so a composer that reads a missing bound as a zero silently welds
/// free joints shut. These tests author each shape of that authoring and check the range, the
/// limit flag, and the ordered note that a range the page cannot carry produces.
/// </remarks>
public sealed class UsdPhysicsExtractionJointLimitTests
{
    private const double DegreesToRadians = Math.PI / 180.0;

    private const double Tolerance = 1e-5;

    /// <summary>One authored joint property with the name the extractor would have produced.</summary>
    private readonly record struct Authored(
        UsdPhysicsExtractionKey Key, string Name, double Value);

    /// <summary>The parts of a composed joint the limit rules decide.</summary>
    private readonly record struct ComposedLimits(
        float Lower,
        float Upper,
        float ConeAngle0,
        float ConeAngle1,
        float MinDistance,
        float MaxDistance,
        bool IsLimited);

    private static (ComposedLimits Limits, UsdPhysicsCompositionReport Report) ComposeJoint(
        string typeName, params Authored[] properties)
    {
        var fixture = new UsdPhysicsExtractionPageFixture
        {
            MetersPerUnit = 1.0,
            KilogramsPerUnit = 1.0,
            TimeCodesPerSecond = 24.0,
            EndTimeCode = 1.0,
            DefaultSceneIndex = 0,
        };

        fixture.AddObject(
            1,
            "/Scene",
            "Scene",
            UsdPhysicsExtractionObjectKind.Scene,
            UsdPhysicsExtractionDomains.Scene,
            UsdPhysicsExtractionObjectTraits.Enabled |
                UsdPhysicsExtractionObjectTraits.DefaultScene);

        int first = AddBody(fixture, 2, "/A", (0.0, 0.0, 0.0));
        int second = AddBody(fixture, 4, "/B", (0.0, 2.0, 0.0));

        int joint = fixture.AddObject(
            6,
            "/Joint",
            "Joint",
            UsdPhysicsExtractionObjectKind.Joint,
            UsdPhysicsExtractionDomains.Joint,
            UsdPhysicsExtractionObjectTraits.Enabled,
            UsdPhysicsExtractionGeometryKind.None,
            typeName);
        fixture.SetObjectLinks(joint, 0, -1);
        foreach (Authored property in properties)
        {
            fixture.AddProperty(
                property.Key,
                property.Name,
                UsdPhysicsExtractionValueKind.Real,
                UsdPhysicsExtractionSource.Standard,
                property.Value);
        }

        fixture.AddTarget(2, "/A", first);
        fixture.AddTarget(4, "/B", second);
        fixture.AddRelationship(UsdPhysicsExtractionKey.Body0Targets, "physics:body0", 0, 1);
        fixture.AddRelationship(UsdPhysicsExtractionKey.Body1Targets, "physics:body1", 1, 1);
        fixture.SetObjectRange(joint, 0, properties.Length, 0, 2);

        UsdPhysicsExtractionPage page = fixture.BuildPage();
        using var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report = UsdPhysicsExtractionComposer.Compose(page, builder);
        using PhysxBuildPage built = builder.Build();
        return (ReadLimits(built), report);
    }

    private static int AddBody(
        UsdPhysicsExtractionPageFixture fixture,
        ulong id,
        string path,
        (double X, double Y, double Z) position)
    {
        int body = fixture.AddObject(
            id,
            path,
            path[1..],
            UsdPhysicsExtractionObjectKind.RigidBody,
            UsdPhysicsExtractionDomains.RigidBody,
            UsdPhysicsExtractionObjectTraits.Enabled | UsdPhysicsExtractionObjectTraits.Dynamic);
        fixture.SetObjectLinks(body, 0, -1);
        fixture.SetObjectTransform(body, position, (1, 0, 0, 0), (0, 0, 0));

        int collider = fixture.AddObject(
            id + 1,
            path + "/Shape",
            "Shape",
            UsdPhysicsExtractionObjectKind.Collider,
            UsdPhysicsExtractionDomains.Collision,
            UsdPhysicsExtractionObjectTraits.Enabled,
            UsdPhysicsExtractionGeometryKind.Sphere);
        fixture.SetObjectLinks(collider, 0, body);
        fixture.SetObjectTransform(collider, position, (1, 0, 0, 0), (0.5, 0, 0));
        return body;
    }

    private static ComposedLimits ReadLimits(PhysxBuildPage built)
    {
        PhysxPageReader reader = built.CreateReader();
        PhysxJointDesc joint = reader.Joints[0];
        return new ComposedLimits(
            joint.LowerLimit,
            joint.UpperLimit,
            joint.ConeAngle0,
            joint.ConeAngle1,
            joint.MinDistance,
            joint.MaxDistance,
            (joint.Flags & (uint)PhysxJointFlags.LimitEnabled) != 0u);
    }

    private static Authored Low(double value) =>
        new(UsdPhysicsExtractionKey.JointLowerLimit, "physics:lowerLimit", value);

    private static Authored High(double value) =>
        new(UsdPhysicsExtractionKey.JointUpperLimit, "physics:upperLimit", value);

    private static Authored InstancedLow(double value) =>
        new(UsdPhysicsExtractionKey.LimitLow, "limit:angular:physics:low", value);

    private static Authored InstancedHigh(double value) =>
        new(UsdPhysicsExtractionKey.LimitHigh, "limit:angular:physics:high", value);

    [Test]
    public async Task AJointWithoutAnyAuthoredBoundStaysFree()
    {
        (ComposedLimits limits, UsdPhysicsCompositionReport report) =
            ComposeJoint("PhysicsRevoluteJoint");

        await Assert.That(limits.IsLimited).IsFalse();
        await Assert.That(limits.Lower).IsEqualTo(0.0F);
        await Assert.That(limits.Upper).IsEqualTo(0.0F);
        await Assert.That(report.Skipped.Length).IsEqualTo(0);
    }

    [Test]
    public async Task AnOrderedRangeIsTheOnlyRangeThatEnablesTheLimit()
    {
        (ComposedLimits limits, UsdPhysicsCompositionReport report) =
            ComposeJoint("PhysicsRevoluteJoint", Low(-30.0), High(60.0));

        await Assert.That(limits.IsLimited).IsTrue();
        await Assert.That((double)limits.Lower)
            .IsEqualTo(-30.0 * DegreesToRadians).Within(Tolerance);
        await Assert.That((double)limits.Upper)
            .IsEqualTo(60.0 * DegreesToRadians).Within(Tolerance);
        await Assert.That(report.Skipped.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ALowerBoundOnItsOwnLeavesTheAxisFreeAndIsReported()
    {
        (ComposedLimits limits, UsdPhysicsCompositionReport report) =
            ComposeJoint("PhysicsRevoluteJoint", Low(-30.0));

        await Assert.That(limits.IsLimited).IsFalse();
        await Assert.That(limits.Lower).IsEqualTo(0.0F);
        await Assert.That(limits.Upper).IsEqualTo(0.0F);
        await Assert.That(report.Skipped.Length).IsEqualTo(1);
        await Assert.That(report.Skipped[0]).Contains("/Joint");
        await Assert.That(report.Skipped[0]).Contains("one side");
    }

    [Test]
    public async Task AnUpperBoundOnItsOwnLeavesTheAxisFreeAndIsReported()
    {
        (ComposedLimits limits, UsdPhysicsCompositionReport report) =
            ComposeJoint("PhysicsPrismaticJoint", High(0.5));

        await Assert.That(limits.IsLimited).IsFalse();
        await Assert.That(limits.Upper).IsEqualTo(0.0F);
        await Assert.That(report.Skipped.Length).IsEqualTo(1);
        await Assert.That(report.Skipped[0]).Contains("one side");
    }

    [Test]
    public async Task AnAxisLockedThroughItsRangeIsReportedRatherThanEnforced()
    {
        (ComposedLimits locked, UsdPhysicsCompositionReport lockedReport) =
            ComposeJoint("PhysicsRevoluteJoint", Low(45.0), High(-45.0));
        (ComposedLimits pinned, UsdPhysicsCompositionReport pinnedReport) =
            ComposeJoint("PhysicsRevoluteJoint", Low(0.0), High(0.0));

        await Assert.That(locked.IsLimited).IsFalse();
        await Assert.That(lockedReport.Skipped.Length).IsEqualTo(1);
        await Assert.That(lockedReport.Skipped[0]).Contains("locks an axis");
        await Assert.That(pinned.IsLimited).IsFalse();
        await Assert.That(pinnedReport.Skipped.Length).IsEqualTo(1);
        await Assert.That(pinnedReport.Skipped[0]).Contains("locks an axis");
    }

    [Test]
    public async Task TheMultipleApplyLimitInstanceFollowsTheSameRules()
    {
        (ComposedLimits ranged, UsdPhysicsCompositionReport rangedReport) = ComposeJoint(
            "PhysicsRevoluteJoint", InstancedLow(-15.0), InstancedHigh(15.0));
        (ComposedLimits half, UsdPhysicsCompositionReport halfReport) = ComposeJoint(
            "PhysicsRevoluteJoint", InstancedLow(-15.0));
        (ComposedLimits none, UsdPhysicsCompositionReport noneReport) = ComposeJoint(
            "PhysicsRevoluteJoint",
            new Authored(
                UsdPhysicsExtractionKey.DriveStiffness, "drive:angular:physics:stiffness", 10.0));

        await Assert.That(ranged.IsLimited).IsTrue();
        await Assert.That((double)ranged.Lower)
            .IsEqualTo(-15.0 * DegreesToRadians).Within(Tolerance);
        await Assert.That(rangedReport.Skipped.Length).IsEqualTo(0);

        await Assert.That(half.IsLimited).IsFalse();
        await Assert.That(halfReport.Skipped.Length).IsEqualTo(1);
        await Assert.That(halfReport.Skipped[0]).Contains("one side");

        // A driven joint that states no range at all keeps its drive and stays free.
        await Assert.That(none.IsLimited).IsFalse();
        await Assert.That(noneReport.Skipped.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ASphericalJointWithoutConeAnglesKeepsAFreeSwing()
    {
        (ComposedLimits unauthored, UsdPhysicsCompositionReport unauthoredReport) =
            ComposeJoint("PhysicsSphericalJoint");
        (ComposedLimits negative, UsdPhysicsCompositionReport negativeReport) = ComposeJoint(
            "PhysicsSphericalJoint",
            new Authored(
                UsdPhysicsExtractionKey.JointConeAngle0, "physics:coneAngle0Limit", -1.0),
            new Authored(
                UsdPhysicsExtractionKey.JointConeAngle1, "physics:coneAngle1Limit", -1.0));

        await Assert.That(unauthored.IsLimited).IsFalse();
        await Assert.That(unauthored.ConeAngle0).IsEqualTo(0.0F);
        await Assert.That(unauthoredReport.Skipped.Length).IsEqualTo(0);
        await Assert.That(negative.IsLimited).IsFalse();
        await Assert.That(negativeReport.Skipped.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ASphericalJointCarriesAuthoredConeAnglesAndWidensTheUnlimitedSide()
    {
        (ComposedLimits limits, UsdPhysicsCompositionReport report) = ComposeJoint(
            "PhysicsSphericalJoint",
            new Authored(
                UsdPhysicsExtractionKey.JointConeAngle0, "physics:coneAngle0Limit", 20.0));

        await Assert.That(limits.IsLimited).IsTrue();
        await Assert.That((double)limits.ConeAngle0)
            .IsEqualTo(20.0 * DegreesToRadians).Within(Tolerance);
        await Assert.That((double)limits.ConeAngle1).IsGreaterThan(Math.PI - 0.01);
        await Assert.That((double)limits.ConeAngle1).IsLessThan(Math.PI);
        await Assert.That(report.Skipped.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ASphericalJointThatClosesItsConeIsReportedRatherThanEnforced()
    {
        (ComposedLimits limits, UsdPhysicsCompositionReport report) = ComposeJoint(
            "PhysicsSphericalJoint",
            new Authored(UsdPhysicsExtractionKey.JointConeAngle0, "physics:coneAngle0Limit", 0.0),
            new Authored(
                UsdPhysicsExtractionKey.JointConeAngle1, "physics:coneAngle1Limit", 30.0));

        await Assert.That(limits.IsLimited).IsFalse();
        await Assert.That(limits.ConeAngle0).IsEqualTo(0.0F);
        await Assert.That(report.Skipped.Length).IsEqualTo(1);
        await Assert.That(report.Skipped[0]).Contains("swing cone");
    }

    [Test]
    public async Task ADistanceJointWithoutBoundsKeepsItsFullReach()
    {
        (ComposedLimits limits, UsdPhysicsCompositionReport report) =
            ComposeJoint("PhysicsDistanceJoint");

        await Assert.That(limits.IsLimited).IsFalse();
        await Assert.That(limits.MinDistance).IsEqualTo(0.0F);

        // The solver switches the maximum off once it reaches the largest float, so an
        // unauthored reach must arrive as that value and not as a zero that would pin the joint.
        await Assert.That(limits.MaxDistance).IsEqualTo(float.MaxValue);
        await Assert.That(report.Skipped.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ADistanceJointOnlyBoundsTheSideItAuthors()
    {
        (ComposedLimits limits, UsdPhysicsCompositionReport report) = ComposeJoint(
            "PhysicsDistanceJoint",
            new Authored(UsdPhysicsExtractionKey.JointMaxDistance, "physics:maxDistance", 2.0));

        await Assert.That(limits.IsLimited).IsTrue();
        await Assert.That(limits.MinDistance).IsEqualTo(0.0F);
        await Assert.That((double)limits.MaxDistance).IsEqualTo(2.0).Within(Tolerance);
        await Assert.That(report.Skipped.Length).IsEqualTo(0);

        (ComposedLimits reach, _) = ComposeJoint(
            "PhysicsDistanceJoint",
            new Authored(UsdPhysicsExtractionKey.JointMinDistance, "physics:minDistance", 1.0));

        await Assert.That(reach.IsLimited).IsTrue();
        await Assert.That((double)reach.MinDistance).IsEqualTo(1.0).Within(Tolerance);
        await Assert.That(reach.MaxDistance).IsEqualTo(float.MaxValue);
    }

    [Test]
    public async Task AFixedJointNeverCarriesALimit()
    {
        (ComposedLimits limits, UsdPhysicsCompositionReport report) =
            ComposeJoint("PhysicsFixedJoint", Low(-30.0), High(30.0));

        await Assert.That(limits.IsLimited).IsFalse();
        await Assert.That(limits.Lower).IsEqualTo(0.0F);
        await Assert.That(report.Skipped.Length).IsEqualTo(0);
    }
}
