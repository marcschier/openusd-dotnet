# Shader pipeline scaffold

This tree owns an offline-capable build-time shader toolchain. It does not add a
runtime compiler dependency.

Pinned inputs:

- Slang 2026.19 at `0fb6b7587a94fa33d88141ec9930766eedc6f59a`.
- SPIRV-Tools `v2026.4` at `ef96ed763b43b59b33b31b362f09a02b729fa1c9`, with the matching
  SPIRV-Headers `496543121ce6419f23d6fa5d7194ba66c36212d2` from that release's `DEPS`.
- Vulkan 1.2 / SPIR-V 1.5, Shader Model 6.0, and Metal 2.4.
- Xcode 16.4 for the macOS-only metallib gate.

All network inputs and SHA-256 hashes are in `toolchain.lock.json`. Downloaded
archives, extracted tools, build trees, and transient shader outputs are
ignored. Reviewed runtime inputs under `checked` are intentionally committed.

From a PowerShell 7 developer shell:

```powershell
./eng/shaders/fetch-toolchain.ps1
./eng/shaders/build-toolchain.ps1
./eng/shaders/build-shaders.ps1
./eng/shaders/verify-reproducibility.ps1
./eng/shaders/update-checked.ps1
./eng/shaders/verify-checked.ps1
```

After the first command has populated the cache, extraction can be repeated
without network access:

```powershell
./eng/shaders/fetch-toolchain.ps1 -Offline -Force
```

`build-shaders.ps1` emits DXIL, SPIR-V, MSL text, normalized reflection JSON,
the exact executed argument plan, and an artifact hash manifest. Profiles,
capabilities, validator environments, Metal language selection, and Xcode
selection are all derived from `toolchain.lock.json`; manifest profiles that do
not match the locked Shader Model are rejected. Linux validation uses
`-ArtifactScope Spirv`, which emits and records only the declared SPIR-V targets
and does not require DXC. macOS validation uses `-ArtifactScope Metal`, which
emits only the MSL text and the compiled `metallib`, because DXIL emission loads
the `dxcompiler` and `dxil` libraries that exist only on Windows. Both
restrictions are enforced by the script and asserted by
`validate-workflow-paths.ps1`.

Mesh shader permutations are generated from the manifest-declared `featureBits`.
Names are deterministic: the empty feature set keeps the base program name for
compatibility, while feature variants append manifest-ordered tokens joined with
`+`, for example `mesh.fragment.uv+material+normal`. `MAP_*` bits require
`HAS_UV`, so the mesh fragment family expands to five fetch-topology variants. The
vertex family only varies on `HAS_UV` and `MAP_NORMAL`, so it expands to three
variants because normal mapping requires UVs and is the only texture feature that
changes vertex outputs. The manifest also declares hard budgets of 8 fragment
and 8 vertex permutations for the mesh family; plan generation fails when an
expanded family exceeds its budget. Arithmetic-only material choices remain
uniforms and are intentionally not feature bits.

The joint density-volume camera pass declares `volume.camera.vertex` and separate
`volume.camera.fragment.atlas` and `volume.camera.fragment.uniform` programs.
Their fragment wrappers include `volume.camera.fragment.shared.slang`, so clipping,
field-local extinction and overlapping-field source integration use the same code.
Fullscreen coverage includes rays whose volume-box faces lie beyond both clip planes.
The atlas variant binds one guttered R32Float 3D texture; the uniform variant declares
neither that texture nor its sampler and also serves backends without 3D sampling.

The source layout has a 32-byte parameter buffer `b0`, field/frame storage `t6`/`t8`,
atlas `t9` with sampler `s4` at Vulkan bindings 9/13, and opaque depth `t28` at
Vulkan binding 45. The captured background uses `t0` at Vulkan binding 2 and
Metal texture index 0. The pass does not use environment bindings 46/47 or
`t29`/`t30`, and the shared native frame ABI is unchanged. Admission permits at
most eight active fields; zero-opacity fields do not consume those slots. Sampled
field axes are 1–512 voxels, and one-voxel repeated-boundary gutters isolate atlas
regions. The existing Z stack is retained when it fits; otherwise a deterministic
portable grid keeps every atlas axis at or below 2,048. The managed payload is
limited to `int.MaxValue` bytes, and device limits and CPU/GPU reservations also
apply. A packable atlas does not imply that the sampling workload is admitted.

The two phases use identical integer midpoint sampling lattices. For `N` active
fields and `A` equal to the sum of their longest sampled axes, with axis 1 for a
uniform or fallback field, each phase is bounded by `P = 2 * A * (2 * N - 1)`
positions. Both phases together are bounded by `E = 2 * N * P` field evaluations
per pixel. Admission refuses `P > 7,117` or `E > 64,040` before `CreateResources`
and atlas or depth-target allocation; frame-area multiplication is also checked.
Two fields with longest axes of 512 give `P = 6,144` and `E = 24,576` and pass
those per-pixel bounds; three or eight such fields are refused. Sampling is not
clamped.

CPU range checks address finite-endpoint ray-length overflow and
inverse-transform/eye-coordinate product overflow. Collapsed ray normalization,
near-parallel slab or clip arithmetic, and far-origin cancellation remain
unqualified. Validation refuses non-finite active `VolumeDensity` values and
copied R32 samples. It also bounds the maximum absolute sample, the nonnegative
density scale and their product to `float.MaxValue / 32`. A retained atlas stores
the sample maxima and subnormal-sample flag with its CPU reservation and checks
them against the current scale, opacity and workload before reuse. For fields
that can contribute, admission refuses subnormal density scales, opacities or
samples. It also refuses a unit-ray optical depth below the smallest normal float
times the maximum steps in a segment. This bound does not establish a lower
bound for shorter clipped rays or interpolated samples. Admission also refuses
a non-finite exposure or tint, an exposure scale above `float.MaxValue`, or a
nonnegative tint/scale product above that limit. When a display transform is
requested, it uses the larger of 1 and `2^exposure` because rendering can fall
back to the direct path. The shader composites the exposure-adjusted volume
source against the captured background without fixed-function blending. The
admission check can refuse a final composite that would fit. The ray, local-step,
tint and exposure arithmetic still lacks a finite-domain proof for every
admitted input.

Both fragments use precise floating-point mode. Each Xcode Metal compile also
receives `-fno-fast-math` for a program marked `precise`, preserving that choice
in the second compiler stage. For each ray and field, the density pass tracks
the maximum sampled density and integrates local step
lengths weighted by density divided by that maximum. It derives field optical
depth from the normalized integral, so a large raw density is not multiplied by
a thin local step before normalization. The shader caps a field's optical depth
at `float.MaxValue / 16`, leaving room to sum all eight fields without
overflow; the cap exceeds the maximum expected under the current density-product
bound and a unit-box chord.

When total field optical depth is at most `0.001`, the shader derives color from
the integrated tint weights instead of accumulating per-step alpha. The
per-step depths can be subnormal even when the complete ray produces a visible
color on Metal. This path omits within-ray ordering only in the range where the
relative color difference is below `0.101%` for nonnegative tint. The previous
hosted Metal run failed the thin-field and high-tint cases at densities
`1e-37`, `1e-20`, and `1e30`; D3D12 and Vulkan passed their high-tint cases.
A later provisional source passed ten overlapping high-tint Metal cases and 16
of 17 targeted composition cases; the remaining case failed before rendering.
Its complete camera gate and the current source remain to be qualified.

Source preflight refuses a volume material with additional non-density textures,
duplicate density entries, or a composited density texture before publication.
`RenderCore` disposes a volume candidate only if it created it; `ApplyPageCore`
keeps a staged candidate through commit or rollback. The Windows camera gate
exercises preflight refusal, preservation of the prior scene and output, and
corrected retry. It cannot reverse a final output copy that already succeeded.
Transparent surfaces are not depth-interleaved with volume transport; physical
density-volume shadow transport is a separate limitation.

All four sources, including the shared include, are provenance inputs in
`shader_model.py`; its changes also require regenerating and verifying the
`FlatOrdinalShaders` checked receipt. An earlier Windows run regenerated and
verified the checked shader payload and receipt. Its 114-case camera gate
passed on D3D12 WARP and Vulkan SwiftShader, including snapshot rollback,
admission and retirement cases. These results do not qualify later source
changes. The macOS metallib gate, Linux and Metal camera execution, and the
native producer-to-renderer chain remain to be qualified.

The earlier sampled density volume retains a separate single-permutation mesh family:
`mesh.volume.vertex` supplies camera-ray and volume-local transform data, while
`mesh.volume.fragment` declares the 3D density texture, its sampler, and the
opaque-depth snapshot texture. These are not mesh permutation variants. Its
sampled-volume binding layout declares these resources, and a D3D12 root signature
must cover every resource its shader binary references, so those resources cannot
live in the shared fragment binary that ordinary mesh pipelines use. Programs select
the preprocessor switch through a manifest `defines` object, validated against
the `DEFAULT_DEFINES` set in `shader_model.py`; the value is applied identically
to DXIL, SPIR-V, and MSL, so a resource can never exist in one backend's binary
and be absent from another's.

Normal-map permutations additionally bind an independent detail-normal texture at
the otherwise unused volume texture/sampler registers (`t9`/`s4`, Vulkan bindings
9/13). The source refuses simultaneous volume and normal-map defines. This keeps
the full material path within 16 fragment samplers without borrowing the emission
composite. Detail samples use their own UV affine and tangent orientation, then
combine with the primary normal through per-fragment reoriented normal mapping.
The surface block grew to 304 bytes with page ABI 30; the rows at offsets 240 and
256 carry the detail affine, followed by alternate UV rows at offsets 272 and 288. An
exactly antipodal primary normal uses a half-turn about tangent X, resolving the
shortest-arc ambiguity without division by zero.

Reflection schema 2 preserves separate D3D register/space and Vulkan
set/binding contracts, resource access and shape, arrays and strides, recursive
struct/matrix layout, and stage I/O semantics. System values have a null
location. Normalized access is mandatory; Slang's omitted raw access for a
read-only texture is normalized to `read`, while other omissions are rejected.
Compute group sizes must be three positive dimensions, and matrix stride follows
the locked row-major or column-major layout, including rectangular matrices.
Missing required resource or user-varying bindings are errors.

`update-checked.ps1` is a Windows win-x64 authority gate. It reconstructs the
toolchain from verified archives, rebuilds every deterministic artifact, strips
debug information and line directives, and replaces `checked` only after the
complete build succeeds. By default it preserves verified download archives;
use `-RefreshDownloads` to test a network-clean fetch, or `-Offline` to prove
that the cached archives are sufficient.

`verify-checked.ps1` never downloads. It rebuilds into `.cache`, captures the
same generated command plan used by the compiler, and requires byte-for-byte
equality with every checked DXIL, SPIR-V, MSL, reflection, and manifest file.
The checked manifest hashes source files, the lock, the shader manifest, and
the scripts that generated and validated it. Every required checked text input
must use LF-only line endings before publication.

SPIR-V verification also caps mesh-fragment numeric invocation-local storage at a 1024-byte
static lower bound per function, including private globals. Large frame-light arrays must be
indexed in the existing raw storage buffer rather than materialized per fragment. This guards
against the previously observed 14-KiB light-array copies and their large Vulkan pipeline
allocation without lowering the 128-light limit or changing the frame-buffer layout.

The shader workflow runs only when this tree, its documentation, or the
workflow changes. Windows x64 is the byte-authoritative checked-artifact gate.
Linux x64 independently generates, validates, and reproduces only the declared
SPIR-V targets, then structurally checks the full committed payload and validates
committed `checked/*.spv`. macOS arm64 selects the locked Xcode, compiles all
checked MSL files to AIR, links exactly one packaging-ready
`mesh.metallib` containing every declared shader entry, and writes
`eng/shaders/out/checked-payload/osx-arm64/metallib-manifest.json`. Corruption
tests prove that checked-only SPIR-V or MSL changes are rejected. Same-host
reproducibility never implies cross-host byte identity. CI caches verified
downloads only and records compiler, CMake, Ninja, Python, and Xcode versions.

Checked MSL validation binds `[[vertex]]`, `[[fragment]]`, or `[[kernel]]`
directly to the expected function declaration. Metallib validation accepts only
an exact exported function symbol, not a helper or prefix/suffix match. The
metallib manifest records repository-relative commands and paths, symbol-dump
hashes, and hashes of the Xcode selection, payload validation, and checked
packaging scripts. Roots outside the repository are rejected.

The sidecar uses exact schema version 4. It permits only the locked top-level
and library fields, duplicate-free source/AIR/entry records for each expanded
manifest program, one compile command and one exact symbol check per entry, one
combined link and inspection command, the final library and symbol-dump
hashes/sizes, and the complete centralized provenance set.
`scripts/metal_sidecar.py` is the shared generation, staging, and package
validator. It rejects missing or extra records and fields, duplicate or
mismatched mappings, absolute or traversing paths and command arguments, and
hash or size drift. Metal `Pack` invokes this validator before NuGet package
emission with checked-file verification enabled, so corrupt or stale staged
pairs cannot produce a package.

The macOS validator stages the validated combined library at
`eng/shaders/checked/mesh.metallib`, the exact path consumed and packaged by
`OpenUsd.Rendering.Silk.Metal`, plus its validation manifest at
`eng/shaders/checked/mesh.metallib.manifest.json`. Both staged files are ignored
and must never be committed from Windows. macOS builds and packs fail when
either is absent, and the package gate byte-compares both native package assets
with the staged artifacts after the project-level validator succeeds.

`OpenUsd.Rendering.Silk.Metal` defaults
`OpenUsdRequireMetalShaderLibrary=false`, so ordinary cross-platform compile
and analyzer builds remain shader-binary independent. Metal execution,
conformance, and package workflows set the property to `true`, which makes a
missing staged library an error. Every `Pack` additionally requires macOS, the
opt-in property, and the validated combined library; Metal package production
is macOS-only.

Windows win-x64 is authoritative for the committed deterministic artifacts.
Cross-host Slang output is not assumed to be byte-identical. During release
packaging, macOS osx-arm64 with exactly Xcode 16.4 compiles the checked mesh,
pick, selection, and compute MSL files using Metal 2.4 and replaces those
runtime inputs with the single combined `mesh.metallib`. Metallib output is not generated or committed
by the Windows gate.

Checked manifest provenance is an exact set defined in `shader_model.py`.
Validation rejects omitted, unexpected, or duplicate input records before
checking their hashes.

`prepare-metal-library.ps1` is the workflow entry point for macOS consumers. It
selects locked Xcode 16.4, builds and validates the combined library, verifies
the manifest and stage hashes, and leaves the library at the project-consumed
path. Shader CI runs Metal conformance and package verification with the
required property. Package CI prepares the same artifact before its required
macOS execution gate. Regular CI and the macOS OpenGL render proof explicitly
remain compile-only for the Metal RHI.

Run the shader-specific Python test with:

```powershell
python -m unittest discover eng/shaders/tests
```
