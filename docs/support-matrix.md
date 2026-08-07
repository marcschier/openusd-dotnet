# Support matrix

This matrix describes the implementation and declared repository gates for the public
`0.5.0-alpha` baseline. It is not a 1.0 compatibility promise and does not claim that every
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
| Version | `0.5.0-alpha` |
| Repository visibility | Public |
| API compatibility | Pre-1.0; public APIs may change |
| Package identity stability | Pre-1.0; IDs may change |
| Public NuGet feed | Published to NuGet.org via OIDC trusted publishing |
| Public GitHub Packages feed | Published alongside each GitHub release |
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
| `OpenUsd.Runtime.Core` | `net8.0` carrier | Core metapackage for all supported RIDs |
| `OpenUsd.Runtime.Core.win-x64` | `net8.0` carrier | Windows x64 native install |
| `OpenUsd.Runtime.Core.linux-x64` | `net8.0` carrier | Linux x64 native install |
| `OpenUsd.Runtime.Core.osx-arm64` | `net8.0` carrier | macOS arm64 native install |
| `OpenUsd.Runtime.Imaging` | `net8.0` carrier | Imaging metapackage for all supported RIDs |
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
[Testing](testing.md#render-gate-capability-limits). The two Vulkan composition limits are hardware
limits, not implementation gaps: hosted Windows has no system Vulkan ICD and SwiftShader implements
neither `VK_KHR_external_memory_win32` nor `VK_KHR_external_semaphore_win32`, so it cannot export a
Vulkan image to a D3D11 shared handle; hosted Linux reaches lavapipe, but the X11/Wayland compositor
reports `supported image handles: (none)`, so no external Vulkan image can be imported.

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
- **MDL SDK is reachable but not integrated.** It is an Apache-2.0 SDK with HLSL and GLSL backends.
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
hdSilk now supports texture-backed `UsdPreviewSurface` map binding on all three RHIs by decoding
resolved assets through OpenUSD Hio and uploading cached sampled RGBA8 textures. UDIM expansion and
colour-delta parity with Storm remain outside the current support claim.
It also supports a documented MaterialX projection plus generated-source paths for graphs outside that projection:
`ND_standard_surface_surfaceshader` base colour, emission colour, metalness, roughness, and normal can be constant,
driven by a direct image, or folded through constant multiply/add/subtract/clamp/mix nodes. Unsupported nodes are
reported with `TF_WARN` diagnostics that name the material input and node id. That source support is broader than the
Storm parity evidence: the only gated MaterialX-adjacent scene is a hand-authored PreviewSurface equivalent of constant
base colour, roughness, and zero metalness. Storm currently renders the authored MaterialX standard-surface parity mesh
as black in this harness, so the scene is recorded but not gated.
The staged runtime includes `usdMtlx`, MaterialX DLLs, `MaterialXGenGlsl`, and the standard libraries, and the
asset uses the MaterialX `out` terminal; with `UsdImagingGLEngine` scene materials enabled, Storm still covers only
the 347-pixel PreviewSurface anchor against hdSilk's 4314-pixel MaterialX mesh. Where the two overlap, the colour
delta remains only 3, so the divergence is capability rather than shading. Generated Vulkan MaterialX is gated by
self-consistency instead: an unlit `ND_constant_color3 -> ND_multiply_color3FA -> ND_surface_unlit` graph renders
against an emissive PreviewSurface equivalent at `maxChannelDelta=1` and `meanChannelDelta=0.237`, and disabling
generated shader selection fails at 187 / 19.574. Metal carries generated `MslShaderGenerator` source through the same
runtime shader cache, but this Windows harness cannot execute a Metal pixel gate.
Sampled `UsdVol` density rendering is currently implemented and conformance-gated only for the Vulkan hdSilk backend
and only when the native profile provides OpenUSD's `hioOpenVDB` field reader. The native shim reads one
`UsdVolOpenVDBAsset` density field through OpenUSD Hio and publishes a bulk cached R32 volume texture; Vulkan uploads
that cache as a 3D texture and the mesh raymarches it. The gate compares the sampled render with a uniform proxy at the
same mean density and with a shifted density grid, so a uniform fallback fails. If the Hio OpenVDB reader is absent, the
gate is reported as a capability skip. Storm is not a usable VDB reference in the current offscreen harness: the same
sampled, uniform-mean, and shifted VDB stages produce identical Storm images, so hdSilk remains gated by the
self-divergence proof rather than cross-renderer parity. D3D12 now has the R32 3D texture upload/bind path needed to try
the same scene on WARP, but it is not advertised as supported because the shifted-grid divergence remains invariant in
that backend (`maxChannelDelta=0`, `meanChannelDelta=0` for sampled versus shifted). Metal's explicit texture slot
binding still needs an executed macOS proof before it can be advertised. Multi-field volumes, non-density field roles
such as temperature or velocity, Field3D rendering, and `UsdVolVolume` prims with several field relationships remain
outside the rendering support claim.

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
| X-ray mode | Not exposed | Not supported | Not supported | Not supported |
| Hosted pixel evidence | Workflow-gated | Workflow-gated | Workflow-gated | Pending hosted proof |

See [Rendering](rendering.md) for request binding, stale results, GPU passes, and lifecycle detail.

## hdSilk Storm parity sign-off

The 1.0 rendering-parity claim is intentionally narrower than "everything hdSilk can parse".
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
context and the Metal hdSilk backend. It runs only the two parity-driver tests on macOS:
deterministic Storm/Metal capture and the perturbation companion that must go red when the Metal
image is flipped, mirrored, transposed, shifted, or sampled at the wrong time. It asserts all
25 registered scenes with no macOS exclusions unless the script is changed to name one; a missing
CGL context, missing Metal device, scene-count mismatch, or missing input is a hard failure in this
required capture path. Those two tests are the curated scene matrix plus its cross-backend
divergence companion, not two individual scenes.

The other `StormSilkParityCaptureDriverTests` entries are targeted backend/detail gates rather than
additional curated scenes: D3D12 frame capture, hdSilk complexity page selection, Vulkan synthetic
MaterialX and texture self-consistency/divergence, non-diffuse texture slots, remaining constant
PreviewSurface inputs, cull-style divergence, and area-light equivalence/divergence. The macOS
parity capture does not run those Vulkan/D3D12 detail gates. Metal detail evidence outside the
curated scene capture remains limited to the native pipeline probe, the ten-entry `mesh.metallib`
contract, IOSurface composition, selection/picking/compute conformance, and the generated
MaterialX-vs-PreviewSurface Metal self-consistency gate.

This path is **pending hosted proof** until a workflow run records that hosted macOS can create the
required CGL OpenGL context and Metal device and shows which scenes gate; it must not be counted as
observed Storm/Metal parity yet.

The harness is driven by the `render` workflow, **not** by ordinary CI, so a green `ci` badge does
not mean parity ran. The full 22-gate matrix passes on Windows with a conformant GPU driver. Hosted
Linux runs and passes it against Vulkan SwiftShader. Hosted Windows currently fails at
`vkCreateInstance: ErrorIncompatibleDriver` because the runner has no usable Vulkan ICD; that is the
`render-unblock-vulkan` limitation and needs a GPU-equipped self-hosted runner.

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
| Shadows | None |
| Point instancing | `point-instancer-cluster` |
| Points | `points-asymmetric` |
| Basis curves / draw-mode generated lines | `bounds-draw-mode`, `origin-draw-mode` |
| Draw modes | `cards-draw-mode`, `bounds-draw-mode`, `origin-draw-mode` |
| Double-sided/culling | `single-sided-winding`, `cull-style-back-self-consistency`, and its divergence companion |
| Time-varying transform/display colour | `time-varying-transform-primvar` |
| UsdSkel CPU skinning | `skinned-pennant` |
| Subdivision surfaces | None |

### Ungated because Storm cannot be the reference

These are not hdSilk implementation gaps. They are cases where the Storm offscreen harness does not
produce a usable reference image for the authored feature.

- **MaterialX standard surface:** Storm shades only the 347-pixel PreviewSurface anchor while hdSilk
  shades 4314 MaterialX pixels. `materialx-standard-surface-constant` is registered, measured, and not
  gated.
- **Catmull-Clark subdivision:** `subdivision-catmull-clark` measures 0.931015 adjusted IoU against
  Storm's coarse/control-cage-like output, so it remains measured and not gated.
- **Shadows:** `light-distant-shadow` is byte-identical with shadows disabled
  (`disabledAdjustedIoU=1.000000`), so it remains registered, measured, and not gated.

Self-consistency gates that deliberately avoid Storm's offscreen reference gaps:

- `texture-wrap-clamp-self-consistency`, `texture-wrap-mirror-self-consistency`, and
  `texture-wrap-use-metadata-self-consistency` compare `repeat` against the candidate wrap mode with UVs
  confined to `[0,1]`, where every wrap mode is mathematically equivalent. Each measured
  `maxChannelDelta=2` / `meanChannelDelta=0.082`. The companion outside-`[0,1]` UV divergence cases require
  the sampler address mode to differ: clamp and `useMetadata`/black each measured `maxChannelDelta=203` /
  `meanChannelDelta=14.950`, and mirror measured `maxChannelDelta=127` / `meanChannelDelta=7.135`.
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

At the parity harness complexity, Storm renders the Catmull-Clark probe as a
coarse/control-cage-like surface rather than full refinement. An eager
OpenSubdiv refinement experiment moved hdSilk away from Storm, so subdivision
remains measured but ungated until the remaining divergence is eliminated.

### Implemented or reachable but not yet parity-gated

- `depth-overlap-multiprim` has the thinnest accepted perturbation margin at 0.184333.
- Varying, uniform, and face-varying texture-coordinate primvar interpolation modes are gated.
- Other primvar names beyond constant `displayColor`, normals, and `st` texture coordinates are not gated.
- Texture `repeat`, `clamp`, `mirror`, `useMetadata`/black, `sRGB`, diffuse `auto`, and raw/linear colour
  space are gated.
- Specular, opacity, and occlusion texture slots have no parity scene.
- `displacement` is not implemented in hdSilk. The material ABI can carry the authored input, but the
  checked mesh shader has no tessellation or vertex/fragment displacement path that changes pixels.
- The Vulkan renderer-consumption path for emissive colour, occlusion, opacity threshold, clearcoat,
  clearcoat roughness, and IOR is self-consistency-gated, but not yet Storm-gated from authored USD.
- MaterialX images, normal maps, emission, non-zero metalness, and arithmetic chains have no Storm parity gate.
- Sphere-light glossy shaping, dome textures, image-based lighting, and area-light texture inputs are not gated.
- Multiple point-instancer prototypes/proto-index variation and instanced shadows are not gated.
- Wide point splats and authored curve widths/ribbons are not gated.
- Authored `cullStyle=back` and the default `backUnlessDoubleSided` are gated for a single-sided mesh and
  a double-sided back-face divergence companion. `front`, `frontUnlessDoubleSided`, and `nothing` remain ungated.
- Animated materials, textures, lights, and topology have no parity scene.
- GPU skinning is not implemented or gated. `docs/rendering.md` records the ABI/shader design that
  must land before this can become a backend gate.
- OCIO final display/view/look correction is not implemented. OpenUSD exposes it through
  `HdxColorCorrectionTask`, but hdSilk does not run the Hdx task graph and has no render-settings ABI
  for OCIO config names, LUT resources, or generated colour-correction shader code.

### Unimplemented or deliberately excluded from the 1.0 parity claim

- Full Catmull-Clark/Loop/bilinear subdivision, creases, and subdivision primvar refinement are not part of the
  1.0 parity claim.
- hdSilk evaluates the narrow CPU blend-shape subset described in `docs/rendering.md` before CPU
  skinning. GPU blend-shape deformation and in-between/primvar/tangent deltas are not implemented.
- Light linking is not implemented.

## hdSilk lighting parity

| Feature | Status | Detail |
| --- | --- | --- |
| Deterministic headlight | Implemented and parity-gated | Used when no authored UsdLux light is present |
| `UsdLuxDistantLight` | Implemented subset and parity-gated | Matte and glossy scenes gate; margin 0.609274 |
| `UsdLuxSphereLight` | Implemented subset and parity-gated | Matte point-attenuation scene gates; margin 0.542752 |
| `UsdLuxDomeLight` | Ambient-only and parity-gated | Untextured dome ambient is gated; image IBL is not implemented |
| Shadows | Measured, ungated | Offscreen Storm shadows are not a reference |
| Rect/disk/cylinder area lights | Implemented and self-consistency-gated | Storm renders authored references black |
| Light linking | Not implemented | No linked-light filtering; no instanced-shadow parity |

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
| `UsdPhysics` | Scene, body, collision, material, joints, limits/drives, filtering | Authoring only |
| `UsdVol` | Volume, field assets, OpenVDBAsset schema, Field3DAsset | Vulkan single-density OpenVDB gate |
| `UsdRender` | SettingsBase, Settings, Product, Var, Pass | Data API; no RenderDenoisePass in pinned OpenUSD |
| `UsdMedia` | SpatialAudio, AssetPreviewsAPI | Data API |
| `UsdProc` | GenerativeProcedural | Data API |
| `UsdUI` | Backdrop, NodeGraphNodeAPI, SceneGraphPrimAPI | Focused API-schema coverage |

Complete generated bindings for every OpenUSD schema are not a current claim.
UsdShade binding coverage includes purpose, strength, and collection bindings. UsdSkel blend-shape
coverage includes inbetweens, skinning method, and target relationships for authoring and
inspection; hdSilk blend-shape rendering is not implemented.

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
| Composition tab data (`PcpPrimIndex`) | ABI/API implemented; UI pending |
| Spline plot data (`TsSpline`) | ABI/API implemented; UI pending |
| Validation panel data (`UsdValidation`) | ABI/API implemented; UI pending |
| Root/session edit targets and layer muting | Implemented |
| Timeline playback and authored timing | Implemented |
| Orbit, pan, dolly/zoom, projection toggle, and framing | Implemented |
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
- Edge/point picking and x-ray selection are not supported.
- The Viewer is an inspector and focused editor, not a `usdview` clone or full DCC.
- Only `win-x64`, `linux-x64`, and `osx-arm64` runtime packages exist today.
- The curated Storm/hdSilk parity matrix is observed on D3D12 WARP and Vulkan SwiftShader. Metal is
  wired into the macOS render job for the same 25-scene capture, but that hosted result is pending
  and must not be reported as passing until the workflow artifact exists.
- Volume rendering outside Vulkan single-density OpenVDB assets, path tracing, proprietary shaders,
  arbitrary MaterialX graphs, and third-party Hydra render delegates are excluded from the 1.0 support claim.

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
