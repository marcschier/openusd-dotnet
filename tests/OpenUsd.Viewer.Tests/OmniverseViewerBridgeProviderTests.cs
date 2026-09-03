// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Grpc;
using OpenUsd.LiveAuthoring;
using OpenUsd.Viewer.Bridge.Grpc;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Covers the optional gRPC integration provider against a peer that is not there.
/// </summary>
/// <remarks>
/// Every test here points at a loopback port nothing is listening on, which is the cheapest
/// faithful way to produce the failure modes that matter: a connect that never becomes ready,
/// a client that would otherwise keep retrying forever, and a credential provider that would
/// otherwise keep being asked for a token. What is asserted is what happens <em>after</em> the
/// failure, because that is where a leak shows up.
/// </remarks>
public sealed class OmniverseViewerBridgeProviderTests
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(600);

    [Test]
    public async Task ATimedOutConnectLeavesNoClientCredentialUseOrEventBehind()
    {
        await using var host = new FakeBridgeHost();
        await using var provider = new OmniverseViewerBridgeProvider(host.CreateOptions());
        int events = 0;
        provider.StatusChanged += (_, _) => Interlocked.Increment(ref events);

        await Assert.That(async () => await provider.ConnectAsync(new ViewerBridgeConnectRequest()))
            .Throws<TimeoutException>();

        long credentialsAfterFailure = host.CredentialRequestCount;
        int eventsAfterFailure = Volatile.Read(ref events);
        await Task.Delay(QuietPeriod);

        // Nothing may still be running: no further credential acquisition, no further reconnect
        // attempt, and no further status event for a session the caller was told had failed.
        await Assert.That(host.CredentialRequestCount).IsEqualTo(credentialsAfterFailure);
        await Assert.That(Volatile.Read(ref events)).IsEqualTo(eventsAfterFailure);
        await Assert.That(provider.GetStatus().State)
            .IsEqualTo(ViewerBridgeConnectionState.Disconnected);
        await Assert.That(provider.IsAvailable).IsTrue();
    }

    [Test]
    public async Task ACancelledConnectLeavesNoClientBehind()
    {
        await using var host = new FakeBridgeHost();
        await using var provider = new OmniverseViewerBridgeProvider(
            host.CreateOptions(readyTimeout: TimeSpan.FromSeconds(30)));
        using var caller = new CancellationTokenSource();
        int events = 0;
        provider.StatusChanged += (_, _) => Interlocked.Increment(ref events);

        Task connect = provider
            .ConnectAsync(new ViewerBridgeConnectRequest(), caller.Token)
            .AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        await caller.CancelAsync();

        await Assert.That(async () => await connect).Throws<OperationCanceledException>();

        long credentialsAfterFailure = host.CredentialRequestCount;
        int eventsAfterFailure = Volatile.Read(ref events);
        await Task.Delay(QuietPeriod);

        await Assert.That(host.CredentialRequestCount).IsEqualTo(credentialsAfterFailure);
        await Assert.That(Volatile.Read(ref events)).IsEqualTo(eventsAfterFailure);
        await Assert.That(provider.GetStatus().State)
            .IsEqualTo(ViewerBridgeConnectionState.Disconnected);
    }

    [Test]
    public async Task AFaultedSessionFactoryLeavesNoClientBehind()
    {
        await using var host = new FakeBridgeHost();
        OmniverseViewerBridgeOptions options = host.CreateOptions();
        options.SessionFactory = (_, _) =>
            throw new InvalidOperationException("the host could not mint a session token");
        await using var provider = new OmniverseViewerBridgeProvider(options);

        await Assert.That(async () => await provider.ConnectAsync(new ViewerBridgeConnectRequest()))
            .Throws<InvalidOperationException>();
        await Task.Delay(QuietPeriod);

        await Assert.That(host.CredentialRequestCount).IsEqualTo(0);
        await Assert.That(provider.GetStatus().State)
            .IsEqualTo(ViewerBridgeConnectionState.Disconnected);

        // A failed attempt must not poison the provider: a later attempt still runs.
        options.SessionFactory = host.CreateSessionFactory();
        await Assert.That(async () => await provider.ConnectAsync(new ViewerBridgeConnectRequest()))
            .Throws<TimeoutException>();
        await Assert.That(host.CredentialRequestCount).IsGreaterThan(0);
    }

    [Test]
    public async Task RepeatedConnectsNeverMutateOrChainOntoTheHostOwnedOptions()
    {
        await using var host = new FakeBridgeHost();
        await using var provider = new OmniverseViewerBridgeProvider(host.CreateOptions());
        IProgress<BridgeClientEvent>? originalObserver = host.Options.Observer;
        string? originalSession = host.Options.RequestedSessionId;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            await Assert.That(async () =>
                await provider.ConnectAsync(new ViewerBridgeConnectRequest($"session-{attempt}")))
                .Throws<TimeoutException>();
        }

        // The host handed out one options instance three times. If the provider had installed its
        // relay on that instance, the third client would be reporting through three chained
        // relays, each holding a dead readiness source and a dead session.
        await Assert.That(host.Options.Observer).IsSameReferenceAs(originalObserver);
        await Assert.That(host.Options.RequestedSessionId).IsEqualTo(originalSession);
        await Assert.That(host.HandedOutInstances).IsEqualTo(1);

        // The host's own observer keeps receiving events on every attempt, exactly once each.
        // More than once would mean relays had been chained onto the host's own options.
        await Assert.That(host.HostObserverEventCount).IsGreaterThan(0);
        await Assert.That(host.MaxObserverCallsPerEvent).IsEqualTo(1);
    }

    [Test]
    public async Task AThrowingHostObserverCannotStallOrBreakTheProvider()
    {
        await using var host = new FakeBridgeHost();
        host.HostObserverThrows = true;
        await using var provider = new OmniverseViewerBridgeProvider(host.CreateOptions());
        int events = 0;
        provider.StatusChanged += (_, _) => Interlocked.Increment(ref events);

        await Assert.That(async () => await provider.ConnectAsync(new ViewerBridgeConnectRequest()))
            .Throws<TimeoutException>();

        // The provider still saw every event and still published status, even though the host's
        // own observer threw on each one, and the defect is reported as a bounded, typed note.
        await Assert.That(Volatile.Read(ref events)).IsGreaterThan(0);
        await Assert.That(provider.HostObserverFailureCount).IsGreaterThan(0);
        await Assert.That(provider.LastDiagnostic).IsNotNull();
        await Assert.That(provider.LastDiagnostic!).Contains("host bridge observer");
        await Assert.That(provider.LastDiagnostic!).DoesNotContain("secret");
    }

    [Test]
    public async Task AThrowingStatusSubscriberIsIsolatedFromEveryOtherSubscriber()
    {
        await using var host = new FakeBridgeHost();
        await using var provider = new OmniverseViewerBridgeProvider(host.CreateOptions());
        int second = 0;
        provider.StatusChanged += (_, _) =>
            throw new InvalidOperationException("subscriber defect");
        provider.StatusChanged += (_, _) => Interlocked.Increment(ref second);

        await Assert.That(async () => await provider.ConnectAsync(new ViewerBridgeConnectRequest()))
            .Throws<TimeoutException>();

        await Assert.That(Volatile.Read(ref second)).IsGreaterThan(0);
        await Assert.That(provider.SubscriberFailureCount).IsGreaterThan(0);
        await Assert.That(provider.LastDiagnostic!).Contains("subscriber");
    }

    [Test]
    public async Task PublicStatusNeverCarriesUserInfoOrAQueryFromTheEndpoint()
    {
        await using var host = new FakeBridgeHost(
            new Uri("https://operator:sup3rsecret@127.0.0.1:53021/live?token=abc"));
        await using var provider = new OmniverseViewerBridgeProvider(host.CreateOptions());

        // Nothing is listening, so the attempt fails; which failure it is does not matter here,
        // only that whatever it reports has already been scrubbed.
        _ = await ConnectAndCaptureFailureAsync(provider);
        ViewerBridgeStatus status = provider.GetStatus();

        await Assert.That(status.Endpoint).IsEqualTo("https://127.0.0.1:53021/live");
        if (status.Detail is { } detail)
        {
            await Assert.That(detail).DoesNotContain("sup3rsecret");
            await Assert.That(detail).DoesNotContain("token=");
            await Assert.That(detail.Length).IsLessThanOrEqualTo(ViewerBridgeLimits.MaxTextLength);
        }
    }

    [Test]
    public async Task TheSelectedSessionReachesTheClientAsTheRequestedSessionId()
    {
        await using var host = new FakeBridgeHost();
        await using var provider = new OmniverseViewerBridgeProvider(host.CreateOptions());

        _ = await ConnectAndCaptureFailureAsync(provider, "stage-b");

        await Assert.That(provider.GetStatus().SessionId).IsEqualTo("stage-b");
    }

    [Test]
    public async Task AFactoryConfiguredSessionAppliesWhenTheOperatorChoseNothing()
    {
        await using var host = new FakeBridgeHost();
        host.Options.RequestedSessionId = "factory-default";
        await using var provider = new OmniverseViewerBridgeProvider(host.CreateOptions());

        _ = await ConnectAndCaptureFailureAsync(provider);

        await Assert.That(provider.GetStatus().SessionId).IsEqualTo("factory-default");

        // Choosing a session in the Viewer overrides the factory's own default, so a list the
        // operator was shown and a session the client asks for cannot disagree.
        _ = await ConnectAndCaptureFailureAsync(provider, "operator-choice");

        await Assert.That(provider.GetStatus().SessionId).IsEqualTo("operator-choice");
        await Assert.That(host.Options.RequestedSessionId).IsEqualTo("factory-default");
    }

    [Test]
    public async Task ResyncWithoutASessionIsRefusedWithoutTouchingTheHost()
    {
        await using var host = new FakeBridgeHost();
        await using var provider = new OmniverseViewerBridgeProvider(host.CreateOptions());

        await Assert.That(async () => await provider.ResyncAsync())
            .Throws<InvalidOperationException>();

        await Assert.That(host.CredentialRequestCount).IsEqualTo(0);
        await Assert.That(host.HandedOutInstances).IsEqualTo(0);
    }

    [Test]
    public async Task DisposalRefusesLaterCommands()
    {
        await using var host = new FakeBridgeHost();
        var provider = new OmniverseViewerBridgeProvider(host.CreateOptions());
        await provider.DisposeAsync();

        await Assert.That(provider.IsAvailable).IsFalse();
        await Assert.That(async () => await provider.ConnectAsync(new ViewerBridgeConnectRequest()))
            .Throws<ObjectDisposedException>();
        await provider.DisposeAsync();
    }

    [Test]
    public async Task CloningOptionsCopiesEveryValueAndSharesOnlyTheLiveHostServices()
    {
        var credentials = new EphemeralBearerTokenProvider(
            "token",
            DateTimeOffset.UtcNow.AddHours(1));
        var observer = new CountingObserver();
        var options = new BridgeClientOptions
        {
            Endpoint = new Uri("http://127.0.0.1:53019"),
            Credentials = credentials,
            BridgeRootPath = "/Bridge",
            LocalOriginId = "openusd-local",
            RequestedSessionId = "session-a",
            CallDeadline = TimeSpan.FromSeconds(7),
            InitialBackoff = TimeSpan.FromMilliseconds(11),
            MaxBackoff = TimeSpan.FromMilliseconds(120),
            KeepAliveInterval = TimeSpan.FromSeconds(9),
            KeepAliveTimeout = TimeSpan.FromSeconds(3),
            MaxReadOnlyCallAttempts = 2,
            MaxPublishAttempts = 2,
            OutboundQueueCapacity = 8,
            AllowNonLoopback = false,
            Observer = observer,
        };

        BridgeClientOptions clone = options.Clone();
        clone.RequestedSessionId = "session-b";
        clone.Observer = new CountingObserver();

        await Assert.That(clone.Endpoint).IsEqualTo(options.Endpoint);
        await Assert.That(clone.Credentials).IsSameReferenceAs(credentials);
        await Assert.That(clone.BridgeRootPath).IsEqualTo("/Bridge");
        await Assert.That(clone.LocalOriginId).IsEqualTo("openusd-local");
        await Assert.That(clone.CallDeadline).IsEqualTo(TimeSpan.FromSeconds(7));
        await Assert.That(clone.InitialBackoff).IsEqualTo(TimeSpan.FromMilliseconds(11));
        await Assert.That(clone.MaxBackoff).IsEqualTo(TimeSpan.FromMilliseconds(120));
        await Assert.That(clone.KeepAliveInterval).IsEqualTo(TimeSpan.FromSeconds(9));
        await Assert.That(clone.KeepAliveTimeout).IsEqualTo(TimeSpan.FromSeconds(3));
        await Assert.That(clone.MaxReadOnlyCallAttempts).IsEqualTo(2);
        await Assert.That(clone.MaxPublishAttempts).IsEqualTo(2);
        await Assert.That(clone.OutboundQueueCapacity).IsEqualTo(8);
        await Assert.That(clone.MaxReceiveMessageBytes).IsEqualTo(options.MaxReceiveMessageBytes);
        await Assert.That(clone.MaxSendMessageBytes).IsEqualTo(options.MaxSendMessageBytes);

        // Mutating the copy is exactly what an integration needs to do, and it must not be
        // visible on the instance the host still holds.
        await Assert.That(options.RequestedSessionId).IsEqualTo("session-a");
        await Assert.That(options.Observer).IsSameReferenceAs(observer);
        clone.Validate();
    }

    [Test]
    public async Task ARequestedSessionIdentifierIsBounded()
    {
        var options = new BridgeClientOptions
        {
            Endpoint = new Uri("http://127.0.0.1:53019"),
            Credentials = new EphemeralBearerTokenProvider(
                "token",
                DateTimeOffset.UtcNow.AddHours(1)),
            RequestedSessionId = new string('s', 4096),
        };

        await Assert.That(options.Validate).Throws<ArgumentException>();
    }

    private static async Task<Exception?> ConnectAndCaptureFailureAsync(
        OmniverseViewerBridgeProvider provider,
        string? sessionId = null)
    {
        try
        {
            await provider.ConnectAsync(new ViewerBridgeConnectRequest(sessionId));
            return null;
        }
#pragma warning disable CA1031 // The test's subject is what survives the failure, not its type.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return exception;
        }
    }

    private sealed class CountingObserver : IProgress<BridgeClientEvent>
    {
        internal long Count;

        public void Report(BridgeClientEvent value) => Interlocked.Increment(ref Count);
    }

    /// <summary>
    /// Stands in for an embedding host: it owns the coordinator, the credential provider, and
    /// exactly one <see cref="BridgeClientOptions"/> instance that it hands back on every
    /// factory call, which is the reuse pattern the provider has to survive.
    /// </summary>
    private sealed class FakeBridgeHost : IAsyncDisposable
    {
        private readonly QueuedLiveAuthoringSink _sink;
        private readonly Lock _gate = new();
        private readonly Dictionary<string, int> _reportsPerEvent = new(StringComparer.Ordinal);
        private long _credentialRequests;
        private long _hostObserverEvents;

        internal FakeBridgeHost(Uri? endpoint = null)
        {
            _sink = new QueuedLiveAuthoringSink(new NoOpExecutor(), capacity: 4);
            Coordinator = new LiveAuthoringSessionCoordinator(
                _sink,
                new LiveAuthoringSessionOptions
                {
                    BridgeRootPath = "/Bridge",
                    LocalOriginId = "openusd-local",
                });
            Options = new BridgeClientOptions
            {
                // Nothing listens here. Every attempt therefore fails fast and locally, which is
                // what makes these tests deterministic rather than timing-dependent.
                Endpoint = endpoint ?? new Uri("http://127.0.0.1:53023"),
                Credentials = new CountingCredentialProvider(this),
                BridgeRootPath = "/Bridge",
                LocalOriginId = "openusd-local",
                InitialBackoff = TimeSpan.FromMilliseconds(10),
                MaxBackoff = TimeSpan.FromMilliseconds(40),
                Observer = new HostObserver(this),
            };
        }

        internal LiveAuthoringSessionCoordinator Coordinator { get; }

        internal BridgeClientOptions Options { get; }

        internal bool HostObserverThrows { get; set; }

        internal long CredentialRequestCount => Interlocked.Read(ref _credentialRequests);

        internal long HostObserverEventCount => Interlocked.Read(ref _hostObserverEvents);

        internal int HandedOutInstances { get; private set; }

        /// <summary>
        /// Gets the highest number of times any single client event reached the host observer.
        /// A value above one means relays were chained onto the host's options.
        /// </summary>
        internal int MaxObserverCallsPerEvent
        {
            get
            {
                using (_gate.EnterScope())
                {
                    return _reportsPerEvent.Count == 0 ? 0 : _reportsPerEvent.Values.Max();
                }
            }
        }

        internal OmniverseViewerBridgeOptions CreateOptions(TimeSpan? readyTimeout = null) =>
            new()
            {
                DisplayName = "Fake Omniverse Bridge",
                SessionFactory = CreateSessionFactory(),
                ReadyTimeout = readyTimeout ?? ReadyTimeout,
            };

        internal Func<string?, CancellationToken, ValueTask<OmniverseViewerBridgeSessionConfiguration>>
            CreateSessionFactory() =>
            (_, _) =>
            {
                HandedOutInstances = 1;
                return ValueTask.FromResult(
                    new OmniverseViewerBridgeSessionConfiguration(Coordinator, Options));
            };

        internal void RecordHostEvent(BridgeClientEvent value)
        {
            Interlocked.Increment(ref _hostObserverEvents);
            using (_gate.EnterScope())
            {
                string key = $"{value.Kind}:{value.Attempt}:{value.Sequence}:{value.TimestampUtc:O}";
                _reportsPerEvent[key] = _reportsPerEvent.GetValueOrDefault(key) + 1;
            }
        }

        internal void RecordCredentialRequest() => Interlocked.Increment(ref _credentialRequests);

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync().ConfigureAwait(false);
            await _sink.DisposeAsync().ConfigureAwait(false);
        }

        private sealed class HostObserver(FakeBridgeHost host) : IProgress<BridgeClientEvent>
        {
            public void Report(BridgeClientEvent value)
            {
                host.RecordHostEvent(value);
                if (host.HostObserverThrows)
                {
                    throw new InvalidOperationException("host observer defect");
                }
            }
        }

        private sealed class CountingCredentialProvider(FakeBridgeHost host)
            : IBridgeCallCredentialProvider
        {
            public ValueTask<BridgeCallCredential> GetCredentialAsync(
                CancellationToken cancellationToken)
            {
                host.RecordCredentialRequest();
                return ValueTask.FromResult(new BridgeCallCredential(
                    "token",
                    DateTimeOffset.UtcNow.AddHours(1)));
            }
        }

        private sealed class NoOpExecutor : ILiveAuthoringBatchExecutor
        {
            private ulong _serial;

            public ValueTask<LiveAuthoringBatchResult> ExecuteAsync(
                LiveAuthoringBatch batch,
                CancellationToken cancellationToken)
            {
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
    }
}
