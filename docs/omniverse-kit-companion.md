# Omniverse Kit companion specification

This document specifies the Kit-side half of the `openusd.bridge.v1` contract: a separately
owned, separately distributed, separately licensed Omniverse Kit extension that this repository
does not implement, does not vendor, and is not authorized to create a repository for. Everything
here is **specification**, not an implementation claim. `eng/support-manifest.json`'s
`excluded-unreachable/kit-extension-companion-repo` entry and `eng/omniverse-profile.json`'s
`kit-baseline` dependency (`"pending"`) remain the authoritative claims about what exists; nothing
in this document changes either of them.

This document is hand-authored. Its structured, cross-checked facts live in
[`eng/kit-companion-spec.json`](../eng/kit-companion-spec.json), validated against
[`eng/kit-companion-spec.schema.json`](../eng/kit-companion-spec.schema.json) and regenerated into
[`docs/omniverse-kit-companion-reference.g.md`](omniverse-kit-companion-reference.g.md) by
`eng/generate-kit-companion-spec.py`. Read this document for the "why" and the exact behavior; read
the generated reference for a quick, always-current table lookup.

## Why this exists

[`docs/omniverse-bridge.md`](omniverse-bridge.md) defines the wire contract and ships a C# client.
It deliberately stops at the wire: the Kit-side peer that resolves `omni://`, authenticates against
Nucleus, owns a live session and its `.live` layer, and answers the five `LiveBridge` RPCs is out of
scope for that document because it is out of scope for this repository. This document is the
specification a separately authorized team would implement against, precise enough that a
compliant server could be written and acceptance-tested without guessing at behavior this
repository already assumes on the client side.

## Repository, distribution, and license boundary

**This repository (`marcschier/openusd2`)**

- Owns: the `openusd.bridge.v1` wire contract, `OpenUsd.Bridge.Protocol`, `OpenUsd.Bridge.Grpc`,
  and this specification.
- License: MIT (this repository's `LICENSE`).
- Repository: this one.
- Distribution: NuGet.org (`OpenUsd.Bridge.Protocol`, `OpenUsd.Bridge.Grpc`).
- NVIDIA dependency: none. No `omni.client`, no Kit headers, no Omniverse binary, no
  NVIDIA-licensed source.

**The Kit companion extension**

- Owns: `omni.client`, `omni://` resolution, Nucleus auth, live session lifecycle, `.live`
  layers, stage notices, credentials, and the server side of `LiveBridge`.
- License: chosen independently by that repository once authorized; never assumed to be MIT or
  Apache-2.0 just because this repository is.
- Repository: a separate repository, **not yet created or authorized**. No name, path, or URL is
  reserved here.
- Distribution: NGC catalog, Kit Extension Manager registry, or a local development search path --
  see [Installation](#installation).
- NVIDIA dependency: required; that is precisely why it lives outside this repository.

The wire contract is the only coupling point. Neither side vendors the other's source: a
.NET-hosted companion would depend on the published `OpenUsd.Bridge.Protocol`/`OpenUsd.Bridge.Grpc`
NuGet packages by version, and a Python/Kit-hosted companion generates stubs from the `.proto` files
those packages ship (see [Python gRPC workflow](#python-grpc-generated-stub-workflow)). That
separation is what keeps the two sides on independent release cadences and independent licenses.

### `extension.toml` dependencies and the Kit baseline placeholder

[`docs/examples/omniverse-kit-companion/extension.toml`](examples/omniverse-kit-companion/extension.toml)
is a structural sketch, not an installable manifest -- every version field reads the literal string
`"pending"`, which fails Kit's own semantic-version validation on purpose. Its
`[package.target].kit` field is the supported named Kit baseline placeholder, and it is **tied to**
`eng/omniverse-profile.json`'s `kit-baseline` dependency: that dependency's `version` field is the
only place a real value may ever be read from, never a literal baked into an extension manifest.
`eng/generate-kit-companion-spec.py` cross-checks that `kit-baseline` continues to exist in
`eng/omniverse-profile.json` with `kind: "external-optional"`, so this tie cannot silently break.

Once a named Kit release is independently authorized, pinning it means editing
`eng/omniverse-profile.json`'s `kit-baseline.version` and `versionSource` (pointing at a
repository-relative provenance/lock file, exactly like every other pinned dependency in that
profile) -- never editing the example `extension.toml` in place, and never inferring a Kit version
from the OpenUSD runtime version this repository pins.

The example's other dependencies (`omni.kit.uiapp`, `omni.usd`, `omni.client`,
`omni.kit.async_engine`) are structural placeholders for the Kit APIs the
[Kit-side ownership](#kit-side-ownership) list below assigns to the companion side. None is a real
dependency of this repository, and none is pinned to a version here.

## Kit-side ownership

The companion extension is where every Omniverse-specific behavior this repository's client
already assumes actually happens:

- **`omni.client` acquisition** -- Companion. This repository has no dependency on it, directly or
  transitively.
- **`omni://` URI resolution** -- Companion. Never resolved by this repository's client or
  coordinator.
- **Nucleus authentication, SSO, tokens** -- Companion. The protocol's own optional
  `IBridgeCallCredentialProvider`-equivalent auth is a separate, transport-level concern; it is not
  Nucleus auth and does not substitute for it.
- **Live session create/join/leave** -- Companion. Maps onto `HandshakeRequest.requested_session_id`
  and the epoch the companion mints or resumes in `HandshakeResponse.epoch`.
- **`.live` layer read/write** -- Companion. This repository's `LiveAuthoringSessionCoordinator`
  applies updates to whatever layer the *host process* configured; the companion owns the actual
  Kit-side `.live` layer the bridge root is backed by.
- **Stage notices -> wire updates** -- Companion. Translating a Kit `Tf.Notice`/stage-change
  callback into a `StageUpdate` oneof case is entirely the companion's job; see
  [Update mapping](#mapping-kit-usd-edits-to-protocol-updates).
- **Credentials (Nucleus and transport)** -- Companion. Never logged, cached beyond one attempt, or
  echoed in a `detail` field -- see [Observability](#observability-and-credential-redaction).
- **Wire contract definition and versioning** -- This repository.
  `src/OpenUsd.Bridge.Protocol/protos/openusd/bridge/v1/{wire,service}.proto`.
- **Applying snapshots/deltas to a stage** -- This repository.
  `OpenUsd.LiveAuthoring.LiveAuthoringSessionCoordinator`.
- **Duplicate, gap, conflict, epoch, loop rules** -- This repository. Enforced identically on both
  sides -- see [Exact server behavior](#exact-server-behavior).
- **Rendering, physics, change notification** -- This repository. Unaffected by which side of the
  wire an edit came from.

## Python gRPC generated-stub workflow

A Kit-hosted companion is Python-first. It never hand-writes protobuf/gRPC bindings; it generates
them from this repository's shipped `.proto` files at the companion's own build time:

1. Take the exact `.proto` files from the `OpenUsd.Bridge.Protocol` package version the companion
   targets: `wire.proto` and `service.proto`, or the equivalent `FileDescriptorSet` returned by
   `BridgeProtocol.CreateDescriptorSet()` for a descriptor-driven toolchain without file-system
   access to the sources.
2. Generate with `grpcio-tools`:
   ```shell
   python -m grpc_tools.protoc -I<protos-root> \
     --python_out=<gen> --grpc_python_out=<gen> --pyi_out=<gen> \
     openusd/bridge/v1/wire.proto openusd/bridge/v1/service.proto
   ```
   Pin `grpcio`, `grpcio-tools`, and `protobuf` to versions compatible with the companion's own Kit
   Python runtime -- never to whatever this repository's own test harness happens to use.
3. Never hand-edit the generated protobuf and gRPC Python output modules. Regenerate them
   whenever the pinned `OpenUsd.Bridge.Protocol` package version changes, and record which
   package version the generated stubs came from.
4. **Package/version negotiation is an application-layer concern, not a codegen-layer one.** The
   companion never assumes its generated stub matches the peer just because both were "regenerated
   recently." It reads `PackageName` (`openusd.bridge.v1`) from the descriptor and the negotiated
   `HandshakeResponse.server_version` at connect time, and refuses to proceed past `Negotiate` on a
   major-version mismatch -- exactly the same rule `BridgeNegotiator.Evaluate` enforces in the C#
   client.
5. Configure both the send and receive message-size options to `BridgeProtocol.MaxFrameBytes`
   (`MaxEstimatedBatchPayloadBytes` + 64 KiB framing allowance): `grpc.max_send_message_length` and
   `grpc.max_receive_message_length`. A message at exactly the negotiated effective limit must still
   fit inside the frame that carries it, so neither option is set to the negotiated limit alone.
6. Token authentication is presented the same way the C# client presents `IBridgeCallCredentialProvider`
   credentials: per call, never cached across a reconnect, never logged, and validated for
   header-safety (no CR/LF, bounded length) before it is placed on the wire.
7. Loopback/headless service configuration: default to a loopback-only listen address; require TLS
   for any non-loopback endpoint with no opt-out, matching the client-side defaults in
   [`docs/omniverse-bridge.md`](omniverse-bridge.md#security). A headless Kit Service exposes no
   additional network surface beyond the `LiveBridge` gRPC endpoint itself.

## Exact server behavior

Every RPC below assumes the wire contract in
[`src/OpenUsd.Bridge.Protocol/protos/openusd/bridge/v1/`](../src/OpenUsd.Bridge.Protocol/protos/openusd/bridge/v1)
and the enums/messages it defines. See
[`docs/omniverse-kit-companion-reference.g.md`](omniverse-kit-companion-reference.g.md) for a
condensed per-RPC table generated from the same source as this section.

### `Negotiate` (unary, never transparently retried)

1. Authenticate first, if an auth layer is configured; reject with `HANDSHAKE_REJECTION_UNAUTHENTICATED`
   without distinguishing "missing" from "invalid" credential in the detail text.
2. Check `request.client_version.major` against the server's own major version **before**
   evaluating capabilities or limits; reject with `HANDSHAKE_REJECTION_VERSION` on any mismatch. A
   peer speaking a different major version may describe capabilities/limits that mean something
   different, so this check must come first.
3. Compute the agreed capability set as the intersection of `request.client_capabilities` and the
   server's supported capabilities. Reject with `HANDSHAKE_REJECTION_CAPABILITY` when
   `CAPABILITY_FULL_SNAPSHOT` or `CAPABILITY_ORDERED_DELTA` is missing from that intersection --
   both are always required.
4. Compute `effective_limits` as the element-wise minimum of `request.client_limits` and the
   server's own enforced limits. Reject with `HANDSHAKE_REJECTION_LIMITS` when any resulting bound
   is non-positive.
5. Verify `request.bridge_root_path` equals the one absolute prim path this server instance owns.
   Reject with `HANDSHAKE_REJECTION_BRIDGE_ROOT` on any other value, including a sub-path or a
   parent path -- exact-match only.
6. When `request.requested_session_id` names a still-live session, resume its existing epoch
   unchanged; otherwise mint a fresh `SessionEpoch` (new `session_id`, and an `epoch` that
   increments only when retiring a prior epoch for the same `remote_origin_id`).
7. Return `accepted = true` with `server_version`, the agreed capabilities, `effective_limits`, the
   resolved epoch, and `bridge_root_path` echoed back exactly. **Never** return `accepted = true`
   without an epoch.
8. `Negotiate` is never retried transparently at the transport layer, even though it looks
   read-only: it establishes a session and an epoch, and a transparent retry could mint a second
   session behind the caller's back.

### `GetSnapshot` (unary, read-only, retry-safe)

1. Validate `request.epoch.session_id` and `.epoch` against exactly the session and epoch this
   connection negotiated. On any mismatch -- stale (older) or advanced (newer) -- **fail the
   call** instead of substituting a snapshot for a different epoch. A snapshot is only ever
   returned for the one epoch this connection negotiated.
2. On a mismatch, terminate the connection for renegotiation rather than continuing it: the
   client must call `Negotiate` again, which re-establishes a session, an epoch, and the
   capabilities/limits agreed for it as one combined negotiation outcome, and only then request
   another snapshot. The server never lets a client keep using capabilities or limits that were
   agreed for an epoch other than the one it is now operating under -- see
   [Epoch changes require renegotiation](#epoch-changes-within-a-connection-require-renegotiation).
3. Build a **complete, self-contained** `StageSnapshot` rooted exactly at the negotiated
   `bridge_root_path` -- every update needed to reconstruct the overlay from empty, never a diff
   against a previously issued snapshot.
4. Bound the snapshot to `MaxBridgeOverlayUpdates` (4096) updates and
   `MaxBridgeOverlayPayloadBytes` (`MaxEstimatedBatchPayloadBytes` − 64 KiB) estimated bytes. When
   authored Kit content would exceed either bound, **fail the call** rather than truncate: a
   partial baseline the coordinator applies as if complete is a correctness bug, not a size
   optimization.
5. Set `sequence` to the last remote sequence already covered by this snapshot's content, so a
   subsequent `StageDelta` at `sequence + 1` continues without a gap.
6. The call is idempotent: identical inputs against unchanged server state produce byte-identical
   output, so it is always safe to retry under a bounded transport retry policy.

### `GetStatus` (unary, read-only, retry-safe)

1. Return a `SessionStatus` reflecting the server's own bounded counters at call time: `state`,
   `epoch`, `last_accepted_sequence`, `last_applied_sequence`, applied/duplicate/rejected/
   loop-suppressed counts, overlay size, and replay-ledger occupancy.
2. **Track and report every one of `SessionStatus`'s non-optional scalar fields accurately**:
   `applied_snapshot_count`, `applied_delta_count`, `duplicate_delta_count`,
   `rejected_delta_count`, `loop_suppressed_delta_count`, `resync_required_count`,
   `overlay_prim_count`, `overlay_update_count`, `replay_window_length`, `replay_ledger_count`,
   `replay_ledger_bytes`, `oldest_retained_sequence`, and `session_observer_failure_count` are
   proto3 non-optional scalars: they have no wire-level absent state, so an unset value is
   indistinguishable from an accurately reported zero. The server never estimates one of these or
   reports a placeholder zero for one it does not actually track. A companion that cannot yet
   track one of these accurately has an implementation gap to close before claiming `GetStatus`
   support, or a case to raise as an optional field in a future protocol version -- it is never
   omitted from the response, because it cannot be.
3. Redact `last_failure_detail` and `last_session_observer_failure_detail` exactly like every
   other `detail` field: bounded length, never a credential, never raw payload bytes. Unlike the
   counters above, these two fields are `optional` in the proto and may legitimately be left
   unset when there is no failure to report.
4. Treat the call as read-only and safe to retry under a bounded transport retry policy.

### `PublishLocalBatch` (unary, mutating, never transparently retried)

Requires `CAPABILITY_LOCAL_EDIT_EXPORT` to have been agreed during negotiation.

1. **Idempotency-ledger lookup first**, before anything else is touched. On a fingerprint match
   for `request.idempotency_key`, do not touch the stage a second time and do not return the
   ledger's original `SESSION_OUTCOME_APPLIED` `Acknowledgement` as if it were a fresh apply --
   the ledger never stores that response. Instead form and return a **new** `Acknowledgement`
   with outcome `SESSION_OUTCOME_DUPLICATE`, `sequence` equal to the ledger entry's recorded
   sequence, `correlation_id` echoed from this request, and the session's current state. See
   [Idempotency key ledger](#idempotency-key-ledger) for the exact rule.
2. On a miss, validate `request.epoch`: `SESSION_REJECTION_EPOCH_RETIRED` for an old epoch,
   `SESSION_REJECTION_EPOCH_ADVANCED` for one the server itself has not yet advertised. Either
   rejection means this connection's negotiated epoch is stale: the client must renegotiate (a
   fresh `Negotiate` call) rather than retry the same batch under the old epoch.
3. Validate `request.sequence` is the next expected per-epoch local sequence for this `origin_id`;
   reject `SESSION_REJECTION_SEQUENCE_GAP` on a skip. The server never invents the missing
   sequence's content.
4. Validate every update targets a path at or under the negotiated `bridge_root_path`; reject
   `SESSION_REJECTION_BRIDGE_SCOPE` for anything outside it.
5. Validate the resulting overlay stays within `MaxBridgeOverlayUpdates`/`MaxBridgeOverlayPayloadBytes`;
   reject `SESSION_REJECTION_OVERLAY_BUDGET` otherwise.
6. Canonicalize and re-validate every update against the checks above before issuing a single
   Kit-side authoring call; nothing is sent to the `.live` layer until every update in the batch
   has passed every check.
7. Issue the Kit-side authoring calls for the batch against the bridge-owned `.live` layer, then
   record the idempotency-ledger entry and return `SESSION_OUTCOME_APPLIED` only after every one
   of those calls has completed successfully -- never before, and never merely after they were
   issued. **This specification does not claim the batch commits as one atomic, rollback-safe
   transaction, and does not claim Nucleus persistence**: no Kit API providing either guarantee
   has been evidenced (an `SdfChangeBlock` batches change notifications; it is not a rollback or
   durability mechanism). A companion that later adopts a Kit API with a genuinely evidenced
   atomic-replacement guarantee documents and evidences that separately; it is not assumed here.
8. On any partial or failed apply -- some authoring calls succeeded and a later one in the same
   batch failed -- return `SESSION_REJECTION_APPLY_FAILED`, stop emitting further `StageDelta`
   frames on this session's `StreamChanges` connection, send `ResyncRequired`, and let the next
   `GetSnapshot` rebuild the authoritative baseline from the Kit stage's actual current state
   rather than trying to reason about, or roll back, a partially-applied batch.
9. Echo `request.origin_id` on the `StageDelta` this batch produces for every other subscriber on
   `StreamChanges`, so a subscriber whose own `origin_id` matches suppresses it as an echo instead
   of reapplying it. The publisher's own connection never receives its own batch back as a delta.
10. Never retried at the transport layer: a mutating call is retried only by the client, only with
    the same `idempotency_key`, and only after a transport failure whose effect on the server is
    unknown -- see [`docs/omniverse-bridge.md`](omniverse-bridge.md#local-edit-export).

### `StreamChanges` (bidirectional stream)

1. The first `ChangeStreamRequest` frame must carry a `HandshakeRequest`; any other kind arriving
   first is `SESSION_REJECTION_NOT_NEGOTIATED` and the stream closes.
2. After a successful embedded handshake, emit exactly one `StageSnapshot` before any `StageDelta`,
   at the epoch/sequence the handshake resolved.
3. Every frame this connection sends or accepts after that carries exactly the negotiated
   `session_id` and `epoch` -- see
   [Epoch changes within a connection require renegotiation](#epoch-changes-within-a-connection-require-renegotiation).
4. Emit `StageDelta` frames in strictly increasing per-epoch sequence order for the one
   authoritative remote origin this session tracks. Never interleave two origins' sequences into
   one numbering -- see [Scoping and no general merge](#layerbridge-root-scoping-and-no-general-distributed-merge).
5. Suppress delivering a delta back to the connection that published it (echo suppression by
   `origin_id`), matching the client-side `LoopSuppressed` outcome, so neither side ever
   double-applies its own edit.
6. On detecting it cannot maintain a gapless sequence for a subscriber -- a slow consumer, an
   internal buffer overrun, a lost delta -- send `ResyncRequired` with the precise `ResyncReason`
   rather than silently dropping or reordering deltas. Resync happens **in-band**; the stream is
   not closed to force it.
7. Apply the negotiated `effective_limits` to backpressure: when the outbound direction cannot keep
   up, buffer to a bounded, documented depth and then emit `ResyncRequired` rather than growing an
   unbounded queue or blocking the Kit main thread.
8. Answer `Acknowledgement`, `SnapshotRequest`, `PublishLocalBatchRequest`, `StatusRequest`, and
   `KeepAlive` frames inline, in receipt order, without reordering them relative to outbound
   deltas.
9. On teardown (client disconnect, transport error, server shutdown), release the session's
   negotiated capabilities/limits and idempotency-ledger memory budget. A reconnect always
   re-negotiates; it never resumes a torn-down stream's in-memory state directly.
10. **Clean shutdown**: stop accepting new `Negotiate` calls, let in-flight `PublishLocalBatch`
    calls finish, send a final `SessionEvent`/`ResyncRequired` as appropriate, and close existing
    streams so every connected client reconnects and resynchronizes against the next server
    instance rather than hanging indefinitely.

### Epoch changes within a connection require renegotiation

A connection agrees one epoch together with one capability set and one limit set as a single
negotiation outcome. Those three never drift apart independently, so an epoch change is never
adopted in place on an already-negotiated connection:

- If the server's authoritative epoch for the negotiated bridge root changes while a
  `StreamChanges` connection is open -- another Kit live session became authoritative, the live
  session was reset, or any other event that retires the negotiated epoch -- the server never
  switches that stream to the new epoch in place. It ends the stream, or answers the next frame
  with `SESSION_REJECTION_EPOCH_ADVANCED`/`SESSION_REJECTION_EPOCH_RETIRED` as appropriate, so the
  client renegotiates through a fresh `Negotiate` call before any further snapshot or delta is
  exchanged.
- `GetSnapshot` never substitutes a snapshot for a different epoch than the one requested; see
  [`GetSnapshot`](#getsnapshot-unary-read-only-retry-safe) above.
- `PublishLocalBatch` rejects a request naming a retired or advanced epoch instead of applying it
  under the old one; see [`PublishLocalBatch`](#publishlocalbatch-unary-mutating-never-transparently-retried)
  above.
- The only path back into a synchronized state is: renegotiate (`Negotiate` establishes the new
  epoch, capabilities, and limits together), then take a full `StageSnapshot` under that new
  epoch, then resume `StreamChanges`. A client is never expected to reconcile an old epoch's
  capabilities or limits against a new epoch's data.

## Idempotency key ledger

`PublishLocalBatchRequest.idempotency_key` is what makes a mutating call safely retryable. The
ledger that recognizes a replay is bounded exactly like every other retained structure this
protocol defines, mirroring `OpenUsd.LiveAuthoring.LiveAuthoringValidation`:

| Bound | Value |
| --- | --- |
| Default retained window | 64 entries |
| Maximum retained window | 4,096 entries |
| Bytes retained per entry | 64 (sequence + content fingerprint + fixed overhead; never the payload) |
| Maximum ledger bytes | 262,144 (window max × per-entry bytes) |

Rules:

- A ledger entry retains only a sequence and a bounded content fingerprint -- a hash over epoch,
  `origin_id`, `correlation_id`, `coalescing_key`, and updates -- never the full authored payload
  and never a stored copy of the original `Acknowledgement`.
- A replayed key whose fingerprint matches the ledger entry (byte-identical
  epoch/`origin_id`/`correlation_id`/`coalescing_key`/updates) never re-applies and never returns
  the first, `SESSION_OUTCOME_APPLIED` `Acknowledgement` as if it were a fresh apply. The server
  forms and returns a **new** `Acknowledgement` with outcome `SESSION_OUTCOME_DUPLICATE`,
  `sequence` equal to the ledger entry's recorded sequence, `correlation_id` echoed from the
  replay request (necessarily identical to the original's, since the fingerprint matched), and
  the session's current state fields -- so a caller can never mistake a replay's answer for a
  second successful mutation, and the server never has to retain the first response to answer a
  replay.
- A reused key whose fingerprint does not match the ledger entry -- its epoch, `origin_id`,
  `correlation_id`, `coalescing_key`, or updates differ -- is a `DuplicateConflict`: reject with
  `SESSION_REJECTION_DUPLICATE_CONFLICT` and require a full resync rather than guessing which
  version is authoritative.
- A key older than the bounded replay window can no longer be proven a duplicate; reject with
  `SESSION_REJECTION_REPLAY_EXPIRED` and require a full resync instead of silently risking a
  double-apply.
- The ledger is bounded by both entry count and byte size independently, and never grows with
  session lifetime.

## Mapping Kit USD edits to protocol updates

Every `LiveStageUpdate` case the client already understands has exactly one wire case. A companion
translating Kit stage notices into wire updates uses this mapping unchanged:

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
| `ReplaceBridgeOverlayUpdate` | *(no delta case -- see notes)* |

Notes:

- `SetAttributeUpdate`: `SetAttribute.time_code` unset (`SetAttributeUpdate.TimeCode == null`)
  authors the attribute's **default value**; `time_code` set authors one discrete **time sample**
  at that code. Both are the same wire case -- there is no separate time-sample message, and both
  are fully supported by the current wire contract.
- `ApiSchemaUpdate` requires `CAPABILITY_API_SCHEMA` to have been agreed during negotiation.
- `SetPointInstancerOrientationsUpdate` requires `CAPABILITY_POINT_INSTANCER_ORIENTATIONS`.
- `ReplaceBridgeOverlayUpdate` has no delta case; it is only representable as a `StageSnapshot`
  returned by `GetSnapshot` or pushed on `StreamChanges`. Encoding one inside a delta throws
  `BridgeProtocolException(OverlayReplacementNotAllowed)`.

### Kit edits that require a full snapshot, not a delta

The wire model's `StageUpdate` oneof is closed and versioned, with exactly thirteen delta cases
(see the mapping above). A Kit-side edit that does not fit one of them is never approximated by a
different case -- it forces a full `StageSnapshot` instead:

- **Renaming or reparenting a prim** inside the bridge-owned overlay. There is no rename/move
  update case; `RemovePrimUpdate` + `DefinePrimUpdate` at a new path would lose identity
  continuity and could race with in-flight deltas that still reference the old path.
- **Sublayer/reference/payload structure changes on the bridge root prim itself** (as opposed to
  `SetReferenceUpdate`/`SetPayloadUpdate` on a prim under the root). Layer-structural edits change
  what composes the overlay, not a value inside it.
- **Variant set restructuring** (adding/removing a variant set or its variants), as opposed to
  `SetVariantSelectionUpdate` choosing among existing variants.
- **Composition arcs authored outside `bridgeRootPath`** intended to affect prims inside it. The
  coordinator only accepts updates targeting a path at or under the bridge root; this is
  bridge-scope-violating by construction and is never partially applied.
- **Removing a single authored time sample in isolation**, leaving the default value and every
  other time sample intact. `ClearUpdate`'s `CLEAR_TARGET_ATTRIBUTE_VALUE` clears an attribute's
  default value and all of its time samples together; there is no wire case for removing one
  discrete time sample without touching the rest.
- **Any edit the server cannot express as one of the thirteen `StageUpdate` cases.** Deferred to a
  full snapshot and, if it recurs, to a proposed wire addition under a new minor version.

## Layer/bridge-root scoping and no general distributed merge

- **Bridge-root rule**: every `StageUpdate` the server sends or accepts targets a path at or under
  the one `bridge_root_path` negotiated for the session. One negotiated session owns exactly one
  bridge root; a Kit stage needing more than one independently-scoped overlay negotiates more than
  one session, each with its own `SessionEpoch`.
- **Layer rule**: the bridge-owned overlay is authored into the Kit-side `.live` layer the
  companion already owns. This repository's coordinator applies it to whatever layer the *host
  process* configured as the bridge overlay's edit target. Neither side reaches into a layer the
  other did not name for this purpose, and neither side restructures the *set* of layers as part of
  a bridge update.
- **No general distributed merge.** Exactly one remote origin is authoritative for a session's
  ordered sequence. The server never combines edits from two different origins into one sequence
  position, and a client never guesses at a merge result. A conflict between two Kit users sharing
  one Nucleus live session is a Kit/Nucleus concern the companion resolves **before** an edit ever
  reaches this protocol -- by the time an edit reaches `openusd.bridge.v1`, it is already the one
  authoritative outcome for that origin.

## Execution modes

### Headless Kit Service

A non-interactive Kit process (no GUI extension loaded) hosting only the `LiveBridge` server and
the Kit-side Nucleus/live-session machinery it depends on. Intended for a long-running process
co-located with, or reachable from, whatever hosts `OpenUsd.Bridge.Grpc`'s client. Listen address,
TLS material, and token validation are companion-owned configuration; this specification only
fixes the wire contract and the loopback-by-default posture the C# client already assumes.

**Health endpoint**: `GetStatus` (unary RPC) is the primary surface -- a monitor negotiates once and
polls it on a bounded interval. A process-level liveness probe (container orchestrator HTTP/TCP
check) is a companion deployment concern outside `openusd.bridge.v1` and is not specified here
pending an authorized deployment target.

### GUI extension

An interactive Kit extension (`omni.kit.uiapp`-hosted) that additionally exposes the same server
through a visible window: connection status, negotiated capabilities/limits, and `SessionStatus`
counters. Server behavior is identical to the headless mode; only the presence of a UI differs.

**Health surfacing**: `GetStatus`, shown in the extension's own status window, plus `SessionEvent`
frames on the open `StreamChanges` connection surfaced as a live log/toast.

## Observability and credential redaction

- Every logged `Acknowledgement`, `SessionEvent`, or `SessionStatus` uses only the bounded `detail`
  text already redacted by the protocol layer; the companion never logs a raw `StageUpdate` payload
  at a default log level, since authored scene data may be sensitive.
- `correlation_id` is propagated into the companion's own log lines so one authored batch can be
  traced from Kit-side capture through `Negotiate`/`PublishLocalBatch`/`StreamChanges` to the
  client-side receipt.
- `SessionStatus`'s own counters (applied/duplicate/rejected/loop-suppressed delta counts, replay
  ledger occupancy, overlay size) are the specified metrics surface. A companion may additionally
  export them to its own metrics system but must not invent counters this protocol does not define
  as the basis for a health claim. `ResyncRequired` frequency by `ResyncReason` is the specified
  signal for detecting a subscriber that cannot keep up.
- A presented token, header, or Nucleus credential is **never** included in a log line, a
  `SessionEvent.detail`, an `Acknowledgement.detail`, or `SessionStatus.last_failure_detail`,
  mirroring `BridgeCallCredential.ToString()` printing `<redacted>` on the client side.
- A credential validation failure reports only that authentication failed
  (`HANDSHAKE_REJECTION_UNAUTHENTICATED`), never which part of the credential was wrong.

## Installation

**No installation path below exists yet.** Every entry states its current status so this
specification never implies a package is downloadable today.

### NGC (NVIDIA GPU Cloud) -- not yet published

No catalog listing exists. This section will name the exact NGC collection/resource path once one
is authorized and published; it does not invent NGC-specific commands ahead of that.

### Kit Extension Manager -- not yet published

No registry listing exists. Once published: open Kit's Extension Manager, search for the published
extension id, enable it, and confirm its declared Kit baseline and `openusd.bridge.v1` protocol
version match what this repository's client negotiates.

### Local development -- usable as a working tree today, not yet published as a package

Clone the (not-yet-authorized) companion repository, or start from the checked example
`extension.toml` (see [Checked examples](#checked-examples)) as a *structural*, not literal,
starting point:

1. Add the working tree's parent directory to Kit's extension search path
   (`kit.exe --ext-folder <path>`).
2. Generate Python stubs per [Python gRPC workflow](#python-grpc-generated-stub-workflow) before
   first run.
3. Run headless (`kit.exe --no-window <headless-service-kit-file>`) or interactively for the GUI
   mode.
4. Verify against this repository's `OpenUsd.Bridge.Grpc` client using the loopback defaults in
   [`docs/omniverse-bridge.md`](omniverse-bridge.md#security).

## Acceptance matrix

Every entry matches an item in the approved plan. Method names the exact scenario; pass criteria
names the exact observable outcome a compliant companion (or this repository's client against a
compliant companion) must produce.

### `round-trip` -- Round-trip authoring

- **Method**: a batch authored on the `OpenUsd.LiveAuthoring` side is published
  (`PublishLocalBatch`), observed as a `StageDelta` with the same `origin_id` on a second
  independently-negotiated connection, and re-applied there.
- **Pass criteria**: the second connection's applied overlay state is structurally equal to the
  first connection's local state, and the publishing connection never receives its own batch back
  as a non-echo-suppressed delta.

### `join-leave` -- Session join and leave

- **Method**: a client negotiates, receives an initial `StageSnapshot`, disconnects cleanly, and a
  second client negotiates fresh against the same server-owned bridge root.
- **Pass criteria**: the leaving client's disposal is idempotent and presents no further credential
  or mutation; the joining client's snapshot reflects the full current overlay state with no
  residue from the first client's session identity or epoch.

### `ordered-1000-edits` -- 1000 ordered edits

- **Method**: 1000 sequentially numbered updates are sent in strict per-epoch sequence order under
  the negotiated bounds.
- **Pass criteria**: every update is applied exactly once, in order, with no gap and no duplicate
  application; the final overlay state matches the deterministic result of applying all 1000
  updates in the sent order.

### `time-sample-authoring` -- Time-sampled attribute authoring

- **Method**: a `SetAttributeUpdate` is published with `SetAttribute.time_code` set, authoring a
  discrete time sample rather than the default value; a second `SetAttributeUpdate` for the same
  attribute is published with `time_code` unset, authoring the default value; both are read back
  through `GetSnapshot`.
- **Pass criteria**: the time-sampled write leaves the attribute's default value untouched and
  adds exactly one time sample at the given code; the default-value write does not add or remove
  any time sample; a snapshot faithfully separates the default value from every authored time
  sample rather than collapsing them.

### `epoch-change-renegotiation` -- Epoch change forces renegotiation, never silent adoption

- **Method**: while a client's `StreamChanges` connection is open, the server's authoritative
  epoch for the negotiated bridge root changes (for example a second Kit session takes over);
  separately, a client calls `GetSnapshot` with the epoch it negotiated after the server's
  authoritative epoch has already advanced past it.
- **Pass criteria**: the open connection never receives a snapshot, delta, or acknowledgement
  frame naming the new epoch -- it is terminated, or the next frame is rejected with
  `SESSION_REJECTION_EPOCH_ADVANCED`, so the client must call `Negotiate` again; `GetSnapshot`
  never substitutes a snapshot for a different epoch than the one requested. In both cases the
  client only obtains the new epoch, its capabilities, and its limits together through a fresh
  `Negotiate` call, followed by a fresh `GetSnapshot`.

### `reverse-edits` -- Reverse-order / out-of-order delivery

- **Method**: a transport redelivers an already-sent burst out of strict order, including at least
  one already-applied sequence.
- **Pass criteria**: a sequence lower than the last accepted one is `SESSION_OUTCOME_DUPLICATE`
  (identical content, answered with a freshly formed acknowledgement rather than the original
  `SESSION_OUTCOME_APPLIED` response) or `SESSION_REJECTION_DUPLICATE_CONFLICT` (differing
  content); the overlay is never mutated a second time by a re-delivered identical sequence, and
  a genuine conflict forces a full resync.

### `backpressure` -- Backpressure under a slow consumer

- **Method**: the server (or the client's bounded outbound queue) is driven past its documented
  buffer depth by a rate the consumer cannot keep up with.
- **Pass criteria**: the bounded queue refuses admission or the server emits `ResyncRequired`; no
  update is silently dropped without either an explicit rejection or a resync request.

### `restart-resync` -- Restart and full resync

- **Method**: the server process (or the client) restarts mid-session, and the surviving peer
  reconnects.
- **Pass criteria**: the reconnecting peer negotiates a new or resumed epoch, requests and applies
  a full `StageSnapshot` before accepting any delta, and never applies a delta numbered against a
  stale, pre-restart epoch.

### `auth-version-mismatch` -- Authentication and protocol-version mismatch

- **Method**: a client presents an invalid/missing credential; separately, a client whose major
  version differs from the server's negotiates.
- **Pass criteria**: an invalid/missing credential is `HANDSHAKE_REJECTION_UNAUTHENTICATED` before
  any capability/limit evaluation; a major-version mismatch is `HANDSHAKE_REJECTION_VERSION`
  before capability/limit evaluation; neither rejection attempts a mutation first.

### `independent-upgrades` -- Independent upgrade of client and server

- **Method**: the client is upgraded to a newer minor version while the server stays on an older
  compatible minor version, and vice versa.
- **Pass criteria**: negotiation succeeds on major-version equality alone; the newer side's
  additional capabilities are simply absent from the agreed set when the older side does not
  advertise them, and no additional-capability update is sent to or accepted by the side lacking
  it.

### `license-isolation` -- License and dependency isolation

- **Method**: building and testing `OpenUsd.Bridge.Protocol`/`OpenUsd.Bridge.Grpc` in isolation,
  without the companion repository present or referenced anywhere in this repository.
- **Pass criteria**: both packages restore, build, and test with no NVIDIA-licensed dependency, no
  reference to a companion repository, and no manifest requiring one;
  `tests/OpenUsd.Package.Tests/BridgePackageTests.cs` continues to assert this, unchanged by this
  specification.

## Machine-readable companion spec/config schema

[`eng/kit-companion-spec.schema.json`](../eng/kit-companion-spec.schema.json) is the JSON Schema for
[`eng/kit-companion-spec.json`](../eng/kit-companion-spec.json), the structured source behind this
document. `eng/generate-kit-companion-spec.py`:

- Validates `eng/kit-companion-spec.json` against that shape (hand-rolled, no external JSON Schema
  library, exactly like every other generator in this repository).
- Cross-checks `protocol.*` against `src/OpenUsd.Bridge.Protocol/BridgeProtocol.cs` and
  `src/OpenUsd.LiveAuthoring/LiveAuthoringValidation.cs` by regex, so this specification's numbers
  cannot silently drift from the code that implements the client side of the contract.
- Cross-checks `kitBaseline` against a real `kit-baseline` dependency in
  `eng/omniverse-profile.json`.
- Verifies the acceptance matrix carries exactly the fixed set of ids the approved plan requires.
- Verifies every referenced file (`.proto` sources, example artifacts, doc links) exists.
- Regenerates [`docs/omniverse-kit-companion-reference.g.md`](omniverse-kit-companion-reference.g.md).

Run it the same way as every other generator in this repository:

```shell
python eng/generate-kit-companion-spec.py            # regenerate the reference document
python eng/generate-kit-companion-spec.py --verify   # fail if stale or invalid (CI-safe)
python -m unittest discover eng/tests                 # runs eng/tests/test_generate_kit_companion_spec.py
```

## Checked examples

- [`examples/omniverse-kit-companion/extension.toml`](examples/omniverse-kit-companion/extension.toml)
  -- a structural sketch, not an installable manifest. See
  [Kit baseline placeholder](#extensiontoml-dependencies-and-the-kit-baseline-placeholder).
- [`examples/omniverse-kit-companion/companion_service.py`](examples/omniverse-kit-companion/companion_service.py)
  -- checked Python pseudocode shaped like [Exact server behavior](#exact-server-behavior), not a
  runnable module.
- [`examples/omniverse-kit-companion/README.md`](examples/omniverse-kit-companion/README.md)
  explains why neither file can be mistaken for a shipped, working extension.

## Related documents

- [Omniverse bridge protocol and client](omniverse-bridge.md) -- the wire contract and the C#
  client this specification's server side must interoperate with.
- [Omniverse interoperability profile](omniverse-profile.md) -- the version-pinned dependency
  baselines, including `kit-baseline`, and the pending Kit execution evidence.
- [Live authoring](live-authoring.md) -- the ordered admission, validation, and session recovery
  contracts the coordinator enforces on both the wire and Kit sides.
- [Generated companion reference tables](omniverse-kit-companion-reference.g.md) -- the same facts
  as this document's tables, generated directly from `eng/kit-companion-spec.json`.
