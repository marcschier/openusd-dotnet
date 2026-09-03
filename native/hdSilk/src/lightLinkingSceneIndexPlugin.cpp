// Copyright (c) marcschier. Licensed under the MIT License.

#include "lightLinkingSceneIndexPlugin.h"

#include "pxr/base/tf/staticTokens.h"
#include "pxr/imaging/hd/sceneIndexPluginRegistry.h"
#include "pxr/imaging/hdsi/lightLinkingSceneIndex.h"

PXR_NAMESPACE_OPEN_SCOPE

TF_DEFINE_PRIVATE_TOKENS(
    _hdSilkLightLinkingTokens,
    ((sceneIndexPluginName, "HdSilk_LightLinkingSceneIndexPlugin"))
);

namespace
{
// Must match the "displayName" in resources/plugInfo.json.in.
const char* const _pluginDisplayName = "Silk.NET";
}

TF_REGISTRY_FUNCTION(TfType)
{
    HdSceneIndexPluginRegistry::Define<HdSilk_LightLinkingSceneIndexPlugin>();
}

TF_REGISTRY_FUNCTION(HdSceneIndexPlugin)
{
    const HdSceneIndexPluginRegistry::InsertionPhase insertionPhase = 0;

    // Appended at the end of the phase so that it observes the scene every
    // earlier plugin produced. The implicit-surface plugin runs at the start of
    // the same phase and turns UsdGeomCube and friends into meshes; linking has
    // to see those as the geometry prims they became, because the category it
    // computes is keyed by prim type.
    HdSceneIndexPluginRegistry::GetInstance().RegisterSceneIndexForRenderer(
        _pluginDisplayName,
        _hdSilkLightLinkingTokens->sceneIndexPluginName,
        /* inputArgs = */ nullptr,
        insertionPhase,
        HdSceneIndexPluginRegistry::InsertionOrderAtEnd);
}

HdSilk_LightLinkingSceneIndexPlugin::HdSilk_LightLinkingSceneIndexPlugin() = default;

HdSceneIndexBaseRefPtr
HdSilk_LightLinkingSceneIndexPlugin::_AppendSceneIndex(
    const HdSceneIndexBaseRefPtr& inputScene,
    const HdContainerDataSourceHandle& /*inputArgs*/)
{
    // No input arguments: the defaults cover every Rprim and light type Hydra
    // declares, which is exactly the set hdSilk publishes from.
    return HdsiLightLinkingSceneIndex::New(inputScene, nullptr);
}

PXR_NAMESPACE_CLOSE_SCOPE
