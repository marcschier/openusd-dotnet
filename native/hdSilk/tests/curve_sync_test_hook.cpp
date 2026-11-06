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
#include <cstring>
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

class CubicTopologyDelegate final : public HdUnitTestDelegate
{
public:
    using HdUnitTestDelegate::HdUnitTestDelegate;

    HdBasisCurvesTopology GetBasisCurvesTopology(const SdfPath& id) override
    {
        HdBasisCurvesTopology topology = HdUnitTestDelegate::GetBasisCurvesTopology(id);
        if (_hideCurve)
        {
            topology.SetInvisibleCurves(VtIntArray{0});
        }
        if (_hidePoint)
        {
            topology.SetInvisiblePoints(VtIntArray{1});
        }
        return topology;
    }

    SdfPath GetMaterialId(const SdfPath& id) override
    {
        return _boundMaterial ? SdfPath("/Material") : HdUnitTestDelegate::GetMaterialId(id);
    }

    void SetHiddenPoint(bool hidden) { _hidePoint = hidden; }
    void SetHiddenCurve(bool hidden) { _hideCurve = hidden; }
    void SetBoundMaterial(bool bound) { _boundMaterial = bound; }

private:
    bool _hideCurve = true;
    bool _hidePoint = false;
    bool _boundMaterial = false;
};

bool HasCubicData(
    const std::vector<uint8_t>& page,
    const SdfPath& id,
    const VtVec3fArray& expected,
    const VtIntArray& expectedSubprims)
{
    if (!HdSilkTest::HasMeshPoints(page, id.GetString(), expected))
    {
        return false;
    }

    size_t offset = 0;
    uint32_t size = 0;
    uint32_t pathLength = 0;
    uint32_t pointCount = 0;
    uint32_t indexCount = 0;
    uint32_t primitiveCount = 0;
    uint32_t materialLength = 0;
    uint32_t attributeCount = 0;
    if (!HdSilkTest::FindMeshCommand(
            page, OPENUSD_SILK_COMMAND_MESH_UPSERT, id.GetString(), &offset, &size) ||
        !HdSilkTest::ReadPageValue(page, offset + 48, &pathLength) ||
        !HdSilkTest::ReadPageValue(page, offset + 52, &pointCount) ||
        !HdSilkTest::ReadPageValue(page, offset + 56, &indexCount) ||
        !HdSilkTest::ReadPageValue(page, offset + 60, &primitiveCount) ||
        !HdSilkTest::ReadPageValue(page, offset + 216, &materialLength) ||
        !HdSilkTest::ReadPageValue(page, offset + 220, &attributeCount) ||
        pointCount != expected.size() || indexCount != expected.size() ||
        primitiveCount != expectedSubprims.size())
    {
        return false;
    }
    const size_t indexOffset = offset + 276 + pathLength +
        static_cast<size_t>(pointCount) * 3 * sizeof(float);
    for (size_t index = 0; index < indexCount; ++index)
    {
        uint32_t value = 0;
        if (!HdSilkTest::ReadPageValue(
                page, indexOffset + index * sizeof(uint32_t), &value) ||
            value != index)
        {
            return false;
        }
    }
    const size_t subprims = indexOffset +
        static_cast<size_t>(indexCount) * sizeof(uint32_t);
    for (size_t primitive = 0; primitive < primitiveCount; ++primitive)
    {
        uint32_t subprim = 0;
        if (!HdSilkTest::ReadPageValue(
                page, subprims + primitive * sizeof(uint32_t), &subprim) ||
            subprim != static_cast<uint32_t>(expectedSubprims[primitive]))
        {
            return false;
        }
    }

    size_t attributeOffset = subprims +
        static_cast<size_t>(primitiveCount) * sizeof(uint32_t) + materialLength;
    const size_t end = offset + size;
    bool foundWidths = false;
    for (uint32_t attribute = 0; attribute < attributeCount; ++attribute)
    {
        uint32_t semantic = 0;
        uint32_t components = 0;
        uint32_t interpolation = 0;
        uint32_t nameLength = 0;
        uint32_t elements = 0;
        if (attributeOffset > end || end - attributeOffset < 20 ||
            !HdSilkTest::ReadPageValue(page, attributeOffset, &semantic) ||
            !HdSilkTest::ReadPageValue(page, attributeOffset + 4, &components) ||
            !HdSilkTest::ReadPageValue(page, attributeOffset + 8, &interpolation) ||
            !HdSilkTest::ReadPageValue(page, attributeOffset + 12, &nameLength) ||
            !HdSilkTest::ReadPageValue(page, attributeOffset + 16, &elements))
        {
            return false;
        }
        attributeOffset += 20;
        const uint64_t bytes = static_cast<uint64_t>(components) * elements * sizeof(float);
        if (nameLength > end - attributeOffset ||
            bytes > end - attributeOffset - nameLength)
        {
            return false;
        }
        const size_t values = attributeOffset + nameLength;
        if (semantic == OPENUSD_SILK_ATTRIBUTE_WIDTH)
        {
            if (foundWidths || components != 1 || elements != expected.size() ||
                interpolation != OPENUSD_SILK_INTERPOLATION_VERTEX ||
                nameLength != 6 ||
                std::memcmp(page.data() + attributeOffset, "widths", 6) != 0)
            {
                return false;
            }
            foundWidths = true;
            for (size_t point = 0; point < expected.size(); ++point)
            {
                float width = 0;
                if (!HdSilkTest::ReadPageValue(
                        page, values + point * sizeof(float), &width) ||
                    width != expected[point][0] * 0.25f)
                {
                    return false;
                }
            }
        }
        attributeOffset = values + static_cast<size_t>(bytes);
    }
    return foundWidths;
}

bool HasIndexedCubicData(
    const std::vector<uint8_t>& page,
    const SdfPath& id)
{
    VtVec3fArray expected;
    for (size_t chord = 0; chord < 8; ++chord)
    {
        for (size_t endpoint = 0; endpoint < 2; ++endpoint)
        {
            const float t = static_cast<float>(chord + endpoint) / 8.0f;
            expected.push_back(GfVec3f(3.0f - 3.0f * t, 0.0f, 0.0f));
        }
    }
    return HasCubicData(page, id, expected, VtIntArray{2, 2, 2, 2, 2, 2, 2, 2});
}

int32_t VerifyIndexedCubicSync()
{
    HdSilkRenderDelegate renderDelegate;
    std::unique_ptr<HdRenderIndex> index(HdRenderIndex::New(&renderDelegate, {}));
    if (!index)
    {
        return 17;
    }
    CubicTopologyDelegate scene(index.get(), SdfPath::AbsoluteRootPath());
    const SdfPath id("/Curve");
    VtVec3fArray points;
    VtFloatArray widths;
    VtIntArray indices;
    for (int point = 0; point < 11; ++point)
    {
        points.push_back(GfVec3f(static_cast<float>(point), 0.0f, 0.0f));
        widths.push_back(static_cast<float>(point) * 0.25f);
        indices.push_back(10 - point);
    }
    scene.AddBasisCurves(
        id, points, VtIntArray{7, 4}, indices, VtVec3fArray(),
        HdTokens->cubic, HdTokens->bezier, VtValue(GfVec3f(1.0f)),
        HdInterpolationConstant, VtValue(1.0f), HdInterpolationConstant,
        VtValue(widths), HdInterpolationVertex);
    scene.SetCurveWrapMode(id, HdTokens->nonperiodic);
    HdRprim* curve = const_cast<HdRprim*>(index->GetRprim(id));
    if (curve == nullptr)
    {
        return 18;
    }
    const auto& state = renderDelegate.GetSceneState();
    const auto& budget = state->MeshPreparationBudget();
    constexpr uint64_t expected = 11 * 12 + 11 * 4 + 3 * 8 * 76;
    budget->Configure(expected * 2);
    HdDirtyBits dirty = curve->GetInitialDirtyBitsMask();
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    uint32_t firstCommands = 0;
    const std::vector<uint8_t> firstPage = state->BuildPage(nullptr, &firstCommands);
    uint32_t quietCommands = 0;
    state->BuildPage(nullptr, &quietCommands);
    if (dirty != HdChangeTracker::Clean || budget->Refused() ||
        budget->Reserved() != expected || firstCommands <= quietCommands ||
        !HasIndexedCubicData(firstPage, id))
    {
        return 19;
    }

    HdChangeTracker& tracker = index->GetChangeTracker();
    tracker.MarkRprimClean(id, HdChangeTracker::Clean);
    scene.SetHiddenPoint(true);
    tracker.MarkRprimDirty(id, HdChangeTracker::DirtyTopology);
    dirty = curve->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    uint32_t refusedCommands = 0;
    state->BuildPage(nullptr, &refusedCommands);
    if (!budget->Refused() || budget->Reserved() != expected ||
        !state->HasMeshPath(id.GetString()) || refusedCommands != quietCommands)
    {
        return 20;
    }
    try
    {
        budget->ThrowIfRefused();
        return 21;
    }
    catch (const HdSilkMeshPreparationExceeded& error)
    {
        if (std::string(error.what()).find("without invisible control points") == std::string::npos)
        {
            return 22;
        }
    }

    scene.SetHiddenPoint(false);
    budget->Configure(expected * 2);
    tracker.MarkRprimClean(id, HdChangeTracker::Clean);
    tracker.MarkRprimDirty(id, HdChangeTracker::DirtyTopology);
    dirty = curve->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    uint32_t retryCommands = 0;
    const std::vector<uint8_t> retryPage = state->BuildPage(nullptr, &retryCommands);
    if (budget->Refused() || budget->Reserved() != expected ||
        budget->Peak() != expected * 2 || retryCommands <= quietCommands ||
        !HasIndexedCubicData(retryPage, id))
    {
        return 23;
    }

    scene.SetBoundMaterial(true);
    tracker.MarkRprimClean(id, HdChangeTracker::Clean);
    tracker.MarkRprimDirty(id, HdChangeTracker::DirtyMaterialId);
    dirty = curve->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    uint32_t materialCommands = 0;
    state->BuildPage(nullptr, &materialCommands);
    if (!budget->Refused() || budget->Reserved() != expected ||
        !state->HasMeshPath(id.GetString()) || materialCommands != quietCommands)
    {
        return 31;
    }
    try
    {
        budget->ThrowIfRefused();
        return 32;
    }
    catch (const HdSilkMeshPreparationExceeded& error)
    {
        if (std::string(error.what()).find("cubic Bezier material bindings") == std::string::npos)
        {
            return 33;
        }
    }
    scene.SetBoundMaterial(false);
    budget->Configure(expected * 2);
    tracker.MarkRprimClean(id, HdChangeTracker::Clean);
    tracker.MarkRprimDirty(id, HdChangeTracker::DirtyMaterialId);
    dirty = curve->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    uint32_t restoredCommands = 0;
    const std::vector<uint8_t> restoredPage = state->BuildPage(nullptr, &restoredCommands);
    if (budget->Refused() || budget->Reserved() != expected ||
        restoredCommands <= quietCommands || !HasIndexedCubicData(restoredPage, id))
    {
        return 34;
    }

    tracker.MarkRprimClean(id, HdChangeTracker::Clean);
    scene.SetVisibility(id, false);
    dirty = curve->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    if (budget->Refused() || state->HasMeshPath(id.GetString()) ||
        budget->Reserved() != expected)
    {
        return 24;
    }
    index->RemoveRprim(id);
    return budget->Reserved() == 0 ? 0 : 25;
}

int32_t VerifyIndexedVaryingWidthRefusal()
{
    HdSilkRenderDelegate renderDelegate;
    std::unique_ptr<HdRenderIndex> index(HdRenderIndex::New(&renderDelegate, {}));
    if (!index)
    {
        return 26;
    }
    HdUnitTestDelegate scene(index.get(), SdfPath::AbsoluteRootPath());
    const SdfPath id("/Curve");
    scene.AddBasisCurves(
        id,
        VtVec3fArray{
            GfVec3f(0.0f), GfVec3f(1.0f, 0.0f, 0.0f),
            GfVec3f(2.0f, 0.0f, 0.0f), GfVec3f(3.0f, 0.0f, 0.0f)},
        VtIntArray{4}, VtIntArray{3, 2, 1, 0}, VtVec3fArray(),
        HdTokens->cubic, HdTokens->bezier, VtValue(GfVec3f(1.0f)),
        HdInterpolationConstant, VtValue(1.0f), HdInterpolationConstant,
        VtValue(VtFloatArray{0.25f, 0.75f}), HdInterpolationVarying);
    scene.SetCurveWrapMode(id, HdTokens->nonperiodic);
    HdRprim* curve = const_cast<HdRprim*>(index->GetRprim(id));
    if (curve == nullptr)
    {
        return 27;
    }
    const auto& state = renderDelegate.GetSceneState();
    const auto& budget = state->MeshPreparationBudget();
    budget->Configure(4096);
    HdDirtyBits dirty = curve->GetInitialDirtyBitsMask();
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    if (!budget->Refused() || budget->Reserved() != 0 ||
        state->HasMeshPath(id.GetString()))
    {
        return 28;
    }
    try
    {
        budget->ThrowIfRefused();
    }
    catch (const HdSilkMeshPreparationExceeded& error)
    {
        return std::string(error.what()).find(
            "indexed cubic Bezier varying widths") != std::string::npos ? 0 : 29;
    }
    return 30;
}

bool HasShortPinnedIndexedData(
    const std::vector<uint8_t>& page,
    const SdfPath& id,
    bool includeFirstCurve)
{
    const int controls[2][4] = {{4, 2, 2, 2}, {3, 1, 0, 0}};
    VtVec3fArray expected;
    VtIntArray subprims;
    for (int curve = includeFirstCurve ? 0 : 1; curve < 2; ++curve)
    {
        for (size_t chord = 0; chord < 8; ++chord)
        {
            for (size_t endpoint = 0; endpoint < 2; ++endpoint)
            {
                const double t = static_cast<double>(chord + endpoint) / 8.0;
                const double s = 1.0 - t;
                const double x =
                    s * s * s * controls[curve][0] +
                    3.0 * s * s * t * controls[curve][1] +
                    3.0 * s * t * t * controls[curve][2] +
                    t * t * t * controls[curve][3];
                expected.push_back(GfVec3f(static_cast<float>(x), 0.0f, 0.0f));
            }
            subprims.push_back(curve);
        }
    }
    return HasCubicData(page, id, expected, subprims);
}

int32_t VerifyShortPinnedIndexedSync()
{
    HdSilkRenderDelegate renderDelegate;
    std::unique_ptr<HdRenderIndex> index(HdRenderIndex::New(&renderDelegate, {}));
    if (!index)
    {
        return 35;
    }
    CubicTopologyDelegate scene(index.get(), SdfPath::AbsoluteRootPath());
    scene.SetHiddenCurve(false);
    const SdfPath id("/Curve");
    const VtVec3fArray points{
        GfVec3f(0.0f, 0.0f, 0.0f), GfVec3f(1.0f, 0.0f, 0.0f),
        GfVec3f(2.0f, 0.0f, 0.0f), GfVec3f(3.0f, 0.0f, 0.0f),
        GfVec3f(4.0f, 0.0f, 0.0f)};
    scene.AddBasisCurves(
        id, points, VtIntArray{2, 3}, VtIntArray{4, 2, 3, 1, 0}, VtVec3fArray(),
        HdTokens->cubic, HdTokens->bezier, VtValue(GfVec3f(1.0f)),
        HdInterpolationConstant, VtValue(1.0f), HdInterpolationConstant,
        VtValue(VtFloatArray{0.0f, 0.25f, 0.5f, 0.75f, 1.0f}),
        HdInterpolationVertex);
    scene.SetCurveWrapMode(id, HdTokens->pinned);
    HdRprim* curve = const_cast<HdRprim*>(index->GetRprim(id));
    if (curve == nullptr)
    {
        return 36;
    }
    const auto& state = renderDelegate.GetSceneState();
    const auto& budget = state->MeshPreparationBudget();
    constexpr uint64_t expected = 5 * 12 + 5 * 4 + 2 * 8 * 76;
    budget->Configure(expected);
    HdDirtyBits dirty = curve->GetInitialDirtyBitsMask();
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    uint32_t firstCommands = 0;
    const std::vector<uint8_t> firstPage = state->BuildPage(nullptr, &firstCommands);
    uint32_t quietCommands = 0;
    state->BuildPage(nullptr, &quietCommands);
    if (dirty != HdChangeTracker::Clean || budget->Refused() ||
        budget->Reserved() != expected || budget->Peak() != expected ||
        firstCommands <= quietCommands || !HasShortPinnedIndexedData(firstPage, id, true))
    {
        return 37;
    }

    HdChangeTracker& tracker = index->GetChangeTracker();
    scene.SetHiddenCurve(true);
    tracker.MarkRprimClean(id, HdChangeTracker::Clean);
    tracker.MarkRprimDirty(id, HdChangeTracker::DirtyTopology);
    dirty = curve->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    uint32_t refusedCommands = 0;
    state->BuildPage(nullptr, &refusedCommands);
    if (!budget->Refused() || budget->Reserved() != expected ||
        !state->HasMeshPath(id.GetString()) || refusedCommands != quietCommands)
    {
        return 38;
    }

    budget->Configure(expected * 2);
    tracker.MarkRprimClean(id, HdChangeTracker::Clean);
    tracker.MarkRprimDirty(id, HdChangeTracker::DirtyTopology);
    dirty = curve->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    uint32_t hiddenCommands = 0;
    const std::vector<uint8_t> hiddenPage = state->BuildPage(nullptr, &hiddenCommands);
    if (dirty != HdChangeTracker::Clean || budget->Refused() ||
        budget->Reserved() != expected || budget->Peak() != expected * 2 ||
        hiddenCommands <= quietCommands || !HasShortPinnedIndexedData(hiddenPage, id, false))
    {
        return 39;
    }

    scene.SetHiddenCurve(false);
    tracker.MarkRprimClean(id, HdChangeTracker::Clean);
    tracker.MarkRprimDirty(id, HdChangeTracker::DirtyTopology);
    dirty = curve->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    uint32_t restoredCommands = 0;
    const std::vector<uint8_t> restoredPage = state->BuildPage(nullptr, &restoredCommands);
    if (dirty != HdChangeTracker::Clean || budget->Refused() ||
        budget->Reserved() != expected || restoredCommands <= quietCommands ||
        !HasShortPinnedIndexedData(restoredPage, id, true))
    {
        return 40;
    }

    tracker.MarkRprimClean(id, HdChangeTracker::Clean);
    scene.SetVisibility(id, false);
    dirty = curve->PropagateRprimDirtyBits(tracker.GetRprimDirtyBits(id));
    curve->Sync(&scene, renderDelegate.GetRenderParam(), &dirty, TfToken("hull"));
    if (dirty != HdChangeTracker::Clean || budget->Refused() ||
        state->HasMeshPath(id.GetString()) || budget->Reserved() != expected)
    {
        return 41;
    }
    index->RemoveRprim(id);
    return budget->Reserved() == 0 ? 0 : 42;
}
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
    if (visibleCommands <= quietCommands)
    {
        return 16;
    }
    const int32_t cubicResult = VerifyIndexedCubicSync();
    if (cubicResult != 0)
    {
        return cubicResult;
    }
    const int32_t varyingResult = VerifyIndexedVaryingWidthRefusal();
    return varyingResult == 0 ? VerifyShortPinnedIndexedSync() : varyingResult;
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
