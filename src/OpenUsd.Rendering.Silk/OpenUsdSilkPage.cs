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
        ByteLength = data.Length;
        MeshPreparationUsage = meshPreparationUsage;
        MaximumCommandPageBytes = maximumCommandPageBytes;
        SilkManagedDiagnostics.PageCreated();
    }

    /// <summary>Gets the command-page ABI version.</summary>
    public uint AbiVersion { get; }

    /// <summary>Gets the monotonically increasing page revision.</summary>
    public ulong Revision { get; }

    /// <summary>Gets the command count.</summary>
    public uint CommandCount { get; }

    /// <summary>Gets the serialized byte length of this immutable command page.</summary>
    public int ByteLength { get; }

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

    internal ReplaySnapshot CaptureReplay() => new(AbiVersion, Revision,
        Volatile.Read(ref _data) ?? throw new ObjectDisposedException(nameof(OpenUsdSilkPage)),
        CommandCount, MeshPreparationUsage, MaximumCommandPageBytes, _recoverySource);

    internal readonly record struct ReplaySnapshot(
        uint Abi, ulong Revision, byte[] Data, uint Commands, SilkMeshPreparationUsage? Usage,
        int? PageLimit, WeakReference<OpenUsdSilkSession>? Source)
    {
        internal bool Matches(OpenUsdSilkPage page) =>
            Revision == page.Revision && ReferenceEquals(Data, Volatile.Read(ref page._data));

        internal OpenUsdSilkPage CreatePage() => new(Abi, Revision, Data, Commands, Usage, PageLimit)
        {
            _recoverySource = Source
        };
    }

    internal RenderDiagnosticsState WithPreparationDiagnostics(RenderDiagnosticsState diagnostics)
    {
        if (MeshPreparationUsage is not { } usage)
        {
            return diagnostics;
        }
        string pageLimit =
            MaximumCommandPageBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unset";
        string message = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Logical mesh reservation: limit={usage.MaximumReservedBytes} bytes. Serialized page: " +
            $"ceiling={pageLimit}. ") +
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
