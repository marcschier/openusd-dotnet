// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;

namespace OpenUsd.Physics.Baking;

/// <summary>
/// Identifies the deformable domain one <see cref="UsdPhysicsPointSample"/> was produced by.
/// </summary>
public enum UsdPhysicsPointSampleDomain
{
    /// <summary>A particle system's particle positions.</summary>
    Particles,

    /// <summary>A cloth or other surface deformable's simulated vertices.</summary>
    Cloth,

    /// <summary>A volume deformable's simulated vertices.</summary>
    Deformable
}

/// <summary>
/// One immutable simulated point sample for a particle system, cloth, or volume deformable.
/// </summary>
/// <remarks>
/// A sample carries its own <see cref="TopologyRevision"/>. When it does not match the revision the
/// matching <see cref="UsdPhysicsBakeBinding"/> was extracted at, the sample refers to topology the
/// stage no longer has and the whole batch is rejected without authoring anything.
/// </remarks>
public sealed class UsdPhysicsPointSample : IUsdDetachedResult
{
    private readonly ImmutableArray<UsdVec3d> _points;
    private readonly ImmutableArray<UsdVec3d> _velocities;
    private readonly ImmutableArray<int> _faceVertexCounts;
    private readonly ImmutableArray<int> _faceVertexIndices;

    /// <summary>Initializes a point sample by defensively copying every span.</summary>
    /// <param name="id">The stable identity of the simulated deformable.</param>
    /// <param name="domain">The deformable domain that produced the sample.</param>
    /// <param name="topologyRevision">The topology revision the sample was produced at.</param>
    /// <param name="points">The simulated world-space positions.</param>
    /// <param name="velocities">The simulated world-space velocities, or empty when unavailable.</param>
    /// <param name="faceVertexCounts">
    /// The simulated face vertex counts, or empty when the topology is unchanged. Supplying topology
    /// requires supplying both counts and indices.
    /// </param>
    /// <param name="faceVertexIndices">The simulated face vertex indices, or empty.</param>
    public UsdPhysicsPointSample(
        UsdPhysicsObjectId id,
        UsdPhysicsPointSampleDomain domain,
        ulong topologyRevision,
        ReadOnlySpan<UsdVec3d> points,
        ReadOnlySpan<UsdVec3d> velocities = default,
        ReadOnlySpan<int> faceVertexCounts = default,
        ReadOnlySpan<int> faceVertexIndices = default)
    {
        if (id.IsNone)
        {
            throw new ArgumentException("A point sample cannot use the sentinel identity.", nameof(id));
        }
        if (points.IsEmpty)
        {
            throw new ArgumentException("A point sample must carry at least one point.", nameof(points));
        }
        if (!velocities.IsEmpty && velocities.Length != points.Length)
        {
            throw new ArgumentException(
                "The velocity count must match the point count.", nameof(velocities));
        }
        if (faceVertexCounts.IsEmpty != faceVertexIndices.IsEmpty)
        {
            throw new ArgumentException(
                "Face vertex counts and indices must be supplied together.", nameof(faceVertexCounts));
        }

        long total = 0;
        foreach (int count in faceVertexCounts)
        {
            if (count < 0)
            {
                throw new ArgumentException(
                    "A face vertex count cannot be negative.", nameof(faceVertexCounts));
            }
            total += count;
        }
        if (total != faceVertexIndices.Length)
        {
            throw new ArgumentException(
                "The face vertex counts do not describe the supplied indices.",
                nameof(faceVertexIndices));
        }
        foreach (int index in faceVertexIndices)
        {
            if ((uint)index >= (uint)points.Length)
            {
                throw new ArgumentException(
                    "A face vertex index falls outside the supplied points.",
                    nameof(faceVertexIndices));
            }
        }

        Id = id;
        Domain = domain;
        TopologyRevision = topologyRevision;
        _points = [.. points];
        _velocities = [.. velocities];
        _faceVertexCounts = [.. faceVertexCounts];
        _faceVertexIndices = [.. faceVertexIndices];
    }

    /// <summary>Gets the stable identity of the simulated deformable.</summary>
    public UsdPhysicsObjectId Id { get; }

    /// <summary>Gets the deformable domain that produced this sample.</summary>
    public UsdPhysicsPointSampleDomain Domain { get; }

    /// <summary>Gets the topology revision this sample was produced at.</summary>
    public ulong TopologyRevision { get; }

    /// <summary>Gets the simulated world-space positions.</summary>
    public ReadOnlySpan<UsdVec3d> Points => _points.AsSpan();

    /// <summary>Gets the simulated world-space velocities, which is empty when unavailable.</summary>
    public ReadOnlySpan<UsdVec3d> Velocities => _velocities.AsSpan();

    /// <summary>Gets the simulated face vertex counts, which is empty when topology is unchanged.</summary>
    public ReadOnlySpan<int> FaceVertexCounts => _faceVertexCounts.AsSpan();

    /// <summary>Gets the simulated face vertex indices, which is empty when topology is unchanged.</summary>
    public ReadOnlySpan<int> FaceVertexIndices => _faceVertexIndices.AsSpan();

    /// <summary>Gets a value indicating whether this sample carries simulated topology.</summary>
    public bool HasTopology => !_faceVertexCounts.IsEmpty;
}

/// <summary>
/// One complete, immutable set of simulation results ready to be applied or baked.
/// </summary>
/// <remarks>
/// A batch is fully detached: it retains no stage, prim, native handle, or transport buffer, so it
/// stays valid after the world that produced it advances, resets, or is disposed. Both the preview
/// and the bake consume a batch whole; a batch is never partially applied.
/// </remarks>
public sealed class UsdPhysicsResultBatch : IUsdDetachedResult
{
    private readonly ImmutableArray<UsdPhysicsBodyPose> _bodies;
    private readonly ImmutableArray<UsdPhysicsPointSample> _pointSamples;

    /// <summary>Initializes a result batch by defensively copying every element.</summary>
    /// <param name="identityRevision">The extraction revision the results were produced against.</param>
    /// <param name="timeCode">The authored time code the results describe.</param>
    /// <param name="bodies">
    /// The rigid, controller, vehicle, and articulation transforms to author.
    /// </param>
    /// <param name="pointSamples">The particle, cloth, and deformable point samples to author.</param>
    public UsdPhysicsResultBatch(
        ulong identityRevision,
        double timeCode,
        ReadOnlySpan<UsdPhysicsBodyPose> bodies,
        IEnumerable<UsdPhysicsPointSample>? pointSamples = null)
    {
        if (!double.IsFinite(timeCode))
        {
            throw new ArgumentOutOfRangeException(nameof(timeCode), "The time code must be finite.");
        }

        IdentityRevision = identityRevision;
        TimeCode = timeCode;
        _bodies = [.. bodies];

        var samples = ImmutableArray.CreateBuilder<UsdPhysicsPointSample>();
        if (pointSamples is not null)
        {
            foreach (UsdPhysicsPointSample sample in pointSamples)
            {
                ArgumentNullException.ThrowIfNull(sample, nameof(pointSamples));
                samples.Add(sample);
            }
        }
        _pointSamples = samples.ToImmutable();
    }

    /// <summary>Creates a batch from one published transport frame.</summary>
    /// <param name="frame">The leased frame to copy every body pose out of.</param>
    /// <param name="identityRevision">The extraction revision the frame was produced against.</param>
    /// <param name="pointSamples">Point samples to layer on top of the frame's body poses.</param>
    /// <returns>A detached batch that outlives the frame lease.</returns>
    /// <remarks>
    /// Deformable geometry is not folded in automatically. A point sample authors geometry rather
    /// than a transform, and a host that has not bound its deformable prims would turn every such
    /// sample into a rejected record, so the caller decides by passing
    /// <see cref="DeformationSamples"/> explicitly.
    /// </remarks>
    public static UsdPhysicsResultBatch FromFrame(
        UsdPhysicsFrame frame,
        ulong identityRevision,
        IEnumerable<UsdPhysicsPointSample>? pointSamples = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return new UsdPhysicsResultBatch(
            identityRevision, frame.TimeCode, frame.Bodies, pointSamples);
    }

    /// <summary>
    /// Turns every complete deformation window of one frame into an immutable point sample.
    /// </summary>
    /// <param name="frame">The leased frame whose deformation windows are copied.</param>
    /// <param name="topologyRevision">The extraction revision the windows were produced against.</param>
    /// <returns>One sample per published window, in frame order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <remarks>
    /// A window is copied whole or not at all. The frame contract already guarantees that a body
    /// whose vertices did not fit was dropped rather than truncated, and the window bounds are
    /// re-checked here because a sample outlives the lease that produced it.
    /// </remarks>
    public static ImmutableArray<UsdPhysicsPointSample> DeformationSamples(
        UsdPhysicsFrame frame,
        ulong topologyRevision)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ReadOnlySpan<UsdPhysicsDeformation> windows = frame.Deformations;
        if (windows.IsEmpty)
        {
            return [];
        }

        ReadOnlySpan<UsdVec3d> vertices = frame.DeformationVertices;
        var samples = ImmutableArray.CreateBuilder<UsdPhysicsPointSample>(windows.Length);
        for (int index = 0; index < windows.Length; index++)
        {
            ref readonly UsdPhysicsDeformation window = ref windows[index];
            if (window.Id.IsNone || window.VertexCount <= 0 || window.VertexOffset < 0 ||
                window.VertexOffset > vertices.Length - window.VertexCount)
            {
                continue;
            }

            samples.Add(new UsdPhysicsPointSample(
                window.Id,
                MapDomain(window.Kind),
                topologyRevision,
                vertices.Slice(window.VertexOffset, window.VertexCount)));
        }

        return samples.Count == samples.Capacity ? samples.MoveToImmutable() : samples.ToImmutable();
    }

    private static UsdPhysicsPointSampleDomain MapDomain(UsdPhysicsDeformationKind kind) => kind switch
    {
        UsdPhysicsDeformationKind.Surface => UsdPhysicsPointSampleDomain.Cloth,
        UsdPhysicsDeformationKind.Volume => UsdPhysicsPointSampleDomain.Deformable,
        _ => UsdPhysicsPointSampleDomain.Particles
    };

    /// <summary>Gets the extraction revision these results were produced against.</summary>
    public ulong IdentityRevision { get; }

    /// <summary>Gets the authored time code these results describe.</summary>
    public double TimeCode { get; }

    /// <summary>Gets the simulated body transforms in this batch.</summary>
    public ReadOnlySpan<UsdPhysicsBodyPose> Bodies => _bodies.AsSpan();

    /// <summary>Gets the simulated point samples in this batch.</summary>
    public IReadOnlyList<UsdPhysicsPointSample> PointSamples => _pointSamples;

    /// <summary>Gets the total number of records this batch would author.</summary>
    public int RecordCount => _bodies.Length + _pointSamples.Length;
}
