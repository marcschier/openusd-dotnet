# Getting started

This guide covers the supported source workflow for the public `0.8.0-alpha` repository. Managed
packages are published to NuGet.org, but the source workflow below does not depend on them; see
[Packaging](packaging.md) for consuming the published packages instead.

## 1. Choose the workflow

| Goal | Native OpenUSD required? | Recommended path |
| --- | :---: | --- |
| Build managed libraries | No | Build `OpenUsd.slnx` |
| Run native-independent managed tests | No | Use `eng/run-managed-tests.ps1` |
| Compile a NativeAOT smoke | No | Publish `OpenUsd.HelloStage` |
| Create, open, or inspect a USD stage | Yes | Build the RID runtime, then run the native probe |
| Run the Viewer | Yes | Build the RID runtime, then use `eng/run-viewer.ps1` |
| Exercise live authoring | Yes | Stage the runtime, then run the live-authoring sample |

The full framework, RID, renderer, and feature status is in [Support matrix](support-matrix.md).

## 2. Prerequisites

Every repository build requires:

- Git.
- .NET SDK `10.0.301`, pinned by [`global.json`](../global.json).
- PowerShell 7 for the cross-platform engineering scripts.

Native builds additionally require the platform toolchain described in
[Native build](native-build.md). The supported native RIDs are `win-x64`, `linux-x64`, and
`osx-arm64`.

If the pinned SDK is unavailable, install it under the ignored `.dotnet/` directory:

```shell
./eng/install-dotnet.ps1
```

On macOS or Linux, the shell installer is also available:

```shell
bash ./eng/install-dotnet.sh
```

After installation, verify the selected SDK:

```shell
dotnet --version
```

Expected output:

```text
10.0.301
```

## 3. Build the managed baseline

```shell
git clone https://github.com/marcschier/openusd-dotnet.git
cd openusd-dotnet
dotnet restore OpenUsd.slnx
dotnet build OpenUsd.slnx -c Release --no-restore
./eng/run-managed-tests.ps1 -Configuration Release
```

The solution build compiles the managed libraries, Viewer, samples, tests, and probes. It does not
download or build OpenUSD.

Managed tests use TUnit on Microsoft.Testing.Platform. Use the repository runner rather than a bare
`dotnet test`; [Testing](testing.md) documents the current runner constraint and targeted selectors.

Build the smallest data-API sample without loading native code:

```shell
dotnet build samples/OpenUsd.HelloStage/OpenUsd.HelloStage.csproj -c Release -f net10.0
```

Executing the sample creates and reopens a stage, so it requires the matching Core runtime. Run it
only after completing the native setup below.

## 4. Build the native runtime

First inspect the exact locked plan without downloading or compiling:

```shell
./eng/build-native.ps1 -Rid win-x64 -PlanOnly
```

Replace `win-x64` with the current supported RID where appropriate. Then fetch, build, and run the
native plus NativeAOT probe:

```shell
./eng/fetch-native.ps1 -Rid win-x64
./eng/build-native.ps1 -Rid win-x64
./eng/run-native-probe.ps1 -Rid win-x64
```

On Windows, run the build from a Visual Studio developer shell. Linux and macOS prerequisites, the
locked OpenUSD revision, and generated install layout are maintained in
[Native build](native-build.md).

The normal managed build and native build remain separate by design. Native outputs are staged under:

```text
native/install/<rid>
native/install/shim/<rid>
```

Do not manually flatten the plugin trees. The native probe, package tests, and Viewer scripts stage
the required layout.

For a direct source run, follow the loader-path and plugin-path commands in the
[HelloStage guide](../samples/OpenUsd.HelloStage/README.md).

## 5. Use the data API

The following names are the current public API:

```csharp
using OpenUsd;
using OpenUsd.Interop;

// Register the packaged plugin tree before touching any stage API. The runtime
// packages deploy it next to the application, so this is a one-line startup step.
string pluginPath = Path.Combine(AppContext.BaseDirectory, "usd");
if (File.Exists(Path.Combine(pluginPath, "plugInfo.json")))
{
    _ = OpenUsdNativeRuntime.RegisterPlugins(pluginPath);
}

using UsdStage stage = UsdStage.Create("scene.usda");
stage.DefinePrim("/World", "Xform");
UsdPrim sensor = stage.DefinePrim("/World/Sensor", "Xform");
sensor.SetDouble("custom:temperature", 42.5);
sensor.SetString("custom:label", "north");
stage.SetDefaultPrim("/World");
stage.Save();
```

This code requires a matching native runtime and data plugin tree at execution time.

> **Register the plugins first.** `UsdStage.Create` needs the OpenUSD plugin tree. If it has not been
> registered, the process **terminates inside native code with no managed exception and no output** —
> on Windows the exit code is `0x80000003`. The build succeeds, so the failure only appears at run
> time and looks like nothing happened at all. Adding a `PackageReference` to `OpenUsd.Runtime.Core`
> deploys the tree to `usd/` beside the application; the snippet above then registers it.

[`OpenUsd.HelloStage`](../samples/OpenUsd.HelloStage/README.md) is the smallest runnable round trip.
The broader `eng/run-native-probe.ps1` proof handles staging and exercises create, open, save, reload,
values, composition, schemas, scheduling, and NativeAOT.

Continue with [Data API](data-api.md) for:

- stage timing, default prims, layers, edit targets, and muting;
- typed scalar, array, geometry, relationship, and metadata APIs;
- references, payloads, inherits, specializes, variants, and population masks;
- focused `UsdGeom`, `UsdShade`, `UsdLux`, and `UsdSkel` facades;
- scheduler-owned shared stages and render sources.

The deep native contract remains documented there rather than repeated in this guide.
Read [Programming model](programming-model.md) before sharing a stage across threads or returning
values from scheduler callbacks.

## 6. Launch the Viewer

After building the native runtime for the current RID:

```shell
./eng/run-viewer.ps1 -Rid win-x64 -StagePath test-assets/minimal.usda
```

The script publishes a staged Viewer, copies the matching native libraries and plugin resources, and
opens the requested stage. The Viewer can inspect hierarchy, properties, layer state, variants,
timeline data, cameras, renderer diagnostics, and selection.

Available Viewer backends depend on the host:

| Host | Choices |
| --- | --- |
| Windows x64 | Storm, D3D12, Vulkan |
| Linux x64 | Storm, Vulkan |
| macOS arm64 | Storm, Metal |

Read [Viewer](viewer.md) for controls and [Rendering](rendering.md) for backend behavior and evidence.

## 7. Run live authoring

The sample library models one ordered producer applying bounded batches to a scheduler-owned stage.
Its executable requires the matching native runtime on the process library path:

```shell
dotnet run --project samples/OpenUsd.LiveAuthoring.Sample/OpenUsd.LiveAuthoring.Sample.csproj -c Release
```

Read [Live authoring](live-authoring.md) for the integration model and the
[executable sample guide](../samples/OpenUsd.LiveAuthoring.Sample/README.md) for loader setup and
expected output.

## 8. NativeAOT and performance checks

Compile the same source-only NativeAOT smoke used by CI. On Windows, use an x64 Visual Studio
developer shell:

```shell
dotnet publish samples/OpenUsd.HelloStage/OpenUsd.HelloStage.csproj -c Release -f net10.0 -r win-x64 -p:PublishAot=true
```

Run the deterministic allocation, lifetime, batching, boundary, and benchmark smoke:

```shell
./eng/run-performance.ps1
```

Native-backed AOT execution is covered by `eng/run-native-probe.ps1` and package-only consumers. See
[Testing](testing.md) and [Packaging](packaging.md) instead of assembling package or loader paths by
hand.

## 9. Common failures

| Symptom | Resolution |
| --- | --- |
| SDK selection error | Install or select exactly `10.0.301` |
| Native runtime not found | Build the matching RID before native-backed execution |
| Plugin or schema discovery fails | Use the probe or Viewer staging script; do not flatten resources |
| `dotnet test` reports zero tests | Use `eng/run-managed-tests.ps1` |
| Metal package build fails off macOS | Produce Metal packages only on macOS with the locked Xcode |
| Public package restore fails | Use `OpenUsd.Runtime.Core` or `OpenUsd.Runtime.Imaging` plus a supported RID |

For loader, plugin, ABI, platform, NativeAOT, and evidence symptoms, use
[Troubleshooting](troubleshooting.md).

## Next steps

- [Support matrix](support-matrix.md) for current availability and limitations.
- [Architecture](architecture.md) before changing boundaries or ownership.
- [Programming model](programming-model.md) for lifetimes, scheduling, cancellation, and errors.
- [Data API](data-api.md) for public API detail.
- [Live authoring](live-authoring.md) for ordered external updates and backpressure.
- [Rendering](rendering.md) and [Shader pipeline](shader-pipeline.md) for renderer work.
- [Versioning and compatibility](versioning-compatibility.md) before changing a public or native contract.
- [Performance](performance.md) before changing native-call shape or retained resource behavior.
- [Native build](native-build.md), [Packaging](packaging.md), and [Testing](testing.md) for release gates.
