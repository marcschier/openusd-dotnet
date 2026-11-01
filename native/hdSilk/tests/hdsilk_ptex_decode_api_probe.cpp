// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_hdsilk.h"
#include "hdsilk_test_hooks.h"

#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>
#include <fstream>
#include <iostream>
#include <iterator>
#include <numeric>
#include <stdexcept>
#include <string>
#include <vector>

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

openusd_status Describe(
    const char* path,
    const uint32_t* faceIds,
    uint32_t faceCount,
    uint32_t maximumDimension,
    uint64_t maximumBytes,
    openusd_silk_ptex_face_layout* faces,
    uint32_t faceCapacity,
    openusd_silk_ptex_decode_info* info,
    openusd_error_buffer* error)
{
    return openusd_silk_ptex_describe_faces(
        path,
        faceIds,
        faceCount,
        maximumDimension,
        maximumBytes,
        faces,
        faceCapacity,
        info,
        error);
}

openusd_status DecodeMemory(
    const std::vector<uint8_t>& source,
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
    return openusd_silk_ptex_decode_faces_from_memory(
        source.data(), source.size(), faceIds, faceCount, maximumDimension,
        maximumBytes, faces, faceCapacity, pixels, pixelCapacity, info, error);
}

openusd_status DescribeMemory(
    const std::vector<uint8_t>& source,
    const uint32_t* faceIds,
    uint32_t faceCount,
    uint32_t maximumDimension,
    uint64_t maximumBytes,
    openusd_silk_ptex_face_layout* faces,
    uint32_t faceCapacity,
    openusd_silk_ptex_decode_info* info,
    openusd_error_buffer* error)
{
    return openusd_silk_ptex_describe_faces_from_memory(
        source.data(), source.size(), faceIds, faceCount, maximumDimension,
        maximumBytes, faces, faceCapacity, info, error);
}

void MemorySourceMatchesPathAndPreservesShortBuffers(const std::string& path)
{
    std::ifstream input(path, std::ios::binary);
    Require(input.good(), "could not open Ptex memory-source fixture");
    const std::vector<uint8_t> source(
        (std::istreambuf_iterator<char>(input)), std::istreambuf_iterator<char>());
    Require(source.size() > 16, "Ptex memory-source fixture is unexpectedly short");
    constexpr std::array<uint32_t, 4> faceIds = {3, 1, 1, 0};
    std::array<openusd_silk_ptex_face_layout, 4> faces;
    std::memset(faces.data(), 0xA5, sizeof(faces));
    const auto unchangedFaces = faces;
    std::array<uint8_t, 192> pixels;
    pixels.fill(0xA5);
    const auto unchangedPixels = pixels;
    std::array<char, 256> errorText{};
    openusd_error_buffer error{errorText.data(), errorText.size(), 0};
    openusd_silk_ptex_decode_info info = MakeInfo();
    const uint64_t faceReadsBefore = openusd_hdsilk_test_get_ptex_face_read_count();

    Require(
        DescribeMemory(source, faceIds.data(), 4, 8, 192, nullptr, 0, &info, &error) ==
            OPENUSD_STATUS_BUFFER_TOO_SMALL,
        "Ptex memory describe did not report required layout capacity");
    Require(info.file_face_count == 4 && info.requested_face_count == 4 &&
        info.required_byte_count == 192,
        "Ptex memory describe returned incorrect sizing metadata");
    info = MakeInfo();
    Require(
        DescribeMemory(source, faceIds.data(), 4, 8, 192, faces.data(), 3, &info, &error) ==
            OPENUSD_STATUS_BUFFER_TOO_SMALL,
        "Ptex memory describe accepted an undersized layout buffer");
    Require(std::memcmp(faces.data(), unchangedFaces.data(), sizeof(faces)) == 0,
        "Ptex memory describe modified an undersized layout buffer");
    info = MakeInfo();
    Require(
        DescribeMemory(source, faceIds.data(), 4, 8, 192, faces.data(), 4, &info, nullptr) ==
            OPENUSD_STATUS_OK,
        "Ptex memory describe failed with sufficient capacity");
    Require(openusd_hdsilk_test_get_ptex_face_read_count() == faceReadsBefore,
        "Ptex memory describe read face pixels");

    std::array<openusd_silk_ptex_face_layout, 4> pathFaces{};
    std::array<uint8_t, 192> pathPixels{};
    openusd_silk_ptex_decode_info pathInfo = MakeInfo();
    Require(
        Decode(path.c_str(), faceIds.data(), 4, 8, 192, pathFaces.data(), 4,
            pathPixels.data(), pathPixels.size(), &pathInfo, nullptr) == OPENUSD_STATUS_OK,
        "Ptex path decode failed while checking memory-source parity");
    Require(std::memcmp(&info, &pathInfo, sizeof(info)) == 0 &&
        std::memcmp(faces.data(), pathFaces.data(), sizeof(faces)) == 0,
        "Ptex memory describe differs from path decode");

    faces = unchangedFaces;
    info = MakeInfo();
    Require(
        DecodeMemory(source, faceIds.data(), 4, 8, 192, nullptr, 0, nullptr, 0, &info, &error) ==
            OPENUSD_STATUS_BUFFER_TOO_SMALL,
        "Ptex memory decode sizing did not report required capacity");
    Require(info.requested_face_count == 4 && info.required_byte_count == 192,
        "Ptex memory decode sizing returned incorrect counts");
    info = MakeInfo();
    Require(
        DecodeMemory(source, faceIds.data(), 4, 8, 192, faces.data(), 3,
            pixels.data(), pixels.size(), &info, &error) == OPENUSD_STATUS_BUFFER_TOO_SMALL,
        "Ptex memory decode accepted an undersized layout buffer");
    Require(std::memcmp(faces.data(), unchangedFaces.data(), sizeof(faces)) == 0 &&
        pixels == unchangedPixels, "Ptex memory decode modified buffers after layout refusal");
    info = MakeInfo();
    Require(
        DecodeMemory(source, faceIds.data(), 4, 8, 192, faces.data(), 4,
            pixels.data(), pixels.size() - 1, &info, &error) == OPENUSD_STATUS_BUFFER_TOO_SMALL,
        "Ptex memory decode accepted an undersized pixel buffer");
    Require(std::memcmp(faces.data(), unchangedFaces.data(), sizeof(faces)) == 0 &&
        pixels == unchangedPixels, "Ptex memory decode modified buffers after pixel refusal");
    Require(openusd_hdsilk_test_get_ptex_face_read_count() == faceReadsBefore + 4,
        "Ptex memory decode read pixels before output admission");

    info = MakeInfo();
    Require(
        DecodeMemory(source, faceIds.data(), 4, 8, 192, faces.data(), 4,
            pixels.data(), pixels.size(), &info, nullptr) == OPENUSD_STATUS_OK,
        "Ptex memory decode failed with sufficient capacity");
    Require(std::memcmp(&info, &pathInfo, sizeof(info)) == 0 &&
        std::memcmp(faces.data(), pathFaces.data(), sizeof(faces)) == 0 &&
        pixels == pathPixels, "Ptex memory decode differs from path decode");

    info = MakeInfo();
    Require(
        DescribeMemory(source, nullptr, 0, 8, 0, nullptr, 0, &info, nullptr) == OPENUSD_STATUS_OK &&
        info.file_face_count == 4 && info.requested_face_count == 0 && info.required_byte_count == 0,
        "empty Ptex memory describe returned incorrect metadata");
    info = MakeInfo();
    Require(
        DecodeMemory(source, nullptr, 0, 8, 0, nullptr, 0, nullptr, 0, &info, nullptr) ==
            OPENUSD_STATUS_OK &&
        info.file_face_count == 4 && info.requested_face_count == 0 && info.required_byte_count == 0,
        "empty Ptex memory decode returned incorrect metadata");

    info = MakeInfo();
    Require(
        openusd_silk_ptex_decode_faces_from_memory(nullptr, source.size(), faceIds.data(), 1, 8, 48,
            nullptr, 0, nullptr, 0, &info, &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
        "Ptex memory decode accepted a null source");
    Require(
        openusd_silk_ptex_describe_faces_from_memory(source.data(), 0, faceIds.data(), 1, 8, 48,
            nullptr, 0, &info, &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
        "Ptex memory describe accepted an empty source");
    const std::vector<uint8_t> truncated(source.begin(), source.begin() + 16);
    info = MakeInfo();
    Require(
        DescribeMemory(truncated, faceIds.data(), 1, 8, 48, nullptr, 0, &info, &error) ==
            OPENUSD_STATUS_NATIVE_ERROR && error.required > 0,
        "Ptex memory describe accepted a truncated source without a diagnostic");
    info = MakeInfo();
    Require(
        DecodeMemory(truncated, faceIds.data(), 1, 8, 48, nullptr, 0, nullptr, 0, &info, &error) ==
            OPENUSD_STATUS_NATIVE_ERROR && error.required > 0,
        "Ptex memory decode accepted a truncated source without a diagnostic");
}

void DescribeMatchesDecodeAndPreservesShortBuffers(const std::string& path)
{
    constexpr std::array<uint32_t, 4> faceIds = {3, 1, 1, 0};
    std::array<openusd_silk_ptex_face_layout, 4> described;
    std::memset(described.data(), 0xA5, sizeof(described));
    const auto unchanged = described;
    openusd_silk_ptex_decode_info info = MakeInfo();
    std::array<char, 256> errorText{};
    openusd_error_buffer error{errorText.data(), errorText.size(), 0};
    const uint64_t faceReadsBefore = openusd_hdsilk_test_get_ptex_face_read_count();

    Require(
        Describe(path.c_str(), faceIds.data(), static_cast<uint32_t>(faceIds.size()),
            8, 192, nullptr, 0, &info, &error) == OPENUSD_STATUS_BUFFER_TOO_SMALL,
        "Ptex describe sizing did not report the required layout capacity");
    Require(info.file_face_count == 4 && info.requested_face_count == faceIds.size() &&
        info.required_byte_count == 192,
        "Ptex describe sizing returned incorrect counts");

    info = MakeInfo();
    Require(
        Describe(path.c_str(), faceIds.data(), static_cast<uint32_t>(faceIds.size()),
            8, 192, described.data(), 3, &info, &error) == OPENUSD_STATUS_BUFFER_TOO_SMALL,
        "Ptex describe accepted an undersized layout buffer");
    Require(std::memcmp(described.data(), unchanged.data(), sizeof(described)) == 0,
        "Ptex describe partially wrote an undersized layout buffer");
    Require(info.requested_face_count == faceIds.size() && info.required_byte_count == 192,
        "Ptex describe refusal omitted the required capacities");

    info = MakeInfo();
    Require(
        Describe(path.c_str(), faceIds.data(), static_cast<uint32_t>(faceIds.size()),
            8, 192, described.data(), static_cast<uint32_t>(described.size()), &info, nullptr) ==
            OPENUSD_STATUS_OK,
        "Ptex describe failed with sufficient layout capacity");
    Require(openusd_hdsilk_test_get_ptex_face_read_count() == faceReadsBefore,
        "Ptex describe read face pixels");
    std::array<openusd_silk_ptex_face_layout, 4> decoded{};
    std::array<uint8_t, 192> pixels{};
    openusd_silk_ptex_decode_info decodedInfo = MakeInfo();
    Require(
        Decode(path.c_str(), faceIds.data(), static_cast<uint32_t>(faceIds.size()),
            8, pixels.size(), decoded.data(), static_cast<uint32_t>(decoded.size()),
            pixels.data(), pixels.size(), &decodedInfo, nullptr) == OPENUSD_STATUS_OK,
        "Ptex decode failed while checking described layouts");
    Require(openusd_hdsilk_test_get_ptex_face_read_count() == faceReadsBefore + faceIds.size(),
        "Ptex face-read counter did not observe the decode control");
    Require(std::memcmp(&info, &decodedInfo, sizeof(info)) == 0,
        "Ptex describe and decode returned different metadata");
    Require(std::memcmp(described.data(), decoded.data(), sizeof(described)) == 0,
        "Ptex describe and decode returned different layouts");
    Require(described[0].face_id == 3 && described[1].face_id == 1 &&
        described[2].face_id == 1 && described[3].face_id == 0 &&
        described[0].data_offset_bytes == 0 && described[1].data_offset_bytes == 48 &&
        described[2].data_offset_bytes == 96 && described[3].data_offset_bytes == 144,
        "Ptex describe changed request order or duplicate offsets");

    info = MakeInfo();
    Require(
        Describe(path.c_str(), nullptr, 0, 8, 0, nullptr, 0, &info, nullptr) ==
            OPENUSD_STATUS_OK,
        "empty Ptex describe failed");
    Require(info.file_face_count == 4 && info.requested_face_count == 0 &&
        info.required_byte_count == 0,
        "empty Ptex describe returned incorrect metadata");
}

void DescribeReportsInteriorAdjacency(const std::string& path)
{
    openusd_silk_ptex_decode_info info = MakeInfo();
    Require(Describe(path.c_str(), nullptr, 0, 4096, 0, nullptr, 0, &info, nullptr) ==
        OPENUSD_STATUS_OK, "Ptex adjacency metadata query failed");
    Require(info.file_face_count > 0 && info.file_face_count <= 65536,
        "Ptex adjacency fixture has an invalid face count");

    std::vector<uint32_t> faceIds(info.file_face_count);
    std::iota(faceIds.begin(), faceIds.end(), uint32_t{0});
    std::vector<openusd_silk_ptex_face_layout> described(faceIds.size());
    info = MakeInfo();
    Require(
        Describe(path.c_str(), faceIds.data(), static_cast<uint32_t>(faceIds.size()),
            4096, 8 * 1024 * 1024, described.data(),
            static_cast<uint32_t>(described.size()), &info, nullptr) == OPENUSD_STATUS_OK,
        "Ptex adjacency describe failed");
    std::vector<openusd_silk_ptex_face_layout> decoded(faceIds.size());
    std::vector<uint8_t> pixels(static_cast<size_t>(info.required_byte_count));
    openusd_silk_ptex_decode_info decodedInfo = MakeInfo();
    Require(
        Decode(path.c_str(), faceIds.data(), static_cast<uint32_t>(faceIds.size()),
            4096, 8 * 1024 * 1024, decoded.data(), static_cast<uint32_t>(decoded.size()),
            pixels.data(), pixels.size(), &decodedInfo, nullptr) == OPENUSD_STATUS_OK,
        "Ptex adjacency decode failed");
    Require(std::memcmp(&info, &decodedInfo, sizeof(info)) == 0 &&
        std::memcmp(described.data(), decoded.data(),
            described.size() * sizeof(openusd_silk_ptex_face_layout)) == 0,
        "Ptex describe changed interior face layouts");

    size_t adjacentEdges = 0;
    for (const openusd_silk_ptex_face_layout& face : described)
    {
        for (size_t edge = 0; edge < 4; ++edge)
        {
            const int32_t adjacentId = face.adjacent_face_ids[edge];
            if (adjacentId < 0)
            {
                continue;
            }
            const uint32_t adjacentEdge = face.adjacent_edge_ids[edge];
            Require(static_cast<size_t>(adjacentId) < described.size() && adjacentEdge < 4,
                "Ptex describe returned an invalid adjacency");
            const openusd_silk_ptex_face_layout& adjacent =
                described[static_cast<size_t>(adjacentId)];
            Require(adjacent.adjacent_face_ids[adjacentEdge] == static_cast<int32_t>(face.face_id) &&
                adjacent.adjacent_edge_ids[adjacentEdge] == edge,
                "Ptex describe returned nonreciprocal adjacency");
            ++adjacentEdges;
        }
    }
    Require(adjacentEdges > 0, "Ptex adjacency fixture has no interior edges");
}

void DescribeAdmitsTheMaximumBatchAndRejectsLateInvalidFaces(const std::string& path)
{
    constexpr uint32_t maximumFaces = 65536;
    constexpr uint64_t faceBytes = 48;
    std::vector<uint32_t> faceIds(maximumFaces, 1);
    std::vector<openusd_silk_ptex_face_layout> faces(maximumFaces);
    openusd_silk_ptex_decode_info info = MakeInfo();
    Require(
        Describe(path.c_str(), faceIds.data(), maximumFaces, 8, maximumFaces * faceBytes,
            faces.data(), maximumFaces, &info, nullptr) == OPENUSD_STATUS_OK,
        "Ptex describe rejected the maximum face count");
    Require(info.requested_face_count == maximumFaces &&
        info.required_byte_count == maximumFaces * faceBytes &&
        faces.front().face_id == 1 && faces.front().data_offset_bytes == 0 &&
        faces.back().face_id == 1 &&
        faces.back().data_offset_bytes == (maximumFaces - 1) * faceBytes,
        "Ptex describe changed the maximum batch extent or order");

    constexpr std::array<uint32_t, 2> invalidIds = {0, 4};
    std::array<openusd_silk_ptex_face_layout, 2> outputs;
    std::memset(outputs.data(), 0xA5, sizeof(outputs));
    const auto unchanged = outputs;
    info = MakeInfo();
    Require(
        Describe(path.c_str(), invalidIds.data(), static_cast<uint32_t>(invalidIds.size()),
            8, 96, outputs.data(), static_cast<uint32_t>(outputs.size()), &info, nullptr) ==
            OPENUSD_STATUS_INVALID_ARGUMENT,
        "Ptex describe accepted an invalid ID after a valid face");
    Require(std::memcmp(outputs.data(), unchanged.data(), sizeof(outputs)) == 0,
        "Ptex describe wrote layouts before validating the complete selection");
}

void DescribeRejectsInvalidRequestsWithoutWritingLayouts(const std::string& path)
{
    constexpr uint32_t firstFace = 0;
    constexpr uint32_t invalidFace = 4;
    std::array<openusd_silk_ptex_face_layout, 1> faces;
    std::memset(faces.data(), 0xA5, sizeof(faces));
    const auto unchanged = faces;
    openusd_silk_ptex_decode_info info = MakeInfo();
    std::array<char, 256> errorText{};
    openusd_error_buffer error{errorText.data(), errorText.size(), 0};

    Require(Describe(nullptr, &firstFace, 1, 8, 48, faces.data(), 1, &info, &error) ==
        OPENUSD_STATUS_INVALID_ARGUMENT, "Ptex describe accepted a null path");
    Require(Describe(path.c_str(), &firstFace, 1, 8, 48, faces.data(), 1, nullptr, &error) ==
        OPENUSD_STATUS_INVALID_ARGUMENT, "Ptex describe accepted a null info packet");
    Require(Describe(path.c_str(), nullptr, 1, 8, 48, faces.data(), 1, &info, &error) ==
        OPENUSD_STATUS_INVALID_ARGUMENT, "Ptex describe accepted missing face IDs");
    Require(Describe(path.c_str(), &invalidFace, 1, 8, 48, faces.data(), 1, &info, &error) ==
        OPENUSD_STATUS_INVALID_ARGUMENT, "Ptex describe accepted an out-of-range face ID");
    Require(Describe(path.c_str(), &firstFace, 1, 8, 48, nullptr, 1, &info, &error) ==
        OPENUSD_STATUS_INVALID_ARGUMENT, "Ptex describe accepted a null output with nonzero capacity");
    Require(Describe(path.c_str(), &firstFace, 1, 0, 48, faces.data(), 1, &info, &error) ==
        OPENUSD_STATUS_INVALID_ARGUMENT, "Ptex describe accepted a zero dimension limit");
    Require(Describe(path.c_str(), &firstFace, 65537, 8, 48, faces.data(), 1, &info, &error) ==
        OPENUSD_STATUS_INVALID_ARGUMENT, "Ptex describe accepted too many face IDs");
    Require(Describe(path.c_str(), &firstFace, 1, 2, 48, faces.data(), 1, &info, &error) ==
        OPENUSD_STATUS_NATIVE_ERROR, "Ptex describe ignored its face dimension limit");
    Require(Describe(path.c_str(), &firstFace, 1, 8, 47, faces.data(), 1, &info, &error) ==
        OPENUSD_STATUS_NATIVE_ERROR, "Ptex describe ignored its pixel byte limit");
    Require(Describe((path + ".missing").c_str(), &firstFace, 1, 8, 48, faces.data(),
        1, &info, &error) == OPENUSD_STATUS_NATIVE_ERROR,
        "Ptex describe accepted a missing asset");
    Require(error.required > 0, "Ptex describe omitted the failure diagnostic");

    info = MakeInfo();
    ++info.version;
    Require(Describe(path.c_str(), &firstFace, 1, 8, 48, faces.data(), 1, &info, &error) ==
        OPENUSD_STATUS_INVALID_ARGUMENT, "Ptex describe accepted an unsupported info version");
    info = MakeInfo();
    --info.struct_size;
    Require(Describe(path.c_str(), &firstFace, 1, 8, 48, faces.data(), 1, &info, &error) ==
        OPENUSD_STATUS_INVALID_ARGUMENT, "Ptex describe accepted an undersized info packet");
    Require(std::memcmp(faces.data(), unchanged.data(), sizeof(faces)) == 0,
        "Ptex describe modified layouts after a failed request");
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
    if (argc != 3)
    {
        std::cerr << "Usage: hdsilk_ptex_decode_api_probe <quad-colors.ptx> <tetrahedron.ptx>\n";
        return 1;
    }

    try
    {
        MemorySourceMatchesPathAndPreservesShortBuffers(argv[1]);
        DescribeMatchesDecodeAndPreservesShortBuffers(argv[1]);
        DescribeReportsInteriorAdjacency(argv[2]);
        DescribeAdmitsTheMaximumBatchAndRejectsLateInvalidFaces(argv[1]);
        DescribeRejectsInvalidRequestsWithoutWritingLayouts(argv[1]);
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
