// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_storm_child.h"
#include "openusd_storm_child_navigation.h"
#include "openusd_hydra.h"
#include "openusd_render_camera_internal.h"
#include "openusd_storm_child_pick.h"

#include <GL/gl.h>
#include <GL/glx.h>
#include <GL/glxext.h>
#include <X11/Xlib.h>
#include <X11/XKBlib.h>
#include <X11/Xproto.h>
#include <X11/keysym.h>
#include <X11/Xutil.h>
#include <sys/syscall.h>
#include <unistd.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <condition_variable>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <deque>
#include <exception>
#include <functional>
#include <limits>
#include <memory>
#include <mutex>
#include <new>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

namespace
{
constexpr int GlxContextMajorVersion = 0x2091;
constexpr int GlxContextMinorVersion = 0x2092;
constexpr int GlxContextProfileMask = 0x9126;
constexpr int GlxContextCompatibilityProfileBit = 0x00000002;
constexpr int GlxBadFramebufferConfig = 9;
constexpr GLenum GlMajorVersion = 0x821B;
constexpr GLenum GlMinorVersion = 0x821C;
constexpr GLenum GlContextProfileMask = 0x9126;
constexpr size_t MaximumCaptureBytes = 64u * 1024u * 1024u;

using GlxCreateContextAttribs =
    GLXContext (*)(Display*, GLXFBConfig, GLXContext, Bool, const int*);

enum class CommandKind
{
    Render,
    Capture,
    Pick,
    Selection,
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
    std::string error;
    std::condition_variable completion;
};

uint32_t CurrentThreadId() noexcept
{
    return static_cast<uint32_t>(syscall(SYS_gettid));
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

bool IsFailpoint(const char* value) noexcept
{
    const char* configured = std::getenv("OPENUSD_STORM_CHILD_FAILPOINT");
    return configured != nullptr && std::strcmp(configured, value) == 0;
}

std::string X11Error(const char* operation)
{
    return std::string(operation) + " failed.";
}
}

struct ChildState
{
    Display* display = nullptr;
    Window parent = None;
    std::atomic<Window> window{None};
    Colormap colormap = None;
    GLXFBConfig framebuffer_config = nullptr;
    XVisualInfo* visual = nullptr;
    GLXContext context = nullptr;
    GLuint capture_texture = 0;
    int32_t capture_width = 0;
    int32_t capture_height = 0;
    uint64_t captured_frame_count = 0;
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
    std::atomic_uint32_t render_thread_id{0};
    uint32_t creator_thread_id = 0;
    std::atomic_uint32_t peak_pending_command_count{0};
    std::atomic_int32_t gl_major{0};
    std::atomic_int32_t gl_minor{0};
    std::atomic_int32_t compatibility_profile{0};
    std::atomic_int32_t converged{0};
    bool detectable_auto_repeat = false;
    OpenUsdStormChildNavigationState navigation;
    openusd_render_camera latest_camera =
        openusd_render_camera_detail::Automatic();
};

struct openusd_storm_child
{
};

namespace
{
std::atomic_size_t g_live_count{0};
std::atomic_size_t g_peak_count{0};
std::atomic_uintptr_t g_next_token{0x10000};
std::mutex g_registry_gate;
std::unordered_map<openusd_storm_child*, std::shared_ptr<ChildState>> g_children;
enum class XDispatcherInitialization : int
{
    Uninitialized,
    Initialized,
    TooLate
};

std::mutex g_x_dispatcher_initialization_gate;
std::atomic<XDispatcherInitialization> g_x_dispatcher_initialization{
    XDispatcherInitialization::Uninitialized};
std::atomic<XErrorHandler> g_previous_x_error_handler{nullptr};
std::atomic_uint64_t g_x_errors_forwarded_while_active{0};
std::mutex g_x_error_gate;

struct XErrorState
{
    std::atomic_bool active{false};
    std::atomic_uintptr_t display{0};
    std::atomic_ulong first_serial{0};
    std::atomic_ulong last_serial{0};
    std::array<std::atomic_int, 4> expected_errors{};
    std::array<std::atomic_int, 4> expected_requests{};
    std::atomic_ulong expected_resource{None};
    std::atomic_bool match_resource{false};
    std::atomic_int captured{0};
    std::atomic_int unexpected{0};
    std::atomic_int error_code{0};
    std::atomic_int request_code{0};
    std::atomic_int minor_code{0};
    std::atomic_ulong resource_id{None};
    std::atomic_int unexpected_error_code{0};
    std::atomic_int unexpected_request_code{0};
    std::atomic_int unexpected_minor_code{0};
    std::atomic_ulong unexpected_resource_id{None};
};

XErrorState g_x_error_state;

bool ContainsExpected(
    const std::array<std::atomic_int, 4>& values,
    int value) noexcept
{
    if (values[0].load(std::memory_order_relaxed) == 0)
    {
        return true;
    }
    for (const std::atomic_int& expected : values)
    {
        if (expected.load(std::memory_order_relaxed) == value)
        {
            return true;
        }
    }
    return false;
}

void StoreXError(
    XErrorState& state,
    const XErrorEvent* event,
    std::atomic_int& destination,
    bool unexpected) noexcept
{
    int expected = 0;
    if (destination.compare_exchange_strong(
            expected,
            1,
            std::memory_order_acq_rel,
            std::memory_order_acquire))
    {
        if (!unexpected)
        {
            state.error_code.store(event->error_code, std::memory_order_relaxed);
            state.request_code.store(event->request_code, std::memory_order_relaxed);
            state.minor_code.store(event->minor_code, std::memory_order_relaxed);
            state.resource_id.store(event->resourceid, std::memory_order_relaxed);
        }
        else
        {
            state.unexpected_error_code.store(
                event->error_code,
                std::memory_order_relaxed);
            state.unexpected_request_code.store(
                event->request_code,
                std::memory_order_relaxed);
            state.unexpected_minor_code.store(
                event->minor_code,
                std::memory_order_relaxed);
            state.unexpected_resource_id.store(
                event->resourceid,
                std::memory_order_relaxed);
        }
        destination.store(2, std::memory_order_release);
    }
}

int DispatchXError(Display* display, XErrorEvent* event)
{
    XErrorState& state = g_x_error_state;
    if (state.active.load(std::memory_order_acquire))
    {
        const bool checked_request =
            reinterpret_cast<uintptr_t>(display) ==
                state.display.load(std::memory_order_relaxed) &&
            event->serial >=
                state.first_serial.load(std::memory_order_relaxed) &&
            event->serial <=
                state.last_serial.load(std::memory_order_acquire);
        const bool expected =
            checked_request &&
            ContainsExpected(state.expected_errors, event->error_code) &&
            ContainsExpected(state.expected_requests, event->request_code) &&
            (!state.match_resource.load(std::memory_order_relaxed) ||
             event->resourceid ==
                state.expected_resource.load(std::memory_order_relaxed));
        if (expected)
        {
            StoreXError(state, event, state.captured, false);
            return 0;
        }
        if (checked_request)
        {
            StoreXError(state, event, state.unexpected, true);
        }
        g_x_errors_forwarded_while_active.fetch_add(
            1,
            std::memory_order_relaxed);
    }
    XErrorHandler previous =
        g_previous_x_error_handler.load(std::memory_order_acquire);
    if (previous == nullptr || previous == DispatchXError)
    {
        std::abort();
    }
    return previous(display, event);
}

std::string DescribeXError(
    const char* operation,
    const XErrorState& state)
{
    const bool unexpected =
        state.unexpected.load(std::memory_order_acquire) == 2;
    return std::string(operation) + " raised X11 error " +
        std::to_string((unexpected
            ? state.unexpected_error_code
            : state.error_code).load(std::memory_order_relaxed)) +
        " (request " +
        std::to_string((unexpected
            ? state.unexpected_request_code
            : state.request_code).load(std::memory_order_relaxed)) +
        ", minor " +
        std::to_string((unexpected
            ? state.unexpected_minor_code
            : state.minor_code).load(std::memory_order_relaxed)) +
        ", resource " +
        std::to_string(static_cast<uint64_t>(
            (unexpected
                ? state.unexpected_resource_id
                : state.resource_id).load(std::memory_order_relaxed))) + ").";
}

void InjectUnrelatedXError()
{
    Display* display = XOpenDisplay(nullptr);
    if (display == nullptr)
    {
        return;
    }
    XWindowAttributes attributes{};
    XGetWindowAttributes(display, None, &attributes);
    XSync(display, False);
    XCloseDisplay(display);
}

struct ExpectedXError
{
    std::array<int, 4> errors{};
    std::array<int, 4> requests{};
    XID resource = None;
    bool match_resource = false;
};

ExpectedXError GlxContextCreationError(
    int request_code,
    int error_base)
{
    return {
        {BadValue, BadMatch, BadAlloc, error_base + GlxBadFramebufferConfig},
        {request_code, 0, 0, 0},
        None,
        false};
}

ExpectedXError WindowRequestError(
    int request,
    XID resource,
    std::array<int, 4> errors = {BadWindow, BadMatch, BadValue, 0})
{
    return {errors, {request, 0, 0, 0}, resource, true};
}

ExpectedXError ColormapRequestError(Window parent)
{
    return {
        {BadWindow, BadMatch, BadAlloc, 0},
        {X_CreateColormap, 0, 0, 0},
        parent,
        true};
}

ExpectedXError ParentValidationError(Window parent)
{
    return {
        {BadWindow, 0, 0, 0},
        {X_GetWindowAttributes, 0, 0, 0},
        parent,
        true};
}

ExpectedXError ChildDestroyError(Window window)
{
    return {
        {BadWindow, 0, 0, 0},
        {X_DestroyWindow, 0, 0, 0},
        window,
        true};
}

bool IsCapturedBadWindow(
    const XErrorState& state,
    int request,
    Window window) noexcept
{
    return state.captured.load(std::memory_order_acquire) == 2 &&
        state.error_code.load(std::memory_order_relaxed) == BadWindow &&
        state.request_code.load(std::memory_order_relaxed) == request &&
        state.resource_id.load(std::memory_order_relaxed) == window;
}

bool HasUnexpectedXError(const XErrorState& state) noexcept
{
    return state.unexpected.load(std::memory_order_acquire) == 2;
}

bool HasCapturedXError(const XErrorState& state) noexcept
{
    return state.captured.load(std::memory_order_acquire) == 2;
}

class ScopedXErrorTrap
{
public:
    ScopedXErrorTrap(
        Display* display,
        ExpectedXError expected)
        : _display(display),
          _lock(g_x_error_gate),
          _forwarded_before(
              g_x_errors_forwarded_while_active.load(
                  std::memory_order_relaxed))
    {
        // The dispatcher is process-lifetime. Only the checked request window
        // is serialized; the handler itself never takes this mutex.
        XSync(_display, False);
        XErrorState& state = g_x_error_state;
        if (state.active.load(std::memory_order_acquire))
        {
            std::abort();
        }
        state.captured.store(0, std::memory_order_relaxed);
        state.unexpected.store(0, std::memory_order_relaxed);
        state.display.store(
            reinterpret_cast<uintptr_t>(display),
            std::memory_order_relaxed);
        state.first_serial.store(NextRequest(display), std::memory_order_relaxed);
        state.last_serial.store(
            std::numeric_limits<unsigned long>::max(),
            std::memory_order_relaxed);
        for (size_t index = 0; index < expected.errors.size(); ++index)
        {
            state.expected_errors[index].store(
                expected.errors[index],
                std::memory_order_relaxed);
            state.expected_requests[index].store(
                expected.requests[index],
                std::memory_order_relaxed);
        }
        state.expected_resource.store(expected.resource, std::memory_order_relaxed);
        state.match_resource.store(expected.match_resource, std::memory_order_relaxed);
        state.active.store(true, std::memory_order_release);
    }

    ScopedXErrorTrap(const ScopedXErrorTrap&) = delete;
    ScopedXErrorTrap& operator=(const ScopedXErrorTrap&) = delete;

    ~ScopedXErrorTrap()
    {
        Finish();
    }

    void Finish()
    {
        if (!_finished)
        {
            if (IsFailpoint("xerror-trap-concurrency"))
            {
                const auto deadline =
                    std::chrono::steady_clock::now() +
                    std::chrono::seconds(1);
                while (g_x_errors_forwarded_while_active.load(
                           std::memory_order_relaxed) == _forwarded_before &&
                       std::chrono::steady_clock::now() < deadline)
                {
                    std::this_thread::yield();
                }
                if (g_x_errors_forwarded_while_active.load(
                        std::memory_order_relaxed) == _forwarded_before)
                {
                    g_x_error_state.unexpected_error_code.store(
                        0,
                        std::memory_order_relaxed);
                    g_x_error_state.unexpected_request_code.store(
                        0,
                        std::memory_order_relaxed);
                    g_x_error_state.unexpected_minor_code.store(
                        0,
                        std::memory_order_relaxed);
                    g_x_error_state.unexpected_resource_id.store(
                        None,
                        std::memory_order_relaxed);
                    g_x_error_state.unexpected.store(
                        2,
                        std::memory_order_release);
                }
            }
            const unsigned long next_serial = NextRequest(_display);
            g_x_error_state.last_serial.store(
                next_serial == 0 ? 0 : next_serial - 1,
                std::memory_order_release);
            XSync(_display, False);
            g_x_error_state.active.store(false, std::memory_order_release);
            _finished = true;
        }
    }

    std::string Describe(const char* operation) const
    {
        return DescribeXError(operation, g_x_error_state);
    }

    bool HasExpectedError() const noexcept
    {
        return HasCapturedXError(g_x_error_state);
    }

    bool HasUnexpectedError() const noexcept
    {
        return HasUnexpectedXError(g_x_error_state);
    }

    bool CapturedBadWindow(int request, Window window) const noexcept
    {
        return IsCapturedBadWindow(g_x_error_state, request, window);
    }

private:
    Display* _display;
    std::unique_lock<std::mutex> _lock;
    uint64_t _forwarded_before;
    bool _finished = false;
};

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

uint32_t XModifiers(unsigned int state) noexcept
{
    uint32_t modifiers = OPENUSD_STORM_CHILD_MODIFIER_NONE;
    if ((state & Mod1Mask) != 0)
    {
        modifiers |= OPENUSD_STORM_CHILD_MODIFIER_ALT;
    }
    if ((state & ShiftMask) != 0)
    {
        modifiers |= OPENUSD_STORM_CHILD_MODIFIER_SHIFT;
    }
    if ((state & ControlMask) != 0)
    {
        modifiers |= OPENUSD_STORM_CHILD_MODIFIER_CONTROL;
    }
    if ((state & Mod4Mask) != 0)
    {
        modifiers |= OPENUSD_STORM_CHILD_MODIFIER_META;
    }
    return modifiers;
}

uint32_t XButtons(
    unsigned int state,
    int event_type = 0,
    unsigned int event_button = 0) noexcept
{
    uint32_t buttons = OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE;
    if ((state & Button1Mask) != 0)
    {
        buttons |= OPENUSD_STORM_CHILD_POINTER_BUTTON_LEFT;
    }
    if ((state & Button2Mask) != 0)
    {
        buttons |= OPENUSD_STORM_CHILD_POINTER_BUTTON_MIDDLE;
    }
    if ((state & Button3Mask) != 0)
    {
        buttons |= OPENUSD_STORM_CHILD_POINTER_BUTTON_RIGHT;
    }
    uint32_t event_flag = OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE;
    if (event_button == Button1)
    {
        event_flag = OPENUSD_STORM_CHILD_POINTER_BUTTON_LEFT;
    }
    else if (event_button == Button2)
    {
        event_flag = OPENUSD_STORM_CHILD_POINTER_BUTTON_MIDDLE;
    }
    else if (event_button == Button3)
    {
        event_flag = OPENUSD_STORM_CHILD_POINTER_BUTTON_RIGHT;
    }
    if (event_type == ButtonPress)
    {
        buttons |= event_flag;
    }
    else if (event_type == ButtonRelease)
    {
        buttons &= ~event_flag;
    }
    return buttons;
}

void UpdateNavigationKey(
    ChildState* child,
    XKeyEvent* event,
    bool pressed,
    bool repeat_release)
{
    const KeySym key = XLookupKeysym(event, 0);
    std::lock_guard lock(child->navigation.gate);
    uint32_t modifiers = XModifiers(event->state);
    uint32_t changed = OPENUSD_STORM_CHILD_MODIFIER_NONE;
    uint32_t command_key = 0;
    uint64_t OpenUsdStormChildNavigationState::* command_counter = nullptr;
    if (key == XK_Alt_L || key == XK_Alt_R)
    {
        changed = OPENUSD_STORM_CHILD_MODIFIER_ALT;
    }
    else if (key == XK_Shift_L || key == XK_Shift_R)
    {
        changed = OPENUSD_STORM_CHILD_MODIFIER_SHIFT;
    }
    else if (key == XK_Control_L || key == XK_Control_R)
    {
        changed = OPENUSD_STORM_CHILD_MODIFIER_CONTROL;
    }
    else if (key == XK_Super_L || key == XK_Super_R ||
             key == XK_Meta_L || key == XK_Meta_R)
    {
        changed = OPENUSD_STORM_CHILD_MODIFIER_META;
    }
    else if (key == XK_f || key == XK_F)
    {
        command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_FRAME_SELECTED;
        command_counter =
            &OpenUsdStormChildNavigationState::frame_selected_press_count;
    }
    else if (key == XK_Home)
    {
        command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_RESET_AUTOMATIC;
        command_counter =
            &OpenUsdStormChildNavigationState::reset_automatic_press_count;
    }
    else if (key == XK_p || key == XK_P)
    {
        command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_TOGGLE_PROJECTION;
        command_counter =
            &OpenUsdStormChildNavigationState::toggle_projection_press_count;
    }
    if (pressed)
    {
        modifiers |= changed;
    }
    else if (!repeat_release)
    {
        modifiers &= ~changed;
    }
    child->navigation.modifiers = modifiers;
    if (command_counter != nullptr && !repeat_release)
    {
        const bool repeat =
            pressed &&
            (child->navigation.command_keys_down & command_key) != 0;
        OpenUsdStormChildNavigationUpdateCommandKeyLocked(
            &child->navigation,
            command_key,
            pressed,
            repeat,
            command_counter);
    }
    OpenUsdStormChildNavigationAdvance(&child->navigation);
}

bool IsLegacyAutoRepeatRelease(
    ChildState* child,
    const XKeyEvent& release)
{
    if (child->detectable_auto_repeat || XPending(child->display) == 0)
    {
        return false;
    }
    XEvent next{};
    XPeekEvent(child->display, &next);
    return next.type == KeyPress &&
        next.xkey.window == release.window &&
        next.xkey.keycode == release.keycode &&
        next.xkey.time == release.time &&
        next.xany.send_event == False;
}

void PumpEvents(ChildState* child)
{
    while (XPending(child->display) > 0)
    {
        XEvent event{};
        XNextEvent(child->display, &event);
        if (event.xany.window != child->window.load(std::memory_order_relaxed) ||
            event.xany.send_event != False)
        {
            continue;
        }
        switch (event.type)
        {
            case FocusIn:
                child->focused.store(1, std::memory_order_relaxed);
                child->focus_count.fetch_add(1, std::memory_order_relaxed);
                OpenUsdStormChildNavigationSetFocus(&child->navigation, true);
                break;
            case FocusOut:
                child->focused.store(0, std::memory_order_relaxed);
                OpenUsdStormChildNavigationSetFocus(&child->navigation, false);
                break;
            case EnterNotify:
                OpenUsdStormChildNavigationSetInside(&child->navigation, true);
                break;
            case LeaveNotify:
                OpenUsdStormChildNavigationSetInside(&child->navigation, false);
                break;
            case MotionNotify:
                child->pointer_count.fetch_add(1, std::memory_order_relaxed);
                OpenUsdStormChildNavigationUpdatePointer(
                    &child->navigation,
                    event.xmotion.x,
                    event.xmotion.y,
                    XButtons(event.xmotion.state),
                    XModifiers(event.xmotion.state));
                break;
            case ButtonPress:
            case ButtonRelease:
            {
                child->pointer_count.fetch_add(1, std::memory_order_relaxed);
                const uint32_t buttons = XButtons(
                    event.xbutton.state,
                    event.type,
                    event.xbutton.button);
                const uint32_t modifiers = XModifiers(event.xbutton.state);
                if (event.type == ButtonPress &&
                    (event.xbutton.button == Button4 ||
                     event.xbutton.button == Button5))
                {
                    child->wheel_count.fetch_add(1, std::memory_order_relaxed);
                    OpenUsdStormChildNavigationAddWheel(
                        &child->navigation,
                        event.xbutton.button == Button4 ? 1.0 : -1.0,
                        event.xbutton.x,
                        event.xbutton.y,
                        buttons,
                        modifiers);
                }
                else
                {
                    OpenUsdStormChildNavigationUpdatePointer(
                        &child->navigation,
                        event.xbutton.x,
                        event.xbutton.y,
                        buttons,
                        modifiers);
                }
                break;
            }
            case KeyPress:
            case KeyRelease:
            {
                child->key_count.fetch_add(1, std::memory_order_relaxed);
                const bool repeat_release =
                    event.type == KeyRelease &&
                    IsLegacyAutoRepeatRelease(child, event.xkey);
                UpdateNavigationKey(
                    child,
                    &event.xkey,
                    event.type == KeyPress,
                    repeat_release);
                break;
            }
            default:
                break;
        }
    }
}

openusd_status CreateContext(ChildState* child, std::string& error)
{
    int glx_request_code = 0;
    int glx_event_code = 0;
    int glx_error_code = 0;
    if (XQueryExtension(
            child->display,
            "GLX",
            &glx_request_code,
            &glx_event_code,
            &glx_error_code) == False)
    {
        error = "The GLX extension is unavailable.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    const auto address = glXGetProcAddressARB(
        reinterpret_cast<const GLubyte*>("glXCreateContextAttribsARB"));
    if (address == nullptr)
    {
        error = "GLX_ARB_create_context is unavailable.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    GlxCreateContextAttribs create_context = nullptr;
    static_assert(sizeof(create_context) == sizeof(address));
    std::memcpy(&create_context, &address, sizeof(create_context));

    for (const int minor : {6, 5})
    {
        const bool inject_46_error =
            minor == 6 &&
            (IsFailpoint("glx-context-46-xerror") ||
             IsFailpoint("glx-context-46-xerror-and-unrelated"));
        const int attributes[] =
        {
            GlxContextMajorVersion,
            4,
            GlxContextMinorVersion,
            inject_46_error ? 99 : minor,
            GlxContextProfileMask,
            GlxContextCompatibilityProfileBit,
            None
        };
        ScopedXErrorTrap trap(
            child->display,
            GlxContextCreationError(glx_request_code, glx_error_code));
        GLXContext candidate = create_context(
            child->display,
            child->framebuffer_config,
            nullptr,
            True,
            attributes);
        if (inject_46_error &&
            IsFailpoint("glx-context-46-xerror-and-unrelated"))
        {
            InjectUnrelatedXError();
        }
        trap.Finish();
        if (trap.HasUnexpectedError())
        {
            error = trap.Describe("glXCreateContextAttribsARB");
            if (candidate != nullptr)
            {
                glXDestroyContext(child->display, candidate);
            }
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        if (!trap.HasExpectedError() && candidate != nullptr)
        {
            child->context = candidate;
            break;
        }
        if (candidate != nullptr)
        {
            glXDestroyContext(child->display, candidate);
        }
    }
    if (child->context == nullptr ||
        glXMakeCurrent(
            child->display,
            child->window.load(std::memory_order_relaxed),
            child->context) == False)
    {
        error = "A GLX 4.6 or 4.5 compatibility context could not be created.";
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
        (profile & GlxContextCompatibilityProfileBit) != 0 ? 1 : 0,
        std::memory_order_relaxed);
    return OPENUSD_STATUS_OK;
}

openusd_status DestroyContextChecked(ChildState* child, std::string& error)
{
    if (child->capture_texture != 0 &&
        glXGetCurrentContext() == child->context)
    {
        glDeleteTextures(1, &child->capture_texture);
    }
    child->capture_texture = 0;
    child->capture_width = 0;
    child->capture_height = 0;
    child->captured_frame_count = 0;
    if (child->context != nullptr &&
        glXGetCurrentContext() == child->context &&
        (IsFailpoint("glx-unbind") ||
         glXMakeCurrent(child->display, None, nullptr) == False))
    {
        error = IsFailpoint("glx-unbind")
            ? "Injected glXMakeCurrent teardown failure."
            : X11Error("glXMakeCurrent");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    if (child->context != nullptr)
    {
        if (IsFailpoint("context-delete"))
        {
            error = "Injected glXDestroyContext failure.";
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        glXDestroyContext(child->display, child->context);
        child->context = nullptr;
        XSync(child->display, False);
    }
    return OPENUSD_STATUS_OK;
}

void DestroyContextBestEffort(ChildState* child) noexcept
{
    if (child->capture_texture != 0 &&
        glXGetCurrentContext() == child->context)
    {
        glDeleteTextures(1, &child->capture_texture);
        child->capture_texture = 0;
    }
    if (child->context != nullptr &&
        glXGetCurrentContext() == child->context)
    {
        glXMakeCurrent(child->display, None, nullptr);
    }
    if (child->context != nullptr)
    {
        glXDestroyContext(child->display, child->context);
        child->context = nullptr;
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

openusd_status CreateRenderer(ChildState* child, std::string& error)
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
    PumpEvents(child);
    char error_bytes[4096]{};
    openusd_error_buffer native_error{
        error_bytes,
        sizeof(error_bytes),
        0};
    const int32_t width =
        std::max<int32_t>(1, child->width.load(std::memory_order_relaxed));
    const int32_t height =
        std::max<int32_t>(1, child->height.load(std::memory_order_relaxed));
    openusd_status status = openusd_storm_render_v2(
        child->renderer,
        width,
        height,
        0,
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
    GLint texture_binding = 0;
    GLint read_buffer = GL_BACK;
    glGetIntegerv(GL_TEXTURE_BINDING_2D, &texture_binding);
    glGetIntegerv(GL_READ_BUFFER, &read_buffer);
    glReadBuffer(GL_BACK);
    if (child->capture_texture == 0)
    {
        glGenTextures(1, &child->capture_texture);
    }
    glBindTexture(GL_TEXTURE_2D, child->capture_texture);
    if (child->capture_width != width || child->capture_height != height)
    {
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
        glTexImage2D(
            GL_TEXTURE_2D,
            0,
            GL_RGBA8,
            width,
            height,
            0,
            GL_RGBA,
            GL_UNSIGNED_BYTE,
            nullptr);
        child->capture_width = width;
        child->capture_height = height;
    }
    glCopyTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, 0, 0, width, height);
    uint8_t pixel[4]{};
    glReadPixels(
        width / 2,
        height / 2,
        1,
        1,
        GL_RGBA,
        GL_UNSIGNED_BYTE,
        pixel);
    glBindTexture(GL_TEXTURE_2D, static_cast<GLuint>(texture_binding));
    glReadBuffer(static_cast<GLenum>(read_buffer));
    const GLenum preserve_error = glGetError();
    if (preserve_error != GL_NO_ERROR)
    {
        error = "Preserving the completed Storm frame failed with error " +
            std::to_string(preserve_error) + ".";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    const uint64_t signature =
        static_cast<uint64_t>(pixel[0]) |
        (static_cast<uint64_t>(pixel[1]) << 8u) |
        (static_cast<uint64_t>(pixel[2]) << 16u) |
        (static_cast<uint64_t>(pixel[3]) << 24u);
    child->pixel_signature.store(signature, std::memory_order_relaxed);
    child->pixel_sample_count.fetch_add(1, std::memory_order_relaxed);
    glXSwapBuffers(
        child->display,
        child->window.load(std::memory_order_relaxed));
    frame_count =
        child->frame_count.fetch_add(1, std::memory_order_relaxed) + 1;
    child->captured_frame_count = frame_count;
    if (IsFailpoint("post-swap-back-corrupt"))
    {
        GLint draw_buffer = GL_BACK;
        GLfloat clear_color[4]{};
        glGetIntegerv(GL_DRAW_BUFFER, &draw_buffer);
        glGetFloatv(GL_COLOR_CLEAR_VALUE, clear_color);
        const GLboolean scissor_enabled = glIsEnabled(GL_SCISSOR_TEST);
        glDrawBuffer(GL_BACK);
        glDisable(GL_SCISSOR_TEST);
        glClearColor(0.0F, 1.0F, 0.0F, 1.0F);
        glClear(GL_COLOR_BUFFER_BIT);
        glFlush();
        glDrawBuffer(static_cast<GLenum>(draw_buffer));
        glClearColor(
            clear_color[0],
            clear_color[1],
            clear_color[2],
            clear_color[3]);
        if (scissor_enabled != False)
        {
            glEnable(GL_SCISSOR_TEST);
        }
    }
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

void CreateCaptureTestPattern(
    int32_t width,
    int32_t height,
    uint32_t background_rgba,
    std::vector<uint8_t>& pixels)
{
    const uint8_t background[4]{
        RgbaChannel(background_rgba, 0),
        RgbaChannel(background_rgba, 1),
        RgbaChannel(background_rgba, 2),
        RgbaChannel(background_rgba, 3)};
    for (size_t offset = 0; offset < pixels.size(); offset += 4)
    {
        std::copy_n(background, 4, pixels.begin() + offset);
    }
    const int32_t left = width / 4;
    const int32_t bottom = height / 4;
    const int32_t right = left + std::max<int32_t>(1, width / 2);
    const int32_t top = bottom + std::max<int32_t>(1, height / 2);
    for (int32_t y = bottom; y < top; ++y)
    {
        for (int32_t x = left; x < right; ++x)
        {
            const size_t offset =
                (static_cast<size_t>(y) * static_cast<size_t>(width) +
                 static_cast<size_t>(x)) * 4u;
            pixels[offset] = 255;
            pixels[offset + 1] = 0;
            pixels[offset + 2] = 255;
            pixels[offset + 3] = 255;
        }
    }
}

openusd_status CaptureFramebuffer(ChildState* child, Command* command)
{
    if (child->captured_frame_count == 0 || child->capture_texture == 0)
    {
        command->error =
            "A completed Storm frame is required before framebuffer capture.";
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if (CurrentThreadId() !=
            child->render_thread_id.load(std::memory_order_relaxed) ||
        glXGetCurrentContext() != child->context ||
        glXGetCurrentDrawable() !=
            child->window.load(std::memory_order_relaxed))
    {
        command->error =
            "Framebuffer capture requires the child render thread and current GLX context.";
        return OPENUSD_STATUS_WRONG_THREAD;
    }
    if ((command->capture_flags &
         ~OPENUSD_STORM_CHILD_CAPTURE_DRAW_TEST_PATTERN) != 0)
    {
        command->error = "The framebuffer capture flags are invalid.";
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const int32_t width = child->capture_width;
    const int32_t height = child->capture_height;
    const size_t pixel_count =
        static_cast<size_t>(width) * static_cast<size_t>(height);
    if (pixel_count > MaximumCaptureBytes / 4u)
    {
        command->error =
            "The framebuffer exceeds the 64 MiB diagnostic capture limit.";
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    std::vector<uint8_t> pixels(pixel_count * 4u);
    while (glGetError() != GL_NO_ERROR) {}
    if ((command->capture_flags &
         OPENUSD_STORM_CHILD_CAPTURE_DRAW_TEST_PATTERN) != 0)
    {
        CreateCaptureTestPattern(
            width,
            height,
            command->capture_background_rgba,
            pixels);
    }
    else
    {
        GLint pack_alignment = 4;
        GLint texture_binding = 0;
        glGetIntegerv(GL_PACK_ALIGNMENT, &pack_alignment);
        glGetIntegerv(GL_TEXTURE_BINDING_2D, &texture_binding);
        glPixelStorei(GL_PACK_ALIGNMENT, 1);
        glBindTexture(GL_TEXTURE_2D, child->capture_texture);
        glGetTexImage(
            GL_TEXTURE_2D,
            0,
            GL_RGBA,
            GL_UNSIGNED_BYTE,
            pixels.data());
        const GLenum read_error = glGetError();
        glBindTexture(GL_TEXTURE_2D, static_cast<GLuint>(texture_binding));
        glPixelStorei(GL_PACK_ALIGNMENT, pack_alignment);
        if (read_error != GL_NO_ERROR)
        {
            command->error = "Reading the preserved Storm frame failed with error " +
                std::to_string(read_error) + ".";
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
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
    command->capture.frame_count = child->captured_frame_count;
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
    command->capture.read_buffer =
        OPENUSD_STORM_CHILD_CAPTURE_READ_PRESERVED_TEXTURE;
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
        if (glXMakeCurrent(child->display, None, nullptr) == False)
        {
            error = X11Error("glXMakeCurrent");
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
            glXMakeCurrent(
                child->display,
                child->window.load(std::memory_order_relaxed),
                child->context);
            error = IsFailpoint("storm-abandon")
                ? "Injected Storm abandon failure."
                : error_bytes;
            return abandon_status;
        }
        child->renderer = nullptr;
        child->teardown_fallback_count.fetch_add(1, std::memory_order_relaxed);
    }
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
            if (glXGetCurrentContext() == child->context &&
                glXMakeCurrent(child->display, None, nullptr) == False)
            {
                error = destroy_message + " " + X11Error("glXMakeCurrent");
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
                if (child->context != nullptr)
                {
                    glXMakeCurrent(
                        child->display,
                        child->window.load(std::memory_order_relaxed),
                        child->context);
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
    return DestroyContextChecked(child, error);
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

void FailPendingCommands(
    ChildState* child,
    openusd_status status,
    const std::string& error)
{
    for (const std::shared_ptr<Command>& pending :
         child->synchronous_commands)
    {
        if (pending->pick != nullptr)
        {
            pending->pick->Cancel(
                child->context_generation.load(std::memory_order_relaxed));
        }
        pending->status = status;
        pending->error = error;
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
    child->render_thread_id.store(CurrentThreadId(), std::memory_order_relaxed);
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
        if (status != OPENUSD_STATUS_OK)
        {
            child->lifecycle = LifecycleState::Stopped;
            FailPendingCommands(child, status, error);
        }
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
        PumpEvents(child);
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
    if (child == nullptr || child->display == nullptr ||
        child->window.load(std::memory_order_relaxed) == None)
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
    if (CurrentThreadId() != child->creator_thread_id)
    {
        WriteError(
            error,
            "The Storm child XID must be operated on its creator thread.");
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

openusd_status WaitForInitialization(
    ChildState* child,
    openusd_error_buffer* error)
{
    std::unique_lock lock(child->gate);
    child->initialized.wait(
        lock,
        [child] { return child->initialization_complete; });
    if (child->initialization_status != OPENUSD_STATUS_OK)
    {
        WriteError(error, child->initialization_error);
        return child->initialization_status;
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
    if (child->initialization_complete &&
        child->initialization_status != OPENUSD_STATUS_OK)
    {
        WriteError(error, child->initialization_error);
        return child->initialization_status;
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

bool SelectFramebufferConfig(ChildState* child, std::string& error)
{
    const int attributes[] =
    {
        GLX_X_RENDERABLE, True,
        GLX_DRAWABLE_TYPE, GLX_WINDOW_BIT,
        GLX_RENDER_TYPE, GLX_RGBA_BIT,
        GLX_X_VISUAL_TYPE, GLX_TRUE_COLOR,
        GLX_RED_SIZE, 8,
        GLX_GREEN_SIZE, 8,
        GLX_BLUE_SIZE, 8,
        GLX_ALPHA_SIZE, 8,
        GLX_DEPTH_SIZE, 24,
        GLX_STENCIL_SIZE, 8,
        GLX_DOUBLEBUFFER, True,
        None
    };
    int count = 0;
    GLXFBConfig* configs = glXChooseFBConfig(
        child->display,
        DefaultScreen(child->display),
        attributes,
        &count);
    if (configs == nullptr || count == 0)
    {
        error = "No double-buffered GLX framebuffer configuration is available.";
        if (configs != nullptr)
        {
            XFree(configs);
        }
        return false;
    }
    child->framebuffer_config = configs[0];
    child->visual = glXGetVisualFromFBConfig(
        child->display,
        child->framebuffer_config);
    XFree(configs);
    if (child->visual == nullptr)
    {
        error = "The GLX framebuffer configuration has no X11 visual.";
        return false;
    }
    return true;
}
}

extern "C" uint32_t openusd_storm_child_get_abi_version(void)
{
    return OPENUSD_STORM_CHILD_ABI_VERSION;
}

extern "C" openusd_status openusd_storm_child_initialize_linux(
    openusd_error_buffer* error)
{
    return Guard(error, [&]() -> openusd_status
    {
        std::lock_guard lock(g_x_dispatcher_initialization_gate);
        const XDispatcherInitialization initialization =
            g_x_dispatcher_initialization.load(std::memory_order_acquire);
        if (initialization == XDispatcherInitialization::TooLate)
        {
            WriteError(
                error,
                "The Linux X11 error dispatcher was initialized too late. "
                "Call openusd_storm_child_initialize_linux immediately after "
                "XInitThreads and before any other Xlib call.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }

        XErrorHandler previous = XSetErrorHandler(DispatchXError);
        if (previous != DispatchXError)
        {
            g_previous_x_error_handler.store(previous, std::memory_order_release);
        }

        if (initialization == XDispatcherInitialization::Initialized)
        {
            return OPENUSD_STATUS_OK;
        }

        g_x_dispatcher_initialization.store(
            XDispatcherInitialization::Initialized,
            std::memory_order_release);
        return OPENUSD_STATUS_OK;
    });
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
        if (g_x_dispatcher_initialization.load(std::memory_order_acquire) !=
            XDispatcherInitialization::Initialized)
        {
            XDispatcherInitialization expected =
                XDispatcherInitialization::Uninitialized;
            g_x_dispatcher_initialization.compare_exchange_strong(
                expected,
                XDispatcherInitialization::TooLate,
                std::memory_order_acq_rel,
                std::memory_order_acquire);
            WriteError(
                error,
                "The Linux X11 error dispatcher is not initialized. Call "
                "openusd_storm_child_initialize_linux immediately after "
                "XInitThreads and before any other Xlib call.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        const Window parent = static_cast<Window>(
            reinterpret_cast<uintptr_t>(parent_window));
        if (parent == None || plugin_path == nullptr ||
            plugin_path[0] == '\0' || stage == nullptr || child == nullptr ||
            width <= 0 || height <= 0 || dpi == 0)
        {
            WriteError(
                error,
                "A valid X11 parent, plugin path, stage, size, DPI, and output are required.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        openusd_status status = openusd_stage_retain(stage, error);
        if (status != OPENUSD_STATUS_OK)
        {
            return status;
        }
        auto result = std::make_shared<ChildState>();
        result->display = XOpenDisplay(nullptr);
        if (result->display == nullptr)
        {
            openusd_stage_release(stage);
            WriteError(error, "XOpenDisplay failed for the active DISPLAY.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        Bool detectable_auto_repeat = False;
        result->detectable_auto_repeat =
            XkbSetDetectableAutoRepeat(
                result->display,
                True,
                &detectable_auto_repeat) != False &&
            detectable_auto_repeat != False;
        XWindowAttributes parent_attributes{};
        ScopedXErrorTrap parent_trap(
            result->display,
            ParentValidationError(parent));
        const int parent_result = XGetWindowAttributes(
            result->display,
            parent,
            &parent_attributes);
        parent_trap.Finish();
        if (parent_trap.HasUnexpectedError())
        {
            XCloseDisplay(result->display);
            openusd_stage_release(stage);
            WriteError(
                error,
                parent_trap.Describe("XGetWindowAttributes"));
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        if (parent_trap.HasExpectedError() || parent_result == 0)
        {
            XCloseDisplay(result->display);
            openusd_stage_release(stage);
            WriteError(error, "The parent XID is not valid on the active DISPLAY.");
            return OPENUSD_STATUS_INVALID_ARGUMENT;
        }
        result->stage = stage;
        result->parent = parent;
        result->creator_thread_id = CurrentThreadId();
        result->plugin_path = plugin_path;
        result->width.store(width, std::memory_order_relaxed);
        result->height.store(height, std::memory_order_relaxed);
        result->dpi.store(dpi, std::memory_order_relaxed);
        std::string selection_error;
        if (!SelectFramebufferConfig(result.get(), selection_error))
        {
            XCloseDisplay(result->display);
            openusd_stage_release(stage);
            WriteError(error, selection_error);
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        {
            ScopedXErrorTrap colormap_trap(
                result->display,
                ColormapRequestError(parent));
            result->colormap = XCreateColormap(
                result->display,
                parent,
                result->visual->visual,
                AllocNone);
            colormap_trap.Finish();
            if (colormap_trap.HasExpectedError() ||
                colormap_trap.HasUnexpectedError() ||
                result->colormap == None)
            {
                XFree(result->visual);
                XCloseDisplay(result->display);
                openusd_stage_release(stage);
                WriteError(error, colormap_trap.Describe("XCreateColormap"));
                return OPENUSD_STATUS_NATIVE_ERROR;
            }
        }
        XSetWindowAttributes attributes{};
        attributes.colormap = result->colormap;
        attributes.background_pixel = 0;
        attributes.border_pixel = 0;
        attributes.event_mask =
            FocusChangeMask | PointerMotionMask | ButtonPressMask |
            ButtonReleaseMask | KeyPressMask | KeyReleaseMask |
            EnterWindowMask | LeaveWindowMask | StructureNotifyMask |
            ExposureMask;
        const bool inject_window_error =
            IsFailpoint("xcreate-window-xerror");
        const Window request_parent =
            inject_window_error ? None : parent;
        ScopedXErrorTrap window_trap(
            result->display,
            WindowRequestError(X_CreateWindow, request_parent));
        const Window created = XCreateWindow(
            result->display,
            request_parent,
            0,
            0,
            static_cast<unsigned int>(width),
            static_cast<unsigned int>(height),
            0,
            result->visual->depth,
            InputOutput,
            result->visual->visual,
            CWColormap | CWBackPixel | CWBorderPixel | CWEventMask,
            &attributes);
        if (created != None && !inject_window_error)
        {
            XStoreName(result->display, created, "OpenUSD Storm GLX child");
            XMapWindow(result->display, created);
        }
        window_trap.Finish();
        if (window_trap.HasExpectedError() ||
            window_trap.HasUnexpectedError() ||
            created == None)
        {
            XFreeColormap(result->display, result->colormap);
            XFree(result->visual);
            XCloseDisplay(result->display);
            openusd_stage_release(stage);
            WriteError(error, window_trap.Describe("XCreateWindow/XMapWindow"));
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        result->window.store(created, std::memory_order_relaxed);

        result->render_thread = std::thread(RenderThreadMain, result.get());
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
            bool initialization_failed = false;
            {
                std::lock_guard lock(state->gate);
                initialization_failed =
                    state->initialization_complete &&
                    state->initialization_status != OPENUSD_STATUS_OK;
            }
            if (!initialization_failed)
            {
                status = QueueStop(state.get(), error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
            }
            state->render_thread.join();
        }
        if (IsFailpoint("destroy-window"))
        {
            WriteError(error, "Injected XDestroyWindow failure.");
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        const Window window =
            state->window.load(std::memory_order_relaxed);
        const Window destroy_target =
            IsFailpoint("destroy-window-xerror") ? None : window;
        ScopedXErrorTrap destroy_trap(
            state->display,
            ChildDestroyError(destroy_target));
        XDestroyWindow(state->display, destroy_target);
        destroy_trap.Finish();
        const bool already_destroyed =
            destroy_target == window &&
            destroy_trap.CapturedBadWindow(X_DestroyWindow, window);
        if (destroy_trap.HasUnexpectedError() ||
            (destroy_trap.HasExpectedError() && !already_destroyed))
        {
            WriteError(error, destroy_trap.Describe("XDestroyWindow"));
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        state->window.store(None, std::memory_order_relaxed);
        if (state->colormap != None)
        {
            XFreeColormap(state->display, state->colormap);
            state->colormap = None;
        }
        if (state->visual != nullptr)
        {
            XFree(state->visual);
            state->visual = nullptr;
        }
        XCloseDisplay(state->display);
        state->display = nullptr;
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
    *window = reinterpret_cast<void*>(
        static_cast<uintptr_t>(
            state->window.load(std::memory_order_relaxed)));
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
    const openusd_status initialization_status =
        WaitForInitialization(state.get(), error);
    if (initialization_status != OPENUSD_STATUS_OK)
    {
        return initialization_status;
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
    XResizeWindow(
        state->display,
        state->window.load(std::memory_order_relaxed),
        static_cast<unsigned int>(width),
        static_cast<unsigned int>(height));
    XSync(state->display, False);
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
    if (normalized == 0)
    {
        XUnmapWindow(
            state->display,
            state->window.load(std::memory_order_relaxed));
    }
    else
    {
        XMapWindow(
            state->display,
            state->window.load(std::memory_order_relaxed));
    }
    XSync(state->display, False);
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
    XSetInputFocus(
        state->display,
        state->window.load(std::memory_order_relaxed),
        RevertToParent,
        CurrentTime);
    XSync(state->display, False);
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
