// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace OpenUsd.Interop;

/// <summary>
/// Packs a list of managed strings into one contiguous, null-terminated UTF-8 buffer plus
/// an offset table, matching the layout the native ABI expects for
/// <c>openusd_string_list_view</c> inputs. This logic is pure and native-independent so it
/// can be exercised directly by unit tests.
/// </summary>
internal static class NativeStringListPacking
{
    /// <summary>Packs <paramref name="values"/> into a data buffer and matching offset table.</summary>
    internal static (byte[] Data, nuint[] Offsets) Pack(ReadOnlySpan<string> values)
    {
        var offsets = new nuint[values.Length];
        if (values.Length == 0)
        {
            return ([], offsets);
        }

        var data = new List<byte>();
        for (int i = 0; i < values.Length; i++)
        {
            string value = values[i] ??
                throw new ArgumentException("Packed string list entries must not be null.", nameof(values));
            NativeStringValidation.ThrowIfContainsNull(value, nameof(values));
            offsets[i] = (nuint)data.Count;
            data.AddRange(Encoding.UTF8.GetBytes(value));
            data.Add(0);
        }
        return (data.ToArray(), offsets);
    }
}
