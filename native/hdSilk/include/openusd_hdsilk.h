// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_HDSILK_H
#define OPENUSD_HDSILK_H

#include "openusd_dotnet.h"
#include "openusd_render_camera.h"

#if defined(_WIN32)
#if defined(OPENUSD_HDSILK_BUILD)
#define OPENUSD_HDSILK_API __declspec(dllexport)
#else
#define OPENUSD_HDSILK_API __declspec(dllimport)
#endif
#else
#define OPENUSD_HDSILK_API __attribute__((visibility("default")))
#endif

#if defined(__cplusplus)
#define OPENUSD_HDSILK_NOEXCEPT noexcept
#else
#define OPENUSD_HDSILK_NOEXCEPT
#endif

#ifdef __cplusplus
extern "C" {
#endif

/// ABI version of the openusd_silk_page_view struct and the wire format
/// written into its data buffer. Bump whenever either changes in a way that
/// is not purely additive.
#define OPENUSD_SILK_PAGE_ABI_VERSION 11u
#define OPENUSD_SILK_SESSION_ABI_VERSION 4u

/// Command types written into openusd_silk_page_view::data. Every command
/// starts with a little-endian uint32 "type" followed by a little-endian
/// uint32 "byte_size" that includes this 8-byte header and all payload
/// bytes that follow. The stream never embeds native pointers; paths are
/// UTF-8 byte sequences with an explicit length prefix and no NUL
/// terminator. Every integer, float, double, and array element below is
/// little-endian and fields are packed without implicit alignment padding.
///
/// FRAME (type = 1):
///   int32  width
///   int32  height
///   double view_matrix[16]        (row-major)
///   double projection_matrix[16]  (row-major)
///   uint32 clip_plane_count       (0..8)
///   uint32 reserved               (0)
///   double clip_planes[8][4]      (eye-space planes, unused entries zero)
///
/// MESH_UPSERT (type = 2):
///   Offset Size Type     Field
///        8    8 uint64   stable_id_hash
///       16    4 int32    prim_id
///       20    4 int32    instance_id
///       24    4 int32    instance_index
///       28    4 uint32   topology_kind
///       32    8 uint64   topology_revision
///       40    4 uint32   double_sided (0 or 1)
///       44    4 uint32   cull_style (OPENUSD_SILK_CULL_STYLE_*)
///       48    4 uint32   path_byte_count
///       52    4 uint32   point_count
///       56    4 uint32   index_count
///       60    4 uint32   triangle_count
///       64   16 float    display_color[4]
///       80  128 double   transform[16] (row-major)
///      208    8 uint64   material_binding_hash
///      216    4 uint32   material_path_byte_count
///      220    4 uint32   attribute_count
///      224    * uint8    path[path_byte_count] (UTF-8, no NUL)
///        *    * float    points[point_count * 3]
///        *    * uint32   indices[index_count] (triangle list)
///        *    * uint32   triangle_subprims[triangle_count]
///        *    * uint8    material_path[material_path_byte_count]
///        *    *          attributes[attribute_count]
///
/// ABI v4 adds the vertex attribute table and the material binding. Every fixed
/// offset up to and including transform is unchanged from v3; only the variable
/// section moved, so the additions are structural rather than a re-layout.
///
/// Each attribute entry is:
///        0    4 uint32   semantic (OPENUSD_SILK_ATTRIBUTE_*)
///        4    4 uint32   component_count (1..4)
///        8    4 uint32   interpolation (OPENUSD_SILK_INTERPOLATION_*)
///       12    4 uint32   name_byte_count
///       16    4 uint32   element_count
///       20    * uint8    name[name_byte_count] (UTF-8, no NUL)
///        *    * float    data[element_count * component_count]
///
/// The table is how every per-vertex value other than position travels, so
/// authored normals, texture coordinates and arbitrary primvars all use one
/// mechanism and a new attribute needs no further ABI bump. Data is always
/// float and always already resolved to the emitted triangle-list vertices, so
/// element_count equals point_count for vertex interpolation and 1 for constant
/// interpolation. faceVarying primvars are triangulated through HdMeshUtil, and
/// uniform primvars are expanded from authored-face values through the
/// triangle_subprims mapping. Interpolations that cannot be resolved are omitted
/// entirely rather than guessed at, so a consumer sees an absent attribute
/// instead of silently wrong data.
///
/// Every entry carries its authored primvar name, whatever its semantic. The
/// name is required rather than decorative: a mesh may carry several texture
/// coordinate sets and a UsdUVTexture reader selects one of them by name, so a
/// nameless TEXCOORD entry could not be bound. Entries are sorted by name, so an
/// unchanged scene produces byte-identical pages, which the reproducibility and
/// parity evidence depend on. The semantic tells a renderer what an entry means
/// by contract; OPENUSD_SILK_ATTRIBUTE_CUSTOM means only the name identifies it.
///
/// material_binding_hash is 64-bit FNV-1a over the exact bound material path
/// bytes, and is zero with an empty path when the mesh has no material binding.
/// It is an index only: material_path stays authoritative, exactly as prim path
/// does for mesh identity.
///
/// ABI v6 adds double_sided and cull_style. They are the resolved Hydra mesh
/// render state; consumers use them to match Storm's authored single-sided
/// culling while keeping double-sided meshes uncullled.
///
/// ABI v7 extends FRAME with camera clip planes. Mesh and material command
/// layouts are unchanged.
///
/// ABI v8 stops duplicating point-instancer prototype geometry. Instance index
/// zero still carries the full mesh payload and per-instance transform. Later
/// records for the same path carry instance_id, instance_index, render state,
/// display_color and transform, with point_count, index_count, triangle_count,
/// material_path_byte_count and attribute_count all zero; consumers reuse the
/// instance-zero prototype geometry, material path and attributes.
///
/// ABI v9 extends FRAME with a fixed light table. hdSilk currently publishes
/// direct DistantLight, SphereLight, RectLight, DiskLight and CylinderLight
/// entries plus DomeLight as ambient only. The table is frame-local because
/// lights are evaluated in eye space by the managed renderer after applying
/// the current view matrix.
///
/// FRAME v9 appends after clip_planes:
///   uint32 light_count (0..4 direct lights)
///   uint32 reserved[3] (0)
///   repeated 4 times:
///     uint32 light_type (OPENUSD_SILK_LIGHT_*)
///     uint32 shadow_enabled (diagnostic only)
///     float shape_x
///     float shape_y
///     float color[3]
///     float intensity
///     double transform[16] (row-major)
///     float exposure
///     float diffuse
///     float specular
///     float radius
///   float ambient_color[3]
///   float ambient_intensity
///
/// ABI v10 appends a generated MaterialX fragment SPIR-V payload to
/// MATERIAL_UPSERT. The existing fixed header, scalar table and texture table
/// stay byte-for-byte identical; consumers read the generated payload after the
/// texture table.
///
/// All offsets are from the command header's first byte. stable_id_hash is
/// 64-bit FNV-1a over the exact path bytes and is an index only: path is the
/// authoritative identity. prim_id is Hydra's explicit non-negative Rprim
/// identifier. topology_kind is OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST or
/// OPENUSD_SILK_TOPOLOGY_LINE_LIST. For triangle lists, index_count must equal
/// triangle_count * 3 and each triangle_subprims entry is the authored USD face
/// index decoded from HdMeshUtil primitiveParams for the corresponding emitted
/// triangle. For line lists, index_count must equal triangle_count * 2 and each
/// triangle_subprims entry is the authored USD curve segment index for the
/// corresponding emitted line. topology_revision starts at 1 and changes only
/// when Hydra reports dirty topology.
///
/// In ABI v3 instance identity is meaningful. A prim with no instancer
/// publishes exactly one full record with instance_id and instance_index both
/// zero. A point-instanced prototype publishes one record per resolved
/// instance: path stays the authoritative prototype path, instance_index is the
/// zero-based instance ordinal, and instance_id is a stable non-zero diagnostic
/// identifier for the owning instancer. Consumers must therefore key retained
/// meshes by (path, instance_index) rather than by path alone. Since ABI v8,
/// only instance zero carries geometry; later records carry their own fully
/// resolved transform and reuse instance zero's geometry.
///
/// MESH_REMOVE (type = 3):
///   uint64 stable_id_hash
///   int32  instance_index
///   uint32 path_byte_count
///   uint8  path[path_byte_count]  (UTF-8, no NUL)
///
/// A removal retires exactly one (path, instance_index) identity, so a
/// shrinking instancer emits one removal per dropped instance.
///
/// MATERIAL_UPSERT (type = 4), added by ABI v5:
///   Offset Size Type     Field
///        8    8 uint64   material_id_hash
///       16    4 uint32   path_byte_count
///       20    4 uint32   surface_kind (OPENUSD_SILK_SURFACE_*)
///       24    4 uint32   scalar_count
///       28    4 uint32   texture_count
///       32    * uint8    path[path_byte_count] (UTF-8, no NUL)
///        *    *          scalars[scalar_count]
///        *    *          textures[texture_count]
///        *    4 uint32   generated_fragment_spirv_byte_count
///        *    * uint8    generated_fragment_spirv_bytes
///        *    4 uint32   generated_fragment_msl_source_byte_count
///        *    * uint8    generated_fragment_msl_source_bytes
///
/// Each scalar entry is:
///        0    4 uint32   parameter (OPENUSD_SILK_MATERIAL_*)
///        4    4 uint32   component_count (1..4)
///        8    * float    value[component_count]
///
/// Each texture entry is:
///        0    4 uint32   parameter (OPENUSD_SILK_MATERIAL_*)
///        4    4 uint32   wrap_s (OPENUSD_SILK_WRAP_*)
///        8    4 uint32   wrap_t (OPENUSD_SILK_WRAP_*)
///       12    4 uint32   source_color_space (OPENUSD_SILK_COLOR_SPACE_*)
///       16    4 uint32   asset_byte_count
///       20    4 uint32   uv_primvar_byte_count
///       24    4 uint32   component_count (1..4)
///       28   16 float    scale[4]
///       44   16 float    bias[4]
///       60   16 float    fallback[4]
///       76    * uint8    asset[asset_byte_count] (UTF-8, resolved, no NUL)
///        *    * uint8    uv_primvar[uv_primvar_byte_count] (UTF-8, no NUL)
///
/// Like the vertex attribute table, both tables are keyed by an explicit
/// parameter id, so supporting a further UsdPreviewSurface input needs a new
/// id rather than another ABI bump. A parameter appears in at most one table:
/// the scalar table carries its authored constant, the texture table carries
/// its connected UsdUVTexture, and a parameter present in neither is left at
/// the consumer's documented UsdPreviewSurface default.
///
/// surface_kind is OPENUSD_SILK_SURFACE_UNSUPPORTED when the bound network is
/// not a UsdPreviewSurface. Such a material is still published, with empty
/// tables, so the consumer can diagnose it visibly instead of silently
/// approximating an unsupported shading graph.
///
/// material_id_hash is 64-bit FNV-1a over the exact path bytes and is an index
/// only: path stays the authoritative identity, exactly as for meshes. It
/// matches MESH_UPSERT's material_binding_hash for the same path.
///
/// MATERIAL_REMOVE (type = 5), added by ABI v5:
///   uint64 material_id_hash
///   uint32 path_byte_count
///   uint8  path[path_byte_count]  (UTF-8, no NUL)
#define OPENUSD_SILK_COMMAND_FRAME 1u
#define OPENUSD_SILK_COMMAND_MESH_UPSERT 2u
#define OPENUSD_SILK_COMMAND_MESH_REMOVE 3u
#define OPENUSD_SILK_COMMAND_MATERIAL_UPSERT 4u
#define OPENUSD_SILK_COMMAND_MATERIAL_REMOVE 5u
#define OPENUSD_SILK_MAX_FRAME_LIGHTS 4u
#define OPENUSD_SILK_LIGHT_DISTANT 1u
#define OPENUSD_SILK_LIGHT_SPHERE 2u
#define OPENUSD_SILK_LIGHT_RECT 3u
#define OPENUSD_SILK_LIGHT_DISK 4u
#define OPENUSD_SILK_LIGHT_CYLINDER 5u
#define OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST 1u
#define OPENUSD_SILK_TOPOLOGY_LINE_LIST 2u
#define OPENUSD_SILK_TOPOLOGY_POINT_LIST 3u

#define OPENUSD_SILK_CULL_STYLE_DONT_CARE 0u
#define OPENUSD_SILK_CULL_STYLE_NOTHING 1u
#define OPENUSD_SILK_CULL_STYLE_BACK 2u
#define OPENUSD_SILK_CULL_STYLE_FRONT 3u
#define OPENUSD_SILK_CULL_STYLE_BACK_UNLESS_DOUBLE_SIDED 4u
#define OPENUSD_SILK_CULL_STYLE_FRONT_UNLESS_DOUBLE_SIDED 5u

/// Surface models the material table can describe. UNSUPPORTED is published
/// rather than omitted so an unsupported graph is diagnosable.
#define OPENUSD_SILK_SURFACE_UNSUPPORTED 0u
#define OPENUSD_SILK_SURFACE_PREVIEW_SURFACE 1u
#define OPENUSD_SILK_SURFACE_MATERIALX_PROJECTED 2u
#define OPENUSD_SILK_SURFACE_MATERIALX_GENERATED 3u

/// UsdPreviewSurface inputs carried by the ABI v5 material tables.
#define OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR 1u
#define OPENUSD_SILK_MATERIAL_EMISSIVE_COLOR 2u
#define OPENUSD_SILK_MATERIAL_SPECULAR_COLOR 3u
#define OPENUSD_SILK_MATERIAL_METALLIC 4u
#define OPENUSD_SILK_MATERIAL_ROUGHNESS 5u
#define OPENUSD_SILK_MATERIAL_CLEARCOAT 6u
#define OPENUSD_SILK_MATERIAL_CLEARCOAT_ROUGHNESS 7u
#define OPENUSD_SILK_MATERIAL_OPACITY 8u
#define OPENUSD_SILK_MATERIAL_OPACITY_THRESHOLD 9u
#define OPENUSD_SILK_MATERIAL_IOR 10u
#define OPENUSD_SILK_MATERIAL_NORMAL 11u
#define OPENUSD_SILK_MATERIAL_DISPLACEMENT 12u
#define OPENUSD_SILK_MATERIAL_OCCLUSION 13u
#define OPENUSD_SILK_MATERIAL_USE_SPECULAR_WORKFLOW 14u

/// UsdUVTexture wrap modes.
#define OPENUSD_SILK_WRAP_BLACK 0u
#define OPENUSD_SILK_WRAP_CLAMP 1u
#define OPENUSD_SILK_WRAP_REPEAT 2u
#define OPENUSD_SILK_WRAP_MIRROR 3u

/// UsdUVTexture sourceColorSpace values.
#define OPENUSD_SILK_COLOR_SPACE_AUTO 0u
#define OPENUSD_SILK_COLOR_SPACE_RAW 1u
#define OPENUSD_SILK_COLOR_SPACE_SRGB 2u

/// Vertex attribute semantics carried by the ABI v4 attribute table. CUSTOM
/// means the entry is identified by its authored primvar name alone.
#define OPENUSD_SILK_ATTRIBUTE_CUSTOM 0u
#define OPENUSD_SILK_ATTRIBUTE_NORMAL 1u
#define OPENUSD_SILK_ATTRIBUTE_TEXCOORD 2u
#define OPENUSD_SILK_ATTRIBUTE_COLOR 3u
#define OPENUSD_SILK_ATTRIBUTE_TANGENT 4u

/// Interpolation of an attribute, already resolved onto emitted triangle-list
/// vertices. CONSTANT carries exactly one element for the whole mesh.
#define OPENUSD_SILK_INTERPOLATION_CONSTANT 0u
#define OPENUSD_SILK_INTERPOLATION_VERTEX 1u

/// Renderer-neutral complexity levels. The page wire format is unchanged:
/// complexity only changes how hdSilk emits curve and point records.
#define OPENUSD_SILK_COMPLEXITY_LOW 0u
#define OPENUSD_SILK_COMPLEXITY_MEDIUM 1u
#define OPENUSD_SILK_COMPLEXITY_HIGH 2u
#define OPENUSD_SILK_COMPLEXITY_VERY_HIGH 3u

typedef struct openusd_silk_session openusd_silk_session;
typedef struct openusd_silk_page openusd_silk_page;

/// A native-owned, immutable view over a single serialized page of wire
/// commands. The memory referenced by "data" remains valid until the
/// owning openusd_silk_page is released with openusd_silk_page_release.
typedef struct openusd_silk_page_view
{
    uint32_t struct_size;
    uint32_t abi_version;
    uint64_t revision;
    const uint8_t* data;
    size_t data_size;
    uint32_t command_count;
} openusd_silk_page_view;

/// Compatibility path API. Registers hdSilk, opens a temporary data-stage
/// handle, and delegates to openusd_silk_session_create_from_stage.
OPENUSD_HDSILK_API openusd_status openusd_silk_session_create(
    const char* plugin_path,
    const char* stage_path,
    openusd_silk_session** session,
    openusd_error_buffer* error);

/// Creates hdSilk from the exact stage object and retains its shared stage
/// core. Sync and teardown acquire stage access; returned pages own immutable
/// copied bytes and remain valid independently until page release.
OPENUSD_HDSILK_API openusd_status openusd_silk_session_create_from_stage(
    const char* plugin_path,
    openusd_stage* stage,
    openusd_silk_session** session,
    openusd_error_buffer* error);

/// ABI-compatible non-throwing release. If stage access cannot be acquired,
/// the session remains allocated rather than being torn down unsafely.
OPENUSD_HDSILK_API void openusd_silk_session_release(
    openusd_silk_session* session) OPENUSD_HDSILK_NOEXCEPT;

/// Destroys a session after acquiring shared-stage access. Failure leaves the
/// session intact and retryable.
OPENUSD_HDSILK_API openusd_status openusd_silk_session_destroy(
    openusd_silk_session* session,
    openusd_error_buffer* error);

/// Renders one frame at the given viewport size and time code, then returns
/// a native-owned page describing everything that changed (plus the
/// current frame state) since the previous call. The page must be released
/// with openusd_silk_page_release once the caller is done reading *view.
/// Matrix mode writes the exact caller-supplied doubles into the FRAME command.
OPENUSD_HDSILK_API openusd_status openusd_silk_session_sync(
    openusd_silk_session* session,
    int32_t width,
    int32_t height,
    double time_code,
    const openusd_render_camera* camera,
    openusd_silk_page** page,
    openusd_silk_page_view* view,
    openusd_error_buffer* error);

/// Same as openusd_silk_session_sync, with an explicit renderer-neutral complexity
/// for curve and point tessellation density. Subdivision refinement remains at the
/// historical hdSilk baseline and is not controlled by this setting.
OPENUSD_HDSILK_API openusd_status openusd_silk_session_sync_with_complexity(
    openusd_silk_session* session,
    int32_t width,
    int32_t height,
    double time_code,
    const openusd_render_camera* camera,
    uint32_t complexity,
    openusd_silk_page** page,
    openusd_silk_page_view* view,
    openusd_error_buffer* error);

OPENUSD_HDSILK_API void openusd_silk_page_release(
    openusd_silk_page* page) OPENUSD_HDSILK_NOEXCEPT;

OPENUSD_HDSILK_API openusd_status openusd_silk_session_get_renderer_name(
    const openusd_silk_session* session,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error);

#ifdef __cplusplus
}
#endif

#endif
