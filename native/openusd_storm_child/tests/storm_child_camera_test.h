// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_STORM_CHILD_CAMERA_TEST_H
#define OPENUSD_STORM_CHILD_CAMERA_TEST_H

#include "openusd_storm_child.h"

#include <cmath>
#include <cstdint>
#include <cstring>
#include <limits>

namespace openusd_storm_child_camera_test
{
static_assert(OPENUSD_STORM_CHILD_ABI_VERSION == 7);

inline openusd_render_camera AutomaticCamera()
{
    openusd_render_camera camera{};
    camera.struct_size = sizeof(camera);
    camera.mode = OPENUSD_RENDER_CAMERA_MODE_AUTO;
    return camera;
}

inline openusd_render_camera MatrixCamera(double marker = 0.0)
{
    openusd_render_camera camera{};
    camera.struct_size = sizeof(camera);
    camera.mode = OPENUSD_RENDER_CAMERA_MODE_MATRICES;
    camera.view[0] = 1.0;
    camera.view[5] = 1.0;
    camera.view[10] = 1.0;
    camera.view[15] = 1.0;
    camera.view[12] = marker;
    camera.projection[0] = 1.0;
    camera.projection[5] = 1.0;
    camera.projection[10] = 1.0;
    camera.projection[15] = 1.0;
    return camera;
}

inline uint64_t CameraSignature(const openusd_render_camera& camera)
{
    uint64_t hash = 14695981039346656037ull;
    const auto append = [&hash](uint64_t value)
    {
        for (int shift = 0; shift < 64; shift += 8)
        {
            hash ^= static_cast<uint8_t>((value >> shift) & 0xffu);
            hash *= 1099511628211ull;
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
    return hash;
}

inline bool RejectsCamera(
    openusd_storm_child* child,
    const openusd_render_camera* camera,
    openusd_error_buffer* error)
{
    uint64_t frame_count = 99;
    int32_t converged = 1;
    return openusd_storm_child_render(
               child,
               0.0,
               camera,
               &frame_count,
               &converged,
               error) == OPENUSD_STATUS_INVALID_ARGUMENT &&
        frame_count == 0 &&
        converged == 0;
}

inline bool VerifyInvalidCameras(
    openusd_storm_child* child,
    openusd_error_buffer* error)
{
    openusd_render_camera invalid = MatrixCamera();
    invalid.struct_size = sizeof(invalid) - 1;
    if (!RejectsCamera(child, &invalid, error))
    {
        return false;
    }
    invalid = MatrixCamera();
    invalid.mode = static_cast<openusd_render_camera_mode>(99);
    if (!RejectsCamera(child, &invalid, error))
    {
        return false;
    }
    invalid = MatrixCamera();
    invalid.view[2] = std::nan("");
    if (!RejectsCamera(child, &invalid, error))
    {
        return false;
    }
    invalid = MatrixCamera();
    invalid.projection[6] = std::numeric_limits<double>::infinity();
    return RejectsCamera(child, &invalid, error) &&
        RejectsCamera(child, nullptr, error);
}
}

#endif
