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
#define OPENUSD_SILK_PAGE_ABI_VERSION 15u
#define OPENUSD_SILK_SESSION_ABI_VERSION 5u

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
/// ABI v8 stops duplicating point-instancer prototype geometry. The first
/// record a path publishes in a page carries the full mesh payload and its own
/// per-instance transform. Later records for the same path carry instance_id,
/// instance_index, render state, display_color and transform, with point_count,
/// index_count, triangle_count, material_path_byte_count and attribute_count
/// all zero; consumers reuse the payload record's geometry, material path and
/// attributes. The elision is topology neutral: a line list emitted from
/// instanced BasisCurves and a point list emitted from instanced Points carry
/// their payload once in exactly the same way a triangle list does.
///
/// Since ABI v14 that payload record is the lowest instance_index the path
/// publishes, which is not necessarily zero: instance_index is the instance's
/// own index inside its instancer, so a prototype that covers only part of an
/// instancer publishes a sparse set of indices. With authored proto indices
/// [0, 1, 0, 1] the second prototype owns instancer instances 1 and 3 and never
/// publishes an index-zero record at all. A consumer must therefore key the
/// prototype payload by the first record a path publishes rather than by index
/// zero. Records of one path arrive in ascending instance_index order, and a
/// page that moves the payload to a new index publishes the new payload record
/// before retiring the old identity.
///
/// Because a payload record is what every later record of its path refers to,
/// the records of one path are all-or-nothing within a page. hdSilk serializes
/// them atomically: if any record of a path cannot be serialized, every record
/// of that path is rolled back out of the page, so an instance reference is
/// never published without the payload record it reuses. Other paths are
/// independent and still serialize, and consumers keep whatever they already
/// retained for the dropped path.
///
/// A record whose geometry is empty is therefore never published at all. An
/// empty record is byte-identical to an instance reference on the wire, so a
/// prim with no points or no indices is retired with RemoveMesh rather than
/// emitted; otherwise the payload record of an empty point-instanced prototype
/// would itself look like a record reusing a payload.
///
/// Every record is validated as hdSilk holds it, before the draw mode and
/// complexity transforms index into its points and vertex attributes, and then
/// again after those transforms rebuild the topology. Validating only the
/// transformed record would let a malformed index -- one that the wireframe
/// draw mode carries through unchanged and complexity then dereferences at both
/// endpoints of every subdivided line -- read out of bounds on its way to being
/// rejected.
///
/// ABI v9 extends FRAME with a fixed light table. hdSilk currently publishes
/// direct DistantLight, SphereLight, RectLight, DiskLight and CylinderLight
/// entries plus DomeLight as ambient only. The table is frame-local because
/// lights are evaluated in eye space by the managed renderer after applying
/// the current view matrix.
///
/// FRAME v9 appends after clip_planes. ABI v12 expands the fixed direct-light
/// table from four to eight entries without changing an entry's layout:
///   uint32 light_count (0..8 direct lights)
///   uint32 reserved[3] (0)
///   repeated 8 times:
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
/// A line list emitted from linear segmented basis curves carries the authored
/// widths in the attribute table under the name "widths" with semantic
/// OPENUSD_SILK_ATTRIBUTE_WIDTH and one component. Constant widths publish one
/// CONSTANT element; uniform, varying, and vertex widths are resolved onto the
/// emitted line vertices and publish one VERTEX element each. Vertex widths are
/// parallel to the points array and are indexed by the resolved point index, so
/// an indexed topology reads the same slot for the width as for the position;
/// an unindexed topology resolves a point by its flattened control-point
/// ordinal, so it accepts a widths array sized either to that control-point
/// count or to a longer points array, both of which index identically.
/// Every VERTEX attribute, widths included, is interpolated at the subdivision
/// parameter when complexity splits a segment, so an authored ramp stays a ramp.
/// The widths do not change the emitted line geometry: every supported backend
/// rasterizes a line at exactly one pixel, which is what Storm does for linear
/// curves at this refinement, so a consumer that wants ribbons must build them
/// itself from these values.
///
/// In ABI v3 instance identity is meaningful. A prim with no instancer
/// publishes exactly one full record with instance_id and instance_index both
/// zero. A point-instanced prototype publishes one record per resolved
/// instance: path stays the authoritative prototype path, instance_index
/// identifies the instance, and instance_id is a stable non-zero diagnostic
/// identifier for the owning instancer. Consumers must therefore key retained
/// meshes by (path, instance_index) rather than by path alone. Since ABI v8,
/// only the payload record carries geometry; later records carry their own
/// fully resolved transform and reuse that record's geometry.
///
/// Since ABI v14 instance_index is the instance's own index inside its
/// instancer -- the index into the point instancer's protoIndices and positions
/// arrays, which is what UsdImaging decodes back to a scene instance -- rather
/// than the ordinal of the instance in the resolved array. The two differ
/// whenever an instancer has several prototypes, whenever proto indices change
/// over time, and whenever invisibleIds hides an instance; only the former
/// survives those edits, so a retained pick identity keeps naming the same
/// scene instance instead of silently re-pointing at another one. Nested
/// instancers have no such USD index, so hdSilk composes one as
/// parent_index * inner_instance_count + inner_index, which is unique and
/// equally stable but is an hdSilk encoding rather than a USD instance index.
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
///        *   24 float    uv_transform[6]
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
///       76    4 uint32   output_channel (OPENUSD_SILK_TEXTURE_CHANNEL_*)
///       80    4 uint32   composite_op (OPENUSD_SILK_COMPOSITE_*)
///       84    4 float    composite_factor
///       88    * uint8    asset[asset_byte_count] (UTF-8, resolved, no NUL)
///        *    * uint8    uv_primvar[uv_primvar_byte_count] (UTF-8, no NUL)
///
/// ABI v13 adds output_channel. It is the resolved output port of the connected
/// UsdUVTexture -- the "r", "g", "b", "a" or "rgb" token the surface input is
/// wired to -- and it is required rather than optional: a consumer cannot infer
/// which channel of a shared file feeds which input, and two inputs connected to
/// different outputs of one texture prim are otherwise indistinguishable from two
/// inputs connected to two separate prims. A scalar input (component_count 1) must
/// carry a single-channel output and a colour or vector input (component_count 3
/// or more) must carry OPENUSD_SILK_TEXTURE_CHANNEL_RGB; any other pairing, and
/// any output token this delegate does not model, is rejected with a diagnostic
/// rather than guessed at. Every other field keeps its ABI v5 offset, so only the
/// variable section moved.
///
/// wrap_s, wrap_t, and fallback are resolved from whichever schema the connected
/// image node belongs to, not from one fixed set of names. A UsdUVTexture states
/// them as wrapS/wrapT and fallback, and an unauthored wrap resolves to
/// OPENUSD_SILK_WRAP_BLACK. A MaterialX ND_image_* states them as uaddressmode,
/// vaddressmode, and default, and an unauthored address mode resolves to
/// OPENUSD_SILK_WRAP_REPEAT because MaterialX defaults both axes to periodic.
/// MaterialX "constant" addressing is carried as OPENUSD_SILK_WRAP_BLACK, which
/// is the wire's "neither periodic nor mirrored" mode. That is an approximation
/// and is documented as one: see OPENUSD_SILK_WRAP_BLACK below. An address mode
/// outside the four MaterialX names is rejected with a diagnostic and the input
/// keeps its documented default.
///
/// ABI v14 appends uv_transform, the one constant texture-coordinate transform
/// every texture of this material samples through, as the row-major affine
/// (m00, m01, m10, m11, tx, ty) applied as u' = m00*u + m01*v + tx and
/// v' = m10*u + m11*v + ty. It is the identity (1, 0, 0, 1, 0, 0) unless the
/// graph routes texture coordinates through a chain of MaterialX place2d or
/// UsdPreviewSurface UsdTransform2d nodes whose inputs are all constant, in
/// which case that chain is folded exactly and composed into one affine,
/// including place2d's SRT and TRS operation orders and UsdTransform2d's
/// opposite, counter-clockwise rotation sense. hdSilk carries one transform per
/// material because the renderer samples every texture of a material from one
/// coordinate stream, and it reconciles both halves of that stream: a texture
/// asking for a different transform, or naming a different uv_primvar, than the
/// material's first texture is dropped with a diagnostic rather than sampled
/// with coordinates it did not author. Every uv_primvar surviving in one
/// MATERIAL_UPSERT is therefore either equal to the material's stream or empty,
/// which is what lets a consumer take the stream from the first entry that names
/// one. A chain with a non-constant input, a
/// chain passing through any other node, and a chain that never reaches a
/// coordinate node are likewise rejected with a diagnostic and leave the input
/// at the documented default, because folding part of an authored chain would
/// render coordinates the graph never produced.
///
/// Like the vertex attribute table, both tables are keyed by an explicit
/// parameter id, so supporting a further UsdPreviewSurface input needs a new
/// id rather than another ABI bump. A parameter appears in at most one table:
/// the scalar table carries its authored constant, the texture table carries
/// its connected UsdUVTexture, and a parameter present in neither is left at
/// the consumer's documented UsdPreviewSurface default. Two inputs may name the
/// same asset with different output_channel values, which is exactly how a packed
/// occlusion/roughness/metallic file is authored.
///
/// ABI v15 appends composite_op and composite_factor, which is how one surface
/// input is driven by *two* images combined per pixel.
///
/// composite_op is OPENUSD_SILK_COMPOSITE_NONE on an ordinary entry. An entry
/// whose composite_op is anything else is the *second* operand of the parameter
/// it names, and the texture table must also carry exactly one entry for that
/// same parameter with OPENUSD_SILK_COMPOSITE_NONE, which is the first operand.
/// The consumer combines them per pixel, after each entry's own scale and bias:
///
///   MULTIPLY  primary * composite
///   ADD       primary + composite
///   SUBTRACT  primary - composite
///   MIX       primary * (1 - composite_factor) + composite * composite_factor
///
/// composite_factor is meaningful only for MIX and is zero otherwise. The
/// combination is evaluated in the shader in floating point, so unlike the
/// per-entry scale and bias -- which are applied per texel when the image is
/// decoded into its own storage format -- it is not clamped by an eight-bit
/// source and no unit-range restriction applies to it.
///
/// A texture table therefore holds at most two entries per parameter: one
/// primary and one composite. It holds at most one composite entry in total,
/// because the consumer binds exactly one composite texture per material rather
/// than one per surface input. A material whose graph asks to composite a second
/// parameter has *both* entries of that second parameter dropped with a
/// diagnostic, leaving that input at its documented default; publishing only its
/// first operand would render one of two authored images and look plausible.
/// Both operands read the material's single texture-coordinate stream, which the
/// uv_primvar reconciliation above already guarantees.
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
#define OPENUSD_SILK_MAX_FRAME_LIGHTS 8u
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
#define OPENUSD_SILK_SURFACE_VOLUME_DENSITY 4u

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
#define OPENUSD_SILK_MATERIAL_VOLUME_DENSITY 15u

/// Texture wrap modes.
///
/// OPENUSD_SILK_WRAP_BLACK is the value published for UsdUVTexture's
/// "black"/unauthored wrap and for MaterialX "constant" addressing. Its name
/// describes what those schemas ask for, not what this renderer currently does:
/// no supported backend is given a border colour, and every consumer in this
/// repository resolves BLACK to clamp-to-edge, so it renders identically to
/// OPENUSD_SILK_WRAP_CLAMP. A sample outside the unit range therefore returns
/// the edge texel rather than black or the MaterialX node's `default` value.
/// The wire carries no border colour at all, so a consumer that wants true
/// border sampling needs a new field and an ABI bump rather than a different
/// reading of this one. The two values stay distinct because they record
/// different authored intent, which a consumer that does implement border
/// sampling must be able to tell apart.
#define OPENUSD_SILK_WRAP_BLACK 0u
#define OPENUSD_SILK_WRAP_CLAMP 1u
#define OPENUSD_SILK_WRAP_REPEAT 2u
#define OPENUSD_SILK_WRAP_MIRROR 3u

/// UsdUVTexture sourceColorSpace values.
#define OPENUSD_SILK_COLOR_SPACE_AUTO 0u
#define OPENUSD_SILK_COLOR_SPACE_RAW 1u
#define OPENUSD_SILK_COLOR_SPACE_SRGB 2u

/// Connected UsdUVTexture output ports, carried by the ABI v13 texture table.
/// These are exactly the outputs UsdUVTexture declares: four single-channel
/// outputs and one three-channel output. A MaterialX image node's single "out"
/// port resolves to RGB for a colour or vector input and to R for a scalar input,
/// which is what its one decoded channel occupies. There is no "unspecified"
/// value: an entry whose output cannot be resolved is rejected, never published.
#define OPENUSD_SILK_TEXTURE_CHANNEL_R 0u
#define OPENUSD_SILK_TEXTURE_CHANNEL_G 1u
#define OPENUSD_SILK_TEXTURE_CHANNEL_B 2u
#define OPENUSD_SILK_TEXTURE_CHANNEL_A 3u
#define OPENUSD_SILK_TEXTURE_CHANNEL_RGB 4u

/// How a texture entry combines with the primary entry of the same parameter.
///
/// NONE marks the primary entry itself. Any other value marks the second operand
/// of a two-image surface input; see the MATERIAL_UPSERT layout above for the
/// exact per-pixel expression and for the one-composite-per-material limit.
#define OPENUSD_SILK_COMPOSITE_NONE 0u
#define OPENUSD_SILK_COMPOSITE_MULTIPLY 1u
#define OPENUSD_SILK_COMPOSITE_ADD 2u
#define OPENUSD_SILK_COMPOSITE_SUBTRACT 3u
#define OPENUSD_SILK_COMPOSITE_MIX 4u

/// Vertex attribute semantics carried by the ABI v4 attribute table. CUSTOM
/// means the entry is identified by its authored primvar name alone.
#define OPENUSD_SILK_ATTRIBUTE_CUSTOM 0u
#define OPENUSD_SILK_ATTRIBUTE_NORMAL 1u
#define OPENUSD_SILK_ATTRIBUTE_TEXCOORD 2u
#define OPENUSD_SILK_ATTRIBUTE_COLOR 3u
#define OPENUSD_SILK_ATTRIBUTE_TANGENT 4u
#define OPENUSD_SILK_ATTRIBUTE_WIDTH 5u

/// Interpolation of an attribute, already resolved onto emitted triangle-list
/// vertices. CONSTANT carries exactly one element for the whole mesh.
#define OPENUSD_SILK_INTERPOLATION_CONSTANT 0u
#define OPENUSD_SILK_INTERPOLATION_VERTEX 1u

/// Renderer-neutral complexity levels. The page wire format is unchanged:
/// complexity only changes how hdSilk emits curve and point records. It applies
/// after the draw mode, so it also subdivides a triangle-list record that the
/// wireframe, hidden-surface-wireframe, or points draw mode converted into
/// lines or points, and changing complexity while one of those draw modes is
/// active republishes those records too.
#define OPENUSD_SILK_COMPLEXITY_LOW 0u
#define OPENUSD_SILK_COMPLEXITY_MEDIUM 1u
#define OPENUSD_SILK_COMPLEXITY_HIGH 2u
#define OPENUSD_SILK_COMPLEXITY_VERY_HIGH 3u

/// Renderer-neutral draw modes matching OpenUsd.Rendering.RenderDrawMode.
#define OPENUSD_SILK_DRAW_MODE_SMOOTH_SHADED 0u
#define OPENUSD_SILK_DRAW_MODE_FLAT_SHADED 1u
#define OPENUSD_SILK_DRAW_MODE_WIREFRAME 2u
#define OPENUSD_SILK_DRAW_MODE_POINTS 3u
#define OPENUSD_SILK_DRAW_MODE_BOUNDS 4u
#define OPENUSD_SILK_DRAW_MODE_WIREFRAME_ON_SURFACE 5u
#define OPENUSD_SILK_DRAW_MODE_GEOM_ONLY 6u
#define OPENUSD_SILK_DRAW_MODE_GEOM_FLAT 7u
#define OPENUSD_SILK_DRAW_MODE_GEOM_SMOOTH 8u
#define OPENUSD_SILK_DRAW_MODE_HIDDEN_SURFACE_WIREFRAME 9u

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

/// Same as openusd_silk_session_sync_with_complexity, with an explicit
/// renderer-neutral draw mode forwarded to UsdImagingGLRenderParams.
OPENUSD_HDSILK_API openusd_status openusd_silk_session_sync_with_complexity_and_draw_mode(
    openusd_silk_session* session,
    int32_t width,
    int32_t height,
    double time_code,
    const openusd_render_camera* camera,
    uint32_t complexity,
    uint32_t draw_mode,
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
