// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Interop;

public sealed class PhysxPageValidatorTests
{
    private const int MagicOffset = 0;
    private const int AbiOffset = 8;
    private const int HeaderSizeOffset = 12;
    private const int ByteSizeOffset = 16;
    private const int MetersPerUnitOffset = 40;
    private const int StartTimeCodeOffset = 64;
    private const int EndTimeCodeOffset = 72;
    private const int UpAxisOffset = 80;
    private const int SimulationRateOffset = 88;
    private const int MaxSubstepsOffset = 92;
    private const int ScenesSpanOffset = 112;
    private const int MaterialsSpanOffset = 120;
    private const int CapacitiesOffset = 296;
    private const int ReservedOffset = 328;

    /// <summary>Where the principal axis rotation sits inside one actor record.</summary>
    private const int PrincipalAxesOffset = 96;

    [Test]
    public async Task CanonicalPageIsValid()
    {
        byte[] page = PhysxPageFixture.CreatePageBytes();
        PhysxPageValidationResult result = PhysxPageValidator.Validate(page);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.ErrorCode).IsEqualTo(PhysxPageError.None);
        await Assert.That(result.Message).IsNull();
        await Assert.That(result.Validation.DynamicActorCount).IsEqualTo(2u);
    }

    [Test]
    public async Task EmptyBufferIsRejectedAsNull()
    {
        await AssertFails([], PhysxPageError.Null);
    }

    [Test]
    public async Task MisalignedPageAddressIsRejected()
    {
        byte[] page = PhysxPageFixture.CreatePageBytes();
        PhysxPageValidationResult result = PhysxPageValidator.Validate(page, (nuint)0x1004);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.ErrorCode).IsEqualTo(PhysxPageError.Alignment);
    }

    [Test]
    public async Task TruncatedPageIsRejectedBySize()
    {
        byte[] page = PhysxPageFixture.CreatePageBytes();
        await AssertFails(page[..64], PhysxPageError.Size);
    }

    [Test]
    public async Task DeclaredByteSizeMustMatchTheBuffer()
    {
        await AssertFails(
            Patch(page => BinaryPrimitives.WriteUInt64LittleEndian(
                page.AsSpan(ByteSizeOffset),
                (ulong)page.Length + 8)),
            PhysxPageError.Size);
    }

    [Test]
    public async Task WrongMagicIsRejected()
    {
        await AssertFails(
            Patch(page => BinaryPrimitives.WriteUInt64LittleEndian(page.AsSpan(MagicOffset), 0xDEADBEEFUL)),
            PhysxPageError.Magic);
    }

    [Test]
    public async Task DifferentAbiVersionIsRejected()
    {
        await AssertFails(
            Patch(page => BinaryPrimitives.WriteUInt32LittleEndian(
                page.AsSpan(AbiOffset),
                PhysxAbi.Version + 1)),
            PhysxPageError.Abi);
    }

    [Test]
    public async Task DifferentHeaderSizeIsRejected()
    {
        await AssertFails(
            Patch(page => BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(HeaderSizeOffset), 240)),
            PhysxPageError.HeaderSize);
    }

    [Test]
    public async Task NonPositiveUnitScaleIsRejected()
    {
        await AssertFails(
            Patch(page => BinaryPrimitives.WriteDoubleLittleEndian(page.AsSpan(MetersPerUnitOffset), 0.0)),
            PhysxPageError.Value);
    }

    [Test]
    public async Task UnorderedTimeRangeIsRejected()
    {
        await AssertFails(
            Patch(page =>
            {
                BinaryPrimitives.WriteDoubleLittleEndian(page.AsSpan(StartTimeCodeOffset), 10.0);
                BinaryPrimitives.WriteDoubleLittleEndian(page.AsSpan(EndTimeCodeOffset), 0.0);
            }),
            PhysxPageError.Value);
    }

    [Test]
    public async Task UnknownUpAxisIsRejected()
    {
        await AssertFails(
            Patch(page => BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(UpAxisOffset), 3)),
            PhysxPageError.Value);
    }

    [Test]
    public async Task SimulationRateOutsideTheSupportedRangeIsRejected()
    {
        await AssertFails(
            Patch(page => BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(SimulationRateOffset), 10)),
            PhysxPageError.Value);
        await AssertFails(
            Patch(page => BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(SimulationRateOffset), 1000)),
            PhysxPageError.Value);
    }

    [Test]
    public async Task SubstepLimitOutsideTheSupportedRangeIsRejected()
    {
        await AssertFails(
            Patch(page => BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(MaxSubstepsOffset), 0)),
            PhysxPageError.Value);
        await AssertFails(
            Patch(page => BinaryPrimitives.WriteUInt32LittleEndian(
                page.AsSpan(MaxSubstepsOffset),
                PhysxAbi.MaxSubsteps + 1)),
            PhysxPageError.Value);
    }

    [Test]
    public async Task NonZeroReservedHeaderFieldsAreRejected()
    {
        await AssertFails(
            Patch(page => BinaryPrimitives.WriteUInt64LittleEndian(page.AsSpan(ReservedOffset), 1)),
            PhysxPageError.Value);
    }

    [Test]
    public async Task MisalignedSectionOffsetIsRejected()
    {
        await AssertFails(
            Patch(page =>
            {
                uint offset = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(ScenesSpanOffset));
                BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(ScenesSpanOffset), offset + 4);
            }),
            PhysxPageError.Alignment);
    }

    [Test]
    public async Task SectionOutsideThePageIsRejected()
    {
        await AssertFails(
            Patch(page => BinaryPrimitives.WriteUInt32LittleEndian(
                page.AsSpan(ScenesSpanOffset),
                (uint)page.Length)),
            PhysxPageError.Range);
    }

    [Test]
    public async Task OverlappingSectionsAreRejected()
    {
        await AssertFails(
            Patch(page =>
            {
                uint offset = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(ScenesSpanOffset));
                BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(MaterialsSpanOffset), offset);
            }),
            PhysxPageError.Overlap);
    }

    [Test]
    public async Task SectionCountAboveTheSupportedLimitIsRejected()
    {
        await AssertFails(
            Patch(page => BinaryPrimitives.WriteUInt32LittleEndian(
                page.AsSpan(ScenesSpanOffset + 4),
                PhysxAbi.MaxScenes + 1)),
            PhysxPageError.CountLimit);
    }

    [Test]
    public async Task InvalidUtf8InTheStringSectionIsRejected()
    {
        await AssertFails(
            Patch(page =>
            {
                PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(page);
                page[header.StringBytes.Offset + 1] = 0xFF;
            }),
            PhysxPageError.Encoding);
    }

    [Test]
    public async Task EmbeddedNullBytesInTheStringSectionAreRejected()
    {
        await AssertFails(
            Patch(page =>
            {
                PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(page);
                page[header.StringBytes.Offset + 1] = 0x00;
            }),
            PhysxPageError.Encoding);
    }

    [Test]
    public async Task IdentityThatIsNotDerivedFromItsAddressIsRejected()
    {
        await AssertFails(
            Patch(page =>
            {
                PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(page);
                BinaryPrimitives.WriteUInt64LittleEndian(
                    page.AsSpan((int)header.Identities.Offset),
                    0x1234567890ABCDEFUL);
            }),
            PhysxPageError.Value);
    }

    [Test]
    public async Task DuplicateIdentitiesAreRejected()
    {
        await AssertFails(
            Patch(page =>
            {
                PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(page);
                int first = (int)header.Identities.Offset;
                int second = first + PhysxAbi.RecordSizes.Identity;
                page.AsSpan(first, PhysxAbi.RecordSizes.Identity).CopyTo(page.AsSpan(second));
            }),
            PhysxPageError.DuplicateId);
    }

    [Test]
    public async Task RecordThatReferencesAnUnknownIdentityIsRejected()
    {
        await AssertFails(
            Patch(page =>
            {
                PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(page);
                BinaryPrimitives.WriteUInt64LittleEndian(
                    page.AsSpan((int)header.Scenes.Offset),
                    0x0F0F0F0F0F0F0F0FUL);
            }),
            PhysxPageError.Reference);
    }

    [Test]
    public async Task ActorThatReferencesAMissingSceneIsRejected()
    {
        await AssertFails(
            Patch(page =>
            {
                PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(page);
                BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan((int)header.Actors.Offset + 8), 7);
            }),
            PhysxPageError.Reference);
    }

    [Test]
    public async Task ActorWithAnUnusablePrincipalAxisFrameIsRejected()
    {
        await AssertFails(
            Patch(page =>
            {
                PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(page);
                BinaryPrimitives.WriteSingleLittleEndian(
                    page.AsSpan((int)header.Actors.Offset + PrincipalAxesOffset), 5.0F);
            }),
            PhysxPageError.Value);
    }

    [Test]
    public async Task ActorWithAnUnsetPrincipalAxisFrameIsAccepted()
    {
        byte[] page = Patch(bytes =>
        {
            PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(bytes);
            bytes.AsSpan((int)header.Actors.Offset + PrincipalAxesOffset, 16).Clear();
        });

        PhysxPageValidationResult result = PhysxPageValidator.Validate(page);

        // A default initialized description carries an all zero rotation, which is the identity.
        await Assert.That(result.IsValid).IsTrue().Because(result.Message ?? "no message");
    }

    [Test]
    public async Task ActorWithAPrincipalAxisFrameThatIsNotUnitLengthIsAccepted()
    {
        byte[] page = Patch(bytes =>
        {
            PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(bytes);
            Span<byte> frame = bytes.AsSpan((int)header.Actors.Offset + PrincipalAxesOffset, 16);
            BinaryPrimitives.WriteSingleLittleEndian(frame, 0.0F);
            BinaryPrimitives.WriteSingleLittleEndian(frame[4..], 0.0F);
            BinaryPrimitives.WriteSingleLittleEndian(frame[8..], 0.9F);
            BinaryPrimitives.WriteSingleLittleEndian(frame[12..], 0.9F);
        });

        PhysxPageValidationResult result = PhysxPageValidator.Validate(page);

        // The contract accepts any rotation close enough to unit length for a consumer to
        // normalize it, so a legal quaternion never has to be replaced by the identity.
        await Assert.That(result.IsValid).IsTrue().Because(result.Message ?? "no message");
    }

    [Test]
    public async Task ActorShapeThatReferencesAMissingShapeIsRejected()
    {
        await AssertFails(
            Patch(page =>
            {
                PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(page);
                BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan((int)header.ActorShapes.Offset), 42);
            }),
            PhysxPageError.Reference);
    }

    [Test]
    public async Task MeshIndexOutsideItsOwnPointRangeIsRejected()
    {
        await AssertFails(
            Patch(page =>
            {
                PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(page);
                BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan((int)header.MeshIndices.Offset), 99);
            }),
            PhysxPageError.Reference);
    }

    [Test]
    public async Task NonFiniteMeshPointIsRejected()
    {
        await AssertFails(
            Patch(page =>
            {
                PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(page);
                BinaryPrimitives.WriteSingleLittleEndian(
                    page.AsSpan((int)header.MeshPoints.Offset),
                    float.NaN);
            }),
            PhysxPageError.Value);
    }

    [Test]
    public async Task ResultCapacityAboveTheSupportedMaximumIsRejected()
    {
        await AssertFails(
            Patch(page => BinaryPrimitives.WriteUInt32LittleEndian(
                page.AsSpan(CapacitiesOffset),
                PhysxAbi.MaxResultCapacity + 1)),
            PhysxPageError.Capacity);
    }

    [Test]
    public async Task ResultCapacitySmallerThanTheMovableActorCountIsRejected()
    {
        await AssertFails(
            Patch(page => BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(CapacitiesOffset), 1)),
            PhysxPageError.Capacity);
    }

    [Test]
    public async Task NonZeroReservedCapacityFieldsAreRejected()
    {
        await AssertFails(
            Patch(page => BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(CapacitiesOffset + 28), 1)),
            PhysxPageError.Value);
    }

    private static byte[] Patch(Action<byte[]> mutate)
    {
        byte[] page = PhysxPageFixture.CreatePageBytes();
        mutate(page);
        return page;
    }

    private static async Task AssertFails(byte[] page, PhysxPageError expected)
    {
        PhysxPageValidationResult result = PhysxPageValidator.Validate(page);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.ErrorCode).IsEqualTo(expected);
        await Assert.That(result.Message).IsNotNull();
        await Assert.That(result.Validation.ErrorCode).IsEqualTo((uint)expected);
    }

    // ---- Articulation / controller validator tests (ABI v5) ----

    [Test]
    public async Task ArticulationWithValidLinksPassesValidation()
    {
        byte[] page = CreateArticulationPageBytes();
        PhysxPageValidationResult result = PhysxPageValidator.Validate(page);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task ArticulationLinkWithNonFinitePositionIsRejected()
    {
        byte[] page = CreateArticulationPageBytes();
        PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(page);
        int linkOffset = (int)header.ArticulationLinks.Offset;
        // WorldPose.Position.X is at offset 16 in the link desc (after Id=8 + ParentId=8)
        BinaryPrimitives.WriteSingleLittleEndian(page.AsSpan(linkOffset + 16), float.NaN);

        PhysxPageValidationResult result = PhysxPageValidator.Validate(page);
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.ErrorCode).IsEqualTo(PhysxPageError.Value);
    }

    [Test]
    public async Task ArticulationRootLinkWithNonNoneJointTypeIsRejected()
    {
        byte[] page = CreateArticulationPageBytes();
        PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(page);
        int linkOffset = (int)header.ArticulationLinks.Offset;
        int jointTypeFieldOffset = GetArticulationLinkJointTypeOffset();
        BinaryPrimitives.WriteUInt32LittleEndian(
            page.AsSpan(linkOffset + jointTypeFieldOffset),
            (uint)PhysxArticulationJointType.Spherical);

        PhysxPageValidationResult result = PhysxPageValidator.Validate(page);
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task ArticulationWithTooManyIterationsIsRejected()
    {
        byte[] page = CreateArticulationPageBytes();
        PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(page);
        int articOffset = (int)header.Articulations.Offset;
        // PositionIterations is at offset 20 in PhysxArticulationDesc (Id=8 + SceneIndex=4 + Flags=4 + LinkOffset=4)
        // Actually: Id(8) + SceneIndex(4) + Flags(4) + LinkOffset(4) + LinkCount(4) = 24
        // PositionIterations is at offset 24
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(articOffset + 24), 256u);

        PhysxPageValidationResult result = PhysxPageValidator.Validate(page);
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.ErrorCode).IsEqualTo(PhysxPageError.Value);
    }

    [Test]
    public async Task ControllerWithValidFieldsPassesValidation()
    {
        byte[] page = CreateControllerPageBytes();
        PhysxPageValidationResult result = PhysxPageValidator.Validate(page);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task ControllerWithInvalidShapeIsRejected()
    {
        byte[] page = CreateControllerPageBytes();
        PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(page);
        int ctrlOffset = (int)header.Controllers.Offset;
        // Shape is at offset 12 in PhysxControllerDesc (Id=8, SceneIndex=4)
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(ctrlOffset + 12), 99u);

        PhysxPageValidationResult result = PhysxPageValidator.Validate(page);
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.ErrorCode).IsEqualTo(PhysxPageError.Value);
    }

    [Test]
    public async Task ControllerWithNonPositiveRadiusIsRejected()
    {
        byte[] page = CreateControllerPageBytes();
        PhysxBuildPageHeader header = PhysxPageFixture.ReadHeader(page);
        int ctrlOffset = (int)header.Controllers.Offset;
        // Radius is after Position(12) + UpDirection(12) = 24 from Shape field
        // Shape=12, Position=16 (3 floats at 16+12=28), UpDirection=28 (at 28+12=40), Radius at 40
        // Actually: Id(8) + SceneIndex(4) + Shape(4) + Position(12) + UpDirection(12) = 40, Radius at 40
        BinaryPrimitives.WriteSingleLittleEndian(page.AsSpan(ctrlOffset + 40), 0.0f);

        PhysxPageValidationResult result = PhysxPageValidator.Validate(page);
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.ErrorCode).IsEqualTo(PhysxPageError.Value);
    }

    private static int GetArticulationLinkJointTypeOffset()
    {
        // PhysxArticulationLinkDesc layout: Id(8) + ParentId(8) + WorldPose(28) + ParentFrame(28)
        // + ChildFrame(28) + CenterOfMass(12) + Inertia(12) + PrincipalAxes(16) + Mass(4)
        // + LinearDamping(4) + AngularDamping(4) + MaxLinearVelocity(4) + MaxAngularVelocity(4)
        // + JointFriction(4) + MaxJointVelocity(4) + JointType(4)
        // = 8+8+28+28+28+12+12+16+4+4+4+4+4+4+4 = 168
        return 168;
    }

    private static byte[] CreateArticulationPageBytes()
    {
        using var builder = new PhysxPageBuilder
        {
            MetersPerUnit = 1.0,
            KilogramsPerUnit = 1.0,
            TimeCodesPerSecond = 24.0,
            StartTimeCode = 0.0,
            EndTimeCode = 100.0,
            UpAxis = PhysxUpAxis.Y,
            SimulationRateHz = 60,
            MaxSubsteps = 4
        };

        builder.AddScene(new PhysxSceneDesc
        {
            Id = builder.DefineIdentity("/Scene"),
            GravityDirection = new PhysxVec3f(0, -1, 0),
            GravityMagnitude = 9.81f,
            PositionIterations = 4,
            VelocityIterations = 1,
            ContactOffset = 0.02f
        });

        var rootLink = new PhysxArticulationLinkDesc
        {
            Id = builder.DefineIdentity("/Art/Root"),
            ParentId = 0,
            WorldPose = PhysxTransform.Identity,
            ParentFrame = PhysxTransform.Identity,
            ChildFrame = PhysxTransform.Identity,
            Inertia = new PhysxVec3f(1, 1, 1),
            PrincipalAxes = PhysxQuatf.Identity,
            Mass = 1.0f,
            JointType = (uint)PhysxArticulationJointType.None,
        };
        builder.AddArticulationLink(in rootLink);

        var childLink = new PhysxArticulationLinkDesc
        {
            Id = builder.DefineIdentity("/Art/Child"),
            ParentId = rootLink.Id,
            WorldPose = new PhysxTransform(new PhysxVec3f(0, -1, 0), PhysxQuatf.Identity),
            ParentFrame = PhysxTransform.Identity,
            ChildFrame = PhysxTransform.Identity,
            Inertia = new PhysxVec3f(1, 1, 1),
            PrincipalAxes = PhysxQuatf.Identity,
            Mass = 1.0f,
            JointType = (uint)PhysxArticulationJointType.Spherical,
        };
        builder.AddArticulationLink(in childLink);

        builder.AddArticulation(new PhysxArticulationDesc
        {
            Id = builder.DefineIdentity("/Art"),
            SceneIndex = 0,
            Flags = 0,
            LinkOffset = 0,
            LinkCount = 2,
            PositionIterations = 16,
            VelocityIterations = 4,
            SleepThreshold = 0.005f,
            StabilizationThreshold = 0.001f,
        });

        using PhysxBuildPage page = builder.Build();
        return page.Bytes.ToArray();
    }

    private static byte[] CreateControllerPageBytes()
    {
        using var builder = new PhysxPageBuilder
        {
            MetersPerUnit = 1.0,
            KilogramsPerUnit = 1.0,
            TimeCodesPerSecond = 24.0,
            StartTimeCode = 0.0,
            EndTimeCode = 100.0,
            UpAxis = PhysxUpAxis.Y,
            SimulationRateHz = 60,
            MaxSubsteps = 4
        };

        builder.AddScene(new PhysxSceneDesc
        {
            Id = builder.DefineIdentity("/Scene"),
            GravityDirection = new PhysxVec3f(0, -1, 0),
            GravityMagnitude = 9.81f,
            PositionIterations = 4,
            VelocityIterations = 1,
            ContactOffset = 0.02f
        });

        builder.AddMaterial(new PhysxMaterialDesc
        {
            Id = builder.DefineIdentity("/Mat"),
            StaticFriction = 0.5f,
            DynamicFriction = 0.5f,
            Density = 1000.0f,
        });

        builder.AddController(new PhysxControllerDesc
        {
            Id = builder.DefineIdentity("/Controller"),
            SceneIndex = 0,
            Shape = (uint)PhysxControllerShape.Capsule,
            Position = new PhysxVec3f(0, 2, 0),
            UpDirection = new PhysxVec3f(0, 1, 0),
            Radius = 0.3f,
            Height = 1.0f,
            MaterialIndex = 0,
            ScaleCoefficient = 0.8f,
        });

        using PhysxBuildPage page = builder.Build();
        return page.Bytes.ToArray();
    }
}
