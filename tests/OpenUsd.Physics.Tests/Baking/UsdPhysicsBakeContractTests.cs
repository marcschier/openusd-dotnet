// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Baking;

namespace OpenUsd.Physics.Tests.Baking;

/// <summary>
/// Contract tests for the physics baking value types. These never touch the native runtime.
/// </summary>
public sealed class UsdPhysicsBakeContractTests
{
    [Test]
    public async Task BakeSpec_DefaultsToWholeStageRange()
    {
        var spec = new UsdPhysicsBakeSpec("bake.usda");

        await Assert.That(spec.StartTimeCode).IsNull();
        await Assert.That(spec.EndTimeCode).IsNull();
        await Assert.That(spec.SampleStride).IsNull();
        await Assert.That(spec.Save).IsTrue();
        await Assert.That(spec.Options).IsEqualTo(UsdPhysicsBakeOptions.Default);
    }

    [Test]
    public void BakeSpec_RejectsEmptyDestination()
    {
        Assert.Throws<ArgumentException>(() => _ = new UsdPhysicsBakeSpec("  "));
    }

    [Test]
    public void BakeSpec_RejectsInvertedRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new UsdPhysicsBakeSpec("bake.usda", 5, 1));
    }

    [Test]
    public void BakeSpec_RejectsNonPositiveStride()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new UsdPhysicsBakeSpec("bake.usda", 1, 5, 0));
    }

    [Test]
    public void BakeOptions_RejectsNonPositiveChunkSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = UsdPhysicsBakeOptions.Default with { ChunkSize = 0 });
    }

    [Test]
    public void PointSample_RejectsMismatchedVelocityCount()
    {
        Assert.Throws<ArgumentException>(() => _ = new UsdPhysicsPointSample(
            BakeFixture.ClothId,
            UsdPhysicsPointSampleDomain.Cloth,
            1,
            [new UsdVec3d(0, 0, 0), new UsdVec3d(1, 0, 0)],
            [new UsdVec3d(0, 0, 0)]));
    }

    [Test]
    public void PointSample_RejectsHalfSuppliedTopology()
    {
        Assert.Throws<ArgumentException>(() => _ = new UsdPhysicsPointSample(
            BakeFixture.ClothId,
            UsdPhysicsPointSampleDomain.Cloth,
            1,
            [new UsdVec3d(0, 0, 0)],
            default,
            [1],
            default));
    }

    [Test]
    public void PointSample_RejectsOutOfRangeFaceVertexIndex()
    {
        Assert.Throws<ArgumentException>(() => _ = new UsdPhysicsPointSample(
            BakeFixture.ClothId,
            UsdPhysicsPointSampleDomain.Cloth,
            1,
            [new UsdVec3d(0, 0, 0)],
            default,
            [1],
            [7]));
    }

    [Test]
    public async Task ResultBatch_CountsEveryRecord()
    {
        UsdPhysicsResultBatch batch = BakeFixture.CreateBatch(2, 1);

        await Assert.That(batch.RecordCount).IsEqualTo(2);
        await Assert.That(batch.TimeCode).IsEqualTo(2d);
        await Assert.That(batch.PointSamples.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Bindings_ExposeDeterministicOrder()
    {
        var bindings = new UsdPhysicsBakeBindings(
            3,
            [
                new UsdPhysicsBakeBinding(new UsdPhysicsObjectId(9), "/Zed"),
                new UsdPhysicsBakeBinding(new UsdPhysicsObjectId(2), "/Alpha")
            ]);

        await Assert.That(bindings.Bindings[0].PrimPath).IsEqualTo("/Alpha");
        await Assert.That(bindings.Bindings[1].PrimPath).IsEqualTo("/Zed");
        await Assert.That(bindings.IdentityRevision).IsEqualTo(3ul);
    }

    [Test]
    public void Bindings_RejectDuplicateIdentities()
    {
        Assert.Throws<ArgumentException>(() => _ = new UsdPhysicsBakeBindings(
            1,
            [
                new UsdPhysicsBakeBinding(new UsdPhysicsObjectId(4), "/A"),
                new UsdPhysicsBakeBinding(new UsdPhysicsObjectId(4), "/B")
            ]));
    }

    [Test]
    public async Task Bindings_MissingIdentityDoesNotResolve()
    {
        UsdPhysicsBakeBindings bindings = BakeFixture.CreateBindings();

        bool found = bindings.TryGetBinding(new UsdPhysicsObjectId(0xDEAD), out _);

        await Assert.That(found).IsFalse();
    }

    [Test]
    public async Task ProgressAndOutcome_AreDetachedValues()
    {
        var progress = new UsdPhysicsBakeProgress(1, 4, 2.5, 12);
        var outcome = new UsdPhysicsBakeRecordOutcome(
            BakeFixture.BodyId, UsdPhysicsBakeRecordStatus.InstanceProxy, 7);

        await Assert.That(progress).IsAssignableTo<IUsdDetachedResult>();
        await Assert.That(outcome).IsAssignableTo<IUsdDetachedResult>();
        await Assert.That(outcome.Detail).IsEqualTo(7);
    }
}

