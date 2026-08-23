// Copyright (c) marcschier. Licensed under the MIT License.

// Probe for the CPU collision, rigid body, and joint domains the version 4 ABI
// adds. Every check compares a simulated result against a number the authored
// description predicts, and against the number the simulation would produce if
// the authored field were dropped, so a check can only pass when the field
// actually reaches PhysX.

#include "openusd_physx_world.h"
#include "page_builder.h"

#include <cmath>
#include <cstring>
#include <iostream>
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

bool CheckNear(float value, float expected, float tolerance, const std::string& description)
{
    if (!std::isfinite(value) || std::fabs(value - expected) > tolerance)
    {
        ++g_failures;
        std::cerr << "check failed: " << description << " (value " << value << ", expected " << expected
                  << " within " << tolerance << ")\n";
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

openusd_physx_quatf AxisRotation(float angle, float x, float y, float z) noexcept
{
    const float half = angle * 0.5F;
    const float sine = std::sin(half);
    openusd_physx_quatf value{};
    value.x = x * sine;
    value.y = y * sine;
    value.z = z * sine;
    value.w = std::cos(half);
    return value;
}

// The rotation angle about the local X axis, which is the twist a joint limits.
float TwistAngle(const openusd_physx_quatf& rotation) noexcept
{
    return 2.0F * std::atan2(rotation.x, rotation.w);
}

void PushCapacities(openusd_physx_test::PageBuilder& builder, uint32_t bodies, uint32_t events)
{
    openusd_physx_result_capacities capacities{};
    capacities.max_body_states = bodies;
    capacities.max_events = events;
    capacities.max_diagnostics = 32;
    capacities.max_debug_lines = 0;
    capacities.max_query_hits = 16;
    builder.Header().capacities = capacities;
}

openusd_physx_scene_desc MakeScene(openusd_physx_test::PageBuilder& builder, const char* path)
{
    openusd_physx_scene_desc scene{};
    scene.id = builder.AddIdentity(path, OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    scene.gravity_direction = openusd_physx_vec3f{0.0F, -1.0F, 0.0F};
    scene.gravity_magnitude = 9.81F;
    scene.position_iterations = 8;
    scene.velocity_iterations = 2;
    scene.bounce_threshold = 0.2F;
    scene.contact_offset = 0.02F;
    return scene;
}

openusd_physx_material_desc MakeMaterial(openusd_physx_test::PageBuilder& builder, const char* path)
{
    openusd_physx_material_desc material{};
    material.id = builder.AddIdentity(path, OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    material.static_friction = 0.8F;
    material.dynamic_friction = 0.7F;
    material.restitution = 0.0F;
    material.density = 1000.0F;
    return material;
}

// Steps the world the requested number of times and leaves the last result in
// the caller owned page.
bool StepWorld(
    openusd_physx_world* world,
    openusd_physx_result_page& results,
    uint32_t steps,
    openusd_physx_error_buffer* error,
    const std::string& description)
{
    openusd_physx_step_desc step_desc{};
    step_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_step_desc));
    step_desc.fixed_time_step = 1.0 / 60.0;
    step_desc.substep_count = 1;
    for (uint32_t index = 0; index < steps; ++index)
    {
        if (openusd_physx_world_step(world, &step_desc, &results, error) != OPENUSD_PHYSX_STATUS_OK)
        {
            ++g_failures;
            std::cerr << "check failed: " << description << " could not step (step " << index << ")\n";
            return false;
        }
    }
    return true;
}

const float kPi = 3.14159265358979323846F;

// ---------------------------------------------------------------------------
// A page with a static ground, a static height field, and one dynamic body per
// analytic shape type the version 4 ABI adds.
// ---------------------------------------------------------------------------
struct ShapeScene
{
    std::vector<uint64_t> page;
    size_t page_size = 0;
    uint64_t cylinder_id = 0;
    uint64_t cone_id = 0;
    uint64_t capsule_id = 0;
    uint64_t heightfield_sphere_id = 0;
};

ShapeScene MakeShapeScene()
{
    openusd_physx_test::PageBuilder builder;
    builder.Scenes().push_back(MakeScene(builder, "/Shapes/PhysicsScene"));
    builder.Materials().push_back(MakeMaterial(builder, "/Shapes/Material"));

    const openusd_physx_vec3f unit_scale{1.0F, 1.0F, 1.0F};

    openusd_physx_shape_desc ground{};
    ground.id = builder.AddIdentity("/Shapes/GroundShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    ground.type = OPENUSD_PHYSX_SHAPE_BOX;
    ground.local_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    ground.scale = unit_scale;
    ground.half_extents = openusd_physx_vec3f{40.0F, 0.5F, 40.0F};
    ground.material_index = 0;
    /* A shape that authors its own offsets proves the page reaches the shape
     * rather than the scene default. */
    ground.contact_offset = 0.03F;
    ground.rest_offset = 0.0F;
    builder.Shapes().push_back(ground);

    // Upright cylinder: the convex core axis is local X, so a quarter turn
    // about Z sends it to Y.
    openusd_physx_shape_desc cylinder{};
    cylinder.id = builder.AddIdentity("/Shapes/CylinderShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    cylinder.type = OPENUSD_PHYSX_SHAPE_CYLINDER;
    cylinder.local_pose.position = openusd_physx_vec3f{0.0F, 0.0F, 0.0F};
    cylinder.local_pose.rotation = AxisRotation(kPi * 0.5F, 0.0F, 0.0F, 1.0F);
    cylinder.scale = unit_scale;
    cylinder.radius = 0.5F;
    cylinder.half_height = 0.5F;
    cylinder.material_index = 0;
    builder.Shapes().push_back(cylinder);

    openusd_physx_shape_desc cone{};
    cone.id = builder.AddIdentity("/Shapes/ConeShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    cone.type = OPENUSD_PHYSX_SHAPE_CONE;
    cone.local_pose.position = openusd_physx_vec3f{0.0F, 0.0F, 0.0F};
    cone.local_pose.rotation = AxisRotation(kPi * 0.5F, 0.0F, 0.0F, 1.0F);
    cone.scale = unit_scale;
    cone.radius = 0.5F;
    cone.half_height = 0.5F;
    cone.material_index = 0;
    builder.Shapes().push_back(cone);

    openusd_physx_shape_desc capsule{};
    capsule.id = builder.AddIdentity("/Shapes/CapsuleShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    capsule.type = OPENUSD_PHYSX_SHAPE_CAPSULE;
    capsule.local_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    capsule.scale = unit_scale;
    capsule.radius = 0.3F;
    capsule.half_height = 0.4F;
    capsule.material_index = 0;
    /* Torsional friction needs a patch radius, which is one of the offsets the
     * version 4 shape record carries. */
    capsule.torsional_patch_radius = 0.05F;
    capsule.min_torsional_patch_radius = 0.01F;
    builder.Shapes().push_back(capsule);

    // A flat height field whose raw samples are 100 and whose height scale is
    // one hundredth, so the surface sits at exactly one unit. Dropping the
    // scale would put the surface a hundred units up instead.
    const uint32_t rows = 8;
    const uint32_t columns = 8;
    for (uint32_t sample = 0; sample < rows * columns; ++sample)
    {
        openusd_physx_heightfield_sample value{};
        value.height = 100;
        value.material0 = 0;
        value.material1 = 0;
        builder.HeightfieldSamples().push_back(value);
    }

    openusd_physx_shape_desc field{};
    field.id = builder.AddIdentity("/Shapes/HeightFieldShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    field.type = OPENUSD_PHYSX_SHAPE_HEIGHTFIELD;
    field.local_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    field.scale = unit_scale;
    field.material_index = 0;
    field.sample_offset = 0;
    field.row_count = rows;
    field.column_count = columns;
    field.height_scale = 0.01F;
    field.row_scale = 1.0F;
    field.column_scale = 1.0F;
    builder.Shapes().push_back(field);

    openusd_physx_shape_desc sphere{};
    sphere.id = builder.AddIdentity("/Shapes/SphereShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    sphere.type = OPENUSD_PHYSX_SHAPE_SPHERE;
    sphere.local_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    sphere.scale = unit_scale;
    sphere.radius = 0.5F;
    sphere.material_index = 0;
    builder.Shapes().push_back(sphere);

    for (uint32_t index = 0; index < 6; ++index)
    {
        builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{index, -1});
    }

    ShapeScene scene;

    openusd_physx_actor_desc ground_body{};
    ground_body.id = builder.AddIdentity("/Shapes/GroundBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    ground_body.scene_index = 0;
    ground_body.type = OPENUSD_PHYSX_ACTOR_STATIC;
    ground_body.world_pose = openusd_physx_test::Pose(0.0F, -0.5F, 0.0F);
    ground_body.shape_offset = 0;
    ground_body.shape_count = 1;
    builder.Actors().push_back(ground_body);

    openusd_physx_actor_desc cylinder_body{};
    cylinder_body.id = builder.AddIdentity("/Shapes/CylinderBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    cylinder_body.scene_index = 0;
    cylinder_body.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
    cylinder_body.world_pose = openusd_physx_test::Pose(-4.0F, 2.0F, 0.0F);
    cylinder_body.mass = 1.0F;
    cylinder_body.shape_offset = 1;
    cylinder_body.shape_count = 1;
    scene.cylinder_id = cylinder_body.id;
    builder.Actors().push_back(cylinder_body);

    openusd_physx_actor_desc cone_body{};
    cone_body.id = builder.AddIdentity("/Shapes/ConeBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    cone_body.scene_index = 0;
    cone_body.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
    cone_body.world_pose = openusd_physx_test::Pose(-8.0F, 2.0F, 0.0F);
    cone_body.mass = 1.0F;
    cone_body.shape_offset = 2;
    cone_body.shape_count = 1;
    scene.cone_id = cone_body.id;
    builder.Actors().push_back(cone_body);

    openusd_physx_actor_desc capsule_body{};
    capsule_body.id = builder.AddIdentity("/Shapes/CapsuleBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    capsule_body.scene_index = 0;
    capsule_body.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
    capsule_body.world_pose = openusd_physx_test::Pose(-12.0F, 2.0F, 0.0F);
    capsule_body.mass = 1.0F;
    capsule_body.shape_offset = 3;
    capsule_body.shape_count = 1;
    scene.capsule_id = capsule_body.id;
    builder.Actors().push_back(capsule_body);

    openusd_physx_actor_desc field_body{};
    field_body.id = builder.AddIdentity("/Shapes/HeightFieldBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    field_body.scene_index = 0;
    field_body.type = OPENUSD_PHYSX_ACTOR_STATIC;
    field_body.world_pose = openusd_physx_test::Pose(-3.5F, 0.0F, -3.5F);
    field_body.shape_offset = 4;
    field_body.shape_count = 1;
    builder.Actors().push_back(field_body);

    openusd_physx_actor_desc sphere_body{};
    sphere_body.id = builder.AddIdentity("/Shapes/SphereBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    sphere_body.scene_index = 0;
    sphere_body.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
    sphere_body.world_pose = openusd_physx_test::Pose(0.0F, 4.0F, 0.0F);
    sphere_body.mass = 1.0F;
    sphere_body.shape_offset = 5;
    sphere_body.shape_count = 1;
    scene.heightfield_sphere_id = sphere_body.id;
    builder.Actors().push_back(sphere_body);

    PushCapacities(builder, 16, 256);
    scene.page = builder.Build();
    scene.page_size = builder.Size();
    return scene;
}

// ---------------------------------------------------------------------------
// A page that authors the per body budgets and locks the version 4 actor
// record adds.
// ---------------------------------------------------------------------------
struct TuningScene
{
    std::vector<uint64_t> page;
    size_t page_size = 0;
    uint64_t locked_id = 0;
    uint64_t clamped_id = 0;
    uint64_t free_id = 0;
};

TuningScene MakeTuningScene()
{
    openusd_physx_test::PageBuilder builder;
    builder.Scenes().push_back(MakeScene(builder, "/Tuning/PhysicsScene"));
    builder.Materials().push_back(MakeMaterial(builder, "/Tuning/Material"));

    const openusd_physx_vec3f unit_scale{1.0F, 1.0F, 1.0F};
    openusd_physx_shape_desc sphere{};
    sphere.id = builder.AddIdentity("/Tuning/SphereShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    sphere.type = OPENUSD_PHYSX_SHAPE_SPHERE;
    sphere.local_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    sphere.scale = unit_scale;
    sphere.radius = 0.25F;
    sphere.material_index = 0;
    builder.Shapes().push_back(sphere);

    for (uint32_t index = 0; index < 3; ++index)
    {
        builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{0, -1});
    }

    TuningScene scene;

    // Locked on both horizontal axes and launched diagonally: only the vertical
    // component may change.
    openusd_physx_actor_desc locked{};
    locked.id = builder.AddIdentity("/Tuning/LockedBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    locked.scene_index = 0;
    locked.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
    locked.world_pose = openusd_physx_test::Pose(0.0F, 20.0F, 0.0F);
    locked.mass = 1.0F;
    locked.linear_velocity = openusd_physx_vec3f{6.0F, 0.0F, -4.0F};
    locked.flags = OPENUSD_PHYSX_ACTOR_FLAG_LOCK_LINEAR_X | OPENUSD_PHYSX_ACTOR_FLAG_LOCK_LINEAR_Z |
        OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_SLEEPING;
    locked.shape_offset = 0;
    locked.shape_count = 1;
    scene.locked_id = locked.id;
    builder.Actors().push_back(locked);

    // Falling with a clamped speed. After one second of free fall an
    // unclamped body reaches 9.81 units per second, so a clamp of one is a
    // tenfold separation.
    openusd_physx_actor_desc clamped{};
    clamped.id = builder.AddIdentity("/Tuning/ClampedBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    clamped.scene_index = 0;
    clamped.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
    clamped.world_pose = openusd_physx_test::Pose(4.0F, 20.0F, 0.0F);
    clamped.mass = 1.0F;
    clamped.max_linear_velocity = 1.0F;
    clamped.flags = OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_SLEEPING;
    clamped.shape_offset = 1;
    clamped.shape_count = 1;
    scene.clamped_id = clamped.id;
    builder.Actors().push_back(clamped);

    // The control that proves the clamp above is not the simulation default.
    openusd_physx_actor_desc free_body{};
    free_body.id = builder.AddIdentity("/Tuning/FreeBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    free_body.scene_index = 0;
    free_body.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
    free_body.world_pose = openusd_physx_test::Pose(8.0F, 20.0F, 0.0F);
    free_body.mass = 1.0F;
    free_body.position_iterations = 16;
    free_body.velocity_iterations = 4;
    free_body.flags = OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_SLEEPING;
    free_body.shape_offset = 2;
    free_body.shape_count = 1;
    scene.free_id = free_body.id;
    builder.Actors().push_back(free_body);

    PushCapacities(builder, 8, 64);
    scene.page = builder.Build();
    scene.page_size = builder.Size();
    return scene;
}

// ---------------------------------------------------------------------------
// A page that exercises the six degree of freedom joint, an angular limit, and
// a joint that must break.
// ---------------------------------------------------------------------------
struct JointScene
{
    std::vector<uint64_t> page;
    size_t page_size = 0;
    uint64_t driven_id = 0;
    uint64_t twisted_id = 0;
    uint64_t hanging_id = 0;
    uint64_t breakable_joint_id = 0;
};

JointScene MakeJointScene()
{
    openusd_physx_test::PageBuilder builder;
    builder.Scenes().push_back(MakeScene(builder, "/Joints/PhysicsScene"));
    builder.Materials().push_back(MakeMaterial(builder, "/Joints/Material"));

    const openusd_physx_vec3f unit_scale{1.0F, 1.0F, 1.0F};
    openusd_physx_shape_desc box{};
    box.id = builder.AddIdentity("/Joints/BoxShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    box.type = OPENUSD_PHYSX_SHAPE_BOX;
    box.local_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    box.scale = unit_scale;
    box.half_extents = openusd_physx_vec3f{0.2F, 0.2F, 0.2F};
    box.material_index = 0;
    builder.Shapes().push_back(box);

    for (uint32_t index = 0; index < 3; ++index)
    {
        builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{0, -1});
    }

    JointScene scene;

    openusd_physx_actor_desc driven{};
    driven.id = builder.AddIdentity("/Joints/DrivenBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    driven.scene_index = 0;
    driven.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
    driven.world_pose = openusd_physx_test::Pose(0.0F, 5.0F, 0.0F);
    driven.mass = 1.0F;
    driven.inertia = openusd_physx_vec3f{0.1F, 0.1F, 0.1F};
    driven.flags = OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_GRAVITY | OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_SLEEPING;
    driven.shape_offset = 0;
    driven.shape_count = 1;
    scene.driven_id = driven.id;
    builder.Actors().push_back(driven);

    openusd_physx_actor_desc twisted{};
    twisted.id = builder.AddIdentity("/Joints/TwistedBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    twisted.scene_index = 0;
    twisted.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
    twisted.world_pose = openusd_physx_test::Pose(5.0F, 5.0F, 0.0F);
    twisted.mass = 1.0F;
    twisted.inertia = openusd_physx_vec3f{0.1F, 0.1F, 0.1F};
    twisted.angular_velocity = openusd_physx_vec3f{6.0F, 0.0F, 0.0F};
    twisted.flags = OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_GRAVITY | OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_SLEEPING;
    twisted.shape_offset = 1;
    twisted.shape_count = 1;
    scene.twisted_id = twisted.id;
    builder.Actors().push_back(twisted);

    openusd_physx_actor_desc hanging{};
    hanging.id = builder.AddIdentity("/Joints/HangingBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    hanging.scene_index = 0;
    hanging.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
    hanging.world_pose = openusd_physx_test::Pose(10.0F, 5.0F, 0.0F);
    hanging.mass = 10.0F;
    hanging.flags = OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_SLEEPING;
    hanging.shape_offset = 2;
    hanging.shape_count = 1;
    scene.hanging_id = hanging.id;
    builder.Actors().push_back(hanging);

    // A six degree of freedom joint that leaves the X axis free and drives it
    // to two units. Every other axis is locked, so a body that reaches
    // (2, 5, 0) can only have got there through the per axis drive.
    openusd_physx_joint_desc drive_joint{};
    drive_joint.id = builder.AddIdentity("/Joints/DriveJoint", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    drive_joint.type = OPENUSD_PHYSX_JOINT_D6;
    drive_joint.actor0_index = -1;
    drive_joint.actor1_index = 0;
    drive_joint.local_frame0 = openusd_physx_test::Pose(0.0F, 5.0F, 0.0F);
    drive_joint.local_frame1 = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    for (uint32_t axis = 0; axis < OPENUSD_PHYSX_JOINT_AXIS_COUNT; ++axis)
    {
        drive_joint.motion[axis] = OPENUSD_PHYSX_JOINT_MOTION_LOCKED;
    }
    drive_joint.motion[OPENUSD_PHYSX_JOINT_AXIS_X] = OPENUSD_PHYSX_JOINT_MOTION_FREE;
    drive_joint.axis_drive_flags[OPENUSD_PHYSX_JOINT_AXIS_X] =
        OPENUSD_PHYSX_JOINT_DRIVE_FLAG_ENABLED | OPENUSD_PHYSX_JOINT_DRIVE_FLAG_ACCELERATION;
    drive_joint.axis_drive_stiffness[OPENUSD_PHYSX_JOINT_AXIS_X] = 400.0F;
    drive_joint.axis_drive_damping[OPENUSD_PHYSX_JOINT_AXIS_X] = 40.0F;
    drive_joint.axis_drive_target_position[OPENUSD_PHYSX_JOINT_AXIS_X] = 2.0F;
    builder.Joints().push_back(drive_joint);

    // A six degree of freedom joint that leaves only the twist free and limits
    // it. A body spun at six radians per second would turn six radians in a
    // second without the limit.
    openusd_physx_joint_desc twist_joint{};
    twist_joint.id = builder.AddIdentity("/Joints/TwistJoint", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    twist_joint.type = OPENUSD_PHYSX_JOINT_D6;
    twist_joint.actor0_index = -1;
    twist_joint.actor1_index = 1;
    twist_joint.local_frame0 = openusd_physx_test::Pose(5.0F, 5.0F, 0.0F);
    twist_joint.local_frame1 = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    for (uint32_t axis = 0; axis < OPENUSD_PHYSX_JOINT_AXIS_COUNT; ++axis)
    {
        twist_joint.motion[axis] = OPENUSD_PHYSX_JOINT_MOTION_LOCKED;
    }
    twist_joint.motion[OPENUSD_PHYSX_JOINT_AXIS_TWIST] = OPENUSD_PHYSX_JOINT_MOTION_LIMITED;
    twist_joint.axis_lower_limit[OPENUSD_PHYSX_JOINT_AXIS_TWIST] = -0.25F;
    twist_joint.axis_upper_limit[OPENUSD_PHYSX_JOINT_AXIS_TWIST] = 0.25F;
    twist_joint.limit_restitution = 0.0F;
    builder.Joints().push_back(twist_joint);

    // A fixed joint holding a ten kilogram body against gravity, which needs
    // about ninety eight newtons, with a break force of one newton.
    openusd_physx_joint_desc break_joint{};
    break_joint.id = builder.AddIdentity("/Joints/BreakJoint", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    break_joint.type = OPENUSD_PHYSX_JOINT_FIXED;
    break_joint.actor0_index = -1;
    break_joint.actor1_index = 2;
    break_joint.local_frame0 = openusd_physx_test::Pose(10.0F, 5.0F, 0.0F);
    break_joint.local_frame1 = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    break_joint.break_force = 1.0F;
    break_joint.break_torque = 1.0F;
    for (uint32_t axis = 0; axis < OPENUSD_PHYSX_JOINT_AXIS_COUNT; ++axis)
    {
        break_joint.motion[axis] = OPENUSD_PHYSX_JOINT_MOTION_LOCKED;
    }
    scene.breakable_joint_id = break_joint.id;
    builder.Joints().push_back(break_joint);

    PushCapacities(builder, 8, 256);
    scene.page = builder.Build();
    scene.page_size = builder.Size();
    return scene;
}

// ---------------------------------------------------------------------------
// Two fixed base articulation chains that differ only in whether their joints
// are driven, so the held pose can only come from the drive reaching PhysX.
// ---------------------------------------------------------------------------
struct ArticulationScene
{
    std::vector<uint64_t> page;
    size_t page_size = 0;
    uint64_t passive_root_id = 0;
    uint64_t passive_tip_id = 0;
    uint64_t driven_root_id = 0;
    uint64_t driven_tip_id = 0;
    uint64_t limited_tip_id = 0;
};

// Appends one three link chain and returns the identity of its root and tip.
// Each joint rotates about the world Z axis because the joint frames map the
// articulation twist axis, which is the frame X axis, onto Z.
void AppendChain(
    openusd_physx_test::PageBuilder& builder,
    const std::string& prefix,
    float base_x,
    bool driven,
    bool limited,
    uint64_t& root_id,
    uint64_t& tip_id)
{
    const uint32_t link_offset = static_cast<uint32_t>(builder.ArticulationLinks().size());
    const uint32_t shape_offset = static_cast<uint32_t>(builder.ActorShapes().size());
    for (uint32_t index = 0; index < 3; ++index)
    {
        builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{0, -1});
    }

    openusd_physx_articulation_desc articulation{};
    articulation.id = builder.AddIdentity(prefix, OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    articulation.scene_index = 0;
    articulation.flags = OPENUSD_PHYSX_ARTICULATION_FLAG_FIXED_BASE |
        OPENUSD_PHYSX_ARTICULATION_FLAG_DISABLE_SLEEPING;
    articulation.link_offset = link_offset;
    articulation.link_count = 3;
    articulation.position_iterations = 32;
    articulation.velocity_iterations = 8;
    builder.Articulations().push_back(articulation);

    const openusd_physx_quatf joint_frame = AxisRotation(kPi * 0.5F, 0.0F, 1.0F, 0.0F);
    uint64_t parent_id = 0;
    for (uint32_t index = 0; index < 3; ++index)
    {
        openusd_physx_articulation_link_desc link{};
        link.id = builder
                      .AddIdentity(
                          prefix + "/Link" + std::to_string(index),
                          OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM,
                          0)
                      .id;
        link.parent_id = parent_id;
        link.parent_frame = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
        link.child_frame = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
        link.world_pose = openusd_physx_test::Pose(base_x + static_cast<float>(index), 5.0F, 0.0F);
        link.mass = 1.0F;
        link.shape_offset = shape_offset + index;
        link.shape_count = 1;
        if (index == 0)
        {
            link.joint_type = OPENUSD_PHYSX_ARTICULATION_JOINT_NONE;
            root_id = link.id;
        }
        else
        {
            link.joint_type = OPENUSD_PHYSX_ARTICULATION_JOINT_REVOLUTE;
            link.parent_frame.position = openusd_physx_vec3f{0.5F, 0.0F, 0.0F};
            link.parent_frame.rotation = joint_frame;
            link.child_frame.position = openusd_physx_vec3f{-0.5F, 0.0F, 0.0F};
            link.child_frame.rotation = joint_frame;
            if (limited)
            {
                link.motion[OPENUSD_PHYSX_JOINT_AXIS_TWIST] = OPENUSD_PHYSX_JOINT_MOTION_LIMITED;
                link.lower_limit[OPENUSD_PHYSX_JOINT_AXIS_TWIST] = -0.1F;
                link.upper_limit[OPENUSD_PHYSX_JOINT_AXIS_TWIST] = 0.1F;
            }
            else
            {
                link.motion[OPENUSD_PHYSX_JOINT_AXIS_TWIST] = OPENUSD_PHYSX_JOINT_MOTION_FREE;
            }
            if (driven)
            {
                // An acceleration drive is inertia invariant, so the same gains
                // hold every link of the chain against gravity.
                link.flags |= OPENUSD_PHYSX_ARTICULATION_LINK_FLAG_DRIVE_ACCELERATION;
                link.drive_flags[OPENUSD_PHYSX_JOINT_AXIS_TWIST] = OPENUSD_PHYSX_JOINT_DRIVE_FLAG_ENABLED;
                link.drive_stiffness[OPENUSD_PHYSX_JOINT_AXIS_TWIST] = 10000.0F;
                link.drive_damping[OPENUSD_PHYSX_JOINT_AXIS_TWIST] = 1000.0F;
                link.drive_target_position[OPENUSD_PHYSX_JOINT_AXIS_TWIST] = 0.0F;
                link.armature[OPENUSD_PHYSX_JOINT_AXIS_TWIST] = 0.1F;
            }
        }
        parent_id = link.id;
        tip_id = link.id;
        builder.ArticulationLinks().push_back(link);
    }
}

ArticulationScene MakeArticulationScene()
{
    openusd_physx_test::PageBuilder builder;
    builder.Scenes().push_back(MakeScene(builder, "/Arti/PhysicsScene"));
    builder.Materials().push_back(MakeMaterial(builder, "/Arti/Material"));

    openusd_physx_shape_desc box{};
    box.id = builder.AddIdentity("/Arti/LinkShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    box.type = OPENUSD_PHYSX_SHAPE_BOX;
    box.local_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    box.scale = openusd_physx_vec3f{1.0F, 1.0F, 1.0F};
    box.half_extents = openusd_physx_vec3f{0.45F, 0.1F, 0.1F};
    box.material_index = 0;
    builder.Shapes().push_back(box);

    ArticulationScene scene;
    AppendChain(builder, "/Arti/Passive", 0.0F, false, false, scene.passive_root_id, scene.passive_tip_id);
    AppendChain(builder, "/Arti/Driven", 10.0F, true, false, scene.driven_root_id, scene.driven_tip_id);
    uint64_t limited_root = 0;
    AppendChain(builder, "/Arti/Limited", 20.0F, false, true, limited_root, scene.limited_tip_id);

    PushCapacities(builder, 16, 256);
    scene.page = builder.Build();
    scene.page_size = builder.Size();
    return scene;
}

// ---------------------------------------------------------------------------
// Character controllers on flat ground, against a wall, and on a thirty degree
// ramp, with two controllers that differ only in their slope limit.
// ---------------------------------------------------------------------------
struct ControllerScene
{
    std::vector<uint64_t> page;
    size_t page_size = 0;
    uint64_t walker_id = 0;
    uint64_t blocked_id = 0;
    uint64_t climber_id = 0;
    uint64_t stopped_id = 0;
    uint64_t box_id = 0;
};

ControllerScene MakeControllerScene()
{
    openusd_physx_test::PageBuilder builder;
    builder.Scenes().push_back(MakeScene(builder, "/Ctrl/PhysicsScene"));
    builder.Materials().push_back(MakeMaterial(builder, "/Ctrl/Material"));

    const openusd_physx_vec3f unit_scale{1.0F, 1.0F, 1.0F};

    openusd_physx_shape_desc ground{};
    ground.id = builder.AddIdentity("/Ctrl/GroundShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    ground.type = OPENUSD_PHYSX_SHAPE_BOX;
    ground.local_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    ground.scale = unit_scale;
    ground.half_extents = openusd_physx_vec3f{40.0F, 0.5F, 40.0F};
    ground.material_index = 0;
    builder.Shapes().push_back(ground);

    openusd_physx_shape_desc wall{};
    wall.id = builder.AddIdentity("/Ctrl/WallShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    wall.type = OPENUSD_PHYSX_SHAPE_BOX;
    wall.local_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    wall.scale = unit_scale;
    wall.half_extents = openusd_physx_vec3f{0.25F, 2.0F, 4.0F};
    wall.material_index = 0;
    builder.Shapes().push_back(wall);

    // A thirty degree ramp: rotating a slab about Z tilts its top face by the
    // same angle, and moving along positive X climbs it.
    openusd_physx_shape_desc ramp{};
    ramp.id = builder.AddIdentity("/Ctrl/RampShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    ramp.type = OPENUSD_PHYSX_SHAPE_BOX;
    ramp.local_pose.position = openusd_physx_vec3f{0.0F, 0.0F, 0.0F};
    ramp.local_pose.rotation = AxisRotation(kPi / 6.0F, 0.0F, 0.0F, 1.0F);
    ramp.scale = unit_scale;
    ramp.half_extents = openusd_physx_vec3f{4.0F, 0.25F, 3.0F};
    ramp.material_index = 0;
    builder.Shapes().push_back(ramp);

    // Both ramps share the one ramp shape, so the fourth reference points back
    // at it rather than at a shape that does not exist.
    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{0, -1});
    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{1, -1});
    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{2, -1});
    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{2, -1});

    ControllerScene scene;

    openusd_physx_actor_desc ground_body{};
    ground_body.id = builder.AddIdentity("/Ctrl/GroundBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    ground_body.scene_index = 0;
    ground_body.type = OPENUSD_PHYSX_ACTOR_STATIC;
    ground_body.world_pose = openusd_physx_test::Pose(0.0F, -0.5F, 0.0F);
    ground_body.shape_offset = 0;
    ground_body.shape_count = 1;
    builder.Actors().push_back(ground_body);

    openusd_physx_actor_desc wall_body{};
    wall_body.id = builder.AddIdentity("/Ctrl/WallBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    wall_body.scene_index = 0;
    wall_body.type = OPENUSD_PHYSX_ACTOR_STATIC;
    wall_body.world_pose = openusd_physx_test::Pose(11.5F, 2.0F, 0.0F);
    wall_body.shape_offset = 1;
    wall_body.shape_count = 1;
    builder.Actors().push_back(wall_body);

    openusd_physx_actor_desc ramp_a{};
    ramp_a.id = builder.AddIdentity("/Ctrl/RampA", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    ramp_a.scene_index = 0;
    ramp_a.type = OPENUSD_PHYSX_ACTOR_STATIC;
    ramp_a.world_pose = openusd_physx_test::Pose(22.0F, 1.0F, 0.0F);
    ramp_a.shape_offset = 2;
    ramp_a.shape_count = 1;
    builder.Actors().push_back(ramp_a);

    openusd_physx_actor_desc ramp_b{};
    ramp_b.id = builder.AddIdentity("/Ctrl/RampB", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    ramp_b.scene_index = 0;
    ramp_b.type = OPENUSD_PHYSX_ACTOR_STATIC;
    ramp_b.world_pose = openusd_physx_test::Pose(22.0F, 1.0F, 20.0F);
    ramp_b.shape_offset = 3;
    ramp_b.shape_count = 1;
    builder.Actors().push_back(ramp_b);

    const auto make_capsule = [&](const char* path, float x, float y, float z, float slope_limit) {
        openusd_physx_controller_desc controller{};
        controller.id = builder.AddIdentity(path, OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
        controller.scene_index = 0;
        controller.shape = OPENUSD_PHYSX_CONTROLLER_CAPSULE;
        controller.position = openusd_physx_vec3f{x, y, z};
        controller.radius = 0.3F;
        controller.height = 1.0F;
        controller.slope_limit = slope_limit;
        controller.step_offset = 0.2F;
        controller.contact_offset = 0.05F;
        controller.density = 10.0F;
        controller.climbing_mode = OPENUSD_PHYSX_CONTROLLER_CLIMBING_EASY;
        controller.non_walkable_mode = OPENUSD_PHYSX_CONTROLLER_PREVENT_CLIMBING;
        controller.material_index = 0;
        controller.flags =
            OPENUSD_PHYSX_CONTROLLER_FLAG_APPLY_GRAVITY | OPENUSD_PHYSX_CONTROLLER_FLAG_REPORT_HITS;
        return controller;
    };

    openusd_physx_controller_desc walker = make_capsule("/Ctrl/Walker", 0.0F, 3.0F, 0.0F, kPi / 4.0F);
    scene.walker_id = walker.id;
    builder.Controllers().push_back(walker);

    openusd_physx_controller_desc blocked = make_capsule("/Ctrl/Blocked", 10.0F, 3.0F, 0.0F, kPi / 4.0F);
    scene.blocked_id = blocked.id;
    builder.Controllers().push_back(blocked);

    // The two ramp walkers differ only in their slope limit: forty five degrees
    // admits the thirty degree ramp, fifteen degrees does not.
    openusd_physx_controller_desc climber = make_capsule("/Ctrl/Climber", 19.0F, 1.5F, 0.0F, kPi / 4.0F);
    scene.climber_id = climber.id;
    builder.Controllers().push_back(climber);

    openusd_physx_controller_desc stopped = make_capsule("/Ctrl/Stopped", 19.0F, 1.5F, 20.0F, kPi / 12.0F);
    scene.stopped_id = stopped.id;
    builder.Controllers().push_back(stopped);

    openusd_physx_controller_desc box_controller{};
    box_controller.id = builder.AddIdentity("/Ctrl/BoxWalker", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    box_controller.scene_index = 0;
    box_controller.shape = OPENUSD_PHYSX_CONTROLLER_BOX;
    box_controller.position = openusd_physx_vec3f{-10.0F, 3.0F, 0.0F};
    box_controller.half_extents = openusd_physx_vec3f{0.3F, 0.8F, 0.3F};
    box_controller.step_offset = 0.2F;
    box_controller.contact_offset = 0.05F;
    box_controller.density = 10.0F;
    box_controller.material_index = 0;
    box_controller.flags = OPENUSD_PHYSX_CONTROLLER_FLAG_APPLY_GRAVITY;
    scene.box_id = box_controller.id;
    builder.Controllers().push_back(box_controller);

    PushCapacities(builder, 16, 512);
    scene.page = builder.Build();
    scene.page_size = builder.Size();
    return scene;
}

int RunShapes(openusd_physx_world* world, openusd_physx_error_buffer* error)
{
    const ShapeScene scene = MakeShapeScene();
    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    if (!CheckStatus(
            openusd_physx_world_build(world, scene.page.data(), scene.page_size, &validation, error),
            OPENUSD_PHYSX_STATUS_OK,
            "the analytic shape page builds"))
    {
        std::cerr << "  reported: " << (error->data != nullptr ? error->data : "") << '\n';
        return 1;
    }
    Check(validation.actor_count == 6 && validation.dynamic_actor_count == 4, "the shape page declares six actors");

    openusd_physx_world_status_info status{};
    status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
    CheckStatus(openusd_physx_world_get_status(world, &status, error), OPENUSD_PHYSX_STATUS_OK, "shape page status");
    ResultStorage storage(status.capacities);
    openusd_physx_result_page results = storage.Page();

    if (!StepWorld(world, results, 240, error, "the analytic shape page"))
    {
        return 1;
    }

    const openusd_physx_body_state* cylinder = FindState(results, scene.cylinder_id);
    const openusd_physx_body_state* cone = FindState(results, scene.cone_id);
    const openusd_physx_body_state* capsule = FindState(results, scene.capsule_id);
    const openusd_physx_body_state* sphere = FindState(results, scene.heightfield_sphere_id);
    if (!Check(cylinder != nullptr && cone != nullptr && capsule != nullptr && sphere != nullptr,
            "every dynamic shape reports a body state"))
    {
        return 1;
    }

    // An upright cylinder of unit height rests with its centre half a unit
    // above the ground. A shape that never collided would be far below zero
    // after four seconds of free fall.
    CheckNear(cylinder->pose.position.y, 0.5F, 0.15F, "an upright cylinder rests on the ground");
    CheckNear(cone->pose.position.y, 0.5F, 0.25F, "an upright cone rests on the ground");
    // A PhysX capsule is stated about its local X axis, so an unrotated
    // capsule lies on its side and rests at exactly its radius.
    CheckNear(capsule->pose.position.y, 0.3F, 0.1F, "a capsule rests on the ground at its radius");

    // The height field surface sits one unit up because the raw sample of one
    // hundred is multiplied by the authored height scale of one hundredth.
    CheckNear(sphere->pose.position.y, 1.5F, 0.15F, "a sphere rests on the scaled height field surface");
    return 0;
}

int RunTuning(openusd_physx_world* world, openusd_physx_error_buffer* error)
{
    const TuningScene scene = MakeTuningScene();
    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    if (!CheckStatus(
            openusd_physx_world_build(world, scene.page.data(), scene.page_size, &validation, error),
            OPENUSD_PHYSX_STATUS_OK,
            "the rigid body tuning page builds"))
    {
        std::cerr << "  reported: " << (error->data != nullptr ? error->data : "") << '\n';
        return 1;
    }

    openusd_physx_world_status_info status{};
    status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
    CheckStatus(openusd_physx_world_get_status(world, &status, error), OPENUSD_PHYSX_STATUS_OK, "tuning page status");
    ResultStorage storage(status.capacities);
    openusd_physx_result_page results = storage.Page();

    if (!StepWorld(world, results, 60, error, "the rigid body tuning page"))
    {
        return 1;
    }

    const openusd_physx_body_state* locked = FindState(results, scene.locked_id);
    const openusd_physx_body_state* clamped = FindState(results, scene.clamped_id);
    const openusd_physx_body_state* free_body = FindState(results, scene.free_id);
    if (!Check(locked != nullptr && clamped != nullptr && free_body != nullptr,
            "every tuned body reports a body state"))
    {
        return 1;
    }

    // The locked body was launched at six and minus four units per second, so
    // without the locks it would have travelled six and four units.
    CheckNear(locked->pose.position.x, 0.0F, 1e-3F, "a linear X lock holds the authored X position");
    CheckNear(locked->pose.position.z, 0.0F, 1e-3F, "a linear Z lock holds the authored Z position");
    // One second of semi implicit integration at sixty hertz drops a body just
    // under five units, and the horizontal locks must not touch that.
    CheckNear(locked->pose.position.y, 15.0F, 0.5F, "a body with only horizontal locks still falls");

    const float clamped_speed = std::fabs(clamped->linear_velocity.y);
    const float free_speed = std::fabs(free_body->linear_velocity.y);
    Check(clamped_speed <= 1.05F, "an authored maximum linear velocity clamps the fall speed");
    Check(free_speed > 9.0F, "a body without a clamp reaches the full free fall speed");
    Check(
        clamped->pose.position.y > free_body->pose.position.y + 3.0F,
        "the clamped body stays well above the unclamped body");
    return 0;
}

int RunJoints(openusd_physx_world* world, openusd_physx_error_buffer* error)
{
    const JointScene scene = MakeJointScene();
    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    if (!CheckStatus(
            openusd_physx_world_build(world, scene.page.data(), scene.page_size, &validation, error),
            OPENUSD_PHYSX_STATUS_OK,
            "the joint page builds"))
    {
        std::cerr << "  reported: " << (error->data != nullptr ? error->data : "") << '\n';
        return 1;
    }
    Check(validation.joint_count == 3, "the joint page declares three joints");

    openusd_physx_world_status_info status{};
    status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
    CheckStatus(openusd_physx_world_get_status(world, &status, error), OPENUSD_PHYSX_STATUS_OK, "joint page status");
    ResultStorage storage(status.capacities);
    openusd_physx_result_page results = storage.Page();

    openusd_physx_step_desc step_desc{};
    step_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_step_desc));
    step_desc.fixed_time_step = 1.0 / 60.0;
    step_desc.substep_count = 1;

    bool saw_break = false;
    float max_twist = 0.0F;
    for (uint32_t index = 0; index < 180; ++index)
    {
        if (!CheckStatus(
                openusd_physx_world_step(world, &step_desc, &results, error),
                OPENUSD_PHYSX_STATUS_OK,
                "the joint page steps"))
        {
            return 1;
        }
        for (uint32_t event = 0; event < results.header.event_count; ++event)
        {
            if (results.events[event].type == OPENUSD_PHYSX_EVENT_JOINT_BREAK &&
                results.events[event].id0 == scene.breakable_joint_id)
            {
                saw_break = true;
            }
        }
        const openusd_physx_body_state* twisted = FindState(results, scene.twisted_id);
        if (twisted != nullptr)
        {
            max_twist = std::max(max_twist, std::fabs(TwistAngle(twisted->pose.rotation)));
        }
    }

    const openusd_physx_body_state* driven = FindState(results, scene.driven_id);
    const openusd_physx_body_state* hanging = FindState(results, scene.hanging_id);
    if (!Check(driven != nullptr && hanging != nullptr, "every jointed body reports a body state"))
    {
        return 1;
    }

    // The drive target is two units along the joint X axis, and every other
    // axis is locked, so the position proves both the per axis motion and the
    // per axis drive reached PhysX.
    CheckNear(driven->pose.position.x, 2.0F, 0.15F, "a six degree of freedom drive reaches its target position");
    CheckNear(driven->pose.position.y, 5.0F, 0.05F, "a locked axis holds the authored position");
    CheckNear(driven->pose.position.z, 0.0F, 0.05F, "a locked axis holds the authored position on Z");

    // Three seconds at six radians per second is over seventeen radians, so a
    // quarter radian bound can only come from the authored twist limit.
    Check(max_twist < 0.45F, "an authored twist limit bounds the rotation");
    Check(max_twist > 0.0F, "the limited twist axis still moves");

    Check(saw_break, "a joint whose break force is exceeded reports a break event");
    Check(hanging->pose.position.y < 4.0F, "the body under a broken joint falls away");
    return 0;
}

int RunArticulations(openusd_physx_world* world, openusd_physx_error_buffer* error)
{
    const ArticulationScene scene = MakeArticulationScene();
    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    if (!CheckStatus(
            openusd_physx_world_build(world, scene.page.data(), scene.page_size, &validation, error),
            OPENUSD_PHYSX_STATUS_OK,
            "the articulation page builds"))
    {
        std::cerr << "  reported: " << (error->data != nullptr ? error->data : "") << '\n';
        return 1;
    }

    openusd_physx_world_status_info status{};
    status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
    CheckStatus(
        openusd_physx_world_get_status(world, &status, error),
        OPENUSD_PHYSX_STATUS_OK,
        "articulation page status");
    Check(status.articulation_count == 3, "the world reports three articulations");
    Check(status.articulation_link_count == 9, "the world reports nine articulation links");
    Check(status.dynamic_actor_count == 9, "every articulation link publishes a body state");

    ResultStorage storage(status.capacities);
    openusd_physx_result_page results = storage.Page();
    if (!StepWorld(world, results, 180, error, "the articulation page"))
    {
        return 1;
    }

    const openusd_physx_body_state* passive_root = FindState(results, scene.passive_root_id);
    const openusd_physx_body_state* passive_tip = FindState(results, scene.passive_tip_id);
    const openusd_physx_body_state* driven_tip = FindState(results, scene.driven_tip_id);
    const openusd_physx_body_state* limited_tip = FindState(results, scene.limited_tip_id);
    if (!Check(
            passive_root != nullptr && passive_tip != nullptr && driven_tip != nullptr &&
                limited_tip != nullptr,
            "every articulation link reports a body state"))
    {
        return 1;
    }
    Check(
        (passive_root->flags & OPENUSD_PHYSX_BODY_STATE_FLAG_ARTICULATION_LINK) != 0,
        "an articulation link publishes the articulation link flag");

    // A fixed base root can never translate, whatever the chain below it does.
    CheckNear(passive_root->pose.position.x, 0.0F, 1e-3F, "a fixed articulation base holds its X position");
    CheckNear(passive_root->pose.position.y, 5.0F, 1e-3F, "a fixed articulation base holds its Y position");

    // The free chain folds under gravity; the driven chain is held level by its
    // joint drives; the limited chain may only fall as far as its limits allow.
    Check(passive_tip->pose.position.y < 4.0F, "a free revolute chain falls under gravity");
    CheckNear(driven_tip->pose.position.y, 5.0F, 0.25F, "a driven revolute chain holds its target pose");
    CheckNear(driven_tip->pose.position.x, 12.0F, 0.25F, "a driven revolute chain stays extended");
    Check(
        limited_tip->pose.position.y > passive_tip->pose.position.y + 0.5F,
        "an articulation joint limit bounds how far the chain folds");
    Check(limited_tip->pose.position.y < 5.0F, "a limited articulation joint still moves");
    return 0;
}

int RunControllers(openusd_physx_world* world, openusd_physx_error_buffer* error)
{
    const ControllerScene scene = MakeControllerScene();
    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    if (!CheckStatus(
            openusd_physx_world_build(world, scene.page.data(), scene.page_size, &validation, error),
            OPENUSD_PHYSX_STATUS_OK,
            "the character controller page builds"))
    {
        std::cerr << "  reported: " << (error->data != nullptr ? error->data : "") << '\n';
        return 1;
    }

    openusd_physx_world_status_info status{};
    status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
    CheckStatus(
        openusd_physx_world_get_status(world, &status, error),
        OPENUSD_PHYSX_STATUS_OK,
        "controller page status");
    Check(status.controller_count == 5, "the world reports five character controllers");

    ResultStorage storage(status.capacities);
    openusd_physx_result_page results = storage.Page();

    // Settle everything onto its ground before any movement is commanded.
    if (!StepWorld(world, results, 120, error, "the character controller page"))
    {
        return 1;
    }

    const openusd_physx_body_state* settled = FindState(results, scene.walker_id);
    const openusd_physx_body_state* settled_box = FindState(results, scene.box_id);
    if (!Check(settled != nullptr && settled_box != nullptr, "every controller reports a body state"))
    {
        return 1;
    }
    Check(
        (settled->flags & OPENUSD_PHYSX_BODY_STATE_FLAG_CONTROLLER) != 0,
        "a controller publishes the controller flag");
    // The capsule centre rests half its height plus its radius plus the contact
    // offset above the ground, and it started three units up, so a controller
    // that never collided would be far below zero.
    CheckNear(settled->pose.position.y, 0.85F, 0.15F, "a capsule controller lands on the ground");
    CheckNear(settled_box->pose.position.y, 0.85F, 0.15F, "a box controller lands on the ground");

    const float start_walker_x = settled->pose.position.x;
    const openusd_physx_body_state* climber_start = FindState(results, scene.climber_id);
    const openusd_physx_body_state* stopped_start = FindState(results, scene.stopped_id);
    if (!Check(climber_start != nullptr && stopped_start != nullptr, "both ramp controllers report a state"))
    {
        return 1;
    }
    const float climber_start_y = climber_start->pose.position.y;
    const float stopped_start_y = stopped_start->pose.position.y;

    openusd_physx_step_desc step_desc{};
    step_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_step_desc));
    step_desc.fixed_time_step = 1.0 / 60.0;
    step_desc.substep_count = 1;

    openusd_physx_command commands[4]{};
    const uint64_t moving[4] = {scene.walker_id, scene.blocked_id, scene.climber_id, scene.stopped_id};
    for (uint32_t index = 0; index < 4; ++index)
    {
        commands[index].target_id = moving[index];
        commands[index].type = OPENUSD_PHYSX_COMMAND_MOVE_CONTROLLER;
        commands[index].vector = openusd_physx_vec3f{0.02F, 0.0F, 0.0F};
    }
    step_desc.commands = commands;
    step_desc.command_count = 4;

    bool saw_hit = false;
    for (uint32_t index = 0; index < 150; ++index)
    {
        if (!CheckStatus(
                openusd_physx_world_step(world, &step_desc, &results, error),
                OPENUSD_PHYSX_STATUS_OK,
                "the character controller page steps"))
        {
            std::cerr << "  reported: " << (error->data != nullptr ? error->data : "") << '\n';
            return 1;
        }
        for (uint32_t event = 0; event < results.header.event_count; ++event)
        {
            if (results.events[event].type == OPENUSD_PHYSX_EVENT_CONTROLLER_HIT &&
                results.events[event].id0 == scene.blocked_id)
            {
                saw_hit = true;
            }
        }
    }

    const openusd_physx_body_state* walker = FindState(results, scene.walker_id);
    const openusd_physx_body_state* blocked = FindState(results, scene.blocked_id);
    const openusd_physx_body_state* climber = FindState(results, scene.climber_id);
    const openusd_physx_body_state* stopped = FindState(results, scene.stopped_id);
    if (!Check(
            walker != nullptr && blocked != nullptr && climber != nullptr && stopped != nullptr,
            "every commanded controller reports a body state"))
    {
        return 1;
    }

    // One hundred and fifty commands of two hundredths of a unit each move an
    // unobstructed controller three units.
    CheckNear(walker->pose.position.x - start_walker_x, 3.0F, 0.2F, "a move command displaces a controller");
    Check(blocked->pose.position.x < 11.0F, "a wall stops a commanded controller");
    Check(blocked->pose.position.x > 10.5F, "a blocked controller still reaches the wall");
    Check(saw_hit, "a controller that touches a shape reports a controller hit event");

    // The two ramp controllers were commanded identically and differ only in
    // their slope limit, so any difference in the height gained is the limit.
    const float climbed = climber->pose.position.y - climber_start_y;
    const float refused = stopped->pose.position.y - stopped_start_y;
    Check(climbed > 0.3F, "a controller whose slope limit admits the ramp climbs it");
    Check(refused < climbed - 0.25F, "a controller whose slope limit refuses the ramp climbs far less");
    return 0;
}
}

// ---------------------------------------------------------------------------
// Four single axis joints that each author a drive. PhysX gives a revolute
// joint only a velocity motor and gives a prismatic or a spherical joint no
// motor at all, so these scenes are the proof that the authored stiffness,
// damping, target position, target velocity and acceleration flag all reach
// the solver.
// ---------------------------------------------------------------------------
struct SingleAxisDriveScene
{
    std::vector<uint64_t> page;
    size_t page_size = 0;
    uint64_t revolute_id = 0;
    uint64_t prismatic_id = 0;
    uint64_t spherical_id = 0;
    uint64_t spun_id = 0;
    uint64_t twist_id = 0;
};

SingleAxisDriveScene MakeSingleAxisDriveScene()
{
    openusd_physx_test::PageBuilder builder;
    builder.Scenes().push_back(MakeScene(builder, "/Drives/PhysicsScene"));
    builder.Materials().push_back(MakeMaterial(builder, "/Drives/Material"));

    openusd_physx_shape_desc box{};
    box.id = builder.AddIdentity("/Drives/BoxShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    box.type = OPENUSD_PHYSX_SHAPE_BOX;
    box.local_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    box.scale = openusd_physx_vec3f{1.0F, 1.0F, 1.0F};
    box.half_extents = openusd_physx_vec3f{0.1F, 0.1F, 0.1F};
    box.material_index = 0;
    builder.Shapes().push_back(box);

    SingleAxisDriveScene scene;
    const char* names[5] = {
        "/Drives/Revolute", "/Drives/Prismatic", "/Drives/Spherical", "/Drives/Spun", "/Drives/Twist"};
    uint64_t* ids[5] = {
        &scene.revolute_id, &scene.prismatic_id, &scene.spherical_id, &scene.spun_id, &scene.twist_id};
    const float anchors[5] = {0.0F, 10.0F, 20.0F, 30.0F, 40.0F};
    for (uint32_t index = 0; index < 5; ++index)
    {
        builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{0, -1});

        openusd_physx_actor_desc body{};
        body.id = builder.AddIdentity(names[index], OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
        body.scene_index = 0;
        body.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
        body.world_pose = openusd_physx_test::Pose(anchors[index] + 1.0F, 5.0F, 0.0F);
        body.mass = 1.0F;
        body.inertia = openusd_physx_vec3f{0.05F, 0.05F, 0.05F};
        body.flags = OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_SLEEPING;
        if (index == 2)
        {
            // A spherical joint keeps all three angular degrees of freedom, so
            // a drive authored on one swing axis cannot hold an arm that
            // gravity is free to rotate about the untouched twist axis. This
            // body therefore carries no weight: any travel it makes is the
            // drive and nothing else.
            body.flags |= OPENUSD_PHYSX_ACTOR_FLAG_DISABLE_GRAVITY;
        }
        body.shape_offset = index;
        body.shape_count = 1;
        *ids[index] = body.id;
        builder.Actors().push_back(body);
    }

    // A revolute drive that asks for a quarter turn about the world Z axis.
    // Gravity pulls the arm down, so only a position drive can stand it up.
    openusd_physx_joint_desc revolute{};
    revolute.id = builder.AddIdentity("/Drives/RevoluteJoint", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    revolute.type = OPENUSD_PHYSX_JOINT_REVOLUTE;
    revolute.axis = OPENUSD_PHYSX_AXIS_Z;
    revolute.flags = OPENUSD_PHYSX_JOINT_FLAG_DRIVE_ENABLED;
    revolute.actor0_index = -1;
    revolute.actor1_index = 0;
    revolute.local_frame0 = openusd_physx_test::Pose(anchors[0], 5.0F, 0.0F);
    revolute.local_frame1 = openusd_physx_test::Pose(-1.0F, 0.0F, 0.0F);
    revolute.drive_stiffness = 4000.0F;
    revolute.drive_damping = 400.0F;
    revolute.drive_target_position = kPi * 0.5F;
    builder.Joints().push_back(revolute);

    // A prismatic drive along the world Y axis that must hold the body at the
    // anchor height instead of letting it sink down the slide.
    openusd_physx_joint_desc prismatic{};
    prismatic.id = builder.AddIdentity("/Drives/PrismaticJoint", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    prismatic.type = OPENUSD_PHYSX_JOINT_PRISMATIC;
    prismatic.axis = OPENUSD_PHYSX_AXIS_Y;
    prismatic.flags = OPENUSD_PHYSX_JOINT_FLAG_DRIVE_ENABLED;
    prismatic.actor0_index = -1;
    prismatic.actor1_index = 1;
    prismatic.local_frame0 = openusd_physx_test::Pose(anchors[1] + 1.0F, 5.0F, 0.0F);
    prismatic.local_frame1 = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    prismatic.drive_stiffness = 6000.0F;
    prismatic.drive_damping = 600.0F;
    prismatic.drive_target_position = 0.0F;
    builder.Joints().push_back(prismatic);

    // A spherical drive that swings the arm about the world up axis, which
    // moves it in Z. The body carries no weight, so any Z travel is the drive.
    openusd_physx_joint_desc spherical{};
    spherical.id = builder.AddIdentity("/Drives/SphericalJoint", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    spherical.type = OPENUSD_PHYSX_JOINT_SPHERICAL;
    spherical.flags = OPENUSD_PHYSX_JOINT_FLAG_DRIVE_ENABLED;
    spherical.actor0_index = -1;
    spherical.actor1_index = 2;
    spherical.local_frame0 = openusd_physx_test::Pose(anchors[2], 5.0F, 0.0F);
    spherical.local_frame1 = openusd_physx_test::Pose(-1.0F, 0.0F, 0.0F);
    spherical.drive_stiffness = 4000.0F;
    spherical.drive_damping = 400.0F;
    spherical.drive_target_position = kPi * 0.25F;
    builder.Joints().push_back(spherical);

    // A revolute acceleration drive with a velocity target. An acceleration
    // drive must reach the solver as a drive: turning it into a free spinning
    // axis would leave the arm hanging instead of turning.
    openusd_physx_joint_desc spun{};
    spun.id = builder.AddIdentity("/Drives/SpunJoint", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    spun.type = OPENUSD_PHYSX_JOINT_REVOLUTE;
    spun.axis = OPENUSD_PHYSX_AXIS_Z;
    spun.flags = OPENUSD_PHYSX_JOINT_FLAG_DRIVE_ENABLED | OPENUSD_PHYSX_JOINT_FLAG_DRIVE_ACCELERATION;
    spun.actor0_index = -1;
    spun.actor1_index = 3;
    spun.local_frame0 = openusd_physx_test::Pose(anchors[3], 5.0F, 0.0F);
    spun.local_frame1 = openusd_physx_test::Pose(-1.0F, 0.0F, 0.0F);
    spun.drive_damping = 2000.0F;
    spun.drive_target_velocity = kPi;
    builder.Joints().push_back(spun);

    // A second spherical drive, this one asking for its authored rest angle so
    // the arm hangs still, used to prove that a driven spherical joint keeps all
    // three angular degrees of freedom. The probe twists it about the joint
    // twist axis, which a joint that locked the twist to build the drive could
    // never allow.
    openusd_physx_joint_desc twist{};
    twist.id = builder.AddIdentity("/Drives/TwistJoint", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    twist.type = OPENUSD_PHYSX_JOINT_SPHERICAL;
    twist.flags = OPENUSD_PHYSX_JOINT_FLAG_DRIVE_ENABLED;
    twist.actor0_index = -1;
    twist.actor1_index = 4;
    twist.local_frame0 = openusd_physx_test::Pose(anchors[4], 5.0F, 0.0F);
    twist.local_frame1 = openusd_physx_test::Pose(-1.0F, 0.0F, 0.0F);
    twist.drive_stiffness = 4000.0F;
    twist.drive_damping = 400.0F;
    twist.drive_target_position = 0.0F;
    builder.Joints().push_back(twist);

    PushCapacities(builder, 8, 256);
    scene.page = builder.Build();
    scene.page_size = builder.Size();
    return scene;
}

int RunSingleAxisDrives(openusd_physx_world* world, openusd_physx_error_buffer* error)
{
    const SingleAxisDriveScene scene = MakeSingleAxisDriveScene();
    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    if (!CheckStatus(
            openusd_physx_world_build(world, scene.page.data(), scene.page_size, &validation, error),
            OPENUSD_PHYSX_STATUS_OK,
            "the single axis drive page builds"))
    {
        std::cerr << "  reported: " << (error->data != nullptr ? error->data : "") << '\n';
        return 1;
    }
    Check(validation.joint_count == 5, "the single axis drive page declares five joints");

    openusd_physx_world_status_info status{};
    status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
    CheckStatus(
        openusd_physx_world_get_status(world, &status, error),
        OPENUSD_PHYSX_STATUS_OK,
        "single axis drive page status");
    ResultStorage storage(status.capacities);
    openusd_physx_result_page results = storage.Page();

    openusd_physx_step_desc step_desc{};
    step_desc.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_step_desc));
    step_desc.fixed_time_step = 1.0 / 60.0;
    step_desc.substep_count = 1;

    // The twist body is torqued about the joint twist axis on every step. A
    // driven spherical joint that kept its twist locked would swallow the whole
    // torque, so the measured spin is the proof that the twist is still free.
    openusd_physx_command twist_torque{};
    twist_torque.type = OPENUSD_PHYSX_COMMAND_ADD_TORQUE;
    twist_torque.target_id = scene.twist_id;
    twist_torque.vector = openusd_physx_vec3f{0.1F, 0.0F, 0.0F};
    step_desc.commands = &twist_torque;
    step_desc.command_count = 1;

    float spun_travel = 0.0F;
    float spun_x = 31.0F;
    float spun_y = 5.0F;
    for (uint32_t index = 0; index < 240; ++index)
    {
        if (!CheckStatus(
                openusd_physx_world_step(world, &step_desc, &results, error),
                OPENUSD_PHYSX_STATUS_OK,
                "the single axis drive page steps"))
        {
            return 1;
        }
        const openusd_physx_body_state* spun = FindState(results, scene.spun_id);
        if (spun != nullptr)
        {
            const float dx = spun->pose.position.x - spun_x;
            const float dy = spun->pose.position.y - spun_y;
            spun_travel += std::sqrt((dx * dx) + (dy * dy));
            spun_x = spun->pose.position.x;
            spun_y = spun->pose.position.y;
        }
    }

    const openusd_physx_body_state* revolute = FindState(results, scene.revolute_id);
    const openusd_physx_body_state* prismatic = FindState(results, scene.prismatic_id);
    const openusd_physx_body_state* spherical = FindState(results, scene.spherical_id);
    if (!Check(
            revolute != nullptr && prismatic != nullptr && spherical != nullptr,
            "every driven body reports a body state"))
    {
        return 1;
    }

    // A quarter turn stands the revolute arm on end over its anchor, which a
    // velocity only motor could never hold.
    Check(std::fabs(revolute->pose.position.x - 0.0F) < 0.35F, "a revolute position drive turns the arm off the horizontal");
    Check(std::fabs(revolute->pose.position.y - 5.0F) > 0.7F, "a revolute position drive reaches its target angle");

    // The prismatic body is held at the anchor height rather than sliding away
    // under its own weight, which needs the authored stiffness.
    CheckNear(prismatic->pose.position.y, 5.0F, 0.15F, "a prismatic drive holds its body against gravity");
    CheckNear(prismatic->pose.position.x, 11.0F, 0.05F, "a prismatic drive leaves the locked axes alone");

    // Only a swing drive moves the arm out of the plane it starts in. The
    // spherical body carries no weight, so the swing is the drive alone; the
    // twist axis stays free, which the dedicated twist body below proves.
    if (!Check(
            std::fabs(spherical->pose.position.z) > 0.3F,
            "a spherical drive swings its body out of the gravity plane"))
    {
        std::cerr << "  spherical arm at (" << spherical->pose.position.x << ", " << spherical->pose.position.y
                  << ", " << spherical->pose.position.z << ")\n";
    }

    // A spherical joint has three angular degrees of freedom. The drive is
    // authored on the swing axis, so the twist must still turn freely under an
    // applied torque, and the arm must still hang at its authored rest angle.
    const openusd_physx_body_state* twisted = FindState(results, scene.twist_id);
    if (!Check(twisted != nullptr, "the twisted body reports a body state"))
    {
        return 1;
    }
    Check(
        std::fabs(twisted->angular_velocity.x) > 1.0F,
        "a driven spherical joint leaves its twist axis free");
    CheckNear(twisted->pose.position.x, 41.0F, 0.25F, "a driven spherical joint still holds its swing");

    // Half a turn a second for four seconds is many circles of arc length,
    // which neither a hanging nor a free spinning arm ever covers.
    Check(spun_travel > 6.0F, "a revolute acceleration drive spins its arm instead of free wheeling");
    return 0;
}

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
    Check(
        (capabilities.flags & OPENUSD_PHYSX_CAPABILITY_CONVEX_CORE_SHAPES) != 0,
        "the library reports analytic cylinder and cone support");
    Check(
        (capabilities.flags & OPENUSD_PHYSX_CAPABILITY_HEIGHTFIELD_SHAPES) != 0,
        "the library reports height field support");
    Check(
        (capabilities.flags & OPENUSD_PHYSX_CAPABILITY_D6_JOINT_DRIVES) != 0,
        "the library reports six degree of freedom joint drives");
    Check(
        (capabilities.flags & OPENUSD_PHYSX_CAPABILITY_SHAPE_OFFSETS) != 0,
        "the library reports per shape offsets");
    Check(
        (capabilities.flags & OPENUSD_PHYSX_CAPABILITY_RIGID_BODY_TUNING) != 0,
        "the library reports per body tuning");
    Check(
        (capabilities.flags & OPENUSD_PHYSX_CAPABILITY_ARTICULATIONS) != 0,
        "the library reports reduced coordinate articulations");
    Check(
        (capabilities.flags & OPENUSD_PHYSX_CAPABILITY_CHARACTER_CONTROLLERS) != 0,
        "the library reports character controllers");

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

    int fatal = RunShapes(world, &error);
    if (fatal == 0)
    {
        fatal = RunTuning(world, &error);
    }
    if (fatal == 0)
    {
        fatal = RunJoints(world, &error);
    }
    if (fatal == 0)
    {
        fatal = RunSingleAxisDrives(world, &error);
    }
    if (fatal == 0)
    {
        fatal = RunArticulations(world, &error);
    }
    if (fatal == 0)
    {
        fatal = RunControllers(world, &error);
    }

    openusd_physx_world_release(world);

    if (fatal != 0 || g_failures != 0)
    {
        std::cerr << g_failures << " CPU domain check(s) failed.\n";
        return 1;
    }
    std::cout << "cpu domain probe passed\n";
    return 0;
}
