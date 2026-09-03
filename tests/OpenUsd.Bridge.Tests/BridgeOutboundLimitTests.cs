// Copyright (c) marcschier. Licensed under the MIT License.

using System.Globalization;
using OpenUsd.Bridge.Grpc;
using OpenUsd.Bridge.Protocol;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Tests;

/// <summary>
/// A negotiated limit set has eight bounds, and every one of them is enforced outbound. Each test
/// here builds a batch this implementation accepts — it constructs, it validates, it would publish
/// against the local bounds — and shows it refused against a peer that agreed a smaller bound.
/// </summary>
/// <remarks>
/// The refusal must land on the receipt rather than on the peer: once a local edit is authoritative
/// here, discovering at the far end that the session was never allowed to carry it leaves the host
/// with an applied edit and no answer about it.
/// </remarks>
public sealed class BridgeOutboundLimitTests
{
    private const string LongText =
        "a-text-value-far-longer-than-the-peer-agreed-to-accept-in-one-value";

    [Test]
    public async Task ABatchPastTheNegotiatedUpdateCountIsRefused()
    {
        await using var harness = await StartAsync(BridgeLimits.Local with
        {
            MaxUpdatesPerMessage = 2
        });

        BridgeLocalPublicationResult result = await PublishAsync(
            harness,
            [
                new SetActiveUpdate($"{BridgeTestData.BridgeRoot}/A", true),
                new SetActiveUpdate($"{BridgeTestData.BridgeRoot}/B", true),
                new SetActiveUpdate($"{BridgeTestData.BridgeRoot}/C", true)
            ]);

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail!).Contains("negotiated bounds");
        await Assert.That(BridgeLimits.Local.Allows(3, 0)).IsTrue();
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ABatchPastTheNegotiatedByteBoundIsRefused()
    {
        await using var harness = await StartAsync(
            BridgeLimits.Local with { MaxMessagePayloadBytes = 48 },
            waitForStreaming: false);

        BridgeLocalPublicationResult result = await PublishAsync(
            harness,
            [
                new SetAttributeUpdate(
                    $"{BridgeTestData.BridgeRoot}/Cube",
                    "custom:label",
                    LiveAttributeValue.FromString(LongText))
            ]);

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail!).Contains("bytes");
        await Assert.That(BridgeLimits.Local.Allows(1, 4096)).IsTrue();
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ABatchPastTheNegotiatedCollectionElementCountIsRefused()
    {
        await using var harness = await StartAsync(BridgeLimits.Local with
        {
            MaxCollectionElementCount = 4
        });

        BridgeLocalPublicationResult result = await PublishAsync(
            harness,
            [
                new SetAttributeUpdate(
                    $"{BridgeTestData.BridgeRoot}/Cube",
                    "custom:indices",
                    LiveAttributeValue.FromInt32Array([1, 2, 3, 4, 5, 6, 7, 8]))
            ]);

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail!).Contains("8 elements");
        await Assert.That(harness.Client.GetStatus().EffectiveLimits.MaxCollectionElementCount)
            .IsLessThan(LiveAuthoringValidation.MaxCollectionElementCount);
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ABatchPastTheNegotiatedIdentifierLengthIsRefused()
    {
        await using var harness = await StartAsync(BridgeLimits.Local with
        {
            MaxIdentifierLength = 8
        });

        BridgeLocalPublicationResult result = await PublishAsync(
            harness,
            [
                new SetAttributeUpdate(
                    $"{BridgeTestData.BridgeRoot}/Cube",
                    "custom:pressureReading",
                    LiveAttributeValue.FromDouble(1.5))
            ]);

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail!).Contains("identifier bound");
        await Assert.That("custom:pressureReading".Length)
            .IsLessThanOrEqualTo(LiveAuthoringValidation.MaxIdentifierLength);
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ABatchPastTheNegotiatedPathLengthIsRefused()
    {
        await using var harness = await StartAsync(BridgeLimits.Local with { MaxPathLength = 16 });
        string path = $"{BridgeTestData.BridgeRoot}/AVeryDeeplyNestedPrimPath";

        BridgeLocalPublicationResult result =
            await PublishAsync(harness, [new SetActiveUpdate(path, true)]);

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail!).Contains("path bound");
        await Assert.That(path.Length).IsLessThanOrEqualTo(LiveAuthoringValidation.MaxPathLength);
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ABatchPastTheNegotiatedTextValueLengthIsRefused()
    {
        await using var harness = await StartAsync(BridgeLimits.Local with
        {
            MaxTextValueLength = 8
        });

        BridgeLocalPublicationResult result = await PublishAsync(
            harness,
            [
                new SetAttributeUpdate(
                    $"{BridgeTestData.BridgeRoot}/Cube",
                    "custom:label",
                    LiveAttributeValue.FromString(LongText))
            ]);

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail!).Contains("text bound");
        await Assert.That(LongText.Length)
            .IsLessThanOrEqualTo(LiveAuthoringValidation.MaxTextValueLength);
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ABatchPastTheNegotiatedOpaqueIdentifierLengthIsRefused()
    {
        await using var harness = await StartAsync(BridgeLimits.Local with
        {
            MaxOpaqueIdLength = 12
        });
        const string correlationId = "correlation-identifier-the-peer-will-not-accept";

        BridgeLocalPublicationResult result = await PublishAsync(
            harness,
            [new SetActiveUpdate($"{BridgeTestData.BridgeRoot}/Cube", true)],
            correlationId: correlationId);

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail!).Contains("opaque identifier bound");
        await Assert.That(correlationId.Length)
            .IsLessThanOrEqualTo(LiveAuthoringValidation.MaxOpaqueIdLength);
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ABatchPastTheNegotiatedTotalCollectionElementCountIsRefused()
    {
        // Every single collection fits; only their sum does not. That is the bound a per-collection
        // check cannot see, and the one a peer runs out of memory on.
        await using var harness = await StartAsync(BridgeLimits.Local with
        {
            MaxCollectionElementCount = 8,
            MaxTotalCollectionElementCount = 12
        });

        BridgeLocalPublicationResult result = await PublishAsync(
            harness,
            [
                new SetAttributeUpdate(
                    $"{BridgeTestData.BridgeRoot}/Cube",
                    "custom:first",
                    LiveAttributeValue.FromInt32Array([1, 2, 3, 4, 5, 6, 7, 8])),
                new SetAttributeUpdate(
                    $"{BridgeTestData.BridgeRoot}/Cube",
                    "custom:second",
                    LiveAttributeValue.FromInt32Array([1, 2, 3, 4, 5, 6, 7, 8]))
            ]);

        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted);
        await Assert.That(result.Detail!).Contains("16 collection elements in total");
        await Assert.That(harness.Client.GetStatus().EffectiveLimits.MaxTotalCollectionElementCount)
            .IsLessThan(LiveAuthoringValidation.MaxTotalCollectionElementCountPerBatch);
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
    }

    /// <summary>
    /// The bounds a batch is judged against are the ones the session that will carry it agreed. A
    /// batch admitted and retained under generous limits is re-checked against the tighter limits
    /// the reconnect negotiated, immediately before it would have been sent — and because one
    /// attempt already left the client, the refusal is reported as indeterminate rather than as a
    /// claim that the peer never saw it.
    /// </summary>
    [Test]
    public async Task ARetainedBatchIsRefusedAgainstTheTighterLimitsOfAReconnect()
    {
        var server = new FakeBridgeServer
        {
            PublishFailure = global::Grpc.Core.StatusCode.Unavailable,
            PublishFailureCount = 1
        };

        // The peer lowers what it will accept at an exactly known point: after it has seen the
        // batch once and before the transport failure that forces the renegotiation.
        server.OnPublish = _ => server.Limits = BridgeLimits.Local with
        {
            MaxCollectionElementCount = 4
        };

        await using var harness = await BridgeClientHarness.StartAsync(server);
        BridgeLocalBatch batch = new(
            BridgeTestData.Epoch(server.Epoch),
            1,
            [
                new SetAttributeUpdate(
                    $"{BridgeTestData.BridgeRoot}/Cube",
                    "custom:indices",
                    LiveAttributeValue.FromInt32Array([1, 2, 3, 4, 5, 6, 7, 8]))
            ],
            BridgeTestData.LocalOrigin,
            correlationId: "local-1");

        BridgeLocalPublicationReceipt receipt = await harness.Client.PublishLocalBatchAsync(batch);
        BridgeLocalPublicationResult result =
            await receipt.Published.WaitAsync(TimeSpan.FromSeconds(30));

        // It was admitted under the generous limits and answered under the tighter ones. The peer
        // already received one attempt whose answer was lost, so the tighter bound explains why the
        // batch cannot be re-sent; it does not establish that the batch was never applied.
        await Assert.That(receipt.Accepted).IsTrue();
        await Assert.That(server.PublishAttemptCount).IsEqualTo(1);
        await Assert.That(result.Outcome).IsEqualTo(BridgeLocalPublicationOutcome.Indeterminate);
        await Assert.That(result.Attempts).IsEqualTo(1);
        await Assert.That(result.Detail!).Contains("8 elements");
        await Assert.That(result.Detail!).Contains("NotPermitted");
        await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
        await Assert.That(harness.Client.GetStatus().EffectiveLimits.MaxCollectionElementCount)
            .IsEqualTo(4);
    }

    /// <summary>Every bound is checked deeply, including inside collections and metadata.</summary>
    [Test]
    public async Task EveryNestedTextPathAndIdentifierIsMeasuredAgainstTheNegotiatedBound()
    {
        string longVariant = new('v', 24);
        (BridgeLimits Limits, LiveStageUpdate Update, string Expected)[] cases =
        [
            (
                BridgeLimits.Local with { MaxTextValueLength = 8 },
                new SetAttributeUpdate(
                    $"{BridgeTestData.BridgeRoot}/Cube",
                    "custom:tokens",
                    LiveAttributeValue.FromTokenArray(["ok", LongText])),
                "text bound"),
            (
                BridgeLimits.Local with { MaxPathLength = 16 },
                new SetRelationshipTargetsUpdate(
                    $"{BridgeTestData.BridgeRoot}/Cube",
                    "material:binding",
                    [$"{BridgeTestData.BridgeRoot}/Materials/Steel"]),
                "path bound"),
            (
                BridgeLimits.Local with { MaxIdentifierLength = 16 },
                new SetVariantSelectionUpdate(
                    $"{BridgeTestData.BridgeRoot}/Cube",
                    "modelingVariant",
                    [longVariant],
                    longVariant),
                "identifier bound"),
            (
                BridgeLimits.Local with { MaxTextValueLength = 8 },
                new SetMetadataUpdate(
                    $"{BridgeTestData.BridgeRoot}/Cube",
                    "vendorKey",
                    LiveMetadataValue.FromString(LongText)),
                "text bound"),
            (
                BridgeLimits.Local with { MaxTextValueLength = 8 },
                new SetReferenceUpdate($"{BridgeTestData.BridgeRoot}/Ref", $"./{LongText}.usda"),
                "text bound"),
            (
                BridgeLimits.Local with { MaxCollectionElementCount = 1 },
                new SetPointInstancerOrientationsUpdate(
                    $"{BridgeTestData.BridgeRoot}/Points",
                    [new UsdQuatf(1, 0, 0, 0), new UsdQuatf(0, 1, 0, 0)]),
                "2 elements")
        ];

        for (int index = 0; index < cases.Length; index++)
        {
            (BridgeLimits limits, LiveStageUpdate update, string expected) = cases[index];
            await using var harness = await StartAsync(limits);

            BridgeLocalPublicationResult result = await PublishAsync(harness, [update]);

            await Assert.That(result.Outcome)
                .IsEqualTo(BridgeLocalPublicationOutcome.NotPermitted)
                .Because(index.ToString(CultureInfo.InvariantCulture));
            await Assert.That(result.Detail!)
                .Contains(expected)
                .Because(index.ToString(CultureInfo.InvariantCulture));
            await Assert.That(harness.Server.Published.Count).IsEqualTo(0);
        }
    }

    private static async Task<BridgeClientHarness> StartAsync(
        BridgeLimits limits,
        bool waitForStreaming = true) =>
        await BridgeClientHarness.StartAsync(
            new FakeBridgeServer { Limits = limits },
            waitForStreaming);

    private static async Task<BridgeLocalPublicationResult> PublishAsync(
        BridgeClientHarness harness,
        IReadOnlyList<LiveStageUpdate> updates,
        string? correlationId = "local-1")
    {
        await harness.WaitForAsync(() => harness.Client.GetStatus().Negotiated);
        var batch = new BridgeLocalBatch(
            BridgeTestData.Epoch(harness.Server.Epoch),
            1,
            updates,
            BridgeTestData.LocalOrigin,
            correlationId: correlationId);
        BridgeLocalPublicationReceipt receipt = await harness.Client.PublishLocalBatchAsync(batch);
        return await receipt.Published.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
