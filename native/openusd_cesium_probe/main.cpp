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
    bool sawDoubleTransform = false;
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

std::vector<uint8_t> makeGlb() {
    std::vector<uint8_t> binary;
    appendF32(binary, 0.0f); appendF32(binary, 0.0f); appendF32(binary, 0.0f);
    appendF32(binary, 1.0f); appendF32(binary, 0.0f); appendF32(binary, 0.0f);
    appendF32(binary, 0.0f); appendF32(binary, 1.0f); appendF32(binary, 0.0f);
    appendU16(binary, 0); appendU16(binary, 1); appendU16(binary, 2);
    while ((binary.size() % 4) != 0) {
        binary.push_back(0);
    }

    std::string json = R"({"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],"meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1}]}],"buffers":[{"byteLength":44}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36,"target":34962},{"buffer":0,"byteOffset":36,"byteLength":6,"target":34963}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]},{"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"}]})";
    while ((json.size() % 4) != 0) {
        json.push_back(' ');
    }

    std::vector<uint8_t> bytes;
    appendU32(bytes, 0x46546C67u);
    appendU32(bytes, 2u);
    appendU32(bytes, static_cast<uint32_t>(12 + 8 + json.size() + 8 + binary.size()));
    appendU32(bytes, static_cast<uint32_t>(json.size()));
    appendU32(bytes, 0x4E4F534Au);
    bytes.insert(bytes.end(), json.begin(), json.end());
    appendU32(bytes, static_cast<uint32_t>(binary.size()));
    appendU32(bytes, 0x004E4942u);
    bytes.insert(bytes.end(), binary.begin(), binary.end());
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
    if (urlView.find("tileset.json") != std::string_view::npos) {
        const std::string tileset = R"json({
          "asset": { "version": "1.0" },
          "geometricError": 500,
          "root": {
            "boundingVolume": { "sphere": [0.0, 0.0, 0.0, 500.0] },
            "geometricError": 500,
            "refine": "ADD",
            "transform": [1,0,0,0, 0,1,0,0, 0,0,1,0, 6378137.123456789,0,0,1],
            "children": [{
              "boundingVolume": { "sphere": [0.0, 0.0, 0.0, 100.0] },
              "geometricError": 0,
              "content": { "uri": "tile.glb" }
            }]
          }
        })json";
        payload = copyBytes(tileset.data(), tileset.size());
        response->content_type = "application/json";
    } else if (urlView.find("tile.glb") != std::string_view::npos) {
        payload = makeGlb();
        response->content_type = "model/gltf-binary";
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
              << " free=" << state.freeCalls
              << " render=" << update.tiles_to_render_count
              << " loaded=" << update.loaded_tile_count << '\n';
    return state.requests >= 2 && state.loadThreadCalls > 0 && state.mainThreadCalls > 0 &&
           state.freeCalls > 0 && state.sawDoubleTransform && update.loaded_tile_count > 0;
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