# Omniverse interchange fixtures

Small, synthetic, redistribution-compatible fixtures used by the Omniverse interoperability
profile's semantic round-trip evidence (`tests/OpenUsd.Tests/OmniverseInterchangeRoundTripTests.cs`,
tracked by `eng/omniverse-profile.json` and the `omniverse-interchange` area of
`eng/support-manifest.json`).

Every file below is repository-authored under this repository's MIT license. They contain no
NVIDIA proprietary content, no Kit/Nucleus/Carbonite/MDL module source, and no assets captured from
or derived from any Omniverse or Kit installation. `UsdPreviewSurface`, `UsdLux`, and the MaterialX
`ND_standard_surface_surfaceshader` node definition referenced below are open USD/MaterialX
standards, not NVIDIA-specific extensions. `mdl-only-omnipbr.usda` names `OmniPBR.mdl` the way an
Omniverse-authored stage does -- as an unresolved identifier string -- and no `.mdl` module is
present or required, because nothing in this repository opens one.

- `unknown-metadata-roundtrip.usda` — covers preservation of vendor-neutral `customData`
  dictionary entries unrecognized by any schema, `custom` (non-schema) attributes, and applied
  API-schema tokens, including a multi-apply instance name (`CollectionAPI:lightLink`) and a
  clearly synthetic, unregistered schema token (`SyntheticVendorMetadataAPI`) that stands in for
  the kind of vendor/tool metadata Omniverse-authored assets attach, without requiring any
  schema plugin to be installed to preserve the token and without implying this repository
  implements any real vendor schema (e.g. SimReady).
- `dual-context-material-anchor.usda` — a single Material prim exposing both the universal
  `outputs:surface` (`UsdPreviewSurface`) terminal and a MaterialX render-context
  `outputs:mtlx:surface` terminal (MaterialX `ND_standard_surface_surfaceshader`) so that
  UsdPreviewSurface-only and MaterialX-aware consumers each resolve a distinct, valid network from
  the same authored material anchor.
- `mdl-only-omnipbr.usda` — a Material whose **only** surface terminal is `outputs:mdl:surface`,
  connected to a shader with `info:implementationSource = "sourceAsset"`,
  `info:mdl:sourceAsset = @OmniPBR.mdl@`, and
  `info:mdl:sourceAsset:subIdentifier = "OmniPBR"`. It is the condition the optional MDL slice
  exists for: with no universal and no MaterialX context, a renderer that reads only the universal
  context sees no surface terminal at all and would draw the mesh as an undiagnosed default grey.
  The module file is deliberately absent, and no NVIDIA MDL source is copied here, because the
  accepted-subset distillation reads only the authored USD input values and never opens the module.
  Its authored inputs are split on purpose: `diffuse_color_constant`,
  `reflection_roughness_constant`, `metallic_constant`, `opacity_constant`/`enable_opacity`,
  `emissive_color`/`enable_emission` and `normalmap_texture` are inside the accepted subset, while
  `subsurface_weight`, `specular_level` and a non-unit `emissive_intensity` are outside it and must
  be reported by name. See `tests/OpenUsd.Tests/MdlOnlyMaterialFixtureTests.cs` for the authored
  shape and `native/hdSilk/tests/hdsilk_probe.cpp` for the rendering behaviour.
- `external-schema/resources/` — a synthetic, repository-authored, MIT-licensed **codeless**
  USD schema plugin (`plugInfo.json` plus `generatedSchema.usda`, following the same codeless
  layout as `schemas/openUsdPhysics/resources/`) declaring one concrete typed schema
  (`OmniverseExternalFixtureWidget`) and one single-apply API schema
  (`OmniverseExternalFixtureTagAPI`). It exists only to prove that a schema/plugin tree supplied
  from *outside* this repository's own `schemas/` tree registers through `PlugRegistry` and
  resolves its real type name, properties, and applied-schema token without flattening, name
  collision, or package-path rewriting (see
  `tests/OpenUsd.NativeCoverage.Tests/ExternalSchemaFixtureProbeTests.cs` and
  `ExternalSchemaFixtureRegistrationTests.cs`). It does not model NVIDIA SimReady, PhysxSchema, or
  any other real vendor schema, and it is never vendored into a shipped package.
- `simready-style-metadata-roundtrip.usda` — a nested vendor-metadata dictionary
  (`SyntheticSimReadyMetadata`) authored both in the root layer's `customLayerData` and in a
  prim's `customData`, loosely modeled on the publicly documented pattern of a vendor-namespace
  dictionary nested under those standard USD fields (see e.g. the SimReady Foundation's open
  specification). It proves nested (`:`-separated) key-path preservation at both layer and prim
  scope survives export/reopen; it does not implement the real SimReady schema, copy any
  validator logic, or claim vendor compliance.
- `lighting/` — textured `UsdLuxDomeLight` fixtures for the mean-radiance ambient **fallback**. What
  these gate is the fallback path, not the prefiltered image-based response: the image is reduced to
  a single solid-angle-weighted mean radiance, every surface normal receives that same value, and the
  result is rotation invariant by construction. The directional response that a supported dome gets
  instead is gated by `SilkEnvironmentLightingTests`, `SilkEnvironmentRetentionTests` and the
  WARP/SwiftShader `SilkEnvironmentLightingConformance` pixel gates. The Radiance HDR images are
  generated by the committed `lighting/generate-dome-fixtures.py`; regenerating them is
  byte-deterministic. Every authored radiance is a power of two, which Radiance RGBE stores
  exactly, so each image has an *exact* analytic mean radiance rather than one that would need a
  tolerance wide enough to hide a real error.
  `tests/OpenUsd.Rendering.Tests/OmniverseDomeFixtureTests.cs` decodes the committed bytes and
  pushes them through the renderer's own resolver, so the images, their documented means, and the
  arithmetic the renderer performs cannot drift apart. That test does not claim Hio reads these
  files; the Hio path is exercised by the render workflows.
  - `dome-white-latlong.hdr` + `dome-untextured-parity.usda` — the parity pair. A constant
    unit-white environment has mean radiance exactly 1.0, so the textured dome must resolve to
    the same ambient as the untextured dome authored beside it with identical emission inputs.
  - `dome-sky-ground-latlong.hdr` + `dome-hdr-mean-ambient.usda` — a bright sky over a dim ground
    with exact per-channel mean `(1.25, 0.625, 0.3125)` and resulting ambient `(1.2, 0.6, 0.3)`.
    The dome authors a non-zero `inputs:specular`, which the fallback reports as unsupported
    rather than approximating; a dome the prefiltered environment carries resolves it instead.
  - `dome-polar-cap-latlong.hdr` + `dome-polar-cap-weighting.usda` — one lit row at the pole,
    whose correct mean is under a third of what an unweighted texel average would report. The
    weighting makes the mean correct as a mean; it does not make the result directional.
  - `dome-unsupported-diagnostics.usda` — one dome per unsupported condition (an `angular`
    mapping, a deliberately absent asset, and `enableColorTemperature`), each of which must name
    its own prim in a diagnostic and fall back to that dome's untextured emission.
  - `light-shadow-linking.usda` — UsdLux light and shadow linking. Two identical quads under a pure
    red `DistantLight` whose `collection:lightLink:excludes` names one of them while keeping the
    `UsdLuxLightAPI` schema default `includeRoot = true`, plus a pure blue `DistantLight` that
    authors no collection and therefore links to everything. Neither light emits green, so the
    expected result is measurable per channel without a reference image. A third quad is excluded
    only from the shadow-link collection, which hdSilk resolves and publishes but applies to
    nothing, because it implements no shadow pass. The fixture references no image and is checked
    for authored structure by
    `tests/OpenUsd.Rendering.Tests/OmniverseLightLinkingFixtureTests.cs`; the resolved masks are
    gated separately by `native/hdSilk/tests/hdsilk_probe.cpp` against a stage it authors itself and
    by `tests/OpenUsd.Rendering.ConformanceTests/SilkLightLinkConformance.cs` on D3D12 WARP and
    Vulkan SwiftShader.

These fixtures are intentionally small and do not attempt to reproduce the full accepted
interchange corpus described in the plan; they exist to keep the profile's semantic round-trip
claims locally provable without external or proprietary assets.
