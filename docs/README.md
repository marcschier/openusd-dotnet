# OpenUsd documentation

OpenUsd is a public `0.6.0-alpha` project with 22 packages published to NuGet.org, including the
five Cesium package IDs that were buildable from this tree but withheld at `0.5.0-alpha`. Start
with the route that matches what you are trying to do, then follow the
deeper design or evidence documents only as needed.

## Choose your route

| Audience | Start here | Continue with |
| --- | --- | --- |
| Evaluating the project | [Support matrix](support-matrix.md) | [Architecture](architecture.md) |
| Building from source | [Getting started](getting-started.md) | [Testing](testing.md) |
| Learning ownership and threading | [Programming model](programming-model.md) | [Data API](data-api.md) |
| Using the data API | [Getting started](getting-started.md) | [Data API](data-api.md) |
| Adding live updates | [Live authoring](live-authoring.md) | [Live authoring sample][live-authoring] |
| Using the desktop Viewer | [Getting started](getting-started.md) | [Viewer](viewer.md) |
| Working on rendering | [Rendering](rendering.md) | [Shader pipeline](shader-pipeline.md) |
| Investigating performance | [Performance](performance.md) | [Testing](testing.md) |
| Building native inputs | [Native build](native-build.md) | [Packaging](packaging.md) |
| Preparing package evidence | [Packaging](packaging.md) | [Testing](testing.md) |
| Reviewing compatibility | [Versioning](versioning-compatibility.md) | [Support matrix](support-matrix.md) |
| Diagnosing a failure | [Troubleshooting](troubleshooting.md) | [Support matrix](support-matrix.md) |

## Learning paths

### Application developer

1. Read [Getting started](getting-started.md) for the source-only distribution model and SDK setup.
2. Confirm the required framework and RID in [Support matrix](support-matrix.md).
3. Read [Programming model](programming-model.md) for ownership, scheduling, and cancellation.
4. Use the public examples in [Data API](data-api.md).
5. Use [Live authoring](live-authoring.md) when one ordered producer updates a shared stage.

### Viewer user

1. Build and stage the matching native runtime through [Getting started](getting-started.md).
2. Launch the repository Viewer against a sample stage.
3. Read [Viewer](viewer.md) for hierarchy, layers, cameras, timelines, selection, and diagnostics.

### Rendering contributor

1. Read [Architecture](architecture.md) for layer and ownership boundaries.
2. Read [Rendering](rendering.md) for neutral state, Storm, hdSilk, RHIs, picking, and selection.
3. Read [Shader pipeline](shader-pipeline.md) before changing checked shader inputs.
4. Read [Performance](performance.md) before changing hot-path boundaries or retained resources.
5. Use [Testing](testing.md) to select the smallest conformance or platform gate.

### Native and package maintainer

1. Read [Native build](native-build.md) for locked inputs and supported hosts.
2. Read [Packaging](packaging.md) for runtime assets and clean-feed consumers.
3. Read [Versioning and compatibility](versioning-compatibility.md) before changing a contract.
4. Read [Testing](testing.md) for native, package-only NativeAOT, and render evidence.

## Documentation map

| Document | Scope |
| --- | --- |
| [Getting started](getting-started.md) | SDK, managed build, native staging, Viewer, samples, AOT |
| [Support matrix](support-matrix.md) | Distribution, packages, frameworks, RIDs, backends, features |
| [Architecture](architecture.md) | Managed/native boundaries, ownership, scheduler, rendering layers |
| [Programming model](programming-model.md) | Lifetimes, scheduler callbacks, cancellation, errors, paths, AOT |
| [Data API](data-api.md) | Stages, layers, prims, values, composition, schemas, and helpers |
| [Live authoring](live-authoring.md) | Ordered batches, validation, backpressure, consumers, and disposal |
| [Rendering](rendering.md) | Neutral contracts, Storm, hdSilk, picking, selection, and composition |
| [Viewer](viewer.md) | Desktop inspection, editing, cameras, settings, and diagnostics |
| [Native build](native-build.md) | Locked OpenUSD source, toolchains, builds, archives, and probes |
| [Packaging](packaging.md) | Runtime package layout, SBOM, symbols, and package-only execution |
| [Versioning](versioning-compatibility.md) | Managed, ABI, package, runtime, and plugin compatibility |
| [Shader pipeline](shader-pipeline.md) | Reproducible shader inputs, outputs, validation, and Metal staging |
| [Performance](performance.md) | Native-call shape, allocations, retained resources, and benchmarks |
| [Testing](testing.md) | Managed, native, package, performance, continuous render, and soak gates |
| [Troubleshooting](troubleshooting.md) | Native loading, plugins, platform shells, AOT, and evidence triage |

## Repository examples

| Project | What it demonstrates |
| --- | --- |
| [`OpenUsd.HelloStage`][hello-stage] | Create, save, reopen, and verify a stage |
| [`OpenUsd.LiveAuthoring`][live-authoring-project] | Ordered update adapter and shared-stage ownership |
| [`OpenUsd.LiveAuthoring.Sample`][live-authoring-sample] | End-to-end authoring, verification, and disposal |

Managed sample builds do not require native binaries. Executing either sample application requires
the matching Core runtime; [Getting started](getting-started.md) explains the supported path.

## Project policies

- [Security](../SECURITY.md) — report vulnerabilities privately and treat USD inputs as untrusted.
- [Contributing](../CONTRIBUTING.md) — build, test, formatting, and change-boundary expectations.
- [License](../LICENSE) and [third-party notices](../NOTICE).

[hello-stage]: ../samples/OpenUsd.HelloStage/README.md
[live-authoring]: ../samples/OpenUsd.LiveAuthoring/README.md
[live-authoring-project]: ../samples/OpenUsd.LiveAuthoring/README.md
[live-authoring-sample]: ../samples/OpenUsd.LiveAuthoring.Sample/README.md
