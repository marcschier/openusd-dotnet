// Copyright (c) marcschier. Licensed under the MIT License.

#include "../src/volumeBounds.h"

#include <cmath>
#include <iostream>
#include <limits>
#include <stdexcept>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{

void Require(bool value, const char* message)
{
    if (!value)
    {
        throw std::runtime_error(message);
    }
}

void RequireRejected(const GfBBox3d& bounds, const char* message)
{
    const GfMatrix4d sentinel(7.0);
    GfMatrix4d result = sentinel;
    Require(!HdSilkTryGetVolumeProxyTransform(bounds, &result), message);
    Require(result == sentinel, "rejected bounds changed the output transform");
}

void ValidAffineBoundsMapEveryCorner()
{
    const GfRange3d range(GfVec3d(7, -3, 4), GfVec3d(38, 28, 35));
    const GfMatrix4d field(
        0.02, 0.006, 0, 0,
        0.004, -0.015, 0, 0,
        0, 0, 0.02, 0,
        -0.2, 0.25, 0.02, 1);
    GfMatrix4d proxy(0.0);
    Require(HdSilkTryGetVolumeProxyTransform(GfBBox3d(range, field), &proxy),
        "valid reflected and sheared bounds were rejected");
    for (int corner = 0; corner < 8; ++corner)
    {
        GfVec3d unit;
        GfVec3d index;
        for (int axis = 0; axis < 3; ++axis)
        {
            const bool high = (corner & (1 << axis)) != 0;
            unit[axis] = high ? 0.5 : -0.5;
            index[axis] = high ? range.GetMax()[axis] : range.GetMin()[axis];
        }
        const GfVec3d expected = field.Transform(index);
        const GfVec3d actual = proxy.Transform(unit);
        for (int axis = 0; axis < 3; ++axis)
        {
            Require(std::abs(actual[axis] - expected[axis]) < 1e-12,
                "proxy corner did not map to the field corner");
        }
    }

    const GfMatrix4d volume(
        0.8, 0.2, 0, 0,
        -0.1, 0.6, 0, 0,
        0, 0, 1.5, 0,
        2, -3, 0.5, 1);
    Require(HdSilkTryComposeVolumeProxyTransform(GfBBox3d(range, field), volume, &proxy),
        "valid volume transform was rejected");
    const GfVec3d expected = volume.Transform(field.Transform(range.GetMin()));
    const GfVec3d actual = proxy.Transform(GfVec3d(-0.5, -0.5, -0.5));
    for (int axis = 0; axis < 3; ++axis)
    {
        Require(std::abs(actual[axis] - expected[axis]) < 1e-12,
            "volume transform was applied in the wrong order");
    }
}

void InvalidRangesAreRejected()
{
    RequireRejected(GfBBox3d(), "empty bounds were accepted");
    const GfVec3d lower(1, 2, 3);
    for (int axis = 0; axis < 3; ++axis)
    {
        GfVec3d upper(4, 5, 6);
        upper[axis] = lower[axis];
        RequireRejected(GfBBox3d(GfRange3d(lower, upper)), "zero-size axis was accepted");
    }
    const double inf = std::numeric_limits<double>::infinity();
    const double nan = std::numeric_limits<double>::quiet_NaN();
    RequireRejected(GfBBox3d(GfRange3d(GfVec3d(0, 0, 0), GfVec3d(inf, 1, 1))),
        "infinite bounds were accepted");
    RequireRejected(GfBBox3d(GfRange3d(GfVec3d(nan, 0, 0), GfVec3d(1, 1, 1))),
        "NaN bounds were accepted");
    RequireRejected(
        GfBBox3d(GfRange3d(GfVec3d(-1e308, 0, 0), GfVec3d(1e308, 1, 1))),
        "overflowing bounds were accepted");
}

void InvalidMatricesAreRejected()
{
    const GfRange3d range(GfVec3d(0, 0, 0), GfVec3d(1, 2, 3));
    GfMatrix4d matrix(1.0);
    matrix[0][0] = 0;
    RequireRejected(GfBBox3d(range, matrix), "singular field matrix was accepted");

    for (int axis = 0; axis < 3; ++axis)
    {
        matrix = GfMatrix4d(1.0);
        matrix[axis][3] = 0.1;
        RequireRejected(GfBBox3d(range, matrix), "perspective field matrix was accepted");
    }
    matrix = GfMatrix4d(1.0);
    matrix[3][3] = 2;
    RequireRejected(GfBBox3d(range, matrix), "non-affine homogeneous coordinate was accepted");

    matrix = GfMatrix4d(1.0);
    matrix[1][1] = std::numeric_limits<double>::quiet_NaN();
    RequireRejected(GfBBox3d(range, matrix), "NaN field matrix was accepted");
    matrix = GfMatrix4d(1.0);
    matrix[3][2] = std::numeric_limits<double>::infinity();
    RequireRejected(GfBBox3d(range, matrix), "infinite field matrix was accepted");

    matrix = GfMatrix4d(1.0);
    matrix[0][0] = 1e200;
    matrix[1][1] = 1e-100;
    matrix[2][2] = 1e-100;
    RequireRejected(
        GfBBox3d(GfRange3d(GfVec3d(-5e199, 0, 0), GfVec3d(5e199, 1, 1)), matrix),
        "overflowing proxy transform was accepted");

    const GfMatrix4d sentinel(7.0);
    GfMatrix4d output = sentinel;
    GfMatrix4d volume(1.0);
    volume[0][0] = 1e200;
    Require(!HdSilkTryComposeVolumeProxyTransform(GfBBox3d(range, matrix), volume, &output),
        "overflowing proxy-to-world transform was accepted");
    Require(output == sentinel, "rejected composition changed the output transform");
    volume[0][0] = std::numeric_limits<double>::quiet_NaN();
    Require(!HdSilkTryComposeVolumeProxyTransform(GfBBox3d(range), volume, &output),
        "non-finite volume transform was accepted");
    Require(output == sentinel, "rejected volume transform changed the output");
}

} // namespace

int main()
{
    try
    {
        ValidAffineBoundsMapEveryCorner();
        InvalidRangesAreRejected();
        InvalidMatricesAreRejected();
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
