// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Explicit scene-ingestion choices for one hdSilk sync request.
/// </summary>
/// <remarks>
/// The current native/USD seam can only toggle proxy, render, and guide
/// purposes; default-purpose inheritance remains always included, so masks that
/// exclude <see cref="RenderPurpose.Default"/> are rejected explicitly instead
/// of falling back silently.
///
/// Material binding currently accepts one exact purpose token. The explicit
/// request path is currently certified only for <c>full</c> and
/// <c>preview</c>; the legacy viewport path continues to use the historical
/// empty all-purpose token through the older sync overload.
/// </remarks>
public sealed class SilkSceneIngestionOptions
{
    private const RenderPurpose SupportedPurposes =
        RenderPurpose.Default |
        RenderPurpose.Proxy |
        RenderPurpose.Render |
        RenderPurpose.Guide;

    /// <summary>Creates validated explicit scene-ingestion choices.</summary>
    public SilkSceneIngestionOptions(
        RenderPurpose includedPurposes,
        string materialBindingPurpose)
    {
        ValidateIncludedPurposes(includedPurposes);
        IncludedPurposes = includedPurposes;
        MaterialBindingPurpose = NormalizeMaterialBindingPurpose(materialBindingPurpose);
    }

    /// <summary>Gets the included standard USD purpose mask.</summary>
    public RenderPurpose IncludedPurposes { get; }

    /// <summary>Gets the single requested material-binding purpose token.</summary>
    /// <remarks>
    /// The all-purpose binding is represented as the empty string, matching the
    /// underlying SDK token value.
    /// </remarks>
    public string MaterialBindingPurpose { get; }

    internal static void ValidateIncludedPurposes(RenderPurpose includedPurposes)
    {
        if ((includedPurposes & ~SupportedPurposes) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(includedPurposes),
                "Only the standard default, proxy, render, and guide purpose bits are supported.");
        }

        if ((includedPurposes & RenderPurpose.Default) == 0)
        {
            throw new NotSupportedException(
                "hdSilk cannot currently exclude default-purpose inheritance through the native USD seam.");
        }
    }

    internal static string NormalizeMaterialBindingPurpose(
        string materialBindingPurpose)
    {
        ArgumentNullException.ThrowIfNull(materialBindingPurpose);
        if (string.Equals(
            materialBindingPurpose,
            "allPurpose",
            StringComparison.Ordinal))
        {
            materialBindingPurpose = string.Empty;
        }

        if (materialBindingPurpose.Length == 0)
        {
            throw new NotSupportedException(
                "Explicit hdSilk scene ingestion cannot yet request the empty/allPurpose material binding token.");
        }
        if (materialBindingPurpose.Length > 128)
        {
            throw new ArgumentOutOfRangeException(
                nameof(materialBindingPurpose),
                "The material-binding purpose must be 128 characters or fewer.");
        }

        foreach (char ch in materialBindingPurpose)
        {
            if (ch == '\0' || char.IsWhiteSpace(ch))
            {
                throw new ArgumentException(
                    "The material-binding purpose must be empty or a single non-whitespace token.",
                    nameof(materialBindingPurpose));
            }
        }

        if (!string.Equals(materialBindingPurpose, "full", StringComparison.Ordinal) &&
            !string.Equals(materialBindingPurpose, "preview", StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                "Explicit hdSilk scene ingestion currently supports only the 'full' and 'preview' " +
                "material-binding purpose tokens.");
        }

        return materialBindingPurpose;
    }
}
