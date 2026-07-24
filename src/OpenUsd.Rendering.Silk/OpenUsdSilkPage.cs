// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Owns a managed copy of one immutable hdSilk command page.
/// </summary>
/// <remarks>
/// Native page memory is released before this object is returned. Command
/// views therefore reference managed-owned bytes and cannot outlive a native
/// lease.
/// </remarks>
public sealed class OpenUsdSilkPage : IDisposable
{
    private byte[]? _data;

    internal OpenUsdSilkPage(
        uint abiVersion,
        ulong revision,
        byte[] data,
        uint commandCount)
    {
        SilkCommandParser.ValidatePageAbi(abiVersion);
        AbiVersion = abiVersion;
        Revision = revision;
        _data = data;
        CommandCount = commandCount;
        SilkManagedDiagnostics.PageCreated();
    }

    /// <summary>Gets the command-page ABI version.</summary>
    public uint AbiVersion { get; }

    /// <summary>Gets the monotonically increasing page revision.</summary>
    public ulong Revision { get; }

    /// <summary>Gets the command count.</summary>
    public uint CommandCount { get; }

    /// <summary>Gets a command enumerator over managed-owned page bytes.</summary>
    public SilkCommandEnumerator GetEnumerator()
    {
        byte[] data = Volatile.Read(ref _data)
            ?? throw new ObjectDisposedException(nameof(OpenUsdSilkPage));
        return SilkCommandParser.Enumerate(data, CommandCount, AbiVersion);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _data, null) is not null)
        {
            SilkManagedDiagnostics.PageDestroyed();
        }
    }
}
