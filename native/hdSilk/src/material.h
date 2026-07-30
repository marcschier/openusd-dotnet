// Copyright (c) marcschier. Licensed under the MIT License.
//
// Architecture note: this Sprim follows the shape of Pixar's Apache-2.0
// hdEmbree material handling (OpenUSD pxr/imaging/plugin/hdEmbree): pull the
// HdMaterialNetworkMap through the scene delegate when Hydra marks it dirty,
// then resolve it rather than draw with it.

#ifndef HDSILK_MATERIAL_H
#define HDSILK_MATERIAL_H

#include "pxr/pxr.h"
#include "pxr/imaging/hd/material.h"

#include <memory>

PXR_NAMESPACE_OPEN_SCOPE

class HdSilkSceneState;
struct HdSilkMaterialRecord;

/// Resolves a bound UsdPreviewSurface network into the flat, pointer-free
/// material record published over the ABI v5 wire format.
///
/// A network this delegate does not understand is still published, marked
/// unsupported with empty parameter tables, so the consumer can diagnose it
/// visibly rather than silently approximate an unsupported shading graph.
class HdSilkMaterial final : public HdMaterial
{
public:
    explicit HdSilkMaterial(SdfPath const& id);
    ~HdSilkMaterial() override = default;

    HdDirtyBits GetInitialDirtyBitsMask() const override;

    void Sync(
        HdSceneDelegate* sceneDelegate,
        HdRenderParam* renderParam,
        HdDirtyBits* dirtyBits) override;

    /// Resolves a network map into a record. Exposed for focused native tests
    /// so the resolution rules can be proven without a render index.
    static HdSilkMaterialRecord Resolve(
        SdfPath const& id,
        HdMaterialNetworkMap const& networkMap);

private:
    HdSilkMaterial(const HdSilkMaterial&) = delete;
    HdSilkMaterial& operator=(const HdSilkMaterial&) = delete;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
