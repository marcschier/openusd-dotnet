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

            Task<LiveAuthoringBatchResult> first = sink.ApplyAsync(SnapshotBatch(1)).AsTask();
            await executor.FirstStarted.WaitAsync(TimeSpan.FromSeconds(5));
            Task<LiveAuthoringBatchResult>[] pending = Enumerable.Range(2, batchCount - 1)
                .Select(sequence => sink.ApplyAsync(SnapshotBatch(sequence)).AsTask())
                .ToArray();

            executor.Release();
            LiveAuthoringBatchResult[] results =
                await Task.WhenAll([first, .. pending]).WaitAsync(TimeSpan.FromSeconds(10));
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
                .Select(sequence => PumpBatch.Create(sequence, 1))
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
                    new SetScalarUpdate(
                        "/Plant/Pump1",
                        "custom:pressure",
                        LiveScalarValue.FromDouble(100 + sequence),
                        TimeCode: sequence),
                    new SetScalarUpdate(
                        "/Plant/Pump1",
                        "custom:sourceSequence",
                        LiveScalarValue.FromInt64(sequence),
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
                        updates.Add(new SetScalarUpdate(
                            "/Plant/Pump1",
                            "custom:sourceSequence",
                            LiveScalarValue.FromInt64(sample.SourceSequence),
                            TimeCode: sample.SourceSequence));
                        updates.Add(new SetScalarUpdate(
                            "/Plant/Pump1",
                            "custom:pressure",
                            LiveScalarValue.FromDouble(sample.Pressure),
                            TimeCode: sample.SourceSequence));
                        updates.Add(new SetScalarUpdate(
                            "/Plant/Pump1",
                            "custom:state",
                            LiveScalarValue.FromToken(sample.State),
                            TimeCode: sample.SourceSequence));
                    }

                    return await inner.ApplyAsync(
                        new LiveAuthoringBatch(++_nextBatchSequence, updates),
                        cancellationToken);
                }
                finally
                {
                    _gate.Release();
                }
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
                            new SetScalarUpdate(
                                "/Plant/Pump1",
                                "custom:sourceSequence",
                                LiveScalarValue.FromInt64(firstSourceSequence),
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
                foreach (SetScalarUpdate update in batch.Updates.OfType<SetScalarUpdate>())
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
