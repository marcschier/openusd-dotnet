# OpenUsd.LiveAuthoring.Sample

## Purpose and non-goals

This executable demonstrates the separate
[OpenUsd.LiveAuthoring adapter library](../OpenUsd.LiveAuthoring/README.md). It creates a scheduler-owned
stage, attaches a fake render consumer to the exact retained render source, submits three ordered
batches, verifies authored values and composition, rejects a stage-bound callback result, and checks
disposal order.

The fake consumer intentionally avoids duplicating Viewer or renderer-host logic. This sample is not a
production renderer, persistent authoring tool, OPC UA client, or Pump implementation.

## Prerequisites and matching native runtime

- .NET SDK 10.0.301.
- The matching Core runtime and USD plugin tree.
- No Imaging runtime; the fake consumer does not render pixels.
- A platform NativeAOT toolchain only when publishing.

| Host | RID | Runtime package |
| --- | --- | --- |
| Windows x64 | `win-x64` | `OpenUsd.Runtime.Core.win-x64` |
| Linux x64 | `linux-x64` | `OpenUsd.Runtime.Core.linux-x64` |
| macOS arm64 | `osx-arm64` | `OpenUsd.Runtime.Core.osx-arm64` |

The native runtime RID, architecture, package version, and C ABI must match the `OpenUsd` managed
assemblies. Building does not load native code; executing does.

## Commands from the repository root

Build and run against a locally built Windows runtime:

```powershell
dotnet build samples/OpenUsd.LiveAuthoring.Sample/OpenUsd.LiveAuthoring.Sample.csproj -c Release
$nativePaths = @(
    "$PWD\native\install\shim\win-x64\bin"
    "$PWD\native\install\win-x64\bin"
    "$PWD\native\install\win-x64\lib"
)
$env:PATH = ($nativePaths + $env:PATH) -join [IO.Path]::PathSeparator
$env:PXR_PLUGINPATH_NAME = "$PWD\native\install\win-x64\lib\usd"
dotnet run --project samples/OpenUsd.LiveAuthoring.Sample/OpenUsd.LiveAuthoring.Sample.csproj -c Release
```

The optional first argument selects the stage path. Its parent directory must already exist:

```powershell
dotnet run --project samples/OpenUsd.LiveAuthoring.Sample/OpenUsd.LiveAuthoring.Sample.csproj `
    -c Release -- samples\OpenUsd.LiveAuthoring.Sample\bin\Release\net10.0\live-authoring.usda
```

For Linux or macOS, use the matching native install and set `LD_LIBRARY_PATH` or
`DYLD_LIBRARY_PATH`.

## Source versus package consumption

The executable currently uses `ProjectReference` for both the adapter and `src/OpenUsd`. The adapter is
sample source, not a published package. An external package-based host should:

1. reference or copy the `OpenUsd.LiveAuthoring` adapter source,
2. replace repository `OpenUsd` project references with `PackageReference Include="OpenUsd"`, and
3. add `OpenUsd.Runtime.Core.<rid>` at the same package version to the executable.

Follow [Pack](../../docs/packaging.md#pack) and the
[package-only execution gate](../../docs/packaging.md#package-only-execution-gate) to build a local
feed and configure source mapping when validating repository-built packages.

The Core package stages native libraries and `usd/**`. A package-based bootstrap must register the
copied `usd` plugin tree or set `PXR_PLUGINPATH_NAME` before creating the host. Add an Imaging runtime
only when replacing the fake consumer with a renderer that requires it.

## Expected output and files

Successful output has this shape; native serial values vary:

```text
render source identity retained: True
stage-bound result rejected: True
property batch serials: <before>..<after>
composition invalidation: Composition
variant change sequence: 3
session=True, scalar=True, relationship=True, reference=True, payload=True, active=True,
instanceable=True, variants=True
clean disposal: True
```

`Assets/reference.usda` and `Assets/payload.usda` are copied below the build or publish output. The
program's default stage path is `live-authoring.usda` beside the executable.

The sample selects the session layer and never calls `Save`, so it does not promise a persisted authored
stage. Verification occurs while the scheduler-owned stage is alive. The two source assets are inputs
and are not modified.

## Important code

- `UsdLiveAuthoringHost.CreateAsync` creates one scheduler, retained render source, and bounded sink.
- The three `LiveAuthoringBatch` values demonstrate scalar defaults and samples, relationships,
  references, payloads, active state, instanceability, variants, and composition invalidation.
- `FakeRenderSourceConsumer` retains a lease from the exact source supplied by the host.
- The scheduler callback returns a string of detached verification data.
- Returning `UsdPrim` from a scheduler callback is intentionally rejected as stage-bound state.
- Final checks prove consumer, source, and scheduler disposal in host-owned order.

The sample uses public managed operations. It does not add a direct native call per update element.

## NativeAOT

Publish for the target RID. On Windows, run this from an x64 Visual Studio developer shell:

```powershell
dotnet publish samples/OpenUsd.LiveAuthoring.Sample/OpenUsd.LiveAuthoring.Sample.csproj -c Release `
    -r win-x64 -p:AotSample=true -o artifacts\samples\live-authoring-aot
& .\artifacts\samples\live-authoring-aot\OpenUsd.LiveAuthoring.Sample.exe
```

A source publish does not copy `native/install`; keep the matching directories on the loader path and
set `PXR_PLUGINPATH_NAME`. A package-based publish obtains native files and resources from
`OpenUsd.Runtime.Core.<rid>`. NativeAOT output is RID-specific and must be published on a supported
toolchain.

## Troubleshooting

- `DllNotFoundException`: stage the matching Core runtime or configure the source native loader path.
- File-format or asset error: register the USD plugin tree and confirm both files exist under `Assets`.
- ABI or capability error: align the managed and native runtime versions.
- Sequence exception: preserve the sample's positive, strictly increasing batch sequences.
- Verification failure: confirm the reference and payload assets were copied without modification.
- No durable `.usda` edits: expected; the configured edit target is the transient session layer.
- Renderer integration issue: attach to the supplied scheduler/source pair instead of reopening a path.

## Next documentation

- [Adapter library](../OpenUsd.LiveAuthoring/README.md)
- [Samples overview](../README.md)
- [Data API](../../docs/data-api.md)
- [Rendering](../../docs/rendering.md)
- [Viewer](../../docs/viewer.md)
- [Native build](../../docs/native-build.md)
- [Packaging](../../docs/packaging.md)
