// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal sealed record ViewerAuthoredEditStep : IViewerHistoryStep<ViewerAuthoredEditStep>
{
    internal ViewerAuthoredEditStep(
        string description, string layerIdentifier, UsdLayerAuthoredSnapshot before, UsdLayerAuthoredSnapshot after)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerIdentifier);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.Identity != after.Identity || !before.Addresses.SequenceEqual(after.Addresses))
        {
            throw new ArgumentException("An authored step must describe the same target and ordered addresses.");
        }
        Description = description;
        LayerIdentifier = layerIdentifier;
        Before = before;
        After = after;
        RetainedBytes = checked(
            SnapshotStorage(before) + SnapshotStorage(after) +
            ((description.Length + (long)layerIdentifier.Length) * 2) + 256);
    }

    public string Description { get; }

    internal string LayerIdentifier { get; }

    internal UsdLayerAuthoredSnapshot Before { get; }

    internal UsdLayerAuthoredSnapshot After { get; }

    public int ChangeCount => Before.Addresses.Count;

    public long RetainedBytes { get; }

    private static long SnapshotStorage(UsdLayerAuthoredSnapshot snapshot) =>
        // The packet, copied value packets and UTF-16 declarations fit within 3x packet bytes.
        // Per-opinion padding covers DTOs, string/array headers and address/collection storage.
        checked((3L * snapshot.ByteLength) + (512L * snapshot.Opinions.Count) + 256);

    internal static bool HasSameAffectedState(UsdLayerAuthoredSnapshot before, UsdLayerAuthoredSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        // A disjoint revision is not a change to these opinions; native CAS remains the conflict authority.
        if (before.Identity != after.Identity || before.Opinions.Count != after.Opinions.Count)
        {
            return false;
        }
        for (int index = 0; index < before.Opinions.Count; index++)
        {
            UsdLayerAuthoredOpinion left = before.Opinions[index];
            UsdLayerAuthoredOpinion right = after.Opinions[index];
            if (left.Address != right.Address || left.PropertyKind != right.PropertyKind ||
                !string.Equals(left.TypeName, right.TypeName, StringComparison.Ordinal) ||
                left.Variability != right.Variability || left.Custom != right.Custom ||
                !left.Value.Equals(right.Value))
            {
                return false;
            }
        }
        return true;
    }

    public ViewerAuthoredEditStep Reversed() => new(Description, LayerIdentifier, After, Before);

    public bool TryCoalesce(
        ViewerAuthoredEditStep next, [NotNullWhen(true)] out ViewerAuthoredEditStep? merged)
    {
        ArgumentNullException.ThrowIfNull(next);
        // Unrelated revisions split a gesture; only native CAS decides affected-state equivalence.
        if (LayerIdentifier == next.LayerIdentifier && After.HasSamePayload(next.Before))
        {
            merged = new ViewerAuthoredEditStep(Description, LayerIdentifier, Before, next.After);
            return true;
        }
        merged = null;
        return false;
    }
}
