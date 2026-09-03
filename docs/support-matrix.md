# Support matrix

This matrix describes the implementation and declared repository gates for the public
`0.12.0-alpha` baseline. It is not a 1.0 compatibility promise and does not claim that every
workflow is currently green.

## Status terms

| Term | Meaning |
| --- | --- |
| Implemented | Production source and focused tests exist |
| Workflow-gated | A repository workflow defines required automated execution |
| Compile-only | The host compiles the path but does not execute it in the ordinary CI job |
| Pending hosted proof | Source/contracts exist, but current docs still mark hosted execution pending |
| Implemented, not gated | Source exists, but no accepted automated reference gate proves the behavior |
| Excluded | Deliberately outside the locked runtime or project scope |
| Unreachable | A closed, unavailable, or incompatible upstream path cannot be implemented here |
| Not supported | The current API rejects or does not expose the capability |

Workflow badges in the [root README](../README.md) provide the current run status for the default
branch.

## Distribution and stability

| Item | Current state |
| --- | --- |
| Version | `0.12.0-alpha` |
| Repository visibility | Public |
| API compatibility | Pre-1.0; public APIs may change |
| Package identity stability | Pre-1.0; IDs may change |
| Public NuGet feed | Published to NuGet.org via OIDC trusted publishing |
| Public GitHub Packages feed | Published alongside each GitHub release |
| Source build | Supported |
| CI/local-feed package build | Implemented |
| Default managed build includes native OpenUSD | No |
| Live-authoring boundary | Source-only sample; not a published package |
| MCP server | `OpenUsd.Mcp.Tool` net10.0 tool; source runner and self-contained local RID bundles |

See [Getting started](getting-started.md) for the supported source workflow and
[Packaging](packaging.md) for package production and clean-consumer evidence. Compatibility rules are
centralized in [Versioning and compatibility](versioning-compatibility.md).

## Packages

All package projects below are packable from source. Availability here means repository production,
not public-feed publication. All 27 IDs other than `OpenUsd.LiveAuthoring`,
`OpenUsd.Bridge.Protocol`, `OpenUsd.Bridge.Grpc`, and `OpenUsd.Viewer.Bridge.Grpc` are published to
NuGet.org
at `0.12.0-alpha`; the five Cesium IDs became public after being withheld at `0.5.0-alpha`.
`OpenUsd.LiveAuthoring`, the two optional Omniverse bridge IDs, and the optional Viewer bridge
integration are newly added to `eng/pack-packages.ps1`'s published set and ship starting with the
next release.

| Package | Framework project | Native requirement |
| --- | --- | --- |
| `OpenUsd.Interop` | 8/9/10 | Native library only when invoked |
| `OpenUsd` | 8/9/10 | Core runtime |
| `OpenUsd.LiveAuthoring` | 8/9/10 | Core runtime when its executor creates or opens a stage |
| `OpenUsd.Bridge.Protocol` | 8/9/10 | None; a transport-neutral wire model with no networking dependency |
| `OpenUsd.Bridge.Grpc` | 8/9/10 | None; an optional gRPC client for an externally owned Kit peer |
| `OpenUsd.Rendering` | 8/9/10 | None for neutral contracts |
| `OpenUsd.Rendering.Storm` | 8/9/10 | Imaging runtime |
| `OpenUsd.Cesium` | 8/9/10 | Cesium runtime |
| `OpenUsd.Rendering.Silk` | 8/9/10 | Imaging runtime for scene pages |
| `OpenUsd.Rendering.Silk.D3D12` | 8/9/10 | Imaging runtime; Windows execution |
| `OpenUsd.Rendering.Silk.Vulkan` | 8/9/10 | Imaging runtime; Windows/Linux execution |
| `OpenUsd.Rendering.Silk.Metal` | 8/9/10 | Imaging runtime; macOS execution |
| `OpenUsd.Viewer` | 8/9/10 | Backend runtime of the renderer it activates |
| `OpenUsd.Viewer.Bridge.Grpc` | 8/9/10 | None; optional Viewer-to-bridge adapter, host-configured |
| `OpenUsd.Mcp.Tool` | 10 tool | Core/Imaging runtime required for scene and render operations |
| `OpenUsd.Runtime.Core` | `net8.0` carrier | Core metapackage for all supported RIDs |
| `OpenUsd.Runtime.Core.win-x64` | `net8.0` carrier | Windows x64 native install |
| `OpenUsd.Runtime.Core.linux-x64` | `net8.0` carrier | Linux x64 native install |
| `OpenUsd.Runtime.Core.osx-arm64` | `net8.0` carrier | macOS arm64 native install |
| `OpenUsd.Runtime.Imaging` | `net8.0` carrier | Imaging metapackage for all supported RIDs |
| `OpenUsd.Runtime.Imaging.win-x64` | `net8.0` carrier | Matching Windows Core package |
| `OpenUsd.Runtime.Imaging.linux-x64` | `net8.0` carrier | Matching Linux Core package |
| `OpenUsd.Runtime.Imaging.osx-arm64` | `net8.0` carrier | Matching macOS Core package |
| `OpenUsd.Runtime.Cesium` | `net8.0` carrier | Cesium metapackage for all supported RIDs |
| `OpenUsd.Runtime.Cesium.win-x64` | `net8.0` carrier | Windows Cesium shim |
| `OpenUsd.Runtime.Cesium.linux-x64` | `net8.0` carrier | Linux Cesium shim |
| `OpenUsd.Runtime.Cesium.osx-arm64` | `net8.0` carrier | macOS Cesium shim |

The runtime projects use `net8.0` to carry RID assets and transitive build targets. The managed
libraries they support target all three production frameworks. `OpenUsd.LiveAuthoring` moved from
`samples/` to `src/OpenUsd.LiveAuthoring` once its data-model and admission/observability contracts
were productized (bounded arrays/matrix/vector attribute values, explicit clear/API-schema updates,
opaque correlation/origin tracking, an admission receipt separate from the applied result, and
structured health snapshots/events); see [Live authoring](live-authoring.md). Its recovery contracts
(explicit session states, one authoritative remote epoch, fingerprint-backed duplicate/replay rules,
loop prevention, and bounded full-snapshot replacement of a bridge-owned overlay) are now implemented in
the same package. Network transport — a versioned wire contract and its gRPC or WebSocket adapter —
remains a later phase and is out of scope for this package.

## Target frameworks

| Surface | `net8.0` | `net9.0` | `net10.0` | Validation |
| --- | :---: | :---: | :---: | --- |
| Packable managed libraries | ✅ | ✅ | ✅ | Multi-TFM build and tests |
| Public API analyzer baseline | ✅ | ✅ | ✅ | Production managed libraries |
| AOT, trim, single-file analyzers | ✅ | ✅ | ✅ | Enabled for production libraries |
| `OpenUsd.LiveAuthoring` | ✅ | ✅ | ✅ | Package-consumer live-authoring adapter |
| Viewer library | ✅ | ✅ | ✅ | Embeddable `OpenUsd.Viewer` package |
| Viewer desktop application | — | — | ✅ | `OpenUsd.Viewer.App` entry point |
| MCP .NET tool | — | — | ✅ | `OpenUsd.Mcp.Tool`; command `openusd-mcp` |
| Executable samples and probes | — | — | ✅ | Development and evidence projects |
| Runtime asset package projects | Carrier | — | — | Assets are RID-specific |

Repository development uses SDK `10.0.301`, even when compiling `net8.0` and `net9.0` targets.

## Runtime identifiers

| RID | Native build | Core package | Imaging package | Package gate | Render gate |
| --- | --- | --- | --- | --- | --- |
| `win-x64` | Implemented | Implemented | Implemented | Workflow-gated | Workflow-gated, partly narrowed |
| `linux-x64` | Implemented | Implemented | Implemented | Workflow-gated | Workflow-gated, partly narrowed |
| `osx-arm64` | Implemented | Implemented | Implemented | Workflow-gated | Workflow-gated, partly narrowed |

The narrowed render proofs are the Windows Avalonia Vulkan composition gate, the Linux X11 and
Wayland Vulkan import smokes, and macOS CGL Storm-to-Metal parity when hosted CGL cannot create the
required pixel format. Each needs a graphics capability a hosted runner does not provide, each
records a `status: skipped` evidence artifact instead of a silent pass, and each has a documented
route back to full coverage in [Testing](testing.md#render-gate-capability-limits). The two Vulkan
composition limits are hardware limits, not implementation gaps: hosted Windows has no system Vulkan
ICD and SwiftShader implements neither `VK_KHR_external_memory_win32` nor
`VK_KHR_external_semaphore_win32`, so it cannot export a Vulkan image to a D3D11 shared handle;
hosted Linux reaches lavapipe, but the X11/Wayland compositor reports
`supported image handles: (none)`, so no external Vulkan image can be imported.

No runtime package is currently defined for Windows arm64, Linux arm64, macOS x64, or mobile/browser
RIDs.

The locked viewer-standard native profile enables USD validation, Imaging/USD Imaging, Storm,
MaterialX, OpenImageIO, OpenColorIO, oneTBB, OpenGL, Ptex, OpenVDB, Alembic, and Draco. Python,
usdview, tests, examples, tutorials, tools, documentation, Embree CPU ray tracing, and RenderMan
remain out of the native runtime; Python is intentionally not a runtime dependency.

### Permanent reachability boundaries

The project pursues `usdview` and Omniverse parity where the necessary APIs are open and reachable.
The limits below distinguish that from work that is merely not built yet.

- **Omniverse RTX, Carbonite, omni.ui, Kit, Nucleus, and OptiX are unreachable.** They are closed
  NVIDIA platform pieces, not open standards this runtime can ship or reimplement.
- **MDL SDK is reachable and integrated behind an optional adapter.** The pinned baseline in
  `eng/mdl.lock.json` is NVIDIA MDL SDK 2026.0.2 (tag commit
  `1b9592c1b086b691b44d1a30eee4827d1ff55b8c`), which is **BSD-3-Clause**, not Apache-2.0 as earlier
  notes in this repository stated. Two optional adapters sit behind one project-owned C ABI:
  `openusd_mdl` distils an accepted OmniPBR/OmniSurface/OmniGlass subset from authored USD values
  with no dependency at all, and `openusd_mdl_sdk` additionally compiles a user-supplied module
  through a user-supplied MDL SDK runtime to resolve unauthored defaults and constant expression
  defaults. Neither is shipped in any package, no package contains an MDL SDK binary, and
  MDL-generated shader code and layered BSDF evaluation remain unimplemented. See
  [Optional MDL materials](rendering.md#optional-mdl-materials).
- **PhysX, `UsdPhysics`, and MaterialX are reachable.** `UsdPhysics` authoring and a MaterialX subset
  already exist in this repository.
- **Cesium for Omniverse's Fabric path is unreachable.** It bypasses Hydra and writes tiles through
  `omni::fabric::StageReaderWriter`.
- **No Cesium Hydra delegate or scene index is available.** None exists in this repository or the
  locked dependencies.
- **`cesium-native` is reachable but quarantined.** It is an Apache-2.0 standalone C++17 library, and
  this project owns the required C ABI shim.
- **The `usdview` embedded Python REPL is excluded.** It requires Python, which the locked native
  profile never enables.
- **The `usdview` Python plugin container, plugin dot py, is excluded.** This is the same hard Python
  rule, not a current preference.
- **Other `usdview` UI features are reachable.** They wrap C++ USD and Hydra APIs; many equivalents
  are already built.

The native toolchain and archive model are in [Native build](native-build.md). Runtime asset layout
and package-only execution are in [Packaging](packaging.md).

## Viewer renderers

| Viewer kind | Hydra path | Concrete presentation/API | Supported host | Current role |
| --- | --- | --- | --- | --- |
| Storm | Hydra/Storm | WGL, GLX, or CGL host | Windows, Linux, macOS | Primary |
| D3D12 | Hydra to hdSilk pages | Direct3D 12 | Windows | Fallback |
| Vulkan | Hydra to hdSilk pages | Vulkan | Windows, Linux | Fallback |
| Metal | Hydra to hdSilk pages | Metal | macOS | Fallback |

The Viewer permits only the platform combinations above. Renderer-neutral code must not depend on a
concrete RHI, and concrete backends must not absorb Hydra translation or Viewer policy.
The Viewer diagnostics tab now reports the active compositor/API/device, compute availability,
descriptor-indexed texture-table availability when the backend exposes it, software-device status,
draw counts, retained hdSilk command counts, uniform uploads, and cumulative retained-scene upload
bytes. ABI/package mismatches are presented as stale-runtime-package errors with remediation guidance
instead of raw initialization exceptions.
The shared RHI and all three concrete backends support RGBA8, RGBA16Float, and
RGBA32Float color targets, R32Float sampled textures, and D32Float depth targets.
Mesh rendering and visible selection compositing are format-aware; picking and
selection masks deliberately remain RGBA8 identity surfaces. Frame capture remains
an explicit RGBA8 display-image contract.
hdSilk now supports texture-backed `UsdPreviewSurface` map binding on all three RHIs by decoding
resolved assets through OpenUSD Hio. UNorm8/sRGB images use cached sampled RGBA8 textures; SNorm8,
integer, half, float, and double images use explicitly converted RGBA32Float textures so HDR values
are preserved. One- through four-channel expansion and RGB-only sRGB conversion are deterministic.
Compressed Hio formats and non-finite channels are rejected with actionable diagnostics. Missing and corrupt
assets use authored fallbacks with bounded stable diagnostics; failed loads are cached without poisoning
the successful cache and can be explicitly retried. Unresolved and unsupported materials likewise retain
default shading with distinct diagnostics. Every active texture slot binds its own cached sampler, preserving
independent `wrapS` and `wrapT` state across base-colour, normal, roughness, metallic, emissive, and volume maps.
Every material texture entry carries the connected `UsdUVTexture` output port explicitly (page ABI v13
`output_channel`, surfaced as `SilkMaterialTexture.Channel`), so hdSilk selects `r`, `g`, `b`, `a`, or `rgb`
from authored data rather than from a fixed convention; unknown or width-incompatible output tokens are
rejected with diagnostics. Scalar maps replicate the selected channel into every uploaded component after
`scale`/`bias`, so `roughness` and `metallic` are fully independent inputs with independent feature bits,
textures, samplers, bindings, and UDIM mask bits — a roughness-only material leaves metallic constant and a
metallic-only material leaves roughness constant. A packed occlusion/roughness/metallic file feeding two
inputs from two channels is decoded and uploaded once per channel.
Ordinary (non-UDIM) material textures now upload a full CPU-generated mip chain instead of one level:
a shared packed layout (mip 0 first, ascending, each tightly packed) is validated by every backend's
upload path, and D3D12, Vulkan, and Metal each allocate, bind, and transition every requested level.
The CPU box-filters Rgba8Unorm and Rgba32Float alike after decode/flip/scale-bias, averages alpha and
scalar maps ordinarily, and renormalizes normal maps after averaging (falling back to straight-up on
exact cancellation); base-level readback is unchanged. `<UDIM>` atlases remain single-level in this
slice — their sparse per-tile gutter layout is not naively downsamplable. Anisotropic sampling is now
capability-negotiated: `SilkGraphicsCapabilities.MaxSamplerAnisotropy` reports the device's actual
bounded maximum (1x when unsupported, such as a Vulkan device with `samplerAnisotropy` disabled), and
`SilkSamplerDescriptor.Validate` rejects a request above that maximum outright rather than silently
clamping it. Ordinary (non-UDIM) mipmapped material textures sampled with a linear filter request
`min(device max, 8)` when the device advertises anisotropy; `<UDIM>` atlases, single-level volume
density textures, and nearest-only `Rgba32Float` sampling always stay isotropic regardless of device
capability.
Resolver-aware `<UDIM>` textures use bounded per-slot atlases with one-pixel gutters, standard tile
numbering, and authored fallback values in sparse cells. Tile format/dimension mismatches and atlas
spans above 256 cells are diagnosed and rejected. RGBA32Float sampling is nearest-only until
cross-backend filter negotiation lands. Successful local files and resolved UDIM tiles are reloaded
when their size or last-write timestamp changes; non-filesystem resolver assets use material
dirtiness or explicit retry. Colour-delta parity with Storm remains outside the current support claim.
Texture cache residency is now bounded rather than unlimited: `SilkTextureResidencyOptions` exposes
independently configurable, validated nonzero decoded-CPU and estimated-GPU byte budgets (512 MiB
defaults), threaded through a dedicated `SilkSceneGpuResources`/`SilkMeshRenderer` constructor overload
alongside the original device-only overload, with existing callers unaffected. Ordinary, UDIM,
fallback, and volume entries are all tracked and evicted by a single deterministic least-recently-used
policy with a stable creation-order tie-breaker, applied only from an internal, submission-safe trim
point invoked after each relevant graphics submission has completed — never while unsubmitted or
in-flight commands may still use a retained texture — and only against entries not referenced since the previous trim,
so an over-budget working set rendered every frame is retained rather than decoded, uploaded, evicted,
and re-decoded every frame; failed texture fallbacks are eligible for eviction only as a last resort.
An entry that alone exceeds a budget is evicted (not retried in a loop) with a bounded
`TextureBudgetExceeded` diagnostic, and the same diagnostic reports a pinned current-frame working set
that alone stays over budget once no stale entry remains to evict. `SilkSceneGpuStatistics` reports
current and peak decoded/GPU resident bytes, both configured budgets, the total cache entry count
across every kind, and a cumulative eviction count.
It also supports a documented MaterialX projection plus generated-source paths for graphs outside that projection:
`ND_standard_surface_surfaceshader` base colour (scaled by `base`), emission colour (scaled by `emission`), metalness,
roughness, index of refraction (`specular_IOR`), coat weight and coat roughness, monochrome constant opacity, and normal
can be constant, driven by a direct image, or folded through constant multiply/add/subtract/clamp/mix nodes;
`ND_open_pbr_surface_surfaceshader` (OpenPBR Surface 1.1, the identifier the pinned MaterialX 1.39.4 libraries declare)
projects the same way from `base_color`/`base_weight`, `emission_color`/`emission_luminance`, `base_metalness`,
`specular_roughness`, `specular_ior`, `coat_weight`, `coat_roughness`, `geometry_opacity`, and `geometry_normal`.
A chain of constant
multiply/add/subtract/mix nodes over exactly one image is folded into that texture's scale and bias, and a constant
texture-coordinate chain of `ND_place2d_vector2` and `UsdTransform2d` nodes is composed into the single per-material
UV transform its images sample through. Unsupported nodes are
reported with `TF_WARN` diagnostics that name the material input and node id. Transmission, subsurface, sheen/fuzz,
thin film, anisotropy, coat tint/coat IOR/coat normal, and MaterialX `specular_color` as a specular workflow are
explicitly **not** projected: each is reported by name and left at the renderer default rather than folded into a
parameter with a similar range. OpenPBR is supported only as this projection; hdSilk generates no MaterialX shader code
for it. That source support is broader than the
Storm parity evidence: the only gated MaterialX-adjacent scene is a hand-authored PreviewSurface equivalent of constant
base colour, roughness, and zero metalness. Storm currently renders the authored MaterialX standard-surface parity mesh
as black in this harness, so the scene is recorded but not gated.
The UV transform fold is gated by pixel self-consistency and divergence on both executable backends rather than by
Storm parity: a quad whose authored coordinate reads one texel of a 2x2 image renders identically to a constant
reference of the texel the folded affine selects, both for a pure `(1, 0, 0, 1, 0.5, 0.5)` translation and for the
`(0, -1, 1, 0, 1, 0)` quarter turn a `UsdTransform2d` produces (`maxChannelDelta=2`, `meanChannelDelta=0.017` on both
Vulkan and D3D12 WARP), while the same material with the identity transform fails that comparison at
`maxChannelDelta=244`. The rotation case is what proves the off-diagonal matrix terms reach the shader, which a
translation-only capture cannot. Non-constant transform inputs, a transform behind an unsupported intermediate node, a
chain that never reaches a coordinate node, and a second image in the same material asking for a different transform
are all rejected with diagnostics and proven by the native `hdsilk_probe` rather than approximated.
Constant arithmetic over one image is gated the same way and to the same precision: the chain
`mix(0.2, image * 0.5, 0.75)` folds to `image * 0.375 + 0.05`, and a 0.4 texel shades bit-identically to a constant
material of 0.2 (`maxChannelDelta=0`, `meanChannelDelta=0.000` on both Vulkan and D3D12 WARP) because both values are
exactly representable in eight bits, while the same image bound with the identity scale and bias diverges at
`maxChannelDelta=49`. Two images combined into one input, an image-driven mix factor, a non-affine operator over an
image, arithmetic over a normal map, and any fold that would leave the unit range are all rejected with diagnostics.
MaterialX image sampling is read with MaterialX's own names and defaults: `uaddressmode`/`vaddressmode` per axis,
defaulting to `periodic`, and `default` as the unreadable-file fallback. That default is gated by pixels on both
executable backends -- a 1.25 coordinate on a 2x2 image matches the wrapped texel under periodic and the edge texel
under the wire's black mode, each at `maxChannelDelta=2`, and the two diverge at `maxChannelDelta=244`. An
address mode outside the four MaterialX names, and a `ND_texcoord_vector2` naming a second UV set, are rejected with
diagnostics rather than resolved to the first set.
The staged runtime includes `usdMtlx`, MaterialX DLLs, `MaterialXGenGlsl`, and the standard libraries, and the
asset uses the MaterialX `out` terminal; with `UsdImagingGLEngine` scene materials enabled, Storm still covers only
the 347-pixel PreviewSurface anchor against hdSilk's 4314-pixel MaterialX mesh. Where the two overlap, the colour
delta remains only 3, so the divergence is capability rather than shading. Generated Vulkan MaterialX is gated by
self-consistency instead: an unlit `ND_constant_color3 -> ND_multiply_color3FA -> ND_surface_unlit` graph renders
against an emissive PreviewSurface equivalent at `maxChannelDelta=1` and `meanChannelDelta=0.237`, and disabling
generated shader selection fails at 187 / 19.574. Metal carries generated `MslShaderGenerator` source through the same
runtime shader cache, but this Windows harness cannot execute a Metal pixel gate.
Sampled `UsdVol` density rendering is implemented and conformance-gated for the Vulkan and D3D12 hdSilk backends,
and only when the native profile provides OpenUSD's `hioOpenVDB` field reader. The Vulkan legs are executed on
win-x64 and linux-x64; the D3D12 WARP leg and the cross-backend comparison are executed on win-x64 only, because
Direct3D 12 exists only on Windows. The native shim reads one
`UsdVolOpenVDBAsset` density field through OpenUSD Hio and publishes a bulk cached R32 volume texture; both backends
upload that cache as a 3D texture and the mesh raymarches it, integrating one sample per voxel layer so the
reconstruction follows the grid's own Z resolution rather than a fixed step count. Each backend gate compares the
sampled render with a
uniform proxy at the same mean density and with a shifted density grid, so a uniform fallback fails. If the Hio OpenVDB
reader is absent, the gate is reported as a capability skip. Storm is not a usable VDB reference in the current
offscreen harness: the same sampled, uniform-mean, and shifted VDB stages produce identical Storm images, so hdSilk
remains gated by the self-divergence proof plus a cross-backend comparison rather than cross-renderer parity. D3D12 WARP
and Vulkan SwiftShader now render the same sampled grid to the same image (`maxChannelDelta=0`,
`meanChannelDelta=0.000000`), with identical footprint variance `890.508958`, sampled-versus-uniform
`maxChannelDelta=100` / `meanChannelDelta=19.730324`, and shifted-grid `maxChannelDelta=115` /
`meanChannelDelta=2.409473`. Self-divergence alone was not enough evidence: D3D12 previously passed the
sampled-versus-uniform check while rendering a flat authored density, because the checked DXIL mesh fragment declared no
density texture at all and the shifted-grid check was therefore invariant. That is why the cross-backend comparison is
now part of the gate. Only one checked fragment program samples the density grid, so a volume material that also binds
2D material maps, or one routed through the runtime MaterialX shader service, has no correct pipeline; hdSilk names that
combination and refuses the draw rather than shading the proxy at the authored uniform density. The Metal backend now
implements the same R32Float 3D texture create, blit upload, and explicit texture/sampler slot binding, the
`volumeFragmentMain` entry point is part of the pinned `mesh.metallib` contract, and `UniformDensityVolumeGatesOnMetal`
and `SampledOpenVdbDensityGatesOnMetal` run the same shared helper, stages, crops, and thresholds as the Vulkan and
D3D12 legs from the `macos-arm64` render job. That job stages the runtime with `eng/stage-hdsilk-runtime.ps1`, uploads
`render-volume-evidence-osx-arm64-<run>`, and classifies the result with `eng/assert-volume-evidence.ps1`. No workflow
run has recorded `status=executed` for that backend yet, so osx-arm64 is deliberately absent from the sampled-volume
evidence platforms and Metal sampled volumes carry no rendering support claim: the promotion step is a run whose
`volume-evidence-metal-status.json` says `executed`, after which `-AllowCapabilitySkip` is removed from that job and
osx-arm64 joins the gate. Multi-field volumes, non-density field
roles such as temperature or velocity, Field3D rendering, and `UsdVolVolume` prims with several field relationships
remain outside the rendering support claim. The field's own transform is likewise not yet honored: the grid is
stretched to fill the proxy rather than placed by the `UsdVolOpenVDBAsset` prim transform and the VDB's own
index-to-world transform, so only the density values are transported, not the grid's placement.

## Backend capabilities

These rows reflect the current `RenderBackendCapability` declarations. An em dash means the
capability is not advertised by that backend descriptor.

| Capability | Storm | D3D12 | Vulkan | Metal |
| --- | :---: | :---: | :---: | :---: |
| Presentation | ✅ | ✅ | ✅ | ✅ |
| Offscreen | — | ✅ | ✅ | ✅ |
| Compute | — | ✅ | ✅ | ✅ |
| Maximum samples per pixel | 8 | 1 | 1 | 1 |
| Shadows | ✅ | — | — | — |
| Device-loss detection | ✅ | ✅ | ✅ | ✅ |
| Renderer-neutral picking | ✅ | ✅ | ✅ | ✅ |

The `Shadows` row states that the Storm backend descriptor advertises the capability. It does not
mean shadows are rendered in every configuration: the offscreen parity harness is measured not to
produce them at all, so no shadow scene is gated. See
[Testing](testing.md#storm-offscreen-harness-capability-limits).

Selection rendering is related but separate from the capability enum:

| Selection behavior | Storm | D3D12 | Vulkan | Metal |
| --- | --- | --- | --- | --- |
| Visible selection | Native yellow highlight | Orange outline | Orange outline | Orange outline |
| Occlusion policy | Storm-native | Visible-only | Visible-only | Visible-only |
| X-ray mode | Not exposed | Implemented | Implemented | Source path only |
| Hosted pixel evidence | Workflow-gated | Workflow-gated | Workflow-gated | Pending hosted proof |

See [Rendering](rendering.md) for request binding, stale results, GPU passes, and lifecycle detail.

## hdSilk Storm parity sign-off

The next-alpha rendering-parity claim is intentionally narrower than "everything hdSilk can parse".
`eng/run-parity-capture.ps1` registers 25 curated scenes. Twenty-two are hard gates at
`1.000000` adjusted IoU against D3D12 WARP and Vulkan SwiftShader; three are
measured but deliberately ungated because Storm is not a valid offscreen reference there:
`materialx-standard-surface-constant` renders the MaterialX side black in Storm,
`light-distant-shadow` never casts the authored shadow in Storm, and
`subdivision-catmull-clark` records Storm's coarse/control-cage-like subdivision output.
Hosted Mesa/llvmpipe Storm runs only 21 registered scenes, excluding `single-sided-winding`,
`bounds-draw-mode`, `origin-draw-mode`, and `subdivision-catmull-clark` because Mesa Storm
differs from conformant-driver Storm.

The render workflow now wires the curated capture into the `macos-15` arm64 job with a CGL Storm
context and the Metal hdSilk backend, but only after `resolve-macos-cgl-capability.ps1` proves that
the host can create the required accelerated offline-capable CGL pixel format. When that preflight
fails on hosted arm64 with `kCGLBadPixelFormat`, the job records
`artifacts/render-capability/macos-cgl.json` and continues. When CGL is available, the macOS path
runs deterministic Storm/Metal capture, the perturbation companion that must go red when the Metal
image is flipped, mirrored, transposed, shifted, or sampled at the wrong time, both platform-neutral
Silk complexity proofs, and Metal MaterialX self-consistency. The capture asserts all 25 registered
scenes with no macOS exclusions unless the script is changed to name one. A missing Metal device,
scene-count mismatch, or missing input is still a hard failure when the capture runs.

The other `StormSilkParityCaptureDriverTests` entries are targeted backend/detail gates rather than
additional curated scenes: D3D12 frame capture, hdSilk complexity page selection, Vulkan synthetic
MaterialX and texture self-consistency/divergence, non-diffuse texture slots, remaining constant
PreviewSurface inputs, cull-style divergence, and area-light equivalence/divergence. The macOS
parity capture does not run those Vulkan/D3D12 detail gates. Metal detail evidence outside the
curated scene capture remains limited to the native pipeline probe, the ten-entry `mesh.metallib`
contract, IOSurface composition, selection/picking/compute conformance, and the generated
MaterialX-vs-PreviewSurface Metal self-consistency gate.

This path must not be counted as observed hosted Storm/Metal parity until a workflow run records CGL
availability and the capture artifacts. The current hosted macOS evidence is an explicit CGL
capability skip plus the separate Metal composition, MaterialX self-consistency, lifecycle, and
Storm/Metal switching proofs.

The harness is driven by the `render` workflow, **not** by ordinary CI, so a green `ci` badge does
not mean parity ran. The full 22-gate matrix passes on Windows with a conformant GPU driver. Hosted
Linux runs and passes it against Vulkan SwiftShader. Hosted Windows WGL now runs the seven
non-Vulkan WGL tests with Mesa llvmpipe and D3D12 WARP; Vulkan composition still records a
capability skip because the runner has no usable Vulkan ICD. That is the `render-unblock-vulkan`
limitation and needs a GPU-equipped self-hosted runner.

### Feature-to-scene matrix

| Feature | Gated parity scene |
| --- | --- |
| Asymmetric mesh orientation/projection | `orientation-asymmetric` |
| Clip plane supplied by the parity harness | `clip-plane-asymmetric` |
| Depth, overlapping prims, retained draw order | `depth-overlap-multiprim` |
| Authored normals and UV primvars | `material-normals-uv` |
| Constant/display-colour primvars | Multiple mesh scenes |
| PreviewSurface diffuse and roughness constants | `material-normals-uv`, `light-dome-ambient`, direct-light scenes |
| PreviewSurface specular workflow | `light-distant-specular` |
| PreviewSurface texture-backed diffuse colour | `materials-textures` |
| Texture wrap modes | `materials-textures`, `texture-wrap-*` equivalence and divergence |
| Texture colour spaces | `materials-textures`, `texture-colorspace-auto` and `-raw` divergence |
| Texture scale, bias, and fallback | `texture-scale-bias-fallback` equivalence and divergence |
| Texture slots beyond diffuse | Emissive, roughness, metallic, and normal self-consistency pairs |
| Texture-coordinate primvar interpolation | `primvar-st-*-texture` for varying, faceVarying and uniform |
| Metallic workflow with non-zero `metallic` | `material-metallic-workflow` |
| Remaining PreviewSurface constants | Emissive, occlusion, opacity threshold, clearcoat and IOR pairs |
| MaterialX standard-surface authored graph | None |
| MaterialX projection arithmetic/equivalent constants | `materialx-standard-surface-preview-equivalent` |
| Distant light direct transport | `light-distant-exposure` |
| Distant light glossy specular | `light-distant-specular` |
| Sphere light direct transport | `light-sphere-point` |
| Rect light direct transport | `light-rect-*-self-consistency`, `light-rect-*-divergence-self-consistency` |
| Disk light direct transport | `light-disk-*-self-consistency`, `light-disk-*-divergence-self-consistency` |
| Cylinder light direct transport | `light-cylinder-*` equivalence and divergence |
| Dome light ambient | `light-dome-ambient` |
| Dome light textured IBL | Analytic tests plus WARP/SwiftShader environment conformance |
| Shadows | None |
| Point instancing | `point-instancer-cluster` |
| Point-instancer instance identity | None; page-level gated by `hdsilk_probe` and managed identity tests (see below) |
| Points | `points-asymmetric` |
| Basis curves / draw-mode generated lines | `bounds-draw-mode`, `origin-draw-mode` |
| Basis curve width interpolation | None; workflow-gated by page-level and cross-backend tests instead (see below) |
| Draw modes | `cards-draw-mode`, `bounds-draw-mode`, `origin-draw-mode` |
| Double-sided/culling | `single-sided-winding`, `cull-style-back-self-consistency`, and its divergence companion |
| Front cull styles and orientation | None; cross-backend and page-level gated (see below) |
| Time-varying transform/display colour | `time-varying-transform-primvar` |
| UsdSkel CPU skinning | `skinned-pennant` |
| Subdivision surfaces | Catmull-Clark, Loop, and bilinear levels 0-3 with tags and refined primvars |

### Ungated because Storm cannot be the reference

These are not hdSilk implementation gaps. They are cases where the Storm offscreen harness does not
produce a usable reference image for the authored feature.

- **MaterialX standard surface:** Storm shades only the 347-pixel PreviewSurface anchor while hdSilk
  shades 4314 MaterialX pixels. `materialx-standard-surface-constant` is registered, measured, and not
  gated.
- **Catmull-Clark Storm parity:** `subdivision-catmull-clark` measures 0.931015 adjusted IoU against
  Storm's coarse/control-cage-like Low-complexity output, so that parity scene remains measured and not gated.
  Subdivision itself is gated analytically by `hdsilk_subdivision_probe` at refinement levels 1-3.
- **Shadows:** `light-distant-shadow` is byte-identical with shadows disabled
  (`disabledAdjustedIoU=1.000000`), so it remains registered, measured, and not gated.
- **Textured dome-light image-based lighting:** Storm's offscreen harness renders no dome texture, so
  there is no usable reference image for the authored feature, and Storm's offscreen path has no
  directional environment response to compare against in any case. hdSilk prefilters an accepted
  textured dome into a world-oriented cosine irradiance map, a roughness-sliced GGX specular atlas and a
  numerically integrated split-sum BRDF table, so the response depends on the world-space shading
  direction, on the dome's authored orientation, and on the material's roughness. It is gated
  analytically and by its own cross-backend pixel gates instead.
  `SilkEnvironmentLightingTests` pins the prefilter -- the exact `pi * L` cosine identity, a hemisphere
  that lights one pole and not the other, rotation equivalence, monotonic lobe broadening, energy
  preservation at every roughness, the exact `irradiance / pi` identity at roughness 1, the independent
  diffuse and specular contribution scales, multi-dome composition, colour-space resolution from the
  image-info v2 observation, half-precision saturation, USD's lat-long convention against external
  reference directions, the BRDF table against an independent integration of the same equations, and
  every budget and cache-identity rule. `SilkEnvironmentRetentionTests` pins the retention rules,
  including that a prefiltered dome
  contributes nothing to the frame ambient term and that every unsupported cause falls back and is
  named, that an in-place asset repair is observed without a new command, and that a device loss
  recreates every environment-owned GPU object. `SilkEnvironmentLightingConformance` runs the executed
  pixel gates on D3D12 WARP and Vulkan SwiftShader, including a dome-only stage that suppresses the
  fallback headlight and rotated and non-uniformly scaled meshes. `SilkDomeEnvironmentTests` still
  pins the ABI v16 environment record and the
  mean-radiance ambient **fallback**, including its rotation invariance and the parity case in which a
  constant-1.0 environment reproduces the untextured unit dome's ambient exactly.
  `OmniverseDomeFixtureTests` decodes the committed `test-assets/omniverse/lighting/*.hdr` fixtures and
  proves the bytes on disk carry exactly the radiance their stages document. The reflection resolution
  bound, the four-dome composition bound, the absence of dome shadows and of per-prim dome linking, and
  the unlit generated-MaterialX terminal are all reported against the affected prim; see
  `docs/rendering.md` for the diagnostic codes and budgets.

Self-consistency gates that deliberately avoid Storm's offscreen reference gaps:

- `texture-wrap-clamp-self-consistency`, `texture-wrap-mirror-self-consistency`, and
  `texture-wrap-use-metadata-self-consistency` compare `repeat` against the candidate wrap mode with UVs
  confined to `[0,1]`, where every wrap mode is mathematically equivalent. Each measured
  `maxChannelDelta=2` / `meanChannelDelta=0.082`. The companion outside-`[0,1]` UV divergence cases require
  the sampler address mode to differ: clamp and `useMetadata`/black each measured `maxChannelDelta=203` /
  `meanChannelDelta=14.950` -- identical, because both resolve to clamp-to-edge -- and mirror measured
  `maxChannelDelta=127` / `meanChannelDelta=7.135`.
- `texture-colorspace-auto-self-consistency` compares diffuse `sourceColorSpace=sRGB` with `auto`, which
  resolves to the same sRGB decode for diffuse textures. It measured `maxChannelDelta=2` /
  `meanChannelDelta=0.082`. The raw/linear companion compares the same texture as `sRGB` versus `raw`;
  it measured `maxChannelDelta=71` / `meanChannelDelta=14.780`, so the colour-space token reaches decode.
- `texture-scale-bias-fallback-self-consistency` compares a constant PreviewSurface diffuse colour with a
  missing texture's fallback after non-identity scale and bias compose to the same colour. It measured
  `maxChannelDelta=2` / `meanChannelDelta=0.088`. Its companion removes the bias and measures
  `maxChannelDelta=26` / `meanChannelDelta=5.637`; the prior red proof that zeroing the bias fails with
  `maxChannelDelta=49` / `meanChannelDelta=7.451` shows the low-delta assertion is live.
- `cull-style-back-self-consistency` compares explicit `cullStyle=back` with `backUnlessDoubleSided` on a
  single-sided mesh viewed from the outside. It measured `maxChannelDelta=2` / `meanChannelDelta=0.095`.
  The double-sided back-face companion measured `maxChannelDelta=172` / `meanChannelDelta=24.072`, so an
  ignored `cullStyle` token or ignored double-sided flag would diverge.
- `light-rect-zero-area-self-consistency` compares a zero-area rect light with a sphere light. It measured
  `maxChannelDelta=10` / `meanChannelDelta=0.550`. Its full-area companion measured `maxChannelDelta=28` /
  `meanChannelDelta=2.355`, so ignoring rect extent would lose the required divergence.
- `light-disk-edge-on-self-consistency` compares an edge-on disk light with an unlit half. It measured
  `maxChannelDelta=16` / `meanChannelDelta=0.607`. The face-on companion measured `maxChannelDelta=49` /
  `meanChannelDelta=4.014`, so ignoring disk orientation would lose the required divergence.
- `light-cylinder-zero-length-self-consistency` compares a zero-length cylinder light with a sphere light.
  It measured `maxChannelDelta=10` / `meanChannelDelta=0.550`. Its full-length companion measured
  `maxChannelDelta=25` / `meanChannelDelta=2.039`, so ignoring cylinder length would lose divergence.
- The non-diffuse texture-slot gates compare an untextured material with neutral one-pixel fallbacks routed
  through the emissive, roughness, metallic, and normal texture shader variants. They measured:
  emissive `maxChannelDelta=8` / `meanChannelDelta=0.636`, roughness `maxChannelDelta=11` /
  `meanChannelDelta=0.810`, metallic `maxChannelDelta=2` / `meanChannelDelta=0.065`, and normal
  `maxChannelDelta=8` / `meanChannelDelta=0.624`. Their companion non-neutral fallbacks measured:
  emissive `maxChannelDelta=79` / `meanChannelDelta=7.735`, roughness `maxChannelDelta=41` /
  `meanChannelDelta=8.278`, metallic `maxChannelDelta=144` / `meanChannelDelta=19.155`, and normal
  `maxChannelDelta=101` / `meanChannelDelta=14.463`, so silently dropping any slot loses divergence.
- The remaining PreviewSurface constant-input gates compare different material setups that are equivalent
  only if the named input reaches the checked shader. They measured: occlusion `maxChannelDelta=2` /
  `meanChannelDelta=0.382`, opacity threshold `maxChannelDelta=2` / `meanChannelDelta=0.355`,
  emissive `maxChannelDelta=2` / `meanChannelDelta=0.091`, clearcoat `maxChannelDelta=10` /
  `meanChannelDelta=0.852`, clearcoat roughness `maxChannelDelta=9` / `meanChannelDelta=0.719`,
  and IOR `maxChannelDelta=10` / `meanChannelDelta=0.859`. Their companions measured: occlusion
  `maxChannelDelta=157` / `meanChannelDelta=20.629`, opacity threshold `maxChannelDelta=157` /
  `meanChannelDelta=20.611`, emissive `maxChannelDelta=63` / `meanChannelDelta=6.582`, clearcoat
  `maxChannelDelta=20` / `meanChannelDelta=3.604`, clearcoat roughness `maxChannelDelta=25` /
  `meanChannelDelta=4.867`, and IOR `maxChannelDelta=19` / `meanChannelDelta=3.377`, so dropping
  those inputs loses divergence.
- The texture-coordinate primvar interpolation scenes bind the same asymmetric texture through `st`
  authored as `varying`, `faceVarying`, and `uniform` primvars. They measured `1.000000` adjusted IoU
  with a `0.218603` perturbation margin. Colour deltas were max 4 / mean 1.717 for varying,
  max 4 / mean 1.717 for face-varying, and max 4 / mean 1.347 for uniform. The face-varying and
  uniform scenes exercise hdSilk's expanded-topology path, so treating those primvars as ordinary
  point/vertex data is not a passing shortcut.

At the parity harness's Low complexity, Storm renders the Catmull-Clark probe as
a coarse/control-cage-like surface rather than full refinement. hdSilk also keeps
Low at refinement level 0, preserving the established parity corpus. Medium,
High, and VeryHigh select OpenSubdiv levels 1, 2, and 3 and are gated by exact
analytic native fixtures rather than this unsuitable Storm reference.

### Implemented or reachable but not yet parity-gated

- `depth-overlap-multiprim` has the thinnest accepted perturbation margin at 0.184333.
- Varying, uniform, and face-varying texture-coordinate primvar interpolation modes are gated.
- Other primvar names beyond constant `displayColor`, normals, and `st` texture coordinates are not gated.
- Texture `repeat`, `clamp`, `mirror`, `useMetadata`/black, `sRGB`, diffuse `auto`, and raw/linear colour
  space are gated. `useMetadata` and `black` are distinct wire values: the fragment stage resolves both
  to clamp-to-edge, because the wire carries no border colour and no backend is given one, while the
  vertex-stage displacement sampler implements both exactly as a transparent-black border and resolves
  `useMetadata` from the image's own per-axis wrap metadata.
- Specular, opacity, and occlusion texture slots have no parity scene.
- `displacement` moves geometry in hdSilk, resolved from the authored `displacement` material terminal
  rather than inferred from a surface input. A constant authored amount, or a per-vertex float sample
  of a connected `UsdUVTexture` height field, offsets every emitted point along its object-space
  shading normal in the one retained vertex buffer the colour pass, the raster shadow depth pass, the
  pick pass and the selection outline all draw. It is gated by the native producer probe, by analytic
  cases, and by WARP and SwiftShader pixel gates (constant, texture-driven, shadows, cache reuse and
  invalidation, refusals) rather than by a Storm parity scene. What is *not* implemented is named in
  [`rendering.md`](rendering.md#usdpreviewsurface-displacement): no re-derived shading frame, no
  adaptive or limit-surface tessellation, no UDIM or two-image composite height field, and no
  displacement inside the ABI v20 GPU deformation kernel.
- The Vulkan renderer-consumption path for emissive colour, occlusion, opacity threshold, clearcoat,
  clearcoat roughness, and IOR is self-consistency-gated, but not yet Storm-gated from authored USD.
- MaterialX images, normal maps, emission, non-zero metalness, and arithmetic chains have no Storm parity gate.
- OpenPBR (`ND_open_pbr_surface_surfaceshader`) and the broadened `standard_surface` inputs are gated by the native
  hdSilk projection probe and by managed page/renderer self-consistency, not by Storm parity. Both models carry only
  the inputs that are the same quantity as an existing wire parameter. Not projected, and reported by name instead:
  `transmission*`, `subsurface*`, `sheen`/`fuzz_weight`, `thin_film*`, every anisotropy and rotation input,
  `coat_color`, `coat_IOR`/`coat_ior`, `coat_normal`/`geometry_coat_normal`, `coat_affect_*`, `coat_darkening`,
  `thin_walled`/`geometry_thin_walled`, `diffuse_roughness`/`base_diffuse_roughness`, and `tangent`/`geometry_tangent`.
  MaterialX `specular_color` is an edge tint rather than the normal-incidence reflectance
  `UsdPreviewSurface.specularColor` carries, so **no specular workflow is derived from it**. A nodedef weight the graph
  *connects* is a per-pixel multiply this projection has no slot for, and the input it scales is left at the renderer
  default. `standard_surface.opacity` is `color3` while the wire binds one channel, so only a constant with equal
  channels projects; a per-channel or connected colour-typed opacity is refused. There is **no generated OpenPBR
  shader path**: an OpenPBR material needing a lobe the projection omits renders without it and says so.
- The MaterialX `place2d` / `UsdTransform2d` UV transform fold is gated by Vulkan and D3D12 pixel self-consistency and
  divergence, not by Storm parity, and only one texture-coordinate stream per material is carried: **both** the UV
  transform and the UV primvar are reconciled across a material's textures, so divergent per-image transforms,
  divergent per-image primvars (a base colour on `uvSet0` beside a normal map on `uvSet1`),
  non-constant transform inputs, transforms behind unsupported intermediate nodes, and chains that never reach a
  coordinate node are rejected with diagnostics rather than rendered. **Multiple UV sets are not supported** by the
  wire or by the checked shaders, and the expanded surface-model projection does not change that.
- Constant arithmetic over one image folds into that texture's scale and bias and is gated the same way. Two images
  joined by one constant `multiply`, `add`, `subtract` or `mix` are supported as a per-pixel composite and gated by
  pixels on both executable backends, but **one composite per material** only: a graph that composites a second
  surface input has both entries of that second input dropped with a diagnostic. Image-driven mix factors, non-affine
  operators over an image, three or more images in one input, arithmetic on the `normal` input, and per-entry folds
  leaving the unit range are rejected with diagnostics.
- MaterialX image sampling is read from `uaddressmode`/`vaddressmode` and `default`, with MaterialX's periodic
  default. Border sampling is **not** implemented: `SilkTextureWrap.Black` -- published for UsdUVTexture's
  `black`/unauthored `wrap` and for MaterialX `constant` addressing -- is resolved to clamp-to-edge, so it renders
  identically to `SilkTextureWrap.Clamp` and a sample outside the unit range returns the edge texel rather than black
  or a MaterialX node's `default` colour. The `default` colour is transported only in its other role, as the
  unreadable-file fallback. A second UV set named by a `ND_texcoord_vector2` `index` is not supported.
- Sphere-light glossy shaping and area-light texture inputs are not gated. Dome textures have no gated
  Storm parity scene either, because the offscreen harness renders none; hdSilk's textured-dome
  image-based lighting is gated analytically and by its own D3D12 WARP and Vulkan SwiftShader pixel
  gates instead.
- Multiple point-instancer prototypes, proto-index variation, `invisibleIds`, and two levels of nesting have no
  Storm parity scene, and instanced shadows are not gated at all. What is gated is the published identity, at page
  level rather than by pixels, under manifest capability `point-instancer-instance-identity`: `hdsilk_probe` drives
  `test-assets/hdsilk-pointinstancer-probe.usda` and requires the
  instance's own index inside its instancer -- the index into `protoIndices`/`positions` that UsdImaging decodes back
  to a scene instance -- rather than the ordinal of the instance in the resolved array, so a prototype that owns only
  part of an instancer publishes a sparse index set, swapping proto indices retires and republishes identities instead
  of renumbering them, and hiding an instance leaves the survivors' indices alone. Nested instancers have no USD
  instance index; hdSilk composes `parent_index * inner_instance_count + inner_index` against the inner instancer's
  own authoritative instance count and never a per-prototype radix, and drops with a diagnostic any index that count
  cannot explain. The encoding is unique and stable but is an hdSilk encoding rather than an index USD can decode.
  Two wire invariants keep the ABI v8 payload resolvable: the records of one path serialize atomically, so a rejected
  payload cannot leave orphan instance references, and an empty mesh is retired rather than published, because an
  empty record is byte-identical on the wire to an instance reference. The elision is topology neutral, so instanced
  `BasisCurves` line lists and instanced `Points` point lists carry their payload once on the lowest published index
  in the same way a triangle list does.
- Wide point splats are not gated. Authored curve widths are now resolved for every interpolation
  `UsdGeomCurves` can author on a linear segmented curve -- constant, uniform, varying, and vertex --
  published as an `OPENUSD_SILK_ATTRIBUTE_WIDTH` vertex attribute, and interpolated along each
  segment when complexity subdivides it. They no longer delete the prim: before this, only a scalar
  or single-element `widths` array was accepted, and `parity-curve-width-interpolation.usda`
  published one of its four curve prims instead of four.
  What they still do not do is change coverage, and that is deliberate: Storm rasterizes linear basis
  curves as one-pixel screen-space lines at the harness refinement and ignores authored world-space
  widths (`parity-curve-width-probe.usda` measured Storm at 128 pixels against 2093 for ribbons), and
  no backend can widen a line portably -- D3D12 has no line width state, Vulkan needs the optional
  `wideLines` feature, Metal has none. There is therefore no Storm parity scene; the capability is
  workflow-gated instead by page-level tests on all three `render.yml` platform jobs, a D3D12 WARP
  and Vulkan SwiftShader cross-backend gate on `windows-wgl`, and `hdsilk_probe` in `ci.yml` and
  `native.yml`. Ribbons and half-tubes at higher refinement are not implemented and not claimed.
- Authored `cullStyle=back` and the default `backUnlessDoubleSided` are gated for a single-sided mesh and
  a double-sided back-face divergence companion against Storm. `front` and `frontUnlessDoubleSided` have no
  Storm parity scene, but they are no longer ungated or unimplemented: they were mapped to *back* culling by
  a catch-all, which culled exactly the faces they ask to keep, and `SilkCullMode` had no `Front` member at
  all. `CullStyleFrontCullsTheOppositeFacesOfBackOnD3D12AndVulkan` renders one front-facing and one
  back-facing single-sided quad and requires the two styles to select disjoint halves of the canvas
  identically on D3D12 WARP and Vulkan SwiftShader; measured `back=2323/0`, `front=0/2323`, and
  `frontUnlessDoubleSided` on a double-sided mesh `2323/2323`, versus `front=2323/0` -- indistinguishable
  from `back` -- when the catch-all is reinstated. The claim is bounded: USD gprims author `doubleSided`
  rather than a Hydra cull style, and the hosted session pins `UsdImagingGLRenderParams.cullStyle` to
  `backUnlessDoubleSided`, so the front styles are reachable through the page and not from a stage.
  `nothing` remains ungated.
- Authored `orientation` needs no page-level handling: `HdMeshUtil::ComputeTriangleIndices` already reverses
  a `leftHanded` face's corners, and every backend rasterizes counter-clockwise-front, so the emitted winding
  is correct for both orientations. `hdsilk_probe` pins the emitted indices for a matched `rightHanded` and
  `leftHanded` quad pair so neither a dropped nor a duplicated correction can regress silently. There is no
  Storm parity scene for orientation.
- Animated materials, textures, lights, and topology have no parity scene.
- GPU blend-shape deformation and linear skinning are gated on D3D12 WARP and Vulkan SwiftShader for the bounded
  triangle-list subset described in `docs/rendering.md`. Metal and ineligible rigs retain the authoritative CPU path.
- Live GPU display/view/look correction is implemented as a precise 3D-LUT subset, not as
  arbitrary OCIO GPU shader generation. `RenderSettings.DisplayTransform` carries a
  renderer-neutral `RenderDisplayTransform` (config path plus optional display, view, and look);
  hdSilk renders the scene into a linear RGBA16Float intermediate and applies exposure and one
  baked lattice in a fullscreen pass, so the transform reaches live presentation and ordinary
  captures alike. The lattice is baked once through the same `SilkOpenColorIoProcessor` the CPU
  export path uses, in a single bulk native call, and is bounded and LRU-cached. D3D12 WARP and
  Vulkan SwiftShader gates compare every GPU pixel against the CPU processor within 2 code values;
  Metal is source-complete and compile-only. OCIO GPU shader generation, config-authored shaper
  spaces, and OCIO dynamic properties other than exposure remain outside the claim, and there is
  still no Hydra-side render-settings path that selects the transform from a scene, because hdSilk
  does not run the Hdx task graph. Failures are reported as bounded diagnostics rather than a
  silent identity result. See `docs/rendering.md` for the exact exclusions.
  CPU capture/export OCIO is unchanged and still supported through
  `SilkOpenColorIoProcessor` applied after readback: create a processor from a
  `SilkOpenColorIoDisplayTransform` (config path, source color space, optional display/view/looks)
  and pass it to the OCIO `SilkFrameCapture.Capture` / `CaptureRetained` / `SilkFrameCapturer.Capture`
  overloads. Exposure is applied before the OCIO transform; `RenderSettings.OutputTransform`
  must be `Identity` when an OCIO processor is supplied, and `RenderSettings.DisplayTransform`
  must be null so the image is never colour managed twice.

### Unimplemented or deliberately excluded from the next-alpha parity claim

- Catmull-Clark, Loop, and bilinear uniform subdivision, creases, corners, holes, and subdivision primvar refinement
  are implemented and analytically gated. Adaptive and limit-surface tessellation and GPU refinement
  remain outside the current claim.
- UsdPreviewSurface `displacement` moves the drawn geometry, resolved from the authored `displacement`
  material terminal and refused by name for any other terminal or driving node. It is applied at
  whatever emitted vertex density the display style's refinement produced: refinement is the
  tessellation, and complexity Low keeps refinement level 0, so a displaced prim at Low is displaced
  at its control cage. The order is subdivide, then deform, then displace, in the prim's object space
  along the object-space shading normal, and the shading frame is never re-derived. A displaced
  skinned prim is refused by the ABI v20 GPU deformation kernel with the named reason
  `SilkDeformationGpuFallback.MaterialDisplacement` and drawn from hdSilk's authoritative CPU-resolved
  points, which is what keeps the deform-then-displace ordering exact. Height samples stay
  single-precision through the authored `scale` and `bias`, applied after filtering so a
  transparent-black border carries the bias, including signed and over-unit values;
  `sourceColorSpace = auto` and `wrap = useMetadata` are resolved from what the image library observed
  and refused by name when it observed nothing; an unreadable image displaces by the authored
  `fallback` and says so. UDIM height fields, two-image composite operands, a coordinate set the mesh
  does not carry, non-triangle topology, a non-finite amount, an unrepresentable metadata wrap mode,
  and both preflighted budgets are reported by name.
- hdSilk evaluates the UsdSkel subset described in `docs/rendering.md`: blend-shape point and normal offsets,
  in-between shapes, and linear joint-weighted skinning of points and per-point normals, all re-resolved per evaluation
  time. D3D12 and Vulkan execute the eligible subset on the GPU and compare against that CPU answer; Metal and rigs
  needing derived tangents, expanded topology, subdivision, dual quaternions, or over-budget payloads use the CPU
  fallback. Tangent and arbitrary primvar deltas are not authorable in `UsdSkelBlendShape`; face-varying or uniform
  normals on a deformed prim are omitted rather than published at the bind pose.
- UsdLux light linking is implemented on hdSilk: `collection:lightLink` resolves to a per-draw light mask, and
  a prim a light's collection excludes is not lit by that light. It is gated by analytic cross-backend
  rendering rather than by Storm parity, because Storm's offscreen harness has no linked-light reference.
  `collection:shadowLink` is applied where UsdLux defines it, as a caster restriction: a prim a light's
  shadow collection excludes is not drawn into that light's shadow map, while every prim the light
  illuminates still receives. hdSilk casts raster shadows for authored `UsdLuxDistantLight`s through a
  bounded light-space shadow map and a depth-only pass, gated analytically on D3D12 WARP and Vulkan
  SwiftShader; every other light type, and any device that cannot record a depth-only pass, is reported
  as `OPENUSD_SILK_SHADOW_UNSUPPORTED` rather than silently rendering unshadowed.
  `collection:lightLink` on a `UsdLuxDomeLight` is implemented too, for textured and untextured domes
  alike: up to eight domes carry a stable bit in a bounded dome table, a linked prim receives only the
  skies its collection admits in both the diffuse and the specular response, and a scene that links no
  dome renders byte-identical pixels. A scene with more domes than the table admits publishes no dome
  bits at all and reports `OPENUSD_SILK_LIGHT_LINK_DOME_BUDGET`, and `collection:shadowLink` on a dome
  is reported through `OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_SHADOW_COLLECTION` rather than applied,
  because no dome shadow pass exists to restrict.
  Per-instance linking is resolved onto the composed identities hdSilk publishes under nested
  instancing: the whole instancer chain is walked, each level's contribution is unioned onto the
  identities it actually publishes, and one sparse entry per composed identity carries its own light,
  shadow and dome masks. Every level's path-wide categories are included, the level a prim names as
  well, which is what carries a collection that names a point instancer to the prototypes it
  scatters. The per-instance categories `HdsiLightLinkingSceneIndex` reports for a *nested* level are
  the union over every ancestor instance -- upstream documents that linking through nested instances
  is not resolved -- so hdSilk drops them with a warning naming the prim rather than handing one
  ancestor's collection to identities the author excluded from it; the root instancer's per-instance
  categories and every level's path-wide categories still reach every identity exactly. The 4096-entry
  bound is applied in exactly one place, to the resolved and sparsified table, so prims that link to
  everything and instances whose categories differ but whose masks do not cost nothing; the collector
  bounds only its own transient memory, through a separate constant far above the ABI bound. A path is
  published whole or omitted whole so truncation always fails open.
  See "hdSilk UsdLux light and shadow linking", "hdSilk UsdLux dome linking" and "hdSilk raster
  shadows" in `docs/rendering.md`.

## hdSilk lighting parity

| Feature | Status | Detail |
| --- | --- | --- |
| Deterministic headlight | Implemented and parity-gated | Used when no authored UsdLux light is present |
| `UsdLuxDistantLight` | Implemented subset and parity-gated | Matte and glossy scenes gate; margin 0.609274 |
| `UsdLuxSphereLight` | Implemented subset and parity-gated | Matte point-attenuation scene gates; margin 0.542752 |
| `UsdLuxDomeLight` (untextured) | Ambient-only, parity-gated | Authored scalar/color controls |
| `UsdLuxDomeLight` (textured) | Analytic and pixel-gated | Diffuse/specular prefilter; named mean fallback |
| Display output | Reinhard presentation, identity parity | MCP/Viewer preserve highlights |
| Storm shadow parity | Measured, ungated | Offscreen Storm shadows are not a reference |
| Rect/disk/cylinder area lights | Implemented and self-consistency-gated | Storm renders authored references black |
| Light linking | Implemented, analytically gated | Per-draw light mask on D3D12 WARP and Vulkan SwiftShader |
| Nested-instance linking | Implemented, analytically gated | Composed `parent * count + inner` masks |
| Dome linking | Implemented, analytically gated | Per-draw dome mask, bounded at 8; unlinked pixels byte-identical |
| Dome shadow linking | Excluded and diagnosed | No dome shadow pass exists to restrict casters |
| Shadow linking | Implemented, analytically gated | Caster restriction applied in the depth-only pass |
| hdSilk raster shadows | Distant lights implemented, gated | Bounded light-space maps on WARP and SwiftShader |
| Shadow-map depth resource path | Implemented and gated | Render, sample and re-render a sampled depth target |

`light-distant-shadow` is one of the measured Storm offscreen-harness limits, alongside area lights,
subdivision, and MaterialX. Storm's authored-shadow and shadow-disabled captures are byte-identical
(`disabledAdjustedIoU=1.000000`, Storm hash
`E0713BDEA4E1D9B817A367160F13B3B23D6A06DCC6AC04858D985B8497024B03`). Forcing the candidate shadow
setup in the native shim (`GlfSimpleLight::SetHasShadow(true)`, a 1024x1024 `GlfSimpleShadowArray`,
legacy and scene-index `HdxShadowTask` enablement, and explicit light-space matrices) did not change
the capture. The alternate `enableSceneLights=true` path is not a usable reference either: it failed
the existing doubled-intensity sensitivity probe, so the harness must keep the explicit
`SetLightingState` path that bypasses Storm's shadow-producing UsdLux task flow.

## Data and authoring features

| Feature | Status | Detail |
| --- | --- | --- |
| Stage create/open/masked open | Implemented | File-backed stages and population masks |
| Save, reload, and export | Implemented | Root save plus stage/layer export paths |
| Traversal and hierarchy | Implemented | Bulk prim paths, children, and inspection |
| Stage timing and default prim | Implemented | Authored timeline values and default prim |
| Layer stack and edit targets | Implemented | Root, session, owned local layers |
| Layer muting and sublayers | Implemented | Local stack controls |
| Prim lifecycle | Implemented | Define, override, class, remove, active/load/instance state |
| Scalar and string-like values | Implemented | Bool, integers, floats, doubles, strings, tokens |
| Math and color values | Implemented | Vectors, matrices, quaternions, colors |
| Bulk arrays and time samples | Implemented | Contiguous typed values |
| Attributes and relationships | Implemented | Enumeration, typed access, target editing |
| References | Implemented | Authoring and clearing |
| Payloads | Implemented | Authoring, arc inspection, clearing, and load state |
| Inherits and specializes | Implemented | Authoring and clearing |
| Variants | Implemented | Sets, names, selections, clearing |
| Prim and layer metadata | Implemented | Typed values and clearing |
| World transforms and bounds | Implemented | Time and purpose-aware queries |
| Ordered scheduler | Implemented | Serialized stage ownership and bounded work |
| Change notifications | Implemented | Coalesced stage changes |
| Shared render source | Implemented | Retained exact-stage renderer access |
| Asset resolution | Implemented | Resolver contexts, scoped binding, bulk resolved-asset records |
| Plugin tree discovery | Implemented | Registration and enumeration of unflattened third-party plugin trees |

The complete API examples and native ownership rules are in [Data API](data-api.md).

## Focused schema facades

| Schema family | Current managed coverage | Scope |
| --- | --- | --- |
| `UsdGeom` | Xform, Xformable, Imageable, Mesh, Camera | Focused, not generated-complete |
| `UsdShade` | Material, Shader, NodeGraph, connectability, terminals, binding, Preview Surface, UV Texture | Focused |
| `UsdLux` | Common light API, shaping, six concrete light types | Focused |
| `UsdSkel` | Root, Skeleton, Animation, BlendShape, Binding, joint and binding data | Focused |
| `Pcp` | Detached prim-index node/error inspection | Focused read-only |
| `Ts` | Double-valued spline knots, tangents, extrapolation, evaluation | Focused read-only |
| `UsdValidation` | Registry enumeration and stage/prim validation results | Focused read-only |
| `Ar` and `Plug` | Resolver contexts, bulk resolution, plugin registration and enumeration | Read-only inspection |
| `UsdPhysics` | Scene, body, collision, material, joints, limits/drives, filtering | Authoring only |
| `UsdVol` | Volume, field assets, OpenVDBAsset schema, Field3DAsset | Vulkan and D3D12 single-density OpenVDB gate |
| `UsdRender` | SettingsBase, Settings, Product, Var, Pass | Data API; no RenderDenoisePass in pinned OpenUSD |
| `UsdMedia` | SpatialAudio, AssetPreviewsAPI | Data API |
| `UsdProc` | GenerativeProcedural | Data API |
| `UsdUI` | Backdrop, NodeGraphNodeAPI, SceneGraphPrimAPI | Focused API-schema coverage |

Complete generated bindings for every OpenUSD schema are not a current claim.
UsdShade binding coverage includes purpose, strength, and collection bindings. UsdSkel blend-shape
coverage includes inbetweens, skinning method, and target relationships for authoring and
inspection; hdSilk renders that blend-shape subset on the CPU, including in-between shapes and
normal offsets, and does not evaluate it on the GPU.

## Picking and selection

| Feature | Storm | D3D12 | Vulkan | Metal |
| --- | --- | --- | --- | --- |
| Primitive picking | Implemented | Implemented | Implemented | Implemented source path |
| Face identity | Not supported | Implemented | Implemented | Implemented source path |
| Edge picking | Not supported | Implemented | Implemented | Implemented source path |
| Point picking | Not supported | Implemented | Implemented | Implemented source path |
| Stale-result reporting | Implemented | Implemented | Implemented | Implemented |
| Selection survives switching | Implemented | Implemented | Implemented | Implemented |
| Hosted real-pixel proof | Workflow-gated | Workflow-gated | Workflow-gated | Pending hosted proof |

The Viewer exposes Prim, Face, Edge, and Point targets plus visible-only and x-ray outline modes.
Optional geometry values are not fabricated when a backend returns identity-only results.

## Viewer features

| Feature | Status |
| --- | --- |
| Open/drop `.usd`, `.usda`, `.usdc`, and `.usdz` | Implemented |
| Hierarchy filtering and selection | Implemented |
| Properties, relationships, variants, and payload inspection | Implemented |
| Composition tab data (`PcpPrimIndex`) | Implemented |
| Spline data in the Value tab (`TsSpline`) | Implemented; read-only, no knot authoring |
| Validation panel data (`UsdValidation`) | Implemented |
| Root/session edit targets and layer muting | Implemented |
| Timeline playback and authored timing | Implemented |
| Orbit (including viewport-focused 5-degree arrows), pan, zoom, projection toggle, and framing | Implemented |
| Selected `UsdGeomCamera` adoption | Implemented |
| usdview viewport draw-mode ladder | Implemented |
| Stage AABB/OBB statistics display | Implemented |
| Renderer switching and fallback | Implemented on supported host combinations |
| Picking and selection display | Implemented with limits above |
| Bounded diagnostics and redacted export | Implemented |
| NativeAOT-safe settings store | Implemented |
| Full DCC authoring toolset | Not a goal |

See [Viewer](viewer.md) for controls, exact editing behavior, and automated evidence boundaries.

## Build and evidence workflows

| Workflow | Scope |
| --- | --- |
| [`ci.yml`][ci] | Multi-OS build, managed tests, formatting, coverage, AOT compilation |
| [`native.yml`][native] | Locked native builds, probes, fuzz contract, verified archives |
| [`shaders.yml`][shaders] | DXIL/SPIR-V reproducibility and macOS Metal validation |
| [`package.yml`][package] | Required-mode clean-feed and package-only NativeAOT consumers |
| [`performance.yml`][performance] | Deterministic safety gates and BenchmarkDotNet smoke |
| [`render.yml`][render] | Platform render, switching, picking, lifecycle, and package evidence |
| [`release.yml`][release] | Aggregate reusable release gate |

Detailed commands and evidence expectations are maintained in [Testing](testing.md), not duplicated
here.

## Known limits and non-goals

- Public package availability and API stability are not promised before 1.0.
- OpenUSD C++ types and layouts do not cross the project C ABI.
- Per-element P/Invoke is not accepted on scene or render hot paths.
- The schema facades are intentionally focused rather than generated-complete.
- Storm component picking and x-ray selection are not supported; D3D12 and Vulkan provide them.
- The Viewer is an inspector and focused editor, not a `usdview` clone or full DCC.
- Only `win-x64`, `linux-x64`, and `osx-arm64` runtime packages exist today.
- The curated Storm/hdSilk parity matrix is observed on D3D12 WARP and Vulkan SwiftShader. Metal is
  wired into the macOS render job for the same 25-scene capture, but hosted arm64 currently records a
  CGL capability skip; it must not be reported as passing until a workflow run captures it.
- Volume rendering outside single-density OpenVDB assets, path tracing, proprietary shaders,
  arbitrary MaterialX graphs, and third-party Hydra render delegates are excluded from the next-alpha support claim.

Use [Troubleshooting](troubleshooting.md) to diagnose native loading, plugin discovery, platform,
NativeAOT, and evidence failures without weakening these support boundaries.

[ci]: ../.github/workflows/ci.yml
[native]: ../.github/workflows/native.yml
[package]: ../.github/workflows/package.yml
[performance]: ../.github/workflows/performance.yml
[release]: ../.github/workflows/release.yml
[render]: ../.github/workflows/render.yml
[shaders]: ../.github/workflows/shaders.yml

## UsdGeom data schema surface

| Surface | Current state |
| --- | --- |
| Mesh, xform, imageable, camera | Implemented |
| Subset, curves, points, point instancer, implicit surfaces, tet mesh | Implemented |
| PrimvarsAPI and ModelAPI authoring helpers | Implemented |
| Hydra rendering for these prims | Unchanged; hdSilk/Storm support is tracked separately |
