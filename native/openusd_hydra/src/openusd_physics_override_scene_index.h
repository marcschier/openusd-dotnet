// Copyright (c) marcschier. Licensed under the MIT License.

#ifndef OPENUSD_PHYSICS_OVERRIDE_SCENE_INDEX_H
#define OPENUSD_PHYSICS_OVERRIDE_SCENE_INDEX_H

#include "openusd_render_physics.h"

#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec3d.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/tf/declarePtrs.h"
#include "pxr/base/tf/token.h"
#include "pxr/base/vt/array.h"
#include "pxr/base/vt/value.h"
#include "pxr/imaging/hd/dataSourceLocator.h"
#include "pxr/imaging/hd/extentSchema.h"
#include "pxr/imaging/hd/filteringSceneIndex.h"
#include "pxr/imaging/hd/overlayContainerDataSource.h"
#include "pxr/imaging/hd/primvarSchema.h"
#include "pxr/imaging/hd/primvarsSchema.h"
#include "pxr/imaging/hd/retainedDataSource.h"
#include "pxr/imaging/hd/sceneIndex.h"
#include "pxr/imaging/hd/sceneIndexPluginRegistry.h"
#include "pxr/imaging/hd/xformSchema.h"
#include "pxr/pxr.h"
#include "pxr/usd/sdf/path.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <iterator>
#include <mutex>
#include <shared_mutex>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

PXR_NAMESPACE_OPEN_SCOPE

/*
 * One validated batch entry. The path is already parsed and the transform is
 * already converted to the OpenUSD row-vector convention, so applying a batch
 * never parses caller memory.
 */
struct OpenUsdPhysicsOverrideEntry
{
    SdfPath path;
    GfMatrix4d transform{1.0};
    uint64_t object_id = 0;
    bool preserve_stretch = false;
};

/*
 * Recovers the symmetric scale-shear factor of a rendered transform.
 *
 * GfMatrix4d::Factor yields M = r * s * -r * u * t, so r * s * -r is the
 * symmetric left polar factor carrying every authored scale and shear while u
 * is the authored rotation a simulated pose replaces. Matrices are applied as
 * v * M here, so composing stretch * simulated keeps the authored shape in the
 * body frame and replaces only the authored rotation and translation.
 *
 * Returns false for a non-finite or overflowing basis so the caller keeps the
 * unstretched simulated pose instead of rendering a NaN. A singular basis
 * factors with clamped scales and keeps its collapsed axes collapsed.
 */
inline bool OpenUsdPhysicsExtractStretch(
    const GfMatrix4d& authored,
    GfMatrix4d* stretch)
{
    for (int row = 0; row < 3; ++row)
    {
        for (int column = 0; column < 3; ++column)
        {
            if (!std::isfinite(authored[row][column]))
            {
                return false;
            }
        }
    }

    GfMatrix4d rotation(1.0);
    GfVec3d scale(1.0);
    GfMatrix4d orientation(1.0);
    GfVec3d translation(0.0);
    GfMatrix4d perspective(1.0);
    // A singular basis still factors into usable pieces with clamped scales, so
    // the return value is deliberately not treated as a failure here.
    authored.Factor(&rotation, &scale, &orientation, &translation, &perspective);
    GfMatrix4d diagonal(1.0);
    diagonal.SetScale(scale);
    const GfMatrix4d factored = rotation * diagonal * rotation.GetTranspose();

    GfMatrix4d result(1.0);
    for (int row = 0; row < 3; ++row)
    {
        for (int column = 0; column < 3; ++column)
        {
            const double element = factored[row][column];
            if (!std::isfinite(element))
            {
                return false;
            }

            result[row][column] = element;
        }
    }

    *stretch = result;
    return true;
}

/*
 * One validated deformation region. The path is already parsed and the points
 * are already converted to the value type Hydra consumes, so applying a batch
 * never parses caller memory.
 */
struct OpenUsdPhysicsDeformationEntry
{
    SdfPath path;
    VtVec3fArray points;
    uint64_t object_id = 0;
    uint64_t topology_revision = 0;
};

/*
 * The bounds one deformed prim renders with.
 *
 * Replacing a prim's points invalidates its authored extent by construction: a
 * body that deforms outside its rest pose bounds would be frustum culled while
 * its simulated geometry is on screen, and anything derived from bounds - fit
 * to view, the scene bounding box - would be computed from geometry that is not
 * being drawn. The extent is therefore recomputed from the accepted points and
 * published beside them.
 */
struct OpenUsdPhysicsDeformationBounds
{
    GfVec3f minimum{0.0f, 0.0f, 0.0f};
    GfVec3f maximum{0.0f, 0.0f, 0.0f};
    bool valid = false;
};

inline OpenUsdPhysicsDeformationBounds OpenUsdPhysicsComputeBounds(
    const VtVec3fArray& points)
{
    OpenUsdPhysicsDeformationBounds bounds;
    if (points.empty())
    {
        return bounds;
    }
    bounds.minimum = points[0];
    bounds.maximum = points[0];
    for (const GfVec3f& point : points)
    {
        for (int axis = 0; axis < 3; ++axis)
        {
            bounds.minimum[axis] = std::min(bounds.minimum[axis], point[axis]);
            bounds.maximum[axis] = std::max(bounds.maximum[axis], point[axis]);
        }
    }
    bounds.valid = true;
    return bounds;
}

/*
 * Renderer-neutral applied-state counters mirrored into the C ABI diagnostics
 * struct by the owning renderer.
 */
struct OpenUsdPhysicsOverrideCounters
{
    uint32_t applied_count = 0;
    uint32_t unresolved_count = 0;
    uint32_t dropped_count = 0;
    uint32_t unsupported_count = 0;
    uint64_t revision = 0;
    uint64_t applied_batch_count = 0;
    uint64_t rejected_batch_count = 0;
    uint64_t dirtied_prim_count = 0;
};

/*
 * The same shape for the deformation channel, plus the one refusal only
 * geometry can produce: a region whose point count the rendered prim's element
 * topology cannot accept.
 */
struct OpenUsdPhysicsDeformationCounters
{
    uint32_t applied_count = 0;
    uint32_t unresolved_count = 0;
    uint32_t dropped_count = 0;
    uint32_t unsupported_count = 0;
    uint32_t mismatched_count = 0;
    uint64_t revision = 0;
    uint64_t applied_batch_count = 0;
    uint64_t rejected_batch_count = 0;
    uint64_t dirtied_prim_count = 0;
};

TF_DECLARE_REF_PTRS(OpenUsdPhysicsOverrideSceneIndex);

/*
 * Replaces the xform of individual rendered prims with an externally simulated
 * world transform. The stage is never authored: the override lives only in the
 * Hydra scene index graph, so dropping it restores the authored transform.
 *
 * GetPrim is called by Hydra from arbitrary sync threads while the owning
 * render thread applies batches, so the retained table is guarded by a shared
 * mutex and the empty case is a lock-free atomic check.
 */
class OpenUsdPhysicsOverrideSceneIndex final
    : public HdSingleInputFilteringSceneIndexBase
{
public:
    static OpenUsdPhysicsOverrideSceneIndexRefPtr New(
        const HdSceneIndexBaseRefPtr& inputScene)
    {
        return TfCreateRefPtr(new OpenUsdPhysicsOverrideSceneIndex(inputScene));
    }

    HdSceneIndexPrim GetPrim(const SdfPath& primPath) const override
    {
        HdSceneIndexPrim prim = _GetInputSceneIndex()->GetPrim(primPath);
        if (!prim.dataSource)
        {
            return prim;
        }

        const bool hasTransforms = _count.load(std::memory_order_acquire) != 0;
        const bool hasDeformations =
            _deformationCount.load(std::memory_order_acquire) != 0;
        if (!hasTransforms && !hasDeformations)
        {
            return prim;
        }

        GfMatrix4d transform(1.0);
        bool overridden = false;
        VtVec3fArray points;
        bool deformed = false;
        OpenUsdPhysicsDeformationBounds bounds;
        {
            std::shared_lock<std::shared_mutex> lock(_gate);
            if (hasTransforms)
            {
                const auto entry = _overrides.find(primPath);
                if (entry != _overrides.end())
                {
                    transform = entry->second;
                    overridden = true;
                }
            }
            if (hasDeformations)
            {
                const auto entry = _deformations.find(primPath);
                if (entry != _deformations.end())
                {
                    points = entry->second.points;
                    bounds = entry->second.bounds;
                    deformed = true;
                }
            }
        }

        if (!overridden && !deformed)
        {
            return prim;
        }

        // The simulated points replace only the points primvar, so the prim
        // keeps every other primvar, its topology, its material, and its own
        // transform. A transform override may drive the same prim in the same
        // frame; both are overlaid onto one data source so neither drops the
        // other.
        if (deformed)
        {
            prim.dataSource = HdOverlayContainerDataSource::New(
                HdRetainedContainerDataSource::New(
                    HdPrimvarsSchemaTokens->primvars,
                    HdRetainedContainerDataSource::New(
                        HdPrimvarsSchemaTokens->points,
                        HdPrimvarSchema::Builder()
                            .SetPrimvarValue(
                                HdRetainedTypedSampledDataSource<VtVec3fArray>::New(
                                    points))
                            .SetInterpolation(
                                HdPrimvarSchema::
                                    BuildInterpolationDataSource(
                                        HdPrimvarSchemaTokens->vertex))
                            .SetRole(
                                HdPrimvarSchema::BuildRoleDataSource(
                                    HdPrimvarSchemaTokens->point))
                            .Build())),
                prim.dataSource);
            if (bounds.valid)
            {
                prim.dataSource = HdOverlayContainerDataSource::New(
                    HdRetainedContainerDataSource::New(
                        HdExtentSchemaTokens->extent,
                        HdExtentSchema::Builder()
                            .SetMin(
                                HdRetainedTypedSampledDataSource<GfVec3d>::New(
                                    GfVec3d(bounds.minimum)))
                            .SetMax(
                                HdRetainedTypedSampledDataSource<GfVec3d>::New(
                                    GfVec3d(bounds.maximum)))
                            .Build()),
                    prim.dataSource);
            }
        }

        if (overridden)
        {
            prim.dataSource = HdOverlayContainerDataSource::New(
                HdRetainedContainerDataSource::New(
                    HdXformSchemaTokens->xform,
                    HdXformSchema::Builder()
                        .SetMatrix(
                            HdRetainedTypedSampledDataSource<GfMatrix4d>::New(
                                transform))
                        .SetResetXformStack(
                            HdRetainedTypedSampledDataSource<bool>::New(true))
                        .Build()),
                prim.dataSource);
        }

        return prim;
    }

    /*
     * Reports how many vertices the rendered prim currently draws, which is the
     * only bound a deformation region has to agree with. A prim whose points the
     * input scene does not expose returns zero, and the caller refuses the
     * region rather than guessing.
     */
    size_t GetRenderedPointCount(const SdfPath& primPath) const
    {
        const HdSceneIndexPrim prim = _GetInputSceneIndex()->GetPrim(primPath);
        if (!prim.dataSource)
        {
            return 0;
        }

        HdPrimvarsSchema primvars = HdPrimvarsSchema::GetFromParent(prim.dataSource);
        HdPrimvarSchema points = primvars.GetPrimvar(HdPrimvarsSchemaTokens->points);
        if (!points)
        {
            return 0;
        }

        HdSampledDataSourceHandle value = points.GetPrimvarValue();
        if (!value)
        {
            return 0;
        }

        const VtValue authored = value->GetValue(0.0);
        if (!authored.IsHolding<VtVec3fArray>())
        {
            return 0;
        }

        return authored.UncheckedGet<VtVec3fArray>().size();
    }

    SdfPathVector GetChildPrimPaths(const SdfPath& primPath) const override
    {
        return _GetInputSceneIndex()->GetChildPrimPaths(primPath);
    }

    /*
     * Replaces every retained override with the supplied batch and dirties the
     * exact prims whose effective transform changed. Entries must already be
     * validated; unresolved and unsupported counts are supplied by the caller
     * because only it can classify caller-supplied items.
     */
    void ApplyBatch(
        const std::vector<OpenUsdPhysicsOverrideEntry>& entries,
        uint64_t revision,
        uint32_t unresolved_count,
        uint32_t dropped_count,
        uint32_t unsupported_count)
    {
        // Reading the input scene must happen outside the gate: it can call back
        // into arbitrary scene index code that in turn queries this filter.
        std::vector<GfMatrix4d> composed;
        composed.reserve(entries.size());
        for (const auto& entry : entries)
        {
            composed.push_back(_Compose(entry));
        }

        std::vector<SdfPath> dirtied;
        {
            std::unique_lock<std::shared_mutex> lock(_gate);
            dirtied.reserve(entries.size() + _overrides.size());
            for (const auto& retained : _overrides)
            {
                dirtied.push_back(retained.first);
            }
            _overrides.clear();
            for (size_t index = 0; index < entries.size(); ++index)
            {
                _overrides[entries[index].path] = composed[index];
            }
            for (const auto& entry : entries)
            {
                dirtied.push_back(entry.path);
            }
            _count.store(_overrides.size(), std::memory_order_release);
            _counters.applied_count = static_cast<uint32_t>(_overrides.size());
            _counters.unresolved_count = unresolved_count;
            _counters.dropped_count = dropped_count;
            _counters.unsupported_count = unsupported_count;
            _counters.revision = revision;
            ++_counters.applied_batch_count;
        }
        _DirtyTransforms(dirtied);
    }

    /* Drops every override so the authored transforms render again. */
    void ClearOverrides()
    {
        std::vector<SdfPath> dirtied;
        {
            std::unique_lock<std::shared_mutex> lock(_gate);
            dirtied.reserve(_overrides.size());
            for (const auto& retained : _overrides)
            {
                dirtied.push_back(retained.first);
            }
            _overrides.clear();
            _count.store(0, std::memory_order_release);
            _counters.applied_count = 0;
            _counters.unresolved_count = 0;
            _counters.dropped_count = 0;
            _counters.unsupported_count = 0;
        }
        _DirtyTransforms(dirtied);
    }

    /*
     * Replaces every retained deformation with the supplied batch and dirties
     * the exact prims whose effective points changed. Entries must already be
     * validated; the classification counts are supplied by the caller because
     * only it can classify caller-supplied regions.
     */
    void ApplyDeformationBatch(
        const std::vector<OpenUsdPhysicsDeformationEntry>& entries,
        uint64_t revision,
        uint32_t unresolved_count,
        uint32_t dropped_count,
        uint32_t unsupported_count,
        uint32_t mismatched_count)
    {
        std::vector<SdfPath> dirtied;
        {
            std::unique_lock<std::shared_mutex> lock(_gate);
            dirtied.reserve(entries.size() + _deformations.size());
            for (const auto& retained : _deformations)
            {
                dirtied.push_back(retained.first);
            }
            _deformations.clear();
            for (const auto& entry : entries)
            {
                _deformations[entry.path] =
                    RetainedDeformation{entry.points, OpenUsdPhysicsComputeBounds(entry.points)};
            }
            for (const auto& entry : entries)
            {
                dirtied.push_back(entry.path);
            }
            _deformationCount.store(_deformations.size(), std::memory_order_release);
            _deformationCounters.applied_count =
                static_cast<uint32_t>(_deformations.size());
            _deformationCounters.unresolved_count = unresolved_count;
            _deformationCounters.dropped_count = dropped_count;
            _deformationCounters.unsupported_count = unsupported_count;
            _deformationCounters.mismatched_count = mismatched_count;
            _deformationCounters.revision = revision;
            ++_deformationCounters.applied_batch_count;
        }
        _DirtyPoints(dirtied);
    }

    /* Drops every deformation so the authored points render again. */
    void ClearDeformations()
    {
        std::vector<SdfPath> dirtied;
        {
            std::unique_lock<std::shared_mutex> lock(_gate);
            dirtied.reserve(_deformations.size());
            for (const auto& retained : _deformations)
            {
                dirtied.push_back(retained.first);
            }
            _deformations.clear();
            _deformationCount.store(0, std::memory_order_release);
            _deformationCounters.applied_count = 0;
            _deformationCounters.unresolved_count = 0;
            _deformationCounters.dropped_count = 0;
            _deformationCounters.unsupported_count = 0;
            _deformationCounters.mismatched_count = 0;
        }
        _DirtyPoints(dirtied);
    }

    void RecordRejectedDeformationBatch()
    {
        std::unique_lock<std::shared_mutex> lock(_gate);
        ++_deformationCounters.rejected_batch_count;
    }

    OpenUsdPhysicsDeformationCounters GetDeformationCounters() const
    {
        std::shared_lock<std::shared_mutex> lock(_gate);
        return _deformationCounters;
    }

    bool HasDeformation(const SdfPath& primPath) const
    {
        std::shared_lock<std::shared_mutex> lock(_gate);
        return _deformations.find(primPath) != _deformations.end();
    }

    void RecordRejectedBatch()
    {
        std::unique_lock<std::shared_mutex> lock(_gate);
        ++_counters.rejected_batch_count;
    }

    OpenUsdPhysicsOverrideCounters GetCounters() const
    {
        std::shared_lock<std::shared_mutex> lock(_gate);
        return _counters;
    }

    bool HasOverride(const SdfPath& primPath) const
    {
        std::shared_lock<std::shared_mutex> lock(_gate);
        return _overrides.find(primPath) != _overrides.end();
    }

protected:
    void _PrimsAdded(
        const HdSceneIndexBase&,
        const HdSceneIndexObserver::AddedPrimEntries& entries) override
    {
        _SendPrimsAdded(entries);
    }

    void _PrimsRemoved(
        const HdSceneIndexBase&,
        const HdSceneIndexObserver::RemovedPrimEntries& entries) override
    {
        if (_count.load(std::memory_order_acquire) != 0 ||
            _deformationCount.load(std::memory_order_acquire) != 0)
        {
            std::unique_lock<std::shared_mutex> lock(_gate);
            for (const auto& entry : entries)
            {
                for (auto retained = _overrides.begin();
                     retained != _overrides.end();)
                {
                    retained = retained->first.HasPrefix(entry.primPath)
                        ? _overrides.erase(retained)
                        : std::next(retained);
                }
                for (auto retained = _deformations.begin();
                     retained != _deformations.end();)
                {
                    retained = retained->first.HasPrefix(entry.primPath)
                        ? _deformations.erase(retained)
                        : std::next(retained);
                }
            }
            _count.store(_overrides.size(), std::memory_order_release);
            _counters.applied_count = static_cast<uint32_t>(_overrides.size());
            _deformationCount.store(_deformations.size(), std::memory_order_release);
            _deformationCounters.applied_count =
                static_cast<uint32_t>(_deformations.size());
        }
        _SendPrimsRemoved(entries);
    }

    void _PrimsDirtied(
        const HdSceneIndexBase&,
        const HdSceneIndexObserver::DirtiedPrimEntries& entries) override
    {
        _SendPrimsDirtied(entries);
    }

private:
    explicit OpenUsdPhysicsOverrideSceneIndex(
        const HdSceneIndexBaseRefPtr& inputScene)
        : HdSingleInputFilteringSceneIndexBase(inputScene)
    {
    }

    /*
     * Resolves the transform an entry actually renders with. Entries that ask to
     * keep the rendered prim's scale and shear are recomposed against the input
     * scene's own xform, so an authored scaled or sheared prim keeps its shape
     * while the simulated rotation and translation take over. Nothing is read
     * back into the caller and nothing is authored onto the stage.
     */
    GfMatrix4d _Compose(const OpenUsdPhysicsOverrideEntry& entry) const
    {
        if (!entry.preserve_stretch)
        {
            return entry.transform;
        }

        const HdSceneIndexPrim prim = _GetInputSceneIndex()->GetPrim(entry.path);
        if (!prim.dataSource)
        {
            return entry.transform;
        }

        const HdMatrixDataSourceHandle authored =
            HdXformSchema::GetFromParent(prim.dataSource).GetMatrix();
        if (!authored)
        {
            return entry.transform;
        }

        GfMatrix4d stretch(1.0);
        if (!OpenUsdPhysicsExtractStretch(authored->GetTypedValue(0.0), &stretch))
        {
            return entry.transform;
        }

        const GfMatrix4d composed = stretch * entry.transform;
        for (int row = 0; row < 4; ++row)
        {
            for (int column = 0; column < 4; ++column)
            {
                if (!std::isfinite(composed[row][column]))
                {
                    return entry.transform;
                }
            }
        }

        return composed;
    }

    void _DirtyTransforms(const std::vector<SdfPath>& dirtied)
    {
        if (dirtied.empty())
        {
            return;
        }
        HdSceneIndexObserver::DirtiedPrimEntries notices;
        notices.reserve(dirtied.size());
        const HdDataSourceLocatorSet locators{HdXformSchema::GetDefaultLocator()};
        for (const SdfPath& path : dirtied)
        {
            notices.emplace_back(path, locators);
        }
        {
            std::unique_lock<std::shared_mutex> lock(_gate);
            _counters.dirtied_prim_count += notices.size();
        }
        _SendPrimsDirtied(notices);
    }

    /* One retained deformation: the simulated points and the bounds they imply. */
    struct RetainedDeformation
    {
        VtVec3fArray points;
        OpenUsdPhysicsDeformationBounds bounds;
    };

    void _DirtyPoints(const std::vector<SdfPath>& dirtied)
    {
        if (dirtied.empty())
        {
            return;
        }
        HdSceneIndexObserver::DirtiedPrimEntries notices;
        notices.reserve(dirtied.size());
        // Replacing points changes the bounds, so both locators are dirtied
        // together; dirtying only the points would leave Storm culling against
        // the authored rest pose extent.
        const HdDataSourceLocatorSet locators{
            HdPrimvarsSchema::GetPointsLocator(),
            HdExtentSchema::GetDefaultLocator()};
        for (const SdfPath& path : dirtied)
        {
            notices.emplace_back(path, locators);
        }
        {
            std::unique_lock<std::shared_mutex> lock(_gate);
            _deformationCounters.dirtied_prim_count += notices.size();
        }
        _SendPrimsDirtied(notices);
    }

    mutable std::shared_mutex _gate;
    std::unordered_map<SdfPath, GfMatrix4d, SdfPath::Hash> _overrides;
    std::unordered_map<SdfPath, RetainedDeformation, SdfPath::Hash> _deformations;
    std::atomic_size_t _count{0};
    std::atomic_size_t _deformationCount{0};
    OpenUsdPhysicsOverrideCounters _counters;
    OpenUsdPhysicsDeformationCounters _deformationCounters;
};

/*
 * Installs the override scene index into exactly the Hydra graph being created
 * on this thread. The registry callback is registered once per process for all
 * renderers, and a thread-local capture slot binds the created scene index to
 * the renderer whose engine construction requested it. Engines created by any
 * other component observe an unchanged input scene.
 */
class OpenUsdPhysicsOverrideSceneIndexRegistrar
{
public:
    static void EnsureRegistered()
    {
        static std::once_flag once;
        std::call_once(once, []()
        {
            HdSceneIndexPluginRegistry::GetInstance()
                .RegisterSceneIndexForRenderer(
                    std::string(),
                    &OpenUsdPhysicsOverrideSceneIndexRegistrar::_Append,
                    nullptr,
                    /* insertionPhase = */ 1000,
                    HdSceneIndexPluginRegistry::InsertionOrderAtEnd);
        });
    }

    class Capture
    {
    public:
        Capture()
        {
            OpenUsdPhysicsOverrideSceneIndexRegistrar::EnsureRegistered();
            _Capturing() = true;
            _Captured() = nullptr;
        }

        Capture(const Capture&) = delete;
        Capture& operator=(const Capture&) = delete;

        ~Capture()
        {
            _Capturing() = false;
            _Captured() = nullptr;
        }

        OpenUsdPhysicsOverrideSceneIndexRefPtr Take() const
        {
            return _Captured();
        }
    };

private:
    static bool& _Capturing()
    {
        static thread_local bool capturing = false;
        return capturing;
    }

    static OpenUsdPhysicsOverrideSceneIndexRefPtr& _Captured()
    {
        static thread_local OpenUsdPhysicsOverrideSceneIndexRefPtr captured;
        return captured;
    }

    static HdSceneIndexBaseRefPtr _Append(
        const std::string&,
        const HdSceneIndexBaseRefPtr& inputScene,
        const HdContainerDataSourceHandle&)
    {
        if (!_Capturing() || _Captured())
        {
            return inputScene;
        }
        OpenUsdPhysicsOverrideSceneIndexRefPtr sceneIndex =
            OpenUsdPhysicsOverrideSceneIndex::New(inputScene);
        _Captured() = sceneIndex;
        return sceneIndex;
    }
};

PXR_NAMESPACE_CLOSE_SCOPE

#endif
