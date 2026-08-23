// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Rendering;

/// <summary>
/// One complete, preallocated physics result the renderer consumes.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot carries only detached values: stable simulation identities, world poses, and bounded
/// deformable geometry. It never retains a stage, prim, layer, or solver handle, so it stays valid
/// after the world that produced it advances, is rebuilt, or is disposed.
/// </para>
/// <para>
/// Every buffer is allocated once, when the snapshot is constructed, and is then reused forever.
/// Filling a snapshot never allocates: a producer calls <see cref="BeginWrite"/>, adds entries
/// until the bounded capacity is reached, and calls <see cref="EndWrite"/>. Entries past capacity
/// are dropped and reported through the owning domain's <see cref="PhysicsRenderDomainReport"/>
/// rather than growing the buffer or failing the frame.
/// </para>
/// </remarks>
public sealed class PhysicsRenderSnapshot
{
    private const int DomainCount = (int)PhysicsRenderDomain.Deformable + 1;

    private readonly PhysicsRenderBodyState[] _bodies;
    private readonly PhysicsRenderDeformableRegion[] _regions;
    private readonly float[] _vertices;
    private readonly PhysicsRenderDomainStatus[] _domainStatus;
    private readonly int[] _domainCount;
    private readonly int[] _domainDropped;
    private int _bodyCount;
    private int _regionCount;
    private int _vertexCount;
    private bool _writing;

    /// <summary>Initializes a snapshot whose bounded buffers are allocated once.</summary>
    /// <param name="capacities">The bounded storage the snapshot preallocates.</param>
    public PhysicsRenderSnapshot(PhysicsRenderCapacities capacities)
    {
        Capacities = capacities;
        _bodies = capacities.BodyCapacity == 0
            ? []
            : new PhysicsRenderBodyState[capacities.BodyCapacity];
        _regions = capacities.DeformableCapacity == 0
            ? []
            : new PhysicsRenderDeformableRegion[capacities.DeformableCapacity];
        _vertices = capacities.DeformableVertexCapacity == 0
            ? []
            : new float[checked(capacities.DeformableVertexCapacity * 3)];
        _domainStatus = new PhysicsRenderDomainStatus[DomainCount];
        _domainCount = new int[DomainCount];
        _domainDropped = new int[DomainCount];
    }

    /// <summary>Gets the bounded storage this snapshot preallocated.</summary>
    public PhysicsRenderCapacities Capacities { get; }

    /// <summary>Gets the monotonic publication revision this snapshot carries.</summary>
    public ulong Revision { get; internal set; }

    /// <summary>Gets the number of fixed sub-steps advanced since the last reset.</summary>
    public ulong StepIndex { get; private set; }

    /// <summary>
    /// Gets the revision of the simulated object set and its topology.
    /// </summary>
    /// <remarks>
    /// A change means identities were added, removed, or rebuilt, so values from an earlier
    /// snapshot are stale and must be snapped rather than interpolated.
    /// </remarks>
    public ulong IdentityRevision { get; private set; }

    /// <summary>Gets the simulated seconds advanced since the authored start time code.</summary>
    public double SimulationSeconds { get; private set; }

    /// <summary>Gets the authored time code this snapshot reflects.</summary>
    public double TimeCode { get; private set; }

    /// <summary>Gets the fixed simulation step, in seconds, that produced this snapshot.</summary>
    public double FixedStepSeconds { get; private set; }

    /// <summary>Gets a value indicating whether the producer completed this snapshot.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Gets the number of body poses this snapshot carries.</summary>
    public int BodyCount => _bodyCount;

    /// <summary>Gets the number of deformable regions this snapshot carries.</summary>
    public int DeformableCount => _regionCount;

    /// <summary>Gets the number of deformable vertex triples this snapshot carries.</summary>
    public int DeformableVertexCount => _vertexCount;

    /// <summary>Gets the body poses this snapshot carries.</summary>
    public ReadOnlySpan<PhysicsRenderBodyState> Bodies => _bodies.AsSpan(0, _bodyCount);

    /// <summary>Gets the deformable regions this snapshot carries.</summary>
    public ReadOnlySpan<PhysicsRenderDeformableRegion> Deformables =>
        _regions.AsSpan(0, _regionCount);

    /// <summary>Gets the shared deformable vertex components, three per vertex.</summary>
    public ReadOnlySpan<float> DeformableVertices => _vertices.AsSpan(0, _vertexCount * 3);

    /// <summary>Gets the deformable regions this snapshot carries, as borrowed memory.</summary>
    internal ReadOnlyMemory<PhysicsRenderDeformableRegion> DeformablesMemory =>
        _regions.AsMemory(0, _regionCount);

    /// <summary>Gets the shared deformable vertex components, as borrowed memory.</summary>
    internal ReadOnlyMemory<float> DeformableVerticesMemory => _vertices.AsMemory(0, _vertexCount * 3);

    /// <summary>Gets a value indicating whether any bounded buffer dropped entries.</summary>
    public bool HasOverflow
    {
        get
        {
            foreach (int dropped in _domainDropped)
            {
                if (dropped != 0)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>Returns the vertex components of one deformable region.</summary>
    /// <param name="region">The region whose vertices are read.</param>
    /// <returns>The region's vertex components, three per vertex.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The region does not lie inside this snapshot's vertex buffer.
    /// </exception>
    public ReadOnlySpan<float> GetDeformableVertices(PhysicsRenderDeformableRegion region)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(region.VertexOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(region.VertexCount);
        int end = checked(region.VertexOffset + region.VertexCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(end, _vertexCount);
        return _vertices.AsSpan(region.VertexOffset * 3, region.VertexCount * 3);
    }

    /// <summary>Returns the renderable state of one simulation domain.</summary>
    /// <param name="domain">The reported domain.</param>
    /// <returns>The domain's status, counts, and drop count.</returns>
    public PhysicsRenderDomainReport GetDomain(PhysicsRenderDomain domain)
    {
        int index = DomainIndex(domain);
        return new PhysicsRenderDomainReport(
            domain,
            _domainStatus[index],
            _domainCount[index],
            DomainCapacity(domain),
            _domainDropped[index]);
    }

    /// <summary>Begins filling this snapshot, discarding whatever it carried before.</summary>
    /// <param name="stepIndex">The number of fixed sub-steps advanced since the last reset.</param>
    /// <param name="identityRevision">The revision of the simulated object set and topology.</param>
    /// <param name="simulationSeconds">The simulated seconds this snapshot reflects.</param>
    /// <param name="timeCode">The authored time code this snapshot reflects.</param>
    /// <param name="fixedStepSeconds">The fixed simulation step, in seconds.</param>
    /// <exception cref="ArgumentOutOfRangeException">A time value is not finite.</exception>
    public void BeginWrite(
        ulong stepIndex,
        ulong identityRevision,
        double simulationSeconds,
        double timeCode,
        double fixedStepSeconds)
    {
        ThrowIfNotFinite(simulationSeconds, nameof(simulationSeconds));
        ThrowIfNotFinite(timeCode, nameof(timeCode));
        ThrowIfNotFinite(fixedStepSeconds, nameof(fixedStepSeconds));

        ResetCounters();
        StepIndex = stepIndex;
        IdentityRevision = identityRevision;
        SimulationSeconds = simulationSeconds;
        TimeCode = timeCode;
        FixedStepSeconds = fixedStepSeconds;
        IsComplete = false;
        _writing = true;
    }

    /// <summary>Appends one body pose to the bounded body buffer.</summary>
    /// <param name="body">The pose to append.</param>
    /// <returns>
    /// <see langword="true"/> when the pose was stored; <see langword="false"/> when the bounded
    /// capacity was already reached and the pose was dropped and counted instead.
    /// </returns>
    /// <exception cref="InvalidOperationException"><see cref="BeginWrite"/> was not called.</exception>
    public bool TryAddBody(in PhysicsRenderBodyState body)
    {
        ThrowIfNotWriting();
        PhysicsRenderDomain domain = DomainOf(body.Id.Kind);
        int index = DomainIndex(domain);
        if (_bodyCount >= _bodies.Length)
        {
            _domainDropped[index]++;
            return false;
        }

        _bodies[_bodyCount++] = body with { Orientation = body.Orientation.Normalized().Canonical() };
        _domainCount[index]++;
        if (_domainStatus[index] == PhysicsRenderDomainStatus.Unavailable)
        {
            _domainStatus[index] = PhysicsRenderDomainStatus.Supported;
        }
        return true;
    }

    /// <summary>Appends one deformable geometry region to the bounded vertex buffer.</summary>
    /// <param name="id">The stable simulation identity of the deformable object.</param>
    /// <param name="domain">The domain that produced the region.</param>
    /// <param name="vertices">The region's vertex components, three per vertex.</param>
    /// <param name="topologyRevision">The revision of the region's element topology.</param>
    /// <returns>
    /// <see langword="true"/> when the region was stored; <see langword="false"/> when a bounded
    /// capacity was reached and the region was dropped and counted instead.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="vertices"/> does not contain whole vertex triples.
    /// </exception>
    /// <exception cref="InvalidOperationException"><see cref="BeginWrite"/> was not called.</exception>
    public bool TryAddDeformable(
        PhysicsRenderObjectId id,
        PhysicsRenderDomain domain,
        ReadOnlySpan<float> vertices,
        ulong topologyRevision)
    {
        ThrowIfNotWriting();
        if (vertices.Length % 3 != 0)
        {
            throw new ArgumentException(
                "Deformable vertices must contain three components per vertex.",
                nameof(vertices));
        }

        int index = DomainIndex(domain);
        int vertexCount = vertices.Length / 3;
        if (_regionCount >= _regions.Length ||
            checked(_vertexCount + vertexCount) > Capacities.DeformableVertexCapacity)
        {
            _domainDropped[index]++;
            return false;
        }

        vertices.CopyTo(_vertices.AsSpan(_vertexCount * 3));
        _regions[_regionCount++] = new PhysicsRenderDeformableRegion(
            id,
            domain,
            _vertexCount,
            vertexCount,
            topologyRevision);
        _vertexCount += vertexCount;
        _domainCount[index]++;
        if (_domainStatus[index] == PhysicsRenderDomainStatus.Unavailable)
        {
            _domainStatus[index] = PhysicsRenderDomainStatus.Supported;
        }
        return true;
    }

    /// <summary>Declares the renderable state of one domain the producer knows about.</summary>
    /// <param name="domain">The reported domain.</param>
    /// <param name="status">The renderable state to report.</param>
    /// <exception cref="InvalidOperationException"><see cref="BeginWrite"/> was not called.</exception>
    public void SetDomainStatus(PhysicsRenderDomain domain, PhysicsRenderDomainStatus status)
    {
        ThrowIfNotWriting();
        _domainStatus[DomainIndex(domain)] = status;
    }

    /// <summary>Completes this snapshot so a consumer may read it.</summary>
    /// <exception cref="InvalidOperationException"><see cref="BeginWrite"/> was not called.</exception>
    public void EndWrite()
    {
        ThrowIfNotWriting();
        for (int index = 0; index < _domainDropped.Length; index++)
        {
            if (_domainDropped[index] != 0)
            {
                _domainStatus[index] = PhysicsRenderDomainStatus.Truncated;
            }
        }

        _writing = false;
        IsComplete = true;
    }

    /// <summary>Copies every published value into another bounded snapshot.</summary>
    /// <remarks>
    /// The copy never allocates and never grows the destination: entries that do not fit are
    /// dropped and reported through the destination's domain reports, so a consumer whose bounded
    /// storage is smaller than the producer's still renders the entries it can hold.
    /// </remarks>
    /// <param name="destination">The snapshot the values are copied into.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
    public void CopyTo(PhysicsRenderSnapshot destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (ReferenceEquals(destination, this))
        {
            return;
        }

        destination.ResetCounters();
        destination.Revision = Revision;
        destination.StepIndex = StepIndex;
        destination.IdentityRevision = IdentityRevision;
        destination.SimulationSeconds = SimulationSeconds;
        destination.TimeCode = TimeCode;
        destination.FixedStepSeconds = FixedStepSeconds;
        destination.IsComplete = IsComplete;
        destination._writing = false;

        Array.Copy(_domainStatus, destination._domainStatus, _domainStatus.Length);
        Array.Copy(_domainDropped, destination._domainDropped, _domainDropped.Length);

        int bodies = Math.Min(_bodyCount, destination._bodies.Length);
        _bodies.AsSpan(0, bodies).CopyTo(destination._bodies);
        destination._bodyCount = bodies;

        for (int index = 0; index < _domainCount.Length; index++)
        {
            destination._domainCount[index] = _domainCount[index];
        }

        for (int index = bodies; index < _bodyCount; index++)
        {
            int domain = DomainIndex(DomainOf(_bodies[index].Id.Kind));
            destination._domainCount[domain]--;
            destination._domainDropped[domain]++;
        }

        for (int index = 0; index < _regionCount; index++)
        {
            PhysicsRenderDeformableRegion region = _regions[index];
            int domain = DomainIndex(region.Domain);
            if (destination._regionCount >= destination._regions.Length ||
                checked(destination._vertexCount + region.VertexCount) >
                    destination.Capacities.DeformableVertexCapacity)
            {
                destination._domainCount[domain]--;
                destination._domainDropped[domain]++;
                continue;
            }

            _vertices
                .AsSpan(region.VertexOffset * 3, region.VertexCount * 3)
                .CopyTo(destination._vertices.AsSpan(destination._vertexCount * 3));
            destination._regions[destination._regionCount++] = region with
            {
                VertexOffset = destination._vertexCount
            };
            destination._vertexCount += region.VertexCount;
        }

        for (int index = 0; index < destination._domainDropped.Length; index++)
        {
            if (destination._domainDropped[index] != 0 &&
                destination._domainStatus[index] == PhysicsRenderDomainStatus.Supported)
            {
                destination._domainStatus[index] = PhysicsRenderDomainStatus.Truncated;
            }
        }
    }

    /// <summary>Clears every value so the snapshot carries nothing.</summary>
    public void Clear()
    {
        ResetCounters();
        Revision = 0;
        StepIndex = 0;
        IdentityRevision = 0;
        SimulationSeconds = 0;
        TimeCode = 0;
        FixedStepSeconds = 0;
        IsComplete = false;
        _writing = false;
    }

    private void ResetCounters()
    {
        _bodyCount = 0;
        _regionCount = 0;
        _vertexCount = 0;
        Array.Clear(_domainStatus);
        Array.Clear(_domainCount);
        Array.Clear(_domainDropped);
    }

    private int DomainCapacity(PhysicsRenderDomain domain) => domain switch
    {
        PhysicsRenderDomain.Particles or
        PhysicsRenderDomain.Cloth or
        PhysicsRenderDomain.Deformable => Capacities.DeformableCapacity,
        _ => Capacities.BodyCapacity
    };

    private void ThrowIfNotWriting()
    {
        if (!_writing)
        {
            throw new InvalidOperationException(
                "The snapshot is not being written; call BeginWrite first.");
        }
    }

    private static int DomainIndex(PhysicsRenderDomain domain) => (int)domain;

    private static PhysicsRenderDomain DomainOf(PhysicsRenderObjectKind kind) => kind switch
    {
        PhysicsRenderObjectKind.Articulation or
        PhysicsRenderObjectKind.ArticulationLink => PhysicsRenderDomain.Articulation,
        PhysicsRenderObjectKind.Controller => PhysicsRenderDomain.Controller,
        PhysicsRenderObjectKind.Vehicle => PhysicsRenderDomain.Vehicle,
        PhysicsRenderObjectKind.ParticleSystem => PhysicsRenderDomain.Particles,
        PhysicsRenderObjectKind.Deformable => PhysicsRenderDomain.Deformable,
        _ => PhysicsRenderDomain.RigidBody
    };

    private static void ThrowIfNotFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value must be finite.");
        }
    }
}
