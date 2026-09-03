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

    /// Number of ND_surface_unlit fragment generations that failed.
    ///
    /// A generation failure no longer changes the published surface kind -- the
    /// material stays OPENUSD_SILK_SURFACE_MATERIALX_GENERATED with an empty
    /// payload, because ND_surface_unlit is unlit whether or not a fragment was
    /// produced -- so the failure has no representation in the page and needs
    /// somewhere of its own to be observed from.
    static uint64_t GetGeneratedSurfaceFailureCountForTesting();

    /// Forces the next ND_surface_unlit generation to fail.
    ///
    /// The failure path is otherwise unreachable in a build whose MaterialX
    /// generation succeeds, and it is exactly the path whose behaviour matters.
    static void SetGeneratedSurfaceFailureForTesting(bool fail);

private:
    HdSilkMaterial(const HdSilkMaterial&) = delete;
    HdSilkMaterial& operator=(const HdSilkMaterial&) = delete;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
