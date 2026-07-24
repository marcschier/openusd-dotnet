// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_RENDERER_STAGE_BRIDGE_H
#define OPENUSD_RENDERER_STAGE_BRIDGE_H

#include "openusd_dotnet.h"

#include "pxr/pxr.h"
#include "pxr/usd/usd/stage.h"

typedef openusd_status (*openusd_renderer_stage_initializer)(
    const PXR_NS::UsdStageRefPtr* stage_view,
    void* renderer_context,
    openusd_error_buffer* error);

// Private project-native bridge. The stage_view pointer addresses a stack
// object valid only during initializer. The initializer may copy the RefPtr
// into its renderer engine/session, but must not retain the pointer address.
OPENUSD_DOTNET_API openusd_status openusd_renderer_stage_initialize(
    const openusd_stage_access* access,
    openusd_renderer_stage_initializer initializer,
    void* renderer_context,
    openusd_error_buffer* error);

#endif
