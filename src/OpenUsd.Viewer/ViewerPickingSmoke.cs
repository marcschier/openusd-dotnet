// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;

namespace OpenUsd.Viewer;

internal static class ViewerPickingSmokeContract
{
    internal const int CurrentSchemaVersion = 2;
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
    ViewerSilkOutlineEvidence[] SilkOutlines,
    DateTimeOffset CompletedAtUtc);

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
