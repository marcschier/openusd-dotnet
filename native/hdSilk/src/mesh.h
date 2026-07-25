// Copyright (c) marcschier. Licensed under the MIT License.
//
// Architecture note: this Rprim follows the shape of Pixar's Apache-2.0
// hdTiny/hdEmbree examples (OpenUSD extras/imaging/examples/hdTiny,
// pxr/imaging/plugin/hdEmbree): pull scene data through HdSceneDelegate
// only for the bits marked dirty by HdDirtyBits, triangulate with
// HdMeshUtil, and publish the result rather than draw it directly.

#ifndef HDSILK_MESH_H
#define HDSILK_MESH_H

#include "pxr/pxr.h"
#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/vt/array.h"
#include "pxr/imaging/hd/mesh.h"
#include "pxr/imaging/hd/meshTopology.h"

#include <cstdint>
#include <vector>

PXR_NAMESPACE_OPEN_SCOPE

struct HdSilkMeshRecord;

/// HdSilkMesh is the only supported Rprim type in this walking skeleton. Its
/// Sync() pulls topology, points, and transform from the scene delegate
/// whenever Hydra marks them dirty, triangulates the topology with
/// HdMeshUtil::ComputeTriangleIndices, and publishes the result to the
/// render delegate's shared HdSilkSceneState so it can be serialized into a
/// page of wire commands, including the canonical displayColor primvar.
class HdSilkMesh final : public HdMesh
{
public:
    explicit HdSilkMesh(SdfPath const& id);
    ~HdSilkMesh() override = default;

    /// Inform the scene graph which state needs to be downloaded on the
    /// first Sync() call.
    HdDirtyBits GetInitialDirtyBitsMask() const override;

    /// Pulls invalidated scene data (per dirtyBits) and publishes the
    /// resulting mesh into the shared HdSilkSceneState. May be called from
    /// worker threads in parallel with Sync() of other Rprims.
    void Sync(
        HdSceneDelegate* sceneDelegate,
        HdRenderParam* renderParam,
        HdDirtyBits* dirtyBits,
        TfToken const& reprToken) override;

protected:
    void _InitRepr(TfToken const& reprToken, HdDirtyBits* dirtyBits) override;
    HdDirtyBits _PropagateDirtyBits(HdDirtyBits bits) const override;

private:
    /// Expands one published record into per-instance records. A prim with no
    /// instancer yields exactly one record at instance index 0.
    std::vector<HdSilkMeshRecord> _BuildInstanceRecords(
        HdSceneDelegate* sceneDelegate,
        HdSilkMeshRecord record);

    HdMeshTopology _topology;
    GfMatrix4d _transform;
    VtVec3fArray _points;
    std::vector<uint32_t> _triangleIndices;
    std::vector<uint32_t> _triangleSubprims;
    uint64_t _topologyRevision = 0;
    GfVec3f _displayColor{0.7f};

    HdSilkMesh(const HdSilkMesh&) = delete;
    HdSilkMesh& operator=(const HdSilkMesh&) = delete;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
