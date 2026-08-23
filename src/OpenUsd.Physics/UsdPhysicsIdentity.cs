// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics;

/// <summary>
/// Identifies the kind of simulated object addressed by a <see cref="UsdPhysicsObjectId"/>.
/// </summary>
public enum UsdPhysicsObjectKind
{
    /// <summary>The kind is not known or not yet classified.</summary>
    Unknown,

    /// <summary>A <c>UsdPhysicsScene</c>.</summary>
    Scene,

    /// <summary>A dynamic rigid body.</summary>
    RigidBody,

    /// <summary>A static (non-simulated) body.</summary>
    StaticBody,

    /// <summary>A collision shape.</summary>
    Collider,

    /// <summary>A joint between two bodies.</summary>
    Joint,

    /// <summary>A reduced-coordinate articulation root.</summary>
    Articulation,

    /// <summary>A reduced-coordinate articulation link.</summary>
    ArticulationLink,

    /// <summary>A character controller.</summary>
    Controller,

    /// <summary>A vehicle.</summary>
    Vehicle,

    /// <summary>A particle system.</summary>
    ParticleSystem,

    /// <summary>A surface or volume deformable.</summary>
    Deformable
}

/// <summary>
/// Computes the stable identity of a simulated object from its authored address.
/// </summary>
/// <remarks>
/// A viewer, a bake, or any other presentation layer only ever learns prim paths, while every
/// simulation result is addressed by identity. Without a published way to compute the identity of a
/// path, no caller outside this assembly can bind a simulation result back to the prim it drives,
/// which is why the algorithm the built world uses is exposed here rather than reimplemented by
/// each caller against an unpublished hash.
/// </remarks>
public static class UsdPhysicsIdentities
{
    /// <summary>Computes the identity a built world gives one authored prim.</summary>
    /// <param name="primPath">The absolute authored prim path.</param>
    /// <param name="kind">The object kind the identity is tagged with.</param>
    /// <returns>The stable identity of the composed prim.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="primPath"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="primPath"/> is not an absolute, UTF-8 encodable prim path.
    /// </exception>
    /// <remarks>
    /// Only plainly composed prims are addressed. Native instance proxies and
    /// <c>PointInstancer</c> elements carry an instance domain the caller cannot observe from a
    /// path alone, so they are not addressable through this entry point.
    /// </remarks>
    public static UsdPhysicsObjectId FromPrimPath(
        string primPath,
        UsdPhysicsObjectKind kind = UsdPhysicsObjectKind.Unknown)
    {
        ArgumentNullException.ThrowIfNull(primPath);
        ulong value = PhysxIdentity.Compute(primPath, PhysxInstanceDomain.Prim, instanceIndex: 0);
        return new UsdPhysicsObjectId(value, kind);
    }

    /// <summary>Computes the identity a built world gives one composed simulation object.</summary>
    /// <param name="primPath">The absolute authored prim path the object was composed from.</param>
    /// <param name="kind">The kind of simulation object composed at that path.</param>
    /// <returns>
    /// The identity the retained world addresses the object by, or <see cref="UsdPhysicsObjectId.None"/>
    /// when the kind is not addressed by an authored prim path alone.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="primPath"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="primPath"/> is not an absolute, UTF-8 encodable prim path.
    /// </exception>
    /// <remarks>
    /// <para>
    /// One authored prim commonly composes into several simulation objects at once: a chassis prim
    /// is a rigid actor, a collision shape, and a vehicle, and each of those is addressed by its
    /// own identity. <see cref="FromPrimPath"/> only ever produces the plain prim identity, so a
    /// caller that used it to address a vehicle or a character controller would silently send every
    /// command to the actor instead, and a world that contains no actor at that path would refuse
    /// them all. This entry point applies the same address the composer does, so the identity it
    /// returns is the one the world actually holds.
    /// </para>
    /// <para>
    /// The addresses are part of the published contract rather than an implementation detail
    /// precisely because a viewer, a bake, or a test has no other way to name a composed object.
    /// </para>
    /// </remarks>
    public static UsdPhysicsObjectId ForSimulatedObject(
        string primPath,
        UsdPhysicsObjectKind kind)
    {
        ArgumentNullException.ThrowIfNull(primPath);
        string? address = kind switch
        {
            UsdPhysicsObjectKind.Scene => primPath,
            UsdPhysicsObjectKind.RigidBody => primPath,
            UsdPhysicsObjectKind.StaticBody => primPath,
            UsdPhysicsObjectKind.Joint => primPath,
            UsdPhysicsObjectKind.Articulation => primPath,
            UsdPhysicsObjectKind.ArticulationLink => primPath,
            UsdPhysicsObjectKind.ParticleSystem => primPath,
            UsdPhysicsObjectKind.Deformable => primPath,
            UsdPhysicsObjectKind.Collider => primPath + ".shape",
            UsdPhysicsObjectKind.Controller => primPath + ".controller",
            UsdPhysicsObjectKind.Vehicle => primPath + ".vehicle",
            _ => null,
        };

        if (address is null)
        {
            return UsdPhysicsObjectId.None;
        }

        ulong value = PhysxIdentity.Compute(address, PhysxInstanceDomain.Prim, instanceIndex: 0);
        return new UsdPhysicsObjectId(value, kind);
    }
}

/// <summary>
/// Identifies one simulated object without exposing a native handle, prim, or stage reference.
/// </summary>
/// <remarks>
/// The identity is stable across <see cref="UsdPhysicsSession.ResetAsync"/> for the same authored
/// object and is derived from the canonical prim path plus instance domain/index. It never encodes
/// unstable traversal order or a raw native pointer.
/// </remarks>
public readonly record struct UsdPhysicsObjectId : IUsdDetachedResult
{
    /// <summary>Gets the sentinel identity that addresses no object.</summary>
    public static UsdPhysicsObjectId None { get; }

    /// <summary>Initializes an object identity from its opaque stable value.</summary>
    public UsdPhysicsObjectId(ulong value, UsdPhysicsObjectKind kind = UsdPhysicsObjectKind.Unknown)
    {
        Value = value;
        Kind = kind;
    }

    /// <summary>Gets the opaque stable 64-bit identity value.</summary>
    public ulong Value { get; }

    /// <summary>Gets the kind of object addressed by this identity.</summary>
    public UsdPhysicsObjectKind Kind { get; }

    /// <summary>Gets a value indicating whether this identity addresses no object.</summary>
    public bool IsNone => Value == 0;

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Kind}:0x{Value:x16}");
}
