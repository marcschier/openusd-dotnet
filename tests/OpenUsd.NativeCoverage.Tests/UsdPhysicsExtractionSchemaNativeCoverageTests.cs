// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Extraction;
using OpenUsd.Physics.Interop;

namespace OpenUsd.NativeCoverage.Tests;

[NotInParallel("PhysicsExtraction")]
public sealed class UsdPhysicsExtractionSchemaNativeCoverageTests
{
    private const string PrecedenceStage = """
        #usda 1.0
        (
            metersPerUnit = 1
            upAxis = "Y"
        )

        def PhysicsCollisionGroup "Group" (
            prepend apiSchemas = ["OpenUsdPhysicsCollisionFilterSettingsAPI"]
        )
        {
            bool physics:invertFilteredGroups = 0
            bool physxCollisionGroup:invertFilteredGroups = 0
            bool openUsdPhysics:collisionFilter:invertFilteredGroups = 1
        }

        def Xform "Body" (
            prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsMassAPI"]
        )
        {
            float physics:mass = 2
            float physxRigidBody:mass = 7
        }
        """;

    [Test]
    public async Task ProjectOpinionsWinOverForeignAndStandardOpinions()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(ProjectOpinionsWinOverForeignAndStandardOpinions), PrecedenceStage);

        UsdPhysicsExtractionObject group = PhysicsExtractionStages.Find(
            page, "/Group", UsdPhysicsExtractionObjectKind.CollisionGroup);
        UsdPhysicsExtractionProperty invert = PhysicsExtractionStages.Property(
            page, group, UsdPhysicsExtractionKey.FilterInvertGroups);

        await Assert.That(invert.Source).IsEqualTo(UsdPhysicsExtractionSource.Project);
        await Assert.That(invert.Name)
            .IsEqualTo("openUsdPhysics:collisionFilter:invertFilteredGroups");
        await Assert.That(invert.Scalar).IsEqualTo(1.0).Within(1e-12);
        await Assert.That(
                invert.Flags.HasFlag(UsdPhysicsExtractionPropertyTraits.ShadowsWeaker)).IsTrue();
        await Assert.That(group.Flags.HasFlag(UsdPhysicsExtractionObjectTraits.ProjectOpinions))
            .IsTrue();
    }

    [Test]
    public async Task ForeignOpinionsWinOverStandardOpinionsWhenNoProjectOpinionExists()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(ForeignOpinionsWinOverStandardOpinionsWhenNoProjectOpinionExists),
            PrecedenceStage);

        UsdPhysicsExtractionObject body = PhysicsExtractionStages.Find(
            page, "/Body", UsdPhysicsExtractionObjectKind.RigidBody);
        UsdPhysicsExtractionProperty mass = PhysicsExtractionStages.Property(
            page, body, UsdPhysicsExtractionKey.MassMass);

        await Assert.That(mass.Source).IsEqualTo(UsdPhysicsExtractionSource.Foreign);
        await Assert.That(mass.Name).IsEqualTo("physxRigidBody:mass");
        await Assert.That(mass.Scalar).IsEqualTo(7.0).Within(1e-6);
        await Assert.That(page.Flags.HasFlag(UsdPhysicsExtractionPageTraits.HasForeignOpinions))
            .IsTrue();
        await Assert.That(PhysicsExtractionStages.HasDiagnostic(
            page, UsdPhysicsExtractionCode.ForeignOpinionUsed)).IsTrue();
    }

    [Test]
    public async Task StandardOpinionsApplyWhenNothingStrongerIsAuthored()
    {
        const string usda = """
            #usda 1.0
            (
                metersPerUnit = 1
                upAxis = "Y"
            )

            def Xform "Body" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                float physics:mass = 5
            }
            """;

        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(StandardOpinionsApplyWhenNothingStrongerIsAuthored), usda);

        UsdPhysicsExtractionObject body = PhysicsExtractionStages.Find(
            page, "/Body", UsdPhysicsExtractionObjectKind.RigidBody);
        UsdPhysicsExtractionProperty mass = PhysicsExtractionStages.Property(
            page, body, UsdPhysicsExtractionKey.MassMass);

        await Assert.That(mass.Source).IsEqualTo(UsdPhysicsExtractionSource.Standard);
        await Assert.That(mass.Scalar).IsEqualTo(5.0).Within(1e-6);
        await Assert.That(
                mass.Flags.HasFlag(UsdPhysicsExtractionPropertyTraits.ShadowsWeaker)).IsFalse();
        await Assert.That(page.Flags.HasFlag(UsdPhysicsExtractionPageTraits.HasForeignOpinions))
            .IsFalse();
    }

    [Test]
    public async Task AmbiguousForeignOpinionsAreReportedAndResolvedDeterministically()
    {
        const string usda = """
            #usda 1.0
            (
                metersPerUnit = 1
                upAxis = "Y"
            )

            def Xform "Body" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                float physxRigidBody:mass = 7
                float physxBody:mass = 9
            }
            """;

        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(AmbiguousForeignOpinionsAreReportedAndResolvedDeterministically), usda);
        UsdPhysicsExtractionPage repeated = PhysicsExtractionStages.Extract(
            nameof(AmbiguousForeignOpinionsAreReportedAndResolvedDeterministically), usda);

        UsdPhysicsExtractionObject body = PhysicsExtractionStages.Find(
            page, "/Body", UsdPhysicsExtractionObjectKind.RigidBody);
        UsdPhysicsExtractionProperty mass = PhysicsExtractionStages.Property(
            page, body, UsdPhysicsExtractionKey.MassMass);
        UsdPhysicsExtractionObject repeatedBody = PhysicsExtractionStages.Find(
            repeated, "/Body", UsdPhysicsExtractionObjectKind.RigidBody);
        UsdPhysicsExtractionProperty repeatedMass = PhysicsExtractionStages.Property(
            repeated, repeatedBody, UsdPhysicsExtractionKey.MassMass);

        await Assert.That(mass.Source).IsEqualTo(UsdPhysicsExtractionSource.Foreign);
        await Assert.That(mass.Name).StartsWith("physx");
        await Assert.That(repeatedMass.Name).IsEqualTo(mass.Name);
        await Assert.That(
                mass.Flags.HasFlag(UsdPhysicsExtractionPropertyTraits.AmbiguousForeign)).IsTrue();
        await Assert.That(PhysicsExtractionStages.HasDiagnostic(
            page, UsdPhysicsExtractionCode.ForeignOpinionAmbiguous)).IsTrue();
    }

    [Test]
    public async Task MalformedPhysicsValuesAreReportedWithoutStoppingExtraction()
    {
        const string usda = """
            #usda 1.0
            (
                metersPerUnit = 1
                upAxis = "Y"
            )

            def PhysicsScene "Scene"
            {
                float physics:gravityMagnitude = nan
            }

            def Xform "Body" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsMassAPI"]
            )
            {
                float physics:mass = 4
            }
            """;

        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(MalformedPhysicsValuesAreReportedWithoutStoppingExtraction), usda);

        UsdPhysicsExtractionObject scene = PhysicsExtractionStages.Find(
            page, "/Scene", UsdPhysicsExtractionObjectKind.Scene);
        UsdPhysicsExtractionProperty gravity = PhysicsExtractionStages.Property(
            page, scene, UsdPhysicsExtractionKey.SceneGravityMagnitude);

        await Assert.That(gravity.Flags.HasFlag(UsdPhysicsExtractionPropertyTraits.Invalid))
            .IsTrue();
        await Assert.That(PhysicsExtractionStages.HasDiagnostic(
            page, UsdPhysicsExtractionCode.PropertyValueInvalid)).IsTrue();

        UsdPhysicsExtractionObject body = PhysicsExtractionStages.Find(
            page, "/Body", UsdPhysicsExtractionObjectKind.RigidBody);
        UsdPhysicsExtractionProperty mass = PhysicsExtractionStages.Property(
            page, body, UsdPhysicsExtractionKey.MassMass);
        await Assert.That(mass.Scalar).IsEqualTo(4.0).Within(1e-6);
    }

    [Test]
    public async Task FingerprintTracksPhysicsEditsAndIgnoresVisualEdits()
    {
        const string baseline = """
            #usda 1.0
            (
                metersPerUnit = 1
                upAxis = "Y"
            )

            def PhysicsScene "Scene"
            {
            }

            def Sphere "Body" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI"]
            )
            {
                double radius = 1
            }
            """;

        const string visualEdit = """
            #usda 1.0
            (
                metersPerUnit = 1
                upAxis = "Y"
            )

            def PhysicsScene "Scene"
            {
            }

            def Sphere "Body" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI"]
            )
            {
                double radius = 1
                color3f[] primvars:displayColor = [(1, 0, 0)]
                float[] primvars:displayOpacity = [0.25]
            }
            """;

        const string physicsEdit = """
            #usda 1.0
            (
                metersPerUnit = 1
                upAxis = "Y"
            )

            def PhysicsScene "Scene"
            {
            }

            def Sphere "Body" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI"]
            )
            {
                double radius = 1
                bool physics:kinematicEnabled = 1
            }
            """;

        UsdPhysicsExtractionPage first = PhysicsExtractionStages.Extract(
            nameof(FingerprintTracksPhysicsEditsAndIgnoresVisualEdits), baseline);
        UsdPhysicsExtractionPage visual = PhysicsExtractionStages.Extract(
            nameof(FingerprintTracksPhysicsEditsAndIgnoresVisualEdits), visualEdit);
        UsdPhysicsExtractionPage physics = PhysicsExtractionStages.Extract(
            nameof(FingerprintTracksPhysicsEditsAndIgnoresVisualEdits), physicsEdit);

        await Assert.That(visual.FingerprintLow).IsEqualTo(first.FingerprintLow);
        await Assert.That(visual.FingerprintHigh).IsEqualTo(first.FingerprintHigh);
        await Assert.That(physics.FingerprintLow).IsNotEqualTo(first.FingerprintLow);
    }

    [Test]
    public async Task FingerprintIsIndependentOfUnmappedPropertyReporting()
    {
        const string usda = """
            #usda 1.0
            (
                metersPerUnit = 1
                upAxis = "Y"
            )

            def PhysicsScene "Scene"
            {
            }

            def Xform "Body" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI"]
            )
            {
                float openUsdPhysics:body:somethingNobodyMapsYet = 3
            }
            """;

        UsdPhysicsExtractionPage without = PhysicsExtractionStages.Extract(
            nameof(FingerprintIsIndependentOfUnmappedPropertyReporting), usda);
        UsdPhysicsExtractionPage with = PhysicsExtractionStages.Extract(
            nameof(FingerprintIsIndependentOfUnmappedPropertyReporting),
            usda,
            UsdPhysicsExtractionOptions.Default with { IncludeUnmapped = true });

        await Assert.That(with.FingerprintLow).IsEqualTo(without.FingerprintLow);
        await Assert.That(with.FingerprintHigh).IsEqualTo(without.FingerprintHigh);
        await Assert.That(with.PropertyCount).IsGreaterThan(without.PropertyCount);

        UsdPhysicsExtractionObject body = PhysicsExtractionStages.Find(
            with, "/Body", UsdPhysicsExtractionObjectKind.RigidBody);
        bool sawUnmapped = false;
        for (int offset = 0; offset < body.PropertyCount; offset++)
        {
            UsdPhysicsExtractionProperty property = with.GetProperty(body.PropertyStart + offset);
            sawUnmapped |= property.Flags.HasFlag(UsdPhysicsExtractionPropertyTraits.Unmapped);
        }

        await Assert.That(sawUnmapped).IsTrue();
    }

    [Test]
    public async Task EverySchemaDomainIsExtractedAndUnsupportedDomainsAreReportedOnce()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(EverySchemaDomainIsExtractedAndUnsupportedDomainsAreReportedOnce),
            PhysicsExtractionDomainStage.Usda);

        UsdPhysicsExtractionDomains seen = UsdPhysicsExtractionDomains.None;
        var kinds = new HashSet<UsdPhysicsExtractionObjectKind>();
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            seen |= item.Domains;
            kinds.Add(item.Kind);
        }

        foreach (UsdPhysicsExtractionObjectKind kind in PhysicsExtractionDomainStage.ExpectedKinds)
        {
            await Assert.That(kinds.Contains(kind)).IsTrue()
                .Because($"{kind} must be extracted. Page: {PhysicsExtractionStages.Describe(page)}");
        }

        foreach (UsdPhysicsExtractionDomains domain in PhysicsExtractionDomainStage.ExpectedDomains)
        {
            await Assert.That(seen.HasFlag(domain)).IsTrue().Because($"{domain} must be extracted.");
        }

        // The vehicle domain is simulated now, so it must not be reported as unsupported.
        UsdPhysicsExtractionObject vehicle = PhysicsExtractionStages.Find(
            page, "/Vehicle", UsdPhysicsExtractionObjectKind.Vehicle);
        await Assert.That(vehicle.Flags.HasFlag(UsdPhysicsExtractionObjectTraits.UnsupportedDomain))
            .IsFalse();

        // A domain the runtime still does not simulate is reported once and marked, which is what
        // keeps an authored deformable from silently disappearing into a world that ignores it.
        UsdPhysicsExtractionObject deformable = PhysicsExtractionStages.Find(
            page, "/VolumeDeformable", UsdPhysicsExtractionObjectKind.VolumeDeformable);
        await Assert.That(deformable.Flags.HasFlag(UsdPhysicsExtractionObjectTraits.UnsupportedDomain))
            .IsTrue();
        await Assert.That(PhysicsExtractionStages.HasDiagnostic(
            page, UsdPhysicsExtractionCode.DomainNotSimulated)).IsTrue();

        // Supported domains keep working next to the unsupported ones.
        UsdPhysicsExtractionObject body = PhysicsExtractionStages.Find(
            page, "/Body", UsdPhysicsExtractionObjectKind.RigidBody);
        await Assert.That(body.IsEnabled).IsTrue();
        await Assert.That(body.Flags.HasFlag(UsdPhysicsExtractionObjectTraits.UnsupportedDomain))
            .IsFalse();
    }

    [Test]
    public async Task ExtractionComposesIntoAValidSimulationBuildPage()
    {
        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(ExtractionComposesIntoAValidSimulationBuildPage),
            PhysicsExtractionDomainStage.Usda);

        using var builder = new PhysxPageBuilder();
        UsdPhysicsCompositionReport report = UsdPhysicsExtractionComposer.Compose(page, builder);

        await Assert.That(report.Scenes).IsGreaterThan(0);
        await Assert.That(report.Actors).IsGreaterThan(0);
        await Assert.That(report.Shapes).IsGreaterThan(0);

        using PhysxBuildPage built = builder.Build();
        PhysxPageValidationResult validation = PhysxPageValidator.Validate(built.Bytes);
        await Assert.That(validation.IsValid).IsTrue().Because(validation.Message ?? "no message");

        // Skip notes stay ordered so checkpoint comparisons remain stable.
        using var second = new PhysxPageBuilder();
        UsdPhysicsCompositionReport repeated = UsdPhysicsExtractionComposer.Compose(page, second);
        await Assert.That(repeated.Skipped.ToArray()).IsEquivalentTo(report.Skipped.ToArray());
    }
    [Test]
    public async Task CrossDomainRelationshipsResolveOnTheObjectThatAuthorsThem()
    {
        const string usda = """
            #usda 1.0
            (
                metersPerUnit = 1
                upAxis = "Y"
            )

            def PhysicsScene "Scene"
            {
            }

            def Material "Rubber" (
                prepend apiSchemas = ["PhysicsMaterialAPI"]
            )
            {
                float physics:density = 1200
            }

            def Cube "First" (
                prepend apiSchemas = [
                    "PhysicsRigidBodyAPI",
                    "PhysicsCollisionAPI",
                    "PhysicsFilteredPairsAPI",
                    "MaterialBindingAPI"
                ]
            )
            {
                rel physics:simulationOwner = </Scene>
                rel physics:filteredPairs = </Second>
                rel material:binding:physics = </Rubber>
            }

            def Cube "Second" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI", "PhysicsCollisionAPI"]
            )
            {
                rel physics:simulationOwner = </Scene>
            }
            """;

        UsdPhysicsExtractionPage page = PhysicsExtractionStages.Extract(
            nameof(CrossDomainRelationshipsResolveOnTheObjectThatAuthorsThem), usda);

        UsdPhysicsExtractionObject scene = PhysicsExtractionStages.Find(
            page, "/Scene", UsdPhysicsExtractionObjectKind.Scene);
        UsdPhysicsExtractionObject first = PhysicsExtractionStages.Find(
            page, "/First", UsdPhysicsExtractionObjectKind.RigidBody);

        // The body owns the simulation owner and the filter pair even though those
        // relationships name the scene and filtering domains.
        await Assert.That(first.SceneIndex).IsEqualTo(scene.Index);
        await Assert.That(first.IsEnabled).IsTrue();

        bool sawFilteredPairs = false;
        bool sawMaterialBinding = false;
        for (int index = 0; index < page.ObjectCount; index++)
        {
            UsdPhysicsExtractionObject item = page.GetObject(index);
            if (item.Path != "/First")
            {
                continue;
            }

            for (int offset = 0; offset < item.RelationshipCount; offset++)
            {
                UsdPhysicsExtractionRelationship relationship =
                    page.GetRelationship(item.RelationshipStart + offset);
                if (relationship.Key == UsdPhysicsExtractionKey.FilteredPairsTargets)
                {
                    sawFilteredPairs = true;
                    UsdPhysicsExtractionTarget target =
                        page.GetTarget(relationship.TargetStart);
                    await Assert.That(target.Path).IsEqualTo("/Second");
                    await Assert.That(target.ObjectIndex).IsGreaterThanOrEqualTo(0);
                }

                if (relationship.Key == UsdPhysicsExtractionKey.MaterialBindingTargets)
                {
                    sawMaterialBinding = true;
                }
            }
        }

        await Assert.That(sawFilteredPairs).IsTrue();
        await Assert.That(sawMaterialBinding).IsTrue();
    }
}
