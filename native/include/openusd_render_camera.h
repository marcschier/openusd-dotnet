// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_RENDER_CAMERA_H
#define OPENUSD_RENDER_CAMERA_H

#include <stddef.h>
#include <stdint.h>

#if defined(__cplusplus)
typedef enum openusd_render_camera_mode : uint32_t
#else
typedef enum openusd_render_camera_mode
#endif
{
    /// Uses the renderer's legacy fixed look-at/perspective camera. Matrix
    /// storage is ignored in this mode.
    OPENUSD_RENDER_CAMERA_MODE_AUTO = 0,

    /// Uses the exact row-major view and projection matrices below. All 32
    /// values must be finite.
    OPENUSD_RENDER_CAMERA_MODE_MATRICES = 1
} openusd_render_camera_mode;

/// Shared camera ABI for Storm, hdSilk, and the Storm child. Callers must set
/// struct_size to sizeof(openusd_render_camera); other sizes and modes are
/// rejected. Natural platform layout is part of the ABI and must not be packed.
typedef struct openusd_render_camera
{
    uint32_t struct_size;
    openusd_render_camera_mode mode;
    double view[16];
    double projection[16];
} openusd_render_camera;

#if defined(__cplusplus)
static_assert(sizeof(openusd_render_camera_mode) == 4);
static_assert(alignof(openusd_render_camera) == alignof(double));
static_assert(offsetof(openusd_render_camera, struct_size) == 0);
static_assert(offsetof(openusd_render_camera, mode) == 4);
static_assert(offsetof(openusd_render_camera, view) == 8);
static_assert(offsetof(openusd_render_camera, projection) == 136);
static_assert(sizeof(openusd_render_camera) == 264);
#elif defined(__STDC_VERSION__) && __STDC_VERSION__ >= 201112L
_Static_assert(sizeof(openusd_render_camera_mode) == 4, "camera mode must be 4 bytes");
_Static_assert(offsetof(openusd_render_camera, struct_size) == 0, "invalid struct_size offset");
_Static_assert(offsetof(openusd_render_camera, mode) == 4, "invalid mode offset");
_Static_assert(offsetof(openusd_render_camera, view) == 8, "invalid view offset");
_Static_assert(offsetof(openusd_render_camera, projection) == 136, "invalid projection offset");
_Static_assert(sizeof(openusd_render_camera) == 264, "invalid camera size");
#endif

#endif
