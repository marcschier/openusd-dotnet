// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using OpenUsd.Rendering;
using OpenUsd.Rendering.Silk;
using OpenUsd.Rendering.Storm;

namespace OpenUsd.Viewer;

internal sealed record ViewerUnsupportedFeature(
    string Code,
    string Message) : IUsdDetachedResult;

internal sealed record ViewerDiagnosticEntry(
    DateTimeOffset Timestamp,
    string Source,
    string Code,
    string Message) : IUsdDetachedResult;

internal sealed record ViewerBackendRuntimeIdentity(
    string Compositor,
    string Api,
    string DeviceName) : IUsdDetachedResult
{
    internal static ViewerBackendRuntimeIdentity Unknown { get; } =
        new("Unknown", "Unknown", "Unknown");
}

internal readonly record struct ViewerResourceCounters(
    long StormChildren,
    long StormChildPeak,
    long ManagedStorm,
    long NativeStorm,
    long NativeStormPeak,
    long AbandonedStorm,
    long ManagedSilk,
    long NativeSilk,
    long NativeSilkPeak,
    long ManagedPages,
    long NativePages,
    long NativePagePeak,
    long GpuScenes,
    long GpuMeshes)
{
    internal static ViewerResourceCounters Capture()
    {
        (long childLive, long childPeak) = OpenUsdStormChildRuntime.GetChildCounts();
        (
            long managedStorm,
            long nativeStorm,
            long nativeStormPeak,
            long abandonedStorm) = OpenUsdStormRuntime.GetDiagnostics();
        (
            long managedSilk,
            long nativeSilk,
            long nativeSilkPeak,
            long managedPages,
            long nativePages,
            long nativePagePeak,
            long gpuScenes,
            long gpuMeshes) = OpenUsdSilkRuntime.GetDiagnostics();
        return new ViewerResourceCounters(
            childLive,
            childPeak,
            managedStorm,
            nativeStorm,
            nativeStormPeak,
            abandonedStorm,
            managedSilk,
            nativeSilk,
            nativeSilkPeak,
            managedPages,
            nativePages,
            nativePagePeak,
            gpuScenes,
            gpuMeshes);
    }
}

internal sealed record ViewerDiagnosticsSample(
    DateTimeOffset Timestamp,
    string BackendIdentity,
    ViewerBackendRuntimeIdentity RuntimeIdentity,
    string RecoveryReason,
    TimeSpan FrameDuration,
    TimeSpan? GpuDuration,
    int DrawCalls,
    long Triangles,
    int RetiredCleanupCount,
    ulong StateRevision,
    ViewerResourceCounters Resources,
    IReadOnlyList<ViewerDiagnosticEntry> Entries);

internal sealed record ViewerDiagnosticsSnapshot(
    DateTimeOffset Timestamp,
    string BackendIdentity,
    ViewerBackendRuntimeIdentity RuntimeIdentity,
    string RecoveryReason,
    TimeSpan FrameDuration,
    TimeSpan? GpuDuration,
    int DrawCalls,
    long Triangles,
    int RetiredCleanupCount,
    ulong StateRevision,
    ViewerResourceCounters Resources,
    ViewerDiagnosticEntry[] Entries,
    ViewerUnsupportedFeature[] UnsupportedFeatures) : IUsdDetachedResult
{
    internal static ViewerDiagnosticsSnapshot Empty { get; } = new(
        DateTimeOffset.MinValue,
        "Unavailable",
        ViewerBackendRuntimeIdentity.Unknown,
        "None",
        TimeSpan.Zero,
        null,
        0,
        0,
        0,
        0,
        default,
        [],
        []);
}

internal sealed class ViewerDiagnosticsBuffer
{
    internal const int DefaultEntryCapacity = 32;
    internal const int DefaultUnsupportedCapacity = 16;
    private readonly ViewerDiagnosticEntry?[] _entries;
    private readonly ViewerUnsupportedFeature?[] _unsupported;
    private int _entryCount;
    private int _entryStart;
    private int _unsupportedCount;
    private ViewerDiagnosticsSample? _latest;

    internal ViewerDiagnosticsBuffer(
        int entryCapacity = DefaultEntryCapacity,
        int unsupportedCapacity = DefaultUnsupportedCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entryCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unsupportedCapacity);
        _entries = new ViewerDiagnosticEntry[entryCapacity];
        _unsupported = new ViewerUnsupportedFeature[unsupportedCapacity];
    }

    internal int EntryCount => _entryCount;

    internal int UnsupportedCount => _unsupportedCount;

    internal void Observe(ViewerDiagnosticsSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        _latest = sample;
        foreach (ViewerDiagnosticEntry entry in sample.Entries)
        {
            AddEntry(entry);
        }
    }

    internal void AddUnsupported(IEnumerable<ViewerUnsupportedFeature> unsupported)
    {
        ArgumentNullException.ThrowIfNull(unsupported);
        foreach (ViewerUnsupportedFeature feature in unsupported)
        {
            ArgumentNullException.ThrowIfNull(feature);
            if (ContainsUnsupported(feature.Code))
            {
                continue;
            }
            if (_unsupportedCount == _unsupported.Length)
            {
                Array.Copy(_unsupported, 1, _unsupported, 0, _unsupported.Length - 1);
                _unsupportedCount--;
            }
            _unsupported[_unsupportedCount++] = feature with
            {
                Code = ViewerScalarFormatter.Bound(feature.Code, 128),
                Message = ViewerScalarFormatter.Bound(feature.Message, 512)
            };
        }
    }

    internal ViewerDiagnosticsSnapshot Snapshot()
    {
        ViewerDiagnosticsSample latest = _latest ?? new ViewerDiagnosticsSample(
            DateTimeOffset.MinValue,
            "Unavailable",
            ViewerBackendRuntimeIdentity.Unknown,
            "None",
            TimeSpan.Zero,
            null,
            0,
            0,
            0,
            0,
            default,
            []);
        var entries = new ViewerDiagnosticEntry[_entryCount];
        for (int index = 0; index < _entryCount; index++)
        {
            entries[index] = _entries[(_entryStart + index) % _entries.Length]!;
        }
        var unsupported = new ViewerUnsupportedFeature[_unsupportedCount];
        Array.Copy(_unsupported, unsupported, _unsupportedCount);
        return new ViewerDiagnosticsSnapshot(
            latest.Timestamp,
            latest.BackendIdentity,
            latest.RuntimeIdentity,
            latest.RecoveryReason,
            latest.FrameDuration,
            latest.GpuDuration,
            latest.DrawCalls,
            latest.Triangles,
            latest.RetiredCleanupCount,
            latest.StateRevision,
            latest.Resources,
            entries,
            unsupported);
    }

    private void AddEntry(ViewerDiagnosticEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var bounded = entry with
        {
            Source = ViewerScalarFormatter.Bound(entry.Source, 128),
            Code = ViewerScalarFormatter.Bound(entry.Code, 128),
            Message = ViewerScalarFormatter.Bound(entry.Message, 512)
        };
        if (_entryCount != 0)
        {
            ViewerDiagnosticEntry latest =
                _entries[(_entryStart + _entryCount - 1) % _entries.Length]!;
            if (string.Equals(latest.Source, bounded.Source, StringComparison.Ordinal) &&
                string.Equals(latest.Code, bounded.Code, StringComparison.Ordinal) &&
                string.Equals(latest.Message, bounded.Message, StringComparison.Ordinal))
            {
                return;
            }
        }
        if (_entryCount < _entries.Length)
        {
            _entries[(_entryStart + _entryCount) % _entries.Length] = bounded;
            _entryCount++;
            return;
        }
        _entries[_entryStart] = bounded;
        _entryStart = (_entryStart + 1) % _entries.Length;
    }

    private bool ContainsUnsupported(string code)
    {
        for (int index = 0; index < _unsupportedCount; index++)
        {
            if (string.Equals(_unsupported[index]!.Code, code, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}

internal sealed class ViewerDiagnosticsCadence
{
    private readonly long _minimumIntervalTicks;
    private long _lastSampleTimestamp = long.MinValue;
    private ulong _lastStateKey;
    private bool _hasStateKey;

    internal ViewerDiagnosticsCadence(TimeSpan minimumInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(minimumInterval, TimeSpan.Zero);
        _minimumIntervalTicks = Math.Max(
            1,
            checked((long)Math.Round(minimumInterval.TotalSeconds * Stopwatch.Frequency)));
    }

    internal bool ShouldSample(long timestamp, ulong stateKey, bool force)
    {
        bool stateChanged = !_hasStateKey || _lastStateKey != stateKey;
        bool intervalElapsed =
            _lastSampleTimestamp == long.MinValue ||
            timestamp - _lastSampleTimestamp >= _minimumIntervalTicks;
        if (!force && !stateChanged && !intervalElapsed)
        {
            return false;
        }
        _lastSampleTimestamp = timestamp;
        _lastStateKey = stateKey;
        _hasStateKey = true;
        return true;
    }
}

internal sealed class ViewerPathRedactor
{
    private readonly (string Value, string Replacement)[] _paths;

    internal ViewerPathRedactor(string? sourceTreePath, string? userProfilePath)
    {
        var paths = new List<(string Value, string Replacement)>(4);
        AddPath(paths, sourceTreePath, "<source-tree>");
        AddPath(paths, userProfilePath, "<user-profile>");
        _paths = paths.ToArray();
    }

    internal string Redact(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string redacted = value;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        foreach ((string path, string replacement) in _paths)
        {
            redacted = redacted.Replace(path, replacement, comparison);
        }
        return redacted;
    }

    internal static ViewerPathRedactor CreateDefault() =>
        new(FindSourceTree(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private static void AddPath(
        List<(string Value, string Replacement)> paths,
        string? path,
        string replacement)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        string fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        paths.Add((fullPath, replacement));
        string alternate = OperatingSystem.IsWindows()
            ? fullPath.Replace('\\', '/')
            : fullPath.Replace('/', '\\');
        if (!string.Equals(alternate, fullPath, StringComparison.Ordinal))
        {
            paths.Add((alternate, replacement));
        }
    }

    private static string? FindSourceTree()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenUsd.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        return null;
    }
}

internal sealed class ViewerDiagnosticsFormatter(ViewerPathRedactor redactor)
{
    internal const int MaximumTextLength = 32 * 1024;

    internal string Format(ViewerDiagnosticsSnapshot snapshot, bool includePaths)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var builder = new StringBuilder(4096);
        Append(builder, "Updated", snapshot.Timestamp == DateTimeOffset.MinValue
            ? "Never"
            : snapshot.Timestamp.ToString("O", CultureInfo.InvariantCulture));
        Append(builder, "Backend", snapshot.BackendIdentity);
        Append(builder, "Compositor", snapshot.RuntimeIdentity.Compositor);
        Append(builder, "API", snapshot.RuntimeIdentity.Api);
        Append(builder, "Device", snapshot.RuntimeIdentity.DeviceName);
        Append(builder, "Fallback/recovery", snapshot.RecoveryReason);
        Append(builder, "State revision", snapshot.StateRevision.ToString(CultureInfo.InvariantCulture));
        Append(builder, "Frame CPU", FormatDuration(snapshot.FrameDuration));
        Append(builder, "Frame GPU", snapshot.GpuDuration is { } gpu
            ? FormatDuration(gpu)
            : "Unsupported");
        Append(builder, "Draw calls", snapshot.DrawCalls.ToString(CultureInfo.InvariantCulture));
        Append(builder, "Triangles", snapshot.Triangles.ToString(CultureInfo.InvariantCulture));
        Append(
            builder,
            "Retired cleanup",
            snapshot.RetiredCleanupCount.ToString(CultureInfo.InvariantCulture));
        AppendResources(builder, snapshot.Resources);

        builder.AppendLine().AppendLine("Latest diagnostics");
        if (snapshot.Entries.Length == 0)
        {
            builder.AppendLine("- None");
        }
        foreach (ViewerDiagnosticEntry entry in snapshot.Entries)
        {
            builder.Append("- ")
                .Append(entry.Timestamp.ToString("O", CultureInfo.InvariantCulture))
                .Append(" [")
                .Append(ViewerScalarFormatter.Bound(entry.Source, 128))
                .Append("] ")
                .Append(ViewerScalarFormatter.Bound(entry.Code, 128))
                .Append(": ")
                .AppendLine(ViewerScalarFormatter.Bound(entry.Message, 512));
        }

        builder.AppendLine().AppendLine("Unsupported features");
        if (snapshot.UnsupportedFeatures.Length == 0)
        {
            builder.AppendLine("- None");
        }
        foreach (ViewerUnsupportedFeature feature in snapshot.UnsupportedFeatures)
        {
            builder.Append("- ")
                .Append(ViewerScalarFormatter.Bound(feature.Code, 128))
                .Append(": ")
                .AppendLine(ViewerScalarFormatter.Bound(feature.Message, 512));
        }

        string text = ViewerScalarFormatter.Bound(builder.ToString(), MaximumTextLength);
        return includePaths ? text : redactor.Redact(text);
    }

    private static void Append(StringBuilder builder, string name, string value) =>
        builder.Append(name)
            .Append(": ")
            .AppendLine(ViewerScalarFormatter.Bound(value, 1024));

    private static string FormatDuration(TimeSpan duration) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{duration.TotalMilliseconds:F3} ms");

    private static void AppendResources(
        StringBuilder builder,
        ViewerResourceCounters resources)
    {
        Append(
            builder,
            "Storm resources",
            $"child={resources.StormChildren}/{resources.StormChildPeak}; " +
            $"managed={resources.ManagedStorm}; native={resources.NativeStorm}/" +
            $"{resources.NativeStormPeak}; abandoned={resources.AbandonedStorm}");
        Append(
            builder,
            "Silk resources",
            $"managed={resources.ManagedSilk}; native={resources.NativeSilk}/" +
            $"{resources.NativeSilkPeak}");
        Append(
            builder,
            "Silk pages",
            $"managed={resources.ManagedPages}; native={resources.NativePages}/" +
            $"{resources.NativePagePeak}");
        Append(
            builder,
            "GPU resources",
            $"scenes={resources.GpuScenes}; meshes={resources.GpuMeshes}");
    }
}

internal static class ViewerDiagnosticEntryFactory
{
    internal static ViewerDiagnosticEntry[] From(
        RenderBackendDiagnostics diagnostics,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        int count = Math.Min(diagnostics.Entries.Count, ViewerDiagnosticsBuffer.DefaultEntryCapacity);
        var entries = new ViewerDiagnosticEntry[count];
        int sourceOffset = diagnostics.Entries.Count - count;
        for (int index = 0; index < count; index++)
        {
            RenderBackendDiagnostic diagnostic = diagnostics.Entries[sourceOffset + index];
            entries[index] = new ViewerDiagnosticEntry(
                timestamp,
                diagnostic.Backend?.ToString() ?? "Renderer",
                diagnostic.Code,
                diagnostic.Message);
        }
        return entries;
    }
}
