// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using OpenUsd.Geom;
using OpenUsd.Physics.Baking;

namespace OpenUsd.Physics.Tests.Baking;

/// <summary>
/// Regression tests for defects found reviewing the physics bake: untrusted page arithmetic,
/// the save-failure contract, destination reservation, and chunk-independent parenting.
/// </summary>
public sealed class UsdPhysicsBakeRegressionTests
{
    private const int PointCountFieldOffset = 48;
    private const int FloatCountFieldOffset = 36;
    private const int FloatOffsetFieldOffset = 32;

    private static readonly UsdPhysicsObjectId ChildId =
        new(0x3003, UsdPhysicsObjectKind.RigidBody);

    private static readonly UsdPhysicsObjectId GrandchildId =
        new(0x4004, UsdPhysicsObjectKind.RigidBody);

    /// <summary>
    /// A page is untrusted input. A point count whose scaled size wraps a 32 bit product must be
    /// rejected instead of letting a tiny payload authorize a huge authoring loop.
    /// </summary>
    [Test]
    [Arguments(0x55555556u, 2u)]
    [Arguments(0xAAAAAAABu, 1u)]
    [Arguments(0x55555557u, 5u)]
    [Arguments(0x80000000u, 0u)]
    public async Task AuthorPage_RejectsAPointCountWhoseScaledSizeWrapsThirtyTwoBits(
        uint pointCount, uint floatCount)
    {
        BakeFixture.SkipIfUnavailable();

        var world = await UsdPhysicsBakerTests.World.CreateAsync("bake-page-wrap");
        await using (world)
        {
            byte[] before = File.ReadAllBytes(world.DestinationPath);
            byte[] page = BuildClothPage();
            PatchRecord(page, 0, PointCountFieldOffset, pointCount);
            PatchRecord(page, 0, FloatCountFieldOffset, floatCount);

            UsdPhysicsBakeEngine.ChunkResult chunk = await AuthorAsync(world, page);

            await Assert.That(chunk.Succeeded).IsFalse();
            await Assert.That(chunk.Authored).IsEqualTo(0);
            await Assert.That(chunk.Applied).IsEqualTo(0);
            await Assert.That(File.ReadAllBytes(world.DestinationPath)).IsEquivalentTo(before);
        }
    }

    /// <summary>
    /// A payload offset near the top of the address space must be rejected by the section bounds
    /// check rather than folded into a pointer.
    /// </summary>
    [Test]
    [Arguments(0xFFFFFFF0u)]
    [Arguments(0x7FFFFFFFu)]
    public async Task AuthorPage_RejectsAPayloadOffsetOutsideItsSection(uint floatOffset)
    {
        BakeFixture.SkipIfUnavailable();

        var world = await UsdPhysicsBakerTests.World.CreateAsync("bake-page-offset");
        await using (world)
        {
            byte[] before = File.ReadAllBytes(world.DestinationPath);
            byte[] page = BuildClothPage();
            PatchRecord(page, 0, FloatOffsetFieldOffset, floatOffset);

            UsdPhysicsBakeEngine.ChunkResult chunk = await AuthorAsync(world, page);

            await Assert.That(chunk.Succeeded).IsFalse();
            await Assert.That(chunk.Outcomes[0].Status)
                .IsEqualTo(UsdPhysicsBakeRecordStatus.InvalidRecord);
            await Assert.That(File.ReadAllBytes(world.DestinationPath)).IsEquivalentTo(before);
        }
    }

    /// <summary>
    /// A point count past the practical per-record ceiling must be rejected before the runtime
    /// allocates or iterates anything sized by it.
    /// </summary>
    [Test]
    public async Task AuthorPage_RejectsAPointCountPastThePracticalCeiling()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await UsdPhysicsBakerTests.World.CreateAsync("bake-page-ceiling");
        await using (world)
        {
            byte[] page = BuildClothPage();
            PatchRecord(page, 0, PointCountFieldOffset, 0x08000000u);

            UsdPhysicsBakeEngine.ChunkResult chunk = await AuthorAsync(world, page);

            await Assert.That(chunk.Succeeded).IsFalse();
            await Assert.That(chunk.Outcomes[0].Status)
                .IsEqualTo(UsdPhysicsBakeRecordStatus.SampleCountMismatch);
        }
    }

    /// <summary>
    /// When the save at the end of a bake genuinely fails, the transaction must stay open so the
    /// caller's rollback restores the destination and the reported outcome stays coherent.
    /// </summary>
    [Test]
    public async Task Bake_RollsBackWhenTheDestinationCannotActuallyBeSaved()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await UsdPhysicsBakerTests.World.CreateAsync("bake-real-save-failure");
        await using (world)
        {
            // The block is probed and released before the bake starts, so preflight still sees a
            // writable destination and the test skips loudly on a system that cannot be blocked.
            using (BakeSaveBlocker probe = BakeSaveBlocker.Create(world.DestinationPath))
            {
                if (!probe.IsEffective)
                {
                    Skip.Test(probe.Explanation);
                    return;
                }
            }

            byte[] before = File.ReadAllBytes(world.DestinationPath);
            BakeSaveBlocker? blocker = null;
            world.Baker.FaultInjector = point =>
            {
                if (point == UsdPhysicsBakeFaultPoint.DuringSave)
                {
                    // A real, platform-appropriate block, so the runtime's own save fails instead
                    // of a simulated failure standing in for it.
                    blocker = BakeSaveBlocker.Create(world.DestinationPath);
                }
                return null;
            };

            UsdPhysicsBakeTransactionResult result;
            try
            {
                result = await world.Baker.BakeAsync(
                    world.Spec(), new BakeFixture.RampSource(), BakeFixture.CreateBindings());
            }
            finally
            {
                blocker?.Dispose();
            }

            await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Failed);
            await Assert.That(result.WasSaved).IsFalse();
            await Assert.That(result.WasRolledBack).IsTrue();
            await Assert.That(result.Diagnostics.Entries.Any(
                diagnostic => diagnostic.Message.Contains(
                    "rolled back", StringComparison.OrdinalIgnoreCase))).IsFalse();
            await Assert.That(result.Diagnostics.Entries.Any(
                diagnostic => diagnostic.Message.Contains(
                    "was restored to the content", StringComparison.Ordinal))).IsTrue();

            // On disk nothing changed, and the in-memory layer no longer holds the samples.
            await Assert.That(File.ReadAllBytes(world.DestinationPath)).IsEquivalentTo(before);
            (double x, double y, double z) = await ReadTranslationAsync(world, "/Body", 1);
            await Assert.That(x).IsEqualTo(0);
            await Assert.That(y).IsEqualTo(0);
            await Assert.That(z).IsEqualTo(0);
        }
    }

    /// <summary>
    /// Two open transactions on one destination could each restore a different backup, so the
    /// second one must be refused while the first is open and admitted once it closes.
    /// </summary>
    [Test]
    public async Task Bake_RefusesASecondTransactionForTheSameDestination()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await UsdPhysicsBakerTests.World.CreateAsync("bake-destination-reservation");
        await using (world)
        {
            (UsdStageScheduler rivalScheduler, UsdPhysicsBaker rivalBaker) =
                await world.CreateRivalAsync();
            var gate = new GatedSource();
            try
            {
                ValueTask<UsdPhysicsBakeTransactionResult> first = world.Baker.BakeAsync(
                    world.Spec(), gate, BakeFixture.CreateBindings());
                await gate.Started.Task;

                UsdPhysicsBakeTransactionResult rival = await rivalBaker.BakeAsync(
                    world.Spec(), new BakeFixture.RampSource(), BakeFixture.CreateBindings());

                await Assert.That(rival.Status).IsEqualTo(UsdPhysicsBakeStatus.Failed);
                await Assert.That(rival.Diagnostics.Entries.Any(
                    diagnostic => diagnostic.Message.Contains(
                        "already open", StringComparison.OrdinalIgnoreCase))).IsTrue();

                gate.Release.SetResult();
                UsdPhysicsBakeTransactionResult completed = await first;
                await Assert.That(completed.Status).IsEqualTo(UsdPhysicsBakeStatus.Completed);

                // The reservation is released with the transaction, so the rival now succeeds.
                UsdPhysicsBakeTransactionResult retried = await rivalBaker.BakeAsync(
                    world.Spec(), new BakeFixture.RampSource(), BakeFixture.CreateBindings());
                await Assert.That(retried.Status).IsEqualTo(UsdPhysicsBakeStatus.Completed);
            }
            finally
            {
                rivalBaker.Dispose();
                await rivalScheduler.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// A failed transaction must release its destination reservation, otherwise one bad bake would
    /// lock the layer out for the rest of the process.
    /// </summary>
    [Test]
    public async Task Bake_ReleasesTheDestinationReservationAfterARollback()
    {
        BakeFixture.SkipIfUnavailable();

        var world = await UsdPhysicsBakerTests.World.CreateAsync("bake-reservation-release");
        await using (world)
        {
            world.Baker.FaultInjector = point => point == UsdPhysicsBakeFaultPoint.AfterFirstChunk
                ? new InvalidOperationException("injected") : null;
            UsdPhysicsBakeTransactionResult failed = await world.Baker.BakeAsync(
                world.Spec(), new BakeFixture.RampSource(), BakeFixture.CreateBindings());
            await Assert.That(failed.WasRolledBack).IsTrue();

            world.Baker.FaultInjector = null;
            UsdPhysicsBakeTransactionResult second = await world.Baker.BakeAsync(
                world.Spec(), new BakeFixture.RampSource(), BakeFixture.CreateBindings());
            await Assert.That(second.Status).IsEqualTo(UsdPhysicsBakeStatus.Completed);
        }
    }

    /// <summary>
    /// A parent and its descendants must compose to the requested world transforms whether the
    /// batch is authored in one chunk or split so each record lands in its own chunk.
    /// </summary>
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4096)]
    public async Task Bake_ComposesTheRequestedWorldTransformForEveryChunkSize(int chunkSize)
    {
        BakeFixture.SkipIfUnavailable();

        var world = await UsdPhysicsBakerTests.World.CreateAsync($"bake-hierarchy-{chunkSize}");
        await using (world)
        {
            var options = new UsdPhysicsBakeOptions
            {
                TransformSpace = UsdPhysicsBakeTransformSpace.LocalToParent,
                ChunkSize = chunkSize
            };

            UsdPhysicsBakeTransactionResult result = await world.Baker.BakeAsync(
                world.Spec(options), new HierarchySource(), HierarchyBindings());

            await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Completed);
            await AssertTranslationAsync(world, "/Body", 10, 0, 0);
            await AssertTranslationAsync(world, "/Body/Child", 10, 5, 0);
            await AssertTranslationAsync(world, "/Body/Child/Grandchild", 10, 5, 2);
        }
    }

    /// <summary>
    /// The authored bytes themselves must not depend on how the batch was chunked.
    /// </summary>
    [Test]
    public async Task Bake_AuthorsIdenticalContentForEveryChunkSize()
    {
        BakeFixture.SkipIfUnavailable();

        string single = await BakeHierarchyToTextAsync("bake-hierarchy-text-a", 1);
        string paired = await BakeHierarchyToTextAsync("bake-hierarchy-text-b", 2);
        string whole = await BakeHierarchyToTextAsync("bake-hierarchy-text-c", 4096);

        await Assert.That(paired).IsEqualTo(single);
        await Assert.That(whole).IsEqualTo(single);
    }

    /// <summary>
    /// The save block must defeat exactly the strategy OpenUSD uses to save a layer: writing a
    /// sibling temporary file and renaming it over the destination. This runs without the native
    /// runtime so every supported operating system verifies its own branch of the helper.
    /// </summary>
    [Test]
    public async Task SaveBlocker_BlocksBothDirectWritesAndTemporaryFileRenames()
    {
        string directory = BakeFixture.CreateWorkDirectory("save-blocker-contract");
        string destination = BakeFixture.WriteDestinationLayer(directory);
        byte[] before = File.ReadAllBytes(destination);
        string temporary = Path.Combine(directory, "bake.usda.tmp");

        using (BakeSaveBlocker blocker = BakeSaveBlocker.Create(destination))
        {
            if (!blocker.IsEffective)
            {
                Skip.Test(blocker.Explanation);
                return;
            }

            await Assert.That(TryWrite(destination)).IsFalse();
            await Assert.That(TryReplaceByRename(temporary, destination)).IsFalse();
        }

        await Assert.That(File.ReadAllBytes(destination)).IsEquivalentTo(before);

        // Releasing the block must leave the destination fully writable again.
        await Assert.That(TryWrite(destination)).IsTrue();
        File.WriteAllBytes(destination, before);
    }

    private static bool TryWrite(string path)
    {
        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Write);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryReplaceByRename(string temporary, string destination)
    {
        try
        {
            File.WriteAllText(temporary, "#usda 1.0\n");
            File.Move(temporary, destination, overwrite: true);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task<string> BakeHierarchyToTextAsync(string name, int chunkSize)
    {
        var world = await UsdPhysicsBakerTests.World.CreateAsync(name);
        await using (world)
        {
            var options = new UsdPhysicsBakeOptions
            {
                TransformSpace = UsdPhysicsBakeTransformSpace.LocalToParent,
                ChunkSize = chunkSize
            };
            await world.Baker.BakeAsync(
                world.Spec(options), new HierarchySource(), HierarchyBindings());
            string text = File.ReadAllText(world.DestinationPath);
            // The work directory name is the only thing two runs legitimately differ by.
            return text.Replace(name, "<name>", StringComparison.Ordinal);
        }
    }

    private static UsdPhysicsBakeBindings HierarchyBindings() =>
        new(
            7,
            [
                new UsdPhysicsBakeBinding(BakeFixture.BodyId, "/Body"),
                new UsdPhysicsBakeBinding(ChildId, "/Body/Child"),
                new UsdPhysicsBakeBinding(GrandchildId, "/Body/Child/Grandchild")
            ]);

    private static async Task AssertTranslationAsync(
        UsdPhysicsBakerTests.World world, string path, double x, double y, double z)
    {
        (double actualX, double actualY, double actualZ) =
            await ReadTranslationAsync(world, path, 1);
        await Assert.That(actualX).IsEqualTo(x).Within(1e-9);
        await Assert.That(actualY).IsEqualTo(y).Within(1e-9);
        await Assert.That(actualZ).IsEqualTo(z).Within(1e-9);
    }

    private static async Task<(double X, double Y, double Z)> ReadTranslationAsync(
        UsdPhysicsBakerTests.World world, string path, double timeCode) =>
        await world.Scheduler.InvokeAsync(stage =>
        {
            UsdMatrix4d matrix = UsdGeomXformable.Wrap(stage.GetPrim(path))
                .GetWorldTransform(timeCode);
            UsdVec3d translation = matrix.ExtractTranslation();
            return (translation.X, translation.Y, translation.Z);
        });

    private static async Task<UsdPhysicsBakeEngine.ChunkResult> AuthorAsync(
        UsdPhysicsBakerTests.World world, byte[] page)
    {
        var results = new byte[
            UsdPhysicsBakePageBuilder.ResultHeaderSize + UsdPhysicsBakePageBuilder.ResultRecordSize];
        UsdPhysicsObjectKind[] kinds = [UsdPhysicsObjectKind.Deformable];
        return await world.Scheduler.EditAsync(
            stage => UsdPhysicsBakeEngine.AuthorPage(
                stage, world.DestinationIdentifier, page, results, kinds),
            UsdStageInvalidationKind.Property);
    }

    private static byte[] BuildClothPage()
    {
        using var builder = new UsdPhysicsBakePageBuilder();
        UsdVec3d[] points =
        [
            new(0, 0, 0),
            new(1, 0, 0),
            new(0, 1, 0)
        ];
        builder.AddPoints(
            BakeFixture.ClothId.Value,
            "/Cloth",
            points,
            default,
            default,
            default,
            writeVelocity: false);
        return builder.Build(
            UsdPhysicsBakePageFlags.TimeSample | UsdPhysicsBakePageFlags.Atomic, 1, 7).ToArray();
    }

    private static void PatchRecord(byte[] page, int record, int fieldOffset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(
            page.AsSpan(
                UsdPhysicsBakePageBuilder.HeaderSize +
                    (record * UsdPhysicsBakePageBuilder.RecordSize) + fieldOffset),
            value);

    /// <summary>A source that holds its transaction open until the test releases it.</summary>
    private sealed class GatedSource : IUsdPhysicsBakeSource
    {
        private int _calls;

        public TaskCompletionSource Started { get; } = new();

        public TaskCompletionSource Release { get; } = new();

        public async ValueTask<UsdPhysicsResultBatch?> GetBatchAsync(
            double timeCode, CancellationToken cancellationToken)
        {
            if (++_calls == 2)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            return BakeFixture.CreateBatch(timeCode, timeCode);
        }
    }

    /// <summary>Poses a three level chain at fixed world transforms.</summary>
    private sealed class HierarchySource : IUsdPhysicsBakeSource
    {
        public ValueTask<UsdPhysicsResultBatch?> GetBatchAsync(
            double timeCode, CancellationToken cancellationToken) =>
            ValueTask.FromResult<UsdPhysicsResultBatch?>(new UsdPhysicsResultBatch(
                7,
                timeCode,
                [
                    Pose(BakeFixture.BodyId, 10, 0, 0),
                    Pose(ChildId, 10, 5, 0),
                    Pose(GrandchildId, 10, 5, 2)
                ],
                []));

        private static UsdPhysicsBodyPose Pose(
            UsdPhysicsObjectId id, double x, double y, double z) =>
            new(
                id,
                new UsdVec3d(x, y, z),
                UsdPhysicsOrientation.Identity,
                default,
                default,
                IsSleeping: false,
                IsKinematic: false);
    }
}
