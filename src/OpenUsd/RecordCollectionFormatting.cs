// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace OpenUsd;

internal static class RecordCollectionFormatting
{
    public static int SequenceHashCode<T>(IEnumerable<T> values)
    {
        HashCode hash = default;
        foreach (T value in values)
        {
            hash.Add(value);
        }
        return hash.ToHashCode();
    }

    public static string FormatSequence<T>(IEnumerable<T> values)
    {
        var builder = new StringBuilder("[");
        bool first = true;
        foreach (T value in values)
        {
            if (!first)
            {
                builder.Append(", ");
            }
            builder.Append(value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value?.ToString());
            first = false;
        }
        builder.Append(']');
        return builder.ToString();
    }
}
