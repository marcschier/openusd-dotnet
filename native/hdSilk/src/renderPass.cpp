// Copyright (c) marcschier. Licensed under the MIT License.

#include "renderPass.h"

#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec4f.h"
#include "pxr/imaging/hd/renderPassState.h"

#include <utility>

PXR_NAMESPACE_OPEN_SCOPE

HdSilkRenderPass::HdSilkRenderPass(
    HdRenderIndex* index,
    HdRprimCollection const& collection,
    std::shared_ptr<HdSilkSceneState> sceneState)
    : HdRenderPass(index, collection)
    , _sceneState(std::move(sceneState))
{
}

void
HdSilkRenderPass::_Execute(
    HdRenderPassStateSharedPtr const& renderPassState,
    TfTokenVector const& /*renderTags*/)
{
    if (!_sceneState || !renderPassState)
    {
        return;
    }

    const GfMatrix4d viewMatrix = renderPassState->GetWorldToViewMatrix();
    const GfMatrix4d projectionMatrix = renderPassState->GetProjectionMatrix();
    const GfVec4f viewport = renderPassState->GetViewport();

    HdSilkFrameState frame;
    frame.width = static_cast<int32_t>(viewport[2]);
    frame.height = static_cast<int32_t>(viewport[3]);
    HdSilkFlattenMatrix(viewMatrix, frame.viewMatrix);
    HdSilkFlattenMatrix(projectionMatrix, frame.projectionMatrix);

    _sceneState->SetFrame(frame);
}

PXR_NAMESPACE_CLOSE_SCOPE
