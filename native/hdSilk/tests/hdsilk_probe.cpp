// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_hdsilk.h"
#include "hdsilk_test_hooks.h"
#include "openusd_dotnet_test_hooks.h"
#include "sceneState.h"

#include "pxr/base/gf/frustum.h"
#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec3d.h"

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
static_assert(OPENUSD_SILK_SESSION_ABI_VERSION == 4);
static_assert(OPENUSD_SILK_PAGE_ABI_VERSION == 2);

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
            uint32_t pathSize = 0;
            uint32_t pointCount = 0;
            uint32_t indexCount = 0;
            uint32_t triangleCount = 0;
            constexpr size_t pathOffset = 200;
            if (ReadValue(data, size, offset + 8, &stableHash) &&
                ReadValue(data, size, offset + 16, &primId) &&
                ReadValue(data, size, offset + 20, &instanceId) &&
                ReadValue(data, size, offset + 24, &instanceIndex) &&
                ReadValue(data, size, offset + 28, &topologyKind) &&
                ReadValue(data, size, offset + 32, &topologyRevision) &&
                ReadValue(data, size, offset + 40, &pathSize) &&
                ReadValue(data, size, offset + 44, &pointCount) &&
                ReadValue(data, size, offset + 48, &indexCount) &&
                ReadValue(data, size, offset + 52, &triangleCount))
            {
                size_t pointBytes = 0;
                size_t indexBytes = 0;
                size_t subprimBytes = 0;
                size_t expectedSize = pathOffset;
                const bool sizesValid =
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
                    expectedSize == byteSize &&
                    static_cast<uint64_t>(triangleCount) * 3 == indexCount;
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
                    result.mesh_identities.push_back(
                        ParsedMeshIdentity{path, primId, topologyRevision});
                    result.mesh_identity_valid &=
                        !path.empty() &&
                        path.front() == '/' &&
                        stableHash == ComputeStableHash(path) &&
                        primId >= 0 &&
                        topologyKind == OPENUSD_SILK_TOPOLOGY_TRIANGLE_LIST &&
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
                    }
                    else if (path == TopologyMeshPath)
                    {
                        result.found_topology_upsert = true;
                        result.topology_subprims = std::move(subprims);
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
            constexpr size_t pathSizeOffset = 8 + 8;
            constexpr size_t pathOffset = 8 + 8 + 4;
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

bool RejectsInvalidSceneStateRecord(HdSilkMeshRecord record)
{
    try
    {
        HdSilkSceneState state;
        state.UpsertMesh(record.path, std::move(record));
        static_cast<void>(state.BuildPage(nullptr, nullptr));
        return false;
    }
    catch (const std::invalid_argument&)
    {
        return true;
    }
}

bool VerifySceneStateSerialization()
{
    HdSilkSceneState state;
    state.UpsertMesh("/Zed", MakeSceneStateRecord("/Zed", 2));
    state.UpsertMesh("/Alpha", MakeSceneStateRecord("/Alpha", 1, 5));
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
    state.UpsertMesh("/Alpha", MakeSceneStateRecord("/Alpha", 9, 1));
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
    invalidInstance.instanceIndex = 1;
    return RejectsInvalidSceneStateRecord(std::move(invalidPoints)) &&
        RejectsInvalidSceneStateRecord(std::move(invalidMapping)) &&
        RejectsInvalidSceneStateRecord(std::move(invalidInstance));
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
    return parsed.frame_width == 64 &&
        parsed.frame_height == 64 &&
        std::memcmp(
            parsed.frame_view.data(),
            camera.view,
            sizeof(camera.view)) == 0 &&
        std::memcmp(
            parsed.frame_projection.data(),
            camera.projection,
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

    const size_t initialStageCoreCount = openusd_test_get_live_stage_core_count();
    openusd_stage* stage = nullptr;
    if (openusd_stage_open(argv[2], &stage, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_retain(stage, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_edit_target_session_layer(stage, &error) != OPENUSD_STATUS_OK ||
        !AuthorSharedMesh(stage, 0.0F, &error) ||
        !AuthorTopologyMesh(stage, &error))
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
