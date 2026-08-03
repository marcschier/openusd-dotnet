// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_CESIUM_H
#define OPENUSD_CESIUM_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#if defined(OPENUSD_CESIUM_BUILD)
#define OPENUSD_CESIUM_API __declspec(dllexport)
#else
#define OPENUSD_CESIUM_API __declspec(dllimport)
#endif
#else
#define OPENUSD_CESIUM_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef enum openusd_cesium_status
{
    OPENUSD_CESIUM_STATUS_OK = 0,
    OPENUSD_CESIUM_STATUS_INVALID_ARGUMENT = 1,
    OPENUSD_CESIUM_STATUS_NOT_FOUND = 2,
    OPENUSD_CESIUM_STATUS_BUFFER_TOO_SMALL = 3,
    OPENUSD_CESIUM_STATUS_NATIVE_ERROR = 4,
    OPENUSD_CESIUM_STATUS_WRONG_THREAD = 5
} openusd_cesium_status;

typedef struct openusd_cesium_error_buffer
{
    char* data;
    size_t capacity;
    size_t required;
} openusd_cesium_error_buffer;

typedef struct openusd_cesium_tileset openusd_cesium_tileset;
typedef struct openusd_cesium_task openusd_cesium_task;

typedef struct openusd_cesium_vec2d { double x; double y; } openusd_cesium_vec2d;
typedef struct openusd_cesium_vec3d { double x; double y; double z; } openusd_cesium_vec3d;
typedef struct openusd_cesium_matrix4d { double values[16]; } openusd_cesium_matrix4d;

#define OPENUSD_CESIUM_VIEW_STATE_VERSION UINT32_C(1)
typedef struct openusd_cesium_view_state
{
    uint32_t struct_size;
    uint32_t version;
    openusd_cesium_vec3d position_ecef;
    openusd_cesium_vec3d direction_ecef;
    openusd_cesium_vec3d up_ecef;
    double viewport_width;
    double viewport_height;
    double horizontal_fov_radians;
    double vertical_fov_radians;
} openusd_cesium_view_state;

#define OPENUSD_CESIUM_TILE_LOAD_RESULT_VERSION UINT32_C(1)
typedef enum openusd_cesium_tile_load_state
{
    OPENUSD_CESIUM_TILE_LOAD_SUCCESS = 0,
    OPENUSD_CESIUM_TILE_LOAD_FAILED = 1,
    OPENUSD_CESIUM_TILE_LOAD_RETRY_LATER = 2
} openusd_cesium_tile_load_state;

typedef enum openusd_cesium_tile_content_kind
{
    OPENUSD_CESIUM_TILE_CONTENT_UNKNOWN = 0,
    OPENUSD_CESIUM_TILE_CONTENT_EMPTY = 1,
    OPENUSD_CESIUM_TILE_CONTENT_EXTERNAL_TILESET = 2,
    OPENUSD_CESIUM_TILE_CONTENT_GLTF_MODEL = 3
} openusd_cesium_tile_content_kind;

typedef struct openusd_cesium_tile_load_result
{
    uint32_t struct_size;
    uint32_t version;
    openusd_cesium_tile_load_state state;
    openusd_cesium_tile_content_kind content_kind;
    uint32_t gltf_mesh_count;
    uint32_t gltf_node_count;
    uint64_t completed_request_status_code;
    const char* completed_request_url;
} openusd_cesium_tile_load_result;

#define OPENUSD_CESIUM_UPDATE_RESULT_VERSION UINT32_C(1)
typedef struct openusd_cesium_update_result
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t tiles_to_render_count;
    int32_t worker_thread_tile_load_queue_length;
    int32_t main_thread_tile_load_queue_length;
    uint32_t tiles_visited;
    uint32_t culled_tiles_visited;
    uint32_t tiles_culled;
    uint32_t max_depth_visited;
    int32_t frame_number;
    int32_t loaded_tile_count;
    float load_progress;
} openusd_cesium_update_result;

typedef enum openusd_cesium_message_severity
{
    OPENUSD_CESIUM_MESSAGE_INFO = 0,
    OPENUSD_CESIUM_MESSAGE_WARNING = 1,
    OPENUSD_CESIUM_MESSAGE_ERROR = 2
} openusd_cesium_message_severity;

typedef struct openusd_cesium_asset_response
{
    uint32_t struct_size;
    uint16_t status_code;
    const char* content_type;
    const uint8_t* data;
    size_t data_size;
    void (*free_data)(void* user_data, const uint8_t* data, size_t data_size);
    void* user_data;
} openusd_cesium_asset_response;

typedef void* (*openusd_cesium_prepare_load_thread_fn)(
    void* user_data,
    const openusd_cesium_tile_load_result* load_result,
    const openusd_cesium_matrix4d* transform,
    openusd_cesium_error_buffer* error);
typedef void* (*openusd_cesium_prepare_main_thread_fn)(
    void* user_data,
    void* load_thread_resource,
    openusd_cesium_error_buffer* error);
typedef void (*openusd_cesium_free_resources_fn)(
    void* user_data,
    void* load_thread_resource,
    void* main_thread_resource);
typedef void (*openusd_cesium_attach_raster_fn)(
    void* user_data,
    int32_t overlay_texture_coordinate_id,
    void* raster_main_thread_resource,
    const openusd_cesium_vec2d* translation,
    const openusd_cesium_vec2d* scale);
typedef void (*openusd_cesium_detach_raster_fn)(
    void* user_data,
    int32_t overlay_texture_coordinate_id,
    void* raster_main_thread_resource);
typedef openusd_cesium_status (*openusd_cesium_asset_request_fn)(
    void* user_data,
    const char* method,
    const char* url,
    const uint8_t* request_data,
    size_t request_data_size,
    openusd_cesium_asset_response* response,
    openusd_cesium_error_buffer* error);
typedef void (*openusd_cesium_message_fn)(
    void* user_data,
    openusd_cesium_message_severity severity,
    const char* message);
typedef void (*openusd_cesium_start_task_fn)(void* user_data, openusd_cesium_task* task);

#define OPENUSD_CESIUM_RENDERER_CALLBACKS_VERSION UINT32_C(1)
typedef struct openusd_cesium_renderer_callbacks
{
    uint32_t struct_size;
    uint32_t version;
    void* user_data;
    openusd_cesium_prepare_load_thread_fn prepare_in_load_thread;
    openusd_cesium_prepare_main_thread_fn prepare_in_main_thread;
    openusd_cesium_free_resources_fn free_resources;
    openusd_cesium_attach_raster_fn attach_raster_in_main_thread;
    openusd_cesium_detach_raster_fn detach_raster_in_main_thread;
} openusd_cesium_renderer_callbacks;

#define OPENUSD_CESIUM_ASSET_ACCESSOR_VERSION UINT32_C(1)
typedef struct openusd_cesium_asset_accessor
{
    uint32_t struct_size;
    uint32_t version;
    void* user_data;
    openusd_cesium_asset_request_fn request;
} openusd_cesium_asset_accessor;

#define OPENUSD_CESIUM_TASK_PROCESSOR_VERSION UINT32_C(1)
typedef struct openusd_cesium_task_processor
{
    uint32_t struct_size;
    uint32_t version;
    void* user_data;
    openusd_cesium_start_task_fn start_task;
} openusd_cesium_task_processor;

#define OPENUSD_CESIUM_TILESET_OPTIONS_VERSION UINT32_C(1)
typedef struct openusd_cesium_tileset_options
{
    uint32_t struct_size;
    uint32_t version;
    double maximum_screen_space_error;
    int32_t preload_ancestors;
    int32_t preload_siblings;
    int32_t forbid_holes;
    openusd_cesium_message_fn message_callback;
    void* message_user_data;
} openusd_cesium_tileset_options;

/*
 * Creates a tileset from a tileset.json URL. The renderer and asset callback
 * tables must remain valid until openusd_cesium_tileset_release returns. The
 * thread that creates the tileset is its main thread. openusd_cesium_tileset_update_view
 * and openusd_cesium_tileset_release must be called on that same thread.
 * prepare_in_load_thread may be invoked on worker threads. prepare_in_main_thread,
 * free_resources, attach_raster_in_main_thread, and detach_raster_in_main_thread
 * are invoked only from the tileset main thread.
 */
OPENUSD_CESIUM_API openusd_cesium_status openusd_cesium_tileset_create(
    const char* tileset_url,
    const openusd_cesium_asset_accessor* asset_accessor,
    const openusd_cesium_renderer_callbacks* renderer_callbacks,
    const openusd_cesium_task_processor* task_processor,
    const openusd_cesium_tileset_options* options,
    openusd_cesium_tileset** tileset,
    openusd_cesium_error_buffer* error);

OPENUSD_CESIUM_API openusd_cesium_status openusd_cesium_tileset_update_view(
    openusd_cesium_tileset* tileset,
    const openusd_cesium_view_state* view_states,
    size_t view_state_count,
    float delta_time_seconds,
    openusd_cesium_update_result* result,
    openusd_cesium_error_buffer* error);

OPENUSD_CESIUM_API openusd_cesium_status openusd_cesium_tileset_get_message_count(
    const openusd_cesium_tileset* tileset,
    size_t* count,
    openusd_cesium_error_buffer* error);

OPENUSD_CESIUM_API openusd_cesium_status openusd_cesium_tileset_get_message(
    const openusd_cesium_tileset* tileset,
    size_t index,
    openusd_cesium_message_severity* severity,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_cesium_error_buffer* error);

OPENUSD_CESIUM_API openusd_cesium_status openusd_cesium_tileset_release(
    openusd_cesium_tileset* tileset,
    openusd_cesium_error_buffer* error);

OPENUSD_CESIUM_API openusd_cesium_status openusd_cesium_task_execute(openusd_cesium_task* task);
OPENUSD_CESIUM_API void openusd_cesium_task_destroy(openusd_cesium_task* task);

#ifdef __cplusplus
}
#endif

#endif