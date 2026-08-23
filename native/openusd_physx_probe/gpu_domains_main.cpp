// Copyright (c) marcschier. Licensed under the MIT License.

// CUDA accelerated domain probe. This binary requires the simulation SDK and
// drives a real world that mixes CPU rigid bodies with a position based
// dynamics particle system, a surface deformable, and a finite element volume
// deformable.
//
// The probe asserts the same contract on both kinds of machine:
//
//   * With a usable device, every GPU object must be built, must publish a
//     deformation window, and must actually move. Nothing is accepted on faith:
//     a body that does not deform fails the probe.
//   * Without a usable device, every GPU object must be skipped one by one with
//     a diagnostic that names it, the rigid bodies of the same build must still
//     fall and come to rest, and the world must still step, reset, and report a
//     consistent status.
//
// There is deliberately no third path. The domains are never emulated on the
// CPU, so a run without a device proves graceful degradation rather than a
// weaker version of the same simulation.

#include "openusd_physx_world.h"
#include "page_builder.h"

#include <cmath>
#include <cstring>
#include <iostream>
#include <string>
#include <vector>

namespace
{
using openusd_physx_test::MakeGpuDomainScene;
using openusd_physx_test::PageBuilder;

int g_failures = 0;

bool Check(bool condition, const std::string& description)
{
    if (!condition)
    {
        ++g_failures;
        std::cerr << "check failed: " << description << '\n';
    }
    return condition;
}

bool CheckStatus(openusd_physx_status status, openusd_physx_status expected, const std::string& description)
{
    if (status != expected)
    {
        ++g_failures;
        std::cerr << "check failed: " << description << " (status " << status << ", expected " << expected << ")\n";
        return false;
    }
    return true;
}

struct ResultStorage
{
    std::vector<openusd_physx_body_state> body_states;
    std::vector<openusd_physx_event> events;
    std::vector<openusd_physx_diagnostic> diagnostics;
    std::vector<openusd_physx_deformation_state> deformations;
    std::vector<openusd_physx_vec3f> deformation_points;

    explicit ResultStorage(const openusd_physx_result_capacities& capacities)
        : body_states(capacities.max_body_states)
        , events(capacities.max_events)
        , diagnostics(capacities.max_diagnostics)
        , deformations(capacities.max_deformation_bodies)
        , deformation_points(capacities.max_deformation_points)
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
        page.deformations = deformations.empty() ? nullptr : deformations.data();
        page.deformation_capacity = deformations.size();
        page.deformation_points = deformation_points.empty() ? nullptr : deformation_points.data();
        page.deformation_point_capacity = deformation_points.size();
        return page;
    }
};

const openusd_physx_body_state* FindState(const openusd_physx_result_page& page, uint64_t id)
{
    for (uint32_t index = 0; index < page.header.body_state_count; ++index)
    {
        if (page.body_states[index].id == id)
        {
            return &page.body_states[index];
        }
    }
    return nullptr;
}

const openusd_physx_deformation_state* FindDeformation(const openusd_physx_result_page& page, uint64_t id)
{
    for (uint32_t index = 0; index < page.header.deformation_body_count; ++index)
    {
        if (page.deformations[index].id == id)
        {
            return &page.deformations[index];
        }
    }
    return nullptr;
}

// Sum of the squared distance between two vertex windows. It is the honest
// measure of "did this body deform or move at all" without assuming a direction.
double WindowDisplacement(
    const std::vector<openusd_physx_vec3f>& before,
    const std::vector<openusd_physx_vec3f>& after)
{
    const size_t count = before.size() < after.size() ? before.size() : after.size();
    double total = 0.0;
    for (size_t index = 0; index < count; ++index)
    {
        const double dx = static_cast<double>(after[index].x) - static_cast<double>(before[index].x);
        const double dy = static_cast<double>(after[index].y) - static_cast<double>(before[index].y);
        const double dz = static_cast<double>(after[index].z) - static_cast<double>(before[index].z);
        total += (dx * dx) + (dy * dy) + (dz * dz);
    }
    return total;
}

std::vector<openusd_physx_vec3f> CaptureWindow(
    const openusd_physx_result_page& page, const openusd_physx_deformation_state& state)
{
    std::vector<openusd_physx_vec3f> window(state.point_count);
    for (uint32_t index = 0; index < state.point_count; ++index)
    {
        window[index] = page.deformation_points[state.point_offset + index];
    }
    return window;
}

bool AllFinite(const std::vector<openusd_physx_vec3f>& window)
{
    for (const openusd_physx_vec3f& point : window)
    {
        if (!std::isfinite(point.x) || !std::isfinite(point.y) || !std::isfinite(point.z))
        {
            return false;
        }
    }
    return true;
}

// Counts the diagnostics that name one identity with the object skip code.
uint32_t CountSkips(const openusd_physx_result_page& page, uint64_t id)
{
    uint32_t count = 0;
    for (uint32_t index = 0; index < page.header.diagnostic_count; ++index)
    {
        const openusd_physx_diagnostic& diagnostic = page.diagnostics[index];
        if (diagnostic.id == id &&
            (diagnostic.code == OPENUSD_PHYSX_DIAGNOSTIC_GPU_OBJECT_SKIPPED ||
             diagnostic.code == OPENUSD_PHYSX_DIAGNOSTIC_GPU_UNAVAILABLE))
        {
            ++count;
        }
    }
    return count;
}
}

int main()
{
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};

    openusd_physx_capabilities capabilities{};
    capabilities.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_capabilities));
    CheckStatus(
        openusd_physx_world_get_capabilities(OPENUSD_PHYSX_WORLD_ABI_VERSION, &capabilities, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "world_get_capabilities");
    const bool device_available = (capabilities.flags & OPENUSD_PHYSX_CAPABILITY_CUDA_CONTEXT) != 0;
    // The domain bits are derived from the context bit, so they must move
    // together. A runtime that claimed a particle system without a context
    // would be advertising something it can never build.
    Check(
        device_available ==
            ((capabilities.flags & OPENUSD_PHYSX_CAPABILITY_PARTICLE_SYSTEMS) != 0),
        "the particle system capability is published exactly when a CUDA context is operational");
    Check(
        device_available ==
            ((capabilities.flags & OPENUSD_PHYSX_CAPABILITY_SURFACE_DEFORMABLES) != 0),
        "the surface deformable capability is published exactly when a CUDA context is operational");
    Check(
        device_available ==
            ((capabilities.flags & OPENUSD_PHYSX_CAPABILITY_VOLUME_DEFORMABLES) != 0),
        "the volume deformable capability is published exactly when a CUDA context is operational");
    Check(
        device_available == ((capabilities.flags & OPENUSD_PHYSX_CAPABILITY_GPU_DOMAINS) != 0),
        "the GPU domain capability is published exactly when a CUDA context is operational");

    PageBuilder builder = MakeGpuDomainScene();
    const uint64_t particle_system_id =
        PageBuilder::ComputeIdentity("/World/ParticleSystem", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0);
    const uint64_t granules_id =
        PageBuilder::ComputeIdentity("/World/Granules", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0);
    const uint64_t fluid_id = PageBuilder::ComputeIdentity("/World/Fluid", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0);
    const uint64_t cloth_id = PageBuilder::ComputeIdentity("/World/Cloth", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0);
    const uint64_t jelly_id = PageBuilder::ComputeIdentity("/World/Jelly", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0);
    const uint64_t floater_id = PageBuilder::ComputeIdentity("/World/Floater", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0);
    const uint64_t falling_id = PageBuilder::ComputeIdentity("/World/SphereBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0);

    std::vector<uint64_t> page = builder.Build();
    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    if (!CheckStatus(
            openusd_physx_page_validate(page.data(), builder.Size(), &validation, &error),
            OPENUSD_PHYSX_STATUS_OK,
            "the GPU domain page validates"))
    {
        std::cerr << "  reported message: " << error_data << '\n';
        return 1;
    }

    openusd_physx_world_desc world_desc{};
    world_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_desc));
    world_desc.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION;
    world_desc.worker_thread_count = 2;
    world_desc.flags = OPENUSD_PHYSX_WORLD_FLAG_ENABLE_EVENTS;
    openusd_physx_world* world = nullptr;
    if (!CheckStatus(
            openusd_physx_world_create(&world_desc, &world, &error), OPENUSD_PHYSX_STATUS_OK, "world_create") ||
        world == nullptr)
    {
        std::cerr << "the world could not be created: " << error_data << '\n';
        return 1;
    }

    if (!CheckStatus(
            openusd_physx_world_build(world, page.data(), builder.Size(), &validation, &error),
            OPENUSD_PHYSX_STATUS_OK,
            "the GPU domain page builds"))
    {
        std::cerr << "  reported message: " << error_data << '\n';
        openusd_physx_world_release(world);
        return 1;
    }

    openusd_physx_world_status_info info{};
    info.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
    CheckStatus(openusd_physx_world_get_status(world, &info, &error), OPENUSD_PHYSX_STATUS_OK, "world_get_status");
    Check(info.state == OPENUSD_PHYSX_WORLD_STATE_READY, "a built world reports the ready state");

    if (device_available)
    {
        Check(info.particle_system_count == 1, "the built world owns the declared particle system");
        Check(info.particle_body_count == 2, "the built world owns both particle bodies");
        Check(info.deformable_surface_count == 1, "the built world owns the declared surface deformable");
        Check(info.deformable_volume_count == 2, "the built world owns both declared volume deformables");
        Check(info.deformation_body_count == 5, "every GPU object publishes one deformation body");
    }
    else
    {
        Check(info.particle_system_count == 0, "a world without a device owns no particle system");
        Check(info.particle_body_count == 0, "a world without a device owns no particle body");
        Check(info.deformable_surface_count == 0, "a world without a device owns no surface deformable");
        Check(info.deformable_volume_count == 0, "a world without a device owns no volume deformable");
        Check(info.deformation_body_count == 0, "a world without a device publishes no deformation body");
    }
    Check(
        info.dynamic_actor_count >= 2,
        "the CPU rigid bodies of the same build are still simulated regardless of the device");

    ResultStorage storage(info.capacities);
    openusd_physx_result_page results = storage.Page();

    openusd_physx_step_desc step{};
    step.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_step_desc));
    step.fixed_time_step = 1.0 / 60.0;
    step.substep_count = 1;

    // The first step carries the build diagnostics, which is where a skipped
    // object is reported.
    if (!CheckStatus(
            openusd_physx_world_step(world, &step, &results, &error), OPENUSD_PHYSX_STATUS_OK, "the first step runs"))
    {
        std::cerr << "  reported message: " << error_data << '\n';
        openusd_physx_world_release(world);
        return 1;
    }

    if (!device_available)
    {
        Check(
            CountSkips(results, particle_system_id) == 1,
            "a world without a device reports exactly one skip for the particle system");
        Check(
            CountSkips(results, cloth_id) == 1,
            "a world without a device reports exactly one skip for the surface deformable");
        Check(
            CountSkips(results, jelly_id) == 1,
            "a world without a device reports exactly one skip for the volume deformable");
        Check(
            CountSkips(results, floater_id) == 1,
            "a world without a device reports exactly one skip for the gravity free volume deformable");
        Check(
            results.header.deformation_body_count == 0,
            "a world without a device publishes no deformation window");
        Check(
            (results.header.overflow_flags & OPENUSD_PHYSX_OVERFLOW_DEFORMATION) == 0,
            "a world without a device never reports a deformation overflow");
    }

    std::vector<openusd_physx_vec3f> granules_before;
    std::vector<openusd_physx_vec3f> fluid_before;
    std::vector<openusd_physx_vec3f> cloth_before;
    std::vector<openusd_physx_vec3f> jelly_before;
    std::vector<openusd_physx_vec3f> floater_before;
    if (device_available)
    {
        const openusd_physx_deformation_state* granules = FindDeformation(results, granules_id);
        const openusd_physx_deformation_state* fluid = FindDeformation(results, fluid_id);
        const openusd_physx_deformation_state* cloth = FindDeformation(results, cloth_id);
        const openusd_physx_deformation_state* jelly = FindDeformation(results, jelly_id);
        if (Check(granules != nullptr, "the solid particle body publishes a deformation window") &&
            Check(fluid != nullptr, "the fluid particle body publishes a deformation window") &&
            Check(cloth != nullptr, "the surface deformable publishes a deformation window") &&
            Check(jelly != nullptr, "the volume deformable publishes a deformation window"))
        {
            Check(
                granules->kind == OPENUSD_PHYSX_DEFORMATION_PARTICLES,
                "a solid particle body publishes the particle deformation kind");
            Check(
                fluid->kind == OPENUSD_PHYSX_DEFORMATION_FLUID,
                "a fluid particle body publishes the fluid deformation kind");
            Check(
                cloth->kind == OPENUSD_PHYSX_DEFORMATION_SURFACE,
                "a surface deformable publishes the surface deformation kind");
            Check(
                jelly->kind == OPENUSD_PHYSX_DEFORMATION_VOLUME,
                "a volume deformable publishes the volume deformation kind");
            Check(cloth->point_count == 9, "the surface publishes one vertex per authored point");
            Check(jelly->point_count == 8, "the volume publishes one vertex per authored simulation point");
            granules_before = CaptureWindow(results, *granules);
            fluid_before = CaptureWindow(results, *fluid);
            cloth_before = CaptureWindow(results, *cloth);
            jelly_before = CaptureWindow(results, *jelly);
            Check(AllFinite(cloth_before), "every published surface vertex is finite");
            Check(AllFinite(jelly_before), "every published volume vertex is finite");
        }
        const openusd_physx_deformation_state* floater = FindDeformation(results, floater_id);
        if (Check(floater != nullptr, "the gravity free volume deformable publishes a deformation window"))
        {
            floater_before = CaptureWindow(results, *floater);
        }
    }

    const openusd_physx_body_state* falling_first = FindState(results, falling_id);
    const float falling_first_height = falling_first != nullptr ? falling_first->pose.position.y : 0.0F;

    for (int index = 0; index < 90; ++index)
    {
        if (!CheckStatus(
                openusd_physx_world_step(world, &step, &results, &error),
                OPENUSD_PHYSX_STATUS_OK,
                "a later step runs"))
        {
            break;
        }
    }

    const openusd_physx_body_state* falling_last = FindState(results, falling_id);
    if (Check(falling_last != nullptr, "the CPU rigid body still publishes a state"))
    {
        Check(
            falling_last->pose.position.y < falling_first_height,
            "the CPU rigid body of the same build still falls, whether or not the device exists");
    }

    if (device_available)
    {
        const openusd_physx_deformation_state* granules = FindDeformation(results, granules_id);
        const openusd_physx_deformation_state* fluid = FindDeformation(results, fluid_id);
        const openusd_physx_deformation_state* cloth = FindDeformation(results, cloth_id);
        const openusd_physx_deformation_state* jelly = FindDeformation(results, jelly_id);
        if (Check(granules != nullptr, "the solid particle body still publishes after stepping") &&
            Check(fluid != nullptr, "the fluid particle body still publishes after stepping") &&
            Check(cloth != nullptr, "the surface deformable still publishes after stepping") &&
            Check(jelly != nullptr, "the volume deformable still publishes after stepping"))
        {
            const std::vector<openusd_physx_vec3f> granules_after = CaptureWindow(results, *granules);
            const std::vector<openusd_physx_vec3f> fluid_after = CaptureWindow(results, *fluid);
            const std::vector<openusd_physx_vec3f> cloth_after = CaptureWindow(results, *cloth);
            const std::vector<openusd_physx_vec3f> jelly_after = CaptureWindow(results, *jelly);
            Check(AllFinite(granules_after), "every published particle position stays finite");
            Check(AllFinite(fluid_after), "every published fluid position stays finite");
            Check(AllFinite(cloth_after), "every published surface vertex stays finite");
            Check(AllFinite(jelly_after), "every published volume vertex stays finite");
            Check(
                WindowDisplacement(granules_before, granules_after) > 1.0e-4,
                "the solid particles actually moved under gravity");
            Check(
                WindowDisplacement(fluid_before, fluid_after) > 1.0e-4,
                "the fluid particles actually moved under gravity");
            Check(
                WindowDisplacement(cloth_before, cloth_after) > 1.0e-4,
                "the surface deformable actually deformed");
            Check(
                WindowDisplacement(jelly_before, jelly_after) > 1.0e-4,
                "the volume deformable actually deformed");
        }
        // The gravity opt out is a behaviour, so it is proven by comparing two
        // volumes that differ by nothing else: the one that keeps gravity has to
        // travel further than the one that opted out.
        const openusd_physx_deformation_state* floater = FindDeformation(results, floater_id);
        if (Check(floater != nullptr, "the gravity free volume deformable still publishes after stepping") &&
            !floater_before.empty() && !jelly_before.empty())
        {
            const std::vector<openusd_physx_vec3f> floater_after = CaptureWindow(results, *floater);
            const openusd_physx_deformation_state* jelly_state = FindDeformation(results, jelly_id);
            Check(AllFinite(floater_after), "every gravity free volume vertex stays finite");
            if (jelly_state != nullptr)
            {
                const std::vector<openusd_physx_vec3f> jelly_after = CaptureWindow(results, *jelly_state);
                Check(
                    WindowDisplacement(floater_before, floater_after) <
                        WindowDisplacement(jelly_before, jelly_after),
                    "a deformable that opts out of gravity moves less than the identical one that keeps it");
            }
        }
    }

    // A reset must restore both the CPU and the GPU state, so the same probe
    // that proved motion also proves the motion can be undone.
    openusd_physx_reset_desc reset{};
    reset.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_reset_desc));
    CheckStatus(openusd_physx_world_reset(world, &reset, &error), OPENUSD_PHYSX_STATUS_OK, "the world resets");
    CheckStatus(
        openusd_physx_world_fetch_results(world, &results, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "results can be fetched after a reset");
    Check(results.header.step_index == 0, "a reset rewinds the step index");

    if (device_available)
    {
        const openusd_physx_deformation_state* cloth = FindDeformation(results, cloth_id);
        if (Check(cloth != nullptr, "the surface deformable publishes after a reset"))
        {
            const std::vector<openusd_physx_vec3f> restored = CaptureWindow(results, *cloth);
            Check(
                WindowDisplacement(cloth_before, restored) < 1.0e-3,
                "a reset restores the surface deformable to its built configuration");
        }
    }

    // A result page that declares only one half of the deformation window is a
    // caller error, not a silently half filled result.
    openusd_physx_result_page malformed = storage.Page();
    malformed.deformation_points = nullptr;
    malformed.deformation_point_capacity = 0;
    CheckStatus(
        openusd_physx_world_fetch_results(world, &malformed, &error),
        OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
        "a half declared deformation window is refused");

    // A page that declares no deformation buffers at all is legal: the world
    // keeps stepping and simply publishes no deformation.
    openusd_physx_result_page without = storage.Page();
    without.deformations = nullptr;
    without.deformation_capacity = 0;
    without.deformation_points = nullptr;
    without.deformation_point_capacity = 0;
    CheckStatus(
        openusd_physx_world_fetch_results(world, &without, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "a result page without deformation buffers is accepted");
    Check(
        without.header.deformation_body_count == 0,
        "a result page without deformation buffers publishes no deformation body");
    if (device_available)
    {
        Check(
            (without.header.overflow_flags & OPENUSD_PHYSX_OVERFLOW_DEFORMATION) != 0,
            "a device backed world reports a deformation overflow when the caller declares no window");
    }

    openusd_physx_world_release(world);

    if (g_failures != 0)
    {
        std::cerr << g_failures << " CUDA domain check(s) failed.\n";
        return 1;
    }
    std::cout << "openusd_physx CUDA domain checks passed ("
              << (device_available ? "device backed" : "device absent") << ").\n";
    return 0;
}
