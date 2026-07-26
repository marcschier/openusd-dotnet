# Shader pipeline scaffold

This tree owns an offline-capable build-time shader toolchain. It does not add a
runtime compiler dependency.

Pinned inputs:

- Slang 2026.13.1 at `c0952c29d3664b60ada5e5edb3fff9ffd6f98d2d`.
- SPIRV-Tools `vulkan-sdk-1.4.350.1` at
  `0539c81f69a3daeb706fd3477dca61435b475156`.
- Vulkan 1.2 / SPIR-V 1.5, Shader Model 6.0, and Metal 2.4.
- Xcode 16.4 for the macOS-only metallib gate.

All network inputs and SHA-256 hashes are in `toolchain.lock.json`. Downloaded
archives, extracted tools, build trees, and transient shader outputs are
ignored. Reviewed runtime inputs under `checked` are intentionally committed.

From a PowerShell 7 developer shell:

```powershell
./eng/shaders/fetch-toolchain.ps1
./eng/shaders/build-toolchain.ps1
./eng/shaders/build-shaders.ps1
./eng/shaders/verify-reproducibility.ps1
./eng/shaders/update-checked.ps1
./eng/shaders/verify-checked.ps1
```

After the first command has populated the cache, extraction can be repeated
without network access:

```powershell
./eng/shaders/fetch-toolchain.ps1 -Offline -Force
```

`build-shaders.ps1` emits DXIL, SPIR-V, MSL text, normalized reflection JSON,
the exact executed argument plan, and an artifact hash manifest. Profiles,
capabilities, validator environments, Metal language selection, and Xcode
selection are all derived from `toolchain.lock.json`; manifest profiles that do
not match the locked Shader Model are rejected. Linux validation uses
`-ArtifactScope Spirv`, which emits and records only the ten SPIR-V targets and
does not require DXC. macOS validation uses `-ArtifactScope Metal`, which emits
only the MSL text and the compiled `metallib`, because DXIL emission loads the
`dxcompiler` and `dxil` libraries that exist only on Windows. Both restrictions
are enforced by the script and asserted by `validate-workflow-paths.ps1`.

Reflection schema 2 preserves separate D3D register/space and Vulkan
set/binding contracts, resource access and shape, arrays and strides, recursive
struct/matrix layout, and stage I/O semantics. System values have a null
location. Normalized access is mandatory; Slang's omitted raw access for a
read-only texture is normalized to `read`, while other omissions are rejected.
Compute group sizes must be three positive dimensions, and matrix stride follows
the locked row-major or column-major layout, including rectangular matrices.
Missing required resource or user-varying bindings are errors.

`update-checked.ps1` is a Windows win-x64 authority gate. It reconstructs the
toolchain from verified archives, rebuilds every deterministic artifact, strips
debug information and line directives, and replaces `checked` only after the
complete build succeeds. By default it preserves verified download archives;
use `-RefreshDownloads` to test a network-clean fetch, or `-Offline` to prove
that the cached archives are sufficient.

`verify-checked.ps1` never downloads. It rebuilds into `.cache`, captures the
same generated command plan used by the compiler, and requires byte-for-byte
equality with every checked DXIL, SPIR-V, MSL, reflection, and manifest file.
The checked manifest hashes source files, the lock, the shader manifest, and
the scripts that generated and validated it. Every required checked text input
must use LF-only line endings before publication.

The shader workflow runs only when this tree, its documentation, or the
workflow changes. Windows x64 is the byte-authoritative checked-artifact gate.
Linux x64 independently generates, validates, and reproduces only the ten
SPIR-V targets, then structurally checks the full committed payload and validates
committed `checked/*.spv`. macOS arm64 selects the locked Xcode, compiles all ten
checked MSL files to AIR, links exactly one packaging-ready
`mesh.metallib` containing the visible mesh, pick, selection mask, selection
outline, fill, and scale entries, and writes
`eng/shaders/out/checked-payload/osx-arm64/metallib-manifest.json`. Corruption
tests prove that checked-only SPIR-V or MSL changes are rejected. Same-host
reproducibility never implies cross-host byte identity. CI caches verified
downloads only and records compiler, CMake, Ninja, Python, and Xcode versions.

Checked MSL validation binds `[[vertex]]`, `[[fragment]]`, or `[[kernel]]`
directly to the expected function declaration. Metallib validation accepts only
an exact exported function symbol, not a helper or prefix/suffix match. The
metallib manifest records repository-relative commands and paths, symbol-dump
hashes, and hashes of the Xcode selection, payload validation, and checked
packaging scripts. Roots outside the repository are rejected.

The sidecar uses exact schema version 4. It permits only the locked top-level
and library fields, exactly ten duplicate-free source/AIR/entry records, ten
compile commands, one combined link and inspection command, ten symbol checks,
the final library and symbol-dump hashes/sizes, and the complete centralized
provenance set. `scripts/metal_sidecar.py` is the shared generation, staging,
and package validator. It rejects missing or extra records and fields,
duplicate or mismatched mappings, absolute or traversing paths and command
arguments, and hash or size drift. Metal `Pack` invokes this validator before
NuGet package emission with checked-file verification enabled, so corrupt or
stale staged pairs cannot produce a package.

The macOS validator stages the validated combined library at
`eng/shaders/checked/mesh.metallib`, the exact path consumed and packaged by
`OpenUsd.Rendering.Silk.Metal`, plus its validation manifest at
`eng/shaders/checked/mesh.metallib.manifest.json`. Both staged files are ignored
and must never be committed from Windows. macOS builds and packs fail when
either is absent, and the package gate byte-compares both native package assets
with the staged artifacts after the project-level validator succeeds.

`OpenUsd.Rendering.Silk.Metal` defaults
`OpenUsdRequireMetalShaderLibrary=false`, so ordinary cross-platform compile
and analyzer builds remain shader-binary independent. Metal execution,
conformance, and package workflows set the property to `true`, which makes a
missing staged library an error. Every `Pack` additionally requires macOS, the
opt-in property, and the validated combined library; Metal package production
is macOS-only.

Windows win-x64 is authoritative for the committed deterministic artifacts.
Cross-host Slang output is not assumed to be byte-identical. During release
packaging, macOS osx-arm64 with exactly Xcode 16.4 compiles the checked mesh,
pick, selection, and compute MSL files using Metal 2.4 and replaces those
runtime inputs with the single ten-entry `mesh.metallib`. Metallib output is not generated or committed
by the Windows gate.

Checked manifest provenance is an exact set defined in `shader_model.py`.
Validation rejects omitted, unexpected, or duplicate input records before
checking their hashes.

`prepare-metal-library.ps1` is the workflow entry point for macOS consumers. It
selects locked Xcode 16.4, builds and validates the combined library, verifies
the manifest and stage hashes, and leaves the library at the project-consumed
path. Shader CI runs Metal conformance and package verification with the
required property. Package CI prepares the same artifact before its required
macOS execution gate. Regular CI and the macOS OpenGL render proof explicitly
remain compile-only for the Metal RHI.

Run the shader-specific Python test with:

```powershell
python -m unittest discover eng/shaders/tests
```
