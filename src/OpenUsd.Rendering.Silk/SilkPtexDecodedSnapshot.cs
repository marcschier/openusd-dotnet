// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OpenUsd.Rendering.Silk;

// Capture and comparison require exclusive access to the caller's decoded data.
internal sealed class SilkPtexDecodedSnapshot : IDisposable
{
    private readonly SilkDecodedPtexFaces _source;
    private SilkPtexFaceLayout[]? _sourceFaces;
    private byte[]? _sourcePixels;
    private SilkDecodedPtexFaces? _owned;

    private SilkPtexDecodedSnapshot(SilkDecodedPtexFaces source, SilkDecodedPtexFaces owned)
    {
        _source = source;
        _sourceFaces = source.Faces;
        _sourcePixels = source.Pixels;
        _owned = owned;
    }

    internal static ulong RequiredBytes(SilkDecodedPtexFaces source) =>
        checked(2ul * ((ulong)source.Faces.Length * (ulong)Unsafe.SizeOf<SilkPtexFaceLayout>() +
            (ulong)source.Pixels.Length) + (ulong)Unsafe.SizeOf<SilkPtexDecodeInfo>());

    internal static SilkPtexDecodedSnapshot Capture(
        SilkDecodedPtexFaces source, SilkCpuTextureBudget budget)
    {
        ObjectDisposedException.ThrowIf(source.IsDisposed, source);
        IDisposable reservation = budget.Reserve(RequiredBytes(source));
        SilkDecodedPtexFaces? owned = null;
        try
        {
            owned = new(source.Info, (SilkPtexFaceLayout[])source.Faces.Clone(),
                (byte[])source.Pixels.Clone(), reservation);
            return new(source, owned);
        }
        catch
        {
            if (owned is null)
            {
                reservation.Dispose();
            }
            else
            {
                owned.Dispose();
            }
            throw;
        }
    }

    internal SilkDecodedPtexFaces Owned => _owned ??
        throw new ObjectDisposedException(nameof(SilkPtexDecodedSnapshot));

    internal bool Matches(SilkDecodedPtexFaces source)
    {
        SilkDecodedPtexFaces owned = Owned;
        // Retained source arrays remain charged even after decoder disposal.
        // Compare them too, so mutation followed by disposal cannot mask a change.
        if (source.IsDisposed)
        {
            return ReferenceEquals(source, _source) &&
                MemoryMarshal.AsBytes(_sourceFaces.AsSpan())
                    .SequenceEqual(MemoryMarshal.AsBytes(owned.Faces.AsSpan())) &&
                _sourcePixels.AsSpan().SequenceEqual(owned.Pixels);
        }
        return source.Info.Equals(owned.Info) &&
            MemoryMarshal.AsBytes(source.Faces.AsSpan())
                .SequenceEqual(MemoryMarshal.AsBytes(owned.Faces.AsSpan())) &&
            source.Pixels.AsSpan().SequenceEqual(owned.Pixels);
    }

    public void Dispose()
    {
        _sourceFaces = null;
        _sourcePixels = null;
        Interlocked.Exchange(ref _owned, null)?.Dispose();
    }
}
