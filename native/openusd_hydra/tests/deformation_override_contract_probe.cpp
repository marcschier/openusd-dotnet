// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physics_override_scene_index.h"

#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/vt/array.h"
#include "pxr/imaging/hd/extentSchema.h"
#include "pxr/imaging/hd/primvarSchema.h"
#include "pxr/imaging/hd/primvarsSchema.h"
#include "pxr/imaging/hd/retainedDataSource.h"
#include "pxr/imaging/hd/retainedSceneIndex.h"
#include "pxr/imaging/hd/sceneIndexObserver.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/imaging/hd/xformSchema.h"
#include "pxr/usd/sdf/path.h"

#include <cmath>
#include <cstdint>
#include <iostream>
#include <string>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
int g_failures = 0;

bool Expect(bool condition, const std::string& description)
{
    if (!condition)
    {
        std::cerr << "FAIL: " << description << '\n';
        ++g_failures;
    }
    return condition;
}

class RecordingObserver final : public HdSceneIndexObserver
{
public:
    void PrimsAdded(const HdSceneIndexBase&, const AddedPrimEntries&) override
    {
    }

    void PrimsRemoved(const HdSceneIndexBase&, const RemovedPrimEntries&) override
    {
    }

    void PrimsDirtied(
        const HdSceneIndexBase&,
        const DirtiedPrimEntries& entries) override
    {
        for (const DirtiedPrimEntry& entry : entries)
        {
            dirtied.push_back(entry.primPath);
            points_dirtied = points_dirtied ||
                entry.dirtyLocators.Intersects(
                    HdPrimvarsSchema::GetPointsLocator());
            extent_dirtied = extent_dirtied ||
                entry.dirtyLocators.Intersects(HdExtentSchema::GetDefaultLocator());
            xform_dirtied = xform_dirtied ||
                entry.dirtyLocators.Intersects(
                    HdXformSchema::GetDefaultLocator());
        }
    }

    void PrimsRenamed(const HdSceneIndexBase&, const RenamedPrimEntries&) override
    {
    }

    void Reset()
    {
        dirtied.clear();
        points_dirtied = false;
        extent_dirtied = false;
        xform_dirtied = false;
    }

    std::vector<SdfPath> dirtied;
    bool points_dirtied = false;
    bool extent_dirtied = false;
    bool xform_dirtied = false;
};

VtVec3fArray MakeTriangle(float height)
{
    VtVec3fArray points(3);
    points[0] = GfVec3f(0.0f, height, 0.0f);
    points[1] = GfVec3f(1.0f, height, 0.0f);
    points[2] = GfVec3f(1.0f, height, 1.0f);
    return points;
}

HdContainerDataSourceHandle AuthoredMesh(const VtVec3fArray& points)
{
    return HdRetainedContainerDataSource::New(
        HdXformSchemaTokens->xform,
        HdXformSchema::Builder()
            .SetMatrix(
                HdRetainedTypedSampledDataSource<GfMatrix4d>::New(GfMatrix4d(1.0)))
            .Build(),
        HdPrimvarsSchemaTokens->primvars,
        HdRetainedContainerDataSource::New(
            HdPrimvarsSchemaTokens->points,
            HdPrimvarSchema::Builder()
                .SetPrimvarValue(
                    HdRetainedTypedSampledDataSource<VtVec3fArray>::New(points))
                .SetInterpolation(
                    HdPrimvarSchema::BuildInterpolationDataSource(
                        HdPrimvarSchemaTokens->vertex))
                .SetRole(
                    HdPrimvarSchema::BuildRoleDataSource(
                        HdPrimvarSchemaTokens->point))
                .Build()));
}

bool ReadPoints(
    const HdSceneIndexBaseRefPtr& sceneIndex,
    const SdfPath& path,
    VtVec3fArray* points)
{
    const HdSceneIndexPrim prim = sceneIndex->GetPrim(path);
    if (!prim.dataSource)
    {
        return false;
    }
    HdPrimvarsSchema primvars = HdPrimvarsSchema::GetFromParent(prim.dataSource);
    HdPrimvarSchema primvar = primvars.GetPrimvar(HdPrimvarsSchemaTokens->points);
    if (!primvar)
    {
        return false;
    }
    HdSampledDataSourceHandle value = primvar.GetPrimvarValue();
    if (!value)
    {
        return false;
    }
    const VtValue authored = value->GetValue(0.0);
    if (!authored.IsHolding<VtVec3fArray>())
    {
        return false;
    }
    *points = authored.UncheckedGet<VtVec3fArray>();
    return true;
}

bool Near(const VtVec3fArray& left, const VtVec3fArray& right)
{
    if (left.size() != right.size())
    {
        return false;
    }
    for (size_t index = 0; index < left.size(); ++index)
    {
        for (size_t axis = 0; axis < 3; ++axis)
        {
            if (std::fabs(left[index][static_cast<int>(axis)] -
                    right[index][static_cast<int>(axis)]) > 1e-5f)
            {
                return false;
            }
        }
    }
    return true;
}

OpenUsdPhysicsDeformationEntry MakeEntry(const SdfPath& path, const VtVec3fArray& points)
{
    OpenUsdPhysicsDeformationEntry entry;
    entry.path = path;
    entry.points = points;
    entry.object_id = 0x51DEC107u;
    entry.topology_revision = 7;
    return entry;
}
}

int main()
{
    // The packed page layout is the contract two languages agree on, so it is
    // asserted here as well as in the header the managed mirror is generated
    // against.
    static_assert(sizeof(openusd_storm_deformation_override_item) == 40);
    static_assert(sizeof(openusd_storm_deformation_override_diagnostics) == 64);
#if UINTPTR_MAX == UINT64_MAX
    static_assert(sizeof(openusd_storm_deformation_override_update) == 56);
#endif

    HdRetainedSceneIndexRefPtr retained = HdRetainedSceneIndex::New();
    const SdfPath clothPath("/World/Cloth");
    const SdfPath jellyPath("/World/Jelly");
    const VtVec3fArray authoredCloth = MakeTriangle(0.0f);
    const VtVec3fArray authoredJelly = MakeTriangle(4.0f);
    retained->AddPrims({
        {clothPath, HdPrimTypeTokens->mesh, AuthoredMesh(authoredCloth)},
        {jellyPath, HdPrimTypeTokens->mesh, AuthoredMesh(authoredJelly)}});

    OpenUsdPhysicsOverrideSceneIndexRefPtr overrides =
        OpenUsdPhysicsOverrideSceneIndex::New(retained);
    RecordingObserver observer;
    overrides->AddObserver(HdSceneIndexObserverPtr(&observer));

    VtVec3fArray read;
    Expect(
        ReadPoints(overrides, clothPath, &read) && Near(read, authoredCloth),
        "the passthrough scene index leaves authored points alone");
    Expect(
        overrides->GetRenderedPointCount(clothPath) == 3,
        "the rendered vertex count is reported from the input scene");
    Expect(
        overrides->GetRenderedPointCount(SdfPath("/World/Absent")) == 0,
        "a prim the input scene does not carry reports no vertices");

    // A simulated surface deformation must reach the prim without anything being
    // authored onto a stage: the retained scene index is untouched and only the
    // filtering scene index carries the simulated points.
    const VtVec3fArray simulatedCloth = MakeTriangle(2.5f);
    observer.Reset();
    overrides->ApplyDeformationBatch({MakeEntry(clothPath, simulatedCloth)}, 11, 0, 0, 0, 0);

    Expect(
        ReadPoints(overrides, clothPath, &read) && Near(read, simulatedCloth),
        "a simulated surface deformation replaces the rendered points");
    Expect(observer.points_dirtied, "applying a deformation dirties the points locator");
    Expect(!observer.xform_dirtied, "applying a deformation does not dirty the xform locator");
    Expect(
        overrides->HasDeformation(clothPath),
        "the scene index reports the prim it is deforming");

    VtVec3fArray retainedCloth;
    Expect(
        ReadPoints(retained, clothPath, &retainedCloth) && Near(retainedCloth, authoredCloth),
        "the input scene still carries the authored points, so nothing was authored");
    Expect(
        ReadPoints(overrides, jellyPath, &read) && Near(read, authoredJelly),
        "a prim outside the batch keeps its authored points");

    OpenUsdPhysicsDeformationCounters counters = overrides->GetDeformationCounters();
    Expect(counters.applied_count == 1, "one region is reported as applied");
    Expect(counters.revision == 11, "the batch revision is reported");
    Expect(counters.applied_batch_count == 1, "one batch is reported as accepted");
    Expect(counters.dirtied_prim_count != 0, "dirtied prims are counted");

    // Replacing points invalidates the authored extent, so the bounds have to
    // follow the simulated geometry or Storm culls a deformed body against its
    // rest pose and it disappears while it is on screen.
    Expect(observer.extent_dirtied, "applying a deformation dirties the extent locator");
    const HdSceneIndexPrim clothPrim = overrides->GetPrim(clothPath);
    HdExtentSchema clothExtent = HdExtentSchema::GetFromParent(clothPrim.dataSource);
    if (Expect(
            clothExtent.IsDefined() && clothExtent.GetMin() && clothExtent.GetMax(),
            "a deformed prim publishes an extent"))
    {
        const GfVec3d minimum = clothExtent.GetMin()->GetTypedValue(0.0);
        const GfVec3d maximum = clothExtent.GetMax()->GetTypedValue(0.0);
        Expect(
            std::fabs(minimum[1] - 2.5) < 1e-5 && std::fabs(maximum[1] - 2.5) < 1e-5,
            "the published extent bounds the simulated points rather than the authored ones");
        Expect(
            std::fabs(minimum[0] - 0.0) < 1e-5 && std::fabs(maximum[0] - 1.0) < 1e-5,
            "the published extent spans the simulated points on every axis");
    }

    // A batch replaces the previous one whole, so a prim that leaves the batch
    // goes back to its authored points in the same frame the new batch lands.
    const VtVec3fArray simulatedJelly = MakeTriangle(6.5f);
    observer.Reset();
    overrides->ApplyDeformationBatch({MakeEntry(jellyPath, simulatedJelly)}, 12, 1, 0, 2, 3);
    Expect(
        ReadPoints(overrides, clothPath, &read) && Near(read, authoredCloth),
        "a prim the newest batch omits renders its authored points again");
    Expect(
        ReadPoints(overrides, jellyPath, &read) && Near(read, simulatedJelly),
        "a simulated volume deformation replaces the rendered points");

    counters = overrides->GetDeformationCounters();
    Expect(counters.unresolved_count == 1, "the unresolved count is reported verbatim");
    Expect(counters.unsupported_count == 2, "the unsupported count is reported verbatim");
    Expect(
        counters.mismatched_count == 3,
        "a region the topology cannot accept is reported as mismatched rather than drawn");

    // Transform and deformation overrides drive the same prim independently.
    GfMatrix4d moved(1.0);
    moved.SetTranslateOnly(GfVec3d(3.0, 0.0, 0.0));
    OpenUsdPhysicsOverrideEntry transform;
    transform.path = jellyPath;
    transform.transform = moved;
    observer.Reset();
    overrides->ApplyBatch({transform}, 13, 0, 0, 0);
    Expect(
        ReadPoints(overrides, jellyPath, &read) && Near(read, simulatedJelly),
        "applying a transform batch leaves the retained deformation in place");
    Expect(observer.xform_dirtied, "applying a transform batch dirties the xform locator");

    const HdSceneIndexPrim jelly = overrides->GetPrim(jellyPath);
    HdXformSchema jellyXform = HdXformSchema::GetFromParent(jelly.dataSource);
    Expect(
        jellyXform.IsDefined() && jellyXform.GetMatrix() &&
            jellyXform.GetMatrix()->GetTypedValue(0.0) == moved,
        "one prim carries both the simulated transform and the simulated points");

    // Clearing one channel must not clear the other.
    observer.Reset();
    overrides->ClearDeformations();
    Expect(
        ReadPoints(overrides, jellyPath, &read) && Near(read, authoredJelly),
        "clearing the deformations restores the authored points");
    Expect(observer.points_dirtied, "clearing the deformations dirties the points locator");
    jellyXform = HdXformSchema::GetFromParent(overrides->GetPrim(jellyPath).dataSource);
    Expect(
        jellyXform.IsDefined() && jellyXform.GetMatrix() &&
            jellyXform.GetMatrix()->GetTypedValue(0.0) == moved,
        "clearing the deformations leaves the transform override applied");

    overrides->ApplyDeformationBatch({MakeEntry(jellyPath, simulatedJelly)}, 14, 0, 0, 0, 0);
    overrides->ClearOverrides();
    Expect(
        ReadPoints(overrides, jellyPath, &read) && Near(read, simulatedJelly),
        "clearing the transform overrides leaves the deformation applied");

    // A removed prim must not keep a retained deformation alive.
    overrides->RemoveObserver(HdSceneIndexObserverPtr(&observer));
    retained->RemovePrims({{jellyPath}});
    Expect(
        !overrides->HasDeformation(jellyPath),
        "removing a prim drops the deformation it retained");

    if (g_failures != 0)
    {
        std::cerr << g_failures << " Storm deformation override check(s) failed.\n";
        return 1;
    }
    std::cout << "openusd_hydra Storm deformation override checks passed.\n";
    return 0;
}
