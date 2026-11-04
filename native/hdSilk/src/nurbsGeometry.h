// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef HDSILK_NURBS_GEOMETRY_H
#define HDSILK_NURBS_GEOMETRY_H

#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

namespace HdSilkNurbs
{

constexpr size_t SamplesPerKnotSpan = 8;
constexpr size_t MaxTessellatedPoints = 1u << 20;
constexpr size_t MaxEvaluationWork = 64u * 1024u * 1024u;
// Each list carries up to three components of retained vertex attributes.
constexpr size_t MaxAttributeLists = 16;

struct Point
{
    double x = 0.0;
    double y = 0.0;
    double z = 0.0;
};

struct Range
{
    double minimum = 0.0;
    double maximum = 0.0;
};

struct CurvesInput
{
    std::vector<Point> points;
    std::vector<double> weights;
    std::vector<int32_t> vertexCounts;
    std::vector<int32_t> orders;
    std::vector<double> knots;
    std::vector<Range> ranges;
    // Vertex attributes interpolated with the same rational basis as points.
    std::vector<std::vector<Point>> attributes;
};

struct CurvesOutput
{
    std::vector<Point> points;
    std::vector<int32_t> vertexCounts;
    std::vector<std::vector<Point>> attributes;
};

struct PatchInput
{
    std::vector<Point> points;
    std::vector<double> weights;
    int32_t uVertexCount = 0;
    int32_t vVertexCount = 0;
    int32_t uOrder = 0;
    int32_t vOrder = 0;
    std::vector<double> uKnots;
    std::vector<double> vKnots;
    Range uRange;
    Range vRange;
    bool hasURange = false;
    bool hasVRange = false;
    bool hasTrimData = false;
    bool leftHanded = false;
    std::vector<std::vector<Point>> attributes;
};

struct PatchOutput
{
    std::vector<Point> points;
    std::vector<Point> normals;
    std::vector<int32_t> faceVertexCounts;
    std::vector<int32_t> faceVertexIndices;
    std::vector<std::vector<Point>> attributes;
};

// Output sizes derived only from counts, orders, knots, and ranges. Points,
// weights, and attribute values are not read. Elements are curve segments
// or patch quads; runs are emitted curve polylines.
struct GeometryPlan
{
    size_t points = 0;
    size_t elements = 0;
    size_t runs = 0;
};

bool TryAddEvaluationWork(
    size_t uOrder,
    size_t vOrder,
    size_t sampleCount,
    bool isPatch,
    size_t* totalWork);

bool PlanCurves(
    const CurvesInput& input,
    size_t attributeLists,
    GeometryPlan* plan,
    std::string* error);

bool PlanPatch(
    const PatchInput& input,
    size_t attributeLists,
    GeometryPlan* plan,
    std::string* error);

bool EvaluateCurves(
    const CurvesInput& input,
    CurvesOutput* output,
    std::string* error);

bool EvaluatePatch(
    const PatchInput& input,
    PatchOutput* output,
    std::string* error);

} // namespace HdSilkNurbs

#endif
