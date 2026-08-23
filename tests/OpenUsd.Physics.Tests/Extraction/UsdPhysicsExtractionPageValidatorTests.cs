// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Physics.Extraction;

namespace OpenUsd.Physics.Tests.Extraction;

public sealed class UsdPhysicsExtractionPageValidatorTests
{
    private static byte[] CanonicalPage()
    {
        var fixture = new UsdPhysicsExtractionPageFixture
        {
            MetersPerUnit = 0.01,
            KilogramsPerUnit = 1.0,
            UpAxis = 1,
            DefaultSceneIndex = 0,
        };

        fixture.AddObject(
            7,
            "/Scene",
            "Scene",
            UsdPhysicsExtractionObjectKind.Scene,
            UsdPhysicsExtractionDomains.Scene,
            UsdPhysicsExtractionObjectTraits.Enabled);
        int body = fixture.AddObject(
            9,
            "/Body",
            "Body",
            UsdPhysicsExtractionObjectKind.RigidBody,
            UsdPhysicsExtractionDomains.RigidBody,
            UsdPhysicsExtractionObjectTraits.Enabled | UsdPhysicsExtractionObjectTraits.Dynamic);

        fixture.AddNumber(1.0);
        fixture.AddNumber(2.0);
        fixture.AddNumber(3.0);
        fixture.AddText("convex-hull");
        fixture.AddProperty(
            UsdPhysicsExtractionKey.MassMass,
            "physics:mass",
            UsdPhysicsExtractionValueKind.Real,
            UsdPhysicsExtractionSource.Standard,
            12.5);
        fixture.AddProperty(
            UsdPhysicsExtractionKey.BodyVelocity,
            "physics:velocity",
            UsdPhysicsExtractionValueKind.Vector3,
            UsdPhysicsExtractionSource.Standard,
            0.0,
            valueStart: 0,
            valueCount: 3);
        fixture.AddTarget(7, "/Scene", 0);
        fixture.AddRelationship(
            UsdPhysicsExtractionKey.SimulationOwnerTargets, "physics:simulationOwner", 0, 1);
        fixture.SetObjectRange(body, 0, 2, 0, 1);

        fixture.AddPoint(0f, 0f, 0f);
        fixture.AddPoint(1f, 0f, 0f);
        fixture.AddPoint(0f, 1f, 0f);
        fixture.AddIndex(0);
        fixture.AddIndex(1);
        fixture.AddIndex(2);
        fixture.SetObjectGeometry(body, 0, 3, 0, 3);
        fixture.SetObjectTransform(body, (1, 2, 3), (1, 0, 0, 0), (0.5, 0.5, 0.5));

        fixture.AddDiagnostic(
            UsdPhysicsExtractionSeverity.Information,
            UsdPhysicsExtractionCategory.Units,
            UsdPhysicsExtractionCode.UpAxisConverted,
            -1,
            "The stage up axis was converted.");

        return fixture.Build();
    }

    private static async Task RejectsAsync(Action<byte[]> corrupt, string because)
    {
        byte[] page = CanonicalPage();
        corrupt(page);
        await Assert.That(() => UsdPhysicsExtractionPage.Create(page))
            .Throws<UsdPhysicsExtractionException>()
            .Because(because);
    }

    [Test]
    public async Task CanonicalPageIsAccepted()
    {
        UsdPhysicsExtractionPage page = UsdPhysicsExtractionPage.Create(CanonicalPage());

        await Assert.That(page.ObjectCount).IsEqualTo(2);
        await Assert.That(page.PropertyCount).IsEqualTo(2);
        await Assert.That(page.RelationshipCount).IsEqualTo(1);
        await Assert.That(page.TargetCount).IsEqualTo(1);
        await Assert.That(page.DiagnosticCount).IsEqualTo(1);
        await Assert.That(page.NumberCount).IsEqualTo(3);
        await Assert.That(page.TextCount).IsEqualTo(1);
        await Assert.That(page.PointCount).IsEqualTo(3);
        await Assert.That(page.IndexCount).IsEqualTo(3);
        await Assert.That(page.MetersPerUnit).IsEqualTo(0.01).Within(1e-12);
        await Assert.That(page.UpAxis).IsEqualTo(UsdPhysicsExtractionUpAxis.Y);
        await Assert.That(page.DefaultSceneIndex).IsEqualTo(0);
    }

    [Test]
    public async Task ShortBufferIsRejected() =>
        await Assert.That(() => UsdPhysicsExtractionPage.Create(new byte[8]))
            .Throws<UsdPhysicsExtractionException>();

    [Test]
    public async Task WrongMagicIsRejected() =>
        await RejectsAsync(
            page => BinaryPrimitives.WriteUInt64LittleEndian(page.AsSpan(0), 1234UL),
            "a page must declare the extraction magic");

    [Test]
    public async Task WrongAbiVersionIsRejected() =>
        await RejectsAsync(
            page => BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(8), 99u),
            "a page must declare the ABI version the reader mirrors");

    [Test]
    public async Task WrongHeaderSizeIsRejected() =>
        await RejectsAsync(
            page => BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(12), 64u),
            "a page must declare the header size the reader mirrors");

    [Test]
    public async Task DeclaredSizeMismatchIsRejected() =>
        await RejectsAsync(
            page => BinaryPrimitives.WriteUInt64LittleEndian(page.AsSpan(16), 40960UL),
            "the declared size must match the buffer");

    [Test]
    public async Task UnalignedBufferIsRejected()
    {
        byte[] page = CanonicalPage();
        var padded = new byte[page.Length + 1];
        page.CopyTo(padded, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(padded.AsSpan(16), (ulong)padded.Length);

        await Assert.That(() => UsdPhysicsExtractionPage.Create(padded))
            .Throws<UsdPhysicsExtractionException>();
    }

    [Test]
    public async Task NonPositiveUnitsAreRejected() =>
        await RejectsAsync(
            page => BinaryPrimitives.WriteDoubleLittleEndian(page.AsSpan(40), 0.0),
            "metersPerUnit must be a positive finite number");

    [Test]
    public async Task NonFiniteTimeCodeIsRejected() =>
        await RejectsAsync(
            page => BinaryPrimitives.WriteDoubleLittleEndian(page.AsSpan(56), double.NaN),
            "timeCodesPerSecond must be finite");

    [Test]
    public async Task UnknownUpAxisIsRejected() =>
        await RejectsAsync(
            page => BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(88), 7u),
            "the up axis must be Y, Z or X");

    [Test]
    public async Task DefaultSceneIndexOutOfRangeIsRejected() =>
        await RejectsAsync(
            page => BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(96), 12),
            "the default scene must exist");

    [Test]
    public async Task SectionOffsetInsideTheHeaderIsRejected() =>
        await RejectsAsync(
            page => BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(104), 8u),
            "a section may not start inside the header");

    [Test]
    public async Task UnalignedSectionOffsetIsRejected() =>
        await RejectsAsync(
            page => BinaryPrimitives.WriteUInt32LittleEndian(
                page.AsSpan(104 + (PhysicsExtractAbi.SectionObjects * 8)),
                ReadOffset(page, PhysicsExtractAbi.SectionObjects) + 1),
            "a section must start on the page alignment");

    [Test]
    public async Task SectionPastTheEndOfThePageIsRejected() =>
        await RejectsAsync(
            page => BinaryPrimitives.WriteUInt32LittleEndian(
                page.AsSpan(104 + (PhysicsExtractAbi.SectionObjects * 8) + 4), 4096u),
            "a section may not run past the page");

    [Test]
    public async Task CountAboveCapacityIsRejected() =>
        await RejectsAsync(
            page => BinaryPrimitives.WriteUInt32LittleEndian(
                page.AsSpan(104 + (PhysicsExtractAbi.SectionDiagnostics * 8) + 4),
                (uint)PhysicsExtractAbi.MaxDiagnostics + 1u),
            "a section may not exceed its capacity");

    [Test]
    public async Task OverlappingSectionsAreRejected() =>
        await RejectsAsync(
            page => BinaryPrimitives.WriteUInt32LittleEndian(
                page.AsSpan(104 + (PhysicsExtractAbi.SectionProperties * 8)),
                ReadOffset(page, PhysicsExtractAbi.SectionObjects)),
            "two sections may not share bytes");

    [Test]
    public async Task UnterminatedStringSectionIsRejected() =>
        await RejectsAsync(
            page =>
            {
                uint offset = ReadOffset(page, PhysicsExtractAbi.SectionStrings);
                uint count = ReadCount(page, PhysicsExtractAbi.SectionStrings);
                page[offset + count - 1] = (byte)'x';
            },
            "the string section must end with a terminator");

    [Test]
    public async Task NonEmptyFirstStringByteIsRejected() =>
        await RejectsAsync(
            page => page[ReadOffset(page, PhysicsExtractAbi.SectionStrings)] = (byte)'x',
            "offset zero must resolve to the empty string");

    [Test]
    public async Task ObjectPropertyRangeOutOfBoundsIsRejected() =>
        await RejectsAsync(
            page =>
            {
                uint objects = ReadOffset(page, PhysicsExtractAbi.SectionObjects);
                int record = (int)objects + PhysicsExtractAbi.ObjectRecordBytes;
                BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(record + 64), 99u);
            },
            "an object may not name properties that do not exist");

    [Test]
    public async Task ObjectRelationshipRangeOutOfBoundsIsRejected() =>
        await RejectsAsync(
            page =>
            {
                uint objects = ReadOffset(page, PhysicsExtractAbi.SectionObjects);
                int record = (int)objects + PhysicsExtractAbi.ObjectRecordBytes;
                BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(record + 68), 5u);
            },
            "an object may not name relationships that do not exist");

    [Test]
    public async Task TriangleIndexPastThePointCountIsRejected() =>
        await RejectsAsync(
            page =>
            {
                uint indices = ReadOffset(page, PhysicsExtractAbi.SectionIndices);
                BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan((int)indices), 64u);
            },
            "a triangle index must name a point of the same object");

    [Test]
    public async Task PropertyValueRangeOutOfBoundsIsRejected() =>
        await RejectsAsync(
            page =>
            {
                uint properties = ReadOffset(page, PhysicsExtractAbi.SectionProperties);
                int record = (int)properties + PhysicsExtractAbi.PropertyRecordBytes;
                BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(record + 24), 99u);
            },
            "a property may not name numbers that do not exist");

    [Test]
    public async Task RelationshipTargetRangeOutOfBoundsIsRejected() =>
        await RejectsAsync(
            page =>
            {
                uint relationships = ReadOffset(page, PhysicsExtractAbi.SectionRelationships);
                BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan((int)relationships + 12), 9u);
            },
            "a relationship may not name targets that do not exist");

    [Test]
    public async Task TargetObjectIndexOutOfBoundsIsRejected() =>
        await RejectsAsync(
            page =>
            {
                uint targets = ReadOffset(page, PhysicsExtractAbi.SectionTargets);
                BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan((int)targets + 12), 17);
            },
            "a resolved target must name an object of this page");

    [Test]
    public async Task DiagnosticObjectIndexOutOfBoundsIsRejected() =>
        await RejectsAsync(
            page =>
            {
                uint diagnostics = ReadOffset(page, PhysicsExtractAbi.SectionDiagnostics);
                BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan((int)diagnostics + 12), 42);
            },
            "a diagnostic must name an object of this page");

    [Test]
    public async Task StringOffsetPastTheSectionIsRejected() =>
        await RejectsAsync(
            page =>
            {
                uint objects = ReadOffset(page, PhysicsExtractAbi.SectionObjects);
                BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan((int)objects + 24), 9999u);
            },
            "a string offset must stay inside the string section");

    private static uint ReadOffset(byte[] page, int section) =>
        BinaryPrimitives.ReadUInt32LittleEndian(
            page.AsSpan(PhysicsExtractAbi.OffsetSpans + (section * 8)));

    private static uint ReadCount(byte[] page, int section) =>
        BinaryPrimitives.ReadUInt32LittleEndian(
            page.AsSpan(PhysicsExtractAbi.OffsetSpans + (section * 8) + 4));
}
