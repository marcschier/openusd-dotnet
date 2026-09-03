# OpenUsd.LiveAuthoring

## Purpose and non-goals

`OpenUsd.LiveAuthoring` is a managed package for applying ordered external updates to one
scheduler-owned OpenUSD stage. It provides a bounded, discriminated data model (scalars, arrays,
matrices, metadata, and a curated API-schema registry), pure validation, bounded admission separated
from the eventual applied result, opaque correlation/origin tracking, structured health snapshots and
events, tail coalescing, scheduler execution, ownership of the exact retained render source, and a
transport-neutral session coordinator with explicit recovery semantics.

This is a supported package published from `src/OpenUsd.LiveAuthoring` with its own
`PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt` baseline, targeting `net8.0`, `net9.0`, and `net10.0`.

Keep these three scopes separate:

1. **Adapter package:** this directory contains reusable contracts, the queue, the executor, and the
   host. It has no entry point.
2. **Executable sample:** [OpenUsd.LiveAuthoring.Sample](../../samples/OpenUsd.LiveAuthoring.Sample/README.md)
   constructs batches, uses a fake render consumer, and verifies the adapter.
3. **External OPC UA Pump integration:** a Pump-owned sink maps monitored values and ordering into these
   contracts. That integration is outside this repository and outside this package.

The adapter is not an OPC UA client, renderer, stage viewer, transaction engine, multi-producer
ordering service, or distributed merge engine. It adds no network transport: there is no gRPC,
protobuf, or WebSocket dependency, and the versioned wire contract that will carry snapshots and deltas
is a separate, later adapter described in [Live authoring](../../docs/live-authoring.md). It does not
promise rollback after a native or unsupported-operation failure partway through a validated batch:
raw `QueuedLiveAuthoringSink` partial-failure semantics are explicit and retained by design. Recovery
from such a failure lives in `LiveAuthoringSessionCoordinator`, which moves the session to
`ResyncRequired` and waits for a newer full snapshot instead of synthesizing a rollback.

## Prerequisites and matching native runtime

- .NET SDK 10.0.301.
- No native runtime for a managed build or queue/validation/data-model tests.
- A matching Core runtime when an executable creates or opens a stage.
- A platform NativeAOT toolchain when publishing an executable.

| Host | RID | Runtime package |
| --- | --- | --- |
| Windows x64 | `win-x64` | `OpenUsd.Runtime.Core.win-x64` |
| Linux x64 | `linux-x64` | `OpenUsd.Runtime.Core.linux-x64` |
| macOS arm64 | `osx-arm64` | `OpenUsd.Runtime.Core.osx-arm64` |

The consuming executable must use the same `OpenUsd` and runtime package version. The package needs no
Imaging runtime unless the chosen render consumer separately uses Hydra, Storm, or another imaging
backend.

## Commands from the repository root

Build all three target frameworks:

```powershell
dotnet build src/OpenUsd.LiveAuthoring/OpenUsd.LiveAuthoring.csproj -c Release
```

Run the queue, validation, admission, correlation, health, and data-model tests, which do not require
native binaries:

```powershell
dotnet build tests/OpenUsd.LiveAuthoring.Tests/OpenUsd.LiveAuthoring.Tests.csproj `
    -c Release -f net10.0
.\eng\run-managed-tests.ps1 `
    -Project tests/OpenUsd.LiveAuthoring.Tests/OpenUsd.LiveAuthoring.Tests.csproj `
    -Framework net10.0 `
    -Configuration Release
```

This project is a library, so `dotnet run` is not valid. Run the separate executable after configuring
the matching native runtime:

```powershell
dotnet run --project samples/OpenUsd.LiveAuthoring.Sample/OpenUsd.LiveAuthoring.Sample.csproj -c Release
```

See the executable README for exact source-runtime setup and NativeAOT commands.

## Source versus package consumption

Inside the repository, the package references `src/OpenUsd` with `ProjectReference`. An external
application references `OpenUsd.LiveAuthoring` as an ordinary `PackageReference` at the same version as
`OpenUsd` and `OpenUsd.Runtime.Core`.

Use the local feed and source-mapping process in [Pack](../../docs/packaging.md#pack) and the
[package-only execution gate](../../docs/packaging.md#package-only-execution-gate) when validating
repository-built packages. Do not add OPC UA packages to this package merely to consume it from Pump.

## Expected output and files

A Release build produces `OpenUsd.LiveAuthoring.dll` for `net8.0`, `net9.0`, and `net10.0`. It produces
no executable and authors no USD file. The targeted test command reports passing queue, validation,
admission, correlation, health, coalescing, cancellation, data-model, and disposal tests.

Stage files and console output belong to the hosting executable. With the default session edit layer,
live edits are transient unless the host deliberately authors and saves the root layer.

## Data model

`LiveStageUpdate` is a closed, discriminated hierarchy rather than one shallow type per USD primitive:

- `DefinePrimUpdate`, `RemovePrimUpdate`, `SetActiveUpdate`, `SetInstanceableUpdate` — prim existence
  and classification.
- `SetAttributeUpdate` carries one `LiveAttributeValue`, a bounded, NativeAOT-safe discriminated union
  covering Boolean/Int64/Double/String/Token/Vec3f scalars, `Matrix4d`, and Int32/Float/Double/Vec2f/
  Vec3f/Color3f/Boolean/Token/String arrays — all backed by existing typed `UsdPrim` accessors.
- `ClearUpdate` explicitly clears or removes an authored opinion — an attribute value, relationship
  targets, references, payloads, or one metadata field — discriminated by `LiveClearTargetKind` rather
  than one type per clearable concept.
- `SetRelationshipTargetsUpdate`, `SetReferenceUpdate`, `SetPayloadUpdate`, `SetVariantSelectionUpdate`
  (a `null` selection already clears it) — composition and relationship arcs.
- `SetMetadataUpdate` carries a `LiveMetadataValue` (Boolean/Int64/Double/String).
- `ApiSchemaUpdate` applies or removes a single-apply API schema by its bare token (for example
  `"AssetPreviewsAPI"`), discriminated by `LiveApiSchemaOperation`. Only a bounded, curated registry
  (`UsdStageBatchExecutor.SupportedApiSchemaTokens`) is supported for apply, reusing each schema's own
  typed `Apply(UsdPrim)` method; removal has no underlying typed API yet and is always rejected with an
  explicit `NotSupportedException` rather than silently no-op-ing.
- `SetPointInstancerOrientationsUpdate` authors a `UsdGeomPointInstancer` quaternion orientation array
  through the existing typed facade; positions, velocities, and scales for the same prim are ordinary
  Vec3f-array `SetAttributeUpdate`s against the `"positions"`, `"velocities"`, and `"scales"` attributes.
- `ReplaceBridgeOverlayUpdate` replaces one bridge-owned overlay subtree with a complete snapshot inside
  a single scheduler edit. It must be the only update in its batch, cannot nest another replacement, and
  every nested update must target its bridge root or a descendant.

## Session recovery

`LiveAuthoringSessionCoordinator` sits in front of an `ILiveAuthoringSink` and owns one bridge session.
It is transport-neutral: an adapter decodes any wire format into `LiveAuthoringSnapshot` and
`LiveAuthoringDelta` values and calls the coordinator, so every rule below is testable without
networking.

- **Explicit states.** `Disconnected`, `Connecting`, `Synchronized`, `ResyncRequired`, `Stopping`, and
  `Faulted`. Only `Synchronized` accepts deltas.
- **One authoritative remote.** `LiveAuthoringRemoteEpoch` names the remote origin, session identifier,
  and epoch generation. A different origin or session is rejected; an older epoch is retired; a newer
  epoch forces a full resync because sequences are only comparable inside one epoch.
- **Duplicate and gap rules.** A replayed sequence is acknowledged as an idempotent `Duplicate` only
  when a bounded replay ledger proves it is the same message: a SHA-256 fingerprint over the epoch,
  effective origin, correlation identifier, coalescing key, and canonical update content must match.
  A retained sequence carrying different content is a `DuplicateConflict`, a replay below the retained
  window is `ReplayExpired`, and both force a full resync rather than a false acknowledgement. A
  sequence more than one above the last accepted one is a `SequenceGap`.
- **Bounded replay ledger.** `LiveAuthoringSessionOptions.ReplayWindowLength` (capped by
  `LiveAuthoringValidation.MaxReplayWindowLength`, byte-capped by `MaxReplayLedgerBytes`) sets how many
  recent sequences are retained. Each entry costs a fixed `ReplayLedgerEntryBytes` and never holds the
  payload. `GetStatus()` exposes `ReplayWindowLength`, `ReplayLedgerCount`, `ReplayLedgerBytes`, and
  `OldestRetainedSequence`. Connect, reconnect, disconnect, and every applied snapshot clear it.
- **Loop prevention, after identity.** Session state, authoritative remote origin, session identifier,
  and epoch are validated *before* loop suppression, so a stale or misrouted message can never be
  reported as a harmless echo. A genuine echo (`OriginId` equal to `LocalOriginId`) is not re-authored,
  but still consumes its remote sequence so the stream stays contiguous.
- **Bridge-owned overlay.** `BridgeRootPath` (default `/Bridge`) must be reserved for the bridge. A full
  snapshot removes and re-authors only that subtree in the current edit-target layer, so a user-edit
  layer, a `UsdSessionOverlay` physics overlay, and the root layer keep every opinion outside it.
- **Staged validation and atomic replacement.** `UsdStageBatchExecutor` re-validates every nested update
  against the bridge root before removing anything, then removes, re-anchors, and re-authors inside one
  serialized scheduler edit. A partway failure removes the bridge root again, leaving an empty overlay
  rather than a mixture of two snapshots.
- **No per-batch checkpoints.** Any incremental apply failure moves the session to `ResyncRequired` and
  rejects deltas until a newer full snapshot succeeds. High-rate transform traffic never pays for a
  checkpoint.
- **Bounded snapshots.** `LiveAuthoringValidation.MaxBridgeOverlayUpdates` and
  `MaxBridgeOverlayPayloadBytes` bound an overlay. `ExportSnapshot()`/`ExportOverlayUpdates()` return
  exactly what the bridge authored, in canonical order, so an export rebuilds the same overlay on a
  fresh session.
- **Observability and disposal.** `GetStatus()` returns a bounded `LiveAuthoringSessionStatus`, an
  optional `IProgress<LiveAuthoringSessionEvent>` receives bounded events, observer failures are
  isolated and counted, and `DisposeAsync` is idempotent, drains the in-flight operation, and disposes
  the sink only when `OwnsSink` is set.

## Bounds and finiteness

Every limit below is a public `LiveAuthoringValidation` constant, enforced by pure managed validation
before a batch reaches the scheduler:

- `MaxUpdatesPerBatch` — updates in one batch.
- `MaxOpaqueIdLength` — a `CorrelationId` or `OriginId`.
- `MaxIdentifierLength` — an attribute/relationship/variant-set/variant name, schema token, prim type
  name, or metadata key.
- `MaxPathLength` — an absolute prim path, including relationship targets and reference/payload target
  prim paths.
- `MaxTextValueLength` — a coalescing key, asset path, or scalar/array string/token value.
- `MaxCollectionElementCount` — relationship targets, known variants, one attribute array, or one
  orientation array.
- `MaxTotalCollectionElementCountPerBatch` — the combined relationship/variant/array/orientation
  element count across one whole batch.
- `MaxBridgeOverlayUpdates` — the authored opinions in one bridge-owned overlay and therefore in one
  full snapshot.
- `MaxBridgeOverlayPayloadBytes` — the estimated retained payload of one bridge-owned overlay, sized
  below `MaxEstimatedBatchPayloadBytes` so a full overlay still fits inside its replacement batch.
- `MaxReplayWindowLength`, `DefaultReplayWindowLength`, `ReplayLedgerEntryBytes`,
  `MaxReplayLedgerBytes` — the entry and byte bounds on one session's replay ledger.
- `MaxEstimatedBatchPayloadBytes` — the combined estimated retained byte size across one whole batch:
  every text value counted as its UTF-8 byte length, plus every numeric scalar/array value counted at
  its natural in-memory size. Element-count bounds alone do not bound retained memory (millions of
  string elements at the per-element text limit could retain tens of gigabytes); this closes that gap
  with a bound sized for a bounded, high-rate live-authoring burst rather than a bulk scene dump.

Every `double`, `float`, `Vec2f`/`Vec3f`/`Color3f`, `Matrix4d`, and quaternion orientation value — scalar
or array element — must be finite; `NaN` and infinities are rejected with an
`ArgumentOutOfRangeException` before the value can reach the stage.

`LiveAttributeValue.From*Array` and the point-instancer orientation snapshot enforce
`MaxCollectionElementCount` (and, for text arrays, `MaxTextValueLength` per element) before copying, so
an oversized input is rejected immediately rather than after a large allocation. Every accumulation
behind these bounds is overflow-checked and surfaces as a bounded `ArgumentException`.

## Admission and observability

`ILiveAuthoringSink.ApplyAsync` returns a `LiveAuthoringAdmissionReceipt` as soon as a batch is admitted
(enqueued or coalesced), separately from the eventual applied result:

- `Sequence`, `CorrelationId`, and `OriginId` on the receipt double as the sequence acknowledgement: an
  opaque, caller-assigned correlation/origin pair that this library never interprets, only carries
  through to the receipt, the applied `LiveAuthoringBatchResult`, and health events. Admission is an
  in-process memory guarantee, not a durability guarantee: it does not survive a process crash or
  restart before the batch reaches the stage.
- `Applied` is the `Task<LiveAuthoringBatchResult>` that completes once the batch is actually applied
  (or faults). It is independent of the caller's own cancellation token, so cancelling a
  `WaitForResultAsync` wait never cancels the underlying ordered edit.
- Tail coalescing runs one edit for several superseded callers, but each caller's own result still
  carries that caller's own `CorrelationId`/`OriginId` — never another coalesced caller's — while
  sharing the coalesced sequence range, batch count, and actual execution outcome.
- `QueuedLiveAuthoringSink.GetHealthSnapshot()` returns a bounded `LiveAuthoringHealthSnapshot`
  (capacity, pending/peak/coalesced counts, accepting state, last admitted/applied/failed sequence and
  detail, and health-observer failure count/detail) for polling from an external health endpoint.
- An optional `IProgress<LiveAuthoringHealthEvent>` (`UsdLiveAuthoringOptions.HealthObserver`, or passed
  directly to `QueuedLiveAuthoringSink`) receives bounded, length-capped `Admitted`, `Coalesced`,
  `Rejected`, `Applied`, `Failed`, and `Disposed` events. An observer is untrusted code running inline
  on the admission or worker path: every exception it throws is caught, counted, and recorded (never
  rethrown), so a broken observer can neither fail `ApplyAsync` nor lose an already-admitted batch.
- The existing queue metrics (`Capacity`, `PendingBatchCount`, `PeakPendingBatchCount`,
  `CoalescedBatchCount`, `HealthObserverFailureCount`) remain available directly on
  `QueuedLiveAuthoringSink`.

## Important code and behavior

- `LiveAuthoringContracts.cs` defines `ILiveAuthoringSink`, `ILiveAuthoringBatchExecutor`,
  `IUsdStageRenderSourceConsumer`, and options.
- `LiveAuthoringValues.cs` defines `LiveAttributeValue` and `LiveMetadataValue`.
- `LiveAuthoringUpdates.cs` defines the `LiveStageUpdate` hierarchy.
- `LiveAuthoringBatch.cs` defines `LiveAuthoringBatch`, `LiveAuthoringBatchResult`, and
  `LiveAuthoringAdmissionReceipt`.
- `LiveAuthoringHealth.cs` defines `LiveAuthoringHealthSnapshot` and `LiveAuthoringHealthEvent`.
- `LiveAuthoringSession.cs` defines `LiveAuthoringSessionState`, `LiveAuthoringRemoteEpoch`,
  `LiveAuthoringSessionEvent`, `LiveAuthoringSessionStatus`, and `LiveAuthoringSessionOptions`.
- `LiveAuthoringSnapshot.cs` defines `LiveAuthoringSnapshot`, `LiveAuthoringDelta`, and the bounded
  outcome/rejection results.
- `LiveAuthoringOverlayModel.cs` keeps the coordinator's slot-keyed model of the bridge-owned overlay so
  an export is deterministic and never claims opinions the bridge does not own.
- `LiveAuthoringSessionCoordinator.cs` owns session state, epoch identity, duplicate/gap rules, loop
  prevention, snapshot recovery, and deterministic disposal.
- `LiveAuthoringDeltaFingerprint.cs` computes the content-derived identity a replay decision needs.
- `LiveAuthoringReplayLedger.cs` retains a bounded window of accepted sequences and their fingerprints.
- `LiveAuthoringValidation.cs` rejects invalid paths, identifiers, values, opaque-ID lengths, and
  batches before admission.
- `QueuedLiveAuthoringSink.cs` admits one logical producer in strictly increasing sequence order.
- `UsdStageBatchExecutor.cs` selects the edit layer and translates each validated update to public APIs.
- `UsdLiveAuthoringHost.cs` owns one `UsdStageScheduler` and one `UsdStageRenderSource`.

One logical producer may have multiple outstanding submissions, but independent producers must
serialize before calling the sink. A full queue can replace only its tail when both batches have the
same non-empty coalescing key. Cancellation before admission prevents admission entirely. Cancellation
of a receipt's `WaitForResultAsync` call only cancels that caller's wait; the ordered edit is still
drained and applied.

The render consumer must attach to the scheduler and retained source supplied by the host. Reopening the
stage path would create a different stage identity and break authoring/render synchronization.

## NativeAOT

The package targets .NET 8, 9, and 10 with trimming, AOT, and single-file analyzers enabled (inherited
from the repository's production-library defaults). Publishing NativeAOT is an executable operation;
use the sibling executable or the external host. On Windows, run the publish command from an x64 Visual
Studio developer shell:

```powershell
dotnet publish samples/OpenUsd.LiveAuthoring.Sample/OpenUsd.LiveAuthoring.Sample.csproj -c Release `
    -r win-x64 -p:AotSample=true -o artifacts\samples\live-authoring-aot
```

Use the matching RID and Core runtime. The contracts avoid boxed domain payloads and runtime code
generation; `LiveAttributeValue` and `LiveMetadataValue` are explicit NativeAOT-safe discriminated
values, and array payloads are defensively copied plain arrays, never `object`-boxed collections.

## External OPC UA Pump integration

The external Pump adapter should:

1. Use one serialized producer and assign strictly increasing `LiveAuthoringBatch.Sequence` values.
2. Map Pump domain values to `LiveAttributeValue` without adding OPC UA types to this package.
3. Use stable coalescing keys only when a newer pending snapshot fully supersedes the older one.
4. Assign an opaque `CorrelationId`/`OriginId` per batch if it wants to correlate admission and applied
   results with its own monitored-item or session identifiers.
5. Forward cancellation and observe every admission receipt's `Applied` task, or attach a
   `HealthObserver`, so admission, native, and disposal failures reach Pump health reporting.
6. Give the selected renderer the host-provided scheduler and render source; never reopen the stage path.

Pump owns monitored-item semantics, reconnection, namespace mapping, health reporting, and deployment.
This repository neither builds nor validates that external integration.

## Troubleshooting

- "`dotnet run` is not supported": run `OpenUsd.LiveAuthoring.Sample`, not this package.
- Sequence rejection: serialize producers and submit positive, strictly increasing sequence numbers.
- Queue stalls: observe every admission receipt's `Applied` task or attach a `HealthObserver`, and check
  `GetHealthSnapshot()` for whether the bounded consumer is making progress.
- Missing persisted edits: the default edit target is the session layer, which `UsdStage.Save` omits.
- Stage-bound result exception: return detached data from scheduler callbacks, not `UsdPrim` or `UsdStage`.
- Native load or ABI error: fix the final executable's Core runtime RID/version, not this package.
- API schema removal always throws `NotSupportedException`: no underlying OpenUSD typed API exposes
  generic schema removal yet; only application is supported for the curated registry.
- `ResyncRequired` rejections: send a newer full snapshot. The session deliberately has no per-batch
  checkpoint to retry from, and resending the rejected delta stays rejected.
- `BridgeScope` rejections: the update targets a prim outside `BridgeRootPath`. Reserve that root for the
  bridge and author user content elsewhere.
- Snapshot rejected with `EpochRetired`: the remote sent an older epoch. Advance the epoch on the remote
  side rather than lowering it locally.
- `DuplicateConflict` rejections: the producer reused a remote sequence for different content. Fix the
  producer's sequence allocation; a reused sequence is a broken ordering agreement, not a retry.
- `ReplayExpired` rejections: the producer replayed further back than `ReplayWindowLength` retains.
  Raise the window to at least the adapter's deepest retransmission, or stop replaying acknowledged
  sequences.

## Next documentation

- [Live authoring](../../docs/live-authoring.md)
- [Executable sample](../../samples/OpenUsd.LiveAuthoring.Sample/README.md)
- [Samples overview](../../samples/README.md)
- [Data API](../../docs/data-api.md)
- [Architecture](../../docs/architecture.md)
- [Rendering](../../docs/rendering.md)
- [Packaging](../../docs/packaging.md)
