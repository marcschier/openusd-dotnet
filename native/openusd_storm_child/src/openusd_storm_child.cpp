// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_storm_child.h"
#include "openusd_storm_child_navigation.h"
#include "openusd_hydra.h"
#include "openusd_render_camera_internal.h"
#include "openusd_storm_child_internal.h"
#include "openusd_storm_child_pick.h"

#define NOMINMAX
#include <Windows.h>
#include <gl/GL.h>

#include <algorithm>
#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <deque>
#include <exception>
#include <memory>
#include <mutex>
#include <new>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

namespace
{
constexpr wchar_t WindowClassName[] = L"OpenUsdStormNativeChild";
constexpr int WglContextMajorVersion = 0x2091;
constexpr int WglContextMinorVersion = 0x2092;
constexpr int WglContextProfileMask = 0x9126;
constexpr int WglContextCompatibilityProfileBit = 0x00000002;
constexpr GLenum GlMajorVersion = 0x821B;
constexpr GLenum GlMinorVersion = 0x821C;
constexpr GLenum GlContextProfileMask = 0x9126;
constexpr GLenum GlDrawFramebuffer = 0x8CA9;
constexpr GLenum GlReadFramebuffer = 0x8CA8;
constexpr GLenum GlDrawFramebufferBinding = 0x8CA6;
constexpr GLenum GlReadFramebufferBinding = 0x8CAA;
constexpr GLenum GlFramebuffer = 0x8D40;
constexpr GLenum GlRenderbuffer = 0x8D41;
constexpr GLenum GlRenderbufferBinding = 0x8CA7;
constexpr GLenum GlColorAttachment0 = 0x8CE0;
constexpr GLenum GlDepthAttachment = 0x8D00;
constexpr GLenum GlFramebufferComplete = 0x8CD5;
constexpr GLenum GlDepthComponent24 = 0x81A6;
constexpr GLenum GlTextureBinding2d = 0x8069;
constexpr GLint GlRgba8 = 0x8058;
constexpr size_t MaximumCaptureBytes = 64u * 1024u * 1024u;

using WglCreateContextAttribs =
    HGLRC(WINAPI*)(HDC, HGLRC, const int*);
using GlBindFramebuffer = void(APIENTRY*)(GLenum, GLuint);
using GlGenFramebuffers = void(APIENTRY*)(GLsizei, GLuint*);
using GlFramebufferTexture2D =
    void(APIENTRY*)(GLenum, GLenum, GLenum, GLuint, GLint);
using GlCheckFramebufferStatus = GLenum(APIENTRY*)(GLenum);
using GlDeleteFramebuffers = void(APIENTRY*)(GLsizei, const GLuint*);
using GlGenRenderbuffers = void(APIENTRY*)(GLsizei, GLuint*);
using GlBindRenderbuffer = void(APIENTRY*)(GLenum, GLuint);
using GlRenderbufferStorage =
    void(APIENTRY*)(GLenum, GLenum, GLsizei, GLsizei);
using GlFramebufferRenderbuffer =
    void(APIENTRY*)(GLenum, GLenum, GLenum, GLuint);
using GlDeleteRenderbuffers = void(APIENTRY*)(GLsizei, const GLuint*);
using GlBlitFramebuffer = void(APIENTRY*)(
    GLint,
    GLint,
    GLint,
    GLint,
    GLint,
    GLint,
    GLint,
    GLint,
    GLbitfield,
    GLenum);

enum class CommandKind
{
    Render,
    Capture,
    Pick,
    Selection,
    TransformOverrides,
    DeformationOverrides,
    ContextLoss,
    Stop
};

enum class LifecycleState
{
    Running,
    Closing,
    Stopped
};

struct Command
{
    CommandKind kind = CommandKind::Render;
    double time_code = 0;
    openusd_render_camera camera =
        openusd_render_camera_detail::Automatic();
    uint64_t revision = 0;
    uint64_t scene_revision = 0;
    uint32_t revision_flags = 0;
    bool wait = false;
    bool done = false;
    openusd_status status = OPENUSD_STATUS_OK;
    uint64_t frame_count = 0;
    int32_t converged = 0;
    uint32_t capture_background_rgba = 0;
    uint8_t capture_tolerance = 0;
    uint32_t capture_flags = 0;
    bool capture_pixels = false;
    openusd_storm_child_framebuffer_capture capture{};
    std::vector<uint8_t> captured_pixels;
    std::unique_ptr<OpenUsdStormChildPickPayload> pick;
    std::unique_ptr<OpenUsdStormChildSelectionPayload> selection;
    std::unique_ptr<OpenUsdStormChildTransformOverridePayload>
        transform_overrides;
    std::unique_ptr<OpenUsdStormChildDeformationOverridePayload>
        deformation_overrides;
    std::string error;
    std::condition_variable completion;
};

std::atomic_size_t g_live_count{0};
std::atomic_size_t g_peak_count{0};
std::atomic_uintptr_t g_next_token{0x10000};
std::once_flag g_window_class_once;
ATOM g_window_class = 0;
DWORD g_window_class_error = ERROR_SUCCESS;

void UpdatePeak(size_t value) noexcept
{
    size_t current = g_peak_count.load(std::memory_order_relaxed);
    while (current < value &&
           !g_peak_count.compare_exchange_weak(
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

std::string Win32Error(const char* operation)
{
    return std::string(operation) + " failed with Win32 error " +
        std::to_string(GetLastError()) + ".";
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
        WriteError(error, "Unknown native Storm child exception.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
}

template <typename T>
T LoadWgl(const char* name)
{
    const PROC address = wglGetProcAddress(name);
    if (!OpenUsdStormChildIsValidWglAddress(address))
    {
        return nullptr;
    }
    T result = nullptr;
    static_assert(sizeof(result) == sizeof(address));
    std::memcpy(&result, &address, sizeof(result));
    return result;
}

bool IsFailpoint(const char* value) noexcept
{
    char configured[64]{};
    const DWORD length = GetEnvironmentVariableA(
        "OPENUSD_STORM_CHILD_FAILPOINT",
        configured,
        static_cast<DWORD>(sizeof(configured)));
    return length > 0 &&
        length < sizeof(configured) &&
        std::strcmp(configured, value) == 0;
}
}

struct ChildState
{
    std::atomic<HWND> window{nullptr};
    HDC device = nullptr;
    HGLRC context = nullptr;
    openusd_stage* stage = nullptr;
    openusd_storm_renderer* renderer = nullptr;
    std::string plugin_path;
    std::string renderer_name;
    std::thread render_thread;
    std::mutex destroy_gate;
    std::mutex gate;
    std::condition_variable commands_available;
    std::condition_variable initialized;
    std::deque<std::shared_ptr<Command>> synchronous_commands;
    std::shared_ptr<Command> asynchronous_render;
    LifecycleState lifecycle = LifecycleState::Running;
    bool initialization_complete = false;
    openusd_status initialization_status = OPENUSD_STATUS_OK;
    std::string initialization_error;
    std::atomic_int32_t width{1};
    std::atomic_int32_t height{1};
    std::atomic_uint32_t dpi{96};
    std::atomic_int32_t visible{1};
    std::atomic_int32_t focused{0};
    std::atomic_uint64_t frame_count{0};
    std::atomic_uint64_t pixel_signature{0};
    std::atomic_uint64_t pixel_sample_count{0};
    std::atomic_uint64_t focus_count{0};
    std::atomic_uint64_t pointer_count{0};
    std::atomic_uint64_t wheel_count{0};
    std::atomic_uint64_t key_count{0};
    std::atomic_uint64_t context_generation{0};
    std::atomic_uint64_t coalesced_request_count{0};
    std::atomic_uint64_t cancelled_command_count{0};
    std::atomic_uint64_t teardown_fallback_count{0};
    std::atomic_uint64_t latest_requested_revision{0};
    std::atomic_uint64_t latest_requested_camera_signature{0};
    std::atomic_uint64_t latest_rendered_camera_signature{0};
    std::atomic<double> latest_time_code{0};
    std::atomic_uint32_t render_thread_id{0};
    uint32_t creator_thread_id = 0;
    std::atomic_uint32_t peak_pending_command_count{0};
    std::atomic_bool window_userdata_cleared{false};
    std::atomic_int32_t gl_major{0};
    std::atomic_int32_t gl_minor{0};
    std::atomic_int32_t compatibility_profile{0};
    std::atomic_int32_t converged{0};
    OpenUsdStormChildNavigationState navigation;
    GLuint render_framebuffer = 0;
    GLuint render_color_texture = 0;
    GLuint render_depth_buffer = 0;
    int32_t render_width = 0;
    int32_t render_height = 0;
    openusd_render_camera latest_camera =
        openusd_render_camera_detail::Automatic();
};

struct openusd_storm_child
{
};

namespace
{
std::mutex g_registry_gate;
std::unordered_map<openusd_storm_child*, std::shared_ptr<ChildState>> g_children;

std::shared_ptr<ChildState> LookupChild(openusd_storm_child* child)
{
    if (child == nullptr)
    {
        return {};
    }
    std::lock_guard lock(g_registry_gate);
    const auto found = g_children.find(child);
    return found == g_children.end() ? std::shared_ptr<ChildState>{} : found->second;
}

openusd_storm_child* RegisterChild(const std::shared_ptr<ChildState>& child)
{
    const uintptr_t value =
        g_next_token.fetch_add(16, std::memory_order_relaxed);
    auto* token = reinterpret_cast<openusd_storm_child*>(value);
    std::lock_guard lock(g_registry_gate);
    g_children.emplace(token, child);
    return token;
}

bool UnregisterChild(
    openusd_storm_child* child,
    const std::shared_ptr<ChildState>& state)
{
    std::lock_guard lock(g_registry_gate);
    const auto found = g_children.find(child);
    if (found == g_children.end() || found->second != state)
    {
        return false;
    }
    g_children.erase(found);
    return true;
}

int32_t MouseX(LPARAM value) noexcept
{
    return static_cast<int32_t>(
        static_cast<int16_t>(static_cast<uintptr_t>(value) & 0xffffu));
}

int32_t MouseY(LPARAM value) noexcept
{
    return static_cast<int32_t>(
        static_cast<int16_t>((static_cast<uintptr_t>(value) >> 16) & 0xffffu));
}

uint32_t MouseButtons(WPARAM value) noexcept
{
    uint32_t buttons = OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE;
    if ((value & MK_LBUTTON) != 0)
    {
        buttons |= OPENUSD_STORM_CHILD_POINTER_BUTTON_LEFT;
    }
    if ((value & MK_MBUTTON) != 0)
    {
        buttons |= OPENUSD_STORM_CHILD_POINTER_BUTTON_MIDDLE;
    }
    if ((value & MK_RBUTTON) != 0)
    {
        buttons |= OPENUSD_STORM_CHILD_POINTER_BUTTON_RIGHT;
    }
    return buttons;
}

uint32_t MouseModifiers(ChildState* child, WPARAM value)
{
    uint32_t modifiers =
        OpenUsdStormChildNavigationGetModifiers(&child->navigation) &
        (OPENUSD_STORM_CHILD_MODIFIER_ALT |
         OPENUSD_STORM_CHILD_MODIFIER_META);
    if ((value & MK_SHIFT) != 0 || GetKeyState(VK_SHIFT) < 0)
    {
        modifiers |= OPENUSD_STORM_CHILD_MODIFIER_SHIFT;
    }
    if ((value & MK_CONTROL) != 0 || GetKeyState(VK_CONTROL) < 0)
    {
        modifiers |= OPENUSD_STORM_CHILD_MODIFIER_CONTROL;
    }
    if (GetKeyState(VK_MENU) < 0)
    {
        modifiers |= OPENUSD_STORM_CHILD_MODIFIER_ALT;
    }
    if (GetKeyState(VK_LWIN) < 0 || GetKeyState(VK_RWIN) < 0)
    {
        modifiers |= OPENUSD_STORM_CHILD_MODIFIER_META;
    }
    return modifiers;
}

void TrackMouseLeave(HWND window)
{
    TRACKMOUSEEVENT tracking{};
    tracking.cbSize = sizeof(tracking);
    tracking.dwFlags = TME_LEAVE;
    tracking.hwndTrack = window;
    TrackMouseEvent(&tracking);
}

void UpdateNavigationKey(
    ChildState* child,
    WPARAM key,
    bool pressed,
    LPARAM lparam)
{
    std::lock_guard lock(child->navigation.gate);
    uint32_t modifier = OPENUSD_STORM_CHILD_MODIFIER_NONE;
    uint32_t command_key = 0;
    bool count_repeats = false;
    uint64_t OpenUsdStormChildNavigationState::* command_counter = nullptr;
    switch (key)
    {
        case VK_MENU:
        case VK_LMENU:
        case VK_RMENU:
            modifier = OPENUSD_STORM_CHILD_MODIFIER_ALT;
            break;
        case VK_SHIFT:
        case VK_LSHIFT:
        case VK_RSHIFT:
            modifier = OPENUSD_STORM_CHILD_MODIFIER_SHIFT;
            break;
        case VK_CONTROL:
        case VK_LCONTROL:
        case VK_RCONTROL:
            modifier = OPENUSD_STORM_CHILD_MODIFIER_CONTROL;
            break;
        case VK_LWIN:
        case VK_RWIN:
            modifier = OPENUSD_STORM_CHILD_MODIFIER_META;
            break;
        case 'F':
            command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_FRAME_SELECTED;
            command_counter =
                &OpenUsdStormChildNavigationState::frame_selected_press_count;
            break;
        case VK_HOME:
            command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_RESET_AUTOMATIC;
            command_counter =
                &OpenUsdStormChildNavigationState::reset_automatic_press_count;
            break;
        case 'P':
            command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_TOGGLE_PROJECTION;
            command_counter =
                &OpenUsdStormChildNavigationState::toggle_projection_press_count;
            break;
        case VK_LEFT:
            command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_ORBIT_LEFT;
            count_repeats = true;
            command_counter =
                &OpenUsdStormChildNavigationState::orbit_left_press_count;
            break;
        case VK_RIGHT:
            command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_ORBIT_RIGHT;
            count_repeats = true;
            command_counter =
                &OpenUsdStormChildNavigationState::orbit_right_press_count;
            break;
        case VK_UP:
            command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_ORBIT_UP;
            count_repeats = true;
            command_counter =
                &OpenUsdStormChildNavigationState::orbit_up_press_count;
            break;
        case VK_DOWN:
            command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_ORBIT_DOWN;
            count_repeats = true;
            command_counter =
                &OpenUsdStormChildNavigationState::orbit_down_press_count;
            break;
        default:
            break;
    }
    if (modifier != OPENUSD_STORM_CHILD_MODIFIER_NONE)
    {
        if (pressed)
        {
            child->navigation.modifiers |= modifier;
        }
        else
        {
            child->navigation.modifiers &= ~modifier;
        }
    }
    if (command_counter != nullptr)
    {
        const bool repeat =
            pressed &&
            (static_cast<uintptr_t>(lparam) & (uintptr_t{1} << 30)) != 0;
        OpenUsdStormChildNavigationUpdateCommandKeyLocked(
            &child->navigation,
            command_key,
            pressed,
            repeat,
            count_repeats,
            command_counter);
    }
    OpenUsdStormChildNavigationAdvance(&child->navigation);
}

LRESULT CALLBACK ChildWindowProcedure(
    HWND window,
    UINT message,
    WPARAM wparam,
    LPARAM lparam)
{
    auto* child = reinterpret_cast<ChildState*>(
        GetWindowLongPtrW(window, GWLP_USERDATA));
    if (message == WM_NCCREATE)
    {
        const auto* create = reinterpret_cast<const CREATESTRUCTW*>(lparam);
        child = static_cast<ChildState*>(create->lpCreateParams);
        SetWindowLongPtrW(
            window,
            GWLP_USERDATA,
            reinterpret_cast<LONG_PTR>(child));
    }
    if (child != nullptr)
    {
        switch (message)
        {
            case WM_SETFOCUS:
                child->focused.store(1, std::memory_order_relaxed);
                child->focus_count.fetch_add(1, std::memory_order_relaxed);
                OpenUsdStormChildNavigationSetFocus(&child->navigation, true);
                return 0;
            case WM_KILLFOCUS:
                child->focused.store(0, std::memory_order_relaxed);
                OpenUsdStormChildNavigationSetFocus(&child->navigation, false);
                return 0;
            case WM_MOUSEMOVE:
                child->pointer_count.fetch_add(1, std::memory_order_relaxed);
                TrackMouseLeave(window);
                OpenUsdStormChildNavigationUpdatePointer(
                    &child->navigation,
                    MouseX(lparam),
                    MouseY(lparam),
                    MouseButtons(wparam),
                    MouseModifiers(child, wparam));
                return 0;
            case WM_LBUTTONUP:
            case WM_MBUTTONUP:
            case WM_RBUTTONUP:
                child->pointer_count.fetch_add(1, std::memory_order_relaxed);
                OpenUsdStormChildNavigationUpdatePointer(
                    &child->navigation,
                    MouseX(lparam),
                    MouseY(lparam),
                    MouseButtons(wparam),
                    MouseModifiers(child, wparam));
                if (MouseButtons(wparam) ==
                        OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE &&
                    GetCapture() == window)
                {
                    ReleaseCapture();
                }
                return 0;
            case WM_LBUTTONDOWN:
            case WM_MBUTTONDOWN:
            case WM_RBUTTONDOWN:
                child->pointer_count.fetch_add(1, std::memory_order_relaxed);
                SetFocus(window);
                SetCapture(window);
                TrackMouseLeave(window);
                OpenUsdStormChildNavigationUpdatePointer(
                    &child->navigation,
                    MouseX(lparam),
                    MouseY(lparam),
                    MouseButtons(wparam),
                    MouseModifiers(child, wparam));
                return 0;
            case WM_MOUSEWHEEL:
            {
                child->wheel_count.fetch_add(1, std::memory_order_relaxed);
                POINT point{MouseX(lparam), MouseY(lparam)};
                ScreenToClient(window, &point);
                const int16_t raw_delta = static_cast<int16_t>(
                    (static_cast<uintptr_t>(wparam) >> 16) & 0xffffu);
                OpenUsdStormChildNavigationAddWheel(
                    &child->navigation,
                    static_cast<double>(raw_delta) / WHEEL_DELTA,
                    point.x,
                    point.y,
                    MouseButtons(LOWORD(wparam)),
                    MouseModifiers(child, LOWORD(wparam)));
                return 0;
            }
            case WM_KEYDOWN:
            case WM_SYSKEYDOWN:
            case WM_KEYUP:
            case WM_SYSKEYUP:
            case WM_CHAR:
            {
                child->key_count.fetch_add(1, std::memory_order_relaxed);
                if (message != WM_CHAR)
                {
                    UpdateNavigationKey(
                        child,
                        wparam,
                        message == WM_KEYDOWN || message == WM_SYSKEYDOWN,
                        lparam);
                }
                return 0;
            }
            case WM_MOUSELEAVE:
                OpenUsdStormChildNavigationSetInside(&child->navigation, false);
                return 0;
            case WM_CAPTURECHANGED:
                OpenUsdStormChildNavigationResetButtons(&child->navigation);
                return 0;
            case WM_ERASEBKGND:
                return 1;
            case WM_NCDESTROY:
                SetWindowLongPtrW(window, GWLP_USERDATA, 0);
                child->window_userdata_cleared.store(
                    true,
                    std::memory_order_release);
                break;
            default:
                break;
        }
    }
    return DefWindowProcW(window, message, wparam, lparam);
}

void RegisterChildWindowClass()
{
    WNDCLASSW window_class{};
    window_class.style = CS_OWNDC | CS_HREDRAW | CS_VREDRAW;
    window_class.lpfnWndProc = ChildWindowProcedure;
    window_class.hInstance = GetModuleHandleW(nullptr);
    window_class.hCursor = LoadCursorW(nullptr, MAKEINTRESOURCEW(32512));
    window_class.lpszClassName = WindowClassName;
    g_window_class = RegisterClassW(&window_class);
    if (g_window_class == 0)
    {
        g_window_class_error = GetLastError();
        if (g_window_class_error == ERROR_CLASS_ALREADY_EXISTS)
        {
            g_window_class = 1;
            g_window_class_error = ERROR_SUCCESS;
        }
    }
}

openusd_status CreateContext(
    ChildState* child,
    std::string& error)
{
    child->device = GetDC(child->window);
    if (child->device == nullptr)
    {
        error = Win32Error("GetDC");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    if (GetPixelFormat(child->device) == 0)
    {
        PIXELFORMATDESCRIPTOR descriptor{};
        descriptor.nSize = sizeof(descriptor);
        descriptor.nVersion = 1;
        descriptor.dwFlags =
            PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER;
        descriptor.iPixelType = PFD_TYPE_RGBA;
        descriptor.cColorBits = 32;
        descriptor.cAlphaBits = 8;
        descriptor.cDepthBits = 24;
        descriptor.cStencilBits = 8;
        descriptor.iLayerType = PFD_MAIN_PLANE;
        const int format = ChoosePixelFormat(child->device, &descriptor);
        if (format == 0 ||
            SetPixelFormat(child->device, format, &descriptor) == FALSE)
        {
            error = Win32Error("SetPixelFormat");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
    }

    HGLRC bootstrap = wglCreateContext(child->device);
    if (bootstrap == nullptr ||
        wglMakeCurrent(child->device, bootstrap) == FALSE)
    {
        if (bootstrap != nullptr)
        {
            wglDeleteContext(bootstrap);
        }
        error = Win32Error("WGL bootstrap");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    const WglCreateContextAttribs create_context =
        LoadWgl<WglCreateContextAttribs>("wglCreateContextAttribsARB");
    if (create_context == nullptr)
    {
        wglMakeCurrent(nullptr, nullptr);
        wglDeleteContext(bootstrap);
        error = "WGL_ARB_create_context is unavailable.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    for (const int minor : {6, 5})
    {
        const int attributes[] =
        {
            WglContextMajorVersion,
            4,
            WglContextMinorVersion,
            minor,
            WglContextProfileMask,
            WglContextCompatibilityProfileBit,
            0
        };
        child->context =
            create_context(child->device, nullptr, attributes);
        if (child->context != nullptr)
        {
            break;
        }
    }
    wglMakeCurrent(nullptr, nullptr);
    wglDeleteContext(bootstrap);
    if (child->context == nullptr ||
        wglMakeCurrent(child->device, child->context) == FALSE)
    {
        error = "A WGL 4.6 or 4.5 compatibility context could not be created.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    child->context_generation.fetch_add(1, std::memory_order_relaxed);
    GLint major = 0;
    GLint minor = 0;
    GLint profile = 0;
    glGetIntegerv(GlMajorVersion, &major);
    glGetIntegerv(GlMinorVersion, &minor);
    glGetIntegerv(GlContextProfileMask, &profile);
    child->gl_major.store(major, std::memory_order_relaxed);
    child->gl_minor.store(minor, std::memory_order_relaxed);
    child->compatibility_profile.store(
        (profile & WglContextCompatibilityProfileBit) != 0 ? 1 : 0,
        std::memory_order_relaxed);
    return OPENUSD_STATUS_OK;
}

void DestroyRenderTarget(ChildState* child) noexcept;

openusd_status DestroyContextChecked(
    ChildState* child,
    std::string& error)
{
    if (child->context != nullptr &&
        wglGetCurrentContext() == child->context)
    {
        DestroyRenderTarget(child);
    }
    if (child->context != nullptr &&
        wglGetCurrentContext() == child->context)
    {
        if (IsFailpoint("wgl-unbind") ||
            wglMakeCurrent(nullptr, nullptr) == FALSE)
        {
            error = IsFailpoint("wgl-unbind")
                ? "Injected wglMakeCurrent teardown failure."
                : Win32Error("wglMakeCurrent");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
    }
    if (child->context != nullptr)
    {
        if (IsFailpoint("context-delete") ||
            wglDeleteContext(child->context) == FALSE)
        {
            error = IsFailpoint("context-delete")
                ? "Injected wglDeleteContext failure."
                : Win32Error("wglDeleteContext");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        child->context = nullptr;
    }
    if (child->device != nullptr)
    {
        if (IsFailpoint("release-dc") ||
            ReleaseDC(child->window, child->device) == 0)
        {
            error = IsFailpoint("release-dc")
                ? "Injected ReleaseDC failure."
                : Win32Error("ReleaseDC");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        child->device = nullptr;
    }
    return OPENUSD_STATUS_OK;
}

void DestroyContextBestEffort(ChildState* child) noexcept
{
    if (child->context != nullptr &&
        wglGetCurrentContext() == child->context)
    {
        DestroyRenderTarget(child);
        wglMakeCurrent(nullptr, nullptr);
    }
    if (child->context != nullptr)
    {
        wglDeleteContext(child->context);
        child->context = nullptr;
    }
    if (child->device != nullptr)
    {
        ReleaseDC(child->window, child->device);
        child->device = nullptr;
    }
}

openusd_status GetStormName(
    openusd_storm_renderer* renderer,
    std::string& name,
    std::string& error)
{
    size_t required = 0;
    char error_bytes[1024]{};
    openusd_error_buffer native_error{
        error_bytes,
        sizeof(error_bytes),
        0};
    openusd_status status = openusd_storm_get_renderer_name(
        renderer,
        nullptr,
        0,
        &required,
        &native_error);
    if (status != OPENUSD_STATUS_BUFFER_TOO_SMALL || required == 0)
    {
        error = error_bytes;
        return status;
    }
    std::string value(required, '\0');
    status = openusd_storm_get_renderer_name(
        renderer,
        value.data(),
        value.size(),
        &required,
        &native_error);
    if (status != OPENUSD_STATUS_OK)
    {
        error = error_bytes;
        return status;
    }
    value.resize(required - 1);
    name = std::move(value);
    return OPENUSD_STATUS_OK;
}

openusd_status CreateRenderer(
    ChildState* child,
    std::string& error)
{
    char error_bytes[4096]{};
    openusd_error_buffer native_error{
        error_bytes,
        sizeof(error_bytes),
        0};
    openusd_status status = openusd_storm_create_from_stage(
        child->plugin_path.c_str(),
        child->stage,
        &child->renderer,
        &native_error);
    if (status != OPENUSD_STATUS_OK)
    {
        error = error_bytes;
        return status;
    }
    status = GetStormName(child->renderer, child->renderer_name, error);
    if (status != OPENUSD_STATUS_OK)
    {
        openusd_storm_release(child->renderer);
        child->renderer = nullptr;
    }
    return status;
}

void DestroyRenderTarget(ChildState* child) noexcept
{
    if (child->render_framebuffer != 0)
    {
        const GlDeleteFramebuffers delete_framebuffers =
            LoadWgl<GlDeleteFramebuffers>("glDeleteFramebuffers");
        if (delete_framebuffers != nullptr)
        {
            delete_framebuffers(1, &child->render_framebuffer);
        }
        child->render_framebuffer = 0;
    }
    if (child->render_depth_buffer != 0)
    {
        const GlDeleteRenderbuffers delete_renderbuffers =
            LoadWgl<GlDeleteRenderbuffers>("glDeleteRenderbuffers");
        if (delete_renderbuffers != nullptr)
        {
            delete_renderbuffers(1, &child->render_depth_buffer);
        }
        child->render_depth_buffer = 0;
    }
    if (child->render_color_texture != 0)
    {
        glDeleteTextures(1, &child->render_color_texture);
        child->render_color_texture = 0;
    }
    child->render_width = 0;
    child->render_height = 0;
}

openusd_status EnsureRenderTarget(
    ChildState* child,
    int32_t width,
    int32_t height,
    std::string& error)
{
    if (child->render_framebuffer != 0 &&
        child->render_width == width &&
        child->render_height == height)
    {
        return OPENUSD_STATUS_OK;
    }

    const GlGenFramebuffers gen_framebuffers =
        LoadWgl<GlGenFramebuffers>("glGenFramebuffers");
    const GlBindFramebuffer bind_framebuffer =
        LoadWgl<GlBindFramebuffer>("glBindFramebuffer");
    const GlFramebufferTexture2D framebuffer_texture =
        LoadWgl<GlFramebufferTexture2D>("glFramebufferTexture2D");
    const GlCheckFramebufferStatus check_framebuffer =
        LoadWgl<GlCheckFramebufferStatus>("glCheckFramebufferStatus");
    const GlGenRenderbuffers gen_renderbuffers =
        LoadWgl<GlGenRenderbuffers>("glGenRenderbuffers");
    const GlBindRenderbuffer bind_renderbuffer =
        LoadWgl<GlBindRenderbuffer>("glBindRenderbuffer");
    const GlRenderbufferStorage renderbuffer_storage =
        LoadWgl<GlRenderbufferStorage>("glRenderbufferStorage");
    const GlFramebufferRenderbuffer framebuffer_renderbuffer =
        LoadWgl<GlFramebufferRenderbuffer>("glFramebufferRenderbuffer");
    if (gen_framebuffers == nullptr ||
        bind_framebuffer == nullptr ||
        framebuffer_texture == nullptr ||
        check_framebuffer == nullptr ||
        gen_renderbuffers == nullptr ||
        bind_renderbuffer == nullptr ||
        renderbuffer_storage == nullptr ||
        framebuffer_renderbuffer == nullptr)
    {
        error = "Required OpenGL framebuffer functions are unavailable.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    GLint framebuffer_binding = 0;
    GLint renderbuffer_binding = 0;
    GLint texture_binding = 0;
    glGetIntegerv(GlDrawFramebufferBinding, &framebuffer_binding);
    glGetIntegerv(GlRenderbufferBinding, &renderbuffer_binding);
    glGetIntegerv(GlTextureBinding2d, &texture_binding);
    DestroyRenderTarget(child);

    glGenTextures(1, &child->render_color_texture);
    glBindTexture(GL_TEXTURE_2D, child->render_color_texture);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
    glTexImage2D(
        GL_TEXTURE_2D,
        0,
        GlRgba8,
        width,
        height,
        0,
        GL_RGBA,
        GL_UNSIGNED_BYTE,
        nullptr);

    gen_renderbuffers(1, &child->render_depth_buffer);
    bind_renderbuffer(GlRenderbuffer, child->render_depth_buffer);
    renderbuffer_storage(
        GlRenderbuffer,
        GlDepthComponent24,
        width,
        height);

    gen_framebuffers(1, &child->render_framebuffer);
    bind_framebuffer(GlFramebuffer, child->render_framebuffer);
    framebuffer_texture(
        GlFramebuffer,
        GlColorAttachment0,
        GL_TEXTURE_2D,
        child->render_color_texture,
        0);
    framebuffer_renderbuffer(
        GlFramebuffer,
        GlDepthAttachment,
        GlRenderbuffer,
        child->render_depth_buffer);
    const GLenum framebuffer_status = check_framebuffer(GlFramebuffer);

    bind_framebuffer(
        GlFramebuffer,
        static_cast<GLuint>(framebuffer_binding));
    bind_renderbuffer(
        GlRenderbuffer,
        static_cast<GLuint>(renderbuffer_binding));
    glBindTexture(GL_TEXTURE_2D, static_cast<GLuint>(texture_binding));
    const GLenum gl_error = glGetError();
    if (framebuffer_status != GlFramebufferComplete ||
        gl_error != GL_NO_ERROR)
    {
        DestroyRenderTarget(child);
        error = "Could not create the Storm child framebuffer.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    child->render_width = width;
    child->render_height = height;
    return OPENUSD_STATUS_OK;
}

openusd_status PresentRenderTarget(
    ChildState* child,
    int32_t width,
    int32_t height,
    std::string& error)
{
    const GlBindFramebuffer bind_framebuffer =
        LoadWgl<GlBindFramebuffer>("glBindFramebuffer");
    const GlBlitFramebuffer blit_framebuffer =
        LoadWgl<GlBlitFramebuffer>("glBlitFramebuffer");
    if (bind_framebuffer == nullptr || blit_framebuffer == nullptr)
    {
        error = "Required OpenGL presentation functions are unavailable.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    GLint read_framebuffer = 0;
    GLint draw_framebuffer = 0;
    GLint read_buffer = GL_BACK;
    GLint draw_buffer = GL_BACK;
    glGetIntegerv(GlReadFramebufferBinding, &read_framebuffer);
    glGetIntegerv(GlDrawFramebufferBinding, &draw_framebuffer);
    glGetIntegerv(GL_READ_BUFFER, &read_buffer);
    glGetIntegerv(GL_DRAW_BUFFER, &draw_buffer);
    bind_framebuffer(GlReadFramebuffer, child->render_framebuffer);
    bind_framebuffer(GlDrawFramebuffer, 0);
    glReadBuffer(GlColorAttachment0);
    glDrawBuffer(GL_BACK);
    blit_framebuffer(
        0,
        0,
        width,
        height,
        0,
        0,
        width,
        height,
        GL_COLOR_BUFFER_BIT,
        GL_NEAREST);
    bind_framebuffer(
        GlDrawFramebuffer,
        static_cast<GLuint>(draw_framebuffer));
    glDrawBuffer(static_cast<GLenum>(draw_buffer));
    bind_framebuffer(
        GlReadFramebuffer,
        static_cast<GLuint>(read_framebuffer));
    glReadBuffer(static_cast<GLenum>(read_buffer));
    const GLenum gl_error = glGetError();
    if (gl_error != GL_NO_ERROR)
    {
        error = "Could not present the Storm child framebuffer.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    if (SwapBuffers(child->device) == FALSE)
    {
        error = Win32Error("SwapBuffers");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    return OPENUSD_STATUS_OK;
}

openusd_status RenderFrame(
    ChildState* child,
    double time_code,
    const openusd_render_camera& camera,
    uint64_t state_revision,
    uint64_t scene_revision,
    uint32_t revision_flags,
    uint64_t& frame_count,
    int32_t& converged,
    std::string& error)
{
    if (child->renderer == nullptr || child->context == nullptr)
    {
        error = "The Storm child renderer is unavailable.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    char error_bytes[4096]{};
    openusd_error_buffer native_error{
        error_bytes,
        sizeof(error_bytes),
        0};
    const int32_t width =
        std::max<int32_t>(1, child->width.load(std::memory_order_relaxed));
    const int32_t height =
        std::max<int32_t>(1, child->height.load(std::memory_order_relaxed));
    openusd_status status = EnsureRenderTarget(child, width, height, error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    status = openusd_storm_render_v2(
        child->renderer,
        width,
        height,
        child->render_framebuffer,
        time_code,
        &camera,
        state_revision,
        scene_revision,
        revision_flags,
        &converged,
        &native_error);
    if (status != OPENUSD_STATUS_OK)
    {
        error = error_bytes;
        return status;
    }
    uint8_t pixel[4]{};
    const GlBindFramebuffer bind_framebuffer =
        LoadWgl<GlBindFramebuffer>("glBindFramebuffer");
    if (bind_framebuffer == nullptr)
    {
        error = "glBindFramebuffer is unavailable for Storm diagnostics.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    GLint read_framebuffer = 0;
    GLint read_buffer = GL_BACK;
    glGetIntegerv(GlReadFramebufferBinding, &read_framebuffer);
    glGetIntegerv(GL_READ_BUFFER, &read_buffer);
    bind_framebuffer(GlReadFramebuffer, child->render_framebuffer);
    glReadBuffer(GlColorAttachment0);
    glReadPixels(
        width / 2,
        height / 2,
        1,
        1,
        GL_RGBA,
        GL_UNSIGNED_BYTE,
        pixel);
    bind_framebuffer(
        GlReadFramebuffer,
        static_cast<GLuint>(read_framebuffer));
    glReadBuffer(static_cast<GLenum>(read_buffer));
    const uint64_t signature =
        static_cast<uint64_t>(pixel[0]) |
        (static_cast<uint64_t>(pixel[1]) << 8u) |
        (static_cast<uint64_t>(pixel[2]) << 16u) |
        (static_cast<uint64_t>(pixel[3]) << 24u);
    child->pixel_signature.store(signature, std::memory_order_relaxed);
    child->pixel_sample_count.fetch_add(1, std::memory_order_relaxed);
    status = PresentRenderTarget(child, width, height, error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    frame_count =
        child->frame_count.fetch_add(1, std::memory_order_relaxed) + 1;
    child->latest_time_code.store(time_code, std::memory_order_relaxed);
    child->latest_camera = camera;
    child->latest_rendered_camera_signature.store(
        openusd_render_camera_detail::Signature(camera),
        std::memory_order_relaxed);
    child->converged.store(converged, std::memory_order_relaxed);
    return OPENUSD_STATUS_OK;
}

uint32_t PackRgba(
    uint8_t red,
    uint8_t green,
    uint8_t blue,
    uint8_t alpha) noexcept
{
    return static_cast<uint32_t>(red) |
        (static_cast<uint32_t>(green) << 8u) |
        (static_cast<uint32_t>(blue) << 16u) |
        (static_cast<uint32_t>(alpha) << 24u);
}

uint8_t RgbaChannel(uint32_t rgba, uint32_t channel) noexcept
{
    return static_cast<uint8_t>((rgba >> (channel * 8u)) & 0xffu);
}

openusd_status DrawCaptureTestPattern(
    GLuint framebuffer,
    int32_t width,
    int32_t height,
    uint32_t background_rgba,
    std::string& error)
{
    const GlBindFramebuffer gl_bind_framebuffer =
        LoadWgl<GlBindFramebuffer>("glBindFramebuffer");
    if (gl_bind_framebuffer == nullptr)
    {
        error = "glBindFramebuffer is unavailable for diagnostic capture.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    GLint draw_framebuffer = 0;
    GLint draw_buffer = GL_BACK;
    GLint viewport[4]{};
    GLint scissor_box[4]{};
    GLfloat clear_color[4]{};
    glGetIntegerv(GlDrawFramebufferBinding, &draw_framebuffer);
    glGetIntegerv(GL_DRAW_BUFFER, &draw_buffer);
    glGetIntegerv(GL_VIEWPORT, viewport);
    glGetIntegerv(GL_SCISSOR_BOX, scissor_box);
    glGetFloatv(GL_COLOR_CLEAR_VALUE, clear_color);
    const GLboolean scissor_enabled = glIsEnabled(GL_SCISSOR_TEST);
    const GLboolean dither_enabled = glIsEnabled(GL_DITHER);

    gl_bind_framebuffer(GlDrawFramebuffer, framebuffer);
    glDrawBuffer(GlColorAttachment0);
    glViewport(0, 0, width, height);
    glDisable(GL_DITHER);
    glDisable(GL_SCISSOR_TEST);
    glClearColor(
        static_cast<GLfloat>(RgbaChannel(background_rgba, 0)) / 255.0F,
        static_cast<GLfloat>(RgbaChannel(background_rgba, 1)) / 255.0F,
        static_cast<GLfloat>(RgbaChannel(background_rgba, 2)) / 255.0F,
        static_cast<GLfloat>(RgbaChannel(background_rgba, 3)) / 255.0F);
    glClear(GL_COLOR_BUFFER_BIT);
    glEnable(GL_SCISSOR_TEST);
    glScissor(
        width / 4,
        height / 4,
        std::max<int32_t>(1, width / 2),
        std::max<int32_t>(1, height / 2));
    glClearColor(1.0F, 0.0F, 1.0F, 1.0F);
    glClear(GL_COLOR_BUFFER_BIT);
    glFlush();

    gl_bind_framebuffer(
        GlDrawFramebuffer,
        static_cast<GLuint>(draw_framebuffer));
    glDrawBuffer(static_cast<GLenum>(draw_buffer));
    glViewport(viewport[0], viewport[1], viewport[2], viewport[3]);
    glScissor(
        scissor_box[0],
        scissor_box[1],
        scissor_box[2],
        scissor_box[3]);
    glClearColor(
        clear_color[0],
        clear_color[1],
        clear_color[2],
        clear_color[3]);
    if (scissor_enabled == GL_FALSE)
    {
        glDisable(GL_SCISSOR_TEST);
    }
    if (dither_enabled != GL_FALSE)
    {
        glEnable(GL_DITHER);
    }

    GLint restored_draw_framebuffer = 0;
    GLint restored_draw_buffer = 0;
    GLint restored_viewport[4]{};
    GLint restored_scissor_box[4]{};
    GLfloat restored_clear_color[4]{};
    glGetIntegerv(
        GlDrawFramebufferBinding,
        &restored_draw_framebuffer);
    glGetIntegerv(GL_DRAW_BUFFER, &restored_draw_buffer);
    glGetIntegerv(GL_VIEWPORT, restored_viewport);
    glGetIntegerv(GL_SCISSOR_BOX, restored_scissor_box);
    glGetFloatv(GL_COLOR_CLEAR_VALUE, restored_clear_color);
    const bool state_restored =
        restored_draw_framebuffer == draw_framebuffer &&
        restored_draw_buffer == draw_buffer &&
        std::equal(
            restored_viewport,
            restored_viewport + 4,
            viewport) &&
        std::equal(
            restored_scissor_box,
            restored_scissor_box + 4,
            scissor_box) &&
        std::equal(
            restored_clear_color,
            restored_clear_color + 4,
            clear_color) &&
        glIsEnabled(GL_SCISSOR_TEST) == scissor_enabled &&
        glIsEnabled(GL_DITHER) == dither_enabled;
    const GLenum gl_error = glGetError();
    if (gl_error != GL_NO_ERROR || !state_restored)
    {
        error = gl_error != GL_NO_ERROR
            ? "The diagnostic GL test pattern failed with error " +
                std::to_string(gl_error) + "."
            : "The diagnostic GL test pattern did not restore GL state.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    return OPENUSD_STATUS_OK;
}

openusd_status CaptureFramebuffer(
    ChildState* child,
    Command* command)
{
    if (child->frame_count.load(std::memory_order_acquire) == 0)
    {
        command->error =
            "A completed Storm frame is required before framebuffer capture.";
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (GetCurrentThreadId() !=
            child->render_thread_id.load(std::memory_order_relaxed) ||
        wglGetCurrentContext() != child->context ||
        wglGetCurrentDC() != child->device)
    {
        command->error =
            "Framebuffer capture requires the child render thread and current WGL context.";
        return OPENUSD_STATUS_WRONG_THREAD;
    }
    if ((command->capture_flags &
         ~OPENUSD_STORM_CHILD_CAPTURE_DRAW_TEST_PATTERN) != 0)
    {
        command->error = "The framebuffer capture flags are invalid.";
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    const int32_t width =
        std::max<int32_t>(1, child->width.load(std::memory_order_relaxed));
    const int32_t height =
        std::max<int32_t>(1, child->height.load(std::memory_order_relaxed));
    const size_t pixel_count =
        static_cast<size_t>(width) * static_cast<size_t>(height);
    if (pixel_count > MaximumCaptureBytes / 4u)
    {
        command->error =
            "The framebuffer exceeds the 64 MiB diagnostic capture limit.";
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const size_t byte_count = pixel_count * 4u;
    std::vector<uint8_t> pixels(byte_count);
    if (child->render_framebuffer == 0 ||
        child->render_width != width ||
        child->render_height != height)
    {
        command->error =
            "A completed Storm frame at the current dimensions is required before capture.";
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }

    while (glGetError() != GL_NO_ERROR)
    {
    }
    if ((command->capture_flags &
         OPENUSD_STORM_CHILD_CAPTURE_DRAW_TEST_PATTERN) != 0)
    {
        const openusd_status pattern_status = DrawCaptureTestPattern(
            child->render_framebuffer,
            width,
            height,
            command->capture_background_rgba,
            command->error);
        if (pattern_status != OPENUSD_STATUS_OK)
        {
            return pattern_status;
        }
    }

    constexpr GLenum capture_buffer = GlColorAttachment0;
    const GlBindFramebuffer gl_bind_framebuffer =
        LoadWgl<GlBindFramebuffer>("glBindFramebuffer");
    if (gl_bind_framebuffer == nullptr)
    {
        command->error =
            "glBindFramebuffer is unavailable for diagnostic capture.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    GLint read_framebuffer = 0;
    GLint pack_alignment = 4;
    GLint read_buffer = GL_BACK;
    glGetIntegerv(GlReadFramebufferBinding, &read_framebuffer);
    glGetIntegerv(GL_PACK_ALIGNMENT, &pack_alignment);
    glGetIntegerv(GL_READ_BUFFER, &read_buffer);
    gl_bind_framebuffer(
        GlReadFramebuffer,
        child->render_framebuffer);
    glPixelStorei(GL_PACK_ALIGNMENT, 1);
    glReadBuffer(capture_buffer);
    glReadPixels(
        0,
        0,
        width,
        height,
        GL_RGBA,
        GL_UNSIGNED_BYTE,
        pixels.data());
    const GLenum read_error = glGetError();
    gl_bind_framebuffer(
        GlReadFramebuffer,
        static_cast<GLuint>(read_framebuffer));
    glReadBuffer(static_cast<GLenum>(read_buffer));
    glPixelStorei(GL_PACK_ALIGNMENT, pack_alignment);
    GLint restored_read_framebuffer = 0;
    GLint restored_pack_alignment = 0;
    GLint restored_read_buffer = 0;
    glGetIntegerv(
        GlReadFramebufferBinding,
        &restored_read_framebuffer);
    glGetIntegerv(GL_PACK_ALIGNMENT, &restored_pack_alignment);
    glGetIntegerv(GL_READ_BUFFER, &restored_read_buffer);
    const GLenum restore_error = glGetError();
    if (read_error != GL_NO_ERROR ||
        restore_error != GL_NO_ERROR ||
    restored_read_framebuffer != read_framebuffer ||
    restored_pack_alignment != pack_alignment ||
        restored_read_buffer != read_buffer)
    {
        command->error = read_error != GL_NO_ERROR
            ? "glReadPixels failed with error " +
                std::to_string(read_error) + "."
            : "Framebuffer capture did not restore GL readback state.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }

    uint64_t hash = 14695981039346656037ull;
    const auto hash_byte = [&hash](uint8_t value)
    {
        hash ^= value;
        hash *= 1099511628211ull;
    };
    for (uint32_t value :
         {static_cast<uint32_t>(width), static_cast<uint32_t>(height)})
    {
        for (uint32_t shift = 0; shift < 32; shift += 8)
        {
            hash_byte(static_cast<uint8_t>((value >> shift) & 0xffu));
        }
    }

    uint64_t sums[4]{};
    uint8_t minimum[4]{255, 255, 255, 255};
    uint8_t maximum[4]{};
    uint64_t non_background = 0;
    const uint8_t background[4]{
        RgbaChannel(command->capture_background_rgba, 0),
        RgbaChannel(command->capture_background_rgba, 1),
        RgbaChannel(command->capture_background_rgba, 2),
        RgbaChannel(command->capture_background_rgba, 3)};
    for (size_t offset = 0; offset < pixels.size(); offset += 4)
    {
        bool differs = false;
        for (uint32_t channel = 0; channel < 4; ++channel)
        {
            const uint8_t value = pixels[offset + channel];
            hash_byte(value);
            sums[channel] += value;
            minimum[channel] = std::min(minimum[channel], value);
            maximum[channel] = std::max(maximum[channel], value);
            if (channel < 3 &&
                std::abs(
                    static_cast<int>(value) -
                    static_cast<int>(background[channel])) >
                    command->capture_tolerance)
            {
                differs = true;
            }
        }
        non_background += differs ? 1u : 0u;
    }

    command->capture.frame_count =
        child->frame_count.load(std::memory_order_relaxed);
    command->capture.pixel_hash = hash;
    command->capture.pixel_count = pixel_count;
    command->capture.non_background_pixel_count = non_background;
    command->capture.width = width;
    command->capture.height = height;
    command->capture.dpi = child->dpi.load(std::memory_order_relaxed);
    command->capture.background_rgba = command->capture_background_rgba;
    command->capture.average_rgba = PackRgba(
        static_cast<uint8_t>(sums[0] / pixel_count),
        static_cast<uint8_t>(sums[1] / pixel_count),
        static_cast<uint8_t>(sums[2] / pixel_count),
        static_cast<uint8_t>(sums[3] / pixel_count));
    command->capture.minimum_rgba =
        PackRgba(minimum[0], minimum[1], minimum[2], minimum[3]);
    command->capture.maximum_rgba =
        PackRgba(maximum[0], maximum[1], maximum[2], maximum[3]);
    command->capture.read_buffer = capture_buffer;
    if (command->capture_pixels)
    {
        command->captured_pixels = std::move(pixels);
    }
    return OPENUSD_STATUS_OK;
}

openusd_status RecreateAfterContextLoss(
    ChildState* child,
    std::string& error)
{
    if (child->renderer != nullptr)
    {
        if (wglMakeCurrent(nullptr, nullptr) == FALSE)
        {
            error = Win32Error("wglMakeCurrent");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        char error_bytes[4096]{};
        openusd_error_buffer native_error{
            error_bytes,
            sizeof(error_bytes),
            0};
        const openusd_status abandon_status = IsFailpoint("storm-abandon")
            ? OPENUSD_STATUS_NATIVE_ERROR
            : openusd_storm_abandon(child->renderer, &native_error);
        if (abandon_status != OPENUSD_STATUS_OK)
        {
            wglMakeCurrent(child->device, child->context);
            error = IsFailpoint("storm-abandon")
                ? "Injected Storm abandon failure."
                : error_bytes;
            return abandon_status;
        }
        child->renderer = nullptr;
        child->teardown_fallback_count.fetch_add(1, std::memory_order_relaxed);
    }
    DestroyRenderTarget(child);
    openusd_status status = DestroyContextChecked(child, error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    status = CreateContext(child, error);
    if (status == OPENUSD_STATUS_OK)
    {
        status = CreateRenderer(child, error);
    }
    return status;
}

openusd_status TeardownRendererAndContext(
    ChildState* child,
    std::string& error)
{
    if (child->renderer != nullptr)
    {
        char destroy_error_bytes[4096]{};
        openusd_error_buffer destroy_error{
            destroy_error_bytes,
            sizeof(destroy_error_bytes),
            0};
        const bool inject_destroy =
            IsFailpoint("storm-destroy") ||
            IsFailpoint("storm-destroy-and-abandon");
        const openusd_status destroy_status = inject_destroy
            ? OPENUSD_STATUS_NATIVE_ERROR
            : openusd_storm_destroy(child->renderer, &destroy_error);
        if (destroy_status == OPENUSD_STATUS_OK)
        {
            child->renderer = nullptr;
        }
        else
        {
            const std::string destroy_message = inject_destroy
                ? "Injected Storm destroy failure."
                : std::string(destroy_error_bytes);
            if (wglGetCurrentContext() == child->context &&
                wglMakeCurrent(nullptr, nullptr) == FALSE)
            {
                error = destroy_message + " " + Win32Error("wglMakeCurrent");
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
            char abandon_error_bytes[4096]{};
            openusd_error_buffer abandon_error{
                abandon_error_bytes,
                sizeof(abandon_error_bytes),
                0};
            const bool inject_abandon =
                IsFailpoint("storm-destroy-and-abandon");
            const openusd_status abandon_status = inject_abandon
                ? OPENUSD_STATUS_NATIVE_ERROR
                : openusd_storm_abandon(child->renderer, &abandon_error);
            if (abandon_status != OPENUSD_STATUS_OK)
            {
                if (child->context != nullptr && child->device != nullptr)
                {
                    wglMakeCurrent(child->device, child->context);
                }
                error = destroy_message + " Storm abandon also failed: " +
                    (inject_abandon
                        ? "injected failure."
                        : std::string(abandon_error_bytes));
                return abandon_status;
            }
            child->renderer = nullptr;
            child->teardown_fallback_count.fetch_add(
                1,
                std::memory_order_relaxed);
        }
    }
    DestroyRenderTarget(child);
    return DestroyContextChecked(child, error);
}

void CompleteCommand(
    ChildState* child,
    const std::shared_ptr<Command>& command)
{
    std::lock_guard lock(child->gate);
    command->done = true;
    if (command->wait)
    {
        command->completion.notify_all();
    }
}

uint32_t PendingCommandCount(const ChildState* child) noexcept
{
    return static_cast<uint32_t>(
        child->synchronous_commands.size() +
        (child->asynchronous_render == nullptr ? 0 : 1));
}

void UpdatePendingPeak(ChildState* child) noexcept
{
    const uint32_t value = PendingCommandCount(child);
    uint32_t current =
        child->peak_pending_command_count.load(std::memory_order_relaxed);
    while (current < value &&
           !child->peak_pending_command_count.compare_exchange_weak(
               current,
               value,
               std::memory_order_relaxed,
               std::memory_order_relaxed))
    {
    }
}

void CancelPendingCommands(ChildState* child)
{
    for (const std::shared_ptr<Command>& pending :
         child->synchronous_commands)
    {
        if (pending->pick != nullptr)
        {
            pending->pick->Cancel(
                child->context_generation.load(std::memory_order_relaxed));
        }
        pending->status = OPENUSD_STATUS_INVALID_ARGUMENT;
        pending->error = "The Storm child is closing.";
        pending->done = true;
        if (pending->wait)
        {
            pending->completion.notify_all();
        }
        child->cancelled_command_count.fetch_add(1, std::memory_order_relaxed);
    }
    child->synchronous_commands.clear();
    if (child->asynchronous_render != nullptr)
    {
        child->asynchronous_render.reset();
        child->cancelled_command_count.fetch_add(1, std::memory_order_relaxed);
    }
}

void RenderThreadMain(ChildState* child)
{
    child->render_thread_id.store(GetCurrentThreadId(), std::memory_order_relaxed);
    std::string error;
    openusd_status status = CreateContext(child, error);
    if (status == OPENUSD_STATUS_OK)
    {
        status = CreateRenderer(child, error);
    }
    {
        std::lock_guard lock(child->gate);
        child->initialization_status = status;
        child->initialization_error = error;
        child->initialization_complete = true;
        child->initialized.notify_all();
    }
    if (status != OPENUSD_STATUS_OK)
    {
        DestroyContextBestEffort(child);
        return;
    }

    bool stop = false;
    while (!stop)
    {
        std::shared_ptr<Command> command;
        {
            std::unique_lock lock(child->gate);
            child->commands_available.wait(
                lock,
                [child]
                {
                    return !child->synchronous_commands.empty() ||
                        child->asynchronous_render != nullptr;
                });
            if (!child->synchronous_commands.empty())
            {
                command = child->synchronous_commands.front();
                child->synchronous_commands.pop_front();
            }
            else
            {
                command = std::move(child->asynchronous_render);
            }
        }
        switch (command->kind)
        {
            case CommandKind::Render:
                command->status = RenderFrame(
                    child,
                    command->time_code,
                    command->camera,
                    command->revision,
                    command->scene_revision,
                    command->revision_flags,
                    command->frame_count,
                    command->converged,
                    command->error);
                break;
            case CommandKind::Capture:
                command->status =
                    CaptureFramebuffer(child, command.get());
                break;
            case CommandKind::Pick:
                command->status = command->pick->Execute(
                    child->renderer,
                    child->context_generation.load(std::memory_order_relaxed),
                    command->error);
                break;
            case CommandKind::Selection:
                command->status =
                    command->selection->Execute(child->renderer, command->error);
                break;
            case CommandKind::TransformOverrides:
                command->status = command->transform_overrides->Execute(
                    child->renderer,
                    command->error);
                break;
            case CommandKind::DeformationOverrides:
                command->status = command->deformation_overrides->Execute(
                    child->renderer,
                    command->error);
                break;
            case CommandKind::ContextLoss:
                command->status =
                    RecreateAfterContextLoss(child, command->error);
                break;
            case CommandKind::Stop:
                command->status =
                    TeardownRendererAndContext(child, command->error);
                {
                    std::lock_guard lock(child->gate);
                    if (command->status == OPENUSD_STATUS_OK)
                    {
                        child->lifecycle = LifecycleState::Stopped;
                        stop = true;
                    }
                    else
                    {
                        child->lifecycle = LifecycleState::Running;
                    }
                }
                break;
        }
        CompleteCommand(child, command);
    }
}

openusd_status ValidateChild(
    ChildState* child,
    openusd_error_buffer* error)
{
    if (child == nullptr || child->window == nullptr ||
        IsWindow(child->window) == FALSE)
    {
        WriteError(error, "A valid Storm native child is required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
}

openusd_status RequireCreatorThread(
    const ChildState* child,
    openusd_error_buffer* error)
{
    if (GetCurrentThreadId() != child->creator_thread_id)
    {
        WriteError(
            error,
            "The Storm child HWND must be operated on its creator thread.");
        return OPENUSD_STATUS_WRONG_THREAD;
    }
    return OPENUSD_STATUS_OK;
}

openusd_status RequireRunning(
    ChildState* child,
    openusd_error_buffer* error)
{
    std::lock_guard lock(child->gate);
    if (child->lifecycle != LifecycleState::Running)
    {
        WriteError(error, "The Storm child is closing or stopped.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return OPENUSD_STATUS_OK;
}

openusd_status QueueCommand(
    ChildState* child,
    CommandKind kind,
    double time_code,
    const openusd_render_camera* camera,
    uint64_t revision,
    uint64_t scene_revision,
    uint32_t revision_flags,
    bool wait,
    uint64_t* frame_count,
    int32_t* converged,
    openusd_error_buffer* error)
{
    openusd_status status = ValidateChild(child, error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    openusd_render_camera camera_value =
        openusd_render_camera_detail::Automatic();
    if (kind == CommandKind::Render)
    {
        std::string camera_error;
        if (!openusd_render_camera_detail::Validate(camera, camera_error))
        {
            WriteError(error, camera_error);
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        camera_value = *camera;
        if ((revision_flags & ~OPENUSD_STORM_RENDER_HAS_SCENE_REVISION) != 0)
        {
            WriteError(error, "The render revision flags are invalid.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
    }
    std::unique_lock lock(child->gate);
    if (child->lifecycle != LifecycleState::Running)
    {
        WriteError(error, "The Storm child is closing or stopped.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (!wait && kind == CommandKind::Render)
    {
        child->latest_requested_revision.store(revision, std::memory_order_relaxed);
        child->latest_requested_camera_signature.store(
            openusd_render_camera_detail::Signature(camera_value),
            std::memory_order_relaxed);
        if (child->asynchronous_render != nullptr)
        {
            child->asynchronous_render->time_code = time_code;
            child->asynchronous_render->camera = camera_value;
            child->asynchronous_render->revision = revision;
            child->asynchronous_render->scene_revision = scene_revision;
            child->asynchronous_render->revision_flags = revision_flags;
            child->coalesced_request_count.fetch_add(
                1,
                std::memory_order_relaxed);
        }
        else
        {
            auto command = std::make_shared<Command>();
            command->kind = kind;
            command->time_code = time_code;
            command->camera = camera_value;
            command->revision = revision;
            command->scene_revision = scene_revision;
            command->revision_flags = revision_flags;
            child->asynchronous_render = std::move(command);
            UpdatePendingPeak(child);
        }
        child->commands_available.notify_one();
        return OPENUSD_STATUS_OK;
    }
    auto command = std::make_shared<Command>();
    command->kind = kind;
    command->time_code = time_code;
    command->camera = camera_value;
    command->revision = revision;
    command->scene_revision = scene_revision;
    command->revision_flags = revision_flags;
    command->wait = wait;
    child->synchronous_commands.push_back(command);
    UpdatePendingPeak(child);
    child->commands_available.notify_one();
    if (!wait)
    {
        return OPENUSD_STATUS_OK;
    }
    command->completion.wait(lock, [&command] { return command->done; });
    if (frame_count != nullptr)
    {
        *frame_count = command->frame_count;
    }
    if (converged != nullptr)
    {
        *converged = command->converged;
    }
    if (command->status != OPENUSD_STATUS_OK)
    {
        WriteError(error, command->error);
    }
    return command->status;
}

openusd_status QueueCapture(
    ChildState* child,
    uint32_t background_rgba,
    uint8_t tolerance,
    uint32_t flags,
    uint8_t* rgba_buffer,
    size_t rgba_capacity,
    size_t* rgba_required,
    openusd_storm_child_framebuffer_capture* capture,
    openusd_error_buffer* error)
{
    const openusd_status child_status = ValidateChild(child, error);
    if (child_status != OPENUSD_STATUS_OK)
    {
        return child_status;
    }
    auto command = std::make_shared<Command>();
    command->kind = CommandKind::Capture;
    command->wait = true;
    command->capture_background_rgba = background_rgba;
    command->capture_tolerance = tolerance;
    command->capture_flags = flags;
    command->capture_pixels = rgba_buffer != nullptr;
    std::unique_lock lock(child->gate);
    if (child->lifecycle != LifecycleState::Running)
    {
        WriteError(error, "The Storm child is closing or stopped.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    child->synchronous_commands.push_back(command);
    UpdatePendingPeak(child);
    child->commands_available.notify_one();
    command->completion.wait(lock, [&command] { return command->done; });
    if (command->status != OPENUSD_STATUS_OK)
    {
        WriteError(error, command->error);
        return command->status;
    }
    lock.unlock();

    *capture = command->capture;
    *rgba_required = command->capture.pixel_count * 4u;
    if (rgba_buffer == nullptr)
    {
        return OPENUSD_STATUS_OK;
    }
    if (rgba_capacity < *rgba_required)
    {
        WriteError(
            error,
            "The RGBA framebuffer output buffer is too small.");
        return OPENUSD_STATUS_BUFFER_TOO_SMALL;
    }
    std::memcpy(
        rgba_buffer,
        command->captured_pixels.data(),
        *rgba_required);
    return OPENUSD_STATUS_OK;
}

openusd_status QueuePick(
    ChildState* child,
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
    if (result == nullptr ||
        result->struct_size != sizeof(openusd_render_pick_result) ||
        result->version != OPENUSD_RENDER_PICK_RESULT_VERSION)
    {
        if (result != nullptr)
        {
            std::memset(result, 0, sizeof(*result));
        }
        WriteError(error, "The pick result struct size or version is unsupported.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const uint32_t result_size = result->struct_size;
    const uint32_t result_version = result->version;
    std::memset(result, 0, sizeof(*result));
    result->struct_size = result_size;
    result->version = result_version;
    result->status = OPENUSD_RENDER_PICK_STATUS_INVALID;
    result->normalized_depth = 1.0;
    result->instance_index = -1;
    result->element_index = -1;
    if (request == nullptr ||
        (prim_path_buffer == nullptr && prim_path_capacity != 0) ||
        (instancer_path_buffer == nullptr && instancer_path_capacity != 0) ||
        (instance_context == nullptr && instance_context_capacity != 0) ||
        (instance_context_paths_buffer == nullptr &&
         instance_context_paths_capacity != 0) ||
        !OpenUsdStormChildValidPickCapacities(
            prim_path_capacity,
            instancer_path_capacity,
            instance_context_capacity,
            instance_context_paths_capacity))
    {
        WriteError(error, "The Storm child pick arguments are invalid.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const openusd_status child_status = ValidateChild(child, error);
    if (child_status != OPENUSD_STATUS_OK)
    {
        return child_status;
    }

    auto command = std::make_shared<Command>();
    command->kind = CommandKind::Pick;
    command->wait = true;
    command->pick = std::make_unique<OpenUsdStormChildPickPayload>();
    command->pick->request = *request;
    command->pick->result.struct_size = sizeof(openusd_render_pick_result);
    command->pick->result.version = OPENUSD_RENDER_PICK_RESULT_VERSION;
    command->pick->result.status = OPENUSD_RENDER_PICK_STATUS_INVALID;
    command->pick->result.normalized_depth = 1.0;
    command->pick->result.instance_index = -1;
    command->pick->result.element_index = -1;
    command->pick->prim_path.resize(prim_path_capacity);
    command->pick->instancer_path.resize(instancer_path_capacity);
    command->pick->instance_context.resize(instance_context_capacity);
    command->pick->instance_context_paths.resize(
        instance_context_paths_capacity);

    std::unique_lock lock(child->gate);
    if (child->lifecycle != LifecycleState::Running)
    {
        command->pick->Cancel(
            child->context_generation.load(std::memory_order_relaxed));
        OpenUsdStormChildCopyPickOutputs(
            *command->pick,
            result,
            prim_path_buffer,
            prim_path_capacity,
            instancer_path_buffer,
            instancer_path_capacity,
            instance_context,
            instance_context_capacity,
            instance_context_paths_buffer,
            instance_context_paths_capacity);
        WriteError(error, "The Storm child is closing or stopped.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    child->synchronous_commands.push_front(command);
    UpdatePendingPeak(child);
    child->commands_available.notify_one();
    command->completion.wait(lock, [&command] { return command->done; });
    OpenUsdStormChildCopyPickOutputs(
        *command->pick,
        result,
        prim_path_buffer,
        prim_path_capacity,
        instancer_path_buffer,
        instancer_path_capacity,
        instance_context,
        instance_context_capacity,
        instance_context_paths_buffer,
        instance_context_paths_capacity);
    if (command->status != OPENUSD_STATUS_OK &&
        command->status != OPENUSD_STATUS_BUFFER_TOO_SMALL)
    {
        WriteError(error, command->error);
    }
    return command->status;
}

openusd_status QueueTransformOverrides(
    ChildState* child,
    const openusd_storm_transform_override_update* update,
    openusd_storm_transform_override_diagnostics* diagnostics,
    openusd_error_buffer* error)
{
    if (update == nullptr ||
        update->struct_size !=
            sizeof(openusd_storm_transform_override_update) ||
        update->version != OPENUSD_STORM_TRANSFORM_OVERRIDE_UPDATE_VERSION ||
        !OpenUsdStormChildValidTransformOverrideCapacities(
            update->item_count,
            update->path_bytes_size) ||
        (update->item_count != 0 && update->items == nullptr) ||
        (update->path_bytes_size != 0 && update->path_bytes == nullptr))
    {
        WriteError(
            error,
            "The packed Storm child transform override update is invalid.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (diagnostics != nullptr &&
        (diagnostics->struct_size !=
             sizeof(openusd_storm_transform_override_diagnostics) ||
         diagnostics->version !=
             OPENUSD_STORM_TRANSFORM_OVERRIDE_DIAGNOSTICS_VERSION))
    {
        WriteError(
            error,
            "The Storm child transform override diagnostics struct is invalid.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const openusd_status child_status = ValidateChild(child, error);
    if (child_status != OPENUSD_STATUS_OK)
    {
        return child_status;
    }
    auto command = std::make_shared<Command>();
    command->kind = CommandKind::TransformOverrides;
    command->wait = true;
    command->transform_overrides =
        std::make_unique<OpenUsdStormChildTransformOverridePayload>();
    command->transform_overrides->update = *update;
    command->transform_overrides->capture_diagnostics = diagnostics != nullptr;
    if (update->item_count != 0)
    {
        command->transform_overrides->items.assign(
            update->items,
            update->items + update->item_count);
    }
    if (update->path_bytes_size != 0)
    {
        command->transform_overrides->path_bytes.assign(
            update->path_bytes,
            update->path_bytes + update->path_bytes_size);
    }

    std::unique_lock lock(child->gate);
    if (child->lifecycle != LifecycleState::Running)
    {
        WriteError(error, "The Storm child is closing or stopped.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    child->synchronous_commands.push_front(command);
    UpdatePendingPeak(child);
    child->commands_available.notify_one();
    command->completion.wait(lock, [&command] { return command->done; });
    if (command->status != OPENUSD_STATUS_OK)
    {
        WriteError(error, command->error);
        return command->status;
    }
    if (diagnostics != nullptr)
    {
        *diagnostics = command->transform_overrides->diagnostics;
    }
    return command->status;
}

openusd_status QueueDeformationOverrides(
    ChildState* child,
    const openusd_storm_deformation_override_update* update,
    openusd_storm_deformation_override_diagnostics* diagnostics,
    openusd_error_buffer* error)
{
    if (update == nullptr ||
        update->struct_size !=
            sizeof(openusd_storm_deformation_override_update) ||
        update->version != OPENUSD_STORM_DEFORMATION_OVERRIDE_UPDATE_VERSION ||
        !OpenUsdStormChildValidDeformationOverrideCapacities(
            update->item_count,
            update->point_count,
            update->path_bytes_size) ||
        (update->item_count != 0 && update->items == nullptr) ||
        (update->point_count != 0 && update->points == nullptr) ||
        (update->path_bytes_size != 0 && update->path_bytes == nullptr))
    {
        WriteError(
            error,
            "The packed Storm child deformation override update is invalid.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (diagnostics != nullptr &&
        (diagnostics->struct_size !=
             sizeof(openusd_storm_deformation_override_diagnostics) ||
         diagnostics->version !=
             OPENUSD_STORM_DEFORMATION_OVERRIDE_DIAGNOSTICS_VERSION))
    {
        WriteError(
            error,
            "The Storm child deformation override diagnostics struct is invalid.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const openusd_status child_status = ValidateChild(child, error);
    if (child_status != OPENUSD_STATUS_OK)
    {
        return child_status;
    }
    auto command = std::make_shared<Command>();
    command->kind = CommandKind::DeformationOverrides;
    command->wait = true;
    command->deformation_overrides =
        std::make_unique<OpenUsdStormChildDeformationOverridePayload>();
    command->deformation_overrides->update = *update;
    command->deformation_overrides->capture_diagnostics = diagnostics != nullptr;
    if (update->item_count != 0)
    {
        command->deformation_overrides->items.assign(
            update->items,
            update->items + update->item_count);
    }
    if (update->point_count != 0)
    {
        command->deformation_overrides->points.assign(
            update->points,
            update->points + (static_cast<size_t>(update->point_count) * 3u));
    }
    if (update->path_bytes_size != 0)
    {
        command->deformation_overrides->path_bytes.assign(
            update->path_bytes,
            update->path_bytes + update->path_bytes_size);
    }

    std::unique_lock lock(child->gate);
    if (child->lifecycle != LifecycleState::Running)
    {
        WriteError(error, "The Storm child is closing or stopped.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    child->synchronous_commands.push_front(command);
    UpdatePendingPeak(child);
    child->commands_available.notify_one();
    command->completion.wait(lock, [&command] { return command->done; });
    if (command->status != OPENUSD_STATUS_OK)
    {
        WriteError(error, command->error);
        return command->status;
    }
    if (diagnostics != nullptr)
    {
        *diagnostics = command->deformation_overrides->diagnostics;
    }
    return command->status;
}

openusd_status QueueSelection(
    ChildState* child,
    const openusd_storm_selection_update* update,
    openusd_error_buffer* error)
{
    if (update == nullptr ||
        update->struct_size != sizeof(openusd_storm_selection_update) ||
        update->version != OPENUSD_STORM_SELECTION_UPDATE_VERSION ||
        update->item_count > OpenUsdStormChildMaximumPickContextEntries ||
        update->path_bytes_size > OpenUsdStormChildMaximumPickStringBytes ||
        (update->item_count != 0 && update->items == nullptr) ||
        (update->path_bytes_size != 0 && update->path_bytes == nullptr))
    {
        WriteError(error, "The packed Storm child selection update is invalid.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const openusd_status child_status = ValidateChild(child, error);
    if (child_status != OPENUSD_STATUS_OK)
    {
        return child_status;
    }
    auto command = std::make_shared<Command>();
    command->kind = CommandKind::Selection;
    command->wait = true;
    command->selection =
        std::make_unique<OpenUsdStormChildSelectionPayload>();
    command->selection->update = *update;
    if (update->item_count != 0)
    {
        command->selection->items.assign(
            update->items,
            update->items + update->item_count);
    }
    if (update->path_bytes_size != 0)
    {
        command->selection->path_bytes.assign(
            update->path_bytes,
            update->path_bytes + update->path_bytes_size);
    }

    std::unique_lock lock(child->gate);
    if (child->lifecycle != LifecycleState::Running)
    {
        WriteError(error, "The Storm child is closing or stopped.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    child->synchronous_commands.push_front(command);
    UpdatePendingPeak(child);
    child->commands_available.notify_one();
    command->completion.wait(lock, [&command] { return command->done; });
    if (command->status != OPENUSD_STATUS_OK)
    {
        WriteError(error, command->error);
    }
    return command->status;
}

openusd_status QueueStop(
    ChildState* child,
    openusd_error_buffer* error)
{
    auto command = std::make_shared<Command>();
    command->kind = CommandKind::Stop;
    command->wait = true;
    std::unique_lock lock(child->gate);
    if (child->lifecycle == LifecycleState::Stopped)
    {
        return OPENUSD_STATUS_OK;
    }
    if (child->lifecycle != LifecycleState::Running)
    {
        WriteError(error, "The Storm child is already closing.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    child->lifecycle = LifecycleState::Closing;
    CancelPendingCommands(child);
    child->synchronous_commands.push_front(command);
    UpdatePendingPeak(child);
    child->commands_available.notify_one();
    command->completion.wait(lock, [&command] { return command->done; });
    if (command->status != OPENUSD_STATUS_OK)
    {
        WriteError(error, command->error);
    }
    return command->status;
}
}

extern "C" uint32_t openusd_storm_child_get_abi_version(void)
{
    return OPENUSD_STORM_CHILD_ABI_VERSION;
}

extern "C" openusd_status openusd_storm_child_create(
    void* parent_window,
    const char* plugin_path,
    openusd_stage* stage,
    int32_t width,
    int32_t height,
    uint32_t dpi,
    openusd_storm_child** child,
    openusd_error_buffer* error)
{
    if (child != nullptr)
    {
        *child = nullptr;
    }
    return Guard(error, [&]() -> openusd_status
    {
        const HWND parent = static_cast<HWND>(parent_window);
        DWORD parent_process_id = 0;
        const DWORD parent_thread_id =
            GetWindowThreadProcessId(parent, &parent_process_id);
        if (parent == nullptr || IsWindow(parent) == FALSE ||
            parent_process_id != GetCurrentProcessId() ||
            plugin_path == nullptr || plugin_path[0] == '\0' ||
            stage == nullptr || child == nullptr ||
            width <= 0 || height <= 0 || dpi == 0)
        {
            WriteError(error, "A valid parent, plugin path, stage, size, DPI, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        if (parent_thread_id != GetCurrentThreadId())
        {
            WriteError(
                error,
                "The Storm child must be created on its parent HWND thread.");
            return OPENUSD_STATUS_WRONG_THREAD;
        }

        openusd_status status = openusd_stage_retain(stage, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        auto result = std::make_shared<ChildState>();
        result->stage = stage;
        result->creator_thread_id = GetCurrentThreadId();
        result->plugin_path = plugin_path;
        result->width.store(width, std::memory_order_relaxed);
        result->height.store(height, std::memory_order_relaxed);
        result->dpi.store(dpi, std::memory_order_relaxed);

        std::call_once(g_window_class_once, RegisterChildWindowClass);
        if (g_window_class == 0)
        {
            openusd_stage_release(stage);
            SetLastError(g_window_class_error);
            WriteError(error, Win32Error("RegisterClassW"));
            return OPENUSD_STATUS_NATIVE_ERROR;
        }

        result->window = CreateWindowExW(
            0,
            WindowClassName,
            L"",
            WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN |
                WS_TABSTOP,
            0,
            0,
            width,
            height,
            parent,
            nullptr,
            GetModuleHandleW(nullptr),
            result.get());
        if (result->window == nullptr)
        {
            openusd_stage_release(stage);
            WriteError(error, Win32Error("CreateWindowExW"));
            return OPENUSD_STATUS_NATIVE_ERROR;
        }

        result->render_thread = std::thread(RenderThreadMain, result.get());
        {
            std::unique_lock lock(result->gate);
            result->initialized.wait(
                lock,
                [&result] { return result->initialization_complete; });
            status = result->initialization_status;
            if (status != OPENUSD_STATUS_OK)
            {
                WriteError(error, result->initialization_error);
            }
        }
        if (status != OPENUSD_STATUS_OK)
        {
            result->render_thread.join();
            SetWindowLongPtrW(result->window, GWLP_USERDATA, 0);
            DestroyWindow(result->window);
            openusd_stage_release(stage);
            return status;
        }

        openusd_storm_child* token = RegisterChild(result);
        const size_t live =
            g_live_count.fetch_add(1, std::memory_order_relaxed) + 1;
        UpdatePeak(live);
        *child = token;
        return OPENUSD_STATUS_OK;
    });
}

extern "C" openusd_status openusd_storm_child_destroy(
    openusd_storm_child* child,
    openusd_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_status
    {
        std::shared_ptr<ChildState> state = LookupChild(child);
        openusd_status status = ValidateChild(state.get(), error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        status = RequireCreatorThread(state.get(), error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        std::lock_guard destroy_lock(state->destroy_gate);
        if (state->render_thread.joinable())
        {
            status = QueueStop(state.get(), error);
            if (status != OPENUSD_STATUS_OK)
            {
                return status;
            }
            state->render_thread.join();
        }
        if (IsFailpoint("destroy-window"))
        {
            WriteError(error, "Injected DestroyWindow failure.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        SetLastError(ERROR_SUCCESS);
        if (DestroyWindow(state->window) == FALSE)
        {
            WriteError(error, Win32Error("DestroyWindow"));
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        if (IsWindow(state->window) != FALSE ||
            !state->window_userdata_cleared.load(std::memory_order_acquire))
        {
            WriteError(error, "DestroyWindow left a live Storm child HWND.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        state->window = nullptr;
        openusd_stage_release(state->stage);
        state->stage = nullptr;
        if (!UnregisterChild(child, state))
        {
            WriteError(error, "The Storm child handle was already released.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        g_live_count.fetch_sub(1, std::memory_order_relaxed);
        return OPENUSD_STATUS_OK;
    });
}

extern "C" openusd_status openusd_storm_child_get_window(
    openusd_storm_child* child,
    void** window,
    openusd_error_buffer* error)
{
    if (window != nullptr)
    {
        *window = nullptr;
    }
    const std::shared_ptr<ChildState> state = LookupChild(child);
    const openusd_status status = ValidateChild(state.get(), error);
    if (status != OPENUSD_STATUS_OK || window == nullptr)
    {
        if (status == OPENUSD_STATUS_OK)
        {
            WriteError(error, "A window output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return status;
    }
    *window = state->window;
    return OPENUSD_STATUS_OK;
}

extern "C" openusd_status openusd_storm_child_get_renderer_name(
    openusd_storm_child* child,
    char* buffer,
    size_t capacity,
    size_t* required,
    openusd_error_buffer* error)
{
    const std::shared_ptr<ChildState> state = LookupChild(child);
    const openusd_status status = ValidateChild(state.get(), error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    if (required == nullptr)
    {
        WriteError(error, "A required-length output is required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    *required = state->renderer_name.size() + 1;
    if (buffer == nullptr || capacity < *required)
    {
        return OPENUSD_STATUS_BUFFER_TOO_SMALL;
    }
    std::memcpy(buffer, state->renderer_name.c_str(), *required);
    return OPENUSD_STATUS_OK;
}

extern "C" openusd_status openusd_storm_child_render(
    openusd_storm_child* child,
    double time_code,
    const openusd_render_camera* camera,
    uint64_t* frame_count,
    int32_t* converged,
    openusd_error_buffer* error)
{
    return openusd_storm_child_render_v2(
        child,
        time_code,
        camera,
        0,
        0,
        0,
        frame_count,
        converged,
        error);
}

extern "C" openusd_status openusd_storm_child_render_v2(
    openusd_storm_child* child,
    double time_code,
    const openusd_render_camera* camera,
    uint64_t state_revision,
    uint64_t scene_revision,
    uint32_t revision_flags,
    uint64_t* frame_count,
    int32_t* converged,
    openusd_error_buffer* error)
{
    if (frame_count != nullptr)
    {
        *frame_count = 0;
    }
    if (converged != nullptr)
    {
        *converged = 0;
    }
    if (frame_count == nullptr || converged == nullptr)
    {
        WriteError(error, "Frame-count and convergence outputs are required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return Guard(error, [&]
    {
        const std::shared_ptr<ChildState> state = LookupChild(child);
        return QueueCommand(
            state.get(),
            CommandKind::Render,
            time_code,
            camera,
            state_revision,
            scene_revision,
            revision_flags,
            true,
            frame_count,
            converged,
            error);
    });
}

extern "C" openusd_status openusd_storm_child_request_frame(
    openusd_storm_child* child,
    double time_code,
    const openusd_render_camera* camera,
    openusd_error_buffer* error)
{
    return openusd_storm_child_request_frame_v3(
        child,
        time_code,
        camera,
        0,
        0,
        0,
        error);
}

extern "C" openusd_status openusd_storm_child_request_frame_v2(
    openusd_storm_child* child,
    double time_code,
    const openusd_render_camera* camera,
    uint64_t revision,
    openusd_error_buffer* error)
{
    return openusd_storm_child_request_frame_v3(
        child,
        time_code,
        camera,
        revision,
        0,
        0,
        error);
}

extern "C" openusd_status openusd_storm_child_request_frame_v3(
    openusd_storm_child* child,
    double time_code,
    const openusd_render_camera* camera,
    uint64_t state_revision,
    uint64_t scene_revision,
    uint32_t revision_flags,
    openusd_error_buffer* error)
{
    return Guard(error, [&]
    {
        const std::shared_ptr<ChildState> state = LookupChild(child);
        return QueueCommand(
            state.get(),
            CommandKind::Render,
            time_code,
            camera,
            state_revision,
            scene_revision,
            revision_flags,
            false,
            nullptr,
            nullptr,
            error);
    });
}

extern "C" openusd_status openusd_storm_child_pick(
    openusd_storm_child* child,
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
    return Guard(error, [&]
    {
        const std::shared_ptr<ChildState> state = LookupChild(child);
        return QueuePick(
            state.get(),
            request,
            result,
            prim_path_buffer,
            prim_path_capacity,
            instancer_path_buffer,
            instancer_path_capacity,
            instance_context,
            instance_context_capacity,
            instance_context_paths_buffer,
            instance_context_paths_capacity,
            error);
    });
}

extern "C" openusd_status openusd_storm_child_set_selection(
    openusd_storm_child* child,
    const openusd_storm_selection_update* update,
    openusd_error_buffer* error)
{
    return Guard(error, [&]
    {
        const std::shared_ptr<ChildState> state = LookupChild(child);
        return QueueSelection(state.get(), update, error);
    });
}

extern "C" openusd_status openusd_storm_child_set_transform_overrides(
    openusd_storm_child* child,
    const openusd_storm_transform_override_update* update,
    openusd_storm_transform_override_diagnostics* diagnostics,
    openusd_error_buffer* error)
{
    return Guard(error, [&]
    {
        const std::shared_ptr<ChildState> state = LookupChild(child);
        return QueueTransformOverrides(state.get(), update, diagnostics, error);
    });
}

extern "C" openusd_status openusd_storm_child_set_deformation_overrides(
    openusd_storm_child* child,
    const openusd_storm_deformation_override_update* update,
    openusd_storm_deformation_override_diagnostics* diagnostics,
    openusd_error_buffer* error)
{
    return Guard(error, [&]
    {
        const std::shared_ptr<ChildState> state = LookupChild(child);
        return QueueDeformationOverrides(state.get(), update, diagnostics, error);
    });
}

extern "C" openusd_status openusd_storm_child_resize(
    openusd_storm_child* child,
    int32_t width,
    int32_t height,
    uint32_t dpi,
    openusd_error_buffer* error)
{
    const std::shared_ptr<ChildState> state = LookupChild(child);
    openusd_status status = ValidateChild(state.get(), error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    if (width <= 0 || height <= 0 || dpi == 0)
    {
        WriteError(error, "Positive width, height, and DPI values are required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    status = RequireCreatorThread(state.get(), error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    status = RequireRunning(state.get(), error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    state->width.store(width, std::memory_order_relaxed);
    state->height.store(height, std::memory_order_relaxed);
    state->dpi.store(dpi, std::memory_order_relaxed);
    if (SetWindowPos(
            state->window,
            nullptr,
            0,
            0,
            width,
            height,
            SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE) == FALSE)
    {
        WriteError(error, Win32Error("SetWindowPos"));
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    return OPENUSD_STATUS_OK;
}

extern "C" openusd_status openusd_storm_child_set_visible(
    openusd_storm_child* child,
    int32_t visible,
    openusd_error_buffer* error)
{
    const std::shared_ptr<ChildState> state = LookupChild(child);
    openusd_status status = ValidateChild(state.get(), error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    status = RequireCreatorThread(state.get(), error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    status = RequireRunning(state.get(), error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    const int32_t normalized = visible == 0 ? 0 : 1;
    state->visible.store(normalized, std::memory_order_relaxed);
    ShowWindow(state->window, normalized == 0 ? SW_HIDE : SW_SHOWNA);
    return OPENUSD_STATUS_OK;
}

extern "C" openusd_status openusd_storm_child_focus(
    openusd_storm_child* child,
    openusd_error_buffer* error)
{
    const std::shared_ptr<ChildState> state = LookupChild(child);
    openusd_status status = ValidateChild(state.get(), error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    status = RequireCreatorThread(state.get(), error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    status = RequireRunning(state.get(), error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    SetLastError(ERROR_SUCCESS);
    if (SetFocus(state->window) == nullptr && GetLastError() != ERROR_SUCCESS)
    {
        WriteError(error, Win32Error("SetFocus"));
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    return OPENUSD_STATUS_OK;
}

extern "C" openusd_status openusd_storm_child_simulate_context_loss(
    openusd_storm_child* child,
    openusd_error_buffer* error)
{
    return Guard(error, [&]
    {
        const std::shared_ptr<ChildState> state = LookupChild(child);
        return QueueCommand(
            state.get(),
            CommandKind::ContextLoss,
            0,
            nullptr,
            0,
            0,
            0,
            true,
            nullptr,
            nullptr,
            error);
    });
}

extern "C" openusd_status openusd_storm_child_get_diagnostics(
    openusd_storm_child* child,
    openusd_storm_child_diagnostics* diagnostics,
    openusd_error_buffer* error)
{
    if (diagnostics != nullptr)
    {
        std::memset(diagnostics, 0, sizeof(*diagnostics));
    }
    const std::shared_ptr<ChildState> state = LookupChild(child);
    const openusd_status status = ValidateChild(state.get(), error);
    if (status != OPENUSD_STATUS_OK || diagnostics == nullptr)
    {
        if (status == OPENUSD_STATUS_OK)
        {
            WriteError(error, "A diagnostics output is required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        return status;
    }
    diagnostics->frame_count =
        state->frame_count.load(std::memory_order_relaxed);
    diagnostics->pixel_signature =
        state->pixel_signature.load(std::memory_order_relaxed);
    diagnostics->pixel_sample_count =
        state->pixel_sample_count.load(std::memory_order_relaxed);
    diagnostics->focus_count =
        state->focus_count.load(std::memory_order_relaxed);
    diagnostics->pointer_count =
        state->pointer_count.load(std::memory_order_relaxed);
    diagnostics->wheel_count =
        state->wheel_count.load(std::memory_order_relaxed);
    diagnostics->key_count =
        state->key_count.load(std::memory_order_relaxed);
    diagnostics->context_generation =
        state->context_generation.load(std::memory_order_relaxed);
    diagnostics->coalesced_request_count =
        state->coalesced_request_count.load(std::memory_order_relaxed);
    diagnostics->cancelled_command_count =
        state->cancelled_command_count.load(std::memory_order_relaxed);
    diagnostics->teardown_fallback_count =
        state->teardown_fallback_count.load(std::memory_order_relaxed);
    diagnostics->latest_requested_revision =
        state->latest_requested_revision.load(std::memory_order_relaxed);
    diagnostics->latest_requested_camera_signature =
        state->latest_requested_camera_signature.load(std::memory_order_relaxed);
    diagnostics->latest_rendered_camera_signature =
        state->latest_rendered_camera_signature.load(std::memory_order_relaxed);
    diagnostics->render_thread_id =
        state->render_thread_id.load(std::memory_order_relaxed);
    diagnostics->creator_thread_id = state->creator_thread_id;
    {
        std::lock_guard lock(state->gate);
        diagnostics->pending_command_count = PendingCommandCount(state.get());
    }
    diagnostics->peak_pending_command_count =
        state->peak_pending_command_count.load(std::memory_order_relaxed);
    diagnostics->gl_major =
        state->gl_major.load(std::memory_order_relaxed);
    diagnostics->gl_minor =
        state->gl_minor.load(std::memory_order_relaxed);
    diagnostics->compatibility_profile =
        state->compatibility_profile.load(std::memory_order_relaxed);
    diagnostics->width = state->width.load(std::memory_order_relaxed);
    diagnostics->height = state->height.load(std::memory_order_relaxed);
    diagnostics->dpi = state->dpi.load(std::memory_order_relaxed);
    diagnostics->visible = state->visible.load(std::memory_order_relaxed);
    diagnostics->focused = state->focused.load(std::memory_order_relaxed);
    diagnostics->converged = state->converged.load(std::memory_order_relaxed);
    return OPENUSD_STATUS_OK;
}

extern "C" openusd_status openusd_storm_child_get_navigation_input(
    openusd_storm_child* child,
    openusd_storm_child_navigation_input* input,
    openusd_error_buffer* error)
{
    uint32_t requested_size = 0;
    uint32_t requested_version = 0;
    const bool valid_output = OpenUsdStormChildPrepareNavigationOutput(
        input,
        &requested_size,
        &requested_version);
    if (input == nullptr)
    {
        WriteError(error, "A navigation input output is required.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (!valid_output)
    {
        WriteError(
            error,
            "The navigation input struct size or version is unsupported.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const std::shared_ptr<ChildState> state = LookupChild(child);
    const openusd_status status = ValidateChild(state.get(), error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    OpenUsdStormChildCopyNavigationInput(&state->navigation, input);
    return OPENUSD_STATUS_OK;
}

extern "C" openusd_status openusd_storm_child_capture_framebuffer(
    openusd_storm_child* child,
    uint32_t background_rgba,
    uint8_t tolerance,
    uint32_t flags,
    uint8_t* rgba_buffer,
    size_t rgba_capacity,
    size_t* rgba_required,
    openusd_storm_child_framebuffer_capture* capture,
    openusd_error_buffer* error)
{
    if (rgba_required != nullptr)
    {
        *rgba_required = 0;
    }
    if (capture != nullptr)
    {
        std::memset(capture, 0, sizeof(*capture));
    }
    if (rgba_required == nullptr || capture == nullptr ||
        (rgba_buffer == nullptr && rgba_capacity != 0))
    {
        WriteError(
            error,
            "Framebuffer capture outputs and buffer arguments are invalid.");
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    return Guard(error, [&]
    {
        const std::shared_ptr<ChildState> state = LookupChild(child);
        return QueueCapture(
            state.get(),
            background_rgba,
            tolerance,
            flags,
            rgba_buffer,
            rgba_capacity,
            rgba_required,
            capture,
            error);
    });
}

extern "C" size_t openusd_storm_child_diagnostic_get_live_count(void)
{
    return g_live_count.load(std::memory_order_relaxed);
}

extern "C" size_t openusd_storm_child_diagnostic_get_peak_count(void)
{
    return g_peak_count.load(std::memory_order_relaxed);
}

extern "C" void openusd_storm_child_diagnostic_reset_peak_count(void)
{
    g_peak_count.store(
        g_live_count.load(std::memory_order_relaxed),
        std::memory_order_relaxed);
}
