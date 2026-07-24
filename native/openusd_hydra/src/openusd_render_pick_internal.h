// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_RENDER_PICK_INTERNAL_H
#define OPENUSD_RENDER_PICK_INTERNAL_H

#include "openusd_render_pick.h"

#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec3d.h"
#include "pxr/base/gf/vec4d.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <limits>
#include <string>

PXR_NAMESPACE_USING_DIRECTIVE

namespace openusd_render_pick_detail
{
inline void ResetResult(openusd_render_pick_result* result) noexcept
{
    if (result == nullptr)
    {
        return;
    }
    const uint32_t struct_size = result->struct_size;
    const uint32_t version = result->version;
    std::memset(result, 0, sizeof(*result));
    result->struct_size = struct_size;
    result->version = version;
    result->status = OPENUSD_RENDER_PICK_STATUS_INVALID;
    result->normalized_depth = 1.0;
    result->instance_index = -1;
    result->element_index = -1;
}

inline bool ValidateResult(
    openusd_render_pick_result* result,
    std::string& error) noexcept
{
    if (result == nullptr)
    {
        error = "A pick result output is required.";
        return false;
    }
    const bool valid =
        result->struct_size == sizeof(openusd_render_pick_result) &&
        result->version == OPENUSD_RENDER_PICK_RESULT_VERSION;
    ResetResult(result);
    if (!valid)
    {
        error = "The pick result struct size or version is unsupported.";
        return false;
    }
    return true;
}

inline bool ValidateRequest(
    const openusd_render_pick_request* request,
    std::string& error) noexcept
{
    if (request == nullptr)
    {
        error = "A pick request is required.";
        return false;
    }
    if (request->struct_size != sizeof(openusd_render_pick_request) ||
        request->version != OPENUSD_RENDER_PICK_REQUEST_VERSION)
    {
        error = "The pick request struct size or version is unsupported.";
        return false;
    }
    if (request->reserved != 0 ||
        (request->flags &
         ~(OPENUSD_RENDER_PICK_REQUEST_HAS_SCENE_REVISION |
           OPENUSD_RENDER_PICK_REQUEST_CULL_BACK_FACES)) != 0)
    {
        error = "The pick request flags or reserved fields are invalid.";
        return false;
    }
    if (request->width != 1 || request->height != 1 ||
        request->viewport_width <= 0 || request->viewport_height <= 0 ||
        request->x < 0 || request->y < 0 ||
        request->x >= request->viewport_width ||
        request->y >= request->viewport_height)
    {
        error =
            "The pick request must identify one physical pixel inside a positive viewport.";
        return false;
    }
    if (request->target > OPENUSD_RENDER_PICK_TARGET_POINT ||
        request->resolve_mode != OPENUSD_RENDER_PICK_RESOLVE_NEAREST_TO_CENTER)
    {
        error = "The pick target or resolve mode is invalid.";
        return false;
    }
    if (!std::isfinite(request->time_code))
    {
        error = "The pick time code must be finite.";
        return false;
    }
    return true;
}

inline GfMatrix4d NarrowProjection(
    const GfMatrix4d& projection,
    int32_t x,
    int32_t y,
    int32_t viewport_width,
    int32_t viewport_height)
{
    const double left =
        (2.0 * static_cast<double>(x) /
         static_cast<double>(viewport_width)) - 1.0;
    const double right =
        (2.0 * static_cast<double>(x + 1) /
         static_cast<double>(viewport_width)) - 1.0;
    const double top =
        1.0 - (2.0 * static_cast<double>(y) /
               static_cast<double>(viewport_height));
    const double bottom =
        1.0 - (2.0 * static_cast<double>(y + 1) /
               static_cast<double>(viewport_height));

    GfMatrix4d narrow(1.0);
    narrow[0][0] = 2.0 / (right - left);
    narrow[1][1] = 2.0 / (bottom - top);
    narrow[3][0] = -(right + left) / (right - left);
    narrow[3][1] = -(bottom + top) / (bottom - top);
    return projection * narrow;
}

inline double NormalizedOpenGlDepth(
    const GfVec3d& world_point,
    const GfMatrix4d& view,
    const GfMatrix4d& projection) noexcept
{
    const GfVec4d clip =
        GfVec4d(world_point[0], world_point[1], world_point[2], 1.0) *
        view *
        projection;
    if (!std::isfinite(clip[2]) || !std::isfinite(clip[3]) ||
        std::abs(clip[3]) <= std::numeric_limits<double>::epsilon())
    {
        return 1.0;
    }
    const double depth = ((clip[2] / clip[3]) + 1.0) * 0.5;
    return std::clamp(depth, 0.0, 1.0);
}
}

#endif
