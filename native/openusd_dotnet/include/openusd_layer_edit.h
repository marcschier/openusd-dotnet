// Copyright (c) marcschier. Licensed under the MIT License.
#pragma once

#include "openusd_dotnet.h"

#define OPENUSD_LAYER_EDIT_PACKET_VERSION 1u
#define OPENUSD_LAYER_EDIT_MAX_BYTES (4u << 20)
#define OPENUSD_LAYER_EDIT_MAX_STRING_BYTES 4096u
#define OPENUSD_LAYER_EDIT_MAX_ITEMS 4096u
#define OPENUSD_LAYER_EDIT_MAX_ADDRESSES 256u
#define OPENUSD_LAYER_EDIT_MAX_SPECS 4096u
#define OPENUSD_LAYER_EDIT_MAX_FIELDS 128u
#define OPENUSD_LAYER_EDIT_MAX_PATH_ELEMENTS 32u

#ifdef __cplusplus
extern "C" {
#endif

typedef struct openusd_edit_buffer openusd_edit_buffer;

typedef struct openusd_edit_buffer_view
{
    const uint8_t* data;
    size_t size;
} openusd_edit_buffer_view;

typedef enum openusd_edit_outcome
{
    OPENUSD_EDIT_APPLIED = 0,
    OPENUSD_EDIT_CONFLICT = 1,
    OPENUSD_EDIT_STALE_TARGET = 2,
    OPENUSD_EDIT_NOT_EDITABLE = 3
} openusd_edit_outcome;

OPENUSD_DOTNET_API void openusd_edit_buffer_release(openusd_edit_buffer* buffer);

OPENUSD_DOTNET_API openusd_status openusd_stage_edit_get_local_layer(
    const openusd_stage* stage, const char* identifier, size_t identifier_size,
    openusd_layer** layer, openusd_error_buffer* error);
OPENUSD_DOTNET_API openusd_status openusd_stage_edit_get_user_layer(
    const openusd_stage* stage, openusd_layer** layer, openusd_error_buffer* error);
OPENUSD_DOTNET_API openusd_status openusd_layer_edit_get_state(
    const openusd_layer* layer, openusd_edit_buffer** owner,
    openusd_edit_buffer_view* view, openusd_error_buffer* error);
OPENUSD_DOTNET_API openusd_status openusd_layer_edit_capture(
    const openusd_layer* layer, const uint8_t* addresses, size_t addresses_size,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view,
    openusd_error_buffer* error);
OPENUSD_DOTNET_API openusd_status openusd_layer_edit_apply(
    const openusd_layer* layer, const uint8_t* expected, size_t expected_size,
    const uint8_t* mutations, size_t mutations_size, int32_t* outcome,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view,
    openusd_error_buffer* error);
OPENUSD_DOTNET_API openusd_status openusd_layer_edit_restore(
    const openusd_layer* layer, const uint8_t* expected, size_t expected_size,
    const uint8_t* restore, size_t restore_size, int32_t* outcome,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view,
    openusd_error_buffer* error);
OPENUSD_DOTNET_API openusd_status openusd_layer_edit_checkpoint(
    const openusd_layer* layer, openusd_edit_buffer** owner,
    openusd_edit_buffer_view* view, openusd_error_buffer* error);
OPENUSD_DOTNET_API openusd_status openusd_layer_edit_checkpoint_restore(
    const openusd_layer* layer, const uint8_t* expected, size_t expected_size,
    const uint8_t* restore, size_t restore_size, int32_t* outcome,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view,
    openusd_error_buffer* error);
OPENUSD_DOTNET_API openusd_status openusd_layer_edit_acknowledge_saved(
    const openusd_layer* layer, const uint8_t* checkpoint, size_t checkpoint_size,
    int32_t* acknowledged, openusd_error_buffer* error);
OPENUSD_DOTNET_API openusd_status openusd_edit_checkpoint_export(
    const uint8_t* checkpoint, size_t checkpoint_size,
    const char* destination, size_t destination_size,
    openusd_edit_buffer** owner, openusd_edit_buffer_view* view,
    openusd_error_buffer* error);

#ifdef __cplusplus
}
#endif
