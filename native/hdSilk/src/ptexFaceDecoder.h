// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_HDSILK_PTEX_FACE_DECODER_H
#define OPENUSD_HDSILK_PTEX_FACE_DECODER_H

#include <array>
#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

struct HdSilkPtexFaceData
{
    uint32_t faceId;
    uint32_t width;
    uint32_t height;
    uint32_t rowStrideBytes;
    uint32_t flags;
    uint64_t dataOffsetBytes;
    std::array<int32_t, 4> adjacentFaceIds;
    std::array<uint32_t, 4> adjacentEdgeIds;
};

struct HdSilkPtexDecodedFaces
{
    uint32_t meshType;
    uint32_t dataType;
    uint32_t channelCount;
    int32_t alphaChannel;
    uint32_t fileFaceCount;
    std::vector<HdSilkPtexFaceData> faces;
    std::vector<uint8_t> pixels;
};

HdSilkPtexDecodedFaces HdSilkDecodePtexFaces(
    const std::string& path,
    const uint32_t* faceIds,
    size_t faceCount,
    uint32_t maximumFaceDimension,
    uint64_t maximumDecodedBytes);

#endif
