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
#define OPENUSD_SILK_PAGE_ABI_VERSION 3u
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
///
/// MESH_UPSERT (type = 2):
///   Offset Size Type     Field
///        8    8 uint64   stable_id_hash
///       16    4 int32    prim_id
///       20    4 int32    instance_id
///       24    4 int32    instance_index
///       28    4 uint32   topology_kind
///       32    8 uint64   topology_revision
///       40    4 uint32   path_byte_count
///       44    4 uint32   point_count
///       48    4 uint32   index_count
///       52    4 uint32   triangle_count
///       56   16 float    display_color[4]
///       72  128 double   transform[16] (row-major)
///      200    * uint8    path[path_byte_count] (UTF-8, no NUL)
///        *    * float    points[point_count * 3]
///        *    * uint32   indices[index_count] (triangle list)
///        *    * uint32   triangle_subprims[triangle_count]
///
/// All offsets are from the command header's first byte. stable_id_hash is
/// 64-bit FNV-1a over the exact path bytes and is an index only: path is the
/// authoritative identity. prim_id is Hydra's explicit non-negative Rprim
/// identifier. topology_kind is OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST,
/// index_count must equal triangle_count * 3, and each triangle_subprims entry
/// is the authored USD face index decoded from HdMeshUtil primitiveParams for
/// the corresponding emitted triangle. topology_revision starts at 1 and
/// changes only when Hydra reports dirty topology.
///
/// In ABI v3 instance identity is meaningful. A prim with no instancer
/// publishes exactly one record with instance_id and instance_index both zero.
/// A point-instanced prototype publishes one record per resolved instance:
/// path stays the authoritative prototype path, instance_index is the
/// zero-based instance ordinal, and instance_id is a stable non-zero
/// diagnostic identifier for the owning instancer. Consumers must therefore
/// key retained meshes by (path, instance_index) rather than by path alone.
/// Each instance record carries its own fully resolved transform.
///
/// MESH_REMOVE (type = 3):
///   uint64 stable_id_hash
///   int32  instance_index
///   uint32 path_byte_count
///   uint8  path[path_byte_count]  (UTF-8, no NUL)
///
/// A removal retires exactly one (path, instance_index) identity, so a
/// shrinking instancer emits one removal per dropped instance.
#define OPENUSD_SILK_COMMAND_FRAME 1u
#define OPENUSD_SILK_COMMAND_MESH_UPSERT 2u
#define OPENUSD_SILK_COMMAND_MESH_REMOVE 3u
#define OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST 1u

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
