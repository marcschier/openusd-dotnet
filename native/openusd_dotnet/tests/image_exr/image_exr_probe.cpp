// Copyright (c) marcschier. Licensed under the MIT License.

#include "image_exr_test_hooks.h"
#include <OpenEXR/ImfChannelList.h>
#include <OpenEXR/ImfFrameBuffer.h>
#include <OpenEXR/ImfHeader.h>
#include <OpenEXR/ImfInputFile.h>
#include <OpenEXR/ImfStringAttribute.h>
#include <algorithm>
#include <array>
#include <cerrno>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <vector>
#if defined(_WIN32)
#define NOMINMAX
#include <Windows.h>
#else
#include <fcntl.h>
#include <sys/stat.h>
#include <unistd.h>
#endif

namespace Exr = OPENEXR_IMF_NAMESPACE;
namespace fs = std::filesystem;

namespace
{
void Require(bool value, const char* message)
{
    if (!value) throw std::runtime_error(message);
}

struct File
{
    fs::path path;
    uint64_t handle = UINT64_MAX;

    explicit File(const fs::path& value) : path(value)
    {
#if defined(_WIN32)
        handle = reinterpret_cast<uintptr_t>(CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr));
#else
        const int descriptor = open(path.c_str(), O_CREAT | O_EXCL | O_RDWR, 0600);
        if (descriptor >= 0) handle = static_cast<uint64_t>(descriptor);
#endif
        Require(handle != UINT64_MAX, "Cannot create probe file.");
    }

    ~File()
    {
#if defined(_WIN32)
        if (handle != UINT64_MAX) CloseHandle(reinterpret_cast<HANDLE>(static_cast<uintptr_t>(handle)));
#else
        if (handle != UINT64_MAX) close(static_cast<int>(handle));
#endif
    }

    File(const File&) = delete;
    File& operator=(const File&) = delete;

    uint64_t Position() const
    {
#if defined(_WIN32)
        LARGE_INTEGER zero{}, value{};
        Require(SetFilePointerEx(reinterpret_cast<HANDLE>(static_cast<uintptr_t>(handle)),
            zero, &value, FILE_CURRENT) != 0, "Owner handle was closed.");
        return static_cast<uint64_t>(value.QuadPart);
#else
        const off_t value = lseek(static_cast<int>(handle), 0, SEEK_CUR);
        Require(value >= 0, "Owner descriptor was closed.");
        return static_cast<uint64_t>(value);
#endif
    }

    void Reset()
    {
#if defined(_WIN32)
        LARGE_INTEGER zero{};
        const HANDLE file = reinterpret_cast<HANDLE>(static_cast<uintptr_t>(handle));
        Require(SetFilePointerEx(file, zero, nullptr, FILE_BEGIN) && SetEndOfFile(file), "Reset failed.");
#else
        Require(ftruncate(static_cast<int>(handle), 0) == 0 &&
            lseek(static_cast<int>(handle), 0, SEEK_SET) == 0, "Reset failed.");
#endif
    }
};

constexpr std::array<uint16_t, 16> Pixels{
    0x5400, 0x8000, 0xb800, 0x3c00, 0x3400, 0x4000, 0x4800, 0x3800,
    0x3000, 0x4c00, 0x2c00, 0x0000, 0x0000, 0x5400, 0x0000, 0x3c00
};

openusd_image_encode_exr_request_v1 Request()
{
    openusd_image_encode_exr_request_v1 request{};
    request.struct_size = sizeof(request);
    request.version = OPENUSD_IMAGE_EXR_VERSION;
    request.pixel_format = OPENUSD_IMAGE_EXR_RGBA16F_LE;
    request.width = request.height = request.display_width = request.display_height = 2;
    request.row_order = OPENUSD_IMAGE_EXR_TOP_DOWN;
    request.color_policy = OPENUSD_IMAGE_EXR_RAW_WORKING_UNSPECIFIED;
    request.alpha_policy = OPENUSD_IMAGE_EXR_ALPHA_STORED_UNSPECIFIED;
    request.window_policy = OPENUSD_IMAGE_EXR_VIEWPORT_ORIGIN_ZERO;
    request.pixel_aspect_numerator = request.pixel_aspect_denominator = 1;
    request.pixel_ceiling = 4;
    request.output_byte_limit = 4096;
    return request;
}

openusd_image_encode_exr_result_v1 Encode(uint64_t handle,
    const openusd_image_encode_exr_request_v1& request,
    const uint16_t* pixels = Pixels.data(), uint64_t bytes = sizeof(Pixels),
    openusd_image_encode_cancel_v1 callback = nullptr, void* context = nullptr)
{
    openusd_image_encode_exr_result_v1 result;
    std::memset(&result, 0xcc, sizeof(result));
    const uint32_t status = openusd_image_encode_exr_rgba16f_v1(
        &request, sizeof(request), reinterpret_cast<const uint8_t*>(pixels), bytes,
        handle, callback, context, &result, sizeof(result));
    Require(status == result.status && result.struct_size == sizeof(result) &&
        result.version == 1 && result.reserved0 == 0 && result.reserved1 == 0, "Invalid result envelope.");
    if (status != OPENUSD_IMAGE_ENCODE_OK)
        Require(result.encoded_bytes == 0 && result.encoded_rows == 0, "Failure exposed success payload.");
    return result;
}

void Verify(const fs::path& path, const openusd_image_encode_exr_request_v1& request)
{
    Exr::InputFile input(path.string().c_str());
    const auto& header = input.header();
    const auto& data = header.dataWindow();
    const auto& display = header.displayWindow();
    Require(data.min.x == request.data_origin_x && data.min.y == request.data_origin_y &&
        data.max.x == request.data_origin_x + 1 && data.max.y == request.data_origin_y + 1 &&
        display.min.x == request.display_origin_x && display.min.y == request.display_origin_y &&
        display.max.x == request.display_origin_x + static_cast<int>(request.display_width) - 1 &&
        display.max.y == request.display_origin_y + static_cast<int>(request.display_height) - 1,
        "EXR windows changed.");
    Require(header.pixelAspectRatio() == static_cast<float>(
        static_cast<double>(request.pixel_aspect_numerator) / request.pixel_aspect_denominator),
        "Pixel aspect changed.");
    Require(header.typedAttribute<Exr::StringAttribute>("openusd:colorPolicy").value() ==
        "raw-working-unspecified", "Color policy changed.");
    const char* alpha[] = { "", "stored-unspecified", "stored-associated", "stored-unassociated" };
    Require(header.typedAttribute<Exr::StringAttribute>("openusd:alphaPolicy").value() ==
        alpha[request.alpha_policy], "Stored alpha policy changed.");
    std::array<uint16_t, 16> decoded{};
    const intptr_t offset = static_cast<intptr_t>(request.data_origin_x) * 8 +
        static_cast<intptr_t>(request.data_origin_y) * 16;
    const uintptr_t base = reinterpret_cast<uintptr_t>(decoded.data()) - static_cast<uintptr_t>(offset);
    Exr::FrameBuffer frame;
    const char* names[] = { "R", "G", "B", "A" };
    for (size_t channel = 0; channel < 4; ++channel)
        frame.insert(names[channel], Exr::Slice(Exr::HALF, reinterpret_cast<char*>(base + channel * 2), 8, 16));
    input.setFrameBuffer(frame);
    input.readPixels(data.min.y, data.max.y);
    for (size_t row = 0; row < 2; ++row)
    {
        const size_t source = request.row_order == OPENUSD_IMAGE_EXR_TOP_DOWN ? row : 1 - row;
        Require(std::equal(decoded.begin() + row * 8, decoded.begin() + (row + 1) * 8,
            Pixels.begin() + source * 8), "Half bits or row order changed.");
    }
}

void Roundtrip(const fs::path& root)
{
    for (uint32_t rows : { OPENUSD_IMAGE_EXR_TOP_DOWN, OPENUSD_IMAGE_EXR_BOTTOM_UP })
    for (uint32_t alpha : { 1u, 2u, 3u })
    for (bool windows : { false, true })
    {
        auto request = Request();
        request.row_order = rows;
        request.alpha_policy = alpha;
        if (windows)
        {
            request.window_policy = OPENUSD_IMAGE_EXR_EXPLICIT_WINDOWS;
            request.data_origin_x = -3;
            request.data_origin_y = 5;
            request.display_origin_x = 1;
            request.display_origin_y = -2;
            request.display_width = 9;
            request.display_height = 7;
            request.pixel_aspect_numerator = 3;
            request.pixel_aspect_denominator = 2;
        }
        File file(root / ("roundtrip-" + std::to_string(rows) + "-" +
            std::to_string(alpha) + "-" + std::to_string(windows) + ".exr"));
        const auto result = Encode(file.handle, request);
        Require(result.status == OPENUSD_IMAGE_ENCODE_OK && result.encoded_rows == 2 &&
            result.encoded_bytes == fs::file_size(file.path) && file.Position() == result.encoded_bytes,
            "Roundtrip failed or owner/cursor/extent changed.");
        Verify(file.path, request);
    }
}

void InvalidInput(const fs::path& root)
{
    File file(root / "invalid-input.exr");
    for (int kind = 0; kind < 10; ++kind)
    {
        auto request = Request();
        auto pixels = Pixels;
        switch (kind)
        {
        case 0: request.reserved2 = 1; break;
        case 1: request.width = 0; break;
        case 2: request.output_byte_limit = UINT64_MAX; break;
        case 3: request.pixel_ceiling = 0; break;
        case 4: pixels[15] = 0x7e01; break;
        case 5: pixels[15] = 0x7c00; break;
        case 6: pixels[15] = 0xfc00; break;
        case 7: request.flags = 1; break;
        case 8: request.version = 2; break;
        case 9: request.struct_size = 111; break;
        }
        Require(Encode(file.handle, request, pixels.data()).status == OPENUSD_IMAGE_ENCODE_INVALID_INPUT,
            "Invalid input was accepted.");
        Require(fs::file_size(file.path) == 0 && file.Position() == 0, "Preflight mutated output.");
    }
    Require(Encode(file.handle, Request(), Pixels.data(), sizeof(Pixels) - 1).status ==
        OPENUSD_IMAGE_ENCODE_INVALID_INPUT, "Truncated input accepted.");
    auto request = Request();
    request.pixel_aspect_numerator = 0;
    Require(Encode(file.handle, request).status == OPENUSD_IMAGE_ENCODE_UNSUPPORTED,
        "Unsupported windows accepted.");
    openusd_image_exr_test_stats_v1 stats{};
    openusd_private_image_exr_test_stats_v1(&stats);
    Require(stats.codec_constructions == 0 && fs::file_size(file.path) == 0, "Refusal constructed codec.");
}

void InvalidHandles(const fs::path& root)
{
    Require(Encode(UINT64_MAX, Request()).status == OPENUSD_IMAGE_ENCODE_INVALID_INPUT,
        "Invalid handle accepted.");
#if defined(_WIN32)
    Require(Encode(0, Request()).status == OPENUSD_IMAGE_ENCODE_INVALID_INPUT, "Null HANDLE accepted.");
#else
    Require(Encode(static_cast<uint64_t>(std::numeric_limits<int>::max()) + 1, Request()).status ==
        OPENUSD_IMAGE_ENCODE_INVALID_INPUT, "Truncated descriptor accepted.");
    File file(root / "invalid-handles.exr");
    for (int flags : { O_RDONLY, O_RDWR | O_APPEND, O_RDWR | O_NONBLOCK })
    {
        const int descriptor = open(file.path.c_str(), flags);
        Require(descriptor >= 0, "Cannot open refused descriptor profile.");
        const auto result = Encode(static_cast<uint64_t>(descriptor), Request());
        const bool still_open = fcntl(descriptor, F_GETFD) >= 0;
        close(descriptor);
        Require(result.status == OPENUSD_IMAGE_ENCODE_UNSUPPORTED && still_open &&
            fs::file_size(file.path) == 0, "Refused profile mutated/closed output.");
    }
    const int directory = open(root.c_str(), O_RDONLY);
    Require(directory >= 0, "Cannot open directory.");
    const auto directory_result = Encode(static_cast<uint64_t>(directory), Request());
    close(directory);
    Require(directory_result.status == OPENUSD_IMAGE_ENCODE_UNSUPPORTED, "Directory accepted.");
    int descriptors[2];
    Require(pipe(descriptors) == 0, "Cannot create pipe.");
    const auto pipe_result = Encode(static_cast<uint64_t>(descriptors[1]), Request());
    close(descriptors[0]);
    close(descriptors[1]);
    Require(pipe_result.status == OPENUSD_IMAGE_ENCODE_UNSUPPORTED, "Pipe accepted.");
    const int descriptor = static_cast<int>(file.handle);
    Require(lseek(descriptor, 1, SEEK_SET) == 1, "Cannot move cursor.");
    Require(Encode(file.handle, Request()).status == OPENUSD_IMAGE_ENCODE_INVALID_INPUT &&
        file.Position() == 1 && fs::file_size(file.path) == 0, "Nonzero cursor accepted/changed.");
    file.Reset();
    Require(write(descriptor, "x", 1) == 1, "Cannot seed nonempty file.");
    Require(Encode(file.handle, Request()).status == OPENUSD_IMAGE_ENCODE_INVALID_INPUT &&
        fs::file_size(file.path) == 1 && file.Position() == 1, "Nonempty output accepted/changed.");
#endif
    (void)root;
}

void Quota(const fs::path& root)
{
    File file(root / "quota.exr");
    auto request = Request();
    const auto success = Encode(file.handle, request);
    Require(success.status == OPENUSD_IMAGE_ENCODE_OK, "Cannot determine exact extent.");
    for (uint64_t quota : { uint64_t{0}, uint64_t{64}, success.encoded_bytes - 1,
        success.encoded_bytes, success.encoded_bytes + 1 })
    {
        file.Reset();
        request.output_byte_limit = quota;
        const auto result = Encode(file.handle, request);
        Require(result.status == (quota < success.encoded_bytes
            ? OPENUSD_IMAGE_ENCODE_QUOTA_EXCEEDED : OPENUSD_IMAGE_ENCODE_OK), "Quota boundary changed.");
        Require(fs::file_size(file.path) <= quota, "Logical extent exceeded quota.");
        if (quota < success.encoded_bytes)
        {
            file.Reset();
            Require(Encode(file.handle, Request()).status == OPENUSD_IMAGE_ENCODE_OK, "Quota reset/retry failed.");
        }
    }
}

struct Cancellation
{
    uint32_t phase;
    uint32_t minimum_rows = 0;
    bool fired = false;
    uint32_t rows = 0;
    uint64_t extent = 0;
};
uint32_t OPENUSD_IMAGE_ENCODE_CALL Cancel(void* context, uint32_t phase, uint32_t rows, uint64_t extent)
{
    auto& cancellation = *static_cast<Cancellation*>(context);
    if (phase != cancellation.phase || rows < cancellation.minimum_rows) return 0;
    cancellation.fired = true;
    cancellation.rows = rows;
    cancellation.extent = extent;
    return 1;
}

void CancellationAndRetry(const fs::path& root)
{
    File file(root / "cancel.exr");
    for (uint32_t phase = 1; phase <= 7; ++phase)
    {
        file.Reset();
        Cancellation cancellation{ phase };
        const auto result = Encode(file.handle, Request(), Pixels.data(), sizeof(Pixels), Cancel, &cancellation);
        Require(cancellation.fired && result.status == OPENUSD_IMAGE_ENCODE_CANCELLED &&
            fs::file_size(file.path) <= 4096, "Cancellation phase failed.");
        file.Reset();
        Require(Encode(file.handle, Request()).status == OPENUSD_IMAGE_ENCODE_OK, "Cancel drain/retry failed.");
        Verify(file.path, Request());
    }
    file.Reset();
    Cancellation after_row{ OPENUSD_IMAGE_ENCODE_PHASE_ENCODE_ROW, 1 };
    const auto result = Encode(file.handle, Request(), Pixels.data(), sizeof(Pixels), Cancel, &after_row);
    Require(result.status == OPENUSD_IMAGE_ENCODE_CANCELLED && after_row.fired &&
        after_row.rows == 1 && after_row.extent > 0 && fs::file_size(file.path) <= 4096,
        "Cancellation after an encoded row lost progress/extent bounds.");
    file.Reset();
    Require(Encode(file.handle, Request()).status == OPENUSD_IMAGE_ENCODE_OK, "Late cancel drain/retry failed.");
    Verify(file.path, Request());
}

void FaultsAndRetry(const fs::path& root)
{
    File file(root / "faults.exr");
    for (uint32_t kind = 1; kind <= 7; ++kind)
    for (bool late : { false, true })
    {
        file.Reset();
        openusd_private_image_exr_test_configure_v1(kind, late ? 1u : 0u, late ? 1u : 0u);
        const auto result = Encode(file.handle, Request());
        const uint32_t expected = kind == EXR_TEST_NATIVE_ONCE ? OPENUSD_IMAGE_ENCODE_NATIVE_ERROR :
            kind == EXR_TEST_ALLOC_ONCE ? OPENUSD_IMAGE_ENCODE_OUT_OF_MEMORY : OPENUSD_IMAGE_ENCODE_IO_ERROR;
        Require(result.status == expected && fs::file_size(file.path) <= 4096, "Fault lost sticky status/quota.");
        openusd_image_exr_test_stats_v1 stats{};
        openusd_private_image_exr_test_stats_v1(&stats);
        Require(stats.fault_triggers == 1, "Codec retried a sticky fault.");
        openusd_private_image_exr_test_configure_v1(EXR_TEST_NONE, 0, 0);
        file.Reset();
        Require(Encode(file.handle, Request()).status == OPENUSD_IMAGE_ENCODE_OK, "Fault drain/retry failed.");
        Verify(file.path, Request());
    }
}

#if !defined(_WIN32)
void PosixDescriptors(const fs::path& root)
{
    File file(root / "descriptor-zero.exr");
    const int saved = dup(STDIN_FILENO);
    Require(dup2(static_cast<int>(file.handle), STDIN_FILENO) == STDIN_FILENO, "Cannot install fd zero.");
    const auto result = Encode(0, Request());
    const bool retained = fcntl(STDIN_FILENO, F_GETFD) >= 0;
    if (saved >= 0) { Require(dup2(saved, STDIN_FILENO) == STDIN_FILENO, "Cannot restore stdin."); close(saved); }
    else close(STDIN_FILENO);
    Require(result.status == OPENUSD_IMAGE_ENCODE_OK && retained, "Valid fd zero rejected/closed.");
    Verify(file.path, Request());

    File target(root / "authorized-target.exr");
    const fs::path link = root / "authorized-link.exr";
    fs::create_symlink(target.path.filename(), link);
    const int descriptor = open(link.c_str(), O_RDWR);
    Require(descriptor >= 0, "Cannot open authorized symlink target.");
    fs::remove(link);
    const fs::path renamed = root / "renamed-target.exr";
    fs::rename(target.path, renamed);
    const auto linked_result = Encode(static_cast<uint64_t>(descriptor), Request());
    const bool linked_retained = fcntl(descriptor, F_GETFD) >= 0;
    close(descriptor);
    Require(linked_result.status == OPENUSD_IMAGE_ENCODE_OK && linked_retained &&
        !fs::exists(link) && !fs::exists(target.path), "Encoder reopened/closed an authorized descriptor.");
    Verify(renamed, Request());
}
#endif
}

int main(int argc, char** argv)
{
    if (argc != 3) return 2;
    const std::string mode = argv[2];
    const fs::path root = fs::path(argv[1]) / ("image-exr-" + mode);
    try
    {
        Require(fs::create_directory(root), "Use a fresh output directory.");
        if (mode == "roundtrip") Roundtrip(root);
        else if (mode == "invalid-input") InvalidInput(root);
        else if (mode == "invalid-handles") InvalidHandles(root);
        else if (mode == "quota") Quota(root);
        else if (mode == "cancel") CancellationAndRetry(root);
        else if (mode == "faults") FaultsAndRetry(root);
#if !defined(_WIN32)
        else if (mode == "posix-descriptors") PosixDescriptors(root);
#endif
        else throw std::runtime_error("Unknown probe mode.");
        Require(Pixels[1] == 0x8000 && Pixels[2] == 0xb800, "Input changed.");
        fs::remove_all(root);
        std::cout << "IMAGE_EXR_" << mode << "=passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << "IMAGE_EXR_" << mode << "=failed: " << error.what() << '\n';
        fs::remove_all(root);
        return 1;
    }
}
