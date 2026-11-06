// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_GEOM_SUBSET_SCENE_INDEX_H
#define OPENUSD_GEOM_SUBSET_SCENE_INDEX_H

#include "pxr/base/tf/declarePtrs.h"
#include "pxr/base/tf/token.h"
#include "pxr/imaging/hd/filteringSceneIndex.h"
#include "pxr/imaging/hd/sceneIndex.h"
#include "pxr/imaging/hd/sceneIndexPluginRegistry.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/pxr.h"
#include "pxr/usd/sdf/path.h"

#include <mutex>
#include <string>

PXR_NAMESPACE_OPEN_SCOPE

TF_DECLARE_REF_PTRS(OpenUsdGeomSubsetSceneIndex);

/*
 * Hides GeomSubset prims whose parent is not a gprim.
 *
 * Hydra's scene index adapter answers every added, dirtied, or removed
 * geomSubset by dirtying the parent rprim's topology. A subset authored under
 * the pseudo-root or an Xform has no parent rprim, so the change tracker fails
 * verification for each notice, including the selection notice an ancestor
 * selection sends to the subset. Such a subset never contributes geometry, so
 * this scene index reports it with an empty prim type. The adapter then caches
 * it as a typeless prim and ignores its later notices. The data source is kept
 * so other consumers can still read it.
 *
 * A prim retyped by a resync is re-added with its subtree, so the parent type
 * read while forwarding the added notice is current for every child entry.
 */
class OpenUsdGeomSubsetSceneIndex final
    : public HdSingleInputFilteringSceneIndexBase
{
public:
    static OpenUsdGeomSubsetSceneIndexRefPtr New(
        const HdSceneIndexBaseRefPtr& inputScene)
    {
        return TfCreateRefPtr(new OpenUsdGeomSubsetSceneIndex(inputScene));
    }

    HdSceneIndexPrim GetPrim(const SdfPath& primPath) const override
    {
        HdSceneIndexPrim prim = _GetInputSceneIndex()->GetPrim(primPath);
        if (prim.primType == HdPrimTypeTokens->geomSubset &&
            !_ParentIsGprim(primPath))
        {
            prim.primType = TfToken();
        }
        return prim;
    }

    SdfPathVector GetChildPrimPaths(const SdfPath& primPath) const override
    {
        return _GetInputSceneIndex()->GetChildPrimPaths(primPath);
    }

protected:
    void _PrimsAdded(
        const HdSceneIndexBase&,
        const HdSceneIndexObserver::AddedPrimEntries& entries) override
    {
        for (size_t index = 0; index < entries.size(); ++index)
        {
            if (_IsMisplaced(entries[index]))
            {
                HdSceneIndexObserver::AddedPrimEntries filtered(
                    entries.begin(), entries.begin() + index);
                filtered.reserve(entries.size());
                for (; index < entries.size(); ++index)
                {
                    filtered.push_back(entries[index]);
                    if (_IsMisplaced(entries[index]))
                    {
                        filtered.back().primType = TfToken();
                    }
                }
                _SendPrimsAdded(filtered);
                return;
            }
        }
        _SendPrimsAdded(entries);
    }

    void _PrimsRemoved(
        const HdSceneIndexBase&,
        const HdSceneIndexObserver::RemovedPrimEntries& entries) override
    {
        _SendPrimsRemoved(entries);
    }

    void _PrimsDirtied(
        const HdSceneIndexBase&,
        const HdSceneIndexObserver::DirtiedPrimEntries& entries) override
    {
        _SendPrimsDirtied(entries);
    }

private:
    explicit OpenUsdGeomSubsetSceneIndex(
        const HdSceneIndexBaseRefPtr& inputScene)
        : HdSingleInputFilteringSceneIndexBase(inputScene)
    {
    }

    bool _IsMisplaced(const HdSceneIndexObserver::AddedPrimEntry& entry) const
    {
        return entry.primType == HdPrimTypeTokens->geomSubset &&
            !_ParentIsGprim(entry.primPath);
    }

    bool _ParentIsGprim(const SdfPath& primPath) const
    {
        const SdfPath parent = primPath.GetParentPath();
        return !parent.IsEmpty() &&
            HdPrimTypeIsGprim(_GetInputSceneIndex()->GetPrim(parent).primType);
    }
};

/*
 * Installs the subset scene index into exactly the Hydra graph being created
 * on this thread, using the same thread-local capture scheme as the physics
 * override registrar so engines built by other components are unchanged.
 */
class OpenUsdGeomSubsetSceneIndexRegistrar
{
public:
    class Capture
    {
    public:
        Capture()
        {
            static std::once_flag once;
            std::call_once(once, []()
            {
                HdSceneIndexPluginRegistry::GetInstance()
                    .RegisterSceneIndexForRenderer(
                        std::string(),
                        &OpenUsdGeomSubsetSceneIndexRegistrar::_Append,
                        nullptr,
                        // Last, so prims retyped by renderer plugins, such as
                        // implicit surfaces converted to meshes, are gprims.
                        /* insertionPhase = */ 1000,
                        HdSceneIndexPluginRegistry::InsertionOrderAtEnd);
            });
            _Capturing() = true;
            _Appended() = false;
        }

        Capture(const Capture&) = delete;
        Capture& operator=(const Capture&) = delete;

        ~Capture()
        {
            _Capturing() = false;
            _Appended() = false;
        }
    };

private:
    static bool& _Capturing()
    {
        static thread_local bool capturing = false;
        return capturing;
    }

    static bool& _Appended()
    {
        static thread_local bool appended = false;
        return appended;
    }

    static HdSceneIndexBaseRefPtr _Append(
        const std::string&,
        const HdSceneIndexBaseRefPtr& inputScene,
        const HdContainerDataSourceHandle&)
    {
        if (!_Capturing() || _Appended())
        {
            return inputScene;
        }
        _Appended() = true;
        return OpenUsdGeomSubsetSceneIndex::New(inputScene);
    }
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
