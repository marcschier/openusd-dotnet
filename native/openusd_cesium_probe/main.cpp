#include "openusd_cesium.h"

#include <cmath>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

namespace {

struct ProbeState {
    int requests = 0;
    int loadThreadCalls = 0;
    int mainThreadCalls = 0;
    int freeCalls = 0;
    int meshPrimitiveCalls = 0;
    int rootLoadCalls = 0;
    int childLoadCalls = 0;
    int gltfMeshCountInLoadResult = 0;
    int gltfContentCalls = 0;
    int externalContentCalls = 0;
    int emptyContentCalls = 0;
    bool sawDoubleTransform = false;
    bool sawExpectedMesh = false;
    bool sawRootSelection = false;
    bool sawChildSelection = false;
    bool failAsset = false;
    bool failRenderer = false;
};

void appendU32(std::vector<uint8_t>& bytes, uint32_t value) {
    bytes.push_back(static_cast<uint8_t>(value & 0xFF));
    bytes.push_back(static_cast<uint8_t>((value >> 8) & 0xFF));
    bytes.push_back(static_cast<uint8_t>((value >> 16) & 0xFF));
    bytes.push_back(static_cast<uint8_t>((value >> 24) & 0xFF));
}

void appendBytes(std::vector<uint8_t>& bytes, const void* value, size_t size) {
    const auto* first = static_cast<const uint8_t*>(value);
    bytes.insert(bytes.end(), first, first + size);
}

void appendF32(std::vector<uint8_t>& bytes, float value) {
    appendBytes(bytes, &value, sizeof(value));
}

void appendU16(std::vector<uint8_t>& bytes, uint16_t value) {
    appendBytes(bytes, &value, sizeof(value));
}

std::vector<uint8_t> makeGlb(float scale) {
    std::vector<uint8_t> binary;
    const float positions[] = {
        1.0f * scale, 1.0f * scale, 1.0f * scale,
        1.0f * scale, 1.0f * scale, -1.0f * scale,
        1.0f * scale, -1.0f * scale, 1.0f * scale,
        1.0f * scale, -1.0f * scale, -1.0f * scale,
        -1.0f * scale, 1.0f * scale, 1.0f * scale,
        -1.0f * scale, 1.0f * scale, -1.0f * scale,
        -1.0f * scale, -1.0f * scale, 1.0f * scale,
        -1.0f * scale, -1.0f * scale, -1.0f * scale,
    };
    const float normals[] = {
        0.0f, 0.0f, 1.0f,
        0.0f, 0.0f, -1.0f,
        0.0f, 1.0f, 0.0f,
        0.0f, -1.0f, 0.0f,
        -1.0f, 0.0f, 0.0f,
        -1.0f, 0.0f, 0.0f,
        0.0f, -1.0f, 0.0f,
        -1.0f, -1.0f, -1.0f,
    };
    const float texcoords[] = {
        0.0f, 0.0f,
        1.0f, 0.0f,
        1.0f, 1.0f,
        0.0f, 1.0f,
        0.5f, 0.5f,
        0.25f, 0.5f,
        0.5f, 0.25f,
        0.75f, 0.75f,
    };
    const uint16_t indices[] = {
        4, 2, 0, 2, 7, 3, 6, 5, 7, 1, 7, 5,
        0, 3, 1, 4, 1, 5, 4, 6, 2, 2, 6, 7,
        6, 4, 5, 1, 3, 7, 0, 2, 3, 4, 0, 1,
    };
    appendBytes(binary, positions, sizeof(positions));
    appendBytes(binary, normals, sizeof(normals));
    appendBytes(binary, texcoords, sizeof(texcoords));
    appendBytes(binary, indices, sizeof(indices));
    while ((binary.size() % 4) != 0) {
        binary.push_back(0);
    }

    const std::string extent = scale == 2.0f ? "2" : "1";
    std::string json = std::string(R"({"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],)") +
        R"("nodes":[{"mesh":0}],"meshes":[{"primitives":[{"attributes":{"POSITION":0,)" +
        R"("NORMAL":1,"TEXCOORD_0":2},"indices":3}]}],)" +
        R"("buffers":[{"byteLength":328}],)" +
        R"("bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":96,"target":34962},)" +
        R"({"buffer":0,"byteOffset":96,"byteLength":96,"target":34962},)" +
        R"({"buffer":0,"byteOffset":192,"byteLength":64,"target":34962},)" +
        R"({"buffer":0,"byteOffset":256,"byteLength":72,"target":34963}],)" +
        R"("accessors":[{"bufferView":0,"componentType":5126,"count":8,"type":"VEC3","min":[-)" + extent +
        R"(,-)" + extent + R"(,-)" + extent + R"(],"max":[)" + extent +
        R"(,)" + extent + R"(,)" + extent + R"(]},)" +
        R"({"bufferView":1,"componentType":5126,"count":8,"type":"VEC3"},)" +
        R"({"bufferView":2,"componentType":5126,"count":8,"type":"VEC2"},)" +
        R"({"bufferView":3,"componentType":5123,"count":36,"type":"SCALAR"}]})";
    while ((json.size() % 4) != 0) {
        json.push_back(' ');
    }

    std::vector<uint8_t> glb;
    appendBytes(glb, "glTF", 4);
    appendU32(glb, 2u);
    appendU32(glb, static_cast<uint32_t>(12 + 8 + json.size() + 8 + binary.size()));
    appendU32(glb, static_cast<uint32_t>(json.size()));
    appendBytes(glb, "JSON", 4);
    appendBytes(glb, json.data(), json.size());
    appendU32(glb, static_cast<uint32_t>(binary.size()));
    appendBytes(glb, "BIN\0", 4);
    appendBytes(glb, binary.data(), binary.size());
    return glb;
}

std::vector<uint8_t> makeB3dm(float scale) {
    const std::string featureTable = R"({"BATCH_LENGTH":0})";
    std::vector<uint8_t> featureTableJson(featureTable.begin(), featureTable.end());
    while ((featureTableJson.size() % 8) != 0) {
        featureTableJson.push_back(0x20);
    }
    std::vector<uint8_t> glb = makeGlb(scale);
    std::vector<uint8_t> bytes;
    appendBytes(bytes, "b3dm", 4);
    appendU32(bytes, 1u);
    appendU32(bytes, static_cast<uint32_t>(28 + featureTableJson.size() + glb.size()));
    appendU32(bytes, static_cast<uint32_t>(featureTableJson.size()));
    appendU32(bytes, 0u);
    appendU32(bytes, 0u);
    appendU32(bytes, 0u);
    bytes.insert(bytes.end(), featureTableJson.begin(), featureTableJson.end());
    bytes.insert(bytes.end(), glb.begin(), glb.end());
    return bytes;
}

std::vector<uint8_t> copyBytes(const void* source, size_t size) {
    std::vector<uint8_t> result(size);
    std::memcpy(result.data(), source, size);
    return result;
}

bool hasDropLoadCallbackFailpoint() {
#if defined(_WIN32)
    char* value = nullptr;
    size_t valueSize = 0;
    const bool present =
        _dupenv_s(&value, &valueSize, "OPENUSD_CESIUM_PROBE_DROP_LOAD_CALLBACK") == 0 && value != nullptr;
    std::free(value);
    return present;
#else
    return std::getenv("OPENUSD_CESIUM_PROBE_DROP_LOAD_CALLBACK") != nullptr;
#endif
}

bool hasDropMeshCallbackFailpoint() {
#if defined(_WIN32)
    char* value = nullptr;
    size_t valueSize = 0;
    const bool present =
        _dupenv_s(&value, &valueSize, "OPENUSD_CESIUM_PROBE_DROP_MESH_CALLBACK") == 0 && value != nullptr;
    std::free(value);
    return present;
#else
    return std::getenv("OPENUSD_CESIUM_PROBE_DROP_MESH_CALLBACK") != nullptr;
#endif
}

void freeData(void*, const uint8_t* data, size_t) {
    std::free(const_cast<uint8_t*>(data));
}

openusd_cesium_status assetRequest(
    void* userData,
    const char*,
    const char* url,
    const uint8_t*,
    size_t,
    openusd_cesium_asset_response* response,
    openusd_cesium_error_buffer*) {
    auto* state = static_cast<ProbeState*>(userData);
    ++state->requests;
    if (state->failAsset) {
        response->status_code = 404;
        response->content_type = "text/plain";
        return OPENUSD_CESIUM_STATUS_OK;
    }

    std::vector<uint8_t> payload;
    std::string_view urlView(url != nullptr ? url : "");
    if (urlView.find("root.b3dm") != std::string_view::npos) {
        payload = makeB3dm(2.0f);
        response->content_type = "application/octet-stream";
    } else if (urlView.find("tile.b3dm") != std::string_view::npos) {
        payload = makeB3dm(1.0f);
        response->content_type = "application/octet-stream";
    } else if (urlView.find("tileset.json") != std::string_view::npos) {
        const std::string tileset = R"json({
          "asset": { "version": "1.0" },
          "geometricError": 2000,
          "root": {
            "boundingVolume": { "sphere": [0.0, 0.0, 0.0, 2000.0] },
            "geometricError": 2000,
            "refine": "REPLACE",
            "transform": [1,0,0,0, 0,1,0,0, 0,0,1,0, 6378137.123456789,0,0,1],
            "content": { "uri": "root.b3dm" },
            "children": [{
              "boundingVolume": { "sphere": [0.0, 0.0, 0.0, 1000.0] },
              "geometricError": 0,
              "content": { "uri": "tile.b3dm" }
            }]
          }
        })json";
        payload = copyBytes(tileset.data(), tileset.size());
        response->content_type = "application/json";
    } else {
        response->status_code = 404;
        response->content_type = "text/plain";
        return OPENUSD_CESIUM_STATUS_OK;
    }

    auto* data = static_cast<uint8_t*>(std::malloc(payload.size()));
    if (data == nullptr) {
        return OPENUSD_CESIUM_STATUS_NATIVE_ERROR;
    }
    std::memcpy(data, payload.data(), payload.size());
    response->status_code = 200;
    response->data = data;
    response->data_size = payload.size();
    response->free_data = freeData;
    return OPENUSD_CESIUM_STATUS_OK;
}

void* prepareLoad(
    void* userData,
    const openusd_cesium_tile_load_result* loadResult,
    const openusd_cesium_matrix4d* transform,
    openusd_cesium_error_buffer*) {
    auto* state = static_cast<ProbeState*>(userData);
    ++state->loadThreadCalls;
    if (state->failRenderer) {
        return nullptr;
    }
    if (loadResult != nullptr && loadResult->content_kind == OPENUSD_CESIUM_TILE_CONTENT_GLTF_MODEL &&
        transform != nullptr && std::abs(transform->values[12] - 6378137.123456789) < 0.000001) {
        state->sawDoubleTransform = true;
    }
    if (loadResult != nullptr) {
        state->gltfMeshCountInLoadResult += static_cast<int>(loadResult->gltf_mesh_count);
        if (loadResult->content_kind == OPENUSD_CESIUM_TILE_CONTENT_GLTF_MODEL) {
            ++state->gltfContentCalls;
        } else if (loadResult->content_kind == OPENUSD_CESIUM_TILE_CONTENT_EXTERNAL_TILESET) {
            ++state->externalContentCalls;
        } else if (loadResult->content_kind == OPENUSD_CESIUM_TILE_CONTENT_EMPTY) {
            ++state->emptyContentCalls;
        }
    }
    const std::string_view url(loadResult != nullptr && loadResult->completed_request_url != nullptr
        ? loadResult->completed_request_url
        : "");
    if (url.find("root.b3dm") != std::string_view::npos) {
        ++state->rootLoadCalls;
    }
    if (url.find("tile.b3dm") != std::string_view::npos) {
        ++state->childLoadCalls;
    }
    return new int(17);
}

void* prepareMain(void* userData, void* loadThreadResource, openusd_cesium_error_buffer*) {
    auto* state = static_cast<ProbeState*>(userData);
    ++state->mainThreadCalls;
    int value = loadThreadResource != nullptr ? *static_cast<int*>(loadThreadResource) : 0;
    delete static_cast<int*>(loadThreadResource);
    return new int(value + 1);
}

void freeResources(void* userData, void* loadThreadResource, void* mainThreadResource) {
    auto* state = static_cast<ProbeState*>(userData);
    ++state->freeCalls;
    delete static_cast<int*>(loadThreadResource);
    delete static_cast<int*>(mainThreadResource);
}

void meshPrimitive(void* userData, const openusd_cesium_mesh_primitive* primitive) {
    auto* state = static_cast<ProbeState*>(userData);
    ++state->meshPrimitiveCalls;
    if (primitive == nullptr || primitive->position_count != 8 ||
        primitive->face_count != 12 || primitive->face_vertex_index_count != 36 ||
        primitive->normal_count != 8 || primitive->texcoord_0_count != 8) {
        return;
    }
    const size_t middlePoint = primitive->position_count / 2;
    const size_t lastPoint = primitive->position_count - 1;
    const size_t middleIndex = primitive->face_vertex_index_count / 2;
    const size_t lastIndex = primitive->face_vertex_index_count - 1;
    const bool pointsMatch =
        primitive->positions[0].x == 1.0f &&
        primitive->positions[0].y == 1.0f &&
        primitive->positions[0].z == 1.0f &&
        primitive->positions[middlePoint].x == -1.0f &&
        primitive->positions[middlePoint].y == 1.0f &&
        primitive->positions[middlePoint].z == 1.0f &&
        primitive->positions[lastPoint].x == -1.0f &&
        primitive->positions[lastPoint].y == -1.0f &&
        primitive->positions[lastPoint].z == -1.0f;
    const bool topologyMatches =
        primitive->face_vertex_counts[0] == 3 &&
        primitive->face_vertex_counts[primitive->face_count / 2] == 3 &&
        primitive->face_vertex_counts[primitive->face_count - 1] == 3 &&
        primitive->face_vertex_indices[0] == 4 &&
        primitive->face_vertex_indices[middleIndex] == 4 &&
        primitive->face_vertex_indices[lastIndex] == 1;
    const bool normalsMatch =
        primitive->normals[0].z == 1.0f &&
        primitive->normals[middlePoint].x == -1.0f &&
        primitive->normals[lastPoint].z == -1.0f;
    const bool texcoordsMatch =
        primitive->texcoords_0[0].x == 0.0f &&
        primitive->texcoords_0[middlePoint].y == 0.5f &&
        primitive->texcoords_0[lastPoint].x == 0.75f;
    const bool transformMatches =
        primitive->transform != nullptr &&
        std::abs(primitive->transform->values[12] - 6378137.123456789) < 0.000001;
    state->sawExpectedMesh = state->sawExpectedMesh ||
        (pointsMatch && topologyMatches && normalsMatch && texcoordsMatch && transformMatches);
}

void startTask(void*, openusd_cesium_task* task) {
    std::thread([task]() {
        (void)openusd_cesium_task_execute(task);
        openusd_cesium_task_destroy(task);
    }).detach();
}

bool runProbe(bool failAsset, bool failRenderer) {
    ProbeState state{};
    state.failAsset = failAsset;
    state.failRenderer = failRenderer;

    openusd_cesium_asset_accessor asset{};
    asset.struct_size = sizeof(asset);
    asset.version = OPENUSD_CESIUM_ASSET_ACCESSOR_VERSION;
    asset.user_data = &state;
    asset.request = assetRequest;

    openusd_cesium_renderer_callbacks renderer{};
    renderer.struct_size = sizeof(renderer);
    renderer.version = OPENUSD_CESIUM_RENDERER_CALLBACKS_VERSION;
    renderer.user_data = &state;
    renderer.prepare_in_load_thread = hasDropLoadCallbackFailpoint() ? nullptr : prepareLoad;
    renderer.prepare_in_main_thread = prepareMain;
    renderer.free_resources = freeResources;
    renderer.mesh_primitive_in_load_thread = hasDropMeshCallbackFailpoint() ? nullptr : meshPrimitive;

    openusd_cesium_task_processor tasks{};
    tasks.struct_size = sizeof(tasks);
    tasks.version = OPENUSD_CESIUM_TASK_PROCESSOR_VERSION;
    tasks.start_task = startTask;

    openusd_cesium_tileset_options options{};
    options.struct_size = sizeof(options);
    options.version = OPENUSD_CESIUM_TILESET_OPTIONS_VERSION;
    options.maximum_screen_space_error = 16.0;

    char errorData[512]{};
    openusd_cesium_error_buffer error{errorData, sizeof(errorData), 0};
    openusd_cesium_tileset* tileset = nullptr;
    if (openusd_cesium_tileset_create("memory://tileset.json", &asset, &renderer, &tasks, &options, &tileset, &error) != OPENUSD_CESIUM_STATUS_OK) {
        std::cerr << "create failed: " << errorData << '\n';
        return false;
    }

    openusd_cesium_view_state view{};
    view.struct_size = sizeof(view);
    view.version = OPENUSD_CESIUM_VIEW_STATE_VERSION;
    view.position_ecef = {6378137.123456789 + 1000.0, 0.0, 0.0};
    view.direction_ecef = {-1.0, 0.0, 0.0};
    view.up_ecef = {0.0, 0.0, 1.0};
    view.viewport_width = 1024.0;
    view.viewport_height = 768.0;
    view.horizontal_fov_radians = 1.0;
    view.vertical_fov_radians = 0.75;

    openusd_cesium_update_result update{};
    update.struct_size = sizeof(update);
    for (int i = 0; i < 100; ++i) {
        if (openusd_cesium_tileset_update_view(tileset, &view, 1, 0.016f, &update, &error) != OPENUSD_CESIUM_STATUS_OK) {
            std::cerr << "update failed: " << errorData << '\n';
            (void)openusd_cesium_tileset_release(tileset, &error);
            return false;
        }
        if ((failAsset && state.requests > 0 && update.worker_thread_tile_load_queue_length == 0 &&
             update.main_thread_tile_load_queue_length == 0) ||
            (!failAsset && state.mainThreadCalls > 0 && update.worker_thread_tile_load_queue_length == 0 &&
             update.main_thread_tile_load_queue_length == 0)) {
            break;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
    state.sawChildSelection =
        state.childLoadCalls > 0 &&
        state.rootLoadCalls == 0 &&
        update.tiles_to_render_count > 0;

    view.position_ecef = {6378137.123456789 + 10000000.0, 0.0, 0.0};
    for (int i = 0; i < 100 && !failAsset && !failRenderer; ++i) {
        if (openusd_cesium_tileset_update_view(tileset, &view, 1, 0.016f, &update, &error) != OPENUSD_CESIUM_STATUS_OK) {
            std::cerr << "far update failed: " << errorData << '\n';
            (void)openusd_cesium_tileset_release(tileset, &error);
            return false;
        }
        if (state.rootLoadCalls > 0 && update.worker_thread_tile_load_queue_length == 0 &&
            update.main_thread_tile_load_queue_length == 0) {
            break;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(10));
    }
    state.sawRootSelection =
        state.rootLoadCalls > 0 &&
        state.childLoadCalls > 0 &&
        update.tiles_to_render_count > 0;

    size_t messageCount = 0;
    (void)openusd_cesium_tileset_get_message_count(tileset, &messageCount, &error);
    (void)openusd_cesium_tileset_release(tileset, &error);

    if (failAsset) {
        return messageCount > 0 && state.loadThreadCalls == 0;
    }
    if (failRenderer) {
        return state.loadThreadCalls > 0 && state.mainThreadCalls > 0 && !state.sawDoubleTransform;
    }
    std::cout << "requests=" << state.requests
              << " load=" << state.loadThreadCalls
              << " main=" << state.mainThreadCalls
              << " mesh=" << state.meshPrimitiveCalls
              << " sawMesh=" << state.sawExpectedMesh
              << " gltfMeshes=" << state.gltfMeshCountInLoadResult
              << " gltfContent=" << state.gltfContentCalls
              << " external=" << state.externalContentCalls
              << " empty=" << state.emptyContentCalls
              << " root=" << state.rootLoadCalls
              << " child=" << state.childLoadCalls
              << " free=" << state.freeCalls
              << " render=" << update.tiles_to_render_count
              << " loaded=" << update.loaded_tile_count << '\n';
    return state.requests >= 2 && state.loadThreadCalls > 0 && state.mainThreadCalls > 0 &&
           state.freeCalls > 0 && state.sawDoubleTransform &&
           state.gltfContentCalls > 0 && state.sawRootSelection && state.sawChildSelection &&
           state.meshPrimitiveCalls > 0 && state.sawExpectedMesh && update.loaded_tile_count > 0;
}

} // namespace

int main(int argc, char** argv) {
    const bool failAsset = argc > 1 && std::string_view(argv[1]) == "--expect-asset-failure";
    const bool failRenderer = argc > 1 && std::string_view(argv[1]) == "--expect-renderer-break";
    if (!runProbe(failAsset, failRenderer)) {
        return failAsset || failRenderer ? 3 : 1;
    }
    return 0;
}