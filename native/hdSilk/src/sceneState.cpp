// Copyright (c) marcschier. Licensed under the MIT License.

#include "sceneState.h"

#include "instanceLinking.h"
#include "openusd_hdsilk.h"

#include "pxr/base/tf/diagnostic.h"
#include "pxr/base/gf/vec3d.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <exception>
#include <limits>
#include <stdexcept>
#include <utility>

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
// The fixed MESH_UPSERT payload, excluding the 8-byte command header: through
// transform (200) plus the ABI v4 material binding hash, material path byte
// count and attribute count, plus the ABI v20 deformation flags, unsupported
// reasons and block byte count, plus the ABI v22 subprim identity flags,
// unsupported reasons, the four table counts and the instancer path length.
constexpr size_t MeshFixedPayloadSize = 260;

// semantic, component_count, interpolation, name_byte_count, element_count.
constexpr size_t MeshAttributeFixedSize = 20;

// joint_count, influences_per_point, bind_point_count, blend_range_count,
// blend_delta_count, reserved, deformation_identity, geom_bind_transform.
constexpr size_t DeformationFixedSize = 96;

// first_delta, delta_count, weight, reserved.
constexpr size_t DeformationBlendRangeSize = 16;

// point_index, position_offset[3], normal_offset[3].
constexpr size_t DeformationBlendDeltaSize = 28;

// deformation_identity covers every block byte after itself, so it starts at
// the end of the fixed identity field.
constexpr size_t DeformationIdentityOffset = 24;
constexpr size_t DeformationIdentityCoverageOffset = 32;

void AppendU32(std::vector<uint8_t>& buffer, uint32_t value)
{
    buffer.push_back(static_cast<uint8_t>(value & 0xFFu));
    buffer.push_back(static_cast<uint8_t>((value >> 8) & 0xFFu));
    buffer.push_back(static_cast<uint8_t>((value >> 16) & 0xFFu));
    buffer.push_back(static_cast<uint8_t>((value >> 24) & 0xFFu));
}

void AppendI32(std::vector<uint8_t>& buffer, int32_t value)
{
    AppendU32(buffer, static_cast<uint32_t>(value));
}

void AppendU64(std::vector<uint8_t>& buffer, uint64_t value)
{
    for (int shift = 0; shift < 64; shift += 8)
    {
        buffer.push_back(static_cast<uint8_t>((value >> shift) & 0xFFu));
    }
}

void AppendF32(std::vector<uint8_t>& buffer, float value)
{
    uint32_t bits = 0;
    std::memcpy(&bits, &value, sizeof(bits));
    AppendU32(buffer, bits);
}

void AppendF64(std::vector<uint8_t>& buffer, double value)
{
    uint64_t bits = 0;
    std::memcpy(&bits, &value, sizeof(bits));
    AppendU64(buffer, bits);
}

void AppendBytes(std::vector<uint8_t>& buffer, const void* data, size_t size)
{
    if (size == 0)
    {
        return;
    }
    const uint8_t* bytes = static_cast<const uint8_t*>(data);
    buffer.insert(buffer.end(), bytes, bytes + size);
}

void AppendCommand(std::vector<uint8_t>& buffer, uint32_t type, const std::vector<uint8_t>& payload)
{
    if (payload.size() >
        static_cast<size_t>(std::numeric_limits<uint32_t>::max()) - 8)
    {
        throw std::length_error("An hdSilk command exceeds the 32-bit byte_size field.");
    }
    const uint32_t byteSize = static_cast<uint32_t>(8 + payload.size());
    AppendU32(buffer, type);
    AppendU32(buffer, byteSize);
    AppendBytes(buffer, payload.data(), payload.size());
}

uint32_t CheckedCount(size_t value, const char* name)
{
    if (value > std::numeric_limits<uint32_t>::max())
    {
        throw std::length_error(std::string("The hdSilk ") + name +
            " exceeds the 32-bit wire count.");
    }
    return static_cast<uint32_t>(value);
}

size_t CheckedByteCount(size_t count, size_t elementSize, const char* name)
{
    if (count != 0 &&
        elementSize > std::numeric_limits<size_t>::max() / count)
    {
        throw std::length_error(std::string("The hdSilk ") + name +
            " byte count overflows size_t.");
    }
    return count * elementSize;
}

size_t CheckedAdd(size_t left, size_t right, const char* name)
{
    if (right > std::numeric_limits<size_t>::max() - left)
    {
        throw std::length_error(std::string("The hdSilk ") + name +
            " byte count overflows size_t.");
    }
    return left + right;
}

uint64_t ComputeStableHash(const std::string& path)
{
    // 64-bit FNV-1a over the UTF-8 path bytes. Deterministic across
    // platforms and processes and independent of any native pointer,
    // matching the "no native pointers inside data" wire format
    // requirement.
    uint64_t hash = 14695981039346656037ull;
    for (unsigned char byte : path)
    {
        hash ^= static_cast<uint64_t>(byte);
        hash *= 1099511628211ull;
    }
    return hash;
}

bool Utf8PathLess(const std::string& left, const std::string& right)
{
    return std::lexicographical_compare(
        left.begin(),
        left.end(),
        right.begin(),
        right.end(),
        [](char leftByte, char rightByte)
        {
            return static_cast<unsigned char>(leftByte) <
                static_cast<unsigned char>(rightByte);
        });
}

void ValidatePath(const std::string& path)
{
    if (path.empty() || path.front() != '/' ||
        path.find('\0') != std::string::npos)
    {
        throw std::invalid_argument(
            "An hdSilk mesh path must be a non-empty absolute UTF-8 path.");
    }
    static_cast<void>(CheckedCount(path.size(), "path byte count"));
}

/// The exact serialized size of one deformation block, computed from the counts
/// alone. It is deliberately derived from the counts rather than measured off a
/// built buffer, because the byte budget has to be checked before hdSilk
/// allocates the arrays the counts describe.
size_t DeformationBlockByteCount(
    uint32_t bindPointCount,
    uint32_t influencesPerPoint,
    uint32_t jointCount,
    uint32_t blendRangeCount,
    uint32_t blendDeltaCount,
    bool hasBindNormals)
{
    const size_t pointComponents = CheckedByteCount(
        CheckedByteCount(bindPointCount, 3, "deformation bind point"),
        sizeof(float),
        "deformation bind point");
    const size_t influences = CheckedByteCount(
        bindPointCount, influencesPerPoint, "deformation influence");

    size_t size = DeformationFixedSize;
    size = CheckedAdd(size, pointComponents, "deformation block");
    if (hasBindNormals)
    {
        size = CheckedAdd(size, pointComponents, "deformation block");
    }
    size = CheckedAdd(
        size,
        CheckedByteCount(influences, sizeof(uint32_t), "deformation influence"),
        "deformation block");
    size = CheckedAdd(
        size,
        CheckedByteCount(influences, sizeof(float), "deformation influence"),
        "deformation block");
    size = CheckedAdd(
        size,
        CheckedByteCount(
            CheckedByteCount(jointCount, 16, "deformation joint"),
            sizeof(float),
            "deformation joint"),
        "deformation block");
    size = CheckedAdd(
        size,
        CheckedByteCount(
            blendRangeCount, DeformationBlendRangeSize, "deformation blend range"),
        "deformation block");
    size = CheckedAdd(
        size,
        CheckedByteCount(
            blendDeltaCount, DeformationBlendDeltaSize, "deformation blend delta"),
        "deformation block");
    return size;
}

/// Checks every bound and every internal consistency rule of a published rig.
/// A record whose rig does not validate is refused as a whole record, exactly
/// like a record with a malformed index: the alternative is a consumer
/// evaluating a rig that addresses points the record does not carry.
void ValidateDeformation(
    const HdSilkMeshDeformation& deformation,
    size_t pointCount)
{
    if (!deformation.published)
    {
        if (deformation.flags != 0 ||
            !deformation.bindPoints.empty() ||
            !deformation.bindNormals.empty() ||
            !deformation.jointIndices.empty() ||
            !deformation.jointWeights.empty() ||
            !deformation.jointMatrices.empty() ||
            !deformation.blendRanges.empty() ||
            !deformation.blendDeltas.empty())
        {
            throw std::invalid_argument(
                "An unpublished hdSilk deformation must carry no rig data.");
        }
        return;
    }
    const uint32_t knownFlags =
        OPENUSD_SILK_DEFORMATION_FLAG_BIND_NORMALS |
        OPENUSD_SILK_DEFORMATION_FLAG_BLEND_NORMAL_OFFSETS;
    if ((deformation.flags & ~knownFlags) != 0)
    {
        throw std::invalid_argument(
            "An hdSilk deformation carries an unknown flag.");
    }
    if (deformation.jointCount == 0 ||
        deformation.jointCount > OPENUSD_SILK_MAX_DEFORMATION_JOINTS)
    {
        throw std::invalid_argument(
            "An hdSilk deformation joint count is outside the ABI budget.");
    }
    if (deformation.influencesPerPoint == 0 ||
        deformation.influencesPerPoint > OPENUSD_SILK_MAX_DEFORMATION_INFLUENCES)
    {
        throw std::invalid_argument(
            "An hdSilk deformation influence width is outside the ABI budget.");
    }
    if (deformation.blendRanges.size() > OPENUSD_SILK_MAX_DEFORMATION_BLEND_RANGES)
    {
        throw std::invalid_argument(
            "An hdSilk deformation blend range count is outside the ABI budget.");
    }
    if (deformation.blendDeltas.size() > OPENUSD_SILK_MAX_DEFORMATION_BLEND_DELTAS)
    {
        throw std::invalid_argument(
            "An hdSilk deformation blend delta count is outside the ABI budget.");
    }
    if (deformation.bindPoints.size() != pointCount * 3)
    {
        throw std::invalid_argument(
            "An hdSilk deformation must bind exactly one point per emitted point.");
    }
    const bool hasBindNormals =
        (deformation.flags & OPENUSD_SILK_DEFORMATION_FLAG_BIND_NORMALS) != 0;
    if (deformation.bindNormals.size() != (hasBindNormals ? pointCount * 3 : 0))
    {
        throw std::invalid_argument(
            "An hdSilk deformation bind normal array does not match its flag.");
    }
    const size_t influences = pointCount * deformation.influencesPerPoint;
    if (deformation.jointIndices.size() != influences ||
        deformation.jointWeights.size() != influences)
    {
        throw std::invalid_argument(
            "An hdSilk deformation influence stream is not a fixed-width table.");
    }
    if (deformation.jointMatrices.size() !=
        static_cast<size_t>(deformation.jointCount) * 16)
    {
        throw std::invalid_argument(
            "An hdSilk deformation joint palette does not match its joint count.");
    }
    for (uint32_t index : deformation.jointIndices)
    {
        if (index >= deformation.jointCount)
        {
            throw std::invalid_argument(
                "An hdSilk deformation joint index is outside the joint palette.");
        }
    }
    for (float weight : deformation.jointWeights)
    {
        if (!std::isfinite(weight))
        {
            throw std::invalid_argument(
                "An hdSilk deformation joint weight is not finite.");
        }
    }
    for (float value : deformation.jointMatrices)
    {
        if (!std::isfinite(value))
        {
            throw std::invalid_argument(
                "An hdSilk deformation joint matrix element is not finite.");
        }
    }
    for (float value : deformation.geomBindTransform)
    {
        if (!std::isfinite(value))
        {
            throw std::invalid_argument(
                "An hdSilk deformation geom bind transform element is not finite.");
        }
    }
    for (float value : deformation.bindPoints)
    {
        if (!std::isfinite(value))
        {
            throw std::invalid_argument(
                "An hdSilk deformation bind point component is not finite.");
        }
    }
    for (float value : deformation.bindNormals)
    {
        if (!std::isfinite(value))
        {
            throw std::invalid_argument(
                "An hdSilk deformation bind normal component is not finite.");
        }
    }
    for (const HdSilkMeshBlendRange& range : deformation.blendRanges)
    {
        if (!std::isfinite(range.weight))
        {
            throw std::invalid_argument(
                "An hdSilk deformation blend range weight is not finite.");
        }
        const size_t last = CheckedAdd(
            range.firstDelta, range.deltaCount, "deformation blend range");
        if (last > deformation.blendDeltas.size())
        {
            throw std::invalid_argument(
                "An hdSilk deformation blend range is outside the delta table.");
        }
    }
    for (const HdSilkMeshBlendDelta& delta : deformation.blendDeltas)
    {
        if (delta.pointIndex >= pointCount)
        {
            throw std::invalid_argument(
                "An hdSilk deformation blend delta is outside the point array.");
        }
        for (int component = 0; component < 3; ++component)
        {
            if (!std::isfinite(delta.positionOffset[component]) ||
                !std::isfinite(delta.normalOffset[component]))
            {
                throw std::invalid_argument(
                    "An hdSilk deformation blend delta component is not finite.");
            }
        }
    }
}

/// Appends one deformation block and returns its byte count, patching the
/// identity in place so it is exactly the FNV-1a of the bytes the consumer
/// receives rather than a second, independently computed value that could
/// disagree with them.
size_t AppendDeformation(
    std::vector<uint8_t>& payload,
    const HdSilkMeshDeformation& deformation,
    uint32_t bindPointCount)
{
    const size_t start = payload.size();
    AppendU32(payload, deformation.jointCount);
    AppendU32(payload, deformation.influencesPerPoint);
    AppendU32(payload, bindPointCount);
    AppendU32(payload, CheckedCount(deformation.blendRanges.size(), "blend range count"));
    AppendU32(payload, CheckedCount(deformation.blendDeltas.size(), "blend delta count"));
    AppendU32(payload, 0);
    AppendU64(payload, 0);
    for (float value : deformation.geomBindTransform)
    {
        AppendF32(payload, value);
    }
    for (float value : deformation.bindPoints)
    {
        AppendF32(payload, value);
    }
    for (float value : deformation.bindNormals)
    {
        AppendF32(payload, value);
    }
    for (uint32_t value : deformation.jointIndices)
    {
        AppendU32(payload, value);
    }
    for (float value : deformation.jointWeights)
    {
        AppendF32(payload, value);
    }
    for (float value : deformation.jointMatrices)
    {
        AppendF32(payload, value);
    }
    for (const HdSilkMeshBlendRange& range : deformation.blendRanges)
    {
        AppendU32(payload, range.firstDelta);
        AppendU32(payload, range.deltaCount);
        AppendF32(payload, range.weight);
        AppendU32(payload, 0);
    }
    for (const HdSilkMeshBlendDelta& delta : deformation.blendDeltas)
    {
        AppendU32(payload, delta.pointIndex);
        for (float value : delta.positionOffset)
        {
            AppendF32(payload, value);
        }
        for (float value : delta.normalOffset)
        {
            AppendF32(payload, value);
        }
    }

    uint64_t identity = 14695981039346656037ull;
    for (size_t offset = start + DeformationIdentityCoverageOffset;
         offset < payload.size();
         ++offset)
    {
        identity ^= static_cast<uint64_t>(payload[offset]);
        identity *= 1099511628211ull;
    }
    for (size_t byte = 0; byte < 8; ++byte)
    {
        payload[start + DeformationIdentityOffset + byte] =
            static_cast<uint8_t>((identity >> (byte * 8)) & 0xFFu);
    }
    return payload.size() - start;
}

void AppendLight(
    std::vector<uint8_t>& payload,
    const HdSilkLightRecord* record)
{
    if (record == nullptr)
    {
        AppendU32(payload, 0);
        AppendU32(payload, 0);
        AppendU32(payload, 0);
        AppendU32(payload, 0);
        for (int i = 0; i < 4; ++i)
        {
            AppendF32(payload, 0.0f);
        }
        for (int i = 0; i < 16; ++i)
        {
            AppendF64(payload, i % 5 == 0 ? 1.0 : 0.0);
        }
        AppendF32(payload, 0.0f);
        AppendF32(payload, 0.0f);
        AppendF32(payload, 0.0f);
        AppendF32(payload, 0.0f);
        return;
    }

    AppendU32(payload, record->type);
    AppendU32(payload, record->shadowEnabled);
    AppendF32(payload, record->shapeX);
    AppendF32(payload, record->shapeY);
    AppendF32(payload, record->color[0]);
    AppendF32(payload, record->color[1]);
    AppendF32(payload, record->color[2]);
    AppendF32(payload, record->intensity);
    for (double value : record->transform)
    {
        AppendF64(payload, value);
    }
    AppendF32(payload, record->exposure);
    AppendF32(payload, record->diffuse);
    AppendF32(payload, record->specular);
    AppendF32(payload, record->radius);
}

/// Rebuilds the point-origin table of a mesh the points draw mode has just
/// indexed every emitted vertex of.
///
/// The mode draws one point per emitted vertex, including a vertex that carries
/// an authored point no face of the mesh references. USD lets a mesh carry such
/// stray points, and while the mesh is shaded nothing rasterizes them, so the
/// producer publishes the "no authored counterpart" sentinel for them and keeps
/// them out of authored_point_count -- which the ABI defines as one past the
/// largest authored index the table names. Drawn as points they are on screen
/// and pickable like every other point, so leaving the sentinel there answered
/// a pick on a plainly visible point with "this names no authored component",
/// and the declared authored space stopped short of the very points the mode
/// had just made visible.
///
/// The table an unexpanded record publishes IS the identity: emitted vertex v
/// is authored point v, and the sentinel marks exactly the authored points no
/// primitive referenced. Where the table still is that identity, a sentinel
/// therefore names authored point v and is rewritten to it. Where it is not --
/// a face-varying record whose topology was expanded emits one vertex per
/// corner, so several emitted vertices share one authored point -- nothing is
/// rewritten: a sentinel there marks a vertex the mesh generated rather than an
/// authored point that went undrawn, and naming it after its own emitted index
/// would invent an authored point the stage does not have.
void RebuildPointDrawModeOrigins(HdSilkMeshRecord& record, size_t pointCount)
{
    const bool claimsPoints =
        (record.subprimIdentity & OPENUSD_SILK_SUBPRIM_IDENTITY_POINT) != 0;
    if (!claimsPoints && record.pointOrigins.empty())
    {
        return;
    }
    // The claim is kept only while the table still covers one origin per
    // emitted vertex, and only while it is published exactly when it is
    // claimed. Anything else describes vertices this mode has rebuilt the
    // primitives of, and is refused with the geometry reason rather than
    // published against them.
    if (!claimsPoints || record.pointOrigins.size() != pointCount)
    {
        record.RejectSubprimIdentity(OPENUSD_SILK_SUBPRIM_UNSUPPORTED_GEOMETRY);
        return;
    }

    for (size_t vertex = 0; vertex < pointCount; ++vertex)
    {
        const uint32_t origin = record.pointOrigins[vertex];
        if (origin != OPENUSD_SILK_SUBPRIM_NONE &&
            static_cast<size_t>(origin) != vertex)
        {
            // Not the identity mapping, so a sentinel here is a generated
            // vertex. The table already names every authored point it can.
            return;
        }
    }

    // Every vertex now names an authored point, so the largest index the table
    // names is pointCount - 1. A record with as many vertices as the sentinel
    // value could not distinguish its last authored point from "none", so it is
    // refused rather than published with an index that means both.
    if (pointCount >= static_cast<size_t>(OPENUSD_SILK_SUBPRIM_NONE))
    {
        record.RejectSubprimIdentity(OPENUSD_SILK_SUBPRIM_UNSUPPORTED_GEOMETRY);
        return;
    }
    for (size_t vertex = 0; vertex < pointCount; ++vertex)
    {
        record.pointOrigins[vertex] = static_cast<uint32_t>(vertex);
    }
    record.authoredPointCount = static_cast<uint32_t>(pointCount);
}

HdSilkMeshRecord ApplyDrawMode(const HdSilkMeshRecord& record, uint32_t drawMode)
{
    if (record.topologyKind != OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST)
    {
        return record;
    }

    if (drawMode != OPENUSD_SILK_DRAW_MODE_WIREFRAME &&
        drawMode != OPENUSD_SILK_DRAW_MODE_HIDDEN_SURFACE_WIREFRAME &&
        drawMode != OPENUSD_SILK_DRAW_MODE_POINTS)
    {
        return record;
    }

    HdSilkMeshRecord result = record;
    const size_t pointCount = result.points.size() / 3;
    result.materialPath.clear();
    result.attributes.clear();
    // A draw mode that replaces the emitted primitives replaces what the
    // deformation block would be evaluated into, so the rig is dropped with its
    // reason rather than published against a topology it no longer describes.
    result.deformation.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_GEOMETRY);

    if (drawMode == OPENUSD_SILK_DRAW_MODE_POINTS)
    {
        result.topologyKind = OPENUSD_SILK_TOPOLOGY_POINT_LIST;
        result.indices.clear();
        result.indices.reserve(pointCount);
        result.triangleSubprims.clear();
        result.triangleSubprims.reserve(pointCount);
        // Edge and face identity both go. A point list has no corner an
        // authored edge could map onto, and the per-primitive subprim table
        // this branch rebuilds carries no authored face at all: every entry is
        // zero, because a point belongs to no triangulated face. Keeping the
        // FACE claim while replacing the table with that data is what made a
        // mesh drawn as points answer every face pick with authored face zero
        // -- an index nothing on screen came from and no round trip could
        // resolve. Both targets are refused with the same topology-mode reason
        // the emitted points already carry.
        result.cornerEdges.clear();
        result.authoredEdgeCount = 0;
        result.subprimIdentity &= ~(OPENUSD_SILK_SUBPRIM_IDENTITY_FACE |
            OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE);
        result.subprimUnsupported |=
            OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE;
        // Points are the one target this mode can still answer exactly: the
        // emitted vertex array is untouched, so every point origin still names
        // the authored point its vertex came from -- and every authored point
        // the mode has just made visible is renamed after the authored index it
        // came from, rather than staying the sentinel the shaded record needed.
        // The claim is kept only while that stays true: a table that no longer
        // covers one origin per emitted vertex is refused with the geometry
        // reason rather than published against vertices it does not describe.
        RebuildPointDrawModeOrigins(result, pointCount);
        for (size_t point = 0; point < pointCount; ++point)
        {
            if (point > std::numeric_limits<uint32_t>::max())
            {
                throw std::overflow_error("The hdSilk point draw-mode index overflows uint32.");
            }
            result.indices.push_back(static_cast<uint32_t>(point));
            result.triangleSubprims.push_back(0);
        }
        return result;
    }

    result.topologyKind = OPENUSD_SILK_TOPOLOGY_LINE_LIST;
    std::vector<uint32_t> lines;
    std::vector<uint32_t> lineSubprims;
    lines.reserve(result.indices.size() * 2);
    lineSubprims.reserve(result.indices.size());
    for (size_t triangle = 0; triangle + 2 < result.indices.size(); triangle += 3)
    {
        const uint32_t a = result.indices[triangle];
        const uint32_t b = result.indices[triangle + 1];
        const uint32_t c = result.indices[triangle + 2];
        const uint32_t subprim = triangle / 3 < result.triangleSubprims.size()
            ? result.triangleSubprims[triangle / 3]
            : 0u;
        lines.push_back(a);
        lines.push_back(b);
        lines.push_back(b);
        lines.push_back(c);
        lines.push_back(c);
        lines.push_back(a);
        lineSubprims.push_back(subprim);
        lineSubprims.push_back(subprim);
        lineSubprims.push_back(subprim);
    }
    result.indices = std::move(lines);
    result.triangleSubprims = std::move(lineSubprims);
    // A wireframe emits one line per triangle corner edge, in corner order, so
    // the triangle-list corner-edge table is already the line-list one: entry
    // 3t+c became line 3t+c. Nothing is re-derived, and a triangulation
    // diagonal stays OPENUSD_SILK_SUBPRIM_NONE rather than becoming an
    // authored edge because it is now drawn as a visible line.
    if (!result.cornerEdges.empty() &&
        result.cornerEdges.size() != result.triangleSubprims.size())
    {
        result.RejectSubprimIdentity(OPENUSD_SILK_SUBPRIM_UNSUPPORTED_GEOMETRY);
    }
    return result;
}

/// Splits the retained lights into the ordered direct-light table the FRAME
/// command publishes, the bounded dome table it publishes beside it, and the
/// single accumulated ambient term. Separated from AppendFrame because the
/// light-link masks index these exact orderings, so all of them must be derived
/// once from the same input rather than resolved twice.
///
/// The dome table is bounded and all-or-nothing. A scene with more domes than
/// OPENUSD_SILK_MAX_DOME_LIGHTS publishes no dome table at all and reports
/// OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_DOME_BUDGET, rather than publishing the
/// domes that fit: a half-maskable set of domes has no consumer-side sum that is
/// the authored image, while an unpublished table degrades exactly to the
/// pre-v21 result and names the loss.
void SelectDirectLights(
    const std::vector<HdSilkLightRecord>& lights,
    std::vector<HdSilkLightRecord>* outDirectLights,
    std::vector<HdSilkFrameDome>* outDomes,
    bool* outDomeBudgetExceeded,
    float (&outAmbientColor)[3],
    float* outAmbientIntensity)
{
    outDirectLights->clear();
    outDomes->clear();
    *outDomeBudgetExceeded = false;
    outAmbientColor[0] = 0.0f;
    outAmbientColor[1] = 0.0f;
    outAmbientColor[2] = 0.0f;
    *outAmbientIntensity = 0.0f;
    size_t directLightCount = 0;
    size_t domeCount = 0;
    for (const HdSilkLightRecord& light : lights)
    {
        if (light.ambientOnly)
        {
            ++domeCount;
            HdSilkFrameDome dome;
            dome.path = light.path;
            dome.flags = OPENUSD_SILK_DOME_FLAG_PRESENT;
            dome.lightLinkCategory = light.lightLinkCategory;

            // A textured dome's emission is its image. It is published as its
            // own ENVIRONMENT command and deliberately contributes nothing to
            // the ambient term: folding an untextured approximation of it in as
            // well would light the scene twice from one dome.
            if (!light.textureAsset.empty())
            {
                dome.flags |= OPENUSD_SILK_DOME_FLAG_TEXTURED;
            }
            else
            {
                // An untextured dome publishes no ENVIRONMENT record, so the
                // shadow-collection bit the light adapter set has nowhere on the
                // wire to travel. Name it here instead of dropping it.
                if ((light.unsupportedFeatures &
                        OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_SHADOW_COLLECTION) != 0u)
                {
                    TF_WARN(
                        "hdSilk ignores collection:shadowLink on the untextured "
                        "dome light '%s'; no dome shadow map is rendered, and "
                        "applying a caster collection to receiving would darken "
                        "exactly the prims the author asked to keep lit.",
                        light.path.c_str());
                }

                // Storm's unit white dome resolves to 0.96 diffuse irradiance.
                // Preserve that normalization while still honoring authored
                // color, intensity, exposure, and diffuse contribution.
                const float exposed =
                    0.96f * light.intensity * std::pow(2.0f, light.exposure) * light.diffuse;
                dome.ambientColor[0] = light.color[0] * exposed;
                dome.ambientColor[1] = light.color[1] * exposed;
                dome.ambientColor[2] = light.color[2] * exposed;

                // Accumulated from the very floats the dome entry carries, in
                // the order the dome table publishes them. Summing the per-dome
                // ambient of every entry therefore reproduces this value bit for
                // bit, so a consumer that masks domes and one that does not
                // cannot drift apart on a fully linked prim. Multiple domes
                // accumulate instead of whichever record happens to be visited
                // last replacing every earlier one.
                outAmbientColor[0] += dome.ambientColor[0];
                outAmbientColor[1] += dome.ambientColor[1];
                outAmbientColor[2] += dome.ambientColor[2];
                *outAmbientIntensity = 1.0f;
            }

            if (outDomes->size() < OPENUSD_SILK_MAX_DOME_LIGHTS)
            {
                outDomes->push_back(std::move(dome));
            }
            continue;
        }
        ++directLightCount;
        if (outDirectLights->size() < OPENUSD_SILK_MAX_FRAME_LIGHTS)
        {
            outDirectLights->push_back(light);
        }
    }
    if (directLightCount > OPENUSD_SILK_MAX_FRAME_LIGHTS)
    {
        TF_WARN(
            "hdSilk retained %zu of %zu direct lights; the page ABI limit is %u",
            outDirectLights->size(),
            directLightCount,
            OPENUSD_SILK_MAX_FRAME_LIGHTS);
    }
    if (domeCount > OPENUSD_SILK_MAX_DOME_LIGHTS)
    {
        *outDomeBudgetExceeded = true;
        outDomes->clear();
        TF_WARN(
            "hdSilk resolved %zu dome lights; the page ABI limit is %u, so no "
            "dome light-link mask is published and every dome lights every prim.",
            domeCount,
            OPENUSD_SILK_MAX_DOME_LIGHTS);
        for (const HdSilkLightRecord& light : lights)
        {
            if (light.ambientOnly &&
                light.textureAsset.empty() &&
                !light.lightLinkCategory.empty())
            {
                TF_WARN(
                    "hdSilk ignores collection:lightLink on the untextured dome "
                    "light '%s': the scene authors more dome lights than the "
                    "bounded dome table admits.",
                    light.path.c_str());
            }
        }
    }
}

void AppendFrame(
    std::vector<uint8_t>& buffer,
    const HdSilkFrameState& frame,
    const std::vector<HdSilkLightRecord>& directLights,
    const std::vector<HdSilkFrameDome>& domes,
    const float (&ambientColor)[3],
    float ambientIntensity)
{
    std::vector<uint8_t> payload;
    payload.reserve(
        16 +
        sizeof(frame.viewMatrix) +
        sizeof(frame.projectionMatrix) +
        sizeof(frame.clipPlanes) +
        16 +
        (static_cast<size_t>(OPENUSD_SILK_MAX_FRAME_LIGHTS) * 176) +
        16 +
        16 +
        (static_cast<size_t>(OPENUSD_SILK_MAX_DOME_LIGHTS) * 32));
    AppendI32(payload, frame.width);
    AppendI32(payload, frame.height);
    for (double value : frame.viewMatrix)
    {
        AppendF64(payload, value);
    }
    for (double value : frame.projectionMatrix)
    {
        AppendF64(payload, value);
    }
    AppendU32(payload, frame.clipPlaneCount);
    AppendU32(payload, 0);
    for (const auto& plane : frame.clipPlanes)
    {
        for (double value : plane)
        {
            AppendF64(payload, value);
        }
    }

    AppendU32(payload, CheckedCount(directLights.size(), "frame light count"));
    AppendU32(payload, 0);
    AppendU32(payload, 0);
    AppendU32(payload, 0);
    for (size_t i = 0; i < OPENUSD_SILK_MAX_FRAME_LIGHTS; ++i)
    {
        AppendLight(payload, i < directLights.size() ? &directLights[i] : nullptr);
    }
    AppendF32(payload, ambientColor[0]);
    AppendF32(payload, ambientColor[1]);
    AppendF32(payload, ambientColor[2]);
    AppendF32(payload, ambientIntensity);

    AppendU32(payload, CheckedCount(domes.size(), "frame dome count"));
    AppendU32(payload, 0);
    AppendU32(payload, 0);
    AppendU32(payload, 0);
    for (size_t i = 0; i < OPENUSD_SILK_MAX_DOME_LIGHTS; ++i)
    {
        const HdSilkFrameDome* dome = i < domes.size() ? &domes[i] : nullptr;
        AppendF32(payload, dome != nullptr ? dome->ambientColor[0] : 0.0f);
        AppendF32(payload, dome != nullptr ? dome->ambientColor[1] : 0.0f);
        AppendF32(payload, dome != nullptr ? dome->ambientColor[2] : 0.0f);
        AppendF32(payload, 0.0f);
        AppendU32(payload, dome != nullptr ? dome->flags : OPENUSD_SILK_DOME_FLAG_NONE);
        AppendU32(payload, 0);
        AppendU32(payload, 0);
        AppendU32(payload, 0);
    }
    AppendCommand(buffer, OPENUSD_SILK_COMMAND_FRAME, payload);
}

struct MeshWireCounts
{
    uint32_t pathByteCount;
    uint32_t pointCount;
    uint32_t indexCount;
    uint32_t triangleCount;
    uint32_t deformationByteCount;
    uint32_t pointOriginCount;
    uint32_t cornerEdgeCount;
    uint32_t instancerPathByteCount;
    uint32_t instancerContextCount;
    size_t payloadSize;
};

/// The number of corner edges one emitted primitive of a topology owns. A
/// triangle owns three, a line owns the single edge it is, and a point owns
/// none.
size_t CornerEdgesPerPrimitive(uint32_t topologyKind)
{
    switch (topologyKind)
    {
    case OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST:
        return 3;
    case OPENUSD_SILK_TOPOLOGY_LINE_LIST:
        return 1;
    default:
        return 0;
    }
}

/// Checks the ABI v22 subprim-identity tables against the emitted arrays they
/// describe. A table is either absent or complete; a partial table would let a
/// consumer read authored identity for some components and emitted indices for
/// the rest, which is exactly the confusion the tables exist to remove.
void ValidateSubprimIdentity(
    const HdSilkMeshRecord& record,
    size_t pointCount,
    size_t primitiveCount)
{
    if ((record.subprimIdentity & ~(OPENUSD_SILK_SUBPRIM_IDENTITY_FACE |
            OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE |
            OPENUSD_SILK_SUBPRIM_IDENTITY_POINT)) != 0)
    {
        throw std::invalid_argument(
            "An hdSilk mesh declares an unknown subprim identity flag.");
    }
    if ((record.subprimUnsupported &
            ~(OPENUSD_SILK_SUBPRIM_UNSUPPORTED_REFINED_SUBDIVISION |
                OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE |
                OPENUSD_SILK_SUBPRIM_UNSUPPORTED_GEOMETRY |
                OPENUSD_SILK_SUBPRIM_UNSUPPORTED_BUDGET)) != 0)
    {
        throw std::invalid_argument(
            "An hdSilk mesh declares an unknown subprim unsupported reason.");
    }

    // Authored face identity is a claim about `triangle_subprims`: every
    // emitted primitive names the authored face it was triangulated from. Only
    // a triangle list carries that mapping, and the wireframe line list derived
    // from one, whose lines are the corners of those same triangles. A point
    // list emits one primitive per vertex, belonging to no triangulated face,
    // so a FACE claim there names a face nothing on screen came from -- in
    // practice authored face zero, for every point of the mesh. It is refused
    // here as well as at the transform that produces it, so no producer can
    // reintroduce the claim behind the transform's back.
    const bool claimsFaces =
        (record.subprimIdentity & OPENUSD_SILK_SUBPRIM_IDENTITY_FACE) != 0;
    if (claimsFaces &&
        record.topologyKind != OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST &&
        record.topologyKind != OPENUSD_SILK_TOPOLOGY_LINE_LIST)
    {
        throw std::invalid_argument(
            "An hdSilk mesh claims authored face identity for an emitted "
            "topology whose primitives are not authored mesh faces.");
    }
    // The face table is the emitted primitive table itself, so the claim is
    // only meaningful while there is one entry per emitted primitive. A record
    // that lost that correspondence would answer a face pick from an entry that
    // describes a different primitive.
    if (claimsFaces && record.triangleSubprims.size() != primitiveCount)
    {
        throw std::invalid_argument(
            "An hdSilk mesh claims authored face identity without one authored "
            "face per emitted primitive.");
    }

    const bool claimsPoints =
        (record.subprimIdentity & OPENUSD_SILK_SUBPRIM_IDENTITY_POINT) != 0;
    if (claimsPoints != !record.pointOrigins.empty())
    {
        throw std::invalid_argument(
            "An hdSilk mesh point-origin table must be published exactly when "
            "the record claims authored point identity.");
    }
    if (!record.pointOrigins.empty())
    {
        if (record.pointOrigins.size() != pointCount)
        {
            throw std::invalid_argument(
                "An hdSilk mesh requires one point origin per emitted vertex.");
        }
        uint32_t largestPoint = 0;
        bool namedPoint = false;
        for (uint32_t origin : record.pointOrigins)
        {
            if (origin == OPENUSD_SILK_SUBPRIM_NONE)
            {
                continue;
            }
            if (origin >= record.authoredPointCount)
            {
                throw std::invalid_argument(
                    "An hdSilk mesh point origin is outside the authored point "
                    "count it declares.");
            }
            if (!namedPoint || origin > largestPoint)
            {
                largestPoint = origin;
                namedPoint = true;
            }
        }
        // The ABI defines the authored counts as one past the largest index the
        // table names, which is what stops a record from declaring an authored
        // space larger than the entries a consumer read.
        const uint32_t expectedPointCount = namedPoint ? largestPoint + 1 : 0;
        if (record.authoredPointCount != expectedPointCount)
        {
            throw std::invalid_argument(
                "An hdSilk mesh authored point count is not one past the "
                "largest authored index its point-origin table names.");
        }
    }
    else if (record.authoredPointCount != 0)
    {
        throw std::invalid_argument(
            "An hdSilk mesh declares authored points with no point-origin table.");
    }

    const bool claimsEdges =
        (record.subprimIdentity & OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE) != 0;
    if (claimsEdges != !record.cornerEdges.empty())
    {
        throw std::invalid_argument(
            "An hdSilk mesh corner-edge table must be published exactly when "
            "the record claims authored edge identity.");
    }
    if (!record.cornerEdges.empty())
    {
        const size_t expected =
            primitiveCount * CornerEdgesPerPrimitive(record.topologyKind);
        if (expected == 0 || record.cornerEdges.size() != expected)
        {
            throw std::invalid_argument(
                "An hdSilk mesh requires one corner edge per emitted primitive "
                "corner.");
        }
        uint32_t largestEdge = 0;
        bool namedEdge = false;
        for (uint32_t edge : record.cornerEdges)
        {
            if (edge == OPENUSD_SILK_SUBPRIM_NONE)
            {
                continue;
            }
            if (edge >= record.authoredEdgeCount)
            {
                throw std::invalid_argument(
                    "An hdSilk mesh corner edge is outside the authored edge "
                    "count it declares.");
            }
            if (!namedEdge || edge > largestEdge)
            {
                largestEdge = edge;
                namedEdge = true;
            }
        }
        const uint32_t expectedEdgeCount = namedEdge ? largestEdge + 1 : 0;
        if (record.authoredEdgeCount != expectedEdgeCount)
        {
            throw std::invalid_argument(
                "An hdSilk mesh authored edge count is not one past the largest "
                "authored index its corner-edge table names.");
        }
    }
    else if (record.authoredEdgeCount != 0)
    {
        throw std::invalid_argument(
            "An hdSilk mesh declares authored edges with no corner-edge table.");
    }
}

/// Checks every invariant an HdSilkMeshRecord must satisfy before anything
/// indexes into it. This runs on the record as published, before ApplyDrawMode
/// and ApplyComplexity, because both of those dereference record.indices into
/// record.points and into vertex attribute data: validating only the
/// transformed record would let a malformed index read out of bounds on the way
/// to being rejected. The transformed record is validated again, since the
/// transforms rebuild the topology.
void ValidateMeshShape(const HdSilkMeshRecord& record)
{
    ValidatePath(record.path);
    if (record.primId < 0)
    {
        throw std::invalid_argument(
            "An hdSilk MESH_UPSERT requires a non-negative explicit prim ID.");
    }
    if (record.instanceIndex < 0)
    {
        throw std::invalid_argument(
            "An hdSilk mesh instance index must be non-negative.");
    }
    if (record.topologyKind != OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST &&
        record.topologyKind != OPENUSD_SILK_TOPOLOGY_LINE_LIST &&
        record.topologyKind != OPENUSD_SILK_TOPOLOGY_POINT_LIST)
    {
        throw std::invalid_argument("An hdSilk mesh has an unsupported topology kind.");
    }
    if (record.topologyRevision == 0)
    {
        throw std::invalid_argument(
            "An hdSilk mesh topology revision must be non-zero.");
    }
    if (record.doubleSided > 1)
    {
        throw std::invalid_argument("An hdSilk mesh double-sided flag must be 0 or 1.");
    }
    if (record.cullStyle > 5)
    {
        throw std::invalid_argument("An hdSilk mesh cull style is unknown.");
    }
    if ((record.points.size() % 3) != 0)
    {
        throw std::invalid_argument(
            "An hdSilk mesh point component count must be divisible by three.");
    }
    const size_t indicesPerPrimitive =
        record.topologyKind == OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST
            ? 3u
            : (record.topologyKind == OPENUSD_SILK_TOPOLOGY_LINE_LIST ? 2u : 1u);
    if ((record.indices.size() % indicesPerPrimitive) != 0)
    {
        throw std::invalid_argument(
            "An hdSilk mesh index count does not match its topology kind.");
    }

    const size_t pointCount = record.points.size() / 3;
    const size_t primitiveCount = record.indices.size() / indicesPerPrimitive;
    if (record.triangleSubprims.size() != primitiveCount)
    {
        throw std::invalid_argument(
            "An hdSilk mesh requires one authored subprim index per primitive.");
    }
    for (uint32_t index : record.indices)
    {
        if (index >= pointCount)
        {
            throw std::invalid_argument(
                "An hdSilk primitive index is outside the point array.");
        }
    }
    for (const HdSilkMeshAttribute& attribute : record.attributes)
    {
        if (attribute.componentCount < 1 || attribute.componentCount > 4)
        {
            throw std::invalid_argument(
                "An hdSilk mesh attribute must have one to four components.");
        }
        if (attribute.interpolation != OPENUSD_SILK_INTERPOLATION_CONSTANT &&
            attribute.interpolation != OPENUSD_SILK_INTERPOLATION_VERTEX)
        {
            throw std::invalid_argument(
                "An hdSilk mesh attribute has an unsupported interpolation.");
        }
        // Resolved onto emitted vertices before it reaches the wire, so the
        // consumer never has to re-index an attribute against the topology.
        const size_t elementCount =
            attribute.interpolation == OPENUSD_SILK_INTERPOLATION_CONSTANT
                ? 1u
                : pointCount;
        if (attribute.data.size() != elementCount * attribute.componentCount)
        {
            throw std::invalid_argument(
                "An hdSilk mesh attribute length does not match its interpolation.");
        }
        if (attribute.semantic == OPENUSD_SILK_ATTRIBUTE_CUSTOM &&
            attribute.name.empty())
        {
            throw std::invalid_argument(
                "An hdSilk custom mesh attribute requires its authored name.");
        }
    }
    ValidateDeformation(record.deformation, pointCount);
    ValidateSubprimIdentity(record, pointCount, primitiveCount);
    // An instancer path must be a real absolute path when present, and must be
    // present exactly when the record belongs to an instancer: an instance
    // index with no instancer to index is not an identity a consumer can use.
    if (!record.instancerPath.empty())
    {
        ValidatePath(record.instancerPath);
    }
    if (record.instancerPath.empty() != (record.instanceId == 0))
    {
        throw std::invalid_argument(
            "An hdSilk mesh must publish an instancer path exactly when it "
            "belongs to an instancer.");
    }

    // ABI v23. The ordered chain is published exactly when the record belongs
    // to an instancer, and its innermost level is the instancer the record
    // separately names: a chain that disagreed with its own record would hand a
    // consumer two different instances for one hit.
    if (record.instancerContext.empty() == !record.instancerPath.empty())
    {
        throw std::invalid_argument(
            "An hdSilk mesh must publish an instancer context exactly when it "
            "belongs to an instancer.");
    }
    if (record.instancerContext.size() >
        OPENUSD_SILK_MAX_INSTANCER_CONTEXT_ENTRIES)
    {
        throw std::length_error(
            "An hdSilk mesh instancer context exceeds the ABI level budget.");
    }
    for (const HdSilkInstancerContextEntry& entry : record.instancerContext)
    {
        ValidatePath(entry.path);
        if (entry.index < 0)
        {
            throw std::invalid_argument(
                "An hdSilk mesh instancer context index must be non-negative.");
        }
    }
    if (!record.instancerContext.empty() &&
        record.instancerContext.back().path != record.instancerPath)
    {
        throw std::invalid_argument(
            "An hdSilk mesh instancer context must end at the instancer the "
            "record names.");
    }
}

MeshWireCounts ValidateMesh(const HdSilkMeshRecord& record)
{
    ValidateMeshShape(record);
    const size_t pointCount = record.points.size() / 3;
    const size_t indicesPerPrimitive =
        record.topologyKind == OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST
            ? 3u
            : (record.topologyKind == OPENUSD_SILK_TOPOLOGY_LINE_LIST ? 2u : 1u);
    const size_t primitiveCount = record.indices.size() / indicesPerPrimitive;

    size_t payloadSize = MeshFixedPayloadSize;
    payloadSize = CheckedAdd(
        payloadSize, record.path.size(), "mesh payload");
    payloadSize = CheckedAdd(
        payloadSize,
        CheckedByteCount(record.points.size(), sizeof(float), "point"),
        "mesh payload");
    payloadSize = CheckedAdd(
        payloadSize,
        CheckedByteCount(record.indices.size(), sizeof(uint32_t), "index"),
        "mesh payload");
    payloadSize = CheckedAdd(
        payloadSize,
        CheckedByteCount(
            record.triangleSubprims.size(),
            sizeof(uint32_t),
            "primitive subprim"),
        "mesh payload");
    payloadSize = CheckedAdd(
        payloadSize, record.materialPath.size(), "mesh payload");
    for (const HdSilkMeshAttribute& attribute : record.attributes)
    {
        payloadSize = CheckedAdd(payloadSize, MeshAttributeFixedSize, "mesh payload");
        payloadSize = CheckedAdd(payloadSize, attribute.name.size(), "mesh payload");
        payloadSize = CheckedAdd(
            payloadSize,
            CheckedByteCount(attribute.data.size(), sizeof(float), "attribute"),
            "mesh payload");
    }
    size_t deformationByteCount = 0;
    if (record.deformation.published)
    {
        deformationByteCount = DeformationBlockByteCount(
            CheckedCount(pointCount, "deformation bind point count"),
            record.deformation.influencesPerPoint,
            record.deformation.jointCount,
            CheckedCount(record.deformation.blendRanges.size(), "blend range count"),
            CheckedCount(record.deformation.blendDeltas.size(), "blend delta count"),
            (record.deformation.flags &
                OPENUSD_SILK_DEFORMATION_FLAG_BIND_NORMALS) != 0);
        if (deformationByteCount > OPENUSD_SILK_MAX_DEFORMATION_BYTES)
        {
            throw std::length_error(
                "An hdSilk deformation block exceeds the ABI byte budget.");
        }
        payloadSize = CheckedAdd(payloadSize, deformationByteCount, "mesh payload");
    }
    const size_t subprimIdentityByteCount = CheckedAdd(
        CheckedByteCount(
            record.pointOrigins.size(), sizeof(uint32_t), "point origin"),
        CheckedByteCount(
            record.cornerEdges.size(), sizeof(uint32_t), "corner edge"),
        "mesh subprim identity");
    if (subprimIdentityByteCount > OPENUSD_SILK_MAX_SUBPRIM_IDENTITY_BYTES)
    {
        throw std::length_error(
            "An hdSilk subprim identity table exceeds the ABI byte budget.");
    }
    payloadSize = CheckedAdd(payloadSize, subprimIdentityByteCount, "mesh payload");
    payloadSize = CheckedAdd(
        payloadSize, record.instancerPath.size(), "mesh payload");
    for (const HdSilkInstancerContextEntry& entry : record.instancerContext)
    {
        payloadSize = CheckedAdd(payloadSize, 8, "mesh payload");
        payloadSize = CheckedAdd(payloadSize, entry.path.size(), "mesh payload");
    }
    if (payloadSize >
        static_cast<size_t>(std::numeric_limits<uint32_t>::max()) - 8)
    {
        throw std::length_error(
            "An hdSilk MESH_UPSERT exceeds the 32-bit command byte_size.");
    }

    return MeshWireCounts{
        CheckedCount(record.path.size(), "path byte count"),
        CheckedCount(pointCount, "point count"),
        CheckedCount(record.indices.size(), "index count"),
        CheckedCount(primitiveCount, "primitive count"),
        CheckedCount(deformationByteCount, "deformation byte count"),
        CheckedCount(record.pointOrigins.size(), "point origin count"),
        CheckedCount(record.cornerEdges.size(), "corner edge count"),
        CheckedCount(record.instancerPath.size(), "instancer path byte count"),
        CheckedCount(
            record.instancerContext.size(), "instancer context count"),
        payloadSize};
}

uint32_t ComplexityDensity(uint32_t complexity)
{
    switch (complexity)
    {
    case OPENUSD_SILK_COMPLEXITY_MEDIUM:
        return 2;
    case OPENUSD_SILK_COMPLEXITY_HIGH:
        return 4;
    case OPENUSD_SILK_COMPLEXITY_VERY_HIGH:
        return 8;
    case OPENUSD_SILK_COMPLEXITY_LOW:
    default:
        return 1;
    }
}

void AppendVertexAttributeElement(
    HdSilkMeshAttribute& destination,
    const HdSilkMeshAttribute& source,
    uint32_t sourcePoint)
{
    const size_t offset =
        static_cast<size_t>(sourcePoint) * source.componentCount;
    for (uint32_t component = 0; component < source.componentCount; ++component)
    {
        destination.data.push_back(source.data[offset + component]);
    }
}

void AppendInterpolatedVertexAttributeElement(
    HdSilkMeshAttribute& destination,
    const HdSilkMeshAttribute& source,
    uint32_t firstPoint,
    uint32_t secondPoint,
    float t)
{
    const size_t firstOffset =
        static_cast<size_t>(firstPoint) * source.componentCount;
    const size_t secondOffset =
        static_cast<size_t>(secondPoint) * source.componentCount;
    for (uint32_t component = 0; component < source.componentCount; ++component)
    {
        const float first = source.data[firstOffset + component];
        const float second = source.data[secondOffset + component];
        destination.data.push_back(first + ((second - first) * t));
    }
}

void AppendPointWithAttributes(
    HdSilkMeshRecord& destination,
    const HdSilkMeshRecord& source,
    uint32_t sourcePoint)
{
    const size_t offset = static_cast<size_t>(sourcePoint) * 3;
    destination.points.push_back(source.points[offset]);
    destination.points.push_back(source.points[offset + 1]);
    destination.points.push_back(source.points[offset + 2]);
    for (size_t attribute = 0; attribute < source.attributes.size(); ++attribute)
    {
        const HdSilkMeshAttribute& sourceAttribute = source.attributes[attribute];
        if (sourceAttribute.interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX)
        {
            AppendVertexAttributeElement(
                destination.attributes[attribute],
                sourceAttribute,
                sourcePoint);
        }
    }
}

/// Emits one point of a complexity-subdivided line at parameter t, together with
/// every vertex attribute the record carries. Attributes are interpolated at the
/// same t as the position rather than copied from whichever endpoint is nearer:
/// a subdivided segment is the same segment, so a per-vertex value such as an
/// authored curve width has to vary along it exactly as the position does.
/// Selecting an endpoint instead made a 2x-subdivided curve report a step
/// function where the authored data was a ramp.
void AppendInterpolatedLinePoint(
    HdSilkMeshRecord& destination,
    const HdSilkMeshRecord& source,
    uint32_t firstPoint,
    uint32_t secondPoint,
    float t)
{
    const size_t firstOffset = static_cast<size_t>(firstPoint) * 3;
    const size_t secondOffset = static_cast<size_t>(secondPoint) * 3;
    for (size_t component = 0; component < 3; ++component)
    {
        const float first = source.points[firstOffset + component];
        const float second = source.points[secondOffset + component];
        destination.points.push_back(first + ((second - first) * t));
    }
    for (size_t attribute = 0; attribute < source.attributes.size(); ++attribute)
    {
        const HdSilkMeshAttribute& sourceAttribute = source.attributes[attribute];
        if (sourceAttribute.interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX)
        {
            AppendInterpolatedVertexAttributeElement(
                destination.attributes[attribute],
                sourceAttribute,
                firstPoint,
                secondPoint,
                t);
        }
    }
}

/// The exact number of vertices a complexity duplication emits for a point
/// list, together with the mapping every one of them satisfies.
///
/// The point-list branch of ApplyComplexity emits `density` whole copies of the
/// authored point behind each of the record's emitted primitives, in primitive
/// order, so
///
///     emitted vertices == emitted point origins == indices.size() * density
///
/// and emitted vertex (p * density + c) is the source vertex indices[p], whose
/// authored point is pointOrigins[indices[p]].
///
/// The emitted count is therefore the INDEX count and not the source vertex
/// count. A point list may carry a vertex no primitive draws -- USD lets a mesh
/// carry stray points, and a record whose draw mode did not index them keeps
/// them in its point array -- and the two counts then differ. Sizing the
/// budget, the reservation and the refusal from the source table's own length
/// instead made all three describe a table this branch never emits: a record
/// just inside the budget was refused for a table it would not have built, and
/// a record just outside it reserved past the very bound the budget exists to
/// hold.
size_t ComplexityEmittedOriginCount(
    const HdSilkMeshRecord& record,
    uint32_t density)
{
    return CheckedByteCount(
        record.indices.size(),
        static_cast<size_t>(density),
        "complexity point origin");
}

HdSilkMeshRecord ApplyComplexity(
    HdSilkMeshRecord record,
    uint32_t complexity)
{
    const uint32_t density = ComplexityDensity(complexity);
    if (density == 1 ||
        (record.topologyKind != OPENUSD_SILK_TOPOLOGY_LINE_LIST &&
            record.topologyKind != OPENUSD_SILK_TOPOLOGY_POINT_LIST) ||
        record.points.empty())
    {
        return record;
    }

    // The source identity table is taken out of the record before anything is
    // copied or reserved. No branch here publishes it -- a resubdivided line
    // refuses point identity outright and a duplicated point list emits a
    // rebuilt table -- so copying it into the result and dropping it again
    // duplicated up to the ABI's whole bounded identity allocation, per record,
    // before the preflight that decides whether even one entry may be reserved
    // had run. Moving it into a local leaves the result with no capacity at all
    // while keeping the source entries readable for the emission below.
    const std::vector<uint32_t> sourcePointOrigins =
        std::move(record.pointOrigins);
    record.pointOrigins.clear();

    const size_t sourcePointCount = record.points.size() / 3;
    const bool duplicatesPoints =
        record.topologyKind == OPENUSD_SILK_TOPOLOGY_POINT_LIST;

    // Point-list preflight. Every decision it makes is arithmetic on counts the
    // source already carries, so it runs before the record is copied and before
    // a single emitted entry is reserved.
    size_t emittedOriginCount = 0;
    uint32_t identityRefusal = OPENUSD_SILK_SUBPRIM_UNSUPPORTED_NONE;
    bool duplicateOrigins = false;
    if (duplicatesPoints)
    {
        const bool claimsPoints =
            (record.subprimIdentity & OPENUSD_SILK_SUBPRIM_IDENTITY_POINT) != 0;
        // The source table must cover exactly one origin per source vertex, and
        // every drawn index must land inside it: that is what makes
        // pointOrigins[indices[p]] a read this branch may perform at all. A
        // table that is missing, short or long -- or published without the
        // claim -- describes vertices about to be duplicated rather than the
        // ones that will be emitted, so it is refused by name instead of being
        // indexed into.
        bool originsUsable = !sourcePointOrigins.empty() &&
            sourcePointOrigins.size() == sourcePointCount;
        for (size_t primitive = 0;
             originsUsable && primitive < record.indices.size();
             ++primitive)
        {
            originsUsable =
                record.indices[primitive] < sourcePointOrigins.size();
        }
        duplicateOrigins = claimsPoints && originsUsable;
        if (!duplicateOrigins &&
            (claimsPoints || !sourcePointOrigins.empty()))
        {
            identityRefusal = OPENUSD_SILK_SUBPRIM_UNSUPPORTED_GEOMETRY;
        }
        // The shared identity budget is re-checked against the size the emitted
        // table WOULD reach, from the exact emitted count and not from the
        // source table's length. Exceeding it drops only the point claim, with
        // the budget as the reason: the duplicated geometry is still published,
        // exactly as an over-budget point cloud is at Low.
        if (duplicateOrigins)
        {
            emittedOriginCount = ComplexityEmittedOriginCount(record, density);
            if (HdSilkSubprimIdentityExceedsBudget(emittedOriginCount, 0))
            {
                duplicateOrigins = false;
                emittedOriginCount = 0;
                identityRefusal = OPENUSD_SILK_SUBPRIM_UNSUPPORTED_BUDGET;
            }
        }
    }

    HdSilkMeshRecord result = record;
    result.points.clear();
    result.indices.clear();
    result.triangleSubprims.clear();
    // Complexity rebuilds the point array itself, so the bind pose the rig
    // addresses no longer indexes the emitted points.
    result.deformation.Reject(OPENUSD_SILK_DEFORMATION_UNSUPPORTED_GEOMETRY);
    // A resubdivided line rebuilds the emitted vertices and primitives: it has
    // interior vertices no authored point corresponds to, and its segments are
    // fractions of an authored edge rather than the edge. Both tables are
    // dropped with the reason instead of being renumbered onto components the
    // scene never authored. A point list is different, and is handled below:
    // it emits whole copies of authored points, so every emitted vertex still
    // names exactly one authored point.
    if (!duplicatesPoints)
    {
        result.RejectSubprimIdentity(
            OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE);
    }
    for (HdSilkMeshAttribute& attribute : result.attributes)
    {
        if (attribute.interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX)
        {
            attribute.data.clear();
        }
    }

    if (duplicatesPoints)
    {
        // Face and edge identity stay refused for exactly the reasons a point
        // list refuses them at every other stage: a point belongs to no
        // triangulated face, and there is no corner an authored edge could map
        // onto. Neither claim becomes answerable because the points were
        // duplicated, so both are dropped here with the topology-mode reason.
        std::vector<uint32_t>().swap(result.cornerEdges);
        result.authoredEdgeCount = 0;
        result.subprimIdentity &= ~(OPENUSD_SILK_SUBPRIM_IDENTITY_FACE |
            OPENUSD_SILK_SUBPRIM_IDENTITY_EDGE);
        result.subprimUnsupported |=
            OPENUSD_SILK_SUBPRIM_UNSUPPORTED_TOPOLOGY_MODE;

        // Point identity survives complexity here, because this branch emits
        // whole copies of authored points rather than new interior geometry:
        // every duplicate of authored point p is still authored point p, so the
        // mapping stays exact and each copy carries the source origin along.
        // Refusing it wholesale -- which is what this did before -- made a
        // point cloud viewed at anything above Low answer no point pick at all,
        // even though every emitted vertex was an authored point, and it also
        // silently zeroed authoredPointCount, so a consumer could not even tell
        // how large the authored point space had been.
        //
        // Whichever way the preflight decided, the geometry below is published:
        // a refusal names Budget or Geometry and costs the record only its
        // point claim.
        if (identityRefusal != OPENUSD_SILK_SUBPRIM_UNSUPPORTED_NONE)
        {
            result.RejectSubprimIdentity(identityRefusal);
        }
        if (duplicateOrigins)
        {
            result.pointOrigins.reserve(emittedOriginCount);
        }

        uint32_t largestOrigin = 0;
        bool namedOrigin = false;
        for (size_t primitive = 0; primitive < record.indices.size(); ++primitive)
        {
            const uint32_t point = record.indices[primitive];
            for (uint32_t copy = 0; copy < density; ++copy)
            {
                const uint32_t emitted =
                    CheckedCount(result.points.size() / 3, "complexity point");
                AppendPointWithAttributes(result, record, point);
                result.indices.push_back(emitted);
                result.triangleSubprims.push_back(record.triangleSubprims[primitive]);
                if (duplicateOrigins)
                {
                    const uint32_t origin = sourcePointOrigins[point];
                    result.pointOrigins.push_back(origin);
                    if (origin != OPENUSD_SILK_SUBPRIM_NONE &&
                        (!namedOrigin || origin > largestOrigin))
                    {
                        largestOrigin = origin;
                        namedOrigin = true;
                    }
                }
            }
        }
        if (duplicateOrigins &&
            result.pointOrigins.size() != emittedOriginCount)
        {
            // The emission is what the preflight sized the budget and the
            // reservation from, so a table that does not close on that exact
            // count means the mapping invariant no longer holds. It is refused
            // rather than published against a count nothing checked.
            result.RejectSubprimIdentity(
                OPENUSD_SILK_SUBPRIM_UNSUPPORTED_GEOMETRY);
        }
        // authoredPointCount is retained exactly, and is only kept while the
        // duplicated table still names the same authored space the source
        // declared. A source vertex no primitive indexes would otherwise drop
        // out of the emitted table and shrink that space behind the count,
        // which the ABI defines as one past the largest authored index the
        // table names. That mismatch is a refusal by name, not a silent
        // renumbering onto an authored space the record never declared.
        else if (duplicateOrigins &&
            record.authoredPointCount !=
                (namedOrigin ? largestOrigin + 1u : 0u))
        {
            result.RejectSubprimIdentity(
                OPENUSD_SILK_SUBPRIM_UNSUPPORTED_GEOMETRY);
        }
        return result;
    }

    for (size_t primitive = 0; primitive < record.indices.size() / 2; ++primitive)
    {
        const uint32_t first = record.indices[primitive * 2];
        const uint32_t second = record.indices[(primitive * 2) + 1];
        for (uint32_t segment = 0; segment < density; ++segment)
        {
            const uint32_t emitted =
                CheckedCount(result.points.size() / 3, "complexity point");
            const float start = static_cast<float>(segment) / density;
            const float end = static_cast<float>(segment + 1) / density;
            AppendInterpolatedLinePoint(
                result,
                record,
                first,
                second,
                start);
            AppendInterpolatedLinePoint(
                result,
                record,
                first,
                second,
                end);
            result.indices.push_back(emitted);
            result.indices.push_back(emitted + 1);
            result.triangleSubprims.push_back(record.triangleSubprims[primitive]);
        }
    }
    return result;
}

/// The topology revision one record publishes for the presentation it is drawn
/// with.
///
/// `topology_revision` is what a consumer keys its retained topology by: a
/// record that arrives carrying the revision the consumer already holds is
/// taken to have the topology the consumer already holds, and a record that
/// contradicts that is refused outright rather than silently replacing it. The
/// draw mode and the complexity level both rebuild the emitted topology -- a
/// shaded triangle list becomes a line list or a point list, a line list is
/// resubdivided, a point list is duplicated, and the point-origin table is
/// rebuilt with it -- while the authored topology, and therefore the source
/// revision, does not move at all. Publishing the source revision for every
/// presentation handed the consumer two different topologies under one
/// revision, which is exactly the contradiction it refuses: applying a shaded
/// page and then a points page, or a Low page and then a Medium page, to one
/// retained scene was rejected as a topology that changed without a new
/// revision.
///
/// The published revision is therefore the source revision composed with the
/// presentation that produced the emitted arrays. It is
///   * exactly the source revision when the presentation rebuilds nothing, so
///     a session that never leaves smooth-shaded Low publishes the revisions it
///     always did;
///   * a pure function of the source revision and the presentation, so
///     republishing one presentation republishes one revision and a consumer
///     keeps its retained topology instead of rotating identity for a page that
///     changed nothing;
///   * distinct from the source revision, and from every other presentation of
///     the same source, so every presentation change is visible as a revision
///     change and switching back rotates the revision again.
///
/// It is deliberately derived from the presentation rather than from the
/// emitted arrays: an ABI v8 instance reference carries no arrays at all and
/// must publish exactly the revision of the prototype payload whose geometry it
/// reuses.
uint64_t PresentationTopologyRevision(
    uint64_t sourceRevision,
    uint32_t sourceTopologyKind,
    uint32_t presentedTopologyKind,
    uint32_t density)
{
    if (sourceTopologyKind == presentedTopologyKind && density == 1)
    {
        return sourceRevision;
    }

    uint64_t mixed = 14695981039346656037ull;
    const uint64_t inputs[] = {
        sourceRevision,
        static_cast<uint64_t>(sourceTopologyKind),
        static_cast<uint64_t>(presentedTopologyKind),
        static_cast<uint64_t>(density)};
    for (uint64_t input : inputs)
    {
        for (unsigned byte = 0; byte < 8; ++byte)
        {
            mixed ^= (input >> (byte * 8)) & 0xFFull;
            mixed *= 1099511628211ull;
        }
    }
    // Zero is the "no revision" a record may never publish, and the source
    // revision is the one value a presented topology has to be distinguishable
    // from. A mix that lands on either is stepped off it, which stays a pure
    // function of the same inputs.
    while (mixed == 0 || mixed == sourceRevision)
    {
        ++mixed;
    }
    return mixed;
}

void AppendMeshUpsert(
    std::vector<uint8_t>& buffer,
    HdSilkMeshRecord record,
    uint32_t complexity,
    uint32_t sourceTopologyKind)
{
    const uint64_t sourceRevision = record.topologyRevision;
    HdSilkMeshRecord complexRecord =
        ApplyComplexity(std::move(record), complexity);
    // Complexity rebuilds only line and point topology, so a record that
    // reaches the wire as a triangle list is presented at density one whatever
    // the session's complexity is: its refinement level is what complexity
    // moves, and that already moved the source revision at the producer.
    const uint32_t presentedDensity =
        (complexRecord.topologyKind == OPENUSD_SILK_TOPOLOGY_LINE_LIST ||
            complexRecord.topologyKind == OPENUSD_SILK_TOPOLOGY_POINT_LIST)
            ? ComplexityDensity(complexity)
            : 1u;
    complexRecord.topologyRevision = PresentationTopologyRevision(
        sourceRevision,
        sourceTopologyKind,
        complexRecord.topologyKind,
        presentedDensity);
    const MeshWireCounts counts = ValidateMesh(complexRecord);

    std::vector<uint8_t> payload;
    payload.reserve(counts.payloadSize);

    AppendU64(payload, ComputeStableHash(complexRecord.path));
    AppendI32(payload, complexRecord.primId);
    AppendI32(payload, complexRecord.instanceId);
    AppendI32(payload, complexRecord.instanceIndex);
    AppendU32(payload, complexRecord.topologyKind);
    AppendU64(payload, complexRecord.topologyRevision);
    AppendU32(payload, complexRecord.doubleSided);
    AppendU32(payload, complexRecord.cullStyle);
    AppendU32(payload, counts.pathByteCount);
    AppendU32(payload, counts.pointCount);
    AppendU32(payload, counts.indexCount);
    AppendU32(payload, counts.triangleCount);
    for (float value : complexRecord.displayColor)
    {
        AppendF32(payload, value);
    }
    for (double value : complexRecord.transform)
    {
        AppendF64(payload, value);
    }
    AppendU64(
        payload,
        complexRecord.materialPath.empty() ? 0ull : ComputeStableHash(complexRecord.materialPath));
    AppendU32(payload, CheckedCount(complexRecord.materialPath.size(), "material path byte count"));
    AppendU32(payload, CheckedCount(complexRecord.attributes.size(), "attribute count"));
    AppendU32(payload, complexRecord.deformation.published
        ? complexRecord.deformation.flags
        : 0u);
    AppendU32(payload, complexRecord.deformation.unsupportedFeatures);
    AppendU32(payload, counts.deformationByteCount);
    AppendU32(payload, complexRecord.subprimIdentity);
    AppendU32(payload, complexRecord.subprimUnsupported);
    AppendU32(payload, counts.pointOriginCount);
    AppendU32(payload, counts.cornerEdgeCount);
    AppendU32(payload, complexRecord.authoredEdgeCount);
    AppendU32(payload, complexRecord.authoredPointCount);
    AppendU32(payload, counts.instancerPathByteCount);
    AppendU32(payload, counts.instancerContextCount);
    AppendBytes(payload, complexRecord.path.data(), complexRecord.path.size());
    for (float value : complexRecord.points)
    {
        AppendF32(payload, value);
    }
    for (uint32_t value : complexRecord.indices)
    {
        AppendU32(payload, value);
    }
    for (uint32_t value : complexRecord.triangleSubprims)
    {
        AppendU32(payload, value);
    }
    AppendBytes(payload, complexRecord.materialPath.data(), complexRecord.materialPath.size());
    for (const HdSilkMeshAttribute& attribute : complexRecord.attributes)
    {
        AppendU32(payload, attribute.semantic);
        AppendU32(payload, attribute.componentCount);
        AppendU32(payload, attribute.interpolation);
        AppendU32(payload, CheckedCount(attribute.name.size(), "attribute name byte count"));
        AppendU32(
            payload,
            CheckedCount(
                attribute.data.size() / attribute.componentCount,
                "attribute element count"));
        AppendBytes(payload, attribute.name.data(), attribute.name.size());
        for (float value : attribute.data)
        {
            AppendF32(payload, value);
        }
    }
    if (complexRecord.deformation.published)
    {
        const size_t written = AppendDeformation(
            payload,
            complexRecord.deformation,
            counts.pointCount);
        if (written != counts.deformationByteCount)
        {
            throw std::logic_error(
                "The hdSilk deformation block byte count does not match the "
                "bytes the block budget was checked against.");
        }
    }
    for (uint32_t value : complexRecord.pointOrigins)
    {
        AppendU32(payload, value);
    }
    for (uint32_t value : complexRecord.cornerEdges)
    {
        AppendU32(payload, value);
    }
    AppendBytes(
        payload,
        complexRecord.instancerPath.data(),
        complexRecord.instancerPath.size());
    for (const HdSilkInstancerContextEntry& entry : complexRecord.instancerContext)
    {
        AppendU32(
            payload,
            CheckedCount(entry.path.size(), "instancer context path byte count"));
        AppendI32(payload, entry.index);
        AppendBytes(payload, entry.path.data(), entry.path.size());
    }

    AppendCommand(buffer, OPENUSD_SILK_COMMAND_MESH_UPSERT, payload);
}

void AppendMeshRemove(std::vector<uint8_t>& buffer, const HdSilkMeshKey& key)
{
    ValidatePath(key.path);
    const uint32_t pathByteCount = CheckedCount(key.path.size(), "path byte count");
    if (key.path.size() >
        static_cast<size_t>(std::numeric_limits<uint32_t>::max()) - 24)
    {
        throw std::length_error(
            "An hdSilk MESH_REMOVE exceeds the 32-bit command byte_size.");
    }

    std::vector<uint8_t> payload;
    payload.reserve(8 + 4 + 4 + pathByteCount);
    AppendU64(payload, ComputeStableHash(key.path));
    AppendI32(payload, key.instanceIndex);
    AppendU32(payload, pathByteCount);
    AppendBytes(payload, key.path.data(), key.path.size());

    AppendCommand(buffer, OPENUSD_SILK_COMMAND_MESH_REMOVE, payload);
}

void AppendMaterialUpsert(
    std::vector<uint8_t>& buffer,
    const HdSilkMaterialRecord& record)
{
    ValidatePath(record.path);
    if (record.surfaceKind != OPENUSD_SILK_SURFACE_UNSUPPORTED &&
        record.surfaceKind != OPENUSD_SILK_SURFACE_PREVIEW_SURFACE &&
        record.surfaceKind != OPENUSD_SILK_SURFACE_MATERIALX_PROJECTED &&
        record.surfaceKind != OPENUSD_SILK_SURFACE_MATERIALX_GENERATED &&
        record.surfaceKind != OPENUSD_SILK_SURFACE_VOLUME_DENSITY &&
        record.surfaceKind != OPENUSD_SILK_SURFACE_MDL_DISTILLED &&
        record.surfaceKind != OPENUSD_SILK_SURFACE_MDL_UNAVAILABLE)
    {
        throw std::invalid_argument("The hdSilk material surface kind is unknown.");
    }
    for (const HdSilkMaterialScalar& scalar : record.scalars)
    {
        if (scalar.parameter == 0 ||
            scalar.componentCount == 0 || scalar.componentCount > 4)
        {
            throw std::invalid_argument(
                "An hdSilk material scalar needs a parameter and 1 to 4 components.");
        }
    }
    for (const HdSilkMaterialTexture& texture : record.textures)
    {
        if (texture.parameter == 0 ||
            texture.componentCount == 0 || texture.componentCount > 4)
        {
            throw std::invalid_argument(
                "An hdSilk material texture needs a parameter and 1 to 4 components.");
        }
        if (texture.wrapS > OPENUSD_SILK_WRAP_USE_METADATA ||
            texture.wrapT > OPENUSD_SILK_WRAP_USE_METADATA ||
            texture.sourceColorSpace > OPENUSD_SILK_COLOR_SPACE_SRGB)
        {
            throw std::invalid_argument(
                "An hdSilk material texture has an unknown wrap or color space.");
        }
        if (texture.asset.empty())
        {
            throw std::invalid_argument(
                "An hdSilk material texture requires a resolved asset path.");
        }
        if (texture.outputChannel > OPENUSD_SILK_TEXTURE_CHANNEL_RGB)
        {
            throw std::invalid_argument(
                "An hdSilk material texture has an unresolved or unknown "
                "UsdUVTexture output channel.");
        }
        const bool isRgbChannel =
            texture.outputChannel == OPENUSD_SILK_TEXTURE_CHANNEL_RGB;
        if (isRgbChannel ? texture.componentCount < 3 : texture.componentCount != 1)
        {
            throw std::invalid_argument(
                "An hdSilk material texture output channel must be rgb for a "
                "colour or vector input and a single channel for a one-component "
                "input.");
        }
        if (texture.compositeOp > OPENUSD_SILK_COMPOSITE_MIX)
        {
            throw std::invalid_argument(
                "An hdSilk material texture has an unknown composite operator.");
        }
        if (!std::isfinite(texture.compositeFactor))
        {
            throw std::invalid_argument(
                "An hdSilk material texture composite factor must be finite.");
        }
    }

    // A composite entry is the *second* operand of its parameter, so it is
    // meaningless without the first. The whole point of the wire carrying both
    // is that a consumer can bind two images for one input; publishing a lone
    // composite would make the consumer either drop it silently or render one
    // operand of an authored pair.
    size_t compositeCount = 0;
    for (const HdSilkMaterialTexture& texture : record.textures)
    {
        size_t primaries = 0;
        size_t composites = 0;
        for (const HdSilkMaterialTexture& sibling : record.textures)
        {
            if (sibling.parameter != texture.parameter)
            {
                continue;
            }
            if (sibling.compositeOp == OPENUSD_SILK_COMPOSITE_NONE)
            {
                ++primaries;
            }
            else
            {
                ++composites;
            }
        }
        if (primaries != 1 || composites > 1)
        {
            throw std::invalid_argument(
                "An hdSilk material parameter must carry exactly one primary "
                "texture and at most one composite operand.");
        }
        if (texture.compositeOp != OPENUSD_SILK_COMPOSITE_NONE)
        {
            ++compositeCount;
        }
    }
    if (compositeCount > 1)
    {
        // The consumer binds one composite texture per material, not one per
        // surface input, so a second composited parameter has no slot to sample.
        throw std::invalid_argument(
            "An hdSilk material carries at most one composite texture operand.");
    }

    for (size_t index = 0; index < 6; ++index)
    {
        if (!std::isfinite(record.uvTransform[index]))
        {
            throw std::invalid_argument(
                "An hdSilk material UV transform must be finite.");
        }
    }

    const uint32_t pathByteCount = CheckedCount(record.path.size(), "path byte count");
    std::vector<uint8_t> payload;
    payload.reserve(24 + pathByteCount);
    AppendU64(payload, ComputeStableHash(record.path));
    AppendU32(payload, pathByteCount);
    AppendU32(payload, record.surfaceKind);
    AppendU32(payload, CheckedCount(record.scalars.size(), "material scalar count"));
    AppendU32(payload, CheckedCount(record.textures.size(), "material texture count"));
    AppendBytes(payload, record.path.data(), record.path.size());

    for (const HdSilkMaterialScalar& scalar : record.scalars)
    {
        AppendU32(payload, scalar.parameter);
        AppendU32(payload, scalar.componentCount);
        for (uint32_t index = 0; index < scalar.componentCount; ++index)
        {
            AppendF32(payload, scalar.value[index]);
        }
    }

    for (const HdSilkMaterialTexture& texture : record.textures)
    {
        AppendU32(payload, texture.parameter);
        AppendU32(payload, texture.wrapS);
        AppendU32(payload, texture.wrapT);
        AppendU32(payload, texture.sourceColorSpace);
        AppendU32(payload, CheckedCount(texture.asset.size(), "material asset byte count"));
        AppendU32(
            payload,
            CheckedCount(texture.uvPrimvar.size(), "material uv primvar byte count"));
        AppendU32(payload, texture.componentCount);
        for (int index = 0; index < 4; ++index)
        {
            AppendF32(payload, texture.scale[index]);
        }
        for (int index = 0; index < 4; ++index)
        {
            AppendF32(payload, texture.bias[index]);
        }
        for (int index = 0; index < 4; ++index)
        {
            AppendF32(payload, texture.fallback[index]);
        }
        AppendU32(payload, texture.outputChannel);
        AppendU32(payload, texture.compositeOp);
        AppendF32(payload, texture.compositeFactor);
        AppendBytes(payload, texture.asset.data(), texture.asset.size());
        AppendBytes(payload, texture.uvPrimvar.data(), texture.uvPrimvar.size());
    }
    AppendU32(
        payload,
        CheckedCount(
            record.generatedFragmentSpirv.size() * sizeof(uint32_t),
            "generated MaterialX fragment SPIR-V byte count"));
    for (uint32_t word : record.generatedFragmentSpirv)
    {
        AppendU32(payload, word);
    }
    AppendU32(
        payload,
        CheckedCount(
            record.generatedFragmentMslSource.size(),
            "generated MaterialX fragment MSL source byte count"));
    AppendBytes(
        payload,
        record.generatedFragmentMslSource.data(),
        record.generatedFragmentMslSource.size());
    for (size_t index = 0; index < 6; ++index)
    {
        AppendF32(payload, record.uvTransform[index]);
    }

    AppendCommand(buffer, OPENUSD_SILK_COMMAND_MATERIAL_UPSERT, payload);
}

void AppendMaterialRemove(std::vector<uint8_t>& buffer, const std::string& path)
{
    ValidatePath(path);
    const uint32_t pathByteCount = CheckedCount(path.size(), "path byte count");
    std::vector<uint8_t> payload;
    payload.reserve(8 + 4 + pathByteCount);
    AppendU64(payload, ComputeStableHash(path));
    AppendU32(payload, pathByteCount);
    AppendBytes(payload, path.data(), path.size());

    AppendCommand(buffer, OPENUSD_SILK_COMMAND_MATERIAL_REMOVE, payload);
}

void AppendEnvironmentUpsert(
    std::vector<uint8_t>& buffer,
    const std::string& path,
    const HdSilkEnvironmentSnapshot& snapshot)
{
    ValidatePath(path);
    if (snapshot.textureAsset.empty() ||
        snapshot.textureAsset.find('\0') != std::string::npos)
    {
        throw std::invalid_argument(
            "An hdSilk environment record requires a non-empty texture asset path.");
    }
    if (snapshot.textureFormat > OPENUSD_SILK_DOME_TEXTURE_CUBE_MAP_VERTICAL_CROSS ||
        snapshot.sourceColorSpace > OPENUSD_SILK_COLOR_SPACE_SRGB)
    {
        throw std::invalid_argument(
            "An hdSilk environment record carries an unknown texture format or "
            "colour space.");
    }
    if (snapshot.domeIndex != OPENUSD_SILK_DOME_INDEX_NONE &&
        snapshot.domeIndex >= OPENUSD_SILK_MAX_DOME_LIGHTS)
    {
        throw std::invalid_argument(
            "An hdSilk environment record carries a dome index outside the "
            "bounded dome table.");
    }
    for (int index = 0; index < 3; ++index)
    {
        if (!std::isfinite(snapshot.color[index]))
        {
            throw std::invalid_argument(
                "An hdSilk environment colour must be finite.");
        }
    }
    if (!std::isfinite(snapshot.intensity) ||
        !std::isfinite(snapshot.exposure) ||
        !std::isfinite(snapshot.diffuse) ||
        !std::isfinite(snapshot.specular))
    {
        throw std::invalid_argument(
            "An hdSilk environment emission control must be finite.");
    }
    for (int index = 0; index < 16; ++index)
    {
        if (!std::isfinite(snapshot.transform[index]))
        {
            throw std::invalid_argument(
                "An hdSilk environment transform must be finite.");
        }
    }

    const uint32_t pathByteCount = CheckedCount(path.size(), "path byte count");
    const uint32_t textureByteCount = CheckedCount(
        snapshot.textureAsset.size(),
        "environment texture byte count");
    std::vector<uint8_t> payload;
    payload.reserve(192 + pathByteCount + textureByteCount);
    AppendU64(payload, ComputeStableHash(path));
    AppendU32(payload, pathByteCount);
    AppendU32(payload, textureByteCount);
    AppendU32(payload, snapshot.textureFormat);
    AppendU32(payload, snapshot.sourceColorSpace);
    AppendU32(payload, snapshot.unsupportedFeatures);
    AppendU32(payload, snapshot.domeIndex);
    AppendF32(payload, snapshot.color[0]);
    AppendF32(payload, snapshot.color[1]);
    AppendF32(payload, snapshot.color[2]);
    AppendF32(payload, snapshot.intensity);
    AppendF32(payload, snapshot.exposure);
    AppendF32(payload, snapshot.diffuse);
    AppendF32(payload, snapshot.specular);
    AppendU32(payload, 0);
    for (double value : snapshot.transform)
    {
        AppendF64(payload, value);
    }
    AppendBytes(payload, path.data(), path.size());
    AppendBytes(
        payload,
        snapshot.textureAsset.data(),
        snapshot.textureAsset.size());

    AppendCommand(buffer, OPENUSD_SILK_COMMAND_ENVIRONMENT_UPSERT, payload);
}

void AppendEnvironmentRemove(std::vector<uint8_t>& buffer, const std::string& path)
{
    ValidatePath(path);
    const uint32_t pathByteCount = CheckedCount(path.size(), "path byte count");
    std::vector<uint8_t> payload;
    payload.reserve(8 + 4 + pathByteCount);
    AppendU64(payload, ComputeStableHash(path));
    AppendU32(payload, pathByteCount);
    AppendBytes(payload, path.data(), path.size());

    AppendCommand(buffer, OPENUSD_SILK_COMMAND_ENVIRONMENT_REMOVE, payload);
}

void AppendLightLink(std::vector<uint8_t>& buffer, const HdSilkLinkTable& table)
{
    std::vector<uint8_t> payload;
    payload.reserve(16 + (table.entries.size() * 28));
    AppendU32(payload, CheckedCount(table.entries.size(), "light link entry count"));
    AppendU32(payload, table.lightCount);
    AppendU32(payload, table.flags);
    AppendU32(payload, table.domeCount);
    for (const HdSilkLinkEntry& entry : table.entries)
    {
        ValidatePath(entry.path);
        AppendU32(payload, entry.lightMask);
        AppendU32(payload, entry.shadowMask);
        AppendU32(payload, entry.domeMask);
        AppendI32(payload, entry.instanceIndex);
        AppendU32(payload, CheckedCount(entry.path.size(), "path byte count"));
        AppendBytes(payload, entry.path.data(), entry.path.size());
    }

    AppendCommand(buffer, OPENUSD_SILK_COMMAND_LIGHT_LINK, payload);
}

void AppendShadow(std::vector<uint8_t>& buffer, const HdSilkShadowTable& table)
{
    std::vector<uint8_t> payload;
    payload.reserve(16 + (table.descriptors.size() * 288));
    AppendU32(payload, CheckedCount(table.descriptors.size(), "shadow descriptor count"));
    AppendU32(payload, table.lightCount);
    AppendU32(payload, table.flags);
    AppendU32(payload, 0);
    for (const HdSilkShadowDescriptor& descriptor : table.descriptors)
    {
        AppendU32(payload, descriptor.lightIndex);
        AppendU32(payload, descriptor.mapIndex);
        AppendU32(payload, descriptor.resolution);
        AppendU32(payload, descriptor.flags);
        for (double value : descriptor.view)
        {
            AppendF64(payload, value);
        }
        for (double value : descriptor.projection)
        {
            AppendF64(payload, value);
        }
        AppendF32(payload, descriptor.depthBias);
        AppendF32(payload, descriptor.normalBias);
        AppendF32(payload, descriptor.pcfRadius);
        AppendU32(payload, 0);
    }

    AppendCommand(buffer, OPENUSD_SILK_COMMAND_SHADOW, payload);
}

/// Builds the row-major, row-vector world-to-light view matrix of a directional
/// light that illuminates a bounding sphere.
///
/// "direction" points from the shaded surface toward the light, matching the
/// frame light table, so the light itself is placed along it and looks back.
/// The eye is pushed a full diameter outside the sphere so the near plane stays
/// positive for every caster, which an orthographic projection needs even though
/// it has no perspective divide.
void BuildDirectionalShadowView(
    const GfVec3d& direction,
    const GfVec3d& center,
    double radius,
    double (&outView)[16],
    double* outEyeDistance)
{
    GfVec3d zAxis = direction.GetNormalized();
    // A world up axis parallel to the light direction cannot produce a basis, so
    // the fallback is chosen by the same test rather than by luck.
    GfVec3d up = std::abs(zAxis[1]) > 0.99 ? GfVec3d(1.0, 0.0, 0.0) : GfVec3d(0.0, 1.0, 0.0);
    GfVec3d xAxis = GfCross(up, zAxis).GetNormalized();
    GfVec3d yAxis = GfCross(zAxis, xAxis).GetNormalized();
    const double eyeDistance = (2.0 * radius) + 1.0;
    const GfVec3d eye = center + (zAxis * eyeDistance);

    outView[0] = xAxis[0];
    outView[1] = yAxis[0];
    outView[2] = zAxis[0];
    outView[3] = 0.0;
    outView[4] = xAxis[1];
    outView[5] = yAxis[1];
    outView[6] = zAxis[1];
    outView[7] = 0.0;
    outView[8] = xAxis[2];
    outView[9] = yAxis[2];
    outView[10] = zAxis[2];
    outView[11] = 0.0;
    outView[12] = -GfDot(eye, xAxis);
    outView[13] = -GfDot(eye, yAxis);
    outView[14] = -GfDot(eye, zAxis);
    outView[15] = 1.0;
    *outEyeDistance = eyeDistance;
}

/// Builds the row-major, row-vector orthographic projection covering a bounding
/// sphere seen from BuildDirectionalShadowView's eye, in the same OpenGL
/// [-w, +w] clip-depth convention the FRAME camera projection uses.
void BuildOrthographicShadowProjection(
    double radius,
    double eyeDistance,
    double (&outProjection)[16])
{
    const double nearPlane = eyeDistance - radius;
    const double farPlane = eyeDistance + radius;
    for (int index = 0; index < 16; ++index)
    {
        outProjection[index] = 0.0;
    }
    outProjection[0] = 1.0 / radius;
    outProjection[5] = 1.0 / radius;
    outProjection[10] = -2.0 / (farPlane - nearPlane);
    outProjection[14] = -(farPlane + nearPlane) / (farPlane - nearPlane);
    outProjection[15] = 1.0;
}

/// Resolves the masks one prim's categories produce against the ordered direct
/// light table and the bounded dome table of the page being built. A light with
/// an empty link identity links to everything, which is what UsdImaging reserves
/// the empty identity for, so its bit is always set.
void ResolveLinkMasks(
    const std::vector<HdSilkLightRecord>& directLights,
    const std::vector<HdSilkFrameDome>& domes,
    const std::vector<std::string>& categories,
    uint32_t* outLightMask,
    uint32_t* outShadowMask,
    uint32_t* outDomeMask)
{
    uint32_t lightMask = 0;
    uint32_t shadowMask = 0;
    uint32_t domeMask = 0;
    for (size_t index = 0; index < directLights.size(); ++index)
    {
        const uint32_t bit = 1u << index;
        const HdSilkLightRecord& light = directLights[index];
        if (light.lightLinkCategory.empty() ||
            std::find(
                categories.begin(),
                categories.end(),
                light.lightLinkCategory) != categories.end())
        {
            lightMask |= bit;
        }

        // Resolved independently of the light mask on purpose. UsdLux defines
        // collection:lightLink and collection:shadowLink as two separate
        // collections over the same light: the first decides which prims the
        // light illuminates, the second decides which prims cast its shadow.
        // A prim can legitimately be in the caster collection and out of the
        // lit collection -- an off-screen or unlit blocker that must still
        // occlude other receivers is exactly that case -- so intersecting the
        // two here would silently delete its shadow.
        if (light.shadowLinkCategory.empty() ||
            std::find(
                categories.begin(),
                categories.end(),
                light.shadowLinkCategory) != categories.end())
        {
            shadowMask |= bit;
        }
    }

    // The dome bit space is its own ordering, resolved by exactly the same rule
    // as the direct one: a dome whose collection identity is empty includes the
    // root with nothing excluded and therefore lights every prim. There is no
    // dome shadow mask, because hdSilk renders no dome shadow map; a dome's
    // collection:shadowLink is diagnosed against the dome instead.
    for (size_t index = 0; index < domes.size(); ++index)
    {
        const HdSilkFrameDome& dome = domes[index];
        if (dome.lightLinkCategory.empty() ||
            std::find(
                categories.begin(),
                categories.end(),
                dome.lightLinkCategory) != categories.end())
        {
            domeMask |= 1u << index;
        }
    }
    *outLightMask = lightMask;
    *outShadowMask = shadowMask;
    *outDomeMask = domeMask;
}
std::atomic<uint64_t> _rejectedMeshCount{0};
std::atomic<uint64_t> _rejectedMaterialCount{0};
std::atomic<uint64_t> _truncatedLinkCount{0};
}

bool
HdSilkAppendInstanceMemberships(
    const std::string& primPath,
    const std::vector<std::string>& primCategories,
    const std::vector<std::vector<std::string>>& instanceCategories,
    const std::vector<int>& publishedIndices,
    size_t rowLimit,
    std::vector<HdSilkCategoryMembership>* outMemberships)
{
    // One instancer with no parent is the depth-one case of the general nested
    // resolution, so it is resolved by the same code rather than by a second
    // implementation that could drift from it. With no ancestor level there is
    // nothing to compose against and nothing to merge in, so every identity is
    // the instancer's own index and every instance's own categories replace the
    // prototype's exactly as before.
    HdSilkInstancerLevel level;
    level.publishedIndices = publishedIndices;
    level.instanceCategories = instanceCategories;
    return HdSilkAppendNestedInstanceMemberships(
        primPath,
        primCategories,
        {},
        {std::move(level)},
        rowLimit,
        outMemberships,
        nullptr);
}

uint64_t
HdSilkSceneState::GetRejectedMeshCount()
{
    return _rejectedMeshCount.load(std::memory_order_relaxed);
}

uint64_t
HdSilkSceneState::GetRejectedMaterialCount()
{
    return _rejectedMaterialCount.load(std::memory_order_relaxed);
}

uint64_t
HdSilkSceneState::GetTruncatedLinkCount()
{
    return _truncatedLinkCount.load(std::memory_order_relaxed);
}

bool
HdSilkSceneState::HasLightLinks() const
{
    std::lock_guard<std::mutex> lock(_mutex);
    for (const auto& entry : _lights)
    {
        if (entry.second.ambientOnly)
        {
            // A dome's collection:lightLink resolves into the bounded dome mask
            // since ABI v21, so it is a reason to collect prim categories. Its
            // collection:shadowLink is not: no dome shadow map exists to
            // restrict, and it is diagnosed against the dome instead.
            if (!entry.second.lightLinkCategory.empty())
            {
                return true;
            }
            continue;
        }
        if (!entry.second.lightLinkCategory.empty() ||
            !entry.second.shadowLinkCategory.empty())
        {
            return true;
        }
    }
    return false;
}

void
HdSilkSceneState::SetCategoryMemberships(
    std::vector<HdSilkCategoryMembership> memberships,
    bool truncated)
{
    std::lock_guard<std::mutex> lock(_mutex);
    _categoryMemberships = std::move(memberships);
    _categoryMembershipsTruncated = truncated;
}

HdSilkLinkTable
HdSilkSceneState::_ResolveLinkTable(
    const std::vector<HdSilkLightRecord>& directLights,
    const std::vector<HdSilkFrameDome>& domes,
    bool domeBudgetExceeded) const
{
    HdSilkLinkTable table;
    table.lightCount = static_cast<uint32_t>(directLights.size());
    table.domeCount = static_cast<uint32_t>(domes.size());
    bool anyLinked = false;
    for (const HdSilkLightRecord& light : directLights)
    {
        if (!light.lightLinkCategory.empty() || !light.shadowLinkCategory.empty())
        {
            anyLinked = true;
            break;
        }
    }
    for (const HdSilkFrameDome& dome : domes)
    {
        if (!dome.lightLinkCategory.empty())
        {
            anyLinked = true;
            break;
        }
    }
    if (!anyLinked)
    {
        // Every light reaches every prim, so the default already describes the
        // scene and an entry for each prim would say nothing. The empty table is
        // canonical -- light count zero -- so that a scene which links nothing
        // never publishes a command, however its light set moves. The dome
        // budget still has to be reported, because a scene that over-ran it lost
        // a capability whether or not it authored a collection today.
        HdSilkLinkTable empty;
        if (domeBudgetExceeded)
        {
            empty.flags |= OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_DOME_BUDGET;
        }
        return empty;
    }

    const uint32_t defaultMask = directLights.size() >= 32
        ? 0xFFFFFFFFu
        : ((1u << directLights.size()) - 1u);
    const uint32_t defaultDomeMask = domes.size() >= 32
        ? 0xFFFFFFFFu
        : ((1u << domes.size()) - 1u);
    size_t droppedEntries = 0;

    // The bound is applied to the table this resolution produces, not to the
    // memberships it reads. A prim that links to every light produces no entry
    // at all, so charging it against the budget before its masks are known --
    // which is what a bound on the collected rows does -- let a scene of prims
    // that link to everything crowd out the one prim that does not, and
    // reported a truncated table for a page that fits in a single entry.
    //
    // A path is also admitted or refused whole. The path-wide row is what a
    // consumer falls back to for every instance it has no row for, so a
    // restrictive path row published without the overrides that widen it would
    // be applied to exactly the instances the author opted back in. A group
    // that does not fit is dropped entirely and the path keeps the default of
    // being linked to every light, which is the documented behaviour of an
    // omitted entry: truncation fails open, never closed.
    struct PathGroup
    {
        const HdSilkCategoryMembership* pathRow = nullptr;
        std::vector<const HdSilkCategoryMembership*> instanceRows;
    };
    std::vector<std::pair<const std::string*, PathGroup>> groups;
    std::unordered_map<std::string, size_t> groupIndices;
    for (const HdSilkCategoryMembership& membership : _categoryMemberships)
    {
        const auto existing = groupIndices.find(membership.path);
        size_t index = 0;
        if (existing == groupIndices.end())
        {
            index = groups.size();
            groupIndices.emplace(membership.path, index);
            groups.emplace_back(&membership.path, PathGroup());
        }
        else
        {
            index = existing->second;
        }
        if (membership.instanceIndex == OPENUSD_SILK_LINK_ALL_INSTANCES)
        {
            groups[index].second.pathRow = &membership;
        }
        else
        {
            groups[index].second.instanceRows.push_back(&membership);
        }
    }

    for (const auto& group : groups)
    {
        const std::string& path = *group.first;
        const PathGroup& rows = group.second;

        // Path-wide masks resolve first, because an instance entry is only
        // worth publishing when it differs from what its path already resolves
        // to. Comparing an instance against the global default instead would
        // omit an instance that opts back into every light under a path that
        // opts out of one, and the consumer would fall back to the path's
        // narrower mask.
        uint32_t pathLight = defaultMask;
        uint32_t pathShadow = defaultMask;
        uint32_t pathDome = defaultDomeMask;
        if (rows.pathRow != nullptr)
        {
            ResolveLinkMasks(
                directLights,
                domes,
                rows.pathRow->categories,
                &pathLight,
                &pathShadow,
                &pathDome);
        }

        std::vector<HdSilkLinkEntry> candidates;
        candidates.reserve(rows.instanceRows.size() + 1);
        if (pathLight != defaultMask ||
            pathShadow != defaultMask ||
            pathDome != defaultDomeMask)
        {
            HdSilkLinkEntry entry;
            entry.path = path;
            entry.instanceIndex = OPENUSD_SILK_LINK_ALL_INSTANCES;
            entry.lightMask = pathLight;
            entry.shadowMask = pathShadow;
            entry.domeMask = pathDome;
            candidates.push_back(std::move(entry));
        }
        for (const HdSilkCategoryMembership* instanceRow : rows.instanceRows)
        {
            uint32_t lightMask = 0;
            uint32_t shadowMask = 0;
            uint32_t domeMask = 0;
            ResolveLinkMasks(
                directLights,
                domes,
                instanceRow->categories,
                &lightMask,
                &shadowMask,
                &domeMask);
            if (lightMask == pathLight &&
                shadowMask == pathShadow &&
                domeMask == pathDome)
            {
                continue;
            }
            HdSilkLinkEntry entry;
            entry.path = path;
            entry.instanceIndex = instanceRow->instanceIndex;
            entry.lightMask = lightMask;
            entry.shadowMask = shadowMask;
            entry.domeMask = domeMask;
            candidates.push_back(std::move(entry));
        }

        if (candidates.empty())
        {
            // Every instance of this path resolves to the default, so the path
            // costs the table nothing at all.
            continue;
        }
        if (candidates.size() >
                static_cast<size_t>(OPENUSD_SILK_MAX_LINK_ENTRIES) ||
            table.entries.size() >
                static_cast<size_t>(OPENUSD_SILK_MAX_LINK_ENTRIES) -
                    candidates.size())
        {
            droppedEntries += candidates.size();
            continue;
        }
        table.entries.insert(
            table.entries.end(),
            std::make_move_iterator(candidates.begin()),
            std::make_move_iterator(candidates.end()));
    }
    if (droppedEntries > 0)
    {
        table.flags |= OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_TRUNCATED;
        _truncatedLinkCount.fetch_add(droppedEntries, std::memory_order_relaxed);
        TF_WARN(
            "hdSilk dropped %zu light link entries; the page ABI limit is %u. "
            "The dropped prims stay linked to every light.",
            droppedEntries,
            OPENUSD_SILK_MAX_LINK_ENTRIES);
    }
    if (_categoryMembershipsTruncated)
    {
        // The collecting frame stopped before it had walked the whole render
        // index, so prims it never reached are absent from the table for the
        // same reason a dropped entry is and must be reported the same way.
        table.flags |= OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_TRUNCATED;
    }
    if (domeBudgetExceeded)
    {
        table.flags |= OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_DOME_BUDGET;
    }

    std::sort(
        table.entries.begin(),
        table.entries.end(),
        [](const HdSilkLinkEntry& left, const HdSilkLinkEntry& right)
        {
            if (left.path != right.path)
            {
                return Utf8PathLess(left.path, right.path);
            }
            return left.instanceIndex < right.instanceIndex;
        });
    if (table.entries.empty() &&
        table.flags == OPENUSD_SILK_LIGHT_LINK_UNSUPPORTED_NONE)
    {
        // A table with no entries says nothing about the light ordering, so it
        // is canonicalized: otherwise a scene that links nothing would publish a
        // new command every time its light count moved, and the very first page
        // of every scene would publish an empty table that says exactly what a
        // consumer already assumes.
        table.lightCount = 0;
        table.domeCount = 0;
    }
    return table;
}

HdSilkWorldBounds
HdSilkSceneState::_ResolveCasterBounds() const
{
    // An instance record reuses the payload record's geometry and carries no
    // points of its own, so its extent is the payload record's object-space box
    // under the instance's own transform. Resolving the payload box per path
    // once keeps the walk proportional to the number of published records rather
    // than to the number of points in the scene.
    std::unordered_map<std::string, const _Entry*> payloadEntries;
    for (const auto& entry : _meshes)
    {
        if (!entry.second.hasLocalBounds)
        {
            continue;
        }
        const auto existing = payloadEntries.find(entry.first.path);
        if (existing == payloadEntries.end() ||
            entry.second.record.instanceIndex <
                existing->second->record.instanceIndex)
        {
            payloadEntries[entry.first.path] = &entry.second;
        }
    }

    HdSilkWorldBounds bounds;
    for (const auto& entry : _meshes)
    {
        const _Entry* source = &entry.second;
        if (!source->hasLocalBounds)
        {
            const auto payload = payloadEntries.find(entry.first.path);
            if (payload == payloadEntries.end())
            {
                continue;
            }
            source = payload->second;
        }

        const double* transform = entry.second.record.transform;
        for (int corner = 0; corner < 8; ++corner)
        {
            const double local[3] = {
                (corner & 1) != 0 ? source->localMaximum[0] : source->localMinimum[0],
                (corner & 2) != 0 ? source->localMaximum[1] : source->localMinimum[1],
                (corner & 4) != 0 ? source->localMaximum[2] : source->localMinimum[2]};
            double world[3];
            bool finite = true;
            for (int column = 0; column < 3; ++column)
            {
                world[column] =
                    (local[0] * transform[column]) +
                    (local[1] * transform[4 + column]) +
                    (local[2] * transform[8 + column]) +
                    transform[12 + column];
                finite = finite && std::isfinite(world[column]);
            }
            if (!finite)
            {
                continue;
            }
            if (!bounds.valid)
            {
                bounds.valid = true;
                for (int axis = 0; axis < 3; ++axis)
                {
                    bounds.minimum[axis] = world[axis];
                    bounds.maximum[axis] = world[axis];
                }
                continue;
            }
            for (int axis = 0; axis < 3; ++axis)
            {
                bounds.minimum[axis] = std::min(bounds.minimum[axis], world[axis]);
                bounds.maximum[axis] = std::max(bounds.maximum[axis], world[axis]);
            }
        }
    }
    return bounds;
}

HdSilkShadowTable
HdSilkSceneState::_ResolveShadowTable(
    const std::vector<HdSilkLightRecord>& directLights,
    const HdSilkLinkTable& links) const
{
    HdSilkShadowTable table;
    bool anyShadowEnabled = false;
    for (const HdSilkLightRecord& light : directLights)
    {
        if (light.shadowEnabled != 0u)
        {
            anyShadowEnabled = true;
            break;
        }
    }
    if (!anyShadowEnabled)
    {
        // A scene that authors no shadow at all publishes the canonical empty
        // table, so a consumer allocates nothing and the command disappears the
        // moment the last shadow-enabled light does.
        return table;
    }

    HdSilkWorldBounds bounds = _ResolveCasterBounds();
    double radius = 0.0;
    GfVec3d center(0.0, 0.0, 0.0);
    if (bounds.valid)
    {
        double extent = 0.0;
        for (int axis = 0; axis < 3; ++axis)
        {
            center[axis] = (bounds.minimum[axis] + bounds.maximum[axis]) * 0.5;
            const double half = (bounds.maximum[axis] - bounds.minimum[axis]) * 0.5;
            extent += half * half;
        }
        radius = std::sqrt(extent);
    }
    // A scene whose published geometry is a single point, a single line, or
    // nothing has no extent to project. Padding it to an arbitrary size would
    // silently choose a shadow frustum no author asked for.
    const bool hasCasters = bounds.valid && radius > 1e-6;

    uint32_t flags = OPENUSD_SILK_SHADOW_UNSUPPORTED_NONE;
    size_t budgetOverflow = 0;
    for (size_t index = 0; index < directLights.size(); ++index)
    {
        const HdSilkLightRecord& light = directLights[index];
        if (light.shadowEnabled == 0u)
        {
            continue;
        }
        if (light.type != OPENUSD_SILK_LIGHT_DISTANT)
        {
            // Only a distant light has an exact light-space projection here. A
            // sphere, rect, disk or cylinder light needs a cube or perspective
            // projection this producer has not derived, so it is named instead
            // of being approximated by a directional map that would shadow the
            // wrong geometry.
            flags |= OPENUSD_SILK_SHADOW_UNSUPPORTED_LIGHT_TYPE;
            continue;
        }
        if (!hasCasters)
        {
            flags |= OPENUSD_SILK_SHADOW_UNSUPPORTED_NO_CASTERS;
            continue;
        }
        if (table.descriptors.size() >= OPENUSD_SILK_MAX_SHADOW_MAPS)
        {
            ++budgetOverflow;
            continue;
        }

        GfVec3d direction(
            light.transform[8],
            light.transform[9],
            light.transform[10]);
        if (GfDot(direction, direction) <= 1e-12)
        {
            flags |= OPENUSD_SILK_SHADOW_UNSUPPORTED_NO_CASTERS;
            continue;
        }

        HdSilkShadowDescriptor descriptor;
        descriptor.lightIndex = static_cast<uint32_t>(index);
        descriptor.mapIndex =
            static_cast<uint32_t>(table.descriptors.size());
        descriptor.resolution = OPENUSD_SILK_DEFAULT_SHADOW_MAP_RESOLUTION;
        descriptor.flags = OPENUSD_SILK_SHADOW_FLAG_ORTHOGRAPHIC;
        double eyeDistance = 0.0;
        BuildDirectionalShadowView(
            direction,
            center,
            radius,
            descriptor.view,
            &eyeDistance);
        BuildOrthographicShadowProjection(
            radius,
            eyeDistance,
            descriptor.projection);

        const double texelWorldSize =
            (2.0 * radius) / static_cast<double>(descriptor.resolution);
        // The depth bias is expressed in the map's own normalized depth so a
        // consumer never has to know the world extent it was derived from; the
        // normal bias stays in world units because the receiver it offsets is.
        descriptor.depthBias =
            static_cast<float>(1.5 / static_cast<double>(descriptor.resolution));
        descriptor.normalBias = static_cast<float>(1.5 * texelWorldSize);
        descriptor.pcfRadius = 1.0f;

        const uint32_t bit = 1u << descriptor.lightIndex;
        for (const HdSilkLinkEntry& entry : links.entries)
        {
            if ((entry.shadowMask & bit) == 0u)
            {
                descriptor.flags |= OPENUSD_SILK_SHADOW_FLAG_CASTER_LINKED;
                break;
            }
        }
        table.descriptors.push_back(descriptor);
    }

    if (budgetOverflow > 0)
    {
        flags |= OPENUSD_SILK_SHADOW_UNSUPPORTED_MAP_BUDGET;
        TF_WARN(
            "hdSilk dropped %zu shadow map(s); the page ABI limit is %u. "
            "The dropped lights are published without occlusion.",
            budgetOverflow,
            OPENUSD_SILK_MAX_SHADOW_MAPS);
    }
    table.flags = flags;
    table.lightCount = table.descriptors.empty() && flags == 0u
        ? 0u
        : static_cast<uint32_t>(directLights.size());
    return table;
}

void
HdSilkSceneState::ReplaceMeshInstances(
    const std::string& path,
    std::vector<HdSilkMeshRecord> records)
{
    for (const HdSilkMeshRecord& record : records)
    {
        if (record.path != path)
        {
            throw std::invalid_argument(
                "The hdSilk scene-state key must match the mesh record path.");
        }
    }

    std::lock_guard<std::mutex> lock(_mutex);
    std::vector<int32_t>& published = _instancesByPath[path];
    std::vector<int32_t> retained;
    retained.reserve(records.size());

    for (HdSilkMeshRecord& record : records)
    {
        const int32_t instanceIndex = record.instanceIndex;
        const HdSilkMeshKey key{path, instanceIndex};
        _Entry& entry = _meshes[key];
        entry.record = std::move(record);
        entry.dirty = true;
        entry.hasLocalBounds = false;
        const std::vector<float>& points = entry.record.points;
        for (size_t offset = 0; offset + 2 < points.size(); offset += 3)
        {
            const double x = static_cast<double>(points[offset]);
            const double y = static_cast<double>(points[offset + 1]);
            const double z = static_cast<double>(points[offset + 2]);
            if (!std::isfinite(x) || !std::isfinite(y) || !std::isfinite(z))
            {
                // A non-finite point is rejected by wire validation later; it
                // must not poison the caster bounds a shadow projection is
                // derived from before that rejection happens.
                continue;
            }
            if (!entry.hasLocalBounds)
            {
                entry.hasLocalBounds = true;
                entry.localMinimum[0] = x;
                entry.localMinimum[1] = y;
                entry.localMinimum[2] = z;
                entry.localMaximum[0] = x;
                entry.localMaximum[1] = y;
                entry.localMaximum[2] = z;
                continue;
            }
            entry.localMinimum[0] = std::min(entry.localMinimum[0], x);
            entry.localMinimum[1] = std::min(entry.localMinimum[1], y);
            entry.localMinimum[2] = std::min(entry.localMinimum[2], z);
            entry.localMaximum[0] = std::max(entry.localMaximum[0], x);
            entry.localMaximum[1] = std::max(entry.localMaximum[1], y);
            entry.localMaximum[2] = std::max(entry.localMaximum[2], z);
        }
        retained.push_back(instanceIndex);

        // Drop a queued removal for an identity that is alive again, so a
        // rapid destroy/recreate cannot erase the replacement record.
        _pendingRemovals.erase(
            std::remove(_pendingRemovals.begin(), _pendingRemovals.end(), key),
            _pendingRemovals.end());
    }

    // Instances that existed before this sync but are absent now must be
    // retired explicitly; otherwise a shrinking instancer leaves stale
    // geometry in every consumer's retained scene.
    for (int32_t previousIndex : published)
    {
        if (std::find(retained.begin(), retained.end(), previousIndex) !=
            retained.end())
        {
            continue;
        }
        const HdSilkMeshKey staleKey{path, previousIndex};
        if (_meshes.erase(staleKey) != 0)
        {
            _pendingRemovals.push_back(staleKey);
        }
    }

    if (retained.empty())
    {
        _instancesByPath.erase(path);
    }
    else
    {
        published = std::move(retained);
    }
}

void
HdSilkSceneState::RemoveMesh(const std::string& path)
{
    std::lock_guard<std::mutex> lock(_mutex);
    const auto published = _instancesByPath.find(path);
    if (published == _instancesByPath.end())
    {
        return;
    }
    for (int32_t instanceIndex : published->second)
    {
        const HdSilkMeshKey key{path, instanceIndex};
        if (_meshes.erase(key) != 0)
        {
            _pendingRemovals.push_back(key);
        }
    }
    _instancesByPath.erase(published);
}

void
HdSilkSceneState::SetFrame(const HdSilkFrameState& frame)
{
    std::lock_guard<std::mutex> lock(_mutex);
    _frame = frame;
}

void
HdSilkSceneState::SetComplexity(uint32_t complexity)
{
    if (complexity > OPENUSD_SILK_COMPLEXITY_VERY_HIGH)
    {
        throw std::invalid_argument("An hdSilk complexity level is unknown.");
    }
    std::lock_guard<std::mutex> lock(_mutex);
    if (_complexity == complexity)
    {
        return;
    }
    _complexity = complexity;
    // Complexity subdivides line and point topology. A triangle-list record
    // becomes one of those on the way to the wire whenever the current draw
    // mode converts it, so it has to be republished too: otherwise changing
    // complexity while wireframe or points draw mode is active leaves every
    // converted record at the previous density.
    const bool drawModeConverts =
        _drawMode == OPENUSD_SILK_DRAW_MODE_WIREFRAME ||
        _drawMode == OPENUSD_SILK_DRAW_MODE_HIDDEN_SURFACE_WIREFRAME ||
        _drawMode == OPENUSD_SILK_DRAW_MODE_POINTS;
    for (auto& entry : _meshes)
    {
        const uint32_t kind = entry.second.record.topologyKind;
        if (kind == OPENUSD_SILK_TOPOLOGY_LINE_LIST ||
            kind == OPENUSD_SILK_TOPOLOGY_POINT_LIST ||
            (drawModeConverts && kind == OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST))
        {
            entry.second.dirty = true;
        }
    }
}

void
HdSilkSceneState::SetDrawMode(uint32_t drawMode)
{
    if (drawMode > OPENUSD_SILK_DRAW_MODE_HIDDEN_SURFACE_WIREFRAME)
    {
        throw std::invalid_argument("An hdSilk draw mode is unknown.");
    }
    std::lock_guard<std::mutex> lock(_mutex);
    if (_drawMode == drawMode)
    {
        return;
    }
    _drawMode = drawMode;
    for (auto& entry : _meshes)
    {
        entry.second.dirty = true;
    }
}

void
HdSilkSceneState::ReplaceMaterial(HdSilkMaterialRecord record)
{
    if (record.path.empty())
    {
        throw std::invalid_argument("An hdSilk material record requires a path.");
    }
    std::lock_guard<std::mutex> lock(_mutex);
    const std::string path = record.path;
    _MaterialEntry& entry = _materials[path];
    entry.record = std::move(record);
    entry.dirty = true;
    // A queued removal for a material that is alive again must be dropped, so a
    // rapid destroy/recreate cannot erase the replacement.
    _pendingMaterialRemovals.erase(
        std::remove(
            _pendingMaterialRemovals.begin(),
            _pendingMaterialRemovals.end(),
            path),
        _pendingMaterialRemovals.end());
}

void
HdSilkSceneState::RemoveMaterial(const std::string& path)
{
    std::lock_guard<std::mutex> lock(_mutex);
    if (_materials.erase(path) != 0)
    {
        _pendingMaterialRemovals.push_back(path);
    }
}

HdSilkEnvironmentSnapshot
HdSilkMakeEnvironmentSnapshot(const HdSilkLightRecord& record, uint32_t domeIndex)
{
    HdSilkEnvironmentSnapshot snapshot;
    snapshot.textureAsset = record.textureAsset;
    snapshot.textureFormat = record.textureFormat;
    snapshot.sourceColorSpace = record.sourceColorSpace;
    snapshot.unsupportedFeatures = record.unsupportedFeatures;
    snapshot.domeIndex = domeIndex;
    if (domeIndex == OPENUSD_SILK_DOME_INDEX_NONE &&
        !record.lightLinkCategory.empty())
    {
        // The dome authors a receiver collection and the page publishes no dome
        // table to carry its bit, so it lights every prim. Named here rather
        // than dropped: a collection that changes nothing is exactly the kind of
        // silence this profile avoids.
        snapshot.unsupportedFeatures |=
            OPENUSD_SILK_ENVIRONMENT_UNSUPPORTED_LINK_COLLECTION;
    }
    snapshot.color[0] = record.color[0];
    snapshot.color[1] = record.color[1];
    snapshot.color[2] = record.color[2];
    snapshot.intensity = record.intensity;
    snapshot.exposure = record.exposure;
    snapshot.diffuse = record.diffuse;
    snapshot.specular = record.specular;
    for (int index = 0; index < 16; ++index)
    {
        snapshot.transform[index] = record.transform[index];
    }
    return snapshot;
}

void
HdSilkSceneState::ReplaceLight(HdSilkLightRecord record)
{
    if (record.path.empty())
    {
        throw std::invalid_argument("An hdSilk light record requires a path.");
    }
    std::lock_guard<std::mutex> lock(_mutex);
    const std::string path = record.path;
    _lights[path] = std::move(record);
}

void
HdSilkSceneState::RemoveLight(const std::string& path)
{
    std::lock_guard<std::mutex> lock(_mutex);
    _lights.erase(path);
}

std::vector<uint8_t>
HdSilkSceneState::BuildPage(uint64_t* outRevision, uint32_t* outCommandCount)
{
    std::lock_guard<std::mutex> lock(_mutex);
    if (_revision == std::numeric_limits<uint64_t>::max())
    {
        throw std::overflow_error("The hdSilk page revision is exhausted.");
    }

    std::vector<uint8_t> buffer;
    std::vector<_Entry*> dirtyEntries;
    dirtyEntries.reserve(_meshes.size());
    for (auto& entry : _meshes)
    {
        if (entry.second.dirty)
        {
            dirtyEntries.push_back(&entry.second);
        }
    }
    std::sort(
        dirtyEntries.begin(),
        dirtyEntries.end(),
        [](_Entry* left, _Entry* right)
        {
            if (left->record.path != right->record.path)
            {
                return Utf8PathLess(left->record.path, right->record.path);
            }
            return left->record.instanceIndex < right->record.instanceIndex;
        });

    std::vector<HdSilkMeshKey> removals = _pendingRemovals;
    std::sort(
        removals.begin(),
        removals.end(),
        [](const HdSilkMeshKey& left, const HdSilkMeshKey& right)
        {
            if (left.path != right.path)
            {
                return Utf8PathLess(left.path, right.path);
            }
            return left.instanceIndex < right.instanceIndex;
        });
    removals.erase(
        std::unique(removals.begin(), removals.end()),
        removals.end());

    std::vector<HdSilkLightRecord> lights;
    lights.reserve(_lights.size());
    for (const auto& entry : _lights)
    {
        lights.push_back(entry.second);
    }
    std::sort(
        lights.begin(),
        lights.end(),
        [](const HdSilkLightRecord& left, const HdSilkLightRecord& right)
        {
            return Utf8PathLess(left.path, right.path);
        });

    std::vector<HdSilkLightRecord> directLights;
    std::vector<HdSilkFrameDome> domes;
    bool domeBudgetExceeded = false;
    float ambientColor[3] = {0.0f, 0.0f, 0.0f};
    float ambientIntensity = 0.0f;
    SelectDirectLights(
        lights,
        &directLights,
        &domes,
        &domeBudgetExceeded,
        ambientColor,
        &ambientIntensity);

    AppendFrame(buffer, _frame, directLights, domes, ambientColor, ambientIntensity);
    size_t appendedCommands = 1;

    // The link table follows the frame it indexes and precedes every command
    // that names a prim, so a consumer applying the page in order knows which
    // lights reach a surface before that surface arrives.
    HdSilkLinkTable links = _ResolveLinkTable(directLights, domes, domeBudgetExceeded);
    HdSilkShadowTable shadows = _ResolveShadowTable(directLights, links);
    if (links != _publishedLinks)
    {
        const size_t bufferSize = buffer.size();
        try
        {
            AppendLightLink(buffer, links);
            ++appendedCommands;
            _publishedLinks = std::move(links);
        }
        catch (const std::exception& error)
        {
            // A rejected table leaves the previously published linking in place
            // rather than half replacing it, and is not recorded as published,
            // so the next page retries it.
            buffer.resize(bufferSize);
            TF_WARN("hdSilk skipped the light link table: %s", error.what());
        }
    }

    // The shadow table follows the link table it depends on: a descriptor's
    // caster-linked flag is resolved from the shadow masks that table carries.
    // An unchanged table publishes nothing, which is exactly how a consumer
    // knows its retained shadow maps are still the ones these lights and these
    // caster bounds produced.
    if (shadows != _publishedShadows)
    {
        const size_t bufferSize = buffer.size();
        try
        {
            AppendShadow(buffer, shadows);
            ++appendedCommands;
            _publishedShadows = std::move(shadows);
        }
        catch (const std::exception& error)
        {
            buffer.resize(bufferSize);
            TF_WARN("hdSilk skipped the shadow table: %s", error.what());
        }
    }


    // Environments follow the frame and precede materials and meshes. A textured
    // dome is scene-wide state the frame ambient term deliberately no longer
    // carries, so a consumer that applies the page in order has resolved it
    // before the first surface that is lit by it arrives.
    std::unordered_map<std::string, HdSilkEnvironmentSnapshot> environments;
    for (const HdSilkLightRecord& light : lights)
    {
        if (light.ambientOnly && !light.textureAsset.empty())
        {
            // The dome's entry in the bounded dome table, resolved by the same
            // path-sorted ordering the FRAME command published, so the index a
            // consumer reads here is the bit a LIGHT_LINK dome_mask sets.
            uint32_t domeIndex = OPENUSD_SILK_DOME_INDEX_NONE;
            for (size_t index = 0; index < domes.size(); ++index)
            {
                if (domes[index].path == light.path)
                {
                    domeIndex = static_cast<uint32_t>(index);
                    break;
                }
            }
            environments.emplace(
                light.path,
                HdSilkMakeEnvironmentSnapshot(light, domeIndex));
        }
    }

    std::vector<const std::string*> environmentUpserts;
    for (const auto& entry : environments)
    {
        const auto published = _publishedEnvironments.find(entry.first);
        if (published == _publishedEnvironments.end() ||
            published->second != entry.second)
        {
            environmentUpserts.push_back(&entry.first);
        }
    }
    std::sort(
        environmentUpserts.begin(),
        environmentUpserts.end(),
        [](const std::string* left, const std::string* right)
        {
            return Utf8PathLess(*left, *right);
        });

    std::vector<std::string> environmentRemovals;
    for (const auto& entry : _publishedEnvironments)
    {
        if (environments.find(entry.first) == environments.end())
        {
            environmentRemovals.push_back(entry.first);
        }
    }
    std::sort(
        environmentRemovals.begin(),
        environmentRemovals.end(),
        Utf8PathLess);

    for (const std::string* path : environmentUpserts)
    {
        const size_t bufferSize = buffer.size();
        try
        {
            AppendEnvironmentUpsert(buffer, *path, environments.at(*path));
            ++appendedCommands;
            _publishedEnvironments[*path] = environments.at(*path);
        }
        catch (const std::exception& error)
        {
            // A rejected environment leaves the dome unpublished rather than
            // half published, and is not recorded as published, so the next page
            // retries it once the authored value is valid again.
            buffer.resize(bufferSize);
            TF_WARN(
                "hdSilk skipped dome environment '%s': %s",
                path->c_str(),
                error.what());
        }
    }

    for (const std::string& path : environmentRemovals)
    {
        const size_t bufferSize = buffer.size();
        try
        {
            AppendEnvironmentRemove(buffer, path);
            ++appendedCommands;
            _publishedEnvironments.erase(path);
        }
        catch (const std::exception& error)
        {
            buffer.resize(bufferSize);
            TF_WARN(
                "hdSilk skipped removal of dome environment '%s': %s",
                path.c_str(),
                error.what());
        }
    }

    // Materials precede meshes so a consumer that applies the page in order
    // always has the material available when the mesh that binds it arrives.
    std::vector<_MaterialEntry*> dirtyMaterials;
    dirtyMaterials.reserve(_materials.size());
    for (auto& entry : _materials)
    {
        if (entry.second.dirty)
        {
            dirtyMaterials.push_back(&entry.second);
        }
    }
    std::sort(
        dirtyMaterials.begin(),
        dirtyMaterials.end(),
        [](_MaterialEntry* left, _MaterialEntry* right)
        {
            return Utf8PathLess(left->record.path, right->record.path);
        });

    std::vector<std::string> materialRemovals = _pendingMaterialRemovals;
    std::sort(materialRemovals.begin(), materialRemovals.end(), Utf8PathLess);
    materialRemovals.erase(
        std::unique(materialRemovals.begin(), materialRemovals.end()),
        materialRemovals.end());

    for (const _MaterialEntry* entry : dirtyMaterials)
    {
        const size_t bufferSize = buffer.size();
        try
        {
            AppendMaterialUpsert(buffer, entry->record);
            ++appendedCommands;
        }
        catch (const std::exception& error)
        {
            buffer.resize(bufferSize);
            _rejectedMaterialCount.fetch_add(1, std::memory_order_relaxed);
            TF_WARN(
                "hdSilk skipped material '%s': %s",
                entry->record.path.c_str(),
                error.what());
        }
    }

    for (const std::string& path : materialRemovals)
    {
        const size_t bufferSize = buffer.size();
        try
        {
            AppendMaterialRemove(buffer, path);
            ++appendedCommands;
        }
        catch (const std::exception& error)
        {
            buffer.resize(bufferSize);
            TF_WARN(
                "hdSilk skipped removal of material '%s': %s",
                path.c_str(),
                error.what());
        }
    }

    // Records are grouped by path and serialized atomically per path. ABI v8
    // makes the records of one path interdependent: the lowest instance index
    // carries the payload every later record of that path reuses, so appending
    // a later record after the payload record was rejected would publish an
    // instance reference that no page can ever resolve. A path that cannot be
    // serialized whole is therefore dropped whole, and every consumer keeps
    // whatever it already retained for that path rather than a torn version of
    // it. Paths are independent, so one malformed prim still cannot blank the
    // scene: the rest of the page serializes normally.
    for (size_t start = 0; start < dirtyEntries.size();)
    {
        size_t end = start + 1;
        while (end < dirtyEntries.size() &&
            dirtyEntries[end]->record.path == dirtyEntries[start]->record.path)
        {
            ++end;
        }

        const size_t pathBufferSize = buffer.size();
        const size_t pathCommandCount = appendedCommands;
        for (size_t index = start; index < end; ++index)
        {
            const _Entry* entry = dirtyEntries[index];
            try
            {
                // The record is validated as published, before ApplyDrawMode
                // and ApplyComplexity index into its points and attributes.
                // Validating only the transformed record would let a malformed
                // index read out of bounds on the way to being rejected.
                ValidateMeshShape(entry->record);
                HdSilkMeshRecord modeRecord =
                    ApplyDrawMode(entry->record, _drawMode);
                AppendMeshUpsert(
                    buffer,
                    std::move(modeRecord),
                    _complexity,
                    entry->record.topologyKind);
                ++appendedCommands;
            }
            catch (const std::exception& error)
            {
                buffer.resize(pathBufferSize);
                appendedCommands = pathCommandCount;
                _rejectedMeshCount.fetch_add(
                    end - start, std::memory_order_relaxed);
                TF_WARN(
                    "hdSilk skipped mesh '%s' and its %zu published instance(s): instance %d is invalid: %s",
                    entry->record.path.c_str(),
                    end - start,
                    static_cast<int>(entry->record.instanceIndex),
                    error.what());
                break;
            }
        }
        start = end;
    }

    for (const HdSilkMeshKey& key : removals)
    {
        const size_t bufferSize = buffer.size();
        try
        {
            AppendMeshRemove(buffer, key);
            ++appendedCommands;
        }
        catch (const std::exception& error)
        {
            buffer.resize(bufferSize);
            TF_WARN(
                "hdSilk skipped removal of '%s': %s",
                key.path.c_str(),
                error.what());
        }
    }

    const uint32_t commandCount =
        CheckedCount(appendedCommands, "page command count");

    for (_Entry* entry : dirtyEntries)
    {
        entry->dirty = false;
    }
    _pendingRemovals.clear();
    for (_MaterialEntry* entry : dirtyMaterials)
    {
        entry->dirty = false;
    }
    _pendingMaterialRemovals.clear();

    ++_revision;
    if (outRevision != nullptr)
    {
        *outRevision = _revision;
    }
    if (outCommandCount != nullptr)
    {
        *outCommandCount = commandCount;
    }

    return buffer;
}

PXR_NAMESPACE_CLOSE_SCOPE
