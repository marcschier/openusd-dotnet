// Copyright (c) marcschier. Licensed under the MIT License.

#include "points.h"

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
}

HdSilkPoints::HdSilkPoints(SdfPath const& id)
    : HdPoints(id)
    , _transform(1.0)
{
}

HdDirtyBits
HdSilkPoints::GetInitialDirtyBitsMask() const
{
    return HdChangeTracker::Clean
        | HdChangeTracker::InitRepr
        | HdChangeTracker::DirtyPoints
        | HdChangeTracker::DirtyTransform
        | HdChangeTracker::DirtyVisibility
        | HdChangeTracker::DirtyPrimvar
        | HdChangeTracker::DirtyInstancer
        | HdChangeTracker::DirtyInstanceIndex;
}

HdDirtyBits
HdSilkPoints::_PropagateDirtyBits(HdDirtyBits bits) const
{
    return bits;
}

void
HdSilkPoints::_InitRepr(TfToken const& reprToken, HdDirtyBits* /*dirtyBits*/)
{
    _ReprVector::iterator it = std::find_if(
        _reprs.begin(), _reprs.end(), _ReprComparator(reprToken));
    if (it == _reprs.end())
    {
        _reprs.emplace_back(reprToken, HdReprSharedPtr());
    }
}

void
HdSilkPoints::Sync(
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

    const bool pointsDirty =
        HdChangeTracker::IsPrimvarDirty(*dirtyBits, id, HdTokens->points);
    const bool displayColorDirty =
        HdChangeTracker::IsPrimvarDirty(*dirtyBits, id, HdTokens->displayColor);
    const bool primvarsDirty = (*dirtyBits & HdChangeTracker::DirtyPrimvar) != 0;
    const bool transformDirty = HdChangeTracker::IsTransformDirty(*dirtyBits, id);

    if (pointsDirty)
    {
        ExtractPoints(sceneDelegate->Get(id, HdTokens->points), &_points);
        if (_topologyRevision == std::numeric_limits<uint64_t>::max())
        {
            throw std::overflow_error(
                "The hdSilk points topology revision is exhausted.");
        }
        ++_topologyRevision;
    }
    if (transformDirty)
    {
        _transform = sceneDelegate->GetTransform(id);
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
    if (!IsVisible())
    {
        silkRenderParam->GetSceneState().RemoveMesh(id.GetString());
        *dirtyBits = HdChangeTracker::Clean;
        return;
    }

    if (pointsDirty || transformDirty || visibilityDirty || displayColorDirty ||
        primvarsDirty || instancerDirty)
    {
        if (_points.empty())
        {
            TF_WARN("hdSilk skipped points '%s': empty points array", id.GetText());
            silkRenderParam->GetSceneState().RemoveMesh(id.GetString());
            *dirtyBits = HdChangeTracker::Clean;
            return;
        }

        HdSilkMeshRecord record;
        record.path = id.GetString();
        record.primId = GetPrimId();
        record.topologyKind = OPENUSD_SILK_TOPOLOGY_POINT_LIST;
        record.topologyRevision = _topologyRevision;
        HdSilkFlattenMatrix(_transform, record.transform);
        record.displayColor[0] = _displayColor[0];
        record.displayColor[1] = _displayColor[1];
        record.displayColor[2] = _displayColor[2];

        if (_points.size() > static_cast<size_t>(std::numeric_limits<uint32_t>::max()))
        {
            throw std::overflow_error(
                "The hdSilk points primitive exceeds the 32-bit vertex index.");
        }
        record.points.reserve(_points.size() * 3);
        record.indices.reserve(_points.size());
        record.triangleSubprims.reserve(_points.size());

        // A points prim emits one vertex per authored point, in authored order,
        // so point identity is exact and needs no mapping table beyond the
        // identity it publishes. There is no authored edge between two authored
        // points, so the edge target is refused by topology rather than left
        // to be inferred from the emitted point list.
        //
        // The ABI v22 identity budget is checked from the size the table WOULD
        // have, before anything is reserved or filled, exactly as the mesh
        // producer checks it. Checking after building it would make an oversized
        // point cloud pay the whole allocation the budget exists to refuse,
        // which is precisely the cost a hostile or merely enormous stage would
        // impose. A refusal drops only the exact point identity and names the
        // budget as the reason: the geometry is still published, so an
        // over-budget point cloud is still drawn, still occludes, and still
        // answers a prim pick.
        const bool identityWithinBudget =
            !HdSilkSubprimIdentityExceedsBudget(_points.size(), 0);
        if (identityWithinBudget)
        {
            record.pointOrigins.reserve(_points.size());
            record.subprimIdentity = OPENUSD_SILK_SUBPRIM_IDENTITY_POINT;
            record.subprimUnsupported =
                OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE;
        }
        else
        {
            record.subprimIdentity = OPENUSD_SILK_SUBPRIM_IDENTITY_NONE;
            record.subprimUnsupported =
                OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE |
                OPENUSD_SILK_SUBPRIM_UNSUPPORTED_BUDGET;
        }
        for (size_t pointIndex = 0; pointIndex < _points.size(); ++pointIndex)
        {
            const GfVec3f& point = _points[pointIndex];
            record.points.push_back(point[0]);
            record.points.push_back(point[1]);
            record.points.push_back(point[2]);
            record.indices.push_back(static_cast<uint32_t>(pointIndex));
            record.triangleSubprims.push_back(static_cast<uint32_t>(pointIndex));
            if (identityWithinBudget)
            {
                record.pointOrigins.push_back(
                    static_cast<uint32_t>(pointIndex));
            }
        }
        record.authoredPointCount = identityWithinBudget
            ? static_cast<uint32_t>(_points.size())
            : 0;
        AddEyeFacingNormals(&record);

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
HdSilkPoints::_BuildInstanceRecords(
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

    // Points prototypes carry their payload once, exactly as meshes do since
    // ABI v8: the lowest published instance index keeps the full record and
    // every later instance reuses it. The published index is the instance's own
    // index inside the instancer rather than its position in the resolved
    // array, so the payload rides the lowest index the prototype owns and not
    // necessarily index zero.
    const std::vector<HdSilkInstanceSample> samples =
        static_cast<HdSilkInstancer*>(instancer)->ComputeInstanceSamples(
            GetId());

    const int32_t instanceId = HdSilkStableInstanceId(instancerId.GetString());
    std::vector<HdSilkMeshRecord> records;
    records.reserve(samples.size());
    // Built once and copied per instance: it holds no geometry and no identity
    // table, so an instance reference never costs a copy of the prototype's
    // points.
    const HdSilkMeshRecord reference = HdSilkMakeInstanceReference(record);
    for (size_t position = 0; position < samples.size(); ++position)
    {
        const HdSilkInstanceSample& sample = samples[position];
        if (sample.index > static_cast<int64_t>(
                std::numeric_limits<int32_t>::max()))
        {
            throw std::overflow_error(
                "The hdSilk points instance index exceeds the 32-bit instance index.");
        }
        HdSilkMeshRecord instanceRecord =
            position == 0 ? std::move(record) : reference;
        instanceRecord.instanceId = instanceId;
        instanceRecord.instancerPath = instancerId.GetString();
        instanceRecord.instanceIndex = static_cast<int32_t>(sample.index);
        instanceRecord.instancerContext = sample.context;
        HdSilkFlattenMatrix(
            _transform * sample.transform,
            instanceRecord.transform);
        records.push_back(std::move(instanceRecord));
    }
    return records;
}

PXR_NAMESPACE_CLOSE_SCOPE
