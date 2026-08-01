# OpenUsd.LiveAuthoring

## Purpose and non-goals

`OpenUsd.LiveAuthoring` is a managed adapter library for applying ordered external updates to one
scheduler-owned OpenUSD stage. It provides data-only update records, validation, bounded admission,
tail coalescing, scheduler execution, and ownership of the exact retained render source.

This directory is source-only sample code. It is not a published NuGet package and does not provide a
stable package contract in the `0.3.x` line.

Keep these three scopes separate:

1. **Adapter library:** this directory contains reusable contracts and queue/host behavior. It has no
   entry point.
2. **Executable sample:** [OpenUsd.LiveAuthoring.Sample](../OpenUsd.LiveAuthoring.Sample/README.md)
   constructs batches, uses a fake render consumer, and verifies the adapter.
3. **External OPC UA Pump integration:** a Pump-owned sink maps monitored values and ordering into these
   contracts. That integration is outside this repository and outside this library.

The adapter is not an OPC UA client, renderer, stage viewer, transaction engine, or multi-producer
ordering service. It does not promise rollback after a native failure partway through a validated batch.

## Prerequisites and matching native runtime

- .NET SDK 10.0.301.
- No native runtime for a managed build or queue-only tests.
- A matching Core runtime when an executable creates or opens a stage.
- A platform NativeAOT toolchain when publishing an executable.

| Host | RID | Runtime package |
| --- | --- | --- |
| Windows x64 | `win-x64` | `OpenUsd.Runtime.Core.win-x64` |
| Linux x64 | `linux-x64` | `OpenUsd.Runtime.Core.linux-x64` |
| macOS arm64 | `osx-arm64` | `OpenUsd.Runtime.Core.osx-arm64` |

The executable host must use the same `OpenUsd` and runtime package version. The adapter needs no Imaging
runtime unless the chosen render consumer separately uses Hydra, Storm, or another imaging backend.

## Commands from the repository root

Build all three target frameworks:

```powershell
dotnet build samples/OpenUsd.LiveAuthoring/OpenUsd.LiveAuthoring.csproj -c Release
```

Run the queue and validation tests, which do not require native binaries:

```powershell
dotnet build tests/OpenUsd.LiveAuthoring.Tests/OpenUsd.LiveAuthoring.Tests.csproj `
    -c Release -f net10.0
.\eng\run-managed-tests.ps1 `
    -Project tests/OpenUsd.LiveAuthoring.Tests/OpenUsd.LiveAuthoring.Tests.csproj `
    -Framework net10.0 `
    -Configuration Release
```

This project is a library, so `dotnet run` is not valid. Run the separate executable after configuring
the matching native runtime:

```powershell
dotnet run --project samples/OpenUsd.LiveAuthoring.Sample/OpenUsd.LiveAuthoring.Sample.csproj -c Release
```

See the executable README for exact source-runtime setup and NativeAOT commands.

## Source versus package consumption

Inside the repository, the adapter references `src/OpenUsd` with `ProjectReference`. The adapter itself
is sample source and is not packed. An external application can:

- vendor this directory and keep it as a source `ProjectReference`, replacing its `OpenUsd` project
  reference with an `OpenUsd` package reference;
- copy the adapter types that fit its integration boundary; or
- use the pattern as a starting point for its own boundary assembly.

The final executable, not an abstract library layer, should select
`OpenUsd.Runtime.Core.win-x64`, `.linux-x64`, or `.osx-arm64` at the exact managed package version.
Use the local feed and source-mapping process in [Pack](../../docs/packaging.md#pack) and the
[package-only execution gate](../../docs/packaging.md#package-only-execution-gate) when validating
repository-built packages. Do not add OPC UA packages to this adapter merely to consume it from Pump.

## Expected output and files

A Release build produces `OpenUsd.LiveAuthoring.dll` for `net8.0`, `net9.0`, and `net10.0`. It produces
no executable and authors no USD file. The targeted test command reports passing queue, validation,
snapshot, coalescing, cancellation, and disposal tests.

Stage files and console output belong to the hosting executable. With the default session edit layer,
live edits are transient unless the host deliberately authors and saves the root layer.

## Important code and behavior

- `LiveAuthoringContracts.cs` defines `ILiveAuthoringSink`, data-only `LiveStageUpdate` records,
  `LiveScalarValue`, options, and detached results.
- `LiveAuthoringValidation.cs` rejects invalid paths, identifiers, values, and batches before admission.
- `QueuedLiveAuthoringSink.cs` admits one logical producer in strictly increasing sequence order.
- `UsdStageBatchExecutor.cs` selects the edit layer and translates each validated update to public APIs.
- `UsdLiveAuthoringHost.cs` owns one `UsdStageScheduler` and one `UsdStageRenderSource`.

One logical producer may have multiple outstanding submissions, but independent producers must
serialize before calling the sink. A full queue can replace only its tail when both batches have the
same non-empty coalescing key. Cancellation before admission prevents execution. Cancellation after
admission only cancels that caller's wait; the ordered edit is still drained.

The render consumer must attach to the scheduler and retained source supplied by the host. Reopening the
stage path would create a different stage identity and break authoring/render synchronization.

## NativeAOT

The library targets .NET 8, 9, and 10 with trimming, AOT, and single-file analyzers enabled. Publishing
NativeAOT is an executable operation; use the sibling executable or the external host. On Windows, run
the publish command from an x64 Visual Studio developer shell:

```powershell
dotnet publish samples/OpenUsd.LiveAuthoring.Sample/OpenUsd.LiveAuthoring.Sample.csproj -c Release `
    -r win-x64 -p:AotSample=true -o artifacts\samples\live-authoring-aot
```

Use the matching RID and Core runtime. The contracts avoid boxed domain payloads and runtime code
generation; `LiveScalarValue` is an explicit NativeAOT-safe discriminated value.

## External OPC UA Pump integration

The external Pump adapter should:

1. Use one serialized producer and assign strictly increasing `LiveAuthoringBatch.Sequence` values.
2. Map Pump domain values to `LiveScalarValue` without adding OPC UA types to this library.
3. Use stable coalescing keys only when a newer pending snapshot fully supersedes the older one.
4. Forward cancellation and observe returned tasks so admission, native, and disposal failures reach
   Pump health reporting.
5. Give the selected renderer the host-provided scheduler and render source; never reopen the stage path.

Pump owns monitored-item semantics, reconnection, namespace mapping, health reporting, and deployment.
This repository neither builds nor validates that external integration.

## Troubleshooting

- "`dotnet run` is not supported": run `OpenUsd.LiveAuthoring.Sample`, not this library project.
- Sequence rejection: serialize producers and submit positive, strictly increasing sequence numbers.
- Queue stalls: observe every returned task and check whether the bounded consumer is making progress.
- Missing persisted edits: the default edit target is the session layer, which `UsdStage.Save` omits.
- Stage-bound result exception: return detached data from scheduler callbacks, not `UsdPrim` or `UsdStage`.
- Native load or ABI error: fix the final executable's Core runtime RID/version, not this adapter.

## Next documentation

- [Executable sample](../OpenUsd.LiveAuthoring.Sample/README.md)
- [Samples overview](../README.md)
- [Data API](../../docs/data-api.md)
- [Architecture](../../docs/architecture.md)
- [Rendering](../../docs/rendering.md)
- [Packaging](../../docs/packaging.md)
