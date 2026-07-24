// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_HDSILK_TEST_HOOKS_H
#define OPENUSD_HDSILK_TEST_HOOKS_H

#include "openusd_hdsilk.h"

#ifdef __cplusplus
extern "C" {
#endif

OPENUSD_HDSILK_API int32_t
openusd_hdsilk_test_external_delegate_does_not_publish(void);

OPENUSD_HDSILK_API size_t
openusd_hdsilk_test_get_session_in_flight(openusd_silk_session* session);

#ifdef __cplusplus
}
#endif

#endif
