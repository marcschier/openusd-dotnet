# openusd_mdl -- optional MDL material adapter

`openusd_mdl` is a project-owned, optional shared library that hdSilk loads
dynamically while resolving a bound material. It is never linked by the base
runtime, never referenced by a default package, and its absence is a normal,
diagnosed state rather than an error.

## What this adapter does

It distils the **authored USD input values** of an accepted Omniverse MDL
material into the renderer-neutral, `UsdPreviewSurface`-compatible scalar and
texture record hdSilk already publishes. The original MDL network is left
untouched on the stage and in the Hydra material network; distillation exists
only so a bound MDL-only material can be shaded instead of reported as
unshadeable.

It is an **authored-value distillation foundation**, not an MDL SDK-backed
adapter. It is the contract, loader and mapping an SDK-backed implementation can
later be built on behind the same C ABI -- not a substitute for one.

## How hdSilk finds it

hdSilk loads this library at run time and never links it. Resolution is narrow
on purpose, because a shared library loaded by bare name is a shared library an
attacker can substitute:

* The default location is the **absolute sibling** of the hdSilk library, formed
  from that module's own path. There is no bare-library-name load, so neither the
  process directory nor the current directory participates.
* `OPENUSD_MDL_ADAPTER_PATH` overrides it and must be **absolute**; a relative
  value is refused rather than resolved against the working directory.
* On Windows the library is opened with `LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR |
  LOAD_LIBRARY_SEARCH_DEFAULT_DIRS`, so this adapter's own dependencies resolve
  from the directory it was loaded from and from the system directories, and from
  nowhere else.
* That one variable is the only environment the loader reads; it never enumerates
  the environment block.

## What this adapter does not do

* `openusd_mdl` does **not** link, contain, or require the NVIDIA MDL SDK, and
  does not open, parse, or evaluate `.mdl` modules. `info:mdl:sourceAsset` and
  `info:mdl:sourceAsset:subIdentifier` are read as identity only. An input the
  stage does not author is not published, and the consumer's documented
  `UsdPreviewSurface` default stands.
* Neither adapter evaluates MDL-generated shader code or layered BSDFs. A
  distilled MDL material is shaded by the same PreviewSurface-compatible GPU
  pipeline as every other material.

## The SDK-backed sibling

`openusd_mdl_sdk` is a second target behind the same C ABI, built only when
`-DOPENUSD_MDL_SDK_ROOT=<sdk>` names an MDL SDK acquisition. It keeps the
authored-value fast path exactly as it is and adds module evaluation on top:

* compiles the named module from an explicitly configured absolute search path;
* resolves unauthored parameter defaults;
* folds constant expression defaults -- elemental, conversion and copy
  constructors, and parameter aliases;
* resolves texture-valued defaults the SDK materialises.

Authored stage values always win, and each distilled entry records whether it
came from the stage or the module. Any other expression, a layered BSDF, or an
unresolvable resource is reported by parameter name rather than folded.

It links no MDL SDK either: `neuraylib` is header-only, so the target compiles
against the pinned headers and opens the runtime at run time from the absolute
path in `OPENUSD_MDL_SDK_RUNTIME`. The runtime is never redistributed; run
`./eng/fetch-mdl-sdk.ps1` to acquire the pinned, digest-verified baseline for
development.

Anything outside the accepted subset is returned by name in
`unsupported_parameters` so hdSilk can report each dropped input rather than
folding it into an unrelated parameter.

## Accepted subset

| Module | Material | Source for the parameter names |
| --- | --- | --- |
| `OmniPBR.mdl` | `OmniPBR` | NVIDIA-Omniverse/usd-exchange `source/rtx/library/MaterialAlgo.cpp` (Apache-2.0) |
| `OmniGlass.mdl` | `OmniGlass` | the same file's `defineGlassMaterial` |
| `OmniSurface.mdl` | `OmniSurface` | the OmniSurface parameter interface in the Omniverse materials documentation |

See `src/adapter.cpp` for the exact per-input mapping and for the reason each
input is distilled the way it is.

## Building

The adapter is off by default:

```
cmake --preset win-x64-mdl
cmake --build --preset win-x64-mdl
```

`linux-x64-mdl` and `osx-arm64-mdl` build the identical dependency-free source,
but only `win-x64` is gated: `native.yml` builds this configuration and runs the
hdSilk probe against it on Windows alone, so the other two RIDs are buildable
rather than proven.

The build produces `openusd_mdl` in the build tree and installs nothing. hdSilk
finds it beside its own binary, or at the absolute path named by the
`OPENUSD_MDL_ADAPTER_PATH` environment variable.
