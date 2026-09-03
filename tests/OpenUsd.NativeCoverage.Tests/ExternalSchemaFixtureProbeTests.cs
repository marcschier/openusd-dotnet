// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.NativeCoverage.Tests;

/// <summary>
/// Checks that a real OpenUSD registry loads a synthetic, repository-authored, codeless schema
/// plugin supplied from *outside* this repository's own <c>schemas/</c> tree, resolves its real
/// type name and properties, and keeps standard USD behavior on the same prim intact.
/// </summary>
/// <remarks>
/// <para>
/// This exercises the same "codeless plugin, real registry" contract as
/// <c>OpenUsdPhysicsSchemaProbeTests</c>, but for a plugin tree this repository never ships:
/// the fixture lives under <c>test-assets/omniverse/external-schema/</c>, is registered only
/// for this test process, and stands in for any externally supplied codeless schema/plugin
/// tree an Omniverse-interchange consumer might register (for example, a vendor's own
/// project-specific schema). These assertions only mean anything in a process that had the
/// plugin root on <c>PXR_PLUGINPATH_NAME</c> before OpenUSD built its schema registry, so they
/// no-op unless <see cref="ProbeVariable"/> is set. <see cref="ExternalSchemaFixtureRegistrationTests"/>
/// launches that process and fails with its output when anything here fails.
/// </para>
/// </remarks>
public sealed class ExternalSchemaFixtureProbeTests
{
    internal const string ProbeVariable = "OPENUSD_EXTERNAL_SCHEMA_PROBE";

    internal static bool IsProbeProcess =>
        Environment.GetEnvironmentVariable(ProbeVariable) == "1";

    [Test]
    public async Task ConcreteExternalTypeResolvesByNameWithoutFlattening()
    {
        if (!IsProbeProcess)
        {
            return;
        }

        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(ConcreteExternalTypeResolvesByNameWithoutFlattening));
        string stagePath = Path.Combine(directory, "widget.usda");

        using UsdStage stage = UsdStage.Create(stagePath);
        UsdPrim prim = stage.DefinePrim("/World/Widget", "OmniverseExternalFixtureWidget");

        await Assert.That(prim.TypeName).IsEqualTo("OmniverseExternalFixtureWidget")
            .Because("an externally registered codeless plugin must resolve its own type name, " +
                "not fall back to a flattened or generic Typed prim.");

        string[] attributes = prim.GetAttributeNames();
        await Assert.That(attributes).Contains("omniverseExternalFixture:widget:label");
        await Assert.That(attributes).Contains("omniverseExternalFixture:widget:weight");

        string exportPath = Path.Combine(directory, "widget-export.usda");
        stage.Export(exportPath);
        using UsdStage reopened = UsdStage.Open(exportPath);
        UsdPrim reopenedPrim = reopened.GetPrim("/World/Widget");

        await Assert.That(reopenedPrim.TypeName).IsEqualTo("OmniverseExternalFixtureWidget")
            .Because("the externally registered type name must survive export and reopen, not " +
                "be flattened away or rewritten.");
        await Assert.That(reopenedPrim.GetAttributeNames())
            .Contains("omniverseExternalFixture:widget:label");
    }

    [Test]
    public async Task AppliedExternalApiSchemaContributesItsFallbacksWithoutCollision()
    {
        if (!IsProbeProcess)
        {
            return;
        }

        string directory = NativeCoverageRuntime.CreateTempDirectory(
            nameof(AppliedExternalApiSchemaContributesItsFallbacksWithoutCollision));
        string stagePath = Path.Combine(directory, "tagged.usda");
        await File.WriteAllTextAsync(
            stagePath,
            """
            #usda 1.0

            def Xform "Tagged" (
                prepend apiSchemas = ["OmniverseExternalFixtureTagAPI"]
            )
            {
                double3 xformOp:translate = (1, 2, 3)
                uniform token[] xformOpOrder = ["xformOp:translate"]
            }
            """);

        using UsdStage stage = UsdStage.Open(stagePath);
        UsdPrim prim = stage.GetPrim("/Tagged");

        await Assert.That(prim.GetAppliedSchemas()).Contains("OmniverseExternalFixtureTagAPI");
        await Assert.That(prim.GetAttributeNames()).Contains("omniverseExternalFixture:tag:category")
            .Because("the applied external API schema must contribute its own fallback property.");

        // Standard Xformable behavior on the same prim must be untouched by the externally
        // registered plugin: no collision, no package-path rewriting of unrelated properties.
        await Assert.That(prim.TypeName).IsEqualTo("Xform");
        await Assert.That(prim.GetAttributeNames()).Contains("xformOp:translate");
    }
}
