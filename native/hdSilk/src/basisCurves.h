// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef HDSILK_BASIS_CURVES_H
#define HDSILK_BASIS_CURVES_H

#include "pxr/pxr.h"
#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/vt/array.h"
#include "pxr/imaging/hd/basisCurves.h"
#include "pxr/imaging/hd/basisCurvesTopology.h"

#include "sceneState.h"

#include <cstdint>
#include <vector>

PXR_NAMESPACE_OPEN_SCOPE

struct HdSilkMeshRecord;

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

/// HdSilkBasisCurves supports the linear/segmented curves emitted by
/// UsdImagingGLDrawModeAdapter for origin and bounds draw modes, and plain
/// linear segmented BasisCurves prims. It publishes each segment through the
/// existing mesh scene-state path as line-list topology so the page ABI stays
/// unchanged while matching Storm's measured one-pixel line rasterization.
/// Authored widths are resolved onto the emitted line vertices for constant,
/// uniform, varying, and vertex interpolation and published through the vertex
/// attribute table.
class HdSilkBasisCurves final : public HdBasisCurves
{
public:
    explicit HdSilkBasisCurves(SdfPath const& id);
    ~HdSilkBasisCurves() override = default;

    HdDirtyBits GetInitialDirtyBitsMask() const override;

    void Sync(
        HdSceneDelegate* sceneDelegate,
        HdRenderParam* renderParam,
        HdDirtyBits* dirtyBits,
        TfToken const& reprToken) override;

protected:
    void _InitRepr(TfToken const& reprToken, HdDirtyBits* dirtyBits) override;
    HdDirtyBits _PropagateDirtyBits(HdDirtyBits bits) const override;

private:
    std::vector<HdSilkMeshRecord> _BuildInstanceRecords(
        HdSceneDelegate* sceneDelegate,
        HdSilkMeshRecord record);

    HdBasisCurvesTopology _topology;
    GfMatrix4d _transform;
    VtVec3fArray _points;
    HdSilkCurveWidths _widths;
    GfVec3f _displayColor{0.7f};
    uint64_t _topologyRevision = 0;
    bool _topologySupported = false;

    HdSilkBasisCurves(const HdSilkBasisCurves&) = delete;
    HdSilkBasisCurves& operator=(const HdSilkBasisCurves&) = delete;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
