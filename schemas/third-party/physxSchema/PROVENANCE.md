# PhysxSchema provenance and redistribution decision

**Decision: NOT VENDORED. Blocked at the pinned PhysX SDK tag `106.4-physx-5.5.0`.**

This directory records the license and identity evidence that was required before any
NVIDIA PhysxSchema USD artifact could be vendored. It deliberately contains no NVIDIA
content: only the evidence, the decision, and the follow-up that the decision requires.

The machine-readable form of everything below is [`provenance.json`](provenance.json),
which the schema contract tests read.

## Two version identifiers, deliberately kept apart

There is no such thing as "PhysxSchema 106.4". Two independent identifiers are involved
and this file never merges them:

| | Repository / tag compatibility baseline | Embedded schema version |
|---|---|---|
| What it versions | The PhysX **SDK** source revision we pin and build against | The PhysxSchema **USD schema line** |
| Value | tag `106.4-physx-5.5.0` (PhysX SDK 5.5.0) | `25.11.1` |
| Where it is stated | `eng/physx.lock.json` | upstream `schemas/physx/VERSION` |
| Resolved commit | `dd587fedd79836442a4117164ea8c46685453c34` | `3ca45ad36e9755f7c8c5bea9f7c57d308d9f0c54` |
| Carries USD schema artifacts | no | yes |

The tag is an SDK tag. It says nothing about the schema version, and the schema line is
released on its own cadence. Any future record must keep both fields and must never
restate the schema version as `106.4`.

## What was required

The plan requires the exact license, source revision, and hashes of the PhysxSchema
codeless artifacts (`plugInfo.json` and `generatedSchema.usda`) to be verified before
vendoring, and requires the packaging plan to fail rather than lean on the PhysX SDK
license by association.

## What was verified

`eng/physx.lock.json` pins the PhysX SDK to `https://github.com/NVIDIA-Omniverse/PhysX.git`
at tag `106.4-physx-5.5.0`. That tag resolves to commit
`dd587fedd79836442a4117164ea8c46685453c34`.

At that commit the repository root contains exactly `.github`, `CONTRIBUTING.md`,
`LICENSE.md`, `README.md`, `SECURITY.md`, `blast`, `flow`, and `physx`. There is no
`schemas/` tree, no `usd/` tree, and no `physxSchema/` directory. A direct request for
`contents/schemas/physx` at that commit returns HTTP 404.

`LICENSE.md` at that commit (blob `4db78a72f6e6f48be4ea78b97ea5b848039bc74e`, 1548 bytes)
is BSD 3-Clause, `Copyright (c) 2008-2024, NVIDIA Corporation`. It governs the C++ SDK
sources that are actually present in the commit.

**Therefore no PhysxSchema codeless artifact exists at the pinned SDK revision, and the
BSD-3-Clause SDK license cannot be applied to such artifacts by association.**

## Where the artifacts actually live

PhysxSchema USD artifacts are published on the `main` branch of the same repository, on a
release line that declares its own version — `schemas/physx/VERSION` reads `25.11.1`, and
that file is the authority for the schema version. At the observed commit
`3ca45ad36e9755f7c8c5bea9f7c57d308d9f0c54`:

| Artifact (under `schemas/physx/source/physxSchema/`) | Git blob SHA-1 | Bytes |
|---|---|---|
| `schema.usda` | `9442b225…` | 254426 |
| `generatedSchema.usda` | `14abb136…` | 139920 |
| `plugInfo.json` | `7b57b6e0…` | 42022 |

SHA-256 of the fetched bytes:

- `schema.usda`
  `0a6099b43e7cb1c9220f986b0fa67a4761b134ab3cd4ff9ea720048d9890a90e`
- `generatedSchema.usda`
  `35c6d9577b1fef8bb37b7c974d65dea1eeabfdeec0b0c1aa57dc399cb402e35b`
- `plugInfo.json`
  `0d9e70dfd2c4d2b3a45920c406b2470bbe7f5d1d34e62d1b073872bd2af43cb3`

Root `LICENSE.md` at that commit (blob `2388f6f9…`, 1517 bytes) is BSD 3-Clause,
`Copyright (c) 2008-2025, NVIDIA Corporation`, and the schema files carry no conflicting
per-file license header. Source-form redistribution of those specific files would be
permitted — but they belong to the `25.11.1` schema line, which is not the revision the
pinned SDK tag identifies.

The SHA-256 values were computed from the fetched bytes; the downloads were then deleted.
No PhysxSchema bytes are stored in this repository.

## Sources that were rejected

- **PyPI `physx-usd-schemas` 25.11.1** publishes the same codeless artifacts as a wheel
  under `license_expression: LicenseRef-NVIDIA-Omniverse`. A `LicenseRef-` identifier is
  by definition not an OSI-approved license, so the wheel is not redistributable here.
- **The Omniverse Kit extension `omni.usd.schema.physx`** is distributed under the NVIDIA
  Omniverse License Agreement and is likewise not redistributable.

## Approved decision: option B — do not vendor

The plan's domain matrix sourced articulations, particles, cloth, and deformables from
"PhysxSchema 106.4" — a version that does not exist. Three options were on the table:

- **A — adopt the schema line.** Vendor the BSD-3-Clause `main`-branch artifacts at
  `3ca45ad36e9755f7c8c5bea9f7c57d308d9f0c54` and record the baseline as two fields: SDK
  tag `106.4-physx-5.5.0` and PhysxSchema `25.11.1`.
- **B — do not vendor.** Ship only standard `UsdPhysics` plus `openUsdPhysics`, and
  grow `openUsdPhysics` to cover the advanced domains directly.
- **C — defer.** Keep the advanced domains out of the initial release.

**Option B was approved.** The mismatched PhysxSchema `25.11.1` artifacts are not
vendored, and the project-owned `openUsdPhysics` schema was expanded until every agreed
domain is representable without them.

## Consequence for this repository

1. No PhysxSchema artifact is vendored — no `plugInfo.json`, no `generatedSchema.usda`,
   and no definition derived from either. Nothing under `schemas/third-party/physxSchema`
   is a schema payload; this directory holds evidence only.
2. `schemas/openUsdPhysics` is the only project-owned schema extension source, and it now
   covers articulation tendons and mimic joints, particles, PBD materials, fluids and
   particle cloth, surface deformables, FEM volume deformables, attachments and
   auto-attachments, collision filters, cooked-data references, vehicles, and character
   controllers. Every domain is marked `covered` in the definition's `gaps` matrix.
3. Its semantics are independently authored from public OpenUSD physics concepts and this
   repository's own runtime requirements. No NVIDIA schema content, property naming,
   documentation, or licensing text is copied or paraphrased.
4. The precedence rule `openUsdPhysics:* > physx* > physics:*` is still authored and
   tested, because a stage may carry `physx*` opinions written by other tools even though
   this repository ships no `physx*` schema definitions. Those opinions are an **optional
   raw input**: read when present, never required, and resolving as untyped custom
   properties. They are ranked below `openUsdPhysics:*`.
5. Runtime packaging must not add a PhysxSchema resource root. Adopting option A later
   would need a new approved plan revision recorded in `provenance.json`, keeping the SDK
   tag and the embedded schema version as two separate fields — the schema is never
   `106.4`.
