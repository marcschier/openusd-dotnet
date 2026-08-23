// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Physics.Baking;

namespace OpenUsd.Physics.Tests.Baking;

/// <summary>
/// Native-backed tests for <see cref="UsdPhysicsPreviewApplier"/>.
/// </summary>
public sealed class UsdPhysicsPreviewApplierTests
{
    [Test]
    public void Constructor_RejectsNullScheduler()
    {
        Assert.Throws<ArgumentNullException>(
            () => _ = new UsdPhysicsPreviewApplier(null!, null!));
    }

    [Test]
    public async Task Apply_AuthorsIntoOverlayAndNeverTouchesFilesOnDisk()
    {
        BakeFixture.SkipIfUnavailable();

        string directory = BakeFixture.CreateWorkDirectory("preview-apply");
        string bakePath = BakeFixture.WriteDestinationLayer(directory);
        string rootPath = WriteRoot(directory);
        byte[] rootBefore = File.ReadAllBytes(rootPath);
        byte[] bakeBefore = File.ReadAllBytes(bakePath);

        await using var scheduler = UsdStageScheduler.Open(rootPath);
        using UsdSessionOverlay overlay = await NormalizeAsync(scheduler);
        using var applier = new UsdPhysicsPreviewApplier(scheduler, overlay);

        UsdPhysicsPreviewResult result = await applier.ApplyAsync(
            BakeFixture.CreateBatch(1, 4), BakeFixture.CreateBindings());

        await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Completed);
        await Assert.That(result.AppliedCount).IsEqualTo(2);
        await Assert.That(result.RejectedCount).IsEqualTo(0);
        await Assert.That(result.AuthoredAttributeCount).IsGreaterThan(0);
        await Assert.That(File.ReadAllBytes(rootPath)).IsEquivalentTo(rootBefore);
        await Assert.That(File.ReadAllBytes(bakePath)).IsEquivalentTo(bakeBefore);
    }

    [Test]
    public async Task Apply_OverridesAuthoredTransformAsTheStrongestOpinion()
    {
        BakeFixture.SkipIfUnavailable();

        string directory = BakeFixture.CreateWorkDirectory("preview-strength");
        BakeFixture.WriteDestinationLayer(directory);
        string rootPath = WriteRoot(directory);

        await using var scheduler = UsdStageScheduler.Open(rootPath);
        using UsdSessionOverlay overlay = await NormalizeAsync(scheduler);
        using var applier = new UsdPhysicsPreviewApplier(scheduler, overlay);

        await applier.ApplyAsync(BakeFixture.CreateBatch(1, 4), BakeFixture.CreateBindings());

        UsdMatrix4d matrix = await scheduler.InvokeAsync(
            stage => stage.GetPrim("/Body").GetMatrix4d("xformOp:transform"));
        UsdVec3f[] points = await scheduler.InvokeAsync(
            stage => stage.GetPrim("/Cloth").GetVec3fArray("points"));

        await Assert.That(matrix.M30).IsEqualTo(4d);
        await Assert.That(matrix.M31).IsEqualTo(8d);
        await Assert.That(matrix.M32).IsEqualTo(12d);
        await Assert.That(points.Length).IsEqualTo(3);
        await Assert.That((double)points[0].Y).IsEqualTo(4d);
    }

    [Test]
    public async Task Clear_RestoresTheUserAuthoredOpinionAndPreservesUserEdits()
    {
        BakeFixture.SkipIfUnavailable();

        string directory = BakeFixture.CreateWorkDirectory("preview-clear");
        BakeFixture.WriteDestinationLayer(directory);
        string rootPath = WriteRoot(directory);

        await using var scheduler = UsdStageScheduler.Open(rootPath);
        using UsdSessionOverlay overlay = await NormalizeAsync(scheduler);
        using var applier = new UsdPhysicsPreviewApplier(scheduler, overlay);

        // A user edit made while the overlay is active must survive a reset.
        await scheduler.EditAsync(
            stage =>
            {
                overlay.SetEditTargetToUserLayer();
                stage.GetPrim("/Body").SetDouble("userMarker", 42);
                return true;
            },
            UsdStageInvalidationKind.Property);

        await applier.ApplyAsync(BakeFixture.CreateBatch(1, 4), BakeFixture.CreateBindings());
        await applier.ClearAsync();

        double marker = await scheduler.InvokeAsync(
            stage => stage.GetPrim("/Body").GetDouble("userMarker"));
        UsdVec3f[] points = await scheduler.InvokeAsync(
            stage => stage.GetPrim("/Cloth").GetVec3fArray("points"));

        await Assert.That(marker).IsEqualTo(42d);
        await Assert.That((double)points[0].Y).IsEqualTo(0d);
    }

    [Test]
    public async Task Apply_RejectsWholeBatchWhenIdentityRevisionMovedOn()
    {
        BakeFixture.SkipIfUnavailable();

        string directory = BakeFixture.CreateWorkDirectory("preview-stale-identity");
        BakeFixture.WriteDestinationLayer(directory);
        string rootPath = WriteRoot(directory);

        await using var scheduler = UsdStageScheduler.Open(rootPath);
        using UsdSessionOverlay overlay = await NormalizeAsync(scheduler);
        using var applier = new UsdPhysicsPreviewApplier(scheduler, overlay);

        UsdPhysicsPreviewResult result = await applier.ApplyAsync(
            BakeFixture.CreateBatch(1, 4, identityRevision: 99), BakeFixture.CreateBindings());

        bool authored = await scheduler.InvokeAsync(
            stage => stage.GetPrim("/Body").GetAttributeNames()
                .Contains("xformOp:transform", StringComparer.Ordinal));

        await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Failed);
        await Assert.That(result.AppliedCount).IsEqualTo(0);
        await Assert.That(authored).IsFalse();
        await Assert.That(result.Diagnostics.HasErrors).IsTrue();
    }

    [Test]
    public async Task Apply_RejectsWholeBatchWhenTopologyRevisionMovedOn()
    {
        BakeFixture.SkipIfUnavailable();

        string directory = BakeFixture.CreateWorkDirectory("preview-stale-topology");
        BakeFixture.WriteDestinationLayer(directory);
        string rootPath = WriteRoot(directory);

        await using var scheduler = UsdStageScheduler.Open(rootPath);
        using UsdSessionOverlay overlay = await NormalizeAsync(scheduler);
        using var applier = new UsdPhysicsPreviewApplier(scheduler, overlay);

        var batch = new UsdPhysicsResultBatch(
            7, 1, [BakeFixture.CreatePose(1, 1, 1)], [BakeFixture.CreateCloth(1, 404)]);

        UsdPhysicsPreviewResult result =
            await applier.ApplyAsync(batch, BakeFixture.CreateBindings());

        bool authored = await scheduler.InvokeAsync(
            stage => stage.GetPrim("/Body").GetAttributeNames()
                .Contains("xformOp:transform", StringComparer.Ordinal));

        await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Failed);
        await Assert.That(authored).IsFalse();
        await Assert.That(result.Outcomes.Any(
            o => o.Status == UsdPhysicsBakeRecordStatus.StaleTopology)).IsTrue();
    }

    [Test]
    public async Task Apply_RejectsPointInstancerInstancesWithAPreciseDiagnostic()
    {
        BakeFixture.SkipIfUnavailable();

        string directory = BakeFixture.CreateWorkDirectory("preview-instance");
        BakeFixture.WriteDestinationLayer(directory);
        string rootPath = WriteRoot(directory);

        await using var scheduler = UsdStageScheduler.Open(rootPath);
        using UsdSessionOverlay overlay = await NormalizeAsync(scheduler);
        using var applier = new UsdPhysicsPreviewApplier(scheduler, overlay);

        var bindings = new UsdPhysicsBakeBindings(
            7,
            [
                new UsdPhysicsBakeBinding(BakeFixture.BodyId, "/Body", 5),
                new UsdPhysicsBakeBinding(BakeFixture.ClothId, "/Cloth", -1, 3)
            ]);

        UsdPhysicsPreviewResult result = await applier.ApplyAsync(
            BakeFixture.CreateBatch(1, 4), bindings);

        await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Failed);
        await Assert.That(result.Outcomes.Any(
            o => o.Status == UsdPhysicsBakeRecordStatus.InstanceProxy && o.Detail == 5)).IsTrue();
        await Assert.That(result.Diagnostics.Entries.Any(
            d => d.Message.Contains("prototype", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Apply_IsIdempotentForTheSameBatch()
    {
        BakeFixture.SkipIfUnavailable();

        string directory = BakeFixture.CreateWorkDirectory("preview-idempotent");
        BakeFixture.WriteDestinationLayer(directory);
        string rootPath = WriteRoot(directory);

        await using var scheduler = UsdStageScheduler.Open(rootPath);
        using UsdSessionOverlay overlay = await NormalizeAsync(scheduler);
        using var applier = new UsdPhysicsPreviewApplier(scheduler, overlay);

        UsdPhysicsBakeBindings bindings = BakeFixture.CreateBindings();
        UsdPhysicsPreviewResult first =
            await applier.ApplyAsync(BakeFixture.CreateBatch(1, 4), bindings);
        UsdPhysicsPreviewResult second =
            await applier.ApplyAsync(BakeFixture.CreateBatch(1, 4), bindings);

        await Assert.That(second.AppliedCount).IsEqualTo(first.AppliedCount);
        await Assert.That(second.AuthoredAttributeCount)
            .IsEqualTo(first.AuthoredAttributeCount);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Apply_ReportsTheExactChangeSerialPairOfEveryAuthoredChunk(int chunkSize)
    {
        BakeFixture.SkipIfUnavailable();

        string directory = BakeFixture.CreateWorkDirectory($"preview-serials-{chunkSize}");
        BakeFixture.WriteDestinationLayer(directory);
        string rootPath = WriteRoot(directory);

        await using var scheduler = UsdStageScheduler.Open(rootPath, 1024, NotificationCapacity);
        using UsdSessionOverlay overlay = await NormalizeAsync(scheduler);
        using var applier = new UsdPhysicsPreviewApplier(
            scheduler, overlay, new UsdPhysicsBakeOptions { ChunkSize = chunkSize });

        // Two records, so a chunk size of one splits the apply into two scheduled edits.
        UsdPhysicsPreviewResult result = await applier.ApplyAsync(
            BakeFixture.CreateBatch(1, 4), BakeFixture.CreateBindings());
        List<UsdStageChange> published = await DrainAsync(scheduler);

        await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Completed);
        await Assert.That(result.Edits.Count).IsEqualTo(chunkSize == 1 ? 2 : 1);

        ulong previous = 0;
        foreach (UsdPhysicsPreviewEdit edit in result.Edits)
        {
            await Assert.That(edit.AfterChangeSerial).IsGreaterThan(edit.BeforeChangeSerial);
            await Assert.That(edit.BeforeChangeSerial).IsGreaterThanOrEqualTo(previous);
            await Assert.That(edit.Invalidation).IsEqualTo(UsdStageInvalidationKind.Property);
            await Assert.That(Matches(published, edit)).IsTrue();
            previous = edit.AfterChangeSerial;
        }
    }

    [Test]
    public async Task Apply_ReportsPairsThatNeverAbsorbAnUnrelatedEditRunBetweenChunks()
    {
        BakeFixture.SkipIfUnavailable();

        string directory = BakeFixture.CreateWorkDirectory("preview-serials-interleaved");
        BakeFixture.WriteDestinationLayer(directory);
        string rootPath = WriteRoot(directory);

        await using var scheduler = UsdStageScheduler.Open(rootPath, 1024, NotificationCapacity);
        using UsdSessionOverlay overlay = await NormalizeAsync(scheduler);
        using var applier = new UsdPhysicsPreviewApplier(
            scheduler, overlay, new UsdPhysicsBakeOptions { ChunkSize = 1 });

        // Discard everything normalizing the overlay published, so the drain below holds only the
        // three changes this test orchestrates.
        _ = await DrainAsync(scheduler);

        using var gateEntered = new ManualResetEventSlim(false);
        using var releaseGate = new ManualResetEventSlim(false);
        UsdPhysicsPreviewResult result;
        Task<bool> unrelated;
        Task<bool> gate = scheduler.InvokeAsync(_ =>
        {
            gateEntered.Set();
            return releaseGate.Wait(GateTimeout);
        }).AsTask();

        try
        {
            await Assert.That(gateEntered.Wait(GateTimeout)).IsTrue();

            // The scheduler runs one queued item at a time in arrival order and the owner thread is
            // parked inside the gate, so the order established here is fixed rather than raced.
            // ApplyAsync queues its first chunk before it ever suspends, so that chunk is already
            // behind the gate by the time the call hands back its task.
            Task<UsdPhysicsPreviewResult> apply = applier
                .ApplyAsync(BakeFixture.CreateBatch(1, 4), BakeFixture.CreateBindings())
                .AsTask();

            // Queued behind the first chunk. The applier only queues the second chunk once the
            // first has completed, so this unrelated edit runs strictly between the two chunks.
            unrelated = scheduler.EditAsync(
                stage =>
                {
                    stage.GetPrim("/Body/Child").SetDouble("unrelated", 1);
                    return true;
                },
                UsdStageInvalidationKind.Property).AsTask();

            releaseGate.Set();
            result = await apply;
        }
        finally
        {
            releaseGate.Set();
        }

        await Assert.That(await gate).IsTrue();
        await Assert.That(await unrelated).IsTrue();
        List<UsdStageChange> published = await DrainAsync(scheduler);

        await Assert.That(result.Status).IsEqualTo(UsdPhysicsBakeStatus.Completed);
        await Assert.That(result.Edits.Count).IsEqualTo(2);

        // The read-only gate moves no serial, so exactly three changes were published: the first
        // chunk, the unrelated edit, and the second chunk.
        await Assert.That(published.Count).IsEqualTo(3);
        foreach (UsdPhysicsPreviewEdit edit in result.Edits)
        {
            await Assert.That(Matches(published, edit)).IsTrue();
        }

        // The unrelated edit must sit strictly between the two reported pairs. Serials read next to
        // the scheduled call instead of inside it would have swallowed it, producing a second pair
        // that starts at the first pair's end and matches nothing the scheduler published.
        UsdStageChange foreign = published.Single(change => !IsReported(result.Edits, change));
        await Assert.That(foreign.EditCount).IsEqualTo(1);
        await Assert.That(foreign.BeforeChangeSerial)
            .IsGreaterThanOrEqualTo(result.Edits[0].AfterChangeSerial);
        await Assert.That(foreign.AfterChangeSerial)
            .IsLessThanOrEqualTo(result.Edits[1].BeforeChangeSerial);
        await Assert.That(foreign.AfterChangeSerial).IsGreaterThan(foreign.BeforeChangeSerial);
    }

    [Test]
    public async Task Clear_MigratesAndClearsInsideOneScheduledEditAndReportsItsExactPair()
    {
        BakeFixture.SkipIfUnavailable();

        string directory = BakeFixture.CreateWorkDirectory("preview-clear-serials");
        BakeFixture.WriteDestinationLayer(directory);
        string rootPath = WriteRoot(directory);

        await using var scheduler = UsdStageScheduler.Open(rootPath, 1024, NotificationCapacity);
        using UsdSessionOverlay overlay = await NormalizeAsync(scheduler);
        using var applier = new UsdPhysicsPreviewApplier(scheduler, overlay);

        await applier.ApplyAsync(BakeFixture.CreateBatch(1, 4), BakeFixture.CreateBindings());

        // Author straight into the session container so the clear has contamination to migrate.
        await scheduler.EditAsync(
            stage =>
            {
                using UsdLayer session = stage.GetSessionLayer();
                session.SetMetadata("rogue", "data");
                return true;
            },
            UsdStageInvalidationKind.Full);

        List<UsdStageChange> ignored = await DrainAsync(scheduler);
        await Assert.That(ignored.Count).IsGreaterThanOrEqualTo(1);

        ulong before = await scheduler.InvokeAsync(stage => stage.ChangeSerial);
        UsdPhysicsPreviewClearResult clear = await applier.ClearAsync();
        ulong after = await scheduler.InvokeAsync(stage => stage.ChangeSerial);
        List<UsdStageChange> published = await DrainAsync(scheduler);

        await Assert.That(clear.Status).IsEqualTo(UsdPhysicsBakeStatus.Completed);
        await Assert.That(clear.MigratedUserOpinions).IsTrue();
        await Assert.That(clear.Edits.Count).IsEqualTo(1);

        // Detection, migration, and the layer clear all ran inside the one scheduled edit, so the
        // reported pair brackets the whole clear and the scheduler published exactly one change.
        await Assert.That(clear.Edits[0].BeforeChangeSerial).IsEqualTo(before);
        await Assert.That(clear.Edits[0].AfterChangeSerial).IsEqualTo(after);
        await Assert.That(clear.Edits[0].Invalidation).IsEqualTo(UsdStageInvalidationKind.Full);
        await Assert.That(published.Count).IsEqualTo(1);
        await Assert.That(Matches(published, clear.Edits[0])).IsTrue();

        bool contaminated = await scheduler.InvokeAsync(_ => overlay.DetectContamination());
        await Assert.That(contaminated).IsFalse();
    }

    [Test]
    public async Task Clear_ReportsNoEditWhenThereIsNothingToClear()
    {
        BakeFixture.SkipIfUnavailable();

        string directory = BakeFixture.CreateWorkDirectory("preview-clear-noop");
        BakeFixture.WriteDestinationLayer(directory);
        string rootPath = WriteRoot(directory);

        await using var scheduler = UsdStageScheduler.Open(rootPath, 1024, NotificationCapacity);
        using UsdSessionOverlay overlay = await NormalizeAsync(scheduler);
        using var applier = new UsdPhysicsPreviewApplier(scheduler, overlay);

        UsdPhysicsPreviewClearResult clear = await applier.ClearAsync();

        await Assert.That(clear.Status).IsEqualTo(UsdPhysicsBakeStatus.Completed);
        await Assert.That(clear.MigratedUserOpinions).IsFalse();
        await Assert.That(clear.Edits.Count).IsEqualTo(0);
    }

    private const int NotificationCapacity = 65536;

    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(30);

    private static bool Matches(List<UsdStageChange> published, UsdPhysicsPreviewEdit edit) =>
        published.Any(change =>
            change.BeforeChangeSerial == edit.BeforeChangeSerial &&
            change.AfterChangeSerial == edit.AfterChangeSerial &&
            change.Invalidation == edit.Invalidation &&
            change.EditCount == 1);

    private static bool IsReported(
        IReadOnlyList<UsdPhysicsPreviewEdit> edits, UsdStageChange change) =>
        edits.Any(edit =>
            edit.BeforeChangeSerial == change.BeforeChangeSerial &&
            edit.AfterChangeSerial == change.AfterChangeSerial);

    /// <summary>Drains every change the feed has already buffered without disposing anything.</summary>
    private static async Task<List<UsdStageChange>> DrainAsync(UsdStageScheduler scheduler)
    {
        var drained = new List<UsdStageChange>();
        using var idle = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        try
        {
            await foreach (UsdStageChange change in
                scheduler.ReadChangesAsync(idle.Token).ConfigureAwait(false))
            {
                drained.Add(change);
                idle.CancelAfter(TimeSpan.FromMilliseconds(250));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: the feed only completes when the scheduler is disposed.
        }
        return drained;
    }

    private static string WriteRoot(string directory) =>
        BakeFixture.WriteRootLayer(directory);

    private static async Task<UsdSessionOverlay> NormalizeAsync(UsdStageScheduler scheduler)
    {
        UsdSessionOverlay? overlay = null;
        await scheduler.InvokeAsync(stage =>
        {
            overlay = stage.NormalizeSessionOverlay();
        });
        return overlay!;
    }
}

