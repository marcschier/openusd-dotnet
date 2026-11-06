// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;
using OpenUsd.Shade;

namespace OpenUsd.Viewer;

internal sealed partial class ViewerAuthoredEditController
{
    internal static bool IsDirectMaterialBinding(string name) =>
        name is "material:binding" or "material:binding:preview" or "material:binding:full";

    internal async Task<ViewerAuthoredEditCapture> CaptureMaterialBindingAsync(
        string primPath, string relationshipName, CancellationToken cancellationToken = default)
    {
        var address = new UsdLayerEditAddress($"{primPath}.{relationshipName}", UsdLayerEditField.RelationshipTargets);
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
                _ = ReadDocument(stage);
                ValidateDirectMaterialBinding(stage, primPath, relationshipName);
                return CaptureReview(stage, [address]);
            }, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ValidateDirectMaterialBinding(UsdStage stage, string primPath, string relationshipName)
    {
        if (!IsDirectMaterialBinding(relationshipName) ||
            !stage.GetPrim(primPath).GetAppliedSchemas().Contains("MaterialBindingAPI", StringComparer.Ordinal) ||
            stage.GetPrimPropertySnapshot(primPath, limits: UsdPropertyInspectionLimits.Viewer).Properties
                .OfType<UsdRelationshipPropertySnapshot>()
                .FirstOrDefault(property => property.Name == relationshipName) is not { IsCustom: false })
        {
            throw new NotSupportedException(
                "An existing noncustom direct binding with MaterialBindingAPI is required. " +
                "Collection bindings and missing APIs remain read-only; no schema will be applied automatically.");
        }
    }

    private static void ValidateMaterialBindingChanges(UsdStage stage, IReadOnlyList<UsdLayerEdit> edits)
    {
        foreach (UsdLayerEdit edit in edits)
        {
            int separator = edit.Address.Path.LastIndexOf('.');
            string name = edit.Address.Path[(separator + 1)..];
            if (edit.Address.Field != UsdLayerEditField.RelationshipTargets || !IsDirectMaterialBinding(name))
            {
                continue;
            }
            ValidateDirectMaterialBinding(stage, edit.Address.Path[..separator], name);
            if (edit.Operation != UsdLayerEditOperation.Set)
            {
                continue;
            }
            UsdPathListEdit targets = edit.Value.AsPathList();
            if (!targets.IsExplicit || targets.ExplicitItems.Count > 1 ||
                (targets.ExplicitItems.Count == 1 &&
                    !UsdShadeMaterial.TryWrap(stage.GetPrim(targets.ExplicitItems[0]), out _)))
            {
                throw new NotSupportedException(
                    "Choose one existing native Material prim or an explicitly empty list to unbind.");
            }
        }
    }
}
