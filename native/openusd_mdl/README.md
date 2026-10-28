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
* resolves texture-valued defaults the SDK materialises;
* follows bounded, SDK-proven `(*)` variant relationships rooted in OmniPBR,
  OmniGlass, or OmniSurface, preserving wrapper body constants and argument aliases rather than
  replacing them with the root's defaults.

Authored stage values always win, and each distilled entry records whether it
came from the stage or the module. Any other expression, a layered BSDF, or an
unresolvable resource is reported by parameter name rather than folded.

It links no MDL SDK either: `neuraylib` is header-only, so the target compiles
against the pinned headers and opens the runtime at run time from the absolute
path in `OPENUSD_MDL_SDK_RUNTIME`. The runtime is never redistributed; run
`./eng/fetch-mdl-sdk.ps1` to acquire the pinned, digest-verified baseline for
development.

The current optional adapter ABI is **v7**, paired with hdSilk page ABI **27**.
SDK-proven OmniPBR materials preserve their `specular_level` as a distinct finite
`[0,1]` scalar. The renderer applies it to both dielectric Fresnel endpoints and
the diffuse complement, then mixes the metallic layer independently; it is not
an IOR conversion. Raw/unproven bodies retain the unsupported-input diagnostic.

An authored or SDK-proven `ao_to_diffuse = 0` suppresses the ORM occlusion
channel while preserving the roughness and metallic channels. The pinned current
OmniPBR and ClearCoat modules default this weight to zero; their AO multiplication
has no effect at that value. Nonzero weights remain explicitly partial because MDL
modulates diffuse color whereas the existing PreviewSurface occlusion slot affects
ambient lighting. Missing authored-only weights retain their historical behavior.
This is removal of an inactive contribution, not a memory-driven omission of an
active material input.

Material subidentifiers may be simple names or exact signature-qualified names.
For a qualified name, parameter count and every type name must match the loaded
SDK definition. The signature is never discarded to force a match. A mismatch
reports the current SDK signature and remains unsupported; this does not supply
an older module implementation or infer compatibility between changed interfaces.

The emission extension introduced optional adapter ABI **v5**, paired with hdSilk page ABI **25**. It adds
an explicit texture multiply operand for emission masking, retains the v4 diffuse
texture luminance mix and the v3 authored-UV affine/provenance. The adapter and loader
must be deployed together; v4 adapters and page-24 consumers are incompatible.
Data/session ABIs are unchanged, and optional adapters still ship in no base package.

Anything outside the accepted subset is returned by name in
`unsupported_parameters` so hdSilk can report each dropped input rather than
folding it into an unrelated parameter.

An ordinary material body without a proven variant relationship is not part of
the wrapper profile. Legacy direct-default projection reports that its body is
not proven; unknown variant roots and arbitrary calls are refused or named as
unsupported rather than evaluated as general MDL.

`OPENUSD_MDL_MODULE_PATH` configures the SDK's module search roots. Absolute and
relative file identities, including paths with spaces, are resolved inside
those roots; use one common asset root, not a search path per asset. A URI
outside the configured roots remains a refusal. Cache identity includes the
resolved module filename, material, configuration and generation.

The variant evaluator bounds retained SDK prototype links to eight, expression
depth to 16, reduction work to 4,096 steps, parameter counts to 256, strings to
4 KiB each and aggregate text to 256 KiB. It retains at most 32 module/material
scopes. The SDK can normalize a source variant chain, so source nesting is not
the retained prototype-link count. Empty default textures mean absence;
nonempty missing or unsupported resources retain explicit diagnostics.

## Accepted subset

| Module | Material | Source for the parameter names |
| --- | --- | --- |
| `OmniPBR.mdl` | `OmniPBR` | NVIDIA-Omniverse/usd-exchange `source/rtx/library/MaterialAlgo.cpp` (Apache-2.0) |
| `OmniGlass.mdl` | `OmniGlass` | the same file's `defineGlassMaterial` |
| `OmniSurface.mdl` | `OmniSurface` | the OmniSurface parameter interface in the Omniverse materials documentation |

See `src/adapter.cpp` for the exact per-input mapping and for the reason each
input is distilled the way it is.

OmniPBR `diffuse_tint` multiplies the resolved constant base color and, when present,
the diffuse texture's RGB scale. Texture alpha, transfer function, wrap modes and
asset-origin provenance stay unchanged. Authored tint overrides a variant/default
tint; mixed constant provenance records authored participation. Non-finite values
and overflowed constants remain explicitly reported. Finite texture tints outside
`[0,1]` use the shared renderer's budgeted RGBA32F promotion rather than byte clipping.
The authored-only adapter does not invent an unauthored base color for a tint-only
request. This tint operation does not evaluate normal-map mixing or other named
unsupported controls.

OmniPBR texture grading preserves `(lookup * albedo_brightness + albedo_add) *
diffuse_tint` in RGB scale/bias, including finite signed and extended values.
Alpha and texture ownership/origin stay unchanged. Brightness/addition are inactive
when the module selects the constant diffuse color; only tint modifies that constant.
Malformed, non-finite or overflowing grading remains explicitly reported and leaves
the prior tint-only partial projection intact.

Finite texture-only `albedo_desaturation` is carried separately as a luminance
mix with the pinned MDL weights `(0.212671, 0.715160, 0.072169)`. The shared renderer
mixes linear RGB before the folded channel affine in admitted float storage.
Zero omits the operation; constant-color selection stays unchanged. Authored
negative amounts retain saturation rather than being clamped to zero; amounts
above one also extrapolate. Non-finite values remain explicitly unsupported.

Opacity textures honor both `enable_opacity` and `enable_opacity_texture`.
The SDK enum value is preserved as its integer value, not its declaration index.
`opacity_mode` maps alpha, average RGB, luminance RGB and maximum RGB explicitly;
the default is average, not red. RGB reductions use admitted float storage and
are limited to scalar opacity, leaving displacement and color ports unchanged.
Unknown modes, invalid constants/thresholds and enabled unavailable maps remain
diagnosed. The shared shader discards below a positive threshold and makes every
surviving fragment fully opaque, including equality. Fractional opacity at a zero
threshold is unchanged. Opacity-aware shadows and picking are separate, unfinished
surfaces; this does not claim complete transparent-material fidelity.

For an SDK-proven OmniPBR root or variant, surface emission follows the pinned
`OmniPBRBase` definition: `material_emission` defaults to radiant exitance and
`diffuse_edf` evaluates to `1/pi`, so emitted radiance is color times intensity
times `1/pi`. A color texture replaces the constant; a mask multiplies the
selected color. A mask alone folds constant radiance into its RGB scale; a color
texture plus mask uses an explicit second multiply operand through the existing
hdSilk composite path. Alpha and asset provenance are unchanged. Zero intensity
omits inactive textures. Non-finite/negative intensity and overflowed constant
exitance are diagnosed rather than clipped.
This is visible surface radiance, not emissive geometry lighting other objects.
The dependency-free and unproven-body profiles retain their prior unitless partial
projection and intensity diagnostics; no arbitrary EDF or unknown unit is assumed.
The normalization is grounded in the pinned SDK's
[`diffuse_edf_evaluate`][mdl-libbsdf]
and default `material_emission` mode in `compilercore_known_defs.h` under
[`src/mdl/compiler/compilercore`][mdl-compilercore].

For OmniPBR authored UV set zero, `texture_scale`, `texture_rotate` and
`texture_translate` form the same clockwise-rotation-then-scale affine as the pinned
module helper. The six floats are coordinate values, independent of texture color
scale/bias, and are carried through the existing hdSilk material UV matrix. Negative
and zero scales remain valid. Projected world/object coordinates, other UV sets,
independent detail/clearcoat coordinate streams and unresolved/non-finite transforms remain
explicitly unsupported; no partial transform is applied.
The shared renderer also adjusts the normal-map tangent basis for the retained UV
affine. This does not evaluate arbitrary layered BSDFs or packed MDL normals.

Regular OmniPBR normal textures retain finite signed `bump_factor` and
`flip_tangent_u/v` through encoded-XY scale/bias, leaving Z and alpha unchanged.
The SDK's default vertical flip is retained; zero strength omits the inactive map.
Strengths outside the unit interval require unclipped float texture storage.
Non-finite strength remains diagnosed, as do packed/octahedral
normal-roughness inputs and layered BSDFs.

For SDK-proven OmniPBR with a neutral base normal, a regular detail map can use the
existing normal-map interface exactly: reoriented normal mapping over the geometric
normal is the detail normal itself. An absent/empty base or a resolved base with zero
strength qualifies; an unresolved base does not. Detail strength, tangent
flips, source color space and asset provenance are retained. Zero detail strength
remains neutral, and non-finite values are reported instead of defaulted.

ABI 7 additionally carries a dedicated detail-normal texture and paired UV-affine
rows when the base is active or another map needs a different affine. Both maps
remain separate, full-resolution inputs, oriented independently and combined by
per-fragment RNM. The emission composite remains available concurrently. The
neutral-base/shared-UV case keeps its existing single-map representation.
Packed normal/roughness decoding and arbitrary layered BSDFs remain unsupported.

Native controls cover the exact neutral-base cases and refusal paths, including
signed strength and a zero-strength base. Actual composed reference/detail stages
publish identical 341-byte material commands. In the saved warehouse census,
six requests covering 78 placements gain a normal map; the other 871 records remain
unchanged.

Real RTX 5070 D3D12 and Vulkan controls now match the standard normal-map reference
byte-for-byte in HDR and PNG, both with an absent base and a zero-strength base. All
143 foreground pixels differ from the flat-normal control. A diffuse-plus-detail
pair with matching UVs also matches its reference; incompatible UVs preserve the
diffuse-only pixels and retain the detail-input diagnostic. These are bounded
constant/texture controls, not general two-active-normal blending.

Roughness and metallic texture influence can be folded into the selected channel's
scale/bias as `texture * influence + constant * (1 - influence)`. Fractional projection
requires a known finite constant and influence within `[0,1]`; unsupported ranges
are reported instead of silently clamped. Zero influence keeps the constant without
publishing an inactive map. Enabled ORM selects its G/B channels ahead of standalone
roughness/metallic maps, preventing duplicate competing inputs. Occlusion keeps ORM's
unmodified R channel. The SDK resolves actual module defaults; the dependency-free
authored-only profile retains its historical full-texture behavior when no influence
or module default was supplied.

When the selected standalone or ORM texture is unavailable, an exact zero influence
is still recognized as neutral and keeps the constant. Nonzero influences remain
unsupported: the pinned module blends the lookup even without a valid map, so absence
alone does not prove equivalence to the constant. Malformed controls and texture
resources retain their diagnostics. This diagnostic correction does not change
shading records or establish new warehouse fidelity.

The SDK wrapper probes distinguish empty module-default maps from unresolved
nonempty resources for both standalone and ORM selection. They check that zero
influence preserves constants without hiding a missing resource, while nonzero
influence with no selected map remains diagnosed.

## Building

The adapter is off by default:

```
cmake --preset win-x64-mdl
cmake --build --preset win-x64-mdl
```

For adapter-only work, configure this directory directly instead of the full native
project. This builds the same targets and public-C-ABI probes without OpenUSD,
OpenGL, Vulkan or their runtime dependencies:

```powershell
cmake -S .\native\openusd_mdl -B D:\temp\openusd-mdl -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build D:\temp\openusd-mdl
ctest --test-dir D:\temp\openusd-mdl --no-tests=error --output-on-failure
```

Run these commands from a configured native compiler environment. Optional
`OPENUSD_MDL_SDK_ROOT`, `OPENUSD_MDL_SDK_RUNTIME_DIR` and
`OPENUSD_MDL_REAL_MODULE_ROOT` settings enable the existing SDK and real-module
probes; they do not acquire dependencies or change either adapter's linkage.
`OPENUSD_BUILD_NATIVE_TESTS=OFF` builds only the selected adapter libraries.
Adapter-only probes do not qualify the hdSilk loader, renderer, or base packages.

`linux-x64-mdl` and `osx-arm64-mdl` build the identical dependency-free source,
but only `win-x64` is gated: `native.yml` builds this configuration and runs the
hdSilk probe against it on Windows alone, so the other two RIDs are buildable
rather than proven.

The build produces `openusd_mdl` in the build tree and installs nothing. hdSilk
finds it beside its own binary, or at the absolute path named by the
`OPENUSD_MDL_ADAPTER_PATH` environment variable.

## Real standalone module qualification

The SimReady warehouse tooling can acquire pinned, first-party BSD-noticed OmniPBR and
OmniGlass sources independently of Kit; see [the warehouse guide](../../docs/simready-warehouse.md).
An SDK-enabled build also produces `mdl_module_probe`. Give it the absolute acquired
module directory and set `OPENUSD_MDL_SDK_RUNTIME` to the installed `libmdl_sdk` file.
Unlike optional-availability probes, it fails if the real modules do not compile or their
known defaults cannot be obtained through the adapter's C ABI.

`OPENUSD_MDL_REAL_MODULE_ROOT` enables the opt-in `mdl_real_module_defaults` CTest for that
directory. It checks actual PBR diffuse/roughness and glass IOR defaults, while printing
every unsupported projection parameter. Passing establishes compiled module defaults,
not complete BSDF evaluation, full warehouse material support, or RTX-equivalent output.

The same real-module root enables `mdl_sdk_wrapper_variants`. Supplying
`OPENUSD_MDL_WAREHOUSE_SOURCE_ROOT` additionally enables
`mdl_warehouse_wrapper_projection`, which consumes the unchanged warehouse
wrappers. `mdl_sdk_wrapper_rejections` covers the public refusal and input-bound
contracts without acquiring the warehouse. The wrapper cases also check
authored precedence, texture origin/gamma/owner, same-name module isolation,
result release and cache-generation changes. They do not make unprojected UV,
tint, opacity, glass/volume or layered-BSDF controls supported.

With the separate verified OmniSurface acquisition profile,
`OPENUSD_MDL_OMNISURFACE_MODULE_ROOT` also enables
`mdl_warehouse_omnisurface_projection`. It compiles the unchanged translucent-plastic
wrapper and checks basic scalar projection, authored overrides, refusal of mismatched
signatures, and explicit unsupported reflection/transmission/subsurface controls.
It does not reuse the OmniPBR dielectric curve or claim complete OmniSurface shading.
The four fork-sourced, BSD-noticed dependencies remain external; acquisition provenance
and configuration are documented in the warehouse guide.

`OPENUSD_MDL_LEGACY_MODULE_ROOT` additionally enables `mdl_sdk_legacy_pbr` against the
separately acquired OmniPBR 2.1.0 profile. The real SDK compares the full historical
signature, preserves authored values, and isolates current/legacy adapter contexts.
This is an opt-in test setting, not permission to replace the current module root.
For runtime selection, set `OPENUSD_MDL_LEGACY_PBR_ROOT` to the verified legacy directory
before starting the host. It is disabled by default and tries one alternative only when
a compiled `OmniPBR.mdl` lacks the exact requested `OmniPBR(...)` signature. The SDK
matches every original type without rewriting the signature or authored inputs.
Successful current requests, unqualified names, missing modules, and compilation/body
failures remain on the strict path. Configuration/candidate failures preserve the
primary refusal and append bounded diagnostics. The existing per-configuration SDK
scope isolation prevents same-name modules from leaking between requests.

The selected source is disclosed through the result diagnostic and the matched hdSilk
bridge. Use `mdl_sdk_legacy_routing` and `mdl_sdk_legacy_routing_invalid` to exercise the
opt-in and refusal paths. `hdsilk_probe --mdl-compatibility-diagnostic <current-module>
<legacy-signature>` checks that a configured real adapter's successful selection
diagnostic survives the bridge. The enabled replay now produces partial projections for
all 877 eligible warehouse requests without changing the preceding 871 successful
records. A fresh single-process run matches the checkpointed replay exactly. This is
not full material fidelity. After correcting namespaced-signature framing in the
scene-index/material bridge, the actual current and routed legacy triangle paths produce
identical HDR and PNG on RTX 5070 D3D12 and Vulkan. This is a constant-material control,
not a complete warehouse image or proof of every mapped lobe/texture.

The wrapper suite also checks that an SDK-rejected search-path configuration does not
poison a subsequent cold lookup on a previously valid adapter. This recovery already
works through the module lookup's path reapplication; it is not a new fallback that
ignores configuration errors.

`mdl_sdk_udim` covers default and inherited, filesystem-backed `<UDIM>` texture sets.
The SDK's resolved tile coordinates and filenames are checked before publishing one
absolute mask through the existing texture record. Every tile must match the mask and
be a regular file; supported tile numbers are 1001..1999, with at most 256 tiles and
256 rectangular atlas cells. Missing sets, larger sets, animated resources, other
UV-tile encodings, selectors and container members remain explicitly unsupported.
The adapter does not decode, resize, or remove tiles.

The SDK can return regular-expression syntax in its filesystem mask. Projection uses
the SDK's original MDL marker plus the resolved tile directory and verifies every
substitution instead of handing that expression to the renderer. Native controls cover
two tiles, inherited tint/metadata, an authored single-file override, the exact
256-tile/cell boundary, missing tiles, excessive sparse extent and a 257-tile refusal.
All 877 existing warehouse request records remain unchanged with this extension.
The native `hdsilk_probe --mdl-udim-stage-match <plugins> <authored-stage> <wrapper-stage>`
control also verifies that the actual composed one-mesh stages publish identical material
command bytes, including the absolute UDIM asset mask. The preceding adapter fails this
same control for the wrapper. Real RTX 5070 D3D12 and Vulkan controls using one-pixel
red and green PNG tiles produce byte-identical HDR/PNG for both paths; both tiles are
visible, with no missing-texture or fallback diagnostic. The ASCII PPM files used by
the SDK-only tests could not be decoded by this installed Hio profile and must not
be used as pixel evidence.

`mdl_sdk_alias_color_probe` additionally checks that authored texture decoding
metadata follows parameter aliases and copy constructors. Explicit destination
metadata still wins. It uses the existing default-expression subset, keeps
unproven bodies diagnosed, and can load a preserved adapter by absolute path to
demonstrate the pre-correction raw-versus-sRGB failure.

[mdl-libbsdf]:
  https://github.com/NVIDIA/MDL-SDK/blob/1b9592c1b086b691b44d1a30eee4827d1ff55b8c/src/mdl/jit/libbsdf/libbsdf.cpp
[mdl-compilercore]:
  https://github.com/NVIDIA/MDL-SDK/tree/1b9592c1b086b691b44d1a30eee4827d1ff55b8c/src/mdl/compiler/compilercore
