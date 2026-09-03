// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Protocol;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Tests;

/// <summary>
/// Negotiation is mandatory and conservative: an incompatible major version, a missing required
/// capability, an unusable limit set, a different bridge root, or a missing epoch all refuse the
/// session before a single mutation is attempted.
/// </summary>
public sealed class BridgeNegotiationTests
{
    [Test]
    public async Task AMatchingPeerIsAcceptedWithTheIntersectedLimits()
    {
        BridgeHandshakeRequest request = LocalRequest();
        var peerLimits = BridgeLimits.Local with { MaxUpdatesPerMessage = 128 };
        var response = new BridgeHandshakeResponse(
            accepted: true,
            BridgeProtocol.Version,
            BridgeProtocol.SupportedCapabilities,
            BridgeTestData.Epoch(3),
            BridgeTestData.BridgeRoot,
            peerLimits);

        BridgeNegotiationResult result = BridgeNegotiator.Evaluate(request, response);

        await Assert.That(result.Accepted).IsTrue().Because(result.Detail);
        await Assert.That(result.Rejection).IsEqualTo(BridgeHandshakeRejection.None);
        await Assert.That(result.EffectiveLimits.MaxUpdatesPerMessage).IsEqualTo(128);
        await Assert.That(result.Epoch!.Epoch).IsEqualTo(3L);
        await Assert.That(result.Supports(BridgeCapability.FullSnapshot)).IsTrue();
    }

    [Test]
    public async Task ADifferentMajorVersionIsRejectedBeforeAnythingElseIsChecked()
    {
        var response = new BridgeHandshakeResponse(
            accepted: true,
            new BridgeProtocolVersion(BridgeProtocol.CurrentMajorVersion + 1, 0),
            [],
            epoch: null,
            "/OtherRoot",
            default);

        BridgeNegotiationResult result = BridgeNegotiator.Evaluate(LocalRequest(), response);

        await Assert.That(result.Accepted).IsFalse();
        await Assert.That(result.Rejection).IsEqualTo(BridgeHandshakeRejection.Version);
    }

    [Test]
    public async Task ANewerMinorVersionStaysCompatible()
    {
        var response = new BridgeHandshakeResponse(
            accepted: true,
            new BridgeProtocolVersion(BridgeProtocol.CurrentMajorVersion, 7),
            BridgeProtocol.SupportedCapabilities,
            BridgeTestData.Epoch(1),
            BridgeTestData.BridgeRoot,
            BridgeLimits.Local);

        BridgeNegotiationResult result = BridgeNegotiator.Evaluate(LocalRequest(), response);

        await Assert.That(result.Accepted).IsTrue().Because(result.Detail);
        await Assert.That(result.PeerVersion.Minor).IsEqualTo(7);
    }

    [Test]
    public async Task AMissingRequiredCapabilityIsRejected()
    {
        var response = new BridgeHandshakeResponse(
            accepted: true,
            BridgeProtocol.Version,
            [BridgeCapability.FullSnapshot, BridgeCapability.HealthStatus],
            BridgeTestData.Epoch(1),
            BridgeTestData.BridgeRoot,
            BridgeLimits.Local);

        BridgeNegotiationResult result = BridgeNegotiator.Evaluate(LocalRequest(), response);

        await Assert.That(result.Accepted).IsFalse();
        await Assert.That(result.Rejection).IsEqualTo(BridgeHandshakeRejection.Capability);
        await Assert.That(result.Detail).Contains(nameof(BridgeCapability.OrderedDelta));
    }

    [Test]
    public async Task AnUnusableLimitSetIsRejected()
    {
        var response = new BridgeHandshakeResponse(
            accepted: true,
            BridgeProtocol.Version,
            BridgeProtocol.SupportedCapabilities,
            BridgeTestData.Epoch(1),
            BridgeTestData.BridgeRoot,
            BridgeLimits.Local with { MaxUpdatesPerMessage = 0 });

        BridgeNegotiationResult result = BridgeNegotiator.Evaluate(LocalRequest(), response);

        await Assert.That(result.Accepted).IsFalse();
        await Assert.That(result.Rejection).IsEqualTo(BridgeHandshakeRejection.Limits);
    }

    [Test]
    public async Task ADifferentBridgeRootIsRejected()
    {
        var response = new BridgeHandshakeResponse(
            accepted: true,
            BridgeProtocol.Version,
            BridgeProtocol.SupportedCapabilities,
            BridgeTestData.Epoch(1),
            "/OtherBridge",
            BridgeLimits.Local);

        BridgeNegotiationResult result = BridgeNegotiator.Evaluate(LocalRequest(), response);

        await Assert.That(result.Accepted).IsFalse();
        await Assert.That(result.Rejection).IsEqualTo(BridgeHandshakeRejection.BridgeRoot);
    }

    [Test]
    public async Task AnAcceptedHandshakeWithoutAnEpochIsRejectedAsMalformed()
    {
        var response = new BridgeHandshakeResponse(
            accepted: true,
            BridgeProtocol.Version,
            BridgeProtocol.SupportedCapabilities,
            epoch: null,
            BridgeTestData.BridgeRoot,
            BridgeLimits.Local);

        BridgeNegotiationResult result = BridgeNegotiator.Evaluate(LocalRequest(), response);

        await Assert.That(result.Accepted).IsFalse();
        await Assert.That(result.Rejection).IsEqualTo(BridgeHandshakeRejection.Malformed);
    }

    [Test]
    public async Task AnExplicitPeerRejectionKeepsItsReason()
    {
        var response = new BridgeHandshakeResponse(
            accepted: false,
            BridgeProtocol.Version,
            BridgeProtocol.SupportedCapabilities,
            epoch: null,
            BridgeTestData.BridgeRoot,
            BridgeLimits.Local,
            BridgeHandshakeRejection.Unauthenticated,
            "credential refused");

        BridgeNegotiationResult result = BridgeNegotiator.Evaluate(LocalRequest(), response);

        await Assert.That(result.Accepted).IsFalse();
        await Assert.That(result.Rejection).IsEqualTo(BridgeHandshakeRejection.Unauthenticated);
        await Assert.That(result.Detail).IsEqualTo("credential refused");
    }

    [Test]
    public async Task AHandshakeRequestSurvivesAWireRoundTrip()
    {
        BridgeHandshakeRequest request = LocalRequest();

        bool decoded = BridgeWireCodec.TryDecodeHandshakeRequest(
            BridgeWireCodec.EncodeHandshakeRequest(request),
            out BridgeHandshakeRequest? roundTripped,
            out BridgeWireError error);

        await Assert.That(decoded).IsTrue().Because(error.ToString());
        await Assert.That(roundTripped!.ClientOriginId).IsEqualTo(request.ClientOriginId);
        await Assert.That(roundTripped.BridgeRootPath).IsEqualTo(request.BridgeRootPath);
        await Assert.That(roundTripped.Version).IsEqualTo(request.Version);
        await Assert.That(roundTripped.Capabilities.Count).IsEqualTo(request.Capabilities.Count);
        await Assert.That(roundTripped.Limits).IsEqualTo(request.Limits);
    }

    [Test]
    public async Task LocalLimitsMirrorTheAuthoringValidationConstants()
    {
        BridgeLimits limits = BridgeLimits.Local;

        await Assert.That(limits.MaxUpdatesPerMessage)
            .IsEqualTo(LiveAuthoringValidation.MaxUpdatesPerBatch);
        await Assert.That(limits.MaxCollectionElementCount)
            .IsEqualTo(LiveAuthoringValidation.MaxCollectionElementCount);
        await Assert.That(limits.MaxMessagePayloadBytes)
            .IsEqualTo(LiveAuthoringValidation.MaxEstimatedBatchPayloadBytes);
        await Assert.That(limits.MaxIdentifierLength)
            .IsEqualTo(LiveAuthoringValidation.MaxIdentifierLength);
        await Assert.That(limits.MaxPathLength).IsEqualTo(LiveAuthoringValidation.MaxPathLength);
        await Assert.That(limits.MaxTextValueLength)
            .IsEqualTo(LiveAuthoringValidation.MaxTextValueLength);
        await Assert.That(limits.MaxOpaqueIdLength)
            .IsEqualTo(LiveAuthoringValidation.MaxOpaqueIdLength);
        await Assert.That(limits.MaxTotalCollectionElementCount)
            .IsEqualTo(LiveAuthoringValidation.MaxTotalCollectionElementCountPerBatch);
    }

    [Test]
    public async Task AnUnknownCapabilityCannotBeAdvertised()
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new BridgeHandshakeRequest(
                BridgeProtocol.Version,
                [(BridgeCapability)4242],
                BridgeTestData.LocalOrigin,
                BridgeTestData.BridgeRoot,
                BridgeLimits.Local));

        await Assert.That(exception.ParamName).IsEqualTo("capabilities");
    }

    private static BridgeHandshakeRequest LocalRequest() =>
        BridgeHandshakeRequest.CreateLocal(BridgeTestData.LocalOrigin, BridgeTestData.BridgeRoot);
}
