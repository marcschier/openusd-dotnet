// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;

namespace OpenUsd.Physics;

/// <summary>
/// Describes one immutable synchronous <see cref="UsdPhysicsSession.Step"/> request.
/// </summary>
public sealed record UsdPhysicsStepRequest
{
    private readonly ImmutableArray<UsdPhysicsCommand> _commands;
    private readonly ImmutableArray<UsdPhysicsQueryRequest> _queries;

    /// <summary>Initializes a validated step request by defensively copying commands and queries.</summary>
    public UsdPhysicsStepRequest(
        double deltaSeconds,
        int maxSubSteps = 8,
        IEnumerable<UsdPhysicsCommand>? commands = null,
        IEnumerable<UsdPhysicsQueryRequest>? queries = null)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deltaSeconds),
                "The step delta must be finite and positive.");
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSubSteps);

        DeltaSeconds = deltaSeconds;
        MaxSubSteps = maxSubSteps;
        _commands = CopyDefensively(commands);
        _queries = CopyDefensively(queries);
    }

    /// <summary>Gets the wall/simulation time to advance, in seconds.</summary>
    public double DeltaSeconds { get; }

    /// <summary>Gets the maximum number of fixed sub-steps this call may advance.</summary>
    public int MaxSubSteps { get; }

    /// <summary>Gets the commands to apply before advancing, in submission order.</summary>
    public IReadOnlyList<UsdPhysicsCommand> Commands => _commands;

    /// <summary>Gets the batched scene queries to resolve during this step, in submission order.</summary>
    public IReadOnlyList<UsdPhysicsQueryRequest> Queries => _queries;

    private static ImmutableArray<T> CopyDefensively<T>(IEnumerable<T>? items)
    {
        if (items is null)
        {
            return ImmutableArray<T>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<T>();
        foreach (T item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            builder.Add(item);
        }
        return builder.ToImmutable();
    }
}

/// <summary>
/// Reports the immutable result of one <see cref="UsdPhysicsSession.Step"/> call.
/// </summary>
public sealed record UsdPhysicsStepResult(
    UsdPhysicsSnapshot Snapshot,
    UsdPhysicsEventBatch Events,
    IReadOnlyList<UsdPhysicsQueryResult> QueryResults,
    UsdPhysicsDiagnostics Diagnostics) : IUsdDetachedResult
{
    /// <summary>Gets the resulting session snapshot after this step.</summary>
    public UsdPhysicsSnapshot Snapshot { get; } = Snapshot ?? throw new ArgumentNullException(nameof(Snapshot));

    /// <summary>Gets the events produced during this step.</summary>
    public UsdPhysicsEventBatch Events { get; } = Events ?? throw new ArgumentNullException(nameof(Events));

    /// <summary>Gets the scene query results, in the same order as the corresponding step request's queries.</summary>
    /// <remarks>
    /// The constructor argument is defensively copied into immutable storage; a mutable list passed
    /// to the constructor may be freely mutated afterward without affecting this result.
    /// </remarks>
    public IReadOnlyList<UsdPhysicsQueryResult> QueryResults { get; } = CopyQueryResultsDefensively(QueryResults);

    /// <summary>Gets the diagnostics produced while executing this step.</summary>
    public UsdPhysicsDiagnostics Diagnostics { get; } =
        Diagnostics ?? throw new ArgumentNullException(nameof(Diagnostics));

    private static ImmutableArray<UsdPhysicsQueryResult> CopyQueryResultsDefensively(
        IReadOnlyList<UsdPhysicsQueryResult> queryResults)
    {
        ArgumentNullException.ThrowIfNull(queryResults);

        var builder = ImmutableArray.CreateBuilder<UsdPhysicsQueryResult>(queryResults.Count);
        foreach (UsdPhysicsQueryResult queryResult in queryResults)
        {
            ArgumentNullException.ThrowIfNull(queryResult);
            builder.Add(queryResult);
        }
        return builder.MoveToImmutable();
    }
}

/// <summary>
/// Grants the exclusive right to call <see cref="UsdPhysicsSession.Step"/> for one session.
/// </summary>
/// <remarks>
/// Only one owner may be acquired per session at a time, modeling the single dedicated physics
/// worker that owns every native object. Acquire it once from whichever component owns the fixed
/// simulation tick (a viewer transport loop, a headless runner, or a test), and dispose it to
/// release stepping back to another owner. The token permanently binds to the managed thread that
/// acquired it: <see cref="UsdPhysicsSession.Step"/> rejects every call that does not arrive on that
/// exact thread, even while the token itself remains otherwise valid. <see cref="Dispose"/>, unlike
/// <see cref="UsdPhysicsSession.Step"/>, may be called from any thread.
/// </remarks>
public sealed class UsdPhysicsStepOwnership : IDisposable
{
    private readonly UsdPhysicsSession _session;
    private int _disposed;

    internal UsdPhysicsStepOwnership(UsdPhysicsSession session)
    {
        _session = session;
        OwnerThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>
    /// Gets the <see cref="Environment.CurrentManagedThreadId"/> observed when this token was
    /// acquired. <see cref="UsdPhysicsSession.Step"/> only accepts calls made from this exact thread.
    /// </summary>
    internal int OwnerThreadId { get; }

    internal bool Owns(UsdPhysicsSession session) =>
        ReferenceEquals(_session, session) && Volatile.Read(ref _disposed) == 0;

    /// <summary>Releases exclusive stepping rights back to the owning session.</summary>
    /// <remarks>May be called from any thread, not only the thread that acquired this token.</remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _session.ReleaseStepOwnership(this);
        }
    }
}
