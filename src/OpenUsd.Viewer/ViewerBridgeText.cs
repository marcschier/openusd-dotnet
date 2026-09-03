// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace OpenUsd.Viewer;

/// <summary>
/// Bounds and scrubs every string that crosses the bridge seam before it can reach a label,
/// a tooltip, or the status bar.
/// </summary>
/// <remarks>
/// <para>
/// Two rules, both applied unconditionally. Userinfo, query, and fragment are removed from
/// anything that looks like a URL, because the most common way a credential escapes into a
/// user interface is inside an endpoint an error message quoted back, and a bearer token is
/// as likely to sit in a query as in the authority. Length is capped, because a status bar
/// that grows with a provider's error text stops being a status bar.
/// </para>
/// <para>
/// It is public for the same reason <see cref="ViewerBridgeEndpoint"/> is: an integration
/// package that adapts a real transport has to apply this rule at its own boundary, before a
/// transport's exception text can reach a public status snapshot, and two independent
/// implementations of a redaction rule is one too many.
/// </para>
/// </remarks>
public static class ViewerBridgeText
{
    /// <summary>Scrubs and truncates <paramref name="value"/>, collapsing blanks to null.</summary>
    public static string? Bound(string? value, int maxLength = ViewerBridgeLimits.MaxTextLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string scrubbed = ScrubUserInfo(value.Trim());
        if (scrubbed.Length <= maxLength)
        {
            return scrubbed;
        }
        return string.Concat(scrubbed.AsSpan(0, maxLength - 1), "\u2026");
    }

    /// <summary>Describes a provider failure without exposing caller-controlled exception text.</summary>
    public static string Describe(string operation, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(exception);
        return $"{operation} failed: {exception.GetType().Name}.";
    }

    /// <summary>
    /// Removes <c>user:password@</c> and any query or fragment from every URL in
    /// <paramref name="value"/>.
    /// </summary>
    private static string ScrubUserInfo(string value)
    {
        int marker = value.IndexOf("://", StringComparison.Ordinal);
        if (marker < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        int index = 0;
        while (marker >= 0)
        {
            int authorityStart = marker + 3;
            int urlEnd = authorityStart;
            while (urlEnd < value.Length && !IsUrlTerminator(value[urlEnd]))
            {
                urlEnd++;
            }

            int authorityEnd = authorityStart;
            while (authorityEnd < urlEnd && value[authorityEnd] is not ('/' or '?' or '#'))
            {
                authorityEnd++;
            }

            int pathEnd = authorityEnd;
            while (pathEnd < urlEnd && value[pathEnd] is not ('?' or '#'))
            {
                pathEnd++;
            }

            builder.Append(value, index, authorityStart - index);
            int at = -1;
            for (int scan = authorityEnd - 1; scan >= authorityStart; scan--)
            {
                if (value[scan] == '@')
                {
                    at = scan;
                    break;
                }
            }

            int start = at >= 0 ? at + 1 : authorityStart;
            builder.Append(value, start, pathEnd - start);
            index = urlEnd;
            marker = index < value.Length
                ? value.IndexOf("://", index, StringComparison.Ordinal)
                : -1;
        }

        builder.Append(value, index, value.Length - index);
        return builder.ToString();
    }

    private static bool IsUrlTerminator(char value) =>
        char.IsWhiteSpace(value) || value is '"' or '\'' or '<' or '>';
}
