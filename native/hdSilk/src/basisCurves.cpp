// Copyright (c) marcschier. Licensed under the MIT License.

#include "basisCurves.h"

#include "openusd_hdsilk.h"

#include "instancer.h"
#include "renderDelegate.h"
#include "sceneState.h"

#include "pxr/base/gf/vec3d.h"
#include "pxr/base/tf/diagnostic.h"
#include "pxr/base/vt/value.h"
#include "pxr/imaging/hd/changeTracker.h"
#include "pxr/imaging/hd/renderIndex.h"
#include "pxr/imaging/hd/sceneDelegate.h"
#include "pxr/imaging/hd/tokens.h"

#include <algorithm>
#include <limits>
#include <stdexcept>
#include <string>
#include <utility>

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
bool ExtractPoints(const VtValue& value, VtVec3fArray* out)
{
    if (value.IsHolding<VtVec3fArray>())
    {
        *out = value.UncheckedGet<VtVec3fArray>();
        return true;
    }
    if (value.IsHolding<VtVec3dArray>())
    {
        const VtVec3dArray& source = value.UncheckedGet<VtVec3dArray>();
        VtVec3fArray converted(source.size());
        for (size_t i = 0; i < source.size(); ++i)
        {
            converted[i] = GfVec3f(source[i]);
        }
        *out = std::move(converted);
        return true;
    }
    return false;
}

/// Reads the interpolation Hydra declares for the "widths" primvar. Curves that
/// reach hdSilk through UsdImaging always declare one; the size-based inference
/// in HdSilkResolveCurveWidths covers delegates that publish widths without a
/// usable descriptor.
bool FindAuthoredWidthInterpolation(
    HdSceneDelegate* sceneDelegate,
    const SdfPath& id,
    HdSilkCurveWidthInterpolation* interpolation)
{
    const HdInterpolation candidates[] = {
        HdInterpolationConstant,
        HdInterpolationUniform,
        HdInterpolationVarying,
        HdInterpolationVertex};
    for (HdInterpolation candidate : candidates)
    {
        for (const HdPrimvarDescriptor& descriptor :
            sceneDelegate->GetPrimvarDescriptors(id, candidate))
        {
            if (descriptor.name != HdTokens->widths)
            {
                continue;
            }
            if (candidate == HdInterpolationConstant)
            {
                *interpolation = HdSilkCurveWidthInterpolation::Constant;
            }
            else if (candidate == HdInterpolationUniform)
            {
                *interpolation = HdSilkCurveWidthInterpolation::Uniform;
            }
            else
            {
                *interpolation = HdSilkCurveWidthInterpolation::Vertex;
            }
            return true;
        }
    }
    return false;
}

bool ExtractDisplayColor(const VtValue& value, GfVec3f* color)
{
    if (value.IsHolding<GfVec3f>())
    {
        *color = value.UncheckedGet<GfVec3f>();
        return true;
    }
    if (value.IsHolding<VtVec3fArray>())
    {
        const VtVec3fArray& colors = value.UncheckedGet<VtVec3fArray>();
        if (!colors.empty())
        {
            *color = colors.front();
            return true;
        }
    }
    return false;
}
}

HdSilkBasisCurves::HdSilkBasisCurves(SdfPath const& id)
    : HdBasisCurves(id)
    , _transform(1.0)
{
}

HdDirtyBits
HdSilkBasisCurves::GetInitialDirtyBitsMask() const
{
    return HdChangeTracker::Clean
        | HdChangeTracker::InitRepr
        | HdChangeTracker::DirtyPoints
        | HdChangeTracker::DirtyTopology
        | HdChangeTracker::DirtyWidths
        | HdChangeTracker::DirtyTransform
        | HdChangeTracker::DirtyVisibility
        | HdChangeTracker::DirtyPrimvar
        | HdChangeTracker::DirtyInstancer
        | HdChangeTracker::DirtyInstanceIndex;
}

HdDirtyBits
HdSilkBasisCurves::_PropagateDirtyBits(HdDirtyBits bits) const
{
    return bits;
}

void
HdSilkBasisCurves::_InitRepr(TfToken const& reprToken, HdDirtyBits* /*dirtyBits*/)
{
    _ReprVector::iterator it = std::find_if(
        _reprs.begin(), _reprs.end(), _ReprComparator(reprToken));
    if (it == _reprs.end())
    {
        _reprs.emplace_back(reprToken, HdReprSharedPtr());
    }
}

void
HdSilkBasisCurves::Sync(
    HdSceneDelegate* sceneDelegate,
    HdRenderParam* renderParam,
    HdDirtyBits* dirtyBits,
    TfToken const& /*reprToken*/)
{
    SdfPath const& id = GetId();

    const bool visibilityDirty =
        HdChangeTracker::IsVisibilityDirty(*dirtyBits, id);
    if (visibilityDirty)
    {
        _UpdateVisibility(sceneDelegate, dirtyBits);
    }

    const bool topologyDirty = HdChangeTracker::IsTopologyDirty(*dirtyBits, id);
    const bool pointsDirty =
        HdChangeTracker::IsPrimvarDirty(*dirtyBits, id, HdTokens->points);
    const bool widthsDirty = ((*dirtyBits & HdChangeTracker::DirtyWidths) != 0) ||
        HdChangeTracker::IsPrimvarDirty(*dirtyBits, id, HdTokens->widths);
    const bool displayColorDirty =
        HdChangeTracker::IsPrimvarDirty(*dirtyBits, id, HdTokens->displayColor);
    const bool primvarsDirty = (*dirtyBits & HdChangeTracker::DirtyPrimvar) != 0;
    const bool transformDirty = HdChangeTracker::IsTransformDirty(*dirtyBits, id);

    const bool topologyRefreshed = topologyDirty || _topologyRevision == 0;
    if (topologyRefreshed)
    {
        _topology = GetBasisCurvesTopology(sceneDelegate);
        _topologySupported = _topology.GetCurveType() == HdTokens->linear &&
            _topology.GetCurveWrap() == HdTokens->segmented;
        if (_topologySupported)
        {
            if (_topologyRevision == std::numeric_limits<uint64_t>::max())
            {
                throw std::overflow_error(
                    "The hdSilk basisCurves topology revision is exhausted.");
            }
            ++_topologyRevision;
        }
        else
        {
            TF_WARN(
                "hdSilk skipped basisCurves '%s': unsupported topology type='%s' wrap='%s' (only linear segmented is supported)",
                id.GetText(),
                _topology.GetCurveType().GetText(),
                _topology.GetCurveWrap().GetText());
        }
    }
    if (transformDirty)
    {
        _transform = sceneDelegate->GetTransform(id);
    }
    if (pointsDirty || topologyRefreshed)
    {
        ExtractPoints(sceneDelegate->Get(id, HdTokens->points), &_points);
    }
    // Vertex widths are validated and indexed against the resolved point array,
    // so a points change alone still invalidates the resolution.
    if (widthsDirty || pointsDirty || topologyRefreshed)
    {
        HdSilkCurveWidthInterpolation declared =
            HdSilkCurveWidthInterpolation::Constant;
        const bool hasDeclared =
            FindAuthoredWidthInterpolation(sceneDelegate, id, &declared);
        if (!HdSilkResolveCurveWidths(
                _topology,
                _points.size(),
                sceneDelegate->Get(id, HdTokens->widths),
                hasDeclared,
                declared,
                &_widths))
        {
            // Storm still rasterizes such a curve, so falling back to the
            // UsdGeomCurves default width keeps the geometry rather than
            // deleting the prim over unusable width data.
            _widths.interpolation = HdSilkCurveWidthInterpolation::Constant;
            _widths.values.assign(1, HdSilkDefaultCurveWidth);
            TF_WARN(
                "hdSilk used the default width for basisCurves '%s': authored widths are unusable for this topology",
                id.GetText());
        }
    }
    if (displayColorDirty || primvarsDirty)
    {
        ExtractDisplayColor(sceneDelegate->Get(id, HdTokens->displayColor), &_displayColor);
    }

    _UpdateInstancer(sceneDelegate, dirtyBits);
    HdInstancer::_SyncInstancerAndParents(
        sceneDelegate->GetRenderIndex(),
        GetInstancerId());

    const bool instancerDirty =
        HdChangeTracker::IsInstancerDirty(*dirtyBits, id) ||
        HdChangeTracker::IsInstanceIndexDirty(*dirtyBits, id);

    HdSilkRenderParam* silkRenderParam =
        static_cast<HdSilkRenderParam*>(renderParam);
    if (!IsVisible() || !_topologySupported)
    {
        silkRenderParam->GetSceneState().RemoveMesh(id.GetString());
        *dirtyBits = HdChangeTracker::Clean;
        return;
    }

    if (topologyRefreshed || pointsDirty || widthsDirty || transformDirty ||
        visibilityDirty || displayColorDirty || primvarsDirty || instancerDirty)
    {
        HdSilkMeshRecord record;
        record.path = id.GetString();
        record.primId = GetPrimId();
        record.topologyKind = OPENUSD_SILK_TOPOLOGY_LINE_LIST;
        record.topologyRevision = _topologyRevision;
        HdSilkFlattenMatrix(_transform, record.transform);
        record.displayColor[0] = _displayColor[0];
        record.displayColor[1] = _displayColor[1];
        record.displayColor[2] = _displayColor[2];

        // Storm draws linear basis curves as one-pixel GL lines at the parity
        // complexity used by hdSilk, so line-list topology matches its measured
        // rasterization by construction and no backend has to widen a line.
        // Authored widths therefore do not change the emitted line geometry;
        // they are resolved onto the emitted vertices -- constant, uniform,
        // varying, or vertex -- and published so a consumer sees the same
        // per-vertex widths Hydra resolved. Before this resolution existed a
        // non-constant width array deleted the whole prim, which is the one
        // case where widths did change what was drawn: nothing at all.
        if (!HdSilkBuildLinearSegmentedCurveLines(
                _topology, _points, _widths, &record))
        {
            TF_WARN(
                "hdSilk skipped basisCurves '%s': invalid or empty linear segmented topology",
                id.GetText());
            silkRenderParam->GetSceneState().RemoveMesh(id.GetString());
            *dirtyBits = HdChangeTracker::Clean;
            return;
        }

        const std::string path = record.path;
        std::vector<HdSilkMeshRecord> records =
            _BuildInstanceRecords(sceneDelegate, std::move(record));
        silkRenderParam->GetSceneState().ReplaceMeshInstances(
            path,
            std::move(records));
    }

    *dirtyBits = HdChangeTracker::Clean;
}

std::vector<HdSilkMeshRecord>
HdSilkBasisCurves::_BuildInstanceRecords(
    HdSceneDelegate* sceneDelegate,
    HdSilkMeshRecord record)
{
    const SdfPath& instancerId = GetInstancerId();
    if (instancerId.IsEmpty())
    {
        record.instanceId = 0;
        record.instanceIndex = 0;
        std::vector<HdSilkMeshRecord> records;
        records.push_back(std::move(record));
        return records;
    }

    HdInstancer* instancer =
        sceneDelegate->GetRenderIndex().GetInstancer(instancerId);
    if (instancer == nullptr)
    {
        return {};
    }

    // Curve prototypes carry their payload once, exactly as meshes do since ABI
    // v8: the lowest published instance index keeps the full record and every
    // later instance reuses it. The published index is the instance's own index
    // inside the instancer rather than its position in the resolved array, so
    // the payload rides the lowest index the prototype owns and not necessarily
    // index zero.
    const std::vector<HdSilkInstanceSample> samples =
        static_cast<HdSilkInstancer*>(instancer)->ComputeInstanceSamples(
            GetId());

    const int32_t instanceId = HdSilkStableInstanceId(instancerId.GetString());
    std::vector<HdSilkMeshRecord> records;
    records.reserve(samples.size());
    for (size_t position = 0; position < samples.size(); ++position)
    {
        const HdSilkInstanceSample& sample = samples[position];
        if (sample.index > static_cast<int64_t>(
                std::numeric_limits<int32_t>::max()))
        {
            throw std::overflow_error(
                "The hdSilk basisCurves instance index exceeds the 32-bit instance index.");
        }
        HdSilkMeshRecord instanceRecord = record;
        instanceRecord.instanceId = instanceId;
        instanceRecord.instanceIndex = static_cast<int32_t>(sample.index);
        HdSilkFlattenMatrix(
            _transform * sample.transform,
            instanceRecord.transform);
        if (position != 0)
        {
            instanceRecord.points.clear();
            instanceRecord.indices.clear();
            instanceRecord.triangleSubprims.clear();
            instanceRecord.materialPath.clear();
            instanceRecord.attributes.clear();
        }
        records.push_back(std::move(instanceRecord));
    }
    return records;
}

PXR_NAMESPACE_CLOSE_SCOPE
