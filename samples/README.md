# Samples

## Purpose and non-goals

The samples show how to consume the managed OpenUsd data API and how to layer an ordered live-authoring
adapter over a scheduler-owned stage.

| Sample | Purpose |
| --- | --- |
| [OpenUsd.HelloStage](OpenUsd.HelloStage/README.md) | Create, save, reopen, and verify one small stage. |
| [OpenUsd.LiveAuthoring](OpenUsd.LiveAuthoring/README.md) | Source-only adapter library for ordered external updates. |
| [OpenUsd.LiveAuthoring.Sample](OpenUsd.LiveAuthoring.Sample/README.md) | Executable demonstration of the adapter. |

These projects are not renderer tutorials, performance benchmarks, or stable application templates.
The live-authoring projects do not contain OPC UA types or a production renderer.

## Prerequisites and native runtime

- .NET SDK 10.0.301. `global.json` rejects other SDK versions.
- A native runtime that matches the host process RID and the managed package version.
- A native toolchain when building the native runtime from source.
- A platform NativeAOT toolchain when publishing. On Windows, use an x64 Visual Studio developer shell.

| Host | RID | Recommended Core reference | Explicit Core package |
| --- | --- | --- | --- |
| Windows x64 | `win-x64` | `OpenUsd.Runtime.Core` | `OpenUsd.Runtime.Core.win-x64` |
| Linux x64 | `linux-x64` | `OpenUsd.Runtime.Core` | `OpenUsd.Runtime.Core.linux-x64` |
| macOS arm64 | `osx-arm64` | `OpenUsd.Runtime.Core` | `OpenUsd.Runtime.Core.osx-arm64` |

These samples need the Core runtime, not `OpenUsd.Runtime.Imaging`. A source checkout expects
`native/install/<rid>` and `native/install/shim/<rid>`. A package consumer references `OpenUsd`
and `OpenUsd.Runtime.Core` at the same version. The explicit `OpenUsd.Runtime.Core.<rid>`
packages remain available for projects that intentionally pin a RID. Never load a runtime for a
different RID, architecture, package version, or C ABI.

Rendering consumers use `OpenUsd.Runtime.Imaging` plus a managed backend. The explicit Imaging
packages are `OpenUsd.Runtime.Imaging.win-x64`, `OpenUsd.Runtime.Imaging.linux-x64`, and
`OpenUsd.Runtime.Imaging.osx-arm64`.

Managed builds do not require native binaries. Executing stage operations does.

## Commands from the repository root

Build the sample projects:

```powershell
dotnet build samples/OpenUsd.HelloStage/OpenUsd.HelloStage.csproj -c Release -f net10.0
dotnet build samples/OpenUsd.LiveAuthoring/OpenUsd.LiveAuthoring.csproj -c Release
dotnet build samples/OpenUsd.LiveAuthoring.Sample/OpenUsd.LiveAuthoring.Sample.csproj -c Release
```

After building the locked `win-x64` native runtime, configure a source run:

```powershell
$nativePaths = @(
    "$PWD\native\install\shim\win-x64\bin"
    "$PWD\native\install\win-x64\bin"
    "$PWD\native\install\win-x64\lib"
)
$env:PATH = ($nativePaths + $env:PATH) -join [IO.Path]::PathSeparator
$env:PXR_PLUGINPATH_NAME = "$PWD\native\install\win-x64\lib\usd"
dotnet run --project samples/OpenUsd.HelloStage/OpenUsd.HelloStage.csproj -c Release -- `
    artifacts\samples\hello-stage.usda
dotnet run --project samples/OpenUsd.LiveAuthoring.Sample/OpenUsd.LiveAuthoring.Sample.csproj -c Release
```

Use the equivalent native install and loader variable for `linux-x64` (`LD_LIBRARY_PATH`) or
`osx-arm64` (`DYLD_LIBRARY_PATH`). The individual sample READMEs contain targeted commands.

## Source versus package consumption

The repository projects use `ProjectReference` so changes to `src/OpenUsd` are tested immediately.
To validate package consumption, first build the packages into a repository-local feed:

```xml
<PropertyGroup>
  <OpenUsdPackageVersion>0.4.0-alpha</OpenUsdPackageVersion>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="OpenUsd" Version="$(OpenUsdPackageVersion)" />
  <PackageReference Include="OpenUsd.Runtime.Core"
                    Version="$(OpenUsdPackageVersion)" />
</ItemGroup>
```

Follow [Pack](../docs/packaging.md#pack) and the
[package-only execution gate](../docs/packaging.md#package-only-execution-gate) for local-feed and
source-mapping setup when validating repository-built packages. Use the RID-agnostic runtime
reference unless the application deliberately pins one RID-specific package. Keep one version
across all shipped packages.

`OpenUsd.LiveAuthoring` is not shipped to NuGet.org and is not part of the package set. External
consumers that want this boundary should vendor `samples/OpenUsd.LiveAuthoring` as source, keep it as a
source `ProjectReference` with its `OpenUsd` reference changed to `PackageReference`, or copy the
specific adapter pattern into their own integration assembly. The final executable still owns the
matching native runtime package.

## Expected output and files

- HelloStage prints the reopened values and writes the requested `.usda` file.
- The live-authoring library build produces managed assemblies for .NET 8, 9, and 10 and no executable.
- The live-authoring executable prints identity, ordering, invalidation, verification, and disposal checks.
- The live-authoring executable copies `Assets/reference.usda` and `Assets/payload.usda` to its output.
- The live-authoring demonstration edits the session layer and does not promise a persisted authored stage.

## Important code

- `OpenUsd.HelloStage/Program.cs` is the smallest create/save/open round trip.
- `OpenUsd.LiveAuthoring/LiveAuthoringContracts.cs` defines data-only updates and detached results.
- `OpenUsd.LiveAuthoring/QueuedLiveAuthoringSink.cs` provides ordering, backpressure, and tail coalescing.
- `OpenUsd.LiveAuthoring/UsdLiveAuthoringHost.cs` owns the scheduler and exact retained render source.
- `OpenUsd.LiveAuthoring.Sample/Program.cs` wires the adapter to a fake render consumer.

The samples call the public managed API. They do not add direct P/Invoke or per-element native loops.

## NativeAOT

HelloStage can be published directly with `PublishAot`. The live-authoring executable uses its existing
`AotSample` switch. Run these Windows commands from an x64 Visual Studio developer shell:

```powershell
dotnet publish samples/OpenUsd.HelloStage/OpenUsd.HelloStage.csproj -c Release -r win-x64 `
    -p:PublishAot=true -o artifacts\samples\hello-stage-aot
dotnet publish samples/OpenUsd.LiveAuthoring.Sample/OpenUsd.LiveAuthoring.Sample.csproj -c Release `
    -r win-x64 -p:AotSample=true -o artifacts\samples\live-authoring-aot
```

Source publishes do not copy `native/install` into the publish directory. Keep the matching native
libraries on the loader path and register the USD plugin tree, or consume the matching runtime package.

## Troubleshooting

- SDK selection failure: install 10.0.301 with `.\eng\install-dotnet.ps1`.
- `DllNotFoundException`: the matching Core runtime is absent from the app or native loader path.
- `BadImageFormatException`: the native architecture or RID does not match the process.
- File-format or plugin errors: stage `usd/plugInfo.json`, or set `PXR_PLUGINPATH_NAME` for a source run.
- ABI or capability error: use `OpenUsd` and `OpenUsd.Runtime.Core` from the same build.
- No live-authored file: the executable intentionally uses the transient session layer.

## Next documentation

- [Data API](../docs/data-api.md)
- [Native build](../docs/native-build.md)
- [Packaging](../docs/packaging.md)
- [Rendering](../docs/rendering.md)
- [Testing](../docs/testing.md)
