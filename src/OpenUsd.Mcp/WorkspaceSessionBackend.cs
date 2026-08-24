// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

/// <summary>Contains canonical paths used to initialize a native session backend.</summary>
public sealed record WorkspaceSessionBackendContext(
    string SessionId,
    string SourcePath,
    string OutputDirectory,
    string OverlayPath);

/// <summary>Creates native-operation backends for isolated workspace sessions.</summary>
public interface IWorkspaceSessionBackendFactory
{
    /// <summary>Creates and initializes a backend.</summary>
    ValueTask<IWorkspaceSessionBackend> CreateAsync(
        WorkspaceSessionBackendContext context,
        CancellationToken cancellationToken);
}

/// <summary>Isolates native and filesystem mutation from session orchestration.</summary>
public interface IWorkspaceSessionBackend : IAsyncDisposable, IPreviewRenderSourceProvider
{
    /// <summary>Persists the live overlay and exports the composed stage to a flattened layer.</summary>
    ValueTask PersistOverlayAndExportAsync(
        string outputPath,
        CancellationToken cancellationToken);

    /// <summary>Copies the stable overlay to an immutable checkpoint.</summary>
    ValueTask CreateCheckpointAsync(
        WorkspaceCheckpoint checkpoint,
        CancellationToken cancellationToken);

    /// <summary>Deletes a checkpoint that could not be committed to the session manifest.</summary>
    ValueTask DeleteCheckpointAsync(
        WorkspaceCheckpoint checkpoint,
        CancellationToken cancellationToken);

    /// <summary>Applies a fully validated batch to the overlay.</summary>
    ValueTask<ulong> ApplyAsync(
        WorkspaceEditBatch batch,
        CancellationToken cancellationToken);

    /// <summary>Atomically restores an overlay checkpoint and reloads the stage.</summary>
    ValueTask<ulong> RollbackAsync(
        WorkspaceCheckpoint checkpoint,
        CancellationToken cancellationToken);

    /// <summary>Reads the current scheduler-owned native stage revision.</summary>
    ValueTask<ulong> GetStageRevisionAsync(CancellationToken cancellationToken);

    /// <summary>Computes detached bounded scene statistics on the stage scheduler.</summary>
    ValueTask<WorkspaceSceneStatistics> InspectSceneAsync(CancellationToken cancellationToken);

    /// <summary>Creates renderer-neutral preview cameras on the stage scheduler.</summary>
    ValueTask<IReadOnlyList<CameraState>> CreatePreviewCamerasAsync(
        string? cameraPath,
        bool orbit,
        int width,
        int height,
        IReadOnlyList<double> timeCodes,
        CancellationToken cancellationToken);

    /// <summary>Atomically persists the session manifest and journal.</summary>
    ValueTask PersistAsync(
        WorkspaceSessionManifest manifest,
        CancellationToken cancellationToken);
}
