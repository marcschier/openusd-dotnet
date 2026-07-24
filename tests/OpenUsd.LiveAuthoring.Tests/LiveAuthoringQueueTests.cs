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

        Task<LiveAuthoringBatchResult>[] results =
        [
            sink.ApplyAsync(Batch(1)).AsTask(),
            sink.ApplyAsync(Batch(2)).AsTask(),
            sink.ApplyAsync(Batch(3)).AsTask()
        ];
        await Task.WhenAll(results);

        await Assert.That(executor.Sequences).IsEquivalentTo([1L, 2L, 3L]);
        await Assert.That(executor.Sequences).IsInOrder();
    }

    [Test]
    public async Task SubmissionSequencesMustStrictlyIncrease()
    {
        var executor = new RecordingExecutor();
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 1);

        _ = await sink.ApplyAsync(Batch(2));
        await Assert.That(
                async () => await sink.ApplyAsync(Batch(1)))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(executor.Sequences).IsEquivalentTo([2L]);
    }

    [Test]
    public async Task CancellationWhileBackpressuredDoesNotAdmitBatch()
    {
        var executor = new RecordingExecutor(blockFirst: true);
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 1);
        Task<LiveAuthoringBatchResult> first = sink.ApplyAsync(Batch(1)).AsTask();
        await executor.FirstStarted;
        Task<LiveAuthoringBatchResult> second = sink.ApplyAsync(Batch(2)).AsTask();
        using var cancellation = new CancellationTokenSource();
        Task<LiveAuthoringBatchResult> third =
            sink.ApplyAsync(Batch(3), cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.That(async () => await third).Throws<OperationCanceledException>();
        executor.Release();
        await Task.WhenAll(first, second);
        await Assert.That(executor.Sequences).IsEquivalentTo([1L, 2L]);
    }

    [Test]
    public async Task CancellationAfterAdmissionCancelsOnlyWait()
    {
        var executor = new RecordingExecutor(blockFirst: true);
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 1);
        using var cancellation = new CancellationTokenSource();
        Task<LiveAuthoringBatchResult> submitted =
            sink.ApplyAsync(Batch(1), cancellation.Token).AsTask();
        await executor.FirstStarted;

        cancellation.Cancel();
        await Assert.That(async () => await submitted).Throws<OperationCanceledException>();
        executor.Release();
        await executor.FirstCompleted;

        await Assert.That(executor.Sequences).IsEquivalentTo([1L]);
        await Assert.That(executor.CompletedSequences).IsEquivalentTo([1L]);
    }

    [Test]
    public async Task FullQueueCoalescesOnlyMatchingTailSnapshot()
    {
        var executor = new RecordingExecutor(blockFirst: true);
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 1);
        Task<LiveAuthoringBatchResult> first = sink.ApplyAsync(Batch(1)).AsTask();
        await executor.FirstStarted;
        Task<LiveAuthoringBatchResult> superseded =
            sink.ApplyAsync(Batch(2, "temperature")).AsTask();
        Task<LiveAuthoringBatchResult> latest =
            sink.ApplyAsync(Batch(3, "temperature")).AsTask();

        executor.Release();
        LiveAuthoringBatchResult[] results = await Task.WhenAll(first, superseded, latest);

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
        Task<LiveAuthoringBatchResult> first = sink.ApplyAsync(Batch(1)).AsTask();
        await executor.FirstStarted;
        Task<LiveAuthoringBatchResult> second = sink.ApplyAsync(Batch(2)).AsTask();
        ValueTask disposal = sink.DisposeAsync();

        executor.Release();
        await Task.WhenAll(first, second);
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

        await Assert.That(
                async () => await sink.ApplyAsync(Batch(1)))
            .Throws<InvalidOperationException>();
        LiveAuthoringBatchResult result = await sink.ApplyAsync(Batch(2));

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
        await Assert.That(typeof(IUsdDetachedResult).IsAssignableFrom(typeof(LiveAuthoringBatchResult)))
            .IsTrue();
        await Assert.That(apply.ReturnType).IsEqualTo(typeof(ValueTask<LiveAuthoringBatchResult>));
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
                new SetScalarUpdate(
                    "/World/Sensor",
                    "custom:value",
                    LiveScalarValue.FromDouble(1)),
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
                    new SetScalarUpdate(
                        "/World",
                        "custom::invalid",
                        LiveScalarValue.FromDouble(1))
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

        await Assert.That(targets.ParamName).IsEqualTo("updates[0].Targets");
        await Assert.That(variants.ParamName).IsEqualTo("updates[0].KnownVariants");
    }

    [Test]
    public async Task PureValidationRejectsMalformedUpdateFields()
    {
        (LiveStageUpdate Update, string ParameterName)[] cases =
        [
            (new DefinePrimUpdate("World", "Xform"), "updates[0].PrimPath"),
            (
                new SetScalarUpdate(
                    "/World",
                    "custom::value",
                    LiveScalarValue.FromDouble(1)),
                "updates[0].AttributeName"),
            (
                new SetScalarUpdate(
                    "/World",
                    "custom:value",
                    LiveScalarValue.FromDouble(1),
                    double.PositiveInfinity),
                "updates[0].TimeCode"),
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
                "updates[0].Selection")
        ];

        foreach ((LiveStageUpdate update, string parameterName) in cases)
        {
            ArgumentException exception = CaptureArgument(
                () => new LiveAuthoringBatch(1, [update]));
            await Assert.That(exception.ParamName).IsEqualTo(parameterName);
        }
    }

    private static LiveAuthoringBatch Batch(long sequence, string? key = null) =>
        new(
            sequence,
            [
                new SetScalarUpdate(
                    "/World/Sensor",
                    "custom:value",
                    LiveScalarValue.FromInt64(sequence))
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
                "session");
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        public void Release() => _release.TrySetResult();
    }
}
