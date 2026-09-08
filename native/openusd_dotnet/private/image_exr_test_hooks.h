// Copyright (c) marcschier. Licensed under the MIT License.
#ifndef OPENUSD_IMAGE_EXR_TEST_HOOKS_H
#define OPENUSD_IMAGE_EXR_TEST_HOOKS_H

#include "openusd_image_exr.h"

/* Separate probe DLL only; never defined in the packaged data library. */
#define EXR_TEST_NONE 0u
#define EXR_TEST_WRITE_ONCE 1u
#define EXR_TEST_WRITE_PERSISTENT 2u
#define EXR_TEST_SEEK_ONCE 3u
#define EXR_TEST_SEEK_PERSISTENT 4u
#define EXR_TEST_SHORT_WRITE_ONCE 5u
#define EXR_TEST_NATIVE_ONCE 6u
#define EXR_TEST_ALLOC_ONCE 7u

typedef struct openusd_image_exr_test_stats_v1
{
    uint64_t writes;
    uint64_t seeks;
    uint64_t backwards_seeks;
    uint64_t fault_triggers;
    uint64_t sticky_blocks;
    uint64_t codec_constructions;
} openusd_image_exr_test_stats_v1;

#ifdef __cplusplus
extern "C" {
#endif
OPENUSD_DOTNET_API void OPENUSD_IMAGE_ENCODE_CALL openusd_private_image_exr_test_configure_v1(
    uint32_t kind, uint32_t minimum_rows, uint64_t minimum_extent);
OPENUSD_DOTNET_API void OPENUSD_IMAGE_ENCODE_CALL openusd_private_image_exr_test_stats_v1(
    openusd_image_exr_test_stats_v1* stats);
#ifdef __cplusplus
}
#endif
#endif
