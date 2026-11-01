// Copyright (c) marcschier. Licensed under the MIT License.

#pragma once

#include "pxr/base/gf/bbox3d.h"
#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/range3d.h"
#include "pxr/base/gf/vec3d.h"
#include "pxr/pxr.h"

#include <cmath>

PXR_NAMESPACE_OPEN_SCOPE

inline bool HdSilkIsFiniteVolumeTransform(const GfMatrix4d& transform)
{
    for (int row = 0; row < 4; ++row)
    {
        for (int column = 0; column < 4; ++column)
        {
            if (!std::isfinite(transform[row][column]))
            {
                return false;
            }
        }
    }
    return true;
}

inline bool HdSilkTryGetVolumeProxyTransform(const GfBBox3d& bounds, GfMatrix4d* transform)
{
    const GfRange3d& range = bounds.GetRange();
    if (range.IsEmpty())
    {
        return false;
    }

    const GfVec3d size = range.GetMax() - range.GetMin();
    const GfVec3d center = range.GetMin() + 0.5 * size;
    for (int axis = 0; axis < 3; ++axis)
    {
        if (!std::isfinite(size[axis]) || size[axis] <= 0.0 ||
            !std::isfinite(center[axis]))
        {
            return false;
        }
    }

    const GfMatrix4d& fieldTransform = bounds.GetMatrix();
    if (!HdSilkIsFiniteVolumeTransform(fieldTransform))
    {
        return false;
    }
    const double determinant = fieldTransform.GetDeterminant3();
    if (!std::isfinite(determinant) || determinant == 0.0 ||
        fieldTransform[0][3] != 0.0 || fieldTransform[1][3] != 0.0 ||
        fieldTransform[2][3] != 0.0 || fieldTransform[3][3] != 1.0)
    {
        return false;
    }

    const GfMatrix4d mapped =
        GfMatrix4d(1.0).SetScale(size) *
        GfMatrix4d(1.0).SetTranslate(center) *
        fieldTransform;
    if (!HdSilkIsFiniteVolumeTransform(mapped))
    {
        return false;
    }
    *transform = mapped;
    return true;
}

inline bool HdSilkTryComposeVolumeProxyTransform(
    const GfBBox3d& bounds, const GfMatrix4d& volumeTransform, GfMatrix4d* transform)
{
    GfMatrix4d proxyToVolume;
    if (!HdSilkIsFiniteVolumeTransform(volumeTransform) ||
        !HdSilkTryGetVolumeProxyTransform(bounds, &proxyToVolume))
    {
        return false;
    }
    const GfMatrix4d proxyToWorld = proxyToVolume * volumeTransform;
    if (!HdSilkIsFiniteVolumeTransform(proxyToWorld))
    {
        return false;
    }
    *transform = proxyToWorld;
    return true;
}

PXR_NAMESPACE_CLOSE_SCOPE
