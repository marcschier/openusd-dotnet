// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_storm_child.h"
#include "storm_child_camera_test.h"
#include "openusd_storm_child_internal.h"
#include "openusd_storm_child_macos_input.h"
#include "openusd_hydra.h"

#include <Windows.h>
#include <gl/GL.h>

#include <array>
#include <atomic>
#include <chrono>
#include <cstring>
#include <cstdint>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

namespace
{
using namespace openusd_storm_child_camera_test;

constexpr wchar_t ParentClassName[] = L"OpenUsdStormChildProbeParent";
constexpr char MissingWglCreateContextError[] =
    "WGL_ARB_create_context is unavailable.";
constexpr int CapabilityUnavailableExitCode = 125;

bool Require(bool condition, const char* message)
{
    if (!condition)
    {
        std::cerr << message << '\n';
    }
    return condition;
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
            -OpenUsdStormChildMacMaximumScrollStepsPerEvent;
}

LPARAM PointParameter(int x, int y) noexcept
{
    return static_cast<LPARAM>(
        (static_cast<uint32_t>(static_cast<uint16_t>(x)) & 0xffffu) |
        (static_cast<uint32_t>(static_cast<uint16_t>(y)) << 16));
}

openusd_storm_child* CreateChild(
    HWND parent,
    const char* plugin_path,
    openusd_stage* stage,
    openusd_error_buffer* error)
{
    openusd_storm_child* child = nullptr;
    return openusd_storm_child_create(
               parent,
               plugin_path,
               stage,
               256,
               192,
               96,
               &child,
               error) == OPENUSD_STATUS_OK
        ? child
        : nullptr;
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

openusd_render_pick_request PickRequest(
    const openusd_render_camera& camera,
    int32_t x,
    int32_t y,
    int32_t width,
    int32_t height,
    uint64_t context_generation,
    uint64_t revision = 0,
    double time_code = 0)
{
    openusd_render_pick_request request{};
    request.struct_size = sizeof(request);
    request.version = OPENUSD_RENDER_PICK_REQUEST_VERSION;
    request.x = x;
    request.y = y;
    request.width = 1;
    request.height = 1;
    request.viewport_width = width;
    request.viewport_height = height;
    request.target = OPENUSD_RENDER_PICK_TARGET_PRIMITIVE;
    request.resolve_mode = OPENUSD_RENDER_PICK_RESOLVE_NEAREST_TO_CENTER;
    request.time_code = time_code;
    request.state_revision = revision;
    request.context_generation = context_generation;
    request.camera = camera;
    return request;
}

openusd_status Pick(
    openusd_storm_child* child,
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
    return openusd_storm_child_pick(
        child,
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

bool TestRetryableDestroyFailure(
    HWND parent,
    const char* plugin_path,
    openusd_stage* stage,
    const char* failpoint,
    openusd_error_buffer* error)
{
    openusd_storm_child* child =
        CreateChild(parent, plugin_path, stage, error);
    if (!Require(child != nullptr, "Could not create failure-injection child."))
    {
        return false;
    }
    void* window = nullptr;
    if (!Require(
            openusd_storm_child_get_window(child, &window, error) ==
                OPENUSD_STATUS_OK,
            "Could not get failure-injection child HWND."))
    {
        return false;
    }
    _putenv_s("OPENUSD_STORM_CHILD_FAILPOINT", failpoint);
    const openusd_status failed = openusd_storm_child_destroy(child, error);
    _putenv_s("OPENUSD_STORM_CHILD_FAILPOINT", "");
    const bool window_survived =
        IsWindow(static_cast<HWND>(window)) != FALSE;
    if (!window_survived)
    {
        std::cerr << "HWND did not survive failpoint " << failpoint << ".\n";
    }
    const openusd_status retried = openusd_storm_child_destroy(child, error);
    if (retried != OPENUSD_STATUS_OK)
    {
        std::cerr << "Destroy retry failed for failpoint " << failpoint <<
            ": " << (error->data == nullptr ? "" : error->data) << "\n";
    }
    return
        Require(
            failed == OPENUSD_STATUS_NATIVE_ERROR,
            "Injected child destroy failure was not reported.") &&
        Require(
            window_survived,
            "Failed child destroy did not preserve the HWND for retry.") &&
        Require(
            retried == OPENUSD_STATUS_OK,
            "Child destroy retry did not succeed.") &&
        Require(
            IsWindow(static_cast<HWND>(window)) == FALSE,
            "Child HWND survived a successful destroy retry.");
}
}

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        std::cerr << "Usage: storm_child_probe <plugin-path> <stage-path>\n";
        return 2;
    }

    WNDCLASSW window_class{};
    window_class.lpfnWndProc = DefWindowProcW;
    window_class.hInstance = GetModuleHandleW(nullptr);
    window_class.lpszClassName = ParentClassName;
    if (RegisterClassW(&window_class) == 0 &&
        GetLastError() != ERROR_CLASS_ALREADY_EXISTS)
    {
        std::cerr << "Could not register parent window class.\n";
        return 3;
    }
    HWND parent = CreateWindowExW(
        0,
        ParentClassName,
        L"",
        WS_OVERLAPPEDWINDOW,
        0,
        0,
        320,
        240,
        nullptr,
        nullptr,
        window_class.hInstance,
        nullptr);
    if (!Require(parent != nullptr, "Could not create parent window."))
    {
        return 4;
    }

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
            !OpenUsdStormChildIsValidWglAddress(nullptr) &&
               !OpenUsdStormChildIsValidWglAddress(
                   reinterpret_cast<PROC>(1)) &&
               !OpenUsdStormChildIsValidWglAddress(
                   reinterpret_cast<PROC>(2)) &&
               !OpenUsdStormChildIsValidWglAddress(
                   reinterpret_cast<PROC>(3)) &&
               !OpenUsdStormChildIsValidWglAddress(
                   reinterpret_cast<PROC>(-1)) &&
               OpenUsdStormChildIsValidWglAddress(
                   reinterpret_cast<PROC>(0x10000)),
            "WGL procedure sentinel validation is incorrect.") &&
        Require(
            openusd_register_plugins(argv[1], &plugin_count, &error) ==
                OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            openusd_stage_open(argv[2], &stage, &error) ==
                OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            (child = CreateChild(parent, argv[1], stage, &error)) != nullptr,
            error_data);
    if (!passed)
    {
        const bool capability_unavailable =
            child == nullptr &&
            std::strcmp(error_data, MissingWglCreateContextError) == 0;
        if (stage != nullptr)
        {
            openusd_stage_release(stage);
        }
        DestroyWindow(parent);
        if (capability_unavailable)
        {
            std::cerr <<
                "Skipping Storm child probe: WGL context creation is unavailable.\n";
            return CapabilityUnavailableExitCode;
        }
        return 5;
    }
    const openusd_render_camera automatic = AutomaticCamera();
    passed =
        Require(
            VerifyInvalidCameras(child, &error),
            "Storm child accepted an invalid camera.") &&
        passed;

    void* child_window = nullptr;
    openusd_storm_child_diagnostics diagnostics{};
    openusd_storm_child_navigation_input invalid_navigation{};
    std::memset(&invalid_navigation, 0xff, sizeof(invalid_navigation));
    invalid_navigation.struct_size = sizeof(invalid_navigation);
    invalid_navigation.version =
        OPENUSD_STORM_CHILD_NAVIGATION_INPUT_VERSION + 1;
    openusd_storm_child_navigation_input navigation = NavigationInput();
    openusd_storm_child_framebuffer_capture capture{};
    size_t capture_required = 0;
    uint64_t frame_count = 0;
    int32_t converged = 0;
    DWORD child_process_id = 0;
    passed =
        Require(
            openusd_storm_child_get_window(
                child,
                &child_window,
                &error) == OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            GetParent(static_cast<HWND>(child_window)) == parent,
            "Storm child has the wrong HWND parent.") &&
        Require(
            openusd_storm_child_get_navigation_input(
                child,
                nullptr,
                &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
            "Storm navigation input accepted a null output.") &&
        Require(
            openusd_storm_child_get_navigation_input(
                child,
                &invalid_navigation,
                &error) == OPENUSD_STATUS_INVALID_ARGUMENT &&
                IsZeroed(invalid_navigation),
            "Storm navigation input did not zero an invalid version output.") &&
        Require(
            openusd_storm_child_get_navigation_input(
                child,
                &navigation,
                &error) == OPENUSD_STATUS_OK &&
                navigation.struct_size == sizeof(navigation) &&
                navigation.version ==
                    OPENUSD_STORM_CHILD_NAVIGATION_INPUT_VERSION &&
                navigation.sequence == 0,
            "The initial Storm navigation input snapshot is invalid.") &&
        Require(
            (GetWindowLongPtrW(
                 static_cast<HWND>(child_window),
                 GWL_STYLE) &
             WS_CHILD) != 0,
            "Storm native window is not a WS_CHILD.") &&
        Require(
            GetWindowThreadProcessId(
                static_cast<HWND>(child_window),
                &child_process_id) == GetCurrentThreadId() &&
                child_process_id == GetCurrentProcessId(),
            "Storm child HWND is not owned by the creator UI thread/process.") &&
        Require(
            openusd_storm_child_capture_framebuffer(
                child,
                0xff0e0e0eu,
                2,
                0,
                nullptr,
                0,
                &capture_required,
                &capture,
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

    passed =
        passed &&
        Require(
            openusd_storm_child_get_diagnostics(
                child,
                &diagnostics,
                &error) == OPENUSD_STATUS_OK &&
                diagnostics.context_generation == 1,
            error_data);
    const openusd_render_pick_request center_pick =
        PickRequest(automatic, 128, 96, 256, 192, 1);
    openusd_render_pick_result center_pick_result{};
    std::array<char, 256> center_pick_path{};
    passed =
        passed &&
        Require(
            Pick(
                child,
                center_pick,
                &center_pick_result,
                center_pick_path.data(),
                static_cast<uint32_t>(center_pick_path.size()),
                &error) == OPENUSD_STATUS_OK &&
                center_pick_result.status == OPENUSD_RENDER_PICK_STATUS_HIT &&
                center_pick_path[0] == '/',
            "The prioritized render-thread center pick did not hit.") &&
        Require(
            [&]
            {
                const openusd_render_pick_request empty_pick =
                    PickRequest(automatic, 0, 0, 256, 192, 1);
                openusd_render_pick_result empty_result{};
                std::array<char, 256> ignored_path{};
                return Pick(
                           child,
                           empty_pick,
                           &empty_result,
                           ignored_path.data(),
                           static_cast<uint32_t>(ignored_path.size()),
                           &error) == OPENUSD_STATUS_OK &&
                    empty_result.status == OPENUSD_RENDER_PICK_STATUS_MISS;
            }(),
            "The Storm child empty-pixel pick did not miss.") &&
        Require(
            [&]
            {
                openusd_render_pick_request stale_pick = center_pick;
                stale_pick.state_revision = 99;
                openusd_render_pick_result stale_result{};
                std::array<char, 256> ignored_path{};
                return Pick(
                           child,
                           stale_pick,
                           &stale_result,
                           ignored_path.data(),
                           static_cast<uint32_t>(ignored_path.size()),
                           &error) == OPENUSD_STATUS_OK &&
                    stale_result.status == OPENUSD_RENDER_PICK_STATUS_STALE;
            }(),
            "The Storm child stale pick binding was not deterministic.");
    const std::string center_pick_identity = center_pick_path.data();

    openusd_storm_child_framebuffer_capture initial_capture{};
    openusd_storm_child_framebuffer_capture edited_capture{};
    const uint64_t initial_frame_count = frame_count;
    passed =
        passed &&
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
                initial_capture.dpi == 96 &&
                initial_capture.pixel_count == 256u * 192u &&
                capture_required == 256u * 192u * 4u,
            "The real Storm framebuffer dimensions are incorrect.") &&
        Require(
            (std::cerr
                 << "Initial capture: hash=" << initial_capture.pixel_hash
                 << " nonBackground="
                 << initial_capture.non_background_pixel_count
                 << " min=" << initial_capture.minimum_rgba
                 << " max=" << initial_capture.maximum_rgba
                 << " average=" << initial_capture.average_rgba
                 << " readBuffer=" << initial_capture.read_buffer << '\n',
             true),
            "Could not report initial framebuffer evidence.") &&
        Require(
            initial_capture.non_background_pixel_count > 0 &&
                initial_capture.minimum_rgba != initial_capture.maximum_rgba &&
                initial_capture.read_buffer == 0x8CE0,
            "The real Storm framebuffer contained only the background color.");

    const std::string selected_path = center_pick_identity;
    const openusd_storm_selection_item selected_item{
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
    selection.items = &selected_item;
    selection.path_bytes = selected_path.data();
    selection.path_bytes_size = static_cast<uint32_t>(selected_path.size());
    openusd_storm_child_framebuffer_capture selected_capture{};
    passed =
        passed &&
        Require(
            openusd_storm_child_set_selection(child, &selection, &error) ==
                OPENUSD_STATUS_OK,
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
                &selected_capture,
                &error) == OPENUSD_STATUS_OK &&
                selected_capture.pixel_count == initial_capture.pixel_count &&
                selected_capture.pixel_hash != initial_capture.pixel_hash &&
                (selected_capture.average_rgba != initial_capture.average_rgba ||
                 selected_capture.minimum_rgba != initial_capture.minimum_rgba ||
                 selected_capture.maximum_rgba != initial_capture.maximum_rgba),
            "Storm selection highlighting did not change framebuffer hash and color.");
    if (passed)
    {
        std::cerr << "Child selection evidence: baselineHash="
                  << initial_capture.pixel_hash
                  << " selectedHash=" << selected_capture.pixel_hash
                  << " baselineAverage=" << initial_capture.average_rgba
                  << " selectedAverage=" << selected_capture.average_rgba
                  << "\n";
    }
    selection.item_count = 0;
    selection.items = nullptr;
    selection.path_bytes = nullptr;
    selection.path_bytes_size = 0;
    openusd_storm_child_framebuffer_capture cleared_selection_capture{};
    passed =
        passed &&
        Require(
            openusd_storm_child_set_selection(child, &selection, &error) ==
                OPENUSD_STATUS_OK,
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
                &cleared_selection_capture,
                &error) == OPENUSD_STATUS_OK &&
                cleared_selection_capture.pixel_hash ==
                    initial_capture.pixel_hash &&
                cleared_selection_capture.average_rgba ==
                    initial_capture.average_rgba &&
                cleared_selection_capture.minimum_rgba ==
                    initial_capture.minimum_rgba &&
                cleared_selection_capture.maximum_rgba ==
                    initial_capture.maximum_rgba,
            "ClearSelected did not restore the baseline framebuffer.");
    if (passed)
    {
        std::cerr << "Child selection clear evidence: baselineHash="
                  << initial_capture.pixel_hash
                  << " clearedHash=" << cleared_selection_capture.pixel_hash
                  << " restored=1\n";
    }

    std::vector<uint8_t> pattern_pixels(256u * 192u * 4u);
    openusd_storm_child_framebuffer_capture pattern_capture{};
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
                pattern_capture.pixel_hash != 0 &&
                pattern_capture.minimum_rgba != pattern_capture.maximum_rgba &&
                pattern_capture.read_buffer == 0x8CE0,
            "The known framebuffer pattern statistics are incorrect.");
    openusd_storm_child_framebuffer_capture repeated_pattern{};
    passed =
        passed &&
        Require(
            openusd_storm_child_capture_framebuffer(
                child,
                0xff0e0e0eu,
                2,
                OPENUSD_STORM_CHILD_CAPTURE_DRAW_TEST_PATTERN,
                nullptr,
                0,
                &capture_required,
                &repeated_pattern,
                &error) == OPENUSD_STATUS_OK &&
                repeated_pattern.pixel_hash == pattern_capture.pixel_hash,
            "The known framebuffer pattern hash is not stable.") &&
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
                frame_count > initial_frame_count && converged != 0,
                "A live edit did not produce a converged child frame.") &&
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
            "The render-relevant live edit did not change framebuffer evidence.") &&
        Require(
            openusd_storm_child_resize(
                child,
                4097,
                4096,
                192,
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
                &capture,
                &error) == OPENUSD_STATUS_INVALID_ARGUMENT &&
                capture_required == 0,
            "Framebuffer capture did not enforce the 64 MiB safety limit.") &&
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
            error_data);

    const HWND native_child = static_cast<HWND>(child_window);
    SendMessageW(native_child, WM_SETFOCUS, 0, 0);
    SendMessageW(native_child, WM_SYSKEYDOWN, VK_MENU, 1);
    SendMessageW(native_child, WM_MOUSEMOVE, 0, PointParameter(10, 20));
    SendMessageW(
        native_child,
        WM_LBUTTONDOWN,
        MK_LBUTTON,
        PointParameter(10, 20));
    SendMessageW(
        native_child,
        WM_MOUSEMOVE,
        MK_LBUTTON,
        PointParameter(30, 40));
    navigation = NavigationInput();
    passed =
        passed &&
        Require(
            openusd_storm_child_get_navigation_input(
                child,
                &navigation,
                &error) == OPENUSD_STATUS_OK &&
                navigation.pointer_x == 30 &&
                navigation.pointer_y == 40 &&
                navigation.buttons == OPENUSD_STORM_CHILD_POINTER_BUTTON_LEFT &&
                navigation.modifiers == OPENUSD_STORM_CHILD_MODIFIER_ALT &&
                (navigation.state &
                 (OPENUSD_STORM_CHILD_NAVIGATION_STATE_FOCUSED |
                  OPENUSD_STORM_CHILD_NAVIGATION_STATE_INSIDE)) ==
                    (OPENUSD_STORM_CHILD_NAVIGATION_STATE_FOCUSED |
                     OPENUSD_STORM_CHILD_NAVIGATION_STATE_INSIDE),
            "The Windows Alt-left navigation snapshot is invalid.");
    SendMessageW(native_child, WM_LBUTTONUP, 0, PointParameter(30, 40));
    SendMessageW(native_child, WM_SYSKEYUP, VK_MENU, 0xc0000001u);
    SendMessageW(
        native_child,
        WM_MBUTTONDOWN,
        MK_MBUTTON,
        PointParameter(31, 41));
    navigation = NavigationInput();
    passed =
        passed &&
        Require(
            openusd_storm_child_get_navigation_input(
                child,
                &navigation,
                &error) == OPENUSD_STATUS_OK &&
                navigation.buttons ==
                    OPENUSD_STORM_CHILD_POINTER_BUTTON_MIDDLE,
            "The Windows middle-button navigation mapping is invalid.");
    SendMessageW(native_child, WM_MBUTTONUP, 0, PointParameter(31, 41));
    SendMessageW(
        native_child,
        WM_RBUTTONDOWN,
        MK_RBUTTON,
        PointParameter(32, 42));
    navigation = NavigationInput();
    passed =
        passed &&
        Require(
            openusd_storm_child_get_navigation_input(
                child,
                &navigation,
                &error) == OPENUSD_STATUS_OK &&
                navigation.buttons ==
                    OPENUSD_STORM_CHILD_POINTER_BUTTON_RIGHT,
            "The Windows right-button navigation mapping is invalid.");
    SendMessageW(native_child, WM_RBUTTONUP, 0, PointParameter(32, 42));
    POINT wheel_point{32, 42};
    ClientToScreen(native_child, &wheel_point);
    SendMessageW(
        native_child,
        WM_MOUSEWHEEL,
        static_cast<WPARAM>(WHEEL_DELTA) << 16,
        PointParameter(wheel_point.x, wheel_point.y));
    const WPARAM command_keys[] =
    {
        static_cast<WPARAM>('F'),
        static_cast<WPARAM>(VK_HOME),
        static_cast<WPARAM>('P')
    };
    for (WPARAM key : command_keys)
    {
        SendMessageW(native_child, WM_KEYDOWN, key, 1);
        SendMessageW(native_child, WM_KEYDOWN, key, 0x40000001u);
        SendMessageW(native_child, WM_KEYDOWN, key, 1);
    }
    navigation = NavigationInput();
    passed =
        passed &&
        Require(
            openusd_storm_child_get_navigation_input(
                child,
                &navigation,
                &error) == OPENUSD_STATUS_OK &&
                navigation.frame_selected_press_count == 1 &&
                navigation.reset_automatic_press_count == 1 &&
                navigation.toggle_projection_press_count == 1,
            "Held Windows command keys were not suppressed.");
    for (WPARAM key : command_keys)
    {
        SendMessageW(native_child, WM_KEYUP, key, 0xc0000001u);
        SendMessageW(native_child, WM_KEYDOWN, key, 1);
        SendMessageW(native_child, WM_KEYUP, key, 0xc0000001u);
    }
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
                navigation.cumulative_wheel_delta == 1.0 &&
                navigation.frame_selected_press_count == 2 &&
                navigation.reset_automatic_press_count == 2 &&
                navigation.toggle_projection_press_count == 2,
            "The Windows wheel or release/repress command counters are invalid.");
    const WPARAM arrow_keys[] =
    {
        static_cast<WPARAM>(VK_LEFT),
        static_cast<WPARAM>(VK_RIGHT),
        static_cast<WPARAM>(VK_UP),
        static_cast<WPARAM>(VK_DOWN)
    };
    for (WPARAM key : arrow_keys)
    {
        SendMessageW(native_child, WM_KEYDOWN, key, 1);
        SendMessageW(native_child, WM_KEYDOWN, key, 0x40000001u);
        SendMessageW(native_child, WM_KEYDOWN, key, 1);
        SendMessageW(native_child, WM_KEYUP, key, 0xc0000001u);
    }
    SendMessageW(native_child, WM_SYSKEYDOWN, VK_MENU, 1);
    for (WPARAM key : arrow_keys)
    {
        SendMessageW(native_child, WM_KEYDOWN, key, 1);
        SendMessageW(native_child, WM_KEYDOWN, key, 0x40000001u);
        SendMessageW(native_child, WM_KEYUP, key, 0xc0000001u);
    }
    SendMessageW(native_child, WM_SYSKEYUP, VK_MENU, 0xc0000001u);
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
            "Windows arrow repeats or modifier filtering are invalid.");
    SendMessageW(native_child, WM_MOUSELEAVE, 0, 0);
    navigation = NavigationInput();
    passed =
        passed &&
        Require(
            openusd_storm_child_get_navigation_input(
                child,
                &navigation,
                &error) == OPENUSD_STATUS_OK &&
                (navigation.state &
                 OPENUSD_STORM_CHILD_NAVIGATION_STATE_INSIDE) == 0,
            "The Windows navigation inside state did not clear.");
    SendMessageW(native_child, WM_KEYDOWN, 'P', 1);
    SendMessageW(native_child, WM_KEYDOWN, 'P', 0x40000001u);
    SendMessageW(native_child, WM_KEYDOWN, VK_LEFT, 1);
    SendMessageW(native_child, WM_SYSKEYDOWN, VK_MENU, 1);
    SendMessageW(
        native_child,
        WM_LBUTTONDOWN,
        MK_LBUTTON,
        PointParameter(33, 43));
    SendMessageW(native_child, WM_KILLFOCUS, 0, 0);
    navigation = NavigationInput();
    passed =
        passed &&
        Require(
            openusd_storm_child_get_navigation_input(
                child,
                &navigation,
                &error) == OPENUSD_STATUS_OK &&
                !((navigation.state &
                   OPENUSD_STORM_CHILD_NAVIGATION_STATE_FOCUSED) != 0) &&
                navigation.buttons == OPENUSD_STORM_CHILD_POINTER_BUTTON_NONE &&
                navigation.modifiers == OPENUSD_STORM_CHILD_MODIFIER_NONE,
            "Windows focus loss did not reset navigation buttons and modifiers.");
    SendMessageW(native_child, WM_SETFOCUS, 0, 0);
    SendMessageW(native_child, WM_KEYDOWN, 'P', 1);
    SendMessageW(native_child, WM_KEYUP, 'P', 0xc0000001u);
    SendMessageW(native_child, WM_KEYDOWN, VK_LEFT, 1);
    SendMessageW(native_child, WM_KEYUP, VK_LEFT, 0xc0000001u);
    SendMessageW(native_child, WM_MOUSEMOVE, 0, PointParameter(34, 44));
    SendMessageW(native_child, WM_KEYDOWN, VK_SPACE, 0);
    navigation = NavigationInput();
    passed =
        passed &&
        Require(
            openusd_storm_child_get_navigation_input(
                child,
                &navigation,
                &error) == OPENUSD_STATUS_OK &&
                navigation.toggle_projection_press_count == 4 &&
                navigation.orbit_left_press_count == 4 &&
                navigation.orbit_right_press_count == 2 &&
                navigation.orbit_up_press_count == 2 &&
                navigation.orbit_down_press_count == 2,
            "Windows focus loss did not reset the pressed command-key state.") &&
        Require(
            openusd_storm_child_get_diagnostics(
                child,
                &diagnostics,
                &error) == OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            diagnostics.render_thread_id != 0 &&
                diagnostics.render_thread_id != GetCurrentThreadId(),
            "Storm did not use a dedicated render thread.") &&
        Require(
            diagnostics.creator_thread_id == GetCurrentThreadId(),
            "Storm diagnostics did not preserve the HWND creator thread.") &&
        Require(
            diagnostics.pixel_sample_count >= 2,
            "Storm diagnostic pixel samples were not captured.") &&
        Require(
            diagnostics.gl_major == 4 &&
                diagnostics.gl_minor >= 5 &&
                diagnostics.compatibility_profile == 1,
            "Storm did not create a 4.6/4.5 compatibility context.") &&
        Require(
            diagnostics.width == 400 &&
                diagnostics.height == 200 &&
                diagnostics.dpi == 192,
            "Storm child 150/200 percent DPI transition is incorrect.") &&
        Require(
            diagnostics.focus_count >= 2 &&
                diagnostics.pointer_count >= 10 &&
                diagnostics.wheel_count == 1 &&
                diagnostics.key_count >= 10,
            "Storm child input messages were not observed.");

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
        Require(
            IsWindow(static_cast<HWND>(child_window)) != FALSE,
            "Worker-thread destroy changed the child HWND.") &&
        passed;

    const size_t abandoned_before =
        openusd_storm_diagnostic_get_abandoned_engine_count();
    const uint64_t before_context_loss_frame_count = frame_count;
    const openusd_storm_child_navigation_input before_context_navigation =
        navigation;
    const openusd_render_camera persistent_camera = MatrixCamera(0.125);
    const openusd_render_pick_request old_context_pick =
        PickRequest(persistent_camera, 200, 100, 400, 200, 1);
    openusd_render_pick_result context_lost_pick_result{};
    std::array<char, 256> context_pick_path{};
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
            Pick(
                child,
                old_context_pick,
                &context_lost_pick_result,
                context_pick_path.data(),
                static_cast<uint32_t>(context_pick_path.size()),
                &error) == OPENUSD_STATUS_OK &&
                context_lost_pick_result.status ==
                    OPENUSD_RENDER_PICK_STATUS_STALE &&
                (context_lost_pick_result.flags &
                 OPENUSD_RENDER_PICK_RESULT_STALE_CONTEXT_GENERATION) != 0,
            "A queued Storm pick did not report stale context generation.") &&
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
                    before_context_navigation.toggle_projection_press_count &&
                navigation.orbit_left_press_count ==
                    before_context_navigation.orbit_left_press_count &&
                navigation.orbit_right_press_count ==
                    before_context_navigation.orbit_right_press_count &&
                navigation.orbit_up_press_count ==
                    before_context_navigation.orbit_up_press_count &&
                navigation.orbit_down_press_count ==
                    before_context_navigation.orbit_down_press_count,
            "Context recreation did not preserve native navigation input.") &&
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
            frame_count > before_context_loss_frame_count,
            "Storm did not render after context recreation.") &&
        Require(
            openusd_storm_diagnostic_get_abandoned_engine_count() ==
                abandoned_before + 1,
            "Context loss did not record exactly one abandoned Storm engine.") &&
        Require(
            openusd_storm_child_get_diagnostics(
                child,
                &diagnostics,
                &error) == OPENUSD_STATUS_OK &&
                diagnostics.context_generation == 2,
            "Storm context generation did not advance.") &&
        Require(
            [&]
            {
                const openusd_render_pick_request recovered_pick =
                    PickRequest(persistent_camera, 200, 100, 400, 200, 2);
                openusd_render_pick_result recovered_result{};
                return Pick(
                           child,
                           recovered_pick,
                           &recovered_result,
                           context_pick_path.data(),
                           static_cast<uint32_t>(context_pick_path.size()),
                           &error) == OPENUSD_STATUS_OK &&
                    (recovered_result.status == OPENUSD_RENDER_PICK_STATUS_HIT ||
                     recovered_result.status == OPENUSD_RENDER_PICK_STATUS_MISS);
            }(),
            "Storm picking did not recover on the recreated context.");

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
    const auto burst_elapsed =
        std::chrono::steady_clock::now() - burst_start;
    const openusd_render_camera latest_camera = MatrixCamera(0.01);
    passed =
        passed &&
        Require(
            burst_elapsed < std::chrono::seconds(5),
            "The 10k request burst exceeded the bounded enqueue latency.") &&
        Require(
            openusd_storm_child_get_diagnostics(
                child,
                &diagnostics,
                &error) == OPENUSD_STATUS_OK,
            error_data) &&
        Require(
            diagnostics.latest_requested_revision == 10000,
            "The latest coalesced render revision was not retained.") &&
        Require(
            diagnostics.latest_requested_camera_signature ==
                CameraSignature(latest_camera),
            "The latest coalesced render camera was not retained.") &&
        Require(
            diagnostics.peak_pending_command_count <= 1,
            "The asynchronous render queue was not bounded to one command.") &&
        Require(
            diagnostics.coalesced_request_count > 0,
            "The render burst did not exercise request coalescing.");

    std::atomic_bool start_stress{false};
    std::atomic_bool stop_stress{false};
    std::atomic_uint32_t completed_workers{0};
    std::atomic_uint32_t cancelled_synchronous_workers{0};
    std::atomic_uint32_t cancelled_pick_workers{0};
    std::vector<std::thread> workers;
    for (int worker_index = 0; worker_index < 12; ++worker_index)
    {
        workers.emplace_back([&, worker_index]
        {
            char worker_error_data[512]{};
            openusd_error_buffer worker_error{
                worker_error_data,
                sizeof(worker_error_data),
                0};
            while (!start_stress.load(std::memory_order_acquire))
            {
                std::this_thread::yield();
            }
            for (uint64_t iteration = 0;
                 iteration < 5000 &&
                 !stop_stress.load(std::memory_order_acquire);
                 ++iteration)
            {
                openusd_status status = OPENUSD_STATUS_OK;
                openusd_render_pick_result worker_pick_result{};
                if (worker_index <= 2)
                {
                    uint64_t rendered = 0;
                    int32_t worker_converged = 0;
                    status = openusd_storm_child_render(
                        child,
                        static_cast<double>(iteration),
                        &automatic,
                        &rendered,
                        &worker_converged,
                        &worker_error);
                }
                else if (worker_index <= 4)
                {
                    const openusd_render_camera camera =
                        MatrixCamera(static_cast<double>(iteration) * 0.000001);
                    status = openusd_storm_child_request_frame_v2(
                        child,
                        static_cast<double>(iteration),
                        &camera,
                        iteration,
                        &worker_error);
                }
                else if (worker_index == 5)
                {
                    openusd_storm_child_diagnostics worker_diagnostics{};
                    status = openusd_storm_child_get_diagnostics(
                        child,
                        &worker_diagnostics,
                        &worker_error);
                }
                else if (worker_index == 6)
                {
                    openusd_storm_child_navigation_input worker_input =
                        NavigationInput();
                    status = openusd_storm_child_get_navigation_input(
                        child,
                        &worker_input,
                        &worker_error);
                }
                else if (worker_index == 7)
                {
                    status = openusd_storm_child_resize(
                        child,
                        400,
                        200,
                        192,
                        &worker_error);
                    if (status == OPENUSD_STATUS_WRONG_THREAD)
                    {
                        status = OPENUSD_STATUS_OK;
                    }
                }
                else if (worker_index == 8)
                {
                    status = openusd_storm_child_focus(child, &worker_error);
                    if (status == OPENUSD_STATUS_WRONG_THREAD)
                    {
                        status = OPENUSD_STATUS_OK;
                    }
                }
                else
                {
                    const openusd_render_pick_request worker_pick =
                        PickRequest(automatic, 200, 100, 400, 200, 2);
                    std::array<char, 64> worker_pick_path{};
                    status = Pick(
                        child,
                        worker_pick,
                        &worker_pick_result,
                        worker_pick_path.data(),
                        static_cast<uint32_t>(worker_pick_path.size()),
                        &worker_error);
                }
                if (status != OPENUSD_STATUS_OK)
                {
                    if (worker_index <= 2)
                    {
                        cancelled_synchronous_workers.fetch_add(
                            1,
                            std::memory_order_relaxed);
                    }
                    if (worker_index >= 9 &&
                        worker_pick_result.status ==
                            OPENUSD_RENDER_PICK_STATUS_CANCELLED)
                    {
                        cancelled_pick_workers.fetch_add(
                            1,
                            std::memory_order_relaxed);
                    }
                    break;
                }
            }
            completed_workers.fetch_add(1, std::memory_order_release);
        });
    }
    start_stress.store(true, std::memory_order_release);
    std::this_thread::sleep_for(std::chrono::milliseconds(50));
    passed =
        Require(
            openusd_storm_child_destroy(child, &error) == OPENUSD_STATUS_OK,
            error_data) &&
        passed;
    stop_stress.store(true, std::memory_order_release);
    for (std::thread& worker : workers)
    {
        worker.join();
    }
    passed =
        Require(
            completed_workers.load(std::memory_order_acquire) ==
                static_cast<uint32_t>(workers.size()),
            "Concurrent child operations did not complete during teardown.") &&
        Require(
            cancelled_synchronous_workers.load(std::memory_order_relaxed) > 0,
            "Stop did not complete a queued synchronous render waiter.") &&
        Require(
            cancelled_pick_workers.load(std::memory_order_relaxed) > 0,
            "Stop did not cancel a queued prioritized pick waiter.") &&
        Require(
            IsWindow(static_cast<HWND>(child_window)) == FALSE,
            "UI-thread child destroy left the HWND alive.") &&
        Require(
            openusd_storm_child_get_diagnostics(
                child,
                &diagnostics,
                &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
            "A released Storm child handle remained valid.") &&
        Require(
            openusd_storm_child_capture_framebuffer(
                child,
                0xff0e0e0eu,
                2,
                0,
                nullptr,
                0,
                &capture_required,
                &capture,
                &error) == OPENUSD_STATUS_INVALID_ARGUMENT,
            "Framebuffer capture accepted a released child handle.") &&
        Require(
            ((navigation = NavigationInput()),
             openusd_storm_child_get_navigation_input(
                 child,
                 &navigation,
                 &error) == OPENUSD_STATUS_INVALID_ARGUMENT) &&
                IsZeroed(navigation),
            "Navigation input accepted a released child handle or was not zeroed.") &&
        passed;
    child = nullptr;

    const size_t fallback_before =
        openusd_storm_diagnostic_get_abandoned_engine_count();
    openusd_storm_child* fallback_child =
        CreateChild(parent, argv[1], stage, &error);
    openusd_status fallback_status = OPENUSD_STATUS_NATIVE_ERROR;
    if (Require(
            fallback_child != nullptr,
            "Could not create Storm fallback child."))
    {
        _putenv_s("OPENUSD_STORM_CHILD_FAILPOINT", "storm-destroy");
        fallback_status =
            openusd_storm_child_destroy(fallback_child, &error);
        _putenv_s("OPENUSD_STORM_CHILD_FAILPOINT", "");
    }
    passed =
        Require(
            fallback_status == OPENUSD_STATUS_OK,
            "Storm destroy fallback to abandon failed.") &&
        Require(
            openusd_storm_diagnostic_get_abandoned_engine_count() ==
                fallback_before + 1,
            "Storm destroy fallback did not record the abandoned engine.") &&
        TestRetryableDestroyFailure(
            parent,
            argv[1],
            stage,
            "storm-destroy-and-abandon",
            &error) &&
        TestRetryableDestroyFailure(
            parent,
            argv[1],
            stage,
            "wgl-unbind",
            &error) &&
        TestRetryableDestroyFailure(
            parent,
            argv[1],
            stage,
            "context-delete",
            &error) &&
        TestRetryableDestroyFailure(
            parent,
            argv[1],
            stage,
            "release-dc",
            &error) &&
        TestRetryableDestroyFailure(
            parent,
            argv[1],
            stage,
            "destroy-window",
            &error) &&
        passed;

    openusd_stage_release(stage);
    stage = nullptr;
    passed =
        Require(
            openusd_storm_child_diagnostic_get_live_count() == 0,
            "Storm child wrapper leaked.") &&
        Require(
            openusd_storm_diagnostic_get_live_renderer_count() == 0,
            "Storm renderer wrapper leaked.") &&
        passed;
    DestroyWindow(parent);
    if (passed)
    {
        std::cout
            << "Framebuffer evidence: dimensions=" << initial_capture.width
            << 'x' << initial_capture.height
            << ", dpi=" << initial_capture.dpi
            << ", initialHash=" << initial_capture.pixel_hash
            << ", initialNonBackground="
            << initial_capture.non_background_pixel_count
            << ", initialAverage=" << initial_capture.average_rgba
            << ", initialMin=" << initial_capture.minimum_rgba
            << ", initialMax=" << initial_capture.maximum_rgba
            << ", editedHash=" << edited_capture.pixel_hash
            << ", editedNonBackground="
            << edited_capture.non_background_pixel_count
            << ", editedAverage=" << edited_capture.average_rgba
            << ", patternHash=" << pattern_capture.pixel_hash
            << ", patternNonBackground="
            << pattern_capture.non_background_pixel_count
            << ".\n";
        std::cout << "Storm native child probe passed.\n";
        return 0;
    }
    return 6;
}
