// Copyright (c) marcschier. Licensed under the MIT License.
//
// Architecture note: this scene index plugin follows the shape of hdSilk's
// existing implicit-surface plugin, which in turn mirrors Pixar's Apache-2.0
// hdEmbree plugin registration.

#ifndef HDSILK_LIGHT_LINKING_SCENE_INDEX_PLUGIN_H
#define HDSILK_LIGHT_LINKING_SCENE_INDEX_PLUGIN_H

#include "pxr/pxr.h"
#include "pxr/imaging/hd/sceneIndexPlugin.h"

PXR_NAMESPACE_OPEN_SCOPE

/// HdSilk_LightLinkingSceneIndexPlugin inserts HdsiLightLinkingSceneIndex into
/// hdSilk's scene index chain.
///
/// UsdImaging transports a UsdLux light-link or shadow-link collection as a
/// membership expression on the light prim and nothing else; it does not assign
/// the category identifiers a render delegate reads, and no scene index in
/// OpenUSD inserts itself to do so. A renderer that wants linking has to ask for
/// it. Without this plugin hdSilk sees an empty light-link identity on every
/// light and empty categories on every prim, which is indistinguishable from a
/// scene that authors no linking at all -- so the collection would be silently
/// ignored rather than diagnosed.
class HdSilk_LightLinkingSceneIndexPlugin : public HdSceneIndexPlugin
{
public:
    HdSilk_LightLinkingSceneIndexPlugin();

protected:
    HdSceneIndexBaseRefPtr _AppendSceneIndex(
        const HdSceneIndexBaseRefPtr& inputScene,
        const HdContainerDataSourceHandle& inputArgs) override;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
