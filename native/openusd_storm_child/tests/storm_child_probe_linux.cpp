// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_storm_child.h"
#include "storm_child_camera_test.h"
#include "openusd_hydra.h"

#include <X11/Xlib.h>
#include <X11/extensions/XTest.h>
#include <X11/keysym.h>
#include <sys/syscall.h>
#include <sys/wait.h>
#include <unistd.h>

#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <filesystem>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

namespace
{
using namespace openusd_storm_child_camera_test;

std::atomic_int g_untrapped_x_errors{0};
std::atomic_int g_rebound_x_errors{0};

int ProbeXErrorHandler(Display*, XErrorEvent*)
{
    g_untrapped_x_errors.fetch_add(1, std::memory_order_relaxed);
    return 0;
}

int ReboundXErrorHandler(Display*, XErrorEvent*)
{
    g_rebound_x_errors.fetch_add(1, std::memory_order_relaxed);
    return 0;
}

uint32_t CurrentThreadId()
{
    return static_cast<uint32_t>(syscall(SYS_gettid));
}

bool Require(bool condition, const char* message)
{
    if (!condition)
    {
        std::cerr << message << '\n';
    }
    return condition;
}

openusd_status RenderUntilConverged(
    openusd_storm_child* child,
    double time_code,
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
            child,
            time_code,
            camera,
            frame_count,
            converged,
            error);
        if (status != OPENUSD_STATUS_OK || *converged != 0)
        {
            break;
        }
    }
    return status;
}

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

bool PrepareInput(
    Display* display,
    Window child,
    int* root_x,
    int* root_y)
{
    int event_base = 0;
    int error_base = 0;
    int major_version = 0;
    int minor_version = 0;
    if (XTestQueryExtension(
            display,
            &event_base,
            &error_base,
            &major_version,
            &minor_version) == False)
    {
        return false;
    }
    XSetInputFocus(display, child, RevertToParent, CurrentTime);
    Window ignored = None;
    if (XTranslateCoordinates(
            display,
            child,
            DefaultRootWindow(display),
            20,
            20,
            root_x,
            root_y,
            &ignored) == False)
    {
        return false;
    }
    return true;
}

bool SendDragStart(Display* display, Window child)
{
    int root_x = 0;
    int root_y = 0;
    if (!PrepareInput(display, child, &root_x, &root_y))
    {
        return false;
    }
    const KeyCode alt = XKeysymToKeycode(display, XK_Alt_L);
    const bool delivered =
        XTestFakeMotionEvent(
            display,
            DefaultScreen(display),
            root_x,
            root_y,
            CurrentTime) != False &&
        alt != 0 &&
        XTestFakeKeyEvent(display, alt, True, CurrentTime) != False &&
        XTestFakeButtonEvent(display, Button1, True, CurrentTime) != False &&
        XTestFakeMotionEvent(
            display,
            DefaultScreen(display),
            root_x + 20,
            root_y + 20,
            CurrentTime) != False;
    XFlush(display);
    XSync(display, False);
    return delivered;
}

bool SendCommandWithRepeat(Display* display, KeyCode key)
{
    const bool held =
        key != 0 &&
        XTestFakeKeyEvent(display, key, True, CurrentTime) != False &&
        XTestFakeKeyEvent(display, key, True, CurrentTime) != False &&
        XTestFakeKeyEvent(display, key, False, CurrentTime) != False;
    XFlush(display);
    XSync(display, False);
    usleep(2000);
    return held &&
        XTestFakeKeyEvent(display, key, True, CurrentTime) != False &&
        XTestFakeKeyEvent(display, key, False, CurrentTime) != False;
}

bool SendInputCompletion(Display* display, Window child)
{
    int root_x = 0;
    int root_y = 0;
    if (!PrepareInput(display, child, &root_x, &root_y))
    {
        return false;
    }
    const KeyCode alt = XKeysymToKeycode(display, XK_Alt_L);
    const KeyCode f = XKeysymToKeycode(display, XK_f);
    const KeyCode home = XKeysymToKeycode(display, XK_Home);
    const KeyCode p = XKeysymToKeycode(display, XK_p);
    const KeyCode space = XKeysymToKeycode(display, XK_space);
    const bool delivered =
        XTestFakeButtonEvent(display, Button1, False, CurrentTime) != False &&
        alt != 0 &&
        XTestFakeKeyEvent(display, alt, False, CurrentTime) != False &&
        XTestFakeButtonEvent(display, Button2, True, CurrentTime) != False &&
        XTestFakeButtonEvent(display, Button2, False, CurrentTime) != False &&
        XTestFakeButtonEvent(display, Button3, True, CurrentTime) != False &&
        XTestFakeButtonEvent(display, Button3, False, CurrentTime) != False &&
        XTestFakeButtonEvent(display, Button4, True, CurrentTime) != False &&
        XTestFakeButtonEvent(display, Button4, False, CurrentTime) != False &&
        XTestFakeButtonEvent(display, Button5, True, CurrentTime) != False &&
        XTestFakeButtonEvent(display, Button5, False, CurrentTime) != False &&
        SendCommandWithRepeat(display, f) &&
        SendCommandWithRepeat(display, home) &&
        SendCommandWithRepeat(display, p) &&
        space != 0 &&
        XTestFakeKeyEvent(display, space, True, CurrentTime) != False &&
        XTestFakeKeyEvent(display, space, False, CurrentTime) != False;
    XFlush(display);
    XSync(display, False);
    return delivered;
}

void SetFailpoint(const char* value)
{
    if (value == nullptr)
    {
        unsetenv("OPENUSD_STORM_CHILD_FAILPOINT");
    }
    else
    {
        setenv("OPENUSD_STORM_CHILD_FAILPOINT", value, 1);
    }
}

bool ValidateStormChildRuntimeTopology(const char* runtime_path)
{
    namespace fs = std::filesystem;
    const fs::path directory(runtime_path);
    const fs::path link = directory / "libopenusd_storm_child.so";
    const fs::path soname = directory / "libopenusd_storm_child.so.7";
    const fs::path real = directory / "libopenusd_storm_child.so.7.0.0";
    std::error_code error;
    const bool valid =
        fs::is_symlink(link, error) &&
        !error &&
        fs::read_symlink(link, error) == soname.filename() &&
        !error &&
        fs::is_symlink(soname, error) &&
        !error &&
        fs::read_symlink(soname, error) == real.filename() &&
        !error &&
        fs::is_regular_file(real, error) &&
        !error &&
        !fs::is_symlink(real, error) &&
        !error;
    return valid;
}
}

int main(int argc, char** argv)
{
    if (argc != 4)
    {
        std::cerr <<
            "Usage: storm_child_probe <plugin-path> <stage-path> <runtime-path>\n";
        return 2;
    }
    if (XInitThreads() == 0)
    {
        std::cerr << "XInitThreads failed.\n";
        return 3;
    }
    const pid_t initialization_contract_child = fork();
    if (initialization_contract_child == 0)
    {
        char child_error_data[1024]{};
        openusd_error_buffer child_error{
            child_error_data,
            sizeof(child_error_data),
            0};
        openusd_storm_child* uninitialized_child = nullptr;
        const openusd_status create_status = openusd_storm_child_create(
            reinterpret_cast<void*>(static_cast<uintptr_t>(1)),
            "unused",
            reinterpret_cast<openusd_stage*>(static_cast<uintptr_t>(1)),
            1,
            1,
            96,
            &uninitialized_child,
            &child_error);
        const bool create_rejected =
            create_status == OPENUSD_STATUS_NATIVE_ERROR &&
            uninitialized_child == nullptr &&
            std::string(child_error_data).find("not initialized") !=
                std::string::npos;
        child_error_data[0] = '\0';
        child_error.required = 0;
        const openusd_status late_status =
            openusd_storm_child_initialize_linux(&child_error);
        const bool late_rejected =
            late_status == OPENUSD_STATUS_NATIVE_ERROR &&
            std::string(child_error_data).find("too late") != std::string::npos;
        _exit(create_rejected && late_rejected ? 0 : 1);
    }
    int initialization_contract_status = 0;
    if (initialization_contract_child < 0 ||
        waitpid(
            initialization_contract_child,
            &initialization_contract_status,
            0) != initialization_contract_child ||
        !WIFEXITED(initialization_contract_status) ||
        WEXITSTATUS(initialization_contract_status) != 0)
    {
        std::cerr << "The Linux dispatcher too-late contract failed.\n";
        return 3;
    }
    XSetErrorHandler(ProbeXErrorHandler);
    char initialization_error_data[1024]{};
    openusd_error_buffer initialization_error{
        initialization_error_data,
        sizeof(initialization_error_data),
        0};
    if (openusd_storm_child_initialize_linux(&initialization_error) !=
            OPENUSD_STATUS_OK ||
        openusd_storm_child_initialize_linux(&initialization_error) !=
            OPENUSD_STATUS_OK)
    {
        std::cerr << initialization_error_data << '\n';
        return 3;
    }
    Display* display = XOpenDisplay(nullptr);
    if (!Require(display != nullptr, "Could not open DISPLAY."))
    {
        return 4;
    }
    const int screen = DefaultScreen(display);
    Window parent = XCreateSimpleWindow(
        display,
        RootWindow(display, screen),
        0,
        0,
        320,
        240,
        0,
        BlackPixel(display, screen),
        BlackPixel(display, screen));
    XMapWindow(display, parent);
    XSync(display, False);

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
            ValidateStormChildRuntimeTopology(argv[3]),
            "Storm child runtime does not contain the exact ABI-7 SONAME link chain.") &&
        Require(
            openusd_register_plugins(argv[1], &plugin_count, &error) ==
                OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            openusd_stage_open(argv[2], &stage, &error) ==
                OPENUSD_STATUS_OK,
            error_data);
    if (!passed)
    {
        XDestroyWindow(display, parent);
        XCloseDisplay(display);
        return 5;
    }
    const openusd_render_camera automatic = AutomaticCamera();
    passed =
        Require(
            VerifyInvalidCameras(child, &error),
            "Storm child accepted an invalid camera.") &&
        passed;

    Window destroyed_parent = XCreateSimpleWindow(
        display,
        RootWindow(display, screen),
        0,
        0,
        64,
        64,
        0,
        BlackPixel(display, screen),
        BlackPixel(display, screen));
    XDestroyWindow(display, destroyed_parent);
    XSync(display, False);
    const int errors_before_concurrency =
        g_untrapped_x_errors.load(std::memory_order_relaxed);
    std::atomic_bool error_thread_ready{false};
    std::atomic_bool error_thread_opened_display{false};
    std::atomic_bool run_error_thread{true};
    std::thread error_thread([&]
    {
        Display* error_display = XOpenDisplay(nullptr);
        error_thread_opened_display.store(
            error_display != nullptr,
            std::memory_order_release);
        error_thread_ready.store(true, std::memory_order_release);
        if (error_display == nullptr)
        {
            return;
        }
        while (run_error_thread.load(std::memory_order_acquire))
        {
            XWindowAttributes attributes{};
            XGetWindowAttributes(error_display, None, &attributes);
            XSync(error_display, False);
        }
        XCloseDisplay(error_display);
    });
    while (!error_thread_ready.load(std::memory_order_acquire))
    {
        std::this_thread::yield();
    }
    SetFailpoint("xerror-trap-concurrency");
    for (int iteration = 0; iteration < 8; ++iteration)
    {
        passed =
            Require(
                openusd_storm_child_create(
                    reinterpret_cast<void*>(
                        static_cast<uintptr_t>(destroyed_parent)),
                    argv[1],
                    stage,
                    64,
                    64,
                    96,
                    &child,
                    &error) == OPENUSD_STATUS_INVALID_ARGUMENT &&
                    child == nullptr,
                "A repeated checked trap did not reject a destroyed parent.") &&
            passed;
    }
    SetFailpoint(nullptr);
    run_error_thread.store(false, std::memory_order_release);
    error_thread.join();
    passed =
        Require(
            error_thread_opened_display.load(std::memory_order_acquire) &&
                g_untrapped_x_errors.load(std::memory_order_relaxed) >
                    errors_before_concurrency,
            "Concurrent unrelated X errors were not forwarded by the stable dispatcher.") &&
        Require(
            openusd_storm_child_initialize_linux(&error) == OPENUSD_STATUS_OK,
            "Repeated dispatcher initialization after active traps was not idempotent.") &&
        passed;
    passed =
        Require(
            openusd_storm_child_create(
                reinterpret_cast<void*>(
                    static_cast<uintptr_t>(destroyed_parent)),
                argv[1],
                stage,
                64,
                64,
                96,
                &child,
                &error) == OPENUSD_STATUS_INVALID_ARGUMENT &&
                child == nullptr,
            "A destroyed parent XID was not rejected cleanly.") &&
        passed;

    XSetErrorHandler(ReboundXErrorHandler);
    passed =
        Require(
            openusd_storm_child_initialize_linux(&error) == OPENUSD_STATUS_OK,
            "Dispatcher rebind after an external XError handler replacement failed.") &&
        Require(
            openusd_storm_child_initialize_linux(&error) == OPENUSD_STATUS_OK,
            "Repeated dispatcher rebind was not idempotent.") &&
        passed;

    const int errors_before_expected_window_error =
        g_rebound_x_errors.load(std::memory_order_relaxed);
    SetFailpoint("xcreate-window-xerror");
    passed =
        Require(
            openusd_storm_child_create(
                reinterpret_cast<void*>(static_cast<uintptr_t>(parent)),
                argv[1],
                stage,
                64,
                64,
                96,
                &child,
                &error) == OPENUSD_STATUS_NATIVE_ERROR &&
                child == nullptr,
            "An injected XCreateWindow error escaped the XError dispatcher trap.") &&
        passed;
    passed =
        Require(
            g_rebound_x_errors.load(std::memory_order_relaxed) ==
                errors_before_expected_window_error,
            "An expected checked X11 error was forwarded to the rebound handler.") &&
        passed;
    SetFailpoint("glx-context-46-xerror-and-unrelated");
    passed =
        Require(
            openusd_storm_child_create(
                reinterpret_cast<void*>(static_cast<uintptr_t>(parent)),
                argv[1],
                stage,
                256,
                192,
                96,
                &child,
                &error) == OPENUSD_STATUS_OK,
            error_data);
    SetFailpoint(nullptr);
    passed =
        Require(
            g_rebound_x_errors.load(std::memory_order_relaxed) ==
                errors_before_expected_window_error + 1,
            "The dispatcher did not capture GLX while forwarding one unrelated error "
            "to the rebound handler.") &&
        passed;
    if (!passed)
    {
        if (stage != nullptr)
        {
            openusd_stage_release(stage);
        }
        XDestroyWindow(display, parent);
        XCloseDisplay(display);
        return 5;
    }

    void* child_window_pointer = nullptr;
    uint64_t frame_count = 0;
    int32_t converged = 0;
    openusd_storm_child_diagnostics diagnostics{};
    openusd_storm_child_framebuffer_capture initial_capture{};
    openusd_storm_child_framebuffer_capture repeated_capture{};
    openusd_storm_child_framebuffer_capture edited_capture{};
    size_t capture_required = 0;
    SetFailpoint("post-swap-back-corrupt");
    passed =
        Require(
            openusd_storm_child_get_window(
                child,
                &child_window_pointer,
                &error) == OPENUSD_STATUS_OK,
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
                &initial_capture,
                &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
            "Framebuffer capture succeeded before a completed frame.") &&
        Require(
            RenderUntilConverged(
                child,
                0,
                &frame_count,
                &converged,
                &error) == OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            frame_count >= 1 && converged != 0,
            "The first Storm child frame did not converge.");
    SetFailpoint(nullptr);

    const Window child_window = static_cast<Window>(
        reinterpret_cast<uintptr_t>(child_window_pointer));
    Window root = None;
    Window actual_parent = None;
    Window* children = nullptr;
    unsigned int child_count = 0;
    const bool queried = XQueryTree(
        display,
        child_window,
        &root,
        &actual_parent,
        &children,
        &child_count) != 0;
    if (children != nullptr)
    {
        XFree(children);
    }
    passed =
        passed &&
        Require(queried && actual_parent == parent, "Storm child has the wrong X11 parent.") &&
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
                initial_capture.pixel_count == 256u * 192u &&
                initial_capture.non_background_pixel_count > 0 &&
                initial_capture.read_buffer ==
                    OPENUSD_STORM_CHILD_CAPTURE_READ_PRESERVED_TEXTURE,
            "The first preserved framebuffer evidence is invalid.") &&
        Require(
            openusd_storm_child_capture_framebuffer(
                child,
                0xff0e0e0eu,
                2,
                0,
                nullptr,
                0,
                &capture_required,
                &repeated_capture,
                &error) == OPENUSD_STATUS_OK &&
                repeated_capture.frame_count == initial_capture.frame_count &&
                repeated_capture.pixel_hash == initial_capture.pixel_hash,
            "Framebuffer capture did not return the exact completed frame.");

    std::vector<uint8_t> pixels(256u * 192u * 4u);
    openusd_storm_child_framebuffer_capture pattern_capture{};
    openusd_storm_child_diagnostics input_before{};
    openusd_storm_child_navigation_input invalid_navigation{};
    std::memset(&invalid_navigation, 0xff, sizeof(invalid_navigation));
    invalid_navigation.struct_size = sizeof(invalid_navigation) - 1;
    invalid_navigation.version =
        OPENUSD_STORM_CHILD_NAVIGATION_INPUT_VERSION;
    openusd_storm_child_navigation_input navigation = NavigationInput();
    passed =
        passed &&
        Require(
            openusd_storm_child_get_navigation_input(
                child,
                nullptr,
                &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
            "Linux navigation input accepted a null output.") &&
        Require(
            openusd_storm_child_get_navigation_input(
                child,
                &invalid_navigation,
                &error) == OPENUSD_STATUS_INVALID_ARGUMENT &&
                IsZeroed(invalid_navigation),
            "Linux navigation input did not zero an invalid layout.") &&
        Require(
            openusd_storm_child_get_navigation_input(
                child,
                &navigation,
                &error) == OPENUSD_STATUS_OK &&
                navigation.sequence == 0,
            "The initial Linux navigation snapshot is invalid.") &&
        Require(
            openusd_storm_child_get_diagnostics(
                child,
                &input_before,
                &error) == OPENUSD_STATUS_OK &&
                input_before.focus_count == 0 &&
                input_before.pointer_count == 0 &&
                input_before.wheel_count == 0 &&
                input_before.key_count == 0,
            "Native input counters were non-zero before real X11 input.") &&
        Require(
            openusd_storm_child_capture_framebuffer(
                child,
                0xff0e0e0eu,
                2,
                OPENUSD_STORM_CHILD_CAPTURE_DRAW_TEST_PATTERN,
                pixels.data(),
                pixels.size(),
                &capture_required,
                &pattern_capture,
                &error) == OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            pattern_capture.non_background_pixel_count == 128u * 96u &&
                pattern_capture.pixel_hash != 0,
            "The known framebuffer pattern is incorrect.") &&
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
                child,
                0,
                &frame_count,
                &converged,
                &error) == OPENUSD_STATUS_OK,
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
            "The live stage edit did not change framebuffer evidence.");

    passed =
        passed &&
        Require(
            openusd_storm_child_resize(
                child,
                300,
                150,
                144,
                &error) == OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            openusd_storm_child_resize(
                child,
                400,
                200,
                192,
                &error) == OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            openusd_storm_child_set_visible(child, 0, &error) ==
                OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            openusd_storm_child_get_diagnostics(
                child,
                &diagnostics,
                &error) == OPENUSD_STATUS_OK &&
                diagnostics.visible == 0,
            "Storm child visibility did not update while hidden.") &&
        Require(
            openusd_storm_child_set_visible(child, 1, &error) ==
                OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            openusd_storm_child_focus(child, &error) == OPENUSD_STATUS_OK,
            error_data);
    passed =
        Require(
            SendDragStart(display, child_window),
            "XTest server-routed drag injection failed.") &&
        passed;
    passed =
        passed &&
        Require(
            openusd_storm_child_render(
                child,
                0,
                &automatic,
                &frame_count,
                &converged,
                &error) == OPENUSD_STATUS_OK,
            error_data);
    navigation = NavigationInput();
    passed =
        passed &&
        Require(
            openusd_storm_child_get_navigation_input(
                child,
                &navigation,
                &error) == OPENUSD_STATUS_OK &&
                navigation.pointer_x == 40 &&
                navigation.pointer_y == 40 &&
                navigation.buttons == OPENUSD_STORM_CHILD_POINTER_BUTTON_LEFT &&
                navigation.modifiers == OPENUSD_STORM_CHILD_MODIFIER_ALT &&
                (navigation.state &
                 (OPENUSD_STORM_CHILD_NAVIGATION_STATE_FOCUSED |
                  OPENUSD_STORM_CHILD_NAVIGATION_STATE_INSIDE)) ==
                    (OPENUSD_STORM_CHILD_NAVIGATION_STATE_FOCUSED |
                     OPENUSD_STORM_CHILD_NAVIGATION_STATE_INSIDE),
            "The Linux XTest Alt-left navigation snapshot is invalid.") &&
        Require(
            SendInputCompletion(display, child_window),
            "XTest server-routed completion injection failed.") &&
        Require(
            openusd_storm_child_render(
                child,
                0,
                &automatic,
                &frame_count,
                &converged,
                &error) == OPENUSD_STATUS_OK,
            error_data);
    navigation = NavigationInput();
    passed =
        passed &&
        Require(
            openusd_storm_child_get_navigation_input(
                child,
                &navigation,
                &error) == OPENUSD_STATUS_OK &&
                navigation.buttons == OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE &&
                navigation.modifiers == OPENUSD_STORM_CHILD_MODIFIER_NONE &&
                navigation.cumulative_wheel_delta == 0.0 &&
                navigation.frame_selected_press_count == 2 &&
                navigation.reset_automatic_press_count == 2 &&
                navigation.toggle_projection_press_count == 2,
            "The Linux held/repressed command navigation snapshot is invalid.");
    const KeyCode p_key = XKeysymToKeycode(display, XK_p);
    passed =
        passed &&
        Require(
            p_key != 0 &&
                XTestFakeKeyEvent(display, p_key, True, CurrentTime) != False &&
                XTestFakeKeyEvent(display, p_key, True, CurrentTime) != False,
            "Could not hold the Linux projection key before focus loss.");
    XFlush(display);
    XSync(display, False);
    passed =
        passed &&
        Require(
            openusd_storm_child_render(
                child,
                0,
                &automatic,
                &frame_count,
                &converged,
                &error) == OPENUSD_STATUS_OK,
            error_data);
    XSetInputFocus(display, parent, RevertToParent, CurrentTime);
    XSync(display, False);
    passed =
        passed &&
        Require(
            openusd_storm_child_render(
                child,
                0,
                &automatic,
                &frame_count,
                &converged,
                &error) == OPENUSD_STATUS_OK,
            error_data);
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
                navigation.buttons == OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE &&
                navigation.modifiers == OPENUSD_STORM_CHILD_MODIFIER_NONE,
            "Linux focus loss did not reset navigation input.") &&
        Require(
            openusd_storm_child_focus(child, &error) == OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            openusd_storm_child_render(
                child,
                0,
                &automatic,
                &frame_count,
                &converged,
                &error) == OPENUSD_STATUS_OK,
            error_data);
    passed =
        passed &&
        Require(
            XTestFakeKeyEvent(display, p_key, False, CurrentTime) != False &&
                XTestFakeKeyEvent(display, p_key, True, CurrentTime) != False &&
                XTestFakeKeyEvent(display, p_key, False, CurrentTime) != False,
            "Could not release and repress the Linux projection key.");
    XFlush(display);
    XSync(display, False);
    passed =
        passed &&
        Require(
            openusd_storm_child_render(
                child,
                0,
                &automatic,
                &frame_count,
                &converged,
                &error) == OPENUSD_STATUS_OK,
            error_data);
    navigation = NavigationInput();
    passed =
        passed &&
        Require(
            openusd_storm_child_get_navigation_input(
                child,
                &navigation,
                &error) == OPENUSD_STATUS_OK &&
                navigation.toggle_projection_press_count == 4,
            "Linux focus loss did not reset the pressed command-key state.");

    passed =
        passed &&
        Require(
            openusd_storm_child_get_diagnostics(
                child,
                &diagnostics,
                &error) == OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            diagnostics.render_thread_id != 0 &&
                diagnostics.render_thread_id != CurrentThreadId() &&
                diagnostics.creator_thread_id == CurrentThreadId(),
            "Storm did not preserve dedicated render/UI thread identity.") &&
        Require(
            diagnostics.gl_major == 4 &&
                diagnostics.gl_minor == 5 &&
                diagnostics.compatibility_profile == 1,
            "A trapped GLX 4.6 failure did not fall back to GLX 4.5.") &&
        Require(
            diagnostics.width == 400 &&
                diagnostics.height == 200 &&
                diagnostics.dpi == 192,
            "Storm child resize/DPI diagnostics are incorrect.") &&
        Require(
            diagnostics.focus_count > input_before.focus_count &&
                diagnostics.pointer_count >= input_before.pointer_count + 3 &&
                diagnostics.wheel_count > input_before.wheel_count &&
                diagnostics.key_count >= input_before.key_count + 2,
            "Native Storm child input counters did not advance.");

    openusd_status wrong_thread_destroy = OPENUSD_STATUS_OK;
    std::thread wrong_destroy([&]
    {
        char worker_error_data[512]{};
        openusd_error_buffer worker_error{
            worker_error_data,
            sizeof(worker_error_data),
            0};
        wrong_thread_destroy =
            openusd_storm_child_destroy(child, &worker_error);
    });
    wrong_destroy.join();
    passed =
        Require(
            wrong_thread_destroy == OPENUSD_STATUS_WRONG_THREAD,
            "Worker-thread child destroy was not rejected.") &&
        passed;

    const size_t abandoned_before =
        openusd_storm_diagnostic_get_abandoned_engine_count();
    const openusd_render_camera persistent_camera = MatrixCamera(0.125);
    const openusd_storm_child_navigation_input before_context_navigation =
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
            "The explicit Storm child camera was not propagated.") &&
        Require(
            openusd_storm_child_simulate_context_loss(child, &error) ==
                OPENUSD_STATUS_OK,
            error_data) &&
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
                navigation.sequence >= before_context_navigation.sequence &&
                navigation.frame_selected_press_count ==
                    before_context_navigation.frame_selected_press_count &&
                navigation.reset_automatic_press_count ==
                    before_context_navigation.reset_automatic_press_count &&
                navigation.toggle_projection_press_count ==
                    before_context_navigation.toggle_projection_press_count,
            "Context recreation did not preserve Linux navigation input.") &&
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
            openusd_storm_diagnostic_get_abandoned_engine_count() ==
                abandoned_before + 1,
            "Context loss did not abandon exactly one Storm engine.") &&
        Require(
            openusd_storm_child_get_diagnostics(
                child,
                &diagnostics,
                &error) == OPENUSD_STATUS_OK &&
                diagnostics.context_generation == 2,
            "Storm context generation did not advance.");

    const auto burst_start = std::chrono::steady_clock::now();
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
            std::chrono::steady_clock::now() - burst_start <
                std::chrono::seconds(5),
            "The 10k request burst exceeded bounded enqueue latency.") &&
        Require(
            openusd_storm_child_get_diagnostics(
                child,
                &diagnostics,
                &error) == OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            diagnostics.latest_requested_revision == 10000 &&
                diagnostics.latest_requested_camera_signature ==
                    CameraSignature(latest_camera) &&
                diagnostics.peak_pending_command_count <= 1 &&
                diagnostics.coalesced_request_count > 0,
            "Asynchronous render requests were not bounded/coalesced.");

    SetFailpoint("destroy-window-xerror");
    const openusd_status trapped_destroy_status =
        openusd_storm_child_destroy(child, &error);
    SetFailpoint(nullptr);
    passed =
        passed &&
        Require(
            trapped_destroy_status == OPENUSD_STATUS_NATIVE_ERROR &&
                openusd_storm_child_diagnostic_get_live_count() == 1,
            "An injected XDestroyWindow error was not retryable.");

    std::atomic_bool run_leases{true};
    std::atomic_bool unexpected_status{false};
    std::vector<std::thread> lease_workers;
    for (int worker = 0; worker < 4; ++worker)
    {
        lease_workers.emplace_back([&, worker]
        {
            char worker_error_data[512]{};
            openusd_error_buffer worker_error{
                worker_error_data,
                sizeof(worker_error_data),
                0};
            uint64_t revision = static_cast<uint64_t>(worker + 1);
            while (run_leases.load(std::memory_order_acquire))
            {
                const openusd_render_camera camera =
                    MatrixCamera(static_cast<double>(revision) * 0.000001);
                openusd_status status =
                    openusd_storm_child_request_frame_v2(
                        child,
                        static_cast<double>(revision),
                        &camera,
                        revision,
                        &worker_error);
                if (status != OPENUSD_STATUS_OK &&
                    status != OPENUSD_STATUS_INVALID_ARGUMENT)
                {
                    unexpected_status.store(true, std::memory_order_release);
                    break;
                }
                openusd_storm_child_diagnostics worker_diagnostics{};
                status = openusd_storm_child_get_diagnostics(
                    child,
                    &worker_diagnostics,
                    &worker_error);
                if (status != OPENUSD_STATUS_OK &&
                    status != OPENUSD_STATUS_INVALID_ARGUMENT)
                {
                    unexpected_status.store(true, std::memory_order_release);
                    break;
                }
                openusd_storm_child_navigation_input worker_navigation =
                    NavigationInput();
                status = openusd_storm_child_get_navigation_input(
                    child,
                    &worker_navigation,
                    &worker_error);
                if (status != OPENUSD_STATUS_OK &&
                    status != OPENUSD_STATUS_INVALID_ARGUMENT)
                {
                    unexpected_status.store(true, std::memory_order_release);
                    break;
                }
                revision += 4;
            }
        });
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
    const openusd_status destroy_status =
        openusd_storm_child_destroy(child, &error);
    run_leases.store(false, std::memory_order_release);
    for (std::thread& worker : lease_workers)
    {
        worker.join();
    }
    passed =
        Require(
            destroy_status == OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            !unexpected_status.load(std::memory_order_acquire),
            "A leased operation returned an unexpected teardown status.") &&
        Require(
            openusd_storm_child_get_diagnostics(
                child,
                &diagnostics,
                &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
            "A released Storm child token remained valid.") &&
        Require(
            ((navigation = NavigationInput()),
             openusd_storm_child_get_navigation_input(
                 child,
                 &navigation,
                 &error) == OPENUSD_STATUS_INVALID_ARGUMENT) &&
                IsZeroed(navigation),
            "A released Linux navigation token remained valid or was not zeroed.") &&
        Require(
            openusd_storm_child_diagnostic_get_live_count() == 0,
            "The native Storm child wrapper leaked.") &&
        passed;

    Window externally_destroyed_parent = XCreateSimpleWindow(
        display,
        RootWindow(display, screen),
        0,
        0,
        160,
        120,
        0,
        BlackPixel(display, screen),
        BlackPixel(display, screen));
    XMapWindow(display, externally_destroyed_parent);
    XSync(display, False);
    openusd_storm_child* externally_destroyed_child = nullptr;
    passed =
        Require(
            openusd_storm_child_create(
                reinterpret_cast<void*>(
                    static_cast<uintptr_t>(externally_destroyed_parent)),
                argv[1],
                stage,
                128,
                96,
                96,
                &externally_destroyed_child,
                &error) == OPENUSD_STATUS_OK,
            error_data) &&
        passed;
    XDestroyWindow(display, externally_destroyed_parent);
    XSync(display, False);
    while (XPending(display) > 0)
    {
        XEvent event{};
        XNextEvent(display, &event);
    }
    std::this_thread::sleep_for(std::chrono::milliseconds(20));
    const openusd_status externally_destroyed_status =
        openusd_storm_child_destroy(externally_destroyed_child, &error);
    passed =
        Require(
            externally_destroyed_status == OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            openusd_storm_child_diagnostic_get_live_count() == 0,
            "Externally destroyed parent teardown leaked the child registry entry.") &&
        Require(
            openusd_storm_diagnostic_get_live_renderer_count() == 0,
            "Externally destroyed parent teardown leaked the Storm renderer.") &&
        Require(
            openusd_storm_child_get_diagnostics(
                externally_destroyed_child,
                &diagnostics,
                &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
            "Externally destroyed child left a valid stale handle.") &&
        passed;

    openusd_stage_release(stage);
    XDestroyWindow(display, parent);
    XCloseDisplay(display);
    return passed ? 0 : 6;
}
