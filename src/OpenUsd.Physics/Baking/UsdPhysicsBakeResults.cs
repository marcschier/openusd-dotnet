// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;

namespace OpenUsd.Physics.Baking;

/// <summary>
/// Reports why one record in a batch was applied, skipped, or rejected.
/// </summary>
public enum UsdPhysicsBakeRecordStatus
{
    /// <summary>The record was authored into the destination layer.</summary>
    Applied,

    /// <summary>The record was intentionally left unauthored by the configured policy.</summary>
    Skipped,

    /// <summary>The bound prim path does not resolve on the stage.</summary>
    PathMissing,

    /// <summary>The bound prim is not transformable.</summary>
    NotTransformable,

    /// <summary>The bound prim is not point based.</summary>
    NotPointBased,

    /// <summary>
    /// The bound prim is an instance proxy, so authoring it would silently do nothing or corrupt
    /// the composition of every sibling instance.
    /// </summary>
    InstanceProxy,

    /// <summary>
    /// The bound prim lives inside a prototype, so authoring it would leak simulated state into
    /// every instance that shares the prototype.
    /// </summary>
    InPrototype,

    /// <summary>The sample capacity does not match the composed topology.</summary>
    SampleCountMismatch,

    /// <summary>The destination layer already holds this time sample and the policy rejects it.</summary>
    ExistingSample,

    /// <summary>The record kind is not supported by this runtime.</summary>
    UnsupportedKind,

    /// <summary>Authoring the record failed inside the runtime.</summary>
    AuthoringFailed,

    /// <summary>The record is malformed.</summary>
    InvalidRecord,

    /// <summary>The record's identity is not bound to any extracted prim.</summary>
    IdentityUnbound,

    /// <summary>The record's topology revision no longer matches the extracted binding.</summary>
    StaleTopology
}

/// <summary>
/// Reports the outcome of one record in a physics preview or bake.
/// </summary>
/// <param name="Id">The stable identity the record addressed.</param>
/// <param name="Status">The record outcome.</param>
/// <param name="Detail">
/// An outcome-specific detail value, such as the composed sample count that a
/// <see cref="UsdPhysicsBakeRecordStatus.SampleCountMismatch"/> was measured against.
/// </param>
public readonly record struct UsdPhysicsBakeRecordOutcome(
    UsdPhysicsObjectId Id,
    UsdPhysicsBakeRecordStatus Status,
    int Detail = 0) : IUsdDetachedResult;

/// <summary>
/// Describes the destination layer a bake was preflighted against.
/// </summary>
/// <param name="Identifier">The layer identifier that was resolved.</param>
/// <param name="Exists">Whether the layer resolved at all.</param>
/// <param name="IsLocal">Whether the layer participates in the stage local layer stack.</param>
/// <param name="IsAnonymous">Whether the layer is anonymous rather than file backed.</param>
/// <param name="IsMuted">Whether the stage currently mutes the layer.</param>
/// <param name="IsEditable">Whether the layer permits authoring.</param>
/// <param name="IsSaveable">Whether the layer permits saving.</param>
/// <param name="IsRootLayer">Whether the layer is the stage root layer.</param>
/// <param name="IsSessionLayer">Whether the layer is the stage session layer.</param>
/// <param name="IsFileBacked">Whether the layer resolves to a file on disk.</param>
/// <param name="IsDirty">Whether the layer holds unsaved edits.</param>
public readonly record struct UsdPhysicsBakeLayerInfo(
    string Identifier,
    bool Exists,
    bool IsLocal,
    bool IsAnonymous,
    bool IsMuted,
    bool IsEditable,
    bool IsSaveable,
    bool IsRootLayer,
    bool IsSessionLayer,
    bool IsFileBacked,
    bool IsDirty) : IUsdDetachedResult;

/// <summary>
/// Identifies one stage change a physics preview produced, by the exact change serial pair the
/// scheduler publishes for it.
/// </summary>
/// <remarks>
/// <para>
/// The serials are read on the scheduler's own thread, immediately around the single call that can
/// move them, so they are the same values the scheduler samples before and after the callback. A
/// listener on the stage change feed can therefore suppress its own preview edits exactly, by
/// matching both serials of a published <c>UsdStageChange</c> against a reported edit, instead of
/// guessing from serials read next to the call.
/// </para>
/// <para>
/// Only edits that actually moved the serial are reported, because the scheduler publishes nothing
/// for a callback that changed nothing. Every reported edit therefore corresponds to exactly one
/// published change.
/// </para>
/// </remarks>
/// <param name="BeforeChangeSerial">The stage change serial before the edit.</param>
/// <param name="AfterChangeSerial">The stage change serial after the edit.</param>
/// <param name="Invalidation">The invalidation kind the edit was scheduled with.</param>
public readonly record struct UsdPhysicsPreviewEdit(
    ulong BeforeChangeSerial,
    ulong AfterChangeSerial,
    UsdStageInvalidationKind Invalidation) : IUsdDetachedResult;

/// <summary>
/// Reports the immutable result of applying one batch to the physics session overlay.
/// </summary>
public sealed record UsdPhysicsPreviewResult : IUsdDetachedResult
{
    private readonly ImmutableArray<UsdPhysicsBakeRecordOutcome> _outcomes;
    private readonly ImmutableArray<UsdPhysicsPreviewEdit> _edits;

    /// <summary>Initializes a preview result by defensively copying outcomes.</summary>
    /// <param name="status">The overall preview outcome.</param>
    /// <param name="appliedCount">The number of records authored into the overlay.</param>
    /// <param name="skippedCount">The number of records intentionally left unauthored.</param>
    /// <param name="rejectedCount">The number of records that could not be authored.</param>
    /// <param name="authoredAttributeCount">The number of attributes authored.</param>
    /// <param name="outcomes">The per-record outcomes in authoring order.</param>
    /// <param name="diagnostics">The diagnostics produced while applying.</param>
    /// <param name="edits">The stage changes this apply produced, in order, or none.</param>
    public UsdPhysicsPreviewResult(
        UsdPhysicsBakeStatus status,
        int appliedCount,
        int skippedCount,
        int rejectedCount,
        int authoredAttributeCount,
        IEnumerable<UsdPhysicsBakeRecordOutcome> outcomes,
        UsdPhysicsDiagnostics diagnostics,
        IEnumerable<UsdPhysicsPreviewEdit>? edits = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(appliedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(skippedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(rejectedCount);
        ArgumentOutOfRangeException.ThrowIfNegative(authoredAttributeCount);
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Status = status;
        AppliedCount = appliedCount;
        SkippedCount = skippedCount;
        RejectedCount = rejectedCount;
        AuthoredAttributeCount = authoredAttributeCount;
        _outcomes = [.. outcomes];
        _edits = edits is null ? [] : [.. edits];
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the overall preview outcome.</summary>
    public UsdPhysicsBakeStatus Status { get; }

    /// <summary>Gets the number of records authored into the overlay.</summary>
    public int AppliedCount { get; }

    /// <summary>Gets the number of records intentionally left unauthored.</summary>
    public int SkippedCount { get; }

    /// <summary>Gets the number of records that could not be authored.</summary>
    public int RejectedCount { get; }

    /// <summary>Gets the number of attributes authored.</summary>
    public int AuthoredAttributeCount { get; }

    /// <summary>Gets the per-record outcomes in authoring order.</summary>
    public IReadOnlyList<UsdPhysicsBakeRecordOutcome> Outcomes => _outcomes;

    /// <summary>
    /// Gets the stage changes this apply produced, one per authored chunk that moved the stage
    /// change serial, in the order they were published.
    /// </summary>
    public IReadOnlyList<UsdPhysicsPreviewEdit> Edits => _edits;

    /// <summary>Gets the diagnostics produced while applying.</summary>
    public UsdPhysicsDiagnostics Diagnostics { get; }
}

/// <summary>
/// Reports the immutable result of clearing every preview opinion from the physics overlay.
/// </summary>
public sealed record UsdPhysicsPreviewClearResult : IUsdDetachedResult
{
    private readonly ImmutableArray<UsdPhysicsPreviewEdit> _edits;

    /// <summary>Initializes a clear result by defensively copying edits.</summary>
    /// <param name="status">The overall clear outcome.</param>
    /// <param name="migratedUserOpinions">
    /// Whether opinions authored directly into the session container were migrated into the
    /// overlay's user layer before the physics layer was cleared.
    /// </param>
    /// <param name="edits">The stage changes this clear produced, in order, or none.</param>
    public UsdPhysicsPreviewClearResult(
        UsdPhysicsBakeStatus status,
        bool migratedUserOpinions,
        IEnumerable<UsdPhysicsPreviewEdit>? edits = null)
    {
        Status = status;
        MigratedUserOpinions = migratedUserOpinions;
        _edits = edits is null ? [] : [.. edits];
    }

    /// <summary>Gets the overall clear outcome.</summary>
    public UsdPhysicsBakeStatus Status { get; }

    /// <summary>Gets whether user opinions were migrated out of the session container.</summary>
    public bool MigratedUserOpinions { get; }

    /// <summary>
    /// Gets the stage changes this clear produced. Contamination migration and the physics layer
    /// clear run inside one scheduled edit, so at most one change is reported.
    /// </summary>
    public IReadOnlyList<UsdPhysicsPreviewEdit> Edits => _edits;
}

/// <summary>
/// Reports the immutable result of preflighting a bake destination without mutating anything.
/// </summary>
public sealed record UsdPhysicsBakePreflightResult : IUsdDetachedResult
{
    private readonly ImmutableArray<UsdPhysicsBakeRecordOutcome> _outcomes;

    /// <summary>Initializes a preflight result by defensively copying outcomes.</summary>
    /// <param name="canBake">Whether the bake may proceed.</param>
    /// <param name="layer">The destination layer that was inspected.</param>
    /// <param name="sampleCount">The number of samples the bake would author.</param>
    /// <param name="outcomes">The per-record outcomes measured without mutating the stage.</param>
    /// <param name="diagnostics">The diagnostics explaining every rejection.</param>
    public UsdPhysicsBakePreflightResult(
        bool canBake,
        UsdPhysicsBakeLayerInfo layer,
        int sampleCount,
        IEnumerable<UsdPhysicsBakeRecordOutcome> outcomes,
        UsdPhysicsDiagnostics diagnostics)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleCount);
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(diagnostics);

        CanBake = canBake;
        Layer = layer;
        SampleCount = sampleCount;
        _outcomes = [.. outcomes];
        Diagnostics = diagnostics;
    }

    /// <summary>Gets a value indicating whether the bake may proceed.</summary>
    public bool CanBake { get; }

    /// <summary>Gets the destination layer that was inspected.</summary>
    public UsdPhysicsBakeLayerInfo Layer { get; }

    /// <summary>Gets the number of samples the bake would author.</summary>
    public int SampleCount { get; }

    /// <summary>Gets the per-record outcomes measured without mutating the stage.</summary>
    public IReadOnlyList<UsdPhysicsBakeRecordOutcome> Outcomes => _outcomes;

    /// <summary>Gets the diagnostics explaining every rejection.</summary>
    public UsdPhysicsDiagnostics Diagnostics { get; }
}

/// <summary>
/// Reports the immutable result of one transactional bake.
/// </summary>
public sealed record UsdPhysicsBakeTransactionResult : IUsdDetachedResult
{
    private readonly ImmutableArray<UsdPhysicsBakeRecordOutcome> _outcomes;

    /// <summary>Initializes a bake result by defensively copying outcomes.</summary>
    /// <param name="status">The overall bake outcome.</param>
    /// <param name="layer">The destination layer the bake targeted.</param>
    /// <param name="sampleCount">The number of authored time samples.</param>
    /// <param name="recordCount">The number of authored records across every sample.</param>
    /// <param name="authoredAttributeCount">The number of authored attributes.</param>
    /// <param name="wasRolledBack">Whether the destination layer was restored to its prior content.</param>
    /// <param name="wasSaved">Whether the destination layer was saved.</param>
    /// <param name="outcomes">The per-record outcomes of the sample that ended the bake.</param>
    /// <param name="diagnostics">The diagnostics produced while baking.</param>
    public UsdPhysicsBakeTransactionResult(
        UsdPhysicsBakeStatus status,
        UsdPhysicsBakeLayerInfo layer,
        int sampleCount,
        int recordCount,
        int authoredAttributeCount,
        bool wasRolledBack,
        bool wasSaved,
        IEnumerable<UsdPhysicsBakeRecordOutcome> outcomes,
        UsdPhysicsDiagnostics diagnostics)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleCount);
        ArgumentOutOfRangeException.ThrowIfNegative(recordCount);
        ArgumentOutOfRangeException.ThrowIfNegative(authoredAttributeCount);
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(diagnostics);

        Status = status;
        Layer = layer;
        SampleCount = sampleCount;
        RecordCount = recordCount;
        AuthoredAttributeCount = authoredAttributeCount;
        WasRolledBack = wasRolledBack;
        WasSaved = wasSaved;
        _outcomes = [.. outcomes];
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the overall bake outcome.</summary>
    public UsdPhysicsBakeStatus Status { get; }

    /// <summary>Gets the destination layer the bake targeted.</summary>
    public UsdPhysicsBakeLayerInfo Layer { get; }

    /// <summary>Gets the number of authored time samples.</summary>
    public int SampleCount { get; }

    /// <summary>Gets the number of authored records across every sample.</summary>
    public int RecordCount { get; }

    /// <summary>Gets the number of authored attributes.</summary>
    public int AuthoredAttributeCount { get; }

    /// <summary>
    /// Gets a value indicating whether the destination layer was restored to the exact content it
    /// held before the bake began.
    /// </summary>
    public bool WasRolledBack { get; }

    /// <summary>Gets a value indicating whether the destination layer was saved.</summary>
    public bool WasSaved { get; }

    /// <summary>Gets the per-record outcomes of the sample that ended the bake.</summary>
    public IReadOnlyList<UsdPhysicsBakeRecordOutcome> Outcomes => _outcomes;

    /// <summary>Gets the diagnostics produced while baking.</summary>
    public UsdPhysicsDiagnostics Diagnostics { get; }
}

/// <summary>
/// Reports bake progress between two bounded chunks.
/// </summary>
/// <param name="CompletedSamples">The number of fully authored time samples.</param>
/// <param name="TotalSamples">The number of time samples the bake will author.</param>
/// <param name="TimeCode">The time code of the most recently authored sample.</param>
/// <param name="CompletedRecords">The number of authored records so far.</param>
public readonly record struct UsdPhysicsBakeProgress(
    int CompletedSamples,
    int TotalSamples,
    double TimeCode,
    int CompletedRecords) : IUsdDetachedResult;
