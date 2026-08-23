// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using System.Runtime.CompilerServices;

namespace OpenUsd.Physics.Interop;

/// <summary>
/// The outcome of validating a build page.
/// </summary>
internal readonly struct PhysxPageValidationResult
{
    private PhysxPageValidationResult(bool isValid, PhysxPageValidation validation, string? message)
    {
        IsValid = isValid;
        Validation = validation;
        Message = message;
    }

    /// <summary>Gets a value indicating whether the page satisfies every rule.</summary>
    internal bool IsValid { get; }

    /// <summary>Gets the native shaped validation record.</summary>
    internal PhysxPageValidation Validation { get; }

    /// <summary>Gets the failure message, or <see langword="null"/> when the page is valid.</summary>
    internal string? Message { get; }

    /// <summary>Gets the failure code.</summary>
    internal PhysxPageError ErrorCode => (PhysxPageError)Validation.ErrorCode;

    /// <summary>Gets the section the failure was found in.</summary>
    internal PhysxPageSection Section => (PhysxPageSection)Validation.Section;

    /// <summary>Creates a successful outcome.</summary>
    internal static PhysxPageValidationResult Success(PhysxPageValidation validation) =>
        new(true, validation, null);

    /// <summary>Creates a failed outcome.</summary>
    internal static PhysxPageValidationResult Failure(
        PhysxPageError code,
        PhysxPageSection section,
        uint elementIndex,
        ulong byteOffset,
        string message)
    {
        var validation = new PhysxPageValidation
        {
            StructSize = (uint)Unsafe.SizeOf<PhysxPageValidation>(),
            ErrorCode = (uint)code,
            Section = (uint)section,
            ElementIndex = elementIndex,
            ByteOffset = byteOffset
        };
        return new PhysxPageValidationResult(false, validation, message);
    }

    /// <summary>Throws when the page is invalid.</summary>
    internal void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException(Message);
        }
    }
}

/// <summary>
/// Validates a pointer-free build page against every rule the native validator enforces.
/// </summary>
/// <remarks>
/// The managed validator exists so a malformed page is rejected before it ever reaches the native
/// boundary, and so page construction can be tested without the native runtime present. It mirrors
/// <c>openusd_physx_page::Validate</c> rule for rule, in the same order, with the same error code,
/// section, element index, and byte offset, so a page accepted here is accepted natively.
/// </remarks>
internal static class PhysxPageValidator
{
    /// <summary>The byte offset of the result capacities inside the build page header.</summary>
    private const ulong HeaderCapacitiesOffset = 296;

    /// <summary>The byte offset of the reserved block inside the build page header.</summary>
    private const ulong HeaderReservedOffset = 328;

    private static readonly string[] SectionNames =
    [
        "header",
        "strings",
        "identities",
        "scenes",
        "materials",
        "shapes",
        "actors",
        "actor shapes",
        "joints",
        "filter pairs",
        "mesh points",
        "mesh indices",
        "capacities",
        "height field samples",
        "articulations",
        "articulation links",
        "controllers",
        "articulation tendons",
        "articulation tendon nodes",
        "articulation mimic joints",
        "vehicles",
        "vehicle wheels",
        "particle materials",
        "particle systems",
        "particle bodies",
        "deformable materials",
        "deformables"
    ];

    /// <summary>Validates a complete build page.</summary>
    /// <param name="page">The page bytes.</param>
    /// <param name="address">The address the page will be presented at, or zero when unknown.</param>
    internal static PhysxPageValidationResult Validate(ReadOnlySpan<byte> page, nuint address = 0)
    {
        if (page.IsEmpty)
        {
            return PhysxPageValidationResult.Failure(
                PhysxPageError.Null,
                PhysxPageSection.Header,
                0,
                0,
                "The build page is null.");
        }
        if (address != 0 && (address % PhysxAbi.PageAlignment) != 0)
        {
            return PhysxPageValidationResult.Failure(
                PhysxPageError.Alignment,
                PhysxPageSection.Header,
                0,
                0,
                "The build page must start on an eight byte boundary.");
        }
        if (page.Length < PhysxAbi.RecordSizes.BuildPageHeader || (ulong)page.Length > PhysxAbi.PageMaxBytes)
        {
            return PhysxPageValidationResult.Failure(
                PhysxPageError.Size,
                PhysxPageSection.Header,
                0,
                (ulong)page.Length,
                "The build page size is smaller than the header or larger than the supported maximum.");
        }

        var reader = new PhysxPageReader(page);
        PhysxBuildPageHeader header = reader.Header;

        PhysxPageValidationResult? failure =
            ValidateHeader(in header, (ulong)page.Length) ??
            ValidateSpans(in header) ??
            ValidateStrings(reader);
        if (failure is not null)
        {
            return failure.Value;
        }

        var identifiers = new HashSet<ulong>(reader.Identities.Length);
        failure =
            ValidateIdentities(reader, identifiers) ??
            ValidateScenes(reader, identifiers) ??
            ValidateMaterials(reader, identifiers) ??
            ValidateShapes(reader, identifiers) ??
            ValidateMeshPoints(reader) ??
            ValidateActorShapes(reader);
        if (failure is not null)
        {
            return failure.Value;
        }

        uint dynamicActorCount = 0;
        uint publishedWheelCount = 0;
        ulong deformationBodies = 0;
        ulong deformationPoints = 0;
        failure =
            ValidateActors(reader, identifiers, ref dynamicActorCount) ??
            ValidateJoints(reader, identifiers) ??
            ValidateFilterPairs(reader) ??
            ValidateArticulations(reader, identifiers) ??
            ValidateControllers(reader, identifiers) ??
            ValidateTendons(reader, identifiers) ??
            ValidateMimicJoints(reader, identifiers) ??
            ValidateVehicles(reader, identifiers, ref publishedWheelCount) ??
            ValidateParticleMaterials(reader, identifiers) ??
            ValidateParticleSystems(reader, identifiers) ??
            ValidateParticleBodies(reader, identifiers, ref deformationBodies, ref deformationPoints) ??
            ValidateDeformableMaterials(reader, identifiers) ??
            ValidateDeformables(reader, identifiers, ref deformationBodies, ref deformationPoints) ??
            ValidateGpuDomainReferences(reader) ??
            ValidateSimulatedBodyIdentitiesAreUnique(reader) ??
            ValidateCapacities(
                in header,
                dynamicActorCount,
                header.ArticulationLinks.Count,
                header.Controllers.Count,
                publishedWheelCount,
                deformationBodies,
                deformationPoints);
        if (failure is not null)
        {
            return failure.Value;
        }

        return PhysxPageValidationResult.Success(new PhysxPageValidation
        {
            StructSize = (uint)Unsafe.SizeOf<PhysxPageValidation>(),
            ErrorCode = (uint)PhysxPageError.None,
            Section = (uint)PhysxPageSection.Header,
            ElementIndex = 0,
            ByteOffset = 0,
            Revision = header.Revision,
            SourceHash = header.SourceHash,
            IdentityCount = header.Identities.Count,
            SceneCount = header.Scenes.Count,
            MaterialCount = header.Materials.Count,
            ShapeCount = header.Shapes.Count,
            ActorCount = header.Actors.Count,
            DynamicActorCount = dynamicActorCount,
            JointCount = header.Joints.Count,
            FilterPairCount = header.FilterPairs.Count,
            Capacities = header.Capacities
        });
    }

    private static PhysxPageValidationResult? ValidateHeader(in PhysxBuildPageHeader header, ulong pageSize)
    {
        if (header.Magic != PhysxAbi.PageMagic)
        {
            return Fail(PhysxPageError.Magic, PhysxPageSection.Header, 0, 0, "The build page magic is wrong.");
        }
        if (header.AbiVersion != PhysxAbi.Version)
        {
            return Fail(
                PhysxPageError.Abi,
                PhysxPageSection.Header,
                header.AbiVersion,
                8,
                "The build page requires an exact ABI version match.");
        }
        if (header.HeaderSize != PhysxAbi.RecordSizes.BuildPageHeader)
        {
            return Fail(
                PhysxPageError.HeaderSize,
                PhysxPageSection.Header,
                header.HeaderSize,
                12,
                "The build page header size does not match this ABI.");
        }
        if (header.ByteSize != pageSize)
        {
            return Fail(
                PhysxPageError.Size,
                PhysxPageSection.Header,
                0,
                header.ByteSize,
                "The build page declares a byte size that differs from the supplied buffer size.");
        }
        if (!IsPositiveFinite(header.MetersPerUnit) || !IsPositiveFinite(header.KilogramsPerUnit))
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.Header,
                0,
                40,
                "The build page requires positive finite unit scales.");
        }
        if (!IsPositiveFinite(header.TimeCodesPerSecond) ||
            !double.IsFinite(header.StartTimeCode) ||
            !double.IsFinite(header.EndTimeCode) ||
            header.EndTimeCode < header.StartTimeCode)
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.Header,
                0,
                56,
                "The build page requires a positive time code rate and an ordered finite time range.");
        }
        if (header.UpAxis >= 3)
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.Header,
                header.UpAxis,
                80,
                "The build page up axis is out of range.");
        }
        if (header.SimulationRateHz < PhysxAbi.MinSimulationRateHz ||
            header.SimulationRateHz > PhysxAbi.MaxSimulationRateHz)
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.Header,
                header.SimulationRateHz,
                88,
                "The build page simulation rate must be between 24 and 240 hertz.");
        }
        if (header.MaxSubsteps == 0 || header.MaxSubsteps > PhysxAbi.MaxSubsteps)
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.Header,
                header.MaxSubsteps,
                92,
                "The build page substep limit must be between one and sixty four.");
        }

        ReadOnlySpan<ulong> reserved =
        [
            header.Reserved0,
            header.Reserved1,
            header.Reserved2
        ];
        for (uint index = 0; index < reserved.Length; index++)
        {
            if (reserved[(int)index] != 0)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Header,
                    index,
                    HeaderReservedOffset,
                    "Reserved build page header fields must be zero.");
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateSpans(in PhysxBuildPageHeader header)
    {
        ulong pageBytes = header.ByteSize;
        uint headerSize = header.HeaderSize;
        Span<(ulong Offset, ulong Bytes, PhysxPageSection Section)> ranges =
            new (ulong, ulong, PhysxPageSection)[PhysxAbi.PageSectionSpanCount];
        int rangeCount = 0;

        ReadOnlySpan<(PhysxPageSpan Span, int Stride, uint MaxCount, PhysxPageSection Section)> rules =
        [
            (header.StringBytes, 1, (uint)(PhysxAbi.PageMaxBytes - 1), PhysxPageSection.Strings),
            (header.Identities, PhysxAbi.RecordSizes.Identity, PhysxAbi.MaxRecords, PhysxPageSection.Identities),
            (header.Scenes, PhysxAbi.RecordSizes.SceneDesc, PhysxAbi.MaxScenes, PhysxPageSection.Scenes),
            (header.Materials, PhysxAbi.RecordSizes.MaterialDesc, PhysxAbi.MaxRecords, PhysxPageSection.Materials),
            (header.Shapes, PhysxAbi.RecordSizes.ShapeDesc, PhysxAbi.MaxRecords, PhysxPageSection.Shapes),
            (header.Actors, PhysxAbi.RecordSizes.ActorDesc, PhysxAbi.MaxRecords, PhysxPageSection.Actors),
            (header.ActorShapes, PhysxAbi.RecordSizes.ActorShapeRef, PhysxAbi.MaxRecords, PhysxPageSection.ActorShapes),
            (header.Joints, PhysxAbi.RecordSizes.JointDesc, PhysxAbi.MaxRecords, PhysxPageSection.Joints),
            (header.FilterPairs, PhysxAbi.RecordSizes.FilterPair, PhysxAbi.MaxRecords, PhysxPageSection.FilterPairs),
            (header.MeshPoints, PhysxAbi.RecordSizes.Vec3f, PhysxAbi.MaxMeshPoints, PhysxPageSection.MeshPoints),
            (header.MeshIndices, PhysxAbi.RecordSizes.MeshIndex, PhysxAbi.MaxMeshIndices, PhysxPageSection.MeshIndices),
            (header.HeightfieldSamples, PhysxAbi.RecordSizes.HeightfieldSample, PhysxAbi.MaxMeshPoints,
                PhysxPageSection.HeightfieldSamples),
            (header.Articulations, PhysxAbi.RecordSizes.ArticulationDesc, PhysxAbi.MaxRecords,
                PhysxPageSection.Articulations),
            (header.ArticulationLinks, PhysxAbi.RecordSizes.ArticulationLinkDesc, PhysxAbi.MaxRecords,
                PhysxPageSection.ArticulationLinks),
            (header.Controllers, PhysxAbi.RecordSizes.ControllerDesc, PhysxAbi.MaxRecords,
                PhysxPageSection.Controllers),
            (header.ArticulationTendons, PhysxAbi.RecordSizes.TendonDesc, PhysxAbi.MaxRecords,
                PhysxPageSection.ArticulationTendons),
            (header.ArticulationTendonNodes, PhysxAbi.RecordSizes.TendonNodeDesc, PhysxAbi.MaxRecords,
                PhysxPageSection.ArticulationTendonNodes),
            (header.ArticulationMimicJoints, PhysxAbi.RecordSizes.MimicJointDesc, PhysxAbi.MaxRecords,
                PhysxPageSection.ArticulationMimicJoints),
            (header.Vehicles, PhysxAbi.RecordSizes.VehicleDesc, PhysxAbi.MaxRecords, PhysxPageSection.Vehicles),
            (header.VehicleWheels, PhysxAbi.RecordSizes.VehicleWheelDesc, PhysxAbi.MaxRecords,
                PhysxPageSection.VehicleWheels),
            (header.ParticleMaterials, PhysxAbi.RecordSizes.ParticleMaterialDesc, PhysxAbi.MaxRecords,
                PhysxPageSection.ParticleMaterials),
            (header.ParticleSystems, PhysxAbi.RecordSizes.ParticleSystemDesc, PhysxAbi.MaxRecords,
                PhysxPageSection.ParticleSystems),
            (header.ParticleBodies, PhysxAbi.RecordSizes.ParticleBodyDesc, PhysxAbi.MaxRecords,
                PhysxPageSection.ParticleBodies),
            (header.DeformableMaterials, PhysxAbi.RecordSizes.DeformableMaterialDesc, PhysxAbi.MaxRecords,
                PhysxPageSection.DeformableMaterials),
            (header.Deformables, PhysxAbi.RecordSizes.DeformableDesc, PhysxAbi.MaxRecords,
                PhysxPageSection.Deformables)
        ];

        foreach ((PhysxPageSpan span, int stride, uint maxCount, PhysxPageSection section) in rules)
        {
            PhysxPageValidationResult? failure = CheckSpan(
                span,
                stride,
                maxCount,
                section,
                pageBytes,
                headerSize,
                out ulong bytes);
            if (failure is not null)
            {
                return failure;
            }
            if (bytes != 0)
            {
                ranges[rangeCount++] = (span.Offset, bytes, section);
            }
        }

        ranges = ranges[..rangeCount];
        ranges.Sort(static (first, second) => first.Offset.CompareTo(second.Offset));
        for (int index = 1; index < ranges.Length; index++)
        {
            (ulong previousOffset, ulong previousBytes, _) = ranges[index - 1];
            (ulong offset, _, PhysxPageSection section) = ranges[index];
            if (previousOffset + previousBytes > offset)
            {
                return Fail(PhysxPageError.Overlap, section, 0, offset, "Two build page sections overlap.");
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? CheckSpan(
        PhysxPageSpan span,
        int stride,
        uint maxCount,
        PhysxPageSection section,
        ulong pageBytes,
        uint headerSize,
        out ulong bytes)
    {
        bytes = 0;
        if (span.Count == 0)
        {
            if (span.Offset != 0)
            {
                return Fail(
                    PhysxPageError.Range,
                    section,
                    0,
                    span.Offset,
                    "An empty page section must declare a zero byte offset.");
            }
            return null;
        }
        if (span.Count > maxCount)
        {
            return Fail(
                PhysxPageError.CountLimit,
                section,
                span.Count,
                span.Offset,
                "A page section exceeds the supported element count.");
        }
        if ((span.Offset % PhysxAbi.PageAlignment) != 0)
        {
            return Fail(
                PhysxPageError.Alignment,
                section,
                0,
                span.Offset,
                "A page section offset is not eight byte aligned.");
        }
        if (span.Offset < headerSize)
        {
            return Fail(PhysxPageError.Range, section, 0, span.Offset, "A page section overlaps the page header.");
        }

        ulong total = (ulong)span.Count * (ulong)stride;
        if (total > pageBytes || span.Offset + total > pageBytes)
        {
            return Fail(
                PhysxPageError.Range,
                section,
                span.Count,
                span.Offset,
                "A page section extends past the end of the page.");
        }

        bytes = total;
        return null;
    }

    private static PhysxPageValidationResult? ValidateStrings(PhysxPageReader reader)
    {
        ReadOnlySpan<byte> strings = reader.Strings;
        if (!strings.IsEmpty && !PhysxUtf8.IsValid(strings))
        {
            return Fail(
                PhysxPageError.Encoding,
                PhysxPageSection.Strings,
                0,
                reader.Header.StringBytes.Offset,
                "The build page string section is not valid UTF-8 without embedded null bytes.");
        }
        return null;
    }

    private static PhysxPageValidationResult? ValidateIdentities(PhysxPageReader reader, HashSet<ulong> identifiers)
    {
        ReadOnlySpan<PhysxIdentityRecord> identities = reader.Identities;
        uint stringCount = reader.Header.StringBytes.Count;
        for (uint index = 0; index < identities.Length; index++)
        {
            PhysxIdentityRecord identity = identities[(int)index];
            if (identity.Id == PhysxAbi.InvalidId)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Identities,
                    index,
                    0,
                    Describe(PhysxPageSection.Identities, index) + " uses the reserved zero identity.");
            }
            if (identity.InstanceDomain >= (uint)PhysxInstanceDomain.Count)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Identities,
                    index,
                    0,
                    Describe(PhysxPageSection.Identities, index) + " declares an unknown instance domain.");
            }
            if (identity.PathLength == 0 || (ulong)identity.PathOffset + identity.PathLength > stringCount)
            {
                return Fail(
                    PhysxPageError.Range,
                    PhysxPageSection.Identities,
                    index,
                    identity.PathOffset,
                    Describe(PhysxPageSection.Identities, index) + " references a path outside the string section.");
            }

            ReadOnlySpan<byte> path = reader.GetPathBytes(in identity);
            if (path.IsEmpty || path[0] != (byte)'/')
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Identities,
                    index,
                    identity.PathOffset,
                    Describe(PhysxPageSection.Identities, index) + " does not reference an absolute prim path.");
            }
            if (!PhysxIdentity.TryCompute(
                    path,
                    (PhysxInstanceDomain)identity.InstanceDomain,
                    identity.InstanceIndex,
                    out ulong expected,
                    out _) ||
                identity.Id != expected)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Identities,
                    index,
                    0,
                    Describe(PhysxPageSection.Identities, index) +
                        " is not derived from its path, instance domain, and instance index.");
            }
            if (!identifiers.Add(identity.Id))
            {
                return Fail(
                    PhysxPageError.DuplicateId,
                    PhysxPageSection.Identities,
                    index,
                    0,
                    Describe(PhysxPageSection.Identities, index) + " collides with an earlier identity.");
            }
        }

        return null;
    }

    /// <summary>
    /// Rejects a page that addresses two simulated bodies with one identity.
    /// </summary>
    /// <remarks>
    /// An actor and an articulation link are both published as a body state keyed by identity, and
    /// both are resolved from that identity when a command arrives. Two of them sharing an identity
    /// therefore gives the world two bodies at one address: whichever the command map happened to
    /// keep receives every command, while both publish a pose for the same prim. Overlapping
    /// articulation roots are the way that happens in practice, and the composer refuses them, but
    /// the page is the contract the world builds from and it must not depend on the composer having
    /// got it right.
    /// </remarks>
    private static PhysxPageValidationResult? ValidateSimulatedBodyIdentitiesAreUnique(
        PhysxPageReader reader)
    {
        ReadOnlySpan<PhysxActorDesc> actors = reader.Actors;
        ReadOnlySpan<PhysxArticulationLinkDesc> links = reader.ArticulationLinks;
        var seen = new HashSet<ulong>(actors.Length + links.Length);

        for (uint index = 0; index < actors.Length; index++)
        {
            if (!seen.Add(actors[(int)index].Id))
            {
                return Fail(
                    PhysxPageError.DuplicateId,
                    PhysxPageSection.Actors,
                    index,
                    0,
                    Describe(PhysxPageSection.Actors, index) +
                        " declares an identity another simulated body already declares.");
            }
        }

        for (uint index = 0; index < links.Length; index++)
        {
            if (!seen.Add(links[(int)index].Id))
            {
                return Fail(
                    PhysxPageError.DuplicateId,
                    PhysxPageSection.ArticulationLinks,
                    index,
                    0,
                    Describe(PhysxPageSection.ArticulationLinks, index) +
                        " declares an identity another simulated body already declares.");
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateScenes(PhysxPageReader reader, HashSet<ulong> identifiers)
    {
        ReadOnlySpan<PhysxSceneDesc> scenes = reader.Scenes;
        for (uint index = 0; index < scenes.Length; index++)
        {
            PhysxSceneDesc scene = scenes[(int)index];
            PhysxPageValidationResult? missing =
                RequireIdentity(identifiers, scene.Id, PhysxPageSection.Scenes, index);
            if (missing is not null)
            {
                return missing;
            }
            if (!scene.GravityDirection.IsFinite ||
                !IsNonNegativeFinite(scene.GravityMagnitude) ||
                !IsNonNegativeFinite(scene.BounceThreshold) ||
                !IsPositiveFinite(scene.ContactOffset))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Scenes,
                    index,
                    0,
                    Describe(PhysxPageSection.Scenes, index) +
                        " declares a non finite or negative simulation value.");
            }
            if (scene.GravityMagnitude > 0.0F && scene.GravityDirection.IsZero)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Scenes,
                    index,
                    0,
                    Describe(PhysxPageSection.Scenes, index) + " declares gravity without a direction.");
            }
            if (scene.PositionIterations is 0 or > 255 || scene.VelocityIterations is 0 or > 255)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Scenes,
                    index,
                    0,
                    Describe(PhysxPageSection.Scenes, index) +
                        " declares solver iteration counts outside one to two hundred fifty five.");
            }
            if (scene.Reserved0 != 0 || (scene.Flags & ~(uint)PhysxSceneFlags.All) != 0)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Scenes,
                    index,
                    0,
                    Describe(PhysxPageSection.Scenes, index) +
                        " declares unknown flags or must leave reserved fields zero.");
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateMaterials(PhysxPageReader reader, HashSet<ulong> identifiers)
    {
        ReadOnlySpan<PhysxMaterialDesc> materials = reader.Materials;
        for (uint index = 0; index < materials.Length; index++)
        {
            PhysxMaterialDesc material = materials[(int)index];
            PhysxPageValidationResult? missing =
                RequireIdentity(identifiers, material.Id, PhysxPageSection.Materials, index);
            if (missing is not null)
            {
                return missing;
            }
            if (!IsNonNegativeFinite(material.StaticFriction) ||
                !IsNonNegativeFinite(material.DynamicFriction) ||
                !IsNonNegativeFinite(material.Restitution) ||
                material.Restitution > 1.0F ||
                !IsPositiveFinite(material.Density))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Materials,
                    index,
                    0,
                    Describe(PhysxPageSection.Materials, index) +
                        " declares friction, restitution, or density outside the supported range.");
            }
            if ((material.Flags & ~(uint)PhysxMaterialFlags.All) != 0)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Materials,
                    index,
                    0,
                    Describe(PhysxPageSection.Materials, index) + " declares unknown flags.");
            }
            if (material.FrictionCombineMode >= (uint)PhysxCombineMode.Count ||
                material.RestitutionCombineMode >= (uint)PhysxCombineMode.Count)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Materials,
                    index,
                    0,
                    Describe(PhysxPageSection.Materials, index) +
                        " declares an unknown friction or restitution combine mode.");
            }
            if (!IsNonNegativeFinite(material.Damping))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Materials,
                    index,
                    0,
                    Describe(PhysxPageSection.Materials, index) +
                        " declares a non finite or negative contact damping.");
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateShapes(PhysxPageReader reader, HashSet<ulong> identifiers)
    {
        ReadOnlySpan<PhysxShapeDesc> shapes = reader.Shapes;
        ReadOnlySpan<uint> meshIndices = reader.MeshIndices;
        uint materialCount = reader.Header.Materials.Count;
        uint meshPointCount = reader.Header.MeshPoints.Count;
        uint meshIndexCount = reader.Header.MeshIndices.Count;
        uint heightfieldSampleCount = reader.Header.HeightfieldSamples.Count;

        for (uint index = 0; index < shapes.Length; index++)
        {
            PhysxShapeDesc shape = shapes[(int)index];
            PhysxPageValidationResult? missing =
                RequireIdentity(identifiers, shape.Id, PhysxPageSection.Shapes, index);
            if (missing is not null)
            {
                return missing;
            }
            if (shape.Type >= (uint)PhysxShapeType.Count ||
                (shape.Flags & ~(uint)PhysxShapeFlags.All) != 0 ||
                shape.Reserved0 != 0 ||
                shape.Reserved1 != 0)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Shapes,
                    index,
                    0,
                    Describe(PhysxPageSection.Shapes, index) +
                        " declares an unknown type, unknown flags, or non zero reserved fields.");
            }
            if (!shape.LocalPose.IsFinite ||
                !shape.LocalPose.Rotation.IsUsableRotation ||
                !IsPositiveFinite(shape.Scale.X) ||
                !IsPositiveFinite(shape.Scale.Y) ||
                !IsPositiveFinite(shape.Scale.Z))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Shapes,
                    index,
                    0,
                    Describe(PhysxPageSection.Shapes, index) +
                        " declares a non finite local pose or a non positive scale.");
            }
            if (shape.MaterialIndex < -1 || shape.MaterialIndex >= (int)materialCount)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.Shapes,
                    index,
                    0,
                    Describe(PhysxPageSection.Shapes, index) + " references a material that does not exist.");
            }

            var type = (PhysxShapeType)shape.Type;
            bool isMesh = type is PhysxShapeType.ConvexMesh or PhysxShapeType.TriangleMesh;
            if (!isMesh &&
                (shape.PointCount != 0 || shape.IndexCount != 0 || shape.PointOffset != 0 || shape.IndexOffset != 0))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Shapes,
                    index,
                    0,
                    Describe(PhysxPageSection.Shapes, index) +
                        " is an analytic shape and must not reference mesh data.");
            }
            if (!IsNonNegativeFinite(shape.ContactOffset) ||
                !IsNonNegativeFinite(shape.RestOffset) ||
                !IsNonNegativeFinite(shape.TorsionalPatchRadius) ||
                !IsNonNegativeFinite(shape.MinTorsionalPatchRadius))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Shapes,
                    index,
                    0,
                    Describe(PhysxPageSection.Shapes, index) +
                        " declares a non finite or negative contact, rest, or torsional patch offset.");
            }
            if (shape.ContactOffset > 0.0F && shape.RestOffset >= shape.ContactOffset)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Shapes,
                    index,
                    0,
                    Describe(PhysxPageSection.Shapes, index) +
                        " declares a rest offset that is not below its contact offset.");
            }
            if (type != PhysxShapeType.Heightfield &&
                (shape.SampleOffset != 0 ||
                 shape.RowCount != 0 ||
                 shape.ColumnCount != 0 ||
                 shape.HeightScale != 0.0F ||
                 shape.RowScale != 0.0F ||
                 shape.ColumnScale != 0.0F))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Shapes,
                    index,
                    0,
                    Describe(PhysxPageSection.Shapes, index) +
                        " is not a height field and must leave the height field block zero.");
            }
            switch (type)
            {
                case PhysxShapeType.Sphere:
                    if (!IsPositiveFinite(shape.Radius))
                    {
                        return Fail(
                            PhysxPageError.Value,
                            PhysxPageSection.Shapes,
                            index,
                            0,
                            Describe(PhysxPageSection.Shapes, index) + " requires a positive radius.");
                    }
                    break;
                case PhysxShapeType.Box:
                    if (!IsPositiveFinite(shape.HalfExtents.X) ||
                        !IsPositiveFinite(shape.HalfExtents.Y) ||
                        !IsPositiveFinite(shape.HalfExtents.Z))
                    {
                        return Fail(
                            PhysxPageError.Value,
                            PhysxPageSection.Shapes,
                            index,
                            0,
                            Describe(PhysxPageSection.Shapes, index) + " requires positive half extents.");
                    }
                    break;
                case PhysxShapeType.Capsule:
                case PhysxShapeType.Cylinder:
                case PhysxShapeType.Cone:
                    if (!IsPositiveFinite(shape.Radius) || !IsPositiveFinite(shape.HalfHeight))
                    {
                        return Fail(
                            PhysxPageError.Value,
                            PhysxPageSection.Shapes,
                            index,
                            0,
                            Describe(PhysxPageSection.Shapes, index) +
                                " requires a positive radius and half height.");
                    }
                    break;
                case PhysxShapeType.Heightfield:
                    if (shape.RowCount < 2 || shape.ColumnCount < 2)
                    {
                        return Fail(
                            PhysxPageError.Value,
                            PhysxPageSection.Shapes,
                            index,
                            0,
                            Describe(PhysxPageSection.Shapes, index) +
                                " requires at least two height field rows and columns.");
                    }
                    if (shape.SampleOffset + ((ulong)shape.RowCount * shape.ColumnCount) > heightfieldSampleCount)
                    {
                        return Fail(
                            PhysxPageError.Range,
                            PhysxPageSection.Shapes,
                            index,
                            shape.SampleOffset,
                            Describe(PhysxPageSection.Shapes, index) +
                                " references height field samples outside the height field sample section.");
                    }
                    if (!IsPositiveFinite(shape.HeightScale) ||
                        !IsPositiveFinite(shape.RowScale) ||
                        !IsPositiveFinite(shape.ColumnScale))
                    {
                        return Fail(
                            PhysxPageError.Value,
                            PhysxPageSection.Shapes,
                            index,
                            0,
                            Describe(PhysxPageSection.Shapes, index) +
                                " requires positive height, row, and column scales.");
                    }
                    break;
                case PhysxShapeType.Plane:
                    break;
                default:
                    uint minimumPoints = type == PhysxShapeType.ConvexMesh ? 4u : 3u;
                    if (shape.PointCount < minimumPoints ||
                        (ulong)shape.PointOffset + shape.PointCount > meshPointCount)
                    {
                        return Fail(
                            PhysxPageError.Range,
                            PhysxPageSection.Shapes,
                            index,
                            shape.PointOffset,
                            Describe(PhysxPageSection.Shapes, index) +
                                " references mesh points outside the mesh point section.");
                    }
                    if ((ulong)shape.IndexOffset + shape.IndexCount > meshIndexCount)
                    {
                        return Fail(
                            PhysxPageError.Range,
                            PhysxPageSection.Shapes,
                            index,
                            shape.IndexOffset,
                            Describe(PhysxPageSection.Shapes, index) +
                                " references mesh indices outside the mesh index section.");
                    }
                    if (type == PhysxShapeType.TriangleMesh && (shape.IndexCount < 3 || (shape.IndexCount % 3) != 0))
                    {
                        return Fail(
                            PhysxPageError.Value,
                            PhysxPageSection.Shapes,
                            index,
                            0,
                            Describe(PhysxPageSection.Shapes, index) +
                                " requires a positive triangle index count that is a multiple of three.");
                    }
                    for (uint offset = 0; offset < shape.IndexCount; offset++)
                    {
                        uint value = meshIndices[(int)(shape.IndexOffset + offset)];
                        if (value >= shape.PointCount)
                        {
                            return Fail(
                                PhysxPageError.Reference,
                                PhysxPageSection.MeshIndices,
                                shape.IndexOffset + offset,
                                0,
                                Describe(PhysxPageSection.Shapes, index) +
                                    " references a vertex index outside its own point range.");
                        }
                    }
                    break;
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateMeshPoints(PhysxPageReader reader)
    {
        ReadOnlySpan<PhysxVec3f> points = reader.MeshPoints;
        for (uint index = 0; index < points.Length; index++)
        {
            if (!points[(int)index].IsFinite)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.MeshPoints,
                    index,
                    0,
                    Describe(PhysxPageSection.MeshPoints, index) + " is not finite.");
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateActorShapes(PhysxPageReader reader)
    {
        ReadOnlySpan<PhysxActorShapeRef> references = reader.ActorShapes;
        uint shapeCount = reader.Header.Shapes.Count;
        uint materialCount = reader.Header.Materials.Count;
        for (uint index = 0; index < references.Length; index++)
        {
            PhysxActorShapeRef reference = references[(int)index];
            if (reference.ShapeIndex >= shapeCount)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.ActorShapes,
                    index,
                    0,
                    Describe(PhysxPageSection.ActorShapes, index) + " references a shape that does not exist.");
            }
            if (reference.MaterialIndex < -1 || reference.MaterialIndex >= (int)materialCount)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.ActorShapes,
                    index,
                    0,
                    Describe(PhysxPageSection.ActorShapes, index) + " references a material that does not exist.");
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateActors(
        PhysxPageReader reader,
        HashSet<ulong> identifiers,
        ref uint dynamicActorCount)
    {
        ReadOnlySpan<PhysxActorDesc> actors = reader.Actors;
        ReadOnlySpan<PhysxActorShapeRef> actorShapes = reader.ActorShapes;
        ReadOnlySpan<PhysxShapeDesc> shapes = reader.Shapes;
        uint sceneCount = reader.Header.Scenes.Count;
        uint actorShapeCount = reader.Header.ActorShapes.Count;

        for (uint index = 0; index < actors.Length; index++)
        {
            PhysxActorDesc actor = actors[(int)index];
            PhysxPageValidationResult? missing =
                RequireIdentity(identifiers, actor.Id, PhysxPageSection.Actors, index);
            if (missing is not null)
            {
                return missing;
            }
            if (actor.Type >= (uint)PhysxActorType.Count ||
                (actor.Flags & ~(uint)PhysxActorFlags.All) != 0 ||
                actor.Reserved0 != 0)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Actors,
                    index,
                    0,
                    Describe(PhysxPageSection.Actors, index) +
                        " declares an unknown type, unknown flags, or non zero reserved fields.");
            }
            if (actor.SceneIndex < 0 || actor.SceneIndex >= (int)sceneCount)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.Actors,
                    index,
                    0,
                    Describe(PhysxPageSection.Actors, index) + " is not owned by a scene in this page.");
            }
            if (!actor.WorldPose.IsFinite ||
                !actor.WorldPose.Rotation.IsUsableRotation ||
                !actor.LinearVelocity.IsFinite ||
                !actor.AngularVelocity.IsFinite ||
                !actor.CenterOfMass.IsFinite ||
                !actor.Inertia.IsFinite ||
                !IsUnsetOrUsableRotation(actor.PrincipalAxes) ||
                !IsNonNegativeFinite(actor.Mass) ||
                !IsNonNegativeFinite(actor.LinearDamping) ||
                !IsNonNegativeFinite(actor.AngularDamping) ||
                !IsNonNegativeFinite(actor.Inertia.X) ||
                !IsNonNegativeFinite(actor.Inertia.Y) ||
                !IsNonNegativeFinite(actor.Inertia.Z))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Actors,
                    index,
                    0,
                    Describe(PhysxPageSection.Actors, index) +
                        " declares a non finite or negative rigid body value.");
            }
            if (actor.ShapeCount == 0 || (ulong)actor.ShapeOffset + actor.ShapeCount > actorShapeCount)
            {
                return Fail(
                    PhysxPageError.Range,
                    PhysxPageSection.Actors,
                    index,
                    actor.ShapeOffset,
                    Describe(PhysxPageSection.Actors, index) +
                        " must reference at least one shape inside the actor shape section.");
            }
            if (actor.CollisionGroup >= PhysxAbi.MaxCollisionGroups)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Actors,
                    index,
                    0,
                    Describe(PhysxPageSection.Actors, index) +
                        " uses a collision group outside zero to thirty one.");
            }
            if (actor.PositionIterations > 255 || actor.VelocityIterations > 255)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Actors,
                    index,
                    0,
                    Describe(PhysxPageSection.Actors, index) +
                        " declares more solver iterations than the world supports.");
            }
            if (!IsNonNegativeFinite(actor.MaxLinearVelocity) ||
                !IsNonNegativeFinite(actor.MaxAngularVelocity) ||
                !IsNonNegativeFinite(actor.MaxDepenetrationVelocity) ||
                !IsNonNegativeFinite(actor.MaxContactImpulse) ||
                !IsNonNegativeFinite(actor.StabilizationThreshold) ||
                !IsNonNegativeFinite(actor.WakeCounter) ||
                !IsNonNegativeFinite(actor.SleepThreshold))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Actors,
                    index,
                    0,
                    Describe(PhysxPageSection.Actors, index) +
                        " declares a non finite or negative velocity, impulse, sleep, or wake budget.");
            }
            if (!IsNonNegativeFinite(actor.MinCcdAdvanceCoefficient) ||
                actor.MinCcdAdvanceCoefficient > 1.0F ||
                !IsNonNegativeFinite(actor.ContactSlopCoefficient))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Actors,
                    index,
                    0,
                    Describe(PhysxPageSection.Actors, index) +
                        " declares a continuous collision advance or contact slop coefficient " +
                        "outside the supported range.");
            }
            if (actor.Type == (uint)PhysxActorType.Static)
            {
                if (!actor.LinearVelocity.IsZero || !actor.AngularVelocity.IsZero)
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.Actors,
                        index,
                        0,
                        Describe(PhysxPageSection.Actors, index) + " is static and must not declare velocities.");
                }
                continue;
            }

            dynamicActorCount++;
            for (uint offset = 0; offset < actor.ShapeCount; offset++)
            {
                PhysxActorShapeRef reference = actorShapes[(int)(actor.ShapeOffset + offset)];
                var shapeType = (PhysxShapeType)shapes[(int)reference.ShapeIndex].Type;
                if (shapeType is PhysxShapeType.Plane or PhysxShapeType.TriangleMesh or PhysxShapeType.Heightfield)
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.Actors,
                        index,
                        0,
                        Describe(PhysxPageSection.Actors, index) +
                            " is movable and cannot use a plane, triangle mesh, or height field shape.");
                }
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateJoints(PhysxPageReader reader, HashSet<ulong> identifiers)
    {
        ReadOnlySpan<PhysxJointDesc> joints = reader.Joints;
        uint actorCount = reader.Header.Actors.Count;
        for (uint index = 0; index < joints.Length; index++)
        {
            PhysxJointDesc joint = joints[(int)index];
            PhysxPageValidationResult? missing =
                RequireIdentity(identifiers, joint.Id, PhysxPageSection.Joints, index);
            if (missing is not null)
            {
                return missing;
            }
            if (joint.Type >= (uint)PhysxJointType.Count ||
                (joint.Flags & ~(uint)PhysxJointFlags.All) != 0 ||
                joint.Axis >= 3 ||
                joint.Reserved0 != 0 ||
                joint.Reserved1 != 0 ||
                joint.Reserved2 != 0 ||
                joint.Reserved3 != 0)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Joints,
                    index,
                    0,
                    Describe(PhysxPageSection.Joints, index) +
                        " declares an unknown type, axis, flags, or non zero reserved fields.");
            }
            if (joint.Actor0Index < -1 ||
                joint.Actor0Index >= (int)actorCount ||
                joint.Actor1Index < -1 ||
                joint.Actor1Index >= (int)actorCount ||
                (joint.Actor0Index < 0 && joint.Actor1Index < 0) ||
                (joint.Actor0Index >= 0 && joint.Actor0Index == joint.Actor1Index))
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.Joints,
                    index,
                    0,
                    Describe(PhysxPageSection.Joints, index) +
                        " must reference one or two distinct actors from this page.");
            }
            if (!joint.LocalFrame0.IsFinite ||
                !joint.LocalFrame0.Rotation.IsUsableRotation ||
                !joint.LocalFrame1.IsFinite ||
                !joint.LocalFrame1.Rotation.IsUsableRotation)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Joints,
                    index,
                    0,
                    Describe(PhysxPageSection.Joints, index) + " declares a non finite joint frame.");
            }
            if (!float.IsFinite(joint.LowerLimit) ||
                !float.IsFinite(joint.UpperLimit) ||
                joint.LowerLimit > joint.UpperLimit)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Joints,
                    index,
                    0,
                    Describe(PhysxPageSection.Joints, index) +
                        " declares an unordered or non finite limit range.");
            }
            if (!IsNonNegativeFinite(joint.MinDistance) ||
                !IsNonNegativeFinite(joint.MaxDistance) ||
                joint.MinDistance > joint.MaxDistance)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Joints,
                    index,
                    0,
                    Describe(PhysxPageSection.Joints, index) +
                        " declares an unordered or negative distance range.");
            }
            if (!IsNonNegativeFinite(joint.ConeAngle0) ||
                !IsNonNegativeFinite(joint.ConeAngle1) ||
                !IsNonNegativeFinite(joint.DriveStiffness) ||
                !IsNonNegativeFinite(joint.DriveDamping) ||
                !IsNonNegativeFinite(joint.DriveMaxForce) ||
                !float.IsFinite(joint.DriveTargetPosition) ||
                !float.IsFinite(joint.DriveTargetVelocity) ||
                !IsNonNegativeFinite(joint.BreakForce) ||
                !IsNonNegativeFinite(joint.BreakTorque))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Joints,
                    index,
                    0,
                    Describe(PhysxPageSection.Joints, index) +
                        " declares a non finite or negative limit, drive, or break value.");
            }
            if (!IsNonNegativeFinite(joint.LimitStiffness) ||
                !IsNonNegativeFinite(joint.LimitDamping) ||
                !IsNonNegativeFinite(joint.LimitRestitution) ||
                joint.LimitRestitution > 1.0F ||
                !IsNonNegativeFinite(joint.LimitBounceThreshold) ||
                !IsNonNegativeFinite(joint.LimitContactDistance))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Joints,
                    index,
                    0,
                    Describe(PhysxPageSection.Joints, index) +
                        " declares a limit spring or restitution outside the supported range.");
            }
            if ((joint.Flags & (uint)PhysxJointFlags.LimitSoft) != 0 && joint.LimitStiffness <= 0.0F)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Joints,
                    index,
                    0,
                    Describe(PhysxPageSection.Joints, index) +
                        " declares a soft limit without a positive limit stiffness.");
            }
            if (!IsNonNegativeFinite(joint.InvMassScale0) ||
                !IsNonNegativeFinite(joint.InvInertiaScale0) ||
                !IsNonNegativeFinite(joint.InvMassScale1) ||
                !IsNonNegativeFinite(joint.InvInertiaScale1))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Joints,
                    index,
                    0,
                    Describe(PhysxPageSection.Joints, index) +
                        " declares a non finite or negative mass scale.");
            }
            for (uint axis = 0; axis < PhysxAbi.JointAxisCount; axis++)
            {
                if (joint.Motion[(int)axis] >= (uint)PhysxJointMotion.Count ||
                    (joint.AxisDriveFlags[(int)axis] & ~(uint)PhysxJointDriveFlags.All) != 0)
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.Joints,
                        index,
                        axis,
                        Describe(PhysxPageSection.Joints, index) +
                            " declares an unknown per axis motion or drive flag.");
                }
                if (!float.IsFinite(joint.AxisLowerLimit[(int)axis]) ||
                    !float.IsFinite(joint.AxisUpperLimit[(int)axis]) ||
                    joint.AxisLowerLimit[(int)axis] > joint.AxisUpperLimit[(int)axis])
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.Joints,
                        index,
                        axis,
                        Describe(PhysxPageSection.Joints, index) +
                            " declares an unordered or non finite per axis limit range.");
                }
                if (!IsNonNegativeFinite(joint.AxisDriveStiffness[(int)axis]) ||
                    !IsNonNegativeFinite(joint.AxisDriveDamping[(int)axis]) ||
                    !IsNonNegativeFinite(joint.AxisDriveMaxForce[(int)axis]) ||
                    !float.IsFinite(joint.AxisDriveTargetPosition[(int)axis]) ||
                    !float.IsFinite(joint.AxisDriveTargetVelocity[(int)axis]))
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.Joints,
                        index,
                        axis,
                        Describe(PhysxPageSection.Joints, index) +
                            " declares a non finite or negative per axis drive value.");
                }
                if (joint.Motion[(int)axis] == (uint)PhysxJointMotion.Locked &&
                    (joint.AxisDriveFlags[(int)axis] & (uint)PhysxJointDriveFlags.Enabled) != 0)
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.Joints,
                        index,
                        axis,
                        Describe(PhysxPageSection.Joints, index) + " drives an axis that it also locks.");
                }
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateFilterPairs(PhysxPageReader reader)
    {
        ReadOnlySpan<PhysxFilterPair> pairs = reader.FilterPairs;
        uint actorCount = reader.Header.Actors.Count;
        for (uint index = 0; index < pairs.Length; index++)
        {
            PhysxFilterPair pair = pairs[(int)index];
            if (pair.Actor0Index >= actorCount ||
                pair.Actor1Index >= actorCount ||
                pair.Actor0Index == pair.Actor1Index)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.FilterPairs,
                    index,
                    0,
                    Describe(PhysxPageSection.FilterPairs, index) +
                        " must reference two distinct actors from this page.");
            }
        }

        if (actorCount != 0 && reader.Header.Scenes.Count == 0)
        {
            return Fail(
                PhysxPageError.Reference,
                PhysxPageSection.Scenes,
                0,
                0,
                "A page with actors must declare at least one scene.");
        }

        if ((reader.Header.Articulations.Count != 0 ||
             reader.Header.Controllers.Count != 0 ||
             reader.Header.Vehicles.Count != 0) &&
            reader.Header.Scenes.Count == 0)
        {
            return Fail(
                PhysxPageError.Reference,
                PhysxPageSection.Scenes,
                0,
                0,
                "A page with articulations, controllers, or vehicles must declare at least one scene.");
        }

        if ((reader.Header.ArticulationTendons.Count != 0 || reader.Header.ArticulationMimicJoints.Count != 0) &&
            reader.Header.Articulations.Count == 0)
        {
            return Fail(
                PhysxPageError.Reference,
                PhysxPageSection.Articulations,
                0,
                0,
                "A page with tendons or mimic joints must declare at least one articulation.");
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateCapacities(
        in PhysxBuildPageHeader header,
        uint dynamicActorCount,
        uint articulationLinkCount,
        uint controllerCount,
        uint publishedWheelCount,
        ulong deformationBodies,
        ulong deformationPoints)
    {
        PhysxResultCapacities capacities = header.Capacities;
        if (capacities.MaxBodyStates > PhysxAbi.MaxResultCapacity ||
            capacities.MaxEvents > PhysxAbi.MaxResultCapacity ||
            capacities.MaxDiagnostics > PhysxAbi.MaxResultCapacity ||
            capacities.MaxDebugLines > PhysxAbi.MaxResultCapacity ||
            capacities.MaxQueryHits > PhysxAbi.MaxResultCapacity ||
            capacities.MaxDeformationBodies > PhysxAbi.MaxResultCapacity)
        {
            return Fail(
                PhysxPageError.Capacity,
                PhysxPageSection.Capacities,
                0,
                HeaderCapacitiesOffset,
                "A declared result capacity exceeds the supported maximum.");
        }
        if (capacities.Reserved0 != 0)
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.Capacities,
                0,
                HeaderCapacitiesOffset,
                "Reserved capacity fields must be zero.");
        }
        if (capacities.MaxBodyStates <
            dynamicActorCount + articulationLinkCount + controllerCount + publishedWheelCount)
        {
            return Fail(
                PhysxPageError.Capacity,
                PhysxPageSection.Capacities,
                dynamicActorCount,
                HeaderCapacitiesOffset,
                "The declared body state capacity is smaller than the number of movable actors, " +
                    "articulation links, controllers, and published vehicle wheels.");
        }
        // The GPU counts are summed in sixty four bits because a page may
        // legally declare more vertices than a thirty two bit sum could hold,
        // and that has to be a diagnosed capacity error rather than a wrapped
        // comparison that silently accepts an under sized buffer.
        if (deformationBodies > capacities.MaxDeformationBodies)
        {
            return Fail(
                PhysxPageError.Capacity,
                PhysxPageSection.Capacities,
                (uint)(deformationBodies & 0xFFFFFFFFUL),
                HeaderCapacitiesOffset,
                "The declared deformation body capacity is smaller than the number of particle " +
                    "bodies and deformables the page declares.");
        }
        if (deformationPoints > capacities.MaxDeformationPoints)
        {
            return Fail(
                PhysxPageError.Capacity,
                PhysxPageSection.Capacities,
                (uint)(deformationPoints & 0xFFFFFFFFUL),
                HeaderCapacitiesOffset,
                "The declared deformation point capacity is smaller than the number of simulated " +
                    "vertices the page declares.");
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateParticleMaterials(
        PhysxPageReader reader,
        HashSet<ulong> identifiers)
    {
        ReadOnlySpan<PhysxParticleMaterialDesc> materials = reader.ParticleMaterials;
        for (int index = 0; index < materials.Length; index++)
        {
            ref readonly PhysxParticleMaterialDesc material = ref materials[index];
            PhysxPageValidationResult? failure = RequireIdentity(
                identifiers, material.Id, PhysxPageSection.ParticleMaterials, (uint)index);
            if (failure is not null)
            {
                return failure;
            }
            if (material.Reserved0 != 0)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.ParticleMaterials,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.ParticleMaterials, (uint)index) +
                        " declares a non zero reserved field.");
            }
            if (!IsNonNegativeFinite(material.Friction) ||
                !IsNonNegativeFinite(material.Damping) ||
                !IsNonNegativeFinite(material.Adhesion) ||
                !IsNonNegativeFinite(material.AdhesionOffsetScale) ||
                !IsNonNegativeFinite(material.ParticleFrictionScale) ||
                !IsNonNegativeFinite(material.ParticleAdhesionScale) ||
                !IsNonNegativeFinite(material.Viscosity) ||
                !IsNonNegativeFinite(material.SurfaceTension) ||
                !IsNonNegativeFinite(material.Cohesion) ||
                !IsNonNegativeFinite(material.VorticityConfinement) ||
                !IsNonNegativeFinite(material.Drag) ||
                !IsNonNegativeFinite(material.Lift) ||
                !IsNonNegativeFinite(material.GravityScale) ||
                !IsNonNegativeFinite(material.Density) ||
                !IsNonNegativeFinite(material.CflCoefficient))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.ParticleMaterials,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.ParticleMaterials, (uint)index) +
                        " declares a non finite or negative particle material value.");
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateParticleSystems(
        PhysxPageReader reader,
        HashSet<ulong> identifiers)
    {
        PhysxBuildPageHeader header = reader.Header;
        ReadOnlySpan<PhysxParticleSystemDesc> systems = reader.ParticleSystems;
        uint claimedBodies = 0;
        for (int index = 0; index < systems.Length; index++)
        {
            ref readonly PhysxParticleSystemDesc system = ref systems[index];
            PhysxPageValidationResult? failure = RequireIdentity(
                identifiers, system.Id, PhysxPageSection.ParticleSystems, (uint)index);
            if (failure is not null)
            {
                return failure;
            }
            if ((system.Flags & ~(uint)PhysxParticleSystemFlags.All) != 0 ||
                system.Reserved0 != 0 || system.Reserved1 != 0)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.ParticleSystems,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.ParticleSystems, (uint)index) +
                        " declares unknown flags or a non zero reserved field.");
            }
            if (system.SceneIndex < 0 || (uint)system.SceneIndex >= header.Scenes.Count)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.ParticleSystems,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.ParticleSystems, (uint)index) +
                        " must reference a scene from this page.");
            }
            if (!IsNonNegativeFinite(system.ContactOffset) ||
                !IsNonNegativeFinite(system.RestOffset) ||
                !IsNonNegativeFinite(system.ParticleContactOffset) ||
                !IsNonNegativeFinite(system.SolidRestOffset) ||
                !IsNonNegativeFinite(system.FluidRestOffset) ||
                !IsNonNegativeFinite(system.MaxDepenetrationVelocity) ||
                !IsNonNegativeFinite(system.NeighborhoodScale) ||
                !system.Wind.IsFinite)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.ParticleSystems,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.ParticleSystems, (uint)index) +
                        " declares a non finite or negative particle system value.");
            }
            if (system.ParticleContactOffset > 0.0F &&
                (system.SolidRestOffset > system.ParticleContactOffset ||
                 system.FluidRestOffset > system.ParticleContactOffset))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.ParticleSystems,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.ParticleSystems, (uint)index) +
                        " declares a rest offset larger than its particle contact offset.");
            }
            if (system.ContactOffset > 0.0F && system.RestOffset > system.ContactOffset)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.ParticleSystems,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.ParticleSystems, (uint)index) +
                        " declares a rest offset larger than its contact offset.");
            }
            if (system.MaxNeighborhood != 0 &&
                (system.MaxNeighborhood < PhysxAbi.MinParticleNeighborhood ||
                 system.MaxNeighborhood > PhysxAbi.MaxParticleNeighborhood))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.ParticleSystems,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.ParticleSystems, (uint)index) +
                        " declares a neighbourhood budget outside the supported range.");
            }
            if (system.SolverPositionIterations > 255)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.ParticleSystems,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.ParticleSystems, (uint)index) +
                        " declares a solver iteration count outside the supported range.");
            }
            if (system.BodyOffset != claimedBodies ||
                system.BodyCount > header.ParticleBodies.Count - claimedBodies)
            {
                return Fail(
                    PhysxPageError.Range,
                    PhysxPageSection.ParticleSystems,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.ParticleSystems, (uint)index) +
                        " must own a particle body window that continues where the previous system ended.");
            }
            claimedBodies += system.BodyCount;
        }

        if (claimedBodies != header.ParticleBodies.Count)
        {
            return Fail(
                PhysxPageError.Range,
                PhysxPageSection.ParticleBodies,
                claimedBodies,
                0,
                "Every particle body must belong to exactly one particle system.");
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateParticleBodies(
        PhysxPageReader reader,
        HashSet<ulong> identifiers,
        ref ulong deformationBodies,
        ref ulong deformationPoints)
    {
        PhysxBuildPageHeader header = reader.Header;
        ReadOnlySpan<PhysxParticleBodyDesc> bodies = reader.ParticleBodies;
        ReadOnlySpan<PhysxVec3f> points = reader.MeshPoints;
        for (int index = 0; index < bodies.Length; index++)
        {
            ref readonly PhysxParticleBodyDesc body = ref bodies[index];
            PhysxPageValidationResult? failure = RequireIdentity(
                identifiers, body.Id, PhysxPageSection.ParticleBodies, (uint)index);
            if (failure is not null)
            {
                return failure;
            }
            if (body.Kind >= (uint)PhysxParticleBodyKind.Count ||
                (body.Flags & ~(uint)PhysxParticleBodyFlags.All) != 0 ||
                body.Reserved0 != 0 || body.Reserved1 != 0)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.ParticleBodies,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.ParticleBodies, (uint)index) +
                        " declares an unknown kind, unknown flags, or a non zero reserved field.");
            }
            if (body.MaterialIndex >= 0 && (uint)body.MaterialIndex >= header.ParticleMaterials.Count)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.ParticleBodies,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.ParticleBodies, (uint)index) +
                        " must reference a particle material from this page.");
            }
            if (body.ParticleGroup > PhysxAbi.MaxParticleGroup)
            {
                return Fail(
                    PhysxPageError.Range,
                    PhysxPageSection.ParticleBodies,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.ParticleBodies, (uint)index) +
                        " declares a collision group outside the twenty bits a particle phase " +
                        "reserves for it.");
            }
            if (!IsNonNegativeFinite(body.Mass) || !body.WorldPose.IsFinite ||
                !IsUnsetOrUsableRotation(body.WorldPose.Rotation))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.ParticleBodies,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.ParticleBodies, (uint)index) +
                        " declares a negative mass or an unusable world pose.");
            }
            if (body.PointCount == 0 || body.PointCount > PhysxAbi.MaxParticlesPerBody ||
                body.PointOffset > header.MeshPoints.Count ||
                body.PointCount > header.MeshPoints.Count - body.PointOffset)
            {
                return Fail(
                    PhysxPageError.Range,
                    PhysxPageSection.ParticleBodies,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.ParticleBodies, (uint)index) +
                        " must own a non empty point window inside the mesh point section and " +
                        "inside the supported particle budget.");
            }
            for (uint point = 0; point < body.PointCount; point++)
            {
                if (!points[(int)(body.PointOffset + point)].IsFinite)
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.ParticleBodies,
                        (uint)index,
                        0,
                        Describe(PhysxPageSection.ParticleBodies, (uint)index) +
                            " declares a non finite particle position.");
                }
            }
            deformationBodies += 1;
            deformationPoints += body.PointCount;
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateDeformableMaterials(
        PhysxPageReader reader,
        HashSet<ulong> identifiers)
    {
        ReadOnlySpan<PhysxDeformableMaterialDesc> materials = reader.DeformableMaterials;
        for (int index = 0; index < materials.Length; index++)
        {
            ref readonly PhysxDeformableMaterialDesc material = ref materials[index];
            PhysxPageValidationResult? failure = RequireIdentity(
                identifiers, material.Id, PhysxPageSection.DeformableMaterials, (uint)index);
            if (failure is not null)
            {
                return failure;
            }
            if (material.Kind >= (uint)PhysxDeformableKind.Count || material.Reserved0 != 0 ||
                material.Reserved1 != 0 || material.Reserved2 != 0)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.DeformableMaterials,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.DeformableMaterials, (uint)index) +
                        " declares an unknown kind or a non zero reserved field.");
            }
            if (!IsPositiveFinite(material.YoungsModulus) || !IsPositiveFinite(material.Density) ||
                !IsNonNegativeFinite(material.DynamicFriction) ||
                !IsNonNegativeFinite(material.ElasticityDamping))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.DeformableMaterials,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.DeformableMaterials, (uint)index) +
                        " declares a non positive stiffness or density, or a negative friction or damping.");
            }
            if (!float.IsFinite(material.PoissonsRatio) || material.PoissonsRatio < 0.0F ||
                material.PoissonsRatio >= 0.5F)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.DeformableMaterials,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.DeformableMaterials, (uint)index) +
                        " declares a Poisson ratio outside the usable zero to one half interval.");
            }
            if (material.Kind == (uint)PhysxDeformableKind.Surface)
            {
                if (!IsNonNegativeFinite(material.BendingStiffness) ||
                    !IsNonNegativeFinite(material.BendingDamping) || !IsPositiveFinite(material.Thickness))
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.DeformableMaterials,
                        (uint)index,
                        0,
                        Describe(PhysxPageSection.DeformableMaterials, (uint)index) +
                            " is a surface material, so it must declare a positive thickness and " +
                            "a non negative bending response.");
                }
            }
            else if (material.BendingStiffness != 0.0F || material.BendingDamping != 0.0F ||
                     material.Thickness != 0.0F)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.DeformableMaterials,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.DeformableMaterials, (uint)index) +
                        " is a volume material, so it must leave the surface shell fields unset.");
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateDeformables(
        PhysxPageReader reader,
        HashSet<ulong> identifiers,
        ref ulong deformationBodies,
        ref ulong deformationPoints)
    {
        PhysxBuildPageHeader header = reader.Header;
        ReadOnlySpan<PhysxDeformableDesc> deformables = reader.Deformables;
        ReadOnlySpan<PhysxDeformableMaterialDesc> materials = reader.DeformableMaterials;
        ReadOnlySpan<PhysxVec3f> points = reader.MeshPoints;
        ReadOnlySpan<uint> indices = reader.MeshIndices;
        for (int index = 0; index < deformables.Length; index++)
        {
            ref readonly PhysxDeformableDesc deformable = ref deformables[index];
            PhysxPageValidationResult? failure = RequireIdentity(
                identifiers, deformable.Id, PhysxPageSection.Deformables, (uint)index);
            if (failure is not null)
            {
                return failure;
            }
            if (deformable.Kind >= (uint)PhysxDeformableKind.Count ||
                (deformable.Flags & ~(uint)PhysxDeformableFlags.All) != 0 ||
                deformable.Reserved0 != 0 || deformable.Reserved1 != 0)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Deformables,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.Deformables, (uint)index) +
                        " declares an unknown kind, unknown flags, or a non zero reserved field.");
            }
            if (deformable.SceneIndex < 0 || (uint)deformable.SceneIndex >= header.Scenes.Count)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.Deformables,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.Deformables, (uint)index) +
                        " must reference a scene from this page.");
            }
            if (deformable.MaterialIndex >= 0)
            {
                if ((uint)deformable.MaterialIndex >= header.DeformableMaterials.Count)
                {
                    return Fail(
                        PhysxPageError.Reference,
                        PhysxPageSection.Deformables,
                        (uint)index,
                        0,
                        Describe(PhysxPageSection.Deformables, (uint)index) +
                            " must reference a deformable material from this page.");
                }
                if (materials[deformable.MaterialIndex].Kind != deformable.Kind)
                {
                    return Fail(
                        PhysxPageError.Reference,
                        PhysxPageSection.Deformables,
                        (uint)index,
                        0,
                        Describe(PhysxPageSection.Deformables, (uint)index) +
                            " must reference a material of its own deformable kind.");
                }
            }
            if (!IsNonNegativeFinite(deformable.VertexVelocityDamping) ||
                !IsNonNegativeFinite(deformable.MaxDisplacement) ||
                !IsNonNegativeFinite(deformable.SelfCollisionFilterDistance) ||
                !IsNonNegativeFinite(deformable.MaxDepenetrationVelocity) ||
                !IsNonNegativeFinite(deformable.SettlingThreshold) ||
                !IsNonNegativeFinite(deformable.SleepThreshold) ||
                !deformable.WorldPose.IsFinite ||
                !IsUnsetOrUsableRotation(deformable.WorldPose.Rotation))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Deformables,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.Deformables, (uint)index) +
                        " declares a non finite or negative solver value, or an unusable world pose.");
            }
            if (deformable.SolverPositionIterations > 255)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Deformables,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.Deformables, (uint)index) +
                        " declares a solver iteration count outside the supported range.");
            }
            if (deformable.PointCount < 3 || deformable.PointCount > PhysxAbi.MaxDeformableVertices ||
                deformable.PointOffset > header.MeshPoints.Count ||
                deformable.PointCount > header.MeshPoints.Count - deformable.PointOffset)
            {
                return Fail(
                    PhysxPageError.Range,
                    PhysxPageSection.Deformables,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.Deformables, (uint)index) +
                        " must own a simulation point window inside the mesh point section and " +
                        "inside the supported vertex budget.");
            }
            for (uint point = 0; point < deformable.PointCount; point++)
            {
                if (!points[(int)(deformable.PointOffset + point)].IsFinite)
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.Deformables,
                        (uint)index,
                        0,
                        Describe(PhysxPageSection.Deformables, (uint)index) +
                            " declares a non finite simulation vertex.");
                }
            }
            uint verticesPerElement = deformable.Kind == (uint)PhysxDeformableKind.Surface ? 3u : 4u;
            if (deformable.IndexCount == 0 || (deformable.IndexCount % verticesPerElement) != 0 ||
                deformable.IndexOffset > header.MeshIndices.Count ||
                deformable.IndexCount > header.MeshIndices.Count - deformable.IndexOffset)
            {
                return Fail(
                    PhysxPageError.Range,
                    PhysxPageSection.Deformables,
                    (uint)index,
                    0,
                    Describe(PhysxPageSection.Deformables, (uint)index) +
                        " must own a whole element index window inside the mesh index section.");
            }
            for (uint element = 0; element < deformable.IndexCount; element++)
            {
                if (indices[(int)(deformable.IndexOffset + element)] >= deformable.PointCount)
                {
                    return Fail(
                        PhysxPageError.Reference,
                        PhysxPageSection.Deformables,
                        (uint)index,
                        0,
                        Describe(PhysxPageSection.Deformables, (uint)index) +
                            " declares a simulation index outside its own point window.");
                }
            }
            if (deformable.Kind == (uint)PhysxDeformableKind.Surface)
            {
                if (deformable.CollisionPointCount != 0 || deformable.CollisionIndexCount != 0 ||
                    deformable.CollisionPointOffset != 0 || deformable.CollisionIndexOffset != 0)
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.Deformables,
                        (uint)index,
                        0,
                        Describe(PhysxPageSection.Deformables, (uint)index) +
                            " is a surface, so it must leave the collision mesh window unset.");
                }
                if ((deformable.Flags & (uint)PhysxDeformableFlags.Kinematic) != 0)
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.Deformables,
                        (uint)index,
                        0,
                        Describe(PhysxPageSection.Deformables, (uint)index) +
                            " is a surface, so it cannot declare a kinematic simulation mesh.");
                }
            }
            else if (deformable.CollisionPointCount != 0 || deformable.CollisionIndexCount != 0)
            {
                PhysxPageValidationResult? collision = ValidateCollisionMesh(reader, in deformable, (uint)index);
                if (collision is not null)
                {
                    return collision;
                }
            }
            deformationBodies += 1;
            deformationPoints += deformable.PointCount;
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateCollisionMesh(
        PhysxPageReader reader,
        in PhysxDeformableDesc deformable,
        uint index)
    {
        PhysxBuildPageHeader header = reader.Header;
        ReadOnlySpan<PhysxVec3f> points = reader.MeshPoints;
        ReadOnlySpan<uint> indices = reader.MeshIndices;
        if (deformable.CollisionPointCount < 4 ||
            deformable.CollisionPointCount > PhysxAbi.MaxDeformableVertices ||
            deformable.CollisionPointOffset > header.MeshPoints.Count ||
            deformable.CollisionPointCount > header.MeshPoints.Count - deformable.CollisionPointOffset)
        {
            return Fail(
                PhysxPageError.Range,
                PhysxPageSection.Deformables,
                index,
                0,
                Describe(PhysxPageSection.Deformables, index) +
                    " must own a collision point window inside the mesh point section.");
        }
        if (deformable.CollisionIndexCount == 0 || (deformable.CollisionIndexCount % 4u) != 0 ||
            deformable.CollisionIndexOffset > header.MeshIndices.Count ||
            deformable.CollisionIndexCount > header.MeshIndices.Count - deformable.CollisionIndexOffset)
        {
            return Fail(
                PhysxPageError.Range,
                PhysxPageSection.Deformables,
                index,
                0,
                Describe(PhysxPageSection.Deformables, index) +
                    " must own a whole tetrahedron collision index window inside the mesh index section.");
        }
        for (uint element = 0; element < deformable.CollisionIndexCount; element++)
        {
            if (indices[(int)(deformable.CollisionIndexOffset + element)] >= deformable.CollisionPointCount)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.Deformables,
                    index,
                    0,
                    Describe(PhysxPageSection.Deformables, index) +
                        " declares a collision index outside its own collision point window.");
            }
        }
        for (uint point = 0; point < deformable.CollisionPointCount; point++)
        {
            if (!points[(int)(deformable.CollisionPointOffset + point)].IsFinite)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Deformables,
                    index,
                    0,
                    Describe(PhysxPageSection.Deformables, index) + " declares a non finite collision vertex.");
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateGpuDomainReferences(PhysxPageReader reader)
    {
        PhysxBuildPageHeader header = reader.Header;
        if ((header.ParticleSystems.Count != 0 || header.Deformables.Count != 0) && header.Scenes.Count == 0)
        {
            return Fail(
                PhysxPageError.Reference,
                PhysxPageSection.Scenes,
                0,
                0,
                "A page with particle systems or deformables must declare at least one scene.");
        }
        if (header.ParticleBodies.Count != 0 && header.ParticleSystems.Count == 0)
        {
            return Fail(
                PhysxPageError.Reference,
                PhysxPageSection.ParticleSystems,
                0,
                0,
                "A page with particle bodies must declare at least one particle system.");
        }
        return null;
    }

    private static PhysxPageValidationResult? ValidateArticulations(PhysxPageReader reader, HashSet<ulong> identifiers)
    {
        ReadOnlySpan<PhysxArticulationDesc> articulations = reader.Articulations;
        ReadOnlySpan<PhysxArticulationLinkDesc> links = reader.ArticulationLinks;
        ReadOnlySpan<PhysxActorShapeRef> actorShapes = reader.ActorShapes;
        ReadOnlySpan<PhysxShapeDesc> shapes = reader.Shapes;
        uint sceneCount = reader.Header.Scenes.Count;
        uint actorShapeCount = reader.Header.ActorShapes.Count;
        uint linkSectionCount = reader.Header.ArticulationLinks.Count;

        uint claimedLinks = 0;
        for (uint index = 0; index < articulations.Length; index++)
        {
            PhysxArticulationDesc articulation = articulations[(int)index];
            PhysxPageValidationResult? missing =
                RequireIdentity(identifiers, articulation.Id, PhysxPageSection.Articulations, index);
            if (missing is not null)
            {
                return missing;
            }
            if ((articulation.Flags & ~(uint)PhysxArticulationFlags.All) != 0 ||
                articulation.Reserved0 != 0 || articulation.Reserved1 != 0 ||
                articulation.Reserved2 != 0 || articulation.Reserved3 != 0)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Articulations,
                    index,
                    0,
                    Describe(PhysxPageSection.Articulations, index) +
                        " declares unknown flags or non zero reserved fields.");
            }
            if (articulation.SceneIndex < 0 || articulation.SceneIndex >= (int)sceneCount)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.Articulations,
                    index,
                    0,
                    Describe(PhysxPageSection.Articulations, index) + " must reference a scene from this page.");
            }
            if (articulation.LinkCount == 0 ||
                articulation.LinkOffset != claimedLinks ||
                articulation.LinkCount > linkSectionCount - claimedLinks)
            {
                return Fail(
                    PhysxPageError.Range,
                    PhysxPageSection.Articulations,
                    index,
                    0,
                    Describe(PhysxPageSection.Articulations, index) +
                        " must own a non empty link window that continues where the previous articulation ended.");
            }
            claimedLinks += articulation.LinkCount;
            if (articulation.PositionIterations > 255 || articulation.VelocityIterations > 255)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Articulations,
                    index,
                    0,
                    Describe(PhysxPageSection.Articulations, index) +
                        " declares more solver iterations than the simulation SDK accepts.");
            }
            if (!IsNonNegativeFinite(articulation.SleepThreshold) ||
                !IsNonNegativeFinite(articulation.StabilizationThreshold) ||
                !IsNonNegativeFinite(articulation.MaxJointVelocity) ||
                !IsNonNegativeFinite(articulation.WakeCounter))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Articulations,
                    index,
                    0,
                    Describe(PhysxPageSection.Articulations, index) +
                        " declares a non finite or negative solver budget.");
            }
        }

        if (claimedLinks != linkSectionCount)
        {
            return Fail(
                PhysxPageError.Range,
                PhysxPageSection.ArticulationLinks,
                claimedLinks,
                0,
                "Every articulation link must belong to exactly one articulation.");
        }

        for (uint artIndex = 0; artIndex < articulations.Length; artIndex++)
        {
            PhysxArticulationDesc articulation = articulations[(int)artIndex];
            for (uint local = 0; local < articulation.LinkCount; local++)
            {
                uint linkIndex = articulation.LinkOffset + local;
                PhysxArticulationLinkDesc link = links[(int)linkIndex];
                PhysxPageValidationResult? missing =
                    RequireIdentity(identifiers, link.Id, PhysxPageSection.ArticulationLinks, linkIndex);
                if (missing is not null)
                {
                    return missing;
                }
                if (link.JointType >= (uint)PhysxArticulationJointType.Count ||
                    (link.Flags & ~(uint)PhysxArticulationLinkFlags.All) != 0 ||
                    link.Reserved0 != 0)
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.ArticulationLinks,
                        linkIndex,
                        0,
                        Describe(PhysxPageSection.ArticulationLinks, linkIndex) +
                            " declares an unknown joint type, unknown flags, or a non zero reserved field.");
                }

                if (local == 0)
                {
                    if (link.ParentId != 0 ||
                        link.JointType != (uint)PhysxArticulationJointType.None)
                    {
                        return Fail(
                            PhysxPageError.Reference,
                            PhysxPageSection.ArticulationLinks,
                            linkIndex,
                            0,
                            Describe(PhysxPageSection.ArticulationLinks, linkIndex) +
                                " is a root link, so it must name no parent and no inbound joint.");
                    }
                }
                else
                {
                    bool parentFound = false;
                    for (uint earlier = 0; earlier < local && !parentFound; earlier++)
                    {
                        PhysxArticulationLinkDesc candidate = links[(int)(articulation.LinkOffset + earlier)];
                        parentFound = candidate.Id == link.ParentId;
                    }
                    if (!parentFound ||
                        link.JointType == (uint)PhysxArticulationJointType.None)
                    {
                        return Fail(
                            PhysxPageError.Reference,
                            PhysxPageSection.ArticulationLinks,
                            linkIndex,
                            0,
                            Describe(PhysxPageSection.ArticulationLinks, linkIndex) +
                                " must name an inbound joint and a parent link that appears " +
                                "earlier in the same articulation.");
                    }
                }

                if (!link.WorldPose.IsFinite || !link.WorldPose.Rotation.IsUsableRotation ||
                    !link.ParentFrame.IsFinite || !link.ParentFrame.Rotation.IsUsableRotation ||
                    !link.ChildFrame.IsFinite || !link.ChildFrame.Rotation.IsUsableRotation)
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.ArticulationLinks,
                        linkIndex,
                        0,
                        Describe(PhysxPageSection.ArticulationLinks, linkIndex) +
                            " declares a non finite pose or joint frame.");
                }
                if (!IsNonNegativeFinite(link.Mass) || !link.CenterOfMass.IsFinite ||
                    !link.Inertia.IsFinite || link.Inertia.X < 0.0F ||
                    link.Inertia.Y < 0.0F || link.Inertia.Z < 0.0F ||
                    !IsUnsetOrUsableRotation(link.PrincipalAxes))
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.ArticulationLinks,
                        linkIndex,
                        0,
                        Describe(PhysxPageSection.ArticulationLinks, linkIndex) +
                            " declares a non finite or negative mass frame.");
                }
                if (!IsNonNegativeFinite(link.LinearDamping) || !IsNonNegativeFinite(link.AngularDamping) ||
                    !IsNonNegativeFinite(link.MaxLinearVelocity) || !IsNonNegativeFinite(link.MaxAngularVelocity) ||
                    !IsNonNegativeFinite(link.JointFriction) || !IsNonNegativeFinite(link.MaxJointVelocity))
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.ArticulationLinks,
                        linkIndex,
                        0,
                        Describe(PhysxPageSection.ArticulationLinks, linkIndex) +
                            " declares a non finite or negative damping, velocity, or friction budget.");
                }
                if (link.ShapeCount != 0 &&
                    (link.ShapeOffset >= actorShapeCount ||
                     link.ShapeCount > actorShapeCount - link.ShapeOffset))
                {
                    return Fail(
                        PhysxPageError.Range,
                        PhysxPageSection.ArticulationLinks,
                        linkIndex,
                        0,
                        Describe(PhysxPageSection.ArticulationLinks, linkIndex) +
                            " references an actor shape window outside this page.");
                }
                if (link.CollisionGroup >= PhysxAbi.MaxCollisionGroups)
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.ArticulationLinks,
                        linkIndex,
                        0,
                        Describe(PhysxPageSection.ArticulationLinks, linkIndex) +
                            " declares a collision group outside the supported range.");
                }

                // Every articulation link is a movable body, so it carries the same geometry
                // restriction a dynamic actor carries.
                for (uint offset = 0; offset < link.ShapeCount; offset++)
                {
                    PhysxActorShapeRef reference = actorShapes[(int)(link.ShapeOffset + offset)];
                    var shapeType = (PhysxShapeType)shapes[(int)reference.ShapeIndex].Type;
                    if (shapeType is PhysxShapeType.Plane or PhysxShapeType.TriangleMesh
                        or PhysxShapeType.Heightfield)
                    {
                        return Fail(
                            PhysxPageError.Value,
                            PhysxPageSection.ArticulationLinks,
                            linkIndex,
                            0,
                            Describe(PhysxPageSection.ArticulationLinks, linkIndex) +
                                " is movable and cannot use a plane, triangle mesh, or height field shape.");
                    }
                }
                for (uint axis = 0; axis < PhysxAbi.JointAxisCount; axis++)
                {
                    if (link.Motion[(int)axis] >= (uint)PhysxJointMotion.Count ||
                        (link.DriveFlags[(int)axis] & ~(uint)PhysxJointDriveFlags.All) != 0)
                    {
                        return Fail(
                            PhysxPageError.Value,
                            PhysxPageSection.ArticulationLinks,
                            linkIndex,
                            axis,
                            Describe(PhysxPageSection.ArticulationLinks, linkIndex) +
                                " declares an unknown per axis motion or drive flag.");
                    }
                    if (!float.IsFinite(link.LowerLimit[(int)axis]) ||
                        !float.IsFinite(link.UpperLimit[(int)axis]) ||
                        link.LowerLimit[(int)axis] > link.UpperLimit[(int)axis])
                    {
                        return Fail(
                            PhysxPageError.Value,
                            PhysxPageSection.ArticulationLinks,
                            linkIndex,
                            axis,
                            Describe(PhysxPageSection.ArticulationLinks, linkIndex) +
                                " declares an unordered or non finite per axis limit range.");
                    }
                    if (!IsNonNegativeFinite(link.DriveStiffness[(int)axis]) ||
                        !IsNonNegativeFinite(link.DriveDamping[(int)axis]) ||
                        !IsNonNegativeFinite(link.DriveMaxForce[(int)axis]) ||
                        !float.IsFinite(link.DriveTargetPosition[(int)axis]) ||
                        !float.IsFinite(link.DriveTargetVelocity[(int)axis]) ||
                        !IsNonNegativeFinite(link.Armature[(int)axis]))
                    {
                        return Fail(
                            PhysxPageError.Value,
                            PhysxPageSection.ArticulationLinks,
                            linkIndex,
                            axis,
                            Describe(PhysxPageSection.ArticulationLinks, linkIndex) +
                                " declares a non finite or negative per axis drive value.");
                    }
                    if (link.Motion[(int)axis] == (uint)PhysxJointMotion.Locked &&
                        (link.DriveFlags[(int)axis] & (uint)PhysxJointDriveFlags.Enabled) != 0)
                    {
                        return Fail(
                            PhysxPageError.Value,
                            PhysxPageSection.ArticulationLinks,
                            linkIndex,
                            axis,
                            Describe(PhysxPageSection.ArticulationLinks, linkIndex) +
                                " drives an axis that it also locks.");
                    }
                }
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateControllers(PhysxPageReader reader, HashSet<ulong> identifiers)
    {
        ReadOnlySpan<PhysxControllerDesc> controllers = reader.Controllers;
        uint sceneCount = reader.Header.Scenes.Count;
        uint materialCount = reader.Header.Materials.Count;

        for (uint index = 0; index < controllers.Length; index++)
        {
            PhysxControllerDesc controller = controllers[(int)index];
            PhysxPageValidationResult? missing =
                RequireIdentity(identifiers, controller.Id, PhysxPageSection.Controllers, index);
            if (missing is not null)
            {
                return missing;
            }
            if (controller.Shape >= (uint)PhysxControllerShape.Count ||
                (controller.Flags & ~(uint)PhysxControllerFlags.All) != 0 ||
                controller.NonWalkableMode >= (uint)PhysxControllerNonWalkableMode.Count ||
                controller.ClimbingMode >= (uint)PhysxControllerClimbingMode.Count ||
                controller.Reserved0 != 0 || controller.Reserved1 != 0)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Controllers,
                    index,
                    0,
                    Describe(PhysxPageSection.Controllers, index) +
                        " declares an unknown shape, mode, flags, or a non zero reserved field.");
            }
            if (controller.SceneIndex < 0 || controller.SceneIndex >= (int)sceneCount)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.Controllers,
                    index,
                    0,
                    Describe(PhysxPageSection.Controllers, index) + " must reference a scene from this page.");
            }
            if (controller.MaterialIndex < -1 || controller.MaterialIndex >= (int)materialCount)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.Controllers,
                    index,
                    0,
                    Describe(PhysxPageSection.Controllers, index) + " references a material outside this page.");
            }
            if (!controller.Position.IsFinite || !controller.UpDirection.IsFinite)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Controllers,
                    index,
                    0,
                    Describe(PhysxPageSection.Controllers, index) +
                        " declares a non finite position or up direction.");
            }
            if (controller.Shape == (uint)PhysxControllerShape.Capsule)
            {
                if (!(controller.Radius > 0.0F) || !float.IsFinite(controller.Radius) ||
                    !IsNonNegativeFinite(controller.Height))
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.Controllers,
                        index,
                        0,
                        Describe(PhysxPageSection.Controllers, index) +
                            " declares a capsule without a positive radius.");
                }
            }
            else if (!(controller.HalfExtents.X > 0.0F) || !(controller.HalfExtents.Y > 0.0F) ||
                     !(controller.HalfExtents.Z > 0.0F) || !controller.HalfExtents.IsFinite)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Controllers,
                    index,
                    0,
                    Describe(PhysxPageSection.Controllers, index) +
                        " declares a box without positive half extents.");
            }
            if (!IsNonNegativeFinite(controller.SlopeLimit) ||
                controller.SlopeLimit >= 1.5707963F ||
                !IsNonNegativeFinite(controller.StepOffset) ||
                !IsNonNegativeFinite(controller.ContactOffset) ||
                !IsNonNegativeFinite(controller.Density) ||
                !IsNonNegativeFinite(controller.ScaleCoefficient) || controller.ScaleCoefficient > 1.0F ||
                !IsNonNegativeFinite(controller.VolumeGrowth))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Controllers,
                    index,
                    0,
                    Describe(PhysxPageSection.Controllers, index) +
                        " declares a slope, step, contact, or scale value outside the supported range.");
            }
            if (controller.CollisionGroup >= PhysxAbi.MaxCollisionGroups)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Controllers,
                    index,
                    0,
                    Describe(PhysxPageSection.Controllers, index) +
                        " declares a collision group outside the supported range.");
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateTendons(PhysxPageReader reader, HashSet<ulong> identifiers)
    {
        ReadOnlySpan<PhysxTendonDesc> tendons = reader.ArticulationTendons;
        ReadOnlySpan<PhysxTendonNodeDesc> nodes = reader.ArticulationTendonNodes;
        ReadOnlySpan<PhysxArticulationDesc> articulations = reader.Articulations;
        uint claimedNodes = 0;
        for (uint index = 0; index < tendons.Length; index++)
        {
            PhysxTendonDesc tendon = tendons[(int)index];
            PhysxPageValidationResult? identity =
                RequireIdentity(identifiers, tendon.Id, PhysxPageSection.ArticulationTendons, index);
            if (identity is not null)
            {
                return identity;
            }
            if (tendon.Type >= (uint)PhysxTendonType.Count ||
                (tendon.Flags & ~(uint)PhysxTendonFlags.LimitEnabled) != 0 ||
                tendon.Reserved0 != 0 || tendon.Reserved1 != 0.0F)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.ArticulationTendons,
                    index,
                    0,
                    Describe(PhysxPageSection.ArticulationTendons, index) +
                        " declares an unknown tendon type, unknown flags, or a non zero reserved field.");
            }
            if (tendon.ArticulationIndex >= (uint)articulations.Length)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.ArticulationTendons,
                    index,
                    0,
                    Describe(PhysxPageSection.ArticulationTendons, index) +
                        " must reference an articulation from this page.");
            }
            if (tendon.NodeOffset != claimedNodes || tendon.NodeCount == 0 ||
                tendon.NodeCount > (uint)nodes.Length - claimedNodes)
            {
                return Fail(
                    PhysxPageError.Range,
                    PhysxPageSection.ArticulationTendons,
                    index,
                    0,
                    Describe(PhysxPageSection.ArticulationTendons, index) +
                        " must own a non empty node window that continues where the previous tendon ended.");
            }
            claimedNodes += tendon.NodeCount;
            if (!IsNonNegativeFinite(tendon.Stiffness) || !IsNonNegativeFinite(tendon.Damping) ||
                !IsNonNegativeFinite(tendon.LimitStiffness) || !float.IsFinite(tendon.Offset) ||
                !IsNonNegativeFinite(tendon.RestLength))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.ArticulationTendons,
                    index,
                    0,
                    Describe(PhysxPageSection.ArticulationTendons, index) +
                        " declares a non finite or negative tendon gain.");
            }
            if (!float.IsFinite(tendon.LowLimit) || !float.IsFinite(tendon.HighLimit) ||
                tendon.LowLimit > tendon.HighLimit)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.ArticulationTendons,
                    index,
                    0,
                    Describe(PhysxPageSection.ArticulationTendons, index) +
                        " declares an unordered or non finite tendon limit range.");
            }
        }
        if (claimedNodes != (uint)nodes.Length)
        {
            return Fail(
                PhysxPageError.Range,
                PhysxPageSection.ArticulationTendonNodes,
                claimedNodes,
                0,
                "Every articulation tendon node must belong to exactly one tendon.");
        }

        for (uint index = 0; index < tendons.Length; index++)
        {
            PhysxTendonDesc tendon = tendons[(int)index];
            PhysxArticulationDesc articulation = articulations[(int)tendon.ArticulationIndex];
            for (uint local = 0; local < tendon.NodeCount; local++)
            {
                uint nodeIndex = tendon.NodeOffset + local;
                PhysxTendonNodeDesc node = nodes[(int)nodeIndex];
                PhysxPageValidationResult? nodeIdentity =
                    RequireIdentity(identifiers, node.Id, PhysxPageSection.ArticulationTendonNodes, nodeIndex);
                if (nodeIdentity is not null)
                {
                    return nodeIdentity;
                }
                if ((node.Flags & ~(uint)PhysxTendonFlags.LimitEnabled) != 0 ||
                    node.Reserved0 != 0 || node.Reserved1 != 0)
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.ArticulationTendonNodes,
                        nodeIndex,
                        0,
                        Describe(PhysxPageSection.ArticulationTendonNodes, nodeIndex) +
                            " declares unknown flags or a non zero reserved field.");
                }
                if (local == 0)
                {
                    if (node.ParentIndex != 0)
                    {
                        return Fail(
                            PhysxPageError.Reference,
                            PhysxPageSection.ArticulationTendonNodes,
                            nodeIndex,
                            0,
                            Describe(PhysxPageSection.ArticulationTendonNodes, nodeIndex) +
                                " roots its tendon, so it must name no parent node.");
                    }
                }
                else if (node.ParentIndex == 0 || node.ParentIndex > local)
                {
                    return Fail(
                        PhysxPageError.Reference,
                        PhysxPageSection.ArticulationTendonNodes,
                        nodeIndex,
                        0,
                        Describe(PhysxPageSection.ArticulationTendonNodes, nodeIndex) +
                            " must name a parent node that appears earlier in the same tendon.");
                }
                if (node.LinkIndex >= articulation.LinkCount)
                {
                    return Fail(
                        PhysxPageError.Reference,
                        PhysxPageSection.ArticulationTendonNodes,
                        nodeIndex,
                        0,
                        Describe(PhysxPageSection.ArticulationTendonNodes, nodeIndex) +
                            " must reference a link of the articulation that owns its tendon.");
                }
                if (tendon.Type == (uint)PhysxTendonType.Fixed)
                {
                    if (node.Axis >= (uint)PhysxJointAxis.Count)
                    {
                        return Fail(
                            PhysxPageError.Value,
                            PhysxPageSection.ArticulationTendonNodes,
                            nodeIndex,
                            0,
                            Describe(PhysxPageSection.ArticulationTendonNodes, nodeIndex) +
                                " declares an axis outside the supported range.");
                    }
                    if (local != 0 && node.LinkIndex == 0)
                    {
                        return Fail(
                            PhysxPageError.Reference,
                            PhysxPageSection.ArticulationTendonNodes,
                            nodeIndex,
                            0,
                            Describe(PhysxPageSection.ArticulationTendonNodes, nodeIndex) +
                                " drives an inbound joint, so it must not reference the " +
                                "articulation root, which has none.");
                    }
                }
                else if (node.Axis != 0 || !node.RelativeOffset.IsFinite)
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.ArticulationTendonNodes,
                        nodeIndex,
                        0,
                        Describe(PhysxPageSection.ArticulationTendonNodes, nodeIndex) +
                            " is a spatial attachment, so it must leave the axis unset and " +
                            "declare a finite relative offset.");
                }
                if (!float.IsFinite(node.Coefficient) || !float.IsFinite(node.RecipCoefficient) ||
                    !IsNonNegativeFinite(node.RestLength))
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.ArticulationTendonNodes,
                        nodeIndex,
                        0,
                        Describe(PhysxPageSection.ArticulationTendonNodes, nodeIndex) +
                            " declares a non finite coefficient or a negative rest length.");
                }
                if (!float.IsFinite(node.LowLimit) || !float.IsFinite(node.HighLimit) ||
                    node.LowLimit > node.HighLimit)
                {
                    return Fail(
                        PhysxPageError.Value,
                        PhysxPageSection.ArticulationTendonNodes,
                        nodeIndex,
                        0,
                        Describe(PhysxPageSection.ArticulationTendonNodes, nodeIndex) +
                            " declares an unordered or non finite limit range.");
                }
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateMimicJoints(PhysxPageReader reader, HashSet<ulong> identifiers)
    {
        ReadOnlySpan<PhysxMimicJointDesc> mimicJoints = reader.ArticulationMimicJoints;
        ReadOnlySpan<PhysxArticulationDesc> articulations = reader.Articulations;
        for (uint index = 0; index < mimicJoints.Length; index++)
        {
            PhysxMimicJointDesc mimic = mimicJoints[(int)index];
            PhysxPageValidationResult? identity =
                RequireIdentity(identifiers, mimic.Id, PhysxPageSection.ArticulationMimicJoints, index);
            if (identity is not null)
            {
                return identity;
            }
            if (mimic.Reserved0 != 0 || mimic.Reserved1 != 0 || mimic.Reserved2 != 0)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.ArticulationMimicJoints,
                    index,
                    0,
                    Describe(PhysxPageSection.ArticulationMimicJoints, index) +
                        " declares a non zero reserved field.");
            }
            if (mimic.ArticulationIndex >= (uint)articulations.Length)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.ArticulationMimicJoints,
                    index,
                    0,
                    Describe(PhysxPageSection.ArticulationMimicJoints, index) +
                        " must reference an articulation from this page.");
            }
            PhysxArticulationDesc articulation = articulations[(int)mimic.ArticulationIndex];
            if (mimic.LinkA == 0 || mimic.LinkB == 0 ||
                mimic.LinkA >= articulation.LinkCount || mimic.LinkB >= articulation.LinkCount)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.ArticulationMimicJoints,
                    index,
                    0,
                    Describe(PhysxPageSection.ArticulationMimicJoints, index) +
                        " must couple two non root links of the articulation that owns it.");
            }
            if (mimic.LinkA == mimic.LinkB && mimic.AxisA == mimic.AxisB)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.ArticulationMimicJoints,
                    index,
                    0,
                    Describe(PhysxPageSection.ArticulationMimicJoints, index) +
                        " must couple two different joint axes.");
            }
            if (mimic.AxisA >= (uint)PhysxJointAxis.Count || mimic.AxisB >= (uint)PhysxJointAxis.Count)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.ArticulationMimicJoints,
                    index,
                    0,
                    Describe(PhysxPageSection.ArticulationMimicJoints, index) +
                        " declares an axis outside the supported range.");
            }
            if (!float.IsFinite(mimic.GearRatio) || mimic.GearRatio == 0.0F || !float.IsFinite(mimic.Offset) ||
                !IsNonNegativeFinite(mimic.NaturalFrequency) || !IsNonNegativeFinite(mimic.DampingRatio))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.ArticulationMimicJoints,
                    index,
                    0,
                    Describe(PhysxPageSection.ArticulationMimicJoints, index) +
                        " declares a zero, non finite, or negative coupling value.");
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateVehicles(
        PhysxPageReader reader,
        HashSet<ulong> identifiers,
        ref uint publishedWheelCount)
    {
        ReadOnlySpan<PhysxVehicleDesc> vehicles = reader.Vehicles;
        ReadOnlySpan<PhysxVehicleWheelDesc> wheels = reader.VehicleWheels;
        ReadOnlySpan<PhysxActorDesc> actors = reader.Actors;
        uint sceneCount = reader.Header.Scenes.Count;
        uint claimedWheels = 0;
        for (uint index = 0; index < vehicles.Length; index++)
        {
            PhysxVehicleDesc vehicle = vehicles[(int)index];
            PhysxPageValidationResult? identity =
                RequireIdentity(identifiers, vehicle.Id, PhysxPageSection.Vehicles, index);
            if (identity is not null)
            {
                return identity;
            }
            if ((vehicle.Flags & ~(uint)PhysxVehicleFlags.All) != 0 ||
                vehicle.Drive >= (uint)PhysxVehicleDrive.Count ||
                vehicle.Query >= (uint)PhysxVehicleQuery.Count ||
                vehicle.Reserved0 != 0 || vehicle.Reserved1 != 0)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Vehicles,
                    index,
                    0,
                    Describe(PhysxPageSection.Vehicles, index) +
                        " declares unknown flags, an unknown drive or query mode, or a non zero reserved field.");
            }
            if (vehicle.SceneIndex >= sceneCount)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.Vehicles,
                    index,
                    0,
                    Describe(PhysxPageSection.Vehicles, index) + " must reference a scene from this page.");
            }
            if (vehicle.ActorIndex >= (uint)actors.Length)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.Vehicles,
                    index,
                    0,
                    Describe(PhysxPageSection.Vehicles, index) + " must reference a chassis actor from this page.");
            }
            PhysxActorDesc chassis = actors[(int)vehicle.ActorIndex];
            if (chassis.Type != (uint)PhysxActorType.Dynamic)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.Vehicles,
                    index,
                    0,
                    Describe(PhysxPageSection.Vehicles, index) +
                        " must reference a dynamic, non kinematic chassis actor.");
            }
            if ((uint)chassis.SceneIndex != vehicle.SceneIndex)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.Vehicles,
                    index,
                    0,
                    Describe(PhysxPageSection.Vehicles, index) + " must share the scene of its chassis actor.");
            }
            if (vehicle.LongitudinalAxis > (uint)PhysxAxis.Z || vehicle.LateralAxis > (uint)PhysxAxis.Z ||
                vehicle.VerticalAxis > (uint)PhysxAxis.Z ||
                vehicle.LongitudinalAxis == vehicle.LateralAxis ||
                vehicle.LongitudinalAxis == vehicle.VerticalAxis ||
                vehicle.LateralAxis == vehicle.VerticalAxis)
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Vehicles,
                    index,
                    0,
                    Describe(PhysxPageSection.Vehicles, index) + " must declare three different coordinate axes.");
            }
            if (vehicle.WheelOffset != claimedWheels || vehicle.WheelCount == 0 ||
                vehicle.WheelCount > (uint)wheels.Length - claimedWheels ||
                vehicle.WheelCount > PhysxAbi.MaxVehicleWheels)
            {
                return Fail(
                    PhysxPageError.Range,
                    PhysxPageSection.Vehicles,
                    index,
                    0,
                    Describe(PhysxPageSection.Vehicles, index) +
                        " must own a non empty wheel window that continues where the previous " +
                        "vehicle ended and fits the supported wheel budget.");
            }
            claimedWheels += vehicle.WheelCount;
            if ((vehicle.Flags & (uint)PhysxVehicleFlags.PublishWheels) != 0)
            {
                publishedWheelCount += vehicle.WheelCount;
            }
            if (!IsNonNegativeFinite(vehicle.ChassisMass) || !vehicle.ChassisMoi.IsFinite ||
                vehicle.ChassisMoi.X < 0.0F || vehicle.ChassisMoi.Y < 0.0F || vehicle.ChassisMoi.Z < 0.0F ||
                !IsNonNegativeFinite(vehicle.SprungMassTotal))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Vehicles,
                    index,
                    0,
                    Describe(PhysxPageSection.Vehicles, index) +
                        " declares a non finite or negative chassis mass frame.");
            }
            if (!IsNonNegativeFinite(vehicle.MaxBrakeTorque) ||
                !IsNonNegativeFinite(vehicle.MaxHandBrakeTorque) ||
                !IsNonNegativeFinite(vehicle.MaxSteerAngle) || vehicle.MaxSteerAngle > 3.1415927F ||
                !IsNonNegativeFinite(vehicle.DefaultFriction))
            {
                return Fail(
                    PhysxPageError.Value,
                    PhysxPageSection.Vehicles,
                    index,
                    0,
                    Describe(PhysxPageSection.Vehicles, index) +
                        " declares a brake, steer, or friction value outside the supported range.");
            }
            if (vehicle.Drive == (uint)PhysxVehicleDrive.Engine)
            {
                PhysxPageValidationResult? engineFailure = ValidateVehicleEngine(in vehicle, index);
                if (engineFailure is not null)
                {
                    return engineFailure;
                }
            }
        }
        if (claimedWheels != (uint)wheels.Length)
        {
            return Fail(
                PhysxPageError.Range,
                PhysxPageSection.VehicleWheels,
                claimedWheels,
                0,
                "Every vehicle wheel must belong to exactly one vehicle.");
        }

        for (uint index = 0; index < vehicles.Length; index++)
        {
            PhysxVehicleDesc vehicle = vehicles[(int)index];
            bool hasDrivenWheel = false;
            for (uint local = 0; local < vehicle.WheelCount; local++)
            {
                uint wheelIndex = vehicle.WheelOffset + local;
                PhysxVehicleWheelDesc wheel = wheels[(int)wheelIndex];
                PhysxPageValidationResult? wheelIdentity =
                    RequireIdentity(identifiers, wheel.Id, PhysxPageSection.VehicleWheels, wheelIndex);
                if (wheelIdentity is not null)
                {
                    return wheelIdentity;
                }
                if ((wheel.Flags & (uint)PhysxVehicleWheelFlags.Driven) != 0)
                {
                    hasDrivenWheel = true;
                }
                PhysxPageValidationResult? wheelFailure =
                    ValidateVehicleWheel(in wheel, in vehicle, wheelIndex);
                if (wheelFailure is not null)
                {
                    return wheelFailure;
                }
            }
            if (vehicle.Drive == (uint)PhysxVehicleDrive.Engine && !hasDrivenWheel)
            {
                return Fail(
                    PhysxPageError.Reference,
                    PhysxPageSection.Vehicles,
                    index,
                    0,
                    Describe(PhysxPageSection.Vehicles, index) +
                        " drives an engine, so at least one of its wheels must receive drive torque.");
            }
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateVehicleEngine(in PhysxVehicleDesc vehicle, uint index)
    {
        if (!IsPositiveFinite(vehicle.EnginePeakTorque) || !IsPositiveFinite(vehicle.EngineMoi) ||
            !IsNonNegativeFinite(vehicle.EngineIdleOmega) ||
            !IsPositiveFinite(vehicle.EngineMaxOmega) ||
            vehicle.EngineIdleOmega >= vehicle.EngineMaxOmega ||
            !IsNonNegativeFinite(vehicle.EngineDampingFullThrottle) ||
            !IsNonNegativeFinite(vehicle.EngineDampingZeroThrottleClutchEngaged) ||
            !IsNonNegativeFinite(vehicle.EngineDampingZeroThrottleClutchDisengaged))
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.Vehicles,
                index,
                0,
                Describe(PhysxPageSection.Vehicles, index) +
                    " drives an engine, so it must declare a usable engine.");
        }
        if (!IsPositiveFinite(vehicle.ClutchStrength) ||
            !IsNonNegativeFinite(vehicle.GearSwitchTime) ||
            !IsPositiveFinite(vehicle.FinalGearRatio) ||
            !IsPositiveFinite(vehicle.ReverseGearRatio) ||
            !IsPositiveFinite(vehicle.FirstGearRatio) || !IsPositiveFinite(vehicle.TopGearRatio) ||
            vehicle.TopGearRatio > vehicle.FirstGearRatio)
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.Vehicles,
                index,
                0,
                Describe(PhysxPageSection.Vehicles, index) +
                    " drives an engine, so it must declare a usable clutch and gearbox.");
        }
        // The bound is a subtraction so that a forward gear count near the top
        // of the unsigned range cannot wrap past the comparison.
        if (vehicle.ForwardGearCount == 0 || vehicle.ForwardGearCount > PhysxAbi.MaxVehicleGears - 2)
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.Vehicles,
                index,
                0,
                Describe(PhysxPageSection.Vehicles, index) +
                    " declares a forward gear count outside the supported range.");
        }
        if (!IsNonNegativeFinite(vehicle.AutoboxUpRatio) || vehicle.AutoboxUpRatio > 1.0F ||
            !IsNonNegativeFinite(vehicle.AutoboxDownRatio) || vehicle.AutoboxDownRatio > 1.0F ||
            !IsNonNegativeFinite(vehicle.AutoboxLatency))
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.Vehicles,
                index,
                0,
                Describe(PhysxPageSection.Vehicles, index) +
                    " declares an autobox value outside the supported range.");
        }

        return null;
    }

    private static PhysxPageValidationResult? ValidateVehicleWheel(
        in PhysxVehicleWheelDesc wheel,
        in PhysxVehicleDesc vehicle,
        uint wheelIndex)
    {
        if ((wheel.Flags & ~(uint)PhysxVehicleWheelFlags.All) != 0 ||
            wheel.Reserved0 != 0 || wheel.Reserved1 != 0)
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.VehicleWheels,
                wheelIndex,
                0,
                Describe(PhysxPageSection.VehicleWheels, wheelIndex) +
                    " declares unknown flags or a non zero reserved field.");
        }
        if (wheel.AxleIndex >= vehicle.WheelCount)
        {
            return Fail(
                PhysxPageError.Range,
                PhysxPageSection.VehicleWheels,
                wheelIndex,
                0,
                Describe(PhysxPageSection.VehicleWheels, wheelIndex) +
                    " declares an axle index outside the axle budget of its vehicle.");
        }
        if (!wheel.SuspensionAttachment.IsFinite || !wheel.WheelAttachment.IsFinite ||
            !IsUnsetOrUsableRotation(wheel.SuspensionAttachment.Rotation) ||
            !IsUnsetOrUsableRotation(wheel.WheelAttachment.Rotation))
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.VehicleWheels,
                wheelIndex,
                0,
                Describe(PhysxPageSection.VehicleWheels, wheelIndex) +
                    " declares a non finite or unusable attachment frame.");
        }
        if (!wheel.SuspensionTravelDir.IsFinite ||
            !IsPositiveFinite(wheel.SuspensionTravelDist) ||
            !IsPositiveFinite(wheel.SuspensionStiffness) ||
            !IsNonNegativeFinite(wheel.SuspensionDamping) ||
            !IsNonNegativeFinite(wheel.SprungMass))
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.VehicleWheels,
                wheelIndex,
                0,
                Describe(PhysxPageSection.VehicleWheels, wheelIndex) +
                    " declares a non finite or non positive suspension.");
        }
        float travelLength =
            (wheel.SuspensionTravelDir.X * wheel.SuspensionTravelDir.X) +
            (wheel.SuspensionTravelDir.Y * wheel.SuspensionTravelDir.Y) +
            (wheel.SuspensionTravelDir.Z * wheel.SuspensionTravelDir.Z);
        if (travelLength <= 1.0e-8F)
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.VehicleWheels,
                wheelIndex,
                0,
                Describe(PhysxPageSection.VehicleWheels, wheelIndex) +
                    " declares a degenerate suspension travel direction.");
        }
        if (!IsPositiveFinite(wheel.Radius) || !IsPositiveFinite(wheel.HalfWidth) ||
            !IsPositiveFinite(wheel.Mass) || !IsNonNegativeFinite(wheel.Moi) ||
            !IsNonNegativeFinite(wheel.DampingRate))
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.VehicleWheels,
                wheelIndex,
                0,
                Describe(PhysxPageSection.VehicleWheels, wheelIndex) +
                    " declares a non finite or non positive wheel body.");
        }
        if (!IsNonNegativeFinite(wheel.TireLatStiffX) ||
            !IsNonNegativeFinite(wheel.TireLatStiffY) ||
            !IsNonNegativeFinite(wheel.TireLongStiff) ||
            !IsNonNegativeFinite(wheel.TireCamberStiff) ||
            !IsNonNegativeFinite(wheel.TireRestLoad) ||
            !IsNonNegativeFinite(wheel.TireFriction))
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.VehicleWheels,
                wheelIndex,
                0,
                Describe(PhysxPageSection.VehicleWheels, wheelIndex) +
                    " declares a non finite or negative tire value.");
        }
        if (!IsNonNegativeFinite(wheel.SteerResponse) || wheel.SteerResponse > 1.0F ||
            !IsNonNegativeFinite(wheel.BrakeResponse) || wheel.BrakeResponse > 1.0F ||
            !IsNonNegativeFinite(wheel.HandBrakeResponse) || wheel.HandBrakeResponse > 1.0F ||
            !IsNonNegativeFinite(wheel.DriveTorqueRatio) || wheel.DriveTorqueRatio > 1.0F)
        {
            return Fail(
                PhysxPageError.Value,
                PhysxPageSection.VehicleWheels,
                wheelIndex,
                0,
                Describe(PhysxPageSection.VehicleWheels, wheelIndex) +
                    " declares a command response outside the zero to one range.");
        }

        return null;
    }

    private static PhysxPageValidationResult? RequireIdentity(
        HashSet<ulong> identifiers,
        ulong id,
        PhysxPageSection section,
        uint index) =>
        identifiers.Contains(id)
            ? null
            : Fail(
                PhysxPageError.Reference,
                section,
                index,
                0,
                Describe(section, index) + " uses an identity that is missing from the identity table.");

    private static PhysxPageValidationResult Fail(
        PhysxPageError code,
        PhysxPageSection section,
        uint elementIndex,
        ulong byteOffset,
        string message) =>
        PhysxPageValidationResult.Failure(code, section, elementIndex, byteOffset, message);

    private static string Describe(PhysxPageSection section, uint index)
    {
        int position = (int)section;
        string name = position < SectionNames.Length ? SectionNames[position] : "unknown";
        return string.Create(CultureInfo.InvariantCulture, $"{name} record {index}");
    }

    private static bool IsPositiveFinite(double value) => double.IsFinite(value) && value > 0.0;

    private static bool IsPositiveFinite(float value) => float.IsFinite(value) && value > 0.0F;

    private static bool IsNonNegativeFinite(float value) => float.IsFinite(value) && value >= 0.0F;

    /// <summary>
    /// Accepts the optional principal axis rotation, where an all zero quaternion stands for the
    /// identity rotation exactly as the native page validator reads it.
    /// </summary>
    private static bool IsUnsetOrUsableRotation(PhysxQuatf value) =>
        (value.X == 0.0F && value.Y == 0.0F && value.Z == 0.0F && value.W == 0.0F) ||
        value.IsUsableRotation;
}
