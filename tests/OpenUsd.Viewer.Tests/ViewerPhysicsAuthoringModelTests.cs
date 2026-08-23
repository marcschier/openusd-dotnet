// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics;
using OpenUsd.Physics.Schema;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Asserts the physics authoring model: what the inspector may author, what it must refuse, and
/// that the domains it presents come from the generated schema catalog rather than a hand list.
/// </summary>
public sealed class ViewerPhysicsAuthoringModelTests
{
    [Test]
    public async Task EveryGeneratedSchemaIsProjectedIntoTheInspectorCatalog()
    {
        IReadOnlyList<ViewerPhysicsSchemaDescriptor> projected =
            ViewerPhysicsSchemaProjection.Schemas;

        await Assert.That(projected.Count)
            .IsEqualTo(OpenUsdPhysicsPropertyCatalog.Schemas.Count);
        await Assert.That(projected.Count).IsGreaterThan(30);

        var violations = new List<string>();
        foreach (OpenUsdPhysicsSchemaDescriptor declared in OpenUsdPhysicsPropertyCatalog.Schemas)
        {
            ViewerPhysicsSchemaDescriptor? match =
                ViewerPhysicsSchemaProjection.Find(declared.Identifier);
            if (match is null ||
                match.Properties.Count != declared.Properties.Count ||
                match.RequiredCapability != declared.RequiredCapability)
            {
                violations.Add(declared.Identifier);
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task EveryDomainTheViewerAdvertisesIsRepresentedByAtLeastOneSchema()
    {
        // The inspector claims to cover scenes, rigid bodies, colliders, materials, articulations,
        // tendons, mimic joints, character controllers, vehicles, and the GPU domains. That claim
        // has to be backed by the catalog rather than by a comment.
        string[] domains =
        [
            "Scene",
            "RigidBody",
            "Collision",
            "Material",
            "Articulation",
            "Tendon",
            "MimicJoint",
            "CharacterController",
            "Vehicle",
            "Particles",
            "Cloth",
            "Deformable",
            "Attachment",
        ];

        var missing = new List<string>();
        foreach (string domain in domains)
        {
            bool present = false;
            foreach (ViewerPhysicsSchemaDescriptor schema in ViewerPhysicsSchemaProjection.Schemas)
            {
                if (string.Equals(schema.Domain, domain, StringComparison.Ordinal))
                {
                    present = true;
                    break;
                }
            }

            if (!present)
            {
                missing.Add(domain);
            }
        }

        await Assert.That(missing).IsEmpty();
    }

    [Test]
    public async Task EveryScalarProjectSchemaPropertyIsAuthorable()
    {
        var authorable = 0;
        var violations = new List<string>();
        foreach (ViewerPhysicsSchemaDescriptor schema in ViewerPhysicsSchemaProjection.Schemas)
        {
            foreach (ViewerPhysicsPropertyDescriptor property in schema.Properties)
            {
                if (property.Kind == ViewerPhysicsValueKind.Unsupported)
                {
                    continue;
                }

                authorable++;
                (ViewerPhysicsAuthorability authority, _) = ViewerPhysicsEditability.Classify(
                    property.Name,
                    property.Kind,
                    property.RequiredCapability,
                    UsdPhysicsCapability.All,
                    property.IsEditable);
                if (authority != ViewerPhysicsAuthorability.Editable)
                {
                    violations.Add(property.Name);
                }
            }
        }

        await Assert.That(violations).IsEmpty();
        await Assert.That(authorable).IsGreaterThan(100);
    }

    [Test]
    public async Task AnUnsupportedCapabilityRefusesEditingAndSaysWhy()
    {
        (ViewerPhysicsAuthorability authority, string detail) = ViewerPhysicsEditability.Classify(
            OpenUsdPhysicsTokens.VehicleLateralStickyTireDamping,
            ViewerPhysicsValueKind.Number,
            UsdPhysicsCapability.Vehicles,
            UsdPhysicsCapability.RigidBodies,
            isAuthorable: true);

        await Assert.That(authority).IsEqualTo(ViewerPhysicsAuthorability.UnsupportedCapability);
        await Assert.That(detail).Contains("Vehicles");
    }

    [Test]
    public async Task AValueTypeTheRuntimeCannotCarryIsDescribedButNotAuthorable()
    {
        ViewerPhysicsCoreProperty? mass = ViewerPhysicsCoreProperties.Find("physics:mass");

        await Assert.That(mass).IsNotNull();
        await Assert.That(mass!.IsAuthorable).IsFalse();
        (ViewerPhysicsAuthorability authority, string detail) = ViewerPhysicsEditability.Classify(
            "physics:mass",
            mass.Kind,
            mass.RequiredCapability,
            UsdPhysicsCapability.All,
            mass.IsAuthorable);
        await Assert.That(authority).IsEqualTo(ViewerPhysicsAuthorability.UnsupportedType);
        await Assert.That(detail).IsEqualTo(ViewerPhysicsCoreProperties.UnsupportedTypeDetail);
    }

    [Test]
    public async Task StockBooleanAndTokenPropertiesStayAuthorable()
    {
        string[] authorable =
        [
            "physics:rigidBodyEnabled",
            "physics:kinematicEnabled",
            "physics:startsAsleep",
            "physics:collisionEnabled",
            "physics:jointEnabled",
            "physics:excludeFromArticulation",
            "physics:axis",
            "physics:approximation",
            "physics:mergeGroup",
            "physics:diagonalInertia",
        ];

        var violations = new List<string>();
        foreach (string name in authorable)
        {
            ViewerPhysicsCoreProperty? property = ViewerPhysicsCoreProperties.Find(name);
            if (property is null ||
                !property.IsAuthorable ||
                property.Kind == ViewerPhysicsValueKind.Unsupported)
            {
                violations.Add(name);
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task MultipleApplyDrivesAndLimitsAreLabelledWithTheirInstance()
    {
        ViewerPhysicsCoreProperty? drive =
            ViewerPhysicsCoreProperties.Find("drive:angular:physics:stiffness");
        ViewerPhysicsCoreProperty? limit =
            ViewerPhysicsCoreProperties.Find("limit:linear:physics:low");
        ViewerPhysicsCoreProperty? driveType =
            ViewerPhysicsCoreProperties.Find("drive:linear:physics:type");

        await Assert.That(drive).IsNotNull();
        await Assert.That(drive!.Label).Contains("angular");
        await Assert.That(drive.IsAuthorable).IsFalse();
        await Assert.That(limit).IsNotNull();
        await Assert.That(limit!.Label).Contains("linear");
        await Assert.That(driveType).IsNotNull();
        await Assert.That(driveType!.Kind).IsEqualTo(ViewerPhysicsValueKind.Token);
        await Assert.That(driveType.Tokens).Contains("acceleration");
    }

    [Test]
    public async Task OnlySimulationMetadataIsClassifiedAsSimulationNeutral()
    {
        await Assert.That(ViewerPhysicsAuthoringClassifier.IsSimulationNeutral(
            OpenUsdPhysicsTokens.SimulationSourceRevision)).IsTrue();
        await Assert.That(ViewerPhysicsAuthoringClassifier.IsSimulationNeutral(
            OpenUsdPhysicsTokens.SceneSolverType)).IsFalse();
        await Assert.That(ViewerPhysicsAuthoringClassifier.IsSimulationNeutral(
            "physics:mass")).IsFalse();
        await Assert.That(ViewerPhysicsAuthoringClassifier.IsSimulationNeutral(
            "something:unknown")).IsFalse();
    }

    [Test]
    public async Task ValuesParseExactlyOrRefuseWithAReason()
    {
        await Assert.That(ViewerPhysicsValueParser.TryParse(
            ViewerPhysicsValueKind.Number, [], "12.5", out ViewerPhysicsValue number, out _))
            .IsTrue();
        await Assert.That(number.NumberValue).IsEqualTo(12.5d);
        await Assert.That(number.IsAuthored).IsTrue();

        await Assert.That(ViewerPhysicsValueParser.TryParse(
            ViewerPhysicsValueKind.Number, [], "12kg", out _, out string error)).IsFalse();
        await Assert.That(error).IsNotEmpty();

        await Assert.That(ViewerPhysicsValueParser.TryParse(
            ViewerPhysicsValueKind.Number, [], "NaN", out _, out _)).IsFalse();

        await Assert.That(ViewerPhysicsValueParser.TryParse(
            ViewerPhysicsValueKind.Integer, [], "7", out ViewerPhysicsValue integer, out _))
            .IsTrue();
        await Assert.That(integer.IntegerValue).IsEqualTo(7L);

        await Assert.That(ViewerPhysicsValueParser.TryParse(
            ViewerPhysicsValueKind.Bool, [], "true", out ViewerPhysicsValue flag, out _)).IsTrue();
        await Assert.That(flag.BoolValue).IsTrue();
    }

    [Test]
    public async Task ATokenOutsideTheSchemaListIsRefusedWithTheAllowedTokens()
    {
        string[] tokens = ["pgs", "tgs"];

        await Assert.That(ViewerPhysicsValueParser.TryParse(
            ViewerPhysicsValueKind.Token, tokens, "tgs", out ViewerPhysicsValue value, out _))
            .IsTrue();
        await Assert.That(value.TextValue).IsEqualTo("tgs");

        await Assert.That(ViewerPhysicsValueParser.TryParse(
            ViewerPhysicsValueKind.Token, tokens, "xpbd", out _, out string error)).IsFalse();
        await Assert.That(error).Contains("pgs");
        await Assert.That(error).Contains("tgs");
    }

    [Test]
    public async Task AVectorParsesFromEveryShapeAUserWouldType()
    {
        string[] shapes = ["0 -9.81 0", "(0, -9.81, 0)", "0,-9.81,0", " 0  -9.81  0 "];
        var violations = new List<string>();
        foreach (string text in shapes)
        {
            if (!ViewerPhysicsValueParser.TryParse(
                    ViewerPhysicsValueKind.Vector3,
                    [],
                    text,
                    out ViewerPhysicsValue value,
                    out _) ||
                value.VectorValue.Y != -9.81d)
            {
                violations.Add(text);
            }
        }

        await Assert.That(violations).IsEmpty();

        await Assert.That(ViewerPhysicsValueParser.TryParse(
            ViewerPhysicsValueKind.Vector3, [], "0 1", out _, out string error)).IsFalse();
        await Assert.That(error).IsNotEmpty();
    }

    [Test]
    public async Task AnUnauthoredValueFormatsAsTheSchemaFallbackRatherThanAsZero()
    {
        ViewerPhysicsValue value = ViewerPhysicsValue.Unauthored(ViewerPhysicsValueKind.Number);

        await Assert.That(value.IsAuthored).IsFalse();
        await Assert.That(value.Format()).IsEqualTo("(unauthored)");
        await Assert.That(value.Format("0.2")).IsEqualTo("0.2");
    }

    [Test]
    public async Task TheInspectorProjectsEveryExtractedPropertyIncludingUnknownOnes()
    {
        var document = new ViewerPhysicsExtractionDocument(
            7UL,
            [
                new ViewerPhysicsExtractedObject(
                    1UL,
                    "/World/Body",
                    "RigidBody",
                    IsEnabled: true,
                    [
                        new ViewerPhysicsExtractedProperty(
                            "physics:mass", "4", "Standard", IsAuthored: true),
                        new ViewerPhysicsExtractedProperty(
                            OpenUsdPhysicsTokens.BodySleepThreshold,
                            "0.05",
                            "Project",
                            IsAuthored: true),
                        new ViewerPhysicsExtractedProperty(
                            "vendor:private:thing", "9", "Foreign", IsAuthored: true),
                    ],
                    ["Warning Body OPENUSD_PHYSICS_X: something to know"]),
            ],
            "Extracted one object.");

        IReadOnlyList<ViewerPhysicsObjectSection> sections =
            ViewerPhysicsInspectorProjector.Project(document, UsdPhysicsCapability.All);

        await Assert.That(sections.Count).IsEqualTo(1);
        ViewerPhysicsObjectSection section = sections[0];
        await Assert.That(section.Rows.Count).IsEqualTo(3);
        await Assert.That(section.Diagnostics.Count).IsEqualTo(1);
        await Assert.That(section.Header).Contains("/World/Body");
        await Assert.That(section.EditableCount).IsEqualTo(1);

        ViewerPhysicsPropertyRow? damping = ViewerPhysicsInspectorProjector.FindRow(
            sections, "/World/Body", OpenUsdPhysicsTokens.BodySleepThreshold);
        await Assert.That(damping).IsNotNull();
        await Assert.That(damping!.IsEditable).IsTrue();
        await Assert.That(damping.ValueText).IsEqualTo("0.05");

        ViewerPhysicsPropertyRow? unknown = ViewerPhysicsInspectorProjector.FindRow(
            sections, "/World/Body", "vendor:private:thing");
        await Assert.That(unknown).IsNotNull();
        await Assert.That(unknown!.IsEditable).IsFalse();
        await Assert.That(unknown.ValueText).IsEqualTo("9");
    }

    [Test]
    public async Task TheProjectionCarriesTheComposedCommandAddressOntoEverySection()
    {
        // Two sections of one prim: the extractor's identity tells them apart, and the composed
        // address is what a command is built from. Losing either in the projection would retarget
        // every interaction that follows a reload.
        var document = new ViewerPhysicsExtractionDocument(
            3UL,
            [
                new ViewerPhysicsExtractedObject(
                    11UL,
                    "/World/Car",
                    "RigidBody",
                    IsEnabled: true,
                    [],
                    [],
                    TargetId: 0xAAAAUL,
                    TargetPath: "/World/Car",
                    Commandability: ViewerPhysicsCommandability.Body),
                new ViewerPhysicsExtractedObject(
                    12UL,
                    "/World/Car",
                    "Vehicle",
                    IsEnabled: true,
                    [],
                    [],
                    TargetId: 0xBBBBUL,
                    TargetPath: "/World/Car",
                    Commandability: ViewerPhysicsCommandability.Vehicle),
            ],
            "Extracted two objects.");

        IReadOnlyList<ViewerPhysicsObjectSection> sections =
            ViewerPhysicsInspectorProjector.Project(document, UsdPhysicsCapability.All);

        await Assert.That(sections.Count).IsEqualTo(2);
        await Assert.That(sections[0].TargetId).IsEqualTo(0xAAAAUL);
        await Assert.That(sections[1].TargetId).IsEqualTo(0xBBBBUL);
        await Assert.That(sections[0].Accepts(ViewerPhysicsCommandability.Body)).IsTrue();
        await Assert.That(sections[0].Accepts(ViewerPhysicsCommandability.Vehicle)).IsFalse();
        await Assert.That(sections[1].Accepts(ViewerPhysicsCommandability.Vehicle)).IsTrue();
        await Assert.That(sections[1].Accepts(ViewerPhysicsCommandability.Body)).IsFalse();
    }

    [Test]
    public async Task ADisabledObjectSaysSoRatherThanLookingSimulated()
    {
        var document = new ViewerPhysicsExtractionDocument(
            1UL,
            [
                new ViewerPhysicsExtractedObject(
                    1UL,
                    "/World/Sleeping", "RigidBody", IsEnabled: false, [], []),
            ],
            "Extracted one object.");

        IReadOnlyList<ViewerPhysicsObjectSection> sections =
            ViewerPhysicsInspectorProjector.Project(document, UsdPhysicsCapability.All);

        await Assert.That(sections[0].Detail).Contains("not simulated");
    }

    [Test]
    public async Task AFallbackValueIsLabelledAsAFallbackRatherThanAsAuthored()
    {
        var document = new ViewerPhysicsExtractionDocument(
            1UL,
            [
                new ViewerPhysicsExtractedObject(
                    1UL,
                    "/World/Body",
                    "RigidBody",
                    IsEnabled: true,
                    [
                        new ViewerPhysicsExtractedProperty(
                            OpenUsdPhysicsTokens.BodySleepThreshold,
                            string.Empty,
                            "Fallback",
                            IsAuthored: false),
                    ],
                    []),
            ],
            "Extracted one object.");

        ViewerPhysicsPropertyRow row = ViewerPhysicsInspectorProjector
            .Project(document, UsdPhysicsCapability.All)[0].Rows[0];

        await Assert.That(row.Source).Contains("fallback");
        await Assert.That(row.ValueText).Contains("schema fallback");
    }

    [Test]
    public async Task ProjectingAnEmptyDocumentProducesNoSectionsAndNoAllocationOfRows()
    {
        IReadOnlyList<ViewerPhysicsObjectSection> sections =
            ViewerPhysicsInspectorProjector.Project(
                ViewerPhysicsExtractionDocument.Empty,
                UsdPhysicsCapability.All);

        await Assert.That(sections).IsEmpty();
    }
}
