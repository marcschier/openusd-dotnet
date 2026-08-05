// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;

namespace OpenUsd.Viewer;

internal static class ViewerPickingSmokeContract
{
    internal const int CurrentSchemaVersion = 3;
    internal const string ScenarioName = "viewer-picking-short-smoke";

    internal static readonly double[] SampleFractions =
    [
        0.05,
        0.50,
        0.95
    ];
}

internal sealed record ViewerPickingSmokeBackendEvidence(
    string Backend,
    int HitX,
    int HitY,
    string HitPath,
    int MissX,
    int MissY,
    bool ClickHit,
    bool ClickMiss);

internal sealed record ViewerSilkOutlineEvidence(
    string Backend,
    string SelectedStatus,
    ulong MaskPasses,
    ulong OutlinePasses,
    ulong SelectedDraws,
    string ClearedStatus,
    bool ClearedWithoutAdditionalPass);

internal sealed record ViewerPickingSmokeEvidence(
    int SchemaVersion,
    string Scenario,
    string StagePath,
    string CommonHitPath,
    ViewerPickingSmokeBackendEvidence[] Backends,
    bool StormClickHit,
    bool StormClickMiss,
    long StaleRetries,
    bool SelectionPreservedAcrossSwitches,
    string StormUnselectedHash,
    string StormSelectedHash,
    string StormClearedHash,
    bool StormHighlightChanged,
    bool StormHighlightCleared,
    bool HostPickHitObserved,
    bool HostPickMissObserved,
    bool HostSelectionHitObserved,
    bool HostSelectionClearObserved,
    ViewerSilkOutlineEvidence[] SilkOutlines,
    DateTimeOffset CompletedAtUtc);

internal static class ViewerPickingSmokeHostObserver
{
    private static readonly object Gate = new();
    private static TaskCompletionSource<ViewerPickEventArgs>? _nextPick;
    private static TaskCompletionSource<ViewerSelectionChangedEventArgs>? _nextSelection;

    internal static Task ObservePickAsync(
        ViewerPickEventArgs args,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        TaskCompletionSource<ViewerPickEventArgs>? completion;
        lock (Gate)
        {
            completion = _nextPick;
            _nextPick = null;
        }
        completion?.TrySetResult(args);
        return Task.CompletedTask;
    }

    internal static Task ObserveSelectionAsync(
        ViewerSelectionChangedEventArgs args,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        TaskCompletionSource<ViewerSelectionChangedEventArgs>? completion;
        lock (Gate)
        {
            completion = _nextSelection;
            _nextSelection = null;
        }
        completion?.TrySetResult(args);
        return Task.CompletedTask;
    }

    internal static async Task<ViewerPickEventArgs> WaitForNextPickAsync(
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ViewerPickEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Gate)
        {
            if (_nextPick is not null)
            {
                throw new InvalidOperationException(
                    "A Viewer picking smoke host-pick wait is already pending.");
            }
            _nextPick = completion;
        }
        try
        {
            return await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            lock (Gate)
            {
                if (ReferenceEquals(_nextPick, completion))
                {
                    _nextPick = null;
                }
            }
        }
    }

    internal static async Task<ViewerSelectionChangedEventArgs> WaitForNextSelectionAsync(
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ViewerSelectionChangedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Gate)
        {
            if (_nextSelection is not null)
            {
                throw new InvalidOperationException(
                    "A Viewer picking smoke host-selection wait is already pending.");
            }
            _nextSelection = completion;
        }
        try
        {
            return await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            lock (Gate)
            {
                if (ReferenceEquals(_nextSelection, completion))
                {
                    _nextSelection = null;
                }
            }
        }
    }
}

internal static class ViewerPickingSmokeWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    internal static async Task WriteAsync(
        string path,
        ViewerPickingSmokeEvidence evidence,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(evidence);
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "The Viewer picking smoke artifact requires a parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = fullPath + ".tmp";
        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    evidence,
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
