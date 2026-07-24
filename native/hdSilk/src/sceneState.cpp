// Copyright (c) marcschier. Licensed under the MIT License.

#include "sceneState.h"

#include "openusd_hdsilk.h"

#include <algorithm>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <utility>

PXR_NAMESPACE_OPEN_SCOPE

namespace
{
constexpr size_t MeshFixedPayloadSize = 192;

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

void AppendFrame(std::vector<uint8_t>& buffer, const HdSilkFrameState& frame)
{
    std::vector<uint8_t> payload;
    payload.reserve(8 + sizeof(frame.viewMatrix) + sizeof(frame.projectionMatrix));
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

MeshWireCounts ValidateMesh(const HdSilkMeshRecord& record)
{
    ValidatePath(record.path);
    if (record.primId < 0)
    {
        throw std::invalid_argument(
            "An hdSilk MESH_UPSERT requires a non-negative explicit prim ID.");
    }
    if (record.instanceId != 0 || record.instanceIndex != 0)
    {
        throw std::invalid_argument(
            "hdSilk page ABI v2 reserves zero for unsupported instance identity.");
    }
    if (record.topologyKind != OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST)
    {
        throw std::invalid_argument("An hdSilk mesh has an unsupported topology kind.");
    }
    if (record.topologyRevision == 0)
    {
        throw std::invalid_argument(
            "An hdSilk mesh topology revision must be non-zero.");
    }
    if ((record.points.size() % 3) != 0)
    {
        throw std::invalid_argument(
            "An hdSilk mesh point component count must be divisible by three.");
    }
    if ((record.indices.size() % 3) != 0)
    {
        throw std::invalid_argument(
            "An hdSilk mesh index count must be divisible by three.");
    }

    const size_t pointCount = record.points.size() / 3;
    const size_t triangleCount = record.indices.size() / 3;
    if (record.triangleSubprims.size() != triangleCount)
    {
        throw std::invalid_argument(
            "An hdSilk mesh requires one authored subprim index per triangle.");
    }
    for (uint32_t index : record.indices)
    {
        if (index >= pointCount)
        {
            throw std::invalid_argument(
                "An hdSilk triangle index is outside the point array.");
        }
    }

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
            "triangle subprim"),
        "mesh payload");
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
        CheckedCount(triangleCount, "triangle count"),
        payloadSize};
}

void AppendMeshUpsert(std::vector<uint8_t>& buffer, const HdSilkMeshRecord& record)
{
    const MeshWireCounts counts = ValidateMesh(record);

    std::vector<uint8_t> payload;
    payload.reserve(counts.payloadSize);

    AppendU64(payload, ComputeStableHash(record.path));
    AppendI32(payload, record.primId);
    AppendI32(payload, record.instanceId);
    AppendI32(payload, record.instanceIndex);
    AppendU32(payload, record.topologyKind);
    AppendU64(payload, record.topologyRevision);
    AppendU32(payload, counts.pathByteCount);
    AppendU32(payload, counts.pointCount);
    AppendU32(payload, counts.indexCount);
    AppendU32(payload, counts.triangleCount);
    for (float value : record.displayColor)
    {
        AppendF32(payload, value);
    }
    for (double value : record.transform)
    {
        AppendF64(payload, value);
    }
    AppendBytes(payload, record.path.data(), record.path.size());
    for (float value : record.points)
    {
        AppendF32(payload, value);
    }
    for (uint32_t value : record.indices)
    {
        AppendU32(payload, value);
    }
    for (uint32_t value : record.triangleSubprims)
    {
        AppendU32(payload, value);
    }

    AppendCommand(buffer, OPENUSD_SILK_COMMAND_MESH_UPSERT, payload);
}

void AppendMeshRemove(std::vector<uint8_t>& buffer, const std::string& path)
{
    ValidatePath(path);
    const uint32_t pathByteCount = CheckedCount(path.size(), "path byte count");
    if (path.size() >
        static_cast<size_t>(std::numeric_limits<uint32_t>::max()) - 20)
    {
        throw std::length_error(
            "An hdSilk MESH_REMOVE exceeds the 32-bit command byte_size.");
    }

    std::vector<uint8_t> payload;
    payload.reserve(8 + 4 + pathByteCount);
    AppendU64(payload, ComputeStableHash(path));
    AppendU32(payload, pathByteCount);
    AppendBytes(payload, path.data(), path.size());

    AppendCommand(buffer, OPENUSD_SILK_COMMAND_MESH_REMOVE, payload);
}
}

void
HdSilkSceneState::UpsertMesh(const std::string& path, HdSilkMeshRecord record)
{
    if (record.path != path)
    {
        throw std::invalid_argument(
            "The hdSilk scene-state key must match the mesh record path.");
    }
    std::lock_guard<std::mutex> lock(_mutex);
    _Entry& entry = _meshes[path];
    entry.record = std::move(record);
    entry.dirty = true;

    // If this path had a removal queued (e.g. a rapid destroy+recreate),
    // drop it: BuildPage() must not emit a MESH_REMOVE for a path that is
    // alive again by the time the next page is built. The replacement record
    // intentionally keeps its new prim ID and topology revision (which may
    // reset), allowing consumers to recognize an implicit recreation.
    _pendingRemovals.erase(
        std::remove(_pendingRemovals.begin(), _pendingRemovals.end(), path),
        _pendingRemovals.end());
}

void
HdSilkSceneState::RemoveMesh(const std::string& path)
{
    std::lock_guard<std::mutex> lock(_mutex);
    if (_meshes.erase(path) != 0)
    {
        _pendingRemovals.push_back(path);
    }
}

void
HdSilkSceneState::SetFrame(const HdSilkFrameState& frame)
{
    std::lock_guard<std::mutex> lock(_mutex);
    _frame = frame;
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
            return Utf8PathLess(left->record.path, right->record.path);
        });

    std::vector<std::string> removals = _pendingRemovals;
    std::sort(removals.begin(), removals.end(), Utf8PathLess);
    removals.erase(
        std::unique(removals.begin(), removals.end()),
        removals.end());

    size_t commandCountSize =
        CheckedAdd(1, dirtyEntries.size(), "page command count");
    commandCountSize = CheckedAdd(
        commandCountSize,
        removals.size(),
        "page command count");
    const uint32_t commandCount =
        CheckedCount(commandCountSize, "page command count");

    AppendFrame(buffer, _frame);

    for (const _Entry* entry : dirtyEntries)
    {
        AppendMeshUpsert(buffer, entry->record);
    }

    for (const std::string& path : removals)
    {
        AppendMeshRemove(buffer, path);
    }

    for (_Entry* entry : dirtyEntries)
    {
        entry->dirty = false;
    }
    _pendingRemovals.clear();

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
