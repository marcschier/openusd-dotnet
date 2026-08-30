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
#include <cmath>
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

/// The UsdGeomCurves width fallback, used when no usable widths are authored.
constexpr float DefaultCurveWidth = 1.0f;

using CurveWidthInterpolation = HdSilkCurveWidthInterpolation;
using CurveWidths = HdSilkCurveWidths;

bool ExtractWidthValues(const VtValue& value, std::vector<float>* out)
{
    out->clear();
    if (value.IsEmpty())
    {
        return true;
    }
    if (value.IsHolding<float>())
    {
        out->push_back(value.UncheckedGet<float>());
        return true;
    }
    if (value.IsHolding<double>())
    {
        out->push_back(static_cast<float>(value.UncheckedGet<double>()));
        return true;
    }
    if (value.IsHolding<VtFloatArray>())
    {
        const VtFloatArray& widths = value.UncheckedGet<VtFloatArray>();
        out->assign(widths.begin(), widths.end());
        return true;
    }
    if (value.IsHolding<VtDoubleArray>())
    {
        const VtDoubleArray& widths = value.UncheckedGet<VtDoubleArray>();
        out->reserve(widths.size());
        for (double width : widths)
        {
            out->push_back(static_cast<float>(width));
        }
        return true;
    }
    return false;
}

/// Clamps authored widths onto the non-negative finite range USD defines for
/// them. A non-finite width is rejected rather than clamped: it is authoring
/// corruption, not a legitimate zero-width curve.
bool SanitizeWidthValues(std::vector<float>* values)
{
    for (float& width : *values)
    {
        if (!std::isfinite(width))
        {
            return false;
        }
        width = std::max(width, 0.0f);
    }
    return true;
}

/// Reads the interpolation Hydra declares for the "widths" primvar. Curves that
/// reach hdSilk through UsdImaging always declare one; the size-based inference
/// below covers delegates that publish widths without a descriptor.
bool FindAuthoredWidthInterpolation(
    HdSceneDelegate* sceneDelegate,
    const SdfPath& id,
    CurveWidthInterpolation* interpolation)
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
                *interpolation = CurveWidthInterpolation::Constant;
            }
            else if (candidate == HdInterpolationUniform)
            {
                *interpolation = CurveWidthInterpolation::Uniform;
            }
            else
            {
                *interpolation = CurveWidthInterpolation::Vertex;
            }
            return true;
        }
    }
    return false;
}

size_t ExpectedWidthCount(
    const HdBasisCurvesTopology& topology,
    CurveWidthInterpolation interpolation)
{
    switch (interpolation)
    {
    case CurveWidthInterpolation::Uniform:
        return topology.GetCurveVertexCounts().size();
    case CurveWidthInterpolation::Vertex:
        return topology.CalculateNeededNumberOfControlPoints();
    case CurveWidthInterpolation::Constant:
    default:
        return 1;
    }
}

/// Matches authored widths to an interpolation the emitted line list can
/// resolve. The declared interpolation wins whenever its element count agrees
/// with the topology; otherwise the count itself selects one, so a scene that
/// authors a per-curve or per-point array without a usable descriptor still
/// renders instead of disappearing.
bool ResolveCurveWidths(
    const HdBasisCurvesTopology& topology,
    const std::vector<float>& authored,
    bool hasDeclaredInterpolation,
    CurveWidthInterpolation declaredInterpolation,
    CurveWidths* out)
{
    if (authored.empty())
    {
        out->interpolation = CurveWidthInterpolation::Constant;
        out->values.assign(1, DefaultCurveWidth);
        return true;
    }
    if (hasDeclaredInterpolation &&
        authored.size() == ExpectedWidthCount(topology, declaredInterpolation))
    {
        out->interpolation = declaredInterpolation;
        out->values = authored;
        return true;
    }

    const CurveWidthInterpolation inferred[] = {
        CurveWidthInterpolation::Constant,
        CurveWidthInterpolation::Uniform,
        CurveWidthInterpolation::Vertex};
    for (CurveWidthInterpolation candidate : inferred)
    {
        if (authored.size() == ExpectedWidthCount(topology, candidate))
        {
            out->interpolation = candidate;
            out->values = authored;
            return true;
        }
    }
    return false;
}

/// Resolves the authored width for one emitted line endpoint. Vertex and
/// varying widths are indexed by the flattened control-point ordinal rather
/// than by the resolved point index: that ordinal is exactly what
/// CalculateNeededNumberOfControlPoints counts, so it stays correct for the
/// indexed topologies Hydra may hand to a delegate.
float ResolveWidthAt(
    const CurveWidths& widths,
    size_t curveIndex,
    size_t controlPointOrdinal)
{
    switch (widths.interpolation)
    {
    case CurveWidthInterpolation::Uniform:
        return widths.values[curveIndex];
    case CurveWidthInterpolation::Vertex:
        return widths.values[controlPointOrdinal];
    case CurveWidthInterpolation::Constant:
    default:
        return widths.values.front();
    }
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

bool IsInvisible(const VtIntArray& values, int value)
{
    return std::find(values.begin(), values.end(), value) != values.end();
}

void AddEyeFacingNormals(HdSilkMeshRecord* record)
{
    HdSilkMeshAttribute normals;
    normals.name = HdTokens->normals.GetString();
    normals.semantic = OPENUSD_SILK_ATTRIBUTE_NORMAL;
    normals.componentCount = 3;
    normals.interpolation = OPENUSD_SILK_INTERPOLATION_VERTEX;
    const size_t pointCount = record->points.size() / 3;
    normals.data.reserve(pointCount * 3);
    for (size_t point = 0; point < pointCount; ++point)
    {
        normals.data.push_back(0.0f);
        normals.data.push_back(0.0f);
        normals.data.push_back(1.0f);
    }
    record->attributes.push_back(std::move(normals));
}

/// Publishes the widths already resolved onto the emitted line vertices.
/// Constant widths collapse to a single wire element so an unchanged
/// constant-width scene keeps the payload it had before non-constant widths
/// were supported.
void AddResolvedWidths(
    const CurveWidths& widths,
    std::vector<float> emitted,
    HdSilkMeshRecord* record)
{
    HdSilkMeshAttribute attribute;
    attribute.name = HdTokens->widths.GetString();
    attribute.semantic = OPENUSD_SILK_ATTRIBUTE_WIDTH;
    attribute.componentCount = 1;
    if (widths.interpolation == CurveWidthInterpolation::Constant)
    {
        attribute.interpolation = OPENUSD_SILK_INTERPOLATION_CONSTANT;
        attribute.data.assign(1, widths.values.front());
    }
    else
    {
        attribute.interpolation = OPENUSD_SILK_INTERPOLATION_VERTEX;
        attribute.data = std::move(emitted);
    }
    record->attributes.push_back(std::move(attribute));
}

bool BuildLinearSegmentedLineList(
    const HdBasisCurvesTopology& topology,
    const VtVec3fArray& points,
    const CurveWidths& widths,
    HdSilkMeshRecord* record)
{
    const VtIntArray& counts = topology.GetCurveVertexCounts();
    const VtIntArray& indices = topology.GetCurveIndices();
    const VtIntArray& invisibleCurves = topology.GetInvisibleCurves();
    const VtIntArray& invisiblePoints = topology.GetInvisiblePoints();
    std::vector<float> emittedWidths;

    size_t vertexCursor = 0;
    uint32_t segmentIndex = 0;
    for (size_t curveIndex = 0; curveIndex < counts.size(); ++curveIndex)
    {
        const int count = counts[curveIndex];
        if (count < 0 || (count % 2) != 0)
        {
            return false;
        }
        if (vertexCursor > static_cast<size_t>(std::numeric_limits<int>::max()) ||
            static_cast<size_t>(count) >
                static_cast<size_t>(std::numeric_limits<int>::max()) - vertexCursor)
        {
            return false;
        }
        if (IsInvisible(invisibleCurves, static_cast<int>(curveIndex)))
        {
            vertexCursor += static_cast<size_t>(count);
            segmentIndex += static_cast<uint32_t>(count / 2);
            continue;
        }

        for (int local = 0; local < count; local += 2)
        {
            const size_t firstVertex = vertexCursor + static_cast<size_t>(local);
            const size_t secondVertex = firstVertex + 1;
            if (topology.HasIndices() && secondVertex >= indices.size())
            {
                return false;
            }
            const int firstIndex = topology.HasIndices()
                ? indices[firstVertex]
                : static_cast<int>(firstVertex);
            const int secondIndex = topology.HasIndices()
                ? indices[secondVertex]
                : static_cast<int>(secondVertex);
            if (firstIndex < 0 || secondIndex < 0 ||
                static_cast<size_t>(firstIndex) >= points.size() ||
                static_cast<size_t>(secondIndex) >= points.size())
            {
                return false;
            }
            if (IsInvisible(invisiblePoints, firstIndex) ||
                IsInvisible(invisiblePoints, secondIndex))
            {
                ++segmentIndex;
                continue;
            }

            if ((record->points.size() / 3) >
                static_cast<size_t>(std::numeric_limits<uint32_t>::max()) - 2)
            {
                throw std::overflow_error(
                    "The hdSilk basisCurves line list exceeds the 32-bit vertex index.");
            }
            const uint32_t base = static_cast<uint32_t>(record->points.size() / 3);
            const GfVec3f line[] = {
                points[static_cast<size_t>(firstIndex)],
                points[static_cast<size_t>(secondIndex)]};
            for (const GfVec3f& point : line)
            {
                record->points.push_back(point[0]);
                record->points.push_back(point[1]);
                record->points.push_back(point[2]);
            }
            record->indices.insert(
                record->indices.end(),
                {base, base + 1});
            emittedWidths.push_back(
                ResolveWidthAt(widths, curveIndex, firstVertex));
            emittedWidths.push_back(
                ResolveWidthAt(widths, curveIndex, secondVertex));
            record->triangleSubprims.push_back(segmentIndex);
            ++segmentIndex;
        }
        vertexCursor += static_cast<size_t>(count);
    }
    if (record->indices.empty())
    {
        return false;
    }
    AddEyeFacingNormals(record);
    AddResolvedWidths(widths, std::move(emittedWidths), record);
    return true;
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
    if (widthsDirty || topologyRefreshed)
    {
        std::vector<float> authored;
        CurveWidthInterpolation declared = CurveWidthInterpolation::Constant;
        const bool hasDeclared =
            FindAuthoredWidthInterpolation(sceneDelegate, id, &declared);
        const bool extracted =
            ExtractWidthValues(sceneDelegate->Get(id, HdTokens->widths), &authored) &&
            SanitizeWidthValues(&authored) &&
            ResolveCurveWidths(_topology, authored, hasDeclared, declared, &_widths);
        if (!extracted)
        {
            // Storm still rasterizes such a curve, so falling back to the
            // UsdGeomCurves default width keeps the geometry rather than
            // deleting the prim over unusable width data.
            _widths.interpolation = CurveWidthInterpolation::Constant;
            _widths.values.assign(1, DefaultCurveWidth);
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
        if (!BuildLinearSegmentedLineList(_topology, _points, _widths, &record))
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

    const VtMatrix4dArray instanceTransforms =
        static_cast<HdSilkInstancer*>(instancer)->ComputeInstanceTransforms(
            GetId());
    if (instanceTransforms.size() >
        static_cast<size_t>(std::numeric_limits<int32_t>::max()))
    {
        throw std::overflow_error(
            "The hdSilk basisCurves instance count exceeds the 32-bit instance index.");
    }

    const int32_t instanceId = HdSilkStableInstanceId(instancerId.GetString());
    std::vector<HdSilkMeshRecord> records;
    records.reserve(instanceTransforms.size());
    for (size_t index = 0; index < instanceTransforms.size(); ++index)
    {
        HdSilkMeshRecord instanceRecord = record;
        instanceRecord.instanceId = instanceId;
        instanceRecord.instanceIndex = static_cast<int32_t>(index);
        HdSilkFlattenMatrix(
            _transform * instanceTransforms[index],
            instanceRecord.transform);
        records.push_back(std::move(instanceRecord));
    }
    return records;
}

PXR_NAMESPACE_CLOSE_SCOPE
