// Copyright (c) marcschier. Licensed under the MIT License.

namespace OpenUsd.Physics.Interop;

/// <summary>
/// Mirrors the compile-time constants of the retained physics world C ABI, version 7.
/// </summary>
/// <remarks>
/// Every value is copied from <c>native/openusd_physx/include/openusd_physx_world.h</c> and from the
/// bounds the native page validator enforces in <c>native/openusd_physx/src/openusd_physx_page.cpp</c>.
/// The ABI is negotiated exactly: there is no forward or backward compatibility window, so a runtime
/// that reports a different version or a different record size is rejected rather than downgraded.
/// </remarks>
internal static class PhysxAbi
{
    /// <summary>The exact ABI version this managed mirror implements.</summary>
    internal const uint Version = 7u;

    /// <summary>The native library name resolved for every entry point.</summary>
    internal const string LibraryName = "openusd_physx";

    /// <summary>"USDPHYSX" in little endian byte order.</summary>
    internal const ulong PageMagic = 0x5853594850445355UL;

    /// <summary>The largest build page the ABI accepts, in bytes.</summary>
    internal const ulong PageMaxBytes = 0x40000000UL;

    /// <summary>The byte alignment required of the page start and of every section offset.</summary>
    internal const uint PageAlignment = 8u;

    /// <summary>The maximum number of scenes a single page may declare.</summary>
    internal const uint MaxScenes = 64u;

    /// <summary>The number of distinct collision groups; valid groups are zero to thirty one.</summary>
    internal const uint MaxCollisionGroups = 32u;

    /// <summary>The slowest supported fixed simulation rate, in hertz.</summary>
    internal const uint MinSimulationRateHz = 24u;

    /// <summary>The fastest supported fixed simulation rate, in hertz.</summary>
    internal const uint MaxSimulationRateHz = 240u;

    /// <summary>The maximum number of substeps one step call may advance.</summary>
    internal const uint MaxSubsteps = 64u;

    /// <summary>The maximum element count any declared result capacity may request.</summary>
    internal const uint MaxResultCapacity = 1048576u;

    /// <summary>The fixed byte length of a native diagnostic message, including its terminator.</summary>
    internal const int DiagnosticMessageBytes = 192;

    /// <summary>The reserved identity value that never addresses an object.</summary>
    internal const ulong InvalidId = 0UL;

    /// <summary>The maximum element count of a record section other than mesh data.</summary>
    internal const uint MaxRecords = 1u << 22;

    /// <summary>The maximum number of mesh points a page may declare.</summary>
    internal const uint MaxMeshPoints = 1u << 26;

    /// <summary>The maximum number of mesh indices a page may declare.</summary>
    internal const uint MaxMeshIndices = 1u << 27;

    /// <summary>The number of spans the build page header declares, in serialization order.</summary>
    internal const int PageSectionSpanCount = 25;

    /// <summary>The largest number of particles one particle body may declare.</summary>
    internal const uint MaxParticlesPerBody = 4194304u;

    /// <summary>The largest number of simulated vertices one deformable may declare.</summary>
    internal const uint MaxDeformableVertices = 1048576u;

    /// <summary>
    /// The largest particle collision group a page may declare.
    /// </summary>
    /// <remarks>
    /// A position based dynamics phase packs the collision group into the low twenty bits of a
    /// thirty two bit phase word and reserves the bits above it for behaviour flags, so twenty bits
    /// is the group space the solver itself has. Bounding the authored group is what lets the
    /// runtime pack a group, the per body behaviour flags, and the bound material index into one
    /// lookup key without two different bodies ever colliding on the same key and silently sharing
    /// a phase they never asked to share.
    /// </remarks>
    internal const uint MaxParticleGroup = 1048575u;

    /// <summary>The smallest particle neighbourhood budget a particle system may declare.</summary>
    internal const uint MinParticleNeighborhood = 8u;

    /// <summary>The largest particle neighbourhood budget a particle system may declare.</summary>
    internal const uint MaxParticleNeighborhood = 1024u;

    /// <summary>The number of axes a six degree of freedom joint describes.</summary>
    internal const int JointAxisCount = 6;

    /// <summary>
    /// The maximum number of wheels one vehicle may declare. This mirrors
    /// <c>OPENUSD_PHYSX_MAX_VEHICLE_WHEELS</c>, which itself mirrors the wheel budget of the
    /// simulation SDK, because every wheel response table the runtime fills is a fixed array of
    /// that length.
    /// </summary>
    internal const uint MaxVehicleWheels = 20u;

    /// <summary>
    /// The total gear budget one vehicle gearbox may declare, counting the reverse gear and the
    /// neutral gear, so a record may declare at most <c>MaxVehicleGears - 2</c> forward gears.
    /// </summary>
    internal const uint MaxVehicleGears = 32u;

    /// <summary>The declared size of every fixed-layout record, asserted against the native runtime.</summary>
    internal static class RecordSizes
    {
        /// <summary>Size of <see cref="PhysxTransform"/>.</summary>
        internal const int Transform = 28;

        /// <summary>Size of <see cref="PhysxPageSpan"/>.</summary>
        internal const int PageSpan = 8;

        /// <summary>Size of <see cref="PhysxVec3f"/>, which is the mesh point stride.</summary>
        internal const int Vec3f = 12;

        /// <summary>Size of one mesh index.</summary>
        internal const int MeshIndex = 4;

        /// <summary>Size of <see cref="PhysxResultCapacities"/>.</summary>
        internal const int ResultCapacities = 32;

        /// <summary>Size of <see cref="PhysxBuildPageHeader"/>.</summary>
        internal const int BuildPageHeader = 352;

        /// <summary>Size of <see cref="PhysxIdentityRecord"/>.</summary>
        internal const int Identity = 24;

        /// <summary>Size of <see cref="PhysxSceneDesc"/>.</summary>
        internal const int SceneDesc = 48;

        /// <summary>Size of <see cref="PhysxMaterialDesc"/>.</summary>
        internal const int MaterialDesc = 40;

        /// <summary>Size of <see cref="PhysxShapeDesc"/>.</summary>
        internal const int ShapeDesc = 144;

        /// <summary>Size of <see cref="PhysxActorDesc"/>.</summary>
        internal const int ActorDesc = 184;

        /// <summary>Size of <see cref="PhysxActorShapeRef"/>.</summary>
        internal const int ActorShapeRef = 8;

        /// <summary>Size of <see cref="PhysxJointDesc"/>.</summary>
        internal const int JointDesc = 408;

        /// <summary>Size of <see cref="PhysxFilterPair"/>.</summary>
        internal const int FilterPair = 8;

        /// <summary>Size of <see cref="PhysxCommand"/>.</summary>
        internal const int Command = 80;

        /// <summary>Size of <see cref="PhysxBodyState"/>.</summary>
        internal const int BodyState = 72;

        /// <summary>Size of <see cref="PhysxEventRecord"/>.</summary>
        internal const int Event = 80;

        /// <summary>Size of <see cref="PhysxDiagnosticRecord"/>.</summary>
        internal const int Diagnostic = 208;

        /// <summary>Size of <see cref="PhysxDebugLine"/>.</summary>
        internal const int DebugLine = 32;

        /// <summary>Size of <see cref="PhysxResultHeader"/>.</summary>
        internal const int ResultHeader = 88;

        /// <summary>Size of <see cref="PhysxQueryRequest"/>.</summary>
        internal const int QueryRequest = 96;

        /// <summary>Size of <see cref="PhysxQueryHit"/>.</summary>
        internal const int QueryHit = 64;

        /// <summary>Size of <see cref="PhysxHeightfieldSample"/>.</summary>
        internal const int HeightfieldSample = 4;

        /// <summary>Size of <see cref="PhysxArticulationDesc"/>.</summary>
        internal const int ArticulationDesc = 64;

        /// <summary>Size of <see cref="PhysxArticulationLinkDesc"/>.</summary>
        internal const int ArticulationLinkDesc = 432;

        /// <summary>Size of <see cref="PhysxControllerDesc"/>.</summary>
        internal const int ControllerDesc = 112;

        /// <summary>Size of <see cref="PhysxTendonDesc"/>.</summary>
        internal const int TendonDesc = 64;

        /// <summary>Size of <see cref="PhysxTendonNodeDesc"/>.</summary>
        internal const int TendonNodeDesc = 64;

        /// <summary>Size of <see cref="PhysxMimicJointDesc"/>.</summary>
        internal const int MimicJointDesc = 64;

        /// <summary>Size of <see cref="PhysxVehicleDesc"/>.</summary>
        internal const int VehicleDesc = 160;

        /// <summary>Size of <see cref="PhysxVehicleWheelDesc"/>.</summary>
        internal const int VehicleWheelDesc = 168;

        /// <summary>Size of <see cref="PhysxParticleMaterialDesc"/>.</summary>
        internal const int ParticleMaterialDesc = 72;

        /// <summary>Size of <see cref="PhysxParticleSystemDesc"/>.</summary>
        internal const int ParticleSystemDesc = 80;

        /// <summary>Size of <see cref="PhysxParticleBodyDesc"/>.</summary>
        internal const int ParticleBodyDesc = 72;

        /// <summary>Size of <see cref="PhysxDeformableMaterialDesc"/>.</summary>
        internal const int DeformableMaterialDesc = 56;

        /// <summary>Size of <see cref="PhysxDeformableDesc"/>.</summary>
        internal const int DeformableDesc = 128;

        /// <summary>Size of <see cref="PhysxDeformationState"/>.</summary>
        internal const int DeformationState = 32;
    }
}
