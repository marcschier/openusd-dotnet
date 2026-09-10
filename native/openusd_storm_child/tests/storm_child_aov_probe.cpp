// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_storm_child.h"
#include "storm_child_camera_test.h"

#define NOMINMAX
#include <Windows.h>

#include <algorithm>
#include <cmath>
#include <cstring>
#include <iostream>
#include <memory>
#include <thread>
#include <vector>

namespace
{
using Owner = std::unique_ptr<
    openusd_storm_aov_owner, decltype(&openusd_storm_aov_release)>;

bool Require(bool condition, const char* message)
{
    if (!condition)
    {
        std::cerr << message << '\n';
    }
    return condition;
}

bool VerifyCapture(
    openusd_storm_child*& child,
    openusd_error_buffer& error)
{
    const auto camera = openusd_storm_child_camera_test::AutomaticCamera();
    uint64_t frames = 0;
    int32_t converged = 0;
    for (int iteration = 0; iteration < 64 && converged == 0; ++iteration)
    {
        if (!Require(openusd_storm_child_render(
                child, 0, &camera, &frames, &converged, &error) ==
                OPENUSD_STATUS_OK, error.data))
        {
            return false;
        }
    }
    if (!Require(converged != 0, "The reference native frame did not converge."))
    {
        return false;
    }
    std::vector<uint8_t> reference(256u * 192u * 4u);
    openusd_storm_child_framebuffer_capture original{};
    size_t required = 0;
    if (!Require(openusd_storm_child_capture_framebuffer(
            child, 0xff0e0e0e, 2, 0, reference.data(), reference.size(),
            &required, &original, &error) == OPENUSD_STATUS_OK, error.data))
    {
        return false;
    }

    openusd_storm_aov_request request{};
    request.struct_size = sizeof(request);
    request.version = OPENUSD_STORM_AOV_VERSION;
    request.width = 256;
    request.height = 192;
    request.output_count = 2;
    request.output_kinds[0] = OPENUSD_STORM_AOV_COLOR;
    request.output_kinds[1] = OPENUSD_STORM_AOV_DEPTH;
    request.max_pixels = OPENUSD_STORM_AOV_MAX_PIXELS;
    request.max_working_bytes = OPENUSD_STORM_AOV_MAX_WORKING_BYTES;
    request.camera = camera;
    std::vector<uint8_t> rgba(reference.size());
    openusd_storm_child_framebuffer_capture capture{};
    openusd_storm_aov_owner* captured = nullptr;
    const auto status = openusd_storm_child_capture_aovs(
        child, &request, &captured, rgba.data(), rgba.size(),
        &required, &capture, &error);
    Owner owner(captured, openusd_storm_aov_release);
    if (!Require(status == OPENUSD_STATUS_OK, error.data) ||
        !Require(owner != nullptr, "The child did not transfer an AOV owner.") ||
        !Require(required == rgba.size() &&
            capture.width == 256 && capture.height == 192 &&
            capture.frame_count > original.frame_count &&
            capture.non_background_pixel_count > 1000,
            "The same-command native presentation frame is incomplete.") ||
        !Require(rgba == reference,
            "AOV capture changed the native viewport presentation pixels."))
    {
        return false;
    }
    openusd_storm_aov_view view{};
    view.struct_size = sizeof(view);
    view.version = OPENUSD_STORM_AOV_VERSION;
    if (!Require(openusd_storm_aov_get_view(owner.get(), &view, &error) ==
            OPENUSD_STATUS_OK, error.data) ||
        !Require(view.width == 256 && view.height == 192 &&
            view.output_count == 2 && view.time_code == 0 &&
            view.outputs[0].kind == OPENUSD_STORM_AOV_COLOR &&
            view.outputs[0].format == OPENUSD_STORM_AOV_FORMAT_FLOAT16_VEC4 &&
            view.outputs[0].status == OPENUSD_STORM_AOV_STATUS_READY &&
            view.outputs[0].origin == OPENUSD_STORM_AOV_ORIGIN_TOP_LEFT &&
            view.outputs[0].data_bytes == 256u * 192u * 8u &&
            view.outputs[1].kind == OPENUSD_STORM_AOV_DEPTH &&
            view.outputs[1].format == OPENUSD_STORM_AOV_FORMAT_FLOAT32 &&
            view.outputs[1].status == OPENUSD_STORM_AOV_STATUS_READY &&
            view.outputs[1].depth_convention == OPENUSD_STORM_AOV_DEPTH_OPENGL_WINDOW &&
            view.outputs[1].data_bytes == 256u * 192u * 4u,
            "The child did not return typed, top-down color and normalized depth."))
    {
        return false;
    }
    const auto* depth = reinterpret_cast<const float*>(
        static_cast<const uint8_t*>(view.pixel_data) + view.outputs[1].data_offset);
    if (!Require(std::all_of(depth, depth + 256u * 192u, [](float value)
            { return std::isfinite(value) && value >= 0 && value <= 1; }) &&
            depth[0] == 1 && depth[96u * 256u + 128u] < 1,
            "The returned depth does not contain a foreground cube and clear background."))
    {
        return false;
    }
    const uint64_t sequence = view.capture_id;
    const auto* pixel_begin = static_cast<const uint8_t*>(view.pixel_data);
    const std::vector<uint8_t> detached_reference(
        pixel_begin, pixel_begin + static_cast<size_t>(view.pixel_bytes));
    openusd_storm_child_framebuffer_capture refused{};
    std::memset(&refused, 0xff, sizeof(refused));
    openusd_storm_aov_owner* second = nullptr;
    std::vector<uint8_t> untouched(rgba.size(), 0xa5);
    const auto live_owner_status = openusd_storm_child_capture_aovs(
        child, &request, &second, untouched.data(), untouched.size(),
        &required, &refused, &error);
    Owner unexpected(second, openusd_storm_aov_release);
    const openusd_storm_child_framebuffer_capture empty{};
    if (!Require(live_owner_status != OPENUSD_STATUS_OK && unexpected == nullptr &&
            required == 0 && std::memcmp(&refused, &empty, sizeof(empty)) == 0 &&
            std::all_of(untouched.begin(), untouched.end(),
                [](uint8_t value) { return value == 0xa5; }),
            "A refused second owner published partial native frame output."))
    {
        return false;
    }
    if (!Require(openusd_storm_child_capture_aovs(
            child, &request, &second, nullptr, 0, &required, &refused, &error) ==
            OPENUSD_STATUS_BUFFER_TOO_SMALL && required == rgba.size() &&
            second == nullptr && std::memcmp(&refused, &empty, sizeof(empty)) == 0,
            "The caller buffer admission did not report its exact extent without capture."))
    {
        openusd_storm_aov_release(second);
        return false;
    }
    if (!Require(std::memcmp(view.pixel_data, detached_reference.data(),
            detached_reference.size()) == 0,
            "Refused capture damaged the previously published AOV owner."))
    {
        return false;
    }
    if (!Require(openusd_storm_child_destroy(child, &error) == OPENUSD_STATUS_OK,
            error.data))
    {
        return false;
    }
    child = nullptr;
    bool detached = false;
    std::thread reader([&]
    {
        openusd_storm_aov_view after{};
        after.struct_size = sizeof(after);
        after.version = OPENUSD_STORM_AOV_VERSION;
        detached = openusd_storm_aov_get_view(owner.get(), &after, &error) ==
            OPENUSD_STATUS_OK && after.capture_id == sequence &&
            after.output_count == 2 && after.pixel_bytes == view.pixel_bytes &&
            std::memcmp(after.pixel_data, detached_reference.data(),
                detached_reference.size()) == 0;
        owner.reset();
    });
    reader.join();
    if (!Require(detached, "Detached AOV view/release failed after child destruction."))
    {
        return false;
    }
    std::cout << "STORM_CHILD_AOV_NATIVE_APPEARANCE=PASS\n"
        << "STORM_CHILD_AOV_TYPED_COLOR_DEPTH=PASS\n"
        << "STORM_CHILD_AOV_ATOMIC_REFUSAL=PASS\n"
        << "STORM_CHILD_AOV_DETACHED_OWNER=PASS\n";
    return true;
}
}

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        std::cerr << "Usage: storm_child_aov_probe <plugin-path> <stage-path>\n";
        return 2;
    }
    HWND parent = CreateWindowExW(
        WS_EX_NOACTIVATE, L"STATIC", L"", WS_OVERLAPPEDWINDOW,
        0, 0, 320, 240, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (!Require(parent != nullptr, "Could not create the native parent window."))
    {
        return 3;
    }
    char text[4096]{};
    openusd_error_buffer error{text, sizeof(text), 0};
    size_t plugins = 0;
    openusd_stage* stage = nullptr;
    openusd_storm_child* child = nullptr;
    bool passed =
        Require(openusd_storm_child_get_abi_version() == 9, "Expected child ABI 9.") &&
        Require(openusd_register_plugins(argv[1], &plugins, &error) ==
            OPENUSD_STATUS_OK, text) &&
        Require(openusd_stage_open(argv[2], &stage, &error) ==
            OPENUSD_STATUS_OK, text) &&
        Require(openusd_storm_child_create(parent, argv[1], stage,
            256, 192, 96, &child, &error) == OPENUSD_STATUS_OK, text);
    if (passed)
    {
        passed = VerifyCapture(child, error);
    }
    if (child != nullptr)
    {
        passed = Require(openusd_storm_child_destroy(child, &error) ==
            OPENUSD_STATUS_OK, text) && passed;
    }
    if (stage != nullptr)
    {
        openusd_stage_release(stage);
    }
    DestroyWindow(parent);
    return passed ? 0 : 1;
}
