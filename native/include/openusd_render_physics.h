// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_RENDER_PHYSICS_H
#define OPENUSD_RENDER_PHYSICS_H

#include <stddef.h>
#include <stdint.h>

#define OPENUSD_STORM_TRANSFORM_OVERRIDE_ITEM_VERSION 1u
#define OPENUSD_STORM_TRANSFORM_OVERRIDE_UPDATE_VERSION 1u
#define OPENUSD_STORM_TRANSFORM_OVERRIDE_DIAGNOSTICS_VERSION 1u

/* Maximum overrides one renderer retains; excess items are dropped and counted. */
#define OPENUSD_STORM_TRANSFORM_OVERRIDE_MAXIMUM_ITEMS 4096u
#define OPENUSD_STORM_TRANSFORM_OVERRIDE_MAXIMUM_PATH_BYTES 1048576u

/* The batch replaces every retained override. Always set for a complete batch. */
#define OPENUSD_STORM_TRANSFORM_OVERRIDE_UPDATE_REPLACE 0x1u

/* The item was snapped rather than interpolated. Diagnostics only. */
#define OPENUSD_STORM_TRANSFORM_OVERRIDE_ITEM_SNAPPED 0x1u

/*
 * The item transform carries rotation and translation only, and the renderer
 * must keep the scale and shear the prim already carries. The renderer splits
 * the rendered prim's own transform into a symmetric scale-shear factor and a
 * rotation, then replaces only that rotation, so an authored scaled or sheared
 * prim keeps its shape under simulation without the caller reading the stage.
 * Callers that supply a full world matrix including scale must leave this
 * clear, otherwise the scale is applied twice.
 */
#define OPENUSD_STORM_TRANSFORM_OVERRIDE_ITEM_PRESERVE_STRETCH 0x2u

/*
 * One packed rigid transform override. path_offset/path_length address UTF-8
 * bytes in openusd_storm_transform_override_update.path_bytes and are not
 * NUL-terminated. object_id is the caller's opaque stable simulation identity;
 * native code never interprets it and never receives a physics engine handle.
 * transform is a row-major world matrix with translation in elements 12..14.
 * instance_index is -1 for a non-instanced prim.
 */
typedef struct openusd_storm_transform_override_item
{
    uint64_t object_id;
    uint32_t path_offset;
    uint32_t path_length;
    int32_t instance_index;
    uint32_t flags;
    double transform[16];
} openusd_storm_transform_override_item;

/*
 * Caller-owned packed transform override batch. Native code copies or consumes
 * every byte synchronously and never retains either pointer.
 */
typedef struct openusd_storm_transform_override_update
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t item_count;
    uint32_t flags;
    uint64_t revision;
    const openusd_storm_transform_override_item* items;
    const char* path_bytes;
    uint32_t path_bytes_size;
    uint32_t reserved;
} openusd_storm_transform_override_update;

/*
 * Pointer-free applied-state diagnostics. applied_count is the number of
 * overrides currently driving rendered prims, unresolved_count counts batch
 * items whose prim path is absent from the rendered scene, unsupported_count
 * counts items the backend cannot apply (currently instanced items), and
 * dropped_count counts items refused for capacity.
 */
typedef struct openusd_storm_transform_override_diagnostics
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t applied_count;
    uint32_t unresolved_count;
    uint64_t revision;
    uint64_t applied_batch_count;
    uint64_t rejected_batch_count;
    uint64_t dirtied_prim_count;
    uint32_t capacity;
    uint32_t dropped_count;
    uint32_t unsupported_count;
    uint32_t reserved;
} openusd_storm_transform_override_diagnostics;

#if defined(__cplusplus)
static_assert(sizeof(openusd_storm_transform_override_item) == 152);
static_assert(offsetof(openusd_storm_transform_override_item, path_offset) == 8);
static_assert(offsetof(openusd_storm_transform_override_item, transform) == 24);
static_assert(sizeof(openusd_storm_transform_override_diagnostics) == 64);
static_assert(
    offsetof(openusd_storm_transform_override_diagnostics, revision) == 16);
static_assert(
    offsetof(openusd_storm_transform_override_diagnostics, capacity) == 48);
#if UINTPTR_MAX == UINT64_MAX
static_assert(sizeof(openusd_storm_transform_override_update) == 48);
static_assert(offsetof(openusd_storm_transform_override_update, revision) == 16);
static_assert(offsetof(openusd_storm_transform_override_update, items) == 24);
static_assert(
    offsetof(openusd_storm_transform_override_update, path_bytes) == 32);
#endif
#endif

#define OPENUSD_STORM_DEFORMATION_OVERRIDE_ITEM_VERSION 1u
#define OPENUSD_STORM_DEFORMATION_OVERRIDE_UPDATE_VERSION 1u
#define OPENUSD_STORM_DEFORMATION_OVERRIDE_DIAGNOSTICS_VERSION 1u

/*
 * Bounded deformation pages. A deformation batch carries one region per
 * simulated body and one shared point page every region addresses, so the
 * whole frame crosses the boundary in a single call and no element is ever
 * marshalled on its own. Both bounds are page bounds, not per object bounds:
 * a batch that exceeds either is refused whole rather than truncated, because
 * a half applied deformation renders geometry no producer described.
 */
#define OPENUSD_STORM_DEFORMATION_OVERRIDE_MAXIMUM_ITEMS 1024u
#define OPENUSD_STORM_DEFORMATION_OVERRIDE_MAXIMUM_POINTS 4194304u
#define OPENUSD_STORM_DEFORMATION_OVERRIDE_MAXIMUM_PATH_BYTES 1048576u

/* The batch replaces every retained deformation. Always set for a complete batch. */
#define OPENUSD_STORM_DEFORMATION_OVERRIDE_UPDATE_REPLACE 0x1u

/* The region was snapped rather than blended. Diagnostics only. */
#define OPENUSD_STORM_DEFORMATION_OVERRIDE_ITEM_SNAPPED 0x1u

/*
 * One packed deformation region. path_offset/path_length address UTF-8 bytes in
 * openusd_storm_deformation_override_update.path_bytes and are not
 * NUL-terminated. point_offset/point_count address triples in
 * openusd_storm_deformation_override_update.points, which carries three floats
 * per point in the same space the rendered prim's own points are authored in:
 * these are object local, because the renderer keeps applying the prim's own
 * transform, and a transform override may drive the same prim at the same time.
 * object_id is the caller's opaque stable simulation identity; native code never
 * interprets it and never receives a physics engine handle. instance_index is -1
 * for a non-instanced prim. topology_revision names the element topology the
 * points were produced against; a renderer that sees a different topology
 * refuses the region rather than drawing vertices against indices that never
 * described them.
 */
typedef struct openusd_storm_deformation_override_item
{
    uint64_t object_id;
    uint32_t path_offset;
    uint32_t path_length;
    int32_t instance_index;
    uint32_t flags;
    uint32_t point_offset;
    uint32_t point_count;
    uint64_t topology_revision;
} openusd_storm_deformation_override_item;

/*
 * Caller-owned packed deformation batch. Native code copies or consumes every
 * byte synchronously and never retains any pointer.
 */
typedef struct openusd_storm_deformation_override_update
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t item_count;
    uint32_t flags;
    uint64_t revision;
    const openusd_storm_deformation_override_item* items;
    const float* points;
    const char* path_bytes;
    uint32_t point_count;
    uint32_t path_bytes_size;
} openusd_storm_deformation_override_update;

/*
 * Pointer-free applied-state diagnostics. applied_count is the number of prims
 * currently drawn with simulated points, unresolved_count counts regions whose
 * prim path is absent from the rendered scene, unsupported_count counts regions
 * the backend cannot apply (currently instanced regions), dropped_count counts
 * regions refused for capacity, and mismatched_count counts regions the renderer
 * refused because the prim's element topology does not accept that many points.
 */
typedef struct openusd_storm_deformation_override_diagnostics
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t applied_count;
    uint32_t unresolved_count;
    uint64_t revision;
    uint64_t applied_batch_count;
    uint64_t rejected_batch_count;
    uint64_t dirtied_prim_count;
    uint32_t capacity;
    uint32_t dropped_count;
    uint32_t unsupported_count;
    uint32_t mismatched_count;
} openusd_storm_deformation_override_diagnostics;

#if defined(__cplusplus)
static_assert(sizeof(openusd_storm_deformation_override_item) == 40);
static_assert(offsetof(openusd_storm_deformation_override_item, path_offset) == 8);
static_assert(offsetof(openusd_storm_deformation_override_item, point_offset) == 24);
static_assert(
    offsetof(openusd_storm_deformation_override_item, topology_revision) == 32);
static_assert(sizeof(openusd_storm_deformation_override_diagnostics) == 64);
static_assert(
    offsetof(openusd_storm_deformation_override_diagnostics, revision) == 16);
static_assert(
    offsetof(openusd_storm_deformation_override_diagnostics, capacity) == 48);
static_assert(
    offsetof(openusd_storm_deformation_override_diagnostics, mismatched_count) == 60);
#if UINTPTR_MAX == UINT64_MAX
static_assert(sizeof(openusd_storm_deformation_override_update) == 56);
static_assert(offsetof(openusd_storm_deformation_override_update, revision) == 16);
static_assert(offsetof(openusd_storm_deformation_override_update, items) == 24);
static_assert(offsetof(openusd_storm_deformation_override_update, points) == 32);
static_assert(
    offsetof(openusd_storm_deformation_override_update, path_bytes) == 40);
static_assert(
    offsetof(openusd_storm_deformation_override_update, point_count) == 48);
#endif
#endif

#endif
