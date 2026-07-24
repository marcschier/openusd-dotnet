// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_DOTNET_H
#define OPENUSD_DOTNET_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#if defined(OPENUSD_DOTNET_BUILD)
#define OPENUSD_DOTNET_API __declspec(dllexport)
#else
#define OPENUSD_DOTNET_API __declspec(dllimport)
#endif
#else
#define OPENUSD_DOTNET_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef enum openusd_status
{
    OPENUSD_STATUS_OK = 0,
    OPENUSD_STATUS_INVALID_ARGUMENT = 1,
    OPENUSD_STATUS_NOT_FOUND = 2,
    OPENUSD_STATUS_BUFFER_TOO_SMALL = 3,
    OPENUSD_STATUS_NATIVE_ERROR = 4,
    OPENUSD_STATUS_WRONG_THREAD = 5
} openusd_status;

typedef struct openusd_error_buffer
{
    char* data;
    size_t capacity;
    size_t required;
} openusd_error_buffer;

typedef struct openusd_stage openusd_stage;
typedef struct openusd_stage_access openusd_stage_access;
typedef struct openusd_layer openusd_layer;
typedef struct openusd_string_list openusd_string_list;
typedef struct openusd_payload_arc_list openusd_payload_arc_list;

typedef struct openusd_string_list_view
{
    uint32_t struct_size;
    const char* data;
    size_t data_size;
    const size_t* offsets;
    size_t offsets_size;
    size_t count;
} openusd_string_list_view;

/*
 * A native-owned packed list of composed direct payload-list entries in deterministic
 * expanded-prim-index and list-op order. The offset table contains exactly three entries per arc:
 * authored asset path, authored target prim path (empty when omitted), and the
 * identifier of the layer that introduces the arc.
 */
#define OPENUSD_PAYLOAD_ARC_LIST_VIEW_VERSION 1
typedef struct openusd_payload_arc_list_view
{
    uint32_t struct_size;
    uint32_t version;
    const char* data;
    size_t data_size;
    const size_t* offsets;
    size_t offsets_size;
    size_t count;
} openusd_payload_arc_list_view;

#define OPENUSD_CAPABILITY_STRING_LIST_V2 (UINT64_C(1) << 0)
#define OPENUSD_CAPABILITY_GUARDED_STATUS_EXPORTS (UINT64_C(1) << 1)
#define OPENUSD_CAPABILITY_SHADE_CONNECTED_SOURCES (UINT64_C(1) << 2)
#define OPENUSD_CAPABILITY_SHARED_STAGE_ACCESS (UINT64_C(1) << 3)
#define OPENUSD_CAPABILITY_WORLD_BOUNDS_QUERY (UINT64_C(1) << 4)
#define OPENUSD_CAPABILITY_VARIANT_SET_NAMES (UINT64_C(1) << 5)
#define OPENUSD_CAPABILITY_COMPOSED_DIRECT_PAYLOAD_ARCS (UINT64_C(1) << 6)
#define OPENUSD_CAPABILITY_WORLD_TRANSFORM_QUERY (UINT64_C(1) << 7)
#define OPENUSD_CAPABILITY_CAMERA_STATE_QUERY (UINT64_C(1) << 8)

typedef struct openusd_vec3f
{
    float x;
    float y;
    float z;
} openusd_vec3f;

typedef struct openusd_vec2f
{
    float x;
    float y;
} openusd_vec2f;

typedef struct openusd_quatf
{
    float real;
    float x;
    float y;
    float z;
} openusd_quatf;

typedef struct openusd_matrix4d
{
    double values[16];
} openusd_matrix4d;

typedef struct openusd_extent3f
{
    openusd_vec3f minimum;
    openusd_vec3f maximum;
} openusd_extent3f;

#define OPENUSD_BOUNDS3D_VERSION UINT32_C(1)

typedef struct openusd_bounds3d
{
    uint32_t struct_size;
    uint32_t version;
    int32_t is_valid;
    int32_t is_empty;
    double minimum[3];
    double maximum[3];
} openusd_bounds3d;

typedef enum openusd_geom_schema_kind
{
    OPENUSD_GEOM_SCHEMA_IMAGEABLE = 0,
    OPENUSD_GEOM_SCHEMA_XFORMABLE = 1,
    OPENUSD_GEOM_SCHEMA_XFORM = 2,
    OPENUSD_GEOM_SCHEMA_MESH = 3,
    OPENUSD_GEOM_SCHEMA_CAMERA = 4
} openusd_geom_schema_kind;

typedef enum openusd_geom_visibility
{
    OPENUSD_GEOM_VISIBILITY_INHERITED = 0,
    OPENUSD_GEOM_VISIBILITY_INVISIBLE = 1
} openusd_geom_visibility;

typedef enum openusd_geom_purpose
{
    OPENUSD_GEOM_PURPOSE_DEFAULT = 0,
    OPENUSD_GEOM_PURPOSE_RENDER = 1,
    OPENUSD_GEOM_PURPOSE_PROXY = 2,
    OPENUSD_GEOM_PURPOSE_GUIDE = 3
} openusd_geom_purpose;

#define OPENUSD_GEOM_PURPOSE_MASK_DEFAULT (UINT32_C(1) << 0)
#define OPENUSD_GEOM_PURPOSE_MASK_PROXY (UINT32_C(1) << 1)
#define OPENUSD_GEOM_PURPOSE_MASK_RENDER (UINT32_C(1) << 2)
#define OPENUSD_GEOM_PURPOSE_MASK_GUIDE (UINT32_C(1) << 3)
#define OPENUSD_GEOM_PURPOSE_MASK_ALL \
    (OPENUSD_GEOM_PURPOSE_MASK_DEFAULT | OPENUSD_GEOM_PURPOSE_MASK_PROXY | \
     OPENUSD_GEOM_PURPOSE_MASK_RENDER | OPENUSD_GEOM_PURPOSE_MASK_GUIDE)

typedef enum openusd_geom_interpolation
{
    OPENUSD_GEOM_INTERPOLATION_CONSTANT = 0,
    OPENUSD_GEOM_INTERPOLATION_UNIFORM = 1,
    OPENUSD_GEOM_INTERPOLATION_VARYING = 2,
    OPENUSD_GEOM_INTERPOLATION_VERTEX = 3,
    OPENUSD_GEOM_INTERPOLATION_FACE_VARYING = 4
} openusd_geom_interpolation;

typedef enum openusd_geom_subdivision_scheme
{
    OPENUSD_GEOM_SUBDIVISION_NONE = 0,
    OPENUSD_GEOM_SUBDIVISION_CATMULL_CLARK = 1,
    OPENUSD_GEOM_SUBDIVISION_LOOP = 2,
    OPENUSD_GEOM_SUBDIVISION_BILINEAR = 3
} openusd_geom_subdivision_scheme;

typedef enum openusd_geom_orientation
{
    OPENUSD_GEOM_ORIENTATION_RIGHT_HANDED = 0,
    OPENUSD_GEOM_ORIENTATION_LEFT_HANDED = 1
} openusd_geom_orientation;

typedef enum openusd_geom_camera_projection
{
    OPENUSD_GEOM_CAMERA_PERSPECTIVE = 0,
    OPENUSD_GEOM_CAMERA_ORTHOGRAPHIC = 1
} openusd_geom_camera_projection;

typedef enum openusd_geom_camera_float_property
{
    OPENUSD_GEOM_CAMERA_FOCAL_LENGTH = 0,
    OPENUSD_GEOM_CAMERA_HORIZONTAL_APERTURE = 1,
    OPENUSD_GEOM_CAMERA_VERTICAL_APERTURE = 2
} openusd_geom_camera_float_property;

#define OPENUSD_GEOM_CAMERA_STATE_VERSION UINT32_C(1)

/*
 * A detached, pointer-free camera snapshot. The window is the exact GfFrustum
 * window at its reference plane, including authored aperture offsets.
 * Perspective focal length is positive; orthographic focal length may be zero.
 */
typedef struct openusd_geom_camera_state
{
    uint32_t struct_size;
    uint32_t version;
    int32_t is_valid;
    int32_t projection;
    double window_left;
    double window_right;
    double window_bottom;
    double window_top;
    double clipping_near;
    double clipping_far;
    double focal_length;
    double horizontal_aperture;
    double vertical_aperture;
    double horizontal_aperture_offset;
    double vertical_aperture_offset;
    double focus_distance;
    double f_stop;
} openusd_geom_camera_state;

typedef enum openusd_shade_value_type
{
    OPENUSD_SHADE_VALUE_INVALID = 0,
    OPENUSD_SHADE_VALUE_FLOAT = 1,
    OPENUSD_SHADE_VALUE_COLOR3F = 2,
    OPENUSD_SHADE_VALUE_VECTOR3F = 3,
    OPENUSD_SHADE_VALUE_NORMAL3F = 4,
    OPENUSD_SHADE_VALUE_TOKEN = 5,
    OPENUSD_SHADE_VALUE_STRING = 6,
    OPENUSD_SHADE_VALUE_ASSET = 7,
    OPENUSD_SHADE_VALUE_FLOAT3 = 8
} openusd_shade_value_type;

typedef enum openusd_shade_attribute_type
{
    OPENUSD_SHADE_ATTRIBUTE_INVALID = 0,
    OPENUSD_SHADE_ATTRIBUTE_INPUT = 1,
    OPENUSD_SHADE_ATTRIBUTE_OUTPUT = 2
} openusd_shade_attribute_type;

typedef enum openusd_lux_schema_kind
{
    OPENUSD_LUX_SCHEMA_DISTANT_LIGHT = 0,
    OPENUSD_LUX_SCHEMA_SPHERE_LIGHT = 1,
    OPENUSD_LUX_SCHEMA_RECT_LIGHT = 2,
    OPENUSD_LUX_SCHEMA_DISK_LIGHT = 3,
    OPENUSD_LUX_SCHEMA_DOME_LIGHT = 4,
    OPENUSD_LUX_SCHEMA_CYLINDER_LIGHT = 5
} openusd_lux_schema_kind;

typedef enum openusd_lux_float_property
{
    OPENUSD_LUX_FLOAT_INTENSITY = 0,
    OPENUSD_LUX_FLOAT_EXPOSURE = 1,
    OPENUSD_LUX_FLOAT_DIFFUSE = 2,
    OPENUSD_LUX_FLOAT_SPECULAR = 3,
    OPENUSD_LUX_FLOAT_COLOR_TEMPERATURE = 4
} openusd_lux_float_property;

typedef enum openusd_lux_bool_property
{
    OPENUSD_LUX_BOOL_ENABLE_COLOR_TEMPERATURE = 0,
    OPENUSD_LUX_BOOL_NORMALIZE = 1
} openusd_lux_bool_property;

typedef enum openusd_lux_shape_property
{
    OPENUSD_LUX_SHAPE_ANGLE = 0,
    OPENUSD_LUX_SHAPE_RADIUS = 1,
    OPENUSD_LUX_SHAPE_WIDTH = 2,
    OPENUSD_LUX_SHAPE_HEIGHT = 3,
    OPENUSD_LUX_SHAPE_LENGTH = 4
} openusd_lux_shape_property;

typedef enum openusd_lux_asset_property
{
    OPENUSD_LUX_ASSET_TEXTURE_FILE = 0
} openusd_lux_asset_property;

typedef enum openusd_lux_shaping_property
{
    OPENUSD_LUX_SHAPING_FOCUS = 0,
    OPENUSD_LUX_SHAPING_CONE_ANGLE = 1,
    OPENUSD_LUX_SHAPING_CONE_SOFTNESS = 2
} openusd_lux_shaping_property;

typedef enum openusd_skel_schema_kind
{
    OPENUSD_SKEL_SCHEMA_ROOT = 0,
    OPENUSD_SKEL_SCHEMA_SKELETON = 1,
    OPENUSD_SKEL_SCHEMA_ANIMATION = 2
} openusd_skel_schema_kind;

typedef enum openusd_skel_matrix_property
{
    OPENUSD_SKEL_MATRIX_BIND_TRANSFORMS = 0,
    OPENUSD_SKEL_MATRIX_REST_TRANSFORMS = 1
} openusd_skel_matrix_property;

typedef enum openusd_skel_animation_vec3_property
{
    OPENUSD_SKEL_ANIMATION_TRANSLATIONS = 0,
    OPENUSD_SKEL_ANIMATION_SCALES = 1
} openusd_skel_animation_vec3_property;

typedef enum openusd_skel_binding_relationship
{
    OPENUSD_SKEL_BINDING_SKELETON = 0,
    OPENUSD_SKEL_BINDING_ANIMATION_SOURCE = 1
} openusd_skel_binding_relationship;

typedef enum openusd_skel_interpolation
{
    OPENUSD_SKEL_INTERPOLATION_CONSTANT = 0,
    OPENUSD_SKEL_INTERPOLATION_VERTEX = 1
} openusd_skel_interpolation;

typedef enum openusd_metadata_kind
{
    OPENUSD_METADATA_KIND_STRING = 0,
    OPENUSD_METADATA_KIND_BOOL = 1,
    OPENUSD_METADATA_KIND_INT64 = 2,
    OPENUSD_METADATA_KIND_DOUBLE = 3
} openusd_metadata_kind;

typedef struct openusd_metadata_value
{
    uint32_t struct_size;
    int32_t kind;
    int32_t bool_value;
    int64_t int64_value;
    double double_value;
} openusd_metadata_value;

typedef enum openusd_scalar_kind
{
    OPENUSD_SCALAR_KIND_BOOL = 0,
    OPENUSD_SCALAR_KIND_INT64 = 1,
    OPENUSD_SCALAR_KIND_DOUBLE = 2,
    OPENUSD_SCALAR_KIND_STRING = 3,
    OPENUSD_SCALAR_KIND_TOKEN = 4,
    OPENUSD_SCALAR_KIND_VEC3F = 5,
    OPENUSD_SCALAR_KIND_COLOR3F = 6,
    OPENUSD_SCALAR_KIND_MATRIX4D = 7
} openusd_scalar_kind;

typedef struct openusd_scalar_value
{
    uint32_t struct_size;
    int32_t kind;
    int32_t bool_value;
    int64_t int64_value;
    double double_value;
    openusd_vec3f vec3f_value;
    openusd_matrix4d matrix4d_value;
} openusd_scalar_value;

OPENUSD_DOTNET_API uint32_t openusd_get_abi_version(void);

OPENUSD_DOTNET_API uint64_t openusd_get_capabilities(void);

OPENUSD_DOTNET_API openusd_status openusd_get_version(
    char* buffer,
    size_t capacity,
    size_t* required);

OPENUSD_DOTNET_API openusd_status openusd_register_plugins(
    const char* path,
    size_t* plugin_count,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_open(
    const char* path,
    openusd_stage** stage,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_open_masked(
    const char* path,
    const openusd_string_list_view* mask_paths,
    openusd_stage** stage,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_create_new(
    const char* path,
    openusd_stage** stage,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_retain(
    openusd_stage* const stage,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API void openusd_stage_release(openusd_stage* stage);

OPENUSD_DOTNET_API openusd_status openusd_stage_access_begin(
    openusd_stage* const stage,
    openusd_stage_access** access,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_access_end(
    openusd_stage_access* const access,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_root_layer(
    const openusd_stage* stage,
    openusd_layer** layer,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_session_layer(
    const openusd_stage* stage,
    openusd_layer** layer,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_root_layer_identifier(
    const openusd_stage* stage,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_session_layer_identifier(
    const openusd_stage* stage,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_edit_target_layer_identifier(
    const openusd_stage* stage,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_edit_target_root_layer(
    const openusd_stage* stage,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_edit_target_session_layer(
    const openusd_stage* stage,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_edit_target_layer(
    const openusd_stage* stage,
    const openusd_layer* layer,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_layer_stack_identifiers(
    const openusd_stage* stage,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_mute_layer(
    const openusd_stage* stage,
    const char* layer_identifier,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_unmute_layer(
    const openusd_stage* stage,
    const char* layer_identifier,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_is_layer_muted(
    const openusd_stage* stage,
    const char* layer_identifier,
    int32_t* muted,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_save(
    const openusd_stage* stage,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_reload(
    const openusd_stage* stage,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_export(
    const openusd_stage* stage,
    const char* path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_start_time_code(
    const openusd_stage* stage,
    double* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_start_time_code(
    const openusd_stage* stage,
    double value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_end_time_code(
    const openusd_stage* stage,
    double* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_end_time_code(
    const openusd_stage* stage,
    double value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_frames_per_second(
    const openusd_stage* stage,
    double* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_frames_per_second(
    const openusd_stage* stage,
    double value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_time_codes_per_second(
    const openusd_stage* stage,
    double* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_time_codes_per_second(
    const openusd_stage* stage,
    double value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_world_bounds(
    const openusd_stage* stage,
    const char* target_prim_path,
    uint32_t purpose_mask,
    int32_t time_sampled,
    double time_code,
    openusd_bounds3d* bounds,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_default_prim_path(
    const openusd_stage* stage,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_default_prim(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_clear_default_prim(
    const openusd_stage* stage,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_define_prim(
    const openusd_stage* stage,
    const char* prim_path,
    const char* type_name,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_override_prim(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_create_class_prim(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_prim_paths(
    const openusd_stage* stage,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_prim_type_name(
    const openusd_stage* stage,
    const char* prim_path,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_prim_applied_schemas(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_prim_child_paths(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_prim_attribute_names(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_prim_relationship_names(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_attribute_type_name(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_attribute_value_state(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    int32_t* has_authored_value_opinion,
    int32_t* value_is_blocked,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_attribute_time_samples(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    double* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_clear_attribute_value(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_block_attribute_value(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_attribute_scalar_value(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    openusd_scalar_value* value,
    char* string_buffer,
    size_t string_capacity,
    size_t* string_required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_double(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    double value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_double(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    double* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_double_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const double* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_double_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    double* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_matrix4d(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const openusd_matrix4d* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_matrix4d(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    openusd_matrix4d* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_int32_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const int32_t* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_int32_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    int32_t* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_float_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const float* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_float_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    float* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_vec2f_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const openusd_vec2f* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_vec2f_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    openusd_vec2f* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_vec3f_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const openusd_vec3f* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_vec3f_array(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    openusd_vec3f* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t schema_kind,
    int32_t* matches,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_define_xform(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_define_mesh(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_define_camera(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_imageable_set_visibility(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t visibility,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_imageable_get_visibility(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    int32_t* visibility,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_imageable_set_purpose(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t purpose,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_imageable_get_purpose(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* purpose,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_xformable_set_local_transform(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_matrix4d* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_xformable_get_local_transform(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    openusd_matrix4d* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_xformable_get_world_transform(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    openusd_matrix4d* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_xformable_set_reset_xform_stack(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t reset,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_xformable_get_reset_xform_stack(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* reset,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_set_points(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_vec3f* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_get_points(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    openusd_vec3f* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_set_topology(
    const openusd_stage* stage,
    const char* prim_path,
    const int32_t* face_vertex_counts,
    size_t face_count,
    const int32_t* face_vertex_indices,
    size_t index_count,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_get_face_vertex_counts(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_get_face_vertex_indices(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_set_normals(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_vec3f* values,
    size_t count,
    int32_t interpolation,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_get_normals(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    openusd_vec3f* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_set_normals_interpolation(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t interpolation,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_get_normals_interpolation(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* interpolation,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_set_subdivision_scheme(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t scheme,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_get_subdivision_scheme(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* scheme,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_set_orientation(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t orientation,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_get_orientation(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* orientation,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_set_double_sided(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t double_sided,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_get_double_sided(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* double_sided,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_set_extent(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_extent3f* extent,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_mesh_get_extent(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    openusd_extent3f* extent,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_camera_set_projection(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t projection,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_camera_get_projection(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* projection,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_camera_set_float_property(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t property,
    float value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_camera_get_float_property(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t property,
    float* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_camera_set_clipping_range(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_vec2f* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_camera_get_clipping_range(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_vec2f* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_camera_get_state(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    openusd_geom_camera_state* state,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_bool(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_bool(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    int32_t* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_int64(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int64_t value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_int64(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    int64_t* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_string(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const char* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_string(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_token(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const char* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_token(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_vec3f(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const openusd_vec3f* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_vec3f(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    openusd_vec3f* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_color3f(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    const openusd_vec3f* value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_color3f(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    openusd_vec3f* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_has_prim(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* exists,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_remove_prim(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_prim_active(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t active,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_prim_active(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* active,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_create_relationship(
    const openusd_stage* stage,
    const char* prim_path,
    const char* relationship_name,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_relationship_targets(
    const openusd_stage* stage,
    const char* prim_path,
    const char* relationship_name,
    const openusd_string_list_view* targets,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_relationship_targets(
    const openusd_stage* stage,
    const char* prim_path,
    const char* relationship_name,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_clear_relationship_targets(
    const openusd_stage* stage,
    const char* prim_path,
    const char* relationship_name,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_add_reference(
    const openusd_stage* stage,
    const char* prim_path,
    const char* asset_path,
    const char* target_prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_clear_references(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_add_payload(
    const openusd_stage* stage,
    const char* prim_path,
    const char* asset_path,
    const char* target_prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_clear_payloads(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_composed_payload_arcs(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_payload_arc_list** list,
    openusd_payload_arc_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_add_inherit(
    const openusd_stage* stage,
    const char* prim_path,
    const char* inherited_prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_clear_inherits(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_add_specialize(
    const openusd_stage* stage,
    const char* prim_path,
    const char* specialized_prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_clear_specializes(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_load_prim(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_unload_prim(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_is_prim_loaded(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* loaded,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_instanceable(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t instanceable,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_instanceable(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* instanceable,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_is_prim_instance(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* instance,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_is_prim_prototype(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* prototype,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_prim_prototype_path(
    const openusd_stage* stage,
    const char* prim_path,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_add_variant_set(
    const openusd_stage* stage,
    const char* prim_path,
    const char* variant_set_name,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_variant_set_names(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_add_variant(
    const openusd_stage* stage,
    const char* prim_path,
    const char* variant_set_name,
    const char* variant_name,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_variant_selection(
    const openusd_stage* stage,
    const char* prim_path,
    const char* variant_set_name,
    const char* variant_selection,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_variant_selection(
    const openusd_stage* stage,
    const char* prim_path,
    const char* variant_set_name,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_variant_names(
    const openusd_stage* stage,
    const char* prim_path,
    const char* variant_set_name,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_set_prim_metadata(
    const openusd_stage* stage,
    const char* prim_path,
    const char* key,
    const openusd_metadata_value* value,
    const char* string_value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_prim_metadata(
    const openusd_stage* stage,
    const char* prim_path,
    const char* key,
    int32_t requested_kind,
    openusd_metadata_value* value,
    char* string_buffer,
    size_t string_capacity,
    size_t* string_required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_clear_prim_metadata(
    const openusd_stage* stage,
    const char* prim_path,
    const char* key,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_stage_get_change_serial(
    const openusd_stage* stage,
    uint64_t* serial,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API void openusd_layer_release(openusd_layer* layer);

OPENUSD_DOTNET_API openusd_status openusd_layer_get_identifier(
    const openusd_layer* layer,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_layer_save(
    const openusd_layer* layer,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_layer_reload(
    const openusd_layer* layer,
    int32_t force,
    int32_t* reloaded,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_layer_export(
    const openusd_layer* layer,
    const char* path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_layer_add_sublayer(
    const openusd_layer* layer,
    const char* sublayer_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_layer_remove_sublayer(
    const openusd_layer* layer,
    const char* sublayer_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_layer_get_sublayer_paths(
    const openusd_layer* layer,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_layer_set_metadata(
    const openusd_layer* layer,
    const char* key,
    const openusd_metadata_value* value,
    const char* string_value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_layer_get_metadata(
    const openusd_layer* layer,
    const char* key,
    int32_t requested_kind,
    openusd_metadata_value* value,
    char* string_buffer,
    size_t string_capacity,
    size_t* string_required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_layer_clear_metadata(
    const openusd_layer* layer,
    const char* key,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_is_material(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* is_material,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_is_shader(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* is_shader,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_define_material(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_define_shader(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_shader_set_source_id(
    const openusd_stage* stage,
    const char* shader_path,
    const char* source_id,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_shader_get_source_id(
    const openusd_stage* stage,
    const char* shader_path,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_create_input(
    const openusd_stage* stage,
    const char* connectable_path,
    const char* input_name,
    openusd_shade_value_type value_type,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_get_input_type(
    const openusd_stage* stage,
    const char* connectable_path,
    const char* input_name,
    openusd_shade_value_type* value_type,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_set_input_float(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    float value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_get_input_float(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    float* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_set_input_vec3f(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    openusd_shade_value_type value_type,
    openusd_vec3f value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_get_input_vec3f(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    openusd_shade_value_type value_type,
    openusd_vec3f* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_set_input_string(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    openusd_shade_value_type value_type,
    const char* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_get_input_string(
    const openusd_stage* stage,
    const char* shader_path,
    const char* input_name,
    openusd_shade_value_type value_type,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_create_output(
    const openusd_stage* stage,
    const char* connectable_path,
    const char* output_name,
    openusd_shade_value_type value_type,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_get_output_type(
    const openusd_stage* stage,
    const char* connectable_path,
    const char* output_name,
    openusd_shade_value_type* value_type,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_connect(
    const openusd_stage* stage,
    const char* destination_path,
    const char* destination_name,
    openusd_shade_attribute_type destination_type,
    const char* source_path,
    const char* source_name,
    openusd_shade_attribute_type source_type,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_disconnect(
    const openusd_stage* stage,
    const char* destination_path,
    const char* destination_name,
    openusd_shade_attribute_type destination_type,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_get_connected_source(
    const openusd_stage* stage,
    const char* destination_path,
    const char* destination_name,
    openusd_shade_attribute_type destination_type,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_shade_attribute_type* source_type,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_get_connected_sources(
    const openusd_stage* stage,
    const char* destination_path,
    const char* destination_name,
    openusd_shade_attribute_type destination_type,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_material_create_surface_output(
    const openusd_stage* stage,
    const char* material_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_material_bind(
    const openusd_stage* stage,
    const char* prim_path,
    const char* material_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_material_unbind(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_get_direct_material(
    const openusd_stage* stage,
    const char* prim_path,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_lux_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_lux_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_schema_kind schema_kind,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_lux_set_float(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_float_property property,
    float value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_lux_get_float(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_float_property property,
    float* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_lux_set_bool(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_bool_property property,
    int32_t value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_lux_get_bool(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_bool_property property,
    int32_t* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_lux_set_color(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_vec3f value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_lux_get_color(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_vec3f* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_lux_set_shape(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_shape_property property,
    float value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_lux_get_shape(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_shape_property property,
    float* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_lux_set_asset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_asset_property property,
    const char* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_lux_get_asset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_asset_property property,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_lux_has_shaping(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* has_shaping,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_lux_apply_shaping(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_lux_set_shaping(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_shaping_property property,
    float value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_lux_get_shaping(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_lux_shaping_property property,
    float* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_schema_kind schema_kind,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_has_binding(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* has_binding,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_apply_binding(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_set_joints(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_schema_kind schema_kind,
    const openusd_string_list_view* joints,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_get_joints(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_schema_kind schema_kind,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_set_skeleton_matrices(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_matrix_property property,
    const openusd_matrix4d* values,
    size_t count,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_get_skeleton_matrices(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_matrix_property property,
    openusd_matrix4d* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_set_animation_vec3(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_animation_vec3_property property,
    const openusd_vec3f* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_get_animation_vec3(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_animation_vec3_property property,
    int32_t time_sampled,
    double time_code,
    openusd_vec3f* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_set_animation_rotations(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_quatf* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_get_animation_rotations(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    openusd_quatf* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_set_binding_target(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_binding_relationship relationship,
    const char* target_prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_get_binding_target(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_binding_relationship relationship,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_clear_binding_target(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_binding_relationship relationship,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_set_geom_bind_transform(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_matrix4d* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_get_geom_bind_transform(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_matrix4d* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_set_joint_influences(
    const openusd_stage* stage,
    const char* prim_path,
    const int32_t* joint_indices,
    size_t joint_index_count,
    const float* joint_weights,
    size_t joint_weight_count,
    int32_t element_size,
    openusd_skel_interpolation interpolation,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_get_joint_influences(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* joint_indices,
    size_t joint_index_capacity,
    size_t* joint_index_required,
    float* joint_weights,
    size_t joint_weight_capacity,
    size_t* joint_weight_required,
    int32_t* element_size,
    openusd_skel_interpolation* interpolation,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API void openusd_string_list_release(openusd_string_list* list);
OPENUSD_DOTNET_API void openusd_payload_arc_list_release(openusd_payload_arc_list* list);

#ifdef __cplusplus
}
#endif

#endif
