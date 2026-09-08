// Copyright (c) marcschier. Licensed under the MIT License.
#pragma once

#include "openusd_layer_edit.h"

#define OPENUSD_REVIEW_PACKET_VERSION 1u
#define OPENUSD_REVIEW_MAX_ENVELOPE_BYTES (24u << 20)
#define OPENUSD_REVIEW_MAX_DOCUMENT_BYTES (16u << 20)
#define OPENUSD_REVIEW_MAX_REVIEW_BYTES OPENUSD_LAYER_EDIT_MAX_BYTES
#define OPENUSD_REVIEW_MAX_MANIFEST_FILES 1024u
#define OPENUSD_REVIEW_MAX_SOURCE_FILE_BYTES (16u << 20)
#define OPENUSD_REVIEW_MAX_SOURCE_TOTAL_BYTES (64u << 20)
#define OPENUSD_REVIEW_MAX_SOURCE_SPECS 65536u
#define OPENUSD_REVIEW_MAX_SOURCE_TOKENS 262144u
#define OPENUSD_REVIEW_MAX_SOURCE_TOTAL_TOKENS 1048576u
#define OPENUSD_REVIEW_MAX_SOURCE_EDGES 65536u
#define OPENUSD_REVIEW_MAX_COMPOSITION_WORK 262144u
#define OPENUSD_REVIEW_MAX_COMPOSITION_DEPTH 32u

#ifdef __cplusplus
extern "C" {
#endif

// Admits the actual filesystem text-USD source and dependency bytes before
// opening the stage. Unverified resident cache entries require reconciliation.
// The new session starts with a pristine, real-anchored owned review layer.
// Verified source admission currently uses Windows stable-read leases; other
// platforms and aliases that change SDK anchors require explicit reconciliation.
OPENUSD_DOTNET_API openusd_status openusd_stage_open_for_review(
    const char* path, openusd_stage** stage, openusd_error_buffer* error);

// Returns an RSB1 source binding, never a binding inferred for a legacy stage.
OPENUSD_DOTNET_API openusd_status openusd_stage_review_source_binding(
    const openusd_stage* stage, openusd_edit_buffer** owner,
    openusd_edit_buffer_view* view, openusd_error_buffer* error);

// Returns an RDE1 envelope containing a URD1 document and a process-local
// receipt. The caller publishes the document bytes; this API writes no file.
OPENUSD_DOTNET_API openusd_status openusd_layer_review_capture(
    const openusd_layer* layer, const uint8_t* binding, size_t binding_size,
    const char* target_document_path, size_t target_document_path_size,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view,
    openusd_error_buffer* error);

// Validates a URD1 document against an explicit source path and returns an
// RDE1 envelope with an empty receipt.
OPENUSD_DOTNET_API openusd_status openusd_review_document_read(
    const uint8_t* document, size_t document_size,
    const char* expected_source_path, size_t expected_source_path_size,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view,
    openusd_error_buffer* error);

// Returns only recorded source/target/dependency metadata in RDI1 after full
// structural URD1 validation. No filesystem access, stage, receipt or replay.
// Metadata remains untrusted; the caller must validate explicit source intent.
OPENUSD_DOTNET_API openusd_status openusd_review_document_inspect(
    const uint8_t* document, size_t document_size,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view,
    openusd_error_buffer* error);

// Import requires a verified source and a pristine session. Applied returns a
// UED1 kind-5 state for a new review history target; other outcomes are empty.
OPENUSD_DOTNET_API openusd_status openusd_stage_review_import(
    const openusd_stage* stage, const uint8_t* document, size_t document_size,
    const uint8_t* binding, size_t binding_size, int32_t* outcome,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view,
    openusd_error_buffer* error);

OPENUSD_DOTNET_API openusd_status openusd_layer_review_acknowledge_saved(
    const openusd_layer* layer, const uint8_t* receipt, size_t receipt_size,
    int32_t* acknowledged, openusd_error_buffer* error);

#ifdef __cplusplus
}
#endif
