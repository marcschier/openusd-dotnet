// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef HDSILK_CURVE_WIDTHS_H
#define HDSILK_CURVE_WIDTHS_H

#include "pxr/pxr.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/vt/array.h"
#include "pxr/base/vt/value.h"
#include "pxr/imaging/hd/basisCurvesTopology.h"

#include "sceneState.h"

#include <cstddef>
#include <vector>

PXR_NAMESPACE_OPEN_SCOPE

/// Authored width interpolations hdSilk resolves for linear segmented curves.
/// USD gives varying and vertex the same element count on linear curves -- one
/// value per control point -- so both resolve to Vertex.
enum class HdSilkCurveWidthInterpolation
{
    Constant,
    Uniform,
    Vertex
};

/// Authored widths after validation against the curve topology. "values" is
/// never empty, so every resolved lookup is in range by construction.
struct HdSilkCurveWidths
{
    HdSilkCurveWidthInterpolation interpolation =
        HdSilkCurveWidthInterpolation::Constant;
    std::vector<float> values{1.0f};
};

/// The UsdGeomCurves width fallback, used when no usable widths are authored.
constexpr float HdSilkDefaultCurveWidth = 1.0f;

/// Element count a given interpolation must supply for this topology. Vertex
/// widths are parallel to the points array, so an indexed topology expects one
/// value per authored point rather than one per flattened control-point slot:
/// the curve indices select into points and into the widths alike.
size_t HdSilkExpectedCurveWidthCount(
    const HdBasisCurvesTopology& topology,
    size_t pointCount,
    HdSilkCurveWidthInterpolation interpolation);

/// Whether an authored element count is usable for this interpolation.
///
/// This is the acceptance rule; HdSilkExpectedCurveWidthCount is the canonical
/// count. They differ for vertex widths on an unindexed topology, where an
/// array sized to the points array is accepted alongside one sized to the
/// flattened control-point count. Both are indexed identically -- an unindexed
/// curve's resolved point index is its flattened ordinal -- so a points array
/// longer than the curves consume, which USD permits, still resolves in range
/// instead of falling back to the default width.
bool HdSilkCurveWidthCountMatches(
    const HdBasisCurvesTopology& topology,
    size_t pointCount,
    HdSilkCurveWidthInterpolation interpolation,
    size_t authoredCount);

/// Resolves the authored "widths" value against the curve topology. Returns
/// false when the value holds a type or an element count no interpolation can
/// explain; the caller then falls back to HdSilkDefaultCurveWidth rather than
/// dropping the prim, because Storm still rasterizes such a curve.
///
/// The declared interpolation wins whenever its element count agrees with the
/// topology; otherwise the count itself selects one, so a delegate that
/// publishes widths without a usable primvar descriptor still renders.
bool HdSilkResolveCurveWidths(
    const HdBasisCurvesTopology& topology,
    size_t pointCount,
    const VtValue& authoredWidths,
    bool hasDeclaredInterpolation,
    HdSilkCurveWidthInterpolation declaredInterpolation,
    HdSilkCurveWidths* out);

/// Emits linear segmented curves as a line list with eye-facing normals and the
/// resolved widths already placed on the emitted vertices. Returns false for a
/// topology this delegate cannot express as independent segments, leaving
/// "record" partially populated for the caller to discard.
bool HdSilkBuildLinearSegmentedCurveLines(
    const HdBasisCurvesTopology& topology,
    const VtVec3fArray& points,
    const HdSilkCurveWidths& widths,
    HdSilkMeshRecord* record);

PXR_NAMESPACE_CLOSE_SCOPE

#endif
