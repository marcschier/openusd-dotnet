// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef HDSILK_TEST_NORMAL_CORRESPONDENCE_PAGE_H
#define HDSILK_TEST_NORMAL_CORRESPONDENCE_PAGE_H

#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>

inline bool HdSilkTestReadNormalCorrespondence(
    const uint8_t* data, size_t end, size_t* cursor, uint32_t pointCount)
{
    if (*cursor == end)
    {
        return true;
    }
    if (*cursor > end || end - *cursor < 12)
    {
        return false;
    }
    uint32_t identities = 0;
    uint32_t corners = 0;
    uint32_t extraPoints = 0;
    std::memcpy(&identities, data + *cursor, 4);
    std::memcpy(&corners, data + *cursor + 4, 4);
    std::memcpy(&extraPoints, data + *cursor + 8, 4);
    *cursor += 12;
    const uint64_t entries = uint64_t{identities} + uint64_t{corners} * 3;
    const uint64_t totalEntries = entries + uint64_t{extraPoints} * 3;
    if (identities == 0 || identities != pointCount ||
        totalEntries > (end - *cursor) / 4 || totalEntries * 4 != end - *cursor)
    {
        return false;
    }
    for (uint64_t entry = 0; entry < entries; ++entry)
    {
        uint32_t value = 0;
        std::memcpy(&value, data + *cursor, 4);
        *cursor += 4;
        if (value > static_cast<uint32_t>(std::numeric_limits<int32_t>::max()) ||
            (entry >= identities && value >= uint64_t{pointCount} + extraPoints))
        {
            return false;
        }
    }
    for (uint64_t component = 0; component < uint64_t{extraPoints} * 3; ++component)
    {
        float value = 0;
        std::memcpy(&value, data + *cursor, 4);
        *cursor += 4;
        if (!std::isfinite(value))
        {
            return false;
        }
    }
    return true;
}

#endif
