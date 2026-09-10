// Copyright (c) marcschier. Licensed under the MIT License.

#include "openusd_storm_child.h"
#include "storm_child_camera_test.h"
#include "storm_child_aov_test_hooks.h"

#define NOMINMAX
#include <Windows.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstring>
#include <iostream>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <thread>
#include <vector>

namespace
{
using Phase = OpenUsdStormChildAovTestPhase;
using Owner = std::unique_ptr<
    openusd_storm_aov_owner, decltype(&openusd_storm_aov_release)>;
constexpr int32_t CaptureSize = 64;
constexpr size_t RgbaBytes = CaptureSize * CaptureSize * 4u;
constexpr uint64_t NativeBudget = 128u * 1024u;

std::mutex g_gate;
std::condition_variable g_changed;
Phase g_pause_phase = Phase::BeforeCompanionRender;
bool g_pause = false;
bool g_reached = false;
bool g_resume = false;
std::atomic_size_t g_rgba_allocation{0};

bool Require(bool condition, const char* message)
{
    if (!condition)
    {
        std::cerr << message << '\n';
    }
    return condition;
}

bool VerifyResize(
    HWND parent,
    const char* plugin_path,
    openusd_stage* stage,
    Phase phase,
    int32_t resized_size)
{
    char error_text[4096]{};
    openusd_error_buffer error{error_text, sizeof(error_text), 0};
    openusd_storm_child* child = nullptr;
    if (!Require(openusd_storm_child_create(
            parent, plugin_path, stage, CaptureSize, CaptureSize, 96, &child, &error) ==
            OPENUSD_STATUS_OK, error_text))
    {
        return false;
    }
    openusd_storm_aov_request request{};
    request.struct_size = sizeof(request);
    request.version = OPENUSD_STORM_AOV_VERSION;
    request.width = CaptureSize;
    request.height = CaptureSize;
    request.output_count = 1;
    request.output_kinds[0] = OPENUSD_STORM_AOV_DEPTH;
    request.max_pixels = CaptureSize * CaptureSize;
    request.max_working_bytes = NativeBudget;
    request.camera = openusd_storm_child_camera_test::AutomaticCamera();
    std::vector<uint8_t> rgba(RgbaBytes, 0xa5);
    openusd_storm_child_framebuffer_capture capture{};
    std::memset(&capture, 0xff, sizeof(capture));
    openusd_storm_aov_owner* captured = nullptr;
    size_t required = 99;
    openusd_status status = OPENUSD_STATUS_OK;
    {
        std::lock_guard lock(g_gate);
        g_pause_phase = phase;
        g_pause = true;
        g_reached = false;
        g_resume = false;
        g_rgba_allocation.store(0, std::memory_order_relaxed);
    }
    std::thread capturer([&]
    {
        status = openusd_storm_child_capture_aovs(
            child, &request, &captured, rgba.data(), rgba.size(), &required, &capture, &error);
    });
    bool paused;
    {
        std::unique_lock lock(g_gate);
        paused = g_changed.wait_for(
            lock, std::chrono::seconds(10), [] { return g_reached; });
    }
    char resize_text[4096]{};
    openusd_error_buffer resize_error{resize_text, sizeof(resize_text), 0};
    const openusd_status resize_status = paused
        ? openusd_storm_child_resize(child, resized_size, resized_size, 96, &resize_error)
        : OPENUSD_STATUS_NATIVE_ERROR;
    {
        std::lock_guard lock(g_gate);
        g_resume = true;
        g_changed.notify_all();
    }
    capturer.join();
    Owner owner(captured, openusd_storm_aov_release);
    const openusd_storm_child_framebuffer_capture empty{};
    const size_t allocated = g_rgba_allocation.load(std::memory_order_relaxed);
    std::cout << "CHILD_AOV_RESIZE_BUDGET phase=" << static_cast<int>(phase)
        << " resize=" << resized_size << " admittedRgba=" << RgbaBytes
        << " observedRgba=" << allocated << " budget=" << NativeBudget
        << " status=" << status << '\n';
    bool passed =
        Require(paused, "Capture did not reach the deterministic resize boundary.") &&
        Require(resize_status == OPENUSD_STATUS_OK, resize_text) &&
        Require(allocated <= RgbaBytes,
            "Native resize grew the RGBA allocation beyond the admitted companion budget.") &&
        Require(status != OPENUSD_STATUS_OK && owner == nullptr &&
            required == 0 && std::memcmp(&capture, &empty, sizeof(empty)) == 0 &&
            std::all_of(rgba.begin(), rgba.end(), [](uint8_t value) { return value == 0xa5; }),
            "A resized native capture published success or partial output.");
    owner.reset();
    {
        std::lock_guard lock(g_gate);
        g_pause = false;
    }
    if (paused)
    {
        passed = Require(openusd_storm_child_resize(
            child, CaptureSize, CaptureSize, 96, &resize_error) ==
            OPENUSD_STATUS_OK, resize_text) && passed;
        status = openusd_storm_child_capture_aovs(
            child, &request, &captured, rgba.data(), rgba.size(), &required, &capture, &error);
        owner.reset(captured);
        passed = Require(status == OPENUSD_STATUS_OK, error_text) &&
            Require(owner != nullptr && required == RgbaBytes &&
                capture.width == CaptureSize && capture.height == CaptureSize &&
                g_rgba_allocation.load(std::memory_order_relaxed) == RgbaBytes,
                "Capture did not recover with the original admitted dimensions.") && passed;
        owner.reset();
    }
    passed = Require(openusd_storm_child_destroy(child, &resize_error) ==
        OPENUSD_STATUS_OK, resize_text) && passed;
    return passed;
}
}

void OpenUsdStormChildAovTestHook(Phase phase, std::size_t rgba_bytes)
{
    if (phase == Phase::BeforeRgbaAllocation)
    {
        g_rgba_allocation.store(rgba_bytes, std::memory_order_relaxed);
    }
    std::unique_lock lock(g_gate);
    if (!g_pause || phase != g_pause_phase)
    {
        return;
    }
    g_reached = true;
    g_changed.notify_all();
    if (!g_changed.wait_for(lock, std::chrono::seconds(15), [] { return g_resume; }))
    {
        throw std::runtime_error("Timed out waiting for the native creator-thread resize.");
    }
}

int main(int argc, char** argv)
{
    if (argc != 3)
    {
        std::cerr << "Usage: storm_child_aov_resize_probe <plugin-path> <stage-path>\n";
        return 2;
    }
    HWND parent = CreateWindowExW(
        WS_EX_NOACTIVATE, L"STATIC", L"", WS_OVERLAPPEDWINDOW,
        0, 0, 320, 320, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (!Require(parent != nullptr, "Could not create the native parent window."))
    {
        return 3;
    }
    char text[4096]{};
    openusd_error_buffer error{text, sizeof(text), 0};
    size_t plugins = 0;
    openusd_stage* stage = nullptr;
    bool passed =
        Require(openusd_register_plugins(argv[1], &plugins, &error) ==
            OPENUSD_STATUS_OK, text) &&
        Require(openusd_stage_open(argv[2], &stage, &error) ==
            OPENUSD_STATUS_OK, text);
    if (passed)
    {
        for (Phase phase : {Phase::BeforeCompanionRender,
             Phase::BeforeCompanionReadback, Phase::BeforeRgbaAllocation})
        {
            for (int32_t size : {256, 32})
            {
                passed = VerifyResize(parent, argv[1], stage, phase, size) && passed;
            }
        }
    }
    if (stage != nullptr)
    {
        openusd_stage_release(stage);
    }
    DestroyWindow(parent);
    return passed ? 0 : 1;
}
