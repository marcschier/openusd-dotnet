// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

/// <summary>Configures the canonical roots and edit bounds of an MCP session workspace.</summary>
public sealed class McpSessionWorkspaceOptions
{
    /// <summary>Initializes workspace options.</summary>
    public McpSessionWorkspaceOptions(
        string sourceRoot,
        string outputRoot,
        int maximumBatchOperationCount = 128,
        int maximumCheckpointCount = 256,
        int maximumJournalEntryCount = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBatchOperationCount);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCheckpointCount);
        if (maximumJournalEntryCount < McpSessionWorkspace.MinimumJournalEntryCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumJournalEntryCount),
                maximumJournalEntryCount,
                $"The journal quota must be at least {McpSessionWorkspace.MinimumJournalEntryCount}.");
        }

        SourceRoot = sourceRoot;
        OutputRoot = outputRoot;
        MaximumBatchOperationCount = maximumBatchOperationCount;
        MaximumCheckpointCount = maximumCheckpointCount;
        MaximumJournalEntryCount = maximumJournalEntryCount;
    }

    /// <summary>Gets the configured read-only source root.</summary>
    public string SourceRoot { get; }

    /// <summary>Gets the configured writable output root.</summary>
    public string OutputRoot { get; }

    /// <summary>Gets the maximum number of operations accepted in one batch.</summary>
    public int MaximumBatchOperationCount { get; }

    /// <summary>Gets the maximum number of retained checkpoints in one session.</summary>
    public int MaximumCheckpointCount { get; }

    /// <summary>Gets the maximum number of retained journal entries in one session.</summary>
    public int MaximumJournalEntryCount { get; }
}

/// <summary>Reports that a bounded per-session workspace quota would be exceeded.</summary>
public sealed class WorkspaceQuotaExceededException : InvalidOperationException
{
    /// <summary>Initializes a workspace quota error.</summary>
    public WorkspaceQuotaExceededException(string message)
        : base(message)
    {
    }
}

/// <summary>Identifies the current optimistic revision of an active session.</summary>
public sealed record WorkspaceSessionRevision
{
    /// <summary>Initializes an optimistic session revision.</summary>
    public WorkspaceSessionRevision(string sessionId, long generation, ulong stageRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        SessionId = sessionId;
        Generation = generation;
        StageRevision = stageRevision;
    }

    /// <summary>Gets the session identifier.</summary>
    public string SessionId { get; }

    /// <summary>Gets the transactional session generation.</summary>
    public long Generation { get; }

    /// <summary>Gets the native stage change serial.</summary>
    public ulong StageRevision { get; }
}

/// <summary>Describes a newly active workspace session.</summary>
public sealed record WorkspaceSessionInfo(
    string SessionId,
    long Generation,
    ulong StageRevision,
    string SourcePath,
    string OutputDirectory,
    string OverlayPath,
    DateTimeOffset CreatedAt);

/// <summary>Reports a committed transactional edit.</summary>
public sealed record WorkspaceEditResult(
    string SessionId,
    long Generation,
    ulong StageRevision,
    WorkspaceCheckpoint Checkpoint,
    int OperationCount);

/// <summary>Reports a committed rollback.</summary>
public sealed record WorkspaceRollbackResult(
    string SessionId,
    long Generation,
    ulong StageRevision,
    string CheckpointId);

/// <summary>Reports whether the workspace currently owns one active session.</summary>
public sealed record WorkspaceSessionStatus(
    bool IsActive,
    WorkspaceSessionInfo? Session);

/// <summary>Contains detached status and journal state for the active session.</summary>
public sealed record WorkspaceSessionSnapshot(
    WorkspaceSessionInfo Session,
    WorkspaceSessionManifest Manifest);

/// <summary>Contains detached bounded scene statistics produced on the stage scheduler.</summary>
public sealed record WorkspaceSceneStatistics(
    string DefaultPrimPath,
    int PrimCount,
    int MeshCount,
    long CurveVertexCount,
    long MeshVertexCount,
    long FaceCount,
    int RootPrimCount,
    int LeafPrimCount,
    int MaximumDepth) : IUsdDetachedResult;

/// <summary>Contains session state captured with a scheduler-owned final stage export.</summary>
public sealed record WorkspaceFinalStageResult(
    WorkspaceSessionSnapshot Snapshot,
    string FinalStagePath);

/// <summary>Reports a stale or foreign optimistic session revision.</summary>
public sealed class WorkspaceSessionRevisionException : InvalidOperationException
{
    /// <summary>Initializes a session revision error.</summary>
    public WorkspaceSessionRevisionException()
        : base("The session ID, generation, or stage revision is stale.")
    {
    }
}
