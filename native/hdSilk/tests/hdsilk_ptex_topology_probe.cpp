// Copyright (c) marcschier. Licensed under the MIT License.

#include "subdivision.h"

#include "pxr/imaging/pxOsd/subdivTags.h"
#include "pxr/imaging/pxOsd/tokens.h"

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
#include <opensubdiv/far/patchTable.h>
#include <opensubdiv/far/patchTableFactory.h>
#include <opensubdiv/far/ptexIndices.h>
#include <opensubdiv/far/topologyDescriptor.h>
#include <opensubdiv/far/topologyRefiner.h>
#include <opensubdiv/sdc/types.h>
#if defined(_MSC_VER)
#pragma warning(pop)
#elif defined(__clang__)
#pragma clang diagnostic pop
#elif defined(__GNUC__)
#pragma GCC diagnostic pop
#endif

#include <algorithm>
#include <array>
#include <cstdint>
#include <iostream>
#include <memory>
#include <string>
#include <type_traits>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE
namespace Far = OpenSubdiv::Far;
namespace Sdc = OpenSubdiv::Sdc;

namespace
{
static_assert(std::is_same_v<decltype(HdSilkPtexTriangle::faceIndex), uint32_t>);

struct TestTopology
{
    const char* name;
    Sdc::SchemeType scheme;
    int vertices;
    std::vector<int> counts;
    std::vector<int> indices;
    std::vector<int> holes;
    bool leftHanded;
    bool boundaryNone = false;
    bool edgeOnly = false;
};

VtIntArray ToVtArray(const std::vector<int>& values)
{
    VtIntArray result;
    result.reserve(values.size());
    for (int value : values)
    {
        result.push_back(value);
    }
    return result;
}

HdMeshTopology MakeMeshTopology(const TestTopology& topology)
{
    const TfToken scheme = topology.scheme == Sdc::SCHEME_LOOP
        ? PxOsdOpenSubdivTokens->loop
        : topology.scheme == Sdc::SCHEME_BILINEAR
            ? PxOsdOpenSubdivTokens->bilinear
            : PxOsdOpenSubdivTokens->catmullClark;
    const TfToken orientation = topology.leftHanded
        ? PxOsdOpenSubdivTokens->leftHanded
        : PxOsdOpenSubdivTokens->rightHanded;
    HdMeshTopology result(
        scheme,
        orientation,
        ToVtArray(topology.counts),
        ToVtArray(topology.indices),
        ToVtArray(topology.holes));
    if (topology.boundaryNone || topology.edgeOnly)
    {
        PxOsdSubdivTags tags;
        tags.SetVertexInterpolationRule(
            topology.boundaryNone
                ? PxOsdOpenSubdivTokens->none
                : PxOsdOpenSubdivTokens->edgeOnly);
        result.SetSubdivTags(tags);
    }
    return result;
}

std::unique_ptr<Far::TopologyRefiner> MakeOracleRefiner(
    const TestTopology& topology,
    int level)
{
    Far::TopologyDescriptor descriptor;
    descriptor.numVertices = topology.vertices;
    descriptor.numFaces = static_cast<int>(topology.counts.size());
    descriptor.numVertsPerFace = topology.counts.data();
    descriptor.vertIndicesPerFace = topology.indices.data();
    descriptor.numHoles = static_cast<int>(topology.holes.size());
    descriptor.holeIndices = topology.holes.empty()
        ? nullptr
        : topology.holes.data();
    descriptor.isLeftHanded = topology.leftHanded;

    using Factory = Far::TopologyRefinerFactory<Far::TopologyDescriptor>;
    Factory::Options options(topology.scheme);
    options.schemeOptions.SetVtxBoundaryInterpolation(
        topology.boundaryNone
            ? Sdc::Options::VTX_BOUNDARY_NONE
            : topology.edgeOnly
                ? Sdc::Options::VTX_BOUNDARY_EDGE_ONLY
                : Sdc::Options::VTX_BOUNDARY_EDGE_AND_CORNER);
    options.validateFullTopology = true;
    std::unique_ptr<Far::TopologyRefiner> refiner(
        Factory::Create(descriptor, options));
    if (refiner)
    {
        Far::TopologyRefiner::UniformOptions refine(level);
        refine.fullTopologyInLastLevel = true;
        refiner->RefineUniform(refine);
    }
    return refiner;
}

std::unique_ptr<Far::PatchTable> MakeOraclePatchTable(
    const Far::TopologyRefiner& refiner)
{
    Far::PatchTableFactory::Options options;
    options.includeBaseLevelIndices = false;
    options.generateVaryingTables = false;
    options.generateFVarTables = false;
    return std::unique_ptr<Far::PatchTable>(
        Far::PatchTableFactory::Create(refiner, options));
}

bool Fail(const std::string& context, const std::string& message)
{
    std::cerr << context << ": " << message << '\n';
    return false;
}

bool SameVertices(Far::ConstIndexArray first, Far::ConstIndexArray second)
{
    if (first.size() != second.size())
    {
        return false;
    }
    for (int index = 0; index < first.size(); ++index)
    {
        if (first[index] != second[index])
        {
            return false;
        }
    }
    return true;
}

bool FindPatch(
    const Far::PatchTable& table,
    Far::ConstIndexArray vertices,
    Far::PatchParam* result)
{
    int matches = 0;
    for (int array = 0; array < table.GetNumPatchArrays(); ++array)
    {
        for (int patch = 0; patch < table.GetNumPatches(array); ++patch)
        {
            if (SameVertices(vertices, table.GetPatchVertices(array, patch)))
            {
                *result = table.GetPatchParam(array, patch);
                ++matches;
            }
        }
    }
    return matches == 1;
}

bool TestPtexCoordinatesMatchOpenSubdiv(
    const TestTopology& topology,
    int level)
{
    const std::string context = std::string(topology.name) +
        " level " + std::to_string(level);
    HdSilkMeshSubdivision subdivision;
    std::string diagnostic;
    if (subdivision.Rebuild(MakeMeshTopology(topology), level, {}, &diagnostic) !=
        HdSilkSubdivisionStatus::Refined)
    {
        return Fail(context, "subdivision refused: " + diagnostic);
    }

    std::vector<HdSilkPtexTriangle> triangles(
        subdivision.GetTriangleSubprims().size());
    std::vector<uint32_t> offsets(subdivision.GetPtexScratchCount());
    const HdSilkPtexMappingStatus status = subdivision.WriteDefaultPtexTriangles(
        triangles.data(), triangles.size(), offsets.data(), offsets.size());
    if (status != HdSilkPtexMappingStatus::Mapped)
    {
        return Fail(
            context,
            std::string("Ptex coordinate mapping was refused: ") +
                HdSilkPtexMappingStatusName(status));
    }

    const std::unique_ptr<Far::TopologyRefiner> oracle =
        MakeOracleRefiner(topology, level);
    if (!oracle)
    {
        return Fail(context, "oracle refiner could not be created");
    }
    const std::unique_ptr<Far::PatchTable> patches = MakeOraclePatchTable(*oracle);
    if (!patches)
    {
        return Fail(context, "oracle patch table could not be created");
    }
    const Far::PtexIndices ptexIndices(*oracle);
    if (offsets.size() != topology.counts.size() +
            static_cast<size_t>(oracle->GetLevel(1).GetNumFaces()))
    {
        return Fail(context, "incorrect scratch face count");
    }
    for (size_t face = 0; face < topology.counts.size(); ++face)
    {
        if (offsets[face] !=
            static_cast<uint32_t>(ptexIndices.GetFaceId(static_cast<int>(face))))
        {
            return Fail(context, "coarse face offset differs from OpenSubdiv");
        }
    }

    const Far::TopologyLevel& last = oracle->GetLevel(level);
    size_t triangleIndex = 0;
    int matchedPatches = 0;
    for (int face = 0; face < last.GetNumFaces(); ++face)
    {
        if (last.IsFaceHole(face))
        {
            continue;
        }
        const Far::ConstIndexArray vertices = last.GetFaceVertices(face);
        Far::PatchParam param;
        if (!FindPatch(*patches, vertices, &param))
        {
            return Fail(context, "last-level face does not have one matching patch");
        }
        ++matchedPatches;
        std::array<std::array<float, 2>, 4> expected{{
            {{0.0F, 0.0F}}, {{1.0F, 0.0F}},
            {{1.0F, 1.0F}}, {{0.0F, 1.0F}}}};
        if (topology.scheme == Sdc::SCHEME_LOOP)
        {
            expected[2] = {{0.0F, 1.0F}};
        }
        for (int corner = 0; corner < vertices.size(); ++corner)
        {
            float& u = expected[static_cast<size_t>(corner)][0];
            float& v = expected[static_cast<size_t>(corner)][1];
            if (topology.scheme == Sdc::SCHEME_LOOP)
            {
                param.UnnormalizeTriangle(u, v);
            }
            else
            {
                param.Unnormalize(u, v);
            }
        }

        for (int corner = 1; corner + 1 < vertices.size(); ++corner)
        {
            if (triangleIndex >= triangles.size())
            {
                return Fail(context, "triangle output was truncated");
            }
            const HdSilkPtexTriangle& triangle = triangles[triangleIndex];
            const std::array<float, 6> expectedCorners{{
                expected[0][0], expected[0][1],
                expected[static_cast<size_t>(corner)][0],
                expected[static_cast<size_t>(corner)][1],
                expected[static_cast<size_t>(corner + 1)][0],
                expected[static_cast<size_t>(corner + 1)][1]}};
            if (triangle.faceIndex != static_cast<uint32_t>(param.GetFaceId()) ||
                triangle.cornerCoordinates != expectedCorners)
            {
                return Fail(
                    context,
                    "Ptex face or corner coordinates differ at triangle " +
                        std::to_string(triangleIndex));
            }
            ++triangleIndex;
        }
    }
    if (triangleIndex != triangles.size() ||
        matchedPatches != patches->GetNumPatchesTotal())
    {
        return Fail(context, "triangle or patch counts do not reconcile");
    }
    return true;
}

bool TestAnalyticQuadAndRotatedLoop()
{
    const TestTopology quad{
        "analytic quad", Sdc::SCHEME_CATMARK, 4,
        {4}, {0, 1, 2, 3}, {}, false};
    HdSilkMeshSubdivision subdivision;
    std::string diagnostic;
    if (subdivision.Rebuild(MakeMeshTopology(quad), 1, {}, &diagnostic) !=
        HdSilkSubdivisionStatus::Refined)
    {
        return Fail(quad.name, diagnostic);
    }
    std::vector<HdSilkPtexTriangle> triangles(
        subdivision.GetTriangleSubprims().size());
    std::vector<uint32_t> offsets(subdivision.GetPtexScratchCount());
    if (subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size(), offsets.data(), offsets.size()) !=
        HdSilkPtexMappingStatus::Mapped)
    {
        return Fail(quad.name, "mapping failed");
    }
    const std::array<float, 6> first{{0.0F, 0.0F, 0.5F, 0.0F, 0.5F, 0.5F}};
    const std::array<float, 6> second{{0.0F, 0.0F, 0.5F, 0.5F, 0.0F, 0.5F}};
    if (triangles.size() != 8 || triangles[0].faceIndex != 0 ||
        triangles[0].cornerCoordinates != first ||
        triangles[1].cornerCoordinates != second)
    {
        return Fail(quad.name, "first refined quad has incorrect corner coordinates");
    }

    const TestTopology loop{
        "analytic loop", Sdc::SCHEME_LOOP, 3,
        {3}, {0, 1, 2}, {}, false};
    if (subdivision.Rebuild(MakeMeshTopology(loop), 2, {}, &diagnostic) !=
        HdSilkSubdivisionStatus::Refined)
    {
        return Fail(loop.name, diagnostic);
    }
    triangles.assign(subdivision.GetTriangleSubprims().size(), {});
    offsets.assign(subdivision.GetPtexScratchCount(), 0);
    if (subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size(), offsets.data(), offsets.size()) !=
        HdSilkPtexMappingStatus::Mapped)
    {
        return Fail(loop.name, "mapping failed");
    }
    const std::array<float, 6> rotated{{
        0.25F, 0.25F, 0.0F, 0.25F, 0.25F, 0.0F}};
    if (triangles.size() != 16 || triangles[3].faceIndex != 0 ||
        triangles[3].cornerCoordinates != rotated)
    {
        return Fail(loop.name, "rotated triangular patch is incorrectly oriented");
    }
    return true;
}

bool TestBufferRefusalAndRetry()
{
    const TestTopology quad{
        "buffer admission", Sdc::SCHEME_CATMARK, 4,
        {4}, {0, 1, 2, 3}, {}, false};
    HdSilkMeshSubdivision subdivision;
    std::string diagnostic;
    if (subdivision.Rebuild(MakeMeshTopology(quad), 2, {}, &diagnostic) !=
        HdSilkSubdivisionStatus::Refined)
    {
        return Fail(quad.name, diagnostic);
    }
    HdSilkPtexTriangle sentinel;
    sentinel.faceIndex = 99;
    sentinel.cornerCoordinates.fill(-1.0F);
    std::vector<HdSilkPtexTriangle> triangles(
        subdivision.GetTriangleSubprims().size(), sentinel);
    std::vector<uint32_t> offsets(subdivision.GetPtexScratchCount(), 99);
    if (subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size() - 1, offsets.data(), offsets.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size(), offsets.data(), offsets.size() - 1) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        subdivision.WriteDefaultPtexTriangles(
            nullptr, triangles.size(), offsets.data(), offsets.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size(), nullptr, offsets.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers ||
        subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size(),
            reinterpret_cast<uint32_t*>(triangles.data()), offsets.size()) !=
            HdSilkPtexMappingStatus::InvalidBuffers)
    {
        return Fail(quad.name, "invalid buffers were not refused");
    }
    if (std::any_of(
            triangles.begin(), triangles.end(),
            [&](const HdSilkPtexTriangle& value)
            {
                return value.faceIndex != sentinel.faceIndex ||
                    value.cornerCoordinates != sentinel.cornerCoordinates;
            }) ||
        std::any_of(offsets.begin(), offsets.end(), [](uint32_t value)
            {
                return value != 99;
            }))
    {
        return Fail(quad.name, "buffer refusal modified caller storage");
    }
    if (subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size(), offsets.data(), offsets.size()) !=
            HdSilkPtexMappingStatus::Mapped ||
        offsets[0] != 0 || triangles[0].faceIndex != 0 ||
        triangles[0].cornerCoordinates[0] != 0.0F)
    {
        return Fail(quad.name, "retry after buffer refusal did not succeed");
    }

    subdivision.Clear();
    if (subdivision.GetPtexScratchCount() != 0 ||
        subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size(), offsets.data(), offsets.size()) !=
            HdSilkPtexMappingStatus::RefinerUnavailable)
    {
        return Fail(quad.name, "cleared refiner accepted a mapping");
    }
    return true;
}

bool TestLargePolygon()
{
    constexpr int cornerCount = 1024;
    TestTopology polygon{
        "large polygon", Sdc::SCHEME_CATMARK, cornerCount,
        {cornerCount}, {}, {}, false};
    polygon.indices.reserve(cornerCount);
    for (int corner = 0; corner < cornerCount; ++corner)
    {
        polygon.indices.push_back(corner);
    }
    HdSilkMeshSubdivision subdivision;
    std::string diagnostic;
    if (subdivision.Rebuild(MakeMeshTopology(polygon), 2, {}, &diagnostic) !=
        HdSilkSubdivisionStatus::Refined)
    {
        return Fail(polygon.name, diagnostic);
    }
    std::vector<HdSilkPtexTriangle> triangles(
        subdivision.GetTriangleSubprims().size());
    std::vector<uint32_t> scratch(subdivision.GetPtexScratchCount());
    if (subdivision.WriteDefaultPtexTriangles(
            triangles.data(), triangles.size(), scratch.data(), scratch.size()) !=
            HdSilkPtexMappingStatus::Mapped ||
        triangles.size() != static_cast<size_t>(cornerCount) * 8)
    {
        return Fail(polygon.name, "mapping or triangle count is incorrect");
    }
    const std::array<float, 6> first{{0.0F, 0.0F, 0.5F, 0.0F, 0.5F, 0.5F}};
    for (int corner = 0; corner < cornerCount; ++corner)
    {
        const HdSilkPtexTriangle& triangle =
            triangles[static_cast<size_t>(corner) * 8];
        if (triangle.faceIndex != static_cast<uint32_t>(corner) ||
            triangle.cornerCoordinates != first)
        {
            return Fail(polygon.name, "a Ptex subface was misidentified");
        }
    }
    return true;
}

bool TestUnrenderableBoundaryRefusal()
{
    const TestTopology quad{
        "open boundary none", Sdc::SCHEME_CATMARK, 4,
        {4}, {0, 1, 2, 3}, {}, false, true};
    HdSilkMeshSubdivision subdivision;
    std::string diagnostic;
    if (subdivision.Rebuild(MakeMeshTopology(quad), 1, {}, &diagnostic) !=
            HdSilkSubdivisionStatus::InvalidTopology ||
        diagnostic.empty())
    {
        return Fail(quad.name, "unrenderable surface was not refused");
    }
    HdSilkPtexTriangle triangle;
    uint32_t offset = 0;
    if (subdivision.WriteDefaultPtexTriangles(
            &triangle, 1, &offset, 1) !=
        HdSilkPtexMappingStatus::RefinerUnavailable)
    {
        return Fail(quad.name, "unrenderable surface accepted a Ptex mapping");
    }
    return true;
}
}

int main()
{
    const std::vector<TestTopology> topologies{
        {"quad", Sdc::SCHEME_CATMARK, 4, {4}, {0, 1, 2, 3}, {}, false},
        {"left-handed quad", Sdc::SCHEME_CATMARK, 4,
            {4}, {0, 1, 2, 3}, {}, true},
        {"pentagon", Sdc::SCHEME_CATMARK, 5,
            {5}, {0, 1, 2, 3, 4}, {}, false},
        {"left-handed pentagon", Sdc::SCHEME_CATMARK, 5,
            {5}, {0, 1, 2, 3, 4}, {}, true},
        {"mixed polygons", Sdc::SCHEME_CATMARK, 12,
            {3, 4, 5}, {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11}, {}, false},
        {"left-handed mixed polygons", Sdc::SCHEME_CATMARK, 12,
            {3, 4, 5}, {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11}, {}, true},
        {"hole", Sdc::SCHEME_CATMARK, 6,
            {4, 4}, {0, 1, 2, 3, 1, 4, 5, 2}, {1}, false},
        {"left-handed first-face hole", Sdc::SCHEME_CATMARK, 6,
            {4, 4}, {0, 1, 2, 3, 1, 4, 5, 2}, {0}, true},
        {"edge-only quad", Sdc::SCHEME_CATMARK, 4,
            {4}, {0, 1, 2, 3}, {}, false, false, true},
        {"bilinear mixed polygons", Sdc::SCHEME_BILINEAR, 7,
            {3, 4}, {0, 1, 2, 3, 4, 5, 6}, {}, false},
        {"loop triangle", Sdc::SCHEME_LOOP, 3, {3}, {0, 1, 2}, {}, false},
        {"left-handed loop triangle", Sdc::SCHEME_LOOP, 3,
            {3}, {0, 1, 2}, {}, true},
        {"loop tetrahedron", Sdc::SCHEME_LOOP, 4,
            {3, 3, 3, 3}, {0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3}, {}, false},
        {"closed boundary none", Sdc::SCHEME_CATMARK, 8,
            {4, 4, 4, 4, 4, 4},
            {0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4,
                1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7},
            {}, false, true},
        {"mixed open and closed boundary none", Sdc::SCHEME_CATMARK, 12,
            {4, 4, 4, 4, 4, 4, 4},
            {0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4,
                1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7, 8, 9, 10, 11},
            {}, false, true}};
    if (!TestAnalyticQuadAndRotatedLoop() ||
        !TestBufferRefusalAndRetry() ||
        !TestLargePolygon() ||
        !TestUnrenderableBoundaryRefusal())
    {
        return 1;
    }
    for (const TestTopology& topology : topologies)
    {
        for (int level = 1; level <= HdSilkMaxMeshRefineLevel; ++level)
        {
            if (!TestPtexCoordinatesMatchOpenSubdiv(topology, level))
            {
                return 1;
            }
        }
    }
    std::cout << "Ptex topology probes passed for "
              << topologies.size() * HdSilkMaxMeshRefineLevel
              << " refined meshes\n";
    return 0;
}
