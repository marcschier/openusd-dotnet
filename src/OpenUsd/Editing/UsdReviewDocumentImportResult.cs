// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Editing;

/// <summary>An immutable result of verified whole-review installation into a pristine session.</summary>
public sealed class UsdReviewDocumentImportResult : IUsdDetachedResult
{
    internal UsdReviewDocumentImportResult(
        UsdLayerEditOutcome outcome, UsdLayerEditingState? afterState, string? diagnostic)
    {
        Outcome = outcome;
        AfterState = afterState;
        Diagnostic = diagnostic;
    }

    /// <summary>Gets the native comparison outcome; native validation and unsupported-domain errors throw.</summary>
    public UsdLayerEditOutcome Outcome { get; }
    /// <summary>Gets the detached new history target state on Applied, otherwise null.</summary>
    public UsdLayerEditingState? AfterState { get; }
    /// <summary>Gets a bounded native conflict or restriction diagnostic, when available.</summary>
    public string? Diagnostic { get; }
}
