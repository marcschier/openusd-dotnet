// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal sealed class ViewerDocumentObservation : IUsdDetachedResult
{
    internal ViewerDocumentObservation(
        ViewerDocumentState state, IReadOnlyList<UsdLayerEditingState> layers,
        IReadOnlyList<string> otherChangedLayers)
    {
        State = state;
        Layers = Array.AsReadOnly(layers.ToArray());
        OtherChangedLayers = Array.AsReadOnly(otherChangedLayers.ToArray());
    }

    internal ViewerDocumentState State { get; }

    internal IReadOnlyList<UsdLayerEditingState> Layers { get; }

    internal IReadOnlyList<string> OtherChangedLayers { get; }

    internal bool HasChanges =>
        State.Source.HasChanges || State.Review?.HasChanges == true || State.HasOtherLayerChanges;

    internal bool Matches(ViewerDocumentObservation current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (State != current.State || Layers.Count != current.Layers.Count ||
            !OtherChangedLayers.SequenceEqual(current.OtherChangedLayers))
        {
            return false;
        }
        for (int index = 0; index < Layers.Count; index++)
        {
            UsdLayerEditingState expected = Layers[index];
            UsdLayerEditingState actual = current.Layers[index];
            if (expected.Identity != actual.Identity || expected.Role != actual.Role ||
                expected.Identifier != actual.Identifier || expected.PermissionToEdit != actual.PermissionToEdit ||
                expected.PermissionToSave != actual.PermissionToSave ||
                (expected.Role != UsdLayerRole.Physics && expected.Revision != actual.Revision))
            {
                return false;
            }
        }
        return true;
    }
}
