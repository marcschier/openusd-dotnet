# Contributing

Thank you for improving OpenUsd. Keep changes focused, deterministic, and covered by the smallest
useful test first, then run the applicable full gates before requesting review.

## Prerequisites

Run commands from the repository root with PowerShell 7. The repository pins .NET SDK **10.0.301**
in `global.json` and disables SDK roll-forward. If that SDK is not already installed, bootstrap the
repository-local copy:

```powershell
# Windows
./eng/install-dotnet.ps1

# macOS or Linux
bash ./eng/install-dotnet.sh
```

Confirm that `dotnet --version` reports `10.0.301`. Production libraries must continue to build for
`net8.0`, `net9.0`, and `net10.0`; SDK 10.0.301 is the build SDK, not permission to drop older target
frameworks.

## Make focused changes

- Preserve the project-owned C ABI between managed code and OpenUSD C++.
- Do not add per-element P/Invoke to scene translation, render, or other hot paths. Use bounded bulk
  transfers and explicit ownership.
- Keep renderer-neutral state and Hydra translation separate from backend-specific D3D12, Vulkan,
  Metal, OpenGL, or presentation logic.
- Treat warnings, analyzers, formatting, trimming, NativeAOT diagnostics, and generated-file drift as
  errors.
- Do not include unrelated generated files, local native installs, downloaded toolchains, build
  outputs, credentials, or machine-specific paths.

## Targeted validation

Build the changed project and its closest test project in Release configuration. Managed tests use
TUnit on Microsoft.Testing.Platform and must be launched through the repository runner after they
have been built:

```powershell
dotnet build tests/OpenUsd.Tests/OpenUsd.Tests.csproj -c Release -f net10.0
./eng/run-managed-tests.ps1 `
  -Project tests/OpenUsd.Tests/OpenUsd.Tests.csproj `
  -Framework net10.0 `
  -Configuration Release
```

Substitute the test project and framework that cover the change. Use `-TestArguments` for a focused
tree-node filter when iteration speed matters; [Testing](docs/testing.md) contains examples. Run any
nearby Pester-free `eng/test-*.ps1` contract test when changing its paired script or workflow
contract.

Documentation-only and sample-documentation changes must run:

```powershell
./eng/test-documentation.ps1
```

Native, package, shader, and rendering changes also require the relevant platform gate described in
[Native build](docs/native-build.md), [Packaging](docs/packaging.md),
[Shader pipeline](eng/shaders/README.md), and [Testing](docs/testing.md).

## Full managed gates

Before requesting review for a managed or cross-cutting change, run:

```powershell
dotnet restore OpenUsd.slnx
python eng/generate-interop.py --verify
dotnet build OpenUsd.slnx -c Release --no-restore `
  -p:OpenUsdRequireMetalShaderLibrary=false
dotnet format OpenUsd.slnx --verify-no-changes --no-restore
./eng/check-line-length.ps1
./eng/test-documentation.ps1
./eng/run-managed-tests.ps1 -Configuration Release
```

Some rendering and native execution gates require a particular operating system, GPU API, Xcode
version, or a staged native runtime. Run all gates available on the development host and state
clearly which platform gates remain for hosted validation. A successful compile-only build is not a
substitute for a required native, package-only, shader, or renderer execution gate.

## NativeAOT and trimming

Changes to production libraries, interop, runtime discovery, serialization, reflection, or package
assets must remain trimming- and NativeAOT-clean. At minimum, publish the affected sample or probe
for the current RID:

```powershell
$rid = 'win-x64' # Use linux-x64 or osx-arm64 on those hosts.
dotnet publish samples/OpenUsd.HelloStage/OpenUsd.HelloStage.csproj `
  -c Release -f net10.0 -r $rid -p:PublishAot=true
dotnet publish tests/OpenUsd.NativeProbe/OpenUsd.NativeProbe.csproj `
  -c Release -f net10.0 -r $rid -p:AotProbe=true
```

When a matching native install is available, run `./eng/run-native-probe.ps1 -Rid <rid>`. Rendering
interop changes may additionally require `./eng/run-rhi-probe.ps1` or
`./eng/run-silk-probe.ps1`. Do not suppress trimming or AOT diagnostics merely to make a publish
succeed.

## Public API baselines

Production library APIs are checked by Microsoft.CodeAnalysis.PublicApiAnalyzers. Every intentional
public addition, removal, or signature change must update the affected
`src/<project>/PublicAPI.Unshipped.txt`. Keep `PublicAPI.Shipped.txt` stable except as part of an
explicit release-baseline update. Do not suppress API baseline diagnostics or edit a baseline to
hide an accidental breaking change.

Review public API changes for:

- consistent behavior on .NET 8, 9, and 10;
- nullable annotations, ownership, disposal, and thread-affinity contracts;
- NativeAOT and trimming compatibility;
- documentation and sample coverage; and
- corresponding ABI, package, or compatibility tests when the API crosses those boundaries.

## Native ABI and generated interop

When changing `native/openusd_dotnet/include/openusd_dotnet.h`:

1. Update the native implementation and its negative, ownership, and compatibility tests.
2. Update the ABI version or capability mask in `eng/openusd.lock.json` when the contract changes.
3. Regenerate and verify the checked managed declarations:

   ```powershell
   python eng/generate-interop.py
   python eng/generate-interop.py --verify
   ```

4. Rebuild the applicable RID, run CTest, and run the native plus NativeAOT probes.
5. Update package tests and documentation when ABI versions, runtime assets, discovery, or public
   behavior change.

Changes to locked OpenUSD or native dependency inputs must update their hashes and provenance,
rebuild the affected native artifacts, and pass archive/package validation. Never hand-edit a
generated interop file without updating its source contract.

## Shader regeneration

Shader source, reflection schema, manifest, lock, or generation-script changes must regenerate and
verify the reviewed payload through the repository scripts:

```powershell
./eng/shaders/build-shaders.ps1
./eng/shaders/verify-reproducibility.ps1
./eng/shaders/update-checked.ps1
./eng/shaders/verify-checked.ps1
```

Follow the authority-host rules in `eng/shaders/README.md`: Windows win-x64 owns checked
deterministic Slang outputs, while the macOS Xcode gate owns `mesh.metallib`. Commit only the
reviewed checked payload and provenance files that the documented process permits.

## Documentation and samples

- Update root documentation and the relevant file under `docs/` when public APIs, ABI behavior,
  runtime assets, build steps, or user-visible behavior change.
- Every `samples/*/*.csproj` must have a sibling `README.md` and an entry in
  `samples/README.md`.
- Sample commands must run from the repository root, identify native/runtime prerequisites, and use
  source references unless a public package source is explicitly available.
- Keep local links valid, use LF line endings, balance fenced code and Mermaid blocks, and avoid
  public-feed or release-availability claims unless repository evidence supports them.

## Commits and review

Use small commits with an imperative subject and enough body text to explain non-obvious constraints
or generated changes. Include tests and required generated artifacts in the commit that introduces
the behavior they validate. Before handing work off, inspect `git status` and the final diff for
unrelated files, secrets, absolute paths, and build output.

Do not rewrite shared history, create releases, push branches, push tags, or otherwise update a
remote without explicit authorization from the repository owner or requester.
