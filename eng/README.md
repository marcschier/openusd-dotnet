# Engineering

Repository automation, native dependency locking, CMake presets, ABI generation, package assembly, and quality
gates live here.

- `openusd.lock.json` pins OpenUSD v26.05, direct dependency archives, toolchains, and ABI generations.
- `fetch-native.ps1` downloads and verifies only the archives required by a selected RID.
- `build-vulkan-sdk.ps1` builds the pinned Vulkan headers, loader, shaderc, and VMA headers locally without a
  machine-wide SDK install.
- `build-native.ps1` invokes the verified upstream build script with the viewer-standard profile.

Inspect a build without downloading or compiling:

```shell
./eng/build-native.ps1 -Rid win-x64 -PlanOnly
```
