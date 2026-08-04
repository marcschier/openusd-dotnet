// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_hdsilk.h"
#include "openusd_render_camera_internal.h"
#include "openusd_renderer_stage_bridge.h"

#include "renderDelegate.h"
#include "mesh.h"

#include "pxr/base/gf/frustum.h"
#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec2i.h"
#include "pxr/base/gf/vec3d.h"
#include "pxr/base/gf/vec4d.h"
#include "pxr/base/plug/plugin.h"
#include "pxr/base/plug/registry.h"
#include "pxr/base/tf/errorMark.h"
#include "pxr/base/tf/token.h"
#include "pxr/imaging/cameraUtil/conformWindow.h"
#include "pxr/pxr.h"
#include "pxr/usd/usd/prim.h"
#include "pxr/usd/usd/stage.h"
#include "pxr/usdImaging/usdImagingGL/engine.h"
#include "pxr/usdImaging/usdImagingGL/renderParams.h"

#include <algorithm>
#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <exception>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
const TfToken& SilkRendererPluginId()
{
    static const TfToken id("HdSilkRendererPlugin");
    return id;
}
}

struct SilkSessionState
{
    openusd_stage* stage_core = nullptr;
    UsdStageRefPtr stage;
    std::unique_ptr<UsdImagingGLEngine> engine;
    std::shared_ptr<HdSilkSceneState> sceneState;
    mutable std::mutex mutex;
    std::mutex lifetime_mutex;
    std::condition_variable lifetime_changed;
    size_t in_flight = 0;
    bool closing = false;
    std::string name;
};

struct openusd_silk_page
{
    std::vector<uint8_t> data;
    uint64_t revision = 0;
    uint32_t command_count = 0;
};

namespace
{
struct SilkSessionRegistry
{
    std::mutex mutex;
    std::unordered_map<uintptr_t, std::shared_ptr<SilkSessionState>> sessions;
    uintptr_t next_token = 1;
};

std::atomic_size_t g_live_page_count{0};
std::atomic_size_t g_peak_page_count{0};
std::atomic_size_t g_peak_session_count{0};

void UpdatePeak(std::atomic_size_t& peak, size_t value) noexcept
{
    size_t current = peak.load(std::memory_order_relaxed);
    while (current < value &&
           !peak.compare_exchange_weak(
               current,
               value,
               std::memory_order_relaxed,
               std::memory_order_relaxed))
    {
    }
}

SilkSessionRegistry& GetSessionRegistry()
{
    static SilkSessionRegistry* registry = new SilkSessionRegistry();
    return *registry;
}

uintptr_t GetSessionToken(const openusd_silk_session* session) noexcept
{
    return reinterpret_cast<uintptr_t>(session);
}

openusd_silk_session* GetSessionHandle(uintptr_t token) noexcept
{
    return reinterpret_cast<openusd_silk_session*>(token);
}

class SessionOperationGuard final
{
public:
    explicit SessionOperationGuard(std::shared_ptr<SilkSessionState> state)
        : _state(std::move(state))
    {
    }

    ~SessionOperationGuard()
    {
        if (!_state)
        {
            return;
        }
        try
        {
            std::lock_guard<std::mutex> lock(_state->lifetime_mutex);
            --_state->in_flight;
            if (_state->in_flight == 0)
            {
                _state->lifetime_changed.notify_all();
            }
        }
        catch (...)
        {
            std::terminate();
        }
    }

    SessionOperationGuard(const SessionOperationGuard&) = delete;
    SessionOperationGuard& operator=(const SessionOperationGuard&) = delete;

private:
    std::shared_ptr<SilkSessionState> _state;
};

void WriteError(openusd_error_buffer* error, const std::string& message)
{
    if (error == nullptr)
    {
        return;
    }

    error->required = message.size() + 1;
    if (error->data == nullptr || error->capacity == 0)
    {
        return;
    }

    const size_t count = std::min(message.size(), error->capacity - 1);
    std::memcpy(error->data, message.data(), count);
    error->data[count] = '\0';
}

std::shared_ptr<SilkSessionState> AcquireSessionOperation(
    const openusd_silk_session* session,
    openusd_error_buffer* error)
{
    if (session == nullptr)
    {
        WriteError(error, "A valid hdSilk session is required.");
        return nullptr;
    }

    SilkSessionRegistry& registry = GetSessionRegistry();
    std::lock_guard<std::mutex> registry_lock(registry.mutex);
    const auto iterator = registry.sessions.find(GetSessionToken(session));
    if (iterator == registry.sessions.end())
    {
        WriteError(error, "The hdSilk session handle is stale or invalid.");
        return nullptr;
    }

    const std::shared_ptr<SilkSessionState>& state = iterator->second;
    std::lock_guard<std::mutex> lifetime_lock(state->lifetime_mutex);
    if (state->closing)
    {
        WriteError(error, "The hdSilk session is being destroyed.");
        return nullptr;
    }
    ++state->in_flight;
    return state;
}

std::shared_ptr<SilkSessionState> BeginSessionDestroy(
    const openusd_silk_session* session,
    openusd_error_buffer* error)
{
    if (session == nullptr)
    {
        WriteError(error, "A valid hdSilk session is required.");
        return nullptr;
    }

    SilkSessionRegistry& registry = GetSessionRegistry();
    std::lock_guard<std::mutex> registry_lock(registry.mutex);
    const auto iterator = registry.sessions.find(GetSessionToken(session));
    if (iterator == registry.sessions.end())
    {
        WriteError(error, "The hdSilk session handle is stale or invalid.");
        return nullptr;
    }

    const std::shared_ptr<SilkSessionState>& state = iterator->second;
    std::lock_guard<std::mutex> lifetime_lock(state->lifetime_mutex);
    if (state->closing)
    {
        WriteError(error, "The hdSilk session is already being destroyed.");
        return nullptr;
    }
    state->closing = true;
    return state;
}

void CancelSessionDestroy(const std::shared_ptr<SilkSessionState>& state) noexcept
{
    try
    {
        std::lock_guard<std::mutex> lock(state->lifetime_mutex);
        state->closing = false;
    }
    catch (...)
    {
        std::terminate();
    }
}

openusd_status RegisterSession(
    const std::shared_ptr<SilkSessionState>& state,
    openusd_silk_session** session,
    openusd_error_buffer* error)
{
    SilkSessionRegistry& registry = GetSessionRegistry();
    std::lock_guard<std::mutex> lock(registry.mutex);
    if (registry.next_token == 0)
    {
        WriteError(error, "The hdSilk session token space is exhausted.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    const uintptr_t token = registry.next_token++;
    const auto [iterator, inserted] = registry.sessions.emplace(token, state);
    static_cast<void>(iterator);
    if (!inserted)
    {
        WriteError(error, "Could not allocate a unique hdSilk session token.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    UpdatePeak(g_peak_session_count, registry.sessions.size());
    *session = GetSessionHandle(token);
    return OPENUSD_STATUS_OK;
}

void RemoveDestroyedSession(
    const openusd_silk_session* session,
    const std::shared_ptr<SilkSessionState>& state)
{
    SilkSessionRegistry& registry = GetSessionRegistry();
    std::lock_guard<std::mutex> lock(registry.mutex);
    const auto iterator = registry.sessions.find(GetSessionToken(session));
    if (iterator != registry.sessions.end() && iterator->second == state)
    {
        registry.sessions.erase(iterator);
    }
}

std::string ConsumeErrors(TfErrorMark& mark)
{
    std::string message;
    for (const TfError& error : mark)
    {
        if (!message.empty())
        {
            message += '\n';
        }
        message += error.GetCommentary();
    }
    mark.Clear();
    return message;
}

template <typename TAction>
openusd_status Guard(openusd_error_buffer* error, TAction&& action)
{
    try
    {
        return action();
    }
    catch (const std::exception& exception)
    {
        WriteError(error, exception.what());
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    catch (...)
    {
        WriteError(error, "Unknown native exception.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
}

template <typename TAction>
openusd_status WithStageAccess(
    openusd_stage* stage,
    openusd_error_buffer* error,
    TAction&& action)
{
    openusd_stage_access* access = nullptr;
    openusd_status status = openusd_stage_access_begin(stage, &access, error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }

    try
    {
        status = action(access);
    }
    catch (...)
    {
        openusd_error_buffer end_error{nullptr, 0, 0};
        const openusd_status end_status = openusd_stage_access_end(access, &end_error);
        if (end_status != OPENUSD_STATUS_OK)
        {
            std::terminate();
        }
        throw;
    }

    openusd_error_buffer end_error{nullptr, 0, 0};
    const openusd_status end_status = openusd_stage_access_end(access, &end_error);
    return status == OPENUSD_STATUS_OK ? end_status : status;
}

class StageRetainGuard final
{
public:
    StageRetainGuard(openusd_stage* stage, openusd_error_buffer* error)
        : _stage(stage)
        , _status(openusd_stage_retain(stage, error))
        , _retained(_status == OPENUSD_STATUS_OK)
    {
    }

    ~StageRetainGuard()
    {
        if (_retained)
        {
            openusd_stage_release(_stage);
        }
    }

    openusd_status Status() const noexcept
    {
        return _status;
    }

    void Dismiss() noexcept
    {
        _retained = false;
    }

private:
    openusd_stage* _stage;
    openusd_status _status;
    bool _retained;
};

class StageReleaseGuard final
{
public:
    explicit StageReleaseGuard(openusd_stage* stage) : _stage(stage)
    {
    }

    ~StageReleaseGuard()
    {
        if (_stage != nullptr)
        {
            openusd_stage_release(_stage);
        }
    }

private:
    openusd_stage* _stage;
};

class SceneStateCaptureGuard final
{
public:
    SceneStateCaptureGuard()
        : _token(HdSilkRenderDelegate::BeginSceneStateCapture())
    {
    }

    ~SceneStateCaptureGuard()
    {
        if (_active)
        {
            HdSilkRenderDelegate::CancelSceneStateCapture(_token);
        }
    }

    std::shared_ptr<HdSilkSceneState> Take()
    {
        std::shared_ptr<HdSilkSceneState> result =
            HdSilkRenderDelegate::EndSceneStateCapture(_token);
        _active = false;
        return result;
    }

private:
    uint64_t _token;
    bool _active = true;
};

bool IsRendererCreateFailpoint(const char* name) noexcept
{
#if !defined(OPENUSD_RENDERER_ENABLE_TEST_HOOKS)
    static_cast<void>(name);
    return false;
#elif defined(_WIN32)
    char* value = nullptr;
    size_t value_size = 0;
    if (_dupenv_s(
            &value,
            &value_size,
            "OPENUSD_RENDERER_CREATE_FAILPOINT") != 0)
    {
        return false;
    }
    const bool matches = value != nullptr && std::strcmp(value, name) == 0;
    std::free(value);
    return matches;
#else
    const char* value = std::getenv("OPENUSD_RENDERER_CREATE_FAILPOINT");
    return value != nullptr && std::strcmp(value, name) == 0;
#endif
}

openusd_status CopyString(
    const std::string& value,
    char* buffer,
    size_t capacity,
    size_t* required)
{
    if (required == nullptr)
    {
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    *required = value.size() + 1;
    if (buffer == nullptr || capacity < *required)
    {
        return OPENUSD_STATUS_BUFFER_TOO_SMALL;
    }

    std::memcpy(buffer, value.c_str(), *required);
    return OPENUSD_STATUS_OK;
}

struct SilkInitializationContext
{
    const char* plugin_path;
    SilkSessionState* session;
};

openusd_status InitializeSilkSession(
    const UsdStageRefPtr* stage_view,
    void* renderer_context,
    openusd_error_buffer* error)
{
    if (stage_view == nullptr || !*stage_view || renderer_context == nullptr)
    {
        WriteError(error, "A valid stage view and hdSilk initialization context are required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    auto* context = static_cast<SilkInitializationContext*>(renderer_context);
    PlugRegistry& registry = PlugRegistry::GetInstance();
    static std::mutex plugin_mutex;
    static bool plugin_loaded = false;
    {
        std::lock_guard<std::mutex> plugin_lock(plugin_mutex);
        if (!plugin_loaded)
        {
            TfErrorMark plugin_mark;
            registry.RegisterPlugins(context->plugin_path);
            const PlugPluginPtr silkPlugin = registry.GetPluginWithName("hdSilk");
            if (!silkPlugin)
            {
                WriteError(error, "The hdSilk plugin metadata was not registered.");
                return OPENUSD_STATUS_NOT_FOUND;
            }
            if (!silkPlugin->Load())
            {
                WriteError(error, "The hdSilk renderer plugin could not be loaded.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            if (!plugin_mark.IsClean())
            {
                WriteError(error, ConsumeErrors(plugin_mark));
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            plugin_loaded = true;
        }
    }

    TfErrorMark mark;
    const TfTokenVector rendererPlugins = UsdImagingGLEngine::GetRendererPlugins();
    if (std::find(rendererPlugins.begin(), rendererPlugins.end(), SilkRendererPluginId()) ==
        rendererPlugins.end())
    {
        WriteError(error, "The HdSilkRendererPlugin was not registered with Hydra.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    SceneStateCaptureGuard capture;
    UsdImagingGLEngine::Parameters parameters;
    parameters.rendererPluginId = SilkRendererPluginId();
    parameters.gpuEnabled = false;
    auto engine = std::make_unique<UsdImagingGLEngine>(parameters);
    engine->SetEnablePresentation(false);
    if (engine->GetCurrentRendererId() != SilkRendererPluginId())
    {
        WriteError(error, "Hydra did not select the HdSilkRendererPlugin.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    std::shared_ptr<HdSilkSceneState> scene_state =
        capture.Take();
    if (!scene_state)
    {
        WriteError(error, "Could not capture the hdSilk render delegate scene state.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    if (!mark.IsClean())
    {
        WriteError(error, ConsumeErrors(mark));
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    context->session->stage = *stage_view;
    context->session->engine = std::move(engine);
    context->session->sceneState = std::move(scene_state);
    context->session->name =
        UsdImagingGLEngine::GetRendererDisplayName(SilkRendererPluginId());
    return OPENUSD_STATUS_OK;
}
}

openusd_status openusd_silk_session_create(
    const char* plugin_path,
    const char* stage_path,
    openusd_silk_session** session,
    openusd_error_buffer* error)
{
    if (session != nullptr)
    {
        *session = nullptr;
    }
    if (plugin_path == nullptr || stage_path == nullptr || session == nullptr)
    {
        WriteError(error, "Plugin path, stage path, and session output are required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    return Guard(error, [&]()
    {
        openusd_stage* stage = nullptr;
        const openusd_status open_status = openusd_stage_open(stage_path, &stage, error);
        if (open_status != OPENUSD_STATUS_OK)
        {
            return open_status;
        }
        StageReleaseGuard stage_guard(stage);
        return openusd_silk_session_create_from_stage(plugin_path, stage, session, error);
    });
}

openusd_status openusd_silk_session_create_from_stage(
    const char* plugin_path,
    openusd_stage* stage,
    openusd_silk_session** session,
    openusd_error_buffer* error)
{
    if (session != nullptr)
    {
        *session = nullptr;
    }
    if (plugin_path == nullptr || stage == nullptr || session == nullptr)
    {
        WriteError(error, "Plugin path, stage handle, and session output are required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    return Guard(error, [&]()
    {
        StageRetainGuard stage_guard(stage, error);
        if (stage_guard.Status() != OPENUSD_STATUS_OK)
        {
            return stage_guard.Status();
        }
        if (IsRendererCreateFailpoint("after-retain"))
        {
            throw std::bad_alloc();
        }

        auto result = std::make_shared<SilkSessionState>();
        openusd_silk_session* handle = nullptr;
        SilkInitializationContext context{plugin_path, result.get()};
        const openusd_status status = WithStageAccess(stage, error, [&](openusd_stage_access* access)
        {
            const openusd_status initialize_status = openusd_renderer_stage_initialize(
                access,
                InitializeSilkSession,
                &context,
                error);
            if (initialize_status != OPENUSD_STATUS_OK)
            {
                return initialize_status;
            }

            result->stage_core = stage;
            try
            {
                const openusd_status register_status =
                    RegisterSession(result, &handle, error);
                if (register_status == OPENUSD_STATUS_OK)
                {
                    return OPENUSD_STATUS_OK;
                }
                result->engine.reset();
                result->sceneState.reset();
                result->stage.Reset();
                result->stage_core = nullptr;
                return register_status;
            }
            catch (...)
            {
                result->engine.reset();
                result->sceneState.reset();
                result->stage.Reset();
                result->stage_core = nullptr;
                throw;
            }
        });
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }

        stage_guard.Dismiss();
        *session = handle;
        return OPENUSD_STATUS_OK;
    });
}

void openusd_silk_session_release(openusd_silk_session* session) noexcept
{
    try
    {
        openusd_error_buffer error{nullptr, 0, 0};
        static_cast<void>(openusd_silk_session_destroy(session, &error));
    }
    catch (...)
    {
    }
}

openusd_status openusd_silk_session_destroy(
    openusd_silk_session* session,
    openusd_error_buffer* error)
{
    return Guard(error, [&]()
    {
        const std::shared_ptr<SilkSessionState> state =
            BeginSessionDestroy(session, error);
        if (!state)
        {
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        {
            std::unique_lock<std::mutex> lifetime_lock(state->lifetime_mutex);
            state->lifetime_changed.wait(
                lifetime_lock,
                [&state] { return state->in_flight == 0; });
        }

        std::lock_guard<std::mutex> lock(state->mutex);
        if (state->stage_core == nullptr || !state->stage ||
            !state->engine || !state->sceneState)
        {
            CancelSessionDestroy(state);
            WriteError(error, "A valid hdSilk session is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }

        const openusd_status access_status = WithStageAccess(
            state->stage_core,
            error,
            [&](openusd_stage_access*)
            {
                state->engine.reset();
                state->sceneState.reset();
                state->stage.Reset();
                return OPENUSD_STATUS_OK;
            });
        if (access_status != OPENUSD_STATUS_OK)
        {
            CancelSessionDestroy(state);
            return access_status;
        }

        openusd_stage* stage = state->stage_core;
        state->stage_core = nullptr;
        RemoveDestroyedSession(session, state);
        openusd_stage_release(stage);
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_silk_session_sync(
    openusd_silk_session* session,
    int32_t width,
    int32_t height,
    double time_code,
    const openusd_render_camera* camera,
    openusd_silk_page** page,
    openusd_silk_page_view* view,
    openusd_error_buffer* error)
{
    return openusd_silk_session_sync_with_complexity(
        session,
        width,
        height,
        time_code,
        camera,
        OPENUSD_SILK_COMPLEXITY_LOW,
        page,
        view,
        error);
}

openusd_status openusd_silk_session_sync_with_complexity(
    openusd_silk_session* session,
    int32_t width,
    int32_t height,
    double time_code,
    const openusd_render_camera* camera,
    uint32_t complexity,
    openusd_silk_page** page,
    openusd_silk_page_view* view,
    openusd_error_buffer* error)
{
    if (page != nullptr)
    {
        *page = nullptr;
    }
    if (page == nullptr || view == nullptr)
    {
        WriteError(error, "A valid session, page, and view outputs are required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    std::string camera_error;
    if (!openusd_render_camera_detail::Validate(camera, camera_error))
    {
        WriteError(error, camera_error);
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (complexity > OPENUSD_SILK_COMPLEXITY_VERY_HIGH)
    {
        WriteError(error, "A valid hdSilk complexity level is required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return Guard(error, [&]()
    {
        const std::shared_ptr<SilkSessionState> state =
            AcquireSessionOperation(session, error);
        if (!state)
        {
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        SessionOperationGuard operation(state);
        std::lock_guard<std::mutex> lock(state->mutex);
        if (state->stage_core == nullptr || !state->stage ||
            !state->engine || !state->sceneState ||
            width <= 0 || height <= 0 ||
            view->struct_size < sizeof(openusd_silk_page_view))
        {
            WriteError(
                error,
                "A valid session, positive viewport size, and page/view outputs are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return WithStageAccess(state->stage_core, error, [&](openusd_stage_access*)
        {
            TfErrorMark mark;

            GfMatrix4d viewMatrix(1.0);
            GfMatrix4d projectionMatrix(1.0);
            if (camera->mode == OPENUSD_RENDER_CAMERA_MODE_AUTO)
            {
                viewMatrix.SetLookAt(
                    GfVec3d(4.0, 3.0, 4.0),
                    GfVec3d(0.0, 0.0, 0.0),
                    GfVec3d(0.0, 1.0, 0.0));
                GfFrustum frustum;
                frustum.SetPerspective(
                    45.0,
                    static_cast<double>(width) / static_cast<double>(height),
                    0.1,
                    1000.0);
                projectionMatrix = frustum.ComputeProjectionMatrix();
            }
            else
            {
                openusd_render_camera_detail::AssignRowMajor(
                    camera->view,
                    viewMatrix);
                openusd_render_camera_detail::AssignRowMajor(
                    camera->projection,
                    projectionMatrix);
            }

            state->engine->SetCameraState(
                viewMatrix, projectionMatrix);
            state->engine->SetRenderBufferSize(GfVec2i(width, height));

            // Storm reaches Hydra through UsdImagingGLEngine, whose free camera
            // conforms the projection to the render buffer aspect with
            // CameraUtilFit. Publishing the caller's raw matrix here instead made
            // hdSilk render the same stage at a different scale from Storm on any
            // non-square viewport: the parity harness measured hdSilk covering
            // 1.24-1.27x Storm on every scene, exactly the 160x128 aspect, and a
            // square viewport compared byte-identical. Conform the published
            // matrix the same way so both renderers agree.
            const GfMatrix4d conformedProjection = CameraUtilConformedWindow(
                projectionMatrix,
                CameraUtilFit,
                static_cast<double>(width) / static_cast<double>(height));

            UsdImagingGLRenderParams parameters;
            parameters.frame = UsdTimeCode(time_code);
            parameters.showRender = true;
            parameters.clipPlanes.reserve(camera->clip_plane_count);
            for (uint32_t plane = 0; plane < camera->clip_plane_count; ++plane)
            {
                parameters.clipPlanes.emplace_back(
                    camera->clip_planes[plane][0],
                    camera->clip_planes[plane][1],
                    camera->clip_planes[plane][2],
                    camera->clip_planes[plane][3]);
            }
            state->sceneState->SetComplexity(complexity);
            HdSilkBeginUsdSkelEvaluation(state->stage, UsdTimeCode(time_code));
            try
            {
                state->engine->Render(state->stage->GetPseudoRoot(), parameters);
            }
            catch (...)
            {
                HdSilkEndUsdSkelEvaluation();
                throw;
            }
            HdSilkEndUsdSkelEvaluation();
            if (!mark.IsClean())
            {
                WriteError(error, ConsumeErrors(mark));
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            HdSilkFrameState frame;
            frame.width = width;
            frame.height = height;
            HdSilkFlattenMatrix(conformedProjection, frame.projectionMatrix);
            if (camera->mode == OPENUSD_RENDER_CAMERA_MODE_MATRICES)
            {
                std::memcpy(
                    frame.viewMatrix,
                    camera->view,
                    sizeof(frame.viewMatrix));
            }
            else
            {
                HdSilkFlattenMatrix(viewMatrix, frame.viewMatrix);
            }
            frame.clipPlaneCount = camera->clip_plane_count;
            std::memcpy(
                frame.clipPlanes,
                camera->clip_planes,
                sizeof(frame.clipPlanes));
            state->sceneState->SetFrame(frame);

            auto result = std::make_unique<openusd_silk_page>();
            result->data =
                state->sceneState->BuildPage(&result->revision, &result->command_count);

            view->struct_size = sizeof(openusd_silk_page_view);
            view->abi_version = OPENUSD_SILK_PAGE_ABI_VERSION;
            view->revision = result->revision;
            view->data = result->data.empty() ? nullptr : result->data.data();
            view->data_size = result->data.size();
            view->command_count = result->command_count;

            const size_t live =
                g_live_page_count.fetch_add(1, std::memory_order_relaxed) + 1;
            UpdatePeak(g_peak_page_count, live);
            *page = result.release();
            return OPENUSD_STATUS_OK;
        });
    });
}

void openusd_silk_page_release(openusd_silk_page* page) noexcept
{
    if (page != nullptr)
    {
        g_live_page_count.fetch_sub(1, std::memory_order_relaxed);
    }
    delete page;
}

openusd_status openusd_silk_session_get_renderer_name(
    const openusd_silk_session* session,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    if (required != nullptr)
    {
        *required = 0;
    }
    if (buffer != nullptr && capacity > 0)
    {
        buffer[0] = '\0';
    }
    return Guard(error, [&]()
    {
        const std::shared_ptr<SilkSessionState> state =
            AcquireSessionOperation(session, error);
        if (!state)
        {
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        SessionOperationGuard operation(state);
        std::lock_guard<std::mutex> lock(state->mutex);
        if (state->stage_core == nullptr)
        {
            WriteError(error, "A valid session is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return CopyString(state->name, buffer, capacity, required);
    });
}

extern "C" OPENUSD_HDSILK_API size_t
openusd_silk_diagnostic_get_live_session_count(void) noexcept
{
    try
    {
        SilkSessionRegistry& registry = GetSessionRegistry();
        std::lock_guard<std::mutex> lock(registry.mutex);
        return registry.sessions.size();
    }
    catch (...)
    {
        return SIZE_MAX;
    }
}

extern "C" OPENUSD_HDSILK_API size_t
openusd_silk_diagnostic_get_peak_session_count(void) noexcept
{
    return g_peak_session_count.load(std::memory_order_relaxed);
}

extern "C" OPENUSD_HDSILK_API size_t
openusd_silk_diagnostic_get_live_page_count(void) noexcept
{
    return g_live_page_count.load(std::memory_order_relaxed);
}

extern "C" OPENUSD_HDSILK_API size_t
openusd_silk_diagnostic_get_peak_page_count(void) noexcept
{
    return g_peak_page_count.load(std::memory_order_relaxed);
}

extern "C" OPENUSD_HDSILK_API void
openusd_silk_diagnostic_reset_peak_counts(void) noexcept
{
    const size_t sessions = openusd_silk_diagnostic_get_live_session_count();
    g_peak_session_count.store(
        sessions == SIZE_MAX ? 0 : sessions,
        std::memory_order_relaxed);
    g_peak_page_count.store(
        g_live_page_count.load(std::memory_order_relaxed),
        std::memory_order_relaxed);
}

#if defined(OPENUSD_HDSILK_ENABLE_TEST_HOOKS)
extern "C" OPENUSD_HDSILK_API size_t
openusd_hdsilk_test_get_session_in_flight(openusd_silk_session* session)
{
    try
    {
        SilkSessionRegistry& registry = GetSessionRegistry();
        std::lock_guard<std::mutex> registry_lock(registry.mutex);
        const auto iterator = registry.sessions.find(GetSessionToken(session));
        if (iterator == registry.sessions.end())
        {
            return SIZE_MAX;
        }
        std::lock_guard<std::mutex> lifetime_lock(
            iterator->second->lifetime_mutex);
        return iterator->second->in_flight;
    }
    catch (...)
    {
        return SIZE_MAX;
    }
}
#endif
