// Copyright (c) marcschier. Licensed under the MIT License.

#include "hdsilk_test_hooks.h"

#include "geometry_sync_page.h"
#include "mesh.h"
#include "renderDelegate.h"

#include "pxr/base/gf/matrix4f.h"
#include "pxr/base/gf/vec3d.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/vt/array.h"
#include "pxr/base/vt/value.h"
#include "pxr/imaging/hd/changeTracker.h"
#include "pxr/imaging/hd/extComputationContext.h"
#include "pxr/imaging/hd/renderIndex.h"
#include "pxr/imaging/hd/rprim.h"
#include "pxr/imaging/hd/sprim.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/imaging/hd/unitTestDelegate.h"
#include "pxr/imaging/pxOsd/tokens.h"
#include "pxr/usd/usd/stage.h"
#include "pxr/usd/usd/timeCode.h"

#include <memory>
#include <string>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
class MeshPointDelegate final : public HdUnitTestDelegate
{
public:
    MeshPointDelegate(HdRenderIndex* index, const SdfPath& meshId)
        : HdUnitTestDelegate(index, SdfPath::AbsoluteRootPath())
        , _meshId(meshId)
    {
    }

    VtValue Get(const SdfPath& id, const TfToken& key) override
    {
        if (_overridePoints && id == _meshId && key == HdTokens->points)
        {
            return _authoredPoints;
        }
        return HdUnitTestDelegate::Get(id, key);
    }

    HdExtComputationPrimvarDescriptorVector GetExtComputationPrimvarDescriptors(
        const SdfPath& id, HdInterpolation interpolation) override
    {
        if (_computedEnabled && id == _meshId && interpolation == HdInterpolationVertex)
        {
            return {HdExtComputationPrimvarDescriptor(
                HdTokens->points, HdInterpolationVertex, TfToken(), _computationId,
                _outputName, HdTupleType{HdTypeFloatVec3, 1})};
        }
        return {};
    }

    HdExtComputationOutputDescriptorVector GetExtComputationOutputDescriptors(
        const SdfPath& computationId) override
    {
        if (computationId == _computationId)
        {
            return {HdExtComputationOutputDescriptor(_outputName, HdTupleType{HdTypeFloatVec3, 1})};
        }
        return {};
    }

    VtValue GetExtComputationInput(const SdfPath& computationId, const TfToken& input) override
    {
        if (computationId == _computationId &&
            (input == HdTokens->elementCount || input == HdTokens->dispatchCount))
        {
            return VtValue(size_t{3});
        }
        return VtValue();
    }

    void InvokeExtComputation(const SdfPath& computationId, HdExtComputationContext* context) override
    {
        if (computationId != _computationId || context == nullptr)
        {
            return;
        }
        ++_computationInvocations;
        if (_computedFailure)
        {
            context->RaiseComputationError();
        }
        else
        {
            context->SetOutputValue(_outputName, _computedPoints);
        }
    }

    void SetAuthoredPoints(const VtValue& points)
    {
        _authoredPoints = points;
        _overridePoints = true;
    }

    void SetComputedPoints(const VtValue& points, bool failure = false)
    {
        _computedPoints = points;
        _computedFailure = failure;
    }

    bool EnableComputation(HdRenderParam* renderParam)
    {
        _computedEnabled = true;
        HdRenderIndex& index = GetRenderIndex();
        index.InsertSprim(HdPrimTypeTokens->extComputation, this, _computationId);
        HdSprim* computation = index.GetSprim(HdPrimTypeTokens->extComputation, _computationId);
        if (computation == nullptr)
        {
            return false;
        }
        HdDirtyBits dirty = computation->GetInitialDirtyBitsMask();
        computation->Sync(this, renderParam, &dirty);
        return true;
    }

    int ComputationInvocations() const { return _computationInvocations; }

private:
    const SdfPath _meshId;
    const SdfPath _computationId{"/TestComputation"};
    const TfToken _outputName{"computedPoints"};
    VtValue _authoredPoints;
    VtValue _computedPoints;
    bool _overridePoints = false;
    bool _computedEnabled = false;
    bool _computedFailure = false;
    int _computationInvocations = 0;
};

const VtVec3fArray& OriginalPoints()
{
    static const VtVec3fArray points{
        GfVec3f(0.0f, 0.0f, 0.0f), GfVec3f(1.0f, 0.0f, 0.0f), GfVec3f(0.0f, 1.0f, 0.0f)};
    return points;
}

void AddMesh(MeshPointDelegate* scene, const SdfPath& id)
{
    scene->AddMesh(
        id, GfMatrix4f(1.0f), OriginalPoints(), VtIntArray{3}, VtIntArray{0, 1, 2},
        false, SdfPath(), PxOsdOpenSubdivTokens->none);
}

HdDirtyBits SyncDirty(
    MeshPointDelegate* scene,
    HdSilkRenderDelegate* renderDelegate,
    HdRprim* rprim,
    const SdfPath& id,
    HdDirtyBits requested)
{
    HdChangeTracker& tracker = scene->GetRenderIndex().GetChangeTracker();
    tracker.MarkRprimClean(id, HdChangeTracker::Clean);
    tracker.MarkRprimDirty(id, requested);
    HdDirtyBits dirty = rprim->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    rprim->Sync(scene, renderDelegate->GetRenderParam(), &dirty, TfToken("hull"));
    return dirty;
}

struct SkelScopeGuard
{
    const HdSilkSceneState* state;
    ~SkelScopeGuard() { HdSilkEndUsdSkelEvaluation(state); }
};
}

extern "C" OPENUSD_HDSILK_API int32_t
openusd_hdsilk_test_mesh_invalid_points(int32_t invalidKind, int32_t bounded, int32_t topologyOnly)
{
    if (invalidKind < 0 || invalidKind > 3 || (bounded != 0 && bounded != 1) ||
        (topologyOnly != 0 && topologyOnly != 1))
    {
        return 1;
    }
    HdSilkRenderDelegate renderDelegate;
    std::unique_ptr<HdRenderIndex> index(HdRenderIndex::New(&renderDelegate, {}));
    if (!index)
    {
        return 2;
    }
    const SdfPath id("/Mesh");
    MeshPointDelegate scene(index.get(), id);
    AddMesh(&scene, id);
    HdRprim* mesh = const_cast<HdRprim*>(index->GetRprim(id));
    if (mesh == nullptr)
    {
        return 3;
    }
    const auto& state = renderDelegate.GetSceneState();
    const auto& budget = state->MeshPreparationBudget();
    if (bounded != 0)
    {
        budget->Configure(4096);
    }
    HdDirtyBits dirty = mesh->GetInitialDirtyBitsMask();
    mesh->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    uint32_t initialCommands = 0;
    const std::vector<uint8_t> initialPage = state->BuildPage(nullptr, &initialCommands);
    uint32_t quietCommands = 0;
    state->BuildPage(nullptr, &quietCommands);
    if (dirty != HdChangeTracker::Clean || budget->Refused() ||
        !state->HasMeshPath(id.GetString()) || initialCommands <= quietCommands ||
        !HdSilkTest::HasMeshPoints(initialPage, id.GetString(), OriginalPoints()))
    {
        return 4;
    }
    const uint64_t originalReservation = budget->Reserved();
    if (bounded != 0 && originalReservation == 0)
    {
        return 5;
    }

    if (invalidKind == 0)
    {
        scene.SetAuthoredPoints(VtValue());
    }
    else if (invalidKind == 1)
    {
        scene.SetAuthoredPoints(VtValue(1.0f));
    }
    else if (invalidKind == 2)
    {
        scene.SetAuthoredPoints(VtValue(VtVec3fArray()));
    }
    else
    {
        scene.SetAuthoredPoints(VtValue(VtVec3dArray()));
    }
    dirty = SyncDirty(
        &scene, &renderDelegate, mesh, id,
        topologyOnly != 0 ? HdChangeTracker::DirtyTopology : HdChangeTracker::DirtyPoints);
    uint32_t invalidCommands = 0;
    const std::vector<uint8_t> invalidPage = state->BuildPage(nullptr, &invalidCommands);
    if (dirty != HdChangeTracker::Clean ||
        !HdSilkTest::HasMeshPoints(initialPage, id.GetString(), OriginalPoints()))
    {
        return 6;
    }
    if (bounded != 0 && invalidKind < 2)
    {
        if (!budget->Refused() || !state->HasMeshPath(id.GetString()) ||
            budget->Reserved() != originalReservation || invalidCommands != quietCommands)
        {
            return 7;
        }
        try
        {
            budget->ThrowIfRefused();
            return 8;
        }
        catch (const HdSilkMeshPreparationExceeded& error)
        {
            if (std::string(error.what()).find("requires resolved float3 or double3 points") ==
                std::string::npos)
            {
                return 9;
            }
        }
        budget->Configure(4096);
    }
    else if (budget->Refused() || state->HasMeshPath(id.GetString()) ||
        invalidCommands <= quietCommands || !HdSilkTest::HasMeshRemoval(invalidPage, id.GetString()))
    {
        return 10;
    }

    const VtVec3dArray recovered{
        GfVec3d(0.0, 0.0, 0.0), GfVec3d(2.0, 0.0, 0.0), GfVec3d(0.0, 1.0, 0.0)};
    scene.SetAuthoredPoints(VtValue(recovered));
    dirty = SyncDirty(&scene, &renderDelegate, mesh, id, HdChangeTracker::DirtyPoints);
    uint32_t recoveredCommands = 0;
    const std::vector<uint8_t> recoveredPage = state->BuildPage(nullptr, &recoveredCommands);
    if (dirty != HdChangeTracker::Clean || budget->Refused() ||
        !state->HasMeshPath(id.GetString()) || recoveredCommands <= quietCommands ||
        (bounded != 0 && budget->Reserved() != originalReservation) ||
        !HdSilkTest::HasMeshPoints(
            recoveredPage, id.GetString(),
            VtVec3fArray{GfVec3f(0.0f, 0.0f, 0.0f), GfVec3f(2.0f, 0.0f, 0.0f),
                GfVec3f(0.0f, 1.0f, 0.0f)}))
    {
        return 11;
    }
    return 0;
}

extern "C" OPENUSD_HDSILK_API int32_t
openusd_hdsilk_test_mesh_computed_points(void)
{
    HdSilkRenderDelegate renderDelegate;
    std::unique_ptr<HdRenderIndex> index(HdRenderIndex::New(&renderDelegate, {}));
    if (!index)
    {
        return 1;
    }
    const SdfPath id("/Mesh");
    MeshPointDelegate scene(index.get(), id);
    AddMesh(&scene, id);
    const VtVec3fArray authored{
        GfVec3f(-1.0f, 0.0f, 0.0f), GfVec3f(1.0f, 0.0f, 0.0f), GfVec3f(0.0f, 1.0f, 0.0f)};
    scene.SetAuthoredPoints(VtValue(authored));
    scene.SetComputedPoints(VtValue(OriginalPoints()));
    if (!scene.EnableComputation(renderDelegate.GetRenderParam()))
    {
        return 2;
    }
    HdRprim* mesh = const_cast<HdRprim*>(index->GetRprim(id));
    if (mesh == nullptr)
    {
        return 3;
    }
    const auto& state = renderDelegate.GetSceneState();
    HdDirtyBits dirty = mesh->GetInitialDirtyBitsMask();
    mesh->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    uint32_t commands = 0;
    std::vector<uint8_t> page = state->BuildPage(nullptr, &commands);
    if (dirty != HdChangeTracker::Clean || scene.ComputationInvocations() != 1 ||
        !HdSilkTest::HasMeshPoints(page, id.GetString(), OriginalPoints()))
    {
        return 4;
    }

    const VtVec3fArray computedAfterTopology{
        GfVec3f(0.25f, 0.0f, 0.0f), GfVec3f(1.25f, 0.0f, 0.0f), GfVec3f(0.0f, 1.0f, 0.0f)};
    scene.SetComputedPoints(VtValue(computedAfterTopology));
    dirty = SyncDirty(&scene, &renderDelegate, mesh, id, HdChangeTracker::DirtyTopology);
    page = state->BuildPage(nullptr, &commands);
    if (dirty != HdChangeTracker::Clean || scene.ComputationInvocations() != 2 ||
        !HdSilkTest::HasMeshPoints(page, id.GetString(), computedAfterTopology))
    {
        return 5;
    }

    scene.SetAuthoredPoints(VtValue());
    scene.SetComputedPoints(VtValue(1.0f));
    dirty = SyncDirty(&scene, &renderDelegate, mesh, id, HdChangeTracker::DirtyNormals);
    page = state->BuildPage(nullptr, &commands);
    if (dirty != HdChangeTracker::Clean || scene.ComputationInvocations() != 2 ||
        !HdSilkTest::HasMeshPoints(page, id.GetString(), computedAfterTopology))
    {
        return 6;
    }

    scene.SetAuthoredPoints(VtValue(authored));
    scene.SetComputedPoints(VtValue(), true);
    dirty = SyncDirty(&scene, &renderDelegate, mesh, id, HdChangeTracker::DirtyPoints);
    page = state->BuildPage(nullptr, &commands);
    if (dirty != HdChangeTracker::Clean || scene.ComputationInvocations() != 3 ||
        !HdSilkTest::HasMeshPoints(page, id.GetString(), authored))
    {
        return 7;
    }

    scene.SetComputedPoints(VtValue(1.0f));
    dirty = SyncDirty(&scene, &renderDelegate, mesh, id, HdChangeTracker::DirtyPoints);
    page = state->BuildPage(nullptr, &commands);
    if (dirty != HdChangeTracker::Clean || scene.ComputationInvocations() != 4 ||
        !HdSilkTest::HasMeshPoints(page, id.GetString(), authored))
    {
        return 8;
    }

    scene.SetAuthoredPoints(VtValue());
    scene.SetComputedPoints(VtValue(1.0f));
    dirty = SyncDirty(&scene, &renderDelegate, mesh, id, HdChangeTracker::DirtyPoints);
    page = state->BuildPage(nullptr, &commands);
    if (dirty != HdChangeTracker::Clean || state->HasMeshPath(id.GetString()) ||
        !HdSilkTest::HasMeshRemoval(page, id.GetString()))
    {
        return 9;
    }

    scene.SetAuthoredPoints(VtValue(authored));
    scene.SetComputedPoints(VtValue(VtVec3fArray()));
    dirty = SyncDirty(&scene, &renderDelegate, mesh, id, HdChangeTracker::DirtyPoints);
    state->BuildPage(nullptr, &commands);
    if (dirty != HdChangeTracker::Clean || state->HasMeshPath(id.GetString()))
    {
        return 10;
    }

    const VtVec3dArray recovered{
        GfVec3d(2.0, 0.0, 0.0), GfVec3d(1.0, 0.0, 0.0), GfVec3d(0.0, 1.0, 0.0)};
    scene.SetComputedPoints(VtValue(recovered));
    dirty = SyncDirty(&scene, &renderDelegate, mesh, id, HdChangeTracker::DirtyPoints);
    page = state->BuildPage(nullptr, &commands);
    return dirty == HdChangeTracker::Clean &&
        HdSilkTest::HasMeshPoints(
            page, id.GetString(),
            VtVec3fArray{GfVec3f(2.0f, 0.0f, 0.0f), GfVec3f(1.0f, 0.0f, 0.0f),
                GfVec3f(0.0f, 1.0f, 0.0f)}) ? 0 : 11;
}

extern "C" OPENUSD_HDSILK_API int32_t
openusd_hdsilk_test_mesh_skel_normals_recovery(const char* stagePath, int32_t fallbackKind)
{
    if (stagePath == nullptr || fallbackKind < 0 || fallbackKind > 2)
    {
        return 1;
    }
    const UsdStageRefPtr stage = UsdStage::Open(stagePath);
    if (!stage)
    {
        return 2;
    }
    HdSilkRenderDelegate renderDelegate;
    std::unique_ptr<HdRenderIndex> index(HdRenderIndex::New(&renderDelegate, {}));
    if (!index)
    {
        return 3;
    }
    const SdfPath id("/World/Rig/InbetweenTriangle");
    MeshPointDelegate scene(index.get(), id);
    AddMesh(&scene, id);
    const VtVec3fArray fallback{
        GfVec3f(-1.0f, 0.0f, 0.0f), GfVec3f(1.0f, 0.0f, 0.0f), GfVec3f(0.0f, 1.0f, 0.0f)};
    scene.SetAuthoredPoints(fallbackKind == 2 ? VtValue() : VtValue(fallback));
    if (fallbackKind == 1)
    {
        scene.SetComputedPoints(VtValue(OriginalPoints()));
        if (!scene.EnableComputation(renderDelegate.GetRenderParam()))
        {
            return 4;
        }
    }
    HdRprim* mesh = const_cast<HdRprim*>(index->GetRprim(id));
    if (mesh == nullptr)
    {
        return 5;
    }
    const auto& state = renderDelegate.GetSceneState();
    const SkelScopeGuard guard{state.get()};
    HdSilkBeginUsdSkelEvaluation(state.get(), stage, UsdTimeCode(2.0));
    HdDirtyBits dirty = mesh->GetInitialDirtyBitsMask();
    mesh->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    uint32_t commands = 0;
    std::vector<uint8_t> page = state->BuildPage(nullptr, &commands);
    const VtVec3fArray deformed{
        GfVec3f(0.25f, 0.0f, 0.0f), GfVec3f(1.0f, 0.0f, 0.0f), GfVec3f(0.0f, 1.0f, 0.0f)};
    if (dirty != HdChangeTracker::Clean ||
        !HdSilkTest::HasMeshPoints(page, id.GetString(), deformed) ||
        scene.ComputationInvocations() != 0)
    {
        return 6;
    }

    HdSilkEndUsdSkelEvaluation(state.get());
    dirty = SyncDirty(&scene, &renderDelegate, mesh, id, HdChangeTracker::DirtyNormals);
    page = state->BuildPage(nullptr, &commands);
    if (dirty != HdChangeTracker::Clean)
    {
        return 7;
    }
    if (fallbackKind == 2)
    {
        if (state->HasMeshPath(id.GetString()) || !HdSilkTest::HasMeshRemoval(page, id.GetString()))
        {
            return 8;
        }
    }
    else if (!HdSilkTest::HasMeshPoints(
            page, id.GetString(), fallbackKind == 1 ? OriginalPoints() : fallback) ||
        scene.ComputationInvocations() != (fallbackKind == 1 ? 1 : 0))
    {
        return 9;
    }

    HdSilkBeginUsdSkelEvaluation(state.get(), stage, UsdTimeCode(2.0));
    dirty = SyncDirty(&scene, &renderDelegate, mesh, id, HdChangeTracker::DirtyNormals);
    page = state->BuildPage(nullptr, &commands);
    return dirty == HdChangeTracker::Clean &&
        HdSilkTest::HasMeshPoints(page, id.GetString(), deformed) ? 0 : 10;
}
