// Copyright (c) marcschier. Licensed under the MIT License.

using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;

namespace OpenUsd.Bridge.Grpc;

/// <summary>One live transport: the invoker to call through, and whatever owns it.</summary>
internal sealed class BridgeConnection : IDisposable
{
    private readonly IDisposable? _owned;

    internal BridgeConnection(CallInvoker invoker, IDisposable? owned)
    {
        Invoker = invoker;
        _owned = owned;
    }

    internal CallInvoker Invoker { get; }

    public void Dispose() => _owned?.Dispose();
}

/// <summary>Creates one transport for a connection attempt.</summary>
/// <remarks>
/// The client takes this as a delegate so a test can drive the reconnect and resync state machine
/// against an in-memory peer, exactly the way the real transport drives it, without opening a
/// socket or provisioning a certificate.
/// </remarks>
internal delegate ValueTask<BridgeConnection> BridgeConnectionFactory(
    CancellationToken cancellationToken);

/// <summary>
/// Builds a bounded, credential-carrying gRPC channel from <see cref="BridgeClientOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// The channel is configured defensively: send and receive sizes are capped at the protocol frame
/// budget, HTTP/2 keepalive pings bound how long a dead peer looks alive, and the retry service
/// config names only the read-only methods. <c>PublishLocalBatch</c> is deliberately absent from
/// the retry config, so the transport never replays a mutating call on its own.
/// </para>
/// <para>
/// Nothing here disables certificate validation or trusts an arbitrary certificate. A non-loopback
/// endpoint must be <c>https</c> and must validate against the host's trust store; a deployment
/// with a private certificate authority installs it in that store rather than weakening the client.
/// </para>
/// </remarks>
internal static class BridgeChannelFactory
{
    internal static BridgeConnection Create(BridgeClientOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            KeepAlivePingDelay = options.KeepAliveInterval,
            KeepAlivePingTimeout = options.KeepAliveTimeout,
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
            EnableMultipleHttp2Connections = false,
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan
        };

        var channelOptions = new GrpcChannelOptions
        {
            HttpHandler = handler,
            DisposeHttpClient = true,
            MaxReceiveMessageSize = ToMessageSize(options.MaxReceiveMessageBytes),
            MaxSendMessageSize = ToMessageSize(options.MaxSendMessageBytes),
            ThrowOperationCanceledOnCancellation = true,
            ServiceConfig = CreateServiceConfig(options)
        };

        GrpcChannel channel = GrpcChannel.ForAddress(options.Endpoint, channelOptions);
        return new BridgeConnection(channel.CreateCallInvoker(), channel);
    }

    private static ServiceConfig CreateServiceConfig(BridgeClientOptions options)
    {
        var retry = new RetryPolicy
        {
            MaxAttempts = options.MaxReadOnlyCallAttempts,
            InitialBackoff = options.InitialBackoff,
            MaxBackoff = options.MaxBackoff,
            BackoffMultiplier = 2,
            RetryableStatusCodes = { StatusCode.Unavailable }
        };

        var config = new ServiceConfig();
        foreach (string method in ReadOnlyMethods)
        {
            config.MethodConfigs.Add(new MethodConfig
            {
                Names =
                {
                    new MethodName
                    {
                        Service = Protocol.BridgeProtocol.ServiceName,
                        Method = method
                    }
                },
                RetryPolicy = retry
            });
        }

        return config;
    }

    /// <remarks>
    /// <c>Negotiate</c> is not in this list even though it looks read-only: it establishes a session
    /// and an epoch on the peer, so a transparent transport retry could leave a second session
    /// behind. Renegotiation is driven by the client's own reconnect loop, which always starts from
    /// a fresh handshake and a fresh resync.
    /// </remarks>
    private static readonly string[] ReadOnlyMethods = ["GetSnapshot", "GetStatus"];

    private static int ToMessageSize(long value) =>
        value > int.MaxValue ? int.MaxValue : (int)value;
}
