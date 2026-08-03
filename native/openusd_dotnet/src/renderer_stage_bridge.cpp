// Copyright (c) marcschier. Licensed under the MIT License.

#include "internal/common.h"

openusd_status openusd_renderer_stage_initialize(
    const openusd_stage_access* access,
    openusd_renderer_stage_initializer initializer,
    void* renderer_context,
    openusd_error_buffer* error)
{
    // OUTER_ABI_GUARD
    return Guard(error, [&]() -> openusd_status
    {
        if (access == nullptr || initializer == nullptr || !access->lock.owns_lock() ||
            access->owner != std::this_thread::get_id() || access->stage == nullptr ||
            !access->stage->value)
        {
            WriteError(error, "An owner-thread stage access guard and renderer initializer are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        UsdStageRefPtr stage_view = access->stage->value;
        return initializer(&stage_view, renderer_context, error);
    });
}
