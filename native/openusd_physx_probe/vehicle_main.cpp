// Copyright (c) marcschier. Licensed under the MIT License.

// Probe for the vehicle domain the version 6 ABI adds. A four wheeled engine
// drive vehicle is built on a ground plane and then actually driven: it must
// accelerate under throttle, change gear through its autobox, steer measurably
// off its straight line, and decelerate under brake. Every check compares a
// measured result against what the same vehicle does without the command, so a
// check can only pass when the command reached the simulation SDK.

#include "openusd_physx_world.h"
#include "page_builder.h"

#include <cmath>
#include <iostream>
#include <limits>
#include <string>
#include <vector>

namespace
{
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

// ---------------------------------------------------------------------------
// A ground plane, a static ramp free chassis box, and one four wheeled engine
// drive vehicle. The forward axis is world Z, the right axis is world X, and the
// up axis is world Y, which is the axis frame the rest of the page uses.
// ---------------------------------------------------------------------------
struct VehicleScene
{
    std::vector<uint64_t> page;
    size_t page_size = 0;
    uint64_t vehicle_id = 0;
    uint64_t chassis_id = 0;
    uint64_t wheel_ids[4]{};
};

const float kChassisMass = 1500.0F;
const float kWheelRadius = 0.35F;
const float kWheelHalfWidth = 0.15F;
const float kChassisHalfLength = 2.0F;
const float kChassisHalfWidth = 0.9F;
const float kChassisHalfHeight = 0.25F;
const float kSuspensionTravel = 0.25F;

// Builds the vehicle page. `autobox` selects whether the vehicle declares an
// automatic gearbox, which is the only difference between the automatic and the
// manual drivetrain the probe drives. `forward_gear_count` is written into the
// record untouched so that a malformed gear budget can be handed to the world.
// `wheel_count` is how many wheel records the page actually carries, and
// `declared_wheel_count` overrides the count the vehicle record declares when it
// is non zero, so that a malformed wheel budget can be handed to the world too.
VehicleScene MakeVehicleScene(
    bool autobox,
    uint32_t forward_gear_count = 4,
    uint32_t wheel_count = 4,
    uint32_t declared_wheel_count = 0)
{
    openusd_physx_test::PageBuilder builder;
    VehicleScene scene;

    openusd_physx_scene_desc physics_scene{};
    physics_scene.id = builder.AddIdentity("/Vehicle/PhysicsScene", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    physics_scene.gravity_direction = openusd_physx_vec3f{0.0F, -1.0F, 0.0F};
    physics_scene.gravity_magnitude = 9.81F;
    physics_scene.position_iterations = 8;
    physics_scene.velocity_iterations = 2;
    physics_scene.bounce_threshold = 0.2F;
    physics_scene.contact_offset = 0.02F;
    builder.Scenes().push_back(physics_scene);

    openusd_physx_material_desc material{};
    material.id = builder.AddIdentity("/Vehicle/Material", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    material.static_friction = 1.0F;
    material.dynamic_friction = 1.0F;
    material.restitution = 0.0F;
    material.density = 1000.0F;
    builder.Materials().push_back(material);

    // A wide, thin static box is the road. A plane would do, but a box keeps the
    // road query result finite in every direction the vehicle can reach.
    openusd_physx_shape_desc ground{};
    ground.id = builder.AddIdentity("/Vehicle/GroundShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    ground.type = OPENUSD_PHYSX_SHAPE_BOX;
    ground.local_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    ground.scale = openusd_physx_vec3f{1.0F, 1.0F, 1.0F};
    ground.half_extents = openusd_physx_vec3f{500.0F, 0.5F, 500.0F};
    ground.material_index = 0;
    builder.Shapes().push_back(ground);

    openusd_physx_shape_desc chassis_shape{};
    chassis_shape.id = builder.AddIdentity("/Vehicle/ChassisShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    chassis_shape.type = OPENUSD_PHYSX_SHAPE_BOX;
    chassis_shape.local_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    chassis_shape.scale = openusd_physx_vec3f{1.0F, 1.0F, 1.0F};
    chassis_shape.half_extents =
        openusd_physx_vec3f{kChassisHalfWidth, kChassisHalfHeight, kChassisHalfLength};
    chassis_shape.material_index = 0;
    builder.Shapes().push_back(chassis_shape);

    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{0, -1});
    openusd_physx_actor_desc ground_actor{};
    ground_actor.id = builder.AddIdentity("/Vehicle/Ground", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    ground_actor.type = OPENUSD_PHYSX_ACTOR_STATIC;
    ground_actor.scene_index = 0;
    ground_actor.world_pose = openusd_physx_test::Pose(0.0F, -0.5F, 0.0F);
    ground_actor.shape_offset = 0;
    ground_actor.shape_count = 1;
    builder.Actors().push_back(ground_actor);

    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{1, -1});
    openusd_physx_actor_desc chassis{};
    chassis.id = builder.AddIdentity("/Vehicle/Chassis", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    chassis.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
    chassis.scene_index = 0;
    // The chassis starts one suspension travel above its rest height so the
    // suspension settles rather than starting fully extended into the road.
    chassis.world_pose = openusd_physx_test::Pose(0.0F, 0.8F, 0.0F);
    chassis.shape_offset = 1;
    chassis.shape_count = 1;
    chassis.mass = kChassisMass;
    chassis.flags = OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_SLEEPING;
    scene.chassis_id = chassis.id;
    builder.Actors().push_back(chassis);

    openusd_physx_vehicle_desc vehicle{};
    vehicle.id = builder.AddIdentity("/Vehicle/Car", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    vehicle.scene_index = 0;
    vehicle.actor_index = 1;
    vehicle.wheel_offset = 0;
    vehicle.wheel_count = declared_wheel_count != 0 ? declared_wheel_count : wheel_count;
    vehicle.flags = OPENUSD_PHYSX_VEHICLE_FLAG_PUBLISH_WHEELS;
    if (autobox)
    {
        vehicle.flags |= OPENUSD_PHYSX_VEHICLE_FLAG_AUTOBOX_ENABLED;
    }
    vehicle.drive = OPENUSD_PHYSX_VEHICLE_DRIVE_ENGINE;
    vehicle.query = OPENUSD_PHYSX_VEHICLE_QUERY_RAYCAST;
    vehicle.longitudinal_axis = OPENUSD_PHYSX_AXIS_Z;
    vehicle.lateral_axis = OPENUSD_PHYSX_AXIS_X;
    vehicle.vertical_axis = OPENUSD_PHYSX_AXIS_Y;
    vehicle.chassis_mass = kChassisMass;
    vehicle.chassis_moi = openusd_physx_vec3f{3625.0F, 3625.0F, 750.0F};
    vehicle.engine_peak_torque = 500.0F;
    vehicle.engine_moi = 1.0F;
    vehicle.engine_idle_omega = 75.0F;
    vehicle.engine_max_omega = 600.0F;
    vehicle.engine_damping_full_throttle = 0.15F;
    vehicle.engine_damping_zero_throttle_clutch_engaged = 2.0F;
    vehicle.engine_damping_zero_throttle_clutch_disengaged = 0.35F;
    vehicle.clutch_strength = 10.0F;
    vehicle.gear_switch_time = 0.5F;
    vehicle.final_gear_ratio = 4.0F;
    vehicle.reverse_gear_ratio = 4.0F;
    vehicle.first_gear_ratio = 4.0F;
    vehicle.top_gear_ratio = 1.1F;
    vehicle.forward_gear_count = forward_gear_count;
    vehicle.autobox_up_ratio = 0.65F;
    vehicle.autobox_down_ratio = 0.15F;
    vehicle.autobox_latency = 2.0F;
    vehicle.max_brake_torque = 3000.0F;
    vehicle.max_hand_brake_torque = 5000.0F;
    vehicle.max_steer_angle = 0.5F;
    vehicle.default_friction = 1.0F;
    scene.vehicle_id = vehicle.id;
    builder.Vehicles().push_back(vehicle);

    // Wheels tile axles two at a time. The first axle steers, every later axle
    // takes the handbrake, and every wheel is driven and braked, which is what
    // makes throttle, steer and brake all observable no matter how many axles
    // the page declares.
    const uint32_t axle_count = (wheel_count + 1U) / 2U;
    const float share = 1.0F / static_cast<float>(wheel_count);
    for (uint32_t index = 0; index < wheel_count; ++index)
    {
        const uint32_t axle = index / 2U;
        const bool front = axle == 0U;
        const float lateral = (index % 2 == 0) ? -kChassisHalfWidth : kChassisHalfWidth;
        // Axles spread evenly from the front of the chassis to the back, which
        // keeps every suspension attachment inside the chassis it hangs from.
        const float axle_fraction = axle_count <= 1U
            ? 0.0F
            : static_cast<float>(axle) / static_cast<float>(axle_count - 1U);
        const float longitudinal = kChassisHalfLength * 0.7F * (1.0F - (2.0F * axle_fraction));

        openusd_physx_vehicle_wheel_desc wheel{};
        wheel.id = builder
                       .AddIdentity(
                           "/Vehicle/Car/Wheel" + std::to_string(index), OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0)
                       .id;
        wheel.suspension_attachment = openusd_physx_test::Pose(lateral, 0.0F, longitudinal);
        wheel.suspension_travel_dir = openusd_physx_vec3f{0.0F, -1.0F, 0.0F};
        wheel.suspension_travel_dist = kSuspensionTravel;
        wheel.wheel_attachment = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
        wheel.radius = kWheelRadius;
        wheel.half_width = kWheelHalfWidth;
        wheel.mass = 20.0F;
        wheel.moi = 0.0F;
        wheel.damping_rate = 0.25F;
        wheel.suspension_stiffness = 35000.0F;
        wheel.suspension_damping = 4500.0F;
        wheel.sprung_mass = kChassisMass * share;
        wheel.tire_lat_stiff_x = 0.01F;
        wheel.tire_lat_stiff_y = 18.0F;
        wheel.tire_long_stiff = 5000.0F;
        wheel.tire_camber_stiff = 0.0F;
        wheel.tire_rest_load = 0.0F;
        wheel.tire_friction = 1.0F;
        wheel.steer_response = front ? 1.0F : 0.0F;
        wheel.brake_response = 1.0F;
        wheel.hand_brake_response = front ? 0.0F : 1.0F;
        wheel.drive_torque_ratio = share;
        wheel.axle_index = axle;
        wheel.flags = OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_BRAKES | OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_DRIVEN;
        if (front)
        {
            wheel.flags |= OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_STEERS;
        }
        else
        {
            wheel.flags |= OPENUSD_PHYSX_VEHICLE_WHEEL_FLAG_HAND_BRAKES;
        }
        if (index < 4)
        {
            scene.wheel_ids[index] = wheel.id;
        }
        builder.VehicleWheels().push_back(wheel);
    }

    openusd_physx_result_capacities capacities{};
    capacities.max_body_states = 16 + wheel_count;
    capacities.max_events = 256;
    capacities.max_diagnostics = 32;
    capacities.max_debug_lines = 0;
    capacities.max_query_hits = 16;
    builder.Header().capacities = capacities;

    scene.page = builder.Build();
    scene.page_size = builder.Size();
    return scene;
}

// Applies one driver input command and steps the world the requested number of
// times. Commands are batched, which is what the ABI requires of a hot path.
// Gear change events are counted as they are produced, because every step
// publishes only the events of that step.
bool Drive(
    openusd_physx_world* world,
    openusd_physx_result_page& results,
    uint64_t vehicle_id,
    float throttle,
    float brake,
    float steer,
    uint32_t steps,
    openusd_physx_error_buffer* error,
    const std::string& description,
    uint32_t* gear_changes = nullptr,
    float gear = 0.0F)
{
    openusd_physx_command command{};
    command.type = OPENUSD_PHYSX_COMMAND_VEHICLE_INPUT;
    command.target_id = vehicle_id;
    command.vector = openusd_physx_vec3f{throttle, brake, steer};
    command.point = openusd_physx_vec3f{0.0F, 0.0F, gear};

    openusd_physx_step_desc step_desc{};
    step_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_step_desc));
    step_desc.fixed_time_step = 1.0 / 60.0;
    step_desc.substep_count = 1;
    step_desc.commands = &command;
    step_desc.command_count = 1;

    for (uint32_t index = 0; index < steps; ++index)
    {
        if (openusd_physx_world_step(world, &step_desc, &results, error) != OPENUSD_PHYSX_STATUS_OK)
        {
            ++g_failures;
            std::cerr << "check failed: " << description << " could not step (step " << index << "): "
                      << (error->data != nullptr ? error->data : "") << '\n';
            return false;
        }
        if (gear_changes != nullptr)
        {
            for (uint32_t event_index = 0; event_index < results.header.event_count; ++event_index)
            {
                const openusd_physx_event& event = results.events[event_index];
                if (event.type == OPENUSD_PHYSX_EVENT_VEHICLE_GEAR_CHANGE && event.id0 == vehicle_id)
                {
                    Check(event.detail0 != event.detail1, "a gear change event names two different gears");
                    ++(*gear_changes);
                }
            }
        }
    }
    return true;
}

// Steps once with a single driver input command and reports the status the
// world returned. The world is expected to validate the whole batch before it
// applies any of it, so a rejected command must leave the simulation untouched.
openusd_physx_status StepWithInput(
    openusd_physx_world* world,
    openusd_physx_result_page& results,
    uint64_t vehicle_id,
    const openusd_physx_vec3f& vector,
    const openusd_physx_vec3f& point,
    openusd_physx_error_buffer* error)
{
    openusd_physx_command command{};
    command.type = OPENUSD_PHYSX_COMMAND_VEHICLE_INPUT;
    command.target_id = vehicle_id;
    command.vector = vector;
    command.point = point;

    openusd_physx_step_desc step_desc{};
    step_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_step_desc));
    step_desc.fixed_time_step = 1.0 / 60.0;
    step_desc.substep_count = 1;
    step_desc.commands = &command;
    step_desc.command_count = 1;
    return openusd_physx_world_step(world, &step_desc, &results, error);
}

// A malformed driver input must be rejected before the step runs, because the
// gear it carries indexes the fixed gearbox and autobox arrays of the
// simulation SDK. Rejection is proven to be atomic by the step index and the
// chassis pose, neither of which may move.
int RunCommandValidation(
    openusd_physx_world* world,
    openusd_physx_result_page& results,
    uint64_t vehicle_id,
    uint64_t chassis_id,
    openusd_physx_error_buffer* error)
{
    const openusd_physx_vec3f neutral_vector{0.0F, 1.0F, 0.0F};
    const openusd_physx_vec3f neutral_point{0.0F, 0.0F, 0.0F};
    if (StepWithInput(world, results, vehicle_id, neutral_vector, neutral_point, error) !=
        OPENUSD_PHYSX_STATUS_OK)
    {
        std::cerr << "the validation baseline step failed: " << (error->data != nullptr ? error->data : "") << '\n';
        return 1;
    }
    const openusd_physx_body_state* baseline_state = FindState(results, chassis_id);
    if (!Check(baseline_state != nullptr, "the chassis reports a body state before the malformed commands"))
    {
        return 1;
    }
    const uint64_t baseline_step = results.header.step_index;
    const openusd_physx_vec3f baseline_position = baseline_state->pose.position;

    const float max_gear = static_cast<float>(OPENUSD_PHYSX_MAX_VEHICLE_GEARS);
    struct MalformedCase
    {
        openusd_physx_vec3f vector;
        openusd_physx_vec3f point;
        const char* description;
    };
    const MalformedCase malformed[] = {
        {neutral_vector, {0.0F, 0.0F, -1.0F}, "a negative gear"},
        {neutral_vector, {0.0F, 0.0F, -0.5F}, "a small negative gear"},
        {neutral_vector, {0.0F, 0.0F, 1.5F}, "a fractional gear"},
        {neutral_vector, {0.0F, 0.0F, max_gear + 1.0F}, "a gear past the gear budget"},
        {neutral_vector, {0.0F, 0.0F, 1.0e30F}, "a huge gear"},
        {neutral_vector, {0.0F, 0.0F, std::numeric_limits<float>::quiet_NaN()}, "a gear that is not a number"},
        {neutral_vector, {0.0F, 0.0F, std::numeric_limits<float>::infinity()}, "an infinite gear"},
        {{2.0F, 0.0F, 0.0F}, neutral_point, "a throttle above one"},
        {{-1.0F, 0.0F, 0.0F}, neutral_point, "a negative throttle"},
        {{0.0F, 2.0F, 0.0F}, neutral_point, "a brake above one"},
        {{0.0F, 0.0F, -2.0F}, neutral_point, "a steer below minus one"},
        {neutral_vector, {2.0F, 0.0F, 0.0F}, "a handbrake above one"},
        {neutral_vector, {0.0F, -1.0F, 0.0F}, "a negative clutch"}};

    for (const MalformedCase& malformed_case : malformed)
    {
        const openusd_physx_status status =
            StepWithInput(world, results, vehicle_id, malformed_case.vector, malformed_case.point, error);
        CheckStatus(
            status,
            OPENUSD_PHYSX_STATUS_INVALID_ARGUMENT,
            std::string("the world rejects ") + malformed_case.description);
        Check(
            results.header.step_index == baseline_step,
            std::string("the world does not step for ") + malformed_case.description);
        const openusd_physx_body_state* rejected_state = FindState(results, chassis_id);
        Check(
            rejected_state != nullptr && rejected_state->pose.position.x == baseline_position.x &&
                rejected_state->pose.position.y == baseline_position.y &&
                rejected_state->pose.position.z == baseline_position.z,
            std::string("the chassis does not move for ") + malformed_case.description);
    }

    // The two edges of the documented encoding must still be accepted: zero
    // asks the autobox, and the largest value the ABI allows is bounded against
    // the gearbox the vehicle actually declares instead of reaching past it.
    CheckStatus(
        StepWithInput(world, results, vehicle_id, neutral_vector, {0.0F, 0.0F, 0.0F}, error),
        OPENUSD_PHYSX_STATUS_OK,
        "the world accepts the automatic gear");
    CheckStatus(
        StepWithInput(world, results, vehicle_id, neutral_vector, {0.0F, 0.0F, max_gear}, error),
        OPENUSD_PHYSX_STATUS_OK,
        "the world accepts the largest gear the ABI allows");
    CheckStatus(
        StepWithInput(world, results, vehicle_id, neutral_vector, {0.0F, 0.0F, 1.0F}, error),
        OPENUSD_PHYSX_STATUS_OK,
        "the world accepts the reverse gear");
    Check(
        results.header.step_index > baseline_step,
        "the world steps again once the commands are within the documented ranges");

    const openusd_physx_body_state* recovered = FindState(results, chassis_id);
    Check(
        recovered != nullptr && std::isfinite(recovered->pose.position.y),
        "the chassis is still simulated after the malformed commands");
    return 0;
}

// The wheel budget must be refused by the page contract rather than reaching the
// runtime, where every brake, steer, differential and axle table is a fixed
// array of exactly the simulation SDK wheel budget.
int RunWheelBudgetRejection(openusd_physx_world* world, openusd_physx_error_buffer* error)
{
    struct BudgetCase
    {
        uint32_t wheel_records;
        uint32_t declared;
        const char* description;
    };
    const BudgetCase refused[] = {
        {OPENUSD_PHYSX_MAX_VEHICLE_WHEELS + 1U, 0U, "a vehicle with one wheel past the budget"},
        {32U, 0U, "a vehicle with the wheel budget of the previous ABI"},
        {OPENUSD_PHYSX_MAX_VEHICLE_WHEELS,
         UINT32_MAX,
         "a vehicle declaring the largest unsigned wheel count"}};

    for (const BudgetCase& budget_case : refused)
    {
        const VehicleScene scene =
            MakeVehicleScene(true, 4, budget_case.wheel_records, budget_case.declared);
        openusd_physx_page_validation validation{};
        validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
        CheckStatus(
            openusd_physx_world_build(world, scene.page.data(), scene.page_size, &validation, error),
            OPENUSD_PHYSX_STATUS_INVALID_PAGE,
            std::string("the world refuses ") + budget_case.description);
        Check(
            validation.error_code == OPENUSD_PHYSX_PAGE_ERROR_RANGE &&
                validation.section == OPENUSD_PHYSX_SECTION_VEHICLES,
            std::string("the refusal of ") + budget_case.description + " names the vehicle section");
    }

    // The widest legal vehicle must still build and drive, which is what proves
    // the bound refuses overflow rather than every large wheel count.
    const VehicleScene accepted = MakeVehicleScene(true, 4, OPENUSD_PHYSX_MAX_VEHICLE_WHEELS);
    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    if (!CheckStatus(
            openusd_physx_world_build(world, accepted.page.data(), accepted.page_size, &validation, error),
            OPENUSD_PHYSX_STATUS_OK,
            "the world accepts the widest vehicle the ABI can carry"))
    {
        std::cerr << "  reported: " << (error->data != nullptr ? error->data : "") << '\n';
        return 1;
    }

    openusd_physx_world_status_info status{};
    status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
    CheckStatus(
        openusd_physx_world_get_status(world, &status, error),
        OPENUSD_PHYSX_STATUS_OK,
        "the widest vehicle status");
    Check(
        status.vehicle_wheel_count == OPENUSD_PHYSX_MAX_VEHICLE_WHEELS,
        "the world reports every wheel the widest vehicle declares");
    ResultStorage storage(status.capacities);
    openusd_physx_result_page results = storage.Page();
    if (!Drive(world, results, accepted.vehicle_id, 1.0F, 0.0F, 0.0F, 60, error, "the widest vehicle"))
    {
        return 1;
    }
    const openusd_physx_body_state* driven = FindState(results, accepted.chassis_id);
    Check(
        driven != nullptr && std::isfinite(driven->pose.position.y),
        "a vehicle with the largest wheel count still simulates");
    return 0;
}

// A forward gear count that overflows when reverse and neutral are added to it
// must be refused by the page contract rather than reaching the runtime, where
// it would size a loop that writes past the fixed gear ratio array. The counts
// are handed to the world exactly as a caller would write them.
int RunGearBudgetRejection(openusd_physx_world* world, openusd_physx_error_buffer* error)
{
    struct BudgetCase
    {
        uint32_t forward_gear_count;
        const char* description;
    };
    const BudgetCase refused[] = {
        {UINT32_MAX, "a forward gear count of the largest unsigned value"},
        {UINT32_MAX - 1U, "a forward gear count one below the largest unsigned value"},
        {OPENUSD_PHYSX_MAX_VEHICLE_GEARS, "a forward gear count that leaves no room for reverse and neutral"},
        {OPENUSD_PHYSX_MAX_VEHICLE_GEARS - 1U, "a forward gear count that leaves room for only one of them"},
        {0U, "a gearbox with no forward gear"}};

    for (const BudgetCase& budget_case : refused)
    {
        const VehicleScene scene = MakeVehicleScene(true, budget_case.forward_gear_count);
        openusd_physx_page_validation validation{};
        validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
        const openusd_physx_status status =
            openusd_physx_world_build(world, scene.page.data(), scene.page_size, &validation, error);
        CheckStatus(
            status,
            OPENUSD_PHYSX_STATUS_INVALID_PAGE,
            std::string("the world refuses ") + budget_case.description);
        Check(
            validation.error_code == OPENUSD_PHYSX_PAGE_ERROR_VALUE &&
                validation.section == OPENUSD_PHYSX_SECTION_VEHICLES,
            std::string("the refusal of ") + budget_case.description + " names the vehicle section");
    }

    // The largest gear budget the encoding can carry must still build, which is
    // what proves the bound rejects overflow rather than every large value.
    const VehicleScene accepted = MakeVehicleScene(true, OPENUSD_PHYSX_MAX_VEHICLE_GEARS - 2U);
    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    if (!CheckStatus(
            openusd_physx_world_build(world, accepted.page.data(), accepted.page_size, &validation, error),
            OPENUSD_PHYSX_STATUS_OK,
            "the world accepts the largest gearbox the ABI can carry"))
    {
        std::cerr << "  reported: " << (error->data != nullptr ? error->data : "") << '\n';
        return 1;
    }

    openusd_physx_world_status_info status{};
    status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
    CheckStatus(
        openusd_physx_world_get_status(world, &status, error), OPENUSD_PHYSX_STATUS_OK, "the widest gearbox status");
    ResultStorage storage(status.capacities);
    openusd_physx_result_page results = storage.Page();
    if (!Drive(world, results, accepted.vehicle_id, 1.0F, 0.0F, 0.0F, 30, error, "the widest gearbox"))
    {
        return 1;
    }
    const openusd_physx_body_state* driven = FindState(results, accepted.chassis_id);
    Check(
        driven != nullptr && std::isfinite(driven->pose.position.y),
        "a vehicle with the largest gearbox still simulates");
    return 0;
}

// A vehicle that does not declare an autobox must never shift by itself, must
// start in a gear it can drive in, and must still obey an explicit gear command.
// A vehicle that does declare one must shift by itself, which the accelerating
// run above already proves.
int RunManualGearbox(openusd_physx_world* world, openusd_physx_error_buffer* error)
{
    const VehicleScene scene = MakeVehicleScene(false);
    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    if (!CheckStatus(
            openusd_physx_world_build(world, scene.page.data(), scene.page_size, &validation, error),
            OPENUSD_PHYSX_STATUS_OK,
            "the manual vehicle page builds"))
    {
        std::cerr << "  reported: " << (error->data != nullptr ? error->data : "") << '\n';
        return 1;
    }

    openusd_physx_world_status_info status{};
    status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
    CheckStatus(
        openusd_physx_world_get_status(world, &status, error), OPENUSD_PHYSX_STATUS_OK, "the manual vehicle status");
    ResultStorage storage(status.capacities);
    openusd_physx_result_page results = storage.Page();

    // The very first step must not report a gear change. A manual vehicle is
    // built in its first forward gear, so a change reported here would be an
    // artefact of the world record rather than a real shift.
    uint32_t initial_changes = 0;
    if (!Drive(
            world,
            results,
            scene.vehicle_id,
            0.0F,
            1.0F,
            0.0F,
            1,
            error,
            "the first manual vehicle step",
            &initial_changes))
    {
        return 1;
    }
    Check(initial_changes == 0, "the first step of a manual vehicle reports no gear change");

    if (!Drive(world, results, scene.vehicle_id, 0.0F, 1.0F, 0.0F, 60, error, "the settling manual vehicle"))
    {
        return 1;
    }
    const openusd_physx_body_state* settled = FindState(results, scene.chassis_id);
    if (!Check(settled != nullptr, "the manual chassis reports a body state"))
    {
        return 1;
    }
    const float settled_z = settled->pose.position.z;

    // Full throttle with the gear left to the drivetrain. Without an autobox
    // there is nothing that could shift, so the vehicle must drive on the gear
    // it was built in and must report no gear change at all.
    uint32_t manual_changes = 0;
    if (!Drive(
            world,
            results,
            scene.vehicle_id,
            1.0F,
            0.0F,
            0.0F,
            240,
            error,
            "the accelerating manual vehicle",
            &manual_changes))
    {
        return 1;
    }
    const openusd_physx_body_state* accelerated = FindState(results, scene.chassis_id);
    if (!Check(accelerated != nullptr, "the accelerating manual chassis reports a body state"))
    {
        return 1;
    }
    Check(
        accelerated->pose.position.z - settled_z > 5.0F,
        "a vehicle without an autobox drives forward on the gear it starts in");
    Check(manual_changes == 0, "a vehicle without an autobox never shifts by itself");

    // Stop, then select reverse explicitly. An explicit gear command is honoured
    // whether or not the vehicle has an autobox, and it is the only thing that
    // can move this gearbox.
    if (!Drive(world, results, scene.vehicle_id, 0.0F, 1.0F, 0.0F, 240, error, "the stopping manual vehicle"))
    {
        return 1;
    }
    const openusd_physx_body_state* stopped = FindState(results, scene.chassis_id);
    if (!Check(stopped != nullptr, "the stopped manual chassis reports a body state"))
    {
        return 1;
    }
    const float stopped_z = stopped->pose.position.z;

    uint32_t reverse_changes = 0;
    if (!Drive(
            world,
            results,
            scene.vehicle_id,
            1.0F,
            0.0F,
            0.0F,
            240,
            error,
            "the reversing manual vehicle",
            &reverse_changes,
            1.0F))
    {
        return 1;
    }
    const openusd_physx_body_state* reversed = FindState(results, scene.chassis_id);
    if (!Check(reversed != nullptr, "the reversing manual chassis reports a body state"))
    {
        return 1;
    }
    Check(reverse_changes > 0, "an explicit gear command shifts a vehicle without an autobox");
    Check(
        reversed->pose.position.z < stopped_z - 0.5F,
        "a vehicle commanded into reverse drives backwards");
    return 0;
}

int RunVehicle(openusd_physx_world* world, openusd_physx_error_buffer* error)
{
    const VehicleScene scene = MakeVehicleScene(true);
    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    if (!CheckStatus(
            openusd_physx_world_build(world, scene.page.data(), scene.page_size, &validation, error),
            OPENUSD_PHYSX_STATUS_OK,
            "the vehicle page builds"))
    {
        std::cerr << "  reported: " << (error->data != nullptr ? error->data : "") << '\n';
        return 1;
    }

    openusd_physx_world_status_info status{};
    status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
    CheckStatus(
        openusd_physx_world_get_status(world, &status, error), OPENUSD_PHYSX_STATUS_OK, "vehicle page status");
    Check(status.vehicle_count == 1, "the world reports the vehicle the page declares");
    Check(status.vehicle_wheel_count == 4, "the world reports four wheels");
    Check(status.dynamic_actor_count == 5, "the chassis and every published wheel report a body state");

    ResultStorage storage(status.capacities);
    openusd_physx_result_page results = storage.Page();

    // The very first step must not report a gear change. The gearbox already
    // sits on the start gear the autobox flag selected, so a change reported
    // here would be an artefact of the world record rather than a real shift.
    uint32_t initial_changes = 0;
    if (!Drive(
            world, results, scene.vehicle_id, 0.0F, 1.0F, 0.0F, 1, error, "the first vehicle step", &initial_changes))
    {
        return 1;
    }
    Check(initial_changes == 0, "the first step of an automatic vehicle reports no gear change");

    // Settle on the suspension with the brake held, which is what a parked
    // vehicle does. An automatic drivetrain idles against the brake instead of
    // creeping away.
    if (!Drive(world, results, scene.vehicle_id, 0.0F, 1.0F, 0.0F, 60, error, "the settling vehicle"))
    {
        return 1;
    }
    const openusd_physx_body_state* settled = FindState(results, scene.chassis_id);
    if (!Check(settled != nullptr, "the chassis reports a body state"))
    {
        return 1;
    }
    const float settled_height = settled->pose.position.y;
    const float settled_z = settled->pose.position.z;
    Check(
        settled_height > 0.2F && settled_height < 1.2F,
        "the suspension holds the chassis above the road instead of dropping through it");
    Check(std::fabs(settled->linear_velocity.z) < 0.5F, "a braked vehicle does not roll away");

    for (uint32_t index = 0; index < 4; ++index)
    {
        const openusd_physx_body_state* wheel = FindState(results, scene.wheel_ids[index]);
        if (!Check(wheel != nullptr, "every wheel reports a body state"))
        {
            return 1;
        }
        Check(
            (wheel->flags & OPENUSD_PHYSX_BODY_STATE_FLAG_VEHICLE_WHEEL) != 0,
            "a wheel body state carries the vehicle wheel flag");
        Check(
            wheel->pose.position.y < settled_height,
            "a wheel is published below the chassis it hangs from");
    }

    // Full throttle, straight ahead. Three seconds of a five hundred newton
    // metre engine through a four to one first gear must move the car.
    uint32_t gear_changes = 0;
    if (!Drive(
            world, results, scene.vehicle_id, 1.0F, 0.0F, 0.0F, 180, error, "the accelerating vehicle", &gear_changes))
    {
        return 1;
    }
    const openusd_physx_body_state* accelerated = FindState(results, scene.chassis_id);
    if (!Check(accelerated != nullptr, "the accelerating chassis reports a body state"))
    {
        return 1;
    }
    const float travelled = accelerated->pose.position.z - settled_z;
    const float top_speed = accelerated->linear_velocity.z;
    Check(travelled > 5.0F, "a vehicle under full throttle drives forward");
    Check(top_speed > 3.0F, "a vehicle under full throttle reaches a real forward speed");
    Check(std::fabs(accelerated->pose.position.x) < 1.0F, "a vehicle with no steer input drives straight");

    // The autobox must have shifted at least once on the way up, and every shift
    // is reported as an event carrying both gears.
    Check(gear_changes > 0, "an accelerating vehicle reports at least one autobox gear change");

    // Full brake from the straight run must take the ground speed down sharply.
    // Braking is measured before the steer test so a yawing chassis cannot be
    // confused with a vehicle that failed to slow.
    const float speed_before_brake =
        std::sqrt(accelerated->linear_velocity.x * accelerated->linear_velocity.x +
                  accelerated->linear_velocity.z * accelerated->linear_velocity.z);
    Check(speed_before_brake > 1.0F, "the vehicle is still moving when the brake is applied");
    if (!Drive(world, results, scene.vehicle_id, 0.0F, 1.0F, 0.0F, 180, error, "the braking vehicle"))
    {
        return 1;
    }
    const openusd_physx_body_state* braked = FindState(results, scene.chassis_id);
    if (!Check(braked != nullptr, "the braking chassis reports a body state"))
    {
        return 1;
    }
    const float speed_after_brake =
        std::sqrt(braked->linear_velocity.x * braked->linear_velocity.x +
                  braked->linear_velocity.z * braked->linear_velocity.z);
    Check(
        speed_after_brake < speed_before_brake * 0.25F,
        "a vehicle under full brake sheds most of its speed");

    // Full steer to one side. The lateral offset it builds up in two seconds can
    // only come from the steer command, because the straight run above proved
    // the same vehicle holds its line without one.
    const float straight_x = braked->pose.position.x;
    if (!Drive(world, results, scene.vehicle_id, 1.0F, 0.0F, 1.0F, 180, error, "the steering vehicle"))
    {
        return 1;
    }
    const openusd_physx_body_state* steered = FindState(results, scene.chassis_id);
    if (!Check(steered != nullptr, "the steering chassis reports a body state"))
    {
        return 1;
    }
    Check(
        std::fabs(steered->pose.position.x - straight_x) > 0.5F,
        "a vehicle under full steer leaves the line it was driving");
    Check(
        std::fabs(steered->angular_velocity.y) > 0.05F,
        "a vehicle under full steer yaws about its vertical axis");

    // A reset must return the vehicle to the pose and the drivetrain state it
    // was built with, not merely move the chassis actor.
    openusd_physx_reset_desc reset{};
    reset.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_reset_desc));
    CheckStatus(openusd_physx_world_reset(world, &reset, error), OPENUSD_PHYSX_STATUS_OK, "the vehicle world resets");
    if (!Drive(world, results, scene.vehicle_id, 0.0F, 0.0F, 0.0F, 1, error, "the reset vehicle"))
    {
        return 1;
    }
    const openusd_physx_body_state* after_reset = FindState(results, scene.chassis_id);
    Check(
        after_reset != nullptr && std::fabs(after_reset->pose.position.z) < 0.1F,
        "a reset returns the vehicle to its authored position");
    Check(
        after_reset != nullptr && std::fabs(after_reset->linear_velocity.z) < 0.5F,
        "a reset clears the speed the vehicle had built up");
    return RunCommandValidation(world, results, scene.vehicle_id, scene.chassis_id, error);
}
} // namespace

int main()
{
    char error_data[1024]{};
    openusd_physx_error_buffer error{error_data, sizeof(error_data), 0};

    openusd_physx_capabilities capabilities{};
    capabilities.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_capabilities));
    CheckStatus(
        openusd_physx_world_get_capabilities(OPENUSD_PHYSX_WORLD_ABI_VERSION, &capabilities, &error),
        OPENUSD_PHYSX_STATUS_OK,
        "world_get_capabilities");
    Check((capabilities.flags & OPENUSD_PHYSX_CAPABILITY_VEHICLES) != 0, "the library reports vehicles");

    openusd_physx_world_desc world_desc{};
    world_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_desc));
    world_desc.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION;
    world_desc.worker_thread_count = 2;
    world_desc.flags = OPENUSD_PHYSX_WORLD_FLAG_ENABLE_EVENTS;

    openusd_physx_world* world = nullptr;
    if (!CheckStatus(openusd_physx_world_create(&world_desc, &world, &error), OPENUSD_PHYSX_STATUS_OK, "world_create") ||
        world == nullptr)
    {
        std::cerr << "the world could not be created: " << error_data << '\n';
        return 1;
    }

    const int fatal = RunVehicle(world, &error);
    const int budget_fatal = fatal == 0 ? RunGearBudgetRejection(world, &error) : 0;
    const int wheel_fatal =
        fatal == 0 && budget_fatal == 0 ? RunWheelBudgetRejection(world, &error) : 0;
    const int manual_fatal =
        fatal == 0 && budget_fatal == 0 && wheel_fatal == 0 ? RunManualGearbox(world, &error) : 0;
    openusd_physx_world_release(world);

    if (fatal != 0 || budget_fatal != 0 || wheel_fatal != 0 || manual_fatal != 0 || g_failures != 0)
    {
        std::cerr << g_failures << " vehicle check(s) failed.\n";
        return 1;
    }
    std::cout << "vehicle probe passed.\n";
    return 0;
}
