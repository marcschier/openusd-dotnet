// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering;

namespace OpenUsd.Mcp;

/// <summary>
/// Owns one active OpenUSD overlay session with optimistic revisions and transactional editing.
/// </summary>
public sealed class McpSessionWorkspace : IAsyncDisposable, IPreviewRenderSourceProvider
{
    private const int CloseJournalReserve = 3;
    private readonly IWorkspaceSessionBackendFactory _backendFactory;
    private readonly List<WorkspaceCheckpoint> _checkpoints = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<WorkspaceJournalEntry> _journal = [];
    private readonly int _maximumBatchOperationCount;
    private readonly int _maximumCheckpointCount;
    private readonly int _maximumJournalEntryCount;
    private readonly WorkspacePathContainment _paths;
    private ActiveSession? _active;
    private bool _disposed;
    private long _journalSequence;
    private PendingCheckpointCleanup? _pendingCheckpointCleanup;

    internal const int MinimumJournalEntryCount = 1 + CloseJournalReserve;

    /// <summary>Initializes a scheduler-backed workspace.</summary>
    public McpSessionWorkspace(McpSessionWorkspaceOptions options)
        : this(options, new OpenUsdWorkspaceSessionBackendFactory())
    {
    }

    /// <summary>Initializes a workspace with an injectable native-operation backend.</summary>
    public McpSessionWorkspace(
        McpSessionWorkspaceOptions options,
        IWorkspaceSessionBackendFactory backendFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(backendFactory);
        _paths = new WorkspacePathContainment(options.SourceRoot, options.OutputRoot);
        _maximumBatchOperationCount = options.MaximumBatchOperationCount;
        _maximumCheckpointCount = options.MaximumCheckpointCount;
        _maximumJournalEntryCount = options.MaximumJournalEntryCount;
        _backendFactory = backendFactory;
    }

    /// <summary>Creates the single active session and its isolated overlay.</summary>
    public async ValueTask<WorkspaceSessionInfo> StartAsync(
        string relativeSourcePath,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        IWorkspaceSessionBackend? createdBackend = null;
        try
        {
            ThrowIfDisposed();
            if (_active is not null)
            {
                throw new InvalidOperationException("Only one workspace session may be active.");
            }

            string sourcePath = _paths.ResolveSourceFile(relativeSourcePath);
            string sessionId = Guid.NewGuid().ToString("N");
            string outputDirectory = _paths.CreateOutputDirectory(sessionId);
            string overlayPath = Path.Combine(outputDirectory, "overlay.usda");
            var context = new WorkspaceSessionBackendContext(
                sessionId,
                sourcePath,
                outputDirectory,
                overlayPath);
            createdBackend = await _backendFactory.CreateAsync(
                    context,
                    cancellationToken)
                .ConfigureAwait(false);
            ulong stageRevision = await createdBackend
                .GetStageRevisionAsync(cancellationToken)
                .ConfigureAwait(false);
            DateTimeOffset createdAt = DateTimeOffset.UtcNow;
            _active = new ActiveSession(
                context,
                createdBackend,
                createdAt,
                stageRevision);
            _checkpoints.Clear();
            _journal.Clear();
            _journalSequence = 0;
            _pendingCheckpointCleanup = null;
            AddJournal(
                WorkspaceJournalKind.SessionCreated,
                0,
                0,
                null,
                0,
                null);
            await PersistAsync(CancellationToken.None).ConfigureAwait(false);
            return CreateInfo(_active);
        }
        catch
        {
            if (createdBackend is not null)
            {
                _active = null;
                await createdBackend.DisposeAsync().ConfigureAwait(false);
            }

            _checkpoints.Clear();
            _journal.Clear();
            _journalSequence = 0;
            _pendingCheckpointCleanup = null;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Applies an all-or-nothing bounded batch after checkpointing the overlay.</summary>
    public async ValueTask<WorkspaceEditResult> EditAsync(
        WorkspaceSessionRevision revision,
        WorkspaceEditBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(batch);
        batch.Validate(_maximumBatchOperationCount);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveSession active = await GetCurrentAsync(revision, cancellationToken)
                .ConfigureAwait(false);
            await RecoverPendingCheckpointCleanupAsync(active).ConfigureAwait(false);
            EnsureMutationCapacity(checkpointCount: 1, journalEntryCount: 1);
            WorkspaceCheckpoint checkpoint = CreateCheckpoint(active.Generation);
            await active.Backend.CreateCheckpointAsync(checkpoint, cancellationToken)
                .ConfigureAwait(false);
            _checkpoints.Add(checkpoint);

            try
            {
                active.StageRevision = await active.Backend
                    .ApplyAsync(batch, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception editError)
            {
                try
                {
                    active.StageRevision = await active.Backend
                        .RollbackAsync(checkpoint, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException(
                        "The edit failed and its checkpoint could not be restored.",
                        editError,
                        rollbackError);
                }

                AddJournal(
                    WorkspaceJournalKind.EditFailed,
                    active.Generation,
                    active.Generation,
                    checkpoint.CheckpointId,
                    batch.Operations.Count,
                    editError.Message);
                try
                {
                    await PersistAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception journalError)
                {
                    throw new AggregateException(
                        "The edit was restored, but its recovery journal could not be persisted.",
                        editError,
                        journalError);
                }

                throw;
            }

            long before = active.Generation;
            int journalCountBeforeCommit = _journal.Count;
            long journalSequenceBeforeCommit = _journalSequence;
            active.Generation = checked(active.Generation + 1);
            AddJournal(
                WorkspaceJournalKind.EditCommitted,
                before,
                active.Generation,
                checkpoint.CheckpointId,
                batch.Operations.Count,
                null);
            try
            {
                await PersistAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception journalError)
            {
                active.Generation = before;
                _journal.RemoveRange(
                    journalCountBeforeCommit,
                    _journal.Count - journalCountBeforeCommit);
                _journalSequence = journalSequenceBeforeCommit;
                try
                {
                    active.StageRevision = await active.Backend
                        .RollbackAsync(checkpoint, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException(
                        "The edit journal failed and its checkpoint could not be restored.",
                        journalError,
                        rollbackError);
                }

                AddJournal(
                    WorkspaceJournalKind.EditFailed,
                    before,
                    before,
                    checkpoint.CheckpointId,
                    batch.Operations.Count,
                    journalError.Message);
                try
                {
                    await PersistAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception recoveryJournalError)
                {
                    throw new AggregateException(
                        "The edit was restored, but its recovery journal could not be persisted.",
                        journalError,
                        recoveryJournalError);
                }

                throw;
            }

            return new WorkspaceEditResult(
                active.Context.SessionId,
                active.Generation,
                active.StageRevision,
                checkpoint,
                batch.Operations.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Creates an explicit immutable checkpoint without changing the generation.</summary>
    public async ValueTask<WorkspaceCheckpointResult> CheckpointAsync(
        WorkspaceSessionRevision revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveSession active = await GetCurrentAsync(revision, cancellationToken)
                .ConfigureAwait(false);
            await RecoverPendingCheckpointCleanupAsync(active).ConfigureAwait(false);
            EnsureMutationCapacity(checkpointCount: 1, journalEntryCount: 1);
            WorkspaceCheckpoint checkpoint = CreateCheckpoint(active.Generation);
            await active.Backend.CreateCheckpointAsync(checkpoint, cancellationToken)
                .ConfigureAwait(false);
            int checkpointCountBefore = _checkpoints.Count;
            int journalCountBefore = _journal.Count;
            long journalSequenceBefore = _journalSequence;
            _checkpoints.Add(checkpoint);
            AddJournal(
                WorkspaceJournalKind.CheckpointCreated,
                active.Generation,
                active.Generation,
                checkpoint.CheckpointId,
                0,
                null);
            try
            {
                await PersistAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception persistenceError)
            {
                try
                {
                    await active.Backend
                        .DeleteCheckpointAsync(checkpoint, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception cleanupError)
                {
                    _pendingCheckpointCleanup = new PendingCheckpointCleanup(
                        checkpoint,
                        checkpointCountBefore,
                        journalCountBefore,
                        journalSequenceBefore,
                        persistenceError);
                    throw new AggregateException(
                        "The checkpoint manifest failed and its checkpoint file could not be deleted.",
                        persistenceError,
                        cleanupError);
                }

                RevertCheckpointTracking(
                    checkpointCountBefore,
                    journalCountBefore,
                    journalSequenceBefore);
                throw;
            }

            return new WorkspaceCheckpointResult(
                active.Context.SessionId,
                active.Generation,
                active.StageRevision,
                checkpoint);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Atomically restores a known checkpoint and advances the generation.</summary>
    public async ValueTask<WorkspaceRollbackResult> RollbackAsync(
        WorkspaceSessionRevision revision,
        string checkpointId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveSession active = await GetCurrentAsync(revision, cancellationToken)
                .ConfigureAwait(false);
            await RecoverPendingCheckpointCleanupAsync(active).ConfigureAwait(false);
            WorkspaceCheckpoint checkpoint = _checkpoints.Find(
                    candidate => string.Equals(
                        candidate.CheckpointId,
                        checkpointId,
                        StringComparison.Ordinal))
                ?? throw new ArgumentException("The checkpoint does not exist.", nameof(checkpointId));
            EnsureMutationCapacity(checkpointCount: 1, journalEntryCount: 1);
            WorkspaceCheckpoint recoveryCheckpoint = CreateCheckpoint(active.Generation);
            await active.Backend
                .CreateCheckpointAsync(recoveryCheckpoint, cancellationToken)
                .ConfigureAwait(false);
            _checkpoints.Add(recoveryCheckpoint);
            long before = active.Generation;
            int journalCountBeforeRollback = _journal.Count;
            long journalSequenceBeforeRollback = _journalSequence;
            try
            {
                active.StageRevision = await active.Backend
                    .RollbackAsync(checkpoint, cancellationToken)
                    .ConfigureAwait(false);
                active.Generation = checked(active.Generation + 1);
                AddJournal(
                    WorkspaceJournalKind.RollbackCommitted,
                    before,
                    active.Generation,
                    checkpoint.CheckpointId,
                    0,
                    null);
                await PersistAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception rollbackError)
            {
                active.Generation = before;
                _journal.RemoveRange(
                    journalCountBeforeRollback,
                    _journal.Count - journalCountBeforeRollback);
                _journalSequence = journalSequenceBeforeRollback;
                try
                {
                    active.StageRevision = await active.Backend
                        .RollbackAsync(recoveryCheckpoint, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception recoveryError)
                {
                    throw new AggregateException(
                        "The rollback failed and its recovery checkpoint could not be restored.",
                        rollbackError,
                        recoveryError);
                }

                AddJournal(
                    WorkspaceJournalKind.RollbackFailed,
                    before,
                    before,
                    checkpoint.CheckpointId,
                    0,
                    rollbackError.Message);
                try
                {
                    await PersistAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception recoveryJournalError)
                {
                    throw new AggregateException(
                        "The rollback was restored, but its recovery journal could not be persisted.",
                        rollbackError,
                        recoveryJournalError);
                }

                throw;
            }

            return new WorkspaceRollbackResult(
                active.Context.SessionId,
                active.Generation,
                active.StageRevision,
                checkpoint.CheckpointId);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Returns a detached snapshot of the active manifest.</summary>
    public async ValueTask<WorkspaceSessionManifest> GetManifestAsync(
        WorkspaceSessionRevision revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveSession active = await GetCurrentAsync(revision, cancellationToken)
                .ConfigureAwait(false);
            return CreateManifest(active);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Returns detached status for the optional active session.</summary>
    public async ValueTask<WorkspaceSessionStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_active is not null)
            {
                if (_active.CloseState == SessionCloseState.Active)
                {
                    _active.StageRevision = await _active.Backend
                        .GetStageRevisionAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            return new WorkspaceSessionStatus(
                _active is not null,
                _active is null ? null : CreateInfo(_active));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Returns detached session and journal state at an optimistic revision.</summary>
    public async ValueTask<WorkspaceSessionSnapshot> GetSnapshotAsync(
        WorkspaceSessionRevision revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveSession active = await GetCurrentAsync(revision, cancellationToken)
                .ConfigureAwait(false);
            return new WorkspaceSessionSnapshot(
                CreateInfo(active),
                CreateManifest(active));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Returns detached bounded scene statistics at an optimistic revision.</summary>
    public async ValueTask<WorkspaceSceneStatistics> InspectSceneAsync(
        WorkspaceSessionRevision revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveSession active = await GetCurrentAsync(revision, cancellationToken)
                .ConfigureAwait(false);
            return await active.Backend.InspectSceneAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Creates preview cameras at an optimistic revision.</summary>
    public async ValueTask<IReadOnlyList<CameraState>> CreatePreviewCamerasAsync(
        WorkspaceSessionRevision revision,
        string? cameraPath,
        bool orbit,
        int width,
        int height,
        IReadOnlyList<double> timeCodes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(timeCodes);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveSession active = await GetCurrentAsync(revision, cancellationToken)
                .ConfigureAwait(false);
            return await active.Backend.CreatePreviewCamerasAsync(
                    cameraPath,
                    orbit,
                    width,
                    height,
                    timeCodes,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Persists the live overlay and exports a flattened stage without changing session status.
    /// </summary>
    public async ValueTask<WorkspaceFinalStageResult> ExportFinalStageAsync(
        WorkspaceSessionRevision revision,
        string relativeOutputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveSession active = await GetCurrentAsync(revision, cancellationToken)
                .ConfigureAwait(false);
            string outputPath = ResolveSessionOutputPath(active, relativeOutputPath);
            await active.Backend
                .PersistOverlayAndExportAsync(outputPath, cancellationToken)
                .ConfigureAwait(false);
            return new WorkspaceFinalStageResult(
                new WorkspaceSessionSnapshot(
                    CreateInfo(active),
                    CreateManifest(active)),
                outputPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Acquires a retained render source from the active scheduler-backed backend.</summary>
    public async ValueTask<UsdStageRenderSource> AcquireRenderSourceAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ActiveSession active = _active
                ?? throw new InvalidOperationException("No workspace session is active.");
            if (active.CloseState != SessionCloseState.Active)
            {
                throw new InvalidOperationException(
                    "The workspace session teardown has started; only close retry is permitted.");
            }

            return await active.Backend
                .AcquireRenderSourceAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Closes the active session after checking its optimistic revision.</summary>
    public async ValueTask CloseAsync(
        WorkspaceSessionRevision revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveSession active = await GetForCloseAsync(revision, cancellationToken)
                .ConfigureAwait(false);
            await CloseCoreAsync(active).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Closes the active session, if any.</summary>
    public async ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_active is not null)
            {
                await CloseCoreAsync(_active).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            if (_active is not null)
            {
                await CloseCoreAsync(_active).ConfigureAwait(false);
            }

            _disposed = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<ActiveSession> GetCurrentAsync(
        WorkspaceSessionRevision revision,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ActiveSession active = _active
            ?? throw new InvalidOperationException("No workspace session is active.");
        if (!string.Equals(
                revision.SessionId,
                active.Context.SessionId,
                StringComparison.Ordinal) ||
                revision.Generation != active.Generation ||
                revision.StageRevision != active.StageRevision ||
                active.CloseState != SessionCloseState.Active)
        {
            if (active.CloseState != SessionCloseState.Active)
            {
                throw new InvalidOperationException(
                    "The workspace session teardown failed; only close retry is permitted.");
            }

            throw new WorkspaceSessionRevisionException();
        }

        ulong currentStageRevision = await active.Backend
            .GetStageRevisionAsync(cancellationToken)
            .ConfigureAwait(false);
        if (currentStageRevision != revision.StageRevision)
        {
            active.StageRevision = currentStageRevision;
            throw new WorkspaceSessionRevisionException();
        }

        return active;
    }

    private static WorkspaceCheckpoint CreateCheckpoint(long generation)
    {
        string checkpointId = Guid.NewGuid().ToString("N");
        return new WorkspaceCheckpoint(
            checkpointId,
            generation,
            Path.Combine("checkpoints", string.Concat(checkpointId, ".usda")),
            DateTimeOffset.UtcNow);
    }

    private void AddJournal(
        WorkspaceJournalKind kind,
        long generationBefore,
        long generationAfter,
        string? checkpointId,
        int operationCount,
        string? error)
    {
        if (_journal.Count >= _maximumJournalEntryCount)
        {
            throw new WorkspaceQuotaExceededException(
                $"The session journal quota of {_maximumJournalEntryCount} entries was exceeded.");
        }

        _journal.Add(new WorkspaceJournalEntry(
            checked(++_journalSequence),
            kind,
            generationBefore,
            generationAfter,
            checkpointId,
            operationCount,
            DateTimeOffset.UtcNow,
            error));
    }

    private async ValueTask PersistAsync(CancellationToken cancellationToken)
    {
        ActiveSession active = _active!;
        await active.Backend.PersistAsync(CreateManifest(active), cancellationToken)
            .ConfigureAwait(false);
    }

    private WorkspaceSessionManifest CreateManifest(ActiveSession active) =>
        new(
            active.Context.SessionId,
            active.Context.SourcePath,
            active.Context.OverlayPath,
            active.Generation,
            active.StageRevision,
            active.CreatedAt,
            _checkpoints.ToArray(),
            _journal.ToArray());

    private static WorkspaceSessionInfo CreateInfo(ActiveSession active) =>
        new(
            active.Context.SessionId,
            active.Generation,
            active.StageRevision,
            active.Context.SourcePath,
            active.Context.OutputDirectory,
            active.Context.OverlayPath,
            active.CreatedAt);

    private static string ResolveSessionOutputPath(
        ActiveSession active,
        string relativeOutputPath)
    {
        string outputDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(active.Context.OutputDirectory));
        string outputPath = WorkspacePathContainment.ResolveContainedPath(
            outputDirectory,
            relativeOutputPath);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(outputPath, active.Context.SourcePath, comparison))
        {
            throw new InvalidOperationException("Finalization cannot overwrite the source stage.");
        }

        WorkspacePathContainment.CreateContainedDirectory(
            outputDirectory,
            Path.GetDirectoryName(outputPath)!);
        WorkspacePathContainment.RejectReparsePoints(outputDirectory, outputPath);
        return outputPath;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private async ValueTask CloseCoreAsync(ActiveSession active)
    {
        bool isRetry = active.CloseState == SessionCloseState.CloseFailed;
        active.CloseState = SessionCloseState.Closing;
        if (isRetry)
        {
            WorkspaceJournalEntry[] journalBeforeRetry = _journal.ToArray();
            long sequenceBeforeRetry = _journalSequence;
            try
            {
                CompactCloseJournal();
                AddJournal(
                    WorkspaceJournalKind.SessionCloseRetry,
                    active.Generation,
                    active.Generation,
                    null,
                    0,
                    null);
                await PersistAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                _journal.Clear();
                _journal.AddRange(journalBeforeRetry);
                _journalSequence = sequenceBeforeRetry;
                active.CloseState = SessionCloseState.CloseFailed;
                throw;
            }
        }

        try
        {
            if (!active.ResourcesReleased)
            {
                await active.Backend.DisposeAsync().ConfigureAwait(false);
                active.ResourcesReleased = true;
            }

            int journalCountBeforeClosed = _journal.Count;
            long sequenceBeforeClosed = _journalSequence;
            AddJournal(
                WorkspaceJournalKind.SessionClosed,
                active.Generation,
                active.Generation,
                null,
                0,
                null);
            try
            {
                await PersistAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                _journal.RemoveRange(
                    journalCountBeforeClosed,
                    _journal.Count - journalCountBeforeClosed);
                _journalSequence = sequenceBeforeClosed;
                throw;
            }

            if (ReferenceEquals(_active, active))
            {
                _active = null;
                _pendingCheckpointCleanup = null;
            }
        }
        catch (Exception closeError)
        {
            active.CloseAttemptCount++;
            active.CloseState = SessionCloseState.CloseFailed;
            AddJournal(
                WorkspaceJournalKind.SessionCloseFailed,
                active.Generation,
                active.Generation,
                null,
                0,
                $"Close attempt {active.CloseAttemptCount} failed: {closeError.Message}");
            try
            {
                await PersistAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception journalError)
            {
                throw new AggregateException(
                    "Session teardown failed and its failure journal could not be persisted.",
                    closeError,
                    journalError);
            }

            throw;
        }
    }

    private async ValueTask<ActiveSession> GetForCloseAsync(
        WorkspaceSessionRevision revision,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ActiveSession active = _active
            ?? throw new InvalidOperationException("No workspace session is active.");
        if (active.CloseState == SessionCloseState.Active)
        {
            return await GetCurrentAsync(revision, cancellationToken).ConfigureAwait(false);
        }

        if (active.CloseState != SessionCloseState.CloseFailed ||
            !string.Equals(revision.SessionId, active.Context.SessionId, StringComparison.Ordinal) ||
            revision.Generation != active.Generation ||
            revision.StageRevision != active.StageRevision)
        {
            throw new WorkspaceSessionRevisionException();
        }

        return active;
    }

    private void EnsureMutationCapacity(int checkpointCount, int journalEntryCount)
    {
        if (_checkpoints.Count > _maximumCheckpointCount - checkpointCount)
        {
            throw new WorkspaceQuotaExceededException(
                $"The session checkpoint quota of {_maximumCheckpointCount} was exceeded.");
        }

        int normalJournalLimit = _maximumJournalEntryCount - CloseJournalReserve;
        if (_journal.Count > normalJournalLimit - journalEntryCount)
        {
            throw new WorkspaceQuotaExceededException(
                $"The session journal quota of {_maximumJournalEntryCount} entries was exceeded.");
        }
    }

    private void CompactCloseJournal()
    {
        int firstCloseEntry = _journal.FindIndex(
            static entry => IsCloseJournalKind(entry.Kind));
        if (firstCloseEntry < 0)
        {
            return;
        }

        WorkspaceJournalEntry? latestFailure = _journal
            .LastOrDefault(static entry =>
                entry.Kind == WorkspaceJournalKind.SessionCloseFailed);
        _journal.RemoveRange(firstCloseEntry, _journal.Count - firstCloseEntry);
        if (latestFailure is not null)
        {
            _journal.Add(latestFailure);
        }
    }

    private static bool IsCloseJournalKind(WorkspaceJournalKind kind) =>
        kind is
            WorkspaceJournalKind.SessionCloseFailed or
            WorkspaceJournalKind.SessionCloseRetry or
            WorkspaceJournalKind.SessionClosed;

    private async ValueTask RecoverPendingCheckpointCleanupAsync(ActiveSession active)
    {
        PendingCheckpointCleanup? pending = _pendingCheckpointCleanup;
        if (pending is null)
        {
            return;
        }

        try
        {
            await active.Backend
                .DeleteCheckpointAsync(pending.Checkpoint, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception cleanupError)
        {
            throw new AggregateException(
                "The checkpoint manifest failed and its checkpoint file still could not be deleted.",
                pending.PersistenceError,
                cleanupError);
        }

        RevertCheckpointTracking(
            pending.CheckpointCountBefore,
            pending.JournalCountBefore,
            pending.JournalSequenceBefore);
        _pendingCheckpointCleanup = null;
    }

    private void RevertCheckpointTracking(
        int checkpointCountBefore,
        int journalCountBefore,
        long journalSequenceBefore)
    {
        _checkpoints.RemoveRange(
            checkpointCountBefore,
            _checkpoints.Count - checkpointCountBefore);
        _journal.RemoveRange(
            journalCountBefore,
            _journal.Count - journalCountBefore);
        _journalSequence = journalSequenceBefore;
    }

    private sealed record PendingCheckpointCleanup(
        WorkspaceCheckpoint Checkpoint,
        int CheckpointCountBefore,
        int JournalCountBefore,
        long JournalSequenceBefore,
        Exception PersistenceError);

    private sealed class ActiveSession
    {
        internal ActiveSession(
            WorkspaceSessionBackendContext context,
            IWorkspaceSessionBackend backend,
            DateTimeOffset createdAt,
            ulong stageRevision)
        {
            Context = context;
            Backend = backend;
            CreatedAt = createdAt;
            StageRevision = stageRevision;
        }

        internal IWorkspaceSessionBackend Backend { get; }

        internal WorkspaceSessionBackendContext Context { get; }

        internal DateTimeOffset CreatedAt { get; }

        internal int CloseAttemptCount { get; set; }

        internal SessionCloseState CloseState { get; set; }

        internal long Generation { get; set; }

        internal bool ResourcesReleased { get; set; }

        internal ulong StageRevision { get; set; }
    }

    private enum SessionCloseState
    {
        Active,
        Closing,
        CloseFailed,
    }
}
