// Copyright (c) marcschier. Licensed under the MIT License.

#include "hdsilk_test_hooks.h"

#include "renderDelegate.h"

#include "pxr/base/gf/matrix4f.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/vt/array.h"
#include "pxr/base/vt/value.h"
#include "pxr/imaging/hd/changeTracker.h"
#include "pxr/imaging/hd/renderIndex.h"
#include "pxr/imaging/hd/rprim.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/imaging/hd/unitTestDelegate.h"
#include "pxr/imaging/pxOsd/tokens.h"

#include <cstdio>
#include <memory>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
enum class GeometryKind
{
    Points,
    Mesh
};

int32_t
TestNonGeometryDirty(GeometryKind kind, int32_t dirtyKind, int32_t exactLimit)
{
    if (dirtyKind != 0 && dirtyKind != 1)
    {
        return 1;
    }

    HdSilkRenderDelegate renderDelegate;
    std::unique_ptr<HdRenderIndex> index(HdRenderIndex::New(&renderDelegate, {}));
    if (!index)
    {
        return 1;
    }

    HdUnitTestDelegate scene(index.get(), SdfPath::AbsoluteRootPath());
    const SdfPath id(kind == GeometryKind::Points ? "/Points" : "/Mesh");
    VtVec3fArray points(kind == GeometryKind::Points ? 2 : 3);
    points[0] = GfVec3f(0.0f, 0.0f, 0.0f);
    points[1] = GfVec3f(1.0f, 0.0f, 0.0f);
    if (kind == GeometryKind::Points)
    {
        scene.AddPoints(
            id,
            points,
            VtValue(GfVec3f(1.0f, 1.0f, 1.0f)),
            HdInterpolationConstant,
            VtValue(1.0f),
            HdInterpolationConstant,
            VtValue(1.0f),
            HdInterpolationConstant);
    }
    else
    {
        points[2] = GfVec3f(0.0f, 1.0f, 0.0f);
        VtIntArray counts(1);
        counts[0] = 3;
        VtIntArray indices(3);
        indices[0] = 0;
        indices[1] = 1;
        indices[2] = 2;
        scene.AddMesh(
            id, GfMatrix4f(1.0f), points, counts, indices, false, SdfPath(),
            PxOsdOpenSubdivTokens->none);
    }

    HdRprim* rprim = const_cast<HdRprim*>(index->GetRprim(id));
    if (rprim == nullptr)
    {
        return 1;
    }
    const auto& state = renderDelegate.GetSceneState();
    const auto& budget = state->MeshPreparationBudget();
    const uint64_t expected = kind == GeometryKind::Points ? 96ull : 267ull;
    budget->Configure(expected * 4);
    HdDirtyBits dirty = rprim->GetInitialDirtyBitsMask();
    rprim->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    if (budget->Refused() || budget->Reserved() != expected ||
        dirty != HdChangeTracker::Clean || !state->HasMeshPath(id.GetString()))
    {
        std::fprintf(stderr, "Initial geometry sync: refused=%d reserved=%llu expected=%llu dirty=%u published=%d.\n",
            budget->Refused(),
            static_cast<unsigned long long>(budget->Reserved()),
            static_cast<unsigned long long>(expected),
            static_cast<unsigned int>(dirty),
            state->HasMeshPath(id.GetString()));
        return 2;
    }

    uint32_t initialCommands = 0;
    state->BuildPage(nullptr, &initialCommands);
    uint32_t quietCommands = 0;
    state->BuildPage(nullptr, &quietCommands);
    if (initialCommands <= quietCommands)
    {
        return 3;
    }

    HdChangeTracker& tracker = index->GetChangeTracker();
    tracker.MarkRprimClean(id, HdChangeTracker::Clean);
    const HdDirtyBits requested = kind == GeometryKind::Points
        ? (dirtyKind == 0 ? HdChangeTracker::DirtyMaterialId : HdChangeTracker::DirtyExtent)
        : (dirtyKind == 0 ? HdChangeTracker::DirtyExtent : HdChangeTracker::DirtyRenderTag);
    tracker.MarkRprimDirty(id, requested);
    dirty = rprim->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    if ((dirty & requested) == 0)
    {
        return 4;
    }

    budget->Configure(exactLimit != 0 ? expected : expected * 3);
    if (budget->Reserved() != expected || budget->Peak() != expected)
    {
        return 5;
    }
    rprim->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    if (budget->Refused())
    {
        std::fprintf(stderr, "Geometry budget refused: reserved=%llu peak=%llu limit=%llu.\n",
            static_cast<unsigned long long>(budget->Reserved()),
            static_cast<unsigned long long>(budget->Peak()),
            static_cast<unsigned long long>(budget->Limit()));
        return 6;
    }
    uint32_t afterCommands = 0;
    state->BuildPage(nullptr, &afterCommands);
    if (afterCommands != quietCommands || dirty != HdChangeTracker::Clean ||
        !state->HasMeshPath(id.GetString()))
    {
        return 7;
    }
    if (budget->Reserved() != expected || budget->Peak() != expected)
    {
        std::fprintf(stderr, "Geometry budget changed: reserved=%llu peak=%llu expected=%llu.\n",
            static_cast<unsigned long long>(budget->Reserved()),
            static_cast<unsigned long long>(budget->Peak()),
            static_cast<unsigned long long>(expected));
        return 8;
    }

    tracker.MarkRprimClean(id, dirty);
    budget->Configure(expected);
    HdDirtyBits control = HdChangeTracker::DirtyPrimvar;
    if (kind == GeometryKind::Points)
    {
        scene.UpdatePrimvarValue(id, HdTokens->displayColor, VtValue(GfVec3f(1.0f, 0.0f, 0.0f)));
    }
    else
    {
        scene.SetMeshCullStyle(id, HdCullStyleBack);
        control = HdChangeTracker::DirtyCullStyle;
    }
    dirty = rprim->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    if ((dirty & control) == 0)
    {
        return 9;
    }
    rprim->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    uint32_t refusedCommands = 0;
    state->BuildPage(nullptr, &refusedCommands);
    if (!budget->Refused() || budget->Reserved() != expected ||
        refusedCommands != quietCommands || !state->HasMeshPath(id.GetString()))
    {
        std::fprintf(stderr,
            "Geometry update refusal: refused=%d reserved=%llu expected=%llu commands=%u quiet=%u.\n",
            budget->Refused(),
            static_cast<unsigned long long>(budget->Reserved()),
            static_cast<unsigned long long>(expected),
            refusedCommands, quietCommands);
        return 10;
    }

    tracker.MarkRprimClean(id, HdChangeTracker::Clean);
    budget->Configure(expected * 3);
    tracker.MarkRprimDirty(id, control);
    dirty = rprim->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    rprim->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    uint32_t retriedCommands = 0;
    state->BuildPage(nullptr, &retriedCommands);
    if (budget->Refused() || dirty != HdChangeTracker::Clean ||
        retriedCommands <= quietCommands || budget->Reserved() != expected ||
        budget->Peak() != expected * 2 || !state->HasMeshPath(id.GetString()))
    {
        std::fprintf(stderr,
            "Geometry update retry: refused=%d reserved=%llu peak=%llu expected=%llu commands=%u quiet=%u.\n",
            budget->Refused(),
            static_cast<unsigned long long>(budget->Reserved()),
            static_cast<unsigned long long>(budget->Peak()),
            static_cast<unsigned long long>(expected),
            retriedCommands, quietCommands);
        return 11;
    }

    if (kind == GeometryKind::Mesh)
    {
        uint32_t controlQuietCommands = 0;
        state->BuildPage(nullptr, &controlQuietCommands);
        tracker.MarkRprimClean(id, dirty);
        state->NoteMaterialBindingPurposeChanged();
        budget->Configure(expected);
        tracker.MarkRprimDirty(id, HdChangeTracker::DirtyExtent);
        dirty = rprim->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
        rprim->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
        uint32_t refusedBindingCommands = 0;
        state->BuildPage(nullptr, &refusedBindingCommands);
        if (!budget->Refused() || budget->Reserved() != expected ||
            refusedBindingCommands != controlQuietCommands || !state->HasMeshPath(id.GetString()))
        {
            std::fprintf(stderr,
                "Material generation refusal: refused=%d reserved=%llu commands=%u quiet=%u.\n",
                budget->Refused(),
                static_cast<unsigned long long>(budget->Reserved()),
                refusedBindingCommands, controlQuietCommands);
            return 12;
        }

        tracker.MarkRprimClean(id, HdChangeTracker::Clean);
        budget->Configure(expected * 3);
        tracker.MarkRprimDirty(id, HdChangeTracker::DirtyExtent);
        dirty = rprim->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
        rprim->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
        uint32_t retriedBindingCommands = 0;
        state->BuildPage(nullptr, &retriedBindingCommands);
        if (budget->Refused() || dirty != HdChangeTracker::Clean ||
            retriedBindingCommands <= controlQuietCommands ||
            budget->Reserved() != expected || budget->Peak() != expected * 2)
        {
            std::fprintf(stderr,
                "Material generation retry: refused=%d reserved=%llu peak=%llu commands=%u quiet=%u.\n",
                budget->Refused(),
                static_cast<unsigned long long>(budget->Reserved()),
                static_cast<unsigned long long>(budget->Peak()),
                retriedBindingCommands, controlQuietCommands);
            return 13;
        }
    }
    return 0;
}
}

extern "C" OPENUSD_HDSILK_API int32_t
openusd_hdsilk_test_point_non_geometry_dirty(int32_t dirtyKind, int32_t exactLimit)
{
    return TestNonGeometryDirty(GeometryKind::Points, dirtyKind, exactLimit);
}

extern "C" OPENUSD_HDSILK_API int32_t
openusd_hdsilk_test_mesh_non_geometry_dirty(int32_t dirtyKind, int32_t exactLimit)
{
    return TestNonGeometryDirty(GeometryKind::Mesh, dirtyKind, exactLimit);
}
