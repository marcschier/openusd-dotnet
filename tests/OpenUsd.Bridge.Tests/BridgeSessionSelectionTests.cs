// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Grpc;
using Wire = OpenUsd.Bridge.Protocol.Wire;

namespace OpenUsd.Bridge.Tests;

/// <summary>
/// Covers <see cref="BridgeClientOptions.RequestedSessionId"/>: that a configured selection
/// actually reaches the peer on the unary handshake, that the stream handshake rejoins the
/// session the peer answered with, and that a peer which ignores the request is still followed
/// rather than argued with.
/// </summary>
public sealed class BridgeSessionSelectionTests
{
    [Test]
    public async Task NoRequestedSessionLeavesTheHandshakeFieldUnset()
    {
        await using BridgeClientHarness harness = await BridgeClientHarness.StartAsync();

        Wire.HandshakeRequest handshake = harness.Server.NegotiateRequests[0];

        await Assert.That(handshake.HasRequestedSessionId).IsFalse();
    }

    [Test]
    public async Task AConfiguredSessionIsCarriedOnTheUnaryHandshake()
    {
        await using BridgeClientHarness harness = await BridgeClientHarness.StartAsync(
            requestedSessionId: "operator-chosen");

        Wire.HandshakeRequest handshake = harness.Server.NegotiateRequests[0];

        await Assert.That(handshake.HasRequestedSessionId).IsTrue();
        await Assert.That(handshake.RequestedSessionId).IsEqualTo("operator-chosen");
    }

    [Test]
    public async Task AnHonouredRequestIsAlsoWhatTheStreamHandshakeRejoins()
    {
        var server = new FakeBridgeServer { HonourRequestedSessionId = true };
        await using BridgeClientHarness harness = await BridgeClientHarness.StartAsync(
            server,
            requestedSessionId: "operator-chosen");
        await harness.WaitForAsync(() => harness.Server.Received.Count > 0);

        Wire.HandshakeRequest unary = harness.Server.NegotiateRequests[0];
        Wire.HandshakeRequest streamed = harness.Server.Received[0].Handshake;

        await Assert.That(unary.RequestedSessionId).IsEqualTo("operator-chosen");
        await Assert.That(streamed.HasRequestedSessionId).IsTrue();
        await Assert.That(streamed.RequestedSessionId).IsEqualTo("operator-chosen");
        await Assert.That(harness.Client.GetStatus().SessionId).IsEqualTo("operator-chosen");
    }

    [Test]
    public async Task ARefusedRequestFollowsThePeersOwnSessionOnTheStream()
    {
        // The peer keeps its own session identifier. Rejoining the one the client asked for
        // would either be refused or, worse, attach to a session the peer never agreed to.
        var server = new FakeBridgeServer { HonourRequestedSessionId = false };
        await using BridgeClientHarness harness = await BridgeClientHarness.StartAsync(
            server,
            requestedSessionId: "operator-chosen");
        await harness.WaitForAsync(() => harness.Server.Received.Count > 0);

        Wire.HandshakeRequest streamed = harness.Server.Received[0].Handshake;

        await Assert.That(streamed.RequestedSessionId).IsEqualTo(BridgeTestData.SessionId);
        await Assert.That(harness.Client.GetStatus().SessionId)
            .IsEqualTo(BridgeTestData.SessionId);
    }

    [Test]
    public async Task EveryReconnectRepeatsTheConfiguredRequest()
    {
        var server = new FakeBridgeServer { HonourRequestedSessionId = true };
        await using BridgeClientHarness harness = await BridgeClientHarness.StartAsync(
            server,
            requestedSessionId: "operator-chosen");
        server.CloseStream();
        await harness.WaitForAsync(() => harness.Server.NegotiateCount > 1);

        foreach (Wire.HandshakeRequest handshake in harness.Server.NegotiateRequests)
        {
            await Assert.That(handshake.RequestedSessionId).IsEqualTo("operator-chosen");
        }
    }

    [Test]
    public async Task ARequestedSessionIdentifierIsValidatedLikeEveryOtherOpaqueIdentity()
    {
        var options = new BridgeClientOptions
        {
            Endpoint = new Uri("http://127.0.0.1:53017"),
            Credentials = new EphemeralBearerTokenProvider(
                BridgeClientHarness.Token,
                DateTimeOffset.UtcNow.AddHours(1)),
            RequestedSessionId = new string('s', 8192),
        };

        await Assert.That(options.Validate).Throws<ArgumentException>();

        options.RequestedSessionId = "   ";
        await Assert.That(options.Validate).Throws<ArgumentException>();

        options.RequestedSessionId = null;
        options.Validate();
    }

    [Test]
    public async Task CloneIsIndependentOfTheOriginalExceptForLiveHostServices()
    {
        var credentials = new EphemeralBearerTokenProvider(
            BridgeClientHarness.Token,
            DateTimeOffset.UtcNow.AddHours(1));
        var observer = new NullProgress();
        var options = new BridgeClientOptions
        {
            Endpoint = new Uri("http://127.0.0.1:53017"),
            Credentials = credentials,
            BridgeRootPath = BridgeTestData.BridgeRoot,
            LocalOriginId = BridgeTestData.LocalOrigin,
            RequestedSessionId = "session-a",
            OutboundQueueCapacity = 7,
            MaxPublishAttempts = 2,
            Observer = observer,
        };

        BridgeClientOptions clone = options.Clone();
        clone.RequestedSessionId = "session-b";
        clone.Observer = null;
        clone.OutboundQueueCapacity = 9;

        await Assert.That(options.RequestedSessionId).IsEqualTo("session-a");
        await Assert.That(options.Observer).IsSameReferenceAs(observer);
        await Assert.That(options.OutboundQueueCapacity).IsEqualTo(7);
        await Assert.That(clone.Credentials).IsSameReferenceAs(credentials);
        await Assert.That(clone.BridgeRootPath).IsEqualTo(BridgeTestData.BridgeRoot);
        await Assert.That(clone.LocalOriginId).IsEqualTo(BridgeTestData.LocalOrigin);
        await Assert.That(clone.MaxPublishAttempts).IsEqualTo(2);
        clone.Validate();
    }

    private sealed class NullProgress : IProgress<BridgeClientEvent>
    {
        public void Report(BridgeClientEvent value)
        {
        }
    }
}
