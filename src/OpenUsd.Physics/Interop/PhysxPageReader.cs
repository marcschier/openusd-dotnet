// Copyright (c) marcschier. Licensed under the MIT License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenUsd.Physics.Interop;

/// <summary>
/// Reads a pointer-free build page as typed sections without copying record data.
/// </summary>
/// <remarks>
/// The reader assumes nothing: every accessor clamps to the span the caller supplied, so a
/// deliberately corrupt page produces empty sections instead of out-of-range reads. Callers that
/// need the full rule set run <see cref="PhysxPageValidator"/> first.
/// </remarks>
internal readonly ref struct PhysxPageReader
{
    private readonly ReadOnlySpan<byte> _page;
    private readonly PhysxBuildPageHeader _header;

    /// <summary>Initializes a reader over a complete build page.</summary>
    internal PhysxPageReader(ReadOnlySpan<byte> page)
    {
        _page = page;
        _header = page.Length >= PhysxAbi.RecordSizes.BuildPageHeader
            ? MemoryMarshal.Read<PhysxBuildPageHeader>(page)
            : default;
    }

    /// <summary>Gets the page header.</summary>
    internal PhysxBuildPageHeader Header => _header;

    /// <summary>Gets the raw page bytes.</summary>
    internal ReadOnlySpan<byte> Bytes => _page;

    /// <summary>Gets the UTF-8 string section.</summary>
    internal ReadOnlySpan<byte> Strings => Section(_header.StringBytes, 1);

    /// <summary>Gets the identity section.</summary>
    internal ReadOnlySpan<PhysxIdentityRecord> Identities => Cast<PhysxIdentityRecord>(_header.Identities);

    /// <summary>Gets the scene section.</summary>
    internal ReadOnlySpan<PhysxSceneDesc> Scenes => Cast<PhysxSceneDesc>(_header.Scenes);

    /// <summary>Gets the material section.</summary>
    internal ReadOnlySpan<PhysxMaterialDesc> Materials => Cast<PhysxMaterialDesc>(_header.Materials);

    /// <summary>Gets the shape section.</summary>
    internal ReadOnlySpan<PhysxShapeDesc> Shapes => Cast<PhysxShapeDesc>(_header.Shapes);

    /// <summary>Gets the actor section.</summary>
    internal ReadOnlySpan<PhysxActorDesc> Actors => Cast<PhysxActorDesc>(_header.Actors);

    /// <summary>Gets the actor-to-shape reference section.</summary>
    internal ReadOnlySpan<PhysxActorShapeRef> ActorShapes => Cast<PhysxActorShapeRef>(_header.ActorShapes);

    /// <summary>Gets the joint section.</summary>
    internal ReadOnlySpan<PhysxJointDesc> Joints => Cast<PhysxJointDesc>(_header.Joints);

    /// <summary>Gets the suppressed collision pair section.</summary>
    internal ReadOnlySpan<PhysxFilterPair> FilterPairs => Cast<PhysxFilterPair>(_header.FilterPairs);

    /// <summary>Gets the mesh point section.</summary>
    internal ReadOnlySpan<PhysxVec3f> MeshPoints => Cast<PhysxVec3f>(_header.MeshPoints);

    /// <summary>Gets the mesh index section.</summary>
    internal ReadOnlySpan<uint> MeshIndices => Cast<uint>(_header.MeshIndices);

    /// <summary>Gets the height field sample section.</summary>
    internal ReadOnlySpan<PhysxHeightfieldSample> HeightfieldSamples =>
        Cast<PhysxHeightfieldSample>(_header.HeightfieldSamples);

    /// <summary>Gets the articulation section.</summary>
    internal ReadOnlySpan<PhysxArticulationDesc> Articulations => Cast<PhysxArticulationDesc>(_header.Articulations);

    /// <summary>Gets the articulation link section.</summary>
    internal ReadOnlySpan<PhysxArticulationLinkDesc> ArticulationLinks =>
        Cast<PhysxArticulationLinkDesc>(_header.ArticulationLinks);

    /// <summary>Gets the controller section.</summary>
    internal ReadOnlySpan<PhysxControllerDesc> Controllers => Cast<PhysxControllerDesc>(_header.Controllers);

    /// <summary>Gets the articulation tendon section.</summary>
    internal ReadOnlySpan<PhysxTendonDesc> ArticulationTendons => Cast<PhysxTendonDesc>(_header.ArticulationTendons);

    /// <summary>Gets the articulation tendon node section.</summary>
    internal ReadOnlySpan<PhysxTendonNodeDesc> ArticulationTendonNodes =>
        Cast<PhysxTendonNodeDesc>(_header.ArticulationTendonNodes);

    /// <summary>Gets the articulation mimic joint section.</summary>
    internal ReadOnlySpan<PhysxMimicJointDesc> ArticulationMimicJoints =>
        Cast<PhysxMimicJointDesc>(_header.ArticulationMimicJoints);

    /// <summary>Gets the vehicle section.</summary>
    internal ReadOnlySpan<PhysxVehicleDesc> Vehicles => Cast<PhysxVehicleDesc>(_header.Vehicles);

    /// <summary>Gets the vehicle wheel section.</summary>
    internal ReadOnlySpan<PhysxVehicleWheelDesc> VehicleWheels =>
        Cast<PhysxVehicleWheelDesc>(_header.VehicleWheels);

    /// <summary>Gets the position based dynamics particle material section.</summary>
    internal ReadOnlySpan<PhysxParticleMaterialDesc> ParticleMaterials =>
        Cast<PhysxParticleMaterialDesc>(_header.ParticleMaterials);

    /// <summary>Gets the particle system section.</summary>
    internal ReadOnlySpan<PhysxParticleSystemDesc> ParticleSystems =>
        Cast<PhysxParticleSystemDesc>(_header.ParticleSystems);

    /// <summary>Gets the particle body section.</summary>
    internal ReadOnlySpan<PhysxParticleBodyDesc> ParticleBodies =>
        Cast<PhysxParticleBodyDesc>(_header.ParticleBodies);

    /// <summary>Gets the surface and volume deformable material section.</summary>
    internal ReadOnlySpan<PhysxDeformableMaterialDesc> DeformableMaterials =>
        Cast<PhysxDeformableMaterialDesc>(_header.DeformableMaterials);

    /// <summary>Gets the surface and volume deformable section.</summary>
    internal ReadOnlySpan<PhysxDeformableDesc> Deformables => Cast<PhysxDeformableDesc>(_header.Deformables);

    /// <summary>Gets the UTF-8 path bytes an identity record addresses.</summary>
    internal ReadOnlySpan<byte> GetPathBytes(in PhysxIdentityRecord identity)
    {
        ReadOnlySpan<byte> strings = Strings;
        if (identity.PathOffset > (uint)strings.Length ||
            identity.PathLength > (uint)strings.Length - identity.PathOffset)
        {
            return default;
        }
        return strings.Slice((int)identity.PathOffset, (int)identity.PathLength);
    }

    /// <summary>Decodes the prim path an identity record addresses.</summary>
    internal string GetPath(in PhysxIdentityRecord identity) => Encoding.UTF8.GetString(GetPathBytes(in identity));

    private ReadOnlySpan<byte> Section(PhysxPageSpan span, int stride)
    {
        if (span.Count == 0)
        {
            return default;
        }

        ulong bytes = (ulong)span.Count * (ulong)stride;
        if (span.Offset > (ulong)_page.Length || bytes > (ulong)_page.Length - span.Offset)
        {
            return default;
        }

        return _page.Slice((int)span.Offset, (int)bytes);
    }

    private ReadOnlySpan<T> Cast<T>(PhysxPageSpan span)
        where T : unmanaged =>
        MemoryMarshal.Cast<byte, T>(Section(span, Unsafe.SizeOf<T>()));
}
