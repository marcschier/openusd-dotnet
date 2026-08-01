// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_RENDER_CAMERA_INTERNAL_H
#define OPENUSD_RENDER_CAMERA_INTERNAL_H

#include "openusd_render_camera.h"

#include <cmath>
#include <cstdint>
#include <cstring>
#include <string>

namespace openusd_render_camera_detail
{
inline constexpr uint32_t MaxClipPlaneCount = 8;

inline openusd_render_camera Automatic() noexcept
{
    openusd_render_camera camera{};
    camera.struct_size = sizeof(camera);
    camera.mode = OPENUSD_RENDER_CAMERA_MODE_AUTO;
    return camera;
}

inline bool Validate(
    const openusd_render_camera* camera,
    std::string& error)
{
    if (camera == nullptr)
    {
        error = "A render camera is required.";
        return false;
    }
    if (camera->struct_size != sizeof(openusd_render_camera))
    {
        error = "The render camera structure has an invalid size.";
        return false;
    }
    if (camera->clip_plane_count > MaxClipPlaneCount)
    {
        error = "The render camera clip plane count is invalid.";
        return false;
    }
    for (uint32_t plane = 0; plane < camera->clip_plane_count; ++plane)
    {
        for (double value : camera->clip_planes[plane])
        {
            if (!std::isfinite(value))
            {
                error = "The render camera clip planes must be finite.";
                return false;
            }
        }
    }
    if (camera->mode == OPENUSD_RENDER_CAMERA_MODE_AUTO)
    {
        return true;
    }
    if (camera->mode != OPENUSD_RENDER_CAMERA_MODE_MATRICES)
    {
        error = "The render camera mode is invalid.";
        return false;
    }

    for (double value : camera->view)
    {
        if (!std::isfinite(value))
        {
            error = "The render camera view matrix must be finite.";
            return false;
        }
    }
    for (double value : camera->projection)
    {
        if (!std::isfinite(value))
        {
            error = "The render camera projection matrix must be finite.";
            return false;
        }
    }
    return true;
}

template <typename TMatrix>
inline void AssignRowMajor(
    const double* values,
    TMatrix& matrix)
{
    matrix = TMatrix(
        values[0],
        values[1],
        values[2],
        values[3],
        values[4],
        values[5],
        values[6],
        values[7],
        values[8],
        values[9],
        values[10],
        values[11],
        values[12],
        values[13],
        values[14],
        values[15]);
}

inline uint64_t Signature(const openusd_render_camera& camera) noexcept
{
    uint64_t hash = UINT64_C(14695981039346656037);
    const auto append = [&hash](uint64_t value)
    {
        for (int shift = 0; shift < 64; shift += 8)
        {
            hash ^= static_cast<uint8_t>((value >> shift) & UINT64_C(0xff));
            hash *= UINT64_C(1099511628211);
        }
    };

    append(static_cast<uint32_t>(camera.mode));
    if (camera.mode == OPENUSD_RENDER_CAMERA_MODE_MATRICES)
    {
        for (double value : camera.view)
        {
            uint64_t bits = 0;
            std::memcpy(&bits, &value, sizeof(bits));
            append(bits);
        }
        for (double value : camera.projection)
        {
            uint64_t bits = 0;
            std::memcpy(&bits, &value, sizeof(bits));
            append(bits);
        }
    }
    if (camera.clip_plane_count != 0)
    {
        append(camera.clip_plane_count);
        for (uint32_t plane = 0; plane < camera.clip_plane_count; ++plane)
        {
            for (double value : camera.clip_planes[plane])
            {
                uint64_t bits = 0;
                std::memcpy(&bits, &value, sizeof(bits));
                append(bits);
            }
        }
    }
    return hash;
}
}

#endif
