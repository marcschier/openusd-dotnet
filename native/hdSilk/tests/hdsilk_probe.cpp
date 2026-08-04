// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_hdsilk.h"
#include "hdsilk_test_hooks.h"
#include "materialXBridge.h"
#include "openusd_dotnet_test_hooks.h"
#include "sceneState.h"

#include "pxr/base/gf/frustum.h"
#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec3d.h"
#include "pxr/imaging/cameraUtil/conformWindow.h"
#include "pxr/imaging/hd/tokens.h"
#include "pxr/usd/sdf/assetPath.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdlib>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <limits>
#include <mutex>
#include <string>
#include <thread>
#include <unordered_set>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
constexpr char SharedMeshPath[] = "/World/SharedStageProbeMesh";
constexpr char TopologyMeshPath[] = "/World/TopologyProbeMesh";
constexpr char MaterialPath[] = "/World/ProbeMaterial";
constexpr char SurfaceShaderPath[] = "/World/ProbeMaterial/Surface";
constexpr char TextureShaderPath[] = "/World/ProbeMaterial/Texture";
constexpr char MaterialTextureAsset[] = "textures/probe-albedo.png";
constexpr char PrimvarMeshPath[] = "/World/PrimvarMesh";
constexpr char BasisCurvesPath[] = "/World/ProbeBasisCurves";

/// One parsed entry of the ABI 4 vertex attribute table.
struct ParsedAttribute
{
    std::string name;
    uint32_t semantic = 0;
    uint32_t componentCount = 0;
    uint32_t interpolation = 0;
    uint32_t elementCount = 0;
    float firstValue = 0.0F;
};
static_assert(OPENUSD_SILK_SESSION_ABI_VERSION == 4);
static_assert(OPENUSD_SILK_PAGE_ABI_VERSION == 11);

openusd_render_camera AutomaticCamera()
{
    openusd_render_camera camera{};
    camera.struct_size = sizeof(camera);
    camera.mode = OPENUSD_RENDER_CAMERA_MODE_AUTO;
    return camera;
}

openusd_render_camera ExplicitCamera()
{
    openusd_render_camera camera{};
    camera.struct_size = sizeof(camera);
    camera.mode = OPENUSD_RENDER_CAMERA_MODE_MATRICES;
    GfMatrix4d view(1.0);
    view.SetTranslate(GfVec3d(1.25, -2.5, 3.75));
    GfFrustum frustum;
    frustum.SetPerspective(37.0, 1.5, 0.25, 750.0);
    std::memcpy(camera.view, view.GetArray(), sizeof(camera.view));
    const GfMatrix4d projection = frustum.ComputeProjectionMatrix();
    std::memcpy(
        camera.projection,
        projection.GetArray(),
        sizeof(camera.projection));
    return camera;
}

void SetEnvironment(const char* name, const char* value)
{
#if defined(_WIN32)
    _putenv_s(name, value);
#else
    if (value[0] == '\0')
    {
        unsetenv(name);
    }
    else
    {
        setenv(name, value, 1);
    }
#endif
}

struct ParsedMeshIdentity
{
    std::string path;
    int32_t prim_id = -1;
    uint64_t topology_revision = 0;
    int32_t instance_id = 0;
    int32_t instance_index = 0;
};

struct ParsedPage
{
    uint32_t frame_count = 0;
    uint32_t mesh_upsert_count = 0;
    uint32_t mesh_remove_count = 0;
    uint32_t parsed_count = 0;
    bool found_shared_upsert = false;
    bool found_shared_remove = false;
    bool found_topology_upsert = false;
    bool mesh_identity_valid = true;
    bool instance_fields_zero = true;
    float shared_first_x = 0.0F;
    uint64_t shared_stable_hash = 0;
    int32_t shared_prim_id = -1;
    uint32_t shared_topology_kind = 0;
    uint64_t shared_topology_revision = 0;
    uint32_t shared_triangle_count = 0;
    std::vector<uint32_t> shared_subprims;
    std::vector<uint32_t> topology_subprims;
    std::vector<std::string> upsert_paths;
    std::vector<std::string> remove_paths;
    std::vector<ParsedMeshIdentity> mesh_identities;
    std::vector<uint32_t> mesh_point_counts;
    std::vector<uint32_t> mesh_index_counts;
    std::vector<uint32_t> mesh_attribute_counts;
    uint32_t material_upsert_count = 0;
    uint32_t material_remove_count = 0;
    bool material_valid = true;
    bool found_material_upsert = false;
    std::string material_path;
    std::string shared_material_binding;
    uint32_t material_surface_kind = 0;
    uint32_t material_scalar_count = 0;
    uint32_t material_texture_count = 0;
    float material_roughness = -1.0F;
    std::string material_texture_asset;
    std::string material_texture_uv;
    uint32_t material_texture_parameter = 0;
    std::vector<std::string> material_remove_paths;
    std::vector<ParsedAttribute> primvar_mesh_attributes;
    bool found_primvar_mesh = false;
    bool found_basis_curves = false;
    uint32_t basis_curves_topology_kind = 0;
    uint32_t basis_curves_triangle_count = 0;
    std::vector<float> basis_curves_points;
    std::vector<uint32_t> basis_curves_indices;
    std::vector<uint32_t> basis_curves_subprims;
    int32_t frame_width = 0;
    int32_t frame_height = 0;
    std::array<double, 16> frame_view{};
    std::array<double, 16> frame_projection{};
};

class StartBarrier
{
public:
    explicit StartBarrier(size_t participantCount) : _remaining(participantCount)
    {
    }

    void ArriveAndWait()
    {
        std::unique_lock<std::mutex> lock(_mutex);
        if (--_remaining == 0)
        {
            _condition.notify_all();
            return;
        }
        _condition.wait(lock, [this] { return _remaining == 0; });
    }

private:
    std::mutex _mutex;
    std::condition_variable _condition;
    size_t _remaining;
};

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

uint64_t ComputeStableHash(const std::string& path)
{
    uint64_t hash = 14695981039346656037ull;
    for (unsigned char byte : path)
    {
        hash ^= static_cast<uint64_t>(byte);
        hash *= 1099511628211ull;
    }
    return hash;
}

bool AddSize(size_t* value, size_t add)
{
    if (add > std::numeric_limits<size_t>::max() - *value)
    {
        return false;
    }
    *value += add;
    return true;
}

bool MultiplySize(size_t left, size_t right, size_t* result)
{
    if (left != 0 && right > std::numeric_limits<size_t>::max() / left)
    {
        return false;
    }
    *result = left * right;
    return true;
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

ParsedPage ParseCommands(const uint8_t* data, size_t size)
{
    ParsedPage result;
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
            std::cerr << "Malformed command at offset " << offset << "\n";
            break;
        }

        if (type == OPENUSD_SILK_COMMAND_FRAME)
        {
            ++result.frame_count;
            constexpr size_t payloadOffset = 8;
            constexpr size_t viewOffset = payloadOffset + 8;
            constexpr size_t projectionOffset = viewOffset + (16 * sizeof(double));
            if (!ReadValue(
                    data,
                    size,
                    offset + payloadOffset,
                    &result.frame_width) ||
                !ReadValue(
                    data,
                    size,
                    offset + payloadOffset + sizeof(int32_t),
                    &result.frame_height) ||
                projectionOffset + (16 * sizeof(double)) >
                    static_cast<size_t>(byteSize))
            {
                std::cerr << "Malformed FRAME command.\n";
                break;
            }
            std::memcpy(
                result.frame_view.data(),
                data + offset + viewOffset,
                sizeof(result.frame_view));
            std::memcpy(
                result.frame_projection.data(),
                data + offset + projectionOffset,
                sizeof(result.frame_projection));
        }
        else if (type == OPENUSD_SILK_COMMAND_MESH_UPSERT)
        {
            ++result.mesh_upsert_count;
            uint64_t stableHash = 0;
            int32_t primId = -1;
            int32_t instanceId = -1;
            int32_t instanceIndex = -1;
            uint32_t topologyKind = 0;
            uint64_t topologyRevision = 0;
            uint32_t doubleSided = 0;
            uint32_t cullStyle = 0;
            uint32_t pathSize = 0;
            uint32_t pointCount = 0;
            uint32_t indexCount = 0;
            uint32_t triangleCount = 0;
            uint32_t materialPathSize = 0;
            uint32_t attributeCount = 0;
            constexpr size_t pathOffset = 224;
            if (ReadValue(data, size, offset + 8, &stableHash) &&
                ReadValue(data, size, offset + 16, &primId) &&
                ReadValue(data, size, offset + 20, &instanceId) &&
                ReadValue(data, size, offset + 24, &instanceIndex) &&
                ReadValue(data, size, offset + 28, &topologyKind) &&
                ReadValue(data, size, offset + 32, &topologyRevision) &&
                ReadValue(data, size, offset + 40, &doubleSided) &&
                ReadValue(data, size, offset + 44, &cullStyle) &&
                ReadValue(data, size, offset + 48, &pathSize) &&
                ReadValue(data, size, offset + 52, &pointCount) &&
                ReadValue(data, size, offset + 56, &indexCount) &&
                ReadValue(data, size, offset + 60, &triangleCount) &&
                ReadValue(data, size, offset + 216, &materialPathSize) &&
                ReadValue(data, size, offset + 220, &attributeCount))
            {
                size_t pointBytes = 0;
                size_t indexBytes = 0;
                size_t subprimBytes = 0;
                size_t expectedSize = pathOffset;
                bool sizesValid =
                    MultiplySize(pointCount, 3 * sizeof(float), &pointBytes) &&
                    MultiplySize(indexCount, sizeof(uint32_t), &indexBytes) &&
                    MultiplySize(
                        triangleCount,
                        sizeof(uint32_t),
                        &subprimBytes) &&
                    AddSize(&expectedSize, pathSize) &&
                    AddSize(&expectedSize, pointBytes) &&
                    AddSize(&expectedSize, indexBytes) &&
                    AddSize(&expectedSize, subprimBytes) &&
                    AddSize(&expectedSize, materialPathSize);
                // Walk the ABI v4 attribute table so the exact-size check stays
                // exact rather than being relaxed to accommodate it.
                std::vector<ParsedAttribute> attributes;
                for (uint32_t attribute = 0;
                     sizesValid && attribute < attributeCount;
                     ++attribute)
                {
                    uint32_t componentCount = 0;
                    uint32_t nameSize = 0;
                    uint32_t elementCount = 0;
                    size_t dataBytes = 0;
                    const size_t entry = offset + expectedSize;
                    uint32_t semantic = 0;
                    uint32_t interpolation = 0;
                    sizesValid =
                        ReadValue(data, size, entry, &semantic) &&
                        ReadValue(data, size, entry + 4, &componentCount) &&
                        ReadValue(data, size, entry + 8, &interpolation) &&
                        ReadValue(data, size, entry + 12, &nameSize) &&
                        ReadValue(data, size, entry + 16, &elementCount) &&
                        MultiplySize(elementCount, componentCount, &dataBytes) &&
                        MultiplySize(dataBytes, sizeof(float), &dataBytes) &&
                        AddSize(&expectedSize, 20) &&
                        AddSize(&expectedSize, nameSize) &&
                        AddSize(&expectedSize, dataBytes);
                    if (!sizesValid)
                    {
                        break;
                    }
                    ParsedAttribute parsed;
                    parsed.name.assign(
                        reinterpret_cast<const char*>(data + entry + 20),
                        nameSize);
                    parsed.semantic = semantic;
                    parsed.componentCount = componentCount;
                    parsed.interpolation = interpolation;
                    parsed.elementCount = elementCount;
                    if (elementCount != 0 && componentCount != 0)
                    {
                        ReadValue(
                            data,
                            size,
                            entry + 20 + nameSize,
                            &parsed.firstValue);
                    }
                    attributes.push_back(std::move(parsed));
                }
                const uint32_t indicesPerPrimitive =
                    topologyKind == OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST
                        ? 3u
                        : (topologyKind == OPENUSD_SILK_TOPOLOGY_LINE_LIST ? 2u : 1u);
                sizesValid = sizesValid &&
                    expectedSize == byteSize &&
                    (topologyKind == OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST ||
                     topologyKind == OPENUSD_SILK_TOPOLOGY_LINE_LIST ||
                     topologyKind == OPENUSD_SILK_TOPOLOGY_POINT_LIST) &&
                    static_cast<uint64_t>(triangleCount) *
                        indicesPerPrimitive == indexCount &&
                    doubleSided <= 1 &&
                    cullStyle <= OPENUSD_SILK_CULL_STYLE_FRONT_UNLESS_DOUBLE_SIDED;
                if (!sizesValid)
                {
                    result.mesh_identity_valid = false;
                }
                else
                {
                    const std::string path(
                        reinterpret_cast<const char*>(data + offset + pathOffset),
                        pathSize);
                    result.upsert_paths.push_back(path);
                    result.mesh_point_counts.push_back(pointCount);
                    result.mesh_index_counts.push_back(indexCount);
                    result.mesh_attribute_counts.push_back(attributeCount);
                    result.mesh_identities.push_back(
                        ParsedMeshIdentity{
                            path,
                            primId,
                            topologyRevision,
                            instanceId,
                            instanceIndex});
                    result.mesh_identity_valid &=
                        !path.empty() &&
                        path.front() == '/' &&
                        stableHash == ComputeStableHash(path) &&
                        primId >= 0 &&
                        (topologyKind == OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST ||
                         topologyKind == OPENUSD_SILK_TOPOLOGY_LINE_LIST ||
                         topologyKind == OPENUSD_SILK_TOPOLOGY_POINT_LIST) &&
                        topologyRevision != 0;
                    result.instance_fields_zero &=
                        instanceId == 0 && instanceIndex == 0;

                    const size_t pointsOffset =
                        offset + pathOffset + pathSize;
                    const size_t subprimOffset =
                        pointsOffset + pointBytes + indexBytes;
                    std::vector<uint32_t> subprims(triangleCount);
                    for (uint32_t triangle = 0;
                         triangle < triangleCount;
                         ++triangle)
                    {
                        if (!ReadValue(
                                data,
                                size,
                                subprimOffset +
                                    (triangle * sizeof(uint32_t)),
                                &subprims[triangle]))
                        {
                            result.mesh_identity_valid = false;
                            break;
                        }
                    }

                    float firstX = 0.0F;
                    if (path == SharedMeshPath && pointCount > 0 &&
                        ReadValue(data, size, pointsOffset, &firstX))
                    {
                        result.found_shared_upsert = true;
                        result.shared_first_x = firstX;
                        result.shared_stable_hash = stableHash;
                        result.shared_prim_id = primId;
                        result.shared_topology_kind = topologyKind;
                        result.shared_topology_revision = topologyRevision;
                        result.shared_triangle_count = triangleCount;
                        result.shared_subprims = std::move(subprims);
                        result.shared_material_binding.assign(
                            reinterpret_cast<const char*>(
                                data + subprimOffset + subprimBytes),
                            materialPathSize);
                    }
                    else if (path == TopologyMeshPath)
                    {
                        result.found_topology_upsert = true;
                        result.topology_subprims = std::move(subprims);
                    }
                    else if (path == PrimvarMeshPath)
                    {
                        result.found_primvar_mesh = true;
                        result.primvar_mesh_attributes = std::move(attributes);
                    }
                    else if (path == BasisCurvesPath)
                    {
                        result.found_basis_curves = true;
                        result.basis_curves_topology_kind = topologyKind;
                        result.basis_curves_triangle_count = triangleCount;
                        result.basis_curves_subprims = std::move(subprims);
                        result.basis_curves_points.resize(
                            static_cast<size_t>(pointCount) * 3);
                        for (size_t component = 0;
                             component < result.basis_curves_points.size();
                             ++component)
                        {
                            if (!ReadValue(
                                    data,
                                    size,
                                    pointsOffset +
                                        (component * sizeof(float)),
                                    &result.basis_curves_points[component]))
                            {
                                result.mesh_identity_valid = false;
                                break;
                            }
                        }
                        result.basis_curves_indices.resize(indexCount);
                        const size_t indicesOffset = pointsOffset + pointBytes;
                        for (uint32_t index = 0; index < indexCount; ++index)
                        {
                            if (!ReadValue(
                                    data,
                                    size,
                                    indicesOffset +
                                        (static_cast<size_t>(index) *
                                         sizeof(uint32_t)),
                                    &result.basis_curves_indices[index]))
                            {
                                result.mesh_identity_valid = false;
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                result.mesh_identity_valid = false;
            }
        }
        else if (type == OPENUSD_SILK_COMMAND_MESH_REMOVE)
        {
            ++result.mesh_remove_count;
            uint32_t pathSize = 0;
            constexpr size_t pathSizeOffset = 8 + 8 + 4;
            constexpr size_t pathOffset = 8 + 8 + 4 + 4;
            if (ReadValue(data, size, offset + pathSizeOffset, &pathSize) &&
                pathOffset <= byteSize &&
                static_cast<size_t>(pathSize) <=
                    static_cast<size_t>(byteSize) - pathOffset)
            {
                const std::string path(
                    reinterpret_cast<const char*>(data + offset + pathOffset),
                    pathSize);
                result.remove_paths.push_back(path);
                result.found_shared_remove = path == SharedMeshPath;
            }
        }
        else if (type == OPENUSD_SILK_COMMAND_MATERIAL_UPSERT)
        {
            ++result.material_upsert_count;
            uint64_t stableHash = 0;
            uint32_t pathSize = 0;
            uint32_t surfaceKind = 0;
            uint32_t scalarCount = 0;
            uint32_t textureCount = 0;
            constexpr size_t pathOffset = 32;
            if (!ReadValue(data, size, offset + 8, &stableHash) ||
                !ReadValue(data, size, offset + 16, &pathSize) ||
                !ReadValue(data, size, offset + 20, &surfaceKind) ||
                !ReadValue(data, size, offset + 24, &scalarCount) ||
                !ReadValue(data, size, offset + 28, &textureCount))
            {
                result.material_valid = false;
                offset += byteSize;
                ++result.parsed_count;
                continue;
            }
            size_t cursor = pathOffset;
            bool valid = AddSize(&cursor, pathSize);
            const std::string path(
                reinterpret_cast<const char*>(data + offset + pathOffset),
                pathSize);
            float roughness = -1.0F;
            for (uint32_t scalar = 0; valid && scalar < scalarCount; ++scalar)
            {
                uint32_t parameter = 0;
                uint32_t componentCount = 0;
                const size_t entry = offset + cursor;
                valid = ReadValue(data, size, entry, &parameter) &&
                    ReadValue(data, size, entry + 4, &componentCount) &&
                    componentCount >= 1 && componentCount <= 4;
                if (valid && parameter == OPENUSD_SILK_MATERIAL_ROUGHNESS)
                {
                    valid = ReadValue(data, size, entry + 8, &roughness);
                }
                size_t valueBytes = 0;
                valid = valid &&
                    MultiplySize(componentCount, sizeof(float), &valueBytes) &&
                    AddSize(&cursor, 8) &&
                    AddSize(&cursor, valueBytes);
            }
            std::string textureAsset;
            std::string textureUv;
            uint32_t textureParameter = 0;
            for (uint32_t texture = 0; valid && texture < textureCount; ++texture)
            {
                uint32_t parameter = 0;
                uint32_t assetSize = 0;
                uint32_t uvSize = 0;
                const size_t entry = offset + cursor;
                valid = ReadValue(data, size, entry, &parameter) &&
                    ReadValue(data, size, entry + 16, &assetSize) &&
                    ReadValue(data, size, entry + 20, &uvSize) &&
                    assetSize != 0;
                if (valid && texture == 0)
                {
                    textureParameter = parameter;
                    textureAsset.assign(
                        reinterpret_cast<const char*>(data + entry + 76),
                        assetSize);
                    textureUv.assign(
                        reinterpret_cast<const char*>(data + entry + 76 + assetSize),
                        uvSize);
                }
                valid = valid &&
                    AddSize(&cursor, 76) &&
                    AddSize(&cursor, assetSize) &&
                    AddSize(&cursor, uvSize);
            }
            uint32_t generatedFragmentSize = 0;
            if (valid)
            {
                valid = ReadValue(data, size, offset + cursor, &generatedFragmentSize) &&
                    (generatedFragmentSize % sizeof(uint32_t)) == 0 &&
                    AddSize(&cursor, sizeof(uint32_t)) &&
                    AddSize(&cursor, generatedFragmentSize);
            }
            uint32_t generatedMslSourceSize = 0;
            if (valid)
            {
                valid = ReadValue(data, size, offset + cursor, &generatedMslSourceSize) &&
                    AddSize(&cursor, sizeof(uint32_t)) &&
                    AddSize(&cursor, generatedMslSourceSize);
            }
            // Requiring the exact size means an unaccounted byte fails here rather
            // than surfacing as a silently mis-read parameter later.
            valid = valid && cursor == byteSize &&
                !path.empty() && path.front() == '/' &&
                stableHash == ComputeStableHash(path);
            result.material_valid &= valid;
            if (valid)
            {
                result.found_material_upsert = true;
                result.material_path = path;
                result.material_surface_kind = surfaceKind;
                result.material_scalar_count = scalarCount;
                result.material_texture_count = textureCount;
                result.material_roughness = roughness;
                result.material_texture_asset = std::move(textureAsset);
                result.material_texture_uv = std::move(textureUv);
                result.material_texture_parameter = textureParameter;
            }
        }
        else if (type == OPENUSD_SILK_COMMAND_MATERIAL_REMOVE)
        {
            ++result.material_remove_count;
            uint32_t pathSize = 0;
            constexpr size_t pathOffset = 20;
            if (ReadValue(data, size, offset + 16, &pathSize) &&
                pathOffset <= byteSize &&
                static_cast<size_t>(pathSize) ==
                    static_cast<size_t>(byteSize) - pathOffset)
            {
                result.material_remove_paths.emplace_back(
                    reinterpret_cast<const char*>(data + offset + pathOffset),
                    pathSize);
            }
            else
            {
                result.material_valid = false;
            }
        }
        else
        {
            std::cerr << "Unknown command type " << type << " at offset " << offset << "\n";
        }

        offset += byteSize;
        ++result.parsed_count;
    }
    return result;
}

HdSilkMeshRecord MakeSceneStateRecord(
    const std::string& path,
    int32_t primId,
    uint64_t topologyRevision = 1)
{
    HdSilkMeshRecord record;
    record.path = path;
    record.primId = primId;
    record.topologyRevision = topologyRevision;
    record.points = {
        0.0F, 0.0F, 0.0F,
        1.0F, 0.0F, 0.0F,
        0.0F, 1.0F, 0.0F};
    record.indices = {0, 1, 2};
    record.triangleSubprims = {0};
    return record;
}

const ParsedMeshIdentity* FindMeshIdentity(
    const ParsedPage& page,
    const std::string& path)
{
    const auto iterator = std::find_if(
        page.mesh_identities.begin(),
        page.mesh_identities.end(),
        [&path](const ParsedMeshIdentity& identity)
        {
            return identity.path == path;
        });
    return iterator == page.mesh_identities.end() ? nullptr : &*iterator;
}

/// Publishes exactly one non-instanced record, mirroring what HdSilkMesh does
/// for a prim without an instancer.
void ReplaceSingleMesh(
    HdSilkSceneState& state,
    const std::string& path,
    HdSilkMeshRecord record)
{
    std::vector<HdSilkMeshRecord> records;
    records.push_back(std::move(record));
    state.ReplaceMeshInstances(path, std::move(records));
}

/// Point-instanced prototypes publish one record per instance under a shared
/// authoritative path. ABI v8 carries geometry only in instance zero, and a
/// shrinking instancer must retire exactly the instances it dropped.
bool VerifyInstancedSceneStateSerialization()
{
    constexpr char InstancedPath[] = "/Instanced";
    HdSilkSceneState state;

    std::vector<HdSilkMeshRecord> instances;
    for (int32_t index = 0; index < 3; ++index)
    {
        HdSilkMeshRecord record = MakeSceneStateRecord(InstancedPath, 7);
        record.instanceId = 42;
        record.instanceIndex = index;
        record.transform[3] = static_cast<double>(index);
        if (index != 0)
        {
            record.points.clear();
            record.indices.clear();
            record.triangleSubprims.clear();
            record.attributes.clear();
        }
        instances.push_back(std::move(record));
    }
    state.ReplaceMeshInstances(InstancedPath, std::move(instances));

    uint32_t commandCount = 0;
    const std::vector<uint8_t> firstBytes =
        state.BuildPage(nullptr, &commandCount);
    const ParsedPage first = ParseCommands(firstBytes.data(), firstBytes.size());
    if (commandCount != 4 ||
        first.mesh_upsert_count != 3 ||
        first.mesh_remove_count != 0 ||
        first.mesh_identities.size() != 3)
    {
        return false;
    }
    for (int32_t index = 0; index < 3; ++index)
    {
        const ParsedMeshIdentity& identity =
            first.mesh_identities[static_cast<size_t>(index)];
        if (identity.path != InstancedPath ||
            identity.instance_id != 42 ||
            identity.instance_index != index ||
            identity.prim_id != 7 ||
            first.mesh_point_counts[static_cast<size_t>(index)] !=
                (index == 0 ? 3u : 0u) ||
            first.mesh_index_counts[static_cast<size_t>(index)] !=
                (index == 0 ? 3u : 0u) ||
            first.mesh_attribute_counts[static_cast<size_t>(index)] != 0u)
        {
            return false;
        }
    }

    // Shrinking to a single instance must retire instances 1 and 2 without
    // disturbing instance 0.
    std::vector<HdSilkMeshRecord> shrunk;
    HdSilkMeshRecord survivor = MakeSceneStateRecord(InstancedPath, 7);
    survivor.instanceId = 42;
    survivor.instanceIndex = 0;
    shrunk.push_back(std::move(survivor));
    state.ReplaceMeshInstances(InstancedPath, std::move(shrunk));

    const std::vector<uint8_t> shrunkBytes =
        state.BuildPage(nullptr, &commandCount);
    const ParsedPage shrunkPage =
        ParseCommands(shrunkBytes.data(), shrunkBytes.size());
    if (commandCount != 4 ||
        shrunkPage.mesh_upsert_count != 1 ||
        shrunkPage.mesh_remove_count != 2 ||
        shrunkPage.remove_paths !=
            std::vector<std::string>({InstancedPath, InstancedPath}))
    {
        return false;
    }

    // Removing the prim retires the one remaining instance.
    state.RemoveMesh(InstancedPath);
    const std::vector<uint8_t> removedBytes =
        state.BuildPage(nullptr, &commandCount);
    const ParsedPage removedPage =
        ParseCommands(removedBytes.data(), removedBytes.size());
    return commandCount == 2 &&
        removedPage.mesh_upsert_count == 0 &&
        removedPage.mesh_remove_count == 1;
}

/// A malformed record must be skipped with a diagnostic rather than aborting
/// the page, so one bad prim cannot blank an otherwise renderable scene. The
/// page still carries its FRAME command and no MESH_UPSERT.
bool RejectsInvalidSceneStateRecord(HdSilkMeshRecord record)
{
    const std::string path = record.path;
    HdSilkSceneState state;
    std::vector<HdSilkMeshRecord> records;
    records.push_back(std::move(record));
    const uint64_t rejectedBefore = HdSilkSceneState::GetRejectedMeshCount();
    try
    {
        state.ReplaceMeshInstances(path, std::move(records));
    }
    catch (const std::invalid_argument&)
    {
        // Path/key mismatches are still contract violations at publish time.
        return true;
    }

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    return commandCount == 1 &&
        page.mesh_upsert_count == 0 &&
        HdSilkSceneState::GetRejectedMeshCount() > rejectedBefore;
}

bool VerifySceneStateSerialization()
{
    HdSilkSceneState state;
    ReplaceSingleMesh(state, "/Zed", MakeSceneStateRecord("/Zed", 2));
    ReplaceSingleMesh(state, "/Alpha", MakeSceneStateRecord("/Alpha", 1, 5));
    uint64_t revision = 0;
    uint32_t commandCount = 0;
    std::vector<uint8_t> firstBytes =
        state.BuildPage(&revision, &commandCount);
    ParsedPage first = ParseCommands(firstBytes.data(), firstBytes.size());
    if (revision != 1 ||
        commandCount != 3 ||
        first.upsert_paths !=
            std::vector<std::string>({"/Alpha", "/Zed"}))
    {
        return false;
    }

    std::vector<uint8_t> steadyBytes =
        state.BuildPage(&revision, &commandCount);
    ParsedPage steady = ParseCommands(steadyBytes.data(), steadyBytes.size());
    if (revision != 2 ||
        commandCount != 1 ||
        steady.mesh_upsert_count != 0 ||
        steady.mesh_remove_count != 0)
    {
        return false;
    }

    state.RemoveMesh("/Alpha");
    ReplaceSingleMesh(state, "/Alpha", MakeSceneStateRecord("/Alpha", 9, 1));
    std::vector<uint8_t> readdBytes =
        state.BuildPage(&revision, &commandCount);
    ParsedPage readd = ParseCommands(readdBytes.data(), readdBytes.size());
    const ParsedMeshIdentity* readdIdentity =
        FindMeshIdentity(readd, "/Alpha");
    if (readd.upsert_paths != std::vector<std::string>({"/Alpha"}) ||
        readd.mesh_remove_count != 0 ||
        readdIdentity == nullptr ||
        readdIdentity->prim_id != 9 ||
        readdIdentity->topology_revision != 1)
    {
        return false;
    }

    state.RemoveMesh("/Zed");
    state.RemoveMesh("/Alpha");
    std::vector<uint8_t> removalBytes =
        state.BuildPage(&revision, &commandCount);
    ParsedPage removals =
        ParseCommands(removalBytes.data(), removalBytes.size());
    if (removals.remove_paths !=
        std::vector<std::string>({"/Alpha", "/Zed"}))
    {
        return false;
    }

    HdSilkMeshRecord invalidPoints = MakeSceneStateRecord("/InvalidPoints", 3);
    invalidPoints.points.pop_back();
    HdSilkMeshRecord invalidMapping = MakeSceneStateRecord("/InvalidMapping", 4);
    invalidMapping.triangleSubprims.clear();
    HdSilkMeshRecord invalidInstance = MakeSceneStateRecord("/InvalidInstance", 5);
    invalidInstance.instanceIndex = -1;
    return RejectsInvalidSceneStateRecord(std::move(invalidPoints)) &&
        RejectsInvalidSceneStateRecord(std::move(invalidMapping)) &&
        RejectsInvalidSceneStateRecord(std::move(invalidInstance)) &&
        VerifyInstancedSceneStateSerialization();
}

openusd_status Sync(
    openusd_silk_session* session,
    ParsedPage* parsed,
    openusd_error_buffer* error,
    const openusd_render_camera* requestedCamera = nullptr)
{
    const openusd_render_camera automatic = AutomaticCamera();
    const openusd_render_camera* camera =
        requestedCamera == nullptr ? &automatic : requestedCamera;
    openusd_silk_page* page = nullptr;
    openusd_silk_page_view view{};
    view.struct_size = sizeof(openusd_silk_page_view);
    const openusd_status status =
        openusd_silk_session_sync(
            session,
            64,
            64,
            0.0,
            camera,
            &page,
            &view,
            error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    if (view.struct_size != sizeof(openusd_silk_page_view) ||
        view.abi_version != OPENUSD_SILK_PAGE_ABI_VERSION)
    {
        openusd_silk_page_release(page);
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    *parsed = ParseCommands(view.data, view.data_size);
    const bool countMatches = parsed->parsed_count == view.command_count;
    openusd_silk_page_release(page);
    return countMatches ? OPENUSD_STATUS_OK : OPENUSD_STATUS_NATIVE_ERROR;
}

bool IsLegacyAutomaticFrame(const ParsedPage& parsed)
{
    GfMatrix4d view(1.0);
    view.SetLookAt(
        GfVec3d(4.0, 3.0, 4.0),
        GfVec3d(0.0, 0.0, 0.0),
        GfVec3d(0.0, 1.0, 0.0));
    GfFrustum frustum;
    frustum.SetPerspective(45.0, 1.0, 0.1, 1000.0);
    const GfMatrix4d projection = frustum.ComputeProjectionMatrix();
    return parsed.frame_width == 64 &&
        parsed.frame_height == 64 &&
        std::memcmp(
            parsed.frame_view.data(),
            view.GetArray(),
            sizeof(parsed.frame_view)) == 0 &&
        std::memcmp(
            parsed.frame_projection.data(),
            projection.GetArray(),
            sizeof(parsed.frame_projection)) == 0;
}

bool IsExplicitFrame(
    const ParsedPage& parsed,
    const openusd_render_camera& camera)
{
    // The published projection is conformed to the viewport aspect, matching
    // what UsdImagingGLEngine's free camera does for Storm. This camera declares
    // aspect 1.5 while the probe syncs a square 64x64 viewport, so the conformed
    // matrix must differ from the authored one -- asserting that difference is
    // what stops this check passing if the conform were dropped again.
    GfMatrix4d authored(1.0);
    std::memcpy(
        const_cast<double*>(authored.GetArray()),
        camera.projection,
        sizeof(camera.projection));
    const GfMatrix4d conformed =
        CameraUtilConformedWindow(authored, CameraUtilFit, 1.0);
    if (conformed == authored)
    {
        return false;
    }
    return parsed.frame_width == 64 &&
        parsed.frame_height == 64 &&
        std::memcmp(
            parsed.frame_view.data(),
            camera.view,
            sizeof(camera.view)) == 0 &&
        std::memcmp(
            parsed.frame_projection.data(),
            conformed.GetArray(),
            sizeof(camera.projection)) == 0;
}

bool RejectsCamera(
    openusd_silk_session* session,
    const openusd_render_camera* camera,
    openusd_error_buffer* error)
{
    openusd_silk_page* page = reinterpret_cast<openusd_silk_page*>(1);
    openusd_silk_page_view view{};
    view.struct_size = sizeof(view);
    return openusd_silk_session_sync(
               session,
               64,
               64,
               0.0,
               camera,
               &page,
               &view,
               error) == OPENUSD_STATUS_INVALID_ARGUMENT &&
        page == nullptr;
}

bool VerifyCameraValidation(
    openusd_silk_session* session,
    openusd_error_buffer* error)
{
    openusd_render_camera invalid = ExplicitCamera();
    invalid.struct_size = sizeof(invalid) - 1;
    if (!RejectsCamera(session, &invalid, error))
    {
        return false;
    }
    invalid = ExplicitCamera();
    invalid.mode = static_cast<openusd_render_camera_mode>(99);
    if (!RejectsCamera(session, &invalid, error))
    {
        return false;
    }
    invalid = ExplicitCamera();
    invalid.view[3] = std::nan("");
    if (!RejectsCamera(session, &invalid, error))
    {
        return false;
    }
    invalid = ExplicitCamera();
    invalid.projection[7] = std::numeric_limits<double>::infinity();
    return RejectsCamera(session, &invalid, error) &&
        RejectsCamera(session, nullptr, error);
}

/// Requires the exact attribute table the probe stage authors. Element counts
/// and values are asserted, not just presence, so a table that arrives with the
/// right shape but the wrong data still fails.
bool VerifyPrimvarAttributes(const std::vector<ParsedAttribute>& attributes)
{
    const ParsedAttribute* st = nullptr;
    const ParsedAttribute* weight = nullptr;
    const ParsedAttribute* tint = nullptr;
    const ParsedAttribute* normals = nullptr;
    const ParsedAttribute* faceWeight = nullptr;
    const ParsedAttribute* uniformWeight = nullptr;
    for (const ParsedAttribute& attribute : attributes)
    {
        if (attribute.name == "st")
        {
            st = &attribute;
        }
        else if (attribute.name == "probeWeight")
        {
            weight = &attribute;
        }
        else if (attribute.name == "probeTint")
        {
            tint = &attribute;
        }
        else if (attribute.name == "normals")
        {
            normals = &attribute;
        }
        else if (attribute.name == "probeFaceWeight")
        {
            faceWeight = &attribute;
        }
        else if (attribute.name == "probeUniformWeight")
        {
            uniformWeight = &attribute;
        }
    }
    if (st == nullptr || weight == nullptr || tint == nullptr ||
        normals == nullptr || faceWeight == nullptr || uniformWeight == nullptr)
    {
        return false;
    }

    // Entries are sorted by name, so an unchanged scene produces byte-identical
    // pages. Checking the order here keeps that contract honest.
    for (size_t index = 1; index < attributes.size(); ++index)
    {
        if (attributes[index - 1].name > attributes[index].name)
        {
            return false;
        }
    }

    return st->semantic == OPENUSD_SILK_ATTRIBUTE_TEXCOORD &&
        st->componentCount == 2 &&
        st->interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
        st->elementCount == 6 &&
        st->firstValue == 0.0F &&
        weight->semantic == OPENUSD_SILK_ATTRIBUTE_CUSTOM &&
        weight->componentCount == 1 &&
        weight->interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
        weight->elementCount == 6 &&
        weight->firstValue == 0.25F &&
        tint->semantic == OPENUSD_SILK_ATTRIBUTE_CUSTOM &&
        tint->componentCount == 3 &&
        tint->interpolation == OPENUSD_SILK_INTERPOLATION_CONSTANT &&
        tint->elementCount == 1 &&
        tint->firstValue == 0.2F &&
        normals->semantic == OPENUSD_SILK_ATTRIBUTE_NORMAL &&
        normals->componentCount == 3 &&
        normals->interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
        normals->elementCount == 6 &&
        normals->firstValue == 0.0F &&
        faceWeight->semantic == OPENUSD_SILK_ATTRIBUTE_CUSTOM &&
        faceWeight->componentCount == 1 &&
        faceWeight->interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
        faceWeight->elementCount == 6 &&
        faceWeight->firstValue == 0.125F &&
        uniformWeight->semantic == OPENUSD_SILK_ATTRIBUTE_CUSTOM &&
        uniformWeight->componentCount == 1 &&
        uniformWeight->interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
        uniformWeight->elementCount == 6 &&
        uniformWeight->firstValue == 0.875F;
}

bool AuthorSharedMesh(
    openusd_stage* stage,

    float firstX,
    openusd_error_buffer* error)
{
    const std::array<openusd_vec3f, 3> points{
        openusd_vec3f{firstX, 0.0F, 0.0F},
        openusd_vec3f{1.0F, 0.0F, 0.0F},
        openusd_vec3f{0.0F, 1.0F, 0.0F}};
    const std::array<int32_t, 1> counts{3};
    const std::array<int32_t, 3> indices{0, 1, 2};
    return openusd_geom_define_mesh(stage, SharedMeshPath, error) == OPENUSD_STATUS_OK &&
        openusd_geom_mesh_set_points(
            stage, SharedMeshPath, points.data(), points.size(), 0, 0.0, error) ==
            OPENUSD_STATUS_OK &&
        openusd_geom_mesh_set_topology(
            stage,
            SharedMeshPath,
            counts.data(),
            counts.size(),
            indices.data(),
            indices.size(),
            error) == OPENUSD_STATUS_OK;
}

/// Authors a UsdPreviewSurface with one constant input and one connected
/// UsdUVTexture, then binds it to the shared mesh. This exercises the real
/// Hydra path end to end rather than the serializer alone: the resolution
/// depends on Hydra building the network map from these authored opinions.
bool AuthorSharedMaterial(openusd_stage* stage, openusd_error_buffer* error)
{
    return openusd_shade_define_material(stage, MaterialPath, error) ==
            OPENUSD_STATUS_OK &&
        openusd_shade_define_shader(stage, SurfaceShaderPath, error) ==
            OPENUSD_STATUS_OK &&
        openusd_shade_shader_set_source_id(
            stage, SurfaceShaderPath, "UsdPreviewSurface", error) ==
            OPENUSD_STATUS_OK &&
        openusd_shade_create_input(
            stage,
            SurfaceShaderPath,
            "roughness",
            OPENUSD_SHADE_VALUE_FLOAT,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_set_input_float(
            stage, SurfaceShaderPath, "roughness", 0.375F, error) ==
            OPENUSD_STATUS_OK &&
        openusd_shade_create_input(
            stage,
            SurfaceShaderPath,
            "diffuseColor",
            OPENUSD_SHADE_VALUE_COLOR3F,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_create_output(
            stage,
            SurfaceShaderPath,
            "surface",
            OPENUSD_SHADE_VALUE_TOKEN,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_define_shader(stage, TextureShaderPath, error) ==
            OPENUSD_STATUS_OK &&
        openusd_shade_shader_set_source_id(
            stage, TextureShaderPath, "UsdUVTexture", error) ==
            OPENUSD_STATUS_OK &&
        openusd_shade_create_input(
            stage,
            TextureShaderPath,
            "file",
            OPENUSD_SHADE_VALUE_ASSET,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_set_input_string(
            stage,
            TextureShaderPath,
            "file",
            OPENUSD_SHADE_VALUE_ASSET,
            MaterialTextureAsset,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_create_output(
            stage,
            TextureShaderPath,
            "rgb",
            OPENUSD_SHADE_VALUE_FLOAT3,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_connect(
            stage,
            SurfaceShaderPath,
            "diffuseColor",
            OPENUSD_SHADE_ATTRIBUTE_INPUT,
            TextureShaderPath,
            "rgb",
            OPENUSD_SHADE_ATTRIBUTE_OUTPUT,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_material_create_surface_output(stage, MaterialPath, error) ==
            OPENUSD_STATUS_OK &&
        openusd_shade_connect(
            stage,
            MaterialPath,
            "surface",
            OPENUSD_SHADE_ATTRIBUTE_OUTPUT,
            SurfaceShaderPath,
            "surface",
            OPENUSD_SHADE_ATTRIBUTE_OUTPUT,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_material_bind(stage, SharedMeshPath, MaterialPath, error) ==
            OPENUSD_STATUS_OK;
}

bool EditSharedMeshPoints(
    openusd_stage* stage,

    float firstX,
    openusd_error_buffer* error)
{
    const std::array<openusd_vec3f, 3> points{
        openusd_vec3f{firstX, 0.0F, 0.0F},
        openusd_vec3f{1.0F, 0.0F, 0.0F},
        openusd_vec3f{0.0F, 1.0F, 0.0F}};
    return openusd_geom_mesh_set_points(
               stage,
               SharedMeshPath,
               points.data(),
               points.size(),
               0,
               0.0,
               error) == OPENUSD_STATUS_OK;
}

bool AuthorTopologyMesh(
    openusd_stage* stage,
    openusd_error_buffer* error)
{
    const std::array<openusd_vec3f, 7> points{
        openusd_vec3f{0.0F, 0.0F, 0.0F},
        openusd_vec3f{1.0F, 0.0F, 0.0F},
        openusd_vec3f{1.0F, 1.0F, 0.0F},
        openusd_vec3f{0.0F, 1.0F, 0.0F},
        openusd_vec3f{2.0F, 0.0F, 0.0F},
        openusd_vec3f{3.0F, 0.0F, 0.0F},
        openusd_vec3f{2.0F, 1.0F, 0.0F}};
    const std::array<int32_t, 3> counts{4, 3, 2};
    const std::array<int32_t, 9> indices{0, 1, 2, 3, 4, 5, 6, 0, 1};
    return openusd_geom_define_mesh(stage, TopologyMeshPath, error) ==
            OPENUSD_STATUS_OK &&
        openusd_geom_mesh_set_points(
            stage,
            TopologyMeshPath,
            points.data(),
            points.size(),
            0,
            0.0,
            error) == OPENUSD_STATUS_OK &&
        openusd_geom_mesh_set_topology(
            stage,
            TopologyMeshPath,
            counts.data(),
            counts.size(),
            indices.data(),
            indices.size(),
            error) == OPENUSD_STATUS_OK;
}

bool AuthorBasisCurves(
    openusd_stage* stage,
    openusd_error_buffer* error)
{
    const std::array<openusd_vec3f, 2> points{
        openusd_vec3f{0.0F, 0.0F, 0.0F},
        openusd_vec3f{1.0F, 0.0F, 0.0F}};
    const std::array<int32_t, 1> counts{2};
    const std::array<float, 1> widths{0.2F};
    // The generic vec3f setter creates Float3Array, while a typed
    // BasisCurves prim predeclares points as Point3fArray. Author the arrays
    // before applying the schema so this probe can use the public C ABI without
    // adding a test-only BasisCurves authoring entry point.
    return openusd_stage_define_prim(stage, BasisCurvesPath, "Scope", error) ==
            OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage,
            BasisCurvesPath,
            "type",
            "linear",
            0,
            0.0,
            error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage,
            BasisCurvesPath,
            "basis",
            "bezier",
            0,
            0.0,
            error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage,
            BasisCurvesPath,
            "wrap",
            "segmented",
            0,
            0.0,
            error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_vec3f_array(
            stage,
            BasisCurvesPath,
            "points",
            points.data(),
            points.size(),
            0,
            0.0,
            error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_int32_array(
            stage,
            BasisCurvesPath,
            "curveVertexCounts",
            counts.data(),
            counts.size(),
            0,
            0.0,
            error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_float_array(
            stage,
            BasisCurvesPath,
            "widths",
            widths.data(),
            widths.size(),
            0,
            0.0,
            error) == OPENUSD_STATUS_OK &&
        openusd_stage_define_prim(stage, BasisCurvesPath, "BasisCurves", error) ==
            OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage,
            BasisCurvesPath,
            "type",
            "linear",
            0,
            0.0,
            error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage,
            BasisCurvesPath,
            "basis",
            "bezier",
            0,
            0.0,
            error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage,
            BasisCurvesPath,
            "wrap",
            "segmented",
            0,
            0.0,
            error) == OPENUSD_STATUS_OK;
}

bool VerifyBasisCurvesTessellation(const ParsedPage& page)
{
    const std::vector<float> expectedPoints{
        0.0F, 0.0F, 0.0F,
        1.0F, 0.0F, 0.0F};
    const std::vector<uint32_t> expectedIndices{0, 1};
    const std::vector<uint32_t> expectedSubprims{0};
    return page.found_basis_curves &&
    page.basis_curves_topology_kind == OPENUSD_SILK_TOPOLOGY_LINE_LIST &&
    page.basis_curves_triangle_count == 1 &&
        page.basis_curves_points == expectedPoints &&
        page.basis_curves_indices == expectedIndices &&
        page.basis_curves_subprims == expectedSubprims;
}

bool EditSharedMeshTopologyToQuad(
    openusd_stage* stage,
    openusd_error_buffer* error)
{
    const std::array<openusd_vec3f, 4> points{
        openusd_vec3f{2.0F, 0.0F, 0.0F},
        openusd_vec3f{1.0F, 0.0F, 0.0F},
        openusd_vec3f{1.0F, 1.0F, 0.0F},
        openusd_vec3f{2.0F, 1.0F, 0.0F}};
    const std::array<int32_t, 1> counts{4};
    const std::array<int32_t, 4> indices{0, 1, 2, 3};
    return openusd_geom_mesh_set_points(
               stage,
               SharedMeshPath,
               points.data(),
               points.size(),
               0,
               0.0,
               error) == OPENUSD_STATUS_OK &&
        openusd_geom_mesh_set_topology(
            stage,
            SharedMeshPath,
            counts.data(),
            counts.size(),
            indices.data(),
            indices.size(),
            error) == OPENUSD_STATUS_OK;
}

bool VerifyConcurrentSessions(
    const char* pluginPath,
    openusd_stage* stage,
    openusd_error_buffer* error)
{
    constexpr size_t threadCount = 4;
    StartBarrier barrier(threadCount);
    std::array<bool, threadCount> passed{};
    std::array<std::thread, threadCount> threads;
    for (size_t index = 0; index < threadCount; ++index)
    {
        threads[index] = std::thread(
            [&, index]
            {
                std::array<char, 4096> threadErrorText{};
                openusd_error_buffer threadError{
                    threadErrorText.data(), threadErrorText.size(), 0};
                barrier.ArriveAndWait();
                openusd_silk_session* session = nullptr;
                if (openusd_silk_session_create_from_stage(
                        pluginPath, stage, &session, &threadError) != OPENUSD_STATUS_OK)
                {
                    return;
                }
                ParsedPage parsed;
                const openusd_status status = Sync(session, &parsed, &threadError);
                openusd_silk_session_release(session);
                passed[index] =
                    status == OPENUSD_STATUS_OK &&
                    parsed.frame_count == 1 &&
                    parsed.found_shared_upsert;
            });
    }
    for (std::thread& thread : threads)
    {
        thread.join();
    }
    for (bool value : passed)
    {
        if (!value)
        {
            if (error != nullptr && error->data != nullptr && error->capacity > 0)
            {
                constexpr char message[] = "Concurrent hdSilk session creation failed.";
                const size_t copySize = sizeof(message) <= error->capacity
                    ? sizeof(message)
                    : error->capacity;
                std::memcpy(error->data, message, copySize);
                error->data[copySize - 1] = '\0';
                error->required = sizeof(message);
            }
            return false;
        }
    }
    return true;
}

openusd_status GetRendererName(
    openusd_silk_session* session,
    openusd_error_buffer* error)
{
    std::array<char, 256> buffer{};
    size_t required = 0;
    return openusd_silk_session_get_renderer_name(
        session, buffer.data(), buffer.size(), &required, error);
}

bool WaitForInFlight(
    openusd_silk_session* session,
    size_t expected)
{
    const auto deadline =
        std::chrono::steady_clock::now() + std::chrono::seconds(10);
    while (std::chrono::steady_clock::now() < deadline)
    {
        if (openusd_hdsilk_test_get_session_in_flight(session) == expected)
        {
            return true;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    return false;
}

bool VerifyDestroyRace(
    const char* pluginPath,
    openusd_stage* stage,
    openusd_error_buffer* error)
{
    constexpr size_t queuedCount = 12;
    openusd_silk_session* session = nullptr;
    if (openusd_silk_session_create_from_stage(
            pluginPath, stage, &session, error) != OPENUSD_STATUS_OK)
    {
        return false;
    }

    openusd_stage_access* access = nullptr;
    if (openusd_stage_access_begin(stage, &access, error) != OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        return false;
    }

    std::array<openusd_status, queuedCount + 1> statuses{};
    std::thread blocker(
        [&]
        {
            std::array<char, 4096> text{};
            openusd_error_buffer threadError{text.data(), text.size(), 0};
            ParsedPage parsed;
            statuses[0] = Sync(session, &parsed, &threadError);
        });
    if (!WaitForInFlight(session, 1))
    {
        openusd_stage_access_end(access, error);
        blocker.join();
        openusd_silk_session_release(session);
        return false;
    }

    std::array<std::thread, queuedCount> queued;
    for (size_t index = 0; index < queuedCount; ++index)
    {
        queued[index] = std::thread(
            [&, index]
            {
                std::array<char, 4096> text{};
                openusd_error_buffer threadError{text.data(), text.size(), 0};
                if ((index % 2) == 0)
                {
                    ParsedPage parsed;
                    statuses[index + 1] = Sync(session, &parsed, &threadError);
                }
                else
                {
                    statuses[index + 1] = GetRendererName(session, &threadError);
                }
            });
    }
    if (!WaitForInFlight(session, queuedCount + 1))
    {
        openusd_stage_access_end(access, error);
        blocker.join();
        for (std::thread& thread : queued)
        {
            thread.join();
        }
        openusd_silk_session_release(session);
        return false;
    }

    std::atomic<bool> destroyDone{false};
    openusd_status destroyStatus = OPENUSD_STATUS_NATIVE_ERROR;
    std::thread destroyer(
        [&]
        {
            std::array<char, 4096> text{};
            openusd_error_buffer threadError{text.data(), text.size(), 0};
            destroyStatus = openusd_silk_session_destroy(session, &threadError);
            destroyDone.store(true, std::memory_order_release);
        });
    std::this_thread::sleep_for(std::chrono::milliseconds(25));
    const bool waited = !destroyDone.load(std::memory_order_acquire);
    const openusd_status endStatus = openusd_stage_access_end(access, error);
    blocker.join();
    for (std::thread& thread : queued)
    {
        thread.join();
    }
    destroyer.join();

    for (openusd_status status : statuses)
    {
        if (status != OPENUSD_STATUS_OK)
        {
            return false;
        }
    }
    if (!waited || endStatus != OPENUSD_STATUS_OK ||
        destroyStatus != OPENUSD_STATUS_OK)
    {
        return false;
    }

    openusd_silk_page* stalePage =
        reinterpret_cast<openusd_silk_page*>(1);
    openusd_silk_page_view staleView{};
    staleView.struct_size = sizeof(openusd_silk_page_view);
    const openusd_render_camera automatic = AutomaticCamera();
    if (openusd_silk_session_sync(
            session,
            64,
            64,
            0.0,
            &automatic,
            &stalePage,
            &staleView,
            error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        stalePage != nullptr)
    {
        return false;
    }
    std::array<char, 8> staleName{};
    staleName.fill('x');
    size_t staleRequired = 99;
    if (openusd_silk_session_get_renderer_name(
            session,
            staleName.data(),
            staleName.size(),
            &staleRequired,
            error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        staleRequired != 0 || staleName[0] != '\0' ||
        openusd_silk_session_destroy(session, error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT)
    {
        return false;
    }
    return true;
}

bool VerifyNeverReusedHandles(
    const char* pluginPath,
    openusd_stage* stage,
    openusd_error_buffer* error)
{
    constexpr size_t iterationCount = 32;
    std::unordered_set<uintptr_t> tokens;
    for (size_t index = 0; index < iterationCount; ++index)
    {
        openusd_silk_session* session = nullptr;
        if (openusd_silk_session_create_from_stage(
                pluginPath, stage, &session, error) != OPENUSD_STATUS_OK ||
            !tokens.insert(reinterpret_cast<uintptr_t>(session)).second ||
            openusd_silk_session_destroy(session, error) != OPENUSD_STATUS_OK ||
            GetRendererName(session, error) != OPENUSD_STATUS_INVALID_ARGUMENT)
        {
            openusd_silk_session_release(session);
            return false;
        }
    }
    return tokens.size() == iterationCount;
}

bool VerifyMaterialXBridge()
{
    HdMaterialNode primvar;
    primvar.path = SdfPath("/World/Material/Primvar");
    primvar.identifier = TfToken("ND_geompropvalue_vector2");
    primvar.parameters[TfToken("geomprop")] = VtValue(std::string("st"));

    HdMaterialNode image;
    image.path = SdfPath("/World/Material/Image");
    image.identifier = TfToken("ND_image_color3");
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Material/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");
    surface.parameters[TfToken("specular_roughness")] = VtValue(0.42F);
    surface.parameters[TfToken("metalness")] = VtValue(0.0F);

    HdMaterialNetwork network;
    network.nodes = {primvar, image, surface};
    network.relationships.push_back(
        {primvar.path, TfToken("out"), image.path, TfToken("texcoord")});
    network.relationships.push_back(
        {image.path, TfToken("out"), surface.path, TfToken("base_color")});
    network.primvars = {TfToken("st")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));

    const HdSilkMaterialXDocument document =
        HdSilkCreateMaterialXDocumentFromNetwork(SdfPath("/World/Material"), map);
    if (!document.success)
    {
        std::cerr << "MaterialX bridge failed: " << document.error
                  << " validation='" << document.validation << "'\n";
        return false;
    }

    bool vulkanGenerated = true;
    bool mslGenerated = true;
#if defined(OPENUSD_HDSILK_WITH_MATERIALX_VULKAN)
    const HdSilkMaterialXVulkanShader vulkan =
        HdSilkGenerateMaterialXVulkanFragment(SdfPath("/World/Material"), map);
    vulkanGenerated = vulkan.fragmentSpirv.size() > 5 &&
        vulkan.fragmentSpirv[0] == 0x07230203u &&
        vulkan.fragmentSource.find("main") != std::string::npos;
    if (!vulkan.success)
    {
        std::cerr << "MaterialX Vulkan generation failed: " << vulkan.error << "\n";
        return false;
    }
#endif
#if defined(OPENUSD_HDSILK_WITH_MATERIALX_MSL)
    const HdSilkMaterialXVulkanShader msl =
        HdSilkGenerateMaterialXVulkanFragment(SdfPath("/World/Material"), map);
    mslGenerated = msl.success &&
        msl.fragmentMslSource.find("fragment") != std::string::npos;
    if (!mslGenerated)
    {
        std::cerr << "MaterialX Metal generation failed: " << msl.error << "\n";
        return false;
    }
#endif

    return vulkanGenerated &&
        mslGenerated &&
        document.xml.find("standard_surface") != std::string::npos &&
        document.xml.find("image") != std::string::npos &&
        document.xml.find("materialx-basecolor.png") != std::string::npos &&
        document.xml.find("specular_roughness") != std::string::npos &&
        document.textureNodeCount == 1 &&
        document.primvarNodeCount == 1;
}
}

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        std::cerr << "Usage: hdsilk_probe <plugin-path> <stage-path>\n";
        return 2;
    }

    std::array<char, 4096> errorText{};
    openusd_error_buffer error{errorText.data(), errorText.size(), 0};
    if (!VerifySceneStateSerialization())
    {
        std::cerr << "hdSilk scene-state ABI serialization checks failed.\n";
        return 3;
    }
    if (openusd_hdsilk_test_external_delegate_does_not_publish() != 1)
    {
        std::cerr << "External hdSilk delegate stole a creation token.\n";
        return 4;
    }
    if (!VerifyMaterialXBridge())
    {
        std::cerr << "hdSilk MaterialX bridge check failed.\n";
        return 4;
    }

    const size_t initialStageCoreCount = openusd_test_get_live_stage_core_count();
    openusd_stage* stage = nullptr;
    if (openusd_stage_open(argv[2], &stage, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_retain(stage, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_edit_target_session_layer(stage, &error) != OPENUSD_STATUS_OK ||
        !AuthorSharedMesh(stage, 0.0F, &error) ||
        !AuthorTopologyMesh(stage, &error) ||
        !AuthorBasisCurves(stage, &error) ||
        !AuthorSharedMaterial(stage, &error))
    {
        openusd_stage_release(stage);
        std::cerr << "Stage setup failed: " << errorText.data() << "\n";
        return 4;
    }

    const size_t retainedStageCoreCount = openusd_test_get_live_stage_core_count();
    SetEnvironment("OPENUSD_RENDERER_CREATE_FAILPOINT", "after-retain");
    openusd_silk_session* failedSession =
        reinterpret_cast<openusd_silk_session*>(1);
    const openusd_status failedCreateStatus =
        openusd_silk_session_create_from_stage(argv[1], stage, &failedSession, &error);
    SetEnvironment("OPENUSD_RENDERER_CREATE_FAILPOINT", "");
    if (failedCreateStatus == OPENUSD_STATUS_OK ||
        failedSession != nullptr ||
        openusd_test_get_live_stage_core_count() != retainedStageCoreCount)
    {
        openusd_stage_release(stage);
        openusd_stage_release(stage);
        std::cerr << "hdSilk after-retain failpoint did not roll back cleanly.\n";
        return 5;
    }

    openusd_silk_session* session = nullptr;
    if (openusd_silk_session_create_from_stage(
            argv[1], stage, &session, &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        openusd_stage_release(stage);
        std::cerr << "session_create_from_stage failed: " << errorText.data() << "\n";
        return 6;
    }
    openusd_stage_release(stage);

    SetEnvironment("OPENUSD_DOTNET_STAGE_ACCESS_BEGIN_FAILPOINT", "after-retain");
    const openusd_status failedDestroyStatus =
        openusd_silk_session_destroy(session, &error);
    SetEnvironment("OPENUSD_DOTNET_STAGE_ACCESS_BEGIN_FAILPOINT", "");
    if (failedDestroyStatus == OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "hdSilk destroy ignored the stage-access failpoint.\n";
        return 7;
    }

    ParsedPage initial;
    if (Sync(session, &initial, &error) != OPENUSD_STATUS_OK ||
        !initial.found_shared_upsert ||
        !initial.found_topology_upsert ||
        initial.shared_first_x != 0.0F ||
        !initial.mesh_identity_valid ||
        !initial.instance_fields_zero ||
        initial.shared_stable_hash != ComputeStableHash(SharedMeshPath) ||
        initial.shared_prim_id < 0 ||
        initial.shared_topology_kind != OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST ||
        initial.shared_topology_revision != 1 ||
        initial.shared_triangle_count != 1 ||
        initial.shared_subprims != std::vector<uint32_t>{0} ||
        initial.topology_subprims != std::vector<uint32_t>({0, 0, 1}) ||
        !VerifyBasisCurvesTessellation(initial) ||
        !std::is_sorted(
            initial.upsert_paths.begin(),
            initial.upsert_paths.end(),
            Utf8PathLess))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Unsaved shared-stage mesh was not observed: " << errorText.data() << "\n";
        return 5;
    }
    if (!IsLegacyAutomaticFrame(initial))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr
            << "Automatic hdSilk camera changed from the legacy frame: "
            << initial.frame_width << 'x' << initial.frame_height
            << " view[0]=" << initial.frame_view[0]
            << " projection[0]=" << initial.frame_projection[0] << "\n";
        return 5;
    }
    // ABI v5: the bound UsdPreviewSurface must arrive resolved, and the mesh must
    // reference it by path. Asserting both together is what proves the material
    // Sprim, the binding, and the wire agree, rather than each in isolation.
    if (!initial.material_valid ||
        !initial.found_material_upsert ||
        initial.material_upsert_count != 1 ||
        initial.material_path != MaterialPath ||
        initial.material_surface_kind != OPENUSD_SILK_SURFACE_PREVIEW_SURFACE ||
        initial.material_scalar_count != 1 ||
        initial.material_roughness != 0.375F ||
        initial.material_texture_count != 1 ||
        initial.material_texture_parameter !=
            OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
        initial.material_texture_asset.find(MaterialTextureAsset) ==
            std::string::npos ||
        initial.shared_material_binding != MaterialPath)
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr
            << "hdSilk material was not published as expected: path='"
            << initial.material_path << "' kind=" << initial.material_surface_kind
            << " scalars=" << initial.material_scalar_count
            << " roughness=" << initial.material_roughness
            << " textures=" << initial.material_texture_count
            << " asset='" << initial.material_texture_asset
            << "' binding='" << initial.shared_material_binding << "'\n";
        return 5;
    }

    // ABI v4 attribute table: the stage authors texture coordinates, arbitrary
    // named primvars, a constant primvar, and face-varying normals. All must
    // arrive resolved onto the emitted vertices. Without this, nothing proves
    // texture coordinates or authored normals reach a consumer at all.
    if (!initial.found_primvar_mesh ||
        !VerifyPrimvarAttributes(initial.primvar_mesh_attributes))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "hdSilk primvar attributes were not published as expected; got "
                  << initial.primvar_mesh_attributes.size() << " attribute(s):";
        for (const ParsedAttribute& attribute : initial.primvar_mesh_attributes)
        {
            std::cerr << " {" << attribute.name
                      << " semantic=" << attribute.semantic
                      << " components=" << attribute.componentCount
                      << " interpolation=" << attribute.interpolation
                      << " elements=" << attribute.elementCount
                      << " first=" << attribute.firstValue << "}";
        }
        std::cerr << "\n";
        return 5;
    }

    if (openusd_geom_imageable_set_visibility(
            stage,
            PrimvarMeshPath,
            OPENUSD_GEOM_VISIBILITY_INVISIBLE,
            0,
            0.0,
            &error) != OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Primvar mesh visibility edit failed: "
                  << errorText.data() << "\n";
        return 5;
    }
    ParsedPage hidden;
    if (Sync(session, &hidden, &error) != OPENUSD_STATUS_OK ||
        std::find(
            hidden.remove_paths.begin(),
            hidden.remove_paths.end(),
            PrimvarMeshPath) == hidden.remove_paths.end())
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Invisible hdSilk mesh did not emit removal.\n";
        return 5;
    }
    if (openusd_geom_imageable_set_visibility(
            stage,
            PrimvarMeshPath,
            OPENUSD_GEOM_VISIBILITY_INHERITED,
            0,
            0.0,
            &error) != OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Primvar mesh visibility restore failed: "
                  << errorText.data() << "\n";
        return 5;
    }
    ParsedPage visible;
    if (Sync(session, &visible, &error) != OPENUSD_STATUS_OK ||
        !visible.found_primvar_mesh ||
        !VerifyPrimvarAttributes(visible.primvar_mesh_attributes))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Restored visible hdSilk mesh did not republish attributes.\n";
        return 5;
    }

    const openusd_render_camera explicitCamera = ExplicitCamera();
    ParsedPage explicitPage;
    if (Sync(session, &explicitPage, &error, &explicitCamera) !=
            OPENUSD_STATUS_OK ||
        !IsExplicitFrame(explicitPage, explicitCamera) ||
        explicitPage.mesh_upsert_count != 0 ||
        !VerifyCameraValidation(session, &error))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "hdSilk camera ABI validation failed: "
                  << errorText.data() << "\n";
        return 6;
    }

    if (!VerifyConcurrentSessions(argv[1], stage, &error))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << errorText.data() << "\n";
        return 6;
    }
    if (!VerifyDestroyRace(argv[1], stage, &error))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Queued Sync/name destruction race failed: "
                  << errorText.data() << "\n";
        return 7;
    }
    if (!VerifyNeverReusedHandles(argv[1], stage, &error))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "hdSilk token reuse/stale-handle soak failed: "
                  << errorText.data() << "\n";
        return 8;
    }

    if (!EditSharedMeshPoints(stage, 2.0F, &error))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Live mesh edit failed: " << errorText.data() << "\n";
        return 7;
    }
    ParsedPage edited;
    if (Sync(session, &edited, &error) != OPENUSD_STATUS_OK ||
        !edited.found_shared_upsert ||
        edited.shared_first_x != 2.0F ||
        edited.shared_topology_revision != initial.shared_topology_revision ||
        edited.shared_subprims != initial.shared_subprims ||
        !edited.instance_fields_zero)
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Live mesh edit was not observed: " << errorText.data() << "\n";
        return 8;
    }

    if (!EditSharedMeshTopologyToQuad(stage, &error))
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Live mesh topology edit failed: " << errorText.data() << "\n";
        return 9;
    }
    ParsedPage topologyEdited;
    if (Sync(session, &topologyEdited, &error) != OPENUSD_STATUS_OK ||
        !topologyEdited.found_shared_upsert ||
        topologyEdited.shared_topology_revision <=
            edited.shared_topology_revision ||
        topologyEdited.shared_triangle_count != 2 ||
        topologyEdited.shared_subprims != std::vector<uint32_t>({0, 0}) ||
        !topologyEdited.instance_fields_zero)
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Live mesh topology identity was not updated: "
                  << errorText.data() << "\n";
        return 9;
    }

    if (openusd_stage_remove_prim(stage, SharedMeshPath, &error) != OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "Live mesh removal failed: " << errorText.data() << "\n";
        return 9;
    }
    openusd_stage_release(stage);

    ParsedPage removed;
    if (Sync(session, &removed, &error) != OPENUSD_STATUS_OK ||
        !removed.found_shared_remove)
    {
        openusd_silk_session_release(session);
        std::cerr << "Live mesh removal was not observed: " << errorText.data() << "\n";
        return 10;
    }

    for (int index = 0; index < 16; ++index)
    {
        openusd_silk_session* rapidSession = nullptr;
        if (openusd_silk_session_create(argv[1], argv[2], &rapidSession, &error) !=
            OPENUSD_STATUS_OK)
        {
            openusd_silk_session_release(session);
            std::cerr << "Rapid session creation failed: " << errorText.data() << "\n";
            return 11;
        }
        if (index == 0)
        {
            ParsedPage pathPage;
            if (Sync(rapidSession, &pathPage, &error) != OPENUSD_STATUS_OK ||
                pathPage.frame_count != 1)
            {
                openusd_silk_session_release(rapidSession);
                openusd_silk_session_release(session);
                std::cerr << "Path session failed after temporary stage release: "
                          << errorText.data() << "\n";
                return 12;
            }
        }
        openusd_silk_session_release(rapidSession);
    }

    if (openusd_silk_session_destroy(session, &error) != OPENUSD_STATUS_OK)
    {
        std::cerr << "Checked hdSilk destruction failed: " << errorText.data() << "\n";
        return 12;
    }
    if (openusd_test_get_live_stage_core_count() != initialStageCoreCount)
    {
        std::cerr << "hdSilk probe leaked a stage core.\n";
        return 13;
    }
    std::cout << "OK: exact-stage retention, unsaved/live edits, concurrent and rapid sessions\n";
    return 0;
}
