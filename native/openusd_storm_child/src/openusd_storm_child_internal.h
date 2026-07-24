// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_STORM_CHILD_INTERNAL_H
#define OPENUSD_STORM_CHILD_INTERNAL_H

#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>

#include <cstdint>

inline bool OpenUsdStormChildIsValidWglAddress(PROC address) noexcept
{
    const auto value = reinterpret_cast<std::intptr_t>(address);
    return address != nullptr &&
        value != 1 &&
        value != 2 &&
        value != 3 &&
        value != -1;
}

#endif
