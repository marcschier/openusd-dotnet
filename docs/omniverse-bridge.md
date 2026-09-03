# Omniverse bridge protocol and client

The Omniverse bridge is an **optional** pair of packages that lets an external Omniverse Kit
extension drive a bounded, bridge-owned overlay on a stage this process already owns.

| Package | Framework project | What it carries |
| --- | --- | --- |
| `OpenUsd.Bridge.Protocol` | 8/9/10 | The versioned `openusd.bridge.v1` wire model and its codec |
| `OpenUsd.Bridge.Grpc` | 8/9/10 | The optional gRPC client adapter and its connection state machine |

Neither package is referenced by `OpenUsd`, `OpenUsd.LiveAuthoring`, `OpenUsd.Rendering`, or the
Viewer. A consumer that wants ordered stage authoring never acquires a protobuf or gRPC dependency
it did not ask for, and `OpenUsd.LiveAuthoring` stays free of any networking dependency.

## No NVIDIA dependency, and what the Kit side owns

This repository contains **no NVIDIA component**: no `omni.client`, no Kit headers, no Omniverse
binary, and no NVIDIA-licensed source. The packages above ship protobuf-generated code, hand-written
C#, and the `.proto` contract, and a package-only test asserts that no shipped entry mentions
`omni` or `nvidia`.

Everything Omniverse-specific lives on the other side of the wire, in a **separately owned and
separately distributed Kit extension**:

| Responsibility | Owner |
| --- | --- |
| `omni://` URI resolution | Kit extension (requires `omni.client`) |
| Nucleus authentication, SSO, and tokens | Kit extension |
| Live session create/join/leave and `.live` layer I/O | Kit extension |
| Producing snapshots and ordered deltas on the wire | Kit extension |
| Wire contract definition and versioning | This repository |
| Applying snapshots and deltas to a stage | This repository (`OpenUsd.LiveAuthoring`) |
| Duplicate, gap, conflict, epoch, and loop rules | This repository (`LiveAuthoringSessionCoordinator`) |
| Rendering, physics, and change notification | This repository |

The wire contract is the only coupling point, which is what keeps the two sides on independent
release cadences and independent licenses.

**External Kit execution evidence is pending.** No job in this repository runs a Kit or Nucleus
process, so the implemented client protocol is proven against the contract, a Python protobuf and
gRPC runtime, and an in-memory peer — not against Kit. `eng/omniverse-profile.json` tracks that gap
under `kitExecutionEvidence`, and the support manifest marks the Kit-side leg `pending-hosted-proof`
rather than implemented.

## The contract

`openusd.bridge.v1` is defined by two files that ship inside `OpenUsd.Bridge.Protocol`:

| File | Contents |
| --- | --- |
| `protos/openusd/bridge/v1/wire.proto` | Every message and enum; no service, so a non-gRPC transport can use it |
| `protos/openusd/bridge/v1/service.proto` | The `LiveBridge` service only; imports `wire.proto` |

Contract rules, each asserted by a test:

- No `google.protobuf.Any`, no JSON blob field, no reflection-driven polymorphism, and no native
  handle or pointer. Every payload is an explicit, typed field.
- Every enum reserves `0` for an `*_UNSPECIFIED` value, and a decoder rejects an unspecified or
  unknown enum value explicitly instead of defaulting it.
- Every `oneof` case maps to exactly one authoring update or value kind; an unset `oneof` is
  rejected, never ignored.
- Bounds mirror `OpenUsd.LiveAuthoring.LiveAuthoringValidation` exactly.

### Mapped update and value kinds

| Authoring type | Wire case |
| --- | --- |
| `DefinePrimUpdate` | `StageUpdate.define_prim` |
| `RemovePrimUpdate` | `StageUpdate.remove_prim` |
| `SetAttributeUpdate` | `StageUpdate.set_attribute` |
| `ClearUpdate` | `StageUpdate.clear` |
| `SetRelationshipTargetsUpdate` | `StageUpdate.set_relationship_targets` |
| `SetReferenceUpdate` | `StageUpdate.set_reference` |
| `SetPayloadUpdate` | `StageUpdate.set_payload` |
| `SetActiveUpdate` | `StageUpdate.set_active` |
| `SetInstanceableUpdate` | `StageUpdate.set_instanceable` |
| `SetVariantSelectionUpdate` | `StageUpdate.set_variant_selection` |
| `SetMetadataUpdate` | `StageUpdate.set_metadata` |
| `ApiSchemaUpdate` | `StageUpdate.api_schema` |
| `SetPointInstancerOrientationsUpdate` | `StageUpdate.set_point_instancer_orientations` |
| `ReplaceBridgeOverlayUpdate` | **No wire case** — a full replacement is a `StageSnapshot` |

Every `LiveAttributeKind` and `LiveMetadataKind` case has its own field in `AttributeValue` and
`MetadataValue`. `color3f[]` keeps a distinct field from `vec3f[]` so the authored role survives the
round trip even though the two share a storage shape.

`ReplaceBridgeOverlayUpdate` is deliberately unrepresentable inside a delta: encoding one throws
`BridgeProtocolException` with `OverlayReplacementNotAllowed`. A peer that wants to replace the
overlay sends a snapshot, which the coordinator applies atomically.

### Lifecycle messages

| Concern | Messages |
| --- | --- |
| Version and capability negotiation | `HandshakeRequest`, `HandshakeResponse`, `Capability`, `Limits` |
| Connect and session epoch | `SessionEpoch` on every snapshot, delta, request, and event |
| Bounded full snapshot | `StageSnapshot` |
| Ordered delta | `StageDelta` |
| Acknowledgement, result, rejection | `Acknowledgement` with `SessionOutcome` and `SessionRejection` |
| Health and status | `SessionEvent`, `SessionStatus` |
| Bidirectional change streaming | `ChangeStreamRequest`, `ChangeStreamMessage`, `KeepAlive`, `ResyncRequired` |
| Local-edit export | `PublishLocalBatchRequest` |

`SessionRejection` carries every `LiveAuthoringSessionRejection` case plus two protocol-only cases,
`PROTOCOL_VIOLATION` and `NOT_NEGOTIATED`. Those two never map onto an authoring rejection: a
decoder refuses them rather than describing a transport failure as an authoring verdict.

## Version and capability negotiation

Negotiation is mandatory. The client will not send a mutating message until a handshake has been
accepted, and `BridgeNegotiator.Evaluate` checks, in this order:

1. **Major version.** A different major version is refused first, because a peer speaking another
   major version may describe capabilities and limits that do not mean what this version thinks.
   A newer *minor* version is compatible; it simply advertises more capabilities.
2. **Explicit peer rejection**, preserving the peer's own reason.
3. **Required capabilities.** `FullSnapshot` and `OrderedDelta` must be advertised by both peers.
4. **Limits.** The effective limit set is the element-wise minimum of both peers' limits, and it
   must stay usable: a peer that advertises a bound larger than `LiveAuthoringValidation` allows is
   clamped, never trusted to raise it.
5. **Bridge root path**, which must be the path the coordinator owns.
6. **Epoch presence** on an accepted handshake.

A rejection is either fatal or transient. `Version`, `Capability`, `Limits`, `BridgeRoot`, and
`Unauthenticated` fault the client and stop the run loop; `Unavailable` and `Malformed` back off and
retry.

## Bounds

Every bound is read from `LiveAuthoringValidation`, so the wire model cannot drift from the
authoring layer:

| Bound | Value source |
| --- | --- |
| Updates per message | `MaxUpdatesPerBatch` (4096) |
| Elements per collection | `MaxCollectionElementCount` (65536) |
| Aggregate collection elements per message | `MaxTotalCollectionElementCountPerBatch` |
| Estimated payload bytes | `MaxEstimatedBatchPayloadBytes` (16 MiB) |
| Identifier, path, text, and opaque-id lengths | `MaxIdentifierLength` and the matching bounds |
| Encoded frame bytes | `BridgeProtocol.MaxFrameBytes` (payload budget plus 64 KiB framing) |

An oversized frame is rejected **before** it is parsed, so an untrusted buffer is never materialized
into messages. Every decode failure returns a bounded `BridgeWireError` whose detail is capped at 256
characters and never quotes payload bytes; only encoding, which is a local programming error,
throws.

## Client adapter

`OmniverseBridgeClient` maps wire messages onto `LiveAuthoringSessionCoordinator` and maps the
coordinator's verdicts back onto the wire. It owns the transport and nothing else: duplicate, gap,
conflict, epoch, loop-suppression, and overlay-budget semantics stay with the coordinator, and there
is no merge engine in this repository.

```text
Disconnected → Connecting → Negotiating → Resynchronizing → Streaming
                    ↑             │              ↑              │
                    └── Backoff ──┴──────────────┴──────────────┘
                                  └── Faulted (no retry)
```

| State | What it does |
| --- | --- |
| `Connecting` | Creates a transport |
| `Negotiating` | Agrees version, capabilities, and limits before any mutation |
| `Resynchronizing` | Requests a bounded full snapshot and applies it |
| `Streaming` | Applies ordered deltas and acknowledges every one |
| `Backoff` | Waits out a bounded exponential backoff with full jitter |
| `ConnectionRestartRequested` | A publication or acknowledgement failure ended the connection deliberately |
| `Faulted` | Stops; a retry cannot fix the cause |

A full resync happens after a connection, on a peer `ResyncRequired` frame for the negotiated epoch,
and whenever the coordinator answers with `SequenceGap`, `ResyncRequired`, `ApplyFailed`,
`DuplicateConflict`, `ReplayExpired`, or `OverlayBudget`. A resync does not drop the connection; a
transport failure, a failed publication, and any epoch change do.

Backoff doubles from `InitialBackoff` to `MaxBackoff` and applies full jitter, so two clients that
lose the same peer do not return in lockstep. Every wait is bounded, and `RunAsync` honours its
cancellation token at every step.

One client runs one loop: a second concurrent `RunAsync` throws rather than racing two transports
into the same coordinator. `DisposeAsync` installs its terminal `Cancelled` answer under the state
lock **first** — before it cancels the lifetime token, completes the outbound channel, or awaits the
loop — then drains every queued publication. The ordering is the guarantee: an admission racing
disposal either lands in the queue that drain empties, or is refused with `Cancelled`. It is never
answered with `Refused`, which reads as backpressure and invites a retry against a client that is
already gone. A queued batch that had already been sent is drained as `Indeterminate` rather than
`Cancelled`. Disposal is idempotent, and a disposed client never reconnects, never applies another
snapshot, and never presents another credential.

### Negotiated capabilities and limits

The capabilities and effective limits agreed during negotiation are stored for the session and
enforced in both directions:

- A batch is refused when the session did not agree `LocalEditExport`, when an update needs a
  capability the session did not agree (`ApiSchema`, `PointInstancerOrientations`), or when it
  exceeds **any** of the eight negotiated bounds — update count, encoded byte size, collection
  element count, identifier length, path length, text length, opaque identifier length, or total
  collection elements across the batch — even if the local bounds would allow it. One deep
  validator walks every update, attribute value, metadata value, and collection element, and the
  same validator runs at the call site and again immediately before the batch is sent, so a batch
  queued while disconnected, or retained across a reconnect onto tighter limits, is judged against
  the session that will actually carry it rather than one that no longer exists.
- Admission is atomic with respect to the session and to the client's own end: the reservation, the
  receipt, and the channel write happen under one lock that rechecks both. A batch is therefore
  either queued before a terminal drain — a fault, a stop, or disposal — and answered by it, or
  refused after it. A pump for a session that cannot export keeps consuming and refusing for the
  life of that connection rather than draining once, so an accepted receipt never stays pending on
  a queue nothing will read again.
- An inbound frame is size-checked on the wire message, before it is decoded, using the negotiated
  byte bound and the real `CalculateSize()` rather than a proxy for it; an oversized snapshot,
  delta, or acknowledgement is never materialized into authoring values. Capability checks then run
  on the decoded updates.
- `BridgeProtocol.GetRequiredCapability` is the single table both directions consult, so the sender
  and the receiver cannot disagree about which update kinds are optional.

A negotiated session belongs to one connection. When the connection ends, the capabilities,
effective limits, session identity, and epoch are cleared, so `BridgeClientStatus` never claims an
active session while the client is backing off. Queuing while disconnected stays allowed; those
batches are re-authorized after the next handshake.

### One epoch per connection

An epoch, a capability set, and a limit set are agreed together by one handshake, so they are
enforced together for the life of that connection. Every inbound snapshot, delta, and resync demand
must name **exactly** the negotiated epoch before it reaches the coordinator:

| Inbound epoch | Client behaviour |
| --- | --- |
| The negotiated epoch | Accepted and handed to the coordinator |
| A newer epoch | `ConnectionRestartRequested` and a full renegotiation; never adopted in band |
| An older epoch | Counted protocol rejection, then restart |
| Another origin or session identifier | Counted protocol rejection, then restart |

Adopting a newer epoch in band would apply it under a negotiation that never covered it, while the
client's session identity, outbound authorization, and reported status still described the old one.
A coordinator rejection that names the session identity — `EpochAdvanced`, `EpochRetired`,
`SessionIdentity`, `RemoteOrigin` — restarts the connection for the same reason, instead of taking
an in-band snapshot under a negotiation that is already out of date. Ordinary baseline losses
(`SequenceGap`, `ResyncRequired`, `ApplyFailed`, `DuplicateConflict`, `ReplayExpired`,
`OverlayBudget`) still resync in band, because the epoch has not changed.

After a renegotiation onto a new epoch, queued local batches from the previous epoch are completed
rather than replayed: their sequences mean nothing in the new epoch. A batch that never left the
client is completed as `EpochRetired`; one that had already been sent is completed as
`Indeterminate`, because a retired epoch says nothing about whether the peer applied the attempt it
already received. A peer that comes back on an epoch *older* than the one the coordinator holds is
refused as a protocol violation and retried under backoff rather than faulting the client.

`BridgeClientStatus` exposes `NegotiatedCapabilities`, `EffectiveLimits`, and a `Supports` helper,
so a host can see what the session actually agreed rather than what it asked for.

### Local-edit export

Outbound traffic is deliberately narrow. `PublishLocalBatchAsync` offers one `BridgeLocalBatch` — an
edit the host already owns and already applied — to a bounded channel and returns a
`BridgeLocalPublicationReceipt`. Nothing in these packages observes a stage, subscribes to a change
feed, or synthesizes an edit: inventing stage-mutation capture here would duplicate the existing
change feeds and could disagree with them.

The receipt separates two questions that a single boolean used to conflate:

| Question | Answer |
| --- | --- |
| Did the bounded channel take it? | `Receipt.Accepted` |
| What eventually happened to it? | `Receipt.Published`, a `BridgeLocalPublicationResult` |

| Outcome | Meaning |
| --- | --- |
| `Published` | The peer acknowledged the batch as applied |
| `Duplicate` | The peer already held it; the idempotency key identified the replay |
| `RemoteRejected` | The peer refused it on its own terms; never retried |
| `ProtocolRejected` | The peer's answer violated the contract |
| `TransportFailed` | Every bounded attempt failed; whether the peer acted is unknown |
| `Indeterminate` | The batch was sent and the answer is unrecoverable; whether the peer acted is unknown |
| `EpochRetired` | The batch belongs to an epoch the session has left, before any attempt |
| `NotPermitted` | The session cannot carry it: capability or negotiated bound, before any attempt |
| `Refused` | The bounded channel was full |
| `Cancelled` | The client stopped or was disposed first, before any attempt |

`EpochRetired`, `NotPermitted`, and `Cancelled` are **definitive**: they assert the peer never saw
the batch. That is only knowable while the batch is still queued. Once one request has left the
client the peer may have applied it and only the answer may have been lost — which is exactly why
every attempt carries the same idempotency key — so an attempted publication that can no longer be
retried or acknowledged is reported as `Indeterminate` instead, with the reason it could not be
re-asked kept in `Detail` and the attempt count in `Attempts`. Tighter limits on the next session, a
lost `LocalEditExport` capability, a retired epoch, cancellation, a terminal drain, and disposal all
collapse to `Indeterminate` once `Attempts > 0`. `BridgeLocalPublicationResult.IsIndeterminate`
covers both that outcome and `TransportFailed`: a host that must not lose an authoritative edit
republishes under the same key or reconciles, rather than believing a refusal no peer ever made.

The peer's acknowledgement is not taken at face value. It is decoded through the same validated
codec as any other inbound message and must name this batch: the same sequence, the same echoed
correlation identifier, a session state that still holds an epoch, and an outcome that is a real
acknowledgement. Anything else is counted as a protocol rejection and restarts the connection.

Retry is bounded and honest about what it knows. A transport failure does **not** prove the peer
never acted — the request may have been applied and only the answer lost — so the batch is retried
with the same idempotency key up to `MaxPublishAttempts`, across reconnects, and the retry is safe
because the peer can recognize the replay. A semantic refusal is never retried. Any publication
failure ends the connection instead of leaving a `Streaming` client with an edit it silently
dropped.

An omitted `BridgeLocalBatch.IdempotencyKey` is derived, not invented per attempt. The derived key
names the **origin**, the session, the epoch, and the sequence, because two hosts publishing into
one session allocate their own per-epoch sequences and a key without the origin would collide
across them — which a peer's idempotency ledger reads as "already applied", silently dropping the
second host's edit. The readable `origin:session:epoch:sequence` form is kept while it fits inside
`MaxOpaqueIdLength` and while neither identifier contains the `:` separator; otherwise the pair is
replaced by a SHA-256 digest over a length-prefixed encoding of it, which is bounded, unambiguous,
and deterministic. Both forms round-trip through the wire contract as legal opaque identifiers.

### Publisher identity

The origin identifier decides two things silently: which inbound deltas the coordinator suppresses
as echoes of local edits, and which publisher a derived idempotency key names. Two publishers that
share one identifier therefore suppress each other's edits *and* derive colliding keys a peer reads
as replays — an edit that never lands, with nothing reported anywhere.

So there is no shared literal default. `LiveAuthoringSessionOptions.LocalOriginId` is `null` by
default and resolves through `LocalOriginIdFactory`, or through
`LiveAuthoringOriginId.CreateProcessInstanceUnique()` when no factory is supplied: an identifier
naming the process, the instance sequence within it, and eight bytes of entropy. Two coordinators
constructed with default options in one process therefore get different origins and different
derived keys, and neither suppresses the other's edits as its own echo. `LocalOriginIdFactory` is
the determinism seam — a test injects a fixed factory instead of reaching for a literal that would
reintroduce the collision.

`BridgeClientOptions.LocalOriginId` is `null` by default too, and the client then adopts the
coordinator's **resolved** identity, so the two sides agree by construction rather than by both
happening to name the same string. A value set there must equal the coordinator's, checked at
construction. `OmniverseBridgeClient.LocalOriginId` exposes the resolved identifier a host builds
its batches with, and an outbound `BridgeLocalBatch` whose `OriginId` names anything else is refused
— before and independently of any negotiation — because the peer's echo of it could not be
recognized as a local edit and its derived key would name a publisher this session does not have.

Every opaque identity — origin, session, correlation, idempotency key, requested session — is also
required to be well-formed UTF-16 before it is encoded, hashed, or sent. The default UTF-8 encoder
maps an unpaired surrogate to U+FFFD rather than failing, so two identifiers differing only in an
unpaired surrogate would encode to identical bytes and collide as one key, one fingerprint, and one
publisher on the peer. `LiveAuthoringValidation.IsWellFormedUtf16` is the single check both layers
use.

Order and retention are explicit. A failed batch goes back to the **head** of the retry list, ahead
of anything queued after it, so an ordered local sequence is never inverted by a reconnect. One
bound — `OutboundQueueCapacity` — covers the channel and the retry list together, accounted by a
single counter that moves once at admission and once at completion, so a reconnect cannot quietly
double the retention a host configured; `BridgeClientStatus.PendingOutboundBatchCount` reports that
one number. Batches from a retired epoch are completed rather than replayed.

## Security

| Control | Default |
| --- | --- |
| Endpoint | Loopback only; a non-loopback host requires `AllowNonLoopback` |
| Transport security | `https` is **required** for a non-loopback endpoint, with no opt-out |
| Certificate validation | Host trust store; nothing disables or overrides validation |
| Credentials | Required — `IBridgeCallCredentialProvider` per call; there is no anonymous mode |
| Credential handling | Requested per attempt, never cached across a reconnect, never logged or persisted |
| Credential validation | Scheme and token bounded and header-safe; control characters, CR, LF, spaces refused |
| Deadlines | Every unary call carries `CallDeadline` (30 s default); the stream is bounded by cancellation |
| Message sizes | Send and receive capped at `BridgeProtocol.MaxFrameBytes` |
| Keepalive | HTTP/2 pings with a bounded timeout |
| Retry | Read-only `GetSnapshot`/`GetStatus` only; `Negotiate` and `PublishLocalBatch` are never retried |
| Diagnostics | Bounded, redacted; `BridgeCallCredential.ToString()` prints `<redacted>` |
| Session selection | `RequestedSessionId` is optional, bounded, and validated like every other opaque identity |

`Negotiate` is excluded from transport retry even though it looks read-only: it establishes a
session and an epoch on the peer, so a transparent retry could leave a second session behind.
`PublishLocalBatch` is excluded because the transport must not choose when to replay a mutation;
the client's own bounded, idempotency-keyed retry does that, and only for a failure whose effect on
the peer is unknown.

A credential is validated where it is created, not where it is used. A carriage return or line feed
in a header value is a header-injection primitive on some intermediaries, and an unbounded token is
a cheap way to push a request past a peer's header limits, so both are refused by
`BridgeCallCredential` before anything can place them in request metadata — and the rejection message
never echoes the offending value. The header value is re-checked immediately before the `Metadata`
is built, because a credential provider is host code.

### Diagnostics

`BridgeClientEvent` carries the real `PreviousState` for every transition, not a copy of `State`.
An observer that throws is isolated on its own counters — `ObserverFailureCount` and
`LastObserverFailureDetail` — so a defect in host reporting code can never overwrite
`LastFailureDetail`, which is where the reason the transport actually failed is recorded.

## Using it

```csharp
await using var coordinator = new LiveAuthoringSessionCoordinator(
    sink,
    new LiveAuthoringSessionOptions
    {
        BridgeRootPath = "/Bridge"

        // LocalOriginId is left unset, so the coordinator takes a process-instance-unique origin.
        // Set it only when a peer must recognize this publisher across restarts, and then own its
        // uniqueness: two publishers sharing one origin silently swallow each other's edits.
    });

var options = new BridgeClientOptions
{
    Endpoint = new Uri("http://127.0.0.1:53017"),
    Credentials = new EphemeralBearerTokenProvider(sessionToken, expiresAtUtc),
    BridgeRootPath = "/Bridge"

    // LocalOriginId is left unset too, so the client adopts the coordinator's resolved identity.
};

await using var client = new OmniverseBridgeClient(coordinator, options);
Task run = client.RunAsync(cancellationToken);

// Batches must name the identity the client publishes under; anything else is refused.
var batch = new BridgeLocalBatch(epoch, sequence, updates, client.LocalOriginId);
```

The client's bridge root must match the coordinator's, and a `LocalOriginId` set explicitly on the
client must equal the coordinator's; either mismatch throws at construction rather than negotiating
a path the coordinator would later reject or an origin whose echoes it would reapply.

### Choosing a session

`BridgeClientOptions.RequestedSessionId` asks the peer to join a particular session. It is a
request, not an assertion:

- The unary `Negotiate` handshake carries it in `requested_session_id`. It is bounded and validated
  like every other opaque identity, because it reaches the peer inside a handshake sent before
  anything is authenticated end to end.
- The session the peer answers with is the one the client adopts, and that adopted identifier - not
  the requested one - is what the `StreamChanges` handshake rejoins. A peer that ignores the request
  is followed rather than argued with: rejoining a session the peer never created would either be
  refused or, worse, attach to someone else's.
- Every reconnect repeats the configured request, so a selection survives a dropped connection.

`BridgeClientOptions.Clone()` returns an independent copy. It exists for integrations that must
observe a client they did not configure - the Viewer's bridge provider is the motivating case -
without reaching into the host's own options object. `Credentials` and `Observer` are copied by
reference because both are live host services; everything else is copied by value, so mutating the
copy never affects the original.

## Viewer integration

`OpenUsd.Viewer.Bridge.Grpc` is an optional package that exposes one bridge session to the OpenUsd
Viewer's host-injected bridge seam. It is the only assembly that references both `OpenUsd.Viewer`
and `OpenUsd.Bridge.Grpc`; neither references it. That direction is the point: the Viewer package
keeps no gRPC, protobuf, or NVIDIA-adjacent dependency, and a host that never installs this package
runs a Viewer with no bridge surface whatsoever rather than a disabled one.

```csharp
var bridge = new OmniverseViewerBridgeProvider(new OmniverseViewerBridgeOptions
{
    DisplayName = "Omniverse Bridge",
    Sessions =
    {
        new ViewerBridgeSession("stage-a", "Kit stage A"),
    },
    SessionFactory = async (sessionId, cancellationToken) =>
    {
        // Endpoint and credentials are resolved here, in host code, from the host's own secret
        // store, at the moment the operator asks for a connection.
        BridgeClientOptions options = await host.CreateBridgeOptionsAsync(
            sessionId,
            cancellationToken);
        return new OmniverseViewerBridgeSessionConfiguration(host.Coordinator, options);
    },
});

ViewerEntryPoint.Run(new ViewerHostOptions
{
    StagePath = stagePath,
    PluginPath = pluginPath,
    BridgeConnection = bridge,
});
```

What the adapter guarantees:

- **No configuration of its own.** It holds no endpoint and no credential between connections. The
  session factory runs in host code and returns the coordinator plus a fully configured
  `BridgeClientOptions`; the adapter hands that straight to `OmniverseBridgeClient` and never reads,
  logs, persists, or displays a credential. Only the redacted scheme, host, port, and path of
  `BridgeClientOptions.Endpoint` is ever shown, through `ViewerBridgeEndpoint.Redact`.
- **The host's options are never mutated.** The adapter calls `BridgeClientOptions.Clone()` and
  installs its event relay on the copy. A host that returns the same options instance on every
  connect would otherwise end up with a relay chained onto every previous relay, each one holding a
  dead session and a dead readiness source, and with its own configuration quietly rewritten.
- **Connect is transactional.** One connect builds one client, runs its connect/negotiate/resync/
  stream loop on a background task, and waits - with a bounded `ReadyTimeout` - only until a
  snapshot is applied or the session fails. If construction fails, readiness faults, the wait times
  out, or the caller cancels, the partial session is detached, cancelled, and disposed before the
  exception is rethrown. Nothing is left reconnecting, asking the host for another credential, or
  raising events at a Viewer that already reported the attempt as failed.
- **Disconnect really disconnects.** It cancels the loop and disposes the client, which is what
  stops it presenting credentials and applying updates into a coordinator the host believes it
  released.
- **Resync is a deliberate restart.** The client contract has no in-band resync message: a fresh
  connection renegotiates and takes a fresh baseline, so resync stops and restarts the same
  configuration rather than inventing a message the peer would not answer.
- **The operator's session selection has a defined effect.** The chosen session becomes
  `BridgeClientOptions.RequestedSessionId` on the copied options, so it reaches the peer on the
  handshake. A factory that wants the last word sets its own `RequestedSessionId` and is called with
  no selection; a selection always wins over it, because a session list the Viewer showed and a
  session the client asks for must not be allowed to disagree.
- **Reporting failures are isolated in both directions.** Every client event updates provider state
  and readiness *before* the host's own observer is called, so one throwing host handler can no
  longer stop the provider seeing `SnapshotApplied` and turn a healthy connection into a connect
  timeout. The failure is recorded as a bounded provider diagnostic - the exception type name only,
  never the event payload - and then rethrown so the client's own `ObserverFailureCount` and
  `LastObserverFailureDetail` stay the place a host looks for a defect in its reporting code.
  `StatusChanged` subscribers are invoked one at a time with each isolated from the others, so a
  Viewer-side defect cannot skip the remaining subscribers or surface as a transport fault.
  `HostObserverFailureCount`, `SubscriberFailureCount`, and `LastDiagnostic` report both.
- **Everything textual is redacted at this boundary.** Transport exception text, client event
  details, and peer failure details are scrubbed of userinfo, query, and fragment and truncated with
  `ViewerBridgeText` before they are stored, so the public `ViewerBridgeStatus` a host reads
  directly is already safe - the Viewer's own later scrubbing is a second line, not the first.

The Viewer never sees a `BridgeClientStatus`, a `BridgeConnectionState`, or any gRPC type. States
map onto the renderer-neutral `ViewerBridgeConnectionState`, with `Backoff` and
`ConnectionRestartRequested` both reported as `Reconnecting`, and snapshot plus delta counts summed
into one applied-update count. See [Bridge connections](viewer.md#bridge-connections) for the
Viewer-side contract, bounds, and UI rules.

## Other transports

`BridgeWireCodec` encodes and decodes every message as opaque bytes, so a WebSocket, a message bus,
or a test harness can carry the same contract without gRPC. A WebSocket fallback is **not**
implemented, because the evidence does not call for one: a Python `grpcio-tools` run generates a
working `LiveBridgeStub` and message classes from these `.proto` files, and a Python protobuf
runtime loads the generated descriptor and round-trips a delta. Both are asserted by
`tests/OpenUsd.Bridge.Tests/BridgeDescriptorTests.cs`, which skips with a recorded reason when no
Python protobuf runtime is present.

`BridgeProtocol.CreateDescriptorSet()` and `BridgeGrpcProtocol.CreateDescriptorSet()` return the
serialized `FileDescriptorSet` for the wire file and for the service plus its import, built from the
compiled contract so they cannot drift from the code that encodes messages.

## Testing

| Suite | Coverage |
| --- | --- |
| `tests/OpenUsd.Bridge.Tests` | The contract and the client (see below) |
| `tests/OpenUsd.Package.Tests/BridgePackageTests.cs` | The two packages as packages (see below) |
| `tests/OpenUsd.Package.Tests/ViewerBridgePackageTests.cs` | The Viewer integration package's isolation |
| `tests/OpenUsd.Viewer.Tests/ViewerBridgeConnectionTests.cs` | The Viewer's host-injected bridge seam (see below) |
| `tests/OpenUsd.Viewer.Tests/OmniverseViewerBridgeProviderTests.cs` | The gRPC provider with no peer (see below) |
| `tests/OpenUsd.Bridge.Tests/BridgeSessionSelectionTests.cs` | Session selection on both handshakes (see below) |

`tests/OpenUsd.Bridge.Tests` covers a round trip for every update, value, clear-target, rejection,
event, and frame kind; random-byte and truncation fuzzing; malformed, unset-`oneof`,
unspecified-enum, and unknown-enum rejection; bounds and frame-size limits; negotiation; descriptor
and Python compatibility; the client's security defaults and credential-injection refusals;
capability and negotiated count/byte enforcement in both directions, including batches queued before
negotiation; every one of the eight negotiated bounds enforced outbound against locally valid but
peer-invalid batches, including a batch retained across a reconnect onto tighter limits; the
admission/drain race driven by an explicit barrier (a publisher authorized before negotiation and
released after a no-export pump has drained, and around a fatal drain, a stop, and disposal);
derived idempotency keys that name the origin in both their readable and hashed forms;
epoch consistency (stream and unary snapshots, deltas, and resync demands for an
advanced, retired, or foreign session; queued batches retired after renegotiation; status never
reporting an epoch the connection did not negotiate); acknowledgement validation (malformed,
mismatched sequence, dropped correlation,
epoch-less state, remote refusal); bounded retry across a reconnect with preserved order and
idempotency keys; the single total retention bound; session state cleared when a connection ends;
deterministic disposal; and reconnect, resync, acknowledgement, and publish behaviour against an
in-memory peer.

`BridgePackageTests` covers package-only restore from a clean feed, `.proto` content, dependency
isolation, absence of any NVIDIA component, and a NativeAOT consumer that publishes without a trim
or AOT warning and then executes.

`ViewerBridgePackageTests` covers the other direction: that `OpenUsd.Viewer` references no bridge
project and no gRPC or protobuf package, that nothing references `OpenUsd.Viewer.Bridge.Grpc` back,
that the integration package packs `net8.0`, `net9.0`, and `net10.0` with no `omni`- or
`nvidia`-named entry and with both expected dependencies, and that it keeps the production-library
trim, NativeAOT, and public-API gates rather than opting out of them.

`ViewerBridgeConnectionTests` covers the Viewer seam itself with a fake provider: menu visibility
and enablement for absent, injected-but-unavailable, and available providers; status transitions and
command gating; the bounded, drop-oldest marshalling queue and its reported drop count; redaction of
userinfo and query from endpoints and provider error text; command serialization; and cancellation,
failure, and disposal behaviour.

`OmniverseViewerBridgeProviderTests` points the real provider at a loopback port nothing is
listening on, which is the cheapest faithful way to produce the failures that matter, and then
asserts what survives them: no credential acquisition, no reconnect, and no status event after a
timed-out, cancelled, or factory-faulted connect; a host options instance handed back three times
that is never mutated and never accumulates chained relays; a throwing host observer that neither
stalls readiness nor hides itself; a throwing status subscriber that does not skip the others; a
public status whose endpoint and detail carry no userinfo or query; and an operator selection that
overrides a factory default.

`BridgeSessionSelectionTests` covers `RequestedSessionId` on the wire: unset when nothing was
configured, carried on the unary handshake when it was, repeated on every reconnect, rejoined by the
stream handshake when the peer honours it, and replaced by the peer's own session when it does not.

Run them with:

```shell
dotnet run --project tests/OpenUsd.Bridge.Tests/OpenUsd.Bridge.Tests.csproj -c Release
dotnet run --project tests/OpenUsd.Package.Tests/OpenUsd.Package.Tests.csproj -c Release \
  -- --treenode-filter "/*/*/BridgePackageTests/*"
dotnet run --project tests/OpenUsd.Package.Tests/OpenUsd.Package.Tests.csproj -c Release \
  -- --treenode-filter "/*/*/ViewerBridgePackageTests/*"
dotnet run --project tests/OpenUsd.Viewer.Tests/OpenUsd.Viewer.Tests.csproj -c Release \
  -- --treenode-filter "/*/*/ViewerBridgeConnectionTests/*"
dotnet run --project tests/OpenUsd.Viewer.Tests/OpenUsd.Viewer.Tests.csproj -c Release \
  -- --treenode-filter "/*/*/OmniverseViewerBridgeProviderTests/*"
```

## Related documents

- [Live authoring](live-authoring.md) — the ordered admission, validation, and session recovery
  contracts this bridge drives.
- [Omniverse interoperability profile](omniverse-profile.md) — the version-pinned interchange
  profile and its pending Kit execution evidence.
- [Omniverse Kit companion specification](omniverse-kit-companion.md) — the specification for the
  separately owned and separately distributed Kit-side server this client talks to: repository and
  license boundary, exact `Negotiate`/`GetSnapshot`/`GetStatus`/`PublishLocalBatch`/`StreamChanges`
  behavior, and the acceptance matrix a compliant implementation must pass.
- [Packaging](packaging.md) — package production and clean-consumer evidence.
- [Viewer](viewer.md#bridge-connections) — the Viewer's host-injected bridge seam, its bounds, and
  the UI rules that keep credentials out of the shell.
