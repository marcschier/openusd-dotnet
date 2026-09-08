// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_PROPERTY_INSPECTION_H
#define OPENUSD_PROPERTY_INSPECTION_H

#include "openusd_dotnet.h"

#ifdef __cplusplus
extern "C" {
#endif

#define OPENUSD_PROPERTY_INSPECTION_VERSION 1u
#define OPENUSD_PROPERTY_MAX_PROPERTIES 65536u
#define OPENUSD_PROPERTY_MAX_TEXT_BYTES (16u * 1024u * 1024u)
#define OPENUSD_PROPERTY_MAX_PREVIEW_ELEMENTS 16u
#define OPENUSD_PROPERTY_MAX_PREVIEW_TEXT_BYTES 65536u
#define OPENUSD_PROPERTY_MAX_METADATA_WORK 1048576u
#define OPENUSD_PROPERTY_UNKNOWN_COUNT UINT64_MAX

typedef struct openusd_property_snapshot openusd_property_snapshot;

typedef struct openusd_property_limits
{
    uint32_t struct_size;
    uint32_t version;
    uint32_t maximum_property_count;
    uint32_t maximum_text_bytes;
    uint32_t preview_elements;
    uint32_t time_sample_preview;
    uint32_t target_preview;
    uint32_t maximum_metadata_work;
    uint32_t maximum_preview_text_bytes;
    uint32_t reserved;
} openusd_property_limits;

typedef enum openusd_property_preview_status
{
    OPENUSD_PROPERTY_COMPLETE = 0,
    OPENUSD_PROPERTY_TRUNCATED = 1,
    OPENUSD_PROPERTY_DEFERRED = 2,
    OPENUSD_PROPERTY_UNSUPPORTED = 3
} openusd_property_preview_status;

typedef enum openusd_property_reason
{
    OPENUSD_PROPERTY_REASON_NONE = 0,
    OPENUSD_PROPERTY_REASON_PREVIEW_LIMIT = 1,
    OPENUSD_PROPERTY_REASON_TEXT_LIMIT = 2,
    OPENUSD_PROPERTY_REASON_DEFERRED_STORE = 3,
    OPENUSD_PROPERTY_REASON_VALUE_CLIPS = 4,
    OPENUSD_PROPERTY_REASON_ARRAY_EDIT = 5,
    OPENUSD_PROPERTY_REASON_TYPE = 6,
    OPENUSD_PROPERTY_REASON_SPLINE = 7,
    OPENUSD_PROPERTY_REASON_ASSET_EXPRESSION = 8,
    OPENUSD_PROPERTY_REASON_COMPOSITION = 9
} openusd_property_reason;

typedef enum openusd_property_resolve_source
{
    OPENUSD_PROPERTY_SOURCE_NONE = 0,
    OPENUSD_PROPERTY_SOURCE_FALLBACK = 1,
    OPENUSD_PROPERTY_SOURCE_DEFAULT = 2,
    OPENUSD_PROPERTY_SOURCE_TIME_SAMPLES = 3,
    OPENUSD_PROPERTY_SOURCE_VALUE_CLIPS = 4,
    OPENUSD_PROPERTY_SOURCE_SPLINE = 5,
    OPENUSD_PROPERTY_SOURCE_DEFERRED = 6
} openusd_property_resolve_source;

typedef enum openusd_property_value_state
{
    OPENUSD_PROPERTY_VALUE_UNSET = 0,
    OPENUSD_PROPERTY_VALUE_PRESENT = 1,
    OPENUSD_PROPERTY_VALUE_BLOCKED = 2,
    OPENUSD_PROPERTY_VALUE_DEFERRED = 3
} openusd_property_value_state;

/* Offsets index strings for values/targets and doubles for time samples.
 * Unknown totals are explicit, never a fabricated zero for deferred data.
 */
typedef struct openusd_property_preview
{
    uint64_t total_count;
    uint32_t offset;
    uint32_t count;
    uint32_t status;
    uint32_t reason;
} openusd_property_preview;

/* Four consecutive strings: authored, evaluated, resolved, anchor layer.
 * missing is 0=resolved, 1=nonempty asset unresolved, 2=not applicable/unknown.
 * Paths are complete or absent, never silently shortened identifiers.
 */
typedef struct openusd_property_asset
{
    uint32_t string_offset;
    uint32_t missing;
} openusd_property_asset;

/* kind: 0=attribute, 1=relationship. flags: custom=1, authored declaration=2,
 * array=4, authored-value-opinion-known=8, authored-value-opinion=16.
 * variability: 0=varying, 1=uniform.
 * First six consecutive strings: name, type, value-source layer/spec,
 * target-source layer/spec. Empty source pairs mean unproven/not applicable.
 * Attribute targets are connections, independent of its USD value.
 */
typedef struct openusd_property_entry
{
    uint32_t kind;
    uint32_t name;
    uint32_t type;
    uint32_t flags;
    uint32_t variability;
    uint32_t resolve_source;
    uint32_t value_state;
    uint32_t source_layer;
    uint32_t source_path;
    uint32_t target_source_layer;
    uint32_t target_source_path;
    uint32_t reserved;
    openusd_property_preview value;
    openusd_property_preview time_samples;
    openusd_property_preview targets;
    uint32_t asset_offset;
    uint32_t asset_count;
} openusd_property_entry;

typedef struct openusd_property_view
{
    uint32_t struct_size;
    uint32_t version;
    uint64_t change_serial;
    uint32_t is_complete;
    uint32_t metadata_work;
    uint32_t time_sampled;
    uint32_t reserved;
    double time_code;
    const openusd_property_entry* entries;
    size_t entries_size;
    size_t entry_count;
    const openusd_property_asset* assets;
    size_t assets_size;
    size_t asset_count;
    const double* times;
    size_t times_size;
    size_t time_count;
    const char* data;
    size_t data_size;
    const uint32_t* offsets;
    size_t offsets_size;
    size_t string_count;
} openusd_property_view;

/* One query and one release; all returned storage is owned by snapshot and
 * detached from the stage. String zero is the requested absolute prim path.
 * Property rows are complete, in ordinal UTF-8 name order, or the query fails.
 * Individual previews explicitly report truncation/deferral and count knowledge.
 * This read-only composed view is NOT exact authored edit/undo authority.
 */
OPENUSD_DOTNET_API openusd_status openusd_stage_get_prim_property_snapshot(
    const openusd_stage* stage,
    const char* prim_path,
    int32_t time_sampled,
    double time_code,
    const openusd_property_limits* limits,
    openusd_property_snapshot** snapshot,
    openusd_property_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API void openusd_property_snapshot_release(
    openusd_property_snapshot* snapshot);

#ifdef __cplusplus
}
#endif

#endif
