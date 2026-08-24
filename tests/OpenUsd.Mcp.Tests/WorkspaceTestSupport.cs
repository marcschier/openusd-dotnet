// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Mcp.Tests;

internal sealed class WorkspaceTestFiles : IDisposable
{
    private readonly int _maximumBatchOperationCount;
    private readonly int _maximumCheckpointCount;
    private readonly int _maximumJournalEntryCount;
    private readonly string _root;

    internal WorkspaceTestFiles(
        int maximumBatchOperationCount = 8,
        int maximumCheckpointCount = 256,
        int maximumJournalEntryCount = 1024)
    {
        _maximumBatchOperationCount = maximumBatchOperationCount;
        _maximumCheckpointCount = maximumCheckpointCount;
        _maximumJournalEntryCount = maximumJournalEntryCount;
        _root = Path.Combine(
            AppContext.BaseDirectory,
            "workspace-tests",
            Guid.NewGuid().ToString("N"));
        SourceRoot = Path.Combine(_root, "source");
        OutputRoot = Path.Combine(_root, "output");
        Directory.CreateDirectory(SourceRoot);
        Directory.CreateDirectory(OutputRoot);
        SourcePath = Path.Combine(SourceRoot, "scene.usda");
        File.WriteAllText(SourcePath, "#usda 1.0");
    }

    internal string OutputRoot { get; }

    internal string SourcePath { get; }

    internal string SourceRoot { get; }

    internal McpSessionWorkspace CreateWorkspace(RecordingWorkspaceBackend backend) =>
        new(
            new McpSessionWorkspaceOptions(
                SourceRoot,
                OutputRoot,
                _maximumBatchOperationCount,
                _maximumCheckpointCount,
                _maximumJournalEntryCount),
            new RecordingWorkspaceBackendFactory(backend));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

internal sealed class RecordingWorkspaceBackendFactory : IWorkspaceSessionBackendFactory
{
    private readonly RecordingWorkspaceBackend _backend;

    internal RecordingWorkspaceBackendFactory(RecordingWorkspaceBackend backend)
    {
        _backend = backend;
    }

    public ValueTask<IWorkspaceSessionBackend> CreateAsync(
        WorkspaceSessionBackendContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _backend.Context = context;
        return ValueTask.FromResult<IWorkspaceSessionBackend>(_backend);
    }
}

internal sealed class RecordingWorkspaceBackend : IWorkspaceSessionBackend
{
    internal Exception AcquireRenderSourceError { get; set; } =
        new NotSupportedException("The recording backend does not create native render sources.");

    internal int AcquireRenderSourceCount { get; private set; }

    internal Exception? ApplyError { get; set; }

    internal WorkspaceSessionBackendContext? Context { get; set; }

    internal int DisposeCount { get; private set; }

    internal int DisposeAttemptCount { get; private set; }

    internal int DisposeFailuresRemaining { get; set; }

    internal int DeleteCheckpointFailuresRemaining { get; set; }

    internal List<string> DeletedCheckpointIds { get; } = [];

    internal List<string> Events { get; } = [];

    internal WorkspaceSessionManifest? LastManifest { get; private set; }

    internal WorkspaceEditBatch? LastBatch { get; private set; }

    internal int PersistFailuresRemaining { get; set; }

    internal Exception? ExportError { get; set; }

    internal Action<string, CancellationToken>? ExportCallback { get; set; }

    internal Action<CancellationToken>? InspectSceneCallback { get; set; }

    internal ulong StageRevision { get; set; }

    public ValueTask<UsdStageRenderSource> AcquireRenderSourceAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AcquireRenderSourceCount++;
        Events.Add("acquire-render-source");
        return ValueTask.FromException<UsdStageRenderSource>(AcquireRenderSourceError);
    }

    public ValueTask<ulong> ApplyAsync(
        WorkspaceEditBatch batch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add("apply");
        LastBatch = batch;
        return ApplyError is null
            ? ValueTask.FromResult(++StageRevision)
            : ValueTask.FromException<ulong>(ApplyError);
    }

    public ValueTask PersistOverlayAndExportAsync(
        string outputPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add("export");
        if (ExportError is not null)
        {
            return ValueTask.FromException(ExportError);
        }

        File.WriteAllText(outputPath, "#usda 1.0\n");
        ExportCallback?.Invoke(outputPath, cancellationToken);
        return ValueTask.CompletedTask;
    }

    public ValueTask CreateCheckpointAsync(
        WorkspaceCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add("checkpoint");
        string path = Path.Combine(Context!.OutputDirectory, checkpoint.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "#usda 1.0\n");
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteCheckpointAsync(
        WorkspaceCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (DeleteCheckpointFailuresRemaining > 0)
        {
            DeleteCheckpointFailuresRemaining--;
            Events.Add("delete-checkpoint-failed");
            return ValueTask.FromException(new IOException("delete checkpoint failed"));
        }

        Events.Add("delete-checkpoint");
        DeletedCheckpointIds.Add(checkpoint.CheckpointId);
        File.Delete(Path.Combine(Context!.OutputDirectory, checkpoint.FileName));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeAttemptCount++;
        if (DisposeFailuresRemaining > 0)
        {
            DisposeFailuresRemaining--;
            Events.Add("dispose-failed");
            return ValueTask.FromException(new IOException("dispose failed"));
        }

        DisposeCount++;
        Events.Add("dispose");
        return ValueTask.CompletedTask;
    }

    public ValueTask<ulong> GetStageRevisionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add("get-stage-revision");
        return ValueTask.FromResult(StageRevision);
    }

    public ValueTask<WorkspaceSceneStatistics> InspectSceneAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add("inspect-scene");
        InspectSceneCallback?.Invoke(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            new WorkspaceSceneStatistics(
                "/World",
                1,
                0,
                0,
                0,
                0,
                1,
                1,
                0));
    }

    public ValueTask<IReadOnlyList<CameraState>> CreatePreviewCamerasAsync(
        string? cameraPath,
        bool orbit,
        int width,
        int height,
        IReadOnlyList<double> timeCodes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add("create-preview-cameras");
        return ValueTask.FromResult<IReadOnlyList<CameraState>>(
            Enumerable.Repeat(CameraState.Default, timeCodes.Count).ToArray());
    }

    public ValueTask PersistAsync(
        WorkspaceSessionManifest manifest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (PersistFailuresRemaining > 0)
        {
            PersistFailuresRemaining--;
            Events.Add("persist-failed");
            return ValueTask.FromException(new IOException("persist failed"));
        }

        LastManifest = manifest;
        Events.Add("persist");
        return ValueTask.CompletedTask;
    }

    public ValueTask<ulong> RollbackAsync(
        WorkspaceCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Events.Add("rollback");
        return ValueTask.FromResult(++StageRevision);
    }
}
