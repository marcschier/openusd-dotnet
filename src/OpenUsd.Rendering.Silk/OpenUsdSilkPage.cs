// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering.Silk;

/// <summary>
/// Owns one immutable hdSilk command page in managed memory or opt-in private storage.
/// </summary>
/// <remarks>
/// Contiguous native memory is copied before return. Public command views always
/// reference managed-owned bytes and remain valid after enumeration advances.
/// File-backed enumerator copies keep storage alive until their final reader is
/// collected; renderer-owned enumeration releases its storage reference deterministically.
/// </remarks>
public sealed class OpenUsdSilkPage : IDisposable
{
    private byte[]? _data;
    private SilkCommandStorage? _storage;
    private WeakReference<OpenUsdSilkSession>? _recoverySource;

    internal OpenUsdSilkPage(uint abiVersion, ulong revision, byte[] data, uint commandCount)
        : this(abiVersion, revision, data, commandCount, null)
    {
    }

    internal OpenUsdSilkPage(
        uint abiVersion,
        ulong revision,
        byte[] data,
        uint commandCount,
        SilkMeshPreparationUsage? meshPreparationUsage,
        int? maximumCommandPageBytes = null)
    {
        SilkCommandParser.ValidatePageAbi(abiVersion);
        AbiVersion = abiVersion;
        Revision = revision;
        _data = data;
        CommandCount = commandCount;
        SerializedByteLength = (ulong)data.Length;
        MeshPreparationUsage = meshPreparationUsage;
        MaximumCommandPageBytes = maximumCommandPageBytes;
        SilkManagedDiagnostics.PageCreated();
    }

    internal OpenUsdSilkPage(
        uint abiVersion, ulong revision, SilkCommandStorage storage, uint commandCount,
        SilkMeshPreparationUsage? meshPreparationUsage, int maximumCommandPageBytes)
    {
        SilkCommandParser.ValidatePageAbi(abiVersion);
        AbiVersion = abiVersion;
        Revision = revision;
        CommandCount = commandCount;
        SerializedByteLength = storage.Length;
        MeshPreparationUsage = meshPreparationUsage;
        MaximumCommandPageBytes = maximumCommandPageBytes;
        _storage = storage.Acquire();
        SilkManagedDiagnostics.PageCreated();
    }

    /// <summary>Gets the command-page ABI version.</summary>
    public uint AbiVersion { get; }

    /// <summary>Gets the monotonically increasing page revision.</summary>
    public ulong Revision { get; }

    /// <summary>Gets the command count.</summary>
    public uint CommandCount { get; }

    /// <summary>Gets the serialized length when it fits Int32; otherwise throws OverflowException.</summary>
    public int ByteLength => checked((int)SerializedByteLength);

    /// <summary>Gets the complete serialized byte length, including file-backed updates larger than Int32.</summary>
    public ulong SerializedByteLength { get; }

    /// <summary>Gets native mesh accounting, or null when neither session nor request enables admission.</summary>
    public SilkMeshPreparationUsage? MeshPreparationUsage { get; }

    internal int? MaximumCommandPageBytes { get; }

    internal void SetRecoverySource(WeakReference<OpenUsdSilkSession> source) => _recoverySource = source;

    internal void RegisterReplay(Action replay)
    {
        if (_recoverySource is { } source && source.TryGetTarget(out OpenUsdSilkSession? session))
        {
            session.RegisterRejectedPage(Revision, replay);
        }
    }

    internal ReplaySnapshot CaptureReplay()
    {
        byte[]? bytes = Volatile.Read(ref _data);
        SilkCommandStorage? storage = bytes is null
            ? (Volatile.Read(ref _storage) ?? throw new ObjectDisposedException(nameof(OpenUsdSilkPage))).Acquire()
            : null;
        return new(AbiVersion, Revision, bytes, CommandCount, MeshPreparationUsage,
            MaximumCommandPageBytes, _recoverySource, storage);
    }

    internal readonly record struct ReplaySnapshot(
        uint Abi, ulong Revision, byte[]? Data, uint Commands, SilkMeshPreparationUsage? Usage,
        int? PageLimit, WeakReference<OpenUsdSilkSession>? Source, SilkCommandStorage? Storage = null) : IDisposable
    {
        internal bool Matches(OpenUsdSilkPage page) =>
            Revision == page.Revision && (Data is not null
                ? ReferenceEquals(Data, Volatile.Read(ref page._data))
                : Storage is not null && ReferenceEquals(Storage, Volatile.Read(ref page._storage)));

        internal OpenUsdSilkPage CreatePage()
        {
            OpenUsdSilkPage page = Data is not null
                ? new(Abi, Revision, Data, Commands, Usage, PageLimit)
                : new(Abi, Revision, Storage ?? throw new ObjectDisposedException(nameof(ReplaySnapshot)),
                    Commands, Usage, PageLimit ?? throw new InvalidOperationException("Stored pages require a command limit."));
            page._recoverySource = Source;
            return page;
        }

        public void Dispose() => Storage?.Dispose();
    }

    internal RenderDiagnosticsState WithPreparationDiagnostics(RenderDiagnosticsState diagnostics) =>
        WithPreparationDiagnostics(diagnostics, MeshPreparationUsage, MaximumCommandPageBytes);

    internal static RenderDiagnosticsState WithPreparationDiagnostics(
        RenderDiagnosticsState diagnostics, SilkMeshPreparationUsage? preparationUsage, int? maximumPageBytes)
    {
        if (preparationUsage is not { } usage)
        {
            return diagnostics;
        }
        string pageLimit =
            maximumPageBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unset";
        string message = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Logical mesh reservation: limit={usage.MaximumReservedBytes} bytes. Serialized buffer: " +
            $"ceiling={pageLimit} (one contiguous page or one file-backed command). ") +
            "Not total process memory; excludes SDK/material caches, metadata, other live pages and GPU resources.";
        var admission = new RenderDiagnostic(
            RenderDiagnosticSeverity.Information, "HDSILK_PREPARATION_ADMISSION", message);
        return diagnostics.Entries.Count < 128
            ? new RenderDiagnosticsState([.. diagnostics.Entries, admission])
            : new RenderDiagnosticsState([
                .. diagnostics.Entries.Take(126),
                new(RenderDiagnosticSeverity.Warning, "HDSILK_ADMISSION_DIAGNOSTICS_TRUNCATED",
                    "Additional renderer diagnostics were omitted to report resource admission within the job limit."),
                admission
            ]);
    }

    /// <summary>Gets a command enumerator over managed-owned page bytes.</summary>
    public SilkCommandEnumerator GetEnumerator()
        => GetEnumerator(preserveViews: true);

    internal SilkCommandEnumerator GetTransientEnumerator() => GetEnumerator(preserveViews: false);

    private SilkCommandEnumerator GetEnumerator(bool preserveViews)
    {
        byte[]? data = Volatile.Read(ref _data);
        return data is not null
            ? SilkCommandParser.Enumerate(data, CommandCount, AbiVersion)
            : new(Volatile.Read(ref _storage) ?? throw new ObjectDisposedException(nameof(OpenUsdSilkPage)),
                CommandCount, preserveViews);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _data, null) is not null)
        {
            SilkManagedDiagnostics.PageDestroyed();
        }
        if (Interlocked.Exchange(ref _storage, null) is { } storage)
        {
            storage.Dispose();
            SilkManagedDiagnostics.PageDestroyed();
        }
    }
}
