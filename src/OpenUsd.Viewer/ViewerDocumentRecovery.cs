// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Editing;

namespace OpenUsd.Viewer;

internal sealed record ViewerRecoveryBinding(
    string SourceIdentity,
    string SourceFingerprint,
    string ReviewIdentity,
    string AssetAnchorIdentity);

internal sealed record ViewerRecoveryAdmission(bool CanOffer, string Message);

internal sealed class ViewerDocumentRecovery
{
    internal const int CurrentVersion = 1;
    internal const int MaximumPayloadBytes = 8 * 1024 * 1024;
    private readonly ViewerRecoveryBinding _binding;
    private readonly string _payloadFormat;
    private readonly byte[] _payload;

    internal ViewerDocumentRecovery(
        int version,
        ViewerRecoveryBinding binding,
        string payloadFormat,
        ViewerDocumentLayerRole role,
        ReadOnlySpan<byte> payload)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ValidateBinding(binding);
        ValidateIdentity(payloadFormat, nameof(payloadFormat), 128);
        if (role != ViewerDocumentLayerRole.Review)
        {
            throw new ArgumentException(
                "Recovery may contain only review opinions, never source or simulation.", nameof(role));
        }
        ArgumentOutOfRangeException.ThrowIfGreaterThan(payload.Length, MaximumPayloadBytes);
        if (payload.IsEmpty)
        {
            throw new ArgumentException(
                "A recovery checkpoint must contain an explicit native layer payload.", nameof(payload));
        }
        Version = version;
        _binding = binding with { SourceFingerprint = binding.SourceFingerprint.ToUpperInvariant() };
        _payloadFormat = payloadFormat;
        _payload = payload.ToArray();
    }

    internal int Version { get; }

    internal int PayloadBytes => _payload.Length;

    internal ViewerRecoveryBinding Binding => _binding;

    internal string PayloadFormat => _payloadFormat;
    internal UsdReviewDocument? NativeDocument { get; private init; }

    internal byte[] CopyPayload() => [.. _payload];

    internal static ViewerDocumentRecovery FromNative(UsdReviewDocument document, string key) =>
        new(CurrentVersion,
            new ViewerRecoveryBinding(Path.GetFullPath(document.SourceRootPath), document.SourceFingerprint,
                key, document.AssetAnchor),
            "URD1", ViewerDocumentLayerRole.Review, document.CopyBytes())
        {
            NativeDocument = document
        };

    internal ViewerRecoveryAdmission Validate(ViewerRecoveryBinding expected, string supportedPayloadFormat)
    {
        ValidateBinding(expected);
        ValidateIdentity(supportedPayloadFormat, nameof(supportedPayloadFormat), 128);
        if (Version != CurrentVersion || _payloadFormat != supportedPayloadFormat)
        {
            return new ViewerRecoveryAdmission(
                false, "This recovery version or native payload format is unsupported.");
        }
        if (_binding.SourceIdentity != expected.SourceIdentity ||
            !string.Equals(_binding.SourceFingerprint, expected.SourceFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return new ViewerRecoveryAdmission(
                false, "The source or dependency fingerprint changed; do not replay recovery.");
        }
        if (_binding.ReviewIdentity != expected.ReviewIdentity)
        {
            return new ViewerRecoveryAdmission(
                false, "The checkpoint belongs to another review document or target lineage.");
        }
        if (_binding.AssetAnchorIdentity != expected.AssetAnchorIdentity)
        {
            return new ViewerRecoveryAdmission(
                false, "The asset-resolution context changed; recovery requires reconciliation.");
        }
        return new ViewerRecoveryAdmission(
            true, "Recovery may be offered. Consent and native target validation are still required before replay.");
    }

    private static void ValidateBinding(ViewerRecoveryBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ValidateIdentity(binding.SourceIdentity, nameof(binding.SourceIdentity));
        ValidateIdentity(binding.ReviewIdentity, nameof(binding.ReviewIdentity));
        ValidateIdentity(binding.AssetAnchorIdentity, nameof(binding.AssetAnchorIdentity));
        ArgumentNullException.ThrowIfNull(binding.SourceFingerprint);
        if (binding.SourceFingerprint.Length != 64 || !binding.SourceFingerprint.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "Recovery requires a complete SHA256 source/dependency fingerprint.", nameof(binding));
        }
    }

    private static void ValidateIdentity(string value, string name, int maximumLength = 4096)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > maximumLength || value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Recovery identity is malformed or exceeds its safety bound.", name);
        }
    }
}
