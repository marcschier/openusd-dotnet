// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenUsd.LiveAuthoring;

/// <summary>
/// A fixed-size, content-derived identity for one inbound delta, used to decide whether a replayed
/// sequence is the same message or a conflicting one.
/// </summary>
/// <remarks>
/// The fingerprint is a SHA-256 digest of a canonical encoding, never a reference identity or a
/// synthesized <see cref="object.ToString"/>. Record <c>ToString</c> would compare collection
/// properties by type name, so two deltas carrying completely different arrays would look identical;
/// that is exactly the confusion a replay ledger must not make. Storing a digest rather than the
/// canonical bytes also keeps the ledger's memory fixed per entry regardless of payload size.
/// </remarks>
internal readonly struct LiveAuthoringDeltaFingerprint : IEquatable<LiveAuthoringDeltaFingerprint>
{
    /// <summary>The digest length in bytes, and therefore the payload size of one ledger entry.</summary>
    internal const int DigestBytes = 32;

    private readonly ulong _a;
    private readonly ulong _b;
    private readonly ulong _c;
    private readonly ulong _d;

    private LiveAuthoringDeltaFingerprint(ReadOnlySpan<byte> digest)
    {
        _a = BinaryPrimitives.ReadUInt64LittleEndian(digest);
        _b = BinaryPrimitives.ReadUInt64LittleEndian(digest[8..]);
        _c = BinaryPrimitives.ReadUInt64LittleEndian(digest[16..]);
        _d = BinaryPrimitives.ReadUInt64LittleEndian(digest[24..]);
    }

    /// <summary>
    /// Computes the fingerprint of one delta from its epoch identity, effective origin, correlation
    /// identifier, coalescing key, and canonical update content.
    /// </summary>
    /// <param name="delta">The delta to fingerprint.</param>
    /// <param name="effectiveOriginId">
    /// The origin the coordinator would forward to the sink, which is the delta's own origin when it
    /// has one and the authoritative remote origin otherwise. Omitting the origin is therefore
    /// equivalent to explicitly naming the authoritative remote origin, while a different origin
    /// remains distinguishable.
    /// </param>
    internal static LiveAuthoringDeltaFingerprint Compute(
        LiveAuthoringDelta delta,
        string effectiveOriginId)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var writer = new CanonicalWriter(hash);
        writer.WriteText(delta.Epoch.RemoteOriginId);
        writer.WriteText(delta.Epoch.SessionId);
        writer.WriteInt64(delta.Epoch.Epoch);
        writer.WriteInt64(delta.Sequence);
        writer.WriteText(effectiveOriginId);
        writer.WriteOptionalText(delta.CorrelationId);
        writer.WriteOptionalText(delta.CoalescingKey);
        writer.WriteInt64(delta.Updates.Count);
        foreach (LiveStageUpdate update in delta.Updates)
        {
            writer.WriteUpdate(update);
        }

        Span<byte> digest = stackalloc byte[DigestBytes];
        int written = hash.GetHashAndReset(digest);
        return written == DigestBytes
            ? new LiveAuthoringDeltaFingerprint(digest)
            : throw new InvalidOperationException(
                $"The SHA-256 digest produced {written} bytes instead of {DigestBytes}.");
    }

    /// <inheritdoc/>
    public bool Equals(LiveAuthoringDeltaFingerprint other) =>
        _a == other._a && _b == other._b && _c == other._c && _d == other._d;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is LiveAuthoringDeltaFingerprint other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_a, _b, _c, _d);

    /// <summary>Returns a short, stable, log-safe rendering of the digest prefix.</summary>
    public override string ToString() => _a.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Writes a canonical, unambiguous byte encoding of update content into a hash. Every value is
    /// length- or kind-prefixed so no two distinct payloads can produce the same byte stream by
    /// concatenation.
    /// </summary>
    private readonly struct CanonicalWriter(IncrementalHash hash)
    {
        internal void WriteUpdate(LiveStageUpdate update)
        {
            switch (update)
            {
                case DefinePrimUpdate define:
                    WriteTag(1);
                    WriteText(define.PrimPath);
                    WriteOptionalText(define.TypeName);
                    return;
                case RemovePrimUpdate remove:
                    WriteTag(2);
                    WriteText(remove.PrimPath);
                    return;
                case SetAttributeUpdate attribute:
                    WriteTag(3);
                    WriteText(attribute.PrimPath);
                    WriteText(attribute.AttributeName);
                    WriteOptionalDouble(attribute.TimeCode);
                    WriteAttributeValue(attribute.Value);
                    return;
                case ClearUpdate clear:
                    WriteTag(4);
                    WriteText(clear.PrimPath);
                    WriteInt64((long)clear.TargetKind);
                    WriteOptionalText(clear.Name);
                    return;
                case SetRelationshipTargetsUpdate relationship:
                    WriteTag(5);
                    WriteText(relationship.PrimPath);
                    WriteText(relationship.RelationshipName);
                    WriteTextList(relationship.Targets);
                    return;
                case SetReferenceUpdate reference:
                    WriteTag(6);
                    WriteText(reference.PrimPath);
                    WriteOptionalText(reference.AssetPath);
                    WriteOptionalText(reference.TargetPrimPath);
                    return;
                case SetPayloadUpdate payload:
                    WriteTag(7);
                    WriteText(payload.PrimPath);
                    WriteOptionalText(payload.AssetPath);
                    WriteOptionalText(payload.TargetPrimPath);
                    return;
                case SetActiveUpdate active:
                    WriteTag(8);
                    WriteText(active.PrimPath);
                    WriteBoolean(active.Active);
                    return;
                case SetInstanceableUpdate instanceable:
                    WriteTag(9);
                    WriteText(instanceable.PrimPath);
                    WriteBoolean(instanceable.Instanceable);
                    return;
                case SetVariantSelectionUpdate variant:
                    WriteTag(10);
                    WriteText(variant.PrimPath);
                    WriteText(variant.VariantSetName);
                    WriteTextList(variant.KnownVariants);
                    WriteOptionalText(variant.Selection);
                    return;
                case SetMetadataUpdate metadata:
                    WriteTag(11);
                    WriteText(metadata.PrimPath);
                    WriteText(metadata.Key);
                    WriteMetadataValue(metadata.Value);
                    return;
                case ApiSchemaUpdate apiSchema:
                    WriteTag(12);
                    WriteText(apiSchema.PrimPath);
                    WriteText(apiSchema.SchemaToken);
                    WriteInt64((long)apiSchema.Operation);
                    return;
                case SetPointInstancerOrientationsUpdate orientations:
                    WriteTag(13);
                    WriteText(orientations.PrimPath);
                    WriteInt64(orientations.Orientations.Count);
                    foreach (UsdQuatf value in orientations.Orientations)
                    {
                        WriteSingle(value.Real);
                        WriteSingle(value.X);
                        WriteSingle(value.Y);
                        WriteSingle(value.Z);
                    }
                    return;
                case ReplaceBridgeOverlayUpdate replace:
                    WriteTag(14);
                    WriteText(replace.BridgeRootPath);
                    WriteInt64(replace.Updates.Count);
                    foreach (LiveStageUpdate nested in replace.Updates)
                    {
                        WriteUpdate(nested);
                    }
                    return;
                default:
                    throw new NotSupportedException(
                        $"The live update type '{update.GetType().FullName}' is not supported.");
            }
        }

        internal void WriteText(string value)
        {
            Span<byte> header = stackalloc byte[5];
            header[0] = 1;
            int byteCount = Encoding.UTF8.GetByteCount(value);
            BinaryPrimitives.WriteInt32LittleEndian(header[1..], byteCount);
            hash.AppendData(header);
            byte[] buffer = Encoding.UTF8.GetBytes(value);
            hash.AppendData(buffer);
        }

        internal void WriteOptionalText(string? value)
        {
            if (value is null)
            {
                Span<byte> absent = [0];
                hash.AppendData(absent);
                return;
            }

            WriteText(value);
        }

        internal void WriteInt64(long value)
        {
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
            hash.AppendData(buffer);
        }

        private void WriteTag(byte tag)
        {
            Span<byte> buffer = [tag];
            hash.AppendData(buffer);
        }

        private void WriteBoolean(bool value)
        {
            Span<byte> buffer = [value ? (byte)1 : (byte)0];
            hash.AppendData(buffer);
        }

        private void WriteDouble(double value)
        {
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
            hash.AppendData(buffer);
        }

        private void WriteSingle(float value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
            hash.AppendData(buffer);
        }

        private void WriteOptionalDouble(double? value)
        {
            if (value is not { } present)
            {
                Span<byte> absent = [0];
                hash.AppendData(absent);
                return;
            }

            Span<byte> marker = [1];
            hash.AppendData(marker);
            WriteDouble(present);
        }

        private void WriteTextList(IReadOnlyList<string> values)
        {
            WriteInt64(values.Count);
            foreach (string value in values)
            {
                WriteText(value);
            }
        }

        private void WriteAttributeValue(LiveAttributeValue value)
        {
            WriteInt64((long)value.Kind);
            switch (value.Kind)
            {
                case LiveAttributeKind.Boolean:
                    WriteBoolean(value.Boolean);
                    return;
                case LiveAttributeKind.Int64:
                    WriteInt64(value.Int64Value);
                    return;
                case LiveAttributeKind.Double:
                    WriteDouble(value.DoubleValue);
                    return;
                case LiveAttributeKind.String:
                    WriteText(value.StringValue);
                    return;
                case LiveAttributeKind.Token:
                    WriteText(value.TokenValue);
                    return;
                case LiveAttributeKind.Vec3f:
                    WriteVec3f(value.Vec3f);
                    return;
                case LiveAttributeKind.Matrix4d:
                    WriteMatrix4d(value.Matrix4d);
                    return;
                case LiveAttributeKind.Int32Array:
                    WriteInt64(value.Int32Array.Count);
                    foreach (int element in value.Int32Array)
                    {
                        WriteInt64(element);
                    }
                    return;
                case LiveAttributeKind.FloatArray:
                    WriteInt64(value.FloatArray.Count);
                    foreach (float element in value.FloatArray)
                    {
                        WriteSingle(element);
                    }
                    return;
                case LiveAttributeKind.DoubleArray:
                    WriteInt64(value.DoubleArray.Count);
                    foreach (double element in value.DoubleArray)
                    {
                        WriteDouble(element);
                    }
                    return;
                case LiveAttributeKind.Vec2fArray:
                    WriteInt64(value.Vec2fArray.Count);
                    foreach (UsdVec2f element in value.Vec2fArray)
                    {
                        WriteSingle(element.X);
                        WriteSingle(element.Y);
                    }
                    return;
                case LiveAttributeKind.Vec3fArray:
                    WriteInt64(value.Vec3fArray.Count);
                    foreach (UsdVec3f element in value.Vec3fArray)
                    {
                        WriteVec3f(element);
                    }
                    return;
                case LiveAttributeKind.Color3fArray:
                    WriteInt64(value.Color3fArray.Count);
                    foreach (UsdVec3f element in value.Color3fArray)
                    {
                        WriteVec3f(element);
                    }
                    return;
                case LiveAttributeKind.BooleanArray:
                    WriteInt64(value.BooleanArray.Count);
                    foreach (bool element in value.BooleanArray)
                    {
                        WriteBoolean(element);
                    }
                    return;
                case LiveAttributeKind.TokenArray:
                    WriteTextList(value.TokenArray);
                    return;
                case LiveAttributeKind.StringArray:
                    WriteTextList(value.StringArray);
                    return;
                default:
                    throw new NotSupportedException(
                        $"The attribute value kind '{value.Kind}' is not supported.");
            }
        }

        private void WriteMetadataValue(LiveMetadataValue value)
        {
            WriteInt64((long)value.Kind);
            switch (value.Kind)
            {
                case LiveMetadataKind.Boolean:
                    WriteBoolean(value.Boolean);
                    return;
                case LiveMetadataKind.Int64:
                    WriteInt64(value.Int64Value);
                    return;
                case LiveMetadataKind.Double:
                    WriteDouble(value.DoubleValue);
                    return;
                case LiveMetadataKind.String:
                    WriteText(value.StringValue);
                    return;
                default:
                    throw new NotSupportedException(
                        $"The metadata value kind '{value.Kind}' is not supported.");
            }
        }

        private void WriteVec3f(UsdVec3f value)
        {
            WriteSingle(value.X);
            WriteSingle(value.Y);
            WriteSingle(value.Z);
        }

        private void WriteMatrix4d(UsdMatrix4d value)
        {
            foreach (double element in value.ToArray())
            {
                WriteDouble(element);
            }
        }
    }
}
