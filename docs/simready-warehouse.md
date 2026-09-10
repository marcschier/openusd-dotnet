# SimReady Warehouse-01

This integration targets the complete published warehouse in the OpenUSD Viewer and MCP,
without a mandatory Isaac Sim/Kit runtime. The pinned snapshot has been acquired and
verified, and complete native composition/physics audits are available. Full-scene rendering,
material qualification and the physics demonstration are not yet accepted.

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
  -ReportPath <absolute-new-audit-json>

.\eng\inspect-simready-warehouse.ps1 `
  -Operation Localize -DataRoot D:\simready `
  -AuditExecutable <absolute-path-to-openusd_warehouse_audit_probe.exe> `
  -NativeRuntimeRoot <absolute-matched-runtime-root> `
  -CacheRoot D:\simready\warehouse-01\cache\<new-cache-name> `
  -ReportPath <absolute-new-localization-json>
```

The wrapper is currently Windows-specific and revalidates the original snapshot before
execution. Audit JSON includes the pinned inventory, profile, executable and SDK identities.
An incomplete audit returns an explicit nonzero outcome while retaining its diagnostic
report. Localization reports remaining dependencies separately. Neither operation starts
a renderer or solver.

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
  -ReportPath <absolute-new-audit-json>
```

This sets the standard USD asset search path only for the audit process and restores the
caller's environment. It verifies the module profile before use. USD dependencies now
resolve, but nested MDL wrapper evaluation remains a separate requirement. In particular,
the corpus's OmniSurface wrappers and nontrivial GlassWithVolume/helper graphs are not
covered by the PBR/glass defaults proof; OmniSurface's transitive implementation modules
have not yet been qualified.

### Bounded SDK wrapper qualification

The SDK-backed adapter now also projects SDK-proven material variants (`(*)`) rooted in
OmniPBR or OmniGlass. This uses the SDK's prototype, argument and expression relationships,
not a text scan of MDL source. Wrapper constants, parameter aliases, authored-value
precedence and resolved texture ownership are checked through the existing C ABI.
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

### Actual physics capacity gap

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

The pinned PhysX 5.5.0 SDK and project-owned runtime now build locally, and the real native
ABI/retained-world/CPU-domain probes pass. That proves the runtime, not the warehouse's
full collider coverage. Optional NVIDIA GPU binaries remain local inputs and are not
redistributed.

MDL wrappers importing OmniPBR/OmniGlass require qualified material handling; an
empty/default material is not full warehouse rendering. Effective intensity, exposure,
color and inherited-visibility inspection reduces the 553 light prims to **98 visible,
nonzero lights: 96 rectangle, one distant and one dome**. The 97 contributing direct lights
still exceed the current eight-light transport/shader profile; hidden lights must not be
counted as a requirement, nor may the first eight be treated as the whole scene.

For Windows MCP work, explicitly use `OPENUSD_MCP_USE_WARP=false` instead of its
deterministic software default. This does not choose a named GPU. D3D12 capabilities now
report the observed DXGI name and software flag, and the small HDR wrapper fixture
exercised the RTX 5070. Full-warehouse
resource admission and rendering still require their own device-specific evidence.

The original warehouse will remain unchanged. A separate scenario will add dynamics only
to selected movable asset placements, with explicit masses, inputs and any necessary
de-instancing. Structures remain static. All required authored colliders must be accounted
for independently of render visibility and purpose.

Viewer and MCP simulation recording will share a fixed-step physics -> complete publication
-> applied render overrides -> coherent capture path. Existing authored-time image sequences
are not equivalent to stepping physics. Full-scene/collider coverage, material fidelity,
resource bounds, source preservation and real native execution remain completion gates.

## Tooling regression checks

```powershell
python -m unittest .\eng\tests\test_simready_warehouse.py -v
python -m unittest .\eng\tests\test_simready_mdl.py -v
```

These tests require no dataset or native runtime. They cover exact inventory and storage
admission, path/metadata refusals, interrupted/resumed transfers, source preservation,
create-only publication, Git/LFS hashes and snapshot locking. They run through the existing
`eng\tests` discovery in `ci.yml`. They are not full-warehouse rendering evidence.

The native `openusd_warehouse_audit_contract` and
`openusd_warehouse_localization_contract` CTests use repository-owned fixtures. They cover
instance-expanded collider counts, unknown authored schema tokens, time samples, inactive
payloads, exact physics-material remapping, source-file preservation, no cache overwrite,
dependency parse-error refusal and UDIM anchoring. These small tests do not download or
render the warehouse.
