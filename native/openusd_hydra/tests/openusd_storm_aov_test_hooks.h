// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_STORM_AOV_TEST_HOOKS_H
#define OPENUSD_STORM_AOV_TEST_HOOKS_H

#include "openusd_storm_aov_candidate.h"

#define OPENUSD_STORM_AOV_TEST_STATISTICS_VERSION 1u
#define OPENUSD_STORM_AOV_TEST_FAIL_NONE 0u
#define OPENUSD_STORM_AOV_TEST_FAIL_AFTER_MAP 1u

typedef struct openusd_storm_aov_test_statistics
{
    uint32_t struct_size;
    uint32_t version;
    uint64_t owner_allocations;
    uint64_t live_owners;
    uint64_t map_calls;
    uint64_t unmap_calls;
    uint64_t decode_calls;
} openusd_storm_aov_test_statistics;

#ifdef __cplusplus
extern "C" {
#endif

OPENUSD_HYDRA_API openusd_status openusd_storm_aov_candidate_test_get_statistics(
    openusd_storm_aov_test_statistics* statistics,
    openusd_error_buffer* error);

OPENUSD_HYDRA_API openusd_status openusd_storm_aov_candidate_test_set_failpoint(
    uint32_t failpoint,
    openusd_error_buffer* error);

#ifdef __cplusplus
}
#endif

#endif
