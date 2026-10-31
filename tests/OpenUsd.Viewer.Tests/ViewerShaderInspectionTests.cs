// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerShaderInspectionTests
{
    [Test]
    [Arguments(UsdPropertyValueState.Unset,
        "No shader info:id is available. Source-asset definitions are not resolved here.")]
    [Arguments(UsdPropertyValueState.Blocked, "Shader info:id is blocked; no registry definition was selected.")]
    [Arguments(UsdPropertyValueState.Deferred, "Shader info:id is deferred; its identifier has not been proven.")]
    public async Task UnavailableIdentifiersNeverUseCoincidentalPreviewText(
        UsdPropertyValueState state, string reason)
    {
        int reads = 0;
        ViewerShaderInspection result = ViewerShaderInspection.Read("id", "UsdPreviewSurface", () =>
        {
            reads++;
            throw new InvalidOperationException("An unavailable identifier cannot authorize a registry lookup.");
        }, state);
        await Assert.That(reads).IsEqualTo(0);
        await Assert.That(result.HasDefinition).IsFalse();
        await Assert.That(result.Summary).IsEqualTo(reason);
        await Assert.That(result.Properties).IsEmpty();
        await Assert.That(result.DescribeProperty("inputs:roughness")).IsNull();
        await Assert.That(() => ViewerShaderInspection.Read(
            "id", "UsdPreviewSurface", () => throw new InvalidOperationException(), (UsdPropertyValueState)99))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments("sourceAsset")]
    [Arguments("sourceCode")]
    [Arguments("ID")]
    [Arguments("unknown")]
    [Arguments("")]
    public async Task NonIdImplementationNeverReadsTheRegistryOrUsesACoincidentalIdentifier(string source)
    {
        int reads = 0;
        ViewerShaderInspection inspection = ViewerShaderInspection.Read(source, "Exact", () =>
        {
            reads++;
            return new UsdShaderNodeDefinitionSnapshot([Definition("Exact", [])], false);
        });

        await Assert.That(reads).IsEqualTo(0);
        await Assert.That(inspection.HasDefinition).IsFalse();
        await Assert.That(inspection.Properties).IsEmpty();
        await Assert.That(inspection.Summary).Contains($"'{source}'");
        await Assert.That(inspection.Summary).Contains("does not use info:id");
        await Assert.That(inspection.DescribeProperty("inputs:value")).IsNull();
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    public async Task MissingIdentifierDoesNotEnumerateTheRegistry(string identifier)
    {
        ViewerShaderInspection inspection = ViewerShaderInspection.Read("id", identifier,
            static () => throw new InvalidOperationException("Unexpected registry read."));

        await Assert.That(inspection.HasDefinition).IsFalse();
        await Assert.That(inspection.Summary).StartsWith("No shader info:id");
    }

    [Test]
    public async Task ExplicitIdReadsOnceAndPreservesRegistryFailures()
    {
        int reads = 0;
        ViewerShaderInspection inspection = ViewerShaderInspection.Read("id", "Exact", () =>
        {
            reads++;
            return new UsdShaderNodeDefinitionSnapshot([Definition("Exact", [])], false);
        });

        await Assert.That(reads).IsEqualTo(1);
        await Assert.That(inspection.HasDefinition).IsTrue();
        await Assert.That(inspection.Summary).StartsWith("Sdr definition: Exact");
        var failure = new InvalidOperationException("Registry unavailable.");
        await Assert.That(() => ViewerShaderInspection.Read("id", "Exact", () => throw failure))
            .Throws<InvalidOperationException>().WithMessage("Registry unavailable.");
        await Assert.That(() => ViewerShaderInspection.Read(null!, "Exact",
            static () => new UsdShaderNodeDefinitionSnapshot([], false))).Throws<ArgumentNullException>();
        await Assert.That(() => ViewerShaderInspection.Read("id", null!,
            static () => new UsdShaderNodeDefinitionSnapshot([], false))).Throws<ArgumentNullException>();
        await Assert.That(() => ViewerShaderInspection.Read("id", "Exact", null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ExactIdentifierPreservesDirectionsNamespacesAndDetachedPropertyMetadata()
    {
        UsdShaderProperty[] properties =
        [
            new("value", "float", UsdShaderPropertyDirection.Input, false, true),
            new("value", "color", UsdShaderPropertyDirection.Output, true, false),
            new("nested:value", "token", UsdShaderPropertyDirection.Input, false, false)
        ];
        var registry = new UsdShaderNodeDefinitionSnapshot([Definition("Exact", properties)], false);
        ViewerShaderInspection inspection = ViewerShaderInspection.Create("Exact", registry);
        properties[0] = properties[0] with { Type = "changed" };

        await Assert.That(inspection.HasDefinition).IsTrue();
        await Assert.That(inspection.Summary).Contains("Sdr definition: Exact");
        await Assert.That(inspection.DescribeProperty("inputs:value"))
            .IsEqualTo("Sdr input; type: float; array: No; connectable: Yes. Not edit permission.");
        await Assert.That(inspection.DescribeProperty("outputs:value"))
            .IsEqualTo("Sdr output; type: color; array: Yes; connectable: No. Not edit permission.");
        await Assert.That(inspection.DescribeProperty("inputs:nested:value"))
            .IsEqualTo("Sdr input; type: token; array: No; connectable: No. Not edit permission.");
        await Assert.That(inspection.DescribeProperty("info:id")).IsNull();
        await Assert.That(inspection.DescribeProperty("inputs:VALUE"))
            .StartsWith("No matching property in this Sdr definition");
        await Assert.That(inspection.DescribeProperty("value")).IsNull();
        await Assert.That(() => ((IList<UsdShaderProperty>)inspection.Properties).Clear())
            .Throws<NotSupportedException>();
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("exact")]
    [Arguments("DisplayName")]
    [Arguments("unknown")]
    public async Task MissingOrNonExactIdentifiersNeverGuessADefinition(string identifier)
    {
        ViewerShaderInspection inspection = ViewerShaderInspection.Create(identifier,
            new UsdShaderNodeDefinitionSnapshot([Definition("Exact", [])], false));

        await Assert.That(inspection.HasDefinition).IsFalse();
        await Assert.That(inspection.Properties).IsEmpty();
        await Assert.That(inspection.Summary).Contains(identifier.Length == 0 || string.IsNullOrWhiteSpace(identifier)
            ? "No shader info:id"
            : "No Sdr definition matches");
        await Assert.That(inspection.DescribeProperty("inputs:value")).IsNull();
    }

    [Test]
    public async Task TruncatedAmbiguousAndInvalidDefinitionsDoNotGrantMetadataClaims()
    {
        UsdShaderNodeDefinition definition = Definition("Exact",
            [new("value", "float", UsdShaderPropertyDirection.Input, false, true)]);
        ViewerShaderInspection truncated = ViewerShaderInspection.Create("Exact",
            new UsdShaderNodeDefinitionSnapshot([definition], true));
        ViewerShaderInspection ambiguous = ViewerShaderInspection.Create("Exact",
            new UsdShaderNodeDefinitionSnapshot([definition, definition with { ShadingSystem = "other" }], false));
        ViewerShaderInspection invalid = ViewerShaderInspection.Create("Exact",
            new UsdShaderNodeDefinitionSnapshot([definition with { IsValid = false }], false));

        await Assert.That(truncated.Summary).Contains("truncated");
        await Assert.That(ambiguous.Summary).Contains("Multiple Sdr definitions");
        await Assert.That(invalid.Summary).Contains("invalid");
        foreach (ViewerShaderInspection inspection in new[] { truncated, ambiguous, invalid })
        {
            await Assert.That(inspection.HasDefinition).IsFalse();
            await Assert.That(inspection.Properties).IsEmpty();
            await Assert.That(inspection.DescribeProperty("inputs:value")).IsNull();
        }
    }

    [Test]
    public async Task EmptyRegistryIsDistinctFromAValidDefinitionWithoutProperties()
    {
        ViewerShaderInspection absent = ViewerShaderInspection.Create("Exact",
            new UsdShaderNodeDefinitionSnapshot([], false));
        ViewerShaderInspection empty = ViewerShaderInspection.Create("Exact",
            new UsdShaderNodeDefinitionSnapshot([Definition("Exact", [])], false));

        await Assert.That(absent.HasDefinition).IsFalse();
        await Assert.That(absent.Summary).StartsWith("No Sdr definition matches");
        await Assert.That(absent.DescribeProperty("inputs:value")).IsNull();
        await Assert.That(empty.HasDefinition).IsTrue();
        await Assert.That(empty.Properties).IsEmpty();
        await Assert.That(empty.DescribeProperty("inputs:value")).StartsWith("No matching property");
    }

    [Test]
    public async Task DuplicatePropertiesAreExplicitlyAmbiguousWithoutDroppingCustomGraphProperties()
    {
        UsdShaderProperty property = new("value", "float", UsdShaderPropertyDirection.Input, false, true);
        ViewerShaderInspection inspection = ViewerShaderInspection.Create("Exact",
            new UsdShaderNodeDefinitionSnapshot([Definition("Exact", [property, property])], false));

        await Assert.That(inspection.DescribeProperty("inputs:value"))
            .IsEqualTo("Ambiguous property metadata in this Sdr definition; no type or connection claim.");
        await Assert.That(inspection.DescribeProperty("inputs:custom"))
            .IsEqualTo("No matching property in this Sdr definition; authored property and connections are preserved.");
        await Assert.That(inspection.Properties.Count).IsEqualTo(2);
    }

    [Test]
    public async Task NullInputsAreRejectedAndDisplayedNativeStringsAreBounded()
    {
        await Assert.That(() => ViewerShaderInspection.Create(null!,
            new UsdShaderNodeDefinitionSnapshot([], false))).Throws<ArgumentNullException>();
        await Assert.That(() => ViewerShaderInspection.Create("Exact", null!))
            .Throws<ArgumentNullException>();
        string identifier = new('a', 4096);
        ViewerShaderInspection inspection = ViewerShaderInspection.Create(identifier,
            new UsdShaderNodeDefinitionSnapshot(
                [Definition(identifier, [new("value", new string('b', 4096),
                    UsdShaderPropertyDirection.Input, false, false)]) with
                { ShadingSystem = new string('c', 4096), Context = new string('d', 4096) }], false));
        await Assert.That(inspection.Summary.Length).IsLessThan(650);
        await Assert.That(inspection.DescribeProperty("inputs:value")!.Length).IsLessThan(250);
        await Assert.That(() => inspection.DescribeProperty(null!)).Throws<ArgumentNullException>();
    }

    private static UsdShaderNodeDefinition Definition(string identifier, IReadOnlyList<UsdShaderProperty> properties) =>
        new(identifier, "DisplayName", "function", "glslfx", "surface", "", "", "", properties, true);
}
