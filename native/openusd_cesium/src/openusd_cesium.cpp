// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_cesium.h"

#include <Cesium3DTilesSelection/IPrepareRendererResources.h>
#include <Cesium3DTilesSelection/TileLoadResult.h>
#include <Cesium3DTilesSelection/Tileset.h>
#include <Cesium3DTilesSelection/TilesetLoadFailureDetails.h>
#include <Cesium3DTilesSelection/TilesetOptions.h>
#include <Cesium3DTilesSelection/ViewState.h>
#include <Cesium3DTilesContent/registerAllTileContentTypes.h>
#include <CesiumAsync/AsyncSystem.h>
#include <CesiumAsync/IAssetAccessor.h>
#include <CesiumAsync/IAssetRequest.h>
#include <CesiumAsync/IAssetResponse.h>
#include <CesiumAsync/ITaskProcessor.h>
#include <CesiumGltf/AccessorView.h>
#include <CesiumGltf/Model.h>
#include <CesiumRasterOverlays/RasterOverlayTile.h>

#include <algorithm>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <exception>
#include <functional>
#include <memory>
#include <mutex>
#include <span>
#include <string>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>


using Cesium3DTilesSelection::TileLoadResult;

struct Message {
    openusd_cesium_message_severity severity;
    std::string text;
};

void WriteError(openusd_cesium_error_buffer* error, std::string_view message) noexcept {
    if (error == nullptr) {
        return;
    }
    error->required = message.size() + 1;
    if (error->data == nullptr || error->capacity == 0) {
        return;
    }
    const size_t count = std::min(message.size(), error->capacity - 1);
    std::memcpy(error->data, message.data(), count);
    error->data[count] = '\0';
}

void ResetError(openusd_cesium_error_buffer* error) noexcept {
    if (error != nullptr) {
        error->required = 0;
        if (error->data != nullptr && error->capacity != 0) {
            error->data[0] = '\0';
        }
    }
}

template <typename TAction>
openusd_cesium_status Guard(openusd_cesium_error_buffer* error, TAction&& action) noexcept {
    try {
        ResetError(error);
        return action();
    } catch (const std::exception& ex) {
        WriteError(error, ex.what());
        return OPENUSD_CESIUM_STATUS_NATIVE_ERROR;
    } catch (...) {
        WriteError(error, "Unknown native exception.");
        return OPENUSD_CESIUM_STATUS_NATIVE_ERROR;
    }
}

openusd_cesium_status CopyString(
    const std::string& value,
    char* buffer,
    size_t capacity,
    size_t* required) noexcept {
    if (required == nullptr) {
        return OPENUSD_CESIUM_STATUS_INVALID_ARGUMENT;
    }
    *required = value.size() + 1;
    if (buffer == nullptr || capacity < *required) {
        return OPENUSD_CESIUM_STATUS_BUFFER_TOO_SMALL;
    }
    std::memcpy(buffer, value.c_str(), *required);
    return OPENUSD_CESIUM_STATUS_OK;
}

bool HasBytes(uint32_t structSize, size_t offset, size_t size) noexcept {
    return structSize >= offset && size <= static_cast<size_t>(structSize - offset);
}

glm::dvec3 ToDVec3(const openusd_cesium_vec3d& value) noexcept {
    return {value.x, value.y, value.z};
}

glm::dmat4 ToDMat4(const openusd_cesium_matrix4d& value) noexcept {
    glm::dmat4 result(1.0);
    for (int column = 0; column < 4; ++column) {
        for (int row = 0; row < 4; ++row) {
            result[column][row] = value.values[column * 4 + row];
        }
    }
    return result;
}

openusd_cesium_matrix4d FromDMat4(const glm::dmat4& value) noexcept {
    openusd_cesium_matrix4d result{};
    for (int column = 0; column < 4; ++column) {
        for (int row = 0; row < 4; ++row) {
            result.values[column * 4 + row] = value[column][row];
        }
    }
    return result;
}

class CallbackAssetResponse final : public CesiumAsync::IAssetResponse {
public:
    CallbackAssetResponse(
        uint16_t statusCode,
        std::string contentType,
        std::vector<std::byte>&& data) noexcept
        : _statusCode(statusCode)
        , _contentType(std::move(contentType))
        , _data(std::move(data)) {
    }

    uint16_t statusCode() const override { return _statusCode; }
    std::string contentType() const override { return _contentType; }
    const CesiumAsync::HttpHeaders& headers() const override { return _headers; }
    std::span<const std::byte> data() const override { return _data; }

private:
    uint16_t _statusCode;
    std::string _contentType;
    std::vector<std::byte> _data;
    CesiumAsync::HttpHeaders _headers;
};

class CallbackAssetRequest final : public CesiumAsync::IAssetRequest {
public:
    CallbackAssetRequest(
        std::string method,
        std::string url,
        std::unique_ptr<CallbackAssetResponse>&& response) noexcept
        : _method(std::move(method))
        , _url(std::move(url))
        , _response(std::move(response)) {
    }

    const std::string& method() const override { return _method; }
    const std::string& url() const override { return _url; }
    const CesiumAsync::HttpHeaders& headers() const override { return _headers; }
    const CesiumAsync::IAssetResponse* response() const override { return _response.get(); }

private:
    std::string _method;
    std::string _url;
    CesiumAsync::HttpHeaders _headers;
    std::unique_ptr<CallbackAssetResponse> _response;
};

class CallbackAssetAccessor final : public CesiumAsync::IAssetAccessor {
public:
    explicit CallbackAssetAccessor(openusd_cesium_asset_accessor callbacks) noexcept
        : _callbacks(callbacks) {
    }

    CesiumAsync::Future<std::shared_ptr<CesiumAsync::IAssetRequest>> get(
        const CesiumAsync::AsyncSystem& asyncSystem,
        const std::string& url,
        const std::vector<THeader>& headers) override {
        static_cast<void>(headers);
        return request(asyncSystem, "GET", url, {}, {});
    }

    CesiumAsync::Future<std::shared_ptr<CesiumAsync::IAssetRequest>> request(
        const CesiumAsync::AsyncSystem& asyncSystem,
        const std::string& verb,
        const std::string& url,
        const std::vector<THeader>& headers,
        const std::span<const std::byte>& contentPayload) override {
        static_cast<void>(headers);
        return asyncSystem.runInWorkerThread([callbacks = _callbacks, verb, url,
            payload = std::vector<std::byte>(contentPayload.begin(), contentPayload.end())]() mutable {
            if (callbacks.request == nullptr) {
                return std::shared_ptr<CesiumAsync::IAssetRequest>{};
            }

            openusd_cesium_asset_response response{};
            response.struct_size = sizeof(response);
            char errorData[512]{};
            openusd_cesium_error_buffer error{errorData, sizeof(errorData), 0};
            const openusd_cesium_status status = callbacks.request(
                callbacks.user_data,
                verb.c_str(),
                url.c_str(),
                reinterpret_cast<const uint8_t*>(payload.data()),
                payload.size(),
                &response,
                &error);

            std::vector<std::byte> bytes;
            if (status == OPENUSD_CESIUM_STATUS_OK && response.data != nullptr && response.data_size != 0) {
                const auto* begin = reinterpret_cast<const std::byte*>(response.data);
                bytes.assign(begin, begin + response.data_size);
            }
            if (response.free_data != nullptr && response.data != nullptr) {
                response.free_data(response.user_data, response.data, response.data_size);
            }

            const uint16_t statusCode = status == OPENUSD_CESIUM_STATUS_OK ? response.status_code : 500;
            std::string contentType = response.content_type != nullptr ? response.content_type : "application/octet-stream";
            auto assetResponse = std::make_unique<CallbackAssetResponse>(statusCode, std::move(contentType), std::move(bytes));
            return std::shared_ptr<CesiumAsync::IAssetRequest>(
                std::make_shared<CallbackAssetRequest>(verb, url, std::move(assetResponse)));
        });
    }

    void tick() noexcept override {}

private:
    openusd_cesium_asset_accessor _callbacks;
};

struct openusd_cesium_task {
    std::function<void()> action;
    std::atomic<bool> executed{false};
};

class CallbackTaskProcessor final : public CesiumAsync::ITaskProcessor {
public:
    explicit CallbackTaskProcessor(openusd_cesium_task_processor callbacks) noexcept
        : _callbacks(callbacks) {
    }

    void startTask(std::function<void()> f) override {
        if (_callbacks.start_task == nullptr) {
            std::thread(std::move(f)).detach();
            return;
        }
        auto* task = new openusd_cesium_task{std::move(f)};
        _callbacks.start_task(_callbacks.user_data, task);
    }

private:
    openusd_cesium_task_processor _callbacks;
};

openusd_cesium_tile_content_kind GetContentKind(const TileLoadResult& result) noexcept {
    if (std::holds_alternative<Cesium3DTilesSelection::TileUnknownContent>(result.contentKind)) {
        return OPENUSD_CESIUM_TILE_CONTENT_UNKNOWN;
    }
    if (std::holds_alternative<Cesium3DTilesSelection::TileEmptyContent>(result.contentKind)) {
        return OPENUSD_CESIUM_TILE_CONTENT_EMPTY;
    }
    if (std::holds_alternative<Cesium3DTilesSelection::TileExternalContent>(result.contentKind)) {
        return OPENUSD_CESIUM_TILE_CONTENT_EXTERNAL_TILESET;
    }
    return OPENUSD_CESIUM_TILE_CONTENT_GLTF_MODEL;
}

openusd_cesium_tile_load_state GetLoadState(const TileLoadResult& result) noexcept {
    switch (result.state) {
    case Cesium3DTilesSelection::TileLoadResultState::Success:
        return OPENUSD_CESIUM_TILE_LOAD_SUCCESS;
    case Cesium3DTilesSelection::TileLoadResultState::Failed:
        return OPENUSD_CESIUM_TILE_LOAD_FAILED;
    case Cesium3DTilesSelection::TileLoadResultState::RetryLater:
        return OPENUSD_CESIUM_TILE_LOAD_RETRY_LATER;
    }
    return OPENUSD_CESIUM_TILE_LOAD_FAILED;
}

openusd_cesium_tile_load_result MakeLoadResultView(const TileLoadResult& result) noexcept {
    openusd_cesium_tile_load_result view{};
    view.struct_size = sizeof(view);
    view.version = OPENUSD_CESIUM_TILE_LOAD_RESULT_VERSION;
    view.state = GetLoadState(result);
    view.content_kind = GetContentKind(result);
    if (const auto* model = std::get_if<CesiumGltf::Model>(&result.contentKind)) {
        view.gltf_mesh_count = static_cast<uint32_t>(std::min<size_t>(model->meshes.size(), UINT32_MAX));
        view.gltf_node_count = static_cast<uint32_t>(std::min<size_t>(model->nodes.size(), UINT32_MAX));
    }
    if (result.pCompletedRequest) {
        view.completed_request_url = result.pCompletedRequest->url().c_str();
        if (const CesiumAsync::IAssetResponse* response = result.pCompletedRequest->response()) {
            view.completed_request_status_code = response->statusCode();
        }
    }
    return view;
}

const CesiumGltf::Accessor* GetAccessor(const CesiumGltf::Model& model, int64_t index) noexcept {
    if (index < 0 || static_cast<size_t>(index) >= model.accessors.size()) {
        return nullptr;
    }
    return &model.accessors[static_cast<size_t>(index)];
}

bool TryReadPositions(
    const CesiumGltf::Model& model,
    int64_t accessorIndex,
    std::vector<openusd_cesium_vec3f>& positions) {
    CesiumGltf::AccessorView<glm::vec3> view(model, static_cast<int32_t>(accessorIndex));
    if (view.status() != CesiumGltf::AccessorViewStatus::Valid || view.size() < 0) {
        return false;
    }
    const size_t count = static_cast<size_t>(view.size());
    positions.resize(count);
    for (size_t index = 0; index < count; ++index) {
        const glm::vec3& position = view[static_cast<int64_t>(index)];
        positions[index] = openusd_cesium_vec3f{position.x, position.y, position.z};
    }
    return true;
}

bool TryReadVec3Attribute(
    const CesiumGltf::Model& model,
    int64_t accessorIndex,
    std::vector<openusd_cesium_vec3f>& values) {
    CesiumGltf::AccessorView<glm::vec3> view(model, static_cast<int32_t>(accessorIndex));
    if (view.status() != CesiumGltf::AccessorViewStatus::Valid || view.size() < 0) {
        return false;
    }
    const size_t count = static_cast<size_t>(view.size());
    values.resize(count);
    for (size_t index = 0; index < count; ++index) {
        const glm::vec3& value = view[static_cast<int64_t>(index)];
        values[index] = openusd_cesium_vec3f{value.x, value.y, value.z};
    }
    return true;
}

bool TryReadVec2Attribute(
    const CesiumGltf::Model& model,
    int64_t accessorIndex,
    std::vector<openusd_cesium_vec2f>& values) {
    CesiumGltf::AccessorView<glm::vec2> view(model, static_cast<int32_t>(accessorIndex));
    if (view.status() != CesiumGltf::AccessorViewStatus::Valid || view.size() < 0) {
        return false;
    }
    const size_t count = static_cast<size_t>(view.size());
    values.resize(count);
    for (size_t index = 0; index < count; ++index) {
        const glm::vec2& value = view[static_cast<int64_t>(index)];
        values[index] = openusd_cesium_vec2f{value.x, value.y};
    }
    return true;
}

template <typename TIndex>
bool CopyIndices(
    const CesiumGltf::Model& model,
    int64_t accessorIndex,
    size_t vertexCount,
    std::vector<int32_t>& indices) {
    CesiumGltf::AccessorView<TIndex> view(model, static_cast<int32_t>(accessorIndex));
    if (view.status() != CesiumGltf::AccessorViewStatus::Valid || view.size() < 0) {
        return false;
    }
    const size_t count = static_cast<size_t>(view.size());
    indices.resize(count);
    for (size_t index = 0; index < count; ++index) {
        const auto value = static_cast<uint32_t>(view[static_cast<int64_t>(index)]);
        if (value >= vertexCount || value > static_cast<uint32_t>(INT32_MAX)) {
            return false;
        }
        indices[index] = static_cast<int32_t>(value);
    }
    return true;
}

bool TryReadIndices(
    const CesiumGltf::Model& model,
    int64_t accessorIndex,
    size_t vertexCount,
    std::vector<int32_t>& indices) {
    const CesiumGltf::Accessor* accessor = GetAccessor(model, accessorIndex);
    if (accessor == nullptr || accessor->type != CesiumGltf::Accessor::Type::SCALAR ||
        accessor->bufferView < 0 || accessor->count < 0) {
        return false;
    }
    if (accessor->componentType == 5121) {
        return CopyIndices<uint8_t>(model, accessorIndex, vertexCount, indices);
    }
    if (accessor->componentType == 5123) {
        return CopyIndices<uint16_t>(model, accessorIndex, vertexCount, indices);
    }
    if (accessor->componentType == 5125) {
        return CopyIndices<uint32_t>(model, accessorIndex, vertexCount, indices);
    }
    return false;
}

void EmitMeshPrimitives(
    const openusd_cesium_renderer_callbacks& callbacks,
    const TileLoadResult& result,
    const openusd_cesium_matrix4d& transform) {
    if (callbacks.mesh_primitive_in_load_thread == nullptr) {
        return;
    }
    const auto* model = std::get_if<CesiumGltf::Model>(&result.contentKind);
    if (model == nullptr) {
        return;
    }
    for (size_t meshIndex = 0; meshIndex < model->meshes.size(); ++meshIndex) {
        const CesiumGltf::Mesh& mesh = model->meshes[meshIndex];
        for (size_t primitiveIndex = 0; primitiveIndex < mesh.primitives.size(); ++primitiveIndex) {
            const CesiumGltf::MeshPrimitive& primitive = mesh.primitives[primitiveIndex];
            if ((primitive.mode != -1 && primitive.mode != 4) || primitive.indices < 0) {
                continue;
            }
            const auto position = primitive.attributes.find("POSITION");
            if (position == primitive.attributes.end()) {
                continue;
            }
            std::vector<openusd_cesium_vec3f> positions;
            if (!TryReadPositions(*model, position->second, positions)) {
                continue;
            }
            std::vector<int32_t> indices;
            if (!TryReadIndices(*model, primitive.indices, positions.size(), indices) ||
                indices.empty() || (indices.size() % 3) != 0) {
                continue;
            }
            std::vector<openusd_cesium_vec3f> normals;
            const auto normal = primitive.attributes.find("NORMAL");
            if (normal != primitive.attributes.end() &&
                !TryReadVec3Attribute(*model, normal->second, normals)) {
                normals.clear();
            }
            std::vector<openusd_cesium_vec2f> texcoords;
            const auto texcoord = primitive.attributes.find("TEXCOORD_0");
            if (texcoord != primitive.attributes.end() &&
                !TryReadVec2Attribute(*model, texcoord->second, texcoords)) {
                texcoords.clear();
            }
            std::vector<int32_t> counts(indices.size() / 3, 3);
            openusd_cesium_mesh_primitive meshView{};
            meshView.struct_size = sizeof(meshView);
            meshView.version = OPENUSD_CESIUM_MESH_PRIMITIVE_VERSION;
            meshView.mesh_index = static_cast<uint32_t>(std::min<size_t>(meshIndex, UINT32_MAX));
            meshView.primitive_index = static_cast<uint32_t>(std::min<size_t>(primitiveIndex, UINT32_MAX));
            meshView.transform = &transform;
            meshView.positions = positions.data();
            meshView.position_count = positions.size();
            meshView.face_vertex_counts = counts.data();
            meshView.face_count = counts.size();
            meshView.face_vertex_indices = indices.data();
            meshView.face_vertex_index_count = indices.size();
            meshView.normals = normals.data();
            meshView.normal_count = normals.size();
            meshView.texcoords_0 = texcoords.data();
            meshView.texcoord_0_count = texcoords.size();
            callbacks.mesh_primitive_in_load_thread(callbacks.user_data, &meshView);
        }
    }
}

class CallbackPrepareRendererResources final : public Cesium3DTilesSelection::IPrepareRendererResources {
public:
    explicit CallbackPrepareRendererResources(openusd_cesium_renderer_callbacks callbacks) noexcept
        : _callbacks(callbacks) {
    }

    CesiumAsync::Future<Cesium3DTilesSelection::TileLoadResultAndRenderResources> prepareInLoadThread(
        const CesiumAsync::AsyncSystem& asyncSystem,
        TileLoadResult&& tileLoadResult,
        const glm::dmat4& transform,
        const std::any& rendererOptions) override {
        static_cast<void>(rendererOptions);
        void* loadResource = nullptr;
        openusd_cesium_matrix4d abiTransform = FromDMat4(transform);
        EmitMeshPrimitives(_callbacks, tileLoadResult, abiTransform);
        if (_callbacks.prepare_in_load_thread != nullptr) {
            openusd_cesium_tile_load_result view = MakeLoadResultView(tileLoadResult);
            char errorData[512]{};
            openusd_cesium_error_buffer error{errorData, sizeof(errorData), 0};
            loadResource = _callbacks.prepare_in_load_thread(
                _callbacks.user_data,
                &view,
                &abiTransform,
                &error);
        }
        return asyncSystem.createResolvedFuture(Cesium3DTilesSelection::TileLoadResultAndRenderResources{
            std::move(tileLoadResult), loadResource});
    }

    void* prepareInMainThread(Cesium3DTilesSelection::Tile& tile, void* pLoadThreadResult) override {
        static_cast<void>(tile);
        if (_callbacks.prepare_in_main_thread == nullptr) {
            return pLoadThreadResult;
        }
        char errorData[512]{};
        openusd_cesium_error_buffer error{errorData, sizeof(errorData), 0};
        return _callbacks.prepare_in_main_thread(_callbacks.user_data, pLoadThreadResult, &error);
    }

    void free(Cesium3DTilesSelection::Tile& tile, void* pLoadThreadResult, void* pMainThreadResult) noexcept override {
        static_cast<void>(tile);
        if (_callbacks.free_resources != nullptr) {
            _callbacks.free_resources(_callbacks.user_data, pLoadThreadResult, pMainThreadResult);
        }
    }

    void* prepareRasterInLoadThread(CesiumImage::ImageAsset& image, const std::any& rendererOptions) override {
        static_cast<void>(image);
        static_cast<void>(rendererOptions);
        return nullptr;
    }

    void* prepareRasterInMainThread(CesiumRasterOverlays::RasterOverlayTile& rasterTile, void* pLoadThreadResult) override {
        static_cast<void>(rasterTile);
        return pLoadThreadResult;
    }

    void freeRaster(const CesiumRasterOverlays::RasterOverlayTile& rasterTile, void* pLoadThreadResult, void* pMainThreadResult) noexcept override {
        static_cast<void>(rasterTile);
        if (_callbacks.free_resources != nullptr) {
            _callbacks.free_resources(_callbacks.user_data, pLoadThreadResult, pMainThreadResult);
        }
    }

    void attachRasterInMainThread(
        const Cesium3DTilesSelection::Tile& tile,
        int32_t overlayTextureCoordinateID,
        const CesiumRasterOverlays::RasterOverlayTile& rasterTile,
        void* pMainThreadRendererResources,
        const glm::dvec2& translation,
        const glm::dvec2& scale) override {
        static_cast<void>(tile);
        static_cast<void>(rasterTile);
        if (_callbacks.attach_raster_in_main_thread != nullptr) {
            openusd_cesium_vec2d abiTranslation{translation.x, translation.y};
            openusd_cesium_vec2d abiScale{scale.x, scale.y};
            _callbacks.attach_raster_in_main_thread(
                _callbacks.user_data,
                overlayTextureCoordinateID,
                pMainThreadRendererResources,
                &abiTranslation,
                &abiScale);
        }
    }

    void detachRasterInMainThread(
        const Cesium3DTilesSelection::Tile& tile,
        int32_t overlayTextureCoordinateID,
        const CesiumRasterOverlays::RasterOverlayTile& rasterTile,
        void* pMainThreadRendererResources) noexcept override {
        static_cast<void>(tile);
        static_cast<void>(rasterTile);
        if (_callbacks.detach_raster_in_main_thread != nullptr) {
            _callbacks.detach_raster_in_main_thread(
                _callbacks.user_data,
                overlayTextureCoordinateID,
                pMainThreadRendererResources);
        }
    }

private:
    openusd_cesium_renderer_callbacks _callbacks;
};

struct openusd_cesium_tileset {
    std::thread::id mainThread;
    openusd_cesium_message_fn messageCallback = nullptr;
    void* messageUserData = nullptr;
    mutable std::mutex mutex;
    std::vector<Message> messages;
    std::shared_ptr<CallbackAssetAccessor> assetAccessor;
    std::shared_ptr<CallbackPrepareRendererResources> rendererResources;
    std::shared_ptr<CallbackTaskProcessor> taskProcessor;
    std::unique_ptr<Cesium3DTilesSelection::Tileset> tileset;
};

void RecordMessage(
    openusd_cesium_tileset* owner,
    openusd_cesium_message_severity severity,
    std::string message) {
    if (owner == nullptr) {
        return;
    }
    {
        std::lock_guard<std::mutex> lock(owner->mutex);
        owner->messages.push_back(Message{severity, message});
    }
    if (owner->messageCallback != nullptr) {
        owner->messageCallback(owner->messageUserData, severity, message.c_str());
    }
}

Cesium3DTilesSelection::TilesetOptions ToOptions(
    openusd_cesium_tileset* owner,
    const openusd_cesium_tileset_options* abiOptions) {
    Cesium3DTilesSelection::TilesetOptions options;
    if (abiOptions != nullptr) {
        if (HasBytes(abiOptions->struct_size, offsetof(openusd_cesium_tileset_options, maximum_screen_space_error), sizeof(double)) &&
            abiOptions->maximum_screen_space_error > 0.0) {
            options.maximumScreenSpaceError = abiOptions->maximum_screen_space_error;
        }
        if (HasBytes(abiOptions->struct_size, offsetof(openusd_cesium_tileset_options, preload_ancestors), sizeof(int32_t))) {
            options.preloadAncestors = abiOptions->preload_ancestors != 0;
        }
        if (HasBytes(abiOptions->struct_size, offsetof(openusd_cesium_tileset_options, preload_siblings), sizeof(int32_t))) {
            options.preloadSiblings = abiOptions->preload_siblings != 0;
        }
        if (HasBytes(abiOptions->struct_size, offsetof(openusd_cesium_tileset_options, forbid_holes), sizeof(int32_t))) {
            options.forbidHoles = abiOptions->forbid_holes != 0;
        }
        if (HasBytes(abiOptions->struct_size, offsetof(openusd_cesium_tileset_options, message_callback), sizeof(openusd_cesium_message_fn))) {
            owner->messageCallback = abiOptions->message_callback;
            owner->messageUserData = abiOptions->message_user_data;
        }
    }
    options.loadErrorCallback = [owner](const Cesium3DTilesSelection::TilesetLoadFailureDetails& details) {
        std::string message = details.message;
        if (message.empty()) {
            message = "Cesium tileset resource failed to load.";
        }
        RecordMessage(owner, OPENUSD_CESIUM_MESSAGE_ERROR, message);
    };
    return options;
}

bool ValidateCallbacks(
    const openusd_cesium_asset_accessor* assetAccessor,
    const openusd_cesium_renderer_callbacks* rendererCallbacks,
    openusd_cesium_error_buffer* error) noexcept {
    if (assetAccessor == nullptr || rendererCallbacks == nullptr) {
        WriteError(error, "Asset accessor and renderer callback tables are required.");
        return false;
    }
    if (assetAccessor->request == nullptr) {
        WriteError(error, "An asset request callback is required.");
        return false;
    }
    if (rendererCallbacks->free_resources == nullptr) {
        WriteError(error, "A renderer free_resources callback is required.");
        return false;
    }
    return true;
}

template <typename TCallbacks>
TCallbacks CopyCallbackTable(const TCallbacks* callbacks) noexcept {
    TCallbacks copy{};
    if (callbacks != nullptr) {
        const size_t count = std::min<size_t>(callbacks->struct_size, sizeof(copy));
        std::memcpy(&copy, callbacks, count);
    }
    return copy;
}

void FillUpdateResult(
    openusd_cesium_update_result* output,
    const Cesium3DTilesSelection::ViewUpdateResult& result,
    Cesium3DTilesSelection::Tileset& tileset) noexcept {
    if (output == nullptr || output->struct_size == 0) {
        return;
    }
    const uint32_t structSize = output->struct_size;
    std::memset(output, 0, std::min<size_t>(structSize, sizeof(*output)));
    output->struct_size = structSize;
    if (HasBytes(structSize, offsetof(openusd_cesium_update_result, version), sizeof(uint32_t))) {
        output->version = OPENUSD_CESIUM_UPDATE_RESULT_VERSION;
    }
    if (HasBytes(structSize, offsetof(openusd_cesium_update_result, tiles_to_render_count), sizeof(uint32_t))) {
        output->tiles_to_render_count = static_cast<uint32_t>(std::min<size_t>(result.tilesToRenderThisFrame.size(), UINT32_MAX));
    }
    if (HasBytes(structSize, offsetof(openusd_cesium_update_result, worker_thread_tile_load_queue_length), sizeof(int32_t))) {
        output->worker_thread_tile_load_queue_length = result.workerThreadTileLoadQueueLength;
    }
    if (HasBytes(structSize, offsetof(openusd_cesium_update_result, main_thread_tile_load_queue_length), sizeof(int32_t))) {
        output->main_thread_tile_load_queue_length = result.mainThreadTileLoadQueueLength;
    }
    if (HasBytes(structSize, offsetof(openusd_cesium_update_result, tiles_visited), sizeof(uint32_t))) {
        output->tiles_visited = result.tilesVisited;
    }
    if (HasBytes(structSize, offsetof(openusd_cesium_update_result, culled_tiles_visited), sizeof(uint32_t))) {
        output->culled_tiles_visited = result.culledTilesVisited;
    }
    if (HasBytes(structSize, offsetof(openusd_cesium_update_result, tiles_culled), sizeof(uint32_t))) {
        output->tiles_culled = result.tilesCulled;
    }
    if (HasBytes(structSize, offsetof(openusd_cesium_update_result, max_depth_visited), sizeof(uint32_t))) {
        output->max_depth_visited = result.maxDepthVisited;
    }
    if (HasBytes(structSize, offsetof(openusd_cesium_update_result, frame_number), sizeof(int32_t))) {
        output->frame_number = result.frameNumber;
    }
    if (HasBytes(structSize, offsetof(openusd_cesium_update_result, loaded_tile_count), sizeof(int32_t))) {
        output->loaded_tile_count = tileset.getNumberOfTilesLoaded();
    }
    if (HasBytes(structSize, offsetof(openusd_cesium_update_result, load_progress), sizeof(float))) {
        output->load_progress = tileset.computeLoadProgress();
    }
}


extern "C" OPENUSD_CESIUM_API openusd_cesium_status openusd_cesium_tileset_create(
    const char* tileset_url,
    const openusd_cesium_asset_accessor* asset_accessor,
    const openusd_cesium_renderer_callbacks* renderer_callbacks,
    const openusd_cesium_task_processor* task_processor,
    const openusd_cesium_tileset_options* options,
    openusd_cesium_tileset** tileset,
    openusd_cesium_error_buffer* error) {
    return Guard(error, [&]() -> openusd_cesium_status {
        if (tileset == nullptr || tileset_url == nullptr || tileset_url[0] == '\0') {
            WriteError(error, "A tileset URL and output handle are required.");
            return OPENUSD_CESIUM_STATUS_INVALID_ARGUMENT;
        }
        *tileset = nullptr;
        if (!ValidateCallbacks(asset_accessor, renderer_callbacks, error)) {
            return OPENUSD_CESIUM_STATUS_INVALID_ARGUMENT;
        }

        auto owner = std::make_unique<openusd_cesium_tileset>();
        owner->mainThread = std::this_thread::get_id();
        owner->assetAccessor = std::make_shared<CallbackAssetAccessor>(CopyCallbackTable(asset_accessor));
        owner->rendererResources = std::make_shared<CallbackPrepareRendererResources>(
            CopyCallbackTable(renderer_callbacks));
        openusd_cesium_task_processor taskCallbacks{};
        if (task_processor != nullptr) {
            taskCallbacks = CopyCallbackTable(task_processor);
        }
        owner->taskProcessor = std::make_shared<CallbackTaskProcessor>(taskCallbacks);
        Cesium3DTilesContent::registerAllTileContentTypes();
        Cesium3DTilesSelection::TilesetExternals externals{
            owner->assetAccessor,
            owner->rendererResources,
            CesiumAsync::AsyncSystem(owner->taskProcessor)};
        Cesium3DTilesSelection::TilesetOptions nativeOptions = ToOptions(owner.get(), options);
        owner->tileset = std::make_unique<Cesium3DTilesSelection::Tileset>(externals, std::string(tileset_url), nativeOptions);
        *tileset = owner.release();
        return OPENUSD_CESIUM_STATUS_OK;
    });
}

extern "C" OPENUSD_CESIUM_API openusd_cesium_status openusd_cesium_tileset_update_view(
    openusd_cesium_tileset* tileset,
    const openusd_cesium_view_state* view_states,
    size_t view_state_count,
    float delta_time_seconds,
    openusd_cesium_update_result* result,
    openusd_cesium_error_buffer* error) {
    return Guard(error, [&]() -> openusd_cesium_status {
        if (tileset == nullptr || tileset->tileset == nullptr || (view_state_count != 0 && view_states == nullptr)) {
            WriteError(error, "A tileset and view state array are required.");
            return OPENUSD_CESIUM_STATUS_INVALID_ARGUMENT;
        }
        if (tileset->mainThread != std::this_thread::get_id()) {
            WriteError(error, "Tileset update must be called from the tileset main thread.");
            return OPENUSD_CESIUM_STATUS_WRONG_THREAD;
        }
        std::vector<Cesium3DTilesSelection::ViewState> nativeViews;
        nativeViews.reserve(view_state_count);
        for (size_t i = 0; i < view_state_count; ++i) {
            const openusd_cesium_view_state& view = view_states[i];
            if (view.struct_size < sizeof(openusd_cesium_view_state)) {
                WriteError(error, "View state struct_size is too small.");
                return OPENUSD_CESIUM_STATUS_INVALID_ARGUMENT;
            }
            nativeViews.emplace_back(
                ToDVec3(view.position_ecef),
                ToDVec3(view.direction_ecef),
                ToDVec3(view.up_ecef),
                glm::dvec2(view.viewport_width, view.viewport_height),
                view.horizontal_fov_radians,
                view.vertical_fov_radians);
        }
        const Cesium3DTilesSelection::ViewUpdateResult& update = tileset->tileset->updateView(nativeViews, delta_time_seconds);
        tileset->tileset->loadTiles();
        FillUpdateResult(result, update, *tileset->tileset);
        return OPENUSD_CESIUM_STATUS_OK;
    });
}

extern "C" OPENUSD_CESIUM_API openusd_cesium_status openusd_cesium_tileset_get_message_count(
    const openusd_cesium_tileset* tileset,
    size_t* count,
    openusd_cesium_error_buffer* error) {
    return Guard(error, [&]() -> openusd_cesium_status {
        if (tileset == nullptr || count == nullptr) {
            WriteError(error, "A tileset and count output are required.");
            return OPENUSD_CESIUM_STATUS_INVALID_ARGUMENT;
        }
        std::lock_guard<std::mutex> lock(tileset->mutex);
        *count = tileset->messages.size();
        return OPENUSD_CESIUM_STATUS_OK;
    });
}

extern "C" OPENUSD_CESIUM_API openusd_cesium_status openusd_cesium_tileset_get_message(
    const openusd_cesium_tileset* tileset,
    size_t index,
    openusd_cesium_message_severity* severity,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_cesium_error_buffer* error) {
    return Guard(error, [&]() -> openusd_cesium_status {
        if (tileset == nullptr || required == nullptr) {
            WriteError(error, "A tileset and required output are needed.");
            return OPENUSD_CESIUM_STATUS_INVALID_ARGUMENT;
        }
        std::lock_guard<std::mutex> lock(tileset->mutex);
        if (index >= tileset->messages.size()) {
            WriteError(error, "Message index is out of range.");
            return OPENUSD_CESIUM_STATUS_NOT_FOUND;
        }
        if (severity != nullptr) {
            *severity = tileset->messages[index].severity;
        }
        return CopyString(tileset->messages[index].text, buffer, capacity, required);
    });
}

extern "C" OPENUSD_CESIUM_API openusd_cesium_status openusd_cesium_tileset_release(
    openusd_cesium_tileset* tileset,
    openusd_cesium_error_buffer* error) {
    return Guard(error, [&]() -> openusd_cesium_status {
        if (tileset == nullptr) {
            return OPENUSD_CESIUM_STATUS_OK;
        }
        if (tileset->mainThread != std::this_thread::get_id()) {
            WriteError(error, "Tileset release must be called from the tileset main thread.");
            return OPENUSD_CESIUM_STATUS_WRONG_THREAD;
        }
        delete tileset;
        return OPENUSD_CESIUM_STATUS_OK;
    });
}

extern "C" OPENUSD_CESIUM_API openusd_cesium_status openusd_cesium_task_execute(openusd_cesium_task* task) {
    if (task == nullptr) {
        return OPENUSD_CESIUM_STATUS_INVALID_ARGUMENT;
    }
    bool expected = false;
    if (!task->executed.compare_exchange_strong(expected, true)) {
        return OPENUSD_CESIUM_STATUS_INVALID_ARGUMENT;
    }
    task->action();
    return OPENUSD_CESIUM_STATUS_OK;
}

extern "C" OPENUSD_CESIUM_API void openusd_cesium_task_destroy(openusd_cesium_task* task) {
    delete task;
}
