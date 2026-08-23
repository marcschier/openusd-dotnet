// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics;

/// <summary>
/// Retains a bounded ring of in-memory simulation checkpoints used to accelerate seeking.
/// </summary>
/// <remarks>
/// <para>
/// Checkpointing is opt-in twice over. The caller must configure both
/// <see cref="UsdPhysicsSessionOptions.CheckpointInterval"/> and
/// <see cref="UsdPhysicsSessionOptions.MaxCheckpoints"/>, and the world must additionally prove that
/// restoring a captured state produces the same subsequent trajectory as replaying from the authored
/// start. A world that cannot prove that - which includes every world whose solver keeps internal
/// state a body pose does not describe - never gets checkpoint acceleration, and its seeks always
/// replay canonically from the authored start time code. Silent divergence is strictly worse than a
/// slower seek.
/// </para>
/// <para>
/// The ring is fully preallocated. Capturing a checkpoint copies poses into buffers that already
/// exist, so a checkpointed playback never allocates while stepping, and the oldest checkpoint is
/// overwritten once the bound is reached rather than growing memory without limit.
/// </para>
/// </remarks>
internal sealed class UsdPhysicsCheckpointCache
{
    private readonly UsdPhysicsBodyPose[][] _states;
    private readonly ulong[] _stepIndices;
    private readonly int[] _counts;
    private readonly int _interval;
    private int _count;
    private int _next;

    /// <summary>Allocates the whole bounded ring up front.</summary>
    /// <param name="interval">Fixed sub-steps between checkpoints; zero disables checkpointing.</param>
    /// <param name="maxCheckpoints">The retained checkpoint bound; zero disables checkpointing.</param>
    /// <param name="stateCapacity">The number of body poses one checkpoint can hold.</param>
    /// <param name="replayEquivalent">Whether the world proved restoring a checkpoint is replay-equivalent.</param>
    internal UsdPhysicsCheckpointCache(
        int interval,
        int maxCheckpoints,
        int stateCapacity,
        bool replayEquivalent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(interval);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCheckpoints);
        ArgumentOutOfRangeException.ThrowIfNegative(stateCapacity);

        IsEnabled = replayEquivalent && interval > 0 && maxCheckpoints > 0 && stateCapacity > 0;
        _interval = interval;
        if (!IsEnabled)
        {
            _states = [];
            _stepIndices = [];
            _counts = [];
            return;
        }

        _states = new UsdPhysicsBodyPose[maxCheckpoints][];
        _stepIndices = new ulong[maxCheckpoints];
        _counts = new int[maxCheckpoints];
        for (int index = 0; index < maxCheckpoints; index++)
        {
            _states[index] = new UsdPhysicsBodyPose[stateCapacity];
        }
    }

    /// <summary>Gets a value indicating whether checkpoint acceleration is available at all.</summary>
    internal bool IsEnabled { get; }

    /// <summary>Gets the number of retained checkpoints.</summary>
    internal int Count => _count;

    /// <summary>Gets the retained checkpoint bound.</summary>
    internal int Capacity => _states.Length;

    /// <summary>Determines whether a checkpoint should be captured after reaching a sub-step count.</summary>
    /// <remarks>
    /// The interval is measured against the newest retained checkpoint rather than against exact
    /// multiples, because catch-up advances several sub-steps at once and would otherwise step over
    /// every multiple and never capture anything.
    /// </remarks>
    internal bool ShouldCapture(ulong stepIndex) =>
        IsEnabled && stepIndex > 0 && stepIndex >= LatestStepIndex() + (ulong)_interval;

    /// <summary>Captures the world's restorable state into the oldest ring slot.</summary>
    /// <returns><see langword="false"/> when the world declined to be captured.</returns>
    internal bool TryCapture(IUsdPhysicsWorld world, ulong stepIndex)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!IsEnabled)
        {
            return false;
        }

        int slot = _next;
        int written = world.CaptureState(_states[slot]);
        if (written < 0)
        {
            return false;
        }

        _counts[slot] = written;
        _stepIndices[slot] = stepIndex;
        _next = (_next + 1) % _states.Length;
        if (_count < _states.Length)
        {
            _count++;
        }
        return true;
    }

    /// <summary>
    /// Restores the newest retained checkpoint that is at or before <paramref name="targetStepIndex"/>.
    /// </summary>
    /// <param name="world">The world to restore into.</param>
    /// <param name="targetStepIndex">The sub-step count the caller is seeking to.</param>
    /// <param name="fixedStepSeconds">The fixed step used to derive the restored simulation time.</param>
    /// <param name="restoredStepIndex">The sub-step count actually restored.</param>
    /// <returns><see langword="false"/> when nothing usable is retained, so the caller replays canonically.</returns>
    internal bool TryRestore(
        IUsdPhysicsWorld world,
        ulong targetStepIndex,
        double fixedStepSeconds,
        out ulong restoredStepIndex)
    {
        ArgumentNullException.ThrowIfNull(world);
        restoredStepIndex = 0;
        if (!IsEnabled || _count == 0)
        {
            return false;
        }

        int best = -1;
        for (int index = 0; index < _count; index++)
        {
            ulong candidate = _stepIndices[index];
            if (candidate > targetStepIndex)
            {
                continue;
            }
            if (best < 0 || candidate > _stepIndices[best])
            {
                best = index;
            }
        }

        if (best < 0)
        {
            return false;
        }

        ulong step = _stepIndices[best];
        if (!world.TryRestoreState(_states[best].AsSpan(0, _counts[best]), step * fixedStepSeconds))
        {
            return false;
        }

        restoredStepIndex = step;
        return true;
    }

    /// <summary>Discards every retained checkpoint.</summary>
    /// <remarks>
    /// A rebuild, a reset, and an invalidation all discard checkpoints, because a checkpoint captured
    /// against one built world can never be restored into a differently built one.
    /// </remarks>
    internal void Clear()
    {
        _count = 0;
        _next = 0;
    }

    private ulong LatestStepIndex()
    {
        ulong latest = 0;
        for (int index = 0; index < _count; index++)
        {
            if (_stepIndices[index] > latest)
            {
                latest = _stepIndices[index];
            }
        }
        return latest;
    }
}
