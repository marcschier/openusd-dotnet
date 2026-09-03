// Copyright (c) marcschier. Licensed under the MIT License.

using System.Reflection;

namespace OpenUsd.LiveAuthoring.Tests;

public sealed class LiveAuthoringQueueTests
{
    [Test]
    public async Task BatchesExecuteInAdmissionOrder()
    {
        var executor = new RecordingExecutor();
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 3);

        LiveAuthoringAdmissionReceipt[] receipts =
        [
            await sink.ApplyAsync(Batch(1)),
            await sink.ApplyAsync(Batch(2)),
            await sink.ApplyAsync(Batch(3))
        ];
        await Task.WhenAll(receipts.Select(static receipt => receipt.Applied));

        await Assert.That(executor.Sequences).IsEquivalentTo([1L, 2L, 3L]);
        await Assert.That(executor.Sequences).IsInOrder();
    }

    [Test]
    public async Task SubmissionSequencesMustStrictlyIncrease()
    {
        var executor = new RecordingExecutor();
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 1);

        LiveAuthoringAdmissionReceipt admitted = await sink.ApplyAsync(Batch(2));
        await Assert.That(
                async () => await sink.ApplyAsync(Batch(1)))
            .Throws<ArgumentOutOfRangeException>();
        _ = await admitted.Applied;
        await Assert.That(executor.Sequences).IsEquivalentTo([2L]);
    }

    [Test]
    public async Task CancellationWhileBackpressuredDoesNotAdmitBatch()
    {
        var executor = new RecordingExecutor(blockFirst: true);
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 1);
        Task<LiveAuthoringAdmissionReceipt> first = sink.ApplyAsync(Batch(1)).AsTask();
        await executor.FirstStarted;
        Task<LiveAuthoringAdmissionReceipt> second = sink.ApplyAsync(Batch(2)).AsTask();
        using var cancellation = new CancellationTokenSource();
        Task<LiveAuthoringAdmissionReceipt> third =
            sink.ApplyAsync(Batch(3), cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.That(async () => await third).Throws<OperationCanceledException>();
        executor.Release();
        LiveAuthoringAdmissionReceipt firstReceipt = await first;
        LiveAuthoringAdmissionReceipt secondReceipt = await second;
        await Task.WhenAll(firstReceipt.Applied, secondReceipt.Applied);
        await Assert.That(executor.Sequences).IsEquivalentTo([1L, 2L]);
    }

    [Test]
    public async Task CancellationOfWaitForResultDoesNotAffectExecution()
    {
        var executor = new RecordingExecutor(blockFirst: true);
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 1);
        LiveAuthoringAdmissionReceipt receipt = await sink.ApplyAsync(Batch(1));
        await executor.FirstStarted;

        using var cancellation = new CancellationTokenSource();
        Task<LiveAuthoringBatchResult> waiting =
            receipt.WaitForResultAsync(cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.That(async () => await waiting).Throws<OperationCanceledException>();

        executor.Release();
        await executor.FirstCompleted;
        LiveAuthoringBatchResult result = await receipt.Applied;

        await Assert.That(result.LastSequence).IsEqualTo(1);
        await Assert.That(executor.Sequences).IsEquivalentTo([1L]);
        await Assert.That(executor.CompletedSequences).IsEquivalentTo([1L]);
    }

    [Test]
    public async Task FullQueueCoalescesOnlyMatchingTailSnapshot()
    {
        var executor = new RecordingExecutor(blockFirst: true);
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 1);
        LiveAuthoringAdmissionReceipt first = await sink.ApplyAsync(Batch(1));
        await executor.FirstStarted;
        LiveAuthoringAdmissionReceipt superseded = await sink.ApplyAsync(Batch(2, "temperature"));
        LiveAuthoringAdmissionReceipt latest = await sink.ApplyAsync(Batch(3, "temperature"));

        await Assert.That(superseded.Coalesced).IsFalse();
        await Assert.That(latest.Coalesced).IsTrue();

        executor.Release();
        LiveAuthoringBatchResult[] results =
            await Task.WhenAll(first.Applied, superseded.Applied, latest.Applied);

        await Assert.That(executor.Sequences).IsEquivalentTo([1L, 3L]);
        await Assert.That(sink.PeakPendingBatchCount).IsEqualTo(1);
        await Assert.That(sink.CoalescedBatchCount).IsEqualTo(1);
        await Assert.That(results[1].FirstSequence).IsEqualTo(2);
        await Assert.That(results[1].LastSequence).IsEqualTo(3);
        await Assert.That(results[1].BatchCount).IsEqualTo(2);
        await Assert.That(results[2]).IsEqualTo(results[1]);
    }

    [Test]
    public async Task DisposalDrainsAcceptedBatchesAndDisposesExecutor()
    {
        var executor = new RecordingExecutor(blockFirst: true);
        var sink = new QueuedLiveAuthoringSink(executor, capacity: 1);
        LiveAuthoringAdmissionReceipt first = await sink.ApplyAsync(Batch(1));
        await executor.FirstStarted;
        LiveAuthoringAdmissionReceipt second = await sink.ApplyAsync(Batch(2));
        ValueTask disposal = sink.DisposeAsync();

        executor.Release();
        await Task.WhenAll(first.Applied, second.Applied);
        await disposal;

        await Assert.That(executor.Disposed).IsTrue();
        await Assert.That(sink.Completion.IsCompletedSuccessfully).IsTrue();
        await Assert.That(
                async () => await sink.ApplyAsync(Batch(3)))
            .Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task ExecutorErrorsAreSurfacedWithoutLosingLaterBatches()
    {
        var executor = new RecordingExecutor(failSequence: 1);
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 1);

        LiveAuthoringAdmissionReceipt first = await sink.ApplyAsync(Batch(1));
        await Assert.That(async () => await first.Applied).Throws<InvalidOperationException>();

        LiveAuthoringAdmissionReceipt second = await sink.ApplyAsync(Batch(2));
        LiveAuthoringBatchResult result = await second.Applied;

        await Assert.That(result.LastSequence).IsEqualTo(2);
        await Assert.That(executor.Sequences).IsEquivalentTo([1L, 2L]);
    }

    [Test]
    public async Task ContractsDefaultToSessionLayerAndPreventStageBoundResults()
    {
        var options = new UsdLiveAuthoringOptions();
        MethodInfo apply = typeof(ILiveAuthoringSink).GetMethod(
            nameof(ILiveAuthoringSink.ApplyAsync))!;

        await Assert.That(options.EditLayer).IsEqualTo(LiveAuthoringEditLayer.Session);
        await Assert.That(options.HealthObserver).IsNull();
        await Assert.That(typeof(IUsdDetachedResult).IsAssignableFrom(typeof(LiveAuthoringBatchResult)))
            .IsTrue();
        await Assert.That(apply.ReturnType)
            .IsEqualTo(typeof(ValueTask<LiveAuthoringAdmissionReceipt>));
        await Assert.That(
                typeof(LiveStageUpdate).Assembly
                    .GetExportedTypes()
                    .SelectMany(static type => type.GetMethods())
                    .SelectMany(static method => method.GetParameters())
                    .Any(static parameter => parameter.ParameterType == typeof(Func<UsdStage, UsdStage>)))
            .IsFalse();
    }

    [Test]
    public async Task InvalidationClassificationUsesStrongestUpdate()
    {
        var batch = new LiveAuthoringBatch(
            1,
            [
                new SetAttributeUpdate(
                    "/World/Sensor",
                    "custom:value",
                    LiveAttributeValue.FromDouble(1)),
                new DefinePrimUpdate("/World/New", "Xform"),
                new SetReferenceUpdate("/World/New", "asset.usda", "/Asset")
            ]);

        await Assert.That(batch.Invalidation)
            .IsEqualTo(UsdStageInvalidationKind.Composition);
    }

    [Test]
    public async Task InvalidLaterUpdateRejectsWholeBatchBeforeExecutor()
    {
        var executor = new RecordingExecutor();
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 1);

        async Task submitInvalidBatch()
        {
            var batch = new LiveAuthoringBatch(
                1,
                [
                    new DefinePrimUpdate("/World", "Xform"),
                    new SetAttributeUpdate(
                        "/World",
                        "custom::invalid",
                        LiveAttributeValue.FromDouble(1))
                ]);
            _ = await sink.ApplyAsync(batch);
        }

        await Assert.That(submitInvalidBatch).Throws<ArgumentException>();
        await Assert.That(executor.Sequences).IsEmpty();
    }

    [Test]
    public async Task NullUpdateListsProduceDescriptiveArgumentErrors()
    {
        ArgumentException targets = CaptureArgument(() => new LiveAuthoringBatch(
            1,
            [
                new SetRelationshipTargetsUpdate(
                    "/World",
                    "custom:targets",
                    null!)
            ]));
        ArgumentException variants = CaptureArgument(() => new LiveAuthoringBatch(
            1,
            [
                new SetVariantSelectionUpdate(
                    "/World",
                    "look",
                    null!,
                    "red")
            ]));
        ArgumentException orientations = CaptureArgument(() => new LiveAuthoringBatch(
            1,
            [
                new SetPointInstancerOrientationsUpdate("/World", null!)
            ]));

        await Assert.That(targets.ParamName).IsEqualTo("updates[0].Targets");
        await Assert.That(variants.ParamName).IsEqualTo("updates[0].KnownVariants");
        await Assert.That(orientations.ParamName).IsEqualTo("updates[0].Orientations");
    }

    [Test]
    public async Task PureValidationRejectsMalformedUpdateFields()
    {
        (LiveStageUpdate Update, string ParameterName)[] cases =
        [
            (new DefinePrimUpdate("World", "Xform"), "updates[0].PrimPath"),
            (
                new SetAttributeUpdate(
                    "/World",
                    "custom::value",
                    LiveAttributeValue.FromDouble(1)),
                "updates[0].AttributeName"),
            (
                new SetAttributeUpdate(
                    "/World",
                    "custom:value",
                    LiveAttributeValue.FromDouble(1),
                    double.PositiveInfinity),
                "updates[0].TimeCode"),
            (
                new SetAttributeUpdate(
                    "/World",
                    "custom:tokens",
                    LiveAttributeValue.FromTokenArray(["ok", "bad\0"])),
                "updates[0].Value.TokenArray[1]"),
            (new SetReferenceUpdate("/World", null, null), "updates[0].AssetPath"),
            (new SetReferenceUpdate("/World", "", null), "updates[0].AssetPath"),
            (new SetPayloadUpdate("/World", "asset\0.usda", null), "updates[0].AssetPath"),
            (new SetReferenceUpdate("/World", "asset.usda", ""), "updates[0].TargetPrimPath"),
            (
                new SetPayloadUpdate("/World", "asset.usda", "/Target\0"),
                "updates[0].TargetPrimPath"),
            (
                new SetRelationshipTargetsUpdate(
                    "/World",
                    "custom::targets",
                    ["/Target"]),
                "updates[0].RelationshipName"),
            (
                new SetRelationshipTargetsUpdate(
                    "/World",
                    "custom:targets",
                    ["/Target", null!]),
                "updates[0].Targets[1]"),
            (
                new SetVariantSelectionUpdate(
                    "/World",
                    "look::invalid",
                    ["red"],
                    "red"),
                "updates[0].VariantSetName"),
            (
                new SetVariantSelectionUpdate(
                    "/World",
                    "look",
                    ["red-value"],
                    "red-value"),
                "updates[0].KnownVariants[0]"),
            (
                new SetVariantSelectionUpdate(
                    "/World",
                    "look",
                    ["red", "red"],
                    "red"),
                "updates[0].KnownVariants[1]"),
            (
                new SetVariantSelectionUpdate(
                    "/World",
                    "look",
                    ["red", "blue"],
                    "green"),
                "updates[0].Selection"),
            (
                new ClearUpdate("/World", LiveClearTargetKind.AttributeValue, null),
                "updates[0].Name"),
            (
                new ClearUpdate("/World", LiveClearTargetKind.RelationshipTargets, "bad::name"),
                "updates[0].Name"),
            (
                new ClearUpdate("/World", LiveClearTargetKind.Metadata, ""),
                "updates[0].Name"),
            (
                new ClearUpdate("/World", LiveClearTargetKind.References, "unexpected"),
                "updates[0].Name"),
            (
                new ClearUpdate("/World", LiveClearTargetKind.Payloads, "unexpected"),
                "updates[0].Name"),
            (
                new SetMetadataUpdate("/World", "", LiveMetadataValue.FromBoolean(true)),
                "updates[0].Key"),
            (
                new SetMetadataUpdate("/World", "customData:vendor", LiveMetadataValue.FromString("bad\0")),
                "updates[0].Value.StringValue"),
            (
                new ApiSchemaUpdate("/World", "bad::token", LiveApiSchemaOperation.Apply),
                "updates[0].SchemaToken"),
            (
                new ApiSchemaUpdate("/World", "", LiveApiSchemaOperation.Remove),
                "updates[0].SchemaToken")
        ];

        foreach ((LiveStageUpdate update, string parameterName) in cases)
        {
            ArgumentException exception = CaptureArgument(
                () => new LiveAuthoringBatch(1, [update]));
            await Assert.That(exception.ParamName).IsEqualTo(parameterName);
        }
    }

    [Test]
    public async Task OpaqueCorrelationAndOriginIdsAreValidatedBeforeAdmission()
    {
        await Assert.That(static () => new LiveAuthoringBatch(
                1,
                [new DefinePrimUpdate("/World")],
                correlationId: ""))
            .Throws<ArgumentException>();
        await Assert.That(static () => new LiveAuthoringBatch(
                1,
                [new DefinePrimUpdate("/World")],
                originId: new string('x', LiveAuthoringValidation.MaxOpaqueIdLength + 1)))
            .Throws<ArgumentException>();
        await Assert.That(static () => new LiveAuthoringBatch(
                1,
                [new DefinePrimUpdate("/World")],
                correlationId: "has\0nul"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task CorrelationAndOriginIdsFlowThroughAdmissionAndAppliedResult()
    {
        var executor = new RecordingExecutor();
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 2);
        var batch = new LiveAuthoringBatch(
            1,
            [
                new SetAttributeUpdate(
                    "/World/Sensor",
                    "custom:value",
                    LiveAttributeValue.FromInt64(1))
            ],
            correlationId: "corr-1",
            originId: "origin-1");

        LiveAuthoringAdmissionReceipt receipt = await sink.ApplyAsync(batch);

        await Assert.That(receipt.Sequence).IsEqualTo(1);
        await Assert.That(receipt.CorrelationId).IsEqualTo("corr-1");
        await Assert.That(receipt.OriginId).IsEqualTo("origin-1");
        await Assert.That(receipt.Coalesced).IsFalse();

        LiveAuthoringBatchResult result = await receipt.Applied;
        await Assert.That(result.CorrelationId).IsEqualTo("corr-1");
        await Assert.That(result.OriginId).IsEqualTo("origin-1");
    }

    [Test]
    public async Task HealthSnapshotReportsQueueMetricsAndLastOutcomes()
    {
        var executor = new RecordingExecutor(failSequence: 2);
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 4);

        LiveAuthoringAdmissionReceipt first = await sink.ApplyAsync(Batch(1));
        _ = await first.Applied;
        LiveAuthoringAdmissionReceipt second = await sink.ApplyAsync(Batch(2));
        await Assert.That(async () => await second.Applied).Throws<InvalidOperationException>();

        LiveAuthoringHealthSnapshot snapshot = sink.GetHealthSnapshot();

        await Assert.That(snapshot.Capacity).IsEqualTo(4);
        await Assert.That(snapshot.PendingBatchCount).IsEqualTo(0);
        await Assert.That(snapshot.IsAccepting).IsTrue();
        await Assert.That(snapshot.LastAdmittedSequence).IsEqualTo(2);
        await Assert.That(snapshot.LastAppliedSequence).IsEqualTo(1);
        await Assert.That(snapshot.LastFailedSequence).IsEqualTo(2);
        await Assert.That(snapshot.LastFailureDetail).IsNotNull();
    }

    [Test]
    public async Task HealthObserverReceivesAdmittedAppliedFailedAndDisposedEvents()
    {
        var executor = new RecordingExecutor(failSequence: 2);
        var observer = new RecordingHealthObserver();
        var sink = new QueuedLiveAuthoringSink(executor, capacity: 4, observer);

        LiveAuthoringAdmissionReceipt first = await sink.ApplyAsync(Batch(1));
        _ = await first.Applied;
        LiveAuthoringAdmissionReceipt second = await sink.ApplyAsync(Batch(2));
        await Assert.That(async () => await second.Applied).Throws<InvalidOperationException>();
        await sink.DisposeAsync();

        List<LiveAuthoringHealthEventKind> kinds = observer.Events
            .Select(static healthEvent => healthEvent.Kind)
            .ToList();
        await Assert.That(kinds).Contains(LiveAuthoringHealthEventKind.Admitted);
        await Assert.That(kinds).Contains(LiveAuthoringHealthEventKind.Applied);
        await Assert.That(kinds).Contains(LiveAuthoringHealthEventKind.Failed);
        await Assert.That(kinds).Contains(LiveAuthoringHealthEventKind.Disposed);
        LiveAuthoringHealthEvent failed = observer.Events
            .First(static healthEvent => healthEvent.Kind == LiveAuthoringHealthEventKind.Failed);
        await Assert.That(failed.Sequence).IsEqualTo(2);
        await Assert.That(failed.Detail).IsNotNull();
    }

    [Test]
    public async Task HealthObserverReceivesCoalescedAndRejectedEvents()
    {
        var executor = new RecordingExecutor(blockFirst: true);
        var observer = new RecordingHealthObserver();
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 1, observer);

        LiveAuthoringAdmissionReceipt first = await sink.ApplyAsync(Batch(1));
        await executor.FirstStarted;
        _ = await sink.ApplyAsync(Batch(2, "temperature"));
        LiveAuthoringAdmissionReceipt latest = await sink.ApplyAsync(Batch(3, "temperature"));
        await Assert.That(async () => await sink.ApplyAsync(Batch(2))).Throws<ArgumentOutOfRangeException>();

        executor.Release();
        await Task.WhenAll(first.Applied, latest.Applied);

        List<LiveAuthoringHealthEventKind> kinds = observer.Events
            .Select(static healthEvent => healthEvent.Kind)
            .ToList();
        await Assert.That(kinds).Contains(LiveAuthoringHealthEventKind.Coalesced);
        await Assert.That(kinds).Contains(LiveAuthoringHealthEventKind.Rejected);
    }

    [Test]
    public async Task PartialFailureSurfacesWithoutRollingBackEarlierSideEffects()
    {
        var executor = new SideEffectRecordingExecutor();
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 1);
        var batch = new LiveAuthoringBatch(
            1,
            [
                new SetAttributeUpdate(
                    "/World/Sensor",
                    "custom:value",
                    LiveAttributeValue.FromInt64(1)),
                new ApiSchemaUpdate("/World/Sensor", "AssetPreviewsAPI", LiveApiSchemaOperation.Remove)
            ]);

        LiveAuthoringAdmissionReceipt receipt = await sink.ApplyAsync(batch);
        await Assert.That(async () => await receipt.Applied).Throws<NotSupportedException>();
        await Assert.That(executor.AppliedSideEffects).IsEquivalentTo(["custom:value"]);
    }

    [Test]
    public async Task ApiSchemaApplyRegistryIsBoundedAndCurated()
    {
        await Assert.That(UsdStageBatchExecutor.SupportedApiSchemaTokens).IsEquivalentTo(
            ["SkelBindingAPI", "AssetPreviewsAPI", "NodeGraphNodeAPI", "SceneGraphPrimAPI"]);
    }

    [Test]
    public async Task LiveAttributeValueSupportsArrayMatrixAndScalarRoundTrip()
    {
        LiveAttributeValue matrix = LiveAttributeValue.FromMatrix4d(UsdMatrix4d.Identity);
        await Assert.That(matrix.Kind).IsEqualTo(LiveAttributeKind.Matrix4d);
        await Assert.That(matrix.Matrix4d).IsEqualTo(UsdMatrix4d.Identity);
        await Assert.That(() => matrix.DoubleArray).Throws<InvalidOperationException>();

        LiveAttributeValue doubles = LiveAttributeValue.FromDoubleArray([1.0, 2.0, 3.0]);
        await Assert.That(doubles.DoubleArray).IsEquivalentTo([1.0, 2.0, 3.0]);

        LiveAttributeValue tokens = LiveAttributeValue.FromTokenArray(["a", "b"]);
        await Assert.That(tokens.TokenArray).IsEquivalentTo(["a", "b"]);

        LiveAttributeValue vec3fArray = LiveAttributeValue.FromVec3fArray(
            [new UsdVec3f(1, 2, 3), new UsdVec3f(4, 5, 6)]);
        await Assert.That(vec3fArray.Vec3fArray.Count).IsEqualTo(2);

        await Assert.That(LiveAttributeValue.FromInt64(5) == LiveAttributeValue.FromInt64(5)).IsTrue();
        await Assert.That(LiveAttributeValue.FromInt64(5) == LiveAttributeValue.FromInt64(6)).IsFalse();
        await Assert.That(
                LiveAttributeValue.FromDoubleArray([1.0, 2.0]) ==
                LiveAttributeValue.FromDoubleArray([1.0, 2.0]))
            .IsTrue();
    }

    [Test]
    public async Task CoalescedWaitersPreserveTheirOwnCorrelationAndOriginIds()
    {
        var executor = new RecordingExecutor(blockFirst: true);
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 1);
        LiveAuthoringAdmissionReceipt first = await sink.ApplyAsync(Batch(1));
        await executor.FirstStarted;
        LiveAuthoringAdmissionReceipt superseded = await sink.ApplyAsync(new LiveAuthoringBatch(
            2,
            [
                new SetAttributeUpdate(
                    "/World/Sensor",
                    "custom:value",
                    LiveAttributeValue.FromInt64(2))
            ],
            coalescingKey: "temperature",
            correlationId: "corr-2",
            originId: "origin-2"));
        LiveAuthoringAdmissionReceipt latest = await sink.ApplyAsync(new LiveAuthoringBatch(
            3,
            [
                new SetAttributeUpdate(
                    "/World/Sensor",
                    "custom:value",
                    LiveAttributeValue.FromInt64(3))
            ],
            coalescingKey: "temperature",
            correlationId: "corr-3",
            originId: "origin-3"));

        executor.Release();
        LiveAuthoringBatchResult supersededResult = await superseded.Applied;
        LiveAuthoringBatchResult latestResult = await latest.Applied;
        _ = await first.Applied;

        // Both waiters observe the same coalesced sequence range and batch count...
        await Assert.That(supersededResult.FirstSequence).IsEqualTo(2);
        await Assert.That(supersededResult.LastSequence).IsEqualTo(3);
        await Assert.That(supersededResult.BatchCount).IsEqualTo(2);
        await Assert.That(latestResult.FirstSequence).IsEqualTo(2);
        await Assert.That(latestResult.LastSequence).IsEqualTo(3);
        await Assert.That(latestResult.BatchCount).IsEqualTo(2);

        // ...but each keeps its own opaque correlation/origin identifiers.
        await Assert.That(supersededResult.CorrelationId).IsEqualTo("corr-2");
        await Assert.That(supersededResult.OriginId).IsEqualTo("origin-2");
        await Assert.That(latestResult.CorrelationId).IsEqualTo("corr-3");
        await Assert.That(latestResult.OriginId).IsEqualTo("origin-3");
    }

    [Test]
    public async Task ThrowingHealthObserverDoesNotFailAdmissionOrLoseTheBatch()
    {
        var executor = new RecordingExecutor();
        var observer = new ThrowingHealthObserver();
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 2, observer);

        LiveAuthoringAdmissionReceipt receipt = await sink.ApplyAsync(Batch(1));
        LiveAuthoringBatchResult result = await receipt.Applied;

        await Assert.That(result.LastSequence).IsEqualTo(1);
        await Assert.That(executor.Sequences).IsEquivalentTo([1L]);
        await Assert.That(observer.ReportAttempts).IsGreaterThanOrEqualTo(2);
        await Assert.That(sink.HealthObserverFailureCount).IsGreaterThanOrEqualTo(2);
        LiveAuthoringHealthSnapshot snapshot = sink.GetHealthSnapshot();
        await Assert.That(snapshot.HealthObserverFailureCount).IsEqualTo(sink.HealthObserverFailureCount);
        await Assert.That(snapshot.LastHealthObserverFailureDetail).IsNotNull();
    }

    [Test]
    public async Task ThrowingHealthObserverDoesNotFailDisposal()
    {
        var executor = new RecordingExecutor();
        var observer = new ThrowingHealthObserver();
        var sink = new QueuedLiveAuthoringSink(executor, capacity: 2, observer);
        LiveAuthoringAdmissionReceipt receipt = await sink.ApplyAsync(Batch(1));
        _ = await receipt.Applied;

        await sink.DisposeAsync();

        await Assert.That(sink.Completion.IsCompletedSuccessfully).IsTrue();
        await Assert.That(executor.Disposed).IsTrue();
        await Assert.That(sink.HealthObserverFailureCount).IsGreaterThanOrEqualTo(3);
        await Assert.That(sink.GetHealthSnapshot().LastHealthObserverFailureDetail).IsNotNull();
    }

    [Test]
    public async Task PureValidationEnforcesUpdateCountBound()
    {
        LiveStageUpdate[] tooManyUpdates = Enumerable.Repeat<LiveStageUpdate>(
                new DefinePrimUpdate("/World"),
                LiveAuthoringValidation.MaxUpdatesPerBatch + 1)
            .ToArray();

        ArgumentException exception = CaptureArgument(() => new LiveAuthoringBatch(1, tooManyUpdates));
        await Assert.That(exception.ParamName).IsEqualTo("updates");
    }

    [Test]
    public async Task PureValidationEnforcesAggregateElementCountAcrossBatch()
    {
        var maxBooleans = new bool[LiveAuthoringValidation.MaxCollectionElementCount];
        int updatesNeeded =
            (int)(LiveAuthoringValidation.MaxTotalCollectionElementCountPerBatch /
                LiveAuthoringValidation.MaxCollectionElementCount) + 1;
        var updates = new List<LiveStageUpdate>();
        for (int index = 0; index < updatesNeeded; index++)
        {
            updates.Add(new SetAttributeUpdate(
                "/World/Sensor",
                $"custom:flags{index}",
                LiveAttributeValue.FromBooleanArray(maxBooleans)));
        }

        ArgumentException exception = CaptureArgument(() => new LiveAuthoringBatch(1, updates));
        await Assert.That(exception.ParamName).IsEqualTo("updates");
    }

    [Test]
    public async Task PureValidationEnforcesCollectionAndTextLengthBounds()
    {
        // An oversized attribute array (for example DoubleArray) is rejected earlier, by
        // LiveAttributeValue.From*Array itself before it copies its input; see
        // LiveAttributeValueArrayFactoriesRejectOversizedInputsBeforeCopying. Targets and
        // orientations are plain record fields with no equivalent public factory, so this batch-level
        // check is still their first and only opportunity to reject an oversized collection.
        string tooLongIdentifier = new('a', LiveAuthoringValidation.MaxIdentifierLength + 1);
        string tooLongPath = "/" + new string('a', LiveAuthoringValidation.MaxPathLength);
        string tooLongText = new('a', LiveAuthoringValidation.MaxTextValueLength + 1);
        string[] tooManyTargets = Enumerable
            .Repeat("/Target", LiveAuthoringValidation.MaxCollectionElementCount + 1)
            .ToArray();
        var tooManyOrientations = new UsdQuatf[LiveAuthoringValidation.MaxCollectionElementCount + 1];

        (LiveStageUpdate Update, string ParameterName)[] cases =
        [
            (new DefinePrimUpdate(tooLongPath), "updates[0].PrimPath"),
            (new DefinePrimUpdate("/World", tooLongIdentifier), "updates[0].TypeName"),
            (
                new SetAttributeUpdate("/World", tooLongIdentifier, LiveAttributeValue.FromInt64(1)),
                "updates[0].AttributeName"),
            (
                new SetAttributeUpdate("/World", "custom:value", LiveAttributeValue.FromString(tooLongText)),
                "updates[0].Value.StringValue"),
            (new SetReferenceUpdate("/World", tooLongText), "updates[0].AssetPath"),
            (
                new SetRelationshipTargetsUpdate("/World", "custom:targets", tooManyTargets),
                "updates[0].Targets"),
            (
                new SetPointInstancerOrientationsUpdate("/World", tooManyOrientations),
                "updates[0].Orientations"),
            (
                new SetMetadataUpdate("/World", tooLongIdentifier, LiveMetadataValue.FromBoolean(true)),
                "updates[0].Key"),
            (
                new ApiSchemaUpdate("/World", tooLongIdentifier, LiveApiSchemaOperation.Apply),
                "updates[0].SchemaToken")
        ];

        foreach ((LiveStageUpdate update, string parameterName) in cases)
        {
            ArgumentException exception = CaptureArgument(() => new LiveAuthoringBatch(1, [update]));
            await Assert.That(exception.ParamName).IsEqualTo(parameterName);
        }
    }

    [Test]
    public async Task PureValidationRejectsNonFiniteNumericValues()
    {
        (LiveStageUpdate Update, string ParameterName)[] cases =
        [
            (
                new SetAttributeUpdate("/World", "custom:value", LiveAttributeValue.FromDouble(double.NaN)),
                "updates[0].Value.DoubleValue"),
            (
                new SetAttributeUpdate(
                    "/World",
                    "custom:value",
                    LiveAttributeValue.FromDouble(double.PositiveInfinity)),
                "updates[0].Value.DoubleValue"),
            (
                new SetAttributeUpdate(
                    "/World",
                    "custom:vec",
                    LiveAttributeValue.FromVec3f(new UsdVec3f(float.NaN, 0, 0))),
                "updates[0].Value.Vec3f.X"),
            (
                new SetAttributeUpdate(
                    "/World",
                    "custom:mat",
                    LiveAttributeValue.FromMatrix4d(new UsdMatrix4d(
                        double.NaN, 0, 0, 0,
                        0, 0, 0, 0,
                        0, 0, 0, 0,
                        0, 0, 0, 1))),
                "updates[0].Value.Matrix4d[0]"),
            (
                new SetAttributeUpdate(
                    "/World",
                    "custom:floats",
                    LiveAttributeValue.FromFloatArray([1f, float.NaN])),
                "updates[0].Value.FloatArray[1]"),
            (
                new SetAttributeUpdate(
                    "/World",
                    "custom:doubles",
                    LiveAttributeValue.FromDoubleArray([1.0, double.PositiveInfinity])),
                "updates[0].Value.DoubleArray[1]"),
            (
                new SetAttributeUpdate(
                    "/World",
                    "custom:vec2s",
                    LiveAttributeValue.FromVec2fArray([new UsdVec2f(0, 0), new UsdVec2f(float.NaN, 0)])),
                "updates[0].Value.Vec2fArray[1].X"),
            (
                new SetAttributeUpdate(
                    "/World",
                    "custom:vec3s",
                    LiveAttributeValue.FromVec3fArray(
                        [new UsdVec3f(0, 0, 0), new UsdVec3f(0, float.NaN, 0)])),
                "updates[0].Value.Vec3fArray[1].Y"),
            (
                new SetAttributeUpdate(
                    "/World",
                    "custom:colors",
                    LiveAttributeValue.FromColor3fArray([new UsdVec3f(0, 0, float.NaN)])),
                "updates[0].Value.Color3fArray[0].Z"),
            (
                new SetMetadataUpdate("/World", "customData:value", LiveMetadataValue.FromDouble(double.NaN)),
                "updates[0].Value.DoubleValue"),
            (
                new SetPointInstancerOrientationsUpdate(
                    "/World",
                    [new UsdQuatf(float.NaN, 0, 0, 0)]),
                "updates[0].Orientations[0].Real")
        ];

        foreach ((LiveStageUpdate update, string parameterName) in cases)
        {
            ArgumentException exception = CaptureArgument(() => new LiveAuthoringBatch(1, [update]));
            await Assert.That(exception.ParamName).IsEqualTo(parameterName);
        }
    }

    [Test]
    public async Task PureValidationEnforcesEstimatedByteBudgetForTextPayloads()
    {
        string maxLengthText = new('a', LiveAuthoringValidation.MaxTextValueLength);
        int elementsNeeded =
            (int)(LiveAuthoringValidation.MaxEstimatedBatchPayloadBytes /
                LiveAuthoringValidation.MaxTextValueLength) + 1;
        string[] hugeTextArray = Enumerable.Repeat(maxLengthText, elementsNeeded).ToArray();
        var update = new SetAttributeUpdate(
            "/World/Sensor",
            "custom:notes",
            LiveAttributeValue.FromStringArray(hugeTextArray));

        ArgumentException exception = CaptureArgument(() => new LiveAuthoringBatch(1, [update]));
        await Assert.That(exception.ParamName).IsEqualTo("updates");
        await Assert.That(exception.Message).Contains("estimated retained payload");
    }

    [Test]
    public async Task PureValidationEnforcesEstimatedByteBudgetForNumericPayloads()
    {
        var maxDoubles = new double[LiveAuthoringValidation.MaxCollectionElementCount];
        long bytesPerUpdate = 8L * LiveAuthoringValidation.MaxCollectionElementCount;
        int updatesNeeded =
            (int)(LiveAuthoringValidation.MaxEstimatedBatchPayloadBytes / bytesPerUpdate) + 1;
        var updates = new List<LiveStageUpdate>();
        for (int index = 0; index < updatesNeeded; index++)
        {
            updates.Add(new SetAttributeUpdate(
                "/World/Sensor",
                $"custom:samples{index}",
                LiveAttributeValue.FromDoubleArray(maxDoubles)));
        }

        ArgumentException exception = CaptureArgument(() => new LiveAuthoringBatch(1, updates));
        await Assert.That(exception.ParamName).IsEqualTo("updates");
        await Assert.That(exception.Message).Contains("estimated retained payload");
    }

    [Test]
    public async Task LiveAttributeValueArrayFactoriesRejectOversizedInputsBeforeCopying()
    {
        var tooManyDoubles = new double[LiveAuthoringValidation.MaxCollectionElementCount + 1];
        await Assert.That(() => LiveAttributeValue.FromDoubleArray(tooManyDoubles))
            .Throws<ArgumentException>();

        var tooManyBooleans = new bool[LiveAuthoringValidation.MaxCollectionElementCount + 1];
        await Assert.That(() => LiveAttributeValue.FromBooleanArray(tooManyBooleans))
            .Throws<ArgumentException>();

        string[] tooLongTokenElement =
            ["ok", new string('a', LiveAuthoringValidation.MaxTextValueLength + 1)];
        await Assert.That(() => LiveAttributeValue.FromTokenArray(tooLongTokenElement))
            .Throws<ArgumentException>();

        string[] tooManyStrings = Enumerable
            .Repeat("ok", LiveAuthoringValidation.MaxCollectionElementCount + 1)
            .ToArray();
        await Assert.That(() => LiveAttributeValue.FromStringArray(tooManyStrings))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task AggregateBoundsAcceptExactlyAtTheLimitAndRejectOneOver()
    {
        // The batch also accounts for the small PrimPath/AttributeName text overhead alongside the
        // string array, so "at the limit" here means comfortably under it, and "one over" uses a full
        // extra max-length element rather than chasing an exact byte boundary.
        string maxLengthText = new('a', LiveAuthoringValidation.MaxTextValueLength);
        int elementsAtLimit =
            (int)(LiveAuthoringValidation.MaxEstimatedBatchPayloadBytes /
                LiveAuthoringValidation.MaxTextValueLength);
        string[] underLimitArray = Enumerable.Repeat(maxLengthText, elementsAtLimit - 1).ToArray();

        _ = new LiveAuthoringBatch(
            1,
            [
                new SetAttributeUpdate(
                    "/World/Sensor",
                    "custom:notes",
                    LiveAttributeValue.FromStringArray(underLimitArray))
            ]);

        string[] overLimitArray = Enumerable.Repeat(maxLengthText, elementsAtLimit + 1).ToArray();
        await Assert.That(() => new LiveAuthoringBatch(
                1,
                [
                    new SetAttributeUpdate(
                        "/World/Sensor",
                        "custom:notes",
                        LiveAttributeValue.FromStringArray(overLimitArray))
                ]))
            .Throws<ArgumentException>();
    }

    public sealed class OpcUaPumpFinalAcceptanceTests
    {
        [Test]
        public async Task SerializedPumpMapsEveryMonitoredSequenceToAnOrderedStageUpdate()
        {
            const int sampleCount = 240;
            var executor = new SourceSequenceStageProbe(sampleCount);
            await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 8);
            var pumpSink = new SerializedPumpSink(sink);

            List<Task<LiveAuthoringBatchResult>> pending = [];
            for (int first = 1; first <= sampleCount; first += 4)
            {
                pending.Add(pumpSink
                    .ApplyAsync(PumpBatch.Create(first, Math.Min(4, sampleCount - first + 1)))
                    .AsTask());
            }

            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10));

            long[] expected = Enumerable.Range(1, sampleCount)
                .Select(static value => (long)value)
                .ToArray();
            await Assert.That(executor.SourceSequences).IsEquivalentTo(expected);
            await Assert.That(executor.SourceSequences).IsInOrder();
            await Assert.That(sink.PeakPendingBatchCount).IsLessThanOrEqualTo(sink.Capacity);

            Console.WriteLine(
                $"OPCUA_ORDER_GREEN samples={executor.SourceSequences.Count}; " +
                $"batches={executor.BatchSequences.Count}; peakPending={sink.PeakPendingBatchCount}");

            await Assert.That(static () => SourceSequenceStageProbe.RequireStrictlyConsecutive([1, 2, 4]))
                .Throws<InvalidOperationException>();
            await Assert.That(
                    static async () => await SourceSequenceStageProbe.ExecuteBrokenBatchAsync(2))
                .Throws<InvalidOperationException>();
            Console.WriteLine("OPCUA_ORDER_RED gap=detected; reorderedStageSample=detected");
        }

        [Test]
        public async Task SustainedCoalescedSnapshotsStayBoundedAndExposeBrokenCapacityChecks()
        {
            const int batchCount = 400;
            var executor = new BlockingSnapshotExecutor();
            await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 2);
            long beforeWorkingSet = Environment.WorkingSet;

            Task<LiveAuthoringAdmissionReceipt> first = sink.ApplyAsync(SnapshotBatch(1)).AsTask();
            await executor.FirstStarted.WaitAsync(TimeSpan.FromSeconds(5));
            Task<LiveAuthoringAdmissionReceipt>[] pending = Enumerable.Range(2, batchCount - 1)
                .Select(sequence => sink.ApplyAsync(SnapshotBatch(sequence)).AsTask())
                .ToArray();

            LiveAuthoringAdmissionReceipt firstReceipt = await first;
            LiveAuthoringAdmissionReceipt[] pendingReceipts = await Task.WhenAll(pending);
            executor.Release();
            LiveAuthoringBatchResult[] results = await Task
                .WhenAll([firstReceipt.Applied, .. pendingReceipts.Select(static r => r.Applied)])
                .WaitAsync(TimeSpan.FromSeconds(10));
            long afterWorkingSet = Environment.WorkingSet;

            await Assert.That(sink.PeakPendingBatchCount).IsLessThanOrEqualTo(sink.Capacity);
            await Assert.That(sink.CoalescedBatchCount).IsGreaterThan(0);
            await Assert.That(executor.ExecutedSequences.Count).IsLessThan(batchCount);
            await Assert.That(results.Max(static result => result.LastSequence)).IsEqualTo(batchCount);

            Console.WriteLine(
                $"OPCUA_BOUNDED_GREEN batches={batchCount}; executed={executor.ExecutedSequences.Count}; " +
                $"coalesced={sink.CoalescedBatchCount}; peakPending={sink.PeakPendingBatchCount}; " +
                $"capacity={sink.Capacity}; workingSetDelta={afterWorkingSet - beforeWorkingSet}");

            await Assert.That(static () => RequireBounded(peakPending: 3, capacity: 2))
                .Throws<InvalidOperationException>();
            await Assert.That(static () => RequireCoalescing(coalesced: 0))
                .Throws<InvalidOperationException>();
            Console.WriteLine("OPCUA_BOUNDED_RED capacityOverflow=detected; missingCoalescing=detected");
        }

        [Test]
        public async Task ConcurrentPumpCallbacksMustSerializeBeforeTheSinkToAvoidMissedUpdates()
        {
            const int sampleCount = 300;
            var executor = new SourceSequenceStageProbe(sampleCount);
            await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 16);
            var pumpSink = new SerializedPumpSink(sink);
            PumpBatch[] batches = Enumerable.Range(1, sampleCount)
                .Select(static sequence => PumpBatch.Create(sequence, 1))
                .ToArray();

            Task[] callbacks = batches
                .Select(batch => pumpSink.ApplyAsync(batch, CancellationToken.None).AsTask())
                .ToArray();

            await Task.WhenAll(callbacks).WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(executor.SourceSequences.Count).IsEqualTo(sampleCount);
            await Assert.That(executor.SourceSequences).IsInOrder();

            Console.WriteLine(
                $"OPCUA_RACE_GREEN samples={executor.SourceSequences.Count}; " +
                $"peakPending={sink.PeakPendingBatchCount}; batches={executor.BatchSequences.Count}");

            var broken = new SerializedPumpSink(new QueuedLiveAuthoringSink(new SourceSequenceStageProbe(0), 4));
            try
            {
                await broken.ApplyAsync(new PumpBatch([new PumpSample(2, 10, "Running")]));
                throw new InvalidOperationException("Broken callback order was not detected.");
            }
            catch (InvalidOperationException exception)
                when (exception.Message.Contains("strictly increasing", StringComparison.Ordinal))
            {
                Console.WriteLine("OPCUA_RACE_RED lateOrMissingSourceSequence=detected");
            }
            finally
            {
                await broken.DisposeAsync();
            }
        }

        private static LiveAuthoringBatch SnapshotBatch(int sequence) =>
            new(
                sequence,
                [
                    new DefinePrimUpdate("/Plant", "Xform"),
                    new DefinePrimUpdate("/Plant/Pump1", "Xform"),
                    new SetAttributeUpdate(
                        "/Plant/Pump1",
                        "custom:pressure",
                        LiveAttributeValue.FromDouble(100 + sequence),
                        TimeCode: sequence),
                    new SetAttributeUpdate(
                        "/Plant/Pump1",
                        "custom:sourceSequence",
                        LiveAttributeValue.FromInt64(sequence),
                        TimeCode: sequence)
                ],
                coalescingKey: "plant:pump1:snapshot");

        private static void RequireBounded(int peakPending, int capacity)
        {
            if (peakPending > capacity)
            {
                throw new InvalidOperationException(
                    $"Pending batches exceeded capacity: peak={peakPending}, capacity={capacity}.");
            }
        }

        private static void RequireCoalescing(long coalesced)
        {
            if (coalesced <= 0)
            {
                throw new InvalidOperationException("Sustained snapshot load did not coalesce.");
            }
        }

        private sealed class SerializedPumpSink(ILiveAuthoringSink inner) : IAsyncDisposable
        {
            private readonly SemaphoreSlim _gate = new(1, 1);
            private long _lastSourceSequence;
            private long _nextBatchSequence;
            private bool _primDefined;

            public async ValueTask<LiveAuthoringBatchResult> ApplyAsync(
                PumpBatch batch,
                CancellationToken cancellationToken = default)
            {
                await _gate.WaitAsync(cancellationToken);
                LiveAuthoringAdmissionReceipt receipt;
                try
                {
                    var updates = new List<LiveStageUpdate>();
                    if (!_primDefined)
                    {
                        updates.Add(new DefinePrimUpdate("/Plant", "Xform"));
                        updates.Add(new DefinePrimUpdate("/Plant/Pump1", "Xform"));
                        _primDefined = true;
                    }

                    foreach (PumpSample sample in batch.Samples)
                    {
                        if (sample.SourceSequence != _lastSourceSequence + 1)
                        {
                            throw new InvalidOperationException(
                                "Pump source sequences must be strictly increasing.");
                        }

                        _lastSourceSequence = sample.SourceSequence;
                        updates.Add(new SetAttributeUpdate(
                            "/Plant/Pump1",
                            "custom:sourceSequence",
                            LiveAttributeValue.FromInt64(sample.SourceSequence),
                            TimeCode: sample.SourceSequence));
                        updates.Add(new SetAttributeUpdate(
                            "/Plant/Pump1",
                            "custom:pressure",
                            LiveAttributeValue.FromDouble(sample.Pressure),
                            TimeCode: sample.SourceSequence));
                        updates.Add(new SetAttributeUpdate(
                            "/Plant/Pump1",
                            "custom:state",
                            LiveAttributeValue.FromToken(sample.State),
                            TimeCode: sample.SourceSequence));
                    }

                    receipt = await inner.ApplyAsync(
                        new LiveAuthoringBatch(++_nextBatchSequence, updates),
                        cancellationToken);
                }
                finally
                {
                    _gate.Release();
                }

                return await receipt.Applied.ConfigureAwait(false);
            }

            public async ValueTask DisposeAsync()
            {
                if (inner is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync();
                }
                _gate.Dispose();
            }
        }

        private sealed class SourceSequenceStageProbe(int expectedSamples) : ILiveAuthoringBatchExecutor
        {
            private long _nextSourceSequence = 1;

            public List<long> BatchSequences { get; } = [];

            public List<long> SourceSequences { get; } = [];

            public static void RequireStrictlyConsecutive(IReadOnlyList<long> sequences)
            {
                for (int index = 0; index < sequences.Count; index++)
                {
                    long expected = index + 1L;
                    if (sequences[index] != expected)
                    {
                        throw new InvalidOperationException(
                            $"Expected source sequence {expected}, saw {sequences[index]}.");
                    }
                }
            }

            public static async Task ExecuteBrokenBatchAsync(long firstSourceSequence)
            {
                var probe = new SourceSequenceStageProbe(0);
                await probe.ExecuteAsync(
                    new LiveAuthoringBatch(
                        1,
                        [
                            new SetAttributeUpdate(
                                "/Plant/Pump1",
                                "custom:sourceSequence",
                                LiveAttributeValue.FromInt64(firstSourceSequence),
                                TimeCode: firstSourceSequence)
                        ]),
                    CancellationToken.None);
            }

            public ValueTask<LiveAuthoringBatchResult> ExecuteAsync(
                LiveAuthoringBatch batch,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BatchSequences.Add(batch.Sequence);
                foreach (SetAttributeUpdate update in batch.Updates.OfType<SetAttributeUpdate>())
                {
                    if (!string.Equals(
                            update.AttributeName,
                            "custom:sourceSequence",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    long sourceSequence = update.Value.Int64Value;
                    if (sourceSequence != _nextSourceSequence)
                    {
                        throw new InvalidOperationException(
                            $"Expected source sequence {_nextSourceSequence}, saw {sourceSequence}.");
                    }

                    SourceSequences.Add(sourceSequence);
                    _nextSourceSequence++;
                }

                return ValueTask.FromResult(new LiveAuthoringBatchResult(
                    batch.Sequence,
                    batch.Sequence,
                    1,
                    batch.Updates.Count,
                    batch.Invalidation,
                    (ulong)batch.Sequence,
                    (ulong)batch.Sequence + 1,
                    "session"));
            }

            public ValueTask DisposeAsync()
            {
                if (expectedSamples > 0 && SourceSequences.Count != expectedSamples)
                {
                    throw new InvalidOperationException(
                        $"Expected {expectedSamples} source samples, saw {SourceSequences.Count}.");
                }

                return ValueTask.CompletedTask;
            }
        }

        private sealed class BlockingSnapshotExecutor : ILiveAuthoringBatchExecutor
        {
            private readonly TaskCompletionSource _firstStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource _release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _executionCount;

            public Task FirstStarted => _firstStarted.Task;

            public List<long> ExecutedSequences { get; } = [];

            public async ValueTask<LiveAuthoringBatchResult> ExecuteAsync(
                LiveAuthoringBatch batch,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ExecutedSequences.Add(batch.Sequence);
                if (Interlocked.Increment(ref _executionCount) == 1)
                {
                    _firstStarted.TrySetResult();
                    await _release.Task.ConfigureAwait(false);
                }

                return new LiveAuthoringBatchResult(
                    batch.Sequence,
                    batch.Sequence,
                    1,
                    batch.Updates.Count,
                    batch.Invalidation,
                    (ulong)batch.Sequence,
                    (ulong)batch.Sequence + 1,
                    "session");
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public void Release() => _release.TrySetResult();
        }

        private sealed record PumpBatch(IReadOnlyList<PumpSample> Samples)
        {
            public static PumpBatch Create(int firstSequence, int count) =>
                new(Enumerable
                    .Range(firstSequence, count)
                    .Select(static sequence => new PumpSample(
                        sequence,
                        10 + sequence * 0.25,
                        sequence % 2 == 0 ? "Running" : "Idle"))
                    .ToArray());
        }

        private sealed record PumpSample(long SourceSequence, double Pressure, string State);
    }

    private static LiveAuthoringBatch Batch(long sequence, string? key = null) =>
        new(
            sequence,
            [
                new SetAttributeUpdate(
                    "/World/Sensor",
                    "custom:value",
                    LiveAttributeValue.FromInt64(sequence))
            ],
            key);

    private static ArgumentException CaptureArgument(Func<LiveAuthoringBatch> action)
    {
        try
        {
            _ = action();
        }
        catch (ArgumentException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected batch validation to fail.");
    }

    private sealed class RecordingExecutor(
        bool blockFirst = false,
        long failSequence = 0) : ILiveAuthoringBatchExecutor
    {
        private readonly TaskCompletionSource _firstStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _executionCount;

        public bool Disposed { get; private set; }

        public Task FirstStarted => _firstStarted.Task;

        public Task FirstCompleted => _firstCompleted.Task;

        public List<long> CompletedSequences { get; } = [];

        public List<long> Sequences { get; } = [];

        public async ValueTask<LiveAuthoringBatchResult> ExecuteAsync(
            LiveAuthoringBatch batch,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sequences.Add(batch.Sequence);
            if (batch.Sequence == failSequence)
            {
                throw new InvalidOperationException("Deterministic executor failure.");
            }
            int executionCount = Interlocked.Increment(ref _executionCount);
            if (executionCount == 1)
            {
                _firstStarted.TrySetResult();
                if (blockFirst)
                {
                    await _release.Task.ConfigureAwait(false);
                }
            }

            CompletedSequences.Add(batch.Sequence);
            if (executionCount == 1)
            {
                _firstCompleted.TrySetResult();
            }
            return new LiveAuthoringBatchResult(
                batch.Sequence,
                batch.Sequence,
                1,
                batch.Updates.Count,
                batch.Invalidation,
                (ulong)batch.Sequence,
                (ulong)batch.Sequence + 1,
                "session",
                batch.CorrelationId,
                batch.OriginId);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class SideEffectRecordingExecutor : ILiveAuthoringBatchExecutor
    {
        public List<string> AppliedSideEffects { get; } = [];

        public ValueTask<LiveAuthoringBatchResult> ExecuteAsync(
            LiveAuthoringBatch batch,
            CancellationToken cancellationToken)
        {
            foreach (LiveStageUpdate update in batch.Updates)
            {
                switch (update)
                {
                    case SetAttributeUpdate attribute:
                        AppliedSideEffects.Add(attribute.AttributeName);
                        break;
                    case ApiSchemaUpdate { Operation: LiveApiSchemaOperation.Remove } apiSchema:
                        throw new NotSupportedException(
                            $"Removing '{apiSchema.SchemaToken}' is not supported.");
                }
            }

            return ValueTask.FromResult(new LiveAuthoringBatchResult(
                batch.Sequence,
                batch.Sequence,
                1,
                batch.Updates.Count,
                batch.Invalidation,
                0,
                1,
                "session"));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingHealthObserver : IProgress<LiveAuthoringHealthEvent>
    {
        private readonly object _gate = new();
        private readonly List<LiveAuthoringHealthEvent> _events = [];

        public IReadOnlyList<LiveAuthoringHealthEvent> Events
        {
            get
            {
                lock (_gate)
                {
                    return [.. _events];
                }
            }
        }

        public void Report(LiveAuthoringHealthEvent value)
        {
            lock (_gate)
            {
                _events.Add(value);
            }
        }
    }

    /// <summary>
    /// A deliberately broken health observer used to prove that <see cref="QueuedLiveAuthoringSink"/>
    /// isolates observer exceptions instead of letting them fail admission, execution, or disposal.
    /// </summary>
    private sealed class ThrowingHealthObserver : IProgress<LiveAuthoringHealthEvent>
    {
        private int _reportAttempts;

        public int ReportAttempts => Volatile.Read(ref _reportAttempts);

        public void Report(LiveAuthoringHealthEvent value)
        {
            Interlocked.Increment(ref _reportAttempts);
            throw new InvalidOperationException("The health observer deliberately fails.");
        }
    }
}
