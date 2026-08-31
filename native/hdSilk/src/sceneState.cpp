// Copyright (c) marcschier. Licensed under the MIT License.

#include "sceneState.h"

#include "openusd_hdsilk.h"

#include "pxr/base/tf/diagnostic.h"

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
// count and attribute count.
constexpr size_t MeshFixedPayloadSize = 216;

// semantic, component_count, interpolation, name_byte_count, element_count.
constexpr size_t MeshAttributeFixedSize = 20;

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

    if (drawMode == OPENUSD_SILK_DRAW_MODE_POINTS)
    {
        result.topologyKind = OPENUSD_SILK_TOPOLOGY_POINT_LIST;
        result.indices.clear();
        result.indices.reserve(pointCount);
        result.triangleSubprims.clear();
        result.triangleSubprims.reserve(pointCount);
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
    return result;
}

void AppendFrame(
    std::vector<uint8_t>& buffer,
    const HdSilkFrameState& frame,
    const std::vector<HdSilkLightRecord>& lights)
{
    std::vector<uint8_t> payload;
    payload.reserve(
        16 +
        sizeof(frame.viewMatrix) +
        sizeof(frame.projectionMatrix) +
        sizeof(frame.clipPlanes) +
        16 +
        (static_cast<size_t>(OPENUSD_SILK_MAX_FRAME_LIGHTS) * 176) +
        16);
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

    std::vector<HdSilkLightRecord> directLights;
    size_t directLightCount = 0;
    float ambientColor[3] = {0.0f, 0.0f, 0.0f};
    float ambientIntensity = 0.0f;
    for (const HdSilkLightRecord& light : lights)
    {
        if (light.ambientOnly)
        {
            // Storm's unit white dome resolves to 0.96 diffuse irradiance. Preserve that
            // normalization while still honoring authored color, intensity, exposure,
            // and diffuse contribution. Multiple domes accumulate instead of whichever
            // record happens to be visited last replacing every earlier one.
            const float exposed =
                0.96f * light.intensity * std::pow(2.0f, light.exposure) * light.diffuse;
            ambientColor[0] += light.color[0] * exposed;
            ambientColor[1] += light.color[1] * exposed;
            ambientColor[2] += light.color[2] * exposed;
            ambientIntensity = 1.0f;
            continue;
        }
        ++directLightCount;
        if (directLights.size() < OPENUSD_SILK_MAX_FRAME_LIGHTS)
        {
            directLights.push_back(light);
        }
    }
    if (directLightCount > OPENUSD_SILK_MAX_FRAME_LIGHTS)
    {
        TF_WARN(
            "hdSilk retained %zu of %zu direct lights; the page ABI limit is %u",
            directLights.size(),
            directLightCount,
            OPENUSD_SILK_MAX_FRAME_LIGHTS);
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
    AppendCommand(buffer, OPENUSD_SILK_COMMAND_FRAME, payload);
}

struct MeshWireCounts
{
    uint32_t pathByteCount;
    uint32_t pointCount;
    uint32_t indexCount;
    uint32_t triangleCount;
    size_t payloadSize;
};

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

HdSilkMeshRecord ApplyComplexity(
    const HdSilkMeshRecord& record,
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

    HdSilkMeshRecord result = record;
    result.points.clear();
    result.indices.clear();
    result.triangleSubprims.clear();
    for (HdSilkMeshAttribute& attribute : result.attributes)
    {
        if (attribute.interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX)
        {
            attribute.data.clear();
        }
    }

    if (record.topologyKind == OPENUSD_SILK_TOPOLOGY_POINT_LIST)
    {
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
            }
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

void AppendMeshUpsert(
    std::vector<uint8_t>& buffer,
    const HdSilkMeshRecord& record,
    uint32_t complexity)
{
    HdSilkMeshRecord complexRecord = ApplyComplexity(record, complexity);
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
        record.surfaceKind != OPENUSD_SILK_SURFACE_VOLUME_DENSITY)
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
        if (texture.wrapS > OPENUSD_SILK_WRAP_MIRROR ||
            texture.wrapT > OPENUSD_SILK_WRAP_MIRROR ||
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
std::atomic<uint64_t> _rejectedMeshCount{0};
std::atomic<uint64_t> _rejectedMaterialCount{0};
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

    AppendFrame(buffer, _frame, lights);
    size_t appendedCommands = 1;

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
                const HdSilkMeshRecord modeRecord =
                    ApplyDrawMode(entry->record, _drawMode);
                AppendMeshUpsert(buffer, modeRecord, _complexity);
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
