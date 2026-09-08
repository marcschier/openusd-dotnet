// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Text;

namespace OpenUsd.Interop;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct OpenUsdNativeHierarchyLimits(
    uint StructSize,
    uint Version,
    uint MaximumPrimCount,
    uint MaximumTextBytes,
    uint MaximumDepth,
    uint MaximumVariantSets,
    uint MaximumVariantNames,
    uint MaximumMetadataWork);

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct OpenUsdNativeHierarchyEntryRecord(
    int ParentIndex,
    uint Depth,
    uint ChildCount,
    uint Flags,
    uint StringOffset,
    uint VariantOffset,
    uint VariantCount,
    uint VariantStatus);

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct OpenUsdNativeHierarchyVariantRecord(uint StringOffset, uint VariantCount);

internal sealed record OpenUsdNativeHierarchyVariantSet(string Name, string Selection, string[] VariantNames);

internal sealed record OpenUsdNativeHierarchyEntry(
    OpenUsdNativeHierarchyEntryRecord Values,
    string Path,
    string Name,
    string TypeName,
    string PrototypePath,
    OpenUsdNativeHierarchyVariantSet[] VariantSets);

internal sealed record OpenUsdNativeHierarchySnapshot(
    ulong ChangeSerial,
    bool IsComplete,
    int TextByteCount,
    int MetadataWorkCount,
    OpenUsdNativeHierarchyEntry[] Entries);

public static unsafe partial class OpenUsdNativeRuntime
{
    private static readonly UTF8Encoding HierarchyStrictUtf8 = new(false, true);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeHierarchyView
    {
        internal uint StructSize;
        internal uint Version;
        internal ulong ChangeSerial;
        internal uint IsComplete;
        internal uint MetadataWork;
        internal OpenUsdNativeHierarchyEntryRecord* Entries;
        internal nuint EntriesSize;
        internal nuint EntryCount;
        internal OpenUsdNativeHierarchyVariantRecord* VariantSets;
        internal nuint VariantSetsSize;
        internal nuint VariantSetCount;
        internal byte* Data;
        internal nuint DataSize;
        internal uint* Offsets;
        internal nuint OffsetsSize;
        internal nuint StringCount;
    }

    internal static OpenUsdNativeHierarchySnapshot GetHierarchySnapshot(
        OpenUsdNativeStage stage, OpenUsdNativeHierarchyLimits limits)
    {
        EnsureCompatibleAbi();
        using var lease = new SafeHandleLease(stage);
        var view = new NativeHierarchyView
        {
            StructSize = (uint)sizeof(NativeHierarchyView),
            Version = 1
        };
        Span<byte> errorBytes = stackalloc byte[ErrorBufferSize];
        fixed (byte* errorPointer = errorBytes)
        {
            var error = new NativeErrorBuffer(errorPointer, (nuint)errorBytes.Length);
            OpenUsdNativeStatus status = NativeMethods.StageGetHierarchySnapshot(
                lease.Handle, ref limits, out nint owner, ref view, ref error);
            try
            {
                ThrowIfFailed(status, errorBytes, error);
                if (owner == 0)
                {
                    throw InvalidHierarchy("a successful query has no owner");
                }
                return DecodeHierarchySnapshot(view, limits);
            }
            finally
            {
                if (owner != 0)
                {
                    NativeMethods.HierarchySnapshotRelease(owner);
                }
            }
        }
    }

    internal static OpenUsdNativeHierarchySnapshot DecodeHierarchySnapshot(
        NativeHierarchyView view, OpenUsdNativeHierarchyLimits limits)
    {
        if (limits.StructSize != sizeof(OpenUsdNativeHierarchyLimits) || limits.Version != 1 ||
            limits.MaximumPrimCount > 1_000_000 || limits.MaximumTextBytes > 128 * 1024 * 1024 ||
            limits.MaximumDepth > 1024 || limits.MaximumVariantSets > 65_536 ||
            limits.MaximumVariantNames > 262_144 || limits.MaximumMetadataWork > 16_000_000 ||
            view.StructSize != sizeof(NativeHierarchyView) || view.Version != 1 ||
            view.IsComplete > 1 || view.MetadataWork > limits.MaximumMetadataWork)
        {
            throw InvalidHierarchy("invalid version, limits or header");
        }
        ValidateHierarchyBuffer(view.Entries, view.EntriesSize, view.EntryCount, limits.MaximumPrimCount, 4);
        ValidateHierarchyBuffer(view.VariantSets, view.VariantSetsSize, view.VariantSetCount, limits.MaximumVariantSets, 4);
        ValidateHierarchyBuffer(view.Data, view.DataSize, view.DataSize, limits.MaximumTextBytes, 1);
        uint maximumStrings = (limits.MaximumPrimCount * 4) +
            (limits.MaximumVariantSets * 2) + limits.MaximumVariantNames;
        ValidateHierarchyBuffer(view.Offsets, view.OffsetsSize, view.StringCount, maximumStrings, 4);
        if (view.StringCount < (view.EntryCount * 4) + (view.VariantSetCount * 2) ||
            view.StringCount > view.DataSize)
        {
            throw InvalidHierarchy("the string table cannot cover the declared records");
        }

        string[] strings = DecodeHierarchyStrings(
            new ReadOnlySpan<byte>(view.Data, (int)view.DataSize),
            new ReadOnlySpan<uint>(view.Offsets, (int)view.StringCount));
        var records = new ReadOnlySpan<OpenUsdNativeHierarchyEntryRecord>(view.Entries, (int)view.EntryCount);
        var variantRecords = new ReadOnlySpan<OpenUsdNativeHierarchyVariantRecord>(
            view.VariantSets, (int)view.VariantSetCount);
        var entries = new OpenUsdNativeHierarchyEntry[records.Length];
        var paths = new Dictionary<string, int>(records.Length, StringComparer.Ordinal);
        var childCounts = new int[records.Length];
        var parents = new List<int>();
        int stringCursor = 0;
        int variantCursor = 0;
        uint variantNames = 0;
        bool complete = true;
        bool prototypesStarted = false;
        for (int index = 0; index < records.Length; index++)
        {
            OpenUsdNativeHierarchyEntryRecord record = records[index];
            if (record.Depth == 0 || record.Depth > limits.MaximumDepth ||
                record.ChildCount > view.EntryCount || (record.Flags & ~0x3FFu) != 0 ||
                (record.Flags & 128) != 0 || record.VariantStatus > 1 ||
                record.StringOffset != (uint)stringCursor || record.VariantOffset != (uint)variantCursor ||
                record.VariantCount > view.VariantSetCount - (nuint)variantCursor ||
                (record.VariantStatus == 1 && record.VariantCount != 0))
            {
                throw InvalidHierarchy("invalid entry flags, depth or record ranges");
            }
            while (parents.Count >= record.Depth)
            {
                parents.RemoveAt(parents.Count - 1);
            }
            int parent = parents.Count == 0 ? -1 : parents[^1];
            if (record.Depth != parents.Count + 1 || record.ParentIndex != parent)
            {
                throw InvalidHierarchy("parent indices are not canonical preorder");
            }
            string path = TakeHierarchyString(strings, ref stringCursor);
            string name = TakeHierarchyString(strings, ref stringCursor);
            string type = TakeHierarchyString(strings, ref stringCursor);
            string prototypePath = TakeHierarchyString(strings, ref stringCursor);
            int separator = path.LastIndexOf('/');
            bool isPrototype = (record.Flags & 16) != 0;
            bool inPrototype = (record.Flags & 32) != 0;
            bool isInstance = (record.Flags & 64) != 0;
            if (!OpenUsdIdentifierValidation.IsAbsolutePrimPath(path) ||
                !path.AsSpan(separator + 1).SequenceEqual(name) || !paths.TryAdd(path, index) ||
                (parent < 0 ? separator != 0 : !path.AsSpan(0, separator).SequenceEqual(entries[parent].Path)) ||
                (isPrototype && parent >= 0) ||
                inPrototype != (isPrototype || (parent >= 0 && (records[parent].Flags & 32) != 0)) ||
                (parent < 0 && prototypesStarted && !isPrototype) ||
                (isInstance && (record.Flags & 512) == 0) ||
                isInstance != (prototypePath.Length != 0) ||
                (isInstance && !OpenUsdIdentifierValidation.IsAbsolutePrimPath(prototypePath)))
            {
                throw InvalidHierarchy("invalid path, parent, prototype or instance relationship");
            }
            prototypesStarted |= isPrototype;
            if (parent >= 0)
            {
                childCounts[parent]++;
            }
            complete &= record.VariantStatus == 0;
            OpenUsdNativeHierarchyVariantSet[] variants = record.VariantCount == 0
                ? []
                : new OpenUsdNativeHierarchyVariantSet[(int)record.VariantCount];
            HashSet<string>? setNames = variants.Length == 0 ? null : new(StringComparer.Ordinal);
            for (int variantIndex = 0; variantIndex < variants.Length; variantIndex++)
            {
                OpenUsdNativeHierarchyVariantRecord variant = variantRecords[variantCursor++];
                if (variant.StringOffset != (uint)stringCursor ||
                    variant.VariantCount > limits.MaximumVariantNames - variantNames)
                {
                    throw InvalidHierarchy("invalid variant range or choice budget");
                }
                variantNames += variant.VariantCount;
                string setName = TakeHierarchyString(strings, ref stringCursor);
                string selection = TakeHierarchyString(strings, ref stringCursor);
                if (setName.Length == 0 || !setNames!.Add(setName) ||
                    variant.VariantCount > strings.Length - stringCursor)
                {
                    throw InvalidHierarchy("invalid variant-set name or choices");
                }
                var names = new string[(int)variant.VariantCount];
                var uniqueNames = new HashSet<string>(StringComparer.Ordinal);
                for (int choice = 0; choice < names.Length; choice++)
                {
                    names[choice] = TakeHierarchyString(strings, ref stringCursor);
                    if (names[choice].Length == 0 || !uniqueNames.Add(names[choice]))
                    {
                        throw InvalidHierarchy("empty or duplicate variant choice");
                    }
                }
                variants[variantIndex] = new OpenUsdNativeHierarchyVariantSet(setName, selection, names);
            }
            entries[index] = new OpenUsdNativeHierarchyEntry(record, path, name, type, prototypePath, variants);
            parents.Add(index);
        }
        if (stringCursor != strings.Length || variantCursor != variantRecords.Length ||
            complete != (view.IsComplete != 0))
        {
            throw InvalidHierarchy("unreferenced data or inconsistent completeness");
        }
        var referencedPrototypes = new bool[entries.Length];
        for (int index = 0; index < entries.Length; index++)
        {
            OpenUsdNativeHierarchyEntry entry = entries[index];
            if (entry.Values.ChildCount != childCounts[index])
            {
                throw InvalidHierarchy("child counts do not describe the complete hierarchy");
            }
            if (entry.PrototypePath.Length != 0)
            {
                if (!paths.TryGetValue(entry.PrototypePath, out int target) || (records[target].Flags & 16) == 0)
                {
                    throw InvalidHierarchy("an instance refers to a missing prototype root");
                }
                referencedPrototypes[target] = true;
            }
        }
        for (int index = 0; index < entries.Length; index++)
        {
            if ((records[index].Flags & 16) != 0 && !referencedPrototypes[index])
            {
                throw InvalidHierarchy("an unreferenced prototype was included");
            }
        }
        return new OpenUsdNativeHierarchySnapshot(
            view.ChangeSerial, complete, (int)view.DataSize, (int)view.MetadataWork, entries);
    }

    private static void ValidateHierarchyBuffer<T>(
        T* pointer, nuint byteSize, nuint count, nuint maximumCount, nuint alignment)
        where T : unmanaged
    {
        if (count > maximumCount || byteSize != count * (nuint)sizeof(T) ||
            (count != 0 && pointer == null) || (nuint)pointer % alignment != 0 ||
            byteSize > nuint.MaxValue - (nuint)pointer)
        {
            throw InvalidHierarchy("invalid buffer pointer, alignment, count or extent");
        }
    }

    private static string[] DecodeHierarchyStrings(ReadOnlySpan<byte> data, ReadOnlySpan<uint> offsets)
    {
        var strings = new string[offsets.Length];
        uint expected = 0;
        for (int index = 0; index < offsets.Length; index++)
        {
            if (offsets[index] != expected || expected >= data.Length)
            {
                throw InvalidHierarchy("noncanonical string offsets");
            }
            ReadOnlySpan<byte> remaining = data[(int)expected..];
            int terminator = remaining.IndexOf((byte)0);
            if (terminator < 0)
            {
                throw InvalidHierarchy("unterminated string");
            }
            try
            {
                strings[index] = HierarchyStrictUtf8.GetString(remaining[..terminator]);
            }
            catch (DecoderFallbackException)
            {
                throw InvalidHierarchy("malformed UTF-8");
            }
            expected += (uint)terminator + 1;
        }
        if (expected != data.Length)
        {
            throw InvalidHierarchy("trailing string data");
        }
        return strings;
    }

    private static string TakeHierarchyString(string[] strings, ref int cursor)
    {
        if ((uint)cursor >= (uint)strings.Length)
        {
            throw InvalidHierarchy("a string range exceeds its buffer");
        }
        return strings[cursor++];
    }

    private static OpenUsdNativeException InvalidHierarchy(string detail) =>
        new(OpenUsdNativeStatus.NativeError, $"The native runtime returned an invalid hierarchy snapshot: {detail}.");
}
