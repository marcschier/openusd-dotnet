// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Extraction;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Extraction;

/// <summary>
/// Locks the unit conversion of every authored quantity that is a scalar budget rather than a
/// position, a velocity state or a mass that already had its own coverage.
/// </summary>
/// <remarks>
/// <para>
/// These are the values a missed conversion hides in. A mis-scaled position moves a body somewhere
/// visibly wrong on the first frame; a mis-scaled clamp or threshold simply never engages, and a
/// mis-scaled link mass produces a link whose mass and inertia disagree with each other. Nothing
/// fails, nothing is diagnosed, and the stage simulates plausibly but not as authored.
/// </para>
/// <para>
/// The runtime reads metres, radians and kilograms because
/// <c>native/openusd_physx/src/openusd_physx_runtime.cpp</c> creates PhysX with the default
/// <c>PxTolerancesScale</c>, and it applies each of these fields verbatim. The conversion is
/// therefore entirely the composer's responsibility.
/// </para>
/// </remarks>
public sealed class UsdPhysicsExtractionUnitConversionTests
{
    private const double CentimetreStage = 0.01;
    private const double GramStage = 0.001;

    [Test]
    public async Task BodyVelocityClampsAndSleepThresholdReachTheWorldInSimulationUnits()
    {
        UsdPhysicsExtractionPageFixture fixture = CreateFixture(CentimetreStage, 1.0);
        AddScene(fixture);
        AddSphereBody(fixture);

        // 500 stage units per second is 5 m/s on a centimetre stage, 90 degrees per second is
        // pi/2 radians per second, and a mass normalized kinetic energy scales as the square of
        // the linear unit.
        fixture.AddProperty(
            UsdPhysicsExtractionKey.BodyMaxLinearVelocity,
            "physics:maxLinearVelocity",
            UsdPhysicsExtractionValueKind.Real,
            UsdPhysicsExtractionSource.Standard,
            500.0);
        fixture.AddProperty(
            UsdPhysicsExtractionKey.BodyMaxAngularVelocity,
            "physics:maxAngularVelocity",
            UsdPhysicsExtractionValueKind.Real,
            UsdPhysicsExtractionSource.Standard,
            90.0);
        fixture.AddProperty(
            UsdPhysicsExtractionKey.BodySleepThreshold,
            "physics:sleepThreshold",
            UsdPhysicsExtractionValueKind.Real,
            UsdPhysicsExtractionSource.Standard,
            50.0);
        fixture.SetObjectRange(BodyIndex, 0, 3, 0, 0);

        PhysxActorDesc actor = ComposeActor(fixture);

        await Assert.That((double)actor.MaxLinearVelocity).IsEqualTo(5.0).Within(1e-5);
        await Assert.That((double)actor.MaxAngularVelocity)
            .IsEqualTo(Math.PI / 2.0)
            .Within(1e-5);
        await Assert.That((double)actor.SleepThreshold).IsEqualTo(0.005).Within(1e-7);
    }

    [Test]
    public async Task AnUnauthoredBudgetStillLeavesTheRuntimeDefaultInPlace()
    {
        UsdPhysicsExtractionPageFixture fixture = CreateFixture(CentimetreStage, 1.0);
        AddScene(fixture);
        AddSphereBody(fixture);

        PhysxActorDesc actor = ComposeActor(fixture);

        await Assert.That(actor.MaxLinearVelocity).IsEqualTo(0.0F);
        await Assert.That(actor.MaxAngularVelocity).IsEqualTo(0.0F);
        await Assert.That(actor.SleepThreshold).IsEqualTo(0.0F);
    }

    [Test]
    public async Task ASceneBounceThresholdReachesTheWorldInMetresPerSecond()
    {
        UsdPhysicsExtractionPageFixture fixture = CreateFixture(CentimetreStage, 1.0);
        int scene = AddScene(fixture);
        fixture.AddProperty(
            UsdPhysicsExtractionKey.SceneBounceThreshold,
            "physics:bounceThreshold",
            UsdPhysicsExtractionValueKind.Real,
            UsdPhysicsExtractionSource.Standard,
            20.0);
        fixture.SetObjectRange(scene, 0, 1, 0, 0);
        AddSphereBody(fixture);

        UsdPhysicsExtractionPage page = fixture.BuildPage();
        using var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report = UsdPhysicsExtractionComposer.Compose(page, builder);
        await Assert.That(report.Skipped.Length).IsEqualTo(0);

        using PhysxBuildPage built = builder.Build();
        PhysxPageReader reader = built.CreateReader();

        await Assert.That((double)reader.Scenes[0].BounceThreshold).IsEqualTo(0.2).Within(1e-6);
    }

    /// <summary>
    /// Proves an authored articulation link mass changes unit exactly like the inertia beside it.
    /// </summary>
    /// <remarks>
    /// This one is native backed because an articulation reaches the composer only through the
    /// extractor's link and joint topology, which a hand built page cannot reproduce faithfully
    /// enough to be evidence. The stage authors mass in grams and an inertia tensor in the same
    /// stage units, so a link that converted only one of them is internally inconsistent.
    /// </remarks>
    [Test]
    public async Task AnArticulationLinkMassChangesUnitLikeItsInertia()
    {
        CpuDomainFixture.RequireRuntime();

        using CpuDomainFixture fixture = CpuDomainFixture.Create(
            nameof(AnArticulationLinkMassChangesUnitLikeItsInertia));
        fixture.WriteStage(
            """
            #usda 1.0
            (
                upAxis = "Y"
                metersPerUnit = 1
                kilogramsPerUnit = 0.001
                timeCodesPerSecond = 24
                startTimeCode = 0
                endTimeCode = 24
            )

            def PhysicsScene "Scene"
            {
                vector3f physics:gravityDirection = (0, -1, 0)
                float physics:gravityMagnitude = 9.81
            }

            def Xform "Robot" (
                prepend apiSchemas = ["PhysicsArticulationRootAPI"]
            )
            {
                def Sphere "Root" (
                    prepend apiSchemas = [
                        "PhysicsCollisionAPI", "PhysicsRigidBodyAPI", "PhysicsMassAPI"
                    ]
                )
                {
                    double radius = 0.2
                    float physics:mass = 2500
                    float3 physics:diagonalInertia = (40, 40, 40)
                    double3 xformOp:translate = (0, 5, 0)
                    uniform token[] xformOpOrder = ["xformOp:translate"]
                }

                def PhysicsFixedJoint "RootAnchor"
                {
                    rel physics:body1 = </Robot/Root>
                }
            }
            """);

        UsdPhysicsExtractionPage page = fixture.Extract();
        using var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report = UsdPhysicsExtractionComposer.Compose(page, builder);
        await Assert.That(report.Articulations).IsEqualTo(1);

        using PhysxBuildPage built = builder.Build();
        PhysxPageReader reader = built.CreateReader();
        PhysxArticulationLinkDesc link = reader.ArticulationLinks[0];

        // 2500 grams is 2.5 kg, and the inertia beside it is already scaled the same way.
        await Assert.That((double)link.Mass).IsEqualTo(2.5).Within(1e-4);
        await Assert.That((double)link.Inertia.X).IsEqualTo(0.04).Within(1e-6);
    }

    private const int BodyIndex = 1;

    private static UsdPhysicsExtractionPageFixture CreateFixture(
        double metersPerUnit, double kilogramsPerUnit) => new()
        {
            MetersPerUnit = metersPerUnit,
            KilogramsPerUnit = kilogramsPerUnit,
            TimeCodesPerSecond = 24.0,
            EndTimeCode = 1.0,
            UpAxis = (uint)UsdPhysicsExtractionUpAxis.Y,
            DefaultSceneIndex = 0,
        };

    private static int AddScene(UsdPhysicsExtractionPageFixture fixture) => fixture.AddObject(
        1,
        "/Scene",
        "Scene",
        UsdPhysicsExtractionObjectKind.Scene,
        UsdPhysicsExtractionDomains.Scene,
        UsdPhysicsExtractionObjectTraits.Enabled | UsdPhysicsExtractionObjectTraits.DefaultScene);

    private static void AddSphereBody(UsdPhysicsExtractionPageFixture fixture)
    {
        int body = fixture.AddObject(
            4,
            "/Body",
            "Body",
            UsdPhysicsExtractionObjectKind.RigidBody,
            UsdPhysicsExtractionDomains.RigidBody,
            UsdPhysicsExtractionObjectTraits.Enabled | UsdPhysicsExtractionObjectTraits.Dynamic);
        fixture.SetObjectLinks(body, 0, -1);
        fixture.SetObjectTransform(body, (0.0, 1.0, 0.0), (1, 0, 0, 0), (0, 0, 0));

        int collider = fixture.AddObject(
            5,
            "/Body/Shape",
            "Shape",
            UsdPhysicsExtractionObjectKind.Collider,
            UsdPhysicsExtractionDomains.Collision,
            UsdPhysicsExtractionObjectTraits.Enabled,
            UsdPhysicsExtractionGeometryKind.Sphere);
        fixture.SetObjectLinks(collider, 0, body);
        fixture.SetObjectTransform(collider, (0.0, 1.0, 0.0), (1, 0, 0, 0), (0.5, 0, 0));
    }

    private static PhysxActorDesc ComposeActor(UsdPhysicsExtractionPageFixture fixture)
    {
        UsdPhysicsExtractionPage page = fixture.BuildPage();
        using var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report = UsdPhysicsExtractionComposer.Compose(page, builder);
        if (report.Actors != 1)
        {
            throw new InvalidOperationException(
                $"The fixture composed {report.Actors} actor(s): {string.Join("; ", report.Skipped)}");
        }

        using PhysxBuildPage built = builder.Build();
        PhysxPageValidationResult validation = PhysxPageValidator.Validate(built.Bytes);
        return validation.IsValid
            ? built.CreateReader().Actors[0]
            : throw new InvalidOperationException(validation.Message ?? "The page is not valid.");
    }
}
