// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Tests;

/// <summary>
/// A fully deterministic in-memory world used to test transport behavior without a native runtime.
/// </summary>
/// <remarks>
/// Every simulated body encodes the number of completed fixed sub-steps in its position, so a test
/// can assert exactly how far the world advanced, that a seek reproduces a canonical replay, and that
/// a restored checkpoint is indistinguishable from replaying to the same sub-step.
/// </remarks>
internal sealed class FakePhysicsWorld : IUsdPhysicsWorld
{
    private readonly UsdPhysicsBodyPose[] _state;

    internal FakePhysicsWorld(int bodyCount = 4)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bodyCount);
        _state = new UsdPhysicsBodyPose[bodyCount];
        WriteState(0);
    }

    internal bool BuildSucceeds { get; set; } = true;

    /// <summary>Thrown by the next build, which models a build that cannot commit at all.</summary>
    internal Exception? BuildThrows { get; set; }

    internal bool ReplayEquivalentCheckpoints { get; set; }

    internal bool ResultsAreApproximate { get; set; }

    internal bool FailNextStep { get; set; }

    /// <summary>Whether the world refuses every staged command, modelling an unsupported backend.</summary>
    internal bool RefuseCommands { get; set; }

    /// <summary>The commands staged for the next advance, in submission order.</summary>
    internal List<UsdPhysicsCommand> StagedCommands { get; } = [];

    /// <summary>The number of commands consumed by an advance.</summary>
    internal int AppliedCommands { get; private set; }

    /// <summary>The number of times staged commands were explicitly discarded.</summary>
    internal int DiscardCount { get; private set; }

    /// <summary>Invoked at the start of every build so a test can observe what the world holds.</summary>
    internal Action<FakePhysicsWorld>? OnBuild { get; set; }

    /// <summary>Invoked at the start of every step so a test can observe or interrupt a replay.</summary>
    internal Action<FakePhysicsWorld>? OnStep { get; set; }

    internal UsdPhysicsCapabilities Capabilities { get; set; } =
        new(UsdPhysicsCapability.RigidBodies);

    internal int BuildCount { get; private set; }

    internal int ResetCount { get; private set; }

    internal int StepCalls { get; private set; }

    internal long SubStepsAdvanced { get; private set; }

    internal int RestoreCount { get; private set; }

    internal int CaptureCount { get; private set; }

    internal ulong Counter { get; private set; }

    internal bool IsDisposed { get; private set; }

    public UsdPhysicsWorldBuildResult Build(
        UsdPhysicsTimeline timeline,
        UsdPhysicsFixedStep step,
        UsdPhysicsSessionOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BuildCount++;
        OnBuild?.Invoke(this);
        if (BuildThrows is { } failure)
        {
            throw failure;
        }

        // The real world builds transactionally, so a build that does not succeed leaves the
        // state the previous build produced exactly where it was.
        if (!BuildSucceeds)
        {
            return new UsdPhysicsWorldBuildResult(
                false,
                UsdPhysicsCapabilities.None,
                UsdPhysicsDiagnostics.Empty,
                0,
                ReplayEquivalentCheckpoints,
                ResultsAreApproximate);
        }

        WriteState(0);
        return new UsdPhysicsWorldBuildResult(
            true,
            Capabilities,
            UsdPhysicsDiagnostics.Empty,
            _state.Length,
            ReplayEquivalentCheckpoints,
            ResultsAreApproximate);
    }

    public void ResetToStart()
    {
        ResetCount++;
        StagedCommands.Clear();
        WriteState(0);
    }

    /// <summary>The extracted stage the most recent attach carried.</summary>
    internal OpenUsd.Physics.Extraction.UsdPhysicsExtractionPage? Extraction { get; private set; }

    /// <summary>The number of times an extraction was attached.</summary>
    internal int Attachments { get; private set; }

    public void AttachExtraction(OpenUsd.Physics.Extraction.UsdPhysicsExtractionPage? page)
    {
        Attachments++;
        Extraction = page;
    }

    public UsdPhysicsCommandStaging StageCommands(IReadOnlyList<UsdPhysicsCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (RefuseCommands)
        {
            return new UsdPhysicsCommandStaging(0, commands.Count, "The fake world refuses commands.");
        }

        StagedCommands.AddRange(commands);
        return new UsdPhysicsCommandStaging(
            commands.Count,
            0,
            $"Staged {commands.Count} runtime command(s).");
    }

    public void DiscardStagedCommands()
    {
        DiscardCount++;
        StagedCommands.Clear();
    }

    public bool TryStep(double fixedSeconds, int subSteps, UsdPhysicsFrame destination)
    {
        StepCalls++;
        AppliedCommands += StagedCommands.Count;
        StagedCommands.Clear();
        OnStep?.Invoke(this);
        if (FailNextStep)
        {
            FailNextStep = false;
            return false;
        }

        SubStepsAdvanced += subSteps;
        WriteState(Counter + (ulong)subSteps);
        return CopyInto(destination);
    }

    public bool TryFetch(UsdPhysicsFrame destination) => CopyInto(destination);

    public int CaptureState(Span<UsdPhysicsBodyPose> destination)
    {
        if (!ReplayEquivalentCheckpoints)
        {
            return -1;
        }

        CaptureCount++;
        int count = Math.Min(destination.Length, _state.Length);
        _state.AsSpan(0, count).CopyTo(destination);
        return count;
    }

    public bool TryRestoreState(ReadOnlySpan<UsdPhysicsBodyPose> state, double simulationSeconds)
    {
        if (!ReplayEquivalentCheckpoints || state.Length != _state.Length)
        {
            return false;
        }

        RestoreCount++;
        state.CopyTo(_state);
        Counter = (ulong)_state[0].Position.Y;
        return true;
    }

    public UsdPhysicsDiagnostics DrainDiagnostics() => UsdPhysicsDiagnostics.Empty;

    public void Dispose() => IsDisposed = true;

    private bool CopyInto(UsdPhysicsFrame destination)
    {
        Span<UsdPhysicsBodyPose> bodies = destination.BodyBuffer;
        int count = Math.Min(bodies.Length, _state.Length);
        _state.AsSpan(0, count).CopyTo(bodies);
        destination.SetBodyCount(count);
        return true;
    }

    private void WriteState(ulong counter)
    {
        Counter = counter;
        for (int index = 0; index < _state.Length; index++)
        {
            _state[index] = new UsdPhysicsBodyPose(
                new UsdPhysicsObjectId((ulong)index + 1, UsdPhysicsObjectKind.RigidBody),
                new UsdVec3d(index, counter, 0),
                UsdPhysicsOrientation.Identity,
                new UsdVec3d(0, 1, 0),
                default,
                false,
                false);
        }
    }
}

/// <summary>
/// A clock whose elapsed time is supplied explicitly, so transport timing never depends on a timer.
/// </summary>
internal sealed class FakePhysicsClock : IUsdPhysicsClock
{
    private double _pending;

    internal void Advance(double seconds) => _pending += seconds;

    public double NextElapsedSeconds()
    {
        double elapsed = _pending;
        _pending = 0;
        return elapsed;
    }

    public void Restart() => _pending = 0;
}
