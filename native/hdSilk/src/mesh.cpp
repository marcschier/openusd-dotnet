// Copyright (c) marcschier. Licensed under the MIT License.

#include "mesh.h"

#include "openusd_hdsilk.h"

#include "instancer.h"
#include "renderDelegate.h"
#include "sceneState.h"

#include "pxr/base/gf/vec3f.h"
#include "pxr/base/gf/vec3i.h"
#include "pxr/base/vt/value.h"
#include "pxr/imaging/hd/changeTracker.h"
#include "pxr/imaging/hd/extComputationUtils.h"
#include "pxr/imaging/hd/meshUtil.h"
#include "pxr/imaging/hd/renderIndex.h"
#include "pxr/imaging/hd/sceneDelegate.h"
#include "pxr/imaging/hd/tokens.h"

#include <algorithm>
#include <limits>
#include <stdexcept>
#include <utility>

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
/// Points reach Hydra as float or double arrays depending on the authored
/// type. Returns false when the value holds neither, leaving the previous
/// points untouched.
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
}

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
        | HdChangeTracker::DirtyMaterialId
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
    const bool normalsDirty =
        HdChangeTracker::IsPrimvarDirty(*dirtyBits, id, HdTokens->normals);
    const bool transformDirty = HdChangeTracker::IsTransformDirty(*dirtyBits, id);
    const bool materialDirty =
        (*dirtyBits & HdChangeTracker::DirtyMaterialId) != 0;
    if (materialDirty)
    {
        // Hydra resolves the binding for us; the path is the only identity the
        // wire carries, exactly as it is for mesh identity.
        SetMaterialId(sceneDelegate->GetMaterialId(id));
    }

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

    // Skinned and otherwise procedurally deformed meshes publish "points" as
    // an ExtComputation output rather than an authored primvar, so a plain
    // Get() returns nothing and would leave the point array empty while the
    // topology is fully triangulated. Pull computed primvars first and let an
    // authored value win only when no computation supplied one.
    bool pointsResolved = false;
    HdExtComputationPrimvarDescriptorVector dirtyComputedPrimvars;
    for (size_t interpolation = 0;
         interpolation < HdInterpolationCount;
         ++interpolation)
    {
        const HdExtComputationPrimvarDescriptorVector computedPrimvars =
            sceneDelegate->GetExtComputationPrimvarDescriptors(
                id,
                static_cast<HdInterpolation>(interpolation));
        for (HdExtComputationPrimvarDescriptor const& primvar : computedPrimvars)
        {
            if (HdChangeTracker::IsPrimvarDirty(*dirtyBits, id, primvar.name))
            {
                dirtyComputedPrimvars.emplace_back(primvar);
            }
        }
    }
    if (!dirtyComputedPrimvars.empty())
    {
        const HdExtComputationUtils::ValueStore computedValues =
            HdExtComputationUtils::GetComputedPrimvarValues(
                dirtyComputedPrimvars,
                sceneDelegate);
        for (HdExtComputationPrimvarDescriptor const& primvar :
             dirtyComputedPrimvars)
        {
            if (primvar.name != HdTokens->points)
            {
                continue;
            }
            const auto value = computedValues.find(primvar.name);
            if (value != computedValues.end() &&
                ExtractPoints(value->second, &_points))
            {
                pointsResolved = true;
            }
        }
    }

    // Topology and points must stay consistent. When topology is refreshed
    // without a matching points dirty bit the cached array can still describe
    // the previous topology, so re-pull it rather than publish indices that
    // overrun the point buffer.
    if (!pointsResolved && (pointsDirty || topologyRefreshed))
    {
        ExtractPoints(sceneDelegate->Get(id, HdTokens->points), &_points);
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

    if (normalsDirty || topologyRefreshed)
    {
        // Only the interpolations that map directly onto emitted triangle-list
        // vertices are resolved here. Anything else leaves _normals empty so
        // the consumer computes them, which is the pre-ABI-4 behaviour, rather
        // than this delegate guessing at a re-indexing it cannot verify.
        _normals.clear();
        _normalsAreConstant = false;
        const VtValue value = sceneDelegate->Get(id, HdTokens->normals);
        if (value.IsHolding<VtVec3fArray>())
        {
            const VtVec3fArray& normals = value.UncheckedGet<VtVec3fArray>();
            if (normals.size() == _points.size())
            {
                _normals = normals;
            }
            else if (normals.size() == 1)
            {
                _normals = normals;
                _normalsAreConstant = true;
            }
        }
        else if (value.IsHolding<GfVec3f>())
        {
            _normals = VtVec3fArray(1, value.UncheckedGet<GfVec3f>());
            _normalsAreConstant = true;
        }
    }

    // Instancer state must be refreshed before instance transforms are read,
    // and parent instancers are synced by Rprim reference in Hydra.
    _UpdateInstancer(sceneDelegate, dirtyBits);
    HdInstancer::_SyncInstancerAndParents(
        sceneDelegate->GetRenderIndex(),
        GetInstancerId());

    const bool instancerDirty =
        HdChangeTracker::IsInstancerDirty(*dirtyBits, id) ||
        HdChangeTracker::IsInstanceIndexDirty(*dirtyBits, id);

    if (topologyRefreshed || pointsDirty || transformDirty ||
        displayColorDirty || normalsDirty || instancerDirty || materialDirty)
    {
        HdSilkMeshRecord record;
        record.path = id.GetString();
        record.primId = GetPrimId();
        record.topologyRevision = _topologyRevision;
        record.materialPath = GetMaterialId().GetString();
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

        if (!_normals.empty())
        {
            HdSilkMeshAttribute normals;
            normals.semantic = OPENUSD_SILK_ATTRIBUTE_NORMAL;
            normals.componentCount = 3;
            normals.interpolation = _normalsAreConstant
                ? OPENUSD_SILK_INTERPOLATION_CONSTANT
                : OPENUSD_SILK_INTERPOLATION_VERTEX;
            normals.data.reserve(_normals.size() * 3);
            for (const GfVec3f& normal : _normals)
            {
                normals.data.push_back(normal[0]);
                normals.data.push_back(normal[1]);
                normals.data.push_back(normal[2]);
            }
            record.attributes.push_back(std::move(normals));
        }

        // Capture the key before the record is moved; argument evaluation
        // order relative to the moved-from object is otherwise unspecified.
        const std::string path = record.path;
        HdSilkRenderParam* silkRenderParam =
            static_cast<HdSilkRenderParam*>(renderParam);

        std::vector<HdSilkMeshRecord> records =
            _BuildInstanceRecords(sceneDelegate, std::move(record));
        silkRenderParam->GetSceneState().ReplaceMeshInstances(
            path,
            std::move(records));
    }

    *dirtyBits = HdChangeTracker::Clean;
}

std::vector<HdSilkMeshRecord>
HdSilkMesh::_BuildInstanceRecords(
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

    // hdSilk has no instancing wire ABI of its own: each resolved instance is
    // flattened into its own record so backend-neutral consumers keep drawing
    // plain triangle lists. instance_index makes the identities distinct while
    // the prototype path stays authoritative.
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
            "The hdSilk instance count exceeds the 32-bit instance index.");
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
            instanceTransforms[index] * _transform,
            instanceRecord.transform);
        records.push_back(std::move(instanceRecord));
    }
    return records;
}

PXR_NAMESPACE_CLOSE_SCOPE
