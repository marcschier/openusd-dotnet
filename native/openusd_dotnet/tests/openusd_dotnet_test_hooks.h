// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_DOTNET_TEST_HOOKS_H
#define OPENUSD_DOTNET_TEST_HOOKS_H

#include "openusd_dotnet.h"

#ifdef __cplusplus
extern "C" {
#endif

OPENUSD_DOTNET_API size_t openusd_test_get_live_stage_core_count(void);
OPENUSD_DOTNET_API size_t openusd_test_get_destroyed_stage_core_count(void);

#ifdef __cplusplus
}
#endif

#endif
