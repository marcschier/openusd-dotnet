// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer.Tests;

public sealed class ViewerSessionModelTests
{
    [Test]
    public async Task CommandEligibilityRequiresInteractiveSelectionAndSchemaSupport()
    {
        var enabled = new ViewerPrimCommandContext(
            HasDocument: true,
            IsBusy: false,
            IsAutomated: false,
            HasSelection: true,
            IsImageable: true,
            IsInstance: false,
            IsPrototype: false);

        await Assert.That(ViewerSessionCommandPolicy.CanExecute(
            ViewerPrimCommand.SetActive,
            enabled)).IsTrue();
        await Assert.That(ViewerSessionCommandPolicy.CanExecute(
            ViewerPrimCommand.SetPurpose,
            enabled)).IsTrue();
        await Assert.That(ViewerSessionCommandPolicy.CanExecute(
            ViewerPrimCommand.SetPurpose,
            enabled with { IsImageable = false })).IsFalse();
        await Assert.That(ViewerSessionCommandPolicy.CanExecute(
            ViewerPrimCommand.SetInstanceable,
            enabled with { IsInstance = true })).IsFalse();
        await Assert.That(ViewerSessionCommandPolicy.CanExecute(
            ViewerPrimCommand.SetLoaded,
            enabled with { IsAutomated = true })).IsFalse();
        await Assert.That(ViewerSessionCommandPolicy.CanExecute(
            ViewerPrimCommand.SetVariantSelection,
            enabled)).IsTrue();
        await Assert.That(ViewerSessionCommandPolicy.CanExecute(
            ViewerPrimCommand.SetVariantSelection,
            enabled with { IsAutomated = true })).IsFalse();
        await Assert.That(ViewerSessionCommandPolicy.CanExecute(
            ViewerPrimCommand.SetActive,
            enabled with { IsBusy = true })).IsFalse();
        await Assert.That(ViewerSessionCommandPolicy.CanExecute(
            ViewerPrimCommand.SetActive,
            enabled with { HasSelection = false })).IsFalse();
    }

    [Test]
    public async Task CommandsDeclareCorrectInvalidation()
    {
        await Assert.That(ViewerSessionCommandPolicy.GetInvalidation(
            ViewerPrimCommand.SetActive)).IsEqualTo(UsdStageInvalidationKind.Topology);
        await Assert.That(ViewerSessionCommandPolicy.GetInvalidation(
            ViewerPrimCommand.SetLoaded)).IsEqualTo(UsdStageInvalidationKind.Composition);
        await Assert.That(ViewerSessionCommandPolicy.GetInvalidation(
            ViewerPrimCommand.SetInstanceable)).IsEqualTo(UsdStageInvalidationKind.Composition);
        await Assert.That(ViewerSessionCommandPolicy.GetInvalidation(
            ViewerPrimCommand.SetVisibility)).IsEqualTo(UsdStageInvalidationKind.Property);
        await Assert.That(ViewerSessionCommandPolicy.GetInvalidation(
            ViewerPrimCommand.SetPurpose)).IsEqualTo(UsdStageInvalidationKind.Property);
        await Assert.That(ViewerSessionCommandPolicy.GetInvalidation(
            ViewerPrimCommand.SetVariantSelection))
            .IsEqualTo(UsdStageInvalidationKind.Composition);
    }

    [Test]
    public async Task SessionLayerIsDefaultAndRootRequiresExplicitPolicy()
    {
        await Assert.That(ViewerSessionCommandPolicy.ResolveEditTarget(
            rootLayerEditsExplicitlyEnabled: false))
            .IsEqualTo(ViewerSessionEditTarget.Session);
        await Assert.That(ViewerSessionCommandPolicy.ResolveEditTarget(
            rootLayerEditsExplicitlyEnabled: true))
            .IsEqualTo(ViewerSessionEditTarget.ExplicitRoot);
    }

    [Test]
    public async Task RequestsRejectMissingOrUnknownValues()
    {
        await Assert.That(() => new ViewerPrimCommandRequest(
            ViewerPrimCommand.SetActive).Validate())
            .Throws<InvalidOperationException>();
        await Assert.That(() => new ViewerPrimCommandRequest(
            ViewerPrimCommand.SetVisibility,
            TokenValue: "hidden").Validate())
            .Throws<InvalidOperationException>();
        new ViewerPrimCommandRequest(
            ViewerPrimCommand.SetPurpose,
            TokenValue: "Proxy").Validate();
        new ViewerPrimCommandRequest(
            ViewerPrimCommand.SetVariantSelection,
            VariantSetName: "look",
            AvailableVariantNames: ["red", "blue"]).Validate();
        new ViewerPrimCommandRequest(
            ViewerPrimCommand.SetVariantSelection,
            TokenValue: "blue",
            VariantSetName: "look",
            AvailableVariantNames: ["red", "blue"]).Validate();
        await Assert.That(() => new ViewerPrimCommandRequest(
            ViewerPrimCommand.SetVariantSelection,
            TokenValue: "missing",
            VariantSetName: "look",
            AvailableVariantNames: ["red", "blue"]).Validate())
            .Throws<InvalidOperationException>();
        await Assert.That(() => new ViewerPrimCommandRequest(
            ViewerPrimCommand.SetVariantSelection,
            TokenValue: "red",
            VariantSetName: string.Empty,
            AvailableVariantNames: ["red"]).Validate())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task UnsupportedCatalogNoLongerClaimsCompositionEnumerationIsUnavailable()
    {
        await Assert.That(ViewerUnsupportedFeatureCatalog.SessionControlApiGaps).IsEmpty();
        await Assert.That(ViewerUnsupportedFeatureCatalog.PurposeVisibilityNotImageable.Code)
            .IsEqualTo("PRIM_NOT_USDGEOM_IMAGEABLE");
    }
}
