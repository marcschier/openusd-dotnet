# SimReady Warehouse-01

This integration targets the complete published warehouse in the OpenUSD Viewer and MCP,
without a mandatory Isaac Sim/Kit runtime. The pinned snapshot has been acquired and
verified, and the complete collider world plus a removable moving-box scenario have CPU
execution and source-restoration evidence. Lossless storage of the measured material classes,
the optional authored camera, and bounded Viewer/MCP recording fixtures are qualified.
Full-scene rendered material fidelity and the warehouse simulation presentation are not yet
accepted.

## Current qualification boundary

The following records the verified state as of 2026-10-13. Historical failures later in this
document remain useful diagnostics, but do not supersede these narrower successful results.

- **Geometry identity.** Verified: all 27,389 original placements and world transforms
  reconcile; the renderer retains 6,838 shared GPU geometries. Remaining boundary: no
  completed full-root image with the latest material implementation.
- **Material storage.** Verified: actual 4K rubber, concrete and three lamp classes retain
  their maps and replay exactly; four referenced book owners qualify twelve UDIM material
  paths. Remaining boundary: storage and bounded-fixture pixels do not prove full-scene
  shading or lighting fidelity.
- **Physics.** Verified: complete static/dynamic collider worlds, one removable moving box,
  240 fixed steps, reset and unchanged source data. Remaining boundary: full-root rendered
  recording through each real host.
- **Camera and workspace.** Verified: optional `/WarehouseDemoCamera` imports with the
  intended pose and optics; explicit MCP root settings preserve units, axis and timeline
  through rollback/export. Remaining boundary: the camera-only population-mask check does
  not load or render warehouse geometry.
- **Host and package integration.** Verified: current MCP and Viewer
  recording/cancellation/recovery/gallery fixtures pass; the Windows clean-feed Imaging
  NativeAOT consumer executes using public APIs. Remaining boundary: these fixtures and
  package probes are not full-warehouse or additional-platform acceptance.

The reconciled material inventory contains 2,941 material prims, including 472 physics-only
materials, with no unexplained oversized actual storage class for the recorded source and
selected representations. The largest reconciled material requires **492,229,969 bytes**.
Adding the previously measured **24,993,796-byte** frame overhead gives **517,223,765 bytes**,
below the unchanged **536,870,912-byte (512-MiB)** GPU texture pool. New shared allocations
still require measurement; this arithmetic is not total VRAM or process-memory accounting.
See [lossless texture representations](rendering.md) for the exact indexed RGB and split-plane
formats and their eligibility/fallback rules.

The original full-render admission still requires at least **32,212,254,720 bytes (30 GiB)**
of available physical memory and commit headroom, a **13-GiB** process/tree limit, a **7-GiB**
managed heap limit, **4 GiB** of logical GPU buffers, separate **512-MiB** CPU texture,
GPU texture and staging pools, and the **900-second** deadline. The qualified 64-GiB host
preserves **17,002,270,720 bytes** of physical-memory reserve. A separate stricter diagnostic
profile does not qualify the original profile. Failed admission starts no renderer; neither
larger texture pools nor reduced source resolution/content is a substitute for acceptance.

## Dataset and identity

The source is
[NVIDIA PhysicalAI-SimReady-Warehouse-01](https://huggingface.co/datasets/nvidia/PhysicalAI-SimReady-Warehouse-01),
pinned to `c7fe115cb79c7ddbd0532630d7768b5736b0ecc4` in
`eng\datasets\simready-warehouse-01.json`.

The root is `physical_ai_simready_warehouse_01.usd`. The publisher describes 753 catalogued
assets and a prebuilt warehouse that uses payload composition. Catalogue entries are not a
count of placed instances. The published initial scene has colliders but no rigid bodies
applied; a moving demonstration therefore needs a distinct, reversible scenario layer.

The pinned complete file inventory contains **8,804 files, 14,446,592,890 bytes
(13.45 GiB)**. These values were obtained from the complete paginated file tree, not from
the older card's `download_size` field. The profile binds every relative path, size, Git
blob identity and any LFS SHA256 into a canonical inventory digest:
`f09418f2d624fbb635d3f202433f3a8cf1bbd78b2aa149d4201cd5d204ed26cd`.
Inventory drift fails rather than silently adopting changed data.

NVIDIA labels the dataset [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).
Keep attribution, the licence link, pinned source and a description of modifications with
derived outputs. Optional SDKs, shader modules and GPU binaries have separate licences.
No dataset assets are committed to this repository or bundled in its packages.

## Inventory, download and verification

Python's standard library and the existing PowerShell host are sufficient. Inventory is
the default operation; it fetches metadata only:

```powershell
.\eng\fetch-simready-warehouse.ps1 -Operation Inventory -DataRoot D:\simready
```

It saves the hash-validated file list under
`D:\simready\warehouse-01\metadata\<revision>\inventory.json` and prints JSON with
`sourceBytes`, `missingSourceBytes`, `requiredFreeBytes`, `availableFreeBytes`,
`downloadAdmitted` and `snapshotVerified`. A successful inventory operation does not mean
the dataset has been downloaded. Inspect those explicit states.

Once storage admission succeeds:

```powershell
.\eng\fetch-simready-warehouse.ps1 -Operation Download -DataRoot D:\simready
.\eng\fetch-simready-warehouse.ps1 -Operation Verify -DataRoot D:\simready
```

Download and verification use the saved inventory, independently checked against the
repository-pinned digest; editing or truncating that inventory cannot qualify a partial
snapshot as complete.

The default first-download admission is **29,478,978,426 free bytes (27.45 GiB)**:
13.45 GiB source data, 8 GiB material/collision cache allowance, 4 GiB output allowance and
2 GiB safety headroom. These allowances are explicit profile values, not a claim about
measured future cache or GPU requirements. Existing verified files and resumable partial
bytes reduce additional source storage. Free space is checked before any payload and again
before each remaining file. Download fails with `WAREHOUSE_STORAGE_BLOCKED` if it cannot
preserve that headroom; it does not delete data, switch volumes or omit assets.

Source files are published under
`D:\simready\warehouse-01\source\<revision>`. Transfers preserve relative paths, retain
interrupted partials and require the exact resume range. LFS content is verified using its
SHA256; ordinary Git blobs use the Git `blob <length>\0` hash, not plain file SHA1.
Incorrect lengths, digests or range responses cannot publish a source file. Publication is
create-only on the same volume, without a second full-file copy. An existing changed source
file is reported and never overwritten.

Transient connection resets, timeouts, incomplete transfers and HTTP 429/500/502/503/504
responses receive at most five attempts per file. A retry resumes the persisted partial offset
rather than restarting or appending a full response; the complete digest is checked before
publication. Backoff starts at two seconds;
server `Retry-After` instructions take precedence. A requested delay above five minutes stops
the command rather than retrying earlier than the server permits. Authentication/permission,
certificate, digest and structural-integrity errors are not retried. Progress reports count
verified files and bytes, and retry diagnostics name the relative asset without exposing
redirected signed URLs.

A process-lifetime lock prevents concurrent acquisition commands from corrupting shared
partials; the lock is released by the operating system if the process exits. No downloaded
scripts or plugins run. Traversal, reparse points, case collisions, file/directory conflicts
and nonportable Windows names are rejected. A source path reaching 260 characters is
refused with a request for a shorter physical root. The approved `D:\simready` layout's
longest pinned asset path is 228 characters.

`verified.json` is written only after all files pass verification. It is a receipt, not
authority to ignore later file changes: verify again before using a modified snapshot.

## Remaining rendering and physics work

### Actual full-root audit

The pinned root was opened with all active payloads through the matched OpenUSD 26.05 SDK.
The audit reports **112,162 active defined prims including instance proxies**, **10,394
instances**, **322 prototypes**, **30,363 mesh placements**, **26,834 collider prims**, and
no rigid-body prims. Three inactive payloads are intentional and are not counted as missing
active content. The stage is Z-up with `metersPerUnit = 1`. Its active light types include
551 rectangle lights, one distant light and one dome light. No time-sampled attribute
definitions were observed in that composed root.

These are composed-root observations, not counts inferred from the 753-row asset catalogue
or claims that the scene already renders/simulates completely.

The first dependency audit found 13 unresolved assets and 864 composition errors. Seven
obsolete `omniverse://.../materials/physics/*.usda` references actually have exact matching
files in the verified snapshot under `Props/materials/physics`. Their density/friction/
restitution values must be reused, not replaced with guessed defaults. The repository
profile explicitly maps those seven URIs to their local files.

The native audit has an opt-in localization operation. It copies the USD layer graph into
a **new derived cache**, rewriting known dependencies without flattening, modifying source
layers, or copying textures. It keeps instanceability, references/payloads, attributes and
original texture identities. UDIM identifiers are resolved through the SDK's UDIM helper
and retain the original tile location; an unresolved template must not silently become
relative to the cache. Parse failures prevent publication, and an existing cache is not
overwritten.

On the full warehouse, the corrected 967-layer cache is about 3.31 GB. Its geometry counts,
instance/prototype counts and world bounds match the original. The 864 composition errors
are eliminated. **Six dependencies remain unresolved:** `OmniPBR.mdl`, `OmniGlass.mdl`, and
four clock textures referenced through an old Omniverse URI. Subsequent corpus inspection
confirmed all four exact clock texture files in the pinned snapshot, and their explicit
remaps are now also in the profile. The `localized-v3` cache resolves all eleven local
material/texture mappings. With the pinned standalone MDL core below, the full USD
dependency audit has **zero unresolved asset paths and zero composition errors**.
That is USD dependency closure, not complete MDL graph evaluation or rendering readiness.

Build `openusd_warehouse_audit_probe` with the existing native CMake graph and matching SDK.
Then use the verified-source wrapper, with new output paths:

```powershell
.\eng\inspect-simready-warehouse.ps1 `
  -Operation Audit -DataRoot D:\simready `
  -AuditExecutable <absolute-path-to-openusd_warehouse_audit_probe.exe> `
  -NativeRuntimeRoot <absolute-matched-runtime-root> `
  -MaximumCommitBytes <explicit-byte-ceiling> `
  -ReportPath <absolute-new-audit-json>

.\eng\inspect-simready-warehouse.ps1 `
  -Operation Localize -DataRoot D:\simready `
  -AuditExecutable <absolute-path-to-openusd_warehouse_audit_probe.exe> `
  -NativeRuntimeRoot <absolute-matched-runtime-root> `
  -MaximumCommitBytes <explicit-byte-ceiling> `
  -CacheRoot D:\simready\warehouse-01\cache\<new-cache-name> `
  -ReportPath <absolute-new-localization-json>
```

The wrapper is currently Windows-specific and revalidates the original snapshot before
execution. Audit JSON includes the pinned inventory, profile, executable and SDK identities.
An incomplete audit returns an explicit nonzero outcome while retaining its diagnostic
report. Localization reports remaining dependencies separately. Neither operation starts
a renderer or solver.

All three operations require an explicit `MaximumCommitBytes`. The worker starts
suspended and joins a Windows job enforcing both process and tree commit ceilings
before it runs; there is no sampled-only fallback. A requested `MaximumWorkingSetBytes`
also fails before launch if the account lacks the required privilege. Commit is not
physical RAM: shared mappings and system cache remain outside that ceiling. A separate
`.process.json` receipt preserves the limits, kernel peaks and exit outcome even when
the native operation times out or fails.

`-Operation MaterialCensus` avoids reading mesh points, topology and subset indices.
It resolves effective `full` material-purpose bindings, including all-purpose fallback,
inherited/collection strength, instance-proxy placements, and material-bind subsets.
It reports inherited authored visibility and default/render/proxy purpose eligibility,
deduplicated material/shader definitions, and the winning binding relationships.
This is not camera visibility, subset element coverage, point-instancer expansion,
dependency closure or MDL/renderer fidelity. It is the bridge from declaration-only
material inventories to the actual binding requests a renderer must account for.
Reports refuse rather than truncate beyond one million visited prims, 200,000 binding
records or 64 MiB of retained report text. Use the same verified-source wrapper and
new report path; the small `openusd_warehouse_material_census_contract` fixture proves
resolution, exact/over-limit behavior and source preservation without loading a warehouse.

The guarded census of `localized-v3` reverified all 8,804 source files and visited
112,162 active defined prims including proxies. All **30,363 mesh placements** had
resolved effective material bindings; **27,389** were authored-visible and eligible
for the default/render/proxy purpose set. It found 3,233 bound material definitions,
of which 3,144 had eligible placement uses. No material-bind subsets or point instancers
were encountered. Windows reported peak job memory of **533,696,512 bytes** under the explicit
1-GiB configured commit ceiling; no mesh topology was read and no image or simulation was produced.
This is the raw Windows reported peak, not proof of a strict historical aggregate
memory bound; see [kernel-bounded diagnostic accounting](testing.md#kernel-bounded-windows-diagnostics).

Joining those eligible requests to the earlier authored-value projection by module,
material and exact C-ABI parameter values matched every request: 877 distinct requests,
870 partial and seven refused. The refused requests affect 220 eligible placements
(215 legacy signature-qualified OmniPBR placements and five translucent-plastic
placements). These are baseline classifications, not a claim that the new tint support
alone makes those materials complete.

### Zero-weight AO fidelity correction

The pinned current OmniPBR and ClearCoat modules default `ao_to_diffuse` to **zero**.
The adapter previously published the ORM occlusion channel anyway, incorrectly
darkening ambient lighting even when the authored or SDK-resolved material disabled
AO multiplication. Zero weight now omits only that inactive channel; roughness,
metallic, their influences and all source assets remain unchanged. Nonzero AO
weights stay explicitly partial: MDL multiplies diffuse color, not merely the
renderer's ambient-light term.

A fresh bounded replay still resolves all **877 requests**. It removes the inactive
occlusion contribution from **764 requests covering 23,498 eligible placements**,
changes only the obsolete AO diagnostic in 38 more requests, and leaves 75 records
identical. No unrelated scalar, texture, origin, UV or diagnostic changes were found.
Nine SDK groups, the authored-only adapter, and eight native-backed pixel cases pass.
Hardware D3D12/Vulkan authored-zero and module-default-zero HDR/PNG match a neutral
reference exactly; enabling AO changes all 143 foreground pixels in the control.
The original all-black active control was rejected by the diagnostic's no-lit-foreground
check rather than accepted as an image.

This is a fidelity correction for a disabled input, not quality reduction to meet a
memory budget. General colored/fractional AO-to-diffuse semantics remain unsupported,
and no complete warehouse material image is implied.

### Standalone MDL core, without Kit

NVIDIA publishes actual OmniPBR/OmniGlass module sources in
[SimReady Foundation](https://github.com/NVIDIA/simready-foundation) and
[the SimReady Blender add-on](https://github.com/NVIDIA/simready-blender-add-on). Their
embedded BSD-style notices permit redistribution with the notices retained. The modules
are not shader stubs and do not require installing or running the add-on or Kit.

`eng\datasets\simready-mdl-core.json` pins six source files by exact first-party repository
commit, path, size and Git blob identity: OmniPBR, ClearCoat, PBRBase, OmniGlass, Glass
Opacity, and the matching MDL SDK's `nvidia/core_definitions`. Acquisition retains each
complete source and licence header outside the repository:

```powershell
python .\eng\fetch_simready_mdl.py --operation Download --data-root D:\simready
python .\eng\fetch_simready_mdl.py --operation Verify --data-root D:\simready
```

The JSON result gives `moduleRoot`. It is an optional local source dependency and is not
included in base packages. The acquisition receipt deliberately does not claim compilation.
The project-owned `mdl_module_probe` separately compiled the actual pinned OmniPBR and
OmniGlass modules through the existing C ABI and MDL SDK 2026.0.2, checking literal module
defaults without authored inputs. Both passed. It also reported unsupported projection
parameters explicitly; this does **not** establish full material/BSDF shading.

To audit a prepared layer cache with those verified source modules:

```powershell
.\eng\inspect-simready-warehouse.ps1 `
  -Operation Audit -WithMdlCore -DataRoot D:\simready `
  -CacheRoot D:\simready\warehouse-01\cache\localized-v3 `
  -AuditExecutable <absolute-path-to-openusd_warehouse_audit_probe.exe> `
  -NativeRuntimeRoot <absolute-matched-runtime-root> `
  -MaximumCommitBytes <explicit-byte-ceiling> `
  -ReportPath <absolute-new-audit-json>
```

This sets the standard USD asset search path only for the audit process and restores the
caller's environment. It verifies the module profile before use. USD dependencies now
resolve, but nested MDL wrapper evaluation remains a separate requirement. In particular,
the corpus's OmniSurface wrappers and nontrivial GlassWithVolume/helper graphs are not
covered by the PBR/glass defaults proof; OmniSurface's transitive implementation modules
require the separate opt-in profile below.

#### Optional OmniSurface dependency closure

`eng\datasets\simready-mdl-omnisurface.json` pins four unmodified, NVIDIA BSD-noticed
sources from the public [Slang MDL-SDK fork][slang-mdl-test-materials]:
`OmniSurface.mdl` and its `OmniSurface/OmniImage`, `OmniShared`, and `OmniSurfaceBase`
dependencies. This is explicitly a fork-sourced profile, not a first-party NVIDIA
distribution. Each file is pinned by commit, path, byte count and Git blob, and its full
licence notice is preserved. The existing six-file PBR/glass profile is unchanged.

```powershell
python .\eng\fetch_simready_mdl.py --operation Download --data-root D:\simready `
  --profile .\eng\datasets\simready-mdl-omnisurface.json
python .\eng\fetch_simready_mdl.py --operation Verify --data-root D:\simready `
  --profile .\eng\datasets\simready-mdl-omnisurface.json
```

Append the returned `moduleRoot` to `OPENUSD_MDL_MODULE_PATH` alongside the verified core
and original source roots, using the platform's path-list separator. The additional
content-addressed snapshot does not overwrite the core snapshot, original dataset, or
localized layers, and no module source enters a base package.

The unchanged `Natural_Plastic_E.mdl` now compiles with these dependencies. The adapter
verifies its `(*)` prototype names and types before applying the existing partial
OmniSurface projection; it does not rewrite the source or substitute an OmniPBR curve.
The opt-in `mdl_warehouse_omnisurface_projection` CTest uses
`OPENUSD_MDL_OMNISURFACE_MODULE_ROOT` and `OPENUSD_MDL_WAREHOUSE_SOURCE_ROOT` at configure
time, alongside the existing SDK/core configuration. It checks real wrapper constants,
authored overrides, explicit unsupported reflection/transmission/subsurface controls,
and signature rejection. Dependency closure and this parameter projection do **not**
qualify translucent-plastic appearance, complete OmniSurface BSDFs, or warehouse pixels.

Replaying the saved, topology-free eligible census with the additional root produced
**871 partial successes and six refusals across the same 877 requests**. Only the
five-placement plastic request gained shading data; all 870 previously successful
records were unchanged. The six legacy requests still cover 215 placements. Their
diagnostics now include the actual SDK signature, as introduced by the preceding
signature-resolution change. The replay completed under a 1-GiB process/tree commit
cap, with a 1,037,246,464-byte measured peak; this is not a full-scene memory bound.

#### Isolated legacy OmniPBR interface

`eng\datasets\simready-mdl-legacy-pbr.json` pins the actual OmniPBR 2.1.0 source,
`OmniPBR_ClearCoat_Opacity`, and its `nvidia/core_definitions` dependency from a
[historical Gatling source revision][gatling-mdl].
These are third-party redistributions with complete NVIDIA BSD notices, not shader stubs
or first-party distribution claims. The snapshot is separate from both current profiles.

```powershell
python .\eng\fetch_simready_mdl.py --operation Download --data-root D:\simready `
  --profile .\eng\datasets\simready-mdl-legacy-pbr.json
python .\eng\fetch_simready_mdl.py --operation Verify --data-root D:\simready `
  --profile .\eng\datasets\simready-mdl-legacy-pbr.json
```

The SDK matches the warehouse's full legacy signature against this real definition
without changing its type list. An isolated replay of all six requests retained every
authored parameter and signature and produced partial projections for all 215 placements.
Only the diagnostic's explicitly selected module path changed; no USD layer or production
module-routing policy changed. The older implementation uses the same 0.08/1.0/exponent-5
dielectric curve with a separately weighted metallic layer.

`OPENUSD_MDL_LEGACY_MODULE_ROOT`, alongside `OPENUSD_MDL_REAL_MODULE_ROOT` at CMake
configure time, enables `mdl_sdk_legacy_pbr`. It checks exact legacy resolution, authored
controls, malformed-signature refusal, and current/legacy/current SDK context isolation.
This CMake test option is **not** the runtime selection setting. Do not prepend the legacy
root to an existing live module path: both versions define `::OmniPBR` and
`::nvidia::core_definitions`, so search-order substitution can change unrelated materials.
Use the separate runtime setting for mixed-version selection:

```powershell
$env:OPENUSD_MDL_LEGACY_PBR_ROOT = "<verified legacy moduleRoot>"
```

Set it before starting Viewer or MCP, with the current module roots still in
`OPENUSD_MDL_MODULE_PATH`. Use a matched new SDK-backed adapter and hdSilk build.
The setting is disabled when absent. Only an existing, successfully compiled
`OmniPBR.mdl` missing an exact `OmniPBR(...)` signature may try this one alternative
root. The SDK must match the original full signature; authored inputs are unchanged.
Successful current requests, unqualified names, missing primary modules, compilation
failures, and unsupported bodies are not redirected. Invalid configuration and a refused
candidate retain the primary refusal with explicit additional diagnostics.

The alternative uses isolated SDK contexts under the existing root-count and scope
bounds, not a global search-order replacement. Selected-module diagnostics survive the
native bridge and are reported against the material. Windows configuration paths are
read as Unicode and converted to UTF-8. Changing this process configuration requires
restarting/reloading the host; it is not a live review-layer edit.

Native routing and diagnostic controls passed, including current/legacy/current calls
and a Unicode legacy root. The routing-enabled replay now covers **all 877 requests and
27,389 eligible placements with partial projections and no module-resolution refusals**.
Every one of the preceding 871 successful records is unchanged; the six newly routed
requests match their isolated legacy-module results with original signatures and
authored inputs retained.

The first attempt was stopped by the protected host-headroom guard. A checkpointed
replay then completed in seven bounded processes, followed by one fresh process whose
877 records matched every checkpoint exactly. The single-process replay used the same
1-GiB process/tree limit and peaked at 882,057,216 bytes. It replayed the frozen request
inventory instead of reparsing the much larger scene-metadata audit; no requests or
inputs were dropped, and scene topology and pixels were not loaded.

Once host headroom recovered, the actual Hydra-to-GPU path exposed a second signature
bug: the synthesized node identifier was split at the last colon inside
`::base::mono_mode`. A shared length-framed producer/consumer codec now preserves both
the module and the complete signature. The old synthetic form remains readable, and
malformed framed identities are refused. Native network and framing controls include
Windows paths, URI ports, parentheses, Unicode, empty subidentifiers and length overflow.

The corrected runtime produces **byte-identical 32x32 HDR and PNG** for the current-module
reference and exact-signature legacy triangle on RTX 5070 D3D12 and Vulkan. D3D12 stayed
under a 1-GiB process/tree cap. The Vulkan driver could not compile the reference pipeline
under that cap; the identical one-triangle input succeeded under 1.5 GiB, with measured
peaks below 1.40 billion bytes. Full-scene caps were not increased. The rebuilt required
NativeAOT package consumer also passed with the corrected runtime.

These qualify identity preservation and the tested constant-material path, not every
texture or BSDF. The complete material frame, full fidelity, placement/geometry
reconciliation, and physics acceptance remain separate work.

#### SDK-resolved tiled texture defaults

The optional adapter can now retain filesystem-backed `<UDIM>` sets obtained from
compiled module defaults and inherited variants, using the renderer's existing absolute
mask representation. It verifies every SDK tile coordinate and regular-file identity,
preserves gamma/tint/origin, and enforces 1001..1999 tile numbering plus limits of
256 tiles and 256 rectangular atlas cells. It neither decodes nor resizes the tiles.
Missing sets, excessive sparse extents, animated resources, other tile encodings,
selectors and container members remain explicit refusals.

Seven minimized cases cover two-tile defaults, inherited tint, a single-file authored
override, the exact 256-tile/cell boundary, missing sets, sparse overflow and 257 tiles.
All nine related SDK CTest groups pass under a 1-GiB process/tree cap. The complete
877-request warehouse replay is unchanged: the observed authored UDIM values already
used the existing direct-input path, so this closes a module-default gap rather than
claiming new visible warehouse coverage. A native-only comparison of the actual composed
authored and module-default stages publishes identical 344-byte material commands; the
preceding adapter fails the wrapper in that same comparison.

The actual two-tile hardware comparison is now qualified on RTX 5070 D3D12 and Vulkan:
authored and module-default paths produce byte-identical HDR and PNG, with 54 red-tile
and 89 green-tile pixels in the 32x32 view and no missing-texture/fallback diagnostics.
Both run under a 1.5-GiB process/tree cap. The SDK-only ASCII PPM fixtures initially produced
equal fallback images because this installed Hio profile could not decode them. Those
images were rejected by the tile-color check and are **not** qualified pixel evidence.
Fresh lossless PNG fixtures carry the same tile colors and pass. Required-material
diagnostics now refuse missing/unreadable texture fallbacks before publishing an image,
so matching grey fallback frames cannot pass as material fidelity.

### Bounded SDK wrapper qualification

The SDK-backed adapter now also projects SDK-proven material variants (`(*)`) rooted in
OmniPBR, OmniGlass, or OmniSurface. This uses the SDK's prototype, argument and expression relationships,
not a text scan of MDL source. Wrapper constants, parameter aliases, authored-value
precedence and resolved texture ownership are checked through the existing C ABI.

The effective-binding census identified authored `diffuse_tint` on 7,584 eligible
placements in the earlier unsupported-input evidence. The adapter now multiplies
constant color and diffuse texture RGB scale while preserving alpha and
texture ownership/origin. A real PNG-based HDR comparison checks exact wire values
and pixel equality against independent PreviewSurface controls on D3D12 and Vulkan;
an untinted control must differ, and missing/failed texture fallback is forbidden
in the fixture. Finite extended texture tint now uses admitted float storage rather
than clipped bytes. Non-finite/overflowed and otherwise unsupported controls remain
diagnosed rather than being declared equivalent.

Bounded roughness/metallic texture influence now uses the existing scalar-channel
scale/bias representation, including the selected ORM G/B channels and ORM priority
over standalone maps. A zero influence keeps the constant; invalid ranges remain
diagnosed. Native real-wrapper checks preserve the painted-metal material's authored
0.45 roughness blend, and direct/packed HDR cases match independent controls on D3D12
and Vulkan. This does not qualify the still-unimplemented normal-roughness adjustment,
albedo grading or complete MDL BSDF behavior.

A fresh projection of all 877 eligible requests through the updated SDK adapter still
reports 870 partial results and seven refusals; none is claimed fully faithful.
Unprojected authored-input incidences, weighted by eligible placements, fell from
7,584 to zero for diffuse tint, 13,991 to 499 for roughness influence, and 13,016 to
1,909 for metallic influence. Some controls are neutral or inactive, so these figures
are not counts of visibly changed objects. The projection used the saved census, not
another topology traversal, with an explicit 2-GiB process/tree commit quota.

Optional MDL adapter ABI 3 adds a distinct material-wide UV affine. Matched adapter
and hdSilk loader builds now preserve finite OmniPBR authored-set-zero scale,
clockwise rotation and translation, including the real wrappers' 4x and 2x tiling.
The prior ABI-2 adapter is rejected explicitly rather than misread. Projected
world/object UVs, other UV sets, and detail/clearcoat streams are still unsupported.
The base session, page and data ABIs are unchanged; no optional adapter is added to
the base packages.

The ABI-3 re-projection of the saved eligible census retains all 877 request outcomes
and emits a valid authored UV affine for 844 requests, including 272 nonidentity
transforms. Remaining unhandled authored scale incidences are 51 eligible placements,
down from 3,681; rotation and translation each remain unhandled on six projected or
alternate-coordinate placements. These control counts do not imply complete
materials or camera-visible pixel coverage.

Regular normal textures now retain finite signed strength and horizontal/vertical
tangent flips, including the pinned module's default vertical flip. Strong maps
are promoted to RGBA32F with overlapping CPU and actual GPU payload admission;
zero strength omits the inactive map. Detail-normal mixing, packed normal-roughness
inputs and full BSDF fidelity remain incomplete. Small exact HDR controls establish
this projection only, not a warehouse image or complete visual equivalence.

A saved-census re-projection with these normal controls still reports 870 partial
requests and seven refusals. Unsupported authored strength incidences fall from
2,595 to 291 eligible placements; horizontal and vertical flip incidences fall from
62 and 58 respectively to six each. Windows reported 959,332,352 bytes of peak job
memory under the explicit 2-GiB commit quota. No mesh topology was traversed; the
remaining neutral/inactive controls and partial results are not a full-fidelity claim.

The first neutral-base detail projection used the existing normal-map
path: a regular detail normal over an absent/empty or resolved zero-strength base
reduces to that detail normal. Signed detail strength, tangent flips, color space and
asset provenance are retained. A sole detail texture can use its own affine; when
other maps shared the material affine, the detail affine had to match it. Active or
unresolved bases and incompatible shared UVs remained explicitly unsupported rather
than replacing existing textures or consuming the emission-composite slot.

This adds normals to six current requests covering 78 eligible placements, leaving
all other 871 result records unchanged. Native SDK and actual composed-stage controls
pass, including identical reference/detail material command bytes. Bounded RTX 5070
D3D12 and Vulkan controls now produce byte-identical HDR/PNG for the standard reference,
detail-only and zero-strength-base cases. All 143 foreground pixels differ from the
flat-normal control. Matching-UV diffuse/detail maps coexist without changing the
reference pixels; incompatible UVs leave diffuse-only pixels unchanged and are diagnosed.
General two-active-map blending was not part of that earlier compatible subset.

Optional MDL ABI 7 and page ABI 27 now carry two active regular normal maps and
independent detail UV/tangent affines. The fragment shader performs RNM after
sampling both maps; it does not bake, resample or discard either input. Detail
normals use a dedicated normal-only binding, leaving the emission composite
available simultaneously and avoiding any collision with sampled density volumes.
The surface block grows to 272 bytes, with unchanged light/shadow-mask offsets.

The complete bounded replay still resolves **877 requests**. Exactly **39 requests
covering 2,689 eligible placements** gain a detail normal and paired UV rows;
**838 records remain identical**. All pre-existing scalar values, textures, global
UV affines and asset metadata are preserved. Only newly implemented detail controls
and tangent flips cease to be reported as unsupported.

Fourteen native-backed D3D12/Vulkan cases cover independent Rodrigues-rotation
references, rotated detail plus simultaneous emission-mask multiplication,
retained/streamed uploads, varying detail images, two-tile UDIM coordinates and
antipodal primary normals, under a 1-GiB process/tree cap. Independent coordinate
streams, packed normal/roughness decoding, general layered BSDFs and a complete
warehouse image remain separate requirements.

The same fourteen cases also pass on RTX 5070 hardware with the existing 1.5-GiB
small-hardware test ceiling. A Vulkan-only reproduction showed that a 1-GiB job
could refuse native pipeline creation with `ErrorUnknown`; the identical case
passed at 1.5 GiB without changing shaders or material inputs. This changes no
warehouse quota or protected host reserve. Existing hardware zero-AO HDR/PNG
controls remain byte-identical after the ABI and surface-layout update.

Albedo brightness/addition now follow the pinned module's texture-only order:
brightness, then addition, then tint. They do not modify the selected constant color.
Independent HDR controls pass on WARP, SwiftShader and hardware D3D12. A further
saved-census projection removes all 2,967 previously unsupported authored addition
incidences and all 400 brightness incidences; desaturation remains on 647.
The 870 partial results and seven refusals are unchanged. This projection stayed
under an explicit 2-GiB job commit quota, with reported peak job memory
1,023,438,848 bytes, and all original source files were reverified unchanged.

Optional MDL ABI 4 and hdSilk page ABI 25 add a separate diffuse texture luminance
mix, preserving alpha, constant colors and the existing grading/tint order.
Independent float-texel HDR controls cover quarter/full mixes and unclipped
extrapolation on both D3D12 and Vulkan. The actual warehouse includes a negative
desaturation of `-0.3` on 36 eligible placements; this is authored saturation,
not an invalid value to clamp away.
The saved-census v7 projection removes all 647 previously unsupported authored
desaturation incidences and emits 18 active luminance-mix records. It retains
870 partial requests and seven refusals. The metadata-only process used a 2-GiB
commit quota and reported peak job memory of 945,037,312 bytes.

SDK enum defaults and explicit opacity mono modes now replace the earlier red-only
projection: 164 distinct requests use average RGB and two use luminance, covering
10,811 and five eligible binding incidences respectively. The alpha and maximum
modes are independently qualified too. The shared cutout shader now makes threshold
survivors fully opaque instead of retaining fractional alpha. The v8 projection
still has 870 partial requests/seven refusals; the remaining 48 authored mode
incidences belong to inactive opacity paths conservatively reported as unused.
This was another saved-metadata-only run under a 2-GiB commit quota, with raw
reported peak job memory of 953,073,664 bytes. Opacity-aware shadows/picking and
complete glass/transparent-material fidelity remain unfinished.

SDK-proven OmniPBR emission now retains intensity and masks using the pinned
diffuse EDF's exitance-to-radiance normalization. The v9 saved-census projection
removes all 97 previously unsupported authored intensity incidences and all five
mask incidences: four active requests use color textures, one uses a constant
color plus mask, and four use constant emission. Optional adapter ABI 5 carries
an explicit multiply operand when both color and mask textures are present;
the base page remains ABI 25. Independent HDR controls preserve alpha and source
bytes. This affects visible emitting surfaces, not their illumination of other
geometry. The 870 partial results and seven refusals remain unchanged.
The metadata run used a 2-GiB process/tree commit quota and reported peak job
memory of 950,984,704 bytes; all 8,804 original files were reverified unchanged.

### Bounded command-stream diagnostic

The SDK-proven OmniPBR `specular_level` is now represented by optional adapter ABI 6
and page ABI 26. The direct-light and environment shaders use its full weighted
dielectric curve and an independent metallic mixture. Small endpoint, interpolation,
metalness and SDK-to-HDR controls qualify this operation; they do not establish the
remaining detail-normal, packed-normal, glass or complete warehouse BSDF behavior.
Earlier material color/UV/normal controls now explicitly use the same independently
qualified default dielectric curve instead of treating a plain PreviewSurface as an
exact OmniPBR BSDF oracle.

Re-evaluating the saved, topology-free eligible-material census with ABI 6 retains
all 877 requests and the prior 870 successful partial projections / seven refusals.
The new scalar is present in 848 SDK-proven requests covering 26,962 eligible
placements; 481 requests no longer report `specular_level` as unsupported. Observed
projected levels are 0.5, approximately 0.2, 0.75 and approximately 0.05. This census
measures projected inputs, not image fidelity or final camera-visible coverage.

After requalifying one triangle against the current runtime, guarded full-root
create/sync diagnostics were run with pre-start process/tree commit limits,
monitored physical/commit headroom, fixed page limits and no GPU rendering.
MDL was deliberately disabled, so these are geometry/resource diagnostics, not
material-fidelity results. Mesh-reservation refusal and a 2-GiB kernel allocation
refusal both returned cleanly without publishing a page.
Logical mesh-reservation ceilings were raised separately from the kernel caps
(2, then 4, then 8 GiB); those conservative work estimates are not host allocations.

An 8-GiB guarded run reached serialization. Aggregate triangle-page preflight now
reports **4,743,378,888 bytes** for that complete update: 1,026,468 prefix bytes and
4,742,352,420 remaining command bytes. Its raw peak job memory was 7,749,017,600
bytes; the sampled working set was larger because mapped/shared pages are outside
the commit quota. The 128-MiB page ceiling refused the update before large mesh
payload copies. No page or image was published.
A follow-up measured 4,743,357,910 bytes and a largest individual mesh command
of 59,664,633 bytes, below 128 MiB. The small cross-process byte-count variation
has not yet been attributed; complete placement/identity reconciliation remains required.

A minimized fresh-process control now isolates one concrete source of byte-count
variation: USD assigns generated `__Prototype_N` names differently, and unequal instance
multiplicities repeat those names different numbers of times. Six nine-prototype runs
remain exactly 46,282 bytes. At ten prototypes, a second digit produces 51,322 versus
51,319 bytes; the complete three-byte difference is in serialized path strings.
A 30-prototype control varies from 256,384 to 256,555 bytes, again entirely accounted
for by path-string lengths. Each control preserves the same authored identity/transform
set and geometry payload counts across processes. Raw page hashes are therefore not a
cross-session semantic identity; use the source-origin mapping. This explains the
minimized mechanism, not every byte of the historical full-warehouse difference.

This stream exceeds both a single managed byte array and the current one-page
transport's practical limits. Increasing a memory ceiling cannot make that
representation valid: bounded command transport with one atomic publication
is still required before the complete warehouse can be rendered.

The file-backed path is now connected through the C ABI, managed reader, Viewer/MCP
policy, per-session live-file quota and transactional renderer/capture replay.
Small real-file fixtures cover exact output and failure/retry behavior, and captures
match RGBA/depth on RTX 5070 D3D12/Vulkan.

After requalifying the triangle's exact byte hash under a 512-MiB job, a synchronization-only
warehouse run succeeded with **4,743,378,157 bytes, 32,521 commands, 27,389 mesh records,
58,640,734 points and 268,704,708 indices**. Its SHA-256 is
`640059807F236670E71DD42FB63334E6AB5C80A147D86A12FBED56A66D4FC33F`.
The pre-start process/tree cap was 8 GiB, managed heap cap 256 MiB, live-file/update
quota 6 GiB, command ceiling 128 MiB and transfer buffer 1 MiB. Raw peak job memory
was 7,828,570,112 bytes; the monitored run completed in 43.36 seconds and preserved
the localized root hash.

This run used **no MDL and no GPU rendering**. Its mesh-record count matches the
earlier eligible-placement count numerically, not yet by an independently reconciled
identity set. Cross-process byte variation remains unattributed. Retained CPU/GPU
geometry costs, actual warehouse pixels/materials and Viewer/MCP journeys remain
unqualified; successful transport is not complete warehouse acceptance.

The diagnostic can now stream a create-only, byte-limited JSONL identity inventory
before GPU preparation, without serializing vertex/index arrays. It retains each
wire path/index, ordered instancer context, transform, topology counts and material
path, with a completion record and SHA-256. Small controls cover an ordinary mesh,
four two-level point instances and two USD native instances under 1-GiB process/tree
caps. Exact 2,179-byte admission succeeds; one byte less refuses publication and
removes the pending file. Existing output is never overwritten. These are raw
Hydra identities: USD native instances still name propagated prototypes rather
than their authored instance-proxy paths. The full census join and cross-process
variation therefore remain unqualified; the inventory is not a substitute for them.

The schema-2 inventory adds USD-resolved source paths and point-instancer contexts in
256-key batches while native imaging is still alive. The new optional source-identity
extension uses USD origin data rather than stripping generated path segments or guessing
from transforms. A masked, multi-prototype control exposed that stable wire indices are
not USD pick ordinals; these are now retained separately without changing page ABI 26.
The source resolver refuses stale pages, changed stages, unknown keys and over-budget
results atomically. Small native/managed controls and the rebuilt NativeAOT package
consumer qualify this mechanism; the subsequent complete warehouse join is reported below.

`eng/reconcile_simready_placements.py` compares a schema-2 inventory against the eligible
geometry paths in a schema-1 effective material census or schema-2 placement census.
The native audit's `--placement-census <plugins> <stage>` mode independently records USD
world transforms and inherited visibility at numeric time zero, without reading topology
or vertex arrays. This differs deliberately from the material census's default-time scope.
Transform comparisons require an explicitly time-zero inventory, finite 16-component
row-major matrices, and absolute/relative tolerances of `1e-6` stage units / `1e-9`.
The report records both tolerances and the largest absolute difference. Identity matches
cannot hide a transform mismatch, and legacy identity-only reports do not claim transform coverage.
The checker also verifies the recorded inventory hash,
extent and completion marker, stage-root agreement, preserved source/runtime identities,
missing/unexpected source paths, duplicate source paths and duplicate retained keys.
Point-instance contexts require a separately expanded oracle and are not silently collapsed
into a prototype path. Inputs are limited to 64 MiB, inventory lines to 1 MiB, and records
to 100,000; mismatch reports include total counts and at most 64 examples per category.
Exit codes are 0 when the requested identity/transform comparison matches, 1 for a mismatch and 2 for invalid input
or publication failure. Reports are create-only and always say `frameAccepted: false`.
An independent USD census and the retained inventory match all 257 identities and
world matrices in the batch-boundary control with zero component difference. A separate
rotation/nonuniform-scale/animated-translation control also matches exactly and excludes
an instance whose visibility is time-sampled invisible at zero.

The complete source-side oracle now records **30,363 placements**, including **27,389
eligible identities**, and finite world matrices at time zero. Its eligible identity set
exactly matches the frozen effective-material census. Collection reads no topology or
vertex arrays: it completed in 5.25 seconds with a **348,643,328-byte** peak under a
1-GiB process/tree cap, preserving the localized root hash. This supplies an independent
expected set, not a pixel acceptance result.

The next admitted full-MDL attempt emitted a 29,077,800-byte source inventory.
All **27,389 emitted source identities and world matrices** match that independent
oracle: no missing, unexpected or duplicate identities, and a maximum matrix-component
difference of **1.4210854715202e-14**. All **6,838 GPU geometries** uploaded. The unchanged
physical-headroom guard then stopped the process after 195.13 seconds, at a
12,190,474,240-byte job peak below the 13-GiB cap. Source/runtime hashes were preserved.
No material frame was accepted; this closes the identity/placement comparison, not
topology, shading, pixel or live-host acceptance.

A minimized allocation control identifies a host-resident cost in the D3D12 geometry
path: ordinary CPU-writable upload heaps consume system memory. The new explicit
GPU-upload-heap factory option moves those allocations to supported local GPU memory
without changing bytes or lowering quotas. A 128-MiB control preserves its private-commit
charge but shifts DXGI usage from nonlocal to local memory and avoids almost all of
the corresponding process working-set increase. Hardware heap/readback/draw and
NativeAOT controls pass; a complete warehouse frame using this opt-in remains unqualified.

The first admitted GPU-local full-material attempt reached material drawing, then
refused an **89,478,484-byte** texture with **497,269,416 bytes** reserved in the unchanged
512-MiB GPU texture pool. A host commit-headroom stop was also recorded; the raw Windows
job peak slightly exceeded its configured 13-GiB limit, so that counter is not asserted
as a strict historical commit bound. Source/runtime hashes and all 27,389 reconciled
placements remained unchanged; no frame was accepted.

The allocation size was reproduced with six 4K RGBA8 maps on one quad, and separately
with six 2K float maps. Streamed ordinary scalar inputs now store their already-reduced
red channel as `R8Unorm` or `R32Float`, preserving precision, source resolution, filtering
and every mip rather than storing the same scalar four times. Seven scalar slots and
their five composite operators match the retained path on both backends. Exact 126-byte
and 504-byte working sets pass; one byte less refuses without changing the published
frame, and replay succeeds. UDIM metadata and vector/color maps remain RGBA.

The six-map 4K control uploads **268,435,452 bytes**, with **268,460,040 peak GPU texture
bytes** and **134,217,728 peak CPU pixel bytes**, under the original pools. D3D12/Vulkan
HDR and PNG exactly match a mip-invariant neutral-normal reference. An earlier non-unit
normal reference produced different HDR after normal-map mip filtering and was not
accepted as equivalence evidence. The 64-case streaming/row suite, stricter eight-case
exact-boundary follow-up, 1,984 managed cases per target framework and required NativeAOT
consumer pass; 20 optional native-environment managed cases remain skipped per framework.
All 108 checked shader/reflection payloads remained byte-identical after regenerating
their package-test input provenance with the original Windows SDK DXC.

The following full attempt progressed beyond the former scalar-map refusal but
encountered a native D3D12 staging allocation failure near its process-commit cap.
The ordinary upload-band ceiling is now 8 MiB rather than 64 MiB. The same 4K float
control retains all 357,913,936 mip bytes and exactly reproduces its prior HDR/PNG,
while native staging peaks at 8,389,376 bytes on D3D12 and 8,388,992 on Vulkan.
The old path explicitly refuses that smaller transfer pool. This reduces transient
transfer pressure; it does not establish that the whole-scene process-memory failure
is fixed, and no whole-scene quota or protected host reserve was changed.

Six 4K maps rendered repeatedly for 16 unchanged frames showed no sustained growth
in that minimized path: material-cache entries and CPU texture reservations returned
to zero each frame, and private memory settled into an approximately 1.07/1.15-GB
alternation. A separate committed-resource control exposed another concrete cost:
1,000 tiny 160-byte uniforms allocated 1,000 separate D3D12 resources. They now share
four 64-KiB pages with independent aligned ranges, including the scene uniforms'
`Uniform|Storage|Upload` usage. Live leases prevent range reuse, and empty pages
release promptly. Pooled offsets are respected by graphics/compute bindings and
readback; hardware pixels, 147 D3D12 regressions, eight focused lifetime/concurrency
cases and required NativeAOT package execution pass. The logical buffer pool is
unchanged; complete warehouse process-memory acceptance remains separate.

A 65,536-point wire fixture also exposed a managed double copy of point-origin and
corner-edge tables. Their combined 524,284-byte payload previously added 1,048,672
allocation bytes; retention now allocates only one copy plus array overhead.
Simulated-position updates reuse these immutable tables rather than copying them
per update. Defensive copies of caller-supplied tables, source-wire independence,
negative identity sentinels and lightweight-instance sharing remain intact.
Forty targeted identity/physics cases pass on each of net8/net9/net10 under a
1-GiB process/tree cap. This qualifies the bounded allocation change, not a
measured reduction in the full warehouse peak.

```powershell
python .\eng\reconcile_simready_placements.py `
  --census '<effective-or-placement-census.json>' `
  --manifest '<capture-directory>\diagnostic.placements.jsonl' `
  --capture-receipt '<capture-directory>\receipt.json' `
  --output '<new-reconciliation-report.json>'
```

Bounded **diagnostic exterior captures** now execute on both RTX 5070 backends at
1920x1080, retaining all 27,389 expected mesh records in both CPU and GPU scene state.
The diagnostic closes native imaging after storing the complete update, before GPU
preparation; this is not yet the full live Viewer/MCP journey. MDL is deliberately
disabled, so these are geometry/transport diagnostics, not material-fidelity evidence.
The raw display clipped to a white silhouette. A measured foreground HDR median of
820.28345 selected -12.15391 stops with the existing Reinhard transform; changing exposure
left the captured HDR bytes unchanged. The exterior still occludes interior geometry.

| Diagnostic | Process/tree cap | Managed heap cap | Raw peak job bytes | Elapsed |
| --- | --- | --- | --- | --- |
| D3D12 exterior | 10 GiB | 6 GiB | 10,427,478,016 | 80.48 s |
| Vulkan exterior | 12 GiB | 6 GiB | 11,165,958,144 | 70.51 s |

Both retained 2,490,179,760 logical GPU buffer bytes under a 4-GiB pool, plus independent
512-MiB CPU-texture, GPU-texture and staging ceilings. Host headroom was monitored and
recovered after process exit. The Vulkan driver needed additional process headroom:
its minimized shader-creation case refused under 1 GiB and succeeded under 2 GiB.
The byte-count variation between fresh native sessions remains unassigned.

These runs exposed per-draw descriptor allocation in both backends. D3D12 now packs and
reuses descriptor tables within a submission; Vulkan allocates draw sets from per-layout
256-set submission pools. Their resources remain owned until GPU completion. Tiny-draw
regressions, rollover, material-binding and lifetime cases cover these paths without
warehouse topology. Retained geometry also shares its already immutable numeric arrays,
per-vertex normals reuse their attribute buffer, and picking identity caches no longer
retain unused draw-index arrays.

Full-material preparation remains **incomplete**. Opt-in full-resolution material
working-set streaming now has bounded small-scene evidence: ordered HDR/depth equivalence,
transparent material revisits, exact/one-byte-short pixel admission, later-batch rollback
and replay, and final display/selection on D3D12 WARP and Vulkan SwiftShader. SDK-backed
RTX triangle captures also complete on both backends. These are not warehouse material
fidelity evidence or live Viewer/MCP qualification.

The first streamed MDL warehouse attempt used a 12-GiB process/tree cap, 6-GiB managed
heap cap, 4-GiB GPU-buffer pool, independent 512-MiB texture/staging pools, a timeout
and monitored host headroom. It stopped **before streamed drawing**, refusing 257,952
GPU-buffer bytes with 4,294,863,504 bytes reserved against the 4,294,967,296-byte ceiling.
Raw peak job memory was 12,409,274,368 bytes; elapsed time was 216.92 seconds. No image
was produced. Image-decoder managed allocations also raised first-chance out-of-memory
exceptions during preparation. Geometry layout/buffer demand and decoder peak ownership
must be isolated with small probes before another full-topology attempt or any quota
increase. The successful no-MDL exterior footprint must not be used as a full-material
memory estimate.

Subsequent minimized probes identified an actual geometry-cache defect: record equality
compared newly allocated vertex-attribute lists by identity. Every normal-mapped instance
therefore missed its prototype's 48-byte layout. Structural comparison now restores
sharing; two-instance D3D12/Vulkan controls prove one geometry upload and both placements.
The next bounded warehouse run prepared 6,838 geometries using about **3.863 GB** of
GPU buffers under the unchanged 4-GiB quota. It then reached a distinct missing-UV pipeline
failure, also reproduced with a single triangle. Missing coordinate inputs now receive
explicitly diagnosed renderer-default `(0, 0)` values when no resolved native default exists.
Native named float2 primvar-reader fallbacks are now projected as constant coordinates when
the authored set is absent. Small stored-page/native GPU controls compare these values and
time edits against explicit coordinates, including a UV transform. This does not establish
full warehouse material fidelity.

The next admitted full-MDL attempt completed native synchronization and all **6,838**
geometry uploads under the unchanged 13-GiB process/tree, 7-GiB managed heap and 4-GiB
GPU-buffer limits. It then refused a 357,913,936-byte mip-chain allocation while a
268,435,456-byte float base image was still owned in the 512-MiB CPU pool. No image was
accepted, and its process exited cleanly through the guard's cleanup.

That overlap was reproduced with a 4x4 texture and independently with one 4K texture
on a single quad, without warehouse topology. Built-in backends now progressively
upload ordinary texture rows and generate only the next mip while retaining the
current one. A full 4K RGBA32F chain still uploads all 357,913,936 bytes, but its owned
CPU-pixel peak is 335,544,320 bytes. D3D12 and Vulkan images exactly match the uniform
reference, and the 512-MiB CPU pool is unchanged. Per-material GPU residency still
must fit its own pool; this does not lower resolution, remove maps, or bypass any
allocation or host-headroom limit.

The 46 focused Windows conformance cases cover every uploaded mip, row offsets,
immutable command snapshots, tight budgets and failure-atomic replay. Related managed
image/retention tests pass on net8.0/net9.0/net10.0 and the required rebuilt NativeAOT
package consumer passes. Full-warehouse completion remains a separate gate; no
successful material-complete frame is implied by the isolated 4K control.

An additional single-mesh cancellation regression exposed an unchecked upload path.
Capture cancellation now stops after the completed row band or mip submission, preserving
the published color and scene revision, releasing CPU/staging reservations, and allowing
ordinary replay without retaining the cancelled request. The expanded streaming/row suite
passes all 50 D3D12/Vulkan cases under a 1-GiB process/tree cap; its cancellation controls
measure exactly 64 bytes for one row or 256 bytes for the first complete mip, with no
later uploads. The earlier 46-case evidence remains a separate, preserved qualification.

Prepared environment transactions also now propagate managed allocation failures instead
of committing fallback lighting. Authored normals avoid unused normal-accumulator work.
Streaming configuration forwarding is implemented for Viewer/MCP, including isolated product
captures, but full live-host memory behavior is still unqualified. The diagnostic now
renders HDR once and uses the existing CPU display converter for exposure; its PNG and HDR
triangle outputs match the previous repeated-render path byte-for-byte.

A subsequent single-pass run reached material drawing and refused an 89,478,484-byte
upload copy while 447,494,820 CPU texture bytes were live under the 512-MiB pool.
The minimized three-map case demonstrated that retaining every map's pixels and copies
until the material draw caused the overlap. Streaming now completes each map upload and
releases its CPU pixels before decoding the next, retaining the full GPU material working
set until its draw completes. No resolution or texture channels are dropped.

The unchanged warehouse's painted-metal wrapper retains roughness **0.19**, rather than
silently inheriting the base module's **0.5**.

Module locations must lie inside the explicitly configured search roots. For warehouse
use, `OPENUSD_MDL_MODULE_PATH` needs the acquired core module directory **and the common
original source root**, separated by the platform path separator (`;` on Windows).
It does not need one directory per asset. An absolute module URI outside those roots is
deliberately refused. `OPENUSD_MDL_ADAPTER_PATH` and `OPENUSD_MDL_SDK_RUNTIME` still name
the absolute adapter and SDK library files; neither runtime is added to base packages.

The opt-in `mdl_sdk_wrapper_variants` CTest requires `OPENUSD_MDL_REAL_MODULE_ROOT`.
`mdl_warehouse_wrapper_projection` additionally requires the configured CMake variable
`OPENUSD_MDL_WAREHOUSE_SOURCE_ROOT`. The latter reads two unchanged real warehouse
wrappers. These probes retain named diagnostics for unprojected controls; a successful
call does not imply a complete BSDF. The separate `mdl_sdk_alias_color_probe` preserves
authored raw/sRGB/auto metadata through parameter and constructor aliases, including
explicit destination overrides. Its regressions fail against the pre-correction adapter
and pass against the corrected implementation.

`SilkWarehouseMdlWrapperTests.SdkVariantConstantsReachHydraAndTheRenderedHdrPixels`
also exercises the actual hdSilk translation and D3D12 HDR path. Its retained pixels must
equal an explicit PreviewSurface fixture and differ from a base-default control.
It requires `OPENUSD_WAREHOUSE_MDL_REQUIRED=1`, a matched `OPENUSD_TEST_PLUGIN_PATH`,
and `OPENUSD_WAREHOUSE_MDL_WRAPPER` pointing to the repository's
`native\openusd_mdl\tests\fixtures\wrappers\variants.mdl`. For that fixture, include its
common `fixtures` directory alongside the real core in `OPENUSD_MDL_MODULE_PATH`.
Build the conformance project, then run it through `eng\run-managed-tests.ps1` with the
`/*/*/SilkWarehouseMdlWrapperTests/*` tree-node filter. This small fixture is not a
warehouse image. The device name and software classification come from the actual DXGI
adapter; the local hardware run recorded **NVIDIA GeForce RTX 5070**, not WARP.

A read-only defaults-only coverage measurement reached all **426 distinct module/material
identities across 3,701 declared shader definitions**. Of those identities, 424 returned
nonempty partial projections, all with named unsupported parameters. Two refused:
an older signature-qualified OmniPBR identity used by nine definitions, and an OmniSurface
wrapper used by three definitions whose transitive modules are absent. This measurement
does not apply authored USD inputs, establish which definitions are bound or visible,
or qualify complete materials. UV transforms, tint, opacity, glass/volume controls and
other reported parameters still need actual material semantics and rendering evidence.
Independent Standards and Spec reviews found the same authored alias color-space defect.
The correction is closed by the reviewed binding change, old-adapter failure evidence,
all seven native CTests, and the rerun RTX HDR fixture. Repeating all 426 default calls
with the corrected adapter preserves every result record and module hash. This accepts
the bounded wrapper implementation only; no full-warehouse runtime promotion or fidelity
claim follows from it.

The resolver now accepts exact signature-qualified names by comparing the SDK's
formal parameter count and type names, with malformed or mismatched signatures still
refused. This fixes valid qualified-name resolution but does not make the warehouse's
legacy OmniPBR signature compatible: the loaded module has a different formal interface,
including reordered and additional inputs. The diagnostic reports that exact interface
instead of silently stripping the requested signature. The six eligible legacy requests
remain explicit refusals with the current core when compatibility routing is disabled.
The legacy profile above supplies a matching implementation for the opt-in route.
The optional OmniSurface profile resolves that separate
dependency refusal; the resulting material still has explicitly unsupported lobes.

### Actual authored material values

The native audit now records each shader input's typed composed value at default time,
its `colorSpace` metadata, and whether the value and metadata were authored. `valueStatus`
is `resolved`, `unresolved`, or `unsupported`; unresolved and unsupported counts are
explicit. Non-finite numeric values refuse the audit by attribute path rather than
producing invalid JSON. Connected inputs remain marked as connected: reading their
attribute value is not shader-graph evaluation.

The full warehouse has **29,337 resolved authored input values** and 154 unassigned asset
slots with no authored value. All observed value types are supported by this audit.
Among the declarations are 1,263 texture scales (many are 2x, 3x or 12x), 75 texture
rotations (seven are 90 degrees), 1,557 diffuse tints and 523 opacity-enable values.
These are actual source opinions, not inferred module defaults or a count of visible
material placements.

Passing those authored values and texture metadata to the existing adapter produces
**882 distinct requests across the 3,701 shader definitions**: 875 partial successes and
seven refusals. The refusals still represent the same nine legacy signature-qualified
OmniPBR definitions and three OmniSurface definitions. The adapter projects authored
`diffuse_tint` into constant color and diffuse-texture RGB scale. Remaining named
unprojected authored inputs include texture influence/scale and normal/detail controls.
Some controls are inactive or neutral, so their presence alone does not prove a visible
defect; each active semantic still needs projection and pixel qualification.

This is a stronger material inventory, not complete material fidelity, a bound/visible
material census or a warehouse render. The `openusd_warehouse_audit_contract` regression
checks exact scalar/vector/bool/int/string/token/asset values, source color-space metadata,
unassigned/blocked/unsupported values, connected authored fallbacks and non-finite refusal.

### Bounded collider preparation and dynamics

`tests\OpenUsd.SimReadyWarehouse.NativeProbe` exercises the existing public hierarchy and
bulk physics interfaces. Metadata-only extraction visits all 112,162 active prims in one
native traversal and reports all 26,834 colliders without truncation. However, requesting
the mesh data reaches the existing point/index limits: the 348,273,912-byte page reports
`truncationFlags = 384`, with 16,777,214 points and 33,554,430 indices and explicit capacity
diagnostics. **That is a failed full-physics admission, not a successful partial simulation.**

Even counting geometry once per native prototype/definition yields 24,500,305 collider
points and 107,242,957 authored face indices. Therefore prototype sharing alone, or raising
just one capacity constant, is not a complete fix. A coherent bounded collision-geometry
preparation/transport/cooking path is required. Authored convex decomposition must not be
silently replaced with a single convex hull.

Bounded windowed extraction, real multi-hull decomposition and exact geometry sharing now
close this static collider gap without increasing the page's point/index ceilings.
The complete detached input page is **139,719,792 bytes**, with 3,644,236 stored points,
20,844,096 indices and 2,052 shared hull ranges. Preparation took 614.38 seconds and
reported **1,708,740,608 bytes** peak job commit under a separate 2-GiB CPU-only profile.

The detached page then built **26,834 actual actors and 35,387 attached SDK shapes**.
Every source collider path, enabled state and instance-proxy status matches the original
audit: 25,066 authored convex hulls, 143 decompositions and 1,625 unapproximated meshes.
Native inspection found no missing, extra or duplicate actor/shape identity pairs.
Stepping and reset preserved the complete shape set. Scale-aware cooking retained 2,455
convex and 560 triangle mesh resources, rather than one resource per placement.
With explicit probe capacities of 32,768 rigid bodies and 65,536 colliders, the solver
run reported **2,063,052,800 bytes** peak job commit under the same 2-GiB ceiling.

Three instances of one hand-truck mesh contain only three non-axis-aligned points.
The original prepared page retains those exact raw inputs; world construction cooks them
using an interior centroid sample to satisfy the SDK's four-point construction requirement.
No source collider is removed or changed to another approximation. The SDK's normal
planar-hull regularization still applies; see [physics extraction](physics-extraction.md).

The page SHA256 is
`07d64823cc9fc975fc6c68f864acfc0c40e2ed35ca1866439065a623e48949cd`.
This is static collider construction and identity evidence, not proof of moving-prop
contact trajectories or full Viewer/MCP recording. The published source has no rigid bodies.
The CPU-only runs preserve one quarter of physical capacity and half initially available
commit; they do not relax the separate full-render admission or its 13-GiB process profile.
Optional NVIDIA GPU binaries remain local inputs and are not redistributed.

The removable box demonstration now has separate CPU execution evidence. Its layer makes
`SM_MultiDepthBox_A08_02` under `/World/Loading_Zone/Xform/sm_warehousecomposition_d01_01`
a 1-kg body, moves it above the existing `SM_MultiDepthBox_A08_08` support, and de-instances
only that selected placement. Geometry and material bindings remain those of the dataset.
Metres, kilograms, Z-up, gravity `(0, 0, -9.81)`, initial upward velocity `2 m/s`, and a
60-Hz, 240-step timeline are explicit. These are demonstration inputs, not calibrated
manufacturer properties.

All 26,834 original colliders remain present as 35,387 actual SDK shapes. The selected
three colliders share one moving actor, giving 26,832 actors overall. The body rises
0.18745 m, drops onto the existing support and settles to zero linear speed. Every fixed
step is checked against the actual solver clock; reset restores position, orientation
and initial velocity without changing the shape identities. This complete-world run
reported 2,041,683,968 bytes peak job commit under the same 2-GiB process/tree profile.

Reopening the original source before and after the scenario yields the same whole-source
physics fingerprint and no rigid bodies. The fingerprint excludes SDK-assigned transient
prototype ordinals, which otherwise change after an equivalent reload. All 8,804 original
files were reverified. Compound-body recording, cancellation/retry and source restoration
also pass on bounded native MCP and Viewer fixtures; a full-warehouse rendered recording
and its final presentation are still separate acceptance gates.

Create the same removable demonstration outside the source tree:

```powershell
python .\eng\create-simready-box-scenario.py `
  --localized-root D:\simready\warehouse-01\cache\localized-v3\physical_ai_simready_warehouse_01.usd `
  --output D:\simready\warehouse-box-drop.usda `
  --focused-camera
```

The generator refuses existing outputs. It does not replace the inventory verification or
localization steps above. `--focused-camera` optionally appends `/WarehouseDemoCamera`,
looking at the selected box stack with a 45-degree vertical field of view, a 16:9 aperture
and clipping range 0.05..20 metres. This is a focused view of the complete composed root,
not the earlier overview camera or a geometry/visibility filter. Without the option, the
generated scenario layer retains its previous content. The CPU qualification above predates
this camera addition; it is not acceptance of a full-root image or host recording.

The generated camera now has separate native qualification at times 0, 120 and 240.
The test opens a camera-only population mask, requires `/World` to be absent, and checks
the exact pose, target direction, perspective aperture, field of view and clipping range.
This proves the generator's camera state without loading warehouse geometry; full-root
rendering and recorded camera presentation remain unqualified.

The JSON result includes `cameraPath` and the explicit `stageSettings` needed by MCP
`open_scene`. Copy the latter object into that request: source-sublayer root metadata is
not inherited by a new MCP workspace. Use the returned `cameraPath`, `fullScenePhysics: true`,
`expectedMetersPerUnit: 1`, and `expectedUpAxis: "Z"` for `render_physics_sequence`.
Use 1920x1080 for the authored camera's 16:9 frame. No resource limits change.

Open the generated layer with the Viewer's `--full-scene-physics` and
`--camera /WarehouseDemoCamera` options; remove the demonstration by opening the
original localized root without the scenario layer.

MDL wrappers importing OmniPBR/OmniGlass require qualified material handling; an
empty/default material is not full warehouse rendering. Effective intensity, exposure,
color and inherited-visibility inspection reduces the 553 light prims to **98 visible,
nonzero lights: 96 rectangle, one distant and one dome**. The 97 contributing direct lights
fit the bounded 128-direct-light profile in hdSilk page ABI 24. Native selection filters
inherited-hidden and zero-output direct lights before admission, and refuses the complete
sync above the limit rather than returning a prefix. Real RTX 5070, WARP and Vulkan
fixtures preserve high light-link bits on meshes and instances at 97 and 128 lights.
Overflow recovery also retires departed link, shadow and dome-environment state even
when the replacement scene has the same number of lights. This qualifies that bounded
lighting transport, not full-warehouse pixels or the previously unsupported shadow models.

The first full-root MCP preview attempt opened the prepared scene successfully, but did
not produce a frame. An external eight-GiB private-memory guard stopped only that owned
host process during `render_preview`; its sampled peak was 8.57 GiB private memory and
9.13 GiB working set. This is a failed resource admission, not an image or an in-renderer
memory-bound guarantee. All 8,804 original files were reverified afterward.
Subsequent native-only isolation reproduces the growth without MCP, GPU resources or
MDL evaluation. It occurs during Hydra synchronization, before page construction or
managed copying. Prototype payload sharing already exists.

The shared-corner candidate compacts byte-identical vertex attributes with their original
point identities when normals are available, without dropping geometry or smoothing an
implicit normal fallback. A 128x128 grid drops from 98,304 emitted vertices to 16,641,
while retaining all 98,304 indices and identical textured pixels. Full-root synchronization
still exceeds the diagnostic memory guard, so this is a verified representation improvement,
not complete resource admission or a warehouse image.

Further private instrumentation attributes roughly 3.1 GB of a 4.3 GB full-root sample
to retained Rprim/scene-record arrays; the temporary corner hash peaks at about 28 MB
in that run. Rprims now borrow active coarse/refined topology tables, and published
attributes share copy-on-write storage instead of duplicating every attribute buffer.
Triangle indices and face/point/edge identity tables also share their producer's storage.
Mutation detaches only changed arrays so earlier records remain unchanged. The controlled grid's
complete 1,340,590-byte command page is byte-identical across these ownership changes.
These are measured representation/lifetime improvements, not a complete warehouse memory
budget. Remaining retained arrays, USD/Hydra caches and later publication peaks still
need admission. Raising the guard or dropping scene content is not a full-scene fix.

The later r17 capture reached all 6,838 shared geometry uploads and emitted all 27,389
source identities; the independent source oracle matches every placement and world
transform. It still produced no accepted image. CPU texture preparation refused a
268,763,232-byte UDIM atlas while 268,537,856 bytes were already reserved against the
512-MiB pool. A separate host commit-headroom guard was recorded during teardown, and
the raw Windows job peak of 13,959,450,624 bytes slightly exceeded the configured
13-GiB limit; neither observation is hidden by the texture diagnosis.

Streamed UDIM preparation now assembles admitted row bands rather than overlapping all
decoded tiles with another complete CPU atlas. Single/sparse byte and float controls
preserve metadata, source row orientation, fallback cells, gutters and rendered output,
including cancellation and failed-upload replay. The original `book_open_a` four-tile
4096x4096 PNG sets reproduce the same requested atlas size on one quad without full-scene
topology. Removing the CPU overlap exposes another blocker: D3D12 rejects the resulting
16,392x4,099 texture at resource creation. At that stage, repacking alone did not establish
that all simultaneously active book maps fit the unchanged GPU pool. The subsequent bounded
book qualifications address GPU layout and actual texture-coordinate residency without
resolution reduction, map omission or a larger quota.

The subsequent bounded source-book run preserves all three original meshes and their
active maps. Conservative primitive UV reachability, dense tile lookup, exact scalar
storage and constant-tile representation fit the GPU working set without changing draw
order. Removing the native decoder's duplicate source vector also avoids the observed
codec allocation refusal under the same 1.5-GiB diagnostic process ceiling. The complete
book run records 218,103,808 CPU pixel bytes, 503,230,647 GPU texture bytes and 16,589,568
staging bytes at peak, with no texture fallbacks. This is original-book rendering evidence,
not full-warehouse, full-BSDF or physics acceptance.

For Windows MCP work, explicitly use `OPENUSD_MCP_USE_WARP=false` instead of its
deterministic software default. This does not choose a named GPU. D3D12 capabilities now
report the observed DXGI name and software flag. The small HDR wrapper and high-light
fixtures exercised the RTX 5070. Full-warehouse resource admission and rendering still
require their own device-specific evidence.

The original warehouse remains unchanged. The separate scenario adds dynamics only to
the selected movable box placement, with explicit mass, inputs and de-instancing.
Structures remain static. All required authored colliders are accounted for independently
of render visibility and purpose in the CPU qualification.

Viewer and MCP simulation recording share a fixed-step physics -> complete publication
-> applied render overrides -> coherent capture path, verified on bounded native fixtures.
Existing authored-time image sequences are not equivalent to stepping physics.
Full-scene rendered collider coverage, material fidelity, resource bounds, source preservation
and actual full-root host execution remain completion gates.

## Tooling regression checks

```powershell
python -m unittest .\eng\tests\test_simready_warehouse.py -v
python -m unittest .\eng\tests\test_simready_mdl.py -v
python -m unittest .\eng\tests\test_simready_scenario.py -v
```

These tests require no dataset or native runtime. They cover exact inventory and storage
admission, path/metadata refusals, interrupted/resumed transfers, source preservation,
create-only publication, Git/LFS hashes, snapshot locking, unchanged default scenario content
and the optional camera's right-handed look-at transform, optics and MCP root settings.
They run through the existing
`eng\tests` discovery in `ci.yml`. They are not full-warehouse rendering evidence.
Fixtures resolve their OS-created temporary directories to physical paths before using
the acquisition interface, including macOS's `/var` alias. Caller-supplied data roots
still reject symlink/reparse ancestors; fixture normalization does not relax that policy.

The native `openusd_warehouse_audit_contract` and
`openusd_warehouse_localization_contract` CTests use repository-owned fixtures. They cover
instance-expanded collider counts, unknown authored schema tokens, time samples, inactive
payloads, exact physics-material remapping, source-file preservation, no cache overwrite,
dependency parse-error refusal and UDIM anchoring. These small tests do not download or
render the warehouse.

[slang-mdl-test-materials]:
https://github.com/shader-slang/MDL-SDK/tree/446562493bed1b175e7cf105425ca3596764652f/examples/mdl/nvidia/test-materials
[gatling-mdl]: https://github.com/pablode/gatling/tree/4e2be6758a004876fb558ffee684f67c03197439/src/gi/mdl
