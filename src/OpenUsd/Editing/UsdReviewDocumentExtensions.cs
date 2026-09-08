// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Interop;

namespace OpenUsd.Editing;

/// <summary>Native-verified portable review capture and pristine-session import.</summary>
/// <remarks>
/// Use stage and layer handles within their owning scheduler callback. Results are deeply detached.
/// These calls do not publish files, save source roots, import physics opinions or reconcile conflicts.
/// </remarks>
public static class UsdReviewDocumentExtensions
{
    /// <summary>Captures the source origin established by <see cref="UsdStage.OpenForReview"/>.</summary>
    /// <remarks>Legacy, dirty or unverified cached stages are not silently reopened or rebound.</remarks>
    public static UsdReviewSourceBinding CaptureReviewSourceBinding(this UsdStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return UsdReviewDocumentCodec.DecodeSourceBinding(
            OpenUsdNativeRuntime.CaptureReviewSourceBinding(stage.Native), stage.RootLayerIdentifier);
    }

    /// <summary>Captures portable review bytes and a private same-process save receipt without publication.</summary>
    /// <remarks>
    /// Only the owned user-review layer is admitted. Native verification retains exact typed opinions
    /// and verifies source and asset anchors; a Save As path does not rebase authored asset paths.
    /// </remarks>
    public static UsdReviewDocument CaptureReviewDocument(
        this UsdLayer layer, UsdReviewSourceBinding source, string targetDocumentPath)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(source);
        UsdReviewDocumentCodec.ValidateTargetPath(targetDocumentPath);
        return UsdReviewDocumentCodec.DecodeCapturedDocument(
            OpenUsdNativeRuntime.CaptureReviewDocument(layer.Native, source.Payload, targetDocumentPath),
            source, targetDocumentPath);
    }

    /// <summary>Installs an admitted review into a verified source stage with a pristine session.</summary>
    /// <remarks>
    /// Native import verifies source, dependency and anchor identities before any installation, returns a
    /// new history target, and rolls back owned changes on failure. It never patches old checkpoint IDs.
    /// Reading or importing does not create a receipt capable of acknowledging publication.
    /// </remarks>
    public static UsdReviewDocumentImportResult ImportReviewDocument(
        this UsdStage stage, UsdReviewDocument document, UsdReviewSourceBinding source)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);
        return UsdReviewDocumentCodec.DecodeImportResult(
            OpenUsdNativeRuntime.ImportReviewDocument(stage.Native, document.Payload, source.Payload), source);
    }

    /// <summary>Conditionally acknowledges caller publication using a private same-process capture receipt.</summary>
    /// <remarks>
    /// Returns false for a document produced by <see cref="UsdReviewDocument.Read"/>. Native acknowledgement
    /// additionally requires the captured target, revision, exact content and verified source to be unchanged.
    /// Source verification failures throw a native error without clearing logical dirty state.
    /// The caller must publish the bytes successfully before invoking this method.
    /// </remarks>
    public static bool AcknowledgeSaved(this UsdLayer layer, UsdReviewDocument captured)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(captured);
        return captured.AcknowledgeSaved(layer.Native);
    }
}
