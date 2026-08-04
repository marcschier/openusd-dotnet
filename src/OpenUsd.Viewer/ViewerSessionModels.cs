// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

internal enum ViewerPrimCommand
{
    SetActive,
    SetLoaded,
    SetInstanceable,
    SetVisibility,
    SetPurpose,
    SetVariantSelection,
    ClearAttributeValue,
    BlockAttributeValue
}

internal enum ViewerSessionEditTarget
{
    Session,
    ExplicitRoot
}

internal readonly record struct ViewerPrimCommandContext(
    bool HasDocument,
    bool IsBusy,
    bool IsAutomated,
    bool HasSelection,
    bool IsImageable,
    bool IsInstance,
    bool IsPrototype);

internal static class ViewerSessionCommandPolicy
{
    internal static bool CanExecute(
        ViewerPrimCommand command,
        ViewerPrimCommandContext context)
    {
        if (!context.HasDocument ||
            context.IsBusy ||
            context.IsAutomated ||
            !context.HasSelection)
        {
            return false;
        }
        return command switch
        {
            ViewerPrimCommand.SetVisibility or ViewerPrimCommand.SetPurpose =>
                context.IsImageable,
            ViewerPrimCommand.SetInstanceable =>
                !context.IsInstance && !context.IsPrototype,
            ViewerPrimCommand.SetActive or
            ViewerPrimCommand.SetLoaded or
            ViewerPrimCommand.SetVariantSelection or
            ViewerPrimCommand.ClearAttributeValue or
            ViewerPrimCommand.BlockAttributeValue => true,
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
    }

    internal static UsdStageInvalidationKind GetInvalidation(ViewerPrimCommand command) =>
        command switch
        {
            ViewerPrimCommand.SetActive => UsdStageInvalidationKind.Topology,
            ViewerPrimCommand.SetLoaded or
            ViewerPrimCommand.SetInstanceable or
            ViewerPrimCommand.SetVariantSelection =>
                UsdStageInvalidationKind.Composition,
            ViewerPrimCommand.SetVisibility or ViewerPrimCommand.SetPurpose =>
                UsdStageInvalidationKind.Property,
            ViewerPrimCommand.ClearAttributeValue or ViewerPrimCommand.BlockAttributeValue =>
                UsdStageInvalidationKind.Property,
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };

    internal static ViewerSessionEditTarget ResolveEditTarget(
        bool rootLayerEditsExplicitlyEnabled) =>
        rootLayerEditsExplicitlyEnabled
            ? ViewerSessionEditTarget.ExplicitRoot
            : ViewerSessionEditTarget.Session;
}

internal static class ViewerUnsupportedFeatureCatalog
{
    internal static ViewerUnsupportedFeature PurposeVisibilityNotImageable { get; } = new(
        "PRIM_NOT_USDGEOM_IMAGEABLE",
        "Purpose and visibility are unavailable because the selected prim is not " +
        "compatible with UsdGeomImageable.");

    internal static ViewerUnsupportedFeature RendererPickingUnavailable { get; } = new(
        "VIEWER_RENDERER_PICKING_UNAVAILABLE",
        "The active renderer cannot resolve this picking request. The current selection was kept.");

    internal static ViewerUnsupportedFeature[] SessionControlApiGaps { get; } = [];
}

internal sealed record ViewerPrimCommandRequest(
    ViewerPrimCommand Command,
    bool? BooleanValue = null,
    string? TokenValue = null,
    string? VariantSetName = null,
    string? AttributeName = null,
    string? PrimPath = null,
    string[]? AvailableVariantNames = null)
{
    internal void Validate()
    {
        if (PrimPath is not null &&
            (string.IsNullOrWhiteSpace(PrimPath) || PrimPath[0] != '/'))
        {
            throw new InvalidOperationException("PrimPath must be an absolute prim path when supplied.");
        }

        switch (Command)
        {
            case ViewerPrimCommand.SetActive:
            case ViewerPrimCommand.SetLoaded:
            case ViewerPrimCommand.SetInstanceable:
                if (BooleanValue is null ||
                    TokenValue is not null ||
                    VariantSetName is not null ||
                    AttributeName is not null ||
                    AvailableVariantNames is not null)
                {
                    throw new InvalidOperationException(
                        $"{Command} requires one Boolean value.");
                }
                break;
            case ViewerPrimCommand.SetVisibility:
                if (BooleanValue is not null ||
                    VariantSetName is not null ||
                    AttributeName is not null ||
                    AvailableVariantNames is not null ||
                    TokenValue is not ("Inherited" or "Invisible"))
                {
                    throw new InvalidOperationException(
                        "Visibility requires Inherited or Invisible.");
                }
                break;
            case ViewerPrimCommand.SetPurpose:
                if (BooleanValue is not null ||
                    VariantSetName is not null ||
                    AttributeName is not null ||
                    AvailableVariantNames is not null ||
                    TokenValue is not ("Default" or "Render" or "Proxy" or "Guide"))
                {
                    throw new InvalidOperationException(
                        "Purpose requires Default, Render, Proxy, or Guide.");
                }
                break;
            case ViewerPrimCommand.SetVariantSelection:
                if (BooleanValue is not null ||
                    AttributeName is not null ||
                    string.IsNullOrWhiteSpace(VariantSetName) ||
                    AvailableVariantNames is null ||
                    (TokenValue is not null &&
                     !AvailableVariantNames.Contains(TokenValue, StringComparer.Ordinal)))
                {
                    throw new InvalidOperationException(
                        "Variant selection requires a variant-set name and either an available " +
                        "variant name or the explicit no-selection value.");
                }
                break;
            case ViewerPrimCommand.ClearAttributeValue:
            case ViewerPrimCommand.BlockAttributeValue:
                if (BooleanValue is not null ||
                    TokenValue is not null ||
                    VariantSetName is not null ||
                    AvailableVariantNames is not null ||
                    string.IsNullOrWhiteSpace(AttributeName))
                {
                    throw new InvalidOperationException(
                        $"{Command} requires one attribute name.");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Command));
        }
    }
}
