// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace OpenUsd.LiveAuthoring;

/// <summary>
/// Performs pure, bounded validation before a live-authoring batch can reach a scheduler. Every limit
/// below is a public constant so a caller can size its own producer buffers against the same numbers
/// this library enforces, rather than discovering them only from a thrown exception.
/// </summary>
public static class LiveAuthoringValidation
{
    /// <summary>The maximum length allowed for an opaque correlation or origin identifier.</summary>
    public const int MaxOpaqueIdLength = 256;

    /// <summary>The maximum number of updates allowed in one batch.</summary>
    public const int MaxUpdatesPerBatch = 4096;

    /// <summary>
    /// The maximum length allowed for a grammar-checked identifier: an attribute name segment,
    /// relationship name segment, variant set name, variant name, variant selection, API-schema token,
    /// or prim type name.
    /// </summary>
    public const int MaxIdentifierLength = 512;

    /// <summary>The maximum length allowed for an absolute prim path, including relationship targets.</summary>
    public const int MaxPathLength = 4096;

    /// <summary>
    /// The maximum length allowed for a free-form text value: a coalescing key, an asset path, a
    /// metadata key, a metadata string value, or a scalar/array string or token attribute value.
    /// </summary>
    public const int MaxTextValueLength = 8192;

    /// <summary>
    /// The maximum number of elements allowed in one bounded collection: relationship targets, known
    /// variants, one attribute array, or one point-instancer orientation array. Also enforced by
    /// <see cref="LiveAttributeValue"/>'s array factories and the point-instancer orientation snapshot
    /// before they copy their input, so an oversized collection fails before a second allocation.
    /// </summary>
    public const int MaxCollectionElementCount = 65536;

    /// <summary>
    /// The maximum combined element count across every relationship-target list, known-variant list,
    /// attribute array, and orientation array in one batch. This bounds the batch's aggregate element
    /// count even when no single collection exceeds <see cref="MaxCollectionElementCount"/>.
    /// </summary>
    public const long MaxTotalCollectionElementCountPerBatch = 4_194_304;

    /// <summary>
    /// The maximum combined estimated retained byte size across one batch: every text value counted as
    /// its UTF-8 byte length (identifiers, prim paths, asset paths, metadata keys/strings, scalar and
    /// array string/token values, and the batch's own coalescing key/correlation/origin identifiers),
    /// plus every numeric scalar and array value counted at its natural in-memory size (for example 8
    /// bytes per <c>double</c>, 12 bytes per <c>Vec3f</c>, 16 bytes per quaternion orientation).
    /// </summary>
    /// <remarks>
    /// <see cref="MaxTotalCollectionElementCountPerBatch"/> alone does not bound retained memory: up to
    /// 4,194,304 string or token elements at <see cref="MaxTextValueLength"/> characters each could
    /// retain tens of gigabytes even though no single count-based limit is exceeded. This byte budget
    /// closes that gap. 16 MiB is sized for a bounded, high-rate live-authoring burst — a handful of
    /// updates carrying moderate arrays — not a bulk scene dump; a producer streaming continuously
    /// should split a larger payload across several ordered batches instead of raising this bound.
    /// </remarks>
    public const long MaxEstimatedBatchPayloadBytes = 16 * 1024 * 1024;

    /// <summary>
    /// The maximum number of nested updates allowed inside one
    /// <see cref="ReplaceBridgeOverlayUpdate"/>, and therefore the maximum number of authored slots a
    /// bridge-owned overlay may hold. A snapshot is a bounded overlay handoff, not a whole-scene dump.
    /// </summary>
    public const int MaxBridgeOverlayUpdates = MaxUpdatesPerBatch;

    /// <summary>
    /// The maximum combined estimated retained byte size of one bridge-owned overlay, measured the same
    /// way as <see cref="MaxEstimatedBatchPayloadBytes"/>. It is deliberately smaller than the batch
    /// budget so an overlay that is exactly at its own limit still fits inside the replacement batch
    /// that carries it, together with that batch's coalescing key and correlation/origin identifiers.
    /// </summary>
    public const long MaxBridgeOverlayPayloadBytes = MaxEstimatedBatchPayloadBytes - (64 * 1024);

    /// <summary>
    /// The maximum number of remote sequences one session may retain in its replay ledger. The ledger
    /// is the evidence behind an idempotent duplicate acknowledgement, so it is bounded like every
    /// other retained structure in this package rather than growing with the session's lifetime.
    /// </summary>
    public const int MaxReplayWindowLength = 4096;

    /// <summary>
    /// The default number of remote sequences retained in the replay ledger. It is sized for an
    /// adapter that retransmits a short burst after a transport hiccup, not for an unbounded history.
    /// </summary>
    public const int DefaultReplayWindowLength = 64;

    /// <summary>
    /// The retained byte size of one replay ledger entry: one 64-bit sequence, one 32-byte content
    /// fingerprint, and the dictionary/queue overhead accounted at a fixed rate. An entry never holds
    /// the delta payload, so retention cost does not grow with message size.
    /// </summary>
    public const int ReplayLedgerEntryBytes = 64;

    /// <summary>
    /// The maximum retained byte size of one session's replay ledger. It bounds the ledger
    /// independently of <see cref="MaxReplayWindowLength"/>, so both a count and a byte ceiling apply.
    /// </summary>
    public const long MaxReplayLedgerBytes =
        (long)MaxReplayWindowLength * ReplayLedgerEntryBytes;

    /// <summary>Validates a constructed batch without native access or stage mutation.</summary>
    public static void Validate(LiveAuthoringBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        Validate(
            batch.Sequence,
            batch.Updates,
            batch.CoalescingKey,
            batch.CorrelationId,
            batch.OriginId,
            nameof(batch));
    }

    /// <summary>
    /// Measures one already-validated update's element count and estimated retained byte size using the
    /// same accounting the batch validator uses, so an overlay model can enforce
    /// <see cref="MaxBridgeOverlayPayloadBytes"/> and
    /// <see cref="MaxTotalCollectionElementCountPerBatch"/> incrementally without a second cost model.
    /// </summary>
    internal static (long Elements, long Bytes) Measure(LiveStageUpdate update, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(update);
        var accounting = new BatchAccounting();
        ValidateUpdate(update, parameterName, accounting);
        return (accounting.ElementCount, accounting.EstimatedBytes);
    }

    /// <summary>
    /// Validates a bridge-owned overlay update list against the bridge root scope and the overlay
    /// bounds. Returns the measured element count and estimated retained byte size.
    /// </summary>
    internal static (long Elements, long Bytes) ValidateBridgeOverlayUpdates(
        string bridgeRootPath,
        IReadOnlyList<LiveStageUpdate> updates,
        string parameterName)
    {
        ValidateBridgeRootPath(bridgeRootPath, $"{parameterName}.BridgeRootPath");
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count > MaxBridgeOverlayUpdates)
        {
            throw new ArgumentException(
                $"A bridge overlay cannot contain more than {MaxBridgeOverlayUpdates} updates.",
                parameterName);
        }

        var accounting = new BatchAccounting();
        for (int index = 0; index < updates.Count; index++)
        {
            LiveStageUpdate? update = updates[index];
            string updateName = $"{parameterName}[{index}]";
            if (update is null)
            {
                throw new ArgumentException(
                    "A bridge overlay cannot contain null updates.",
                    updateName);
            }
            if (update is ReplaceBridgeOverlayUpdate)
            {
                throw new ArgumentException(
                    "A bridge overlay replacement cannot nest another overlay replacement.",
                    updateName);
            }
            ValidateUpdate(update, updateName, accounting);
            ValidateBridgeScope(bridgeRootPath, update, updateName);
            ThrowIfOverlayExceeded(accounting, parameterName);
        }
        ThrowIfOverlayExceeded(accounting, parameterName);
        return (accounting.ElementCount, accounting.EstimatedBytes);
    }

    /// <summary>Validates that an update targets the bridge root itself or a descendant of it.</summary>
    internal static void ValidateBridgeScope(
        string bridgeRootPath,
        LiveStageUpdate update,
        string parameterName)
    {
        string primPath = LiveStageUpdatePaths.GetPrimPath(update);
        if (!LiveStageUpdatePaths.IsWithin(bridgeRootPath, primPath))
        {
            throw new ArgumentException(
                $"The update target '{primPath}' is outside the bridge-owned overlay rooted at " +
                $"'{bridgeRootPath}'. A bridge session may only author its own overlay.",
                parameterName);
        }
    }

    /// <summary>
    /// Validates a bridge root path: an absolute prim path that is not the pseudo-root, because the
    /// bridge overlay must be a removable, replaceable subtree rather than the whole stage.
    /// </summary>
    internal static void ValidateBridgeRootPath(string? bridgeRootPath, string parameterName)
    {
        ValidatePrimPath(bridgeRootPath, parameterName);
        if (bridgeRootPath == "/")
        {
            throw new ArgumentException(
                "A bridge overlay root cannot be the stage pseudo-root. Reserve a named prim path " +
                "such as '/Bridge' for the bridge-owned overlay.",
                parameterName);
        }
    }

    private static void ThrowIfOverlayExceeded(BatchAccounting accounting, string parameterName)
    {
        if (accounting.ElementCount > MaxTotalCollectionElementCountPerBatch)
        {
            throw new ArgumentException(
                "The bridge overlay's combined relationship/variant/array/orientation element count " +
                $"exceeds {MaxTotalCollectionElementCountPerBatch}.",
                parameterName);
        }
        if (accounting.EstimatedBytes > MaxBridgeOverlayPayloadBytes)
        {
            throw new ArgumentException(
                "The bridge overlay's estimated retained payload (text and numeric array bytes) " +
                $"exceeds {MaxBridgeOverlayPayloadBytes} bytes.",
                parameterName);
        }
    }

    internal static void Validate(
        long sequence,
        IReadOnlyList<LiveStageUpdate> updates,
        string? coalescingKey,
        string? correlationId,
        string? originId,
        string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0)
        {
            throw new ArgumentException(
                "A live-authoring batch must contain at least one update.",
                parameterName);
        }
        if (updates.Count > MaxUpdatesPerBatch)
        {
            throw new ArgumentException(
                $"A live-authoring batch cannot contain more than {MaxUpdatesPerBatch} updates.",
                parameterName);
        }
        if (updates.Count > 1)
        {
            for (int index = 0; index < updates.Count; index++)
            {
                if (updates[index] is ReplaceBridgeOverlayUpdate)
                {
                    throw new ArgumentException(
                        "A bridge overlay replacement must be the only update in its batch, so the " +
                        "whole overlay is replaced by exactly one snapshot with nothing applied " +
                        "before or after it inside the same scheduler edit.",
                        $"{parameterName}[{index}]");
                }
            }
        }

        var accounting = new BatchAccounting();
        if (coalescingKey is not null)
        {
            ValidateRequiredText(
                coalescingKey,
                $"{parameterName}.CoalescingKey",
                "A coalescing key",
                MaxTextValueLength);
            accounting.AddText(coalescingKey);
        }
        ValidateOptionalOpaqueId(correlationId, $"{parameterName}.CorrelationId", "A correlation identifier");
        accounting.AddText(correlationId);
        ValidateOptionalOpaqueId(originId, $"{parameterName}.OriginId", "An origin identifier");
        accounting.AddText(originId);
        accounting.ThrowIfExceeded(parameterName);

        for (int index = 0; index < updates.Count; index++)
        {
            LiveStageUpdate? update = updates[index];
            string updateName = $"{parameterName}[{index}]";
            if (update is null)
            {
                throw new ArgumentException(
                    "A live-authoring batch cannot contain null updates.",
                    updateName);
            }
            ValidateUpdate(update, updateName, accounting);
            accounting.ThrowIfExceeded(parameterName);
        }
    }

    private static void ValidateUpdate(LiveStageUpdate update, string parameterName, BatchAccounting accounting)
    {
        switch (update)
        {
            case DefinePrimUpdate define:
                ValidatePrimPath(define.PrimPath, $"{parameterName}.PrimPath");
                accounting.AddText(define.PrimPath);
                ValidateOptionalIdentifier(define.TypeName, $"{parameterName}.TypeName");
                accounting.AddText(define.TypeName);
                return;
            case RemovePrimUpdate remove:
                ValidatePrimPath(remove.PrimPath, $"{parameterName}.PrimPath");
                accounting.AddText(remove.PrimPath);
                return;
            case SetAttributeUpdate attribute:
                ValidatePrimPath(attribute.PrimPath, $"{parameterName}.PrimPath");
                accounting.AddText(attribute.PrimPath);
                ValidateNamespacedIdentifier(
                    attribute.AttributeName,
                    $"{parameterName}.AttributeName");
                accounting.AddText(attribute.AttributeName);
                if (attribute.TimeCode is { } timeCode && !double.IsFinite(timeCode))
                {
                    throw new ArgumentOutOfRangeException(
                        $"{parameterName}.TimeCode",
                        timeCode,
                        "A time code must be finite.");
                }
                ValidateAttributeValue(attribute.Value, $"{parameterName}.Value", accounting);
                return;
            case ClearUpdate clear:
                ValidateClear(clear, parameterName, accounting);
                return;
            case SetRelationshipTargetsUpdate relationship:
                ValidatePrimPath(relationship.PrimPath, $"{parameterName}.PrimPath");
                accounting.AddText(relationship.PrimPath);
                ValidateNamespacedIdentifier(
                    relationship.RelationshipName,
                    $"{parameterName}.RelationshipName");
                accounting.AddText(relationship.RelationshipName);
                ValidateRelationshipTargets(
                    relationship.Targets,
                    $"{parameterName}.Targets",
                    accounting);
                return;
            case SetReferenceUpdate reference:
                ValidatePrimPath(reference.PrimPath, $"{parameterName}.PrimPath");
                accounting.AddText(reference.PrimPath);
                ValidateRequiredText(
                    reference.AssetPath,
                    $"{parameterName}.AssetPath",
                    "An asset path",
                    MaxTextValueLength);
                accounting.AddText(reference.AssetPath);
                ValidateOptionalPrimPath(
                    reference.TargetPrimPath,
                    $"{parameterName}.TargetPrimPath");
                accounting.AddText(reference.TargetPrimPath);
                return;
            case SetPayloadUpdate payload:
                ValidatePrimPath(payload.PrimPath, $"{parameterName}.PrimPath");
                accounting.AddText(payload.PrimPath);
                ValidateRequiredText(
                    payload.AssetPath,
                    $"{parameterName}.AssetPath",
                    "An asset path",
                    MaxTextValueLength);
                accounting.AddText(payload.AssetPath);
                ValidateOptionalPrimPath(
                    payload.TargetPrimPath,
                    $"{parameterName}.TargetPrimPath");
                accounting.AddText(payload.TargetPrimPath);
                return;
            case SetActiveUpdate active:
                ValidatePrimPath(active.PrimPath, $"{parameterName}.PrimPath");
                accounting.AddText(active.PrimPath);
                return;
            case SetInstanceableUpdate instanceable:
                ValidatePrimPath(instanceable.PrimPath, $"{parameterName}.PrimPath");
                accounting.AddText(instanceable.PrimPath);
                return;
            case SetVariantSelectionUpdate variant:
                ValidatePrimPath(variant.PrimPath, $"{parameterName}.PrimPath");
                accounting.AddText(variant.PrimPath);
                ValidateNamespacedIdentifier(
                    variant.VariantSetName,
                    $"{parameterName}.VariantSetName");
                accounting.AddText(variant.VariantSetName);
                ValidateVariants(variant, parameterName, accounting);
                return;
            case SetMetadataUpdate metadata:
                ValidatePrimPath(metadata.PrimPath, $"{parameterName}.PrimPath");
                accounting.AddText(metadata.PrimPath);
                ValidateRequiredText(
                    metadata.Key,
                    $"{parameterName}.Key",
                    "A metadata key",
                    MaxIdentifierLength);
                accounting.AddText(metadata.Key);
                ValidateMetadataValue(metadata.Value, $"{parameterName}.Value", accounting);
                return;
            case ApiSchemaUpdate apiSchema:
                ValidatePrimPath(apiSchema.PrimPath, $"{parameterName}.PrimPath");
                accounting.AddText(apiSchema.PrimPath);
                ValidateIdentifier(
                    apiSchema.SchemaToken,
                    $"{parameterName}.SchemaToken",
                    "A schema token");
                accounting.AddText(apiSchema.SchemaToken);
                if ((uint)apiSchema.Operation > (uint)LiveApiSchemaOperation.Remove)
                {
                    throw new ArgumentOutOfRangeException(
                        $"{parameterName}.Operation",
                        apiSchema.Operation,
                        "The API schema operation is not supported.");
                }
                return;
            case SetPointInstancerOrientationsUpdate orientations:
                ValidatePrimPath(orientations.PrimPath, $"{parameterName}.PrimPath");
                accounting.AddText(orientations.PrimPath);
                ValidateOrientations(
                    orientations.Orientations,
                    $"{parameterName}.Orientations",
                    accounting);
                return;
            case ReplaceBridgeOverlayUpdate replace:
                {
                    (long elements, long bytes) = ValidateBridgeOverlayUpdates(
                        replace.BridgeRootPath,
                        replace.Updates,
                        $"{parameterName}.Updates");
                    accounting.AddText(replace.BridgeRootPath);
                    accounting.AddElements(elements);
                    accounting.AddBytes(bytes);
                }
                return;
            default:
                throw new ArgumentException(
                    $"The live update type '{update.GetType().FullName}' is not supported.",
                    parameterName);
        }
    }

    private static void ValidateClear(ClearUpdate clear, string parameterName, BatchAccounting accounting)
    {
        ValidatePrimPath(clear.PrimPath, $"{parameterName}.PrimPath");
        accounting.AddText(clear.PrimPath);
        string nameParameter = $"{parameterName}.Name";
        switch (clear.TargetKind)
        {
            case LiveClearTargetKind.AttributeValue:
                ValidateNamespacedIdentifier(clear.Name, nameParameter);
                accounting.AddText(clear.Name);
                break;
            case LiveClearTargetKind.RelationshipTargets:
                ValidateNamespacedIdentifier(clear.Name, nameParameter);
                accounting.AddText(clear.Name);
                break;
            case LiveClearTargetKind.Metadata:
                ValidateRequiredText(clear.Name, nameParameter, "A metadata key", MaxIdentifierLength);
                accounting.AddText(clear.Name);
                break;
            case LiveClearTargetKind.References:
            case LiveClearTargetKind.Payloads:
                if (clear.Name is not null)
                {
                    throw new ArgumentException(
                        $"Clearing {clear.TargetKind} does not accept a name.",
                        nameParameter);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    $"{parameterName}.TargetKind",
                    clear.TargetKind,
                    "The clear target kind is not supported.");
        }
    }

    private static void ValidateAttributeValue(
        LiveAttributeValue value,
        string parameterName,
        BatchAccounting accounting)
    {
        if ((uint)value.Kind > (uint)LiveAttributeKind.StringArray)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value.Kind,
                "The attribute kind is not supported.");
        }

        switch (value.Kind)
        {
            case LiveAttributeKind.Boolean:
                accounting.AddBytes(1);
                return;
            case LiveAttributeKind.Int64:
                accounting.AddBytes(8);
                return;
            case LiveAttributeKind.Double:
                ValidateFiniteDouble(value.DoubleValue, $"{parameterName}.DoubleValue");
                accounting.AddBytes(8);
                return;
            case LiveAttributeKind.String:
                ValidateNoNullCharacter(
                    value.StringValue,
                    $"{parameterName}.StringValue",
                    "An attribute text value",
                    MaxTextValueLength);
                accounting.AddText(value.StringValue);
                return;
            case LiveAttributeKind.Token:
                ValidateNoNullCharacter(
                    value.TokenValue,
                    $"{parameterName}.TokenValue",
                    "An attribute text value",
                    MaxTextValueLength);
                if (string.IsNullOrWhiteSpace(value.TokenValue))
                {
                    throw new ArgumentException(
                        "A token value cannot be empty.",
                        $"{parameterName}.TokenValue");
                }
                accounting.AddText(value.TokenValue);
                return;
            case LiveAttributeKind.Vec3f:
                ValidateFiniteVec3f(value.Vec3f, $"{parameterName}.Vec3f");
                accounting.AddBytes(12);
                return;
            case LiveAttributeKind.Matrix4d:
                ValidateFiniteMatrix4d(value.Matrix4d, $"{parameterName}.Matrix4d");
                accounting.AddBytes(128);
                return;
            case LiveAttributeKind.Int32Array:
                ValidateCollectionCount(value.Int32Array.Count, $"{parameterName}.Int32Array");
                accounting.AddElements(value.Int32Array.Count);
                accounting.AddBytes(4L * value.Int32Array.Count);
                return;
            case LiveAttributeKind.FloatArray:
                ValidateCollectionCount(value.FloatArray.Count, $"{parameterName}.FloatArray");
                ValidateFiniteFloatArray(value.FloatArray, $"{parameterName}.FloatArray");
                accounting.AddElements(value.FloatArray.Count);
                accounting.AddBytes(4L * value.FloatArray.Count);
                return;
            case LiveAttributeKind.DoubleArray:
                ValidateCollectionCount(value.DoubleArray.Count, $"{parameterName}.DoubleArray");
                ValidateFiniteDoubleArray(value.DoubleArray, $"{parameterName}.DoubleArray");
                accounting.AddElements(value.DoubleArray.Count);
                accounting.AddBytes(8L * value.DoubleArray.Count);
                return;
            case LiveAttributeKind.Vec2fArray:
                ValidateCollectionCount(value.Vec2fArray.Count, $"{parameterName}.Vec2fArray");
                ValidateFiniteVec2fArray(value.Vec2fArray, $"{parameterName}.Vec2fArray");
                accounting.AddElements(value.Vec2fArray.Count);
                accounting.AddBytes(8L * value.Vec2fArray.Count);
                return;
            case LiveAttributeKind.Vec3fArray:
                ValidateCollectionCount(value.Vec3fArray.Count, $"{parameterName}.Vec3fArray");
                ValidateFiniteVec3fArray(value.Vec3fArray, $"{parameterName}.Vec3fArray");
                accounting.AddElements(value.Vec3fArray.Count);
                accounting.AddBytes(12L * value.Vec3fArray.Count);
                return;
            case LiveAttributeKind.Color3fArray:
                ValidateCollectionCount(value.Color3fArray.Count, $"{parameterName}.Color3fArray");
                ValidateFiniteVec3fArray(value.Color3fArray, $"{parameterName}.Color3fArray");
                accounting.AddElements(value.Color3fArray.Count);
                accounting.AddBytes(12L * value.Color3fArray.Count);
                return;
            case LiveAttributeKind.BooleanArray:
                ValidateCollectionCount(value.BooleanArray.Count, $"{parameterName}.BooleanArray");
                accounting.AddElements(value.BooleanArray.Count);
                accounting.AddBytes(value.BooleanArray.Count);
                return;
            case LiveAttributeKind.TokenArray:
                ValidateCollectionCount(value.TokenArray.Count, $"{parameterName}.TokenArray");
                ValidateTextArray(value.TokenArray, $"{parameterName}.TokenArray", accounting);
                accounting.AddElements(value.TokenArray.Count);
                return;
            case LiveAttributeKind.StringArray:
                ValidateCollectionCount(value.StringArray.Count, $"{parameterName}.StringArray");
                ValidateTextArray(value.StringArray, $"{parameterName}.StringArray", accounting);
                accounting.AddElements(value.StringArray.Count);
                return;
            default:
                return;
        }
    }

    private static void ValidateTextArray(
        IReadOnlyList<string> values,
        string parameterName,
        BatchAccounting accounting)
    {
        for (int index = 0; index < values.Count; index++)
        {
            ValidateNoNullCharacter(
                values[index],
                $"{parameterName}[{index}]",
                "An array text value",
                MaxTextValueLength);
            accounting.AddText(values[index]);
        }
    }

    private static void ValidateFiniteFloatArray(IReadOnlyList<float> values, string parameterName)
    {
        for (int index = 0; index < values.Count; index++)
        {
            ValidateFiniteFloat(values[index], $"{parameterName}[{index}]");
        }
    }

    private static void ValidateFiniteDoubleArray(IReadOnlyList<double> values, string parameterName)
    {
        for (int index = 0; index < values.Count; index++)
        {
            ValidateFiniteDouble(values[index], $"{parameterName}[{index}]");
        }
    }

    private static void ValidateFiniteVec2fArray(IReadOnlyList<UsdVec2f> values, string parameterName)
    {
        for (int index = 0; index < values.Count; index++)
        {
            ValidateFiniteVec2f(values[index], $"{parameterName}[{index}]");
        }
    }

    private static void ValidateFiniteVec3fArray(IReadOnlyList<UsdVec3f> values, string parameterName)
    {
        for (int index = 0; index < values.Count; index++)
        {
            ValidateFiniteVec3f(values[index], $"{parameterName}[{index}]");
        }
    }

    private static void ValidateOrientations(
        IReadOnlyList<UsdQuatf>? orientations,
        string parameterName,
        BatchAccounting accounting)
    {
        if (orientations is null)
        {
            throw new ArgumentException(
                "The orientation array cannot be null.",
                parameterName);
        }
        ValidateCollectionCount(orientations.Count, parameterName);
        for (int index = 0; index < orientations.Count; index++)
        {
            ValidateFiniteQuatf(orientations[index], $"{parameterName}[{index}]");
        }
        accounting.AddElements(orientations.Count);
        accounting.AddBytes(16L * orientations.Count);
    }

    private static void ValidateFiniteFloat(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite.");
        }
    }

    private static void ValidateFiniteDouble(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite.");
        }
    }

    private static void ValidateFiniteVec2f(UsdVec2f value, string parameterName)
    {
        ValidateFiniteFloat(value.X, $"{parameterName}.X");
        ValidateFiniteFloat(value.Y, $"{parameterName}.Y");
    }

    private static void ValidateFiniteVec3f(UsdVec3f value, string parameterName)
    {
        ValidateFiniteFloat(value.X, $"{parameterName}.X");
        ValidateFiniteFloat(value.Y, $"{parameterName}.Y");
        ValidateFiniteFloat(value.Z, $"{parameterName}.Z");
    }

    private static void ValidateFiniteQuatf(UsdQuatf value, string parameterName)
    {
        ValidateFiniteFloat(value.Real, $"{parameterName}.Real");
        ValidateFiniteFloat(value.X, $"{parameterName}.X");
        ValidateFiniteFloat(value.Y, $"{parameterName}.Y");
        ValidateFiniteFloat(value.Z, $"{parameterName}.Z");
    }

    private static void ValidateFiniteMatrix4d(UsdMatrix4d value, string parameterName)
    {
        double[] components = value.ToArray();
        for (int index = 0; index < components.Length; index++)
        {
            ValidateFiniteDouble(components[index], $"{parameterName}[{index}]");
        }
    }

    private static void ValidateCollectionCount(int count, string parameterName)
    {
        if (count > MaxCollectionElementCount)
        {
            throw new ArgumentException(
                $"The collection cannot exceed {MaxCollectionElementCount} elements.",
                parameterName);
        }
    }

    private static void ValidateMetadataValue(
        LiveMetadataValue value,
        string parameterName,
        BatchAccounting accounting)
    {
        if ((uint)value.Kind > (uint)LiveMetadataKind.String)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value.Kind,
                "The metadata kind is not supported.");
        }

        switch (value.Kind)
        {
            case LiveMetadataKind.Boolean:
                accounting.AddBytes(1);
                break;
            case LiveMetadataKind.Int64:
                accounting.AddBytes(8);
                break;
            case LiveMetadataKind.Double:
                ValidateFiniteDouble(value.DoubleValue, $"{parameterName}.DoubleValue");
                accounting.AddBytes(8);
                break;
            case LiveMetadataKind.String:
                ValidateNoNullCharacter(
                    value.StringValue,
                    $"{parameterName}.StringValue",
                    "A metadata text value",
                    MaxTextValueLength);
                accounting.AddText(value.StringValue);
                break;
        }
    }

    private static void ValidateRelationshipTargets(
        IReadOnlyList<string>? targets,
        string parameterName,
        BatchAccounting accounting)
    {
        if (targets is null)
        {
            throw new ArgumentException(
                "Relationship targets cannot be null.",
                parameterName);
        }

        ValidateCollectionCount(targets.Count, parameterName);
        for (int index = 0; index < targets.Count; index++)
        {
            ValidatePrimPath(targets[index], $"{parameterName}[{index}]");
            accounting.AddText(targets[index]);
        }
        accounting.AddElements(targets.Count);
    }

    private static void ValidateVariants(
        SetVariantSelectionUpdate update,
        string parameterName,
        BatchAccounting accounting)
    {
        IReadOnlyList<string>? variants = update.KnownVariants;
        string variantsName = $"{parameterName}.KnownVariants";
        if (variants is null)
        {
            throw new ArgumentException(
                "The known variant list cannot be null.",
                variantsName);
        }
        if (variants.Count == 0)
        {
            throw new ArgumentException(
                "The known variant list cannot be empty.",
                variantsName);
        }
        ValidateCollectionCount(variants.Count, variantsName);

        var known = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < variants.Count; index++)
        {
            string? variant = variants[index];
            string variantName = $"{variantsName}[{index}]";
            ValidateIdentifier(variant, variantName, "A variant name");
            if (!known.Add(variant!))
            {
                throw new ArgumentException(
                    $"The known variant list contains duplicate '{variant}'.",
                    variantName);
            }
            accounting.AddText(variant);
        }

        if (update.Selection is not null)
        {
            string selectionName = $"{parameterName}.Selection";
            ValidateIdentifier(update.Selection, selectionName, "A variant selection");
            if (!known.Contains(update.Selection))
            {
                throw new ArgumentException(
                    $"The variant selection '{update.Selection}' is not in the known variant list.",
                    selectionName);
            }
            accounting.AddText(update.Selection);
        }

        accounting.AddElements(variants.Count);
    }

    private static void ValidateOptionalIdentifier(string? value, string parameterName)
    {
        if (value is not null)
        {
            ValidateNamespacedIdentifier(value, parameterName);
        }
    }

    private static void ValidateNamespacedIdentifier(string? value, string parameterName)
    {
        ValidateRequiredText(value, parameterName, "A namespaced identifier", MaxIdentifierLength);
        int segmentStart = 0;
        for (int index = 0; index <= value!.Length; index++)
        {
            if (index == value.Length || value[index] == ':')
            {
                if (!IsIdentifier(value.AsSpan(segmentStart, index - segmentStart)))
                {
                    throw new ArgumentException(
                        $"'{value}' is not a valid namespaced identifier.",
                        parameterName);
                }
                segmentStart = index + 1;
            }
        }
    }

    private static void ValidateIdentifier(
        string? value,
        string parameterName,
        string description)
    {
        ValidateRequiredText(value, parameterName, description, MaxIdentifierLength);
        if (!IsIdentifier(value.AsSpan()))
        {
            throw new ArgumentException(
                $"'{value}' is not a valid identifier.",
                parameterName);
        }
    }

    private static bool IsIdentifier(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty ||
            (!char.IsAsciiLetter(value[0]) && value[0] != '_'))
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            char current = value[index];
            if (!char.IsAsciiLetterOrDigit(current) && current != '_')
            {
                return false;
            }
        }
        return true;
    }

    private static void ValidateOptionalPrimPath(string? path, string parameterName)
    {
        if (path is not null)
        {
            ValidatePrimPath(path, parameterName);
        }
    }

    private static void ValidatePrimPath(string? path, string parameterName)
    {
        UsdPath.ValidateAbsolutePrimPath(path, parameterName);
        if (path!.Length > MaxPathLength)
        {
            throw new ArgumentException(
                $"A prim path cannot exceed {MaxPathLength} characters.",
                parameterName);
        }
    }

    private static void ValidateRequiredText(
        string? value,
        string parameterName,
        string description,
        int maxLength)
    {
        ValidateNoNullCharacter(value, parameterName, description, maxLength);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"{description} cannot be empty.",
                parameterName);
        }
    }

    private static void ValidateOptionalOpaqueId(string? value, string parameterName, string description)
    {
        if (value is null)
        {
            return;
        }

        ValidateOpaqueIdentity(value, parameterName, description);
    }

    /// <summary>Validates a required opaque identity string against the opaque-identifier bounds.</summary>
    internal static void ValidateOpaqueIdentity(
        string? value,
        string parameterName,
        string description)
    {
        ValidateRequiredText(value, parameterName, description, MaxOpaqueIdLength);
        ValidateWellFormedUtf16(value, parameterName, description);
    }

    /// <summary>
    /// Throws when an opaque identity is not well-formed UTF-16, naming only the offending index.
    /// </summary>
    internal static void ValidateWellFormedUtf16(
        string? value,
        string parameterName,
        string description)
    {
        int offending = FindIllFormedUtf16Index(value);
        if (offending >= 0)
        {
            throw new ArgumentException(
                $"{description} cannot contain an unpaired surrogate; the code unit at index " +
                $"{offending.ToString(CultureInfo.InvariantCulture)} has no pair. Such a value has " +
                "no UTF-8 encoding, so encoders replace it with U+FFFD and two different " +
                "identifiers would hash, key, and compare as one.",
                parameterName);
        }
    }

    /// <summary>
    /// Returns whether a string is well-formed UTF-16, so it has an exact, injective UTF-8 encoding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An identifier that reaches the wire is UTF-8 encoded, hashed into idempotency keys and replay
    /// fingerprints, and compared byte for byte on the peer. The default UTF-8 encoder replaces an
    /// unpaired surrogate with U+FFFD instead of failing, so two distinct identifiers that differ
    /// only in an unpaired surrogate encode to identical bytes — and a colliding idempotency key is
    /// exactly what a peer's ledger reads as "already applied".
    /// </para>
    /// <para>
    /// The check is therefore made where the identity is validated, before any encoding, hashing, or
    /// wire use, rather than left to an encoder that has already been told not to complain.
    /// </para>
    /// </remarks>
    /// <param name="value">The value to check. A <see langword="null"/> value is not well formed.</param>
    /// <returns><see langword="true"/> when every surrogate in the value is correctly paired.</returns>
    public static bool IsWellFormedUtf16(string? value) => value is not null && FindIllFormedUtf16Index(value) < 0;

    private static int FindIllFormedUtf16Index(string? value)
    {
        if (value is null)
        {
            return -1;
        }

        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (!char.IsSurrogate(current))
            {
                continue;
            }
            if (!char.IsHighSurrogate(current) ||
                index + 1 >= value.Length ||
                !char.IsLowSurrogate(value[index + 1]))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    /// <summary>Validates an optional opaque correlation identifier.</summary>
    internal static void ValidateOptionalCorrelationId(string? value, string parameterName) =>
        ValidateOptionalOpaqueId(value, parameterName, "A correlation identifier");

    private static void ValidateNoNullCharacter(
        string? value,
        string parameterName,
        string description,
        int maxLength)
    {
        if (value is null)
        {
            throw new ArgumentException(
                $"{description} cannot be null.",
                parameterName);
        }
        if (value.Contains('\0'))
        {
            throw new ArgumentException(
                $"{description} cannot contain a NUL character.",
                parameterName);
        }
        if (value.Length > maxLength)
        {
            throw new ArgumentException(
                $"{description} cannot exceed {maxLength} characters.",
                parameterName);
        }
    }

    /// <summary>
    /// Accumulates the running element count and estimated retained byte size across one batch during a
    /// single validation pass, so both aggregate bounds are enforced without a second traversal.
    /// Arithmetic is overflow-safe: every addition is checked, and a hypothetical overflow (unreachable
    /// given <see cref="MaxUpdatesPerBatch"/>, <see cref="MaxCollectionElementCount"/>, and
    /// <see cref="MaxTextValueLength"/>) surfaces as a bounded <see cref="ArgumentException"/> rather
    /// than an unhandled <see cref="OverflowException"/>.
    /// </summary>
    private sealed class BatchAccounting
    {
        private long _elementCount;
        private long _estimatedBytes;

        public long ElementCount => _elementCount;

        public long EstimatedBytes => _estimatedBytes;

        public void AddElements(long count) => _elementCount = AddChecked(_elementCount, count);

        public void AddBytes(long bytes) => _estimatedBytes = AddChecked(_estimatedBytes, bytes);

        public void AddText(string? value)
        {
            if (value is not null)
            {
                AddBytes(Encoding.UTF8.GetByteCount(value));
            }
        }

        public void ThrowIfExceeded(string parameterName)
        {
            if (_elementCount > MaxTotalCollectionElementCountPerBatch)
            {
                throw new ArgumentException(
                    "The batch's combined relationship/variant/array/orientation element count " +
                    $"exceeds {MaxTotalCollectionElementCountPerBatch}.",
                    parameterName);
            }
            if (_estimatedBytes > MaxEstimatedBatchPayloadBytes)
            {
                throw new ArgumentException(
                    "The batch's estimated retained payload (text and numeric array bytes) exceeds " +
                    $"{MaxEstimatedBatchPayloadBytes} bytes.",
                    parameterName);
            }
        }

        private static long AddChecked(long total, long addend)
        {
            try
            {
                return checked(total + addend);
            }
            catch (OverflowException exception)
            {
                throw new ArgumentException(
                    "The batch's accounting overflowed while summing element counts or estimated " +
                    "bytes.",
                    exception);
            }
        }
    }
}
