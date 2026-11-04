// Copyright (c) marcschier. Licensed under the MIT License.

#include "nurbsGeometry.h"

#include <cmath>
#include <cstdint>
#include <iostream>
#include <limits>
#include <string>
#include <utility>
#include <vector>

namespace
{
bool Near(double actual, double expected)
{
    return std::abs(actual - expected) < 1e-6;
}

bool NearPoint(
    const HdSilkNurbs::Point& actual,
    const HdSilkNurbs::Point& expected)
{
    return Near(actual.x, expected.x) &&
        Near(actual.y, expected.y) &&
        Near(actual.z, expected.z);
}

bool IsFinite(const HdSilkNurbs::Point& point)
{
    return std::isfinite(point.x) &&
        std::isfinite(point.y) &&
        std::isfinite(point.z);
}

HdSilkNurbs::Point Subtract(
    const HdSilkNurbs::Point& left,
    const HdSilkNurbs::Point& right)
{
    return {left.x - right.x, left.y - right.y, left.z - right.z};
}

HdSilkNurbs::Point Cross(
    const HdSilkNurbs::Point& left,
    const HdSilkNurbs::Point& right)
{
    return {
        left.y * right.z - left.z * right.y,
        left.z * right.x - left.x * right.z,
        left.x * right.y - left.y * right.x};
}

double Dot(
    const HdSilkNurbs::Point& left,
    const HdSilkNurbs::Point& right)
{
    return left.x * right.x + left.y * right.y + left.z * right.z;
}

bool HasConsistentPatchOrientation(
    const HdSilkNurbs::PatchOutput& output,
    const HdSilkNurbs::Point& expectedCenterCross)
{
    if (output.points.size() != 81 || output.normals.size() != 81 ||
        output.faceVertexIndices.size() < 4 ||
        output.faceVertexIndices[0] != 0 ||
        output.faceVertexIndices[1] != 1 ||
        output.faceVertexIndices[2] != 10 ||
        output.faceVertexIndices[3] != 9)
    {
        return false;
    }

    const double expectedLength =
        std::sqrt(Dot(expectedCenterCross, expectedCenterCross));
    if (!(expectedLength > 0.0))
    {
        return false;
    }
    const HdSilkNurbs::Point expectedCenterNormal = {
        expectedCenterCross.x / expectedLength,
        expectedCenterCross.y / expectedLength,
        expectedCenterCross.z / expectedLength};
    const HdSilkNurbs::Point firstFaceU =
        Subtract(output.points[1], output.points[0]);
    const HdSilkNurbs::Point firstFaceV =
        Subtract(output.points[9], output.points[0]);
    return NearPoint(output.normals[40], expectedCenterNormal) &&
        Dot(Cross(firstFaceU, firstFaceV), output.normals[0]) > 0.0;
}

bool HasFinitePointsAndMatchingRuns(const HdSilkNurbs::CurvesOutput& output)
{
    if (output.vertexCounts.empty())
    {
        return false;
    }
    std::size_t pointCount = 0;
    for (int32_t vertexCount : output.vertexCounts)
    {
        if (vertexCount <= 0)
        {
            return false;
        }
        pointCount += static_cast<std::size_t>(vertexCount);
    }
    if (pointCount != output.points.size())
    {
        return false;
    }
    for (const HdSilkNurbs::Point& point : output.points)
    {
        if (!IsFinite(point))
        {
            return false;
        }
    }
    return true;
}

bool SameCurveOutput(
    const HdSilkNurbs::CurvesOutput& left,
    const HdSilkNurbs::CurvesOutput& right)
{
    if (left.vertexCounts != right.vertexCounts ||
        left.points.size() != right.points.size())
    {
        return false;
    }
    for (std::size_t i = 0; i < left.points.size(); ++i)
    {
        if (!NearPoint(left.points[i], right.points[i]))
        {
            return false;
        }
    }
    return true;
}

HdSilkNurbs::CurvesInput MakeSingleCurve(
    std::vector<HdSilkNurbs::Point> points,
    std::vector<double> weights,
    int32_t order,
    std::vector<double> knots,
    bool hasRange = false,
    HdSilkNurbs::Range range = {})
{
    HdSilkNurbs::CurvesInput input;
    input.points = std::move(points);
    input.weights = std::move(weights);
    input.vertexCounts = {static_cast<int32_t>(input.points.size())};
    input.orders = {order};
    input.knots = std::move(knots);
    if (hasRange)
    {
        input.ranges = {range};
    }
    return input;
}

HdSilkNurbs::CurvesInput MakeSingleSpanCurve(int32_t order)
{
    HdSilkNurbs::CurvesInput input;
    input.points.assign(
        static_cast<std::size_t>(order),
        HdSilkNurbs::Point{1.0, 2.0, 3.0});
    input.vertexCounts = {order};
    input.orders = {order};
    input.knots.resize(static_cast<std::size_t>(order) * 2);
    for (std::size_t knot = 0; knot < input.knots.size(); ++knot)
    {
        input.knots[knot] = knot < static_cast<std::size_t>(order) ? 0.0 : 1.0;
    }
    return input;
}

int Fail(const std::string& message)
{
    std::cerr << message << "\n";
    return 1;
}

int VerifyPatchTessellationCapPreflight()
{
    constexpr int32_t uControlCount = 128;
    constexpr int32_t vControlCount = 130;
    const auto makeOrderTwoKnots = [](int32_t vertexCount)
    {
        std::vector<double> knots;
        knots.reserve(static_cast<std::size_t>(vertexCount) + 2);
        knots.push_back(0.0);
        knots.push_back(0.0);
        for (int32_t knot = 1; knot < vertexCount - 1; ++knot)
        {
            knots.push_back(static_cast<double>(knot));
        }
        knots.push_back(static_cast<double>(vertexCount - 1));
        knots.push_back(static_cast<double>(vertexCount - 1));
        return knots;
    };

    HdSilkNurbs::PatchInput patch;
    patch.uVertexCount = uControlCount;
    patch.vVertexCount = vControlCount;
    patch.uOrder = 2;
    patch.vOrder = 2;
    patch.uKnots = makeOrderTwoKnots(uControlCount);
    patch.vKnots = makeOrderTwoKnots(vControlCount);
    patch.points.resize(
        static_cast<std::size_t>(uControlCount) *
        static_cast<std::size_t>(vControlCount));
    constexpr std::size_t uSampleCount =
        std::size_t{8} * static_cast<std::size_t>(uControlCount - 1) + 1;
    constexpr std::size_t vSampleCount =
        std::size_t{8} * static_cast<std::size_t>(vControlCount - 1) + 1;
    constexpr std::size_t permittedVSamples =
        HdSilkNurbs::MaxTessellatedPoints / uSampleCount;
    if (uSampleCount != 1017 || vSampleCount != 1033 ||
        permittedVSamples != 1031 ||
        permittedVSamples * uSampleCount > HdSilkNurbs::MaxTessellatedPoints ||
        (permittedVSamples + 1) * uSampleCount <=
            HdSilkNurbs::MaxTessellatedPoints ||
        uSampleCount * vSampleCount <= HdSilkNurbs::MaxTessellatedPoints)
    {
        return Fail("Over-cap patch fixture no longer crosses the tessellation preflight limit.");
    }

    HdSilkNurbs::PatchOutput output;
    std::string error;
    if (HdSilkNurbs::EvaluatePatch(patch, &output, &error) ||
        error != "NURBS tessellation exceeds its point limit." ||
        !output.points.empty() || !output.normals.empty() ||
        !output.faceVertexCounts.empty() || !output.faceVertexIndices.empty())
    {
        return Fail("Over-cap patch was not refused before tessellated output allocation.");
    }
    return 0;
}

// A periodic quadratic ring in U with rational weights. Unclamped uniform
// knots and order - 1 repeated control rows close the surface, so the seam
// samples must coincide in position and normal.
HdSilkNurbs::PatchInput MakePeriodicRingPatch()
{
    const HdSilkNurbs::Point ring[4] = {
        {1.0, 0.0, 0.0}, {0.0, 1.0, 0.0}, {-1.0, 0.0, 0.0}, {0.0, -1.0, 0.0}};
    const double ringWeights[4] = {1.0, 2.0, 1.0, 2.0};
    HdSilkNurbs::PatchInput patch;
    patch.uVertexCount = 6;
    patch.vVertexCount = 2;
    patch.uOrder = 3;
    patch.vOrder = 2;
    patch.uKnots = {0.0, 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0};
    patch.vKnots = {0.0, 0.0, 1.0, 1.0};
    for (int32_t u = 0; u < patch.uVertexCount; ++u)
    {
        for (int32_t v = 0; v < patch.vVertexCount; ++v)
        {
            HdSilkNurbs::Point point = ring[u % 4];
            point.z = static_cast<double>(v);
            patch.points.push_back(point);
            patch.weights.push_back(ringWeights[u % 4]);
        }
    }
    return patch;
}

int VerifyPeriodicPatch()
{
    const HdSilkNurbs::PatchInput patch = MakePeriodicRingPatch();
    HdSilkNurbs::PatchOutput output;
    std::string error;
    constexpr std::size_t uSamples = 33;
    constexpr std::size_t vSamples = 9;
    if (!HdSilkNurbs::EvaluatePatch(patch, &output, &error))
    {
        return Fail("Periodic rational patch was refused: " + error);
    }
    if (output.points.size() != uSamples * vSamples ||
        output.faceVertexCounts.size() != (uSamples - 1) * (vSamples - 1))
    {
        return Fail("Periodic rational patch did not tessellate its four default-range spans.");
    }
    // At u = 2 the uniform quadratic basis is (1/2, 1/2, 0) over controls
    // 0..2, so the seam is the weight-averaged (1, 0) and (0, 1) ring points.
    const HdSilkNurbs::Point seam = {1.0 / 3.0, 2.0 / 3.0, 0.0};
    if (!NearPoint(output.points[0], seam))
    {
        return Fail("Periodic rational patch did not evaluate its analytic seam point.");
    }
    for (std::size_t v = 0; v < vSamples; ++v)
    {
        const std::size_t first = v * uSamples;
        const std::size_t last = first + uSamples - 1;
        if (!NearPoint(output.points[first], output.points[last]) ||
            !NearPoint(output.normals[first], output.normals[last]) ||
            !Near(output.normals[first].z, 0.0) ||
            !Near(output.points[first].z, static_cast<double>(v) / 8.0))
        {
            return Fail("Periodic rational patch seam positions or normals do not coincide.");
        }
    }

    // The closed form repeats only the first control row at the end. With
    // clamped knots the surface starts and ends on that row.
    HdSilkNurbs::PatchInput closed;
    closed.uVertexCount = 5;
    closed.vVertexCount = 2;
    closed.uOrder = 2;
    closed.vOrder = 2;
    closed.uKnots = {0.0, 0.0, 1.0, 2.0, 3.0, 4.0, 4.0};
    closed.vKnots = {0.0, 0.0, 1.0, 1.0};
    const HdSilkNurbs::Point square[4] = {
        {1.0, 0.0, 0.0}, {0.0, 1.0, 0.0}, {-1.0, 0.0, 0.0}, {0.0, -1.0, 0.0}};
    for (int32_t u = 0; u < closed.uVertexCount; ++u)
    {
        for (int32_t v = 0; v < closed.vVertexCount; ++v)
        {
            HdSilkNurbs::Point point = square[u % 4];
            point.z = static_cast<double>(v);
            closed.points.push_back(point);
        }
    }
    HdSilkNurbs::PatchOutput closedOutput;
    if (!HdSilkNurbs::EvaluatePatch(closed, &closedOutput, &error) ||
        closedOutput.points.size() != 33 * 9 ||
        !NearPoint(closedOutput.points[0], {1.0, 0.0, 0.0}) ||
        !NearPoint(closedOutput.points[32], {1.0, 0.0, 0.0}))
    {
        return Fail("Closed patch did not start and end on its repeated control row: " + error);
    }
    return 0;
}

int VerifyAttributesAndPlans()
{
    std::string error;
    HdSilkNurbs::CurvesInput curve;
    curve.points = {{1.0, 0.0, 0.0}, {1.0, 1.0, 0.0}, {0.0, 1.0, 0.0}};
    const double weight = std::sqrt(0.5);
    curve.weights = {1.0, weight, 1.0};
    curve.vertexCounts = {3};
    curve.orders = {3};
    curve.knots = {0.0, 0.0, 0.0, 1.0, 1.0, 1.0};
    curve.attributes = {{{0.0, 5.0, 0.0}, {1.0, 5.0, 0.0}, {3.0, 5.0, 0.0}}};

    HdSilkNurbs::CurvesOutput curveOutput;
    if (!HdSilkNurbs::EvaluateCurves(curve, &curveOutput, &error) ||
        curveOutput.attributes.size() != 1 ||
        curveOutput.attributes[0].size() != curveOutput.points.size())
    {
        return Fail("Curve vertex attribute was not interpolated: " + error);
    }
    // Quadratic Bernstein basis at t = 1/2 is (1/4, 1/2, 1/4). The rational
    // value differs from the polynomial value 1.25, so weights must apply.
    const double rationalMidpoint =
        (0.5 * weight * 1.0 + 0.25 * 3.0) / (0.5 + 0.5 * weight);
    if (!NearPoint(curveOutput.attributes[0][4], {rationalMidpoint, 5.0, 0.0}) ||
        !NearPoint(curveOutput.attributes[0][0], {0.0, 5.0, 0.0}) ||
        !NearPoint(curveOutput.attributes[0][8], {3.0, 5.0, 0.0}))
    {
        return Fail("Curve vertex attribute did not use the rational point weights.");
    }

    HdSilkNurbs::GeometryPlan curvePlan;
    HdSilkNurbs::CurvesInput curveTopology = curve;
    curveTopology.points.clear();
    curveTopology.weights.clear();
    curveTopology.attributes.clear();
    if (!HdSilkNurbs::PlanCurves(curveTopology, 1, &curvePlan, &error) ||
        curvePlan.points != curveOutput.points.size() ||
        curvePlan.runs != curveOutput.vertexCounts.size() ||
        curvePlan.elements != curvePlan.points - curvePlan.runs)
    {
        return Fail("Curve plan from topology alone does not match evaluated output.");
    }

    HdSilkNurbs::PatchInput patch = MakePeriodicRingPatch();
    patch.attributes = {patch.points, std::vector<HdSilkNurbs::Point>(
        patch.points.size(), HdSilkNurbs::Point{2.0, -1.0, 0.5})};
    HdSilkNurbs::PatchOutput patchOutput;
    if (!HdSilkNurbs::EvaluatePatch(patch, &patchOutput, &error) ||
        patchOutput.attributes.size() != 2)
    {
        return Fail("Patch vertex attributes were not interpolated: " + error);
    }
    for (std::size_t index = 0; index < patchOutput.points.size(); ++index)
    {
        if (!NearPoint(patchOutput.attributes[0][index], patchOutput.points[index]) ||
            !NearPoint(patchOutput.attributes[1][index], {2.0, -1.0, 0.5}))
        {
            return Fail("Patch vertex attributes did not follow the rational surface basis.");
        }
    }

    HdSilkNurbs::GeometryPlan patchPlan;
    HdSilkNurbs::PatchInput patchTopology = patch;
    patchTopology.points.clear();
    patchTopology.weights.clear();
    patchTopology.attributes.clear();
    if (!HdSilkNurbs::PlanPatch(patchTopology, 2, &patchPlan, &error) ||
        patchPlan.points != patchOutput.points.size() ||
        patchPlan.elements != patchOutput.faceVertexCounts.size() ||
        patchPlan.runs != 0)
    {
        return Fail("Patch plan from topology alone does not match evaluated output.");
    }
    patchTopology.hasTrimData = true;
    if (HdSilkNurbs::PlanPatch(patchTopology, 0, &patchPlan, &error) ||
        patchPlan.points != 0 ||
        error != "Trimmed NURBS patches are not supported.")
    {
        return Fail("Patch plan admitted trimmed patch topology.");
    }

    const std::string tooManyLists =
        "NURBS geometry has more retained vertex attribute lists than supported.";
    HdSilkNurbs::CurvesInput overCap = curve;
    overCap.attributes.assign(
        HdSilkNurbs::MaxAttributeLists + 1, curve.attributes[0]);
    HdSilkNurbs::CurvesOutput refusedCurve = curveOutput;
    if (HdSilkNurbs::PlanCurves(
            curveTopology,
            HdSilkNurbs::MaxAttributeLists + 1,
            &curvePlan,
            &error) ||
        error != tooManyLists ||
        HdSilkNurbs::EvaluateCurves(overCap, &refusedCurve, &error) ||
        error != tooManyLists ||
        !refusedCurve.points.empty() || !refusedCurve.attributes.empty())
    {
        return Fail("Attribute lists above the cap were not refused.");
    }
    overCap.attributes.resize(HdSilkNurbs::MaxAttributeLists);
    if (!HdSilkNurbs::EvaluateCurves(overCap, &refusedCurve, &error) ||
        refusedCurve.attributes.size() != HdSilkNurbs::MaxAttributeLists)
    {
        return Fail("Attribute lists at the cap were refused: " + error);
    }

    const std::string mismatch =
        "NURBS vertex attribute values must be finite and match the control point count.";
    HdSilkNurbs::CurvesInput shortAttribute = curve;
    shortAttribute.attributes[0].pop_back();
    HdSilkNurbs::PatchInput nonFiniteAttribute = patch;
    nonFiniteAttribute.attributes[1][3].y =
        std::numeric_limits<double>::quiet_NaN();
    HdSilkNurbs::PatchOutput refusedPatch = patchOutput;
    if (HdSilkNurbs::EvaluateCurves(shortAttribute, &refusedCurve, &error) ||
        error != mismatch || !refusedCurve.points.empty() ||
        HdSilkNurbs::EvaluatePatch(nonFiniteAttribute, &refusedPatch, &error) ||
        error != mismatch || !refusedPatch.points.empty() ||
        !refusedPatch.attributes.empty())
    {
        return Fail("Mismatched or non-finite vertex attributes were not refused.");
    }

    // Each attribute list repeats the basis evaluation, so it is charged as
    // work. Order 2730 sits just below the limit without attributes.
    const std::string workLimit =
        "NURBS evaluation exceeds the 67,108,864-unit work limit; reduce order or sampled range.";
    HdSilkNurbs::CurvesInput boundary = MakeSingleSpanCurve(2730);
    boundary.attributes = {boundary.points};
    HdSilkNurbs::CurvesInput boundaryTopology = boundary;
    boundaryTopology.points.clear();
    if (HdSilkNurbs::PlanCurves(boundaryTopology, 1, &curvePlan, &error) ||
        error != workLimit ||
        HdSilkNurbs::EvaluateCurves(boundary, &refusedCurve, &error) ||
        error != workLimit || !refusedCurve.points.empty() ||
        !HdSilkNurbs::PlanCurves(boundaryTopology, 0, &curvePlan, &error))
    {
        return Fail("Attribute evaluation work was not charged against the work limit.");
    }
    return 0;
}
} // namespace

int main()
{
    HdSilkNurbs::CurvesInput curve;
    curve.points = {{1.0, 0.0, 0.0}, {1.0, 1.0, 0.0}, {0.0, 1.0, 0.0}};
    curve.weights = {1.0, std::sqrt(0.5), 1.0};
    curve.vertexCounts = {3};
    curve.orders = {3};
    curve.knots = {0.0, 0.0, 0.0, 1.0, 1.0, 1.0};
    curve.ranges = {{0.0, 1.0}};

    HdSilkNurbs::CurvesOutput curveOutput;
    std::string error;
    if (!HdSilkNurbs::EvaluateCurves(curve, &curveOutput, &error))
    {
        return Fail("Rational NURBS curve evaluation failed: " + error);
    }
    if (curveOutput.vertexCounts != std::vector<int32_t>{9} ||
        curveOutput.points.size() != 9 ||
        !Near(curveOutput.points[4].x, std::sqrt(0.5)) ||
        !Near(curveOutput.points[4].y, std::sqrt(0.5)))
    {
        return Fail("Rational NURBS curve did not evaluate its authored quarter-circle midpoint.");
    }

    std::size_t boundaryCurveWork = 0;
    if (!HdSilkNurbs::TryAddEvaluationWork(
            2730, 0, 9, false, &boundaryCurveWork) ||
        boundaryCurveWork != 67100670 ||
        boundaryCurveWork > HdSilkNurbs::MaxEvaluationWork)
    {
        return Fail("Curve evaluation work estimate did not accept the value immediately below its limit.");
    }
    HdSilkNurbs::CurvesOutput workLimitCurveOutput = curveOutput;
    const HdSilkNurbs::CurvesInput boundaryCurve = MakeSingleSpanCurve(2730);
    if (!HdSilkNurbs::EvaluateCurves(
            boundaryCurve,
            &workLimitCurveOutput,
            &error) ||
        workLimitCurveOutput.points.size() != 9)
    {
        return Fail("A curve immediately below the evaluation work limit was refused: " + error);
    }

    std::size_t overLimitCurveWork = 0;
    if (!HdSilkNurbs::TryAddEvaluationWork(
            2731, 0, 9, false, &overLimitCurveWork) ||
        overLimitCurveWork <= HdSilkNurbs::MaxEvaluationWork)
    {
        return Fail("Curve evaluation work estimate did not cross its limit at the next tested order.");
    }
    const HdSilkNurbs::CurvesInput overLimitCurve = MakeSingleSpanCurve(2731);
    if (HdSilkNurbs::EvaluateCurves(
            overLimitCurve,
            &workLimitCurveOutput,
            &error) ||
        error != "NURBS evaluation exceeds the 67,108,864-unit work limit; reduce order or sampled range." ||
        !workLimitCurveOutput.points.empty() ||
        !workLimitCurveOutput.vertexCounts.empty())
    {
        return Fail("Over-limit curve work was not refused before publishing output.");
    }

    std::size_t overflowWork = 19;
    if (HdSilkNurbs::TryAddEvaluationWork(
            std::numeric_limits<std::size_t>::max(),
            0,
            1,
            false,
            &overflowWork) ||
        overflowWork != 19)
    {
        return Fail("Overflowing curve evaluation work was not rejected without changing its accumulator.");
    }
    overflowWork = std::numeric_limits<std::size_t>::max() - 1;
    if (HdSilkNurbs::TryAddEvaluationWork(2, 0, 3, false, &overflowWork) ||
        overflowWork != std::numeric_limits<std::size_t>::max() - 1)
    {
        return Fail("Overflowing accumulated evaluation work was not rejected without changing its accumulator.");
    }
    if (!HdSilkNurbs::EvaluateCurves(
            curve,
            &workLimitCurveOutput,
            &error) ||
        !HasFinitePointsAndMatchingRuns(workLimitCurveOutput))
    {
        return Fail("Curve evaluation did not recover after an over-limit refusal.");
    }

    HdSilkNurbs::PatchInput patch;
    patch.points = {
        {0.0, 0.0, 0.0}, {0.0, 1.0, 0.0},
        {1.0, 0.0, 0.0}, {1.0, 1.0, 0.0}};
    patch.weights = {1.0, 1.0, 2.0, 2.0};
    patch.uVertexCount = 2;
    patch.vVertexCount = 2;
    patch.uOrder = 2;
    patch.vOrder = 2;
    patch.uKnots = {0.0, 0.0, 1.0, 1.0};
    patch.vKnots = {0.0, 0.0, 1.0, 1.0};
    patch.uRange = {0.0, 1.0};
    patch.vRange = {0.0, 1.0};
    patch.hasURange = true;
    patch.hasVRange = true;

    HdSilkNurbs::PatchOutput patchOutput;
    if (!HdSilkNurbs::EvaluatePatch(patch, &patchOutput, &error))
    {
        return Fail("Rational NURBS patch evaluation failed: " + error);
    }
    if (patchOutput.points.size() != 81 ||
        patchOutput.normals.size() != 81 ||
        patchOutput.faceVertexCounts.size() != 64 ||
        patchOutput.faceVertexIndices.size() != 256 ||
        !Near(patchOutput.points[40].x, 2.0 / 3.0) ||
        !Near(patchOutput.points[40].y, 0.5) ||
        !Near(patchOutput.normals[40].z, 1.0))
    {
        return Fail("Rational NURBS patch did not use its weights or tessellate the unit knot span.");
    }

    HdSilkNurbs::PatchInput overLimitPatch;
    overLimitPatch.uVertexCount = 1000;
    overLimitPatch.vVertexCount = 2;
    overLimitPatch.uOrder = 1000;
    overLimitPatch.vOrder = 2;
    overLimitPatch.points.assign(2000, {2.0, 0.0, 0.0});
    overLimitPatch.uKnots.resize(2000);
    for (std::size_t knot = 0; knot < overLimitPatch.uKnots.size(); ++knot)
    {
        overLimitPatch.uKnots[knot] = knot < 1000 ? 0.0 : 1.0;
    }
    overLimitPatch.vKnots = {0.0, 0.0, 1.0, 1.0};
    std::size_t overLimitPatchWork = 0;
    if (!HdSilkNurbs::TryAddEvaluationWork(
            1000, 2, 81, true, &overLimitPatchWork) ||
        overLimitPatchWork != 162648648 ||
        overLimitPatchWork <= HdSilkNurbs::MaxEvaluationWork)
    {
        return Fail("Patch evaluation work estimate did not cross its limit for the high-order U fixture.");
    }
    HdSilkNurbs::PatchOutput workLimitPatchOutput = patchOutput;
    if (HdSilkNurbs::EvaluatePatch(
            overLimitPatch,
            &workLimitPatchOutput,
            &error) ||
        error != "NURBS evaluation exceeds the 67,108,864-unit work limit; reduce order or sampled range." ||
        !workLimitPatchOutput.points.empty() ||
        !workLimitPatchOutput.normals.empty() ||
        !workLimitPatchOutput.faceVertexCounts.empty() ||
        !workLimitPatchOutput.faceVertexIndices.empty())
    {
        return Fail("Over-limit patch work was not refused before publishing output.");
    }
    if (!HdSilkNurbs::EvaluatePatch(
            patch,
            &workLimitPatchOutput,
            &error) ||
        workLimitPatchOutput.points.size() != 81 ||
        workLimitPatchOutput.normals.size() != 81)
    {
        return Fail("Patch evaluation did not recover after an over-limit refusal.");
    }

    HdSilkNurbs::PatchInput quadraticOrderPatch;
    quadraticOrderPatch.points = {
        {0.0, 0.0, 0.0}, {0.0, 0.5, 2.0}, {0.0, 1.0, 0.0},
        {0.5, 0.0, 1.0}, {0.5, 0.5, 3.0}, {0.5, 1.0, 1.0},
        {1.0, 0.0, 0.0}, {1.0, 0.5, 2.0}, {1.0, 1.0, 0.0}};
    quadraticOrderPatch.uVertexCount = 3;
    quadraticOrderPatch.vVertexCount = 3;
    quadraticOrderPatch.uOrder = 3;
    quadraticOrderPatch.vOrder = 3;
    quadraticOrderPatch.uKnots = {0.0, 0.0, 0.0, 1.0, 1.0, 1.0};
    quadraticOrderPatch.vKnots = {0.0, 0.0, 0.0, 1.0, 1.0, 1.0};
    HdSilkNurbs::PatchOutput quadraticOrderPatchOutput;
    if (!HdSilkNurbs::EvaluatePatch(
            quadraticOrderPatch,
            &quadraticOrderPatchOutput,
            &error))
    {
        return Fail("Authored order-3 patch evaluation failed: " + error);
    }
    if (quadraticOrderPatchOutput.points.size() != 81 ||
        !NearPoint(quadraticOrderPatchOutput.points[40], {0.5, 0.5, 1.5}))
    {
        return Fail("Authored quadratic orders in both patch axes did not produce the analytic center.");
    }

    HdSilkNurbs::PatchInput rationalCylinderPatch;
    rationalCylinderPatch.points = {
        {1.0, 0.0, 0.0}, {1.0, 0.0, 1.0},
        {1.0, 1.0, 0.0}, {1.0, 1.0, 1.0},
        {0.0, 1.0, 0.0}, {0.0, 1.0, 1.0}};
    rationalCylinderPatch.weights = {
        1.0, 1.0,
        std::sqrt(0.5), std::sqrt(0.5),
        1.0, 1.0};
    rationalCylinderPatch.uVertexCount = 3;
    rationalCylinderPatch.vVertexCount = 2;
    rationalCylinderPatch.uOrder = 3;
    rationalCylinderPatch.vOrder = 2;
    rationalCylinderPatch.uKnots = {0.0, 0.0, 0.0, 1.0, 1.0, 1.0};
    rationalCylinderPatch.vKnots = {0.0, 0.0, 1.0, 1.0};
    HdSilkNurbs::PatchOutput rationalCylinderOutput;
    if (!HdSilkNurbs::EvaluatePatch(
            rationalCylinderPatch,
            &rationalCylinderOutput,
            &error) ||
        rationalCylinderOutput.points.size() != 81 ||
        rationalCylinderOutput.normals.size() != 81 ||
        !NearPoint(
            rationalCylinderOutput.points[40],
            {std::sqrt(0.5), std::sqrt(0.5), 0.5}) ||
        !NearPoint(
            rationalCylinderOutput.normals[40],
            {std::sqrt(0.5), std::sqrt(0.5), 0.0}) ||
        !NearPoint(rationalCylinderOutput.normals[44], {0.0, 1.0, 0.0}) ||
        !NearPoint(rationalCylinderOutput.normals[36], {1.0, 0.0, 0.0}))
    {
        return Fail("Weighted curved patch normals did not match analytic interior and boundary tangents.");
    }

    // Ruled surface between two rational quarter meridians sharing the pole
    // (0,0,1): S = (sin t * (1 - v), sin t * v, cos t). Independently,
    // S_u x S_v is parallel to (sin t, sin t, cos t), so the interior normal at
    // t = 45 degrees is (1,1,1)/sqrt(3) and the pole limit is (0,0,1) for
    // every v, although S_v vanishes along the whole collapsed u = 0 edge.
    const double halfRoot = std::sqrt(0.5);
    const double inverseRootThree = 1.0 / std::sqrt(3.0);
    HdSilkNurbs::PatchInput poleDome;
    poleDome.points = {
        {0.0, 0.0, 1.0}, {0.0, 0.0, 1.0},
        {1.0, 0.0, 1.0}, {0.0, 1.0, 1.0},
        {1.0, 0.0, 0.0}, {0.0, 1.0, 0.0}};
    poleDome.weights = {1.0, 1.0, halfRoot, halfRoot, 1.0, 1.0};
    poleDome.uVertexCount = 3;
    poleDome.vVertexCount = 2;
    poleDome.uOrder = 3;
    poleDome.vOrder = 2;
    poleDome.uKnots = {0.0, 0.0, 0.0, 1.0, 1.0, 1.0};
    poleDome.vKnots = {0.0, 0.0, 1.0, 1.0};
    HdSilkNurbs::PatchOutput poleDomeOutput;
    if (!HdSilkNurbs::EvaluatePatch(poleDome, &poleDomeOutput, &error) ||
        poleDomeOutput.points.size() != 81 ||
        poleDomeOutput.normals.size() != 81 ||
        !NearPoint(
            poleDomeOutput.points[40],
            {halfRoot * 0.5, halfRoot * 0.5, halfRoot}) ||
        !NearPoint(
            poleDomeOutput.normals[40],
            {inverseRootThree, inverseRootThree, inverseRootThree}))
    {
        return Fail("Collapsed-edge rational patch missed its analytic interior: " + error);
    }
    for (std::size_t row = 0; row < 9; ++row)
    {
        if (!NearPoint(poleDomeOutput.points[row * 9], {0.0, 0.0, 1.0}) ||
            !NearPoint(poleDomeOutput.normals[row * 9], {0.0, 0.0, 1.0}))
        {
            return Fail("Collapsed lower-U pole did not publish its unique limiting normal.");
        }
    }

    HdSilkNurbs::PatchInput upperPoleDome = poleDome;
    upperPoleDome.points = {
        {1.0, 0.0, 0.0}, {0.0, 1.0, 0.0},
        {1.0, 0.0, 1.0}, {0.0, 1.0, 1.0},
        {0.0, 0.0, 1.0}, {0.0, 0.0, 1.0}};
    HdSilkNurbs::PatchOutput upperPoleDomeOutput;
    if (!HdSilkNurbs::EvaluatePatch(upperPoleDome, &upperPoleDomeOutput, &error) ||
        upperPoleDomeOutput.normals.size() != 81 ||
        !NearPoint(
            upperPoleDomeOutput.normals[40],
            {-inverseRootThree, -inverseRootThree, -inverseRootThree}))
    {
        return Fail("Reversed collapsed-edge patch missed its analytic interior: " + error);
    }
    for (std::size_t row = 0; row < 9; ++row)
    {
        if (!NearPoint(upperPoleDomeOutput.normals[row * 9 + 8], {0.0, 0.0, -1.0}))
        {
            return Fail("Collapsed upper-U pole limit did not follow the interior orientation.");
        }
    }

    HdSilkNurbs::PatchInput vPoleDome;
    vPoleDome.points = {
        {0.0, 0.0, 1.0}, {1.0, 0.0, 1.0}, {1.0, 0.0, 0.0},
        {0.0, 0.0, 1.0}, {0.0, 1.0, 1.0}, {0.0, 1.0, 0.0}};
    vPoleDome.weights = {1.0, halfRoot, 1.0, 1.0, halfRoot, 1.0};
    vPoleDome.uVertexCount = 2;
    vPoleDome.vVertexCount = 3;
    vPoleDome.uOrder = 2;
    vPoleDome.vOrder = 3;
    vPoleDome.uKnots = {0.0, 0.0, 1.0, 1.0};
    vPoleDome.vKnots = {0.0, 0.0, 0.0, 1.0, 1.0, 1.0};
    HdSilkNurbs::PatchOutput vPoleDomeOutput;
    if (!HdSilkNurbs::EvaluatePatch(vPoleDome, &vPoleDomeOutput, &error) ||
        vPoleDomeOutput.normals.size() != 81 ||
        !NearPoint(
            vPoleDomeOutput.normals[40],
            {-inverseRootThree, -inverseRootThree, -inverseRootThree}))
    {
        return Fail("Collapsed lower-V patch missed its analytic interior: " + error);
    }
    for (std::size_t column = 0; column < 9; ++column)
    {
        if (!NearPoint(vPoleDomeOutput.points[column], {0.0, 0.0, 1.0}) ||
            !NearPoint(vPoleDomeOutput.normals[column], {0.0, 0.0, -1.0}))
        {
            return Fail("Collapsed lower-V pole did not publish its unique limiting normal.");
        }
    }

    HdSilkNurbs::PatchInput leftHandedPoleDome = poleDome;
    leftHandedPoleDome.leftHanded = true;
    HdSilkNurbs::PatchOutput leftHandedPoleDomeOutput;
    if (!HdSilkNurbs::EvaluatePatch(
            leftHandedPoleDome,
            &leftHandedPoleDomeOutput,
            &error) ||
        leftHandedPoleDomeOutput.normals.size() != 81 ||
        !NearPoint(leftHandedPoleDomeOutput.normals[0], {0.0, 0.0, -1.0}))
    {
        return Fail("Left-handed collapsed pole did not reverse its limiting normal.");
    }

    // A collapsed edge whose neighbourhood is a line: S = (u * (1 + v), 0, 0).
    // At the pole S_v vanishes and S_u x S_uv also vanishes, so no normal exists.
    HdSilkNurbs::PatchInput lineDegeneratePatch;
    lineDegeneratePatch.points = {
        {0.0, 0.0, 0.0}, {0.0, 0.0, 0.0},
        {1.0, 0.0, 0.0}, {2.0, 0.0, 0.0}};
    lineDegeneratePatch.uVertexCount = 2;
    lineDegeneratePatch.vVertexCount = 2;
    lineDegeneratePatch.uOrder = 2;
    lineDegeneratePatch.vOrder = 2;
    lineDegeneratePatch.uKnots = {0.0, 0.0, 1.0, 1.0};
    lineDegeneratePatch.vKnots = {0.0, 0.0, 1.0, 1.0};
    const std::string undefinedNormalError =
        "NURBS patch normal is undefined at a degenerate point; only collapsed range-boundary edges with a unique limiting normal are supported.";
    HdSilkNurbs::PatchOutput degenerateOutput = poleDomeOutput;
    if (HdSilkNurbs::EvaluatePatch(
            lineDegeneratePatch,
            &degenerateOutput,
            &error) ||
        error != undefinedNormalError ||
        !degenerateOutput.points.empty() ||
        !degenerateOutput.normals.empty() ||
        !degenerateOutput.faceVertexCounts.empty() ||
        !degenerateOutput.faceVertexIndices.empty())
    {
        return Fail("A collapsed edge without a limiting normal was not refused with empty output.");
    }

    // All controls coincide, so both first partials vanish everywhere.
    HdSilkNurbs::PatchInput pointPatch = lineDegeneratePatch;
    pointPatch.points.assign(4, {1.0, 2.0, 3.0});
    if (HdSilkNurbs::EvaluatePatch(pointPatch, &degenerateOutput, &error) ||
        error != undefinedNormalError ||
        !degenerateOutput.points.empty())
    {
        return Fail("A patch collapsed to a point was not refused.");
    }

    // The u = 0.5 control row collapses, so S_v vanishes along an interior
    // line where the two sides fold onto each other.
    HdSilkNurbs::PatchInput interiorCollapsePatch;
    interiorCollapsePatch.points = {
        {0.0, 0.0, 0.0}, {0.0, 1.0, 0.0},
        {1.0, 0.5, 0.0}, {1.0, 0.5, 0.0},
        {2.0, 0.0, 0.0}, {2.0, 1.0, 0.0}};
    interiorCollapsePatch.uVertexCount = 3;
    interiorCollapsePatch.vVertexCount = 2;
    interiorCollapsePatch.uOrder = 2;
    interiorCollapsePatch.vOrder = 2;
    interiorCollapsePatch.uKnots = {0.0, 0.0, 0.5, 1.0, 1.0};
    interiorCollapsePatch.vKnots = {0.0, 0.0, 1.0, 1.0};
    if (HdSilkNurbs::EvaluatePatch(
            interiorCollapsePatch,
            &degenerateOutput,
            &error) ||
        error != undefinedNormalError ||
        !degenerateOutput.points.empty())
    {
        return Fail("An interior collapsed line was not refused.");
    }
    if (!HdSilkNurbs::EvaluatePatch(poleDome, &degenerateOutput, &error) ||
        degenerateOutput.normals.size() != 81 ||
        !NearPoint(degenerateOutput.normals[0], {0.0, 0.0, 1.0}))
    {
        return Fail("Patch evaluation did not recover after an undefined-normal refusal.");
    }

    HdSilkNurbs::PatchInput higherUOrderPatch;
    higherUOrderPatch.points = {
        {0.0, 0.0, 0.0}, {0.2, 1.3, 0.5},
        {2.0, 0.1, 1.0}, {2.5, 1.7, 3.0},
        {4.1, -0.2, 0.5}, {4.5, 1.2, -1.0}};
    higherUOrderPatch.weights = {1.0, 2.0, 3.0, 4.0, 5.0, 6.0};
    higherUOrderPatch.uVertexCount = 3;
    higherUOrderPatch.vVertexCount = 2;
    higherUOrderPatch.uOrder = 3;
    higherUOrderPatch.vOrder = 2;
    higherUOrderPatch.uKnots = {0.0, 0.0, 0.0, 1.0, 1.0, 1.0};
    higherUOrderPatch.vKnots = {0.0, 0.0, 1.0, 1.0};
    HdSilkNurbs::PatchOutput higherUOrderPatchOutput;
    if (!HdSilkNurbs::EvaluatePatch(
            higherUOrderPatch,
            &higherUOrderPatchOutput,
            &error) ||
        higherUOrderPatchOutput.points.size() != 81 ||
        !NearPoint(
            higherUOrderPatchOutput.points[40],
            {799.0 / 280.0, 23.0 / 28.0, 55.0 / 56.0}) ||
        !HasConsistentPatchOrientation(
            higherUOrderPatchOutput,
            {286615.0, -248150.0, 622629.0}))
    {
        return Fail("Rational patch evaluation with U order greater than V order missed its analytic center.");
    }

    HdSilkNurbs::PatchInput higherVOrderPatch;
    higherVOrderPatch.points = {
        {0.0, 0.0, 0.0}, {1.0, 2.0, 1.0}, {3.0, 5.0, -2.0},
        {4.0, -1.0, 2.0}, {5.0, 3.0, 4.0}, {9.0, 6.0, 1.0}};
    higherVOrderPatch.weights = {1.0, 2.0, 3.0, 4.0, 5.0, 6.0};
    higherVOrderPatch.uVertexCount = 2;
    higherVOrderPatch.vVertexCount = 3;
    higherVOrderPatch.uOrder = 2;
    higherVOrderPatch.vOrder = 3;
    higherVOrderPatch.uKnots = {0.0, 0.0, 1.0, 1.0};
    higherVOrderPatch.vKnots = {0.0, 0.0, 0.0, 1.0, 1.0, 1.0};
    HdSilkNurbs::PatchOutput higherVOrderPatchOutput;
    if (!HdSilkNurbs::EvaluatePatch(
            higherVOrderPatch,
            &higherVOrderPatchOutput,
            &error) ||
        higherVOrderPatchOutput.points.size() != 81 ||
        !NearPoint(
            higherVOrderPatchOutput.points[40],
            {133.0 / 28.0, 85.0 / 28.0, 13.0 / 7.0}) ||
        !HasConsistentPatchOrientation(
            higherVOrderPatchOutput,
            {-36372.0, 42028.0, 50736.0}))
    {
        return Fail("Rational patch evaluation with U order less than V order missed its analytic center.");
    }

    HdSilkNurbs::PatchInput earlyPatchKnot;
    earlyPatchKnot.points = {
        {0.0, 0.0, 0.0}, {0.0, 1.0, 0.0},
        {2.0, 0.0, 0.0}, {2.0, 1.0, 0.0},
        {3.0, 0.0, 0.0}, {3.0, 1.0, 0.0}};
    earlyPatchKnot.weights = {1.0, 1.0, 1.0, 1.0, 1.0, 1.0};
    earlyPatchKnot.uVertexCount = 3;
    earlyPatchKnot.vVertexCount = 2;
    earlyPatchKnot.uOrder = 2;
    earlyPatchKnot.vOrder = 2;
    earlyPatchKnot.uKnots = {0.0, 0.0, 0.25, 1.0, 1.0};
    earlyPatchKnot.vKnots = {0.0, 0.0, 1.0, 1.0};
    earlyPatchKnot.uRange = {0.5, 1.0};
    earlyPatchKnot.hasURange = true;
    HdSilkNurbs::PatchInput latePatchKnot = earlyPatchKnot;
    latePatchKnot.uKnots = {0.0, 0.0, 0.75, 1.0, 1.0};
    HdSilkNurbs::PatchOutput earlyPatchKnotOutput;
    HdSilkNurbs::PatchOutput latePatchKnotOutput;
    if (!HdSilkNurbs::EvaluatePatch(
            earlyPatchKnot,
            &earlyPatchKnotOutput,
            &error) ||
        !HdSilkNurbs::EvaluatePatch(
            latePatchKnot,
            &latePatchKnotOutput,
            &error))
    {
        return Fail("Order-2 patch evaluation with authored internal knot spacing failed: " + error);
    }
    if (earlyPatchKnotOutput.points.size() != 81 ||
        latePatchKnotOutput.points.size() != 153 ||
        !NearPoint(earlyPatchKnotOutput.points.front(), {7.0 / 3.0, 0.0, 0.0}) ||
        !NearPoint(latePatchKnotOutput.points.front(), {4.0 / 3.0, 0.0, 0.0}))
    {
        return Fail("Authored U-knot spacing did not change patch samples at the same parameter.");
    }

    curve.knots[3] = -0.5;
    if (HdSilkNurbs::EvaluateCurves(curve, &curveOutput, &error))
    {
        return Fail("NURBS curve evaluation accepted a non-monotonic knot vector.");
    }

    patch.weights[0] = -1.0;
    if (HdSilkNurbs::EvaluatePatch(patch, &patchOutput, &error))
    {
        return Fail("NURBS patch evaluation accepted mixed-sign rational weights.");
    }

    HdSilkNurbs::CurvesInput rationalQuarterCircle = MakeSingleCurve(
        {{1.0, 0.0, 0.0}, {1.0, 1.0, 0.0}, {0.0, 1.0, 0.0}},
        {1.0, std::sqrt(0.5), 1.0},
        3,
        {0.0, 0.0, 0.0, 1.0, 1.0, 1.0});
    HdSilkNurbs::CurvesOutput rationalQuarterOutput;
    if (!HdSilkNurbs::EvaluateCurves(
            rationalQuarterCircle,
            &rationalQuarterOutput,
            &error))
    {
        return Fail("Analytic rational quarter-circle evaluation failed: " + error);
    }
    if (!HasFinitePointsAndMatchingRuns(rationalQuarterOutput) ||
        rationalQuarterOutput.vertexCounts != std::vector<int32_t>{9} ||
        rationalQuarterOutput.points.size() != 9 ||
        !NearPoint(rationalQuarterOutput.points.front(), {1.0, 0.0, 0.0}) ||
        !NearPoint(rationalQuarterOutput.points[4],
            {std::sqrt(0.5), std::sqrt(0.5), 0.0}) ||
        !NearPoint(rationalQuarterOutput.points.back(), {0.0, 1.0, 0.0}))
    {
        return Fail("Rational quarter-circle endpoints, midpoint, or run topology were incorrect.");
    }

    HdSilkNurbs::CurvesInput unweightedQuarterCircle = rationalQuarterCircle;
    unweightedQuarterCircle.weights = {1.0, 1.0, 1.0};
    HdSilkNurbs::CurvesOutput unweightedQuarterOutput;
    if (!HdSilkNurbs::EvaluateCurves(
            unweightedQuarterCircle,
            &unweightedQuarterOutput,
            &error))
    {
        return Fail("Unit-weight quarter-circle evaluation failed: " + error);
    }
    if (!HasFinitePointsAndMatchingRuns(unweightedQuarterOutput) ||
        unweightedQuarterOutput.vertexCounts != std::vector<int32_t>{9} ||
        !NearPoint(unweightedQuarterOutput.points[4], {0.75, 0.75, 0.0}) ||
        NearPoint(unweightedQuarterOutput.points[4], rationalQuarterOutput.points[4]))
    {
        return Fail("Authored rational weights did not distinguish the curve from unit weights.");
    }

    HdSilkNurbs::CurvesInput scaledQuarterCircle = rationalQuarterCircle;
    for (double& weight : scaledQuarterCircle.weights)
    {
        weight *= 7.0;
    }
    HdSilkNurbs::CurvesOutput scaledQuarterOutput;
    if (!HdSilkNurbs::EvaluateCurves(
            scaledQuarterCircle,
            &scaledQuarterOutput,
            &error))
    {
        return Fail("Common-scaled rational weight evaluation failed: " + error);
    }
    if (!SameCurveOutput(rationalQuarterOutput, scaledQuarterOutput))
    {
        return Fail("Common positive weight scaling changed rational curve points or topology.");
    }

    HdSilkNurbs::CurvesInput linearCurve = MakeSingleCurve(
        {{0.0, 0.0, 0.0}, {2.0, 0.0, 0.0}},
        {1.0, 1.0},
        2,
        {0.0, 0.0, 1.0, 1.0});
    HdSilkNurbs::CurvesOutput linearOutput;
    if (!HdSilkNurbs::EvaluateCurves(linearCurve, &linearOutput, &error))
    {
        return Fail("Order-2 analytic line evaluation failed: " + error);
    }
    if (!HasFinitePointsAndMatchingRuns(linearOutput) ||
        linearOutput.vertexCounts != std::vector<int32_t>{9} ||
        !NearPoint(linearOutput.points.front(), {0.0, 0.0, 0.0}) ||
        !NearPoint(linearOutput.points[4], {1.0, 0.0, 0.0}) ||
        !NearPoint(linearOutput.points.back(), {2.0, 0.0, 0.0}))
    {
        return Fail("Authored order-2 line did not produce its analytic endpoints and midpoint.");
    }

    HdSilkNurbs::CurvesInput quadraticCurve = MakeSingleCurve(
        {{0.0, 0.0, 0.0}, {1.0, 2.0, 0.0}, {2.0, 0.0, 0.0}},
        {1.0, 1.0, 1.0},
        3,
        {0.0, 0.0, 0.0, 1.0, 1.0, 1.0});
    HdSilkNurbs::CurvesOutput quadraticOutput;
    if (!HdSilkNurbs::EvaluateCurves(quadraticCurve, &quadraticOutput, &error))
    {
        return Fail("Order-3 analytic quadratic evaluation failed: " + error);
    }
    if (!HasFinitePointsAndMatchingRuns(quadraticOutput) ||
        quadraticOutput.vertexCounts != std::vector<int32_t>{9} ||
        !NearPoint(quadraticOutput.points[4], {1.0, 1.0, 0.0}))
    {
        return Fail("Authored order-3 curve did not produce its analytic quadratic midpoint.");
    }

    const std::vector<HdSilkNurbs::Point> knotControls = {
        {0.0, 0.0, 0.0}, {2.0, 0.0, 0.0}, {2.0, 3.0, 0.0}};
    HdSilkNurbs::CurvesInput earlyKnotCurve = MakeSingleCurve(
        knotControls,
        {1.0, 1.0, 1.0},
        2,
        {0.0, 0.0, 0.25, 1.0, 1.0},
        true,
        {0.5, 1.0});
    HdSilkNurbs::CurvesInput lateKnotCurve = MakeSingleCurve(
        knotControls,
        {1.0, 1.0, 1.0},
        2,
        {0.0, 0.0, 0.75, 1.0, 1.0},
        true,
        {0.5, 1.0});
    HdSilkNurbs::CurvesOutput earlyKnotOutput;
    HdSilkNurbs::CurvesOutput lateKnotOutput;
    if (!HdSilkNurbs::EvaluateCurves(earlyKnotCurve, &earlyKnotOutput, &error))
    {
        return Fail("Order-2 curve with an early authored knot failed: " + error);
    }
    if (!HdSilkNurbs::EvaluateCurves(lateKnotCurve, &lateKnotOutput, &error))
    {
        return Fail("Order-2 curve with a late authored knot failed: " + error);
    }
    if (!HasFinitePointsAndMatchingRuns(earlyKnotOutput) ||
        !HasFinitePointsAndMatchingRuns(lateKnotOutput) ||
        earlyKnotOutput.vertexCounts != std::vector<int32_t>{9} ||
        lateKnotOutput.vertexCounts != std::vector<int32_t>{17} ||
        !NearPoint(earlyKnotOutput.points.front(), {2.0, 1.0, 0.0}) ||
        !NearPoint(lateKnotOutput.points.front(), {4.0 / 3.0, 0.0, 0.0}) ||
        NearPoint(earlyKnotOutput.points.front(), lateKnotOutput.points.front()))
    {
        return Fail("Authored internal knot spacing did not change the analytic value at parameter 0.5.");
    }

    HdSilkNurbs::CurvesInput fullRangeCurve = linearCurve;
    fullRangeCurve.ranges = {{0.0, 1.0}};
    HdSilkNurbs::CurvesOutput fullRangeOutput;
    if (!HdSilkNurbs::EvaluateCurves(fullRangeCurve, &fullRangeOutput, &error))
    {
        return Fail("Explicit full-domain curve range evaluation failed: " + error);
    }
    if (!HasFinitePointsAndMatchingRuns(fullRangeOutput) ||
        !SameCurveOutput(linearOutput, fullRangeOutput))
    {
        return Fail("Default and explicit full-domain ranges produced different curve output.");
    }

    HdSilkNurbs::CurvesInput middleRangeCurve = linearCurve;
    middleRangeCurve.ranges = {{0.25, 0.75}};
    HdSilkNurbs::CurvesOutput middleRangeOutput;
    if (!HdSilkNurbs::EvaluateCurves(middleRangeCurve, &middleRangeOutput, &error))
    {
        return Fail("Authored subrange curve evaluation failed: " + error);
    }
    if (!HasFinitePointsAndMatchingRuns(middleRangeOutput) ||
        middleRangeOutput.vertexCounts != std::vector<int32_t>{9} ||
        !NearPoint(middleRangeOutput.points.front(), {0.5, 0.0, 0.0}) ||
        !NearPoint(middleRangeOutput.points.back(), {1.5, 0.0, 0.0}))
    {
        return Fail("Authored subrange endpoints were incorrect or leaked full-domain endpoints.");
    }
    for (std::size_t i = 1; i < middleRangeOutput.points.size(); ++i)
    {
        if (!(middleRangeOutput.points[i - 1].x < middleRangeOutput.points[i].x))
        {
            return Fail("Authored subrange samples were not emitted in increasing parameter order.");
        }
    }

    HdSilkNurbs::CurvesInput lowerTouchingRangeCurve = linearCurve;
    lowerTouchingRangeCurve.ranges = {{0.0, 0.5}};
    HdSilkNurbs::CurvesOutput lowerTouchingRangeOutput;
    if (!HdSilkNurbs::EvaluateCurves(
            lowerTouchingRangeCurve,
            &lowerTouchingRangeOutput,
            &error))
    {
        return Fail("Lower-endpoint-touching curve range evaluation failed: " + error);
    }
    HdSilkNurbs::CurvesInput upperTouchingRangeCurve = linearCurve;
    upperTouchingRangeCurve.ranges = {{0.5, 1.0}};
    HdSilkNurbs::CurvesOutput upperTouchingRangeOutput;
    if (!HdSilkNurbs::EvaluateCurves(
            upperTouchingRangeCurve,
            &upperTouchingRangeOutput,
            &error))
    {
        return Fail("Upper-endpoint-touching curve range evaluation failed: " + error);
    }
    if (!HasFinitePointsAndMatchingRuns(lowerTouchingRangeOutput) ||
        !HasFinitePointsAndMatchingRuns(upperTouchingRangeOutput) ||
        !NearPoint(lowerTouchingRangeOutput.points.front(), {0.0, 0.0, 0.0}) ||
        !NearPoint(lowerTouchingRangeOutput.points.back(), {1.0, 0.0, 0.0}) ||
        !NearPoint(upperTouchingRangeOutput.points.front(), {1.0, 0.0, 0.0}) ||
        !NearPoint(upperTouchingRangeOutput.points.back(), {2.0, 0.0, 0.0}))
    {
        return Fail("Curve ranges touching domain endpoints omitted or misplaced those endpoints.");
    }

    HdSilkNurbs::CurvesInput nonUnitDomainCurve = MakeSingleCurve(
        {{0.0, 0.0, 0.0}, {2.0, 0.0, 0.0}},
        {1.0, 1.0},
        2,
        {2.0, 2.0, 6.0, 6.0});
    HdSilkNurbs::CurvesOutput nonUnitDomainOutput;
    if (!HdSilkNurbs::EvaluateCurves(
            nonUnitDomainCurve,
            &nonUnitDomainOutput,
            &error))
    {
        return Fail("Non-unit curve knot domain evaluation failed: " + error);
    }
    nonUnitDomainCurve.ranges = {{3.0, 5.0}};
    HdSilkNurbs::CurvesOutput nonUnitSubrangeOutput;
    if (!HdSilkNurbs::EvaluateCurves(
            nonUnitDomainCurve,
            &nonUnitSubrangeOutput,
            &error))
    {
        return Fail("Non-unit curve subrange evaluation failed: " + error);
    }
    if (!HasFinitePointsAndMatchingRuns(nonUnitDomainOutput) ||
        !HasFinitePointsAndMatchingRuns(nonUnitSubrangeOutput) ||
        !NearPoint(nonUnitDomainOutput.points.front(), {0.0, 0.0, 0.0}) ||
        !NearPoint(nonUnitDomainOutput.points.back(), {2.0, 0.0, 0.0}) ||
        !NearPoint(nonUnitSubrangeOutput.points.front(), {0.5, 0.0, 0.0}) ||
        !NearPoint(nonUnitSubrangeOutput.points.back(), {1.5, 0.0, 0.0}))
    {
        return Fail("Non-unit knot domain or its authored [3, 5] subrange was evaluated incorrectly.");
    }

    HdSilkNurbs::CurvesInput multipleCurves;
    multipleCurves.points = {
        {0.0, 0.0, 0.0}, {2.0, 0.0, 0.0},
        {10.0, 3.0, 0.0}, {10.0, 5.0, 0.0}};
    multipleCurves.weights = {1.0, 1.0, 1.0, 1.0};
    multipleCurves.vertexCounts = {2, 2};
    multipleCurves.orders = {2, 2};
    multipleCurves.knots = {
        0.0, 0.0, 1.0, 1.0,
        0.0, 0.0, 1.0, 1.0};
    HdSilkNurbs::CurvesOutput multipleCurveOutput;
    if (!HdSilkNurbs::EvaluateCurves(multipleCurves, &multipleCurveOutput, &error))
    {
        return Fail("Two-curve analytic input evaluation failed: " + error);
    }
    if (!HasFinitePointsAndMatchingRuns(multipleCurveOutput) ||
        multipleCurveOutput.vertexCounts != std::vector<int32_t>{9, 9} ||
        multipleCurveOutput.points.size() != 18 ||
        !NearPoint(multipleCurveOutput.points[8], {2.0, 0.0, 0.0}) ||
        !NearPoint(multipleCurveOutput.points[9], {10.0, 3.0, 0.0}) ||
        !NearPoint(multipleCurveOutput.points[17], {10.0, 5.0, 0.0}))
    {
        return Fail("Multiple curves were not emitted as separate ordered output runs.");
    }

    HdSilkNurbs::CurvesInput continuousMultiSpan = MakeSingleCurve(
        knotControls,
        {1.0, 1.0, 1.0},
        2,
        {0.0, 0.0, 0.25, 1.0, 1.0});
    HdSilkNurbs::CurvesOutput continuousMultiSpanOutput;
    if (!HdSilkNurbs::EvaluateCurves(
            continuousMultiSpan,
            &continuousMultiSpanOutput,
            &error))
    {
        return Fail("Continuous multi-span line evaluation failed: " + error);
    }
    if (!HasFinitePointsAndMatchingRuns(continuousMultiSpanOutput) ||
        continuousMultiSpanOutput.vertexCounts != std::vector<int32_t>{17} ||
        continuousMultiSpanOutput.points.size() != 17 ||
        !NearPoint(continuousMultiSpanOutput.points.front(), {0.0, 0.0, 0.0}) ||
        !NearPoint(continuousMultiSpanOutput.points[7], {1.75, 0.0, 0.0}) ||
        !NearPoint(continuousMultiSpanOutput.points[8], {2.0, 0.0, 0.0}) ||
        !NearPoint(continuousMultiSpanOutput.points[9], {2.0, 0.375, 0.0}) ||
        !NearPoint(continuousMultiSpanOutput.points.back(), {2.0, 3.0, 0.0}))
    {
        return Fail("Continuous multi-span traversal omitted or duplicated its shared knot sample.");
    }
    HdSilkNurbs::CurvesInput continuousCrossingRange = continuousMultiSpan;
    continuousCrossingRange.ranges = {{0.125, 0.5}};
    HdSilkNurbs::CurvesOutput continuousCrossingRangeOutput;
    if (!HdSilkNurbs::EvaluateCurves(
            continuousCrossingRange,
            &continuousCrossingRangeOutput,
            &error))
    {
        return Fail("Continuous multi-span subrange evaluation failed: " + error);
    }
    if (!HasFinitePointsAndMatchingRuns(continuousCrossingRangeOutput) ||
        continuousCrossingRangeOutput.vertexCounts != std::vector<int32_t>{17} ||
        !NearPoint(continuousCrossingRangeOutput.points.front(), {1.0, 0.0, 0.0}) ||
        !NearPoint(continuousCrossingRangeOutput.points[8], {2.0, 0.0, 0.0}) ||
        !NearPoint(continuousCrossingRangeOutput.points[9], {2.0, 0.125, 0.0}) ||
        !NearPoint(continuousCrossingRangeOutput.points.back(), {2.0, 1.0, 0.0}))
    {
        return Fail("A continuous range crossing an interior knot lost its shared boundary or endpoints.");
    }

    HdSilkNurbs::CurvesInput belowOrderMultiplicity = MakeSingleCurve(
        {{0.0, 0.0, 0.0}, {1.0, 2.0, 0.0}, {2.0, 0.0, 0.0},
            {3.0, -2.0, 0.0}, {4.0, 0.0, 0.0}},
        {1.0, 1.0, 1.0, 1.0, 1.0},
        3,
        {0.0, 0.0, 0.0, 0.5, 0.5, 1.0, 1.0, 1.0});
    HdSilkNurbs::CurvesOutput belowOrderMultiplicityOutput;
    if (!HdSilkNurbs::EvaluateCurves(
            belowOrderMultiplicity,
            &belowOrderMultiplicityOutput,
            &error))
    {
        return Fail("Quadratic curve with order-minus-one knot multiplicity failed: " + error);
    }
    if (!HasFinitePointsAndMatchingRuns(belowOrderMultiplicityOutput) ||
        belowOrderMultiplicityOutput.vertexCounts != std::vector<int32_t>{17} ||
        belowOrderMultiplicityOutput.points.size() != 17 ||
        !NearPoint(belowOrderMultiplicityOutput.points[8], {2.0, 0.0, 0.0}) ||
        !NearPoint(belowOrderMultiplicityOutput.points.front(), {0.0, 0.0, 0.0}) ||
        !NearPoint(belowOrderMultiplicityOutput.points.back(), {4.0, 0.0, 0.0}))
    {
        return Fail("An order-minus-one interior knot did not retain positional continuity in one run.");
    }

    HdSilkNurbs::CurvesInput fullOrderMultiplicity = MakeSingleCurve(
        {{0.0, 0.0, 0.0}, {0.5, 0.0, 0.0}, {1.0, 0.0, 0.0},
            {3.0, 0.0, 0.0}, {3.5, 0.0, 0.0}, {4.0, 0.0, 0.0}},
        {1.0, 1.0, 1.0, 1.0, 1.0, 1.0},
        3,
        {0.0, 0.0, 0.0, 0.5, 0.5, 0.5, 1.0, 1.0, 1.0});
    HdSilkNurbs::CurvesOutput fullOrderMultiplicityOutput;
    if (!HdSilkNurbs::EvaluateCurves(
            fullOrderMultiplicity,
            &fullOrderMultiplicityOutput,
            &error))
    {
        return Fail("Quadratic curve with order-multiplicity discontinuity failed: " + error);
    }
    if (!HasFinitePointsAndMatchingRuns(fullOrderMultiplicityOutput) ||
        fullOrderMultiplicityOutput.vertexCounts != std::vector<int32_t>{9, 9} ||
        fullOrderMultiplicityOutput.points.size() != 18 ||
        !NearPoint(fullOrderMultiplicityOutput.points.front(), {0.0, 0.0, 0.0}) ||
        !NearPoint(fullOrderMultiplicityOutput.points[8], {1.0, 0.0, 0.0}) ||
        !NearPoint(fullOrderMultiplicityOutput.points[9], {3.0, 0.0, 0.0}) ||
        !NearPoint(fullOrderMultiplicityOutput.points.back(), {4.0, 0.0, 0.0}))
    {
        return Fail("Order-multiplicity knot did not preserve distinct one-sided samples in separate runs.");
    }

    HdSilkNurbs::PatchInput discontinuousPatch;
    const std::vector<double> discontinuousCoordinates = {0.0, 0.5, 1.0, 3.0, 3.5, 4.0};
    for (double u : discontinuousCoordinates)
    {
        for (double v : discontinuousCoordinates)
        {
            discontinuousPatch.points.push_back({u, v, 0.0});
        }
    }
    discontinuousPatch.uVertexCount = 6;
    discontinuousPatch.vVertexCount = 6;
    discontinuousPatch.uOrder = 3;
    discontinuousPatch.vOrder = 3;
    discontinuousPatch.uKnots = {
        0.0, 0.0, 0.0, 0.5, 0.5, 0.5, 1.0, 1.0, 1.0};
    discontinuousPatch.vKnots = discontinuousPatch.uKnots;
    HdSilkNurbs::PatchOutput discontinuousPatchOutput;
    if (!HdSilkNurbs::EvaluatePatch(
            discontinuousPatch,
            &discontinuousPatchOutput,
            &error))
    {
        return Fail("Patch evaluation with full-order discontinuities failed: " + error);
    }
    if (discontinuousPatchOutput.points.size() != 324 ||
        discontinuousPatchOutput.faceVertexCounts != std::vector<int32_t>(256, 4) ||
        discontinuousPatchOutput.faceVertexIndices.size() != 1024 ||
        !Near(discontinuousPatchOutput.points[8].x, 1.0) ||
        !Near(discontinuousPatchOutput.points[9].x, 3.0) ||
        !Near(discontinuousPatchOutput.points[8 * 18].y, 1.0) ||
        !Near(discontinuousPatchOutput.points[9 * 18].y, 3.0))
    {
        return Fail("Patch discontinuity samples or connected edge count were not preserved.");
    }
    for (std::size_t face = 0;
         face < discontinuousPatchOutput.faceVertexIndices.size();
         face += 4)
    {
        const std::size_t lowerLeft = static_cast<std::size_t>(
            discontinuousPatchOutput.faceVertexIndices[face]);
        const std::size_t lowerRight = static_cast<std::size_t>(
            discontinuousPatchOutput.faceVertexIndices[face + 1]);
        const std::size_t upperRight = static_cast<std::size_t>(
            discontinuousPatchOutput.faceVertexIndices[face + 2]);
        const std::size_t upperLeft = static_cast<std::size_t>(
            discontinuousPatchOutput.faceVertexIndices[face + 3]);
        const std::size_t u = lowerLeft % 18;
        const std::size_t v = lowerLeft / 18;
        if (u == 8 || v == 8 ||
            lowerRight != lowerLeft + 1 ||
            upperRight != lowerLeft + 19 ||
            upperLeft != lowerLeft + 18)
        {
            return Fail("Patch faces crossed a full-order discontinuity or omitted a connected edge.");
        }
    }

    HdSilkNurbs::CurvesInput discontinuityCrossingRange = fullOrderMultiplicity;
    discontinuityCrossingRange.ranges = {{0.25, 0.75}};
    HdSilkNurbs::CurvesOutput discontinuityCrossingRangeOutput;
    if (!HdSilkNurbs::EvaluateCurves(
            discontinuityCrossingRange,
            &discontinuityCrossingRangeOutput,
            &error))
    {
        return Fail("Curve range crossing an order-multiplicity knot failed: " + error);
    }
    if (!HasFinitePointsAndMatchingRuns(discontinuityCrossingRangeOutput) ||
        discontinuityCrossingRangeOutput.vertexCounts != std::vector<int32_t>{9, 9} ||
        discontinuityCrossingRangeOutput.points.size() != 18 ||
        !NearPoint(discontinuityCrossingRangeOutput.points.front(), {0.5, 0.0, 0.0}) ||
        !NearPoint(discontinuityCrossingRangeOutput.points[8], {1.0, 0.0, 0.0}) ||
        !NearPoint(discontinuityCrossingRangeOutput.points[9], {3.0, 0.0, 0.0}) ||
        !NearPoint(discontinuityCrossingRangeOutput.points.back(), {3.5, 0.0, 0.0}))
    {
        return Fail("A range crossing a discontinuity lost span endpoints or connected separate runs.");
    }

    HdSilkNurbs::CurvesInput discontinuityLowerRange = fullOrderMultiplicity;
    discontinuityLowerRange.ranges = {{0.0, 0.5}};
    HdSilkNurbs::CurvesOutput discontinuityLowerRangeOutput;
    if (!HdSilkNurbs::EvaluateCurves(
            discontinuityLowerRange,
            &discontinuityLowerRangeOutput,
            &error))
    {
        return Fail("Range ending at an order-multiplicity knot failed: " + error);
    }
    HdSilkNurbs::CurvesInput discontinuityUpperRange = fullOrderMultiplicity;
    discontinuityUpperRange.ranges = {{0.5, 1.0}};
    HdSilkNurbs::CurvesOutput discontinuityUpperRangeOutput;
    if (!HdSilkNurbs::EvaluateCurves(
            discontinuityUpperRange,
            &discontinuityUpperRangeOutput,
            &error))
    {
        return Fail("Range starting at an order-multiplicity knot failed: " + error);
    }
    if (!HasFinitePointsAndMatchingRuns(discontinuityLowerRangeOutput) ||
        !HasFinitePointsAndMatchingRuns(discontinuityUpperRangeOutput) ||
        discontinuityLowerRangeOutput.vertexCounts != std::vector<int32_t>{9} ||
        discontinuityUpperRangeOutput.vertexCounts != std::vector<int32_t>{9} ||
        !NearPoint(discontinuityLowerRangeOutput.points.back(), {1.0, 0.0, 0.0}) ||
        !NearPoint(discontinuityUpperRangeOutput.points.front(), {3.0, 0.0, 0.0}))
    {
        return Fail("Ranges touching a discontinuity did not select the corresponding one-sided endpoint.");
    }

    HdSilkNurbs::PatchInput rationalPatch;
    rationalPatch.points = {
        {0.0, 0.0, 0.0}, {0.0, 1.0, 0.0},
        {1.0, 0.0, 0.0}, {1.0, 1.0, 0.0}};
    rationalPatch.weights = {1.0, 1.0, 1.0, 2.0};
    rationalPatch.uVertexCount = 2;
    rationalPatch.vVertexCount = 2;
    rationalPatch.uOrder = 2;
    rationalPatch.vOrder = 2;
    rationalPatch.uKnots = {0.0, 0.0, 1.0, 1.0};
    rationalPatch.vKnots = {0.0, 0.0, 1.0, 1.0};
    HdSilkNurbs::PatchOutput rationalPatchOutput;
    if (!HdSilkNurbs::EvaluatePatch(rationalPatch, &rationalPatchOutput, &error))
    {
        return Fail("Asymmetrically weighted bilinear patch evaluation failed: " + error);
    }
    const std::size_t rationalAsymmetricIndex = 5 * 9 + 2;
    if (rationalPatchOutput.points.size() != 81 ||
        rationalPatchOutput.normals.size() != 81 ||
        rationalPatchOutput.faceVertexIndices.size() != 256 ||
        rationalPatchOutput.faceVertexIndices[0] != 0 ||
        rationalPatchOutput.faceVertexIndices[1] != 1 ||
        rationalPatchOutput.faceVertexIndices[2] != 10 ||
        rationalPatchOutput.faceVertexIndices[3] != 9 ||
        !NearPoint(rationalPatchOutput.points[0], {0.0, 0.0, 0.0}) ||
        !NearPoint(rationalPatchOutput.points[8], {1.0, 0.0, 0.0}) ||
        !NearPoint(rationalPatchOutput.points[72], {0.0, 1.0, 0.0}) ||
        !NearPoint(rationalPatchOutput.points[80], {1.0, 1.0, 0.0}) ||
        !NearPoint(rationalPatchOutput.points[40], {0.6, 0.6, 0.0}) ||
        !NearPoint(rationalPatchOutput.points[rationalAsymmetricIndex],
            {13.0 / 37.0, 25.0 / 37.0, 0.0}))
    {
        return Fail("Rational patch corner, center, or asymmetric interior values were incorrect.");
    }
    for (const HdSilkNurbs::Point& normal : rationalPatchOutput.normals)
    {
        if (!IsFinite(normal) ||
            !Near(std::sqrt(normal.x * normal.x + normal.y * normal.y +
                normal.z * normal.z), 1.0) ||
            !Near(normal.z, 1.0))
        {
            return Fail("Weighted planar patch normals were not finite, unit length, and positively oriented.");
        }
    }

    HdSilkNurbs::PatchInput unitWeightPatch = rationalPatch;
    unitWeightPatch.weights.clear();
    HdSilkNurbs::PatchOutput unitWeightPatchOutput;
    if (!HdSilkNurbs::EvaluatePatch(unitWeightPatch, &unitWeightPatchOutput, &error))
    {
        return Fail("Unit-weight bilinear patch evaluation failed: " + error);
    }
    if (!NearPoint(unitWeightPatchOutput.points[40], {0.5, 0.5, 0.0}) ||
        NearPoint(unitWeightPatchOutput.points[40], rationalPatchOutput.points[40]))
    {
        return Fail("Authored patch weights did not distinguish the rational surface from the unit-weight surface.");
    }

    HdSilkNurbs::PatchInput tiltedRationalPatch = rationalPatch;
    for (HdSilkNurbs::Point& point : tiltedRationalPatch.points)
    {
        point.z = point.x + 2.0 * point.y;
    }
    HdSilkNurbs::PatchOutput tiltedPatchOutput;
    if (!HdSilkNurbs::EvaluatePatch(
            tiltedRationalPatch,
            &tiltedPatchOutput,
            &error))
    {
        return Fail("Tilted rational patch evaluation failed: " + error);
    }
    const HdSilkNurbs::Point expectedTiltedNormal = {
        -1.0 / std::sqrt(6.0), -2.0 / std::sqrt(6.0), 1.0 / std::sqrt(6.0)};
    if (!NearPoint(tiltedPatchOutput.points[40], {0.6, 0.6, 1.8}))
    {
        return Fail("Tilted rational patch center did not preserve its analytic plane.");
    }
    for (std::size_t index = 0; index < tiltedPatchOutput.points.size(); ++index)
    {
        const HdSilkNurbs::Point& point = tiltedPatchOutput.points[index];
        const HdSilkNurbs::Point& normal = tiltedPatchOutput.normals[index];
        if (!IsFinite(point) || !IsFinite(normal) ||
            !Near(point.z, point.x + 2.0 * point.y) ||
            !NearPoint(normal, expectedTiltedNormal) ||
            !Near(std::sqrt(normal.x * normal.x + normal.y * normal.y +
                normal.z * normal.z), 1.0))
        {
            return Fail("Tilted rational patch samples or boundary/interior normals violated the analytic plane.");
        }
    }

    HdSilkNurbs::PatchInput leftHandedPatch = tiltedRationalPatch;
    leftHandedPatch.leftHanded = true;
    HdSilkNurbs::PatchOutput leftHandedPatchOutput;
    if (!HdSilkNurbs::EvaluatePatch(
            leftHandedPatch,
            &leftHandedPatchOutput,
            &error))
    {
        return Fail("Left-handed patch evaluation failed: " + error);
    }
    if (leftHandedPatchOutput.points.size() != tiltedPatchOutput.points.size() ||
        leftHandedPatchOutput.normals.size() != tiltedPatchOutput.normals.size() ||
        leftHandedPatchOutput.faceVertexCounts != tiltedPatchOutput.faceVertexCounts ||
        leftHandedPatchOutput.faceVertexIndices != tiltedPatchOutput.faceVertexIndices)
    {
        return Fail("Changing patch handedness altered its positions or tessellation topology.");
    }
    for (std::size_t index = 0; index < tiltedPatchOutput.points.size(); ++index)
    {
        const HdSilkNurbs::Point& rightNormal = tiltedPatchOutput.normals[index];
        const HdSilkNurbs::Point& leftNormal = leftHandedPatchOutput.normals[index];
        if (!NearPoint(leftHandedPatchOutput.points[index],
                tiltedPatchOutput.points[index]) ||
            !NearPoint(leftNormal,
                {-rightNormal.x, -rightNormal.y, -rightNormal.z}))
        {
            return Fail("Left-handed mode did not preserve points and reverse each normal.");
        }
    }
    for (std::size_t index = 0;
        index < tiltedPatchOutput.faceVertexIndices.size();
        ++index)
    {
        if (tiltedPatchOutput.faceVertexIndices[index] < 0 ||
            static_cast<std::size_t>(tiltedPatchOutput.faceVertexIndices[index]) >=
                tiltedPatchOutput.points.size())
        {
            return Fail("Patch tessellation emitted an out-of-range face vertex index.");
        }
    }

    HdSilkNurbs::PatchInput planarRangePatch = rationalPatch;
    planarRangePatch.weights.clear();
    planarRangePatch.points = {
        {0.0, 0.0, 0.0}, {0.0, 1.0, 2.0},
        {1.0, 0.0, 1.0}, {1.0, 1.0, 3.0}};
    HdSilkNurbs::PatchOutput defaultPatchRangeOutput;
    if (!HdSilkNurbs::EvaluatePatch(
            planarRangePatch,
            &defaultPatchRangeOutput,
            &error))
    {
        return Fail("Default patch range evaluation failed: " + error);
    }
    HdSilkNurbs::PatchInput explicitPatchRange = planarRangePatch;
    explicitPatchRange.uRange = {0.0, 1.0};
    explicitPatchRange.vRange = {0.0, 1.0};
    explicitPatchRange.hasURange = true;
    explicitPatchRange.hasVRange = true;
    HdSilkNurbs::PatchOutput explicitPatchRangeOutput;
    if (!HdSilkNurbs::EvaluatePatch(
            explicitPatchRange,
            &explicitPatchRangeOutput,
            &error))
    {
        return Fail("Explicit full-domain patch range evaluation failed: " + error);
    }
    if (defaultPatchRangeOutput.points.size() != explicitPatchRangeOutput.points.size() ||
        defaultPatchRangeOutput.normals.size() != explicitPatchRangeOutput.normals.size() ||
        defaultPatchRangeOutput.faceVertexCounts != explicitPatchRangeOutput.faceVertexCounts ||
        defaultPatchRangeOutput.faceVertexIndices != explicitPatchRangeOutput.faceVertexIndices)
    {
        return Fail("Default and explicit full patch ranges produced different topology.");
    }
    for (std::size_t index = 0; index < defaultPatchRangeOutput.points.size(); ++index)
    {
        if (!NearPoint(defaultPatchRangeOutput.points[index],
                explicitPatchRangeOutput.points[index]) ||
            !NearPoint(defaultPatchRangeOutput.normals[index],
                explicitPatchRangeOutput.normals[index]))
        {
            return Fail("Default and explicit full patch ranges produced different surface values.");
        }
    }

    HdSilkNurbs::PatchInput restrictedPatch = planarRangePatch;
    restrictedPatch.uRange = {0.25, 0.75};
    restrictedPatch.vRange = {0.2, 0.8};
    restrictedPatch.hasURange = true;
    restrictedPatch.hasVRange = true;
    HdSilkNurbs::PatchOutput restrictedPatchOutput;
    if (!HdSilkNurbs::EvaluatePatch(
            restrictedPatch,
            &restrictedPatchOutput,
            &error))
    {
        return Fail("Restricted patch range evaluation failed: " + error);
    }
    if (!NearPoint(restrictedPatchOutput.points.front(), {0.25, 0.2, 0.65}) ||
        !NearPoint(restrictedPatchOutput.points[8], {0.75, 0.2, 1.15}) ||
        !NearPoint(restrictedPatchOutput.points[72], {0.25, 0.8, 1.85}) ||
        !NearPoint(restrictedPatchOutput.points.back(), {0.75, 0.8, 2.35}))
    {
        return Fail("Patch U/V subrange corners were ignored, swapped, or mapped outside their ranges.");
    }
    for (int32_t index : restrictedPatchOutput.faceVertexIndices)
    {
        if (index < 0 ||
            static_cast<std::size_t>(index) >= restrictedPatchOutput.points.size())
        {
            return Fail("Restricted patch tessellation emitted an invalid face index.");
        }
    }

    HdSilkNurbs::PatchInput uOnlyRangePatch = planarRangePatch;
    uOnlyRangePatch.uRange = {0.25, 0.75};
    uOnlyRangePatch.hasURange = true;
    HdSilkNurbs::PatchOutput uOnlyRangeOutput;
    if (!HdSilkNurbs::EvaluatePatch(uOnlyRangePatch, &uOnlyRangeOutput, &error))
    {
        return Fail("U-only patch subrange evaluation failed: " + error);
    }
    HdSilkNurbs::PatchInput vOnlyRangePatch = planarRangePatch;
    vOnlyRangePatch.vRange = {0.2, 0.8};
    vOnlyRangePatch.hasVRange = true;
    HdSilkNurbs::PatchOutput vOnlyRangeOutput;
    if (!HdSilkNurbs::EvaluatePatch(vOnlyRangePatch, &vOnlyRangeOutput, &error))
    {
        return Fail("V-only patch subrange evaluation failed: " + error);
    }
    if (!NearPoint(uOnlyRangeOutput.points.front(), {0.25, 0.0, 0.25}) ||
        !NearPoint(uOnlyRangeOutput.points[8], {0.75, 0.0, 0.75}) ||
        !NearPoint(vOnlyRangeOutput.points.front(), {0.0, 0.2, 0.4}) ||
        !NearPoint(vOnlyRangeOutput.points[72], {0.0, 0.8, 1.6}))
    {
        return Fail("A patch range specified on only one axis was ignored or applied to the other axis.");
    }
    HdSilkNurbs::PatchInput boundaryTouchingPatchRange = planarRangePatch;
    boundaryTouchingPatchRange.uRange = {0.0, 0.5};
    boundaryTouchingPatchRange.vRange = {0.5, 1.0};
    boundaryTouchingPatchRange.hasURange = true;
    boundaryTouchingPatchRange.hasVRange = true;
    HdSilkNurbs::PatchOutput boundaryTouchingPatchRangeOutput;
    if (!HdSilkNurbs::EvaluatePatch(
            boundaryTouchingPatchRange,
            &boundaryTouchingPatchRangeOutput,
            &error))
    {
        return Fail("Patch ranges touching knot-domain endpoints failed: " + error);
    }
    if (!NearPoint(boundaryTouchingPatchRangeOutput.points.front(), {0.0, 0.5, 1.0}) ||
        !NearPoint(boundaryTouchingPatchRangeOutput.points[8], {0.5, 0.5, 1.5}) ||
        !NearPoint(boundaryTouchingPatchRangeOutput.points[72], {0.0, 1.0, 2.0}) ||
        !NearPoint(boundaryTouchingPatchRangeOutput.points.back(), {0.5, 1.0, 2.5}))
    {
        return Fail("Patch ranges touching domain endpoints omitted or misplaced those endpoints.");
    }

    HdSilkNurbs::PatchInput nonUnitPatchDomain = planarRangePatch;
    nonUnitPatchDomain.uKnots = {2.0, 2.0, 6.0, 6.0};
    nonUnitPatchDomain.vKnots = {2.0, 2.0, 6.0, 6.0};
    nonUnitPatchDomain.uRange = {3.0, 5.0};
    nonUnitPatchDomain.vRange = {3.0, 5.0};
    nonUnitPatchDomain.hasURange = true;
    nonUnitPatchDomain.hasVRange = true;
    HdSilkNurbs::PatchOutput nonUnitPatchDomainOutput;
    if (!HdSilkNurbs::EvaluatePatch(
            nonUnitPatchDomain,
            &nonUnitPatchDomainOutput,
            &error))
    {
        return Fail("Non-unit patch knot domain/subrange evaluation failed: " + error);
    }
    if (!NearPoint(nonUnitPatchDomainOutput.points.front(), {0.25, 0.25, 0.75}) ||
        !NearPoint(nonUnitPatchDomainOutput.points.back(), {0.75, 0.75, 2.25}))
    {
        return Fail("Patch subranges on a non-unit knot domain were not mapped correctly.");
    }

    HdSilkNurbs::PatchInput degeneratePatch = planarRangePatch;
    degeneratePatch.points = {
        {0.0, 0.0, 0.0}, {0.0, 0.0, 0.0},
        {1.0, 0.0, 0.0}, {1.0, 0.0, 0.0}};
    HdSilkNurbs::PatchOutput degeneratePatchOutput;
    if (HdSilkNurbs::EvaluatePatch(
            degeneratePatch,
            &degeneratePatchOutput,
            &error) ||
        error.find("NURBS patch normal is undefined") == std::string::npos ||
        !degeneratePatchOutput.points.empty() || !degeneratePatchOutput.normals.empty() ||
        !degeneratePatchOutput.faceVertexIndices.empty() ||
        !degeneratePatchOutput.faceVertexCounts.empty())
    {
        return Fail("A V-collapsed patch must refuse its undefined normal without partial output.");
    }
    if (!HdSilkNurbs::EvaluatePatch(planarRangePatch, &degeneratePatchOutput, &error) ||
        degeneratePatchOutput.points.size() != 81 ||
        degeneratePatchOutput.normals.size() != 81)
    {
        return Fail("A valid patch could not retry after V-collapse refusal.");
    }

    HdSilkNurbs::PatchInput scaledRationalPatch = rationalPatch;
    for (double& weight : scaledRationalPatch.weights)
    {
        weight *= 7.0;
    }
    HdSilkNurbs::PatchOutput scaledRationalPatchOutput;
    if (!HdSilkNurbs::EvaluatePatch(
            scaledRationalPatch,
            &scaledRationalPatchOutput,
            &error))
    {
        return Fail("Common-scaled rational patch evaluation failed: " + error);
    }
    if (scaledRationalPatchOutput.points.size() != rationalPatchOutput.points.size() ||
        scaledRationalPatchOutput.normals.size() != rationalPatchOutput.normals.size() ||
        scaledRationalPatchOutput.faceVertexIndices != rationalPatchOutput.faceVertexIndices)
    {
        return Fail("Common positive patch weight scaling changed tessellation topology.");
    }
    for (std::size_t index = 0; index < rationalPatchOutput.points.size(); ++index)
    {
        if (!NearPoint(scaledRationalPatchOutput.points[index],
                rationalPatchOutput.points[index]) ||
            !NearPoint(scaledRationalPatchOutput.normals[index],
                rationalPatchOutput.normals[index]))
        {
            return Fail("Common positive patch weight scaling changed surface points or normals.");
        }
    }

    HdSilkNurbs::CurvesInput orderOneCurve = MakeSingleCurve(
        {{5.0, 6.0, 7.0}},
        {1.0},
        1,
        {0.0, 1.0});
    HdSilkNurbs::CurvesOutput orderOneCurveOutput;
    if (!HdSilkNurbs::EvaluateCurves(orderOneCurve, &orderOneCurveOutput, &error) ||
        !HasFinitePointsAndMatchingRuns(orderOneCurveOutput) ||
        orderOneCurveOutput.vertexCounts != std::vector<int32_t>{9} ||
        !NearPoint(orderOneCurveOutput.points.front(), {5.0, 6.0, 7.0}) ||
        !NearPoint(orderOneCurveOutput.points.back(), {5.0, 6.0, 7.0}))
    {
        return Fail("The valid order-1 single-control curve boundary case failed.");
    }

    const auto expectCurveFailureClears = [&](
        const std::string& scenario,
        const HdSilkNurbs::CurvesInput& invalidInput)
    {
        HdSilkNurbs::CurvesOutput output = linearOutput;
        if (HdSilkNurbs::EvaluateCurves(invalidInput, &output, &error) ||
            !output.points.empty() ||
            !output.vertexCounts.empty())
        {
            std::cerr << "Curve failure did not clear output for " << scenario << ".\n";
            return false;
        }
        return true;
    };
    const auto expectPatchFailureClears = [&](
        const std::string& scenario,
        const HdSilkNurbs::PatchInput& invalidInput)
    {
        HdSilkNurbs::PatchOutput output = rationalPatchOutput;
        if (HdSilkNurbs::EvaluatePatch(invalidInput, &output, &error) ||
            !output.points.empty() ||
            !output.normals.empty() ||
            !output.faceVertexCounts.empty() ||
            !output.faceVertexIndices.empty())
        {
            std::cerr << "Patch failure did not clear output for " << scenario << ".\n";
            return false;
        }
        return true;
    };

    HdSilkNurbs::CurvesInput invalidCurve = linearCurve;
    invalidCurve.points.clear();
    invalidCurve.weights.clear();
    if (!expectCurveFailureClears("empty control data", invalidCurve))
    {
        return Fail("Curve evaluator accepted empty control data or retained stale output.");
    }
    invalidCurve = linearCurve;
    invalidCurve.vertexCounts.clear();
    if (!expectCurveFailureClears("missing curve counts", invalidCurve))
    {
        return Fail("Curve evaluator accepted missing curve counts.");
    }
    invalidCurve = linearCurve;
    invalidCurve.orders.clear();
    if (!expectCurveFailureClears("missing curve orders", invalidCurve))
    {
        return Fail("Curve evaluator accepted missing curve orders.");
    }
    invalidCurve = linearCurve;
    invalidCurve.ranges = {{0.0, 1.0}, {0.0, 1.0}};
    if (!expectCurveFailureClears("range count does not match curve count", invalidCurve))
    {
        return Fail("Curve evaluator accepted a range count inconsistent with its curves.");
    }
    invalidCurve = linearCurve;
    invalidCurve.vertexCounts = {3};
    if (!expectCurveFailureClears("inconsistent control count", invalidCurve))
    {
        return Fail("Curve evaluator accepted an inconsistent declared control count.");
    }
    invalidCurve = linearCurve;
    invalidCurve.weights = {1.0};
    if (!expectCurveFailureClears("inconsistent weight count", invalidCurve))
    {
        return Fail("Curve evaluator accepted a weight count different from its control count.");
    }
    invalidCurve = linearCurve;
    invalidCurve.weights = {1.0, 1.0, 1.0};
    if (!expectCurveFailureClears("excess curve weights", invalidCurve))
    {
        return Fail("Curve evaluator accepted more weights than control points.");
    }
    invalidCurve = MakeSingleCurve(
        {{0.0, 0.0, 0.0}, {2.0, 0.0, 0.0}},
        {1.0, 1.0},
        3,
        {0.0, 0.0, 0.0, 1.0, 1.0});
    if (!expectCurveFailureClears("order greater than control count", invalidCurve))
    {
        return Fail("Curve evaluator accepted fewer controls than its order.");
    }
    invalidCurve = linearCurve;
    invalidCurve.orders = {0};
    invalidCurve.knots = {0.0, 0.0};
    if (!expectCurveFailureClears("zero order", invalidCurve))
    {
        return Fail("Curve evaluator accepted order zero.");
    }
    invalidCurve = linearCurve;
    invalidCurve.knots.pop_back();
    if (!expectCurveFailureClears("one knot too few", invalidCurve))
    {
        return Fail("Curve evaluator accepted a knot vector shorter than its required cardinality.");
    }
    invalidCurve = linearCurve;
    invalidCurve.knots.push_back(1.0);
    if (!expectCurveFailureClears("one knot too many", invalidCurve))
    {
        return Fail("Curve evaluator accepted a knot vector longer than its required cardinality.");
    }
    invalidCurve = linearCurve;
    invalidCurve.knots = {0.0, 0.0, -0.1, 1.0};
    if (!expectCurveFailureClears("decreasing knots", invalidCurve))
    {
        return Fail("Curve evaluator accepted decreasing knot values.");
    }
    invalidCurve = linearCurve;
    invalidCurve.knots = {0.0, 0.0, 0.0, 0.0};
    if (!expectCurveFailureClears("collapsed active knot domain", invalidCurve))
    {
        return Fail("Curve evaluator accepted a collapsed active knot domain.");
    }
    invalidCurve = linearCurve;
    invalidCurve.ranges = {{0.75, 0.25}};
    if (!expectCurveFailureClears("reversed parameter range", invalidCurve))
    {
        return Fail("Curve evaluator accepted a reversed parameter range.");
    }
    invalidCurve = linearCurve;
    invalidCurve.ranges = {{0.5, 0.5}};
    if (!expectCurveFailureClears("zero-width parameter range", invalidCurve))
    {
        return Fail("Curve evaluator accepted a zero-width parameter range.");
    }
    invalidCurve = linearCurve;
    invalidCurve.ranges = {{
        std::nextafter(0.0, -std::numeric_limits<double>::infinity()), 0.5}};
    if (!expectCurveFailureClears("range immediately below domain", invalidCurve))
    {
        return Fail("Curve evaluator accepted a range immediately below its knot domain.");
    }
    invalidCurve = linearCurve;
    invalidCurve.ranges = {{
        0.5, std::nextafter(1.0, std::numeric_limits<double>::infinity())}};
    if (!expectCurveFailureClears("range immediately above domain", invalidCurve))
    {
        return Fail("Curve evaluator accepted a range immediately above its knot domain.");
    }

    const double nonFiniteValues[] = {
        std::numeric_limits<double>::quiet_NaN(),
        std::numeric_limits<double>::infinity(),
        -std::numeric_limits<double>::infinity()};
    const char* coordinateNames[] = {"x", "y", "z"};
    for (std::size_t coordinate = 0; coordinate < 3; ++coordinate)
    {
        for (double nonFinite : nonFiniteValues)
        {
            invalidCurve = linearCurve;
            if (coordinate == 0)
            {
                invalidCurve.points[0].x = nonFinite;
            }
            else if (coordinate == 1)
            {
                invalidCurve.points[0].y = nonFinite;
            }
            else
            {
                invalidCurve.points[0].z = nonFinite;
            }
            if (!expectCurveFailureClears(
                    std::string("non-finite point ") + coordinateNames[coordinate],
                    invalidCurve))
            {
                return Fail("Curve evaluator accepted or retained output for non-finite point data.");
            }
        }
    }
    for (double nonFinite : nonFiniteValues)
    {
        invalidCurve = linearCurve;
        invalidCurve.weights[0] = nonFinite;
        if (!expectCurveFailureClears("non-finite weight", invalidCurve))
        {
            return Fail("Curve evaluator accepted or retained output for a non-finite weight.");
        }
        invalidCurve = linearCurve;
        invalidCurve.knots[2] = nonFinite;
        if (!expectCurveFailureClears("non-finite knot", invalidCurve))
        {
            return Fail("Curve evaluator accepted or retained output for a non-finite knot.");
        }
        invalidCurve = linearCurve;
        invalidCurve.ranges = {{nonFinite, 1.0}};
        if (!expectCurveFailureClears("non-finite range minimum", invalidCurve))
        {
            return Fail("Curve evaluator accepted or retained output for a non-finite range minimum.");
        }
        invalidCurve = linearCurve;
        invalidCurve.ranges = {{0.0, nonFinite}};
        if (!expectCurveFailureClears("non-finite range maximum", invalidCurve))
        {
            return Fail("Curve evaluator accepted or retained output for a non-finite range maximum.");
        }
    }
    invalidCurve = linearCurve;
    invalidCurve.weights = {-1.0, 1.0};
    if (!expectCurveFailureClears("mixed-sign weights", invalidCurve))
    {
        return Fail("Curve evaluator accepted mixed-sign rational weights or retained stale output.");
    }
    invalidCurve = linearCurve;
    invalidCurve.weights = {0.0, 0.0};
    if (!expectCurveFailureClears("all-zero singular weights", invalidCurve))
    {
        return Fail("Singular curve evaluation did not fail with cleared output.");
    }
    invalidCurve = linearCurve;
    invalidCurve.weights = {0.0, 1.0};
    // The zero denominator is at the first sample, before curve output is appended.
    if (!expectCurveFailureClears("zero-weight evaluated endpoint", invalidCurve))
    {
        return Fail("Curve evaluator accepted a zero-denominator endpoint or retained output.");
    }

    HdSilkNurbs::PatchInput invalidPatch = rationalPatch;
    invalidPatch.points.pop_back();
    invalidPatch.weights.clear();
    if (!expectPatchFailureClears("control grid has too few points", invalidPatch))
    {
        return Fail("Patch evaluator accepted a control grid with too few points.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.weights.pop_back();
    if (!expectPatchFailureClears("inconsistent patch weight count", invalidPatch))
    {
        return Fail("Patch evaluator accepted a weight count different from its control grid.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.weights.push_back(1.0);
    if (!expectPatchFailureClears("excess patch weights", invalidPatch))
    {
        return Fail("Patch evaluator accepted more weights than control points.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.uVertexCount = 3;
    if (!expectPatchFailureClears("inconsistent U control count", invalidPatch))
    {
        return Fail("Patch evaluator accepted a U control count inconsistent with its grid.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.uVertexCount = 0;
    if (!expectPatchFailureClears("zero U control count", invalidPatch))
    {
        return Fail("Patch evaluator accepted a zero U control count.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.vVertexCount = 0;
    if (!expectPatchFailureClears("zero V control count", invalidPatch))
    {
        return Fail("Patch evaluator accepted a zero V control count.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.uOrder = 0;
    if (!expectPatchFailureClears("zero U order", invalidPatch))
    {
        return Fail("Patch evaluator accepted U order zero.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.vOrder = 0;
    if (!expectPatchFailureClears("zero V order", invalidPatch))
    {
        return Fail("Patch evaluator accepted V order zero.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.uOrder = 3;
    invalidPatch.uKnots = {0.0, 0.0, 0.0, 1.0, 1.0};
    if (!expectPatchFailureClears("U order greater than control count", invalidPatch))
    {
        return Fail("Patch evaluator accepted a U order greater than its control count.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.uKnots.pop_back();
    if (!expectPatchFailureClears("one U knot too few", invalidPatch))
    {
        return Fail("Patch evaluator accepted a U knot vector with the wrong cardinality.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.vKnots.push_back(1.0);
    if (!expectPatchFailureClears("one V knot too many", invalidPatch))
    {
        return Fail("Patch evaluator accepted a V knot vector with the wrong cardinality.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.uKnots = {0.0, 0.0, -0.1, 1.0};
    if (!expectPatchFailureClears("decreasing U knots", invalidPatch))
    {
        return Fail("Patch evaluator accepted decreasing U knots.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.vKnots = {0.0, 0.0, 0.0, 0.0};
    if (!expectPatchFailureClears("collapsed V domain", invalidPatch))
    {
        return Fail("Patch evaluator accepted a collapsed active V knot domain.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.uRange = {0.75, 0.25};
    invalidPatch.hasURange = true;
    if (!expectPatchFailureClears("reversed U range", invalidPatch))
    {
        return Fail("Patch evaluator accepted a reversed U range.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.vRange = {0.5, 0.5};
    invalidPatch.hasVRange = true;
    if (!expectPatchFailureClears("zero-width V range", invalidPatch))
    {
        return Fail("Patch evaluator accepted a zero-width V range.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.uRange = {0.5, 0.5};
    invalidPatch.hasURange = true;
    if (!expectPatchFailureClears("zero-width U range", invalidPatch))
    {
        return Fail("Patch evaluator accepted a zero-width U range.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.vRange = {0.75, 0.25};
    invalidPatch.hasVRange = true;
    if (!expectPatchFailureClears("reversed V range", invalidPatch))
    {
        return Fail("Patch evaluator accepted a reversed V range.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.uRange = {
        std::nextafter(0.0, -std::numeric_limits<double>::infinity()), 0.5};
    invalidPatch.hasURange = true;
    if (!expectPatchFailureClears("U range immediately below domain", invalidPatch))
    {
        return Fail("Patch evaluator accepted a U range immediately below its knot domain.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.vRange = {
        0.5, std::nextafter(1.0, std::numeric_limits<double>::infinity())};
    invalidPatch.hasVRange = true;
    if (!expectPatchFailureClears("V range immediately above domain", invalidPatch))
    {
        return Fail("Patch evaluator accepted a V range immediately above its knot domain.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.vRange = {
        std::nextafter(0.0, -std::numeric_limits<double>::infinity()), 0.5};
    invalidPatch.hasVRange = true;
    if (!expectPatchFailureClears("V range immediately below domain", invalidPatch))
    {
        return Fail("Patch evaluator accepted a V range immediately below its knot domain.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.uRange = {
        0.5, std::nextafter(1.0, std::numeric_limits<double>::infinity())};
    invalidPatch.hasURange = true;
    if (!expectPatchFailureClears("U range immediately above domain", invalidPatch))
    {
        return Fail("Patch evaluator accepted a U range immediately above its knot domain.");
    }

    for (std::size_t coordinate = 0; coordinate < 3; ++coordinate)
    {
        for (double nonFinite : nonFiniteValues)
        {
            invalidPatch = rationalPatch;
            if (coordinate == 0)
            {
                invalidPatch.points[0].x = nonFinite;
            }
            else if (coordinate == 1)
            {
                invalidPatch.points[0].y = nonFinite;
            }
            else
            {
                invalidPatch.points[0].z = nonFinite;
            }
            if (!expectPatchFailureClears(
                    std::string("non-finite patch point ") + coordinateNames[coordinate],
                    invalidPatch))
            {
                return Fail("Patch evaluator accepted or retained output for non-finite point data.");
            }
        }
    }
    for (double nonFinite : nonFiniteValues)
    {
        invalidPatch = rationalPatch;
        invalidPatch.weights[0] = nonFinite;
        if (!expectPatchFailureClears("non-finite patch weight", invalidPatch))
        {
            return Fail("Patch evaluator accepted or retained output for a non-finite weight.");
        }
        invalidPatch = rationalPatch;
        invalidPatch.uKnots[2] = nonFinite;
        if (!expectPatchFailureClears("non-finite U knot", invalidPatch))
        {
            return Fail("Patch evaluator accepted or retained output for a non-finite U knot.");
        }
        invalidPatch = rationalPatch;
        invalidPatch.vKnots[2] = nonFinite;
        if (!expectPatchFailureClears("non-finite V knot", invalidPatch))
        {
            return Fail("Patch evaluator accepted or retained output for a non-finite V knot.");
        }
        invalidPatch = rationalPatch;
        invalidPatch.uRange = {nonFinite, 1.0};
        invalidPatch.hasURange = true;
        if (!expectPatchFailureClears("non-finite U range minimum", invalidPatch))
        {
            return Fail("Patch evaluator accepted or retained output for a non-finite U range minimum.");
        }
        invalidPatch = rationalPatch;
        invalidPatch.uRange = {0.0, nonFinite};
        invalidPatch.hasURange = true;
        if (!expectPatchFailureClears("non-finite U range maximum", invalidPatch))
        {
            return Fail("Patch evaluator accepted or retained output for a non-finite U range maximum.");
        }
        invalidPatch = rationalPatch;
        invalidPatch.vRange = {nonFinite, 1.0};
        invalidPatch.hasVRange = true;
        if (!expectPatchFailureClears("non-finite V range minimum", invalidPatch))
        {
            return Fail("Patch evaluator accepted or retained output for a non-finite V range minimum.");
        }
        invalidPatch = rationalPatch;
        invalidPatch.vRange = {0.0, nonFinite};
        invalidPatch.hasVRange = true;
        if (!expectPatchFailureClears("non-finite V range maximum", invalidPatch))
        {
            return Fail("Patch evaluator accepted or retained output for a non-finite V range maximum.");
        }
    }
    invalidPatch = rationalPatch;
    invalidPatch.weights = {-1.0, 1.0, 1.0, 1.0};
    if (!expectPatchFailureClears("mixed-sign patch weights", invalidPatch))
    {
        return Fail("Patch evaluator accepted mixed-sign weights or retained stale output.");
    }
    invalidPatch = rationalPatch;
    invalidPatch.hasTrimData = true;
    if (!expectPatchFailureClears("trim-data marker", invalidPatch))
    {
        return Fail("Patch evaluator accepted its unsupported trim-data marker or retained stale output.");
    }

    HdSilkNurbs::CurvesInput freshInvalidCurve = linearCurve;
    freshInvalidCurve.orders = {0};
    HdSilkNurbs::CurvesOutput freshCurveFailureOutput;
    if (HdSilkNurbs::EvaluateCurves(
            freshInvalidCurve,
            &freshCurveFailureOutput,
            &error) ||
        !freshCurveFailureOutput.points.empty() ||
        !freshCurveFailureOutput.vertexCounts.empty())
    {
        return Fail("Fresh curve output was not empty after a malformed-input failure.");
    }
    HdSilkNurbs::PatchInput freshInvalidPatch = rationalPatch;
    freshInvalidPatch.vOrder = 0;
    HdSilkNurbs::PatchOutput freshPatchFailureOutput;
    if (HdSilkNurbs::EvaluatePatch(
            freshInvalidPatch,
            &freshPatchFailureOutput,
            &error) ||
        !freshPatchFailureOutput.points.empty() ||
        !freshPatchFailureOutput.normals.empty() ||
        !freshPatchFailureOutput.faceVertexCounts.empty() ||
        !freshPatchFailureOutput.faceVertexIndices.empty())
    {
        return Fail("Fresh patch output was not empty after a malformed-input failure.");
    }

    invalidPatch = rationalPatch;
    invalidPatch.weights = {1.0, 1.0, 0.0, 0.0};
    HdSilkNurbs::PatchOutput singularPatchOutput = rationalPatchOutput;
    if (HdSilkNurbs::EvaluatePatch(invalidPatch, &singularPatchOutput, &error) ||
        !singularPatchOutput.points.empty() ||
        !singularPatchOutput.normals.empty() ||
        !singularPatchOutput.faceVertexCounts.empty() ||
        !singularPatchOutput.faceVertexIndices.empty())
    {
        return Fail("A late singular patch evaluation was accepted or published partial output.");
    }

    HdSilkNurbs::PatchInput allZeroWeightPatch = rationalPatch;
    allZeroWeightPatch.weights = {0.0, 0.0, 0.0, 0.0};
    HdSilkNurbs::PatchOutput allZeroWeightPatchOutput = rationalPatchOutput;
    if (HdSilkNurbs::EvaluatePatch(
            allZeroWeightPatch,
            &allZeroWeightPatchOutput,
            &error) ||
        !allZeroWeightPatchOutput.points.empty() ||
        !allZeroWeightPatchOutput.normals.empty() ||
        !allZeroWeightPatchOutput.faceVertexCounts.empty() ||
        !allZeroWeightPatchOutput.faceVertexIndices.empty())
    {
        return Fail("All-zero patch weights were accepted or retained partial output.");
    }

    HdSilkNurbs::CurvesInput lateInvalidCurve;
    lateInvalidCurve.points = {
        {0.0, 0.0, 0.0}, {1.0, 0.0, 0.0},
        {2.0, 0.0, 0.0}, {3.0, 0.0, 0.0}};
    lateInvalidCurve.vertexCounts = {2, 2};
    lateInvalidCurve.orders = {2, 2};
    lateInvalidCurve.knots = {
        0.0, 0.0, 1.0, 1.0,
        0.0, 0.0, 1.0, 1.0};
    lateInvalidCurve.ranges = {{0.0, 1.0}, {0.5, 0.5}};
    HdSilkNurbs::CurvesOutput lateCurveFailureOutput = linearOutput;
    if (HdSilkNurbs::EvaluateCurves(
            lateInvalidCurve,
            &lateCurveFailureOutput,
            &error) ||
        !lateCurveFailureOutput.points.empty() ||
        !lateCurveFailureOutput.vertexCounts.empty())
    {
        return Fail("A later invalid curve was accepted or published earlier curve output.");
    }

    if (!HdSilkNurbs::EvaluatePatch(
            rationalPatch,
            &singularPatchOutput,
            &error) ||
        singularPatchOutput.points.size() != 81 ||
        singularPatchOutput.normals.size() != 81 ||
        singularPatchOutput.faceVertexCounts.size() != 64 ||
        !HdSilkNurbs::EvaluateCurves(
            linearCurve,
            &lateCurveFailureOutput,
            &error) ||
        !HasFinitePointsAndMatchingRuns(lateCurveFailureOutput))
    {
        return Fail("Valid evaluator calls did not recover into the same output objects after late failures.");
    }

    HdSilkNurbs::CurvesOutput recoveryCurveOutput;
    HdSilkNurbs::PatchOutput recoveryPatchOutput;
    if (!HdSilkNurbs::EvaluateCurves(linearCurve, &recoveryCurveOutput, &error) ||
        !HasFinitePointsAndMatchingRuns(recoveryCurveOutput) ||
        !HdSilkNurbs::EvaluatePatch(
            rationalPatch,
            &recoveryPatchOutput,
            &error) ||
        recoveryPatchOutput.points.size() != 81 ||
        recoveryPatchOutput.normals.size() != 81)
    {
        return Fail("Valid evaluator calls did not recover after preceding rejected inputs.");
    }

    if (VerifyPatchTessellationCapPreflight() != 0 ||
        VerifyPeriodicPatch() != 0 ||
        VerifyAttributesAndPlans() != 0)
    {
        return 1;
    }
    return 0;
}
