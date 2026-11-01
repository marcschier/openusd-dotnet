// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_hdsilk.h"

#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <iterator>
#include <stdexcept>
#include <string>

namespace
{

static_assert(sizeof(openusd_silk_ptex_face_layout) == 64);
static_assert(sizeof(openusd_silk_ptex_decode_info) == 40);

void Require(bool condition, const char* message)
{
    if (!condition)
    {
        throw std::runtime_error(message);
    }
}

openusd_silk_ptex_decode_info MakeInfo()
{
    return {
        sizeof(openusd_silk_ptex_decode_info),
        OPENUSD_SILK_PTEX_DECODE_VERSION,
        0,
        0,
        0,
        0,
        0,
        0,
        0};
}

openusd_status Decode(
    const char* path,
    const uint32_t* faceIds,
    uint32_t faceCount,
    uint32_t maximumDimension,
    uint64_t maximumBytes,
    openusd_silk_ptex_face_layout* faces,
    uint32_t faceCapacity,
    uint8_t* pixels,
    uint64_t pixelCapacity,
    openusd_silk_ptex_decode_info* info,
    openusd_error_buffer* error)
{
    return openusd_silk_ptex_decode_faces(
        path,
        faceIds,
        faceCount,
        maximumDimension,
        maximumBytes,
        faces,
        faceCapacity,
        pixels,
        pixelCapacity,
        info,
        error);
}

void QueryAndCapacityFailuresPreserveDataBuffers(const std::string& path)
{
    constexpr std::array<uint32_t, 4> faceIds = {0, 1, 2, 3};
    openusd_silk_ptex_decode_info info = MakeInfo();
    std::array<char, 256> errorText{};
    openusd_error_buffer error{errorText.data(), errorText.size(), 0};

    Require(
        Decode(path.c_str(), faceIds.data(), static_cast<uint32_t>(faceIds.size()), 8, 192,
            nullptr, 0, nullptr, 0, &info, &error) == OPENUSD_STATUS_BUFFER_TOO_SMALL,
        "sizing query did not report insufficient output buffers");
    Require(info.requested_face_count == faceIds.size(), "sizing query returned the wrong face count");
    Require(info.required_byte_count == 192, "sizing query returned the wrong pixel extent");
    Require(info.file_face_count == 4 && info.channel_count == 3, "sizing query omitted texture metadata");

    std::array<openusd_silk_ptex_face_layout, 4> faces;
    std::memset(faces.data(), 0xA5, sizeof(faces));
    std::array<uint8_t, 192> pixels;
    pixels.fill(0xA5);
    const auto expectedFaces = faces;
    const auto expectedPixels = pixels;

    info = MakeInfo();
    Require(
        Decode(path.c_str(), faceIds.data(), static_cast<uint32_t>(faceIds.size()), 8, 192,
            faces.data(), 3, pixels.data(), pixels.size(), &info, &error) ==
            OPENUSD_STATUS_BUFFER_TOO_SMALL,
        "short face-layout buffer was not refused");
    Require(
        std::memcmp(faces.data(), expectedFaces.data(), sizeof(faces)) == 0,
        "short face-layout buffer was partially written");
    Require(pixels == expectedPixels, "face-layout refusal modified pixel data");
    Require(info.requested_face_count == faceIds.size() && info.required_byte_count == pixels.size(),
        "short face-layout response omitted required capacities");

    info = MakeInfo();
    Require(
        Decode(path.c_str(), faceIds.data(), static_cast<uint32_t>(faceIds.size()), 8, 192,
            faces.data(), static_cast<uint32_t>(faces.size()), pixels.data(),
            pixels.size() - 1, &info, &error) ==
            OPENUSD_STATUS_BUFFER_TOO_SMALL,
        "short pixel buffer was not refused");
    Require(
        std::memcmp(faces.data(), expectedFaces.data(), sizeof(faces)) == 0,
        "short pixel buffer caused a partial face-layout write");
    Require(pixels == expectedPixels, "short pixel buffer was partially written");
}

void SuccessfulDecodePreservesLayoutAndPixels(const std::string& path)
{
    constexpr std::array<uint32_t, 4> faceIds = {0, 1, 2, 3};
    constexpr std::array<std::array<uint8_t, 3>, 4> colors = {{
        {{255, 255, 255}},
        {{255, 0, 0}},
        {{0, 255, 0}},
        {{0, 0, 255}},
    }};
    std::array<openusd_silk_ptex_face_layout, 4> faces{};
    std::array<uint8_t, 192> pixels{};
    openusd_silk_ptex_decode_info info = MakeInfo();
    Require(
        Decode(path.c_str(), faceIds.data(), static_cast<uint32_t>(faceIds.size()), 8,
            pixels.size(), faces.data(), static_cast<uint32_t>(faces.size()),
            pixels.data(), pixels.size(), &info, nullptr) ==
            OPENUSD_STATUS_OK,
        "valid bulk Ptex decode failed");

    Require(info.struct_size == sizeof(info) && info.version == OPENUSD_SILK_PTEX_DECODE_VERSION,
        "decode returned the wrong info packet version");
    Require(info.mesh_type == 1 && info.data_type == 0, "decode changed Ptex mesh or data type");
    Require(info.channel_count == 3 && info.alpha_channel == -1, "decode changed Ptex channel metadata");
    Require(info.file_face_count == 4 && info.requested_face_count == faceIds.size(),
        "decode returned the wrong face counts");
    Require(info.required_byte_count == pixels.size(), "decode returned the wrong byte extent");

    for (size_t index = 0; index < faces.size(); ++index)
    {
        const openusd_silk_ptex_face_layout& face = faces[index];
        Require(face.struct_size == sizeof(face), "face layout has the wrong struct size");
        Require(face.face_id == faceIds[index], "face order changed during bulk decode");
        Require(face.width == 4 && face.height == 4 && face.row_stride_bytes == 12,
            "face layout has incorrect dimensions or stride");
        Require(face.data_offset_bytes == index * 48, "face output offset is incorrect");
        Require(std::all_of(
            std::begin(face.adjacent_face_ids),
            std::end(face.adjacent_face_ids),
            [](int32_t adjacentFace) { return adjacentFace == -1; }),
            "boundary adjacency was not preserved");
        for (size_t pixel = 0; pixel < 16; ++pixel)
        {
            for (size_t channel = 0; channel < colors[index].size(); ++channel)
            {
                Require(
                    pixels[static_cast<size_t>(face.data_offset_bytes) +
                        pixel * colors[index].size() + channel] == colors[index][channel],
                    "bulk-decoded pixel does not match the fixture");
            }
        }
    }
}

void DuplicateAndEmptyRequestsAreSupported(const std::string& path)
{
    constexpr std::array<uint32_t, 2> duplicateIds = {2, 2};
    std::array<openusd_silk_ptex_face_layout, 2> duplicateFaces{};
    std::array<uint8_t, 96> duplicatePixels{};
    openusd_silk_ptex_decode_info info = MakeInfo();
    Require(
        Decode(path.c_str(), duplicateIds.data(),
            static_cast<uint32_t>(duplicateIds.size()), 8, duplicatePixels.size(),
            duplicateFaces.data(), static_cast<uint32_t>(duplicateFaces.size()), duplicatePixels.data(),
            duplicatePixels.size(), &info, nullptr) == OPENUSD_STATUS_OK,
        "duplicate face IDs were not decoded");
    Require(duplicateFaces[0].data_offset_bytes == 0 &&
        duplicateFaces[1].data_offset_bytes == 48,
        "duplicate faces did not receive independent output spans");
    Require(
        std::equal(duplicatePixels.begin(), duplicatePixels.begin() + 48,
            duplicatePixels.begin() + 48),
        "duplicate face payloads differ");

    info = MakeInfo();
    Require(
        Decode(path.c_str(), nullptr, 0, 8, 0, nullptr, 0, nullptr, 0, &info, nullptr) ==
            OPENUSD_STATUS_OK,
        "empty face selection did not return texture metadata");
    Require(info.requested_face_count == 0 && info.required_byte_count == 0,
        "empty face selection returned non-empty output requirements");
}

void InvalidAndOverLimitRequestsReturnStatuses(const std::string& path)
{
    constexpr uint32_t firstFace = 0;
    constexpr uint32_t invalidFace = 4;
    std::array<openusd_silk_ptex_face_layout, 1> faces{};
    std::array<uint8_t, 48> pixels{};
    openusd_silk_ptex_decode_info info = MakeInfo();
    std::array<char, 256> errorText{};
    openusd_error_buffer error{errorText.data(), errorText.size(), 0};

    Require(
        Decode(path.c_str(), nullptr, 1, 8, 48, faces.data(),
            static_cast<uint32_t>(faces.size()),
            pixels.data(), pixels.size(), &info, &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
        "missing face ID input was not rejected as invalid");
    Require(
        Decode(path.c_str(), &invalidFace, 1, 8, 48, faces.data(),
            static_cast<uint32_t>(faces.size()),
            pixels.data(), pixels.size(), &info, &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
        "out-of-range face ID was not rejected as invalid");
    Require(
        Decode(path.c_str(), &firstFace, 1, 2, 48, faces.data(), 1,
            pixels.data(), pixels.size(), &info, &error) ==
            OPENUSD_STATUS_NATIVE_ERROR,
        "dimension limit violation did not return a native error");
    Require(
        Decode(path.c_str(), &firstFace, 1, 8, 47, faces.data(), 1,
            pixels.data(), pixels.size(), &info, &error) == OPENUSD_STATUS_NATIVE_ERROR,
        "decoded-byte limit violation did not return a native error");
    Require(
        Decode(path.c_str(), nullptr, 0, 8, 0, nullptr, 0, nullptr, 0, &info, &error) ==
            OPENUSD_STATUS_OK,
        "empty texture metadata request failed");
    Require(
        Decode((path + ".missing").c_str(), nullptr, 0, 8, 0, nullptr, 0, nullptr, 0,
            &info, &error) == OPENUSD_STATUS_NATIVE_ERROR,
        "missing Ptex file did not return a native error");

    info = MakeInfo();
    info.version++;
    Require(
        Decode(path.c_str(), nullptr, 0, 8, 0, nullptr, 0, nullptr, 0, &info, &error) ==
            OPENUSD_STATUS_INVALID_ARGUMENT,
        "unsupported Ptex decode packet version was accepted");

    info = MakeInfo();
    info.struct_size--;
    Require(
        Decode(path.c_str(), nullptr, 0, 8, 0, nullptr, 0, nullptr, 0, &info, &error) ==
            OPENUSD_STATUS_INVALID_ARGUMENT,
        "undersized Ptex decode packet was accepted");
}

} // namespace

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        std::cerr << "Usage: hdsilk_ptex_decode_api_probe <quad-colors.ptx>\n";
        return 1;
    }

    try
    {
        QueryAndCapacityFailuresPreserveDataBuffers(argv[1]);
        SuccessfulDecodePreservesLayoutAndPixels(argv[1]);
        DuplicateAndEmptyRequestsAreSupported(argv[1]);
        InvalidAndOverLimitRequestsReturnStatuses(argv[1]);
        return 0;
    }
    catch (const std::exception& exception)
    {
        std::cerr << exception.what() << '\n';
        return 1;
    }
}
