// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Baking;

/// <summary>
/// Selects the transform space simulated body poses are authored in.
/// </summary>
public enum UsdPhysicsBakeTransformSpace
{
    /// <summary>
    /// Authors the simulated world transform together with <c>!resetXformStack!</c>, which is exact
    /// and independent of any transform authored on an ancestor.
    /// </summary>
    World,

    /// <summary>
    /// Authors the transform relative to the composed parent transform, preserving ancestor
    /// transforms. Parent poses are resolved against the transforms the batch itself is going to
    /// produce, so a parent and its child compose to the same world transform no matter which
    /// order they appear in or how the batch was split into chunks.
    /// </summary>
    LocalToParent
}

/// <summary>
/// Selects what happens when the destination layer already holds a time sample being authored.
/// </summary>
public enum UsdPhysicsBakeExistingSamplePolicy
{
    /// <summary>Replaces the existing time sample.</summary>
    Overwrite,

    /// <summary>Leaves the existing time sample and reports the record as skipped.</summary>
    Skip,

    /// <summary>Rejects the whole operation without authoring anything.</summary>
    Reject
}

/// <summary>
/// Immutable options shared by the physics preview and the transactional bake.
/// </summary>
public sealed record UsdPhysicsBakeOptions
{
    private readonly int _chunkSize = 4096;

    /// <summary>Gets the default options.</summary>
    public static UsdPhysicsBakeOptions Default { get; } = new();

    /// <summary>Gets the transform space simulated poses are authored in.</summary>
    public UsdPhysicsBakeTransformSpace TransformSpace { get; init; } =
        UsdPhysicsBakeTransformSpace.World;

    /// <summary>Gets the policy applied when a destination time sample already exists.</summary>
    public UsdPhysicsBakeExistingSamplePolicy ExistingSamplePolicy { get; init; } =
        UsdPhysicsBakeExistingSamplePolicy.Overwrite;

    /// <summary>Gets a value indicating whether simulated velocities are authored.</summary>
    public bool WriteVelocities { get; init; } = true;

    /// <summary>Gets a value indicating whether point-based extents are recomputed and authored.</summary>
    public bool WriteExtents { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether project-owned <c>openUsdPhysics:simulation</c> attributes are
    /// authored for physics state that standard USD cannot represent.
    /// </summary>
    public bool WriteSimulationMetadata { get; init; } = true;

    /// <summary>
    /// Gets the maximum number of records authored by one batched native call. Every chunk is
    /// authored all-or-nothing, and progress and cancellation are only observed between chunks.
    /// </summary>
    public int ChunkSize
    {
        get => _chunkSize;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _chunkSize = value;
        }
    }
}
