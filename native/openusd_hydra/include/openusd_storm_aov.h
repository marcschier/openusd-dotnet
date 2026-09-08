// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_STORM_AOV_H
#define OPENUSD_STORM_AOV_H

#include "openusd_hydra.h"

#define OPENUSD_STORM_AOV_VERSION 1u
#define OPENUSD_STORM_AOV_MAX_OUTPUTS 8u
#define OPENUSD_STORM_AOV_MAX_DIMENSION 4096u
#define OPENUSD_STORM_AOV_MAX_PIXELS 1048576u
#define OPENUSD_STORM_AOV_MAX_IDENTITIES 4096u
#define OPENUSD_STORM_AOV_MAX_CONTEXTS 16384u
#define OPENUSD_STORM_AOV_MAX_TEXT_BYTES 1048576u
#define OPENUSD_STORM_AOV_MAX_WORKING_BYTES UINT64_C(134217728)

#define OPENUSD_STORM_AOV_COLOR 1u
#define OPENUSD_STORM_AOV_DEPTH 2u
#define OPENUSD_STORM_AOV_PRIM_ID 3u
#define OPENUSD_STORM_AOV_INSTANCE_ID 4u
#define OPENUSD_STORM_AOV_ELEMENT_ID 5u
#define OPENUSD_STORM_AOV_NEYE 6u
#define OPENUSD_STORM_AOV_NORMAL 7u

#define OPENUSD_STORM_AOV_STATUS_NOT_REQUESTED 0u
#define OPENUSD_STORM_AOV_STATUS_READY 1u
#define OPENUSD_STORM_AOV_STATUS_UNSUPPORTED 2u
#define OPENUSD_STORM_AOV_STATUS_ABSENT 3u

#define OPENUSD_STORM_AOV_FORMAT_NONE 0u
#define OPENUSD_STORM_AOV_FORMAT_FLOAT16_VEC4 1u
#define OPENUSD_STORM_AOV_FORMAT_FLOAT32 2u
#define OPENUSD_STORM_AOV_FORMAT_INT32 3u
#define OPENUSD_STORM_AOV_FORMAT_UNORM8_VEC4 4u

#define OPENUSD_STORM_AOV_ORIGIN_TOP_LEFT 1u
#define OPENUSD_STORM_AOV_DEPTH_NONE 0u
#define OPENUSD_STORM_AOV_DEPTH_OPENGL_WINDOW 1u
#define OPENUSD_STORM_AOV_OUTPUT_RESOLVED_MULTISAMPLE 1u
#define OPENUSD_STORM_AOV_REQUEST_IDENTITIES 1u
#define OPENUSD_STORM_AOV_IDENTITY_RESOLVED 1u
#define OPENUSD_STORM_AOV_IDENTITY_UNRESOLVED 2u
#define OPENUSD_STORM_AOV_BACKGROUND_INDEX UINT32_MAX

typedef struct openusd_storm_aov_owner openusd_storm_aov_owner;

/* Revisions are opaque caller claims, not independently observed USD stage
 * serials. Time and camera select the actual render; applied_camera in the
 * returned view reports the matrices actually used. Unused output slots and
 * identity limits when identities are not requested must be zero. */
typedef struct openusd_storm_aov_request
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t output_count;
    uint32_t flags;
    int32_t width;
    int32_t height;
    uint32_t framebuffer;
    uint32_t revision_flags;
    double time_code;
    uint64_t state_revision;
    uint64_t scene_revision;
    uint32_t max_pixels;
    uint32_t max_unique_identities;
    uint32_t max_instance_contexts;
    uint32_t max_text_bytes;
    uint64_t max_working_bytes;
    uint32_t output_kinds[OPENUSD_STORM_AOV_MAX_OUTPUTS];
    openusd_render_camera camera;
} openusd_storm_aov_request;

typedef struct openusd_storm_aov_output
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t kind;
    uint32_t status;
    uint32_t format;
    uint32_t width;
    uint32_t height;
    uint32_t row_stride_bytes;
    uint32_t origin;
    uint32_t flags;
    uint32_t depth_convention;
    uint32_t reserved;
    uint64_t data_offset;
    uint64_t data_bytes;
} openusd_storm_aov_output;

typedef struct openusd_storm_aov_identity
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t status;
    uint32_t reserved;
    int32_t prim_id;
    int32_t instance_id;
    int32_t instance_index;
    uint32_t prim_path_offset;
    uint32_t prim_path_length;
    uint32_t instancer_path_offset;
    uint32_t instancer_path_length;
    uint32_t context_offset;
    uint32_t context_count;
    uint32_t reserved2;
} openusd_storm_aov_identity;

typedef struct openusd_storm_aov_instance_context
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t path_offset;
    uint32_t path_length;
    int32_t instance_index;
    uint32_t reserved;
} openusd_storm_aov_instance_context;

typedef struct openusd_storm_aov_view
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t output_count;
    uint32_t identity_count;
    uint32_t context_count;
    uint32_t text_byte_count;
    uint32_t width;
    uint32_t height;
    uint32_t identity_status;
    uint32_t revision_flags;
    uint64_t capture_id;
    uint64_t state_revision;
    uint64_t scene_revision;
    double time_code;
    uint64_t owned_bytes;
    uint64_t admitted_working_bytes;
    uint64_t retained_scratch_upper_bound_bytes;
    const openusd_storm_aov_output* outputs;
    const void* pixel_data;
    uint64_t pixel_bytes;
    const openusd_storm_aov_identity* identities;
    const openusd_storm_aov_instance_context* contexts;
    const char* text;
    const uint32_t* identity_indices;
    uint64_t identity_index_count;
    openusd_render_camera applied_camera;
} openusd_storm_aov_view;

#ifdef __cplusplus
extern "C" {
#endif

/* Captures one completed render on the renderer's owner thread/original GL
 * context. Failure initializes *owner to NULL. One live owner per renderer is
 * allowed. Limits bound known output-copy/readback storage, not scene/GPU/RSS
 * memory, opaque SDK decoder internals, or driver wait duration. */
OPENUSD_HYDRA_API openusd_status openusd_storm_aov_capture(
    openusd_storm_renderer* renderer,
    const openusd_storm_aov_request* request,
    openusd_storm_aov_owner** owner,
    openusd_error_buffer* error);

/* Initialize struct_size/version. Failure clears the complete current view.
 * Borrowed pointers remain valid until release, even after renderer teardown.
 * Rows are tight and top-down; scalar pixels use host-native representation.
 * UTF-8 paths are length-delimited. Hydra IDs, decode ordinals and table indices
 * are snapshot-local, not stable authored identity. Context order is preserved.
 * No read/get-view may race release of this owner. */
OPENUSD_HYDRA_API openusd_status openusd_storm_aov_get_view(
    const openusd_storm_aov_owner* owner,
    openusd_storm_aov_view* view,
    openusd_error_buffer* error);

/* CPU-only, NULL-safe and callable on any thread without GL. */
OPENUSD_HYDRA_API void openusd_storm_aov_release(
    openusd_storm_aov_owner* owner) OPENUSD_HYDRA_NOEXCEPT;

#ifdef __cplusplus
}
#endif

#endif
