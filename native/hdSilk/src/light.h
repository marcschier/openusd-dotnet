// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef HDSILK_LIGHT_H
#define HDSILK_LIGHT_H

#include "pxr/pxr.h"
#include "pxr/imaging/hd/light.h"
#include "pxr/base/tf/token.h"

PXR_NAMESPACE_OPEN_SCOPE

class HdSilkLight final : public HdLight
{
public:
    HdSilkLight(SdfPath const& id, TfToken typeId);
    ~HdSilkLight() override = default;

    HdDirtyBits GetInitialDirtyBitsMask() const override;

    void Sync(
        HdSceneDelegate* sceneDelegate,
        HdRenderParam* renderParam,
        HdDirtyBits* dirtyBits) override;

private:
    TfToken _typeId;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
