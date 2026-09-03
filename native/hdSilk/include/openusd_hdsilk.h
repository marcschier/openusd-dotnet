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
#define OPENUSD_SILK_PAGE_ABI_VERSION 23u
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
///      224    4 uint32   deformation_flags (OPENUSD_SILK_DEFORMATION_FLAG_*)
///      228    4 uint32   deformation_unsupported
///                        (OPENUSD_SILK_DEFORMATION_UNSUPPORTED_*)
///      232    4 uint32   deformation_byte_count
///      236    4 uint32   subprim_identity (OPENUSD_SILK_SUBPRIM_IDENTITY_*)
///      240    4 uint32   subprim_unsupported
///                        (OPENUSD_SILK_SUBPRIM_UNSUPPORTED_*)
///      244    4 uint32   point_origin_count (0 or point_count)
///      248    4 uint32   corner_edge_count
///      252    4 uint32   authored_edge_count
///      256    4 uint32   authored_point_count
///      260    4 uint32   instancer_path_byte_count
///      264    4 uint32   instancer_context_count
///      268    * uint8    path[path_byte_count] (UTF-8, no NUL)
///        *    * float    points[point_count * 3]
///        *    * uint32   indices[index_count] (triangle list)
///        *    * uint32   triangle_subprims[triangle_count]
///        *    * uint8    material_path[material_path_byte_count]
///        *    *          attributes[attribute_count]
///        *    *          deformation[deformation_byte_count]
///        *    * uint32   point_origins[point_origin_count]
///        *    * uint32   corner_edges[corner_edge_count]
///        *    * uint8    instancer_path[instancer_path_byte_count]
///        *    *          instancer_context[instancer_context_count]
///
/// ABI v23 adds the ordered instancer context. Every fixed offset up to and
/// including instancer_path_byte_count is unchanged from v22; the only fixed
/// change is that the reserved word at offset 264 now carries
/// instancer_context_count, and the only variable change is the trailing block.
///
/// Each instancer context entry is:
///        0    4 uint32   path_byte_count
///        4    4 int32    instance_index
///        8    * uint8    path[path_byte_count] (UTF-8, no NUL)
///
/// Entries are ordered outermost instancer first and innermost last, and each
/// instance_index is that instance's own index inside the instancer named at
/// the same level. This exists because nested instancing has no single "the"
/// instancer: a prototype instanced by an inner instancer that is itself
/// instanced by an outer one has one index per level, and naming only the
/// innermost path beside a composed mixed-radix ordinal describes an instance
/// that does not exist -- the path says one level and the number says another.
///
/// The block is published exactly when instancer_path is non-empty, and its
/// last entry's path always equals instancer_path, so the pre-v23 convenience
/// pair stays exactly what it always was for the overwhelmingly common
/// single-level scene: one entry whose index is instance_index. For a nested
/// context the record's instance_index remains the hdSilk composite ordinal
/// that keys the retained identity table, which is deliberately not the
/// innermost level's own index; a consumer that needs a scene instance must
/// read the chain, which is the only description that decodes back to one.
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
/// ABI v20 appends the optional bounded deformation block, and with it the
/// three fixed fields at offsets 224, 228 and 232 that describe it. Every
/// earlier fixed offset is unchanged; only the variable section moved, exactly
/// as it did for the ABI v4 attribute table.
///
/// The block is renderer-neutral and bulk: it is how one deformed prototype's
/// whole rig crosses this ABI in a single record, so a consumer never calls
/// back per point, per joint, or per blend shape. It never replaces the
/// resolved `points` and `normals` a record already carries. hdSilk always
/// evaluates the supported UsdSkel subset on the CPU and publishes the result,
/// so a consumer that ignores the block renders exactly the ABI v19 image, and
/// a consumer that evaluates the block must reproduce those same points.
///
/// deformation_byte_count is zero when no rig is published, which is the case
/// for every unskinned prim, for every instance-reference record, and for a
/// skinned prim whose rig hdSilk could not describe within this ABI's budgets.
/// deformation_unsupported is then the named reason, so an unsupported rig is
/// diagnosed against a prim rather than silently rendered at its bind pose.
///
/// The block is:
///   Offset Size Type     Field
///        0    4 uint32   joint_count (1..OPENUSD_SILK_MAX_DEFORMATION_JOINTS)
///        4    4 uint32   influences_per_point
///                        (1..OPENUSD_SILK_MAX_DEFORMATION_INFLUENCES)
///        8    4 uint32   bind_point_count (equals point_count)
///       12    4 uint32   blend_range_count
///                        (0..OPENUSD_SILK_MAX_DEFORMATION_BLEND_RANGES)
///       16    4 uint32   blend_delta_count
///                        (0..OPENUSD_SILK_MAX_DEFORMATION_BLEND_DELTAS)
///       20    4 uint32   reserved (0)
///       24    8 uint64   deformation_identity
///       32   64 float    geom_bind_transform[16] (row-major)
///       96    * float    bind_points[bind_point_count * 3]
///        *    * float    bind_normals[bind_point_count * 3]
///                        (present only with OPENUSD_SILK_DEFORMATION_FLAG_BIND_NORMALS)
///        *    * uint32   joint_indices[bind_point_count * influences_per_point]
///        *    * float    joint_weights[bind_point_count * influences_per_point]
///        *    * float    joint_matrices[joint_count * 16] (row-major)
///        *    *          blend_ranges[blend_range_count]
///        *    *          blend_deltas[blend_delta_count]
///
/// Each blend range is:
///        0    4 uint32   first_delta
///        4    4 uint32   delta_count
///        8    4 float    weight
///       12    4 uint32   reserved (0)
///
/// Each blend delta is:
///        0    4 uint32   point_index (< bind_point_count)
///        4   12 float    position_offset[3]
///       16   12 float    normal_offset[3]
///
/// bind_points are the authored bind-pose points of the prototype, before any
/// deformation, so a consumer evaluates the same input hdSilk did rather than
/// re-deriving one from the deformed array travelling beside it. bind_normals
/// are the authored per-point normals, present only when the mesh authors an
/// interpolation a point-indexed deformation can carry.
///
/// joint_matrices is the joint palette already resolved for this record's
/// evaluation time: skeleton-space skinning transforms, remapped into the
/// prim's own joint order, so joint_indices index it directly and the consumer
/// never sees an OpenUSD path or token. joint_weights are the authored weights
/// as authored; hdSilk does not renormalize them, because the CPU result it
/// must match does not either. A rigidly deformed mesh authors constant
/// influences, and hdSilk expands them to one influence set per point so every
/// consumer reads one bounded representation.
///
/// A blend range is one resolved sub-shape: its weight is the sub-shape weight
/// UsdSkel resolved at this time code, so in-between shapes need no range kind
/// of their own -- an authored blend-shape weight expands into the weights of
/// the primary shape and of every in-between it interpolates through before the
/// ranges are published. Ranges whose resolved weight is zero are omitted,
/// which changes no result. Deltas are sparse and carry the authored
/// pointIndices, and carry authored normalOffsets only with
/// OPENUSD_SILK_DEFORMATION_FLAG_BLEND_NORMAL_OFFSETS; the offsets are zero
/// otherwise. There is no tangent or arbitrary-primvar delta because
/// UsdSkelBlendShape authors no such channel.
///
/// The evaluation order a consumer must reproduce, per point p, is:
///   1. b = bind_points[p], plus every range's weighted position_offset for p;
///   2. b = geom_bind_transform * b;
///   3. b = sum over influences i of joint_weights[i] * joint_matrices[i] * b.
/// Normals follow the same order with the inverse transpose of the 3x3 upper
/// left of each matrix, and are renormalized once at the end.
///
/// A normal whose accumulated vector is non-finite, or whose squared length is
/// at most 1e-30, carries no direction. Every consumer resolves it to exactly
/// (0, 0, 1). The fallback is part of the contract rather than an
/// implementation detail: a rig that collapses a normal must collapse it the
/// same way everywhere, or the same rig would verify against one consumer and
/// not against another. Because the fallback is indistinguishable from a
/// genuinely computed +Z, a consumer comparing these normals against a
/// separately resolved array must compare the *degeneracy* as well as the
/// values -- a degeneracy on both sides is nothing to compare, and a degeneracy
/// on exactly one side is a disagreement about the surface.
///
/// deformation_identity is a 64-bit FNV-1a over every byte of the block after
/// this field. It is an index, not the authority: it changes whenever the pose
/// changes, so a consumer keys a retained deformation resource and a retained
/// shadow map by it without comparing whole arrays, and a consumer that
/// compares the arrays reaches the same answer.
///
/// A consumer must recompute it over the bytes it received and refuse a record
/// whose declared value disagrees, before the value reaches any cache. Nothing
/// else in this block detects a payload whose content changed while its index
/// did not, and that is precisely the case in which a changed pose would be
/// drawn through resources keyed on the previous one.
///
/// Every floating value in the block -- the geom bind transform, the bind
/// points and normals, the joint weights and matrices, the range weights and
/// both delta channels -- must be finite, and a consumer must refuse a record
/// carrying one that is not. A non-finite element does not fail loudly: it
/// propagates through the whole evaluation and arrives as a NaN vertex, which
/// every rasterizer silently discards, so the only symptom is a surface that
/// quietly loses triangles with nothing naming the cause.
///
/// ABI v22 appends the bounded subprim-identity tables, and with it the six
/// fixed fields at offsets 236 through 256 that describe them, plus the
/// authoritative instancer path at offset 260. Every earlier fixed offset is
/// unchanged; only the variable section moved, exactly as it did for the ABI v4
/// attribute table and the ABI v20 deformation block.
///
/// instancer_path is the absolute USD path of the instancer this record's
/// instance belongs to, empty for a prim with no instancer. It exists because
/// instance_id is a hash: it tells two instancers apart for diagnostics, but no
/// consumer can turn it back into the path a selection has to name. A pick that
/// resolved one instance of a point-instanced prototype could therefore report
/// the instance index without being able to say which instancer it indexes,
/// which is not an identity a round trip can use. The path is authoritative
/// exactly as the prim path is; instance_id remains an index only.
///
/// The tables exist so a consumer can answer an edge or point pick with the
/// identity the scene authored rather than with an index this delegate
/// generated. `triangle_subprims` already carries authored *face* identity, but
/// nothing on the wire before v22 recovered the other two:
///
///   * `points` is the emitted vertex array. When a mesh authors a
///     face-varying primvar the topology is expanded so every corner owns its
///     own vertex, so emitted vertex i is not authored point i and several
///     emitted vertices share one authored point.
///   * `indices` is a triangle list. An n-gon is triangulated, and the
///     triangulation introduces interior diagonals that the scene never
///     authored. A consumer that treated every triangle edge as a mesh edge
///     would report a diagonal as an authored edge, which no round-trip can
///     map back to the stage.
///
/// Both tables are renderer-neutral and bulk: a whole mesh's point origins and
/// authored edges cross this ABI inside the record that carries the geometry,
/// so a consumer never calls back per point, per edge, or per triangle.
///
/// point_origins has one entry per emitted vertex. The entry is the authored
/// point index the vertex was emitted from, or OPENUSD_SILK_SUBPRIM_NONE when
/// the vertex has no authored origin. point_origin_count is either zero or
/// point_count; a partial table is malformed. authored_point_count is one past
/// the largest authored index the table names, so a consumer sizes an
/// authored-point table without scanning.
///
/// The sentinel means "this emitted vertex is not an authored point", which is
/// a statement about the vertex and not about whether it is drawn. A shaded
/// mesh carrying an authored point no face references emits that point and
/// names it with the sentinel, because nothing rasterizes it and it is not a
/// pick target. The points draw mode draws every emitted vertex, so those
/// points become pick targets: the record it publishes names each of them with
/// its authored index and grows authored_point_count to cover them. Only
/// genuinely generated vertices -- the corner copies of an expanded
/// face-varying topology, or a refiner's limit-surface vertices -- keep the
/// sentinel there.
///
/// corner_edges has one entry per emitted primitive corner: three per triangle
/// for a triangle list and one per line for a line list. Entry 3t+c of a
/// triangle list is the edge from corner c to corner (c + 1) % 3 of triangle t.
/// The entry is the authored mesh edge index, or OPENUSD_SILK_SUBPRIM_NONE when
/// the corner edge is a triangulation diagonal the scene never authored.
/// corner_edge_count is either zero or the emitted corner count; a partial
/// table is malformed. authored_edge_count is one past the largest authored
/// edge index the table names.
///
/// Authored edge indices are assigned by walking authored faces in order and,
/// within a face, corners in order, allocating a new index the first time an
/// unordered authored point pair is seen. The numbering is therefore a pure
/// function of the authored topology, so two consumers of the same stage agree
/// without exchanging anything but this table.
///
/// subprim_identity names which of the three targets this record can answer
/// exactly. A cleared bit is not "no data": it means the delegate refuses that
/// target for this record, and subprim_unsupported names why. A refined
/// subdivision surface emits vertices and edges that no authored component
/// corresponds to, so it clears the point and edge bits with
/// OPENUSD_SILK_SUBPRIM_UNSUPPORTED_REFINED_SUBDIVISION rather than publishing
/// a generated index as authored identity. A consumer must refuse the target
/// with that diagnostic rather than substituting an emitted index.
///
/// ABI v9 extends FRAME with a fixed light table. hdSilk currently publishes
/// direct DistantLight, SphereLight, RectLight, DiskLight and CylinderLight
/// entries plus DomeLight as ambient only. The table is frame-local because
/// lights are evaluated in eye space by the managed renderer after applying
/// the current view matrix.
///
/// Since ABI v16 a DomeLight that carries an authored texture is published as an
/// ENVIRONMENT record instead of contributing to that ambient term. An
/// untextured DomeLight is unchanged.
///
/// FRAME v9 appends after clip_planes. ABI v12 expands the fixed direct-light
/// table from four to eight entries without changing an entry's layout:
///   uint32 light_count (0..8 direct lights)
///   uint32 reserved[3] (0)
///   repeated 8 times:
///     uint32 light_type (OPENUSD_SILK_LIGHT_*)
///     uint32 shadow_enabled (drives the ABI v19 SHADOW command)
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
/// ABI v21 appends the bounded dome table after ambient_intensity. It exists so
/// that a dome light can be addressed by a per-prim UsdLux collection, which the
/// single scene-wide ambient colour above cannot express:
///   uint32 dome_count (0..OPENUSD_SILK_MAX_DOME_LIGHTS)
///   uint32 reserved[3] (0)
///   repeated OPENUSD_SILK_MAX_DOME_LIGHTS times, 32 bytes each:
///     float  ambient_color[3] (this dome's own contribution to ambient_color)
///     float  reserved (0)
///     uint32 flags (OPENUSD_SILK_DOME_FLAG_*)
///     uint32 reserved[3] (0)
///
/// The dome table is the ordering the ABI v21 LIGHT_LINK dome_mask indexes and
/// the ordering an ENVIRONMENT record's dome_index names. It is the page's own
/// path-sorted light ordering restricted to DomeLight prims, so dome bit i is
/// stable across pages for an unchanged scene exactly as direct light bit i is,
/// and both bit spaces are bounded at the same eight entries.
///
/// Entry i is meaningful only when OPENUSD_SILK_DOME_FLAG_PRESENT is set.
/// ambient_color is that dome's own summand of the scene-wide ambient_color
/// above, produced by the same expression in the same order, so summing the
/// ambient_color of every present dome reproduces ambient_color bit for bit and
/// a consumer that masks domes cannot drift from one that does not. It is zero
/// for a textured dome, because a textured dome contributes an ENVIRONMENT
/// record rather than an ambient colour; what that record resolves to -- a
/// prefiltered environment, or the consumer's own mean-radiance fallback -- is a
/// consumer-side decision that this producer does not model.
///
/// A scene with more DomeLight prims than the table admits publishes no dome
/// table at all -- dome_count is zero, every ENVIRONMENT record carries
/// OPENUSD_SILK_DOME_INDEX_NONE, and the LIGHT_LINK command reports
/// OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_DOME_BUDGET. Every dome keeps
/// contributing to ambient_color and to the ENVIRONMENT stream exactly as it did
/// before ABI v21, so an over-budget scene loses dome *linking* rather than dome
/// *lighting*. Publishing a partial table instead would have been worse than
/// publishing none: the domes that did fit would be maskable and the ones that
/// did not would not, and no consumer-side sum of the two is the authored image.
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
/// corresponding emitted line. topology_revision starts at 1 and changes when
/// Hydra reports dirty topology, and also when a mesh's OpenSubdiv refinement
/// level changes, because refinement replaces the emitted triangle list even
/// though the authored topology is unchanged.
///
/// topology_revision is a PRESENTATION revision, not the authored one. Draw
/// mode and complexity rebuild the emitted arrays a consumer retains -- a
/// shaded triangle list is republished as a line list or a point list, a line
/// list is resubdivided, a point list is duplicated, and point_origins is
/// rebuilt with them -- while the authored topology behind them does not
/// change. The revision a record publishes therefore composes the authored
/// revision with the presentation that produced its arrays: it equals the
/// authored revision exactly while the presentation rebuilds nothing, it is the
/// same value every time one presentation of one authored topology is
/// republished, and it differs from the authored revision and from every other
/// presentation of it. Consumers may compare it for equality and for
/// replacement, but must not read it as a counter: a presentation change may
/// move it in either direction, and a lower revision is an identity
/// replacement, not a stale page.
///
/// Because the revision describes the presentation and not the payload, an
/// ABI v8 instance reference publishes exactly the revision of the prototype
/// payload record whose geometry it reuses.
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
/// ABI v18 adds the two MDL surface kinds. It appends no field and moves no
/// existing one: only the domain of surface_kind grows. A material whose sole
/// surface terminal is authored in the `mdl` render context -- the shape of an
/// Omniverse-authored stage that never got a preview or MaterialX context --
/// used to reach the delegate with no surface terminal at all and was drawn as
/// an undiagnosed default. It now reaches this table either as
/// OPENUSD_SILK_SURFACE_MDL_DISTILLED, with the accepted subset of its authored
/// MDL inputs distilled into the same scalar and texture tables a
/// UsdPreviewSurface fills, or as OPENUSD_SILK_SURFACE_MDL_UNAVAILABLE with
/// empty tables and a diagnostic naming the material. Distillation happens in
/// the separately built, optional openusd_mdl adapter, which no base package
/// ships; its absence is exactly the MDL_UNAVAILABLE case.
///
/// A distilled MDL material's texture entries name the "st" primvar. An MDL
/// material carries no primvar reader node to state a coordinate stream, and
/// "st" is both the OpenUSD default texture-coordinate primvar name and the set
/// an MDL state::texture_coordinate(0) resolves to for a USD mesh.
///
/// material_id_hash is 64-bit FNV-1a over the exact path bytes and is an index
/// only: path stays the authoritative identity, exactly as for meshes. It
/// matches MESH_UPSERT's material_binding_hash for the same path.
///
/// MATERIAL_REMOVE (type = 5), added by ABI v5:
///   uint64 material_id_hash
///   uint32 path_byte_count
///   uint8  path[path_byte_count]  (UTF-8, no NUL)
///
/// ENVIRONMENT_UPSERT (type = 6), added by ABI v16:
///   Offset Size Type     Field
///        8    8 uint64   environment_id_hash
///       16    4 uint32   path_byte_count
///       20    4 uint32   texture_path_byte_count
///       24    4 uint32   texture_format (OPENUSD_SILK_DOME_TEXTURE_*)
///       28    4 uint32   source_color_space (OPENUSD_SILK_COLOR_SPACE_*)
///       32    4 uint32   unsupported_features (OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_*)
///       36    4 uint32   dome_index (ABI v21; index into the FRAME dome table)
///       40   12 float    color[3]
///       52    4 float    intensity
///       56    4 float    exposure
///       60    4 float    diffuse
///       64    4 float    specular
///       68    4 uint32   reserved (0)
///       72  128 double   transform[16] (row-major light-to-world)
///      200    * uint8    path[path_byte_count] (UTF-8, no NUL)
///        *    * uint8    texture_path[texture_path_byte_count] (UTF-8, no NUL)
///
/// ENVIRONMENT_REMOVE (type = 7), added by ABI v16:
///   uint64 environment_id_hash
///   uint32 path_byte_count
///   uint8  path[path_byte_count]  (UTF-8, no NUL)
///
/// An ENVIRONMENT record is one UsdLuxDomeLight that carries an authored
/// texture. It exists because the FRAME ambient term is a single colour and
/// cannot describe an image: the consumer needs the texture identity, the dome
/// orientation and the authored emission controls to resolve an environment
/// response of its own. path is the dome light's own prim path and is the
/// authoritative identity; environment_id_hash is 64-bit FNV-1a over the exact
/// path bytes and is an index only, exactly as for meshes and materials.
///
/// texture_path is the resolved asset path of the dome's texture:file. It is
/// never empty: a dome light with no authored texture publishes no ENVIRONMENT
/// record at all and keeps contributing to the FRAME ambient term the way it did
/// before ABI v16, so the untextured dome result is unchanged by this addition.
/// A textured dome is excluded from that ambient accumulation instead, because
/// its emission is the image and folding an untextured approximation of it into
/// the frame would double-count the same light once the consumer resolves the
/// texture.
///
/// color, intensity, exposure, diffuse and specular are the authored UsdLux
/// emission controls, published unmultiplied so the consumer can apply exactly
/// the terms it supports and diagnose the rest. transform is the dome's
/// light-to-world matrix, which orients the image; the wire carries it whether
/// or not a given consumer's environment response is orientation dependent.
///
/// unsupported_features names authored dome behaviour hdSilk cannot express on
/// this wire, so a consumer diagnoses it against the named prim rather than
/// rendering a plausible but wrong result. It is producer-side only: a consumer
/// that cannot implement, say, a specular environment response diagnoses that
/// itself and does not need a bit here.
///
/// dome_index, added by ABI v21, is the record's own entry in the FRAME dome
/// table and therefore the bit this dome occupies in a LIGHT_LINK dome_mask. It
/// is what lets a consumer keep one prefiltered response per dome and select
/// them per draw. It is OPENUSD_SILK_DOME_INDEX_NONE when, and only when, the
/// page publishes no dome table at all, which is reported through
/// OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_DOME_BUDGET; such a dome lights every
/// prim. Once a dome table exists every textured dome has an entry in it by
/// construction, so an unindexed record against a non-empty table means the
/// producer resolved its domes and its environments against different orderings,
/// and a consumer refuses the whole page rather than handing that dome's sky to
/// prims whose collection excludes it.
///
/// The records and the table's textured entries are in one-to-one
/// correspondence, resolved over the state a page leaves behind rather than over
/// the commands it happens to carry: exactly one retained ENVIRONMENT record
/// names each entry the FRAME table marks TEXTURED, and no record names an
/// absent or untextured one. A textured entry is one dome's image, so an entry
/// with no record is a dome the consumer has no sky for and no prim can be
/// excluded from, and two records on one entry make one dome's mask bit select
/// the other dome's sky. A consumer refuses such a page whole.
///
/// LIGHT_LINK (type = 8), added by ABI v18:
///   Offset Size Type     Field
///        8    4 uint32   entry_count
///       12    4 uint32   light_count (0..8; the lights the masks index)
///       16    4 uint32   unsupported_features (OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_*)
///       20    4 uint32   dome_count (ABI v21; 0..8, the domes dome_mask indexes)
///       24    * entries[entry_count]
///
/// Each entry is (20 bytes plus the path since ABI v21):
///        0    4 uint32   light_mask
///        4    4 uint32   shadow_mask
///        8    4 uint32   dome_mask (ABI v21)
///       12    4 int32    instance_index (-1 = every instance of path)
///       16    4 uint32   path_byte_count
///       20    * uint8    path[path_byte_count] (UTF-8, no NUL)
///
/// LIGHT_LINK carries UsdLux light and shadow linking. Bit i of light_mask is
/// set when the direct light at index i of the FRAME light table illuminates the
/// named prim, and bit i of shadow_mask is set when the prim casts that light's
/// shadow. The two masks are independent: UsdLux resolves collection:lightLink
/// and collection:shadowLink as separate collections over the same light, so a
/// prim that casts a light's shadow without being lit by it -- an unlit or
/// off-screen blocker that must still occlude other receivers -- is a valid,
/// published combination and must not be rejected or intersected by a consumer.
/// Bits at or above light_count are always zero. The masks index the
/// FRAME table of the same page, which is why the command is published together
/// with the frame it belongs to and never on its own: a mask resolved against a
/// different light ordering would name the wrong lights.
///
/// Bit i of dome_mask, added by ABI v21, is set when the DomeLight at index i of
/// the FRAME dome table illuminates the named prim. It is a third independent
/// mask rather than more bits of light_mask because the two tables are two
/// orderings: direct light 0 and dome 0 are different lights, and folding them
/// into one bit space would have made every existing mask depend on how many
/// domes a scene happens to author. Bits at or above dome_count are always zero.
///
/// A published table that is not the canonical empty retirement -- entry_count,
/// light_count and dome_count all zero, which is how a page says "linking was
/// retired" and which is therefore valid against any frame -- carries exactly
/// the light_count and dome_count of the frame it travels with, whether that
/// frame is in this page or is the one the consumer already retains. The masks
/// were resolved against those orderings, so a table that disagrees with them
/// names a different set of lights or domes, and a consumer refuses the whole
/// page rather than masking against an ordering the producer never used.
///
/// There is deliberately no dome shadow mask. UsdLux collection:shadowLink on a
/// DomeLight is a caster restriction on a shadow this renderer never casts --
/// the ABI v19 SHADOW command covers direct lights only, and a dome has no
/// light-space projection to render one from -- so an authored dome shadow
/// collection is reported through
/// OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_SHADOW_COLLECTION against the dome and
/// is never folded into dome_mask. Applying it to dome_mask would silently turn
/// a caster restriction into a receiver restriction and darken exactly the prims
/// the author asked to keep lit.
///
/// The table is sparse and complete at once. It is a full replacement of the
/// consumer's link state, and it omits every prim whose masks are the default --
/// every light links and every light's shadow links, which is what UsdLux means
/// by a collection that includes the root with no exclusions. A scene with no
/// authored linking therefore publishes no entries at all, and hdSilk publishes
/// the command only when the resolved table differs from the one it last
/// published, including the transition back to empty. A consumer that has never
/// seen a LIGHT_LINK command lights every prim with every light, which is the
/// pre-v18 behaviour.
///
/// A path is published whole or not at all. The path-wide entry is what a
/// consumer falls back to for every instance it has no entry for, so a
/// restrictive path entry published without the instance entries that widen it
/// would apply the author's narrower mask to exactly the instances the author
/// opted back in. When a path's entries do not fit the bound the whole group is
/// omitted, which leaves the path linked to every light: truncation fails open,
/// never closed.
///
/// path is the prim's authoritative USD path, matching MESH_UPSERT. An entry
/// with instance_index -1 applies to every published instance of that path; an
/// entry with a non-negative instance_index applies to exactly that instance and
/// overrides the path-wide entry. The index is the same identity MESH_UPSERT
/// publishes, which under nested instancing is the composed
/// parent_index * inner_instance_count + inner_index rather than any one
/// instancer's own index, so a consumer resolves a mask with the index it
/// already retains and never has to decompose it. An instance is published
/// whenever its masks differ from what its own path already resolves to, which
/// is not the same as differing from the default: an instance that opts back
/// into every light under a path that opts out of one must be published, or the
/// consumer's fallback would apply the path's narrower mask to it. Entries are
/// sorted by path and then by instance_index so an unchanged scene produces
/// byte-identical pages.
///
/// Linking is resolved from the categories Hydra reports for each prim and from
/// the collection identifier each light reports, so nested collections and
/// membership expressions are already collapsed into category identity by
/// UsdImaging before hdSilk sees them. Under instancing those categories are
/// reported one instancer level at a time, and hdSilk composes them onto the
/// composed identities it publishes: a level's categories are unioned onto every
/// identity beneath it, because collection membership propagates to whatever an
/// included instance instances and no descendant can take it away. What hdSilk
/// cannot express is a table larger than OPENUSD_SILK_MAX_LINK_ENTRIES; that is
/// reported through OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_TRUNCATED rather than by
/// silently dropping entries, and the prims that did not fit keep the default
/// "linked to everything" behaviour so a truncated table cannot darken a scene
/// without saying so. The bound counts published entries and nothing before
/// them: a prim that links to every light, and an instance whose categories
/// differ from its path's while its masks do not, are both resolved away and
/// cost the table nothing however many of them a scene has. More DomeLight prims
/// than OPENUSD_SILK_MAX_DOME_LIGHTS is
/// reported the same way, through
/// OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_DOME_BUDGET, and every dome then stays
/// linked to every prim for the same reason. An identity whose categories a
/// level does not report -- Hydra resolves no per-instance membership through a
/// nested level -- keeps its prototype's entry and is named in a producer-side
/// diagnostic rather than published under a mask resolved from another instance.
///
/// SHADOW (type = 9), added by ABI v19:
///   Offset Size Type     Field
///        8    4 uint32   descriptor_count (0..OPENUSD_SILK_MAX_SHADOW_MAPS)
///       12    4 uint32   light_count (0..8; the lights light_index names)
///       16    4 uint32   unsupported_features (OPENUSD_SILK_SHADOW_UNSUPPORTED_*)
///       20    4 uint32   reserved (0)
///       24    * descriptors[descriptor_count]
///
/// Each descriptor is 288 bytes:
///        0    4 uint32   light_index (index into the FRAME light table)
///        4    4 uint32   map_index (0..descriptor_count-1, strictly ascending)
///        8    4 uint32   resolution (square, 256..2048, a power of two)
///       12    4 uint32   flags (OPENUSD_SILK_SHADOW_FLAG_*)
///       16  128 double   view[16] (world-to-light, row-major)
///      144  128 double   projection[16] (light-space projection, row-major)
///      272    4 float    depth_bias (normalized [0,1] light-space depth)
///      276    4 float    normal_bias (world units along the receiver normal)
///      280    4 float    pcf_radius (shadow-map texels)
///      284    4 uint32   reserved (0)
///
/// SHADOW carries one bounded descriptor per direct light that authors
/// `inputs:shadow:enable` and whose light-space projection hdSilk can derive
/// exactly. `view` and `projection` follow the same conventions as the FRAME
/// camera matrices: row-major, row-vector (a point is a row multiplied on the
/// left), and OpenGL [-w, +w] clip depth, so one consumer-side depth convention
/// covers the camera and every light.
///
/// The projection is derived from the world-space bounds of the published
/// caster geometry, so a map always covers the casters it is rendered from and
/// a receiver outside it is unshadowed by construction rather than by clamping.
/// A scene whose casters have no extent publishes no descriptor and reports
/// OPENUSD_SILK_SHADOW_UNSUPPORTED_NO_CASTERS.
///
/// Which prims cast into a map is not on this command: it is the ABI 18
/// LIGHT_LINK shadow mask. Bit `light_index` of a prim's shadow_mask is set when
/// that prim casts the light's shadow, which is exactly UsdLux
/// `collection:shadowLink` semantics -- a caster restriction. Shadow linking
/// never restricts receiving: every prim the light illuminates is shaded against
/// the map, because a receiver excluded from the caster collection is still lit
/// by a light whose other casters occlude it.
///
/// depth_bias, normal_bias and pcf_radius are the producer's policy for this
/// map, derived from its own resolution and world extent, so a consumer applies
/// one filtering and biasing rule rather than inventing its own per backend.
///
/// The whole table is compared and published as one, exactly like LIGHT_LINK: a
/// page whose descriptors are unchanged publishes no SHADOW command, which is
/// how a consumer knows a retained shadow map is still valid for the lights and
/// the caster bounds it was rendered from. A consumer that has never seen a
/// SHADOW command casts no shadows, which is the pre-v19 behaviour.
///
/// unsupported_features names authored shadow state hdSilk did not put on this
/// command, so a consumer diagnoses it against a named light instead of
/// rendering an unshadowed image that looks deliberate.
#define OPENUSD_SILK_COMMAND_FRAME 1u
#define OPENUSD_SILK_COMMAND_MESH_UPSERT 2u
#define OPENUSD_SILK_COMMAND_MESH_REMOVE 3u
#define OPENUSD_SILK_COMMAND_MATERIAL_UPSERT 4u
#define OPENUSD_SILK_COMMAND_MATERIAL_REMOVE 5u
#define OPENUSD_SILK_COMMAND_ENVIRONMENT_UPSERT 6u
#define OPENUSD_SILK_COMMAND_ENVIRONMENT_REMOVE 7u
#define OPENUSD_SILK_COMMAND_LIGHT_LINK 8u
#define OPENUSD_SILK_COMMAND_SHADOW 9u

/// The bounds of the ABI v20 deformation block. Every one of them is checked
/// before hdSilk allocates the arrays they size, so a rig that would need more
/// than this publishes no block at all and names the budget it exceeded in
/// deformation_unsupported.
///
/// The joint bound is the palette a consumer uploads once per deformed
/// prototype: 256 row-major float4x4 rows is 16 KiB, which every supported
/// backend can bind as one storage buffer without a second indirection. The
/// influence bound is the fixed stream width: eight influences per point is two
/// uint4/float4 pairs, which is what the authored `skel:jointIndices` element
/// sizes of production rigs need and what a bounded vertex-side evaluation can
/// read without a loop over an unbounded count.
#define OPENUSD_SILK_MAX_DEFORMATION_JOINTS 256u
#define OPENUSD_SILK_MAX_DEFORMATION_INFLUENCES 8u

/// The bounds of the sparse blend-shape tables. One range is one resolved
/// sub-shape, so the range bound is a bound on how many shapes may be active at
/// one time code rather than on how many a rig authors, and the delta bound is
/// the total sparse deltas across every active range.
#define OPENUSD_SILK_MAX_DEFORMATION_BLEND_RANGES 64u
#define OPENUSD_SILK_MAX_DEFORMATION_BLEND_DELTAS 1048576u

/// The byte ceiling of one deformation block. It bounds the product of the
/// per-point bounds above, which the individual bounds do not: a mesh with four
/// million points and eight influences is inside every other bound and would
/// still be a 256 MiB block. Checked before allocation, against the sizes the
/// counts imply rather than against a built buffer.
#define OPENUSD_SILK_MAX_DEFORMATION_BYTES 67108864u

/// Optional sections of the deformation block.
#define OPENUSD_SILK_DEFORMATION_FLAG_NONE 0u
#define OPENUSD_SILK_DEFORMATION_FLAG_BIND_NORMALS 1u
#define OPENUSD_SILK_DEFORMATION_FLAG_BLEND_NORMAL_OFFSETS 2u

/// Why a deformed prim published no deformation block. hdSilk still published
/// the CPU-resolved points, so the record renders correctly; the reason names
/// what a consumer that wanted to evaluate the rig itself did not receive.
///
/// SKINNING_METHOD covers dual quaternion skinning, which this block's
/// evaluation order cannot express. GEOMETRY covers a record whose emitted
/// points are not the rig's bind points -- a refined subdivision surface, a
/// topology expanded for face-varying attributes, a wireframe draw mode, or a
/// complexity that resubdivided the emitted primitives -- because the
/// influences are addressed by control-point index and there is no correct way
/// to index them against the emitted array. NORMALS covers a mesh whose
/// authored normals a point-indexed deformation cannot carry; the block is
/// still published without bind_normals, exactly as the CPU path omits the
/// authored array.
#define OPENUSD_SILK_DEFORMATION_UNSUPPORTED_NONE 0u
#define OPENUSD_SILK_DEFORMATION_UNSUPPORTED_JOINT_BUDGET 1u
#define OPENUSD_SILK_DEFORMATION_UNSUPPORTED_INFLUENCE_BUDGET 2u
#define OPENUSD_SILK_DEFORMATION_UNSUPPORTED_BLEND_BUDGET 4u
#define OPENUSD_SILK_DEFORMATION_UNSUPPORTED_BYTE_BUDGET 8u
#define OPENUSD_SILK_DEFORMATION_UNSUPPORTED_SKINNING_METHOD 16u
#define OPENUSD_SILK_DEFORMATION_UNSUPPORTED_GEOMETRY 32u
#define OPENUSD_SILK_DEFORMATION_UNSUPPORTED_NORMALS 64u
/// The published rig did not reproduce the CPU-resolved points hdSilk
/// published beside it, so the block was dropped rather than published as a
/// second, disagreeing answer.
#define OPENUSD_SILK_DEFORMATION_UNSUPPORTED_UNVERIFIED 128u

/// The largest absolute component difference hdSilk accepts between the points
/// its own CPU deformation produced and the points the published block
/// reproduces. A block that disagrees by more is dropped with
/// OPENUSD_SILK_DEFORMATION_UNSUPPORTED_UNVERIFIED, because a consumer that
/// evaluated it would draw a different surface from the one this ABI already
/// promised. The tolerance is a float32 rounding tolerance only: the two
/// evaluations run the same arithmetic in a different order.
#define OPENUSD_SILK_DEFORMATION_VERIFY_TOLERANCE 1.0e-4f

/// The entry a subprim-identity table uses for an emitted component that has
/// no authored counterpart: a vertex with no authored origin, or a corner edge
/// that is a triangulation diagonal. It is deliberately not zero, so a producer
/// that forgets to fill an entry cannot silently claim authored point or edge
/// zero.
#define OPENUSD_SILK_SUBPRIM_NONE 0xFFFFFFFFu

/// Which pick targets a MESH_UPSERT record answers with authored identity.
/// FACE is set whenever triangle_subprims carries authored face indices, which
/// only a triangle list and the wireframe line list derived from one do: a
/// point list emits one primitive per vertex, belonging to no triangulated
/// face, so it refuses the face target with
/// OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE rather than answering it from
/// a per-primitive table that carries no authored face. EDGE
/// and POINT are set only when the corresponding table is published and every
/// emitted component it covers maps to an authored component or to
/// OPENUSD_SILK_SUBPRIM_NONE.
#define OPENUSD_SILK_SUBPRIM_IDENTITY_NONE 0u
#define OPENUSD_SILK_SUBPRIM_IDENTITY_FACE 1u
#define OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE 2u
#define OPENUSD_SILK_SUBPRIM_IDENTITY_POINT 4u

/// Why a record refuses an exact subprim target. The reasons are flags, because
/// one record can refuse edges and points for different causes.
///
/// REFINED_SUBDIVISION covers a refined subdivision surface: the emitted
/// vertices and the edges between them are generated by the refiner and
/// correspond to no authored component, so there is no authored index to
/// return. TOPOLOGY_MODE covers an emitted topology whose components are not
/// authored mesh components -- a point list, a resubdivided line list, or a
/// curve segment list. GEOMETRY covers a record whose emitted array stopped
/// matching the authored topology the table was built from. BUDGET covers a
/// table that would exceed OPENUSD_SILK_MAX_SUBPRIM_IDENTITY_BYTES.
#define OPENUSD_SILK_SUBPRIM_UNSUPPORTED_NONE 0u
#define OPENUSD_SILK_SUBPRIM_UNSUPPORTED_REFINED_SUBDIVISION 1u
#define OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE 2u
#define OPENUSD_SILK_SUBPRIM_UNSUPPORTED_GEOMETRY 4u
#define OPENUSD_SILK_SUBPRIM_UNSUPPORTED_BUDGET 8u

/// The byte ceiling of one record's two subprim-identity tables together.
/// Checked before allocation, against the sizes the counts imply rather than
/// against a built buffer, exactly as the deformation budget is. A record whose
/// tables would exceed it publishes neither table and reports
/// OPENUSD_SILK_SUBPRIM_UNSUPPORTED_BUDGET, so an oversized mesh loses exact
/// edge and point picking rather than the whole record.
#define OPENUSD_SILK_MAX_SUBPRIM_IDENTITY_BYTES 67108864u

/// The largest number of shadow maps one page describes. The bound is a memory
/// bound as much as a wire bound: a consumer allocates one square depth map per
/// descriptor, so four 2048-square D32 maps is the 64 MiB ceiling this ABI
/// permits. A scene with more shadow-casting lights than fit publishes the
/// lowest light indices and sets
/// OPENUSD_SILK_SHADOW_UNSUPPORTED_MAP_BUDGET.
#define OPENUSD_SILK_MAX_SHADOW_MAPS 4u

/// The square shadow-map resolutions a descriptor may name. Both bounds are
/// inclusive and every published value is a power of two, so a consumer can
/// allocate an atlas of fixed tiles without renegotiating a size per frame.
#define OPENUSD_SILK_MIN_SHADOW_MAP_RESOLUTION 256u
#define OPENUSD_SILK_MAX_SHADOW_MAP_RESOLUTION 2048u
#define OPENUSD_SILK_DEFAULT_SHADOW_MAP_RESOLUTION 1024u

/// Descriptor flags. ORTHOGRAPHIC states that the projection has no perspective
/// divide, which is exact for a distant light and is the only projection hdSilk
/// derives today. CASTER_LINKED states that at least one published prim is
/// excluded from this light's caster collection, so a consumer that ignores the
/// LIGHT_LINK shadow mask renders a knowably different image.
#define OPENUSD_SILK_SHADOW_FLAG_NONE 0u
#define OPENUSD_SILK_SHADOW_FLAG_ORTHOGRAPHIC 1u
#define OPENUSD_SILK_SHADOW_FLAG_CASTER_LINKED 2u

/// Authored shadow state hdSilk did not put on the SHADOW command.
/// LIGHT_TYPE is set when a shadow-enabled direct light is not a distant light:
/// only a distant light has an exact light-space projection here, and a sphere,
/// rect, disk or cylinder light would need a projection this producer has not
/// derived. MAP_BUDGET is set when more lights asked for a map than
/// OPENUSD_SILK_MAX_SHADOW_MAPS allows. NO_CASTERS is set when a light asked for
/// a map but the published geometry has no world extent to derive one from.
#define OPENUSD_SILK_SHADOW_UNSUPPORTED_NONE 0u
#define OPENUSD_SILK_SHADOW_UNSUPPORTED_LIGHT_TYPE 1u
#define OPENUSD_SILK_SHADOW_UNSUPPORTED_MAP_BUDGET 2u
#define OPENUSD_SILK_SHADOW_UNSUPPORTED_NO_CASTERS 4u

/// The largest sparse light-link table one page publishes. The bound exists
/// because the table is rebuilt and compared whole every frame: an unbounded
/// table would let one scene's linking dominate page building. Prims beyond the
/// bound keep the default of being linked to every light, and the table sets
/// OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_TRUNCATED so the omission is diagnosable.
#define OPENUSD_SILK_MAX_LINK_ENTRIES 4096u

/// Link state hdSilk could not express. TRUNCATED is set when the resolved table
/// did not fit OPENUSD_SILK_MAX_LINK_ENTRIES. DOME_BUDGET, added by ABI v21, is
/// set when the scene authors more DomeLight prims than the bounded dome table
/// admits, so the domes past the bound carry no dome bit and stay linked to
/// every prim.
#define OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_NONE 0u
#define OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_TRUNCATED 1u
#define OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_DOME_BUDGET 2u

/// The instance_index an entry uses to mean "every published instance of path".
#define OPENUSD_SILK_LINK_ALL_INSTANCES (-1)

/// UsdLuxDomeLight texture:format values carried by the ABI v16 environment
/// record. AUTOMATIC is what UsdLux itself defaults to and means the mapping is
/// derived from the image; a consumer that cannot derive it treats AUTOMATIC as
/// LATLONG, which is what every equirectangular HDR in the accepted corpus is.
#define OPENUSD_SILK_DOME_TEXTURE_AUTOMATIC 0u
#define OPENUSD_SILK_DOME_TEXTURE_LATLONG 1u
#define OPENUSD_SILK_DOME_TEXTURE_MIRRORED_BALL 2u
#define OPENUSD_SILK_DOME_TEXTURE_ANGULAR 3u
#define OPENUSD_SILK_DOME_TEXTURE_CUBE_MAP_VERTICAL_CROSS 4u

/// The dome_index an ABI v21 ENVIRONMENT record carries when the page publishes
/// no dome table at all, which happens when the scene authors more domes than
/// OPENUSD_SILK_MAX_DOME_LIGHTS. It is deliberately out of the table's range so
/// a consumer cannot mistake it for dome bit zero.
#define OPENUSD_SILK_DOME_INDEX_NONE 0xFFFFFFFFu

/// Authored dome behaviour hdSilk does not put on the wire. Each bit names one
/// slice so a consumer's diagnostic can name it too.
#define OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_NONE 0u
#define OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_COLOR_TEMPERATURE 1u
#define OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_POLE_AXIS 2u
/// Added by ABI v18 and narrowed by ABI v21. A dome's collection:lightLink is
/// now resolved into the LIGHT_LINK dome_mask, so this bit no longer names an
/// authored receiver collection. It is set only when the dome could not be given
/// a bit at all -- the scene authors more domes than the bounded dome table
/// admits -- and therefore keeps lighting every prim regardless of its
/// collection.
#define OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_LINK_COLLECTION 4u
/// Added by ABI v21. UsdLux collection:shadowLink on a DomeLight restricts which
/// prims cast that dome's shadow, and hdSilk casts no dome shadow at all: the
/// ABI v19 SHADOW command covers direct lights only. The collection is named
/// here rather than reinterpreted as a receiver restriction, which is what
/// folding it into dome_mask would silently have made it.
#define OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_SHADOW_COLLECTION 8u
#define OPENUSD_SILK_MAX_FRAME_LIGHTS 8u
/// The bounded dome table the ABI v21 FRAME command publishes and the LIGHT_LINK
/// dome_mask indexes. Eight, exactly as OPENUSD_SILK_MAX_FRAME_LIGHTS is, so a
/// dome bit and a direct light bit are bounded the same way and a consumer sizes
/// one constant for both.
#define OPENUSD_SILK_MAX_DOME_LIGHTS 8u
/// Flags on one ABI v21 FRAME dome entry. PRESENT distinguishes a published dome
/// from the zeroed tail of the fixed table; TEXTURED marks the dome that
/// publishes an ENVIRONMENT record instead of an ambient colour.
#define OPENUSD_SILK_DOME_FLAG_NONE 0u
#define OPENUSD_SILK_DOME_FLAG_PRESENT 1u
#define OPENUSD_SILK_DOME_FLAG_TEXTURED 2u
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
///
/// MDL_DISTILLED and MDL_UNAVAILABLE were added by ABI v18 and describe a
/// material whose only surface terminal is authored in the `mdl` render
/// context. They are provenance and cache identity, not a second shading
/// model: a distilled MDL material fills the same scalar and texture tables as
/// a UsdPreviewSurface one and is shaded by the same GPU pipeline.
///
/// MDL_UNAVAILABLE is published with empty tables when the terminal is MDL and
/// nothing could be distilled from it -- no optional openusd_mdl adapter is
/// installed, the module or material is outside the adapter's accepted set, or
/// distillation failed. It is a separate kind from UNSUPPORTED so a consumer
/// can name MDL as the cause instead of reporting an unrecognised graph, and so
/// that no MDL-only material can be drawn as an undiagnosed default grey.
#define OPENUSD_SILK_SURFACE_UNSUPPORTED 0u
#define OPENUSD_SILK_SURFACE_PREVIEW_SURFACE 1u
#define OPENUSD_SILK_SURFACE_MATERIALX_PROJECTED 2u
#define OPENUSD_SILK_SURFACE_MATERIALX_GENERATED 3u
#define OPENUSD_SILK_SURFACE_VOLUME_DENSITY 4u
#define OPENUSD_SILK_SURFACE_MDL_DISTILLED 5u
#define OPENUSD_SILK_SURFACE_MDL_UNAVAILABLE 6u

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

/// UsdUVTexture's `useMetadata`, which is also its schema default and therefore
/// what an unauthored `wrap` means.
///
/// Carried distinctly from OPENUSD_SILK_WRAP_BLACK because the two record
/// different authored intent: `black` states the addressing, while
/// `useMetadata` defers it to wrap metadata inside the image file. This delegate
/// reads no image metadata, so both resolve to the same addressing -- USD's
/// documented fallback when no metadata is present is `black` -- but a consumer
/// that does read metadata must be able to tell them apart, and a consumer that
/// reports what it resolved must be able to say which one it resolved.
#define OPENUSD_SILK_WRAP_USE_METADATA 4u

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
/// complexity changes how hdSilk emits curve and point records, and which
/// uniform OpenSubdiv refinement level it evaluates authored subdivision
/// surfaces at. For curves and points it applies after the draw mode, so it
/// also subdivides a triangle-list record that the wireframe,
/// hidden-surface-wireframe, or points draw mode converted into lines or
/// points, and changing complexity while one of those draw modes is active
/// republishes those records too.
///
/// For meshes, complexity selects a bounded refinement level: LOW refines
/// nothing and publishes the control cage exactly as every earlier release did,
/// while MEDIUM, HIGH and VERY_HIGH refine to levels 1, 2 and 3. A mesh is
/// refined only when it authors a subdivisionScheme hdSilk evaluates --
/// catmullClark, bilinear or loop; "none" is a polygon mesh and is never
/// refined. Authored creases, corner sharpness, holes, and the
/// interpolateBoundary/faceVaryingLinearInterpolation/triangleSubdivisionRule
/// rules are all honoured, face-varying primvars are refined through their own
/// OpenSubdiv channels, and every emitted triangle still reports the authored
/// face it descends from so material subsets and uniform primvars keep
/// resolving.
///
/// A mesh hdSilk cannot refine -- an unsupported scheme, a control cage
/// OpenSubdiv rejects as non-manifold or malformed, or one whose refined vertex
/// or face count would exceed hdSilk's bound -- is published as its whole
/// control cage with a diagnostic naming the reason, never as a partially
/// refined surface.
///
/// Because refinement changes the emitted triangle topology, a mesh whose
/// refinement level changes publishes a new topology_revision even though its
/// authored topology did not change.
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
/// for curve and point tessellation density and for the bounded OpenSubdiv mesh
/// refinement level. OPENUSD_SILK_COMPLEXITY_LOW refines nothing, which is the
/// historical hdSilk baseline.
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
