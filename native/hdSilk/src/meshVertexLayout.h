// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef HDSILK_MESH_VERTEX_LAYOUT_H
#define HDSILK_MESH_VERTEX_LAYOUT_H

#include "sceneState.h"

#include <algorithm>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <unordered_map>
#include <utility>

PXR_NAMESPACE_OPEN_SCOPE

struct HdSilkMeshVertexLayout
{
    std::vector<uint32_t> pointIndices;
    std::vector<uint32_t> triangleIndices;
};

namespace HdSilkVertexLayoutDetail
{
struct CornerHash
{
    const std::vector<uint32_t>& points;
    const HdSilkMeshAttributes& attributes;

    size_t operator()(uint32_t corner) const
    {
        uint64_t hash = (14695981039346656037ull ^ points[corner]) * 1099511628211ull;
        for (const HdSilkMeshAttribute& attribute : attributes)
        {
            if (attribute.interpolation != OPENUSD_SILK_INTERPOLATION_VERTEX)
            {
                continue;
            }
            const size_t offset = static_cast<size_t>(corner) * attribute.componentCount;
            for (uint32_t component = 0; component < attribute.componentCount; ++component)
            {
                uint32_t bits = 0;
                std::memcpy(&bits, &attribute.data[offset + component], sizeof(bits));
                hash = (hash ^ bits) * 1099511628211ull;
            }
        }
        return static_cast<size_t>(hash);
    }
};

struct CornerEqual
{
    const std::vector<uint32_t>& points;
    const HdSilkMeshAttributes& attributes;

    bool operator()(uint32_t left, uint32_t right) const
    {
        if (points[left] != points[right])
        {
            return false;
        }
        for (const HdSilkMeshAttribute& attribute : attributes)
        {
            if (attribute.interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
                std::memcmp(
                    attribute.data.data() + static_cast<size_t>(left) * attribute.componentCount,
                    attribute.data.data() + static_cast<size_t>(right) * attribute.componentCount,
                    attribute.componentCount * sizeof(float)) != 0)
            {
                return false;
            }
        }
        return true;
    }
};
}

inline HdSilkMeshVertexLayout HdSilkBuildSharedVertexLayout(
    const std::vector<uint32_t>& sourceIndices,
    size_t sourcePointCount,
    HdSilkMeshAttributes& attributes)
{
    if (sourceIndices.size() > std::numeric_limits<uint32_t>::max())
    {
        throw std::length_error("The hdSilk corner layout exceeds its 32-bit index range.");
    }
    for (const HdSilkMeshAttribute& attribute : std::as_const(attributes))
    {
        if (attribute.interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
            (attribute.componentCount == 0 ||
             sourceIndices.size() > std::numeric_limits<size_t>::max() / attribute.componentCount ||
             attribute.data.size() != sourceIndices.size() * attribute.componentCount))
        {
            throw std::invalid_argument("An hdSilk corner attribute does not match the triangle layout.");
        }
    }

    HdSilkMeshVertexLayout result;
    std::vector<uint32_t> corners;
    const size_t initialCount = std::min(sourceIndices.size(), sourcePointCount);
    result.pointIndices.reserve(initialCount);
    result.triangleIndices.reserve(sourceIndices.size());
    corners.reserve(initialCount);
    {
        std::unordered_map<uint32_t, uint32_t,
            HdSilkVertexLayoutDetail::CornerHash, HdSilkVertexLayoutDetail::CornerEqual> vertices(
                0, {sourceIndices, attributes}, {sourceIndices, attributes});
        vertices.reserve(initialCount);
        for (size_t corner = 0; corner < sourceIndices.size(); ++corner)
        {
            if (sourceIndices[corner] >= sourcePointCount)
            {
                throw std::invalid_argument("An hdSilk corner references an absent source point.");
            }
            const uint32_t key = static_cast<uint32_t>(corner);
            const auto found = vertices.find(key);
            if (found != vertices.end())
            {
                result.triangleIndices.push_back(found->second);
            }
            else
            {
                const uint32_t vertex = static_cast<uint32_t>(result.pointIndices.size());
                vertices.emplace(key, vertex);
                result.pointIndices.push_back(sourceIndices[corner]);
                corners.push_back(key);
                result.triangleIndices.push_back(vertex);
            }
        }
    }

    // Point identity and all corner values participate in the key. Coincident
    // authored points, seams and signed-zero distinctions must remain separate.
    for (HdSilkMeshAttribute& attribute : attributes)
    {
        if (attribute.interpolation != OPENUSD_SILK_INTERPOLATION_VERTEX)
        {
            continue;
        }
        std::vector<float> compact(corners.size() * attribute.componentCount);
        for (size_t vertex = 0; vertex < corners.size(); ++vertex)
        {
            std::memcpy(
                compact.data() + vertex * attribute.componentCount,
                attribute.data.data() + static_cast<size_t>(corners[vertex]) * attribute.componentCount,
                attribute.componentCount * sizeof(float));
        }
        attribute.data.swap(compact);
    }
    return result;
}

PXR_NAMESPACE_CLOSE_SCOPE

#endif
