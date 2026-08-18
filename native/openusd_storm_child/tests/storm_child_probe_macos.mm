// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_storm_child.h"
#include "openusd_storm_child_macos_input.h"
#include "storm_child_camera_test.h"
#include "openusd_hydra.h"

#import <AppKit/AppKit.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <limits>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

extern "C" openusd_status openusd_storm_child_macos_inject_diagnostic_input(
    openusd_storm_child* child,
    openusd_error_buffer* error);

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

extern "C" openusd_status
openusd_storm_child_macos_get_resize_diagnostics(
    openusd_storm_child* child,
    openusd_storm_child_macos_resize_diagnostics* diagnostics,
    openusd_error_buffer* error);

extern "C" openusd_status
openusd_storm_child_macos_enable_recovery_barrier(
    openusd_storm_child* child,
    openusd_error_buffer* error);

extern "C" openusd_status
openusd_storm_child_macos_wait_recovery_staged(
    openusd_storm_child* child,
    openusd_error_buffer* error);

extern "C" openusd_status
openusd_storm_child_macos_release_recovery_barrier(
    openusd_storm_child* child,
    openusd_error_buffer* error);

namespace
{
using namespace openusd_storm_child_camera_test;

class ThreadBarrier
{
public:
    explicit ThreadBarrier(int participants) : remaining_(participants) {}

    void Wait()
    {
        std::unique_lock lock(gate_);
        if (--remaining_ == 0)
        {
            released_ = true;
            ready_.notify_all();
            return;
        }
        ready_.wait(lock, [this] { return released_; });
    }

private:
    std::mutex gate_;
    std::condition_variable ready_;
    int remaining_;
    bool released_ = false;
};

bool Require(bool condition, const char* message)
{
    if (!condition)
    {
        std::cerr << message << '\n';
    }
    return condition;
}

// A hosted macOS runner has no window server session that can vend an
// accelerated OpenGL 4.1 core pixel format. That is a genuine capability gap
// rather than a defect, so report it the same way the Windows probe reports a
// missing WGL_ARB_create_context.
constexpr int CapabilityUnavailableExitCode = 125;
constexpr char MissingCoreProfileError[] =
    "macOS could not create the OpenGL 4.1 core pixel format.";

openusd_storm_child_navigation_input NavigationInput()
{
    openusd_storm_child_navigation_input input{};
    input.struct_size = sizeof(input);
    input.version = OPENUSD_STORM_CHILD_NAVIGATION_INPUT_VERSION;
    return input;
}

bool IsZeroed(const openusd_storm_child_navigation_input& input)
{
    const openusd_storm_child_navigation_input zero{};
    return std::memcmp(&input, &zero, sizeof(input)) == 0;
}

bool VerifyMacScrollNormalization()
{
    return
        OpenUsdStormChildNormalizeMacScrollDelta(9.0, false, false) == 1.0 &&
        OpenUsdStormChildNormalizeMacScrollDelta(-9.0, false, false) == -1.0 &&
        OpenUsdStormChildNormalizeMacScrollDelta(9.0, false, true) == -1.0 &&
        OpenUsdStormChildNormalizeMacScrollDelta(20.0, true, false) == 0.5 &&
        OpenUsdStormChildNormalizeMacScrollDelta(-10.0, true, false) == -0.25 &&
        OpenUsdStormChildNormalizeMacScrollDelta(20.0, true, true) == -0.5 &&
        OpenUsdStormChildNormalizeMacScrollDelta(1000.0, true, false) ==
            OpenUsdStormChildMacMaximumScrollStepsPerEvent &&
        OpenUsdStormChildNormalizeMacScrollDelta(-1000.0, true, false) ==
            -OpenUsdStormChildMacMaximumScrollStepsPerEvent &&
        OpenUsdStormChildNormalizeMacScrollDelta(
            std::numeric_limits<double>::quiet_NaN(),
            true,
            false) == 0.0;
}

NSEvent* KeyEvent(
    NSWindow* window,
    NSEventType type,
    NSString* characters,
    BOOL repeat,
    unsigned short key_code,
    NSEventModifierFlags modifiers = 0)
{
    return [NSEvent
        keyEventWithType:type
        location:NSZeroPoint
        modifierFlags:modifiers
        timestamp:0
        windowNumber:[window windowNumber]
        context:nil
        characters:characters
        charactersIgnoringModifiers:characters
        isARepeat:repeat
        keyCode:key_code];
}

openusd_status RenderUntilConverged(
    openusd_storm_child* child,
    uint64_t* frame_count,
    int32_t* converged,
    openusd_error_buffer* error,
    const openusd_render_camera* requested_camera = nullptr)
{
    const openusd_render_camera automatic = AutomaticCamera();
    const openusd_render_camera* camera =
        requested_camera == nullptr ? &automatic : requested_camera;
    openusd_status status = OPENUSD_STATUS_OK;
    for (int iteration = 0; iteration < 64; ++iteration)
    {
        status = openusd_storm_child_render(
            child, 0, camera, frame_count, converged, error);
        if (status != OPENUSD_STATUS_OK || *converged != 0)
        {
            break;
        }
    }
    return status;
}

void PumpMainRunLoopUntil(const std::atomic_bool& completed)
{
    while (!completed.load(std::memory_order_acquire))
    {
        [[NSRunLoop mainRunLoop]
            runMode:NSDefaultRunLoopMode
            beforeDate:[NSDate dateWithTimeIntervalSinceNow:0.01]];
    }
}
}

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        std::cerr << "Usage: storm_child_probe <plugin-path> <stage-path>\n";
        return 2;
    }

    @autoreleasepool
    {
        [NSApplication sharedApplication];
        [NSApp setActivationPolicy:NSApplicationActivationPolicyRegular];
        [NSApp finishLaunching];
        NSWindow* window = [[NSWindow alloc]
            initWithContentRect:NSMakeRect(0, 0, 640, 480)
            styleMask:NSWindowStyleMaskTitled
            backing:NSBackingStoreBuffered
            defer:NO];
        [window setReleasedWhenClosed:NO];
        [window orderFront:nil];
        const uint32_t backing_dpi = static_cast<uint32_t>(std::lround(
            96.0 * std::max<CGFloat>(1.0, [window backingScaleFactor])));
        const uint32_t alternate_dpi = backing_dpi == 120 ? 144u : 120u;

        char error_data[4096]{};
        openusd_error_buffer error{error_data, sizeof(error_data), 0};
        size_t plugin_count = 0;
        openusd_stage* stage = nullptr;
        openusd_storm_child* child = nullptr;
        bool passed =
            Require(
                openusd_storm_child_get_abi_version() ==
                    OPENUSD_STORM_CHILD_ABI_VERSION,
                "Storm child ABI mismatch.") &&
            Require(
                VerifyMacScrollNormalization(),
                "The macOS wheel-step normalization contract is invalid.") &&
            Require(
                openusd_register_plugins(argv[1], &plugin_count, &error) ==
                    OPENUSD_STATUS_OK,
                error_data) &&
            Require(
                openusd_stage_open(argv[2], &stage, &error) ==
                    OPENUSD_STATUS_OK,
                error_data) &&
            Require(
                openusd_storm_child_create(
                    [window contentView],
                    argv[1],
                    stage,
                    256,
                    192,
                    backing_dpi,
                    &child,
                    &error) == OPENUSD_STATUS_OK,
                error_data);
        if (!passed)
        {
            const bool capability_unavailable =
                std::strstr(error_data, MissingCoreProfileError) != nullptr;
            if (stage != nullptr)
            {
                openusd_stage_release(stage);
            }
            [window close];
            [window release];
            if (capability_unavailable)
            {
                std::cerr <<
                    "Skipping Storm child probe: an accelerated OpenGL 4.1 "
                    "core context is unavailable.\n";
                return CapabilityUnavailableExitCode;
            }
            return 3;
        }
        const openusd_render_camera automatic = AutomaticCamera();
        passed =
            Require(
                VerifyInvalidCameras(child, &error),
                "Storm child accepted an invalid camera.") &&
            passed;

        void* child_view = nullptr;
        char renderer_name[256]{};
        size_t renderer_name_required = 0;
        uint64_t frame_count = 0;
        int32_t converged = 0;
        size_t capture_required = 0;
        openusd_storm_child_framebuffer_capture initial_capture{};
        openusd_storm_child_framebuffer_capture edited_capture{};
        openusd_storm_child_framebuffer_capture pattern_capture{};
        openusd_storm_child_framebuffer_capture preserved_capture{};
        openusd_storm_child_framebuffer_capture resized_capture{};
        openusd_storm_child_framebuffer_capture recreated_capture{};
        openusd_storm_child_diagnostics diagnostics{};
        openusd_storm_child_navigation_input invalid_navigation{};
        std::memset(&invalid_navigation, 0xff, sizeof(invalid_navigation));
        invalid_navigation.struct_size = sizeof(invalid_navigation);
        invalid_navigation.version =
            OPENUSD_STORM_CHILD_NAVIGATION_INPUT_VERSION + 1;
        openusd_storm_child_navigation_input navigation = NavigationInput();
        openusd_storm_child_macos_resize_diagnostics resize_diagnostics{};
        passed =
            Require(
                openusd_storm_child_get_navigation_input(
                    child,
                    nullptr,
                    &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
                "macOS navigation input accepted a null output.") &&
            Require(
                openusd_storm_child_get_navigation_input(
                    child,
                    &invalid_navigation,
                    &error) == OPENUSD_STATUS_INVALID_ARGUMENT &&
                    IsZeroed(invalid_navigation),
                "macOS navigation input did not zero an invalid version.") &&
            Require(
                openusd_storm_child_get_navigation_input(
                    child,
                    &navigation,
                    &error) == OPENUSD_STATUS_OK &&
                    navigation.sequence == 0,
                "The initial macOS navigation snapshot is invalid.") &&
            Require(
                openusd_storm_child_get_window(
                    child, &child_view, &error) == OPENUSD_STATUS_OK,
                error_data) &&
            Require(
                [(NSView*)child_view superview] == [window contentView],
                "Storm child has the wrong NSView parent.") &&
            Require(
                openusd_storm_child_get_renderer_name(
                    child,
                    renderer_name,
                    sizeof(renderer_name),
                    &renderer_name_required,
                    &error) == OPENUSD_STATUS_OK &&
                    std::string(renderer_name) ==
                        "Storm / Metal + OpenGL 4.1 core presentation",
                "The macOS renderer name did not preserve the actual Storm / Metal Hgi.") &&
            Require(
                openusd_storm_child_capture_framebuffer(
                    child,
                    0xff0e0e0eu,
                    2,
                    0,
                    nullptr,
                    0,
                    &capture_required,
                    &initial_capture,
                    &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
                "Framebuffer capture succeeded before a completed frame.") &&
            Require(
                RenderUntilConverged(
                    child, &frame_count, &converged, &error) ==
                    OPENUSD_STATUS_OK,
                error_data) &&
            Require(
                frame_count >= 1 && converged != 0,
                "The first macOS Storm child frame did not converge.") &&
            Require(
                openusd_storm_child_capture_framebuffer(
                    child,
                    0xff0e0e0eu,
                    2,
                    0,
                    nullptr,
                    0,
                    &capture_required,
                    &initial_capture,
                    &error) == OPENUSD_STATUS_OK,
                error_data) &&
            Require(
                initial_capture.width == 256 &&
                    initial_capture.height == 192 &&
                    initial_capture.dpi == backing_dpi &&
                    initial_capture.read_buffer ==
                        OPENUSD_STORM_CHILD_CAPTURE_READ_PRESERVED_TEXTURE &&
                    initial_capture.pixel_count == 256u * 192u &&
                    initial_capture.non_background_pixel_count > 0 &&
                    initial_capture.minimum_rgba != initial_capture.maximum_rgba,
                "The real macOS Storm framebuffer evidence is invalid.");

        std::vector<uint8_t> pattern_pixels(256u * 192u * 4u);
        passed =
            passed &&
            Require(
                openusd_storm_child_capture_framebuffer(
                    child,
                    0xff0e0e0eu,
                    2,
                    OPENUSD_STORM_CHILD_CAPTURE_DRAW_TEST_PATTERN,
                    pattern_pixels.data(),
                    pattern_pixels.size(),
                    &capture_required,
                    &pattern_capture,
                    &error) == OPENUSD_STATUS_OK,
                error_data) &&
            Require(
                pattern_capture.non_background_pixel_count == 128u * 96u &&
                    pattern_capture.read_buffer ==
                        OPENUSD_STORM_CHILD_CAPTURE_READ_PRESERVED_TEXTURE &&
                    pattern_capture.pixel_hash != 0,
                "The macOS diagnostic framebuffer pattern is incorrect.") &&
            Require(
                openusd_storm_child_capture_framebuffer(
                    child,
                    0xff0e0e0eu,
                    2,
                    0,
                    nullptr,
                    0,
                    &capture_required,
                    &preserved_capture,
                    &error) == OPENUSD_STATUS_OK &&
                    preserved_capture.frame_count == initial_capture.frame_count &&
                    preserved_capture.pixel_hash == initial_capture.pixel_hash,
                "Diagnostic capture did not preserve the exact pre-swap Storm frame.") &&
            Require(
                openusd_geom_imageable_set_visibility(
                    stage,
                    "/World/Cube",
                    OPENUSD_GEOM_VISIBILITY_INVISIBLE,
                    0,
                    0,
                    &error) == OPENUSD_STATUS_OK,
                error_data) &&
            Require(
                RenderUntilConverged(
                    child, &frame_count, &converged, &error) ==
                    OPENUSD_STATUS_OK,
                error_data) &&
            Require(
                openusd_storm_child_capture_framebuffer(
                    child,
                    0xff0e0e0eu,
                    2,
                    0,
                    nullptr,
                    0,
                    &capture_required,
                    &edited_capture,
                    &error) == OPENUSD_STATUS_OK,
                error_data) &&
            Require(
                edited_capture.pixel_hash != initial_capture.pixel_hash &&
                    edited_capture.non_background_pixel_count <
                        initial_capture.non_background_pixel_count,
                "The exact shared-stage live edit did not change the framebuffer.") &&
            Require(
                openusd_storm_child_resize(
                    child, 400, 200, alternate_dpi, &error) ==
                    OPENUSD_STATUS_OK &&
                    openusd_storm_child_resize(
                        child, 400, 200, backing_dpi, &error) ==
                        OPENUSD_STATUS_OK,
                error_data) &&
            Require(
                RenderUntilConverged(
                        child, &frame_count, &converged, &error) ==
                        OPENUSD_STATUS_OK,
                error_data) &&
            Require(
                openusd_storm_child_capture_framebuffer(
                        child,
                        0xff0e0e0eu,
                        2,
                        0,
                        nullptr,
                        0,
                        &capture_required,
                        &resized_capture,
                        &error) == OPENUSD_STATUS_OK &&
                        resized_capture.width == 400 &&
                        resized_capture.height == 200 &&
                        resized_capture.dpi == backing_dpi,
                "The completed frame did not preserve the resized backing scale.") &&
            Require(
                openusd_storm_child_set_visible(child, 0, &error) ==
                    OPENUSD_STATUS_OK &&
                    openusd_storm_child_set_visible(child, 1, &error) ==
                    OPENUSD_STATUS_OK &&
                    openusd_storm_child_focus(child, &error) ==
                    OPENUSD_STATUS_OK,
                error_data);

        NSView* view = static_cast<NSView*>(child_view);
        NSView* parent_view = [window contentView];
        passed =
            passed &&
            Require(
                openusd_storm_child_macos_inject_diagnostic_input(
                    child,
                    &error) == OPENUSD_STATUS_OK,
                error_data);
        passed =
            passed &&
            Require(
                ((navigation = NavigationInput()),
                 openusd_storm_child_get_navigation_input(
                     child,
                     &navigation,
                     &error) == OPENUSD_STATUS_OK) &&
                    navigation.sequence > 0 &&
                    navigation.buttons ==
                        OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE &&
                    navigation.modifiers ==
                        OPENUSD_STORM_CHILD_MODIFIER_NONE &&
                    navigation.cumulative_wheel_delta == 1.0 &&
                    navigation.frame_selected_press_count == 2 &&
                    navigation.reset_automatic_press_count == 2 &&
                    navigation.toggle_projection_press_count == 2 &&
                    (navigation.state &
                     (OPENUSD_STORM_CHILD_NAVIGATION_STATE_FOCUSED |
                      OPENUSD_STORM_CHILD_NAVIGATION_STATE_INSIDE)) ==
                        (OPENUSD_STORM_CHILD_NAVIGATION_STATE_FOCUSED |
                         OPENUSD_STORM_CHILD_NAVIGATION_STATE_INSIDE),
                "The Cocoa navigation input snapshot is invalid.") &&
            Require(
                openusd_storm_child_get_diagnostics(
                    child, &diagnostics, &error) == OPENUSD_STATUS_OK,
                error_data) &&
            Require(
                diagnostics.render_thread_id != 0 &&
                    diagnostics.render_thread_id != diagnostics.creator_thread_id,
                "Storm did not use a dedicated macOS OpenGL owner thread.") &&
            Require(
                diagnostics.gl_major == 4 &&
                    diagnostics.gl_minor >= 1 &&
                    diagnostics.compatibility_profile == 0,
                "Storm did not use macOS OpenGL 4.1 core.") &&
            Require(
                diagnostics.width == 400 &&
                    diagnostics.height == 200 &&
                    diagnostics.dpi == backing_dpi,
                "The macOS backing-scale transition is incorrect.") &&
            Require(
                diagnostics.focus_count >= 1 &&
                    diagnostics.pointer_count >= 4 &&
                    diagnostics.wheel_count >= 1 &&
                    diagnostics.key_count >= 18,
                "The NSResponder input counters did not advance.");
        struct ArrowEvent
        {
            NSString* characters;
            unsigned short key_code;
        };
        const ArrowEvent arrow_events[] =
        {
            {@"", 123},
            {[NSString stringWithFormat:@"%C", NSRightArrowFunctionKey], 0},
            {[NSString stringWithFormat:@"%C", NSUpArrowFunctionKey], 126},
            {[NSString stringWithFormat:@"%C", NSDownArrowFunctionKey], 125}
        };
        for (const ArrowEvent& arrow : arrow_events)
        {
            [view keyDown:KeyEvent(
                window,
                NSEventTypeKeyDown,
                arrow.characters,
                NO,
                arrow.key_code)];
            [view keyDown:KeyEvent(
                window,
                NSEventTypeKeyDown,
                arrow.characters,
                YES,
                arrow.key_code)];
            [view keyDown:KeyEvent(
                window,
                NSEventTypeKeyDown,
                arrow.characters,
                NO,
                arrow.key_code)];
            [view keyUp:KeyEvent(
                window,
                NSEventTypeKeyUp,
                arrow.characters,
                NO,
                arrow.key_code)];
        }
        [view keyDown:KeyEvent(
            window,
            NSEventTypeKeyDown,
            @"",
            NO,
            123,
            NSEventModifierFlagOption)];
        [view keyDown:KeyEvent(
            window,
            NSEventTypeKeyDown,
            @"",
            YES,
            123,
            NSEventModifierFlagOption)];
        [view keyUp:KeyEvent(
            window,
            NSEventTypeKeyUp,
            @"",
            NO,
            123,
            NSEventModifierFlagOption)];
        [view keyUp:KeyEvent(
            window,
            NSEventTypeKeyUp,
            @" ",
            NO,
            49)];
        navigation = NavigationInput();
        passed =
            passed &&
            Require(
                openusd_storm_child_get_navigation_input(
                    child,
                    &navigation,
                    &error) == OPENUSD_STATUS_OK &&
                    navigation.orbit_left_press_count == 2 &&
                    navigation.orbit_right_press_count == 2 &&
                    navigation.orbit_up_press_count == 2 &&
                    navigation.orbit_down_press_count == 2,
                "Cocoa arrow repeats, key mapping, or modifier filtering are invalid.");
        NSEvent* held_p = KeyEvent(
            window,
            NSEventTypeKeyDown,
            @"p",
            NO,
            35);
        NSEvent* repeated_p = KeyEvent(
            window,
            NSEventTypeKeyDown,
            @"p",
            YES,
            35);
        NSEvent* released_p = KeyEvent(
            window,
            NSEventTypeKeyUp,
            @"p",
            NO,
            35);
        passed =
            passed &&
            Require(
                held_p != nil && repeated_p != nil && released_p != nil,
                "Could not allocate Cocoa repeat-suppression events.");
        [view keyDown:held_p];
        [view keyDown:repeated_p];
        [view keyDown:held_p];
        NSEvent* held_left = KeyEvent(
            window, NSEventTypeKeyDown, @"", NO, 123);
        NSEvent* repeated_left = KeyEvent(
            window, NSEventTypeKeyDown, @"", YES, 123);
        NSEvent* released_left = KeyEvent(
            window, NSEventTypeKeyUp, @"", NO, 123);
        [view keyDown:held_left];
        [view keyDown:repeated_left];
        navigation = NavigationInput();
        passed =
            passed &&
            Require(
                openusd_storm_child_get_navigation_input(
                    child,
                    &navigation,
                    &error) == OPENUSD_STATUS_OK &&
                    navigation.toggle_projection_press_count == 3 &&
                    navigation.orbit_left_press_count == 4,
                "Held Cocoa command keys were not suppressed.");
        [window makeFirstResponder:nil];
        navigation = NavigationInput();
        passed =
            passed &&
            Require(
                openusd_storm_child_get_navigation_input(
                    child,
                    &navigation,
                    &error) == OPENUSD_STATUS_OK &&
                    (navigation.state &
                     OPENUSD_STORM_CHILD_NAVIGATION_STATE_FOCUSED) == 0 &&
                    navigation.buttons ==
                        OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE &&
                    navigation.modifiers ==
                        OPENUSD_STORM_CHILD_MODIFIER_NONE,
                "Cocoa focus loss did not reset navigation input.") &&
            Require(
                openusd_storm_child_focus(child, &error) ==
                    OPENUSD_STATUS_OK,
                error_data);
        [view keyUp:released_p];
        [view keyDown:held_p];
        [view keyUp:released_p];
        [view keyUp:released_left];
        [view keyDown:held_left];
        [view keyUp:released_left];
        navigation = NavigationInput();
        passed =
            passed &&
            Require(
                openusd_storm_child_get_navigation_input(
                    child,
                    &navigation,
                    &error) == OPENUSD_STATUS_OK &&
                    navigation.toggle_projection_press_count == 4 &&
                    navigation.orbit_left_press_count == 5 &&
                    navigation.orbit_right_press_count == 2 &&
                    navigation.orbit_up_press_count == 2 &&
                    navigation.orbit_down_press_count == 2,
                "Cocoa focus loss did not reset the pressed command-key state.");

        [view removeFromSuperview];
        const openusd_status detached_render = openusd_storm_child_render(
            child, 0, &automatic, &frame_count, &converged, &error);
        [parent_view addSubview:view];
        passed =
            passed &&
            Require(
                detached_render == OPENUSD_STATUS_NATIVE_ERROR,
                "A detached Cocoa drawable was not reported safely.") &&
            Require(
                RenderUntilConverged(
                   child, &frame_count, &converged, &error) ==
                   OPENUSD_STATUS_OK,
                "The Cocoa drawable did not recover after window reattachment.");

        ThreadBarrier resize_barrier(2);
        std::atomic_bool stop_resize_requests{false};
        std::atomic_bool resize_request_started{false};
        std::atomic_bool resize_request_failed{false};
        std::atomic_uint64_t resize_request_revision{1};
        std::thread resize_requester([&]
        {
            char worker_error_data[512]{};
            openusd_error_buffer worker_error{
                worker_error_data, sizeof(worker_error_data), 0};
            resize_barrier.Wait();
            while (!stop_resize_requests.load(std::memory_order_acquire))
            {
                const uint64_t revision =
                   (resize_request_revision.fetch_add(
                       1,
                       std::memory_order_relaxed) % 9000u) + 1u;
                const openusd_render_camera camera =
                    MatrixCamera(static_cast<double>(revision) * 0.000001);
                if (openusd_storm_child_request_frame_v2(
                       child,
                       static_cast<double>(revision),
                       &camera,
                       revision,
                       &worker_error) != OPENUSD_STATUS_OK)
                {
                   resize_request_failed.store(true, std::memory_order_release);
                   break;
                }
                openusd_storm_child_navigation_input worker_navigation =
                   NavigationInput();
                if (openusd_storm_child_get_navigation_input(
                       child,
                       &worker_navigation,
                       &worker_error) != OPENUSD_STATUS_OK)
                {
                   resize_request_failed.store(true, std::memory_order_release);
                   break;
                }
                resize_request_started.store(true, std::memory_order_release);
                std::this_thread::sleep_for(std::chrono::microseconds(100));
            }
        });
        resize_barrier.Wait();
        while (!resize_request_started.load(std::memory_order_acquire) &&
               !resize_request_failed.load(std::memory_order_acquire))
        {
            std::this_thread::yield();
        }
        bool resize_stress_passed =
            !resize_request_failed.load(std::memory_order_acquire);
        for (int iteration = 0;
             iteration < 64 && resize_stress_passed;
             ++iteration)
        {
            const int32_t stress_width = 192 + (iteration % 7) * 17;
            const int32_t stress_height = 128 + (iteration % 5) * 13;
            const uint32_t stress_dpi =
                (iteration % 2) == 0 ? backing_dpi : alternate_dpi;
            openusd_storm_child_framebuffer_capture stress_capture{};
            if (openusd_storm_child_resize(
                   child,
                   stress_width,
                   stress_height,
                   stress_dpi,
                   &error) != OPENUSD_STATUS_OK ||
                openusd_storm_child_render(
                   child,
                   static_cast<double>(iteration),
                   &automatic,
                   &frame_count,
                   &converged,
                   &error) != OPENUSD_STATUS_OK ||
                openusd_storm_child_capture_framebuffer(
                   child,
                   0xff0e0e0eu,
                   2,
                   0,
                   nullptr,
                   0,
                   &capture_required,
                   &stress_capture,
                   &error) != OPENUSD_STATUS_OK ||
                openusd_storm_child_macos_get_resize_diagnostics(
                   child,
                   &resize_diagnostics,
                   &error) != OPENUSD_STATUS_OK ||
                resize_diagnostics.resize_generation == 0 ||
                resize_diagnostics.context_update_generation !=
                   resize_diagnostics.resize_generation ||
                resize_diagnostics.rendered_resize_generation !=
                   resize_diagnostics.resize_generation ||
                resize_diagnostics.context_update_required != 0 ||
                resize_diagnostics.rendered_width != stress_width ||
                resize_diagnostics.rendered_height != stress_height ||
                resize_diagnostics.rendered_dpi != stress_dpi ||
                stress_capture.width != stress_width ||
                stress_capture.height != stress_height ||
                stress_capture.dpi != stress_dpi)
            {
                resize_stress_passed = false;
                break;
            }
        }
        stop_resize_requests.store(true, std::memory_order_release);
        resize_requester.join();
        passed =
            passed &&
            Require(
                !resize_request_failed.load(std::memory_order_acquire),
                "Concurrent frame requests failed during the resize stress.") &&
            Require(
                resize_stress_passed,
                "A frame used resized dimensions before its drawable context update.");

        openusd_status wrong_thread_destroy = OPENUSD_STATUS_OK;
        openusd_status wrong_thread_resize = OPENUSD_STATUS_OK;
        openusd_status wrong_thread_create = OPENUSD_STATUS_OK;
        std::thread wrong_destroy([&]
        {
            char worker_error_data[512]{};
            openusd_error_buffer worker_error{
                worker_error_data, sizeof(worker_error_data), 0};
            openusd_storm_child* unexpected_child = nullptr;
            wrong_thread_create = openusd_storm_child_create(
                parent_view,
                argv[1],
                stage,
                32,
                32,
                backing_dpi,
                &unexpected_child,
                &worker_error);
            wrong_thread_resize = openusd_storm_child_resize(
                child, 32, 32, backing_dpi, &worker_error);
            wrong_thread_destroy =
                openusd_storm_child_destroy(child, &worker_error);
        });
        wrong_destroy.join();
        passed =
            passed &&
            Require(
                wrong_thread_create == OPENUSD_STATUS_WRONG_THREAD &&
                    wrong_thread_resize == OPENUSD_STATUS_WRONG_THREAD &&
                wrong_thread_destroy == OPENUSD_STATUS_WRONG_THREAD,
                "Worker-thread Cocoa creation, resize, or destruction was not rejected.") &&
            Require(
                [view superview] == [window contentView],
                "Worker-thread destruction detached the Storm NSView.");

        const size_t abandoned_before =
            openusd_storm_diagnostic_get_abandoned_engine_count();
        const openusd_render_camera persistent_camera = MatrixCamera(0.125);
        const openusd_storm_child_navigation_input before_recovery_navigation =
            navigation;
        passed =
            passed &&
            Require(
                openusd_storm_child_render(
                    child,
                    0,
                    &persistent_camera,
                    &frame_count,
                    &converged,
                    &error) == OPENUSD_STATUS_OK,
                error_data) &&
            Require(
                openusd_storm_child_get_diagnostics(
                    child,
                    &diagnostics,
                    &error) == OPENUSD_STATUS_OK &&
                    diagnostics.latest_rendered_camera_signature ==
                        CameraSignature(persistent_camera),
                "The explicit Storm child camera was not propagated.");
        openusd_storm_child_macos_resize_diagnostics before_recovery{};
        openusd_storm_child_macos_resize_diagnostics staged_recovery{};
        openusd_storm_child_macos_resize_diagnostics recovered_state{};
        const bool recovery_setup =
            openusd_storm_child_macos_get_resize_diagnostics(
                child,
                &before_recovery,
                &error) == OPENUSD_STATUS_OK &&
            openusd_storm_child_macos_enable_recovery_barrier(
                child,
                &error) == OPENUSD_STATUS_OK;
        std::atomic_bool recovery_completed{false};
        openusd_status recovery_status = OPENUSD_STATUS_NATIVE_ERROR;
        std::thread recovery_thread([&]
        {
            char worker_error_data[512]{};
            openusd_error_buffer worker_error{
                worker_error_data, sizeof(worker_error_data), 0};
            recovery_status =
                openusd_storm_child_simulate_context_loss(child, &worker_error);
            recovery_completed.store(true, std::memory_order_release);
        });
        const int32_t recovery_width = 333;
        const int32_t recovery_height = 211;
        const uint32_t recovery_dpi = alternate_dpi;
        const bool recovery_staged =
            recovery_setup &&
            openusd_storm_child_macos_wait_recovery_staged(
                child,
                &error) == OPENUSD_STATUS_OK;
        bool recovery_interleave_passed =
            recovery_staged &&
            openusd_storm_child_macos_get_resize_diagnostics(
                child,
                &staged_recovery,
                &error) == OPENUSD_STATUS_OK &&
            staged_recovery.context_generation ==
                before_recovery.context_generation &&
            staged_recovery.recovery_generation ==
                before_recovery.recovery_generation + 1 &&
            staged_recovery.recovering == 1 &&
            staged_recovery.renderer_ready == 0 &&
            staged_recovery.drawable_attached == 0 &&
            staged_recovery.preserved_context_generation == 0;
        [view removeFromSuperview];
        const bool recovery_resize =
            openusd_storm_child_resize(
                child,
                recovery_width,
                recovery_height,
                recovery_dpi,
                &error) == OPENUSD_STATUS_OK;
        recovery_interleave_passed =
            recovery_interleave_passed && recovery_resize;
        [parent_view addSubview:view];
        recovery_interleave_passed =
            recovery_interleave_passed &&
            openusd_storm_child_get_diagnostics(
                child,
                &diagnostics,
                &error) == OPENUSD_STATUS_OK &&
            diagnostics.width == recovery_width &&
            diagnostics.height == recovery_height &&
            diagnostics.dpi == recovery_dpi &&
            openusd_storm_child_macos_get_resize_diagnostics(
                child,
                &staged_recovery,
                &error) == OPENUSD_STATUS_OK &&
            staged_recovery.recovering == 1 &&
            staged_recovery.renderer_ready == 0 &&
            staged_recovery.context_update_required == 1 &&
            staged_recovery.rendered_width == 0 &&
            staged_recovery.rendered_height == 0 &&
            staged_recovery.preserved_context_generation == 0;
        const bool recovery_released =
            recovery_setup &&
            openusd_storm_child_macos_release_recovery_barrier(
                child,
                &error) == OPENUSD_STATUS_OK;
        recovery_interleave_passed =
            recovery_interleave_passed && recovery_released;
        PumpMainRunLoopUntil(recovery_completed);
        recovery_thread.join();
        passed =
            passed &&
            Require(
                recovery_interleave_passed,
                "The staged recovery/resize/reattach interleave was inconsistent.") &&
            Require(
                recovery_status == OPENUSD_STATUS_OK,
                "The staged macOS context recovery failed or deadlocked.") &&
            Require(
                openusd_storm_child_get_diagnostics(
                    child,
                    &diagnostics,
                    &error) == OPENUSD_STATUS_OK &&
                    diagnostics.latest_rendered_camera_signature ==
                        CameraSignature(persistent_camera),
                "Context recreation did not preserve the latest camera.") &&
            Require(
                ((navigation = NavigationInput()),
                 openusd_storm_child_get_navigation_input(
                     child,
                     &navigation,
                     &error) == OPENUSD_STATUS_OK) &&
                    navigation.sequence >= before_recovery_navigation.sequence &&
                    navigation.frame_selected_press_count ==
                        before_recovery_navigation.frame_selected_press_count &&
                    navigation.reset_automatic_press_count ==
                        before_recovery_navigation.reset_automatic_press_count &&
                    navigation.toggle_projection_press_count ==
                        before_recovery_navigation.toggle_projection_press_count,
                "Context recreation did not preserve Cocoa navigation input.") &&
            Require(
                openusd_storm_child_render(
                    child,
                    0,
                    &persistent_camera,
                    &frame_count,
                    &converged,
                    &error) ==
                    OPENUSD_STATUS_OK,
                error_data) &&
            Require(
                openusd_storm_diagnostic_get_abandoned_engine_count() ==
                    abandoned_before + 1,
                "Context loss did not abandon exactly one Storm engine.") &&
            Require(
                openusd_storm_child_get_diagnostics(
                    child, &diagnostics, &error) == OPENUSD_STATUS_OK &&
                    diagnostics.context_generation ==
                        before_recovery.context_generation + 1,
                "The macOS context generation did not advance monotonically.") &&
            Require(
                openusd_storm_child_capture_framebuffer(
                    child,
                    0xff0e0e0eu,
                    2,
                    0,
                    nullptr,
                    0,
                    &capture_required,
                    &recreated_capture,
                    &error) == OPENUSD_STATUS_OK &&
                    recreated_capture.frame_count >= frame_count &&
                    recreated_capture.width == recovery_width &&
                    recreated_capture.height == recovery_height &&
                    recreated_capture.dpi == recovery_dpi &&
                    recreated_capture.pixel_hash != 0 &&
                    openusd_storm_child_macos_get_resize_diagnostics(
                        child,
                        &recovered_state,
                        &error) == OPENUSD_STATUS_OK &&
                    recovered_state.context_generation ==
                        before_recovery.context_generation + 1 &&
                    recovered_state.renderer_context_generation ==
                        recovered_state.context_generation &&
                    recovered_state.preserved_context_generation ==
                        recovered_state.context_generation &&
                    recovered_state.recovery_generation ==
                        before_recovery.recovery_generation + 1 &&
                    recovered_state.recovering == 0 &&
                    recovered_state.renderer_ready == 1 &&
                    recovered_state.drawable_attached == 1 &&
                    recovered_state.context_update_generation ==
                        recovered_state.resize_generation &&
                    recovered_state.first_recovery_frame_pending == 0 &&
                    recovered_state.first_recovery_frame_context_generation ==
                        recovered_state.context_generation &&
                    recovered_state.first_recovery_frame_width ==
                        recovery_width &&
                    recovered_state.first_recovery_frame_height ==
                        recovery_height &&
                    recovered_state.first_recovery_frame_dpi ==
                        recovery_dpi,
                "The first post-recovery frame did not match the recovered context state.");

        for (uint64_t revision = 1; revision <= 10000; ++revision)
        {
            const openusd_render_camera camera =
                MatrixCamera(static_cast<double>(revision) * 0.000001);
            if (openusd_storm_child_request_frame_v2(
                    child,
                    static_cast<double>(revision),
                    &camera,
                    revision,
                    &error) != OPENUSD_STATUS_OK)
            {
                passed = Require(false, "A burst render request was rejected.");
                break;
            }
        }
        const openusd_render_camera latest_camera = MatrixCamera(0.01);
        passed =
            passed &&
            Require(
                openusd_storm_child_get_diagnostics(
                    child, &diagnostics, &error) == OPENUSD_STATUS_OK,
                error_data) &&
            Require(
                diagnostics.latest_requested_revision == 10000 &&
                    diagnostics.latest_requested_camera_signature ==
                        CameraSignature(latest_camera) &&
                    diagnostics.peak_pending_command_count <= 1 &&
                    diagnostics.coalesced_request_count > 0,
                "The macOS render request queue was not bounded.");
        const size_t abandoned_before_teardown =
            openusd_storm_diagnostic_get_abandoned_engine_count();
        setenv("OPENUSD_STORM_CHILD_FAILPOINT", "storm-destroy", 1);
        openusd_storm_child* released_child = child;
        const openusd_status destroy_status =
            openusd_storm_child_destroy(child, &error);
        unsetenv("OPENUSD_STORM_CHILD_FAILPOINT");
        passed =
            passed &&
            Require(
               destroy_status == OPENUSD_STATUS_OK &&
                   openusd_storm_diagnostic_get_abandoned_engine_count() ==
                       abandoned_before_teardown + 1,
               "Storm teardown did not safely fall back to abandon.");
        navigation = NavigationInput();
        passed =
            passed &&
            Require(
               openusd_storm_child_get_navigation_input(
                   released_child,
                   &navigation,
                   &error) == OPENUSD_STATUS_INVALID_ARGUMENT &&
                   IsZeroed(navigation),
               "Released Cocoa navigation input remained valid or was not zeroed.");
        child = nullptr;
        openusd_stage_release(stage);
        stage = nullptr;
        passed =
            passed &&
            Require(
                openusd_storm_child_diagnostic_get_live_count() == 0,
                "The Storm macOS child wrapper leaked.") &&
            Require(
                openusd_storm_diagnostic_get_live_renderer_count() == 0,
                "The Storm macOS renderer wrapper leaked.");

        [window close];
        [window release];
        if (passed)
        {
            std::cout
                << "macOS framebuffer evidence: initialHash="
                << initial_capture.pixel_hash
                << ", editedHash=" << edited_capture.pixel_hash
                << ", patternHash=" << pattern_capture.pixel_hash
                << ", rendererName=" << renderer_name
                << ", resizeGeneration=" << resize_diagnostics.resize_generation
                << ", contextUpdateGeneration="
                << resize_diagnostics.context_update_generation
                << ", renderedResizeGeneration="
                << resize_diagnostics.rendered_resize_generation
                << ", recoveryGeneration="
                << recovered_state.recovery_generation
                << ", rendererContextGeneration="
                << recovered_state.renderer_context_generation
                << ", firstRecoveryFrameContextGeneration="
                << recovered_state.first_recovery_frame_context_generation
                << ", contextGeneration=" << diagnostics.context_generation
                << ", navigationSequence="
                << before_recovery_navigation.sequence
                << ", navigationCommands="
                << before_recovery_navigation.frame_selected_press_count
                << "/"
                << before_recovery_navigation.reset_automatic_press_count
                << "/"
                << before_recovery_navigation.toggle_projection_press_count
                << ", scrollPointsPerStep="
                << OpenUsdStormChildMacScrollPointsPerStep
                << ".\nStorm macOS native child probe passed.\n";
            return 0;
        }
        return 4;
    }
}
