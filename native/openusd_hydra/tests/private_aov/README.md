# Private Storm AOV executable candidate

This directory documents the historical ABI 8 private B7F113 proof.
The staged public ABI 9 implementation is described in `..\public_aov\README.md`.
The default-off statements below describe that historical graph, not ABI 9.

This is an opt-in, Windows WGL proof, not a public ABI or package capability.
`OPENUSD_HYDRA_ENABLE_PRIVATE_AOV` is **OFF** in the normal native build.
The candidate header and test hooks are not installed. Storm and child ABI 8,
the ordinary framebuffer API, and the existing pick API are unchanged.

The standalone CMake project builds only the affected shim and native probes.
Supply `OPENUSD_AOV_SDK_ROOT` and `OPENUSD_AOV_BASE_RUNTIME` pointing to an
explicit, matched OpenUSD 26.05 SDK and frozen runtime. Use an owned build and
install directory; never configure this against a shared install destination.
MSVC builds use `/W4 /WX /permissive-`; the separate C ABI probe uses C11.

## Proposed promotion boundary

The executable contract is `..\openusd_storm_aov_candidate.h`. Its three
operations are capture, get-view, and release. A future public contract would
drop `candidate_` from those function names only after ABI coordination.
Proposed promotion is Storm ABI 9 with exact managed/package mirrors. Child
ABI 8 need not change unless its own command/API surface changes. No version
or capability mirror is changed by this candidate.

The proposed managed seam is
`StormAovSnapshot OpenUsdStormRenderer.RenderAovs(StormAovRequest request)`: immutable,
detached managed buffers and identity records, copied in bulk under an internal
native owner/SafeHandle, never one P/Invoke per pixel. Managed/public/package
integration requires separate approval and is not implemented here.

## Data and statuses

Every record is bound to the same completed `openusd_storm_render_v2` call,
including dimensions, time, state/scene revisions, and the applied camera.
Output records preserve request order. Engine outputs always put `color`
first so the existing presentation is not switched to depth/ID visualization.
Color-only presentation selection is restored before publication.

| Requested output | Pinned Windows OpenGL result | Representation |
| --- | --- | --- |
| `color` | Ready after actual map | Native linear Float16 x 4 render color, not display framebuffer RGBA8 bytes |
| `depth` | Ready after actual map | Float32 OpenGL normalized window depth `[0,1]`, clear `1`; not linear view depth |
| `primId` | Ready after actual map | Native signed Int32, clear `-1` |
| `instanceId` | Ready after actual map | Native signed Int32, clear `-1` |
| `elementId` | Absent | The pinned Storm task-controller output route drops it despite a delegate default descriptor |
| `Neye` | Ready after actual map | Raw UNorm8 x 4 eye-normal AOV; not float precision or general signed XYZ normals |
| `normal` | Unsupported | No default native descriptor; no beauty-buffer relabeling |

GetRendererAovs is not an exhaustive capability list: this SDK does not list
instanceId or elementId among its candidate names. Readiness requires the
selected render buffer, exact format/dimensions, resolved texture descriptor,
and successful actual readback. Missing outputs have zero data extents and no
invented format. A failure publishes no owner; an invalid get-view clears it.
All copied rows are top-down with an explicit tight byte stride.

The depth fixture independently fixes near/far to `1/11`. Orthographic planes
at eye distances `3/7` must return `0.2/0.6`; perspective planes at `2/5` must
return `0.55/0.88`. A front-facing +Z plane produces raw Neye bytes
`0,0,255,255`; consumers must not infer an unproven signed-normal remapping.

## Identity and ownership

Identity output is optional and requires both ID buffers to be requested.
One fixed-capacity hash lookup indexes the native pairs; it is not an identity
hash exposed to consumers. The native integer `DecodeIntersection` seam runs
at most once per admitted unique pair, not once per pixel. Records retain the
canonical USD prim path, optional instancer path, instance index, and ordered
SDK instancer context. Paths are bounded, length-delimited UTF-8, not
NUL-terminated strings. Context ordering is preserved, not reinterpreted.

Hydra IDs and table indices are snapshot-local. They are **not** stable authored
IDs, and point-instancer array indices are not the authored `ids` primvar.
Background pixel indices are `UINT32_MAX`; a failed identity decode remains
an explicit unresolved record, never an unknown-to-zero mapping.

Capture validates the existing creation-thread/original-context guard before
engine operations, and again before copying. All native scene access uses the
existing stage access guard. Get-view and release are CPU-only and work after
renderer/stage teardown, on a different thread, without GL. A caller must not
release an owner concurrently with reads of its view. Only one unreleased
snapshot per renderer is admitted; process-wide renderer/job quotas remain
the caller's responsibility.

## Admission boundaries

Hard ceilings are 8 output slots, 4096 per dimension, 1,048,576 pixels, 4096
unique identity pairs, 16,384 context records, 1 MiB of path text, and 128 MiB
of admitted working storage. Every request supplies explicit limits; zero
identity/context/text limits are meaningful for all-background results.
Refusal is explicit, not truncation or an empty-success fallback.

Pixel/count/estimated-byte admission precedes rendering. Every actual render
buffer and backing texture descriptor is then inspected before any owned
copy allocation or Map. Layered, mipmapped, differently sized/formatted,
already mapped, or unresolved backing resources are refused. GPU completion
precedes Map; RAII Unmap covers both null mappings and exceptions after a real
Map. All data is copied before the temporary mapping ends.

Working admission includes owned record/buffer capacities, fixed lookup storage,
control allowance, and both old retained and new replacement HdSt CPU readback
allocations. HdSt keeps mapped storage after Unmap, so a conservative per-kind
high-water is retained for the renderer's lifetime even if changing render
outputs has already freed some buffers. `retained_scratch_upper_bound_bytes`
is intentionally an upper bound, not a measurement. A later smaller request
must still admit that conservative high-water or recreate the renderer.

This is a bounded **output-copy** contract, not a process RSS, GPU-memory, or
whole-scene render-memory guarantee. Allocator/driver bookkeeping and opaque
SDK identity-decoder internals are not claimed to be byte-bounded by this seam.
The SDK decoder's invocation count and the project's owned decoded text/context
storage are bounded separately. The closed data-query/PCP SDK guards are not
changed or reused to claim a bound on these renderer operations.

The private statistics and post-real-Map fault injection are proof hooks, not
part of the proposed public contract. The probes distinguish no-allocation
admission failures from cleanup after actual readback, cover the exact pixel
ceiling, and compare existing RGBA presentation and picking before/after capture.
The hidden windows are never shown, activated, focused, or given synthetic input.
The existing child probe also has an explicit `--render-only` mode: it runs its
real framebuffer, pick, selection, and exact selection-clear assertions, then
destroys the child before any navigation/focus/input-message tests. Its original
three-argument execution path remains unchanged.
