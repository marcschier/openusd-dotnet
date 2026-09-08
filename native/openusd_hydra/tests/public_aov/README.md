# Public Storm AOV contract

The public ABI 9 implementation installs `openusd_storm_aov.h` and exports exactly
`openusd_storm_aov_capture`, `openusd_storm_aov_get_view`, and
`openusd_storm_aov_release`. Data ABI 23 and child ABI 8 are unchanged.
Current managed Storm callers require the matching ABI 9 runtime.
Published `0.14.0-alpha` assets remain ABI 8 and cannot serve these unreleased APIs.
The older B7F113 private proof and its frozen SDK/runtime/receipts are historical.

The native version-1 request/output/identity/context/view sizes on Windows x64
are 640/64/56/24/688 bytes. All scalar record fields have fixed widths.
Pointers borrow owner-managed memory; pixel scalars have native byte order.
`get_view` requires the current structure size/version and clears failure output.
`capture` clears its owner output on failure. Release is null-safe, CPU-only,
and usable on any thread without GL. A reader must retain its owner while using
any borrowed view, even after the get-view call returns.

## Managed boundary

`OpenUsdStormRenderer.RenderAovs(StormAovRequest request)` returns a detached
`StormAovSnapshot`. An internal SafeHandle is allocated before capture and
leased across get-view, complete preflight, and all managed copies.
Release never runs a renderer/GL destructor.

`StormAovRequest` validates dimensions, time, limits, output kinds, duplicates,
and required identity inputs before retaining private copies.
Its camera getter is defensive. Output and identity/context collections use
`OwnedReadOnlyList<T>` rather than mutable arrays or wrappers exposing SyncRoot.

Available payloads are `StormAovOutput<StormAovColor>` (four Half components),
`StormAovOutput<float>` (window depth), `StormAovOutput<int>` (native IDs), and
`StormAovOutput<StormAovNeye>` (four raw bytes). Unavailable outputs have explicit
status and no synthetic typed payload. All rows are tightly packed, top-down.

The decoder checks every view/record version, name, status, scalar format, stride,
origin, extent, pointer/count pairing, address overflow, reserved field, UTF-8
slice, canonical path shape, identity/context range, unique pair, pixel-to-table
binding, and scalar range before allocating or copying managed payloads.
Managed string/reference expansion is admitted separately from native storage.

`CallerStateRevision` and `CallerSceneRevision` are explicitly opaque caller
claims, not independently measured USD serials. Time and dimensions are checked
against the request. `AppliedCamera` preserves the actual native double matrices
and clip equations without narrowing them to float. Explicit camera inputs must
match those applied values. Capture failures invalidate the managed pick binding;
successful capture rebinds picking to the exact requested frame inputs.

## Truthful support boundary

| Output | Native representation | Status on the pinned route |
| --- | --- | --- |
| color | Float16 RGBA render color | Ready after real readback |
| depth | Float32 OpenGL window depth, `[0,1]`, clear 1 | Ready; not linear distance |
| primId | Signed native Hydra ID, clear -1 | Ready; snapshot-local |
| instanceId | Signed native Hydra ID, clear -1 | Ready; not authored ids |
| elementId | No returned buffer | Absent |
| Neye | Raw UNorm8 x 4 | Ready; not signed float-normal precision |
| normal | No supported descriptor | Unsupported |

Identity records retain canonical prim paths, optional instancer paths, and
SDK-ordered context. Raw IDs, table indices, and flattened decode ordinals are
snapshot-local, not stable authored identity. Local context indices are distinct
from flattened ordinals and need not equal them. Background indices are
`UINT32_MAX`; unresolved pairs retain their own explicit records.

## Bounds and presentation

Native hard limits remain 8 slots, 4096 per dimension, 1,048,576 pixels, 4096
unique pairs, 16,384 contexts, 1 MiB UTF-8, and 128 MiB known copy/readback working
storage. The optional identity flag controls the unused native limits.
Managed snapshots have a separate 128 MiB ceiling, including conservative string
and reference expansion. This is not an RSS, whole-scene, GPU, opaque SDK-decoder
allocation, or driver-wait-duration guarantee.

Native admission includes retained and replacement HdSt CPU map storage. Its
per-kind high-water is conservative and can remain after output changes.
Descriptors and resolved backing texture shape are admitted before copy allocation
or Map; RAII Unmap covers success and exceptions.

OpenUSD 26.05 disables viewport AOV selection when multiple outputs are selected
and enables AOV MSAA independently of the caller framebuffer. A project-owned
engine subclass uses the pinned SDK's protected controller extension seam and
public controller methods to keep color presentation while retaining all MRT
outputs. It preserves the target's single-versus-multisample mode, including
implicit paired ID and depth buffers. No SDK source or private memory layout is
patched. Future SDK upgrades must revalidate this extension seam.

The C11 probe covers first capture, ordinary renders before/after capture, literal
depth, real invalid requests, and detached owner lifetime. The existing child
probe runs its real framebuffer/pick/selection-clear path using `--render-only`;
it never sends input/focus messages. Native and managed ABI mirrors, package/support metadata,
and caller routing must remain synchronized.

The repository package test `StormPackagesExecuteBoundedAovsFromCleanNativeAotFeed` is explicitly
enabled with `OPENUSD_STORM_AOV_EXECUTION_REQUIRED=1` on a Windows host with a working Storm GL
context. It rebuilds the standard six-package graph, publishes an isolated NativeAOT consumer,
and executes the million-pixel typed output and canonical-identity assertions without project
references or an external native-runtime path.
