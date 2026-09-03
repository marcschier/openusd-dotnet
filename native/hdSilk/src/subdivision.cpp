// Copyright (c) marcschier. Licensed under the MIT License.

#include "subdivision.h"

#include "pxr/base/tf/diagnostic.h"
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
#include <cstring>
#include <limits>
#include <numeric>
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
