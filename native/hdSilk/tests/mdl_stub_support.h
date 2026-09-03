// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef HDSILK_MDL_STUB_SUPPORT_H
#define HDSILK_MDL_STUB_SUPPORT_H

#include <stdint.h>

#if defined(_WIN32)
#if defined(HDSILK_MDL_STUB_SUPPORT_BUILD)
#define HDSILK_MDL_STUB_SUPPORT_API __declspec(dllexport)
#else
#define HDSILK_MDL_STUB_SUPPORT_API __declspec(dllimport)
#endif
#else
#define HDSILK_MDL_STUB_SUPPORT_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

HDSILK_MDL_STUB_SUPPORT_API uint32_t HdSilkMdlStubSupportValue(void);

#ifdef __cplusplus
}
#endif

#endif
