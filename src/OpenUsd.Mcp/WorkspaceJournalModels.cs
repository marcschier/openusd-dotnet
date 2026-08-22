// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

/// <summary>Identifies a durable workspace journal event.</summary>
public enum WorkspaceJournalKind
{
    /// <summary>A session was created.</summary>
    SessionCreated,

    /// <summary>An edit batch committed.</summary>
    EditCommitted,

    /// <summary>An edit batch failed and was rolled back.</summary>
    EditFailed,

    /// <summary>An explicit checkpoint was created.</summary>
    CheckpointCreated,

    /// <summary>An explicit rollback committed.</summary>
    RollbackCommitted,

    /// <summary>An explicit rollback failed or was restored after journal failure.</summary>
    RollbackFailed,

    /// <summary>A session resource-release attempt failed.</summary>
    SessionCloseFailed,

    /// <summary>A failed session teardown was retried.</summary>
    SessionCloseRetry,

    /// <summary>The active session closed cleanly.</summary>
    SessionClosed
}

/// <summary>Describes one ordered journal event.</summary>
public sealed record WorkspaceJournalEntry(
    long Sequence,
    WorkspaceJournalKind Kind,
    long GenerationBefore,
    long GenerationAfter,
    string? CheckpointId,
    int OperationCount,
    DateTimeOffset RecordedAt,
    string? Error);

/// <summary>Contains the durable state needed to recover or inspect a workspace session.</summary>
public sealed record WorkspaceSessionManifest(
    string SessionId,
    string SourcePath,
    string OverlayPath,
    long Generation,
    ulong StageRevision,
    DateTimeOffset CreatedAt,
    WorkspaceCheckpoint[] Checkpoints,
    WorkspaceJournalEntry[] Journal);
