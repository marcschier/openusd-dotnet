// Copyright (c) marcschier. Licensed under the MIT License.
//
// Analytic evidence for hdSilk's OpenSubdiv refinement.
//
// This probe is deliberately separate from hdsilk_probe: every claim here is
// arithmetic about a control cage whose refined shape is known in closed form,
// so it needs no rasterizer, no reference renderer, and no image comparison.
// It asserts what a coverage comparison against Storm cannot: exact refined
// component counts, exact refined positions, that a hole propagates, that a
// face-varying channel is refined rather than triangulated, that an animated
// point array reuses the cached refiner, and that the refined mesh budget is
// enforced by publishing the whole control cage rather than a partial surface.

#include "openusd_hdsilk.h"

#include "hdsilk_test_hooks.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <map>
#include <string>
#include <vector>

namespace
{
constexpr char CubePath[] = "/World/SubdivCube";
constexpr char CreasedCubePath[] = "/World/SubdivCreasedCube";
constexpr char CorneredCubePath[] = "/World/SubdivCorneredCube";
constexpr char HoleCubePath[] = "/World/SubdivHoleCube";
constexpr char UniformCubePath[] = "/World/SubdivUniformCube";
constexpr char UvQuadPath[] = "/World/SubdivUvQuad";
constexpr char FaceVaryingNormalQuadPath[] =
    "/World/SubdivFaceVaryingNormalQuad";
constexpr char UnsupportedFaceVaryingQuadPath[] =
    "/World/SubdivUnsupportedFaceVaryingQuad";
constexpr char BilinearCubePath[] = "/World/SubdivBilinearCube";
constexpr char LoopTetrahedronPath[] = "/World/SubdivLoopTetrahedron";
constexpr char NoneQuadPath[] = "/World/SubdivNoneQuad";
constexpr char AnimatedCubePath[] = "/World/SubdivAnimatedCube";

// The MESH_UPSERT layout this probe parses is pinned by reconciliation rather
// than by a version assertion: every record is walked field by field and the
// walk must land exactly on the command's byte_size. A layout change therefore
// fails here with a named error instead of being silently misread, and the
// probe does not have to be edited every time an unrelated command is added to
// the page ABI.
static_assert(OPENUSD_SILK_COMMAND_MESH_UPSERT == 2u);

struct ParsedAttribute
{
    std::string name;
    uint32_t semantic = 0;
    uint32_t componentCount = 0;
    uint32_t interpolation = 0;
    uint32_t elementCount = 0;
    std::vector<float> values;
};

struct ParsedMesh
{
    bool found = false;
    uint32_t topologyKind = 0;
    uint64_t topologyRevision = 0;
    uint32_t pointCount = 0;
    uint32_t indexCount = 0;
    uint32_t triangleCount = 0;
    std::vector<float> points;
    std::vector<uint32_t> indices;
    std::vector<uint32_t> subprims;
    std::vector<ParsedAttribute> attributes;
};

using ParsedPage = std::map<std::string, ParsedMesh>;

std::string g_failure;

bool Fail(const std::string& message)
{
    g_failure = message;
    return false;
}

template <typename T>
bool ReadValue(const uint8_t* data, size_t size, size_t offset, T* value)
{
    if (offset > size || sizeof(T) > size - offset)
    {
        return false;
    }
    std::memcpy(value, data + offset, sizeof(T));
    return true;
}

bool ParseMeshUpsert(
    const uint8_t* data,
    size_t size,
    size_t offset,
    uint32_t byteSize,
    ParsedPage* page)
{
    uint32_t topologyKind = 0;
    uint64_t topologyRevision = 0;
    uint32_t pathSize = 0;
    uint32_t pointCount = 0;
    uint32_t indexCount = 0;
    uint32_t triangleCount = 0;
    uint32_t materialPathSize = 0;
    uint32_t attributeCount = 0;
    uint32_t deformationByteCount = 0;
    uint32_t pointOriginCount = 0;
    uint32_t cornerEdgeCount = 0;
    constexpr size_t pathOffset = 268;
    if (!ReadValue(data, size, offset + 28, &topologyKind) ||
        !ReadValue(data, size, offset + 32, &topologyRevision) ||
        !ReadValue(data, size, offset + 48, &pathSize) ||
        !ReadValue(data, size, offset + 52, &pointCount) ||
        !ReadValue(data, size, offset + 56, &indexCount) ||
        !ReadValue(data, size, offset + 60, &triangleCount) ||
        !ReadValue(data, size, offset + 216, &materialPathSize) ||
        !ReadValue(data, size, offset + 220, &attributeCount) ||
        !ReadValue(data, size, offset + 232, &deformationByteCount) ||
        !ReadValue(data, size, offset + 244, &pointOriginCount) ||
        !ReadValue(data, size, offset + 248, &cornerEdgeCount) ||
        pathOffset + pathSize > byteSize)
    {
        return Fail("malformed MESH_UPSERT header");
    }

    ParsedMesh mesh;
    mesh.found = true;
    mesh.topologyKind = topologyKind;
    mesh.topologyRevision = topologyRevision;
    mesh.pointCount = pointCount;
    mesh.indexCount = indexCount;
    mesh.triangleCount = triangleCount;

    const std::string path(
        reinterpret_cast<const char*>(data + offset + pathOffset),
        pathSize);

    size_t cursor = offset + pathOffset + pathSize;
    mesh.points.resize(static_cast<size_t>(pointCount) * 3);
    for (size_t component = 0; component < mesh.points.size(); ++component)
    {
        if (!ReadValue(
                data,
                size,
                cursor + (component * sizeof(float)),
                &mesh.points[component]))
        {
            return Fail("truncated MESH_UPSERT points");
        }
    }
    cursor += mesh.points.size() * sizeof(float);

    mesh.indices.resize(indexCount);
    for (size_t index = 0; index < mesh.indices.size(); ++index)
    {
        if (!ReadValue(
                data,
                size,
                cursor + (index * sizeof(uint32_t)),
                &mesh.indices[index]))
        {
            return Fail("truncated MESH_UPSERT indices");
        }
    }
    cursor += mesh.indices.size() * sizeof(uint32_t);

    mesh.subprims.resize(triangleCount);
    for (size_t subprim = 0; subprim < mesh.subprims.size(); ++subprim)
    {
        if (!ReadValue(
                data,
                size,
                cursor + (subprim * sizeof(uint32_t)),
                &mesh.subprims[subprim]))
        {
            return Fail("truncated MESH_UPSERT subprims");
        }
    }
    cursor += mesh.subprims.size() * sizeof(uint32_t);
    cursor += materialPathSize;

    for (uint32_t attribute = 0; attribute < attributeCount; ++attribute)
    {
        ParsedAttribute parsed;
        uint32_t nameSize = 0;
        if (!ReadValue(data, size, cursor, &parsed.semantic) ||
            !ReadValue(data, size, cursor + 4, &parsed.componentCount) ||
            !ReadValue(data, size, cursor + 8, &parsed.interpolation) ||
            !ReadValue(data, size, cursor + 12, &nameSize) ||
            !ReadValue(data, size, cursor + 16, &parsed.elementCount) ||
            cursor + 20 + nameSize > size)
        {
            return Fail("malformed MESH_UPSERT attribute header");
        }
        parsed.name.assign(
            reinterpret_cast<const char*>(data + cursor + 20),
            nameSize);
        cursor += 20 + nameSize;
        const size_t valueCount =
            static_cast<size_t>(parsed.elementCount) * parsed.componentCount;
        parsed.values.resize(valueCount);
        for (size_t value = 0; value < valueCount; ++value)
        {
            if (!ReadValue(
                    data,
                    size,
                    cursor + (value * sizeof(float)),
                    &parsed.values[value]))
            {
                return Fail("truncated MESH_UPSERT attribute data");
            }
        }
        cursor += valueCount * sizeof(float);
        mesh.attributes.push_back(std::move(parsed));
    }

    // A refined surface emits points the control-cage influences do not
    // address, so this probe's meshes publish no rig; the block is still
    // skipped explicitly so the exact-size check stays exact.
    cursor += deformationByteCount;

    // A refined surface refuses exact edge and point identity, so it publishes
    // neither ABI v22 table. An unrefined mesh in this probe still publishes
    // both, so both are skipped explicitly to keep the exact-size check exact.
    cursor += static_cast<size_t>(pointOriginCount) * sizeof(uint32_t);
    cursor += static_cast<size_t>(cornerEdgeCount) * sizeof(uint32_t);

    if (cursor != offset + byteSize)
    {
        return Fail("MESH_UPSERT byte size did not match its payload");
    }
    (*page)[path] = std::move(mesh);
    return true;
}

bool ParseCommands(const uint8_t* data, size_t size, ParsedPage* page)
{
    size_t offset = 0;
    while (offset + 8 <= size)
    {
        uint32_t type = 0;
        uint32_t byteSize = 0;
        if (!ReadValue(data, size, offset, &type) ||
            !ReadValue(data, size, offset + 4, &byteSize) ||
            byteSize < 8 ||
            static_cast<size_t>(byteSize) > size - offset)
        {
            return Fail("malformed command header");
        }
        if (type == OPENUSD_SILK_COMMAND_MESH_UPSERT &&
            !ParseMeshUpsert(data, size, offset, byteSize, page))
        {
            return false;
        }
        offset += byteSize;
    }
    return true;
}

openusd_render_camera AutomaticCamera()
{
    openusd_render_camera camera{};
    camera.struct_size = sizeof(camera);
    camera.mode = OPENUSD_RENDER_CAMERA_MODE_AUTO;
    return camera;
}

/// Syncs one frame and merges its MESH_UPSERT records into "page".
///
/// A page is a delta, so records are merged rather than replaced: a scrub that
/// republishes only the animated prim must still be compared against the
/// records the previous sync published for every other prim.
bool Sync(
    openusd_silk_session* session,
    uint32_t complexity,
    double timeCode,
    ParsedPage* page,
    openusd_error_buffer* error)
{
    const openusd_render_camera camera = AutomaticCamera();
    openusd_silk_page* rawPage = nullptr;
    openusd_silk_page_view view{};
    view.struct_size = sizeof(openusd_silk_page_view);
    const openusd_status status = openusd_silk_session_sync_with_complexity(
        session,
        64,
        64,
        timeCode,
        &camera,
        complexity,
        &rawPage,
        &view,
        error);
    if (status != OPENUSD_STATUS_OK)
    {
        return Fail(
            "sync failed at complexity " + std::to_string(complexity) + ": " +
            std::string(error->data == nullptr ? "" : error->data));
    }
    if (view.abi_version != OPENUSD_SILK_PAGE_ABI_VERSION)
    {
        openusd_silk_page_release(rawPage);
        return Fail("the page reported an ABI version the probe was not built against");
    }
    const bool parsed = ParseCommands(view.data, view.data_size, page);
    openusd_silk_page_release(rawPage);
    return parsed;
}

bool Close(float left, float right, float tolerance = 1.0e-4F)
{
    return std::fabs(left - right) <= tolerance;
}

/// Reports whether the emitted points contain a vertex at "expected". The
/// refined vertex ordering is OpenSubdiv's, not this delegate's, so a claim
/// about a refined position has to be a claim about the set of positions.
bool ContainsPoint(
    const ParsedMesh& mesh,
    float x,
    float y,
    float z,
    float tolerance = 1.0e-4F)
{
    for (size_t point = 0; point + 2 < mesh.points.size(); point += 3)
    {
        if (Close(mesh.points[point], x, tolerance) &&
            Close(mesh.points[point + 1], y, tolerance) &&
            Close(mesh.points[point + 2], z, tolerance))
        {
            return true;
        }
    }
    return false;
}

const ParsedAttribute* FindAttribute(
    const ParsedMesh& mesh,
    const std::string& name)
{
    for (const ParsedAttribute& attribute : mesh.attributes)
    {
        if (attribute.name == name)
        {
            return &attribute;
        }
    }
    return nullptr;
}

const ParsedMesh* FindMesh(const ParsedPage& page, const char* path)
{
    const auto entry = page.find(path);
    return entry == page.end() ? nullptr : &entry->second;
}

/// Reads the process-wide refusal counter into "out" and always succeeds, so it
/// can be sequenced inside the short-circuiting chain of checks that has to
/// bracket one specific sync.
bool CaptureDiagnosticCount(uint64_t* out)
{
    *out = openusd_hdsilk_test_get_subdivision_diagnostic_count();
    return true;
}

bool RequireCounts(
    const ParsedPage& page,
    const char* path,
    uint32_t triangleCount,
    uint32_t pointCount)
{
    const ParsedMesh* mesh = FindMesh(page, path);
    if (mesh == nullptr)
    {
        return Fail(std::string(path) + " was not published");
    }
    if (mesh->topologyKind != OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST)
    {
        return Fail(std::string(path) + " was not a triangle list");
    }
    if (mesh->triangleCount != triangleCount)
    {
        return Fail(
            std::string(path) + " published " +
            std::to_string(mesh->triangleCount) + " triangles, expected " +
            std::to_string(triangleCount));
    }
    if (mesh->pointCount != pointCount)
    {
        return Fail(
            std::string(path) + " published " +
            std::to_string(mesh->pointCount) + " points, expected " +
            std::to_string(pointCount));
    }
    if (mesh->indexCount != triangleCount * 3 ||
        mesh->subprims.size() != triangleCount)
    {
        return Fail(std::string(path) + " has inconsistent index tables");
    }
    for (uint32_t index : mesh->indices)
    {
        if (index >= mesh->pointCount)
        {
            return Fail(std::string(path) + " indexes a missing point");
        }
    }
    return true;
}

/// Asserts the emitted primitive count without pinning the point count, for a
/// record whose coarse expansion depends on how USD flattens a primvar this
/// delegate does not control.
bool RequireTriangleCount(
    const ParsedPage& page,
    const char* path,
    uint32_t triangleCount)
{
    const ParsedMesh* mesh = FindMesh(page, path);
    if (mesh == nullptr)
    {
        return Fail(std::string(path) + " was not published");
    }
    if (mesh->triangleCount != triangleCount)
    {
        return Fail(
            std::string(path) + " published " +
            std::to_string(mesh->triangleCount) + " triangles, expected " +
            std::to_string(triangleCount));
    }
    return true;
}

/// Complexity Low must publish exactly the historical coarse triangulation:
/// the control cage triangulated by HdMeshUtil, with no refinement anywhere.
bool VerifyLowIsUnrefined(const ParsedPage& page)
{
    // 6 quads -> 12 triangles over 8 control points, for every cube-shaped cage.
    return RequireCounts(page, CubePath, 12, 8) &&
        RequireCounts(page, CreasedCubePath, 12, 8) &&
        RequireCounts(page, CorneredCubePath, 12, 8) &&
        RequireCounts(page, BilinearCubePath, 12, 8) &&
        RequireCounts(page, AnimatedCubePath, 12, 8) &&
        // The hole quad is dropped by the coarse triangulation too.
        RequireCounts(page, HoleCubePath, 10, 8) &&
        // A uniform primvar resolves onto corners, so the record is expanded.
        RequireCounts(page, UniformCubePath, 12, 36) &&
        RequireCounts(page, LoopTetrahedronPath, 4, 4) &&
        RequireCounts(page, NoneQuadPath, 2, 4) &&
        // The face-varying UV expands the quad's two triangles onto corners.
        RequireCounts(page, UvQuadPath, 2, 6) &&
        // So do the authored face-varying normals.
        RequireCounts(page, FaceVaryingNormalQuadPath, 2, 6) &&
        // The malformed face-varying index array is flattened by USD before it
        // reaches this delegate, so the coarse expansion it produces is USD's
        // business rather than a claim this probe makes; only the two emitted
        // triangles of the control cage are.
        RequireTriangleCount(page, UnsupportedFaceVaryingQuadPath, 2);
}

/// Level 1 Catmull-Clark on a cube: 8 + 12 + 6 = 26 vertices and 6 * 4 = 24
/// quads, with the closed-form face, edge and vertex points.
bool VerifyRefinedCube(const ParsedPage& page)
{
    if (!RequireCounts(page, CubePath, 48, 26))
    {
        return false;
    }
    const ParsedMesh& cube = *FindMesh(page, CubePath);
    const float vertexPoint = 5.0F / 9.0F;
    if (!ContainsPoint(cube, 0.0F, 0.0F, 1.0F) ||
        !ContainsPoint(cube, 0.0F, 0.75F, 0.75F) ||
        !ContainsPoint(cube, vertexPoint, vertexPoint, vertexPoint) ||
        !ContainsPoint(cube, -vertexPoint, -vertexPoint, -vertexPoint))
    {
        return Fail(
            "the refined cube is missing a closed-form Catmull-Clark point");
    }
    if (ContainsPoint(cube, 1.0F, 1.0F, 1.0F))
    {
        return Fail(
            "the refined cube kept a control point that Catmull-Clark must move");
    }

    // Subdivision weights are a partition of unity, so a constant vertex or
    // varying primvar must refine to the same constant at every refined vertex.
    for (const char* name : {"probeUnit", "probeVarying"})
    {
        const ParsedAttribute* attribute = FindAttribute(cube, name);
        if (attribute == nullptr ||
            attribute->interpolation != OPENUSD_SILK_INTERPOLATION_VERTEX ||
            attribute->elementCount != cube.pointCount)
        {
            return Fail(
                std::string("the refined cube did not publish '") + name +
                "' onto its refined vertices");
        }
        const float expected = std::strcmp(name, "probeUnit") == 0 ? 1.0F : 2.0F;
        for (float value : attribute->values)
        {
            if (!Close(value, expected))
            {
                return Fail(
                    std::string("refining '") + name +
                    "' did not preserve a constant, so the weights do not sum to one");
            }
        }
    }
    return true;
}

/// Refinement must reach exactly the level the session asked for, which is what
/// makes the complexity-to-level mapping observable rather than assumed.
bool VerifyRefinementLevels(const ParsedPage& high, const ParsedPage& veryHigh)
{
    // Level 2: 26 + 48 edges + 24 faces = 98 vertices, 96 quads.
    if (!RequireCounts(high, CubePath, 192, 98))
    {
        return false;
    }
    // Level 3: 98 + 192 edges + 96 faces = 386 vertices, 384 quads.
    return RequireCounts(veryHigh, CubePath, 768, 386);
}

/// An edge creased past the refinement level pins the surface to the control
/// cage, and a corner tag pins one vertex while its untagged neighbours move.
bool VerifyCreasesAndCorners(const ParsedPage& page)
{
    if (!RequireCounts(page, CreasedCubePath, 48, 26))
    {
        return false;
    }
    const ParsedMesh& creased = *FindMesh(page, CreasedCubePath);
    if (!ContainsPoint(creased, 1.0F, 1.0F, 1.0F) ||
        !ContainsPoint(creased, 1.0F, 1.0F, 0.0F) ||
        !ContainsPoint(creased, 0.0F, 0.0F, 1.0F))
    {
        return Fail(
            "a fully creased cube did not refine back onto its control cage");
    }

    if (!RequireCounts(page, CorneredCubePath, 48, 26))
    {
        return false;
    }
    const ParsedMesh& cornered = *FindMesh(page, CorneredCubePath);
    const float vertexPoint = 5.0F / 9.0F;
    if (!ContainsPoint(cornered, 1.0F, 1.0F, 1.0F))
    {
        return Fail("a sharp corner did not survive refinement");
    }
    if (!ContainsPoint(cornered, -vertexPoint, -vertexPoint, -vertexPoint))
    {
        return Fail("an untagged vertex did not reach its smooth limit point");
    }
    return true;
}

/// A hole tag propagates to every child face, so the refined mesh must drop the
/// authored face entirely rather than refine and then partially emit it.
bool VerifyHoles(const ParsedPage& page)
{
    // Five refined quads per surviving coarse quad face: 5 * 4 * 2 triangles.
    if (!RequireCounts(page, HoleCubePath, 40, 26))
    {
        return false;
    }
    const ParsedMesh& holed = *FindMesh(page, HoleCubePath);
    for (uint32_t subprim : holed.subprims)
    {
        if (subprim == 0)
        {
            return Fail("a refined triangle descended from a hole face");
        }
        if (subprim > 5)
        {
            return Fail("a refined triangle named an authored face that does not exist");
        }
    }
    return true;
}

/// Every emitted triangle must still name the authored face it descends from,
/// and a uniform primvar keyed by that face must resolve onto its corners.
bool VerifySubsetMapping(const ParsedPage& page)
{
    // 6 coarse quads -> 24 refined quads -> 48 triangles, expanded onto corners
    // because a uniform primvar cannot be indexed per refined vertex.
    if (!RequireCounts(page, UniformCubePath, 48, 144))
    {
        return false;
    }
    const ParsedMesh& mesh = *FindMesh(page, UniformCubePath);
    std::array<uint32_t, 6> perFace{};
    for (uint32_t subprim : mesh.subprims)
    {
        if (subprim > 5)
        {
            return Fail("a refined triangle named an authored face that does not exist");
        }
        ++perFace[subprim];
    }
    for (uint32_t count : perFace)
    {
        if (count != 8)
        {
            return Fail(
                "an authored quad did not contribute exactly eight refined triangles");
        }
    }

    const ParsedAttribute* face = FindAttribute(mesh, "probeFace");
    if (face == nullptr || face->componentCount != 1 ||
        face->elementCount != mesh.pointCount)
    {
        return Fail("the refined mesh did not publish its uniform primvar");
    }
    for (uint32_t triangle = 0; triangle < mesh.triangleCount; ++triangle)
    {
        for (uint32_t corner = 0; corner < 3; ++corner)
        {
            const float value = face->values[(triangle * 3) + corner];
            if (!Close(value, static_cast<float>(mesh.subprims[triangle])))
            {
                return Fail(
                    "a uniform primvar value did not follow its authored face "
                    "through refinement");
            }
        }
    }
    return true;
}

/// A flat quad with sharp boundaries refines to an exact 3x3 grid, so its UVs
/// and its vertex primvar are the identity map of its own refined positions.
/// Triangulating the control cage's face-varying data instead of refining it
/// through an OpenSubdiv channel fails this by construction.
bool VerifyFaceVaryingAndVertexPrimvars(const ParsedPage& page)
{
    // 4 refined quads -> 8 triangles, expanded onto 24 corners by the UV.
    if (!RequireCounts(page, UvQuadPath, 8, 24))
    {
        return false;
    }
    const ParsedMesh& quad = *FindMesh(page, UvQuadPath);
    const ParsedAttribute* st = FindAttribute(quad, "st");
    if (st == nullptr || st->componentCount != 2 ||
        st->semantic != OPENUSD_SILK_ATTRIBUTE_TEXCOORD ||
        st->elementCount != quad.pointCount)
    {
        return Fail("the refined quad did not publish refined UVs");
    }
    const ParsedAttribute* probeX = FindAttribute(quad, "probeX");
    if (probeX == nullptr || probeX->componentCount != 1 ||
        probeX->elementCount != quad.pointCount)
    {
        return Fail("the refined quad did not publish its refined vertex primvar");
    }
    bool sawCentre = false;
    for (uint32_t corner = 0; corner < quad.pointCount; ++corner)
    {
        const float x = quad.points[(corner * 3)];
        const float y = quad.points[(corner * 3) + 1];
        if (!Close(st->values[corner * 2], (x + 1.0F) * 0.5F) ||
            !Close(st->values[(corner * 2) + 1], (y + 1.0F) * 0.5F))
        {
            return Fail(
                "a refined UV did not match the identity map of its refined position");
        }
        if (!Close(probeX->values[corner], x))
        {
            return Fail(
                "a refined vertex primvar did not follow the refined positions");
        }
        sawCentre = sawCentre || (Close(x, 0.0F) && Close(y, 0.0F));
    }
    if (!sawCentre)
    {
        return Fail("the refined quad never emitted its centre vertex");
    }
    return true;
}

/// Refined face-varying normals must be renormalized, exactly as refined vertex
/// normals are. Every refinement rule is a weighted average, so a corner set
/// that disagrees produces sub-unit directions: this quad's refined face point
/// averages to (0, 0, 0.5) and its edge points to (+-0.5, 0, 0.5). The refined
/// positions are the exact 3x3 grid, so each refined normal is checked against
/// the position carrying it rather than against the set as a whole.
bool VerifyFaceVaryingNormalsAreRenormalized(const ParsedPage& page)
{
    // 4 refined quads -> 8 triangles, expanded onto 24 corners by the normals.
    if (!RequireCounts(page, FaceVaryingNormalQuadPath, 8, 24))
    {
        return false;
    }
    const ParsedMesh& quad = *FindMesh(page, FaceVaryingNormalQuadPath);
    const ParsedAttribute* normals = FindAttribute(quad, "normals");
    if (normals == nullptr || normals->componentCount != 3 ||
        normals->semantic != OPENUSD_SILK_ATTRIBUTE_NORMAL ||
        normals->elementCount != quad.pointCount)
    {
        return Fail("the refined quad did not publish refined normals");
    }

    const float diagonal = 0.70710678F;
    bool sawCentre = false;
    bool sawEdge = false;
    for (uint32_t corner = 0; corner < quad.pointCount; ++corner)
    {
        const float x = quad.points[corner * 3];
        const float y = quad.points[(corner * 3) + 1];
        const float nx = normals->values[corner * 3];
        const float ny = normals->values[(corner * 3) + 1];
        const float nz = normals->values[(corner * 3) + 2];
        const float length = std::sqrt((nx * nx) + (ny * ny) + (nz * nz));
        if (!Close(length, 1.0F))
        {
            return Fail(
                "a refined face-varying normal was not renormalized, so an "
                "averaged direction reached the wire shorter than unit");
        }
        if (!Close(ny, 0.0F))
        {
            return Fail("a refined face-varying normal left the authored plane");
        }
        if (Close(x, 0.0F) && Close(y, 0.0F))
        {
            // (0, 0, 1) + (1, 0, 0) + (0, 0, 1) + (-1, 0, 0) averages to
            // (0, 0, 0.5), which is (0, 0, 1) once renormalized.
            if (!Close(nx, 0.0F) || !Close(nz, 1.0F))
            {
                return Fail("the refined centre normal is not the unit average");
            }
            sawCentre = true;
        }
        else if (Close(x, 0.0F) && Close(y, -1.0F))
        {
            // The bottom edge averages (0, 0, 1) and (1, 0, 0) to
            // (0.5, 0, 0.5), which renormalizes to the exact diagonal.
            if (!Close(nx, diagonal) || !Close(nz, diagonal))
            {
                return Fail("the refined edge normal is not the unit average");
            }
            sawEdge = true;
        }
        else if (Close(x, -1.0F) && Close(y, -1.0F) &&
                 (!Close(nx, 0.0F) || !Close(nz, 1.0F)))
        {
            return Fail("an authored corner normal did not survive refinement");
        }
    }
    if (!sawCentre || !sawEdge)
    {
        return Fail(
            "the refined quad never emitted the centre and edge normals the "
            "renormalization claim rests on");
    }
    return true;
}

/// A face-varying primvar whose topology cannot be described as an OpenSubdiv
/// channel must refuse refinement for the whole mesh. Binding only the channels
/// that could be described would publish a refined surface whose authored UVs
/// silently disappeared, which reads downstream as a material fault rather than
/// the geometry refusal it is.
bool VerifyUnsupportedFaceVaryingRefusesRefinement(
    const ParsedPage& low,
    const ParsedPage& refined,
    uint64_t diagnosticsBefore,
    uint64_t diagnosticsAfter)
{
    if (diagnosticsAfter != diagnosticsBefore + 1)
    {
        return Fail(
            "the refining sync recorded " +
            std::to_string(diagnosticsAfter - diagnosticsBefore) +
            " refusals; exactly one mesh authors an undescribable "
            "face-varying channel, and a mesh carrying two of them must still "
            "refuse once with one bounded diagnostic rather than once per "
            "channel");
    }
    // The control cage, whole: refining the quad would have emitted eight
    // triangles, so two is the cage and nothing else.
    if (!RequireTriangleCount(refined, UnsupportedFaceVaryingQuadPath, 2))
    {
        return false;
    }
    const ParsedMesh* cage = FindMesh(low, UnsupportedFaceVaryingQuadPath);
    const ParsedMesh& published =
        *FindMesh(refined, UnsupportedFaceVaryingQuadPath);
    if (cage == nullptr)
    {
        return Fail("the undescribable quad was not published unrefined");
    }
    if (published.pointCount != cage->pointCount ||
        published.points != cage->points ||
        published.indices != cage->indices ||
        published.subprims != cage->subprims)
    {
        return Fail(
            "the refusal published something other than the whole control cage");
    }
    return true;
}

bool VerifyBilinearAndLoop(const ParsedPage& page)
{
    // Bilinear splits with the same recurrence as Catmark but no smoothing, so
    // every authored control point survives.
    if (!RequireCounts(page, BilinearCubePath, 48, 26))
    {
        return false;
    }
    const ParsedMesh& bilinear = *FindMesh(page, BilinearCubePath);
    if (!ContainsPoint(bilinear, 1.0F, 1.0F, 1.0F) ||
        !ContainsPoint(bilinear, 1.0F, 1.0F, 0.0F) ||
        !ContainsPoint(bilinear, 0.0F, 0.0F, 1.0F))
    {
        return Fail("bilinear refinement did not split the control cage linearly");
    }

    // Loop on a tetrahedron: 4 + 6 = 10 vertices, 4 * 4 = 16 triangles.
    return RequireCounts(page, LoopTetrahedronPath, 16, 10);
}

bool VerifyUnrefinableSchemeIsUnchanged(
    const ParsedPage& low,
    const ParsedPage& refined)
{
    const ParsedMesh* lowQuad = FindMesh(low, NoneQuadPath);
    const ParsedMesh* refinedQuad = FindMesh(refined, NoneQuadPath);
    if (lowQuad == nullptr || refinedQuad == nullptr)
    {
        return Fail("the unrefinable quad was not published");
    }
    if (refinedQuad->triangleCount != lowQuad->triangleCount ||
        refinedQuad->pointCount != lowQuad->pointCount ||
        refinedQuad->points != lowQuad->points ||
        refinedQuad->indices != lowQuad->indices)
    {
        return Fail("subdivisionScheme 'none' was refined by a complexity change");
    }
    return true;
}

/// Refined positions must follow animated control points, and doing so must not
/// rebuild the refiner: topology did not change, so neither did the refinement.
bool VerifyAnimatedPointsReuseTheRefiner(
    openusd_silk_session* session,
    const ParsedPage& baseline,
    openusd_error_buffer* error)
{
    const uint64_t before =
        openusd_hdsilk_test_get_subdivision_refiner_build_count();
    ParsedPage unchanged = baseline;
    if (!Sync(session, OPENUSD_SILK_COMPLEXITY_MEDIUM, 1.0, &unchanged, error))
    {
        return false;
    }
    const uint64_t afterUnchanged =
        openusd_hdsilk_test_get_subdivision_refiner_build_count();
    if (afterUnchanged != before)
    {
        return Fail(
            "an unchanged frame rebuilt the refiner instead of reusing the cache");
    }

    ParsedPage scrubbed = unchanged;
    if (!Sync(session, OPENUSD_SILK_COMPLEXITY_MEDIUM, 2.0, &scrubbed, error))
    {
        return false;
    }
    if (openusd_hdsilk_test_get_subdivision_refiner_build_count() !=
        afterUnchanged)
    {
        return Fail("animating points rebuilt the refiner");
    }

    if (!RequireCounts(scrubbed, AnimatedCubePath, 48, 26))
    {
        return false;
    }
    const ParsedMesh& animated = *FindMesh(scrubbed, AnimatedCubePath);
    const ParsedMesh* rest = FindMesh(baseline, AnimatedCubePath);
    if (rest == nullptr)
    {
        return Fail("the animated cube was never published at the rest pose");
    }
    if (rest->points == animated.points)
    {
        return Fail("animated control points did not move the refined surface");
    }
    if (animated.topologyRevision != rest->topologyRevision)
    {
        return Fail(
            "animating points changed the topology revision, so the consumer "
            "would discard an unchanged index buffer");
    }
    // The cage is stretched to +-2 in x, so the refined vertex point scales the
    // same way: (10/9, 5/9, 5/9) rather than (5/9, 5/9, 5/9).
    if (!ContainsPoint(animated, 10.0F / 9.0F, 5.0F / 9.0F, 5.0F / 9.0F))
    {
        return Fail("the refined surface did not follow the animated cage");
    }
    return true;
}

/// The refined mesh budget must be enforced by publishing the whole control
/// cage with a diagnostic, never a partially refined surface.
bool VerifyBudgetPublishesTheControlCage(
    const char* pluginPath,
    const char* stagePath,
    openusd_error_buffer* error)
{
    openusd_silk_session* session = nullptr;
    if (openusd_silk_session_create(pluginPath, stagePath, &session, error) !=
        OPENUSD_STATUS_OK)
    {
        return Fail("bounded session create failed");
    }

    // Twenty refined vertices is below the cube's 26 at level 1 and above
    // nothing the stage authors, so the refusal is the budget's doing.
    openusd_hdsilk_test_set_subdivision_vertex_budget(20);
    const uint64_t before =
        openusd_hdsilk_test_get_subdivision_diagnostic_count();
    ParsedPage page;
    const bool synced =
        Sync(session, OPENUSD_SILK_COMPLEXITY_MEDIUM, 1.0, &page, error);
    const uint64_t after =
        openusd_hdsilk_test_get_subdivision_diagnostic_count();
    openusd_hdsilk_test_set_subdivision_vertex_budget(0);
    openusd_silk_session_release(session);

    if (!synced)
    {
        return false;
    }
    if (after <= before)
    {
        return Fail("exceeding the refined mesh budget recorded no diagnostic");
    }
    if (!RequireCounts(page, CubePath, 12, 8))
    {
        return false;
    }
    const ParsedMesh& cube = *FindMesh(page, CubePath);
    if (!ContainsPoint(cube, 1.0F, 1.0F, 1.0F) ||
        !ContainsPoint(cube, -1.0F, -1.0F, -1.0F))
    {
        return Fail(
            "the bounded fallback did not publish the whole control cage");
    }
    return true;
}
}

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        std::cerr << "Usage: hdsilk_subdivision_probe <plugin-path> <stage-path>\n";
        return 2;
    }

    std::array<char, 4096> errorText{};
    openusd_error_buffer error{errorText.data(), errorText.size(), 0};

    openusd_silk_session* session = nullptr;
    if (openusd_silk_session_create(argv[1], argv[2], &session, &error) !=
        OPENUSD_STATUS_OK)
    {
        std::cerr << "hdSilk subdivision probe session create failed: "
                  << errorText.data() << "\n";
        return 3;
    }

    ParsedPage low;
    ParsedPage medium;
    ParsedPage high;
    ParsedPage veryHigh;
    // Bracketing the first refining sync is what attributes a refusal to it:
    // the counter is process-wide, so a delta taken around one sync is the only
    // reading that names which sync refused.
    uint64_t diagnosticsBeforeMedium = 0;
    uint64_t diagnosticsAfterMedium = 0;
    const bool passed =
        Sync(session, OPENUSD_SILK_COMPLEXITY_LOW, 1.0, &low, &error) &&
        VerifyLowIsUnrefined(low) &&
        CaptureDiagnosticCount(&diagnosticsBeforeMedium) &&
        Sync(session, OPENUSD_SILK_COMPLEXITY_MEDIUM, 1.0, &medium, &error) &&
        CaptureDiagnosticCount(&diagnosticsAfterMedium) &&
        VerifyRefinedCube(medium) &&
        VerifyCreasesAndCorners(medium) &&
        VerifyHoles(medium) &&
        VerifySubsetMapping(medium) &&
        VerifyFaceVaryingAndVertexPrimvars(medium) &&
        VerifyFaceVaryingNormalsAreRenormalized(medium) &&
        VerifyUnsupportedFaceVaryingRefusesRefinement(
            low,
            medium,
            diagnosticsBeforeMedium,
            diagnosticsAfterMedium) &&
        VerifyBilinearAndLoop(medium) &&
        VerifyUnrefinableSchemeIsUnchanged(low, medium) &&
        VerifyAnimatedPointsReuseTheRefiner(session, medium, &error) &&
        Sync(session, OPENUSD_SILK_COMPLEXITY_HIGH, 1.0, &high, &error) &&
        Sync(session, OPENUSD_SILK_COMPLEXITY_VERY_HIGH, 1.0, &veryHigh, &error) &&
        VerifyRefinementLevels(high, veryHigh) &&
        Sync(session, OPENUSD_SILK_COMPLEXITY_LOW, 1.0, &low, &error) &&
        VerifyLowIsUnrefined(low);
    openusd_silk_session_release(session);

    if (!passed)
    {
        std::cerr << "hdSilk subdivision probe failed: " << g_failure << "\n";
        return 4;
    }
    if (!VerifyBudgetPublishesTheControlCage(argv[1], argv[2], &error))
    {
        std::cerr << "hdSilk subdivision probe failed: " << g_failure << "\n";
        return 5;
    }
    std::cout << "hdSilk subdivision probe passed.\n";
    return 0;
}
