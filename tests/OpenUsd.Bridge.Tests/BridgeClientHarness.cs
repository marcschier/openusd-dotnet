// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Grpc;
using OpenUsd.Bridge.Protocol;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Tests;

/// <summary>
/// Wires one coordinator, one in-memory peer, and one client together, and exposes the waits a test
/// needs without sleeping on a fixed duration.
/// </summary>
internal sealed class BridgeClientHarness : IAsyncDisposable
{
    internal const string Token = "ephemeral-session-token";

    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<BridgeClientEvent> _events = [];
    private readonly object _gate = new();
    private readonly RecordingOverlayExecutor _executor;
    private readonly QueuedLiveAuthoringSink _sink;

    private BridgeClientHarness(
        FakeBridgeServer server,
        RecordingOverlayExecutor executor,
        QueuedLiveAuthoringSink sink,
        LiveAuthoringSessionCoordinator coordinator,
        OmniverseBridgeClient client)
    {
        Server = server;
        _executor = executor;
        _sink = sink;
        Coordinator = coordinator;
        Client = client;
        Run = Task.CompletedTask;
    }

    internal FakeBridgeServer Server { get; }

    internal LiveAuthoringSessionCoordinator Coordinator { get; }

    internal OmniverseBridgeClient Client { get; }

    internal Task Run { get; private set; }

    internal IReadOnlyList<BridgeClientEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    internal int AppliedBatchCount => _executor.AppliedBatchCount;

    internal static async Task<BridgeClientHarness> StartAsync(
        FakeBridgeServer? server = null,
        bool waitForStreaming = true,
        int outboundCapacity = 64,
        bool startRunLoop = true,
        int maxPublishAttempts = 3,
        IProgress<BridgeClientEvent>? observer = null,
        string? requestedSessionId = null,
        BridgeConnectionFactory? connectionFactory = null)
    {
        server ??= new FakeBridgeServer();
        var executor = new RecordingOverlayExecutor();
        var sink = new QueuedLiveAuthoringSink(executor, capacity: 16);
        var coordinator = new LiveAuthoringSessionCoordinator(
            sink,
            new LiveAuthoringSessionOptions
            {
                BridgeRootPath = BridgeTestData.BridgeRoot,
                LocalOriginId = BridgeTestData.LocalOrigin
            });

        BridgeClientHarness? harness = null;
        var options = new BridgeClientOptions
        {
            Endpoint = new Uri("http://127.0.0.1:53017"),
            Credentials = new EphemeralBearerTokenProvider(Token, DateTimeOffset.UtcNow.AddHours(1)),
            BridgeRootPath = BridgeTestData.BridgeRoot,
            LocalOriginId = BridgeTestData.LocalOrigin,
            RequestedSessionId = requestedSessionId,
            InitialBackoff = TimeSpan.FromMilliseconds(10),
            MaxBackoff = TimeSpan.FromMilliseconds(50),
            OutboundQueueCapacity = outboundCapacity,
            MaxPublishAttempts = maxPublishAttempts,
            Observer = observer ?? new DelegateProgress(observed => harness?.Record(observed))
        };

        var client = new OmniverseBridgeClient(
            coordinator,
            options,
            connectionFactory ??
                (_ => ValueTask.FromResult(new BridgeConnection(server, owned: null))));
        harness = new BridgeClientHarness(server, executor, sink, coordinator, client);
        if (startRunLoop)
        {
            harness.Run = Task.Run(() => client.RunAsync(harness._lifetime.Token));
        }
        if (waitForStreaming)
        {
            await harness.WaitForStateAsync(BridgeConnectionState.Streaming).ConfigureAwait(false);
        }

        return harness;
    }

    internal BridgeLocalBatch LocalBatch(long sequence) =>
        new(
            BridgeTestData.Epoch(Server.Epoch),
            sequence,
            [new SetActiveUpdate($"{BridgeTestData.BridgeRoot}/Cube", sequence % 2 == 0)],
            BridgeTestData.LocalOrigin,
            correlationId: $"local-{sequence}");

    /// <summary>Creates a batch whose only update needs an optional capability.</summary>
    internal BridgeLocalBatch CapabilityBoundLocalBatch(long sequence) =>
        new(
            BridgeTestData.Epoch(Server.Epoch),
            sequence,
            [
                new ApiSchemaUpdate(
                    $"{BridgeTestData.BridgeRoot}/Cube",
                    "AssetPreviewsAPI",
                    LiveApiSchemaOperation.Apply)
            ],
            BridgeTestData.LocalOrigin,
            correlationId: $"local-{sequence}");

    /// <summary>Creates a batch with more updates than a lowered negotiated bound allows.</summary>
    internal BridgeLocalBatch OversizedLocalBatch(long sequence, int updateCount)
    {
        var updates = new List<LiveStageUpdate>(updateCount);
        for (int index = 0; index < updateCount; index++)
        {
            updates.Add(new SetActiveUpdate(
                $"{BridgeTestData.BridgeRoot}/Cube{index.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)}",
                true));
        }

        return new BridgeLocalBatch(
            BridgeTestData.Epoch(Server.Epoch),
            sequence,
            updates,
            BridgeTestData.LocalOrigin,
            correlationId: $"local-{sequence}");
    }

    /// <summary>
    /// Creates a batch the authoring layer accepts and the wire cannot carry.
    /// </summary>
    /// <remarks>
    /// The authoring estimate charges four bytes for an int32 element. Protobuf spends ten on a
    /// negative one, because a negative <c>int32</c> is encoded as a sign-extended 64-bit varint. A
    /// batch of negative integers is therefore locally valid, constructible, and still far past the
    /// byte bound the session enforces — the exact shape that must produce a bounded refusal rather
    /// than an exception on the publish path or a faulted pump.
    /// </remarks>
    internal BridgeLocalBatch WireOversizedLocalBatch(long sequence)
    {
        // One array is capped at MaxCollectionElementCount, so the batch spreads its elements over
        // several updates: 32 x 65536 negative int32 values estimate at 8 MiB and encode to about
        // 21 MiB, which is inside the authoring estimate and outside every wire bound.
        var values = new int[LiveAuthoringValidation.MaxCollectionElementCount];
        Array.Fill(values, -1);
        var updates = new List<LiveStageUpdate>(32);
        for (int index = 0; index < 32; index++)
        {
            updates.Add(new SetAttributeUpdate(
                $"{BridgeTestData.BridgeRoot}/Cube",
                $"custom:indices{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                LiveAttributeValue.FromInt32Array(values)));
        }

        return new BridgeLocalBatch(
            BridgeTestData.Epoch(Server.Epoch),
            sequence,
            updates,
            BridgeTestData.LocalOrigin,
            correlationId: $"local-{sequence}");
    }

    internal async Task WaitForStateAsync(BridgeConnectionState state) =>
        await WaitForAsync(() => Client.State == state).ConfigureAwait(false);

    /// <summary>Starts the run loop for a harness created with <c>startRunLoop: false</c>.</summary>
    internal void Start() => Run = Task.Run(() => Client.RunAsync(_lifetime.Token));

    /// <summary>Publishes one batch and waits for its bounded eventual result.</summary>
    internal async Task<BridgeLocalPublicationResult> PublishAndWaitAsync(BridgeLocalBatch batch)
    {
        BridgeLocalPublicationReceipt receipt = await Client
            .PublishLocalBatchAsync(batch)
            .ConfigureAwait(false);
        return await receipt.Published.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
    }

    internal async Task WaitForAsync(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(5).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"The condition was not met within the bounded wait. State={Client.State}, " +
            $"Status={Client.GetStatus()}.");
    }

    internal async Task StopAsync()
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        try
        {
            await Run.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the expected way this loop ends.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await Client.DisposeAsync().ConfigureAwait(false);
        await Coordinator.DisposeAsync().ConfigureAwait(false);
        await _sink.DisposeAsync().ConfigureAwait(false);
        _lifetime.Dispose();
    }

    private void Record(BridgeClientEvent observed)
    {
        lock (_gate)
        {
            _events.Add(observed);
        }
    }

    private sealed class DelegateProgress : IProgress<BridgeClientEvent>
    {
        private readonly Action<BridgeClientEvent> _report;

        internal DelegateProgress(Action<BridgeClientEvent> report) => _report = report;

        public void Report(BridgeClientEvent value) => _report(value);
    }
}

/// <summary>Applies bridge overlay replacements in memory, like the coordinator's own harness.</summary>
internal sealed class RecordingOverlayExecutor : ILiveAuthoringBatchExecutor
{
    private readonly List<LiveStageUpdate> _overlay = [];
    private ulong _serial;

    internal int AppliedBatchCount { get; private set; }

    internal IReadOnlyList<LiveStageUpdate> Overlay => _overlay;

    public ValueTask<LiveAuthoringBatchResult> ExecuteAsync(
        LiveAuthoringBatch batch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppliedBatchCount++;
        foreach (LiveStageUpdate update in batch.Updates)
        {
            if (update is ReplaceBridgeOverlayUpdate replacement)
            {
                _overlay.Clear();
                _overlay.AddRange(replacement.Updates);
            }
            else
            {
                _overlay.Add(update);
            }
        }

        ulong before = _serial++;
        return ValueTask.FromResult(new LiveAuthoringBatchResult(
            batch.Sequence,
            batch.Sequence,
            1,
            batch.Updates.Count,
            batch.Invalidation,
            before,
            _serial,
            "memory://session",
            batch.CorrelationId,
            batch.OriginId));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
