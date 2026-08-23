// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Extraction;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Extraction;

public sealed class UsdPhysicsExtractionComposerTests
{
    private static UsdPhysicsExtractionPageFixture CreateFixture()
    {
        var fixture = new UsdPhysicsExtractionPageFixture
        {
            MetersPerUnit = 1.0,
            KilogramsPerUnit = 1.0,
            TimeCodesPerSecond = 60.0,
            EndTimeCode = 24.0,
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
        return fixture;
    }

    private static int AddMaterial(UsdPhysicsExtractionPageFixture fixture, int propertyStart)
    {
        int material = fixture.AddObject(
            2,
            "/Material",
            "Material",
            UsdPhysicsExtractionObjectKind.Material,
            UsdPhysicsExtractionDomains.Material,
            UsdPhysicsExtractionObjectTraits.Enabled);
        fixture.AddProperty(
            UsdPhysicsExtractionKey.MaterialStaticFriction,
            "physics:staticFriction",
            UsdPhysicsExtractionValueKind.Real,
            UsdPhysicsExtractionSource.Standard,
            0.6);
        fixture.AddProperty(
            UsdPhysicsExtractionKey.MaterialDynamicFriction,
            "physics:dynamicFriction",
            UsdPhysicsExtractionValueKind.Real,
            UsdPhysicsExtractionSource.Standard,
            0.5);
        fixture.AddProperty(
            UsdPhysicsExtractionKey.MaterialRestitution,
            "physics:restitution",
            UsdPhysicsExtractionValueKind.Real,
            UsdPhysicsExtractionSource.Standard,
            0.25);
        fixture.SetObjectRange(material, propertyStart, 3, 0, 0);
        return material;
    }

    [Test]
    public async Task ASupportedPageComposesIntoAValidBuildPage()
    {
        UsdPhysicsExtractionPageFixture fixture = CreateFixture();
        AddMaterial(fixture, 0);

        int body = fixture.AddObject(
            3,
            "/Body",
            "Body",
            UsdPhysicsExtractionObjectKind.RigidBody,
            UsdPhysicsExtractionDomains.RigidBody,
            UsdPhysicsExtractionObjectTraits.Enabled | UsdPhysicsExtractionObjectTraits.Dynamic);
        fixture.SetObjectLinks(body, 0, -1);
        fixture.SetObjectTransform(body, (0, 4, 0), (1, 0, 0, 0), (0, 0, 0));
        fixture.AddProperty(
            UsdPhysicsExtractionKey.MassMass,
            "physics:mass",
            UsdPhysicsExtractionValueKind.Real,
            UsdPhysicsExtractionSource.Standard,
            8.0);
        fixture.SetObjectRange(body, 3, 1, 0, 0);

        int collider = fixture.AddObject(
            4,
            "/Body/Shape",
            "Shape",
            UsdPhysicsExtractionObjectKind.Collider,
            UsdPhysicsExtractionDomains.Collision,
            UsdPhysicsExtractionObjectTraits.Enabled,
            UsdPhysicsExtractionGeometryKind.Sphere);
        fixture.SetObjectLinks(collider, 0, body);
        fixture.SetObjectTransform(collider, (0, 4, 0), (1, 0, 0, 0), (0.5, 0, 0));

        UsdPhysicsExtractionPage page = fixture.BuildPage();
        using var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report = UsdPhysicsExtractionComposer.Compose(page, builder);

        await Assert.That(report.Scenes).IsEqualTo(1);
        await Assert.That(report.Materials).IsEqualTo(1);
        await Assert.That(report.Shapes).IsEqualTo(1);
        await Assert.That(report.Actors).IsEqualTo(1);
        await Assert.That(report.Skipped.Length).IsEqualTo(0);

        using PhysxBuildPage built = builder.Build();
        PhysxPageValidationResult validation = PhysxPageValidator.Validate(built.Bytes);
        await Assert.That(validation.IsValid).IsTrue().Because(validation.Message ?? "no message");
    }

    [Test]
    public async Task DisabledObjectsAndBodiesWithoutShapesAreSkippedInOrder()
    {
        UsdPhysicsExtractionPageFixture fixture = CreateFixture();

        int lonely = fixture.AddObject(
            10,
            "/Lonely",
            "Lonely",
            UsdPhysicsExtractionObjectKind.RigidBody,
            UsdPhysicsExtractionDomains.RigidBody,
            UsdPhysicsExtractionObjectTraits.Enabled | UsdPhysicsExtractionObjectTraits.Dynamic);
        fixture.SetObjectLinks(lonely, 0, -1);

        int disabled = fixture.AddObject(
            11,
            "/Disabled",
            "Disabled",
            UsdPhysicsExtractionObjectKind.Collider,
            UsdPhysicsExtractionDomains.Collision,
            UsdPhysicsExtractionObjectTraits.DisabledByDiagnostic,
            UsdPhysicsExtractionGeometryKind.Sphere);
        fixture.SetObjectLinks(disabled, 0, -1);
        fixture.SetObjectTransform(disabled, (0, 0, 0), (1, 0, 0, 0), (1, 0, 0));

        int unsupported = fixture.AddObject(
            12,
            "/Unsupported",
            "Unsupported",
            UsdPhysicsExtractionObjectKind.Collider,
            UsdPhysicsExtractionDomains.Collision,
            UsdPhysicsExtractionObjectTraits.Enabled,
            UsdPhysicsExtractionGeometryKind.TetMesh);
        fixture.SetObjectLinks(unsupported, 0, -1);

        UsdPhysicsExtractionPage page = fixture.BuildPage();
        using var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report = UsdPhysicsExtractionComposer.Compose(page, builder);

        await Assert.That(report.Actors).IsEqualTo(0);
        await Assert.That(report.Shapes).IsEqualTo(0);
        await Assert.That(report.Skipped.Length).IsEqualTo(2);
        await Assert.That(report.Skipped[0]).Contains("/Unsupported");
        await Assert.That(report.Skipped[1]).Contains("/Lonely");

        // The same page always produces the same notes so checkpoints stay comparable.
        using var second = new PhysxPageBuilder();
        UsdPhysicsCompositionReport repeated = UsdPhysicsExtractionComposer.Compose(page, second);
        await Assert.That(repeated.Skipped.ToArray()).IsEquivalentTo(report.Skipped.ToArray());
    }

    [Test]
    public async Task StageMetadataMovesOntoTheBuilderInSimulationUnits()
    {
        UsdPhysicsExtractionPageFixture fixture = CreateFixture();
        fixture.MetersPerUnit = 0.01;
        fixture.KilogramsPerUnit = 0.001;
        fixture.TimeCodesPerSecond = 48.0;
        fixture.StartTimeCode = 2.0;
        fixture.EndTimeCode = 12.0;
        fixture.UpAxis = 1;
        fixture.FingerprintLow = 4242;
        fixture.FingerprintHigh = 0;

        UsdPhysicsExtractionPage page = fixture.BuildPage();
        using var builder = new PhysxPageBuilder();
        UsdPhysicsExtractionComposer.Compose(page, builder);

        await Assert.That(builder.MetersPerUnit).IsEqualTo(1.0).Within(1e-12);
        await Assert.That(builder.KilogramsPerUnit).IsEqualTo(1.0).Within(1e-12);
        await Assert.That(builder.TimeCodesPerSecond).IsEqualTo(48.0).Within(1e-12);
        await Assert.That(builder.StartTimeCode).IsEqualTo(2.0).Within(1e-12);
        await Assert.That(builder.EndTimeCode).IsEqualTo(12.0).Within(1e-12);
        await Assert.That(builder.UpAxis).IsEqualTo(PhysxUpAxis.Y);
        await Assert.That(builder.SourceHash).IsEqualTo(4242UL);
    }

    [Test]
    public async Task AuthoredButUnusableMaterialsAreReportedOnce()
    {
        UsdPhysicsExtractionPageFixture fixture = CreateFixture();
        int material = fixture.AddObject(
            2,
            "/Material",
            "Material",
            UsdPhysicsExtractionObjectKind.Material,
            UsdPhysicsExtractionDomains.Material,
            UsdPhysicsExtractionObjectTraits.DisabledByDiagnostic);
        fixture.SetObjectLinks(material, 0, -1);

        UsdPhysicsExtractionPage page = fixture.BuildPage();
        using var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report = UsdPhysicsExtractionComposer.Compose(page, builder);

        await Assert.That(report.Materials).IsEqualTo(0);
        await Assert.That(report.Skipped.Length).IsEqualTo(1);
        await Assert.That(report.Skipped[0]).Contains("material");
    }

    [Test]
    public async Task NullArgumentsAreRejected()
    {
        using var builder = new PhysxPageBuilder();
        UsdPhysicsExtractionPage page = CreateFixture().BuildPage();

        await Assert.That(() => UsdPhysicsExtractionComposer.Compose(null!, builder))
            .Throws<ArgumentNullException>();
        await Assert.That(() => UsdPhysicsExtractionComposer.Compose(page, null!))
            .Throws<ArgumentNullException>();
    }
}
