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

/// HdSilkBasisCurves supports the linear/segmented curves emitted by
/// UsdImagingGLDrawModeAdapter for origin and bounds draw modes. It tessellates
/// each segment into a triangle-list ribbon and publishes that through the
/// existing mesh scene-state path so the page ABI and downstream renderer stay
/// unchanged.
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
    float _width = 1.0f;
    GfVec3f _displayColor{0.7f};
    uint64_t _topologyRevision = 0;
    bool _topologySupported = false;
    bool _widthSupported = true;

    HdSilkBasisCurves(const HdSilkBasisCurves&) = delete;
    HdSilkBasisCurves& operator=(const HdSilkBasisCurves&) = delete;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
