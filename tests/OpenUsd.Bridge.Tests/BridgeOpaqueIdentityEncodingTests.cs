// Copyright (c) marcschier. Licensed under the MIT License.

using OpenUsd.Bridge.Grpc;
using OpenUsd.Bridge.Protocol;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Tests;

/// <summary>
/// An opaque identity that is not well-formed UTF-16 is refused before anything encodes, hashes, or
/// compares it.
/// </summary>
/// <remarks>
/// <para>
/// An unpaired surrogate has no UTF-8 encoding. The default encoder does not say so: it substitutes
/// U+FFFD and carries on. Two identifiers that differ only in one unpaired surrogate therefore
/// encode to identical bytes, which means one idempotency key, one replay fingerprint, and one
/// publisher identity on the peer for two different things — an edit silently acknowledged as a
/// duplicate of another, with no error anywhere.
/// </para>
/// <para>
/// The collision tests below are the point: each pair is two distinct .NET strings that a
/// substituting encoder would flatten into one, so they prove the rejection is load-bearing rather
/// than decorative.
/// </para>
/// </remarks>
public sealed class BridgeOpaqueIdentityEncodingTests
{
    private const string LoneHigh = "origin-\ud800";
    private const string LoneLow = "origin-\udc00";
    private const string LoneHighTail = "origin-\udbff";

    [Test]
    public async Task DistinctUnpairedSurrogatesWouldCollideOnceEncoded()
    {
        // The premise. Two different strings, one byte sequence: this is what the validation exists
        // to stop, and it is asserted rather than assumed.
        await Assert.That(LoneHigh).IsNotEqualTo(LoneLow);
        await Assert.That(LoneHigh).IsNotEqualTo(LoneHighTail);
        await Assert.That(Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(LoneHigh)))
            .IsEqualTo(Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(LoneLow)));
        await Assert.That(Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(LoneHigh)))
            .IsEqualTo(Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(LoneHighTail)));
    }

    [Test]
    public async Task WellFormedTextIsRecognizedAsWellFormed()
    {
        await Assert.That(LiveAuthoringValidation.IsWellFormedUtf16("openusd-local")).IsTrue();
        // A correctly paired surrogate is a legal code point and stays legal.
        await Assert.That(LiveAuthoringValidation.IsWellFormedUtf16("emoji-\ud83d\ude00")).IsTrue();
        await Assert.That(LiveAuthoringValidation.IsWellFormedUtf16(LoneHigh)).IsFalse();
        await Assert.That(LiveAuthoringValidation.IsWellFormedUtf16(LoneLow)).IsFalse();
        await Assert.That(LiveAuthoringValidation.IsWellFormedUtf16("\ud800\ud800")).IsFalse();
        await Assert.That(LiveAuthoringValidation.IsWellFormedUtf16(null)).IsFalse();
    }

    [Test]
    [Arguments(LoneHigh)]
    [Arguments(LoneLow)]
    [Arguments(LoneHighTail)]
    public async Task ARemoteEpochRefusesAnIllFormedOriginOrSession(string illFormed)
    {
        ArgumentException origin = Assert.Throws<ArgumentException>(
            () => _ = new LiveAuthoringRemoteEpoch(illFormed, BridgeTestData.SessionId, 1));
        ArgumentException session = Assert.Throws<ArgumentException>(
            () => _ = new LiveAuthoringRemoteEpoch(BridgeTestData.RemoteOrigin, illFormed, 1));

        await Assert.That(origin.Message).Contains("unpaired surrogate");
        await Assert.That(session.Message).Contains("unpaired surrogate");
    }

    [Test]
    public async Task ALocalBatchRefusesAnIllFormedOriginCorrelationOrKey()
    {
        LiveStageUpdate[] updates = [new SetActiveUpdate($"{BridgeTestData.BridgeRoot}/Cube", true)];

        ArgumentException origin = Assert.Throws<ArgumentException>(
            () => _ = new BridgeLocalBatch(BridgeTestData.Epoch(1), 1, updates, LoneHigh));
        ArgumentException correlation = Assert.Throws<ArgumentException>(
            () => _ = new BridgeLocalBatch(
                BridgeTestData.Epoch(1),
                1,
                updates,
                BridgeTestData.LocalOrigin,
                correlationId: LoneLow));
        ArgumentException key = Assert.Throws<ArgumentException>(
            () => _ = new BridgeLocalBatch(
                BridgeTestData.Epoch(1),
                1,
                updates,
                BridgeTestData.LocalOrigin,
                idempotencyKey: LoneHighTail));

        await Assert.That(origin.Message).Contains("unpaired surrogate");
        await Assert.That(correlation.Message).Contains("unpaired surrogate");
        await Assert.That(key.Message).Contains("unpaired surrogate");
    }

    /// <summary>
    /// Two publishers whose identifiers differ only in an unpaired surrogate would derive one
    /// idempotency key. Neither identifier is constructible, so the collision cannot be reached.
    /// </summary>
    [Test]
    public async Task TwoOriginsThatWouldDeriveOneKeyAreBothRefused()
    {
        // A long session identifier forces the hashed key form, which is where the UTF-8 encoding of
        // the origin actually decides the key.
        var epoch = new LiveAuthoringRemoteEpoch(
            BridgeTestData.RemoteOrigin,
            new string('s', LiveAuthoringValidation.MaxOpaqueIdLength),
            2);
        LiveStageUpdate[] updates = [new SetActiveUpdate($"{BridgeTestData.BridgeRoot}/Cube", true)];

        await Assert.That(
            Assert.Throws<ArgumentException>(
                () => _ = new BridgeLocalBatch(epoch, 5, updates, LoneHigh)).Message)
            .Contains("unpaired surrogate");
        await Assert.That(
            Assert.Throws<ArgumentException>(
                () => _ = new BridgeLocalBatch(epoch, 5, updates, LoneLow)).Message)
            .Contains("unpaired surrogate");

        // The well-formed pair that the two would have collapsed into still produces distinct keys,
        // so the rejection is not hiding a weakness in the derivation itself.
        string first = new BridgeLocalBatch(epoch, 5, updates, "origin-a").IdempotencyKey;
        string second = new BridgeLocalBatch(epoch, 5, updates, "origin-b").IdempotencyKey;
        await Assert.That(first).IsNotEqualTo(second);
    }

    /// <summary>
    /// Two coordinators whose origins differ only in an unpaired surrogate would suppress each
    /// other's edits once compared as bytes on the peer. Neither is constructible.
    /// </summary>
    [Test]
    [Arguments(LoneHigh)]
    [Arguments(LoneLow)]
    public async Task ACoordinatorRefusesAnIllFormedLocalOrigin(string illFormed)
    {
        await using var executor = new RecordingOverlayExecutor();
        await using var sink = new QueuedLiveAuthoringSink(executor, capacity: 4);

        ArgumentException configured = Assert.Throws<ArgumentException>(
            () => _ = new LiveAuthoringSessionCoordinator(
                sink,
                new LiveAuthoringSessionOptions
                {
                    BridgeRootPath = BridgeTestData.BridgeRoot,
                    LocalOriginId = illFormed
                }));
        ArgumentException generated = Assert.Throws<ArgumentException>(
            () => _ = new LiveAuthoringSessionCoordinator(
                sink,
                new LiveAuthoringSessionOptions
                {
                    BridgeRootPath = BridgeTestData.BridgeRoot,
                    LocalOriginIdFactory = () => illFormed
                }));

        await Assert.That(configured.Message).Contains("unpaired surrogate");
        await Assert.That(generated.Message).Contains("unpaired surrogate");
    }

    [Test]
    [Arguments(LoneHigh)]
    [Arguments(LoneLow)]
    public async Task ClientOptionsRefuseAnIllFormedOriginOrRequestedSession(string illFormed)
    {
        var withOrigin = new BridgeClientOptions
        {
            Endpoint = new Uri("http://127.0.0.1:53017"),
            Credentials = new EphemeralBearerTokenProvider(
                "token",
                DateTimeOffset.UtcNow.AddHours(1)),
            BridgeRootPath = BridgeTestData.BridgeRoot,
            LocalOriginId = illFormed
        };
        var withSession = new BridgeClientOptions
        {
            Endpoint = new Uri("http://127.0.0.1:53017"),
            Credentials = new EphemeralBearerTokenProvider(
                "token",
                DateTimeOffset.UtcNow.AddHours(1)),
            BridgeRootPath = BridgeTestData.BridgeRoot,
            RequestedSessionId = illFormed
        };

        await Assert.That(Assert.Throws<ArgumentException>(withOrigin.Validate).Message)
            .Contains("unpaired surrogate");
        await Assert.That(Assert.Throws<ArgumentException>(withSession.Validate).Message)
            .Contains("unpaired surrogate");
    }
}
