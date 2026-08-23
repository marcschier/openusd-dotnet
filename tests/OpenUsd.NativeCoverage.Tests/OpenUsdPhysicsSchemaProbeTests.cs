// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Schema;

namespace OpenUsd.NativeCoverage.Tests;

/// <summary>
/// Checks that a real OpenUSD registry loads the codeless <c>openUsdPhysics</c> plugin,
/// resolves its types, and keeps the standard <c>UsdPhysics</c> schema intact.
/// </summary>
/// <remarks>
/// <para>
/// These assertions only mean anything in a process that had the plugin root on
/// <c>PXR_PLUGINPATH_NAME</c> before OpenUSD built its schema registry, so they no-op
/// unless <see cref="ProbeVariable"/> is set.
/// <see cref="OpenUsdPhysicsSchemaRegistrationTests"/> launches that process and fails
/// with the output when anything here fails; running this class directly in the shared
/// test host would only measure test ordering.
/// </para>
/// <para>
/// A codeless plugin fails quietly when it is wrong. A bad <c>plugInfo.json</c>, a
/// malformed <c>generatedSchema.usda</c>, or a missing prim-type alias yields a prim with
/// no fallback properties rather than an error, and the first visible symptom is a
/// physics value reading back as a default much later.
/// </para>
/// </remarks>
public sealed class OpenUsdPhysicsSchemaProbeTests
{
    internal const string ProbeVariable = "OPENUSD_SCHEMA_PROBE";

    internal static bool IsProbeProcess =>
        Environment.GetEnvironmentVariable(ProbeVariable) == "1";

    [Test]
    public async Task ConcreteTypeContributesItsProjectNamespaceFallbacks()
    {
        if (!IsProbeProcess)
        {
            return;
        }

        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(ConcreteTypeContributesItsProjectNamespaceFallbacks));
        string stagePath = Path.Combine(directory, "friction-table.usda");

        using UsdStage stage = UsdStage.Create(stagePath);
        UsdPrim prim = stage.DefinePrim("/World/FrictionTable", "OpenUsdPhysicsVehicleTireFrictionTable");

        await Assert.That(prim.TypeName).IsEqualTo("OpenUsdPhysicsVehicleTireFrictionTable")
            .Because("the codeless plugin must make the concrete type resolvable by name.");

        string[] attributes = prim.GetAttributeNames();
        await Assert.That(attributes).Contains("openUsdPhysics:frictionTable:defaultFrictionValue");
        await Assert.That(attributes).Contains("openUsdPhysics:frictionTable:frictionValues");
        await Assert.That(prim.GetRelationshipNames()).Contains("openUsdPhysics:frictionTable:groundMaterials");
    }

    [Test]
    public async Task AppliedApiSchemasContributeTheirProjectNamespaceFallbacks()
    {
        if (!IsProbeProcess)
        {
            return;
        }

        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(AppliedApiSchemasContributeTheirProjectNamespaceFallbacks));
        string stagePath = Path.Combine(directory, "vehicle.usda");
        await File.WriteAllTextAsync(
            stagePath,
            """
            #usda 1.0

            def Xform "Vehicle" (
                prepend apiSchemas = ["OpenUsdPhysicsVehicleAPI", "OpenUsdPhysicsCharacterControllerAPI"]
            )
            {
            }
            """);

        using UsdStage stage = UsdStage.Open(stagePath);
        UsdPrim prim = stage.GetPrim("/Vehicle");

        await Assert.That(prim.GetAppliedSchemas()).Contains("OpenUsdPhysicsVehicleAPI");

        string[] attributes = prim.GetAttributeNames();
        await Assert.That(attributes).Contains("openUsdPhysics:vehicle:enabled");
        await Assert.That(attributes).Contains("openUsdPhysics:vehicle:driveType");
        await Assert.That(attributes).Contains("openUsdPhysics:controller:stepOffset")
            .Because("a second applied project API must contribute its own fallbacks on the same prim.");

        IEnumerable<string> projectAttributes = attributes
            .Where(name => name.StartsWith("openUsdPhysics:", StringComparison.Ordinal));
        foreach (string attribute in projectAttributes)
        {
            await Assert.That(attribute.Split(':')).Count().IsEqualTo(3)
                .Because($"'{attribute}' must keep the openUsdPhysics:<group>:<name> shape after registration.");
        }
    }

    [Test]
    public async Task ProjectSchemaLayersOverStandardPhysicsWithoutDisplacingIt()
    {
        if (!IsProbeProcess)
        {
            return;
        }

        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(ProjectSchemaLayersOverStandardPhysicsWithoutDisplacingIt));
        string stagePath = Path.Combine(directory, "precedence.usda");
        await File.WriteAllTextAsync(
            stagePath,
            """
            #usda 1.0

            def Xform "Body" (
                prepend apiSchemas = ["PhysicsRigidBodyAPI", "OpenUsdPhysicsRigidBodySettingsAPI"]
            )
            {
                custom float physxRigidBody:sleepThreshold = 7
            }
            """);

        using UsdStage stage = UsdStage.Open(stagePath);
        UsdPrim prim = stage.GetPrim("/Body");
        string[] attributes = prim.GetAttributeNames();

        await Assert.That(attributes).Contains("physics:rigidBodyEnabled")
            .Because("the standard UsdPhysics opinion must remain readable underneath the project one.");
        await Assert.That(attributes).Contains("openUsdPhysics:body:sleepThreshold")
            .Because("the project opinion is the strongest and must be present alongside it.");
        await Assert.That(attributes).Contains("physxRigidBody:sleepThreshold")
            .Because("an unsupported physx* opinion must survive so it can be reported, not silently dropped.");
    }

    [Test]
    public async Task ExpandedDomainSchemasResolveAndContributeTheirFallbacks()
    {
        if (!IsProbeProcess)
        {
            return;
        }

        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(ExpandedDomainSchemasResolveAndContributeTheirFallbacks));
        string stagePath = Path.Combine(directory, "domains.usda");
        await File.WriteAllTextAsync(
            stagePath,
            """
            #usda 1.0

            def "Tendon" (
                prepend apiSchemas = ["OpenUsdPhysicsTendonAttachmentAPI"]
            )
            {
            }

            def Xform "Soft" (
                prepend apiSchemas = [
                    "OpenUsdPhysicsParticleSystemAPI",
                    "OpenUsdPhysicsSurfaceDeformableAPI",
                    "OpenUsdPhysicsVolumeDeformableAPI",
                    "OpenUsdPhysicsCollisionFilterSettingsAPI",
                    "OpenUsdPhysicsCookedDataAPI",
                    "OpenUsdPhysicsMimicJointAPI"
                ]
            )
            {
            }
            """);

        using UsdStage stage = UsdStage.Open(stagePath);

        UsdPrim soft = stage.GetPrim("/Soft");
        string[] softAttributes = soft.GetAttributeNames();
        await Assert.That(softAttributes).Contains("openUsdPhysics:particleSystem:particleContactOffset");
        await Assert.That(softAttributes).Contains("openUsdPhysics:surfaceDeformable:selfCollision");
        await Assert.That(softAttributes).Contains("openUsdPhysics:volumeDeformable:sleepThreshold");
        await Assert.That(softAttributes).Contains("openUsdPhysics:collisionFilter:pairFilterMode");
        await Assert.That(softAttributes).Contains("openUsdPhysics:cookedData:assetPath");
        await Assert.That(softAttributes).Contains("openUsdPhysics:mimicJoint:gearing")
            .Because("every expanded domain must contribute fallbacks from the same codeless plugin.");

        UsdPrim tendon = stage.GetPrim("/Tendon");
        await Assert.That(tendon.GetAttributeNames()).Contains("openUsdPhysics:tendonAttachment:gearing");

        UsdPrim fixedTendon = stage.DefinePrim("/World/FixedTendon", "OpenUsdPhysicsFixedTendon");
        await Assert.That(fixedTendon.TypeName).IsEqualTo("OpenUsdPhysicsFixedTendon");
        await Assert.That(fixedTendon.GetAttributeNames()).Contains("openUsdPhysics:fixedTendon:stiffness");

        UsdPrim attachment = stage.DefinePrim("/World/Attachment", "OpenUsdPhysicsAttachment");
        await Assert.That(attachment.TypeName).IsEqualTo("OpenUsdPhysicsAttachment");
        await Assert.That(attachment.GetAttributeNames()).Contains("openUsdPhysics:attachment:attachmentType");
        await Assert.That(attachment.GetRelationshipNames()).Contains("openUsdPhysics:attachment:actor0");
    }

    [Test]
    public async Task GeneratedTokensAddressTheAttributesTheRegistryReports()
    {
        if (!IsProbeProcess)
        {
            return;
        }

        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(GeneratedTokensAddressTheAttributesTheRegistryReports));
        string stagePath = Path.Combine(directory, "tokens.usda");
        await File.WriteAllTextAsync(
            stagePath,
            """
            #usda 1.0

            def Xform "Body" (
                prepend apiSchemas = ["OpenUsdPhysicsParticleSystemAPI"]
            )
            {
            }
            """);

        using UsdStage stage = UsdStage.Open(stagePath);
        UsdPrim prim = stage.GetPrim("/Body");
        OpenUsdPhysicsParticleSystemAPI particles = OpenUsdPhysicsParticleSystemAPI.Wrap(prim);

        await Assert.That(OpenUsdPhysicsParticleSystemAPI.Has(prim)).IsTrue()
            .Because("the generated facade must recognise its own applied schema identifier.");
        await Assert.That(prim.GetAttributeNames())
            .Contains(OpenUsdPhysicsTokens.ParticleSystemParticleContactOffset)
            .Because("generated token constants must match the registry's property names exactly.");

        particles.ParticleContactOffset = 0.25;
        await Assert.That(particles.ParticleContactOffset).IsEqualTo(0.25)
            .Because("a generated facade must round-trip a value through the real attribute.");

        particles.Enabled = true;
        await Assert.That(particles.Enabled).IsTrue();
    }

    internal static string ProjectSchemaResources() =>
        Path.Combine(FindRepositoryRoot(), "schemas", "openUsdPhysics", "resources");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Unable to locate the repository root from '{AppContext.BaseDirectory}'.");
    }
}
