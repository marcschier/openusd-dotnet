// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics;

/// <summary>
/// Identifies the lifecycle state of a <see cref="UsdPhysicsSession"/>.
/// </summary>
public enum UsdPhysicsSessionState
{
    /// <summary>The session has not completed its initial build.</summary>
    Unbuilt,

    /// <summary>A build or reset is currently extracting or constructing the simulated world.</summary>
    Building,

    /// <summary>The session is built and ready to step, seek, or bake.</summary>
    Ready,

    /// <summary>
    /// A physics-relevant edit or a failed operation invalidated the world; <see cref="UsdPhysicsSession.ResetAsync"/>
    /// is required before stepping again.
    /// </summary>
    Invalidated,

    /// <summary>The session has been disposed and every resource has been released.</summary>
    Disposed
}

/// <summary>
/// Reports one immutable, versioned, renderer-neutral simulation result.
/// </summary>
/// <remarks>
/// A snapshot never retains a stage, layer, prim, or native handle, and is safe to read from any
/// thread. It is the minimal placeholder for the bounded result page transforms, velocities, and
/// per-domain buffers are added to once the retained world ABI and translator are implemented.
/// </remarks>
public sealed record UsdPhysicsSnapshot(
    ulong Revision,
    double TimeCode,
    ulong StepIndex,
    UsdPhysicsDiagnostics Diagnostics) : IUsdDetachedResult
{
    /// <summary>Gets the empty snapshot published before the first successful step or seek.</summary>
    public static UsdPhysicsSnapshot Empty { get; } = new(0, 0, 0, UsdPhysicsDiagnostics.Empty);

    /// <summary>Gets the simulation time code, in stage time units, this snapshot reflects.</summary>
    public double TimeCode { get; } = double.IsFinite(TimeCode)
        ? TimeCode
        : throw new ArgumentOutOfRangeException(nameof(TimeCode), TimeCode, "The time code must be finite.");

    /// <summary>Gets the diagnostics produced while computing this snapshot.</summary>
    public UsdPhysicsDiagnostics Diagnostics { get; } =
        Diagnostics ?? throw new ArgumentNullException(nameof(Diagnostics));
}
