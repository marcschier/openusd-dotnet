// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Viewer;

internal static class ViewerNumericComponents
{
    internal static bool TryParse(ReadOnlySpan<char> text, Span<double> components, out string error)
    {
        int count = 0;
        while (!text.IsEmpty)
        {
            if (IsSeparator(text[0]))
            {
                text = text[1..];
                continue;
            }
            int length = 1;
            while (length < text.Length && !IsSeparator(text[length]))
            {
                length++;
            }
            if (count == components.Length)
            {
                error = FormattableString.Invariant($"Enter exactly {components.Length} components.");
                return false;
            }
            if (!double.TryParse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture,
                out double component) || !double.IsFinite(component))
            {
                error = "Every component must be a finite number.";
                return false;
            }
            components[count++] = component;
            text = text[length..];
        }
        if (count != components.Length)
        {
            error = FormattableString.Invariant($"Enter exactly {components.Length} components.");
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool IsSeparator(char character) =>
        char.IsWhiteSpace(character) || character is '(' or ')' or '[' or ']' or ',' or ';';
}
