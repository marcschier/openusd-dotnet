// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace OpenUsd.Physics.Extraction;

/// <summary>
/// Rejects extraction pages that are not internally consistent before anything reads them.
/// </summary>
/// <remarks>
/// The validator is deliberately exhaustive: a page crosses a native boundary, so every offset,
/// count, and cross-section index is checked with widened arithmetic before the reader is allowed
/// to index into the bytes. After validation the reader can index without further bounds logic.
/// </remarks>
internal static class UsdPhysicsExtractionPageValidator
{
    /// <summary>Validates one candidate extraction page.</summary>
    /// <param name="bytes">The candidate page bytes.</param>
    /// <exception cref="UsdPhysicsExtractionException">The bytes are not a usable page.</exception>
    internal static void Validate(ReadOnlySpan<byte> bytes)
    {
        ValidateHeader(bytes);

        Span<long> offsets = stackalloc long[PhysicsExtractAbi.SectionCount];
        Span<long> counts = stackalloc long[PhysicsExtractAbi.SectionCount];
        ValidateSections(bytes, offsets, counts);
        ValidateStrings(bytes, offsets, counts);
        ValidateObjects(bytes, offsets, counts);
        ValidateProperties(bytes, offsets, counts);
        ValidateRelationships(bytes, offsets, counts);
        ValidateTargetsAndDiagnostics(bytes, offsets, counts);
    }

    private static void ValidateHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < PhysicsExtractAbi.HeaderBytes)
        {
            throw Invalid(
                $"The page is {bytes.Length} bytes, which is shorter than the " +
                $"{PhysicsExtractAbi.HeaderBytes} byte header.");
        }

        ulong magic = ReadU64(bytes, PhysicsExtractAbi.OffsetMagic);
        if (magic != PhysicsExtractAbi.PageMagic)
        {
            throw Invalid(
                $"The page magic 0x{magic:X16} does not match the expected " +
                $"0x{PhysicsExtractAbi.PageMagic:X16}.");
        }

        uint abiVersion = ReadU32(bytes, PhysicsExtractAbi.OffsetAbiVersion);
        if (abiVersion != PhysicsExtractAbi.AbiVersion)
        {
            throw Invalid(
                $"The page declares ABI version {abiVersion} but this build expects " +
                $"{PhysicsExtractAbi.AbiVersion}.");
        }

        uint headerSize = ReadU32(bytes, PhysicsExtractAbi.OffsetHeaderSize);
        if (headerSize != PhysicsExtractAbi.HeaderBytes)
        {
            throw Invalid(
                $"The page declares a {headerSize} byte header but this build expects " +
                $"{PhysicsExtractAbi.HeaderBytes}.");
        }

        ulong byteSize = ReadU64(bytes, PhysicsExtractAbi.OffsetByteSize);
        if (byteSize > PhysicsExtractAbi.PageMaxBytes)
        {
            throw Invalid(
                $"The page declares {byteSize} bytes, which exceeds the " +
                $"{PhysicsExtractAbi.PageMaxBytes} byte limit.");
        }

        if (byteSize != (ulong)bytes.Length)
        {
            throw Invalid(
                $"The page declares {byteSize} bytes but {bytes.Length} bytes were supplied.");
        }

        if (bytes.Length % PhysicsExtractAbi.Alignment != 0)
        {
            throw Invalid(
                $"The page length {bytes.Length} is not a multiple of " +
                $"{PhysicsExtractAbi.Alignment}.");
        }

        ValidateHeaderValues(bytes);
    }

    private static void ValidateHeaderValues(ReadOnlySpan<byte> bytes)
    {
        ValidatePositive(bytes, PhysicsExtractAbi.OffsetMetersPerUnit, "metersPerUnit");
        ValidatePositive(bytes, PhysicsExtractAbi.OffsetKilogramsPerUnit, "kilogramsPerUnit");
        ValidatePositive(bytes, PhysicsExtractAbi.OffsetTimeCodesPerSecond, "timeCodesPerSecond");
        ValidateFinite(bytes, PhysicsExtractAbi.OffsetStartTimeCode, "startTimeCode");
        ValidateFinite(bytes, PhysicsExtractAbi.OffsetEndTimeCode, "endTimeCode");
        ValidateFinite(bytes, PhysicsExtractAbi.OffsetTimeCode, "timeCode");

        uint upAxis = ReadU32(bytes, PhysicsExtractAbi.OffsetUpAxis);
        if (upAxis > 2u)
        {
            throw Invalid($"The page declares the unknown up axis {upAxis}.");
        }
    }

    private static void ValidateSections(
        ReadOnlySpan<byte> bytes, Span<long> offsets, Span<long> counts)
    {
        long length = bytes.Length;
        for (int section = 0; section < PhysicsExtractAbi.SectionCount; section++)
        {
            int span = PhysicsExtractAbi.OffsetSpans + (section * 8);
            long offset = ReadU32(bytes, span);
            long count = ReadU32(bytes, span + 4);
            string name = PhysicsExtractAbi.Name(section);

            if (count == 0)
            {
                if (offset != 0)
                {
                    throw Invalid(
                        $"The empty {name} section declares the non zero offset {offset}.");
                }
                offsets[section] = 0;
                counts[section] = 0;
                continue;
            }

            if (count > PhysicsExtractAbi.Capacity(section))
            {
                throw Invalid(
                    $"The {name} section declares {count} records, which exceeds the capacity " +
                    $"{PhysicsExtractAbi.Capacity(section)}.");
            }

            if (offset < PhysicsExtractAbi.HeaderBytes)
            {
                throw Invalid($"The {name} section starts at {offset}, inside the header.");
            }

            if (offset % PhysicsExtractAbi.Alignment != 0)
            {
                throw Invalid(
                    $"The {name} section starts at {offset}, which is not " +
                    $"{PhysicsExtractAbi.Alignment} byte aligned.");
            }

            long end = checked(offset + (count * PhysicsExtractAbi.RecordBytes(section)));
            if (end > length)
            {
                throw Invalid(
                    $"The {name} section ends at {end}, past the {length} byte page.");
            }

            offsets[section] = offset;
            counts[section] = count;
        }

        ValidateNoOverlap(offsets, counts);
    }

    private static void ValidateNoOverlap(ReadOnlySpan<long> offsets, ReadOnlySpan<long> counts)
    {
        for (int left = 0; left < PhysicsExtractAbi.SectionCount; left++)
        {
            if (counts[left] == 0)
            {
                continue;
            }
            long leftEnd = offsets[left] + (counts[left] * PhysicsExtractAbi.RecordBytes(left));
            for (int right = left + 1; right < PhysicsExtractAbi.SectionCount; right++)
            {
                if (counts[right] == 0)
                {
                    continue;
                }
                long rightEnd =
                    offsets[right] + (counts[right] * PhysicsExtractAbi.RecordBytes(right));
                if (offsets[left] < rightEnd && offsets[right] < leftEnd)
                {
                    throw Invalid(
                        $"The {PhysicsExtractAbi.Name(left)} section overlaps the " +
                        $"{PhysicsExtractAbi.Name(right)} section.");
                }
            }
        }
    }

    private static void ValidateStrings(
        ReadOnlySpan<byte> bytes, ReadOnlySpan<long> offsets, ReadOnlySpan<long> counts)
    {
        long count = counts[PhysicsExtractAbi.SectionStrings];
        if (count == 0)
        {
            return;
        }
        long offset = offsets[PhysicsExtractAbi.SectionStrings];
        if (bytes[(int)offset] != 0)
        {
            throw Invalid("The strings section does not begin with the empty string.");
        }
        if (bytes[(int)(offset + count - 1)] != 0)
        {
            throw Invalid("The strings section is not terminated.");
        }
    }

    private static void ValidateObjects(
        ReadOnlySpan<byte> bytes, ReadOnlySpan<long> offsets, ReadOnlySpan<long> counts)
    {
        long objectCount = counts[PhysicsExtractAbi.SectionObjects];
        int defaultScene = ReadI32(bytes, PhysicsExtractAbi.OffsetDefaultSceneIndex);
        if (defaultScene < -1 || defaultScene >= objectCount)
        {
            throw Invalid($"The default scene index {defaultScene} names no object.");
        }

        for (long index = 0; index < objectCount; index++)
        {
            int at = (int)(offsets[PhysicsExtractAbi.SectionObjects] +
                (index * PhysicsExtractAbi.ObjectRecordBytes));
            ValidateString(bytes, offsets, counts, ReadU32(bytes, at + 24), "object path");
            ValidateString(bytes, offsets, counts, ReadU32(bytes, at + 28), "object name");
            ValidateString(bytes, offsets, counts, ReadU32(bytes, at + 32), "object type name");
            ValidateOptionalIndex(
                ReadI32(bytes, at + 52), objectCount, "object scene index");
            ValidateOptionalIndex(
                ReadI32(bytes, at + 56), objectCount, "object parent body index");
            ValidateRange(
                bytes, at + 60, counts[PhysicsExtractAbi.SectionProperties], "object properties");
            ValidateRange(
                bytes,
                at + 68,
                counts[PhysicsExtractAbi.SectionRelationships],
                "object relationships");
            ValidateRange(bytes, at + 76, counts[PhysicsExtractAbi.SectionPoints], "object points");
            ValidateRange(
                bytes, at + 84, counts[PhysicsExtractAbi.SectionIndices], "object indices");
        }

        ValidatePointIndices(bytes, offsets, counts);
    }

    private static void ValidatePointIndices(
        ReadOnlySpan<byte> bytes, ReadOnlySpan<long> offsets, ReadOnlySpan<long> counts)
    {
        long objectCount = counts[PhysicsExtractAbi.SectionObjects];
        for (long index = 0; index < objectCount; index++)
        {
            int at = (int)(offsets[PhysicsExtractAbi.SectionObjects] +
                (index * PhysicsExtractAbi.ObjectRecordBytes));
            long pointCount = ReadU32(bytes, at + 80);
            long indexStart = ReadU32(bytes, at + 84);
            long indexCount = ReadU32(bytes, at + 88);
            for (long slot = 0; slot < indexCount; slot++)
            {
                long value = ReadU32(
                    bytes,
                    (int)(offsets[PhysicsExtractAbi.SectionIndices] +
                        ((indexStart + slot) * PhysicsExtractAbi.IndexRecordBytes)));
                if (value >= pointCount)
                {
                    throw Invalid(
                        $"A collider triangle index names point {value} of {pointCount}.");
                }
            }
        }
    }

    private static void ValidateProperties(
        ReadOnlySpan<byte> bytes, ReadOnlySpan<long> offsets, ReadOnlySpan<long> counts)
    {
        long propertyCount = counts[PhysicsExtractAbi.SectionProperties];
        for (long index = 0; index < propertyCount; index++)
        {
            int at = (int)(offsets[PhysicsExtractAbi.SectionProperties] +
                (index * PhysicsExtractAbi.PropertyRecordBytes));
            ValidateString(bytes, offsets, counts, ReadU32(bytes, at + 4), "property name");
            uint valueKind = ReadU32(bytes, at + 8);
            bool isText = valueKind is 9u or 14u;
            long limit = isText
                ? counts[PhysicsExtractAbi.SectionTexts]
                : counts[PhysicsExtractAbi.SectionNumbers];
            ValidateRange(bytes, at + 20, limit, isText ? "property texts" : "property numbers");
        }

        long textCount = counts[PhysicsExtractAbi.SectionTexts];
        for (long index = 0; index < textCount; index++)
        {
            int at = (int)(offsets[PhysicsExtractAbi.SectionTexts] +
                (index * PhysicsExtractAbi.TextRecordBytes));
            ValidateString(bytes, offsets, counts, ReadU32(bytes, at), "text");
        }
    }

    private static void ValidateRelationships(
        ReadOnlySpan<byte> bytes, ReadOnlySpan<long> offsets, ReadOnlySpan<long> counts)
    {
        long relationshipCount = counts[PhysicsExtractAbi.SectionRelationships];
        for (long index = 0; index < relationshipCount; index++)
        {
            int at = (int)(offsets[PhysicsExtractAbi.SectionRelationships] +
                (index * PhysicsExtractAbi.RelationshipRecordBytes));
            ValidateString(bytes, offsets, counts, ReadU32(bytes, at + 4), "relationship name");
            ValidateRange(
                bytes, at + 8, counts[PhysicsExtractAbi.SectionTargets], "relationship targets");
        }
    }

    private static void ValidateTargetsAndDiagnostics(
        ReadOnlySpan<byte> bytes, ReadOnlySpan<long> offsets, ReadOnlySpan<long> counts)
    {
        long objectCount = counts[PhysicsExtractAbi.SectionObjects];
        long targetCount = counts[PhysicsExtractAbi.SectionTargets];
        for (long index = 0; index < targetCount; index++)
        {
            int at = (int)(offsets[PhysicsExtractAbi.SectionTargets] +
                (index * PhysicsExtractAbi.TargetRecordBytes));
            ValidateString(bytes, offsets, counts, ReadU32(bytes, at + 8), "target path");
            ValidateOptionalIndex(ReadI32(bytes, at + 12), objectCount, "target object index");
        }

        long diagnosticCount = counts[PhysicsExtractAbi.SectionDiagnostics];
        for (long index = 0; index < diagnosticCount; index++)
        {
            int at = (int)(offsets[PhysicsExtractAbi.SectionDiagnostics] +
                (index * PhysicsExtractAbi.DiagnosticRecordBytes));
            ValidateOptionalIndex(ReadI32(bytes, at + 12), objectCount, "diagnostic object index");
            ValidateString(bytes, offsets, counts, ReadU32(bytes, at + 16), "diagnostic message");
        }
    }

    private static void ValidateRange(ReadOnlySpan<byte> bytes, int at, long limit, string what)
    {
        long start = ReadU32(bytes, at);
        long count = ReadU32(bytes, at + 4);
        if (count == 0)
        {
            if (start > limit)
            {
                throw Invalid($"The empty {what} range starts at {start} of {limit}.");
            }
            return;
        }
        long end = checked(start + count);
        if (end > limit)
        {
            throw Invalid($"The {what} range ends at {end}, past the available {limit}.");
        }
    }

    private static void ValidateOptionalIndex(int value, long limit, string what)
    {
        if (value < -1 || value >= limit)
        {
            throw Invalid($"The {what} {value} names no object of {limit}.");
        }
    }

    private static void ValidateString(
        ReadOnlySpan<byte> bytes,
        ReadOnlySpan<long> offsets,
        ReadOnlySpan<long> counts,
        uint offset,
        string what)
    {
        if (offset == 0)
        {
            return;
        }
        long count = counts[PhysicsExtractAbi.SectionStrings];
        if (offset >= count)
        {
            throw Invalid($"The {what} names byte {offset} of a {count} byte strings section.");
        }
        _ = bytes;
        _ = offsets;
    }

    private static void ValidatePositive(ReadOnlySpan<byte> bytes, int at, string what)
    {
        double value = ReadF64(bytes, at);
        if (!double.IsFinite(value) || value <= 0.0)
        {
            throw Invalid($"The page declares the unusable {what} {value}.");
        }
    }

    private static void ValidateFinite(ReadOnlySpan<byte> bytes, int at, string what)
    {
        double value = ReadF64(bytes, at);
        if (!double.IsFinite(value))
        {
            throw Invalid($"The page declares the unusable {what} {value}.");
        }
    }

    private static UsdPhysicsExtractionException Invalid(string message) => new(message);

    private static uint ReadU32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);

    private static int ReadI32(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]);

    private static ulong ReadU64(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]);

    private static double ReadF64(ReadOnlySpan<byte> bytes, int offset) =>
        BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes[offset..]));
}
