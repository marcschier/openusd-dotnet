// Copyright (c) marcschier. Licensed under the MIT License.

#include "nurbsGeometry.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <utility>

namespace HdSilkNurbs
{
namespace
{
struct AxisSample
{
    double parameter = 0.0;
    size_t span = 0;
};

bool Fail(std::string* error, const char* message)
{
    if (error != nullptr)
    {
        *error = message;
    }
    return false;
}

bool AddSize(size_t left, size_t right, size_t* result)
{
    if (right > std::numeric_limits<size_t>::max() - left)
    {
        return false;
    }
    *result = left + right;
    return true;
}

bool MultiplySize(size_t left, size_t right, size_t* result)
{
    if (left != 0 && right > std::numeric_limits<size_t>::max() / left)
    {
        return false;
    }
    *result = left * right;
    return true;
}

} // namespace

bool TryAddEvaluationWork(
    size_t uOrder,
    size_t vOrder,
    size_t sampleCount,
    bool isPatch,
    size_t* totalWork)
{
    if (totalWork == nullptr || uOrder == 0 || sampleCount == 0 ||
        (isPatch && vOrder == 0))
    {
        return false;
    }

    size_t uBasisWork = 0;
    size_t perSampleWork = 0;
    if (!MultiplySize(uOrder, uOrder, &uBasisWork) ||
        (isPatch && !MultiplySize(uBasisWork, 2, &uBasisWork)) ||
        !AddSize(uBasisWork, isPatch ? 0 : uOrder, &perSampleWork))
    {
        return false;
    }
    if (isPatch)
    {
        size_t vBasisWork = 0;
        size_t controlNetWork = 0;
        if (!MultiplySize(vOrder, vOrder, &vBasisWork) ||
            !MultiplySize(vBasisWork, 2, &vBasisWork) ||
            !MultiplySize(uOrder, vOrder, &controlNetWork) ||
            !MultiplySize(controlNetWork, 4, &controlNetWork) ||
            !AddSize(perSampleWork, vBasisWork, &perSampleWork) ||
            !AddSize(perSampleWork, controlNetWork, &perSampleWork))
        {
            return false;
        }
    }

    // The squared terms bound basis and derivative recurrences; the remaining
    // terms bound rational point and partial-derivative accumulation.
    size_t additionalWork = 0;
    size_t updatedTotal = 0;
    if (!MultiplySize(perSampleWork, sampleCount, &additionalWork) ||
        !AddSize(*totalWork, additionalWork, &updatedTotal))
    {
        return false;
    }
    *totalWork = updatedTotal;
    return true;
}

namespace
{
bool IsFinite(const Point& point)
{
    return std::isfinite(point.x) && std::isfinite(point.y) &&
        std::isfinite(point.z);
}

bool ValidateWeights(
    const std::vector<double>& weights,
    size_t offset,
    size_t count)
{
    bool hasPositive = false;
    bool hasNegative = false;
    for (size_t index = offset; index < offset + count; ++index)
    {
        const double weight = weights[index];
        if (!std::isfinite(weight))
        {
            return false;
        }
        hasPositive = hasPositive || weight > 0.0;
        hasNegative = hasNegative || weight < 0.0;
    }
    return !(hasPositive && hasNegative);
}

bool ValidateAxis(
    size_t vertexCount,
    size_t order,
    const std::vector<double>& knots,
    bool hasRange,
    const Range& authoredRange,
    Range* range,
    std::string* error)
{
    size_t requiredKnots = 0;
    if (order == 0 || vertexCount < order ||
        !AddSize(vertexCount, order, &requiredKnots) ||
        knots.size() != requiredKnots)
    {
        return Fail(error, "NURBS order, vertex count, and knot count are inconsistent.");
    }
    if (!std::all_of(knots.begin(), knots.end(), [](double value) {
            return std::isfinite(value);
        }) || !std::is_sorted(knots.begin(), knots.end()))
    {
        return Fail(error, "NURBS knots must be finite and monotonically increasing.");
    }

    const size_t degree = order - 1;
    const double domainMinimum = knots[degree];
    const double domainMaximum = knots[vertexCount];
    *range = hasRange
        ? authoredRange
        : Range{domainMinimum, domainMaximum};
    if (!std::isfinite(range->minimum) || !std::isfinite(range->maximum) ||
        range->minimum < domainMinimum || range->maximum > domainMaximum ||
        !(range->minimum < range->maximum))
    {
        return Fail(error, "NURBS parameter range is outside its knot domain.");
    }
    return true;
}

bool BuildAxisSamples(
    const std::vector<double>& knots,
    size_t vertexCount,
    size_t order,
    const Range& range,
    size_t maxSamples,
    std::vector<AxisSample>* samples,
    std::vector<bool>* connections,
    std::string* error)
{
    samples->clear();
    connections->clear();
    const size_t degree = order - 1;
    for (size_t span = degree; span < vertexCount; ++span)
    {
        const double start = std::max(knots[span], range.minimum);
        const double end = std::min(knots[span + 1], range.maximum);
        if (!(start < end))
        {
            continue;
        }

        const bool firstSpan = samples->empty();
        bool splitAtStart = false;
        if (!firstSpan)
        {
            const double knot = knots[span];
            const auto equalRange = std::equal_range(knots.begin(), knots.end(), knot);
            const size_t multiplicity =
                static_cast<size_t>(std::distance(equalRange.first, equalRange.second));
            splitAtStart = multiplicity >= order;
        }

        for (size_t step = 0; step <= SamplesPerKnotSpan; ++step)
        {
            if (step == 0 && !firstSpan && !splitAtStart)
            {
                continue;
            }
            const double fraction =
                static_cast<double>(step) /
                static_cast<double>(SamplesPerKnotSpan);
            if (samples->size() >= maxSamples)
            {
                return Fail(error, "NURBS tessellation exceeds its point limit.");
            }
            samples->push_back({start + (end - start) * fraction, span});
            if (samples->size() > 1)
            {
                connections->push_back(step != 0 || !splitAtStart);
            }
        }

    }

    if (samples->size() < 2 || connections->size() + 1 != samples->size())
    {
        return Fail(error, "NURBS parameter range has no complete non-zero knot span.");
    }
    return true;
}

bool ComputeBasis(
    const std::vector<double>& knots,
    size_t order,
    size_t span,
    double parameter,
    std::vector<double>* basis,
    std::vector<double>* derivative = nullptr)
{
    const size_t degree = order - 1;
    basis->assign(order, 0.0);
    std::vector<double> left(order, 0.0);
    std::vector<double> right(order, 0.0);
    (*basis)[0] = 1.0;

    for (size_t j = 1; j <= degree; ++j)
    {
        left[j] = parameter - knots[span + 1 - j];
        right[j] = knots[span + j] - parameter;
        double saved = 0.0;
        for (size_t r = 0; r < j; ++r)
        {
            const double denominator = right[r + 1] + left[j - r];
            const double term = denominator == 0.0
                ? 0.0
                : (*basis)[r] / denominator;
            (*basis)[r] = saved + right[r + 1] * term;
            saved = left[j - r] * term;
        }
        (*basis)[j] = saved;
    }
    if (!std::all_of(basis->begin(), basis->end(), [](double value) {
            return std::isfinite(value);
        }))
    {
        return false;
    }

    if (derivative != nullptr)
    {
        derivative->assign(order, 0.0);
        if (degree != 0)
        {
            std::vector<double> lowerBasis;
            if (!ComputeBasis(knots, order - 1, span, parameter, &lowerBasis))
            {
                return false;
            }
            const size_t firstIndex = span - degree;
            for (size_t index = 0; index < order; ++index)
            {
                const size_t knotIndex = firstIndex + index;
                if (index != 0)
                {
                    const double denominator =
                        knots[knotIndex + degree] - knots[knotIndex];
                    if (denominator != 0.0)
                    {
                        (*derivative)[index] +=
                            static_cast<double>(degree) *
                            lowerBasis[index - 1] / denominator;
                    }
                }
                if (index < degree)
                {
                    const double denominator =
                        knots[knotIndex + degree + 1] - knots[knotIndex + 1];
                    if (denominator != 0.0)
                    {
                        (*derivative)[index] -=
                            static_cast<double>(degree) *
                            lowerBasis[index] / denominator;
                    }
                }
            }
        }
        if (!std::all_of(derivative->begin(), derivative->end(), [](double value) {
                return std::isfinite(value);
            }))
        {
            return false;
        }
    }
    return true;
}

bool EvaluateRationalPoint(
    const std::vector<Point>& points,
    const std::vector<double>& weights,
    const std::vector<double>& uKnots,
    size_t uOrder,
    size_t uSpan,
    double u,
    const std::vector<double>* vKnots,
    size_t vOrder,
    size_t vSpan,
    double v,
    size_t vVertexCount,
    size_t pointOffset,
    Point* result,
    Point* uDerivative = nullptr,
    Point* vDerivative = nullptr,
    Point* mixedDerivative = nullptr)
{
    if (mixedDerivative != nullptr &&
        (uDerivative == nullptr || vDerivative == nullptr || vKnots == nullptr))
    {
        return false;
    }
    std::vector<double> uBasis;
    std::vector<double> uBasisDerivative;
    if (!ComputeBasis(
            uKnots,
            uOrder,
            uSpan,
            u,
            &uBasis,
            uDerivative == nullptr ? nullptr : &uBasisDerivative))
    {
        return false;
    }

    std::vector<double> vBasis;
    std::vector<double> vBasisDerivative;
    if (vKnots != nullptr &&
        !ComputeBasis(
            *vKnots,
            vOrder,
            vSpan,
            v,
            &vBasis,
            vDerivative == nullptr ? nullptr : &vBasisDerivative))
    {
        return false;
    }

    Point weighted{};
    Point weightedU{};
    Point weightedV{};
    Point weightedUV{};
    double denominator = 0.0;
    double denominatorU = 0.0;
    double denominatorV = 0.0;
    double denominatorUV = 0.0;
    double denominatorScale = 0.0;
    const size_t uStart = uSpan - (uOrder - 1);
    const size_t vStart = vKnots == nullptr ? 0 : vSpan - (vOrder - 1);
    const size_t uLimit = uBasis.size();
    const size_t vLimit = vKnots == nullptr ? 1 : vBasis.size();
    for (size_t vIndex = 0; vIndex < vLimit; ++vIndex)
    {
        for (size_t uIndex = 0; uIndex < uLimit; ++uIndex)
        {
            const size_t controlIndex = pointOffset +
                (vKnots == nullptr
                    ? uStart + uIndex
                    : (uStart + uIndex) * vVertexCount + vStart + vIndex);
            if (controlIndex >= points.size())
            {
                return false;
            }
            const double weight = weights.empty() ? 1.0 : weights[controlIndex];
            const double coefficient = uBasis[uIndex] *
                (vKnots == nullptr ? 1.0 : vBasis[vIndex]) * weight;
            weighted.x += coefficient * points[controlIndex].x;
            weighted.y += coefficient * points[controlIndex].y;
            weighted.z += coefficient * points[controlIndex].z;
            denominator += coefficient;
            denominatorScale += std::abs(coefficient);
            if (uDerivative != nullptr)
            {
                const double coefficientU = uBasisDerivative[uIndex] *
                    (vKnots == nullptr ? 1.0 : vBasis[vIndex]) * weight;
                weightedU.x += coefficientU * points[controlIndex].x;
                weightedU.y += coefficientU * points[controlIndex].y;
                weightedU.z += coefficientU * points[controlIndex].z;
                denominatorU += coefficientU;
            }
            if (vDerivative != nullptr)
            {
                const double coefficientV = uBasis[uIndex] *
                    vBasisDerivative[vIndex] * weight;
                weightedV.x += coefficientV * points[controlIndex].x;
                weightedV.y += coefficientV * points[controlIndex].y;
                weightedV.z += coefficientV * points[controlIndex].z;
                denominatorV += coefficientV;
            }
            if (mixedDerivative != nullptr)
            {
                const double coefficientUV = uBasisDerivative[uIndex] *
                    vBasisDerivative[vIndex] * weight;
                weightedUV.x += coefficientUV * points[controlIndex].x;
                weightedUV.y += coefficientUV * points[controlIndex].y;
                weightedUV.z += coefficientUV * points[controlIndex].z;
                denominatorUV += coefficientUV;
            }
        }
    }

    if (!std::isfinite(denominator) || !std::isfinite(denominatorScale) ||
        denominatorScale == 0.0 ||
        std::abs(denominator) <=
            std::numeric_limits<double>::epsilon() * 8.0 * denominatorScale)
    {
        return false;
    }
    result->x = weighted.x / denominator;
    result->y = weighted.y / denominator;
    result->z = weighted.z / denominator;
    if (!IsFinite(*result))
    {
        return false;
    }
    if (uDerivative != nullptr)
    {
        uDerivative->x = (weightedU.x - result->x * denominatorU) / denominator;
        uDerivative->y = (weightedU.y - result->y * denominatorU) / denominator;
        uDerivative->z = (weightedU.z - result->z * denominatorU) / denominator;
        if (!IsFinite(*uDerivative))
        {
            return false;
        }
    }
    if (vDerivative != nullptr)
    {
        vDerivative->x = (weightedV.x - result->x * denominatorV) / denominator;
        vDerivative->y = (weightedV.y - result->y * denominatorV) / denominator;
        vDerivative->z = (weightedV.z - result->z * denominatorV) / denominator;
        if (!IsFinite(*vDerivative))
        {
            return false;
        }
    }
    if (mixedDerivative != nullptr)
    {
        // From A = S * w: S_uv = (A_uv - S_u * w_v - S_v * w_u - S * w_uv) / w.
        mixedDerivative->x = (weightedUV.x - uDerivative->x * denominatorV -
            vDerivative->x * denominatorU - result->x * denominatorUV) /
            denominator;
        mixedDerivative->y = (weightedUV.y - uDerivative->y * denominatorV -
            vDerivative->y * denominatorU - result->y * denominatorUV) /
            denominator;
        mixedDerivative->z = (weightedUV.z - uDerivative->z * denominatorV -
            vDerivative->z * denominatorU - result->z * denominatorUV) /
            denominator;
        if (!IsFinite(*mixedDerivative))
        {
            return false;
        }
    }
    return true;
}

Point Cross(const Point& left, const Point& right)
{
    return {
        left.y * right.z - left.z * right.y,
        left.z * right.x - left.x * right.z,
        left.x * right.y - left.y * right.x};
}

Point Normalize(const Point& value)
{
    const double lengthSquared =
        value.x * value.x + value.y * value.y + value.z * value.z;
    if (!(lengthSquared > 0.0) || !std::isfinite(lengthSquared))
    {
        return {};
    }
    const double inverseLength = 1.0 / std::sqrt(lengthSquared);
    return {
        value.x * inverseLength,
        value.y * inverseLength,
        value.z * inverseLength};
}

double Length(const Point& value)
{
    return std::sqrt(value.x * value.x + value.y * value.y + value.z * value.z);
}

// uInterior and vInterior give the direction of the patch interior from a
// sample on the first (+1) or last (-1) sampled parameter, or 0 for an interior
// sample. At a collapsed edge one first partial vanishes along the edge, so
// S_v(u0 + h) ~= h * S_uv (or S_u(v0 + h) ~= h * S_uv), and the normal tends to
// sign(h) * S_u x S_uv (or sign(h) * S_uv x S_v) from the interior side.
bool ComputePatchNormal(
    const Point& uDerivative,
    const Point& vDerivative,
    const Point& mixedDerivative,
    int uInterior,
    int vInterior,
    Point* normal)
{
    constexpr double DegenerateTolerance = 1.0e-12;
    constexpr double VanishingTolerance = 1.0e-6;
    const double uLength = Length(uDerivative);
    const double vLength = Length(vDerivative);
    const double scale = std::max(uLength, vLength);
    if (!std::isfinite(scale) || !(scale > 0.0))
    {
        return false;
    }

    const Point cross = Cross(uDerivative, vDerivative);
    const double crossLength = Length(cross);
    if (!std::isfinite(crossLength))
    {
        return false;
    }
    if (crossLength > DegenerateTolerance * scale * scale)
    {
        *normal = Normalize(cross);
        return true;
    }

    const bool uVanishes = uLength <= VanishingTolerance * scale;
    const bool vVanishes = vLength <= VanishingTolerance * scale;
    if (uVanishes == vVanishes)
    {
        return false;
    }

    Point limit;
    if (vVanishes)
    {
        if (uInterior == 0)
        {
            return false;
        }
        limit = Cross(uDerivative, mixedDerivative);
        if (uInterior < 0)
        {
            limit = {-limit.x, -limit.y, -limit.z};
        }
    }
    else
    {
        if (vInterior == 0)
        {
            return false;
        }
        limit = Cross(mixedDerivative, vDerivative);
        if (vInterior < 0)
        {
            limit = {-limit.x, -limit.y, -limit.z};
        }
    }

    const double limitScale = std::max(scale, Length(mixedDerivative));
    const double limitLength = Length(limit);
    if (!std::isfinite(limitLength) ||
        !(limitLength > DegenerateTolerance * limitScale * limitScale))
    {
        return false;
    }
    *normal = Normalize(limit);
    return true;
}
} // namespace

namespace
{
constexpr const char* WorkLimitMessage =
    "NURBS evaluation exceeds the 67,108,864-unit work limit; reduce order or sampled range.";

struct CurveLayout
{
    size_t vertexCount = 0;
    size_t order = 0;
    size_t pointOffset = 0;
    std::vector<double> knots;
    std::vector<AxisSample> samples;
    std::vector<bool> connections;
};

struct PatchLayout
{
    std::vector<AxisSample> uSamples;
    std::vector<bool> uConnections;
    std::vector<AxisSample> vSamples;
    std::vector<bool> vConnections;
    size_t faceCount = 0;
};

// Every evaluated sample is charged once for the position and once for each
// retained attribute list, so the bound covers attribute evaluation as well.
bool AddChargedWork(
    size_t uOrder,
    size_t vOrder,
    size_t sampleCount,
    bool isPatch,
    size_t attributeLists,
    size_t* totalWork,
    std::string* error)
{
    size_t chargedSamples = 0;
    if (!MultiplySize(sampleCount, attributeLists + 1, &chargedSamples) ||
        !TryAddEvaluationWork(uOrder, vOrder, chargedSamples, isPatch, totalWork) ||
        *totalWork > MaxEvaluationWork)
    {
        return Fail(error, WorkLimitMessage);
    }
    return true;
}

// Builds every part of the tessellation that does not depend on point,
// weight, or attribute values. Planning and evaluation share it, so a layout
// that plans successfully is the layout evaluation tessellates.
bool BuildCurvesLayout(
    const CurvesInput& input,
    size_t attributeLists,
    std::vector<CurveLayout>* layouts,
    size_t* controlPointCount,
    GeometryPlan* plan,
    std::string* error)
{
    layouts->clear();
    *plan = {};
    if (attributeLists > MaxAttributeLists)
    {
        return Fail(error, "NURBS geometry has more retained vertex attribute lists than supported.");
    }
    if (input.vertexCounts.empty() ||
        input.vertexCounts.size() != input.orders.size() ||
        (!input.ranges.empty() && input.ranges.size() != input.vertexCounts.size()))
    {
        return Fail(error, "NURBS curve points, weights, orders, or ranges are inconsistent.");
    }

    size_t pointTotal = 0;
    size_t knotTotal = 0;
    for (size_t i = 0; i < input.vertexCounts.size(); ++i)
    {
        if (input.vertexCounts[i] <= 0 ||
            input.orders[i] <= 0 ||
            !AddSize(pointTotal, static_cast<size_t>(input.vertexCounts[i]), &pointTotal) ||
            !AddSize(
                knotTotal,
                static_cast<size_t>(input.vertexCounts[i]) +
                    static_cast<size_t>(input.orders[i]),
                &knotTotal))
        {
            return Fail(error, "NURBS curve control counts or orders are invalid.");
        }
    }
    if (knotTotal != input.knots.size())
    {
        return Fail(error, "NURBS curve points and concatenated knot vectors do not match.");
    }

    GeometryPlan result;
    size_t pointOffset = 0;
    size_t knotOffset = 0;
    size_t evaluationWork = 0;
    layouts->reserve(input.vertexCounts.size());
    for (size_t curve = 0; curve < input.vertexCounts.size(); ++curve)
    {
        CurveLayout layout;
        layout.vertexCount = static_cast<size_t>(input.vertexCounts[curve]);
        layout.order = static_cast<size_t>(input.orders[curve]);
        layout.pointOffset = pointOffset;
        const size_t curveKnotCount = layout.vertexCount + layout.order;
        layout.knots.assign(
            input.knots.begin() + static_cast<std::ptrdiff_t>(knotOffset),
            input.knots.begin() + static_cast<std::ptrdiff_t>(knotOffset + curveKnotCount));
        Range range;
        if (!ValidateAxis(
                layout.vertexCount,
                layout.order,
                layout.knots,
                !input.ranges.empty(),
                input.ranges.empty() ? Range{} : input.ranges[curve],
                &range,
                error) ||
            !BuildAxisSamples(
                layout.knots,
                layout.vertexCount,
                layout.order,
                range,
                MaxTessellatedPoints - result.points,
                &layout.samples,
                &layout.connections,
                error) ||
            !AddChargedWork(
                layout.order,
                0,
                layout.samples.size(),
                false,
                attributeLists,
                &evaluationWork,
                error))
        {
            return false;
        }
        size_t nextPointCount = 0;
        if (!AddSize(result.points, layout.samples.size(), &nextPointCount) ||
            nextPointCount > MaxTessellatedPoints ||
            nextPointCount >
                static_cast<size_t>(std::numeric_limits<int32_t>::max()))
        {
            return Fail(error, "NURBS curve tessellation exceeds the supported point count.");
        }
        const size_t runs = 1 + static_cast<size_t>(std::count(
            layout.connections.begin(), layout.connections.end(), false));
        result.points = nextPointCount;
        result.runs += runs;
        result.elements += layout.samples.size() - runs;
        pointOffset += layout.vertexCount;
        knotOffset += curveKnotCount;
        layouts->push_back(std::move(layout));
    }
    *controlPointCount = pointTotal;
    *plan = result;
    return true;
}

bool BuildPatchLayout(
    const PatchInput& input,
    size_t attributeLists,
    PatchLayout* layout,
    size_t* controlPointCount,
    GeometryPlan* plan,
    std::string* error)
{
    *layout = {};
    *plan = {};
    if (input.hasTrimData)
    {
        return Fail(error, "Trimmed NURBS patches are not supported.");
    }
    if (attributeLists > MaxAttributeLists)
    {
        return Fail(error, "NURBS geometry has more retained vertex attribute lists than supported.");
    }
    if (input.uVertexCount <= 0 || input.vVertexCount <= 0 ||
        input.uOrder <= 0 || input.vOrder <= 0)
    {
        return Fail(error, "NURBS patch points, weights, counts, or orders are invalid.");
    }
    if (!MultiplySize(
            static_cast<size_t>(input.uVertexCount),
            static_cast<size_t>(input.vVertexCount),
            controlPointCount))
    {
        return Fail(error, "NURBS patch point count does not match its control grid.");
    }

    Range uRange;
    Range vRange;
    if (!ValidateAxis(
            static_cast<size_t>(input.uVertexCount),
            static_cast<size_t>(input.uOrder),
            input.uKnots,
            input.hasURange,
            input.uRange,
            &uRange,
            error) ||
        !ValidateAxis(
            static_cast<size_t>(input.vVertexCount),
            static_cast<size_t>(input.vOrder),
            input.vKnots,
            input.hasVRange,
            input.vRange,
            &vRange,
            error))
    {
        return false;
    }

    if (!BuildAxisSamples(
            input.uKnots,
            static_cast<size_t>(input.uVertexCount),
            static_cast<size_t>(input.uOrder),
            uRange,
            MaxTessellatedPoints,
            &layout->uSamples,
            &layout->uConnections,
            error) ||
        !BuildAxisSamples(
            input.vKnots,
            static_cast<size_t>(input.vVertexCount),
            static_cast<size_t>(input.vOrder),
            vRange,
            layout->uSamples.empty()
                ? 0
                : MaxTessellatedPoints / layout->uSamples.size(),
            &layout->vSamples,
            &layout->vConnections,
            error))
    {
        return false;
    }

    size_t sampleCount = 0;
    if (!MultiplySize(layout->uSamples.size(), layout->vSamples.size(), &sampleCount) ||
        sampleCount > MaxTessellatedPoints ||
        sampleCount > static_cast<size_t>(std::numeric_limits<int32_t>::max()))
    {
        return Fail(error, "NURBS patch tessellation exceeds the supported point count.");
    }
    size_t evaluationWork = 0;
    if (!AddChargedWork(
            static_cast<size_t>(input.uOrder),
            static_cast<size_t>(input.vOrder),
            sampleCount,
            true,
            attributeLists,
            &evaluationWork,
            error))
    {
        return false;
    }

    const size_t uFaces = static_cast<size_t>(std::count(
        layout->uConnections.begin(), layout->uConnections.end(), true));
    const size_t vFaces = static_cast<size_t>(std::count(
        layout->vConnections.begin(), layout->vConnections.end(), true));
    size_t indexCount = 0;
    if (!MultiplySize(uFaces, vFaces, &layout->faceCount) ||
        !MultiplySize(layout->faceCount, 4, &indexCount) ||
        indexCount > static_cast<size_t>(std::numeric_limits<int32_t>::max()))
    {
        return Fail(error, "NURBS patch tessellation exceeds the supported index count.");
    }
    plan->points = sampleCount;
    plan->elements = layout->faceCount;
    plan->runs = 0;
    return true;
}

bool ValidateAttributes(
    const std::vector<std::vector<Point>>& attributes,
    size_t controlPointCount,
    std::string* error)
{
    for (const std::vector<Point>& attribute : attributes)
    {
        if (attribute.size() != controlPointCount ||
            !std::all_of(attribute.begin(), attribute.end(), IsFinite))
        {
            return Fail(
                error,
                "NURBS vertex attribute values must be finite and match the control point count.");
        }
    }
    return true;
}
} // namespace

bool PlanCurves(
    const CurvesInput& input,
    size_t attributeLists,
    GeometryPlan* plan,
    std::string* error)
{
    std::vector<CurveLayout> layouts;
    size_t controlPointCount = 0;
    GeometryPlan result;
    if (!BuildCurvesLayout(input, attributeLists, &layouts, &controlPointCount, &result, error))
    {
        *plan = {};
        return false;
    }
    *plan = result;
    return true;
}

bool PlanPatch(
    const PatchInput& input,
    size_t attributeLists,
    GeometryPlan* plan,
    std::string* error)
{
    PatchLayout layout;
    size_t controlPointCount = 0;
    GeometryPlan result;
    if (!BuildPatchLayout(input, attributeLists, &layout, &controlPointCount, &result, error))
    {
        *plan = {};
        return false;
    }
    *plan = result;
    return true;
}

bool EvaluateCurves(
    const CurvesInput& input,
    CurvesOutput* output,
    std::string* error)
{
    *output = {};
    if (input.vertexCounts.empty() ||
        input.vertexCounts.size() != input.orders.size() ||
        (!input.ranges.empty() && input.ranges.size() != input.vertexCounts.size()) ||
        (!input.weights.empty() && input.weights.size() != input.points.size()) ||
        !std::all_of(input.points.begin(), input.points.end(), IsFinite))
    {
        return Fail(error, "NURBS curve points, weights, orders, or ranges are inconsistent.");
    }

    std::vector<CurveLayout> layouts;
    size_t controlPointCount = 0;
    GeometryPlan plan;
    if (!BuildCurvesLayout(
            input, input.attributes.size(), &layouts, &controlPointCount, &plan, error))
    {
        return false;
    }
    if (controlPointCount != input.points.size())
    {
        return Fail(error, "NURBS curve points and concatenated knot vectors do not match.");
    }
    if (!ValidateAttributes(input.attributes, controlPointCount, error))
    {
        return false;
    }

    CurvesOutput result;
    result.points.reserve(plan.points);
    result.vertexCounts.reserve(plan.runs);
    result.attributes.resize(input.attributes.size());
    for (std::vector<Point>& attribute : result.attributes)
    {
        attribute.reserve(plan.points);
    }
    for (const CurveLayout& layout : layouts)
    {
        if (!input.weights.empty() &&
            !ValidateWeights(input.weights, layout.pointOffset, layout.vertexCount))
        {
            return Fail(error, "NURBS curve weights must be finite and have one sign per curve.");
        }
        for (const AxisSample& sample : layout.samples)
        {
            Point evaluated;
            if (!EvaluateRationalPoint(
                    input.points,
                    input.weights,
                    layout.knots,
                    layout.order,
                    sample.span,
                    sample.parameter,
                    nullptr,
                    0,
                    0,
                    0.0,
                    layout.vertexCount,
                    layout.pointOffset,
                    &evaluated))
            {
                return Fail(error, "NURBS curve evaluation produced a singular or non-finite point.");
            }
            result.points.push_back(evaluated);
            for (size_t list = 0; list < input.attributes.size(); ++list)
            {
                Point attribute;
                if (!EvaluateRationalPoint(
                        input.attributes[list],
                        input.weights,
                        layout.knots,
                        layout.order,
                        sample.span,
                        sample.parameter,
                        nullptr,
                        0,
                        0,
                        0.0,
                        layout.vertexCount,
                        layout.pointOffset,
                        &attribute))
                {
                    return Fail(error, "NURBS curve evaluation produced a singular or non-finite point.");
                }
                result.attributes[list].push_back(attribute);
            }
        }

        size_t runStart = 0;
        for (size_t sample = 1; sample < layout.samples.size(); ++sample)
        {
            if (!layout.connections[sample - 1])
            {
                result.vertexCounts.push_back(
                    static_cast<int32_t>(sample - runStart));
                runStart = sample;
            }
        }
        result.vertexCounts.push_back(
            static_cast<int32_t>(layout.samples.size() - runStart));
    }
    *output = std::move(result);
    return true;
}

bool EvaluatePatch(
    const PatchInput& input,
    PatchOutput* output,
    std::string* error)
{
    *output = {};
    if (input.hasTrimData)
    {
        return Fail(error, "Trimmed NURBS patches are not supported.");
    }
    if (input.uVertexCount <= 0 || input.vVertexCount <= 0 ||
        input.uOrder <= 0 || input.vOrder <= 0 ||
        (!input.weights.empty() && input.weights.size() != input.points.size()) ||
        (!input.weights.empty() &&
            !ValidateWeights(input.weights, 0, input.weights.size())) ||
        !std::all_of(input.points.begin(), input.points.end(), IsFinite))
    {
        return Fail(error, "NURBS patch points, weights, counts, or orders are invalid.");
    }

    size_t controlPointCount = 0;
    if (!MultiplySize(
            static_cast<size_t>(input.uVertexCount),
            static_cast<size_t>(input.vVertexCount),
            &controlPointCount) ||
        controlPointCount != input.points.size())
    {
        return Fail(error, "NURBS patch point count does not match its control grid.");
    }

    PatchLayout layout;
    GeometryPlan plan;
    if (!BuildPatchLayout(
            input, input.attributes.size(), &layout, &controlPointCount, &plan, error) ||
        !ValidateAttributes(input.attributes, controlPointCount, error))
    {
        return false;
    }

    const std::vector<AxisSample>& uSamples = layout.uSamples;
    const std::vector<AxisSample>& vSamples = layout.vSamples;
    PatchOutput result;
    result.points.resize(plan.points);
    result.normals.resize(plan.points);
    result.attributes.assign(input.attributes.size(), std::vector<Point>(plan.points));
    for (size_t v = 0; v < vSamples.size(); ++v)
    {
        for (size_t u = 0; u < uSamples.size(); ++u)
        {
            Point evaluated;
            Point uDerivative;
            Point vDerivative;
            Point mixedDerivative;
            if (!EvaluateRationalPoint(
                    input.points,
                    input.weights,
                    input.uKnots,
                    static_cast<size_t>(input.uOrder),
                    uSamples[u].span,
                    uSamples[u].parameter,
                    &input.vKnots,
                    static_cast<size_t>(input.vOrder),
                    vSamples[v].span,
                    vSamples[v].parameter,
                    static_cast<size_t>(input.vVertexCount),
                    0,
                    &evaluated,
                    &uDerivative,
                    &vDerivative,
                    &mixedDerivative))
            {
                return Fail(error, "NURBS patch evaluation produced a singular or non-finite point.");
            }
            const size_t index = v * uSamples.size() + u;
            result.points[index] = evaluated;
            Point normal;
            if (!ComputePatchNormal(
                    uDerivative,
                    vDerivative,
                    mixedDerivative,
                    u == 0 ? 1 : (u + 1 == uSamples.size() ? -1 : 0),
                    v == 0 ? 1 : (v + 1 == vSamples.size() ? -1 : 0),
                    &normal))
            {
                return Fail(
                    error,
                    "NURBS patch normal is undefined at a degenerate point; only collapsed range-boundary edges with a unique limiting normal are supported.");
            }
            if (input.leftHanded)
            {
                normal.x = -normal.x;
                normal.y = -normal.y;
                normal.z = -normal.z;
            }
            result.normals[index] = normal;
            for (size_t list = 0; list < input.attributes.size(); ++list)
            {
                if (!EvaluateRationalPoint(
                        input.attributes[list],
                        input.weights,
                        input.uKnots,
                        static_cast<size_t>(input.uOrder),
                        uSamples[u].span,
                        uSamples[u].parameter,
                        &input.vKnots,
                        static_cast<size_t>(input.vOrder),
                        vSamples[v].span,
                        vSamples[v].parameter,
                        static_cast<size_t>(input.vVertexCount),
                        0,
                        &result.attributes[list][index]))
                {
                    return Fail(error, "NURBS patch evaluation produced a singular or non-finite point.");
                }
            }
        }
    }

    result.faceVertexCounts.assign(layout.faceCount, 4);
    result.faceVertexIndices.reserve(layout.faceCount * 4);
    for (size_t v = 1; v < vSamples.size(); ++v)
    {
        if (!layout.vConnections[v - 1])
        {
            continue;
        }
        for (size_t u = 1; u < uSamples.size(); ++u)
        {
            if (!layout.uConnections[u - 1])
            {
                continue;
            }
            const size_t lowerLeft = (v - 1) * uSamples.size() + u - 1;
            const size_t lowerRight = lowerLeft + 1;
            const size_t upperLeft = lowerLeft + uSamples.size();
            const size_t upperRight = upperLeft + 1;
            result.faceVertexIndices.push_back(static_cast<int32_t>(lowerLeft));
            result.faceVertexIndices.push_back(static_cast<int32_t>(lowerRight));
            result.faceVertexIndices.push_back(static_cast<int32_t>(upperRight));
            result.faceVertexIndices.push_back(static_cast<int32_t>(upperLeft));
        }
    }

    *output = std::move(result);
    return true;
}

} // namespace HdSilkNurbs
