// Copyright (c) marcschier. Licensed under the MIT License.

#include "mesh.h"

#include "renderDelegate.h"
#include "sceneState.h"

#include "pxr/base/gf/vec3f.h"
#include "pxr/base/gf/vec3i.h"
#include "pxr/base/vt/value.h"
#include "pxr/imaging/hd/changeTracker.h"
#include "pxr/imaging/hd/meshUtil.h"
#include "pxr/imaging/hd/tokens.h"

#include <algorithm>
#include <limits>
#include <stdexcept>
#include <utility>

PXR_NAMESPACE_OPEN_SCOPE

HdSilkMesh::HdSilkMesh(SdfPath const& id)
    : HdMesh(id)
    , _transform(1.0)
{
}

HdDirtyBits
HdSilkMesh::GetInitialDirtyBitsMask() const
{
    return HdChangeTracker::Clean
        | HdChangeTracker::InitRepr
        | HdChangeTracker::DirtyPoints
        | HdChangeTracker::DirtyTopology
        | HdChangeTracker::DirtyTransform
        | HdChangeTracker::DirtyVisibility
        | HdChangeTracker::DirtyPrimvar;
}

HdDirtyBits
HdSilkMesh::_PropagateDirtyBits(HdDirtyBits bits) const
{
    return bits;
}

void
HdSilkMesh::_InitRepr(TfToken const& reprToken, HdDirtyBits* /*dirtyBits*/)
{
    _ReprVector::iterator it = std::find_if(
        _reprs.begin(), _reprs.end(), _ReprComparator(reprToken));
    if (it == _reprs.end())
    {
        _reprs.emplace_back(reprToken, HdReprSharedPtr());
    }
}

void
HdSilkMesh::Sync(
    HdSceneDelegate* sceneDelegate,
    HdRenderParam* renderParam,
    HdDirtyBits* dirtyBits,
    TfToken const& /*reprToken*/)
{
    SdfPath const& id = GetId();

    if (HdChangeTracker::IsVisibilityDirty(*dirtyBits, id))
    {
        _UpdateVisibility(sceneDelegate, dirtyBits);
    }

    const bool topologyDirty = HdChangeTracker::IsTopologyDirty(*dirtyBits, id);
    const bool pointsDirty =
        HdChangeTracker::IsPrimvarDirty(*dirtyBits, id, HdTokens->points);
    const bool displayColorDirty =
        HdChangeTracker::IsPrimvarDirty(*dirtyBits, id, HdTokens->displayColor);
    const bool transformDirty = HdChangeTracker::IsTransformDirty(*dirtyBits, id);

    const bool topologyRefreshed = topologyDirty || _topologyRevision == 0;
    if (topologyRefreshed)
    {
        _topology = HdMeshTopology(GetMeshTopology(sceneDelegate));
        if (_topologyRevision == std::numeric_limits<uint64_t>::max())
        {
            throw std::overflow_error("The hdSilk mesh topology revision is exhausted.");
        }
        ++_topologyRevision;

        HdMeshUtil meshUtil(&_topology, id);
        VtVec3iArray triangleIndices;
        VtIntArray primitiveParams;
        meshUtil.ComputeTriangleIndices(&triangleIndices, &primitiveParams);
        if (primitiveParams.size() != triangleIndices.size())
        {
            throw std::runtime_error(
                "HdMeshUtil returned mismatched triangle and primitiveParams counts.");
        }
        if (triangleIndices.size() >
            std::numeric_limits<size_t>::max() / 3)
        {
            throw std::overflow_error(
                "The hdSilk triangle index component count overflows size_t.");
        }

        _triangleIndices.clear();
        _triangleIndices.reserve(triangleIndices.size() * 3);
        _triangleSubprims.clear();
        _triangleSubprims.reserve(primitiveParams.size());
        for (size_t triangleIndex = 0;
             triangleIndex < triangleIndices.size();
             ++triangleIndex)
        {
            const GfVec3i& triangle = triangleIndices[triangleIndex];
            for (int component = 0; component < 3; ++component)
            {
                if (triangle[component] < 0)
                {
                    throw std::runtime_error(
                        "HdMeshUtil returned a negative triangle vertex index.");
                }
                _triangleIndices.push_back(
                    static_cast<uint32_t>(triangle[component]));
            }

            const int faceIndex =
                HdMeshUtil::DecodeFaceIndexFromCoarseFaceParam(
                    primitiveParams[triangleIndex]);
            if (faceIndex < 0)
            {
                throw std::runtime_error(
                    "HdMeshUtil returned a negative authored face index.");
            }
            _triangleSubprims.push_back(static_cast<uint32_t>(faceIndex));
        }
    }
    if (transformDirty)
    {
        _transform = sceneDelegate->GetTransform(id);
    }
    if (pointsDirty)
    {
        const VtValue value = sceneDelegate->Get(id, HdTokens->points);
        if (value.IsHolding<VtVec3fArray>())
        {
            _points = value.UncheckedGet<VtVec3fArray>();
        }
    }
    if (displayColorDirty)
    {
        const VtValue value = sceneDelegate->Get(id, HdTokens->displayColor);
        if (value.IsHolding<VtVec3fArray>())
        {
            const VtVec3fArray& colors = value.UncheckedGet<VtVec3fArray>();
            if (!colors.empty())
            {
                _displayColor = colors.front();
            }
        }
        else if (value.IsHolding<GfVec3f>())
        {
            _displayColor = value.UncheckedGet<GfVec3f>();
        }
    }

    if (topologyRefreshed || pointsDirty || transformDirty || displayColorDirty)
    {
        HdSilkMeshRecord record;
        record.path = id.GetString();
        record.primId = GetPrimId();
        record.topologyRevision = _topologyRevision;
        HdSilkFlattenMatrix(_transform, record.transform);
        record.displayColor[0] = _displayColor[0];
        record.displayColor[1] = _displayColor[1];
        record.displayColor[2] = _displayColor[2];

        if (_points.size() > std::numeric_limits<size_t>::max() / 3)
        {
            throw std::overflow_error(
                "The hdSilk point component count overflows size_t.");
        }
        record.points.reserve(_points.size() * 3);
        for (const GfVec3f& point : _points)
        {
            record.points.push_back(point[0]);
            record.points.push_back(point[1]);
            record.points.push_back(point[2]);
        }

        record.indices = _triangleIndices;
        record.triangleSubprims = _triangleSubprims;

        // Capture the key before std::move(record); argument evaluation
        // order relative to the moved-from object is otherwise unspecified.
        const std::string path = record.path;
        HdSilkRenderParam* silkRenderParam = static_cast<HdSilkRenderParam*>(renderParam);
        silkRenderParam->GetSceneState().UpsertMesh(path, std::move(record));
    }

    *dirtyBits = HdChangeTracker::Clean;
}

PXR_NAMESPACE_CLOSE_SCOPE
