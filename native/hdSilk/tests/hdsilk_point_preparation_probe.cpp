// Copyright (c) marcschier. Licensed under the MIT License.

#include "../src/pointPreparation.h"

#include <initializer_list>
#include <iostream>
#include <limits>
#include <stdexcept>

namespace
{
void PointReservationIncludesSourceOutputAndIdentity()
{
    constexpr uint64_t bytesPerPoint = 48;
    if (HdSilkPointPreparationPlan(0).bytes != 0 ||
        HdSilkPointPreparationPlan(1).bytes != bytesPerPoint ||
        HdSilkPointPreparationPlan(3).bytes != 3 * bytesPerPoint)
    {
        throw std::runtime_error("The point preparation reservation changed.");
    }

    constexpr uint64_t maximumCount = std::numeric_limits<uint32_t>::max();
    if (HdSilkPointPreparationPlan(maximumCount).bytes != maximumCount * bytesPerPoint)
    {
        throw std::runtime_error("The largest supported point reservation changed.");
    }
    for (const uint64_t count : {maximumCount + 1, std::numeric_limits<uint64_t>::max()})
    {
        bool overflow = false;
        try { (void)HdSilkPointPreparationPlan(count); }
        catch (const std::overflow_error&) { overflow = true; }
        if (!overflow)
        {
            throw std::runtime_error("An unrepresentable point index was not refused.");
        }
    }
}
}

int main()
{
    try
    {
        PointReservationIncludesSourceOutputAndIdentity();
        std::cout << "hdSilk point preparation: reservation contract passed\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
