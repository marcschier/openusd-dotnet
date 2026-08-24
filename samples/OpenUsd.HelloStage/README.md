# OpenUsd.HelloStage

## Purpose and non-goals

HelloStage is the smallest executable data-API sample. It creates a file-backed stage, defines `/World`
and `/World/Hello`, authors two custom values, saves the root layer, reopens the stage, and verifies the
round trip.

It is not a renderer, schema tutorial, live-authoring host, or performance example. It uses the public
managed facade and does not call the C ABI directly.

## Prerequisites and matching native runtime

- .NET SDK 10.0.301.
- A writable output location.
- A platform NativeAOT toolchain only when publishing.
- The Core native runtime matching the process. Prefer `OpenUsd.Runtime.Core`; explicit packages are
  `OpenUsd.Runtime.Core.win-x64`, `OpenUsd.Runtime.Core.linux-x64`, and
  `OpenUsd.Runtime.Core.osx-arm64`.

Use the same version for `OpenUsd` and the runtime package. `OpenUsd.Runtime.Imaging` is not needed.
Building requires only the managed projects; running requires `openusd_dotnet`, OpenUSD, and the USD
plugin resource tree.

## Commands from the repository root

Build and run against a locally built Windows runtime:

```powershell
dotnet build samples/OpenUsd.HelloStage/OpenUsd.HelloStage.csproj -c Release -f net10.0
$nativePaths = @(
    "$PWD\native\install\shim\win-x64\bin"
    "$PWD\native\install\win-x64\bin"
    "$PWD\native\install\win-x64\lib"
)
$env:PATH = ($nativePaths + $env:PATH) -join [IO.Path]::PathSeparator
$env:PXR_PLUGINPATH_NAME = "$PWD\native\install\win-x64\lib\usd"
dotnet run --project samples/OpenUsd.HelloStage/OpenUsd.HelloStage.csproj -c Release -- `
    artifacts\samples\hello-stage.usda
```

With no argument, the deterministic default is `hello-stage.usda` in the process working directory:

```powershell
dotnet run --project samples/OpenUsd.HelloStage/OpenUsd.HelloStage.csproj -c Release
```

The sample creates a missing parent directory and replaces an existing file at the selected path.
For Linux or macOS, use the matching install and set `LD_LIBRARY_PATH` or `DYLD_LIBRARY_PATH`.

## Source versus package consumption

This repository uses:

```xml
<ProjectReference Include="..\..\src\OpenUsd\OpenUsd.csproj" />
```

To consume the published packages instead, use one property for the matching version:

```xml
<PropertyGroup>
  <OpenUsdPackageVersion>0.12.0-alpha</OpenUsdPackageVersion>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="OpenUsd" Version="$(OpenUsdPackageVersion)" />
  <PackageReference Include="OpenUsd.Runtime.Core"
                    Version="$(OpenUsdPackageVersion)" />
</ItemGroup>
```

The packages are published to NuGet.org, so no feed configuration is required. To test
repository-built packages before they are published, build them into a local feed and configure
source mapping as described by [Pack](../../docs/packaging.md#pack) and the
[package-only execution gate](../../docs/packaging.md#package-only-execution-gate). Use the Core
metapackage unless you intentionally pin one RID, and keep every OpenUsd package at the same version.
The Core package copies native
libraries and `usd/**` resources to the application output. Program startup registers
`AppContext.BaseDirectory/usd` when that packaged resource tree is present. A source run instead uses
the local loader path and `PXR_PLUGINPATH_NAME`.

## Expected output and file

Successful output has this shape:

```text
Stage: <absolute path to hello-stage.usda>
Default prim: /World
Greeting: Hello from OpenUsd
Answer: 42.5
```

The `.usda` file contains `/World` as the default prim and `/World/Hello` with
`custom:greeting = "Hello from OpenUsd"` and `custom:answer = 42.5`.

## Important code

`Program.cs` demonstrates the complete lifetime:

1. Resolve the argument or deterministic default to an absolute path.
2. Use `UsdStage.Create`, `DefinePrim`, `SetDefaultPrim`, and typed `UsdPrim` setters.
3. Call `UsdStage.Save` before disposing the created stage.
4. Use `UsdStage.Open` and typed getters to verify persisted values.
5. Dispose both stage instances before leaving their `using` scopes.

There is no prim loop and no direct native interop in the authoring path.

## NativeAOT

Publish the unchanged sample. On Windows, run this from an x64 Visual Studio developer shell:

```powershell
dotnet publish samples/OpenUsd.HelloStage/OpenUsd.HelloStage.csproj -c Release -r win-x64 `
    -p:PublishAot=true -o artifacts\samples\hello-stage-aot
& .\artifacts\samples\hello-stage-aot\OpenUsd.HelloStage.exe `
    .\artifacts\samples\hello-stage-aot.usda
```

For a source publish, keep the local native install on `PATH` and set `PXR_PLUGINPATH_NAME` as shown
above. A package-based publish receives the native files and `usd` resources from
`OpenUsd.Runtime.Core`. Publish and run on the target RID; NativeAOT output is platform-specific.

## Troubleshooting

- `DllNotFoundException`: add the matching shim and OpenUSD directories to the native loader path.
- `BadImageFormatException`: use a runtime whose RID and architecture match the executable.
- File-format plugin error: ensure `usd/plugInfo.json` is packaged or set `PXR_PLUGINPATH_NAME`.
- ABI or capability error: align the managed and Core runtime package versions.
- Access or sharing error: choose a writable path that is not open in another process.
- Verification exception: inspect the generated file and confirm no incompatible runtime was loaded.

## Next documentation

- [Samples overview](../README.md)
- [Data API](../../docs/data-api.md)
- [Native build](../../docs/native-build.md)
- [Packaging](../../docs/packaging.md)
- [Live-authoring executable](../OpenUsd.LiveAuthoring.Sample/README.md)
