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
- Current project docs still mark the hosted macOS end-to-end render proof as pending.

Use [Troubleshooting](troubleshooting.md) to diagnose native loading, plugin discovery, platform,
NativeAOT, and evidence failures without weakening these support boundaries.

[ci]: ../.github/workflows/ci.yml
[native]: ../.github/workflows/native.yml
[package]: ../.github/workflows/package.yml
[performance]: ../.github/workflows/performance.yml
[release]: ../.github/workflows/release.yml
[render]: ../.github/workflows/render.yml
[shaders]: ../.github/workflows/shaders.yml
