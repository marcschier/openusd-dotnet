// Copyright (c) marcschier. Licensed under the MIT License.

#include "subdivision.h"

#include "pxr/base/tf/diagnostic.h"
#include "pxr/imaging/hd/primvarSchema.h"
#include "pxr/imaging/pxOsd/meshTopologyValidation.h"
#include "pxr/imaging/pxOsd/subdivTags.h"
#include "pxr/imaging/pxOsd/tokens.h"

// OpenSubdiv's public headers are third-party sources built to their own
// warning settings, and hdSilk compiles at /W4 /WX (-Wall -Wextra -Werror). The
// suppression is scoped to the include block so nothing hdSilk writes below it
// is exempt.
#if defined(_MSC_VER)
#pragma warning(push, 0)
#elif defined(__clang__)
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Weverything"
#elif defined(__GNUC__)
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wall"
#pragma GCC diagnostic ignored "-Wextra"
#pragma GCC diagnostic ignored "-Wpedantic"
#endif
#include <opensubdiv/far/primvarRefiner.h>
#include <opensubdiv/far/topologyDescriptor.h>
#include <opensubdiv/far/topologyRefiner.h>
#include <opensubdiv/sdc/options.h>
#include <opensubdiv/sdc/types.h>
#if defined(_MSC_VER)
#pragma warning(pop)
#elif defined(__clang__)
#pragma clang diagnostic pop
#elif defined(__GNUC__)
#pragma GCC diagnostic pop
#endif

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <limits>
#include <numeric>
#include <type_traits>
#include <unordered_map>

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
namespace OsdFar = OpenSubdiv::Far;
namespace OsdSdc = OpenSubdiv::Sdc;

std::atomic<uint64_t> g_refinerBuildCount{0};
std::atomic<uint64_t> g_diagnosticCount{0};
std::atomic<uint64_t> g_refinedVertexBudget{HdSilkMaxRefinedVertices};

/// A tightly packed float primvar viewed as the element sequence OpenSubdiv's
/// PrimvarRefiner expects. OpenSubdiv addresses elements as "buffer[index]" and
/// then calls Clear()/AddWithWeight() on the result, so a proxy returned by
/// value is enough and lets one implementation serve every component count
/// instead of one template instantiation per width.
class PackedElement
{
public:
    PackedElement(float* data, uint32_t componentCount)
        : _data(data)
        , _componentCount(componentCount)
    {
    }

    void Clear()
    {
        for (uint32_t component = 0; component < _componentCount; ++component)
        {
            _data[component] = 0.0F;
        }
    }

    template <typename Weight>
    void AddWithWeight(PackedElement const& source, Weight weight)
    {
        const float scale = static_cast<float>(weight);
        for (uint32_t component = 0; component < _componentCount; ++component)
        {
            _data[component] += scale * source._data[component];
        }
    }

private:
    float* _data;
    uint32_t _componentCount;
};

class PackedBuffer
{
public:
    PackedBuffer(float* data, uint32_t componentCount)
        : _data(data)
        , _componentCount(componentCount)
    {
    }

    PackedElement operator[](int index) const
    {
        return PackedElement(
            _data + (static_cast<size_t>(index) * _componentCount),
            _componentCount);
    }

private:
    float* _data;
    uint32_t _componentCount;
};

bool TryMapScheme(const TfToken& scheme, OsdSdc::SchemeType* out)
{
    if (scheme == PxOsdOpenSubdivTokens->catmullClark)
    {
        *out = OsdSdc::SCHEME_CATMARK;
        return true;
    }
    if (scheme == PxOsdOpenSubdivTokens->bilinear)
    {
        *out = OsdSdc::SCHEME_BILINEAR;
        return true;
    }
    if (scheme == PxOsdOpenSubdivTokens->loop)
    {
        *out = OsdSdc::SCHEME_LOOP;
        return true;
    }
    return false;
}

/// Maps the authored subdivision tags onto OpenSubdiv scheme options.
///
/// The defaults are USD's, not OpenSubdiv's: UsdGeomMesh defaults
/// interpolateBoundary to edgeAndCorner and faceVaryingLinearInterpolation to
/// cornersPlus1, while Sdc::Options defaults to VTX_BOUNDARY_NONE and
/// FVAR_LINEAR_ALL. Taking OpenSubdiv's defaults would leave an unauthored mesh
/// with unsharpened boundaries and bilinear UVs, which is a different surface
/// from the one USD describes.
OsdSdc::Options MapOptions(const PxOsdSubdivTags& tags)
{
    OsdSdc::Options options;

    const TfToken vertexRule = tags.GetVertexInterpolationRule();
    if (vertexRule == PxOsdOpenSubdivTokens->none)
    {
        options.SetVtxBoundaryInterpolation(OsdSdc::Options::VTX_BOUNDARY_NONE);
    }
    else if (vertexRule == PxOsdOpenSubdivTokens->edgeOnly)
    {
        options.SetVtxBoundaryInterpolation(
            OsdSdc::Options::VTX_BOUNDARY_EDGE_ONLY);
    }
    else
    {
        options.SetVtxBoundaryInterpolation(
            OsdSdc::Options::VTX_BOUNDARY_EDGE_AND_CORNER);
    }

    const TfToken fvarRule = tags.GetFaceVaryingInterpolationRule();
    if (fvarRule == PxOsdOpenSubdivTokens->none)
    {
        options.SetFVarLinearInterpolation(OsdSdc::Options::FVAR_LINEAR_NONE);
    }
    else if (fvarRule == PxOsdOpenSubdivTokens->cornersOnly)
    {
        options.SetFVarLinearInterpolation(
            OsdSdc::Options::FVAR_LINEAR_CORNERS_ONLY);
    }
    else if (fvarRule == PxOsdOpenSubdivTokens->cornersPlus2)
    {
        options.SetFVarLinearInterpolation(
            OsdSdc::Options::FVAR_LINEAR_CORNERS_PLUS2);
    }
    else if (fvarRule == PxOsdOpenSubdivTokens->boundaries)
    {
        options.SetFVarLinearInterpolation(
            OsdSdc::Options::FVAR_LINEAR_BOUNDARIES);
    }
    else if (fvarRule == PxOsdOpenSubdivTokens->all)
    {
        options.SetFVarLinearInterpolation(OsdSdc::Options::FVAR_LINEAR_ALL);
    }
    else
    {
        options.SetFVarLinearInterpolation(
            OsdSdc::Options::FVAR_LINEAR_CORNERS_PLUS1);
    }

    options.SetCreasingMethod(
        tags.GetCreaseMethod() == PxOsdOpenSubdivTokens->chaikin
            ? OsdSdc::Options::CREASE_CHAIKIN
            : OsdSdc::Options::CREASE_UNIFORM);
    options.SetTriangleSubdivision(
        tags.GetTriangleSubdivision() == PxOsdOpenSubdivTokens->smooth
            ? OsdSdc::Options::TRI_SUB_SMOOTH
            : OsdSdc::Options::TRI_SUB_CATMARK);
    return options;
}

/// Expands USD's crease-chain encoding into the vertex-index pairs OpenSubdiv
/// wants. USD authors a chain of vertices per crease with either one sharpness
/// per chain or one per edge of that chain; OpenSubdiv only accepts explicit
/// edges. Returns false when the arrays do not describe a well-formed set, so a
/// malformed crease is diagnosed rather than silently sharpening the wrong edge.
bool ExpandCreases(
    const PxOsdSubdivTags& tags,
    int vertexCount,
    std::vector<OsdFar::Index>* pairs,
    std::vector<float>* weights)
{
    pairs->clear();
    weights->clear();

    const VtIntArray& indices = tags.GetCreaseIndices();
    const VtIntArray& lengths = tags.GetCreaseLengths();
    const VtFloatArray& sharpness = tags.GetCreaseWeights();
    if (lengths.empty())
    {
        return indices.empty();
    }

    size_t totalVertices = 0;
    size_t totalEdges = 0;
    for (int length : lengths)
    {
        if (length < 2)
        {
            return false;
        }
        totalVertices += static_cast<size_t>(length);
        totalEdges += static_cast<size_t>(length) - 1;
    }
    if (totalVertices != indices.size())
    {
        return false;
    }
    const bool perEdge = sharpness.size() == totalEdges;
    const bool perCrease = sharpness.size() == lengths.size();
    if (!perEdge && !perCrease)
    {
        return false;
    }

    pairs->reserve(totalEdges * 2);
    weights->reserve(totalEdges);
    size_t cursor = 0;
    size_t edge = 0;
    for (size_t crease = 0; crease < lengths.size(); ++crease)
    {
        const size_t length = static_cast<size_t>(lengths[crease]);
        for (size_t step = 0; step + 1 < length; ++step, ++edge)
        {
            const int first = indices[cursor + step];
            const int second = indices[cursor + step + 1];
            if (first < 0 || second < 0 || first >= vertexCount ||
                second >= vertexCount || first == second)
            {
                return false;
            }
            pairs->push_back(first);
            pairs->push_back(second);
            weights->push_back(perEdge ? sharpness[edge] : sharpness[crease]);
        }
        cursor += length;
    }
    return true;
}

bool ExpandCorners(
    const PxOsdSubdivTags& tags,
    int vertexCount,
    std::vector<OsdFar::Index>* indices,
    std::vector<float>* weights)
{
    indices->clear();
    weights->clear();

    const VtIntArray& authored = tags.GetCornerIndices();
    const VtFloatArray& sharpness = tags.GetCornerWeights();
    if (authored.empty())
    {
        return true;
    }
    if (authored.size() != sharpness.size())
    {
        return false;
    }
    indices->reserve(authored.size());
    weights->reserve(sharpness.size());
    for (size_t corner = 0; corner < authored.size(); ++corner)
    {
        const int vertex = authored[corner];
        if (vertex < 0 || vertex >= vertexCount)
        {
            return false;
        }
        indices->push_back(vertex);
        weights->push_back(sharpness[corner]);
    }
    return true;
}

/// Multiplies with saturation at the bound so a predicted count can overflow
/// the budget without ever overflowing the accumulator itself.
uint64_t SaturatingScale(uint64_t value, uint64_t factor, uint64_t bound)
{
    if (value == 0 || factor == 0)
    {
        return 0;
    }
    if (value > bound / factor)
    {
        return bound;
    }
    return value * factor;
}

uint64_t SaturatingAdd(uint64_t left, uint64_t right, uint64_t bound)
{
    if (left > bound - right)
    {
        return bound;
    }
    return left + right;
}

/// Predicts the component counts of every refinement level from the control
/// cage, so refinement can be refused before a single refined vertex is
/// allocated. The recurrences are exact for the uniform schemes hdSilk
/// evaluates: every level past the first is all-quad for Catmark and Bilinear
/// and all-triangle for Loop.
bool PredictedRefinementFits(
    OsdSdc::SchemeType scheme,
    uint64_t vertices,
    uint64_t edges,
    uint64_t faces,
    uint64_t faceVertices,
    int refineLevel,
    uint64_t vertexBudget,
    uint64_t faceBudget)
{
    const uint64_t saturate = std::numeric_limits<uint64_t>::max() / 8;
    for (int level = 1; level <= refineLevel; ++level)
    {
        uint64_t nextVertices = 0;
        uint64_t nextEdges = 0;
        uint64_t nextFaces = 0;
        if (scheme == OsdSdc::SCHEME_LOOP)
        {
            nextVertices = SaturatingAdd(vertices, edges, saturate);
            nextEdges = SaturatingAdd(
                SaturatingScale(edges, 2, saturate),
                SaturatingScale(faces, 3, saturate),
                saturate);
            nextFaces = SaturatingScale(faces, 4, saturate);
        }
        else
        {
            nextVertices = SaturatingAdd(
                SaturatingAdd(vertices, edges, saturate),
                faces,
                saturate);
            nextFaces = faceVertices;
            nextEdges = SaturatingAdd(
                SaturatingScale(edges, 2, saturate),
                nextFaces,
                saturate);
        }
        vertices = nextVertices;
        edges = nextEdges;
        faces = nextFaces;
        // Every level past the first is regular, so its face-vertex total is
        // just the face count times the regular face size.
        faceVertices = SaturatingScale(
            faces,
            scheme == OsdSdc::SCHEME_LOOP ? 3 : 4,
            saturate);
        if (vertices > vertexBudget || faces > faceBudget)
        {
            return false;
        }
    }
    return true;
}
}

struct HdSilkMeshSubdivision::_Impl
{
    std::unique_ptr<OsdFar::TopologyRefiner> refiner;
    std::vector<TfToken> channelNames;
    std::vector<size_t> coarseFaceVaryingValues;
    std::vector<size_t> refinedFaceVaryingValues;
    // One refined face-varying value index per emitted triangle corner, per
    // channel, parallel to the triangle index table.
    std::vector<std::vector<uint32_t>> faceVaryingCorners;
};

HdSilkMeshSubdivision::HdSilkMeshSubdivision()
    : _impl(std::make_unique<_Impl>())
{
}

HdSilkMeshSubdivision::~HdSilkMeshSubdivision() = default;

void
HdSilkMeshSubdivision::Clear()
{
    _impl->refiner.reset();
    _impl->channelNames.clear();
    _impl->coarseFaceVaryingValues.clear();
    _impl->refinedFaceVaryingValues.clear();
    _impl->faceVaryingCorners.clear();
    _status = HdSilkSubdivisionStatus::NotRequested;
    _refineLevel = 0;
    _coarseVertexCount = 0;
    _refinedVertexCount = 0;
    _triangleIndices.clear();
    _triangleSubprims.clear();
}

uint64_t
HdSilkMeshSubdivision::GetRefinerBuildCount()
{
    return g_refinerBuildCount.load(std::memory_order_relaxed);
}

uint64_t
HdSilkMeshSubdivision::GetDiagnosticCount()
{
    return g_diagnosticCount.load(std::memory_order_relaxed);
}

void
HdSilkMeshSubdivision::SetRefinedVertexBudgetForTesting(uint64_t budget)
{
    g_refinedVertexBudget.store(
        budget == 0 ? HdSilkMaxRefinedVertices : budget,
        std::memory_order_relaxed);
}

int
HdSilkMeshSubdivision::FindChannel(const TfToken& name) const
{
    for (size_t channel = 0; channel < _impl->channelNames.size(); ++channel)
    {
        if (_impl->channelNames[channel] == name)
        {
            return static_cast<int>(channel);
        }
    }
    return -1;
}

size_t
HdSilkMeshSubdivision::GetCoarseFaceVaryingValueCount(int channel) const
{
    if (channel < 0 ||
        static_cast<size_t>(channel) >= _impl->coarseFaceVaryingValues.size())
    {
        return 0;
    }
    return _impl->coarseFaceVaryingValues[static_cast<size_t>(channel)];
}

size_t
HdSilkMeshSubdivision::GetRefinedFaceVaryingValueCount(int channel) const
{
    if (channel < 0 ||
        static_cast<size_t>(channel) >= _impl->refinedFaceVaryingValues.size())
    {
        return 0;
    }
    return _impl->refinedFaceVaryingValues[static_cast<size_t>(channel)];
}

namespace
{
struct PtexCoordinate
{
    float u;
    float v;
};

struct PtexMemoryRange
{
    std::uintptr_t begin = 0;
    std::uintptr_t end = 0;
};

bool MakePtexMemoryRange(
    const void* data,
    size_t count,
    size_t elementSize,
    PtexMemoryRange* range)
{
    if (count == 0)
    {
        *range = {};
        return true;
    }
    if (data == nullptr ||
        count > std::numeric_limits<size_t>::max() / elementSize)
    {
        return false;
    }
    const size_t bytes = count * elementSize;
    const std::uintptr_t begin = reinterpret_cast<std::uintptr_t>(data);
    if (begin > std::numeric_limits<std::uintptr_t>::max() - bytes)
    {
        return false;
    }
    *range = {begin, begin + bytes};
    return true;
}

bool PtexRangesOverlap(const PtexMemoryRange& first, const PtexMemoryRange& second)
{
    return first.begin < second.end && second.begin < first.end;
}

int FindFaceVertex(OsdFar::ConstIndexArray vertices, OsdFar::Index vertex)
{
    for (int corner = 0; corner < vertices.size(); ++corner)
    {
        if (vertices[corner] == vertex)
        {
            return corner;
        }
    }
    return -1;
}

bool MapPtexChildCoordinates(
    const OsdFar::TopologyLevel& parent,
    OsdFar::Index parentFace,
    const OsdFar::TopologyLevel& child,
    OsdFar::Index childFace,
    const std::array<PtexCoordinate, 4>& parentCoordinates,
    std::array<PtexCoordinate, 4>* childCoordinates)
{
    const OsdFar::ConstIndexArray parentVertices =
        parent.GetFaceVertices(parentFace);
    const OsdFar::ConstIndexArray parentEdges =
        parent.GetFaceEdges(parentFace);
    const OsdFar::ConstIndexArray childVertices =
        child.GetFaceVertices(childFace);
    if ((parentVertices.size() != 3 && parentVertices.size() != 4) ||
        parentEdges.size() != parentVertices.size() ||
        childVertices.size() != parentVertices.size())
    {
        return false;
    }

    for (int corner = 0; corner < childVertices.size(); ++corner)
    {
        const OsdFar::Index vertex = childVertices[corner];
        bool found = false;
        PtexCoordinate coordinate{};
        for (int parentCorner = 0;
             parentCorner < parentVertices.size();
             ++parentCorner)
        {
            if (parent.GetVertexChildVertex(parentVertices[parentCorner]) == vertex)
            {
                if (found)
                {
                    return false;
                }
                coordinate = parentCoordinates[static_cast<size_t>(parentCorner)];
                found = true;
            }

            if (parent.GetEdgeChildVertex(parentEdges[parentCorner]) == vertex)
            {
                const OsdFar::ConstIndexArray endpoints =
                    parent.GetEdgeVertices(parentEdges[parentCorner]);
                if (endpoints.size() != 2 || found)
                {
                    return false;
                }
                const int first = FindFaceVertex(parentVertices, endpoints[0]);
                const int second = FindFaceVertex(parentVertices, endpoints[1]);
                if (first < 0 || second < 0)
                {
                    return false;
                }
                const PtexCoordinate a = parentCoordinates[static_cast<size_t>(first)];
                const PtexCoordinate b = parentCoordinates[static_cast<size_t>(second)];
                coordinate = {(a.u + b.u) * 0.5F, (a.v + b.v) * 0.5F};
                found = true;
            }
        }

        if (parentVertices.size() == 4 &&
            parent.GetFaceChildVertex(parentFace) == vertex)
        {
            if (found)
            {
                return false;
            }
            coordinate = {};
            for (int parentCorner = 0; parentCorner < 4; ++parentCorner)
            {
                coordinate.u += parentCoordinates[static_cast<size_t>(parentCorner)].u;
                coordinate.v += parentCoordinates[static_cast<size_t>(parentCorner)].v;
            }
            coordinate.u *= 0.25F;
            coordinate.v *= 0.25F;
            found = true;
        }
        if (!found)
        {
            return false;
        }
        (*childCoordinates)[static_cast<size_t>(corner)] = coordinate;
    }
    return true;
}
}

HdSilkPtexMappingStatus
HdSilkWritePtexFaceOffsets(
    const HdSilkPtexTriangle* mappings,
    size_t mappingCount,
    const uint32_t* sourceFaces,
    size_t sourceFaceCount,
    size_t authoredFaceCount,
    HdSilkPtexFaceOffsets offsets,
    size_t textureFaceCount,
    HdSilkPtexTriangle* output,
    size_t outputCapacity)
{
    const size_t maxCount = static_cast<size_t>(std::numeric_limits<int>::max());
    if (mappingCount > maxCount || authoredFaceCount > maxCount ||
        textureFaceCount > maxCount)
    {
        return HdSilkPtexMappingStatus::ExceedsBounds;
    }
    if (mappingCount != sourceFaceCount)
    {
        return HdSilkPtexMappingStatus::TriangleMismatch;
    }
    if (outputCapacity < mappingCount)
    {
        return HdSilkPtexMappingStatus::InvalidBuffers;
    }
    PtexMemoryRange outputRange;
    std::array<PtexMemoryRange, 4> inputs{};
    if (!MakePtexMemoryRange(output, outputCapacity, sizeof(*output), &outputRange) ||
        !MakePtexMemoryRange(mappings, mappingCount, sizeof(*mappings), &inputs[0]) ||
        !MakePtexMemoryRange(sourceFaces, sourceFaceCount, sizeof(*sourceFaces), &inputs[1]) ||
        !MakePtexMemoryRange(offsets.values, offsets.valueCount, sizeof(*offsets.values), &inputs[2]) ||
        !MakePtexMemoryRange(offsets.indices, offsets.indexCount, sizeof(*offsets.indices), &inputs[3]))
    {
        return HdSilkPtexMappingStatus::InvalidBuffers;
    }
    for (const PtexMemoryRange& input : inputs)
    {
        if (PtexRangesOverlap(outputRange, input))
        {
            return HdSilkPtexMappingStatus::InvalidBuffers;
        }
    }

    size_t expectedValues = 0;
    switch (offsets.interpolation)
    {
    case HdSilkPtexOffsetInterpolation::None:
        if (offsets.valueCount != 0 || offsets.indexCount != 0)
        {
            return HdSilkPtexMappingStatus::InvalidFaceOffsets;
        }
        break;
    case HdSilkPtexOffsetInterpolation::Constant:
        expectedValues = 1;
        break;
    case HdSilkPtexOffsetInterpolation::Uniform:
        expectedValues = authoredFaceCount;
        break;
    default:
        return HdSilkPtexMappingStatus::InvalidFaceOffsets;
    }
    const bool indexed = offsets.indices != nullptr || offsets.indexCount != 0;
    if (offsets.interpolation != HdSilkPtexOffsetInterpolation::None)
    {
        if (indexed)
        {
            if (offsets.indexCount != expectedValues ||
                (expectedValues != 0 && offsets.valueCount == 0))
            {
                return HdSilkPtexMappingStatus::InvalidFaceOffsets;
            }
            for (size_t index = 0; index < offsets.indexCount; ++index)
            {
                if (offsets.indices[index] < 0 ||
                    static_cast<size_t>(offsets.indices[index]) >= offsets.valueCount)
                {
                    return HdSilkPtexMappingStatus::InvalidFaceOffsets;
                }
            }
        }
        else if (offsets.valueCount != expectedValues)
        {
            return HdSilkPtexMappingStatus::InvalidFaceOffsets;
        }
    }

    const auto offsetForFace = [&](size_t face) -> int32_t
    {
        if (offsets.interpolation == HdSilkPtexOffsetInterpolation::None)
        {
            return 0;
        }
        const size_t element = offsets.interpolation == HdSilkPtexOffsetInterpolation::Constant
            ? 0
            : face;
        const size_t value = indexed
            ? static_cast<size_t>(offsets.indices[element])
            : element;
        return offsets.values[value];
    };
    for (size_t triangle = 0; triangle < mappingCount; ++triangle)
    {
        const size_t face = static_cast<size_t>(sourceFaces[triangle]);
        if (face >= authoredFaceCount)
        {
            return HdSilkPtexMappingStatus::UnsupportedTopology;
        }
        const int64_t adjusted = static_cast<int64_t>(mappings[triangle].faceIndex) +
            static_cast<int64_t>(offsetForFace(face));
        if (adjusted < 0 || static_cast<uint64_t>(adjusted) >= textureFaceCount)
        {
            return HdSilkPtexMappingStatus::TextureFaceOutOfRange;
        }
    }
    for (size_t triangle = 0; triangle < mappingCount; ++triangle)
    {
        output[triangle] = mappings[triangle];
        output[triangle].faceIndex = static_cast<uint32_t>(
            static_cast<int64_t>(mappings[triangle].faceIndex) +
            static_cast<int64_t>(offsetForFace(sourceFaces[triangle])));
    }
    return HdSilkPtexMappingStatus::Mapped;
}

HdSilkPtexMappingStatus
HdSilkWritePtexFaceOffsetPrimvar(
    const HdSilkPtexTriangle* mappings,
    size_t mappingCount,
    const uint32_t* sourceFaces,
    size_t sourceFaceCount,
    size_t authoredFaceCount,
    HdInterpolation interpolation,
    int elementSize,
    const VtValue& value,
    const VtIntArray* indices,
    size_t textureFaceCount,
    HdSilkPtexTriangle* output,
    size_t outputCapacity)
{
    static_assert(std::is_same_v<int32_t, int>);

    PtexMemoryRange outputRange;
    PtexMemoryRange valueRange;
    PtexMemoryRange indicesRange;
    if (!MakePtexMemoryRange(output, outputCapacity, sizeof(*output), &outputRange) ||
        !MakePtexMemoryRange(&value, 1, sizeof(value), &valueRange) ||
        !MakePtexMemoryRange(indices, indices == nullptr ? 0 : 1, sizeof(*indices), &indicesRange) ||
        PtexRangesOverlap(outputRange, valueRange) ||
        PtexRangesOverlap(outputRange, indicesRange))
    {
        return HdSilkPtexMappingStatus::InvalidBuffers;
    }

    HdSilkPtexFaceOffsets offsets;
    if (interpolation == HdInterpolationConstant)
    {
        offsets.interpolation = HdSilkPtexOffsetInterpolation::Constant;
    }
    else if (interpolation == HdInterpolationUniform)
    {
        offsets.interpolation = HdSilkPtexOffsetInterpolation::Uniform;
    }
    else
    {
        return HdSilkPtexMappingStatus::InvalidFaceOffsets;
    }
    if (elementSize != 1)
    {
        return HdSilkPtexMappingStatus::InvalidFaceOffsets;
    }

    if (value.IsHolding<int>())
    {
        if (indices != nullptr)
        {
            return HdSilkPtexMappingStatus::InvalidFaceOffsets;
        }
        offsets.values = &value.UncheckedGet<int>();
        offsets.valueCount = 1;
    }
    else if (value.IsHolding<VtIntArray>())
    {
        const VtIntArray& values = value.UncheckedGet<VtIntArray>();
        offsets.values = values.cdata();
        offsets.valueCount = values.size();
    }
    else
    {
        return HdSilkPtexMappingStatus::InvalidFaceOffsets;
    }

    const int32_t indexedMarker = 0;
    if (indices != nullptr)
    {
        offsets.indices = indices->empty() ? &indexedMarker : indices->cdata();
        offsets.indexCount = indices->size();
    }
    return HdSilkWritePtexFaceOffsets(
        mappings, mappingCount, sourceFaces, sourceFaceCount, authoredFaceCount,
        offsets, textureFaceCount, output, outputCapacity);
}

HdSilkPtexMappingStatus
HdSilkWritePtexFaceOffsetDataSource(
    const HdSilkPtexTriangle* mappings,
    size_t mappingCount,
    const uint32_t* sourceFaces,
    size_t sourceFaceCount,
    size_t authoredFaceCount,
    const HdPrimvarSchema& primvar,
    HdSilkPtexMissingElementSize missingElementSize,
    float shutterOffset,
    size_t textureFaceCount,
    HdSilkPtexTriangle* output,
    size_t outputCapacity)
{
    const size_t maxCount = static_cast<size_t>(std::numeric_limits<int>::max());
    if (mappingCount > maxCount || authoredFaceCount > maxCount ||
        textureFaceCount > maxCount)
    {
        return HdSilkPtexMappingStatus::ExceedsBounds;
    }
    if (mappingCount != sourceFaceCount)
    {
        return HdSilkPtexMappingStatus::TriangleMismatch;
    }
    PtexMemoryRange outputRange;
    PtexMemoryRange schemaRange;
    if (outputCapacity < mappingCount ||
        !MakePtexMemoryRange(output, outputCapacity, sizeof(*output), &outputRange) ||
        !MakePtexMemoryRange(&primvar, 1, sizeof(primvar), &schemaRange) ||
        PtexRangesOverlap(outputRange, schemaRange))
    {
        return HdSilkPtexMappingStatus::InvalidBuffers;
    }
    if (!std::isfinite(shutterOffset) ||
        (missingElementSize != HdSilkPtexMissingElementSize::Reject &&
         missingElementSize != HdSilkPtexMissingElementSize::KnownDefaultOne))
    {
        return HdSilkPtexMappingStatus::InvalidFaceOffsets;
    }

    const HdContainerDataSourceHandle container = primvar.GetContainer();
    if (!container)
    {
        return HdSilkPtexMappingStatus::InvalidFaceOffsets;
    }
    const HdTokenDataSourceHandle interpolationSource = primvar.GetInterpolation();
    if (!interpolationSource)
    {
        return HdSilkPtexMappingStatus::InvalidFaceOffsets;
    }
    const TfToken interpolationToken = interpolationSource->GetTypedValue(shutterOffset);
    HdInterpolation interpolation;
    if (interpolationToken == HdPrimvarSchemaTokens->constant)
    {
        interpolation = HdInterpolationConstant;
    }
    else if (interpolationToken == HdPrimvarSchemaTokens->uniform)
    {
        interpolation = HdInterpolationUniform;
    }
    else
    {
        return HdSilkPtexMappingStatus::InvalidFaceOffsets;
    }

    const HdDataSourceBaseHandle elementSizeSource =
        container->Get(HdPrimvarSchemaTokens->elementSize);
    if (elementSizeSource)
    {
        const HdIntDataSourceHandle elementSize = HdIntDataSource::Cast(elementSizeSource);
        if (!elementSize || elementSize->GetTypedValue(shutterOffset) != 1)
        {
            return HdSilkPtexMappingStatus::InvalidFaceOffsets;
        }
    }
    else if (missingElementSize != HdSilkPtexMissingElementSize::KnownDefaultOne)
    {
        return HdSilkPtexMappingStatus::InvalidFaceOffsets;
    }

    const HdDataSourceBaseHandle indexedValueSource =
        container->Get(HdPrimvarSchemaTokens->indexedPrimvarValue);
    const HdDataSourceBaseHandle indicesSource =
        container->Get(HdPrimvarSchemaTokens->indices);
    if (static_cast<bool>(indexedValueSource) != static_cast<bool>(indicesSource))
    {
        return HdSilkPtexMappingStatus::InvalidFaceOffsets;
    }
    // Indexed tuple width is unknowable when element-size metadata is absent.
    if (!elementSizeSource &&
        missingElementSize == HdSilkPtexMissingElementSize::KnownDefaultOne &&
        indexedValueSource)
    {
        return HdSilkPtexMappingStatus::InvalidFaceOffsets;
    }
    const HdDataSourceBaseHandle rawValueSource = indexedValueSource
        ? indexedValueSource
        : container->Get(HdPrimvarSchemaTokens->primvarValue);
    const HdSampledDataSourceHandle valueSource = HdSampledDataSource::Cast(rawValueSource);
    if (!valueSource)
    {
        return HdSilkPtexMappingStatus::InvalidFaceOffsets;
    }
    const VtValue value = valueSource->GetValue(shutterOffset);
    if (!indicesSource)
    {
        return HdSilkWritePtexFaceOffsetPrimvar(
            mappings, mappingCount, sourceFaces, sourceFaceCount, authoredFaceCount,
            interpolation, 1, value, nullptr, textureFaceCount, output, outputCapacity);
    }
    const HdIntArrayDataSourceHandle indexArray = HdIntArrayDataSource::Cast(indicesSource);
    if (!indexArray)
    {
        return HdSilkPtexMappingStatus::InvalidFaceOffsets;
    }
    const VtIntArray indices = indexArray->GetTypedValue(shutterOffset);
    return HdSilkWritePtexFaceOffsetPrimvar(
        mappings, mappingCount, sourceFaces, sourceFaceCount, authoredFaceCount,
        interpolation, 1, value, &indices, textureFaceCount, output, outputCapacity);
}

HdSilkPtexMappingStatus
HdSilkPlanDefaultCoarsePtexTriQuads(
    const HdMeshTopology& topology,
    size_t sourcePointCount,
    HdSilkPtexCoarseTriQuadPlan* plan)
{
    if (plan == nullptr)
    {
        return HdSilkPtexMappingStatus::InvalidBuffers;
    }

    const VtIntArray& counts = topology.GetFaceVertexCounts();
    const VtIntArray& vertices = topology.GetFaceVertexIndices();
    const VtIntArray& holes = topology.GetHoleIndices();
    PtexMemoryRange planRange;
    std::array<PtexMemoryRange, 3> inputRanges{};
    if (!MakePtexMemoryRange(plan, 1, sizeof(*plan), &planRange) ||
        !MakePtexMemoryRange(counts.cdata(), counts.size(), sizeof(int), &inputRanges[0]) ||
        !MakePtexMemoryRange(vertices.cdata(), vertices.size(), sizeof(int), &inputRanges[1]) ||
        !MakePtexMemoryRange(holes.cdata(), holes.size(), sizeof(int), &inputRanges[2]))
    {
        return HdSilkPtexMappingStatus::InvalidBuffers;
    }
    for (const PtexMemoryRange& input : inputRanges)
    {
        if (PtexRangesOverlap(planRange, input))
        {
            return HdSilkPtexMappingStatus::InvalidBuffers;
        }
    }

    OsdSdc::SchemeType scheme = OsdSdc::SCHEME_CATMARK;
    if (!TryMapScheme(topology.GetScheme(), &scheme) ||
        scheme == OsdSdc::SCHEME_LOOP ||
        (topology.GetOrientation() != PxOsdOpenSubdivTokens->rightHanded &&
         topology.GetOrientation() != PxOsdOpenSubdivTokens->leftHanded))
    {
        return HdSilkPtexMappingStatus::UnsupportedTopology;
    }
    const size_t maxIndex = static_cast<size_t>(std::numeric_limits<int>::max());
    if (counts.size() > maxIndex || vertices.size() > maxIndex ||
        holes.size() > maxIndex)
    {
        return HdSilkPtexMappingStatus::ExceedsBounds;
    }
    int previousHole = -1;
    for (int hole : holes)
    {
        if (hole < 0 || hole <= previousHole ||
            static_cast<size_t>(hole) >= counts.size())
        {
            return HdSilkPtexMappingStatus::UnsupportedTopology;
        }
        previousHole = hole;
    }

    size_t vertexOffset = 0;
    size_t holeOffset = 0;
    size_t inputPointCount = 0;
    uint64_t additionalPoints = 0;
    uint64_t quadCount = 0;
    uint64_t nextPtexFace = 0;
    for (size_t face = 0; face < counts.size(); ++face)
    {
        const int cornerCount = counts[face];
        if (cornerCount < 3 ||
            static_cast<size_t>(cornerCount) > vertices.size() - vertexOffset)
        {
            return HdSilkPtexMappingStatus::UnsupportedTopology;
        }
        for (int corner = 0; corner < cornerCount; ++corner)
        {
            const int vertex = vertices[vertexOffset + static_cast<size_t>(corner)];
            if (vertex < 0)
            {
                return HdSilkPtexMappingStatus::UnsupportedTopology;
            }
            if (static_cast<size_t>(vertex) >= maxIndex)
            {
                return HdSilkPtexMappingStatus::ExceedsBounds;
            }
            if (static_cast<size_t>(vertex) >= sourcePointCount)
            {
                return HdSilkPtexMappingStatus::InvalidPoints;
            }
            inputPointCount = std::max(inputPointCount, static_cast<size_t>(vertex) + 1);
        }
        vertexOffset += static_cast<size_t>(cornerCount);

        const uint64_t faceCount = cornerCount == 4
            ? 1u
            : static_cast<uint64_t>(cornerCount);
        nextPtexFace += faceCount;
        if (nextPtexFace > static_cast<uint64_t>(maxIndex))
        {
            return HdSilkPtexMappingStatus::FaceIndexOverflow;
        }
        const bool hole = holeOffset < holes.size() &&
            static_cast<size_t>(holes[holeOffset]) == face;
        if (hole)
        {
            ++holeOffset;
            continue;
        }
        quadCount += faceCount;
        if (cornerCount != 4)
        {
            additionalPoints += static_cast<uint64_t>(cornerCount) + 1;
        }
        if (quadCount > static_cast<uint64_t>(maxIndex / 6) ||
            additionalPoints > static_cast<uint64_t>(maxIndex))
        {
            return HdSilkPtexMappingStatus::ExceedsBounds;
        }
    }
    if (vertexOffset != vertices.size())
    {
        return HdSilkPtexMappingStatus::UnsupportedTopology;
    }
    if (additionalPoints > static_cast<uint64_t>(maxIndex - inputPointCount))
    {
        return HdSilkPtexMappingStatus::ExceedsBounds;
    }

    HdSilkPtexCoarseTriQuadPlan result;
    result.inputPointCount = inputPointCount;
    result.outputPointCount = inputPointCount + static_cast<size_t>(additionalPoints);
    result.triangleCount = static_cast<size_t>(quadCount * 2);
    result.scratchCount = inputPointCount;
    *plan = result;
    return HdSilkPtexMappingStatus::Mapped;
}

HdSilkPtexMappingStatus
HdSilkWriteDefaultCoarsePtexTriQuads(
    const HdMeshTopology& topology,
    const VtVec3fArray& sourcePoints,
    GfVec3f* points,
    size_t pointCapacity,
    uint32_t* triangleIndices,
    size_t indexCapacity,
    uint32_t* triangleSubprims,
    size_t subprimCapacity,
    HdSilkPtexTriangle* mappings,
    size_t mappingCapacity,
    uint32_t* scratch,
    size_t scratchCapacity)
{
    HdSilkPtexCoarseTriQuadPlan plan;
    const HdSilkPtexMappingStatus planned =
        HdSilkPlanDefaultCoarsePtexTriQuads(topology, sourcePoints.size(), &plan);
    if (planned != HdSilkPtexMappingStatus::Mapped)
    {
        return planned;
    }
    if (pointCapacity < plan.outputPointCount ||
        indexCapacity < plan.triangleCount * 3 ||
        subprimCapacity < plan.triangleCount ||
        mappingCapacity < plan.triangleCount ||
        scratchCapacity < plan.scratchCount)
    {
        return HdSilkPtexMappingStatus::InvalidBuffers;
    }
    const VtIntArray& counts = topology.GetFaceVertexCounts();
    const VtIntArray& vertices = topology.GetFaceVertexIndices();
    const VtIntArray& holes = topology.GetHoleIndices();
    std::array<PtexMemoryRange, 5> outputRanges{};
    std::array<PtexMemoryRange, 4> inputRanges{};
    if (!MakePtexMemoryRange(points, pointCapacity, sizeof(*points), &outputRanges[0]) ||
        !MakePtexMemoryRange(
            triangleIndices, indexCapacity, sizeof(*triangleIndices), &outputRanges[1]) ||
        !MakePtexMemoryRange(
            triangleSubprims, subprimCapacity, sizeof(*triangleSubprims), &outputRanges[2]) ||
        !MakePtexMemoryRange(mappings, mappingCapacity, sizeof(*mappings), &outputRanges[3]) ||
        !MakePtexMemoryRange(scratch, scratchCapacity, sizeof(*scratch), &outputRanges[4]) ||
        !MakePtexMemoryRange(counts.cdata(), counts.size(), sizeof(int), &inputRanges[0]) ||
        !MakePtexMemoryRange(vertices.cdata(), vertices.size(), sizeof(int), &inputRanges[1]) ||
        !MakePtexMemoryRange(holes.cdata(), holes.size(), sizeof(int), &inputRanges[2]) ||
        !MakePtexMemoryRange(
            sourcePoints.cdata(), sourcePoints.size(), sizeof(GfVec3f), &inputRanges[3]))
    {
        return HdSilkPtexMappingStatus::InvalidBuffers;
    }
    for (size_t output = 0; output < outputRanges.size(); ++output)
    {
        for (size_t other = output + 1; other < outputRanges.size(); ++other)
        {
            if (PtexRangesOverlap(outputRanges[output], outputRanges[other]))
            {
                return HdSilkPtexMappingStatus::InvalidBuffers;
            }
        }
        for (const PtexMemoryRange& input : inputRanges)
        {
            if (PtexRangesOverlap(outputRanges[output], input))
            {
                return HdSilkPtexMappingStatus::InvalidBuffers;
            }
        }
    }
    for (size_t point = 0; point < plan.inputPointCount; ++point)
    {
        for (int component = 0; component < 3; ++component)
        {
            if (!std::isfinite(sourcePoints[point][component]))
            {
                return HdSilkPtexMappingStatus::InvalidPoints;
            }
        }
    }
    if (plan.scratchCount != 0)
    {
        std::fill_n(scratch, plan.scratchCount, std::numeric_limits<uint32_t>::max());
    }
    size_t vertexOffset = 0;
    for (size_t face = 0; face < counts.size(); ++face)
    {
        const size_t cornerCount = static_cast<size_t>(counts[face]);
        for (size_t corner = 0; corner < cornerCount; ++corner)
        {
            const size_t vertex = static_cast<size_t>(vertices[vertexOffset + corner]);
            if (scratch[vertex] == static_cast<uint32_t>(face))
            {
                return HdSilkPtexMappingStatus::UnsupportedTopology;
            }
            scratch[vertex] = static_cast<uint32_t>(face);
        }
        vertexOffset += cornerCount;
    }
    if (plan.inputPointCount != 0)
    {
        std::copy_n(sourcePoints.cdata(), plan.inputPointCount, points);
    }

    constexpr std::array<size_t, 6> triangleCorners{{0, 1, 2, 2, 3, 0}};
    constexpr std::array<PtexCoordinate, 4> coordinates{{
        {0.0F, 0.0F}, {1.0F, 0.0F}, {1.0F, 1.0F}, {0.0F, 1.0F}}};
    size_t triangleOffset = 0;
    const auto emitQuad = [&](
        const std::array<uint32_t, 4>& quad, uint32_t sourceFace, uint32_t ptexFace)
    {
        for (size_t triangle = 0; triangle < 2; ++triangle)
        {
            HdSilkPtexTriangle mapping;
            mapping.faceIndex = ptexFace;
            for (size_t corner = 0; corner < 3; ++corner)
            {
                const size_t quadCorner = triangleCorners[triangle * 3 + corner];
                triangleIndices[triangleOffset * 3 + corner] = quad[quadCorner];
                mapping.cornerCoordinates[corner * 2] = coordinates[quadCorner].u;
                mapping.cornerCoordinates[corner * 2 + 1] = coordinates[quadCorner].v;
            }
            triangleSubprims[triangleOffset] = sourceFace;
            mappings[triangleOffset] = mapping;
            ++triangleOffset;
        }
    };

    const bool leftHanded =
        topology.GetOrientation() == PxOsdOpenSubdivTokens->leftHanded;
    vertexOffset = 0;
    size_t pointOffset = plan.inputPointCount;
    size_t holeOffset = 0;
    uint32_t nextPtexFace = 0;
    for (size_t face = 0; face < counts.size(); ++face)
    {
        const size_t cornerCount = static_cast<size_t>(counts[face]);
        const uint32_t faceCount = cornerCount == 4
            ? 1u
            : static_cast<uint32_t>(cornerCount);
        const bool hole = holeOffset < holes.size() &&
            static_cast<size_t>(holes[holeOffset]) == face;
        if (hole)
        {
            ++holeOffset;
        }
        else if (cornerCount == 4)
        {
            const std::array<uint32_t, 4> quad{{
                static_cast<uint32_t>(vertices[vertexOffset]),
                static_cast<uint32_t>(vertices[vertexOffset + (leftHanded ? 3 : 1)]),
                static_cast<uint32_t>(vertices[vertexOffset + 2]),
                static_cast<uint32_t>(vertices[vertexOffset + (leftHanded ? 1 : 3)])}};
            emitQuad(quad, static_cast<uint32_t>(face), nextPtexFace);
        }
        else
        {
            std::array<double, 3> center{{0.0, 0.0, 0.0}};
            for (size_t corner = 0; corner < cornerCount; ++corner)
            {
                const size_t next = (corner + 1) % cornerCount;
                const GfVec3f& first =
                    sourcePoints[static_cast<size_t>(vertices[vertexOffset + corner])];
                const GfVec3f& second =
                    sourcePoints[static_cast<size_t>(vertices[vertexOffset + next])];
                for (int component = 0; component < 3; ++component)
                {
                    points[pointOffset + corner][component] = static_cast<float>(
                        (static_cast<double>(first[component]) + second[component]) * 0.5);
                    center[static_cast<size_t>(component)] += first[component];
                }
            }
            for (int component = 0; component < 3; ++component)
            {
                points[pointOffset + cornerCount][component] = static_cast<float>(
                    center[static_cast<size_t>(component)] / static_cast<double>(cornerCount));
            }
            for (size_t corner = 0; corner < cornerCount; ++corner)
            {
                const size_t previous = (corner + cornerCount - 1) % cornerCount;
                const std::array<uint32_t, 4> quad{{
                    static_cast<uint32_t>(vertices[vertexOffset + corner]),
                    static_cast<uint32_t>(pointOffset + (leftHanded ? previous : corner)),
                    static_cast<uint32_t>(pointOffset + cornerCount),
                    static_cast<uint32_t>(pointOffset + (leftHanded ? corner : previous))}};
                const size_t ptexCorner = leftHanded && corner != 0
                    ? cornerCount - corner
                    : corner;
                emitQuad(
                    quad, static_cast<uint32_t>(face),
                    nextPtexFace + static_cast<uint32_t>(ptexCorner));
            }
            pointOffset += cornerCount + 1;
        }
        nextPtexFace += faceCount;
        vertexOffset += cornerCount;
    }
    return pointOffset == plan.outputPointCount && triangleOffset == plan.triangleCount
        ? HdSilkPtexMappingStatus::Mapped
        : HdSilkPtexMappingStatus::UnsupportedTopology;
}

size_t
HdSilkCoarsePtexScratchCount(const HdMeshTopology& topology)
{
    const size_t faces = topology.GetFaceVertexCounts().size();
    return faces > static_cast<size_t>(std::numeric_limits<int>::max()) ||
            faces > std::numeric_limits<size_t>::max() / 3
        ? 0
        : faces * 3;
}

HdSilkPtexMappingStatus
HdSilkWriteDefaultCoarsePtexTriangles(
    const HdMeshTopology& topology,
    const VtArray<uint32_t>& triangleIndices,
    const VtArray<uint32_t>& triangleSubprims,
    HdSilkPtexTriangle* triangles,
    size_t triangleCapacity,
    uint32_t* scratch,
    size_t scratchCapacity)
{
    const VtIntArray& counts = topology.GetFaceVertexCounts();
    const VtIntArray& vertices = topology.GetFaceVertexIndices();
    const VtIntArray& holes = topology.GetHoleIndices();
    const size_t faceCount = counts.size();
    const size_t requiredScratch = HdSilkCoarsePtexScratchCount(topology);
    if (requiredScratch == 0 ||
        vertices.size() > static_cast<size_t>(std::numeric_limits<int>::max()) ||
        triangleSubprims.size() > static_cast<size_t>(std::numeric_limits<int>::max()) ||
        triangleIndices.size() % 3 != 0 ||
        triangleIndices.size() / 3 != triangleSubprims.size())
    {
        return HdSilkPtexMappingStatus::UnsupportedTopology;
    }
    if ((triangleSubprims.size() != 0 && triangles == nullptr) ||
        scratch == nullptr ||
        triangleCapacity < triangleSubprims.size() ||
        scratchCapacity < requiredScratch)
    {
        return HdSilkPtexMappingStatus::InvalidBuffers;
    }

    PtexMemoryRange outputRange;
    PtexMemoryRange scratchRange;
    std::array<PtexMemoryRange, 5> inputRanges{};
    if (!MakePtexMemoryRange(
            triangles, triangleSubprims.size(), sizeof(HdSilkPtexTriangle), &outputRange) ||
        !MakePtexMemoryRange(scratch, requiredScratch, sizeof(uint32_t), &scratchRange) ||
        !MakePtexMemoryRange(counts.cdata(), counts.size(), sizeof(int), &inputRanges[0]) ||
        !MakePtexMemoryRange(vertices.cdata(), vertices.size(), sizeof(int), &inputRanges[1]) ||
        !MakePtexMemoryRange(holes.cdata(), holes.size(), sizeof(int), &inputRanges[2]) ||
        !MakePtexMemoryRange(
            triangleIndices.cdata(), triangleIndices.size(), sizeof(uint32_t), &inputRanges[3]) ||
        !MakePtexMemoryRange(
            triangleSubprims.cdata(), triangleSubprims.size(), sizeof(uint32_t), &inputRanges[4]) ||
        PtexRangesOverlap(outputRange, scratchRange))
    {
        return HdSilkPtexMappingStatus::InvalidBuffers;
    }
    for (const PtexMemoryRange& input : inputRanges)
    {
        if (PtexRangesOverlap(outputRange, input) ||
            PtexRangesOverlap(scratchRange, input))
        {
            return HdSilkPtexMappingStatus::InvalidBuffers;
        }
    }

    OsdSdc::SchemeType scheme = OsdSdc::SCHEME_CATMARK;
    if (!TryMapScheme(topology.GetScheme(), &scheme) ||
        (topology.GetOrientation() != PxOsdOpenSubdivTokens->rightHanded &&
         topology.GetOrientation() != PxOsdOpenSubdivTokens->leftHanded))
    {
        return HdSilkPtexMappingStatus::UnsupportedTopology;
    }
    const bool loop = scheme == OsdSdc::SCHEME_LOOP;
    const bool leftHanded =
        topology.GetOrientation() == PxOsdOpenSubdivTokens->leftHanded;
    uint32_t* const faceOffsets = scratch;
    uint32_t* const vertexOffsets = scratch + faceCount;
    uint32_t* const observed = scratch + 2 * faceCount;
    std::fill_n(observed, faceCount, 0u);

    uint64_t nextFaceId = 0;
    size_t nextVertex = 0;
    for (size_t face = 0; face < faceCount; ++face)
    {
        const int cornerCount = counts[face];
        if (cornerCount < 3 || (loop && cornerCount != 3) ||
            static_cast<size_t>(cornerCount) > vertices.size() - nextVertex)
        {
            return HdSilkPtexMappingStatus::UnsupportedTopology;
        }
        const uint64_t ptexCount = loop || cornerCount == 4
            ? 1u
            : static_cast<uint64_t>(cornerCount);
        if (nextFaceId + ptexCount >
            static_cast<uint64_t>(std::numeric_limits<int>::max()))
        {
            return HdSilkPtexMappingStatus::FaceIndexOverflow;
        }
        faceOffsets[face] = static_cast<uint32_t>(nextFaceId);
        vertexOffsets[face] = static_cast<uint32_t>(nextVertex);
        nextFaceId += ptexCount;
        nextVertex += static_cast<size_t>(cornerCount);
    }
    if (nextVertex != vertices.size())
    {
        return HdSilkPtexMappingStatus::UnsupportedTopology;
    }
    for (int vertex : vertices)
    {
        if (vertex < 0)
        {
            return HdSilkPtexMappingStatus::UnsupportedTopology;
        }
    }

    constexpr uint32_t holeSentinel = std::numeric_limits<uint32_t>::max();
    int previousHole = -1;
    for (int hole : holes)
    {
        if (hole <= previousHole || hole < 0 ||
            static_cast<size_t>(hole) >= faceCount)
        {
            return HdSilkPtexMappingStatus::UnsupportedTopology;
        }
        observed[static_cast<size_t>(hole)] = holeSentinel;
        previousHole = hole;
    }
    for (size_t face = 0; face < faceCount; ++face)
    {
        if (observed[face] == holeSentinel)
        {
            continue;
        }
        if (!loop && counts[face] != 4)
        {
            return HdSilkPtexMappingStatus::UnsupportedTopology;
        }
        const size_t offset = vertexOffsets[face];
        for (int first = 0; first < counts[face]; ++first)
        {
            for (int second = first + 1; second < counts[face]; ++second)
            {
                if (vertices[offset + static_cast<size_t>(first)] ==
                    vertices[offset + static_cast<size_t>(second)])
                {
                    return HdSilkPtexMappingStatus::UnsupportedTopology;
                }
            }
        }
    }

    for (size_t triangle = 0; triangle < triangleSubprims.size(); ++triangle)
    {
        const size_t face = triangleSubprims[triangle];
        if (face >= faceCount || observed[face] == holeSentinel)
        {
            return HdSilkPtexMappingStatus::TriangleMismatch;
        }
        const uint32_t ordinal = observed[face]++;
        const int cornerCount = counts[face];
        if (ordinal >= static_cast<uint32_t>(cornerCount - 2))
        {
            return HdSilkPtexMappingStatus::TriangleMismatch;
        }

        std::array<int, 3> corners{};
        if (loop)
        {
            corners = leftHanded
                ? std::array<int, 3>{{0, 2, 1}}
                : std::array<int, 3>{{0, 1, 2}};
        }
        else if (leftHanded)
        {
            corners = ordinal == 0
                ? std::array<int, 3>{{2, 1, 0}}
                : std::array<int, 3>{{2, 0, 3}};
        }
        else
        {
            corners = ordinal == 0
                ? std::array<int, 3>{{0, 1, 2}}
                : std::array<int, 3>{{0, 2, 3}};
        }

        std::array<PtexCoordinate, 4> coordinates{{
            {0.0F, 0.0F}, {1.0F, 0.0F}, {1.0F, 1.0F}, {0.0F, 1.0F}}};
        if (loop)
        {
            coordinates[2] = {0.0F, 1.0F};
        }
        HdSilkPtexTriangle result;
        result.faceIndex = faceOffsets[face];
        for (size_t corner = 0; corner < corners.size(); ++corner)
        {
            const int authoredCorner = corners[corner];
            if (triangleIndices[triangle * 3 + corner] != static_cast<uint32_t>(
                    vertices[vertexOffsets[face] + static_cast<size_t>(authoredCorner)]))
            {
                return HdSilkPtexMappingStatus::TriangleMismatch;
            }
            const int ptexCorner = leftHanded && authoredCorner != 0
                ? cornerCount - authoredCorner
                : authoredCorner;
            const PtexCoordinate coordinate = coordinates[static_cast<size_t>(ptexCorner)];
            result.cornerCoordinates[corner * 2] = coordinate.u;
            result.cornerCoordinates[corner * 2 + 1] = coordinate.v;
        }
        triangles[triangle] = result;
    }

    for (size_t face = 0; face < faceCount; ++face)
    {
        if (observed[face] != holeSentinel &&
            observed[face] != static_cast<uint32_t>(counts[face] - 2))
        {
            return HdSilkPtexMappingStatus::TriangleMismatch;
        }
    }
    return HdSilkPtexMappingStatus::Mapped;
}

size_t
HdSilkMeshSubdivision::GetPtexScratchCount() const
{
    if (!IsRefined() || !_impl->refiner)
    {
        return 0;
    }
    return static_cast<size_t>(_impl->refiner->GetLevel(0).GetNumFaces()) +
        static_cast<size_t>(_impl->refiner->GetLevel(1).GetNumFaces());
}

HdSilkPtexMappingStatus
HdSilkMeshSubdivision::WriteDefaultPtexTriangles(
    HdSilkPtexTriangle* triangles,
    size_t triangleCapacity,
    uint32_t* scratch,
    size_t scratchCapacity) const
{
    if (!IsRefined() || !_impl->refiner)
    {
        return HdSilkPtexMappingStatus::RefinerUnavailable;
    }

    const OsdFar::TopologyRefiner& refiner = *_impl->refiner;
    const OsdFar::TopologyLevel& coarse = refiner.GetLevel(0);
    const OsdFar::TopologyLevel& first = refiner.GetLevel(1);
    const OsdFar::TopologyLevel& last = refiner.GetLevel(_refineLevel);
    const size_t coarseFaceCount = static_cast<size_t>(coarse.GetNumFaces());
    const size_t firstFaceCount = static_cast<size_t>(first.GetNumFaces());
    if (triangles == nullptr || scratch == nullptr ||
        triangleCapacity < _triangleSubprims.size() ||
        scratchCapacity < coarseFaceCount + firstFaceCount)
    {
        return HdSilkPtexMappingStatus::InvalidBuffers;
    }
    if (_refineLevel < 1 || _refineLevel > HdSilkMaxMeshRefineLevel ||
        _triangleIndices.size() != _triangleSubprims.size() * 3)
    {
        return HdSilkPtexMappingStatus::UnsupportedTopology;
    }

    const std::uintptr_t triangleBegin = reinterpret_cast<std::uintptr_t>(triangles);
    const std::uintptr_t scratchBegin = reinterpret_cast<std::uintptr_t>(scratch);
    const size_t triangleBytes =
        _triangleSubprims.size() * sizeof(HdSilkPtexTriangle);
    const size_t scratchBytes =
        (coarseFaceCount + firstFaceCount) * sizeof(uint32_t);
    if (triangleBegin > std::numeric_limits<std::uintptr_t>::max() - triangleBytes ||
        scratchBegin > std::numeric_limits<std::uintptr_t>::max() - scratchBytes ||
        (triangleBegin < scratchBegin + scratchBytes &&
            scratchBegin < triangleBegin + triangleBytes))
    {
        return HdSilkPtexMappingStatus::InvalidBuffers;
    }

    const bool loop = refiner.GetSchemeType() == OsdSdc::SCHEME_LOOP;
    uint32_t* const faceOffsets = scratch;
    uint32_t* const childOrdinals = scratch + coarseFaceCount;
    std::fill_n(childOrdinals, firstFaceCount, std::numeric_limits<uint32_t>::max());
    uint64_t nextFaceId = 0;
    for (int face = 0; face < coarse.GetNumFaces(); ++face)
    {
        const int cornerCount = coarse.GetFaceVertices(face).size();
        if (cornerCount < 3 || (loop && cornerCount != 3))
        {
            return HdSilkPtexMappingStatus::UnsupportedTopology;
        }
        const uint64_t faceCount = loop || cornerCount == 4
            ? 1u
            : static_cast<uint64_t>(cornerCount);
        if (nextFaceId + faceCount >
            static_cast<uint64_t>(std::numeric_limits<int>::max()))
        {
            return HdSilkPtexMappingStatus::FaceIndexOverflow;
        }
        faceOffsets[static_cast<size_t>(face)] =
            static_cast<uint32_t>(nextFaceId);
        nextFaceId += faceCount;
        const OsdFar::ConstIndexArray children = coarse.GetFaceChildFaces(face);
        for (int child = 0; child < children.size(); ++child)
        {
            const OsdFar::Index childFace = children[child];
            if (childFace < 0 || static_cast<size_t>(childFace) >= firstFaceCount ||
                childOrdinals[static_cast<size_t>(childFace)] !=
                    std::numeric_limits<uint32_t>::max())
            {
                return HdSilkPtexMappingStatus::UnsupportedTopology;
            }
            childOrdinals[static_cast<size_t>(childFace)] =
                static_cast<uint32_t>(child);
        }
    }

    size_t triangleIndex = 0;
    for (int face = 0; face < last.GetNumFaces(); ++face)
    {
        if (last.IsFaceHole(face))
        {
            continue;
        }

        std::array<OsdFar::Index, HdSilkMaxMeshRefineLevel + 1> facePath{};
        facePath[static_cast<size_t>(_refineLevel)] = face;
        for (int level = _refineLevel; level > 0; --level)
        {
            const OsdFar::Index parent = refiner.GetLevel(level).GetFaceParentFace(
                facePath[static_cast<size_t>(level)]);
            if (parent < 0)
            {
                return HdSilkPtexMappingStatus::UnsupportedTopology;
            }
            facePath[static_cast<size_t>(level - 1)] = parent;
        }

        const OsdFar::Index coarseFace = facePath[0];
        const OsdFar::ConstIndexArray coarseVertices =
            coarse.GetFaceVertices(coarseFace);
        uint32_t ptexFace = faceOffsets[static_cast<size_t>(coarseFace)];
        std::array<PtexCoordinate, 4> coordinates{{
            {0.0F, 0.0F}, {1.0F, 0.0F}, {1.0F, 1.0F}, {0.0F, 1.0F}}};
        int firstLevel = 0;
        if (loop)
        {
            coordinates[2] = {0.0F, 1.0F};
        }
        else if (coarseVertices.size() != 4)
        {
            const OsdFar::ConstIndexArray children =
                coarse.GetFaceChildFaces(coarseFace);
            const OsdFar::ConstIndexArray edges =
                coarse.GetFaceEdges(coarseFace);
            const OsdFar::Index firstFace = facePath[1];
            if (firstFace < 0 || static_cast<size_t>(firstFace) >= firstFaceCount ||
                childOrdinals[static_cast<size_t>(firstFace)] >=
                    static_cast<uint32_t>(children.size()) ||
                children.size() != coarseVertices.size() ||
                edges.size() != coarseVertices.size())
            {
                return HdSilkPtexMappingStatus::UnsupportedTopology;
            }
            const int childIndex =
                static_cast<int>(childOrdinals[static_cast<size_t>(firstFace)]);
            if (children[childIndex] != firstFace)
            {
                return HdSilkPtexMappingStatus::UnsupportedTopology;
            }
            const OsdFar::ConstIndexArray childVertices =
                refiner.GetLevel(1).GetFaceVertices(facePath[1]);
            const int previous =
                (childIndex + coarseVertices.size() - 1) % coarseVertices.size();
            if (childVertices.size() != 4 ||
                childVertices[0] !=
                    coarse.GetVertexChildVertex(coarseVertices[childIndex]) ||
                childVertices[1] != coarse.GetEdgeChildVertex(edges[childIndex]) ||
                childVertices[2] != coarse.GetFaceChildVertex(coarseFace) ||
                childVertices[3] != coarse.GetEdgeChildVertex(edges[previous]))
            {
                return HdSilkPtexMappingStatus::UnsupportedTopology;
            }
            ptexFace += static_cast<uint32_t>(childIndex);
            firstLevel = 1;
        }

        for (int level = firstLevel + 1; level <= _refineLevel; ++level)
        {
            std::array<PtexCoordinate, 4> childCoordinates{};
            if (!MapPtexChildCoordinates(
                    refiner.GetLevel(level - 1),
                    facePath[static_cast<size_t>(level - 1)],
                    refiner.GetLevel(level),
                    facePath[static_cast<size_t>(level)],
                    coordinates,
                    &childCoordinates))
            {
                return HdSilkPtexMappingStatus::UnsupportedTopology;
            }
            coordinates = childCoordinates;
        }

        const OsdFar::ConstIndexArray corners = last.GetFaceVertices(face);
        if (corners.size() != 3 && corners.size() != 4)
        {
            return HdSilkPtexMappingStatus::UnsupportedTopology;
        }
        for (int corner = 1; corner + 1 < corners.size(); ++corner)
        {
            if (triangleIndex >= _triangleSubprims.size() ||
                _triangleSubprims[triangleIndex] !=
                    static_cast<uint32_t>(coarseFace) ||
                _triangleIndices[triangleIndex * 3] !=
                    static_cast<uint32_t>(corners[0]) ||
                _triangleIndices[triangleIndex * 3 + 1] !=
                    static_cast<uint32_t>(corners[corner]) ||
                _triangleIndices[triangleIndex * 3 + 2] !=
                    static_cast<uint32_t>(corners[corner + 1]))
            {
                return HdSilkPtexMappingStatus::TriangleMismatch;
            }
            const PtexCoordinate a = coordinates[0];
            const PtexCoordinate b = coordinates[static_cast<size_t>(corner)];
            const PtexCoordinate c = coordinates[static_cast<size_t>(corner + 1)];
            triangles[triangleIndex] = {
                ptexFace, {a.u, a.v, b.u, b.v, c.u, c.v}};
            ++triangleIndex;
        }
    }

    return triangleIndex == _triangleSubprims.size()
        ? HdSilkPtexMappingStatus::Mapped
        : HdSilkPtexMappingStatus::TriangleMismatch;
}

HdSilkSubdivisionStatus
HdSilkMeshSubdivision::Rebuild(
    HdMeshTopology const& topology,
    int refineLevel,
    std::vector<HdSilkSubdivisionChannel> channels,
    std::string* diagnostic)
{
    Clear();
    if (diagnostic != nullptr)
    {
        diagnostic->clear();
    }

    const TfToken scheme = topology.GetScheme();
    if (refineLevel <= 0 || scheme == PxOsdOpenSubdivTokens->none)
    {
        _status = HdSilkSubdivisionStatus::NotRequested;
        return _status;
    }
    if (refineLevel > HdSilkMaxMeshRefineLevel)
    {
        refineLevel = HdSilkMaxMeshRefineLevel;
    }

    const auto fail = [&](HdSilkSubdivisionStatus status, std::string reason)
    {
        Clear();
        _status = status;
        if (diagnostic != nullptr)
        {
            if (reason.size() > HdSilkMaxSubdivisionDiagnosticLength)
            {
                reason.resize(HdSilkMaxSubdivisionDiagnosticLength - 3);
                reason += "...";
            }
            *diagnostic = std::move(reason);
        }
        g_diagnosticCount.fetch_add(1, std::memory_order_relaxed);
        return _status;
    };

    OsdSdc::SchemeType schemeType = OsdSdc::SCHEME_CATMARK;
    if (!TryMapScheme(scheme, &schemeType))
    {
        return fail(
            HdSilkSubdivisionStatus::UnsupportedScheme,
            "subdivisionScheme '" + scheme.GetString() +
                "' is not evaluated by hdSilk");
    }

    const PxOsdMeshTopologyValidation validation =
        topology.GetPxOsdMeshTopology().Validate();
    if (!validation)
    {
        std::string reason = "the control cage failed OpenSubdiv validation";
        if (validation.begin() != validation.end())
        {
            reason += ": " + validation.begin()->message;
        }
        return fail(HdSilkSubdivisionStatus::InvalidTopology, std::move(reason));
    }

    const VtIntArray& faceVertexCounts = topology.GetFaceVertexCounts();
    const VtIntArray& faceVertexIndices = topology.GetFaceVertexIndices();
    if (faceVertexCounts.empty() || faceVertexIndices.empty())
    {
        return fail(
            HdSilkSubdivisionStatus::InvalidTopology,
            "the control cage has no faces");
    }

    size_t faceVertexTotal = 0;
    int maxVertexIndex = -1;
    for (int count : faceVertexCounts)
    {
        if (count < 3)
        {
            return fail(
                HdSilkSubdivisionStatus::InvalidTopology,
                "a control-cage face has fewer than three vertices");
        }
        if (schemeType == OsdSdc::SCHEME_LOOP && count != 3)
        {
            return fail(
                HdSilkSubdivisionStatus::InvalidTopology,
                "the loop scheme requires an all-triangle control cage");
        }
        faceVertexTotal += static_cast<size_t>(count);
    }
    if (faceVertexTotal != faceVertexIndices.size())
    {
        return fail(
            HdSilkSubdivisionStatus::InvalidTopology,
            "faceVertexCounts and faceVertexIndices disagree");
    }
    for (int index : faceVertexIndices)
    {
        if (index < 0)
        {
            return fail(
                HdSilkSubdivisionStatus::InvalidTopology,
                "a control-cage face references a negative vertex");
        }
        maxVertexIndex = std::max(maxVertexIndex, index);
    }
    const int vertexCount = maxVertexIndex + 1;

    std::vector<OsdFar::Index> creasePairs;
    std::vector<float> creaseWeights;
    std::vector<OsdFar::Index> cornerIndices;
    std::vector<float> cornerWeights;
    const PxOsdSubdivTags& tags = topology.GetSubdivTags();
    if (!ExpandCreases(tags, vertexCount, &creasePairs, &creaseWeights))
    {
        return fail(
            HdSilkSubdivisionStatus::InvalidTopology,
            "the authored crease chains are malformed");
    }
    if (!ExpandCorners(tags, vertexCount, &cornerIndices, &cornerWeights))
    {
        return fail(
            HdSilkSubdivisionStatus::InvalidTopology,
            "the authored corner sharpness is malformed");
    }

    std::vector<OsdFar::Index> holeIndices;
    for (int hole : topology.GetHoleIndices())
    {
        if (hole >= 0 && static_cast<size_t>(hole) < faceVertexCounts.size())
        {
            holeIndices.push_back(hole);
        }
    }

    // Every channel must address every face-vertex exactly once, otherwise
    // OpenSubdiv would read past the array it was handed. A channel that does
    // not is refused rather than skipped: dropping it would publish a refined
    // surface whose authored UVs or normals silently disappeared, which is
    // indistinguishable downstream from a material that never bound them. The
    // control cage still carries the primvar, so the refusal loses nothing.
    size_t unsupportedChannels = 0;
    std::string firstUnsupported;
    for (const HdSilkSubdivisionChannel& channel : channels)
    {
        std::string reason = channel.unsupportedReason;
        if (reason.empty())
        {
            if (channel.valueCount <= 0)
            {
                reason = "it declares no face-varying values";
            }
            else if (channel.indices.size() != faceVertexTotal)
            {
                reason = "it addresses " +
                    std::to_string(channel.indices.size()) +
                    " of " + std::to_string(faceVertexTotal) +
                    " face-vertices";
            }
            else
            {
                for (int value : channel.indices)
                {
                    if (value < 0 || value >= channel.valueCount)
                    {
                        reason = "it indexes value " + std::to_string(value) +
                            " of " + std::to_string(channel.valueCount);
                        break;
                    }
                }
            }
        }
        if (reason.empty())
        {
            continue;
        }
        if (unsupportedChannels == 0)
        {
            firstUnsupported = "face-varying primvar '" +
                channel.name.GetString() + "' cannot be refined: " + reason;
        }
        ++unsupportedChannels;
    }
    if (unsupportedChannels != 0)
    {
        if (unsupportedChannels > 1)
        {
            firstUnsupported += " (and " +
                std::to_string(unsupportedChannels - 1) +
                " further face-varying primvars)";
        }
        return fail(
            HdSilkSubdivisionStatus::UnsupportedFaceVarying,
            std::move(firstUnsupported));
    }

    std::vector<OsdFar::TopologyDescriptor::FVarChannel> descriptorChannels;
    descriptorChannels.reserve(channels.size());
    for (const HdSilkSubdivisionChannel& channel : channels)
    {
        OsdFar::TopologyDescriptor::FVarChannel entry;
        entry.numValues = channel.valueCount;
        entry.valueIndices = channel.indices.cdata();
        descriptorChannels.push_back(entry);
    }

    OsdFar::TopologyDescriptor descriptor;
    descriptor.numVertices = vertexCount;
    descriptor.numFaces = static_cast<int>(faceVertexCounts.size());
    descriptor.numVertsPerFace = faceVertexCounts.cdata();
    descriptor.vertIndicesPerFace = faceVertexIndices.cdata();
    descriptor.numCreases = static_cast<int>(creaseWeights.size());
    descriptor.creaseVertexIndexPairs =
        creasePairs.empty() ? nullptr : creasePairs.data();
    descriptor.creaseWeights =
        creaseWeights.empty() ? nullptr : creaseWeights.data();
    descriptor.numCorners = static_cast<int>(cornerWeights.size());
    descriptor.cornerVertexIndices =
        cornerIndices.empty() ? nullptr : cornerIndices.data();
    descriptor.cornerWeights =
        cornerWeights.empty() ? nullptr : cornerWeights.data();
    descriptor.numHoles = static_cast<int>(holeIndices.size());
    descriptor.holeIndices = holeIndices.empty() ? nullptr : holeIndices.data();
    // USD's leftHanded orientation is the authored winding, not a correction
    // this delegate applies: OpenSubdiv reverses the face itself so refined
    // faces always come out counter-clockwise-front, which is the only winding
    // the hdSilk wire carries.
    descriptor.isLeftHanded =
        topology.GetOrientation() == PxOsdOpenSubdivTokens->leftHanded;
    descriptor.numFVarChannels = static_cast<int>(descriptorChannels.size());
    descriptor.fvarChannels =
        descriptorChannels.empty() ? nullptr : descriptorChannels.data();

    using RefinerFactory = OsdFar::TopologyRefinerFactory<OsdFar::TopologyDescriptor>;
    RefinerFactory::Options factoryOptions(schemeType, MapOptions(tags));
    factoryOptions.validateFullTopology = true;

    std::unique_ptr<OsdFar::TopologyRefiner> refiner(
        RefinerFactory::Create(descriptor, factoryOptions));
    if (!refiner)
    {
        return fail(
            HdSilkSubdivisionStatus::InvalidTopology,
            "OpenSubdiv refused the control cage as non-manifold or invalid");
    }

    const OsdFar::TopologyLevel& coarse = refiner->GetLevel(0);
    const uint64_t vertexBudget =
        g_refinedVertexBudget.load(std::memory_order_relaxed);
    if (!PredictedRefinementFits(
            schemeType,
            static_cast<uint64_t>(coarse.GetNumVertices()),
            static_cast<uint64_t>(coarse.GetNumEdges()),
            static_cast<uint64_t>(coarse.GetNumFaces()),
            static_cast<uint64_t>(coarse.GetNumFaceVertices()),
            refineLevel,
            vertexBudget,
            HdSilkMaxRefinedFaces))
    {
        return fail(
            HdSilkSubdivisionStatus::ExceedsBounds,
            "refining to level " + std::to_string(refineLevel) +
                " would exceed the hdSilk refined mesh budget");
    }

    OsdFar::TopologyRefiner::UniformOptions uniformOptions(refineLevel);
    // The last level is walked for its faces, its hole tags, and its
    // face-varying values, so it needs its full topology rather than the
    // interpolation-only subset OpenSubdiv generates by default.
    uniformOptions.fullTopologyInLastLevel = true;
    refiner->RefineUniform(uniformOptions);
    if (refiner->GetMaxLevel() < refineLevel)
    {
        return fail(
            HdSilkSubdivisionStatus::RefinerFailed,
            "OpenSubdiv did not reach the requested refinement level");
    }
    g_refinerBuildCount.fetch_add(1, std::memory_order_relaxed);

    const OsdFar::TopologyLevel& last = refiner->GetLevel(refineLevel);
    const uint64_t refinedVertices = static_cast<uint64_t>(last.GetNumVertices());
    const uint64_t refinedFaces = static_cast<uint64_t>(last.GetNumFaces());
    if (refinedVertices > vertexBudget || refinedFaces > HdSilkMaxRefinedFaces)
    {
        return fail(
            HdSilkSubdivisionStatus::ExceedsBounds,
            "the refined mesh exceeded the hdSilk refined mesh budget");
    }

    // Map each refined face back to the authored face it descends from, so a
    // uniform primvar or a material subset keyed by authored face still
    // resolves. Walking one level at a time keeps the map exact for the mixed
    // face sizes only the first level can have.
    std::vector<uint32_t> coarseFace(static_cast<size_t>(refinedFaces), 0);
    for (int face = 0; face < last.GetNumFaces(); ++face)
    {
        int current = face;
        for (int level = refineLevel; level > 0; --level)
        {
            current = refiner->GetLevel(level).GetFaceParentFace(current);
            if (current < 0)
            {
                break;
            }
        }
        coarseFace[static_cast<size_t>(face)] =
            current < 0 ? 0u : static_cast<uint32_t>(current);
    }

    _impl->refiner = std::move(refiner);
    _impl->channelNames.reserve(channels.size());
    _impl->coarseFaceVaryingValues.reserve(channels.size());
    _impl->refinedFaceVaryingValues.reserve(channels.size());
    _impl->faceVaryingCorners.resize(channels.size());
    for (size_t channel = 0; channel < channels.size(); ++channel)
    {
        _impl->channelNames.push_back(channels[channel].name);
        _impl->coarseFaceVaryingValues.push_back(
            static_cast<size_t>(channels[channel].valueCount));
        _impl->refinedFaceVaryingValues.push_back(
            static_cast<size_t>(last.GetNumFVarValues(static_cast<int>(channel))));
    }

    _refineLevel = refineLevel;
    _coarseVertexCount = static_cast<size_t>(coarse.GetNumVertices());
    _refinedVertexCount = static_cast<size_t>(refinedVertices);

    // A hole tag propagates to every child face, so skipping holed faces at the
    // last level drops exactly the authored holes -- the same faces
    // HdMeshUtil::ComputeTriangleIndices drops on the unrefined path.
    _triangleIndices.reserve(static_cast<size_t>(refinedFaces) * 6);
    _triangleSubprims.reserve(static_cast<size_t>(refinedFaces) * 2);
    for (size_t channel = 0; channel < _impl->faceVaryingCorners.size(); ++channel)
    {
        _impl->faceVaryingCorners[channel].reserve(
            static_cast<size_t>(refinedFaces) * 6);
    }
    for (int face = 0; face < last.GetNumFaces(); ++face)
    {
        if (last.IsFaceHole(face))
        {
            continue;
        }
        const OpenSubdiv::Far::ConstIndexArray corners =
            last.GetFaceVertices(face);
        if (corners.size() < 3)
        {
            continue;
        }
        for (int corner = 1; corner + 1 < corners.size(); ++corner)
        {
            _triangleIndices.push_back(static_cast<uint32_t>(corners[0]));
            _triangleIndices.push_back(static_cast<uint32_t>(corners[corner]));
            _triangleIndices.push_back(static_cast<uint32_t>(corners[corner + 1]));
            _triangleSubprims.push_back(coarseFace[static_cast<size_t>(face)]);
            for (size_t channel = 0;
                 channel < _impl->faceVaryingCorners.size();
                 ++channel)
            {
                const OpenSubdiv::Far::ConstIndexArray values =
                    last.GetFaceFVarValues(face, static_cast<int>(channel));
                std::vector<uint32_t>& table = _impl->faceVaryingCorners[channel];
                table.push_back(static_cast<uint32_t>(values[0]));
                table.push_back(static_cast<uint32_t>(values[corner]));
                table.push_back(static_cast<uint32_t>(values[corner + 1]));
            }
        }
    }

    if (_triangleIndices.empty())
    {
        return fail(
            HdSilkSubdivisionStatus::InvalidTopology,
            "refinement produced no renderable faces");
    }

    _status = HdSilkSubdivisionStatus::Refined;
    return _status;
}

bool
HdSilkMeshSubdivision::_RefineLevels(
    const std::vector<float>& coarse,
    uint32_t componentCount,
    bool varying,
    std::vector<float>* refined) const
{
    if (!IsRefined() || componentCount == 0 || refined == nullptr)
    {
        return false;
    }
    // A control cage may author more points than its faces reference, and the
    // trailing values are simply unaddressed by any face, so a longer source is
    // accepted and its leading control values are the ones refined.
    const size_t coarseValues = _coarseVertexCount * componentCount;
    if (coarse.size() < coarseValues)
    {
        return false;
    }

    OsdFar::TopologyRefiner& refiner = *_impl->refiner;
    const size_t totalVertices =
        static_cast<size_t>(refiner.GetNumVerticesTotal());
    std::vector<float> buffer(totalVertices * componentCount, 0.0F);
    std::memcpy(buffer.data(), coarse.data(), coarseValues * sizeof(float));

    OsdFar::PrimvarRefiner primvarRefiner(refiner);
    size_t sourceOffset = 0;
    for (int level = 1; level <= _refineLevel; ++level)
    {
        const size_t sourceCount =
            static_cast<size_t>(refiner.GetLevel(level - 1).GetNumVertices());
        PackedBuffer source(
            buffer.data() + (sourceOffset * componentCount),
            componentCount);
        PackedBuffer destination(
            buffer.data() + ((sourceOffset + sourceCount) * componentCount),
            componentCount);
        if (varying)
        {
            primvarRefiner.InterpolateVarying(level, source, destination);
        }
        else
        {
            primvarRefiner.Interpolate(level, source, destination);
        }
        sourceOffset += sourceCount;
    }

    refined->assign(
        buffer.begin() +
            static_cast<std::ptrdiff_t>(sourceOffset * componentCount),
        buffer.begin() +
            static_cast<std::ptrdiff_t>(
                (sourceOffset + _refinedVertexCount) * componentCount));
    return true;
}

bool
HdSilkMeshSubdivision::RefineVertexPrimvar(
    const std::vector<float>& coarse,
    uint32_t componentCount,
    std::vector<float>* refined) const
{
    return _RefineLevels(coarse, componentCount, false, refined);
}

bool
HdSilkMeshSubdivision::RefineVaryingPrimvar(
    const std::vector<float>& coarse,
    uint32_t componentCount,
    std::vector<float>* refined) const
{
    return _RefineLevels(coarse, componentCount, true, refined);
}

bool
HdSilkMeshSubdivision::RefinePoints(
    const VtVec3fArray& coarse,
    VtVec3fArray* refined) const
{
    if (!IsRefined() || refined == nullptr ||
        coarse.size() < _coarseVertexCount)
    {
        return false;
    }
    std::vector<float> packed(_coarseVertexCount * 3);
    for (size_t point = 0; point < _coarseVertexCount; ++point)
    {
        packed[point * 3] = coarse[point][0];
        packed[(point * 3) + 1] = coarse[point][1];
        packed[(point * 3) + 2] = coarse[point][2];
    }
    std::vector<float> refinedPacked;
    if (!_RefineLevels(packed, 3, false, &refinedPacked))
    {
        return false;
    }
    refined->resize(refinedPacked.size() / 3);
    for (size_t point = 0; point < refined->size(); ++point)
    {
        (*refined)[point] = GfVec3f(
            refinedPacked[point * 3],
            refinedPacked[(point * 3) + 1],
            refinedPacked[(point * 3) + 2]);
    }
    return true;
}

bool
HdSilkMeshSubdivision::RefineFaceVaryingPrimvarOntoCorners(
    int channel,
    const std::vector<float>& coarse,
    uint32_t componentCount,
    std::vector<float>* refined) const
{
    if (!IsRefined() || refined == nullptr || componentCount == 0 ||
        channel < 0 ||
        static_cast<size_t>(channel) >= _impl->faceVaryingCorners.size())
    {
        return false;
    }
    const size_t coarseValues = GetCoarseFaceVaryingValueCount(channel);
    if (coarse.size() != coarseValues * componentCount)
    {
        return false;
    }

    OsdFar::TopologyRefiner& refiner = *_impl->refiner;
    const size_t totalValues =
        static_cast<size_t>(refiner.GetNumFVarValuesTotal(channel));
    std::vector<float> buffer(totalValues * componentCount, 0.0F);
    std::memcpy(buffer.data(), coarse.data(), coarse.size() * sizeof(float));

    OsdFar::PrimvarRefiner primvarRefiner(refiner);
    size_t sourceOffset = 0;
    for (int level = 1; level <= _refineLevel; ++level)
    {
        const size_t sourceCount = static_cast<size_t>(
            refiner.GetLevel(level - 1).GetNumFVarValues(channel));
        PackedBuffer source(
            buffer.data() + (sourceOffset * componentCount),
            componentCount);
        PackedBuffer destination(
            buffer.data() + ((sourceOffset + sourceCount) * componentCount),
            componentCount);
        primvarRefiner.InterpolateFaceVarying(
            level,
            source,
            destination,
            channel);
        sourceOffset += sourceCount;
    }

    const std::vector<uint32_t>& corners =
        _impl->faceVaryingCorners[static_cast<size_t>(channel)];
    const size_t refinedValues = GetRefinedFaceVaryingValueCount(channel);
    refined->clear();
    refined->reserve(corners.size() * componentCount);
    const float* lastLevel = buffer.data() + (sourceOffset * componentCount);
    for (uint32_t value : corners)
    {
        if (value >= refinedValues)
        {
            return false;
        }
        const size_t offset = static_cast<size_t>(value) * componentCount;
        refined->insert(
            refined->end(),
            lastLevel + offset,
            lastLevel + offset + componentCount);
    }
    return true;
}

const char*
HdSilkPtexMappingStatusName(HdSilkPtexMappingStatus status)
{
    switch (status)
    {
    case HdSilkPtexMappingStatus::Mapped:
        return "mapped";
    case HdSilkPtexMappingStatus::RefinerUnavailable:
        return "refiner-unavailable";
    case HdSilkPtexMappingStatus::InvalidBuffers:
        return "invalid-buffers";
    case HdSilkPtexMappingStatus::FaceIndexOverflow:
        return "face-index-overflow";
    case HdSilkPtexMappingStatus::UnsupportedTopology:
        return "unsupported-topology";
    case HdSilkPtexMappingStatus::TriangleMismatch:
        return "triangle-mismatch";
    case HdSilkPtexMappingStatus::ExceedsBounds:
        return "exceeds-bounds";
    case HdSilkPtexMappingStatus::InvalidPoints:
        return "invalid-points";
    case HdSilkPtexMappingStatus::InvalidFaceOffsets:
        return "invalid-face-offsets";
    case HdSilkPtexMappingStatus::TextureFaceOutOfRange:
        return "texture-face-out-of-range";
    }
    return "unknown";
}

const char*
HdSilkSubdivisionStatusName(HdSilkSubdivisionStatus status)
{
    switch (status)
    {
    case HdSilkSubdivisionStatus::Refined:
        return "refined";
    case HdSilkSubdivisionStatus::NotRequested:
        return "not-requested";
    case HdSilkSubdivisionStatus::UnsupportedScheme:
        return "unsupported-scheme";
    case HdSilkSubdivisionStatus::InvalidTopology:
        return "invalid-topology";
    case HdSilkSubdivisionStatus::ExceedsBounds:
        return "exceeds-bounds";
    case HdSilkSubdivisionStatus::RefinerFailed:
        return "refiner-failed";
    case HdSilkSubdivisionStatus::UnsupportedFaceVarying:
        return "unsupported-face-varying";
    }
    return "unknown";
}

PXR_NAMESPACE_CLOSE_SCOPE

#if defined(OPENUSD_HDSILK_ENABLE_TEST_HOOKS)
#include "openusd_hdsilk.h"

extern "C" OPENUSD_HDSILK_API uint64_t
openusd_hdsilk_test_get_subdivision_refiner_build_count(void)
{
    return PXR_NS::HdSilkMeshSubdivision::GetRefinerBuildCount();
}

extern "C" OPENUSD_HDSILK_API uint64_t
openusd_hdsilk_test_get_subdivision_diagnostic_count(void)
{
    return PXR_NS::HdSilkMeshSubdivision::GetDiagnosticCount();
}

extern "C" OPENUSD_HDSILK_API void
openusd_hdsilk_test_set_subdivision_vertex_budget(uint64_t budget)
{
    PXR_NS::HdSilkMeshSubdivision::SetRefinedVertexBudgetForTesting(budget);
}
#endif
