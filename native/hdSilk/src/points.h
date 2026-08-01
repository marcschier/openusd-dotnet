// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef HDSILK_POINTS_H
#define HDSILK_POINTS_H

#include "pxr/pxr.h"
#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/vt/array.h"
#include "pxr/imaging/hd/points.h"

#include "sceneState.h"

#include <cstdint>
#include <vector>

PXR_NAMESPACE_OPEN_SCOPE

struct HdSilkMeshRecord;

class HdSilkPoints final : public HdPoints
{
public:
    explicit HdSilkPoints(SdfPath const& id);
    ~HdSilkPoints() override = default;

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

    GfMatrix4d _transform;
    VtVec3fArray _points;
    GfVec3f _displayColor{0.7f};
    uint64_t _topologyRevision = 1;

    HdSilkPoints(const HdSilkPoints&) = delete;
    HdSilkPoints& operator=(const HdSilkPoints&) = delete;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
