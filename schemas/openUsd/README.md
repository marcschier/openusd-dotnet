# `openUsd` schema coverage

The public facades under `src/OpenUsd` claim coverage of the standard OpenUSD domain
schemas. Nothing checked that claim against the schema registry the pinned native
install actually ships, so a schema or a property added by an OpenUSD bump could
disappear silently, and "covered" could mean anything from a full typed facade to a
type that only exists in a doc sentence.

This directory turns the claim into a gate.

| Path | Generated | Purpose |
| --- | --- | --- |
| `schema-registry.g.json` | yes | Every schema type and declared property, read from the pinned install. |
| `managed-coverage.map.json` | no | **Reviewed source of truth.** What the managed surface represents. |
| [`generate-schema-coverage.py`][generator] | no | Produces and verifies the inventory. |
| [`OpenUsdSchemaCoverageContractTests`][tests] | no | Joins the two and fails on an unreviewed gap. |

## The two halves

**The inventory is generated.** `generate-schema-coverage.py` reads one `plugInfo.json`
and one `generatedSchema.usda` per library from the pinned OpenUSD install and emits
`schema-registry.g.json`. It is a pure function of the pinned install, so `--verify`
fails when the checked-in file drifts:

```powershell
python eng/generate-schema-coverage.py            # regenerate
python eng/generate-schema-coverage.py --verify   # CI form; writes nothing
```

Inputs are discovered from `--install-root`, then `$OPENUSD_ROOT`, then
`native/install/<rid>` for the first RID that carries a schema registry. There are no
machine-specific paths.

The staged install must identify itself. `eng/build-native.ps1` writes
`.openusd-install-metadata.json` next to the install and the native pipeline verifies
it, so the generator requires that file and requires its `openUsdCommit` to equal the
commit `eng/openusd.install.lock.json` pins. Without it the install is unidentified,
and an inventory read from an arbitrary OpenUSD build could be checked in under the
pin. `--allow-unverified-install` waives the file's *presence* and nothing else — it
exists for the parser tests, which run against a synthetic registry; a metadata file
naming a different commit always fails.

`generatedSchema.usda` is flattened, so an inherited property is repeated verbatim on
every descendant class. The inventory attributes each property to the single schema
that introduces it — walking both `bases` and built-in `apiSchemas` — so `visibility`
is stated once, on `UsdGeomImageable`, and not thirty times. `inheritedPropertyCount`
records what was folded away.

The `.usda` scan is structural, not line-based: string literals are blanked in place
(offsets preserved, backslash escapes consumed as a unit) so documentation that
contains a class or property declaration cannot be mistaken for one, and a registered
type with no class in the layer, or a base outside the parsed libraries, fails loudly
instead of silently producing a property-free schema.
[`eng/tests/test_generate_schema_coverage.py`][parser-tests] pins that behaviour:

```powershell
python -m unittest discover eng/tests
```

**The map is reviewed.** `managed-coverage.map.json` is hand-authored. Every schema is
either mapped to the managed types that represent it, or recorded as `uncovered` with a
reason code. The same holds property by property: mapped to a managed member, or listed
in `propertyExceptions` with a reason code.

Nothing about the mapping is inferred from naming. A managed member is a mapping only
because a reviewer wrote it down — but both ends are verified, the property against the
pinned registry and the member against the compiled `OpenUsd` assembly, so a typo or a
deleted member fails the gate rather than passing quietly.

A member counts only if the mapped facade declares it itself, as an instance property,
method or field. Members inherited from `object` or `ValueType` (`ToString`, `Equals`,
`GetHashCode`, `GetType`), static construction helpers (`Wrap`, `TryWrap`, `Apply`,
`Has`) and stage plumbing (`Path`, `Prim`, `Stage`) are rejected, so a mapping cannot
be satisfied by a member that exists on every type and represents nothing.

## What the gate fails on

- a schema in the pinned registry with no entry in the map, or an entry for a schema
  the pinned registry no longer has;
- a declared property that is neither mapped nor recorded as a reviewed exception, or a
  mapped property the schema does not declare;
- a `managedTypes` entry that is not a public type of the `OpenUsd` assembly, or a
  mapped member that none of those types declares as a property of its own;
- an exception reason that is not declared, and a declared reason nobody uses;
- coverage totals falling below the reviewed baseline, so a lost facade member cannot be
  laundered into `propertyExceptions`;
- an inventory generated from a different OpenUSD commit than the pin.

## Coverage today

Counts are over the ten libraries the facades claim, from the pinned OpenUSD 26.05
registry. "Properties" counts each property once, on the schema that declares it.

| Library | Schemas | Covered | Properties | Mapped | Exceptions | On uncovered schemas |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `usdGeom` | 31 | 25 | 126 | 63 | 49 | 14 |
| `usdShade` | 7 | 6 | 6 | 4 | 1 | 1 |
| `usdLux` | 21 | 10 | 53 | 20 | 14 | 19 |
| `usdSkel` | 5 | 5 | 22 | 18 | 4 | 0 |
| `usdVol` | 20 | 7 | 20 | 7 | 0 | 13 |
| `usdRender` | 5 | 5 | 25 | 22 | 3 | 0 |
| `usdMedia` | 2 | 2 | 7 | 7 | 0 | 0 |
| `usdProc` | 1 | 1 | 1 | 1 | 0 | 0 |
| `usdUI` | 4 | 3 | 13 | 9 | 1 | 3 |
| `usdPhysics` | 17 | 17 | 54 | 54 | 0 | 0 |
| **Total** | **113** | **81** | **327** | **205** | **72** | **50** |

So 81 of 113 schema types have a managed facade, and 205 of 327 declared properties
resolve to a typed facade member. The rest is stated, not hidden: 72 properties on
covered schemas are reachable only through the generic `UsdPrim` typed accessors, and 50
belong to schemas with no facade at all — the particle-field family added by OpenUSD
26.05, the light-list and shadow APIs, `UsdGeomScope`, `UsdGeomVisibilityAPI`, and the
`_1` versioned successors the facades deliberately do not target yet.

`usdPhysics` is the only library at full parity: all 17 schemas and all 54 declared
properties resolve to `OpenUsd.Physics` facade members, including the two
multiple-apply schemas whose `__INSTANCE_NAME__` template properties are surfaced per
instance.

### `usdPhysics` is not `openUsdPhysics`

`usdPhysics` above is the **standard** physics schema library shipped by OpenUSD:
`UsdPhysicsRigidBodyAPI`, `UsdPhysicsJoint` and friends, wrapped by `OpenUsd.Physics`.
[`schemas/openUsdPhysics`](../openUsdPhysics/) is a **different** surface — the
codeless plugin this repository owns, generated from its own definition and gated by
`OpenUsdPhysicsSchemaContractTests`. Neither map contains the other's types, and the
gate asserts that.

## What this is not

It is not automatic API generation. Nothing here emits a facade, and adding a schema to
the pinned registry does not produce managed code — it produces a failing gate that a
human answers by writing a facade or by recording a reviewed exception.

Property-level *mapping* is reviewed rather than derived because it cannot be derived
safely: `UsdGeomCamera.horizontalApertureOffset` is surfaced on `UsdGeomCameraState`,
`UsdShadeMaterial.outputs:surface` on `GetSurfaceOutput`, and
`UsdSkelBindingAPI.primvars:skel:jointIndices` on `GetJointInfluences`. Name-shape
heuristics get all three wrong, in both directions.

## Adding or changing a schema mapping

1. Stage the pinned native install and run `python eng/generate-schema-coverage.py`.
2. Answer every new inventory entry in `managed-coverage.map.json`: a mapping, or an
   exception with a reason.
3. Run the parser tests and the managed gate:

```powershell
python -m unittest discover eng/tests
dotnet build tests/OpenUsd.Tests/OpenUsd.Tests.csproj -c Debug -f net10.0
./tests/OpenUsd.Tests/bin/Debug/net10.0/OpenUsd.Tests --treenode-filter "/*/*/OpenUsdSchemaCoverageContractTests/*"
```

Raise `ReviewedCoveredSchemas` and `ReviewedMappedProperties` in the test when coverage
grows; they exist so it cannot shrink unnoticed.

[generator]: ../../eng/generate-schema-coverage.py
[parser-tests]: ../../eng/tests/test_generate_schema_coverage.py
[tests]: ../../tests/OpenUsd.Tests/OpenUsdSchemaCoverageContractTests.cs
