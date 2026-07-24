// Copyright (c) marcschier. Licensed under the MIT License.

#include "implicitSurfaceSceneIndexPlugin.h"

#include "pxr/base/tf/staticTokens.h"
#include "pxr/imaging/hd/retainedDataSource.h"
#include "pxr/imaging/hd/sceneIndexPluginRegistry.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/imaging/hdsi/implicitSurfaceSceneIndex.h"

PXR_NAMESPACE_OPEN_SCOPE

TF_DEFINE_PRIVATE_TOKENS(
    _hdSilkImplicitSurfaceTokens,
    ((sceneIndexPluginName, "HdSilk_ImplicitSurfaceSceneIndexPlugin"))
);

namespace
{
// Must match the "displayName" in resources/plugInfo.json.in.
const char* const _pluginDisplayName = "Silk.NET";
}

TF_REGISTRY_FUNCTION(TfType)
{
    HdSceneIndexPluginRegistry::Define<HdSilk_ImplicitSurfaceSceneIndexPlugin>();
}

TF_REGISTRY_FUNCTION(HdSceneIndexPlugin)
{
    const HdSceneIndexPluginRegistry::InsertionPhase insertionPhase = 0;

    HdSceneIndexPluginRegistry::GetInstance().RegisterSceneIndexForRenderer(
        _pluginDisplayName,
        _hdSilkImplicitSurfaceTokens->sceneIndexPluginName,
        /* inputArgs = */ nullptr,
        insertionPhase,
        HdSceneIndexPluginRegistry::InsertionOrderAtStart);
}

HdSilk_ImplicitSurfaceSceneIndexPlugin::HdSilk_ImplicitSurfaceSceneIndexPlugin() = default;

HdSceneIndexBaseRefPtr
HdSilk_ImplicitSurfaceSceneIndexPlugin::_AppendSceneIndex(
    const HdSceneIndexBaseRefPtr& inputScene,
    const HdContainerDataSourceHandle& /*inputArgs*/)
{
    HdDataSourceBaseHandle const toMeshSource =
        HdRetainedTypedSampledDataSource<TfToken>::New(
            HdsiImplicitSurfaceSceneIndexTokens->toMesh);

    HdContainerDataSourceHandle const localInputArgs =
        HdRetainedContainerDataSource::New(
            HdPrimTypeTokens->sphere, toMeshSource,
            HdPrimTypeTokens->cube, toMeshSource,
            HdPrimTypeTokens->cone, toMeshSource,
            HdPrimTypeTokens->cylinder, toMeshSource,
            HdPrimTypeTokens->capsule, toMeshSource,
            HdPrimTypeTokens->plane, toMeshSource);

    return HdsiImplicitSurfaceSceneIndex::New(inputScene, localInputArgs);
}

PXR_NAMESPACE_CLOSE_SCOPE
