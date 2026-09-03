// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef HDSILK_MDL_MATERIAL_SCENE_INDEX_PLUGIN_H
#define HDSILK_MDL_MATERIAL_SCENE_INDEX_PLUGIN_H

#include "pxr/imaging/hd/sceneIndexPlugin.h"
#include "pxr/pxr.h"

PXR_NAMESPACE_OPEN_SCOPE

/// Gives an MDL shader node an identifier hdSilk can recognise.
///
/// A UsdShade shader whose implementation is an MDL source asset carries its
/// identity in `info:mdl:sourceAsset` and `info:mdl:sourceAsset:subIdentifier`.
/// UsdImaging publishes both under the material node's `nodeTypeInfo`, but it
/// can only fill in the node's `nodeIdentifier` from the Sdr registry, and this
/// runtime registers no MDL parser plugin, so the identifier arrives empty and
/// the legacy `HdMaterialNetwork` the render delegate reads keeps no field that
/// could carry the source asset instead.
///
/// This scene index folds the two `nodeTypeInfo` values into the node
/// identifier as `mdl:<module>:<material>` before the network reaches any
/// delegate. It changes no parameter and no connection, and it never touches a
/// node that already has an identifier, so a stage whose MDL nodes do resolve
/// through an installed Sdr plugin is left exactly as it was.
class HdSilk_MdlMaterialSceneIndexPlugin final : public HdSceneIndexPlugin
{
public:
    HdSilk_MdlMaterialSceneIndexPlugin();

protected:
    HdSceneIndexBaseRefPtr _AppendSceneIndex(
        const HdSceneIndexBaseRefPtr& inputScene,
        const HdContainerDataSourceHandle& inputArgs) override;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
