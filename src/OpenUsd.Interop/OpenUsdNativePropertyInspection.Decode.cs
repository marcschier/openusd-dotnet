// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;

namespace OpenUsd.Interop;

public static unsafe partial class OpenUsdNativeRuntime
{
    private static readonly UTF8Encoding PropertyStrictUtf8 = new(false, true);

    internal static OpenUsdNativePropertySnapshot DecodePropertySnapshot(
        NativePropertyView view, OpenUsdNativePropertyLimits limits)
    {
        if (limits.StructSize != sizeof(OpenUsdNativePropertyLimits) || limits.Version != 1 ||
            limits.MaximumPropertyCount > 65_536 || limits.MaximumTextBytes > 16 * 1024 * 1024 ||
            limits.PreviewElements > 16 || limits.TimeSamplePreview > 16 || limits.TargetPreview > 16 ||
            limits.MaximumMetadataWork > 1_048_576 || limits.MaximumPreviewTextBytes > 65_536 ||
            limits.Reserved != 0 || view.StructSize != sizeof(NativePropertyView) || view.Version != 1 ||
            view.IsComplete > 1 || view.TimeSampled > 1 || !double.IsFinite(view.TimeCode) ||
            (view.TimeSampled == 0 && view.TimeCode != 0) || view.Reserved != 0 ||
            view.MetadataWork > limits.MaximumMetadataWork)
        {
            throw InvalidPropertySnapshot("invalid version, limits or header");
        }
        ValidatePropertyBuffer(view.Entries, view.EntriesSize, view.EntryCount, limits.MaximumPropertyCount, 8);
        ValidatePropertyBuffer(view.Assets, view.AssetsSize, view.AssetCount,
            limits.MaximumPropertyCount * limits.PreviewElements, 4);
        ValidatePropertyBuffer(view.Times, view.TimesSize, view.TimeCount,
            limits.MaximumPropertyCount * limits.TimeSamplePreview, 8);
        ValidatePropertyBuffer(view.Data, view.DataSize, view.DataSize, limits.MaximumTextBytes, 1);
        uint maximumStrings = 1 + (limits.MaximumPropertyCount *
            (6 + (limits.PreviewElements * 5) + limits.TargetPreview));
        ValidatePropertyBuffer(view.Offsets, view.OffsetsSize, view.StringCount, maximumStrings, 4);
        if (view.StringCount < 1 + (view.EntryCount * 6) || view.StringCount > view.DataSize)
        {
            throw InvalidPropertySnapshot("the string table cannot cover the property records");
        }

        var data = new ReadOnlySpan<byte>(view.Data, (int)view.DataSize);
        var offsets = new ReadOnlySpan<uint>(view.Offsets, (int)view.StringCount);
        string[] strings = DecodePropertyStrings(data, offsets);
        if (!OpenUsdIdentifierValidation.IsAbsolutePrimPath(strings[0]))
        {
            throw InvalidPropertySnapshot("invalid selected prim path");
        }
        var records = new ReadOnlySpan<OpenUsdNativePropertyEntryRecord>(view.Entries, (int)view.EntryCount);
        var assetRecords = new ReadOnlySpan<OpenUsdNativePropertyAssetRecord>(view.Assets, (int)view.AssetCount);
        var times = new ReadOnlySpan<double>(view.Times, (int)view.TimeCount);
        var entries = new OpenUsdNativePropertyEntry[records.Length];
        int stringCursor = 1;
        int assetCursor = 0;
        int timeCursor = 0;
        bool complete = true;
        for (int index = 0; index < records.Length; index++)
        {
            OpenUsdNativePropertyEntryRecord record = records[index];
            if (strings.Length - stringCursor < 6 ||
                record.Kind > 1 || (record.Flags & ~31u) != 0 || record.Variability > 1 ||
                record.ResolveSource > 6 || record.ValueState > 3 || record.Reserved != 0 ||
                record.Name != stringCursor || record.Type != stringCursor + 1 ||
                record.SourceLayer != stringCursor + 2 || record.SourcePath != stringCursor + 3 ||
                record.TargetSourceLayer != stringCursor + 4 || record.TargetSourcePath != stringCursor + 5 ||
                record.AssetOffset != assetCursor || record.AssetCount > limits.PreviewElements ||
                record.AssetCount > assetRecords.Length - assetCursor ||
                (record.Flags & 24) == 16)
            {
                throw InvalidPropertySnapshot("invalid property flags, source fields or record ranges");
            }
            if (index > 0 && PropertyText(data, offsets, records[index - 1].Name)
                .SequenceCompareTo(PropertyText(data, offsets, record.Name)) >= 0)
            {
                throw InvalidPropertySnapshot("property names are not unique and in canonical UTF-8 order");
            }
            string name = TakePropertyString(strings, ref stringCursor);
            string type = TakePropertyString(strings, ref stringCursor);
            if (!IsPropertyName(name) || (record.Kind == 0 &&
                    (type.Length == 0 || ((record.Flags & 4) != 0) != type.EndsWith("[]", StringComparison.Ordinal))) ||
                (record.Kind == 1 && type.Length != 0))
            {
                throw InvalidPropertySnapshot("invalid property name or explicit type");
            }
            OpenUsdNativePropertySource? source = DecodePropertySource(strings, ref stringCursor);
            OpenUsdNativePropertySource? targetSource = DecodePropertySource(strings, ref stringCursor);
            ValidatePropertyPreview(record.Value, stringCursor, limits.PreviewElements);
            string[] elements = TakePropertyStrings(strings, ref stringCursor, record.Value.Count);
            foreach (string element in elements)
            {
                if (PropertyStrictUtf8.GetByteCount(element) > limits.MaximumPreviewTextBytes)
                {
                    throw InvalidPropertySnapshot("value text exceeds its per-element bound");
                }
            }
            var assets = new OpenUsdNativePropertyAsset[(int)record.AssetCount];
            for (int assetIndex = 0; assetIndex < assets.Length; assetIndex++)
            {
                OpenUsdNativePropertyAssetRecord asset = assetRecords[assetCursor++];
                if (asset.StringOffset != stringCursor || asset.Missing > 2)
                {
                    throw InvalidPropertySnapshot("invalid asset fields");
                }
                string authored = TakePropertyString(strings, ref stringCursor);
                string evaluated = TakePropertyString(strings, ref stringCursor);
                string resolved = TakePropertyString(strings, ref stringCursor);
                string anchor = TakePropertyString(strings, ref stringCursor);
                if (PropertyStrictUtf8.GetByteCount(authored) > limits.MaximumPreviewTextBytes ||
                    PropertyStrictUtf8.GetByteCount(evaluated) > limits.MaximumPreviewTextBytes ||
                    PropertyStrictUtf8.GetByteCount(resolved) > limits.MaximumPreviewTextBytes ||
                    assetIndex >= elements.Length || elements[assetIndex] != authored ||
                    (asset.Missing == 0 && resolved.Length == 0) ||
                    (asset.Missing == 1 && (resolved.Length != 0 || anchor.Length == 0 ||
                        (authored.Length == 0 && evaluated.Length == 0))))
                {
                    throw InvalidPropertySnapshot("inconsistent or unbounded asset path fields");
                }
                assets[assetIndex] = new(authored, evaluated, resolved, anchor,
                    asset.Missing == 2 ? null : asset.Missing != 0);
            }
            ValidatePropertyPreview(record.TimeSamples, timeCursor, limits.TimeSamplePreview);
            if (record.TimeSamples.Count > times.Length - timeCursor)
            {
                throw InvalidPropertySnapshot("invalid time sample range");
            }
            double[] sampleTimes = times.Slice(timeCursor, (int)record.TimeSamples.Count).ToArray();
            timeCursor += sampleTimes.Length;
            for (int sample = 0; sample < sampleTimes.Length; sample++)
            {
                if (!double.IsFinite(sampleTimes[sample]) ||
                    (sample > 0 && sampleTimes[sample - 1] >= sampleTimes[sample]))
                {
                    throw InvalidPropertySnapshot("sample times are not finite and strictly increasing");
                }
            }
            ValidatePropertyPreview(record.Targets, stringCursor, limits.TargetPreview);
            string[] targets = TakePropertyStrings(strings, ref stringCursor, record.Targets.Count);
            var uniqueTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (string target in targets)
            {
                if (!IsInspectionTargetPath(target) || !uniqueTargets.Add(target))
                {
                    throw InvalidPropertySnapshot("invalid or duplicate composed target path");
                }
            }
            if ((assets.Length != 0 && (assets.Length != elements.Length || (type != "asset" && type != "asset[]"))) ||
                (record.Kind == 1 && (record.Value.TotalCount != 0 || record.TimeSamples.TotalCount != 0 ||
                    record.Value.Status != 0 || record.TimeSamples.Status != 0 || record.AssetCount != 0 ||
                    (record.Flags & 28) != 0 || record.ResolveSource != 0 ||
                    record.ValueState != 0 || source is not null)) ||
                (record.Kind == 0 && (record.Flags & 4) == 0 &&
                    record.Value.TotalCount != ulong.MaxValue && record.Value.TotalCount > 1) ||
                (record.ValueState == 0 && (record.ResolveSource != 0 || elements.Length != 0)) ||
                (record.ResolveSource == 6 && (record.ValueState != 3 || record.Value.Status != 2)) ||
                (source is not null && record.ResolveSource is not (2 or 3) && record.ValueState != 2))
            {
                throw InvalidPropertySnapshot("inconsistent property kind, value state or provenance");
            }
            complete &= record.Value.Status == 0 && record.TimeSamples.Status == 0 && record.Targets.Status == 0;
            entries[index] = new(record, name, type, source, targetSource, elements, sampleTimes, targets, assets);
        }
        if (stringCursor != strings.Length || assetCursor != assetRecords.Length ||
            timeCursor != times.Length || complete != (view.IsComplete != 0))
        {
            throw InvalidPropertySnapshot("unreferenced data or inconsistent completeness");
        }
        return new(strings[0], view.TimeSampled != 0 ? view.TimeCode : null, view.ChangeSerial,
            complete, (int)view.DataSize, (int)view.MetadataWork, entries);
    }

    private static void ValidatePropertyPreview(OpenUsdNativePropertyPreview preview, int cursor, uint maximum)
    {
        if (preview.Offset != cursor || preview.Count > maximum || preview.Status > 3 || preview.Reason > 9 ||
            (preview.TotalCount != ulong.MaxValue && preview.Count > preview.TotalCount) ||
            (preview.Status == 0 && (preview.Reason != 0 || preview.TotalCount != preview.Count)) ||
            (preview.Status != 0 && preview.Reason == 0) ||
            (preview.Status == 1 && preview.TotalCount == ulong.MaxValue))
        {
            throw InvalidPropertySnapshot("invalid preview status, reason, total or bounded range");
        }
    }

    private static OpenUsdNativePropertySource? DecodePropertySource(string[] strings, ref int cursor)
    {
        string layer = TakePropertyString(strings, ref cursor);
        string path = TakePropertyString(strings, ref cursor);
        if (layer.Length == 0 && path.Length == 0)
        {
            return null;
        }
        if (layer.Length == 0 || !IsInspectionSourcePath(path))
        {
            throw InvalidPropertySnapshot("incomplete native winning source provenance");
        }
        return new(layer, path);
    }

    private static bool IsPropertyName(string name) =>
        !name.Contains('/', StringComparison.Ordinal) &&
        OpenUsdIdentifierValidation.IsAbsolutePrimPath("/" + name.Replace(':', '/'));

    private static bool IsInspectionTargetPath(string path)
    {
        int separator = path.IndexOf('.', StringComparison.Ordinal);
        return separator < 0
            ? path == "/" || OpenUsdIdentifierValidation.IsAbsolutePrimPath(path)
            : OpenUsdIdentifierValidation.IsAbsolutePrimPath(path[..separator]) &&
                IsPropertyName(path[(separator + 1)..]);
    }

    private static bool IsInspectionSourcePath(string path)
    {
        int separator = path.LastIndexOf('.');
        if (separator <= 0 || !IsPropertyName(path[(separator + 1)..]))
        {
            return false;
        }
        ReadOnlySpan<char> prim = path.AsSpan(0, separator);
        if (!prim.Contains('{'))
        {
            return OpenUsdIdentifierValidation.IsAbsolutePrimPath(path[..separator]);
        }
        var ordinary = new StringBuilder(prim.Length);
        for (int index = 0; index < prim.Length; index++)
        {
            if (prim[index] != '{')
            {
                ordinary.Append(prim[index]);
                continue;
            }
            int end = prim[index..].IndexOf('}');
            if (end <= 1)
            {
                return false;
            }
            ReadOnlySpan<char> selection = prim.Slice(index + 1, end - 1);
            int equals = selection.IndexOf('=');
            if (equals <= 0 || selection.Contains('{') ||
                !IsPropertyName(selection[..equals].ToString()))
            {
                return false;
            }
            index += end;
        }
        return OpenUsdIdentifierValidation.IsAbsolutePrimPath(ordinary.ToString());
    }

    private static string TakePropertyString(string[] strings, ref int cursor)
    {
        if ((uint)cursor >= (uint)strings.Length)
        {
            throw InvalidPropertySnapshot("missing property string");
        }
        return strings[cursor++];
    }

    private static string[] TakePropertyStrings(string[] strings, ref int cursor, uint count)
    {
        if (count > strings.Length - cursor)
        {
            throw InvalidPropertySnapshot("string preview exceeds its buffer");
        }
        var result = new string[(int)count];
        Array.Copy(strings, cursor, result, 0, result.Length);
        cursor += result.Length;
        return result;
    }

    private static ReadOnlySpan<byte> PropertyText(ReadOnlySpan<byte> data, ReadOnlySpan<uint> offsets, uint index)
    {
        int start = (int)offsets[(int)index];
        return data[start..].Slice(0, data[start..].IndexOf((byte)0));
    }

    private static void ValidatePropertyBuffer<T>(
        T* pointer, nuint byteSize, nuint count, nuint maximumCount, nuint alignment)
        where T : unmanaged
    {
        if (count > maximumCount || byteSize != count * (nuint)sizeof(T) ||
            (count != 0 && pointer == null) || (nuint)pointer % alignment != 0 ||
            byteSize > nuint.MaxValue - (nuint)pointer)
        {
            throw InvalidPropertySnapshot("invalid pointer, alignment, count or buffer extent");
        }
    }

    private static string[] DecodePropertyStrings(ReadOnlySpan<byte> data, ReadOnlySpan<uint> offsets)
    {
        var strings = new string[offsets.Length];
        uint expected = 0;
        for (int index = 0; index < offsets.Length; index++)
        {
            if (offsets[index] != expected || expected >= data.Length)
            {
                throw InvalidPropertySnapshot("noncanonical string offsets");
            }
            ReadOnlySpan<byte> remaining = data[(int)expected..];
            int terminator = remaining.IndexOf((byte)0);
            if (terminator < 0)
            {
                throw InvalidPropertySnapshot("unterminated string");
            }
            try
            {
                strings[index] = PropertyStrictUtf8.GetString(remaining[..terminator]);
            }
            catch (DecoderFallbackException)
            {
                throw InvalidPropertySnapshot("malformed UTF-8");
            }
            expected += (uint)terminator + 1;
        }
        if (expected != data.Length)
        {
            throw InvalidPropertySnapshot("trailing string data");
        }
        return strings;
    }

    private static OpenUsdNativeException InvalidPropertySnapshot(string reason) =>
        new(OpenUsdNativeStatus.NativeError, $"Invalid native property inspection snapshot: {reason}.");
}
