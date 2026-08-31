// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_hdsilk.h"
#include "hdsilk_test_hooks.h"
#include "curveWidths.h"
#include "material.h"
#include "materialXBridge.h"
#include "openusd_dotnet_test_hooks.h"
#include "sceneState.h"

#include "pxr/base/gf/frustum.h"
#include "pxr/base/gf/half.h"
#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec2f.h"
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
#include <map>
#include <mutex>
#include <string>
#include <thread>
#include <tuple>
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
constexpr char VaryingWidthCurvesPath[] = "/World/ProbeVaryingWidthCurves";
constexpr char UniformWidthCurvesPath[] = "/World/ProbeUniformWidthCurves";
constexpr char RightHandedMeshPath[] = "/World/ProbeRightHandedMesh";
constexpr char LeftHandedMeshPath[] = "/World/ProbeLeftHandedMesh";
constexpr char LeftHandedFaceVaryingMeshPath[] =
    "/World/ProbeLeftHandedFaceVaryingMesh";

constexpr char BlendShapeMeshPath[] = "/World/Rig/BlendShapeTriangle";

/// One parsed entry of the ABI 4 vertex attribute table.
struct ParsedAttribute
{
    std::string name;
    uint32_t semantic = 0;
    uint32_t componentCount = 0;
    uint32_t interpolation = 0;
    uint32_t elementCount = 0;
    float firstValue = 0.0F;
    std::vector<float> values;
};
static_assert(OPENUSD_SILK_SESSION_ABI_VERSION == 5);
static_assert(OPENUSD_SILK_PAGE_ABI_VERSION == 15);

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
    // Point-instancer evidence needs the resolved world transform and whether
    // the record carried the ABI v8 prototype payload, neither of which any
    // other parsed field can stand in for.
    uint32_t point_count = 0;
    std::array<double, 16> transform{};
};

/// One published basis-curves line list, including the resolved width
/// attribute. Widths are captured in full rather than by first element: the
/// point of the varying-width probe is that every emitted vertex carries its
/// own authored value.
struct ParsedCurves
{
    bool found = false;
    uint32_t topology_kind = 0;
    uint32_t triangle_count = 0;
    std::vector<float> points;
    std::vector<uint32_t> indices;
    std::vector<uint32_t> subprims;
    std::vector<ParsedAttribute> attributes;
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
    std::vector<int32_t> remove_instance_indices;
    std::vector<ParsedMeshIdentity> mesh_identities;
    std::vector<uint32_t> mesh_point_counts;
    std::vector<uint32_t> mesh_index_counts;
    std::vector<uint32_t> mesh_attribute_counts;
    std::vector<uint32_t> mesh_cull_styles;
    std::vector<uint32_t> mesh_topology_kinds;
    std::vector<uint32_t> mesh_triangle_counts;
    uint32_t material_upsert_count = 0;
    uint32_t material_remove_count = 0;
    bool material_valid = true;
    bool found_material_upsert = false;
    std::string material_path;
    std::string shared_material_binding;
    uint32_t material_surface_kind = 0;
    uint32_t material_scalar_count = 0;
    uint32_t material_texture_count = 0;
    std::array<float, 6> material_uv_transform{1.0F, 0.0F, 0.0F, 1.0F, 0.0F, 0.0F};
    float material_roughness = -1.0F;
    std::string material_texture_asset;
    std::string material_texture_uv;
    uint32_t material_texture_parameter = 0;
    // Every published texture entry as (parameter, output channel, asset), in
    // wire order. The channel is what proves the authored UsdUVTexture output
    // token survived resolution, which no other field can stand in for.
    std::vector<std::tuple<uint32_t, uint32_t, std::string>> material_textures;
    std::vector<std::string> material_remove_paths;
    std::vector<ParsedAttribute> primvar_mesh_attributes;
    bool found_primvar_mesh = false;
    ParsedCurves basis_curves;
    ParsedCurves varying_width_curves;
    ParsedCurves uniform_width_curves;
    ParsedCurves right_handed_mesh;
    ParsedCurves left_handed_mesh;
    ParsedCurves left_handed_face_varying_mesh;

    bool found_blend_shape_mesh = false;
    float blend_shape_first_x = 0.0F;
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
                        const size_t valueCount =
                            static_cast<size_t>(elementCount) * componentCount;
                        parsed.values.resize(valueCount);
                        for (size_t value = 0; value < valueCount; ++value)
                        {
                            if (!ReadValue(
                                    data,
                                    size,
                                    entry + 20 + nameSize +
                                        (value * sizeof(float)),
                                    &parsed.values[value]))
                            {
                                sizesValid = false;
                                break;
                            }
                        }
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
                    result.mesh_cull_styles.push_back(cullStyle);
                    result.mesh_topology_kinds.push_back(topologyKind);
                    result.mesh_triangle_counts.push_back(triangleCount);
                    ParsedMeshIdentity identity{
                        path,
                        primId,
                        topologyRevision,
                        instanceId,
                        instanceIndex,
                        pointCount,
                        {}};
                    std::memcpy(
                        identity.transform.data(),
                        data + offset + 80,
                        sizeof(identity.transform));
                    result.mesh_identities.push_back(std::move(identity));
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
                    else if (path == BasisCurvesPath ||
                             path == VaryingWidthCurvesPath ||
                             path == UniformWidthCurvesPath ||
                             path == RightHandedMeshPath ||
                             path == LeftHandedMeshPath ||
                             path == LeftHandedFaceVaryingMeshPath)
                    {
                        ParsedCurves& curves = path == BasisCurvesPath
                            ? result.basis_curves
                            : (path == VaryingWidthCurvesPath
                                ? result.varying_width_curves
                                : (path == UniformWidthCurvesPath
                                    ? result.uniform_width_curves
                                    : (path == RightHandedMeshPath
                                        ? result.right_handed_mesh
                                        : (path == LeftHandedMeshPath
                                            ? result.left_handed_mesh
                                            : result
                                                .left_handed_face_varying_mesh))));
                        curves.found = true;
                        curves.topology_kind = topologyKind;
                        curves.triangle_count = triangleCount;
                        curves.subprims = std::move(subprims);
                        curves.attributes = std::move(attributes);
                        curves.points.resize(
                            static_cast<size_t>(pointCount) * 3);
                        for (size_t component = 0;
                             component < curves.points.size();
                             ++component)
                        {
                            if (!ReadValue(
                                    data,
                                    size,
                                    pointsOffset +
                                        (component * sizeof(float)),
                                    &curves.points[component]))
                            {
                                result.mesh_identity_valid = false;
                                break;
                            }
                        }
                        curves.indices.resize(indexCount);
                        const size_t indicesOffset = pointsOffset + pointBytes;
                        for (uint32_t index = 0; index < indexCount; ++index)
                        {
                            if (!ReadValue(
                                    data,
                                    size,
                                    indicesOffset +
                                        (static_cast<size_t>(index) *
                                         sizeof(uint32_t)),
                                    &curves.indices[index]))
                            {
                                result.mesh_identity_valid = false;
                                break;
                            }
                        }
                    }
                    else if (path == BlendShapeMeshPath && pointCount > 0)
                    {
                        result.found_blend_shape_mesh = true;
                        ReadValue(
                            data,
                            size,
                            pointsOffset,
                            &result.blend_shape_first_x);
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
                int32_t removedInstanceIndex = 0;
                ReadValue(data, size, offset + 16, &removedInstanceIndex);
                result.remove_paths.push_back(path);
                result.remove_instance_indices.push_back(removedInstanceIndex);
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
            std::vector<std::tuple<uint32_t, uint32_t, std::string>> textures;
            for (uint32_t texture = 0; valid && texture < textureCount; ++texture)
            {
                uint32_t parameter = 0;
                uint32_t assetSize = 0;
                uint32_t uvSize = 0;
                uint32_t componentCount = 0;
                uint32_t outputChannel = 0;
                uint32_t compositeOp = 0;
                const size_t entry = offset + cursor;
                valid = ReadValue(data, size, entry, &parameter) &&
                    ReadValue(data, size, entry + 16, &assetSize) &&
                    ReadValue(data, size, entry + 20, &uvSize) &&
                    ReadValue(data, size, entry + 24, &componentCount) &&
                    ReadValue(data, size, entry + 76, &outputChannel) &&
                    ReadValue(data, size, entry + 80, &compositeOp) &&
                    compositeOp <= OPENUSD_SILK_COMPOSITE_MIX &&
                    assetSize != 0 &&
                    outputChannel <= OPENUSD_SILK_TEXTURE_CHANNEL_RGB &&
                    (outputChannel == OPENUSD_SILK_TEXTURE_CHANNEL_RGB
                            ? componentCount == 3
                            : componentCount == 1);
                if (valid)
                {
                    std::string asset(
                        reinterpret_cast<const char*>(data + entry + 88),
                        assetSize);
                    if (texture == 0)
                    {
                        textureParameter = parameter;
                        textureAsset = asset;
                        textureUv.assign(
                            reinterpret_cast<const char*>(
                                data + entry + 88 + assetSize),
                            uvSize);
                    }
                    textures.emplace_back(parameter, outputChannel, std::move(asset));
                }
                valid = valid &&
                    AddSize(&cursor, 88) &&
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
            std::array<float, 6> uvTransform{};
            for (size_t element = 0; valid && element < uvTransform.size(); ++element)
            {
                valid = ReadValue(
                    data,
                    size,
                    offset + cursor + (element * sizeof(float)),
                    &uvTransform[element]);
            }
            valid = valid && AddSize(&cursor, 6 * sizeof(float));
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
                result.material_textures = std::move(textures);
                result.material_uv_transform = uvTransform;
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

/// A prototype that owns only part of an instancer publishes a sparse index
/// set, so its ABI v8 payload record is the lowest index it owns and not
/// necessarily index zero. Nothing about the wire may depend on a zero-indexed
/// record existing.
bool VerifySparsePrototypeInstanceSerialization()
{
    constexpr char SparsePath[] = "/Sparse";
    HdSilkSceneState state;

    // Proto indices [0, 1, 0, 1] give this prototype instancer instances 1
    // and 3; the payload rides on index 1.
    const int32_t owned[] = {1, 3};
    std::vector<HdSilkMeshRecord> instances;
    for (size_t position = 0; position < 2; ++position)
    {
        HdSilkMeshRecord record = MakeSceneStateRecord(SparsePath, 9);
        record.instanceId = 77;
        record.instanceIndex = owned[position];
        record.transform[3] = static_cast<double>(owned[position]);
        if (position != 0)
        {
            record.points.clear();
            record.indices.clear();
            record.triangleSubprims.clear();
            record.attributes.clear();
        }
        instances.push_back(std::move(record));
    }
    state.ReplaceMeshInstances(SparsePath, std::move(instances));

    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    return commandCount == 3 &&
        page.mesh_upsert_count == 2 &&
        page.mesh_remove_count == 0 &&
        page.mesh_identities.size() == 2 &&
        page.mesh_identities[0].instance_index == 1 &&
        page.mesh_identities[1].instance_index == 3 &&
        page.mesh_point_counts == std::vector<uint32_t>({3u, 0u}) &&
        page.mesh_index_counts == std::vector<uint32_t>({3u, 0u});
}

/// A path whose payload record cannot be serialized must be dropped whole.
/// ABI v8 makes the records of one path interdependent, so appending the later
/// instance references after the payload record was rejected would publish
/// references that no consumer can ever resolve. Sibling paths are independent
/// and must still serialize.
bool VerifyRejectedPayloadDropsWholePath()
{
    constexpr char BrokenPath[] = "/Broken";
    constexpr char HealthyPath[] = "/Healthy";
    HdSilkSceneState state;

    std::vector<HdSilkMeshRecord> broken;
    for (int32_t index = 0; index < 3; ++index)
    {
        HdSilkMeshRecord record = MakeSceneStateRecord(BrokenPath, 11);
        record.instanceId = 21;
        record.instanceIndex = index;
        if (index == 0)
        {
            // The payload record alone is malformed: one subprim index short.
            record.triangleSubprims.clear();
        }
        else
        {
            record.points.clear();
            record.indices.clear();
            record.triangleSubprims.clear();
            record.attributes.clear();
        }
        broken.push_back(std::move(record));
    }
    state.ReplaceMeshInstances(BrokenPath, std::move(broken));
    ReplaceSingleMesh(state, HealthyPath, MakeSceneStateRecord(HealthyPath, 12));

    const uint64_t rejectedBefore = HdSilkSceneState::GetRejectedMeshCount();
    uint32_t commandCount = 0;
    const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
    const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
    if (commandCount != 2 ||
        page.mesh_upsert_count != 1 ||
        page.mesh_identities.size() != 1 ||
        page.mesh_identities[0].path != HealthyPath ||
        HdSilkSceneState::GetRejectedMeshCount() <= rejectedBefore)
    {
        return false;
    }

    // A later instance reference failing drops the path just as completely:
    // the payload record has no meaning without the instances that reuse it,
    // and a torn path is exactly what this rule exists to prevent. Rolling the
    // whole path back leaves every consumer on the last version it retained.
    HdSilkSceneState later;
    std::vector<HdSilkMeshRecord> records;
    for (int32_t index = 0; index < 3; ++index)
    {
        HdSilkMeshRecord record = MakeSceneStateRecord(BrokenPath, 11);
        record.instanceId = 21;
        record.instanceIndex = index;
        if (index != 0)
        {
            record.points.clear();
            record.indices.clear();
            record.triangleSubprims.clear();
            record.attributes.clear();
        }
        if (index == 2)
        {
            record.cullStyle = 99;
        }
        records.push_back(std::move(record));
    }
    later.ReplaceMeshInstances(BrokenPath, std::move(records));
    const std::vector<uint8_t> laterBytes =
        later.BuildPage(nullptr, &commandCount);
    const ParsedPage laterPage =
        ParseCommands(laterBytes.data(), laterBytes.size());
    return commandCount == 1 &&
        laterPage.mesh_upsert_count == 0;
}

/// A malformed record must be rejected before anything indexes into it.
///
/// ApplyDrawMode and ApplyComplexity both dereference record.indices into
/// record.points and into vertex attribute data. Wireframe draw mode turns a
/// triangle list into a line list carrying the authored index values through
/// unchanged, and medium complexity then subdivides every line by reading both
/// endpoints' positions and interpolating every VERTEX attribute at the
/// subdivision parameter. An out-of-range index therefore reads past the end of
/// both arrays before the transformed record is ever validated. Validating the
/// record as published closes that, and this case is the proof: a three-point
/// triangle whose third index is 4096, under wireframe and medium complexity,
/// must produce a page with no MESH_UPSERT and no crash. It runs under the
/// address sanitiser in the fuzz job, where an out-of-bounds read is fatal
/// rather than merely wrong.
bool VerifyMalformedRecordIsRejectedBeforeTransforms()
{
    const uint32_t drawModes[] = {
        OPENUSD_SILK_DRAW_MODE_WIREFRAME,
        OPENUSD_SILK_DRAW_MODE_HIDDEN_SURFACE_WIREFRAME,
        OPENUSD_SILK_DRAW_MODE_POINTS};
    for (uint32_t drawMode : drawModes)
    {
        HdSilkSceneState state;
        HdSilkMeshRecord record = MakeSceneStateRecord("/HighIndex", 31);
        record.indices = {0, 1, 4096};
        HdSilkMeshAttribute normals;
        normals.name = "normals";
        normals.semantic = OPENUSD_SILK_ATTRIBUTE_NORMAL;
        normals.componentCount = 3;
        normals.interpolation = OPENUSD_SILK_INTERPOLATION_VERTEX;
        normals.data = {
            0.0F, 0.0F, 1.0F,
            0.0F, 0.0F, 1.0F,
            0.0F, 0.0F, 1.0F};
        record.attributes.push_back(std::move(normals));
        ReplaceSingleMesh(state, "/HighIndex", std::move(record));

        // A healthy sibling proves the rejection is scoped to the bad path
        // rather than aborting the page.
        ReplaceSingleMesh(state, "/Healthy", MakeSceneStateRecord("/Healthy", 32));

        state.SetDrawMode(drawMode);
        state.SetComplexity(OPENUSD_SILK_COMPLEXITY_MEDIUM);

        const uint64_t rejectedBefore = HdSilkSceneState::GetRejectedMeshCount();
        uint32_t commandCount = 0;
        const std::vector<uint8_t> bytes = state.BuildPage(nullptr, &commandCount);
        const ParsedPage page = ParseCommands(bytes.data(), bytes.size());
        if (commandCount != 2 ||
            page.mesh_upsert_count != 1 ||
            page.mesh_identities.size() != 1 ||
            page.mesh_identities[0].path != "/Healthy" ||
            HdSilkSceneState::GetRejectedMeshCount() <= rejectedBefore)
        {
            return false;
        }
    }
    return true;
}

/// Complexity subdivides line and point topology, and a triangle list becomes
/// one of those on the way to the wire whenever the draw mode converts it. A
/// complexity change while wireframe or points draw mode is active must
/// therefore republish those records too; dirtying only the records already
/// stored as lines or points leaves every converted record at the previous
/// density until something else happens to dirty it.
bool VerifyComplexityDirtiesConvertedTriangles()
{
    struct ConvertingMode
    {
        uint32_t drawMode;
        uint32_t topologyKind;
        uint32_t lowTriangleCount;
    };
    const ConvertingMode modes[] = {
        {OPENUSD_SILK_DRAW_MODE_WIREFRAME, OPENUSD_SILK_TOPOLOGY_LINE_LIST, 3},
        {OPENUSD_SILK_DRAW_MODE_HIDDEN_SURFACE_WIREFRAME,
            OPENUSD_SILK_TOPOLOGY_LINE_LIST,
            3},
        {OPENUSD_SILK_DRAW_MODE_POINTS, OPENUSD_SILK_TOPOLOGY_POINT_LIST, 3}};
    for (const ConvertingMode& mode : modes)
    {
        HdSilkSceneState state;
        ReplaceSingleMesh(state, "/Converted", MakeSceneStateRecord("/Converted", 41));
        state.SetDrawMode(mode.drawMode);

        uint32_t commandCount = 0;
        const std::vector<uint8_t> lowBytes =
            state.BuildPage(nullptr, &commandCount);
        const ParsedPage low = ParseCommands(lowBytes.data(), lowBytes.size());
        if (low.mesh_upsert_count != 1 ||
            low.mesh_topology_kinds.size() != 1 ||
            low.mesh_topology_kinds[0] != mode.topologyKind ||
            low.mesh_triangle_counts[0] != mode.lowTriangleCount)
        {
            return false;
        }

        // The record is stored as a triangle list, so it is only republished if
        // the complexity change accounts for the draw-mode conversion.
        state.SetComplexity(OPENUSD_SILK_COMPLEXITY_MEDIUM);
        const std::vector<uint8_t> mediumBytes =
            state.BuildPage(nullptr, &commandCount);
        const ParsedPage medium =
            ParseCommands(mediumBytes.data(), mediumBytes.size());
        if (medium.mesh_upsert_count != 1 ||
            medium.mesh_topology_kinds[0] != mode.topologyKind ||
            medium.mesh_triangle_counts[0] != mode.lowTriangleCount * 2)
        {
            return false;
        }

        // And back down again, so the dirtying is not a one-way ratchet.
        state.SetComplexity(OPENUSD_SILK_COMPLEXITY_LOW);
        const std::vector<uint8_t> restoredBytes =
            state.BuildPage(nullptr, &commandCount);
        const ParsedPage restored =
            ParseCommands(restoredBytes.data(), restoredBytes.size());
        if (restored.mesh_upsert_count != 1 ||
            restored.mesh_triangle_counts[0] != mode.lowTriangleCount)
        {
            return false;
        }
    }
    return true;
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
        VerifyInstancedSceneStateSerialization() &&
        VerifySparsePrototypeInstanceSerialization() &&
        VerifyRejectedPayloadDropsWholePath() &&
        VerifyMalformedRecordIsRejectedBeforeTransforms() &&
        VerifyComplexityDirtiesConvertedTriangles();
}

openusd_status Sync(
    openusd_silk_session* session,
    ParsedPage* parsed,
    openusd_error_buffer* error,
    const openusd_render_camera* requestedCamera = nullptr,
    double timeCode = 0.0,
    const uint32_t* requestedComplexity = nullptr)
{
    const openusd_render_camera automatic = AutomaticCamera();
    const openusd_render_camera* camera =
        requestedCamera == nullptr ? &automatic : requestedCamera;
    openusd_silk_page* page = nullptr;
    openusd_silk_page_view view{};
    view.struct_size = sizeof(openusd_silk_page_view);
    const openusd_status status = requestedComplexity == nullptr
        ? openusd_silk_session_sync(
            session,
            64,
            64,
            timeCode,
            camera,
            &page,
            &view,
            error)
        : openusd_silk_session_sync_with_complexity(
            session,
            64,
            64,
            timeCode,
            camera,
            *requestedComplexity,
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
        openusd_shade_create_output(
            stage,
            TextureShaderPath,
            "b",
            OPENUSD_SHADE_VALUE_FLOAT,
            error) == OPENUSD_STATUS_OK &&
        openusd_shade_create_input(
            stage,
            SurfaceShaderPath,
            "metallic",
            OPENUSD_SHADE_VALUE_FLOAT,
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
        // The same texture prim also drives metallic, from a different output.
        // Only the output token distinguishes the two connections, so this is
        // what proves the token survives resolution rather than being inferred.
        openusd_shade_connect(
            stage,
            SurfaceShaderPath,
            "metallic",
            OPENUSD_SHADE_ATTRIBUTE_INPUT,
            TextureShaderPath,
            "b",
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

/// Authors one linear segmented BasisCurves prim. The generic vec3f setter
/// creates Float3Array, while a typed BasisCurves prim predeclares points as
/// Point3fArray, so the arrays are authored before the schema is applied and
/// the schema tokens are re-authored afterwards. That keeps the probe on the
/// public C ABI without a test-only BasisCurves authoring entry point.
bool AuthorLinearSegmentedCurves(
    openusd_stage* stage,
    const char* path,
    const openusd_vec3f* points,
    size_t pointCount,
    const int32_t* counts,
    size_t countCount,
    const float* widths,
    size_t widthCount,
    openusd_error_buffer* error)
{
    return openusd_stage_define_prim(stage, path, "Scope", error) ==
            OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage, path, "type", "linear", 0, 0.0, error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage, path, "basis", "bezier", 0, 0.0, error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage, path, "wrap", "segmented", 0, 0.0, error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_vec3f_array(
            stage,
            path,
            "points",
            points,
            pointCount,
            0,
            0.0,
            error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_int32_array(
            stage,
            path,
            "curveVertexCounts",
            counts,
            countCount,
            0,
            0.0,
            error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_float_array(
            stage,
            path,
            "widths",
            widths,
            widthCount,
            0,
            0.0,
            error) == OPENUSD_STATUS_OK &&
        openusd_stage_define_prim(stage, path, "BasisCurves", error) ==
            OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage, path, "type", "linear", 0, 0.0, error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage, path, "basis", "bezier", 0, 0.0, error) == OPENUSD_STATUS_OK &&
        openusd_stage_set_token(
            stage, path, "wrap", "segmented", 0, 0.0, error) == OPENUSD_STATUS_OK;
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

    // Two curves whose four control points each author their own width.
    // UsdGeomCurves defaults the widths interpolation to "vertex", so this is
    // exactly the case that used to delete the prim from the page instead of
    // publishing it.
    const std::array<openusd_vec3f, 4> varyingPoints{
        openusd_vec3f{0.0F, 2.0F, 0.0F},
        openusd_vec3f{1.0F, 2.0F, 0.0F},
        openusd_vec3f{0.0F, 3.0F, 0.0F},
        openusd_vec3f{1.0F, 3.0F, 0.0F}};
    const std::array<int32_t, 2> varyingCounts{2, 2};
    const std::array<float, 4> varyingWidths{0.1F, 0.2F, 0.3F, 0.4F};

    // Two curves with one authored width each. UsdGeomCurves still defaults the
    // declared interpolation to "vertex", so the element count is what selects
    // uniform here, and both endpoints of a curve must receive that curve's
    // single value.
    const std::array<openusd_vec3f, 4> uniformPoints{
        openusd_vec3f{0.0F, 4.0F, 0.0F},
        openusd_vec3f{1.0F, 4.0F, 0.0F},
        openusd_vec3f{0.0F, 5.0F, 0.0F},
        openusd_vec3f{1.0F, 5.0F, 0.0F}};
    const std::array<int32_t, 2> uniformCounts{2, 2};
    const std::array<float, 2> uniformWidths{0.25F, 0.75F};

    return AuthorLinearSegmentedCurves(
            stage,
            BasisCurvesPath,
            points.data(),
            points.size(),
            counts.data(),
            counts.size(),
            widths.data(),
            widths.size(),
            error) &&
        AuthorLinearSegmentedCurves(
            stage,
            VaryingWidthCurvesPath,
            varyingPoints.data(),
            varyingPoints.size(),
            varyingCounts.data(),
            varyingCounts.size(),
            varyingWidths.data(),
            varyingWidths.size(),
            error) &&
        AuthorLinearSegmentedCurves(
            stage,
            UniformWidthCurvesPath,
            uniformPoints.data(),
            uniformPoints.size(),
            uniformCounts.data(),
            uniformCounts.size(),
            uniformWidths.data(),
            uniformWidths.size(),
            error);
}

const ParsedAttribute* FindAttribute(
    const std::vector<ParsedAttribute>& attributes,
    const char* name)
{
    for (const ParsedAttribute& attribute : attributes)
    {
        if (attribute.name == name)
        {
            return &attribute;
        }
    }
    return nullptr;
}

bool VerifyBasisCurvesTessellation(const ParsedPage& page)
{
    const std::vector<float> expectedPoints{
        0.0F, 0.0F, 0.0F,
        1.0F, 0.0F, 0.0F};
    const std::vector<uint32_t> expectedIndices{0, 1};
    const std::vector<uint32_t> expectedSubprims{0};
    const ParsedCurves& curves = page.basis_curves;
    const ParsedAttribute* widths = FindAttribute(curves.attributes, "widths");
    // A single authored width stays a one-element CONSTANT entry, so the
    // constant-width payload is exactly what it was before non-constant widths
    // were resolved.
    return curves.found &&
        curves.topology_kind == OPENUSD_SILK_TOPOLOGY_LINE_LIST &&
        curves.triangle_count == 1 &&
        curves.points == expectedPoints &&
        curves.indices == expectedIndices &&
        curves.subprims == expectedSubprims &&
        widths != nullptr &&
        widths->semantic == OPENUSD_SILK_ATTRIBUTE_WIDTH &&
        widths->componentCount == 1 &&
        widths->interpolation == OPENUSD_SILK_INTERPOLATION_CONSTANT &&
        widths->values == std::vector<float>{0.2F};
}

/// Proves authored per-control-point widths reach the wire resolved onto the
/// emitted line vertices, and that the curve is published at all: before width
/// interpolation was resolved, a four-element widths array made hdSilk drop the
/// whole prim, so this scene rendered nothing.
bool VerifyVaryingWidthCurves(const ParsedPage& page)
{
    const std::vector<float> expectedPoints{
        0.0F, 2.0F, 0.0F,
        1.0F, 2.0F, 0.0F,
        0.0F, 3.0F, 0.0F,
        1.0F, 3.0F, 0.0F};
    const std::vector<uint32_t> expectedIndices{0, 1, 2, 3};
    const std::vector<uint32_t> expectedSubprims{0, 1};
    const std::vector<float> expectedWidths{0.1F, 0.2F, 0.3F, 0.4F};
    const ParsedCurves& curves = page.varying_width_curves;
    const ParsedAttribute* widths = FindAttribute(curves.attributes, "widths");
    return curves.found &&
        curves.topology_kind == OPENUSD_SILK_TOPOLOGY_LINE_LIST &&
        curves.triangle_count == 2 &&
        curves.points == expectedPoints &&
        curves.indices == expectedIndices &&
        curves.subprims == expectedSubprims &&
        widths != nullptr &&
        widths->semantic == OPENUSD_SILK_ATTRIBUTE_WIDTH &&
        widths->componentCount == 1 &&
        widths->interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
        widths->elementCount == 4 &&
        widths->values == expectedWidths;
}

/// Proves per-curve widths are expanded onto both endpoints of every segment the
/// curve emits. Two curves, one authored width each: the first segment must
/// carry 0.25 twice and the second 0.75 twice, which no endpoint-selecting or
/// first-value-wins shortcut produces.
bool VerifyUniformWidthCurves(const ParsedPage& page)
{
    const std::vector<float> expectedPoints{
        0.0F, 4.0F, 0.0F,
        1.0F, 4.0F, 0.0F,
        0.0F, 5.0F, 0.0F,
        1.0F, 5.0F, 0.0F};
    const std::vector<uint32_t> expectedIndices{0, 1, 2, 3};
    const std::vector<uint32_t> expectedSubprims{0, 1};
    const std::vector<float> expectedWidths{0.25F, 0.25F, 0.75F, 0.75F};
    const ParsedCurves& curves = page.uniform_width_curves;
    const ParsedAttribute* widths = FindAttribute(curves.attributes, "widths");
    return curves.found &&
        curves.topology_kind == OPENUSD_SILK_TOPOLOGY_LINE_LIST &&
        curves.triangle_count == 2 &&
        curves.points == expectedPoints &&
        curves.indices == expectedIndices &&
        curves.subprims == expectedSubprims &&
        widths != nullptr &&
        widths->semantic == OPENUSD_SILK_ATTRIBUTE_WIDTH &&
        widths->componentCount == 1 &&
        widths->interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
        widths->elementCount == 4 &&
        widths->values == expectedWidths;
}

/// Pins the winding hdSilk emits for each authored USD "orientation".
///
/// USD authors face winding with `orientation`: rightHanded faces are wound
/// counter-clockwise seen from the front, leftHanded clockwise. Every backend
/// hdSilk targets rasterizes with counter-clockwise front faces, so the emitted
/// triangle list has to carry that distinction faithfully. It already does:
/// HdMeshUtil::ComputeTriangleIndices reverses the corners of a leftHanded face
/// itself, so this delegate must pass its output through untouched. That was
/// measured rather than assumed -- adding a reversal here, on the theory that
/// HdMeshUtil ignored orientation and left the convention to the renderer, made
/// the leftHanded quad publish the double-reversed order 2,0,1,2,3,0, which is
/// the inverted facing this case now locks out in both directions.
///
/// The two quads are byte-identical apart from that one token, so the emitted
/// indices are the only place the difference can appear. The third mesh carries
/// a face-varying primvar, which forces the expanded-topology path: that data
/// is one element per triangulated corner in the same HdMeshUtil order, so it
/// stays aligned with the triangles only while neither array is permuted alone.
/// All three live in hdsilk-probe-stage.usda rather than being authored here,
/// because primvar interpolation is attribute metadata and the C ABI exposes
/// prim metadata only.
bool VerifyOrientationWinding(const ParsedPage& page)
{
    const std::vector<float> expectedPoints{
        0.0F, 0.0F, 0.0F,
        1.0F, 0.0F, 0.0F,
        1.0F, 1.0F, 0.0F,
        0.0F, 1.0F, 0.0F};
    const std::vector<uint32_t> expectedSubprims{0, 0};

    const ParsedCurves& right = page.right_handed_mesh;
    const ParsedCurves& left = page.left_handed_mesh;
    if (!right.found || !left.found ||
        right.topology_kind != OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST ||
        left.topology_kind != OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST ||
        right.triangle_count != 2 || left.triangle_count != 2 ||
        right.points != expectedPoints || left.points != expectedPoints ||
        right.subprims != expectedSubprims ||
        left.subprims != expectedSubprims)
    {
        std::cerr << "hdSilk orientation probe: the two quads did not publish "
                  << "matching points or subprims.\n";
        return false;
    }

    // A rightHanded quad triangulates to (0, 1, 2) and (0, 2, 3). The
    // leftHanded quad emits the opposite winding, up to the cyclic rotation
    // HdMeshUtil happens to produce. That opposition is the invariant:
    // publishing the rightHanded order for both, or reversing an already
    // reversed leftHanded face, both break it.
    const std::vector<uint32_t> expectedRight{0, 1, 2, 0, 2, 3};
    const std::vector<uint32_t> expectedLeft{2, 1, 0, 2, 0, 3};
    if (right.indices != expectedRight || left.indices != expectedLeft)
    {
        std::cerr << "hdSilk orientation probe: expected rightHanded indices "
                  << "0,1,2,0,2,3 and leftHanded 2,1,0,2,0,3; got right=";
        for (uint32_t index : right.indices)
        {
            std::cerr << index << ',';
        }
        std::cerr << " left=";
        for (uint32_t index : left.indices)
        {
            std::cerr << index << ',';
        }
        std::cerr << "\n";
        return false;
    }

    // The face-varying mesh expands its topology: face-varying data is one
    // element per triangulated corner in HdMeshUtil's order, so the emitted
    // vertices are one per corner and the winding shows up in the point order
    // rather than in the indices. Every emitted vertex must carry the corner
    // value authored for the point it was expanded from. cornerId is authored
    // 10, 11, 12, 13 for points 0, 1, 2, 3, so this is a direct
    // point-to-corner cross-check that permuting one array without the other
    // cannot satisfy.
    const ParsedCurves& faceVarying = page.left_handed_face_varying_mesh;
    const ParsedAttribute* corner =
        FindAttribute(faceVarying.attributes, "cornerId");
    const std::vector<uint32_t> expandedIndices{0, 1, 2, 3, 4, 5};
    if (!faceVarying.found ||
        faceVarying.triangle_count != 2 ||
        faceVarying.indices != expandedIndices ||
        faceVarying.points.size() != 18 ||
        corner == nullptr ||
        corner->interpolation != OPENUSD_SILK_INTERPOLATION_VERTEX ||
        corner->componentCount != 1 ||
        corner->values.size() != 6)
    {
        std::cerr << "hdSilk orientation probe: the leftHanded face-varying "
                  << "quad did not publish six expanded corners. found="
                  << faceVarying.found
                  << " triangles=" << faceVarying.triangle_count
                  << " indices=" << faceVarying.indices.size()
                  << " pointFloats=" << faceVarying.points.size()
                  << " attributes=";
        for (const ParsedAttribute& attribute : faceVarying.attributes)
        {
            std::cerr << attribute.name << '(' << attribute.elementCount << ')';
        }
        std::cerr << "\n";
        return false;
    }
    for (size_t vertex = 0; vertex < 6; ++vertex)
    {
        const float x = faceVarying.points[vertex * 3];
        const float y = faceVarying.points[(vertex * 3) + 1];
        float expected = -1.0F;
        if (x == 0.0F && y == 0.0F)
        {
            expected = 10.0F;
        }
        else if (x == 1.0F && y == 0.0F)
        {
            expected = 11.0F;
        }
        else if (x == 1.0F && y == 1.0F)
        {
            expected = 12.0F;
        }
        else if (x == 0.0F && y == 1.0F)
        {
            expected = 13.0F;
        }
        if (corner->values[vertex] != expected)
        {
            std::cerr << "hdSilk orientation probe: expanded vertex " << vertex
                      << " at (" << x << ", " << y << ") carries corner "
                      << corner->values[vertex] << ", expected " << expected
                      << "\n";
            return false;
        }
    }

    return true;
}

bool NearlyEqual(const std::vector<float>& left, const std::vector<float>& right)
{
    if (left.size() != right.size())
    {
        return false;
    }
    for (size_t index = 0; index < left.size(); ++index)
    {
        if (std::fabs(left[index] - right[index]) > 1e-5F)
        {
            return false;
        }
    }
    return true;
}

/// Medium complexity halves every emitted segment, and a vertex attribute has to
/// be interpolated at the same parameter as the position rather than copied from
/// the nearer endpoint. The varying-width curve authors 0.1 -> 0.2 and 0.3 ->
/// 0.4, so a correct 2x subdivision publishes the midpoints 0.15 and 0.35;
/// endpoint selection would publish 0.1, 0.1, 0.2, 0.2 and a step function where
/// the authored data is a ramp.
bool VerifyMediumComplexityInterpolatesWidths(const ParsedPage& page)
{
    const std::vector<float> expectedPoints{
        0.0F, 2.0F, 0.0F,
        0.5F, 2.0F, 0.0F,
        0.5F, 2.0F, 0.0F,
        1.0F, 2.0F, 0.0F,
        0.0F, 3.0F, 0.0F,
        0.5F, 3.0F, 0.0F,
        0.5F, 3.0F, 0.0F,
        1.0F, 3.0F, 0.0F};
    const std::vector<uint32_t> expectedIndices{0, 1, 2, 3, 4, 5, 6, 7};
    const std::vector<uint32_t> expectedSubprims{0, 0, 1, 1};
    const std::vector<float> expectedWidths{
        0.10F, 0.15F,
        0.15F, 0.20F,
        0.30F, 0.35F,
        0.35F, 0.40F};
    const std::vector<float> expectedNormals{
        0.0F, 0.0F, 1.0F,
        0.0F, 0.0F, 1.0F,
        0.0F, 0.0F, 1.0F,
        0.0F, 0.0F, 1.0F,
        0.0F, 0.0F, 1.0F,
        0.0F, 0.0F, 1.0F,
        0.0F, 0.0F, 1.0F,
        0.0F, 0.0F, 1.0F};
    const ParsedCurves& curves = page.varying_width_curves;
    const ParsedAttribute* widths = FindAttribute(curves.attributes, "widths");
    const ParsedAttribute* normals = FindAttribute(curves.attributes, "normals");
    return curves.found &&
        curves.topology_kind == OPENUSD_SILK_TOPOLOGY_LINE_LIST &&
        curves.triangle_count == 4 &&
        NearlyEqual(curves.points, expectedPoints) &&
        curves.indices == expectedIndices &&
        curves.subprims == expectedSubprims &&
        widths != nullptr &&
        widths->interpolation == OPENUSD_SILK_INTERPOLATION_VERTEX &&
        widths->elementCount == 8 &&
        NearlyEqual(widths->values, expectedWidths) &&
        normals != nullptr &&
        normals->elementCount == 8 &&
        NearlyEqual(normals->values, expectedNormals);
}

/// Direct coverage of the width resolver and the line builder, without a render
/// index. UsdImaging never authors HdBasisCurvesTopology curve indices, so an
/// indexed topology cannot be reached from a stage; it is still a topology Hydra
/// may hand a delegate, and the vertex-width lookup has to follow the resolved
/// point index exactly as the position does.
bool VerifyCurveWidthResolution()
{
    const VtIntArray counts({2, 2});
    const VtIntArray indices({3, 2, 1, 0});
    const HdBasisCurvesTopology indexed(
        HdTokens->linear,
        HdTokens->bezier,
        HdTokens->segmented,
        counts,
        indices);
    const HdBasisCurvesTopology plain(
        HdTokens->linear,
        HdTokens->bezier,
        HdTokens->segmented,
        counts,
        VtIntArray());
    VtVec3fArray points(4);
    for (size_t point = 0; point < 4; ++point)
    {
        points[point] = GfVec3f(static_cast<float>(point), 0.0F, 0.0F);
    }

    // Indexed vertex widths are sized to the points array and are selected
    // through the same curve indices as the positions, so the emitted order is
    // the reversed authored order here.
    HdSilkCurveWidths indexedWidths;
    if (!HdSilkResolveCurveWidths(
            indexed,
            points.size(),
            VtValue(VtFloatArray({0.1F, 0.2F, 0.3F, 0.4F})),
            true,
            HdSilkCurveWidthInterpolation::Vertex,
            &indexedWidths) ||
        indexedWidths.interpolation != HdSilkCurveWidthInterpolation::Vertex)
    {
        return false;
    }
    HdSilkMeshRecord indexedRecord;
    if (!HdSilkBuildLinearSegmentedCurveLines(
            indexed, points, indexedWidths, &indexedRecord))
    {
        return false;
    }
    const ParsedAttribute* indexedWidthAttribute = nullptr;
    std::vector<ParsedAttribute> indexedAttributes;
    for (const HdSilkMeshAttribute& attribute : indexedRecord.attributes)
    {
        ParsedAttribute parsed;
        parsed.name = attribute.name;
        parsed.semantic = attribute.semantic;
        parsed.componentCount = attribute.componentCount;
        parsed.interpolation = attribute.interpolation;
        parsed.elementCount = static_cast<uint32_t>(
            attribute.data.size() / attribute.componentCount);
        parsed.values = attribute.data;
        indexedAttributes.push_back(std::move(parsed));
    }
    indexedWidthAttribute = FindAttribute(indexedAttributes, "widths");
    const std::vector<float> expectedIndexedPoints{
        3.0F, 0.0F, 0.0F,
        2.0F, 0.0F, 0.0F,
        1.0F, 0.0F, 0.0F,
        0.0F, 0.0F, 0.0F};
    if (indexedWidthAttribute == nullptr ||
        indexedRecord.points != expectedIndexedPoints ||
        !NearlyEqual(
            indexedWidthAttribute->values,
            std::vector<float>({0.4F, 0.3F, 0.2F, 0.1F})))
    {
        return false;
    }

    // An indexed topology sizes vertex widths to the points array, so the
    // flattened control-point count is not a substitute for it.
    if (HdSilkExpectedCurveWidthCount(
            indexed, 7, HdSilkCurveWidthInterpolation::Vertex) != 7 ||
        HdSilkExpectedCurveWidthCount(
            plain, 7, HdSilkCurveWidthInterpolation::Vertex) != 4 ||
        HdSilkExpectedCurveWidthCount(
            indexed, 7, HdSilkCurveWidthInterpolation::Uniform) != 2 ||
        HdSilkExpectedCurveWidthCount(
            indexed, 7, HdSilkCurveWidthInterpolation::Constant) != 1)
    {
        return false;
    }

    // An unindexed curve resolves a point by its flattened ordinal, so a widths
    // array parallel to a points array longer than the curves consume -- which
    // USD permits -- indexes identically and is accepted alongside the
    // control-point count. A shorter one is not: the builder would read past it.
    if (!HdSilkCurveWidthCountMatches(
            plain, 7, HdSilkCurveWidthInterpolation::Vertex, 7) ||
        !HdSilkCurveWidthCountMatches(
            plain, 7, HdSilkCurveWidthInterpolation::Vertex, 4) ||
        HdSilkCurveWidthCountMatches(
            plain, 3, HdSilkCurveWidthInterpolation::Vertex, 3) ||
        HdSilkCurveWidthCountMatches(
            indexed, 7, HdSilkCurveWidthInterpolation::Vertex, 4))
    {
        return false;
    }

    // The same case end to end: six points, four of which two curves consume,
    // and six authored widths. Before the points-sized count was accepted this
    // fell through to the default width and published a flat 1.0.
    VtVec3fArray longPoints(6);
    for (size_t point = 0; point < 6; ++point)
    {
        longPoints[point] = GfVec3f(static_cast<float>(point), 0.0F, 0.0F);
    }
    HdSilkCurveWidths trailing;
    if (!HdSilkResolveCurveWidths(
            plain,
            longPoints.size(),
            VtValue(VtFloatArray({0.1F, 0.2F, 0.3F, 0.4F, 0.5F, 0.6F})),
            true,
            HdSilkCurveWidthInterpolation::Vertex,
            &trailing) ||
        trailing.interpolation != HdSilkCurveWidthInterpolation::Vertex)
    {
        return false;
    }
    HdSilkMeshRecord trailingRecord;
    if (!HdSilkBuildLinearSegmentedCurveLines(
            plain, longPoints, trailing, &trailingRecord))
    {
        return false;
    }
    {
        std::vector<ParsedAttribute> parsedAttributes;
        for (const HdSilkMeshAttribute& attribute : trailingRecord.attributes)
        {
            ParsedAttribute parsed;
            parsed.name = attribute.name;
            parsed.semantic = attribute.semantic;
            parsed.componentCount = attribute.componentCount;
            parsed.interpolation = attribute.interpolation;
            parsed.elementCount = static_cast<uint32_t>(
                attribute.data.size() / attribute.componentCount);
            parsed.values = attribute.data;
            parsedAttributes.push_back(std::move(parsed));
        }
        const ParsedAttribute* trailingWidths =
            FindAttribute(parsedAttributes, "widths");
        if (trailingWidths == nullptr ||
            !NearlyEqual(
                trailingWidths->values,
                std::vector<float>({0.1F, 0.2F, 0.3F, 0.4F})))
        {
            return false;
        }
    }

    // A per-curve array is inferred as uniform even when the delegate declares
    // vertex, because only the element count can be right.
    HdSilkCurveWidths uniform;
    if (!HdSilkResolveCurveWidths(
            plain,
            points.size(),
            VtValue(VtFloatArray({0.25F, 0.75F})),
            true,
            HdSilkCurveWidthInterpolation::Vertex,
            &uniform) ||
        uniform.interpolation != HdSilkCurveWidthInterpolation::Uniform ||
        !NearlyEqual(uniform.values, std::vector<float>({0.25F, 0.75F})))
    {
        return false;
    }

    // Doubles are accepted, negatives clamp to zero, an empty value falls back
    // to the UsdGeomCurves default, and a non-finite or unexplainable array is
    // rejected so the caller can publish the default instead of guessing.
    HdSilkCurveWidths doubles;
    HdSilkCurveWidths empty;
    HdSilkCurveWidths rejected;

    // Half-precision widths reach the delegate whenever a scene index or a
    // delegate hands Hydra the half primvar it authored, and GfHalf converts to
    // float exactly for these values. Both the array and the scalar form are
    // covered; the scalar resolves as constant because one element can only be
    // that for a two-curve topology.
    HdSilkCurveWidths halfArray;
    HdSilkCurveWidths halfScalar;
    if (!HdSilkResolveCurveWidths(
            plain,
            points.size(),
            VtValue(VtHalfArray({
                GfHalf(-1.0F),
                GfHalf(0.5F),
                GfHalf(0.25F),
                GfHalf(0.125F)})),
            true,
            HdSilkCurveWidthInterpolation::Vertex,
            &halfArray) ||
        halfArray.interpolation != HdSilkCurveWidthInterpolation::Vertex ||
        !NearlyEqual(
            halfArray.values,
            std::vector<float>({0.0F, 0.5F, 0.25F, 0.125F})) ||
        !HdSilkResolveCurveWidths(
            plain,
            points.size(),
            VtValue(GfHalf(0.75F)),
            false,
            HdSilkCurveWidthInterpolation::Constant,
            &halfScalar) ||
        halfScalar.interpolation != HdSilkCurveWidthInterpolation::Constant ||
        !NearlyEqual(halfScalar.values, std::vector<float>({0.75F})))
    {
        return false;
    }

    return HdSilkResolveCurveWidths(
            plain,
            points.size(),
            VtValue(VtDoubleArray({-1.0, 2.0, 3.0, 4.0})),
            false,
            HdSilkCurveWidthInterpolation::Constant,
            &doubles) &&
        doubles.interpolation == HdSilkCurveWidthInterpolation::Vertex &&
        NearlyEqual(doubles.values, std::vector<float>({0.0F, 2.0F, 3.0F, 4.0F})) &&
        HdSilkResolveCurveWidths(
            plain,
            points.size(),
            VtValue(),
            false,
            HdSilkCurveWidthInterpolation::Constant,
            &empty) &&
        empty.interpolation == HdSilkCurveWidthInterpolation::Constant &&
        NearlyEqual(empty.values, std::vector<float>({1.0F})) &&
        !HdSilkResolveCurveWidths(
            plain,
            points.size(),
            VtValue(VtFloatArray({0.1F, 0.2F, 0.3F})),
            false,
            HdSilkCurveWidthInterpolation::Constant,
            &rejected) &&
        !HdSilkResolveCurveWidths(
            plain,
            points.size(),
            VtValue(VtFloatArray({
                0.1F,
                std::numeric_limits<float>::quiet_NaN(),
                0.3F,
                0.4F})),
            true,
            HdSilkCurveWidthInterpolation::Vertex,
            &rejected);
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

/// Builds a MaterialX standard-surface network whose base-colour image reads its
/// texture coordinates through a place2d node carrying the supplied constant
/// inputs. When "connectOffset" is set, the place2d offset is driven by another
/// node instead, which is exactly the per-pixel case this projection rejects.
HdMaterialNetworkMap
MakePlace2dNetwork(
    const std::map<TfToken, VtValue>& place2dInputs,
    bool connectOffset)
{
    HdMaterialNode primvar;
    primvar.path = SdfPath("/World/Placed/Primvar");
    primvar.identifier = TfToken("ND_geompropvalue_vector2");
    primvar.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode place2d;
    place2d.path = SdfPath("/World/Placed/Place2d");
    place2d.identifier = TfToken("ND_place2d_vector2");
    place2d.parameters = place2dInputs;

    HdMaterialNode image;
    image.path = SdfPath("/World/Placed/Image");
    image.identifier = TfToken("ND_image_color3");
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode driver;
    driver.path = SdfPath("/World/Placed/Driver");
    driver.identifier = TfToken("ND_constant_vector2");

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Placed/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {primvar, place2d, image, driver, surface};
    network.relationships.push_back(
        {primvar.path, TfToken("out"), place2d.path, TfToken("texcoord")});
    network.relationships.push_back(
        {place2d.path, TfToken("out"), image.path, TfToken("texcoord")});
    network.relationships.push_back(
        {image.path, TfToken("out"), surface.path, TfToken("base_color")});
    if (connectOffset)
    {
        network.relationships.push_back(
            {driver.path, TfToken("out"), place2d.path, TfToken("offset")});
    }
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return map;
}

/// Builds a MaterialX standard-surface network whose base-colour image reads its
/// texture coordinates through two chained place2d nodes. The composed affine is
/// the only correct answer: applying either node alone, or the outer node over
/// the default primvar, produces a different matrix.
HdMaterialNetworkMap
MakeChainedPlace2dNetwork()
{
    HdMaterialNode primvar;
    primvar.path = SdfPath("/World/Placed/Primvar");
    primvar.identifier = TfToken("ND_geompropvalue_vector2");
    primvar.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode inner;
    inner.path = SdfPath("/World/Placed/Inner");
    inner.identifier = TfToken("ND_place2d_vector2");
    inner.parameters[TfToken("scale")] = VtValue(GfVec2f(2.0F, 2.0F));

    HdMaterialNode outer;
    outer.path = SdfPath("/World/Placed/Outer");
    outer.identifier = TfToken("ND_place2d_vector2");
    outer.parameters[TfToken("offset")] = VtValue(GfVec2f(0.25F, 0.0F));

    HdMaterialNode image;
    image.path = SdfPath("/World/Placed/Image");
    image.identifier = TfToken("ND_image_color3");
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Placed/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {primvar, inner, outer, image, surface};
    network.relationships.push_back(
        {primvar.path, TfToken("out"), inner.path, TfToken("texcoord")});
    network.relationships.push_back(
        {inner.path, TfToken("out"), outer.path, TfToken("texcoord")});
    network.relationships.push_back(
        {outer.path, TfToken("out"), image.path, TfToken("texcoord")});
    network.relationships.push_back(
        {image.path, TfToken("out"), surface.path, TfToken("base_color")});
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return map;
}

/// Builds a MaterialX standard-surface network whose base-colour image reads its
/// coordinates through a place2d that sits behind a per-pixel vector2 multiply.
/// Folding only the place2d would render the outer transform over coordinates
/// the multiply never produced.
HdMaterialNetworkMap
MakePlace2dBehindUnsupportedNodeNetwork()
{
    HdMaterialNode primvar;
    primvar.path = SdfPath("/World/Placed/Primvar");
    primvar.identifier = TfToken("ND_geompropvalue_vector2");
    primvar.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode multiply;
    multiply.path = SdfPath("/World/Placed/Multiply");
    multiply.identifier = TfToken("ND_multiply_vector2");
    multiply.parameters[TfToken("in2")] = VtValue(GfVec2f(3.0F, 5.0F));

    HdMaterialNode place2d;
    place2d.path = SdfPath("/World/Placed/Place2d");
    place2d.identifier = TfToken("ND_place2d_vector2");
    place2d.parameters[TfToken("offset")] = VtValue(GfVec2f(0.25F, 0.5F));

    HdMaterialNode image;
    image.path = SdfPath("/World/Placed/Image");
    image.identifier = TfToken("ND_image_color3");
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Placed/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {primvar, multiply, place2d, image, surface};
    network.relationships.push_back(
        {primvar.path, TfToken("out"), multiply.path, TfToken("in1")});
    network.relationships.push_back(
        {multiply.path, TfToken("out"), place2d.path, TfToken("texcoord")});
    network.relationships.push_back(
        {place2d.path, TfToken("out"), image.path, TfToken("texcoord")});
    network.relationships.push_back(
        {image.path, TfToken("out"), surface.path, TfToken("base_color")});
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return map;
}

/// Builds a UsdPreviewSurface network whose diffuse texture reads its
/// coordinates through a UsdTransform2d. When "connectReader" is false the
/// transform has no upstream coordinate node at all, which is the case that must
/// be rejected rather than resolved to the default primvar.
HdMaterialNetworkMap
MakeTransform2dNetwork(bool connectReader)
{
    HdMaterialNode reader;
    reader.path = SdfPath("/World/Preview/Reader");
    reader.identifier = TfToken("UsdPrimvarReader_float2");
    reader.parameters[TfToken("varname")] = VtValue(TfToken("uvSet1"));

    HdMaterialNode transform;
    transform.path = SdfPath("/World/Preview/Transform");
    transform.identifier = TfToken("UsdTransform2d");
    transform.parameters[TfToken("rotation")] = VtValue(90.0F);
    transform.parameters[TfToken("scale")] = VtValue(GfVec2f(2.0F, 1.0F));
    transform.parameters[TfToken("translation")] = VtValue(GfVec2f(0.5F, 0.25F));

    HdMaterialNode texture;
    texture.path = SdfPath("/World/Preview/Texture");
    texture.identifier = TfToken("UsdUVTexture");
    texture.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/preview-basecolor.png"));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Preview/Surface");
    surface.identifier = TfToken("UsdPreviewSurface");

    HdMaterialNetwork network;
    network.nodes = {reader, transform, texture, surface};
    if (connectReader)
    {
        network.relationships.push_back(
            {reader.path, TfToken("result"), transform.path, TfToken("in")});
    }
    network.relationships.push_back(
        {transform.path, TfToken("result"), texture.path, TfToken("st")});
    network.relationships.push_back(
        {texture.path, TfToken("rgb"), surface.path, TfToken("diffuseColor")});
    network.primvars = {TfToken("uvSet1")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    return map;
}

/// Builds a MaterialX standard-surface network with a transformed base-colour
/// image and an untransformed normal map, which is the ordinary authoring shape
/// this renderer's single coordinate stream per material cannot serve.
HdMaterialNetworkMap
MakeDivergentUvChainNetwork()
{
    HdMaterialNode baseCoordinate;
    baseCoordinate.path = SdfPath("/World/Placed/BasePrimvar");
    baseCoordinate.identifier = TfToken("ND_geompropvalue_vector2");
    baseCoordinate.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode place2d;
    place2d.path = SdfPath("/World/Placed/Place2d");
    place2d.identifier = TfToken("ND_place2d_vector2");
    place2d.parameters[TfToken("scale")] = VtValue(GfVec2f(2.0F, 2.0F));
    place2d.parameters[TfToken("offset")] = VtValue(GfVec2f(0.25F, 0.5F));

    HdMaterialNode baseImage;
    baseImage.path = SdfPath("/World/Placed/BaseImage");
    baseImage.identifier = TfToken("ND_image_color3");
    baseImage.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode normalCoordinate;
    normalCoordinate.path = SdfPath("/World/Placed/NormalPrimvar");
    normalCoordinate.identifier = TfToken("ND_geompropvalue_vector2");
    normalCoordinate.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode normalImage;
    normalImage.path = SdfPath("/World/Placed/NormalImage");
    normalImage.identifier = TfToken("ND_image_vector3");
    normalImage.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-normal.png"));

    HdMaterialNode normalMap;
    normalMap.path = SdfPath("/World/Placed/NormalMap");
    normalMap.identifier = TfToken("ND_normalmap");

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Placed/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {
        baseCoordinate,
        place2d,
        baseImage,
        normalCoordinate,
        normalImage,
        normalMap,
        surface};
    network.relationships.push_back(
        {baseCoordinate.path, TfToken("out"), place2d.path, TfToken("texcoord")});
    network.relationships.push_back(
        {place2d.path, TfToken("out"), baseImage.path, TfToken("texcoord")});
    network.relationships.push_back(
        {baseImage.path, TfToken("out"), surface.path, TfToken("base_color")});
    network.relationships.push_back(
        {normalCoordinate.path,
         TfToken("out"),
         normalImage.path,
         TfToken("texcoord")});
    network.relationships.push_back(
        {normalImage.path, TfToken("out"), normalMap.path, TfToken("in")});
    network.relationships.push_back(
        {normalMap.path, TfToken("out"), surface.path, TfToken("normal")});
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return map;
}

/// Builds a MaterialX standard-surface network whose base-colour and normal-map
/// images read two different primvars, with no UV transform anywhere.
///
/// This is the primvar counterpart of MakeDivergentUvChainNetwork, and the
/// no-transform part is deliberate: both entries carry the identity affine, so
/// only the primvar can distinguish them. Reversing the order proves the
/// material stream is the one the first texture in the fixed input order states
/// rather than whichever primvar happens to sort first.
HdMaterialNetworkMap
MakeDivergentUvPrimvarNetwork(bool baseReadsFirstSet)
{
    HdMaterialNode baseCoordinate;
    baseCoordinate.path = SdfPath("/World/Streams/BasePrimvar");
    baseCoordinate.identifier = TfToken("ND_geompropvalue_vector2");
    baseCoordinate.parameters[TfToken("geomprop")] =
        VtValue(std::string(baseReadsFirstSet ? "uvSet0" : "uvSet1"));

    HdMaterialNode baseImage;
    baseImage.path = SdfPath("/World/Streams/BaseImage");
    baseImage.identifier = TfToken("ND_image_color3");
    baseImage.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode normalCoordinate;
    normalCoordinate.path = SdfPath("/World/Streams/NormalPrimvar");
    normalCoordinate.identifier = TfToken("ND_geompropvalue_vector2");
    normalCoordinate.parameters[TfToken("geomprop")] =
        VtValue(std::string(baseReadsFirstSet ? "uvSet1" : "uvSet0"));

    HdMaterialNode normalImage;
    normalImage.path = SdfPath("/World/Streams/NormalImage");
    normalImage.identifier = TfToken("ND_image_vector3");
    normalImage.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-normal.png"));

    HdMaterialNode normalMap;
    normalMap.path = SdfPath("/World/Streams/NormalMap");
    normalMap.identifier = TfToken("ND_normalmap");

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Streams/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {
        baseCoordinate,
        baseImage,
        normalCoordinate,
        normalImage,
        normalMap,
        surface};
    network.relationships.push_back(
        {baseCoordinate.path, TfToken("out"), baseImage.path, TfToken("texcoord")});
    network.relationships.push_back(
        {baseImage.path, TfToken("out"), surface.path, TfToken("base_color")});
    network.relationships.push_back(
        {normalCoordinate.path,
         TfToken("out"),
         normalImage.path,
         TfToken("texcoord")});
    network.relationships.push_back(
        {normalImage.path, TfToken("out"), normalMap.path, TfToken("in")});
    network.relationships.push_back(
        {normalMap.path, TfToken("out"), surface.path, TfToken("normal")});
    network.primvars = {TfToken("uvSet0"), TfToken("uvSet1")};

    HdMaterialNetworkMap streamMap;
    streamMap.map[HdMaterialTerminalTokens->surface] = network;
    streamMap.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return streamMap;
}

/// Builds the same two-image shape with both images on one primvar, which must
/// keep both entries. Without it the divergence case above would pass just as
/// well against a projection that dropped every normal map.
HdMaterialNetworkMap
MakeSharedUvPrimvarNetwork()
{
    HdMaterialNetworkMap shared = MakeDivergentUvPrimvarNetwork(true);
    HdMaterialNetwork& network = shared.map[HdMaterialTerminalTokens->surface];
    for (HdMaterialNode& node : network.nodes)
    {
        if (node.path == SdfPath("/World/Streams/NormalPrimvar"))
        {
            node.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));
        }
    }
    return shared;
}

/// Builds a MaterialX standard-surface network whose base colour is an image
/// driven through a chain of constant arithmetic. The chain is authored as
/// mix(bg = constant, fg = multiply(image, constant), mix = constant) so both the
/// multiply and the mix folds are exercised together.
HdMaterialNetworkMap
MakeAffineOverImageNetwork(
    const TfToken& surfaceInput,
    const TfToken& imageIdentifier,
    const VtValue& multiplyFactor,
    const VtValue& mixBackground,
    float mixFactor)
{
    HdMaterialNode primvar;
    primvar.path = SdfPath("/World/Arith/Primvar");
    primvar.identifier = TfToken("ND_geompropvalue_vector2");
    primvar.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode image;
    image.path = SdfPath("/World/Arith/Image");
    image.identifier = imageIdentifier;
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode multiply;
    multiply.path = SdfPath("/World/Arith/Multiply");
    multiply.identifier = TfToken("ND_multiply_color3");
    multiply.parameters[TfToken("in2")] = multiplyFactor;

    HdMaterialNode mix;
    mix.path = SdfPath("/World/Arith/Mix");
    mix.identifier = TfToken("ND_mix_color3");
    mix.parameters[TfToken("bg")] = mixBackground;
    mix.parameters[TfToken("mix")] = VtValue(mixFactor);

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Arith/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {primvar, image, multiply, mix, surface};
    network.relationships.push_back(
        {primvar.path, TfToken("out"), image.path, TfToken("texcoord")});
    network.relationships.push_back(
        {image.path, TfToken("out"), multiply.path, TfToken("in1")});
    network.relationships.push_back(
        {multiply.path, TfToken("out"), mix.path, TfToken("fg")});
    network.relationships.push_back(
        {mix.path, TfToken("out"), surface.path, surfaceInput});
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return map;
}

/// Builds a MaterialX standard-surface network whose base colour multiplies two
/// images together, which the renderer's one-texture-per-input binding cannot
/// serve and which must therefore be reported rather than half-folded.
HdMaterialNetworkMap
MakeTwoImageProductNetwork()
{
    HdMaterialNode primvar;
    primvar.path = SdfPath("/World/Arith/Primvar");
    primvar.identifier = TfToken("ND_geompropvalue_vector2");
    primvar.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode first;
    first.path = SdfPath("/World/Arith/First");
    first.identifier = TfToken("ND_image_color3");
    first.parameters[TfToken("file")] = VtValue(SdfAssetPath("textures/first.png"));

    HdMaterialNode second;
    second.path = SdfPath("/World/Arith/Second");
    second.identifier = TfToken("ND_image_color3");
    second.parameters[TfToken("file")] = VtValue(SdfAssetPath("textures/second.png"));

    HdMaterialNode multiply;
    multiply.path = SdfPath("/World/Arith/Multiply");
    multiply.identifier = TfToken("ND_multiply_color3");

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Arith/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {primvar, first, second, multiply, surface};
    network.relationships.push_back(
        {primvar.path, TfToken("out"), first.path, TfToken("texcoord")});
    network.relationships.push_back(
        {primvar.path, TfToken("out"), second.path, TfToken("texcoord")});
    network.relationships.push_back(
        {first.path, TfToken("out"), multiply.path, TfToken("in1")});
    network.relationships.push_back(
        {second.path, TfToken("out"), multiply.path, TfToken("in2")});
    network.relationships.push_back(
        {multiply.path, TfToken("out"), surface.path, TfToken("base_color")});
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return map;
}

/// Builds a MaterialX standard-surface network that drives one or two surface
/// inputs from two images joined by the supplied operator.
///
/// The two operands carry different constant multipliers so their folded affines
/// differ, which is what proves each entry keeps its own branch rather than
/// sharing one. `secondInput` adds a second composited parameter, which is the
/// case the renderer's single composite slot cannot serve.
HdMaterialNetworkMap
MakeTwoImageCompositeNetwork(
    const TfToken& nodeIdentifier,
    const TfToken& firstInput,
    const TfToken& secondInput,
    const VtValue& mixFactor)
{
    const bool mix = nodeIdentifier.GetString().rfind("ND_mix_", 0) == 0;
    const TfToken primaryInput = mix ? TfToken("bg") : TfToken("in1");
    const TfToken compositeInput = mix ? TfToken("fg") : TfToken("in2");

    HdMaterialNode primvar;
    primvar.path = SdfPath("/World/Composite/Primvar");
    primvar.identifier = TfToken("ND_geompropvalue_vector2");
    primvar.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode first;
    first.path = SdfPath("/World/Composite/First");
    first.identifier = TfToken("ND_image_color3");
    first.parameters[TfToken("file")] = VtValue(SdfAssetPath("textures/first.png"));

    HdMaterialNode second;
    second.path = SdfPath("/World/Composite/Second");
    second.identifier = TfToken("ND_image_color3");
    second.parameters[TfToken("file")] = VtValue(SdfAssetPath("textures/second.png"));

    // A constant scale on the second branch only, so the two published entries
    // must differ. A fold that shared one affine would report the same scale on
    // both, and a fold that dropped the inner multiply would report neither.
    HdMaterialNode scaleSecond;
    scaleSecond.path = SdfPath("/World/Composite/ScaleSecond");
    scaleSecond.identifier = TfToken("ND_multiply_color3");
    scaleSecond.parameters[TfToken("in2")] = VtValue(GfVec3f(0.5F, 0.5F, 0.5F));

    HdMaterialNode combine;
    combine.path = SdfPath("/World/Composite/Combine");
    combine.identifier = nodeIdentifier;
    if (mix && !mixFactor.IsEmpty())
    {
        combine.parameters[TfToken("mix")] = mixFactor;
    }

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Composite/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {primvar, first, second, scaleSecond, combine, surface};
    network.relationships.push_back(
        {primvar.path, TfToken("out"), first.path, TfToken("texcoord")});
    network.relationships.push_back(
        {primvar.path, TfToken("out"), second.path, TfToken("texcoord")});
    network.relationships.push_back(
        {second.path, TfToken("out"), scaleSecond.path, TfToken("in1")});
    network.relationships.push_back(
        {first.path, TfToken("out"), combine.path, primaryInput});
    network.relationships.push_back(
        {scaleSecond.path, TfToken("out"), combine.path, compositeInput});
    network.relationships.push_back(
        {combine.path, TfToken("out"), surface.path, firstInput});
    if (!secondInput.IsEmpty())
    {
        network.relationships.push_back(
            {combine.path, TfToken("out"), surface.path, secondInput});
    }
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap compositeMap;
    compositeMap.map[HdMaterialTerminalTokens->surface] = network;
    compositeMap.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return compositeMap;
}

/// Builds a MaterialX standard-surface network whose base colour applies a
/// non-affine operator to an image, which no scale and bias can represent.
HdMaterialNetworkMap
MakeNonAffineOverImageNetwork()
{
    HdMaterialNode image;
    image.path = SdfPath("/World/Arith/Image");
    image.identifier = TfToken("ND_image_color3");
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode clamp;
    clamp.path = SdfPath("/World/Arith/Clamp");
    clamp.identifier = TfToken("ND_clamp_color3");
    clamp.parameters[TfToken("low")] = VtValue(GfVec3f(0.2F, 0.2F, 0.2F));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Arith/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {image, clamp, surface};
    network.relationships.push_back(
        {image.path, TfToken("out"), clamp.path, TfToken("in")});
    network.relationships.push_back(
        {clamp.path, TfToken("out"), surface.path, TfToken("base_color")});

    HdMaterialNetworkMap map;
    map.map[HdMaterialTerminalTokens->surface] = network;
    map.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return map;
}

/// Builds a MaterialX standard-surface network whose base-colour image carries
/// the supplied node parameters and reads its coordinates through the supplied
/// coordinate node. Used to pin address-mode and default-value reading, which
/// have MaterialX names and MaterialX defaults rather than UsdUVTexture's.
HdMaterialNetworkMap
MakeMaterialXImageNetwork(
    const std::map<TfToken, VtValue>& imageInputs,
    const TfToken& coordinateIdentifier,
    const std::map<TfToken, VtValue>& coordinateInputs)
{
    HdMaterialNode coordinate;
    coordinate.path = SdfPath("/World/Sampled/Coordinate");
    coordinate.identifier = coordinateIdentifier;
    coordinate.parameters = coordinateInputs;

    HdMaterialNode image;
    image.path = SdfPath("/World/Sampled/Image");
    image.identifier = TfToken("ND_image_color3");
    image.parameters = imageInputs;
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Sampled/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {coordinate, image, surface};
    network.relationships.push_back(
        {coordinate.path, TfToken("out"), image.path, TfToken("texcoord")});
    network.relationships.push_back(
        {image.path, TfToken("out"), surface.path, TfToken("base_color")});
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap sampledMap;
    sampledMap.map[HdMaterialTerminalTokens->surface] = network;
    sampledMap.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return sampledMap;
}

/// Builds a MaterialX standard-surface network whose base colour is a single
/// node of the supplied identifier carrying the supplied inputs, optionally with
/// one of those inputs also driven by a connection.
///
/// Both halves matter. Without a connection this pins that the node folds to the
/// authored constant; with one it pins that the authored value behind the
/// connection is *not* folded, because Hydra leaves the nodedef value in
/// `parameters` even when the author replaced it with a connection.
HdMaterialNetworkMap
MakeConstantFoldNetwork(
    const TfToken& nodeIdentifier,
    const std::map<TfToken, VtValue>& nodeInputs,
    const TfToken& connectedInput,
    bool connectToMissingNode)
{
    HdMaterialNode node;
    node.path = SdfPath("/World/Folded/Node");
    node.identifier = nodeIdentifier;
    node.parameters = nodeInputs;

    HdMaterialNode image;
    image.path = SdfPath("/World/Folded/Image");
    image.identifier = TfToken("ND_image_color3");
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Folded/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {node, surface};
    if (!connectedInput.IsEmpty() && !connectToMissingNode)
    {
        network.nodes.push_back(image);
    }
    network.relationships.push_back(
        {node.path, TfToken("out"), surface.path, TfToken("base_color")});
    if (!connectedInput.IsEmpty())
    {
        // A dangling relationship is authored data too: a network can name a node
        // it does not carry, and reading the authored fallback in that case folds
        // a value the author replaced with a connection.
        network.relationships.push_back(
            {connectToMissingNode ? SdfPath("/World/Folded/Absent") : image.path,
             TfToken("out"),
             node.path,
             connectedInput});
    }

    HdMaterialNetworkMap foldedMap;
    foldedMap.map[HdMaterialTerminalTokens->surface] = network;
    foldedMap.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return foldedMap;
}

/// Builds a MaterialX standard-surface network whose base-colour image has the
/// named constant input driven by a connection while still carrying an authored
/// value for it, which is the shape Hydra produces for any connected input.
HdMaterialNetworkMap
MakeConnectedImageConstantNetwork(const TfToken& connectedInput)
{
    HdMaterialNode primvar;
    primvar.path = SdfPath("/World/Sampled/Primvar");
    primvar.identifier = TfToken("ND_geompropvalue_vector2");
    primvar.parameters[TfToken("geomprop")] = VtValue(std::string("uvSet0"));

    HdMaterialNode driver;
    driver.path = SdfPath("/World/Sampled/Driver");
    driver.identifier = TfToken("ND_constant_color3");
    driver.parameters[TfToken("value")] = VtValue(GfVec3f(0.5F, 0.5F, 0.5F));

    HdMaterialNode image;
    image.path = SdfPath("/World/Sampled/Image");
    image.identifier = TfToken("ND_image_color3");
    image.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/materialx-basecolor.png"));
    image.parameters[connectedInput] = VtValue(GfVec3f(0.125F, 0.25F, 0.5F));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Sampled/Surface");
    surface.identifier = TfToken("ND_standard_surface_surfaceshader");

    HdMaterialNetwork network;
    network.nodes = {primvar, driver, image, surface};
    network.relationships.push_back(
        {primvar.path, TfToken("out"), image.path, TfToken("texcoord")});
    network.relationships.push_back(
        {driver.path, TfToken("out"), image.path, connectedInput});
    network.relationships.push_back(
        {image.path, TfToken("out"), surface.path, TfToken("base_color")});
    network.primvars = {TfToken("uvSet0")};

    HdMaterialNetworkMap connectedMap;
    connectedMap.map[HdMaterialTerminalTokens->surface] = network;
    connectedMap.config["mtlxVersion"] = VtValue(std::string("1.39"));
    return connectedMap;
}

/// Builds a surface network whose base colour is wired to a node the network
/// does not carry, while the surface node still holds an authored value for that
/// same input. Hydra produces this shape whenever a referenced node is filtered
/// out, and the authored value is the one the author replaced.
HdMaterialNetworkMap
MakeDanglingSurfaceInputNetwork(bool previewSurface)
{
    HdMaterialNode surface;
    surface.path = SdfPath("/World/Folded/Surface");
    surface.identifier = previewSurface
        ? TfToken("UsdPreviewSurface")
        : TfToken("ND_standard_surface_surfaceshader");
    const TfToken inputName =
        previewSurface ? TfToken("diffuseColor") : TfToken("base_color");
    surface.parameters[inputName] = VtValue(GfVec3f(0.7F, 0.7F, 0.7F));

    HdMaterialNetwork network;
    network.nodes = {surface};
    network.relationships.push_back(
        {SdfPath("/World/Folded/Absent"), TfToken("out"), surface.path, inputName});

    HdMaterialNetworkMap danglingMap;
    danglingMap.map[HdMaterialTerminalTokens->surface] = network;
    if (!previewSurface)
    {
        danglingMap.config["mtlxVersion"] = VtValue(std::string("1.39"));
    }
    return danglingMap;
}

/// Builds a UsdPreviewSurface network whose UsdUVTexture has a connected `scale`
/// alongside an authored one. The four constant wire floats cannot carry a
/// per-pixel scale, and the authored value is not what the graph asks for.
HdMaterialNetworkMap
MakeConnectedPreviewTextureConstantNetwork()
{
    HdMaterialNode reader;
    reader.path = SdfPath("/World/Folded/Reader");
    reader.identifier = TfToken("UsdPrimvarReader_float2");
    reader.parameters[TfToken("varname")] = VtValue(TfToken("st"));

    HdMaterialNode driver;
    driver.path = SdfPath("/World/Folded/Driver");
    driver.identifier = TfToken("UsdPrimvarReader_float3");

    HdMaterialNode texture;
    texture.path = SdfPath("/World/Folded/Texture");
    texture.identifier = TfToken("UsdUVTexture");
    texture.parameters[TfToken("file")] =
        VtValue(SdfAssetPath("textures/preview-basecolor.png"));
    texture.parameters[TfToken("scale")] = VtValue(GfVec4f(0.5F, 0.5F, 0.5F, 1.0F));

    HdMaterialNode surface;
    surface.path = SdfPath("/World/Folded/Surface");
    surface.identifier = TfToken("UsdPreviewSurface");

    HdMaterialNetwork network;
    network.nodes = {reader, driver, texture, surface};
    network.relationships.push_back(
        {reader.path, TfToken("result"), texture.path, TfToken("st")});
    network.relationships.push_back(
        {driver.path, TfToken("result"), texture.path, TfToken("scale")});
    network.relationships.push_back(
        {texture.path, TfToken("rgb"), surface.path, TfToken("diffuseColor")});
    network.primvars = {TfToken("st")};

    HdMaterialNetworkMap previewMap;
    previewMap.map[HdMaterialTerminalTokens->surface] = network;
    return previewMap;
}

bool UvTransformMatches(
    const float (&actual)[6],
    const std::array<float, 6>& expected)
{
    for (size_t index = 0; index < expected.size(); ++index)
    {
        if (std::fabs(actual[index] - expected[index]) > 1e-5F)
        {
            return false;
        }
    }
    return true;
}

/// Extracts the ABI v14 trailing uv_transform of the one MATERIAL_UPSERT command
/// in a built page, so the fold is proven on the wire and not only in memory.
/// Extracts the ABI v15 composite_op of the first two texture entries of the one
/// MATERIAL_UPSERT command in a built page.
///
/// The renderer only ever sees the page, so a composite that folds correctly in
/// memory but never reaches the wire would render as an ordinary single texture.
bool ReadPageCompositeOperators(
    const std::vector<uint8_t>& page,
    std::array<uint32_t, 2>* out)
{
    size_t offset = 0;
    while (offset + 8 <= page.size())
    {
        uint32_t type = 0;
        uint32_t byteSize = 0;
        std::memcpy(&type, page.data() + offset, sizeof(type));
        std::memcpy(&byteSize, page.data() + offset + 4, sizeof(byteSize));
        if (byteSize < 8 || offset + byteSize > page.size())
        {
            return false;
        }
        if (type != OPENUSD_SILK_COMMAND_MATERIAL_UPSERT)
        {
            offset += byteSize;
            continue;
        }
        uint32_t pathByteCount = 0;
        uint32_t scalarCount = 0;
        uint32_t textureCount = 0;
        std::memcpy(&pathByteCount, page.data() + offset + 16, sizeof(pathByteCount));
        std::memcpy(&scalarCount, page.data() + offset + 24, sizeof(scalarCount));
        std::memcpy(&textureCount, page.data() + offset + 28, sizeof(textureCount));
        if (textureCount < 2)
        {
            return false;
        }
        size_t cursor = offset + 32 + pathByteCount;
        for (uint32_t scalar = 0; scalar < scalarCount; ++scalar)
        {
            uint32_t componentCount = 0;
            std::memcpy(&componentCount, page.data() + cursor + 4, sizeof(componentCount));
            cursor += 8 + (static_cast<size_t>(componentCount) * sizeof(float));
        }
        for (uint32_t texture = 0; texture < 2; ++texture)
        {
            uint32_t assetSize = 0;
            uint32_t uvSize = 0;
            std::memcpy(&assetSize, page.data() + cursor + 16, sizeof(assetSize));
            std::memcpy(&uvSize, page.data() + cursor + 20, sizeof(uvSize));
            std::memcpy(&(*out)[texture], page.data() + cursor + 80, sizeof(uint32_t));
            cursor += 88 + assetSize + uvSize;
        }
        return true;
    }
    return false;
}

bool ReadPageUvTransform(
    const std::vector<uint8_t>& page,
    std::array<float, 6>* out)
{
    size_t offset = 0;
    bool found = false;
    while (offset + 8 <= page.size())
    {
        uint32_t type = 0;
        uint32_t byteSize = 0;
        std::memcpy(&type, page.data() + offset, sizeof(type));
        std::memcpy(&byteSize, page.data() + offset + 4, sizeof(byteSize));
        if (byteSize < 8 || offset + byteSize > page.size())
        {
            return false;
        }
        if (type == OPENUSD_SILK_COMMAND_MATERIAL_UPSERT)
        {
            if (byteSize < 8 + (6 * sizeof(float)))
            {
                return false;
            }
            std::memcpy(
                out->data(),
                page.data() + offset + byteSize - (6 * sizeof(float)),
                6 * sizeof(float));
            found = true;
        }
        offset += byteSize;
    }
    return found && offset == page.size();
}

/// Proves the bounded MaterialX and UsdPreviewSurface UV chain projection:
/// constant place2d SRT/TRS folds, chained-transform composition, the
/// UsdTransform2d fold with its own upstream primvar, the primvar behind the
/// transform, the wire round trip, and every case the projection rejects with a
/// diagnostic instead of approximating.
bool TextureAffineMatches(
    const HdSilkMaterialTexture& texture,
    const std::array<float, 4>& scale,
    const std::array<float, 4>& bias)
{
    for (size_t index = 0; index < 4; ++index)
    {
        if (std::fabs(texture.scale[index] - scale[index]) > 1e-5F ||
            std::fabs(texture.bias[index] - bias[index]) > 1e-5F)
        {
            return false;
        }
    }
    return true;
}

/// Proves the bounded fold of constant arithmetic over exactly one image into
/// the texture entry's own scale and bias, and every graph shape it refuses.
bool VerifyMaterialXImageArithmeticProjection()
{
    const SdfPath materialPath("/World/Arith");

    // mix(bg = 0.2, fg = image * 0.5, mix = 0.75) is affine in the image:
    // value = image * 0.375 + 0.05. Neither node alone produces that pair, so a
    // fold that dropped either the multiply or the mix fails here.
    HdSilkMaterialRecord folded = HdSilkMaterial::Resolve(
        materialPath,
        MakeAffineOverImageNetwork(
            TfToken("base_color"),
            TfToken("ND_image_color3"),
            VtValue(GfVec3f(0.5F, 0.5F, 0.5F)),
            VtValue(GfVec3f(0.2F, 0.2F, 0.2F)),
            0.75F));
    if (folded.surfaceKind != OPENUSD_SILK_SURFACE_MATERIALX_PROJECTED ||
        folded.textures.size() != 1 ||
        folded.textures[0].parameter != OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
        folded.textures[0].uvPrimvar != "uvSet0" ||
        folded.textures[0].outputChannel != OPENUSD_SILK_TEXTURE_CHANNEL_RGB ||
        !TextureAffineMatches(
            folded.textures[0],
            {0.375F, 0.375F, 0.375F, 1.0F},
            {0.05F, 0.05F, 0.05F, 0.0F}))
    {
        std::cerr << "MaterialX image arithmetic fold was wrong: textures="
                  << folded.textures.size();
        if (!folded.textures.empty())
        {
            std::cerr << " scale=(" << folded.textures[0].scale[0] << ", "
                      << folded.textures[0].scale[3] << ") bias=("
                      << folded.textures[0].bias[0] << ", "
                      << folded.textures[0].bias[3] << ")";
        }
        std::cerr << "\n";
        return false;
    }

    // The same shape on a scalar input folds only the red channel, because the
    // consumer replicates the connected output channel after scale and bias.
    HdSilkMaterialRecord scalarFold = HdSilkMaterial::Resolve(
        materialPath,
        MakeAffineOverImageNetwork(
            TfToken("specular_roughness"),
            TfToken("ND_image_float"),
            VtValue(0.5F),
            VtValue(0.2F),
            0.75F));
    if (scalarFold.textures.size() != 1 ||
        scalarFold.textures[0].parameter != OPENUSD_SILK_MATERIAL_ROUGHNESS ||
        scalarFold.textures[0].outputChannel != OPENUSD_SILK_TEXTURE_CHANNEL_R ||
        !TextureAffineMatches(
            scalarFold.textures[0],
            {0.375F, 1.0F, 1.0F, 1.0F},
            {0.05F, 0.0F, 0.0F, 0.0F}))
    {
        std::cerr << "MaterialX scalar image arithmetic fold was wrong: textures="
                  << scalarFold.textures.size() << "\n";
        return false;
    }

    // An affine that leaves the unit range would be clamped by the eight-bit
    // upload path, which changes the lit result rather than rounding it.
    HdSilkMaterialRecord brightened = HdSilkMaterial::Resolve(
        materialPath,
        MakeAffineOverImageNetwork(
            TfToken("base_color"),
            TfToken("ND_image_color3"),
            VtValue(GfVec3f(3.0F, 3.0F, 3.0F)),
            VtValue(GfVec3f(0.0F, 0.0F, 0.0F)),
            1.0F));
    if (!brightened.textures.empty())
    {
        std::cerr << "An out-of-range image arithmetic fold was not rejected: textures="
                  << brightened.textures.size() << "\n";
        return false;
    }

    // Two images multiplied into one input are no longer folded away: since ABI
    // v15 they publish a primary entry and a composite operand that the shader
    // combines per pixel. The affine fold must decline them, which is what routes
    // them to the composite path, and it must not half-fold either operand.
    HdSilkMaterialRecord twoImages =
        HdSilkMaterial::Resolve(materialPath, MakeTwoImageProductNetwork());
    if (twoImages.textures.size() != 2)
    {
        std::cerr << "A two-image product did not publish both operands: textures="
                  << twoImages.textures.size() << "\n";
        return false;
    }
    if (twoImages.textures[0].compositeOp != OPENUSD_SILK_COMPOSITE_NONE ||
        twoImages.textures[1].compositeOp != OPENUSD_SILK_COMPOSITE_MULTIPLY ||
        !TextureAffineMatches(
            twoImages.textures[0], {1.0F, 1.0F, 1.0F, 1.0F}, {0.0F, 0.0F, 0.0F, 0.0F}) ||
        !TextureAffineMatches(
            twoImages.textures[1], {1.0F, 1.0F, 1.0F, 1.0F}, {0.0F, 0.0F, 0.0F, 0.0F}))
    {
        std::cerr << "A two-image product published the wrong operator or affine: ops=("
                  << twoImages.textures[0].compositeOp << ", "
                  << twoImages.textures[1].compositeOp << ")\n";
        return false;
    }

    // A non-affine operator over an image has no scale and bias representation.
    HdSilkMaterialRecord nonAffine =
        HdSilkMaterial::Resolve(materialPath, MakeNonAffineOverImageNetwork());
    if (!nonAffine.textures.empty() || !nonAffine.scalars.empty())
    {
        std::cerr << "A non-affine operator over an image was not rejected: textures="
                  << nonAffine.textures.size() << " scalars="
                  << nonAffine.scalars.size() << "\n";
        return false;
    }

    // The fold must not touch the normal input: scaling a tangent-space normal is
    // not the colour operation this projection models.
    HdSilkMaterialRecord normalArithmetic = HdSilkMaterial::Resolve(
        materialPath,
        MakeAffineOverImageNetwork(
            TfToken("normal"),
            TfToken("ND_image_vector3"),
            VtValue(GfVec3f(0.5F, 0.5F, 0.5F)),
            VtValue(GfVec3f(0.2F, 0.2F, 0.2F)),
            0.75F));
    if (!normalArithmetic.textures.empty())
    {
        std::cerr << "Arithmetic over a normal image was folded rather than rejected: textures="
                  << normalArithmetic.textures.size() << "\n";
        return false;
    }

    // Two images joined by one constant operator publish two entries: a primary
    // and a composite operand the shader combines per pixel. Each keeps its own
    // branch's folded affine, which is what the differing scales prove.
    const SdfPath compositePath("/World/Composite");
    struct CompositeCase
    {
        const char* node;
        uint32_t op;
    };
    const std::array<CompositeCase, 3> compositeCases{
        CompositeCase{"ND_multiply_color3", OPENUSD_SILK_COMPOSITE_MULTIPLY},
        CompositeCase{"ND_add_color3", OPENUSD_SILK_COMPOSITE_ADD},
        CompositeCase{"ND_subtract_color3", OPENUSD_SILK_COMPOSITE_SUBTRACT}};
    for (const CompositeCase& compositeCase : compositeCases)
    {
        HdSilkMaterialRecord composed = HdSilkMaterial::Resolve(
            compositePath,
            MakeTwoImageCompositeNetwork(
                TfToken(compositeCase.node),
                TfToken("base_color"),
                TfToken(),
                VtValue()));
        if (composed.textures.size() != 2 ||
            composed.textures[0].compositeOp != OPENUSD_SILK_COMPOSITE_NONE ||
            composed.textures[1].compositeOp != compositeCase.op ||
            composed.textures[0].asset != "textures/first.png" ||
            composed.textures[1].asset != "textures/second.png" ||
            composed.textures[0].parameter != OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
            composed.textures[1].parameter != OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
            !TextureAffineMatches(
                composed.textures[0], {1.0F, 1.0F, 1.0F, 1.0F}, {0.0F, 0.0F, 0.0F, 0.0F}) ||
            !TextureAffineMatches(
                composed.textures[1], {0.5F, 0.5F, 0.5F, 1.0F}, {0.0F, 0.0F, 0.0F, 0.0F}))
        {
            std::cerr << "The two-image " << compositeCase.node
                      << " composite was wrong: textures=" << composed.textures.size();
            for (const HdSilkMaterialTexture& texture : composed.textures)
            {
                std::cerr << " (" << texture.asset << " op=" << texture.compositeOp
                          << " scale=" << texture.scale[0] << ")";
            }
            std::cerr << "\n";
            return false;
        }
    }

    // Operand order is observable for subtract, so the primary must be `in1`.
    // mix carries its constant factor, which nothing else on the wire encodes.
    HdSilkMaterialRecord blended = HdSilkMaterial::Resolve(
        compositePath,
        MakeTwoImageCompositeNetwork(
            TfToken("ND_mix_color3"),
            TfToken("base_color"),
            TfToken(),
            VtValue(0.25F)));
    if (blended.textures.size() != 2 ||
        blended.textures[1].compositeOp != OPENUSD_SILK_COMPOSITE_MIX ||
        std::fabs(blended.textures[1].compositeFactor - 0.25F) > 1e-6F ||
        blended.textures[0].asset != "textures/first.png")
    {
        std::cerr << "The two-image mix composite was wrong: textures="
                  << blended.textures.size() << " factor="
                  << (blended.textures.size() > 1
                          ? blended.textures[1].compositeFactor
                          : -1.0F)
                  << "\n";
        return false;
    }

    // The renderer binds one composite image per material, so a second
    // composited parameter has no slot. Both of its entries go, not just one:
    // publishing the primary alone would render one of two authored images.
    HdSilkMaterialRecord twoComposites = HdSilkMaterial::Resolve(
        compositePath,
        MakeTwoImageCompositeNetwork(
            TfToken("ND_multiply_color3"),
            TfToken("base_color"),
            TfToken("emission_color"),
            VtValue()));
    if (twoComposites.textures.size() != 2)
    {
        std::cerr << "A second composited parameter was not dropped whole: textures="
                  << twoComposites.textures.size();
        for (const HdSilkMaterialTexture& texture : twoComposites.textures)
        {
            std::cerr << " (parameter=" << texture.parameter
                      << " op=" << texture.compositeOp << ")";
        }
        std::cerr << "\n";
        return false;
    }
    for (const HdSilkMaterialTexture& texture : twoComposites.textures)
    {
        if (texture.parameter != OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR)
        {
            std::cerr << "The surviving composite was not the first input: parameter="
                      << texture.parameter << "\n";
            return false;
        }
    }

    // The wire must round-trip both entries, because the renderer only ever sees
    // the page. BuildPage also rejects a lone composite and a second one.
    HdSilkSceneState compositeState;
    compositeState.ReplaceMaterial(HdSilkMaterial::Resolve(
        compositePath,
        MakeTwoImageCompositeNetwork(
            TfToken("ND_multiply_color3"),
            TfToken("base_color"),
            TfToken(),
            VtValue())));
    uint64_t compositeRevision = 0;
    uint32_t compositeCommands = 0;
    const std::vector<uint8_t> compositePage =
        compositeState.BuildPage(&compositeRevision, &compositeCommands);
    std::array<uint32_t, 2> wireOps{};
    if (!ReadPageCompositeOperators(compositePage, &wireOps) ||
        wireOps[0] != OPENUSD_SILK_COMPOSITE_NONE ||
        wireOps[1] != OPENUSD_SILK_COMPOSITE_MULTIPLY)
    {
        std::cerr << "The composite operators did not reach the page: ("
                  << wireOps[0] << ", " << wireOps[1] << ")\n";
        return false;
    }

    // A pure constant chain must still resolve to a scalar, not to a texture.
    std::map<TfToken, VtValue> unusedInputs;
    HdSilkMaterialRecord constantOnly =
        HdSilkMaterial::Resolve(SdfPath("/World/Placed"), MakePlace2dNetwork(unusedInputs, false));
    if (constantOnly.textures.size() != 1)
    {
        std::cerr << "The direct-image path regressed while folding arithmetic.\n";
        return false;
    }
    return true;
}

bool ScalarMatches(
    const HdSilkMaterialScalar& scalar,
    const std::array<float, 3>& expected)
{
    for (size_t index = 0; index < expected.size(); ++index)
    {
        if (std::fabs(scalar.value[index] - expected[index]) > 1e-5F)
        {
            return false;
        }
    }
    return true;
}

/// Proves that a value hdSilk folds to a constant is the value the MaterialX
/// nodedef states, and that an input the author replaced with a connection is
/// never read from the authored fallback Hydra leaves behind it.
bool VerifyMaterialXConstantFoldProjection()
{
    const SdfPath materialPath("/World/Folded");

    // ND_constant_* states its value on `value`. Before this was read the node
    // matched no operator branch and the whole input was reported unsupported.
    std::map<TfToken, VtValue> constantInputs;
    constantInputs[TfToken("value")] = VtValue(GfVec3f(0.25F, 0.5F, 0.75F));
    HdSilkMaterialRecord constantNode = HdSilkMaterial::Resolve(
        materialPath,
        MakeConstantFoldNetwork(
            TfToken("ND_constant_color3"), constantInputs, TfToken(), false));
    if (constantNode.scalars.size() != 1 ||
        constantNode.scalars[0].parameter != OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
        !ScalarMatches(constantNode.scalars[0], {0.25F, 0.5F, 0.75F}))
    {
        std::cerr << "ND_constant_color3 did not fold its authored value: scalars="
                  << constantNode.scalars.size() << "\n";
        return false;
    }

    // A constant node whose value is connected is not constant. The authored
    // value is still present in `parameters`, so folding it would publish a
    // colour the author replaced with a connection.
    HdSilkMaterialRecord connectedConstant = HdSilkMaterial::Resolve(
        materialPath,
        MakeConstantFoldNetwork(
            TfToken("ND_constant_color3"),
            constantInputs,
            TfToken("value"),
            false));
    if (!connectedConstant.scalars.empty())
    {
        std::cerr << "A connected ND_constant_color3 value was folded anyway: scalars="
                  << connectedConstant.scalars.size() << "\n";
        return false;
    }

    // The MaterialX mix default is 0, which selects bg. A 0.5 default folded an
    // unauthored mix to the midpoint of two operands the graph never blended.
    std::map<TfToken, VtValue> mixInputs;
    mixInputs[TfToken("bg")] = VtValue(GfVec3f(0.25F, 0.25F, 0.25F));
    mixInputs[TfToken("fg")] = VtValue(GfVec3f(0.75F, 0.75F, 0.75F));
    HdSilkMaterialRecord unauthoredMix = HdSilkMaterial::Resolve(
        materialPath,
        MakeConstantFoldNetwork(
            TfToken("ND_mix_color3"), mixInputs, TfToken(), false));
    if (unauthoredMix.scalars.size() != 1 ||
        !ScalarMatches(unauthoredMix.scalars[0], {0.25F, 0.25F, 0.25F}))
    {
        std::cerr << "An unauthored ND_mix_color3 factor did not default to 0: value="
                  << (unauthoredMix.scalars.empty()
                          ? -1.0F
                          : unauthoredMix.scalars[0].value[0])
                  << "\n";
        return false;
    }

    // A connected mix factor is not the authored fallback either.
    std::map<TfToken, VtValue> authoredMix = mixInputs;
    authoredMix[TfToken("mix")] = VtValue(1.0F);
    HdSilkMaterialRecord connectedMix = HdSilkMaterial::Resolve(
        materialPath,
        MakeConstantFoldNetwork(
            TfToken("ND_mix_color3"), authoredMix, TfToken("mix"), false));
    if (!connectedMix.scalars.empty())
    {
        std::cerr << "A connected mix factor folded its authored fallback: scalars="
                  << connectedMix.scalars.size() << "\n";
        return false;
    }

    // A modelled operator must win over the pass-through `in` shortcut. clamp
    // declares `in`, so taking the shortcut first returned the *unclamped* value
    // and made the operator's meaning depend on whether its input happened to be
    // a connection: clamp(1.5, 0.2, default high 1.0) folded to 1.5.
    std::map<TfToken, VtValue> clampInputs;
    clampInputs[TfToken("in")] = VtValue(GfVec3f(1.5F, 1.5F, 1.5F));
    clampInputs[TfToken("low")] = VtValue(GfVec3f(0.2F, 0.2F, 0.2F));
    HdSilkMaterialRecord unconnectedClamp = HdSilkMaterial::Resolve(
        materialPath,
        MakeConstantFoldNetwork(
            TfToken("ND_clamp_color3"), clampInputs, TfToken(), false));
    if (unconnectedClamp.scalars.size() != 1 ||
        !ScalarMatches(unconnectedClamp.scalars[0], {1.0F, 1.0F, 1.0F}))
    {
        std::cerr << "An unconnected clamp did not clamp: scalars="
                  << unconnectedClamp.scalars.size() << " value="
                  << (unconnectedClamp.scalars.empty()
                          ? -1.0F
                          : unconnectedClamp.scalars[0].value[0])
                  << "\n";
        return false;
    }

    // The shortcut itself must still work for a node that only passes `in`
    // through and that this projection does not otherwise model.
    std::map<TfToken, VtValue> dotInputs;
    dotInputs[TfToken("in")] = VtValue(GfVec3f(0.9F, 0.8F, 0.7F));
    HdSilkMaterialRecord passThrough = HdSilkMaterial::Resolve(
        materialPath,
        MakeConstantFoldNetwork(
            TfToken("ND_dot_color3"), dotInputs, TfToken(), false));
    if (passThrough.scalars.size() != 1 ||
        !ScalarMatches(passThrough.scalars[0], {0.9F, 0.8F, 0.7F}))
    {
        std::cerr << "The pass-through `in` shortcut stopped folding: scalars="
                  << passThrough.scalars.size() << "\n";
        return false;
    }

    // The pass-through `in` shortcut must not fire for a connected `in`. A clamp
    // whose `in` is an image used to publish its authored `in` value as a
    // constant colour, dropping the image with no diagnostic at all.
    HdSilkMaterialRecord connectedClamp = HdSilkMaterial::Resolve(
        materialPath,
        MakeConstantFoldNetwork(
            TfToken("ND_clamp_color3"), clampInputs, TfToken("in"), false));
    if (!connectedClamp.scalars.empty() || !connectedClamp.textures.empty())
    {
        std::cerr << "A clamp over an image folded its authored `in`: scalars="
                  << connectedClamp.scalars.size() << " textures="
                  << connectedClamp.textures.size() << "\n";
        return false;
    }

    // A relationship naming a node the network does not carry is still a
    // connection, so the authored fallback behind it must not be folded.
    HdSilkMaterialRecord danglingConnection = HdSilkMaterial::Resolve(
        materialPath,
        MakeConstantFoldNetwork(
            TfToken("ND_clamp_color3"), clampInputs, TfToken("in"), true));
    if (!danglingConnection.scalars.empty())
    {
        std::cerr << "A dangling connection folded the authored fallback: scalars="
                  << danglingConnection.scalars.size() << "\n";
        return false;
    }

    // The same rule applies to the surface terminal itself: a base colour wired
    // to a node the network does not carry must not fall back to the value
    // authored on the surface node.
    HdSilkMaterialRecord danglingTerminal = HdSilkMaterial::Resolve(
        materialPath, MakeDanglingSurfaceInputNetwork(false));
    if (!danglingTerminal.scalars.empty())
    {
        std::cerr << "A dangling MaterialX terminal connection folded the surface "
                     "value: scalars="
                  << danglingTerminal.scalars.size() << "\n";
        return false;
    }
    HdSilkMaterialRecord danglingPreviewTerminal = HdSilkMaterial::Resolve(
        materialPath, MakeDanglingSurfaceInputNetwork(true));
    if (!danglingPreviewTerminal.scalars.empty())
    {
        std::cerr << "A dangling UsdPreviewSurface terminal connection folded the "
                     "surface value: scalars="
                  << danglingPreviewTerminal.scalars.size() << "\n";
        return false;
    }

    // The UsdPreviewSurface texture path used to build its entry inline and read
    // scale, bias and fallback without checking for a connection.
    HdSilkMaterialRecord connectedScale = HdSilkMaterial::Resolve(
        materialPath, MakeConnectedPreviewTextureConstantNetwork());
    if (!connectedScale.textures.empty())
    {
        std::cerr << "A connected UsdUVTexture `scale` was folded anyway: textures="
                  << connectedScale.textures.size() << "\n";
        return false;
    }
    return true;
}

/// Proves that a MaterialX image is sampled the way MaterialX states it: its own
/// address-mode inputs with their own periodic default, and its own `default`
/// value as the entry fallback.
bool VerifyMaterialXImageSamplingProjection()
{
    const SdfPath materialPath("/World/Sampled");
    std::map<TfToken, VtValue> primvarInputs;
    primvarInputs[TfToken("geomprop")] = VtValue(std::string("uvSet0"));
    const TfToken geomPropValue("ND_geompropvalue_vector2");

    // MaterialX defaults both address modes to periodic. Reading UsdUVTexture's
    // wrapS/wrapT here found nothing and published black, which the renderer
    // resolves to clamp-to-edge, so a tiled MaterialX texture stopped tiling.
    std::map<TfToken, VtValue> unauthored;
    HdSilkMaterialRecord tiled = HdSilkMaterial::Resolve(
        materialPath,
        MakeMaterialXImageNetwork(unauthored, geomPropValue, primvarInputs));
    if (tiled.textures.size() != 1 ||
        tiled.textures[0].wrapS != OPENUSD_SILK_WRAP_REPEAT ||
        tiled.textures[0].wrapT != OPENUSD_SILK_WRAP_REPEAT)
    {
        std::cerr << "An unauthored MaterialX address mode was not periodic: textures="
                  << tiled.textures.size();
        if (!tiled.textures.empty())
        {
            std::cerr << " wrap=(" << tiled.textures[0].wrapS << ", "
                      << tiled.textures[0].wrapT << ")";
        }
        std::cerr << "\n";
        return false;
    }

    // Every enumerated mode maps to its own wire value, and the two axes are
    // independent: a single shared read would report the same mode twice.
    struct AddressCase
    {
        const char* mode;
        uint32_t wrap;
    };
    const std::array<AddressCase, 4> addressCases{
        AddressCase{"constant", OPENUSD_SILK_WRAP_BLACK},
        AddressCase{"clamp", OPENUSD_SILK_WRAP_CLAMP},
        AddressCase{"periodic", OPENUSD_SILK_WRAP_REPEAT},
        AddressCase{"mirror", OPENUSD_SILK_WRAP_MIRROR}};
    for (const AddressCase& addressCase : addressCases)
    {
        std::map<TfToken, VtValue> authored;
        authored[TfToken("uaddressmode")] = VtValue(std::string(addressCase.mode));
        // The V axis is authored as a TfToken rather than a std::string because
        // usdMtlx carries MaterialX strings as either, depending on the layer.
        authored[TfToken("vaddressmode")] = VtValue(TfToken("clamp"));
        HdSilkMaterialRecord addressed = HdSilkMaterial::Resolve(
            materialPath,
            MakeMaterialXImageNetwork(authored, geomPropValue, primvarInputs));
        if (addressed.textures.size() != 1 ||
            addressed.textures[0].wrapS != addressCase.wrap ||
            addressed.textures[0].wrapT != OPENUSD_SILK_WRAP_CLAMP)
        {
            std::cerr << "MaterialX address mode '" << addressCase.mode
                      << "' did not map correctly: textures="
                      << addressed.textures.size();
            if (!addressed.textures.empty())
            {
                std::cerr << " wrap=(" << addressed.textures[0].wrapS << ", "
                          << addressed.textures[0].wrapT << ")";
            }
            std::cerr << "\n";
            return false;
        }
    }

    // An address mode outside the enumeration is reported, not guessed at.
    std::map<TfToken, VtValue> unknownMode;
    unknownMode[TfToken("uaddressmode")] = VtValue(std::string("wraparound"));
    HdSilkMaterialRecord unknown = HdSilkMaterial::Resolve(
        materialPath,
        MakeMaterialXImageNetwork(unknownMode, geomPropValue, primvarInputs));
    if (!unknown.textures.empty())
    {
        std::cerr << "An unmodelled MaterialX address mode was not rejected: textures="
                  << unknown.textures.size() << "\n";
        return false;
    }

    // MaterialX names the unreadable-file value `default`; UsdUVTexture names it
    // `fallback`. Reading only the UsdUVTexture name left an authored MaterialX
    // default at the struct's opaque black instead of the authored colour.
    std::map<TfToken, VtValue> defaulted;
    defaulted[TfToken("default")] = VtValue(GfVec3f(0.125F, 0.25F, 0.5F));
    HdSilkMaterialRecord withDefault = HdSilkMaterial::Resolve(
        materialPath,
        MakeMaterialXImageNetwork(defaulted, geomPropValue, primvarInputs));
    if (withDefault.textures.size() != 1 ||
        std::fabs(withDefault.textures[0].fallback[0] - 0.125F) > 1e-5F ||
        std::fabs(withDefault.textures[0].fallback[1] - 0.25F) > 1e-5F ||
        std::fabs(withDefault.textures[0].fallback[2] - 0.5F) > 1e-5F ||
        std::fabs(withDefault.textures[0].fallback[3] - 1.0F) > 1e-5F)
    {
        std::cerr << "A MaterialX image `default` was not read as the fallback: textures="
                  << withDefault.textures.size();
        if (!withDefault.textures.empty())
        {
            std::cerr << " fallback=(" << withDefault.textures[0].fallback[0] << ", "
                      << withDefault.textures[0].fallback[1] << ", "
                      << withDefault.textures[0].fallback[2] << ", "
                      << withDefault.textures[0].fallback[3] << ")";
        }
        std::cerr << "\n";
        return false;
    }

    // A texcoord node reading UV set zero is the documented default primvar.
    std::map<TfToken, VtValue> firstSet;
    firstSet[TfToken("index")] = VtValue(0);
    HdSilkMaterialRecord defaultSet = HdSilkMaterial::Resolve(
        materialPath,
        MakeMaterialXImageNetwork(
            unauthored, TfToken("ND_texcoord_vector2"), firstSet));
    if (defaultSet.textures.size() != 1 || defaultSet.textures[0].uvPrimvar != "st")
    {
        std::cerr << "A zero texcoord index did not resolve to the default primvar.\n";
        return false;
    }

    // A non-zero index names a second UV set. hdSilk carries one coordinate
    // stream per material, so resolving it to `st` would sample the first set
    // while the graph asked for another.
    std::map<TfToken, VtValue> secondSet;
    secondSet[TfToken("index")] = VtValue(1);
    HdSilkMaterialRecord otherSet = HdSilkMaterial::Resolve(
        materialPath,
        MakeMaterialXImageNetwork(
            unauthored, TfToken("ND_texcoord_vector2"), secondSet));
    if (!otherSet.textures.empty())
    {
        std::cerr << "A non-zero texcoord index was silently resolved to st: textures="
                  << otherSet.textures.size() << " primvar='"
                  << (otherSet.textures.empty() ? std::string()
                                                : otherSet.textures[0].uvPrimvar)
                  << "'\n";
        return false;
    }

    // A connected `default` is a per-pixel value the four constant wire floats
    // cannot carry, and the authored value Hydra leaves behind the connection is
    // not what the graph asks to be rendered.
    HdSilkMaterialRecord connectedDefault = HdSilkMaterial::Resolve(
        materialPath, MakeConnectedImageConstantNetwork(TfToken("default")));
    if (!connectedDefault.textures.empty())
    {
        std::cerr << "A connected MaterialX image `default` was folded anyway: textures="
                  << connectedDefault.textures.size() << "\n";
        return false;
    }
    return true;
}

bool VerifyMaterialXUvChainProjection()
{
    const SdfPath materialPath("/World/Placed");

    // SRT with scale and offset only. place2d divides by scale, so scale 2
    // halves the coordinate, and the offset is subtracted after rotation.
    std::map<TfToken, VtValue> scaleOffset;
    scaleOffset[TfToken("scale")] = VtValue(GfVec2f(2.0F, 2.0F));
    scaleOffset[TfToken("offset")] = VtValue(GfVec2f(0.25F, 0.5F));
    HdSilkMaterialRecord scaled =
        HdSilkMaterial::Resolve(materialPath, MakePlace2dNetwork(scaleOffset, false));
    if (scaled.surfaceKind != OPENUSD_SILK_SURFACE_MATERIALX_PROJECTED ||
        scaled.textures.size() != 1 ||
        scaled.textures[0].uvPrimvar != "uvSet0" ||
        !UvTransformMatches(
            scaled.uvTransform, {0.5F, 0.0F, 0.0F, 0.5F, -0.25F, -0.5F}))
    {
        std::cerr << "MaterialX place2d SRT scale/offset fold was wrong: textures="
                  << scaled.textures.size() << " transform=("
                  << scaled.uvTransform[0] << ", " << scaled.uvTransform[1] << ", "
                  << scaled.uvTransform[2] << ", " << scaled.uvTransform[3] << ", "
                  << scaled.uvTransform[4] << ", " << scaled.uvTransform[5] << ")\n";
        return false;
    }

    // A quarter turn about the tile centre. Nothing but an exact rotate2d fold
    // produces this matrix, so an approximated rotation fails here.
    std::map<TfToken, VtValue> rotated;
    rotated[TfToken("rotate")] = VtValue(90.0F);
    rotated[TfToken("pivot")] = VtValue(GfVec2f(0.5F, 0.5F));
    HdSilkMaterialRecord turned =
        HdSilkMaterial::Resolve(materialPath, MakePlace2dNetwork(rotated, false));
    if (!UvTransformMatches(
            turned.uvTransform, {0.0F, 1.0F, -1.0F, 0.0F, 0.0F, 1.0F}))
    {
        std::cerr << "MaterialX place2d rotation fold was wrong: ("
                  << turned.uvTransform[0] << ", " << turned.uvTransform[1] << ", "
                  << turned.uvTransform[2] << ", " << turned.uvTransform[3] << ", "
                  << turned.uvTransform[4] << ", " << turned.uvTransform[5] << ")\n";
        return false;
    }

    // TRS removes the offset before dividing by scale, so the same authored
    // values must not produce the SRT translation.
    std::map<TfToken, VtValue> trs;
    trs[TfToken("scale")] = VtValue(GfVec2f(2.0F, 2.0F));
    trs[TfToken("offset")] = VtValue(GfVec2f(0.25F, 0.0F));
    trs[TfToken("operationorder")] = VtValue(1);
    HdSilkMaterialRecord ordered =
        HdSilkMaterial::Resolve(materialPath, MakePlace2dNetwork(trs, false));
    if (!UvTransformMatches(
            ordered.uvTransform, {0.5F, 0.0F, 0.0F, 0.5F, -0.125F, 0.0F}))
    {
        std::cerr << "MaterialX place2d TRS fold was wrong: ("
                  << ordered.uvTransform[0] << ", " << ordered.uvTransform[1] << ", "
                  << ordered.uvTransform[2] << ", " << ordered.uvTransform[3] << ", "
                  << ordered.uvTransform[4] << ", " << ordered.uvTransform[5] << ")\n";
        return false;
    }

    // A connected place2d input is per-pixel, which this projection does not
    // model. The input must be left at its default rather than sampled with
    // coordinates the graph never asked for.
    HdSilkMaterialRecord connected =
        HdSilkMaterial::Resolve(materialPath, MakePlace2dNetwork(scaleOffset, true));
    if (!connected.textures.empty() ||
        !UvTransformMatches(
            connected.uvTransform, {1.0F, 0.0F, 0.0F, 1.0F, 0.0F, 0.0F}))
    {
        std::cerr << "A connected place2d input was not rejected: textures="
                  << connected.textures.size() << "\n";
        return false;
    }

    // A second image asking for a different transform is outside the one
    // transform per material this renderer can sample through. This is the
    // ordinary authoring shape -- a transformed base colour and a normal map
    // reading the same primvar untransformed -- so the limitation is stated
    // against a real graph rather than a contrived one.
    HdSilkMaterialRecord reconciled =
        HdSilkMaterial::Resolve(materialPath, MakeDivergentUvChainNetwork());
    if (reconciled.textures.size() != 1 ||
        reconciled.textures[0].parameter != OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
        reconciled.textures[0].uvPrimvar != "uvSet0" ||
        !UvTransformMatches(
            reconciled.uvTransform, {0.5F, 0.0F, 0.0F, 0.5F, -0.25F, -0.5F}))
    {
        std::cerr << "Divergent MaterialX UV chains were not reconciled: textures="
                  << reconciled.textures.size();
        for (const HdSilkMaterialTexture& texture : reconciled.textures)
        {
            std::cerr << " (parameter=" << texture.parameter << ")";
        }
        std::cerr << "\n";
        return false;
    }

    // The primvar half of the same limitation. Both images carry the identity
    // affine here, so the transform reconciliation cannot fire and only the
    // primvar can separate them. Before the primvar was reconciled both entries
    // were published and the consumer, which derives one stream from the first
    // entry that names a primvar, sampled the normal map through the base
    // colour's `uvSet0` with nothing reported.
    const SdfPath streamPath("/World/Streams");
    HdSilkMaterialRecord divergentPrimvar =
        HdSilkMaterial::Resolve(streamPath, MakeDivergentUvPrimvarNetwork(true));
    if (divergentPrimvar.textures.size() != 1 ||
        divergentPrimvar.textures[0].parameter !=
            OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
        divergentPrimvar.textures[0].uvPrimvar != "uvSet0" ||
        !UvTransformMatches(
            divergentPrimvar.uvTransform, {1.0F, 0.0F, 0.0F, 1.0F, 0.0F, 0.0F}))
    {
        std::cerr << "Divergent MaterialX UV primvars were not reconciled: textures="
                  << divergentPrimvar.textures.size();
        for (const HdSilkMaterialTexture& texture : divergentPrimvar.textures)
        {
            std::cerr << " (parameter=" << texture.parameter << " primvar='"
                      << texture.uvPrimvar << "')";
        }
        std::cerr << "\n";
        return false;
    }

    // Reversed, the surviving stream must follow the first texture in the fixed
    // input order rather than whichever primvar name sorts first: base colour
    // now reads uvSet1 and it is the normal map that is dropped.
    HdSilkMaterialRecord reversedPrimvar =
        HdSilkMaterial::Resolve(streamPath, MakeDivergentUvPrimvarNetwork(false));
    if (reversedPrimvar.textures.size() != 1 ||
        reversedPrimvar.textures[0].parameter !=
            OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
        reversedPrimvar.textures[0].uvPrimvar != "uvSet1")
    {
        std::cerr << "The reconciled UV primvar did not follow the first texture: textures="
                  << reversedPrimvar.textures.size();
        for (const HdSilkMaterialTexture& texture : reversedPrimvar.textures)
        {
            std::cerr << " (parameter=" << texture.parameter << " primvar='"
                      << texture.uvPrimvar << "')";
        }
        std::cerr << "\n";
        return false;
    }

    // Non-vacuity: the same two-image shape on one primvar must keep both
    // entries, so the two cases above cannot be satisfied by a projection that
    // simply drops every normal map.
    HdSilkMaterialRecord sharedPrimvar =
        HdSilkMaterial::Resolve(streamPath, MakeSharedUvPrimvarNetwork());
    if (sharedPrimvar.textures.size() != 2)
    {
        std::cerr << "Two images on one primvar did not both survive: textures="
                  << sharedPrimvar.textures.size() << "\n";
        return false;
    }
    for (const HdSilkMaterialTexture& texture : sharedPrimvar.textures)
    {
        if (texture.uvPrimvar != "uvSet0")
        {
            std::cerr << "A shared-primvar texture reported '" << texture.uvPrimvar
                      << "'\n";
            return false;
        }
    }

    HdSilkMaterialRecord chainedBehindUnsupported = HdSilkMaterial::Resolve(
        materialPath, MakePlace2dBehindUnsupportedNodeNetwork());
    if (!chainedBehindUnsupported.textures.empty() ||
        !UvTransformMatches(
            chainedBehindUnsupported.uvTransform,
            {1.0F, 0.0F, 0.0F, 1.0F, 0.0F, 0.0F}))
    {
        std::cerr << "A place2d behind an unsupported node was not rejected: textures="
                  << chainedBehindUnsupported.textures.size() << " transform=("
                  << chainedBehindUnsupported.uvTransform[0] << ", "
                  << chainedBehindUnsupported.uvTransform[4] << ", "
                  << chainedBehindUnsupported.uvTransform[5] << ")\n";
        return false;
    }

    // Two chained place2d nodes compose. The inner halves the coordinate and the
    // outer shifts it, so neither node's own matrix nor the reverse composition
    // produces this result.
    HdSilkMaterialRecord chained =
        HdSilkMaterial::Resolve(materialPath, MakeChainedPlace2dNetwork());
    if (chained.textures.size() != 1 ||
        chained.textures[0].uvPrimvar != "uvSet0" ||
        !UvTransformMatches(
            chained.uvTransform, {0.5F, 0.0F, 0.0F, 0.5F, -0.25F, 0.0F}))
    {
        std::cerr << "Chained place2d composition was wrong: textures="
                  << chained.textures.size() << " transform=("
                  << chained.uvTransform[0] << ", " << chained.uvTransform[1] << ", "
                  << chained.uvTransform[2] << ", " << chained.uvTransform[3] << ", "
                  << chained.uvTransform[4] << ", " << chained.uvTransform[5] << ")\n";
        return false;
    }

    // UsdTransform2d scales, then rotates counter-clockwise, then translates.
    // That is the opposite rotation sense to MaterialX rotate2d, so a shared
    // matrix builder would transpose the off-diagonal terms here. The primvar
    // behind the transform must survive: falling back to the default "st" would
    // sample a primvar the graph never named.
    HdSilkMaterialRecord transformed =
        HdSilkMaterial::Resolve(SdfPath("/World/Preview"), MakeTransform2dNetwork(true));
    if (transformed.surfaceKind != OPENUSD_SILK_SURFACE_PREVIEW_SURFACE ||
        transformed.textures.size() != 1 ||
        transformed.textures[0].uvPrimvar != "uvSet1" ||
        !UvTransformMatches(
            transformed.uvTransform, {0.0F, -1.0F, 2.0F, 0.0F, 0.5F, 0.25F}))
    {
        std::cerr << "UsdTransform2d fold was wrong: textures="
                  << transformed.textures.size() << " primvar='"
                  << (transformed.textures.empty()
                          ? std::string()
                          : transformed.textures[0].uvPrimvar)
                  << "' transform=(" << transformed.uvTransform[0] << ", "
                  << transformed.uvTransform[1] << ", " << transformed.uvTransform[2]
                  << ", " << transformed.uvTransform[3] << ", "
                  << transformed.uvTransform[4] << ", " << transformed.uvTransform[5]
                  << ")\n";
        return false;
    }

    // A UsdTransform2d with no upstream reader transforms its own constant
    // input, so there is no primvar to sample. Silently falling back to an
    // untransformed "st" would render a texture nothing authored.
    HdSilkMaterialRecord unrooted =
        HdSilkMaterial::Resolve(SdfPath("/World/Preview"), MakeTransform2dNetwork(false));
    if (!unrooted.textures.empty() ||
        !UvTransformMatches(
            unrooted.uvTransform, {1.0F, 0.0F, 0.0F, 1.0F, 0.0F, 0.0F}))
    {
        std::cerr << "A rootless UsdTransform2d chain was not rejected: textures="
                  << unrooted.textures.size() << "\n";
        return false;
    }

    // The fold has to survive serialization, because the renderer only ever
    // sees the page bytes.
    HdSilkSceneState state;
    state.ReplaceMaterial(scaled);
    uint64_t revision = 0;
    uint32_t commandCount = 0;
    const std::vector<uint8_t> page = state.BuildPage(&revision, &commandCount);
    std::array<float, 6> wire{};
    if (!ReadPageUvTransform(page, &wire) ||
        std::fabs(wire[0] - 0.5F) > 1e-5F ||
        std::fabs(wire[3] - 0.5F) > 1e-5F ||
        std::fabs(wire[4] + 0.25F) > 1e-5F ||
        std::fabs(wire[5] + 0.5F) > 1e-5F)
    {
        std::cerr << "The folded MaterialX UV transform did not reach the wire.\n";
        return false;
    }
    return true;
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

std::string BlendShapeProbeStagePath(const char* probeStagePath)
{
    std::string path(probeStagePath);
    const std::string fileName = "hdsilk-probe-stage.usda";
    const size_t position = path.rfind(fileName);
    if (position == std::string::npos)
    {
        return "hdsilk-blendshape-probe.usda";
    }
    path.replace(position, fileName.size(), "hdsilk-blendshape-probe.usda");
    return path;
}

std::string SiblingProbeStagePath(
    const char* probeStagePath,
    const std::string& siblingFileName)
{
    std::string path(probeStagePath);
    const std::string fileName = "hdsilk-probe-stage.usda";
    const size_t position = path.rfind(fileName);
    if (position == std::string::npos)
    {
        return siblingFileName;
    }
    path.replace(position, fileName.size(), siblingFileName);
    return path;
}

/// One expected published instance of a point-instancer prototype.
struct ExpectedInstance
{
    int32_t index = 0;
    bool carries_geometry = false;
    double translate_x = 0.0;
};

/// Collects every published record whose path contains "marker", in wire
/// order. The prototype rprim path UsdImaging synthesizes for a point
/// instancer carries a generated suffix, so the authored prototype name is
/// what identifies it.
std::vector<ParsedMeshIdentity> InstancesMatching(
    const ParsedPage& page,
    const char* marker)
{
    std::vector<ParsedMeshIdentity> matches;
    for (const ParsedMeshIdentity& identity : page.mesh_identities)
    {
        if (identity.path.find(marker) != std::string::npos)
        {
            matches.push_back(identity);
        }
    }
    return matches;
}

bool MatchesInstances(
    const ParsedPage& page,
    const char* marker,
    const std::vector<ExpectedInstance>& expected)
{
    const std::vector<ParsedMeshIdentity> actual = InstancesMatching(page, marker);
    if (actual.size() != expected.size())
    {
        std::cerr << "hdSilk instancer prototype '" << marker << "' published "
                  << actual.size() << " instances, expected " << expected.size()
                  << "\n";
        return false;
    }
    for (size_t index = 0; index < expected.size(); ++index)
    {
        const ParsedMeshIdentity& record = actual[index];
        const ExpectedInstance& want = expected[index];
        const bool carriesGeometry = record.point_count != 0;
        if (record.instance_index != want.index ||
            carriesGeometry != want.carries_geometry ||
            std::fabs(record.transform[12] - want.translate_x) > 1e-6)
        {
            std::cerr << "hdSilk instancer prototype '" << marker
                      << "' record " << index << " published index "
                      << record.instance_index << " geometry "
                      << record.point_count << " x " << record.transform[12]
                      << ", expected index " << want.index << " geometry "
                      << (want.carries_geometry ? "yes" : "no") << " x "
                      << want.translate_x << "\n";
            return false;
        }
        if (record.instance_id == 0)
        {
            std::cerr << "hdSilk instanced record carries instance id zero.\n";
            return false;
        }
    }
    return true;
}

bool MatchesRemovals(
    const ParsedPage& page,
    const char* marker,
    const std::vector<int32_t>& expected)
{
    std::vector<int32_t> actual;
    for (size_t index = 0; index < page.remove_paths.size(); ++index)
    {
        if (page.remove_paths[index].find(marker) != std::string::npos)
        {
            actual.push_back(page.remove_instance_indices[index]);
        }
    }
    std::sort(actual.begin(), actual.end());
    if (actual != expected)
    {
        std::cerr << "hdSilk instancer prototype '" << marker
                  << "' retired " << actual.size()
                  << " instances, expected " << expected.size() << "\n";
        return false;
    }
    return true;
}

/// Gates the point-instancer subset hdSilk claims: several prototypes under
/// one instancer, proto indices that vary over time, instances hidden through
/// invisibleIds, and one level of nesting.
///
/// The published instance index is the instance's own index inside the
/// instancer, which is what UsdImaging decodes back to a scene instance. The
/// position in the resolved array is not: with two prototypes it counts from
/// zero for each of them, and it renumbers every surviving instance when
/// proto indices change or an instance is hidden, which silently re-points a
/// retained pick identity at a different scene instance.
bool VerifyPointInstancerProbe(
    const char* pluginPath,
    const char* probeStagePath,
    openusd_error_buffer* error)
{
    const std::string stagePath =
        SiblingProbeStagePath(probeStagePath, "hdsilk-pointinstancer-probe.usda");
    openusd_silk_session* session = nullptr;
    if (openusd_silk_session_create(pluginPath, stagePath.c_str(), &session, error) !=
        OPENUSD_STATUS_OK)
    {
        std::cerr << "hdSilk point-instancer session create failed.\n";
        return false;
    }

    bool passed = true;

    // Frame 1 assigns proto indices [0, 1, 0, 1], so Alpha owns instancer
    // instances 0 and 2 while Beta owns 1 and 3. Beta's payload therefore
    // rides on instance 1: the prototype record is the lowest published
    // index, not index zero.
    ParsedPage first;
    if (Sync(session, &first, error, nullptr, 1.0) != OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        return false;
    }
    passed = passed &&
        MatchesInstances(
            first,
            "Alpha",
            {{0, true, 10.0}, {2, false, 12.0}}) &&
        MatchesInstances(
            first,
            "Beta",
            {{1, true, 11.0}, {3, false, 13.0}}) &&
        // Two outer instances of a three-instance inner instancer resolve to
        // six leaves whose composed index is outer * 3 + inner.
        MatchesInstances(
            first,
            "Leaf",
            {{0, true, 0.0},
             {1, false, 1.0},
             {2, false, 2.0},
             {3, false, 100.0},
             {4, false, 101.0},
             {5, false, 102.0}}) &&
        // NestedMulti's inner instancer has four instances and two prototypes.
        // The nested radix is that instance count and nothing else, so both
        // prototypes share one interleaved index space: TwigA owns inner 0 and
        // 2, TwigB owns inner 1 and 3, and the composed index is
        // outer * 4 + inner for both. A radix widened to fit whichever
        // prototype was being resolved would have numbered them against
        // different bases.
        MatchesInstances(
            first,
            "TwigA",
            {{0, true, 0.0},
             {2, false, 2.0},
             {4, false, 200.0},
             {6, false, 202.0}}) &&
        MatchesInstances(
            first,
            "TwigB",
            {{1, true, 1.0},
             {3, false, 3.0},
             {5, false, 201.0},
             {7, false, 203.0}}) &&
        // An empty mesh publishes nothing, instanced or not. A record with no
        // points and no indices is indistinguishable on the wire from an ABI
        // v8 instance reference, so an empty prototype would publish a payload
        // record that looks like a record reusing a payload.
        MatchesInstances(first, "Hollow", {}) &&
        // ABI v8 payload elision is topology neutral. Line-list and point-list
        // prototypes carry their geometry once on the lowest published index
        // exactly as a triangle-list prototype does. Proto indices [0, 1, 0]
        // put the second prototype of each pair on instance 1 alone, so its
        // payload record is a non-zero index.
        MatchesInstances(
            first,
            "StrandA",
            {{0, true, 0.0}, {2, false, 2.0}}) &&
        MatchesInstances(first, "StrandB", {{1, true, 1.0}}) &&
        MatchesInstances(
            first,
            "SpeckA",
            {{0, true, 0.0}, {2, false, 2.0}}) &&
        MatchesInstances(first, "SpeckB", {{1, true, 1.0}});

    // Frame 2 swaps the proto indices to [1, 0, 1, 0]. Each prototype now owns
    // the other two instancer instances, so the previous identities retire and
    // the new ones appear rather than the old ones silently changing meaning.
    ParsedPage second;
    if (Sync(session, &second, error, nullptr, 2.0) != OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        return false;
    }
    passed = passed &&
        MatchesInstances(
            second,
            "Alpha",
            {{1, true, 21.0}, {3, false, 23.0}}) &&
        MatchesInstances(
            second,
            "Beta",
            {{0, true, 20.0}, {2, false, 22.0}}) &&
        MatchesRemovals(second, "Alpha", {0, 2}) &&
        MatchesRemovals(second, "Beta", {1, 3}) &&
        // The nested prototypes swap the same way and against the same radix:
        // every composed index still equals outer * 4 + inner, so the two
        // prototypes trade index sets instead of renumbering each other.
        MatchesInstances(
            second,
            "TwigA",
            {{1, true, 1.0},
             {3, false, 3.0},
             {5, false, 201.0},
             {7, false, 203.0}}) &&
        MatchesInstances(
            second,
            "TwigB",
            {{0, true, 0.0},
             {2, false, 2.0},
             {4, false, 200.0},
             {6, false, 202.0}}) &&
        MatchesRemovals(second, "TwigA", {0, 2, 4, 6}) &&
        MatchesRemovals(second, "TwigB", {1, 3, 5, 7});

    // Frame 3 keeps frame 2's assignment and hides instancer instance 0 with
    // invisibleIds. Only Beta's instance 0 retires; its surviving instance
    // keeps index 2 instead of being renumbered down to zero, and inherits the
    // prototype payload because it is now the lowest published index.
    ParsedPage third;
    if (Sync(session, &third, error, nullptr, 3.0) != OPENUSD_STATUS_OK)
    {
        openusd_silk_session_release(session);
        return false;
    }
    passed = passed &&
        MatchesInstances(
            third,
            "Alpha",
            {{1, true, 31.0}, {3, false, 33.0}}) &&
        MatchesInstances(third, "Beta", {{2, true, 32.0}}) &&
        MatchesRemovals(third, "Alpha", {}) &&
        MatchesRemovals(third, "Beta", {0}) &&
        // Hiding inner instance 0 removes exactly the two composed indices it
        // owned under each outer instance -- 0 and 4 -- and leaves TwigA's
        // indices and TwigB's surviving indices untouched. Renumbering the
        // survivors down would have been invisible to a per-prototype radix
        // and is exactly what the authoritative index prevents.
        MatchesInstances(
            third,
            "TwigA",
            {{1, true, 1.0},
             {3, false, 3.0},
             {5, false, 201.0},
             {7, false, 203.0}}) &&
        MatchesInstances(
            third,
            "TwigB",
            {{2, true, 2.0}, {6, false, 202.0}}) &&
        MatchesRemovals(third, "TwigA", {}) &&
        MatchesRemovals(third, "TwigB", {0, 4});

    openusd_silk_session_release(session);
    return passed;
}

bool VerifyBlendShapeProbe(
    const char* pluginPath,
    const char* probeStagePath,
    openusd_error_buffer* error)
{
    const std::string stagePath = BlendShapeProbeStagePath(probeStagePath);
    openusd_silk_session* session = nullptr;
    if (openusd_silk_session_create(pluginPath, stagePath.c_str(), &session, error) !=
        OPENUSD_STATUS_OK)
    {
        return false;
    }
    ParsedPage page;
    const openusd_status status = Sync(session, &page, error, nullptr, 2.0);
    openusd_silk_session_release(session);
    return status == OPENUSD_STATUS_OK &&
        page.found_blend_shape_mesh &&
        page.blend_shape_first_x > 0.49F &&
        page.blend_shape_first_x < 0.51F;
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
    if (!VerifyMaterialXUvChainProjection())
    {
        std::cerr << "hdSilk MaterialX UV chain projection check failed.\n";
        return 4;
    }
    if (!VerifyMaterialXImageArithmeticProjection())
    {
        std::cerr << "hdSilk MaterialX image arithmetic projection check failed.\n";
        return 4;
    }
    if (!VerifyMaterialXConstantFoldProjection())
    {
        std::cerr << "hdSilk MaterialX constant fold projection check failed.\n";
        return 4;
    }
    if (!VerifyMaterialXImageSamplingProjection())
    {
        std::cerr << "hdSilk MaterialX image sampling projection check failed.\n";
        return 4;
    }
    if (!VerifyBlendShapeProbe(argv[1], argv[2], &error))
    {
        std::cerr
            << "hdSilk blend-shape probe did not publish the expected "
            << "deformed point: " << errorText.data() << "\n";
        return 4;
    }
    if (!VerifyPointInstancerProbe(argv[1], argv[2], &error))
    {
        std::cerr
            << "hdSilk point-instancer probe did not publish the expected "
            << "instance identities: " << errorText.data() << "\n";
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
        !VerifyVaryingWidthCurves(initial) ||
        !VerifyUniformWidthCurves(initial) ||
        !VerifyOrientationWinding(initial) ||
        !VerifyCurveWidthResolution() ||
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
    // Medium complexity halves every emitted line segment. The subdivided
    // vertices must carry attributes interpolated at the same parameter as the
    // position, so the authored width ramp stays a ramp instead of collapsing
    // into a step function at the original endpoints.
    {
        const uint32_t mediumComplexity = OPENUSD_SILK_COMPLEXITY_MEDIUM;
        const uint32_t lowComplexity = OPENUSD_SILK_COMPLEXITY_LOW;
        ParsedPage medium;
        ParsedPage low;
        const bool mediumObserved =
            Sync(session, &medium, &error, nullptr, 0.0, &mediumComplexity) ==
                OPENUSD_STATUS_OK &&
            VerifyMediumComplexityInterpolatesWidths(medium);
        const bool lowRestored =
            Sync(session, &low, &error, nullptr, 0.0, &lowComplexity) ==
                OPENUSD_STATUS_OK &&
            VerifyVaryingWidthCurves(low) &&
            VerifyUniformWidthCurves(low);
        if (!mediumObserved || !lowRestored)
        {
            openusd_silk_session_release(session);
            openusd_stage_release(stage);
            std::cerr
                << "hdSilk medium complexity did not interpolate vertex widths: "
                << "mediumTriangles=" << medium.varying_width_curves.triangle_count
                << " lowTriangles=" << low.varying_width_curves.triangle_count
                << "\n";
            return 5;
        }
    }
    // ABI v5: the bound UsdPreviewSurface must arrive resolved, and the mesh must
    // reference it by path. Asserting both together is what proves the material
    // Sprim, the binding, and the wire agree, rather than each in isolation.
    // ABI v13 adds the connected output channel: diffuseColor and metallic are
    // wired to two different outputs of the same UsdUVTexture prim, so nothing
    // but the published channel can tell those two entries apart.
    bool packedChannelsPublished = initial.material_textures.size() == 2;
    if (packedChannelsPublished)
    {
        const auto& diffuseEntry = initial.material_textures[0];
        const auto& metallicEntry = initial.material_textures[1];
        packedChannelsPublished =
            std::get<0>(diffuseEntry) == OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR &&
            std::get<1>(diffuseEntry) == OPENUSD_SILK_TEXTURE_CHANNEL_RGB &&
            std::get<0>(metallicEntry) == OPENUSD_SILK_MATERIAL_METALLIC &&
            std::get<1>(metallicEntry) == OPENUSD_SILK_TEXTURE_CHANNEL_B &&
            std::get<2>(diffuseEntry) == std::get<2>(metallicEntry) &&
            std::get<2>(diffuseEntry).find(MaterialTextureAsset) !=
                std::string::npos;
    }
    if (!initial.material_valid ||
        !initial.found_material_upsert ||
        initial.material_upsert_count != 1 ||
        initial.material_path != MaterialPath ||
        initial.material_surface_kind != OPENUSD_SILK_SURFACE_PREVIEW_SURFACE ||
        initial.material_scalar_count != 1 ||
        initial.material_roughness != 0.375F ||
        initial.material_texture_count != 2 ||
        initial.material_texture_parameter !=
            OPENUSD_SILK_MATERIAL_DIFFUSE_COLOR ||
        initial.material_texture_asset.find(MaterialTextureAsset) ==
            std::string::npos ||
        !packedChannelsPublished ||
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
            << "' channels=";
        for (const auto& entry : initial.material_textures)
        {
            std::cerr << '(' << std::get<0>(entry) << ':' << std::get<1>(entry) << ')';
        }
        std::cerr << " binding='" << initial.shared_material_binding << "'\n";
        return 5;
    }

    // Cull style must arrive as BACK_UNLESS_DOUBLE_SIDED, the usdview default
    // hdSilk declares on its render params. UsdImagingGLRenderParams otherwise
    // defaults to CULL_STYLE_NOTHING, and UsdImaging reports that as the
    // per-prim value because USD gprims author doubleSided rather than a Hydra
    // cull style. That made the renderer resolve "do not cull" for every mesh
    // and silently stop honoring authored single-sidedness. A range check
    // cannot catch this, because NOTHING is a legal value.
    for (uint32_t cullStyle : initial.mesh_cull_styles)
    {
        if (cullStyle != OPENUSD_SILK_CULL_STYLE_BACK_UNLESS_DOUBLE_SIDED)
        {
            openusd_silk_session_release(session);
            openusd_stage_release(stage);
            std::cerr
                << "hdSilk published cull style " << cullStyle
                << " but the render params policy requires "
                << OPENUSD_SILK_CULL_STYLE_BACK_UNLESS_DOUBLE_SIDED
                << " so authored doubleSided is honored.\n";
            return 5;
        }
    }
    if (initial.mesh_cull_styles.empty())
    {
        openusd_silk_session_release(session);
        openusd_stage_release(stage);
        std::cerr << "hdSilk published no meshes, so the cull style policy "
                     "check proved nothing.\n";
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
