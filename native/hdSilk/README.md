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

## Architecture

- **`src/rendererPlugin.{h,cpp}`**: `HdSilkRendererPlugin`, registered with `HdRendererPluginRegistry` via
  `TF_REGISTRY_FUNCTION(TfType)`. Loaded on demand by the Plug system.
- **`src/renderDelegate.{h,cpp}`**: `HdSilkRenderDelegate` creates `HdSilkMesh` Rprims and `HdSilkRenderPass`,
  and owns the single `HdSilkSceneState` shared between them.
- **`src/mesh.{h,cpp}`**: `HdSilkMesh` (an `HdMesh` Rprim). `Sync()` pulls topology/points/transform from
  `HdSceneDelegate` based on `HdDirtyBits`, triangulates with `HdMeshUtil::ComputeTriangleIndices`, and upserts
  the result into `HdSilkSceneState`.
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

## Wire format

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
