// Copyright (c) marcschier. Licensed under the MIT License.

// Probe for the articulation coupling constructs the version 6 ABI adds: fixed
// tendons, spatial tendons, and mimic joints. Every check compares a coupled
// chain against an identical uncoupled control chain in the same world, so a
// check can only pass when the coupling actually reached PhysX and changed the
// motion rather than merely being accepted by the page validator.

#include "openusd_physx_world.h"
#include "page_builder.h"

#include <cmath>
#include <cstdio>
#include <iostream>
#include <string>
#include <vector>

#if defined(_WIN32)
#include <windows.h>

#include <psapi.h>
#endif

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

const float kPi = 3.14159265358979323846F;

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
    scene.position_iterations = 16;
    scene.velocity_iterations = 4;
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

// ---------------------------------------------------------------------------
// Three identical two joint chains hanging from a fixed base, differing only in
// the coupling construct applied to them. Every chain is free to swing about the
// world Z axis under gravity, so any difference in the tip pose after the same
// number of steps can only come from the coupling.
// ---------------------------------------------------------------------------
struct CouplingScene
{
    std::vector<uint64_t> page;
    size_t page_size = 0;
    uint64_t control_tip_id = 0;
    uint64_t fixed_tendon_tip_id = 0;
    uint64_t fixed_tendon_middle_id = 0;
    uint64_t spatial_tendon_tip_id = 0;
    uint64_t mimic_middle_id = 0;
    uint64_t mimic_tip_id = 0;
    uint32_t control_index = 0;
    uint32_t fixed_index = 0;
    uint32_t spatial_index = 0;
    uint32_t mimic_index = 0;
};

// Appends one three link chain, returns its articulation index and the identity
// of its middle link and its tip.
uint32_t AppendChain(
    openusd_physx_test::PageBuilder& builder,
    const std::string& prefix,
    float base_x,
    uint64_t& middle_id,
    uint64_t& tip_id,
    uint32_t link_count = 3)
{
    const uint32_t link_offset = static_cast<uint32_t>(builder.ArticulationLinks().size());
    const uint32_t shape_offset = static_cast<uint32_t>(builder.ActorShapes().size());
    for (uint32_t index = 0; index < link_count; ++index)
    {
        builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{0, -1});
    }

    const uint32_t articulation_index = static_cast<uint32_t>(builder.Articulations().size());
    openusd_physx_articulation_desc articulation{};
    articulation.id = builder.AddIdentity(prefix, OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    articulation.scene_index = 0;
    articulation.flags = OPENUSD_PHYSX_ARTICULATION_FLAG_FIXED_BASE |
        OPENUSD_PHYSX_ARTICULATION_FLAG_DISABLE_SLEEPING;
    articulation.link_offset = link_offset;
    articulation.link_count = link_count;
    articulation.position_iterations = 32;
    articulation.velocity_iterations = 8;
    builder.Articulations().push_back(articulation);

    // The articulation twist axis is the joint frame X axis, so rotating the
    // frame a quarter turn about Y makes every joint swing about world Z.
    const openusd_physx_quatf joint_frame = AxisRotation(kPi * 0.5F, 0.0F, 1.0F, 0.0F);
    uint64_t parent_id = 0;
    for (uint32_t index = 0; index < link_count; ++index)
    {
        openusd_physx_articulation_link_desc link{};
        link.id = builder
                      .AddIdentity(prefix + "/Link" + std::to_string(index), OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0)
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
        }
        else
        {
            link.joint_type = OPENUSD_PHYSX_ARTICULATION_JOINT_REVOLUTE;
            link.parent_frame.position = openusd_physx_vec3f{0.5F, 0.0F, 0.0F};
            link.parent_frame.rotation = joint_frame;
            link.child_frame.position = openusd_physx_vec3f{-0.5F, 0.0F, 0.0F};
            link.child_frame.rotation = joint_frame;
            link.motion[OPENUSD_PHYSX_JOINT_AXIS_TWIST] = OPENUSD_PHYSX_JOINT_MOTION_FREE;
            middle_id = index == 1 ? link.id : middle_id;
        }
        parent_id = link.id;
        tip_id = link.id;
        builder.ArticulationLinks().push_back(link);
    }
    return articulation_index;
}

CouplingScene MakeCouplingScene()
{
    openusd_physx_test::PageBuilder builder;
    builder.Scenes().push_back(MakeScene(builder, "/Coupling/PhysicsScene"));
    builder.Materials().push_back(MakeMaterial(builder, "/Coupling/Material"));

    openusd_physx_shape_desc box{};
    box.id = builder.AddIdentity("/Coupling/LinkShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    box.type = OPENUSD_PHYSX_SHAPE_BOX;
    box.local_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    box.scale = openusd_physx_vec3f{1.0F, 1.0F, 1.0F};
    box.half_extents = openusd_physx_vec3f{0.45F, 0.1F, 0.1F};
    box.material_index = 0;
    builder.Shapes().push_back(box);

    CouplingScene scene;
    uint64_t unused = 0;
    scene.control_index = AppendChain(builder, "/Coupling/Control", 0.0F, unused, scene.control_tip_id);
    scene.fixed_index = AppendChain(
        builder, "/Coupling/Fixed", 10.0F, scene.fixed_tendon_middle_id, scene.fixed_tendon_tip_id);
    scene.spatial_index = AppendChain(builder, "/Coupling/Spatial", 20.0F, unused, scene.spatial_tendon_tip_id);
    scene.mimic_index = AppendChain(builder, "/Coupling/Mimic", 30.0F, scene.mimic_middle_id, scene.mimic_tip_id);

    // A fixed tendon spanning both joints of the second chain. A stiff tendon
    // with a zero rest length pulls the summed joint angles back to zero, so the
    // chain hangs far closer to its authored pose than the free control chain.
    {
        openusd_physx_tendon_desc tendon{};
        tendon.id = builder.AddIdentity("/Coupling/Fixed/Tendon", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
        tendon.articulation_index = scene.fixed_index;
        tendon.type = OPENUSD_PHYSX_TENDON_FIXED;
        tendon.node_offset = static_cast<uint32_t>(builder.TendonNodes().size());
        tendon.node_count = 3;
        tendon.stiffness = 4000.0F;
        tendon.damping = 200.0F;
        tendon.limit_stiffness = 0.0F;
        tendon.offset = 0.0F;
        tendon.rest_length = 0.0F;
        builder.Tendons().push_back(tendon);

        // The root tendon joint anchors the tendon on the base link; it carries
        // no axis of its own because the base link has no inbound joint.
        openusd_physx_tendon_node_desc root{};
        root.id = builder.AddIdentity("/Coupling/Fixed/Tendon/Root", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
        root.parent_index = 0;
        root.link_index = 0;
        root.axis = OPENUSD_PHYSX_JOINT_AXIS_TWIST;
        root.coefficient = 1.0F;
        root.recip_coefficient = 1.0F;
        builder.TendonNodes().push_back(root);

        for (uint32_t index = 1; index < 3; ++index)
        {
            openusd_physx_tendon_node_desc node{};
            node.id = builder
                          .AddIdentity(
                              "/Coupling/Fixed/Tendon/Joint" + std::to_string(index),
                              OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM,
                              0)
                          .id;
            node.parent_index = index;
            node.link_index = index;
            node.axis = OPENUSD_PHYSX_JOINT_AXIS_TWIST;
            node.coefficient = 1.0F;
            node.recip_coefficient = 1.0F;
            builder.TendonNodes().push_back(node);
        }
    }

    // A spatial tendon between the base link and the tip of the third chain. A
    // stiff attachment pair with a short rest length hauls the tip back up.
    {
        openusd_physx_tendon_desc tendon{};
        tendon.id = builder.AddIdentity("/Coupling/Spatial/Tendon", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
        tendon.articulation_index = scene.spatial_index;
        tendon.type = OPENUSD_PHYSX_TENDON_SPATIAL;
        tendon.node_offset = static_cast<uint32_t>(builder.TendonNodes().size());
        tendon.node_count = 2;
        tendon.stiffness = 4000.0F;
        tendon.damping = 200.0F;
        tendon.limit_stiffness = 0.0F;
        tendon.offset = 0.0F;
        builder.Tendons().push_back(tendon);

        openusd_physx_tendon_node_desc anchor{};
        anchor.id = builder.AddIdentity("/Coupling/Spatial/Tendon/Anchor", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
        anchor.parent_index = 0;
        anchor.link_index = 0;
        anchor.axis = 0;
        anchor.coefficient = 1.0F;
        anchor.relative_offset = openusd_physx_vec3f{0.0F, 1.0F, 0.0F};
        builder.TendonNodes().push_back(anchor);

        openusd_physx_tendon_node_desc leaf{};
        leaf.id = builder.AddIdentity("/Coupling/Spatial/Tendon/Leaf", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
        leaf.parent_index = 1;
        leaf.link_index = 2;
        leaf.axis = 0;
        leaf.coefficient = 1.0F;
        leaf.relative_offset = openusd_physx_vec3f{0.0F, 0.0F, 0.0F};
        leaf.rest_length = 0.5F;
        builder.TendonNodes().push_back(leaf);
    }

    // A mimic joint on the fourth chain. PhysX enforces qA + gearRatio*qB +
    // offset = 0, so a ratio of one makes the second joint rotate exactly
    // opposite to the first and the tip folds back instead of trailing away.
    {
        openusd_physx_mimic_joint_desc mimic{};
        mimic.id = builder.AddIdentity("/Coupling/Mimic/Gear", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
        mimic.articulation_index = scene.mimic_index;
        mimic.link_a = 2;
        mimic.axis_a = OPENUSD_PHYSX_JOINT_AXIS_TWIST;
        mimic.link_b = 1;
        mimic.axis_b = OPENUSD_PHYSX_JOINT_AXIS_TWIST;
        mimic.gear_ratio = 1.0F;
        mimic.offset = 0.0F;
        builder.MimicJoints().push_back(mimic);
    }

    PushCapacities(builder, 24, 256);
    scene.page = builder.Build();
    scene.page_size = builder.Size();
    return scene;
}

// The rotation angle about the world Z axis, which is the swing every joint of
// these chains is free in.
float SwingAngle(const openusd_physx_quatf& rotation) noexcept
{
    return 2.0F * std::atan2(rotation.z, rotation.w);
}

// ---------------------------------------------------------------------------
// A chain whose fixed tendon drives an axis its joints keep locked. The world
// build accepts the page as far as the articulation, its links, and their
// shapes, and only then refuses the tendon, which is exactly the window in
// which a partially built articulation can be leaked.
// ---------------------------------------------------------------------------
std::vector<uint64_t> MakeLockedTendonPage(uint32_t link_count, size_t& page_size)
{
    openusd_physx_test::PageBuilder builder;
    builder.Scenes().push_back(MakeScene(builder, "/Leak/PhysicsScene"));
    builder.Materials().push_back(MakeMaterial(builder, "/Leak/Material"));

    openusd_physx_shape_desc box{};
    box.id = builder.AddIdentity("/Leak/LinkShape", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    box.type = OPENUSD_PHYSX_SHAPE_BOX;
    box.local_pose = openusd_physx_test::Pose(0.0F, 0.0F, 0.0F);
    box.scale = openusd_physx_vec3f{1.0F, 1.0F, 1.0F};
    box.half_extents = openusd_physx_vec3f{0.45F, 0.1F, 0.1F};
    box.material_index = 0;
    builder.Shapes().push_back(box);

    uint64_t middle_id = 0;
    uint64_t tip_id = 0;
    const uint32_t articulation_index =
        AppendChain(builder, "/Leak/Chain", 0.0F, middle_id, tip_id, link_count);

    openusd_physx_tendon_desc tendon{};
    tendon.id = builder.AddIdentity("/Leak/Chain/Tendon", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    tendon.articulation_index = articulation_index;
    tendon.type = OPENUSD_PHYSX_TENDON_FIXED;
    tendon.node_offset = static_cast<uint32_t>(builder.TendonNodes().size());
    tendon.node_count = 2;
    tendon.stiffness = 1000.0F;
    tendon.damping = 50.0F;
    builder.Tendons().push_back(tendon);

    openusd_physx_tendon_node_desc root{};
    root.id = builder.AddIdentity("/Leak/Chain/Tendon/Root", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    root.parent_index = 0;
    root.link_index = 0;
    root.axis = OPENUSD_PHYSX_JOINT_AXIS_TWIST;
    root.coefficient = 1.0F;
    root.recip_coefficient = 1.0F;
    builder.TendonNodes().push_back(root);

    // Every joint of the chain is free in twist only, so this node names an axis
    // the simulation SDK refuses to attach a tendon joint to.
    openusd_physx_tendon_node_desc locked{};
    locked.id = builder.AddIdentity("/Leak/Chain/Tendon/Locked", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    locked.parent_index = 1;
    locked.link_index = 1;
    locked.axis = OPENUSD_PHYSX_JOINT_AXIS_SWING1;
    locked.coefficient = 1.0F;
    locked.recip_coefficient = 1.0F;
    builder.TendonNodes().push_back(locked);

    PushCapacities(builder, link_count + 4, 64);
    std::vector<uint64_t> page = builder.Build();
    page_size = builder.Size();
    return page;
}

// Resident memory of this process, used only as leak evidence. A platform that
// does not report it makes the growth check report itself as unavailable rather
// than pass silently.
bool TryResidentBytes(size_t& bytes) noexcept
{
#if defined(_WIN32)
    PROCESS_MEMORY_COUNTERS counters{};
    counters.cb = static_cast<DWORD>(sizeof(counters));
    if (GetProcessMemoryInfo(GetCurrentProcess(), &counters, static_cast<DWORD>(sizeof(counters))) == 0)
    {
        return false;
    }
    bytes = static_cast<size_t>(counters.WorkingSetSize);
    return true;
#else
    std::FILE* file = std::fopen("/proc/self/statm", "r");
    if (file == nullptr)
    {
        return false;
    }
    unsigned long long total = 0;
    unsigned long long resident = 0;
    const int read = std::fscanf(file, "%llu %llu", &total, &resident);
    std::fclose(file);
    if (read != 2)
    {
        return false;
    }
    bytes = static_cast<size_t>(resident) * static_cast<size_t>(4096);
    return true;
#endif
}

// Builds the refused page again and again on the same world. Every attempt must
// fail the same way, the world must stay usable afterwards, and the memory the
// process holds must not grow with the attempt count, which is what proves the
// partially built articulation is released on the failure path.
int RunCouplingFailureInjection(openusd_physx_world* world, openusd_physx_error_buffer* error)
{
    const uint32_t link_count = 40;
    const int warmup_attempts = 20;
    const int measured_attempts = 400;
    size_t page_size = 0;
    const std::vector<uint64_t> page = MakeLockedTendonPage(link_count, page_size);

    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    const openusd_physx_status first =
        openusd_physx_world_build(world, page.data(), page_size, &validation, error);
    if (!CheckStatus(first, OPENUSD_PHYSX_STATUS_INVALID_PAGE, "a tendon on a locked axis is refused"))
    {
        std::cerr << "  reported: " << (error->data != nullptr ? error->data : "") << '\n';
        return 1;
    }
    const std::string first_reason = error->data != nullptr ? error->data : "";
    if (!Check(
            first_reason.find("Fixed tendon") != std::string::npos,
            "the refusal names the tendon that cannot be built"))
    {
        std::cerr << "  reported: " << first_reason << '\n';
        return 1;
    }

    for (int attempt = 0; attempt < warmup_attempts; ++attempt)
    {
        if (openusd_physx_world_build(world, page.data(), page_size, &validation, error) !=
            OPENUSD_PHYSX_STATUS_INVALID_PAGE)
        {
            Check(false, "every warmup attempt is refused the same way");
            return 1;
        }
    }

    size_t before = 0;
    const bool measurable = TryResidentBytes(before);
    for (int attempt = 0; attempt < measured_attempts; ++attempt)
    {
        const openusd_physx_status status =
            openusd_physx_world_build(world, page.data(), page_size, &validation, error);
        if (status != OPENUSD_PHYSX_STATUS_INVALID_PAGE)
        {
            Check(false, "every repeated attempt is refused the same way");
            return 1;
        }
        const std::string reason = error->data != nullptr ? error->data : "";
        if (reason != first_reason)
        {
            Check(false, "every repeated attempt reports the same reason");
            return 1;
        }
    }

    size_t after = 0;
    if (measurable && TryResidentBytes(after))
    {
        // Four hundred refused builds of a forty link chain would leak tens of
        // megabytes if the articulation were kept, so a few megabytes of
        // allocator noise cannot hide the regression.
        const size_t budget = static_cast<size_t>(8) * 1024U * 1024U;
        const size_t growth = after > before ? after - before : 0;
        if (!Check(growth < budget, "repeated refused builds do not grow the process"))
        {
            std::cerr << "  grew " << (growth / 1024U) << " KiB over " << measured_attempts << " refused builds\n";
        }
    }
    else
    {
        std::cout << "note: this platform does not report resident memory, so only the refusal is checked.\n";
    }

    // The world must still accept a good page after every one of those failures.
    const CouplingScene good = MakeCouplingScene();
    if (!CheckStatus(
            openusd_physx_world_build(world, good.page.data(), good.page_size, &validation, error),
            OPENUSD_PHYSX_STATUS_OK,
            "a good page still builds after the refused ones"))
    {
        std::cerr << "  reported: " << (error->data != nullptr ? error->data : "") << '\n';
        return 1;
    }
    openusd_physx_world_status_info status{};
    status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
    CheckStatus(
        openusd_physx_world_get_status(world, &status, error),
        OPENUSD_PHYSX_STATUS_OK,
        "the recovered world reports its status");
    Check(status.articulation_count == 4, "the recovered world holds the articulations of the good page");
    ResultStorage storage(status.capacities);
    openusd_physx_result_page results = storage.Page();
    if (!StepWorld(world, results, 4, error, "the recovered world"))
    {
        return 1;
    }
    return 0;
}

int RunCoupling(openusd_physx_world* world, openusd_physx_error_buffer* error)
{
    const CouplingScene scene = MakeCouplingScene();
    openusd_physx_page_validation validation{};
    validation.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_page_validation));
    if (!CheckStatus(
            openusd_physx_world_build(world, scene.page.data(), scene.page_size, &validation, error),
            OPENUSD_PHYSX_STATUS_OK,
            "the coupling page builds"))
    {
        std::cerr << "  reported: " << (error->data != nullptr ? error->data : "") << '\n';
        return 1;
    }

    openusd_physx_world_status_info status{};
    status.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_world_status_info));
    CheckStatus(
        openusd_physx_world_get_status(world, &status, error), OPENUSD_PHYSX_STATUS_OK, "coupling page status");
    Check(status.articulation_count == 4, "the world reports four articulations");
    Check(status.tendon_count == 2, "the world reports the two tendons the page declares");
    Check(status.mimic_joint_count == 1, "the world reports the mimic joint the page declares");

    ResultStorage storage(status.capacities);
    openusd_physx_result_page results = storage.Page();
    if (!StepWorld(world, results, 120, error, "the coupling page"))
    {
        return 1;
    }

    const openusd_physx_body_state* control = FindState(results, scene.control_tip_id);
    const openusd_physx_body_state* fixed_tip = FindState(results, scene.fixed_tendon_tip_id);
    const openusd_physx_body_state* spatial_tip = FindState(results, scene.spatial_tendon_tip_id);
    const openusd_physx_body_state* mimic_middle = FindState(results, scene.mimic_middle_id);
    const openusd_physx_body_state* mimic_tip = FindState(results, scene.mimic_tip_id);
    if (!Check(
            control != nullptr && fixed_tip != nullptr && spatial_tip != nullptr && mimic_middle != nullptr &&
                mimic_tip != nullptr,
            "every coupled chain reports a body state"))
    {
        return 1;
    }

    // The control chain is free, so its tip has swung down and inward. Every
    // comparison below is against that same free chain.
    const float control_drop = 5.0F - control->pose.position.y;
    Check(control_drop > 1.0F, "the uncoupled control chain falls freely");

    const float fixed_drop = 5.0F - fixed_tip->pose.position.y;
    Check(
        fixed_drop < control_drop * 0.5F,
        "a stiff fixed tendon holds its chain measurably higher than the uncoupled control chain");

    const float spatial_drop = 5.0F - spatial_tip->pose.position.y;
    Check(
        spatial_drop < control_drop * 0.5F,
        "a stiff spatial tendon holds its chain measurably higher than the uncoupled control chain");

    // A gear ratio of one couples the second joint to the negated angle of the
    // first, so the two links rotate in opposite senses and the tip keeps its
    // world orientation. The control chain swings both joints the same way, so
    // this test can only pass when the mimic joint reached PhysX.
    const float middle_angle = SwingAngle(mimic_middle->pose.rotation);
    const float tip_angle = SwingAngle(mimic_tip->pose.rotation);
    Check(std::fabs(middle_angle) > 0.05F, "the geared chain actually swings");
    Check(
        std::fabs(tip_angle) < std::fabs(middle_angle) * 0.5F,
        "a mimic joint geared to one cancels the tip rotation of its chain");

    // Resetting must restore the coupled chains as exactly as the free one.
    openusd_physx_reset_desc reset{};
    reset.struct_size = static_cast<uint32_t>(sizeof(openusd_physx_reset_desc));
    CheckStatus(openusd_physx_world_reset(world, &reset, error), OPENUSD_PHYSX_STATUS_OK, "the coupled world resets");
    if (!StepWorld(world, results, 1, error, "the reset coupling page"))
    {
        return 1;
    }
    const openusd_physx_body_state* reset_tip = FindState(results, scene.fixed_tendon_tip_id);
    Check(
        reset_tip != nullptr && reset_tip->pose.position.y > 4.9F,
        "a reset returns a tendon coupled chain to its authored pose");
    return 0;
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
    Check(
        (capabilities.flags & OPENUSD_PHYSX_CAPABILITY_ARTICULATION_TENDONS) != 0,
        "the library reports articulation tendons");
    Check(
        (capabilities.flags & OPENUSD_PHYSX_CAPABILITY_ARTICULATION_MIMIC_JOINTS) != 0,
        "the library reports articulation mimic joints");

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

    const int fatal = RunCoupling(world, &error);
    const int injection = fatal == 0 ? RunCouplingFailureInjection(world, &error) : 0;
    openusd_physx_world_release(world);

    if (fatal != 0 || injection != 0 || g_failures != 0)
    {
        std::cerr << g_failures << " articulation coupling check(s) failed.\n";
        return 1;
    }
    std::cout << "articulation coupling probe passed.\n";
    return 0;
}
