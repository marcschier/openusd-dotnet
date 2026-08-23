// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_physics_override_scene_index.h"

#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/rotation.h"
#include "pxr/imaging/hd/retainedDataSource.h"
#include "pxr/imaging/hd/retainedSceneIndex.h"
#include "pxr/imaging/hd/sceneIndexObserver.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/imaging/hd/xformSchema.h"
#include "pxr/usd/sdf/path.h"

#include <cmath>
#include <cstdint>
#include <iostream>
#include <limits>
#include <string>
#include <thread>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
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
            xform_dirtied = xform_dirtied ||
                entry.dirtyLocators.Intersects(
                    HdXformSchema::GetDefaultLocator());
        }
    }

    void PrimsRenamed(const HdSceneIndexBase&, const RenamedPrimEntries&) override
    {
    }

    std::vector<SdfPath> dirtied;
    bool xform_dirtied = false;
};

GfMatrix4d Translation(double x, double y, double z)
{
    GfMatrix4d matrix(1.0);
    matrix.SetTranslateOnly(GfVec3d(x, y, z));
    return matrix;
}

HdContainerDataSourceHandle AuthoredXform(const GfMatrix4d& matrix)
{
    return HdRetainedContainerDataSource::New(
        HdXformSchemaTokens->xform,
        HdXformSchema::Builder()
            .SetMatrix(
                HdRetainedTypedSampledDataSource<GfMatrix4d>::New(matrix))
            .Build());
}

bool ReadXform(
    const HdSceneIndexBaseRefPtr& sceneIndex,
    const SdfPath& path,
    GfMatrix4d* matrix,
    bool* resetXformStack)
{
    const HdSceneIndexPrim prim = sceneIndex->GetPrim(path);
    if (!prim.dataSource)
    {
        return false;
    }
    HdXformSchema schema = HdXformSchema::GetFromParent(prim.dataSource);
    if (!schema.IsDefined() || !schema.GetMatrix())
    {
        return false;
    }
    *matrix = schema.GetMatrix()->GetTypedValue(0.0);
    const HdBoolDataSourceHandle reset = schema.GetResetXformStack();
    *resetXformStack = reset && reset->GetTypedValue(0.0);
    return true;
}

bool Near(const GfMatrix4d& left, const GfMatrix4d& right)
{
    for (int row = 0; row < 4; ++row)
    {
        for (int column = 0; column < 4; ++column)
        {
            if (std::abs(left[row][column] - right[row][column]) > 1e-12)
            {
                return false;
            }
        }
    }
    return true;
}

OpenUsdPhysicsOverrideEntry Entry(
    const char* path,
    const GfMatrix4d& matrix,
    uint64_t id,
    bool preserve_stretch = false)
{
    OpenUsdPhysicsOverrideEntry entry;
    entry.path = SdfPath(path);
    entry.transform = matrix;
    entry.object_id = id;
    entry.preserve_stretch = preserve_stretch;
    return entry;
}

bool NearLinear(const GfMatrix4d& left, const GfMatrix4d& right)
{
    for (int row = 0; row < 3; ++row)
    {
        for (int column = 0; column < 3; ++column)
        {
            if (std::abs(left[row][column] - right[row][column]) > 1e-9)
            {
                return false;
            }
        }
    }
    return true;
}

/* The Gram matrix of the linear part is invariant under a rotation change. */
GfMatrix4d Gram(const GfMatrix4d& matrix)
{
    GfMatrix4d linear(1.0);
    for (int row = 0; row < 3; ++row)
    {
        for (int column = 0; column < 3; ++column)
        {
            linear[row][column] = matrix[row][column];
        }
    }
    return linear * linear.GetTranspose();
}

GfMatrix4d Rotation(double degrees)
{
    GfMatrix4d matrix(1.0);
    matrix.SetRotate(GfRotation(GfVec3d(0.3, 0.5, 0.8).GetNormalized(), degrees));
    return matrix;
}
}

int main()
{
    static_assert(sizeof(openusd_storm_transform_override_item) == 152);
    static_assert(sizeof(openusd_storm_transform_override_update) == 48);
    static_assert(
        sizeof(openusd_storm_transform_override_diagnostics) == 64);

    HdRetainedSceneIndexRefPtr retained = HdRetainedSceneIndex::New();
    const SdfPath cubePath("/World/Cube");
    const SdfPath spherePath("/World/Sphere");
    const GfMatrix4d authoredCube = Translation(1.0, 0.0, 0.0);
    const GfMatrix4d authoredSphere = Translation(0.0, 2.0, 0.0);
    retained->AddPrims({
        {cubePath, HdPrimTypeTokens->mesh, AuthoredXform(authoredCube)},
        {spherePath, HdPrimTypeTokens->mesh, AuthoredXform(authoredSphere)}});

    OpenUsdPhysicsOverrideSceneIndexRefPtr overrides =
        OpenUsdPhysicsOverrideSceneIndex::New(retained);
    RecordingObserver observer;
    overrides->AddObserver(HdSceneIndexObserverPtr(&observer));

    GfMatrix4d matrix(1.0);
    bool reset = false;
    if (!ReadXform(overrides, cubePath, &matrix, &reset) ||
        !Near(matrix, authoredCube) || reset)
    {
        std::cerr << "The passthrough scene index changed an authored xform.\n";
        return 1;
    }

    const GfMatrix4d simulated = Translation(5.0, 6.0, 7.0);
    overrides->ApplyBatch({Entry("/World/Cube", simulated, 11u)}, 3u, 1u, 2u, 4u);
    if (!ReadXform(overrides, cubePath, &matrix, &reset) ||
        !Near(matrix, simulated) || !reset)
    {
        std::cerr << "The override was not applied with a reset xform stack.\n";
        return 2;
    }
    if (!ReadXform(overrides, spherePath, &matrix, &reset) ||
        !Near(matrix, authoredSphere) || reset)
    {
        std::cerr << "An unrelated prim lost its authored xform.\n";
        return 3;
    }
    if (observer.dirtied.size() != 1 ||
        observer.dirtied.front() != cubePath ||
        !observer.xform_dirtied)
    {
        std::cerr << "Applying an override did not dirty exactly the xform.\n";
        return 4;
    }

    OpenUsdPhysicsOverrideCounters counters = overrides->GetCounters();
    if (counters.applied_count != 1u ||
        counters.unresolved_count != 1u ||
        counters.dropped_count != 2u ||
        counters.unsupported_count != 4u ||
        counters.revision != 3u ||
        counters.applied_batch_count != 1u ||
        counters.dirtied_prim_count != 1u)
    {
        std::cerr << "The applied-state counters are wrong.\n";
        return 5;
    }

    observer.dirtied.clear();
    overrides->ClearOverrides();
    if (!ReadXform(overrides, cubePath, &matrix, &reset) ||
        !Near(matrix, authoredCube) || reset)
    {
        std::cerr << "Clearing overrides did not restore the authored xform.\n";
        return 6;
    }
    if (observer.dirtied.size() != 1 || observer.dirtied.front() != cubePath)
    {
        std::cerr << "Clearing overrides did not invalidate the prim.\n";
        return 7;
    }
    counters = overrides->GetCounters();
    if (counters.applied_count != 0u ||
        counters.unresolved_count != 0u ||
        counters.unsupported_count != 0u)
    {
        std::cerr << "Clearing overrides did not reset the applied counters.\n";
        return 8;
    }

    overrides->ApplyBatch({Entry("/World/Cube", simulated, 11u)}, 4u, 0u, 0u, 0u);
    retained->RemovePrims({{cubePath}});
    counters = overrides->GetCounters();
    if (counters.applied_count != 0u || overrides->HasOverride(cubePath))
    {
        std::cerr << "A removed prim kept its retained override.\n";
        return 9;
    }

    retained->AddPrims({
        {cubePath, HdPrimTypeTokens->mesh, AuthoredXform(authoredCube)}});
    overrides->ApplyBatch(
        {Entry("/World/Cube", simulated, 11u),
         Entry("/World/Sphere", Translation(9.0, 9.0, 9.0), 12u)},
        5u,
        0u,
        0u,
        0u);

    // Hydra reads prims from worker threads while the render thread replaces
    // batches, so concurrent reads must never observe a torn override table.
    bool torn = false;
    std::thread reader([&]()
    {
        for (int iteration = 0; iteration < 2000; ++iteration)
        {
            GfMatrix4d read(1.0);
            bool readReset = false;
            if (!ReadXform(overrides, spherePath, &read, &readReset))
            {
                torn = true;
                return;
            }
            if (readReset && read[3][0] != 9.0)
            {
                torn = true;
                return;
            }
        }
    });
    for (int iteration = 0; iteration < 2000; ++iteration)
    {
        overrides->ApplyBatch(
            {Entry("/World/Cube", simulated, 11u),
             Entry("/World/Sphere", Translation(9.0, 9.0, 9.0), 12u)},
            6u + static_cast<uint64_t>(iteration),
            0u,
            0u,
            0u);
    }
    reader.join();
    if (torn)
    {
        std::cerr << "A concurrent reader observed a torn override table.\n";
        return 10;
    }

    overrides->RecordRejectedBatch();
    counters = overrides->GetCounters();
    if (counters.rejected_batch_count != 1u || counters.applied_count != 2u)
    {
        std::cerr << "Rejected batches are not accounted separately.\n";
        return 11;
    }

    overrides->RemoveObserver(HdSceneIndexObserverPtr(&observer));

    // An authored scaled and sheared prim must keep its shape while simulation
    // replaces only the rotation and the translation.
    HdRetainedSceneIndexRefPtr stretched = HdRetainedSceneIndex::New();
    const SdfPath shearedPath("/World/Sheared");
    const SdfPath singularPath("/World/Singular");
    const SdfPath brokenPath("/World/Broken");
    GfMatrix4d shear(1.0);
    shear[0][0] = 2.0;
    shear[1][1] = 0.5;
    shear[2][2] = 1.5;
    shear[0][1] = 0.4;
    shear[1][0] = 0.4;
    shear[0][2] = -0.25;
    shear[2][0] = -0.25;
    const GfMatrix4d authoredRotation = Rotation(37.0);
    const GfMatrix4d authoredSheared =
        shear * authoredRotation * Translation(3.0, 3.0, 3.0);
    GfMatrix4d singular(1.0);
    singular[2][2] = 0.0;
    GfMatrix4d broken(1.0);
    broken[1][1] = std::numeric_limits<double>::quiet_NaN();
    stretched->AddPrims({
        {shearedPath, HdPrimTypeTokens->mesh, AuthoredXform(authoredSheared)},
        {singularPath, HdPrimTypeTokens->mesh, AuthoredXform(singular)},
        {brokenPath, HdPrimTypeTokens->mesh, AuthoredXform(broken)}});

    OpenUsdPhysicsOverrideSceneIndexRefPtr stretchOverrides =
        OpenUsdPhysicsOverrideSceneIndex::New(stretched);
    const GfMatrix4d simulatedPose = Rotation(-112.0) * Translation(5.0, 6.0, 7.0);
    stretchOverrides->ApplyBatch(
        {Entry("/World/Sheared", simulatedPose, 21u, true),
         Entry("/World/Singular", simulatedPose, 22u, true),
         Entry("/World/Broken", simulatedPose, 23u, true)},
        7u,
        0u,
        0u,
        0u);

    GfMatrix4d composed(1.0);
    bool composedReset = false;
    if (!ReadXform(stretchOverrides, shearedPath, &composed, &composedReset) ||
        !composedReset)
    {
        std::cerr << "The stretch preserving override was not applied.\n";
        return 12;
    }
    if (!NearLinear(Gram(composed), Gram(authoredSheared)))
    {
        std::cerr << "The authored scale and shear were not preserved.\n";
        return 13;
    }
    if (NearLinear(composed, authoredSheared))
    {
        std::cerr << "The authored rotation was not replaced.\n";
        return 14;
    }
    if (std::abs(composed[3][0] - 5.0) > 1e-12 ||
        std::abs(composed[3][1] - 6.0) > 1e-12 ||
        std::abs(composed[3][2] - 7.0) > 1e-12)
    {
        std::cerr << "The simulated translation was not applied.\n";
        return 15;
    }

    // Composing the authored rotation again must restore the authored basis
    // exactly, which is the property a polar decomposition guarantees.
    stretchOverrides->ApplyBatch(
        {Entry("/World/Sheared", authoredRotation, 21u, true)}, 8u, 0u, 0u, 0u);
    if (!ReadXform(stretchOverrides, shearedPath, &composed, &composedReset) ||
        !NearLinear(composed, authoredSheared))
    {
        std::cerr << "Recomposing the authored rotation lost the authored basis.\n";
        return 16;
    }

    stretchOverrides->ApplyBatch(
        {Entry("/World/Singular", simulatedPose, 22u, true),
         Entry("/World/Broken", simulatedPose, 23u, true)},
        9u,
        0u,
        0u,
        0u);
    if (!ReadXform(stretchOverrides, singularPath, &composed, &composedReset))
    {
        std::cerr << "A singular authored basis dropped the override.\n";
        return 17;
    }
    for (int row = 0; row < 4; ++row)
    {
        for (int column = 0; column < 4; ++column)
        {
            if (!std::isfinite(composed[row][column]))
            {
                std::cerr << "A singular authored basis produced a NaN.\n";
                return 18;
            }
        }
    }
    if (std::abs(composed[2][2]) > 1e-9 || std::abs(composed[2][0]) > 1e-9 ||
        std::abs(composed[2][1]) > 1e-9)
    {
        std::cerr << "A collapsed authored axis was resurrected.\n";
        return 19;
    }
    if (!ReadXform(stretchOverrides, brokenPath, &composed, &composedReset) ||
        !Near(composed, simulatedPose))
    {
        std::cerr << "A non-finite authored basis did not fall back safely.\n";
        return 20;
    }

    stretchOverrides->ClearOverrides();
    if (!ReadXform(stretchOverrides, shearedPath, &composed, &composedReset) ||
        !Near(composed, authoredSheared) || composedReset)
    {
        std::cerr << "Clearing did not restore the authored sheared xform.\n";
        return 21;
    }

    return 0;
}
