// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Grpc;
using OpenUsd.Bridge.Protocol;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Tests;

/// <summary>
/// The client is loopback-only, authenticated, and bounded by default, and every one of those
/// defaults fails loudly at construction rather than silently at the first connection.
/// </summary>
public sealed class BridgeClientSecurityTests
{
    [Test]
    public async Task ANonLoopbackEndpointIsRefusedByDefault()
    {
        BridgeClientOptions options = CreateOptions();
        options.Endpoint = new Uri("https://bridge.example.com:443");

        ArgumentException exception = Assert.Throws<ArgumentException>(options.Validate);

        await Assert.That(exception.Message).Contains("AllowNonLoopback");
    }

    [Test]
    public async Task ANonLoopbackEndpointRequiresTransportSecurity()
    {
        BridgeClientOptions options = CreateOptions();
        options.Endpoint = new Uri("http://bridge.example.com:8080");
        options.AllowNonLoopback = true;

        ArgumentException exception = Assert.Throws<ArgumentException>(options.Validate);

        await Assert.That(exception.Message).Contains("https");
    }

    [Test]
    public async Task ANonLoopbackHttpsEndpointIsAllowedOnceItIsOptedIn()
    {
        BridgeClientOptions options = CreateOptions();
        options.Endpoint = new Uri("https://bridge.example.com:443");
        options.AllowNonLoopback = true;

        options.Validate();

        await Assert.That(options.Endpoint.Scheme).IsEqualTo("https");
    }

    [Test]
    public async Task ALoopbackEndpointDoesNotRequireTransportSecurity()
    {
        BridgeClientOptions options = CreateOptions();

        options.Validate();

        await Assert.That(options.Endpoint.IsLoopback).IsTrue();
    }

    [Test]
    public async Task AMissingCredentialProviderIsRefused()
    {
        BridgeClientOptions options = CreateOptions();
        options.Credentials = null;

        ArgumentException exception = Assert.Throws<ArgumentException>(options.Validate);

        await Assert.That(exception.ParamName).IsEqualTo("Credentials");
    }

    [Test]
    public async Task AnUnboundedDeadlineIsRefused()
    {
        BridgeClientOptions options = CreateOptions();
        options.CallDeadline = TimeSpan.Zero;

        ArgumentException exception = Assert.Throws<ArgumentException>(options.Validate);

        await Assert.That(exception.ParamName).IsEqualTo("CallDeadline");
    }

    [Test]
    public async Task AMessageSizeAboveTheProtocolFrameBudgetIsRefused()
    {
        BridgeClientOptions options = CreateOptions();
        options.MaxReceiveMessageBytes = BridgeProtocol.MaxFrameBytes + 1;

        ArgumentException exception = Assert.Throws<ArgumentException>(options.Validate);

        await Assert.That(exception.ParamName).IsEqualTo("MaxReceiveMessageBytes");
    }

    [Test]
    public async Task ABackoffCeilingBelowTheInitialBackoffIsRefused()
    {
        BridgeClientOptions options = CreateOptions();
        options.InitialBackoff = TimeSpan.FromSeconds(5);
        options.MaxBackoff = TimeSpan.FromSeconds(1);

        ArgumentException exception = Assert.Throws<ArgumentException>(options.Validate);

        await Assert.That(exception.ParamName).IsEqualTo("MaxBackoff");
    }

    [Test]
    public async Task ACredentialIsNeverRevealedByItsOwnDescription()
    {
        var credential = new BridgeCallCredential(
            "super-secret-token",
            DateTimeOffset.UtcNow.AddMinutes(5));

        string description = credential.ToString();

        await Assert.That(description).DoesNotContain("super-secret-token");
        await Assert.That(description).Contains("redacted");
        await Assert.That(credential.ToHeaderValue()).IsEqualTo("Bearer super-secret-token");
    }

    [Test]
    public async Task AnExpiredCredentialIsNotValid()
    {
        var credential = new BridgeCallCredential(
            "token",
            DateTimeOffset.UtcNow.AddSeconds(-1));

        await Assert.That(credential.IsValidAt(DateTimeOffset.UtcNow)).IsFalse();
        await Assert.That(default(BridgeCallCredential).HasToken).IsFalse();
    }

    [Test]
    public async Task AMismatchedBridgeRootBetweenClientAndCoordinatorIsRefused()
    {
        await using var executor = new RecordingOverlayExecutor();
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 4);
        await using var coordinator = new LiveAuthoringSessionCoordinator(
            sink,
            new LiveAuthoringSessionOptions
            {
                BridgeRootPath = "/BridgeA",
                LocalOriginId = BridgeTestData.LocalOrigin
            });
        BridgeClientOptions options = CreateOptions();
        options.BridgeRootPath = "/BridgeB";

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => _ = new OmniverseBridgeClient(coordinator, options));

        await Assert.That(exception.Message).Contains("bridge root path");
    }

    [Test]
    public async Task AMismatchedLocalOriginBetweenClientAndCoordinatorIsRefused()
    {
        await using var executor = new RecordingOverlayExecutor();
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 4);
        await using var coordinator = new LiveAuthoringSessionCoordinator(
            sink,
            new LiveAuthoringSessionOptions
            {
                BridgeRootPath = BridgeTestData.BridgeRoot,
                LocalOriginId = "other-origin"
            });
        BridgeClientOptions options = CreateOptions();

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => _ = new OmniverseBridgeClient(coordinator, options));

        await Assert.That(exception.Message).Contains("origin identifier");
    }

    private static BridgeClientOptions CreateOptions() =>
        new()
        {
            Endpoint = new Uri("http://127.0.0.1:53017"),
            Credentials = new EphemeralBearerTokenProvider(
                "token",
                DateTimeOffset.UtcNow.AddHours(1)),
            BridgeRootPath = BridgeTestData.BridgeRoot,
            LocalOriginId = BridgeTestData.LocalOrigin
        };
}
