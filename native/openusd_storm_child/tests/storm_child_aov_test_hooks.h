// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_STORM_CHILD_AOV_TEST_HOOKS_H
#define OPENUSD_STORM_CHILD_AOV_TEST_HOOKS_H

#include <cstddef>

enum class OpenUsdStormChildAovTestPhase
{
    BeforeCompanionRender,
    BeforeCompanionReadback,
    BeforeRgbaAllocation
};

void OpenUsdStormChildAovTestHook(
    OpenUsdStormChildAovTestPhase phase, std::size_t rgba_bytes = 0);

#endif
