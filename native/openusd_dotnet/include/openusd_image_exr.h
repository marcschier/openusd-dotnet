// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_IMAGE_EXR_H
#define OPENUSD_IMAGE_EXR_H

#include "openusd_dotnet.h"

#if defined(_WIN32)
#define OPENUSD_IMAGE_ENCODE_CALL __cdecl
#else
#define OPENUSD_IMAGE_ENCODE_CALL
#endif

/*
 * Guarded Data ABI 24 API. The declaration and integer layouts are portable C.
 * The initial encoder supports only Windows x64 synchronous regular files.
 * Other hosts return UNSUPPORTED without file I/O; capability presence describes
 * the available guarded API, not POSIX/macOS/other handle-profile support.
 *
 * These status values form a DEDICATED encode domain, not openusd_status.
 */
#define OPENUSD_IMAGE_ENCODE_OK 0u
#define OPENUSD_IMAGE_ENCODE_INVALID_INPUT 1u
#define OPENUSD_IMAGE_ENCODE_UNSUPPORTED 2u
#define OPENUSD_IMAGE_ENCODE_QUOTA_EXCEEDED 3u
#define OPENUSD_IMAGE_ENCODE_CANCELLED 4u
#define OPENUSD_IMAGE_ENCODE_IO_ERROR 5u
#define OPENUSD_IMAGE_ENCODE_NATIVE_ERROR 6u
#define OPENUSD_IMAGE_ENCODE_OUT_OF_MEMORY 7u

#define OPENUSD_IMAGE_EXR_VERSION 1u
#define OPENUSD_IMAGE_EXR_MAX_DIMENSION 8192u
#define OPENUSD_IMAGE_EXR_MAX_PIXELS UINT64_C(67108864)
#define OPENUSD_IMAGE_EXR_MAX_INPUT_BYTES UINT64_C(536870912)
#define OPENUSD_IMAGE_EXR_RGBA16F_LE 1u
#define OPENUSD_IMAGE_EXR_TOP_DOWN 1u
#define OPENUSD_IMAGE_EXR_BOTTOM_UP 2u
#define OPENUSD_IMAGE_EXR_RAW_WORKING_UNSPECIFIED 1u
#define OPENUSD_IMAGE_EXR_ALPHA_STORED_UNSPECIFIED 1u
#define OPENUSD_IMAGE_EXR_ALPHA_STORED_ASSOCIATED 2u
#define OPENUSD_IMAGE_EXR_ALPHA_STORED_UNASSOCIATED 3u
#define OPENUSD_IMAGE_EXR_VIEWPORT_ORIGIN_ZERO 1u

#define OPENUSD_IMAGE_ENCODE_PHASE_PREFLIGHT 1u
#define OPENUSD_IMAGE_ENCODE_PHASE_VALIDATE_ROW 2u
#define OPENUSD_IMAGE_ENCODE_PHASE_BEFORE_CODEC 3u
#define OPENUSD_IMAGE_ENCODE_PHASE_ENCODE_ROW 4u
#define OPENUSD_IMAGE_ENCODE_PHASE_WRITE 5u
#define OPENUSD_IMAGE_ENCODE_PHASE_SEEK 6u
#define OPENUSD_IMAGE_ENCODE_PHASE_FINISH 7u

#pragma pack(push, 8)
typedef struct openusd_image_encode_exr_request_v1
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t flags;
    uint32_t pixel_format;
    uint32_t width;
    uint32_t height;
    uint32_t row_order;
    uint32_t color_policy;
    uint32_t alpha_policy;
    uint32_t window_policy;
    int32_t data_origin_x;
    int32_t data_origin_y;
    int32_t display_origin_x;
    int32_t display_origin_y;
    uint32_t display_width;
    uint32_t display_height;
    uint32_t pixel_aspect_numerator;
    uint32_t pixel_aspect_denominator;
    uint64_t pixel_ceiling;
    uint64_t output_byte_limit;
    uint64_t reserved0;
    uint64_t reserved1;
    uint64_t reserved2;
} openusd_image_encode_exr_request_v1;

typedef struct openusd_image_encode_exr_result_v1
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t status;
    uint32_t win32_error;
    uint64_t encoded_bytes;
    uint32_t encoded_rows;
    uint32_t reserved0;
    uint64_t reserved1;
} openusd_image_encode_exr_result_v1;
#pragma pack(pop)

/*
 * Zero continues; nonzero cancels. Same encoding thread, synchronous lifetime,
 * sampled per scanline/IO boundary, never per pixel. Must not unwind, reenter,
 * mutate input, close the raw handle or perform I/O on the borrowed file.
 * Context and input remain owned/alive until return. Filesystem/observer hangs
 * are not interruptible. completed_rows counts encoded rows, not validation rows.
 */
typedef uint32_t (OPENUSD_IMAGE_ENCODE_CALL *openusd_image_encode_cancel_v1)(
    void* context, uint32_t phase, uint32_t completed_rows, uint64_t output_extent);

#ifdef __cplusplus
extern "C" {
#endif

/*
 * One synchronous bulk encode. Exact request/result sizes: 112/40, version 1.
 * Flags/reserved values must be zero. Input must be two-byte aligned, immutable,
 * readable packed RGBA16Float LE of exactly width*height*8 bytes. Every sample
 * must be finite. Request/input/result regions must be valid and disjoint.
 * Invalid arbitrary pointers are outside the C contract.
 *
 * Explicit metadata: raw renderer-working/unspecified primaries, selected stored
 * alpha convention (never converted), origin-zero equal full data/display windows,
 * and square pixels 1/1. Unsupported metadata is rejected before mutation/codec
 * construction. Both row orders are mapped without a whole-image copy.
 *
 * Windows x64 profile: zero-extended borrowed HANDLE, neither 0 nor UINT64_MAX,
 * regular writable FILE_WRITE_DATA, synchronous buffered random access, empty,
 * positioned at zero, with exclusive caller I/O ownership. No path is reopened.
 * No close/delete/truncate, durable flush or atomic publication is performed.
 * Caller stages/publishes or resets/discards bounded partial output on failure.
 * A successful call leaves the native cursor at EOF. Byte bounds are logical
 * file extent including rewrites, NOT native codec/kernel heap or disk allocation.
 *
 * Failure is sticky even for transient errors. A valid result buffer is cleared
 * and initialized; success payload (bytes/rows) is zero on every failure. Invalid
 * result pointer/size is not dereferenced. Missing callback requires null context.
 * On an unsupported host, only an optional correctly-sized result is initialized;
 * no request/input/handle is accessed and no file I/O or codec construction occurs.
 */
OPENUSD_DOTNET_API uint32_t OPENUSD_IMAGE_ENCODE_CALL openusd_image_encode_exr_rgba16f_v1(
    const openusd_image_encode_exr_request_v1* request,
    uint32_t request_bytes,
    const uint8_t* rgba16f_le,
    uint64_t rgba16f_bytes,
    uint64_t windows_file_handle,
    openusd_image_encode_cancel_v1 cancellation,
    void* cancellation_context,
    openusd_image_encode_exr_result_v1* result,
    uint32_t result_bytes);

#ifdef __cplusplus
}
#endif
#endif
