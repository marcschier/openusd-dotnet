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

bool ExtractConstantWidth(const VtValue& value, float* width)
{
    if (value.IsEmpty())
    {
        *width = 1.0f;
        return true;
    }
    if (value.IsHolding<float>())
    {
        *width = value.UncheckedGet<float>();
        return true;
    }
    if (value.IsHolding<VtFloatArray>())
    {
        const VtFloatArray& widths = value.UncheckedGet<VtFloatArray>();
        if (widths.empty())
        {
            *width = 1.0f;
            return true;
        }
        if (widths.size() == 1)
        {
            *width = widths.front();
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

bool BuildLinearSegmentedLineList(
    const HdBasisCurvesTopology& topology,
    const VtVec3fArray& points,
    HdSilkMeshRecord* record)
{
    const VtIntArray& counts = topology.GetCurveVertexCounts();
    const VtIntArray& indices = topology.GetCurveIndices();
    const VtIntArray& invisibleCurves = topology.GetInvisibleCurves();
    const VtIntArray& invisiblePoints = topology.GetInvisiblePoints();

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
            record->triangleSubprims.push_back(segmentIndex);
            ++segmentIndex;
        }
        vertexCursor += static_cast<size_t>(count);
    }
    AddEyeFacingNormals(record);
    return !record->indices.empty();
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
        _widthSupported = ExtractConstantWidth(sceneDelegate->Get(id, HdTokens->widths), &_width);
        if (!_widthSupported)
        {
            TF_WARN(
                "hdSilk skipped basisCurves '%s': varying or unsupported widths are not supported",
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
    if (!IsVisible() || !_topologySupported || !_widthSupported)
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
        // complexity used by hdSilk. Publishing line-list topology matches that
        // behavior directly and intentionally ignores authored world-space
        // widths, while the extracted width still gates unsupported varying
        // width data above.
        if (!BuildLinearSegmentedLineList(_topology, _points, &record))
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
