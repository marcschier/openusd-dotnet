// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Viewer;

internal enum ViewerDocumentLayerRole
{
    Review,
    Source,
    Simulation
}

internal enum ViewerDocumentSaveAction
{
    SaveReview,
    SaveAs,
    SaveSource
}

internal sealed record ViewerDocumentLayerState(
    string Identifier,
    ViewerDocumentLayerRole Role,
    string? FilePath,
    ulong Revision,
    bool HasChanges,
    bool IsWritable);

internal sealed record ViewerDocumentState(
    Guid DocumentId,
    ViewerDocumentLayerState Source,
    ViewerDocumentLayerState? Review,
    string EditTargetIdentifier,
    bool SourceEditOptIn = false,
    bool IsBusy = false,
    bool ReviewAbsenceConfirmed = false,
    bool HasOtherLayerChanges = false);

internal sealed record ViewerDocumentSaveDecision(
    bool IsAllowed,
    ViewerDocumentLayerRole Role,
    string? LayerIdentifier,
    string? DestinationPath,
    bool RequiresDestination,
    bool RequiresExternalChangeCheck,
    bool RequiresAssetAnchorValidation,
    string Message);

internal static class ViewerDocumentSavePolicy
{
    internal static ViewerDocumentSaveDecision Plan(
        ViewerDocumentState? document, ViewerDocumentSaveAction action)
    {
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }
        ViewerDocumentLayerRole role = action == ViewerDocumentSaveAction.SaveSource
            ? ViewerDocumentLayerRole.Source : ViewerDocumentLayerRole.Review;
        if (document is null)
        {
            return Refuse(role, "Open a document before saving a layer.");
        }
        if (document.IsBusy)
        {
            return Refuse(role, "Wait for the current document operation or cancel it before saving.");
        }
        if (role == ViewerDocumentLayerRole.Source && !document.SourceEditOptIn)
        {
            return Refuse(role, "Source-layer saving requires explicit source-edit opt-in.");
        }
        ViewerDocumentLayerState? target = role == ViewerDocumentLayerRole.Source
            ? document.Source : document.Review;
        if (target is null || target.Role != role || string.IsNullOrWhiteSpace(target.Identifier))
        {
            return Refuse(role, "The exact requested layer is unavailable; another layer will not be substituted.");
        }
        if (!target.IsWritable)
        {
            return Refuse(role, "The requested layer is read-only.");
        }
        if (target.FilePath is { } path && !Path.IsPathFullyQualified(path))
        {
            return Refuse(role, "The saved layer path must be resolved before publication.");
        }
        if (role == ViewerDocumentLayerRole.Source && target.FilePath is null)
        {
            return Refuse(role, "An anonymous source has no source file to save. Save a review document instead.");
        }
        bool chooseDestination = target.FilePath is null || action == ViewerDocumentSaveAction.SaveAs;
        if (role == ViewerDocumentLayerRole.Review)
        {
            if (target.Identifier == document.Source.Identifier)
            {
                return Refuse(role, "The source layer cannot be published as a review layer.");
            }
            if (!chooseDestination &&
                target.FilePath is { } destination &&
                document.Source.FilePath is { } sourcePath)
            {
                if (!Path.IsPathFullyQualified(sourcePath))
                {
                    return Refuse(role, "Resolve the source path before choosing a separate review destination.");
                }
                StringComparison comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (string.Equals(
                    Path.GetFullPath(destination),
                    Path.GetFullPath(sourcePath),
                    comparison))
                {
                    return Refuse(
                        role, "A review save cannot overwrite the source file. Choose a separate destination.");
                }
            }
        }
        return new ViewerDocumentSaveDecision(
            true, role, target.Identifier, chooseDestination ? null : target.FilePath,
            chooseDestination, true, chooseDestination,
            chooseDestination
                ? "Choose a review destination, then validate composition and asset anchors before publication."
                : "Capture the exact target layer and compare its file identity before publication.");
    }

    private static ViewerDocumentSaveDecision Refuse(ViewerDocumentLayerRole role, string message) =>
        new(false, role, null, null, false, false, false, message);
}
