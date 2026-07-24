// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_RENDER_PICK_H
#define OPENUSD_RENDER_PICK_H

#include "openusd_render_camera.h"

#include <stddef.h>
#include <stdint.h>

#define OPENUSD_RENDER_PICK_REQUEST_VERSION 1u
#define OPENUSD_RENDER_PICK_RESULT_VERSION 1u
#define OPENUSD_RENDER_PICK_INSTANCE_CONTEXT_VERSION 1u
#define OPENUSD_STORM_SELECTION_UPDATE_VERSION 1u

#define OPENUSD_RENDER_PICK_TARGET_PRIMITIVE 0u
#define OPENUSD_RENDER_PICK_TARGET_FACE 1u
#define OPENUSD_RENDER_PICK_TARGET_EDGE 2u
#define OPENUSD_RENDER_PICK_TARGET_POINT 3u

#define OPENUSD_RENDER_PICK_RESOLVE_NEAREST_TO_CENTER 0u

#define OPENUSD_RENDER_PICK_REQUEST_HAS_SCENE_REVISION 0x1u
#define OPENUSD_RENDER_PICK_REQUEST_CULL_BACK_FACES 0x2u

#define OPENUSD_RENDER_PICK_STATUS_INVALID 0u
#define OPENUSD_RENDER_PICK_STATUS_MISS 1u
#define OPENUSD_RENDER_PICK_STATUS_HIT 2u
#define OPENUSD_RENDER_PICK_STATUS_STALE 3u
#define OPENUSD_RENDER_PICK_STATUS_UNSUPPORTED 4u
#define OPENUSD_RENDER_PICK_STATUS_CANCELLED 5u
#define OPENUSD_RENDER_PICK_STATUS_CONTEXT_LOST 6u
#define OPENUSD_RENDER_PICK_STATUS_ERROR 7u

#define OPENUSD_RENDER_PICK_RESULT_HAS_SCENE_REVISION 0x1u
#define OPENUSD_RENDER_PICK_RESULT_HAS_INSTANCE 0x2u
#define OPENUSD_RENDER_PICK_RESULT_HAS_ELEMENT 0x4u
#define OPENUSD_RENDER_PICK_RESULT_HAS_INSTANCE_CONTEXT 0x8u
#define OPENUSD_RENDER_PICK_RESULT_STALE_STATE_REVISION 0x100u
#define OPENUSD_RENDER_PICK_RESULT_STALE_SCENE_REVISION 0x200u
#define OPENUSD_RENDER_PICK_RESULT_STALE_CAMERA 0x400u
#define OPENUSD_RENDER_PICK_RESULT_STALE_VIEWPORT 0x800u
#define OPENUSD_RENDER_PICK_RESULT_STALE_TIME 0x1000u
#define OPENUSD_RENDER_PICK_RESULT_STALE_CONTEXT_GENERATION 0x2000u
#define OPENUSD_RENDER_PICK_RESULT_STALE_BACKEND_STATE 0x4000u
#define OPENUSD_RENDER_PICK_RESULT_STALE_MASK 0x7f00u

#define OPENUSD_STORM_RENDER_HAS_SCENE_REVISION 0x1u
#define OPENUSD_STORM_SELECTION_ITEM_HAS_INSTANCE_INDEX 0x1u

/*
 * Versioned nearest-hit request. Pixel coordinates and viewport dimensions are
 * physical pixels with a top-left origin. Width and height are currently
 * required to be exactly one. The camera, time, revisions, viewport, and child
 * context generation bind the request to the exact rendered state.
 */
typedef struct openusd_render_pick_request
{
    uint32_t struct_size;
    uint32_t version;
    int32_t x;
    int32_t y;
    int32_t width;
    int32_t height;
    int32_t viewport_width;
    int32_t viewport_height;
    uint32_t target;
    uint32_t resolve_mode;
    uint32_t flags;
    uint32_t reserved;
    double time_code;
    uint64_t state_revision;
    uint64_t scene_revision;
    uint64_t context_generation;
    openusd_render_camera camera;
} openusd_render_pick_request;

/*
 * Versioned nearest-hit result. All strings are copied into caller-owned UTF-8
 * buffers supplied to the pick function. Required string sizes include the
 * trailing NUL. No native pointer or native object lifetime crosses the ABI.
 */
typedef struct openusd_render_pick_result
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t status;
    uint32_t flags;
    uint64_t state_revision;
    uint64_t scene_revision;
    uint64_t context_generation;
    uint64_t camera_signature;
    double time_code;
    double world_point[3];
    double world_normal[3];
    double normalized_depth;
    int32_t instance_index;
    int32_t element_index;
    uint32_t instance_context_count;
    uint32_t prim_path_required;
    uint32_t instancer_path_required;
    uint32_t instance_context_paths_required;
} openusd_render_pick_result;

/*
 * One truthfully reported nested instancer-context entry. path_offset and
 * path_length address a NUL-terminated UTF-8 path in the caller-owned context
 * path buffer. path_length excludes the terminator.
 */
typedef struct openusd_render_pick_instance_context
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t path_offset;
    uint32_t path_length;
    int32_t instance_index;
    uint32_t reserved;
} openusd_render_pick_instance_context;

/*
 * One item in a packed selection update. path_offset/path_length address UTF-8
 * bytes in openusd_storm_selection_update.path_bytes. Paths are not
 * NUL-terminated. Element selection is intentionally not part of the Storm
 * highlighting contract.
 */
typedef struct openusd_storm_selection_item
{
    uint32_t path_offset;
    uint32_t path_length;
    int32_t instance_index;
    uint32_t flags;
} openusd_storm_selection_item;

/*
 * Caller-owned packed selection update. Native code copies or consumes every
 * byte synchronously and never retains either pointer.
 */
typedef struct openusd_storm_selection_update
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t item_count;
    uint32_t flags;
    float color[4];
    const openusd_storm_selection_item* items;
    const char* path_bytes;
    uint32_t path_bytes_size;
    uint32_t reserved;
} openusd_storm_selection_update;

#if defined(__cplusplus)
static_assert(sizeof(openusd_render_pick_request) == 344);
static_assert(offsetof(openusd_render_pick_request, time_code) == 48);
static_assert(offsetof(openusd_render_pick_request, camera) == 80);
static_assert(sizeof(openusd_render_pick_result) == 136);
static_assert(offsetof(openusd_render_pick_result, world_point) == 56);
static_assert(offsetof(openusd_render_pick_result, normalized_depth) == 104);
static_assert(sizeof(openusd_render_pick_instance_context) == 24);
static_assert(sizeof(openusd_storm_selection_item) == 16);
#if UINTPTR_MAX == UINT64_MAX
static_assert(sizeof(openusd_storm_selection_update) == 56);
static_assert(offsetof(openusd_storm_selection_update, items) == 32);
#endif
#endif

#endif
