# Physics baking

Physics baking turns simulated results into scene description. `OpenUsd.Physics.Baking` offers two
distinct flows with deliberately different guarantees:

| Flow | Type | Destination | Guarantee |
| --- | --- | --- | --- |
| Preview apply | `UsdPhysicsPreviewApplier` | Removable physics session overlay only | Whole-batch, never saved |
| Transactional bake | `UsdPhysicsBaker` | One writable, file-backed layer | All-or-nothing, saved only on success |

Both flows consume the same immutable input — a `UsdPhysicsResultBatch` resolved against a
`UsdPhysicsBakeBindings` table — and both author through a single batched native call per chunk, so
neither flow ever performs per-element P/Invoke.

Baking requires the native `OPENUSD_CAPABILITY_PHYSICS_BAKE` capability (bit 20). Check
`UsdPhysicsPreviewApplier.IsSupported` or `UsdPhysicsBaker.IsSupported` before starting: when the
runtime does not export it, both flows return a result with `UsdPhysicsBakeStatus.NotSupported`
instead of throwing.

## Inputs

### Bindings

A `UsdPhysicsBakeBindings` table maps every stable `UsdPhysicsObjectId` to the extracted prim path
it came from. Bindings carry the `IdentityRevision` they were extracted at and, per entry, the
`TopologyRevision` of the prim they describe.

```csharp
var bindings = new UsdPhysicsBakeBindings(
    identityRevision: extraction.Revision,
    [
        new UsdPhysicsBakeBinding(bodyId, "/World/Crate"),
        new UsdPhysicsBakeBinding(clothId, "/World/Flag", InstanceIndex: -1, TopologyRevision: 4)
    ]);
```

`InstanceIndex` is `-1` for a prim-backed identity. A non-negative index means the identity is one
instance of a point instancer; both flows refuse those records with a precise diagnostic rather than
authoring an opinion that would move every sibling instance.

### Result batches

A `UsdPhysicsResultBatch` is fully detached: it retains no stage, prim, native handle, or transport
buffer, so it stays valid after the simulation advances, resets, or is disposed.

```csharp
var batch = UsdPhysicsResultBatch.FromFrame(frame, extraction.Revision, pointSamples);
```

A batch carries rigid, controller, vehicle, and articulation transforms as `UsdPhysicsBodyPose`
values, plus `UsdPhysicsPointSample` values for particle, cloth, and volume deformable domains. A
point sample optionally carries velocities and simulated topology.

### Whole-batch rejection

Before anything is authored, the batch is resolved against its bindings:

- a batch whose `IdentityRevision` differs from the bindings is rejected whole;
- an identity that is not bound is rejected;
- a point sample whose `TopologyRevision` differs from its binding is rejected;
- a point-instancer instance is rejected.

Any rejection aborts the whole batch. A half-applied frame would show a pose that never existed, so
partial application is never an option.

## Preview apply

`UsdPhysicsPreviewApplier` authors into `UsdSessionOverlay.PhysicsLayerIdentifier`, which is the
strongest temporary layer on the stage and is never saved. The stage root layer, every referenced
layer, the session layer, and the overlay's own user layer are never written by a preview; the
native call additionally runs with a forbid-root-layer flag so an overlay misconfiguration fails
loudly instead of contaminating authored scene description.

```csharp
using var applier = new UsdPhysicsPreviewApplier(scheduler, overlay);

UsdPhysicsPreviewResult result = await applier.ApplyAsync(batch, bindings, cancellationToken);
if (result.Status != UsdPhysicsBakeStatus.Completed)
{
    foreach (UsdPhysicsDiagnostic diagnostic in result.Diagnostics.Entries)
    {
        Console.Error.WriteLine(diagnostic);
    }
}
```

Preview opinions are authored at the default time, not as time samples, so they override authored
values without adding animation.

Stopping or resetting a simulation calls `ClearAsync`:

```csharp
UsdPhysicsPreviewClearResult clear = await applier.ClearAsync(cancellationToken);
```

`ClearAsync` first migrates any user edit that landed in the physics layer into the overlay's user
layer, then clears the physics layer. User and session authored opinions therefore survive a reset.
Contamination detection, contamination migration, and the layer clear all run inside one scheduled
edit, because all three touch the scheduler-owned stage; none of them ever runs on the awaiting
thread.

### Suppressing your own preview edits

A renderer that watches `UsdStageScheduler.ReadChangesAsync` sees the preview's own edits and would
otherwise re-read the stage for a change it caused itself. Both `ApplyAsync` and `ClearAsync`
therefore report the exact change serial pairs their scheduled edits produced:

```csharp
UsdPhysicsPreviewResult result = await applier.ApplyAsync(batch, bindings, cancellationToken);
foreach (UsdPhysicsPreviewEdit edit in result.Edits)
{
    selfEdits.Add((edit.BeforeChangeSerial, edit.AfterChangeSerial));
}

await foreach (UsdStageChange change in scheduler.ReadChangesAsync(cancellationToken))
{
    if (selfEdits.Remove((change.BeforeChangeSerial, change.AfterChangeSerial)))
    {
        continue; // Our own preview edit.
    }
    Invalidate(change);
}
```

The serials are read on the scheduler's own thread, immediately around the single batched authoring
call, so they are the same values the scheduler samples before and after the callback. Matching both
serials is therefore exact: a pair can never absorb an unrelated edit that interleaved between
chunks, and an unrelated edit can never be mistaken for a preview edit. One edit is reported per
authored chunk that moved the serial, so a batch split into `ChunkSize` chunks reports up to that
many pairs, in publication order; a chunk that changed nothing reports nothing, matching the fact
that the scheduler publishes nothing for it. `UsdPhysicsPreviewResult.Edits` is populated on the
cancellation and failure paths too, so chunks already authored before an abort are still
suppressible.

## Transactional bake

A bake is an explicit user operation and behaves like one.

### Preflight

`PreflightAsync` validates everything without mutating the stage:

```csharp
var spec = new UsdPhysicsBakeSpec(
    destinationLayerIdentifier: bakeLayerIdentifier,
    startTimeCode: null,   // stage start
    endTimeCode: null,     // stage end
    sampleStride: null,    // one sample per time code
    options: UsdPhysicsBakeOptions.Default,
    save: true);

UsdPhysicsBakePreflightResult preflight =
    await baker.PreflightAsync(spec, representativeBatch, bindings, cancellationToken);
```

The destination is rejected unless it resolves, participates in the stage local layer stack, is file
backed rather than anonymous, is unmuted, permits editing, permits saving, and is neither the root
nor the session layer. `UsdPhysicsBakePreflightResult.Layer` reports each of those facts
individually so a UI can explain the refusal.

Records are also preflighted through the runtime with a preflight-only page, which measures — without
authoring — whether every path resolves, is of the right type, is not an instance proxy, is not
inside a prototype, and has a sample capacity matching the composed topology.

### Committing one frame

```csharp
UsdPhysicsBakeTransactionResult result =
    await baker.CommitFrameAsync(spec, batch, bindings, cancellationToken);
```

The batch is authored as exactly one time sample at `batch.TimeCode`.

### Baking a time range

```csharp
UsdPhysicsBakeTransactionResult result = await baker.BakeAsync(
    spec, source, bindings, progress, cancellationToken);
```

`source` is an `IUsdPhysicsBakeSource` that returns one complete batch per sampled time code. The
sample grid is derived from the stage `startTimeCode`, `endTimeCode`, and the requested stride, and
every time code is computed from its index rather than accumulated, so two bakes of the same range
author identical time codes. Records within a sample are authored in a stable path-then-identity
order, which makes the output byte-for-byte reproducible.

`progress` is reported between bounded chunks, and cancellation is observed between chunks. Each
destination nevertheless remains all-or-nothing: cancelling mid-bake rolls the destination all the
way back.

### Transactional guarantees

Before the first sample is authored the destination layer's complete content is snapshotted into an
anonymous layer held by the runtime. Any failure, cancellation, or save error transfers that
snapshot back, so a failed bake leaves the destination exactly as it was — the stage root layer is
never reloaded, the session overlay is never touched, and the caller's edit target is restored after
every chunk. `UsdPhysicsBakeTransactionResult.WasRolledBack` and `WasSaved` report which path ran.

Only one transaction may be open for a destination at a time. The runtime reserves the resolved
layer identity when the transaction begins, so a second baker — even one driving a different stage
or scheduler — is refused with a diagnostic instead of taking a second snapshot that a later
rollback could use to undo already committed work. The reservation is released when the transaction
commits, rolls back, or is released, including on every failure path.

Saving is the last step, and it is the only step that can fail after everything has been authored.
When it fails the transaction deliberately stays open and the layer content is left untouched, so
the rollback that follows still finds its snapshot. A save failure therefore always reports
`WasRolledBack = true` and `WasSaved = false`, never a failure that claims nothing was restored.

The destination is saved only after the entire bake succeeds and only when `UsdPhysicsBakeSpec.Save`
is set and the layer is both file backed and saveable.

### Untrusted page validation

The authoring page is a flat binary blob, so the runtime treats it as untrusted input. Every record
is bounds checked against the section sizes declared in the page header, element counts are scaled
in 64-bit arithmetic so a product can never wrap into a smaller value, and both point and face
counts are capped at 64Mi per record before anything is allocated or iterated. A page that fails
any of these checks is rejected as a whole and nothing is authored.

## What is authored

Standard USD is used wherever the simulated state is representable:

| Simulated state | Authored as |
| --- | --- |
| Rigid, controller, vehicle, articulation transform | `xformOp:transform` plus `xformOpOrder` |
| Linear and angular velocity | `physics:velocity`, `physics:angularVelocity` |
| Kinematic flag | `physics:kinematicEnabled` |
| Particle, cloth, deformable points | `points` |
| Point velocities | `velocities` |
| Simulated topology | `faceVertexCounts`, `faceVertexIndices` |
| Bounds of the simulated points | `extent` |

State that standard USD cannot express is authored into the project-owned `openUsdPhysics`
simulation namespace instead of being silently dropped:

| Attribute | Meaning |
| --- | --- |
| `openUsdPhysics:simulation:identity` | The stable 64-bit simulated identity |
| `openUsdPhysics:simulation:identityIndex` | The record kind the identity was authored as |
| `openUsdPhysics:simulation:sourceRevision` | The extraction revision the results came from |
| `openUsdPhysics:simulation:sleeping` | Whether the body was asleep at this sample |

Set `UsdPhysicsBakeOptions.WriteSimulationMetadata` to `false` to author only standard USD.

## Options

| Option | Default | Effect |
| --- | --- | --- |
| `TransformSpace` | `World` | `World` authors `!resetXformStack!`; `LocalToParent` composes against the parent |
| `ExistingSamplePolicy` | `Overwrite` | `Skip` leaves an existing sample alone; `Reject` fails the whole destination |
| `WriteVelocities` | `true` | Author velocity attributes |
| `WriteExtents` | `true` | Author `extent` for point records |
| `WriteSimulationMetadata` | `true` | Author the project-owned simulation namespace |
| `ChunkSize` | `4096` | Records authored per native call, which bounds progress and cancellation granularity |

`LocalToParent` resolves each parent pose against the transforms the batch itself is going to
produce, not against whatever is composed at the moment a chunk runs. A parent, its child, and its
grandchild therefore compose to the same world transforms whether they land in one chunk or in
three, and `ChunkSize` never changes the authored bytes.

## Diagnostics

Every rejection produces a `UsdPhysicsDiagnostic` in the `Bake` category with a stable code:

| Code | Meaning |
| --- | --- |
| `OPENUSD_PHYSICS_BAKE_UNAVAILABLE` | The runtime does not export batched physics authoring |
| `OPENUSD_PHYSICS_BAKE_STALE_IDENTITY` | The batch revision does not match the bindings |
| `OPENUSD_PHYSICS_BAKE_STALE_TOPOLOGY` | A point sample describes topology that no longer exists |
| `OPENUSD_PHYSICS_BAKE_UNBOUND_IDENTITY` | A simulated identity is not bound to an extracted prim |
| `OPENUSD_PHYSICS_BAKE_LAYER_REJECTED` | The destination layer is not a legal bake target |
| `OPENUSD_PHYSICS_BAKE_RECORD_REJECTED` | The runtime refused one record |
| `OPENUSD_PHYSICS_BAKE_NATIVE_FAILURE` | Authoring failed inside the runtime |
| `OPENUSD_PHYSICS_BAKE_ROLLED_BACK` | The destination was restored to its prior content |
| `OPENUSD_PHYSICS_BAKE_CANCELED` | The operation was canceled |
| `OPENUSD_PHYSICS_BAKE_UNSUPPORTED_DOMAIN` | The source produced no results for a sampled time code |

Per-record refusals are additionally reported as `UsdPhysicsBakeRecordOutcome` values with a
`UsdPhysicsBakeRecordStatus`, which distinguishes `PathMissing`, `NotTransformable`,
`NotPointBased`, `InstanceProxy`, `InPrototype`, `SampleCountMismatch`, `ExistingSample`,
`UnsupportedKind`, `AuthoringFailed`, and `InvalidRecord`.

## Testing

Native-backed baking tests live in `tests/OpenUsd.Physics.Tests/Baking` and skip when the runtime
does not export the capability. Run them through the staged native runtime:

```powershell
pwsh eng/run-native-managed-tests.ps1 `
    -Project tests/OpenUsd.Physics.Tests/OpenUsd.Physics.Tests.csproj `
    -Framework net10.0
```

See [Testing](testing.md) for the wider gate selection.

`UsdPhysicsBakeRegressionTests` additionally drives hand-corrupted authoring pages straight at the
runtime, contends two bakers on two schedulers for one destination, blocks the destination so the
runtime's own save genuinely fails, and bakes a three-level hierarchy at several chunk sizes to
prove the authored bytes do not depend on chunking.

Blocking a save portably needs more than a read-only file. OpenUSD saves a layer by writing a
sibling temporary file and renaming it over the destination, and on Unix that rename succeeds as
long as the containing directory is writable. `BakeSaveBlocker` therefore holds an exclusive
`FileShare.None` handle on Windows and removes the write permission from both the destination and
its containing directory on Unix, then probes itself: a process that ignores directory permissions
cannot be blocked, so the affected test skips with the reason instead of asserting a failure the
platform will not produce. `SaveBlocker_BlocksBothDirectWritesAndTemporaryFileRenames` verifies the
mechanism on whichever operating system the suite runs on, without needing the native runtime.
