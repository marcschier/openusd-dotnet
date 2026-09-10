# Bounded raw RGBA16Float EXR output

> Unreleased source API requiring the matching Data ABI 24 Core runtime.
> Published `0.14.0-alpha` native assets cannot serve this API.

`OpenUsd.Rendering.ExrRgba16FloatWriter` writes one exact, packed RGBA binary16
little-endian plane to a caller-owned `FileStream`. The standard dependency path is
`OpenUsd.Rendering` → `OpenUsd` → `OpenUsd.Interop`, with the matched
`OpenUsd.Runtime.Core.win-x64` native assets. No renderer backend or Hydra translation
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

* Data ABI **24**, required capability **`OPENUSD_CAPABILITY_IMAGE_EXR_OUTPUT`**,
  bit **32**, full current required mask **`0x1FFFFFFFF`**.
* The capability describes a **guarded API**, not portable file-descriptor support.
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

The initial API is viewport/raster output: explicit origin `(0,0)`, complete equal
data/display windows, and square pixels. Crops, overscan, arbitrary authored render
windows/aspect/primaries and float32 input are unsupported, not reinterpreted.

## Platform matrix

| Host/profile | Guarded API | Encode behavior | Actual evidence |
|---|---|---|---|
| Windows x64 regular-file profile | Available | Supported | Native, package-only NativeAOT, Hio and SwiftShader |
| Other Windows handle profiles | Available | Refused before output mutation | Native and managed probes |
| Other hosts/architectures | Portable declaration | `UNSUPPORTED` without file I/O | No non-Windows execution claimed |

The implemented profile requires a regular, synchronous, buffered, writable empty HANDLE.
Asynchronous, append-only, read-only, unbuffered, directory, pipe and device handles are refused.
Other hosts, including Linux/macOS/ARM64/32-bit, do not access request/input/handle storage;
the managed writer throws early `PlatformNotSupportedException`. The unavailable-profile
source branch was compiled and executed on Windows only, not on those other platforms.

Do not reuse the Windows zero/invalid-HANDLE rule for POSIX descriptors: fd zero can
be valid. No POSIX descriptor implementation was added. Native configuration only
resolves/links the OpenEXR output/NTDLL dependencies when the compiler targets the
implemented Windows x64 profile; other platforms are not made to fail merely because
this profile is unavailable.

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
`IOException`. Native I/O retains the numeric Win32 cause in a `Win32Exception`
inner exception. No diagnostic string is parsed. Native allocation failure is not
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

The existing OpenEXR 3.1.11 codec is used; no codec source is copied and no new codec
is installed. `OpenEXR-3_1`, Iex, IlmThread, Imath and zlib are part of the declared
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
