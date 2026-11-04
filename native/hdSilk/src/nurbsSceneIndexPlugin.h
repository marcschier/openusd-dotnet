// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef HDSILK_NURBS_SCENE_INDEX_PLUGIN_H
#define HDSILK_NURBS_SCENE_INDEX_PLUGIN_H

#include "pxr/pxr.h"
#include "pxr/base/tf/token.h"
#include "pxr/usd/sdf/path.h"
#include "pxr/imaging/hd/sceneIndexPlugin.h"

#include <cstdint>

PXR_NAMESPACE_OPEN_SCOPE

class HdSceneDelegate;

class HdSilk_NurbsSceneIndexPlugin final : public HdSceneIndexPlugin
{
public:
    HdSilk_NurbsSceneIndexPlugin();

protected:
    HdSceneIndexBaseRefPtr _AppendSceneIndex(
        const HdSceneIndexBaseRefPtr& inputScene,
        const HdContainerDataSourceHandle& inputArgs) override;
};

const TfToken& HdSilkNurbsGeneratedGeometryPrimvar();
bool HdSilkHasNurbsGeneratedGeometry(
    HdSceneDelegate* sceneDelegate,
    const SdfPath& primPath);

// Constant uint64 primvar on generated NURBS geometry. Holds output point,
// element, and run counts plus the number of retained vertex primvars. It is
// derived from topology only, so reading it does not evaluate control points.
const TfToken& HdSilkNurbsGeneratedGeometryPlanPrimvar();

struct HdSilkNurbsGeneratedGeometryPlan
{
    uint64_t points = 0;
    // Patch quads or curve segments.
    uint64_t elements = 0;
    uint64_t runs = 0;
    uint64_t vertexPrimvars = 0;
};

// Returns false without adapter-owned provenance. Throws for refused topology
// or missing/malformed metadata; neither is a zero-cost preparation plan.
bool HdSilkGetNurbsGeneratedGeometryPlan(
    HdSceneDelegate* sceneDelegate,
    const SdfPath& primPath,
    HdSilkNurbsGeneratedGeometryPlan* plan);

PXR_NAMESPACE_CLOSE_SCOPE

#endif
