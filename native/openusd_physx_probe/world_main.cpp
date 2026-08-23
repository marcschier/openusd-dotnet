// Copyright (c) marcschier. Licensed under the MIT License.

// Retained world ABI probe. This binary requires the simulation SDK because it
// creates a real world, builds the reference page, steps it, queries it, and
// resets it. Every buffer it hands to the library is caller owned and has a
// fixed capacity; the library never allocates or retains any of them.

#include "openusd_physx_world.h"
#include "page_builder.h"

#include <cmath>
#include <cstring>
#include <iostream>
#include <vector>

namespace
{
int g_failures = 0;

bool Check(bool condition, const char* description)
{
    if (!condition)
    {
        ++g_failures;
        std::cerr << "check failed: " << description << '\n';
    }
    return condition;
}

bool CheckStatus(openusd_physx_status status, openusd_physx_status expected, const char* description)
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
}

int main()
{
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};

    openusd_physx_abi_info abi{};
    abi.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_abi_info));
    CheckStatus(openusd_physx_world_get_abi(&abi, &error), OPENUSD_PHYSX_STATUS_OK, "world_get_abi");
    Check(abi.abi_version == OPENUSD_PHYSX_WORLD_ABI_VERSION, "abi version matches the header");

    openusd_physx_capabilities capabilities{};
    capabilities.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_capabilities));
    CheckStatus(
        openusd_physx_world_get_capabilities(OPENUSD_PHYSX_WORLD_ABI_VERSION, &capabilities, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "world_get_capabilities");
    Check(
        (capabilities.flags & OPENUSD_PHYSX_CAPABILITY_CPU_RIGID_BODIES) != 0 &&
            (capabilities.flags & OPENUSD_PHYSX_CAPABILITY_SCENE_QUERIES) != 0,
        "capabilities report rigid bodies and scene queries");
    Check(capabilities.max_substeps == OPENUSD_PHYSX_MAX_SUBSTEPS, "capabilities report the substep bound");
    CheckStatus(
        openusd_physx_world_get_capabilities(OPENUSD_PHYSX_WORLD_ABI_VERSION + 1u, &capabilities, &error),
        OPENUSD_PHYSX_STATUS_VERSION_MISMATCH,
        "capabilities reject a different ABI version");

    openusd_physx_world_desc world_desc{};
    world_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_desc));
    world_desc.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION + 1u;
    openusd_physx_world* rejected = nullptr;
    CheckStatus(
        openusd_physx_world_create(&world_desc, &rejected, &error),
        OPENUSD_PHYSX_STATUS_VERSION_MISMATCH,
        "world_create rejects a different ABI version");
    Check(rejected == nullptr, "world_create clears its output on failure");

    world_desc.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION;
    world_desc.worker_thread_count = 2;
    world_desc.flags = OPENUSD_PHYSX_WORLD_FLAG_ENABLE_EVENTS | OPENUSD_PHYSX_WORLD_FLAG_ENABLE_DEBUG;
    openusd_physx_world* world = nullptr;
    if (!CheckStatus(openusd_physx_world_create(&world_desc, &world, &error), OPENUSD_PHYSX_STATUS_OK, "world_create") ||
        world == nullptr)
    {
        std::cerr << "the world could not be created: " << error_data << '\n';
        return 1;
    }

    openusd_physx_world_status_info status{};
    status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
    CheckStatus(openusd_physx_world_get_status(world, &status, &error), OPENUSD_PHYSX_STATUS_OK, "world_get_status");
    Check(status.state == OPENUSD_PHYSX_WORLD_STATE_EMPTY, "a new world is empty");

    openusd_physx_test::PageBuilder builder = openusd_physx_test::MakeReferenceScene();
    std::vector<uint64_t> page = builder.Build();
    const size_t page_size = builder.Size();

    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    unsigned char truncated[64]{};
    CheckStatus(
        openusd_physx_world_build(world, truncated, sizeof(truncated), &validation, &error),
        OPENUSD_PHYSX_STATUS_INVALID_PAGE,
        "world_build rejects a page that is not a valid build page");
    CheckStatus(openusd_physx_world_get_status(world, &status, &error), OPENUSD_PHYSX_STATUS_OK, "status after a failed build");
    Check(status.actor_count == 0, "a failed build leaves no content behind");

    validation = openusd_physx_page_validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    if (!CheckStatus(
            openusd_physx_world_build(world, page.data(), page_size, &validation, &error),
            OPENUSD_PHYSX_STATUS_OK,
            "world_build"))
    {
        std::cerr << "the reference page could not be built: " << error_data << '\n';
        openusd_physx_world_release(world);
        return 1;
    }
    Check(validation.actor_count == 4 && validation.dynamic_actor_count == 2, "validation reports the actor counts");

    CheckStatus(openusd_physx_world_get_status(world, &status, &error), OPENUSD_PHYSX_STATUS_OK, "status after build");
    Check(status.state == OPENUSD_PHYSX_WORLD_STATE_READY, "a built world is ready");
    Check(status.actor_count == 4 && status.dynamic_actor_count == 2 && status.joint_count == 1, "status reports the world content");
    Check(status.revision == builder.Header().revision, "status reports the page revision");

    ResultStorage storage(status.capacities);
    openusd_physx_result_page results = storage.Page();

    openusd_physx_result_page undersized = results;
    undersized.body_state_capacity = 0;
    undersized.body_states = nullptr;
    openusd_physx_step_desc step_desc{};
    step_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_step_desc));
    step_desc.fixed_time_step = 1.0 / 60.0;
    step_desc.substep_count = 1;
    CheckStatus(
        openusd_physx_world_step(world, &step_desc, &undersized, &error),
        OPENUSD_PHYSX_STATUS_CAPACITY_EXCEEDED,
        "world_step rejects a result page that cannot hold every body state");

    openusd_physx_result_page mismatched = results;
    mismatched.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION + 1u;
    CheckStatus(
        openusd_physx_world_step(world, &step_desc, &mismatched, &error),
        OPENUSD_PHYSX_STATUS_VERSION_MISMATCH,
        "world_step rejects a result page from a different ABI");

    step_desc.fixed_time_step = 10.0;
    CheckStatus(
        openusd_physx_world_step(world, &step_desc, &results, &error),
        OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
        "world_step rejects a time step outside the supported rate range");
    step_desc.fixed_time_step = 1.0 / 60.0;

    step_desc.substep_count = OPENUSD_PHYSX_MAX_SUBSTEPS + 1u;
    CheckStatus(
        openusd_physx_world_step(world, &step_desc, &results, &error),
        OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
        "world_step rejects more substeps than the page declared");
    step_desc.substep_count = 2;

    const uint64_t sphere_id =
        openusd_physx_test::PageBuilder::ComputeIdentity("/World/SphereBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0);
    const uint64_t ground_id =
        openusd_physx_test::PageBuilder::ComputeIdentity("/World/GroundBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0);
    const uint64_t scene_id =
        openusd_physx_test::PageBuilder::ComputeIdentity("/World/PhysicsScene", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0);

    openusd_physx_command broken{};
    broken.target_id = sphere_id;
    broken.type = OPENUSD_PHYSX_COMMAND_SET_LINEAR_VELOCITY;
    broken.vector = openusd_physx_vec3f{std::nanf(""), 0.0F, 0.0F};
    step_desc.commands = &broken;
    step_desc.command_count = 1;
    CheckStatus(
        openusd_physx_world_step(world, &step_desc, &results, &error),
        OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
        "world_step rejects a command batch that carries a non finite value");

    openusd_physx_command commands[3]{};
    commands[0].target_id = sphere_id;
    commands[0].type = OPENUSD_PHYSX_COMMAND_SET_LINEAR_VELOCITY;
    commands[0].vector = openusd_physx_vec3f{0.0F, 0.0F, 0.0F};
    commands[1].target_id = scene_id;
    commands[1].type = OPENUSD_PHYSX_COMMAND_SET_SCENE_GRAVITY;
    commands[1].vector = openusd_physx_vec3f{0.0F, -9.81F, 0.0F};
    commands[2].target_id = 0x1234567890ABCDEFULL;
    commands[2].type = OPENUSD_PHYSX_COMMAND_WAKE;
    step_desc.commands = commands;
    step_desc.command_count = 3;
    if (!CheckStatus(openusd_physx_world_step(world, &step_desc, &results, &error), OPENUSD_PHYSX_STATUS_OK, "world_step"))
    {
        std::cerr << "the first step failed: " << error_data << '\n';
    }
    Check(results.header.step_index == 2, "the result header counts every simulated substep");
    Check(results.header.body_state_count == 2, "the result page reports both movable actors");
    Check(results.header.diagnostic_count >= 1, "an unknown command target is reported as a diagnostic");
    Check(results.header.state == OPENUSD_PHYSX_WORLD_STATE_READY, "the result header reports the world state");
    if (!Check(results.header.overflow_flags == OPENUSD_PHYSX_OVERFLOW_NONE, "the reference page does not overflow"))
    {
        std::cerr << "  overflow_flags=" << results.header.overflow_flags
                  << " events=" << results.header.event_count << "/" << storage.events.size()
                  << " diagnostics=" << results.header.diagnostic_count << "/" << storage.diagnostics.size()
                  << " debug_lines=" << results.header.debug_line_count << "/" << storage.debug_lines.size()
                  << " dropped(events/diagnostics/lines)=" << results.header.dropped_event_count << "/"
                  << results.header.dropped_diagnostic_count << "/" << results.header.dropped_debug_line_count << '\n';
    }

    const openusd_physx_body_state* sphere = FindState(results, sphere_id);
    if (Check(sphere != nullptr, "the sphere is reported in the result page"))
    {
        Check(sphere->pose.position.y < 6.0F, "the sphere falls under gravity");
    }
    Check(FindState(results, ground_id) == nullptr, "static actors are not reported as body states");

    step_desc.commands = nullptr;
    step_desc.command_count = 0;
    for (int index = 0; index < 8; ++index)
    {
        CheckStatus(openusd_physx_world_step(world, &step_desc, &results, &error), OPENUSD_PHYSX_STATUS_OK, "repeated step");
    }
    Check(results.header.simulation_time > 0.0, "the world accumulates simulation time");
    Check(results.header.total_step_seconds >= results.header.last_step_seconds, "step timing is accumulated");
    Check(results.header.debug_line_count > 0, "debug visualization produces lines when the world enables it");

    // A caller may hand over a smaller section than the world produces. That is
    // a bounded truncation with an explicit dropped count, never an allocation.
    const uint32_t full_debug_line_count = results.header.debug_line_count;
    openusd_physx_debug_line few_lines[8]{};
    openusd_physx_result_page truncated_results = storage.Page();
    truncated_results.debug_lines = few_lines;
    truncated_results.debug_line_capacity = 8;
    CheckStatus(
        openusd_physx_world_step(world, &step_desc, &truncated_results, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "step into a page with a small debug line section");
    Check(truncated_results.header.debug_line_count == 8, "the small section is filled to its capacity");
    Check(
        truncated_results.header.dropped_debug_line_count + 8 >= full_debug_line_count,
        "every line past the capacity is counted as dropped");
    Check(
        (truncated_results.header.overflow_flags & OPENUSD_PHYSX_OVERFLOW_DEBUG_LINES) != 0,
        "truncation raises the debug line overflow flag");
    CheckStatus(
        openusd_physx_world_step(world, &step_desc, &results, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "step after a truncated fetch");
    Check(results.header.overflow_flags == OPENUSD_PHYSX_OVERFLOW_NONE, "overflow state is consumed with the results");

    openusd_physx_query_request requests[3]{};
    requests[0].user_id = 1;
    requests[0].type = OPENUSD_PHYSX_QUERY_RAYCAST;
    // Cast well away from the jointed box and the falling sphere so the ground
    // plane is the closest blocking hit.
    requests[0].origin = openusd_physx_vec3f{10.0F, 10.0F, 0.0F};
    requests[0].direction = openusd_physx_vec3f{0.0F, -1.0F, 0.0F};
    requests[0].max_distance = 100.0F;
    requests[0].max_hits = 8;
    requests[1].user_id = 2;
    requests[1].type = OPENUSD_PHYSX_QUERY_OVERLAP;
    requests[1].shape_type = OPENUSD_PHYSX_SHAPE_SPHERE;
    requests[1].origin = openusd_physx_vec3f{0.0F, 0.0F, 0.0F};
    requests[1].rotation = openusd_physx_test::Identity();
    requests[1].radius = 3.0F;
    requests[1].max_hits = 8;
    requests[2].user_id = 3;
    requests[2].type = OPENUSD_PHYSX_QUERY_RAYCAST;
    requests[2].origin = openusd_physx_vec3f{10.0F, 10.0F, 0.0F};
    requests[2].direction = openusd_physx_vec3f{0.0F, 0.0F, 0.0F};
    requests[2].max_distance = 10.0F;
    requests[2].max_hits = 1;

    std::vector<openusd_physx_query_hit> hits(status.capacities.max_query_hits);
    openusd_physx_query_desc query_desc{};
    query_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_query_desc));
    query_desc.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION;
    query_desc.requests = requests;
    query_desc.request_count = 3;
    query_desc.hits = hits.data();
    query_desc.hit_capacity = hits.size();
    openusd_physx_query_result query_result{};
    query_result.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_query_result));
    CheckStatus(openusd_physx_world_query(world, &query_desc, &query_result, &error), OPENUSD_PHYSX_STATUS_OK, "world_query");
    Check(query_result.hit_count > 0, "the downward ray reports at least one hit");
    Check(query_result.rejected_request_count == 1, "a zero length ray is rejected");
    Check(query_result.dropped_hit_count == 0, "the hit capacity is large enough");
    bool ray_hit_ground = false;
    for (size_t index = 0; index < query_result.hit_count; ++index)
    {
        if (hits[index].user_id == 1 && hits[index].actor_id == ground_id)
        {
            ray_hit_ground = true;
        }
        Check(hits[index].actor_id != OPENUSD_PHYSX_INVALID_ID, "every hit carries a stable actor identity");
    }
    Check(ray_hit_ground, "the downward ray reaches the ground actor");

    query_desc.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION + 1u;
    CheckStatus(
        openusd_physx_world_query(world, &query_desc, &query_result, &error),
        OPENUSD_PHYSX_STATUS_VERSION_MISMATCH,
        "world_query rejects a different ABI version");
    query_desc.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION;

    openusd_physx_body_state override_state{};
    override_state.id = sphere_id;
    override_state.pose = openusd_physx_test::Pose(2.0F, 9.0F, 0.0F);
    openusd_physx_reset_desc reset_desc{};
    reset_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_reset_desc));
    reset_desc.body_states = &override_state;
    reset_desc.body_state_count = 1;
    CheckStatus(openusd_physx_world_reset(world, &reset_desc, &error), OPENUSD_PHYSX_STATUS_OK, "world_reset");
    CheckStatus(openusd_physx_world_fetch_results(world, &results, &error), OPENUSD_PHYSX_STATUS_OK, "world_fetch_results");
    Check(results.header.step_index == 0, "reset restarts the step counter");
    Check(results.header.simulation_time == 0.0, "reset restarts the simulation clock");
    sphere = FindState(results, sphere_id);
    if (Check(sphere != nullptr, "the sphere survives the reset"))
    {
        Check(std::fabs(sphere->pose.position.y - 9.0F) < 1e-3F, "reset applies the caller supplied body state");
    }

    override_state.id = 0x0FEDCBA098765432ULL;
    CheckStatus(
        openusd_physx_world_reset(world, &reset_desc, &error),
        OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
        "world_reset rejects an unknown body identity");

    CheckStatus(openusd_physx_world_build(world, page.data(), page_size, nullptr, &error), OPENUSD_PHYSX_STATUS_OK, "rebuild");
    CheckStatus(openusd_physx_world_get_status(world, &status, &error), OPENUSD_PHYSX_STATUS_OK, "status after rebuild");
    Check(status.state == OPENUSD_PHYSX_WORLD_STATE_READY && status.actor_count == 4, "a rebuild replaces the world content");

    // Second page: convex cooking, a capsule, a kinematic actor, and the world
    // default material. These are the paths the reference page does not cover.
    {
        openusd_physx_test::PageBuilder convex_builder;
        openusd_physx_scene_desc convex_scene{};
        convex_scene.id =
            convex_builder.AddIdentity("/Convex/PhysicsScene", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
        convex_scene.gravity_direction = openusd_physx_vec3f{0.0F, -1.0F, 0.0F};
        convex_scene.gravity_magnitude = 9.81F;
        convex_scene.position_iterations = 4;
        convex_scene.velocity_iterations = 1;
        convex_scene.bounce_threshold = 0.2F;
        convex_scene.contact_offset = 0.02F;
        convex_builder.Scenes().push_back(convex_scene);

        for (int corner = 0; corner < 8; ++corner)
        {
            convex_builder.MeshPoints().push_back(openusd_physx_vec3f{
                (corner & 1) != 0 ? 0.5F : -0.5F,
                (corner & 2) != 0 ? 0.5F : -0.5F,
                (corner & 4) != 0 ? 0.5F : -0.5F});
        }

        openusd_physx_shape_desc hull{};
        hull.id = convex_builder.AddIdentity("/Convex/Hull", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
        hull.type = OPENUSD_PHYSX_SHAPE_CONVEX_MESH;
        hull.local_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
        hull.scale = openusd_physx_vec3f{1.0F, 1.0F, 1.0F};
        hull.point_count = 8;
        hull.material_index = -1;
        convex_builder.Shapes().push_back(hull);

        openusd_physx_shape_desc capsule{};
        capsule.id = convex_builder.AddIdentity("/Convex/Capsule", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
        capsule.type = OPENUSD_PHYSX_SHAPE_CAPSULE;
        capsule.local_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
        capsule.scale = openusd_physx_vec3f{1.0F, 1.0F, 1.0F};
        capsule.radius = 0.3F;
        capsule.half_height = 0.5F;
        capsule.material_index = -1;
        convex_builder.Shapes().push_back(capsule);

        convex_builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{0, -1});
        convex_builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{1, -1});

        openusd_physx_actor_desc hull_body{};
        hull_body.id = convex_builder.AddIdentity("/Convex/HullBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
        hull_body.scene_index = 0;
        hull_body.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
        hull_body.world_pose = openusd_physx_test::Pose(0.0F, 5.0F, 0.0F);
        hull_body.mass = 1.0F;
        hull_body.shape_offset = 0;
        hull_body.shape_count = 1;
        convex_builder.Actors().push_back(hull_body);

        openusd_physx_actor_desc mover{};
        mover.id = convex_builder.AddIdentity("/Convex/Mover", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
        mover.scene_index = 0;
        mover.type = OPENUSD_PHYSX_ACTOR_KINEMATIC;
        mover.world_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
        mover.mass = 1.0F;
        mover.shape_offset = 1;
        mover.shape_count = 1;
        convex_builder.Actors().push_back(mover);

        convex_builder.Header().capacities.max_body_states = 4;
        convex_builder.Header().capacities.max_events = 16;
        convex_builder.Header().capacities.max_diagnostics = 16;
        convex_builder.Header().capacities.max_debug_lines = 512;
        convex_builder.Header().capacities.max_query_hits = 16;

        std::vector<uint64_t> convex_page = convex_builder.Build();
        openusd_physx_page_validation convex_validation{};
        convex_validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
        if (CheckStatus(
                openusd_physx_world_build(
                    world,
                    convex_page.data(),
                    convex_builder.Size(),
                    &convex_validation,
                    &error),
                OPENUSD_PHYSX_STATUS_OK,
                "build a convex, capsule, and kinematic page"))
        {
            Check(
                convex_validation.actor_count == 2 && convex_validation.dynamic_actor_count == 2,
                "the convex page reports two movable actors");

            CheckStatus(
                openusd_physx_world_get_status(world, &status, &error),
                OPENUSD_PHYSX_STATUS_OK,
                "status after the convex build");
            ResultStorage convex_storage(status.capacities);
            openusd_physx_result_page convex_results = convex_storage.Page();

            const uint64_t mover_id = openusd_physx_test::PageBuilder::ComputeIdentity(
                "/Convex/Mover",
                OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM,
                0);
            const uint64_t hull_id = openusd_physx_test::PageBuilder::ComputeIdentity(
                "/Convex/HullBody",
                OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM,
                0);

            openusd_physx_command move{};
            move.target_id = mover_id;
            move.type = OPENUSD_PHYSX_COMMAND_KINEMATIC_TARGET;
            move.pose = openusd_physx_test::Pose(1.0F, 0.0F, 0.0F);
            openusd_physx_step_desc convex_step{};
            convex_step.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_step_desc));
            convex_step.fixed_time_step = 1.0 / 60.0;
            convex_step.substep_count = 1;
            convex_step.commands = &move;
            convex_step.command_count = 1;
            CheckStatus(
                openusd_physx_world_step(world, &convex_step, &convex_results, &error),
                OPENUSD_PHYSX_STATUS_OK,
                "step the convex page");
            const openusd_physx_body_state* mover_state = FindState(convex_results, mover_id);
            if (Check(mover_state != nullptr, "the kinematic actor is reported"))
            {
                Check(
                    (mover_state->flags & OPENUSD_PHYSX_BODY_STATE_FLAG_KINEMATIC) != 0,
                    "the kinematic actor is flagged as kinematic");
                Check(mover_state->pose.position.x > 0.5F, "the kinematic target moved the actor");
            }
            const openusd_physx_body_state* hull_state = FindState(convex_results, hull_id);
            if (Check(hull_state != nullptr, "the convex actor is reported"))
            {
                Check(hull_state->pose.position.y < 5.0F, "the cooked convex hull falls under gravity");
            }

            openusd_physx_query_request sweep{};
            sweep.user_id = 10;
            sweep.type = OPENUSD_PHYSX_QUERY_SWEEP;
            sweep.shape_type = OPENUSD_PHYSX_SHAPE_SPHERE;
            sweep.origin = openusd_physx_vec3f{1.0F, 4.0F, 0.0F};
            sweep.direction = openusd_physx_vec3f{0.0F, -1.0F, 0.0F};
            sweep.rotation = openusd_physx_test::Identity();
            sweep.radius = 0.25F;
            sweep.max_distance = 20.0F;
            sweep.max_hits = 4;
            std::vector<openusd_physx_query_hit> sweep_hits(status.capacities.max_query_hits);
            openusd_physx_query_desc sweep_desc{};
            sweep_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_query_desc));
            sweep_desc.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION;
            sweep_desc.requests = &sweep;
            sweep_desc.request_count = 1;
            sweep_desc.hits = sweep_hits.data();
            sweep_desc.hit_capacity = sweep_hits.size();
            openusd_physx_query_result sweep_result{};
            sweep_result.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_query_result));
            CheckStatus(
                openusd_physx_world_query(world, &sweep_desc, &sweep_result, &error),
                OPENUSD_PHYSX_STATUS_OK,
                "sweep the convex page");
            Check(sweep_result.rejected_request_count == 0, "the sweep request is accepted");
            Check(sweep_result.hit_count > 0, "the downward sweep reaches the kinematic capsule");
        }
    }

    // The mass frame of an actor is optional and may be authored as a legal quaternion that is
    // not unit length. The page contract accepts it, so the world must normalize it and keep its
    // orientation rather than fall back to the identity.
    //
    // Proving that takes a body whose motion is not constrained by anything else. The reference
    // page attaches actor 1 to the world frame with a revolute joint, so a body that carries an
    // authored mass frame is placed on actor 2, which is free. A rotation of ninety degrees about
    // Z turns a diagonal inertia of (1, 4, 9) about the principal axes into (4, 1, 9) about the
    // actor axes, so the same world torque about X produces a quarter of the angular acceleration
    // it would produce if the frame were dropped for the identity. The two outcomes are four times
    // apart, so the check reads the frame rather than merely the finiteness of the result.
    {
        const double frame_dt = 1.0 / 60.0;
        const float applied_torque = 1.0F;
        const float principal_inertia_about_torque = 4.0F;
        const float identity_inertia_about_torque = 1.0F;
        const float expected =
            static_cast<float>(applied_torque * frame_dt) / principal_inertia_about_torque;
        const float identity_expected =
            static_cast<float>(applied_torque * frame_dt) / identity_inertia_about_torque;

        openusd_physx_test::PageBuilder frame_builder = openusd_physx_test::MakeReferenceScene();
        openusd_physx_actor_desc& free_body = frame_builder.Actors()[2];
        free_body.principal_axes = openusd_physx_quatf{0.0F, 0.0F, 0.9F, 0.9F};
        free_body.mass = 2.0F;
        free_body.inertia = openusd_physx_vec3f{1.0F, 4.0F, 9.0F};
        free_body.linear_velocity = openusd_physx_vec3f{0.0F, 0.0F, 0.0F};
        free_body.angular_velocity = openusd_physx_vec3f{0.0F, 0.0F, 0.0F};
        free_body.flags |= OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_GRAVITY;
        const uint64_t free_body_id = free_body.id;
        std::vector<uint64_t> frame_page = frame_builder.Build();
        openusd_physx_page_validation frame_validation{};
        frame_validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
        if (CheckStatus(
                openusd_physx_world_build(
                    world, frame_page.data(), frame_builder.Size(), &frame_validation, &error),
                OPENUSD_PHYSX_STATUS_OK,
                "world_build accepts a legal mass frame rotation that is not unit length"))
        {
            openusd_physx_world_status_info frame_status{};
            frame_status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
            CheckStatus(
                openusd_physx_world_get_status(world, &frame_status, &error),
                OPENUSD_PHYSX_STATUS_OK,
                "status after the mass frame build");
            ResultStorage frame_storage(frame_status.capacities);
            openusd_physx_result_page frame_results = frame_storage.Page();
            openusd_physx_command torque{};
            torque.target_id = free_body_id;
            torque.type = OPENUSD_PHYSX_COMMAND_ADD_TORQUE;
            torque.vector = openusd_physx_vec3f{applied_torque, 0.0F, 0.0F};
            openusd_physx_step_desc frame_step{};
            frame_step.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_step_desc));
            frame_step.fixed_time_step = frame_dt;
            frame_step.substep_count = 1;
            frame_step.commands = &torque;
            frame_step.command_count = 1;
            CheckStatus(
                openusd_physx_world_step(world, &frame_step, &frame_results, &error),
                OPENUSD_PHYSX_STATUS_OK,
                "step the mass frame page");
            const openusd_physx_body_state* frame_state = FindState(frame_results, free_body_id);
            if (Check(frame_state != nullptr, "the actor with an authored mass frame is reported"))
            {
                Check(
                    std::isfinite(frame_state->pose.position.x) &&
                        std::isfinite(frame_state->pose.position.y) &&
                        std::isfinite(frame_state->pose.position.z) &&
                        std::isfinite(frame_state->angular_velocity.x),
                    "an authored mass frame keeps the simulation finite");
                const float observed = frame_state->angular_velocity.x;
                if (!Check(
                        std::abs(observed - expected) < 0.25F * expected,
                        "an authored mass frame states the inertia about its own principal axes"))
                {
                    std::cerr << "  observed=" << observed << " expected=" << expected
                              << " (identity frame would give " << identity_expected << ")\n";
                }
                Check(
                    std::abs(observed - identity_expected) > 0.5F * identity_expected,
                    "an authored mass frame is not silently replaced by the identity rotation");
            }
        }
    }

    openusd_physx_world_release(world);
    openusd_physx_world_release(nullptr);

    if (g_failures != 0)
    {
        std::cerr << g_failures << " retained world checks failed\n";
        return 1;
    }
    std::cout << "retained world probe passed\n";
    return 0;
}
