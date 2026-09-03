// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Protocol;

/// <summary>
/// Checks one outbound <see cref="BridgeLocalBatch"/> against every bound in a negotiated
/// <see cref="BridgeLimits"/> set.
/// </summary>
/// <remarks>
/// <para>
/// A negotiated limit set has eight bounds, not two. Checking only the update count and the encoded
/// size would let a batch that is legal for this implementation carry a path, an identifier, a text
/// value, an opaque identifier, or a collection the peer has told us it will refuse — and the first
/// place that refusal would be discovered is the peer, after the local edit is already authoritative
/// here. So the whole batch is walked: every update, every attribute value, every metadata value,
/// and every element of every collection inside them.
/// </para>
/// <para>
/// This is deliberately one validator rather than one per call site. The bounds are checked when a
/// batch is admitted and again immediately before it is sent, and two copies of an eight-bound
/// walk would eventually disagree about which batch is sendable.
/// </para>
/// <para>
/// Lengths are counted in characters, exactly as <see cref="LiveAuthoringValidation"/> counts them,
/// so a bound that both peers derive from the same constants means the same thing on both sides.
/// The encoded size is measured with the message's own <c>CalculateSize</c> rather than by encoding
/// the batch: measuring an oversized batch costs a traversal instead of tens of megabytes, and
/// encoding it would throw for exactly the batch this check exists to refuse.
/// </para>
/// </remarks>
internal static class BridgeOutboundLimits
{
    /// <summary>
    /// Returns whether <paramref name="batch"/> fits inside <paramref name="limits"/>, and if not,
    /// the bounded reason it does not.
    /// </summary>
    internal static bool TryValidate(BridgeLocalBatch batch, BridgeLimits limits, out string detail)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (!TryOpaqueId(batch.Epoch.RemoteOriginId, "the session's remote origin identifier", limits, out detail) ||
            !TryOpaqueId(batch.Epoch.SessionId, "the session identifier", limits, out detail) ||
            !TryOpaqueId(batch.OriginId, "the origin identifier", limits, out detail) ||
            !TryOpaqueId(batch.IdempotencyKey, "the idempotency key", limits, out detail) ||
            !TryOpaqueId(batch.CorrelationId, "the correlation identifier", limits, out detail) ||
            !TryText(batch.CoalescingKey, "the coalescing key", limits, out detail))
        {
            return false;
        }

        long totalElements = 0;
        for (int index = 0; index < batch.Updates.Count; index++)
        {
            if (!TryValidateUpdate(batch.Updates[index], limits, ref totalElements, out detail))
            {
                return false;
            }
            if (totalElements > limits.MaxTotalCollectionElementCount)
            {
                detail = string.Create(
                    CultureInfo.InvariantCulture,
                    $"The batch carries {totalElements} collection elements in total, past the " +
                    $"negotiated bound of {limits.MaxTotalCollectionElementCount}.");
                return false;
            }
        }

        long payloadBytes;
        try
        {
            payloadBytes = BridgeMessageCodec.ToWire(batch).CalculateSize();
        }
        catch (BridgeProtocolException exception)
        {
            // The batch carries something this contract version cannot express. That is a refusal,
            // not a fault: the caller answers one receipt and keeps running.
            detail = $"The batch cannot be encoded for this contract: {exception.Error.Code}.";
            return false;
        }

        if (payloadBytes > BridgeProtocol.MaxFrameBytes)
        {
            detail = string.Create(
                CultureInfo.InvariantCulture,
                $"The batch encodes to {payloadBytes} bytes, past the " +
                $"{BridgeProtocol.MaxFrameBytes}-byte frame budget.");
            return false;
        }
        if (!limits.Allows(batch.Updates.Count, payloadBytes))
        {
            detail = string.Create(
                CultureInfo.InvariantCulture,
                $"The batch exceeds the negotiated bounds ({batch.Updates.Count} updates, " +
                $"{payloadBytes} bytes).");
            return false;
        }

        detail = string.Empty;
        return true;
    }

    private static bool TryValidateUpdate(
        LiveStageUpdate update,
        BridgeLimits limits,
        ref long totalElements,
        out string detail)
    {
        switch (update)
        {
            case DefinePrimUpdate define:
                return TryPath(define.PrimPath, "a prim path", limits, out detail) &&
                    TryIdentifier(define.TypeName, "a prim type name", limits, out detail);

            case RemovePrimUpdate remove:
                return TryPath(remove.PrimPath, "a prim path", limits, out detail);

            case SetAttributeUpdate attribute:
                return TryPath(attribute.PrimPath, "a prim path", limits, out detail) &&
                    TryIdentifier(attribute.AttributeName, "an attribute name", limits, out detail) &&
                    TryValidateAttributeValue(attribute.Value, limits, ref totalElements, out detail);

            case ClearUpdate clear:
                return TryPath(clear.PrimPath, "a prim path", limits, out detail) &&
                    TryIdentifier(clear.Name, "a cleared property or metadata name", limits, out detail);

            case SetRelationshipTargetsUpdate relationship:
                return TryPath(relationship.PrimPath, "a prim path", limits, out detail) &&
                    TryIdentifier(
                        relationship.RelationshipName,
                        "a relationship name",
                        limits,
                        out detail) &&
                    TryCollection(
                        relationship.Targets.Count,
                        "a relationship target list",
                        limits,
                        ref totalElements,
                        out detail) &&
                    TryPaths(relationship.Targets, "a relationship target path", limits, out detail);

            case SetReferenceUpdate reference:
                return TryPath(reference.PrimPath, "a prim path", limits, out detail) &&
                    TryText(reference.AssetPath, "an asset path", limits, out detail) &&
                    TryPath(reference.TargetPrimPath, "a reference target path", limits, out detail);

            case SetPayloadUpdate payload:
                return TryPath(payload.PrimPath, "a prim path", limits, out detail) &&
                    TryText(payload.AssetPath, "an asset path", limits, out detail) &&
                    TryPath(payload.TargetPrimPath, "a payload target path", limits, out detail);

            case SetActiveUpdate active:
                return TryPath(active.PrimPath, "a prim path", limits, out detail);

            case SetInstanceableUpdate instanceable:
                return TryPath(instanceable.PrimPath, "a prim path", limits, out detail);

            case SetVariantSelectionUpdate variant:
                return TryPath(variant.PrimPath, "a prim path", limits, out detail) &&
                    TryIdentifier(variant.VariantSetName, "a variant set name", limits, out detail) &&
                    TryCollection(
                        variant.KnownVariants.Count,
                        "a known variant list",
                        limits,
                        ref totalElements,
                        out detail) &&
                    TryIdentifiers(variant.KnownVariants, "a variant name", limits, out detail) &&
                    TryIdentifier(variant.Selection, "a variant selection", limits, out detail);

            case SetMetadataUpdate metadata:
                return TryPath(metadata.PrimPath, "a prim path", limits, out detail) &&
                    TryIdentifier(metadata.Key, "a metadata key", limits, out detail) &&
                    TryValidateMetadataValue(metadata.Value, limits, out detail);

            case ApiSchemaUpdate apiSchema:
                return TryPath(apiSchema.PrimPath, "a prim path", limits, out detail) &&
                    TryIdentifier(apiSchema.SchemaToken, "a schema token", limits, out detail);

            case SetPointInstancerOrientationsUpdate orientations:
                return TryPath(orientations.PrimPath, "a prim path", limits, out detail) &&
                    TryCollection(
                        orientations.Orientations.Count,
                        "an orientation array",
                        limits,
                        ref totalElements,
                        out detail);

            default:
                // A local batch cannot carry an overlay replacement, and any other kind is one this
                // walk has not been taught to measure. Refusing is the only safe answer: admitting
                // it would send a batch whose bounds were never checked.
                detail = $"The update kind '{update.GetType().Name}' cannot be published outward.";
                return false;
        }
    }

    private static bool TryValidateAttributeValue(
        LiveAttributeValue value,
        BridgeLimits limits,
        ref long totalElements,
        out string detail)
    {
        switch (value.Kind)
        {
            case LiveAttributeKind.String:
                return TryText(value.StringValue, "an attribute text value", limits, out detail);
            case LiveAttributeKind.Token:
                return TryText(value.TokenValue, "an attribute token value", limits, out detail);
            case LiveAttributeKind.Int32Array:
                return TryCollection(
                    value.Int32Array.Count,
                    "an int32 array",
                    limits,
                    ref totalElements,
                    out detail);
            case LiveAttributeKind.FloatArray:
                return TryCollection(
                    value.FloatArray.Count,
                    "a float array",
                    limits,
                    ref totalElements,
                    out detail);
            case LiveAttributeKind.DoubleArray:
                return TryCollection(
                    value.DoubleArray.Count,
                    "a double array",
                    limits,
                    ref totalElements,
                    out detail);
            case LiveAttributeKind.Vec2fArray:
                return TryCollection(
                    value.Vec2fArray.Count,
                    "a vec2f array",
                    limits,
                    ref totalElements,
                    out detail);
            case LiveAttributeKind.Vec3fArray:
                return TryCollection(
                    value.Vec3fArray.Count,
                    "a vec3f array",
                    limits,
                    ref totalElements,
                    out detail);
            case LiveAttributeKind.Color3fArray:
                return TryCollection(
                    value.Color3fArray.Count,
                    "a color3f array",
                    limits,
                    ref totalElements,
                    out detail);
            case LiveAttributeKind.BooleanArray:
                return TryCollection(
                    value.BooleanArray.Count,
                    "a boolean array",
                    limits,
                    ref totalElements,
                    out detail);
            case LiveAttributeKind.TokenArray:
                return TryCollection(
                    value.TokenArray.Count,
                    "a token array",
                    limits,
                    ref totalElements,
                    out detail) &&
                    TryTexts(value.TokenArray, "an array token value", limits, out detail);
            case LiveAttributeKind.StringArray:
                return TryCollection(
                    value.StringArray.Count,
                    "a string array",
                    limits,
                    ref totalElements,
                    out detail) &&
                    TryTexts(value.StringArray, "an array text value", limits, out detail);
            default:
                // Scalars carry no bounded text or collection of their own; their finiteness and
                // kind are the authoring layer's business, not the negotiated bounds'.
                detail = string.Empty;
                return true;
        }
    }

    private static bool TryValidateMetadataValue(
        LiveMetadataValue value,
        BridgeLimits limits,
        out string detail)
    {
        if (value.Kind == LiveMetadataKind.String)
        {
            return TryText(value.StringValue, "a metadata text value", limits, out detail);
        }

        detail = string.Empty;
        return true;
    }

    private static bool TryPaths(
        IReadOnlyList<string> values,
        string description,
        BridgeLimits limits,
        out string detail)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (!TryPath(values[index], description, limits, out detail))
            {
                return false;
            }
        }

        detail = string.Empty;
        return true;
    }

    private static bool TryIdentifiers(
        IReadOnlyList<string> values,
        string description,
        BridgeLimits limits,
        out string detail)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (!TryIdentifier(values[index], description, limits, out detail))
            {
                return false;
            }
        }

        detail = string.Empty;
        return true;
    }

    private static bool TryTexts(
        IReadOnlyList<string> values,
        string description,
        BridgeLimits limits,
        out string detail)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (!TryText(values[index], description, limits, out detail))
            {
                return false;
            }
        }

        detail = string.Empty;
        return true;
    }

    private static bool TryCollection(
        int count,
        string description,
        BridgeLimits limits,
        ref long totalElements,
        out string detail)
    {
        if (count > limits.MaxCollectionElementCount)
        {
            detail = string.Create(
                CultureInfo.InvariantCulture,
                $"The batch carries {description} of {count} elements, past the negotiated bound " +
                $"of {limits.MaxCollectionElementCount} elements.");
            return false;
        }

        totalElements += count;
        detail = string.Empty;
        return true;
    }

    private static bool TryPath(
        string? value,
        string description,
        BridgeLimits limits,
        out string detail) =>
        TryLength(value, description, limits.MaxPathLength, "path", out detail);

    private static bool TryIdentifier(
        string? value,
        string description,
        BridgeLimits limits,
        out string detail) =>
        TryLength(value, description, limits.MaxIdentifierLength, "identifier", out detail);

    private static bool TryText(
        string? value,
        string description,
        BridgeLimits limits,
        out string detail) =>
        TryLength(value, description, limits.MaxTextValueLength, "text", out detail);

    private static bool TryOpaqueId(
        string? value,
        string description,
        BridgeLimits limits,
        out string detail) =>
        TryLength(value, description, limits.MaxOpaqueIdLength, "opaque identifier", out detail);

    private static bool TryLength(
        string? value,
        string description,
        int bound,
        string boundName,
        out string detail)
    {
        if (value is not null && value.Length > bound)
        {
            detail = string.Create(
                CultureInfo.InvariantCulture,
                $"The batch carries {description} of {value.Length} characters, past the " +
                $"negotiated {boundName} bound of {bound} characters.");
            return false;
        }

        detail = string.Empty;
        return true;
    }
}
