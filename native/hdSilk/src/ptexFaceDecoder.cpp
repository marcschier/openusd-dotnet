// Copyright (c) marcschier. Licensed under the MIT License.

#include "ptexFaceDecoder.h"

#include <Ptexture.h>

#include <algorithm>
#include <cstring>
#include <limits>
#include <memory>
#include <stdexcept>
#include <string>

#if defined(OPENUSD_HDSILK_ENABLE_TEST_HOOKS)
#include "openusd_hdsilk.h"
#include <atomic>
#endif

namespace
{
constexpr size_t MaximumRequestedFaces = 65536;

#if defined(OPENUSD_HDSILK_ENABLE_TEST_HOOKS)
std::atomic<uint64_t> g_ptexFaceReads{0};
#endif

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

struct PtexCacheDeleter
{
    void operator()(Ptex::PtexCache* cache) const noexcept
    {
        if (cache != nullptr)
        {
            cache->release();
        }
    }
};

class PtexMemoryInput final : public Ptex::PtexInputHandler
{
public:
    PtexMemoryInput(const uint8_t* source, size_t size) : _source(source), _size(size) {}

    Handle open(const char*) override
    {
        return new Cursor{};
    }

    void seek(Handle handle, int64_t position) override
    {
        Cursor& cursor = *static_cast<Cursor*>(handle);
        if (position < 0 || static_cast<uint64_t>(position) > _size)
        {
            cursor.valid = false;
            _error = "Ptex source seek exceeds the encoded snapshot.";
            return;
        }
        cursor.position = static_cast<size_t>(position);
        cursor.valid = true;
    }

    size_t read(void* buffer, size_t size, Handle handle) override
    {
        Cursor& cursor = *static_cast<Cursor*>(handle);
        if (!cursor.valid)
        {
            return 0;
        }
        const size_t count = std::min(size, _size - cursor.position);
        if (count != 0)
        {
            std::memcpy(buffer, _source + cursor.position, count);
            cursor.position += count;
        }
        if (count != size)
        {
            _error = "Ptex source ended before the requested bytes.";
        }
        return count;
    }

    bool close(Handle handle) override
    {
        delete static_cast<Cursor*>(handle);
        return true;
    }

    const char* lastError() override
    {
        return _error == nullptr ? "" : _error;
    }

    const char* Error() const noexcept { return _error; }

private:
    struct Cursor
    {
        size_t position = 0;
        bool valid = true;
    };

    const uint8_t* _source;
    size_t _size;
    const char* _error = nullptr;
};

class PtexCapturedErrors final : public Ptex::PtexErrorHandler
{
public:
    void reportError(const char* error) override
    {
        if (_error.empty())
        {
            _error = error == nullptr || error[0] == '\0'
                ? "Ptex reported an unspecified error."
                : error;
        }
    }

    const std::string& Error() const noexcept { return _error; }

private:
    std::string _error;
};

class PtexMemoryTexture
{
public:
    PtexMemoryTexture(const uint8_t* source, size_t sourceSize)
        : _input(source, sourceSize),
          _cache(Ptex::PtexCache::create(1, 8 * 1024 * 1024, false, &_input, &_errors))
    {
        if (!_cache)
        {
            throw std::runtime_error("Could not create a Ptex memory-source cache.");
        }
        Ptex::String error;
        _texture.reset(_cache->get("encoded-snapshot.ptx", error));
        if (!_texture)
        {
            throw std::runtime_error(
                std::string("Could not open the Ptex encoded snapshot: ") + error.c_str());
        }
        ThrowIfFailed();
    }

    Ptex::PtexTexture& Texture() const noexcept { return *_texture; }

    void ThrowIfFailed() const
    {
        if (!_errors.Error().empty())
        {
            throw std::runtime_error("Ptex encoded snapshot failed: " + _errors.Error());
        }
        if (_input.Error() != nullptr)
        {
            throw std::runtime_error(_input.Error());
        }
    }

private:
    PtexMemoryInput _input;
    PtexCapturedErrors _errors;
    std::unique_ptr<Ptex::PtexCache, PtexCacheDeleter> _cache;
    std::unique_ptr<Ptex::PtexTexture, PtexTextureDeleter> _texture;
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

void ValidateRequest(
    const uint32_t* faceIds,
    size_t faceCount,
    uint32_t maximumFaceDimension)
{
    if ((faceCount != 0 && faceIds == nullptr) ||
        maximumFaceDimension == 0 || faceCount > MaximumRequestedFaces)
    {
        throw std::invalid_argument("Ptex decode request is invalid.");
    }
}

void ValidatePathRequest(
    const std::string& path,
    const uint32_t* faceIds,
    size_t faceCount,
    uint32_t maximumFaceDimension)
{
    if (path.empty())
    {
        throw std::invalid_argument("Ptex decode path is invalid.");
    }
    ValidateRequest(faceIds, faceCount, maximumFaceDimension);
}

void ValidateMemoryRequest(
    const uint8_t* source,
    size_t sourceSize,
    const uint32_t* faceIds,
    size_t faceCount,
    uint32_t maximumFaceDimension)
{
    if (source == nullptr || sourceSize == 0)
    {
        throw std::invalid_argument("Ptex encoded snapshot is invalid.");
    }
    ValidateRequest(faceIds, faceCount, maximumFaceDimension);
}

void ValidatePixelBuffer(const uint8_t* pixels, uint64_t pixelCapacity)
{
    if (pixels == nullptr && pixelCapacity != 0)
    {
        throw std::invalid_argument("Ptex decode output buffer is invalid.");
    }
}

HdSilkPtexDecodedFaces DescribePtexFaces(
    Ptex::PtexTexture& texture,
    const uint32_t* faceIds,
    size_t faceCount,
    uint32_t maximumFaceDimension,
    uint64_t maximumDecodedBytes)
{
    const Ptex::PtexTexture::Info info = texture.getInfo();
    if ((info.meshType != Ptex::mt_triangle && info.meshType != Ptex::mt_quad) ||
        info.dataType < Ptex::dt_uint8 || info.dataType > Ptex::dt_float ||
        info.numChannels <= 0 || info.numFaces < 0 || info.alphaChannel < -1 ||
        info.alphaChannel >= info.numChannels)
    {
        throw std::runtime_error("Ptex texture header contains unsupported metadata.");
    }

    HdSilkPtexDecodedFaces result{};
    result.meshType = static_cast<uint32_t>(info.meshType);
    result.dataType = static_cast<uint32_t>(info.dataType);
    result.channelCount = static_cast<uint32_t>(info.numChannels);
    result.alphaChannel = info.alphaChannel;
    result.fileFaceCount = static_cast<uint32_t>(info.numFaces);
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
        const Ptex::FaceInfo& faceInfo = texture.getFaceInfo(faceId);
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
    result.requiredByteCount = totalBytes;
    return result;
}

void DecodePixels(
    Ptex::PtexTexture& texture,
    const HdSilkPtexDecodedFaces& decoded,
    uint8_t* pixels)
{
    for (const HdSilkPtexFaceData& face : decoded.faces)
    {
#if defined(OPENUSD_HDSILK_ENABLE_TEST_HOOKS)
        g_ptexFaceReads.fetch_add(1, std::memory_order_relaxed);
#endif
        texture.getData(
            static_cast<int>(face.faceId),
            pixels + static_cast<size_t>(face.dataOffsetBytes),
            static_cast<int>(face.rowStrideBytes));
    }
}

HdSilkPtexDecodedFaces DecodePtexFacesInto(
    Ptex::PtexTexture& texture,
    const uint32_t* faceIds,
    size_t faceCount,
    uint32_t maximumFaceDimension,
    uint64_t maximumDecodedBytes,
    size_t faceCapacity,
    uint8_t* pixels,
    uint64_t pixelCapacity)
{
    HdSilkPtexDecodedFaces result = DescribePtexFaces(
        texture, faceIds, faceCount, maximumFaceDimension, maximumDecodedBytes);
    if (faceCapacity < result.faces.size() ||
        pixelCapacity < result.requiredByteCount ||
        (result.requiredByteCount != 0 && pixels == nullptr))
    {
        return result;
    }

    if (result.requiredByteCount != 0)
    {
        DecodePixels(texture, result, pixels);
    }
    result.pixelsDecoded = true;
    return result;
}

std::unique_ptr<Ptex::PtexTexture, PtexTextureDeleter> OpenPtexTexture(
    const std::string& path)
{
    Ptex::String error;
    std::unique_ptr<Ptex::PtexTexture, PtexTextureDeleter> texture(
        Ptex::PtexTexture::open(path.c_str(), error, false));
    if (!texture)
    {
        throw std::runtime_error(
            "Could not open Ptex texture '" + path + "': " + error.c_str());
    }
    return texture;
}

} // namespace

HdSilkPtexDecodedFaces HdSilkDescribePtexFaces(
    const std::string& path,
    const uint32_t* faceIds,
    size_t faceCount,
    uint32_t maximumFaceDimension,
    uint64_t maximumDecodedBytes)
{
    ValidatePathRequest(path, faceIds, faceCount, maximumFaceDimension);
    std::unique_ptr<Ptex::PtexTexture, PtexTextureDeleter> texture =
        OpenPtexTexture(path);
    return DescribePtexFaces(
        *texture, faceIds, faceCount, maximumFaceDimension, maximumDecodedBytes);
}

HdSilkPtexDecodedFaces HdSilkDescribePtexFacesFromMemory(
    const uint8_t* source,
    size_t sourceSize,
    const uint32_t* faceIds,
    size_t faceCount,
    uint32_t maximumFaceDimension,
    uint64_t maximumDecodedBytes)
{
    ValidateMemoryRequest(source, sourceSize, faceIds, faceCount, maximumFaceDimension);
    PtexMemoryTexture texture(source, sourceSize);
    HdSilkPtexDecodedFaces result = DescribePtexFaces(
        texture.Texture(), faceIds, faceCount, maximumFaceDimension, maximumDecodedBytes);
    texture.ThrowIfFailed();
    return result;
}

HdSilkPtexDecodedFaces HdSilkDecodePtexFaces(
    const std::string& path,
    const uint32_t* faceIds,
    size_t faceCount,
    uint32_t maximumFaceDimension,
    uint64_t maximumDecodedBytes)
{
    ValidatePathRequest(path, faceIds, faceCount, maximumFaceDimension);
    std::unique_ptr<Ptex::PtexTexture, PtexTextureDeleter> texture =
        OpenPtexTexture(path);
    HdSilkPtexDecodedFaces result = DescribePtexFaces(
        *texture, faceIds, faceCount, maximumFaceDimension, maximumDecodedBytes);
    result.pixels.resize(static_cast<size_t>(result.requiredByteCount));
    if (!result.pixels.empty())
    {
        DecodePixels(*texture, result, result.pixels.data());
    }
    result.pixelsDecoded = true;
    return result;
}

#if defined(OPENUSD_HDSILK_ENABLE_TEST_HOOKS)
extern "C" OPENUSD_HDSILK_API uint64_t
openusd_hdsilk_test_get_ptex_face_read_count(void)
{
    return g_ptexFaceReads.load(std::memory_order_relaxed);
}
#endif

HdSilkPtexDecodedFaces HdSilkDecodePtexFacesInto(
    const std::string& path,
    const uint32_t* faceIds,
    size_t faceCount,
    uint32_t maximumFaceDimension,
    uint64_t maximumDecodedBytes,
    size_t faceCapacity,
    uint8_t* pixels,
    uint64_t pixelCapacity)
{
    ValidatePathRequest(path, faceIds, faceCount, maximumFaceDimension);
    ValidatePixelBuffer(pixels, pixelCapacity);
    std::unique_ptr<Ptex::PtexTexture, PtexTextureDeleter> texture =
        OpenPtexTexture(path);
    return DecodePtexFacesInto(
        *texture, faceIds, faceCount, maximumFaceDimension, maximumDecodedBytes,
        faceCapacity, pixels, pixelCapacity);
}

HdSilkPtexDecodedFaces HdSilkDecodePtexFacesFromMemoryInto(
    const uint8_t* source,
    size_t sourceSize,
    const uint32_t* faceIds,
    size_t faceCount,
    uint32_t maximumFaceDimension,
    uint64_t maximumDecodedBytes,
    size_t faceCapacity,
    uint8_t* pixels,
    uint64_t pixelCapacity)
{
    ValidateMemoryRequest(source, sourceSize, faceIds, faceCount, maximumFaceDimension);
    ValidatePixelBuffer(pixels, pixelCapacity);
    PtexMemoryTexture texture(source, sourceSize);
    HdSilkPtexDecodedFaces result = DecodePtexFacesInto(
        texture.Texture(), faceIds, faceCount, maximumFaceDimension, maximumDecodedBytes,
        faceCapacity, pixels, pixelCapacity);
    texture.ThrowIfFailed();
    return result;
}
