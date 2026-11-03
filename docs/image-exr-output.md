# Bounded raw RGBA16Float EXR output

> Unreleased source API requiring the matching Data ABI 26 Core runtime.
> Published `0.14.0-alpha` native assets cannot serve this API.

`OpenUsd.Rendering.ExrRgba16FloatWriter` writes one exact, packed RGBA binary16
little-endian plane to a caller-owned `FileStream`. The standard dependency path is
`OpenUsd.Rendering` → `OpenUsd` → `OpenUsd.Interop`, with the matched
`OpenUsd.Runtime.Core` assets for the current host. No renderer backend or Hydra translation
logic is part of the encoder.

```csharp
using var staging = new FileStream(
    callerSelectedStagingPath, FileMode.CreateNew, FileAccess.Write,
    FileShare.None, bufferSize: 1, FileOptions.None);

long bytes = ExrRgba16FloatWriter.Write(
    staging, width, height, detachedHdrHalfBytes.Span,
    Rgba16FloatRowOrder.TopDown, maximumEncodedBytes, cancellationToken);
```

The caller must select/authorize the staging location; a scene-authored product name
is not write authority. Flush/durable storage, closing, atomic publication and the
final publication cancellation check belong to the caller. Do not publish on failure.
A failure may leave bounded incomplete or complete-but-unaccepted output. Before
reuse, truncate and reset the position to zero, or discard/use a new staging file.
Success synchronizes both the native cursor and `FileStream.Position` to EOF.

## Exact contract

* Introduced in Data ABI **24**, required capability **`OPENUSD_CAPABILITY_IMAGE_EXR_OUTPUT`**,
  bit **32**. The current runtime is Data ABI **25** with full required mask **`0x3FFFFFFFF`**.
* The capability describes a **guarded API**, not support for arbitrary handles or descriptors.
  Old ABI23, a missing capability, and a missing export must be rejected. It is not
  valid to advertise this required export under ABI23.
* `openusd_image_encode_exr_rgba16f_v1` uses a dedicated numeric encode-status domain,
  not the pre-existing `openusd_status` values. Request/result version 1 sizes are
  exactly **112/40** bytes; all fields are fixed-width integers, with pointer/function
  arguments supplied separately using the C calling convention.
* The installed portable C declarations are in `openusd_image_exr.h`, also included
  by `openusd_dotnet.h`. The exact managed mirror is
  `OpenUsd.Interop/OpenUsdNativeExrEncoding.g.cs`.
* Exact input size is `width * height * 8`, with two-byte alignment and finite
  samples. Dimensions are `1..8192`, hard pixels `67,108,864`; maximum borrowed input
  is 512 MiB. NaN/infinity are rejected with a no-copy bit scan before output
  mutation or codec construction. Negative/HDR values, subnormals, signed zero and
  stored alpha are preserved by the lossless writer.
* `Rgba16FloatRowOrder` is a new, explicitly named half-plane contract. Neither
  `Rgba8RowOrder` nor any existing API's semantics/defaults are changed. One borrowed
  scanline with zero vertical slice stride handles either row order without a whole
  image flip, widening copy or per-pixel interop.

## Metadata and alpha

Default output is raw renderer-working data with **unspecified primaries** and
**stored-unspecified alpha**. This matches detached public `SilkHdrColor` storage;
the writer does not infer a transfer function, Rec.709/ACES primaries, physical
radiance, or straight alpha from PNG/display output.

The explicit `ExrStoredAlphaPolicy` overload can record caller-declared associated
or unassociated storage, but performs **no premultiply/unpremultiply or other sample
conversion**. Metadata is recorded in project string attributes
`openusd:colorPolicy` and `openusd:alphaPolicy`; third-party compositors need not
honor those custom attributes. No chromaticities attribute is silently added.

The public writer API is viewport/raster output: explicit origin `(0,0)`, complete equal
data/display windows, and square pixels. Authored render-product jobs need more than
that, because a product's crop, overscan and pixel aspect are part of its raster. They
use an internal overload with the `EXPLICIT_WINDOWS` policy of the same encoder:

- The display window is the product's full raster, with origin `(0,0)`.
- The data window is the product's crop or overscan region. Its bottom-left product
  origin is converted to EXR's downward Y axis, so overscan produces negative or
  beyond-display coordinates rather than a clamped image.
- The `pixelAspectRatio` attribute stores the product's effective pixel aspect as a float.

A pixel aspect outside 1/65536..65536, or a window corner beyond ±1,073,741,823, is
refused before the job stages output. Neither is clamped. Arbitrary authored primaries
and float32 input remain unsupported, not reinterpreted.

## Platform matrix

| Host/profile | Guarded API | Encode behavior | Actual evidence |
|---|---|---|---|
| Windows x64 regular-file profile | Available | Supported | Native, package-only NativeAOT, Hio and SwiftShader |
| Other Windows handle profiles | Available | Refused before output mutation | Native and managed probes |
| Linux/macOS little-endian x64/ARM64 regular-file profile | Available | Implemented | Hosted native and package-only execution required; not yet qualified by this change |
| Other hosts/architectures | Portable declaration | `UNSUPPORTED` without file I/O | No execution asserted |

The Windows profile requires a regular, synchronous, buffered, writable empty HANDLE.
Asynchronous, append-only, read-only, unbuffered, directory, pipe and device handles are refused.
The POSIX profile accepts a borrowed nonnegative `int` descriptor, including fd zero.
It requires an empty writable regular file positioned at zero and refuses `O_APPEND`,
`O_NONBLOCK` and `O_DIRECT` where available. Interrupted seek/write syscalls retry;
a short write records a sticky I/O failure. The encoder does not reopen paths, so
an authorized descriptor remains usable after its symlink is removed or its target
is renamed. The caller still owns exclusive I/O and closes the descriptor after drain.

The existing `windows_file_handle` parameter and `win32_error` result field retain
their names and widths for ABI compatibility. POSIX callers pass a zero-extended
descriptor and receive `errno` in the historical error field. Windows retains its
HANDLE representation and Win32 errors. Unsupported hosts do not access request,
input or handle storage; the managed writer rejects them before native loading.

Native configuration resolves OpenEXR for both implemented profiles and links NTDLL
only on Windows. The source-owned standalone codec probe also builds without the
OpenUSD SDK; it exercises the same encoder source, not a second codec implementation.

## Ownership, cancellation and errors

The native side borrows the handle and never closes, deletes, truncates or reopens
a path. It validates regular-file type, write access, synchronous/buffered mode,
empty length and zero position. Exclusive caller I/O ownership is a precondition,
not a lock the encoder acquires against arbitrary concurrent users of the handle.

The managed layer strongly retains the `SafeFileHandle`, uses `DangerousAddRef`
through the synchronous call and keeps the input span pinned. A C callback/context
is sampled per scanline/IO boundary, on the calling thread only. Its `GCHandle`
lives until native cleanup drains; managed exceptions cannot unwind through C++.
Owner-requested disposal is delayed until the borrowed native operation is safe.
No shared cross-language volatile flag is used. A hung filesystem/observer is not
interruptible by this cooperative mechanism.

Dedicated statuses map to typed managed outcomes: invalid input → argument error,
unsupported profile → not-supported error, quota → `RenderOutputQuotaExceededException`,
cancellation → `OperationCanceledException`, native I/O/codec/allocation failure →
`IOException`. Native I/O retains the numeric OS cause in a `Win32Exception`
inner exception (`NativeErrorCode` is `errno` on POSIX). No diagnostic string is parsed. Native allocation failure is not
misrepresented as a CLR allocation failure.

## What is and is not bounded

The encoded-byte ceiling bounds **logical file extent**, including backward header
and offset-table rewrites. All seek/write admission occurs before OS mutation;
the first failure remains sticky through destructor cleanup, even if the failure
was transient. Failure clears all success bytes/rows.

The bridge borrows the input raster. ZIPS has one scanline per chunk; the maximum
uncompressed scanline and the on-disk offset table are each 64 KiB. These format
extents are **not a total codec allocation formula**. OpenEXR/zlib scratch, native
object overhead, kernel cache and physical disk allocation are not charged to the
encoded-byte ceiling. Zero warmed managed allocation does not mean zero native
heap. The shared disk-job/MCP path retains its conservative 20-byte-per-pixel capture admission
and separately bounds the combined encoded PNG/depth/HDR files. It does not certify codec heap.

## Shared jobs and MCP

Use the `RenderDiskJobRequest` overload with `includeHdrColor: true` and
`hdrColorFormat: RenderHdrColorFormat.Exr` for a generated `.hdr.exr` plane alongside each
display PNG. Existing overloads retain raw `.hdr.rgba16f` output. Actual encoded lengths,
format metadata and hashes participate in the existing all-or-nothing directory publication.
In MCP, the corresponding request is `includeHdrColor: true, hdrColorFormat: "exr"`;
selected-frame reads atomically expose all uncached planes, including the `image/x-exr` resource.
See [rendering](rendering.md#bounded-disk-image-jobs) and [MCP](mcp.md#render_sequence-and-read_sequence_frame).

## Dependency/provenance scope

The locked OpenEXR 3.5.2 codec is used; no codec source is copied and no new codec
is installed. OpenEXR, Iex, IlmThread, Imath and compression dependencies are part of the declared
Core package closure. NTDLL is a Windows **system** dependency and is not
redistributed. The EXR addition retained the storage-admission SDK and left
Storm 9, child 8 and hdSilk session/page 5/23 unchanged. Authored-product execution
separately advances the current hdSilk session interface to 6; page ABI remains 23.
Native-child AOV capture subsequently advances the child interface to 9 without changing this encoder.
The only new data export is ordinal 399; all 398 previous names/ordinals are retained.

Ordinary Windows/MSVC CMake builds consume the source-owned
`native/openusd_dotnet/private/openusd_dotnet.def`; no private configure parameter
is needed. Keep existing ordinals unchanged and append new public exports.

Regenerate with `python eng/generate-capabilities.py` and
`python eng/generate-interop.py`, then run both commands with `--verify`.
The latter also generates the dedicated EXR mirror directly from its public
header and validates its callback/signature/layout contract. The public EXR
capability constant is outside the capability generator's replacement region.

Native install metadata checks the source-matching `openusd_image_exr.h` hash and
version 1 alongside the Data ABI, capabilities and binary hashes. A stale or changed
EXR header, missing capability or old runtime is rejected before bundle publication.

Public API additions remain Unshipped. Set `OPENUSD_EXR_EXECUTION_REQUIRED=1` and run
`RuntimePackageTests.CorePackagesExecuteBoundedExrFromCleanNativeAotFeed` with the
matching package execution install to require an actual Core-only NativeAOT proof.
It independently decodes the scanline output and compares all stored half bits,
checks both row orders and raw metadata, and exercises exact byte ceilings,
cancellation, handle ownership, same-stream reset/retry and shared EXR job publication.
The proof does not
certify native codec heap limits or support on unexecuted platforms.

## Required hosted execution

After rebuilding the source-matching shim and verifying SDK commit/patch provenance,
run `ctest --test-dir native/build/shim/<rid> -C Release --no-tests=error
--output-on-failure -R "^openusd_image_exr_"` on `linux-x64` and `osx-arm64`.
The seven probes cover exact half bits and windows, invalid requests/descriptors,
before/at/after byte ceilings, every cancellation boundary, sticky injected faults,
reset/retry, fd zero and authorized symlink/rename lifetimes.

Set `OPENUSD_EXR_EXECUTION_REQUIRED=1`, configure the matching package execution
inputs, rebuild `OpenUsd.Package.Tests` and run `eng/run-managed-tests.ps1` with
`-Project tests/OpenUsd.Package.Tests/OpenUsd.Package.Tests.csproj -Framework net10.0
-TestArguments @('--treenode-filter',
'/*/*/RuntimePackageTests/CorePackagesExecuteBoundedExrFromCleanNativeAotFeed')`.
The test packs the current RID, publishes a Core-only NativeAOT consumer and checks
loaded-module paths and hashes before accepting the independent decoder result.
Run `RenderDiskJobExrTests` and `RenderProductJobPlanTests` with the same required
switch and a source-matching native runtime to qualify shared output publication.

Local source builds and managed admission tests do not qualify POSIX execution.
When the required host-memory admission refuses a native child, retain its refusal
receipt and run these selectors on a hosted runner; do not lower the guard.
