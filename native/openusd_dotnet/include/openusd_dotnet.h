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
typedef struct openusd_pcp_prim_index_list openusd_pcp_prim_index_list;
typedef struct openusd_ts_spline openusd_ts_spline;
typedef struct openusd_validation_metadata_list openusd_validation_metadata_list;
typedef struct openusd_validation_error_list openusd_validation_error_list;

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

/* Pcp prim-index inspection: nodes are strong-to-weak Pcp node records. */
#define OPENUSD_PCP_PRIM_INDEX_VIEW_VERSION 1u
typedef enum openusd_pcp_arc_type
{
    OPENUSD_PCP_ARC_ROOT = 0,
    OPENUSD_PCP_ARC_INHERIT = 1,
    OPENUSD_PCP_ARC_VARIANT = 2,
    OPENUSD_PCP_ARC_RELOCATE = 3,
    OPENUSD_PCP_ARC_REFERENCE = 4,
    OPENUSD_PCP_ARC_PAYLOAD = 5,
    OPENUSD_PCP_ARC_SPECIALIZE = 6
} openusd_pcp_arc_type;

typedef struct openusd_pcp_node_record
{
    int32_t parent_index;
    int32_t arc_type;
    int32_t is_culled;
    int32_t is_inert;
    int32_t is_due_to_ancestor;
    int32_t has_specs;
    int32_t can_contribute_specs;
    int32_t namespace_depth;
    int32_t depth_below_introduction;
    int32_t sibling_index_at_origin;
    size_t string_offset;
    size_t string_count;
    size_t layer_offset;
    size_t layer_count;
} openusd_pcp_node_record;

typedef struct openusd_pcp_prim_index_view
{
    uint32_t struct_size;
    uint32_t version;
    const openusd_pcp_node_record* nodes;
    size_t nodes_size;
    size_t node_count;
    const char* data;
    size_t data_size;
    const size_t* offsets;
    size_t offsets_size;
    size_t string_count;
    size_t error_offset;
    size_t error_count;
} openusd_pcp_prim_index_view;

/* Ts spline inspection/evaluation. This ABI is double-valued and bulk-knot based. */
#define OPENUSD_TS_SPLINE_DATA_VIEW_VERSION 1u
typedef enum openusd_ts_interp_mode
{
    OPENUSD_TS_INTERP_VALUE_BLOCK = 0,
    OPENUSD_TS_INTERP_HELD = 1,
    OPENUSD_TS_INTERP_LINEAR = 2,
    OPENUSD_TS_INTERP_CURVE = 3
} openusd_ts_interp_mode;

typedef enum openusd_ts_curve_type
{
    OPENUSD_TS_CURVE_BEZIER = 0,
    OPENUSD_TS_CURVE_HERMITE = 1
} openusd_ts_curve_type;

typedef enum openusd_ts_extrap_mode
{
    OPENUSD_TS_EXTRAP_VALUE_BLOCK = 0,
    OPENUSD_TS_EXTRAP_HELD = 1,
    OPENUSD_TS_EXTRAP_LINEAR = 2,
    OPENUSD_TS_EXTRAP_SLOPED = 3,
    OPENUSD_TS_EXTRAP_LOOP_REPEAT = 4,
    OPENUSD_TS_EXTRAP_LOOP_RESET = 5,
    OPENUSD_TS_EXTRAP_LOOP_OSCILLATE = 6
} openusd_ts_extrap_mode;

typedef enum openusd_ts_tangent_algorithm
{
    OPENUSD_TS_TANGENT_NONE = 0,
    OPENUSD_TS_TANGENT_CUSTOM = 1,
    OPENUSD_TS_TANGENT_AUTO_EASE = 2
} openusd_ts_tangent_algorithm;

#define OPENUSD_TS_KNOT_HAS_PRE_VALUE (UINT32_C(1) << 0)
typedef struct openusd_ts_knot_record
{
    double time;
    double value;
    double pre_value;
    double pre_tangent_width;
    double pre_tangent_slope;
    double post_tangent_width;
    double post_tangent_slope;
    int32_t next_interpolation;
    int32_t pre_tangent_algorithm;
    int32_t post_tangent_algorithm;
    uint32_t flags;
} openusd_ts_knot_record;

typedef struct openusd_ts_extrapolation_record
{
    int32_t mode;
    double slope;
} openusd_ts_extrapolation_record;

typedef struct openusd_ts_spline_data_view
{
    uint32_t struct_size;
    uint32_t version;
    int32_t curve_type;
    int32_t is_time_valued;
    openusd_ts_extrapolation_record pre_extrapolation;
    openusd_ts_extrapolation_record post_extrapolation;
    const openusd_ts_knot_record* knots;
    size_t knots_size;
    size_t knot_count;
} openusd_ts_spline_data_view;

/* UsdValidation registry/run results. */
#define OPENUSD_VALIDATION_METADATA_VIEW_VERSION 1u
typedef struct openusd_validation_metadata_record
{
    int32_t is_suite;
    int32_t is_time_dependent;
    size_t string_offset;
    size_t string_count;
    size_t keyword_offset;
    size_t keyword_count;
    size_t schema_type_offset;
    size_t schema_type_count;
} openusd_validation_metadata_record;

typedef struct openusd_validation_metadata_view
{
    uint32_t struct_size;
    uint32_t version;
    const openusd_validation_metadata_record* records;
    size_t records_size;
    size_t count;
    const char* data;
    size_t data_size;
    const size_t* offsets;
    size_t offsets_size;
    size_t string_count;
} openusd_validation_metadata_view;

#define OPENUSD_VALIDATION_ERROR_VIEW_VERSION 1u
typedef enum openusd_validation_severity
{
    OPENUSD_VALIDATION_SEVERITY_NONE = 0,
    OPENUSD_VALIDATION_SEVERITY_ERROR = 1,
    OPENUSD_VALIDATION_SEVERITY_WARNING = 2,
    OPENUSD_VALIDATION_SEVERITY_INFO = 3
} openusd_validation_severity;

typedef struct openusd_validation_error_record
{
    int32_t severity;
    size_t string_offset;
    size_t string_count;
    size_t site_offset;
    size_t site_count;
} openusd_validation_error_record;

typedef struct openusd_validation_error_view
{
    uint32_t struct_size;
    uint32_t version;
    const openusd_validation_error_record* records;
    size_t records_size;
    size_t count;
    const char* data;
    size_t data_size;
    const size_t* offsets;
    size_t offsets_size;
    size_t string_count;
} openusd_validation_error_view;

#define OPENUSD_IMAGE_INFO_VERSION 1u
typedef struct openusd_image_info
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t width;
    uint32_t height;
} openusd_image_info;

#define OPENUSD_CAPABILITY_STRING_LIST_V2 (UINT64_C(1) << 0)
#define OPENUSD_CAPABILITY_GUARDED_STATUS_EXPORTS (UINT64_C(1) << 1)
#define OPENUSD_CAPABILITY_SHADE_CONNECTED_SOURCES (UINT64_C(1) << 2)
#define OPENUSD_CAPABILITY_SHARED_STAGE_ACCESS (UINT64_C(1) << 3)
#define OPENUSD_CAPABILITY_WORLD_BOUNDS_QUERY (UINT64_C(1) << 4)
#define OPENUSD_CAPABILITY_VARIANT_SET_NAMES (UINT64_C(1) << 5)
#define OPENUSD_CAPABILITY_COMPOSED_DIRECT_PAYLOAD_ARCS (UINT64_C(1) << 6)
#define OPENUSD_CAPABILITY_WORLD_TRANSFORM_QUERY (UINT64_C(1) << 7)
#define OPENUSD_CAPABILITY_CAMERA_STATE_QUERY (UINT64_C(1) << 8)
#define OPENUSD_CAPABILITY_PCP_PRIM_INDEX_QUERY (UINT64_C(1) << 9)
#define OPENUSD_CAPABILITY_TS_SPLINE_QUERY (UINT64_C(1) << 10)
#define OPENUSD_CAPABILITY_USD_VALIDATION_QUERY (UINT64_C(1) << 11)
#define OPENUSD_CAPABILITY_USDGEOM_SCHEMA_COMPLETE (UINT64_C(1) << 12)
#define OPENUSD_CAPABILITY_USD_PHYSICS_SCHEMA (UINT64_C(1) << 13)
#define OPENUSD_CAPABILITY_USD_SHADE_SKEL (UINT64_C(1) << 14)
/* Schema facade allocations: 12 Geom, 13 Physics, 14 Shade/Skel, 15 Vol/Render/Media/Proc/UI. */
#define OPENUSD_CAPABILITY_SCHEMA_FACADES_VOL_RENDER_MEDIA_PROC_UI (UINT64_C(1) << 15)

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
    OPENUSD_GEOM_SCHEMA_CAMERA = 4,
    OPENUSD_GEOM_SCHEMA_SUBSET = 5,
    OPENUSD_GEOM_SCHEMA_BASIS_CURVES = 6,
    OPENUSD_GEOM_SCHEMA_NURBS_CURVES = 7,
    OPENUSD_GEOM_SCHEMA_HERMITE_CURVES = 8,
    OPENUSD_GEOM_SCHEMA_NURBS_PATCH = 9,
    OPENUSD_GEOM_SCHEMA_POINTS = 10,
    OPENUSD_GEOM_SCHEMA_POINT_INSTANCER = 11,
    OPENUSD_GEOM_SCHEMA_CAPSULE = 12,
    OPENUSD_GEOM_SCHEMA_CONE = 13,
    OPENUSD_GEOM_SCHEMA_CUBE = 14,
    OPENUSD_GEOM_SCHEMA_CYLINDER = 15,
    OPENUSD_GEOM_SCHEMA_SPHERE = 16,
    OPENUSD_GEOM_SCHEMA_PLANE = 17,
    OPENUSD_GEOM_SCHEMA_TET_MESH = 18,
    OPENUSD_GEOM_SCHEMA_MODEL_API = 19,
    OPENUSD_GEOM_SCHEMA_PRIMVARS_API = 20
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

typedef enum openusd_shade_material_terminal
{
    OPENUSD_SHADE_MATERIAL_TERMINAL_SURFACE = 0,
    OPENUSD_SHADE_MATERIAL_TERMINAL_DISPLACEMENT = 1,
    OPENUSD_SHADE_MATERIAL_TERMINAL_VOLUME = 2
} openusd_shade_material_terminal;

typedef enum openusd_shade_binding_strength
{
    OPENUSD_SHADE_BINDING_WEAKER_THAN_DESCENDANTS = 0,
    OPENUSD_SHADE_BINDING_STRONGER_THAN_DESCENDANTS = 1
} openusd_shade_binding_strength;

typedef enum openusd_shade_material_purpose
{
    OPENUSD_SHADE_MATERIAL_PURPOSE_ALL = 0,
    OPENUSD_SHADE_MATERIAL_PURPOSE_PREVIEW = 1,
    OPENUSD_SHADE_MATERIAL_PURPOSE_FULL = 2
} openusd_shade_material_purpose;

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
    OPENUSD_SKEL_SCHEMA_ANIMATION = 2,
    OPENUSD_SKEL_SCHEMA_BLEND_SHAPE = 3
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
    OPENUSD_SKEL_BINDING_ANIMATION_SOURCE = 1,
    OPENUSD_SKEL_BINDING_BLEND_SHAPE_TARGETS = 2
} openusd_skel_binding_relationship;

typedef enum openusd_skel_interpolation
{
    OPENUSD_SKEL_INTERPOLATION_CONSTANT = 0,
    OPENUSD_SKEL_INTERPOLATION_VERTEX = 1
} openusd_skel_interpolation;

/* ---- UsdPhysics schema facade declarations ---- */
typedef enum openusd_physics_schema_kind
{
    OPENUSD_PHYSICS_SCHEMA_SCENE = 0,
    OPENUSD_PHYSICS_SCHEMA_COLLISION_GROUP = 1,
    OPENUSD_PHYSICS_SCHEMA_JOINT = 2,
    OPENUSD_PHYSICS_SCHEMA_REVOLUTE_JOINT = 3,
    OPENUSD_PHYSICS_SCHEMA_PRISMATIC_JOINT = 4,
    OPENUSD_PHYSICS_SCHEMA_SPHERICAL_JOINT = 5,
    OPENUSD_PHYSICS_SCHEMA_DISTANCE_JOINT = 6,
    OPENUSD_PHYSICS_SCHEMA_FIXED_JOINT = 7
} openusd_physics_schema_kind;

typedef enum openusd_physics_api_kind
{
    OPENUSD_PHYSICS_API_RIGID_BODY = 0,
    OPENUSD_PHYSICS_API_MASS = 1,
    OPENUSD_PHYSICS_API_COLLISION = 2,
    OPENUSD_PHYSICS_API_MESH_COLLISION = 3,
    OPENUSD_PHYSICS_API_MATERIAL = 4,
    OPENUSD_PHYSICS_API_FILTERED_PAIRS = 5,
    OPENUSD_PHYSICS_API_ARTICULATION_ROOT = 6,
    OPENUSD_PHYSICS_API_LIMIT = 7,
    OPENUSD_PHYSICS_API_DRIVE = 8
} openusd_physics_api_kind;

typedef enum openusd_physics_float_property
{
    OPENUSD_PHYSICS_FLOAT_SCENE_GRAVITY_MAGNITUDE = 0,
    OPENUSD_PHYSICS_FLOAT_MASS_MASS = 1,
    OPENUSD_PHYSICS_FLOAT_MASS_DENSITY = 2,
    OPENUSD_PHYSICS_FLOAT_MATERIAL_DYNAMIC_FRICTION = 3,
    OPENUSD_PHYSICS_FLOAT_MATERIAL_STATIC_FRICTION = 4,
    OPENUSD_PHYSICS_FLOAT_MATERIAL_RESTITUTION = 5,
    OPENUSD_PHYSICS_FLOAT_MATERIAL_DENSITY = 6,
    OPENUSD_PHYSICS_FLOAT_JOINT_BREAK_FORCE = 7,
    OPENUSD_PHYSICS_FLOAT_JOINT_BREAK_TORQUE = 8,
    OPENUSD_PHYSICS_FLOAT_REVOLUTE_LOWER_LIMIT = 9,
    OPENUSD_PHYSICS_FLOAT_REVOLUTE_UPPER_LIMIT = 10,
    OPENUSD_PHYSICS_FLOAT_PRISMATIC_LOWER_LIMIT = 11,
    OPENUSD_PHYSICS_FLOAT_PRISMATIC_UPPER_LIMIT = 12,
    OPENUSD_PHYSICS_FLOAT_SPHERICAL_CONE_ANGLE0_LIMIT = 13,
    OPENUSD_PHYSICS_FLOAT_SPHERICAL_CONE_ANGLE1_LIMIT = 14,
    OPENUSD_PHYSICS_FLOAT_DISTANCE_MIN_DISTANCE = 15,
    OPENUSD_PHYSICS_FLOAT_DISTANCE_MAX_DISTANCE = 16,
    OPENUSD_PHYSICS_FLOAT_LIMIT_LOW = 17,
    OPENUSD_PHYSICS_FLOAT_LIMIT_HIGH = 18,
    OPENUSD_PHYSICS_FLOAT_DRIVE_MAX_FORCE = 19,
    OPENUSD_PHYSICS_FLOAT_DRIVE_TARGET_POSITION = 20,
    OPENUSD_PHYSICS_FLOAT_DRIVE_TARGET_VELOCITY = 21,
    OPENUSD_PHYSICS_FLOAT_DRIVE_DAMPING = 22,
    OPENUSD_PHYSICS_FLOAT_DRIVE_STIFFNESS = 23
} openusd_physics_float_property;

typedef enum openusd_physics_bool_property
{
    OPENUSD_PHYSICS_BOOL_RIGID_BODY_ENABLED = 0,
    OPENUSD_PHYSICS_BOOL_RIGID_BODY_KINEMATIC_ENABLED = 1,
    OPENUSD_PHYSICS_BOOL_RIGID_BODY_STARTS_ASLEEP = 2,
    OPENUSD_PHYSICS_BOOL_COLLISION_ENABLED = 3,
    OPENUSD_PHYSICS_BOOL_COLLISION_GROUP_INVERT_FILTERED_GROUPS = 4,
    OPENUSD_PHYSICS_BOOL_JOINT_ENABLED = 5,
    OPENUSD_PHYSICS_BOOL_JOINT_COLLISION_ENABLED = 6,
    OPENUSD_PHYSICS_BOOL_JOINT_EXCLUDE_FROM_ARTICULATION = 7
} openusd_physics_bool_property;

typedef enum openusd_physics_vec3f_property
{
    OPENUSD_PHYSICS_VEC3F_SCENE_GRAVITY_DIRECTION = 0,
    OPENUSD_PHYSICS_VEC3F_RIGID_BODY_VELOCITY = 1,
    OPENUSD_PHYSICS_VEC3F_RIGID_BODY_ANGULAR_VELOCITY = 2,
    OPENUSD_PHYSICS_VEC3F_MASS_CENTER_OF_MASS = 3,
    OPENUSD_PHYSICS_VEC3F_MASS_DIAGONAL_INERTIA = 4,
    OPENUSD_PHYSICS_VEC3F_JOINT_LOCAL_POS0 = 5,
    OPENUSD_PHYSICS_VEC3F_JOINT_LOCAL_POS1 = 6
} openusd_physics_vec3f_property;

typedef enum openusd_physics_quatf_property
{
    OPENUSD_PHYSICS_QUATF_MASS_PRINCIPAL_AXES = 0,
    OPENUSD_PHYSICS_QUATF_JOINT_LOCAL_ROT0 = 1,
    OPENUSD_PHYSICS_QUATF_JOINT_LOCAL_ROT1 = 2
} openusd_physics_quatf_property;

typedef enum openusd_physics_token_property
{
    OPENUSD_PHYSICS_TOKEN_MESH_COLLISION_APPROXIMATION = 0,
    OPENUSD_PHYSICS_TOKEN_REVOLUTE_AXIS = 1,
    OPENUSD_PHYSICS_TOKEN_PRISMATIC_AXIS = 2,
    OPENUSD_PHYSICS_TOKEN_SPHERICAL_AXIS = 3,
    OPENUSD_PHYSICS_TOKEN_DRIVE_TYPE = 4
} openusd_physics_token_property;

typedef enum openusd_physics_string_property
{
    OPENUSD_PHYSICS_STRING_COLLISION_GROUP_MERGE_GROUP_NAME = 0
} openusd_physics_string_property;

typedef enum openusd_skel_skinning_method
{
    OPENUSD_SKEL_SKINNING_CLASSIC_LINEAR = 0,
    OPENUSD_SKEL_SKINNING_DUAL_QUATERNION = 1
} openusd_skel_skinning_method;

typedef enum openusd_skel_blend_shape_vec3_property
{
    OPENUSD_SKEL_BLEND_SHAPE_OFFSETS = 0,
    OPENUSD_SKEL_BLEND_SHAPE_NORMAL_OFFSETS = 1
} openusd_skel_blend_shape_vec3_property;


/* UsdVol schema data API. */
typedef enum openusd_vol_schema_kind
{
    OPENUSD_VOL_SCHEMA_VOLUME = 0,
    OPENUSD_VOL_SCHEMA_VOLUME_FIELD_BASE = 1,
    OPENUSD_VOL_SCHEMA_VOLUME_FIELD_ASSET = 2,
    OPENUSD_VOL_SCHEMA_FIELD_BASE = 3,
    OPENUSD_VOL_SCHEMA_FIELD_ASSET = 4,
    OPENUSD_VOL_SCHEMA_OPENVDB_ASSET = 5,
    OPENUSD_VOL_SCHEMA_FIELD3D_ASSET = 6
} openusd_vol_schema_kind;

typedef enum openusd_vol_asset_property
{
    OPENUSD_VOL_ASSET_FILE_PATH = 0
} openusd_vol_asset_property;

/* UsdRender schema data API. */
typedef enum openusd_render_schema_kind
{
    OPENUSD_RENDER_SCHEMA_SETTINGS_BASE = 0,
    OPENUSD_RENDER_SCHEMA_SETTINGS = 1,
    OPENUSD_RENDER_SCHEMA_PRODUCT = 2,
    OPENUSD_RENDER_SCHEMA_VAR = 3,
    OPENUSD_RENDER_SCHEMA_PASS = 4
} openusd_render_schema_kind;

/* UsdMedia schema data API. */
typedef enum openusd_media_schema_kind
{
    OPENUSD_MEDIA_SCHEMA_SPATIAL_AUDIO = 0,
    OPENUSD_MEDIA_SCHEMA_ASSET_PREVIEWS_API = 1
} openusd_media_schema_kind;

typedef enum openusd_media_asset_property
{
    OPENUSD_MEDIA_ASSET_FILE_PATH = 0,
    OPENUSD_MEDIA_ASSET_DEFAULT_THUMBNAIL = 1
} openusd_media_asset_property;

typedef enum openusd_media_time_property
{
    OPENUSD_MEDIA_TIME_START = 0,
    OPENUSD_MEDIA_TIME_END = 1
} openusd_media_time_property;

/* UsdProc schema data API. */
typedef enum openusd_proc_schema_kind
{
    OPENUSD_PROC_SCHEMA_GENERATIVE_PROCEDURAL = 0
} openusd_proc_schema_kind;

/* UsdUI schema data API. */
typedef enum openusd_ui_schema_kind
{
    OPENUSD_UI_SCHEMA_BACKDROP = 0,
    OPENUSD_UI_SCHEMA_NODE_GRAPH_NODE_API = 1,
    OPENUSD_UI_SCHEMA_SCENE_GRAPH_PRIM_API = 2
} openusd_ui_schema_kind;

typedef enum openusd_ui_vec2f_property
{
    OPENUSD_UI_VEC2F_NODE_POS = 0,
    OPENUSD_UI_VEC2F_NODE_SIZE = 1
} openusd_ui_vec2f_property;

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

OPENUSD_DOTNET_API openusd_status openusd_decode_image_rgba8(
    const char* asset_path,
    uint32_t convert_srgb_to_linear,
    openusd_image_info* info,
    uint8_t* rgba,
    size_t rgba_size,
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

/* UsdGeom schema-completion additions. */
OPENUSD_DOTNET_API openusd_status openusd_geom_define_schema(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t schema_kind,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_set_int32_attr(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t value,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_get_int32_attr(
    const openusd_stage* stage,
    const char* prim_path,
    const char* attribute_name,
    int32_t time_sampled,
    double time_code,
    int32_t* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_point_instancer_set_orientations(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_quatf* values,
    size_t count,
    int32_t time_sampled,
    double time_code,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_geom_point_instancer_get_orientations(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    openusd_quatf* values,
    size_t capacity,
    size_t* required,
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

OPENUSD_DOTNET_API openusd_status openusd_pcp_get_prim_index(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_pcp_prim_index_list** list,
    openusd_pcp_prim_index_view* view,
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


/* UsdVol schema data API. */
OPENUSD_DOTNET_API openusd_status openusd_vol_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_vol_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_vol_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_vol_schema_kind schema_kind,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_vol_get_field_paths(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_vol_set_field_path(
    const openusd_stage* stage,
    const char* prim_path,
    const char* field_name,
    const char* target_prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_vol_has_field_relationship(
    const openusd_stage* stage,
    const char* prim_path,
    const char* field_name,
    int32_t* has_relationship,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_vol_block_field_relationship(
    const openusd_stage* stage,
    const char* prim_path,
    const char* field_name,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_vol_set_asset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_vol_asset_property property,
    const char* asset_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_vol_get_asset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_vol_asset_property property,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_vol_set_field_index(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t field_index,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_vol_get_field_index(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* field_index,
    openusd_error_buffer* error);

/* UsdRender schema data API. */
OPENUSD_DOTNET_API openusd_status openusd_render_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_render_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_render_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_render_schema_kind schema_kind,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_render_set_resolution(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t width,
    int32_t height,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_render_get_resolution(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* width,
    int32_t* height,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_render_set_data_window_ndc(
    const openusd_stage* stage,
    const char* prim_path,
    float min_x,
    float min_y,
    float max_x,
    float max_y,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_render_get_data_window_ndc(
    const openusd_stage* stage,
    const char* prim_path,
    float* min_x,
    float* min_y,
    float* max_x,
    float* max_y,
    openusd_error_buffer* error);

/* UsdMedia schema data API. */
OPENUSD_DOTNET_API openusd_status openusd_media_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_media_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_media_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_media_schema_kind schema_kind,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_media_apply_api(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_media_schema_kind schema_kind,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_media_set_asset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_media_asset_property property,
    const char* asset_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_media_get_asset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_media_asset_property property,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_media_clear_asset(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_media_asset_property property,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_media_set_time(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_media_time_property property,
    double value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_media_get_time(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_media_time_property property,
    double* value,
    openusd_error_buffer* error);

/* UsdProc schema data API. */
OPENUSD_DOTNET_API openusd_status openusd_proc_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_proc_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_proc_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_proc_schema_kind schema_kind,
    openusd_error_buffer* error);

/* UsdUI schema data API. */
OPENUSD_DOTNET_API openusd_status openusd_ui_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_ui_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_ui_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_ui_schema_kind schema_kind,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_ui_apply_api(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_ui_schema_kind schema_kind,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_ui_set_vec2f(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_ui_vec2f_property property,
    const openusd_vec2f* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_ui_get_vec2f(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_ui_vec2f_property property,
    openusd_vec2f* value,
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

OPENUSD_DOTNET_API openusd_status openusd_shade_is_node_graph(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* is_node_graph,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_define_material(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_define_shader(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_define_node_graph(
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

OPENUSD_DOTNET_API openusd_status openusd_shade_get_input_names(
    const openusd_stage* stage,
    const char* connectable_path,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_get_output_names(
    const openusd_stage* stage,
    const char* connectable_path,
    openusd_string_list** list,
    openusd_string_list_view* view,
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

OPENUSD_DOTNET_API openusd_status openusd_shade_material_create_terminal_output(
    const openusd_stage* stage,
    const char* material_path,
    openusd_shade_material_terminal terminal,
    const char* render_context,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_material_bind(
    const openusd_stage* stage,
    const char* prim_path,
    const char* material_path,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_material_bind_ext(
    const openusd_stage* stage,
    const char* prim_path,
    const char* material_path,
    openusd_shade_binding_strength strength,
    openusd_shade_material_purpose purpose,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_shade_material_bind_collection(
    const openusd_stage* stage,
    const char* prim_path,
    const char* collection_prim_path,
    const char* collection_name,
    const char* material_path,
    const char* binding_name,
    openusd_shade_binding_strength strength,
    openusd_shade_material_purpose purpose,
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

OPENUSD_DOTNET_API openusd_status openusd_shade_get_bound_material(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_shade_material_purpose purpose,
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

/* ---- UsdPhysics schema facade exports ---- */
OPENUSD_DOTNET_API openusd_status openusd_physics_is_schema(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_schema_kind schema_kind,
    int32_t* is_schema,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_physics_define(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_schema_kind schema_kind,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_physics_has_api(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_api_kind api_kind,
    const char* instance_name,
    int32_t* has_api,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_physics_apply_api(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_api_kind api_kind,
    const char* instance_name,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_physics_set_float(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_float_property property,
    const char* instance_name,
    float value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_physics_get_float(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_float_property property,
    const char* instance_name,
    float* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_physics_set_bool(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_bool_property property,
    int32_t value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_physics_get_bool(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_bool_property property,
    int32_t* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_physics_set_vec3f(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_vec3f_property property,
    openusd_vec3f value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_physics_get_vec3f(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_vec3f_property property,
    openusd_vec3f* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_physics_set_quatf(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_quatf_property property,
    openusd_quatf value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_physics_get_quatf(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_quatf_property property,
    openusd_quatf* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_physics_set_token(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_token_property property,
    const char* instance_name,
    const char* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_physics_get_token(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_token_property property,
    const char* instance_name,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_physics_set_string(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_string_property property,
    const char* value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_physics_get_string(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_physics_string_property property,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_set_skinning_method(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_skinning_method method,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_get_skinning_method(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_skinning_method* method,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_set_blend_shapes(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_string_list_view* names,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_get_blend_shapes(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_set_blend_shape_targets(
    const openusd_stage* stage,
    const char* prim_path,
    const openusd_string_list_view* targets,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_get_blend_shape_targets(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_set_blend_shape_vec3(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_blend_shape_vec3_property property,
    const openusd_vec3f* values,
    size_t count,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_get_blend_shape_vec3(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_skel_blend_shape_vec3_property property,
    openusd_vec3f* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_set_blend_shape_point_indices(
    const openusd_stage* stage,
    const char* prim_path,
    const int32_t* values,
    size_t count,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_get_blend_shape_point_indices(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t* values,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_set_blend_shape_inbetween(
    const openusd_stage* stage,
    const char* prim_path,
    const char* inbetween_name,
    float weight,
    const openusd_vec3f* offsets,
    size_t offset_count,
    const openusd_vec3f* normal_offsets,
    size_t normal_offset_count,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_get_blend_shape_inbetween_names(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_string_list** list,
    openusd_string_list_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_skel_get_blend_shape_inbetween(
    const openusd_stage* stage,
    const char* prim_path,
    const char* inbetween_name,
    float* weight,
    openusd_vec3f* offsets,
    size_t offset_capacity,
    size_t* offset_required,
    openusd_vec3f* normal_offsets,
    size_t normal_offset_capacity,
    size_t* normal_offset_required,
    openusd_error_buffer* error);
OPENUSD_DOTNET_API void openusd_string_list_release(openusd_string_list* list);
OPENUSD_DOTNET_API void openusd_payload_arc_list_release(openusd_payload_arc_list* list);
OPENUSD_DOTNET_API void openusd_pcp_prim_index_list_release(openusd_pcp_prim_index_list* list);

OPENUSD_DOTNET_API openusd_status openusd_ts_spline_create(
    openusd_ts_spline** spline,
    openusd_error_buffer* error);
OPENUSD_DOTNET_API void openusd_ts_spline_release(openusd_ts_spline* spline);
OPENUSD_DOTNET_API openusd_status openusd_ts_spline_set_data(
    openusd_ts_spline* spline,
    const openusd_ts_spline_data_view* view,
    openusd_error_buffer* error);
OPENUSD_DOTNET_API openusd_status openusd_ts_spline_get_data(
    const openusd_ts_spline* spline,
    openusd_ts_spline_data_view* view,
    openusd_error_buffer* error);
OPENUSD_DOTNET_API openusd_status openusd_ts_spline_eval(
    const openusd_ts_spline* spline,
    double time,
    double* value,
    int32_t* has_value,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_validation_get_registered_validators(
    openusd_validation_metadata_list** list,
    openusd_validation_metadata_view* view,
    openusd_error_buffer* error);
OPENUSD_DOTNET_API void openusd_validation_metadata_list_release(
    openusd_validation_metadata_list* list);
OPENUSD_DOTNET_API openusd_status openusd_validation_validate_stage(
    const openusd_stage* stage,
    openusd_validation_error_list** list,
    openusd_validation_error_view* view,
    openusd_error_buffer* error);
OPENUSD_DOTNET_API openusd_status openusd_validation_validate_prim(
    const openusd_stage* stage,
    const char* prim_path,
    openusd_validation_error_list** list,
    openusd_validation_error_view* view,
    openusd_error_buffer* error);
OPENUSD_DOTNET_API void openusd_validation_error_list_release(openusd_validation_error_list* list);

#ifdef __cplusplus
}
#endif

#endif
