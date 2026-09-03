// Copyright (c) marcschier. Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpenUsd.LiveAuthoring;

namespace OpenUsd.Bridge.Protocol;

/// <summary>
/// One already-authoritative local batch a host publishes outward through the bridge.
/// </summary>
/// <remarks>
/// <para>
/// This type carries an edit the host already owns. Nothing in this package observes a stage,
/// subscribes to a change feed, or synthesizes an edit: capturing local authoring is the host's job,
/// and the bridge only forwards what the host hands it. That boundary is deliberate — inventing
/// stage-mutation capture here would duplicate the existing change feeds and could disagree with
/// them.
/// </para>
/// <para>
/// <see cref="IdempotencyKey"/> is what makes a publication safely retryable. A mutating call is
/// never retried blindly: it is retried only with the same key, so a peer that already observed the
/// batch can recognize the replay instead of applying it twice.
/// </para>
/// </remarks>
public sealed class BridgeLocalBatch
{
    private readonly ReadOnlyCollection<LiveStageUpdate> _updates;

    /// <summary>Initializes a bounded local publication.</summary>
    /// <param name="epoch">The session epoch the publication belongs to.</param>
    /// <param name="sequence">The positive, strictly increasing per-epoch local sequence.</param>
    /// <param name="updates">The ordered updates the host already applied locally.</param>
    /// <param name="originId">The opaque origin identifier naming the publishing process.</param>
    /// <param name="idempotencyKey">
    /// The opaque key that identifies this publication across retries. When omitted, a bounded key
    /// is derived from the origin, session, epoch, and sequence: stable for the same publication,
    /// different for any other, and never longer than
    /// <see cref="LiveAuthoringValidation.MaxOpaqueIdLength"/> even for a maximal origin and
    /// session identifier.
    /// </param>
    /// <param name="coalescingKey">An optional snapshot key forwarded unchanged.</param>
    /// <param name="correlationId">An optional opaque tracing identifier.</param>
    public BridgeLocalBatch(
        LiveAuthoringRemoteEpoch epoch,
        long sequence,
        IEnumerable<LiveStageUpdate> updates,
        string originId,
        string? idempotencyKey = null,
        string? coalescingKey = null,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        ArgumentNullException.ThrowIfNull(updates);
        BridgeValidation.ValidateOpaqueIdentity(originId, nameof(originId), "An origin identifier");
        BridgeValidation.ValidateOptionalCorrelationId(correlationId, nameof(correlationId));
        if (idempotencyKey is not null)
        {
            BridgeValidation.ValidateOpaqueIdentity(
                idempotencyKey,
                nameof(idempotencyKey),
                "An idempotency key");
        }

        LiveStageUpdate[] materialized = [.. updates];
        if (materialized.Length > LiveAuthoringValidation.MaxUpdatesPerBatch)
        {
            throw new ArgumentException(
                "A local batch cannot contain more than " +
                $"{LiveAuthoringValidation.MaxUpdatesPerBatch} updates.",
                nameof(updates));
        }
        foreach (LiveStageUpdate update in materialized)
        {
            ArgumentNullException.ThrowIfNull(update, nameof(updates));
            if (update is ReplaceBridgeOverlayUpdate)
            {
                throw new ArgumentException(
                    "A local batch cannot carry a bridge overlay replacement; publish a snapshot.",
                    nameof(updates));
            }
        }

        // The batch validates through the same authoring rules an in-process batch would, so an
        // oversized or malformed publication fails here rather than on the peer.
        var validated = new LiveAuthoringBatch(
            sequence,
            materialized,
            coalescingKey,
            correlationId,
            originId);

        Epoch = epoch;
        Sequence = sequence;
        OriginId = originId;
        CoalescingKey = coalescingKey;
        CorrelationId = correlationId;
        IdempotencyKey = idempotencyKey ?? DeriveIdempotencyKey(epoch, sequence, originId);
        _updates = Array.AsReadOnly((LiveStageUpdate[])[.. validated.Updates]);
    }

    /// <summary>Gets the session epoch the publication belongs to.</summary>
    public LiveAuthoringRemoteEpoch Epoch { get; }

    /// <summary>Gets the per-epoch local sequence.</summary>
    public long Sequence { get; }

    /// <summary>Gets the opaque origin identifier naming the publishing process.</summary>
    public string OriginId { get; }

    /// <summary>Gets the opaque key that identifies this publication across retries.</summary>
    public string IdempotencyKey { get; }

    /// <summary>Gets the optional coalescing key forwarded unchanged.</summary>
    public string? CoalescingKey { get; }

    /// <summary>Gets the optional opaque tracing identifier.</summary>
    public string? CorrelationId { get; }

    /// <summary>Gets the ordered updates in publication order.</summary>
    public IReadOnlyList<LiveStageUpdate> Updates => _updates;

    /// <summary>
    /// Derives a bounded idempotency key from the origin, epoch, and sequence a publication belongs
    /// to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The key must name the publisher as well as the publication. Two hosts publishing into the
    /// same session allocate their own per-epoch sequences, so a key built from the session, epoch,
    /// and sequence alone would collide across origins — and a colliding key is precisely what a
    /// peer's idempotency ledger treats as "already applied", which would silently drop the second
    /// host's edit. The origin identifier is therefore part of both forms.
    /// </para>
    /// <para>
    /// An origin or a session identifier may itself be as long as
    /// <see cref="LiveAuthoringValidation.MaxOpaqueIdLength"/>, so concatenating them with the epoch
    /// and sequence can produce a key no peer would accept — and one the decoder on the far side
    /// would reject as an over-long opaque identifier. The plain readable form is therefore kept
    /// only while it fits and while it stays unambiguous; past that both identifiers are replaced
    /// by a single SHA-256 digest over a length-prefixed encoding of the pair, which is bounded,
    /// deterministic, and collision resistant.
    /// </para>
    /// <para>
    /// The readable form is used only when neither identifier contains the <c>':'</c> separator.
    /// Otherwise <c>"a:b" + "c"</c> and <c>"a" + "b:c"</c> would produce the same key for two
    /// different publishers, which is the collision the origin was added to prevent. The
    /// length-prefixed digest input has no such ambiguity.
    /// </para>
    /// <para>
    /// The epoch and the sequence stay in clear text in both forms. They are what distinguishes two
    /// publications inside one session, so folding them into the digest would trade a readable,
    /// exactly-distinguishing suffix for nothing.
    /// </para>
    /// </remarks>
    private static string DeriveIdempotencyKey(
        LiveAuthoringRemoteEpoch epoch,
        long sequence,
        string originId)
    {
        string suffix = string.Create(
            CultureInfo.InvariantCulture,
            $":{epoch.Epoch}:{sequence}");
        string derived = originId + ":" + epoch.SessionId + suffix;
        if (originId.Contains(':', StringComparison.Ordinal) ||
            epoch.SessionId.Contains(':', StringComparison.Ordinal) ||
            derived.Length > LiveAuthoringValidation.MaxOpaqueIdLength)
        {
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(
                CultureInfo.InvariantCulture,
                $"{originId.Length}:{originId}:{epoch.SessionId.Length}:{epoch.SessionId}")));
            derived = "sha256-" + Convert.ToHexString(digest) + suffix;
        }

        // The derived key is validated exactly as a caller-supplied one is: a key this constructor
        // invented must never be a key the wire contract would refuse.
        BridgeValidation.ValidateOpaqueIdentity(
            derived,
            nameof(epoch),
            "A derived idempotency key");
        return derived;
    }
}
