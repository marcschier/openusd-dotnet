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
#include "pxr/usd/usdGeom/mesh.h"
#include "pxr/usd/usdGeom/tokens.h"
#include "pxr/usd/usdSkel/bindingAPI.h"
#include "pxr/usd/usdSkel/animMapper.h"
#include "pxr/usd/usdSkel/blendShapeQuery.h"
#include "pxr/usd/usdSkel/cache.h"
#include "pxr/usd/usdSkel/root.h"
#include "pxr/usd/usdSkel/skeletonQuery.h"
#include "pxr/usd/usdSkel/skinningQuery.h"
#include "pxr/usd/usdSkel/tokens.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <numeric>
#include <shared_mutex>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <utility>

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
/// The stage and time one render is evaluating UsdSkel against.
///
/// This is deliberately not thread-local. Hydra syncs Rprims on a worker pool,
/// so a thread-local written by the thread that calls Render() is invisible to
/// every worker that syncs a different prim: a single skinned mesh happened to
/// resolve while a scene of several skinned meshes silently fell back to
/// undeformed authored points for all but one of them. The scope is keyed by
/// the render delegate's scene state instead, which is exactly the identity
/// that separates one concurrent session from another even when both render
/// the same shared stage at different time codes.
struct SkelEvaluationScope
{
    UsdStageRefPtr stage;
    UsdTimeCode time = UsdTimeCode::Default();
};

std::shared_mutex& SkelEvaluationMutex()
{
    static std::shared_mutex mutex;
    return mutex;
}

std::unordered_map<const HdSilkSceneState*, SkelEvaluationScope>&
SkelEvaluationScopes()
{
    static std::unordered_map<const HdSilkSceneState*, SkelEvaluationScope>
        scopes;
    return scopes;
}

bool TryGetSkelEvaluationScope(
    const HdSilkSceneState* sceneState,
    SkelEvaluationScope* scope)
{
    if (sceneState == nullptr)
    {
        return false;
    }
    std::shared_lock<std::shared_mutex> lock(SkelEvaluationMutex());
    const auto entry = SkelEvaluationScopes().find(sceneState);
    if (entry == SkelEvaluationScopes().end() || !entry->second.stage)
    {
        return false;
    }
    *scope = entry->second;
    return true;
}

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

/// Rescales refined normals back to unit length.
///
/// Every refinement rule is a weighted average, and averaging unit directions
/// shortens them: a face-varying normal set whose corners disagree lands well
/// inside the unit sphere at the refined face point. The vertex, varying and
/// face-varying paths therefore all pass through here, so a refined normal is
/// the same length whichever interpolation authored it. Nothing else is
/// rescaled, because only a normal has a length the shading model depends on.
void NormalizeRefinedNormals(
    const TfToken& name,
    uint32_t componentCount,
    std::vector<float>* data)
{
    if (name != HdTokens->normals || componentCount != 3 || data == nullptr)
    {
        return;
    }
    std::vector<float>& values = *data;
    for (size_t element = 0; element + 2 < values.size(); element += 3)
    {
        GfVec3f normal(
            values[element],
            values[element + 1],
            values[element + 2]);
        const float length = normal.GetLength();
        if (length > 0.0F)
        {
            normal /= length;
            values[element] = normal[0];
            values[element + 1] = normal[1];
            values[element + 2] = normal[2];
        }
    }
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

/// Numbers the authored mesh edges of a topology and maps every emitted
/// triangle corner onto one of them.
///
/// An authored edge is the unordered pair of authored point indices that two
/// consecutive corners of an authored face span. Indices are allocated by
/// walking authored faces in order and, inside a face, corners in order, so the
/// numbering is a pure function of the authored topology: two consumers of the
/// same stage agree without exchanging anything but the published table.
///
/// Triangulating an n-gon introduces interior diagonals the scene never
/// authored. A triangle corner whose point pair is not an authored edge is
/// therefore mapped to OPENUSD_SILK_SUBPRIM_NONE rather than to a generated
/// index: an edge pick that lands on a diagonal must miss, not report an edge
/// that no round trip could resolve back to the stage.
///
/// Returns false, leaving the outputs empty, when the authored topology cannot
/// be walked or its edge count would not fit the wire.
bool BuildAuthoredEdgeTable(
    const HdMeshTopology& topology,
    const std::vector<uint32_t>& triangleIndices,
    std::vector<uint32_t>* outCornerEdges,
    uint32_t* outAuthoredEdgeCount)
{
    outCornerEdges->clear();
    *outAuthoredEdgeCount = 0;
    if ((triangleIndices.size() % 3) != 0)
    {
        return false;
    }

    const VtIntArray& faceVertexCounts = topology.GetFaceVertexCounts();
    const VtIntArray& faceVertexIndices = topology.GetFaceVertexIndices();

    // A 64-bit key is the ordered pair packed low-to-high, which keys an
    // unordered edge once the pair is canonicalized.
    std::unordered_map<uint64_t, uint32_t> authoredEdges;
    size_t corner = 0;
    for (int faceVertexCount : faceVertexCounts)
    {
        if (faceVertexCount < 0 ||
            corner + static_cast<size_t>(faceVertexCount) >
                faceVertexIndices.size())
        {
            return false;
        }
        for (int index = 0; index < faceVertexCount; ++index)
        {
            const int first = faceVertexIndices[corner + index];
            const int second = faceVertexIndices[
                corner + ((index + 1) % faceVertexCount)];
            if (first < 0 || second < 0 || first == second)
            {
                continue;
            }
            const uint64_t low = static_cast<uint64_t>(std::min(first, second));
            const uint64_t high = static_cast<uint64_t>(std::max(first, second));
            const uint64_t key = (high << 32) | low;
            if (authoredEdges.find(key) != authoredEdges.end())
            {
                continue;
            }
            if (authoredEdges.size() >=
                static_cast<size_t>(std::numeric_limits<uint32_t>::max()))
            {
                return false;
            }
            authoredEdges.emplace(
                key, static_cast<uint32_t>(authoredEdges.size()));
        }
        corner += static_cast<size_t>(faceVertexCount);
    }

    outCornerEdges->reserve(triangleIndices.size());
    for (size_t base = 0; base < triangleIndices.size(); base += 3)
    {
        for (size_t component = 0; component < 3; ++component)
        {
            const uint32_t first = triangleIndices[base + component];
            const uint32_t second = triangleIndices[base + ((component + 1) % 3)];
            if (first == second)
            {
                outCornerEdges->push_back(OPENUSD_SILK_SUBPRIM_NONE);
                continue;
            }
            const uint64_t low = static_cast<uint64_t>(std::min(first, second));
            const uint64_t high = static_cast<uint64_t>(std::max(first, second));
            const auto found = authoredEdges.find((high << 32) | low);
            outCornerEdges->push_back(
                found == authoredEdges.end()
                    ? OPENUSD_SILK_SUBPRIM_NONE
                    : found->second);
        }
    }

    *outAuthoredEdgeCount = static_cast<uint32_t>(authoredEdges.size());
    return true;
}

/// One past the largest authored index a subprim-identity table names, or zero
/// when it names none.
///
/// The ABI defines the authored counts this way rather than as the size of the
/// authored array, so a consumer is never handed an authored space larger than
/// the table it already read. An authored component no emitted primitive covers
/// -- a point no face references, or an edge that belongs only to hole faces --
/// is simply not named, and the count shrinks to match.
uint32_t OneePastLargestNamed(const std::vector<uint32_t>& table)
{
    uint32_t largest = 0;
    bool named = false;
    for (uint32_t entry : table)
    {
        if (entry == OPENUSD_SILK_SUBPRIM_NONE)
        {
            continue;
        }
        if (!named || entry > largest)
        {
            largest = entry;
            named = true;
        }
    }
    if (!named)
    {
        return 0;
    }
    if (largest == std::numeric_limits<uint32_t>::max() - 1)
    {
        throw std::overflow_error(
            "The hdSilk authored subprim count overflows uint32.");
    }
    return largest + 1;
}

UsdSkelRoot FindSkelRoot(UsdPrim prim){
    for (UsdPrim current = prim; current; current = current.GetParent())
    {
        UsdSkelRoot root(current);
        if (root)
        {
            return root;
        }
    }
    return UsdSkelRoot();
}

/// Resolves the sub-shape weights of the bound blend shapes at the evaluation
/// time. UsdSkel resolves in-betweens here: ComputeSubShapeWeights expands one
/// authored blend-shape weight into the weights of the primary shape and every
/// in-between shape it interpolates through, so the same call covers a rig with
/// and without in-betweens.
bool TryResolveSubShapeWeights(
    SkelEvaluationScope const& scope,
    UsdSkelSkeletonQuery const& skeletonQuery,
    UsdSkelSkinningQuery const& skinningQuery,
    UsdSkelBlendShapeQuery const& blendShapeQuery,
    VtFloatArray* subShapeWeights,
    VtUIntArray* blendShapeIndices,
    VtUIntArray* subShapeIndices)
{
    const UsdSkelAnimQuery& animationQuery = skeletonQuery.GetAnimQuery();
    if (!animationQuery)
    {
        return false;
    }

    VtFloatArray animationWeights;
    if (!animationQuery.ComputeBlendShapeWeights(
            &animationWeights,
            scope.time))
    {
        return false;
    }

    VtFloatArray localWeights;
    const UsdSkelAnimMapperRefPtr& mapper = skinningQuery.GetBlendShapeMapper();
    if (mapper)
    {
        const float defaultWeight = 0.0F;
        if (!mapper->Remap(animationWeights, &localWeights, 1, &defaultWeight))
        {
            return false;
        }
    }
    else
    {
        localWeights = animationWeights;
    }

    return blendShapeQuery.ComputeSubShapeWeights(
        TfMakeSpan(localWeights),
        subShapeWeights,
        blendShapeIndices,
        subShapeIndices);
}

/// The resolved UsdSkel binding of one mesh is cached in HdSilkMeshSkelState.
void ResolveSkelBinding(
    SkelEvaluationScope const& scope,
    SdfPath const& id,
    HdSilkMeshSkelState* binding)
{
    binding->resolved = true;
    binding->valid = false;
    binding->stage = scope.stage.operator->();
    binding->skeletonQuery = UsdSkelSkeletonQuery();
    binding->skinningQuery = UsdSkelSkinningQuery();
    binding->blendShapeQuery = UsdSkelBlendShapeQuery();
    binding->blendShapePointIndices.clear();
    binding->subShapePointOffsets.clear();
    binding->subShapeNormalOffsets.clear();
    binding->hasNormalOffsets = false;

    const UsdPrim prim = scope.stage->GetPrimAtPath(id);
    if (!prim || !UsdGeomMesh(prim))
    {
        return;
    }
    UsdSkelRoot root = FindSkelRoot(prim);
    if (!root)
    {
        return;
    }

    UsdSkelCache cache;
    if (!cache.Populate(root, UsdPrimDefaultPredicate))
    {
        return;
    }
    std::vector<UsdSkelBinding> skelBindings;
    if (!cache.ComputeSkelBindings(root, &skelBindings, UsdPrimDefaultPredicate))
    {
        return;
    }

    for (const UsdSkelBinding& skelBinding : skelBindings)
    {
        const UsdSkelSkeletonQuery skeletonQuery =
            cache.GetSkelQuery(skelBinding.GetSkeleton());
        if (!skeletonQuery)
        {
            continue;
        }
        for (const UsdSkelSkinningQuery& skinningQuery :
             skelBinding.GetSkinningTargets())
        {
            if (!skinningQuery || skinningQuery.GetPrim().GetPath() != id)
            {
                continue;
            }
            binding->skeletonQuery = skeletonQuery;
            binding->skinningQuery = skinningQuery;
            binding->valid = true;
            if (skinningQuery.HasBlendShapes())
            {
                binding->blendShapeQuery = UsdSkelBlendShapeQuery(
                    UsdSkelBindingAPI(skinningQuery.GetPrim()));
                if (binding->blendShapeQuery)
                {
                    binding->blendShapePointIndices =
                        binding->blendShapeQuery.ComputeBlendShapePointIndices();
                    binding->subShapePointOffsets =
                        binding->blendShapeQuery.ComputeSubShapePointOffsets();
                    binding->subShapeNormalOffsets =
                        binding->blendShapeQuery.ComputeSubShapeNormalOffsets();
                    for (const VtVec3fArray& offsets :
                         binding->subShapeNormalOffsets)
                    {
                        if (!offsets.empty())
                        {
                            binding->hasNormalOffsets = true;
                            break;
                        }
                    }
                }
            }
            return;
        }
    }
}

/// The CPU-resolved deformation of one mesh at the evaluation time. Normals are
/// resolved only when the mesh authors a per-point normal array, because that is
/// the only interpolation UsdSkel can deform: blend-shape normal offsets and
/// joint influences are both indexed by point.
struct SkelDeformation
{
    VtVec3fArray points;
    VtVec3fArray normals;
    bool hasNormals = false;
    // Set when the mesh is deformed but its authored normals are not per-point,
    // so bind-pose normals would describe the undeformed surface.
    bool normalsUndeformable = false;
    // The bounded, renderer-neutral description of the same deformation. It is
    // published beside the CPU result rather than instead of it, and only when
    // evaluating it reproduces that result.
    HdSilkMeshDeformation rig;
};

/// Reads the authored per-point normals of a deformed mesh. Face-varying,
/// uniform, and indexed normals are rejected rather than reinterpreted: a
/// blend-shape normal offset and a joint influence are both addressed by point
/// index, so there is no correct way to apply either to a corner-indexed array.
bool TryGetDeformableNormals(
    SkelEvaluationScope const& scope,
    UsdGeomMesh const& mesh,
    size_t pointCount,
    VtVec3fArray* normals)
{
    const UsdAttribute attribute = mesh.GetNormalsAttr();
    if (!attribute || !attribute.HasAuthoredValue())
    {
        return false;
    }
    const TfToken interpolation = mesh.GetNormalsInterpolation();
    if (interpolation != UsdGeomTokens->vertex &&
        interpolation != UsdGeomTokens->varying)
    {
        return false;
    }
    VtVec3fArray authored;
    if (!attribute.Get(&authored, scope.time) ||
        authored.size() != pointCount)
    {
        return false;
    }
    *normals = std::move(authored);
    return true;
}

/// Flattens a GfMatrix4d into the 16 row-major floats the deformation block
/// carries. The block is float because a bounded rig is sized for a consumer
/// that evaluates it in float; the CPU deformation beside it stays double.
void FlattenRigMatrix(const GfMatrix4d& matrix, float* out)
{
    const double* raw = matrix.GetArray();
    for (int element = 0; element < 16; ++element)
    {
        out[element] = static_cast<float>(raw[element]);
    }
}

/// Transforms a point by a row-major 4x4 using USD's row-vector convention,
/// which is the convention UsdSkelSkinPointsLBS composes influences in.
GfVec3f RigTransformPoint(const float* matrix, const GfVec3f& point)
{
    return GfVec3f(
        (point[0] * matrix[0]) + (point[1] * matrix[4]) +
            (point[2] * matrix[8]) + matrix[12],
        (point[0] * matrix[1]) + (point[1] * matrix[5]) +
            (point[2] * matrix[9]) + matrix[13],
        (point[0] * matrix[2]) + (point[1] * matrix[6]) +
            (point[2] * matrix[10]) + matrix[14]);
}

/// Transforms a direction by a row-major 3x3 using the same row-vector
/// convention.
GfVec3f RigTransformDirection(const float* matrix, const GfVec3f& direction)
{
    return GfVec3f(
        (direction[0] * matrix[0]) + (direction[1] * matrix[3]) +
            (direction[2] * matrix[6]),
        (direction[0] * matrix[1]) + (direction[1] * matrix[4]) +
            (direction[2] * matrix[7]),
        (direction[0] * matrix[2]) + (direction[1] * matrix[5]) +
            (direction[2] * matrix[8]));
}

/// The inverse transpose of the upper-left 3x3 of a row-major 4x4, which is
/// what a normal is deformed by. Computed in double so a near-singular joint
/// does not lose the little precision float leaves it. A singular joint yields
/// the identity, which is what UsdSkel's own normal skinning falls back to.
void RigInverseTranspose(const float* matrix, float* out)
{
    const double m00 = matrix[0];
    const double m01 = matrix[1];
    const double m02 = matrix[2];
    const double m10 = matrix[4];
    const double m11 = matrix[5];
    const double m12 = matrix[6];
    const double m20 = matrix[8];
    const double m21 = matrix[9];
    const double m22 = matrix[10];
    const double c00 = (m11 * m22) - (m12 * m21);
    const double c01 = (m12 * m20) - (m10 * m22);
    const double c02 = (m10 * m21) - (m11 * m20);
    const double determinant = (m00 * c00) + (m01 * c01) + (m02 * c02);
    if (!std::isfinite(determinant) || std::fabs(determinant) < 1.0e-12)
    {
        out[0] = 1.0f; out[1] = 0.0f; out[2] = 0.0f;
        out[3] = 0.0f; out[4] = 1.0f; out[5] = 0.0f;
        out[6] = 0.0f; out[7] = 0.0f; out[8] = 1.0f;
        return;
    }
    const double inverse = 1.0 / determinant;
    // The inverse is the adjugate over the determinant; transposing it is the
    // same as reading the adjugate by rows instead of by columns.
    out[0] = static_cast<float>(c00 * inverse);
    out[1] = static_cast<float>(c01 * inverse);
    out[2] = static_cast<float>(c02 * inverse);
    out[3] = static_cast<float>(((m02 * m21) - (m01 * m22)) * inverse);
    out[4] = static_cast<float>(((m00 * m22) - (m02 * m20)) * inverse);
    out[5] = static_cast<float>(((m01 * m20) - (m00 * m21)) * inverse);
    out[6] = static_cast<float>(((m01 * m12) - (m02 * m11)) * inverse);
    out[7] = static_cast<float>(((m02 * m10) - (m00 * m12)) * inverse);
    out[8] = static_cast<float>(((m00 * m11) - (m01 * m10)) * inverse);
}

/// The squared length below which an accumulated normal carries no direction.
/// Shared by the producer's own evaluation and by the managed evaluator, so
/// both reach the same answer for the same rig.
constexpr float RigDegenerateNormalLengthSquared = 1.0e-30f;

/// The direction a degenerate normal resolves to. A consumer has to publish
/// something, and every consumer must publish the same thing or a rig that
/// produced a degenerate normal would verify on one and not on another.
const GfVec3f RigFallbackNormal(0.0f, 0.0f, 1.0f);

/// Evaluates a bounded rig exactly as the ABI documents it, in float, so the
/// verification below measures what a consumer evaluating the published block
/// would produce rather than what the double-precision CPU path already did.
///
/// A point whose accumulated normal carries no direction resolves to the
/// canonical fallback and is reported through outDegenerateNormals, because the
/// fallback is indistinguishable from a genuinely computed +Z and the
/// verification has to tell those apart.
void EvaluateBoundedRig(
    const HdSilkMeshDeformation& rig,
    size_t pointCount,
    VtVec3fArray* points,
    VtVec3fArray* normals,
    std::vector<uint8_t>* outDegenerateNormals = nullptr)
{
    const bool hasNormals =
        (rig.flags & OPENUSD_SILK_DEFORMATION_FLAG_BIND_NORMALS) != 0;
    points->resize(pointCount);
    normals->resize(hasNormals ? pointCount : 0);
    if (outDegenerateNormals != nullptr)
    {
        outDegenerateNormals->assign(hasNormals ? pointCount : 0, 0);
    }

    // Blend-shape deltas are sparse and ranges overlap, so they accumulate into
    // one dense offset per point in a single pass rather than being searched
    // per point.
    std::vector<GfVec3f> pointOffsets(pointCount, GfVec3f(0.0f));
    std::vector<GfVec3f> normalOffsets(
        hasNormals ? pointCount : 0,
        GfVec3f(0.0f));
    for (const HdSilkMeshBlendRange& range : rig.blendRanges)
    {
        for (uint32_t offset = 0; offset < range.deltaCount; ++offset)
        {
            const HdSilkMeshBlendDelta& delta =
                rig.blendDeltas[range.firstDelta + offset];
            GfVec3f& point = pointOffsets[delta.pointIndex];
            point[0] += range.weight * delta.positionOffset[0];
            point[1] += range.weight * delta.positionOffset[1];
            point[2] += range.weight * delta.positionOffset[2];
            if (hasNormals)
            {
                GfVec3f& normal = normalOffsets[delta.pointIndex];
                normal[0] += range.weight * delta.normalOffset[0];
                normal[1] += range.weight * delta.normalOffset[1];
                normal[2] += range.weight * delta.normalOffset[2];
            }
        }
    }

    std::vector<float> jointNormalMatrices(
        static_cast<size_t>(rig.jointCount) * 9);
    for (uint32_t joint = 0; joint < rig.jointCount; ++joint)
    {
        RigInverseTranspose(
            rig.jointMatrices.data() + (static_cast<size_t>(joint) * 16),
            jointNormalMatrices.data() + (static_cast<size_t>(joint) * 9));
    }
    float geomBindNormalMatrix[9];
    RigInverseTranspose(rig.geomBindTransform, geomBindNormalMatrix);

    const size_t influences = rig.influencesPerPoint;
    for (size_t point = 0; point < pointCount; ++point)
    {
        GfVec3f bind(
            rig.bindPoints[point * 3] + pointOffsets[point][0],
            rig.bindPoints[(point * 3) + 1] + pointOffsets[point][1],
            rig.bindPoints[(point * 3) + 2] + pointOffsets[point][2]);
        bind = RigTransformPoint(rig.geomBindTransform, bind);

        GfVec3f skinned(0.0f);
        for (size_t influence = 0; influence < influences; ++influence)
        {
            const size_t slot = (point * influences) + influence;
            const float weight = rig.jointWeights[slot];
            if (weight == 0.0f)
            {
                continue;
            }
            const GfVec3f moved = RigTransformPoint(
                rig.jointMatrices.data() +
                    (static_cast<size_t>(rig.jointIndices[slot]) * 16),
                bind);
            skinned += moved * weight;
        }
        (*points)[point] = skinned;

        if (!hasNormals)
        {
            continue;
        }
        GfVec3f normal(
            rig.bindNormals[point * 3] + normalOffsets[point][0],
            rig.bindNormals[(point * 3) + 1] + normalOffsets[point][1],
            rig.bindNormals[(point * 3) + 2] + normalOffsets[point][2]);
        normal = RigTransformDirection(geomBindNormalMatrix, normal);
        GfVec3f skinnedNormal(0.0f);
        for (size_t influence = 0; influence < influences; ++influence)
        {
            const size_t slot = (point * influences) + influence;
            const float weight = rig.jointWeights[slot];
            if (weight == 0.0f)
            {
                continue;
            }
            const GfVec3f moved = RigTransformDirection(
                jointNormalMatrices.data() +
                    (static_cast<size_t>(rig.jointIndices[slot]) * 9),
                normal);
            skinnedNormal += moved * weight;
        }
        // The fallback matches SilkDeformationEvaluator exactly, so a
        // degenerate normal is the same direction on both sides of the ABI
        // rather than a zero vector here and a +Z there.
        const float lengthSquared = skinnedNormal.GetLengthSq();
        if (!std::isfinite(lengthSquared) ||
            lengthSquared <= RigDegenerateNormalLengthSquared)
        {
            (*normals)[point] = RigFallbackNormal;
            if (outDegenerateNormals != nullptr)
            {
                (*outDegenerateNormals)[point] = 1;
            }
            continue;
        }
        (*normals)[point] = skinnedNormal.GetNormalized();
    }
}

/// Compares one evaluated component against the CPU-resolved one. The tolerance
/// scales with the magnitude because the two evaluations run the same arithmetic
/// in a different order and in a different precision, so a point a thousand
/// units from the origin cannot be held to an absolute float epsilon.
bool RigComponentAgrees(float evaluated, float resolved)
{
    if (!std::isfinite(evaluated) || !std::isfinite(resolved))
    {
        return false;
    }
    const float scale = std::max(1.0f, std::fabs(resolved));
    return std::fabs(evaluated - resolved) <=
        (OPENUSD_SILK_DEFORMATION_VERIFY_TOLERANCE * scale);
}

/// Checks that evaluating the rig reproduces the deformation hdSilk already
/// published. A rig that disagrees is dropped rather than published as a second
/// answer: a consumer choosing the block would then draw a surface this ABI
/// never promised, which is exactly the silent bind-pose failure the block is
/// meant to make impossible.
bool RigReproducesDeformation(
    const HdSilkMeshDeformation& rig,
    const VtVec3fArray& resolvedPoints,
    const VtVec3fArray& resolvedNormals)
{
    VtVec3fArray points;
    VtVec3fArray normals;
    std::vector<uint8_t> degenerateNormals;
    EvaluateBoundedRig(
        rig,
        resolvedPoints.size(),
        &points,
        &normals,
        &degenerateNormals);
    for (size_t point = 0; point < resolvedPoints.size(); ++point)
    {
        for (int component = 0; component < 3; ++component)
        {
            if (!RigComponentAgrees(
                    points[point][component],
                    resolvedPoints[point][component]))
            {
                return false;
            }
        }
    }
    if (normals.empty())
    {
        return true;
    }
    if (normals.size() != resolvedNormals.size() ||
        degenerateNormals.size() != normals.size())
    {
        return false;
    }
    for (size_t point = 0; point < normals.size(); ++point)
    {
        const GfVec3f& raw = resolvedNormals[point];
        const float resolvedLengthSquared = raw.GetLengthSq();
        const bool resolvedDegenerate =
            !std::isfinite(resolvedLengthSquared) ||
            resolvedLengthSquared <= RigDegenerateNormalLengthSquared;
        const bool evaluatedDegenerate = degenerateNormals[point] != 0;
        if (resolvedDegenerate && evaluatedDegenerate)
        {
            // Neither side carries a direction, so there is nothing to compare
            // and nothing to disagree about.
            continue;
        }
        if (resolvedDegenerate != evaluatedDegenerate)
        {
            // One side resolved a direction and the other did not. That is a
            // disagreement about the surface, not a rounding difference, and it
            // is exactly the case a two-sided skip used to hide: a rig whose
            // influences collapse a normal the CPU path kept -- or the reverse
            // -- would have been published as verified.
            return false;
        }
        const GfVec3f resolved = raw.GetNormalized();
        const GfVec3f evaluated = normals[point];
        for (int component = 0; component < 3; ++component)
        {
            if (!RigComponentAgrees(evaluated[component], resolved[component]))
            {
                return false;
            }
        }
    }
    return true;
}

/// The joint palette this record's influences index, in the prim's own joint
/// order. UsdSkel computes skinning transforms in skeleton joint order and
/// remaps them behind ComputeSkinnedPoints, so the remap has to happen here for
/// the published indices to address the published palette directly.
bool TryResolveJointPalette(
    UsdSkelSkinningQuery const& skinningQuery,
    VtMatrix4dArray const& skinningTransforms,
    VtMatrix4dArray* palette)
{
    const UsdSkelAnimMapperRefPtr& mapper = skinningQuery.GetJointMapper();
    if (!mapper)
    {
        *palette = skinningTransforms;
        return true;
    }
    return mapper->RemapTransforms(skinningTransforms, palette);
}

/// Appends one resolved sub-shape to the sparse blend tables. Returns false
/// when the authored shape cannot be described sparsely against this point
/// count, which is a malformed shape rather than a budget refusal.
bool TryAppendBlendRange(
    float weight,
    VtIntArray const& pointIndices,
    VtVec3fArray const& pointOffsets,
    VtVec3fArray const& normalOffsets,
    size_t pointCount,
    bool wantNormalOffsets,
    HdSilkMeshDeformation* rig)
{
    const bool dense = pointIndices.empty();
    const size_t deltaCount = dense ? pointOffsets.size() : pointIndices.size();
    if (deltaCount == 0)
    {
        return true;
    }
    if (dense && pointOffsets.size() != pointCount)
    {
        return false;
    }
    if (!dense && pointOffsets.size() < deltaCount)
    {
        return false;
    }
    const bool hasNormalOffsets =
        wantNormalOffsets && normalOffsets.size() >= deltaCount;

    HdSilkMeshBlendRange range;
    range.firstDelta = static_cast<uint32_t>(rig->blendDeltas.size());
    range.deltaCount = static_cast<uint32_t>(deltaCount);
    range.weight = weight;
    for (size_t entry = 0; entry < deltaCount; ++entry)
    {
        const int64_t pointIndex = dense
            ? static_cast<int64_t>(entry)
            : static_cast<int64_t>(pointIndices[entry]);
        if (pointIndex < 0 ||
            pointIndex >= static_cast<int64_t>(pointCount))
        {
            return false;
        }
        HdSilkMeshBlendDelta delta;
        delta.pointIndex = static_cast<uint32_t>(pointIndex);
        for (int component = 0; component < 3; ++component)
        {
            delta.positionOffset[component] = pointOffsets[entry][component];
            delta.normalOffset[component] = hasNormalOffsets
                ? normalOffsets[entry][component]
                : 0.0f;
        }
        rig->blendDeltas.push_back(delta);
    }
    if (hasNormalOffsets)
    {
        rig->flags |= OPENUSD_SILK_DEFORMATION_FLAG_BLEND_NORMAL_OFFSETS;
    }
    rig->blendRanges.push_back(range);
    return true;
}

/// Builds the bounded rig that describes the deformation hdSilk just resolved,
/// enforces every ABI budget before allocating what the budget bounds, and
/// keeps it only when evaluating it reproduces the resolved result. Any refusal
/// leaves the resolved points and normals untouched: the CPU deformation is
/// what this record publishes either way.
void BuildBoundedRig(
    SkelEvaluationScope const& scope,
    HdSilkMeshSkelState const& binding,
    VtVec3fArray const& bindPoints,
    VtVec3fArray const& bindNormals,
    VtMatrix4dArray const& skinningTransforms,
    VtFloatArray const& subShapeWeights,
    VtUIntArray const& blendShapeIndices,
    VtUIntArray const& subShapeIndices,
    SkelDeformation* deformation)
{
    HdSilkMeshDeformation& rig = deformation->rig;
    const UsdSkelSkinningQuery& skinningQuery = binding.skinningQuery;
    const size_t pointCount = bindPoints.size();
    if (pointCount == 0)
    {
        rig.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_GEOMETRY);
        return;
    }
    if (skinningQuery.GetSkinningMethod() != UsdSkelTokens->classicLinear)
    {
        rig.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_SKINNING_METHOD);
        return;
    }

    // A blend-shape-only mesh has no joints at all. It still publishes one
    // bounded representation rather than a second shape of block: a single
    // identity joint weighted one, which evaluates to the identity and leaves
    // the blend-shape stage as the only thing the rig does.
    uint32_t influencesPerPoint = 1;
    VtIntArray jointIndices;
    VtFloatArray jointWeights;
    VtMatrix4dArray palette;
    GfMatrix4d geomBindTransform(1.0);
    if (skinningQuery.HasJointInfluences())
    {
        const int authoredInfluences =
            skinningQuery.GetNumInfluencesPerComponent();
        if (authoredInfluences <= 0)
        {
            rig.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_GEOMETRY);
            return;
        }
        if (static_cast<uint32_t>(authoredInfluences) >
            OPENUSD_SILK_MAX_DEFORMATION_INFLUENCES)
        {
            rig.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_INFLUENCE_BUDGET);
            return;
        }
        influencesPerPoint = static_cast<uint32_t>(authoredInfluences);
        // Constant influences are expanded here so every consumer reads one
        // fixed-width stream instead of branching on a rigid rig.
        if (!skinningQuery.ComputeVaryingJointInfluences(
                pointCount,
                &jointIndices,
                &jointWeights,
                scope.time))
        {
            rig.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_GEOMETRY);
            return;
        }
        if (!TryResolveJointPalette(
                skinningQuery,
                skinningTransforms,
                &palette))
        {
            rig.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_GEOMETRY);
            return;
        }
        if (palette.empty())
        {
            rig.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_GEOMETRY);
            return;
        }
        if (palette.size() > OPENUSD_SILK_MAX_DEFORMATION_JOINTS)
        {
            rig.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_JOINT_BUDGET);
            return;
        }
        geomBindTransform = skinningQuery.GetGeomBindTransform(scope.time);
    }
    else
    {
        palette.assign(1, GfMatrix4d(1.0));
        jointIndices.assign(pointCount, 0);
        jointWeights.assign(pointCount, 1.0f);
    }

    const size_t influenceCount = pointCount * influencesPerPoint;
    if (jointIndices.size() != influenceCount ||
        jointWeights.size() != influenceCount)
    {
        rig.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_GEOMETRY);
        return;
    }
    const uint32_t jointCount = static_cast<uint32_t>(palette.size());
    for (int index : jointIndices)
    {
        if (index < 0 || static_cast<uint32_t>(index) >= jointCount)
        {
            rig.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_GEOMETRY);
            return;
        }
    }

    const bool hasBindNormals =
        deformation->hasNormals && bindNormals.size() == pointCount;

    // The sparse blend tables are sized before they are filled, because the
    // byte budget bounds their product with the point count and the influence
    // width, and none of the individual budgets does.
    std::vector<size_t> activeSubShapes;
    size_t projectedDeltaCount = 0;
    for (size_t entry = 0; entry < subShapeWeights.size(); ++entry)
    {
        if (subShapeWeights[entry] == 0.0f)
        {
            continue;
        }
        if (entry >= blendShapeIndices.size() ||
            entry >= subShapeIndices.size())
        {
            rig.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_GEOMETRY);
            return;
        }
        const size_t blendShape = blendShapeIndices[entry];
        const size_t subShape = subShapeIndices[entry];
        if (blendShape >= binding.blendShapePointIndices.size() ||
            subShape >= binding.subShapePointOffsets.size())
        {
            rig.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_GEOMETRY);
            return;
        }
        const VtIntArray& indices = binding.blendShapePointIndices[blendShape];
        const VtVec3fArray& offsets = binding.subShapePointOffsets[subShape];
        const size_t deltaCount =
            indices.empty() ? offsets.size() : indices.size();
        if (deltaCount == 0)
        {
            continue;
        }
        activeSubShapes.push_back(entry);
        projectedDeltaCount += deltaCount;
    }
    if (activeSubShapes.size() > OPENUSD_SILK_MAX_DEFORMATION_BLEND_RANGES ||
        projectedDeltaCount > OPENUSD_SILK_MAX_DEFORMATION_BLEND_DELTAS)
    {
        rig.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_BLEND_BUDGET);
        return;
    }

    const uint64_t projectedBytes =
        96ull +
        (static_cast<uint64_t>(pointCount) * 3ull * 4ull) +
        (hasBindNormals
            ? static_cast<uint64_t>(pointCount) * 3ull * 4ull
            : 0ull) +
        (static_cast<uint64_t>(influenceCount) * 8ull) +
        (static_cast<uint64_t>(jointCount) * 64ull) +
        (static_cast<uint64_t>(activeSubShapes.size()) * 16ull) +
        (static_cast<uint64_t>(projectedDeltaCount) * 28ull);
    if (projectedBytes > OPENUSD_SILK_MAX_DEFORMATION_BYTES)
    {
        rig.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_BYTE_BUDGET);
        return;
    }

    rig.flags = hasBindNormals
        ? OPENUSD_SILK_DEFORMATION_FLAG_BIND_NORMALS
        : OPENUSD_SILK_DEFORMATION_FLAG_NONE;
    rig.jointCount = jointCount;
    rig.influencesPerPoint = influencesPerPoint;
    FlattenRigMatrix(geomBindTransform, rig.geomBindTransform);
    rig.bindPoints.resize(pointCount * 3);
    for (size_t point = 0; point < pointCount; ++point)
    {
        rig.bindPoints[point * 3] = bindPoints[point][0];
        rig.bindPoints[(point * 3) + 1] = bindPoints[point][1];
        rig.bindPoints[(point * 3) + 2] = bindPoints[point][2];
    }
    if (hasBindNormals)
    {
        rig.bindNormals.resize(pointCount * 3);
        for (size_t point = 0; point < pointCount; ++point)
        {
            rig.bindNormals[point * 3] = bindNormals[point][0];
            rig.bindNormals[(point * 3) + 1] = bindNormals[point][1];
            rig.bindNormals[(point * 3) + 2] = bindNormals[point][2];
        }
    }
    rig.jointIndices.resize(influenceCount);
    rig.jointWeights.resize(influenceCount);
    for (size_t slot = 0; slot < influenceCount; ++slot)
    {
        rig.jointIndices[slot] = static_cast<uint32_t>(jointIndices[slot]);
        rig.jointWeights[slot] = jointWeights[slot];
    }
    rig.jointMatrices.resize(static_cast<size_t>(jointCount) * 16);
    for (size_t joint = 0; joint < palette.size(); ++joint)
    {
        FlattenRigMatrix(
            palette[joint],
            rig.jointMatrices.data() + (joint * 16));
    }
    rig.blendRanges.reserve(activeSubShapes.size());
    rig.blendDeltas.reserve(projectedDeltaCount);
    static const VtVec3fArray emptyOffsets;
    for (size_t entry : activeSubShapes)
    {
        const size_t blendShape = blendShapeIndices[entry];
        const size_t subShape = subShapeIndices[entry];
        const VtVec3fArray& normalOffsets =
            subShape < binding.subShapeNormalOffsets.size()
                ? binding.subShapeNormalOffsets[subShape]
                : emptyOffsets;
        if (!TryAppendBlendRange(
                subShapeWeights[entry],
                binding.blendShapePointIndices[blendShape],
                binding.subShapePointOffsets[subShape],
                normalOffsets,
                pointCount,
                hasBindNormals && binding.hasNormalOffsets,
                &rig))
        {
            rig.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_GEOMETRY);
            return;
        }
    }

    if (!hasBindNormals && deformation->normalsUndeformable)
    {
        // The record omits its authored normals because the deformation cannot
        // carry them, so the rig omits them for the same reason and names it.
        rig.unsupportedFeatures |=
            OPENUSD_SILK_DEFORMATION_UNSUPPORTED_NORMALS;
    }

    if (!RigReproducesDeformation(
            rig,
            deformation->points,
            deformation->normals))
    {
        rig.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_UNVERIFIED);
        return;
    }
    rig.published = true;
}

bool TryComputeUsdSkelDeformation(
    SkelEvaluationScope const& scope,
    SdfPath const& id,
    HdSilkMeshSkelState* binding,
    SkelDeformation* deformation)
{
    if (!scope.stage || binding == nullptr || deformation == nullptr)
    {
        return false;
    }
    if (!binding->resolved || binding->stage != scope.stage.operator->())
    {
        ResolveSkelBinding(scope, id, binding);
    }
    if (!binding->valid)
    {
        return false;
    }

    const UsdPrim prim = scope.stage->GetPrimAtPath(id);
    UsdGeomMesh mesh(prim);
    if (!mesh)
    {
        return false;
    }
    VtVec3fArray points;
    if (!mesh.GetPointsAttr().Get(&points, scope.time) ||
        points.empty())
    {
        return false;
    }

    VtVec3fArray normals;
    const bool authorsNormals = mesh.GetNormalsAttr() &&
        mesh.GetNormalsAttr().HasAuthoredValue();
    const bool hasAuthoredNormals =
        TryGetDeformableNormals(scope, mesh, points.size(), &normals);

    // The bind pose is captured before anything deforms it, because the
    // bounded rig published beside the result describes the same evaluation
    // from the same input rather than being re-derived from the output.
    const VtVec3fArray bindPoints = points;
    const VtVec3fArray bindNormals = normals;

    const UsdSkelSkeletonQuery& skeletonQuery = binding->skeletonQuery;
    const UsdSkelSkinningQuery& skinningQuery = binding->skinningQuery;

    VtFloatArray subShapeWeights;
    VtUIntArray blendShapeIndices;
    VtUIntArray subShapeIndices;
    const bool hasBlendShapes =
        skinningQuery.HasBlendShapes() && binding->blendShapeQuery;
    if (hasBlendShapes)
    {
        if (!TryResolveSubShapeWeights(
                scope,
                skeletonQuery,
                skinningQuery,
                binding->blendShapeQuery,
                &subShapeWeights,
                &blendShapeIndices,
                &subShapeIndices))
        {
            return false;
        }
        if (!binding->blendShapeQuery.ComputeDeformedPoints(
                TfMakeSpan(subShapeWeights),
                TfMakeSpan(blendShapeIndices),
                TfMakeSpan(subShapeIndices),
                binding->blendShapePointIndices,
                binding->subShapePointOffsets,
                TfMakeSpan(points)))
        {
            return false;
        }
        if (hasAuthoredNormals && binding->hasNormalOffsets &&
            !binding->blendShapeQuery.ComputeDeformedNormals(
                TfMakeSpan(subShapeWeights),
                TfMakeSpan(blendShapeIndices),
                TfMakeSpan(subShapeIndices),
                binding->blendShapePointIndices,
                binding->subShapeNormalOffsets,
                TfMakeSpan(normals)))
        {
            return false;
        }
    }

    VtMatrix4dArray skinningTransforms;
    if (skinningQuery.HasJointInfluences())
    {
        if (!skeletonQuery.ComputeSkinningTransforms(
                &skinningTransforms,
                scope.time))
        {
            return false;
        }
        if (!skinningQuery.ComputeSkinnedPoints(
                skinningTransforms,
                &points,
                scope.time))
        {
            return false;
        }
        if (hasAuthoredNormals &&
            !skinningQuery.ComputeSkinnedNormals(
                skinningTransforms,
                &normals,
                scope.time))
        {
            // Rigidly deformed meshes carry constant influences, which
            // ComputeSkinnedNormals refuses; the surface still moved, so the
            // authored normals are reported as undeformable rather than
            // published unchanged.
            normals.clear();
        }
    }
    else if (!skinningQuery.HasBlendShapes())
    {
        return false;
    }

    deformation->points = std::move(points);
    deformation->hasNormals = !normals.empty();
    if (deformation->hasNormals)
    {
        deformation->normals = std::move(normals);
    }
    // Authored normals that survived neither blend-shape nor joint deformation
    // describe the bind pose of a surface that moved. The consumer shades from
    // the deformed points instead of receiving them.
    deformation->normalsUndeformable =
        authorsNormals && !deformation->hasNormals;

    BuildBoundedRig(
        scope,
        *binding,
        bindPoints,
        deformation->hasNormals ? bindNormals : VtVec3fArray(),
        skinningTransforms,
        subShapeWeights,
        blendShapeIndices,
        subShapeIndices,
        deformation);
    return true;
}
}

#if defined(OPENUSD_HDSILK_ENABLE_TEST_HOOKS)
/// Exercises the published-rig self-verification rule against a constructed
/// one-point rig whose degeneracy is chosen by the caller.
///
/// The rule cannot be reached from a stage fixture: making exactly one of the
/// two sides collapse a normal requires authoring a rig whose influences
/// annihilate a direction the CPU path keeps, which no realistic asset does on
/// purpose. Constructing the four combinations directly is what makes the
/// one-sided cases decisive rather than incidental.
bool
HdSilkTestVerifyDegenerateNormalRule(
    bool resolvedDegenerate,
    bool evaluatedDegenerate)
{
    HdSilkMeshDeformation rig;
    rig.flags = OPENUSD_SILK_DEFORMATION_FLAG_BIND_NORMALS;
    rig.jointCount = 1;
    rig.influencesPerPoint = 1;
    rig.bindPoints = {1.0f, 2.0f, 3.0f};
    // A zero bind normal survives every identity transform as a zero normal, so
    // it is the smallest way to make the evaluated side carry no direction.
    rig.bindNormals = evaluatedDegenerate
        ? std::vector<float>{0.0f, 0.0f, 0.0f}
        : std::vector<float>{0.0f, 0.0f, 1.0f};
    rig.jointIndices = {0};
    rig.jointWeights = {1.0f};
    rig.jointMatrices = {
        1.0f, 0.0f, 0.0f, 0.0f,
        0.0f, 1.0f, 0.0f, 0.0f,
        0.0f, 0.0f, 1.0f, 0.0f,
        0.0f, 0.0f, 0.0f, 1.0f};

    const VtVec3fArray resolvedPoints(1, GfVec3f(1.0f, 2.0f, 3.0f));
    const VtVec3fArray resolvedNormals(
        1,
        resolvedDegenerate
            ? GfVec3f(0.0f, 0.0f, 0.0f)
            : GfVec3f(0.0f, 0.0f, 1.0f));
    return RigReproducesDeformation(rig, resolvedPoints, resolvedNormals);
}
#endif

void
HdSilkBeginUsdSkelEvaluation(
    HdSilkSceneState const* sceneState,
    UsdStageRefPtr const& stage,
    UsdTimeCode time)
{
    if (sceneState == nullptr)
    {
        return;
    }
    std::unique_lock<std::shared_mutex> lock(SkelEvaluationMutex());
    SkelEvaluationScope& scope = SkelEvaluationScopes()[sceneState];
    scope.stage = stage;
    scope.time = time;
}

void
HdSilkEndUsdSkelEvaluation(HdSilkSceneState const* sceneState) noexcept
{
    if (sceneState == nullptr)
    {
        return;
    }
    std::unique_lock<std::shared_mutex> lock(SkelEvaluationMutex());
    SkelEvaluationScopes().erase(sceneState);
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
        // Refinement is driven by the display style's refine level, which the
        // session's complexity resolves into, and by the authored subdivision
        // tags. Without both bits the first Sync would refine an unrefined
        // level and never see an edited crease.
        | HdChangeTracker::DirtyDisplayStyle
        | HdChangeTracker::DirtySubdivTags
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
    const bool subdivTagsDirty =
        (*dirtyBits & HdChangeTracker::DirtySubdivTags) != 0;
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
        // Subdivision tags travel separately from topology in Hydra, and the
        // refiner needs them on the same object it is built from.
        _topology.SetSubdivTags(sceneDelegate->GetSubdivTags(id));
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

        _coarseTriangleIndices.clear();
        _coarseTriangleIndices.reserve(triangleIndices.size() * 3);
        _coarseTriangleSubprims.clear();
        _coarseTriangleSubprims.reserve(primitiveParams.size());
        // Authored winding is HdMeshUtil's job, not this delegate's. USD's
        // "orientation" makes rightHanded faces counter-clockwise seen from the
        // front and leftHanded clockwise, and ComputeTriangleIndices already
        // emits a leftHanded face with its corners reversed, so the triangles
        // that arrive here are always wound counter-clockwise-front. Every
        // backend hdSilk targets rasterizes with counter-clockwise front faces,
        // so the wire needs no orientation flag and no correction here.
        // Reversing them again would invert facing for exactly the prims USD
        // already handled, which is measured by the orientation case in
        // hdsilk_probe. The refined path preserves the same contract by handing
        // OpenSubdiv the authored orientation instead of a pre-reversed cage.
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
                _coarseTriangleIndices.push_back(
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
            _coarseTriangleSubprims.push_back(static_cast<uint32_t>(faceIndex));
        }

        // The authored-edge table indexes the coarse triangulation, so it is
        // rebuilt exactly when that triangulation is. A topology this delegate
        // cannot walk publishes no table at all, which refuses exact edge
        // picking with a reason rather than answering it with a diagonal.
        if (!BuildAuthoredEdgeTable(
                _topology,
                _coarseTriangleIndices,
                &_coarseCornerEdges,
                &_coarseAuthoredEdgeCount))
        {
            TF_WARN(
                "hdSilk could not derive authored edges for mesh '%s'; exact "
                "edge picking is unavailable for it",
                id.GetText());
            _coarseCornerEdges.clear();
            _coarseAuthoredEdgeCount = 0;
        }
    }
    else if (subdivTagsDirty)
    {
        _topology.SetSubdivTags(sceneDelegate->GetSubdivTags(id));
    }

    // The session's complexity resolves into a display style refine level for
    // every prim, and that level -- not the authored subdivisionScheme alone --
    // is what decides whether this mesh is refined. Complexity Low resolves to
    // level 0, which is the historical coarse HdMeshUtil path unchanged, so an
    // unrefined session keeps publishing exactly the bytes it always did.
    const int requestedRefineLevel = std::min(
        std::max(GetDisplayStyle(sceneDelegate).refineLevel, 0),
        HdSilkMaxMeshRefineLevel);
    const bool subdivisionRefreshed =
        topologyRefreshed || subdivTagsDirty || primvarsDirty ||
        requestedRefineLevel != _refineLevel;
    if (subdivisionRefreshed)
    {
        _RefreshSubdivision(sceneDelegate, id, requestedRefineLevel);
    }
    if (transformDirty)
    {
        _transform = sceneDelegate->GetTransform(id);
    }

    // Skinned meshes are evaluated directly from UsdSkel bindings when that
    // subset is supported. Other procedurally deformed meshes still publish
    // "points" as an ExtComputation output rather than an authored primvar, so
    // a plain Get() returns nothing and would leave the point array empty while
    // the topology is fully triangulated. Pull computed primvars only as a
    // fallback and let an authored value win only when no computation supplied
    // one.
    bool pointsResolved = false;
    bool deformationRefreshed = false;
    if (pointsDirty || topologyRefreshed || normalsDirty)
    {
        if (topologyRefreshed)
        {
            // Topology identity changes the resolved skinning target, so the
            // cached binding is dropped rather than reused across a resync.
            _skel.resolved = false;
        }
        SkelEvaluationScope scope;
        SkelDeformation deformation;
        if (TryGetSkelEvaluationScope(
                &static_cast<HdSilkRenderParam*>(renderParam)->GetSceneState(),
                &scope) &&
            TryComputeUsdSkelDeformation(scope, id, &_skel, &deformation))
        {
            _points = std::move(deformation.points);
            _skel.deformedNormals = std::move(deformation.normals);
            _skel.normalsUndeformable = deformation.normalsUndeformable;
            _skel.rig = std::move(deformation.rig);
            _skel.deformed = true;
            pointsResolved = true;
            deformationRefreshed = true;
        }
        else if (_skel.deformed)
        {
            _skel.deformedNormals = VtVec3fArray();
            _skel.normalsUndeformable = false;
            _skel.rig = HdSilkMeshDeformation();
            _skel.deformed = false;
            deformationRefreshed = true;
        }
    }
    HdExtComputationPrimvarDescriptorVector dirtyComputedPrimvars;
    if (!pointsResolved)
    {
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

    // Refined positions follow the control points, so an animated point array
    // re-runs interpolation against the refiner the previous frame built rather
    // than rebuilding the refiner itself. A control cage whose point count stops
    // matching the refiner drops back to the coarse topology instead of
    // publishing indices that overrun the refined buffer.
    if (_subdivision.IsRefined() &&
        (pointsDirty || topologyRefreshed || deformationRefreshed ||
         subdivisionRefreshed ||
         _refinedPoints.size() != _subdivision.GetRefinedVertexCount()))
    {
        if (!_subdivision.RefinePoints(_points, &_refinedPoints))
        {
            TF_WARN(
                "hdSilk could not refine points for mesh '%s'; publishing the "
                "control cage instead",
                id.GetText());
            _subdivision.Clear();
            _refinedPoints = VtVec3fArray();
            _triangleIndices = _coarseTriangleIndices;
            _triangleSubprims = _coarseTriangleSubprims;
        }
    }

    if (normalsDirty || primvarsDirty || topologyRefreshed ||
        deformationRefreshed || subdivisionRefreshed)
    {
        const bool expandedBefore = _attributesRequireExpandedTopology;
        _RefreshAttributes(sceneDelegate, id);

        // Expanding the topology so a face-varying primvar can be resolved onto
        // corners, or collapsing it again when that primvar goes away, changes
        // the emitted point array, the index array and every subprim-identity
        // table with it -- even though Hydra reported only a primvar change.
        // Consumers key retained geometry on the topology revision, so it has
        // to advance in both directions or a stale vertex buffer survives a
        // toggle that invalidated it.
        if (_attributesRequireExpandedTopology != expandedBefore &&
            !topologyRefreshed)
        {
            if (_topologyRevision == std::numeric_limits<uint64_t>::max())
            {
                throw std::overflow_error(
                    "The hdSilk mesh topology revision is exhausted.");
            }
            ++_topologyRevision;
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
        materialDirty || cullDirty || subdivisionRefreshed)
    {
        const VtVec3fArray& emittedPoints = _EmittedPoints();
        // An empty mesh is retired rather than published. A record with no
        // points and no indices is byte-identical on the wire to an ABI v8
        // instance reference, so publishing one as a point-instanced prototype
        // would make the payload record itself look like a record that reuses
        // a payload, and every instance of the path would be unresolvable.
        // Points and basisCurves already refuse to publish empty geometry.
        if (emittedPoints.empty() || _triangleIndices.empty())
        {
            TF_WARN(
                "hdSilk skipped mesh '%s': empty points or triangle indices",
                id.GetText());
            static_cast<HdSilkRenderParam*>(renderParam)
                ->GetSceneState()
                .RemoveMesh(id.GetString());
            *dirtyBits = HdChangeTracker::Clean;
            return;
        }

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

        if (emittedPoints.size() > std::numeric_limits<size_t>::max() / 3)
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
                if (pointIndex >= emittedPoints.size())
                {
                    throw std::runtime_error(
                        "An hdSilk expanded vertex references a missing point.");
                }
                const GfVec3f& point = emittedPoints[pointIndex];
                record.points.push_back(point[0]);
                record.points.push_back(point[1]);
                record.points.push_back(point[2]);
                record.indices.push_back(static_cast<uint32_t>(vertex));
            }
        }
        else
        {
            record.points.reserve(emittedPoints.size() * 3);
            for (const GfVec3f& point : emittedPoints)
            {
                record.points.push_back(point[0]);
                record.points.push_back(point[1]);
                record.points.push_back(point[2]);
            }
            record.indices = _triangleIndices;
        }

        record.triangleSubprims = _triangleSubprims;
        record.attributes = _attributes;

        // ABI v22 subprim identity. Authored face identity always travels with
        // `triangle_subprims`, including through refinement, because the
        // refiner records the coarse face every refined face came from.
        //
        // Point and edge identity are refused for a refined subdivision
        // surface. Its emitted vertices are limit-surface vertices the refiner
        // generated and its edges are refined edges; neither corresponds to an
        // authored component, and returning the emitted index as authored
        // identity would name a component the stage does not have.
        record.subprimIdentity = OPENUSD_SILK_SUBPRIM_IDENTITY_FACE;
        record.subprimUnsupported = OPENUSD_SILK_SUBPRIM_UNSUPPORTED_NONE;
        if (_subdivision.IsRefined())
        {
            record.subprimUnsupported |=
                OPENUSD_SILK_SUBPRIM_UNSUPPORTED_REFINED_SUBDIVISION;
        }
        else
        {
            const size_t emittedPointCount = record.points.size() / 3;
            if (emittedPointCount >
                static_cast<size_t>(std::numeric_limits<uint32_t>::max()))
            {
                throw std::overflow_error(
                    "The hdSilk emitted point count overflows uint32.");
            }

            // The budget is checked from the sizes the tables WOULD have, before
            // anything is reserved or copied. Checking after building them would
            // make an oversized mesh pay the whole allocation the budget exists
            // to refuse, which is exactly the cost a hostile or merely enormous
            // stage would impose.
            const size_t plannedCornerEdgeCount =
                (!_coarseCornerEdges.empty() &&
                 _coarseCornerEdges.size() == _triangleIndices.size())
                    ? _coarseCornerEdges.size()
                    : 0;
            if (HdSilkSubprimIdentityExceedsBudget(
                    emittedPointCount,
                    plannedCornerEdgeCount))
            {
                record.RejectSubprimIdentity(
                    OPENUSD_SILK_SUBPRIM_UNSUPPORTED_BUDGET);
            }
            else
            {
                // An expanded topology emits one vertex per triangle corner, so
                // emitted vertex v came from the authored point the coarse
                // triangulation named at that corner. An unexpanded record
                // emits the authored points themselves, so the mapping is the
                // identity for every point the topology actually references.
                // A face-varying mesh that duplicates one authored point across
                // several corners still answers a point pick with the one index
                // the stage authored.
                //
                // An authored point no index references is emitted -- USD lets a
                // mesh carry stray points -- but it is never rasterized by any
                // primitive, so it is not a pick target and must not be named as
                // one. It publishes the sentinel instead, which also keeps
                // authored_point_count honest: a trailing run of stray points
                // does not inflate the authored space a consumer is handed.
                record.pointOrigins.clear();
                record.pointOrigins.reserve(emittedPointCount);
                bool pointOriginsResolved = true;
                if (_attributesRequireExpandedTopology)
                {
                    if (_triangleIndices.size() != emittedPointCount)
                    {
                        pointOriginsResolved = false;
                    }
                    else
                    {
                        for (uint32_t origin : _triangleIndices)
                        {
                            record.pointOrigins.push_back(origin);
                        }
                    }
                }
                else
                {
                    std::vector<bool> referenced(emittedPointCount, false);
                    for (uint32_t index : _triangleIndices)
                    {
                        if (index < emittedPointCount)
                        {
                            referenced[index] = true;
                        }
                    }
                    for (uint32_t point = 0;
                         point < static_cast<uint32_t>(emittedPointCount);
                         ++point)
                    {
                        record.pointOrigins.push_back(
                            referenced[point]
                                ? point
                                : OPENUSD_SILK_SUBPRIM_NONE);
                    }
                }

                if (pointOriginsResolved)
                {
                    // The ABI defines authored_point_count as one past the
                    // largest authored index the table names, not as the size
                    // of the authored array: a consumer must never be handed an
                    // authored space larger than the entries it read, and an
                    // authored point no emitted vertex references is not named
                    // at all.
                    record.authoredPointCount =
                        OneePastLargestNamed(record.pointOrigins);
                    record.subprimIdentity |=
                        OPENUSD_SILK_SUBPRIM_IDENTITY_POINT;
                }
                else
                {
                    std::vector<uint32_t>().swap(record.pointOrigins);
                    record.subprimUnsupported |=
                        OPENUSD_SILK_SUBPRIM_UNSUPPORTED_GEOMETRY;
                }

                // The corner-edge table indexes the coarse triangulation, whose
                // corner order the expanded record preserves, so it needs no
                // remapping for expansion.
                if (plannedCornerEdgeCount != 0)
                {
                    record.cornerEdges = _coarseCornerEdges;
                    record.authoredEdgeCount =
                        OneePastLargestNamed(record.cornerEdges);
                    record.subprimIdentity |=
                        OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE;
                }
                else
                {
                    record.subprimUnsupported |=
                        OPENUSD_SILK_SUBPRIM_UNSUPPORTED_GEOMETRY;
                }
            }
        }

        // A late defensive re-check of the same budget. The preflight above is
        // what keeps the allocation bounded; this catches a table some later
        // transform grew, and releases the capacity rather than only the size.
        if (HdSilkSubprimIdentityExceedsBudget(
                record.pointOrigins.size(),
                record.cornerEdges.size()))
        {
            record.RejectSubprimIdentity(
                OPENUSD_SILK_SUBPRIM_UNSUPPORTED_BUDGET);
        }

        // The rig addresses control points by index. A refined subdivision
        // surface, and a topology expanded so face-varying attributes can be
        // resolved onto corners, both emit a different point array from the one
        // the influences were authored against, so the rig is dropped with its
        // reason rather than published against points it does not describe.
        record.deformation = _skel.rig;
        if (record.deformation.published)
        {
            const size_t emittedPointCount = record.points.size() / 3;
            if (_attributesRequireExpandedTopology ||
                _subdivision.IsRefined() ||
                record.deformation.bindPoints.size() != emittedPointCount * 3)
            {
                record.deformation.Reject(
                    OPENUSD_SILK_DEFORMATION_UNSUPPORTED_GEOMETRY);
            }
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
    bool publishedNormals = false;

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
            if (primvar.name == HdTokens->normals && _skel.deformed)
            {
                if (!_skel.deformedNormals.empty())
                {
                    // The authored array is the bind pose. Publish the array the
                    // same deformation produced for the points travelling with
                    // it, so a rig cannot light a moved surface with the normals
                    // of the surface it moved from. Only per-point normals are
                    // deformable, so the authored interpolation is already
                    // vertex or varying and needs no adjustment.
                    value = VtValue(_skel.deformedNormals);
                }
                else if (_skel.normalsUndeformable)
                {
                    // Bind-pose normals on a deformed surface are wrong rather
                    // than approximate, so the consumer shades from the deformed
                    // points instead of receiving them.
                    continue;
                }
            }
            PendingAttribute attribute;
            attribute.primvar = primvar;
            attribute.interpolation = interpolation;
            if (interpolation == HdInterpolationFaceVarying)
            {
                if (_subdivision.IsRefined())
                {
                    // A refined surface has its own face-varying topology, so
                    // the authored values are refined through the OpenSubdiv
                    // channel bound for this primvar and resolved onto the
                    // refined corners. Triangulating the control cage instead
                    // would carry the coarse UVs onto a surface that no longer
                    // has the coarse corners.
                    const int channel = _subdivision.FindChannel(primvar.name);
                    VtIntArray channelIndices;
                    const VtValue indexedValue = sceneDelegate->GetIndexedPrimvar(
                        id,
                        primvar.name,
                        &channelIndices);
                    std::vector<float> authored;
                    const uint32_t componentCount =
                        FlattenPrimvar(indexedValue, &authored);
                    if (channel < 0 || componentCount == 0 ||
                        authored.size() !=
                            _subdivision.GetCoarseFaceVaryingValueCount(channel) *
                                componentCount ||
                        !_subdivision.RefineFaceVaryingPrimvarOntoCorners(
                            channel,
                            authored,
                            componentCount,
                            &attribute.data))
                    {
                        continue;
                    }
                    attribute.componentCount = componentCount;
                    // Face-varying normals are averaged by the same rules as
                    // vertex normals and come out equally short, so they are
                    // rescaled on the same terms rather than reaching the
                    // shader as sub-unit directions.
                    NormalizeRefinedNormals(
                        primvar.name,
                        componentCount,
                        &attribute.data);
                }
                else if (
                    !ResolveFaceVaryingPrimvar(
                        value,
                        meshUtil,
                        &attribute.data,
                        &attribute.componentCount) ||
                    attribute.data.size() !=
                        _triangleIndices.size() * attribute.componentCount)
                {
                    continue;
                }
                // Face-varying data is one element per triangulated corner in
                // HdMeshUtil's order, which already reflects the authored
                // orientation, so it lines up with _triangleIndices as-is.
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
                    // Uniform data is keyed by authored face, and every emitted
                    // triangle -- refined or not -- already reports the authored
                    // face it descends from, so this expansion needs no
                    // subdivision-specific branch.
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
                else if (
                    _subdivision.IsRefined() &&
                    (interpolation == HdInterpolationVertex ||
                     interpolation == HdInterpolationVarying))
                {
                    if (!_RefineVertexAttribute(
                            interpolation,
                            attribute.primvar,
                            attribute.componentCount,
                            &attribute.data))
                    {
                        continue;
                    }
                }
            }

            publishedNormals =
                publishedNormals || attribute.primvar.name == HdTokens->normals;
            pending.push_back(std::move(attribute));
        }
    }

    // A skinned prim can reach Hydra with its normals published as an
    // ExtComputation output rather than an authored primvar descriptor, in
    // which case the loop above never sees them. The deformation already
    // resolved the per-point array, so publish it directly rather than letting
    // the consumer fall back to topology-derived normals.
    if (_skel.deformed && !_skel.deformedNormals.empty() && !publishedNormals)
    {
        PendingAttribute attribute;
        attribute.primvar = HdPrimvarDescriptor(
            HdTokens->normals,
            HdInterpolationVertex,
            HdPrimvarRoleTokens->normal);
        attribute.interpolation = HdInterpolationVertex;
        attribute.componentCount =
            FlattenPrimvar(VtValue(_skel.deformedNormals), &attribute.data);
        const bool resolved = attribute.componentCount == 3 &&
            !attribute.data.empty() &&
            (!_subdivision.IsRefined() ||
             _RefineVertexAttribute(
                 HdInterpolationVertex,
                 attribute.primvar,
                 attribute.componentCount,
                 &attribute.data));
        if (resolved)
        {
            pending.push_back(std::move(attribute));
        }
    }

    const size_t emittedPointCount = _EmittedPoints().size();
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
                if (elementCount != emittedPointCount ||
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
                    : emittedPointCount))
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

std::vector<HdSilkSubdivisionChannel>
HdSilkMesh::_CollectFaceVaryingChannels(
    HdSceneDelegate* sceneDelegate,
    SdfPath const& id) const
{
    std::vector<HdSilkSubdivisionChannel> channels;
    const size_t faceVertexTotal = _topology.GetFaceVertexIndices().size();
    const bool addressableCage = faceVertexTotal != 0 &&
        faceVertexTotal <= static_cast<size_t>(std::numeric_limits<int>::max());

    const HdPrimvarDescriptorVector primvars =
        sceneDelegate->GetPrimvarDescriptors(id, HdInterpolationFaceVarying);
    for (const HdPrimvarDescriptor& primvar : primvars)
    {
        // Every authored face-varying primvar is reported, including the ones
        // that cannot be described. A primvar dropped here would leave the
        // refiner believing the mesh never authored it, and the refined surface
        // would publish without an authored UV or normal set instead of
        // refusing to refine at all.
        HdSilkSubdivisionChannel channel;
        channel.name = primvar.name;

        VtIntArray indices;
        const VtValue value =
            sceneDelegate->GetIndexedPrimvar(id, primvar.name, &indices);
        if (!addressableCage)
        {
            channel.unsupportedReason =
                "the control cage has no addressable face-vertex list";
        }
        else if (!value.IsArrayValued())
        {
            channel.unsupportedReason = "it does not hold an array";
        }
        else if (value.GetArraySize() == 0)
        {
            channel.unsupportedReason = "it holds an empty array";
        }
        else if (value.GetArraySize() >
                 static_cast<size_t>(std::numeric_limits<int>::max()))
        {
            channel.unsupportedReason =
                "it holds more values than an OpenSubdiv channel can index";
        }
        else if (indices.empty() && value.GetArraySize() != faceVertexTotal)
        {
            // An unindexed face-varying primvar authors one value per corner,
            // so the identity sequence is its topology. Sharing nothing between
            // faces is what USD means by that encoding, and it is the channel
            // Storm builds for the same primvar. An array that is not one value
            // per corner has no such reading.
            channel.unsupportedReason = "it authors " +
                std::to_string(value.GetArraySize()) +
                " unindexed values for " + std::to_string(faceVertexTotal) +
                " face-vertices";
        }
        else
        {
            channel.valueCount = static_cast<int>(value.GetArraySize());
            if (indices.empty())
            {
                indices.resize(faceVertexTotal);
                std::iota(indices.begin(), indices.end(), 0);
            }
            channel.indices = std::move(indices);
        }
        channels.push_back(std::move(channel));
    }
    return channels;
}

bool
HdSilkMesh::_RefineVertexAttribute(
    HdInterpolation interpolation,
    const HdPrimvarDescriptor& primvar,
    uint32_t componentCount,
    std::vector<float>* data) const
{
    std::vector<float> refined;
    const bool ok = interpolation == HdInterpolationVarying
        ? _subdivision.RefineVaryingPrimvar(*data, componentCount, &refined)
        : _subdivision.RefineVertexPrimvar(*data, componentCount, &refined);
    if (!ok)
    {
        return false;
    }
    NormalizeRefinedNormals(primvar.name, componentCount, &refined);
    *data = std::move(refined);
    return true;
}

void
HdSilkMesh::_RefreshSubdivision(
    HdSceneDelegate* sceneDelegate,
    SdfPath const& id,
    int refineLevel)
{
    const int previousLevel = _refineLevel;
    const bool wasRefined = _subdivision.IsRefined();
    _refineLevel = refineLevel;
    _refinedPoints = VtVec3fArray();

    HdSilkSubdivisionStatus status = HdSilkSubdivisionStatus::NotRequested;
    std::string diagnostic;
    if (refineLevel > 0)
    {
        status = _subdivision.Rebuild(
            _topology,
            refineLevel,
            _CollectFaceVaryingChannels(sceneDelegate, id),
            &diagnostic);
    }
    else
    {
        _subdivision.Clear();
    }

    if (status == HdSilkSubdivisionStatus::Refined)
    {
        _triangleIndices = _subdivision.GetTriangleIndices();
        _triangleSubprims = _subdivision.GetTriangleSubprims();
    }
    else
    {
        // Every refusal publishes the whole control cage rather than a partly
        // refined surface: a mesh that is one level short of what was asked for
        // is a different shape, while the cage is at least the shape the scene
        // authored.
        if (status != HdSilkSubdivisionStatus::NotRequested)
        {
            TF_WARN(
                "hdSilk did not refine mesh '%s' at level %d (%s): %s",
                id.GetText(),
                refineLevel,
                HdSilkSubdivisionStatusName(status),
                diagnostic.c_str());
        }
        _triangleIndices = _coarseTriangleIndices;
        _triangleSubprims = _coarseTriangleSubprims;
    }

    // The emitted triangle table is what a consumer keys its retained geometry
    // by, so a refinement change that is not a topology change still has to
    // move the revision. Without this, switching complexity would republish
    // refined points against indices the consumer had already cached. A mesh
    // that is not refined at either level -- an unrefinable scheme, or one this
    // delegate refused -- emits the same control cage both times and keeps its
    // revision, so a complexity switch does not churn geometry it did not
    // change.
    const bool emittedTopologyChanged =
        wasRefined != _subdivision.IsRefined() ||
        (_subdivision.IsRefined() && previousLevel != refineLevel);
    if (emittedTopologyChanged)
    {
        if (_topologyRevision == std::numeric_limits<uint64_t>::max())
        {
            throw std::overflow_error(
                "The hdSilk mesh topology revision is exhausted.");
        }
        ++_topologyRevision;
    }
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

    // ABI v8 carries prototype geometry once. The lowest published instance
    // index of a prototype remains the full prototype record; later records
    // retain only per-instance identity and transform and let consumers reuse
    // that record's geometry and material.
    HdInstancer* instancer =
        sceneDelegate->GetRenderIndex().GetInstancer(instancerId);
    if (instancer == nullptr)
    {
        return {};
    }

    // The published index is the instance's own index inside the instancer,
    // not its position in the resolved array. Those differ whenever the
    // instancer has several prototypes, whenever proto indices vary, and
    // whenever invisibleIds removes an instance, and only the former survives
    // those edits as a stable identity that USD can decode back to a scene
    // instance.
    const std::vector<HdSilkInstanceSample> samples =
        static_cast<HdSilkInstancer*>(instancer)->ComputeInstanceSamples(
            GetId());

    const int32_t instanceId = HdSilkStableInstanceId(instancerId.GetString());
    std::vector<HdSilkMeshRecord> records;
    records.reserve(samples.size());
    // The lightweight reference is built once and copied per instance. It holds
    // no geometry and no identity table, so copying it is O(1) in the
    // prototype's size; copying the prototype record itself per instance was
    // O(points x instances) for data only the payload record publishes.
    const HdSilkMeshRecord reference = HdSilkMakeInstanceReference(record);
    for (size_t position = 0; position < samples.size(); ++position)
    {
        const HdSilkInstanceSample& sample = samples[position];
        if (sample.index > static_cast<int64_t>(
                std::numeric_limits<int32_t>::max()))
        {
            throw std::overflow_error(
                "The hdSilk instance index exceeds the 32-bit instance index.");
        }
        HdSilkMeshRecord instanceRecord =
            position == 0 ? std::move(record) : reference;
        instanceRecord.instanceId = instanceId;
        instanceRecord.instancerPath = instancerId.GetString();
        instanceRecord.instanceIndex = static_cast<int32_t>(sample.index);
        // The complete ordered chain rides beside the composite index. It is
        // the only description that decodes back to a scene instance once more
        // than one instancing level is involved.
        instanceRecord.instancerContext = sample.context;
        // The prototype's own transform applies first, then the instance
        // transform carries it into world space. USD composes row vectors, so
        // that is "_transform * instance", matching hdEmbree.
        HdSilkFlattenMatrix(
            _transform * sample.transform,
            instanceRecord.transform);
        records.push_back(std::move(instanceRecord));
    }
    return records;
}

PXR_NAMESPACE_CLOSE_SCOPE

#if defined(OPENUSD_HDSILK_ENABLE_TEST_HOOKS)
extern "C" OPENUSD_HDSILK_API int32_t
openusd_hdsilk_test_verify_degenerate_normal_rule(
    int32_t resolvedDegenerate,
    int32_t evaluatedDegenerate)
{
    return PXR_NS::HdSilkTestVerifyDegenerateNormalRule(
        resolvedDegenerate != 0,
        evaluatedDegenerate != 0)
        ? 1
        : 0;
}
#endif
