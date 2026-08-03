# Native build

Use this guide to follow locked OpenUSD inputs through host-specific native installs, verified
archives, and the package and render consumers that reuse them.

**On this page:** [Build and archive flow](#build-and-archive-flow) ·
[Locked inputs](#locked-inputs) · [Cesium-native quarantine](#cesium-native-quarantine) ·
[Local build](#local-build) · [Archive consumers](#verified-archives-and-consumers) ·
[macOS install names](#macos-install-name-policy) · [Related documentation](#related-documentation)

## Build and archive flow

```mermaid
flowchart LR
    lock["Lock file + verified sources"] --> fetch["Fetch and verify"]
    fetch --> build["Build target RID"]
    build --> install["Per-RID native + shim installs"]
    install --> probes["CTest + NativeAOT probes"]
    probes --> archive["Verified archive + sidecar"]
    archive --> packages["Package workflow"]
    archive --> render["Render workflow"]
```

The archive is a transport for verified install trees, not a second build product with different
contents.

## Locked inputs

The native runtime is pinned to OpenUSD v26.05 commit
`2095fafafd033fa23386d7ec6d58c7cc33974518`. `eng/openusd.lock.json` records the OpenUSD archive
hash, the upstream build-script hash, every direct dependency archive hash, and the platform
toolchain matrix.

The fetch step applies verified compatibility patches to the upstream build script: Windows Boost
bootstrap is launched through `cmd.exe` with the `vc143` toolset, and Boost is bound to the `cl.exe`
already configured by the developer shell. This handles Python 3.13 process launching, hardened
Windows command search, and Visual Studio versions newer than Boost's discovery table; the patched
script hash is locked.

The viewer-standard profile retains core USD, validation, Imaging, Hydra, Storm, MaterialX,
OpenImageIO, and OpenColorIO. Python, usdview, examples, tutorials, Embree, Alembic, Draco, OpenVDB,
and RenderMan are excluded.

## Cesium-native quarantine

`eng/cesium.lock.json` is separate from `eng/openusd.lock.json` by design. The Cesium lock pins
cesium-native v0.63.0 by commit and archive SHA-256, and it pins vcpkg manifest resolution to
baseline `56bb2411609227288b70117ead2c47585ba07713`. `eng/fetch-cesium-native.ps1` verifies the
source archive, verifies the upstream `vcpkg.json` and `vcpkg-configuration.json` hashes, checks out
that exact vcpkg commit under `native/.cache`, and bootstraps the tool from the pinned tree. vcpkg
then resolves the manifest from that baseline plus cesium-native's overlay ports/triplets, so the
resolved port versions and port git-tree hashes are reproducible in `vcpkg-manifest-install.log`.

The build is intentionally quarantined and does not feed the Core or Imaging packages.
cesium-native brings a much larger native surface than the lean OpenUSD runtime, including curl,
OpenSSL, Draco, KTX, SQLite, Blend2D, asmjit, s2geometry/abseil, spdlog, meshoptimizer, WebP,
zstd, and other support libraries. Keeping it in `native/install/cesium/<rid>` prevents a user who
only needs USD authoring or rendering from receiving the Cesium dependency set.

Inspect the locked Cesium plan:

```shell
./eng/build-cesium-native.ps1 -Rid win-x64 -PlanOnly
```

Build and smoke-test the Windows install from an x64 developer shell:

```shell
./eng/build-cesium-native.ps1 -Rid win-x64
```

The install contains static libraries in `native/install/cesium/<rid>/lib`, expanded headers in
`native/install/cesium/<rid>/include`, and generated third-party notices. These files are a
build-time SDK for a later C ABI shim, not runtime assets. Packaging is deliberately deferred until
that shim exists because the shipped artifact should be one linked shared library
(`.dll`/`.so`/`.dylib`) that statically contains cesium-native, not hundreds of megabytes of `.lib`
or `.a` files that consumers cannot load directly. Do not add `OpenUsd.Runtime.Cesium.<rid>`
packages for the static libraries.

The smoke probe is `native/cesium_probe`. It links against the installed `cesium-native` CMake
package, parses an in-memory 3D Tiles `tileset.json` with `Cesium3DTilesReader::TilesetReader`, and
fails unless the parsed asset version, top-level geometric error, and recursive tile count match the
expected values. It is deliberately stronger than a load-only check.

`cesium-native.yml` builds `win-x64`, `linux-x64`, and `osx-arm64`, runs the same probe, and records
install/library byte counts for each RID. Local Windows verification measured `win-x64` at
1,012,407,536 install bytes and 984,482,970 static-library bytes. Linux and macOS sizes must come
from CI because they cannot be verified on the Windows workstation.

## Local build

Inspect the exact command without downloading or building:

```shell
./eng/build-native.ps1 -Rid win-x64 -PlanOnly
```

Fetch and verify sources:

```shell
./eng/fetch-native.ps1 -Rid win-x64
```

Build from a Visual Studio developer shell:

```shell
./eng/build-native.ps1 -Rid win-x64
```

If `VULKAN_SDK` is not set, the build wrapper creates the required 1.4.321.0 headers, loader,
shaderc, and VMA headers under `native/install` from the exact source revisions in the lock file. It
does not require a machine-wide LunarG installation.

While those Vulkan sources bootstrap their own dependencies, the log prints
`error: No such remote 'known-good'`, sometimes several times. It comes from the upstream
dependency scripts, not from this repository, and it does not affect the build: the pinned commits
are still checked out and verified against the lock file. It is deliberately not filtered, because
suppressing that stream would also hide genuine `git` failures from the same step.

Run both the native C ABI probe and the NativeAOT managed probe:

```shell
./eng/run-native-probe.ps1 -Rid win-x64
```

## Verified archives and consumers

`native.yml` builds each RID once per locked source identity, verifies CTest and
NativeAOT probes, then calls `eng/create-native-archive.ps1`. The archive contains
only `native/install/<rid>` and `native/install/shim/<rid>` plus a sidecar binding
its SHA-256 to the install metadata, OpenUSD commit, ABI versions, and lock hash.
`eng/test-render-native-archive.ps1` proves the producer and consumer preserve
safe subtrees, hashes, headers, Unix links, and executable modes.

Package and render workflow dispatches can consume an artifact from a successful
native workflow run by passing its numeric run ID as `native-pipeline-run-id`.
The workflow downloads `openusd-native-<rid>`, verifies the sidecar, and routes the
local archive through `eng/prepare-workflow-native-input.ps1`. Existing external
HTTPS archive URL plus SHA-256 inputs remain available after GitHub artifacts expire.

Native artifacts are produced separately from normal managed CI and consumed as
immutable inputs. The `win-x64` gate runs native and NativeAOT probes from a clean
staging directory, registers the copied plugin tree, opens a copied USDA stage,
and reads its root-layer identifier without a global OpenUSD installation.

Archive-mode render staging validates the SHA-256, safe subtrees, symlinks, and
executable modes before installation. On macOS, the workflow then runs the
runtime package gates against that staged install, including `otool` install
name/rpath validation and the signed, hardened package-only NativeAOT consumer.

## macOS install-name policy

The macOS shim configure step sets:

```text
CMAKE_INSTALL_NAME_DIR=@rpath
CMAKE_INSTALL_RPATH=@loader_path
CMAKE_BUILD_WITH_INSTALL_RPATH=OFF
```

Installed package inputs must therefore contain no build or source rpaths.
Packaging accepts exactly one LC_RPATH entry, `@loader_path`.

## Related documentation

- [Packaging](packaging.md) describes how Core and Imaging packages consume each RID install.
- [Testing](testing.md) describes the native, package, render, and release evidence gates.
