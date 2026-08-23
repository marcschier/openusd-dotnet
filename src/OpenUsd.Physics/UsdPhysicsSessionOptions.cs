// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics;

/// <summary>
/// Configures bounded capacities, requested capabilities, and transport limits for one
/// <see cref="UsdPhysicsSession"/>.
/// </summary>
/// <remarks>
/// Every capacity is a hard bound sized once at <see cref="UsdPhysicsSession.BuildAsync"/> or
/// <see cref="UsdPhysicsSession.ResetAsync"/>. A backend never grows a capacity while stepping;
/// exceeding one is a diagnosed bounded overflow that requires a rebuild with larger options.
/// </remarks>
public sealed record UsdPhysicsSessionOptions
{
    /// <summary>Gets the default session options.</summary>
    public static UsdPhysicsSessionOptions Default { get; } = new();

    /// <summary>Initializes validated session options.</summary>
    public UsdPhysicsSessionOptions(
        UsdPhysicsCapability requestedCapabilities = UsdPhysicsCapability.All,
        int maxRigidBodies = 4096,
        int maxColliders = 8192,
        int maxJoints = 4096,
        int maxArticulations = 256,
        int maxControllers = 64,
        int maxVehicles = 64,
        int maxParticles = 0,
        int maxDeformableElements = 0,
        int maxEventsPerStep = 1024,
        int maxQueryHitsPerRequest = 64,
        int maxSubStepsPerTick = 8,
        double? fixedFrequencyOverrideHz = null,
        int checkpointInterval = 0,
        int maxCheckpoints = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxRigidBodies);
        ArgumentOutOfRangeException.ThrowIfNegative(maxColliders);
        ArgumentOutOfRangeException.ThrowIfNegative(maxJoints);
        ArgumentOutOfRangeException.ThrowIfNegative(maxArticulations);
        ArgumentOutOfRangeException.ThrowIfNegative(maxControllers);
        ArgumentOutOfRangeException.ThrowIfNegative(maxVehicles);
        ArgumentOutOfRangeException.ThrowIfNegative(maxParticles);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDeformableElements);
        ArgumentOutOfRangeException.ThrowIfNegative(maxEventsPerStep);
        ArgumentOutOfRangeException.ThrowIfNegative(maxQueryHitsPerRequest);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSubStepsPerTick);
        ArgumentOutOfRangeException.ThrowIfNegative(checkpointInterval);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCheckpoints);
        if (fixedFrequencyOverrideHz is double frequency && (!double.IsFinite(frequency) || frequency <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fixedFrequencyOverrideHz),
                "The fixed frequency override must be finite and positive when provided.");
        }

        RequestedCapabilities = requestedCapabilities;
        MaxRigidBodies = maxRigidBodies;
        MaxColliders = maxColliders;
        MaxJoints = maxJoints;
        MaxArticulations = maxArticulations;
        MaxControllers = maxControllers;
        MaxVehicles = maxVehicles;
        MaxParticles = maxParticles;
        MaxDeformableElements = maxDeformableElements;
        MaxEventsPerStep = maxEventsPerStep;
        MaxQueryHitsPerRequest = maxQueryHitsPerRequest;
        MaxSubStepsPerTick = maxSubStepsPerTick;
        FixedFrequencyOverrideHz = fixedFrequencyOverrideHz;
        CheckpointInterval = checkpointInterval;
        MaxCheckpoints = maxCheckpoints;
    }

    /// <summary>Gets the capabilities requested from the backend; unsupported ones are diagnosed.</summary>
    public UsdPhysicsCapability RequestedCapabilities { get; }

    /// <summary>Gets the maximum number of simultaneously simulated rigid bodies.</summary>
    public int MaxRigidBodies { get; }

    /// <summary>Gets the maximum number of simultaneously simulated colliders.</summary>
    public int MaxColliders { get; }

    /// <summary>Gets the maximum number of simultaneously simulated joints.</summary>
    public int MaxJoints { get; }

    /// <summary>Gets the maximum number of simultaneously simulated articulations.</summary>
    public int MaxArticulations { get; }

    /// <summary>Gets the maximum number of simultaneously simulated character controllers.</summary>
    public int MaxControllers { get; }

    /// <summary>Gets the maximum number of simultaneously simulated vehicles.</summary>
    public int MaxVehicles { get; }

    /// <summary>Gets the maximum number of simultaneously simulated particles. Zero disables particles.</summary>
    public int MaxParticles { get; }

    /// <summary>
    /// Gets the maximum number of simultaneously simulated deformable elements. Zero disables
    /// deformables.
    /// </summary>
    public int MaxDeformableElements { get; }

    /// <summary>Gets the maximum number of events retained per <see cref="UsdPhysicsSession.Step"/> call.</summary>
    public int MaxEventsPerStep { get; }

    /// <summary>Gets the maximum number of hits retained per scene query request.</summary>
    public int MaxQueryHitsPerRequest { get; }

    /// <summary>
    /// Gets the maximum number of fixed sub-steps advanced by one
    /// <see cref="UsdPhysicsSession.Step"/> call.
    /// </summary>
    public int MaxSubStepsPerTick { get; }

    /// <summary>
    /// Gets an explicit fixed simulation frequency in Hz, overriding the frequency derived from the
    /// authored stage's <c>timeCodesPerSecond</c>. When set, it is still clamped to 24-240 Hz.
    /// </summary>
    public double? FixedFrequencyOverrideHz { get; }

    /// <summary>Gets the number of steps between retained checkpoints. Zero disables checkpointing.</summary>
    public int CheckpointInterval { get; }

    /// <summary>Gets the maximum number of retained checkpoints. Zero disables checkpointing.</summary>
    public int MaxCheckpoints { get; }
}
