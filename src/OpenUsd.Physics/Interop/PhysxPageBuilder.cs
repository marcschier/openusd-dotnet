// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace OpenUsd.Physics.Interop;

/// <summary>
/// Builds one pointer-free build page from managed records.
/// </summary>
/// <remarks>
/// The builder stages every section in pooled managed memory and emits the sections in the exact
/// order the native reference builder uses: strings, identities, scenes, materials, shapes, actors,
/// actor shapes, joints, filter pairs, mesh points, and mesh indices. Each section starts on an eight
/// byte boundary, empty sections declare a zero offset and a zero count, and the header is written
/// last because it records the final byte size. The finished page is validated with the managed
/// validator before it is handed out, so a page that cannot be built is reported as a failure instead
/// of being passed to the native validator and rejected there.
/// </remarks>
internal sealed class PhysxPageBuilder : IDisposable
{
    private const uint DefaultDiagnosticCapacity = 64;
    private const uint DefaultQueryHitCapacity = 256;
    private const uint MinimumEventCapacity = 64;
    private const uint EventsPerMovableActor = 4;

    private readonly PhysxIdentityTable _identities = new();
    private readonly List<PhysxSceneDesc> _scenes = [];
    private readonly List<PhysxMaterialDesc> _materials = [];
    private readonly List<PhysxShapeDesc> _shapes = [];
    private readonly List<PhysxActorDesc> _actors = [];
    private readonly List<PhysxActorShapeRef> _actorShapes = [];
    private readonly List<PhysxJointDesc> _joints = [];
    private readonly List<PhysxFilterPair> _filterPairs = [];
    private readonly List<PhysxVec3f> _meshPoints = [];
    private readonly List<uint> _meshIndices = [];
    private readonly List<PhysxHeightfieldSample> _heightfieldSamples = [];
    private readonly List<PhysxArticulationDesc> _articulations = [];
    private readonly List<PhysxArticulationLinkDesc> _articulationLinks = [];
    private readonly List<PhysxControllerDesc> _controllers = [];
    private readonly List<PhysxTendonDesc> _tendons = [];
    private readonly List<PhysxTendonNodeDesc> _tendonNodes = [];
    private readonly List<PhysxMimicJointDesc> _mimicJoints = [];
    private readonly List<PhysxVehicleDesc> _vehicles = [];
    private readonly List<PhysxVehicleWheelDesc> _vehicleWheels = [];
    private readonly List<PhysxParticleMaterialDesc> _particleMaterials = [];
    private readonly List<PhysxParticleSystemDesc> _particleSystems = [];
    private readonly List<PhysxParticleBodyDesc> _particleBodies = [];
    private readonly List<PhysxDeformableMaterialDesc> _deformableMaterials = [];
    private readonly List<PhysxDeformableDesc> _deformables = [];
    private bool _disposed;

    /// <summary>Gets the identity table backing this page.</summary>
    internal PhysxIdentityTable Identities => _identities;

    /// <summary>Gets or sets the monotonic extraction revision this page is produced for.</summary>
    internal ulong Revision { get; set; }

    /// <summary>Gets or sets the physics-relevance fingerprint of the source stage.</summary>
    internal ulong SourceHash { get; set; }

    /// <summary>Gets or sets the authored <c>metersPerUnit</c>.</summary>
    internal double MetersPerUnit { get; set; } = 1.0;

    /// <summary>Gets or sets the authored <c>kilogramsPerUnit</c>.</summary>
    internal double KilogramsPerUnit { get; set; } = 1.0;

    /// <summary>Gets or sets the authored <c>timeCodesPerSecond</c>.</summary>
    internal double TimeCodesPerSecond { get; set; } = 24.0;

    /// <summary>Gets or sets the authored start time code.</summary>
    internal double StartTimeCode { get; set; }

    /// <summary>Gets or sets the authored end time code.</summary>
    internal double EndTimeCode { get; set; }

    /// <summary>Gets or sets the authored up axis.</summary>
    internal PhysxUpAxis UpAxis { get; set; } = PhysxUpAxis.Y;

    /// <summary>Gets or sets the fixed simulation rate, in hertz.</summary>
    internal uint SimulationRateHz { get; set; } = 60;

    /// <summary>Gets or sets the maximum number of substeps one step may advance.</summary>
    internal uint MaxSubsteps { get; set; } = 4;

    /// <summary>Gets or sets an explicit body state capacity; the default covers every movable actor.</summary>
    internal uint? BodyStateCapacity { get; set; }

    /// <summary>Gets or sets an explicit event capacity.</summary>
    internal uint? EventCapacity { get; set; }

    /// <summary>Gets or sets an explicit diagnostic capacity.</summary>
    internal uint? DiagnosticCapacity { get; set; }

    /// <summary>Gets or sets an explicit debug line capacity.</summary>
    internal uint? DebugLineCapacity { get; set; }

    /// <summary>Gets or sets an explicit query hit capacity.</summary>
    internal uint? QueryHitCapacity { get; set; }

    /// <summary>Gets or sets an explicit deformation body capacity; null derives it from the page.</summary>
    internal uint? DeformationBodyCapacity { get; set; }

    /// <summary>Gets or sets an explicit deformation vertex capacity; null derives it from the page.</summary>
    internal uint? DeformationPointCapacity { get; set; }

    /// <summary>Gets the number of staged scenes.</summary>
    internal int SceneCount => _scenes.Count;

    /// <summary>Gets the number of staged materials.</summary>
    internal int MaterialCount => _materials.Count;

    /// <summary>Gets the number of staged particle bodies.</summary>
    internal int ParticleBodyCount => _particleBodies.Count;

    /// <summary>Gets the number of staged particle systems.</summary>
    internal int ParticleSystemCount => _particleSystems.Count;

    /// <summary>Gets the number of staged deformables.</summary>
    internal int DeformableCount => _deformables.Count;

    /// <summary>Gets the number of staged shapes.</summary>
    internal int ShapeCount => _shapes.Count;

    /// <summary>Gets the number of staged actors.</summary>
    internal int ActorCount => _actors.Count;

    /// <summary>Gets the number of staged actor shape references.</summary>
    internal int ActorShapeCount => _actorShapes.Count;

    /// <summary>Gets the type of a staged shape, as a <see cref="PhysxShapeType"/>.</summary>
    internal uint ShapeTypeAt(int index) => _shapes[index].Type;

    /// <summary>Gets the staged shapes in page order.</summary>
    internal IReadOnlyList<PhysxShapeDesc> Shapes => _shapes;

    /// <summary>Gets the staged particle bodies in page order.</summary>
    internal IReadOnlyList<PhysxParticleBodyDesc> ParticleBodies => _particleBodies;

    /// <summary>Gets the staged deformables in page order.</summary>
    internal IReadOnlyList<PhysxDeformableDesc> Deformables => _deformables;

    /// <summary>Gets the shared mesh point section in page order.</summary>
    internal IReadOnlyList<PhysxVec3f> MeshPoints => _meshPoints;

    /// <summary>Gets the number of staged articulation links.</summary>
    internal int ArticulationLinkCount => _articulationLinks.Count;

    /// <summary>Gets the number of staged articulations.</summary>
    internal int ArticulationCount => _articulations.Count;

    /// <summary>Gets the number of staged articulation tendons.</summary>
    internal int TendonCount => _tendons.Count;

    /// <summary>Gets the number of staged articulation tendon nodes.</summary>
    internal int TendonNodeCount => _tendonNodes.Count;

    /// <summary>Gets the number of staged articulation mimic joints.</summary>
    internal int MimicJointCount => _mimicJoints.Count;

    /// <summary>Gets the number of staged vehicles.</summary>
    internal int VehicleCount => _vehicles.Count;

    /// <summary>Gets the number of staged vehicle wheels.</summary>
    internal int VehicleWheelCount => _vehicleWheels.Count;

    /// <summary>Adds an addressable prim to the identity table and returns its stable identity.</summary>
    internal ulong DefineIdentity(
        string path,
        PhysxInstanceDomain domain = PhysxInstanceDomain.Prim,
        uint instanceIndex = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _identities.Add(path, domain, instanceIndex);
    }

    /// <summary>Appends a scene and returns its index.</summary>
    internal int AddScene(in PhysxSceneDesc scene) => Append(_scenes, in scene);

    /// <summary>Appends a material and returns its index.</summary>
    internal int AddMaterial(in PhysxMaterialDesc material) => Append(_materials, in material);

    /// <summary>Appends a shape and returns its index.</summary>
    internal int AddShape(in PhysxShapeDesc shape) => Append(_shapes, in shape);

    /// <summary>Appends an actor and returns its index.</summary>
    /// <remarks>
    /// A description that leaves the principal axes unset carries an all zero quaternion, which
    /// both page readers take for the identity rotation. It is written out as the identity here
    /// so that every page this builder produces states the rotation explicitly.
    /// </remarks>
    internal int AddActor(in PhysxActorDesc actor)
    {
        PhysxActorDesc stored = actor;
        if (stored.PrincipalAxes is { X: 0.0F, Y: 0.0F, Z: 0.0F, W: 0.0F })
        {
            stored.PrincipalAxes = PhysxQuatf.Identity;
        }
        return Append(_actors, in stored);
    }

    /// <summary>Appends an actor-to-shape reference and returns its index.</summary>
    internal int AddActorShape(in PhysxActorShapeRef reference) => Append(_actorShapes, in reference);

    /// <summary>Appends a joint and returns its index.</summary>
    internal int AddJoint(in PhysxJointDesc joint) => Append(_joints, in joint);

    /// <summary>Appends a suppressed collision pair and returns its index.</summary>
    internal int AddFilterPair(in PhysxFilterPair pair) => Append(_filterPairs, in pair);

    /// <summary>Appends mesh points and returns the element offset of the first point.</summary>
    internal uint AddMeshPoints(ReadOnlySpan<PhysxVec3f> points)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint offset = (uint)_meshPoints.Count;
        foreach (PhysxVec3f point in points)
        {
            _meshPoints.Add(point);
        }
        return offset;
    }

    /// <summary>Appends mesh indices and returns the element offset of the first index.</summary>
    internal uint AddMeshIndices(ReadOnlySpan<uint> indices)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint offset = (uint)_meshIndices.Count;
        foreach (uint index in indices)
        {
            _meshIndices.Add(index);
        }
        return offset;
    }

    /// <summary>Appends height field samples and returns the element offset of the first sample.</summary>
    internal uint AddHeightfieldSamples(ReadOnlySpan<PhysxHeightfieldSample> samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint offset = (uint)_heightfieldSamples.Count;
        foreach (PhysxHeightfieldSample sample in samples)
        {
            _heightfieldSamples.Add(sample);
        }
        return offset;
    }

    /// <summary>Appends an articulation and returns its index.</summary>
    internal int AddArticulation(in PhysxArticulationDesc articulation) => Append(_articulations, in articulation);

    /// <summary>Appends an articulation link and returns its index.</summary>
    internal int AddArticulationLink(in PhysxArticulationLinkDesc link) => Append(_articulationLinks, in link);

    /// <summary>Appends a controller and returns its index.</summary>
    internal int AddController(in PhysxControllerDesc controller) => Append(_controllers, in controller);

    /// <summary>Appends an articulation tendon and returns its index.</summary>
    internal int AddTendon(in PhysxTendonDesc tendon) => Append(_tendons, in tendon);

    /// <summary>Appends an articulation tendon node and returns its index.</summary>
    internal int AddTendonNode(in PhysxTendonNodeDesc node) => Append(_tendonNodes, in node);

    /// <summary>Drops the last staged tendon node so a rejected tendon leaves no orphan node.</summary>
    internal void RemoveLastTendonNode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_tendonNodes.Count > 0)
        {
            _tendonNodes.RemoveAt(_tendonNodes.Count - 1);
        }
    }

    /// <summary>Drops the last staged vehicle wheel so a rejected vehicle leaves no orphan wheel.</summary>
    internal void RemoveLastVehicleWheel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_vehicleWheels.Count > 0)
        {
            _vehicleWheels.RemoveAt(_vehicleWheels.Count - 1);
        }
    }

    /// <summary>Appends an articulation mimic joint and returns its index.</summary>
    internal int AddMimicJoint(in PhysxMimicJointDesc mimicJoint) => Append(_mimicJoints, in mimicJoint);

    /// <summary>Appends a vehicle and returns its index.</summary>
    internal int AddVehicle(in PhysxVehicleDesc vehicle) => Append(_vehicles, in vehicle);

    /// <summary>Appends a vehicle wheel and returns its index.</summary>
    internal int AddVehicleWheel(in PhysxVehicleWheelDesc wheel) => Append(_vehicleWheels, in wheel);

    /// <summary>Appends a position based dynamics particle material and returns its index.</summary>
    internal int AddParticleMaterial(in PhysxParticleMaterialDesc material) =>
        Append(_particleMaterials, in material);

    /// <summary>Appends a particle system and returns its index.</summary>
    internal int AddParticleSystem(in PhysxParticleSystemDesc system) => Append(_particleSystems, in system);

    /// <summary>Appends a particle body and returns its index.</summary>
    internal int AddParticleBody(in PhysxParticleBodyDesc body) => Append(_particleBodies, in body);

    /// <summary>Appends a surface or volume deformable material and returns its index.</summary>
    internal int AddDeformableMaterial(in PhysxDeformableMaterialDesc material) =>
        Append(_deformableMaterials, in material);

    /// <summary>Appends a surface or volume deformable and returns its index.</summary>
    internal int AddDeformable(in PhysxDeformableDesc deformable) => Append(_deformables, in deformable);

    /// <summary>Builds and validates the page.</summary>
    /// <exception cref="InvalidOperationException">The staged records do not form a valid page.</exception>
    internal PhysxBuildPage Build()
    {
        if (!TryBuild(out PhysxBuildPage? page, out PhysxPageValidationResult result))
        {
            throw new InvalidOperationException(result.Message);
        }
        return page;
    }

    /// <summary>Builds and validates the page without throwing.</summary>
    internal bool TryBuild(
        [NotNullWhen(true)] out PhysxBuildPage? page,
        out PhysxPageValidationResult result)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        page = null;

        using var buffer = new PhysxPooledBuffer(EstimateByteLength());
        var header = new PhysxBuildPageHeader
        {
            Magic = PhysxAbi.PageMagic,
            AbiVersion = PhysxAbi.Version,
            HeaderSize = PhysxAbi.RecordSizes.BuildPageHeader,
            Revision = Revision,
            SourceHash = SourceHash,
            MetersPerUnit = MetersPerUnit,
            KilogramsPerUnit = KilogramsPerUnit,
            TimeCodesPerSecond = TimeCodesPerSecond,
            StartTimeCode = StartTimeCode,
            EndTimeCode = EndTimeCode,
            UpAxis = (uint)UpAxis,
            Flags = 0,
            SimulationRateHz = SimulationRateHz,
            MaxSubsteps = MaxSubsteps,
            Capacities = ResolveCapacities()
        };

        buffer.Write(in header);
        header.StringBytes = WriteSection(buffer, _identities.StringBytes);
        header.Identities = WriteSection<PhysxIdentityRecord>(buffer, _identities.ToRecords());
        header.Scenes = WriteSection<PhysxSceneDesc>(buffer, CollectionsMarshal.AsSpan(_scenes));
        header.Materials = WriteSection<PhysxMaterialDesc>(buffer, CollectionsMarshal.AsSpan(_materials));
        header.Shapes = WriteSection<PhysxShapeDesc>(buffer, CollectionsMarshal.AsSpan(_shapes));
        header.Actors = WriteSection<PhysxActorDesc>(buffer, CollectionsMarshal.AsSpan(_actors));
        header.ActorShapes = WriteSection<PhysxActorShapeRef>(buffer, CollectionsMarshal.AsSpan(_actorShapes));
        header.Joints = WriteSection<PhysxJointDesc>(buffer, CollectionsMarshal.AsSpan(_joints));
        header.FilterPairs = WriteSection<PhysxFilterPair>(buffer, CollectionsMarshal.AsSpan(_filterPairs));
        header.MeshPoints = WriteSection<PhysxVec3f>(buffer, CollectionsMarshal.AsSpan(_meshPoints));
        header.MeshIndices = WriteSection<uint>(buffer, CollectionsMarshal.AsSpan(_meshIndices));
        header.HeightfieldSamples =
            WriteSection<PhysxHeightfieldSample>(buffer, CollectionsMarshal.AsSpan(_heightfieldSamples));
        header.Articulations =
            WriteSection<PhysxArticulationDesc>(buffer, CollectionsMarshal.AsSpan(_articulations));
        header.ArticulationLinks =
            WriteSection<PhysxArticulationLinkDesc>(buffer, CollectionsMarshal.AsSpan(_articulationLinks));
        header.Controllers =
            WriteSection<PhysxControllerDesc>(buffer, CollectionsMarshal.AsSpan(_controllers));
        header.ArticulationTendons =
            WriteSection<PhysxTendonDesc>(buffer, CollectionsMarshal.AsSpan(_tendons));
        header.ArticulationTendonNodes =
            WriteSection<PhysxTendonNodeDesc>(buffer, CollectionsMarshal.AsSpan(_tendonNodes));
        header.ArticulationMimicJoints =
            WriteSection<PhysxMimicJointDesc>(buffer, CollectionsMarshal.AsSpan(_mimicJoints));
        header.Vehicles = WriteSection<PhysxVehicleDesc>(buffer, CollectionsMarshal.AsSpan(_vehicles));
        header.VehicleWheels =
            WriteSection<PhysxVehicleWheelDesc>(buffer, CollectionsMarshal.AsSpan(_vehicleWheels));
        header.ParticleMaterials =
            WriteSection<PhysxParticleMaterialDesc>(buffer, CollectionsMarshal.AsSpan(_particleMaterials));
        header.ParticleSystems =
            WriteSection<PhysxParticleSystemDesc>(buffer, CollectionsMarshal.AsSpan(_particleSystems));
        header.ParticleBodies =
            WriteSection<PhysxParticleBodyDesc>(buffer, CollectionsMarshal.AsSpan(_particleBodies));
        header.DeformableMaterials =
            WriteSection<PhysxDeformableMaterialDesc>(buffer, CollectionsMarshal.AsSpan(_deformableMaterials));
        header.Deformables = WriteSection<PhysxDeformableDesc>(buffer, CollectionsMarshal.AsSpan(_deformables));
        buffer.PadTo((int)PhysxAbi.PageAlignment);
        header.ByteSize = (ulong)buffer.Length;
        buffer.Overwrite(0, MemoryMarshal.AsBytes(new ReadOnlySpan<PhysxBuildPageHeader>(in header)));

        result = PhysxPageValidator.Validate(buffer.Written);
        if (!result.IsValid)
        {
            return false;
        }

        page = new PhysxBuildPage(buffer.Written, result.Validation);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _identities.Dispose();
    }

    private PhysxResultCapacities ResolveCapacities()
    {
        uint movableActors = 0;
        foreach (PhysxActorDesc actor in CollectionsMarshal.AsSpan(_actors))
        {
            if (actor.Type != (uint)PhysxActorType.Static)
            {
                movableActors++;
            }
        }

        uint publishedWheels = 0;
        foreach (PhysxVehicleDesc vehicle in CollectionsMarshal.AsSpan(_vehicles))
        {
            if ((vehicle.Flags & (uint)PhysxVehicleFlags.PublishWheels) != 0)
            {
                publishedWheels += vehicle.WheelCount;
            }
        }

        uint bodyStateCount =
            movableActors + (uint)_articulationLinks.Count + (uint)_controllers.Count + publishedWheels;
        uint events = EventCapacity ?? Math.Clamp(
            bodyStateCount * EventsPerMovableActor,
            MinimumEventCapacity,
            PhysxAbi.MaxResultCapacity);

        // Every CUDA backed object publishes exactly one deformation window, so
        // the capacity is derived from what the page declares rather than
        // guessed. A page without a GPU object declares zero for both, which is
        // exactly a caller that allocates no deformation buffer.
        ulong deformationBodies = (ulong)_particleBodies.Count + (ulong)_deformables.Count;
        ulong deformationPoints = 0;
        foreach (PhysxParticleBodyDesc body in CollectionsMarshal.AsSpan(_particleBodies))
        {
            deformationPoints += body.PointCount;
        }
        foreach (PhysxDeformableDesc deformable in CollectionsMarshal.AsSpan(_deformables))
        {
            deformationPoints += deformable.PointCount;
        }

        return new PhysxResultCapacities
        {
            MaxBodyStates = BodyStateCapacity ?? bodyStateCount,
            MaxEvents = events,
            MaxDiagnostics = DiagnosticCapacity ?? DefaultDiagnosticCapacity,
            MaxDebugLines = DebugLineCapacity ?? 0,
            MaxQueryHits = QueryHitCapacity ?? DefaultQueryHitCapacity,
            MaxDeformationBodies = DeformationBodyCapacity ??
                (uint)Math.Min(deformationBodies, PhysxAbi.MaxResultCapacity),
            MaxDeformationPoints = DeformationPointCapacity ??
                (uint)Math.Min(deformationPoints, uint.MaxValue)
        };
    }

    private int EstimateByteLength()
    {
        long total = PhysxAbi.RecordSizes.BuildPageHeader;
        total += _identities.StringBytes.Length + PhysxAbi.PageAlignment;
        total += ((long)_identities.Count * PhysxAbi.RecordSizes.Identity) + PhysxAbi.PageAlignment;
        total += ((long)_scenes.Count * PhysxAbi.RecordSizes.SceneDesc) + PhysxAbi.PageAlignment;
        total += ((long)_materials.Count * PhysxAbi.RecordSizes.MaterialDesc) + PhysxAbi.PageAlignment;
        total += ((long)_shapes.Count * PhysxAbi.RecordSizes.ShapeDesc) + PhysxAbi.PageAlignment;
        total += ((long)_actors.Count * PhysxAbi.RecordSizes.ActorDesc) + PhysxAbi.PageAlignment;
        total += ((long)_actorShapes.Count * PhysxAbi.RecordSizes.ActorShapeRef) + PhysxAbi.PageAlignment;
        total += ((long)_joints.Count * PhysxAbi.RecordSizes.JointDesc) + PhysxAbi.PageAlignment;
        total += ((long)_filterPairs.Count * PhysxAbi.RecordSizes.FilterPair) + PhysxAbi.PageAlignment;
        total += ((long)_meshPoints.Count * PhysxAbi.RecordSizes.Vec3f) + PhysxAbi.PageAlignment;
        total += ((long)_heightfieldSamples.Count * PhysxAbi.RecordSizes.HeightfieldSample) + PhysxAbi.PageAlignment;
        total += ((long)_meshIndices.Count * PhysxAbi.RecordSizes.MeshIndex) + PhysxAbi.PageAlignment;
        total += ((long)_articulations.Count * PhysxAbi.RecordSizes.ArticulationDesc) + PhysxAbi.PageAlignment;
        total += ((long)_articulationLinks.Count * PhysxAbi.RecordSizes.ArticulationLinkDesc) + PhysxAbi.PageAlignment;
        total += ((long)_controllers.Count * PhysxAbi.RecordSizes.ControllerDesc) + PhysxAbi.PageAlignment;
        total += ((long)_tendons.Count * PhysxAbi.RecordSizes.TendonDesc) + PhysxAbi.PageAlignment;
        total += ((long)_tendonNodes.Count * PhysxAbi.RecordSizes.TendonNodeDesc) + PhysxAbi.PageAlignment;
        total += ((long)_mimicJoints.Count * PhysxAbi.RecordSizes.MimicJointDesc) + PhysxAbi.PageAlignment;
        total += ((long)_vehicles.Count * PhysxAbi.RecordSizes.VehicleDesc) + PhysxAbi.PageAlignment;
        total += ((long)_vehicleWheels.Count * PhysxAbi.RecordSizes.VehicleWheelDesc) + PhysxAbi.PageAlignment;
        total += ((long)_particleMaterials.Count * PhysxAbi.RecordSizes.ParticleMaterialDesc) + PhysxAbi.PageAlignment;
        total += ((long)_particleSystems.Count * PhysxAbi.RecordSizes.ParticleSystemDesc) + PhysxAbi.PageAlignment;
        total += ((long)_particleBodies.Count * PhysxAbi.RecordSizes.ParticleBodyDesc) + PhysxAbi.PageAlignment;
        total += ((long)_deformableMaterials.Count * PhysxAbi.RecordSizes.DeformableMaterialDesc) +
            PhysxAbi.PageAlignment;
        total += ((long)_deformables.Count * PhysxAbi.RecordSizes.DeformableDesc) + PhysxAbi.PageAlignment;
        return (int)Math.Min(total, int.MaxValue);
    }

    private int Append<T>(List<T> target, in T value)
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int index = target.Count;
        target.Add(value);
        return index;
    }

    private static PhysxPageSpan WriteSection<T>(PhysxPooledBuffer buffer, ReadOnlySpan<T> values)
        where T : unmanaged
    {
        if (values.IsEmpty)
        {
            return default;
        }

        buffer.PadTo((int)PhysxAbi.PageAlignment);
        var span = new PhysxPageSpan((uint)buffer.Length, (uint)values.Length);
        buffer.WriteRange(values);
        return span;
    }
}
