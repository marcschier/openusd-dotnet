// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_HIERARCHY_H
#define OPENUSD_HIERARCHY_H

#include "openusd_dotnet.h"

#ifdef __cplusplus
extern "C" {
#endif

#define OPENUSD_HIERARCHY_VERSION 1u
#define OPENUSD_HIERARCHY_MAX_PRIMS 1000000u
#define OPENUSD_HIERARCHY_MAX_TEXT_BYTES (128u * 1024u * 1024u)
#define OPENUSD_HIERARCHY_MAX_DEPTH 1024u
#define OPENUSD_HIERARCHY_MAX_VARIANT_SETS 65536u
#define OPENUSD_HIERARCHY_MAX_VARIANT_NAMES 262144u
#define OPENUSD_HIERARCHY_MAX_METADATA_WORK 16000000u

typedef struct openusd_hierarchy_snapshot openusd_hierarchy_snapshot;

typedef struct openusd_hierarchy_limits
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t maximum_prim_count;
    uint32_t maximum_text_bytes;
    uint32_t maximum_depth;
    uint32_t maximum_variant_sets;
    uint32_t maximum_variant_names;
    uint32_t maximum_metadata_work;
} openusd_hierarchy_limits;

typedef enum openusd_hierarchy_flags
{
    OPENUSD_HIERARCHY_ACTIVE = 1u,
    OPENUSD_HIERARCHY_LOADED = 2u,
    OPENUSD_HIERARCHY_DEFINED = 4u,
    OPENUSD_HIERARCHY_ABSTRACT = 8u,
    OPENUSD_HIERARCHY_PROTOTYPE = 16u,
    OPENUSD_HIERARCHY_IN_PROTOTYPE = 32u,
    OPENUSD_HIERARCHY_INSTANCE = 64u,
    OPENUSD_HIERARCHY_INSTANCE_PROXY = 128u,
    OPENUSD_HIERARCHY_HAS_PAYLOAD = 256u,
    OPENUSD_HIERARCHY_INSTANCEABLE = 512u
} openusd_hierarchy_flags;

typedef enum openusd_hierarchy_variant_status
{
    OPENUSD_HIERARCHY_VARIANTS_COMPLETE = 0,
    OPENUSD_HIERARCHY_VARIANTS_DEFERRED = 1
} openusd_hierarchy_variant_status;

/* Each entry owns four consecutive strings: path, name, type, prototype path.
 * Root entries (including prototype roots) have parent_index=-1 and depth=1.
 * Scene all-prim preorder is followed by prototypes in first-reference order.
 * Shared/nested prototypes are emitted once; instance proxies are not expanded.
 */
typedef struct openusd_hierarchy_entry
{
    int32_t parent_index;
    uint32_t depth;
    uint32_t child_count;
    uint32_t flags;
    uint32_t string_offset;
    uint32_t variant_offset;
    uint32_t variant_count;
    uint32_t variant_status;
} openusd_hierarchy_entry;

/* Two strings (set name, applied selection), then variant_count choice strings.
 * Set order and lexicographic choice order are those of the pinned Usd API.
 */
typedef struct openusd_hierarchy_variant_set
{
    uint32_t string_offset;
    uint32_t variant_count;
} openusd_hierarchy_variant_set;

typedef struct openusd_hierarchy_view
{
    uint32_t struct_size;
    uint32_t version;
    uint64_t change_serial;
    uint32_t is_complete;
    uint32_t metadata_work;
    const openusd_hierarchy_entry* entries;
    size_t entries_size;
    size_t entry_count;
    const openusd_hierarchy_variant_set* variant_sets;
    size_t variant_sets_size;
    size_t variant_set_count;
    const char* data;
    size_t data_size;
    const uint32_t* offsets;
    size_t offsets_size;
    size_t string_count;
} openusd_hierarchy_view;

/* Exactly one query and one release. All pointers are owned by snapshot and
 * remain valid until release, independently of the stage. Quota failures have
 * an actionable diagnostic and cleared outputs, never a successful truncation.
 * is_complete includes variant metadata: deferred backing stores are explicit
 * per entry, with no partial variant set list. Hierarchy rows are always complete.
 */
OPENUSD_DOTNET_API openusd_status openusd_stage_get_hierarchy_snapshot(
    const openusd_stage* stage,
    const openusd_hierarchy_limits* limits,
    openusd_hierarchy_snapshot** snapshot,
    openusd_hierarchy_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API void openusd_hierarchy_snapshot_release(
    openusd_hierarchy_snapshot* snapshot);

#ifdef __cplusplus
}
#endif

#endif
