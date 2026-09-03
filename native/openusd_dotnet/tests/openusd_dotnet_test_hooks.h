// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_DOTNET_TEST_HOOKS_H
#define OPENUSD_DOTNET_TEST_HOOKS_H

#include "openusd_dotnet.h"

#ifdef __cplusplus
extern "C" {
#endif

OPENUSD_DOTNET_API size_t openusd_test_get_live_stage_core_count(void);
OPENUSD_DOTNET_API size_t openusd_test_get_destroyed_stage_core_count(void);

/*
 * Sdr node-definition capacity/append test hooks. current_bytes/additional_bytes/max_bytes are
 * caller-injected, independent of OPENUSD_SDR_NODE_DEFINITION_MAX_STRING_BYTES, so the atomic
 * preflight arithmetic can be proven correct and overflow-safe at small, deterministic sizes.
 */
OPENUSD_DOTNET_API int32_t openusd_test_sdr_has_string_capacity(
    size_t current_bytes,
    size_t additional_bytes,
    size_t max_bytes);

OPENUSD_DOTNET_API int32_t openusd_test_sdr_try_append_two_strings(
    const char* first_value,
    const char* second_value,
    size_t existing_data_bytes,
    size_t max_string_bytes,
    size_t* data_size_after);

#ifdef __cplusplus
}
#endif

#endif
