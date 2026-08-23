// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Extraction;

namespace OpenUsd.Physics.Tests.Extraction;

public sealed class UsdPhysicsExtractionPageReaderTests
{
    private static UsdPhysicsExtractionPage BuildPage()
    {
        var fixture = new UsdPhysicsExtractionPageFixture
        {
            MetersPerUnit = 0.5,
            KilogramsPerUnit = 2.0,
            TimeCodesPerSecond = 48.0,
            StartTimeCode = 3.0,
            EndTimeCode = 9.0,
            TimeCode = 4.0,
            UpAxis = 2,
            Flags = (uint)(UsdPhysicsExtractionPageTraits.UpAxisConverted |
                UsdPhysicsExtractionPageTraits.HasForeignOpinions),
            DefaultSceneIndex = 0,
            FingerprintLow = 11,
            FingerprintHigh = 22,
        };

        fixture.AddObject(
            101,
            "/Scene",
            "Scene",
            UsdPhysicsExtractionObjectKind.Scene,
            UsdPhysicsExtractionDomains.Scene,
            UsdPhysicsExtractionObjectTraits.Enabled |
                UsdPhysicsExtractionObjectTraits.DefaultScene);
        int body = fixture.AddObject(
            202,
            "/Body",
            "Body",
            UsdPhysicsExtractionObjectKind.RigidBody,
            UsdPhysicsExtractionDomains.RigidBody,
            UsdPhysicsExtractionObjectTraits.Enabled | UsdPhysicsExtractionObjectTraits.Dynamic);

        fixture.AddNumber(4.0);
        fixture.AddNumber(5.0);
        fixture.AddNumber(6.0);
        fixture.AddText("convexHull");
        fixture.AddProperty(
            UsdPhysicsExtractionKey.MassMass,
            "openUsdPhysics:body:mass",
            UsdPhysicsExtractionValueKind.Real,
            UsdPhysicsExtractionSource.Project,
            17.5,
            traits: UsdPhysicsExtractionPropertyTraits.ShadowsWeaker);
        fixture.AddProperty(
            UsdPhysicsExtractionKey.BodyVelocity,
            "physics:velocity",
            UsdPhysicsExtractionValueKind.Vector3,
            UsdPhysicsExtractionSource.Standard,
            0.0,
            valueStart: 0,
            valueCount: 3);
        fixture.AddProperty(
            UsdPhysicsExtractionKey.CollisionApproximation,
            "physics:approximation",
            UsdPhysicsExtractionValueKind.Text,
            UsdPhysicsExtractionSource.Standard,
            0.0,
            valueStart: 0,
            valueCount: 1);
        fixture.AddTarget(101, "/Scene", 0);
        fixture.AddRelationship(
            UsdPhysicsExtractionKey.SimulationOwnerTargets, "physics:simulationOwner", 0, 1);
        fixture.SetObjectRange(body, 0, 3, 0, 1);
        fixture.SetObjectTransform(body, (7, 8, 9), (0, 1, 0, 0), (1, 2, 3));
        fixture.AddPoint(1f, 2f, 3f);
        fixture.AddIndex(0);
        fixture.SetObjectGeometry(body, 0, 1, 0, 1);
        fixture.AddDiagnostic(
            UsdPhysicsExtractionSeverity.Warning,
            UsdPhysicsExtractionCategory.Precedence,
            UsdPhysicsExtractionCode.ForeignOpinionAmbiguous,
            1,
            "two foreign opinions matched",
            202,
            UsdPhysicsExtractionKey.MassMass);

        return fixture.BuildPage();
    }

    [Test]
    public async Task HeaderValuesRoundTrip()
    {
        UsdPhysicsExtractionPage page = BuildPage();

        await Assert.That(page.MetersPerUnit).IsEqualTo(0.5).Within(1e-12);
        await Assert.That(page.KilogramsPerUnit).IsEqualTo(2.0).Within(1e-12);
        await Assert.That(page.TimeCodesPerSecond).IsEqualTo(48.0).Within(1e-12);
        await Assert.That(page.StartTimeCode).IsEqualTo(3.0).Within(1e-12);
        await Assert.That(page.EndTimeCode).IsEqualTo(9.0).Within(1e-12);
        await Assert.That(page.TimeCode).IsEqualTo(4.0).Within(1e-12);
        await Assert.That(page.UpAxis).IsEqualTo(UsdPhysicsExtractionUpAxis.Z);
        await Assert.That(page.FingerprintLow).IsEqualTo(11UL);
        await Assert.That(page.FingerprintHigh).IsEqualTo(22UL);
        await Assert.That(page.TruncationFlags).IsEqualTo(0u);
        await Assert.That(page.Flags.HasFlag(UsdPhysicsExtractionPageTraits.HasForeignOpinions))
            .IsTrue();
    }

    [Test]
    public async Task ObjectRecordsRoundTrip()
    {
        UsdPhysicsExtractionPage page = BuildPage();
        UsdPhysicsExtractionObject scene = page.GetObject(0);
        UsdPhysicsExtractionObject body = page.GetObject(1);

        await Assert.That(scene.Id).IsEqualTo(101UL);
        await Assert.That(scene.Path).IsEqualTo("/Scene");
        await Assert.That(scene.Name).IsEqualTo("Scene");
        await Assert.That(scene.TypeName).IsEqualTo(string.Empty);
        await Assert.That(scene.Kind).IsEqualTo(UsdPhysicsExtractionObjectKind.Scene);
        await Assert.That(scene.IsEnabled).IsTrue();
        await Assert.That(scene.SceneIndex).IsEqualTo(-1);
        await Assert.That(scene.ParentBodyIndex).IsEqualTo(-1);

        await Assert.That(body.Position.X).IsEqualTo(7.0).Within(1e-12);
        await Assert.That(body.Position.Y).IsEqualTo(8.0).Within(1e-12);
        await Assert.That(body.Position.Z).IsEqualTo(9.0).Within(1e-12);
        await Assert.That(body.Rotation.W).IsEqualTo(0.0).Within(1e-12);
        await Assert.That(body.Rotation.X).IsEqualTo(1.0).Within(1e-12);
        await Assert.That(body.Scale.X).IsEqualTo(1.0).Within(1e-12);
        await Assert.That(body.Extent.Z).IsEqualTo(3.0).Within(1e-12);
        await Assert.That(body.PropertyCount).IsEqualTo(3);
        await Assert.That(body.RelationshipCount).IsEqualTo(1);
        await Assert.That(body.PointCount).IsEqualTo(1);
        await Assert.That(body.IndexCount).IsEqualTo(1);
        await Assert.That(body.Index).IsEqualTo(1);
        await Assert.That(body == page.GetObject(1)).IsTrue();
        await Assert.That(body != scene).IsTrue();
    }

    [Test]
    public async Task PropertyRelationshipAndDiagnosticRecordsRoundTrip()
    {
        UsdPhysicsExtractionPage page = BuildPage();

        UsdPhysicsExtractionProperty mass = page.GetProperty(0);
        await Assert.That(mass.Key).IsEqualTo(UsdPhysicsExtractionKey.MassMass);
        await Assert.That(mass.Name).IsEqualTo("openUsdPhysics:body:mass");
        await Assert.That(mass.Source).IsEqualTo(UsdPhysicsExtractionSource.Project);
        await Assert.That(mass.Scalar).IsEqualTo(17.5).Within(1e-12);
        await Assert.That(mass.IsText).IsFalse();
        await Assert.That(mass.Flags.HasFlag(UsdPhysicsExtractionPropertyTraits.ShadowsWeaker))
            .IsTrue();

        UsdPhysicsExtractionProperty velocity = page.GetProperty(1);
        await Assert.That(velocity.ValueCount).IsEqualTo(3);
        await Assert.That(page.GetNumber(velocity.ValueStart)).IsEqualTo(4.0).Within(1e-12);
        await Assert.That(page.GetNumber(velocity.ValueStart + 2)).IsEqualTo(6.0).Within(1e-12);

        UsdPhysicsExtractionProperty approximation = page.GetProperty(2);
        await Assert.That(approximation.IsText).IsTrue();
        await Assert.That(page.GetText(approximation.ValueStart)).IsEqualTo("convexHull");

        UsdPhysicsExtractionRelationship owner = page.GetRelationship(0);
        await Assert.That(owner.Key).IsEqualTo(UsdPhysicsExtractionKey.SimulationOwnerTargets);
        await Assert.That(owner.TargetCount).IsEqualTo(1);

        UsdPhysicsExtractionTarget target = page.GetTarget(owner.TargetStart);
        await Assert.That(target.TargetId).IsEqualTo(101UL);
        await Assert.That(target.Path).IsEqualTo("/Scene");
        await Assert.That(target.ObjectIndex).IsEqualTo(0);

        UsdPhysicsExtractionDiagnostic diagnostic = page.GetDiagnostic(0);
        await Assert.That(diagnostic.Severity).IsEqualTo(UsdPhysicsExtractionSeverity.Warning);
        await Assert.That(diagnostic.Category).IsEqualTo(UsdPhysicsExtractionCategory.Precedence);
        await Assert.That(diagnostic.Code)
            .IsEqualTo(UsdPhysicsExtractionCode.ForeignOpinionAmbiguous);
        await Assert.That(diagnostic.ObjectIndex).IsEqualTo(1);
        await Assert.That(diagnostic.ObjectId).IsEqualTo(202UL);
        await Assert.That(diagnostic.Key).IsEqualTo(UsdPhysicsExtractionKey.MassMass);
        await Assert.That(diagnostic.Message).IsEqualTo("two foreign opinions matched");

        await Assert.That(page.GetPoint(0).Y).IsEqualTo(2f);
        await Assert.That(page.GetIndex(0)).IsEqualTo(0);
    }

    [Test]
    public async Task PageRejectsOutOfRangeAccessAndCopiesItsBytes()
    {
        UsdPhysicsExtractionPage page = BuildPage();

        await Assert.That(() => page.GetObject(-1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => page.GetObject(page.ObjectCount))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => page.GetProperty(page.PropertyCount))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => page.GetDiagnostic(page.DiagnosticCount))
            .Throws<ArgumentOutOfRangeException>();

        byte[] copy = page.ToArray();
        await Assert.That(copy.Length).IsEqualTo(page.ByteSize);
        copy[0] = 0;
        await Assert.That(page.ToArray()[0]).IsNotEqualTo((byte)0);
    }
}
