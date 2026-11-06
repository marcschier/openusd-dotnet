// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_geom_subset_scene_index.h"

#include "pxr/imaging/hd/retainedDataSource.h"
#include "pxr/imaging/hd/retainedSceneIndex.h"
#include "pxr/imaging/hd/sceneIndexObserver.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/usd/sdf/path.h"

#include <iostream>
#include <unordered_map>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
class RecordingObserver final : public HdSceneIndexObserver
{
public:
    void PrimsAdded(const HdSceneIndexBase&, const AddedPrimEntries& entries) override
    {
        for (const AddedPrimEntry& entry : entries)
        {
            added[entry.primPath] = entry.primType;
        }
    }

    void PrimsRemoved(const HdSceneIndexBase&, const RemovedPrimEntries&) override
    {
    }

    void PrimsDirtied(const HdSceneIndexBase&, const DirtiedPrimEntries&) override
    {
    }

    void PrimsRenamed(const HdSceneIndexBase&, const RenamedPrimEntries&) override
    {
    }

    std::unordered_map<SdfPath, TfToken, SdfPath::Hash> added;
};

HdRetainedSceneIndex::AddedPrimEntry Entry(const char* path, const TfToken& type)
{
    return {SdfPath(path), type, HdRetainedContainerDataSource::New()};
}
} // namespace

int main()
{
    const HdRetainedSceneIndexRefPtr input = HdRetainedSceneIndex::New();
    const OpenUsdGeomSubsetSceneIndexRefPtr filtered =
        OpenUsdGeomSubsetSceneIndex::New(input);
    RecordingObserver observer;
    filtered->AddObserver(HdSceneIndexObserverPtr(&observer));

    const TfToken& subset = HdPrimTypeTokens->geomSubset;
    input->AddPrims({
        Entry("/Orphan", subset),
        Entry("/Group", TfToken()),
        Entry("/Group/Stray", subset),
        Entry("/Group/Mesh", HdPrimTypeTokens->mesh),
        Entry("/Group/Mesh/Faces", subset),
        Entry("/Curves", HdPrimTypeTokens->basisCurves),
        Entry("/Curves/Segments", subset),
    });

    const auto typeOf = [&](const char* path)
    {
        return filtered->GetPrim(SdfPath(path)).primType;
    };
    if (!typeOf("/Orphan").IsEmpty() || !typeOf("/Group/Stray").IsEmpty())
    {
        std::cerr << "A subset outside a gprim kept its geomSubset type.\n";
        return 1;
    }
    if (typeOf("/Group/Mesh/Faces") != subset || typeOf("/Curves/Segments") != subset)
    {
        std::cerr << "A subset of a gprim lost its geomSubset type.\n";
        return 2;
    }
    if (typeOf("/Group/Mesh") != HdPrimTypeTokens->mesh ||
        !filtered->GetPrim(SdfPath("/Group/Stray")).dataSource)
    {
        std::cerr << "The filter changed a prim it does not own.\n";
        return 3;
    }
    if (observer.added.size() != 7 ||
        !observer.added[SdfPath("/Orphan")].IsEmpty() ||
        !observer.added[SdfPath("/Group/Stray")].IsEmpty() ||
        observer.added[SdfPath("/Group/Mesh/Faces")] != subset ||
        observer.added[SdfPath("/Curves/Segments")] != subset ||
        observer.added[SdfPath("/Group/Mesh")] != HdPrimTypeTokens->mesh)
    {
        std::cerr << "Added notices disagree with GetPrim.\n";
        return 4;
    }

    // A resync that retypes the parent re-adds its subtree, and the child
    // reflects the parent type read while that notice is forwarded.
    observer.added.clear();
    input->AddPrims({
        Entry("/Group", HdPrimTypeTokens->mesh),
        Entry("/Group/Stray", subset),
    });
    if (observer.added[SdfPath("/Group/Stray")] != subset || typeOf("/Group/Stray") != subset)
    {
        std::cerr << "A subset under a retyped gprim stayed hidden.\n";
        return 5;
    }

    return 0;
}
