// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Interop;

public sealed class PhysxPageBuilderTests
{
    [Test]
    public async Task BuiltPageRoundTripsThroughTheReader()
    {
        using PhysxBuildPage page = PhysxPageFixture.CreatePage();
        PageSummary summary = Summarize(page);

        await Assert.That(summary.Magic).IsEqualTo(PhysxAbi.PageMagic);
        await Assert.That(summary.AbiVersion).IsEqualTo(PhysxAbi.Version);
        await Assert.That(summary.HeaderSize).IsEqualTo((uint)PhysxAbi.RecordSizes.BuildPageHeader);
        await Assert.That(summary.ByteSize).IsEqualTo((ulong)page.ByteLength);
        await Assert.That(summary.Revision).IsEqualTo(PhysxPageFixture.FixtureRevision);
        await Assert.That(summary.SourceHash).IsEqualTo(PhysxPageFixture.FixtureSourceHash);
        await Assert.That(summary.UpAxis).IsEqualTo((uint)PhysxUpAxis.Z);
        await Assert.That(summary.SimulationRateHz).IsEqualTo(60u);
        await Assert.That(summary.MaxSubsteps).IsEqualTo(4u);

        await Assert.That(summary.SceneCount).IsEqualTo(1);
        await Assert.That(summary.MaterialCount).IsEqualTo(2);
        await Assert.That(summary.ShapeCount).IsEqualTo(3);
        await Assert.That(summary.ActorCount).IsEqualTo(3);
        await Assert.That(summary.ActorShapeCount).IsEqualTo(3);
        await Assert.That(summary.JointCount).IsEqualTo(1);
        await Assert.That(summary.FilterPairCount).IsEqualTo(1);
        await Assert.That(summary.MeshPointCount).IsEqualTo(PhysxPageFixture.HullPoints.Length);
        await Assert.That(summary.MeshIndices.SequenceEqual(PhysxPageFixture.HullIndices)).IsTrue();

        uint[] expectedShapeTypes =
        [
            (uint)PhysxShapeType.Plane,
            (uint)PhysxShapeType.Box,
            (uint)PhysxShapeType.ConvexMesh
        ];
        uint[] expectedActorTypes =
        [
            (uint)PhysxActorType.Static,
            (uint)PhysxActorType.Dynamic,
            (uint)PhysxActorType.Kinematic
        ];
        await Assert.That(summary.ShapeTypes.SequenceEqual(expectedShapeTypes)).IsTrue();
        await Assert.That(summary.ActorTypes.SequenceEqual(expectedActorTypes)).IsTrue();
    }

    [Test]
    public async Task IdentitiesRoundTripIncludingNonAsciiPaths()
    {
        using PhysxBuildPage page = PhysxPageFixture.CreatePage();
        PageSummary summary = Summarize(page);

        await Assert.That(summary.Paths.Contains(PhysxPageFixture.BoxPath)).IsTrue();
        await Assert.That(summary.Paths.Contains(PhysxPageFixture.BoxColliderPath)).IsTrue();
        await Assert.That(summary.Paths.Contains(PhysxPageFixture.UnicodeMaterialPath)).IsTrue();
        await Assert.That(summary.Paths.Length).IsEqualTo(summary.IdentityCount);

        for (int index = 0; index < summary.Paths.Length; index++)
        {
            await Assert.That(summary.Identities[index]).IsEqualTo(
                PhysxIdentity.Compute(summary.Paths[index], PhysxInstanceDomain.Prim, 0));
        }
    }

    [Test]
    public async Task EverySectionOffsetIsEightByteAlignedAndOrdered()
    {
        using PhysxBuildPage page = PhysxPageFixture.CreatePage();
        PageSummary summary = Summarize(page);

        uint previous = (uint)PhysxAbi.RecordSizes.BuildPageHeader;
        foreach (uint offset in summary.SectionOffsets)
        {
            await Assert.That(offset % PhysxAbi.PageAlignment).IsEqualTo(0u);
            await Assert.That(offset >= previous).IsTrue();
            previous = offset;
        }

        await Assert.That(page.ByteLength % (int)PhysxAbi.PageAlignment).IsEqualTo(0);
    }

    [Test]
    public async Task ValidationSummaryDescribesTheStagedRecords()
    {
        using PhysxBuildPage page = PhysxPageFixture.CreatePage();
        PhysxPageValidation validation = page.Validation;

        await Assert.That(validation.ErrorCode).IsEqualTo((uint)PhysxPageError.None);
        await Assert.That(validation.Revision).IsEqualTo(PhysxPageFixture.FixtureRevision);
        await Assert.That(validation.SourceHash).IsEqualTo(PhysxPageFixture.FixtureSourceHash);
        await Assert.That(validation.SceneCount).IsEqualTo(1u);
        await Assert.That(validation.MaterialCount).IsEqualTo(2u);
        await Assert.That(validation.ShapeCount).IsEqualTo(3u);
        await Assert.That(validation.ActorCount).IsEqualTo(3u);
        await Assert.That(validation.DynamicActorCount).IsEqualTo(2u);
        await Assert.That(validation.JointCount).IsEqualTo(1u);
        await Assert.That(validation.FilterPairCount).IsEqualTo(1u);
    }

    [Test]
    public async Task AnUnsetPrincipalAxisFrameIsWrittenOutAsTheIdentity()
    {
        using PhysxBuildPage page = PhysxPageFixture.CreatePage();
        PhysxQuatf axes = ReadFirstPrincipalAxes(page);

        // No fixture actor states the frame, so the builder must state it for them.
        await Assert.That(axes.W).IsEqualTo(1.0F);
        await Assert.That(axes.X).IsEqualTo(0.0F);
        await Assert.That(axes.Y).IsEqualTo(0.0F);
        await Assert.That(axes.Z).IsEqualTo(0.0F);
    }

    private static PhysxQuatf ReadFirstPrincipalAxes(PhysxBuildPage page)
    {
        PhysxPageReader reader = page.CreateReader();
        return reader.Actors[0].PrincipalAxes;
    }

    [Test]
    public async Task ResultCapacitiesAreFixedAtBuildTime()
    {
        using PhysxBuildPage page = PhysxPageFixture.CreatePage();
        PhysxResultCapacities capacities = page.Capacities;

        await Assert.That(capacities.MaxBodyStates).IsEqualTo(2u);
        await Assert.That(capacities.MaxEvents > 0u).IsTrue();
        await Assert.That(capacities.MaxDiagnostics > 0u).IsTrue();
        await Assert.That(capacities.MaxQueryHits > 0u).IsTrue();
        await Assert.That(capacities.Reserved0).IsEqualTo(0u);
        await Assert.That(capacities.MaxDeformationBodies).IsEqualTo(0u);
        await Assert.That(capacities.MaxDeformationPoints).IsEqualTo(0u);
    }

    [Test]
    public async Task ExplicitCapacityOverridesAreHonored()
    {
        using PhysxPageBuilder builder = PhysxPageFixture.CreateBuilder();
        builder.BodyStateCapacity = 64;
        builder.EventCapacity = 128;
        builder.DiagnosticCapacity = 16;
        builder.DebugLineCapacity = 32;
        builder.QueryHitCapacity = 8;

        using PhysxBuildPage page = builder.Build();
        PhysxResultCapacities capacities = page.Capacities;

        await Assert.That(capacities.MaxBodyStates).IsEqualTo(64u);
        await Assert.That(capacities.MaxEvents).IsEqualTo(128u);
        await Assert.That(capacities.MaxDiagnostics).IsEqualTo(16u);
        await Assert.That(capacities.MaxDebugLines).IsEqualTo(32u);
        await Assert.That(capacities.MaxQueryHits).IsEqualTo(8u);
    }

    [Test]
    public async Task EmptyPageBuildsWithZeroedSections()
    {
        using var builder = new PhysxPageBuilder();
        using PhysxBuildPage page = builder.Build();
        PageSummary summary = Summarize(page);

        await Assert.That(page.ByteLength).IsEqualTo(PhysxAbi.RecordSizes.BuildPageHeader);
        await Assert.That(summary.IdentityCount).IsEqualTo(0);
        await Assert.That(summary.SceneCount).IsEqualTo(0);
        await Assert.That(summary.ActorCount).IsEqualTo(0);
        foreach (uint offset in summary.SectionOffsets)
        {
            await Assert.That(offset).IsEqualTo(0u);
        }
    }

    [Test]
    public async Task LeaseExposesAnEightByteAlignedPage()
    {
        using PhysxBuildPage page = PhysxPageFixture.CreatePage();
        (ulong address, int length) = pin(page);

        await Assert.That(address % PhysxAbi.PageAlignment).IsEqualTo(0UL);
        await Assert.That(length).IsEqualTo(page.ByteLength);

        static (ulong Address, int Length) pin(PhysxBuildPage page)
        {
            using PhysxPageLease lease = page.Lease();
            return ((ulong)lease.Address, lease.ByteLength);
        }
    }

    [Test]
    public async Task DisposedPageRejectsFurtherAccess()
    {
        PhysxBuildPage page = PhysxPageFixture.CreatePage();
        page.Dispose();

        await Assert.That(() => _ = page.Bytes.Length).Throws<ObjectDisposedException>();
        await Assert.That(() => _ = page.Lease()).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task TryBuildReportsTheFirstInvalidRecordInsteadOfProducingAPage()
    {
        using var builder = new PhysxPageBuilder();
        builder.AddScene(new PhysxSceneDesc
        {
            Id = builder.DefineIdentity(PhysxPageFixture.ScenePath),
            GravityDirection = new PhysxVec3f(0.0F, 0.0F, -1.0F),
            GravityMagnitude = 981.0F,
            PositionIterations = 4,
            VelocityIterations = 1,
            ContactOffset = 0.02F
        });
        builder.AddShape(new PhysxShapeDesc
        {
            Id = builder.DefineIdentity(PhysxPageFixture.BoxColliderPath),
            Type = (uint)PhysxShapeType.Sphere,
            LocalPose = PhysxTransform.Identity,
            Scale = new PhysxVec3f(1.0F, 1.0F, 1.0F),
            Radius = 0.0F,
            MaterialIndex = -1
        });

        bool built = builder.TryBuild(out PhysxBuildPage? page, out PhysxPageValidationResult result);

        await Assert.That(built).IsFalse();
        await Assert.That(page).IsNull();
        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.ErrorCode).IsEqualTo(PhysxPageError.Value);
        await Assert.That(result.Section).IsEqualTo(PhysxPageSection.Shapes);
        await Assert.That(result.Message).IsNotNull();
        await Assert.That(() => builder.Build()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task BuilderRejectsUseAfterDispose()
    {
        var builder = new PhysxPageBuilder();
        builder.Dispose();

        await Assert.That(() => builder.DefineIdentity("/World/Box")).Throws<ObjectDisposedException>();
        await Assert.That(() => builder.Build()).Throws<ObjectDisposedException>();
    }

    private static PageSummary Summarize(PhysxBuildPage page)
    {
        PhysxPageReader reader = page.CreateReader();
        PhysxBuildPageHeader header = reader.Header;

        var paths = new string[reader.Identities.Length];
        var identities = new ulong[reader.Identities.Length];
        for (int index = 0; index < reader.Identities.Length; index++)
        {
            paths[index] = reader.GetPath(in reader.Identities[index]);
            identities[index] = reader.Identities[index].Id;
        }

        var shapeTypes = new uint[reader.Shapes.Length];
        for (int index = 0; index < reader.Shapes.Length; index++)
        {
            shapeTypes[index] = reader.Shapes[index].Type;
        }

        var actorTypes = new uint[reader.Actors.Length];
        for (int index = 0; index < reader.Actors.Length; index++)
        {
            actorTypes[index] = reader.Actors[index].Type;
        }

        return new PageSummary(
            header.Magic,
            header.AbiVersion,
            header.HeaderSize,
            header.ByteSize,
            header.Revision,
            header.SourceHash,
            header.UpAxis,
            header.SimulationRateHz,
            header.MaxSubsteps,
            paths,
            identities,
            shapeTypes,
            actorTypes,
            reader.Scenes.Length,
            reader.Materials.Length,
            reader.Actors.Length,
            reader.ActorShapes.Length,
            reader.Joints.Length,
            reader.FilterPairs.Length,
            reader.MeshPoints.Length,
            reader.MeshIndices.ToArray(),
            [
                header.StringBytes.Offset,
                header.Identities.Offset,
                header.Scenes.Offset,
                header.Materials.Offset,
                header.Shapes.Offset,
                header.Actors.Offset,
                header.ActorShapes.Offset,
                header.Joints.Offset,
                header.FilterPairs.Offset,
                header.MeshPoints.Offset,
                header.MeshIndices.Offset
            ]);
    }

    private sealed record PageSummary(
        ulong Magic,
        uint AbiVersion,
        uint HeaderSize,
        ulong ByteSize,
        ulong Revision,
        ulong SourceHash,
        uint UpAxis,
        uint SimulationRateHz,
        uint MaxSubsteps,
        string[] Paths,
        ulong[] Identities,
        uint[] ShapeTypes,
        uint[] ActorTypes,
        int SceneCount,
        int MaterialCount,
        int ActorCount,
        int ActorShapeCount,
        int JointCount,
        int FilterPairCount,
        int MeshPointCount,
        uint[] MeshIndices,
        uint[] SectionOffsets)
    {
        internal int IdentityCount => Identities.Length;

        internal int ShapeCount => ShapeTypes.Length;
    }
}
