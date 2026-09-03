// Copyright (c) marcschier. Licensed under the MIT License.
//
// Architecture note: this render pass follows the shape of Pixar's
// Apache-2.0 hdTiny example (OpenUSD extras/imaging/examples/hdTiny),
// adapted to capture HdRenderPassState into HdSilkSceneState instead of
// drawing directly.

#ifndef HDSILK_RENDER_PASS_H
#define HDSILK_RENDER_PASS_H

#include "pxr/pxr.h"
#include "pxr/imaging/hd/renderPass.h"

#include "sceneState.h"

#include <memory>

PXR_NAMESPACE_OPEN_SCOPE

/// HdSilkRenderPass captures the world-to-view matrix, projection matrix,
/// and viewport from HdRenderPassState for a single Hydra render iteration
/// and forwards them to the shared HdSilkSceneState so they can be embedded
/// as the FRAME command of the next page.
class HdSilkRenderPass final : public HdRenderPass
{
public:
    HdSilkRenderPass(
        HdRenderIndex* index,
        HdRprimCollection const& collection,
        std::shared_ptr<HdSilkSceneState> sceneState);
    ~HdSilkRenderPass() override = default;

protected:
    void _Execute(
        HdRenderPassStateSharedPtr const& renderPassState,
        TfTokenVector const& renderTags) override;

private:
    std::shared_ptr<HdSilkSceneState> _sceneState;
    // Whether the previous frame published a category membership table. Tracked
    // so that unlinking every light clears the table exactly once instead of
    // republishing an empty one on every frame that follows.
    bool _collectedMemberships = false;

    /// Collects the prim and instance categories UsdLux linking resolves
    /// against, but only while at least one light carries a non-default link
    /// collection.
    void _CollectCategoryMemberships();

    HdSilkRenderPass(const HdSilkRenderPass&) = delete;
    HdSilkRenderPass& operator=(const HdSilkRenderPass&) = delete;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
