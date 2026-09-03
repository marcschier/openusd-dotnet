// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Protocol;

/// <summary>
/// Bounded checks for the identifiers and paths this package accepts before it hands a value to
/// <c>OpenUsd.LiveAuthoring</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every bound is read from the public <see cref="LiveAuthoringValidation"/> constants rather than
/// duplicated as a literal, so the wire model cannot drift from the authoring layer. The authoring
/// layer remains the authority: these checks exist so an untrusted frame is rejected with a bounded
/// protocol error before an oversized value reaches a constructor that would throw instead.
/// </para>
/// <para>
/// This is deliberately not a copy of the authoring grammar. Attribute names, variant names, schema
/// tokens, and value payloads are validated by the authoring types themselves, and the decoder
/// converts the resulting <see cref="ArgumentException"/> into a protocol error.
/// </para>
/// </remarks>
internal static class BridgeValidation
{
    internal static void ValidateOpaqueIdentity(
        string? value,
        string parameterName,
        string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{description} cannot be empty.", parameterName);
        }
        if (value.Length > LiveAuthoringValidation.MaxOpaqueIdLength)
        {
            throw new ArgumentException(
                $"{description} cannot exceed {LiveAuthoringValidation.MaxOpaqueIdLength} characters.",
                parameterName);
        }
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"{description} cannot contain a null character.",
                parameterName);
        }

        // Checked before anything encodes, hashes, or compares the value. The default UTF-8 encoder
        // replaces an unpaired surrogate with U+FFFD rather than failing, so two identifiers that
        // differ only in one would produce the same bytes on the wire, the same idempotency key, and
        // the same replay fingerprint — a silently dropped edit rather than a rejected one.
        if (!LiveAuthoringValidation.IsWellFormedUtf16(value))
        {
            throw new ArgumentException(
                $"{description} cannot contain an unpaired surrogate: such a value has no UTF-8 " +
                "encoding, so two different identifiers would hash and compare as one.",
                parameterName);
        }
    }

    internal static void ValidateOptionalCorrelationId(string? value, string parameterName)
    {
        if (value is null)
        {
            return;
        }

        ValidateOpaqueIdentity(value, parameterName, "A correlation identifier");
    }

    internal static void ValidateBridgeRootPath(string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("A bridge root path cannot be empty.", parameterName);
        }
        if (value.Length > LiveAuthoringValidation.MaxPathLength)
        {
            throw new ArgumentException(
                "A bridge root path cannot exceed " +
                $"{LiveAuthoringValidation.MaxPathLength} characters.",
                parameterName);
        }
        if (value[0] != '/' || value.Length == 1)
        {
            throw new ArgumentException(
                "A bridge root path must be an absolute prim path below the pseudo-root.",
                parameterName);
        }
        if (value[^1] == '/' || value.Contains("//", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A bridge root path cannot contain an empty path segment.",
                parameterName);
        }
        if (value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A bridge root path cannot contain a null character.",
                parameterName);
        }
    }
}
