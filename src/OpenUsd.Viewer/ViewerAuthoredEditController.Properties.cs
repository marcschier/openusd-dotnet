// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal sealed record ViewerPropertyEditCapture(
    ViewerAuthoredEditCapture Capture, string TypeName, UsdLayerEditVariability Variability, bool Custom)
    : IUsdDetachedResult;

internal sealed partial class ViewerAuthoredEditController
{
    internal async Task<ViewerPropertyEditCapture> CapturePropertyAsync(
        string primPath, string attributeName, UsdLayerEditField field, double timeCode,
        CancellationToken cancellationToken = default)
    {
        if (field is not (UsdLayerEditField.Default or UsdLayerEditField.TimeSample))
        {
            throw new ArgumentException("The focused editor supports defaults and individual samples.", nameof(field));
        }
        var address = new UsdLayerEditAddress($"{primPath}.{attributeName}", field, timeCode);
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (IsSuspended)
            {
                throw new InvalidOperationException("Finish or cancel the document transition before editing.");
            }
            return await _scheduler.InvokeAsync(stage =>
            {
                ViewerDocumentObservation document = ReadDocument(stage);
                string composedType = stage.GetPrim(primPath).GetAttribute(attributeName).TypeName;
                if (!ViewerPropertyEditParser.Supports(composedType))
                {
                    throw new NotSupportedException("This declared type remains read-only in the focused editor.");
                }
                UsdLayerAuthoredOpinion? declaration = null;
                foreach (UsdLayerEditingState state in document.Layers)
                {
                    if (state.Role == UsdLayerRole.Physics)
                    {
                        continue;
                    }
                    using UsdLayer local = stage.GetLocalLayer(state.Identifier);
                    UsdLayerAuthoredOpinion opinion = local.CaptureAuthored([address]).Opinions[0];
                    if (opinion.PropertyKind == UsdLayerPropertyKind.Attribute && opinion.TypeName is { Length: > 0 })
                    {
                        declaration = opinion;
                        break;
                    }
                }
                if (declaration is null || declaration.TypeName != composedType)
                {
                    throw new NotSupportedException(
                        "An exact local attribute declaration is required. Referenced/schema-only attributes are " +
                        "read-only here; no declaration will be guessed.");
                }
                UsdLayerEditVariability variability = declaration.Variability ?? UsdLayerEditVariability.Varying;
                if (field == UsdLayerEditField.TimeSample && variability == UsdLayerEditVariability.Uniform)
                {
                    throw new NotSupportedException("Uniform attributes cannot be edited as time samples.");
                }
                ViewerAuthoredEditCapture capture = CaptureReview(stage, [address]);
                return new ViewerPropertyEditCapture(capture, composedType, variability, declaration.Custom ?? false);
            }, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
