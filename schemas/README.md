# Schemas

USD schema sources owned by this repository, plus the licensing evidence for third-party
schemas we were asked to vendor.

| Path | Contents |
| --- | --- |
| [`openUsdPhysics/`](openUsdPhysics/) | The project-owned codeless `openUsdPhysics` schema plugin. |
| [`third-party/physxSchema/`](third-party/physxSchema/) | Licensing evidence for PhysxSchema. Nothing vendored. |

## `openUsdPhysics`

A **codeless** USD schema plugin: `plugInfo.json` carries no `LibraryPath`, so a stock
OpenUSD runtime registers the types from `generatedSchema.usda` alone. There is no
compiled schema library to build, ship, or keep ABI-compatible, and the plugin loads the
same way in the viewer, in tests, and in third-party USD tooling.

Every property is named `openUsdPhysics:<group>:<name>`. That shape is not cosmetic — it
is what makes the precedence rule decidable from the property name alone.

### Precedence

For a single simulated property, the strongest authored opinion wins:

| Rank | Namespace | Source |
| --- | --- | --- |
| 0 | `openUsdPhysics:*` | This repository. |
| 1 | `physx*` | Foreign opinions from other tools. Optional raw input; read when present, never defined here. |
| 2 | `physics:*` | Standard `UsdPhysics`, shipped with OpenUSD. |

An unsupported extension property produces a property-specific diagnostic. It never
suppresses supported semantics on the same object.

`precedence-manifest.json` is the generated, machine-readable form of this table: one
entry per property, with its owning schema, group, type, and rank.

### Layout

| Path | Generated | Purpose |
| --- | --- | --- |
| `openUsdPhysics.schema.json` | no | **Single source of truth.** Library, precedence, coverage, classes. |
| `tools/generate_schema.py` | no | Validates the definition and emits everything below. |
| `schema.usda` | yes | `usdGenSchema` input, for regenerating typed facades. |
| `resources/plugInfo.json` | yes | Codeless plugin registration. |
| `resources/generatedSchema.usda` | yes | Flattened registry layer OpenUSD reads at load. |
| `precedence-manifest.json` | yes | Property-to-source manifest. |
| `../../src/OpenUsd.Physics/Schema/OpenUsdPhysicsTokens.g.cs` | yes | Property-name constants. |
| `../../src/OpenUsd.Physics/Schema/OpenUsdPhysicsSchemas.g.cs` | yes | Typed managed facades. |
| `../../src/OpenUsd.Physics/PublicAPI.Unshipped.txt` | merged | Generated entries merged into the existing file. |

Do not hand-edit a generated file. Edit `openUsdPhysics.schema.json` and regenerate:

```powershell
python schemas/openUsdPhysics/tools/generate_schema.py
```

`--check` regenerates in memory and fails if anything on disk differs, which is the form
to use in CI:

```powershell
python schemas/openUsdPhysics/tools/generate_schema.py --check
```

### Registering the plugin

The plugin root is `schemas/openUsdPhysics`; its resources live in
`schemas/openUsdPhysics/resources`. Point OpenUSD at the resources directory, either by
adding it to `PXR_PLUGINPATH_NAME` before the registry is first touched, or at runtime:

```csharp
OpenUsdNativeRuntime.RegisterPlugins(resourcesDirectory);
```

Registration is additive. It does not disturb the plugins shipped with the OpenUSD
runtime, so standard `UsdPhysics` types keep working exactly as before.

OpenUSD builds its schema registry once, on first use. A plugin registered after that
point reports as loaded but contributes no schema types, so registration has to happen
before anything opens a stage — which is why the native test above uses a child process
rather than registering from inside the shared test host.

`OpenUsd.Physics` also embeds the two resource files, so a shipped application does not
need the repository checkout:

```csharp
string resources = OpenUsdPhysicsSchemaResources.ExtractPluginTo(appDataDirectory);
// put `resources` on PXR_PLUGINPATH_NAME before the process opens its first stage
```

Wiring that path into process startup for packaged applications is left to packaging.

### Managed facades

`generate_schema.py` also emits the managed surface into `src/OpenUsd.Physics/Schema`:
`OpenUsdPhysicsTokens` (one constant per property) and one `readonly struct` facade per
schema class. Both are `.g.cs` — regenerate, never hand-edit.

Property types are restricted to what the managed runtime round-trips through `UsdPrim`:
`bool`, `int64`, `double`, `string`, `token`, `float3`, the corresponding arrays, and
`rel`. The native layer validates `SdfValueTypeNames` strictly, so a `float` attribute
cannot be read as a `double` and the definition uses the wider type instead. Quaternions
are `float[]` ordered `(w, x, y, z)`.

Facades expose `Wrap`, `Prim`, and one member per property; API schemas add `Has`, and
concrete types add `IsA` and `Define`. There is no `Apply`: the managed runtime has no
way to author the `apiSchemas` list yet. That is recorded as a follow-up, not a design
choice — until it lands, apply the schema in USD (or via another tool) and `Wrap` the
prim.

### Coverage and gaps

Every agreed domain is representable by `openUsdPhysics:*` schemas: scene, rigid body,
collision, material, and articulation settings; simulation metadata; character
controllers; the full vehicle stack; articulation tendons and mimic joints; particles,
PBD materials, fluids, particle cloth, diffuse particles, anisotropy, smoothing, and
isosurface extraction; surface deformables; FEM volume deformables; attachments and
auto-attachments; collision filters; and cooked-data references.

No PhysxSchema content is vendored, and none is required. Foreign `physx*` opinions are
treated as an optional raw input: read when present, ranked below `openUsdPhysics:*`, and
never a precondition for any domain above.

Runtime *inputs* — accelerator, brake, and steer commands, controller moves — are
deliberately absent. They are per-frame commands, not authored scene description, and
belong to the runtime command contracts instead.

Domains are listed explicitly under `gaps` in `openUsdPhysics.schema.json`, each with a
`covered` flag and the reason behind it. A domain is either modelled or recorded as an
uncovered gap; it is never silently missing.

All schema classes are single-apply. Multi-apply schemas would rewrite property names to
`<prefix>:__INSTANCE_NAME__:<base>`, which breaks the flat `openUsdPhysics:<group>:<name>`
shape the precedence rule depends on, so they are deliberately deferred. Domains that need
several instances per object — tendons, attachments, tire friction tables — are modelled as
concrete typed prims instead.

## Third-party schemas

See [`third-party/physxSchema/PROVENANCE.md`](third-party/physxSchema/PROVENANCE.md).
Short version: the PhysxSchema USD artifacts do not exist at the PhysX SDK revision this
repository pins, so the licence covering that revision cannot cover them, and nothing was
vendored. The file records the exact URLs, revisions, hashes, and HTTP statuses behind
that conclusion, and the options for changing it.

**Approved decision: option B — do not vendor.** The mismatched PhysxSchema `25.11.1`
artifacts are not vendored, and the project-owned `openUsdPhysics:*` schemas were expanded
instead until every agreed domain is representable without them.

Note the two separate version identifiers, which that file keeps apart on purpose:
`106.4-physx-5.5.0` is the **PhysX SDK repository tag** pinned by `eng/physx.lock.json`,
while the PhysxSchema **USD schema line** declares `25.11.1` in its own
`schemas/physx/VERSION`. The schema is never `106.4`.

## Tests

- [`OpenUsdPhysicsSchemaContractTests`][contract] — no native runtime. Checks that the
  generated artifacts agree with the definition, and pins the codeless and namespace
  invariants.
- [`OpenUsdPhysicsSchemaFacadeTests`][facade] — no native runtime. Checks that the
  generated tokens, facades, and embedded resources agree with the precedence manifest.
- [`PhysxSchemaProvenanceContractTests`][provenance] — no native runtime. Checks that the
  recorded licensing decision matches what is actually in the tree.
- [`OpenUsdPhysicsSchemaProbeTests`][probe] — needs the native runtime. A real OpenUSD
  registry loads the plugin, resolves the types, and keeps standard physics intact.
- [`OpenUsdPhysicsSchemaRegistrationTests`][registration] — needs the native runtime.
  Launches the probe in a clean child process, because OpenUSD builds its schema registry
  once and a plugin registered afterwards never enters it.

[contract]: ../tests/OpenUsd.Tests/OpenUsdPhysicsSchemaContractTests.cs
[facade]: ../tests/OpenUsd.Physics.Tests/OpenUsdPhysicsSchemaFacadeTests.cs
[provenance]: ../tests/OpenUsd.Tests/PhysxSchemaProvenanceContractTests.cs
[probe]: ../tests/OpenUsd.NativeCoverage.Tests/OpenUsdPhysicsSchemaProbeTests.cs
[registration]: ../tests/OpenUsd.NativeCoverage.Tests/OpenUsdPhysicsSchemaRegistrationTests.cs
