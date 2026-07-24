// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_HYDRA_H
#define OPENUSD_HYDRA_H

#include "openusd_dotnet.h"
#include "openusd_render_camera.h"
#include "openusd_render_pick.h"

#if defined(_WIN32)
#if defined(OPENUSD_HYDRA_BUILD)
#define OPENUSD_HYDRA_API __declspec(dllexport)
#else
#define OPENUSD_HYDRA_API __declspec(dllimport)
#endif
#else
#define OPENUSD_HYDRA_API __attribute__((visibility("default")))
#endif

#if defined(__cplusplus)
#define OPENUSD_HYDRA_NOEXCEPT noexcept
#else
#define OPENUSD_HYDRA_NOEXCEPT
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct openusd_storm_renderer openusd_storm_renderer;

#define OPENUSD_STORM_ABI_VERSION 5u

OPENUSD_HYDRA_API uint32_t openusd_storm_get_abi_version(void)
    OPENUSD_HYDRA_NOEXCEPT;

/// Compatibility path API. Opens a temporary data-stage handle and delegates
/// to openusd_storm_create_from_stage.
OPENUSD_HYDRA_API openusd_status openusd_storm_create(
    const char* plugin_path,
    const char* stage_path,
    openusd_storm_renderer** renderer,
    openusd_error_buffer* error);

/// Creates Storm from the exact stage object and retains its shared stage core.
/// A valid current OpenGL context is required; render and release remain on
/// the creating OpenGL owner thread.
OPENUSD_HYDRA_API openusd_status openusd_storm_create_from_stage(
    const char* plugin_path,
    openusd_stage* stage,
    openusd_storm_renderer** renderer,
    openusd_error_buffer* error);

/// ABI-compatible non-throwing release. It attempts checked destruction and
/// deliberately leaves the renderer allocated if thread/context/access
/// validation fails rather than invoking an unsafe GL destructor.
OPENUSD_HYDRA_API void openusd_storm_release(
    openusd_storm_renderer* renderer) OPENUSD_HYDRA_NOEXCEPT;

/// Destroys Storm on its creation thread while its original GL context is
/// current. Failure leaves the renderer intact and retryable.
OPENUSD_HYDRA_API openusd_status openusd_storm_destroy(
    openusd_storm_renderer* renderer,
    openusd_error_buffer* error);

/// Releases stage/session bookkeeping on the creation thread without invoking
/// the GL engine destructor. No particular GL context is required or inspected.
/// The engine alone is intentionally orphaned for process lifetime; the wrapper,
/// copied stage reference, and retained project stage core are released.
OPENUSD_HYDRA_API openusd_status openusd_storm_abandon(
    openusd_storm_renderer* renderer,
    openusd_error_buffer* error);

/// Renders with the shared automatic or explicit matrix camera.
OPENUSD_HYDRA_API openusd_status openusd_storm_render(
    openusd_storm_renderer* renderer,
    int32_t width,
    int32_t height,
    uint32_t framebuffer,
    double time_code,
    const openusd_render_camera* camera,
    int32_t* converged,
    openusd_error_buffer* error);

/// Renders while binding the exact renderer-neutral state/scene revisions.
OPENUSD_HYDRA_API openusd_status openusd_storm_render_v2(
    openusd_storm_renderer* renderer,
    int32_t width,
    int32_t height,
    uint32_t framebuffer,
    double time_code,
    const openusd_render_camera* camera,
    uint64_t state_revision,
    uint64_t scene_revision,
    uint32_t revision_flags,
    int32_t* converged,
    openusd_error_buffer* error);

/// Resolves one nearest rendered hit into caller-owned UTF-8 buffers.
OPENUSD_HYDRA_API openusd_status openusd_storm_pick(
    openusd_storm_renderer* renderer,
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

/// Applies one packed Storm selection update without retaining caller memory.
OPENUSD_HYDRA_API openusd_status openusd_storm_set_selection(
    openusd_storm_renderer* renderer,
    const openusd_storm_selection_update* update,
    openusd_error_buffer* error);

OPENUSD_HYDRA_API openusd_status openusd_storm_get_renderer_name(
    const openusd_storm_renderer* renderer,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_HYDRA_API size_t openusd_storm_diagnostic_get_live_renderer_count(void)
    OPENUSD_HYDRA_NOEXCEPT;
OPENUSD_HYDRA_API size_t openusd_storm_diagnostic_get_peak_renderer_count(void)
    OPENUSD_HYDRA_NOEXCEPT;
OPENUSD_HYDRA_API size_t openusd_storm_diagnostic_get_abandoned_engine_count(void)
    OPENUSD_HYDRA_NOEXCEPT;
OPENUSD_HYDRA_API void openusd_storm_diagnostic_reset_peak_renderer_count(void)
    OPENUSD_HYDRA_NOEXCEPT;

#ifdef __cplusplus
}
#endif

#endif
