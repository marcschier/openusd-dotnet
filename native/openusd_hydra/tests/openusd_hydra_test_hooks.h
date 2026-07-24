// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_HYDRA_TEST_HOOKS_H
#define OPENUSD_HYDRA_TEST_HOOKS_H

#include "openusd_hydra.h"

#include <cstddef>

extern "C" size_t openusd_hydra_test_get_abandoned_engine_count(void) noexcept;
extern "C" int32_t openusd_hydra_test_get_applied_camera(
    const openusd_storm_renderer* renderer,
    openusd_render_camera* camera) noexcept;

#endif
