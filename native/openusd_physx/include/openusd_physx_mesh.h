// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_PHYSX_MESH_H
#define OPENUSD_PHYSX_MESH_H

#include "openusd_physx.h"

#ifdef __cplusplus
extern "C" {
#endif

#define OPENUSD_PHYSX_MESH_ABI_VERSION 1u
#define OPENUSD_PHYSX_MESH_MAX_BATCH 64u
#define OPENUSD_PHYSX_MESH_MAX_POINTS 4194304u
#define OPENUSD_PHYSX_MESH_MAX_INDICES 12582912u
#define OPENUSD_PHYSX_MESH_MAX_HULLS 256u
#define OPENUSD_PHYSX_MESH_MAX_VERTICES 255u
#define OPENUSD_PHYSX_MESH_MAX_RESOLUTION 4000000u

typedef enum openusd_physx_mesh_approximation
{
    OPENUSD_PHYSX_MESH_CONVEX_HULL = 1,
    OPENUSD_PHYSX_MESH_CONVEX_DECOMPOSITION = 2
} openusd_physx_mesh_approximation;

/* Offsets/counts are elements, not bytes. Triangle indices are local to this mesh.
 * Convex hulls accept at least three source points, ignore topology, and require
 * all decomposition settings to be zero. Prepared output still has at least four points.
 * Decomposition zero settings select 64 hulls, 64 vertices, 400000 voxels and 1% error. */
typedef struct openusd_physx_mesh_input
{
    uint32_t approximation;
    uint32_t point_offset;
    uint32_t point_count;
    uint32_t index_offset;
    uint32_t index_count;
    uint32_t max_hulls;
    uint32_t max_vertices;
    uint32_t resolution;
    double volume_error_percent;
} openusd_physx_mesh_input;

typedef struct openusd_physx_mesh_hull
{
    uint32_t input_index;
    uint32_t point_offset;
    uint32_t point_count;
    uint32_t reserved;
} openusd_physx_mesh_hull;

typedef struct openusd_physx_mesh_preparation openusd_physx_mesh_preparation;

/* One-shot, bounded CPU preparation. A handle owns only its prepared output.
 * Input buffers remain caller-owned and are borrowed only by compute.
 * Compute/copy are serialized; cancel may run concurrently. Release requires all
 * other calls to have returned. Cancellation and failure never expose partial output.
 * These limits bound admitted input/output and voxel resolution, not total process memory. */
OPENUSD_PHYSX_API openusd_physx_status openusd_physx_mesh_preparation_create(
    uint32_t abi_version,
    openusd_physx_mesh_preparation** preparation,
    openusd_physx_error_buffer* error);

OPENUSD_PHYSX_API void openusd_physx_mesh_preparation_release(
    openusd_physx_mesh_preparation* preparation);

OPENUSD_PHYSX_API void openusd_physx_mesh_preparation_cancel(
    openusd_physx_mesh_preparation* preparation);

OPENUSD_PHYSX_API openusd_physx_status openusd_physx_mesh_preparation_compute(
    openusd_physx_mesh_preparation* preparation,
    const openusd_physx_mesh_input* inputs,
    uint32_t input_count,
    const openusd_physx_vec3f* points,
    uint32_t point_count,
    const uint32_t* indices,
    uint32_t index_count,
    openusd_physx_error_buffer* error);

/* Insufficient capacity reports both required counts without writing either buffer.
 * Passing null buffers with zero capacities is the size query. */
OPENUSD_PHYSX_API openusd_physx_status openusd_physx_mesh_preparation_copy(
    openusd_physx_mesh_preparation* preparation,
    openusd_physx_mesh_hull* hulls,
    uint32_t hull_capacity,
    openusd_physx_vec3f* points,
    uint32_t point_capacity,
    uint32_t* hull_count,
    uint32_t* point_count,
    openusd_physx_error_buffer* error);

#ifdef __cplusplus
}
#endif

#endif
