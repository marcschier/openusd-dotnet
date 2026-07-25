// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_hydra.h"
#include "openusd_dotnet_test_hooks.h"
#include "openusd_hydra_test_hooks.h"

#include "pxr/base/gf/frustum.h"
#include "pxr/base/gf/matrix4d.h"
#include "pxr/base/gf/vec3d.h"

#include <Windows.h>
#include <gl/GL.h>

#include <array>
#include <cmath>
#include <cstdio>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <limits>
#include <string>
#include <thread>
#include <vector>

PXR_NAMESPACE_USING_DIRECTIVE

namespace
{
constexpr char WindowClassName[] = "OpenUsdStormSharedStageProbe";
constexpr char MeshPath[] = "/World/UnsavedStormMesh";
constexpr GLenum Framebuffer = 0x8D40;
constexpr GLenum Renderbuffer = 0x8D41;
constexpr GLenum ColorAttachment0 = 0x8CE0;
constexpr GLenum DepthAttachment = 0x8D00;
constexpr GLenum FramebufferComplete = 0x8CD5;
constexpr GLenum DepthComponent24 = 0x81A6;
constexpr GLint Rgba8 = 0x8058;
constexpr int CapabilityUnavailableExitCode = 125;
static_assert(OPENUSD_STORM_ABI_VERSION == 5);

enum class FramebufferCreationResult
{
    Success,
    Unsupported,
    Incomplete
};

openusd_render_camera AutomaticCamera()
{
    openusd_render_camera camera{};
    camera.struct_size = sizeof(camera);
    camera.mode = OPENUSD_RENDER_CAMERA_MODE_AUTO;
    return camera;
}

openusd_render_camera LegacyMatricesCamera()
{
    openusd_render_camera camera{};
    camera.struct_size = sizeof(camera);
    camera.mode = OPENUSD_RENDER_CAMERA_MODE_MATRICES;
    GfMatrix4d view(1.0);
    view.SetLookAt(
        GfVec3d(4.0, 3.0, 4.0),
        GfVec3d(0.0, 0.0, 0.0),
        GfVec3d(0.0, 1.0, 0.0));
    GfFrustum frustum;
    frustum.SetPerspective(45.0, 1.0, 0.1, 1000.0);
    const GfMatrix4d projection = frustum.ComputeProjectionMatrix();
    std::memcpy(camera.view, view.GetArray(), sizeof(camera.view));
    std::memcpy(
        camera.projection,
        projection.GetArray(),
        sizeof(camera.projection));
    return camera;
}

openusd_render_camera ExplicitCamera()
{
    openusd_render_camera camera{};
    camera.struct_size = sizeof(camera);
    camera.mode = OPENUSD_RENDER_CAMERA_MODE_MATRICES;
    GfMatrix4d view(1.0);
    view.SetTranslate(GfVec3d(1.0, 2.0, -6.0));
    GfFrustum frustum;
    frustum.SetPerspective(52.0, 1.0, 0.2, 500.0);
    const GfMatrix4d projection = frustum.ComputeProjectionMatrix();
    std::memcpy(camera.view, view.GetArray(), sizeof(camera.view));
    std::memcpy(
        camera.projection,
        projection.GetArray(),
        sizeof(camera.projection));
    return camera;
}

using GlGenFramebuffers = void(APIENTRY*)(GLsizei, GLuint*);
using GlBindFramebuffer = void(APIENTRY*)(GLenum, GLuint);
using GlFramebufferTexture2D =
    void(APIENTRY*)(GLenum, GLenum, GLenum, GLuint, GLint);
using GlCheckFramebufferStatus = GLenum(APIENTRY*)(GLenum);
using GlDeleteFramebuffers = void(APIENTRY*)(GLsizei, const GLuint*);
using GlGenRenderbuffers = void(APIENTRY*)(GLsizei, GLuint*);
using GlBindRenderbuffer = void(APIENTRY*)(GLenum, GLuint);
using GlRenderbufferStorage = void(APIENTRY*)(GLenum, GLenum, GLsizei, GLsizei);
using GlFramebufferRenderbuffer =
    void(APIENTRY*)(GLenum, GLenum, GLenum, GLuint);
using GlDeleteRenderbuffers = void(APIENTRY*)(GLsizei, const GLuint*);

template <typename T>
T LoadGl(const char* name)
{
    const PROC address = wglGetProcAddress(name);
    T result = nullptr;
    static_assert(sizeof(result) == sizeof(address));
    std::memcpy(&result, &address, sizeof(result));
    return result;
}

class WglContext
{
public:
    bool Create()
    {
        WNDCLASSA windowClass{};
        windowClass.style = CS_OWNDC;
        windowClass.lpfnWndProc = DefWindowProcA;
        windowClass.hInstance = GetModuleHandleA(nullptr);
        windowClass.lpszClassName = WindowClassName;
        _classAtom = RegisterClassA(&windowClass);
        if (_classAtom == 0 && GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
        {
            return false;
        }
        _ownsClass = _classAtom != 0;

        _window = CreateWindowExA(
            0,
            WindowClassName,
            WindowClassName,
            WS_OVERLAPPEDWINDOW,
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            64,
            64,
            nullptr,
            nullptr,
            windowClass.hInstance,
            nullptr);
        if (_window == nullptr)
        {
            return false;
        }
        _device = GetDC(_window);
        if (_device == nullptr)
        {
            return false;
        }

        PIXELFORMATDESCRIPTOR descriptor{};
        descriptor.nSize = sizeof(descriptor);
        descriptor.nVersion = 1;
        descriptor.dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER;
        descriptor.iPixelType = PFD_TYPE_RGBA;
        descriptor.cColorBits = 32;
        descriptor.cDepthBits = 24;
        descriptor.iLayerType = PFD_MAIN_PLANE;
        const int format = ChoosePixelFormat(_device, &descriptor);
        if (format == 0 || !SetPixelFormat(_device, format, &descriptor))
        {
            return false;
        }
        _context = wglCreateContext(_device);
        return _context != nullptr && MakeCurrent();
    }

    bool MakeCurrent() const
    {
        return wglMakeCurrent(_device, _context) != FALSE;
    }

    static void ClearCurrent()
    {
        wglMakeCurrent(nullptr, nullptr);
    }

    ~WglContext()
    {
        if (_context != nullptr)
        {
            if (wglGetCurrentContext() == _context)
            {
                wglMakeCurrent(nullptr, nullptr);
            }
            wglDeleteContext(_context);
        }
        if (_device != nullptr && _window != nullptr)
        {
            ReleaseDC(_window, _device);
        }
        if (_window != nullptr)
        {
            DestroyWindow(_window);
        }
        if (_ownsClass)
        {
            UnregisterClassA(WindowClassName, GetModuleHandleA(nullptr));
        }
    }

private:
    ATOM _classAtom = 0;
    HWND _window = nullptr;
    HDC _device = nullptr;
    HGLRC _context = nullptr;
    bool _ownsClass = false;
};

class OffscreenFramebuffer
{
public:
    FramebufferCreationResult Create()
    {
        _genFramebuffers = LoadGl<GlGenFramebuffers>("glGenFramebuffers");
        _bindFramebuffer = LoadGl<GlBindFramebuffer>("glBindFramebuffer");
        _framebufferTexture =
            LoadGl<GlFramebufferTexture2D>("glFramebufferTexture2D");
        _checkFramebuffer =
            LoadGl<GlCheckFramebufferStatus>("glCheckFramebufferStatus");
        _deleteFramebuffers =
            LoadGl<GlDeleteFramebuffers>("glDeleteFramebuffers");
        _genRenderbuffers =
            LoadGl<GlGenRenderbuffers>("glGenRenderbuffers");
        _bindRenderbuffer =
            LoadGl<GlBindRenderbuffer>("glBindRenderbuffer");
        _renderbufferStorage =
            LoadGl<GlRenderbufferStorage>("glRenderbufferStorage");
        _framebufferRenderbuffer =
            LoadGl<GlFramebufferRenderbuffer>("glFramebufferRenderbuffer");
        _deleteRenderbuffers =
            LoadGl<GlDeleteRenderbuffers>("glDeleteRenderbuffers");
        if (_genFramebuffers == nullptr || _bindFramebuffer == nullptr ||
            _framebufferTexture == nullptr || _checkFramebuffer == nullptr ||
            _deleteFramebuffers == nullptr || _genRenderbuffers == nullptr ||
            _bindRenderbuffer == nullptr || _renderbufferStorage == nullptr ||
            _framebufferRenderbuffer == nullptr || _deleteRenderbuffers == nullptr)
        {
            return FramebufferCreationResult::Unsupported;
        }

        glGenTextures(1, &_color);
        glBindTexture(GL_TEXTURE_2D, _color);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
        glTexImage2D(
            GL_TEXTURE_2D,
            0,
            Rgba8,
            64,
            64,
            0,
            GL_RGBA,
            GL_UNSIGNED_BYTE,
            nullptr);

        _genRenderbuffers(1, &_depth);
        _bindRenderbuffer(Renderbuffer, _depth);
        _renderbufferStorage(Renderbuffer, DepthComponent24, 64, 64);

        _genFramebuffers(1, &_framebuffer);
        _bindFramebuffer(Framebuffer, _framebuffer);
        _framebufferTexture(
            Framebuffer, ColorAttachment0, GL_TEXTURE_2D, _color, 0);
        _framebufferRenderbuffer(
            Framebuffer, DepthAttachment, Renderbuffer, _depth);
        return _checkFramebuffer(Framebuffer) == FramebufferComplete
            ? FramebufferCreationResult::Success
            : FramebufferCreationResult::Incomplete;
    }

    GLuint Id() const
    {
        return _framebuffer;
    }

    ~OffscreenFramebuffer()
    {
        if (_deleteFramebuffers != nullptr && _framebuffer != 0)
        {
            _deleteFramebuffers(1, &_framebuffer);
        }
        if (_deleteRenderbuffers != nullptr && _depth != 0)
        {
            _deleteRenderbuffers(1, &_depth);
        }
        if (_color != 0)
        {
            glDeleteTextures(1, &_color);
        }
    }

private:
    GlGenFramebuffers _genFramebuffers = nullptr;
    GlBindFramebuffer _bindFramebuffer = nullptr;
    GlFramebufferTexture2D _framebufferTexture = nullptr;
    GlCheckFramebufferStatus _checkFramebuffer = nullptr;
    GlDeleteFramebuffers _deleteFramebuffers = nullptr;
    GlGenRenderbuffers _genRenderbuffers = nullptr;
    GlBindRenderbuffer _bindRenderbuffer = nullptr;
    GlRenderbufferStorage _renderbufferStorage = nullptr;
    GlFramebufferRenderbuffer _framebufferRenderbuffer = nullptr;
    GlDeleteRenderbuffers _deleteRenderbuffers = nullptr;
    GLuint _framebuffer = 0;
    GLuint _color = 0;
    GLuint _depth = 0;
};

class ScopedStageFile
{
public:
    ScopedStageFile() : _path("storm-wgl-shared-stage.usda")
    {
        std::remove(_path.c_str());
    }

    ~ScopedStageFile()
    {
        std::remove(_path.c_str());
    }

    const char* Path() const
    {
        return _path.c_str();
    }

private:
    std::string _path;
};

bool AuthorMesh(openusd_stage* stage, openusd_error_buffer* error)
{
    const std::array<openusd_vec3f, 3> points{
        openusd_vec3f{-1.0F, -1.0F, 0.0F},
        openusd_vec3f{1.0F, -1.0F, 0.0F},
        openusd_vec3f{0.0F, 1.0F, 0.0F}};
    const std::array<int32_t, 1> counts{3};
    const std::array<int32_t, 3> indices{0, 1, 2};
    return openusd_geom_define_mesh(stage, MeshPath, error) == OPENUSD_STATUS_OK &&
        openusd_geom_mesh_set_points(
            stage, MeshPath, points.data(), points.size(), 0, 0.0, error) ==
            OPENUSD_STATUS_OK &&
        openusd_geom_mesh_set_topology(
            stage,
            MeshPath,
            counts.data(),
            counts.size(),
            indices.data(),
            indices.size(),
            error) == OPENUSD_STATUS_OK &&
        openusd_geom_mesh_set_subdivision_scheme(stage, MeshPath, 0, error) ==
            OPENUSD_STATUS_OK &&
        openusd_geom_mesh_set_double_sided(stage, MeshPath, 1, error) ==
            OPENUSD_STATUS_OK;
}

std::vector<uint8_t> Render(
    openusd_storm_renderer* renderer,
    GLuint framebuffer,
    openusd_error_buffer* error,
    openusd_status* status,
    const openusd_render_camera* requestedCamera = nullptr)
{
    const openusd_render_camera automatic = AutomaticCamera();
    const openusd_render_camera* camera =
        requestedCamera == nullptr ? &automatic : requestedCamera;
    constexpr int32_t width = 64;
    constexpr int32_t height = 64;
    int32_t converged = 0;
    for (int iteration = 0; iteration < 32; ++iteration)
    {
        *status = openusd_storm_render(
            renderer,
            width,
            height,
            framebuffer,
            0.0,
            camera,
            &converged,
            error);
        if (*status != OPENUSD_STATUS_OK || converged != 0)
        {
            break;
        }
    }
    std::vector<uint8_t> pixels(
        static_cast<size_t>(width) * static_cast<size_t>(height) * 4);
    if (*status == OPENUSD_STATUS_OK)
    {
        glFinish();
        glReadBuffer(ColorAttachment0);
        glReadPixels(0, 0, width, height, GL_RGBA, GL_UNSIGNED_BYTE, pixels.data());
    }
    return pixels;
}

uint64_t PixelHash(const std::vector<uint8_t>& pixels) noexcept
{
    uint64_t hash = 14695981039346656037ull;
    for (uint8_t value : pixels)
    {
        hash ^= value;
        hash *= 1099511628211ull;
    }
    return hash;
}

bool FindColorDifference(
    const std::vector<uint8_t>& baseline,
    const std::vector<uint8_t>& selected,
    std::array<uint8_t, 4>& before,
    std::array<uint8_t, 4>& after) noexcept
{
    if (baseline.size() != selected.size())
    {
        return false;
    }
    for (size_t offset = 0; offset + 3 < baseline.size(); offset += 4)
    {
        if (!std::equal(
                baseline.begin() + static_cast<std::ptrdiff_t>(offset),
                baseline.begin() + static_cast<std::ptrdiff_t>(offset + 4),
                selected.begin() + static_cast<std::ptrdiff_t>(offset)))
        {
            std::copy_n(
                baseline.begin() + static_cast<std::ptrdiff_t>(offset),
                4,
                before.begin());
            std::copy_n(
                selected.begin() + static_cast<std::ptrdiff_t>(offset),
                4,
                after.begin());
            return true;
        }
    }
    return false;
}

bool RejectsCamera(
    openusd_storm_renderer* renderer,
    GLuint framebuffer,
    const openusd_render_camera* camera,
    openusd_error_buffer* error)
{
    int32_t converged = 1;
    return openusd_storm_render(
               renderer,
               64,
               64,
               framebuffer,
               0.0,
               camera,
               &converged,
               error) == OPENUSD_STATUS_INVALID_ARGUMENT &&
        converged == 0;
}

bool VerifyCameraAbi(
    openusd_storm_renderer* renderer,
    GLuint framebuffer,
    openusd_error_buffer* error)
{
    openusd_status status = OPENUSD_STATUS_OK;
    const openusd_render_camera automatic = AutomaticCamera();
    const openusd_render_camera legacy = LegacyMatricesCamera();
    const std::vector<uint8_t> automaticPixels =
        Render(renderer, framebuffer, error, &status, &automatic);
    if (status != OPENUSD_STATUS_OK)
    {
        return false;
    }
    openusd_render_camera applied{};
    applied.struct_size = sizeof(applied);
    if (openusd_hydra_test_get_applied_camera(renderer, &applied) != 1 ||
        std::memcmp(applied.view, legacy.view, sizeof(applied.view)) != 0 ||
        std::memcmp(
            applied.projection,
            legacy.projection,
            sizeof(applied.projection)) != 0)
    {
        return false;
    }
    const std::vector<uint8_t> legacyPixels =
        Render(renderer, framebuffer, error, &status, &legacy);
    if (status != OPENUSD_STATUS_OK || automaticPixels != legacyPixels)
    {
        return false;
    }

    const openusd_render_camera explicitCamera = ExplicitCamera();
    static_cast<void>(
        Render(renderer, framebuffer, error, &status, &explicitCamera));
    applied.struct_size = sizeof(applied);
    if (status != OPENUSD_STATUS_OK ||
        openusd_hydra_test_get_applied_camera(renderer, &applied) != 1 ||
        std::memcmp(
            applied.view,
            explicitCamera.view,
            sizeof(applied.view)) != 0 ||
        std::memcmp(
            applied.projection,
            explicitCamera.projection,
            sizeof(applied.projection)) != 0)
    {
        return false;
    }

    openusd_render_camera invalid = explicitCamera;
    invalid.struct_size = sizeof(invalid) - 1;
    if (!RejectsCamera(renderer, framebuffer, &invalid, error))
    {
        return false;
    }
    invalid = explicitCamera;
    invalid.mode = static_cast<openusd_render_camera_mode>(99);
    if (!RejectsCamera(renderer, framebuffer, &invalid, error))
    {
        return false;
    }
    invalid = explicitCamera;
    invalid.view[5] = std::nan("");
    if (!RejectsCamera(renderer, framebuffer, &invalid, error))
    {
        return false;
    }
    invalid = explicitCamera;
    invalid.projection[10] = std::numeric_limits<double>::infinity();
    return RejectsCamera(renderer, framebuffer, &invalid, error) &&
        RejectsCamera(renderer, framebuffer, nullptr, error);
}

bool GetRendererName(
    openusd_storm_renderer* renderer,
    std::string* name,
    openusd_error_buffer* error)
{
    size_t required = 0;
    if (openusd_storm_get_renderer_name(
            renderer, nullptr, 0, &required, error) !=
        OPENUSD_STATUS_BUFFER_TOO_SMALL)
    {
        return false;
    }
    std::vector<char> buffer(required);
    if (openusd_storm_get_renderer_name(
            renderer, buffer.data(), buffer.size(), &required, error) !=
        OPENUSD_STATUS_OK)
    {
        return false;
    }
    *name = buffer.data();
    return true;
}

openusd_render_pick_request PickRequest(
    const openusd_render_camera& camera,
    int32_t x,
    int32_t y,
    uint64_t revision = 0)
{
    openusd_render_pick_request request{};
    request.struct_size = sizeof(request);
    request.version = OPENUSD_RENDER_PICK_REQUEST_VERSION;
    request.x = x;
    request.y = y;
    request.width = 1;
    request.height = 1;
    request.viewport_width = 64;
    request.viewport_height = 64;
    request.target = OPENUSD_RENDER_PICK_TARGET_PRIMITIVE;
    request.resolve_mode = OPENUSD_RENDER_PICK_RESOLVE_NEAREST_TO_CENTER;
    request.time_code = 0;
    request.state_revision = revision;
    request.camera = camera;
    return request;
}

openusd_status Pick(
    openusd_storm_renderer* renderer,
    const openusd_render_pick_request& request,
    openusd_render_pick_result* result,
    char* prim_path,
    uint32_t prim_path_capacity,
    openusd_error_buffer* error)
{
    result->struct_size = sizeof(*result);
    result->version = OPENUSD_RENDER_PICK_RESULT_VERSION;
    std::array<char, 256> instancer_path{};
    std::array<openusd_render_pick_instance_context, 8> context{};
    std::array<char, 512> context_paths{};
    return openusd_storm_pick(
        renderer,
        &request,
        result,
        prim_path,
        prim_path_capacity,
        instancer_path.data(),
        static_cast<uint32_t>(instancer_path.size()),
        context.data(),
        static_cast<uint32_t>(context.size()),
        context_paths.data(),
        static_cast<uint32_t>(context_paths.size()),
        error);
}

bool PickIsStaleWithFlag(
    openusd_storm_renderer* renderer,
    const openusd_render_pick_request& request,
    uint32_t expected_flag,
    openusd_error_buffer* error)
{
    openusd_render_pick_result result{};
    std::array<char, 256> path{};
    return Pick(
               renderer,
               request,
               &result,
               path.data(),
               static_cast<uint32_t>(path.size()),
               error) == OPENUSD_STATUS_OK &&
        result.status == OPENUSD_RENDER_PICK_STATUS_STALE &&
        (result.flags & expected_flag) != 0;
}
}

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        std::cerr << "Usage: storm_wgl_shared_stage_probe <plugin-path> <stage-path>\n";
        return 2;
    }

    WglContext context;
    if (!context.Create())
    {
        std::cerr << "Failed to create the hidden WGL context.\n";
        return 3;
    }
    OffscreenFramebuffer framebuffer;
    const FramebufferCreationResult framebuffer_result = framebuffer.Create();
    if (framebuffer_result == FramebufferCreationResult::Unsupported)
    {
        std::cerr <<
            "Skipping Storm WGL shared-stage probe: framebuffer support is unavailable.\n";
        return CapabilityUnavailableExitCode;
    }
    if (framebuffer_result != FramebufferCreationResult::Success)
    {
        std::cerr << "Failed to create the offscreen OpenGL framebuffer.\n";
        return 4;
    }

    std::array<char, 4096> errorText{};
    openusd_error_buffer error{errorText.data(), errorText.size(), 0};
    ScopedStageFile stageFile;
    const size_t initialStageCoreCount = openusd_test_get_live_stage_core_count();
    const size_t initialAbandonedEngineCount =
        openusd_hydra_test_get_abandoned_engine_count();
    openusd_stage* stage = nullptr;
    if (openusd_stage_create_new(stageFile.Path(), &stage, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_retain(stage, &error) != OPENUSD_STATUS_OK ||
        openusd_stage_set_edit_target_session_layer(stage, &error) != OPENUSD_STATUS_OK ||
        !AuthorMesh(stage, &error))
    {
        openusd_stage_release(stage);
        std::cerr << "Stage setup failed: " << errorText.data() << "\n";
        return 5;
    }

    const size_t retainedStageCoreCount = openusd_test_get_live_stage_core_count();
    _putenv_s("OPENUSD_RENDERER_CREATE_FAILPOINT", "after-retain");
    openusd_storm_renderer* failedRenderer =
        reinterpret_cast<openusd_storm_renderer*>(1);
    const openusd_status failedCreateStatus =
        openusd_storm_create_from_stage(argv[1], stage, &failedRenderer, &error);
    _putenv_s("OPENUSD_RENDERER_CREATE_FAILPOINT", "");
    if (failedCreateStatus == OPENUSD_STATUS_OK ||
        failedRenderer != nullptr ||
        openusd_test_get_live_stage_core_count() != retainedStageCoreCount)
    {
        openusd_stage_release(stage);
        openusd_stage_release(stage);
        std::cerr << "Storm after-retain failpoint did not roll back cleanly.\n";
        return 6;
    }

    openusd_storm_renderer* renderer = nullptr;
    if (openusd_storm_create_from_stage(argv[1], stage, &renderer, &error) !=
        OPENUSD_STATUS_OK)
    {
        openusd_stage_release(stage);
        openusd_stage_release(stage);
        std::cerr << "Storm creation failed: " << errorText.data() << "\n";
        return 6;
    }
    openusd_stage_release(stage);
    const openusd_render_camera automatic = AutomaticCamera();

    _putenv_s("OPENUSD_DOTNET_STAGE_ACCESS_BEGIN_FAILPOINT", "after-retain");
    const openusd_status failedDestroyStatus = openusd_storm_destroy(renderer, &error);
    _putenv_s("OPENUSD_DOTNET_STAGE_ACCESS_BEGIN_FAILPOINT", "");
    if (failedDestroyStatus == OPENUSD_STATUS_OK)
    {
        openusd_storm_release(renderer);
        openusd_stage_release(stage);
        std::cerr << "Storm destroy ignored the stage-access failpoint.\n";
        return 7;
    }
    if (!VerifyCameraAbi(renderer, framebuffer.Id(), &error))
    {
        openusd_storm_release(renderer);
        openusd_stage_release(stage);
        std::cerr << "Storm camera ABI validation failed: "
                  << errorText.data() << "\n";
        return 7;
    }
    openusd_status status = OPENUSD_STATUS_OK;
    const std::vector<uint8_t> selection_baseline =
        Render(renderer, framebuffer.Id(), &error, &status, &automatic);
    if (status != OPENUSD_STATUS_OK ||
        openusd_storm_get_abi_version() != OPENUSD_STORM_ABI_VERSION)
    {
        std::cerr << "Storm ABI or pick pre-render validation failed.\n";
        return 7;
    }
    const openusd_render_pick_request center_request =
        PickRequest(automatic, 32, 32);
    openusd_render_pick_result center_result{};
    std::array<char, 256> center_path{};
    if (Pick(
            renderer,
            center_request,
            &center_result,
            center_path.data(),
            static_cast<uint32_t>(center_path.size()),
            &error) != OPENUSD_STATUS_OK ||
        center_result.status != OPENUSD_RENDER_PICK_STATUS_HIT ||
        center_path[0] != '/' ||
        center_result.normalized_depth < 0 ||
        center_result.normalized_depth > 1)
    {
        std::cerr << "Storm center-pixel pick failed: " << errorText.data() << "\n";
        return 7;
    }
    std::cerr << "Center pick path: " << center_path.data() << "\n";
    const std::string center_identity = center_path.data();
    openusd_render_pick_result sized_result{};
    std::array<char, 1> undersized_path{};
    if (Pick(
            renderer,
            center_request,
            &sized_result,
            undersized_path.data(),
            static_cast<uint32_t>(undersized_path.size()),
            &error) != OPENUSD_STATUS_BUFFER_TOO_SMALL ||
        sized_result.status != OPENUSD_RENDER_PICK_STATUS_HIT ||
        sized_result.prim_path_required <= undersized_path.size())
    {
        std::cerr << "Storm pick buffer sizing contract failed.\n";
        return 7;
    }
    openusd_render_pick_request stale_request = center_request;
    stale_request.state_revision = 1;
    openusd_render_pick_result stale_result{};
    if (Pick(
            renderer,
            stale_request,
            &stale_result,
            center_path.data(),
            static_cast<uint32_t>(center_path.size()),
            &error) != OPENUSD_STATUS_OK ||
            stale_result.status != OPENUSD_RENDER_PICK_STATUS_STALE ||
            (stale_result.flags &
             OPENUSD_RENDER_PICK_RESULT_STALE_STATE_REVISION) == 0)
    {
            std::cerr << "Storm stale pick binding failed.\n";
            return 7;
    }
    openusd_render_pick_request camera_stale_request = center_request;
    camera_stale_request.camera = LegacyMatricesCamera();
    openusd_render_pick_request equivalent_auto_request = center_request;
    equivalent_auto_request.camera.view[3] = 123.0;
    equivalent_auto_request.camera.projection[7] = -456.0;
    openusd_render_pick_result equivalent_auto_result{};
    if (Pick(
            renderer,
            equivalent_auto_request,
            &equivalent_auto_result,
            center_path.data(),
            static_cast<uint32_t>(center_path.size()),
            &error) != OPENUSD_STATUS_OK ||
        equivalent_auto_result.status != OPENUSD_RENDER_PICK_STATUS_HIT ||
        center_identity != center_path.data())
    {
        std::cerr << "Storm automatic camera ignored-matrix binding failed.\n";
        return 7;
    }
    openusd_render_pick_request viewport_stale_request = center_request;
    viewport_stale_request.viewport_width = 63;
    openusd_render_pick_request time_stale_request = center_request;
    time_stale_request.time_code = 1;
    openusd_render_pick_request context_stale_request = center_request;
    context_stale_request.context_generation = 1;
    openusd_render_pick_request scene_stale_request = center_request;
    scene_stale_request.flags |=
            OPENUSD_RENDER_PICK_REQUEST_HAS_SCENE_REVISION;
    scene_stale_request.scene_revision = 1;
    if (!PickIsStaleWithFlag(
                renderer,
                camera_stale_request,
                OPENUSD_RENDER_PICK_RESULT_STALE_CAMERA,
                &error) ||
            !PickIsStaleWithFlag(
                renderer,
                viewport_stale_request,
                OPENUSD_RENDER_PICK_RESULT_STALE_VIEWPORT,
                &error) ||
            !PickIsStaleWithFlag(
                renderer,
                time_stale_request,
                OPENUSD_RENDER_PICK_RESULT_STALE_TIME,
                &error) ||
            !PickIsStaleWithFlag(
                renderer,
                context_stale_request,
                OPENUSD_RENDER_PICK_RESULT_STALE_CONTEXT_GENERATION,
                &error) ||
            !PickIsStaleWithFlag(
                renderer,
                scene_stale_request,
                OPENUSD_RENDER_PICK_RESULT_STALE_SCENE_REVISION,
                &error))
    {
            std::cerr << "Storm stale-reason classification failed.\n";
            return 7;
    }
    openusd_render_pick_request invalid_request = center_request;
    invalid_request.width = 2;
    openusd_render_pick_result invalid_result{};
    if (Pick(
            renderer,
            invalid_request,
            &invalid_result,
            center_path.data(),
            static_cast<uint32_t>(center_path.size()),
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        invalid_result.status != OPENUSD_RENDER_PICK_STATUS_INVALID)
    {
        std::cerr << "Storm invalid pick request was accepted.\n";
        return 7;
    }
    const openusd_render_pick_request empty_request =
        PickRequest(automatic, 0, 0);
    openusd_render_pick_result empty_result{};
    if (Pick(
            renderer,
            empty_request,
            &empty_result,
            center_path.data(),
            static_cast<uint32_t>(center_path.size()),
            &error) != OPENUSD_STATUS_OK ||
        empty_result.status != OPENUSD_RENDER_PICK_STATUS_MISS)
    {
        std::cerr << "Storm empty-pixel pick did not report a miss.\n";
        return 7;
    }
    _putenv_s("OPENUSD_DOTNET_STAGE_ACCESS_BEGIN_FAILPOINT", "after-retain");
    openusd_render_pick_result locked_result{};
    const openusd_status locked_pick_status = Pick(
        renderer,
        center_request,
        &locked_result,
        center_path.data(),
        static_cast<uint32_t>(center_path.size()),
        &error);
    _putenv_s("OPENUSD_DOTNET_STAGE_ACCESS_BEGIN_FAILPOINT", "");
    if (locked_pick_status == OPENUSD_STATUS_OK ||
        locked_result.status != OPENUSD_RENDER_PICK_STATUS_ERROR)
    {
        std::cerr << "Storm pick ignored the stage-access failpoint.\n";
        return 7;
    }
    const std::string selected_path = center_identity;
    const openusd_storm_selection_item selection_item{
        0,
        static_cast<uint32_t>(selected_path.size()),
        -1,
        0};
    openusd_storm_selection_update selection{};
    selection.struct_size = sizeof(selection);
    selection.version = OPENUSD_STORM_SELECTION_UPDATE_VERSION;
    selection.item_count = 1;
    selection.color[0] = 1;
    selection.color[1] = 1;
    selection.color[3] = 1;
    selection.items = &selection_item;
    selection.path_bytes = selected_path.data();
    selection.path_bytes_size =
        static_cast<uint32_t>(selected_path.size());
    if (openusd_storm_set_selection(renderer, &selection, &error) !=
        OPENUSD_STATUS_OK)
    {
        std::cerr << "Storm packed selection update failed: "
                  << errorText.data() << "\n";
        return 7;
    }
    const std::vector<uint8_t> selected_pixels =
        Render(renderer, framebuffer.Id(), &error, &status, &automatic);
    std::array<uint8_t, 4> baseline_color{};
    std::array<uint8_t, 4> selected_color{};
    const uint64_t baseline_hash = PixelHash(selection_baseline);
    const uint64_t selected_hash = PixelHash(selected_pixels);
    if (status != OPENUSD_STATUS_OK ||
        baseline_hash == selected_hash ||
        !FindColorDifference(
            selection_baseline,
            selected_pixels,
            baseline_color,
            selected_color))
    {
        std::cerr << "Storm selection did not change framebuffer hash and color.\n";
        return 7;
    }
    std::cerr << "Selection evidence: baselineHash=" << baseline_hash
              << " selectedHash=" << selected_hash
              << " baselineRGBA="
              << static_cast<uint32_t>(baseline_color[0]) << ","
              << static_cast<uint32_t>(baseline_color[1]) << ","
              << static_cast<uint32_t>(baseline_color[2]) << ","
              << static_cast<uint32_t>(baseline_color[3])
              << " selectedRGBA="
              << static_cast<uint32_t>(selected_color[0]) << ","
              << static_cast<uint32_t>(selected_color[1]) << ","
              << static_cast<uint32_t>(selected_color[2]) << ","
              << static_cast<uint32_t>(selected_color[3]) << "\n";
    selection.item_count = 0;
    selection.items = nullptr;
    selection.path_bytes = nullptr;
    selection.path_bytes_size = 0;
    if (openusd_storm_set_selection(renderer, &selection, &error) !=
        OPENUSD_STATUS_OK)
    {
        std::cerr << "Storm selection clear failed.\n";
        return 7;
    }
    const std::vector<uint8_t> cleared_pixels =
        Render(renderer, framebuffer.Id(), &error, &status, &automatic);
    if (status != OPENUSD_STATUS_OK ||
        PixelHash(cleared_pixels) != baseline_hash ||
        cleared_pixels != selection_baseline)
    {
        std::cerr << "ClearSelected did not restore the baseline framebuffer.\n";
        return 7;
    }
    std::cerr << "Selection clear evidence: baselineHash=" << baseline_hash
              << " clearedHash=" << PixelHash(cleared_pixels)
              << " restored=1\n";

    openusd_status wrongThreadRenderStatus = OPENUSD_STATUS_OK;
    openusd_status wrongThreadDestroyStatus = OPENUSD_STATUS_OK;
    openusd_status wrongThreadPickStatus = OPENUSD_STATUS_OK;
    bool wrongThreadNameSucceeded = false;
    std::thread wrongThread(
        [&]
        {
            std::array<char, 256> threadErrorText{};
            openusd_error_buffer threadError{
                threadErrorText.data(), threadErrorText.size(), 0};
            int32_t converged = 1;
            wrongThreadRenderStatus = openusd_storm_render(
                renderer,
                64,
                64,
                framebuffer.Id(),
                0.0,
                &automatic,
                &converged,
                &threadError);
            openusd_render_pick_result thread_pick_result{};
            std::array<char, 64> thread_pick_path{};
            wrongThreadPickStatus = Pick(
                renderer,
                center_request,
                &thread_pick_result,
                thread_pick_path.data(),
                static_cast<uint32_t>(thread_pick_path.size()),
                &threadError);
            std::string name;
            wrongThreadNameSucceeded =
                GetRendererName(renderer, &name, &threadError) && !name.empty();
            wrongThreadDestroyStatus =
                openusd_storm_destroy(renderer, &threadError);
        });
    wrongThread.join();
    if (wrongThreadRenderStatus != OPENUSD_STATUS_WRONG_THREAD ||
        wrongThreadPickStatus != OPENUSD_STATUS_WRONG_THREAD ||
        wrongThreadDestroyStatus != OPENUSD_STATUS_WRONG_THREAD ||
        !wrongThreadNameSucceeded)
    {
        openusd_storm_release(renderer);
        openusd_stage_release(stage);
        std::cerr << "Storm thread-affinity or cached-name validation failed.\n";
        return 7;
    }

    WglContext secondContext;
    if (!secondContext.Create())
    {
        openusd_storm_release(renderer);
        openusd_stage_release(stage);
        std::cerr << "Failed to create the second WGL context.\n";
        return 8;
    }
    int32_t converged = 0;
    openusd_render_pick_result wrong_context_pick{};
    std::array<char, 64> wrong_context_path{};
    if (openusd_storm_render(
            renderer,
            64,
            64,
            framebuffer.Id(),
            0.0,
            &automatic,
            &converged,
            &error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT ||
        Pick(
            renderer,
            center_request,
            &wrong_context_pick,
            wrong_context_path.data(),
            static_cast<uint32_t>(wrong_context_path.size()),
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_storm_destroy(renderer, &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        !context.MakeCurrent())
    {
        std::cerr << "Storm accepted a different WGL context.\n";
        return 9;
    }

    if (!secondContext.MakeCurrent())
    {
        std::cerr << "Could not restore the second WGL context.\n";
        return 10;
    }
    {
        OffscreenFramebuffer secondFramebuffer;
        openusd_storm_renderer* secondRenderer = nullptr;
        if (secondFramebuffer.Create() != FramebufferCreationResult::Success ||
            openusd_storm_create_from_stage(
                argv[1], stage, &secondRenderer, &error) != OPENUSD_STATUS_OK ||
            openusd_storm_render(
                secondRenderer,
                64,
                64,
                secondFramebuffer.Id(),
                0.0,
                &automatic,
                &converged,
                &error) != OPENUSD_STATUS_OK ||
            openusd_storm_destroy(secondRenderer, &error) != OPENUSD_STATUS_OK)
        {
            openusd_storm_release(secondRenderer);
            std::cerr << "Two-context Storm session validation failed: "
                      << errorText.data() << "\n";
            return 11;
        }
    }
    if (!context.MakeCurrent())
    {
        std::cerr << "Could not restore the original WGL context.\n";
        return 12;
    }

    WglContext::ClearCurrent();
    openusd_render_pick_result no_context_pick{};
    if (openusd_storm_render(
            renderer,
            64,
            64,
            framebuffer.Id(),
            0.0,
            &automatic,
            &converged,
            &error) !=
            OPENUSD_STATUS_INVALID_ARGUMENT ||
        Pick(
            renderer,
            center_request,
            &no_context_pick,
            wrong_context_path.data(),
            static_cast<uint32_t>(wrong_context_path.size()),
            &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        openusd_storm_destroy(renderer, &error) != OPENUSD_STATUS_INVALID_ARGUMENT ||
        !context.MakeCurrent())
    {
        std::cerr << "Storm accepted operation without its WGL context.\n";
        return 13;
    }

    const size_t beforeDetachCoreCount =
        openusd_test_get_live_stage_core_count();
    openusd_stage* detachStage = nullptr;
    openusd_storm_renderer* detached = nullptr;
    if (openusd_stage_open(argv[2], &detachStage, &error) != OPENUSD_STATUS_OK ||
        openusd_storm_create_from_stage(
            argv[1], detachStage, &detached, &error) != OPENUSD_STATUS_OK)
    {
        openusd_stage_release(detachStage);
        std::cerr << "Storm detach-session creation failed.\n";
        return 11;
    }
    openusd_stage_release(detachStage);
    WglContext::ClearCurrent();
    if (openusd_test_get_live_stage_core_count() != beforeDetachCoreCount + 1 ||
        openusd_storm_abandon(detached, &error) != OPENUSD_STATUS_OK ||
        openusd_test_get_live_stage_core_count() != beforeDetachCoreCount ||
        openusd_hydra_test_get_abandoned_engine_count() !=
            initialAbandonedEngineCount + 1 ||
        !context.MakeCurrent())
    {
        std::cerr << "Storm context-loss detach ownership failed: "
                  << errorText.data() << "\n";
        return 12;
    }

    const std::vector<uint8_t> authored =
        Render(renderer, framebuffer.Id(), &error, &status);
    if (status != OPENUSD_STATUS_OK)
    {
        openusd_storm_release(renderer);
        openusd_stage_release(stage);
        std::cerr << "Storm render failed: " << errorText.data() << "\n";
        return 13;
    }

    if (openusd_stage_remove_prim(stage, MeshPath, &error) != OPENUSD_STATUS_OK)
    {
        openusd_storm_release(renderer);
        openusd_stage_release(stage);
        std::cerr << "Stage removal failed: " << errorText.data() << "\n";
        return 14;
    }
    openusd_stage_release(stage);

    const std::vector<uint8_t> removed =
        Render(renderer, framebuffer.Id(), &error, &status);
    if (openusd_storm_destroy(renderer, &error) != OPENUSD_STATUS_OK)
    {
        std::cerr << "Checked Storm destruction failed: " << errorText.data() << "\n";
        return 15;
    }
    if (status != OPENUSD_STATUS_OK)
    {
        std::cerr << "Storm redraw failed: " << errorText.data() << "\n";
        return 16;
    }
    if (authored.size() != removed.size() || authored.empty())
    {
        std::cerr << "Storm did not produce both shared-stage frame buffers.\n";
        return 17;
    }

    openusd_storm_renderer* pathRenderer = nullptr;
    if (openusd_storm_create(argv[1], argv[2], &pathRenderer, &error) !=
            OPENUSD_STATUS_OK ||
        openusd_storm_destroy(pathRenderer, &error) != OPENUSD_STATUS_OK)
    {
        std::cerr << "Storm path compatibility creation failed: " << errorText.data() << "\n";
        return 18;
    }
    if (openusd_test_get_live_stage_core_count() != initialStageCoreCount ||
        openusd_hydra_test_get_abandoned_engine_count() !=
            initialAbandonedEngineCount + 1)
    {
        std::cerr << "Storm leaked stage ownership or destroyed/leaked an unexpected engine.\n";
        return 19;
    }

    std::cout << "OK: WGL affinity, detach ownership, checked destroy, and shared stage\n";
    return 0;
}
