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
#include "pxr/usd/usd/stage.h"
#include "pxr/usd/usd/timeCode.h"
#include "pxr/usd/usdSkel/blendShapeQuery.h"
#include "pxr/usd/usdSkel/skeletonQuery.h"
#include "pxr/usd/usdSkel/skinningQuery.h"

#include "sceneState.h"
#include "subdivision.h"

#include <cstdint>
#include <vector>

PXR_NAMESPACE_OPEN_SCOPE

struct HdSilkMeshRecord;

/// Makes the currently rendered USD stage/time visible to Rprim Sync so
/// hdSilk can evaluate supported UsdSkel deformation directly instead of
/// consuming Hydra's CPU ExtComputation output for points.
///
/// The scope is keyed by the render delegate's scene state rather than by
/// thread, because Hydra syncs Rprims on a worker pool that the thread calling
/// Render() does not own, so a thread-local scope reaches only the prims that
/// happen to sync on the calling thread.
void HdSilkBeginUsdSkelEvaluation(
    HdSilkSceneState const* sceneState,
    UsdStageRefPtr const& stage,
    UsdTimeCode time);
void HdSilkEndUsdSkelEvaluation(HdSilkSceneState const* sceneState) noexcept;

/// The UsdSkel binding and deformation state cached on one mesh across Syncs.
///
/// Resolving a binding walks the whole SkelRoot, so it is resolved once per
/// stage rather than once per Sync. The blend-shape point indices and sub-shape
/// offsets are cached with it because UsdSkelBlendShapeQuery exposes no
/// time-sampled accessor for them, so they cannot vary with the evaluation
/// time; only the sub-shape weights and skinning transforms are re-resolved
/// per time code.
struct HdSilkMeshSkelState
{
    bool resolved = false;
    bool valid = false;
    const UsdStage* stage = nullptr;
    UsdSkelSkeletonQuery skeletonQuery;
    UsdSkelSkinningQuery skinningQuery;
    UsdSkelBlendShapeQuery blendShapeQuery;
    std::vector<VtIntArray> blendShapePointIndices;
    std::vector<VtVec3fArray> subShapePointOffsets;
    std::vector<VtVec3fArray> subShapeNormalOffsets;
    bool hasNormalOffsets = false;

    /// Per-point normals deformed alongside the published points. Empty when
    /// the mesh authors none or when the deformation cannot carry them.
    VtVec3fArray deformedNormals;
    /// Set while the last Sync published CPU-deformed points for this mesh.
    bool deformed = false;
    /// Set when the mesh authors normals the deformation cannot carry, so the
    /// authored bind-pose array is omitted rather than published stale.
    bool normalsUndeformable = false;

    /// The bounded rig describing the same deformation, published on the ABI
    /// v20 deformation block beside the authoritative CPU result. It is
    /// rebuilt whenever the deformation is, because its joint palette and its
    /// resolved sub-shape weights are both evaluated at the capture time.
    HdSilkMeshDeformation rig;
};

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
    /// instancer yields exactly one full record at instance index 0. Point
    /// instancers publish full prototype geometry only in instance zero; later
    /// records carry identity and transform only.
    std::vector<HdSilkMeshRecord> _BuildInstanceRecords(
        HdSceneDelegate* sceneDelegate,
        HdSilkMeshRecord record);

    /// Rebuilds the vertex attribute table from authored primvars.
    void _RefreshAttributes(HdSceneDelegate* sceneDelegate, SdfPath const& id);

    /// Rebuilds the OpenSubdiv refiner and the emitted triangle tables for the
    /// requested refinement level, then republishes either the refined or the
    /// coarse HdMeshUtil topology. A mesh that cannot be refined keeps the
    /// complete control cage rather than a partially refined surface.
    void _RefreshSubdivision(
        HdSceneDelegate* sceneDelegate,
        SdfPath const& id,
        int refineLevel);

    /// Collects one OpenSubdiv face-varying channel per authored face-varying
    /// primvar, using the authored index array when the primvar is indexed and
    /// the identity sequence when it is not -- which is how Storm binds the
    /// same channels.
    ///
    /// Every authored face-varying primvar is reported, including one whose
    /// topology cannot be described: such a channel carries an
    /// unsupportedReason so refinement is refused as a whole rather than
    /// publishing a refined surface that silently lost that primvar.
    std::vector<HdSilkSubdivisionChannel> _CollectFaceVaryingChannels(
        HdSceneDelegate* sceneDelegate,
        SdfPath const& id) const;

    /// Refines one packed vertex- or varying-interpolated primvar in place.
    /// Normals are renormalized afterwards, on the same terms as the refined
    /// face-varying path, because subdivision averages directions and an
    /// averaged unit vector is shorter than one.
    bool _RefineVertexAttribute(
        HdInterpolation interpolation,
        const HdPrimvarDescriptor& primvar,
        uint32_t componentCount,
        std::vector<float>* data) const;

    /// The points the emitted triangle indices address: the refined array while
    /// a subdivision surface is refined, and the authored/deformed control
    /// points otherwise.
    const VtVec3fArray& _EmittedPoints() const
    {
        return _subdivision.IsRefined() ? _refinedPoints : _points;
    }

    HdMeshTopology _topology;
    GfMatrix4d _transform;
    VtVec3fArray _points;
    // The emitted triangle list: the refined tables while a subdivision surface
    // is refined, and the HdMeshUtil triangulation of the control cage
    // otherwise. The coarse tables are retained separately so switching
    // refinement off does not need a topology resync to restore them.
    std::vector<uint32_t> _triangleIndices;
    std::vector<uint32_t> _triangleSubprims;
    std::vector<uint32_t> _coarseTriangleIndices;
    std::vector<uint32_t> _coarseTriangleSubprims;
    // The ABI v22 authored-edge table of the control cage: one entry per coarse
    // triangle corner, in corner order, naming the authored mesh edge that
    // corner spans or OPENUSD_SILK_SUBPRIM_NONE when the corner is a
    // triangulation diagonal of an n-gon. Derived from the authored topology
    // alone, so it is rebuilt exactly when the topology is, beside the coarse
    // triangulation it indexes.
    std::vector<uint32_t> _coarseCornerEdges;
    uint32_t _coarseAuthoredEdgeCount = 0;
    // Refined control cage state. The refiner and every table derived from
    // topology alone survive across frames, so animated points re-run
    // interpolation only; _refineLevel is the level last attempted, so a mesh
    // that could not be refined is not retried on every Sync.
    HdSilkMeshSubdivision _subdivision;
    int _refineLevel = 0;
    VtVec3fArray _refinedPoints;
    uint64_t _topologyRevision = 0;
    GfVec3f _displayColor{0.7f};
    // CPU-resolved UsdSkel deformation. The binding is resolved once per stage
    // because resolving it walks the whole SkelRoot; the deformed normals are
    // republished whenever the deformed points change so a time-varying rig
    // cannot leave bind-pose normals on a moved surface.
    HdSilkMeshSkelState _skel;
    // Authored primvars published over the ABI 4 attribute table: normals, so
    // the consumer stops recomputing them from topology, plus texture
    // coordinates and arbitrary primvars. Only interpolations resolvable onto
    // emitted triangle-list vertices appear here; anything else is omitted so
    // the consumer falls back rather than receiving data this delegate guessed.
    std::vector<HdSilkMeshAttribute> _attributes;
    bool _attributesRequireExpandedTopology = false;

    HdSilkMesh(const HdSilkMesh&) = delete;
    HdSilkMesh& operator=(const HdSilkMesh&) = delete;
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
