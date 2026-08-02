# Support matrix

This matrix describes the implementation and declared repository gates for the private
`0.3.0-alpha` baseline. It is not a 1.0 compatibility promise and does not claim that every private
workflow is currently green.

## Status terms

| Term | Meaning |
| --- | --- |
| Implemented | Production source and focused tests exist |
| Workflow-gated | A repository workflow defines required automated execution |
| Compile-only | The host compiles the path but does not execute it in the ordinary CI job |
| Pending hosted proof | Source/contracts exist, but current docs still mark hosted execution pending |
| Not supported | The current API rejects or does not expose the capability |

Workflow badges in the [root README](../README.md) provide the current run status for repository
members.

## Distribution and stability

| Item | Current state |
| --- | --- |
| Version | `0.3.0-alpha` |
| Repository visibility | Private |
| API compatibility | Pre-1.0; public APIs may change |
| Package identity stability | Pre-1.0; IDs may change |
| Public NuGet feed | Not advertised |
| Public GitHub Packages feed | Not advertised |
| Source build | Supported |
| CI/local-feed package build | Implemented |
| Default managed build includes native OpenUSD | No |
| Live-authoring boundary | Source-only sample; not a published package |

See [Getting started](getting-started.md) for the supported source workflow and
[Packaging](packaging.md) for package production and clean-consumer evidence. Compatibility rules are
centralized in [Versioning and compatibility](versioning-compatibility.md).

## Packages

All package projects below are packable from source. Availability here means repository production,
not public-feed publication.

| Package | Framework project | Native requirement |
| --- | --- | --- |
| `OpenUsd.Interop` | 8/9/10 | Native library only when invoked |
| `OpenUsd` | 8/9/10 | Core runtime |
| `OpenUsd.Rendering` | 8/9/10 | None for neutral contracts |
| `OpenUsd.Rendering.Storm` | 8/9/10 | Imaging runtime |
| `OpenUsd.Rendering.Silk` | 8/9/10 | Imaging runtime for scene pages |
| `OpenUsd.Rendering.Silk.D3D12` | 8/9/10 | Imaging runtime; Windows execution |
| `OpenUsd.Rendering.Silk.Vulkan` | 8/9/10 | Imaging runtime; Windows/Linux execution |
| `OpenUsd.Rendering.Silk.Metal` | 8/9/10 | Imaging runtime; macOS execution |
| `OpenUsd.Viewer` | 8/9/10 | Backend runtime of the renderer it activates |
| `OpenUsd.Runtime.Core.win-x64` | `net8.0` carrier | Windows x64 native install |
| `OpenUsd.Runtime.Core.linux-x64` | `net8.0` carrier | Linux x64 native install |
| `OpenUsd.Runtime.Core.osx-arm64` | `net8.0` carrier | macOS arm64 native install |
| `OpenUsd.Runtime.Imaging.win-x64` | `net8.0` carrier | Matching Windows Core package |
| `OpenUsd.Runtime.Imaging.linux-x64` | `net8.0` carrier | Matching Linux Core package |
| `OpenUsd.Runtime.Imaging.osx-arm64` | `net8.0` carrier | Matching macOS Core package |

The runtime projects use `net8.0` to carry RID assets and transitive build targets. The managed
libraries they support target all three production frameworks. `OpenUsd.LiveAuthoring` is intentionally
absent from this package table: it remains source-only sample code until its admission/completion,
correlation, ordering, update-coverage, and health-reporting gaps are resolved by a production consumer.

## Target frameworks

| Surface | `net8.0` | `net9.0` | `net10.0` | Validation |
| --- | :---: | :---: | :---: | --- |
| Packable managed libraries | ✅ | ✅ | ✅ | Multi-TFM build and tests |
| Public API analyzer baseline | ✅ | ✅ | ✅ | Production managed libraries |
| AOT, trim, single-file analyzers | ✅ | ✅ | ✅ | Enabled for production libraries |
| `OpenUsd.LiveAuthoring` sample library | ✅ | ✅ | ✅ | Analyzer-enabled source-only sample |
| Viewer library | ✅ | ✅ | ✅ | Embeddable `OpenUsd.Viewer` package |
| Viewer desktop application | — | — | ✅ | `OpenUsd.Viewer.App` entry point |
| Executable samples and probes | — | — | ✅ | Development and evidence projects |
| Runtime asset package projects | Carrier | — | — | Assets are RID-specific |

Repository development uses SDK `10.0.301`, even when compiling `net8.0` and `net9.0` targets.

## Runtime identifiers

| RID | Native build | Core package | Imaging package | Package gate | Render gate |
| --- | --- | --- | --- | --- | --- |
| `win-x64` | Implemented | Implemented | Implemented | Workflow-gated | Workflow-gated, partly narrowed |
| `linux-x64` | Implemented | Implemented | Implemented | Workflow-gated | Workflow-gated, partly narrowed |
| `osx-arm64` | Implemented | Implemented | Implemented | Workflow-gated | Workflow-gated, partly narrowed |

The narrowed render proofs are the Windows WGL soak, the Windows Avalonia Vulkan composition gate,
the Linux X11 and Wayland Vulkan import smokes, and the macOS Storm child probe. Each needs a
graphics capability a hosted runner does not provide, each records a `status: skipped` evidence
artifact instead of a silent pass, and each has a documented route back to full coverage in
[Testing](testing.md#render-gate-capability-limits).

No runtime package is currently defined for Windows arm64, Linux arm64, macOS x64, or mobile/browser
RIDs.

The native toolchain and archive model are in [Native build](native-build.md). Runtime asset layout
and package-only execution are in [Packaging](packaging.md).

## Viewer renderers

| Viewer kind | Hydra path | Concrete presentation/API | Supported host | Current role |
| --- | --- | --- | --- | --- |
| Storm | Hydra/Storm | WGL, GLX, or NSOpenGL host | Windows, Linux, macOS | Primary |
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
hdSilk now supports texture-backed `UsdPreviewSurface` map binding on all three RHIs by decoding
resolved assets through OpenUSD Hio and uploading cached sampled RGBA8 textures. UDIM expansion and
colour-delta parity with Storm remain outside the current support claim.
It also supports a documented MaterialX projection rather than arbitrary MaterialX code generation:
`ND_standard_surface_surfaceshader` base colour, emission colour, metalness, roughness, and normal can be constant,
driven by a direct image, or folded through constant multiply/add/subtract/clamp/mix nodes. Unsupported nodes are
reported with `TF_WARN` diagnostics that name the material input and node id. That source support is broader than the
Storm parity evidence: the only gated MaterialX-adjacent scene is a hand-authored PreviewSurface equivalent of constant
base colour, roughness, and zero metalness. Storm currently renders the authored MaterialX standard-surface parity mesh
as black in this harness, so the scene is recorded but not gated.
The staged runtime includes `usdMtlx`, MaterialX DLLs, `MaterialXGenGlsl`, and the standard libraries, and the
asset uses the MaterialX `out` terminal; with `UsdImagingGLEngine` scene materials enabled, Storm still covers only
the 347-pixel PreviewSurface anchor against hdSilk's 4314-pixel MaterialX mesh. Where the two overlap, the colour
delta remains only 3, so the divergence is capability rather than shading. MaterialX image, normal, emission,
non-zero metalness, and arithmetic-node parity remain uncovered by a Storm-gated scene.

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

Selection rendering is related but separate from the capability enum:

| Selection behavior | Storm | D3D12 | Vulkan | Metal |
| --- | --- | --- | --- | --- |
| Visible selection | Native yellow highlight | Orange outline | Orange outline | Orange outline |
| Occlusion policy | Storm-native | Visible-only | Visible-only | Visible-only |
| X-ray mode | Not exposed | Not supported | Not supported | Not supported |
| Hosted pixel evidence | Workflow-gated | Workflow-gated | Workflow-gated | Pending hosted proof |

See [Rendering](rendering.md) for request binding, stale results, GPU passes, and lifecycle detail.

## hdSilk Storm parity sign-off

The 1.0 rendering-parity claim is intentionally narrower than "everything hdSilk can parse".
`eng/run-parity-capture.ps1` registers 21 curated scenes. Eighteen are hard gates at
`1.000000` adjusted IoU on D3D12 WARP and Vulkan SwiftShader in ordinary CI; three are
measured but deliberately ungated. Hosted Mesa/llvmpipe Storm runs only 17 registered scenes,
excluding `single-sided-winding`, `bounds-draw-mode`, `origin-draw-mode`, and
`subdivision-catmull-clark` because Mesa Storm differs from conformant-driver Storm. Metal
does not run this curated set today; it is validated by the macOS native/Metal pipeline probe
only, not by the parity-capture matrix below.

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
| Texture wrap modes | `materials-textures` |
| Texture colour spaces | `materials-textures` |
| Texture scale, bias, and fallback | None |
| Texture slots beyond diffuse | None |
| Metallic workflow with non-zero `metallic` | None |
| MaterialX standard-surface authored graph | None |
| MaterialX projection arithmetic/equivalent constants | `materialx-standard-surface-preview-equivalent` |
| Distant light direct transport | `light-distant-exposure` |
| Distant light glossy specular | `light-distant-specular` |
| Sphere light direct transport | `light-sphere-point` |
| Dome light ambient | `light-dome-ambient` |
| Shadows | None |
| Point instancing | `point-instancer-cluster` |
| Points | `points-asymmetric` |
| Basis curves / draw-mode generated lines | `bounds-draw-mode`, `origin-draw-mode` |
| Draw modes | `cards-draw-mode`, `bounds-draw-mode`, `origin-draw-mode` |
| Double-sided/culling | `single-sided-winding` |
| Time-varying transform/display colour | `time-varying-transform-primvar` |
| UsdSkel CPU skinning | `skinned-pennant` |
| Subdivision surfaces | None |

Uncovered or deliberately ungated features:

- `depth-overlap-multiprim` has the thinnest accepted perturbation margin at 0.184333.
- Varying, uniform, and face-varying primvar interpolation modes have no parity scene.
- Other primvar names beyond constant `displayColor` and vertex `st`/normals are not gated.
- Metallic workflow with non-zero `metallic` is not gated.
- Texture `repeat` and `sRGB` are gated; `clamp`, `mirror`, `useMetadata`, `raw`, and linear/auto are not.
- Texture scale, bias, and fallback are authored as identity or not exercised; removing them would still pass.
- Emissive, specular, metallic, roughness, normal, opacity, and occlusion texture slots have no parity scene.
- `emissiveColor`, `clearcoat`, `clearcoatRoughness`, `opacity`, `opacityThreshold`, `ior`, normal maps,
  `displacement`, and `occlusion` are not gated.
- `materialx-standard-surface-constant` is registered but ungated because Storm renders it black in this harness.
- MaterialX images, normal maps, emission, non-zero metalness, and arithmetic chains have no Storm parity gate.
- Sphere-light glossy specular, soft shadows, shaping, dome textures, and image-based lighting are not gated.
- `light-distant-shadow` is registered but ungated; Storm is byte-identical with shadows disabled.
- Multiple point-instancer prototypes/proto-index variation and instanced shadows are not gated.
- Wide point splats, authored curve widths/ribbons, wire draw mode, and shaded-wire draw mode are not gated.
- Authored `cullStyle` tokens have no parity scene.
- Animated materials, textures, lights, and topology have no parity scene.
- GPU skinning and blend shapes are not gated.
- `subdivision-catmull-clark` is measured at 0.931015 and remains ungated. Full Catmull-Clark/Loop/bilinear
  subdivision, creases, and subdivision primvar refinement are not part of the 1.0 parity claim.

At the parity harness complexity, Storm renders the Catmull-Clark probe as a
coarse/control-cage-like surface rather than full refinement. An eager
OpenSubdiv refinement experiment moved hdSilk away from Storm, so subdivision
remains measured but ungated until the remaining divergence is eliminated.

## hdSilk lighting parity

| Feature | Status | Detail |
| --- | --- | --- |
| Deterministic headlight | Implemented and parity-gated | Used when no authored UsdLux light is present |
| `UsdLuxDistantLight` | Implemented subset and parity-gated | Matte and glossy scenes gate; margin 0.609274 |
| `UsdLuxSphereLight` | Implemented subset and parity-gated | Matte point-attenuation scene gates; margin 0.542752 |
| `UsdLuxDomeLight` | Ambient-only and parity-gated | Untextured dome ambient is gated; image IBL is not implemented |
| Shadows | Measured, ungated | `shadow:enable` is diagnostic-only; Storm matched with shadows disabled |
| Light linking | Not implemented | No linked-light filtering; no instanced-shadow parity |

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

The complete API examples and native ownership rules are in [Data API](data-api.md).

## Focused schema facades

| Schema family | Current managed coverage | Scope |
| --- | --- | --- |
| `UsdGeom` | Xform, Xformable, Imageable, Mesh, Camera | Focused, not generated-complete |
| `UsdShade` | Material, Shader, inputs/outputs, Preview Surface, UV Texture | Focused |
| `UsdLux` | Common light API, shaping, six concrete light types | Focused |
| `UsdSkel` | Root, Skeleton, Animation, Binding, joint data | Focused |

Complete generated bindings for every OpenUSD schema are not a current claim.

## Picking and selection

| Feature | Storm | D3D12 | Vulkan | Metal |
| --- | --- | --- | --- | --- |
| Primitive picking | Implemented | Implemented | Implemented | Implemented source path |
| Face identity | Not supported | Implemented | Implemented | Implemented source path |
| Edge picking | Not supported | Not supported | Not supported | Not supported |
| Point picking | Not supported | Not supported | Not supported | Not supported |
| Stale-result reporting | Implemented | Implemented | Implemented | Implemented |
| Selection survives switching | Implemented | Implemented | Implemented | Implemented |
| Hosted real-pixel proof | Workflow-gated | Workflow-gated | Workflow-gated | Pending hosted proof |

The interactive Viewer currently issues primitive picks. Optional geometry values are not fabricated
when a backend returns identity-only results.

## Viewer features

| Feature | Status |
| --- | --- |
| Open/drop `.usd`, `.usda`, `.usdc`, and `.usdz` | Implemented |
| Hierarchy filtering and selection | Implemented |
| Properties, relationships, variants, and payload inspection | Implemented |
| Root/session edit targets and layer muting | Implemented |
| Timeline playback and authored timing | Implemented |
| Orbit, pan, dolly/zoom, projection toggle, and framing | Implemented |
| Selected `UsdGeomCamera` adoption | Implemented |
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
- Edge/point picking and x-ray selection are not supported.
- The Viewer is an inspector and focused editor, not a `usdview` clone or full DCC.
- Only `win-x64`, `linux-x64`, and `osx-arm64` runtime packages exist today.
- The curated Storm/hdSilk parity matrix is gated on D3D12 WARP and Vulkan SwiftShader only; Metal has a
  single-stage native pipeline probe, not 21-scene parity coverage.
- Volumes, path tracing, proprietary shaders, arbitrary MaterialX graphs, and third-party Hydra render
  delegates are excluded from the 1.0 support claim.

Use [Troubleshooting](troubleshooting.md) to diagnose native loading, plugin discovery, platform,
NativeAOT, and evidence failures without weakening these support boundaries.

[ci]: ../.github/workflows/ci.yml
[native]: ../.github/workflows/native.yml
[package]: ../.github/workflows/package.yml
[performance]: ../.github/workflows/performance.yml
[release]: ../.github/workflows/release.yml
[render]: ../.github/workflows/render.yml
[shaders]: ../.github/workflows/shaders.yml
