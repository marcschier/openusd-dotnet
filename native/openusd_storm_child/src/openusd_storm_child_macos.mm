// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_storm_child.h"
#include "openusd_storm_child_macos_input.h"
#include "openusd_storm_child_navigation.h"
#include "openusd_hydra.h"
#include "openusd_render_camera_internal.h"
#include "openusd_storm_child_pick.h"

#import <AppKit/AppKit.h>
#import <OpenGL/gl3.h>
#import <dispatch/dispatch.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <deque>
#include <exception>
#include <memory>
#include <mutex>
#include <limits>
#include <pthread.h>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

namespace
{
constexpr size_t MaximumCaptureBytes = 64u * 1024u * 1024u;
constexpr const char* ExpectedRendererName = "Storm / Metal";
constexpr const char* PresentationSuffix =
    " + OpenGL 4.1 core presentation";

enum class CommandKind
{
    Render,
    Capture,
    Pick,
    Selection,
    ContextLoss,
    Stop
};
enum class LifecycleState { Running, Closing, Stopped };

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

std::atomic_size_t g_live_count{0};
std::atomic_size_t g_peak_count{0};
std::atomic_uintptr_t g_next_token{0x10000};

uint32_t CurrentThreadId() noexcept
{
    return pthread_mach_thread_np(pthread_self());
}

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
        WriteError(error, "Unknown native macOS Storm child exception.");
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
}

bool IsFailpoint(const char* value) noexcept
{
    const char* configured = std::getenv("OPENUSD_STORM_CHILD_FAILPOINT");
    return configured != nullptr && std::strcmp(configured, value) == 0;
}
}

// Objective-C declarations must live at global scope. Declaring the view inside the
// anonymous namespace above bound its ivar to a namespace-local ChildState that is never
// defined, so the global @implementation below could not see openUsdState at all.
struct ChildState;

@interface OpenUsdStormChildView : NSView
{
@public
    ChildState* openUsdState;
    NSTrackingArea* openUsdTrackingArea;
}
@end

struct ChildState
{
    OpenUsdStormChildView* view = nil;
    NSOpenGLContext* context = nil;
    openusd_stage* stage = nullptr;
    openusd_storm_renderer* renderer = nullptr;
    std::string plugin_path;
    std::string renderer_name;
    std::thread render_thread;
    std::mutex destroy_gate;
    std::mutex context_gate;
    std::mutex recovery_test_gate;
    std::mutex gate;
    std::condition_variable commands_available;
    std::condition_variable initialized;
    std::condition_variable recovery_test_changed;
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
    uint64_t context_generation = 0;
    std::atomic_uint64_t coalesced_request_count{0};
    std::atomic_uint64_t cancelled_command_count{0};
    std::atomic_uint64_t teardown_fallback_count{0};
    std::atomic_uint64_t latest_requested_revision{0};
    std::atomic_uint64_t latest_requested_camera_signature{0};
    std::atomic_uint64_t latest_rendered_camera_signature{0};
    std::atomic_uint32_t render_thread_id{0};
    uint32_t creator_thread_id = 0;
    std::atomic_uint32_t peak_pending_command_count{0};
    bool drawable_attached = false;
    bool context_update_required = false;
    std::atomic_int32_t gl_major{0};
    std::atomic_int32_t gl_minor{0};
    std::atomic_int32_t compatibility_profile{0};
    std::atomic_int32_t converged{0};
    OpenUsdStormChildNavigationState navigation;
    uint64_t resize_generation = 0;
    uint64_t context_update_generation = 0;
    uint64_t rendered_resize_generation = 0;
    uint64_t renderer_context_generation = 0;
    uint64_t preserved_context_generation = 0;
    uint64_t recovery_generation = 0;
    bool recovering = false;
    bool first_recovery_frame_pending = false;
    uint64_t first_recovery_frame_context_generation = 0;
    int32_t first_recovery_frame_width = 0;
    int32_t first_recovery_frame_height = 0;
    uint32_t first_recovery_frame_dpi = 0;
    bool recovery_test_enabled = false;
    bool recovery_test_staged = false;
    bool recovery_test_continue = false;
    std::vector<uint8_t> preserved_pixels;
    uint64_t preserved_frame_count = 0;
    int32_t preserved_width = 0;
    int32_t preserved_height = 0;
    uint32_t preserved_dpi = 0;
    openusd_render_camera latest_camera =
        openusd_render_camera_detail::Automatic();
};

namespace
{
struct MacPhysicalPoint
{
    int32_t x;
    int32_t y;
};

int32_t ClampPhysicalCoordinate(CGFloat value) noexcept
{
    const double rounded = std::round(static_cast<double>(value));
    return static_cast<int32_t>(std::clamp(
        rounded,
        static_cast<double>(std::numeric_limits<int32_t>::min()),
        static_cast<double>(std::numeric_limits<int32_t>::max())));
}

MacPhysicalPoint MacPointer(OpenUsdStormChildView* view, NSEvent* event)
{
    NSPoint local = [view convertPoint:[event locationInWindow] fromView:nil];
    NSPoint backing = [view convertPointToBacking:local];
    NSRect backing_bounds = [view convertRectToBacking:[view bounds]];
    return {
        ClampPhysicalCoordinate(backing.x),
        ClampPhysicalCoordinate(NSHeight(backing_bounds) - backing.y)};
}

uint32_t MacModifiers(NSEvent* event) noexcept
{
    const NSEventModifierFlags flags =
        [event modifierFlags] & NSEventModifierFlagDeviceIndependentFlagsMask;
    uint32_t modifiers = OPENUSD_STORM_CHILD_MODIFIER_NONE;
    if ((flags & NSEventModifierFlagOption) != 0)
    {
        modifiers |= OPENUSD_STORM_CHILD_MODIFIER_ALT;
    }
    if ((flags & NSEventModifierFlagShift) != 0)
    {
        modifiers |= OPENUSD_STORM_CHILD_MODIFIER_SHIFT;
    }
    if ((flags & NSEventModifierFlagControl) != 0)
    {
        modifiers |= OPENUSD_STORM_CHILD_MODIFIER_CONTROL;
    }
    if ((flags & NSEventModifierFlagCommand) != 0)
    {
        modifiers |= OPENUSD_STORM_CHILD_MODIFIER_META;
    }
    return modifiers;
}

uint32_t MacPressedButtons() noexcept
{
    const NSUInteger pressed = [NSEvent pressedMouseButtons];
    uint32_t buttons = OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE;
    if ((pressed & (1u << 0)) != 0)
    {
        buttons |= OPENUSD_STORM_CHILD_POINTER_BUTTON_LEFT;
    }
    if ((pressed & (1u << 2)) != 0)
    {
        buttons |= OPENUSD_STORM_CHILD_POINTER_BUTTON_MIDDLE;
    }
    if ((pressed & (1u << 1)) != 0)
    {
        buttons |= OPENUSD_STORM_CHILD_POINTER_BUTTON_RIGHT;
    }
    return buttons;
}

void UpdateMacPointer(
    OpenUsdStormChildView* view,
    NSEvent* event,
    uint32_t event_button,
    bool pressed)
{
    if (view->openUsdState == nullptr)
    {
        return;
    }
    const MacPhysicalPoint point = MacPointer(view, event);
    uint32_t buttons = MacPressedButtons();
    if (event_button != OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE)
    {
        if (pressed)
        {
            buttons |= event_button;
        }
        else
        {
            buttons &= ~event_button;
        }
    }
    OpenUsdStormChildNavigationUpdatePointer(
        &view->openUsdState->navigation,
        point.x,
        point.y,
        buttons,
        MacModifiers(event));
}

void UpdateMacKey(ChildState* state, NSEvent* event, bool pressed)
{
    const NSEventType type = [event type];
    const bool key_event =
        type == NSEventTypeKeyDown || type == NSEventTypeKeyUp;
    uint32_t command_key = 0;
    bool count_repeats = false;
    uint64_t OpenUsdStormChildNavigationState::* command_counter = nullptr;
    if (key_event)
    {
        NSString* characters = [event charactersIgnoringModifiers];
        const unichar character =
            [characters length] == 0 ? 0 : [characters characterAtIndex:0];
        if (character == 'f' || character == 'F' || [event keyCode] == 3)
        {
            command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_FRAME_SELECTED;
            command_counter =
                &OpenUsdStormChildNavigationState::frame_selected_press_count;
        }
        else if ([event keyCode] == 115)
        {
            command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_RESET_AUTOMATIC;
            command_counter =
                &OpenUsdStormChildNavigationState::reset_automatic_press_count;
        }
        else if (character == 'p' || character == 'P' || [event keyCode] == 35)
        {
            command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_TOGGLE_PROJECTION;
            command_counter =
                &OpenUsdStormChildNavigationState::toggle_projection_press_count;
        }
        else if (character == NSLeftArrowFunctionKey || [event keyCode] == 123)
        {
            command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_ORBIT_LEFT;
            count_repeats = true;
            command_counter =
                &OpenUsdStormChildNavigationState::orbit_left_press_count;
        }
        else if (character == NSRightArrowFunctionKey || [event keyCode] == 124)
        {
            command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_ORBIT_RIGHT;
            count_repeats = true;
            command_counter =
                &OpenUsdStormChildNavigationState::orbit_right_press_count;
        }
        else if (character == NSUpArrowFunctionKey || [event keyCode] == 126)
        {
            command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_ORBIT_UP;
            count_repeats = true;
            command_counter =
                &OpenUsdStormChildNavigationState::orbit_up_press_count;
        }
        else if (character == NSDownArrowFunctionKey || [event keyCode] == 125)
        {
            command_key = OPENUSD_STORM_CHILD_COMMAND_KEY_ORBIT_DOWN;
            count_repeats = true;
            command_counter =
                &OpenUsdStormChildNavigationState::orbit_down_press_count;
        }
    }
    std::lock_guard lock(state->navigation.gate);
    state->navigation.modifiers = MacModifiers(event);
    if (command_counter != nullptr)
    {
        const bool repeat = pressed && [event isARepeat];
        OpenUsdStormChildNavigationUpdateCommandKeyLocked(
            &state->navigation,
            command_key,
            pressed,
            repeat,
            count_repeats,
            command_counter);
    }
    OpenUsdStormChildNavigationAdvance(&state->navigation);
}

double MacScrollDelta(NSEvent* event) noexcept
{
    if ([event type] != NSEventTypeScrollWheel)
    {
        return 0;
    }
    return OpenUsdStormChildNormalizeMacScrollDelta(
        [event scrollingDeltaY],
        [event hasPreciseScrollingDeltas],
        [event isDirectionInvertedFromDevice]);
}
}

@implementation OpenUsdStormChildView
- (BOOL)acceptsFirstResponder { return YES; }
- (void)viewDidMoveToWindow
{
    [super viewDidMoveToWindow];
    if (openUsdState != nullptr)
    {
        std::lock_guard context_lock(openUsdState->context_gate);
        const bool attached =
            [self window] != nil && openUsdState->context != nil;
        if (attached)
        {
            [openUsdState->context setView:self];
        }
        openUsdState->drawable_attached = attached;
        openUsdState->context_update_required = true;
    }
}
- (BOOL)becomeFirstResponder
{
    if (openUsdState != nullptr)
    {
        openUsdState->focused.store(1, std::memory_order_relaxed);
        openUsdState->focus_count.fetch_add(1, std::memory_order_relaxed);
        OpenUsdStormChildNavigationSetFocus(&openUsdState->navigation, true);
    }
    return YES;
}
- (BOOL)resignFirstResponder
{
    if (openUsdState != nullptr)
    {
        openUsdState->focused.store(0, std::memory_order_relaxed);
        OpenUsdStormChildNavigationSetFocus(&openUsdState->navigation, false);
    }
    return YES;
}
- (void)updateTrackingAreas
{
    if (openUsdTrackingArea != nil)
    {
        [self removeTrackingArea:openUsdTrackingArea];
        [openUsdTrackingArea release];
    }
    openUsdTrackingArea = [[NSTrackingArea alloc]
        initWithRect:NSZeroRect
        options:NSTrackingMouseMoved | NSTrackingMouseEnteredAndExited |
            NSTrackingActiveInKeyWindow | NSTrackingInVisibleRect
        owner:self
        userInfo:nil];
    [self addTrackingArea:openUsdTrackingArea];
    [super updateTrackingAreas];
}
- (void)mouseMoved:(NSEvent*)event
{
    if (openUsdState != nullptr)
    {
        openUsdState->pointer_count.fetch_add(1, std::memory_order_relaxed);
        UpdateMacPointer(
            self,
            event,
            OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE,
            false);
    }
}
- (void)mouseDown:(NSEvent*)event
{
    if (openUsdState != nullptr)
    {
        openUsdState->pointer_count.fetch_add(1, std::memory_order_relaxed);
        UpdateMacPointer(
            self,
            event,
            OPENUSD_STORM_CHILD_POINTER_BUTTON_LEFT,
            true);
    }
    [[self window] makeFirstResponder:self];
}
- (void)mouseUp:(NSEvent*)event
{
    if (openUsdState != nullptr)
    {
        openUsdState->pointer_count.fetch_add(1, std::memory_order_relaxed);
        UpdateMacPointer(
            self,
            event,
            OPENUSD_STORM_CHILD_POINTER_BUTTON_LEFT,
            false);
    }
}
- (void)mouseDragged:(NSEvent*)event
{
    if (openUsdState != nullptr)
    {
        openUsdState->pointer_count.fetch_add(1, std::memory_order_relaxed);
        UpdateMacPointer(
            self,
            event,
            OPENUSD_STORM_CHILD_POINTER_BUTTON_LEFT,
            true);
    }
}
- (void)rightMouseDown:(NSEvent*)event
{
    if (openUsdState != nullptr)
    {
        openUsdState->pointer_count.fetch_add(1, std::memory_order_relaxed);
        UpdateMacPointer(
            self,
            event,
            OPENUSD_STORM_CHILD_POINTER_BUTTON_RIGHT,
            true);
    }
    [[self window] makeFirstResponder:self];
}
- (void)rightMouseUp:(NSEvent*)event
{
    if (openUsdState != nullptr)
    {
        openUsdState->pointer_count.fetch_add(1, std::memory_order_relaxed);
        UpdateMacPointer(
            self,
            event,
            OPENUSD_STORM_CHILD_POINTER_BUTTON_RIGHT,
            false);
    }
}
- (void)rightMouseDragged:(NSEvent*)event
{
    if (openUsdState != nullptr)
    {
        openUsdState->pointer_count.fetch_add(1, std::memory_order_relaxed);
        UpdateMacPointer(
            self,
            event,
            OPENUSD_STORM_CHILD_POINTER_BUTTON_RIGHT,
            true);
    }
}
- (void)otherMouseDown:(NSEvent*)event
{
    if (openUsdState != nullptr)
    {
        openUsdState->pointer_count.fetch_add(1, std::memory_order_relaxed);
        UpdateMacPointer(
            self,
            event,
            OPENUSD_STORM_CHILD_POINTER_BUTTON_MIDDLE,
            true);
    }
    [[self window] makeFirstResponder:self];
}
- (void)otherMouseUp:(NSEvent*)event
{
    if (openUsdState != nullptr)
    {
        openUsdState->pointer_count.fetch_add(1, std::memory_order_relaxed);
        UpdateMacPointer(
            self,
            event,
            OPENUSD_STORM_CHILD_POINTER_BUTTON_MIDDLE,
            false);
    }
}
- (void)otherMouseDragged:(NSEvent*)event
{
    if (openUsdState != nullptr)
    {
        openUsdState->pointer_count.fetch_add(1, std::memory_order_relaxed);
        UpdateMacPointer(
            self,
            event,
            OPENUSD_STORM_CHILD_POINTER_BUTTON_MIDDLE,
            true);
    }
}
- (void)mouseEntered:(NSEvent*)event
{
    (void)event;
    if (openUsdState != nullptr)
    {
        OpenUsdStormChildNavigationSetInside(&openUsdState->navigation, true);
    }
}
- (void)mouseExited:(NSEvent*)event
{
    (void)event;
    if (openUsdState != nullptr)
    {
        OpenUsdStormChildNavigationSetInside(&openUsdState->navigation, false);
    }
}
- (void)scrollWheel:(NSEvent*)event
{
    if (openUsdState != nullptr)
    {
        openUsdState->wheel_count.fetch_add(1, std::memory_order_relaxed);
        const MacPhysicalPoint point = MacPointer(self, event);
        OpenUsdStormChildNavigationAddWheel(
            &openUsdState->navigation,
            MacScrollDelta(event),
            point.x,
            point.y,
            MacPressedButtons(),
            MacModifiers(event));
    }
}
- (void)keyDown:(NSEvent*)event
{
    if (openUsdState != nullptr)
    {
        openUsdState->key_count.fetch_add(1, std::memory_order_relaxed);
        UpdateMacKey(openUsdState, event, true);
    }
}
- (void)keyUp:(NSEvent*)event
{
    if (openUsdState != nullptr)
    {
        openUsdState->key_count.fetch_add(1, std::memory_order_relaxed);
        UpdateMacKey(openUsdState, event, false);
    }
}
- (void)flagsChanged:(NSEvent*)event
{
    if (openUsdState != nullptr)
    {
        openUsdState->key_count.fetch_add(1, std::memory_order_relaxed);
        OpenUsdStormChildNavigationUpdateModifiers(
            &openUsdState->navigation,
            MacModifiers(event));
    }
}
- (void)dealloc
{
    openUsdState = nullptr;
    if (openUsdTrackingArea != nil)
    {
        [self removeTrackingArea:openUsdTrackingArea];
        [openUsdTrackingArea release];
        openUsdTrackingArea = nil;
    }
    [super dealloc];
}
@end

struct openusd_storm_child {};

struct openusd_storm_child_macos_resize_diagnostics
{
    uint64_t resize_generation;
    uint64_t context_update_generation;
    uint64_t rendered_resize_generation;
    uint64_t context_generation;
    uint64_t renderer_context_generation;
    uint64_t preserved_context_generation;
    uint64_t recovery_generation;
    uint64_t first_recovery_frame_context_generation;
    int32_t rendered_width;
    int32_t rendered_height;
    uint32_t rendered_dpi;
    int32_t context_update_required;
    int32_t drawable_attached;
    int32_t recovering;
    int32_t renderer_ready;
    int32_t first_recovery_frame_width;
    int32_t first_recovery_frame_height;
    uint32_t first_recovery_frame_dpi;
    int32_t first_recovery_frame_pending;
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
    const uintptr_t value = g_next_token.fetch_add(16, std::memory_order_relaxed);
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

openusd_status GetStormName(
    openusd_storm_renderer* renderer,
    std::string& name,
    std::string& error)
{
    size_t required = 0;
    char error_bytes[1024]{};
    openusd_error_buffer native_error{error_bytes, sizeof(error_bytes), 0};
    openusd_status status = openusd_storm_get_renderer_name(
        renderer, nullptr, 0, &required, &native_error);
    if (status != OPENUSD_STATUS_BUFFER_TOO_SMALL || required == 0)
    {
        error = error_bytes;
        return status;
    }
    std::string value(required, '\0');
    status = openusd_storm_get_renderer_name(
        renderer, value.data(), value.size(), &required, &native_error);
    if (status != OPENUSD_STATUS_OK)
    {
        error = error_bytes;
        return status;
    }
    value.resize(required - 1);
    name = std::move(value);
    return OPENUSD_STATUS_OK;
}

struct MainThreadContextOperation
{
    NSView* view = nil;
    NSOpenGLContext* context_to_release = nil;
    NSOpenGLContext* created_context = nil;
    bool create = false;
    bool old_drawable_attached = false;
    bool release_completed = false;
    bool created_drawable_attached = false;
    openusd_status status = OPENUSD_STATUS_OK;
    std::string error;
};

void PerformMainThreadContextOperation(MainThreadContextOperation* operation)
{
    @autoreleasepool
    {
        if (pthread_main_np() == 0)
        {
            operation->error =
                "NSOpenGLContext drawable ownership requires the Cocoa main thread.";
            operation->status = OPENUSD_STATUS_WRONG_THREAD;
            return;
        }
        if (operation->context_to_release != nil)
        {
            if (IsFailpoint("context-clear"))
            {
                operation->error = "Injected NSOpenGLContext clear failure.";
                operation->status = OPENUSD_STATUS_NATIVE_ERROR;
                return;
            }
            [operation->context_to_release clearDrawable];
            [operation->context_to_release release];
            operation->context_to_release = nil;
        }
        operation->release_completed = true;
        if (!operation->create)
        {
            return;
        }
        const NSOpenGLPixelFormatAttribute attributes[] =
        {
            NSOpenGLPFAOpenGLProfile,
            NSOpenGLProfileVersion4_1Core,
            NSOpenGLPFAColorSize, 24,
            NSOpenGLPFAAlphaSize, 8,
            NSOpenGLPFADepthSize, 24,
            NSOpenGLPFAStencilSize, 8,
            NSOpenGLPFADoubleBuffer,
            NSOpenGLPFAAccelerated,
            NSOpenGLPFANoRecovery,
            static_cast<NSOpenGLPixelFormatAttribute>(0)
        };
        NSOpenGLPixelFormat* format =
            [[NSOpenGLPixelFormat alloc] initWithAttributes:attributes];
        if (format == nil)
        {
            operation->error =
                "macOS could not create the OpenGL 4.1 core pixel format.";
            operation->status = OPENUSD_STATUS_NATIVE_ERROR;
            return;
        }
        NSOpenGLContext* context =
            [[NSOpenGLContext alloc] initWithFormat:format shareContext:nil];
        [format release];
        if (context == nil)
        {
            operation->error =
                "macOS could not create the application-owned NSOpenGLContext.";
            operation->status = OPENUSD_STATUS_NATIVE_ERROR;
            return;
        }
        GLint swap_interval = 1;
        [context setValues:&swap_interval
              forParameter:NSOpenGLContextParameterSwapInterval];
        [context setView:operation->view];
        if ([context view] != operation->view || [operation->view window] == nil)
        {
            [context clearDrawable];
            [context release];
            operation->error =
                "The Storm NSOpenGLContext drawable could not attach to its window.";
            operation->status = OPENUSD_STATUS_NATIVE_ERROR;
            return;
        }
        operation->created_context = context;
        operation->created_drawable_attached = true;
    }
}

void RunMainThreadContextOperation(MainThreadContextOperation* operation)
{
    if (pthread_main_np() != 0)
    {
        PerformMainThreadContextOperation(operation);
    }
    else
    {
        dispatch_sync(dispatch_get_main_queue(), ^
        {
            PerformMainThreadContextOperation(operation);
        });
    }
}

MainThreadContextOperation StageContextOperationLocked(
    ChildState* child,
    bool create)
{
    MainThreadContextOperation operation;
    operation.view = child->view;
    operation.context_to_release = child->context;
    operation.create = create;
    operation.old_drawable_attached = child->drawable_attached;
    child->context = nil;
    child->drawable_attached = false;
    child->context_update_required = true;
    return operation;
}

void RestoreUnreleasedContextLocked(
    ChildState* child,
    MainThreadContextOperation* operation)
{
    if (!operation->release_completed &&
        operation->context_to_release != nil)
    {
        child->context = operation->context_to_release;
        child->drawable_attached = operation->old_drawable_attached;
        child->context_update_required = true;
        operation->context_to_release = nil;
    }
}

void PublishCreatedContextLocked(
    ChildState* child,
    MainThreadContextOperation* operation)
{
    child->context = operation->created_context;
    operation->created_context = nil;
    child->drawable_attached = operation->created_drawable_attached;
    ++child->context_generation;
    child->context_update_required = true;
}

openusd_status InitializeContextOnRenderThreadLocked(
    ChildState* child,
    std::string& error)
{
    @autoreleasepool
    {
        if (child->context == nil ||
            !child->drawable_attached)
        {
            error = "The Storm NSOpenGLContext drawable is unavailable.";
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        [child->context makeCurrentContext];
        [child->context update];
        if ([NSOpenGLContext currentContext] != child->context ||
            [child->context CGLContextObj] == nullptr)
        {
            error = "The application-owned NSOpenGLContext could not be made current.";
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        GLint major = 0;
        GLint minor = 0;
        GLint profile = 0;
        while (glGetError() != GL_NO_ERROR)
        {
        }
        glGetIntegerv(GL_MAJOR_VERSION, &major);
        glGetIntegerv(GL_MINOR_VERSION, &minor);
        glGetIntegerv(GL_CONTEXT_PROFILE_MASK, &profile);
        if (glGetError() != GL_NO_ERROR ||
            major < 4 ||
            (major == 4 && minor < 1) ||
            (profile & GL_CONTEXT_CORE_PROFILE_BIT) == 0 ||
            (profile & GL_CONTEXT_COMPATIBILITY_PROFILE_BIT) != 0)
        {
            [NSOpenGLContext clearCurrentContext];
            error = "Storm requires the macOS OpenGL 4.1 core profile.";
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
        child->gl_major.store(major, std::memory_order_relaxed);
        child->gl_minor.store(minor, std::memory_order_relaxed);
        child->compatibility_profile.store(0, std::memory_order_relaxed);
        child->context_update_generation = child->resize_generation;
        child->context_update_required = false;
        return OPENUSD_STATUS_OK;
    }
}

openusd_status CreateRendererLocked(ChildState* child, std::string& error)
{
    child->renderer_context_generation = 0;
    char error_bytes[4096]{};
    openusd_error_buffer native_error{error_bytes, sizeof(error_bytes), 0};
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
    else if (child->renderer_name != ExpectedRendererName)
    {
        const std::string actual = child->renderer_name;
        openusd_storm_release(child->renderer);
        child->renderer = nullptr;
        child->renderer_name.clear();
        error = "macOS Storm requires openusd_hydra to report '" +
            std::string(ExpectedRendererName) + "', but it reported '" +
            actual + "'.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    child->renderer_name += PresentationSuffix;
    child->renderer_context_generation = child->context_generation;
    return status;
}

openusd_status InitializePublishedContextAndRendererLocked(
    ChildState* child,
    std::string& error)
{
    openusd_status status =
        InitializeContextOnRenderThreadLocked(child, error);
    if (status == OPENUSD_STATUS_OK)
    {
        status = CreateRendererLocked(child, error);
    }
    if ([NSOpenGLContext currentContext] == child->context)
    {
        [NSOpenGLContext clearCurrentContext];
    }
    return status;
}

openusd_status EnsureContextReadyLocked(ChildState* child, std::string& error)
{
    if (CurrentThreadId() !=
        child->render_thread_id.load(std::memory_order_relaxed))
    {
        error =
            "Storm rendering requires its dedicated NSOpenGLContext owner thread.";
        return OPENUSD_STATUS_WRONG_THREAD;
    }
    if (child->context == nil || [child->context CGLContextObj] == nullptr)
    {
        error = "The Storm NSOpenGLContext is unavailable or lost.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    if (!child->drawable_attached)
    {
        error = "The Storm NSOpenGLContext drawable is detached.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    if ([NSOpenGLContext currentContext] != child->context)
    {
        [child->context makeCurrentContext];
        if ([NSOpenGLContext currentContext] != child->context)
        {
            error = "The Storm NSOpenGLContext could not be made current.";
            return OPENUSD_STATUS_NATIVE_ERROR;
        }
    }
    if (child->context_update_required)
    {
        [child->context update];
        child->context_update_generation = child->resize_generation;
        child->context_update_required = false;
    }
    return OPENUSD_STATUS_OK;
}

class CurrentContextScope
{
public:
    explicit CurrentContextScope(NSOpenGLContext* context) noexcept
        : context_(context)
    {
    }

    ~CurrentContextScope()
    {
        if ([NSOpenGLContext currentContext] == context_)
        {
            [NSOpenGLContext clearCurrentContext];
        }
    }

    CurrentContextScope(const CurrentContextScope&) = delete;
    CurrentContextScope& operator=(const CurrentContextScope&) = delete;

private:
    NSOpenGLContext* context_;
};

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
    std::lock_guard context_lock(child->context_gate);
    if (child->renderer == nullptr)
    {
        error = "The Storm child renderer is unavailable.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    openusd_status status = EnsureContextReadyLocked(child, error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    CurrentContextScope current_context(child->context);
    if (child->context_update_generation != child->resize_generation)
    {
        error =
            "The Storm drawable context was not updated for the published resize generation.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    if (child->renderer_context_generation != child->context_generation)
    {
        error =
            "The Storm renderer was not initialized for the published context generation.";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    char error_bytes[4096]{};
    openusd_error_buffer native_error{error_bytes, sizeof(error_bytes), 0};
    const int32_t width =
        std::max<int32_t>(1, child->width.load(std::memory_order_relaxed));
    const int32_t height =
        std::max<int32_t>(1, child->height.load(std::memory_order_relaxed));
    const uint64_t frame_resize_generation = child->resize_generation;
    const bool first_recovery_frame = child->first_recovery_frame_pending;
    status = openusd_storm_render_v2(
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
    const size_t pixel_count =
        static_cast<size_t>(width) * static_cast<size_t>(height);
    if (pixel_count > MaximumCaptureBytes / 4u)
    {
        error = "The framebuffer exceeds the 64 MiB diagnostic capture limit.";
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    std::vector<uint8_t> pixels(pixel_count * 4u);
    GLint pack_alignment = 4;
    GLint read_buffer = GL_BACK;
    glGetIntegerv(GL_PACK_ALIGNMENT, &pack_alignment);
    glGetIntegerv(GL_READ_BUFFER, &read_buffer);
    glPixelStorei(GL_PACK_ALIGNMENT, 1);
    glReadBuffer(GL_BACK);
    glReadPixels(
        0, 0, width, height, GL_RGBA, GL_UNSIGNED_BYTE, pixels.data());
    const GLenum read_error = glGetError();
    glReadBuffer(static_cast<GLenum>(read_buffer));
    glPixelStorei(GL_PACK_ALIGNMENT, pack_alignment);
    if (read_error != GL_NO_ERROR)
    {
        error = "The macOS Storm completed-frame preservation failed with error " +
            std::to_string(read_error) + ".";
        return OPENUSD_STATUS_NATIVE_ERROR;
    }
    const size_t center =
        (static_cast<size_t>(height / 2) * static_cast<size_t>(width) +
         static_cast<size_t>(width / 2)) * 4u;
    const uint64_t signature =
        static_cast<uint64_t>(pixels[center]) |
        (static_cast<uint64_t>(pixels[center + 1]) << 8u) |
        (static_cast<uint64_t>(pixels[center + 2]) << 16u) |
        (static_cast<uint64_t>(pixels[center + 3]) << 24u);
    frame_count =
        child->frame_count.fetch_add(1, std::memory_order_relaxed) + 1;
    child->preserved_pixels = std::move(pixels);
    child->preserved_frame_count = frame_count;
    child->preserved_width = width;
    child->preserved_height = height;
    child->preserved_dpi = child->dpi.load(std::memory_order_relaxed);
    child->preserved_context_generation = child->context_generation;
    child->rendered_resize_generation = frame_resize_generation;
    if (first_recovery_frame)
    {
        child->first_recovery_frame_pending = false;
        child->first_recovery_frame_context_generation =
            child->context_generation;
        child->first_recovery_frame_width = width;
        child->first_recovery_frame_height = height;
        child->first_recovery_frame_dpi =
            child->dpi.load(std::memory_order_relaxed);
    }
    child->pixel_signature.store(signature, std::memory_order_relaxed);
    child->pixel_sample_count.fetch_add(1, std::memory_order_relaxed);
    child->latest_camera = camera;
    child->latest_rendered_camera_signature.store(
        openusd_render_camera_detail::Signature(camera),
        std::memory_order_relaxed);
    [child->context flushBuffer];
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
    pixels.resize(
        static_cast<size_t>(width) * static_cast<size_t>(height) * 4u);
    for (size_t offset = 0; offset < pixels.size(); offset += 4)
    {
        pixels[offset] = background[0];
        pixels[offset + 1] = background[1];
        pixels[offset + 2] = background[2];
        pixels[offset + 3] = background[3];
    }
    const int32_t left = width / 4;
    const int32_t bottom = height / 4;
    const int32_t pattern_width = std::max<int32_t>(1, width / 2);
    const int32_t pattern_height = std::max<int32_t>(1, height / 2);
    for (int32_t y = bottom; y < bottom + pattern_height; ++y)
    {
        for (int32_t x = left; x < left + pattern_width; ++x)
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

openusd_status ExecutePick(
    ChildState* child,
    OpenUsdStormChildPickPayload* pick,
    std::string& error)
{
    std::lock_guard context_lock(child->context_gate);
    openusd_status status = EnsureContextReadyLocked(child, error);
    if (status != OPENUSD_STATUS_OK)
    {
        pick->result.status = OPENUSD_RENDER_PICK_STATUS_CONTEXT_LOST;
        pick->result.context_generation = child->context_generation;
        return status;
    }
    CurrentContextScope current_context(child->context);
    return pick->Execute(
        child->renderer,
        child->context_generation,
        error);
}

openusd_status ExecuteSelection(
    ChildState* child,
    OpenUsdStormChildSelectionPayload* selection,
    std::string& error)
{
    std::lock_guard context_lock(child->context_gate);
    const openusd_status status = EnsureContextReadyLocked(child, error);
    if (status != OPENUSD_STATUS_OK)
    {
        return status;
    }
    CurrentContextScope current_context(child->context);
    return selection->Execute(child->renderer, error);
}

openusd_status CaptureFramebuffer(ChildState* child, Command* command)
{
    std::lock_guard context_lock(child->context_gate);
    if (child->preserved_frame_count == 0 || child->preserved_pixels.empty())
    {
        command->error =
            "A completed Storm frame is required before framebuffer capture.";
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    if ((command->capture_flags &
         ~OPENUSD_STORM_CHILD_CAPTURE_DRAW_TEST_PATTERN) != 0)
    {
        command->error = "The framebuffer capture flags are invalid.";
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    const int32_t width = child->preserved_width;
    const int32_t height = child->preserved_height;
    const size_t pixel_count =
        static_cast<size_t>(width) * static_cast<size_t>(height);
    if (pixel_count > MaximumCaptureBytes / 4u)
    {
        command->error =
            "The framebuffer exceeds the 64 MiB diagnostic capture limit.";
        return OPENUSD_STATUS_INVALID_ARGUMENT;
    }
    std::vector<uint8_t> pixels;
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
        pixels = child->preserved_pixels;
    }
    if (pixels.size() != pixel_count * 4u)
    {
        command->error = "The preserved Storm framebuffer is incomplete.";
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
    command->capture.frame_count = child->preserved_frame_count;
    command->capture.pixel_hash = hash;
    command->capture.pixel_count = pixel_count;
    command->capture.non_background_pixel_count = non_background;
    command->capture.width = width;
    command->capture.height = height;
    command->capture.dpi = child->preserved_dpi;
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

void PauseRecoveryAfterStagingForTest(ChildState* child)
{
    std::unique_lock lock(child->recovery_test_gate);
    if (!child->recovery_test_enabled)
    {
        return;
    }
    child->recovery_test_staged = true;
    child->recovery_test_changed.notify_all();
    child->recovery_test_changed.wait(
        lock,
        [child] { return child->recovery_test_continue; });
    child->recovery_test_enabled = false;
    child->recovery_test_staged = false;
    child->recovery_test_continue = false;
}

openusd_status RecreateAfterContextLoss(ChildState* child, std::string& error)
{
    MainThreadContextOperation replacement;
    {
        std::lock_guard context_lock(child->context_gate);
        if (child->renderer != nullptr)
        {
            [NSOpenGLContext clearCurrentContext];
            char error_bytes[4096]{};
            openusd_error_buffer native_error{
                error_bytes, sizeof(error_bytes), 0};
            const openusd_status status =
                openusd_storm_abandon(child->renderer, &native_error);
            if (status != OPENUSD_STATUS_OK)
            {
                error = error_bytes;
                return status;
            }
            child->renderer = nullptr;
            child->renderer_name.clear();
            child->renderer_context_generation = 0;
            child->teardown_fallback_count.fetch_add(
                1,
                std::memory_order_relaxed);
        }
        else if ([NSOpenGLContext currentContext] == child->context)
        {
            [NSOpenGLContext clearCurrentContext];
        }
        child->preserved_pixels.clear();
        child->preserved_frame_count = 0;
        child->preserved_width = 0;
        child->preserved_height = 0;
        child->preserved_dpi = 0;
        child->preserved_context_generation = 0;
        child->first_recovery_frame_pending = false;
        child->first_recovery_frame_context_generation = 0;
        child->first_recovery_frame_width = 0;
        child->first_recovery_frame_height = 0;
        child->first_recovery_frame_dpi = 0;
        ++child->recovery_generation;
        child->recovering = true;
        replacement = StageContextOperationLocked(child, true);
    }

    PauseRecoveryAfterStagingForTest(child);
    RunMainThreadContextOperation(&replacement);

    openusd_status initialization_status = OPENUSD_STATUS_OK;
    MainThreadContextOperation cleanup;
    {
        std::lock_guard context_lock(child->context_gate);
        if (replacement.status != OPENUSD_STATUS_OK)
        {
            RestoreUnreleasedContextLocked(child, &replacement);
            child->recovering = false;
            error = replacement.error;
            return replacement.status;
        }
        PublishCreatedContextLocked(child, &replacement);
        initialization_status =
            InitializePublishedContextAndRendererLocked(child, error);
        if (initialization_status == OPENUSD_STATUS_OK)
        {
            child->first_recovery_frame_pending = true;
            child->recovering = false;
            return OPENUSD_STATUS_OK;
        }
        cleanup = StageContextOperationLocked(child, false);
    }
    RunMainThreadContextOperation(&cleanup);
    {
        std::lock_guard context_lock(child->context_gate);
        RestoreUnreleasedContextLocked(child, &cleanup);
        child->recovering = false;
        if (cleanup.status != OPENUSD_STATUS_OK)
        {
            error += " Context cleanup also failed: " + cleanup.error;
        }
    }
    return initialization_status;
}

openusd_status TeardownRendererAndContext(
    ChildState* child,
    std::string& error)
{
    std::lock_guard context_lock(child->context_gate);
    if (child->renderer == nullptr)
    {
        if ([NSOpenGLContext currentContext] == child->context)
        {
            [NSOpenGLContext clearCurrentContext];
        }
        child->preserved_pixels.clear();
        child->preserved_frame_count = 0;
        child->preserved_width = 0;
        child->preserved_height = 0;
        child->preserved_dpi = 0;
        child->preserved_context_generation = 0;
        return OPENUSD_STATUS_OK;
    }
    std::string context_error;
    const openusd_status context_status =
        EnsureContextReadyLocked(child, context_error);
    if (context_status != OPENUSD_STATUS_OK)
    {
        [NSOpenGLContext clearCurrentContext];
        char abandon_error_bytes[4096]{};
        openusd_error_buffer abandon_error{
            abandon_error_bytes, sizeof(abandon_error_bytes), 0};
        const openusd_status abandon_status =
            openusd_storm_abandon(child->renderer, &abandon_error);
        if (abandon_status != OPENUSD_STATUS_OK)
        {
            error = context_error + " Storm abandon also failed: " +
                std::string(abandon_error_bytes);
            return abandon_status;
        }
        child->renderer = nullptr;
        child->renderer_name.clear();
        child->renderer_context_generation = 0;
        child->teardown_fallback_count.fetch_add(1, std::memory_order_relaxed);
        return OPENUSD_STATUS_OK;
    }

    char destroy_error_bytes[4096]{};
    openusd_error_buffer destroy_error{
        destroy_error_bytes, sizeof(destroy_error_bytes), 0};
    const bool inject_destroy =
        IsFailpoint("storm-destroy") ||
        IsFailpoint("storm-destroy-and-abandon");
    const openusd_status destroy_status = inject_destroy
        ? OPENUSD_STATUS_NATIVE_ERROR
        : openusd_storm_destroy(child->renderer, &destroy_error);
    if (destroy_status == OPENUSD_STATUS_OK)
    {
        child->renderer = nullptr;
        child->renderer_name.clear();
        child->renderer_context_generation = 0;
    }
    else
    {
        const std::string destroy_message = inject_destroy
            ? "Injected Storm destroy failure."
            : std::string(destroy_error_bytes);
        [NSOpenGLContext clearCurrentContext];
        char abandon_error_bytes[4096]{};
        openusd_error_buffer abandon_error{
            abandon_error_bytes, sizeof(abandon_error_bytes), 0};
        const bool inject_abandon =
            IsFailpoint("storm-destroy-and-abandon");
        const openusd_status abandon_status = inject_abandon
            ? OPENUSD_STATUS_NATIVE_ERROR
            : openusd_storm_abandon(child->renderer, &abandon_error);
        if (abandon_status != OPENUSD_STATUS_OK)
        {
            error = destroy_message + " Storm abandon also failed: " +
                (inject_abandon
                    ? "injected failure."
                    : std::string(abandon_error_bytes));
            return abandon_status;
        }
        child->renderer = nullptr;
        child->renderer_name.clear();
        child->renderer_context_generation = 0;
        child->teardown_fallback_count.fetch_add(1, std::memory_order_relaxed);
    }
    if ([NSOpenGLContext currentContext] == child->context)
    {
        [NSOpenGLContext clearCurrentContext];
    }
    child->preserved_pixels.clear();
    child->preserved_frame_count = 0;
    child->preserved_width = 0;
    child->preserved_height = 0;
    child->preserved_dpi = 0;
    child->preserved_context_generation = 0;
    return OPENUSD_STATUS_OK;
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
            pending->pick->Cancel(0);
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

void RenderThreadMain(ChildState* child)
{
    @autoreleasepool
    {
        child->render_thread_id.store(CurrentThreadId(), std::memory_order_relaxed);
        std::string error;
        openusd_status status;
        {
            std::lock_guard context_lock(child->context_gate);
            status = InitializePublishedContextAndRendererLocked(child, error);
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
                    command->status =
                        ExecutePick(child, command->pick.get(), command->error);
                    break;
                case CommandKind::Selection:
                    command->status = ExecuteSelection(
                        child,
                        command->selection.get(),
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
}

openusd_status ValidateChild(
                ChildState* child,
                openusd_error_buffer* error)
            {
                if (child == nullptr || child->view == nil)
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
                if (pthread_main_np() == 0 ||
                    CurrentThreadId() != child->creator_thread_id)
                {
                    WriteError(
                        error,
                        "The Storm NSView must be operated on its creator Cocoa main thread.");
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

            void WaitForCommandCompletion(
                std::unique_lock<std::mutex>& lock,
                const std::shared_ptr<Command>& command)
            {
                if (pthread_main_np() == 0)
                {
                    command->completion.wait(lock, [&command] { return command->done; });
                    return;
                }
                while (!command->done)
                {
                    lock.unlock();
                    @autoreleasepool
                    {
                        [[NSRunLoop mainRunLoop]
                            runMode:NSDefaultRunLoopMode
                            beforeDate:[NSDate dateWithTimeIntervalSinceNow:0.01]];
                    }
                    lock.lock();
                }
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
                    if (!openusd_render_camera_detail::Validate(
                            camera,
                            camera_error))
                    {
                        WriteError(error, camera_error);
                        return OPENUSD_STATUS_INVALID_ARGUMENT;
                    }
                    camera_value = *camera;
                    if ((revision_flags &
                         ~OPENUSD_STORM_RENDER_HAS_SCENE_REVISION) != 0)
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
                        child->asynchronous_render->scene_revision =
                            scene_revision;
                        child->asynchronous_render->revision_flags =
                            revision_flags;
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
                WaitForCommandCompletion(lock, command);
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
                const openusd_status status = ValidateChild(child, error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
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
                WaitForCommandCompletion(lock, command);
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
                    WriteError(error, "The RGBA framebuffer output buffer is too small.");
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
                    WriteError(
                        error,
                        "The pick result struct size or version is unsupported.");
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
                    (prim_path_buffer == nullptr &&
                     prim_path_capacity != 0) ||
                    (instancer_path_buffer == nullptr &&
                     instancer_path_capacity != 0) ||
                    (instance_context == nullptr &&
                     instance_context_capacity != 0) ||
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
                command->pick =
                    std::make_unique<OpenUsdStormChildPickPayload>();
                command->pick->request = *request;
                command->pick->result.struct_size =
                    sizeof(openusd_render_pick_result);
                command->pick->result.version =
                    OPENUSD_RENDER_PICK_RESULT_VERSION;
                command->pick->result.status =
                    OPENUSD_RENDER_PICK_STATUS_INVALID;
                command->pick->result.normalized_depth = 1.0;
                command->pick->result.instance_index = -1;
                command->pick->result.element_index = -1;
                command->pick->prim_path.resize(prim_path_capacity);
                command->pick->instancer_path.resize(instancer_path_capacity);
                command->pick->instance_context.resize(
                    instance_context_capacity);
                command->pick->instance_context_paths.resize(
                    instance_context_paths_capacity);

                std::unique_lock lock(child->gate);
                if (child->lifecycle != LifecycleState::Running)
                {
                    command->pick->Cancel(0);
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
                command->completion.wait(
                    lock,
                    [&command] { return command->done; });
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
                    update->struct_size !=
                        sizeof(openusd_storm_selection_update) ||
                    update->version !=
                        OPENUSD_STORM_SELECTION_UPDATE_VERSION ||
                    update->item_count >
                        OpenUsdStormChildMaximumPickContextEntries ||
                    update->path_bytes_size >
                        OpenUsdStormChildMaximumPickStringBytes ||
                    (update->item_count != 0 && update->items == nullptr) ||
                    (update->path_bytes_size != 0 &&
                     update->path_bytes == nullptr))
                {
                    WriteError(
                        error,
                        "The packed Storm child selection update is invalid.");
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
                command->completion.wait(
                    lock,
                    [&command] { return command->done; });
                if (command->status != OPENUSD_STATUS_OK)
                {
                    WriteError(error, command->error);
                }
                return command->status;
            }

            openusd_status QueueStop(ChildState* child, openusd_error_buffer* error)
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
                WaitForCommandCompletion(lock, command);
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
                    if (pthread_main_np() == 0)
                    {
                        WriteError(
                            error,
                            "The Storm NSView must be created on the Cocoa main thread.");
                        return OPENUSD_STATUS_WRONG_THREAD;
                    }
                    NSView* parent = static_cast<NSView*>(parent_window);
                    if (parent == nil ||
                        ![parent isKindOfClass:[NSView class]] ||
                        plugin_path == nullptr ||
                        plugin_path[0] == '\0' ||
                        stage == nullptr ||
                        child == nullptr ||
                        width <= 0 ||
                        height <= 0 ||
                        dpi == 0)
                    {
                        WriteError(
                            error,
                            "A valid parent NSView, plugin path, stage, size, DPI, and output are required.");
                        return OPENUSD_STATUS_INVALID_ARGUMENT;
                    }
                    openusd_status status = openusd_stage_retain(stage, error);
                    if (status != OPENUSD_STATUS_OK)
                    {
                        return status;
                    }
                    auto result = std::make_shared<ChildState>();
                    result->stage = stage;
                    result->creator_thread_id = CurrentThreadId();
                    result->plugin_path = plugin_path;
                    result->width.store(width, std::memory_order_relaxed);
                    result->height.store(height, std::memory_order_relaxed);
                    result->dpi.store(dpi, std::memory_order_relaxed);
                    @autoreleasepool
                    {
                        const CGFloat scale = static_cast<CGFloat>(dpi) / 96.0;
                        result->view = [[OpenUsdStormChildView alloc]
                            initWithFrame:NSMakeRect(
                                0,
                                0,
                                static_cast<CGFloat>(width) / scale,
                                static_cast<CGFloat>(height) / scale)];
                        if (result->view == nil)
                        {
                            openusd_stage_release(stage);
                            WriteError(error, "Could not allocate the Storm child NSView.");
                            return OPENUSD_STATUS_NATIVE_ERROR;
                        }
                        result->view->openUsdState = result.get();
                        [result->view setWantsBestResolutionOpenGLSurface:YES];
                        [result->view setAutoresizingMask:
                            NSViewWidthSizable | NSViewHeightSizable];
                        [parent addSubview:result->view];
                    }
                    MainThreadContextOperation initial_context;
                    initial_context.view = result->view;
                    initial_context.create = true;
                    RunMainThreadContextOperation(&initial_context);
                    status = initial_context.status;
                    if (status != OPENUSD_STATUS_OK)
                    {
                        result->view->openUsdState = nullptr;
                        [result->view removeFromSuperview];
                        [result->view release];
                        result->view = nil;
                        openusd_stage_release(stage);
                        WriteError(error, initial_context.error);
                        return status;
                    }
                    {
                        std::lock_guard context_lock(result->context_gate);
                        PublishCreatedContextLocked(
                            result.get(),
                            &initial_context);
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
                        MainThreadContextOperation release_context;
                        {
                            std::lock_guard context_lock(result->context_gate);
                            release_context =
                                StageContextOperationLocked(result.get(), false);
                        }
                        RunMainThreadContextOperation(&release_context);
                        {
                            std::lock_guard context_lock(result->context_gate);
                            RestoreUnreleasedContextLocked(
                                result.get(),
                                &release_context);
                        }
                        result->view->openUsdState = nullptr;
                        [result->view removeFromSuperview];
                        [result->view release];
                        result->view = nil;
                        openusd_stage_release(stage);
                        if (release_context.status != OPENUSD_STATUS_OK)
                        {
                            WriteError(error, release_context.error);
                            return release_context.status;
                        }
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
                    MainThreadContextOperation release_context;
                    {
                        std::lock_guard context_lock(state->context_gate);
                        release_context =
                            StageContextOperationLocked(state.get(), false);
                    }
                    RunMainThreadContextOperation(&release_context);
                    if (release_context.status != OPENUSD_STATUS_OK)
                    {
                        std::lock_guard context_lock(state->context_gate);
                        RestoreUnreleasedContextLocked(
                            state.get(),
                            &release_context);
                        WriteError(error, release_context.error);
                        return release_context.status;
                    }
                    if (IsFailpoint("destroy-view"))
                    {
                        WriteError(error, "Injected NSView detach failure.");
                        return OPENUSD_STATUS_NATIVE_ERROR;
                    }
                    state->view->openUsdState = nullptr;
                    [state->view removeFromSuperview];
                    [state->view release];
                    state->view = nil;
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
                *window = state->view;
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
                std::lock_guard context_lock(state->context_gate);
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
                const CGFloat scale = static_cast<CGFloat>(dpi) / 96.0;
                {
                    std::lock_guard context_lock(state->context_gate);
                    [state->view setFrameSize:NSMakeSize(
                        static_cast<CGFloat>(width) / scale,
                        static_cast<CGFloat>(height) / scale)];
                    [state->view layoutSubtreeIfNeeded];
                    state->width.store(width, std::memory_order_relaxed);
                    state->height.store(height, std::memory_order_relaxed);
                    state->dpi.store(dpi, std::memory_order_relaxed);
                    ++state->resize_generation;
                    state->context_update_required = true;
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
                [state->view setHidden:normalized == 0 ? YES : NO];
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
                NSWindow* window = [state->view window];
                if (window == nil || ![window makeFirstResponder:state->view])
                {
                    WriteError(error, "The Storm child NSView could not receive Cocoa focus.");
                    return OPENUSD_STATUS_NATIVE_ERROR;
                }
                return OPENUSD_STATUS_OK;
            }

            extern "C" __attribute__((visibility("default")))
            openusd_status openusd_storm_child_macos_inject_view_diagnostic_input(
                void* native_view,
                openusd_error_buffer* error)
            {
                if (pthread_main_np() == 0)
                {
                    WriteError(error, "Cocoa input diagnostics require the main thread.");
                    return OPENUSD_STATUS_WRONG_THREAD;
                }
                NSObject* native_object = static_cast<NSObject*>(native_view);
                NSView* view = nil;
                if ([native_object isKindOfClass:[NSView class]])
                {
                    view = static_cast<NSView*>(native_view);
                }
                else if ([native_object isKindOfClass:[NSWindow class]])
                {
                    NSWindow* native_window = static_cast<NSWindow*>(native_view);
                    view = [native_window contentView];
                }
                if (view == nil)
                {
                    WriteError(
                        error,
                        "A valid NSView or NSWindow is required for Cocoa input diagnostics.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                NSWindow* window = [view window];
                if (window == nil || ![window makeFirstResponder:view])
                {
                    WriteError(error, "The NSView cannot receive diagnostic Cocoa input.");
                    return OPENUSD_STATUS_NATIVE_ERROR;
                }
                NSEvent* event = [NSEvent
                    otherEventWithType:NSEventTypeApplicationDefined
                    location:NSZeroPoint
                    modifierFlags:0
                    timestamp:0
                    windowNumber:[window windowNumber]
                    context:nil
                    subtype:0
                    data1:0
                    data2:0];
                if (event == nil)
                {
                    WriteError(error, "Could not allocate the diagnostic Cocoa event.");
                    return OPENUSD_STATUS_NATIVE_ERROR;
                }
                const NSInteger window_number = [window windowNumber];
                NSEvent* moved = [NSEvent
                    mouseEventWithType:NSEventTypeMouseMoved
                    location:NSMakePoint(10, 10)
                    modifierFlags:NSEventModifierFlagOption
                    timestamp:0
                    windowNumber:window_number
                    context:nil
                    eventNumber:1
                    clickCount:0
                    pressure:0];
                NSEvent* down = [NSEvent
                    mouseEventWithType:NSEventTypeLeftMouseDown
                    location:NSMakePoint(10, 10)
                    modifierFlags:NSEventModifierFlagOption
                    timestamp:0
                    windowNumber:window_number
                    context:nil
                    eventNumber:2
                    clickCount:1
                    pressure:1];
                NSEvent* dragged = [NSEvent
                    mouseEventWithType:NSEventTypeLeftMouseDragged
                    location:NSMakePoint(30, 40)
                    modifierFlags:NSEventModifierFlagOption
                    timestamp:0
                    windowNumber:window_number
                    context:nil
                    eventNumber:3
                    clickCount:1
                    pressure:1];
                NSEvent* up = [NSEvent
                    mouseEventWithType:NSEventTypeLeftMouseUp
                    location:NSMakePoint(30, 40)
                    modifierFlags:0
                    timestamp:0
                    windowNumber:window_number
                    context:nil
                    eventNumber:4
                    clickCount:1
                    pressure:0];
                NSEvent* f_down = [NSEvent
                    keyEventWithType:NSEventTypeKeyDown
                    location:NSZeroPoint
                    modifierFlags:0
                    timestamp:0
                    windowNumber:window_number
                    context:nil
                    characters:@"f"
                    charactersIgnoringModifiers:@"f"
                    isARepeat:NO
                    keyCode:3];
                NSEvent* f_up = [NSEvent
                    keyEventWithType:NSEventTypeKeyUp
                    location:NSZeroPoint
                    modifierFlags:0
                    timestamp:0
                    windowNumber:window_number
                    context:nil
                    characters:@"f"
                    charactersIgnoringModifiers:@"f"
                    isARepeat:NO
                    keyCode:3];
                NSEvent* f_repeat = [NSEvent
                    keyEventWithType:NSEventTypeKeyDown
                    location:NSZeroPoint
                    modifierFlags:0
                    timestamp:0
                    windowNumber:window_number
                    context:nil
                    characters:@"f"
                    charactersIgnoringModifiers:@"f"
                    isARepeat:YES
                    keyCode:3];
                NSEvent* home_down = [NSEvent
                    keyEventWithType:NSEventTypeKeyDown
                    location:NSZeroPoint
                    modifierFlags:0
                    timestamp:0
                    windowNumber:window_number
                    context:nil
                    characters:@"\uf729"
                    charactersIgnoringModifiers:@"\uf729"
                    isARepeat:NO
                    keyCode:115];
                NSEvent* home_up = [NSEvent
                    keyEventWithType:NSEventTypeKeyUp
                    location:NSZeroPoint
                    modifierFlags:0
                    timestamp:0
                    windowNumber:window_number
                    context:nil
                    characters:@"\uf729"
                    charactersIgnoringModifiers:@"\uf729"
                    isARepeat:NO
                    keyCode:115];
                NSEvent* home_repeat = [NSEvent
                    keyEventWithType:NSEventTypeKeyDown
                    location:NSZeroPoint
                    modifierFlags:0
                    timestamp:0
                    windowNumber:window_number
                    context:nil
                    characters:@"\uf729"
                    charactersIgnoringModifiers:@"\uf729"
                    isARepeat:YES
                    keyCode:115];
                NSEvent* p_down = [NSEvent
                    keyEventWithType:NSEventTypeKeyDown
                    location:NSZeroPoint
                    modifierFlags:0
                    timestamp:0
                    windowNumber:window_number
                    context:nil
                    characters:@"p"
                    charactersIgnoringModifiers:@"p"
                    isARepeat:NO
                    keyCode:35];
                NSEvent* p_up = [NSEvent
                    keyEventWithType:NSEventTypeKeyUp
                    location:NSZeroPoint
                    modifierFlags:0
                    timestamp:0
                    windowNumber:window_number
                    context:nil
                    characters:@"p"
                    charactersIgnoringModifiers:@"p"
                    isARepeat:NO
                    keyCode:35];
                NSEvent* p_repeat = [NSEvent
                    keyEventWithType:NSEventTypeKeyDown
                    location:NSZeroPoint
                    modifierFlags:0
                    timestamp:0
                    windowNumber:window_number
                    context:nil
                    characters:@"p"
                    charactersIgnoringModifiers:@"p"
                    isARepeat:YES
                    keyCode:35];
                if (moved == nil || down == nil || dragged == nil || up == nil ||
                    f_down == nil || f_repeat == nil || f_up == nil ||
                    home_down == nil || home_repeat == nil || home_up == nil ||
                    p_down == nil || p_repeat == nil || p_up == nil)
                {
                    WriteError(error, "Could not allocate diagnostic Cocoa input events.");
                    return OPENUSD_STATUS_NATIVE_ERROR;
                }
                [view mouseMoved:moved];
                [view mouseDown:down];
                [view mouseDragged:dragged];
                [view mouseUp:up];
                [view scrollWheel:event];
                [view keyDown:f_down];
                [view keyDown:f_repeat];
                [view keyDown:f_down];
                [view keyUp:f_up];
                [view keyDown:f_down];
                [view keyUp:f_up];
                [view keyDown:home_down];
                [view keyDown:home_repeat];
                [view keyDown:home_down];
                [view keyUp:home_up];
                [view keyDown:home_down];
                [view keyUp:home_up];
                [view keyDown:p_down];
                [view keyDown:p_repeat];
                [view keyDown:p_down];
                [view keyUp:p_up];
                [view keyDown:p_down];
                [view keyUp:p_up];
                return OPENUSD_STATUS_OK;
            }

            extern "C" __attribute__((visibility("default")))
            openusd_status openusd_storm_child_macos_inject_diagnostic_input(
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
                status = openusd_storm_child_macos_inject_view_diagnostic_input(
                    state->view,
                    error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                int32_t pointer_x = 0;
                int32_t pointer_y = 0;
                {
                    std::lock_guard navigation_lock(state->navigation.gate);
                    pointer_x = state->navigation.pointer_x;
                    pointer_y = state->navigation.pointer_y;
                }
                OpenUsdStormChildNavigationAddWheel(
                    &state->navigation,
                    OpenUsdStormChildNormalizeMacScrollDelta(
                        1.0,
                        false,
                        false),
                    pointer_x,
                    pointer_y,
                    OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE,
                    OPENUSD_STORM_CHILD_MODIFIER_NONE);
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
                diagnostics->frame_count = state->frame_count.load(std::memory_order_relaxed);
                diagnostics->pixel_signature =
                    state->pixel_signature.load(std::memory_order_relaxed);
                diagnostics->pixel_sample_count =
                    state->pixel_sample_count.load(std::memory_order_relaxed);
                diagnostics->focus_count = state->focus_count.load(std::memory_order_relaxed);
                diagnostics->pointer_count =
                    state->pointer_count.load(std::memory_order_relaxed);
                diagnostics->wheel_count = state->wheel_count.load(std::memory_order_relaxed);
                diagnostics->key_count = state->key_count.load(std::memory_order_relaxed);
                diagnostics->coalesced_request_count =
                    state->coalesced_request_count.load(std::memory_order_relaxed);
                diagnostics->cancelled_command_count =
                    state->cancelled_command_count.load(std::memory_order_relaxed);
                diagnostics->teardown_fallback_count =
                    state->teardown_fallback_count.load(std::memory_order_relaxed);
                diagnostics->latest_requested_revision =
                    state->latest_requested_revision.load(std::memory_order_relaxed);
                diagnostics->latest_requested_camera_signature =
                    state->latest_requested_camera_signature.load(
                        std::memory_order_relaxed);
                diagnostics->latest_rendered_camera_signature =
                    state->latest_rendered_camera_signature.load(
                        std::memory_order_relaxed);
                diagnostics->render_thread_id =
                    state->render_thread_id.load(std::memory_order_relaxed);
                diagnostics->creator_thread_id = state->creator_thread_id;
                {
                    std::lock_guard lock(state->gate);
                    diagnostics->pending_command_count = PendingCommandCount(state.get());
                }
                diagnostics->peak_pending_command_count =
                    state->peak_pending_command_count.load(std::memory_order_relaxed);
                {
                    std::lock_guard context_lock(state->context_gate);
                    diagnostics->context_generation =
                        state->context_generation;
                    diagnostics->gl_major =
                        state->gl_major.load(std::memory_order_relaxed);
                    diagnostics->gl_minor =
                        state->gl_minor.load(std::memory_order_relaxed);
                    diagnostics->compatibility_profile =
                        state->compatibility_profile.load(std::memory_order_relaxed);
                    diagnostics->width = state->width.load(std::memory_order_relaxed);
                    diagnostics->height = state->height.load(std::memory_order_relaxed);
                    diagnostics->dpi = state->dpi.load(std::memory_order_relaxed);
                }
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

            extern "C" __attribute__((visibility("default")))
            openusd_status
            openusd_storm_child_macos_get_resize_diagnostics(
                openusd_storm_child* child,
                openusd_storm_child_macos_resize_diagnostics* diagnostics,
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
                        WriteError(error, "A resize diagnostics output is required.");
                        return OPENUSD_STATUS_INVALID_ARGUMENT;
                    }
                    return status;
                }
                std::lock_guard context_lock(state->context_gate);
                diagnostics->resize_generation = state->resize_generation;
                diagnostics->context_update_generation =
                    state->context_update_generation;
                diagnostics->rendered_resize_generation =
                    state->rendered_resize_generation;
                diagnostics->context_generation = state->context_generation;
                diagnostics->renderer_context_generation =
                    state->renderer_context_generation;
                diagnostics->preserved_context_generation =
                    state->preserved_context_generation;
                diagnostics->recovery_generation = state->recovery_generation;
                diagnostics->first_recovery_frame_context_generation =
                    state->first_recovery_frame_context_generation;
                diagnostics->rendered_width = state->preserved_width;
                diagnostics->rendered_height = state->preserved_height;
                diagnostics->rendered_dpi = state->preserved_dpi;
                diagnostics->context_update_required =
                    state->context_update_required ? 1 : 0;
                diagnostics->drawable_attached =
                    state->drawable_attached ? 1 : 0;
                diagnostics->recovering = state->recovering ? 1 : 0;
                diagnostics->renderer_ready =
                    state->renderer != nullptr ? 1 : 0;
                diagnostics->first_recovery_frame_width =
                    state->first_recovery_frame_width;
                diagnostics->first_recovery_frame_height =
                    state->first_recovery_frame_height;
                diagnostics->first_recovery_frame_dpi =
                    state->first_recovery_frame_dpi;
                diagnostics->first_recovery_frame_pending =
                    state->first_recovery_frame_pending ? 1 : 0;
                return OPENUSD_STATUS_OK;
            }

            extern "C" __attribute__((visibility("default")))
            openusd_status
            openusd_storm_child_macos_enable_recovery_barrier(
                openusd_storm_child* child,
                openusd_error_buffer* error)
            {
                const std::shared_ptr<ChildState> state = LookupChild(child);
                const openusd_status status = ValidateChild(state.get(), error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                std::lock_guard lock(state->recovery_test_gate);
                state->recovery_test_enabled = true;
                state->recovery_test_staged = false;
                state->recovery_test_continue = false;
                return OPENUSD_STATUS_OK;
            }

            extern "C" __attribute__((visibility("default")))
            openusd_status
            openusd_storm_child_macos_wait_recovery_staged(
                openusd_storm_child* child,
                openusd_error_buffer* error)
            {
                const std::shared_ptr<ChildState> state = LookupChild(child);
                const openusd_status status = ValidateChild(state.get(), error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                std::unique_lock lock(state->recovery_test_gate);
                if (!state->recovery_test_changed.wait_for(
                        lock,
                        std::chrono::seconds(10),
                        [&state] { return state->recovery_test_staged; }))
                {
                    WriteError(
                        error,
                        "Timed out waiting for the staged macOS recovery phase.");
                    return OPENUSD_STATUS_NATIVE_ERROR;
                }
                return OPENUSD_STATUS_OK;
            }

            extern "C" __attribute__((visibility("default")))
            openusd_status
            openusd_storm_child_macos_release_recovery_barrier(
                openusd_storm_child* child,
                openusd_error_buffer* error)
            {
                const std::shared_ptr<ChildState> state = LookupChild(child);
                const openusd_status status = ValidateChild(state.get(), error);
                if (status != OPENUSD_STATUS_OK)
                {
                    return status;
                }
                std::lock_guard lock(state->recovery_test_gate);
                if (!state->recovery_test_enabled)
                {
                    WriteError(error, "The macOS recovery barrier is not enabled.");
                    return OPENUSD_STATUS_INVALID_ARGUMENT;
                }
                state->recovery_test_continue = true;
                state->recovery_test_changed.notify_all();
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
