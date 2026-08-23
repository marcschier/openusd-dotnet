// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Extraction;

/// <summary>
/// Mirrors the physics extraction page layout declared by <c>openusd_physics_extract.h</c>.
/// </summary>
/// <remarks>
/// Every constant here is duplicated from the native header on purpose: the managed validator
/// refuses any page that does not match these exact numbers, so a header change that is not
/// mirrored here fails loudly instead of reading misaligned memory.
/// </remarks>
internal static class PhysicsExtractAbi
{
    internal const ulong PageMagic = 0x5458455850445355UL;
    internal const uint AbiVersion = 1u;
    internal const uint Alignment = 8u;
    internal const int HeaderBytes = 216;
    internal const ulong PageMaxBytes = 0x20000000UL;
    internal const uint OptionsVersion = 1u;
    internal const uint ViewVersion = 1u;

    internal const int ObjectRecordBytes = 208;
    internal const int PropertyRecordBytes = 40;
    internal const int RelationshipRecordBytes = 24;
    internal const int TargetRecordBytes = 16;
    internal const int NumberRecordBytes = 8;
    internal const int TextRecordBytes = 8;
    internal const int PointRecordBytes = 12;
    internal const int IndexRecordBytes = 4;
    internal const int DiagnosticRecordBytes = 32;

    internal const int MaxObjects = 1 << 20;
    internal const int MaxProperties = 1 << 22;
    internal const int MaxRelationships = 1 << 21;
    internal const int MaxTargets = 1 << 21;
    internal const int MaxNumbers = 1 << 24;
    internal const int MaxTexts = 1 << 22;
    internal const int MaxPoints = 1 << 24;
    internal const int MaxIndices = 1 << 25;
    internal const int MaxDiagnostics = 1 << 16;
    internal const int MaxStringBytes = 1 << 26;

    internal const uint OptionIncludeMeshData = 1u << 0;
    internal const uint OptionIncludeUnmapped = 1u << 1;
    internal const uint OptionSkipInvisible = 1u << 2;
    internal const uint OptionSkipGuide = 1u << 3;

    internal const int SectionStrings = 0;
    internal const int SectionObjects = 1;
    internal const int SectionProperties = 2;
    internal const int SectionRelationships = 3;
    internal const int SectionTargets = 4;
    internal const int SectionNumbers = 5;
    internal const int SectionTexts = 6;
    internal const int SectionPoints = 7;
    internal const int SectionIndices = 8;
    internal const int SectionDiagnostics = 9;
    internal const int SectionCount = 10;

    internal const int OffsetMagic = 0;
    internal const int OffsetAbiVersion = 8;
    internal const int OffsetHeaderSize = 12;
    internal const int OffsetByteSize = 16;
    internal const int OffsetFingerprintLow = 24;
    internal const int OffsetFingerprintHigh = 32;
    internal const int OffsetMetersPerUnit = 40;
    internal const int OffsetKilogramsPerUnit = 48;
    internal const int OffsetTimeCodesPerSecond = 56;
    internal const int OffsetStartTimeCode = 64;
    internal const int OffsetEndTimeCode = 72;
    internal const int OffsetTimeCode = 80;
    internal const int OffsetUpAxis = 88;
    internal const int OffsetFlags = 92;
    internal const int OffsetDefaultSceneIndex = 96;
    internal const int OffsetTruncationFlags = 100;
    internal const int OffsetSpans = 104;

    /// <summary>Returns the byte size of one record in the named section.</summary>
    /// <param name="section">The zero based section index.</param>
    /// <returns>The record stride, or one byte for the raw string section.</returns>
    internal static int RecordBytes(int section) => section switch
    {
        SectionStrings => 1,
        SectionObjects => ObjectRecordBytes,
        SectionProperties => PropertyRecordBytes,
        SectionRelationships => RelationshipRecordBytes,
        SectionTargets => TargetRecordBytes,
        SectionNumbers => NumberRecordBytes,
        SectionTexts => TextRecordBytes,
        SectionPoints => PointRecordBytes,
        SectionIndices => IndexRecordBytes,
        SectionDiagnostics => DiagnosticRecordBytes,
        _ => 0,
    };

    /// <summary>Returns the capacity bound of the named section.</summary>
    /// <param name="section">The zero based section index.</param>
    /// <returns>The largest record count the section may declare.</returns>
    internal static int Capacity(int section) => section switch
    {
        SectionStrings => MaxStringBytes,
        SectionObjects => MaxObjects,
        SectionProperties => MaxProperties,
        SectionRelationships => MaxRelationships,
        SectionTargets => MaxTargets,
        SectionNumbers => MaxNumbers,
        SectionTexts => MaxTexts,
        SectionPoints => MaxPoints,
        SectionIndices => MaxIndices,
        SectionDiagnostics => MaxDiagnostics,
        _ => 0,
    };

    /// <summary>Returns the name of the named section for diagnostics.</summary>
    /// <param name="section">The zero based section index.</param>
    /// <returns>The section name.</returns>
    internal static string Name(int section) => section switch
    {
        SectionStrings => "strings",
        SectionObjects => "objects",
        SectionProperties => "properties",
        SectionRelationships => "relationships",
        SectionTargets => "targets",
        SectionNumbers => "numbers",
        SectionTexts => "texts",
        SectionPoints => "points",
        SectionIndices => "indices",
        SectionDiagnostics => "diagnostics",
        _ => "unknown",
    };
}
