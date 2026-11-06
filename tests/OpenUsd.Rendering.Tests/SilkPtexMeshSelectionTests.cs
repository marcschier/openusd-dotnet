// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Rendering.Silk;

namespace OpenUsd.Rendering.Tests;

public sealed class SilkPtexMeshSelectionTests
{
    [Test]
    public async Task SelectionKeepsExactFaceIdsAndMapsEachTriangleToSortedOrdinals()
    {
        int[] triangles = [16_777_217, 2, 16_777_216, 2, 0, int.MaxValue];
        using SilkPtexMeshSelection selection = SilkPtexMeshSelection.Create(triangles);
        using SilkPtexMeshSelection.Lease lease = selection.Acquire();
        triangles[0] = 0;

        await Assert.That(lease.FaceIds.SequenceEqual(
            new uint[] { 0, 2, 16_777_216, 16_777_217, int.MaxValue })).IsTrue();
        await Assert.That(lease.TriangleOrdinals.SequenceEqual(
            new uint[] { 3, 1, 2, 1, 0, 4 })).IsTrue();

        using SilkPtexMeshSelection reordered = SilkPtexMeshSelection.Create(
            [int.MaxValue, 0, 16_777_216, 2, 16_777_217, 2]);
        using SilkPtexMeshSelection.Lease reorderedLease = reordered.Acquire();
        await Assert.That(reorderedLease.FaceIds.SequenceEqual(lease.FaceIds)).IsTrue();
        await Assert.That(reorderedLease.TriangleOrdinals.SequenceEqual(
            new uint[] { 4, 0, 2, 1, 3, 1 })).IsTrue();
    }

    [Test]
    public async Task SelectionAllowsMoreTrianglesThanTheFaceLimitWhenFacesRepeat()
    {
        int[] triangles = new int[SilkNativePtexDecoder.MaximumRequestedFaces + 3];
        Array.Fill(triangles, 16_777_217);
        triangles[1] = 7;
        triangles[^1] = 7;

        using SilkPtexMeshSelection selection = SilkPtexMeshSelection.Create(triangles);
        using SilkPtexMeshSelection.Lease lease = selection.Acquire();

        await Assert.That(lease.FaceIds.SequenceEqual(new uint[] { 7, 16_777_217 })).IsTrue();
        await Assert.That(lease.TriangleOrdinals.Length).IsEqualTo(triangles.Length);
        await Assert.That(lease.TriangleOrdinals[0]).IsEqualTo(1u);
        await Assert.That(lease.TriangleOrdinals[1]).IsEqualTo(0u);
        await Assert.That(lease.TriangleOrdinals[^1]).IsEqualTo(0u);
    }

    [Test]
    public async Task SelectionAcceptsTheExactDistinctFaceLimit()
    {
        const int limit = SilkNativePtexDecoder.MaximumRequestedFaces;
        var triangles = new int[limit];
        for (int index = 0; index < triangles.Length; index++)
        {
            triangles[index] = limit - index - 1;
        }

        using SilkPtexMeshSelection selection = SilkPtexMeshSelection.Create(triangles);
        using SilkPtexMeshSelection.Lease lease = selection.Acquire();

        await Assert.That(lease.FaceIds.Length).IsEqualTo(limit);
        await Assert.That(lease.FaceIds[0]).IsEqualTo(0u);
        await Assert.That(lease.FaceIds[^1]).IsEqualTo((uint)(limit - 1));
        await Assert.That(lease.TriangleOrdinals[0]).IsEqualTo((uint)(limit - 1));
        await Assert.That(lease.TriangleOrdinals[^1]).IsEqualTo(0u);
    }

    [Test]
    public async Task SelectionRefusesTooManyDistinctFacesAndReleasesReservations()
    {
        const int count = SilkNativePtexDecoder.MaximumRequestedFaces + 1;
        var triangles = new int[count];
        for (int index = 0; index < triangles.Length; index++)
        {
            triangles[index] = index;
        }
        var budget = new SilkCpuTextureBudget((ulong)count * 8);

        await Assert.That(() => SilkPtexMeshSelection.Create(triangles, budget))
            .Throws<InvalidDataException>().WithMessageContaining("face-count limit");
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    public async Task SelectionRefusesNegativeFaceIdsWithoutReleasingAnotherOwner()
    {
        var budget = new SilkCpuTextureBudget(100);
        using IDisposable otherOwner = budget.Reserve(4);

        await Assert.That(() => SilkPtexMeshSelection.Create([4, -1, 5], budget))
            .Throws<InvalidDataException>().WithMessageContaining("triangle 1");
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(4ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(1ul);
    }

    [Test]
    public async Task SelectionChargesScratchAndRetainedArraysAndReleasesIdempotently()
    {
        var budget = new SilkCpuTextureBudget(45);
        using IDisposable otherOwner = budget.Reserve(1);
        using SilkPtexMeshSelection selection = SilkPtexMeshSelection.Create([4, 2, 4, 9], budget);

        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(45ul);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(29ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(3ul);
        selection.Dispose();
        selection.Dispose();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(1ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(1ul);
        await Assert.That(() => selection.Acquire()).Throws<ObjectDisposedException>();
    }

    [Test]
    [Arguments(29ul, 16ul, 29ul)]
    [Arguments(13ul, 16ul, 29ul)]
    [Arguments(1ul, 12ul, 33ul)]
    public async Task SelectionReleasesFailedAdmissionAndCanRetry(
        ulong otherBytes,
        ulong requestedBytes,
        ulong reservedBytes)
    {
        var budget = new SilkCpuTextureBudget(44);
        using IDisposable otherOwner = budget.Reserve(otherBytes);
        SilkCpuTextureBudgetExceededException? failure = await Assert.That(
            () => SilkPtexMeshSelection.Create([4, 2, 4, 9], budget))
            .Throws<SilkCpuTextureBudgetExceededException>();

        await Assert.That(failure!.RequestedBytes).IsEqualTo(requestedBytes);
        await Assert.That(failure.ReservedBytes).IsEqualTo(reservedBytes);
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(otherBytes);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(1ul);
        otherOwner.Dispose();
        using SilkPtexMeshSelection retried = SilkPtexMeshSelection.Create([4, 2, 4, 9], budget);
        using SilkPtexMeshSelection.Lease lease = retried.Acquire();
        await Assert.That(lease.FaceIds.SequenceEqual(new uint[] { 2, 4, 9 })).IsTrue();
        await Assert.That(lease.TriangleOrdinals.SequenceEqual(new uint[] { 1, 0, 1, 2 })).IsTrue();
        await Assert.That(budget.Usage.PeakReservedBytes).IsEqualTo(44ul);
    }

    [Test]
    public async Task SelectionKeepsReservationsUntilEveryActiveLeaseReleases()
    {
        var budget = new SilkCpuTextureBudget(44);
        using SilkPtexMeshSelection selection = SilkPtexMeshSelection.Create([4, 2, 4, 9], budget);
        using SilkPtexMeshSelection.Lease first = selection.Acquire();
        using SilkPtexMeshSelection.Lease second = selection.Acquire();

        selection.Dispose();
        await Assert.That(() => selection.Acquire()).Throws<ObjectDisposedException>();
        await Assert.That(first.FaceIds.SequenceEqual(new uint[] { 2, 4, 9 })).IsTrue();
        await Assert.That(second.TriangleOrdinals.SequenceEqual(new uint[] { 1, 0, 1, 2 })).IsTrue();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(28ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(2ul);
        first.Dispose();
        first.Dispose();
        await Assert.That(() => { _ = first.FaceIds.Length; }).Throws<ObjectDisposedException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(28ul);
        second.Dispose();
        await Assert.That(() => { _ = second.TriangleOrdinals.Length; }).Throws<ObjectDisposedException>();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }

    [Test]
    [Arguments(true, true)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task AtlasPreparationRefusesDifferentSelectionAndTextureBudgets(
        bool selectionIsBudgeted,
        bool atlasIsBudgeted)
    {
        var selectionBudget = selectionIsBudgeted ? new SilkCpuTextureBudget(32) : null;
        var atlasBudget = atlasIsBudgeted ? new SilkCpuTextureBudget(32) : null;
        using SilkPtexMeshSelection selection = SilkPtexMeshSelection.Create([2], selectionBudget);

        await Assert.That(() => SilkNativePtexDecoder.PrepareSamplingAtlas(
            "missing.ptx", selection, 1, 4, 1, 3, atlasBudget))
            .Throws<ArgumentException>().WithMessageContaining("same CPU texture budget");
        if (selectionBudget is not null)
        {
            await Assert.That(selectionBudget.Usage.ReservedBytes).IsEqualTo(8ul);
        }
        if (atlasBudget is not null)
        {
            await Assert.That(atlasBudget.Usage.ReservedBytes).IsEqualTo(0ul);
        }
    }

    [Test]
    public async Task EmptySelectionDoesNotReserveBytes()
    {
        var budget = new SilkCpuTextureBudget(1);
        using SilkPtexMeshSelection selection = SilkPtexMeshSelection.Create([], budget);
        using SilkPtexMeshSelection.Lease lease = selection.Acquire();

        await Assert.That(lease.FaceIds.IsEmpty).IsTrue();
        await Assert.That(lease.TriangleOrdinals.IsEmpty).IsTrue();
        await Assert.That(budget.Usage.ReservedBytes).IsEqualTo(0ul);
        await Assert.That(budget.Usage.ReservationCount).IsEqualTo(0ul);
    }
}
