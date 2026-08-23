// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace OpenUsd.Physics.Baking;

/// <summary>
/// Reads the fixed-layout result page the native runtime writes for one authoring page.
/// </summary>
internal readonly ref struct UsdPhysicsBakeResultPage
{
    private readonly ReadOnlySpan<byte> _page;

    private UsdPhysicsBakeResultPage(ReadOnlySpan<byte> page) => _page = page;

    /// <summary>Validates and wraps a result page written by the native runtime.</summary>
    /// <param name="page">The buffer the native runtime wrote into.</param>
    /// <param name="result">The validated result page.</param>
    /// <returns><see langword="true"/> when the buffer is a well-formed result page.</returns>
    public static bool TryRead(ReadOnlySpan<byte> page, out UsdPhysicsBakeResultPage result)
    {
        result = default;
        if (page.Length < UsdPhysicsBakePageBuilder.ResultHeaderSize)
        {
            return false;
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(page[0..]) !=
                UsdPhysicsBakePageBuilder.ResultHeaderSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(page[4..]) !=
                UsdPhysicsBakePageBuilder.ResultMagic ||
            BinaryPrimitives.ReadUInt32LittleEndian(page[8..]) !=
                UsdPhysicsBakePageBuilder.ResultVersion)
        {
            return false;
        }

        uint recordCount = BinaryPrimitives.ReadUInt32LittleEndian(page[12..]);
        long required = UsdPhysicsBakePageBuilder.ResultHeaderSize +
            ((long)recordCount * UsdPhysicsBakePageBuilder.ResultRecordSize);
        if (required > page.Length)
        {
            return false;
        }

        result = new UsdPhysicsBakeResultPage(page);
        return true;
    }

    /// <summary>Gets the number of per-record outcomes in the page.</summary>
    public int RecordCount => (int)BinaryPrimitives.ReadUInt32LittleEndian(_page[12..]);

    /// <summary>Gets the number of records the runtime authored.</summary>
    public int AppliedCount => (int)BinaryPrimitives.ReadUInt32LittleEndian(_page[16..]);

    /// <summary>Gets the number of records the runtime intentionally left unauthored.</summary>
    public int SkippedCount => (int)BinaryPrimitives.ReadUInt32LittleEndian(_page[20..]);

    /// <summary>Gets the number of records the runtime rejected.</summary>
    public int RejectedCount => (int)BinaryPrimitives.ReadUInt32LittleEndian(_page[24..]);

    /// <summary>Gets the number of attributes the runtime authored.</summary>
    public int AuthoredCount => (int)BinaryPrimitives.ReadUInt32LittleEndian(_page[28..]);

    /// <summary>Reads the outcome of one record.</summary>
    /// <param name="index">The zero-based record index.</param>
    /// <param name="kind">The identity kind to report the outcome under.</param>
    /// <returns>The record outcome.</returns>
    public UsdPhysicsBakeRecordOutcome GetOutcome(int index, UsdPhysicsObjectKind kind)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, RecordCount);

        ReadOnlySpan<byte> record = _page.Slice(
            UsdPhysicsBakePageBuilder.ResultHeaderSize +
                (index * UsdPhysicsBakePageBuilder.ResultRecordSize),
            UsdPhysicsBakePageBuilder.ResultRecordSize);
        ulong id = BinaryPrimitives.ReadUInt64LittleEndian(record[0..]);
        uint status = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]);
        uint detail = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
        return new UsdPhysicsBakeRecordOutcome(
            new UsdPhysicsObjectId(id, kind),
            MapStatus(status),
            detail > int.MaxValue ? int.MaxValue : (int)detail);
    }

    private static UsdPhysicsBakeRecordStatus MapStatus(uint status) => status switch
    {
        0 => UsdPhysicsBakeRecordStatus.Applied,
        1 => UsdPhysicsBakeRecordStatus.Skipped,
        2 => UsdPhysicsBakeRecordStatus.PathMissing,
        3 => UsdPhysicsBakeRecordStatus.NotTransformable,
        4 => UsdPhysicsBakeRecordStatus.NotPointBased,
        5 => UsdPhysicsBakeRecordStatus.InstanceProxy,
        6 => UsdPhysicsBakeRecordStatus.InPrototype,
        7 => UsdPhysicsBakeRecordStatus.SampleCountMismatch,
        8 => UsdPhysicsBakeRecordStatus.ExistingSample,
        9 => UsdPhysicsBakeRecordStatus.UnsupportedKind,
        10 => UsdPhysicsBakeRecordStatus.AuthoringFailed,
        _ => UsdPhysicsBakeRecordStatus.InvalidRecord
    };
}
