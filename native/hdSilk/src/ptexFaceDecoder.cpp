// Copyright (c) marcschier. Licensed under the MIT License.

#include "ptexFaceDecoder.h"

#include <Ptexture.h>

#include <algorithm>
#include <limits>
#include <memory>
#include <stdexcept>
#include <string>

namespace
{
constexpr size_t MaximumRequestedFaces = 65536;

struct PtexTextureDeleter
{
    void operator()(Ptex::PtexTexture* texture) const noexcept
    {
        if (texture != nullptr)
        {
            texture->release();
        }
    }
};

uint32_t GetDimension(int8_t log2Dimension, uint32_t maximumFaceDimension)
{
    if (log2Dimension < 0 || log2Dimension > 30)
    {
        throw std::runtime_error("Ptex face has an unsupported resolution exponent.");
    }

    const uint32_t dimension = uint32_t{1} << static_cast<uint32_t>(log2Dimension);
    if (dimension > maximumFaceDimension)
    {
        throw std::runtime_error("Ptex face exceeds the configured dimension limit.");
    }
    return dimension;
}

} // namespace

HdSilkPtexDecodedFaces HdSilkDecodePtexFaces(
    const std::string& path,
    const uint32_t* faceIds,
    size_t faceCount,
    uint32_t maximumFaceDimension,
    uint64_t maximumDecodedBytes)
{
    if (path.empty() || (faceCount != 0 && faceIds == nullptr) ||
        maximumFaceDimension == 0 || faceCount > MaximumRequestedFaces)
    {
        throw std::invalid_argument("Ptex decode request is invalid.");
    }

    Ptex::String error;
    std::unique_ptr<Ptex::PtexTexture, PtexTextureDeleter> texture(
        Ptex::PtexTexture::open(path.c_str(), error, false));
    if (!texture)
    {
        throw std::runtime_error(
            "Could not open Ptex texture '" + path + "': " + error.c_str());
    }

    const Ptex::PtexTexture::Info info = texture->getInfo();
    if ((info.meshType != Ptex::mt_triangle && info.meshType != Ptex::mt_quad) ||
        info.dataType < Ptex::dt_uint8 || info.dataType > Ptex::dt_float ||
        info.numChannels <= 0 || info.numFaces < 0 || info.alphaChannel < -1 ||
        info.alphaChannel >= info.numChannels)
    {
        throw std::runtime_error("Ptex texture header contains unsupported metadata.");
    }

    HdSilkPtexDecodedFaces result{
        static_cast<uint32_t>(info.meshType),
        static_cast<uint32_t>(info.dataType),
        static_cast<uint32_t>(info.numChannels),
        info.alphaChannel,
        static_cast<uint32_t>(info.numFaces),
        {},
        {}};
    result.faces.reserve(faceCount);

    const uint64_t channelBytes = static_cast<uint64_t>(Ptex::DataSize(info.dataType));
    const uint64_t pixelBytes = channelBytes * static_cast<uint64_t>(info.numChannels);
    uint64_t totalBytes = 0;
    for (size_t index = 0; index < faceCount; ++index)
    {
        if (faceIds[index] >= static_cast<uint32_t>(info.numFaces))
        {
            throw std::invalid_argument("Ptex face ID is outside the texture face range.");
        }

        const int faceId = static_cast<int>(faceIds[index]);
        const Ptex::FaceInfo& faceInfo = texture->getFaceInfo(faceId);
        const uint32_t width = GetDimension(faceInfo.res.ulog2, maximumFaceDimension);
        const uint32_t height = GetDimension(faceInfo.res.vlog2, maximumFaceDimension);
        const uint64_t rowBytes = static_cast<uint64_t>(width) * pixelBytes;
        const uint64_t pixelCount = static_cast<uint64_t>(width) * height;
        if (rowBytes > static_cast<uint64_t>(std::numeric_limits<int>::max()) ||
            pixelCount > static_cast<uint64_t>(std::numeric_limits<int>::max()) ||
            height > std::numeric_limits<uint64_t>::max() / rowBytes)
        {
            throw std::runtime_error("Ptex face byte size exceeds the supported range.");
        }

        const uint64_t faceBytes = rowBytes * height;
        if (totalBytes > maximumDecodedBytes ||
            faceBytes > maximumDecodedBytes - totalBytes)
        {
            throw std::runtime_error("Ptex decode exceeds the configured output byte limit.");
        }

        HdSilkPtexFaceData decodedFace{
            faceIds[index],
            width,
            height,
            static_cast<uint32_t>(rowBytes),
            faceInfo.flags,
            totalBytes,
            {},
            {}};
        for (int edge = 0; edge < 4; ++edge)
        {
            const int adjacentFace = faceInfo.adjface(edge);
            if (adjacentFace < -1 || adjacentFace >= info.numFaces)
            {
                throw std::runtime_error("Ptex face adjacency is outside the texture face range.");
            }
            decodedFace.adjacentFaceIds[static_cast<size_t>(edge)] = adjacentFace;
            decodedFace.adjacentEdgeIds[static_cast<size_t>(edge)] =
                static_cast<uint32_t>(faceInfo.adjedge(edge));
        }

        result.faces.push_back(decodedFace);
        totalBytes += faceBytes;
    }

    if (totalBytes > static_cast<uint64_t>(std::numeric_limits<size_t>::max()))
    {
        throw std::runtime_error("Ptex decode output exceeds the addressable size.");
    }
    result.pixels.resize(static_cast<size_t>(totalBytes));
    for (const HdSilkPtexFaceData& face : result.faces)
    {
        texture->getData(
            static_cast<int>(face.faceId),
            result.pixels.data() + static_cast<size_t>(face.dataOffsetBytes),
            static_cast<int>(face.rowStrideBytes));
    }
    return result;
}
