// Copyright (c) marcschier. Licensed under the MIT License.

// Concurrency probe. The process wide PhysX runtime, its PxFoundation, and its
// PxPhysics factory are shared by every retained world and by the legacy stage
// and scene entry points, so this probe drives them from several threads at
// once: two threads build, step, query, reset, and release their own worlds
// while two more threads drive the legacy primitive scene ABI. It also checks
// that a rejected call only ever observes the message of its own thread.

#include "openusd_physx.h"
#include "openusd_physx_world.h"
#include "page_builder.h"

#include <atomic>
#include <cstring>
#include <iostream>
#include <limits>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace
{
std::atomic<int> g_failures{0};
std::mutex g_report_mutex;

void Fail(const char* description, const std::string& detail)
{
    g_failures.fetch_add(1, std::memory_order_relaxed);
    const std::lock_guard<std::mutex> lock(g_report_mutex);
    std::cerr << "check failed: " << description;
    if (!detail.empty())
    {
        std::cerr << " (" << detail << ')';
    }
    std::cerr << '\n';
}

bool Check(bool condition, const char* description, const std::string& detail = std::string())
{
    if (!condition)
    {
        Fail(description, detail);
    }
    return condition;
}

bool CheckStatus(openusd_physx_status status, const char* description, const char* error_text)
{
    if (status != OPENUSD_PHYSX_STATUS_OK)
    {
        Fail(description, "status " + std::to_string(static_cast<int>(status)) + ": " + error_text);
        return false;
    }
    return true;
}

struct ResultStorage
{
    std::vector<openusd_physx_body_state> body_states;
    std::vector<openusd_physx_event> events;
    std::vector<openusd_physx_diagnostic> diagnostics;
    std::vector<openusd_physx_debug_line> debug_lines;

    explicit ResultStorage(const openusd_physx_result_capacities& capacities)
        : body_states(capacities.max_body_states)
        , events(capacities.max_events)
        , diagnostics(capacities.max_diagnostics)
        , debug_lines(capacities.max_debug_lines)
    {
    }

    openusd_physx_result_page Page()
    {
        openusd_physx_result_page page{};
        page.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_result_page));
        page.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION;
        page.body_states = body_states.empty() ? nullptr : body_states.data();
        page.body_state_capacity = body_states.size();
        page.events = events.empty() ? nullptr : events.data();
        page.event_capacity = events.size();
        page.diagnostics = diagnostics.empty() ? nullptr : diagnostics.data();
        page.diagnostic_capacity = diagnostics.size();
        page.debug_lines = debug_lines.empty() ? nullptr : debug_lines.data();
        page.debug_line_capacity = debug_lines.size();
        return page;
    }
};

// Builds, steps, queries, resets, and rebuilds one world. Every thread owns its
// own world, page, and result buffers, so a failure here means the shared
// runtime, the shared factory, or the shared error storage raced.
void RunWorldThread(unsigned iterations)
{
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};

    openusd_physx_test::PageBuilder builder = openusd_physx_test::MakeReferenceScene();
    const std::vector<uint64_t> page = builder.Build();
    const size_t page_size = builder.Size();

    for (unsigned iteration = 0; iteration < iterations; ++iteration)
    {
        openusd_physx_world_desc world_desc{};
        world_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_desc));
        world_desc.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION;
        world_desc.worker_thread_count = 1;
        world_desc.flags = OPENUSD_PHYSX_WORLD_FLAG_ENABLE_EVENTS;

        openusd_physx_world* world = nullptr;
        if (!CheckStatus(
                openusd_physx_world_create(&world_desc, &world, &error),
                "concurrent world_create",
                error_data) ||
            !Check(world != nullptr, "concurrent world_create returns a world"))
        {
            return;
        }

        openusd_physx_page_validation validation{};
        validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
        if (!CheckStatus(
                openusd_physx_world_build(world, page.data(), page_size, &validation, &error),
                "concurrent world_build",
                error_data))
        {
            openusd_physx_world_release(world);
            return;
        }
        Check(validation.actor_count == 4, "concurrent build reports the reference actor count");

        openusd_physx_world_status_info status{};
        status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
        if (!CheckStatus(
                openusd_physx_world_get_status(world, &status, &error),
                "concurrent world_get_status",
                error_data))
        {
            openusd_physx_world_release(world);
            return;
        }

        ResultStorage storage(status.capacities);
        openusd_physx_result_page results = storage.Page();

        openusd_physx_step_desc step_desc{};
        step_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_step_desc));
        step_desc.fixed_time_step = 1.0 / 120.0;
        step_desc.substep_count = 2;

        for (unsigned step = 0; step < 30; ++step)
        {
            if (!CheckStatus(
                    openusd_physx_world_step(world, &step_desc, &results, &error),
                    "concurrent world_step",
                    error_data))
            {
                openusd_physx_world_release(world);
                return;
            }
        }
        Check(
            results.header.body_state_count == status.dynamic_actor_count,
            "concurrent step reports one state per movable actor");

        openusd_physx_query_request request{};
        request.user_id = 1;
        request.type = OPENUSD_PHYSX_QUERY_RAYCAST;
        request.origin = openusd_physx_vec3f{10.0F, 10.0F, 0.0F};
        request.direction = openusd_physx_vec3f{0.0F, -1.0F, 0.0F};
        request.max_distance = 100.0F;
        request.max_hits = 4;

        std::vector<openusd_physx_query_hit> hits(8);
        openusd_physx_query_desc query_desc{};
        query_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_query_desc));
        query_desc.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION;
        query_desc.requests = &request;
        query_desc.request_count = 1;
        query_desc.hits = hits.data();
        query_desc.hit_capacity = hits.size();

        openusd_physx_query_result query_result{};
        query_result.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_query_result));
        if (!CheckStatus(
                openusd_physx_world_query(world, &query_desc, &query_result, &error),
                "concurrent world_query",
                error_data))
        {
            openusd_physx_world_release(world);
            return;
        }
        Check(query_result.hit_count > 0, "the concurrent downward ray reports at least one hit");

        if (!CheckStatus(openusd_physx_world_reset(world, nullptr, &error), "concurrent world_reset", error_data))
        {
            openusd_physx_world_release(world);
            return;
        }

        // A rebuild tears the previous content down and recreates it, so the
        // shared factory sees releases and creations from this thread while the
        // other threads are still creating and releasing their own objects.
        if (!CheckStatus(
                openusd_physx_world_build(world, page.data(), page_size, &validation, &error),
                "concurrent world_build rebuild",
                error_data))
        {
            openusd_physx_world_release(world);
            return;
        }

        openusd_physx_world_release(world);
    }
}

// Drives the legacy primitive scene ABI, which shares the same runtime and
// factory as the retained worlds.
void RunLegacySceneThread(unsigned iterations)
{
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};

    for (unsigned iteration = 0; iteration < iterations; ++iteration)
    {
        openusd_physx_scene* scene = nullptr;
        if (!CheckStatus(
                openusd_physx_scene_create({0.0F, -9.81F, 0.0F}, &scene, &error),
                "concurrent scene_create",
                error_data) ||
            !Check(scene != nullptr, "concurrent scene_create returns a scene"))
        {
            return;
        }

        if (!CheckStatus(
                openusd_physx_scene_add_static_plane(scene, 0.0F, 0.8F, 0.6F, 0.1F, &error),
                "concurrent scene_add_static_plane",
                error_data))
        {
            openusd_physx_scene_release(scene);
            return;
        }

        for (unsigned index = 0; index < 4; ++index)
        {
            const float height = 1.0F + static_cast<float>(index) * 2.0F;
            if (!CheckStatus(
                    openusd_physx_scene_add_dynamic_box(
                        scene,
                        {0.0F, height, 0.0F},
                        {0.0F, 0.0F, 0.0F, 1.0F},
                        {0.5F, 0.5F, 0.5F},
                        {0.0F, 0.0F, 0.0F},
                        {0.0F, 0.0F, 0.0F},
                        1000.0F,
                        0.5F,
                        0.5F,
                        0.0F,
                        &error),
                    "concurrent scene_add_dynamic_box",
                    error_data))
            {
                openusd_physx_scene_release(scene);
                return;
            }
        }

        if (!CheckStatus(
                openusd_physx_scene_step(scene, 1.0F / 120.0F, 60, &error),
                "concurrent scene_step",
                error_data))
        {
            openusd_physx_scene_release(scene);
            return;
        }

        openusd_physx_transform transforms[4]{};
        size_t count = 0;
        if (CheckStatus(
                openusd_physx_scene_get_dynamic_transforms(scene, transforms, 4, &count, &error),
                "concurrent scene_get_dynamic_transforms",
                error_data))
        {
            Check(count == 4, "the legacy scene reports every dynamic body");
            for (size_t index = 0; index < count; ++index)
            {
                Check(
                    transforms[index].position.y > 0.0F,
                    "a legacy body stays above the ground plane");
            }
        }

        openusd_physx_scene_release(scene);
    }
}

// Creates and releases worlds and legacy scenes back to back. Release drops the
// last runtime reference of this thread while other threads are still holding
// theirs, and it is also the path where the factory lock must already be gone
// before the runtime reference is destroyed: a violation is reported by the
// runtime as a failed Acquire, which fails the checks below.
void RunLifetimeChurnThread(unsigned iterations)
{
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};

    for (unsigned iteration = 0; iteration < iterations; ++iteration)
    {
        openusd_physx_world_desc world_desc{};
        world_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_desc));
        world_desc.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION;
        world_desc.worker_thread_count = 1;

        openusd_physx_world* world = nullptr;
        if (!CheckStatus(
                openusd_physx_world_create(&world_desc, &world, &error),
                "churn world_create",
                error_data))
        {
            return;
        }
        openusd_physx_world_release(world);

        openusd_physx_scene* scene = nullptr;
        if (!CheckStatus(
                openusd_physx_scene_create({0.0F, -9.81F, 0.0F}, &scene, &error),
                "churn scene_create",
                error_data))
        {
            return;
        }
        openusd_physx_scene_release(scene);
    }
}

// Every rejected call must observe the message produced by its own thread. The
// runtime keeps the PhysX error text in thread local storage, so a message from
// another thread can never be consumed or observed here.
void RunErrorIsolationThread(unsigned iterations, const char* expected_fragment, bool legacy)
{
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};

    for (unsigned iteration = 0; iteration < iterations; ++iteration)
    {
        std::memset(error_data, 0, sizeof(error_data));
        error.required = 0;

        openusd_physx_status status = OPENUSD_PHYSX_STATUS_OK;
        if (legacy)
        {
            openusd_physx_scene* scene = nullptr;
            const float not_a_number = std::numeric_limits<float>::quiet_NaN();
            status = openusd_physx_scene_create({0.0F, not_a_number, 0.0F}, &scene, &error);
            Check(scene == nullptr, "a rejected scene_create clears its output");
        }
        else
        {
            openusd_physx_world_desc world_desc{};
            world_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_desc));
            world_desc.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION + 1u;
            openusd_physx_world* world = nullptr;
            status = openusd_physx_world_create(&world_desc, &world, &error);
            Check(world == nullptr, "a rejected world_create clears its output");
        }

        Check(status != OPENUSD_PHYSX_STATUS_OK, "the rejected call reports a failure");
        const std::string message(error_data);
        Check(
            message.find(expected_fragment) != std::string::npos,
            "the failing call reads back its own message",
            message);
    }
}
} // namespace

int main()
{
    constexpr unsigned iterations = 4;
    constexpr unsigned error_iterations = 64;
    constexpr unsigned churn_iterations = 32;

    std::vector<std::thread> threads;
    threads.emplace_back([]() { RunWorldThread(iterations); });
    threads.emplace_back([]() { RunWorldThread(iterations); });
    threads.emplace_back([]() { RunLegacySceneThread(iterations); });
    threads.emplace_back([]() { RunLegacySceneThread(iterations); });
    threads.emplace_back([]() { RunLifetimeChurnThread(churn_iterations); });
    threads.emplace_back([]() { RunLifetimeChurnThread(churn_iterations); });
    threads.emplace_back([]() { RunErrorIsolationThread(error_iterations, "ABI version", false); });
    threads.emplace_back([]() { RunErrorIsolationThread(error_iterations, "gravity", true); });

    for (std::thread& worker : threads)
    {
        worker.join();
    }

    const int failures = g_failures.load(std::memory_order_relaxed);
    if (failures != 0)
    {
        std::cerr << failures << " concurrency checks failed\n";
        return 1;
    }
    std::cout << "concurrency probe passed\n";
    return 0;
}
