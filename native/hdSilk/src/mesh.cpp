// Copyright (c) marcschier. Licensed under the MIT License.

#include "mesh.h"

#include "openusd_hdsilk.h"

#include "instancer.h"
#include "renderDelegate.h"
#include "sceneState.h"

#include "pxr/base/gf/vec2f.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/gf/vec3i.h"
#include "pxr/base/gf/vec4f.h"
#include "pxr/base/vt/value.h"
#include "pxr/imaging/hd/changeTracker.h"
#include "pxr/imaging/hd/extComputationUtils.h"
#include "pxr/imaging/hd/meshUtil.h"
#include "pxr/imaging/hd/renderIndex.h"
#include "pxr/imaging/hd/sceneDelegate.h"
#include "pxr/imaging/hd/types.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/usd/usdGeom/tokens.h"

#include <algorithm>
#include <limits>
#include <stdexcept>
#include <string>
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

/// Flattens a primvar value into tightly packed floats, returning the component
/// count per element, or zero when the value holds a type this delegate cannot
/// represent on the wire. Only float-convertible scalar and vector types are
/// accepted; anything else is omitted rather than reinterpreted, because a
/// guessed reinterpretation would reach the shader as silently wrong data.
uint32_t FlattenPrimvar(const VtValue& value, std::vector<float>* out)
{
    out->clear();
    if (value.IsHolding<VtFloatArray>())
    {
        const VtFloatArray& source = value.UncheckedGet<VtFloatArray>();
        out->assign(source.begin(), source.end());
        return 1;
    }
    if (value.IsHolding<VtVec2fArray>())
    {
        const VtVec2fArray& source = value.UncheckedGet<VtVec2fArray>();
        out->reserve(source.size() * 2);
        for (const GfVec2f& element : source)
        {
            out->push_back(element[0]);
            out->push_back(element[1]);
        }
        return 2;
    }
    if (value.IsHolding<VtVec3fArray>())
    {
        const VtVec3fArray& source = value.UncheckedGet<VtVec3fArray>();
        out->reserve(source.size() * 3);
        for (const GfVec3f& element : source)
        {
            out->push_back(element[0]);
            out->push_back(element[1]);
            out->push_back(element[2]);
        }
        return 3;
    }
    if (value.IsHolding<VtVec4fArray>())
    {
        const VtVec4fArray& source = value.UncheckedGet<VtVec4fArray>();
        out->reserve(source.size() * 4);
        for (const GfVec4f& element : source)
        {
            for (int component = 0; component < 4; ++component)
            {
                out->push_back(element[component]);
            }
        }
        return 4;
    }
    if (value.IsHolding<float>())
    {
        out->push_back(value.UncheckedGet<float>());
        return 1;
    }
    if (value.IsHolding<GfVec2f>())
    {
        const GfVec2f element = value.UncheckedGet<GfVec2f>();
        out->push_back(element[0]);
        out->push_back(element[1]);
        return 2;
    }
    if (value.IsHolding<GfVec3f>())
    {
        const GfVec3f element = value.UncheckedGet<GfVec3f>();
        out->push_back(element[0]);
        out->push_back(element[1]);
        out->push_back(element[2]);
        return 3;
    }
    if (value.IsHolding<GfVec4f>())
    {
        const GfVec4f element = value.UncheckedGet<GfVec4f>();
        for (int component = 0; component < 4; ++component)
        {
            out->push_back(element[component]);
        }
        return 4;
    }
    return 0;
}

/// Maps an authored primvar onto a wire semantic. The name always travels
/// regardless of semantic, because a mesh may carry several texture coordinate
/// sets and a UsdUVTexture reader selects one of them by name.
uint32_t ResolveSemantic(
    const TfToken& name,
    const TfToken& role,
    uint32_t componentCount)
{
    if (name == HdTokens->normals)
    {
        return OPENUSD_SILK_ATTRIBUTE_NORMAL;
    }
    if (name == HdTokens->displayColor)
    {
        return OPENUSD_SILK_ATTRIBUTE_COLOR;
    }
    if (role == HdPrimvarRoleTokens->textureCoordinate ||
        (componentCount == 2 && name.GetString().rfind("st", 0) == 0))
    {
        return OPENUSD_SILK_ATTRIBUTE_TEXCOORD;
    }
    return OPENUSD_SILK_ATTRIBUTE_CUSTOM;
}

bool GetFloatArraySource(
    const VtValue& value,
    const void** source,
    int* numElements,
    HdType* dataType)
{
    if (value.IsHolding<VtFloatArray>())
    {
        const VtFloatArray& array = value.UncheckedGet<VtFloatArray>();
        if (array.size() > static_cast<size_t>(std::numeric_limits<int>::max()))
        {
            return false;
        }
        *source = array.cdata();
        *numElements = static_cast<int>(array.size());
        *dataType = HdTypeFloat;
        return true;
    }
    if (value.IsHolding<VtVec2fArray>())
    {
        const VtVec2fArray& array = value.UncheckedGet<VtVec2fArray>();
        if (array.size() > static_cast<size_t>(std::numeric_limits<int>::max()))
        {
            return false;
        }
        *source = array.cdata();
        *numElements = static_cast<int>(array.size());
        *dataType = HdTypeFloatVec2;
        return true;
    }
    if (value.IsHolding<VtVec3fArray>())
    {
        const VtVec3fArray& array = value.UncheckedGet<VtVec3fArray>();
        if (array.size() > static_cast<size_t>(std::numeric_limits<int>::max()))
        {
            return false;
        }
        *source = array.cdata();
        *numElements = static_cast<int>(array.size());
        *dataType = HdTypeFloatVec3;
        return true;
    }
    if (value.IsHolding<VtVec4fArray>())
    {
        const VtVec4fArray& array = value.UncheckedGet<VtVec4fArray>();
        if (array.size() > static_cast<size_t>(std::numeric_limits<int>::max()))
        {
            return false;
        }
        *source = array.cdata();
        *numElements = static_cast<int>(array.size());
        *dataType = HdTypeFloatVec4;
        return true;
    }
    return false;
}

bool ResolveFaceVaryingPrimvar(
    const VtValue& value,
    const HdMeshUtil& meshUtil,
    std::vector<float>* data,
    uint32_t* componentCount)
{
    const void* source = nullptr;
    int numElements = 0;
    HdType dataType = HdTypeInvalid;
    if (!GetFloatArraySource(value, &source, &numElements, &dataType))
    {
        return false;
    }

    VtValue triangulated;
    const HdMeshComputationResult result =
        meshUtil.ComputeTriangulatedFaceVaryingPrimvar(
            source,
            numElements,
            dataType,
            &triangulated);
    if (result == HdMeshComputationResult::Error)
    {
        return false;
    }
    if (result == HdMeshComputationResult::Unchanged)
    {
        triangulated = value;
    }

    const uint32_t count = FlattenPrimvar(triangulated, data);
    if (count == 0 || data->empty())
    {
        return false;
    }
    *componentCount = count;
    return true;
}

bool ExpandIndexedElements(
    const std::vector<float>& source,
    uint32_t componentCount,
    const std::vector<uint32_t>& indices,
    std::vector<float>* expanded)
{
    if (componentCount == 0 || source.size() % componentCount != 0)
    {
        return false;
    }
    const size_t elementCount = source.size() / componentCount;
    expanded->clear();
    expanded->reserve(indices.size() * componentCount);
    for (uint32_t index : indices)
    {
        if (index >= elementCount)
        {
            return false;
        }
        const size_t offset = static_cast<size_t>(index) * componentCount;
        expanded->insert(
            expanded->end(),
            source.begin() + static_cast<std::ptrdiff_t>(offset),
            source.begin() + static_cast<std::ptrdiff_t>(
                offset + componentCount));
    }
    return true;
}

bool ExpandUniformElements(
    const std::vector<float>& source,
    uint32_t componentCount,
    const std::vector<uint32_t>& triangleSubprims,
    std::vector<float>* expanded)
{
    if (componentCount == 0 || source.size() % componentCount != 0)
    {
        return false;
    }
    const size_t elementCount = source.size() / componentCount;
    expanded->clear();
    expanded->reserve(triangleSubprims.size() * 3 * componentCount);
    for (uint32_t face : triangleSubprims)
    {
        if (face >= elementCount)
        {
            return false;
        }
        const size_t offset = static_cast<size_t>(face) * componentCount;
        for (int vertex = 0; vertex < 3; ++vertex)
        {
            expanded->insert(
                expanded->end(),
                source.begin() + static_cast<std::ptrdiff_t>(offset),
                source.begin() + static_cast<std::ptrdiff_t>(
                    offset + componentCount));
        }
    }
    return true;
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
        | HdChangeTracker::DirtyDoubleSided
        | HdChangeTracker::DirtyCullStyle
        | HdChangeTracker::DirtyTransform
        | HdChangeTracker::DirtyVisibility
        | HdChangeTracker::DirtyMaterialId
        | HdChangeTracker::DirtyPrimvar
        // Without DirtyInstancer the first Sync never calls through to
        // HdRprim::_UpdateInstancer, so GetInstancerId() stays empty and a
        // point-instanced prototype is emitted once at its own transform
        // instead of once per instance. hdEmbree seeds the same bit.
        | HdChangeTracker::DirtyInstancer
        | HdChangeTracker::DirtyInstanceIndex;
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

    const bool visibilityDirty =
        HdChangeTracker::IsVisibilityDirty(*dirtyBits, id);
    if (visibilityDirty)
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
    const bool primvarsDirty = (*dirtyBits & HdChangeTracker::DirtyPrimvar) != 0;
    const bool transformDirty = HdChangeTracker::IsTransformDirty(*dirtyBits, id);
    const bool materialDirty =
        (*dirtyBits & HdChangeTracker::DirtyMaterialId) != 0;
    const bool cullDirty =
        (*dirtyBits & (HdChangeTracker::DirtyDoubleSided |
                      HdChangeTracker::DirtyCullStyle)) != 0;
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

    if (normalsDirty || primvarsDirty || topologyRefreshed)
    {
        _RefreshAttributes(sceneDelegate, id);
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

    if (!IsVisible())
    {
        static_cast<HdSilkRenderParam*>(renderParam)
            ->GetSceneState()
            .RemoveMesh(id.GetString());
        *dirtyBits = HdChangeTracker::Clean;
        return;
    }

    if (topologyRefreshed || pointsDirty || transformDirty || visibilityDirty ||
        displayColorDirty || normalsDirty || primvarsDirty || instancerDirty ||
        materialDirty || cullDirty)
    {
        HdSilkMeshRecord record;
        record.path = id.GetString();
        record.primId = GetPrimId();
        record.topologyRevision = _topologyRevision;
        record.doubleSided = IsDoubleSided(sceneDelegate) ? 1u : 0u;
        record.cullStyle = static_cast<uint32_t>(GetCullStyle(sceneDelegate));
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
        if (_attributesRequireExpandedTopology)
        {
            record.points.reserve(_triangleIndices.size() * 3);
            record.indices.reserve(_triangleIndices.size());
            for (size_t vertex = 0; vertex < _triangleIndices.size(); ++vertex)
            {
                const uint32_t pointIndex = _triangleIndices[vertex];
                if (pointIndex >= _points.size())
                {
                    throw std::runtime_error(
                        "An hdSilk expanded vertex references a missing point.");
                }
                const GfVec3f& point = _points[pointIndex];
                record.points.push_back(point[0]);
                record.points.push_back(point[1]);
                record.points.push_back(point[2]);
                record.indices.push_back(static_cast<uint32_t>(vertex));
            }
        }
        else
        {
            record.points.reserve(_points.size() * 3);
            for (const GfVec3f& point : _points)
            {
                record.points.push_back(point[0]);
                record.points.push_back(point[1]);
                record.points.push_back(point[2]);
            }
            record.indices = _triangleIndices;
        }

        record.triangleSubprims = _triangleSubprims;
        record.attributes = _attributes;

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

void
HdSilkMesh::_RefreshAttributes(HdSceneDelegate* sceneDelegate, SdfPath const& id)
{
    _attributes.clear();
    _attributesRequireExpandedTopology = false;
    HdMeshUtil meshUtil(&_topology, id);
    struct PendingAttribute
    {
        HdPrimvarDescriptor primvar;
        HdInterpolation interpolation;
        uint32_t componentCount = 0;
        std::vector<float> data;
    };
    std::vector<PendingAttribute> pending;

    static const HdInterpolation resolvable[] = {
        HdInterpolationConstant,
        HdInterpolationVertex,
        HdInterpolationVarying,
        HdInterpolationFaceVarying,
        HdInterpolationUniform};

    for (HdInterpolation interpolation : resolvable)
    {
        const HdPrimvarDescriptorVector primvars =
            sceneDelegate->GetPrimvarDescriptors(id, interpolation);
        for (const HdPrimvarDescriptor& primvar : primvars)
        {
            // Points travel in their own fixed field, so republishing them as an
            // attribute would duplicate the largest array in the page.
            if (primvar.name == HdTokens->points)
            {
                continue;
            }

            VtValue value = sceneDelegate->Get(id, primvar.name);
            PendingAttribute attribute;
            attribute.primvar = primvar;
            attribute.interpolation = interpolation;
            if (interpolation == HdInterpolationFaceVarying)
            {
                if (!ResolveFaceVaryingPrimvar(
                        value,
                        meshUtil,
                        &attribute.data,
                        &attribute.componentCount) ||
                    attribute.data.size() !=
                        _triangleIndices.size() * attribute.componentCount)
                {
                    continue;
                }
                _attributesRequireExpandedTopology = true;
            }
            else
            {
                attribute.componentCount = FlattenPrimvar(value, &attribute.data);
                if (attribute.componentCount == 0 || attribute.data.empty())
                {
                    continue;
                }
                if (interpolation == HdInterpolationUniform)
                {
                    std::vector<float> expanded;
                    if (!ExpandUniformElements(
                            attribute.data,
                            attribute.componentCount,
                            _triangleSubprims,
                            &expanded))
                    {
                        continue;
                    }
                    attribute.data = std::move(expanded);
                    _attributesRequireExpandedTopology = true;
                }
            }

            pending.push_back(std::move(attribute));
        }
    }

    for (PendingAttribute& pendingAttribute : pending)
    {
        uint32_t wireInterpolation = 0;
        std::vector<float> data = std::move(pendingAttribute.data);
        const uint32_t componentCount = pendingAttribute.componentCount;
        if (data.size() % componentCount != 0)
        {
            continue;
        }
        const size_t elementCount = data.size() / componentCount;
        if (pendingAttribute.interpolation == HdInterpolationConstant ||
            elementCount == 1)
        {
            wireInterpolation = OPENUSD_SILK_INTERPOLATION_CONSTANT;
        }
        else
        {
            if (_attributesRequireExpandedTopology &&
                (pendingAttribute.interpolation == HdInterpolationVertex ||
                 pendingAttribute.interpolation == HdInterpolationVarying))
            {
                std::vector<float> expanded;
                if (elementCount != _points.size() ||
                    !ExpandIndexedElements(
                        data,
                        componentCount,
                        _triangleIndices,
                        &expanded))
                {
                    continue;
                }
                data = std::move(expanded);
            }
            if (data.size() / componentCount !=
                (_attributesRequireExpandedTopology
                    ? _triangleIndices.size()
                    : _points.size()))
            {
                // An element count that matches neither one nor the point count
                // cannot be indexed by the emitted vertices.
                continue;
            }
            wireInterpolation = OPENUSD_SILK_INTERPOLATION_VERTEX;
        }

        HdSilkMeshAttribute attribute;
        attribute.name = pendingAttribute.primvar.name.GetString();
        attribute.semantic = ResolveSemantic(
            pendingAttribute.primvar.name,
            pendingAttribute.primvar.role,
            componentCount);
        attribute.componentCount = componentCount;
        attribute.interpolation = wireInterpolation;
        attribute.data = std::move(data);
        _attributes.push_back(std::move(attribute));
    }

    // A stable order keeps the page byte-identical for an unchanged scene, which
    // the reproducibility and parity evidence both depend on; Hydra does not
    // promise a descriptor order.
    std::sort(
        _attributes.begin(),
        _attributes.end(),
        [](const HdSilkMeshAttribute& left, const HdSilkMeshAttribute& right)
        {
            return left.name < right.name;
        });
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

    // ABI v8 carries prototype geometry once. Instance zero remains the full
    // prototype record; later records retain only per-instance identity and
    // transform and let consumers reuse instance zero's geometry and material.
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
        // The prototype's own transform applies first, then the instance
        // transform carries it into world space. USD composes row vectors, so
        // that is "_transform * instance", matching hdEmbree.
        HdSilkFlattenMatrix(
            _transform * instanceTransforms[index],
            instanceRecord.transform);
        if (index != 0)
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
