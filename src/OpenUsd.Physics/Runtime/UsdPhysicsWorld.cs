// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics;
using OpenUsd.Physics.Extraction;

namespace OpenUsd.Physics;

/// <summary>
/// Reports the outcome of building or rebuilding the world a transport owns.
/// </summary>
/// <param name="Succeeded">Whether the world is usable; a failed build leaves the transport faulted.</param>
/// <param name="Capabilities">The capabilities the world can actually provide.</param>
/// <param name="Diagnostics">Every diagnostic produced while building.</param>
/// <param name="BodyCapacity">The maximum number of body poses one published frame can carry.</param>
/// <param name="SupportsReplayEquivalentCheckpoints">
/// Whether restoring a captured checkpoint provably produces the same subsequent trajectory as
/// replaying from the authored start. Only a world that proves this may be seeked through
/// checkpoints; every other world replays canonically from the authored start time code.
/// </param>
/// <param name="ResultsAreApproximate">
/// Whether the world's results are approximate rather than reproducible, which is true for
/// CUDA-backed domains. Approximation is always diagnosed rather than silently promised away.
/// </param>
/// <param name="DeformationCapacity">
/// The maximum number of deformable bodies one published frame can carry. Zero for a world that
/// simulates no particle, cloth, or volume deformable, which is every CPU only build.
/// </param>
/// <param name="DeformationVertexCapacity">
/// The maximum number of simulated vertices one published frame can carry.
/// </param>
internal readonly record struct UsdPhysicsWorldBuildResult(
    bool Succeeded,
    UsdPhysicsCapabilities Capabilities,
    UsdPhysicsDiagnostics Diagnostics,
    int BodyCapacity,
    bool SupportsReplayEquivalentCheckpoints,
    bool ResultsAreApproximate,
    int DeformationCapacity = 0,
    int DeformationVertexCapacity = 0);

/// <summary>
/// Reports what staging one runtime command batch accepted and what it refused.
/// </summary>
/// <param name="Accepted">The number of commands staged for the next advance.</param>
/// <param name="Rejected">The number of commands the world refused.</param>
/// <param name="Message">A sentence describing the outcome, always non-empty.</param>
internal readonly record struct UsdPhysicsCommandStaging(
    int Accepted,
    int Rejected,
    string Message);

/// <summary>
/// Owns every retained simulation object behind one <see cref="UsdPhysicsTransport"/>.
/// </summary>
/// <remarks>
/// <para>
/// A world instance is exclusively owned by the transport's dedicated physics worker thread. Every
/// member is called from that one thread and never concurrently, so implementations never need their
/// own synchronization; the transport's bounded request queue is what serializes lifecycle work
/// against stepping.
/// </para>
/// <para>
/// <see cref="TryStep"/> is the warm path and must not allocate managed memory after the first call:
/// it writes into the caller-owned frame the publication ring already allocated.
/// </para>
/// </remarks>
internal interface IUsdPhysicsWorld : IDisposable
{
    /// <summary>Creates or recreates every retained simulation object.</summary>
    UsdPhysicsWorldBuildResult Build(
        UsdPhysicsTimeline timeline,
        UsdPhysicsFixedStep step,
        UsdPhysicsSessionOptions options,
        CancellationToken cancellationToken);

    /// <summary>Restores the canonical authored start state, discarding all simulated progress.</summary>
    void ResetToStart();

    /// <summary>
    /// Attaches the extracted stage the next build composes its simulation content from.
    /// </summary>
    /// <remarks>
    /// A world with no extraction attached builds authored timeline metadata only and simulates no
    /// authored body, which is reported as a diagnostic rather than hidden. Attaching is separate
    /// from building because the extraction is produced on the stage owner's thread while the build
    /// runs on the physics worker; the page crosses between them as a detached, pointer-free
    /// buffer.
    /// </remarks>
    /// <param name="page">The extracted stage, or <see langword="null"/> to detach.</param>
    void AttachExtraction(UsdPhysicsExtractionPage? page);

    /// <summary>
    /// Stages runtime commands the next advance applies before its first fixed sub-step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Commands are staged rather than applied immediately because the retained runtime applies a
    /// whole batch as part of one step: applying them outside a step would either need a second
    /// interop entry point per command - which is exactly the per-element interop the scene path
    /// forbids - or would silently reorder them relative to the sub-steps they belong to.
    /// </para>
    /// <para>
    /// Staging appends. Two submissions that reach the world before the next advance are applied in
    /// submission order, which is also replace order, so a caller that submits a clear after an
    /// accumulating command gets the clear.
    /// </para>
    /// </remarks>
    /// <param name="commands">The commands to stage, in submission order.</param>
    /// <returns>What the world accepted and what it refused.</returns>
    UsdPhysicsCommandStaging StageCommands(IReadOnlyList<UsdPhysicsCommand> commands);

    /// <summary>Discards every staged command that no advance has consumed yet.</summary>
    /// <remarks>
    /// A build, a reset, or an invalidation replaces the state the commands were authored against,
    /// so applying them afterwards would push a force onto whatever object happens to inherit the
    /// identity next.
    /// </remarks>
    void DiscardStagedCommands();

    /// <summary>Advances the world by <paramref name="subSteps"/> fixed steps and fills a frame.</summary>
    /// <returns><see langword="false"/> when the world rejected the step and is now faulted.</returns>
    bool TryStep(double fixedSeconds, int subSteps, UsdPhysicsFrame destination);

    /// <summary>Fills a frame from the current state without advancing the simulation.</summary>
    bool TryFetch(UsdPhysicsFrame destination);

    /// <summary>Copies the full restorable state into a caller-owned buffer.</summary>
    /// <returns>The number of written entries, or a negative value when the world cannot be captured.</returns>
    int CaptureState(Span<UsdPhysicsBodyPose> destination);

    /// <summary>Restores a previously captured state.</summary>
    /// <returns><see langword="false"/> when the world cannot restore state.</returns>
    bool TryRestoreState(ReadOnlySpan<UsdPhysicsBodyPose> state, double simulationSeconds);

    /// <summary>Takes every diagnostic accumulated since the last drain.</summary>
    /// <remarks>Called only from cold paths so the warm step path never builds diagnostic objects.</remarks>
    UsdPhysicsDiagnostics DrainDiagnostics();
}

/// <summary>
/// Reports how much wall-clock time elapsed since the previous tick.
/// </summary>
/// <remarks>
/// The clock is an explicit seam so transport behavior - catch-up, slowdown, loop wrapping, and
/// bounded publication - can be tested deterministically without sleeping or racing a real timer.
/// </remarks>
internal interface IUsdPhysicsClock
{
    /// <summary>Returns the seconds elapsed since the previous call, and restarts the interval.</summary>
    double NextElapsedSeconds();

    /// <summary>Discards any elapsed time so the next call measures from now.</summary>
    void Restart();
}

/// <summary>
/// The monotonic wall clock the dedicated physics worker uses in production.
/// </summary>
internal sealed class UsdPhysicsStopwatchClock : IUsdPhysicsClock
{
    private long _timestamp = Stopwatch.GetTimestamp();

    /// <inheritdoc/>
    public double NextElapsedSeconds()
    {
        long now = Stopwatch.GetTimestamp();
        long previous = _timestamp;
        _timestamp = now;
        return (double)(now - previous) / Stopwatch.Frequency;
    }

    /// <inheritdoc/>
    public void Restart() => _timestamp = Stopwatch.GetTimestamp();
}
