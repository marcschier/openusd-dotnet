// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Extraction;

public static partial class UsdPhysicsStageExtractor
{
    /// <summary>Extracts all physics geometry in bounded batches and prepares convex collider hulls.</summary>
    /// <param name="scheduler">The stage owner.</param>
    /// <param name="cancellationToken">Cancels between extractions and during native cooking.</param>
    /// <returns>A complete, detached page containing prepared hulls and remaining raw geometry.</returns>
    /// <remarks>Visibility and guide purpose do not exclude physics in this default preparation profile.</remarks>
    public static ValueTask<UsdPhysicsExtractionPage> PrepareAsync(
        UsdStageScheduler scheduler, CancellationToken cancellationToken = default) =>
        PrepareAsync(scheduler, UsdPhysicsExtractionOptions.Default with
        {
            IncludeUnmapped = true,
            SkipGuide = false,
            SkipInvisible = false,
        }, null, cancellationToken);

    /// <summary>Prepares a complete page while preserving the requested source sampling and output bounds.</summary>
    /// <param name="scheduler">The stage owner.</param>
    /// <param name="options">Whole-stage extraction policy and final page bounds; geometry windows are not accepted.</param>
    /// <param name="progress">Receives completed object fractions between zero and one.</param>
    /// <param name="cancellationToken">Cancels preparation.</param>
    /// <returns>A complete prepared page; no partial page is published on failure or cancellation.</returns>
    public static async ValueTask<UsdPhysicsExtractionPage> PrepareAsync(
        UsdStageScheduler scheduler, UsdPhysicsExtractionOptions options,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(options);
        _ = options.ToNative();
        if (!options.IncludeMeshData || options.GeometryObjectStart != 0 || options.GeometryObjectCount != 0)
        {
            throw new ArgumentException("Complete preparation requires mesh data and cannot select a geometry window.", nameof(options));
        }
        ulong revision = await scheduler.InvokeAsync(static stage => stage.ChangeSerial, cancellationToken).ConfigureAwait(false);
        UsdPhysicsExtractionOptions identityOptions = options with { IncludeMeshData = false, FingerprintMeshData = true };
        UsdPhysicsExtractionPage metadata = await ExtractAsync(scheduler, identityOptions, cancellationToken).ConfigureAwait(false);
        RequireUntruncated(metadata);
        var builder = new PreparedPageBuilder(metadata, options);
        var cache = new Dictionary<string, UsdPhysicsExtractionComposer.PreparedCollider>(StringComparer.Ordinal);
        int first = 0;
        while (first < metadata.ObjectCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(512, metadata.ObjectCount - first);
            UsdPhysicsExtractionPage batch;
            while (true)
            {
                batch = await ExtractAsync(scheduler, options with
                {
                    GeometryObjectStart = first,
                    GeometryObjectCount = count,
                    MaxPoints = Math.Min(Effective(options.MaxPoints, PhysicsExtractAbi.MaxPoints), PhysxMeshPreparation.MaxPoints),
                    MaxIndices = Math.Min(Effective(options.MaxIndices, PhysicsExtractAbi.MaxIndices), PhysxMeshPreparation.MaxIndices),
                }, cancellationToken).ConfigureAwait(false);
                RequireSameSource(metadata, batch);
                ulong current = await scheduler.InvokeAsync(static stage => stage.ChangeSerial, cancellationToken).ConfigureAwait(false);
                if (current != revision)
                {
                    throw new UsdPhysicsExtractionException("The stage changed during bounded physics preparation.");
                }
                if ((batch.Flags & UsdPhysicsExtractionPageTraits.Truncated) == 0)
                {
                    break;
                }
                if ((batch.TruncationFlags & ~((1u << 7) | (1u << 8))) != 0 || count == 1)
                {
                    throw new UsdPhysicsExtractionException(
                        $"Complete physics geometry cannot fit a bounded batch starting at '{metadata.GetObject(first).Path}'.");
                }
                count = Math.Max(1, count / 2);
            }
            int batchFirst = first;
            int batchCount = count;
            try
            {
                await Task.Run(() =>
                {
                    var prepared = UsdPhysicsExtractionComposer.PrepareMeshes(batch, cancellationToken, batchFirst, batchCount, cache);
                    builder.AddBatch(batch, batchFirst, batchCount, prepared);
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (PhysxMeshPreparationException exception)
            {
                throw new UsdPhysicsExtractionException(exception.Message, exception);
            }
            first += count;
            progress?.Report((double)first / metadata.ObjectCount);
        }
        UsdPhysicsExtractionPage finalIdentity = await ExtractAsync(scheduler, identityOptions, cancellationToken).ConfigureAwait(false);
        RequireUntruncated(finalIdentity);
        RequireSameSource(metadata, finalIdentity);
        if (await scheduler.InvokeAsync(static stage => stage.ChangeSerial, cancellationToken).ConfigureAwait(false) != revision)
        {
            throw new UsdPhysicsExtractionException("The stage changed before prepared physics geometry could be published.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return builder.Build();
    }

    private static int Effective(int value, int maximum) => value == 0 ? maximum : Math.Min(value, maximum);

    private static void RequireUntruncated(UsdPhysicsExtractionPage page)
    {
        if ((page.Flags & UsdPhysicsExtractionPageTraits.Truncated) != 0 || page.TruncationFlags != 0)
        {
            throw new UsdPhysicsExtractionException("Complete physics metadata exceeds the extraction bounds.");
        }
    }

    private static void RequireSameSource(UsdPhysicsExtractionPage expected, UsdPhysicsExtractionPage actual)
    {
        if (expected.ObjectCount != actual.ObjectCount ||
            expected.FingerprintLow != actual.FingerprintLow || expected.FingerprintHigh != actual.FingerprintHigh)
        {
            throw new UsdPhysicsExtractionException("Physics source inputs changed between geometry batches.");
        }
    }

    private sealed class PreparedPageBuilder
    {
        private readonly UsdPhysicsExtractionPage _metadata;
        private readonly UsdPhysicsExtractionOptions _options;
        private readonly byte[] _objects;
        private readonly List<byte> _strings;
        private readonly List<byte> _diagnostics;
        private readonly HashSet<(int, int, int, int, int, string)> _diagnosticKeys = [];
        private readonly List<PhysxVec3f> _points = [];
        private readonly List<uint> _indices = [];
        private readonly List<(int First, int Count)> _hulls = [];
        private readonly Dictionary<UsdPhysicsExtractionComposer.PreparedCollider,
            (int PointStart, int PointCount, int HullStart, int HullCount)> _shared = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, (int PointStart, int PointCount, int IndexStart, int IndexCount)> _rawShared =
            new(StringComparer.Ordinal);
        private int _nextObject;

        internal PreparedPageBuilder(UsdPhysicsExtractionPage metadata, UsdPhysicsExtractionOptions options)
        {
            _metadata = metadata;
            _options = options;
            _objects = metadata.SectionBytes(PhysicsExtractAbi.SectionObjects).ToArray();
            _strings = [.. metadata.SectionBytes(PhysicsExtractAbi.SectionStrings)];
            _diagnostics = [.. metadata.SectionBytes(PhysicsExtractAbi.SectionDiagnostics)];
            for (int index = 0; index < metadata.DiagnosticCount; index++)
            {
                _diagnosticKeys.Add(DiagnosticKey(metadata.GetDiagnostic(index)));
            }
        }

        internal void AddBatch(UsdPhysicsExtractionPage page, int first, int count,
            Dictionary<int, UsdPhysicsExtractionComposer.PreparedCollider> prepared)
        {
            if (first != _nextObject)
            {
                throw new UsdPhysicsExtractionException("Geometry batches must cover each object exactly once in order.");
            }
            for (int index = first; index < first + count; index++)
            {
                UsdPhysicsExtractionObject item = page.GetObject(index);
                int at = index * PhysicsExtractAbi.ObjectRecordBytes;
                int pointStart = _points.Count;
                int indexStart = _indices.Count;
                int hullStart = _hulls.Count;
                UsdPhysicsExtractionObjectTraits flags = item.Flags;
                if (prepared.TryGetValue(index, out var geometry) && geometry.Failure is null)
                {
                    if (_shared.TryGetValue(geometry, out var shared))
                    {
                        Put(at + 44, (int)(flags | UsdPhysicsExtractionObjectTraits.PreparedPointsMetres));
                        Put(at + 48, (int)UsdPhysicsExtractionGeometryKind.ConvexMesh);
                        Put(at + 76, shared.PointStart);
                        Put(at + 80, shared.PointCount);
                        Put(at + 84, 0);
                        Put(at + 88, 0);
                        Put(at + 208, shared.HullStart);
                        Put(at + 212, shared.HullCount);
                        continue;
                    }
                    int addedPoints = geometry.Hulls.Sum(static hull => hull.Length);
                    Admit(addedPoints, 0, geometry.Hulls.Length, item.Path);
                    foreach (ReadOnlyMemory<PhysxVec3f> hull in geometry.Hulls)
                    {
                        _hulls.Add((_points.Count, hull.Length));
                        foreach (PhysxVec3f point in hull.Span)
                        {
                            _points.Add(point);
                        }
                    }
                    flags |= UsdPhysicsExtractionObjectTraits.PreparedPointsMetres;
                    Put(at + 48, (int)UsdPhysicsExtractionGeometryKind.ConvexMesh);
                    _shared.Add(geometry, (pointStart, _points.Count - pointStart, hullStart, _hulls.Count - hullStart));
                }
                else
                {
                    string? rawKey = item.PointCount != 0 || item.IndexCount != 0
                        ? UsdPhysicsExtractionComposer.GeometryKey(page, item, decomposition: true)
                        : null;
                    if (rawKey is not null && _rawShared.TryGetValue(rawKey, out var raw))
                    {
                        Put(at + 44, (int)flags);
                        Put(at + 76, raw.PointStart);
                        Put(at + 80, raw.PointCount);
                        Put(at + 84, raw.IndexStart);
                        Put(at + 88, raw.IndexCount);
                        Put(at + 208, 0);
                        Put(at + 212, 0);
                        continue;
                    }
                    Admit(item.PointCount, item.IndexCount, 0, item.Path);
                    for (int point = 0; point < item.PointCount; point++)
                    {
                        (float x, float y, float z) = page.GetPoint(item.PointStart + point);
                        _points.Add(new PhysxVec3f(x, y, z));
                    }
                    for (int slot = 0; slot < item.IndexCount; slot++)
                    {
                        _indices.Add((uint)page.GetIndex(item.IndexStart + slot));
                    }
                    if (rawKey is not null)
                    {
                        _rawShared.Add(rawKey, (pointStart, item.PointCount, indexStart, item.IndexCount));
                    }
                }
                Put(at + 44, (int)flags);
                Put(at + 76, pointStart);
                Put(at + 80, _points.Count - pointStart);
                Put(at + 84, indexStart);
                Put(at + 88, _indices.Count - indexStart);
                Put(at + 208, hullStart);
                Put(at + 212, _hulls.Count - hullStart);
            }
            for (int index = 0; index < page.DiagnosticCount; index++)
            {
                UsdPhysicsExtractionDiagnostic diagnostic = page.GetDiagnostic(index);
                if (!_diagnosticKeys.Add(DiagnosticKey(diagnostic)))
                {
                    continue;
                }
                byte[] message = Encoding.UTF8.GetBytes(diagnostic.Message);
                if (_strings.Count + message.Length + 1 > Effective(_options.MaxStringBytes, PhysicsExtractAbi.MaxStringBytes) ||
                    _diagnostics.Count / PhysicsExtractAbi.DiagnosticRecordBytes >=
                        Effective(_options.MaxDiagnostics, PhysicsExtractAbi.MaxDiagnostics))
                {
                    throw new UsdPhysicsExtractionException("Prepared physics diagnostics exceed the complete-page bounds.");
                }
                byte[] record = page.SectionBytes(PhysicsExtractAbi.SectionDiagnostics)
                    .Slice(index * PhysicsExtractAbi.DiagnosticRecordBytes, PhysicsExtractAbi.DiagnosticRecordBytes).ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(16), (uint)_strings.Count);
                _strings.AddRange(message);
                _strings.Add(0);
                _diagnostics.AddRange(record);
                if (diagnostic.ObjectIndex >= 0)
                {
                    int at = diagnostic.ObjectIndex * PhysicsExtractAbi.ObjectRecordBytes + 92;
                    Put(at, checked((int)BinaryPrimitives.ReadUInt32LittleEndian(_objects.AsSpan(at)) + 1));
                }
            }
            _nextObject = first + count;
        }

        private static (int, int, int, int, int, string) DiagnosticKey(UsdPhysicsExtractionDiagnostic diagnostic) =>
            (diagnostic.ObjectIndex, (int)diagnostic.Severity, (int)diagnostic.Category,
                (int)diagnostic.Code, (int)diagnostic.Key, diagnostic.Message);

        private void Admit(int points, int indices, int hulls, string path)
        {
            if ((long)_points.Count + points > Effective(_options.MaxPoints, PhysicsExtractAbi.MaxPoints) ||
                (long)_indices.Count + indices > Effective(_options.MaxIndices, PhysicsExtractAbi.MaxIndices) ||
                (long)_hulls.Count + hulls > PhysicsExtractAbi.Capacity(PhysicsExtractAbi.SectionHulls) ||
                (long)_metadata.ByteSize + ((long)_points.Count + points) * 12 +
                    ((long)_indices.Count + indices) * 4 + ((long)_hulls.Count + hulls) * 8 > (long)PhysicsExtractAbi.PageMaxBytes)
            {
                throw new UsdPhysicsExtractionException(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"Complete prepared physics geometry exceeds the page bounds at '{path}': " +
                    $"points {_points.Count}+{points}/{Effective(_options.MaxPoints, PhysicsExtractAbi.MaxPoints)}, " +
                    $"indices {_indices.Count}+{indices}/{Effective(_options.MaxIndices, PhysicsExtractAbi.MaxIndices)}, " +
                    $"hulls {_hulls.Count}+{hulls}/{PhysicsExtractAbi.Capacity(PhysicsExtractAbi.SectionHulls)}."));
            }
        }

        private void Put(int offset, int value) =>
            BinaryPrimitives.WriteUInt32LittleEndian(_objects.AsSpan(offset), checked((uint)value));

        internal UsdPhysicsExtractionPage Build()
        {
            if (_nextObject != _metadata.ObjectCount)
            {
                throw new UsdPhysicsExtractionException("Prepared geometry does not cover every extracted object.");
            }
            var counts = new int[PhysicsExtractAbi.SectionCount];
            var offsets = new int[counts.Length];
            long length = PhysicsExtractAbi.HeaderBytes;
            for (int section = 0; section < counts.Length; section++)
            {
                counts[section] = section switch
                {
                    PhysicsExtractAbi.SectionStrings => _strings.Count,
                    PhysicsExtractAbi.SectionPoints => _points.Count,
                    PhysicsExtractAbi.SectionIndices => _indices.Count,
                    PhysicsExtractAbi.SectionHulls => _hulls.Count,
                    PhysicsExtractAbi.SectionDiagnostics => _diagnostics.Count / PhysicsExtractAbi.DiagnosticRecordBytes,
                    _ => _metadata.SectionCount(section),
                };
                if (counts[section] == 0)
                {
                    continue;
                }
                offsets[section] = checked((int)length);
                length = (length + (long)counts[section] * PhysicsExtractAbi.RecordBytes(section) + 7) & ~7L;
            }
            if ((ulong)length > PhysicsExtractAbi.PageMaxBytes)
            {
                throw new UsdPhysicsExtractionException("The complete prepared extraction exceeds its byte bound.");
            }
            var bytes = new byte[(int)length];
            _metadata.HeaderBytes.CopyTo(bytes);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(PhysicsExtractAbi.OffsetByteSize), (ulong)length);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(PhysicsExtractAbi.OffsetFlags),
                (uint)(_metadata.Flags & ~UsdPhysicsExtractionPageTraits.PartialGeometry));
            for (int section = 0; section < counts.Length; section++)
            {
                int span = PhysicsExtractAbi.OffsetSpans + section * 8;
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(span), (uint)offsets[section]);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(span + 4), (uint)counts[section]);
                if (counts[section] == 0)
                {
                    continue;
                }
                Span<byte> destination = bytes.AsSpan(offsets[section], counts[section] * PhysicsExtractAbi.RecordBytes(section));
                switch (section)
                {
                    case PhysicsExtractAbi.SectionStrings:
                        CollectionsMarshal.AsSpan(_strings).CopyTo(destination);
                        break;
                    case PhysicsExtractAbi.SectionObjects:
                        _objects.CopyTo(destination);
                        break;
                    case PhysicsExtractAbi.SectionDiagnostics:
                        CollectionsMarshal.AsSpan(_diagnostics).CopyTo(destination);
                        break;
                    case PhysicsExtractAbi.SectionPoints:
                        for (int index = 0; index < _points.Count; index++)
                        {
                            PhysxVec3f point = _points[index];
                            BinaryPrimitives.WriteSingleLittleEndian(destination[(index * 12)..], point.X);
                            BinaryPrimitives.WriteSingleLittleEndian(destination[(index * 12 + 4)..], point.Y);
                            BinaryPrimitives.WriteSingleLittleEndian(destination[(index * 12 + 8)..], point.Z);
                        }
                        break;
                    case PhysicsExtractAbi.SectionIndices:
                        for (int index = 0; index < _indices.Count; index++)
                        {
                            BinaryPrimitives.WriteUInt32LittleEndian(destination[(index * 4)..], _indices[index]);
                        }
                        break;
                    case PhysicsExtractAbi.SectionHulls:
                        for (int index = 0; index < _hulls.Count; index++)
                        {
                            BinaryPrimitives.WriteUInt32LittleEndian(destination[(index * 8)..], (uint)_hulls[index].First);
                            BinaryPrimitives.WriteUInt32LittleEndian(destination[(index * 8 + 4)..], (uint)_hulls[index].Count);
                        }
                        break;
                    default:
                        _metadata.SectionBytes(section).CopyTo(destination);
                        break;
                }
            }
            return UsdPhysicsExtractionPage.Adopt(bytes);
        }
    }
}
