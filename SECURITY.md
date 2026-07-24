# Security policy

## Reporting a vulnerability

Report suspected vulnerabilities privately through
[GitHub Security Advisories](https://github.com/marcschier/openusd-dotnet/security/advisories/new).
Do not open a public issue, discussion, or pull request before coordinated disclosure.

Include, when available:

- the affected commit, version, operating system, RID, architecture, and GPU/backend;
- the expected and observed behavior, impact, and required attacker capabilities;
- a minimal reproducer or smallest offending USD/asset/archive;
- relevant logs or crash artifacts with credentials, private paths, and proprietary assets removed;
  and
- whether the issue reproduces under NativeAOT, a clean staged runtime, or sanitizers.

Please allow maintainers time to reproduce, assess, and prepare a fix before disclosure. Security
advisories, exploit details, and embargoed fixes must remain in the private advisory.

## Supported versions

OpenUsd is pre-1.0 and does not currently maintain long-term support branches.

| Version | Security support |
| --- | --- |
| Latest commit on the default branch | Supported |
| Older commits, forks, local patches, and unofficial native builds | Not supported |

If maintained release lines are introduced, this table will identify them explicitly. A report for
an older revision is still useful when the issue also affects the supported branch.

## Trust model

### USD and related assets are untrusted input

Treat `.usd`, `.usda`, `.usdc`, `.usdz`, referenced layers, payloads, textures, MaterialX documents,
plugin metadata, and any archive containing them as hostile. Malformed content can exercise native
OpenUSD parsers, asset resolvers, codecs, image libraries, renderer plugins, and GPU drivers.

Applications that open untrusted content should use least privilege, resource and time limits, an
isolated working directory, and operating-system sandboxing where appropriate. Limit recursive
composition, file and archive sizes, object counts, memory, and external resolution. Do not assume a
file extension, successful parse, managed wrapper, or viewer preview makes an asset safe.

### Native ABI, plugins, and runtime discovery

The project-owned C ABI is a compatibility, ownership, and error-translation boundary around the
OpenUSD C++ ABI. Every ABI entry point must validate pointers, lengths, integer overflow, enum and
tag values, versioned structure sizes, thread affinity, ownership, and output initialization.
C++ exceptions and unconsumed native diagnostics must not cross the C boundary.

Plugin discovery and native library lookup can load executable code. Treat `PXR_PLUGINPATH_NAME`,
`PATH`, `LD_LIBRARY_PATH`, `DYLD_LIBRARY_PATH`, application base directories, plugin metadata, and
runtime staging directories as security-sensitive configuration. Use only verified runtime trees
and expected ABI/capability versions. An attacker who can replace a native library, plugin, or
metadata file in a searched directory can generally execute code with the application's privileges.

### External GPU handles

D3D shared handles and keyed mutexes, Vulkan external memory or semaphore handles, IOSurface objects,
Metal shared events, and similar cross-process GPU resources are untrusted capabilities. Validate
the adapter/device identity, handle type, dimensions, format, generation, synchronization protocol,
and import result before use. Define ownership explicitly, close or release each imported resource
exactly once, and reject stale or already-consumed handles.

GPU APIs and drivers may fail, hang, reset a device, or terminate a process when given invalid
resources. Do not expose arbitrary external-handle import as an unauthenticated trust boundary; use
a brokered or sandboxed producer when handles originate outside the application's trust domain.

### Archives and paths

Before extracting or staging USDZ, native runtime, shader, or package archives:

- verify the expected digest and provenance;
- reject absolute paths, drive/device paths, parent traversal, alternate data streams, and entries
  that normalize outside the destination;
- reject symlink, hard-link, or reparse-point escapes;
- apply entry-count, expanded-size, compression-ratio, and nesting limits; and
- revalidate containment and expected file types before loading staged native code or plugins.

Asset resolver output and authored paths require the same normalization and containment care.
Never concatenate an untrusted asset path into a shell command.

## Fuzzing and sanitizers

The repository includes a bounded Linux/Clang native stage/layer fuzzing entry point:

```powershell
./eng/run-native-fuzz.ps1 -MaxTotalTime 60 -TimeoutSeconds 10
```

Run fuzzers in an isolated environment against a sanitizer-enabled native build. Preserve the
smallest reproducing input, sanitizer report, locked source identity, and command line. Report
crashes privately before adding a public corpus seed when the result may be exploitable. Fuzzing,
tests, and sanitizers reduce risk but do not prove that native parsers or GPU paths are safe.

## Dependency and artifact provenance

Use the repository lock files and verification scripts for OpenUSD, direct native dependencies,
Vulkan/SwiftShader inputs, shader toolchains, generated payloads, and runtime archives. Network
downloads must be pinned to reviewed versions and cryptographic digests; generated artifacts must
retain their source, command, and hash provenance. Do not substitute an archive merely because its
file name, package identity, or version string matches.

Report unexpected hash changes, compromised upstream releases, dependency confusion, malicious
plugin metadata, or a workflow that consumes an unverified artifact as security issues. Keep
credentials out of logs, manifests, samples, crash dumps, and generated provenance.

## What is not a security boundary

The following improve compatibility, safety, or diagnosability but are not sandboxes:

- the managed API, `SafeHandle`, nullable annotations, trimming, or NativeAOT;
- the project-owned C ABI by itself;
- renderer-neutral interfaces, backend switching, or process-local resource counters;
- plugin metadata, runtime directory layout, file extensions, package identities, or version
  strings;
- viewer validation, schema checks, hashes used only for identity, tests, fuzzing, or conformance
  evidence; and
- a successful parse, render, or device import.

OpenUsd does not isolate native OpenUSD, third-party codecs, plugins, shader drivers, or GPU drivers
from the hosting process. Applications that require a hostile-content boundary must provide their
own process isolation, sandbox policy, authorization, resource limits, and trusted artifact
distribution.
