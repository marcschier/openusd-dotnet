// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_STORM_AOV_INTERNAL_H
#define OPENUSD_STORM_AOV_INTERNAL_H

#include "openusd_storm_aov.h"

#include "pxr/base/tf/token.h"
#include "pxr/pxr.h"

#include <array>
#include <atomic>
#include <memory>
#include <string>

PXR_NAMESPACE_OPEN_SCOPE
class UsdImagingGLEngine;
PXR_NAMESPACE_CLOSE_SCOPE

struct OpenUsdStormAovState
{
    std::shared_ptr<std::atomic_bool> owner_live =
        std::make_shared<std::atomic_bool>(false);
    std::array<uint64_t, OPENUSD_STORM_AOV_MAX_OUTPUTS> scratch_high_water{};
    uint64_t capture_id = 0;
};

namespace openusd_storm_aov_detail
{
openusd_status ValidateRequest(
    const openusd_storm_aov_request& request,
    const OpenUsdStormAovState& state,
    std::string& message);

PXR_NS::TfTokenVector OutputNames(const openusd_storm_aov_request& request);

openusd_status CopyCompleted(
    PXR_NS::UsdImagingGLEngine& engine,
    OpenUsdStormAovState& state,
    const openusd_storm_aov_request& request,
    const openusd_render_camera& applied_camera,
    openusd_storm_aov_owner** owner,
    std::string& message);

void GetView(const openusd_storm_aov_owner& owner, openusd_storm_aov_view& view) noexcept;

}

#endif
