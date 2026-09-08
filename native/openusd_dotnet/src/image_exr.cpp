// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_image_exr.h"
#include <cstddef>

static_assert(sizeof(openusd_image_encode_exr_request_v1) == 112);
static_assert(sizeof(openusd_image_encode_exr_result_v1) == 40);
static_assert(offsetof(openusd_image_encode_exr_request_v1, pixel_ceiling) == 72);
static_assert(offsetof(openusd_image_encode_exr_result_v1, encoded_bytes) == 16);

#if defined(_WIN32) && (defined(_M_X64) || defined(__x86_64__)) && \
    !defined(OPENUSD_IMAGE_EXR_FORCE_UNSUPPORTED)

#define NOMINMAX
#include <Windows.h>
#include <winternl.h>
#if defined(OPENUSD_IMAGE_EXR_TEST_SEAM)
#include "image_exr_test_hooks.h"
#endif
#include <OpenEXR/ImfChannelList.h>
#include <OpenEXR/ImfFrameBuffer.h>
#include <OpenEXR/ImfHeader.h>
#include <OpenEXR/ImfIO.h>
#include <OpenEXR/ImfOutputFile.h>
#include <OpenEXR/ImfStringAttribute.h>
#include <algorithm>
#include <cstring>
#include <limits>
#include <new>
#include <stdexcept>

extern "C" NTSYSAPI NTSTATUS NTAPI NtQueryInformationFile(
    HANDLE, PIO_STATUS_BLOCK, PVOID, ULONG, FILE_INFORMATION_CLASS);

namespace Exr = OPENEXR_IMF_NAMESPACE;
static_assert(sizeof(void*) == 8);

namespace
{
struct Stop final : std::exception
{
    const char* what() const noexcept override { return "image encode stopped"; }
};

#if defined(OPENUSD_IMAGE_EXR_TEST_SEAM)
struct Fault
{
    uint32_t kind = 0;
    uint32_t minimum_rows = 0;
    uint64_t minimum_extent = 0;
    openusd_image_exr_test_stats_v1 stats{};
};
thread_local Fault Test;
#endif

struct Operation
{
    HANDLE file = nullptr;
    uint64_t limit = 0;
    uint64_t position = 0;
    uint64_t extent = 0;
    uint32_t rows = 0;
    uint32_t status = OPENUSD_IMAGE_ENCODE_OK;
    uint32_t error = 0;
    DWORD thread = GetCurrentThreadId();
    openusd_image_encode_cancel_v1 callback = nullptr;
    void* context = nullptr;

    void Remember(uint32_t value, uint32_t native_error = 0) noexcept
    {
        if (status == OPENUSD_IMAGE_ENCODE_OK)
        {
            status = value;
            error = native_error;
        }
    }

    [[noreturn]] void Fail(uint32_t value, uint32_t native_error = 0)
    {
        Remember(value, native_error);
        throw Stop{};
    }

    void Check(uint32_t phase)
    {
        if (status != OPENUSD_IMAGE_ENCODE_OK)
        {
#if defined(OPENUSD_IMAGE_EXR_TEST_SEAM)
            ++Test.stats.sticky_blocks;
#endif
            throw Stop{};
        }
        if (GetCurrentThreadId() != thread) Fail(OPENUSD_IMAGE_ENCODE_NATIVE_ERROR);
        if (callback && callback(context, phase, rows, extent) != 0) Fail(OPENUSD_IMAGE_ENCODE_CANCELLED);
    }

#if defined(OPENUSD_IMAGE_EXR_TEST_SEAM)
    uint32_t FaultAt(bool seek)
    {
        const uint32_t kind = Test.kind;
        const bool seek_kind = kind == EXR_TEST_SEEK_ONCE || kind == EXR_TEST_SEEK_PERSISTENT;
        if (kind == 0 || seek != seek_kind || rows < Test.minimum_rows || extent < Test.minimum_extent)
            return EXR_TEST_NONE;
        ++Test.stats.fault_triggers;
        if (kind != EXR_TEST_WRITE_PERSISTENT && kind != EXR_TEST_SEEK_PERSISTENT)
            Test.kind = EXR_TEST_NONE;
        return kind;
    }
#endif

    void Seek(uint64_t target)
    {
        Check(OPENUSD_IMAGE_ENCODE_PHASE_SEEK);
        if (target > limit) Fail(OPENUSD_IMAGE_ENCODE_QUOTA_EXCEEDED);
#if defined(OPENUSD_IMAGE_EXR_TEST_SEAM)
        ++Test.stats.seeks;
        if (target < position) ++Test.stats.backwards_seeks;
        if (FaultAt(true) != EXR_TEST_NONE) Fail(OPENUSD_IMAGE_ENCODE_IO_ERROR, ERROR_SEEK);
#endif
        LARGE_INTEGER offset{};
        offset.QuadPart = static_cast<LONGLONG>(target);
        if (!SetFilePointerEx(file, offset, nullptr, FILE_BEGIN)) Fail(OPENUSD_IMAGE_ENCODE_IO_ERROR, GetLastError());
        position = target;
    }

    void Write(const char* data, int count)
    {
        Check(OPENUSD_IMAGE_ENCODE_PHASE_WRITE);
        if (count < 0) Fail(OPENUSD_IMAGE_ENCODE_NATIVE_ERROR);
        if (position > limit || static_cast<uint64_t>(count) > limit - position)
            Fail(OPENUSD_IMAGE_ENCODE_QUOTA_EXCEEDED);
        DWORD requested = static_cast<DWORD>(count);
        bool short_write = false;
#if defined(OPENUSD_IMAGE_EXR_TEST_SEAM)
        ++Test.stats.writes;
        const uint32_t fault = FaultAt(false);
        if (fault == EXR_TEST_NATIVE_ONCE)
        {
            Remember(OPENUSD_IMAGE_ENCODE_NATIVE_ERROR);
            throw std::runtime_error("test-only native exception");
        }
        if (fault == EXR_TEST_ALLOC_ONCE)
        {
            Remember(OPENUSD_IMAGE_ENCODE_OUT_OF_MEMORY);
            throw std::bad_alloc{};
        }
        if (fault == EXR_TEST_WRITE_ONCE || fault == EXR_TEST_WRITE_PERSISTENT)
            Fail(OPENUSD_IMAGE_ENCODE_IO_ERROR, ERROR_WRITE_FAULT);
        short_write = fault == EXR_TEST_SHORT_WRITE_ONCE;
        if (short_write) requested /= 2;
#endif
        DWORD written = 0;
        const BOOL success = WriteFile(file, data, requested, &written, nullptr);
        const DWORD native_error = success ? ERROR_SUCCESS : GetLastError();
        if (written > requested) Fail(OPENUSD_IMAGE_ENCODE_NATIVE_ERROR);
        position += written;
        extent = std::max(extent, position);
        if (!success || written != requested || short_write)
            Fail(OPENUSD_IMAGE_ENCODE_IO_ERROR, native_error ? native_error : ERROR_WRITE_FAULT);
    }
};

class BorrowedStream final : public Exr::OStream
{
public:
    explicit BorrowedStream(Operation& operation) : Exr::OStream("caller-owned-handle"), _operation(operation) {}
    void write(const char data[], int count) override { _operation.Write(data, count); }
    uint64_t tellp() override { return _operation.position; }
    void seekp(uint64_t position) override { _operation.Seek(position); }

private:
    Operation& _operation;
};

const char* AlphaName(uint32_t policy) noexcept
{
    switch (policy)
    {
    case OPENUSD_IMAGE_EXR_ALPHA_STORED_UNSPECIFIED: return "stored-unspecified";
    case OPENUSD_IMAGE_EXR_ALPHA_STORED_ASSOCIATED: return "stored-associated";
    case OPENUSD_IMAGE_EXR_ALPHA_STORED_UNASSOCIATED: return "stored-unassociated";
    default: return "";
    }
}

constexpr const char ColorAttribute[] = "openusd:colorPolicy";
constexpr const char AlphaAttribute[] = "openusd:alphaPolicy";
constexpr const char ColorValue[] = "raw-working-unspecified";

uint64_t MinimumFileBytes(const openusd_image_encode_exr_request_v1& request) noexcept
{
    // Fixed scanline header + offsets/chunk headers, excluding compressed data.
    // Every actual seek/write is still separately admitted before native I/O.
    const uint64_t metadata = sizeof(ColorAttribute) + sizeof("string") + 4 + sizeof(ColorValue) - 1 +
        sizeof(AlphaAttribute) + sizeof("string") + 4 + std::strlen(AlphaName(request.alpha_policy));
    return 331 + metadata + static_cast<uint64_t>(request.height) * 16;
}

void ValidateFile(Operation& operation, uint64_t handle)
{
    if (handle == 0 || handle == UINT64_MAX) operation.Fail(OPENUSD_IMAGE_ENCODE_INVALID_INPUT, ERROR_INVALID_HANDLE);
    operation.file = reinterpret_cast<HANDLE>(static_cast<uintptr_t>(handle));
    SetLastError(ERROR_SUCCESS);
    const DWORD type = GetFileType(operation.file);
    if (type == FILE_TYPE_UNKNOWN && GetLastError() != ERROR_SUCCESS)
        operation.Fail(OPENUSD_IMAGE_ENCODE_INVALID_INPUT, ERROR_INVALID_HANDLE);
    if (type != FILE_TYPE_DISK) operation.Fail(OPENUSD_IMAGE_ENCODE_UNSUPPORTED);

    FILE_STANDARD_INFO info{};
    if (!GetFileInformationByHandleEx(operation.file, FileStandardInfo, &info, sizeof(info)))
        operation.Fail(OPENUSD_IMAGE_ENCODE_IO_ERROR, GetLastError());
    if (info.Directory) operation.Fail(OPENUSD_IMAGE_ENCODE_UNSUPPORTED);
    if (info.EndOfFile.QuadPart != 0) operation.Fail(OPENUSD_IMAGE_ENCODE_INVALID_INPUT);

    // Access (8) and mode (16) reject append-only, overlapped and unbuffered
    // handles even for direct C callers; no cross-language volatile flag exists.
    struct AccessInfo { ACCESS_MASK access; } access{};
    struct ModeInfo { ULONG mode; } mode{};
    IO_STATUS_BLOCK io{};
    NTSTATUS status = NtQueryInformationFile(operation.file, &io, &access, sizeof(access),
        static_cast<FILE_INFORMATION_CLASS>(8));
    if (status < 0) operation.Fail(OPENUSD_IMAGE_ENCODE_IO_ERROR, RtlNtStatusToDosError(status));
    status = NtQueryInformationFile(operation.file, &io, &mode, sizeof(mode),
        static_cast<FILE_INFORMATION_CLASS>(16));
    if (status < 0) operation.Fail(OPENUSD_IMAGE_ENCODE_IO_ERROR, RtlNtStatusToDosError(status));
    constexpr ULONG SynchronousModes = 0x10u | 0x20u;
    constexpr ULONG NoIntermediateBuffering = 0x8u;
    if ((access.access & FILE_WRITE_DATA) == 0 ||
        (mode.mode & SynchronousModes) == 0 || (mode.mode & NoIntermediateBuffering) != 0)
        operation.Fail(OPENUSD_IMAGE_ENCODE_UNSUPPORTED);
    LARGE_INTEGER zero{}, position{};
    if (!SetFilePointerEx(operation.file, zero, &position, FILE_CURRENT))
        operation.Fail(OPENUSD_IMAGE_ENCODE_IO_ERROR, GetLastError());
    if (position.QuadPart != 0) operation.Fail(OPENUSD_IMAGE_ENCODE_INVALID_INPUT);
}

void Validate(Operation& operation, const openusd_image_encode_exr_request_v1& request,
    const uint8_t* pixels, uint64_t bytes, uint64_t handle)
{
    if (request.struct_size != sizeof(request) || request.version != OPENUSD_IMAGE_EXR_VERSION)
        operation.Fail(OPENUSD_IMAGE_ENCODE_INVALID_INPUT);
    if (request.flags != 0 || request.reserved0 != 0 || request.reserved1 != 0 || request.reserved2 != 0 ||
        (!operation.callback && operation.context))
        operation.Fail(OPENUSD_IMAGE_ENCODE_INVALID_INPUT);
    if (request.pixel_format != OPENUSD_IMAGE_EXR_RGBA16F_LE ||
        (request.row_order != OPENUSD_IMAGE_EXR_TOP_DOWN && request.row_order != OPENUSD_IMAGE_EXR_BOTTOM_UP))
        operation.Fail(OPENUSD_IMAGE_ENCODE_UNSUPPORTED);
    if (request.width == 0 || request.height == 0 ||
        request.width > OPENUSD_IMAGE_EXR_MAX_DIMENSION || request.height > OPENUSD_IMAGE_EXR_MAX_DIMENSION)
        operation.Fail(OPENUSD_IMAGE_ENCODE_INVALID_INPUT);
    const uint64_t count = static_cast<uint64_t>(request.width) * request.height;
    const uint64_t required = count * 8;
    if (!pixels || reinterpret_cast<uintptr_t>(pixels) % 2 != 0 || bytes != required ||
        bytes > OPENUSD_IMAGE_EXR_MAX_INPUT_BYTES ||
        reinterpret_cast<uintptr_t>(pixels) > std::numeric_limits<uintptr_t>::max() - bytes ||
        request.pixel_ceiling == 0 || request.pixel_ceiling > OPENUSD_IMAGE_EXR_MAX_PIXELS ||
        request.output_byte_limit > static_cast<uint64_t>(INT64_MAX))
        operation.Fail(OPENUSD_IMAGE_ENCODE_INVALID_INPUT);
    if (request.color_policy != OPENUSD_IMAGE_EXR_RAW_WORKING_UNSPECIFIED ||
        request.window_policy != OPENUSD_IMAGE_EXR_VIEWPORT_ORIGIN_ZERO || *AlphaName(request.alpha_policy) == '\0' ||
        request.data_origin_x != 0 || request.data_origin_y != 0 ||
        request.display_origin_x != 0 || request.display_origin_y != 0 ||
        request.display_width != request.width || request.display_height != request.height ||
        request.pixel_aspect_numerator != 1 || request.pixel_aspect_denominator != 1)
        operation.Fail(OPENUSD_IMAGE_ENCODE_UNSUPPORTED);
    if (count > request.pixel_ceiling || request.output_byte_limit < MinimumFileBytes(request))
        operation.Fail(OPENUSD_IMAGE_ENCODE_QUOTA_EXCEEDED);
    ValidateFile(operation, handle);
    operation.Check(OPENUSD_IMAGE_ENCODE_PHASE_PREFLIGHT);
    const uint64_t row_bytes = static_cast<uint64_t>(request.width) * 8;
    for (uint32_t row = 0; row < request.height; ++row)
    {
        operation.Check(OPENUSD_IMAGE_ENCODE_PHASE_VALIDATE_ROW);
        const uint8_t* samples = pixels + row_bytes * row;
        for (uint64_t offset = 0; offset < row_bytes; offset += 2)
        {
            const uint16_t sample = static_cast<uint16_t>(samples[offset]) |
                static_cast<uint16_t>(static_cast<uint16_t>(samples[offset + 1]) << 8);
            if ((sample & 0x7c00u) == 0x7c00u) operation.Fail(OPENUSD_IMAGE_ENCODE_INVALID_INPUT);
        }
    }
}

void Encode(Operation& operation, const openusd_image_encode_exr_request_v1& request, const uint8_t* pixels)
{
    operation.Check(OPENUSD_IMAGE_ENCODE_PHASE_BEFORE_CODEC);
    BorrowedStream stream(operation);
    Exr::Header header(static_cast<int>(request.width), static_cast<int>(request.height));
    header.compression() = Exr::ZIPS_COMPRESSION;
    header.pixelAspectRatio() = 1.0f;
    header.lineOrder() = Exr::INCREASING_Y;
    header.insert(ColorAttribute, Exr::StringAttribute(ColorValue));
    header.insert(AlphaAttribute, Exr::StringAttribute(AlphaName(request.alpha_policy)));
    const char* names[] = { "R", "G", "B", "A" };
    for (const char* name : names) header.channels().insert(name, Exr::Channel(Exr::HALF));
#if defined(OPENUSD_IMAGE_EXR_TEST_SEAM)
    ++Test.stats.codec_constructions;
#endif
    {
        Exr::OutputFile output(stream, header, 0);
        try
        {
            for (uint32_t row = 0; row < request.height; ++row)
            {
                operation.Check(OPENUSD_IMAGE_ENCODE_PHASE_ENCODE_ROW);
                const uint32_t source_row = request.row_order == OPENUSD_IMAGE_EXR_TOP_DOWN ? row : request.height - 1 - row;
                const uint8_t* source = pixels + static_cast<uint64_t>(source_row) * request.width * 8;
                Exr::FrameBuffer frame;
                for (size_t channel = 0; channel < 4; ++channel)
                {
                    // One borrowed row with yStride=0: no image flip or pointer
                    // before the input allocation is needed for bottom-up storage.
                    frame.insert(names[channel], Exr::Slice(Exr::HALF,
                        reinterpret_cast<char*>(const_cast<uint8_t*>(source + channel * 2)), 8, 0));
                }
                output.setFrameBuffer(frame);
                output.writePixels(1);
                ++operation.rows;
            }
        }
        catch (const std::bad_alloc&)
        {
            operation.Remember(OPENUSD_IMAGE_ENCODE_OUT_OF_MEMORY);
            throw;
        }
        catch (...)
        {
            operation.Remember(OPENUSD_IMAGE_ENCODE_NATIVE_ERROR);
            throw;
        }
    }
    // Destructors may absorb I/O exceptions; sticky status remains authoritative.
    operation.Check(OPENUSD_IMAGE_ENCODE_PHASE_FINISH);
    operation.Seek(operation.extent);
}
}

uint32_t OPENUSD_IMAGE_ENCODE_CALL openusd_image_encode_exr_rgba16f_v1(
    const openusd_image_encode_exr_request_v1* request, uint32_t request_bytes,
    const uint8_t* rgba16f_le, uint64_t rgba16f_bytes, uint64_t windows_file_handle,
    openusd_image_encode_cancel_v1 cancellation, void* cancellation_context,
    openusd_image_encode_exr_result_v1* result, uint32_t result_bytes)
{
    if (!result || result_bytes != sizeof(*result)) return OPENUSD_IMAGE_ENCODE_INVALID_INPUT;
    *result = {};
    result->struct_size = sizeof(*result);
    result->version = OPENUSD_IMAGE_EXR_VERSION;
    Operation operation;
    operation.callback = cancellation;
    operation.context = cancellation_context;
#if defined(OPENUSD_IMAGE_EXR_TEST_SEAM)
    Test.stats = {};
#endif
    try
    {
        if (!request || request_bytes != sizeof(*request)) operation.Fail(OPENUSD_IMAGE_ENCODE_INVALID_INPUT);
        operation.limit = request->output_byte_limit;
        Validate(operation, *request, rgba16f_le, rgba16f_bytes, windows_file_handle);
        Encode(operation, *request, rgba16f_le);
    }
    catch (const Stop&) {}
    catch (const std::bad_alloc&) { operation.Remember(OPENUSD_IMAGE_ENCODE_OUT_OF_MEMORY); }
    catch (...) { operation.Remember(OPENUSD_IMAGE_ENCODE_NATIVE_ERROR); }
    result->status = operation.status;
    result->win32_error = operation.error;
    if (operation.status == OPENUSD_IMAGE_ENCODE_OK)
    {
        result->encoded_bytes = operation.extent;
        result->encoded_rows = operation.rows;
    }
    return operation.status;
}

#if defined(OPENUSD_IMAGE_EXR_TEST_SEAM)
void OPENUSD_IMAGE_ENCODE_CALL openusd_private_image_exr_test_configure_v1(
    uint32_t kind, uint32_t minimum_rows, uint64_t minimum_extent)
{
    Test = {};
    Test.kind = kind;
    Test.minimum_rows = minimum_rows;
    Test.minimum_extent = minimum_extent;
}

void OPENUSD_IMAGE_ENCODE_CALL openusd_private_image_exr_test_stats_v1(openusd_image_exr_test_stats_v1* stats)
{
    if (stats) *stats = Test.stats;
}
#endif

#else

uint32_t OPENUSD_IMAGE_ENCODE_CALL openusd_image_encode_exr_rgba16f_v1(
    const openusd_image_encode_exr_request_v1*, uint32_t,
    const uint8_t*, uint64_t, uint64_t,
    openusd_image_encode_cancel_v1, void*,
    openusd_image_encode_exr_result_v1* result, uint32_t result_bytes)
{
    if (result && result_bytes == sizeof(*result))
    {
        *result = {};
        result->struct_size = sizeof(*result);
        result->version = OPENUSD_IMAGE_EXR_VERSION;
        result->status = OPENUSD_IMAGE_ENCODE_UNSUPPORTED;
    }
    return OPENUSD_IMAGE_ENCODE_UNSUPPORTED;
}

#endif
