// Copyright (c) marcschier. Licensed under the MIT License.
//
// Architecture note: this scene index plugin mirrors Pixar's Apache-2.0
// hdEmbree plugin (pxr/imaging/plugin/hdEmbree/implicitSurfaceSceneIndexPlugin.*).
// hdSilk, like hdEmbree, does not natively support implicit surface Rprims
// (cube/sphere/cone/cylinder/capsule/plane); this plugin configures
// HdsiImplicitSurfaceSceneIndex to convert them into HdMesh prims before
// they ever reach HdSilkRenderDelegate::CreateRprim.

#ifndef HDSILK_IMPLICIT_SURFACE_SCENE_INDEX_PLUGIN_H
#define HDSILK_IMPLICIT_SURFACE_SCENE_INDEX_PLUGIN_H

#include "pxr/pxr.h"
#include "pxr/imaging/hd/sceneIndexPlugin.h"

PXR_NAMESPACE_OPEN_SCOPE

/// HdSilk_ImplicitSurfaceSceneIndexPlugin configures the implicit surface
/// scene index to generate meshes for implicit surfaces so that
/// HdSilkMesh::Sync sees ordinary mesh topology/points for prims such as
/// UsdGeomCube.
class HdSilk_ImplicitSurfaceSceneIndexPlugin : public HdSceneIndexPlugin
{
public:
    HdSilk_ImplicitSurfaceSceneIndexPlugin();

protected:
    HdSceneIndexBaseRefPtr _AppendSceneIndex(
        const HdSceneIndexBaseRefPtr& inputScene,
        const HdContainerDataSourceHandle& inputArgs) override;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
