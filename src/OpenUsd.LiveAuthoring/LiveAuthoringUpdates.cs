// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.LiveAuthoring;

/// <summary>Base type for data-only stage updates.</summary>
public abstract record LiveStageUpdate
{
    private protected LiveStageUpdate()
    {
    }

    /// <summary>Gets the renderer invalidation required by this update.</summary>
    public abstract UsdStageInvalidationKind Invalidation { get; }
}

/// <summary>Defines or redefines a prim.</summary>
public sealed record DefinePrimUpdate(string PrimPath, string? TypeName = null) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Topology;
}

/// <summary>Removes a prim and its descendants.</summary>
public sealed record RemovePrimUpdate(string PrimPath) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Topology;
}

/// <summary>Authors a scalar, vector, matrix, or array default value or time sample.</summary>
public sealed record SetAttributeUpdate(
    string PrimPath,
    string AttributeName,
    LiveAttributeValue Value,
    double? TimeCode = null) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Property;
}

/// <summary>Identifies what an explicit <see cref="ClearUpdate"/> removes.</summary>
public enum LiveClearTargetKind
{
    /// <summary>Clears an authored attribute default and time samples.</summary>
    AttributeValue,

    /// <summary>Clears all authored relationship targets.</summary>
    RelationshipTargets,

    /// <summary>Clears all authored references.</summary>
    References,

    /// <summary>Clears all authored payloads.</summary>
    Payloads,

    /// <summary>Clears one authored metadata field.</summary>
    Metadata
}

/// <summary>
/// Explicitly clears or removes authored opinions. This is distinct from authoring a new, empty
/// value: a clear reverts to the next weaker opinion instead of authoring an explicit replacement.
/// </summary>
public sealed record ClearUpdate(
    string PrimPath,
    LiveClearTargetKind TargetKind,
    string? Name = null) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => TargetKind switch
    {
        LiveClearTargetKind.RelationshipTargets => UsdStageInvalidationKind.Topology,
        LiveClearTargetKind.References or LiveClearTargetKind.Payloads =>
            UsdStageInvalidationKind.Composition,
        _ => UsdStageInvalidationKind.Property
    };
}

/// <summary>Creates a relationship and replaces its targets.</summary>
public sealed record SetRelationshipTargetsUpdate(
    string PrimPath,
    string RelationshipName,
    IReadOnlyList<string> Targets) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Topology;
}

/// <summary>Replaces authored references with one asset reference.</summary>
public sealed record SetReferenceUpdate(
    string PrimPath,
    string? AssetPath,
    string? TargetPrimPath = null) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Composition;
}

/// <summary>Replaces authored payloads with one asset payload.</summary>
public sealed record SetPayloadUpdate(
    string PrimPath,
    string? AssetPath,
    string? TargetPrimPath = null) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Composition;
}

/// <summary>Authors prim active state.</summary>
public sealed record SetActiveUpdate(string PrimPath, bool Active) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Topology;
}

/// <summary>Authors prim instanceability.</summary>
public sealed record SetInstanceableUpdate(string PrimPath, bool Instanceable) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Composition;
}

/// <summary>Authors a known variant set and its selection. A null selection clears the selection.</summary>
public sealed record SetVariantSelectionUpdate(
    string PrimPath,
    string VariantSetName,
    IReadOnlyList<string> KnownVariants,
    string? Selection) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Composition;
}

/// <summary>Authors one prim-metadata field.</summary>
public sealed record SetMetadataUpdate(
    string PrimPath,
    string Key,
    LiveMetadataValue Value) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Property;
}

/// <summary>Identifies the direction of an <see cref="ApiSchemaUpdate"/>.</summary>
public enum LiveApiSchemaOperation
{
    /// <summary>Applies the named single-apply API schema.</summary>
    Apply,

    /// <summary>Removes the named single-apply API schema.</summary>
    Remove
}

/// <summary>
/// Applies or removes a single-apply API schema identified by its bare schema token (for example
/// <c>"AssetPreviewsAPI"</c>). Only a bounded, curated registry of schema tokens with an existing typed
/// OpenUSD apply API is supported; <see cref="UsdStageBatchExecutor"/> rejects unknown tokens and API
/// schema removal explicitly rather than silently no-op-ing, because the underlying OpenUSD typed
/// surface does not yet expose generic removal.
/// </summary>
public sealed record ApiSchemaUpdate(
    string PrimPath,
    string SchemaToken,
    LiveApiSchemaOperation Operation) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Composition;
}

/// <summary>
/// Authors the quaternion orientation array of a <c>UsdGeomPointInstancer</c> prim, reusing the
/// existing typed <see cref="OpenUsd.Geom.UsdGeomPointInstancer"/> API. Positions, velocities, and
/// scales for the same prim are ordinary Vec3f-array attributes and use <see cref="SetAttributeUpdate"/>
/// with attribute names <c>"positions"</c>, <c>"velocities"</c>, and <c>"scales"</c>.
/// </summary>
public sealed record SetPointInstancerOrientationsUpdate(
    string PrimPath,
    IReadOnlyList<UsdQuatf> Orientations) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Property;
}

/// <summary>
/// Replaces the entire bridge-owned overlay rooted at <see cref="BridgeRootPath"/> with
/// <see cref="Updates"/> in one scheduler edit.
/// </summary>
/// <remarks>
/// <para>
/// This is the only update that removes and re-authors a whole subtree, and it exists so a full
/// snapshot can replace the bridge overlay without a per-batch checkpoint. The executor removes the
/// bridge root's opinions from the current edit-target layer, re-defines the root anchor, and applies
/// every nested update in order. It never touches opinions outside <see cref="BridgeRootPath"/>, so a
/// user-edit or physics overlay elsewhere in the same session layer stack is preserved.
/// </para>
/// <para>
/// <see cref="BridgeRootPath"/> must be reserved for the bridge: every nested update must target the
/// root itself or a descendant of it, nesting another <see cref="ReplaceBridgeOverlayUpdate"/> is
/// rejected, and a batch containing this update may contain no other update.
/// </para>
/// <para>
/// Replacement is atomic in the scheduler sense: it runs inside one serialized scheduler edit, so no
/// other stage operation observes an intermediate state. If a nested update fails partway through, the
/// executor removes the bridge root again before rethrowing, so the overlay is left empty rather than
/// half-applied.
/// </para>
/// </remarks>
public sealed record ReplaceBridgeOverlayUpdate(
    string BridgeRootPath,
    IReadOnlyList<LiveStageUpdate> Updates) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Composition;
}

internal static class LiveStageUpdatePaths
{
    /// <summary>
    /// Returns the prim path an update targets, or the bridge root for an overlay replacement. Every
    /// closed update case is covered, so the default arm is unreachable for supported updates.
    /// </summary>
    internal static string GetPrimPath(LiveStageUpdate update) =>
        update switch
        {
            DefinePrimUpdate define => define.PrimPath,
            RemovePrimUpdate remove => remove.PrimPath,
            SetAttributeUpdate attribute => attribute.PrimPath,
            ClearUpdate clear => clear.PrimPath,
            SetRelationshipTargetsUpdate relationship => relationship.PrimPath,
            SetReferenceUpdate reference => reference.PrimPath,
            SetPayloadUpdate payload => payload.PrimPath,
            SetActiveUpdate active => active.PrimPath,
            SetInstanceableUpdate instanceable => instanceable.PrimPath,
            SetVariantSelectionUpdate variant => variant.PrimPath,
            SetMetadataUpdate metadata => metadata.PrimPath,
            ApiSchemaUpdate apiSchema => apiSchema.PrimPath,
            SetPointInstancerOrientationsUpdate orientations => orientations.PrimPath,
            ReplaceBridgeOverlayUpdate replace => replace.BridgeRootPath,
            _ => throw new NotSupportedException(
                $"The live update type '{update.GetType().FullName}' is not supported.")
        };

    /// <summary>
    /// Returns whether <paramref name="primPath"/> is the bridge root itself or a descendant of it.
    /// Both paths are compared ordinally; a prefix match that is not followed by a path separator (for
    /// example <c>/BridgeOther</c> against root <c>/Bridge</c>) is deliberately not a descendant.
    /// </summary>
    internal static bool IsWithin(string bridgeRootPath, string primPath) =>
        string.Equals(primPath, bridgeRootPath, StringComparison.Ordinal) ||
        (primPath.Length > bridgeRootPath.Length &&
            primPath.StartsWith(bridgeRootPath, StringComparison.Ordinal) &&
            primPath[bridgeRootPath.Length] == '/');
}

internal static class LiveStageUpdateSnapshot
{
    internal static LiveStageUpdate Snapshot(LiveStageUpdate update) =>
        update switch
        {
            ReplaceBridgeOverlayUpdate replace => replace with
            {
                Updates = Array.AsReadOnly(SnapshotNested(replace.Updates))
            },
            SetRelationshipTargetsUpdate relationship => relationship with
            {
                Targets = Array.AsReadOnly(
                    CopyBounded(relationship.Targets, nameof(relationship.Targets)))
            },
            SetVariantSelectionUpdate variant => variant with
            {
                KnownVariants = Array.AsReadOnly(
                    CopyBounded(variant.KnownVariants, nameof(variant.KnownVariants)))
            },
            SetPointInstancerOrientationsUpdate orientations => orientations with
            {
                Orientations = Array.AsReadOnly(
                    CopyBounded(orientations.Orientations, nameof(orientations.Orientations)))
            },
            _ => update
        };

    /// <summary>
    /// Deep-copies a nested overlay-replacement list so a caller cannot mutate the list, or any
    /// collection inside it, after construction. The count bound is checked before allocating.
    /// </summary>
    private static LiveStageUpdate[] SnapshotNested(IReadOnlyList<LiveStageUpdate>? updates)
    {
        if (updates is null)
        {
            throw new ArgumentException("The collection cannot be null.", nameof(updates));
        }
        if (updates.Count > LiveAuthoringValidation.MaxBridgeOverlayUpdates)
        {
            throw new ArgumentException(
                "A bridge overlay replacement cannot contain more than " +
                $"{LiveAuthoringValidation.MaxBridgeOverlayUpdates} updates.",
                nameof(updates));
        }

        var copy = new LiveStageUpdate[updates.Count];
        for (int index = 0; index < updates.Count; index++)
        {
            LiveStageUpdate? nested = updates[index];
            if (nested is null)
            {
                throw new ArgumentException(
                    "A bridge overlay replacement cannot contain null updates.",
                    nameof(updates));
            }
            if (nested is ReplaceBridgeOverlayUpdate)
            {
                throw new ArgumentException(
                    "A bridge overlay replacement cannot nest another overlay replacement.",
                    nameof(updates));
            }
            copy[index] = Snapshot(nested);
        }
        return copy;
    }

    /// <summary>
    /// Copies a collection while enforcing <see cref="LiveAuthoringValidation.MaxCollectionElementCount"/>
    /// before allocating the copy, so an oversized input fails immediately instead of after a large
    /// allocation. Batch construction already rejects an oversized collection before this snapshot step
    /// runs; this is a defense-in-depth bound for any other caller of this internal helper.
    /// </summary>
    private static T[] CopyBounded<T>(IReadOnlyList<T>? values, string parameterName)
    {
        if (values is null)
        {
            throw new ArgumentException("The collection cannot be null.", parameterName);
        }
        if (values.Count > LiveAuthoringValidation.MaxCollectionElementCount)
        {
            throw new ArgumentException(
                $"The collection cannot exceed {LiveAuthoringValidation.MaxCollectionElementCount} " +
                "elements.",
                parameterName);
        }

        var copy = new T[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            copy[index] = values[index];
        }
        return copy;
    }
}
