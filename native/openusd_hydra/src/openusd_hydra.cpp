// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_hydra.h"
#include "context_support.h"
#include "openusd_physics_override_scene_index.h"
#include "openusd_render_camera_internal.h"
#include "openusd_render_pick_internal.h"
#include "openusd_renderer_stage_bridge.h"

#include "pxr/base/arch/defines.h"
#include "pxr/base/gf/frustum.h"
#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec2i.h"
#include "pxr/base/gf/vec3d.h"
#include "pxr/base/gf/vec3f.h"
#include "pxr/base/gf/vec4d.h"
#include "pxr/base/gf/vec4f.h"
#include "pxr/base/plug/plugin.h"
#include "pxr/base/plug/registry.h"
#include "pxr/base/tf/errorMark.h"
#include "pxr/base/vt/value.h"
#include "pxr/imaging/garch/glApi.h"
#include "pxr/imaging/glf/contextCaps.h"
#include "pxr/imaging/glf/glContext.h"
#include "pxr/imaging/glf/simpleLight.h"
#include "pxr/imaging/glf/simpleMaterial.h"
#include "pxr/imaging/hgi/tokens.h"
#include "pxr/imaging/hdx/pickTask.h"
#include "pxr/pxr.h"
#include "pxr/usd/sdf/path.h"
#include "pxr/usd/usd/prim.h"
#include "pxr/usd/usd/primRange.h"
#include "pxr/usd/usd/stage.h"
#include "pxr/usd/usdLux/lightAPI.h"
#include "pxr/usdImaging/usdImagingGL/engine.h"
#include "pxr/usdImaging/usdImagingGL/renderParams.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cstdint>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <exception>
#include <memory>
#include <string>
#include <thread>
#include <utility>
#include <vector>

#if defined(_WIN32)
#include <Windows.h>
#include <GL/gl.h>
#elif defined(ARCH_OS_DARWIN)
#include <OpenGL/OpenGL.h>
#endif
#if defined(ARCH_OS_LINUX)
#include <GL/glx.h>
#endif

PXR_NAMESPACE_USING_DIRECTIVE

struct openusd_storm_renderer
{
    openusd_stage* stage_core = nullptr;
    UsdStageRefPtr stage;
    std::unique_ptr<UsdImagingGLEngine> engine;
    OpenUsdPhysicsOverrideSceneIndexRefPtr physics_overrides;
    std::thread::id owner;
    uintptr_t context_identity = 0;
    std::string name;
    openusd_render_camera applied_camera =
        openusd_render_camera_detail::Automatic();
    openusd_render_camera requested_camera =
        openusd_render_camera_detail::Automatic();
    int32_t rendered_width = 0;
    int32_t rendered_height = 0;
    double rendered_time_code = 0;
    uint64_t rendered_state_revision = 0;
    uint64_t rendered_scene_revision = 0;
    uint32_t rendered_revision_flags = 0;
    size_t last_render_clip_plane_count = 0;
    size_t last_pick_clip_plane_count = 0;
    bool has_rendered_state = false;
};

namespace
{
std::atomic_size_t g_abandoned_storm_engine_count{0};
std::atomic_size_t g_live_storm_renderer_count{0};
std::atomic_size_t g_peak_storm_renderer_count{0};

constexpr openusd_render_headlight kStormHeadlight{
    sizeof(openusd_render_headlight),
    OPENUSD_RENDER_HEADLIGHT_VERSION,
    {0.0f, 0.0f, 1.0f},
    1.0f,
    {1.0f, 1.0f, 1.0f},
    0.0f};

GlfSimpleLightVector MakeStormHeadlightLights()
{
    GlfSimpleLight light;
    light.SetPosition(GfVec4f(
        kStormHeadlight.direction[0],
        kStormHeadlight.direction[1],
        kStormHeadlight.direction[2],
        0.0f));
    const GfVec4f radiance(
        kStormHeadlight.color[0] * kStormHeadlight.intensity,
        kStormHeadlight.color[1] * kStormHeadlight.intensity,
        kStormHeadlight.color[2] * kStormHeadlight.intensity,
        1.0f);
    light.SetDiffuse(radiance);
    light.SetSpecular(radiance);
    light.SetAmbient(GfVec4f(0.0f, 0.0f, 0.0f, 1.0f));
    light.SetIsCameraSpaceLight(true);
    light.SetHasIntensity(true);
    light.SetHasShadow(false);
    light.SetAttenuation(GfVec3f(1.0f, 0.0f, 0.0f));
    return GlfSimpleLightVector{light};
}

float ReadFloatAttribute(const UsdPrim& prim, const char* name, double time_code, float fallback)
{
    UsdAttribute attribute = prim.GetAttribute(TfToken(name));
    if (!attribute)
    {
        return fallback;
    }
    float value = fallback;
    if (attribute.Get(&value, UsdTimeCode(time_code)))
    {
        return value;
    }
    double double_value = static_cast<double>(fallback);
    return attribute.Get(&double_value, UsdTimeCode(time_code))
        ? static_cast<float>(double_value)
        : fallback;
}

GfVec3f ReadVec3fAttribute(
    const UsdPrim& prim,
    const char* name,
    double time_code,
    const GfVec3f& fallback)
{
    UsdAttribute attribute = prim.GetAttribute(TfToken(name));
    GfVec3f value = fallback;
    return attribute && attribute.Get(&value, UsdTimeCode(time_code)) ? value : fallback;
}

GfMatrix4d ReadTransformAttribute(const UsdPrim& prim, double time_code)
{
    GfMatrix4d transform(1.0);
    UsdAttribute attribute = prim.GetAttribute(TfToken("xformOp:transform"));
    if (attribute)
    {
        attribute.Get(&transform, UsdTimeCode(time_code));
    }
    return transform;
}

GfVec4f MakeRadiance(const UsdPrim& prim, double time_code)
{
    const GfVec3f color = ReadVec3fAttribute(
        prim,
        "inputs:color",
        time_code,
        GfVec3f(1.0f, 1.0f, 1.0f));
    const float intensity = ReadFloatAttribute(prim, "inputs:intensity", time_code, 1.0f);
    const float exposure = ReadFloatAttribute(prim, "inputs:exposure", time_code, 0.0f);
    const float scale = intensity * std::pow(2.0f, exposure);
    return GfVec4f(color[0] * scale, color[1] * scale, color[2] * scale, 1.0f);
}

struct StormSceneLighting
{
    GlfSimpleLightVector lights;
    GfVec4f ambient{0.0f, 0.0f, 0.0f, 1.0f};
};

StormSceneLighting MakeStormSceneLighting(const UsdStageRefPtr& stage, double time_code)
{
    StormSceneLighting lighting;
    if (!stage)
    {
        return lighting;
    }
    for (const UsdPrim& prim : stage->Traverse())
    {
        if (!prim)
        {
            continue;
        }
        const TfToken typeName = prim.GetTypeName();
        if (typeName != TfToken("DistantLight") &&
            typeName != TfToken("SphereLight") &&
            typeName != TfToken("DomeLight"))
        {
            continue;
        }

        GlfSimpleLight light;
        const GfVec4f radiance = MakeRadiance(prim, time_code);
        light.SetID(prim.GetPath());
        light.SetDiffuse(radiance);
        light.SetSpecular(radiance);
        light.SetAmbient(GfVec4f(0.0f, 0.0f, 0.0f, 1.0f));
        light.SetHasIntensity(true);
        light.SetHasShadow(false);
        if (typeName == TfToken("DistantLight"))
        {
            const GfVec3d direction =
                ReadTransformAttribute(prim, time_code).TransformDir(GfVec3d(0.0, 0.0, 1.0));
            light.SetPosition(GfVec4f(
                static_cast<float>(direction[0]),
                static_cast<float>(direction[1]),
                static_cast<float>(direction[2]),
                0.0f));
            light.SetAttenuation(GfVec3f(1.0f, 0.0f, 0.0f));
        }
        else if (typeName == TfToken("SphereLight"))
        {
            const GfVec3d position = ReadTransformAttribute(prim, time_code).ExtractTranslation();
            light.SetPosition(GfVec4f(
                static_cast<float>(position[0]),
                static_cast<float>(position[1]),
                static_cast<float>(position[2]),
                1.0f));
            light.SetAttenuation(GfVec3f(0.0f, 0.0f, 1.0f));
        }
        else
        {
            lighting.ambient = GfVec4f(
                lighting.ambient[0] + radiance[0],
                lighting.ambient[1] + radiance[1],
                lighting.ambient[2] + radiance[2],
                1.0f);
            continue;
        }
        lighting.lights.push_back(light);
    }
    return lighting;
}

GlfSimpleMaterial MakeStormFallbackMaterial()
{
    GlfSimpleMaterial material;
    material.SetAmbient(GfVec4f(0.2f, 0.2f, 0.2f, 1.0f));
    material.SetDiffuse(GfVec4f(0.8f, 0.8f, 0.8f, 1.0f));
    material.SetSpecular(GfVec4f(0.5f, 0.5f, 0.5f, 1.0f));
    material.SetEmission(GfVec4f(0.0f, 0.0f, 0.0f, 1.0f));
    material.SetShininess(32.0);
    return material;
}

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
    {
    }

    ~StageRetainGuard()
    {
        if (_retained)
        {
            openusd_stage_release(_stage);
        }
    }

    openusd_status RetainStatus() const noexcept
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
    bool _retained = _status == OPENUSD_STATUS_OK;
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

uintptr_t CurrentGlContextIdentity() noexcept
{
#if defined(_WIN32)
    return reinterpret_cast<uintptr_t>(wglGetCurrentContext());
#elif defined(ARCH_OS_LINUX)
    return reinterpret_cast<uintptr_t>(glXGetCurrentContext());
#elif defined(ARCH_OS_DARWIN)
    return reinterpret_cast<uintptr_t>(CGLGetCurrentContext());
#else
    return 0;
#endif
}

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

void ResolveCamera(
    const openusd_render_camera& camera,
    int32_t width,
    int32_t height,
    GfMatrix4d& view,
    GfMatrix4d& projection)
{
    if (camera.mode == OPENUSD_RENDER_CAMERA_MODE_AUTO)
    {
        view.SetIdentity();
        view.SetLookAt(
            GfVec3d(4.0, 3.0, 4.0),
            GfVec3d(0.0, 0.0, 0.0),
            GfVec3d(0.0, 1.0, 0.0));
        GfFrustum frustum;
        frustum.SetPerspective(
            45.0,
            static_cast<double>(width) / static_cast<double>(height),
            0.1,
            1000.0);
        projection = frustum.ComputeProjectionMatrix();
        return;
    }

    openusd_render_camera_detail::AssignRowMajor(camera.view, view);
    openusd_render_camera_detail::AssignRowMajor(camera.projection, projection);
}

openusd_render_camera AppliedCamera(
    const GfMatrix4d& view,
    const GfMatrix4d& projection,
    const openusd_render_camera& requested) noexcept
{
    openusd_render_camera camera{};
    camera.struct_size = sizeof(camera);
    camera.mode = OPENUSD_RENDER_CAMERA_MODE_MATRICES;
    std::memcpy(camera.view, view.GetArray(), sizeof(camera.view));
    std::memcpy(
        camera.projection,
        projection.GetArray(),
        sizeof(camera.projection));
    camera.clip_plane_count = requested.clip_plane_count;
    std::memcpy(
        camera.clip_planes,
        requested.clip_planes,
        sizeof(camera.clip_planes));
    return camera;
}

void ApplyClipPlanes(
    const openusd_render_camera& camera,
    UsdImagingGLRenderParams& parameters)
{
    parameters.clipPlanes.clear();
    parameters.clipPlanes.reserve(camera.clip_plane_count);
    for (uint32_t plane = 0; plane < camera.clip_plane_count; ++plane)
    {
        parameters.clipPlanes.emplace_back(
            camera.clip_planes[plane][0],
            camera.clip_planes[plane][1],
            camera.clip_planes[plane][2],
            camera.clip_planes[plane][3]);
    }
}

openusd_status ValidateStormThread(
    const openusd_storm_renderer* renderer,
    openusd_error_buffer* error)
{
    if (renderer == nullptr || renderer->stage_core == nullptr)
    {
        WriteError(error, "A valid Storm renderer is required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (renderer->owner != std::this_thread::get_id())
    {
        WriteError(error, "Storm access must run on the renderer's creation thread.");
        return OPENUSD_STATUS_WRONG_THREAD;
    }
    return OPENUSD_STATUS_OK;
}

openusd_status ValidateStormOwner(
    const openusd_storm_renderer* renderer,
    openusd_error_buffer* error)
{
    const openusd_status thread_status = ValidateStormThread(renderer, error);
    if (thread_status != OPENUSD_STATUS_OK)
    {
        return thread_status;
    }

    const uintptr_t current = CurrentGlContextIdentity();
    if (current == 0)
    {
        WriteError(error, "The renderer's OpenGL context is not current.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (current != renderer->context_identity)
    {
        WriteError(error, "A different OpenGL context is current.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
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

struct StormInitializationContext
{
    const char* plugin_path;
    openusd_storm_renderer* renderer;
};

openusd_status InitializeStormRenderer(
    const UsdStageRefPtr* stage_view,
    void* renderer_context,
    openusd_error_buffer* error)
{
    if (stage_view == nullptr || !*stage_view || renderer_context == nullptr)
    {
        WriteError(error, "A valid stage view and Storm initialization context are required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    auto* context = static_cast<StormInitializationContext*>(renderer_context);
    TfErrorMark mark;
    PlugRegistry& registry = PlugRegistry::GetInstance();
    registry.RegisterPlugins(context->plugin_path);
    const PlugPluginPtr stormPlugin = registry.GetPluginWithName("hdStorm");
    if (!stormPlugin)
    {
        WriteError(error, "The hdStorm plugin metadata was not registered.");
        return OPENUSD_STATUS_NOT_FOUND;
    }
    if (!stormPlugin->Load())
    {
        WriteError(error, "The hdStorm renderer plugin could not be loaded.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    if (!GarchGLApiLoad())
    {
        WriteError(error, "OpenUSD could not load the current OpenGL API.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
#if defined(ARCH_OS_LINUX)
    const OpenUsdStormLinuxContextKind contextKind =
        DiagnoseOpenUsdStormLinuxContext(
            glGetString(GL_VERSION) != nullptr,
            glXGetCurrentContext() != nullptr);
    if (contextKind == OpenUsdStormLinuxContextKind::NonGlx)
    {
        WriteError(
            error,
            "OpenUSD v26.05 Glf/Garch supports GLX contexts only on Linux; "
            "the current OpenGL context is non-GLX (typically Wayland EGL).");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
#endif
    const GlfGLContextSharedPtr currentContext =
        GlfGLContext::GetCurrentGLContext();
    if (!currentContext || !currentContext->IsValid())
    {
        WriteError(error, "OpenUSD Glf did not recognize a valid current OpenGL context.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    GlfContextCaps::InitInstance();
    const uintptr_t contextIdentity = CurrentGlContextIdentity();
    if (contextIdentity == 0)
    {
        WriteError(error, "No platform OpenGL context is current.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    const TfTokenVector rendererPlugins = UsdImagingGLEngine::GetRendererPlugins();
    const TfToken stormRendererId("HdStormRendererPlugin");
    if (std::find(rendererPlugins.begin(), rendererPlugins.end(), stormRendererId) ==
        rendererPlugins.end())
    {
        WriteError(error, "The HdStormRendererPlugin was not registered with Hydra.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    UsdImagingGLEngine::Parameters parameters;
    parameters.rendererPluginId = stormRendererId;
    OpenUsdPhysicsOverrideSceneIndexRefPtr physicsOverrides;
    std::unique_ptr<UsdImagingGLEngine> engine;
    {
        // The override scene index is installed into exactly the Hydra graph
        // this engine builds, in both scene-index and emulated legacy modes.
        const OpenUsdPhysicsOverrideSceneIndexRegistrar::Capture capture;
        engine = std::make_unique<UsdImagingGLEngine>(parameters);
        physicsOverrides = capture.Take();
    }
    engine->SetEnablePresentation(true);
    if (!engine->GetGPUEnabled())
    {
        WriteError(error, "Hydra did not enable a GPU renderer.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    if (!mark.IsClean())
    {
        WriteError(error, ConsumeErrors(mark));
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    context->renderer->stage = *stage_view;
    const TfToken rendererId = engine->GetCurrentRendererId();
    std::string name = UsdImagingGLEngine::GetRendererDisplayName(rendererId);
    const std::string hgi = engine->GetRendererHgiDisplayName();
    if (!hgi.empty())
    {
        name += " / " + hgi;
    }
    context->renderer->engine = std::move(engine);
    context->renderer->physics_overrides = physicsOverrides;
    context->renderer->owner = std::this_thread::get_id();
    context->renderer->context_identity = contextIdentity;
    context->renderer->name = std::move(name);
    return OPENUSD_STATUS_OK;
}

bool ValidOutputBuffer(const void* buffer, uint32_t capacity) noexcept
{
    return buffer != nullptr || capacity == 0;
}

bool ExactDouble(double left, double right) noexcept
{
    return std::memcmp(&left, &right, sizeof(left)) == 0;
}

bool ExactCamera(
    const openusd_render_camera& left,
    const openusd_render_camera& right) noexcept
{
    if (left.struct_size != right.struct_size ||
        left.mode != right.mode ||
        left.clip_plane_count != right.clip_plane_count)
    {
        return false;
    }
    if (std::memcmp(
            left.clip_planes,
            right.clip_planes,
            static_cast<size_t>(left.clip_plane_count) * 4 * sizeof(double)) != 0)
    {
        return false;
    }
    if (left.mode == OPENUSD_RENDER_CAMERA_MODE_AUTO)
    {
        return true;
    }
    return std::memcmp(left.view, right.view, sizeof(left.view)) == 0 &&
        std::memcmp(
            left.projection,
            right.projection,
            sizeof(left.projection)) == 0;
}

bool CheckedRequiredSize(const std::string& value, uint32_t& required) noexcept
{
    const size_t size = value.size() + 1;
    if (size > std::numeric_limits<uint32_t>::max())
    {
        return false;
    }
    required = static_cast<uint32_t>(size);
    return true;
}

void PublishRenderedBinding(
    const openusd_storm_renderer* renderer,
    openusd_render_pick_result* result) noexcept
{
    if (!renderer->has_rendered_state)
    {
        result->state_revision = std::numeric_limits<uint64_t>::max();
        return;
    }
    result->state_revision = renderer->rendered_state_revision;
    result->scene_revision = renderer->rendered_scene_revision;
    result->time_code = renderer->rendered_time_code;
    result->camera_signature =
        openusd_render_camera_detail::Signature(renderer->requested_camera);
    if ((renderer->rendered_revision_flags &
         OPENUSD_STORM_RENDER_HAS_SCENE_REVISION) != 0)
    {
        result->flags |= OPENUSD_RENDER_PICK_RESULT_HAS_SCENE_REVISION;
    }
}

uint32_t RenderedStateMismatchFlags(
    const openusd_storm_renderer* renderer,
    const openusd_render_pick_request& request) noexcept
{
    if (!renderer->has_rendered_state)
    {
        return OPENUSD_RENDER_PICK_RESULT_STALE_BACKEND_STATE;
    }

    uint32_t flags = 0;
    if (request.context_generation != 0)
    {
        flags |= OPENUSD_RENDER_PICK_RESULT_STALE_CONTEXT_GENERATION;
    }
    if (request.viewport_width != renderer->rendered_width ||
        request.viewport_height != renderer->rendered_height)
    {
        flags |= OPENUSD_RENDER_PICK_RESULT_STALE_VIEWPORT;
    }
    if (!ExactDouble(request.time_code, renderer->rendered_time_code))
    {
        flags |= OPENUSD_RENDER_PICK_RESULT_STALE_TIME;
    }
    if (request.state_revision != renderer->rendered_state_revision)
    {
        flags |= OPENUSD_RENDER_PICK_RESULT_STALE_STATE_REVISION;
    }
    if (!ExactCamera(request.camera, renderer->requested_camera))
    {
        flags |= OPENUSD_RENDER_PICK_RESULT_STALE_CAMERA;
    }
    const bool request_has_scene =
        (request.flags & OPENUSD_RENDER_PICK_REQUEST_HAS_SCENE_REVISION) != 0;
    const bool render_has_scene =
        (renderer->rendered_revision_flags &
         OPENUSD_STORM_RENDER_HAS_SCENE_REVISION) != 0;
    if (request_has_scene &&
        (!render_has_scene ||
         request.scene_revision != renderer->rendered_scene_revision))
    {
        flags |= OPENUSD_RENDER_PICK_RESULT_STALE_SCENE_REVISION;
    }
    return flags;
}
}

extern "C" OPENUSD_HYDRA_API uint32_t openusd_storm_get_abi_version(void) noexcept
{
    return OPENUSD_STORM_ABI_VERSION;
}

openusd_status openusd_storm_get_headlight(
    openusd_render_headlight* headlight,
    openusd_error_buffer* error)
{
    if (headlight == nullptr)
    {
        WriteError(error, "A headlight structure is required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (headlight->struct_size != sizeof(openusd_render_headlight) ||
        headlight->version != OPENUSD_RENDER_HEADLIGHT_VERSION)
    {
        WriteError(error, "The headlight structure has an invalid ABI.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    *headlight = kStormHeadlight;
    return OPENUSD_STATUS_OK;
}

openusd_status openusd_storm_create(
    const char* plugin_path,
    const char* stage_path,
    openusd_storm_renderer** renderer,
    openusd_error_buffer* error)
{
    if (renderer != nullptr)
    {
        *renderer = nullptr;
    }
    if (plugin_path == nullptr || stage_path == nullptr || renderer == nullptr)
    {
        WriteError(error, "Plugin path, stage path, and renderer output are required.");
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
        return openusd_storm_create_from_stage(plugin_path, stage, renderer, error);
    });
}

openusd_status openusd_storm_create_from_stage(
    const char* plugin_path,
    openusd_stage* stage,
    openusd_storm_renderer** renderer,
    openusd_error_buffer* error)
{
    if (renderer != nullptr)
    {
        *renderer = nullptr;
    }
    if (plugin_path == nullptr || stage == nullptr || renderer == nullptr)
    {
        WriteError(error, "Plugin path, stage handle, and renderer output are required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    return Guard(error, [&]()
    {
        StageRetainGuard stage_guard(stage, error);
        if (stage_guard.RetainStatus() != OPENUSD_STATUS_OK)
        {
            return stage_guard.RetainStatus();
        }
        if (IsRendererCreateFailpoint("after-retain"))
        {
            throw std::bad_alloc();
        }

        auto result = std::make_unique<openusd_storm_renderer>();
        StormInitializationContext context{plugin_path, result.get()};
        const openusd_status status = WithStageAccess(stage, error, [&](openusd_stage_access* access)
        {
            return openusd_renderer_stage_initialize(
                access,
                InitializeStormRenderer,
                &context,
                error);
        });
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }

        result->stage_core = stage;
        stage_guard.Dismiss();
        const size_t live =
            g_live_storm_renderer_count.fetch_add(1, std::memory_order_relaxed) + 1;
        UpdatePeak(g_peak_storm_renderer_count, live);
        *renderer = result.release();
        return OPENUSD_STATUS_OK;
    });
}

void openusd_storm_release(openusd_storm_renderer* renderer) noexcept
{
    try
    {
        openusd_error_buffer error{nullptr, 0, 0};
        static_cast<void>(openusd_storm_destroy(renderer, &error));
    }
    catch (...)
    {
    }
}

openusd_status openusd_storm_destroy(
    openusd_storm_renderer* renderer,
    openusd_error_buffer* error)
{
    const openusd_status validation = ValidateStormOwner(renderer, error);
    if (validation != OPENUSD_STATUS_OK)
    {
        return validation;
    }

    return Guard(error, [&]()
    {
        const openusd_status status = WithStageAccess(
            renderer->stage_core,
            error,
            [&](openusd_stage_access*)
            {
                renderer->engine.reset();
                renderer->stage.Reset();
                return OPENUSD_STATUS_OK;
            });
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }

        openusd_stage* stage = renderer->stage_core;
        renderer->stage_core = nullptr;
        openusd_stage_release(stage);
        g_live_storm_renderer_count.fetch_sub(1, std::memory_order_relaxed);
        delete renderer;
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_storm_abandon(
    openusd_storm_renderer* renderer,
    openusd_error_buffer* error)
{
    const openusd_status validation = ValidateStormThread(renderer, error);
    if (validation != OPENUSD_STATUS_OK)
    {
        return validation;
    }

    return Guard(error, [&]()
    {
        const openusd_status status = WithStageAccess(
            renderer->stage_core,
            error,
            [&](openusd_stage_access*)
            {
                UsdImagingGLEngine* abandoned_engine = renderer->engine.release();
                if (abandoned_engine != nullptr)
                {
                    g_abandoned_storm_engine_count.fetch_add(
                        1, std::memory_order_relaxed);
                }
                renderer->stage.Reset();
                renderer->name.clear();
                renderer->context_identity = 0;
                return OPENUSD_STATUS_OK;
            });
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }

        openusd_stage* stage = renderer->stage_core;
        renderer->stage_core = nullptr;
        openusd_stage_release(stage);
        g_live_storm_renderer_count.fetch_sub(1, std::memory_order_relaxed);
        delete renderer;
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_storm_render(
    openusd_storm_renderer* renderer,
    int32_t width,
    int32_t height,
    uint32_t framebuffer,
    double time_code,
    const openusd_render_camera* camera,
    int32_t* converged,
    openusd_error_buffer* error)
{
    return openusd_storm_render_v2(
        renderer,
        width,
        height,
        framebuffer,
        time_code,
        camera,
        0,
        0,
        0,
        converged,
        error);
}

openusd_status openusd_storm_render_v2(
    openusd_storm_renderer* renderer,
    int32_t width,
    int32_t height,
    uint32_t framebuffer,
    double time_code,
    const openusd_render_camera* camera,
    uint64_t state_revision,
    uint64_t scene_revision,
    uint32_t revision_flags,
    int32_t* converged,
    openusd_error_buffer* error)
{
    if (converged != nullptr)
    {
        *converged = 0;
    }
    if (renderer == nullptr || renderer->stage_core == nullptr ||
        !renderer->stage || !renderer->engine ||
        width <= 0 || height <= 0 || converged == nullptr)
    {
        WriteError(error, "A valid renderer, viewport size, and convergence output are required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    constexpr uint32_t kValidRenderFlags =
        OPENUSD_STORM_RENDER_HAS_SCENE_REVISION |
        OPENUSD_STORM_RENDER_USE_SCENE_LIGHTS;
    if (!std::isfinite(time_code) ||
        (revision_flags & ~kValidRenderFlags) != 0)
    {
        WriteError(error, "The render time or revision flags are invalid.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    std::string camera_error;
    if (!openusd_render_camera_detail::Validate(camera, camera_error))
    {
        WriteError(error, camera_error);
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    const openusd_status validation = ValidateStormOwner(renderer, error);
    if (validation != OPENUSD_STATUS_OK)
    {
        return validation;
    }

    return Guard(error, [&]()
    {
        return WithStageAccess(renderer->stage_core, error, [&](openusd_stage_access*)
        {
            TfErrorMark mark;
            glBindFramebuffer(GL_FRAMEBUFFER, framebuffer);
            glViewport(0, 0, width, height);
            glClearColor(0.055f, 0.055f, 0.055f, 1.0f);
            glClearDepth(1.0);
            glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

            GfMatrix4d view(1.0);
            GfMatrix4d projection(1.0);
            ResolveCamera(*camera, width, height, view, projection);

            renderer->engine->SetCameraState(view, projection);
            renderer->applied_camera = AppliedCamera(view, projection, *camera);
            renderer->engine->SetRenderBufferSize(GfVec2i(width, height));
            renderer->engine->SetRenderViewport(
                GfVec4d(0.0, 0.0, width, height));
            renderer->engine->SetPresentationOutput(
                HgiTokens->OpenGL, VtValue(framebuffer));
            const bool useSceneLights =
                (revision_flags & OPENUSD_STORM_RENDER_USE_SCENE_LIGHTS) != 0;
            if (useSceneLights)
            {
                const StormSceneLighting lighting =
                    MakeStormSceneLighting(renderer->stage, time_code);
                renderer->engine->SetLightingState(
                    lighting.lights,
                    MakeStormFallbackMaterial(),
                    lighting.ambient);
            }
            else
            {
                renderer->engine->SetLightingState(
                    MakeStormHeadlightLights(),
                    MakeStormFallbackMaterial(),
                    GfVec4f(
                        kStormHeadlight.ambient,
                        kStormHeadlight.ambient,
                        kStormHeadlight.ambient,
                        1.0f));
            }
            UsdImagingGLRenderParams parameters;
            parameters.frame = UsdTimeCode(time_code);
            parameters.showRender = true;
            parameters.enableLighting = true;
            parameters.enableSceneLights = false;
            parameters.enableSceneMaterials = true;
            parameters.highlight = true;
            parameters.clearColor = GfVec4f(0.055f, 0.055f, 0.055f, 1.0f);
            ApplyClipPlanes(*camera, parameters);
            renderer->last_render_clip_plane_count = parameters.clipPlanes.size();
            renderer->engine->Render(renderer->stage->GetPseudoRoot(), parameters);
            if (!mark.IsClean())
            {
                WriteError(error, ConsumeErrors(mark));
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            renderer->requested_camera = *camera;
            renderer->rendered_width = width;
            renderer->rendered_height = height;
            renderer->rendered_time_code = time_code;
            renderer->rendered_state_revision = state_revision;
            renderer->rendered_scene_revision = scene_revision;
            renderer->rendered_revision_flags = revision_flags;
            renderer->has_rendered_state = true;
            *converged = renderer->engine->IsConverged() ? 1 : 0;
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_storm_pick(
    openusd_storm_renderer* renderer,
    const openusd_render_pick_request* request,
    openusd_render_pick_result* result,
    char* prim_path_buffer,
    uint32_t prim_path_capacity,
    char* instancer_path_buffer,
    uint32_t instancer_path_capacity,
    openusd_render_pick_instance_context* instance_context,
    uint32_t instance_context_capacity,
    char* instance_context_paths_buffer,
    uint32_t instance_context_paths_capacity,
    openusd_error_buffer* error)
{
    if (prim_path_buffer != nullptr && prim_path_capacity != 0)
    {
        prim_path_buffer[0] = '\0';
    }
    if (instancer_path_buffer != nullptr && instancer_path_capacity != 0)
    {
        instancer_path_buffer[0] = '\0';
    }
    if (instance_context_paths_buffer != nullptr &&
        instance_context_paths_capacity != 0)
    {
        instance_context_paths_buffer[0] = '\0';
    }
    if (instance_context != nullptr && instance_context_capacity != 0)
    {
        std::memset(
            instance_context,
            0,
            static_cast<size_t>(instance_context_capacity) *
                sizeof(*instance_context));
    }

    std::string validation_error;
    if (!openusd_render_pick_detail::ValidateResult(result, validation_error))
    {
        WriteError(error, validation_error);
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (!openusd_render_pick_detail::ValidateRequest(request, validation_error) ||
        !ValidOutputBuffer(prim_path_buffer, prim_path_capacity) ||
        !ValidOutputBuffer(instancer_path_buffer, instancer_path_capacity) ||
        !ValidOutputBuffer(instance_context, instance_context_capacity) ||
        !ValidOutputBuffer(
            instance_context_paths_buffer,
            instance_context_paths_capacity))
    {
        result->status = OPENUSD_RENDER_PICK_STATUS_INVALID;
        WriteError(
            error,
            validation_error.empty()
                ? "The pick output buffer arguments are invalid."
                : validation_error);
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    std::string camera_error;
    if (!openusd_render_camera_detail::Validate(&request->camera, camera_error))
    {
        result->status = OPENUSD_RENDER_PICK_STATUS_INVALID;
        WriteError(error, camera_error);
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    const openusd_status validation = ValidateStormOwner(renderer, error);
    if (validation != OPENUSD_STATUS_OK)
    {
        result->status = OPENUSD_RENDER_PICK_STATUS_ERROR;
        return validation;
    }

    PublishRenderedBinding(renderer, result);
    const uint32_t stale_flags =
        RenderedStateMismatchFlags(renderer, *request);
    if (stale_flags != 0)
    {
        result->status = OPENUSD_RENDER_PICK_STATUS_STALE;
        result->flags |= stale_flags;
        return OPENUSD_STATUS_OK;
    }
    if (request->target != OPENUSD_RENDER_PICK_TARGET_PRIMITIVE)
    {
        result->status = OPENUSD_RENDER_PICK_STATUS_UNSUPPORTED;
        return OPENUSD_STATUS_OK;
    }

    const openusd_status pick_status = Guard(error, [&]()
    {
        return WithStageAccess(renderer->stage_core, error, [&](openusd_stage_access*)
        {
            TfErrorMark mark;
            GfMatrix4d view(1.0);
            GfMatrix4d projection(1.0);
            openusd_render_camera_detail::AssignRowMajor(
                renderer->applied_camera.view,
                view);
            openusd_render_camera_detail::AssignRowMajor(
                renderer->applied_camera.projection,
                projection);
            const GfMatrix4d pick_projection =
                openusd_render_pick_detail::NarrowProjection(
                    projection,
                    request->x,
                    request->y,
                    request->viewport_width,
                    request->viewport_height);

            UsdImagingGLRenderParams parameters;
            parameters.frame = UsdTimeCode(request->time_code);
            parameters.showRender = true;
            ApplyClipPlanes(request->camera, parameters);
            renderer->last_pick_clip_plane_count = parameters.clipPlanes.size();
            if ((request->flags &
                 OPENUSD_RENDER_PICK_REQUEST_CULL_BACK_FACES) != 0)
            {
                parameters.cullStyle =
                    UsdImagingGLCullStyle::
                        CULL_STYLE_BACK_UNLESS_DOUBLE_SIDED;
            }
            UsdImagingGLEngine::PickParams pick_parameters{
                HdxPickTokens->resolveNearestToCenter};
            UsdImagingGLEngine::IntersectionResultVector hits;
            const bool hit = renderer->engine->TestIntersection(
                pick_parameters,
                view,
                pick_projection,
                renderer->stage->GetPseudoRoot(),
                parameters,
                &hits);
            if (!mark.IsClean())
            {
                result->status = OPENUSD_RENDER_PICK_STATUS_ERROR;
                WriteError(error, ConsumeErrors(mark));
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            if (!hit || hits.empty())
            {
                result->status = OPENUSD_RENDER_PICK_STATUS_MISS;
                return OPENUSD_STATUS_OK;
            }
            if (hits.size() != 1)
            {
                result->status = OPENUSD_RENDER_PICK_STATUS_ERROR;
                WriteError(
                    error,
                    "Nearest-to-center picking returned more than one hit.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            const UsdImagingGLEngine::IntersectionResult& nearest = hits.front();
            const std::string prim_path = nearest.hitPrimPath.GetString();
            std::string instancer_path = nearest.hitInstancerPath.GetString();
            if (instancer_path.empty() && !nearest.instancerContext.empty())
            {
                instancer_path = nearest.instancerContext.back().first.GetString();
            }
            if (prim_path.empty())
            {
                result->status = OPENUSD_RENDER_PICK_STATUS_ERROR;
                WriteError(error, "Storm returned a hit without a prim path.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            result->status = OPENUSD_RENDER_PICK_STATUS_HIT;
            for (size_t index = 0; index < 3; ++index)
            {
                result->world_point[index] = nearest.hitPoint[index];
                result->world_normal[index] = nearest.hitNormal[index];
            }
            result->normalized_depth =
                openusd_render_pick_detail::NormalizedOpenGlDepth(
                    nearest.hitPoint,
                    view,
                    projection);
            if (!instancer_path.empty() && nearest.hitInstanceIndex >= 0)
            {
                result->flags |= OPENUSD_RENDER_PICK_RESULT_HAS_INSTANCE;
                result->instance_index = nearest.hitInstanceIndex;
            }

            if (!CheckedRequiredSize(
                    prim_path,
                    result->prim_path_required) ||
                !CheckedRequiredSize(
                    instancer_path,
                    result->instancer_path_required) ||
                nearest.instancerContext.size() >
                    std::numeric_limits<uint32_t>::max())
            {
                result->status = OPENUSD_RENDER_PICK_STATUS_ERROR;
                WriteError(error, "Storm returned pick identity that exceeds ABI limits.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }

            uint64_t context_paths_required = 0;
            for (const auto& entry : nearest.instancerContext)
            {
                context_paths_required += entry.first.GetString().size() + 1;
            }
            if (context_paths_required > std::numeric_limits<uint32_t>::max())
            {
                result->status = OPENUSD_RENDER_PICK_STATUS_ERROR;
                WriteError(error, "Storm returned an oversized instancer context.");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            result->instance_context_count =
                static_cast<uint32_t>(nearest.instancerContext.size());
            result->instance_context_paths_required =
                static_cast<uint32_t>(context_paths_required);
            if (result->instance_context_count != 0)
            {
                result->flags |=
                    OPENUSD_RENDER_PICK_RESULT_HAS_INSTANCE_CONTEXT;
            }

            const bool buffers_fit =
                prim_path_capacity >= result->prim_path_required &&
                instancer_path_capacity >= result->instancer_path_required &&
                instance_context_capacity >= result->instance_context_count &&
                instance_context_paths_capacity >=
                    result->instance_context_paths_required;
            if (!buffers_fit)
            {
                WriteError(error, "One or more pick output buffers are too small.");
                return OPENUSD_STATUS_BUFFER_TOO_SMALL;
            }

            std::memcpy(
                prim_path_buffer,
                prim_path.c_str(),
                result->prim_path_required);
            std::memcpy(
                instancer_path_buffer,
                instancer_path.c_str(),
                result->instancer_path_required);
            uint32_t context_offset = 0;
            for (uint32_t index = 0;
                 index < result->instance_context_count;
                 ++index)
            {
                const auto& source = nearest.instancerContext[index];
                const std::string path = source.first.GetString();
                auto& destination = instance_context[index];
                destination.struct_size = sizeof(destination);
                destination.version =
                    OPENUSD_RENDER_PICK_INSTANCE_CONTEXT_VERSION;
                destination.path_offset = context_offset;
                destination.path_length =
                    static_cast<uint32_t>(path.size());
                destination.instance_index = source.second;
                std::memcpy(
                    instance_context_paths_buffer + context_offset,
                    path.c_str(),
                    path.size() + 1);
                context_offset += static_cast<uint32_t>(path.size() + 1);
            }
            return OPENUSD_STATUS_OK;
        });
    });
    if (pick_status != OPENUSD_STATUS_OK &&
        pick_status != OPENUSD_STATUS_BUFFER_TOO_SMALL)
    {
        result->status = OPENUSD_RENDER_PICK_STATUS_ERROR;
    }
    return pick_status;
}

openusd_status openusd_storm_set_selection(
    openusd_storm_renderer* renderer,
    const openusd_storm_selection_update* update,
    openusd_error_buffer* error)
{
    if (update == nullptr ||
        update->struct_size != sizeof(openusd_storm_selection_update) ||
        update->version != OPENUSD_STORM_SELECTION_UPDATE_VERSION ||
        update->flags != 0 ||
        update->reserved != 0 ||
        (update->item_count != 0 && update->items == nullptr) ||
        (update->path_bytes_size != 0 && update->path_bytes == nullptr))
    {
        WriteError(error, "The packed Storm selection update is invalid.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    for (float component : update->color)
    {
        if (!std::isfinite(component) || component < 0.0f || component > 1.0f)
        {
            WriteError(error, "Storm selection color components must be in [0, 1].");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
    }

    SdfPathVector paths;
    std::vector<std::pair<SdfPath, int32_t>> instances;
    paths.reserve(update->item_count);
    instances.reserve(update->item_count);
    for (uint32_t index = 0; index < update->item_count; ++index)
    {
        const openusd_storm_selection_item& item = update->items[index];
        if ((item.flags & ~OPENUSD_STORM_SELECTION_ITEM_HAS_INSTANCE_INDEX) != 0 ||
            item.path_length == 0 ||
            item.path_offset > update->path_bytes_size ||
            item.path_length > update->path_bytes_size - item.path_offset)
        {
            WriteError(error, "A packed Storm selection item is invalid.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const std::string path_text(
            update->path_bytes + item.path_offset,
            item.path_length);
        if (path_text.find('\0') != std::string::npos)
        {
            WriteError(error, "Storm selection paths cannot contain NUL bytes.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const SdfPath path(path_text);
        if (!path.IsAbsolutePath() || !path.IsPrimPath() ||
            path == SdfPath::AbsoluteRootPath())
        {
            WriteError(error, "Storm selection paths must be absolute prim paths.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if ((item.flags &
             OPENUSD_STORM_SELECTION_ITEM_HAS_INSTANCE_INDEX) != 0)
        {
            if (item.instance_index < 0)
            {
                WriteError(error, "Storm selection instance indices cannot be negative.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            instances.emplace_back(path, item.instance_index);
        }
        else
        {
            paths.push_back(path);
        }
    }

    const openusd_status validation = ValidateStormOwner(renderer, error);
    if (validation != OPENUSD_STATUS_OK)
    {
        return validation;
    }
    return Guard(error, [&]()
    {
        return WithStageAccess(renderer->stage_core, error, [&](openusd_stage_access*)
        {
            TfErrorMark mark;
            if (paths.empty())
            {
                renderer->engine->ClearSelected();
            }
            else
            {
                renderer->engine->SetSelected(paths);
            }
            for (const auto& instance : instances)
            {
                renderer->engine->AddSelected(instance.first, instance.second);
            }
            renderer->engine->SetSelectionColor(GfVec4f(
                update->color[0],
                update->color[1],
                update->color[2],
                update->color[3]));
            if (!mark.IsClean())
            {
                WriteError(error, ConsumeErrors(mark));
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_storm_set_transform_overrides(
    openusd_storm_renderer* renderer,
    const openusd_storm_transform_override_update* update,
    openusd_error_buffer* error)
{
    if (update == nullptr ||
        update->struct_size != sizeof(openusd_storm_transform_override_update) ||
        update->version != OPENUSD_STORM_TRANSFORM_OVERRIDE_UPDATE_VERSION ||
        (update->flags & ~OPENUSD_STORM_TRANSFORM_OVERRIDE_UPDATE_REPLACE) != 0 ||
        update->reserved != 0 ||
        update->item_count > OPENUSD_STORM_TRANSFORM_OVERRIDE_MAXIMUM_ITEMS ||
        update->path_bytes_size > OPENUSD_STORM_TRANSFORM_OVERRIDE_MAXIMUM_PATH_BYTES ||
        (update->item_count != 0 && update->items == nullptr) ||
        (update->path_bytes_size != 0 && update->path_bytes == nullptr))
    {
        WriteError(error, "The packed Storm transform override update is invalid.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    std::vector<OpenUsdPhysicsOverrideEntry> entries;
    entries.reserve(update->item_count);
    uint32_t unsupported = 0;
    for (uint32_t index = 0; index < update->item_count; ++index)
    {
        const openusd_storm_transform_override_item& item = update->items[index];
        constexpr uint32_t kValidItemFlags =
            OPENUSD_STORM_TRANSFORM_OVERRIDE_ITEM_SNAPPED |
            OPENUSD_STORM_TRANSFORM_OVERRIDE_ITEM_PRESERVE_STRETCH;
        if ((item.flags & ~kValidItemFlags) != 0 ||
            item.path_length == 0 ||
            item.path_offset > update->path_bytes_size ||
            item.path_length > update->path_bytes_size - item.path_offset)
        {
            WriteError(error, "A packed Storm transform override item is invalid.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        for (double component : item.transform)
        {
            if (!std::isfinite(component))
            {
                WriteError(
                    error,
                    "Storm transform override matrices must be finite.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
        }
        const std::string path_text(
            update->path_bytes + item.path_offset,
            item.path_length);
        if (path_text.find('\0') != std::string::npos)
        {
            WriteError(
                error,
                "Storm transform override paths cannot contain NUL bytes.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const SdfPath path(path_text);
        if (!path.IsAbsolutePath() || !path.IsPrimPath() ||
            path == SdfPath::AbsoluteRootPath())
        {
            WriteError(
                error,
                "Storm transform override paths must be absolute prim paths.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (item.instance_index >= 0)
        {
            // Point instancer members are diagnosed rather than rejected so a
            // mixed batch still renders every supported rigid body.
            ++unsupported;
            continue;
        }
        OpenUsdPhysicsOverrideEntry entry;
        entry.path = path;
        entry.object_id = item.object_id;
        entry.preserve_stretch =
            (item.flags &
             OPENUSD_STORM_TRANSFORM_OVERRIDE_ITEM_PRESERVE_STRETCH) != 0;
        std::memcpy(
            entry.transform.GetArray(),
            item.transform,
            sizeof(item.transform));
        entries.push_back(std::move(entry));
    }

    const openusd_status validation = ValidateStormOwner(renderer, error);
    if (validation != OPENUSD_STATUS_OK)
    {
        return validation;
    }
    if (!renderer->physics_overrides)
    {
        WriteError(
            error,
            "This Storm renderer did not install a transform override scene index.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    return Guard(error, [&]()
    {
        return WithStageAccess(renderer->stage_core, error, [&](openusd_stage_access*)
        {
            TfErrorMark mark;
            uint32_t unresolved = 0;
            std::vector<OpenUsdPhysicsOverrideEntry> resolved;
            resolved.reserve(entries.size());
            for (OpenUsdPhysicsOverrideEntry& entry : entries)
            {
                if (renderer->stage &&
                    !renderer->stage->GetPrimAtPath(entry.path))
                {
                    ++unresolved;
                    continue;
                }
                resolved.push_back(std::move(entry));
            }
            renderer->physics_overrides->ApplyBatch(
                resolved,
                update->revision,
                unresolved,
                0,
                unsupported);
            if (!mark.IsClean())
            {
                renderer->physics_overrides->RecordRejectedBatch();
                WriteError(error, ConsumeErrors(mark));
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_storm_get_transform_override_diagnostics(
    openusd_storm_renderer* renderer,
    openusd_storm_transform_override_diagnostics* diagnostics,
    openusd_error_buffer* error)
{
    if (diagnostics == nullptr ||
        diagnostics->struct_size !=
            sizeof(openusd_storm_transform_override_diagnostics) ||
        diagnostics->version !=
            OPENUSD_STORM_TRANSFORM_OVERRIDE_DIAGNOSTICS_VERSION)
    {
        WriteError(
            error,
            "The Storm transform override diagnostics struct is invalid.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const openusd_status validation = ValidateStormOwner(renderer, error);
    if (validation != OPENUSD_STATUS_OK)
    {
        return validation;
    }
    return Guard(error, [&]()
    {
        const uint32_t struct_size = diagnostics->struct_size;
        const uint32_t version = diagnostics->version;
        *diagnostics = openusd_storm_transform_override_diagnostics{};
        diagnostics->struct_size = struct_size;
        diagnostics->version = version;
        diagnostics->capacity = OPENUSD_STORM_TRANSFORM_OVERRIDE_MAXIMUM_ITEMS;
        if (renderer->physics_overrides)
        {
            const OpenUsdPhysicsOverrideCounters counters =
                renderer->physics_overrides->GetCounters();
            diagnostics->applied_count = counters.applied_count;
            diagnostics->unresolved_count = counters.unresolved_count;
            diagnostics->revision = counters.revision;
            diagnostics->applied_batch_count = counters.applied_batch_count;
            diagnostics->rejected_batch_count = counters.rejected_batch_count;
            diagnostics->dirtied_prim_count = counters.dirtied_prim_count;
            diagnostics->dropped_count = counters.dropped_count;
            diagnostics->unsupported_count = counters.unsupported_count;
        }
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_storm_set_deformation_overrides(
    openusd_storm_renderer* renderer,
    const openusd_storm_deformation_override_update* update,
    openusd_error_buffer* error)
{
    if (update == nullptr ||
        update->struct_size != sizeof(openusd_storm_deformation_override_update) ||
        update->version != OPENUSD_STORM_DEFORMATION_OVERRIDE_UPDATE_VERSION ||
        (update->flags & ~OPENUSD_STORM_DEFORMATION_OVERRIDE_UPDATE_REPLACE) != 0 ||
        (update->flags & OPENUSD_STORM_DEFORMATION_OVERRIDE_UPDATE_REPLACE) == 0 ||
        update->item_count > OPENUSD_STORM_DEFORMATION_OVERRIDE_MAXIMUM_ITEMS ||
        update->point_count > OPENUSD_STORM_DEFORMATION_OVERRIDE_MAXIMUM_POINTS ||
        update->path_bytes_size > OPENUSD_STORM_DEFORMATION_OVERRIDE_MAXIMUM_PATH_BYTES ||
        (update->item_count != 0 && update->items == nullptr) ||
        (update->point_count != 0 && update->points == nullptr) ||
        (update->path_bytes_size != 0 && update->path_bytes == nullptr))
    {
        WriteError(error, "The packed Storm deformation override update is invalid.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    /* Every region is checked against the page it addresses before a single
     * float is read, so a malformed batch is refused before it can index
     * anything. */
    std::vector<OpenUsdPhysicsDeformationEntry> entries;
    entries.reserve(update->item_count);
    uint32_t unsupported = 0;
    for (uint32_t index = 0; index < update->item_count; ++index)
    {
        const openusd_storm_deformation_override_item& item = update->items[index];
        if (item.path_length == 0 ||
            item.path_offset > update->path_bytes_size ||
            item.path_length > update->path_bytes_size - item.path_offset ||
            item.point_count == 0 ||
            item.point_offset > update->point_count ||
            item.point_count > update->point_count - item.point_offset ||
            (item.flags & ~OPENUSD_STORM_DEFORMATION_OVERRIDE_ITEM_SNAPPED) != 0)
        {
            WriteError(error, "A packed Storm deformation override region is invalid.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const std::string path_text(
            update->path_bytes + item.path_offset,
            item.path_length);
        if (path_text.find('\0') != std::string::npos)
        {
            WriteError(
                error,
                "Storm deformation override paths cannot contain NUL bytes.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        const SdfPath path(path_text);
        if (!path.IsAbsolutePath() || !path.IsPrimPath() ||
            path == SdfPath::AbsoluteRootPath())
        {
            WriteError(
                error,
                "Storm deformation override paths must be absolute prim paths.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (item.instance_index >= 0)
        {
            // Point instancer members share one prototype, so replacing its
            // points would deform every instance. That is diagnosed rather than
            // rejected so a mixed batch still draws every supported region.
            ++unsupported;
            continue;
        }

        OpenUsdPhysicsDeformationEntry entry;
        entry.path = path;
        entry.object_id = item.object_id;
        entry.topology_revision = item.topology_revision;
        entry.points.resize(item.point_count);
        for (uint32_t point = 0; point < item.point_count; ++point)
        {
            const float* source =
                update->points + (static_cast<size_t>(item.point_offset + point) * 3u);
            if (!std::isfinite(source[0]) || !std::isfinite(source[1]) ||
                !std::isfinite(source[2]))
            {
                WriteError(
                    error,
                    "Storm deformation override points must be finite.");
                return OPENUSD_STATUS_INVALID_ARGUMENT;
            }
            entry.points[point] = GfVec3f(source[0], source[1], source[2]);
        }
        entries.push_back(std::move(entry));
    }

    const openusd_status validation = ValidateStormOwner(renderer, error);
    if (validation != OPENUSD_STATUS_OK)
    {
        return validation;
    }
    if (!renderer->physics_overrides)
    {
        WriteError(
            error,
            "This Storm renderer did not install a physics override scene index.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    return Guard(error, [&]()
    {
        return WithStageAccess(renderer->stage_core, error, [&](openusd_stage_access*)
        {
            TfErrorMark mark;
            uint32_t unresolved = 0;
            uint32_t mismatched = 0;
            std::vector<OpenUsdPhysicsDeformationEntry> resolved;
            resolved.reserve(entries.size());
            for (OpenUsdPhysicsDeformationEntry& entry : entries)
            {
                if (renderer->stage && !renderer->stage->GetPrimAtPath(entry.path))
                {
                    ++unresolved;
                    continue;
                }
                // A region only draws when the rendered prim already has that
                // many vertices. Anything else would hand the prim's own indices
                // vertices they never addressed, so it is refused and counted
                // rather than drawn.
                const size_t rendered =
                    renderer->physics_overrides->GetRenderedPointCount(entry.path);
                if (rendered != entry.points.size())
                {
                    ++mismatched;
                    continue;
                }
                resolved.push_back(std::move(entry));
            }
            renderer->physics_overrides->ApplyDeformationBatch(
                resolved,
                update->revision,
                unresolved,
                0,
                unsupported,
                mismatched);
            if (!mark.IsClean())
            {
                renderer->physics_overrides->RecordRejectedDeformationBatch();
                WriteError(error, ConsumeErrors(mark));
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            return OPENUSD_STATUS_OK;
        });
    });
}

openusd_status openusd_storm_get_deformation_override_diagnostics(
    openusd_storm_renderer* renderer,
    openusd_storm_deformation_override_diagnostics* diagnostics,
    openusd_error_buffer* error)
{
    if (diagnostics == nullptr ||
        diagnostics->struct_size !=
            sizeof(openusd_storm_deformation_override_diagnostics) ||
        diagnostics->version !=
            OPENUSD_STORM_DEFORMATION_OVERRIDE_DIAGNOSTICS_VERSION)
    {
        WriteError(
            error,
            "The Storm deformation override diagnostics struct is invalid.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const openusd_status validation = ValidateStormOwner(renderer, error);
    if (validation != OPENUSD_STATUS_OK)
    {
        return validation;
    }
    return Guard(error, [&]()
    {
        const uint32_t struct_size = diagnostics->struct_size;
        const uint32_t version = diagnostics->version;
        *diagnostics = openusd_storm_deformation_override_diagnostics{};
        diagnostics->struct_size = struct_size;
        diagnostics->version = version;
        diagnostics->capacity = OPENUSD_STORM_DEFORMATION_OVERRIDE_MAXIMUM_ITEMS;
        if (renderer->physics_overrides)
        {
            const OpenUsdPhysicsDeformationCounters counters =
                renderer->physics_overrides->GetDeformationCounters();
            diagnostics->applied_count = counters.applied_count;
            diagnostics->unresolved_count = counters.unresolved_count;
            diagnostics->revision = counters.revision;
            diagnostics->applied_batch_count = counters.applied_batch_count;
            diagnostics->rejected_batch_count = counters.rejected_batch_count;
            diagnostics->dirtied_prim_count = counters.dirtied_prim_count;
            diagnostics->dropped_count = counters.dropped_count;
            diagnostics->unsupported_count = counters.unsupported_count;
            diagnostics->mismatched_count = counters.mismatched_count;
        }
        return OPENUSD_STATUS_OK;
    });
}

openusd_status openusd_storm_get_renderer_name(
    const openusd_storm_renderer* renderer,
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
    if (renderer == nullptr || renderer->stage_core == nullptr)
    {
        WriteError(error, "A valid renderer is required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    return Guard(error, [&]()
    {
        return CopyString(renderer->name, buffer, capacity, required);
    });
}

extern "C" OPENUSD_HYDRA_API size_t openusd_storm_diagnostic_get_live_renderer_count(void) noexcept
{
    return g_live_storm_renderer_count.load(std::memory_order_relaxed);
}

extern "C" OPENUSD_HYDRA_API size_t openusd_storm_diagnostic_get_peak_renderer_count(void) noexcept
{
    return g_peak_storm_renderer_count.load(std::memory_order_relaxed);
}

extern "C" OPENUSD_HYDRA_API size_t openusd_storm_diagnostic_get_abandoned_engine_count(void) noexcept
{
    return g_abandoned_storm_engine_count.load(std::memory_order_relaxed);
}

extern "C" OPENUSD_HYDRA_API void openusd_storm_diagnostic_reset_peak_renderer_count(void) noexcept
{
    g_peak_storm_renderer_count.store(
        g_live_storm_renderer_count.load(std::memory_order_relaxed),
        std::memory_order_relaxed);
}

#if defined(OPENUSD_RENDERER_ENABLE_TEST_HOOKS)
extern "C" size_t openusd_hydra_test_get_abandoned_engine_count(void) noexcept
{
    return g_abandoned_storm_engine_count.load(std::memory_order_relaxed);
}

extern "C" int32_t openusd_hydra_test_get_applied_camera(
    const openusd_storm_renderer* renderer,
    openusd_render_camera* camera) noexcept
{
    if (renderer == nullptr || camera == nullptr ||
        camera->struct_size != sizeof(openusd_render_camera))
    {
        return 0;
    }
    *camera = renderer->applied_camera;
    return 1;
}

extern "C" size_t openusd_hydra_test_get_last_render_clip_plane_count(
    const openusd_storm_renderer* renderer) noexcept
{
    return renderer == nullptr ? 0 : renderer->last_render_clip_plane_count;
}

extern "C" size_t openusd_hydra_test_get_last_pick_clip_plane_count(
    const openusd_storm_renderer* renderer) noexcept
{
    return renderer == nullptr ? 0 : renderer->last_pick_clip_plane_count;
}
#endif
