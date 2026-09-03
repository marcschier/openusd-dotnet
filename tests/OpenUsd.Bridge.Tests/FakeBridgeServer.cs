// Copyright (c) marcschier. Licensed under the MIT License.

using System.Threading.Channels;
using Grpc.Core;
using OpenUsd.Bridge.Protocol;
using OpenUsd.Bridge.Protocol.Wire;

using Wire = OpenUsd.Bridge.Protocol.Wire;

namespace OpenUsd.Bridge.Tests;

/// <summary>
/// An in-memory peer that answers bridge calls exactly the way a gRPC server would, so the client's
/// reconnect, resync, acknowledgement, and publish behaviour is exercised end to end without a
/// socket, a certificate, or a Kit process.
/// </summary>
internal sealed class FakeBridgeServer : CallInvoker
{
    private readonly List<ChangeStreamRequest> _received = [];
    private readonly List<PublishLocalBatchRequest> _published = [];
    private readonly List<HandshakeRequest> _negotiateRequests = [];
    private readonly List<string> _authorizationHeaders = [];
    private readonly object _gate = new();
    private Channel<ChangeStreamMessage> _outbound =
        Channel.CreateUnbounded<ChangeStreamMessage>();

    internal FakeBridgeServer(long epoch = 1) => Epoch = epoch;

    internal long Epoch { get; set; }

    internal string SessionId { get; set; } = BridgeTestData.SessionId;

    internal bool Accept { get; set; } = true;

    internal BridgeHandshakeRejection Rejection { get; set; } = BridgeHandshakeRejection.None;

    internal IReadOnlyList<BridgeCapability> Capabilities { get; set; } =
        BridgeProtocol.SupportedCapabilities;

    internal StatusCode? NegotiateFailure { get; set; }

    internal StatusCode? PublishFailure { get; set; }

    internal int PublishFailureCount { get; set; }

    /// <summary>
    /// Gets or sets the limits the peer advertises. A peer with smaller limits than this
    /// implementation is the normal case for an older or more conservative Kit extension.
    /// </summary>
    internal BridgeLimits Limits { get; set; } = BridgeLimits.Local;

    /// <summary>
    /// Gets or sets a rewrite applied to every acknowledgement before it is returned, so a test can
    /// answer with a malformed, mismatched, or refusing acknowledgement the way a broken or
    /// disagreeing peer would.
    /// </summary>
    internal Func<PublishLocalBatchRequest, Acknowledgement, Acknowledgement>? AcknowledgementRewrite
    {
        get;
        set;
    }

    /// <summary>
    /// Gets or sets a callback the peer runs when a publication arrives, before it decides whether
    /// to fail it. It exists so a test can change what the peer will negotiate next at an exactly
    /// known point in the connection rather than by racing a timer.
    /// </summary>
    internal Action<PublishLocalBatchRequest>? OnPublish { get; set; }

    internal long SnapshotSequence { get; set; }

    /// <summary>
    /// Gets or sets an epoch the peer serves on snapshots instead of the negotiated one, so a test
    /// can reproduce a peer that advanced or confused its session mid-connection.
    /// </summary>
    internal long? SnapshotEpochOverride { get; set; }

    /// <summary>Gets or sets a session identifier the peer serves on snapshots instead of its own.</summary>
    internal string? SnapshotSessionIdOverride { get; set; }

    /// <summary>Gets or sets updates the peer adds to every snapshot it serves.</summary>
    internal IReadOnlyList<LiveAuthoring.LiveStageUpdate> SnapshotExtraUpdates { get; set; } = [];

    internal int SnapshotRequestCount { get; private set; }

    internal int PublishAttemptCount { get; private set; }

    internal int NegotiateCount { get; private set; }

    internal int StreamCount { get; private set; }

    internal IReadOnlyList<PublishLocalBatchRequest> Published
    {
        get
        {
            lock (_gate)
            {
                return [.. _published];
            }
        }
    }

    internal IReadOnlyList<ChangeStreamRequest> Received
    {
        get
        {
            lock (_gate)
            {
                return [.. _received];
            }
        }
    }

    /// <summary>Gets every unary handshake the peer received, in order.</summary>
    internal IReadOnlyList<HandshakeRequest> NegotiateRequests
    {
        get
        {
            lock (_gate)
            {
                return [.. _negotiateRequests];
            }
        }
    }

    /// <summary>
    /// Gets or sets whether the peer honours a requested session identifier by answering with
    /// it. A peer that refuses simply keeps <see cref="SessionId"/>, which is the case a client
    /// must also survive.
    /// </summary>
    internal bool HonourRequestedSessionId { get; set; }

    internal IReadOnlyList<string> AuthorizationHeaders
    {
        get
        {
            lock (_gate)
            {
                return [.. _authorizationHeaders];
            }
        }
    }

    internal void Send(BridgeStreamFrame frame)
    {
        Channel<ChangeStreamMessage> outbound;
        lock (_gate)
        {
            outbound = _outbound;
        }

        outbound.Writer.TryWrite(BridgeWireCodec.ToWire(frame));
    }

    /// <summary>
    /// Ends the current change stream the way a peer that died or restarted would, and arms a fresh
    /// stream for the client's next connection attempt.
    /// </summary>
    internal void CloseStream()
    {
        lock (_gate)
        {
            _outbound.Writer.TryComplete();
            _outbound = Channel.CreateUnbounded<ChangeStreamMessage>();
        }
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request) =>
        throw new NotSupportedException("The bridge client only makes asynchronous calls.");

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request)
    {
        RecordHeaders(options);
        Task<TResponse> response = Task.FromResult((TResponse)Handle(method.Name, request));
        return new AsyncUnaryCall<TResponse>(
            response,
            Task.FromResult(new Metadata()),
            static () => Status.DefaultSuccess,
            static () => [],
            static () => { });
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options,
        TRequest request) =>
        throw new NotSupportedException("The contract has no server-streaming call.");

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall
        <TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options) =>
        throw new NotSupportedException("The contract has no client-streaming call.");

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall
        <TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        string? host,
        CallOptions options)
    {
        RecordHeaders(options);
        StreamCount++;
        Channel<ChangeStreamMessage> outbound;
        lock (_gate)
        {
            outbound = _outbound;
        }

        var requestStream = new RecordingRequestStream<TRequest>(this);
        var responseStream = new ChannelResponseStream<TResponse>(outbound.Reader);
        return new AsyncDuplexStreamingCall<TRequest, TResponse>(
            requestStream,
            responseStream,
            Task.FromResult(new Metadata()),
            static () => Status.DefaultSuccess,
            static () => [],
            static () => { });
    }

    private object Handle<TRequest>(string methodName, TRequest request)
    {
        switch (methodName)
        {
            case "Negotiate":
                NegotiateCount++;
                if (request is HandshakeRequest handshake)
                {
                    lock (_gate)
                    {
                        _negotiateRequests.Add(handshake);
                    }
                    if (HonourRequestedSessionId && handshake.HasRequestedSessionId)
                    {
                        SessionId = handshake.RequestedSessionId;
                    }
                }
                if (NegotiateFailure is StatusCode negotiateFailure)
                {
                    throw new RpcException(new Status(negotiateFailure, "negotiate failed"));
                }

                return CreateHandshakeResponse();
            case "GetSnapshot":
                SnapshotRequestCount++;
                return BridgeMessageCodec.ToWire(BridgeTestData.Snapshot(
                    SnapshotSequence,
                    SnapshotEpochOverride ?? Epoch,
                    SnapshotExtraUpdates,
                    SnapshotSessionIdOverride ?? SessionId));
            case "GetStatus":
                return new Wire.SessionStatus { State = Wire.SessionState.Synchronized };
            case "PublishLocalBatch":
                return HandlePublish((PublishLocalBatchRequest)(object)request!);
            default:
                throw new RpcException(new Status(StatusCode.Unimplemented, methodName));
        }
    }

    private Acknowledgement HandlePublish(PublishLocalBatchRequest request)
    {
        PublishAttemptCount++;
        OnPublish?.Invoke(request);
        if (PublishFailureCount > 0 && PublishFailure is StatusCode failure)
        {
            PublishFailureCount--;
            throw new RpcException(new Status(failure, "publish failed"));
        }

        lock (_gate)
        {
            _published.Add(request);
        }

        var acknowledgement = new Acknowledgement
        {
            Outcome = SessionOutcome.Applied,
            Rejection = SessionRejection.None,
            Sequence = request.Sequence,
            State = Wire.SessionState.Synchronized,
            LastAcceptedSequence = request.Sequence,
            LastAppliedSequence = request.Sequence
        };
        if (request.HasCorrelationId)
        {
            acknowledgement.CorrelationId = request.CorrelationId;
        }

        return AcknowledgementRewrite is null
            ? acknowledgement
            : AcknowledgementRewrite(request, acknowledgement);
    }

    private HandshakeResponse CreateHandshakeResponse()
    {
        var response = new BridgeHandshakeResponse(
            Accept,
            BridgeProtocol.Version,
            Capabilities,
            Accept ? new LiveAuthoring.LiveAuthoringRemoteEpoch(
                BridgeTestData.RemoteOrigin,
                SessionId,
                Epoch) : null,
            BridgeTestData.BridgeRoot,
            Limits,
            Rejection,
            Accept ? null : "refused");
        return BridgeMessageCodec.ToWire(response);
    }

    private void RecordHeaders(CallOptions options)
    {
        Metadata? headers = options.Headers;
        if (headers is null)
        {
            return;
        }

        lock (_gate)
        {
            foreach (Metadata.Entry entry in headers)
            {
                if (string.Equals(entry.Key, "authorization", StringComparison.OrdinalIgnoreCase))
                {
                    _authorizationHeaders.Add(entry.Value);
                }
            }
        }
    }

    private void Record(object request)
    {
        lock (_gate)
        {
            _received.Add((ChangeStreamRequest)request);
        }
    }

    private sealed class RecordingRequestStream<TRequest> : IClientStreamWriter<TRequest>
    {
        private readonly FakeBridgeServer _server;

        internal RecordingRequestStream(FakeBridgeServer server) => _server = server;

        public WriteOptions? WriteOptions { get; set; }

        public Task CompleteAsync() => Task.CompletedTask;

        public Task WriteAsync(TRequest message)
        {
            _server.Record(message!);
            return Task.CompletedTask;
        }

        public Task WriteAsync(TRequest message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return WriteAsync(message);
        }
    }

    private sealed class ChannelResponseStream<TResponse> : IAsyncStreamReader<TResponse>
    {
        private readonly ChannelReader<ChangeStreamMessage> _reader;

        internal ChannelResponseStream(ChannelReader<ChangeStreamMessage> reader) => _reader = reader;

        public TResponse Current { get; private set; } = default!;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (!await _reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
            if (!_reader.TryRead(out ChangeStreamMessage? message))
            {
                return false;
            }

            Current = (TResponse)(object)message;
            return true;
        }
    }
}
