// Copyright (c) marcschier. Licensed under the MIT License.
#ifndef HDSILK_POINT_PREPARATION_H
#define HDSILK_POINT_PREPARATION_H

#include "meshPreparation.h"

#include <limits>
#include <stdexcept>

struct HdSilkPointPreparationPlan
{
    uint64_t bytes = 0;

    // Charge source conversion, emitted positions and normals, plus indices,
    // primitive identities and point origins, even when point identity is refused.
    explicit HdSilkPointPreparationPlan(uint64_t points)
    {
        if (points > std::numeric_limits<uint32_t>::max())
        {
            throw std::overflow_error(
                "The hdSilk points primitive exceeds the 32-bit vertex index.");
        }
        bytes = HdSilkMeshPreparationPlan::Multiply(
            points, 9 * sizeof(float) + 3 * sizeof(uint32_t));
    }
};

#endif
