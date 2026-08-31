// Copyright (c) marcschier. Licensed under the MIT License.

#include "curveWidths.h"

#include "openusd_hdsilk.h"

#include "pxr/base/gf/half.h"
#include "pxr/imaging/hd/tokens.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <stdexcept>
#include <utility>

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
bool ExtractWidthValues(const VtValue& value, std::vector<float>* out)
{
    out->clear();
    if (value.IsEmpty())
    {
        return true;
    }
    if (value.IsHolding<float>())
    {
        out->push_back(value.UncheckedGet<float>());
        return true;
    }
    if (value.IsHolding<double>())
    {
        out->push_back(static_cast<float>(value.UncheckedGet<double>()));
        return true;
    }
    // UsdGeomCurves declares widths as float[], but a scene index or a
    // delegate is free to hand Hydra the half-precision primvar it authored,
    // and GfHalf converts to float exactly.
    if (value.IsHolding<GfHalf>())
    {
        out->push_back(static_cast<float>(value.UncheckedGet<GfHalf>()));
        return true;
    }
    if (value.IsHolding<VtFloatArray>())
    {
        const VtFloatArray& widths = value.UncheckedGet<VtFloatArray>();
        out->assign(widths.begin(), widths.end());
        return true;
    }
    if (value.IsHolding<VtDoubleArray>())
    {
        const VtDoubleArray& widths = value.UncheckedGet<VtDoubleArray>();
        out->reserve(widths.size());
        for (double width : widths)
        {
            out->push_back(static_cast<float>(width));
        }
        return true;
    }
    if (value.IsHolding<VtHalfArray>())
    {
        const VtHalfArray& widths = value.UncheckedGet<VtHalfArray>();
        out->reserve(widths.size());
        for (GfHalf width : widths)
        {
            out->push_back(static_cast<float>(width));
        }
        return true;
    }
    return false;
}

/// Clamps authored widths onto the non-negative finite range USD defines for
/// them. A non-finite width is rejected rather than clamped: it is authoring
/// corruption, not a legitimate zero-width curve.
bool SanitizeWidthValues(std::vector<float>* values)
{
    for (float& width : *values)
    {
        if (!std::isfinite(width))
        {
            return false;
        }
        width = std::max(width, 0.0f);
    }
    return true;
}

/// Resolves the authored width for one emitted line endpoint. Vertex and
/// varying widths are indexed by the resolved point index, exactly as the
/// position is, so an indexed topology reads the same slot for both.
float ResolveWidthAt(
    const HdSilkCurveWidths& widths,
    size_t curveIndex,
    size_t pointIndex)
{
    switch (widths.interpolation)
    {
    case HdSilkCurveWidthInterpolation::Uniform:
        return widths.values[curveIndex];
    case HdSilkCurveWidthInterpolation::Vertex:
        return widths.values[pointIndex];
    case HdSilkCurveWidthInterpolation::Constant:
    default:
        return widths.values.front();
    }
}

bool IsInvisible(const VtIntArray& values, int value)
{
    return std::find(values.begin(), values.end(), value) != values.end();
}

void AddEyeFacingNormals(HdSilkMeshRecord* record)
{
    HdSilkMeshAttribute normals;
    normals.name = HdTokens->normals.GetString();
    normals.semantic = OPENUSD_SILK_ATTRIBUTE_NORMAL;
    normals.componentCount = 3;
    normals.interpolation = OPENUSD_SILK_INTERPOLATION_VERTEX;
    const size_t pointCount = record->points.size() / 3;
    normals.data.reserve(pointCount * 3);
    for (size_t point = 0; point < pointCount; ++point)
    {
        normals.data.push_back(0.0f);
        normals.data.push_back(0.0f);
        normals.data.push_back(1.0f);
    }
    record->attributes.push_back(std::move(normals));
}

/// Publishes the widths already resolved onto the emitted line vertices.
/// Constant widths collapse to a single wire element so an unchanged
/// constant-width scene keeps the payload it had before non-constant widths
/// were supported.
void AddResolvedWidths(
    const HdSilkCurveWidths& widths,
    std::vector<float> emitted,
    HdSilkMeshRecord* record)
{
    HdSilkMeshAttribute attribute;
    attribute.name = HdTokens->widths.GetString();
    attribute.semantic = OPENUSD_SILK_ATTRIBUTE_WIDTH;
    attribute.componentCount = 1;
    if (widths.interpolation == HdSilkCurveWidthInterpolation::Constant)
    {
        attribute.interpolation = OPENUSD_SILK_INTERPOLATION_CONSTANT;
        attribute.data.assign(1, widths.values.front());
    }
    else
    {
        attribute.interpolation = OPENUSD_SILK_INTERPOLATION_VERTEX;
        attribute.data = std::move(emitted);
    }
    record->attributes.push_back(std::move(attribute));
}
}

size_t
HdSilkExpectedCurveWidthCount(
    const HdBasisCurvesTopology& topology,
    size_t pointCount,
    HdSilkCurveWidthInterpolation interpolation)
{
    switch (interpolation)
    {
    case HdSilkCurveWidthInterpolation::Uniform:
        return topology.GetCurveVertexCounts().size();
    case HdSilkCurveWidthInterpolation::Vertex:
        return topology.HasIndices()
            ? pointCount
            : topology.CalculateNeededNumberOfControlPoints();
    case HdSilkCurveWidthInterpolation::Constant:
    default:
        return 1;
    }
}

bool
HdSilkCurveWidthCountMatches(
    const HdBasisCurvesTopology& topology,
    size_t pointCount,
    HdSilkCurveWidthInterpolation interpolation,
    size_t authoredCount)
{
    if (authoredCount ==
        HdSilkExpectedCurveWidthCount(topology, pointCount, interpolation))
    {
        return true;
    }
    // An unindexed curve resolves a point by its flattened control-point
    // ordinal, so a widths array parallel to a longer points array indexes the
    // same way and every lookup the line builder makes is still in range.
    return interpolation == HdSilkCurveWidthInterpolation::Vertex &&
        !topology.HasIndices() &&
        authoredCount == pointCount &&
        pointCount >= topology.CalculateNeededNumberOfControlPoints();
}

bool
HdSilkResolveCurveWidths(
    const HdBasisCurvesTopology& topology,
    size_t pointCount,
    const VtValue& authoredWidths,
    bool hasDeclaredInterpolation,
    HdSilkCurveWidthInterpolation declaredInterpolation,
    HdSilkCurveWidths* out)
{
    std::vector<float> authored;
    if (!ExtractWidthValues(authoredWidths, &authored) ||
        !SanitizeWidthValues(&authored))
    {
        return false;
    }
    if (authored.empty())
    {
        out->interpolation = HdSilkCurveWidthInterpolation::Constant;
        out->values.assign(1, HdSilkDefaultCurveWidth);
        return true;
    }
    if (hasDeclaredInterpolation &&
        HdSilkCurveWidthCountMatches(
            topology, pointCount, declaredInterpolation, authored.size()))
    {
        out->interpolation = declaredInterpolation;
        out->values = std::move(authored);
        return true;
    }

    const HdSilkCurveWidthInterpolation inferred[] = {
        HdSilkCurveWidthInterpolation::Constant,
        HdSilkCurveWidthInterpolation::Uniform,
        HdSilkCurveWidthInterpolation::Vertex};
    for (HdSilkCurveWidthInterpolation candidate : inferred)
    {
        if (HdSilkCurveWidthCountMatches(
                topology, pointCount, candidate, authored.size()))
        {
            out->interpolation = candidate;
            out->values = std::move(authored);
            return true;
        }
    }
    return false;
}

bool
HdSilkBuildLinearSegmentedCurveLines(
    const HdBasisCurvesTopology& topology,
    const VtVec3fArray& points,
    const HdSilkCurveWidths& widths,
    HdSilkMeshRecord* record)
{
    const VtIntArray& counts = topology.GetCurveVertexCounts();
    const VtIntArray& indices = topology.GetCurveIndices();
    const VtIntArray& invisibleCurves = topology.GetInvisibleCurves();
    const VtIntArray& invisiblePoints = topology.GetInvisiblePoints();
    std::vector<float> emittedWidths;

    // Uniform widths are indexed by curve, so a topology with more curves than
    // authored values would read out of range. Reject it here rather than in
    // the emit loop: the record must be discarded whole either way.
    if (widths.interpolation == HdSilkCurveWidthInterpolation::Uniform &&
        widths.values.size() < counts.size())
    {
        return false;
    }

    size_t vertexCursor = 0;
    uint32_t segmentIndex = 0;
    for (size_t curveIndex = 0; curveIndex < counts.size(); ++curveIndex)
    {
        const int count = counts[curveIndex];
        if (count < 0 || (count % 2) != 0)
        {
            return false;
        }
        if (vertexCursor > static_cast<size_t>(std::numeric_limits<int>::max()) ||
            static_cast<size_t>(count) >
                static_cast<size_t>(std::numeric_limits<int>::max()) - vertexCursor)
        {
            return false;
        }
        if (IsInvisible(invisibleCurves, static_cast<int>(curveIndex)))
        {
            vertexCursor += static_cast<size_t>(count);
            segmentIndex += static_cast<uint32_t>(count / 2);
            continue;
        }

        for (int local = 0; local < count; local += 2)
        {
            const size_t firstVertex = vertexCursor + static_cast<size_t>(local);
            const size_t secondVertex = firstVertex + 1;
            if (topology.HasIndices() && secondVertex >= indices.size())
            {
                return false;
            }
            const int firstIndex = topology.HasIndices()
                ? indices[firstVertex]
                : static_cast<int>(firstVertex);
            const int secondIndex = topology.HasIndices()
                ? indices[secondVertex]
                : static_cast<int>(secondVertex);
            if (firstIndex < 0 || secondIndex < 0 ||
                static_cast<size_t>(firstIndex) >= points.size() ||
                static_cast<size_t>(secondIndex) >= points.size())
            {
                return false;
            }
            // Vertex widths are parallel to points, so the resolved point index
            // has to be in range for them too. It is by construction when the
            // resolver accepted the authored array, and checking it keeps the
            // invariant local to the only place that indexes the array.
            if (widths.interpolation == HdSilkCurveWidthInterpolation::Vertex &&
                (static_cast<size_t>(firstIndex) >= widths.values.size() ||
                    static_cast<size_t>(secondIndex) >= widths.values.size()))
            {
                return false;
            }
            if (IsInvisible(invisiblePoints, firstIndex) ||
                IsInvisible(invisiblePoints, secondIndex))
            {
                ++segmentIndex;
                continue;
            }

            if ((record->points.size() / 3) >
                static_cast<size_t>(std::numeric_limits<uint32_t>::max()) - 2)
            {
                throw std::overflow_error(
                    "The hdSilk basisCurves line list exceeds the 32-bit vertex index.");
            }
            const uint32_t base = static_cast<uint32_t>(record->points.size() / 3);
            const GfVec3f line[] = {
                points[static_cast<size_t>(firstIndex)],
                points[static_cast<size_t>(secondIndex)]};
            for (const GfVec3f& point : line)
            {
                record->points.push_back(point[0]);
                record->points.push_back(point[1]);
                record->points.push_back(point[2]);
            }
            record->indices.insert(
                record->indices.end(),
                {base, base + 1});
            emittedWidths.push_back(
                ResolveWidthAt(
                    widths, curveIndex, static_cast<size_t>(firstIndex)));
            emittedWidths.push_back(
                ResolveWidthAt(
                    widths, curveIndex, static_cast<size_t>(secondIndex)));
            record->triangleSubprims.push_back(segmentIndex);
            ++segmentIndex;
        }
        vertexCursor += static_cast<size_t>(count);
    }
    if (record->indices.empty())
    {
        return false;
    }
    AddEyeFacingNormals(record);
    AddResolvedWidths(widths, std::move(emittedWidths), record);
    return true;
}

PXR_NAMESPACE_CLOSE_SCOPE
