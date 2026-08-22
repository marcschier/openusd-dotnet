// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Mcp;

/// <summary>Describes an immutable overlay checkpoint.</summary>
public sealed record WorkspaceCheckpoint(
    string CheckpointId,
    long Generation,
    string FileName,
    DateTimeOffset CreatedAt);

/// <summary>Reports a completed checkpoint operation.</summary>
public sealed record WorkspaceCheckpointResult(
    string SessionId,
    long Generation,
    ulong StageRevision,
    WorkspaceCheckpoint Checkpoint);
