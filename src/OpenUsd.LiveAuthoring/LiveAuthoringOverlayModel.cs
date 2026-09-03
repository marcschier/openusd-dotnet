// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.LiveAuthoring;

/// <summary>Identifies which authored opinion an overlay slot holds.</summary>
internal enum LiveOverlaySlotKind
{
    Prim = 0,
    Active = 1,
    Instanceable = 2,
    Reference = 3,
    Payload = 4,
    Variant = 5,
    ApiSchema = 6,
    Metadata = 7,
    Attribute = 8,
    Relationship = 9,
    Orientations = 10
}

/// <summary>
/// The coordinator's in-memory model of the bridge-owned overlay: exactly the authored opinions the
/// bridge itself produced, and nothing else on the stage.
/// </summary>
/// <remarks>
/// <para>
/// The model exists so a full snapshot can be exported deterministically without reading the stage
/// back. Reading back would report composed values contributed by the root layer, a user-edit layer,
/// or a physics overlay, which the bridge does not own and must never claim in a snapshot.
/// </para>
/// <para>
/// Each authored opinion occupies one slot keyed by prim path, kind, name, and time code, so replaying
/// the same update twice is idempotent and a later update supersedes an earlier one for the same slot
/// rather than accumulating. Removing a prim drops every slot in its subtree, and a clear drops exactly
/// the slots that clear would revert, which keeps the model equal to what the stage would hold after a
/// full replacement.
/// </para>
/// <para>
/// The model enforces <see cref="LiveAuthoringValidation.MaxBridgeOverlayUpdates"/>,
/// <see cref="LiveAuthoringValidation.MaxBridgeOverlayPayloadBytes"/>, and
/// <see cref="LiveAuthoringValidation.MaxTotalCollectionElementCountPerBatch"/> incrementally with the
/// same accounting the batch validator uses, so an export is always constructible as a valid
/// <see cref="LiveAuthoringSnapshot"/>.
/// </para>
/// </remarks>
internal sealed class LiveAuthoringOverlayModel
{
    private readonly Dictionary<SlotKey, SlotValue> _slots;
    private long _elementCount;
    private long _estimatedBytes;

    internal LiveAuthoringOverlayModel(string bridgeRootPath)
    {
        BridgeRootPath = bridgeRootPath;
        _slots = [];
    }

    private LiveAuthoringOverlayModel(LiveAuthoringOverlayModel source)
    {
        BridgeRootPath = source.BridgeRootPath;
        _slots = new Dictionary<SlotKey, SlotValue>(source._slots);
        _elementCount = source._elementCount;
        _estimatedBytes = source._estimatedBytes;
    }

    internal string BridgeRootPath { get; }

    internal int UpdateCount => _slots.Count;

    internal int PrimCount
    {
        get
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (SlotKey key in _slots.Keys)
            {
                paths.Add(key.PrimPath);
            }
            return paths.Count;
        }
    }

    /// <summary>Creates an independent copy so a candidate apply can be discarded on failure.</summary>
    internal LiveAuthoringOverlayModel Clone() => new(this);

    /// <summary>
    /// Folds one update into the model. Throws a bounded <see cref="ArgumentException"/> when the update
    /// would push the overlay past its bounds, which the coordinator surfaces as
    /// <see cref="LiveAuthoringSessionRejection.OverlayBudget"/>.
    /// </summary>
    internal void Apply(LiveStageUpdate update)
    {
        switch (update)
        {
            case RemovePrimUpdate remove:
                RemoveSubtree(remove.PrimPath);
                return;
            case ClearUpdate clear:
                ApplyClear(clear);
                return;
            case ApiSchemaUpdate { Operation: LiveApiSchemaOperation.Remove } apiSchema:
                RemoveSlot(new SlotKey(
                    apiSchema.PrimPath,
                    LiveOverlaySlotKind.ApiSchema,
                    apiSchema.SchemaToken,
                    null));
                return;
            case ReplaceBridgeOverlayUpdate:
                throw new ArgumentException(
                    "A bridge overlay replacement is applied by replacing the whole model, not by " +
                    "folding it into an existing one.",
                    nameof(update));
            default:
                SetSlot(GetSlotKey(update), update);
                return;
        }
    }

    /// <summary>Returns the overlay as a canonical, ordered update list safe to reapply from scratch.</summary>
    /// <remarks>
    /// Prim paths are ordered ordinally, which guarantees a parent precedes every descendant because a
    /// descendant path starts with the parent path. Within one prim, existence and composition
    /// opinions precede value opinions, so re-applying the export against an empty stage reproduces the
    /// overlay exactly.
    /// </remarks>
    internal LiveStageUpdate[] Export()
    {
        return [.. _slots
            .OrderBy(static slot => slot.Key.PrimPath, StringComparer.Ordinal)
            .ThenBy(static slot => (int)slot.Key.Kind)
            .ThenBy(static slot => slot.Key.Name, StringComparer.Ordinal)
            .ThenBy(static slot => slot.Key.TimeCode ?? double.NegativeInfinity)
            .Select(static slot => slot.Value.Update)];
    }

    private void ApplyClear(ClearUpdate clear)
    {
        switch (clear.TargetKind)
        {
            case LiveClearTargetKind.AttributeValue:
                RemoveAttributeSlots(clear.PrimPath, clear.Name!);
                return;
            case LiveClearTargetKind.RelationshipTargets:
                RemoveSlot(new SlotKey(
                    clear.PrimPath,
                    LiveOverlaySlotKind.Relationship,
                    clear.Name,
                    null));
                return;
            case LiveClearTargetKind.References:
                RemoveSlot(new SlotKey(clear.PrimPath, LiveOverlaySlotKind.Reference, null, null));
                return;
            case LiveClearTargetKind.Payloads:
                RemoveSlot(new SlotKey(clear.PrimPath, LiveOverlaySlotKind.Payload, null, null));
                return;
            case LiveClearTargetKind.Metadata:
                RemoveSlot(new SlotKey(
                    clear.PrimPath,
                    LiveOverlaySlotKind.Metadata,
                    clear.Name,
                    null));
                return;
            default:
                throw new NotSupportedException(
                    $"The clear target kind '{clear.TargetKind}' is not supported.");
        }
    }

    private void RemoveAttributeSlots(string primPath, string attributeName)
    {
        SlotKey[] matches = [.. _slots.Keys.Where(key =>
            key.Kind == LiveOverlaySlotKind.Attribute &&
            string.Equals(key.PrimPath, primPath, StringComparison.Ordinal) &&
            string.Equals(key.Name, attributeName, StringComparison.Ordinal))];
        foreach (SlotKey key in matches)
        {
            RemoveSlot(key);
        }
    }

    private void RemoveSubtree(string primPath)
    {
        SlotKey[] matches = [.. _slots.Keys.Where(
            key => LiveStageUpdatePaths.IsWithin(primPath, key.PrimPath))];
        foreach (SlotKey key in matches)
        {
            RemoveSlot(key);
        }
    }

    private void RemoveSlot(SlotKey key)
    {
        if (_slots.Remove(key, out SlotValue existing))
        {
            _elementCount -= existing.Elements;
            _estimatedBytes -= existing.Bytes;
        }
    }

    private void SetSlot(SlotKey key, LiveStageUpdate update)
    {
        (long elements, long bytes) = LiveAuthoringValidation.Measure(update, nameof(update));
        long nextElements = _elementCount + elements;
        long nextBytes = _estimatedBytes + bytes;
        int nextCount = _slots.Count;
        if (_slots.TryGetValue(key, out SlotValue existing))
        {
            nextElements -= existing.Elements;
            nextBytes -= existing.Bytes;
        }
        else
        {
            nextCount++;
        }

        if (nextCount > LiveAuthoringValidation.MaxBridgeOverlayUpdates)
        {
            throw new ArgumentException(
                "The bridge overlay cannot hold more than " +
                $"{LiveAuthoringValidation.MaxBridgeOverlayUpdates} authored opinions.",
                nameof(update));
        }
        if (nextElements > LiveAuthoringValidation.MaxTotalCollectionElementCountPerBatch)
        {
            throw new ArgumentException(
                "The bridge overlay's combined relationship/variant/array/orientation element count " +
                $"exceeds {LiveAuthoringValidation.MaxTotalCollectionElementCountPerBatch}.",
                nameof(update));
        }
        if (nextBytes > LiveAuthoringValidation.MaxBridgeOverlayPayloadBytes)
        {
            throw new ArgumentException(
                "The bridge overlay's estimated retained payload (text and numeric array bytes) " +
                $"exceeds {LiveAuthoringValidation.MaxBridgeOverlayPayloadBytes} bytes.",
                nameof(update));
        }

        _slots[key] = new SlotValue(update, elements, bytes);
        _elementCount = nextElements;
        _estimatedBytes = nextBytes;
    }

    private static SlotKey GetSlotKey(LiveStageUpdate update) =>
        update switch
        {
            DefinePrimUpdate define =>
                new SlotKey(define.PrimPath, LiveOverlaySlotKind.Prim, null, null),
            SetActiveUpdate active =>
                new SlotKey(active.PrimPath, LiveOverlaySlotKind.Active, null, null),
            SetInstanceableUpdate instanceable =>
                new SlotKey(instanceable.PrimPath, LiveOverlaySlotKind.Instanceable, null, null),
            SetReferenceUpdate reference =>
                new SlotKey(reference.PrimPath, LiveOverlaySlotKind.Reference, null, null),
            SetPayloadUpdate payload =>
                new SlotKey(payload.PrimPath, LiveOverlaySlotKind.Payload, null, null),
            SetVariantSelectionUpdate variant => new SlotKey(
                variant.PrimPath,
                LiveOverlaySlotKind.Variant,
                variant.VariantSetName,
                null),
            ApiSchemaUpdate apiSchema => new SlotKey(
                apiSchema.PrimPath,
                LiveOverlaySlotKind.ApiSchema,
                apiSchema.SchemaToken,
                null),
            SetMetadataUpdate metadata =>
                new SlotKey(metadata.PrimPath, LiveOverlaySlotKind.Metadata, metadata.Key, null),
            SetAttributeUpdate attribute => new SlotKey(
                attribute.PrimPath,
                LiveOverlaySlotKind.Attribute,
                attribute.AttributeName,
                attribute.TimeCode),
            SetRelationshipTargetsUpdate relationship => new SlotKey(
                relationship.PrimPath,
                LiveOverlaySlotKind.Relationship,
                relationship.RelationshipName,
                null),
            SetPointInstancerOrientationsUpdate orientations => new SlotKey(
                orientations.PrimPath,
                LiveOverlaySlotKind.Orientations,
                null,
                null),
            _ => throw new NotSupportedException(
                $"The live update type '{update.GetType().FullName}' is not supported.")
        };

    private readonly record struct SlotKey(
        string PrimPath,
        LiveOverlaySlotKind Kind,
        string? Name,
        double? TimeCode);

    private readonly record struct SlotValue(LiveStageUpdate Update, long Elements, long Bytes);
}
