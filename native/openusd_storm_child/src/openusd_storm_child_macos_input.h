// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_STORM_CHILD_MACOS_INPUT_H
#define OPENUSD_STORM_CHILD_MACOS_INPUT_H

#include <algorithm>
#include <cmath>

/*
 * AppKit precise scrolling is reported in points. Forty points equal one
 * logical Viewer wheel step, and one event is bounded to four steps.
 */
constexpr double OpenUsdStormChildMacScrollPointsPerStep = 40.0;
constexpr double OpenUsdStormChildMacMaximumScrollStepsPerEvent = 4.0;

inline double OpenUsdStormChildNormalizeMacScrollDelta(
    double scrolling_delta_y,
    bool precise,
    bool direction_inverted_from_device) noexcept
{
    if (!std::isfinite(scrolling_delta_y) || scrolling_delta_y == 0)
    {
        return 0;
    }
    const double directed = direction_inverted_from_device
        ? -scrolling_delta_y
        : scrolling_delta_y;
    if (!precise)
    {
        return std::copysign(1.0, directed);
    }
    return std::clamp(
        directed / OpenUsdStormChildMacScrollPointsPerStep,
        -OpenUsdStormChildMacMaximumScrollStepsPerEvent,
        OpenUsdStormChildMacMaximumScrollStepsPerEvent);
}

#endif
