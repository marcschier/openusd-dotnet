// Copyright (c) marcschier. Licensed under the MIT License.

// Test-only builder for pointer free physics build pages. The builder is shared
// by the contract probe, which runs without PhysX, and the retained world
// probe, which requires the simulation SDK.

#ifndef OPENUSD_PHYSX_PAGE_BUILDER_H
#define OPENUSD_PHYSX_PAGE_BUILDER_H

#include "openusd_physx_world.h"

#include <cstring>
#include <stdexcept>
#include <string>
#include <vector>

namespace openusd_physx_test
{
inline openusd_physx_quatf Identity() noexcept
{
    return openusd_physx_quatf{0.0F, 0.0F, 0.0F, 1.0F};
}

inline openusd_physx_transform Pose(float x, float y, float z) noexcept
{
    openusd_physx_transform pose{};
    pose.position = openusd_physx_vec3f{x, y, z};
    pose.rotation = Identity();
    return pose;
}

// Accumulates sections into one contiguous, eight byte aligned page.
class PageBuilder
{
public:
    PageBuilder()
    {
        bytes_.resize(sizeof(openusd_physx_build_page_header), 0);
        header_.magic = OPENUSD_PHYSX_PAGE_MAGIC;
        header_.abi_version = OPENUSD_PHYSX_WORLD_ABI_VERSION;
        header_.header_size = static_cast<uint32_t>(sizeof(openusd_physx_build_page_header));
        header_.revision = 1;
        header_.source_hash = 0x0123456789ABCDEFULL;
        header_.meters_per_unit = 1.0;
        header_.kilograms_per_unit = 1.0;
        header_.time_codes_per_second = 24.0;
        header_.start_time_code = 0.0;
        header_.end_time_code = 48.0;
        header_.up_axis = OPENUSD_PHYSX_UP_AXIS_Y;
        header_.simulation_rate_hz = 60;
        header_.max_substeps = 4;
    }

    openusd_physx_build_page_header& Header() noexcept
    {
        return header_;
    }

    // Adds a path to the string table and returns the identity it implies.
    openusd_physx_identity AddIdentity(const std::string& path, uint32_t domain, uint32_t instance_index)
    {
        openusd_physx_identity identity{};
        identity.path_offset = static_cast<uint32_t>(strings_.size());
        identity.path_length = static_cast<uint32_t>(path.size());
        identity.instance_domain = domain;
        identity.instance_index = instance_index;
        identity.id = ComputeIdentity(path, domain, instance_index);
        strings_.insert(strings_.end(), path.begin(), path.end());
        identities_.push_back(identity);
        return identity;
    }

    std::vector<openusd_physx_scene_desc>& Scenes() noexcept { return scenes_; }
    std::vector<openusd_physx_material_desc>& Materials() noexcept { return materials_; }
    std::vector<openusd_physx_shape_desc>& Shapes() noexcept { return shapes_; }
    std::vector<openusd_physx_actor_desc>& Actors() noexcept { return actors_; }
    std::vector<openusd_physx_actor_shape_ref>& ActorShapes() noexcept { return actor_shapes_; }
    std::vector<openusd_physx_joint_desc>& Joints() noexcept { return joints_; }
    std::vector<openusd_physx_filter_pair>& FilterPairs() noexcept { return filter_pairs_; }
    std::vector<openusd_physx_vec3f>& MeshPoints() noexcept { return mesh_points_; }
    std::vector<uint32_t>& MeshIndices() noexcept { return mesh_indices_; }
    std::vector<openusd_physx_heightfield_sample>& HeightfieldSamples() noexcept { return heightfield_samples_; }
    std::vector<openusd_physx_articulation_desc>& Articulations() noexcept { return articulations_; }
    std::vector<openusd_physx_articulation_link_desc>& ArticulationLinks() noexcept { return articulation_links_; }
    std::vector<openusd_physx_controller_desc>& Controllers() noexcept { return controllers_; }
    std::vector<openusd_physx_tendon_desc>& Tendons() noexcept { return tendons_; }
    std::vector<openusd_physx_tendon_node_desc>& TendonNodes() noexcept { return tendon_nodes_; }
    std::vector<openusd_physx_mimic_joint_desc>& MimicJoints() noexcept { return mimic_joints_; }
    std::vector<openusd_physx_vehicle_desc>& Vehicles() noexcept { return vehicles_; }
    std::vector<openusd_physx_vehicle_wheel_desc>& VehicleWheels() noexcept { return vehicle_wheels_; }
    std::vector<openusd_physx_particle_material_desc>& ParticleMaterials() noexcept { return particle_materials_; }
    std::vector<openusd_physx_particle_system_desc>& ParticleSystems() noexcept { return particle_systems_; }
    std::vector<openusd_physx_particle_body_desc>& ParticleBodies() noexcept { return particle_bodies_; }
    std::vector<openusd_physx_deformable_material_desc>& DeformableMaterials() noexcept
    {
        return deformable_materials_;
    }
    std::vector<openusd_physx_deformable_desc>& Deformables() noexcept { return deformables_; }

    // Serializes every section and returns the finished page bytes. The buffer
    // is aligned to eight bytes because it is backed by uint64 storage.
    std::vector<uint64_t> Build()
    {
        bytes_.resize(sizeof(openusd_physx_build_page_header), 0);
        header_.string_bytes = AppendBytes(strings_.data(), strings_.size(), 1);
        header_.identities = Append(identities_);
        header_.scenes = Append(scenes_);
        header_.materials = Append(materials_);
        header_.shapes = Append(shapes_);
        header_.actors = Append(actors_);
        header_.actor_shapes = Append(actor_shapes_);
        header_.joints = Append(joints_);
        header_.filter_pairs = Append(filter_pairs_);
        header_.mesh_points = Append(mesh_points_);
        header_.mesh_indices = Append(mesh_indices_);
        header_.heightfield_samples = Append(heightfield_samples_);
        header_.articulations = Append(articulations_);
        header_.articulation_links = Append(articulation_links_);
        header_.controllers = Append(controllers_);
        header_.articulation_tendons = Append(tendons_);
        header_.articulation_tendon_nodes = Append(tendon_nodes_);
        header_.articulation_mimic_joints = Append(mimic_joints_);
        header_.vehicles = Append(vehicles_);
        header_.vehicle_wheels = Append(vehicle_wheels_);
        header_.particle_materials = Append(particle_materials_);
        header_.particle_systems = Append(particle_systems_);
        header_.particle_bodies = Append(particle_bodies_);
        header_.deformable_materials = Append(deformable_materials_);
        header_.deformables = Append(deformables_);
        header_.byte_size = static_cast<uint64_t>(bytes_.size());
        std::memcpy(bytes_.data(), &header_, sizeof(header_));

        std::vector<uint64_t> storage((bytes_.size() + 7) / 8, 0);
        std::memcpy(storage.data(), bytes_.data(), bytes_.size());
        size_ = bytes_.size();
        return storage;
    }

    size_t Size() const noexcept
    {
        return size_;
    }

    static uint64_t ComputeIdentity(const std::string& path, uint32_t domain, uint32_t instance_index)
    {
        uint64_t id = 0;
        openusd_physx_error_buffer error{nullptr, 0, 0};
        if (openusd_physx_identity_compute(path.c_str(), path.size(), domain, instance_index, &id, &error) !=
            OPENUSD_PHYSX_STATUS_OK)
        {
            throw std::runtime_error("openusd_physx_identity_compute rejected a test path: " + path);
        }
        return id;
    }

private:
    openusd_physx_page_span AppendBytes(const void* data, size_t size, size_t element_size)
    {
        openusd_physx_page_span span{};
        if (size == 0)
        {
            return span;
        }
        while ((bytes_.size() % OPENUSD_PHYSX_PAGE_ALIGNMENT) != 0)
        {
            bytes_.push_back(0);
        }
        span.offset = static_cast<uint32_t>(bytes_.size());
        span.count = static_cast<uint32_t>(size / element_size);
        const unsigned char* source = static_cast<const unsigned char*>(data);
        bytes_.insert(bytes_.end(), source, source + size);
        return span;
    }

    template <typename TRecord>
    openusd_physx_page_span Append(const std::vector<TRecord>& records)
    {
        return AppendBytes(records.data(), records.size() * sizeof(TRecord), sizeof(TRecord));
    }

    openusd_physx_build_page_header header_{};
    std::vector<unsigned char> bytes_;
    std::vector<char> strings_;
    std::vector<openusd_physx_identity> identities_;
    std::vector<openusd_physx_scene_desc> scenes_;
    std::vector<openusd_physx_material_desc> materials_;
    std::vector<openusd_physx_shape_desc> shapes_;
    std::vector<openusd_physx_actor_desc> actors_;
    std::vector<openusd_physx_actor_shape_ref> actor_shapes_;
    std::vector<openusd_physx_joint_desc> joints_;
    std::vector<openusd_physx_filter_pair> filter_pairs_;
    std::vector<openusd_physx_vec3f> mesh_points_;
    std::vector<uint32_t> mesh_indices_;
    std::vector<openusd_physx_heightfield_sample> heightfield_samples_;
    std::vector<openusd_physx_articulation_desc> articulations_;
    std::vector<openusd_physx_articulation_link_desc> articulation_links_;
    std::vector<openusd_physx_controller_desc> controllers_;
    std::vector<openusd_physx_tendon_desc> tendons_;
    std::vector<openusd_physx_tendon_node_desc> tendon_nodes_;
    std::vector<openusd_physx_mimic_joint_desc> mimic_joints_;
    std::vector<openusd_physx_vehicle_desc> vehicles_;
    std::vector<openusd_physx_vehicle_wheel_desc> vehicle_wheels_;
    std::vector<openusd_physx_particle_material_desc> particle_materials_;
    std::vector<openusd_physx_particle_system_desc> particle_systems_;
    std::vector<openusd_physx_particle_body_desc> particle_bodies_;
    std::vector<openusd_physx_deformable_material_desc> deformable_materials_;
    std::vector<openusd_physx_deformable_desc> deformables_;
    size_t size_ = 0;
};

// Builds a page with one scene, two materials, a static ground box, a static
// triangle mesh, two dynamic bodies, one joint, and one suppressed pair.
inline PageBuilder MakeReferenceScene()
{
    PageBuilder builder;

    openusd_physx_scene_desc scene{};
    scene.id = builder.AddIdentity("/World/PhysicsScene", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    scene.gravity_direction = openusd_physx_vec3f{0.0F, -1.0F, 0.0F};
    scene.gravity_magnitude = 9.81F;
    scene.position_iterations = 4;
    scene.velocity_iterations = 1;
    scene.bounce_threshold = 0.2F;
    scene.contact_offset = 0.02F;
    builder.Scenes().push_back(scene);

    openusd_physx_material_desc ground_material{};
    ground_material.id = builder.AddIdentity("/World/Materials/Ground", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    ground_material.static_friction = 0.8F;
    ground_material.dynamic_friction = 0.7F;
    ground_material.restitution = 0.05F;
    ground_material.density = 1000.0F;
    builder.Materials().push_back(ground_material);

    openusd_physx_material_desc body_material{};
    body_material.id = builder.AddIdentity("/World/Materials/Body", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    body_material.static_friction = 0.5F;
    body_material.dynamic_friction = 0.5F;
    body_material.restitution = 0.1F;
    body_material.density = 500.0F;
    builder.Materials().push_back(body_material);

    const openusd_physx_vec3f unit_scale{1.0F, 1.0F, 1.0F};

    openusd_physx_shape_desc ground_shape{};
    ground_shape.id = builder.AddIdentity("/World/Ground", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    ground_shape.type = OPENUSD_PHYSX_SHAPE_BOX;
    ground_shape.local_pose = Pose(0.0F, 0.0F, 0.0F);
    ground_shape.scale = unit_scale;
    ground_shape.half_extents = openusd_physx_vec3f{20.0F, 0.5F, 20.0F};
    ground_shape.material_index = 0;
    builder.Shapes().push_back(ground_shape);

    openusd_physx_shape_desc box_shape{};
    box_shape.id = builder.AddIdentity("/World/Box", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    box_shape.type = OPENUSD_PHYSX_SHAPE_BOX;
    box_shape.local_pose = Pose(0.0F, 0.0F, 0.0F);
    box_shape.scale = unit_scale;
    box_shape.half_extents = openusd_physx_vec3f{0.5F, 0.5F, 0.5F};
    box_shape.material_index = 1;
    builder.Shapes().push_back(box_shape);

    openusd_physx_shape_desc sphere_shape{};
    sphere_shape.id = builder.AddIdentity("/World/Sphere", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    sphere_shape.type = OPENUSD_PHYSX_SHAPE_SPHERE;
    sphere_shape.local_pose = Pose(0.0F, 0.0F, 0.0F);
    sphere_shape.scale = unit_scale;
    sphere_shape.radius = 0.5F;
    sphere_shape.material_index = 1;
    builder.Shapes().push_back(sphere_shape);

    builder.MeshPoints().push_back(openusd_physx_vec3f{-5.0F, 1.0F, -5.0F});
    builder.MeshPoints().push_back(openusd_physx_vec3f{5.0F, 1.0F, -5.0F});
    builder.MeshPoints().push_back(openusd_physx_vec3f{5.0F, 1.0F, 5.0F});
    builder.MeshPoints().push_back(openusd_physx_vec3f{-5.0F, 1.0F, 5.0F});
    builder.MeshIndices().push_back(0);
    builder.MeshIndices().push_back(1);
    builder.MeshIndices().push_back(2);
    builder.MeshIndices().push_back(0);
    builder.MeshIndices().push_back(2);
    builder.MeshIndices().push_back(3);

    openusd_physx_shape_desc mesh_shape{};
    mesh_shape.id = builder.AddIdentity("/World/Ramp", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    mesh_shape.type = OPENUSD_PHYSX_SHAPE_TRIANGLE_MESH;
    mesh_shape.local_pose = Pose(0.0F, 0.0F, 0.0F);
    mesh_shape.scale = unit_scale;
    mesh_shape.point_count = 4;
    mesh_shape.index_count = 6;
    mesh_shape.material_index = 0;
    builder.Shapes().push_back(mesh_shape);

    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{0, -1});
    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{1, -1});
    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{2, -1});
    builder.ActorShapes().push_back(openusd_physx_actor_shape_ref{3, -1});

    openusd_physx_actor_desc ground{};
    ground.id = builder.AddIdentity("/World/GroundBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    ground.scene_index = 0;
    ground.type = OPENUSD_PHYSX_ACTOR_STATIC;
    ground.world_pose = Pose(0.0F, -0.5F, 0.0F);
    ground.shape_offset = 0;
    ground.shape_count = 1;
    ground.collision_group = 0;
    builder.Actors().push_back(ground);

    openusd_physx_actor_desc box{};
    box.id = builder.AddIdentity("/World/BoxBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    box.scene_index = 0;
    box.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
    box.world_pose = Pose(0.0F, 4.0F, 0.0F);
    box.mass = 2.0F;
    box.shape_offset = 1;
    box.shape_count = 1;
    box.collision_group = 1;
    builder.Actors().push_back(box);

    openusd_physx_actor_desc sphere{};
    sphere.id = builder.AddIdentity("/World/SphereBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    sphere.scene_index = 0;
    sphere.type = OPENUSD_PHYSX_ACTOR_DYNAMIC;
    sphere.world_pose = Pose(2.0F, 6.0F, 0.0F);
    sphere.linear_velocity = openusd_physx_vec3f{0.0F, 0.0F, 1.0F};
    sphere.shape_offset = 2;
    sphere.shape_count = 1;
    sphere.collision_group = 2;
    builder.Actors().push_back(sphere);

    openusd_physx_actor_desc ramp{};
    ramp.id = builder.AddIdentity("/World/RampBody", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    ramp.scene_index = 0;
    ramp.type = OPENUSD_PHYSX_ACTOR_STATIC;
    ramp.world_pose = Pose(0.0F, 0.0F, 0.0F);
    ramp.shape_offset = 3;
    ramp.shape_count = 1;
    ramp.collision_group = 0;
    builder.Actors().push_back(ramp);

    openusd_physx_joint_desc joint{};
    joint.id = builder.AddIdentity("/World/Joint", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    joint.type = OPENUSD_PHYSX_JOINT_REVOLUTE;
    joint.flags = OPENUSD_PHYSX_JOINT_FLAG_LIMIT_ENABLED;
    joint.actor0_index = -1;
    joint.actor1_index = 1;
    joint.local_frame0 = Pose(0.0F, 4.0F, 0.0F);
    joint.local_frame1 = Pose(0.0F, 0.0F, 0.0F);
    joint.axis = OPENUSD_PHYSX_AXIS_Z;
    joint.lower_limit = -1.0F;
    joint.upper_limit = 1.0F;
    joint.max_distance = 0.0F;
    joint.drive_max_force = 1000.0F;
    joint.break_force = 100000.0F;
    joint.break_torque = 100000.0F;
    builder.Joints().push_back(joint);

    builder.FilterPairs().push_back(openusd_physx_filter_pair{1, 2});

    builder.Header().capacities.max_body_states = 8;
    builder.Header().capacities.max_events = 64;
    builder.Header().capacities.max_diagnostics = 32;
    // Collision shape visualization for this scene emits a few hundred lines,
    // so the declared capacity has room for a whole frame.
    builder.Header().capacities.max_debug_lines = 1024;
    builder.Header().capacities.max_query_hits = 64;
    return builder;
}

// Extends a page with the CUDA accelerated domains: one position based dynamics
// particle system carrying a solid particle body and a fluid particle body, one
// surface deformable built from a triangulated patch, and one finite element
// volume deformable built from a tetrahedralized box. Every one of them is
// declared exactly the way an extracted stage would declare it, so the page
// contract is exercised whether or not a device is reachable.
inline void AddGpuDomains(PageBuilder& builder)
{
    const uint32_t first_point = static_cast<uint32_t>(builder.MeshPoints().size());
    const uint32_t first_index = static_cast<uint32_t>(builder.MeshIndices().size());

    // 2 x 2 x 2 solid particles and 2 x 2 x 2 fluid particles.
    for (uint32_t stack = 0; stack < 2; ++stack)
    {
        for (uint32_t row = 0; row < 2; ++row)
        {
            for (uint32_t column = 0; column < 2; ++column)
            {
                builder.MeshPoints().push_back(openusd_physx_vec3f{
                    0.1F * static_cast<float>(column),
                    0.1F * static_cast<float>(stack),
                    0.1F * static_cast<float>(row)});
            }
        }
    }
    const uint32_t solid_points = 8;
    const uint32_t fluid_point_offset = first_point + solid_points;
    for (uint32_t stack = 0; stack < 2; ++stack)
    {
        for (uint32_t row = 0; row < 2; ++row)
        {
            for (uint32_t column = 0; column < 2; ++column)
            {
                builder.MeshPoints().push_back(openusd_physx_vec3f{
                    0.1F * static_cast<float>(column),
                    0.1F * static_cast<float>(stack),
                    0.1F * static_cast<float>(row)});
            }
        }
    }

    // A three by three vertex patch, eight triangles.
    const uint32_t surface_point_offset = fluid_point_offset + solid_points;
    for (uint32_t row = 0; row < 3; ++row)
    {
        for (uint32_t column = 0; column < 3; ++column)
        {
            builder.MeshPoints().push_back(openusd_physx_vec3f{
                0.5F * static_cast<float>(column), 0.0F, 0.5F * static_cast<float>(row)});
        }
    }
    for (uint32_t row = 0; row < 2; ++row)
    {
        for (uint32_t column = 0; column < 2; ++column)
        {
            const uint32_t base = (row * 3) + column;
            builder.MeshIndices().push_back(base);
            builder.MeshIndices().push_back(base + 1);
            builder.MeshIndices().push_back(base + 3);
            builder.MeshIndices().push_back(base + 1);
            builder.MeshIndices().push_back(base + 4);
            builder.MeshIndices().push_back(base + 3);
        }
    }
    const uint32_t surface_index_count = 24;

    // A unit cube split into five tetrahedra.
    const uint32_t volume_point_offset = surface_point_offset + 9;
    const uint32_t volume_index_offset = first_index + surface_index_count;
    const float corners[8][3] = {
        {0.0F, 0.0F, 0.0F},
        {1.0F, 0.0F, 0.0F},
        {1.0F, 0.0F, 1.0F},
        {0.0F, 0.0F, 1.0F},
        {0.0F, 1.0F, 0.0F},
        {1.0F, 1.0F, 0.0F},
        {1.0F, 1.0F, 1.0F},
        {0.0F, 1.0F, 1.0F}};
    for (const auto& corner : corners)
    {
        builder.MeshPoints().push_back(openusd_physx_vec3f{corner[0], corner[1], corner[2]});
    }
    const uint32_t tetrahedra[5][4] = {
        {0, 1, 3, 4}, {1, 2, 3, 6}, {1, 3, 4, 6}, {1, 4, 5, 6}, {3, 4, 6, 7}};
    for (const auto& tetrahedron : tetrahedra)
    {
        for (const uint32_t corner : tetrahedron)
        {
            builder.MeshIndices().push_back(corner);
        }
    }

    openusd_physx_particle_material_desc particle_material{};
    particle_material.id =
        builder.AddIdentity("/World/Materials/Water", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    particle_material.friction = 0.2F;
    particle_material.damping = 0.0F;
    particle_material.viscosity = 0.01F;
    particle_material.surface_tension = 0.006F;
    particle_material.cohesion = 0.01F;
    particle_material.gravity_scale = 1.0F;
    particle_material.density = 1000.0F;
    particle_material.cfl_coefficient = 1.0F;
    builder.ParticleMaterials().push_back(particle_material);

    openusd_physx_particle_system_desc system{};
    system.id = builder.AddIdentity("/World/ParticleSystem", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    system.scene_index = 0;
    system.flags = OPENUSD_PHYSX_PARTICLE_SYSTEM_FLAG_GLOBAL_SELF_COLLISION |
        OPENUSD_PHYSX_PARTICLE_SYSTEM_FLAG_NON_PARTICLE_COLLISION;
    system.particle_contact_offset = 0.06F;
    system.contact_offset = 0.06F;
    system.rest_offset = 0.05F;
    system.solid_rest_offset = 0.05F;
    system.fluid_rest_offset = 0.045F;
    system.neighborhood_scale = 1.01F;
    system.max_neighborhood = 96;
    system.solver_position_iterations = 4;
    system.body_offset = 0;
    system.body_count = 2;
    builder.ParticleSystems().push_back(system);

    openusd_physx_particle_body_desc solid{};
    solid.id = builder.AddIdentity("/World/Granules", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    solid.kind = OPENUSD_PHYSX_PARTICLE_BODY_SET;
    solid.flags = OPENUSD_PHYSX_PARTICLE_BODY_FLAG_SELF_COLLISION;
    solid.particle_group = 0;
    solid.material_index = 0;
    solid.mass = 0.0F;
    solid.point_offset = first_point;
    solid.point_count = solid_points;
    solid.world_pose = Pose(0.0F, 3.0F, 0.0F);
    builder.ParticleBodies().push_back(solid);

    openusd_physx_particle_body_desc fluid{};
    fluid.id = builder.AddIdentity("/World/Fluid", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    fluid.kind = OPENUSD_PHYSX_PARTICLE_BODY_SET;
    fluid.flags = OPENUSD_PHYSX_PARTICLE_BODY_FLAG_FLUID | OPENUSD_PHYSX_PARTICLE_BODY_FLAG_SELF_COLLISION;
    fluid.particle_group = 1;
    fluid.material_index = 0;
    fluid.mass = 0.0F;
    fluid.point_offset = fluid_point_offset;
    fluid.point_count = solid_points;
    fluid.world_pose = Pose(1.0F, 3.0F, 0.0F);
    builder.ParticleBodies().push_back(fluid);

    openusd_physx_deformable_material_desc surface_material{};
    surface_material.id =
        builder.AddIdentity("/World/Materials/Cloth", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    surface_material.kind = OPENUSD_PHYSX_DEFORMABLE_SURFACE;
    surface_material.youngs_modulus = 500000.0F;
    surface_material.poissons_ratio = 0.45F;
    surface_material.dynamic_friction = 0.25F;
    surface_material.density = 1000.0F;
    surface_material.thickness = 0.001F;
    builder.DeformableMaterials().push_back(surface_material);

    openusd_physx_deformable_material_desc volume_material{};
    volume_material.id =
        builder.AddIdentity("/World/Materials/Jelly", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    volume_material.kind = OPENUSD_PHYSX_DEFORMABLE_VOLUME;
    volume_material.youngs_modulus = 50000.0F;
    volume_material.poissons_ratio = 0.45F;
    volume_material.dynamic_friction = 0.25F;
    volume_material.density = 1000.0F;
    builder.DeformableMaterials().push_back(volume_material);

    openusd_physx_deformable_desc surface{};
    surface.id = builder.AddIdentity("/World/Cloth", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    surface.scene_index = 0;
    surface.kind = OPENUSD_PHYSX_DEFORMABLE_SURFACE;
    surface.flags = OPENUSD_PHYSX_DEFORMABLE_FLAG_NONE;
    surface.material_index = 0;
    surface.solver_position_iterations = 16;
    surface.vertex_velocity_damping = 0.005F;
    surface.self_collision_filter_distance = 0.1F;
    surface.point_offset = surface_point_offset;
    surface.point_count = 9;
    surface.index_offset = first_index;
    surface.index_count = surface_index_count;
    surface.world_pose = Pose(-3.0F, 4.0F, 0.0F);
    builder.Deformables().push_back(surface);

    openusd_physx_deformable_desc volume{};
    volume.id = builder.AddIdentity("/World/Jelly", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    volume.scene_index = 0;
    volume.kind = OPENUSD_PHYSX_DEFORMABLE_VOLUME;
    volume.flags = OPENUSD_PHYSX_DEFORMABLE_FLAG_NONE;
    volume.material_index = 1;
    volume.solver_position_iterations = 16;
    volume.vertex_velocity_damping = 0.005F;
    volume.self_collision_filter_distance = 0.1F;
    volume.settling_threshold = 0.1F;
    volume.sleep_threshold = 0.05F;
    volume.point_offset = volume_point_offset;
    volume.point_count = 8;
    volume.index_offset = volume_index_offset;
    volume.index_count = 20;
    volume.world_pose = Pose(3.0F, 4.0F, 0.0F);
    builder.Deformables().push_back(volume);

    // A second volume that only differs by the authored gravity opt out, so the
    // probe can prove the flag is applied rather than merely validated: it
    // shares the same topology, material, and iteration counts as the volume
    // above and is placed beside it.
    openusd_physx_deformable_desc floater = volume;
    floater.id = builder.AddIdentity("/World/Floater", OPENUSD_PHYSX_INSTANCE_DOMAIN_PRIM, 0).id;
    floater.flags = OPENUSD_PHYSX_DEFORMABLE_FLAG_DISABLE_GRAVITY;
    floater.world_pose = Pose(6.0F, 4.0F, 0.0F);
    builder.Deformables().push_back(floater);

    builder.Header().capacities.max_deformation_bodies = 8;
    builder.Header().capacities.max_deformation_points = 256;
}

// Builds the reference scene plus every CUDA accelerated domain.
inline PageBuilder MakeGpuDomainScene()
{
    PageBuilder builder = MakeReferenceScene();
    AddGpuDomains(builder);
    return builder;
}
}

#endif
