// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_STORM_CHILD_AOV_H
#define OPENUSD_STORM_CHILD_AOV_H

#include "openusd_storm_child.h"
#include "openusd_render_camera_internal.h"

#include <cmath>
#include <memory>
#include <string>

struct OpenUsdStormChildAovPayload
{
    using Owner = std::unique_ptr<
        openusd_storm_aov_owner, decltype(&openusd_storm_aov_release)>;

    openusd_storm_aov_request request{};
    Owner owner{nullptr, openusd_storm_aov_release};

    static bool Validate(
        const openusd_storm_aov_request* value,
        std::string& error)
    {
        if (value == nullptr ||
            value->struct_size != sizeof(openusd_storm_aov_request) ||
            value->version != OPENUSD_STORM_AOV_VERSION)
        {
            error = "The Storm child AOV request size or version is invalid.";
            return false;
        }
        if (value->framebuffer != 0 ||
            value->width <= 0 || value->height <= 0 ||
            static_cast<uint32_t>(value->width) > OPENUSD_STORM_AOV_MAX_DIMENSION ||
            static_cast<uint32_t>(value->height) > OPENUSD_STORM_AOV_MAX_DIMENSION ||
            PixelCount(*value) > OPENUSD_STORM_AOV_MAX_PIXELS)
        {
            error = "Storm child AOV capture requires its owned framebuffer and "
                "positive dimensions within 4096 per side and 1048576 pixels.";
            return false;
        }
        if (!std::isfinite(value->time_code) ||
            !openusd_render_camera_detail::Validate(&value->camera, error))
        {
            if (error.empty())
            {
                error = "Storm child AOV render time must be finite.";
            }
            return false;
        }
        if (value->max_working_bytes > OPENUSD_STORM_AOV_MAX_WORKING_BYTES ||
            value->max_working_bytes <= CompanionWorkingBytes(*value))
        {
            error = "The Storm child AOV working-byte limit cannot admit its "
                "native RGBA8 companion and bounded command storage.";
            return false;
        }
        return true;
    }

    static uint64_t PixelCount(const openusd_storm_aov_request& value) noexcept
    {
        return static_cast<uint64_t>(value.width) *
            static_cast<uint64_t>(value.height);
    }

    static uint64_t CompanionWorkingBytes(
        const openusd_storm_aov_request& value) noexcept
    {
        return PixelCount(value) * 4u +
            OPENUSD_STORM_CHILD_AOV_WORKING_ALLOWANCE_BYTES;
    }

    openusd_status Execute(
        openusd_storm_renderer* renderer,
        uint32_t framebuffer,
        openusd_error_buffer* error)
    {
        openusd_storm_aov_request forwarded = request;
        forwarded.framebuffer = framebuffer;
        forwarded.max_working_bytes -= CompanionWorkingBytes(request);
        openusd_storm_aov_owner* captured = nullptr;
        const openusd_status status = openusd_storm_aov_capture(
            renderer, &forwarded, &captured, error);
        owner.reset(captured);
        return status;
    }
};

#endif
