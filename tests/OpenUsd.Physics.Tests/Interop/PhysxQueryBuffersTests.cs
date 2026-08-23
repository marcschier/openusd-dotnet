// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Interop;

public sealed class PhysxQueryBuffersTests
{
    [Test]
    public async Task RequestsAreBatchedUntilTheDeclaredCapacityIsReached()
    {
        using var buffers = new PhysxQueryBuffers(requestCapacity: 2, hitCapacity: 4);

        await Assert.That(buffers.TryAddRequest(CreateRequest(1))).IsTrue();
        await Assert.That(buffers.TryAddRequest(CreateRequest(2))).IsTrue();
        await Assert.That(buffers.TryAddRequest(CreateRequest(3))).IsFalse();
        await Assert.That(buffers.RequestCount).IsEqualTo(2);

        PhysxQueryDesc desc = buffers.CreateDesc();
        await Assert.That(desc.AbiVersion).IsEqualTo(PhysxAbi.Version);
        await Assert.That((int)desc.RequestCount).IsEqualTo(2);
        await Assert.That((int)desc.HitCapacity).IsEqualTo(4);
        await Assert.That(HasRequests(in desc)).IsTrue();
        await Assert.That(ReadRequestUserId(in desc, 1)).IsEqualTo(2UL);

        buffers.Clear();
        await Assert.That(buffers.RequestCount).IsEqualTo(0);
        PhysxQueryDesc cleared = buffers.CreateDesc();
        await Assert.That(HasRequests(in cleared)).IsFalse();
    }

    [Test]
    public async Task CaptureCopiesHitsAndSurvivesBufferReuse()
    {
        using var buffers = new PhysxQueryBuffers(requestCapacity: 1, hitCapacity: 2);
        buffers.TryAddRequest(CreateRequest(9));
        PhysxQueryDesc desc = buffers.CreateDesc();
        WriteHit(in desc, 0, new PhysxQueryHit
        {
            UserId = 9,
            ActorId = 21,
            ShapeId = 22,
            Position = new PhysxVec3f(1.0F, 2.0F, 3.0F),
            Normal = new PhysxVec3f(0.0F, 0.0F, 1.0F),
            Distance = 4.5F,
            Flags = (uint)(PhysxQueryHitFlags.HasPosition | PhysxQueryHitFlags.HasNormal |
                PhysxQueryHitFlags.HasDistance)
        });

        var info = new PhysxQueryResultInfo
        {
            HitCount = (nuint)1,
            DroppedHitCount = (nuint)6,
            RejectedRequestCount = (nuint)1,
            OverflowFlags = (uint)PhysxOverflowFlags.QueryHits
        };
        PhysxQueryCapture capture = buffers.Capture(in info);

        await Assert.That(capture.Hits.Length).IsEqualTo(1);
        await Assert.That(capture.DroppedHits).IsEqualTo(6u);
        await Assert.That(capture.RejectedRequests).IsEqualTo(1u);
        await Assert.That(capture.IsOverflowed).IsTrue();

        WriteHit(in desc, 0, default);
        await Assert.That(capture.Hits[0].ActorId).IsEqualTo(21UL);

        UsdPhysicsQueryResult result = capture.ToResult(9);
        await Assert.That(result.Hits.Count).IsEqualTo(1);
        await Assert.That(result.Hits[0].ObjectId.Value).IsEqualTo(21UL);
        await Assert.That(result.Hits[0].ColliderId!.Value.Value).IsEqualTo(22UL);
        await Assert.That(result.Hits[0].Distance).IsEqualTo(4.5);
        await Assert.That(result.DroppedCount).IsEqualTo(6);

        await Assert.That(capture.ToResult(1234).Hits.Count).IsEqualTo(0);
        await Assert.That(capture.ToResults(buffers.StagedRequests).Length).IsEqualTo(1);
        await Assert.That(capture.ToResults(buffers.StagedRequests)[0].Hits.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ABackendTruncationIsCarriedOntoEveryDetachedResult()
    {
        using var buffers = new PhysxQueryBuffers(requestCapacity: 1, hitCapacity: 1);
        buffers.TryAddRequest(CreateRequest(9));
        var info = new PhysxQueryResultInfo
        {
            HitCount = (nuint)1,
            DroppedHitCount = (nuint)3,
            OverflowFlags = (uint)(PhysxOverflowFlags.QueryHits | PhysxOverflowFlags.QueryTruncated)
        };

        PhysxQueryCapture capture = buffers.Capture(in info);

        await Assert.That(capture.DroppedCountIsLowerBound).IsTrue();
        await Assert.That(capture.IsOverflowed).IsTrue();
        await Assert.That(capture.ToResult(9).DroppedCountIsLowerBound).IsTrue();
        await Assert.That(capture.ToResult(1234).DroppedCountIsLowerBound).IsTrue();
        await Assert.That(capture.ToResults(buffers.StagedRequests)[0].DroppedCountIsLowerBound).IsTrue();
    }

    [Test]
    public async Task AnExactDroppedCountIsNeverReportedAsALowerBound()
    {
        using var buffers = new PhysxQueryBuffers(requestCapacity: 1, hitCapacity: 1);
        buffers.TryAddRequest(CreateRequest(9));
        var info = new PhysxQueryResultInfo
        {
            HitCount = (nuint)1,
            DroppedHitCount = (nuint)3,
            OverflowFlags = (uint)PhysxOverflowFlags.QueryHits
        };

        PhysxQueryCapture capture = buffers.Capture(in info);

        await Assert.That(capture.DroppedCountIsLowerBound).IsFalse();
        await Assert.That(capture.ToResult(9).DroppedCountIsLowerBound).IsFalse();
        await Assert.That(capture.ToResult(9).DroppedCount).IsEqualTo(3);
    }

    [Test]
    public async Task ReportedHitCountIsClampedToTheHitCapacity()
    {
        using var buffers = new PhysxQueryBuffers(requestCapacity: 1, hitCapacity: 1);
        var info = new PhysxQueryResultInfo { HitCount = (nuint)4096 };

        PhysxQueryCapture capture = buffers.Capture(in info);

        await Assert.That(capture.Hits.Length).IsEqualTo(1);
    }

    [Test]
    public async Task ZeroHitCapacityIsRepresentedByANullPointer()
    {
        using var buffers = new PhysxQueryBuffers(requestCapacity: 0, hitCapacity: 0);
        PhysxQueryDesc desc = buffers.CreateDesc();

        await Assert.That(HasRequests(in desc)).IsFalse();
        await Assert.That(HasHits(in desc)).IsFalse();
        await Assert.That(buffers.TryAddRequest(CreateRequest(1))).IsFalse();
    }

    [Test]
    public async Task CapacityAboveTheSupportedMaximumIsRejected()
    {
        await Assert.That(() => new PhysxQueryBuffers(PhysxAbi.MaxResultCapacity + 1, 1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new PhysxQueryBuffers(1, PhysxAbi.MaxResultCapacity + 1))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task DisposedBuffersRejectFurtherUse()
    {
        var buffers = new PhysxQueryBuffers(1, 1);
        buffers.Dispose();

        await Assert.That(() => buffers.CreateDesc()).Throws<ObjectDisposedException>();
        await Assert.That(() => buffers.Clear()).Throws<ObjectDisposedException>();
    }

    private static PhysxQueryRequest CreateRequest(ulong userId) => new()
    {
        UserId = userId,
        Type = (uint)PhysxQueryType.Raycast,
        Origin = new PhysxVec3f(0.0F, 0.0F, 0.0F),
        Direction = new PhysxVec3f(0.0F, 0.0F, -1.0F),
        MaxDistance = 100.0F,
        MaxHits = 1
    };

    private static unsafe bool HasRequests(in PhysxQueryDesc desc) => desc.Requests is not null;

    private static unsafe bool HasHits(in PhysxQueryDesc desc) => desc.Hits is not null;

    private static unsafe ulong ReadRequestUserId(in PhysxQueryDesc desc, int index) =>
        desc.Requests[index].UserId;

    private static unsafe void WriteHit(in PhysxQueryDesc desc, int index, PhysxQueryHit value) =>
        desc.Hits[index] = value;
}
