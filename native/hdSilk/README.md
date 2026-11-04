# hdSilk

`hdSilk` is a real Hydra renderer plugin, `openusd_hdsilk`, that emits
versioned, native-owned command pages for the managed Silk.NET renderer.
Unlike a viewer that walks `UsdGeom` prims directly, `hdSilk` participates in
the standard Hydra render pipeline: it is discovered through the USD plugin
system as an `HdRendererPlugin`, constructs an `HdRenderDelegate` that Hydra
populates with `HdMesh` Rprims, and drives an `HdRenderPass` per frame. The
only extra step is that instead of drawing with a graphics API, it serializes
whatever changed into a byte buffer ("page") that a managed renderer can
consume without touching any native pointers.

Mesh synchronization uses a bounded per-sync index to share exact attribute, triangulation and
identity buffers with copy-on-write ownership. Attribute metadata and every payload byte must
match, including signed-zero bits. Each index permits at most 65,536 entries and eight candidates
per hash; reaching either bound leaves the original data intact. The lookup owners are cleared
after resource commit, on failed renders and on scene reset. Live mesh owners remain valid,
and subsequent edits detach rather than changing another mesh. Wire data and ABI versions are
unchanged.

## Architecture

- **`src/rendererPlugin.{h,cpp}`**: `HdSilkRendererPlugin`, registered with `HdRendererPluginRegistry` via
  `TF_REGISTRY_FUNCTION(TfType)`. Loaded on demand by the Plug system.
- **`src/renderDelegate.{h,cpp}`**: `HdSilkRenderDelegate` creates `HdSilkMesh` Rprims and `HdSilkRenderPass`,
  and owns the single `HdSilkSceneState` shared between them.
- **`src/mesh.{h,cpp}`**: `HdSilkMesh` (an `HdMesh` Rprim). `Sync()` pulls topology/points/transform from
  `HdSceneDelegate` based on `HdDirtyBits`, triangulates with `HdMeshUtil::ComputeTriangleIndices`, and upserts
  the result into `HdSilkSceneState`.
- **`src/nurbsSceneIndexPlugin.{h,cpp}`** and **`src/nurbsGeometry.{h,cpp}`**: evaluate rational NURBS curves
  and untrimmed patches from their control points, knots, orders, ranges and optional weights before hdSilk
  synchronizes the resulting linear curves and mesh.
- **`src/renderPass.{h,cpp}`**: `HdSilkRenderPass::_Execute` captures the `HdRenderPassState` world-to-view
  matrix, projection matrix, and viewport into `HdSilkSceneState`, and -- only while a light carries a
  non-default UsdLux link collection -- collects the prim and instance categories the link table resolves from.
- **`src/instanceLinking.{h,cpp}`**: The mapping from Hydra's per-level instance categories onto the composed
  instance identities hdSilk publishes. Hydra reports one category array per instance of each instancer, indexed
  by that instancer's own instance index; hdSilk publishes a nested instance under
  `parentIndex * innerInstanceCount + innerIndex`. This module walks the whole instancer chain, enumerates
  exactly the identities the chain publishes, unions each level's contribution onto them, and emits a membership
  row only where the result differs from the prototype path's own row. Rows come out in ascending composed order.
  `HdSilkAppendPathMemberships` is what the render pass calls per prim: it appends a path atomically, so a path
  whose rows cannot be collected in full leaves no row at all and falls open to every light rather than publishing
  a restrictive path row its own instances contradict. Nothing here is charged against the page ABI's entry
  budget -- a category set that differs is not yet a mask that differs, and which rows survive is decided in
  `HdSilkSceneState` against the page's own light and dome orderings. The only bound applied here is
  `HdSilkMaxCollectedInstanceRows`, a transient-memory policy far above the ABI bound.
- **`src/sceneState.{h,cpp}`**: `HdSilkSceneState`: thread-safe storage for the current frame state plus
  dirty/removed meshes, and `BuildPage()`, which serializes exactly the dirty upserts/removals plus the frame
  state into the wire format.
- **`src/api.cpp`**: The `openusd_hdsilk.h` C ABI: registers/loads the plugin, opens a stage, drives a
  `UsdImagingGLEngine` with `gpuEnabled=false`, and hands out pages.
- **`include/openusd_hdsilk.h`**: Public, versioned C ABI and wire format documentation.
- **`resources/plugInfo.json.in`**: Plugin metadata template, configured with the correct relative `LibraryPath`
  and installed to `plugin/usd/hdSilk/resources/plugInfo.json` under the shim install prefix.
- **`tests/hdsilk_probe.cpp`**: Native probe: verifies deterministic/guarded scene serialization, then creates a
  session against `test-assets/minimal.usda` and checks ABI v2 prim identity, zero instance fields,
  `primitiveParams` face mappings for triangulated and degenerate faces, topology revision changes, steady
  pages, removal/re-add, and session lifetime.

The render delegate / Rprim / render pass split, the `HdMesh` dirty-bit
handling in `Sync()`, and the plugin registration pattern are adapted from
Pixar's Apache-2.0-licensed OpenUSD examples
(`extras/imaging/examples/hdTiny`) and the `hdEmbree` plugin
(`pxr/imaging/plugin/hdEmbree`). All code in this directory is
project-owned and licensed under the repository's MIT license (see the
per-file header); only the *shape* of the Hydra integration follows those
upstream examples.

## Session creation handoff

`UsdImagingGLEngine` does not expose the `HdRenderDelegate` it constructs
internally, so `openusd_silk_session_create` cannot reach into the engine to
fetch the `HdSilkSceneState` it just created. The C ABI starts a thread-local,
unique capture token before engine construction. Only
`HdSilkRendererPlugin::CreateRenderDelegate` publishes its scene state to that
token in a mutex-protected registry, and the creating session takes the state
immediately afterward. Concurrent creation threads therefore cannot steal one
another's state, and direct/external render-delegate construction never
publishes into the active capture.

## Session lifetime

ABI v3 session handles are monotonically allocated opaque tokens, not object
addresses. A process registry maps active tokens to shared session state. Each
API lookup acquires that shared state before it can wait for the session mutex,
so checked destruction can close the token to new calls, wait for every
already-acquired operation, and only then tear down and erase the token. Failed
stage-access acquisition reopens the entry for retry. Destroyed/stale tokens are
rejected, token values are never reused, and the registry keeps no tombstones.

## Scene-ingestion requests

Authored `UsdRender` execution needs explicit scene-ingestion choices that do
not silently collapse to viewport defaults. The current public header describes
session ABI 7 and page ABI 35. Managed creation requires the matching session/page
version exports; older libraries cannot be mixed with these bindings.

The version-1 request is append-only and request-scoped: one struct carries the
standard purpose mask, one exact material-binding-purpose token, and the
existing complexity/draw-mode choices. The implementation applies those choices
at the `UsdImagingGLEngine` session seam, rejects masks that try to exclude the
always-inherited `default` purpose, and treats the pre-existing
`openusd_silk_session_sync*` overloads as an intentional return to the legacy
viewport defaults: purpose mask `default|proxy|render` (bit mask `7`) and the
historical empty material-binding token (`UsdShadeTokens->allPurpose`).
Explicit request-mode material binding is currently certified only for one
exact token, `full` or `preview`; empty/allPurpose and custom tokens are
rejected explicitly rather than falling back silently. Identical repeated
choices keep ordinary incremental synchronization. A changed explicit/legacy
choice reconstructs USD imaging from the current retained stage, keeping the
project-owned delta stream, revision and overrides. This transition can cost a
complete native population; it is not a per-frame cache reset. There is no
retired geometry cache, partial source fingerprint or bounds-based reconstruction.
Material networks use the existing full resolver, and excluded volumes are
filtered before field loading.

## Volume density

The `field:density` relationship selects one `UsdVolOpenVDBAsset`; its target
can be outside the volume, and its `fieldName` selects the grid inside the VDB.
Other field relationships do not add shading. The field's `filePath` and
`fieldName` and the volume's `hdsilk:density` and `hdsilk:sampleDensity`
attributes are read at the requested sync time. USD holds path and token samples
between their authored times.
Finite negative density clamps to zero, and non-finite density rejects the sync.
The defaults are density one and sampling enabled.

For a sampled field, hdSilk maps a unit-cube proxy to Hio's active-index range,
applies the grid's bounding-box matrix, and then applies the volume prim's
local-to-world transform. The target field prim's scene transform is not used.
The shader still integrates a field-local Z column; it does not trace the camera
ray through the grid.

An unavailable VDB, unreadable grid, invalid or degenerate bound, or a bound
whose composed transform is not finite uses the unit-cube uniform-density proxy
with a native warning. A later sync retries sampling. Disabling sampling uses
the same uniform proxy without a warning. A missing usable density field or file
path skips the volume with a warning; a non-finite volume transform rejects the
sync.

The animated-selection tests use unit-bounded copies of the separate density
and shifted-density grids, plus a two-grid file with the names `density` and
`shifted`. The original index-space grids remain available for bounds tests.
`generate_volume_fixtures.cpp` writes the unit and offset-affine copies from
those inputs and creates the single-voxel and sparse fixtures. OpenVDB assigns
a new archive identifier when it writes each file. The sparse fixture combines a
nonzero background, active voxels and an inactive stored value. Its backend gate
compares the decoded 9-by-9-by-9 field and rendered pixels with an independently
authored dense reference, then checks missing-file fallback, recovery and page
and resource release. This covers one float grid, not all OpenVDB grid types or
sparsity patterns. The other backend gates exercise held samples, changing
bounds, missing values, recovery and retained earlier pages.

Each sampled field is decoded into private storage owned by the native page and
its retained leases. Its texture asset begins with `hdsilk-volume:` and names the
resolved source VDB; it is not a path to the decoded bytes. The managed page
retains the field lease before acknowledging publication. Replay snapshots and
scene materials keep their own references, so later syncs and session disposal
cannot replace a field still in use. Disposing `SilkSceneState` releases its
retained fields. Applying raw command bytes or copying an isolated marked
material is refused because the bytes do not contain the field; synthetic
unmarked file-backed textures remain supported.

Private-storage failure emits a warning and uses uniform density until a later
sync can retry. A decoded field larger than 64 MiB is refused. This is a
per-field limit, not a limit on all fields held by pages, scenes or sessions.

An application can configure a shared `SilkVolumeFieldBudget` before a session's
first sync. The budget counts each private decoded field once across configured
sessions, pages, replay snapshots and scenes until its last owner releases it.
If another field would exceed the limit, the entire sync is refused without a
page; releasing retained fields allows the same session to retry. Disposing the
budget handle does not invalidate configured sessions or their field leases.
Unconfigured sessions retain their previous behavior.

The shared limit counts logical decoded-field bytes. It does not bound OpenVDB/Hio
allocations before admission, filesystem caching, allocator overhead, host memory
or GPU memory.

## Bounded direct lighting

Page ABI 24 admits all of a frame's effective direct lights up to 128, after
inherited visibility and zero-output filtering. A 129th effective light refuses
the whole sync with the observed count and limit; nothing is published, and
reducing the count allows the same session to retry. Authored-dark lights retain
an explicit frame flag so omitting them never invents a camera headlight.

The direct and shadow memberships are independent four-word, 128-bit masks,
resolved against the same path-sorted light ordering as the frame. The dome
table and mask remain separately bounded at eight. Shadows remain bounded at
four descriptors: distant lights use orthographic maps, and supported zero-radius
sphere spots use one perspective map. Unshaped point, area-light and dome shadows
remain unsupported.
The native frame is 25416 bytes, and each link entry is 44 bytes plus its UTF-8
path. Page ABI 28 appended a 2048-byte table of 16-byte entries after the dome
block: the `UsdLuxShapingAPI` cone angle and focus of each direct light, with two
reserved zero words. Rect and disk lights publish cone and focus; zero-radius
sphere lights also publish them when the cone is below 180 degrees. Other types,
finite-radius spheres and slots past the light count carry 180 and zero.
The managed/GPU frame is 17344 bytes at the existing raw-buffer binding;
the 240-byte per-draw surface block carries two integer `uint4` masks, without
floating-point conversion or per-element native calls.
Page ABI 29 adds texture wrap value 5 for MaterialX `constant` addressing, which
page 28 published as black. Table layouts are unchanged; the managed cache pads
the image with a ring of the authored `default`, and the surface block's
reserved lanes after the dome mask carry the per-axis constant-border masks.
Page ABI 30 appends a second UV transform to `MATERIAL_UPSERT`: a `u32` mask of
the parameters that sample through it, then six floats in the `uv_transform`
layout. A transformed base colour beside an untransformed normal map therefore
keeps both images; a third distinct transform is still dropped with a `TF_WARN`.
The serializer skips a material whose mask names displacement, the detail normal,
or a parameter with no surface texture. The surface block grows to 304 bytes.

Requested filters commit only when a page is successfully returned. Failure
during engine reconstruction leaves the stage and owned scene state available
for the next explicit or legacy sync to retry. Test-only failure hooks are
excluded from production builds. The C probe covers reconstruction, render and
page-publication failures, while managed pixel cases cover hidden primvars,
normals, topology, ancestor transforms, deletion/redefinition and instancing.

## Linear basis curves

hdSilk emits linear segmented, nonperiodic, periodic and pinned curves as
`LINE_LIST` geometry. Nonperiodic and pinned curves connect consecutive
control points; periodic curves also connect the last point to the first.
Each curve in a periodic batch closes independently. Indexed topologies
select both positions and vertex widths through the resolved point index.
Invisible curves and points do not renumber the remaining segment identities.

Nonperiodic and pinned curves require at least two control points, and
periodic curves require at least three. Invalid topology removes the retained
curve and emits a native warning; a later valid edit republishes it. Direct
cubic Bezier `BasisCurves` are sampled as fixed line chords; NURBS curves are
evaluated into linear curves by the scene-index adapter below. The optional
bounded preparation profile admits the four supported linear wraps and the
supported cubic Bezier topologies.

An unresolved or wrongly typed points value removes the retained curve in an
unbounded session and emits a native warning. A bounded session refuses the
sync instead, leaving the previous record intact until a valid edit can be
retried. A typed empty array removes the curve in either mode.

Constant widths remain one attribute value; uniform, varying and vertex
widths are expanded onto the emitted line vertices. The renderer does not
widen the one-pixel lines from those values.
The native topology probe covers indices, visibility and width expansion;
the managed conformance cases compare animated curves with independent
segmented geometry on D3D12 WARP and Vulkan SwiftShader.

## Rational NURBS geometry

The scene-index adapter evaluates authored NURBS curves and untrimmed patches
before hdSilk synchronizes them. It uses control points, order, knot vectors,
and authored parameter ranges; optional `pointWeights` supply the rational
weights, with unit weights used when they are absent. Open, closed and periodic
patch forms are evaluated the same way. USD defines a closed or periodic form as
closure that the authored control grid and knots already express, so the form
token adds no evaluation rule. hdSilk does not weld the seam: the first and last
sample rows coincide in position and normal but remain separate mesh points.
Patch control points follow USD's row-major layout, with U as rows and V as
columns. OpenUSD's USD Imaging scene-index adapter maps the built-in USD
`pointWeights` attribute into `HdPrimvarsSchema`; hdSilk reads that mapped value
rather than looking for a `primvars:pointWeights` USD attribute.
Each nonzero knot span is sampled at eight intervals. Patch samples form a quad
mesh with normals calculated from the rational surface's U and V derivatives.
Where one partial derivative vanishes along a collapsed edge on the sampled
range boundary, such as the pole of a revolved surface, hdSilk uses the limit
of the normal taken from the patch interior. It calculates that limit from the
rational mixed derivative. hdSilk refuses the patch when both partials vanish,
when the partials are parallel but nonzero, when a collapsed line lies inside
the sampled range, or when the limit itself vanishes. In each of those cases
the surface has no unique normal at that point.
This fixed tessellation does not adapt to curvature or screen-space error. A
single NURBS prim may emit at most 1,048,576
tessellated points; this output limit does not bound source storage or total
process memory. hdSilk also limits estimated evaluation work for each NURBS
prim to 67,108,864 units. Each curve sample costs `order^2 + order` units;
each patch sample costs `2 * uOrder^2 + 2 * vOrder^2 + 4 * uOrder * vOrder`
units. The squared terms bound basis and derivative recurrence work; the
product term bounds rational point, first-derivative and mixed-derivative
accumulation.
Checked arithmetic rejects overflow and work above the limit before tessellated
output is allocated. The evaluator warns and leaves no generated output for a
refused sample; a later scene edit can retry.

A patch with populated trim-curve data is skipped with
a native warning. hdSilk does not clip the surface to its trims or substitute
the control cage. Invalid knots, ranges, weights, or points also skip the
generated geometry; a later scene edit can retry it. Weights must be finite,
and their nonzero values must not mix signs within a curve or patch. A sample
with a singular rational denominator is rejected.

Generated geometry keeps constant source primvars and evaluates up to eight
authored vertex primvars through the same rational basis as the points. A
retained vertex primvar holds one to four float or double components per
control point and is published as float data with vertex interpolation, one
value per generated sample. Each retained primvar adds its samples to the
evaluation-work charge, so a prim near the work limit can be refused once
attributes are added. Curve vertex primvars are expanded onto each emitted
line endpoint, including their texture-coordinate, color or custom semantic;
bounded preparation also reserves their source and expanded storage.
hdSilk warns and discards a vertex primvar beyond the eighth, and every
uniform, varying or faceVarying primvar, because generated
spans and faces have no authored correspondence for those interpolations.
Authored normals are replaced: patches publish evaluated surface normals and
curves publish none. hdSilk omits `ptexFaceOffset` because generated patch
faces have no authored face IDs.

NURBS prims carrying `skel:jointIndices` or `skel:jointWeights` are skipped with
a warning. OpenUSD's skinning scene index resolves only meshes, points and basis
curves, so no skinned control points reach the evaluator. Supporting them
requires deforming the control net before tessellation, which this release
does not do.

Generated patch faces and points have no authored USD subprim identity, so
hdSilk reports that identity as unsupported rather than publishing generated
indices as authored picks. The optional bounded mesh-preparation profile admits
generated NURBS geometry from a topology-only plan. The adapter publishes the
generated point, element and run counts without evaluating points, so hdSilk
reserves the budget before any authored or generated point is read. A refused
reservation leaves no mesh published; a later sync under a larger limit
evaluates and publishes the geometry. Trimmed patches stay refused in both
bounded and unbounded sessions. Generated-geometry recognition requires the
adapter's in-process cache provenance, not an authored boolean or primvar name.
Missing or altered plan metadata and deliberately refused topology stop bounded
preparation with distinct diagnostics; neither becomes a zero-byte reservation.

## Preview Surface connections

A Preview Surface input connected to a known unsupported node stays at the
renderer default and emits a native warning naming the material, input and
node. An authored constant behind that connection is not treated as an
unconnected value.

Page ABI 33 retains direct `HwPtexTexture_1` connections as Ptex sources,
with their asset path and output channel. The renderer can sample supported
surface inputs using the mesh's Ptex mapping. A retained input with an unusable
mapping or source format uses its published fallback and reports
`OPENUSD_SILK_TEXTURE_PTEX_UNSUPPORTED`; admission failures propagate. Ptex
normal, detail-normal and opacity inputs are not sampled. Native translation
rejects Ptex displacement with a warning and leaves the material undisplaced.
Connections to the Ptex node's own inputs and unsupported parameter overrides
are refused with a native warning. A `UsdUVTexture` pointing to the same file
remains an ordinary image source. Backend pixel and lifecycle qualification
of Ptex sampling remains pending.

The subdivision module can derive default Ptex face IDs and local triangle
coordinates for refined Catmull-Clark, bilinear and Loop meshes, and for
unrefined Catmull-Clark or bilinear quads and Loop triangles. The unsplit
mapping refuses visible non-quad Catmull-Clark or bilinear faces: each has
one Ptex face per corner, not one per authored face.

A separate caller-buffered helper plans and emits triangulated Ptex subfaces
for unrefined Catmull-Clark and bilinear control cages. It copies base points
through the highest referenced index, adds edge midpoints and face centres,
and retains each triangle's authored face identity. Holes contribute to later
Ptex face IDs but produce no subfaces or added points. The caller admits the
planned point, index, face, coordinate and scratch buffers before writing.
The writer refuses overlapping or undersized buffers, repeated face vertices
and non-finite copied points; its per-face validation does not establish full
mesh connectivity. Callers must discard all output and scratch contents after
a failure. Face-varying primvars, authored Ptex face remapping and polygonal
`none` meshes are not handled.

A separate helper applies constant or uniform signed-integer `ptexFaceOffset`
values, including indexed values, to default face IDs using each triangle's
authored source face. Its primvar adapter reads integer scalars or arrays from
`VtValue` without copying and distinguishes an indexed input even when its
indices array is empty. The caller must confirm an `elementSize` of one:
larger indexed elements group multiple integers, so treating each integer as
an offset would select the wrong value. An unindexed uniform scalar requires
exactly one authored face. The helper checks every value index and verifies
each final ID against the provided texture face count before writing a separate
caller-owned buffer. Failed calls leave that output and the original mappings
unchanged for retry. Instance interpolation, other numeric types and arbitrary
face-index remapping remain unsupported.

A scene-index adapter reads the interpolation, raw indexed value, indices and
element size from `HdPrimvarSchema` at the requested shutter offset. It accepts
missing element-size metadata only when the caller identifies a producer that
guarantees the default of one. The tested `UsdImagingStageSceneIndex` omits
this metadata for its default; arbitrary scene-index producers cannot be assumed
to do the same. The unindexed path requires exactly one scalar per logical
element, so it refuses grouped values whose metadata was removed. Indexed
primvars require explicit element-size metadata because a filter can leave
grouped values and indices intact after removing a non-default size. Invalid
metadata or incomplete indexed inputs are refused before the output is changed.
The native probe covers time samples, scene edits and retry as well as malformed
retained data sources.

A test-only observation during USD imaging sync compares each Rprim with its
terminal scene-index mesh and the delegate's raw offset values and indices.
The tested ordinary mesh, propagated native-instance prototype and point-instancer
prototype agree at both authored times. An indexed default-one primvar also
agrees as its values and indices change. Instance Rprim IDs are generated Hydra
paths; these observations do not establish the provenance of every producer.

A filtering scene index can remove an authored element size of two while leaving
the Rprim, delegate, values and indices matching. Four unindexed values for two
faces fail the scalar-cardinality check, and indexed offsets without explicit
element-size metadata are refused. Neither input can be treated as scalar
offsets. A separate legacy-delegate control also lacks this metadata. Both
controls invoke Rprim sync directly rather than running Hydra's full sync loop;
a renderer caller must establish the producer's guarantee independently for
unindexed default-size values.

`src/ptexFaceDecoder.cpp` reads selected full-resolution faces into one
contiguous pixel array. It validates face IDs, records each face's dimensions
and adjacency, and checks the requested dimension and total output-byte limits
before allocating the pixel output or decoding. Requests are limited to 65,536
face IDs; duplicate IDs retain separate output spans in request order. Invalid
metadata, an unreadable file or a limit violation rejects the whole result.

`openusd_silk_ptex_decode_faces` exposes the decoder through a session-free,
versioned C ABI. The caller initializes the version-1 info packet, supplies face
IDs and output limits, then receives one face-layout record per requested ID
and one packed pixel buffer. Each layout record carries dimensions, row stride,
flags, adjacency and its offset into that buffer. A sizing request with empty
output buffers reports the required face count and pixel byte extent in the
info packet. If either output buffer is undersized, the call returns
`BUFFER_TOO_SMALL` and leaves both data buffers unchanged. Successful calls
preserve request order and duplicates.

`openusd_silk_ptex_describe_faces` exposes the same face layouts without
requesting decoded face pixels. It accepts the same face IDs and output limits;
each reported offset refers to the packed pixel buffer that the corresponding
decode would produce. A short layout buffer returns `BUFFER_TOO_SMALL`, reports
the required counts in the info packet and leaves the layout buffer unchanged.
An empty selection returns texture metadata without a layout buffer.

The path-based calls open the asset independently. Matching layouts do not
prove that its pixel values stayed unchanged between discovery and decode.
The additive `openusd_silk_ptex_describe_faces_from_memory` and
`openusd_silk_ptex_decode_faces_from_memory` functions instead read caller-owned
encoded bytes. The caller keeps those bytes valid and unchanged until each call
returns and can reuse one snapshot for discovery, sizing and decode. Neither
function retains the pointer. The face limit applies to each request, so the
caller must also bound the combined sampled and neighbor IDs before its final
decode.

The decoded-byte limit covers only the returned pixel array. It does not bound
the encoded source, Ptex cache or decoder allocations, allocator overhead or
later GPU storage. The cache's eviction setting is not a hard limit on active
Ptex allocations. The path-based exports pass paths to Ptex unchanged, so
non-ASCII Windows path handling for those exports remains unqualified. The C
ABI also serves the renderer's mapped face-atlas construction. Backend pixel,
boundary-filtering and rendered lifecycle, refusal, retry and release coverage
remain open.

A separate Storm-only reference renders four Ptex faces with authored offsets
and compares them with reordered topology and indexed offsets. Individual-face
and missing-texture controls demonstrate that the reference responds to those
inputs. A second reference uses four isolated quad faces and constant per-face
colours. It checks authored Catmull-Clark and bilinear schemes at Storm's default
complexity: offsets match reordered topology, and an authored uniform
`ptexFaceIndex` leaves both the baseline and offset renders unchanged. The Loop
fixture gives the same face-index observation. These references do not establish
face-index behavior for other producers or shaders, refined corner mapping, or
filtering across Ptex face boundaries. The generator in
`test-assets/native-profile/ptex/generate_quad_fixture.cpp` creates the quad
Ptex file. Page ABI 32 publishes the mapping status and, when mapping succeeds,
one Ptex face ID and three corner coordinates per emitted triangle. The renderer
uses those mappings for supported Ptex surface inputs.

## Wire format

Named `UsdPrimvarReader_float2` fallbacks and MaterialX `geompropvalue_vector2` defaults
are projected as constant texture-coordinate attributes only when the bound mesh lacks
the named authored attribute. No source primvar is authored or changed. Values remain
before the material's folded UV transform. Material-only default edits dirty the complete
prototype family; lightweight references remain payload-free. Synthesis participates in
page byte admission, per-path rollback and acknowledgement without changing the ABI.
`hdsilk_uv_fallback` covers defaults, malformed/connected values, authored-coordinate
precedence, material-only edits, instance families, exact limits and retry.

Meshes with published normals can share triangle corners whose original point identity
and every resolved vertex attribute agree bit-for-bit. `src/meshVertexLayout.h` builds a
deterministic indexed layout and compacts its attributes without changing triangle order,
face/edge identity, UV seams or normal discontinuities. Layout-changing primvar edits
advance the topology revision even when the mesh remains corner-remapped.
Page ABI 34 carries normal correspondence after the Ptex mapping block, independently
of optional picking identity. The block contains three counts, source vertex identities,
polygon-corner adjacency and any normal-only neighbor positions. Each adjacency triple
names the previous, current and next point around a polygon. Smooth coarse meshes use
authored polygon corners; subdivision uses final-level polygon corners, so a fan diagonal
does not add another face contribution. Coarse smooth adjacency includes hole faces,
matching the pinned Storm reference, without publishing triangles for those faces.
Normal-only positions retain neighbors omitted by a face-varying emitted layout.
Page ABI 35 marks triangle-local fallback in the high bit of the normal identity count;
the remaining bits carry the number of identities. The mode remains present when the
mesh publishes authored normals, which lets a consumer recompute the fallback after
simulated points replace those normals.

The managed fallback sums each corner's cross product and shares that sum only between
copies with the same source identity and exactly equal current positions. Divergent
simulated copies separate; coincident unrelated source vertices remain separate. A
triangle copy that separates from its representative polygon corner gets its local
triangle normal. Authored normals still take precedence. For polygon and bilinear meshes
without them, the renderer derives a vertex per triangle corner without changing retained
points or simulation topology. Simulation, instance
references and restoration share the immutable correspondence arrays; changing the
correspondence changes retained geometry identity even when point bytes stay unchanged.
Malformed counts, indices and nonfinite normal-only positions are refused before scene
publication. The native mesh reservation includes the retained tables and lookup storage.

The Rprim borrows its emitted triangle and face-identity tables from the control cage or
the active refiner instead of retaining a second copy. Attribute collections, triangle
indices and authored face/point/edge identity use native copy-on-write arrays: producer
caches and published records share storage, while edits and presentation changes detach
only the arrays they write. Read-only traversal, including serialization, must use const
views so it neither changes earlier records nor duplicates their payloads.
Sharing these buffers does not add another wire representation.

### Optional mesh preparation admission

`openusd_silk_session_sync_with_mesh_preparation` is an additive extension with versioned
limits packets; older libraries explicitly refuse unavailable versions. The existing
session-6/page-24 calls retain their unbounded behavior. The three-argument managed
`SilkSceneIngestionOptions` overload requests version 1 with a positive
`maximumMeshPreparationReservationBytes`, and successful pages expose `MeshPreparationUsage`.

The profile admits coarse, smooth-shaded mesh Rprims with resolved numeric points
and the existing supported numeric attributes. It also admits point Rprims and linear
basis curves with segmented, nonperiodic, periodic or pinned wraps. Meshes, points and curves
require resolved float3 or double3 points. The profile refuses refinement, other draw
modes, Skel/computed geometry, cubic or unsupported curve topologies and volumes,
rather than silently ignoring their cost. Point-instancer references share the
prototype reservation; their transform/identity metadata is not a numeric buffer
reservation.

The metric is **conservative logical buffer reservation bytes**, not actual allocation,
private memory or RSS. For `P` source points, `T` upper-bound triangles (sum of `max(faceSize-2,0)`),
`C=3T` and `V=max(P,C)`, the base charge is
`12(P+V) + 4(9C+2T+P) + P + 4(V+6C+4P)`.
Each supported attribute adds `4(S+3E)`, where `S` is its source float-component count and
`E` is `S` for a constant, otherwise `V * componentCount`. This reserves conversion/emission,
triangulation, cached and published normal correspondence, remapping/identity flags and
overlapping attribute preparations before hdSilk builds those arrays. It intentionally
does not discount holes, seams or compaction.
The charge remains reserved for that mesh generation even after its temporary arrays shrink.

For a supported linear curve, the charge is `12P + 4*max(W,1) + 76S`, where `P` is
the number of source points, `W` is the number of source width values and `S` is the
upper-bound segment count from the authored topology. The charge covers point and width
conversion, emitted positions and normals, indices, overlapping emitted widths and segment
identities. It counts the complete float, double or half width array even if its shape is
later rejected, and does not discount invisible curves or points.

For a point Rprim, the charge is `48P` bytes for `P` source points. It covers point
conversion, emitted positions and normals, indices, primitive identities and point
origins, including when the separate identity budget refuses the origin table. Empty
point arrays cost zero; counts beyond the 32-bit index range are refused. The existing
renderer draws one-pixel points and does not use authored widths.

An unresolved or wrongly typed points value removes all published instances of the
point Rprim in an unbounded session and emits a native warning. A bounded session
refuses the sync and retains the previous record; a typed empty array removes the
points in either mode. A later valid edit republishes the points.

For mesh Rprims, hdSilk selects points from a valid UsdSkel deformation, then a
computed primvar, then authored points. Topology changes and loss of a previous
deformation refresh the fallback; a normals-only edit without a deformation
transition retains the current points. If every source is unresolved or wrongly
typed, an unbounded session warns and removes the mesh and its instances. A
bounded session refuses the sync before replacing the previous record, and does
not admit Skel or computed primvars. A typed empty result removes the mesh in
either mode. A nonempty array that does not cover the emitted triangles removes
the mesh in an unbounded session; bounded preparation refuses it before
replacing the previous record. If face counts are invalid or require more
indices than supplied, an unbounded sync warns and removes the mesh and its
instances before triangulation. Bounded preparation refuses that topology when
points are nonempty; a typed empty array still removes the mesh in either mode.
The invalid topology remains excluded from attribute and subdivision refresh
until valid topology returns. In the unrefined path, indices in skipped
holes, degenerate faces and trailing topology data do not invalidate emitted
triangles. An active subdivision refiner requires its complete control-point
array, including points used only by hole faces. For point-source removal,
refinement retains its topology, so a valid edit restores the refined mesh.
Supported attribute edits made while points are absent are applied when the
geometry returns. Unreferenced trailing points remain valid for unexpanded
meshes. When an unexpanded point count changes, the pick-topology revision
advances and vertex attributes are checked against the new count.

A producer and its retained record share one reservation lease. A replacement reserves
new work while the old lease remains live; failed partial preparation retains both until
recovery destroys that owner. Concurrent Hydra workers cannot overcommit the ceiling.
Worker refusal is latched and propagated after the Hydra worker barrier, not thrown
through the worker pool. No page is published; the existing repopulation path supplies
the complete retry. Changing the mesh limit or switching to/from legacy mode rebuilds imaging.
If managed copying fails, publication remains unacknowledged and the native page is
released without consuming its pending changes.

Source/SDK materialization and caches, allocator capacity/overhead, maps/strings, materials,
instance metadata, serialized pages, managed/GPU buffers and texture residency are outside
this metric. Keep an independent host-memory safety guard; this profile is not complete
whole-scene resource admission.

The four-argument managed overload additionally takes `maximumCommandPageBytes`, a
positive `int`, and requests preparation limits version 2. Native callers pass the
24-byte `openusd_silk_mesh_preparation_page_limits` packet through its `base` prefix.
The usage output remains version 1 and 32 bytes; session/page ABI versions do not change.
A preparation-v1 library rejects the newer packet instead of ignoring its page ceiling.

`src/pageWriter.h` writes all commands directly into one amortized byte buffer, with
no separate per-command payload vectors or payload-to-page copies. Every growth request
is checked against the page ceiling before allocation, including command headers and
the always-present frame. Incomplete commands roll back; a ceiling refusal aborts the
whole publication. Managed copying checks the native length against the same limit
before allocating, and successful pages expose their actual `ByteLength`.
Changing only the page ceiling does not invalidate unchanged geometry.

This is a **per-page serialized-byte ceiling**, not a total-memory reservation or a
global live-page pool. Vector growth can temporarily overlap old and new buffers;
native and managed buffers overlap during copying. Allocator overhead, metadata,
other live pages, retained geometry, source/SDK caches, materials, textures and GPU
storage remain separate costs. The mesh reservation metric above is unchanged.

`openusd_silk_session_set_preparation_limits` optionally installs immutable version-2
ceilings before the first sync. All native sync entry points apply the effective minimum
of session and per-request limits. Unbounded legacy requests therefore remain bounded
when the host configured a session policy, without changing their purposes or allPurpose
material binding. Configuration after sync starts, repeated configuration, malformed
packets and zero ceilings fail explicitly. An unconfigured session retains prior behavior.
`openusd_silk_page_get_preparation_usage` reads the successful page's immutable version-1
snapshot, even after subsequent syncs or session destruction; unbounded pages have zero
counters. The managed `SilkPreparationLimits` factory overloads fail on older libraries
and release the newly created native session rather than silently omitting admission.

Command-page allocation failures abort the whole publication instead of being treated
as malformed scene records. Environment acknowledgements, dirty flags and removals
advance only after a complete page is built, so a retry still publishes every pending
change. Invalid authored records retain their existing diagnostic behavior. This is
failure atomicity, not a total-process memory limit or pre-allocation admission policy.

### Opt-in file-backed command storage

`HdSilkSceneState::BuildPageTo` shares the contiguous page serializer and publication
journal, but feeds complete commands into a private store. Its update-byte ceiling
and reusable command-buffer ceiling are independent. Triangle preflight checks both
before large payload copies. Per-path rollback can truncate already flushed commands;
write, truncate and flush failures abort the whole update rather than masquerading as
malformed scene records. Final flush precedes revision/output mutation and deferred
acknowledgement. No wire layout or existing C entry point changes.

`src/commandFile.h` implements bounded buffered storage and bounded random-access reads.
Windows uses create-new, non-shared, delete-on-close temporary files. POSIX uses exclusive
mode-0600 files with atomic close-on-exec, unlinked before the first write. Finalized
storage is immutable. The native publication probe covers byte-identical file/contiguous output, allocation and
partial-write failures, both sides of final flush, rollback, exact ceilings and replay.
A counting-store probe serializes more than 4 GiB with one 64-KiB command buffer; it
does **not** write a multi-gigabyte file or qualify warehouse geometry.

The additive version-1 storage extension exposes `openusd_silk_session_set_command_storage_limits`,
`openusd_silk_session_sync_to_storage`, `openusd_silk_page_read_storage` and
`openusd_silk_session_get_command_storage_usage`. Configure it once after preparation
ceilings and acknowledgement, before synchronization. The pool charges all live files
from that session, including acknowledged pages, and returns credit on truncation or
final release. Files survive session destruction. Contiguous sync entry points keep
their original limits; the explicitly opted-in stored path applies the page ceiling
to each command and uses the separate update ceiling for its full extent.

Managed `SilkPreparationLimits.CommandStorage` selects this transport through the
existing Viewer/MCP capture path. The renderer uses bounded bulk reads and a reusable
command buffer for both preflight and transactional application. Failed CPU/GPU preparation
retains the exact file for ordered replay; successful publication or renderer disposal
releases that reference. Public enumerators instead copy each command into managed
storage to preserve existing span lifetimes across advancement and page disposal.
Enumerator copies retain their shared file reader until collection; renderer-owned
enumeration releases it deterministically. `SerializedByteLength` reports the full
64-bit extent; `ByteLength` throws on stored updates exceeding Int32.

File buffering, allocator/reallocation overhead, filesystem cache, retained geometry
and GPU resources remain separate from serialized-byte limits. Only Windows file
storage has local execution evidence. Small-fixture captures match color and depth
on software backends and RTX 5070 D3D12/Vulkan. A guarded no-MDL/no-GPU warehouse run
successfully read and hashed 4,743,378,157 command bytes. Separate bounded exterior
diagnostics now render the complete expected mesh-record set on both backends after
closing native imaging. They deliberately disable MDL and do not qualify material fidelity
or the live Viewer/MCP journey; see `docs/simready-warehouse.md` for limits and scope.

### Source identity batches

The optional version-1 `openusd_silk_session_resolve_source_identities` extension resolves
retained `(Hydra path, instance index)` keys to authored source prim paths and ordered
point-instancer contexts. It requires the latest successful page revision and the same
source-stage change serial. Native USD instances resolve to their instance-proxy paths,
not `/UsdNiPropagatedPrototypes/...`. The terminal render keys and wire ABI remain unchanged.

Requests contain at most 1,024 keys and admit at most 8 MiB of serialized results. Results
preserve input order and are copied only after the complete batch succeeds. Unknown keys,
stale revisions, source edits and byte-budget failures return no partial results. A short
caller buffer reports the exact required size without modifying the buffer.

USD pick ordinals are tracked separately from stable authored instance indices. Each level
uses its position in the masked, per-prototype Hydra index array; nested ordinals compose
with that array's size. Reusing the stable wire index would resolve sparse/multi-prototype
instances to the wrong source slot. Ordinary, nested native and masked point-instance
fixtures exercise this distinction, together with exact quotas, invalid keys and edit/retry.
The managed entry point is `OpenUsdSilkSession.ResolveSourceIdentities`; missing native
support throws explicitly. Returned selections name source objects, not GPU cache keys.

### Native copy acknowledgement

The managed runtime requires `openusd_silk_session_enable_page_acknowledgement` and
`openusd_silk_page_acknowledge`. It enables the mode before the first sync and acknowledges
only after page metadata, copy length and construction of the immutable managed owner
all succeed. Older libraries fail creation explicitly, even when session 6/page 24 match.
The native ABI and wire versions remain unchanged; C callers that do not enable this
additive mode keep historical immediate-publication behavior.

An enabled session has at most one unacknowledged native page. Another sync or explicit
repopulation is refused while that page is outstanding. Acknowledgement commits the
staged dirty flags, removals, environment snapshots, light links and shadows without
allocation. Releasing the page without acknowledgement discards only its prepared
acknowledgement metadata, leaving every change pending. The next request rebuilds its
own current frame and can observe newer scene edits; it is not handed stale request bytes.
Issued page revisions still advance on each successfully built native page.

Prepared metadata borrows native dirty entries, so scene mutation is blocked until the
page is resolved. Session teardown rejects any outstanding publication before destroying
imaging, and owned page bytes/usage remain readable independently. Releasing an older
acknowledged page cannot cancel a newer pending page. There is no second retained serialized
byte buffer. This is in-memory transfer safety, not crash-durable recovery or a memory budget.
After copying succeeds, later GPU preparation failure still uses ordered managed-page replay.

`openusd_silk_session_sync` returns an `openusd_silk_page_view` whose `data`
buffer is a sequence of little-endian commands. Every command starts with a
`uint32 type` and a `uint32 byte_size` (the 8-byte header plus all payload
bytes that follow). No native pointers ever appear in the buffer; prim paths
are length-prefixed UTF-8 byte sequences.

* `FRAME` (`type = 1`): `int32 width`, `int32 height`, `double
  view_matrix[16]` (row-major), `double projection_matrix[16]` (row-major).
  Always present, once, in every page.
* `MESH_UPSERT` (`type = 2`, page ABI v2) has a 200-byte fixed prefix including
  the common header: stable path hash at offset 8 (`uint64`), explicit Hydra
  prim ID at 16 (`int32`), reserved-zero instance ID/index at 20/24 (`int32`),
  triangle-list topology kind at 28 (`uint32`), topology revision at 32
  (`uint64`), path/point/index/triangle counts at 40/44/48/52 (`uint32`),
  display color at 56, and the row-major transform at 72. Offset 200 starts the
  UTF-8 path, followed by `point_count * 3` little-endian floats,
  `index_count` little-endian `uint32` triangle indices, and exactly
  `triangle_count` authored USD face indices decoded from `primitiveParams`.
  `index_count == triangle_count * 3`. Path is authoritative; the FNV-1a hash
  is only a collision-checked index. Topology revision changes only for dirty
  topology, so property updates retain picking ranges and steady frames emit
  no mesh command.
* `MESH_REMOVE` (`type = 3`): `uint64 stable_id_hash`, `uint32
  path_byte_count`, followed by the path bytes. Emitted when an Rprim is
  destroyed.

Commands are serialized in deterministic path-byte order. Counts, products,
command sizes, vertex references, topology mappings, and revision overflow are
checked before pending state is consumed. See `include/openusd_hdsilk.h` for
the full, versioned declaration
(`OPENUSD_SILK_PAGE_ABI_VERSION`, `OPENUSD_SILK_COMMAND_*`).

Destroying and recreating the same authoritative path before `BuildPage()`
coalesces to one `MESH_UPSERT`: the queued remove is cancelled and the new
record may carry a different Hydra prim ID and a topology revision reset to 1.
Consumers therefore treat that same-path/same-hash identity change as implicit
recreation rather than stream corruption.

## Building and testing

`openusd_hdsilk` builds as part of the normal native workflow:

```pwsh
pwsh eng/build-native.ps1 -Rid win-x64
```

This configures/builds the `win-x64` CMake preset (which now includes this
directory via `add_subdirectory(hdSilk)` in `native/CMakeLists.txt`) and
installs `openusd_hdsilk`, its header, and its `plugInfo.json` into
`native/install/shim/win-x64`.

The native probe (`hdsilk_probe`, registered as the `hdsilk_probe` CTest)
assembles its own runtime plugin directory by merging the installed OpenUSD
plugin registry (`<OpenUSD install>/lib/usd` + `plugin/usd`) with hdSilk's
own `plugInfo.json` and built shared library, then runs against
`test-assets/minimal.usda`:

```pwsh
cd native/build/shim/win-x64
ctest -R hdsilk_probe --no-tests=error --output-on-failure
```

`--no-tests=error` is not optional decoration: CTest exits 0 when a `-R`
expression selects nothing, so without it a renamed or unconfigured probe
reports success having run no test at all.
