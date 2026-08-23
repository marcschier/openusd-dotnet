// Copyright (c) marcschier. Licensed under the MIT License.

// End to end probe for batched simulation events, runtime force and impulse
// commands, and batched scene queries. It requires the simulation SDK because
// every check drives a real world: a dynamic body falls onto a static ground and
// through a trigger volume, forces and impulses are applied through the command
// batch, and one query batch mixes raycasts, sweeps, and overlaps.
//
// Every buffer handed to the library is caller owned and fixed size, and every
// batch crosses the ABI exactly once.

#include "openusd_physx_world.h"
#include "page_builder.h"

#include <algorithm>
#include <cmath>
#include <iostream>
#include <limits>
#include <string>
#include <thread>
#include <vector>

namespace
{
using openusd_physx_test::Identity;
using openusd_physx_test::PageBuilder;
using openusd_physx_test::Pose;

int g_failures = 0;

// Mirrors openusd_physx_translate::kMaxPairFilterActors, which is an internal
// bound of the suppressed pair matrix rather than part of the ABI.
constexpr uint32_t kMaxPairFilterActors = 4096;

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

uint64_t IdOf(const std::string& path)
{
    return PageBuilder::ComputeIdentity(path, OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0);
}

struct ResultStorage
{
    std::vector<openusd_physx_body_state> body_states;
    std::vector<openusd_physx_event> events;
    std::vector<openusd_physx_diagnostic> diagnostics;

    explicit ResultStorage(const openusd_physx_result_capacities& capacities)
        : body_states(capacities.max_body_states)
        , events(capacities.max_events)
        , diagnostics(capacities.max_diagnostics)
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
        return page;
    }
};

// A ground box, a trigger volume above it, and a dynamic sphere that falls
// through the trigger onto the ground. That is the smallest scene that produces
// contact, trigger, and sleep events from one simulation. The trigger sits high
// enough that the resting sphere is unambiguously outside it, so the leave event
// is produced by the simulation rather than by a borderline resting pose.
PageBuilder MakeEventScene()
{
    PageBuilder builder;

    openusd_physx_scene_desc scene{};
    scene.id = builder.AddIdentity("/Events/PhysicsScene", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    scene.gravity_direction = openusd_physx_vec3f{0.0F, -1.0F, 0.0F};
    scene.gravity_magnitude = 9.81F;
    scene.position_iterations = 4;
    scene.velocity_iterations = 1;
    scene.bounce_threshold = 0.2F;
    scene.contact_offset = 0.02F;
    builder.Scenes().push_back(scene);

    openusd_physx_material_desc material{};
    material.id = builder.AddIdentity("/Events/Material", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    material.static_friction = 0.6F;
    material.dynamic_friction = 0.5F;
    material.restitution = 0.0F;
    material.density = 1000.0F;
    builder.Materials().push_back(material);

    const openusd_physx_vec3f unit_scale{1.0F, 1.0F, 1.0F};

    openusd_physx_shape_desc ground_shape{};
    ground_shape.id = builder.AddIdentity("/Events/GroundShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    ground_shape.type = OPENUSD_PHYSX_SHAPE_BOX;
    ground_shape.local_pose = Pose(0.0F, 0.0F, 0.0F);
    ground_shape.scale = unit_scale;
    ground_shape.half_extents = openusd_physx_vec3f{20.0F, 0.5F, 20.0F};
    builder.Shapes().push_back(ground_shape);

    openusd_physx_shape_desc trigger_shape{};
    trigger_shape.id = builder.AddIdentity("/Events/TriggerShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    trigger_shape.type = OPENUSD_PHYSX_SHAPE_BOX;
    trigger_shape.local_pose = Pose(0.0F, 0.0F, 0.0F);
    trigger_shape.scale = unit_scale;
    trigger_shape.half_extents = openusd_physx_vec3f{2.0F, 0.5F, 2.0F};
    trigger_shape.flags = OPENUSD_PHYSX_SHAPE_FLAG_TRIGGER;
    builder.Shapes().push_back(trigger_shape);

    openusd_physx_shape_desc sphere_shape{};
    sphere_shape.id = builder.AddIdentity("/Events/SphereShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    sphere_shape.type = OPENUSD_PHYSX_SHAPE_SPHERE;
    sphere_shape.local_pose = Pose(0.0F, 0.0F, 0.0F);
    sphere_shape.scale = unit_scale;
    sphere_shape.radius = 0.5F;
    builder.Shapes().push_back(sphere_shape);

    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{0, -1});
    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{1, -1});
    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{2, -1});

    openusd_physx_actor_desc ground{};
    ground.id = builder.AddIdentity("/Events/GroundBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    ground.scene_index = 0;
    ground.type = OPENUSD_PHYSX_ACTOR_STATIC;
    ground.world_pose = Pose(0.0F, -0.5F, 0.0F);
    ground.shape_offset = 0;
    ground.shape_count = 1;
    builder.Actors().push_back(ground);

    openusd_physx_actor_desc trigger{};
    trigger.id = builder.AddIdentity("/Events/TriggerBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    trigger.scene_index = 0;
    trigger.type = OPENUSD_PHYSX_ACTOR_STATIC;
    trigger.world_pose = Pose(0.0F, 2.5F, 0.0F);
    trigger.shape_offset = 1;
    trigger.shape_count = 1;
    builder.Actors().push_back(trigger);

    openusd_physx_actor_desc sphere{};
    sphere.id = builder.AddIdentity("/Events/SphereBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    sphere.scene_index = 0;
    sphere.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
    sphere.world_pose = Pose(0.0F, 4.0F, 0.0F);
    sphere.mass = 1.0F;
    sphere.shape_offset = 2;
    sphere.shape_count = 1;
    builder.Actors().push_back(sphere);

    builder.Header().capacities.max_body_states = 4;
    builder.Header().capacities.max_events = 64;
    builder.Header().capacities.max_diagnostics = 32;
    builder.Header().capacities.max_debug_lines = 0;
    builder.Header().capacities.max_query_hits = 64;
    return builder;
}

// A page with many actors, no suppressed pairs, and events enabled. The
// suppressed pair matrix is the only quadratic structure in the build, so a
// page that declares no pair at all must not inherit its actor bound.
PageBuilder MakeWideScene(uint32_t actor_count)
{
    PageBuilder builder;

    openusd_physx_scene_desc scene{};
    scene.id = builder.AddIdentity("/Wide/PhysicsScene", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    scene.gravity_direction = openusd_physx_vec3f{0.0F, -1.0F, 0.0F};
    scene.gravity_magnitude = 9.81F;
    scene.position_iterations = 4;
    scene.velocity_iterations = 1;
    scene.bounce_threshold = 0.2F;
    scene.contact_offset = 0.02F;
    builder.Scenes().push_back(scene);

    openusd_physx_material_desc material{};
    material.id = builder.AddIdentity("/Wide/Material", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    material.static_friction = 0.5F;
    material.dynamic_friction = 0.5F;
    material.restitution = 0.0F;
    material.density = 1000.0F;
    builder.Materials().push_back(material);

    openusd_physx_shape_desc shape{};
    shape.id = builder.AddIdentity("/Wide/Shape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    shape.type = OPENUSD_PHYSX_SHAPE_BOX;
    shape.local_pose = Pose(0.0F, 0.0F, 0.0F);
    shape.scale = openusd_physx_vec3f{1.0F, 1.0F, 1.0F};
    shape.half_extents = openusd_physx_vec3f{0.1F, 0.1F, 0.1F};
    builder.Shapes().push_back(shape);
    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{0, -1});

    for (uint32_t index = 0; index < actor_count; ++index)
    {
        openusd_physx_actor_desc actor{};
        actor.id = builder.AddIdentity("/Wide/Body", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, index).id;
        actor.scene_index = 0;
        actor.type = OPENUSD_PHYSX_ACTOR_STATIC;
        actor.world_pose = Pose(static_cast<float>(index) * 0.5F, 0.0F, 0.0F);
        actor.shape_offset = 0;
        actor.shape_count = 1;
        builder.Actors().push_back(actor);
    }

    builder.Header().capacities.max_body_states = 1;
    builder.Header().capacities.max_events = 8;
    builder.Header().capacities.max_diagnostics = 8;
    builder.Header().capacities.max_debug_lines = 0;
    builder.Header().capacities.max_query_hits = 8;
    return builder;
}

// A thin static plate and one dynamic sphere aimed straight at it fast enough to
// pass right through inside a single step unless the pair asks for swept contact
// generation.
PageBuilder MakeTunnellingScene(uint32_t scene_flags = 0, uint32_t actor_flags = 0)
{
    PageBuilder builder;

    openusd_physx_scene_desc scene{};
    scene.id = builder.AddIdentity("/Ccd/PhysicsScene", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    scene.gravity_direction = openusd_physx_vec3f{0.0F, -1.0F, 0.0F};
    scene.gravity_magnitude = 0.0F;
    scene.position_iterations = 8;
    scene.velocity_iterations = 2;
    scene.bounce_threshold = 100.0F;
    scene.contact_offset = 0.002F;
    scene.flags = scene_flags;
    builder.Scenes().push_back(scene);

    openusd_physx_material_desc material{};
    material.id = builder.AddIdentity("/Ccd/Material", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    material.static_friction = 0.5F;
    material.dynamic_friction = 0.5F;
    material.restitution = 0.0F;
    material.density = 1000.0F;
    builder.Materials().push_back(material);

    const openusd_physx_vec3f unit_scale{1.0F, 1.0F, 1.0F};

    openusd_physx_shape_desc plate_shape{};
    plate_shape.id = builder.AddIdentity("/Ccd/PlateShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    plate_shape.type = OPENUSD_PHYSX_SHAPE_BOX;
    plate_shape.local_pose = Pose(0.0F, 0.0F, 0.0F);
    plate_shape.scale = unit_scale;
    plate_shape.half_extents = openusd_physx_vec3f{5.0F, 0.01F, 5.0F};
    builder.Shapes().push_back(plate_shape);

    openusd_physx_shape_desc bullet_shape{};
    bullet_shape.id = builder.AddIdentity("/Ccd/BulletShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    bullet_shape.type = OPENUSD_PHYSX_SHAPE_SPHERE;
    bullet_shape.local_pose = Pose(0.0F, 0.0F, 0.0F);
    bullet_shape.scale = unit_scale;
    bullet_shape.radius = 0.05F;
    builder.Shapes().push_back(bullet_shape);

    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{0, -1});
    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{1, -1});

    openusd_physx_actor_desc plate{};
    plate.id = builder.AddIdentity("/Ccd/PlateBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    plate.scene_index = 0;
    plate.type = OPENUSD_PHYSX_ACTOR_STATIC;
    plate.world_pose = Pose(0.0F, 0.0F, 0.0F);
    plate.shape_offset = 0;
    plate.shape_count = 1;
    builder.Actors().push_back(plate);

    openusd_physx_actor_desc bullet{};
    bullet.id = builder.AddIdentity("/Ccd/BulletBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    bullet.scene_index = 0;
    bullet.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
    bullet.world_pose = Pose(0.0F, 6.0F, 0.0F);
    bullet.linear_velocity = openusd_physx_vec3f{0.0F, -600.0F, 0.0F};
    bullet.mass = 1.0F;
    bullet.flags = actor_flags;
    bullet.shape_offset = 1;
    bullet.shape_count = 1;
    builder.Actors().push_back(bullet);

    builder.Header().capacities.max_body_states = 2;
    builder.Header().capacities.max_events = 32;
    builder.Header().capacities.max_diagnostics = 8;
    builder.Header().capacities.max_debug_lines = 0;
    builder.Header().capacities.max_query_hits = 8;
    return builder;
}

openusd_physx_world* CreateWorld(
    std::vector<uint64_t>& page,
    size_t page_size,
    char* error_data,
    size_t error_size,
    uint32_t world_flags = OPENUSD_PHYSX_WORLD_FLAG_ENABLE_EVENTS)
{
    openusd_physx_error_buffer error{error_data, error_size, 0};

    openusd_physx_world_desc world_desc{};
    world_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_desc));
    world_desc.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION;
    world_desc.worker_thread_count = 2;
    world_desc.flags = world_flags;

    openusd_physx_world* world = nullptr;
    if (openusd_physx_world_create(&world_desc, &world, &error) != OPENUSD_PHYSX_STATUS_OK || world == nullptr)
    {
        std::cerr << "the event world could not be created: " << error_data << '\n';
        return nullptr;
    }

    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    if (openusd_physx_world_build(world, page.data(), page_size, &validation, &error) !=
        OPENUSD_PHYSX_STATUS_OK)
    {
        std::cerr << "the event page could not be built: " << error_data << '\n';
        openusd_physx_world_release(world);
        return nullptr;
    }
    return world;
}

bool EventsAreOrdered(const openusd_physx_result_page& page)
{
    for (uint32_t index = 1; index < page.header.event_count; ++index)
    {
        const openusd_physx_event& previous = page.events[index - 1];
        const openusd_physx_event& current = page.events[index];
        if (previous.step_index != current.step_index)
        {
            if (previous.step_index > current.step_index)
            {
                return false;
            }
            continue;
        }
        if (previous.type != current.type)
        {
            if (previous.type > current.type)
            {
                return false;
            }
            continue;
        }
        if (previous.id0 != current.id0)
        {
            if (previous.id0 > current.id0)
            {
                return false;
            }
            continue;
        }
        if (previous.id1 > current.id1)
        {
            return false;
        }
    }
    return true;
}

// Regression probe for the pair filter block bound. Only the suppressed pair
// matrix is quadratic in the actor count, so a page that declares no suppressed
// pair must build no matter how many actors it carries, even though enabling
// events is what forces the block to exist at all.
void ProbeWideWorldBuildsWithEvents()
{
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};

    PageBuilder builder = MakeWideScene(kMaxPairFilterActors + 1);
    std::vector<uint64_t> page = builder.Build();

    openusd_physx_world_desc world_desc{};
    world_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_desc));
    world_desc.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION;
    world_desc.worker_thread_count = 1;
    world_desc.flags = OPENUSD_PHYSX_WORLD_FLAG_ENABLE_EVENTS;

    openusd_physx_world* world = nullptr;
    if (!CheckStatus(
            openusd_physx_world_create(&world_desc, &world, &error),
            OPENUSD_PHYSX_STATUS_OK,
            "a world for the wide page is created"))
    {
        return;
    }

    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    CheckStatus(
        openusd_physx_world_build(world, page.data(), builder.Size(), &validation, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "events on a page with more actors than the suppressed pair bound still build");
    openusd_physx_world_release(world);
}

// Regression probe for continuous collision detection. Raising
// PxSceneFlag::eENABLE_CCD only lets a scene sweep bodies; the body still needs
// the rigid body flag and the pair still needs the swept contact flag, so every
// level that can ask for continuous detection has to reach the filter data.
void ProbeContinuousCollisionStopsATunnellingBody()
{
    char error_data[512]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};

    const uint64_t bullet_id = IdOf("/Ccd/BulletBody");

    const auto lowest_position = [&](uint32_t scene_flags, uint32_t actor_flags, uint32_t world_flags)
    {
        PageBuilder builder = MakeTunnellingScene(scene_flags, actor_flags);
        std::vector<uint64_t> page = builder.Build();
        openusd_physx_world* world =
            CreateWorld(page, builder.Size(), error_data, sizeof(error_data), world_flags);
        if (world == nullptr)
        {
            return std::numeric_limits<float>::quiet_NaN();
        }
        openusd_physx_world_status_info status{};
        status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
        openusd_physx_world_get_status(world, &status, &error);

        ResultStorage storage(status.capacities);
        openusd_physx_result_page results = storage.Page();
        openusd_physx_step_desc step{};
        step.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_step_desc));
        step.fixed_time_step = 1.0 / 60.0;
        step.substep_count = 1;

        float lowest = std::numeric_limits<float>::max();
        for (int index = 0; index < 4; ++index)
        {
            if (openusd_physx_world_step(world, &step, &results, &error) != OPENUSD_PHYSX_STATUS_OK)
            {
                break;
            }
            for (uint32_t body = 0; body < results.header.body_state_count; ++body)
            {
                if (results.body_states[body].id == bullet_id)
                {
                    lowest = std::min(lowest, results.body_states[body].pose.position.y);
                }
            }
        }
        openusd_physx_world_release(world);
        return lowest;
    };

    const uint32_t events_only = OPENUSD_PHYSX_WORLD_FLAG_ENABLE_EVENTS;
    const float without_ccd = lowest_position(0, 0, events_only);
    const float world_ccd = lowest_position(0, 0, events_only | OPENUSD_PHYSX_WORLD_FLAG_ENABLE_CCD);
    const float scene_ccd = lowest_position(OPENUSD_PHYSX_SCENE_FLAG_ENABLE_CCD, 0, events_only);
    const float actor_ccd = lowest_position(0, OPENUSD_PHYSX_ACTOR_FLAG_ENABLE_CCD, events_only);

    Check(without_ccd < -1.0F, "a fast body tunnels through a thin plate when continuous detection is off");
    Check(world_ccd > -0.5F, "a world that enables continuous detection keeps a fast body above a thin plate");
    Check(scene_ccd > -0.5F, "a scene that enables continuous detection keeps a fast body above a thin plate");
    Check(actor_ccd > -0.5F, "an actor that enables continuous detection keeps a fast body above a thin plate");
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
    Check(
        (capabilities.flags & OPENUSD_PHYSX_CAPABILITY_CONTACT_EVENTS) != 0 &&
            (capabilities.flags & OPENUSD_PHYSX_CAPABILITY_TRIGGER_EVENTS) != 0 &&
            (capabilities.flags & OPENUSD_PHYSX_CAPABILITY_BATCHED_QUERIES) != 0,
        "capabilities report contact events, trigger events, and batched queries");

    PageBuilder builder = MakeEventScene();
    std::vector<uint64_t> page = builder.Build();
    openusd_physx_world* world = CreateWorld(page, builder.Size(), error_data, sizeof(error_data));
    if (world == nullptr)
    {
        return 1;
    }

    openusd_physx_world_status_info status{};
    status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
    CheckStatus(openusd_physx_world_get_status(world, &status, &error), OPENUSD_PHYSX_STATUS_OK, "world_get_status");

    ResultStorage storage(status.capacities);
    openusd_physx_result_page results = storage.Page();

    const uint64_t ground_id = IdOf("/Events/GroundBody");
    const uint64_t trigger_id = IdOf("/Events/TriggerBody");
    const uint64_t sphere_id = IdOf("/Events/SphereBody");
    const uint64_t ground_shape_id = IdOf("/Events/GroundShape");
    const uint64_t trigger_shape_id = IdOf("/Events/TriggerShape");
    const uint64_t sphere_shape_id = IdOf("/Events/SphereShape");

    openusd_physx_step_desc step_desc{};
    step_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_step_desc));
    step_desc.fixed_time_step = 1.0 / 60.0;
    step_desc.substep_count = 1;

    bool saw_trigger_enter = false;
    bool saw_trigger_leave = false;
    bool saw_contact_found = false;
    bool saw_sleep = false;
    bool ordered = true;
    bool identities_are_stable = true;
    bool steps_are_current = true;
    bool contact_carries_geometry = true;

    for (int index = 0; index < 240; ++index)
    {
        const uint64_t before = results.header.step_index;
        if (!CheckStatus(
                openusd_physx_world_step(world, &step_desc, &results, &error),
                OPENUSD_PHYSX_STATUS_OK,
                "world_step"))
        {
            break;
        }
        if (!EventsAreOrdered(results))
        {
            ordered = false;
        }
        for (uint32_t event_index = 0; event_index < results.header.event_count; ++event_index)
        {
            const openusd_physx_event& event = results.events[event_index];
            if (event.step_index <= before || event.step_index > results.header.step_index)
            {
                steps_are_current = false;
            }
            switch (event.type)
            {
            case OPENUSD_PHYSX_EVENT_TRIGGER_ENTER:
            case OPENUSD_PHYSX_EVENT_TRIGGER_LEAVE:
                if (event.id0 != trigger_id || event.id1 != sphere_id ||
                    event.detail0 != trigger_shape_id || event.detail1 != sphere_shape_id ||
                    (event.flags & OPENUSD_PHYSX_EVENT_FLAG_DETAIL_IS_SHAPE) == 0)
                {
                    identities_are_stable = false;
                }
                saw_trigger_enter |= event.type == OPENUSD_PHYSX_EVENT_TRIGGER_ENTER;
                saw_trigger_leave |= event.type == OPENUSD_PHYSX_EVENT_TRIGGER_LEAVE;
                break;
            case OPENUSD_PHYSX_EVENT_CONTACT_FOUND:
            case OPENUSD_PHYSX_EVENT_CONTACT_LOST:
            {
                // The pair is canonicalized by identity, so the smaller actor
                // identity is always reported first no matter which side PhysX
                // put first.
                const bool pair_is_canonical = event.id0 <= event.id1;
                const bool pair_is_expected =
                    (event.id0 == std::min(ground_id, sphere_id) && event.id1 == std::max(ground_id, sphere_id)) ||
                    (event.id0 == std::min(trigger_id, sphere_id) && event.id1 == std::max(trigger_id, sphere_id));
                if (!pair_is_canonical || !pair_is_expected ||
                    (event.flags & OPENUSD_PHYSX_EVENT_FLAG_DETAIL_IS_SHAPE) == 0)
                {
                    identities_are_stable = false;
                }
                if (event.detail0 != ground_shape_id && event.detail0 != sphere_shape_id &&
                    event.detail1 != ground_shape_id && event.detail1 != sphere_shape_id)
                {
                    identities_are_stable = false;
                }
                if (event.type == OPENUSD_PHYSX_EVENT_CONTACT_FOUND)
                {
                    saw_contact_found = true;
                    if ((event.flags & OPENUSD_PHYSX_EVENT_FLAG_HAS_POSITION) == 0 ||
                        (event.flags & OPENUSD_PHYSX_EVENT_FLAG_HAS_NORMAL) == 0)
                    {
                        contact_carries_geometry = false;
                    }
                }
                break;
            }
            case OPENUSD_PHYSX_EVENT_SLEEP:
                saw_sleep = true;
                if (event.id0 != sphere_id || event.id1 != OPENUSD_PHYSX_INVALID_ID)
                {
                    identities_are_stable = false;
                }
                break;
            default:
                break;
            }
        }
        if (saw_sleep && saw_contact_found && saw_trigger_leave)
        {
            break;
        }
    }

    Check(saw_trigger_enter, "the falling body enters the trigger volume");
    Check(saw_trigger_leave, "the falling body leaves the trigger volume");
    Check(saw_contact_found, "the falling body reports a contact with the ground");
    Check(saw_sleep, "the resting body reports a sleep transition");
    Check(ordered, "every event batch is reported in the deterministic order");
    Check(identities_are_stable, "every event carries the identities the build page declared");
    Check(steps_are_current, "every event carries the step index that produced it");
    Check(contact_carries_geometry, "a found contact carries a position and a normal");

    // Several substeps in one call must stamp every event with the substep that
    // produced it, and the last substep must be the index the result header
    // reports. A callback event that arrived before the index was raised would
    // land outside that window.
    openusd_physx_step_desc multi_step = step_desc;
    multi_step.substep_count = 4;
    const uint64_t before_multi = results.header.step_index;
    CheckStatus(
        openusd_physx_world_step(world, &multi_step, &results, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "a multi substep step succeeds");
    Check(
        results.header.step_index == before_multi + 4,
        "a step of four substeps advances the step index by four");
    bool substeps_are_stamped = true;
    for (uint32_t event_index = 0; event_index < results.header.event_count; ++event_index)
    {
        const uint64_t stamped = results.events[event_index].step_index;
        if (stamped <= before_multi || stamped > results.header.step_index)
        {
            substeps_are_stamped = false;
        }
    }
    Check(substeps_are_stamped, "every event of a multi substep step names one of that call's substeps");

    // A page smaller than the event batch keeps the deterministic prefix and
    // counts the remainder, and never allocates.
    openusd_physx_event single_event{};
    openusd_physx_result_page truncated = storage.Page();
    truncated.events = &single_event;
    truncated.event_capacity = 1;

    openusd_physx_command wake_and_push[2]{};
    wake_and_push[0].target_id = sphere_id;
    wake_and_push[0].type = OPENUSD_PHYSX_COMMAND_ADD_IMPULSE;
    wake_and_push[0].vector = openusd_physx_vec3f{0.0F, 6.0F, 0.0F};
    wake_and_push[1].target_id = sphere_id;
    wake_and_push[1].type = OPENUSD_PHYSX_COMMAND_ADD_IMPULSE;
    wake_and_push[1].flags = OPENUSD_PHYSX_COMMAND_FLAG_MAGNITUDE;
    wake_and_push[1].vector = openusd_physx_vec3f{1.0F, 0.0F, 0.0F};
    wake_and_push[1].scalar = 2.0F;
    step_desc.commands = wake_and_push;
    step_desc.command_count = 2;
    CheckStatus(
        openusd_physx_world_step(world, &step_desc, &truncated, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "step with an impulse batch into a one event page");
    step_desc.commands = nullptr;
    step_desc.command_count = 0;
    Check(truncated.header.event_count <= 1, "a one event page never reports more than one event");
    Check(
        truncated.header.event_count + truncated.header.dropped_event_count >= 1,
        "the dropped count accounts for every event past the capacity");

    // The magnitude modifier and the plain vector both push the body, so the
    // body is moving along both axes after the batch.
    CheckStatus(openusd_physx_world_step(world, &step_desc, &results, &error), OPENUSD_PHYSX_STATUS_OK, "settle step");
    bool sphere_is_moving = false;
    for (uint32_t index = 0; index < results.header.body_state_count; ++index)
    {
        if (results.body_states[index].id == sphere_id)
        {
            const openusd_physx_vec3f velocity = results.body_states[index].linear_velocity;
            sphere_is_moving = velocity.x > 0.5F && velocity.y > 0.0F;
        }
    }
    Check(sphere_is_moving, "a vector impulse and a magnitude impulse both act on the body");

    // A clear placed after an add in the same batch wins, so the body keeps only
    // the velocity it already had.
    openusd_physx_command add_then_clear[2]{};
    add_then_clear[0].target_id = sphere_id;
    add_then_clear[0].type = OPENUSD_PHYSX_COMMAND_ADD_FORCE;
    add_then_clear[0].vector = openusd_physx_vec3f{0.0F, 0.0F, 5000.0F};
    add_then_clear[1].target_id = sphere_id;
    add_then_clear[1].type = OPENUSD_PHYSX_COMMAND_CLEAR_FORCE;
    step_desc.commands = add_then_clear;
    step_desc.command_count = 2;
    CheckStatus(
        openusd_physx_world_step(world, &step_desc, &results, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "step with an add then clear batch");
    step_desc.commands = nullptr;
    step_desc.command_count = 0;
    bool clear_won = false;
    for (uint32_t index = 0; index < results.header.body_state_count; ++index)
    {
        if (results.body_states[index].id == sphere_id)
        {
            clear_won = std::fabs(results.body_states[index].linear_velocity.z) < 1.0F;
        }
    }
    Check(clear_won, "a clear that follows an add in the same batch wins");

    // Invalid commands reject the whole batch before anything is applied.
    openusd_physx_command invalid{};
    invalid.target_id = sphere_id;
    invalid.type = OPENUSD_PHYSX_COMMAND_ADD_FORCE;
    invalid.vector = openusd_physx_vec3f{1.0F, 0.0F, 0.0F};
    invalid.point = openusd_physx_vec3f{1.0F, 0.0F, 0.0F};
    step_desc.commands = &invalid;
    step_desc.command_count = 1;
    CheckStatus(
        openusd_physx_world_step(world, &step_desc, &results, &error),
        OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
        "a command that fills a field its type does not read is rejected");

    invalid.point = openusd_physx_vec3f{0.0F, 0.0F, 0.0F};
    invalid.flags = OPENUSD_PHYSX_COMMAND_FLAG_POINT_LOCAL;
    CheckStatus(
        openusd_physx_world_step(world, &step_desc, &results, &error),
        OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
        "a command that declares a modifier its type does not accept is rejected");

    invalid.type = OPENUSD_PHYSX_COMMAND_ADD_FORCE_AT_POINT;
    invalid.flags = OPENUSD_PHYSX_COMMAND_FLAG_POINT_CENTER_OF_MASS;
    CheckStatus(
        openusd_physx_world_step(world, &step_desc, &results, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "a force at the centre of mass needs no application point");
    step_desc.commands = nullptr;
    step_desc.command_count = 0;

    // One batch, one crossing of the ABI, three query kinds.
    openusd_physx_query_request requests[4]{};
    requests[0].user_id = 11;
    requests[0].type = OPENUSD_PHYSX_QUERY_RAYCAST;
    requests[0].origin = openusd_physx_vec3f{8.0F, 8.0F, 0.0F};
    requests[0].direction = openusd_physx_vec3f{0.0F, -1.0F, 0.0F};
    requests[0].max_distance = 100.0F;
    requests[0].max_hits = 8;
    requests[1].user_id = 12;
    requests[1].type = OPENUSD_PHYSX_QUERY_SWEEP;
    requests[1].shape_type = OPENUSD_PHYSX_SHAPE_SPHERE;
    requests[1].origin = openusd_physx_vec3f{8.0F, 8.0F, 0.0F};
    requests[1].direction = openusd_physx_vec3f{0.0F, -1.0F, 0.0F};
    requests[1].rotation = Identity();
    requests[1].radius = 0.25F;
    requests[1].max_distance = 100.0F;
    requests[1].max_hits = 8;
    requests[2].user_id = 13;
    requests[2].type = OPENUSD_PHYSX_QUERY_OVERLAP;
    requests[2].shape_type = OPENUSD_PHYSX_SHAPE_BOX;
    requests[2].origin = openusd_physx_vec3f{0.0F, 1.5F, 0.0F};
    requests[2].rotation = Identity();
    requests[2].half_extents = openusd_physx_vec3f{1.0F, 1.0F, 1.0F};
    requests[2].max_hits = 8;
    // The same overlap without triggers must report strictly fewer actors.
    requests[3].user_id = 14;
    requests[3].type = OPENUSD_PHYSX_QUERY_OVERLAP;
    requests[3].flags = OPENUSD_PHYSX_QUERY_FLAG_EXCLUDE_TRIGGERS;
    requests[3].shape_type = OPENUSD_PHYSX_SHAPE_BOX;
    requests[3].origin = openusd_physx_vec3f{0.0F, 1.5F, 0.0F};
    requests[3].rotation = Identity();
    requests[3].half_extents = openusd_physx_vec3f{1.0F, 1.0F, 1.0F};
    requests[3].max_hits = 8;

    std::vector<openusd_physx_query_hit> hits(status.capacities.max_query_hits);
    openusd_physx_query_desc query_desc{};
    query_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_query_desc));
    query_desc.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION;
    query_desc.requests = requests;
    query_desc.request_count = 4;
    query_desc.hits = hits.data();
    query_desc.hit_capacity = hits.size();
    openusd_physx_query_result query_result{};
    query_result.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_query_result));
    CheckStatus(
        openusd_physx_world_query(world, &query_desc, &query_result, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "one batched query call covers every query kind");
    Check(query_result.rejected_request_count == 0, "every request in the batch is accepted");

    size_t raycast_hits = 0;
    size_t sweep_hits = 0;
    size_t overlap_hits = 0;
    size_t filtered_overlap_hits = 0;
    bool trigger_is_flagged = false;
    bool geometry_is_reported = true;
    bool sorted_by_distance = true;
    float previous_distance = -1.0F;
    uint64_t previous_user = 0;
    for (size_t index = 0; index < query_result.hit_count; ++index)
    {
        const openusd_physx_query_hit& hit = hits[index];
        Check(hit.actor_id != OPENUSD_PHYSX_INVALID_ID, "every hit carries a stable actor identity");
        Check(hit.shape_id != OPENUSD_PHYSX_INVALID_ID, "every hit carries a stable shape identity");
        Check(hit.reserved == 0, "the reserved hit field is never written");
        if (hit.user_id != previous_user)
        {
            previous_user = hit.user_id;
            previous_distance = -1.0F;
        }
        else if (hit.distance < previous_distance)
        {
            sorted_by_distance = false;
        }
        previous_distance = hit.distance;

        if (hit.user_id == 11)
        {
            ++raycast_hits;
            if ((hit.flags & OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_POSITION) == 0 ||
                (hit.flags & OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_NORMAL) == 0 ||
                (hit.flags & OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_DISTANCE) == 0)
            {
                geometry_is_reported = false;
            }
        }
        else if (hit.user_id == 12)
        {
            ++sweep_hits;
        }
        else if (hit.user_id == 13)
        {
            ++overlap_hits;
            if (hit.actor_id == trigger_id &&
                (hit.flags & OPENUSD_PHYSX_QUERY_HIT_FLAG_TRIGGER) != 0)
            {
                trigger_is_flagged = true;
            }
            if ((hit.flags & OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_DISTANCE) != 0)
            {
                geometry_is_reported = false;
            }
        }
        else if (hit.user_id == 14)
        {
            ++filtered_overlap_hits;
            if (hit.actor_id == trigger_id)
            {
                geometry_is_reported = false;
            }
        }
    }

    Check(raycast_hits > 0, "the raycast reports at least one hit");
    Check(sweep_hits > 0, "the sweep reports at least one hit");
    Check(overlap_hits > 0, "the overlap reports at least one hit");
    Check(trigger_is_flagged, "an overlap hit against a trigger volume is flagged as a trigger");
    Check(filtered_overlap_hits < overlap_hits, "excluding triggers removes the trigger volume from the results");
    Check(geometry_is_reported, "every hit reports exactly the geometry its query kind can produce");
    Check(sorted_by_distance, "the hits of one request are ordered nearest first");

    // A small hit page keeps the nearest hits of each request and counts the
    // rest, so the batch degrades predictably instead of failing.
    openusd_physx_query_hit small_hits[2]{};
    query_desc.hits = small_hits;
    query_desc.hit_capacity = 2;
    CheckStatus(
        openusd_physx_world_query(world, &query_desc, &query_result, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "a batch into a small hit page still succeeds");
    Check(query_result.hit_count <= 2, "a small hit page never reports more hits than it holds");
    Check(query_result.dropped_hit_count > 0, "the dropped hit count reports the truncation");
    Check(
        (query_result.overflow_flags & OPENUSD_PHYSX_OVERFLOW_QUERY_HITS) != 0,
        "the truncation raises the query hit overflow flag");
    Check(
        (query_result.overflow_flags & OPENUSD_PHYSX_OVERFLOW_QUERY_TRUNCATED) == 0,
        "a page level truncation still reports an exact dropped hit count");
    query_desc.hits = hits.data();
    query_desc.hit_capacity = hits.size();

    // A budget of one must keep the nearest hit, not whichever hit the
    // simulation SDK happened to write into its own buffer first. Running the
    // same ray with a full budget names the hit the narrow budget has to agree
    // with.
    openusd_physx_query_request column{};
    column.user_id = 21;
    column.type = OPENUSD_PHYSX_QUERY_RAYCAST;
    column.origin = openusd_physx_vec3f{0.0F, 8.0F, 0.0F};
    column.direction = openusd_physx_vec3f{0.0F, -1.0F, 0.0F};
    column.max_distance = 100.0F;
    column.max_hits = 8;
    query_desc.requests = &column;
    query_desc.request_count = 1;
    CheckStatus(
        openusd_physx_world_query(world, &query_desc, &query_result, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "a raycast down the whole column succeeds");
    const size_t wide_hit_count = query_result.hit_count;
    const openusd_physx_query_hit nearest = wide_hit_count == 0 ? openusd_physx_query_hit{} : hits[0];
    Check(wide_hit_count >= 2, "the column raycast reports several stacked hits");

    column.max_hits = 1;
    CheckStatus(
        openusd_physx_world_query(world, &query_desc, &query_result, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "the same raycast with a budget of one succeeds");
    Check(query_result.hit_count == 1, "a budget of one retains exactly one hit");
    Check(
        query_result.hit_count == 1 && hits[0].actor_id == nearest.actor_id &&
            hits[0].shape_id == nearest.shape_id,
        "a narrow budget keeps the nearest hit instead of an arbitrary one");
    Check(
        wide_hit_count >= 1 && query_result.dropped_hit_count == wide_hit_count - 1,
        "a narrow budget counts every hit it discarded exactly");
    Check(
        (query_result.overflow_flags & OPENUSD_PHYSX_OVERFLOW_QUERY_TRUNCATED) == 0,
        "a narrow budget never claims the simulation SDK truncated anything");

    // A sweep that starts inside a shape is discarded unless the request asks
    // for it, and when it is asked for it carries a zero distance and no
    // geometry at all rather than a fabricated pose.
    openusd_physx_query_request inside{};
    inside.user_id = 22;
    inside.type = OPENUSD_PHYSX_QUERY_SWEEP;
    inside.shape_type = OPENUSD_PHYSX_SHAPE_SPHERE;
    inside.origin = openusd_physx_vec3f{0.0F, -0.5F, 0.0F};
    inside.direction = openusd_physx_vec3f{0.0F, -1.0F, 0.0F};
    inside.rotation = Identity();
    inside.radius = 0.2F;
    inside.max_distance = 20.0F;
    inside.max_hits = 8;
    query_desc.requests = &inside;
    CheckStatus(
        openusd_physx_world_query(world, &query_desc, &query_result, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "a sweep that starts inside a shape succeeds");
    Check(
        query_result.hit_count == 0,
        "a sweep that starts inside a shape reports nothing unless initial overlaps are requested");

    inside.flags = OPENUSD_PHYSX_QUERY_FLAG_SWEEP_INITIAL_OVERLAP;
    CheckStatus(
        openusd_physx_world_query(world, &query_desc, &query_result, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "a sweep that requests initial overlaps succeeds");
    Check(query_result.hit_count >= 1, "requesting initial overlaps reports the shape the sweep starts in");
    bool initial_overlap_is_bare = query_result.hit_count >= 1;
    for (size_t index = 0; index < query_result.hit_count; ++index)
    {
        const openusd_physx_query_hit& hit = hits[index];
        if ((hit.flags & OPENUSD_PHYSX_QUERY_HIT_FLAG_INITIAL_OVERLAP) == 0)
        {
            continue;
        }
        if (hit.distance != 0.0F ||
            (hit.flags & OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_DISTANCE) == 0 ||
            (hit.flags & OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_POSITION) != 0 ||
            (hit.flags & OPENUSD_PHYSX_QUERY_HIT_FLAG_HAS_NORMAL) != 0)
        {
            initial_overlap_is_bare = false;
        }
    }
    Check(
        initial_overlap_is_bare,
        "an initially overlapping sweep hit carries a zero distance and no position or normal");

    query_desc.requests = requests;
    query_desc.request_count = 4;
    (void)query_desc;

    // Two worlds built from the same page and stepped from two threads must
    // produce the same events, because nothing about the event pipeline is
    // shared between worlds.
    std::vector<openusd_physx_event> first_events;
    std::vector<openusd_physx_event> second_events;
    const auto drive = [&page, &builder](std::vector<openusd_physx_event>& sink)
    {
        char thread_error_data[512]{};
        openusd_physx_error_buffer thread_error{thread_error_data, sizeof(thread_error_data), 0};
        openusd_physx_world* thread_world =
            CreateWorld(page, builder.Size(), thread_error_data, sizeof(thread_error_data));
        if (thread_world == nullptr)
        {
            return;
        }
        openusd_physx_world_status_info thread_status{};
        thread_status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
        openusd_physx_world_get_status(thread_world, &thread_status, &thread_error);

        ResultStorage thread_storage(thread_status.capacities);
        openusd_physx_result_page thread_results = thread_storage.Page();
        openusd_physx_step_desc thread_step{};
        thread_step.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_step_desc));
        thread_step.fixed_time_step = 1.0 / 60.0;
        thread_step.substep_count = 1;
        for (int index = 0; index < 60; ++index)
        {
            if (openusd_physx_world_step(thread_world, &thread_step, &thread_results, &thread_error) !=
                OPENUSD_PHYSX_STATUS_OK)
            {
                break;
            }
            for (uint32_t event = 0; event < thread_results.header.event_count; ++event)
            {
                sink.push_back(thread_results.events[event]);
            }
        }
        openusd_physx_world_release(thread_world);
    };

    std::thread first(drive, std::ref(first_events));
    std::thread second(drive, std::ref(second_events));
    first.join();
    second.join();

    Check(!first_events.empty(), "a concurrently driven world still reports events");
    bool worlds_agree = first_events.size() == second_events.size();
    for (size_t index = 0; worlds_agree && index < first_events.size(); ++index)
    {
        worlds_agree = first_events[index].type == second_events[index].type &&
            first_events[index].id0 == second_events[index].id0 &&
            first_events[index].id1 == second_events[index].id1 &&
            first_events[index].detail0 == second_events[index].detail0 &&
            first_events[index].detail1 == second_events[index].detail1 &&
            first_events[index].step_index == second_events[index].step_index;
    }
    Check(worlds_agree, "two concurrent worlds built from one page report the same event sequence");

    openusd_physx_world_release(world);

    ProbeWideWorldBuildsWithEvents();
    ProbeContinuousCollisionStopsATunnellingBody();

    if (g_failures != 0)
    {
        std::cerr << g_failures << " event and query checks failed\n";
        return 1;
    }
    std::cout << "retained world event and query probe passed\n";
    return 0;
}
