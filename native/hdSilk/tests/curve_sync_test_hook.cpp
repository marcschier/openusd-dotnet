// Copyright (c) marcschier. Licensed under the MIT License.

#include "hdsilk_test_hooks.h"

#include "geometry_sync_page.h"
#include "renderDelegate.h"

#include "pxr/base/gf/vec3d.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/vt/array.h"
#include "pxr/base/vt/value.h"
#include "pxr/imaging/hd/changeTracker.h"
#include "pxr/imaging/hd/renderIndex.h"
#include "pxr/imaging/hd/rprim.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/imaging/hd/unitTestDelegate.h"

#include <cstdio>
#include <memory>
#include <string>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
class CurvePointOverrideDelegate final : public HdUnitTestDelegate
{
public:
    using HdUnitTestDelegate::HdUnitTestDelegate;

    VtValue Get(const SdfPath& id, const TfToken& key) override
    {
        if (_overridePoints && id == SdfPath("/Curve") && key == HdTokens->points)
        {
            return _points;
        }
        return HdUnitTestDelegate::Get(id, key);
    }

    void SetPoints(const VtValue& points)
    {
        _points = points;
        _overridePoints = true;
    }

private:
    VtValue _points;
    bool _overridePoints = false;
};
}

extern "C" OPENUSD_HDSILK_API int32_t
openusd_hdsilk_test_curve_non_geometry_dirty(int32_t dirtyKind, int32_t exactLimit)
{
    HdSilkRenderDelegate renderDelegate;
    std::unique_ptr<HdRenderIndex> index(HdRenderIndex::New(&renderDelegate, {}));
    if (!index)
    {
        return 1;
    }

    HdUnitTestDelegate scene(index.get(), SdfPath::AbsoluteRootPath());
    const SdfPath id("/Curve");
    VtVec3fArray points(2);
    points[0] = GfVec3f(0.0f, 0.0f, 0.0f);
    points[1] = GfVec3f(1.0f, 0.0f, 0.0f);
    VtIntArray counts(1);
    counts[0] = 2;
    VtFloatArray widths(1);
    widths[0] = 0.1f;
    scene.AddBasisCurves(
        id,
        points,
        counts,
        VtIntArray(),
        VtVec3fArray(),
        HdTokens->linear,
        HdTokens->bezier,
        VtValue(GfVec3f(1.0f, 1.0f, 1.0f)),
        HdInterpolationConstant,
        VtValue(1.0f),
        HdInterpolationConstant,
        VtValue(widths),
        HdInterpolationConstant);
    scene.SetCurveWrapMode(id, HdTokens->nonperiodic);

    HdRprim* curve = const_cast<HdRprim*>(index->GetRprim(id));
    if (curve == nullptr)
    {
        return 1;
    }
    const auto& state = renderDelegate.GetSceneState();
    const auto& budget = state->MeshPreparationBudget();
    constexpr uint64_t expected = 104;
    budget->Configure(expected * 4);
    HdDirtyBits dirty = curve->GetInitialDirtyBitsMask();
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    if (budget->Refused() || budget->Reserved() != expected ||
        dirty != HdChangeTracker::Clean || !state->HasMeshPath(id.GetString()))
    {
        return 2;
    }

    uint32_t initialCommands = 0;
    const std::vector<uint8_t> initialPage = state->BuildPage(nullptr, &initialCommands);
    uint32_t quietCommands = 0;
    state->BuildPage(nullptr, &quietCommands);
    if (initialCommands <= quietCommands ||
        !HdSilkTest::HasMeshPoints(initialPage, id.GetString(), points))
    {
        return 6;
    }

    budget->Configure(exactLimit != 0 ? expected : expected * 3);
    if (budget->Reserved() != expected || budget->Peak() != expected)
    {
        return 7;
    }
    HdChangeTracker& tracker = index->GetChangeTracker();
    tracker.MarkRprimClean(id, HdChangeTracker::Clean);
    const HdDirtyBits requested =
        dirtyKind == 0 ? HdChangeTracker::DirtyMaterialId : HdChangeTracker::DirtyExtent;
    tracker.MarkRprimDirty(id, requested);
    dirty = curve->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    if ((dirty & requested) == 0)
    {
        return 9;
    }
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    if (budget->Refused())
    {
        std::fprintf(stderr, "Curve budget refused: reserved=%llu peak=%llu limit=%llu.\n",
            static_cast<unsigned long long>(budget->Reserved()),
            static_cast<unsigned long long>(budget->Peak()),
            static_cast<unsigned long long>(budget->Limit()));
        return 3;
    }
    uint32_t afterCommands = 0;
    state->BuildPage(nullptr, &afterCommands);
    if (afterCommands != quietCommands)
    {
        return 8;
    }
    if (budget->Reserved() != expected || budget->Peak() != expected)
    {
        std::fprintf(stderr, "Curve budget changed: reserved=%llu peak=%llu expected=%llu.\n",
            static_cast<unsigned long long>(budget->Reserved()),
            static_cast<unsigned long long>(budget->Peak()),
            static_cast<unsigned long long>(expected));
        return 4;
    }
    if (dirty != HdChangeTracker::Clean || !state->HasMeshPath(id.GetString()))
    {
        return 5;
    }

    tracker.MarkRprimClean(id, dirty);
    budget->Configure(expected);
    scene.SetCurveWrapMode(id, HdTokens->segmented);
    dirty = curve->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    if ((dirty & HdChangeTracker::DirtyTopology) == 0)
    {
        return 10;
    }
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    uint32_t refusedCommands = 0;
    state->BuildPage(nullptr, &refusedCommands);
    if (!budget->Refused() || budget->Reserved() != expected ||
        refusedCommands != quietCommands || !state->HasMeshPath(id.GetString()))
    {
        return 11;
    }

    tracker.MarkRprimClean(id, HdChangeTracker::Clean);
    budget->Configure(expected * 3);
    tracker.MarkRprimDirty(id, HdChangeTracker::DirtyTopology);
    dirty = curve->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    uint32_t retriedCommands = 0;
    state->BuildPage(nullptr, &retriedCommands);
    if (budget->Refused() || dirty != HdChangeTracker::Clean ||
        retriedCommands <= quietCommands || budget->Reserved() != expected ||
        budget->Peak() != expected * 2 || !state->HasMeshPath(id.GetString()))
    {
        return 12;
    }

    tracker.MarkRprimClean(id, dirty);
    scene.SetVisibility(id, false);
    dirty = curve->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    if (budget->Refused() || dirty != HdChangeTracker::Clean ||
        budget->Reserved() != expected || state->HasMeshPath(id.GetString()))
    {
        return 13;
    }
    uint32_t hiddenCommands = 0;
    state->BuildPage(nullptr, &hiddenCommands);
    if (hiddenCommands <= quietCommands)
    {
        return 14;
    }

    tracker.MarkRprimClean(id, dirty);
    scene.SetVisibility(id, true);
    dirty = curve->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    if (budget->Refused() || dirty != HdChangeTracker::Clean ||
        budget->Reserved() != expected || !state->HasMeshPath(id.GetString()))
    {
        return 15;
    }
    uint32_t visibleCommands = 0;
    state->BuildPage(nullptr, &visibleCommands);
    return visibleCommands > quietCommands ? 0 : 16;
}

extern "C" OPENUSD_HDSILK_API int32_t
openusd_hdsilk_test_curve_invalid_points(int32_t invalidKind, int32_t bounded)
{
    HdSilkRenderDelegate renderDelegate;
    std::unique_ptr<HdRenderIndex> index(HdRenderIndex::New(&renderDelegate, {}));
    if (!index || invalidKind < 0 || invalidKind > 2 || (bounded != 0 && bounded != 1))
    {
        return 1;
    }

    CurvePointOverrideDelegate scene(index.get(), SdfPath::AbsoluteRootPath());
    const SdfPath id("/Curve");
    const VtVec3fArray points{GfVec3f(0.0f, 0.0f, 0.0f), GfVec3f(1.0f, 0.0f, 0.0f)};
    scene.AddBasisCurves(
        id, points, VtIntArray{2}, VtIntArray(), VtVec3fArray(),
        HdTokens->linear, HdTokens->bezier, VtValue(GfVec3f(1.0f, 1.0f, 1.0f)),
        HdInterpolationConstant, VtValue(1.0f), HdInterpolationConstant,
        VtValue(VtFloatArray{0.1f}), HdInterpolationConstant);
    scene.SetCurveWrapMode(id, HdTokens->nonperiodic);

    HdRprim* curve = const_cast<HdRprim*>(index->GetRprim(id));
    if (curve == nullptr)
    {
        return 2;
    }
    const auto& state = renderDelegate.GetSceneState();
    const auto& budget = state->MeshPreparationBudget();
    if (bounded != 0)
    {
        budget->Configure(1024);
    }
    HdDirtyBits dirty = curve->GetInitialDirtyBitsMask();
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    if (dirty != HdChangeTracker::Clean || budget->Refused() ||
        !state->HasMeshPath(id.GetString()))
    {
        return 3;
    }
    uint32_t initialCommands = 0;
    const std::vector<uint8_t> initialPage = state->BuildPage(nullptr, &initialCommands);
    uint32_t quietCommands = 0;
    state->BuildPage(nullptr, &quietCommands);
    if (initialCommands <= quietCommands ||
        !HdSilkTest::HasMeshPoints(initialPage, id.GetString(), points))
    {
        return 4;
    }
    const uint64_t originalReservation = budget->Reserved();

    if (invalidKind == 0)
    {
        scene.SetPoints(VtValue());
    }
    else if (invalidKind == 1)
    {
        scene.SetPoints(VtValue(1.0f));
    }
    else
    {
        scene.SetPoints(VtValue(VtVec3fArray()));
    }
    HdChangeTracker& tracker = index->GetChangeTracker();
    const auto syncPoints = [&]
    {
        tracker.MarkRprimClean(id, HdChangeTracker::Clean);
        tracker.MarkRprimDirty(id, HdChangeTracker::DirtyPoints);
        dirty = curve->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
        curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    };
    syncPoints();
    uint32_t invalidCommands = 0;
    const std::vector<uint8_t> invalidPage = state->BuildPage(nullptr, &invalidCommands);
    const bool mustRefuse = bounded != 0 && invalidKind != 2;
    if (dirty != HdChangeTracker::Clean)
    {
        return 5;
    }
    if (mustRefuse)
    {
        if (!budget->Refused() || !state->HasMeshPath(id.GetString()) ||
            budget->Reserved() != originalReservation || invalidCommands != quietCommands)
        {
            return 6;
        }
        try
        {
            budget->ThrowIfRefused();
            return 7;
        }
        catch (const HdSilkMeshPreparationExceeded& error)
        {
            if (std::string(error.what()).find("requires resolved float3 or double3 points") ==
                std::string::npos)
            {
                return 8;
            }
        }
        budget->Configure(1024);
    }
    else if (budget->Refused() || state->HasMeshPath(id.GetString()) ||
        invalidCommands <= quietCommands || !HdSilkTest::HasMeshRemoval(invalidPage, id.GetString()))
    {
        return 9;
    }

    const VtVec3dArray recovered{
        GfVec3d(0.0, 0.0, 0.0), GfVec3d(2.0, 0.0, 0.0)};
    scene.SetPoints(VtValue(recovered));
    syncPoints();
    uint32_t recoveredCommands = 0;
    const std::vector<uint8_t> recoveredPage = state->BuildPage(nullptr, &recoveredCommands);
    if (dirty != HdChangeTracker::Clean || budget->Refused() ||
        !state->HasMeshPath(id.GetString()) || recoveredCommands <= quietCommands ||
        !HdSilkTest::HasMeshPoints(
            recoveredPage, id.GetString(),
            VtVec3fArray{GfVec3f(0.0f, 0.0f, 0.0f), GfVec3f(2.0f, 0.0f, 0.0f)}))
    {
        return 10;
    }
    return 0;
}
