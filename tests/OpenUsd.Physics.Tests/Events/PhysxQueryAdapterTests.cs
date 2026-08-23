// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.Immutable;
using OpenUsd.Physics.Interop;

namespace OpenUsd.Physics.Tests.Events;

public sealed class PhysxQueryAdapterTests
{
    private const uint Budget = 64;

    private static readonly float[] Identity = [0F, 0F, 0F, 1F];
    private static readonly float[] Zero = [0F, 0F, 0F, 0F];

    [Test]
    public async Task ARaycastTranslatesWithANormalizedDirectionAndNoShape()
    {
        var request = new UsdPhysicsQueryRequest(
            UsdPhysicsQueryKind.Raycast,
            new UsdVec3d(0, 10, 0),
            new UsdVec3d(0, -2, 0),
            100)
        {
            UserId = 7,
            MaxHits = 4,
            SceneIndex = 1
        };

        await Assert.That(
            PhysxQueryAdapter.TryTranslate(request, Budget, out PhysxQueryRequest native, out string? rejection))
            .IsTrue();
        await Assert.That(rejection).IsNull();
        await Assert.That(native.Type).IsEqualTo((uint)PhysxQueryType.Raycast);
        await Assert.That(native.UserId).IsEqualTo(7UL);
        await Assert.That(native.Direction.Y).IsEqualTo(-1F);
        await Assert.That(native.MaxDistance).IsEqualTo(100F);
        await Assert.That(native.Radius).IsEqualTo(0F);
        await Assert.That(native.MaxHits).IsEqualTo(4u);
        await Assert.That(native.SceneIndex).IsEqualTo(1u);
    }

    [Test]
    public async Task ASweepTranslatesWithASphereRadiusAndOptionalInitialOverlap()
    {
        var request = new UsdPhysicsQueryRequest(
            UsdPhysicsQueryKind.Sweep,
            new UsdVec3d(0, 1, 0),
            new UsdVec3d(1, 0, 0),
            5,
            0.5)
        {
            ReportInitialOverlap = true
        };

        await Assert.That(
            PhysxQueryAdapter.TryTranslate(request, Budget, out PhysxQueryRequest native, out _))
            .IsTrue();
        await Assert.That(native.Type).IsEqualTo((uint)PhysxQueryType.Sweep);
        await Assert.That(native.Radius).IsEqualTo(0.5F);
        await Assert.That(native.Flags).IsEqualTo((uint)PhysxQueryFlags.SweepInitialOverlap);
    }

    [Test]
    public async Task AnOverlapCarriesNoDirectionAndKeepsItsFilterFlags()
    {
        var request = new UsdPhysicsQueryRequest(
            UsdPhysicsQueryKind.Overlap,
            new UsdVec3d(2, 2, 2),
            default,
            0,
            1.25,
            new UsdPhysicsQueryFilter(0xF0, 0x30))
        {
            ExcludeStatic = true,
            ExcludeTriggers = true,
            ReportInitialOverlap = true
        };

        await Assert.That(
            PhysxQueryAdapter.TryTranslate(request, Budget, out PhysxQueryRequest native, out _))
            .IsTrue();
        await Assert.That(native.Type).IsEqualTo((uint)PhysxQueryType.Overlap);
        await Assert.That(native.MaxDistance).IsEqualTo(0F);
        await Assert.That(native.Radius).IsEqualTo(1.25F);
        await Assert.That(native.FilterMask).IsEqualTo(0xC0u);
        await Assert.That(native.Flags)
            .IsEqualTo((uint)(PhysxQueryFlags.ExcludeStatic | PhysxQueryFlags.ExcludeTriggers));
    }

    [Test]
    public async Task AZeroLengthDirectionIsRejected()
    {
        var request = new UsdPhysicsQueryRequest(UsdPhysicsQueryKind.Raycast, default, default, 10);

        await Assert.That(PhysxQueryAdapter.TryTranslate(request, Budget, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task ARaycastWithoutADistanceIsRejected()
    {
        var request = new UsdPhysicsQueryRequest(UsdPhysicsQueryKind.Raycast, default, new UsdVec3d(1, 0, 0), 0);

        await Assert.That(PhysxQueryAdapter.TryTranslate(request, Budget, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task ARaycastThatDeclaresARadiusIsRejected()
    {
        var request = new UsdPhysicsQueryRequest(UsdPhysicsQueryKind.Raycast, default, new UsdVec3d(1, 0, 0), 10, 0.5);

        await Assert.That(PhysxQueryAdapter.TryTranslate(request, Budget, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task AnOverlapThatDeclaresADirectionIsRejected()
    {
        var request = new UsdPhysicsQueryRequest(
            UsdPhysicsQueryKind.Overlap,
            default,
            new UsdVec3d(1, 0, 0),
            0,
            1);

        await Assert.That(PhysxQueryAdapter.TryTranslate(request, Budget, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task ARequestThatExcludesEverythingIsRejected()
    {
        var request = new UsdPhysicsQueryRequest(UsdPhysicsQueryKind.Raycast, default, new UsdVec3d(1, 0, 0), 5)
        {
            ExcludeStatic = true,
            ExcludeDynamic = true
        };

        await Assert.That(PhysxQueryAdapter.TryTranslate(request, Budget, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task ANonFiniteOriginIsRejected()
    {
        var request = new UsdPhysicsQueryRequest(
            UsdPhysicsQueryKind.Raycast,
            new UsdVec3d(double.PositiveInfinity, 0, 0),
            new UsdVec3d(1, 0, 0),
            5);

        await Assert.That(PhysxQueryAdapter.TryTranslate(request, Budget, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task ABatchIsStagedOnceAndKeepsRequestOrder()
    {
        UsdPhysicsQueryRequest[] batch =
        [
            new(UsdPhysicsQueryKind.Raycast, default, new UsdVec3d(0, -1, 0), 20) { UserId = 1 },
            new(UsdPhysicsQueryKind.Sweep, default, new UsdVec3d(1, 0, 0), 5, 0.25) { UserId = 2 },
            new(UsdPhysicsQueryKind.Overlap, default, default, 0, 2) { UserId = 3 }
        ];
        var staged = new PhysxQueryRequest[4];

        bool accepted = PhysxQueryAdapter.TryTranslateBatch(
            batch, Budget, staged, out int count, out int index, out string? rejection);

        await Assert.That(accepted).IsTrue();
        await Assert.That(count).IsEqualTo(3);
        await Assert.That(index).IsEqualTo(-1);
        await Assert.That(rejection).IsNull();
        await Assert.That(staged[0].Type).IsEqualTo((uint)PhysxQueryType.Raycast);
        await Assert.That(staged[1].Type).IsEqualTo((uint)PhysxQueryType.Sweep);
        await Assert.That(staged[2].Type).IsEqualTo((uint)PhysxQueryType.Overlap);
    }

    [Test]
    public async Task ABatchLargerThanTheStagedCapacityIsRejected()
    {
        UsdPhysicsQueryRequest[] batch =
        [
            new(UsdPhysicsQueryKind.Raycast, default, new UsdVec3d(0, -1, 0), 20),
            new(UsdPhysicsQueryKind.Raycast, default, new UsdVec3d(0, 1, 0), 20)
        ];
        var staged = new PhysxQueryRequest[1];

        await Assert.That(
            PhysxQueryAdapter.TryTranslateBatch(batch, Budget, staged, out int count, out _, out string? rejection))
            .IsFalse();
        await Assert.That(count).IsEqualTo(0);
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task HitsAreGroupedPerRequestInOneWalkEvenWhenUserIdsRepeat()
    {
        PhysxQueryRequest[] requests =
        [
            new() { UserId = 5 },
            new() { UserId = 5 },
            new() { UserId = 9 }
        ];
        PhysxQueryHit[] hits =
        [
            new() { UserId = 5, ActorId = 1, Distance = 1F, Flags = (uint)PhysxQueryHitFlags.HasDistance },
            new() { UserId = 5, ActorId = 2, Distance = 2F, Flags = (uint)PhysxQueryHitFlags.HasDistance },
            new() { UserId = 5, ActorId = 3, Distance = 1F, Flags = (uint)PhysxQueryHitFlags.HasDistance },
            new() { UserId = 9, ActorId = 4, Distance = 3F, Flags = (uint)PhysxQueryHitFlags.HasDistance }
        ];

        ImmutableArray<UsdPhysicsQueryResult> results = PhysxQueryAdapter.Detach(requests, hits, 0);

        await Assert.That(results.Length).IsEqualTo(3);
        await Assert.That(results[0].Hits.Count).IsEqualTo(3);
        await Assert.That(results[1].Hits.Count).IsEqualTo(0);
        await Assert.That(results[2].Hits.Count).IsEqualTo(1);
        await Assert.That(results[2].Hits[0].ObjectId.Value).IsEqualTo(4UL);
    }

    [Test]
    public async Task ASingleRequestBatchAttributesTheDroppedHitCount()
    {
        PhysxQueryRequest[] requests = [new() { UserId = 1 }];
        PhysxQueryHit[] hits = [new() { UserId = 1, ActorId = 8, Flags = (uint)PhysxQueryHitFlags.HasDistance }];

        ImmutableArray<UsdPhysicsQueryResult> results = PhysxQueryAdapter.Detach(requests, hits, 6);

        await Assert.That(results.Length).IsEqualTo(1);
        await Assert.That(results[0].DroppedCount).IsEqualTo(6);
        await Assert.That(results[0].IsOverflowed).IsTrue();
    }

    [Test]
    public async Task HitFieldsReportOnlyWhatTheRuntimeAttributed()
    {
        var hit = new PhysxQueryHit
        {
            UserId = 1,
            ActorId = 11,
            ShapeId = 12,
            Position = new PhysxVec3f(1F, 2F, 3F),
            Normal = new PhysxVec3f(0F, 0F, 1F),
            Distance = 4F,
            FaceIndex = 77,
            Flags = (uint)(PhysxQueryHitFlags.HasPosition | PhysxQueryHitFlags.HasNormal |
                PhysxQueryHitFlags.HasDistance | PhysxQueryHitFlags.HasFace | PhysxQueryHitFlags.Trigger)
        };

        UsdPhysicsQueryHit detached = PhysxQueryAdapter.Detach(hit);

        await Assert.That(detached.ObjectId.Value).IsEqualTo(11UL);
        await Assert.That(detached.ColliderId!.Value.Value).IsEqualTo(12UL);
        await Assert.That(detached.ColliderId!.Value.Kind).IsEqualTo(UsdPhysicsObjectKind.Collider);
        await Assert.That(detached.Position.X).IsEqualTo(1.0);
        await Assert.That(detached.Normal.Z).IsEqualTo(1.0);
        await Assert.That(detached.Distance).IsEqualTo(4.0);
        await Assert.That(detached.FaceIndex).IsEqualTo(77u);
        await Assert.That(detached.IsTrigger).IsTrue();
        await Assert.That(detached.HadInitialOverlap).IsFalse();
        await Assert.That(detached.Fields).IsEqualTo(
            UsdPhysicsQueryHitFields.Position | UsdPhysicsQueryHitFields.Normal |
            UsdPhysicsQueryHitFields.Distance | UsdPhysicsQueryHitFields.FaceIndex |
            UsdPhysicsQueryHitFields.Trigger);
    }

    [Test]
    public async Task AnOverlapHitReportsNeitherAPositionNorADistance()
    {
        var hit = new PhysxQueryHit { UserId = 1, ActorId = 11, ShapeId = 12, Flags = 0 };

        UsdPhysicsQueryHit detached = PhysxQueryAdapter.Detach(hit);

        await Assert.That(detached.Fields).IsEqualTo(UsdPhysicsQueryHitFields.None);
        await Assert.That(detached.Distance).IsEqualTo(0.0);
        await Assert.That(detached.Position).IsEqualTo(default(UsdVec3d));
    }

    [Test]
    public async Task DetachingAnEmptyBatchProducesNoResults()
    {
        await Assert.That(PhysxQueryAdapter.Detach([], [], 0).Length).IsEqualTo(0);
        await Assert.That(PhysxQueryAdapter.DetachRun([], 0)).IsSameReferenceAs(UsdPhysicsQueryResult.Empty);
    }

    [Test]
    public async Task ConcurrentBatchesFromDifferentWorldsDetachIndependently()
    {
        PhysxQueryRequest[] requests = [new() { UserId = 1 }, new() { UserId = 2 }];
        PhysxQueryHit[] worldA =
        [
            new() { UserId = 1, ActorId = 10, Distance = 1F, Flags = (uint)PhysxQueryHitFlags.HasDistance },
            new() { UserId = 2, ActorId = 20, Distance = 2F, Flags = (uint)PhysxQueryHitFlags.HasDistance }
        ];
        PhysxQueryHit[] worldB =
        [
            new() { UserId = 1, ActorId = 110, Distance = 1F, Flags = (uint)PhysxQueryHitFlags.HasDistance },
            new() { UserId = 2, ActorId = 220, Distance = 2F, Flags = (uint)PhysxQueryHitFlags.HasDistance }
        ];

        ImmutableArray<UsdPhysicsQueryResult> expectedA = PhysxQueryAdapter.Detach(requests, worldA, 0);
        ImmutableArray<UsdPhysicsQueryResult> expectedB = PhysxQueryAdapter.Detach(requests, worldB, 0);

        var results = new ImmutableArray<UsdPhysicsQueryResult>[48];
        await Task.WhenAll(Enumerable.Range(0, results.Length).Select(index => Task.Run(() =>
        {
            results[index] = (index % 2) == 0
                ? PhysxQueryAdapter.Detach(requests, worldA, 0)
                : PhysxQueryAdapter.Detach(requests, worldB, 0);
        })));

        for (int index = 0; index < results.Length; index++)
        {
            ImmutableArray<UsdPhysicsQueryResult> expected = (index % 2) == 0 ? expectedA : expectedB;
            await Assert.That(results[index].Length).IsEqualTo(expected.Length);
            for (int slot = 0; slot < expected.Length; slot++)
            {
                await Assert.That(results[index][slot]).IsEqualTo(expected[slot]);
            }
        }
    }


    [Test]
    public async Task AShapedRequestCarriesTheIdentityRotationTheAbiRequires()
    {
        var sweep = new UsdPhysicsQueryRequest(
            UsdPhysicsQueryKind.Sweep,
            default,
            new UsdVec3d(1, 0, 0),
            5,
            0.5);
        var overlap = new UsdPhysicsQueryRequest(UsdPhysicsQueryKind.Overlap, default, default, 0, 0.5);
        var raycast = new UsdPhysicsQueryRequest(UsdPhysicsQueryKind.Raycast, default, new UsdVec3d(1, 0, 0), 5);

        await Assert.That(
            PhysxQueryAdapter.TryTranslate(sweep, Budget, out PhysxQueryRequest sweptNative, out _))
            .IsTrue();
        await Assert.That(
            PhysxQueryAdapter.TryTranslate(overlap, Budget, out PhysxQueryRequest overlapNative, out _))
            .IsTrue();
        await Assert.That(
            PhysxQueryAdapter.TryTranslate(raycast, Budget, out PhysxQueryRequest rayNative, out _))
            .IsTrue();

        float[] swept =
            [sweptNative.Rotation.X, sweptNative.Rotation.Y, sweptNative.Rotation.Z, sweptNative.Rotation.W];
        float[] overlapped =
            [overlapNative.Rotation.X, overlapNative.Rotation.Y, overlapNative.Rotation.Z, overlapNative.Rotation.W];
        float[] ray = [rayNative.Rotation.X, rayNative.Rotation.Y, rayNative.Rotation.Z, rayNative.Rotation.W];

        // The ABI rejects a swept or overlapped shape whose quaternion cannot be normalized, and
        // rejects a raycast that declares any geometry at all.
        await Assert.That(swept).IsEquivalentTo(Identity);
        await Assert.That(overlapped).IsEquivalentTo(Identity);
        await Assert.That(ray).IsEquivalentTo(Zero);
    }

    [Test]
    public async Task AFilterThatAcceptsEveryGroupIsFoldedOntoTheAcceptAllMask()
    {
        var filter = new UsdPhysicsQueryFilter(uint.MaxValue, 0);

        await Assert.That(
            PhysxQueryAdapter.TryResolveFilterMask(filter, out uint mask, out string? rejection))
            .IsTrue();
        await Assert.That(rejection).IsNull();
        await Assert.That(mask).IsEqualTo(0u);
    }

    [Test]
    public async Task AFilterWhoseExclusionsCancelItsInclusionsIsRejected()
    {
        var filter = new UsdPhysicsQueryFilter(0x0F, 0x0F);

        await Assert.That(
            PhysxQueryAdapter.TryResolveFilterMask(filter, out uint mask, out string? rejection))
            .IsFalse();
        await Assert.That(mask).IsEqualTo(0u);
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task ARequestWhoseFilterCancelsItselfIsRejectedInsteadOfWidened()
    {
        var request = new UsdPhysicsQueryRequest(
            UsdPhysicsQueryKind.Raycast,
            default,
            new UsdVec3d(0, -1, 0),
            10,
            0,
            new UsdPhysicsQueryFilter(0xFF, 0xFF));

        await Assert.That(PhysxQueryAdapter.TryTranslate(request, Budget, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task AnEmptyIncludeMaskIsRejected()
    {
        var request = new UsdPhysicsQueryRequest(
            UsdPhysicsQueryKind.Raycast,
            default,
            new UsdVec3d(0, -1, 0),
            10,
            0,
            new UsdPhysicsQueryFilter(0, 0));

        await Assert.That(PhysxQueryAdapter.TryTranslate(request, Budget, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task AnAnyHitRequestThatAlsoFiltersIsRejected()
    {
        var narrowed = new UsdPhysicsQueryRequest(
            UsdPhysicsQueryKind.Raycast,
            default,
            new UsdVec3d(0, -1, 0),
            10,
            0,
            new UsdPhysicsQueryFilter(0x0F, 0))
        {
            AnyHit = true
        };
        var triggerFiltered = new UsdPhysicsQueryRequest(
            UsdPhysicsQueryKind.Raycast,
            default,
            new UsdVec3d(0, -1, 0),
            10)
        {
            AnyHit = true,
            ExcludeTriggers = true
        };

        await Assert.That(
            PhysxQueryAdapter.TryTranslate(narrowed, Budget, out _, out string? narrowedRejection))
            .IsFalse();
        await Assert.That(narrowedRejection).IsNotNull();
        await Assert.That(
            PhysxQueryAdapter.TryTranslate(triggerFiltered, Budget, out _, out string? triggerRejection))
            .IsFalse();
        await Assert.That(triggerRejection).IsNotNull();
    }

    [Test]
    public async Task AnAnyHitRequestWithTheDefaultFilterIsAccepted()
    {
        var request = new UsdPhysicsQueryRequest(UsdPhysicsQueryKind.Raycast, default, new UsdVec3d(0, -1, 0), 10)
        {
            AnyHit = true
        };

        await Assert.That(
            PhysxQueryAdapter.TryTranslate(request, Budget, out PhysxQueryRequest native, out _))
            .IsTrue();
        await Assert.That(native.FilterMask).IsEqualTo(0u);
        await Assert.That(native.Flags).IsEqualTo((uint)PhysxQueryFlags.AnyHit);
    }

    [Test]
    public async Task AnUnboundedRequestTakesThePerRequestBudgetOfTheSession()
    {
        var request = new UsdPhysicsQueryRequest(UsdPhysicsQueryKind.Raycast, default, new UsdVec3d(0, -1, 0), 10)
        {
            MaxHits = 0
        };

        await Assert.That(
            PhysxQueryAdapter.TryTranslate(request, Budget, out PhysxQueryRequest native, out _))
            .IsTrue();
        await Assert.That(native.MaxHits).IsEqualTo(Budget);
    }

    [Test]
    public async Task ARequestAboveThePerRequestBudgetIsLoweredOntoIt()
    {
        var request = new UsdPhysicsQueryRequest(UsdPhysicsQueryKind.Raycast, default, new UsdVec3d(0, -1, 0), 10)
        {
            MaxHits = Budget * 4
        };

        await Assert.That(
            PhysxQueryAdapter.TryTranslate(request, Budget, out PhysxQueryRequest native, out _))
            .IsTrue();
        await Assert.That(native.MaxHits).IsEqualTo(Budget);
    }

    [Test]
    public async Task ASessionWithoutAHitBudgetRejectsEveryRequest()
    {
        var request = new UsdPhysicsQueryRequest(UsdPhysicsQueryKind.Raycast, default, new UsdVec3d(0, -1, 0), 10);

        await Assert.That(PhysxQueryAdapter.TryTranslate(request, 0, out _, out string? rejection)).IsFalse();
        await Assert.That(rejection).IsNotNull();
    }

    [Test]
    public async Task AnInitiallyOverlappingSweepHitCarriesNoGeometryAndNoDistance()
    {
        var hit = new PhysxQueryHit
        {
            UserId = 1,
            ActorId = 31,
            ShapeId = 32,
            Distance = 0F,
            Flags = (uint)(PhysxQueryHitFlags.HasDistance | PhysxQueryHitFlags.InitialOverlap)
        };

        UsdPhysicsQueryHit detached = PhysxQueryAdapter.Detach(hit);

        await Assert.That(detached.HadInitialOverlap).IsTrue();
        await Assert.That(detached.Distance).IsEqualTo(0.0);
        await Assert.That(detached.Position).IsEqualTo(default(UsdVec3d));
        await Assert.That(detached.Normal).IsEqualTo(default(UsdVec3d));
        await Assert.That(detached.Fields).IsEqualTo(
            UsdPhysicsQueryHitFields.Distance | UsdPhysicsQueryHitFields.InitialOverlap);
    }

    [Test]
    public async Task ABackendTruncationNeverClaimsAnExactDroppedCount()
    {
        PhysxQueryRequest[] requests = [new() { UserId = 1 }];
        PhysxQueryHit[] hits = [new() { UserId = 1, ActorId = 8, Flags = (uint)PhysxQueryHitFlags.HasDistance }];

        ImmutableArray<UsdPhysicsQueryResult> exact = PhysxQueryAdapter.Detach(requests, hits, 6);
        ImmutableArray<UsdPhysicsQueryResult> bounded = PhysxQueryAdapter.Detach(requests, hits, 6, true);

        await Assert.That(exact[0].DroppedCountIsLowerBound).IsFalse();
        await Assert.That(bounded[0].DroppedCountIsLowerBound).IsTrue();
        await Assert.That(bounded[0].DroppedCount).IsEqualTo(6);
        await Assert.That(bounded[0].IsOverflowed).IsTrue();
        await Assert.That(bounded[0]).IsNotEqualTo(exact[0]);
    }

    [Test]
    public async Task ATruncatedResultWithoutADroppedCountStillReportsOverflow()
    {
        UsdPhysicsQueryResult result = PhysxQueryAdapter.DetachRun([], 0, true);

        await Assert.That(result).IsNotSameReferenceAs(UsdPhysicsQueryResult.Empty);
        await Assert.That(result.DroppedCount).IsEqualTo(0);
        await Assert.That(result.DroppedCountIsLowerBound).IsTrue();
        await Assert.That(result.IsOverflowed).IsTrue();
    }

    [Test]
    public async Task AMultiRequestBatchNeverAttributesABackendTruncationToOneRequest()
    {
        PhysxQueryRequest[] requests = [new() { UserId = 1 }, new() { UserId = 2 }];
        PhysxQueryHit[] hits =
        [
            new() { UserId = 1, ActorId = 8, Flags = (uint)PhysxQueryHitFlags.HasDistance },
            new() { UserId = 2, ActorId = 9, Flags = (uint)PhysxQueryHitFlags.HasDistance }
        ];

        ImmutableArray<UsdPhysicsQueryResult> results = PhysxQueryAdapter.Detach(requests, hits, 6, true);

        await Assert.That(results[0].DroppedCount).IsEqualTo(0);
        await Assert.That(results[0].DroppedCountIsLowerBound).IsFalse();
        await Assert.That(results[1].DroppedCountIsLowerBound).IsFalse();
    }
}
