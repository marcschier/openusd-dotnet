// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_HDSILK_GEOMETRY_SYNC_PAGE_H
#define OPENUSD_HDSILK_GEOMETRY_SYNC_PAGE_H

#include "openusd_hdsilk.h"

#include "pxr/base/gf/vec3f.h"
#include "pxr/base/vt/array.h"

#include <cstdint>
#include <cstring>
#include <string>
#include <vector>

namespace HdSilkTest
{
template <typename T>
bool ReadPageValue(const std::vector<uint8_t>& page, size_t offset, T* value)
{
    if (offset > page.size() || sizeof(T) > page.size() - offset)
    {
        return false;
    }
    std::memcpy(value, page.data() + offset, sizeof(T));
    return true;
}

inline bool FindMeshCommand(
    const std::vector<uint8_t>& page,
    uint32_t expectedType,
    const std::string& expectedPath,
    size_t* commandOffset,
    uint32_t* commandSize)
{
    const size_t pathOffset = expectedType == OPENUSD_SILK_COMMAND_MESH_UPSERT ? 268 : 24;
    const size_t pathLengthOffset = expectedType == OPENUSD_SILK_COMMAND_MESH_UPSERT ? 48 : 20;
    for (size_t offset = 0; offset < page.size();)
    {
        uint32_t type = 0;
        uint32_t size = 0;
        if (!ReadPageValue(page, offset, &type) ||
            !ReadPageValue(page, offset + 4, &size) ||
            size < 8 || size > page.size() - offset)
        {
            return false;
        }
        if (type == expectedType && size >= pathOffset)
        {
            uint32_t pathLength = 0;
            if (!ReadPageValue(page, offset + pathLengthOffset, &pathLength) ||
                pathLength > size - pathOffset)
            {
                return false;
            }
            if (pathLength == expectedPath.size() &&
                std::memcmp(page.data() + offset + pathOffset, expectedPath.data(), pathLength) == 0)
            {
                *commandOffset = offset;
                *commandSize = size;
                return true;
            }
        }
        offset += size;
    }
    return false;
}

inline bool HasMeshRemoval(const std::vector<uint8_t>& page, const std::string& path)
{
    size_t offset = 0;
    uint32_t size = 0;
    return FindMeshCommand(page, OPENUSD_SILK_COMMAND_MESH_REMOVE, path, &offset, &size);
}

inline bool HasMeshPoints(
    const std::vector<uint8_t>& page,
    const std::string& path,
    const pxr::VtVec3fArray& expected)
{
    size_t offset = 0;
    uint32_t size = 0;
    if (!FindMeshCommand(page, OPENUSD_SILK_COMMAND_MESH_UPSERT, path, &offset, &size))
    {
        return false;
    }
    uint32_t pathLength = 0;
    uint32_t pointCount = 0;
    if (!ReadPageValue(page, offset + 48, &pathLength) ||
        !ReadPageValue(page, offset + 52, &pointCount) ||
        pointCount != expected.size())
    {
        return false;
    }
    const size_t pointOffset = 268 + static_cast<size_t>(pathLength);
    if (pointOffset > size || pointCount > (size - pointOffset) / (3 * sizeof(float)))
    {
        return false;
    }
    for (size_t i = 0; i < expected.size(); ++i)
    {
        for (size_t component = 0; component < 3; ++component)
        {
            float value = 0;
            if (!ReadPageValue(page, offset + pointOffset + (i * 3 + component) * sizeof(float), &value) ||
                value != expected[i][component])
            {
                return false;
            }
        }
    }
    return true;
}
}

#endif
