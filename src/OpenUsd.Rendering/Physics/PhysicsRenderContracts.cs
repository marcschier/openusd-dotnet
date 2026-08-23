// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;

namespace OpenUsd.Rendering;

/// <summary>
/// Identifies the kind of simulated object a renderer-neutral physics identity addresses.
/// </summary>
/// <remarks>
/// The values mirror the physics package's object kinds so a producer can forward the stable
/// identity value it already owns without the renderer taking a dependency on the physics
/// package, on a USD prim handle, or on a solver handle.
/// </remarks>
public enum PhysicsRenderObjectKind
{
    /// <summary>The kind is not known or not yet classified.</summary>
    Unknown = 0,

    /// <summary>A physics scene.</summary>
    Scene = 1,

    /// <summary>A dynamic rigid body.</summary>
    RigidBody = 2,

    /// <summary>A static (non-simulated) body.</summary>
    StaticBody = 3,

    /// <summary>A collision shape.</summary>
    Collider = 4,

    /// <summary>A joint between two bodies.</summary>
    Joint = 5,

    /// <summary>A reduced-coordinate articulation root.</summary>
    Articulation = 6,

    /// <summary>A reduced-coordinate articulation link.</summary>
    ArticulationLink = 7,

    /// <summary>A character controller.</summary>
    Controller = 8,

    /// <summary>A vehicle.</summary>
    Vehicle = 9,

    /// <summary>A particle system.</summary>
    ParticleSystem = 10,

    /// <summary>A surface or volume deformable.</summary>
    Deformable = 11
}

/// <summary>
/// Identifies one simulated object for rendering without exposing a USD or solver handle.
/// </summary>
/// <remarks>
/// The value is the opaque stable simulation identity produced by the physics package; the
/// renderer only ever compares it, never dereferences it. The instance ordinal separates the
/// instances of one point-instanced prototype, which share one authored prim path.
/// </remarks>
public readonly record struct PhysicsRenderObjectId
{
    /// <summary>Initializes a renderer-neutral simulation identity.</summary>
    /// <param name="value">The opaque stable 64-bit simulation identity value.</param>
    /// <param name="kind">The kind of object the identity addresses.</param>
    /// <param name="instanceIndex">The zero-based instance ordinal.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="instanceIndex"/> is negative.
    /// </exception>
    public PhysicsRenderObjectId(
        ulong value,
        PhysicsRenderObjectKind kind = PhysicsRenderObjectKind.Unknown,
        int instanceIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(instanceIndex);
        Value = value;
        Kind = kind;
        InstanceIndex = instanceIndex;
    }

    /// <summary>Gets the identity that addresses no object.</summary>
    public static PhysicsRenderObjectId None => default;

    /// <summary>Gets the opaque stable 64-bit simulation identity value.</summary>
    public ulong Value { get; }

    /// <summary>Gets the kind of object this identity addresses.</summary>
    public PhysicsRenderObjectKind Kind { get; }

    /// <summary>Gets the zero-based instance ordinal.</summary>
    public int InstanceIndex { get; }

    /// <summary>Gets a value indicating whether this identity addresses no object.</summary>
    public bool IsNone => Value == 0;

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Kind}:0x{Value:x16}[{InstanceIndex}]");
}

/// <summary>
/// Identifies one simulation domain a renderer can consume independently.
/// </summary>
/// <remarks>
/// Domains are reported and degraded individually: a domain the runtime cannot produce, or that
/// the active backend cannot draw, never prevents a supported domain from rendering.
/// </remarks>
public enum PhysicsRenderDomain
{
    /// <summary>Rigid body transforms.</summary>
    RigidBody = 0,

    /// <summary>Reduced-coordinate articulation link transforms.</summary>
    Articulation = 1,

    /// <summary>Character controller transforms.</summary>
    Controller = 2,

    /// <summary>Vehicle and wheel transforms.</summary>
    Vehicle = 3,

    /// <summary>Particle and fluid point geometry.</summary>
    Particles = 4,

    /// <summary>Cloth and surface deformable geometry.</summary>
    Cloth = 5,

    /// <summary>Volume deformable geometry.</summary>
    Deformable = 6
}

/// <summary>
/// Reports how one simulation domain is being rendered.
/// </summary>
public enum PhysicsRenderDomainStatus
{
    /// <summary>The runtime published no data for the domain.</summary>
    Unavailable = 0,

    /// <summary>The active render backend cannot draw the domain.</summary>
    Unsupported = 1,

    /// <summary>The domain is published and renderable.</summary>
    Supported = 2,

    /// <summary>The domain is renderable but a bounded buffer dropped entries.</summary>
    Truncated = 3
}

/// <summary>
/// Reports one simulation domain's renderable state, bounded capacity, and drop count.
/// </summary>
/// <param name="Domain">The reported domain.</param>
/// <param name="Status">The renderable state of the domain.</param>
/// <param name="Count">The number of entries the snapshot carries for the domain.</param>
/// <param name="Capacity">The number of entries the bounded buffer can carry.</param>
/// <param name="DroppedCount">The number of entries dropped because the buffer was full.</param>
public readonly record struct PhysicsRenderDomainReport(
    PhysicsRenderDomain Domain,
    PhysicsRenderDomainStatus Status,
    int Count,
    int Capacity,
    int DroppedCount)
{
    /// <summary>Gets a value indicating whether the domain contributes to rendering.</summary>
    public bool IsRenderable =>
        Status is PhysicsRenderDomainStatus.Supported or PhysicsRenderDomainStatus.Truncated;

    /// <summary>Creates a report for a domain the active backend cannot draw.</summary>
    /// <param name="domain">The unsupported domain.</param>
    /// <returns>An unsupported, non-renderable report.</returns>
    public static PhysicsRenderDomainReport Unsupported(PhysicsRenderDomain domain) =>
        new(domain, PhysicsRenderDomainStatus.Unsupported, 0, 0, 0);

    /// <summary>Creates a renderer-neutral diagnostic for a degraded domain.</summary>
    /// <returns>
    /// The diagnostic describing the degradation, or <see langword="null"/> when the domain is
    /// fully renderable.
    /// </returns>
    public RenderDiagnostic? ToDiagnostic() => Status switch
    {
        PhysicsRenderDomainStatus.Unavailable => new RenderDiagnostic(
            RenderDiagnosticSeverity.Information,
            PhysicsRenderDiagnosticCodes.DomainUnavailable,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The {Domain} physics domain published no renderable data.")),
        PhysicsRenderDomainStatus.Unsupported => new RenderDiagnostic(
            RenderDiagnosticSeverity.Warning,
            PhysicsRenderDiagnosticCodes.DomainUnsupported,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The active render backend cannot draw the {Domain} physics domain.")),
        PhysicsRenderDomainStatus.Truncated => new RenderDiagnostic(
            RenderDiagnosticSeverity.Warning,
            PhysicsRenderDiagnosticCodes.DomainTruncated,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The {Domain} physics domain dropped {DroppedCount} entries because its bounded " +
                    $"capacity is {Capacity}.")),
        _ => null
    };
}

/// <summary>
/// Declares the renderer-neutral diagnostic codes the physics render path emits.
/// </summary>
public static class PhysicsRenderDiagnosticCodes
{
    /// <summary>A domain published no renderable data.</summary>
    public const string DomainUnavailable = "physics.render.domain.unavailable";

    /// <summary>The active backend cannot draw a domain.</summary>
    public const string DomainUnsupported = "physics.render.domain.unsupported";

    /// <summary>A bounded domain buffer dropped entries.</summary>
    public const string DomainTruncated = "physics.render.domain.truncated";

    /// <summary>A published snapshot could not be copied without truncation.</summary>
    public const string SnapshotTruncated = "physics.render.snapshot.truncated";

    /// <summary>A simulated frame could not be published because every buffer was in use.</summary>
    public const string PublicationDropped = "physics.render.publication.dropped";

    /// <summary>An entity was snapped because interpolation inputs were discontinuous.</summary>
    public const string DiscontinuitySnapped = "physics.render.discontinuity.snapped";

    /// <summary>An override could not be bound to a renderable entity.</summary>
    public const string OverrideUnresolved = "physics.render.override.unresolved";

    /// <summary>An override was dropped because bounded override storage was full.</summary>
    public const string OverrideCapacityExceeded = "physics.render.override.capacity";
}

/// <summary>
/// An immutable unit quaternion describing one simulated body's orientation.
/// </summary>
/// <param name="X">The imaginary X component.</param>
/// <param name="Y">The imaginary Y component.</param>
/// <param name="Z">The imaginary Z component.</param>
/// <param name="W">The real component.</param>
public readonly record struct PhysicsRenderOrientation(double X, double Y, double Z, double W)
{
    private const double SlerpLinearThreshold = 0.9995;

    /// <summary>Gets the identity orientation.</summary>
    public static PhysicsRenderOrientation Identity => new(0, 0, 0, 1);

    /// <summary>Gets a value indicating whether every component is finite.</summary>
    public bool IsFinite =>
        double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z) && double.IsFinite(W);

    /// <summary>Returns the four-dimensional dot product with another orientation.</summary>
    /// <param name="other">The other orientation.</param>
    /// <returns>The dot product.</returns>
    public double Dot(PhysicsRenderOrientation other) =>
        (X * other.X) + (Y * other.Y) + (Z * other.Z) + (W * other.W);

    /// <summary>Returns the unit-length orientation.</summary>
    /// <returns>
    /// The normalized orientation, or <see cref="Identity"/> when this orientation is degenerate.
    /// </returns>
    public PhysicsRenderOrientation Normalized()
    {
        if (!IsFinite)
        {
            return Identity;
        }

        double lengthSquared = Dot(this);
        if (lengthSquared <= 0 || !double.IsFinite(lengthSquared))
        {
            return Identity;
        }

        double inverse = 1.0 / Math.Sqrt(lengthSquared);
        return new PhysicsRenderOrientation(X * inverse, Y * inverse, Z * inverse, W * inverse);
    }

    /// <summary>Returns the canonical representation whose real component is not negative.</summary>
    /// <remarks>
    /// A quaternion and its negation describe the same orientation. Canonicalizing keeps published
    /// values comparable and keeps interpolation between two snapshots on the shortest arc.
    /// </remarks>
    /// <returns>The canonical orientation.</returns>
    public PhysicsRenderOrientation Canonical() =>
        W < 0 ? new PhysicsRenderOrientation(-X, -Y, -Z, -W) : this;

    /// <summary>Interpolates two orientations along the shortest arc.</summary>
    /// <param name="from">The orientation at the earlier snapshot.</param>
    /// <param name="to">The orientation at the later snapshot.</param>
    /// <param name="alpha">The interpolation factor, clamped to the unit interval.</param>
    /// <returns>The canonical unit interpolated orientation.</returns>
    public static PhysicsRenderOrientation Slerp(
        PhysicsRenderOrientation from,
        PhysicsRenderOrientation to,
        double alpha)
    {
        PhysicsRenderOrientation start = from.Normalized();
        PhysicsRenderOrientation end = to.Normalized();
        double factor = double.IsFinite(alpha) ? Math.Clamp(alpha, 0, 1) : 1;

        double cosine = start.Dot(end);
        if (cosine < 0)
        {
            // Negating one endpoint selects the shorter of the two arcs that describe the same
            // rotation; without it a body can visibly spin the long way round between two frames.
            end = new PhysicsRenderOrientation(-end.X, -end.Y, -end.Z, -end.W);
            cosine = -cosine;
        }

        if (cosine > SlerpLinearThreshold)
        {
            return Lerp(start, end, factor).Normalized().Canonical();
        }

        double angle = Math.Acos(Math.Clamp(cosine, -1, 1));
        double sine = Math.Sin(angle);
        if (sine <= double.Epsilon)
        {
            return Lerp(start, end, factor).Normalized().Canonical();
        }

        double startScale = Math.Sin((1 - factor) * angle) / sine;
        double endScale = Math.Sin(factor * angle) / sine;
        return new PhysicsRenderOrientation(
            (start.X * startScale) + (end.X * endScale),
            (start.Y * startScale) + (end.Y * endScale),
            (start.Z * startScale) + (end.Z * endScale),
            (start.W * startScale) + (end.W * endScale))
            .Normalized()
            .Canonical();
    }

    private static PhysicsRenderOrientation Lerp(
        PhysicsRenderOrientation from,
        PhysicsRenderOrientation to,
        double alpha) =>
        new(
            from.X + ((to.X - from.X) * alpha),
            from.Y + ((to.Y - from.Y) * alpha),
            from.Z + ((to.Z - from.Z) * alpha),
            from.W + ((to.W - from.W) * alpha));
}

/// <summary>
/// Reports one simulated body's renderable pose for a single published snapshot.
/// </summary>
/// <param name="Id">The stable simulation identity of the body.</param>
/// <param name="Position">The world-space position, in stage units.</param>
/// <param name="Orientation">The world-space orientation.</param>
/// <param name="IsSleeping">Whether the solver currently considers the body asleep.</param>
/// <param name="IsKinematic">Whether the body is kinematic rather than dynamic.</param>
public readonly record struct PhysicsRenderBodyState(
    PhysicsRenderObjectId Id,
    UsdVec3d Position,
    PhysicsRenderOrientation Orientation,
    bool IsSleeping,
    bool IsKinematic);

/// <summary>
/// Describes one deformable, cloth, or particle geometry region inside a published snapshot.
/// </summary>
/// <param name="Id">The stable simulation identity of the deformable object.</param>
/// <param name="Domain">The domain that produced the region.</param>
/// <param name="VertexOffset">The first vertex triple index inside the shared vertex buffer.</param>
/// <param name="VertexCount">The number of vertex triples the region carries.</param>
/// <param name="TopologyRevision">
/// The revision of the region's element topology; a change means the region must be re-uploaded
/// and must never be interpolated against the previous snapshot.
/// </param>
public readonly record struct PhysicsRenderDeformableRegion(
    PhysicsRenderObjectId Id,
    PhysicsRenderDomain Domain,
    int VertexOffset,
    int VertexCount,
    ulong TopologyRevision);

/// <summary>
/// Declares the bounded storage every physics render buffer preallocates.
/// </summary>
public readonly record struct PhysicsRenderCapacities
{
    /// <summary>Initializes bounded physics render capacities.</summary>
    /// <param name="bodyCapacity">The number of rigid, controller, and link poses.</param>
    /// <param name="deformableCapacity">The number of deformable regions.</param>
    /// <param name="deformableVertexCapacity">The number of deformable vertex triples.</param>
    /// <exception cref="ArgumentOutOfRangeException">A capacity is negative.</exception>
    public PhysicsRenderCapacities(
        int bodyCapacity,
        int deformableCapacity = 0,
        int deformableVertexCapacity = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bodyCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(deformableCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(deformableVertexCapacity);
        BodyCapacity = bodyCapacity;
        DeformableCapacity = deformableCapacity;
        DeformableVertexCapacity = deformableVertexCapacity;
    }

    /// <summary>Gets the number of body poses the bounded buffers can carry.</summary>
    public int BodyCapacity { get; }

    /// <summary>Gets the number of deformable regions the bounded buffers can carry.</summary>
    public int DeformableCapacity { get; }

    /// <summary>Gets the number of deformable vertex triples the bounded buffers can carry.</summary>
    public int DeformableVertexCapacity { get; }

    /// <summary>Determines whether this capacity can hold everything another one can.</summary>
    /// <param name="other">The capacity that must fit.</param>
    /// <returns><see langword="true"/> when nothing would be truncated.</returns>
    public bool Contains(PhysicsRenderCapacities other) =>
        BodyCapacity >= other.BodyCapacity &&
        DeformableCapacity >= other.DeformableCapacity &&
        DeformableVertexCapacity >= other.DeformableVertexCapacity;
}
