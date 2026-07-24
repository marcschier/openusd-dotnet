// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_STORM_CHILD_H
#define OPENUSD_STORM_CHILD_H

#include "openusd_dotnet.h"
#include "openusd_render_camera.h"
#include "openusd_render_pick.h"

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#if defined(OPENUSD_STORM_CHILD_BUILD)
#define OPENUSD_STORM_CHILD_API __declspec(dllexport)
#else
#define OPENUSD_STORM_CHILD_API __declspec(dllimport)
#endif
#else
#define OPENUSD_STORM_CHILD_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct openusd_storm_child openusd_storm_child;

#define OPENUSD_STORM_CHILD_ABI_VERSION 7u

#define OPENUSD_STORM_CHILD_NAVIGATION_INPUT_VERSION 1u

#define OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE 0u
#define OPENUSD_STORM_CHILD_POINTER_BUTTON_LEFT 0x1u
#define OPENUSD_STORM_CHILD_POINTER_BUTTON_MIDDLE 0x2u
#define OPENUSD_STORM_CHILD_POINTER_BUTTON_RIGHT 0x4u

#define OPENUSD_STORM_CHILD_MODIFIER_NONE 0u
#define OPENUSD_STORM_CHILD_MODIFIER_ALT 0x1u
#define OPENUSD_STORM_CHILD_MODIFIER_SHIFT 0x2u
#define OPENUSD_STORM_CHILD_MODIFIER_CONTROL 0x4u
#define OPENUSD_STORM_CHILD_MODIFIER_META 0x8u

#define OPENUSD_STORM_CHILD_NAVIGATION_STATE_NONE 0u
#define OPENUSD_STORM_CHILD_NAVIGATION_STATE_FOCUSED 0x1u
#define OPENUSD_STORM_CHILD_NAVIGATION_STATE_INSIDE 0x2u

#define OPENUSD_STORM_CHILD_CAPTURE_DRAW_TEST_PATTERN 0x1u
#define OPENUSD_STORM_CHILD_CAPTURE_READ_PRESERVED_TEXTURE 0xffffffffu

typedef struct openusd_storm_child_diagnostics
{
    uint64_t frame_count;
    uint64_t pixel_signature;
    uint64_t pixel_sample_count;
    uint64_t focus_count;
    uint64_t pointer_count;
    uint64_t wheel_count;
    uint64_t key_count;
    uint64_t context_generation;
    uint64_t coalesced_request_count;
    uint64_t cancelled_command_count;
    uint64_t teardown_fallback_count;
    uint64_t latest_requested_revision;
    /* FNV-1a over mode and matrix doubles (matrices mode only). */
    uint64_t latest_requested_camera_signature;
    uint64_t latest_rendered_camera_signature;
    uint32_t render_thread_id;
    uint32_t creator_thread_id;
    uint32_t pending_command_count;
    uint32_t peak_pending_command_count;
    int32_t gl_major;
    int32_t gl_minor;
    int32_t compatibility_profile;
    int32_t width;
    int32_t height;
    uint32_t dpi;
    int32_t visible;
    int32_t focused;
    int32_t converged;
} openusd_storm_child_diagnostics;

typedef struct openusd_storm_child_framebuffer_capture
{
    uint64_t frame_count;
    uint64_t pixel_hash;
    uint64_t pixel_count;
    uint64_t non_background_pixel_count;
    int32_t width;
    int32_t height;
    uint32_t dpi;
    uint32_t background_rgba;
    uint32_t average_rgba;
    uint32_t minimum_rgba;
    uint32_t maximum_rgba;
    uint32_t read_buffer;
} openusd_storm_child_framebuffer_capture;

/*
 * Pointer-free, versioned latest-state navigation input. Coordinates are
 * physical child-client pixels with a top-left origin. cumulative_wheel_delta
 * uses platform-normalized logical wheel steps where +1 is one upward detent.
 * On macOS, non-precise events normalize to +/-1; precise deltas use 40 points
 * per step, are bounded to +/-4 steps per event, and undo device inversion.
 */
typedef struct openusd_storm_child_navigation_input
{
    uint32_t struct_size;
    uint32_t version;
    uint64_t sequence;
    int32_t pointer_x;
    int32_t pointer_y;
    uint32_t buttons;
    uint32_t modifiers;
    double cumulative_wheel_delta;
    uint64_t frame_selected_press_count;
    uint64_t reset_automatic_press_count;
    uint64_t toggle_projection_press_count;
    uint32_t state;
    uint32_t reserved;
} openusd_storm_child_navigation_input;

#if defined(__cplusplus)
static_assert(sizeof(openusd_storm_child_navigation_input) == 72);
static_assert(offsetof(openusd_storm_child_navigation_input, sequence) == 8);
static_assert(
    offsetof(openusd_storm_child_navigation_input, cumulative_wheel_delta) == 32);
static_assert(
    offsetof(openusd_storm_child_navigation_input, frame_selected_press_count) == 40);
static_assert(offsetof(openusd_storm_child_navigation_input, state) == 64);
#endif

OPENUSD_STORM_CHILD_API uint32_t openusd_storm_child_get_abi_version(void);

#if defined(__linux__)
/*
 * Installs the process-lifetime X11 error dispatcher. Call this exactly after
 * XInitThreads succeeds and before any other Xlib call or concurrent platform
 * initialization. The call is idempotent. A create attempted before this call
 * permanently marks initialization as too late for the process.
 */
OPENUSD_STORM_CHILD_API openusd_status
openusd_storm_child_initialize_linux(openusd_error_buffer* error);
#endif

/*
 * Linux callers must successfully call XInitThreads before any Xlib call,
 * immediately call openusd_storm_child_initialize_linux, and only then create
 * parent_window or initialize a platform toolkit such as Avalonia. Creation
 * fails if the Linux dispatcher was not initialized first.
 */
OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_create(
    void* parent_window,
    const char* plugin_path,
    openusd_stage* stage,
    int32_t width,
    int32_t height,
    uint32_t dpi,
    openusd_storm_child** child,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_destroy(
    openusd_storm_child* child,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_get_window(
    openusd_storm_child* child,
    void** window,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_get_renderer_name(
    openusd_storm_child* child,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_render(
    openusd_storm_child* child,
    double time_code,
    const openusd_render_camera* camera,
    uint64_t* frame_count,
    int32_t* converged,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_render_v2(
    openusd_storm_child* child,
    double time_code,
    const openusd_render_camera* camera,
    uint64_t state_revision,
    uint64_t scene_revision,
    uint32_t revision_flags,
    uint64_t* frame_count,
    int32_t* converged,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_request_frame(
    openusd_storm_child* child,
    double time_code,
    const openusd_render_camera* camera,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_request_frame_v2(
    openusd_storm_child* child,
    double time_code,
    const openusd_render_camera* camera,
    uint64_t revision,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_request_frame_v3(
    openusd_storm_child* child,
    double time_code,
    const openusd_render_camera* camera,
    uint64_t state_revision,
    uint64_t scene_revision,
    uint32_t revision_flags,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_pick(
    openusd_storm_child* child,
    const openusd_render_pick_request* request,
    openusd_render_pick_result* result,
    char* prim_path_buffer,
    uint32_t prim_path_capacity,
    char* instancer_path_buffer,
    uint32_t instancer_path_capacity,
    openusd_render_pick_instance_context* instance_context,
    uint32_t instance_context_capacity,
    char* instance_context_paths_buffer,
    uint32_t instance_context_paths_capacity,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_set_selection(
    openusd_storm_child* child,
    const openusd_storm_selection_update* update,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_resize(
    openusd_storm_child* child,
    int32_t width,
    int32_t height,
    uint32_t dpi,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_set_visible(
    openusd_storm_child* child,
    int32_t visible,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_focus(
    openusd_storm_child* child,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_simulate_context_loss(
    openusd_storm_child* child,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_get_diagnostics(
    openusd_storm_child* child,
    openusd_storm_child_diagnostics* diagnostics,
    openusd_error_buffer* error);

/*
 * The caller initializes struct_size and version. On every failure for a
 * non-null output, the complete output is reset to zero.
 */
OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_get_navigation_input(
    openusd_storm_child* child,
    openusd_storm_child_navigation_input* input,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API openusd_status openusd_storm_child_capture_framebuffer(
    openusd_storm_child* child,
    uint32_t background_rgba,
    uint8_t tolerance,
    uint32_t flags,
    uint8_t* rgba_buffer,
    size_t rgba_capacity,
    size_t* rgba_required,
    openusd_storm_child_framebuffer_capture* capture,
    openusd_error_buffer* error);

OPENUSD_STORM_CHILD_API size_t openusd_storm_child_diagnostic_get_live_count(void);
OPENUSD_STORM_CHILD_API size_t openusd_storm_child_diagnostic_get_peak_count(void);
OPENUSD_STORM_CHILD_API void openusd_storm_child_diagnostic_reset_peak_count(void);

#ifdef __cplusplus
}
#endif

#endif
