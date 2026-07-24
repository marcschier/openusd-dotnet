// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_render_pick_internal.h"

#include "pxr/base/gf/frustum.h"
#include "pxr/base/gf/range2d.h"
#include "pxr/base/gf/vec2d.h"

#include <cmath>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <string>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
bool Near(const GfMatrix4d& left, const GfMatrix4d& right)
{
    for (int row = 0; row < 4; ++row)
    {
        for (int column = 0; column < 4; ++column)
        {
            if (std::abs(left[row][column] - right[row][column]) > 1e-12)
            {
                return false;
            }
        }
    }
    return true;
}

GfMatrix4d PinnedTestProjection(
    GfFrustum frustum,
    int32_t x,
    int32_t y,
    int32_t width,
    int32_t height)
{
    GfVec2d minimum(
        (2.0 * x / width) - 1.0,
        1.0 - (2.0 * y / height));
    GfVec2d maximum(
        (2.0 * (x + 1) / width) - 1.0,
        1.0 - (2.0 * (y + 1) / height));
    const GfVec2d origin = frustum.GetWindow().GetMin();
    const GfVec2d scale =
        frustum.GetWindow().GetMax() - frustum.GetWindow().GetMin();
    minimum =
        origin + GfCompMult(scale, 0.5 * (GfVec2d(1.0, 1.0) + minimum));
    maximum =
        origin + GfCompMult(scale, 0.5 * (GfVec2d(1.0, 1.0) + maximum));
    frustum.SetWindow(GfRange2d(minimum, maximum));
    return frustum.ComputeProjectionMatrix();
}

bool VerifyProjection(GfFrustum frustum)
{
    const GfMatrix4d projection = frustum.ComputeProjectionMatrix();
    for (const auto& sample :
         {std::pair{32, 32}, std::pair{0, 0}, std::pair{63, 63},
          std::pair{11, 47}})
    {
        const GfMatrix4d expected =
            PinnedTestProjection(frustum, sample.first, sample.second, 64, 64);
        const GfMatrix4d actual =
            openusd_render_pick_detail::NarrowProjection(
                projection,
                sample.first,
                sample.second,
                64,
                64);
        if (!Near(expected, actual))
        {
            return false;
        }
    }
    return true;
}
}

int main()
{
    static_assert(sizeof(openusd_render_pick_request) == 344);
    static_assert(sizeof(openusd_render_pick_result) == 136);
    static_assert(sizeof(openusd_render_pick_instance_context) == 24);

    GfFrustum perspective;
    perspective.SetPerspective(45.0, 1.25, 0.1, 1000.0);
    GfFrustum orthographic;
    orthographic.SetOrthographic(-3.0, 5.0, -2.0, 4.0, 0.1, 100.0);
    if (!VerifyProjection(perspective) || !VerifyProjection(orthographic))
    {
        std::cerr << "One-pixel projection does not match the pinned OpenUSD test convention.\n";
        return 1;
    }

    openusd_render_pick_request request{};
    request.struct_size = sizeof(request);
    request.version = OPENUSD_RENDER_PICK_REQUEST_VERSION;
    request.width = 2;
    request.height = 1;
    request.viewport_width = 64;
    request.viewport_height = 64;
    request.resolve_mode = OPENUSD_RENDER_PICK_RESOLVE_NEAREST_TO_CENTER;
    std::string error;
    if (openusd_render_pick_detail::ValidateRequest(&request, error))
    {
        std::cerr << "Invalid multi-pixel request was accepted.\n";
        return 2;
    }

    openusd_render_pick_result result{};
    result.struct_size = sizeof(result);
    result.version = OPENUSD_RENDER_PICK_RESULT_VERSION;
    result.status = OPENUSD_RENDER_PICK_STATUS_HIT;
    result.instance_index = 12;
    if (!openusd_render_pick_detail::ValidateResult(&result, error) ||
        result.status != OPENUSD_RENDER_PICK_STATUS_INVALID ||
        result.instance_index != -1 ||
        result.normalized_depth != 1.0)
    {
        std::cerr << "Pick result initialization is not deterministic.\n";
        return 3;
    }

    return 0;
}
