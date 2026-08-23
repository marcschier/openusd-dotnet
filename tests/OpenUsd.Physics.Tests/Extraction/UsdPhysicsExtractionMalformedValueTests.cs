// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Extraction;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Extraction;

/// <summary>
/// Locks that one malformed authored value never takes a whole stage down with it.
/// </summary>
/// <remarks>
/// The build page is validated as a whole, so an actor that forwards a value the runtime cannot
/// take makes every other object of the stage fail with it. The composer therefore reduces each
/// unusable value to what an unauthored property would have produced and reports it, so the object
/// keeps simulating and the loss stays visible and ordered.
/// </remarks>
public sealed class UsdPhysicsExtractionMalformedValueTests
{
    private const string BadPath = "/Bad";

    /// <summary>One authored property with the name the extractor would have produced.</summary>
    private readonly record struct Authored(
        UsdPhysicsExtractionKey Key,
        string Name,
        UsdPhysicsExtractionValueKind Kind,
        double Scalar,
        double[] Values,
        UsdPhysicsExtractionPropertyTraits Traits);

    /// <summary>The parts of a composed actor that an authored value decides.</summary>
    private readonly record struct ComposedActor(
        float Mass,
        PhysxVec3f LinearVelocity,
        PhysxVec3f AngularVelocity,
        PhysxVec3f CenterOfMass,
        PhysxVec3f Inertia,
        PhysxQuatf PrincipalAxes,
        float LinearDamping,
        float AngularDamping);

    [Test]
    public async Task AnInertiaThatCannotBeRepresentedIsDroppedAndReported()
    {
        (ComposedActor healthy, ComposedActor bad, UsdPhysicsCompositionReport report) = Compose(
            Vector(
                UsdPhysicsExtractionKey.MassDiagonalInertia,
                "physics:diagonalInertia",
                double.NaN,
                1.0,
                1.0,
                UsdPhysicsExtractionPropertyTraits.Invalid));

        await Assert.That(report.Actors).IsEqualTo(2);
        await Assert.That(bad.Inertia).IsEqualTo(default(PhysxVec3f));
        await Assert.That(healthy.Inertia).IsEqualTo(new PhysxVec3f(2.0F, 3.0F, 4.0F));
        await Assert.That(report.Skipped.Length).IsEqualTo(1);
        await Assert.That(report.Skipped[0]).Contains(BadPath);
        await Assert.That(report.Skipped[0]).Contains("diagonal inertia");
    }

    [Test]
    public async Task ANegativeInertiaIsDroppedAsAWholeVector()
    {
        (_, ComposedActor bad, UsdPhysicsCompositionReport report) = Compose(
            Vector(
                UsdPhysicsExtractionKey.MassDiagonalInertia,
                "physics:diagonalInertia",
                5.0,
                -1.0,
                7.0));

        await Assert.That(bad.Inertia).IsEqualTo(default(PhysxVec3f));
        await Assert.That(report.Skipped.Length).IsEqualTo(1);
        await Assert.That(report.Skipped[0]).Contains("diagonal inertia");
    }

    [Test]
    public async Task AnInertiaThatOverflowsTheBuildPageIsDropped()
    {
        (_, ComposedActor bad, UsdPhysicsCompositionReport report) = Compose(
            Vector(
                UsdPhysicsExtractionKey.MassDiagonalInertia,
                "physics:diagonalInertia",
                1e300,
                1.0,
                1.0));

        await Assert.That(bad.Inertia).IsEqualTo(default(PhysxVec3f));
        await Assert.That(report.Skipped.Length).IsEqualTo(1);
    }

    [Test]
    public async Task ACenterOfMassThatCannotBeRepresentedFallsBackToTheBodyOrigin()
    {
        (_, ComposedActor bad, UsdPhysicsCompositionReport report) = Compose(
            Vector(
                UsdPhysicsExtractionKey.MassCenterOfMass,
                "physics:centerOfMass",
                0.5,
                double.PositiveInfinity,
                0.5,
                UsdPhysicsExtractionPropertyTraits.Invalid));

        await Assert.That(bad.CenterOfMass).IsEqualTo(default(PhysxVec3f));
        await Assert.That(report.Skipped.Length).IsEqualTo(1);
        await Assert.That(report.Skipped[0]).Contains("center of mass");
    }

    [Test]
    public async Task VelocitiesThatCannotBeRepresentedStartTheBodyAtRest()
    {
        (_, ComposedActor bad, UsdPhysicsCompositionReport report) = Compose(
            Vector(
                UsdPhysicsExtractionKey.BodyVelocity,
                "physics:velocity",
                double.NaN,
                0.0,
                0.0,
                UsdPhysicsExtractionPropertyTraits.Invalid),
            Vector(
                UsdPhysicsExtractionKey.BodyAngularVelocity,
                "physics:angularVelocity",
                0.0,
                double.NaN,
                0.0,
                UsdPhysicsExtractionPropertyTraits.Invalid));

        await Assert.That(bad.LinearVelocity).IsEqualTo(default(PhysxVec3f));
        await Assert.That(bad.AngularVelocity).IsEqualTo(default(PhysxVec3f));
        await Assert.That(report.Skipped.Length).IsEqualTo(2);
        await Assert.That(report.Skipped[0]).Contains("linear velocity");
        await Assert.That(report.Skipped[1]).Contains("angular velocity");
    }

    [Test]
    public async Task AMassThatCannotBeRepresentedLetsTheDensityDecide()
    {
        (_, ComposedActor bad, UsdPhysicsCompositionReport report) = Compose(
            Scalar(UsdPhysicsExtractionKey.MassMass, "physics:mass", -4.0));

        await Assert.That(bad.Mass).IsEqualTo(0.0F);
        await Assert.That(report.Skipped.Length).IsEqualTo(1);
        await Assert.That(report.Skipped[0]).Contains("mass");
    }

    [Test]
    public async Task AnUnauthoredMassIsNotReported()
    {
        (_, ComposedActor bad, UsdPhysicsCompositionReport report) = Compose();

        await Assert.That(bad.Mass).IsEqualTo(0.0F);
        await Assert.That(report.Skipped.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ADampingThatCannotBeRepresentedFallsBackToItsDefault()
    {
        (_, ComposedActor bad, UsdPhysicsCompositionReport report) = Compose(
            Scalar(UsdPhysicsExtractionKey.BodyLinearDamping, "physics:linearDamping", -1.0),
            Scalar(
                UsdPhysicsExtractionKey.BodyAngularDamping,
                "physics:angularDamping",
                double.NaN,
                UsdPhysicsExtractionPropertyTraits.Invalid));

        await Assert.That(bad.LinearDamping).IsEqualTo(0.0F);
        await Assert.That(bad.AngularDamping).IsEqualTo(0.05F);
        await Assert.That(report.Skipped.Length).IsEqualTo(2);
        await Assert.That(report.Skipped[0]).Contains("linear damping");
        await Assert.That(report.Skipped[1]).Contains("angular damping");
    }

    [Test]
    public async Task PrincipalAxesThatCannotBeRepresentedStayTheIdentity()
    {
        (_, ComposedActor bad, UsdPhysicsCompositionReport report) = Compose(
            Quaternion(
                UsdPhysicsExtractionKey.MassPrincipalAxes,
                "physics:principalAxes",
                double.NaN,
                0.0,
                0.0,
                0.0,
                UsdPhysicsExtractionPropertyTraits.Invalid));

        await Assert.That(bad.PrincipalAxes).IsEqualTo(PhysxQuatf.Identity);
        await Assert.That(report.Skipped.Length).IsEqualTo(1);
        await Assert.That(report.Skipped[0]).Contains("principal axes");
    }

    [Test]
    public async Task AnAllZeroPrincipalAxesFrameIsReportedRatherThanTakenAsAFrame()
    {
        (_, ComposedActor bad, UsdPhysicsCompositionReport report) = Compose(
            Quaternion(
                UsdPhysicsExtractionKey.MassPrincipalAxes,
                "physics:principalAxes",
                0.0,
                0.0,
                0.0,
                0.0));

        await Assert.That(bad.PrincipalAxes).IsEqualTo(PhysxQuatf.Identity);
        await Assert.That(report.Skipped.Length).IsEqualTo(1);
        await Assert.That(report.Skipped[0]).Contains("principal axes");
    }

    [Test]
    public async Task APrincipalAxesFrameThatIsNotUnitLengthKeepsItsOrientation()
    {
        (_, ComposedActor bad, UsdPhysicsCompositionReport report) = Compose(
            Quaternion(
                UsdPhysicsExtractionKey.MassPrincipalAxes,
                "physics:principalAxes",
                0.9,
                0.0,
                0.0,
                0.9));

        await Assert.That((double)bad.PrincipalAxes.W).IsEqualTo(0.70710678).Within(1e-6);
        await Assert.That((double)bad.PrincipalAxes.Z).IsEqualTo(0.70710678).Within(1e-6);
        await Assert.That(report.Skipped.Length).IsEqualTo(0);
    }

    [Test]
    public async Task EveryReducedValueOfOneActorIsReportedInAStableOrder()
    {
        (_, ComposedActor bad, UsdPhysicsCompositionReport report) = Compose(
            Scalar(UsdPhysicsExtractionKey.MassMass, "physics:mass", double.NaN,
                UsdPhysicsExtractionPropertyTraits.Invalid),
            Vector(
                UsdPhysicsExtractionKey.BodyVelocity,
                "physics:velocity",
                double.NaN,
                0.0,
                0.0,
                UsdPhysicsExtractionPropertyTraits.Invalid),
            Vector(
                UsdPhysicsExtractionKey.BodyAngularVelocity,
                "physics:angularVelocity",
                double.NaN,
                0.0,
                0.0,
                UsdPhysicsExtractionPropertyTraits.Invalid),
            Vector(
                UsdPhysicsExtractionKey.MassCenterOfMass,
                "physics:centerOfMass",
                double.NaN,
                0.0,
                0.0,
                UsdPhysicsExtractionPropertyTraits.Invalid),
            Vector(
                UsdPhysicsExtractionKey.MassDiagonalInertia,
                "physics:diagonalInertia",
                double.NaN,
                0.0,
                0.0,
                UsdPhysicsExtractionPropertyTraits.Invalid),
            Quaternion(
                UsdPhysicsExtractionKey.MassPrincipalAxes,
                "physics:principalAxes",
                double.NaN,
                0.0,
                0.0,
                0.0,
                UsdPhysicsExtractionPropertyTraits.Invalid),
            Scalar(
                UsdPhysicsExtractionKey.BodyLinearDamping,
                "physics:linearDamping",
                double.NaN,
                UsdPhysicsExtractionPropertyTraits.Invalid),
            Scalar(
                UsdPhysicsExtractionKey.BodyAngularDamping,
                "physics:angularDamping",
                double.NaN,
                UsdPhysicsExtractionPropertyTraits.Invalid));

        await Assert.That(bad).IsEqualTo(new ComposedActor(
            0.0F,
            default,
            default,
            default,
            default,
            PhysxQuatf.Identity,
            0.0F,
            0.05F));
        await Assert.That(report.Skipped.Length).IsEqualTo(8);
        await Assert.That(report.Skipped[0]).Contains("mass");
        await Assert.That(report.Skipped[1]).Contains("linear velocity");
        await Assert.That(report.Skipped[2]).Contains("angular velocity");
        await Assert.That(report.Skipped[3]).Contains("center of mass");
        await Assert.That(report.Skipped[4]).Contains("diagonal inertia");
        await Assert.That(report.Skipped[5]).Contains("principal axes");
        await Assert.That(report.Skipped[6]).Contains("linear damping");
        await Assert.That(report.Skipped[7]).Contains("angular damping");
    }

    [Test]
    public async Task AnInvalidPropertyIsReadAsUnauthoredEverywhereElseToo()
    {
        (_, _, UsdPhysicsCompositionReport report) = Compose(
            Scalar(
                UsdPhysicsExtractionKey.BodyDisableGravity,
                "physics:disableGravity",
                1.0,
                UsdPhysicsExtractionPropertyTraits.Invalid));

        await Assert.That(report.Actors).IsEqualTo(2);
        await Assert.That(report.Skipped.Length).IsEqualTo(0);
    }

    private static Authored Scalar(
        UsdPhysicsExtractionKey key,
        string name,
        double value,
        UsdPhysicsExtractionPropertyTraits traits = UsdPhysicsExtractionPropertyTraits.None) =>
        new(key, name, UsdPhysicsExtractionValueKind.Real, value, [], traits);

    private static Authored Vector(
        UsdPhysicsExtractionKey key,
        string name,
        double x,
        double y,
        double z,
        UsdPhysicsExtractionPropertyTraits traits = UsdPhysicsExtractionPropertyTraits.None) =>
        new(key, name, UsdPhysicsExtractionValueKind.Vector3, 0.0, [x, y, z], traits);

    private static Authored Quaternion(
        UsdPhysicsExtractionKey key,
        string name,
        double w,
        double x,
        double y,
        double z,
        UsdPhysicsExtractionPropertyTraits traits = UsdPhysicsExtractionPropertyTraits.None) =>
        new(key, name, UsdPhysicsExtractionValueKind.Quaternion, 0.0, [w, x, y, z], traits);

    /// <summary>
    /// Composes one healthy actor and one actor that authors the given properties.
    /// </summary>
    private static (ComposedActor Healthy, ComposedActor Bad, UsdPhysicsCompositionReport Report)
        Compose(params Authored[] authored)
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

        int healthy = AddBody(fixture, 2, "/Good");
        int inertia = fixture.AddNumber(2.0);
        fixture.AddNumber(3.0);
        fixture.AddNumber(4.0);
        fixture.AddProperty(
            UsdPhysicsExtractionKey.MassDiagonalInertia,
            "physics:diagonalInertia",
            UsdPhysicsExtractionValueKind.Vector3,
            UsdPhysicsExtractionSource.Standard,
            0.0,
            inertia,
            3);
        fixture.SetObjectRange(healthy, 0, 1, 0, 0);

        int bad = AddBody(fixture, 4, BadPath);
        foreach (Authored property in authored)
        {
            int start = -1;
            foreach (double value in property.Values)
            {
                int index = fixture.AddNumber(value);
                start = start < 0 ? index : start;
            }

            fixture.AddProperty(
                property.Key,
                property.Name,
                property.Kind,
                UsdPhysicsExtractionSource.Standard,
                property.Scalar,
                start < 0 ? 0 : start,
                property.Values.Length,
                property.Traits);
        }

        fixture.SetObjectRange(bad, 1, authored.Length, 0, 0);

        UsdPhysicsExtractionPage page = fixture.BuildPage();
        using var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report = UsdPhysicsExtractionComposer.Compose(page, builder);

        // Building validates the page as a whole, so it only succeeds when no reduced value
        // reached it.
        using PhysxBuildPage built = builder.Build();
        return (ReadActor(built, 0), ReadActor(built, 1), report);
    }

    private static int AddBody(UsdPhysicsExtractionPageFixture fixture, ulong id, string path)
    {
        int body = fixture.AddObject(
            id,
            path,
            path[1..],
            UsdPhysicsExtractionObjectKind.RigidBody,
            UsdPhysicsExtractionDomains.RigidBody,
            UsdPhysicsExtractionObjectTraits.Enabled | UsdPhysicsExtractionObjectTraits.Dynamic);
        fixture.SetObjectLinks(body, 0, -1);
        fixture.SetObjectTransform(body, (0.0, 1.0, 0.0), (1, 0, 0, 0), (0, 0, 0));

        int collider = fixture.AddObject(
            id + 1,
            path + "/Shape",
            "Shape",
            UsdPhysicsExtractionObjectKind.Collider,
            UsdPhysicsExtractionDomains.Collision,
            UsdPhysicsExtractionObjectTraits.Enabled,
            UsdPhysicsExtractionGeometryKind.Sphere);
        fixture.SetObjectLinks(collider, 0, body);
        fixture.SetObjectTransform(collider, (0.0, 1.0, 0.0), (1, 0, 0, 0), (0.5, 0, 0));
        return body;
    }

    private static ComposedActor ReadActor(PhysxBuildPage built, int index)
    {
        PhysxPageReader reader = built.CreateReader();
        PhysxActorDesc actor = reader.Actors[index];
        return new ComposedActor(
            actor.Mass,
            actor.LinearVelocity,
            actor.AngularVelocity,
            actor.CenterOfMass,
            actor.Inertia,
            actor.PrincipalAxes,
            actor.LinearDamping,
            actor.AngularDamping);
    }
}
