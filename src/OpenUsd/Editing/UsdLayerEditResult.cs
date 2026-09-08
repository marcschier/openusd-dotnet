// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Editing;

/// <summary>A completed native comparison outcome, distinct from native validation/operation errors.</summary>
public enum UsdLayerEditOutcome
{
    /// <summary>The operation applied and returned its actual after state.</summary>
    Applied = 0,
    /// <summary>Affected state changed, or owned cleanup would destroy unrelated data.</summary>
    Conflict = 1,
    /// <summary>The identity or generation no longer identifies a current local target.</summary>
    StaleTarget = 2,
    /// <summary>Permissions or the native bounded backing do not support editing.</summary>
    NotEditable = 3
}

/// <summary>An immutable detached per-edit result.</summary>
public sealed class UsdLayerEditResult : IUsdDetachedResult
{
    internal UsdLayerEditResult(
        UsdLayerEditOutcome outcome, UsdLayerAuthoredSnapshot? afterSnapshot, string? diagnostic)
    {
        Outcome = outcome;
        AfterSnapshot = afterSnapshot;
        Diagnostic = diagnostic;
    }

    /// <summary>Gets the comparison outcome.</summary>
    public UsdLayerEditOutcome Outcome { get; }
    /// <summary>Gets the actual resulting authored state on Applied, otherwise null.</summary>
    public UsdLayerAuthoredSnapshot? AfterSnapshot { get; }
    /// <summary>Gets a bounded native conflict/restriction diagnostic, when available.</summary>
    public string? Diagnostic { get; }
}

/// <summary>An immutable result of explicit whole-review checkpoint restoration, not per-edit undo.</summary>
public sealed class UsdLayerCheckpointRestoreResult : IUsdDetachedResult
{
    internal UsdLayerCheckpointRestoreResult(
        UsdLayerEditOutcome outcome, UsdLayerCheckpoint? afterCheckpoint, string? diagnostic)
    {
        Outcome = outcome;
        AfterCheckpoint = afterCheckpoint;
        Diagnostic = diagnostic;
    }

    /// <summary>Gets the comparison outcome.</summary>
    public UsdLayerEditOutcome Outcome { get; }
    /// <summary>Gets the resulting checkpoint with its new generation on Applied, otherwise null.</summary>
    public UsdLayerCheckpoint? AfterCheckpoint { get; }
    /// <summary>Gets a bounded native conflict/restriction diagnostic, when available.</summary>
    public string? Diagnostic { get; }
}
