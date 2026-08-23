// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics;

/// <summary>
/// Identifies optional <see cref="UsdPhysicsSession"/> simulation domains and features.
/// </summary>
/// <remarks>
/// A caller requests capabilities through <see cref="UsdPhysicsSessionOptions.RequestedCapabilities"/>;
/// the active backend negotiates them down to what it can actually provide and reports the result
/// through <see cref="UsdPhysicsSession.Capabilities"/>. Requesting a capability never guarantees it
/// is honored, and an unsupported request never fails the build; it is reported as a diagnostic.
/// </remarks>
[Flags]
public enum UsdPhysicsCapability
{
    /// <summary>No optional simulation domain.</summary>
    None = 0,

    /// <summary>Rigid and static bodies, colliders, materials, and filtering.</summary>
    RigidBodies = 1,

    /// <summary>Reduced-coordinate articulations, links, and tendons.</summary>
    Articulations = 2,

    /// <summary>Character controllers.</summary>
    Controllers = 4,

    /// <summary>Vehicles, wheels, suspension, and drivetrain.</summary>
    Vehicles = 8,

    /// <summary>Batched raycast, sweep, and overlap scene queries.</summary>
    SceneQueries = 16,

    /// <summary>Forces, impulses, kinematic targets, teleports, and control inputs.</summary>
    Commands = 32,

    /// <summary>Particle systems, PBD particles, and fluids.</summary>
    Particles = 64,

    /// <summary>Particle cloth and surface deformables.</summary>
    Cloth = 128,

    /// <summary>FEM volume deformables.</summary>
    Deformables = 256,

    /// <summary>Optional CUDA-backed simulation for GPU-only domains.</summary>
    Cuda = 512,

    /// <summary>Checkpoint capture and restoration for seeking.</summary>
    Checkpoints = 1024,

    /// <summary>Baking simulation results into a file-backed animation layer.</summary>
    Bake = 2048,

    /// <summary>Every currently defined capability.</summary>
    All = RigidBodies | Articulations | Controllers | Vehicles | SceneQueries | Commands |
        Particles | Cloth | Deformables | Cuda | Checkpoints | Bake
}

/// <summary>
/// Describes the capabilities a <see cref="UsdPhysicsSession"/> backend actually supports.
/// </summary>
public readonly record struct UsdPhysicsCapabilities
{
    /// <summary>Gets a capability set with no supported domain.</summary>
    public static UsdPhysicsCapabilities None { get; } = new(UsdPhysicsCapability.None);

    /// <summary>Initializes backend capabilities.</summary>
    public UsdPhysicsCapabilities(UsdPhysicsCapability features)
    {
        Features = features;
    }

    /// <summary>Gets the supported capability flags.</summary>
    public UsdPhysicsCapability Features { get; }

    /// <summary>Determines whether every requested capability is supported.</summary>
    /// <remarks>
    /// <see cref="Supports"/> checks that every bit set in <paramref name="capabilities"/> is also
    /// set in <see cref="Features"/>. Requesting <see cref="UsdPhysicsCapability.None"/> is a
    /// degenerate "is a subset of nothing" query and, consistent with .NET's own zero-valued flag
    /// semantics (compare <c>Enum.HasFlag(0)</c>, which is always <see langword="true"/>), this
    /// intentionally always returns <see langword="true"/> regardless of <see cref="Features"/>;
    /// even <see cref="None"/>.Supports(<see cref="UsdPhysicsCapability.None"/>) is
    /// <see langword="true"/>. Callers that need to distinguish "no capability requested" from "a
    /// specific capability is supported" should check <paramref name="capabilities"/> for
    /// <see cref="UsdPhysicsCapability.None"/> before calling this method.
    /// </remarks>
    public bool Supports(UsdPhysicsCapability capabilities) =>
        (Features & capabilities) == capabilities;
}
