// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace OpenUsd.Physics.Baking;

/// <summary>
/// Page and record flags shared with the native batched authoring ABI.
/// </summary>
[Flags]
internal enum UsdPhysicsBakePageFlags : uint
{
    None = 0,
    TimeSample = 1u << 0,
    PreflightOnly = 1u << 1,
    ResetXformStack = 1u << 2,
    RejectExistingSample = 1u << 3,
    SkipExistingSample = 1u << 4,
    ForbidRootLayer = 1u << 5,
    SimulationMetadata = 1u << 6,
    Atomic = 1u << 7,
    Extent = 1u << 8
}

[Flags]
internal enum UsdPhysicsBakeRecordFlags : uint
{
    None = 0,
    Velocity = 1u << 0,
    Topology = 1u << 1,
    Kinematic = 1u << 2,
    Sleeping = 1u << 3
}

/// <summary>
/// Builds one pointer-free authoring page so an entire chunk crosses the native boundary in a
/// single call instead of once per simulated element.
/// </summary>
/// <remarks>
/// The page is a flat blob: a fixed header, a fixed-stride record table, and four shared payload
/// sections that records address by index. Every buffer is rented and returned, so authoring a
/// steady stream of frames does not allocate after warm-up.
/// </remarks>
internal sealed class UsdPhysicsBakePageBuilder : IDisposable
{
    internal const uint PageMagic = 0x4B424850;
    internal const uint PageVersion = 1;
    internal const uint ResultMagic = 0x4B425250;
    internal const uint ResultVersion = 1;
    internal const int HeaderSize = 72;
    internal const int RecordSize = 56;
    internal const int ResultHeaderSize = 32;
    internal const int ResultRecordSize = 16;

    internal const uint KindTransform = 0;
    internal const uint KindPoints = 1;

    private readonly List<RecordEntry> _records = [];
    private PooledBuffer<byte> _strings;
    private PooledBuffer<double> _doubles;
    private PooledBuffer<float> _floats;
    private PooledBuffer<int> _ints;
    private PooledBuffer<byte> _page;

    /// <summary>Gets the number of records staged in this page.</summary>
    public int RecordCount => _records.Count;

    /// <summary>Discards every staged record so the builder can be reused for the next chunk.</summary>
    public void Reset()
    {
        _records.Clear();
        _strings.Clear();
        _doubles.Clear();
        _floats.Clear();
        _ints.Clear();
    }

    /// <summary>Stages one simulated body transform.</summary>
    public void AddTransform(
        ulong id,
        string primPath,
        in UsdMatrix4d transform,
        in UsdVec3d linearVelocity,
        in UsdVec3d angularVelocity,
        bool writeVelocity,
        bool isKinematic,
        bool isSleeping)
    {
        var entry = new RecordEntry
        {
            Id = id,
            Kind = KindTransform,
            PathOffset = AppendString(primPath, out int pathLength),
            PathLength = pathLength,
            DoubleOffset = _doubles.Length
        };

        Span<double> matrix = _doubles.GetAppendSpan(16);
        matrix[0] = transform.M00;
        matrix[1] = transform.M01;
        matrix[2] = transform.M02;
        matrix[3] = transform.M03;
        matrix[4] = transform.M10;
        matrix[5] = transform.M11;
        matrix[6] = transform.M12;
        matrix[7] = transform.M13;
        matrix[8] = transform.M20;
        matrix[9] = transform.M21;
        matrix[10] = transform.M22;
        matrix[11] = transform.M23;
        matrix[12] = transform.M30;
        matrix[13] = transform.M31;
        matrix[14] = transform.M32;
        matrix[15] = transform.M33;

        if (writeVelocity)
        {
            Span<double> velocity = _doubles.GetAppendSpan(6);
            velocity[0] = linearVelocity.X;
            velocity[1] = linearVelocity.Y;
            velocity[2] = linearVelocity.Z;
            velocity[3] = angularVelocity.X;
            velocity[4] = angularVelocity.Y;
            velocity[5] = angularVelocity.Z;
            entry.Flags |= UsdPhysicsBakeRecordFlags.Velocity;
        }

        if (isKinematic)
        {
            entry.Flags |= UsdPhysicsBakeRecordFlags.Kinematic;
        }
        if (isSleeping)
        {
            entry.Flags |= UsdPhysicsBakeRecordFlags.Sleeping;
        }

        entry.DoubleCount = _doubles.Length - entry.DoubleOffset;
        _records.Add(entry);
    }

    /// <summary>Stages one simulated point sample.</summary>
    public void AddPoints(
        ulong id,
        string primPath,
        ReadOnlySpan<UsdVec3d> points,
        ReadOnlySpan<UsdVec3d> velocities,
        ReadOnlySpan<int> faceVertexCounts,
        ReadOnlySpan<int> faceVertexIndices,
        bool writeVelocity)
    {
        var entry = new RecordEntry
        {
            Id = id,
            Kind = KindPoints,
            PathOffset = AppendString(primPath, out int pathLength),
            PathLength = pathLength,
            FloatOffset = _floats.Length,
            IntOffset = _ints.Length,
            PointCount = points.Length
        };

        Span<float> pointData = _floats.GetAppendSpan(points.Length * 3);
        for (int index = 0; index < points.Length; ++index)
        {
            UsdVec3d point = points[index];
            pointData[(index * 3) + 0] = (float)point.X;
            pointData[(index * 3) + 1] = (float)point.Y;
            pointData[(index * 3) + 2] = (float)point.Z;
        }

        if (writeVelocity && !velocities.IsEmpty)
        {
            Span<float> velocityData = _floats.GetAppendSpan(velocities.Length * 3);
            for (int index = 0; index < velocities.Length; ++index)
            {
                UsdVec3d velocity = velocities[index];
                velocityData[(index * 3) + 0] = (float)velocity.X;
                velocityData[(index * 3) + 1] = (float)velocity.Y;
                velocityData[(index * 3) + 2] = (float)velocity.Z;
            }
            entry.Flags |= UsdPhysicsBakeRecordFlags.Velocity;
        }

        if (!faceVertexCounts.IsEmpty)
        {
            faceVertexCounts.CopyTo(_ints.GetAppendSpan(faceVertexCounts.Length));
            faceVertexIndices.CopyTo(_ints.GetAppendSpan(faceVertexIndices.Length));
            entry.Flags |= UsdPhysicsBakeRecordFlags.Topology;
            entry.FaceCount = faceVertexCounts.Length;
        }

        entry.FloatCount = _floats.Length - entry.FloatOffset;
        entry.IntCount = _ints.Length - entry.IntOffset;
        _records.Add(entry);
    }

    /// <summary>
    /// Serializes every staged record into one contiguous page and returns a view over it.
    /// </summary>
    /// <param name="flags">The page flags the native runtime authors under.</param>
    /// <param name="timeCode">The authored time code, ignored unless the page is a time sample.</param>
    /// <param name="revision">The source revision recorded in project-owned simulation metadata.</param>
    /// <returns>A span valid until the next <see cref="Build"/>, <see cref="Reset"/>, or dispose.</returns>
    public ReadOnlySpan<byte> Build(UsdPhysicsBakePageFlags flags, double timeCode, uint revision)
    {
        int recordOffset = HeaderSize;
        int stringOffset = recordOffset + (_records.Count * RecordSize);
        int doubleOffset = Align(stringOffset + _strings.Length, 8);
        int floatOffset = doubleOffset + (_doubles.Length * sizeof(double));
        int intOffset = floatOffset + (_floats.Length * sizeof(float));
        int total = intOffset + (_ints.Length * sizeof(int));

        _page.Clear();
        Span<byte> page = _page.GetAppendSpan(total);
        page.Clear();

        BinaryPrimitives.WriteUInt32LittleEndian(page[0..], HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(page[4..], PageMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(page[8..], PageVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(page[12..], (uint)flags);
        BinaryPrimitives.WriteUInt32LittleEndian(page[16..], (uint)_records.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(page[20..], (uint)recordOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(page[24..], (uint)stringOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(page[28..], (uint)_strings.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(page[32..], (uint)doubleOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(page[36..], (uint)_doubles.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(page[40..], (uint)floatOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(page[44..], (uint)_floats.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(page[48..], (uint)intOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(page[52..], (uint)_ints.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(page[56..], revision);
        BinaryPrimitives.WriteUInt32LittleEndian(page[60..], 0);
        BinaryPrimitives.WriteDoubleLittleEndian(page[64..], timeCode);

        for (int index = 0; index < _records.Count; ++index)
        {
            RecordEntry entry = _records[index];
            Span<byte> record = page.Slice(recordOffset + (index * RecordSize), RecordSize);
            BinaryPrimitives.WriteUInt64LittleEndian(record[0..], entry.Id);
            BinaryPrimitives.WriteUInt32LittleEndian(record[8..], entry.Kind);
            BinaryPrimitives.WriteUInt32LittleEndian(record[12..], (uint)entry.Flags);
            BinaryPrimitives.WriteUInt32LittleEndian(record[16..], (uint)entry.PathOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(record[20..], (uint)entry.PathLength);
            BinaryPrimitives.WriteUInt32LittleEndian(record[24..], (uint)entry.DoubleOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(record[28..], (uint)entry.DoubleCount);
            BinaryPrimitives.WriteUInt32LittleEndian(record[32..], (uint)entry.FloatOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(record[36..], (uint)entry.FloatCount);
            BinaryPrimitives.WriteUInt32LittleEndian(record[40..], (uint)entry.IntOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(record[44..], (uint)entry.IntCount);
            BinaryPrimitives.WriteUInt32LittleEndian(record[48..], (uint)entry.PointCount);
            BinaryPrimitives.WriteUInt32LittleEndian(record[52..], (uint)entry.FaceCount);
        }

        _strings.Span.CopyTo(page[stringOffset..]);
        for (int index = 0; index < _doubles.Length; ++index)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(
                page[(doubleOffset + (index * sizeof(double)))..], _doubles.Span[index]);
        }
        for (int index = 0; index < _floats.Length; ++index)
        {
            BinaryPrimitives.WriteSingleLittleEndian(
                page[(floatOffset + (index * sizeof(float)))..], _floats.Span[index]);
        }
        for (int index = 0; index < _ints.Length; ++index)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                page[(intOffset + (index * sizeof(int)))..], _ints.Span[index]);
        }

        return page;
    }

    /// <summary>Gets the exact size of the result page the native runtime writes for this page.</summary>
    public int ResultSize => ResultHeaderSize + (_records.Count * ResultRecordSize);

    /// <inheritdoc/>
    public void Dispose()
    {
        _strings.Dispose();
        _doubles.Dispose();
        _floats.Dispose();
        _ints.Dispose();
        _page.Dispose();
    }

    private int AppendString(string value, out int length)
    {
        int offset = _strings.Length;
        length = Encoding.UTF8.GetByteCount(value);
        Encoding.UTF8.GetBytes(value, _strings.GetAppendSpan(length));
        return offset;
    }

    private static int Align(int value, int alignment) =>
        (value + alignment - 1) & ~(alignment - 1);

    private struct RecordEntry
    {
        public ulong Id;
        public uint Kind;
        public UsdPhysicsBakeRecordFlags Flags;
        public int PathOffset;
        public int PathLength;
        public int DoubleOffset;
        public int DoubleCount;
        public int FloatOffset;
        public int FloatCount;
        public int IntOffset;
        public int IntCount;
        public int PointCount;
        public int FaceCount;
    }

    private struct PooledBuffer<T> : IDisposable
    {
        private T[]? _buffer;
        private int _length;

        public readonly int Length => _length;

        public readonly ReadOnlySpan<T> Span =>
            _buffer is null ? [] : _buffer.AsSpan(0, _length);

        public Span<T> GetAppendSpan(int count)
        {
            Grow(_length + count);
            Span<T> span = _buffer!.AsSpan(_length, count);
            _length += count;
            return span;
        }

        public void Clear() => _length = 0;

        public void Dispose()
        {
            if (_buffer is not null)
            {
                ArrayPool<T>.Shared.Return(_buffer);
                _buffer = null;
            }
            _length = 0;
        }

        private void Grow(int required)
        {
            if (_buffer is not null && _buffer.Length >= required)
            {
                return;
            }

            T[] replacement = ArrayPool<T>.Shared.Rent(Math.Max(required, 64));
            if (_buffer is not null)
            {
                _buffer.AsSpan(0, _length).CopyTo(replacement);
                ArrayPool<T>.Shared.Return(_buffer);
            }
            _buffer = replacement;
        }
    }
}
