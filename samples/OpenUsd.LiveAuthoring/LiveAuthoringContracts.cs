// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace OpenUsd.LiveAuthoring;

/// <summary>Receives ordered, renderer-neutral live-authoring batches.</summary>
public interface ILiveAuthoringSink
{
    /// <summary>Queues a batch and completes after it has been applied or superseded.</summary>
    ValueTask<LiveAuthoringBatchResult> ApplyAsync(
        LiveAuthoringBatch batch,
        CancellationToken cancellationToken = default);
}

/// <summary>Applies one admitted batch to its destination.</summary>
public interface ILiveAuthoringBatchExecutor : IAsyncDisposable
{
    /// <summary>Applies a batch after queue ordering and backpressure have been enforced.</summary>
    ValueTask<LiveAuthoringBatchResult> ExecuteAsync(
        LiveAuthoringBatch batch,
        CancellationToken cancellationToken);
}

/// <summary>Consumes the exact scheduler-owned stage identity used for rendering.</summary>
public interface IUsdStageRenderSourceConsumer : IAsyncDisposable
{
    /// <summary>
    /// Attaches to the scheduler and retained render source. The consumer must not reopen a stage path.
    /// </summary>
    ValueTask AttachAsync(
        UsdStageScheduler scheduler,
        UsdStageRenderSource renderSource,
        CancellationToken cancellationToken);
}

/// <summary>Selects the layer used for live edits.</summary>
public enum LiveAuthoringEditLayer
{
    /// <summary>Authors transient edits into the stage session layer.</summary>
    Session,

    /// <summary>Authors persistent edits into the root layer.</summary>
    Root
}

/// <summary>Selects whether a host creates or opens its scheduler-owned stage.</summary>
public enum LiveAuthoringStageMode
{
    /// <summary>Create a new file-backed stage.</summary>
    Create,

    /// <summary>Open an existing stage.</summary>
    Open
}

/// <summary>Configures bounded live-authoring and scheduler queues.</summary>
public sealed class UsdLiveAuthoringOptions
{
    /// <summary>Gets or sets the maximum number of batches waiting behind the active edit.</summary>
    public int QueueCapacity { get; set; } = 64;

    /// <summary>Gets or sets the scheduler operation queue capacity.</summary>
    public int SchedulerCapacity { get; set; } = 1024;

    /// <summary>Gets or sets the scheduler change-notification queue capacity.</summary>
    public int NotificationCapacity { get; set; } = 64;

    /// <summary>Gets or sets the default edit layer.</summary>
    public LiveAuthoringEditLayer EditLayer { get; set; } = LiveAuthoringEditLayer.Session;
}

/// <summary>Identifies the scalar payload carried by a property update.</summary>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "The names describe the exact OpenUSD scalar representation.")]
public enum LiveScalarKind
{
    /// <summary>A Boolean value.</summary>
    Boolean,

    /// <summary>A signed 64-bit integer.</summary>
    Int64,

    /// <summary>A double-precision value.</summary>
    Double,

    /// <summary>A string value.</summary>
    String,

    /// <summary>An OpenUSD token value.</summary>
    Token,

    /// <summary>A three-component single-precision vector.</summary>
    Vec3f
}

/// <summary>A NativeAOT-safe scalar value without domain-specific or boxed payloads.</summary>
[SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "The properties describe the exact OpenUSD scalar representation.")]
public readonly record struct LiveScalarValue
{
    private LiveScalarValue(
        LiveScalarKind kind,
        bool boolean,
        long integer,
        double number,
        string? text,
        UsdVec3f vector)
    {
        Kind = kind;
        Boolean = boolean;
        Int64Value = integer;
        DoubleValue = number;
        Text = text;
        Vec3f = vector;
    }

    /// <summary>Gets the active payload kind.</summary>
    public LiveScalarKind Kind { get; }

    /// <summary>Gets the Boolean payload.</summary>
    public bool Boolean { get; }

    /// <summary>Gets the integer payload.</summary>
    public long Int64Value { get; }

    /// <summary>Gets the double payload.</summary>
    public double DoubleValue { get; }

    /// <summary>Gets the string or token payload.</summary>
    public string? Text { get; }

    /// <summary>Gets the vector payload.</summary>
    public UsdVec3f Vec3f { get; }

    /// <summary>Creates a Boolean value.</summary>
    public static LiveScalarValue FromBoolean(bool value) =>
        new(LiveScalarKind.Boolean, value, 0, 0, null, default);

    /// <summary>Creates an integer value.</summary>
    public static LiveScalarValue FromInt64(long value) =>
        new(LiveScalarKind.Int64, false, value, 0, null, default);

    /// <summary>Creates a double value.</summary>
    public static LiveScalarValue FromDouble(double value) =>
        new(LiveScalarKind.Double, false, 0, value, null, default);

    /// <summary>Creates a string value.</summary>
    public static LiveScalarValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(LiveScalarKind.String, false, 0, 0, value, default);
    }

    /// <summary>Creates a token value.</summary>
    public static LiveScalarValue FromToken(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new(LiveScalarKind.Token, false, 0, 0, value, default);
    }

    /// <summary>Creates a vec3f value.</summary>
    public static LiveScalarValue FromVec3f(UsdVec3f value) =>
        new(LiveScalarKind.Vec3f, false, 0, 0, null, value);
}

/// <summary>Base type for data-only stage updates.</summary>
public abstract record LiveStageUpdate
{
    private protected LiveStageUpdate()
    {
    }

    /// <summary>Gets the renderer invalidation required by this update.</summary>
    public abstract UsdStageInvalidationKind Invalidation { get; }
}

/// <summary>Defines or redefines a prim.</summary>
public sealed record DefinePrimUpdate(string PrimPath, string? TypeName = null) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Topology;
}

/// <summary>Removes a prim and its descendants.</summary>
public sealed record RemovePrimUpdate(string PrimPath) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Topology;
}

/// <summary>Authors a scalar default value or time sample.</summary>
public sealed record SetScalarUpdate(
    string PrimPath,
    string AttributeName,
    LiveScalarValue Value,
    double? TimeCode = null) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Property;
}

/// <summary>Creates a relationship and replaces its targets.</summary>
public sealed record SetRelationshipTargetsUpdate(
    string PrimPath,
    string RelationshipName,
    IReadOnlyList<string> Targets) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Topology;
}

/// <summary>Replaces authored references with one asset reference.</summary>
public sealed record SetReferenceUpdate(
    string PrimPath,
    string? AssetPath,
    string? TargetPrimPath = null) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Composition;
}

/// <summary>Replaces authored payloads with one asset payload.</summary>
public sealed record SetPayloadUpdate(
    string PrimPath,
    string? AssetPath,
    string? TargetPrimPath = null) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Composition;
}

/// <summary>Authors prim active state.</summary>
public sealed record SetActiveUpdate(string PrimPath, bool Active) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Topology;
}

/// <summary>Authors prim instanceability.</summary>
public sealed record SetInstanceableUpdate(string PrimPath, bool Instanceable) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Composition;
}

/// <summary>Authors a known variant set and its selection.</summary>
public sealed record SetVariantSelectionUpdate(
    string PrimPath,
    string VariantSetName,
    IReadOnlyList<string> KnownVariants,
    string? Selection) : LiveStageUpdate
{
    /// <inheritdoc/>
    public override UsdStageInvalidationKind Invalidation => UsdStageInvalidationKind.Composition;
}

/// <summary>An ordered, optionally supersedable group of stage updates.</summary>
public sealed class LiveAuthoringBatch
{
    private readonly ReadOnlyCollection<LiveStageUpdate> _updates;

    /// <summary>Initializes an ordered update batch.</summary>
    public LiveAuthoringBatch(
        long sequence,
        IEnumerable<LiveStageUpdate> updates,
        string? coalescingKey = null)
    {
        ArgumentNullException.ThrowIfNull(updates);
        LiveStageUpdate[] materialized = updates.ToArray();
        LiveAuthoringValidation.Validate(
            sequence,
            materialized,
            coalescingKey,
            nameof(updates));

        Sequence = sequence;
        CoalescingKey = coalescingKey;
        LiveStageUpdate[] snapshots = materialized.Select(Snapshot).ToArray();
        _updates = Array.AsReadOnly(snapshots);
        Invalidation = snapshots.Max(static update => update.Invalidation);
    }

    /// <summary>Gets the strictly increasing producer sequence.</summary>
    public long Sequence { get; }

    /// <summary>
    /// Gets the optional snapshot key. A newer pending batch with the same key may supersede this one.
    /// </summary>
    public string? CoalescingKey { get; }

    /// <summary>Gets the updates in application order.</summary>
    public IReadOnlyList<LiveStageUpdate> Updates => _updates;

    /// <summary>Gets the strongest renderer invalidation in the batch.</summary>
    public UsdStageInvalidationKind Invalidation { get; }

    private static LiveStageUpdate Snapshot(LiveStageUpdate update) =>
        update switch
        {
            SetRelationshipTargetsUpdate relationship => relationship with
            {
                Targets = Array.AsReadOnly(relationship.Targets.ToArray())
            },
            SetVariantSelectionUpdate variant => variant with
            {
                KnownVariants = Array.AsReadOnly(variant.KnownVariants.ToArray())
            },
            _ => update
        };
}

/// <summary>A detached result safe to return from a scheduler callback.</summary>
public readonly record struct LiveAuthoringBatchResult(
    long FirstSequence,
    long LastSequence,
    int BatchCount,
    int UpdateCount,
    UsdStageInvalidationKind Invalidation,
    ulong BeforeChangeSerial,
    ulong AfterChangeSerial,
    string EditTargetLayerIdentifier) : IUsdDetachedResult;
