// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics;

/// <summary>
/// Reports the outcome of building or resetting the world owned by a <see cref="IUsdPhysicsBackend"/>.
/// </summary>
internal readonly record struct UsdPhysicsBuildOutcome(
    UsdPhysicsCapabilities Capabilities,
    UsdPhysicsDiagnostics Diagnostics);

/// <summary>
/// Owns every native simulation object for one <see cref="UsdPhysicsSession"/>.
/// </summary>
/// <remarks>
/// <para>
/// This abstraction hides whether a real retained PhysX world is available. Until the retained
/// world ABI and translator exist, <see cref="UsdPhysicsBackendFactory"/> always resolves the
/// <see cref="UsdPhysicsNotSupportedBackend"/>, which reports <see cref="UsdPhysicsCapabilities.None"/>
/// and clear diagnostics instead of fabricating simulation results.
/// </para>
/// <para>
/// A backend instance is exclusively owned by exactly one <see cref="UsdPhysicsSession"/>, which
/// serializes every call into it through its own exclusive world gate: <c>BuildAsync</c> happens
/// once before the session is visible to callers, and <c>ResetAsync</c>, <c>SeekAsync</c>,
/// <c>Step</c>, <c>BakeAsync</c>, and <c>DisposeAsync</c> never execute concurrently with one
/// another for the same instance. Implementations may rely on this single-writer contract instead
/// of independently synchronizing their own state, but should still document any additional
/// concurrency assumptions they make.
/// </para>
/// </remarks>
internal interface IUsdPhysicsBackend : IAsyncDisposable
{
    Task<UsdPhysicsBuildOutcome> BuildAsync(
        UsdStageScheduler? scheduler,
        UsdPhysicsSessionOptions options,
        CancellationToken cancellationToken);

    Task<UsdPhysicsBuildOutcome> ResetAsync(
        UsdStageScheduler? scheduler,
        UsdPhysicsSessionOptions options,
        CancellationToken cancellationToken);

    Task<UsdPhysicsSnapshot> SeekAsync(double timeCode, CancellationToken cancellationToken);

    UsdPhysicsStepResult Step(UsdPhysicsStepRequest request);

    Task<UsdPhysicsBakeResult> BakeAsync(
        UsdStageScheduler? scheduler,
        UsdPhysicsBakeRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resolves the <see cref="IUsdPhysicsBackend"/> used by <see cref="UsdPhysicsSession"/>.
/// </summary>
internal static class UsdPhysicsBackendFactory
{
    /// <summary>Creates the default backend for a new session.</summary>
    /// <remarks>
    /// Always resolves to <see cref="UsdPhysicsNotSupportedBackend"/> today. This is the single
    /// seam a later retained PhysX backend replaces without changing <see cref="UsdPhysicsSession"/>.
    /// </remarks>
    internal static IUsdPhysicsBackend CreateDefault() => new UsdPhysicsNotSupportedBackend();
}

/// <summary>
/// A diagnostics-only backend used while no retained native physics runtime is implemented.
/// </summary>
/// <remarks>
/// <para>
/// Every operation succeeds structurally so a <see cref="UsdPhysicsSession"/> can be built, seeked,
/// and stepped without a native runtime, but no domain is reported as supported, no transform is
/// simulated, and <see cref="BakeAsync"/> always reports <see cref="UsdPhysicsBakeStatus.NotSupported"/>.
/// This never pretends to produce real simulation results.
/// </para>
/// <para>
/// Relies on the owning <see cref="UsdPhysicsSession"/>'s exclusive world gate to guarantee this
/// backend is never called concurrently with itself (see <see cref="IUsdPhysicsBackend"/>). The
/// mutable fields below are still read and written with <see cref="Volatile"/>/<see cref="Interlocked"/>
/// so that a value written just before the session releases its gate is guaranteed visible to
/// whichever call the session admits next, even across a thread-pool continuation.
/// </para>
/// </remarks>
internal sealed class UsdPhysicsNotSupportedBackend : IUsdPhysicsBackend
{
    private const string DiagnosticCode = "OPENUSD_PHYSICS_BACKEND_UNAVAILABLE";
    private const string DiagnosticMessage =
        "No retained native physics backend is registered; UsdPhysicsSession operates in " +
        "diagnostics-only mode until PhysX simulation is implemented.";

    private long _revision;
    private long _stepIndex;
    private double _timeCode;

    public Task<UsdPhysicsBuildOutcome> BuildAsync(
        UsdStageScheduler? scheduler,
        UsdPhysicsSessionOptions options,
        CancellationToken cancellationToken) =>
        ResetAsync(scheduler, options, cancellationToken);

    public Task<UsdPhysicsBuildOutcome> ResetAsync(
        UsdStageScheduler? scheduler,
        UsdPhysicsSessionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        _ = scheduler;
        Volatile.Write(ref _stepIndex, 0);
        Volatile.Write(ref _timeCode, 0);
        var outcome = new UsdPhysicsBuildOutcome(
            UsdPhysicsCapabilities.None,
            new UsdPhysicsDiagnostics([
                new UsdPhysicsDiagnostic(
                    UsdPhysicsDiagnosticSeverity.Warning,
                    UsdPhysicsDiagnosticCategory.Capability,
                    DiagnosticCode,
                    DiagnosticMessage)
            ]));
        return Task.FromResult(outcome);
    }

    public Task<UsdPhysicsSnapshot> SeekAsync(double timeCode, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(timeCode), "The time code must be finite.");
        }
        cancellationToken.ThrowIfCancellationRequested();

        Volatile.Write(ref _timeCode, timeCode);
        var snapshot = new UsdPhysicsSnapshot(
            (ulong)Interlocked.Increment(ref _revision),
            timeCode,
            (ulong)Volatile.Read(ref _stepIndex),
            UsdPhysicsDiagnostics.Empty);
        return Task.FromResult(snapshot);
    }

    public UsdPhysicsStepResult Step(UsdPhysicsStepRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new List<UsdPhysicsDiagnostic>();
        if (request.Commands.Count > 0 || request.Queries.Count > 0)
        {
            diagnostics.Add(new UsdPhysicsDiagnostic(
                UsdPhysicsDiagnosticSeverity.Warning,
                UsdPhysicsDiagnosticCategory.Capability,
                DiagnosticCode,
                "Commands and scene queries were ignored because no native physics backend is registered."));
        }

        double timeCode = Volatile.Read(ref _timeCode) + request.DeltaSeconds;
        Volatile.Write(ref _timeCode, timeCode);
        ulong stepIndex = (ulong)Interlocked.Increment(ref _stepIndex);
        var snapshot = new UsdPhysicsSnapshot(
            (ulong)Interlocked.Increment(ref _revision),
            timeCode,
            stepIndex,
            UsdPhysicsDiagnostics.Empty);

        var queryResults = new List<UsdPhysicsQueryResult>(request.Queries.Count);
        for (int i = 0; i < request.Queries.Count; i++)
        {
            queryResults.Add(UsdPhysicsQueryResult.Empty);
        }

        return new UsdPhysicsStepResult(
            snapshot,
            UsdPhysicsEventBatch.Empty,
            queryResults,
            new UsdPhysicsDiagnostics(diagnostics));
    }

    public Task<UsdPhysicsBakeResult> BakeAsync(
        UsdStageScheduler? scheduler,
        UsdPhysicsBakeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        _ = scheduler;
        var result = new UsdPhysicsBakeResult(
            UsdPhysicsBakeStatus.NotSupported,
            0,
            new UsdPhysicsDiagnostics([
                new UsdPhysicsDiagnostic(
                    UsdPhysicsDiagnosticSeverity.Warning,
                    UsdPhysicsDiagnosticCategory.Bake,
                    DiagnosticCode,
                    "Baking requires simulated results, and no retained native physics backend is registered.")
            ]));
        return Task.FromResult(result);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
